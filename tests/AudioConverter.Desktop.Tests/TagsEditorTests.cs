using System.IO;
using System.Windows;
using System.Windows.Threading;
using AudioConverter.Desktop.Services;
using AudioConverter.Desktop.ViewModels;
using AudioConverter.Infrastructure.Ffmpeg;

namespace AudioConverter.Desktop.Tests;

[TestClass]
public sealed class TagsEditorTests
{
    [TestMethod]
    [TestCategory("RequiresFfmpeg")]
    public async Task Save_WritesEditedTagsAndRenamesFiles()
    {
        var tools = LocateFfmpeg();
        var root = Path.Combine(Path.GetTempPath(), $"codectone-editor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var first = Path.Combine(root, "t01.mp3");
            var second = Path.Combine(root, "t02.mp3");
            await CreateMp3Async(tools, first, "One", "Band", 1);
            await CreateMp3Async(tools, second, "Two", "Band", 2);

            await OnSta(async () =>
            {
                using var vm = new MainViewModel(new TestDialogs());
                vm.Host.TryLocateFfmpeg();
                await vm.Tags.AcceptDropAsync([first, second]);
                Assert.AreEqual(2, vm.Tags.Rows.Count);

                vm.Tags.Rows[0].Title = "One edited";
                vm.Tags.Rows[1].Artist = "";
                vm.Tags.RenameFromTags = true;
                Assert.AreEqual("01 - One edited.mp3", vm.Tags.Rows[0].PendingName);
                Assert.AreEqual("02 - Two.mp3", vm.Tags.Rows[1].PendingName);

                vm.Tags.SaveTagsCommand.Execute(null);
                await WaitForIdleAsync(vm);

                StringAssert.Contains(vm.Tags.Report, "OK  t01.mp3 -> 01 - One edited.mp3");
                StringAssert.Contains(vm.Tags.Report, "OK  t02.mp3 -> 02 - Two.mp3");
                await vm.ShutdownAsync();
            });

            Assert.IsFalse(File.Exists(first));
            var probe = new FfprobeService(tools.FfprobePath);
            var firstTags = (await probe.ProbeAsync(Path.Combine(root, "01 - One edited.mp3"))).Tags;
            Assert.AreEqual("One edited", firstTags["title"]);
            Assert.AreEqual("1", firstTags["track"]);
            var secondTags = (await probe.ProbeAsync(Path.Combine(root, "02 - Two.mp3"))).Tags;
            Assert.AreEqual("Two", secondTags["title"]);
            Assert.IsFalse(secondTags.ContainsKey("artist"));
            Assert.AreEqual(0, Directory.GetFiles(root, "*.tmp*").Length);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    [TestCategory("RequiresFfmpeg")]
    public async Task ApplyToAll_CopiesFilledFieldsOnlyAndMarksEveryRowDirty()
    {
        var tools = LocateFfmpeg();
        var root = Path.Combine(Path.GetTempPath(), $"codectone-editor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var first = Path.Combine(root, "a.mp3");
            var second = Path.Combine(root, "b.mp3");
            await CreateMp3Async(tools, first, "One", "Band", 1);
            await CreateMp3Async(tools, second, "Two", "Band", 2);

            await OnSta(async () =>
            {
                using var vm = new MainViewModel(new TestDialogs());
                vm.Host.TryLocateFfmpeg();
                await vm.Tags.AcceptDropAsync([first, second]);
                vm.Tags.BulkArtist = "  New Artist ";
                vm.Tags.BulkGenre = "   ";
                vm.Tags.ApplyToAllCommand.Execute(null);

                Assert.IsTrue(vm.Tags.Rows.All(row => row.Artist == "New Artist"));
                Assert.IsTrue(vm.Tags.Rows.All(row => row.IsDirty));
                Assert.AreEqual("One", vm.Tags.Rows[0].Title);
                Assert.AreEqual("", vm.Tags.Rows[0].Genre);
                Assert.AreEqual("2 files · 2 with tag changes", vm.Tags.EditorSummary);
                await vm.ShutdownAsync();
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static FfmpegTools LocateFfmpeg()
    {
        try { return FfmpegLocator.Find(); }
        catch (FfmpegDependencyException)
        {
            Assert.Inconclusive("FFmpeg is not available for this test.");
            throw;
        }
    }

    private static Task CreateMp3Async(FfmpegTools tools, string path, string title, string artist, int track) => new FfmpegProcessRunner().RunAsync(tools.FfmpegPath,
    [
        "-hide_banner", "-loglevel", "error", "-y",
        "-f", "lavfi", "-i", "sine=frequency=330:duration=1",
        "-c:a", "libmp3lame", "-b:a", "96k",
        "-metadata", $"title={title}", "-metadata", $"artist={artist}", "-metadata", $"track={track}",
        "-id3v2_version", "3", path,
    ], 1);

    private static async Task WaitForIdleAsync(MainViewModel vm)
    {
        var deadline = DateTime.UtcNow.AddMinutes(2);
        while (vm.Host.IsBusy && DateTime.UtcNow < deadline) await Task.Delay(20);
        Assert.IsFalse(vm.Host.IsBusy, "The tag save did not finish.");
    }

    private static Task OnSta(Func<Task> action)
    {
        var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await action(); result.SetResult(); }
                catch (Exception error) { result.SetException(error); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            }));
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return result.Task;
    }

    private sealed class TestDialogs : IDialogService
    {
        public string? ChooseAudioFile() => null;
        public IReadOnlyList<string>? ChooseAudioFiles() => null;
        public string? ChooseFile(string filter) => null;
        public string? ChooseSaveFile(string filter, string fileName) => null;
        public string? ChooseFolder(string? initialDirectory = null) => null;
        public string? ChooseCoverImage() => null;
        public void Error(string message) => Assert.Fail(message);
        public bool Confirm(string message) => false;
    }
}
