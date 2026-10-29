using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AudioConverter.Desktop.Services;

namespace AudioConverter.Desktop.Tests;

[TestClass]
public sealed class SpectrogramImageTests
{
    [TestMethod]
    public void ReadRowBrightness_OrdersRowsFromBottomFrequencyToTop()
    {
        var path = Path.Combine(Path.GetTempPath(), $"codectone-spectrum-{Guid.NewGuid():N}.png");
        try
        {
            // Top row white, middle gray, bottom black: index 0 must be the bottom (0 Hz) row.
            var pixels = new byte[4 * 3 * 4];
            for (var x = 0; x < 4; x++)
            {
                SetGray(pixels, 0, x, 4, 255);
                SetGray(pixels, 1, x, 4, 128);
                SetGray(pixels, 2, x, 4, 0);
            }
            var bitmap = BitmapSource.Create(4, 3, 96, 96, PixelFormats.Bgra32, null, pixels, 4 * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(path)) encoder.Save(stream);

            var rows = SpectrogramImage.ReadRowBrightness(path);

            Assert.AreEqual(3, rows.Length);
            Assert.AreEqual(0d, rows[0], 0.01);
            Assert.AreEqual(128 / 255d, rows[1], 0.01);
            Assert.AreEqual(1d, rows[2], 0.01);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void SetGray(byte[] pixels, int row, int column, int width, byte value)
    {
        var offset = (row * width + column) * 4;
        pixels[offset] = value;
        pixels[offset + 1] = value;
        pixels[offset + 2] = value;
        pixels[offset + 3] = 255;
    }
}
