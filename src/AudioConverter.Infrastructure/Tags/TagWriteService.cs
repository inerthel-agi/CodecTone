using AudioConverter.Core.Models;
using AudioConverter.Infrastructure.Ffmpeg;

namespace AudioConverter.Infrastructure.Tags;

/// <summary>Rewrites a file's tags by stream copy into a staged file beside it, validates the result, then replaces the original.</summary>
public sealed class TagWriteService(FfmpegTools tools)
{
    private readonly FfmpegProcessRunner runner = new();
    private readonly FfprobeService probe = new(tools.FfprobePath);

    public async Task WriteAsync(
        string path,
        IReadOnlyList<KeyValuePair<string, string>> changes,
        bool hasCoverArt,
        double durationSeconds,
        CancellationToken cancellationToken = default)
    {
        var format = AudioFormats.FromPath(path);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        var staged = Path.Combine(directory, $".{Path.GetFileNameWithoutExtension(fullPath)}.{Guid.NewGuid():N}.tmp{Path.GetExtension(fullPath)}");
        try
        {
            await runner.RunAsync(tools.FfmpegPath, TagWriteCommandBuilder.Build(fullPath, staged, format, changes), 0, null, cancellationToken);
            var info = await probe.ProbeAsync(staged, cancellationToken);
            if (info.DurationSeconds < MinimumDuration(durationSeconds)) throw new InvalidDataException("The written file did not contain valid audio.");
            if (hasCoverArt && !info.HasCoverArt) throw new InvalidDataException("The embedded cover art was not kept.");
            File.Replace(staged, fullPath, null);
        }
        finally
        {
            try { if (File.Exists(staged)) File.Delete(staged); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static double MinimumDuration(double durationSeconds) =>
        durationSeconds <= 0 ? 0.001 : Math.Max(0.001, durationSeconds - Math.Max(0.05, durationSeconds * 0.02));
}
