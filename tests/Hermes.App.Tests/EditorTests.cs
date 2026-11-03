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

        // The new tab's text comes before or after the DataContext; either way its history starts empty.
        editor.Text = "other";
        editor.DataContext = "second tab";
        UiTestHelpers.Pump();
        Assert.False(inner.Document.UndoStack.CanUndo);

        // Switching to a tab with the same text, typing, then a view-model edit: the typing can still be undone.
        editor.DataContext = "third tab";
        UiTestHelpers.Pump();
        inner.Document.Insert(inner.Document.TextLength, "!");
        editor.Text = "other! (formatted)";
        inner.Undo();
        Assert.Equal("other!", inner.Text);
        window.Close();
    }

    [AvaloniaFact]
    public void Preview_text_is_only_filled_in_preview_mode()
    {
        var response = new ResponseViewModel(App.Services.GetRequiredService<DialogService>(), App.Services.GetRequiredService<SettingsService>());
        response.SetResponse(new ResponseModel
        {
            StatusCode = 200,
            ReasonPhrase = "OK",
            ContentType = "text/html",
            Body = "<p>hello</p>"u8.ToArray(),
            Headers = [new KeyValueItem("Content-Type", "text/html")],
        });
        var changed = new List<string?>();
        response.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        response.ViewMode = ResponseViewMode.Pretty;
        Assert.Equal("", response.PreviewText);

        response.ViewMode = ResponseViewMode.Preview;
        Assert.Equal("<p>hello</p>", response.PreviewText);
        Assert.Contains(nameof(ResponseViewModel.PreviewText), changed);

        response.ViewMode = ResponseViewMode.Raw;
        Assert.Equal("", response.PreviewText);
    }

    [AvaloniaFact]
    public void Text_preview_uses_a_wrapping_read_only_editor()
    {
        var response = new ResponseViewModel(App.Services.GetRequiredService<DialogService>(), App.Services.GetRequiredService<SettingsService>());
        response.SetResponse(new ResponseModel
        {
            StatusCode = 200,
            ReasonPhrase = "OK",
            ContentType = "text/html",
            Body = "<p>hello</p>"u8.ToArray(),
            Headers = [new KeyValueItem("Content-Type", "text/html")],
        });
        var view = new Hermes.App.Views.ResponseView { DataContext = response };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();

        response.ViewMode = ResponseViewMode.Preview;
        UiTestHelpers.Pump();

        var preview = view.FindControl<CodeEditor>("PreviewTextEditor")!;
        Assert.True(preview.IsEffectivelyVisible);
        Assert.Equal("<p>hello</p>", preview.Text);
        var inner = Assert.IsType<TextEditor>(preview.Content);
        Assert.True(inner.WordWrap);
        Assert.True(inner.IsReadOnly);
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
            StatusCode = 200,
            ReasonPhrase = "OK",
            ContentType = "image/png",
            Body = png,
            Headers = [new KeyValueItem("Content-Type", "image/png")],
        });
        var image = Assert.IsType<Avalonia.Media.Imaging.Bitmap>(response.PreviewImage);

        response.Clear();

        Assert.Throws<ObjectDisposedException>(() => image.PixelSize);
    }
}
