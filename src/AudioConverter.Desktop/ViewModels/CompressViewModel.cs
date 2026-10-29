using System.IO;
using System.Windows.Input;
using AudioConverter.Core.Compression;
using AudioConverter.Core.Models;
using AudioConverter.Core.Paths;
using AudioConverter.Desktop.Mvvm;

namespace AudioConverter.Desktop.ViewModels;

public sealed class CompressViewModel : WorkspaceBase
{
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

    public CompressViewModel(WorkspaceHost host) : base(host, "Compress", "No compression has run yet.")
    {
        ChooseCompressionFileCommand = new AsyncRelayCommand(() => ChooseSourceAsync(Dialogs.ChooseAudioFile()), () => !Host.IsBusy);
        ChooseCompressionFolderCommand = new AsyncRelayCommand(() => ChooseSourceAsync(Dialogs.ChooseFolder()), () => !Host.IsBusy);
        ChooseCompressionOutputCommand = new RelayCommand(() => CompressionOutputFolder = Dialogs.ChooseFolder(CompressionOutputFolder) ?? CompressionOutputFolder);
        AnalyzeCompressionCommand = new AsyncRelayCommand(AnalyzeCompressionAsync, () => !Host.IsBusy && !string.IsNullOrWhiteSpace(CompressionSourcePath));
        CompressCommand = new AsyncRelayCommand(CompressAsync, () => !Host.IsBusy && !string.IsNullOrWhiteSpace(CompressionSourcePath));
    }

    public override ICommand PrimaryCommand => CompressCommand;
    public override string PrimaryLabel => "Compress audio";
    public AsyncRelayCommand ChooseCompressionFileCommand { get; }
    public AsyncRelayCommand ChooseCompressionFolderCommand { get; }
    public RelayCommand ChooseCompressionOutputCommand { get; }
    public AsyncRelayCommand AnalyzeCompressionCommand { get; }
    public AsyncRelayCommand CompressCommand { get; }
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

    public override void LoadPreferences(IReadOnlyDictionary<string, string> preferences)
    {
        if (EnumPreference<AudioFormat>(Preference(preferences, "Format")) is { } format && CompressionFormats.Contains(format)) CompressionFormat = format;
        SelectedCompressionProfile = Choice(Preference(preferences, "Profile"), CompressionProfiles) ?? SelectedCompressionProfile;
        CompressionTargetTotalMb = Preference(preferences, "TargetMb") ?? CompressionTargetTotalMb;
        CompressionPreserveMetadata = BoolPreference(Preference(preferences, "PreserveMetadata")) ?? CompressionPreserveMetadata;
        CompressionOptimizeArtwork = BoolPreference(Preference(preferences, "OptimizeArtwork")) ?? CompressionOptimizeArtwork;
    }

    public override void SavePreferences(IDictionary<string, string> preferences)
    {
        preferences[$"{Key}.Format"] = CompressionFormat.ToString();
        preferences[$"{Key}.Profile"] = SelectedCompressionProfile;
        preferences[$"{Key}.TargetMb"] = CompressionTargetTotalMb;
        preferences[$"{Key}.PreserveMetadata"] = CompressionPreserveMetadata.ToString();
        preferences[$"{Key}.OptimizeArtwork"] = CompressionOptimizeArtwork.ToString();
    }

    public override async Task AcceptDropAsync(IReadOnlyList<string> paths) =>
        await ChooseSourceAsync(paths.FirstOrDefault(item => File.Exists(item) || Directory.Exists(item)));

    protected override void RaiseCommands()
    {
        ChooseCompressionFileCommand.RaiseCanExecuteChanged(); ChooseCompressionFolderCommand.RaiseCanExecuteChanged(); AnalyzeCompressionCommand.RaiseCanExecuteChanged(); CompressCommand.RaiseCanExecuteChanged();
    }

    private async Task ChooseSourceAsync(string? path)
    {
        if (path is null) return;
        CompressionSourcePath = path;
        await AnalyzeCompressionAsync();
    }

    private async Task AnalyzeCompressionAsync()
    {
        await RunBusyAsync(async () =>
        {
            var preparation = await PrepareCompressionAsync(1);
            CompressionEstimate = BuildCompressionEstimate(preparation.Plan);
            Report = preparation.Failures.Count == 0
                ? "Analysis completed. No invalid files found."
                : string.Join('\n', preparation.Failures);
            Report += "\n" + string.Join('\n', preparation.ScanWarnings.Select(warning => "SKIP folder: " + warning));
            UpdateOperationProgress(1, $"Compression analysis ready: {preparation.Plan.Files.Count} valid file(s)");
        });
    }

