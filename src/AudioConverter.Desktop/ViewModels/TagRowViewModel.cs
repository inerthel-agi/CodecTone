using System.IO;
using AudioConverter.Core.Tags;
using AudioConverter.Desktop.Mvvm;

namespace AudioConverter.Desktop.ViewModels;

/// <summary>One file in the Tags editor: the tags as loaded, the edited values and the rename shown for it.</summary>
public sealed class TagRowViewModel : ObservableObject
{
    private AudioTags original;
    private string title;
    private string artist;
    private string album;
    private string albumArtist;
    private string track;
    private string disc;
    private string date;
    private string genre;
    private string pendingName = "";

    public TagRowViewModel(string sourcePath, AudioTags tags, bool hasCoverArt, double durationSeconds)
    {
        SourcePath = sourcePath;
        HasCoverArt = hasCoverArt;
        DurationSeconds = durationSeconds;
        original = tags;
        title = tags.Title;
        artist = tags.Artist;
        album = tags.Album;
        albumArtist = tags.AlbumArtist;
        track = tags.Track;
        disc = tags.Disc;
        date = tags.Date;
        genre = tags.Genre;
    }

    /// <summary>Raised when a tag field is edited, so the editor can refresh its rename preview.</summary>
    public event Action? Edited;

    public string SourcePath { get; private set; }
    public string FileName => Path.GetFileName(SourcePath);
    public bool HasCoverArt { get; }
    public double DurationSeconds { get; }

    /// <summary>The new file name shown under this row while renaming is on; empty when the name stays the same.</summary>
    public string PendingName { get => pendingName; set => Set(ref pendingName, value); }

    public AudioTags Current => new(Title, Artist, Album, AlbumArtist, Track, Disc, Date, Genre);
    public IReadOnlyList<KeyValuePair<string, string>> Changes => Current.ChangesFrom(original);
    public bool IsDirty => Changes.Count > 0;

    public string Title { get => title; set => Edit(Set(ref title, value)); }
    public string Artist { get => artist; set => Edit(Set(ref artist, value)); }
    public string Album { get => album; set => Edit(Set(ref album, value)); }
    public string AlbumArtist { get => albumArtist; set => Edit(Set(ref albumArtist, value)); }
    public string Track { get => track; set => Edit(Set(ref track, value)); }
    public string Disc { get => disc; set => Edit(Set(ref disc, value)); }
    public string Date { get => date; set => Edit(Set(ref date, value)); }
    public string Genre { get => genre; set => Edit(Set(ref genre, value)); }

    /// <summary>Called after the tags were written: the edited values become the loaded values.</summary>
    public void MarkSaved()
    {
        original = Current;
        Raise(nameof(IsDirty));
    }

    /// <summary>Called after the file was renamed on disk.</summary>
    public void MoveTo(string path)
    {
        SourcePath = path;
        Raise(nameof(FileName));
    }

    private void Edit(bool changed)
    {
        if (!changed) return;
        Raise(nameof(IsDirty));
        Edited?.Invoke();
    }
}
