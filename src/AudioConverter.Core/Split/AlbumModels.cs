namespace AudioConverter.Core.Split;

/// <summary>One track of an album split: a time range of the source with its title and performer.</summary>
public sealed record AlbumTrack(int Number, string Title, string Performer, double StartSeconds, double EndSeconds)
{
    public double DurationSeconds => EndSeconds - StartSeconds;
}

/// <summary>A span of silence in the source, in seconds.</summary>
public sealed record SilenceInterval(double StartSeconds, double EndSeconds);
