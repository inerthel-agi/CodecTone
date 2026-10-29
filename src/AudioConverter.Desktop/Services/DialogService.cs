using Microsoft.Win32;
using System.Windows;

namespace AudioConverter.Desktop.Services;

public interface IDialogService
{
    string? ChooseAudioFile();
    /// <summary>Several audio files at once; null when cancelled.</summary>
    IReadOnlyList<string>? ChooseAudioFiles();
    /// <summary>Any existing file matching a filter such as "Cue sheets|*.cue".</summary>
    string? ChooseFile(string filter);
    /// <summary>A destination path for a new file; null when cancelled.</summary>
    string? ChooseSaveFile(string filter, string fileName);
    string? ChooseFolder(string? initialDirectory = null);
    string? ChooseCoverImage();
    void Error(string message);
    bool Confirm(string message);
}

public sealed class DialogService : IDialogService
{
    public const string AudioFilter = "Audio files|*.mp3;*.flac;*.wav;*.ogg;*.aac;*.m4a|All files|*.*";

    public string? ChooseAudioFile()
    {
        var dialog = new OpenFileDialog { Filter = AudioFilter, CheckFileExists = true, Multiselect = false };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public IReadOnlyList<string>? ChooseAudioFiles()
    {
        var dialog = new OpenFileDialog { Filter = AudioFilter, CheckFileExists = true, Multiselect = true };
        return dialog.ShowDialog() == true ? dialog.FileNames : null;
    }

    public string? ChooseFile(string filter)
    {
        var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true, Multiselect = false };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ChooseSaveFile(string filter, string fileName)
    {
        var dialog = new SaveFileDialog { Filter = filter, FileName = fileName };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ChooseFolder(string? initialDirectory = null)
    {
        var dialog = new OpenFolderDialog { InitialDirectory = initialDirectory ?? "", Multiselect = false };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public string? ChooseCoverImage()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Cover images|*.png;*.jpg;*.jpeg;*.webp|All files|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public void Error(string message) => MessageBox.Show(message, "CodecTone", MessageBoxButton.OK, MessageBoxImage.Error);

    public bool Confirm(string message) => MessageBox.Show(
        message,
        "CodecTone",
        MessageBoxButton.YesNo,
        MessageBoxImage.Warning) == MessageBoxResult.Yes;
}
