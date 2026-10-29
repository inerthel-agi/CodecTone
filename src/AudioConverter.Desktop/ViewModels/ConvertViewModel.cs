using System.IO;
using System.Windows.Input;
using AudioConverter.Core.Models;
using AudioConverter.Core.Paths;
using AudioConverter.Desktop.Mvvm;

namespace AudioConverter.Desktop.ViewModels;

public sealed class ConvertViewModel : WorkspaceBase
{
    private string sourcePath = "";
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
        ChooseFileCommand = new RelayCommand(() => { var path = Dialogs.ChooseAudioFile(); if (path is not null) SourcePath = path; });
        ChooseFolderCommand = new RelayCommand(() => { var path = Dialogs.ChooseFolder(); if (path is not null) SourcePath = path; });
        ChooseOutputCommand = new RelayCommand(() => { var path = Dialogs.ChooseFolder(OutputFolder); if (path is not null) OutputFolder = path; });
        ConvertCommand = new AsyncRelayCommand(ConvertAsync, () => !Host.IsBusy && !string.IsNullOrWhiteSpace(SourcePath));
    }

    public override ICommand PrimaryCommand => ConvertCommand;
    public override string PrimaryLabel => "Convert audio";
    public RelayCommand ChooseFileCommand { get; }
    public RelayCommand ChooseFolderCommand { get; }
    public RelayCommand ChooseOutputCommand { get; }
    public AsyncRelayCommand ConvertCommand { get; }
    public string SourcePath { get => sourcePath; set { if (Set(ref sourcePath, value)) ConvertCommand.RaiseCanExecuteChanged(); } }
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

    public override Task AcceptDropAsync(IReadOnlyList<string> paths)
    {
        if (paths.FirstOrDefault(item => File.Exists(item) || Directory.Exists(item)) is { } path) SourcePath = path;
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
            var files = EnumerateSources(SourcePath).ToArray();
            if (files.Length == 0) throw new InvalidDataException("No supported audio file was found.");
            var results = new List<FileResult>();
            for (var index = 0; index < files.Length; index++)
            {
                var file = files[index];
                try
                {
                    _ = AudioFormats.FromPath(file);
                    var info = await Host.Probe!.ProbeAsync(file, ActiveOperationToken);
                    var directory = ResolveOutputDirectory(file, configuredOutput);
                    Directory.CreateDirectory(directory);
                    var output = OutputPathBuilder.Build(file, directory, format, configuredSuffix);
                    var itemProgress = new Progress<double>(fraction => UpdateOperationProgress((index + fraction) / files.Length, $"Converting {index + 1}/{files.Length}: {Path.GetFileName(file)}"));
                    await Host.Audio!.ConvertAsync(file, output, options, info.DurationSeconds, itemProgress, ActiveOperationToken);
                    results.Add(new FileResult(file, output, null));
                    Host.AppendOperationReport($"OK  {file} -> {output}");
                    Host.SetLastOutputFolder(this, directory);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error) { results.Add(new FileResult(file, null, error.Message)); Host.AppendOperationReport($"FAIL  {file}: {error.Message}"); }
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
    private static IEnumerable<string> EnumerateSources(string source) => File.Exists(source) ? [source] : Directory.Exists(source) ? Directory.EnumerateFiles(source).Where(path => { try { _ = AudioFormats.FromPath(path); return true; } catch { return false; } }) : [];
}
