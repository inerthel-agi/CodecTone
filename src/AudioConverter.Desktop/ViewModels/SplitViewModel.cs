using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows.Input;
using AudioConverter.Core.Models;
using AudioConverter.Core.Split;
using AudioConverter.Core.Validation;
using AudioConverter.Desktop.Mvvm;
using AudioConverter.Infrastructure.Split;

namespace AudioConverter.Desktop.ViewModels;

/// <summary>Splits one long recording into tracks, from a cue sheet or from the silences in the audio.</summary>
public sealed class SplitViewModel : WorkspaceBase
{
    private const string CueSheetMode = "Cue sheet";
    private const string SilenceMode = "Silence";
    private const int DefaultLossyKbps = 192;
    private static readonly int[] LossyBitratesKbps = [96, 128, 160, 192, 256, 320];

    private readonly ObservableCollection<SplitTrackItem> cueTracks = new();
    private readonly ObservableCollection<SplitTrackItem> silenceTracks = new();
    private ProbeInfo? sourceInfo;
    private IReadOnlyList<SilenceInterval>? silences;
    private string sourcePath = "";
    private string sourceDetails = "";
    private double durationSeconds;
    private string cueStatus = "Load a source file first.";
    private string selectedMode = CueSheetMode;
    private double noiseDb = -45;
    private double minimumSilenceSeconds = 1.5;
    private double minimumTrackSeconds = SilenceSplitPlanner.DefaultMinimumTrackSeconds;
    private string album = "";
    private string albumArtist = "";
    private string outputFolder = "";
    private AudioFormat selectedFormat = AudioFormat.Flac;
    private string filePattern = TrackFileNamer.DefaultPattern;
    private bool overwrite;

    public SplitViewModel(WorkspaceHost host) : base(host, "Split", "Nothing has run yet.")
    {
        ChooseSourceCommand = new AsyncRelayCommand(ChooseSourceAsync, () => !Host.IsBusy);
        ChooseCueCommand = new RelayCommand(ChooseCue, () => !Host.IsBusy && HasSource);
        ChooseOutputCommand = new RelayCommand(() => OutputFolder = Dialogs.ChooseFolder(OutputFolder) ?? OutputFolder, () => !Host.IsBusy);
        DetectSilenceCommand = new AsyncRelayCommand(DetectSilenceAsync, () => !Host.IsBusy && HasSource);
        SplitCommand = new AsyncRelayCommand(SplitAsync, () => !Host.IsBusy && Tracks.Count > 0);
    }

    public override ICommand PrimaryCommand => SplitCommand;
    public override string PrimaryLabel => "Split tracks";
    public AsyncRelayCommand ChooseSourceCommand { get; }
    public RelayCommand ChooseCueCommand { get; }
    public RelayCommand ChooseOutputCommand { get; }
    public AsyncRelayCommand DetectSilenceCommand { get; }
    public AsyncRelayCommand SplitCommand { get; }

    public IReadOnlyList<string> SplitModes { get; } = [CueSheetMode, SilenceMode];
    public bool HasSource => SourcePath.Length > 0;
    public string SourcePath { get => sourcePath; private set { if (Set(ref sourcePath, value)) { Raise(nameof(HasSource)); RaiseCommands(); } } }
    public string SourceDetails { get => sourceDetails; private set => Set(ref sourceDetails, value); }
    public double DurationSeconds { get => durationSeconds; private set => Set(ref durationSeconds, value); }
    public string CueStatus { get => cueStatus; private set => Set(ref cueStatus, value); }
    public string SelectedMode
    {
        get => selectedMode;
        set
        {
            if (!Set(ref selectedMode, value)) return;
            Raise(nameof(IsCueMode));
            Raise(nameof(IsSilenceMode));
            RefreshTracks();
        }
    }
    public bool IsCueMode => selectedMode == CueSheetMode;
    public bool IsSilenceMode => selectedMode == SilenceMode;
    public double NoiseDb { get => noiseDb; set => Set(ref noiseDb, value); }
    public double MinimumSilenceSeconds { get => minimumSilenceSeconds; set => Set(ref minimumSilenceSeconds, value); }
    public double MinimumTrackSeconds
    {
        get => minimumTrackSeconds;
        set
        {
            var next = double.IsFinite(value) ? Math.Max(0, value) : minimumTrackSeconds;
            if (!Set(ref minimumTrackSeconds, next) || silences is null) return;
            PlanSilenceTracks();
            RefreshTracks();
        }
    }
    public string Album { get => album; set => Set(ref album, value); }
    public string AlbumArtist { get => albumArtist; set => Set(ref albumArtist, value); }
    public ObservableCollection<SplitTrackItem> Tracks => IsCueMode ? cueTracks : silenceTracks;
    public string TrackSummary => Tracks.Count == 0
        ? (IsCueMode ? "No tracks yet. Load a cue sheet." : "No tracks yet. Detect silences to plan them.")
        : $"{Tracks.Count} tracks · {TimestampParser.Format(Tracks.Sum(track => track.DurationSeconds))} in total";
    public string OutputFolder { get => outputFolder; set => Set(ref outputFolder, value); }
    public AudioFormat SelectedFormat { get => selectedFormat; set => Set(ref selectedFormat, value); }
    public string FilePattern { get => filePattern; set => Set(ref filePattern, value); }
    public bool Overwrite { get => overwrite; set => Set(ref overwrite, value); }

