using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using AudioConverter.Core.Artwork;
using AudioConverter.Core.Compression;
using AudioConverter.Desktop.Mvvm;
using AudioConverter.Infrastructure.Storage;
using L = AudioConverter.Desktop.Localization.Localizer;

namespace AudioConverter.Desktop.ViewModels;

public sealed class ArtworkViewModel : WorkspaceBase
{
    private readonly string artworkPreviewDirectory = Path.Combine(AppPaths.Root, "artwork-preview", Guid.NewGuid().ToString("N"));
    private string artworkSourcePath = "";
    private string artworkOutputFolder = "";
    private ArtworkOutputFormat artworkOutputFormat = ArtworkOutputFormat.Original;
    private bool artworkLimitDimensions;
    private string artworkMaximumDimension = "1200";
    private string artworkEstimate = L.T("ChooseASourceToFindArtwork");
    private ArtworkPlannedAlbum? selectedArtworkAlbum;
    private System.Windows.Media.Imaging.BitmapImage? artworkPreview;
    private string artworkPreviewInfo = L.T("SelectAnAlbumAndPreviewIts");

    public ArtworkViewModel(WorkspaceHost host) : base(host, "Artwork", "ReportEmptyArtwork")
    {
        ChooseArtworkFileCommand = new AsyncRelayCommand(() => ChooseSourceAsync(Dialogs.ChooseAudioFile()), () => !Host.IsBusy);
        ChooseArtworkFolderCommand = new AsyncRelayCommand(() => ChooseSourceAsync(Dialogs.ChooseFolder()), () => !Host.IsBusy);
        ChooseArtworkOutputCommand = new RelayCommand(() => ArtworkOutputFolder = Dialogs.ChooseFolder(ArtworkOutputFolder) ?? ArtworkOutputFolder);
        AnalyzeArtworkCommand = new AsyncRelayCommand(AnalyzeArtworkAsync, () => !Host.IsBusy && !string.IsNullOrWhiteSpace(ArtworkSourcePath));
        ExtractArtworkCommand = new AsyncRelayCommand(ExtractArtworkAsync, () => !Host.IsBusy && !string.IsNullOrWhiteSpace(ArtworkSourcePath));
        PreviewArtworkCommand = new AsyncRelayCommand(PreviewArtworkAsync, () => !Host.IsBusy && SelectedArtworkAlbum is not null);
        OpenArtworkFolderCommand = new RelayCommand(() =>
        {
            try { Host.OpenFolder(ResolveArtworkOutputRoot()); }
            catch (Exception error) { Dialogs.Error(error.Message); }
        });
    }

    public override ICommand PrimaryCommand => ExtractArtworkCommand;
    public override string PrimaryLabel => L.T("ExtractArtwork");
    public AsyncRelayCommand ChooseArtworkFileCommand { get; }
    public AsyncRelayCommand ChooseArtworkFolderCommand { get; }
    public RelayCommand ChooseArtworkOutputCommand { get; }
    public AsyncRelayCommand AnalyzeArtworkCommand { get; }
    public AsyncRelayCommand ExtractArtworkCommand { get; }
    public AsyncRelayCommand PreviewArtworkCommand { get; }
    public RelayCommand OpenArtworkFolderCommand { get; }
    public IReadOnlyList<ArtworkOutputFormat> ArtworkOutputFormats { get; } = Enum.GetValues<ArtworkOutputFormat>();
    public ObservableCollection<ArtworkPlannedAlbum> ArtworkAlbums { get; } = [];
    public string ArtworkSourcePath { get => artworkSourcePath; set { if (Set(ref artworkSourcePath, value)) RaiseCommands(); } }
    public string ArtworkOutputFolder { get => artworkOutputFolder; set => Set(ref artworkOutputFolder, value); }
    public ArtworkOutputFormat ArtworkOutputFormat { get => artworkOutputFormat; set { if (Set(ref artworkOutputFormat, value)) Raise(nameof(IsArtworkConversion)); } }
    public bool IsArtworkConversion => ArtworkOutputFormat != ArtworkOutputFormat.Original;
    public bool ArtworkLimitDimensions { get => artworkLimitDimensions; set => Set(ref artworkLimitDimensions, value); }
    public string ArtworkMaximumDimension { get => artworkMaximumDimension; set => Set(ref artworkMaximumDimension, value); }
    public string ArtworkEstimate { get => artworkEstimate; private set => Set(ref artworkEstimate, value); }
    public ArtworkPlannedAlbum? SelectedArtworkAlbum
    {
        get => selectedArtworkAlbum;
        set { if (Set(ref selectedArtworkAlbum, value)) { ArtworkPreview = null; ArtworkPreviewInfo = L.T("SelectPreviewCoverToLoadThe"); PreviewArtworkCommand.RaiseCanExecuteChanged(); } }
    }
    public System.Windows.Media.Imaging.BitmapImage? ArtworkPreview { get => artworkPreview; private set => Set(ref artworkPreview, value); }
    public string ArtworkPreviewInfo { get => artworkPreviewInfo; private set => Set(ref artworkPreviewInfo, value); }

