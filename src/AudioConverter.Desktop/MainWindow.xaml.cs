using System.ComponentModel;
using System.Windows;
using AudioConverter.Desktop.ViewModels;

namespace AudioConverter.Desktop;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void OnDragOver(object sender, DragEventArgs e)
    {
        var accepted = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        // DragOver keeps firing while the pointer is inside, so it re-shows the overlay right after a child's DragLeave.
        var canLoad = accepted && DataContext is MainViewModel { IsToolWorkspace: true, IsWorkspaceEnabled: true };
        DropOverlay.Visibility = canLoad ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e) => DropOverlay.Visibility = Visibility.Collapsed;

    private async void OnDrop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (DataContext is MainViewModel viewModel && e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            await viewModel.AcceptDropAsync(paths);
        }
    }

    private bool closeReady;
    private bool closePending;
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (closeReady) return;
        e.Cancel = true;
        if (closePending) return;
        closePending = true;
        try
        {
            if (DataContext is MainViewModel viewModel) await viewModel.ShutdownAsync();
        }
        catch (Exception error)
        {
            new AudioConverter.Infrastructure.Storage.LocalDiagnosticLog().Write("Shutdown", error.Message);
        }
        closeReady = true;
        _ = Dispatcher.BeginInvoke(new Action(Close));
    }
}
