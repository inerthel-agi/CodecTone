using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using AudioConverter.Core.Compression;
using AudioConverter.Core.Loudness;
using AudioConverter.Core.Models;
using AudioConverter.Core.Paths;
using AudioConverter.Desktop.Mvvm;
using AudioConverter.Infrastructure.Normalize;
using AudioConverter.Infrastructure.Remix;

namespace AudioConverter.Desktop.ViewModels;

/// <summary>Loudness normalization: measure each file with loudnorm, plan the gains, then write normalized copies.</summary>
public sealed class NormalizeViewModel : WorkspaceBase
{
    private const string AlbumMode = "Album";
    private const string NoSourceText = "Choose a source. Analyze measures every file; Normalize measures first when needed.";
    private const string NoAnalysisText = "Analyze measures every file before the plan is shown.";
    private static readonly string[] Presets = ["-14 LUFS", "-16 LUFS", "-23 LUFS"];

    private string sourcePath = "";
    private string outputFolder = "";
    private string suffix = "";
    private bool overwrite;
    private bool keepMetadata = true;
    private string? selectedTargetPreset = "-14 LUFS";
    private string customTargetLufs = "";
    private string truePeakCeiling = "-1";
    private string selectedMode = "Per track";
    private string planSummary = NoSourceText;
    private MeasuredSource? measurement;

    public NormalizeViewModel(WorkspaceHost host) : base(host, "Normalize", "ReportEmptyGeneric")
    {
        ChooseFileCommand = new RelayCommand(() => SetSource(Dialogs.ChooseAudioFile()), () => !Host.IsBusy);
        ChooseFolderCommand = new RelayCommand(() => SetSource(Dialogs.ChooseFolder()), () => !Host.IsBusy);
        ChooseOutputCommand = new RelayCommand(() => OutputFolder = Dialogs.ChooseFolder(OutputFolder) ?? OutputFolder, () => !Host.IsBusy);
        AnalyzeCommand = new AsyncRelayCommand(AnalyzeAsync, () => !Host.IsBusy && HasSource);
        NormalizeCommand = new AsyncRelayCommand(NormalizeAsync, () => !Host.IsBusy && HasSource);
    }

    public override ICommand PrimaryCommand => NormalizeCommand;
    public override string PrimaryLabel => "Normalize";
    public RelayCommand ChooseFileCommand { get; }
    public RelayCommand ChooseFolderCommand { get; }
    public RelayCommand ChooseOutputCommand { get; }
    public AsyncRelayCommand AnalyzeCommand { get; }
    public AsyncRelayCommand NormalizeCommand { get; }
    public IReadOnlyList<string> TargetPresets { get; } = Presets;
    public IReadOnlyList<string> Modes { get; } = ["Per track", AlbumMode];
    public ObservableCollection<NormalizeRow> Rows { get; } = new();
    public bool HasRows => Rows.Count > 0;
    public bool IsAnalysisEmpty => Rows.Count == 0;
    public string PlanSummary { get => planSummary; private set => Set(ref planSummary, value); }

    public string SourcePath
    {
        get => sourcePath;
        private set
        {
            if (!Set(ref sourcePath, value)) return;
            measurement = null;
            RefreshPlan();
            RaiseCommands();
        }
    }

    public bool HasSource => !string.IsNullOrWhiteSpace(sourcePath);
    public string OutputFolder { get => outputFolder; set => Set(ref outputFolder, value); }
    public string Suffix { get => suffix; set => Set(ref suffix, value); }
    public bool Overwrite { get => overwrite; set => Set(ref overwrite, value); }
    public bool KeepMetadata { get => keepMetadata; set => Set(ref keepMetadata, value); }

    /// <summary>Picking a preset clears the custom value, and typing a custom value clears the preset.</summary>
    public string? SelectedTargetPreset
    {
        get => selectedTargetPreset;
        set
        {
            if (!Set(ref selectedTargetPreset, value)) return;
            if (value is not null && customTargetLufs.Length > 0) CustomTargetLufs = "";
            RefreshPlan();
        }
    }

    public string CustomTargetLufs
    {
        get => customTargetLufs;
        set
        {
            if (!Set(ref customTargetLufs, value)) return;
            if (value.Trim().Length > 0 && selectedTargetPreset is not null) SelectedTargetPreset = null;
            RefreshPlan();
        }
    }

    public string TruePeakCeiling { get => truePeakCeiling; set { if (Set(ref truePeakCeiling, value)) RefreshPlan(); } }
    public string SelectedMode { get => selectedMode; set { if (Set(ref selectedMode, value)) RefreshPlan(); } }

