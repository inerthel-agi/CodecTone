using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AudioConverter.Core.Models;
using AudioConverter.Desktop.Services;
using AudioConverter.Desktop.ViewModels;

namespace AudioConverter.Desktop.Tests;

[TestClass]
public sealed class InspectViewModelTests
{
    [TestMethod]
    public void ShowAnalysis_LosslessFileWithLossyShelf_FlagsVerdictDetailsAndTags()
    {
        using var host = new WorkspaceHost(new TestDialogs());
        var inspect = new InspectViewModel(host);
        var image = WriteSpectrogram("inspect-shelf.png", hz => hz <= 16_000 ? 170 : 6);

        inspect.ShowAnalysis(@"C:\Music\Example Album\01 Example Track.flac", SampleProbe(), image);

        StringAssert.StartsWith(inspect.CutoffVerdict, "Likely made from a lossy source: energy stops near 15.9");
        StringAssert.Contains(inspect.Report, "OK  Spectrogram rendered");
        StringAssert.Contains(inspect.Report, "FAIL  Likely made from a lossy source");
        Assert.AreEqual("FLAC", Detail(inspect, "Codec"));
        Assert.AreEqual("FLAC", Detail(inspect, "Container"));
        Assert.AreEqual("Not reported", Detail(inspect, "Audio bitrate"));
        Assert.AreEqual("44.1 kHz", Detail(inspect, "Sample rate"));
        Assert.AreEqual("Stereo (2)", Detail(inspect, "Channels"));
        Assert.AreEqual("0 Hz", inspect.AxisLabels[0].Text);
        Assert.AreEqual(244, inspect.AxisLabels[0].Top, 1e-9);
        Assert.AreEqual("22.05 kHz", inspect.AxisLabels[^1].Text);
        Assert.AreEqual(0, inspect.AxisLabels[^1].Top, 1e-9);
        CollectionAssert.AreEqual(new[] { "ALBUM", "ARTIST", "COMMENT", "TITLE" }, inspect.Tags.Select(tag => tag.Label).ToArray());
        var comment = inspect.Tags.Single(tag => tag.Label == "COMMENT").Value;
        Assert.IsTrue(comment.Length <= 241 && comment.EndsWith('…'), comment);
        Assert.IsFalse(inspect.HasNoTags);
        Assert.IsNotNull(inspect.Spectrogram);
        Assert.IsTrue(inspect.PrimaryCommand.CanExecute(null));
    }

    [TestMethod]
    public void ShowAnalysis_FlatSpectrumWithoutTags_ReportsFullBandAndNoTags()
    {
        using var host = new WorkspaceHost(new TestDialogs());
        var inspect = new InspectViewModel(host);
        var image = WriteSpectrogram("inspect-flat.png", _ => 170);
        var info = SampleProbe() with { CodecName = "pcm_s16le", Tags = new Dictionary<string, string>() };

        inspect.ShowAnalysis(@"C:\Music\Master.wav", info, image);

        Assert.AreEqual("Full-band audio up to 22.05 kHz.", inspect.CutoffVerdict);
        StringAssert.Contains(inspect.Report, "OK  Full-band audio up to 22.05 kHz.");
        Assert.IsTrue(inspect.HasNoTags);
    }

    private static string Detail(InspectViewModel inspect, string label) =>
        inspect.FileDetails.Single(detail => detail.Label == label).Value;

    /// <summary>A spectrogram-shaped PNG whose brightness depends only on frequency (top row = Nyquist).</summary>
    private static string WriteSpectrogram(string name, Func<double, int> brightnessAtHz)
    {
        const int width = 64;
        const int height = 512;
        var pixels = new byte[width * height * 4];
        for (var row = 0; row < height; row++)
        {
            var value = (byte)brightnessAtHz((height - 1 - row) / (double)(height - 1) * 22_050);
            for (var column = 0; column < width; column++)
            {
                var offset = (row * width + column) * 4;
                pixels[offset] = value;
                pixels[offset + 1] = value;
                pixels[offset + 2] = value;
                pixels[offset + 3] = 255;
            }
        }
        var path = Path.Combine(AppContext.BaseDirectory, name);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4)));
        using (var stream = File.Create(path)) encoder.Save(stream);
        return path;
    }

    private static ProbeInfo SampleProbe() => new(
        DurationSeconds: 245.3,
        CodecName: "flac",
        SampleRate: 44_100,
        BitDepth: 24,
        HasCoverArt: true,
        Tags: new Dictionary<string, string>
        {
            ["TITLE"] = "Example Track",
            ["ARTIST"] = "Example Artist",
            ["ALBUM"] = "Example Album",
            ["COMMENT"] = "Lorem ipsum dolor sit amet,\r\nconsectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore.",
        },
        SizeBytes: 38_500_000,
        Channels: 2);

    private sealed class TestDialogs : IDialogService
    {
        public string? ChooseAudioFile() => null;
        public IReadOnlyList<string>? ChooseAudioFiles() => null;
        public string? ChooseFile(string filter) => null;
        public string? ChooseSaveFile(string filter, string fileName) => null;
        public string? ChooseFolder(string? initialDirectory = null) => null;
        public string? ChooseCoverImage() => null;
        public void Error(string message) => Assert.Fail(message);
        public bool Confirm(string message) => false;
    }
}
