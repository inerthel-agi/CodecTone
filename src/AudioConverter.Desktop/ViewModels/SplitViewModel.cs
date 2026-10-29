using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows.Input;
using AudioConverter.Core.Models;
using AudioConverter.Core.Split;
using AudioConverter.Core.Validation;
using AudioConverter.Desktop.Mvvm;
using AudioConverter.Infrastructure.Split;
using L = AudioConverter.Desktop.Localization.Localizer;

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
    private string cueStatus = L.T("LoadASourceFileFirst");
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

    public SplitViewModel(WorkspaceHost host) : base(host, "Split", "ReportEmptyGeneric")
    {
        ChooseSourceCommand = new AsyncRelayCommand(ChooseSourceAsync, () => !Host.IsBusy);
        ChooseCueCommand = new RelayCommand(ChooseCue, () => !Host.IsBusy && HasSource);
        ChooseOutputCommand = new RelayCommand(() => OutputFolder = Dialogs.ChooseFolder(OutputFolder) ?? OutputFolder, () => !Host.IsBusy);
        DetectSilenceCommand = new AsyncRelayCommand(DetectSilenceAsync, () => !Host.IsBusy && HasSource);
        SplitCommand = new AsyncRelayCommand(SplitAsync, () => !Host.IsBusy && Tracks.Count > 0);
    }

    public override ICommand PrimaryCommand => SplitCommand;
    public override string PrimaryLabel => L.T("SplitTracks");
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
        ? (IsCueMode ? L.T("NoTracksYetLoadACue") : L.T("NoTracksYetDetectSilencesTo"))
        : L.F("TracksInTotal", Tracks.Count, TimestampParser.Format(Tracks.Sum(track => track.DurationSeconds)));
    public string OutputFolder { get => outputFolder; set => Set(ref outputFolder, value); }
    public AudioFormat SelectedFormat { get => selectedFormat; set => Set(ref selectedFormat, value); }
    public string FilePattern { get => filePattern; set => Set(ref filePattern, value); }
    public bool Overwrite { get => overwrite; set => Set(ref overwrite, value); }

    public override async Task AcceptDropAsync(IReadOnlyList<string> paths)
    {
        if (paths.FirstOrDefault(File.Exists) is { } path) await LoadSourceAsync(path);
    }

    protected override void OnLanguageChanged()
    {
        cueStatus = Refreshed(cueStatus, "LoadASourceFileFirst", "NoCueSheetBesideThisFile");
        base.OnLanguageChanged();
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
        if (Dialogs.ChooseFile(L.T("CueSheetsCue")) is { } path) TryLoadCue(path);
        RefreshTracks();
    }

    private Task LoadSourceAsync(string path) => RunBusyAsync(async () =>
    {
        Host.EnsureFfmpeg();
        _ = AudioFormats.FromPath(path);
        Host.Status = L.T("ReadingAudio");
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
            CueStatus = L.T("NoCueSheetBesideThisFile");
            SelectedMode = SilenceMode;
        }
        RefreshTracks();
        UpdateOperationProgress(1, L.F("ReadyToSplit", Path.GetFileName(path)));
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
            CueStatus = L.F("Tracks2", Path.GetFileName(path), tracks.Count);
        }
        catch (Exception error) when (error is IOException or FormatException or NotSupportedException or InvalidDataException or ArgumentException or UnauthorizedAccessException)
        {
            CueStatus = $"{Path.GetFileName(path)}: {error.Message}";
        }
    }

    private Task DetectSilenceAsync() => RunBusyAsync(async () =>
    {
        Host.EnsureFfmpeg();
        Host.Status = L.T("LookingForSilences");
        var progress = new Progress<double>(fraction => UpdateOperationProgress(fraction, L.T("LookingForSilences")));
        var found = await new AlbumSplitService(Host.Tools!).DetectSilenceAsync(
            SourcePath, NoiseDb, MinimumSilenceSeconds, DurationSeconds, progress, ActiveOperationToken);
        silences = found;
        PlanSilenceTracks();
        RefreshTracks();
        if (silenceTracks.Count == 0)
            throw new InvalidDataException(L.T("NoAudioWasFoundOutsideThe"));
        Report = L.F("SilenceAnalysisSilencesFoundTracksPlanne", found.Count, silenceTracks.Count);
        UpdateOperationProgress(1, L.F("TracksPlannedFromTheSilences", silenceTracks.Count));
    });

    private Task SplitAsync() => RunBusyAsync(async () =>
    {
        Host.EnsureFfmpeg();
        var directory = OutputFolder.Trim();
        if (directory.Length == 0) throw new InvalidOperationException(L.T("ChooseAnOutputFolder"));
        var source = SourcePath;
        var format = SelectedFormat;
        var plan = Tracks.Select(item => item.ToAlbumTrack()).ToArray();
        if (plan.Length == 0) throw new InvalidOperationException(L.T("ThereAreNoTracksToSplit"));

        var names = plan.Select(track => TrackFileNamer.Build(FilePattern, track, format)).ToArray();
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length)
            throw new InvalidOperationException(L.T("TwoTracksWouldGetTheSame"));

        var options = CreateOptions(format);
        var splitter = new AlbumSplitService(Host.Tools!);
        var artist = AlbumArtist.Trim();
        var albumName = Album.Trim();
        var lines = new List<string>();
        var written = 0;
        Report = L.T("SplitStarted");
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
                UpdateOperationProgress((position + fraction) / plan.Length, L.F("Splitting", track.Number, plan.Length, track.Title)));
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

        Report = L.F("SplitCompletedWrittenFailed", written, plan.Length - written) + string.Join('\n', lines);
        if (written > 0) Host.SetLastOutputFolder(this, directory);
        UpdateOperationProgress(1, L.F("SplitCompletedOfTracks", written, plan.Length));
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