    private async Task CompressAsync()
    {
        await RunBusyAsync(async () =>
        {
            Report = "Compression started.";
            var preparation = await PrepareCompressionAsync(0.1);
            var plan = preparation.Plan;
            var outputFormat = plan.Options.OutputFormat;
            var outputSuffix = CompressionSuffix;
            CompressionEstimate = BuildCompressionEstimate(plan);
            var activeFiles = plan.Files.Where(file => !file.ShouldSkip).ToArray();
            if (activeFiles.Length == 0)
            {
                Report = "All files were skipped because no meaningful size reduction was expected.";
                UpdateOperationProgress(1, "Nothing to compress.");
                return;
            }

            if (outputFormat.IsLossy()
                && activeFiles.Any(file => file.Source.Format.IsLossy())
                && !Dialogs.Confirm("Some source files are already lossy. Re-encoding them can reduce audio quality. Continue?"))
            {
                Host.Status = "Compression cancelled.";
                Host.TimingText = "";
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
                    var output = OutputPathBuilder.BuildCompressed(file.Source.Path, discovered.SourceRoot, preparation.OutputRoot, outputFormat, outputSuffix);
                    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                    var baseDuration = completedDuration;
                    var progress = new Progress<double>(fraction =>
                    {
                        var audioFraction = totalDuration <= 0 ? 1 : (baseDuration + fraction * file.Source.DurationSeconds) / totalDuration;
                        UpdateOperationProgress(0.1 + audioFraction * 0.9, $"Compressing {activeIndex + 1}/{activeFiles.Length}: {Path.GetFileName(file.Source.Path)}");
                    });
                    await Host.Audio!.CompressAsync(file.Source.Path, output, plan.Options, plan.TargetAudioBitrateKbps, file.Source.DurationSeconds, file.Source.HasCoverArt, progress, ActiveOperationToken);
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
                Host.AppendOperationReport(reportLines[^1]);
                activeIndex++;
            }

            var savings = CompressionSavings.Calculate(originalProcessedBytes, outputBytes);
            var skippedCount = plan.Files.Count(file => file.ShouldSkip);
            Report =
                $"Completed: {succeeded} succeeded, {failed} failed, {skippedCount} skipped\n" +
                $"Original processed: {WorkspaceHost.FormatBytes(originalProcessedBytes)}\n" +
                $"Final: {WorkspaceHost.FormatBytes(outputBytes)}\n" +
                $"Saved: {WorkspaceHost.FormatBytes(savings.SavedBytes)} ({savings.ReductionPercent:0.0}%)\n\n" +
                string.Join('\n', reportLines);
            if (succeeded > 0) Host.SetLastOutputFolder(this, preparation.OutputRoot);
            UpdateOperationProgress(1, $"Compression completed: {succeeded} succeeded, {failed} failed, {skippedCount} skipped");
        });
    }

    private async Task<CompressionPreparation> PrepareCompressionAsync(double progressWeight)
    {
        Host.EnsureFfmpeg();
        var outputRoot = ResolveCompressionOutputRoot();
        var scanWarnings = new List<string>();
        var discovered = await Task.Run(() => CompressionFileDiscovery.Find(CompressionSourcePath, recursive: true,
                onSkipped: scanWarnings.Add, cancellationToken: ActiveOperationToken)
            .Where(file => !IsPathInside(file.Path, outputRoot)).ToArray(), ActiveOperationToken);
        foreach (var warning in scanWarnings) Host.AppendOperationReport("SKIP folder: " + warning);
        if (discovered.Length == 0) throw new InvalidDataException("No supported audio file was found.");

        var sources = new List<CompressionSource>();
        var failures = new List<string>();
        for (var index = 0; index < discovered.Length; index++)
        {
            var file = discovered[index];
            try
            {
                var info = await Host.Probe!.ProbeAsync(file.Path, ActiveOperationToken);
                sources.Add(new CompressionSource(file.Path, file.RelativePath, AudioFormats.FromPath(file.Path), info.DurationSeconds, info.SizeBytes ?? new FileInfo(file.Path).Length, info.AudioBitrateKbps, info.HasCoverArt));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) { failures.Add($"FAIL  {file.RelativePath}: {error.Message}"); }
            UpdateOperationProgress((index + 1d) / discovered.Length * progressWeight, $"Analyzing {index + 1}/{discovered.Length}: {Path.GetFileName(file.Path)}");
        }
        if (sources.Count == 0) throw new InvalidDataException("No valid audio file remained after ffprobe validation.");

        var options = new CompressionOptions(CompressionFormat, ParseCompressionProfile(SelectedCompressionProfile), ParseTargetBytes(), CompressionOptimizeArtwork, CompressionPreserveMetadata, CompressionOverwrite);
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
        "High fidelity" => CompressionProfile.HighFidelity,
        "Balanced" => CompressionProfile.Balanced,
        "Maximum reduction" => CompressionProfile.MaximumReduction,
        "Target total size" => CompressionProfile.TargetTotalSize,
        _ => throw new ArgumentException("Unknown compression profile."),
    };

    private static string BuildCompressionEstimate(CompressionPlan plan)
    {
        var saved = Math.Max(0, plan.OriginalTotalBytes - plan.EstimatedOutputBytes);
        var reduction = plan.OriginalTotalBytes == 0 ? 0 : saved * 100d / plan.OriginalTotalBytes;
        var skipped = plan.Files.Count(file => file.ShouldSkip);
        var bitrate = plan.TargetAudioBitrateKbps is null ? "" : $" · {plan.TargetAudioBitrateKbps} kbps shared target";
        return $"Original {WorkspaceHost.FormatBytes(plan.OriginalTotalBytes)} · estimated {WorkspaceHost.FormatBytes(plan.EstimatedOutputBytes)} · save {reduction:0.0}%{bitrate} · {skipped} skipped";
    }

    private sealed record CompressionPreparation(
        CompressionPlan Plan,
        IReadOnlyDictionary<string, CompressionDiscoveredFile> Discovered,
        IReadOnlyList<string> Failures,
        string OutputRoot,
        IReadOnlyList<string> ScanWarnings);
}
