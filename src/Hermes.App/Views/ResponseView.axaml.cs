using Avalonia.Controls;
using Hermes.App.ViewModels;

namespace Hermes.App.Views;

public partial class ResponseView : UserControl
{
    private ResponseViewModel? _viewModel;

    public ResponseView() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = DataContext as ResponseViewModel;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateTimingBar();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ResponseViewModel.WaitMs) or nameof(ResponseViewModel.DownloadMs))
            UpdateTimingBar();
    }

    /// <summary>Grid column widths can't be bound, so the bar's proportions are set here.</summary>
    private void UpdateTimingBar()
    {
        var wait = Math.Max(_viewModel?.WaitMs ?? 0, 0.01);
        var download = Math.Max(_viewModel?.DownloadMs ?? 0, 0.01);
        TimingBar.ColumnDefinitions[0].Width = new GridLength(wait, GridUnitType.Star);
        TimingBar.ColumnDefinitions[2].Width = new GridLength(download, GridUnitType.Star);
    }
}
