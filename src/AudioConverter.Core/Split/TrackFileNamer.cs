using System.Globalization;
using System.Text.RegularExpressions;
using AudioConverter.Core.Models;

namespace AudioConverter.Core.Split;

/// <summary>Builds track file names from a pattern with {track}, {track:00} and {title} tokens.</summary>
public static partial class TrackFileNamer
{
    public const string DefaultPattern = "{track:00} - {title}";

    private static readonly char[] InvalidCharacters = Path.GetInvalidFileNameChars();

    public static string Build(string pattern, AlbumTrack track, AudioFormat format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        ArgumentNullException.ThrowIfNull(track);

        var expanded = TokenPattern().Replace(pattern, match => match.Value == "{title}"
            ? track.Title
            : TrackNumber(track.Number, match.Groups["pad"].Length));
        var name = new string(expanded.Where(character => !InvalidCharacters.Contains(character)).ToArray()).Trim().TrimEnd('.');
        if (name.Length == 0) throw new ArgumentException("The file name pattern gives an empty file name.", nameof(pattern));
        return $"{name}.{format.ToExtension()}";
    }

    private static string TrackNumber(int number, int padding) =>
        padding == 0
            ? number.ToString(CultureInfo.InvariantCulture)
            : number.ToString(new string('0', padding), CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\{track(?::(?<pad>0+))?\}|\{title\}")]
    private static partial Regex TokenPattern();
}