    public override async Task AcceptDropAsync(IReadOnlyList<string> paths)
    {
        if (paths.FirstOrDefault(File.Exists) is { } path) await LoadSourceAsync(path);
    }

    protected override void RaiseCommands()
    {
        ChooseSourceCommand.RaiseCanExecuteChanged();
        ChooseCueCommand.RaiseCanExecuteChanged();
        ChooseOutputCommand.RaiseCanExecuteChanged();
        DetectSilenceCommand.RaiseCanExecuteChanged();
        SplitCommand.RaiseCanExecuteChanged();
    }

    private async Task ChooseSourceAsync()
    {
        if (Dialogs.ChooseAudioFile() is { } path) await LoadSourceAsync(path);
    }

    private void ChooseCue()
    {
        if (Dialogs.ChooseFile("Cue sheets|*.cue") is { } path) TryLoadCue(path);
        RefreshTracks();
    }

    private Task LoadSourceAsync(string path) => RunBusyAsync(async () =>
    {
        Host.EnsureFfmpeg();
        _ = AudioFormats.FromPath(path);
        Host.Status = "Reading audio…";
        var info = await Host.Probe!.ProbeAsync(path, ActiveOperationToken);
        sourceInfo = info;
        SourcePath = path;
        SourceDetails = WorkspaceHost.DescribeSource(info);
        DurationSeconds = info.DurationSeconds;
        SelectedFormat = AudioFormats.FromPath(path);
        Album = FirstTag(info, "album");
        AlbumArtist = FirstTag(info, "album_artist", "artist");
        OutputFolder = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + " - tracks");

        silences = null;
        silenceTracks.Clear();
        cueTracks.Clear();
        var beside = Path.ChangeExtension(path, ".cue");
        if (File.Exists(beside))
        {
            TryLoadCue(beside);
            SelectedMode = CueSheetMode;
        }
        else
        {
            CueStatus = "No cue sheet beside this file. Choose one, or split by silence.";
            SelectedMode = SilenceMode;
        }
        RefreshTracks();
        UpdateOperationProgress(1, $"Ready to split {Path.GetFileName(path)}");
    });

    /// <summary>Reads a cue sheet into the cue track list. Problems stay in <see cref="CueStatus"/> rather than failing the page.</summary>
    private void TryLoadCue(string path)
    {
        cueTracks.Clear();
        try
        {
            var sheet = CueSheetParser.Parse(ReadText(path));
            var tracks = sheet.ToAlbumTracks(DurationSeconds);
            foreach (var item in ToItems(tracks)) cueTracks.Add(item);
            if (sheet.Title.Length > 0) Album = sheet.Title;
            if (sheet.Performer.Length > 0) AlbumArtist = sheet.Performer;
            CueStatus = $"{Path.GetFileName(path)} · {tracks.Count} tracks";
        }
        catch (Exception error) when (error is IOException or FormatException or NotSupportedException or InvalidDataException or ArgumentException or UnauthorizedAccessException)
        {
            CueStatus = $"{Path.GetFileName(path)}: {error.Message}";
        }
    }

    private Task DetectSilenceAsync() => RunBusyAsync(async () =>
    {
        Host.EnsureFfmpeg();
        Host.Status = "Looking for silences…";
        var progress = new Progress<double>(fraction => UpdateOperationProgress(fraction, "Looking for silences…"));
        var found = await new AlbumSplitService(Host.Tools!).DetectSilenceAsync(
            SourcePath, NoiseDb, MinimumSilenceSeconds, DurationSeconds, progress, ActiveOperationToken);
        silences = found;
        PlanSilenceTracks();
        RefreshTracks();
        if (silenceTracks.Count == 0)
            throw new InvalidDataException("No audio was found outside the silences. Lower the threshold and try again.");
        Report = $"Silence analysis: {found.Count} silences found, {silenceTracks.Count} tracks planned.";
        UpdateOperationProgress(1, $"{silenceTracks.Count} tracks planned from the silences.");
    });

    private Task SplitAsync() => RunBusyAsync(async () =>
    {
        Host.EnsureFfmpeg();
        var directory = OutputFolder.Trim();
        if (directory.Length == 0) throw new InvalidOperationException("Choose an output folder.");
        var source = SourcePath;
        var format = SelectedFormat;
        var plan = Tracks.Select(item => item.ToAlbumTrack()).ToArray();
        if (plan.Length == 0) throw new InvalidOperationException("There are no tracks to split.");

        var names = plan.Select(track => TrackFileNamer.Build(FilePattern, track, format)).ToArray();
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length)
            throw new InvalidOperationException("Two tracks would get the same file name. Change the file name pattern.");

        var options = CreateOptions(format);
        var splitter = new AlbumSplitService(Host.Tools!);
        var artist = AlbumArtist.Trim();
        var albumName = Album.Trim();
        var lines = new List<string>();
        var written = 0;
        Report = "Split started.";
        for (var index = 0; index < plan.Length; index++)
        {
            var track = plan[index];
            var position = index;
            var output = Path.Combine(directory, names[index]);
            var tags = new AlbumTrackTags(
                track.Title,
                track.Performer.Length > 0 ? track.Performer : artist,
                albumName,
                artist,
                track.Number,
                plan.Length);
            var itemProgress = new Progress<double>(fraction =>
                UpdateOperationProgress((position + fraction) / plan.Length, $"Splitting {track.Number}/{plan.Length}: {track.Title}"));
            string line;
            try
            {
                await splitter.WriteTrackAsync(source, output, options, track, tags, itemProgress, ActiveOperationToken);
                written++;
                line = $"OK  {source} -> {output}";
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error)
            {
                line = $"FAIL  {track.Number:00} {track.Title}: {error.Message}";
            }
            lines.Add(line);
            Host.AppendOperationReport(line);
        }

        Report = $"Split completed: {written} written, {plan.Length - written} failed.\n" + string.Join('\n', lines);
        if (written > 0) Host.SetLastOutputFolder(this, directory);
        UpdateOperationProgress(1, $"Split completed: {written} of {plan.Length} tracks.");
    });

    private void PlanSilenceTracks()
    {
        silenceTracks.Clear();
        if (silences is null) return;
        foreach (var item in ToItems(SilenceSplitPlanner.Plan(silences, DurationSeconds, MinimumTrackSeconds))) silenceTracks.Add(item);
    }

    private void RefreshTracks()
    {
        Raise(nameof(Tracks));
        Raise(nameof(TrackSummary));
        RaiseCommands();
    }

    private ConversionOptions CreateOptions(AudioFormat format)
    {
        if (format.IsLossy())
            return new ConversionOptions(format, LossyBitrate(), PreserveMetadata: true, Overwrite: Overwrite);

        // Lossless output keeps the source depth and rate when they are values the encoder accepts.
        var depth = sourceInfo?.BitDepth;
        if (depth is not (16 or 24 or 32) || (format == AudioFormat.Flac && depth == 32)) depth = null;
        var rate = sourceInfo?.SampleRate;
        if (rate is not (>= 8000 and <= 384000)) rate = null;
        return new ConversionOptions(format, null, rate, depth, PreserveMetadata: true, Overwrite: Overwrite);
    }

    /// <summary>The smallest standard bitrate at or above the source's, so re-encoding does not lower quality.</summary>
    private string LossyBitrate()
    {
        var sourceKbps = sourceInfo?.AudioBitrateKbps ?? 0;
        var kbps = sourceKbps > 0
            ? LossyBitratesKbps.FirstOrDefault(value => value >= sourceKbps, LossyBitratesKbps[^1])
            : DefaultLossyKbps;
        return $"{kbps}k";
    }

    private static ObservableCollection<SplitTrackItem> ToItems(IEnumerable<AlbumTrack> tracks) =>
        new(tracks.Select((track, index) => new SplitTrackItem(track with { Number = index + 1 })));

    private static string FirstTag(ProbeInfo info, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (info.Tags.TryGetValue(key, out var value) && value.Trim().Length > 0) return value.Trim();
        }
        return "";
    }

    /// <summary>Reads UTF-8 when the bytes are valid, otherwise Latin-1, which most legacy cue sheets use.</summary>
    private static string ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        try { return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes); }
    }
}