    public override Task AcceptDropAsync(IReadOnlyList<string> paths)
    {
        SetSource(paths.FirstOrDefault(item => File.Exists(item) || Directory.Exists(item)));
        return Task.CompletedTask;
    }

    protected override void RaiseCommands()
    {
        ChooseFileCommand.RaiseCanExecuteChanged();
        ChooseFolderCommand.RaiseCanExecuteChanged();
        ChooseOutputCommand.RaiseCanExecuteChanged();
        AnalyzeCommand.RaiseCanExecuteChanged();
        NormalizeCommand.RaiseCanExecuteChanged();
    }

    private void SetSource(string? path)
    {
        if (path is not null) SourcePath = path;
    }

    private async Task AnalyzeAsync()
    {
        await RunBusyAsync(async () =>
        {
            Host.EnsureFfmpeg();
            Report = "Analysis started.";
            var (target, ceiling) = ParseSettings();
            var analysis = await MeasureSourceAsync(1, target, ceiling);
            measurement = analysis;
            RefreshPlan();
            Report = AnalysisReport(analysis);
            UpdateOperationProgress(1, $"Analysis ready: {analysis.Files.Count} valid file(s)");
        });
    }

    private async Task NormalizeAsync()
    {
        await RunBusyAsync(async () =>
        {
            Host.EnsureFfmpeg();
            Report = "Normalization started.";
            var (target, ceiling) = ParseSettings();
            var mode = ParseMode();
            // Per-track output needs first-pass values taken at this target and ceiling; album output does not.
            var measuredNow = measurement is null
                || (mode == LoudnessMode.PerTrack && (measurement.Target != target || measurement.Ceiling != ceiling));
            var source = measuredNow ? await MeasureSourceAsync(0.5, target, ceiling) : measurement!;
            measurement = source;
            RefreshPlan();

            var plan = BuildPlan(source.Files, target, ceiling, mode);
            var files = source.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
            var active = plan.Items.Where(item => !item.Skipped).ToArray();
            if (active.Any(item => EncodingFor(files[item.Track.Path]).IsLossy)
                && !Dialogs.Confirm("Normalizing re-encodes lossy files, which can reduce audio quality slightly. Continue?"))
            {
                Host.Status = "Normalization cancelled.";
                Host.TimingText = "";
                return;
            }

            var outputRoot = ResolveOutputRoot();
            var reportLines = new List<string>(source.Failures);
            reportLines.AddRange(source.ScanWarnings.Select(warning => "SKIP folder: " + warning));
            foreach (var skipped in plan.Items.Where(item => item.Skipped))
                reportLines.Add($"SKIP  {skipped.Track.RelativePath}: {skipped.SkipReason}");

            var totalDuration = active.Sum(item => item.Track.DurationSeconds);
            var completedDuration = 0d;
            var succeeded = 0;
            var failed = source.Failures.Count;
            var applyStart = measuredNow ? 0.5 : 0;
            var service = new NormalizeProcessingService(Host.Tools!);
            for (var index = 0; index < active.Length; index++)
            {
                var item = active[index];
                var file = files[item.Track.Path];
                var position = index + 1;
                var baseDuration = completedDuration;
                try
                {
                    var format = AudioFormats.FromPath(file.Path);
                    var output = OutputPathBuilder.BuildCompressed(file.Path, file.SourceRoot, outputRoot, format, Suffix);
                    var job = new NormalizeOutput(file.Path, output, EncodingFor(file), item.Track.DurationSeconds, Overwrite);
                    var progress = new Progress<double>(fraction =>
                    {
                        var share = totalDuration <= 0 ? 1 : (baseDuration + fraction * item.Track.DurationSeconds) / totalDuration;
                        UpdateOperationProgress(applyStart + share * (1 - applyStart), $"Normalizing {position}/{active.Length}: {Path.GetFileName(file.Path)}");
                    });
                    if (mode == LoudnessMode.Album)
                        await service.ApplyAlbumAsync(job, item.GainDb, ceiling, progress, ActiveOperationToken);
                    else
                        await service.ApplyTrackAsync(job, target, ceiling, file.Loudness, progress, ActiveOperationToken);
                    succeeded++;
                    reportLines.Add(OkLine(plan, item, file));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error)
                {
                    failed++;
                    reportLines.Add($"FAIL  {item.Track.RelativePath}: {error.Message}");
                }
                completedDuration += item.Track.DurationSeconds;
                Host.AppendOperationReport(reportLines[^1]);
            }

            var skippedCount = plan.Items.Count(item => item.Skipped);
            Report =
                $"Completed: {succeeded} succeeded, {failed} failed, {skippedCount} skipped\n" +
                DescribePlan(plan) + "\n\n" +
                string.Join('\n', reportLines);
            if (succeeded > 0) Host.SetLastOutputFolder(this, outputRoot);
            UpdateOperationProgress(1, $"Normalization completed: {succeeded} succeeded, {failed} failed, {skippedCount} skipped");
        });
    }

