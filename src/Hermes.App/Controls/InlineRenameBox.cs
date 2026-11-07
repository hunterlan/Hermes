using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Hermes.App.ViewModels;

namespace Hermes.App.Controls;

/// <summary>
/// A text box for renaming in place. Its DataContext is the <see cref="IRenamable"/> being renamed; show it while
/// <see cref="IRenamable.IsRenaming"/> is true. When it appears it takes focus with the name selected. Enter and
/// losing focus keep the typed name, Escape cancels.
/// </summary>
public sealed class InlineRenameBox : TextBox
{
    static InlineRenameBox()
    {
        IsVisibleProperty.Changed.AddClassHandler<InlineRenameBox>((box, change) =>
        {
            if (change.GetNewValue<bool>())
                box.BeginEditing();
        });
    }

    protected override Type StyleKeyOverride => typeof(TextBox);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // A row realised after the rename started never sees IsVisible change, so start editing here too.
        if (IsVisible && DataContext is IRenamable { IsRenaming: true })
            BeginEditing();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (DataContext is IRenamable renamable && e.Key is Key.Enter or Key.Escape)
        {
            renamable.CommitRename(e.Key == Key.Enter ? Text : null);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        if (DataContext is IRenamable { IsRenaming: true } renamable)
            renamable.CommitRename(Text);
    }

    private void BeginEditing()
    {
        Text = (DataContext as IRenamable)?.Name;
        // Deferred: the box only accepts focus once it is laid out and visible.
        Dispatcher.UIThread.Post(() =>
        {
            Focus();
            SelectAll();
        });
    }
}
