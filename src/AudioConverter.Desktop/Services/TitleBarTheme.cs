using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace AudioConverter.Desktop.Services;

public static class TitleBarTheme
{
    private const int UseImmersiveDarkMode = 20;
    private const int BorderColor = 34;
    private const int CaptionColor = 35;
    private const int TextColor = 36;
    private const int SystemBackdropType = 38;
    private const int BackdropNone = 1;
    private const int BackdropAcrylic = 3;
    private const uint DefaultColor = 0xFFFFFFFF;

    /// <summary>Colors the title bar for the theme. For Glass, returns true only when Windows accepted the acrylic backdrop.</summary>
    public static bool Apply(Window window, string theme)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == nint.Zero)
        {
            window.SourceInitialized += (_, _) => Apply(window, theme);
            return false;
        }

        var glass = theme.Equals("Glass", StringComparison.OrdinalIgnoreCase);
        var dark = !theme.Equals("White", StringComparison.OrdinalIgnoreCase);
        var immersive = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref immersive, sizeof(int));

        // Respect Settings > Personalization > Colors > Transparency effects: when off, Windows draws no live acrylic.
        var wantBackdrop = glass && TransparencyEffectsEnabled();
        var backdrop = wantBackdrop ? BackdropAcrylic : BackdropNone;
        // Windows 10 and Windows 11 before 22H2 reject attribute 38; that failure is the "no backdrop" signal.
        var backdropApplied = DwmSetWindowAttribute(handle, SystemBackdropType, ref backdrop, sizeof(int)) == 0 && wantBackdrop;
        var source = HwndSource.FromHwnd(handle);
        if (source?.CompositionTarget is { } target)
        {
            // The backdrop only shows through pixels WPF leaves transparent, across the whole client area.
            target.BackgroundColor = backdropApplied ? Colors.Transparent : SystemColors.WindowColor;
            var margins = new Margins(backdropApplied ? -1 : 0);
            _ = DwmExtendFrameIntoClientArea(handle, ref margins);
        }

        var caption = glass ? DefaultColor : ColorRef(dark ? 0x00 : 0xF7, dark ? 0x00 : 0xF7, dark ? 0x00 : 0xF5);
        var text = glass ? DefaultColor : ColorRef(dark ? 0xF6 : 0x11, dark ? 0xF6 : 0x12, dark ? 0xF6 : 0x15);
        var border = glass ? DefaultColor : ColorRef(dark ? 0x34 : 0xD7, dark ? 0x34 : 0xD7, dark ? 0x39 : 0xD2);
        _ = DwmSetWindowAttribute(handle, CaptionColor, ref caption, sizeof(uint));
        _ = DwmSetWindowAttribute(handle, TextColor, ref text, sizeof(uint));
        _ = DwmSetWindowAttribute(handle, BorderColor, ref border, sizeof(uint));
        return backdropApplied;
    }

    private static bool TransparencyEffectsEnabled()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("EnableTransparency") is not int value || value != 0;
    }

    private static uint ColorRef(int red, int green, int blue) =>
        (uint)(red | green << 8 | blue << 16);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Margins(int all)
    {
        private readonly int left = all, right = all, top = all, bottom = all;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(nint window, ref Margins margins);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint window,
        int attribute,
        ref int value,
        int valueSize);

    [DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
    private static extern int DwmSetWindowAttribute(
        nint window,
        int attribute,
        ref uint value,
        int valueSize);
}
