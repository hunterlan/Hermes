using Avalonia.Controls;
using Avalonia.Threading;
using Hermes.App.ViewModels;

namespace Hermes.App.Views;

public partial class AuthEditorView : UserControl
{
    private AuthEditorViewModel? _editor;

    public AuthEditorView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ListenToEditor();
    }

    private void ListenToEditor()
    {
        if (_editor is not null)
            _editor.FocusRequested -= FocusField;
        _editor = DataContext as AuthEditorViewModel;
        if (_editor is not null)
            _editor.FocusRequested += FocusField;
    }

    /// <summary>Waits for the form to be laid out (the field may have just become visible), then puts the cursor in the field.</summary>
    private void FocusField(AuthEditorField field) => Dispatcher.UIThread.Post(() =>
    {
        switch (field)
        {
            case AuthEditorField.CallbackUrl:
                CallbackUrlBox.Focus();
                CallbackUrlBox.SelectAll();
                break;
            case AuthEditorField.AccessTokenUrl:
                AccessTokenUrlBox.FocusEditor();
                break;
        }
    }, DispatcherPriority.Loaded);
}
