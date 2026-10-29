using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AudioConverter.Desktop.Services;

/// <summary>Reads a rendered spectrogram PNG back as data for the cutoff estimate, and as a bitmap for display.</summary>
public static class SpectrogramImage
{
    /// <summary>Mean luminance of each row, 0..1, ordered from 0 Hz (index 0, bottom edge) to Nyquist (last index, top edge).</summary>
    public static double[] ReadRowBrightness(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var pixels = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        var width = pixels.PixelWidth;
        var height = pixels.PixelHeight;
        var stride = width * 4;
        var bytes = new byte[stride * height];
        pixels.CopyPixels(bytes, stride, 0);

        var rows = new double[height];
        for (var y = 0; y < height; y++)
        {
            double sum = 0;
            for (var x = 0; x < width; x++)
            {
                var offset = y * stride + x * 4;
                sum += (0.114 * bytes[offset] + 0.587 * bytes[offset + 1] + 0.299 * bytes[offset + 2]) / 255d;
            }
            rows[height - 1 - y] = sum / width;
        }
        return rows;
    }

    /// <summary>Loads the whole PNG into memory, so the file can be replaced or deleted afterwards.</summary>
    public static BitmapImage Load(string path)
    {
        using var stream = File.OpenRead(path);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
