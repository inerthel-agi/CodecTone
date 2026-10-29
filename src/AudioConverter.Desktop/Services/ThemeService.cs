using System.Windows;

namespace AudioConverter.Desktop.Services;

public static class ThemeService
{
    /// <summary>Theme cycle order; the names match Themes/*.xaml and the value stored in settings.json.</summary>
    public static IReadOnlyList<string> Themes { get; } = ["Oled", "White", "Glass"];

    /// <summary>Known theme name for a stored value, falling back to OLED for unknown values.</summary>
    public static string Normalize(string? theme) => Themes.FirstOrDefault(name => name.Equals(theme, StringComparison.OrdinalIgnoreCase)) ?? Themes[0];
    public static string Next(string? theme) => Themes[(Themes.ToList().IndexOf(Normalize(theme)) + 1) % Themes.Count];
    public static string DisplayName(string? theme) => Normalize(theme) switch { "Oled" => "OLED", var name => name };

    public static void Apply(string theme)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        if (dictionaries.Count == 0) return;
        var name = Normalize(theme);
        var backdrop = Application.Current.MainWindow is { } window && TitleBarTheme.Apply(window, name);
        dictionaries[0] = LoadPalette(name, backdrop);
    }

    /// <summary>
    /// Loads Themes/{theme}.xaml. Glass needs the Windows 11 acrylic backdrop; without it (Windows 10, early Windows 11,
    /// transparency effects off) the window background becomes an opaque tint so text keeps its contrast.
    /// </summary>
    public static ResourceDictionary LoadPalette(string theme, bool backdrop)
    {
        var name = Normalize(theme);
        var palette = new ResourceDictionary { Source = new Uri($"pack://application:,,,/CodecTone;component/Themes/{name}.xaml") };
        if (name == "Glass" && !backdrop) palette["BackdropBrush"] = palette["GlassFallbackBrush"];
        return palette;
    }

    /// <summary>Makes every transition instant. Call before the first window loads; styles read the durations once.</summary>
    public static void DisableMotion(ResourceDictionary resources)
    {
        foreach (var dictionary in resources.MergedDictionaries.Where(dictionary => dictionary.Contains("MotionFast")))
        {
            dictionary["MotionFast"] = new Duration(TimeSpan.Zero);
            dictionary["MotionMedium"] = new Duration(TimeSpan.Zero);
        }
    }
}