    private async Task<MeasuredSource> MeasureSourceAsync(double progressWeight, double target, double ceiling)
    {
        var outputRoot = ResolveOutputRoot();
        var scanWarnings = new List<string>();
        var discovered = await Task.Run(() => CompressionFileDiscovery.Find(SourcePath, recursive: true,
                onSkipped: scanWarnings.Add, cancellationToken: ActiveOperationToken)
            .Where(file => !IsPathInside(file.Path, outputRoot)).ToArray(), ActiveOperationToken);
        foreach (var warning in scanWarnings) Host.AppendOperationReport("SKIP folder: " + warning);
        if (discovered.Length == 0) throw new InvalidDataException("No supported audio file was found.");

        var service = new NormalizeProcessingService(Host.Tools!);
        var files = new List<MeasuredFile>();
        var failures = new List<string>();
        for (var index = 0; index < discovered.Length; index++)
        {
            var file = discovered[index];
            try
            {
                var info = await Host.Probe!.ProbeAsync(file.Path, ActiveOperationToken);
                var loudness = await service.MeasureAsync(file.Path, target, ceiling, ActiveOperationToken);
                files.Add(new MeasuredFile(file.Path, file.RelativePath, file.SourceRoot, info, loudness));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) { failures.Add($"FAIL  {file.RelativePath}: {error.Message}"); }
            UpdateOperationProgress(progressWeight * (index + 1d) / discovered.Length, $"Measuring {index + 1}/{discovered.Length}: {Path.GetFileName(file.Path)}");
        }
        if (files.Count == 0) throw new InvalidDataException("No valid audio file remained after measurement.");
        return new MeasuredSource(files, failures, scanWarnings, target, ceiling);
    }

    /// <summary>Recomputes the plan from the cached measurements, so target, ceiling and mode can change without measuring again.</summary>
    private void RefreshPlan()
    {
        if (measurement is not { } source)
        {
            PlanSummary = HasSource ? NoAnalysisText : NoSourceText;
            ReplaceRows(Array.Empty<MeasuredFile>(), null);
            return;
        }

        try
        {
            var plan = BuildPlan(source.Files, ParseTargetLufs(), ParseCeiling(), ParseMode());
            PlanSummary = DescribePlan(plan);
            ReplaceRows(source.Files, plan);
        }
        catch (ArgumentException error)
        {
            PlanSummary = error.Message;
            ReplaceRows(source.Files, null);
        }
    }

    private void ReplaceRows(IReadOnlyList<MeasuredFile> files, LoudnessPlan? plan)
    {
        var planned = plan?.Items.ToDictionary(item => item.Track.Path, StringComparer.OrdinalIgnoreCase);
        Rows.Clear();
        foreach (var file in files)
        {
            var item = planned is not null && planned.TryGetValue(file.Path, out var found) ? found : null;
            var note = item is null ? ""
                : item.Skipped ? item.SkipReason ?? ""
                : item.PeakLimited ? "limited by the true-peak ceiling"
                : "";
            Rows.Add(new NormalizeRow(
                file.RelativePath,
                DescribeLoudness(file.Loudness.InputIntegrated),
                DescribePeak(file.Loudness.InputTruePeak),
                item is null ? "n/a" : item.Skipped ? "skipped" : $"{Signed(item.GainDb)} dB",
                note));
        }
        Raise(nameof(HasRows));
        Raise(nameof(IsAnalysisEmpty));
    }

    private static LoudnessPlan BuildPlan(IReadOnlyList<MeasuredFile> files, double target, double ceiling, LoudnessMode mode) =>
        LoudnessPlanner.Create(
            files.Select(file => new LoudnessTrack(
                file.Path,
                file.RelativePath,
                file.Probe.DurationSeconds,
                file.Loudness.InputIntegrated,
                file.Loudness.InputTruePeak)).ToArray(),
            mode,
            target,
            ceiling);

    private NormalizeEncoding EncodingFor(MeasuredFile file) =>
        NormalizeEncoding.FromProbe(AudioFormats.FromPath(file.Path), file.Probe, KeepMetadata);

    private double ParseTargetLufs()
    {
        if (string.IsNullOrWhiteSpace(CustomTargetLufs))
        {
            return selectedTargetPreset is { } preset
                ? PresetLufs(preset)
                : throw new ArgumentException("Choose a target loudness.");
        }
        if (!TryParseNumber(CustomTargetLufs, out var value)) throw new ArgumentException("Custom target must be a number in LUFS.");
        return value;
    }

