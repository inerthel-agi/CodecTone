using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AudioConverter.Core.Tags;

/// <summary>A file to rename, with the tags its pattern reads.</summary>
public sealed record TagRenameSource(string FilePath, AudioTags Tags);

/// <summary>Builds file names from tag tokens such as "{track:00} - {title}".</summary>
public static class TagRenamePattern
{
    public const string DefaultPattern = "{track:00} - {title}";

    private const int MaximumCopies = 9999;
    private const string InvalidFileNameChars = "<>:\"/\\|?*";
    private static readonly Regex Token = new(@"\{(?<name>title|artist|album|albumartist|track|disc|date|genre)(?::(?<pad>0+))?\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant);
    private static readonly Regex LeadingNumber = new(@"^\s*(?<number>\d{1,6})", RegexOptions.CultureInvariant);

    /// <summary>File name without its extension. Falls back to the original name when the pattern yields nothing usable.</summary>
    public static string Stem(string pattern, AudioTags tags, string originalFileName)
    {
        var rendered = Render(pattern, tags);
        return rendered.Length > 0 ? rendered : Path.GetFileNameWithoutExtension(originalFileName);
    }

    /// <summary>The rendered name with the original extension kept.</summary>
    public static string FileName(string pattern, AudioTags tags, string originalFileName) =>
        Stem(pattern, tags, originalFileName) + Path.GetExtension(originalFileName);

    /// <summary>
    /// Full target path for each source, in order. A name already on disk, or claimed by an earlier source,
    /// gets " (2)", " (3)"... in its folder. A file keeps its own path when the pattern gives its current name.
    /// </summary>
    public static IReadOnlyList<string> PlanTargets(string pattern, IReadOnlyList<TagRenameSource> sources, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(exists);
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targets = new string[sources.Count];
        for (var index = 0; index < sources.Count; index++)
        {
            var source = sources[index];
            var originalName = Path.GetFileName(source.FilePath);
            var folder = Path.GetDirectoryName(source.FilePath) ?? "";
            var stem = Stem(pattern, source.Tags, originalName);
            var extension = Path.GetExtension(originalName);
            targets[index] = source.FilePath;
            for (var copy = 1; copy <= MaximumCopies; copy++)
            {
                var name = copy == 1 ? stem + extension : $"{stem} ({copy}){extension}";
                var candidate = Path.Combine(folder, name);
                var isSelf = string.Equals(candidate, source.FilePath, StringComparison.OrdinalIgnoreCase);
                if (!isSelf && (claimed.Contains(candidate) || exists(candidate))) continue;
                claimed.Add(candidate);
                targets[index] = candidate;
                break;
            }
        }
        return targets;
    }

    private static string Render(string pattern, AudioTags tags)
    {
        var substituted = Token.Replace(pattern ?? "", match => TokenValue(match, tags));
        var builder = new StringBuilder(substituted.Length);
        foreach (var character in substituted)
        {
            if (character < ' ') builder.Append(' ');
            else if (!InvalidFileNameChars.Contains(character)) builder.Append(character);
        }
        return Whitespace.Replace(builder.ToString(), " ").Trim(' ', '.');
    }

    private static string TokenValue(Match match, AudioTags tags)
    {
        var name = match.Groups["name"].Value.ToLowerInvariant();
        var value = name switch
        {
            "title" => tags.Title,
            "artist" => tags.Artist,
            "album" => tags.Album,
            "albumartist" => tags.AlbumArtist,
            "track" => tags.Track,
            "disc" => tags.Disc,
            "date" => tags.Date,
            _ => tags.Genre,
        };
        if ((name is "track" or "disc") && LeadingNumber.Match(value) is { Success: true } number)
        {
            var pad = match.Groups["pad"].Success ? new string('0', match.Groups["pad"].Value.Length) : null;
            return int.Parse(number.Groups["number"].Value, CultureInfo.InvariantCulture).ToString(pad, CultureInfo.InvariantCulture);
        }
        return value.Trim();
    }
}
