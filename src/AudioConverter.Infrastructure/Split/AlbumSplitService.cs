using System.Diagnostics;
using AudioConverter.Core.Models;
using AudioConverter.Core.Split;
using AudioConverter.Infrastructure.Ffmpeg;

namespace AudioConverter.Infrastructure.Split;

/// <summary>
/// Finds silences and writes album tracks with FFmpeg. Each track is written to a staging file and only
/// moved into place after it probes as valid audio, so a failed track never replaces a good file.
/// </summary>
public sealed class AlbumSplitService(FfmpegTools tools)
{
    private readonly FfmpegProcessRunner runner = new();
    private readonly FfprobeService probe = new(tools.FfprobePath);

    public async Task<IReadOnlyList<SilenceInterval>> DetectSilenceAsync(
        string input,
        double noiseDb,
        double minimumSilenceSeconds,
        double durationSeconds,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var info = FfmpegProcessRunner.CreateStartInfo(
            tools.FfmpegPath,
            AlbumSplitCommandBuilder.BuildSilenceDetect(input, noiseDb, minimumSilenceSeconds),
            redirectOutput: true);
        using var process = new Process { StartInfo = info };
        try
        {
            if (!process.Start()) throw new FfmpegExecutionException("FFmpeg could not start.");
        }
        catch (Exception error) when (error is not FfmpegExecutionException)
        {
            throw new FfmpegExecutionException($"Unable to start FFmpeg: {error.Message}");
        }

        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var waitTask = FfmpegProcessRunner.WaitForExitOrKillAsync(process, cancellationToken);
        try
        {
            while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
            {
                if (FfmpegProgressParser.Parse(line.Trim(), durationSeconds) is { } fraction) progress?.Report(fraction);
            }
            await waitTask;
        }
        catch (OperationCanceledException)
        {
            try { await waitTask; } catch (OperationCanceledException) { }
            throw;
        }

        var stderr = await stderrTask;
        if (process.ExitCode != 0)
        {
            var tail = string.Join(Environment.NewLine, stderr.Split('\n').TakeLast(8)).Trim();
            throw new FfmpegExecutionException($"Silence detection failed (exit code {process.ExitCode}): {tail}");
        }
        progress?.Report(1);
        return SilenceDetectParser.Parse(stderr, durationSeconds);
    }

    public async Task WriteTrackAsync(
        string input,
        string output,
        ConversionOptions options,
        AlbumTrack track,
        AlbumTrackTags tags,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);
        var trim = TrimSelection.Create(track.StartSeconds, track.EndSeconds);
        // Same tolerance as AudioProcessingService: encoders may trim a few milliseconds.
        var minimumDuration = Math.Max(0.001, trim.DurationSeconds - Math.Max(0.05, trim.DurationSeconds * 0.02));
        await TransactionalAudioOutput.RunAsync(
            input,
            output,
            options.Overwrite,
            (staged, token) => runner.RunAsync(
                tools.FfmpegPath,
                AlbumSplitCommandBuilder.BuildTrack(input, staged, options with { Overwrite = true }, trim, tags, showProgress: true),
                trim.DurationSeconds,
                progress,
                token),
            async (staged, token) =>
            {
                var info = await probe.ProbeAsync(staged, token);
                if (info.DurationSeconds < minimumDuration)
                    throw new InvalidDataException("Encoded track did not contain the expected audio.");
            },
            cancellationToken);
        progress?.Report(1);
    }
}
