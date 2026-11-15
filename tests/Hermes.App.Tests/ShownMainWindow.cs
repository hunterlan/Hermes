using Hermes.App.ViewModels;
using Hermes.App.Views;

namespace Hermes.App.Tests;

/// <summary>A main window shown by <see cref="UiTestHelpers.ShowMainWindow"/>, with the view model it displays.</summary>
internal sealed record ShownMainWindow(MainWindow Window, MainWindowViewModel ViewModel);
