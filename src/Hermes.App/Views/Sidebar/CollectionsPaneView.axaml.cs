using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Hermes.App.ViewModels.Sidebar;

namespace Hermes.App.Views.Sidebar;

public partial class CollectionsPaneView : UserControl
{
    private static readonly DataFormat<string> NodeFormat = DataFormat.CreateStringApplicationFormat("hermes-tree-node");
    private const double DragThreshold = 6;

    private TreeNodeViewModel? _pressedNode;
    private Point _pressPoint;
    private bool _dragging;
    private TreeNodeViewModel? _dragged;
    private TreeNodeViewModel? _dropTarget;

    /// <summary>DoDragDropAsync needs the event that started the gesture.</summary>
    private PointerPressedEventArgs? _lastPressArgs;

    public CollectionsPaneView()
    {
        InitializeComponent();
        Tree.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        Tree.AddHandler(DragDrop.DragLeaveEvent, (_, _) => SetDropTarget(null));
        Tree.AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private CollectionsPaneViewModel? ViewModel => DataContext as CollectionsPaneViewModel;

    // ---- click to open, drag to move ----

    private void OnRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || (sender as Control)?.DataContext is not TreeNodeViewModel node)
            return;
        if (e.Source is Visual source && source.FindAncestorOfType<TextBox>(includeSelf: true) is not null)
            return;
        _pressedNode = node;
        _lastPressArgs = e;
        _pressPoint = e.GetPosition(this);
        _dragging = false;
    }

    private async void OnRowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressedNode is null || _dragging || _pressedNode.IsCollection || _pressedNode.IsRenaming)
            return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _pressedNode = null;
            return;
        }
        var delta = e.GetPosition(this) - _pressPoint;
        if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold || _lastPressArgs is null)
            return;

        _dragging = true;
        _dragged = _pressedNode;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(NodeFormat, _dragged.Id.ToString()));
        try
        {
            await DragDrop.DoDragDropAsync(_lastPressArgs, data, DragDropEffects.Move);
        }
        finally
        {
            SetDropTarget(null);
            _dragged = null;
            _pressedNode = null;
            _lastPressArgs = null;
            _dragging = false;
        }
    }

    private void OnRowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && !_dragging && _pressedNode is { IsRenaming: false } node &&
            (sender as Control)?.DataContext == node)
        {
            // A double click keeps the request open in its own tab.
            if (_lastPressArgs?.ClickCount >= 2 && node.IsRequest)
                ViewModel?.Open(node);
            else
                ViewModel?.Activate(node);
        }
        _pressedNode = null;
        _lastPressArgs = null;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.None;
        if (_dragged is null || ViewModel is not { } vm || !e.DataTransfer.Contains(NodeFormat))
            return;
        if (TargetRow(e) is not var (row, target))
        {
            SetDropTarget(null);
            return;
        }
        var position = PositionIn(row, target, e);
        if (vm.CanDrop(_dragged, target, position))
        {
            e.DragEffects = DragDropEffects.Move;
            SetDropTarget(position == DropPosition.Inside ? target : target.Parent);
        }
        else
        {
            SetDropTarget(null);
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (_dragged is { } dragged && ViewModel is { } vm && TargetRow(e) is var (row, target))
            vm.Drop(dragged, target, PositionIn(row, target, e));
        SetDropTarget(null);
    }

    private static (Control Row, TreeNodeViewModel Node)? TargetRow(DragEventArgs e)
    {
        // Walk up to the row border defined in the item template (its child is the Grid with class "row").
        for (var v = e.Source as Visual; v is not null; v = v.GetVisualParent())
            if (v is Border { DataContext: TreeNodeViewModel node, Child: Grid grid } border && grid.Classes.Contains("row"))
                return (border, node);
        return null;
    }

    /// <summary>Upper/lower quarter = before/after; middle = inside (for folders and collections).</summary>
    private static DropPosition PositionIn(Control row, TreeNodeViewModel target, DragEventArgs e)
    {
        if (target.IsCollection)
            return DropPosition.Inside;
        var y = e.GetPosition(row).Y / Math.Max(1, row.Bounds.Height);
        if (target.CanContainChildren)
            return y < 0.25 ? DropPosition.Before : y > 0.75 ? DropPosition.After : DropPosition.Inside;
        return y < 0.5 ? DropPosition.Before : DropPosition.After;
    }

    private void SetDropTarget(TreeNodeViewModel? node)
    {
        if (_dropTarget == node)
            return;
        if (_dropTarget is not null)
            _dropTarget.IsDropTarget = false;
        _dropTarget = node;
        if (node is not null)
            node.IsDropTarget = true;
    }

    // ---- inline rename ----

    private void OnRenameBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty && sender is TextBox { IsVisible: true } box)
        {
            box.Text = (box.DataContext as TreeNodeViewModel)?.Name;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                box.Focus();
                box.SelectAll();
            });
        }
    }

    private void OnRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: TreeNodeViewModel node } box || ViewModel is not { } vm)
            return;
        if (e.Key == Key.Enter)
        {
            vm.CommitRename(node, box.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.CommitRename(node, null);
            e.Handled = true;
        }
    }

    private void OnRenameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: TreeNodeViewModel { IsRenaming: true } node } box)
            ViewModel?.CommitRename(node, box.Text);
    }

    // ---- context menu ----

    private void OnRowContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is Control { DataContext: TreeNodeViewModel node } control)
        {
            ShowMenu(control, node);
            e.Handled = true;
        }
    }

    private void OnRowMenuClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: TreeNodeViewModel node } control)
            ShowMenu(control, node);
        e.Handled = true;
    }

    private void ShowMenu(Control anchor, TreeNodeViewModel node)
    {
        if (ViewModel is not { } vm)
            return;
        vm.SelectedNode = node;

        MenuItem Item(string header, Action action, string? gesture = null)
        {
            var item = new MenuItem { Header = header };
            if (gesture is not null)
                item.InputGesture = KeyGesture.Parse(gesture);
            item.Click += (_, _) => action();
            return item;
        }

        var items = new List<Control>();
        if (node.CanContainChildren)
        {
            items.Add(Item("Add request", () => vm.AddRequestCommand.Execute(node)));
            items.Add(Item("Add folder", () => vm.AddFolderCommand.Execute(node)));
            items.Add(new Separator());
        }
        if (!node.IsRequest)
            items.Add(Item(node.IsCollection ? "Edit collection" : "Edit folder", () => vm.Open(node)));
        else
            items.Add(Item("Open in tab", () => vm.Open(node)));
        items.Add(Item("Rename", () => vm.RenameCommand.Execute(node), "F2"));
        items.Add(Item("Duplicate", () => vm.DuplicateCommand.Execute(node)));
        if (node.IsCollection)
            items.Add(Item("Export", () => vm.ExportCommand.Execute(node)));
        items.Add(new Separator());
        items.Add(Item("Delete", () => vm.DeleteCommand.Execute(node), "Delete"));

        var menu = new ContextMenu { ItemsSource = items };
        menu.Open(anchor);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (ViewModel is not { SelectedNode: { IsRenaming: false } node } vm || e.Source is TextBox)
            return;
        switch (e.Key)
        {
            case Key.F2:
                vm.RenameCommand.Execute(node);
                e.Handled = true;
                break;
            case Key.Delete:
                vm.DeleteCommand.Execute(node);
                e.Handled = true;
                break;
            case Key.Enter:
                vm.Open(node);
                e.Handled = true;
                break;
        }
    }
}
