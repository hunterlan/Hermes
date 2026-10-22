using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Hermes.App.ViewModels;

namespace Hermes.App.Views.Dialogs;

public partial class ImportDialog : Window
{
    public ImportDialog()
    {
        InitializeComponent();
        DropZone.AddHandler(DragDrop.DragOverEvent, (_, e) =>
            e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None);
        DropZone.AddHandler(DragDrop.DropEvent, OnDrop);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ImportDialogViewModel vm)
            vm.CloseRequested += result => Close(result);
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not ImportDialogViewModel vm)
            return;
        var files = e.DataTransfer.TryGetFiles() ?? [];
        vm.AddFiles(files.Select(f => f.TryGetLocalPath()).OfType<string>());
    }
}
