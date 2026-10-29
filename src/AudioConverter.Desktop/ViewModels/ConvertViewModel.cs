using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using AudioConverter.Core.Models;
using AudioConverter.Core.Paths;
using AudioConverter.Desktop.Mvvm;

namespace AudioConverter.Desktop.ViewModels;

public sealed class ConvertViewModel : WorkspaceBase
{
    private string outputFolder = "";
    private string suffix = "";
    private AudioFormat selectedFormat = AudioFormat.Mp3;
    private string bitrate = "192k";
    private string sampleRate = "Auto";
    private string bitDepth = "Auto";
    private bool preserveMetadata = true;
    private bool overwrite;

    public ConvertViewModel(WorkspaceHost host) : base(host, "Convert", "No conversion has run yet.")
    {
        ChooseFileCommand = new RelayCommand(() => { if (Dialogs.ChooseAudioFiles() is { } paths) AddSources(paths); });
        ChooseFolderCommand = new RelayCommand(() => { if (Dialogs.ChooseFolder() is { } path) AddSources([path]); });
        ChooseOutputCommand = new RelayCommand(() => { var path = Dialogs.ChooseFolder(OutputFolder); if (path is not null) OutputFolder = path; });
        ClearQueueCommand = new RelayCommand(() => Queue.Clear());
        RemoveQueueItemCommand = new RelayCommand<QueueItem>(item => Queue.Remove(item));
        ConvertCommand = new AsyncRelayCommand(ConvertAsync, () => !Host.IsBusy && Queue.Count > 0);
        Queue.CollectionChanged += (_, _) => { Raise(nameof(HasQueue)); Raise(nameof(QueueSummary)); ConvertCommand.RaiseCanExecuteChanged(); };
    }

    /// <summary>Files waiting to be converted, in order.</summary>
    public ObservableCollection<QueueItem> Queue { get; } = [];
    public bool HasQueue => Queue.Count > 0;
    public string QueueSummary => Queue.Count == 1 ? "1 file" : $"{Queue.Count} files";
    public RelayCommand ClearQueueCommand { get; }
    public RelayCommand<QueueItem> RemoveQueueItemCommand { get; }

    /// <summary>Adds supported audio files; a folder contributes its files without subfolders. Duplicates are ignored.</summary>
    public void AddSources(IEnumerable<string> paths)
    {
        var known = new HashSet<string>(Queue.Select(item => item.Path), StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths.SelectMany(EnumerateSources).Select(Path.GetFullPath))
            if (known.Add(path)) Queue.Add(new QueueItem(path, Path.GetFileName(path)));
    }

    public override ICommand PrimaryCommand => ConvertCommand;
    public override string PrimaryLabel => "Convert audio";
    public RelayCommand ChooseFileCommand { get; }
    public RelayCommand ChooseFolderCommand { get; }
    public RelayCommand ChooseOutputCommand { get; }
    public AsyncRelayCommand ConvertCommand { get; }
    public string OutputFolder { get => outputFolder; set => Set(ref outputFolder, value); }
    public string Suffix { get => suffix; set => Set(ref suffix, value); }
    public AudioFormat SelectedFormat { get => selectedFormat; set { if (Set(ref selectedFormat, value)) { Raise(nameof(IsLossyFormat)); Raise(nameof(IsLosslessFormat)); } } }
    public bool IsLossyFormat => SelectedFormat.IsLossy();
    public bool IsLosslessFormat => !IsLossyFormat;
    public string Bitrate { get => bitrate; set => Set(ref bitrate, value); }
    public string SampleRate { get => sampleRate; set => Set(ref sampleRate, value); }
    public string BitDepth { get => bitDepth; set => Set(ref bitDepth, value); }
    public bool PreserveMetadata { get => preserveMetadata; set => Set(ref preserveMetadata, value); }
    public bool Overwrite { get => overwrite; set => Set(ref overwrite, value); }

    public override void LoadPreferences(IReadOnlyDictionary<string, string> preferences)
    {
        SelectedFormat = EnumPreference<AudioFormat>(Preference(preferences, "Format")) ?? SelectedFormat;
        Bitrate = Choice(Preference(preferences, "Bitrate"), Bitrates) ?? Bitrate;
        SampleRate = Choice(Preference(preferences, "SampleRate"), SampleRates) ?? SampleRate;
        BitDepth = Choice(Preference(preferences, "BitDepth"), BitDepths) ?? BitDepth;
        PreserveMetadata = BoolPreference(Preference(preferences, "PreserveMetadata")) ?? PreserveMetadata;
        Suffix = Preference(preferences, "Suffix") ?? Suffix;
    }

