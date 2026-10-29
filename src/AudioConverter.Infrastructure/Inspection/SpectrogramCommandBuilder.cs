namespace AudioConverter.Infrastructure.Inspection;

public static class SpectrogramCommandBuilder
{
    /// <summary>
    /// One PNG frame of the first audio stream: log amplitude, grayscale intensity (brightness rises with level,
    /// so the analysis can read it back), linear frequency from 0 Hz at the bottom to Nyquist at the top.
    /// </summary>
    public static string[] Build(string inputPath, string outputPath, int width, int height, bool legend)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (width is < 64 or > 8192) throw new ArgumentOutOfRangeException(nameof(width));
        if (height is < 64 or > 8192) throw new ArgumentOutOfRangeException(nameof(height));

        var filter = $"showspectrumpic=s={width}x{height}:legend={(legend ? 1 : 0)}:scale=log:color=intensity:fscale=lin";
        return
        [
            "-hide_banner", "-loglevel", "error", "-y",
            "-i", inputPath,
            "-filter_complex", $"[0:a:0]{filter}[spectrum]",
            "-map", "[spectrum]", "-frames:v", "1",
            outputPath,
        ];
    }
}
