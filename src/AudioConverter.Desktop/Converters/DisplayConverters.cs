using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;

namespace AudioConverter.Desktop.Converters;

/// <summary>Visible when the string is empty; pass "Invert" to show it only when the string has a value.</summary>
public sealed class EmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var empty = string.IsNullOrWhiteSpace(value as string);
        return empty ^ (parameter as string == "Invert") ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Formats a source path for a file card: "Name", "Parent" or "Glyph" (folder or audio icon).</summary>
public sealed class PathPartConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var path = (value as string ?? "").TrimEnd('\\', '/');
        if (path.Length == 0) return "";
        return (parameter as string) switch
        {
            "Parent" => Path.GetDirectoryName(path) ?? "",
            "Glyph" => Application.Current?.TryFindResource(Directory.Exists(path) ? "GlyphFolder" : "GlyphAudio") ?? "",
            _ => Path.GetFileName(path) is { Length: > 0 } name ? name : path,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public enum ReportLineKind { Info, Ok, Fail, Skip, Cancelled }

/// <summary>One report row. Text drops the status prefix and shortens "input -> output" to file names; Raw keeps the full line.</summary>
public sealed record ReportLine(ReportLineKind Kind, string Text, string Raw);

/// <summary>Turns a plain-text report into rows, using the OK / FAIL / SKIP / CANCELLED prefixes the view model writes.</summary>
public sealed class ReportLinesConverter : IValueConverter
{
    // ponytail: prefix parsing; switch to typed report items if report formats start to diverge.
    public static IReadOnlyList<ReportLine> Parse(string? report)
    {
        var lines = new List<ReportLine>();
        foreach (var raw in (report ?? "").Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line)) continue;
            var (kind, body) = line switch
            {
                _ when line.StartsWith("OK ", StringComparison.Ordinal) => (ReportLineKind.Ok, line[3..]),
                _ when line.StartsWith("FAIL ", StringComparison.Ordinal) => (ReportLineKind.Fail, line[5..]),
                _ when line.StartsWith("SKIP ", StringComparison.Ordinal) => (ReportLineKind.Skip, line[5..]),
                _ when line.StartsWith("CANCELLED", StringComparison.Ordinal) => (ReportLineKind.Cancelled, line),
                _ => (ReportLineKind.Info, line),
            };
            body = body.Trim();
            if (kind == ReportLineKind.Ok && body.Split(" -> ") is [var input, var output])
                body = $"{Path.GetFileName(input.Trim())} → {Path.GetFileName(output.Trim())}";
            lines.Add(new ReportLine(kind, body, line));
        }
        return lines;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => Parse(value as string);
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class UpperCaseConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value?.ToString()?.ToUpperInvariant() ?? "";
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
