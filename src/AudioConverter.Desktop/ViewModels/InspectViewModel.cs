using System.Globalization;
using System.IO;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using AudioConverter.Core.Inspection;
using AudioConverter.Core.Models;
using AudioConverter.Core.Validation;
using AudioConverter.Desktop.Mvvm;
using AudioConverter.Desktop.Services;
using AudioConverter.Infrastructure.Inspection;
using AudioConverter.Infrastructure.Storage;

namespace AudioConverter.Desktop.ViewModels;

public sealed class InspectViewModel : WorkspaceBase
{
    /// <summary>On-screen spectrogram height; the frequency labels are placed against it.</summary>
    private const double SpectrogramHeight = 260;
    private const double LabelHeight = 16;
    private const int AnalysisWidth = 1024;
    private const int AnalysisHeight = 512;
    private const int SaveWidth = 1600;
    private const int SaveHeight = 800;
    private const int MaximumTagLength = 240;

    private readonly string workDirectory = Path.Combine(AppPaths.Root, "inspect", Guid.NewGuid().ToString("N"));
    private string inspectSourcePath = "";
    private string inspectSourceDetails = "";
    private string durationLabel = "";
    private string cutoffVerdict = "Choose a file to check for a lossy cutoff.";
    private double sourceDurationSeconds;
    private BitmapImage? spectrogram;
    private IReadOnlyList<SpectrogramLabel> axisLabels = [];
    private IReadOnlyList<InspectDetail> fileDetails = [];
    private IReadOnlyList<InspectDetail> tags = [];

    public InspectViewModel(WorkspaceHost host) : base(host, "Inspect", "Nothing has run yet.")
    {
        ChooseInspectFileCommand = new AsyncRelayCommand(async () => { var path = Dialogs.ChooseAudioFile(); if (path is not null) await LoadAsync(path); }, () => !Host.IsBusy);
        SaveSpectrogramCommand = new AsyncRelayCommand(SaveSpectrogramAsync, () => !Host.IsBusy && Spectrogram is not null);
    }

    public override ICommand PrimaryCommand => SaveSpectrogramCommand;
    public override string PrimaryLabel => "Save spectrogram";
    public AsyncRelayCommand ChooseInspectFileCommand { get; }
    public AsyncRelayCommand SaveSpectrogramCommand { get; }
    public double SpectrogramHeightPx => SpectrogramHeight;
    public string InspectSourcePath { get => inspectSourcePath; private set => Set(ref inspectSourcePath, value); }
    public string InspectSourceDetails { get => inspectSourceDetails; private set => Set(ref inspectSourceDetails, value); }
    public BitmapImage? Spectrogram { get => spectrogram; private set => Set(ref spectrogram, value); }
    public IReadOnlyList<SpectrogramLabel> AxisLabels { get => axisLabels; private set => Set(ref axisLabels, value); }
    public string DurationLabel { get => durationLabel; private set => Set(ref durationLabel, value); }
    public string CutoffVerdict { get => cutoffVerdict; private set => Set(ref cutoffVerdict, value); }
    public IReadOnlyList<InspectDetail> FileDetails { get => fileDetails; private set => Set(ref fileDetails, value); }
    public IReadOnlyList<InspectDetail> Tags { get => tags; private set { if (Set(ref tags, value)) Raise(nameof(HasNoTags)); } }
    public bool HasNoTags => tags.Count == 0;

    public override async Task AcceptDropAsync(IReadOnlyList<string> paths)
    {
        if (paths.FirstOrDefault(File.Exists) is { } path) await LoadAsync(path);
    }

    public override Task ShutdownAsync()
    {
        try { if (Directory.Exists(workDirectory)) Directory.Delete(workDirectory, true); }
        catch (IOException error) { Host.Log.Write("Cleanup", error.Message); }
        catch (UnauthorizedAccessException error) { Host.Log.Write("Cleanup", error.Message); }
        return Task.CompletedTask;
    }

