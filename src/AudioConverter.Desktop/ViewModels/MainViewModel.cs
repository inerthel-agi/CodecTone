using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using AudioConverter.Core.Compression;
using AudioConverter.Core.Artwork;
using AudioConverter.Core.Models;
using AudioConverter.Core.Paths;
using AudioConverter.Core.Progress;
using AudioConverter.Core.Remix;
using AudioConverter.Core.Validation;
using AudioConverter.Desktop.Mvvm;
using AudioConverter.Desktop.Services;
using AudioConverter.Infrastructure.Audio;
using AudioConverter.Infrastructure.Ffmpeg;
using AudioConverter.Infrastructure.Storage;
using AudioConverter.Infrastructure.Shell;
using AudioConverter.Infrastructure.Remix;
using AudioConverter.Infrastructure.Artwork;

namespace AudioConverter.Desktop.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly IDialogService dialogs;
    private readonly JsonSettingsStore settingsStore = new();
    private readonly WavePreviewPlayer previewPlayer = new();
    private readonly LocalDiagnosticLog diagnosticLog = new();
    private TaskCompletionSource? operationFinished;
    private readonly HashSet<Task> analysisTasks = [];
    private bool closing;
    private string operationWorkspace = "Convert";
    private double previewVolume = 25;
    private ArtworkPlannedAlbum? selectedArtworkAlbum;
    private System.Windows.Media.Imaging.BitmapImage? artworkPreview;
    private string artworkPreviewInfo = "Select an album and preview its cover.";
    private readonly string artworkPreviewDirectory = Path.Combine(AppPaths.Root, "artwork-preview", Guid.NewGuid().ToString("N"));
    private readonly Stopwatch operationTimer = new();
    private FfmpegTools? tools;
    private AudioProcessingService? audio;
    private FfprobeService? probe;
    private RemixProcessingService? remix;
    private ArtworkExtractionService? artworkExtractor;
    private AdaptiveAudioAnalyzer? remixAnalyzer;
    private CancellationTokenSource? remixAnalysisCancellation;
    private CancellationTokenSource? activeOperationCancellation;
    private bool disposed;
    private AudioAnalysis? remixAnalysis;
    private string activeWorkspace = "Convert";
    private bool isBusy;
    private bool ffmpegMissing;
    private string theme = "Oled";
    private string sourcePath = "";
    private string outputFolder = "";
    private string suffix = "";
    private AudioFormat selectedFormat = AudioFormat.Mp3;
    private string bitrate = "192k";
    private string sampleRate = "Auto";
    private string bitDepth = "Auto";
    private bool preserveMetadata = true;
    private bool overwrite;
    private double progress;
    private string status = "Ready";
    private string report = "No conversion has run yet.";
    private string timingText = "";
    private string cutSourcePath = "";
    private string cutOutputFolder = "";
    private string cutSuffix = "_trimmed";
    private AudioFormat cutFormat = AudioFormat.Flac;
    private double durationSeconds;
    private double startSeconds;
    private double endSeconds;
    private bool fadeInEnabled;
    private bool fadeOutEnabled;
    private double fadeInSeconds = 1;
    private double fadeOutSeconds = 1;
    private double[] waveformPeaks = [];
    private string compressionSourcePath = "";
    private string compressionOutputFolder = "";
    private string compressionSuffix = "";
    private AudioFormat compressionFormat = AudioFormat.Mp3;
    private string compressionProfile = "High fidelity";
    private string compressionTargetTotalMb = "100";
    private bool compressionPreserveMetadata = true;
    private bool compressionOptimizeArtwork = true;
    private bool compressionOverwrite;
    private string compressionEstimate = "Choose a source, then analyze it to estimate the output size.";
    private string compressionReport = "No compression has run yet.";
    private string artworkSourcePath = "";
    private string artworkOutputFolder = "";
    private ArtworkOutputFormat artworkOutputFormat = ArtworkOutputFormat.Original;
    private bool artworkLimitDimensions;
    private string artworkMaximumDimension = "1200";
    private string artworkEstimate = "Choose a source, then analyze it to find embedded album artwork.";
    private string artworkReport = "No artwork extraction has run yet.";
    private string remixSourcePath = "";
    private double remixDurationSeconds;
    private int remixSampleRate = 44_100;
    private bool remixSourceHasCover;
    private double[] remixWaveformPeaks = [];
    private double remixPreviewStartSeconds;
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
    private string remixReport = "No remix has been exported yet.";
    private RemixPresetCategory selectedRemixCategory = RemixPresetCategory.SpeedPitch;
    private RemixIntensity selectedRemixIntensity = RemixIntensity.Medium;
    private RemixPreset? selectedRemixPreset;
    private string remixAnalysisStatus = "Choose a song to enable adaptive presets.";
    private string remixAdaptiveExplanation = "Presets use safe static defaults until a song is analyzed.";

    public MainViewModel(IDialogService dialogs)
    {
        this.dialogs = dialogs;
        ShowConvertCommand = new RelayCommand(() => SetWorkspace("Convert"));
        ShowCutCommand = new RelayCommand(() => SetWorkspace("Cut"));
        ShowCompressCommand = new RelayCommand(() => SetWorkspace("Compress"));
        ShowArtworkCommand = new RelayCommand(() => SetWorkspace("Artwork"));
        ShowAboutCommand = new RelayCommand(() => SetWorkspace("About"));
        ShowRemixCommand = new RelayCommand(() => SetWorkspace("Remix"));
        ToggleThemeCommand = new AsyncRelayCommand(ToggleThemeAsync);
        ChooseFileCommand = new RelayCommand(ChooseFile);
        ChooseFolderCommand = new RelayCommand(ChooseFolder);
        ChooseOutputCommand = new RelayCommand(ChooseOutputFolder);
        ConvertCommand = new AsyncRelayCommand(ConvertAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(SourcePath));
        InstallFfmpegCommand = new AsyncRelayCommand(InstallFfmpegAsync, () => !IsBusy);
        ChooseCutFileCommand = new AsyncRelayCommand(ChooseCutFileAsync, () => !IsBusy);
        ChooseCutOutputCommand = new RelayCommand(() => CutOutputFolder = dialogs.ChooseFolder(CutOutputFolder) ?? CutOutputFolder);
        CutCommand = new AsyncRelayCommand(CutAsync, () => !IsBusy && DurationSeconds > 0);
        PreviewCommand = new AsyncRelayCommand(PreviewAsync, () => !IsBusy && DurationSeconds > 0);
        StopPreviewCommand = new RelayCommand(() => { CancelActiveOperation(); previewPlayer.Stop(); Status = "Preview stopped."; });
        ChooseCompressionFileCommand = new AsyncRelayCommand(ChooseCompressionFileAsync, () => !IsBusy);
        ChooseCompressionFolderCommand = new AsyncRelayCommand(ChooseCompressionFolderAsync, () => !IsBusy);
        ChooseCompressionOutputCommand = new RelayCommand(() => CompressionOutputFolder = dialogs.ChooseFolder(CompressionOutputFolder) ?? CompressionOutputFolder);
        AnalyzeCompressionCommand = new AsyncRelayCommand(AnalyzeCompressionAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(CompressionSourcePath));
        CompressCommand = new AsyncRelayCommand(CompressAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(CompressionSourcePath));
        ChooseArtworkFileCommand = new AsyncRelayCommand(ChooseArtworkFileAsync, () => !IsBusy);
        ChooseArtworkFolderCommand = new AsyncRelayCommand(ChooseArtworkFolderAsync, () => !IsBusy);
        ChooseArtworkOutputCommand = new RelayCommand(() => ArtworkOutputFolder = dialogs.ChooseFolder(ArtworkOutputFolder) ?? ArtworkOutputFolder);
        AnalyzeArtworkCommand = new AsyncRelayCommand(AnalyzeArtworkAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(ArtworkSourcePath));
        ExtractArtworkCommand = new AsyncRelayCommand(ExtractArtworkAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(ArtworkSourcePath));
        OpenGitHubCommand = new RelayCommand(OpenGitHub);
        ChooseRemixFileCommand = new AsyncRelayCommand(ChooseRemixFileAsync, () => !IsBusy);
        SelectRemixCategoryCommand = new RelayCommand<string>(SelectRemixCategory);
        ApplyRemixPresetCommand = new RelayCommand<RemixPresetDefinition>(ApplyRemixPreset);
        ResetRemixRackCommand = new RelayCommand(ResetRemixRack);
        AddRemixEffectCommand = new RelayCommand(AddRemixEffect);
        RemoveRemixEffectCommand = new RelayCommand<RemixEffectViewModel>(RemoveRemixEffect);
        MoveRemixEffectUpCommand = new RelayCommand<RemixEffectViewModel>(effect => MoveRemixEffect(effect, -1));
        MoveRemixEffectDownCommand = new RelayCommand<RemixEffectViewModel>(effect => MoveRemixEffect(effect, 1));
        ShowRemixInspectorCommand = new RelayCommand<string>(tab => RemixInspectorTab = tab);
        ChooseRemixOutputCommand = new RelayCommand(() => RemixOutputFolder = dialogs.ChooseFolder(RemixOutputFolder) ?? RemixOutputFolder);
        ChooseRemixCoverCommand = new RelayCommand(ChooseRemixCover);
        RemoveRemixCoverCommand = new RelayCommand(() => { RemixCoverAction = "Remove"; RemixCoverPath = ""; });
        AddMetadataTagCommand = new RelayCommand(() => RemixCustomTags.Add(new MetadataTagViewModel()));
        RemoveMetadataTagCommand = new RelayCommand<MetadataTagViewModel>(tag => RemixCustomTags.Remove(tag));
        PreviewRemixCommand = new AsyncRelayCommand(() => PreviewRemixAsync(), () => !IsBusy && RemixDurationSeconds > 0);
        StopRemixCommand = new RelayCommand(() => { CancelActiveOperation(); previewPlayer.Stop(); Status = "Remix preview stopped."; });
        CancelOperationCommand = new RelayCommand(CancelActiveOperation);
        ExportRemixCommand = new AsyncRelayCommand(ExportRemixAsync, () => !IsBusy && RemixDurationSeconds > 0);
        PreviewOriginalCommand = new AsyncRelayCommand(() => PreviewRemixAsync(true), () => !IsBusy && RemixDurationSeconds > 0);
        PreviewArtworkCommand = new AsyncRelayCommand(PreviewArtworkAsync, () => !IsBusy && SelectedArtworkAlbum is not null);
        SaveReportCommand = new AsyncRelayCommand(SaveReportAsync, () => !IsBusy);
        OpenDiagnosticsCommand = new RelayCommand(OpenDiagnostics);
        OpenArtworkFolderCommand = new RelayCommand(OpenArtworkFolder);
    }

    public IReadOnlyList<AudioFormat> Formats => AudioFormats.All;
    public IReadOnlyList<string> Bitrates { get; } = ["96k", "128k", "160k", "192k", "256k", "320k"];
    public IReadOnlyList<string> SampleRates { get; } = ["Auto", "44100", "48000", "88200", "96000", "192000"];
    public IReadOnlyList<string> BitDepths { get; } = ["Auto", "16", "24", "32"];
    public bool IsConvertPage => activeWorkspace == "Convert";
    public bool IsCutPage => activeWorkspace == "Cut";
    public bool IsCompressionPage => activeWorkspace == "Compress";
    public bool IsArtworkPage => activeWorkspace == "Artwork";
    public bool IsAboutPage => activeWorkspace == "About";
    public bool IsRemixPage => activeWorkspace == "Remix";
    public bool IsToolWorkspace => !IsAboutPage;
    public string ConvertTabState => IsConvertPage ? "Active" : "Inactive";
    public string CutTabState => IsCutPage ? "Active" : "Inactive";
    public string CompressionTabState => IsCompressionPage ? "Active" : "Inactive";
    public string ArtworkTabState => IsArtworkPage ? "Active" : "Inactive";
    public string AboutTabState => IsAboutPage ? "Active" : "Inactive";
    public string RemixTabState => IsRemixPage ? "Active" : "Inactive";
    public string ProductVersion => typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.2.1";
    public bool IsBusy { get => isBusy; private set { if (Set(ref isBusy, value)) { Raise(nameof(IsWorkspaceEnabled)); RaiseCommands(); } } }
    public bool IsWorkspaceEnabled => !IsBusy && !closing;
    public bool FfmpegMissing { get => ffmpegMissing; private set => Set(ref ffmpegMissing, value); }
    public string Theme { get => theme; private set { if (Set(ref theme, value)) Raise(nameof(ThemeButtonText)); } }
    public string ThemeButtonText => Theme.Equals("Oled", StringComparison.OrdinalIgnoreCase) ? "WHITE" : "OLED";
    public string SourcePath { get => sourcePath; set { if (Set(ref sourcePath, value)) RaiseCommands(); } }
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
    public double Progress { get => progress; private set => Set(ref progress, value); }
    public string Status { get => status; private set => Set(ref status, value); }
    public string Report { get => report; private set => Set(ref report, value); }
    public string TimingText { get => timingText; private set => Set(ref timingText, value); }
    public string CutSourcePath { get => cutSourcePath; set => Set(ref cutSourcePath, value); }
    public string CutOutputFolder { get => cutOutputFolder; set => Set(ref cutOutputFolder, value); }
    public string CutSuffix { get => cutSuffix; set => Set(ref cutSuffix, value); }
    public AudioFormat CutFormat { get => cutFormat; set => Set(ref cutFormat, value); }
    public double DurationSeconds { get => durationSeconds; private set { if (Set(ref durationSeconds, value)) RaiseCommands(); } }
    public double StartSeconds { get => startSeconds; set { var next = Math.Clamp(value, 0, Math.Max(0, EndSeconds - 0.001)); Set(ref startSeconds, next); Raise(nameof(SelectionDuration)); } }
    public double EndSeconds { get => endSeconds; set { var next = Math.Clamp(value, Math.Min(DurationSeconds, StartSeconds + 0.001), DurationSeconds); Set(ref endSeconds, next); Raise(nameof(SelectionDuration)); } }
    public double SelectionDuration => Math.Max(0, EndSeconds - StartSeconds);
    public bool FadeInEnabled { get => fadeInEnabled; set => Set(ref fadeInEnabled, value); }
    public bool FadeOutEnabled { get => fadeOutEnabled; set => Set(ref fadeOutEnabled, value); }
    public double FadeInSeconds { get => fadeInSeconds; set => Set(ref fadeInSeconds, value); }
    public double FadeOutSeconds { get => fadeOutSeconds; set => Set(ref fadeOutSeconds, value); }
    public double[] WaveformPeaks { get => waveformPeaks; private set => Set(ref waveformPeaks, value); }
    public IReadOnlyList<AudioFormat> CompressionFormats { get; } = [AudioFormat.Mp3, AudioFormat.Flac, AudioFormat.Ogg, AudioFormat.Aac, AudioFormat.M4a];
    public IReadOnlyList<string> CompressionProfiles => CompressionFormat == AudioFormat.Flac
        ? ["High fidelity", "Balanced", "Maximum reduction"]
        : ["High fidelity", "Balanced", "Maximum reduction", "Target total size"];
    public string CompressionSourcePath { get => compressionSourcePath; set { if (Set(ref compressionSourcePath, value)) RaiseCommands(); } }
    public string CompressionOutputFolder { get => compressionOutputFolder; set => Set(ref compressionOutputFolder, value); }
    public string CompressionSuffix { get => compressionSuffix; set => Set(ref compressionSuffix, value); }
    public AudioFormat CompressionFormat
    {
        get => compressionFormat;
        set
        {
            if (!Set(ref compressionFormat, value)) return;
            if (value == AudioFormat.Flac && SelectedCompressionProfile == "Target total size") SelectedCompressionProfile = "High fidelity";
            Raise(nameof(CompressionProfiles));
            Raise(nameof(IsTargetSizeCompression));
        }
    }
    public string SelectedCompressionProfile { get => compressionProfile; set { if (Set(ref compressionProfile, value)) Raise(nameof(IsTargetSizeCompression)); } }
    public bool IsTargetSizeCompression => SelectedCompressionProfile == "Target total size";
    public string CompressionTargetTotalMb { get => compressionTargetTotalMb; set => Set(ref compressionTargetTotalMb, value); }
    public bool CompressionPreserveMetadata { get => compressionPreserveMetadata; set => Set(ref compressionPreserveMetadata, value); }
    public bool CompressionOptimizeArtwork { get => compressionOptimizeArtwork; set => Set(ref compressionOptimizeArtwork, value); }
    public bool CompressionOverwrite { get => compressionOverwrite; set => Set(ref compressionOverwrite, value); }
    public string CompressionEstimate { get => compressionEstimate; private set => Set(ref compressionEstimate, value); }
    public string CompressionReport { get => compressionReport; private set => Set(ref compressionReport, value); }
    public IReadOnlyList<ArtworkOutputFormat> ArtworkOutputFormats { get; } = Enum.GetValues<ArtworkOutputFormat>();
    public string ArtworkSourcePath { get => artworkSourcePath; set { if (Set(ref artworkSourcePath, value)) { RaiseCommands(); } } }
    public string ArtworkOutputFolder { get => artworkOutputFolder; set => Set(ref artworkOutputFolder, value); }
    public ArtworkOutputFormat ArtworkOutputFormat
    {
        get => artworkOutputFormat;
        set { if (Set(ref artworkOutputFormat, value)) Raise(nameof(IsArtworkConversion)); }
    }
    public bool IsArtworkConversion => ArtworkOutputFormat != ArtworkOutputFormat.Original;
    public bool ArtworkLimitDimensions { get => artworkLimitDimensions; set => Set(ref artworkLimitDimensions, value); }
    public string ArtworkMaximumDimension { get => artworkMaximumDimension; set => Set(ref artworkMaximumDimension, value); }
    public string ArtworkEstimate { get => artworkEstimate; private set => Set(ref artworkEstimate, value); }
    public string ArtworkReport { get => artworkReport; private set => Set(ref artworkReport, value); }
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
            RaiseCategoryStates();
        }
    }
    public RemixIntensity SelectedRemixIntensity
    {
        get => selectedRemixIntensity;
        set
        {
            if (selectedRemixIntensity == value) return;
            if (selectedRemixPreset is not null && remixRackDirty && RemixEffects.Count > 0
                && !dialogs.Confirm("Changing intensity replaces your edited rack. Continue?"))
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
    public string SpeedPitchCategoryState => SelectedRemixCategory == RemixPresetCategory.SpeedPitch ? "Active" : "Inactive";
    public string BassPunchCategoryState => SelectedRemixCategory == RemixPresetCategory.BassPunch ? "Active" : "Inactive";
    public string AtmosphereCategoryState => SelectedRemixCategory == RemixPresetCategory.Atmosphere ? "Active" : "Inactive";
    public string VocalClarityCategoryState => SelectedRemixCategory == RemixPresetCategory.VocalClarity ? "Active" : "Inactive";
    public string ColorTextureCategoryState => SelectedRemixCategory == RemixPresetCategory.ColorTexture ? "Active" : "Inactive";
    public string MasteringCategoryState => SelectedRemixCategory == RemixPresetCategory.Mastering ? "Active" : "Inactive";
    public string RemixSourcePath { get => remixSourcePath; set { if (Set(ref remixSourcePath, value)) RaiseCommands(); } }
    public double RemixDurationSeconds { get => remixDurationSeconds; private set { if (Set(ref remixDurationSeconds, value)) { Raise(nameof(RemixOutputDuration)); RaiseCommands(); } } }
    public double RemixOutputDuration
    {
        get
        {
            if (RemixDurationSeconds <= 0) return 0;
            var rate = BuildRemixRack().OfType<TempoPitchEffect>().FirstOrDefault(effect => effect.Enabled)?.Rate ?? 1;
            return double.IsFinite(rate) && rate > 0 ? RemixDurationSeconds / rate : RemixDurationSeconds;
        }
    }
    public double[] RemixWaveformPeaks { get => remixWaveformPeaks; private set => Set(ref remixWaveformPeaks, value); }
    public double RemixPreviewStartSeconds { get => remixPreviewStartSeconds; set { if (Set(ref remixPreviewStartSeconds, Math.Clamp(value, 0, Math.Max(0, RemixDurationSeconds - 1)))) Raise(nameof(RemixPreviewEndSeconds)); } }
    public double RemixPreviewEndSeconds { get => Math.Min(RemixDurationSeconds, RemixPreviewStartSeconds + 20); set => RemixPreviewStartSeconds = Math.Max(0, value - 20); }
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
    public string RemixReport { get => remixReport; private set => Set(ref remixReport, value); }

    public RelayCommand ShowConvertCommand { get; }
    public RelayCommand ShowCutCommand { get; }
    public RelayCommand ShowCompressCommand { get; }
    public RelayCommand ShowArtworkCommand { get; }
    public RelayCommand ShowAboutCommand { get; }
    public RelayCommand ShowRemixCommand { get; }
    public AsyncRelayCommand ToggleThemeCommand { get; }
    public RelayCommand ChooseFileCommand { get; }
    public RelayCommand ChooseFolderCommand { get; }
    public RelayCommand ChooseOutputCommand { get; }
    public AsyncRelayCommand ConvertCommand { get; }
    public AsyncRelayCommand InstallFfmpegCommand { get; }
    public AsyncRelayCommand ChooseCutFileCommand { get; }
    public RelayCommand ChooseCutOutputCommand { get; }
    public AsyncRelayCommand CutCommand { get; }
    public AsyncRelayCommand PreviewCommand { get; }
    public RelayCommand StopPreviewCommand { get; }
    public AsyncRelayCommand ChooseCompressionFileCommand { get; }
    public AsyncRelayCommand ChooseCompressionFolderCommand { get; }
    public RelayCommand ChooseCompressionOutputCommand { get; }
    public AsyncRelayCommand AnalyzeCompressionCommand { get; }
    public AsyncRelayCommand CompressCommand { get; }
    public AsyncRelayCommand ChooseArtworkFileCommand { get; }
    public AsyncRelayCommand ChooseArtworkFolderCommand { get; }
    public RelayCommand ChooseArtworkOutputCommand { get; }
    public AsyncRelayCommand AnalyzeArtworkCommand { get; }
    public AsyncRelayCommand ExtractArtworkCommand { get; }
    public RelayCommand OpenGitHubCommand { get; }
    public AsyncRelayCommand ChooseRemixFileCommand { get; }
    public RelayCommand<string> SelectRemixCategoryCommand { get; }
    public RelayCommand<RemixPresetDefinition> ApplyRemixPresetCommand { get; }
    public RelayCommand ResetRemixRackCommand { get; }
    public RelayCommand AddRemixEffectCommand { get; }
    public RelayCommand<RemixEffectViewModel> RemoveRemixEffectCommand { get; }
    public RelayCommand<RemixEffectViewModel> MoveRemixEffectUpCommand { get; }
    public RelayCommand<RemixEffectViewModel> MoveRemixEffectDownCommand { get; }
    public RelayCommand<string> ShowRemixInspectorCommand { get; }
    public RelayCommand ChooseRemixOutputCommand { get; }
    public RelayCommand ChooseRemixCoverCommand { get; }
    public RelayCommand RemoveRemixCoverCommand { get; }
    public RelayCommand AddMetadataTagCommand { get; }
    public RelayCommand<MetadataTagViewModel> RemoveMetadataTagCommand { get; }
    public AsyncRelayCommand PreviewRemixCommand { get; }
    public RelayCommand StopRemixCommand { get; }
    public AsyncRelayCommand ExportRemixCommand { get; }
    public RelayCommand CancelOperationCommand { get; }
    public AsyncRelayCommand PreviewOriginalCommand { get; }
    public AsyncRelayCommand PreviewArtworkCommand { get; }
    public AsyncRelayCommand SaveReportCommand { get; }
    public RelayCommand OpenDiagnosticsCommand { get; }
    public RelayCommand OpenArtworkFolderCommand { get; }
    public double PreviewVolume
    {
        get => previewVolume;
        set { if (double.IsFinite(value)) Set(ref previewVolume, Math.Clamp(value, 0, 100)); }
    }
    public ObservableCollection<ArtworkPlannedAlbum> ArtworkAlbums { get; } = [];
    public ArtworkPlannedAlbum? SelectedArtworkAlbum
    {
        get => selectedArtworkAlbum;
        set { if (Set(ref selectedArtworkAlbum, value)) { ArtworkPreview = null; ArtworkPreviewInfo = "Select Preview cover to load the image."; PreviewArtworkCommand.RaiseCanExecuteChanged(); } }
    }
    public System.Windows.Media.Imaging.BitmapImage? ArtworkPreview { get => artworkPreview; private set => Set(ref artworkPreview, value); }
    public string ArtworkPreviewInfo { get => artworkPreviewInfo; private set => Set(ref artworkPreviewInfo, value); }

    public async Task InitializeAsync()
    {
        var settings = await settingsStore.LoadAsync();
        if (closing) return;
        Theme = settings.Theme; OutputFolder = settings.LastOutputDirectory ?? ""; ThemeService.Apply(Theme);
        TryLocateFfmpeg();
    }

    public async Task AcceptDropAsync(IReadOnlyList<string> paths)
    {
        if (IsBusy || closing) return;
        var path = paths.FirstOrDefault(item => File.Exists(item) || Directory.Exists(item));
        if (path is null) return;
        if (IsConvertPage) SourcePath = path;
        else if (IsCutPage && File.Exists(path)) await LoadCutSourceAsync(path);
        else if (IsCompressionPage)
        {
            CompressionSourcePath = path;
            await AnalyzeCompressionAsync();
        }
        else if (IsArtworkPage)
        {
            ArtworkSourcePath = path;
            await AnalyzeArtworkAsync();
        }
        else if (IsRemixPage && File.Exists(path)) await LoadRemixSourceAsync(path);
    }

    private void SetWorkspace(string workspace)
    {
        if (!Set(ref activeWorkspace, workspace, nameof(activeWorkspace))) return;
        Raise(nameof(IsConvertPage));
        Raise(nameof(IsCutPage));
        Raise(nameof(IsCompressionPage));
        Raise(nameof(IsArtworkPage));
        Raise(nameof(IsAboutPage));
        Raise(nameof(IsRemixPage));
        Raise(nameof(IsToolWorkspace));
        Raise(nameof(ConvertTabState));
        Raise(nameof(CutTabState));
        Raise(nameof(CompressionTabState));
        Raise(nameof(ArtworkTabState));
        Raise(nameof(AboutTabState));
        Raise(nameof(RemixTabState));
    }

    private void OpenGitHub()
    {
        try { ExternalLinkService.OpenGitHubProfile(); }
        catch (Exception error) { dialogs.Error($"Unable to open GitHub: {error.Message}"); }
    }

    private void TryLocateFfmpeg()
    {
        try
        {
            tools = FfmpegLocator.Find(); audio = new AudioProcessingService(tools); probe = new FfprobeService(tools.FfprobePath); remix = new RemixProcessingService(tools); remixAnalyzer = new AdaptiveAudioAnalyzer(tools); artworkExtractor = new ArtworkExtractionService(tools);
            FfmpegMissing = false; Status = "FFmpeg ready. All processing stays local.";
        }
        catch (FfmpegDependencyException error)
        {
            FfmpegMissing = true; Status = error.Message;
        }
    }

    private async Task InstallFfmpegAsync()
    {
        await RunBusyAsync(async () =>
        {
            var installer = new FfmpegInstaller();
            var installProgress = new Progress<(double Fraction, string Status)>(item => UpdateOperationProgress(item.Fraction, item.Status));
            tools = await installer.InstallAsync(progress: installProgress, cancellationToken: ActiveOperationToken); audio = new AudioProcessingService(tools); probe = new FfprobeService(tools.FfprobePath); remix = new RemixProcessingService(tools); remixAnalyzer = new AdaptiveAudioAnalyzer(tools); artworkExtractor = new ArtworkExtractionService(tools); FfmpegMissing = false;
        });
    }

    private void ChooseFile() { var path = dialogs.ChooseAudioFile(); if (path is not null) SourcePath = path; }
    private void ChooseFolder() { var path = dialogs.ChooseFolder(); if (path is not null) SourcePath = path; }
    private void ChooseOutputFolder() { var path = dialogs.ChooseFolder(OutputFolder); if (path is not null) OutputFolder = path; }
    private async Task ChooseCutFileAsync() { var path = dialogs.ChooseAudioFile(); if (path is not null) await LoadCutSourceAsync(path); }
    private async Task ChooseCompressionFileAsync()
    {
        var path = dialogs.ChooseAudioFile();
        if (path is null) return;
        CompressionSourcePath = path;
        await AnalyzeCompressionAsync();
    }
    private async Task ChooseCompressionFolderAsync()
    {
        var path = dialogs.ChooseFolder();
        if (path is null) return;
        CompressionSourcePath = path;
        await AnalyzeCompressionAsync();
    }

    private async Task ChooseArtworkFileAsync()
    {
        var path = dialogs.ChooseAudioFile();
        if (path is null) return;
        ArtworkSourcePath = path;
        await AnalyzeArtworkAsync();
    }

    private async Task ChooseArtworkFolderAsync()
    {
        var path = dialogs.ChooseFolder();
        if (path is null) return;
        ArtworkSourcePath = path;
        await AnalyzeArtworkAsync();
    }

    private async Task ChooseRemixFileAsync()
    {
        var path = dialogs.ChooseAudioFile();
        if (path is not null) await LoadRemixSourceAsync(path);
    }

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
            EnsureFfmpeg();
            _ = AudioFormats.FromPath(path);
            Status = "Reading remix source and waveform…";
            var info = await probe!.ProbeAsync(path, ActiveOperationToken);
            var peaks = await audio!.ExtractWaveformAsync(path, 900, info.DurationSeconds, ActiveOperationToken);
            RemixSourcePath = path;
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
        if (!closing && RemixSourcePath == path && remixAnalyzer is not null) StartRemixAnalysis(path);
    }

    private void SelectRemixCategory(string categoryName)
    {
        if (Enum.TryParse<RemixPresetCategory>(categoryName, out var category)) SelectedRemixCategory = category;
    }

    private void ApplyRemixPreset(RemixPresetDefinition definition)
    {
        if (remixRackDirty && RemixEffects.Count > 0
            && !dialogs.Confirm("Applying a preset will replace the current effect rack. Continue?")) return;
        if (definition.Preset == RemixPreset.Earrape
            && !dialogs.Confirm("HEARING WARNING: EARRAPE creates extreme distortion and loudness. Lower your headphone or speaker volume before Preview or Export. High-volume listening can permanently damage hearing. Apply this preset?")) return;
        selectedRemixPreset = definition.Preset;
        ApplyRemixPresetInternal(definition.Preset);
    }

    private void ApplyRemixPresetInternal(RemixPreset preset)
    {
        var result = RemixPresetFactory.CreateAdaptive(preset, SelectedRemixIntensity, remixAnalysis);
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
            var analysis = await remixAnalyzer!.AnalyzeAsync(path, cancellationToken);
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

    private void RaiseCategoryStates()
    {
        Raise(nameof(SpeedPitchCategoryState));
        Raise(nameof(BassPunchCategoryState));
        Raise(nameof(AtmosphereCategoryState));
        Raise(nameof(VocalClarityCategoryState));
        Raise(nameof(ColorTextureCategoryState));
        Raise(nameof(MasteringCategoryState));
    }

    private void AddRemixEffect()
    {
        if (RemixEffects.Count >= 16) { dialogs.Error("A remix rack supports at most 16 effects."); return; }
        if (SelectedRemixEffectKind is RemixEffectKind.TempoPitch or RemixEffectKind.LoudnessNormalize or RemixEffectKind.FadeIn or RemixEffectKind.FadeOut
            && RemixEffects.Any(effect => effect.Kind == SelectedRemixEffectKind))
        {
            dialogs.Error($"{SelectedRemixEffectKind} can appear only once.");
            return;
        }
        var viewModel = new RemixEffectViewModel(SelectedRemixEffectKind, OnRemixRackChanged);
        var firstFade = RemixEffects.ToList().FindIndex(effect => effect.Kind is RemixEffectKind.FadeIn or RemixEffectKind.FadeOut);
        if (SelectedRemixEffectKind is RemixEffectKind.FadeIn or RemixEffectKind.FadeOut || firstFade < 0)
            RemixEffects.Add(viewModel);
        else
            RemixEffects.Insert(firstFade, viewModel);
        OnRemixRackChanged();
    }

    private void RemoveRemixEffect(RemixEffectViewModel effect)
    {
        RemixEffects.Remove(effect);
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
            EnsureFfmpeg();
            var rack = original ? Array.Empty<RemixEffect>() : BuildRemixRack();
            RemixRackValidator.Validate(rack, RemixDurationSeconds);
            previewPlayer.Stop();
            var progress = new Progress<double>(fraction => UpdateOperationProgress(fraction, "Rendering 20-second remix preview…"));
            var output = await remix!.RenderPreviewAsync(
                RemixSourcePath,
                rack,
                remixSampleRate,
                RemixDurationSeconds,
                RemixPreviewStartSeconds,
                progress,
                ActiveOperationToken,
                previewGain: PreviewVolume / 100);
            previewPlayer.Play(output);
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
            var hasRequestedCover = metadata.CoverAction == CoverArtAction.Replace
                || metadata.CoverAction == CoverArtAction.Keep && remixSourceHasCover;
            if (hasRequestedCover
                && RemixFormat is not (AudioFormat.Mp3 or AudioFormat.M4a or AudioFormat.Flac)
                && !dialogs.Confirm("The selected output format does not support embedded cover art here. Export without the cover?"))
            {
                Status = "Remix export cancelled.";
                TimingText = "";
                return;
            }

            var directory = string.IsNullOrWhiteSpace(RemixOutputFolder)
                ? Path.GetDirectoryName(RemixSourcePath)!
                : RemixOutputFolder;
            var output = OutputPathBuilder.Build(RemixSourcePath, directory, RemixFormat, RemixSuffix);
            var encoding = CreateRemixEncoding();
            var progress = new Progress<double>(fraction => UpdateOperationProgress(fraction, "Exporting remix…"));
            await remix!.ExportAsync(
                RemixSourcePath,
                output,
                rack,
                encoding,
                metadata,
                remixSampleRate,
                RemixDurationSeconds,
                remixSourceHasCover,
                progress,
                ActiveOperationToken);
            RemixReport = $"Export completed\nSource: {RemixSourcePath}\nOutput: {output}\nEffects: {rack.Count(effect => effect.Enabled)}\nDuration: {TimestampParser.Format(RemixOutputDuration)}";
            UpdateOperationProgress(1, $"Remix exported: {Path.GetFileName(output)}");
        });
    }

    private IReadOnlyList<RemixEffect> BuildRemixRack() => RemixEffects.Select(effect => effect.ToModel()).ToArray();

    private ConversionOptions CreateRemixEncoding() => new(
        RemixFormat,
        RemixFormat.IsLossy() ? RemixBitrate : null,
        RemixFormat.IsLossy() ? null : ParseOptionalInt(RemixSampleRateOption),
        RemixFormat.IsLossy() ? null : ParseOptionalInt(RemixBitDepth),
        PreserveMetadata: true,
        Overwrite: RemixOverwrite);

    private RemixMetadata BuildRemixMetadata()
    {
        var custom = RemixCustomTags.ToDictionary(tag => tag.Key, tag => tag.Value, StringComparer.OrdinalIgnoreCase);
        return new RemixMetadata(
            RemixTitle,
            RemixArtist,
            RemixAlbum,
            RemixAlbumArtist,
            RemixGenre,
            RemixDate,
            RemixTrack,
            RemixDisc,
            RemixComment,
            custom,
            Enum.Parse<CoverArtAction>(RemixCoverAction),
            string.IsNullOrWhiteSpace(RemixCoverPath) ? null : RemixCoverPath);
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

    private void ChooseRemixCover()
    {
        var path = dialogs.ChooseCoverImage();
        if (path is null) return;
        RemixCoverPath = path;
        RemixCoverAction = "Replace";
    }

    private async Task ConvertAsync()
    {
        await RunBusyAsync(async () =>
        {
            EnsureFfmpeg();
            var format = SelectedFormat;
            var configuredOutput = OutputFolder;
            var configuredSuffix = Suffix;
            var options = CreateOptions(format);
            Report = "Conversion started.";
            var files = EnumerateSources(SourcePath).ToArray();
            if (files.Length == 0) throw new InvalidDataException("No supported audio file was found.");
            var results = new List<FileResult>();
            for (var index = 0; index < files.Length; index++)
            {
                var file = files[index];
                try
                {
                    _ = AudioFormats.FromPath(file);
                    var info = await probe!.ProbeAsync(file, ActiveOperationToken);
                    var directory = ResolveOutputDirectory(file, configuredOutput);
                    Directory.CreateDirectory(directory);
                    var output = OutputPathBuilder.Build(file, directory, format, configuredSuffix);
                    var itemProgress = new Progress<double>(fraction => UpdateOperationProgress((index + fraction) / files.Length, $"Converting {index + 1}/{files.Length}: {Path.GetFileName(file)}"));
                    await audio!.ConvertAsync(file, output, options, info.DurationSeconds, itemProgress, ActiveOperationToken);
                    results.Add(new FileResult(file, output, null));
                    AppendOperationReport($"OK  {file} -> {output}");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error) { results.Add(new FileResult(file, null, error.Message)); AppendOperationReport($"FAIL  {file}: {error.Message}"); }
            }
            var batch = new BatchResult(results);
            Report = $"Completed: {batch.Succeeded} succeeded, {batch.Failed} failed\n" + string.Join('\n', results.Select(result => result.Succeeded ? $"OK  {Path.GetFileName(result.InputPath)} -> {result.OutputPath}" : $"FAIL  {Path.GetFileName(result.InputPath)}: {result.Error}"));
            UpdateOperationProgress(1, $"Completed: {batch.Succeeded} succeeded, {batch.Failed} failed");
            await settingsStore.SaveAsync(new AppSettings(Theme, configuredOutput), ActiveOperationToken);
        });
    }

    private async Task LoadCutSourceAsync(string path)
    {
        await RunBusyAsync(async () =>
        {
            EnsureFfmpeg(); _ = AudioFormats.FromPath(path); Status = "Reading audio and generating waveform…";
            var info = await probe!.ProbeAsync(path, ActiveOperationToken); var peaks = await audio!.ExtractWaveformAsync(path, 900, info.DurationSeconds, ActiveOperationToken);
            CutSourcePath = path; DurationSeconds = info.DurationSeconds; StartSeconds = 0; EndSeconds = info.DurationSeconds;
            CutFormat = AudioFormats.FromPath(path); WaveformPeaks = peaks; UpdateOperationProgress(1, $"Ready to cut {Path.GetFileName(path)}");
        });
    }

    private async Task CutAsync()
    {
        await RunBusyAsync(async () =>
        {
            EnsureFfmpeg(); var trim = CreateTrim(); var directory = ResolveOutputDirectory(CutSourcePath, CutOutputFolder); Directory.CreateDirectory(directory);
            var output = OutputPathBuilder.Build(CutSourcePath, directory, CutFormat, CutSuffix);
            var options = new ConversionOptions(CutFormat, CutFormat.IsLossy() ? "192k" : null, PreserveMetadata: PreserveMetadata, Overwrite: Overwrite);
            var itemProgress = new Progress<double>(fraction => UpdateOperationProgress(fraction, "Cutting audio…"));
            await audio!.TrimAsync(CutSourcePath, output, options, trim, itemProgress, ActiveOperationToken);
            Report = $"Cut completed: {Path.GetFileName(output)}\nOK  {CutSourcePath} -> {output}";
            UpdateOperationProgress(1, $"Cut completed: {Path.GetFileName(output)}");
        });
    }

    private async Task PreviewAsync()
    {
        await RunBusyAsync(async () =>
        {
            EnsureFfmpeg(); previewPlayer.Stop(); var output = AppPaths.PreviewWave;
            Status = "Rendering local preview…";
            var previewProgress = new Progress<double>(fraction => UpdateOperationProgress(fraction, "Rendering local preview…"));
            await audio!.RenderPreviewAsync(CutSourcePath, output, CreateTrim(), previewProgress, ActiveOperationToken);
            previewPlayer.Play(output);
            UpdateOperationProgress(1, "Playing cut preview.");
        });
    }

    private async Task PreviewArtworkAsync()
    {
        var album = SelectedArtworkAlbum;
        if (album is null) return;
        await RunBusyAsync(async () =>
        {
            EnsureFfmpeg();
            var result = await artworkExtractor!.ExtractAsync(album, artworkPreviewDirectory,
                new ArtworkExtractionOptions(ArtworkOutputFormat.Png, 512), cancellationToken: ActiveOperationToken);
            if (result.Error is not null) throw new InvalidDataException(result.Error);
            using var stream = File.OpenRead(result.OutputPath!);
            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            ArtworkPreview = image;
            ArtworkPreviewInfo = $"{result.Album.Artwork.Width} × {result.Album.Artwork.Height} · {result.Album.Artwork.CodecName}\n{result.Album.SourcePath}";
            UpdateOperationProgress(1, "Artwork preview ready.");
        });
    }

    private async Task SaveReportAsync()
    {
        var report = activeWorkspace switch
        {
            "Artwork" => ArtworkReport,
            "Compress" => CompressionReport,
            "Remix" => RemixReport,
            _ => Report,
        };
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Text report|*.txt", FileName = "CodecTone-report.txt" };
        if (dialog.ShowDialog() != true) return;
        try { await File.WriteAllTextAsync(dialog.FileName, report); }
        catch (Exception error) { diagnosticLog.Write("Save report", error.Message); dialogs.Error(error.Message); }
    }

    private void OpenDiagnostics()
    {
        diagnosticLog.Write("Diagnostics", "Opened by user");
        try { Process.Start(new ProcessStartInfo(diagnosticLog.FilePath) { UseShellExecute = true }); }
        catch (Exception error) { dialogs.Error(error.Message); }
    }

    private void OpenArtworkFolder()
    {
        try
        {
            var directory = ResolveArtworkOutputRoot();
            if (!Directory.Exists(directory)) { dialogs.Error("The output folder does not exist yet."); return; }
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }
        catch (Exception error) { dialogs.Error(error.Message); }
    }

    private async Task AnalyzeArtworkAsync()
    {
        await RunBusyAsync(async () =>
        {
            var preparation = await PrepareArtworkAsync(1);
            ArtworkEstimate = $"Scanned {preparation.SourceCount} audio file(s) · {preparation.Plan.Albums.Count} album cover(s) ready · {preparation.Plan.Skipped.Count + preparation.Failures.Count} skipped or invalid";
            ArtworkReport = BuildArtworkPreflightReport(preparation);
            UpdateOperationProgress(1, $"Artwork analysis ready: {preparation.Plan.Albums.Count} album(s)");
        });
    }

    private async Task ExtractArtworkAsync()
    {
        await RunBusyAsync(async () =>
        {
            ArtworkReport = "Artwork extraction started.";
            var preparation = await PrepareArtworkAsync(0.15);
            var options = CreateArtworkOptions();
            var outputRoot = preparation.OutputRoot;
            var lines = new List<string>(preparation.Failures);
            lines.AddRange(preparation.ScanWarnings.Select(warning => "SKIP folder: " + warning));
            lines.AddRange(preparation.Plan.Skipped.Select(item => $"SKIP  {item.SourcePath}: {item.Reason}"));
            var succeeded = 0;
            var failed = preparation.Failures.Count;
            var skipped = preparation.Plan.Skipped.Count;
            for (var index = 0; index < preparation.Plan.Albums.Count; index++)
            {
                var album = preparation.Plan.Albums[index];
                var baseIndex = index;
                var progress = new Progress<double>(fraction => UpdateOperationProgress(
                    0.15 + (baseIndex + fraction) / Math.Max(1, preparation.Plan.Albums.Count) * 0.85,
                    $"Extracting artwork {baseIndex + 1}/{preparation.Plan.Albums.Count}: {album.Artist} - {album.Album}"));
                var result = await artworkExtractor!.ExtractAsync(album, outputRoot, options, progress, ActiveOperationToken);
                if (result.Error is not null)
                {
                    failed++;
                    lines.Add($"FAIL  {album.SourcePath}: {result.Error}");
                }
                else if (result.Skipped)
                {
                    skipped++;
                    lines.Add($"SKIP  {album.Artist} - {album.Album}: identical image already exists at {result.OutputPath}");
                }
                else
                {
                    succeeded++;
                    lines.Add($"OK    {album.Artist} - {album.Album} -> {result.OutputPath}");
                }
                AppendOperationReport(lines[^1]);
            }
            ArtworkReport = $"Completed: {succeeded} extracted, {failed} failed, {skipped} skipped\nOutput: {outputRoot}\n\n" + string.Join('\n', lines);
            UpdateOperationProgress(1, $"Artwork extraction completed: {succeeded} extracted, {failed} failed, {skipped} skipped");
        });
    }

    private async Task<ArtworkPreparation> PrepareArtworkAsync(double progressWeight)
    {
        EnsureFfmpeg();
        var outputRoot = ResolveArtworkOutputRoot();
        var scanWarnings = new List<string>();
        var discovered = await Task.Run(() => CompressionFileDiscovery.Find(ArtworkSourcePath, recursive: true,
                onSkipped: scanWarnings.Add, cancellationToken: ActiveOperationToken)
            .Where(file => !IsPathInside(file.Path, outputRoot)).ToArray(), ActiveOperationToken);
        foreach (var warning in scanWarnings) AppendOperationReport("SKIP folder: " + warning);
        if (discovered.Length == 0) throw new InvalidDataException("No supported audio file was found.");

        var sources = new List<ArtworkSource>();
        var failures = new List<string>();
        for (var index = 0; index < discovered.Length; index++)
        {
            var file = discovered[index];
            try
            {
                var info = await probe!.ProbeAsync(file.Path, ActiveOperationToken);
                sources.Add(new ArtworkSource(file.Path, info.Tags, info.ArtworkStreams));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) { failures.Add($"FAIL  {file.RelativePath}: {error.Message}"); }
            UpdateOperationProgress((index + 1d) / discovered.Length * progressWeight, $"Analyzing artwork {index + 1}/{discovered.Length}: {Path.GetFileName(file.Path)}");
        }
        var plan = ArtworkPlanner.Create(sources);
        ArtworkAlbums.Clear();
        foreach (var album in plan.Albums) ArtworkAlbums.Add(album);
        SelectedArtworkAlbum = ArtworkAlbums.FirstOrDefault();
        return new ArtworkPreparation(plan, failures, outputRoot, discovered.Length, scanWarnings);
    }

    private ArtworkExtractionOptions CreateArtworkOptions()
    {
        int? maximum = null;
        if (IsArtworkConversion && ArtworkLimitDimensions)
        {
            if (!int.TryParse(ArtworkMaximumDimension, out var parsed) || parsed is < 64 or > 8192)
                throw new ArgumentException("Maximum artwork dimension must be between 64 and 8192 pixels.");
            maximum = parsed;
        }
        return new ArtworkExtractionOptions(ArtworkOutputFormat, maximum);
    }

    private string ResolveArtworkOutputRoot()
    {
        if (!string.IsNullOrWhiteSpace(ArtworkOutputFolder)) return Path.GetFullPath(ArtworkOutputFolder);
        return File.Exists(ArtworkSourcePath)
            ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(ArtworkSourcePath))!, "artwork")
            : Path.Combine(Path.GetFullPath(ArtworkSourcePath), "artwork");
    }

    private static string BuildArtworkPreflightReport(ArtworkPreparation preparation)
    {
        var lines = new List<string>(preparation.Failures);
        lines.AddRange(preparation.ScanWarnings.Select(warning => "SKIP folder: " + warning));
        lines.AddRange(preparation.Plan.Albums.Select(album => $"READY {album.Artist} - {album.Album} · stream {album.Artwork.StreamIndex} · {album.Artwork.CodecName} · {album.Artwork.Width}x{album.Artwork.Height}"));
        lines.AddRange(preparation.Plan.Skipped.Select(item => $"SKIP  {item.SourcePath}: {item.Reason}"));
        return lines.Count == 0 ? "No embedded artwork was found." : string.Join('\n', lines);
    }

    private async Task AnalyzeCompressionAsync()
    {
        await RunBusyAsync(async () =>
        {
            var preparation = await PrepareCompressionAsync(1);
            CompressionEstimate = BuildCompressionEstimate(preparation.Plan);
            CompressionReport = preparation.Failures.Count == 0
                ? "Analysis completed. No invalid files found."
                : string.Join('\n', preparation.Failures);
            CompressionReport += "\n" + string.Join('\n', preparation.ScanWarnings.Select(warning => "SKIP folder: " + warning));
            UpdateOperationProgress(1, $"Compression analysis ready: {preparation.Plan.Files.Count} valid file(s)");
        });
    }

    private async Task CompressAsync()
    {
        await RunBusyAsync(async () =>
        {
            CompressionReport = "Compression started.";
            var preparation = await PrepareCompressionAsync(0.1);
            var plan = preparation.Plan;
            var outputFormat = plan.Options.OutputFormat;
            var outputSuffix = CompressionSuffix;
            CompressionEstimate = BuildCompressionEstimate(plan);
            var activeFiles = plan.Files.Where(file => !file.ShouldSkip).ToArray();
            if (activeFiles.Length == 0)
            {
                CompressionReport = "All files were skipped because no meaningful size reduction was expected.";
                UpdateOperationProgress(1, "Nothing to compress.");
                return;
            }

            if (outputFormat.IsLossy()
                && activeFiles.Any(file => file.Source.Format.IsLossy())
                && !dialogs.Confirm("Some source files are already lossy. Re-encoding them can reduce audio quality. Continue?"))
            {
                Status = "Compression cancelled.";
                TimingText = "";
                return;
            }

            var reportLines = new List<string>(preparation.Failures);
            reportLines.AddRange(preparation.ScanWarnings.Select(warning => "SKIP folder: " + warning));
            foreach (var skipped in plan.Files.Where(file => file.ShouldSkip))
                reportLines.Add($"SKIP  {skipped.Source.RelativePath}: {skipped.SkipReason}");

            var totalDuration = activeFiles.Sum(file => file.Source.DurationSeconds);
            var completedDuration = 0d;
            var succeeded = 0;
            var failed = preparation.Failures.Count;
            long originalProcessedBytes = 0;
            long outputBytes = 0;
            var activeIndex = 0;
            foreach (var file in activeFiles)
            {
                try
                {
                    var discovered = preparation.Discovered[file.Source.Path];
                    var output = OutputPathBuilder.BuildCompressed(
                        file.Source.Path,
                        discovered.SourceRoot,
                        preparation.OutputRoot,
                        outputFormat,
                        outputSuffix);
                    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                    var baseDuration = completedDuration;
                    var progress = new Progress<double>(fraction =>
                    {
                        var audioFraction = totalDuration <= 0
                            ? 1
                            : (baseDuration + fraction * file.Source.DurationSeconds) / totalDuration;
                        UpdateOperationProgress(
                            0.1 + audioFraction * 0.9,
                            $"Compressing {activeIndex + 1}/{activeFiles.Length}: {Path.GetFileName(file.Source.Path)}");
                    });
                    await audio!.CompressAsync(
                        file.Source.Path,
                        output,
                        plan.Options,
                        plan.TargetAudioBitrateKbps,
                        file.Source.DurationSeconds,
                        file.Source.HasCoverArt,
                        progress,
                        ActiveOperationToken);
                    completedDuration += file.Source.DurationSeconds;
                    originalProcessedBytes += file.Source.SizeBytes;
                    outputBytes += new FileInfo(output).Length;
                    succeeded++;
                    reportLines.Add($"OK    {file.Source.RelativePath} -> {output}");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error)
                {
                    completedDuration += file.Source.DurationSeconds;
                    failed++;
                    reportLines.Add($"FAIL  {file.Source.RelativePath}: {error.Message}");
                }
                AppendOperationReport(reportLines[^1]);
                activeIndex++;
            }

            var savings = CompressionSavings.Calculate(originalProcessedBytes, outputBytes);
            var skippedCount = plan.Files.Count(file => file.ShouldSkip);
            CompressionReport =
                $"Completed: {succeeded} succeeded, {failed} failed, {skippedCount} skipped\n" +
                $"Original processed: {FormatBytes(originalProcessedBytes)}\n" +
                $"Final: {FormatBytes(outputBytes)}\n" +
                $"Saved: {FormatBytes(savings.SavedBytes)} ({savings.ReductionPercent:0.0}%)\n\n" +
                string.Join('\n', reportLines);
            UpdateOperationProgress(1, $"Compression completed: {succeeded} succeeded, {failed} failed, {skippedCount} skipped");
        });
    }

    private async Task<CompressionPreparation> PrepareCompressionAsync(double progressWeight)
    {
        EnsureFfmpeg();
        var outputRoot = ResolveCompressionOutputRoot();
        var scanWarnings = new List<string>();
        var discovered = await Task.Run(() => CompressionFileDiscovery.Find(CompressionSourcePath, recursive: true,
                onSkipped: scanWarnings.Add, cancellationToken: ActiveOperationToken)
            .Where(file => !IsPathInside(file.Path, outputRoot)).ToArray(), ActiveOperationToken);
        foreach (var warning in scanWarnings) AppendOperationReport("SKIP folder: " + warning);
        if (discovered.Length == 0) throw new InvalidDataException("No supported audio file was found.");

        var sources = new List<CompressionSource>();
        var failures = new List<string>();
        for (var index = 0; index < discovered.Length; index++)
        {
            var file = discovered[index];
            try
            {
                var info = await probe!.ProbeAsync(file.Path, ActiveOperationToken);
                sources.Add(new CompressionSource(
                    file.Path,
                    file.RelativePath,
                    AudioFormats.FromPath(file.Path),
                    info.DurationSeconds,
                    info.SizeBytes ?? new FileInfo(file.Path).Length,
                    info.AudioBitrateKbps,
                    info.HasCoverArt));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error)
            {
                failures.Add($"FAIL  {file.RelativePath}: {error.Message}");
            }
            UpdateOperationProgress(
                (index + 1d) / discovered.Length * progressWeight,
                $"Analyzing {index + 1}/{discovered.Length}: {Path.GetFileName(file.Path)}");
        }
        if (sources.Count == 0) throw new InvalidDataException("No valid audio file remained after ffprobe validation.");

        var options = new CompressionOptions(
            CompressionFormat,
            ParseCompressionProfile(SelectedCompressionProfile),
            ParseTargetBytes(),
            CompressionOptimizeArtwork,
            CompressionPreserveMetadata,
            CompressionOverwrite);
        var plan = CompressionPlanner.Create(sources, options);
        var lookup = discovered.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        return new CompressionPreparation(plan, lookup, failures, outputRoot, scanWarnings);
    }

    private string ResolveCompressionOutputRoot()
    {
        if (!string.IsNullOrWhiteSpace(CompressionOutputFolder)) return Path.GetFullPath(CompressionOutputFolder);
        return File.Exists(CompressionSourcePath)
            ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(CompressionSourcePath))!, "compressed")
            : Path.Combine(Path.GetFullPath(CompressionSourcePath), "compressed");
    }

    private long? ParseTargetBytes()
    {
        if (!IsTargetSizeCompression) return null;
        if (!double.TryParse(CompressionTargetTotalMb, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out var megabytes)
            && !double.TryParse(CompressionTargetTotalMb, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out megabytes))
            throw new ArgumentException("Target total size must be a number in MiB.");
        if (megabytes <= 0) throw new ArgumentException("Target total size must be positive.");
        return checked((long)(megabytes * 1024 * 1024));
    }

    private static CompressionProfile ParseCompressionProfile(string value) => value switch
    {
        "High fidelity" => AudioConverter.Core.Models.CompressionProfile.HighFidelity,
        "Balanced" => AudioConverter.Core.Models.CompressionProfile.Balanced,
        "Maximum reduction" => AudioConverter.Core.Models.CompressionProfile.MaximumReduction,
        "Target total size" => AudioConverter.Core.Models.CompressionProfile.TargetTotalSize,
        _ => throw new ArgumentException("Unknown compression profile."),
    };

    private static bool IsPathInside(string path, string directory)
    {
        var fullPath = Path.GetFullPath(path);
        var fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildCompressionEstimate(CompressionPlan plan)
    {
        var saved = Math.Max(0, plan.OriginalTotalBytes - plan.EstimatedOutputBytes);
        var reduction = plan.OriginalTotalBytes == 0 ? 0 : saved * 100d / plan.OriginalTotalBytes;
        var skipped = plan.Files.Count(file => file.ShouldSkip);
        var bitrate = plan.TargetAudioBitrateKbps is null ? "" : $" · {plan.TargetAudioBitrateKbps} kbps shared target";
        return $"Original {FormatBytes(plan.OriginalTotalBytes)} · estimated {FormatBytes(plan.EstimatedOutputBytes)} · save {reduction:0.0}%{bitrate} · {skipped} skipped";
    }

    private static string FormatBytes(long bytes) => $"{bytes / 1024d / 1024d:0.00} MiB";

    private sealed record CompressionPreparation(
        CompressionPlan Plan,
        IReadOnlyDictionary<string, CompressionDiscoveredFile> Discovered,
        IReadOnlyList<string> Failures,
        string OutputRoot,
        IReadOnlyList<string> ScanWarnings);

    private sealed record ArtworkPreparation(
        ArtworkPlan Plan,
        IReadOnlyList<string> Failures,
        string OutputRoot,
        int SourceCount,
        IReadOnlyList<string> ScanWarnings);

    private TrimSelection CreateTrim() => TrimSelection.Create(StartSeconds, EndSeconds, FadeInEnabled ? FadeInSeconds : 0, FadeOutEnabled ? FadeOutSeconds : 0);
    private ConversionOptions CreateOptions(AudioFormat format) => new(
        format,
        format.IsLossy() ? Bitrate : null,
        format.IsLossy() ? null : ParseOptionalInt(SampleRate),
        format.IsLossy() ? null : ParseOptionalInt(BitDepth),
        PreserveMetadata,
        Overwrite);
    private static int? ParseOptionalInt(string value) => int.TryParse(value, out var result) ? result : null;
    private static string ResolveOutputDirectory(string source, string configured) => string.IsNullOrWhiteSpace(configured) ? Path.Combine(Path.GetDirectoryName(source)!, "converted") : configured;
    private static IEnumerable<string> EnumerateSources(string source) => File.Exists(source) ? [source] : Directory.Exists(source) ? Directory.EnumerateFiles(source).Where(path => { try { _ = AudioFormats.FromPath(path); return true; } catch { return false; } }) : [];
    private void EnsureFfmpeg() { if (tools is null || audio is null || probe is null || remix is null || artworkExtractor is null) throw new FfmpegDependencyException("FFmpeg is required. Install it from this application first."); }

    private async Task RunBusyAsync(Func<Task> action)
    {
        if (IsBusy || closing) return;
        operationWorkspace = activeWorkspace;
        operationFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        diagnosticLog.Write(operationWorkspace, "Operation started");
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
            if (!closing) dialogs.Error(error.Message);
        }
        finally
        {
            operationTimer.Stop();
            if (succeeded && Progress >= 100)
            {
                TimingText = $"Completed in {OperationTiming.FormatCompact(operationTimer.Elapsed)}";
            }
            IsBusy = false;
            activeOperationCancellation.Dispose();
            activeOperationCancellation = null;
            diagnosticLog.Write(operationWorkspace, Status);
            operationFinished?.TrySetResult();
        }
    }

    private void AppendOperationReport(string line)
    {
        switch (operationWorkspace)
        {
            case "Compress": CompressionReport += "\n" + line; break;
            case "Artwork": ArtworkReport += "\n" + line; break;
            case "Remix": RemixReport += "\n" + line; break;
            default: Report += "\n" + line; break;
        }
        diagnosticLog.Write(operationWorkspace, line);
    }

    public async Task ShutdownAsync()
    {
        closing = true;
        Raise(nameof(IsWorkspaceEnabled));
        activeOperationCancellation?.Cancel();
        remixAnalysisCancellation?.Cancel();
        previewPlayer.Stop();
        if (operationFinished is not null) await operationFinished.Task;
        await Task.WhenAll(analysisTasks.ToArray());
        Dispose();
        try { if (Directory.Exists(artworkPreviewDirectory)) Directory.Delete(artworkPreviewDirectory, true); }
        catch (IOException error) { diagnosticLog.Write("Cleanup", error.Message); }
        catch (UnauthorizedAccessException error) { diagnosticLog.Write("Cleanup", error.Message); }
    }

    private CancellationToken ActiveOperationToken => activeOperationCancellation?.Token ?? CancellationToken.None;
    private void CancelActiveOperation() => activeOperationCancellation?.Cancel();

    private void UpdateOperationProgress(double completedFraction, string statusText)
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

    private async Task ToggleThemeAsync()
    {
        try
        {
            Theme = Theme.Equals("Oled", StringComparison.OrdinalIgnoreCase) ? "White" : "Oled";
            ThemeService.Apply(Theme);
            await settingsStore.SaveAsync(new AppSettings(Theme, OutputFolder));
        }
        catch (Exception error) { dialogs.Error($"Unable to save theme settings: {error.Message}"); }
    }

    private void RaiseCommands() { PreviewOriginalCommand.RaiseCanExecuteChanged(); PreviewArtworkCommand.RaiseCanExecuteChanged(); SaveReportCommand.RaiseCanExecuteChanged(); ConvertCommand.RaiseCanExecuteChanged(); CutCommand.RaiseCanExecuteChanged(); PreviewCommand.RaiseCanExecuteChanged(); InstallFfmpegCommand.RaiseCanExecuteChanged(); ChooseCutFileCommand.RaiseCanExecuteChanged(); ChooseCompressionFileCommand.RaiseCanExecuteChanged(); AnalyzeCompressionCommand.RaiseCanExecuteChanged(); CompressCommand.RaiseCanExecuteChanged(); ChooseArtworkFileCommand.RaiseCanExecuteChanged(); ChooseArtworkFolderCommand.RaiseCanExecuteChanged(); AnalyzeArtworkCommand.RaiseCanExecuteChanged(); ExtractArtworkCommand.RaiseCanExecuteChanged(); ChooseRemixFileCommand.RaiseCanExecuteChanged(); PreviewRemixCommand.RaiseCanExecuteChanged(); ExportRemixCommand.RaiseCanExecuteChanged(); }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        activeOperationCancellation?.Cancel();
        remixAnalysisCancellation?.Cancel();
        remixAnalysisCancellation?.Dispose();
        previewPlayer.Dispose();
        remix?.CleanupTemporaryFiles();
    }
}
