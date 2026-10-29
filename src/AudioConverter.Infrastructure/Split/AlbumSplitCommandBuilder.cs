using System.Globalization;
using AudioConverter.Core.Models;
using AudioConverter.Infrastructure.Ffmpeg;

namespace AudioConverter.Infrastructure.Split;

public static class AlbumSplitCommandBuilder
{
    public static string[] BuildSilenceDetect(string inputPath, double noiseDb, double minimumSilenceSeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        if (!double.IsFinite(noiseDb) || noiseDb > 0) throw new ArgumentOutOfRangeException(nameof(noiseDb), "The threshold must be 0 dB or lower.");
        if (!double.IsFinite(minimumSilenceSeconds) || minimumSilenceSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(minimumSilenceSeconds));
        var filter = string.Format(CultureInfo.InvariantCulture, "silencedetect=noise={0:0.##}dB:d={1:0.###}", noiseDb, minimumSilenceSeconds);
        return ["-hide_banner", "-nostats", "-progress", "pipe:1", "-i", inputPath, "-map", "0:a:0", "-af", filter, "-f", "null", "-"];
    }

    /// <summary>
    /// Cuts one track with the same filters as a trim, then writes its tags. Source chapters are dropped
    /// because their timestamps would point outside the track.
    /// </summary>
    public static string[] BuildTrack(
        string inputPath,
        string outputPath,
        ConversionOptions options,
        TrimSelection trim,
        AlbumTrackTags tags,
        bool showProgress)
    {
        ArgumentNullException.ThrowIfNull(tags);
        var args = FfmpegCommandBuilder.BuildTrim(inputPath, outputPath, options, trim, showProgress).ToList();
        args.RemoveAt(args.Count - 1);
        args.AddRange(["-map_chapters", "-1"]);
        foreach (var (key, value) in tags.ToMetadata()) args.AddRange(["-metadata", $"{key}={value}"]);
        args.Add(outputPath);
        return [.. args];
    }
}
