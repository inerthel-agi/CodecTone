using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using AudioConverter.Core.Models;
using AudioConverter.Core.Paths;
using AudioConverter.Core.Remix;
using AudioConverter.Core.Validation;
using AudioConverter.Desktop.Mvvm;

namespace AudioConverter.Desktop.ViewModels;

public sealed class RemixViewModel : WorkspaceBase
{
    private readonly HashSet<Task> analysisTasks = [];
    private CancellationTokenSource? remixAnalysisCancellation;
    private AudioAnalysis? remixAnalysis;
    private string remixSourcePath = "";
    private string remixSourceDetails = "";
    private double remixDurationSeconds;
    private int remixSampleRate = 44_100;
    private bool remixSourceHasCover;
    private double[] remixWaveformPeaks = [];
    private double remixPreviewStartSeconds;
    private double remixPlayheadSeconds = double.NaN;
    private AudioFormat remixFormat = AudioFormat.Mp3;
    private string remixBitrate = "192k";
    private string remixSampleRateOption = "Auto";
    private string remixBitDepth = "Auto";
    private string remixOutputFolder = "";
    private string remixSuffix = "_remix";
    private bool remixOverwrite;
    private RemixEffectKind selectedRemixEffectKind = RemixEffectKind.Bass;
    private string remixInspectorTab = "Export";
    private bool remixRackDirty;
    private string remixTitle = "";
    private string remixArtist = "";
    private string remixAlbum = "";
    private string remixAlbumArtist = "";
    private string remixGenre = "";
    private string remixDate = "";
    private string remixTrack = "";
    private string remixDisc = "";
    private string remixComment = "";
    private string remixCoverAction = "Keep";
    private string remixCoverPath = "";
    private RemixPresetCategory selectedRemixCategory = RemixPresetCategory.SpeedPitch;
    private RemixIntensity selectedRemixIntensity = RemixIntensity.Medium;
    private RemixPreset? selectedRemixPreset;
    private string remixAnalysisStatus = "Choose a song to enable adaptive presets.";
    private string remixAdaptiveExplanation = "Presets use safe static defaults until a song is analyzed.";

    public RemixViewModel(WorkspaceHost host) : base(host, "Remix", "No remix has been exported yet.")
    {
        ChooseRemixFileCommand = new AsyncRelayCommand(async () => { var path = Dialogs.ChooseAudioFile(); if (path is not null) await LoadRemixSourceAsync(path); }, () => !Host.IsBusy);
        SelectRemixCategoryCommand = new RelayCommand<string>(name => { if (Enum.TryParse<RemixPresetCategory>(name, out var category)) SelectedRemixCategory = category; });
        ApplyRemixPresetCommand = new RelayCommand<RemixPresetDefinition>(ApplyRemixPreset);
        ResetRemixRackCommand = new RelayCommand(ResetRemixRack);
        AddRemixEffectCommand = new RelayCommand(AddRemixEffect);
        RemoveRemixEffectCommand = new RelayCommand<RemixEffectViewModel>(effect => { RemixEffects.Remove(effect); OnRemixRackChanged(); });
        MoveRemixEffectUpCommand = new RelayCommand<RemixEffectViewModel>(effect => MoveRemixEffect(effect, -1));
        MoveRemixEffectDownCommand = new RelayCommand<RemixEffectViewModel>(effect => MoveRemixEffect(effect, 1));
        ChooseRemixOutputCommand = new RelayCommand(() => RemixOutputFolder = Dialogs.ChooseFolder(RemixOutputFolder) ?? RemixOutputFolder);
        ChooseRemixCoverCommand = new RelayCommand(() => { var path = Dialogs.ChooseCoverImage(); if (path is null) return; RemixCoverPath = path; RemixCoverAction = "Replace"; });
        RemoveRemixCoverCommand = new RelayCommand(() => { RemixCoverAction = "Remove"; RemixCoverPath = ""; });
        AddMetadataTagCommand = new RelayCommand(() => RemixCustomTags.Add(new MetadataTagViewModel()));
        RemoveMetadataTagCommand = new RelayCommand<MetadataTagViewModel>(tag => RemixCustomTags.Remove(tag));
        PreviewRemixCommand = new AsyncRelayCommand(() => PreviewRemixAsync(), () => !Host.IsBusy && RemixDurationSeconds > 0);
        PreviewOriginalCommand = new AsyncRelayCommand(() => PreviewRemixAsync(true), () => !Host.IsBusy && RemixDurationSeconds > 0);
        StopRemixCommand = new RelayCommand(() => { Host.CancelActiveOperation(); Host.StopPreview(); Host.Status = "Remix preview stopped."; });
        ExportRemixCommand = new AsyncRelayCommand(ExportRemixAsync, () => !Host.IsBusy && RemixDurationSeconds > 0);
    }

