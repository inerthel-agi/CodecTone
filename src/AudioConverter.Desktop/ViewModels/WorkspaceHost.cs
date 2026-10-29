using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using AudioConverter.Core.Models;
using AudioConverter.Core.Progress;
using AudioConverter.Desktop.Mvvm;
using AudioConverter.Desktop.Services;
using AudioConverter.Infrastructure.Artwork;
using AudioConverter.Infrastructure.Audio;
using AudioConverter.Infrastructure.Ffmpeg;
using AudioConverter.Infrastructure.Remix;
using AudioConverter.Infrastructure.Storage;

namespace AudioConverter.Desktop.ViewModels;

/// <summary>
/// State shared by every workspace: FFmpeg services, the single running operation with its progress and
/// cancellation, the preview player and playhead, the last output folder, and persisted settings.
/// </summary>
public sealed class WorkspaceHost : ObservableObject, IDisposable
{
    private readonly JsonSettingsStore settingsStore = new();
    private readonly Stopwatch operationTimer = new();
    private readonly Stopwatch playheadClock = new();
    private TaskCompletionSource? operationFinished;
    private CancellationTokenSource? activeOperationCancellation;
    private WorkspaceBase? operationWorkspace;
    private DispatcherTimer? playheadTimer;
    private Action<double>? playheadSink;
    private double playheadStart;
    private double playheadLength;
    private double playheadRate = 1;
    private bool isBusy;
    private bool closing;
    private bool ffmpegMissing;
    private double progress;
    private string status = "Ready";
    private string timingText = "";
    private bool disposed;

    public WorkspaceHost(IDialogService dialogs)
    {
        Dialogs = dialogs;
        InstallFfmpegCommand = new AsyncRelayCommand(InstallFfmpegAsync, () => !IsBusy);
        CancelOperationCommand = new RelayCommand(CancelActiveOperation);
    }

    public IDialogService Dialogs { get; }
    public LocalDiagnosticLog Log { get; } = new();
    public WavePreviewPlayer PreviewPlayer { get; } = new();
    public AppSettings Settings { get; private set; } = new();
    public FfmpegTools? Tools { get; private set; }
    public AudioProcessingService? Audio { get; private set; }
    public FfprobeService? Probe { get; private set; }
    public RemixProcessingService? Remix { get; private set; }
    public ArtworkExtractionService? ArtworkExtractor { get; private set; }
    public AdaptiveAudioAnalyzer? Analyzer { get; private set; }
    public AsyncRelayCommand InstallFfmpegCommand { get; }
    public RelayCommand CancelOperationCommand { get; }

    /// <summary>Raised when busy or closing changes so workspaces can refresh their commands.</summary>
    public event Action? StateChanged;

    public bool IsBusy { get => isBusy; private set { if (Set(ref isBusy, value)) { Raise(nameof(IsWorkspaceEnabled)); InstallFfmpegCommand.RaiseCanExecuteChanged(); StateChanged?.Invoke(); } } }
    public bool IsClosing => closing;
    public bool IsWorkspaceEnabled => !IsBusy && !closing;
    public bool FfmpegMissing { get => ffmpegMissing; private set => Set(ref ffmpegMissing, value); }
    public double Progress { get => progress; private set => Set(ref progress, value); }
    public string Status { get => status; set => Set(ref status, value); }
    public string TimingText { get => timingText; set => Set(ref timingText, value); }
    public string? LastOutputFolder { get; private set; }
    public WorkspaceBase? LastOutputWorkspace { get; private set; }
    public CancellationToken ActiveOperationToken => activeOperationCancellation?.Token ?? CancellationToken.None;

    public async Task LoadSettingsAsync() => Settings = await settingsStore.LoadAsync();

    /// <summary>Applies a change to the persisted settings and saves them.</summary>
    public async Task SaveSettingsAsync(Func<AppSettings, AppSettings> update, CancellationToken cancellationToken = default)
    {
        Settings = update(Settings);
        await settingsStore.SaveAsync(Settings, cancellationToken);
    }

    public void TryLocateFfmpeg()
    {
        try
        {
            UseTools(FfmpegLocator.Find());
            Status = "FFmpeg ready. All processing stays local.";
        }
        catch (FfmpegDependencyException error)
        {
            FfmpegMissing = true; Status = error.Message;
        }
    }

    public void EnsureFfmpeg()
    {
        if (Tools is null || Audio is null || Probe is null || Remix is null || ArtworkExtractor is null)
            throw new FfmpegDependencyException("FFmpeg is required. Install it from this application first.");
    }

    private void UseTools(FfmpegTools tools)
    {
        Tools = tools; Audio = new AudioProcessingService(tools); Probe = new FfprobeService(tools.FfprobePath); Remix = new RemixProcessingService(tools); Analyzer = new AdaptiveAudioAnalyzer(tools); ArtworkExtractor = new ArtworkExtractionService(tools);
        FfmpegMissing = false;
    }

    private async Task InstallFfmpegAsync()
    {
        await RunBusyAsync(null, async () =>
        {
            var installer = new FfmpegInstaller();
            var installProgress = new Progress<(double Fraction, string Status)>(item => UpdateOperationProgress(item.Fraction, item.Status));
            UseTools(await installer.InstallAsync(progress: installProgress, cancellationToken: ActiveOperationToken));
        });
    }

