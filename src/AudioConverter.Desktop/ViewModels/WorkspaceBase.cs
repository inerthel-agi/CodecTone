using System.IO;
using System.Windows.Input;
using AudioConverter.Core.Models;
using AudioConverter.Desktop.Mvvm;
using AudioConverter.Desktop.Services;

namespace AudioConverter.Desktop.ViewModels;

/// <summary>A tool page: owns its report, primary action and drop handling, and runs work through the shared host.</summary>
public abstract class WorkspaceBase : ObservableObject
{
    private string report;

    protected WorkspaceBase(WorkspaceHost host, string key, string emptyReport)
    {
        Host = host;
        Key = key;
        report = emptyReport;
        SaveReportCommand = new AsyncRelayCommand(SaveReportAsync, () => !Host.IsBusy);
        host.StateChanged += () => { SaveReportCommand.RaiseCanExecuteChanged(); RaiseCommands(); };
    }

    public IReadOnlyList<AudioFormat> Formats => AudioFormats.All;
    public IReadOnlyList<string> Bitrates { get; } = ["96k", "128k", "160k", "192k", "256k", "320k"];
    public IReadOnlyList<string> SampleRates { get; } = ["Auto", "44100", "48000", "88200", "96000", "192000"];
    public IReadOnlyList<string> BitDepths { get; } = ["Auto", "16", "24", "32"];
    public WorkspaceHost Host { get; }
    public string Key { get; }
    protected IDialogService Dialogs => Host.Dialogs;
    public string Report { get => report; protected set => Set(ref report, value); }
    public AsyncRelayCommand SaveReportCommand { get; }

    /// <summary>The main action shown in the bottom bar.</summary>
    public abstract ICommand PrimaryCommand { get; }
    public abstract string PrimaryLabel { get; }

    public virtual Task AcceptDropAsync(IReadOnlyList<string> paths) => Task.CompletedTask;
    public virtual Task ShutdownAsync() => Task.CompletedTask;
    public void AppendReport(string line) => Report += "\n" + line;
    protected virtual void RaiseCommands() { }
    protected Task RunBusyAsync(Func<Task> action) => Host.RunBusyAsync(this, action);
    protected void UpdateOperationProgress(double fraction, string status) => Host.UpdateOperationProgress(fraction, status);
    protected CancellationToken ActiveOperationToken => Host.ActiveOperationToken;

    private async Task SaveReportAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Text report|*.txt", FileName = "CodecTone-report.txt" };
        if (dialog.ShowDialog() != true) return;
        try { await File.WriteAllTextAsync(dialog.FileName, Report); }
        catch (Exception error) { Host.Log.Write("Save report", error.Message); Dialogs.Error(error.Message); }
    }

    protected static bool IsPathInside(string path, string directory)
    {
        var fullPath = Path.GetFullPath(path);
        var fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
    }

    protected static int? ParseOptionalInt(string value) => int.TryParse(value, out var result) ? result : null;
}
