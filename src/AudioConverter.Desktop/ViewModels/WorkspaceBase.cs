using System.IO;
using System.Windows.Input;
using AudioConverter.Core.Models;
using AudioConverter.Desktop.Mvvm;
using AudioConverter.Desktop.Services;
using L = AudioConverter.Desktop.Localization.Localizer;

namespace AudioConverter.Desktop.ViewModels;

/// <summary>A tool page: owns its report, primary action and drop handling, and runs work through the shared host.</summary>
public abstract class WorkspaceBase : ObservableObject
{
    private readonly string emptyReportKey;
    private string report;
    private bool reportIsPlaceholder = true;

    /// <param name="emptyReportKey">String key of the report text shown before anything has run.</param>
    protected WorkspaceBase(WorkspaceHost host, string key, string emptyReportKey)
    {
        Host = host;
        Key = key;
        this.emptyReportKey = emptyReportKey;
        report = Localization.Localizer.T(emptyReportKey);
        SaveReportCommand = new AsyncRelayCommand(SaveReportAsync, () => !Host.IsBusy);
        host.StateChanged += () => { SaveReportCommand.RaiseCanExecuteChanged(); RaiseCommands(); };
        Localization.Localizer.Instance.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>Refreshes text computed in code; XAML text follows the language through bindings.</summary>
    protected virtual void OnLanguageChanged()
    {
        if (reportIsPlaceholder) report = Localization.Localizer.T(emptyReportKey);
        Raise(string.Empty);
    }

    /// <summary>The text of the first key that <paramref name="value"/> shows in any language, re-read now; otherwise the value.</summary>
    protected static string Refreshed(string value, params string[] keys) =>
        keys.FirstOrDefault(key => Localization.Localizer.IsText(value, key)) is { } match ? Localization.Localizer.T(match) : value;

    public IReadOnlyList<AudioFormat> Formats => AudioFormats.All;
    public IReadOnlyList<string> Bitrates { get; } = ["96k", "128k", "160k", "192k", "256k", "320k"];
    public IReadOnlyList<string> SampleRates { get; } = ["Auto", "44100", "48000", "88200", "96000", "192000"];
    public IReadOnlyList<string> BitDepths { get; } = ["Auto", "16", "24", "32"];
    public WorkspaceHost Host { get; }
    public string Key { get; }
    protected IDialogService Dialogs => Host.Dialogs;
    public string Report { get => report; protected set { reportIsPlaceholder = false; Set(ref report, value); } }
    public AsyncRelayCommand SaveReportCommand { get; }

    /// <summary>The main action shown in the bottom bar.</summary>
    public abstract ICommand PrimaryCommand { get; }
    public abstract string PrimaryLabel { get; }

    /// <summary>Restores choices saved by <see cref="SavePreferences"/>; keys are prefixed with <see cref="Key"/>.</summary>
    public virtual void LoadPreferences(IReadOnlyDictionary<string, string> preferences) { }
    public virtual void SavePreferences(IDictionary<string, string> preferences) { }
    public virtual Task AcceptDropAsync(IReadOnlyList<string> paths) => Task.CompletedTask;

    protected string? Preference(IReadOnlyDictionary<string, string> preferences, string name) =>
        preferences.TryGetValue($"{Key}.{name}", out var value) ? value : null;
    protected static T? EnumPreference<T>(string? value) where T : struct, Enum => Enum.TryParse<T>(value, out var parsed) ? parsed : null;
    protected static bool? BoolPreference(string? value) => bool.TryParse(value, out var parsed) ? parsed : null;
    /// <summary>Keeps a stored value only when it is one of the offered choices.</summary>
    protected static string? Choice(string? value, IEnumerable<string> allowed) => value is not null && allowed.Contains(value) ? value : null;
    public virtual Task ShutdownAsync() => Task.CompletedTask;
    public void AppendReport(string line) => Report += "\n" + line;
    protected virtual void RaiseCommands() { }
    protected Task RunBusyAsync(Func<Task> action) => Host.RunBusyAsync(this, action);
    protected void UpdateOperationProgress(double fraction, string status) => Host.UpdateOperationProgress(fraction, status);
    protected CancellationToken ActiveOperationToken => Host.ActiveOperationToken;

    private async Task SaveReportAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = L.T("TextReportTxt"), FileName = "CodecTone-report.txt" };
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
