using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using System.Xml.Linq;
using AudioConverter.Core.Artwork;
using AudioConverter.Core.Inspection;
using AudioConverter.Core.Remix;
using AudioConverter.Desktop;
using AudioConverter.Desktop.Localization;
using AudioConverter.Desktop.Services;
using AudioConverter.Desktop.ViewModels;

namespace AudioConverter.Desktop.Tests;

[TestClass]
public sealed class WorkspaceTests
{
    /// <summary>Assertions and captures below read English text, whatever the Windows display language is.</summary>
    [AssemblyInitialize]
    public static void UseEnglish(TestContext _) => Localizer.Instance.SetLanguage("en");

    [TestMethod]
    public async Task EveryUsedKey_ExistsInEnglishAndFrench()
    {
        var desktop = Path.Combine(RepositoryRoot(), "src", "AudioConverter.Desktop");
        var english = LoadStrings(Path.Combine(desktop, "Localization", "Strings.resx"));
        var french = LoadStrings(Path.Combine(desktop, "Localization", "Strings.fr.resx"));
        var used = new HashSet<string>();
        foreach (var file in Directory.EnumerateFiles(desktop, "*.xaml", SearchOption.AllDirectories))
            used.UnionWith(Regex.Matches(File.ReadAllText(file), @"\{l:T (\w+)\}|LocalizedFormat}"" ConverterParameter=""(\w+)""").Select(match => match.Groups[1].Value + match.Groups[2].Value));
        foreach (var file in Directory.EnumerateFiles(desktop, "*.cs", SearchOption.AllDirectories).Where(file => !file.Contains(@"\obj\")))
        {
            var code = File.ReadAllText(file);
            used.UnionWith(Regex.Matches(code, @"\bL(?:ocalizer)?\.(?:T|F)\(""(\w+)""[,)]").Select(match => match.Groups[1].Value));
            used.UnionWith(Regex.Matches(code, @"base\(host, ""\w+"", ""(\w+)""\)").Select(match => match.Groups[1].Value));
            foreach (Match call in Regex.Matches(code, @"(?:Refreshed|IsText)\([^)]*\)"))
                used.UnionWith(Regex.Matches(call.Value, @"""(\w+)""").Select(match => match.Groups[1].Value));
        }
        used.UnionWith(Enum.GetNames<SourceVerdictKind>().Select(kind => "Verdict" + kind));
        used.UnionWith(ThemeService.Themes.Where(theme => theme != "Oled").Select(theme => "Theme" + theme));
        await OnSta(async () =>
        {
            using var vm = new MainViewModel(new TestDialogs());
            vm.Compress.CompressionFormat = Core.Models.AudioFormat.Mp3;
            IEnumerable<object> items = [.. vm.Tools.Append(vm.AboutItem).Select(item => item.Name), .. vm.Compress.CompressionProfiles, .. vm.Normalize.Modes,
                .. vm.Remix.RemixCoverActions, .. vm.Split.SplitModes, .. vm.Remix.RemixEffectKinds.Cast<object>(), .. vm.Remix.RemixIntensities.Cast<object>(),
                .. Enum.GetValues<ArtworkOutputFormat>().Cast<object>(), "Export", "Metadata"];
            used.UnionWith(items.Select(item => LocalizeItemConverter.Key(item.ToString()!)));
            await vm.ShutdownAsync();
        });

        Assert.AreEqual("", string.Join(", ", used.Where(key => !english.ContainsKey(key) || !french.ContainsKey(key)).Order()));
        Assert.AreEqual("", string.Join(", ", english.Keys.Where(key => !french.ContainsKey(key)).Order()), "untranslated keys");
    }

    [TestMethod]
    public void FrenchStrings_KeepPlaceholdersAndReportPrefixes()
    {
        var folder = Path.Combine(RepositoryRoot(), "src", "AudioConverter.Desktop", "Localization");
        var english = LoadStrings(Path.Combine(folder, "Strings.resx"));
        var french = LoadStrings(Path.Combine(folder, "Strings.fr.resx"));
        static string Shape(string text) =>
            string.Join(" ", Regex.Matches(text, @"\{(\d+)(?::[^}]*)?\}").Select(match => match.Groups[1].Value).Order())
            + "|" + Regex.Match(text, @"^(OK|FAIL|SKIP|CANCELLED|READY)\b").Value + "|" + text.Count(c => c == '\n');
        Assert.AreEqual("", string.Join(", ", english.Where(pair => french.TryGetValue(pair.Key, out var text) && Shape(text) != Shape(pair.Value)).Select(pair => pair.Key)));
        // Preset descriptions are French-only: English shows the catalog text from Core.
        Assert.AreEqual("", string.Join(", ", french.Keys.Where(key => !english.ContainsKey(key) && !key.StartsWith("PresetDescription", StringComparison.Ordinal))));
        Assert.AreEqual("", string.Join(", ", Enum.GetNames<RemixPreset>().Where(preset => !french.ContainsKey("PresetDescription" + preset))));
    }

    private static Dictionary<string, string> LoadStrings(string path) =>
        XDocument.Load(path).Root!.Elements("data").ToDictionary(data => (string)data.Attribute("name")!, data => (string?)data.Element("value") ?? "");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AudioConverter.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
    [TestMethod]
    public async Task EditedRack_RefusalPreservesIntensityAndValues()
    {
        await OnSta(async () =>
        {
            var dialogs = new TestDialogs();
            using var vm = new MainViewModel(dialogs);
            vm.Remix.ApplyRemixPresetCommand.Execute(RemixPresetCatalog.All.Single(p => p.Preset == RemixPreset.BassBoost));
            vm.Remix.RemixEffects[0].First = 3;
            vm.Remix.SelectedRemixIntensity = RemixIntensity.Strong;
            Assert.AreEqual(1, dialogs.Confirmations);
            Assert.AreEqual(RemixIntensity.Medium, vm.Remix.SelectedRemixIntensity);
            Assert.AreEqual(3d, vm.Remix.RemixEffects[0].First);
            await vm.ShutdownAsync();
        });
    }

    [TestMethod]
    public async Task Shutdown_WaitsForOperationCleanupAndKeepsPartialReport()
    {
        await OnSta(async () =>
        {
            using var vm = new MainViewModel(new TestDialogs());
            var release = new TaskCompletionSource();
            var busy = vm.Host.RunBusyAsync(vm.Convert, async () => { await release.Task; throw new OperationCanceledException(); });
            var shutdown = vm.ShutdownAsync();
            Assert.IsFalse(shutdown.IsCompleted);
            Assert.IsFalse(vm.Host.IsWorkspaceEnabled);
            release.SetResult();
            await shutdown;
            await busy;
            StringAssert.Contains(vm.Convert.Report, "CANCELLED");
        });
    }

    [TestMethod]
    public async Task PreviewVolume_DoesNotMutateRack()
    {
        await OnSta(async () =>
        {
            using var vm = new MainViewModel(new TestDialogs());
            vm.Remix.ApplyRemixPresetCommand.Execute(RemixPresetCatalog.All.Single(p => p.Preset == RemixPreset.BassBoost));
            var before = vm.Remix.RemixEffects.Select(e => e.ToModel()).ToArray();
            vm.Remix.PreviewVolume = 10;
            CollectionAssert.AreEqual(before, vm.Remix.RemixEffects.Select(e => e.ToModel()).ToArray());
            await vm.ShutdownAsync();
        });
    }

    [TestMethod]
    public async Task Waveform_RendersRulerSelectionAndPlayheadWithData()
    {
        await OnSta(async () =>
        {
            var host = new System.Windows.Controls.Grid { Background = System.Windows.Media.Brushes.Black };
            host.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/CodecTone;component/Themes/Oled.xaml") });
            var waveform = new AudioConverter.Desktop.Controls.WaveformControl
            {
                Peaks = Enumerable.Range(0, 900).Select(index => Math.Abs(Math.Sin(index / 23d)) * 0.9).ToArray(),
                Duration = 245,
                Start = 30,
                End = 150,
                Playhead = 75,
            };
            host.Children.Add(waveform);
            host.Measure(new Size(800, 240));
            host.Arrange(new Rect(0, 0, 800, 240));
            host.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(800, 240, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(host);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var stream = System.IO.File.Create(System.IO.Path.Combine(AppContext.BaseDirectory, "waveform-oled.png"));
            encoder.Save(stream);
            Assert.AreEqual(150d, waveform.End);
        });
    }

    [TestMethod]
    public async Task PreviewPlayer_ReportsPositionAndReleasesFileOnStop()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"codectone-tone-{Guid.NewGuid():N}.wav");
        WriteTone(path, seconds: 2);
        try
        {
            await OnSta(async () =>
            {
                using var player = new AudioPreviewPlayer { Volume = 0 };
                try { player.Play(path); }
                catch (InvalidOperationException error) { Assert.Inconclusive(error.Message); }
                for (var wait = 0; wait < 30 && player.Position == TimeSpan.Zero; wait++)
                    await Task.Delay(50);
                Assert.IsTrue(player.IsPlaying);
                Assert.IsTrue(player.Position > TimeSpan.Zero, "Position should advance during playback.");
                player.TogglePause();
                Assert.IsTrue(player.IsPaused);
                player.Position = TimeSpan.FromSeconds(1.5);
                Assert.IsTrue(player.Position >= TimeSpan.FromSeconds(1.4), "Seeking should move the reported position.");
                player.TogglePause();
                var ended = false;
                player.Ended += () => ended = true;
                for (var wait = 0; wait < 40 && !ended; wait++) { await Task.Delay(50); player.Poll(); }
                Assert.IsTrue(ended, "The end of playback should be detected.");
                Assert.IsFalse(player.IsPlaying);
                System.IO.File.Delete(path);
            });
        }
        finally { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
    }

    private static void WriteTone(string path, int seconds)
    {
        const int rate = 22_050;
        using var writer = new System.IO.BinaryWriter(System.IO.File.Create(path));
        var samples = rate * seconds;
        writer.Write("RIFF"u8); writer.Write(36 + samples * 2); writer.Write("WAVEfmt "u8); writer.Write(16);
        writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(samples * 2);
        for (var i = 0; i < samples; i++) writer.Write((short)(Math.Sin(i * 2 * Math.PI * 440 / rate) * 8000));
    }

    [TestMethod]
    public async Task ConvertQueue_AddsSupportedFilesOnce()
    {
        var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"codectone-add-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(folder, "sub"));
        foreach (var name in new[] { "a.flac", "b.MP3", "notes.txt", @"sub\c.wav" })
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, name), []);
        try
        {
            await OnSta(async () =>
            {
                using var vm = new MainViewModel(new TestDialogs());
                vm.Convert.AddSources([folder, System.IO.Path.Combine(folder, "a.flac"), System.IO.Path.Combine(folder, "notes.txt")]);
                CollectionAssert.AreEqual(new[] { "a.flac", "b.MP3" }, vm.Convert.Queue.Select(item => item.Name).ToArray());
                Assert.IsTrue(vm.Convert.ConvertCommand.CanExecute(null));
                vm.Convert.RemoveQueueItemCommand.Execute(vm.Convert.Queue[0]);
                Assert.AreEqual("1 file", vm.Convert.QueueSummary);
                await vm.ShutdownAsync();
            });
        }
        finally { System.IO.Directory.Delete(folder, true); }
    }

    [TestMethod]
    public void ThemeCycle_VisitsEveryThemeAndRecoversUnknownValues()
    {
        Assert.AreEqual("White", ThemeService.Next("oled"));
        Assert.AreEqual("Glass", ThemeService.Next("White"));
        Assert.AreEqual("Oled", ThemeService.Next("Glass"));
        Assert.AreEqual("Oled", ThemeService.Normalize("Sepia"));
        Assert.AreEqual("OLED", ThemeService.DisplayName(null));
    }

    [TestMethod]
    public void ReportLines_ClassifyPrefixesAndShortenPaths()
    {
        var lines = AudioConverter.Desktop.Converters.ReportLinesConverter.Parse(
            "Completed: 1 succeeded, 1 failed\n\nOK  C:\\in\\a.flac -> D:\\out\\a.mp3\r\nFAIL  b.flac: bad header\nSKIP  c.mp3: already small\nCANCELLED. Remaining files were not processed.");

        CollectionAssert.AreEqual(
            new[] { "Info", "Ok", "Fail", "Skip", "Cancelled" },
            lines.Select(line => line.Kind.ToString()).ToArray());
        Assert.AreEqual("a.flac → a.mp3", lines[1].Text);
        Assert.AreEqual("b.flac: bad header", lines[2].Text);
        Assert.AreEqual("Remaining files were not processed.", lines[4].Text);
        Assert.AreEqual("OK  C:\\in\\a.flac -> D:\\out\\a.mp3", lines[1].Raw);
    }

    [TestMethod]
    public async Task WorkspaceLayouts_RenderBothThemesAtMinimumSize()
    {
        var sampleFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"codectone-queue-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(sampleFolder);
        foreach (var name in new[] { "01 Intro.flac", "02 A much longer track name that should be trimmed nicely.mp3" })
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(sampleFolder, name), []);
        var bindingErrors = new BindingErrorListener();
        System.Diagnostics.PresentationTraceSources.Refresh();
        System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Add(bindingErrors);
        System.Diagnostics.PresentationTraceSources.DataBindingSource.Switch.Level = System.Diagnostics.SourceLevels.Error;
        await OnSta(async () =>
        {
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            string[] pages;
            using (var probe = new MainViewModel(new TestDialogs())) pages = probe.Tools.Append(probe.AboutItem).Select(item => item.Name).ToArray();
            // French labels run longer, so they get their own pass at the narrowest size and in Glass.
            var passes = ThemeService.Themes.SelectMany(theme => new[] { (900, 650), (1180, 820), (1180, 2300) }.Select(size => (Language: "en", Theme: theme, Size: size)))
                .Append(("fr", "Oled", (900, 650))).Append(("fr", "Glass", (1180, 2300)));
            foreach (var (language, theme, (width, height)) in passes)
            foreach (var workspace in pages)
            {
                Localizer.Instance.SetLanguage(language);
                application.Resources.MergedDictionaries.Clear();
                application.Resources.MergedDictionaries.Add(ThemeService.LoadPalette(theme, backdrop: false));
                application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/CodecTone;component/Themes/Controls.xaml") });
                // Same path as Windows "Animation effects" off: transitions finish instantly, so captures show final states.
                ThemeService.DisableMotion(application.Resources);
                using var vm = new MainViewModel(new TestDialogs());
                vm.Tools.Append(vm.AboutItem).Single(item => item.Name == workspace).ShowCommand.Execute(null);
                if (workspace == "Convert")
                {
                    vm.Convert.AddSources([sampleFolder]);
                    vm.Convert.Queue[0].Update(QueueState.Running, "Converting 42%", 42);
                    vm.Convert.Queue[1].Update(QueueState.Failed, "Failed");
                }
                if (workspace == "Compress")
                {
                    vm.Compress.PlannedFiles.Add(new QueueItem(@"C:\Music\a.flac", @"Album\a.flac"));
                    var skipped = new QueueItem(@"C:\Music\b.mp3", @"Album\b.mp3") { Included = false };
                    skipped.Update(QueueState.Skipped, "Skip: already small");
                    vm.Compress.PlannedFiles.Add(skipped);
                }
                if (workspace == "Remix")
                {
                    vm.Remix.UserPresets.Clear();
                    vm.Remix.UserPresets.Add(new UserRemixPreset("Night drive", [new UserRemixEffect(RemixEffectKind.Reverb, true, 0.3, 2.5, 0.7)]));
                    vm.Remix.AddRemixEffectCommand.Execute(null);
                    vm.Remix.SelectedRemixEffectKind = RemixEffectKind.Reverb;
                    vm.Remix.AddRemixEffectCommand.Execute(null);
                }
                var window = new MainWindow { DataContext = vm };
                var root = (FrameworkElement)window.Content;
                root.Measure(new Size(width, height));
                root.Arrange(new Rect(0, 0, width, height));
                root.UpdateLayout();
                // Give the animation clock a few ticks so zero-length transitions reach their final state.
                for (var tick = 0; tick < 4; tick++) { await Task.Delay(40); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
                Assert.AreEqual(width, root.ActualWidth);
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(root);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using (var stream = System.IO.File.Create(System.IO.Path.Combine(AppContext.BaseDirectory, $"layout-{theme}-{workspace.Replace(' ', '-')}-{width}x{height}{(language == "en" ? "" : "-" + language)}.png"))) encoder.Save(stream);
                await vm.ShutdownAsync();
                window.Close();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            }
            Localizer.Instance.SetLanguage("en");
            application.Shutdown();
        });
        System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Remove(bindingErrors);
        System.IO.Directory.Delete(sampleFolder, true);
        Assert.AreEqual(0, bindingErrors.Errors.Count, string.Join("\n", bindingErrors.Errors.Distinct()));
    }

    /// <summary>Collects "BindingExpression path error" messages, i.e. bindings to properties that do not exist.</summary>
    private sealed class BindingErrorListener : System.Diagnostics.TraceListener
    {
        private readonly System.Text.StringBuilder pending = new();
        public List<string> Errors { get; } = [];
        public override void Write(string? message) => pending.Append(message);
        public override void WriteLine(string? message)
        {
            pending.Append(message);
            var line = pending.ToString();
            pending.Clear();
            if (line.Contains("BindingExpression path error", StringComparison.Ordinal)) lock (Errors) Errors.Add(line);
        }
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
        public int Confirmations { get; private set; }
        public string? ChooseAudioFile() => null;
        public IReadOnlyList<string>? ChooseAudioFiles() => null;
        public string? ChooseFile(string filter) => null;
        public string? ChooseSaveFile(string filter, string fileName) => null;
        public string? ChooseFolder(string? initialDirectory = null) => null;
        public string? ChooseCoverImage() => null;
        public void Error(string message) => Assert.Fail(message);
        public bool Confirm(string message) { Confirmations++; return false; }
    }
}