    public override void SavePreferences(IDictionary<string, string> preferences)
    {
        preferences[$"{Key}.Format"] = SelectedFormat.ToString();
        preferences[$"{Key}.Bitrate"] = Bitrate;
        preferences[$"{Key}.SampleRate"] = SampleRate;
        preferences[$"{Key}.BitDepth"] = BitDepth;
        preferences[$"{Key}.PreserveMetadata"] = PreserveMetadata.ToString();
        preferences[$"{Key}.Suffix"] = Suffix;
    }

    public override Task AcceptDropAsync(IReadOnlyList<string> paths)
    {
        AddSources(paths);
        return Task.CompletedTask;
    }

    protected override void RaiseCommands() => ConvertCommand.RaiseCanExecuteChanged();

    private async Task ConvertAsync()
    {
        await RunBusyAsync(async () =>
        {
            Host.EnsureFfmpeg();
            var format = SelectedFormat;
            var configuredOutput = OutputFolder;
            var configuredSuffix = Suffix;
            var options = CreateOptions(format);
            Report = "Conversion started.";
            var items = Queue.ToArray();
            foreach (var item in items) item.Update(QueueState.Waiting, "Waiting");
            var results = new List<FileResult>();
            for (var index = 0; index < items.Length; index++)
            {
                var item = items[index];
                var file = item.Path;
                try
                {
                    item.Update(QueueState.Running, "Converting…");
                    _ = AudioFormats.FromPath(file);
                    var info = await Host.Probe!.ProbeAsync(file, ActiveOperationToken);
                    var directory = ResolveOutputDirectory(file, configuredOutput);
                    Directory.CreateDirectory(directory);
                    var output = OutputPathBuilder.Build(file, directory, format, configuredSuffix);
                    var itemProgress = new Progress<double>(fraction =>
                    {
                        item.Update(QueueState.Running, $"Converting {fraction:P0}", fraction * 100);
                        UpdateOperationProgress((index + fraction) / items.Length, $"Converting {index + 1}/{items.Length}: {item.Name}");
                    });
                    await Host.Audio!.ConvertAsync(file, output, options, info.DurationSeconds, itemProgress, ActiveOperationToken);
                    results.Add(new FileResult(file, output, null));
                    item.Update(QueueState.Done, "Done", 100);
                    Host.AppendOperationReport($"OK  {file} -> {output}");
                    Host.SetLastOutputFolder(this, directory);
                }
                catch (OperationCanceledException) { item.Update(QueueState.Waiting, "Cancelled"); throw; }
                catch (Exception error)
                {
                    results.Add(new FileResult(file, null, error.Message));
                    item.Update(QueueState.Failed, "Failed");
                    Host.AppendOperationReport($"FAIL  {file}: {error.Message}");
                }
            }
            var batch = new BatchResult(results);
            Report = $"Completed: {batch.Succeeded} succeeded, {batch.Failed} failed\n" + string.Join('\n', results.Select(result => result.Succeeded ? $"OK  {Path.GetFileName(result.InputPath)} -> {result.OutputPath}" : $"FAIL  {Path.GetFileName(result.InputPath)}: {result.Error}"));
            UpdateOperationProgress(1, $"Completed: {batch.Succeeded} succeeded, {batch.Failed} failed");
            await Host.SaveSettingsAsync(settings => settings with { LastOutputDirectory = configuredOutput }, ActiveOperationToken);
        });
    }

    private ConversionOptions CreateOptions(AudioFormat format) => new(
        format,
        format.IsLossy() ? Bitrate : null,
        format.IsLossy() ? null : ParseOptionalInt(SampleRate),
        format.IsLossy() ? null : ParseOptionalInt(BitDepth),
        PreserveMetadata,
        Overwrite);

    private static string ResolveOutputDirectory(string source, string configured) => string.IsNullOrWhiteSpace(configured) ? Path.Combine(Path.GetDirectoryName(source)!, "converted") : configured;
    private static IEnumerable<string> EnumerateSources(string source)
    {
        IEnumerable<string> candidates = File.Exists(source) ? [source] : Directory.Exists(source) ? Directory.EnumerateFiles(source) : [];
        return candidates.Where(path => { try { _ = AudioFormats.FromPath(path); return true; } catch (ArgumentException) { return false; } });
    }
}
