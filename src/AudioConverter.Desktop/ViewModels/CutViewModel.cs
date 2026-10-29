using System.IO;
using System.Windows.Input;
using AudioConverter.Core.Models;
using AudioConverter.Core.Paths;
using AudioConverter.Desktop.Mvvm;
using AudioConverter.Infrastructure.Storage;

namespace AudioConverter.Desktop.ViewModels;

public sealed class CutViewModel : WorkspaceBase
{
    private string cutSourcePath = "";
    private string cutSourceDetails = "";
    private string cutOutputFolder = "";
    private string cutSuffix = "_trimmed";
    private AudioFormat cutFormat = AudioFormat.Flac;
    private bool preserveMetadata = true;
    private bool overwrite;
    private double durationSeconds;
    private double startSeconds;
    private double endSeconds;
    private bool fadeInEnabled;
    private bool fadeOutEnabled;
    private double fadeInSeconds = 1;
    private double fadeOutSeconds = 1;
    private double[] waveformPeaks = [];
    private double cutPlayheadSeconds = double.NaN;

    public CutViewModel(WorkspaceHost host) : base(host, "Cut", "No cut has run yet.")
    {
        ChooseCutFileCommand = new AsyncRelayCommand(async () => { var path = Dialogs.ChooseAudioFile(); if (path is not null) await LoadCutSourceAsync(path); }, () => !Host.IsBusy);
        ChooseCutOutputCommand = new RelayCommand(() => CutOutputFolder = Dialogs.ChooseFolder(CutOutputFolder) ?? CutOutputFolder);
        CutCommand = new AsyncRelayCommand(CutAsync, () => !Host.IsBusy && DurationSeconds > 0);
        PreviewCommand = new AsyncRelayCommand(PreviewAsync, () => !Host.IsBusy && DurationSeconds > 0);
        StopPreviewCommand = new RelayCommand(() => { Host.CancelActiveOperation(); Host.StopPreview(); Host.Status = "Preview stopped."; });
    }

    public override ICommand PrimaryCommand => CutCommand;
    public override string PrimaryLabel => "Cut audio";
    public AsyncRelayCommand ChooseCutFileCommand { get; }
    public RelayCommand ChooseCutOutputCommand { get; }
    public AsyncRelayCommand CutCommand { get; }
    public AsyncRelayCommand PreviewCommand { get; }
    public RelayCommand StopPreviewCommand { get; }
    public string CutSourcePath { get => cutSourcePath; private set => Set(ref cutSourcePath, value); }
    public string CutSourceDetails { get => cutSourceDetails; private set => Set(ref cutSourceDetails, value); }
    public string CutOutputFolder { get => cutOutputFolder; set => Set(ref cutOutputFolder, value); }
    public string CutSuffix { get => cutSuffix; set => Set(ref cutSuffix, value); }
    public AudioFormat CutFormat { get => cutFormat; set => Set(ref cutFormat, value); }
    public bool PreserveMetadata { get => preserveMetadata; set => Set(ref preserveMetadata, value); }
    public bool Overwrite { get => overwrite; set => Set(ref overwrite, value); }
    public double DurationSeconds { get => durationSeconds; private set { if (Set(ref durationSeconds, value)) RaiseCommands(); } }
    public double StartSeconds { get => startSeconds; set { var next = Math.Clamp(value, 0, Math.Max(0, EndSeconds - 0.001)); Set(ref startSeconds, next); Raise(nameof(SelectionDuration)); } }
    public double EndSeconds { get => endSeconds; set { var next = Math.Clamp(value, Math.Min(DurationSeconds, StartSeconds + 0.001), DurationSeconds); Set(ref endSeconds, next); Raise(nameof(SelectionDuration)); } }
    public double SelectionDuration => Math.Max(0, EndSeconds - StartSeconds);
    public bool FadeInEnabled { get => fadeInEnabled; set => Set(ref fadeInEnabled, value); }
    public bool FadeOutEnabled { get => fadeOutEnabled; set => Set(ref fadeOutEnabled, value); }
    public double FadeInSeconds { get => fadeInSeconds; set => Set(ref fadeInSeconds, value); }
    public double FadeOutSeconds { get => fadeOutSeconds; set => Set(ref fadeOutSeconds, value); }
    public double[] WaveformPeaks { get => waveformPeaks; private set => Set(ref waveformPeaks, value); }
    public double CutPlayheadSeconds { get => cutPlayheadSeconds; private set => Set(ref cutPlayheadSeconds, value); }