    protected override void RaiseCommands()
    {
        ChooseInspectFileCommand.RaiseCanExecuteChanged();
        SaveSpectrogramCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Shows details, spectrogram and verdict for a file whose analysis PNG is already rendered.</summary>
    public void ShowAnalysis(string path, ProbeInfo info, string analysisImagePath)
    {
        var nyquist = (info.SampleRate ?? 44_100) / 2d;
        var cutoff = SpectrumCutoffEstimator.Estimate(SpectrogramImage.ReadRowBrightness(analysisImagePath), nyquist);
        var verdict = SourceVerdict.Create(info.CodecName, cutoff, nyquist);

        InspectSourcePath = path;
        InspectSourceDetails = WorkspaceHost.DescribeSource(info);
        FileDetails = DescribeFile(path, info);
        Tags = info.Tags
            .OrderBy(tag => tag.Key, StringComparer.OrdinalIgnoreCase)
            .Select(tag => new InspectDetail(tag.Key, Shorten(tag.Value)))
            .ToArray();
        Spectrogram = SpectrogramImage.Load(analysisImagePath);
        AxisLabels = SpectrogramAxis.Create(nyquist)
            .Select(tick => new SpectrogramLabel(tick.Label, (1 - tick.Fraction) * (SpectrogramHeight - LabelHeight)))
            .ToArray();
        DurationLabel = TimestampParser.Format(info.DurationSeconds);
        sourceDurationSeconds = info.DurationSeconds;
        CutoffVerdict = verdict.Message;
        Report = string.Join('\n', new[]
        {
            "OK  Spectrogram rendered",
            verdict.Kind switch
            {
                SourceVerdictKind.LikelyLossySource => "FAIL  " + verdict.Message,
                SourceVerdictKind.FullBand => "OK  " + verdict.Message,
                _ => verdict.Message,
            },
        });
        RaiseCommands();
    }

    private async Task LoadAsync(string path)
    {
        await RunBusyAsync(async () =>
        {
            Host.EnsureFfmpeg();
            Host.Status = "Reading audio…";
            var info = await Host.Probe!.ProbeAsync(path, ActiveOperationToken);
            UpdateOperationProgress(0.3, "Drawing spectrogram…");
            var analysisImage = Path.Combine(workDirectory, "analysis.png");
            await new SpectrogramService(Host.Tools!).RenderAsync(
                path, analysisImage, AnalysisWidth, AnalysisHeight, legend: false, durationSeconds: info.DurationSeconds, cancellationToken: ActiveOperationToken);
            ShowAnalysis(path, info, analysisImage);
            UpdateOperationProgress(1, $"Spectrogram ready: {Path.GetFileName(path)}");
        });
    }

    private async Task SaveSpectrogramAsync()
    {
        var target = Dialogs.ChooseSaveFile("PNG image|*.png", Path.GetFileNameWithoutExtension(InspectSourcePath) + "-spectrogram.png");
        if (target is null) return;
        await RunBusyAsync(async () =>
        {
            Host.EnsureFfmpeg();
            UpdateOperationProgress(0.2, "Rendering spectrogram with legend…");
            await new SpectrogramService(Host.Tools!).RenderAsync(
                InspectSourcePath, target, SaveWidth, SaveHeight, legend: true, durationSeconds: sourceDurationSeconds, cancellationToken: ActiveOperationToken);
            Host.SetLastOutputFolder(this, Path.GetDirectoryName(Path.GetFullPath(target))!);
            AppendReport($"OK  Spectrogram saved: {target}");
            UpdateOperationProgress(1, $"Spectrogram saved: {Path.GetFileName(target)}");
        });
    }

    private static IReadOnlyList<InspectDetail> DescribeFile(string path, ProbeInfo info) =>
    [
        new("Codec", info.CodecName.ToUpperInvariant()),
        new("Container", Path.GetExtension(path).TrimStart('.').ToUpperInvariant()),
        new("Audio bitrate", info.AudioBitrateKbps is { } kbps ? $"{kbps} kbps" : "Not reported"),
        new("Sample rate", info.SampleRate is { } rate ? (rate / 1000d).ToString("0.##", CultureInfo.InvariantCulture) + " kHz" : "Not reported"),
        new("Bit depth", info.BitDepth is { } depth ? $"{depth}-bit" : "Not reported"),
        new("Channels", ChannelLabel(info.Channels)),
        new("Duration", TimestampParser.Format(info.DurationSeconds)),
        new("File size", info.SizeBytes is { } size ? WorkspaceHost.FormatBytes(size) : "Unknown"),
    ];

    private static string ChannelLabel(int? channels) => channels switch
    {
        null => "Not reported",
        1 => "Mono (1)",
        2 => "Stereo (2)",
        int count => $"{count} channels",
    };

    private static string Shorten(string value)
    {
        var flat = string.Join(' ', value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return flat.Length <= MaximumTagLength ? flat : flat[..MaximumTagLength] + "…";
    }
}

/// <summary>One key/value row in the details or tags list.</summary>
public sealed record InspectDetail(string Label, string Value);

/// <summary>A frequency label; Top is its offset from the top of the spectrogram.</summary>
public sealed record SpectrogramLabel(string Text, double Top);
