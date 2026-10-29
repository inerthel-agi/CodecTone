using System.Globalization;
using System.Text.RegularExpressions;
using AudioConverter.Core.Split;

namespace AudioConverter.Infrastructure.Split;

/// <summary>Reads the silence_start and silence_end lines that FFmpeg's silencedetect filter writes to stderr.</summary>
public static partial class SilenceDetectParser
{
    public static IReadOnlyList<SilenceInterval> Parse(string stderr, double durationSeconds)
    {
        ArgumentNullException.ThrowIfNull(stderr);
        var silences = new List<SilenceInterval>();
        double? openStart = null;
        foreach (var line in stderr.Split('\n'))
        {
            if (StartPattern().Match(line) is { Success: true } start && TryParseSeconds(start.Groups[1].Value, out var begin))
            {
                openStart = Math.Max(0d, begin);
            }
            else if (EndPattern().Match(line) is { Success: true } end && TryParseSeconds(end.Groups[1].Value, out var finish))
            {
                // A silence_end without a start means the silence began at the very beginning.
                var from = openStart ?? 0d;
                openStart = null;
                if (finish > from) silences.Add(new SilenceInterval(from, finish));
            }
        }

        // A silence still open at the end of the input runs to the end of the file.
        if (openStart is { } trailing && durationSeconds > trailing)
            silences.Add(new SilenceInterval(trailing, durationSeconds));
        return silences;
    }

    private static bool TryParseSeconds(string text, out double seconds) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) && double.IsFinite(seconds);

    [GeneratedRegex(@"silence_start:\s*(-?\d+(?:\.\d+)?(?:e[+-]?\d+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex StartPattern();

    [GeneratedRegex(@"silence_end:\s*(-?\d+(?:\.\d+)?(?:e[+-]?\d+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex EndPattern();
}
