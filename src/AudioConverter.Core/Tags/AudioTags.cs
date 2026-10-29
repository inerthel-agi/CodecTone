namespace AudioConverter.Core.Tags;

/// <summary>The editable tag fields of one file. A blank value means the tag is absent.</summary>
public sealed record AudioTags(
    string Title,
    string Artist,
    string Album,
    string AlbumArtist,
    string Track,
    string Disc,
    string Date,
    string Genre)
{
    /// <summary>Reads the fields from probed tags, accepting the spellings each field can have in a file.</summary>
    public static AudioTags FromProbe(IReadOnlyDictionary<string, string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        string Get(params string[] keys) => keys.Select(key => tags.TryGetValue(key, out var value) ? value : null).FirstOrDefault(value => value is not null) ?? "";
        return new AudioTags(
            Get("title"), Get("artist"), Get("album"), Get("album_artist", "albumartist"),
            Get("track"), Get("disc"), Get("date", "year"), Get("genre"));
    }

    /// <summary>
    /// Metadata writes for the fields that differ from <paramref name="original"/>. An empty value clears the key.
    /// Alias keys are cleared before the value is set, so a file keeps one spelling of each field.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> ChangesFrom(AudioTags original)
    {
        ArgumentNullException.ThrowIfNull(original);
        var changes = new List<KeyValuePair<string, string>>();
        AddChange(changes, original.Title, Title, "title");
        AddChange(changes, original.Artist, Artist, "artist");
        AddChange(changes, original.Album, Album, "album");
        AddChange(changes, original.AlbumArtist, AlbumArtist, "albumartist", "album_artist");
        AddChange(changes, original.Track, Track, "track");
        AddChange(changes, original.Disc, Disc, "disc");
        AddChange(changes, original.Date, Date, "year", "date");
        AddChange(changes, original.Genre, Genre, "genre");
        return changes;
    }

    private static void AddChange(List<KeyValuePair<string, string>> changes, string? before, string? after, params string[] keys)
    {
        var value = Clean(after);
        if (Clean(before) == value) return;
        foreach (var alias in keys[..^1]) changes.Add(new KeyValuePair<string, string>(alias, ""));
        changes.Add(new KeyValuePair<string, string>(keys[^1], value));
    }

    private static string Clean(string? value) => (value ?? "").Trim();
}
