using System.Globalization;
using AudioConverter.Core.Models;
using AudioConverter.Infrastructure.Remix;

namespace AudioConverter.Infrastructure.Normalize;

/// <summary>Codec and container choices for one output. The format always comes from the source file.</summary>
public sealed record NormalizeEncoding(AudioFormat Format, string CodecName, int? BitDepth, int BitrateKbps, bool HasCoverArt, bool KeepMetadata)
{
    /// <summary>Uses the source bitrate for lossy output, 320 kbps when ffprobe reports none.</summary>
    public static NormalizeEncoding FromProbe(AudioFormat format, ProbeInfo probe, bool keepMetadata) =>
        new(format, probe.CodecName, probe.BitDepth, Math.Clamp(probe.AudioBitrateKbps ?? 320, 32, 320), probe.HasCoverArt, keepMetadata);

    public bool IsLossy => Format switch
    {
        AudioFormat.M4a => CodecName != "alac",
        AudioFormat.Ogg => CodecName != "flac",
        _ => Format.IsLossy(),
    };
}

public static class NormalizeCommandBuilder
{
    /// <summary>First loudnorm pass. Its target_offset is only valid for the same target and ceiling, so the apply pass must reuse them.</summary>
    public static string[] BuildMeasure(string inputPath, double targetLufs, double ceilingDbtp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        return
        [
            "-hide_banner", "-nostats", "-i", inputPath, "-map", "0:a:0",
            "-af", $"loudnorm=I={Number(targetLufs)}:TP={Number(ceilingDbtp)}:LRA=11:print_format=json", "-f", "null", "NUL",
        ];
    }

    /// <summary>
    /// Second loudnorm pass in linear mode. loudnorm applies (target − measured_I) + offset, where offset is the first pass's target_offset.
    /// It switches to dynamic gain only when linear gain would push the true peak past the ceiling.
    /// </summary>
    public static string[] BuildTrackPass(
        string inputPath,
        string outputPath,
        NormalizeEncoding encoding,
        double targetLufs,
        double ceilingDbtp,
        LoudnessMeasurements measured)
    {
        var offset = double.IsFinite(measured.TargetOffset) ? measured.TargetOffset : 0;
        var filter = string.Join(':', new[]
        {
            $"loudnorm=I={Number(targetLufs)}",
            $"TP={Number(ceilingDbtp)}",
            "LRA=11",
            $"measured_I={Number(measured.InputIntegrated)}",
            $"measured_TP={Number(measured.InputTruePeak)}",
            $"measured_LRA={Number(measured.InputLoudnessRange)}",
            $"measured_thresh={Number(measured.InputThreshold)}",
            $"offset={Number(offset)}",
            "linear=true",
        });
        return Build(inputPath, outputPath, encoding, filter);
    }

    /// <summary>Album pass: one fixed gain, then a limiter at the ceiling that only catches sample peaks the planner did not see.</summary>
    public static string[] BuildAlbumPass(
        string inputPath,
        string outputPath,
        NormalizeEncoding encoding,
        double gainDb,
        double ceilingDbtp)
    {
        var limit = Math.Pow(10, ceilingDbtp / 20).ToString("0.0000", CultureInfo.InvariantCulture);
        return Build(inputPath, outputPath, encoding, $"volume={Number(gainDb)}dB,alimiter=limit={limit}:level=disabled");
    }

    private static string[] Build(string inputPath, string outputPath, NormalizeEncoding encoding, string filter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-i", inputPath, "-map", "0:a:0" };
        var artwork = encoding.HasCoverArt && SupportsArtwork(encoding.Format);
        if (encoding.KeepMetadata)
        {
            args.AddRange(["-map_metadata", "0"]);
            if (artwork) args.AddRange(["-map", "0:v:disp:attached_pic?"]);
            else args.Add("-vn");
        }
        else
        {
            args.AddRange(["-vn", "-map_metadata", "-1"]);
        }

        args.AddRange(["-af", filter]);
        AddAudioEncoding(args, encoding);
        if (encoding.KeepMetadata && artwork)
        {
            args.AddRange(["-c:v", encoding.Format == AudioFormat.Flac ? "copy" : "mjpeg", "-disposition:v:0", "attached_pic"]);
        }

        args.AddRange(["-progress", "pipe:1", "-nostats"]);
        args.Add(outputPath);
        return [.. args];
    }

    private static void AddAudioEncoding(List<string> args, NormalizeEncoding encoding)
    {
        var bitrate = $"{encoding.BitrateKbps}k";
        switch (encoding.Format)
        {
            case AudioFormat.Mp3:
                args.AddRange(["-c:a", "libmp3lame", "-b:a", bitrate]);
                break;
            case AudioFormat.Aac:
                args.AddRange(["-c:a", "aac", "-b:a", bitrate, "-f", "adts"]);
                break;
            case AudioFormat.Ogg when encoding.CodecName == "flac":
                AddFlac(args, encoding.BitDepth);
                break;
            case AudioFormat.Ogg:
                args.AddRange(["-c:a", "libvorbis", "-b:a", bitrate]);
                break;
            case AudioFormat.M4a when encoding.CodecName == "alac":
                args.AddRange(["-c:a", "alac"]);
                break;
            case AudioFormat.M4a:
                args.AddRange(["-c:a", "aac", "-b:a", bitrate]);
                break;
            case AudioFormat.Flac:
                AddFlac(args, encoding.BitDepth);
                break;
            case AudioFormat.Wav:
                var codec = encoding.BitDepth switch
                {
                    24 => "pcm_s24le",
                    32 => "pcm_s32le",
                    _ => "pcm_s16le",
                };
                args.AddRange(["-c:a", codec]);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(encoding));
        }
    }

    private static void AddFlac(List<string> args, int? bitDepth)
    {
        args.AddRange(["-c:a", "flac"]);
        if (bitDepth == 16) args.AddRange(["-sample_fmt", "s16"]);
        else if (bitDepth == 24) args.AddRange(["-sample_fmt", "s32", "-bits_per_raw_sample", "24"]);
    }

    private static bool SupportsArtwork(AudioFormat format) =>
        format is AudioFormat.Mp3 or AudioFormat.M4a or AudioFormat.Flac;

    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