    /// <summary>Runs one operation at a time with progress, timing, cancellation and failure reporting.</summary>
    public async Task RunBusyAsync(WorkspaceBase? workspace, Func<Task> action)
    {
        if (IsBusy || closing) return;
        operationWorkspace = workspace;
        operationFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Log.Write(workspace?.Key ?? "App", "Operation started");
        IsBusy = true;
        activeOperationCancellation = new CancellationTokenSource();
        Progress = 0;
        TimingText = "Estimating time remaining…";
        operationTimer.Restart();
        var succeeded = false;
        try
        {
            await action();
            succeeded = true;
        }
        catch (OperationCanceledException)
        {
            operationTimer.Stop();
            TimingText = $"Cancelled after {OperationTiming.FormatCompact(operationTimer.Elapsed)}";
            Status = "Operation cancelled.";
            AppendOperationReport("CANCELLED. Completed files are listed above; remaining files were not processed.");
        }
        catch (Exception error)
        {
            operationTimer.Stop();
            TimingText = $"Stopped after {OperationTiming.FormatCompact(operationTimer.Elapsed)}";
            Status = error.Message;
            AppendOperationReport("FAIL  " + error.Message);
            if (!closing) Dialogs.Error(error.Message);
        }
        finally
        {
            operationTimer.Stop();
            if (succeeded && Progress >= 100) TimingText = $"Completed in {OperationTiming.FormatCompact(operationTimer.Elapsed)}";
            IsBusy = false;
            activeOperationCancellation.Dispose();
            activeOperationCancellation = null;
            Log.Write(workspace?.Key ?? "App", Status);
            operationFinished?.TrySetResult();
        }
    }

    /// <summary>Adds a line to the report of the workspace that started the running operation.</summary>
    public void AppendOperationReport(string line)
    {
        operationWorkspace?.AppendReport(line);
        Log.Write(operationWorkspace?.Key ?? "App", line);
    }

    public void UpdateOperationProgress(double completedFraction, string statusText)
    {
        var fraction = Math.Clamp(completedFraction, 0, 1);
        Progress = fraction * 100;
        Status = statusText;
        if (fraction >= 1)
        {
            TimingText = $"Completed in {OperationTiming.FormatCompact(operationTimer.Elapsed)}";
            return;
        }

        var remaining = OperationTiming.EstimateRemaining(operationTimer.Elapsed, fraction);
        if (remaining is null || operationTimer.Elapsed < TimeSpan.FromSeconds(2))
        {
            TimingText = "Estimating time remaining…";
            return;
        }

        var finishTime = DateTime.Now.Add(remaining.Value);
        TimingText = $"{OperationTiming.FormatCompact(remaining.Value)} remaining · finishes around {finishTime:HH:mm}";
    }

    public void CancelActiveOperation() => activeOperationCancellation?.Cancel();

    public void SetLastOutputFolder(WorkspaceBase workspace, string directory)
    {
        LastOutputFolder = directory;
        LastOutputWorkspace = workspace;
        Raise(nameof(LastOutputFolder));
    }

    public void OpenFolder(string? directory)
    {
        try
        {
            if (directory is null || !Directory.Exists(directory)) { Dialogs.Error("The output folder does not exist yet."); return; }
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }
        catch (Exception error) { Dialogs.Error(error.Message); }
    }

    /// <summary>Moves a playhead from <paramref name="start"/> over <paramref name="length"/> source seconds, reporting through <paramref name="sink"/> (NaN when stopped).</summary>
    public void StartPlayhead(Action<double> sink, double start, double length, double rate)
    {
        StopPlayhead();
        playheadSink = sink; playheadStart = start; playheadLength = length; playheadRate = rate;
        if (playheadTimer is null)
        {
            playheadTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
            playheadTimer.Tick += (_, _) => TickPlayhead();
        }
        playheadClock.Restart();
        playheadTimer.Start();
        TickPlayhead();
    }

    // ponytail: wall-clock estimate because PlaySound reports no position; use a real audio API if drift becomes visible.
    private void TickPlayhead()
    {
        var travelled = playheadClock.Elapsed.TotalSeconds * playheadRate;
        if (travelled >= playheadLength) { StopPlayhead(); return; }
        playheadSink?.Invoke(playheadStart + travelled);
    }

    public void StopPlayhead()
    {
        playheadTimer?.Stop();
        playheadClock.Reset();
        playheadSink?.Invoke(double.NaN);
        playheadSink = null;
    }

    /// <summary>Cancels work, stops playback and waits for the running operation to finish.</summary>
    public async Task BeginShutdownAsync()
    {
        closing = true;
        Raise(nameof(IsWorkspaceEnabled));
        StateChanged?.Invoke();
        activeOperationCancellation?.Cancel();
        PreviewPlayer.Stop();
        StopPlayhead();
        if (operationFinished is not null) await operationFinished.Task;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        activeOperationCancellation?.Cancel();
        playheadTimer?.Stop();
        PreviewPlayer.Dispose();
        Remix?.CleanupTemporaryFiles();
    }

    public static string FormatBytes(long bytes) => $"{bytes / 1024d / 1024d:0.00} MiB";

    public static string DescribeSource(ProbeInfo info) => string.Join(" · ", new[]
    {
        info.CodecName.ToUpperInvariant(),
        info.SampleRate is { } rate ? $"{rate / 1000d:0.#} kHz" : null,
        info.BitDepth is { } depth ? $"{depth}-bit" : null,
        AudioConverter.Core.Validation.TimestampParser.Format(info.DurationSeconds),
        info.SizeBytes is { } size ? FormatBytes(size) : null,
    }.Where(part => !string.IsNullOrEmpty(part)));
}
