using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using AudioConverter.Core.Compression;
using AudioConverter.Core.Models;
using AudioConverter.Core.Tags;
using AudioConverter.Desktop.Mvvm;
using AudioConverter.Infrastructure.Tags;

namespace AudioConverter.Desktop.ViewModels;

public sealed class TagsViewModel : WorkspaceBase
{
    private bool renameFromTags;
    private string renamePattern = TagRenamePattern.DefaultPattern;
    private string bulkArtist = "";
    private string bulkAlbum = "";
    private string bulkAlbumArtist = "";
    private string bulkGenre = "";
    private string bulkDate = "";
    private bool suspendRefresh;

    public TagsViewModel(WorkspaceHost host) : base(host, "Tags", "Nothing has run yet.")
    {
        ChooseFilesCommand = new AsyncRelayCommand(async () => { var paths = Dialogs.ChooseAudioFiles(); if (paths is not null) await AddSourcesAsync(paths); }, () => !Host.IsBusy);
        ChooseFolderCommand = new AsyncRelayCommand(async () => { var path = Dialogs.ChooseFolder(); if (path is not null) await AddSourcesAsync([path]); }, () => !Host.IsBusy);
        ClearCommand = new RelayCommand(ClearRows, () => !Host.IsBusy && Rows.Count > 0);
        ApplyToAllCommand = new RelayCommand(ApplyToAll, () => !Host.IsBusy && Rows.Count > 0);
        SaveTagsCommand = new AsyncRelayCommand(SaveTagsAsync, () => !Host.IsBusy && Rows.Count > 0);
    }

    public override ICommand PrimaryCommand => SaveTagsCommand;
    public override string PrimaryLabel => "Save tags";
    public AsyncRelayCommand ChooseFilesCommand { get; }
    public AsyncRelayCommand ChooseFolderCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand ApplyToAllCommand { get; }
    public AsyncRelayCommand SaveTagsCommand { get; }
    public ObservableCollection<TagRowViewModel> Rows { get; } = [];
    public bool HasRows => Rows.Count > 0;
    public bool HasNoRows => Rows.Count == 0;

    public string EditorSummary
    {
        get
        {
            if (Rows.Count == 0) return "";
            var edited = Rows.Count(row => row.IsDirty);
            var rename = RenameFromTags ? $" · {Rows.Count(row => row.PendingName.Length > 0)} to rename" : "";
            return $"{Files(Rows.Count)} · {edited} with tag changes{rename}";
        }
    }

    public bool RenameFromTags { get => renameFromTags; set { if (Set(ref renameFromTags, value)) RefreshState(); } }
    public string RenamePattern { get => renamePattern; set { if (Set(ref renamePattern, value)) RefreshState(); } }
    public string BulkArtist { get => bulkArtist; set => Set(ref bulkArtist, value); }
    public string BulkAlbum { get => bulkAlbum; set => Set(ref bulkAlbum, value); }
    public string BulkAlbumArtist { get => bulkAlbumArtist; set => Set(ref bulkAlbumArtist, value); }
    public string BulkGenre { get => bulkGenre; set => Set(ref bulkGenre, value); }
    public string BulkDate { get => bulkDate; set => Set(ref bulkDate, value); }

    public override Task AcceptDropAsync(IReadOnlyList<string> paths) =>
        paths.Count > 0 ? AddSourcesAsync(paths) : Task.CompletedTask;

    protected override void RaiseCommands()
    {
        ChooseFilesCommand.RaiseCanExecuteChanged(); ChooseFolderCommand.RaiseCanExecuteChanged();
        ClearCommand.RaiseCanExecuteChanged(); ApplyToAllCommand.RaiseCanExecuteChanged(); SaveTagsCommand.RaiseCanExecuteChanged();
    }