    public override ICommand PrimaryCommand => ExportRemixCommand;
    public override string PrimaryLabel => "Export remix";
    public AsyncRelayCommand ChooseRemixFileCommand { get; }
    public RelayCommand<string> SelectRemixCategoryCommand { get; }
    public RelayCommand<RemixPresetDefinition> ApplyRemixPresetCommand { get; }
    public RelayCommand ResetRemixRackCommand { get; }
    public RelayCommand AddRemixEffectCommand { get; }
    public RelayCommand<RemixEffectViewModel> RemoveRemixEffectCommand { get; }
    public RelayCommand<RemixEffectViewModel> MoveRemixEffectUpCommand { get; }
    public RelayCommand<RemixEffectViewModel> MoveRemixEffectDownCommand { get; }
    public RelayCommand ChooseRemixOutputCommand { get; }
    public RelayCommand ChooseRemixCoverCommand { get; }
    public RelayCommand RemoveRemixCoverCommand { get; }
    public RelayCommand AddMetadataTagCommand { get; }
    public RelayCommand<MetadataTagViewModel> RemoveMetadataTagCommand { get; }
    public AsyncRelayCommand PreviewRemixCommand { get; }
    public AsyncRelayCommand PreviewOriginalCommand { get; }
    public RelayCommand StopRemixCommand { get; }
    public AsyncRelayCommand ExportRemixCommand { get; }