    public override void LoadPreferences(IReadOnlyDictionary<string, string> preferences)
    {
        ArtworkOutputFormat = EnumPreference<ArtworkOutputFormat>(Preference(preferences, "Format")) ?? ArtworkOutputFormat;
        ArtworkLimitDimensions = BoolPreference(Preference(preferences, "LimitDimensions")) ?? ArtworkLimitDimensions;
        ArtworkMaximumDimension = Preference(preferences, "MaximumDimension") ?? ArtworkMaximumDimension;
    }

    public override void SavePreferences(IDictionary<string, string> preferences)
    {
        preferences[$"{Key}.Format"] = ArtworkOutputFormat.ToString();
        preferences[$"{Key}.LimitDimensions"] = ArtworkLimitDimensions.ToString();
        preferences[$"{Key}.MaximumDimension"] = ArtworkMaximumDimension;
    }

    public override async Task AcceptDropAsync(IReadOnlyList<string> paths) =>
        await ChooseSourceAsync(paths.FirstOrDefault(item => File.Exists(item) || Directory.Exists(item)));

    public override Task ShutdownAsync()
    {
        try { if (Directory.Exists(artworkPreviewDirectory)) Directory.Delete(artworkPreviewDirectory, true); }
        catch (IOException error) { Host.Log.Write("Cleanup", error.Message); }
        catch (UnauthorizedAccessException error) { Host.Log.Write("Cleanup", error.Message); }
        return Task.CompletedTask;
    }

    protected override void OnLanguageChanged()
    {
        artworkEstimate = Refreshed(artworkEstimate, "ChooseASourceToFindArtwork");
        artworkPreviewInfo = Refreshed(artworkPreviewInfo, "SelectAnAlbumAndPreviewIts", "SelectPreviewCoverToLoadThe");
        base.OnLanguageChanged();
    }

    protected override void RaiseCommands()
    {
        ChooseArtworkFileCommand.RaiseCanExecuteChanged(); ChooseArtworkFolderCommand.RaiseCanExecuteChanged(); AnalyzeArtworkCommand.RaiseCanExecuteChanged(); ExtractArtworkCommand.RaiseCanExecuteChanged(); PreviewArtworkCommand.RaiseCanExecuteChanged();
    }

    private async Task ChooseSourceAsync(string? path)
    {
        if (path is null) return;
        ArtworkSourcePath = path;
        await AnalyzeArtworkAsync();
    }

    private async Task AnalyzeArtworkAsync()
    {
        await RunBusyAsync(async () =>
        {
            var preparation = await PrepareArtworkAsync(1);
            ArtworkEstimate = L.F("ScannedAudioFileSAlbumCover", preparation.SourceCount, preparation.Plan.Albums.Count, preparation.Plan.Skipped.Count + preparation.Failures.Count);
            Report = BuildArtworkPreflightReport(preparation);
            UpdateOperationProgress(1, L.F("ArtworkAnalysisReadyAlbumS", preparation.Plan.Albums.Count));
        });
    }

    private async Task PreviewArtworkAsync()
    {
        var album = SelectedArtworkAlbum;
        if (album is null) return;
        await RunBusyAsync(async () =>
        {
            Host.EnsureFfmpeg();
            var result = await Host.ArtworkExtractor!.ExtractAsync(album, artworkPreviewDirectory, new ArtworkExtractionOptions(ArtworkOutputFormat.Png, 512), cancellationToken: ActiveOperationToken);
            if (result.Error is not null) throw new InvalidDataException(result.Error);
            using var stream = File.OpenRead(result.OutputPath!);
            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            ArtworkPreview = image;
            ArtworkPreviewInfo = L.F("ArtworkPreviewDetails", result.Album.Artwork.Width, result.Album.Artwork.Height, result.Album.Artwork.CodecName, result.Album.SourcePath);
            UpdateOperationProgress(1, L.T("ArtworkPreviewReady"));
        });
    }