    public override void LoadPreferences(IReadOnlyDictionary<string, string> preferences)
    {
        CutSuffix = Preference(preferences, "Suffix") ?? CutSuffix;
        PreserveMetadata = BoolPreference(Preference(preferences, "PreserveMetadata")) ?? PreserveMetadata;
    }

    public override void SavePreferences(IDictionary<string, string> preferences)
    {
        preferences[$"{Key}.Suffix"] = CutSuffix;
        preferences[$"{Key}.PreserveMetadata"] = PreserveMetadata.ToString();
    }

    public override async Task AcceptDropAsync(IReadOnlyList<string> paths)
    {
        if (paths.FirstOrDefault(File.Exists) is { } path) await LoadCutSourceAsync(path);
    }

    protected override void RaiseCommands()
    {
        ChooseCutFileCommand.RaiseCanExecuteChanged(); CutCommand.RaiseCanExecuteChanged(); PreviewCommand.RaiseCanExecuteChanged();
    }

    private async Task LoadCutSourceAsync(string path)
    {
        await RunBusyAsync(async () =>
        {
            Host.EnsureFfmpeg(); _ = AudioFormats.FromPath(path); Host.Status = "Reading audio and generating waveform…";
            var info = await Host.Probe!.ProbeAsync(path, ActiveOperationToken); var peaks = await Host.Audio!.ExtractWaveformAsync(path, 900, info.DurationSeconds, ActiveOperationToken);
            CutSourcePath = path; CutSourceDetails = WorkspaceHost.DescribeSource(info); DurationSeconds = info.DurationSeconds; StartSeconds = 0; EndSeconds = info.DurationSeconds;
            CutFormat = AudioFormats.FromPath(path); WaveformPeaks = peaks; UpdateOperationProgress(1, $"Ready to cut {Path.GetFileName(path)}");
        });
    }

    private async Task CutAsync()
    {
        await RunBusyAsync(async () =>
        {
            Host.EnsureFfmpeg(); var trim = CreateTrim(); var directory = string.IsNullOrWhiteSpace(CutOutputFolder) ? Path.Combine(Path.GetDirectoryName(CutSourcePath)!, "converted") : CutOutputFolder; Directory.CreateDirectory(directory);
            var output = OutputPathBuilder.Build(CutSourcePath, directory, CutFormat, CutSuffix);
            var options = new ConversionOptions(CutFormat, CutFormat.IsLossy() ? "192k" : null, PreserveMetadata: PreserveMetadata, Overwrite: Overwrite);
            var itemProgress = new Progress<double>(fraction => UpdateOperationProgress(fraction, "Cutting audio…"));
            await Host.Audio!.TrimAsync(CutSourcePath, output, options, trim, itemProgress, ActiveOperationToken);
            Report = $"Cut completed: {Path.GetFileName(output)}\nOK  {CutSourcePath} -> {output}";
            Host.SetLastOutputFolder(this, directory);
            UpdateOperationProgress(1, $"Cut completed: {Path.GetFileName(output)}");
        });
    }

    private async Task PreviewAsync()
    {
        await RunBusyAsync(async () =>
        {
            Host.EnsureFfmpeg(); Host.StopPreview(); var output = AppPaths.PreviewWave;
            Host.Status = "Rendering local preview…";
            var previewProgress = new Progress<double>(fraction => UpdateOperationProgress(fraction, "Rendering local preview…"));
            await Host.Audio!.RenderPreviewAsync(CutSourcePath, output, CreateTrim(), previewProgress, ActiveOperationToken);
            Host.PlayPreview(output, position => CutPlayheadSeconds = position, StartSeconds, SelectionDuration, 1);
            UpdateOperationProgress(1, "Playing cut preview.");
        });
    }

    private TrimSelection CreateTrim() => TrimSelection.Create(StartSeconds, EndSeconds, FadeInEnabled ? FadeInSeconds : 0, FadeOutEnabled ? FadeOutSeconds : 0);
}