    public ObservableCollection<RemixEffectViewModel> RemixEffects { get; } = [];
    public ObservableCollection<MetadataTagViewModel> RemixCustomTags { get; } = [];
    public IReadOnlyList<RemixEffectKind> RemixEffectKinds { get; } = Enum.GetValues<RemixEffectKind>();
    public IReadOnlyList<string> RemixCoverActions { get; } = ["Keep", "Replace", "Remove"];
    public IReadOnlyList<RemixIntensity> RemixIntensities { get; } = Enum.GetValues<RemixIntensity>();
    public IReadOnlyList<RemixPresetDefinition> FilteredRemixPresets => RemixPresetCatalog.For(SelectedRemixCategory);
    public RemixPresetCategory SelectedRemixCategory
    {
        get => selectedRemixCategory;
        private set
        {
            if (!Set(ref selectedRemixCategory, value)) return;
            Raise(nameof(FilteredRemixPresets));
            Raise(nameof(SpeedPitchCategoryState)); Raise(nameof(BassPunchCategoryState)); Raise(nameof(AtmosphereCategoryState));
            Raise(nameof(VocalClarityCategoryState)); Raise(nameof(ColorTextureCategoryState)); Raise(nameof(MasteringCategoryState));
        }
    }
    public RemixIntensity SelectedRemixIntensity
    {
        get => selectedRemixIntensity;
        set
        {
            if (selectedRemixIntensity == value) return;
            if (selectedRemixPreset is not null && remixRackDirty && RemixEffects.Count > 0
                && !Dialogs.Confirm("Changing intensity replaces your edited rack. Continue?"))
            {
                Raise(nameof(SelectedRemixIntensity));
                return;
            }
            if (!Set(ref selectedRemixIntensity, value) || selectedRemixPreset is not { } preset) return;
            ApplyRemixPresetInternal(preset);
        }
    }
    public string RemixAnalysisStatus { get => remixAnalysisStatus; private set => Set(ref remixAnalysisStatus, value); }
    public string RemixAdaptiveExplanation { get => remixAdaptiveExplanation; private set => Set(ref remixAdaptiveExplanation, value); }
    public string RemixAdaptiveLabel => remixAnalysis is null ? "STATIC" : "ADAPTIVE";
    public bool IsExtremeAudioActive => RemixEffects.Any(effect => effect.Enabled && effect.Kind is RemixEffectKind.Distortion or RemixEffectKind.BitCrusher);
    public string SpeedPitchCategoryState => CategoryState(RemixPresetCategory.SpeedPitch);
    public string BassPunchCategoryState => CategoryState(RemixPresetCategory.BassPunch);
    public string AtmosphereCategoryState => CategoryState(RemixPresetCategory.Atmosphere);
    public string VocalClarityCategoryState => CategoryState(RemixPresetCategory.VocalClarity);
    public string ColorTextureCategoryState => CategoryState(RemixPresetCategory.ColorTexture);
    public string MasteringCategoryState => CategoryState(RemixPresetCategory.Mastering);
    public string RemixSourcePath { get => remixSourcePath; private set { if (Set(ref remixSourcePath, value)) RaiseCommands(); } }
    public string RemixSourceDetails { get => remixSourceDetails; private set => Set(ref remixSourceDetails, value); }
    public double RemixDurationSeconds { get => remixDurationSeconds; private set { if (Set(ref remixDurationSeconds, value)) { Raise(nameof(RemixOutputDuration)); RaiseCommands(); } } }
    public double RemixOutputDuration
    {
        get
        {
            if (RemixDurationSeconds <= 0) return 0;
            var rate = TempoRate(BuildRemixRack());
            return double.IsFinite(rate) && rate > 0 ? RemixDurationSeconds / rate : RemixDurationSeconds;
        }
    }
    public double[] RemixWaveformPeaks { get => remixWaveformPeaks; private set => Set(ref remixWaveformPeaks, value); }
    public double RemixPreviewStartSeconds { get => remixPreviewStartSeconds; set { if (Set(ref remixPreviewStartSeconds, Math.Clamp(value, 0, Math.Max(0, RemixDurationSeconds - 1)))) Raise(nameof(RemixPreviewEndSeconds)); } }
    public double RemixPreviewEndSeconds { get => Math.Min(RemixDurationSeconds, RemixPreviewStartSeconds + 20); set => RemixPreviewStartSeconds = Math.Max(0, value - 20); }
    public double RemixPlayheadSeconds { get => remixPlayheadSeconds; private set => Set(ref remixPlayheadSeconds, value); }
    /// <summary>Forwards to the shared preview volume, which applies live and never to exports.</summary>
    public double PreviewVolume { get => Host.PreviewVolume; set { Host.PreviewVolume = value; Raise(); } }
    public AudioFormat RemixFormat { get => remixFormat; set { if (Set(ref remixFormat, value)) { Raise(nameof(IsRemixLossy)); Raise(nameof(IsRemixLossless)); } } }
    public bool IsRemixLossy => RemixFormat.IsLossy();
    public bool IsRemixLossless => !IsRemixLossy;
    public string RemixBitrate { get => remixBitrate; set => Set(ref remixBitrate, value); }
    public string RemixSampleRateOption { get => remixSampleRateOption; set => Set(ref remixSampleRateOption, value); }
    public string RemixBitDepth { get => remixBitDepth; set => Set(ref remixBitDepth, value); }
    public string RemixOutputFolder { get => remixOutputFolder; set => Set(ref remixOutputFolder, value); }
    public string RemixSuffix { get => remixSuffix; set => Set(ref remixSuffix, value); }
    public bool RemixOverwrite { get => remixOverwrite; set => Set(ref remixOverwrite, value); }
    public RemixEffectKind SelectedRemixEffectKind { get => selectedRemixEffectKind; set => Set(ref selectedRemixEffectKind, value); }
    public string RemixInspectorTab { get => remixInspectorTab; set { if (Set(ref remixInspectorTab, value)) { Raise(nameof(IsRemixExportTab)); Raise(nameof(IsRemixMetadataTab)); } } }
    public bool IsRemixExportTab => RemixInspectorTab == "Export";
    public bool IsRemixMetadataTab => RemixInspectorTab == "Metadata";
    public string RemixTitle { get => remixTitle; set => Set(ref remixTitle, value); }
    public string RemixArtist { get => remixArtist; set => Set(ref remixArtist, value); }
    public string RemixAlbum { get => remixAlbum; set => Set(ref remixAlbum, value); }
    public string RemixAlbumArtist { get => remixAlbumArtist; set => Set(ref remixAlbumArtist, value); }
    public string RemixGenre { get => remixGenre; set => Set(ref remixGenre, value); }
    public string RemixDate { get => remixDate; set => Set(ref remixDate, value); }
    public string RemixTrack { get => remixTrack; set => Set(ref remixTrack, value); }
    public string RemixDisc { get => remixDisc; set => Set(ref remixDisc, value); }
    public string RemixComment { get => remixComment; set => Set(ref remixComment, value); }
    public string RemixCoverAction { get => remixCoverAction; set => Set(ref remixCoverAction, value); }
    public string RemixCoverPath { get => remixCoverPath; set => Set(ref remixCoverPath, value); }

