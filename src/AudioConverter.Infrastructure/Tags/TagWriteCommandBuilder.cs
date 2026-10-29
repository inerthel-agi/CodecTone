using AudioConverter.Core.Models;

namespace AudioConverter.Infrastructure.Tags;

public static class TagWriteCommandBuilder
{
    public static string[] Build(
        string inputPath,
        string outputPath,
        AudioFormat format,
        IReadOnlyList<KeyValuePair<string, string>> changes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(changes);

        // Every stream, including attached cover art, is copied unchanged; only metadata is rewritten.
        var arguments = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-y", "-i", inputPath,
            "-map", "0", "-c", "copy", "-map_metadata", "0",
        };
        if (format == AudioFormat.Mp3) arguments.AddRange(["-id3v2_version", "3"]);
        foreach (var change in changes) arguments.AddRange(["-metadata", $"{change.Key}={change.Value}"]);
        arguments.Add(outputPath);
        return [.. arguments];
    }
}
