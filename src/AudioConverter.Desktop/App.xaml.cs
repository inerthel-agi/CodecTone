using System.Windows;
using AudioConverter.Desktop.Services;
using AudioConverter.Desktop.ViewModels;
namespace AudioConverter.Desktop;
public partial class App : Application
{
    private const string MutexName = "Local\\Inerthel.AudioConverter.SingleInstance";
    private const string ActivationEventName = "Local\\Inerthel.AudioConverter.Activate";
    private Mutex? instanceMutex;
    private EventWaitHandle? activationEvent;
    private readonly CancellationTokenSource shutdown = new();
    private bool ownsMutex;
    private Task? activationListener;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instanceMutex = new Mutex(true, MutexName, out var isFirstInstance);
        ownsMutex = isFirstInstance;
        if (!isFirstInstance)
        {
            try { EventWaitHandle.OpenExisting(ActivationEventName).Set(); }
            catch (WaitHandleCannotBeOpenedException) { }
            Shutdown();
            return;
        }

        activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
        if (!SystemParameters.ClientAreaAnimation) ThemeService.DisableMotion(Resources);
        var viewModel = new MainViewModel(new DialogService());
        var window = new MainWindow { DataContext = viewModel };
        MainWindow = window;
        window.Show();
        activationListener = ListenForActivationAsync(window, shutdown.Token);
        await viewModel.InitializeAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        shutdown.Cancel();
        activationEvent?.Set();
        activationListener?.GetAwaiter().GetResult();
        activationEvent?.Dispose();
        if (ownsMutex) instanceMutex?.ReleaseMutex();
        instanceMutex?.Dispose();
        shutdown.Dispose();
        base.OnExit(e);
    }

    private Task ListenForActivationAsync(Window window, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                WaitHandle.WaitAny([activationEvent!, cancellationToken.WaitHandle]);
                if (cancellationToken.IsCancellationRequested) break;
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (cancellationToken.IsCancellationRequested) return;
                    if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                    window.Show();
                    window.Activate();
                    window.Topmost = true;
                    window.Topmost = false;
                    window.Focus();
                }));
            }
        });
    }
}