    public override async Task AcceptDropAsync(IReadOnlyList<string> paths)
    {
        if (paths.FirstOrDefault(File.Exists) is { } path) await LoadRemixSourceAsync(path);
    }

    public override async Task ShutdownAsync()
    {
        remixAnalysisCancellation?.Cancel();
        await Task.WhenAll(analysisTasks.ToArray());
        remixAnalysisCancellation?.Dispose();
    }

    protected override void RaiseCommands()
    {
        ChooseRemixFileCommand.RaiseCanExecuteChanged(); PreviewRemixCommand.RaiseCanExecuteChanged(); PreviewOriginalCommand.RaiseCanExecuteChanged(); ExportRemixCommand.RaiseCanExecuteChanged();
    }

    private string CategoryState(RemixPresetCategory category) => SelectedRemixCategory == category ? "Active" : "Inactive";
    private static double TempoRate(IEnumerable<RemixEffect> rack) => rack.OfType<TempoPitchEffect>().FirstOrDefault(effect => effect.Enabled)?.Rate ?? 1;

    private async Task LoadRemixSourceAsync(string path)
    {
        remixAnalysisCancellation?.Cancel();
        remixAnalysisCancellation?.Dispose();
        remixAnalysisCancellation = null;
        remixAnalysis = null;
        Raise(nameof(RemixAdaptiveLabel));
        if (selectedRemixPreset is { } selectedPreset && !remixRackDirty) ApplyRemixPresetInternal(selectedPreset);
        RemixAnalysisStatus = "Reading source before adaptive analysis…";
        await RunBusyAsync(async () =>
        {
            Host.EnsureFfmpeg();
            _ = AudioFormats.FromPath(path);
            Host.Status = "Reading remix source and waveform…";
            var info = await Host.Probe!.ProbeAsync(path, ActiveOperationToken);
            var peaks = await Host.Audio!.ExtractWaveformAsync(path, 900, info.DurationSeconds, ActiveOperationToken);
            RemixSourcePath = path;
            RemixSourceDetails = WorkspaceHost.DescribeSource(info);
            RemixDurationSeconds = info.DurationSeconds;
            remixSampleRate = info.SampleRate ?? 44_100;
            remixSourceHasCover = info.HasCoverArt;
            RemixWaveformPeaks = peaks;
            RemixPreviewStartSeconds = 0;
            RemixFormat = AudioFormats.FromPath(path);
            RemixOutputFolder = Path.GetDirectoryName(path) ?? "";
            LoadRemixMetadata(info.Tags);
            UpdateOperationProgress(1, $"Remix source ready: {Path.GetFileName(path)}");
        });
        if (!Host.IsClosing && RemixSourcePath == path && Host.Analyzer is not null) StartRemixAnalysis(path);
    }

    private void ApplyRemixPreset(RemixPresetDefinition definition)
    {
        if (remixRackDirty && RemixEffects.Count > 0
            && !Dialogs.Confirm("Applying a preset will replace the current effect rack. Continue?")) return;
        if (definition.Preset == RemixPreset.Earrape
            && !Dialogs.Confirm("HEARING WARNING: EARRAPE creates extreme distortion and loudness. Lower your headphone or speaker volume before Preview or Export. High-volume listening can permanently damage hearing. Apply this preset?")) return;
        selectedRemixPreset = definition.Preset;
        ApplyRemixPresetInternal(definition.Preset);
    }

    private void ApplyRemixPresetInternal(RemixPreset preset) => LoadRack(RemixPresetFactory.CreateAdaptive(preset, SelectedRemixIntensity, remixAnalysis));

    private void LoadRack(AdaptiveRemixRack result)
    {
        RemixEffects.Clear();
        foreach (var effect in result.Rack)
            RemixEffects.Add(RemixEffectViewModel.From(effect, OnRemixRackChanged));
        RemixAdaptiveExplanation = result.Explanation;
        remixRackDirty = false;
        Raise(nameof(IsExtremeAudioActive));
        Raise(nameof(RemixOutputDuration));
    }