    private async Task AddSourcesAsync(IEnumerable<string> paths)
    {
        await RunBusyAsync(async () =>
        {
            Host.EnsureFfmpeg();
            var lines = new List<string>();
            var candidates = new List<string>();
            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                {
                    candidates.AddRange(await Task.Run(() => CompressionFileDiscovery.Find(path, recursive: true,
                            onSkipped: warning => lines.Add("SKIP folder: " + warning), cancellationToken: ActiveOperationToken)
                        .Select(file => file.Path).ToArray(), ActiveOperationToken));
                }
                else if (File.Exists(path)) candidates.Add(Path.GetFullPath(path));
                else lines.Add($"SKIP  {path}: not found");
            }

            var known = new HashSet<string>(Rows.Select(row => row.SourcePath), StringComparer.OrdinalIgnoreCase);
            var fresh = candidates.Where(path => known.Add(path)).ToArray();
            var added = new List<TagRowViewModel>();
            try
            {
                for (var index = 0; index < fresh.Length; index++)
                {
                    var path = fresh[index];
                    UpdateOperationProgress((double)index / fresh.Length, $"Reading tags {index + 1}/{fresh.Length}: {Path.GetFileName(path)}");
                    if (!IsAudioFile(path)) { lines.Add($"SKIP  {Path.GetFileName(path)}: unsupported format"); continue; }
                    try
                    {
                        var info = await Host.Probe!.ProbeAsync(path, ActiveOperationToken);
                        added.Add(new TagRowViewModel(path, AudioTags.FromProbe(info.Tags), info.HasCoverArt, info.DurationSeconds));
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception error) { lines.Add($"FAIL  {Path.GetFileName(path)}: {error.Message}"); }
                }
            }
            finally
            {
                MergeRows(added);
                Report = string.Join('\n', new[] { $"Added {Files(added.Count)}." }.Concat(lines));
            }
            UpdateOperationProgress(1, $"Added {Files(added.Count)}.");
        });
    }

    private async Task SaveTagsAsync()
    {
        await RunBusyAsync(async () =>
        {
            Host.EnsureFfmpeg();
            Report = "Tag save started.";
            var rows = Rows.ToArray();
            var targets = PlanTargets();
            var writer = new TagWriteService(Host.Tools!);
            var lines = new List<string>();
            var succeeded = 0;
            var failed = 0;
            var skipped = 0;
            string? folder = null;
            for (var index = 0; index < rows.Length; index++)
            {
                ActiveOperationToken.ThrowIfCancellationRequested();
                var row = rows[index];
                var name = row.FileName;
                var target = targets[index];
                var renaming = !string.Equals(target, row.SourcePath, StringComparison.Ordinal);
                var tagsChanged = row.IsDirty;
                UpdateOperationProgress((double)index / rows.Length, $"Saving {index + 1}/{rows.Length}: {name}");
                if (!tagsChanged && !renaming)
                {
                    skipped++;
                    lines.Add($"SKIP  {name}: no changes");
                }
                else
                {
                    folder ??= Path.GetDirectoryName(row.SourcePath);
                    var tagsWritten = false;
                    try
                    {
                        if (tagsChanged)
                        {
                            await writer.WriteAsync(row.SourcePath, row.Changes, row.HasCoverArt, row.DurationSeconds, ActiveOperationToken);
                            row.MarkSaved();
                            tagsWritten = true;
                        }
                        if (renaming)
                        {
                            File.Move(row.SourcePath, target);
                            row.MoveTo(target);
                        }
                        succeeded++;
                        lines.Add(renaming ? $"OK  {name} -> {Path.GetFileName(target)}" : $"OK  {name}: tags saved");
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception error)
                    {
                        failed++;
                        lines.Add($"FAIL  {name}: {(tagsWritten ? "tags saved, rename failed: " : "")}{error.Message}");
                    }
                }
                Host.AppendOperationReport(lines[^1]);
            }

            Report = $"Completed: {succeeded} succeeded, {failed} failed, {skipped} skipped\n" + string.Join('\n', lines);
            if (succeeded > 0 && folder is not null) Host.SetLastOutputFolder(this, folder);
            RefreshState();
            UpdateOperationProgress(1, $"Completed: {succeeded} succeeded, {failed} failed, {skipped} skipped");
        });
    }

    private void MergeRows(IReadOnlyList<TagRowViewModel> added)
    {
        if (added.Count == 0) return;
        foreach (var row in added) row.Edited += OnRowEdited;
        var sorted = Rows.Concat(added).OrderBy(row => row.SourcePath, StringComparer.OrdinalIgnoreCase).ToArray();
        Rows.Clear();
        foreach (var row in sorted) Rows.Add(row);
        RefreshState();
    }

    private void ClearRows()
    {
        foreach (var row in Rows) row.Edited -= OnRowEdited;
        Rows.Clear();
        RefreshState();
    }

    private void ApplyToAll()
    {
        // Batch the edits so the rename preview is planned once, not once per field.
        suspendRefresh = true;
        try
        {
            foreach (var row in Rows)
            {
                if (Filled(BulkArtist) is { } artist) row.Artist = artist;
                if (Filled(BulkAlbum) is { } album) row.Album = album;
                if (Filled(BulkAlbumArtist) is { } albumArtist) row.AlbumArtist = albumArtist;
                if (Filled(BulkGenre) is { } genre) row.Genre = genre;
                if (Filled(BulkDate) is { } date) row.Date = date;
            }
        }
        finally { suspendRefresh = false; }
        RefreshState();
    }

    private void OnRowEdited() => RefreshState();

    /// <summary>Re-plans the rename preview and refreshes the counts and command states.</summary>
    private void RefreshState()
    {
        if (suspendRefresh) return;
        var targets = PlanTargets();
        for (var index = 0; index < Rows.Count; index++)
        {
            var row = Rows[index];
            row.PendingName = string.Equals(targets[index], row.SourcePath, StringComparison.Ordinal) ? "" : Path.GetFileName(targets[index]);
        }
        Raise(nameof(HasRows));
        Raise(nameof(HasNoRows));
        Raise(nameof(EditorSummary));
        RaiseCommands();
    }

    private IReadOnlyList<string> PlanTargets()
    {
        if (!RenameFromTags) return Rows.Select(row => row.SourcePath).ToArray();
        var sources = Rows.Select(row => new TagRenameSource(row.SourcePath, row.Current)).ToArray();
        return TagRenamePattern.PlanTargets(RenamePattern, sources, path => File.Exists(path) || Directory.Exists(path));
    }

    private static string? Filled(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsAudioFile(string path)
    {
        try { _ = AudioFormats.FromPath(path); return true; }
        catch (ArgumentException) { return false; }
    }

    private static string Files(int count) => count == 1 ? "1 file" : $"{count} files";
}
