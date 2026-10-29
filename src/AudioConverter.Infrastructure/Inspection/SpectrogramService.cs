using AudioConverter.Infrastructure.Ffmpeg;

namespace AudioConverter.Infrastructure.Inspection;

public sealed class SpectrogramService(FfmpegTools tools)
{
    private readonly FfmpegProcessRunner runner = new();

    public Task RenderAsync(
        string input,
        string output,
        int width,
        int height,
        bool legend,
        double durationSeconds,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var arguments = SpectrogramCommandBuilder.Build(input, output, width, height, legend);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        return runner.RunAsync(tools.FfmpegPath, arguments, durationSeconds, progress, cancellationToken);
    }
}