    private void ResetRemixRack()
    {
        RemixEffects.Clear();
        selectedRemixPreset = null;
        remixRackDirty = false;
        RemixAdaptiveExplanation = "Custom rack selected. Add and order effects manually.";
        Raise(nameof(IsExtremeAudioActive));
        Raise(nameof(RemixOutputDuration));
    }

    private void StartRemixAnalysis(string path)
    {
        remixAnalysisCancellation = new CancellationTokenSource();
        RemixAnalysisStatus = "Analyzing the full song locally…";
        var task = AnalyzeRemixSourceAsync(path, remixAnalysisCancellation.Token);
        analysisTasks.Add(task);
        _ = ObserveAnalysisAsync(task);
    }

    private async Task ObserveAnalysisAsync(Task task)
    {
        try { await task; }
        finally { analysisTasks.Remove(task); }
    }

    private async Task AnalyzeRemixSourceAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var analysis = await Host.Analyzer!.AnalyzeAsync(path, cancellationToken);
            if (cancellationToken.IsCancellationRequested || RemixSourcePath != path) return;
            remixAnalysis = analysis;
            Raise(nameof(RemixAdaptiveLabel));
            RemixAnalysisStatus = $"Analysis ready · {analysis.IntegratedLufs:0.0} LUFS · {analysis.LoudnessRange:0.0} LU range · width {analysis.StereoWidth:0.00}";
            if (selectedRemixPreset is { } preset && !remixRackDirty) ApplyRemixPresetInternal(preset);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (RemixSourcePath != path) return;
            remixAnalysis = null;
            Raise(nameof(RemixAdaptiveLabel));
            RemixAnalysisStatus = $"Adaptive analysis unavailable · static presets remain usable ({error.Message})";
        }
    }

    private void AddRemixEffect()
    {
        if (RemixEffects.Count >= 16) { Dialogs.Error("A remix rack supports at most 16 effects."); return; }
        if (SelectedRemixEffectKind is RemixEffectKind.TempoPitch or RemixEffectKind.LoudnessNormalize or RemixEffectKind.FadeIn or RemixEffectKind.FadeOut
            && RemixEffects.Any(effect => effect.Kind == SelectedRemixEffectKind))
        {
            Dialogs.Error($"{SelectedRemixEffectKind} can appear only once.");
            return;
        }
        var viewModel = new RemixEffectViewModel(SelectedRemixEffectKind, OnRemixRackChanged);
        var firstFade = RemixEffects.ToList().FindIndex(effect => effect.Kind is RemixEffectKind.FadeIn or RemixEffectKind.FadeOut);
        if (SelectedRemixEffectKind is RemixEffectKind.FadeIn or RemixEffectKind.FadeOut || firstFade < 0) RemixEffects.Add(viewModel);
        else RemixEffects.Insert(firstFade, viewModel);
        OnRemixRackChanged();
    }

    private void MoveRemixEffect(RemixEffectViewModel effect, int offset)
    {
        var index = RemixEffects.IndexOf(effect);
        var destination = index + offset;
        if (index < 0 || destination < 0 || destination >= RemixEffects.Count) return;
        RemixEffects.Move(index, destination);
        OnRemixRackChanged();
    }

    private void OnRemixRackChanged()
    {
        remixRackDirty = true;
        Raise(nameof(IsExtremeAudioActive));
        Raise(nameof(RemixOutputDuration));
    }

    private async Task PreviewRemixAsync(bool original = false)
    {
        await RunBusyAsync(async () =>
        {
            Host.EnsureFfmpeg();
            var rack = original ? Array.Empty<RemixEffect>() : BuildRemixRack();
            RemixRackValidator.Validate(rack, RemixDurationSeconds);
            Host.StopPreview();
            var progress = new Progress<double>(fraction => UpdateOperationProgress(fraction, "Rendering 20-second remix preview…"));
            var output = await Host.Remix!.RenderPreviewAsync(RemixSourcePath, rack, remixSampleRate, RemixDurationSeconds, RemixPreviewStartSeconds, progress, ActiveOperationToken);
            Host.PlayPreview(output, position => RemixPlayheadSeconds = position, RemixPreviewStartSeconds, RemixPreviewEndSeconds - RemixPreviewStartSeconds, TempoRate(rack));
            UpdateOperationProgress(1, original ? "Playing original excerpt." : "Playing remix excerpt.");
        });
    }

    private async Task ExportRemixAsync()
    {
        await RunBusyAsync(async () =>
        {
            var rack = BuildRemixRack();
            RemixRackValidator.Validate(rack, RemixDurationSeconds);
            var metadata = BuildRemixMetadata();
            RemixMetadataValidator.Validate(metadata);
            var hasRequestedCover = metadata.CoverAction == CoverArtAction.Replace || metadata.CoverAction == CoverArtAction.Keep && remixSourceHasCover;
            if (hasRequestedCover
                && RemixFormat is not (AudioFormat.Mp3 or AudioFormat.M4a or AudioFormat.Flac)
                && !Dialogs.Confirm("The selected output format does not support embedded cover art here. Export without the cover?"))
            {
                Host.Status = "Remix export cancelled.";
                Host.TimingText = "";
                return;
            }

            var directory = string.IsNullOrWhiteSpace(RemixOutputFolder) ? Path.GetDirectoryName(RemixSourcePath)! : RemixOutputFolder;
            var output = OutputPathBuilder.Build(RemixSourcePath, directory, RemixFormat, RemixSuffix);
            var encoding = new ConversionOptions(
                RemixFormat,
                RemixFormat.IsLossy() ? RemixBitrate : null,
                RemixFormat.IsLossy() ? null : ParseOptionalInt(RemixSampleRateOption),
                RemixFormat.IsLossy() ? null : ParseOptionalInt(RemixBitDepth),
                PreserveMetadata: true,
                Overwrite: RemixOverwrite);
            var progress = new Progress<double>(fraction => UpdateOperationProgress(fraction, "Exporting remix…"));
            await Host.Remix!.ExportAsync(RemixSourcePath, output, rack, encoding, metadata, remixSampleRate, RemixDurationSeconds, remixSourceHasCover, progress, ActiveOperationToken);
            Report = $"Export completed\nSource: {RemixSourcePath}\nOutput: {output}\nEffects: {rack.Count(effect => effect.Enabled)}\nDuration: {TimestampParser.Format(RemixOutputDuration)}";
            Host.SetLastOutputFolder(this, directory);
            UpdateOperationProgress(1, $"Remix exported: {Path.GetFileName(output)}");
        });
    }

    private IReadOnlyList<RemixEffect> BuildRemixRack() => RemixEffects.Select(effect => effect.ToModel()).ToArray();

    private RemixMetadata BuildRemixMetadata()
    {
        var custom = RemixCustomTags.ToDictionary(tag => tag.Key, tag => tag.Value, StringComparer.OrdinalIgnoreCase);
        return new RemixMetadata(RemixTitle, RemixArtist, RemixAlbum, RemixAlbumArtist, RemixGenre, RemixDate, RemixTrack, RemixDisc, RemixComment, custom,
            Enum.Parse<CoverArtAction>(RemixCoverAction), string.IsNullOrWhiteSpace(RemixCoverPath) ? null : RemixCoverPath);
    }

    private void LoadRemixMetadata(IReadOnlyDictionary<string, string> tags)
    {
        string Get(params string[] keys) => keys.Select(key => tags.TryGetValue(key, out var value) ? value : null).FirstOrDefault(value => value is not null) ?? "";
        RemixTitle = Get("title");
        RemixArtist = Get("artist");
        RemixAlbum = Get("album");
        RemixAlbumArtist = Get("album_artist", "albumartist");
        RemixGenre = Get("genre");
        RemixDate = Get("date", "year");
        RemixTrack = Get("track");
        RemixDisc = Get("disc");
        RemixComment = Get("comment", "description");
        RemixCoverAction = "Keep";
        RemixCoverPath = "";
        var standard = new HashSet<string>(["title", "artist", "album", "album_artist", "albumartist", "genre", "date", "year", "track", "disc", "comment", "description"], StringComparer.OrdinalIgnoreCase);
        RemixCustomTags.Clear();
        foreach (var pair in tags.Where(pair => !standard.Contains(pair.Key)))
            RemixCustomTags.Add(new MetadataTagViewModel { Key = pair.Key, Value = pair.Value });
    }
}
