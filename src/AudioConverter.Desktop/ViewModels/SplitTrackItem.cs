using AudioConverter.Core.Split;
using AudioConverter.Desktop.Mvvm;
using L = AudioConverter.Desktop.Localization.Localizer;

namespace AudioConverter.Desktop.ViewModels;

/// <summary>One editable row of the split track list.</summary>
public sealed class SplitTrackItem : ObservableObject
{
    private string title;

    public SplitTrackItem(AlbumTrack track)
    {
        Number = track.Number;
        title = track.Title;
        Performer = track.Performer;
        StartSeconds = track.StartSeconds;
        EndSeconds = track.EndSeconds;
    }

    public int Number { get; }
    public string Title { get => title; set => Set(ref title, value); }
    public string Performer { get; }
    public double StartSeconds { get; }
    public double EndSeconds { get; }
    public double DurationSeconds => EndSeconds - StartSeconds;

    /// <summary>The track as it will be written, with a fallback title when the title is blank.</summary>
    public AlbumTrack ToAlbumTrack() => new(
        Number,
        string.IsNullOrWhiteSpace(Title) ? L.F("Track2", Number) : Title.Trim(),
        Performer,
        StartSeconds,
        EndSeconds);
}
