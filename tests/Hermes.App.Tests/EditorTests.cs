using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using AvaloniaEdit;
using Hermes.App.Controls;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Tests;

public class EditorTests
{
    [AvaloniaFact]
    public void A_view_model_edit_can_be_undone_but_another_document_cannot()
    {
        var editor = new CodeEditor { DataContext = "first tab" };
        var window = new Window { Content = editor };
        window.Show();
        var inner = Assert.IsType<TextEditor>(editor.Content);

        editor.Text = "{\"a\":1}";
        editor.Text = "{\n  \"a\": 1\n}";
        Assert.True(inner.Document.UndoStack.CanUndo);
        inner.Undo();
        Assert.Equal("{\"a\":1}", inner.Text);

        editor.DataContext = "second tab";
        editor.Text = "other";
        Assert.False(inner.Document.UndoStack.CanUndo);
        window.Close();
    }

    [AvaloniaFact]
    public void Clearing_a_response_frees_its_preview_image()
    {
        var response = new ResponseViewModel(App.Services.GetRequiredService<DialogService>(), App.Services.GetRequiredService<SettingsService>());
        // A 1x1 PNG.
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg==");
        response.SetResponse(new ResponseModel
        {
            StatusCode = 200, ReasonPhrase = "OK", ContentType = "image/png", Body = png,
            Headers = [new KeyValueItem("Content-Type", "image/png")],
        });
        var image = Assert.IsType<Avalonia.Media.Imaging.Bitmap>(response.PreviewImage);

        response.Clear();

        Assert.Throws<ObjectDisposedException>(() => image.PixelSize);
    }
}