    private async Task ExtractArtworkAsync()
    {
        await RunBusyAsync(async () =>
        {
            Report = L.T("ArtworkExtractionStarted");
            var preparation = await PrepareArtworkAsync(0.15);
            var options = CreateArtworkOptions();
            var outputRoot = preparation.OutputRoot;
            var lines = new List<string>(preparation.Failures);
            lines.AddRange(preparation.ScanWarnings.Select(warning => L.T("SKIPFolder") + warning));
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
                    L.F("ExtractingArtwork", baseIndex + 1, preparation.Plan.Albums.Count, album.Artist, album.Album)));
                var result = await Host.ArtworkExtractor!.ExtractAsync(album, outputRoot, options, progress, ActiveOperationToken);
                if (result.Error is not null) { failed++; lines.Add($"FAIL  {album.SourcePath}: {result.Error}"); }
                else if (result.Skipped) { skipped++; lines.Add(L.F("SKIPIdenticalImageAlreadyExistsAt", album.Artist, album.Album, result.OutputPath)); }
                else { succeeded++; lines.Add($"OK    {album.Artist} - {album.Album} -> {result.OutputPath}"); }
                Host.AppendOperationReport(lines[^1]);
            }
            Report = L.F("CompletedExtractedFailedSkippedOutput", succeeded, failed, skipped, outputRoot) + string.Join('\n', lines);
            if (succeeded + skipped > 0) Host.SetLastOutputFolder(this, outputRoot);
            UpdateOperationProgress(1, L.F("ArtworkExtractionCompletedExtractedFaile", succeeded, failed, skipped));
        });
    }

    private async Task<ArtworkPreparation> PrepareArtworkAsync(double progressWeight)
    {
        Host.EnsureFfmpeg();
        var outputRoot = ResolveArtworkOutputRoot();
        var scanWarnings = new List<string>();
        var discovered = await Task.Run(() => CompressionFileDiscovery.Find(ArtworkSourcePath, recursive: true,
                onSkipped: scanWarnings.Add, cancellationToken: ActiveOperationToken)
            .Where(file => !IsPathInside(file.Path, outputRoot)).ToArray(), ActiveOperationToken);
        foreach (var warning in scanWarnings) Host.AppendOperationReport(L.T("SKIPFolder") + warning);
        if (discovered.Length == 0) throw new InvalidDataException(L.T("NoSupportedAudioFileWasFound"));

        var sources = new List<ArtworkSource>();
        var failures = new List<string>();
        for (var index = 0; index < discovered.Length; index++)
        {
            var file = discovered[index];
            try
            {
                var info = await Host.Probe!.ProbeAsync(file.Path, ActiveOperationToken);
                sources.Add(new ArtworkSource(file.Path, info.Tags, info.ArtworkStreams));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) { failures.Add($"FAIL  {file.RelativePath}: {error.Message}"); }
            UpdateOperationProgress((index + 1d) / discovered.Length * progressWeight, L.F("AnalyzingArtwork", index + 1, discovered.Length, Path.GetFileName(file.Path)));
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
                throw new ArgumentException(L.T("MaximumArtworkDimensionMustBeBetween"));
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
        lines.AddRange(preparation.ScanWarnings.Select(warning => L.T("SKIPFolder") + warning));
        lines.AddRange(preparation.Plan.Albums.Select(album => L.F("READYStreamX", album.Artist, album.Album, album.Artwork.StreamIndex, album.Artwork.CodecName, album.Artwork.Width, album.Artwork.Height)));
        lines.AddRange(preparation.Plan.Skipped.Select(item => $"SKIP  {item.SourcePath}: {item.Reason}"));
        return lines.Count == 0 ? L.T("NoEmbeddedArtworkWasFound") : string.Join('\n', lines);
    }

    private sealed record ArtworkPreparation(ArtworkPlan Plan, IReadOnlyList<string> Failures, string OutputRoot, int SourceCount, IReadOnlyList<string> ScanWarnings);
}