    private double ParseCeiling() =>
        TryParseNumber(TruePeakCeiling, out var value) ? value : throw new ArgumentException("True-peak ceiling must be a number in dBTP.");

    private (double Target, double Ceiling) ParseSettings()
    {
        var target = ParseTargetLufs();
        var ceiling = ParseCeiling();
        LoudnessPlanner.ValidateSettings(target, ceiling);
        return (target, ceiling);
    }

    private LoudnessMode ParseMode() => SelectedMode == AlbumMode ? LoudnessMode.Album : LoudnessMode.PerTrack;

    private static double PresetLufs(string preset) => preset switch
    {
        "-14 LUFS" => -14,
        "-16 LUFS" => -16,
        "-23 LUFS" => -23,
        _ => throw new ArgumentException("Unknown loudness preset."),
    };

    private static bool TryParseNumber(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
        || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private string ResolveOutputRoot()
    {
        if (!string.IsNullOrWhiteSpace(OutputFolder)) return Path.GetFullPath(OutputFolder);
        return File.Exists(SourcePath)
            ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(SourcePath))!, "normalized")
            : Path.Combine(Path.GetFullPath(SourcePath), "normalized");
    }

    private static string AnalysisReport(MeasuredSource source)
    {
        var lines = new List<string> { $"Analysis completed: {source.Files.Count} valid file(s), {source.Failures.Count} failed." };
        lines.AddRange(source.Failures);
        lines.AddRange(source.ScanWarnings.Select(warning => "SKIP folder: " + warning));
        lines.AddRange(source.Files.Select(file =>
            $"OK  {file.RelativePath}: {DescribeLoudness(file.Loudness.InputIntegrated)}, true peak {DescribePeak(file.Loudness.InputTruePeak)}"));
        return string.Join('\n', lines);
    }

    private static string OkLine(LoudnessPlan plan, LoudnessPlanItem item, MeasuredFile file)
    {
        var name = item.Track.RelativePath;
        var measured = file.Loudness.InputIntegrated;
        var gain = $"{Signed(item.GainDb)} dB";
        if (plan.Mode == LoudnessMode.Album)
        {
            if (LoudnessPlanner.IsSilent(measured)) return $"OK  {name}: silent, {gain} album gain";
            return $"OK  {name}: {Format1(measured)} → {Format1(measured + item.GainDb)} LUFS ({gain}{(plan.GainCapped ? ", capped by peak" : "")})";
        }
        return $"OK  {name}: {Format1(measured)} → {Format1(plan.TargetLufs)} LUFS ({gain}{(item.PeakLimited ? ", peak-limited" : "")})";
    }

    private static string DescribePlan(LoudnessPlan plan)
    {
        var count = plan.Items.Count;
        var target = $"target {Format1(plan.TargetLufs)} LUFS";
        if (plan.Mode == LoudnessMode.PerTrack)
        {
            var limited = plan.Items.Count(item => item.PeakLimited);
            var silent = plan.Items.Count(item => item.Skipped);
            return $"Per track · {count} file(s) · {target} · true-peak ceiling {Format1(plan.CeilingDbtp)} dBTP"
                + (limited > 0 ? $" · {limited} limited by the ceiling" : "")
                + (silent > 0 ? $" · {silent} silent" : "");
        }

        var album = $"Album · {count} file(s) · album loudness {Format1(plan.AlbumLufs ?? 0)} LUFS · gain {Signed(plan.AlbumGainDb ?? 0)} dB";
        return plan.GainCapped
            ? album + $", capped by the {Format1(plan.CeilingDbtp)} dBTP ceiling ({target} not reached)"
            : album + $" · {target}";
    }

    private static string DescribeLoudness(double lufs) =>
        LoudnessPlanner.IsSilent(lufs) ? "silent" : $"{Format1(lufs)} LUFS";

    private static string DescribePeak(double dbtp) =>
        double.IsFinite(dbtp) ? $"{Format1(dbtp)} dBTP" : "n/a";

    private static string Format1(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    private static string Signed(double value) => value.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);

    private sealed record MeasuredFile(string Path, string RelativePath, string SourceRoot, ProbeInfo Probe, LoudnessMeasurements Loudness);

    /// <summary>Measurements plus the target and ceiling they were taken with, which per-track output depends on.</summary>
    private sealed record MeasuredSource(
        IReadOnlyList<MeasuredFile> Files,
        IReadOnlyList<string> Failures,
        IReadOnlyList<string> ScanWarnings,
        double Target,
        double Ceiling);
}

/// <summary>One row of the analysis list. Text is preformatted for the view.</summary>
public sealed record NormalizeRow(string Name, string Loudness, string TruePeak, string Gain, string Note);
