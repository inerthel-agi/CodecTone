namespace AudioConverter.Infrastructure.Split;

/// <summary>The tags written to one split track. An empty value clears the matching tag copied from the source.</summary>
public sealed record AlbumTrackTags(string Title, string Artist, string Album, string AlbumArtist, int TrackNumber, int TrackCount)
{
    public IEnumerable<(string Key, string Value)> ToMetadata()
    {
        yield return ("title", Title);
        yield return ("artist", Artist);
        yield return ("album", Album);
        yield return ("album_artist", AlbumArtist);
        yield return ("track", $"{TrackNumber}/{TrackCount}");
    }
}
