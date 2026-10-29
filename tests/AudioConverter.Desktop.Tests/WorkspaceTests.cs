using System.Windows;
using System.Windows.Threading;
using AudioConverter.Core.Remix;
using AudioConverter.Desktop;
using AudioConverter.Desktop.Services;
using AudioConverter.Desktop.ViewModels;

namespace AudioConverter.Desktop.Tests;

[TestClass]
public sealed class WorkspaceTests
{
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
        Assert.AreEqual("OK  C:\\in\\a.flac -> D:\\out\\a.mp3", lines[1].Raw);
    }

    [TestMethod]
    public async Task WorkspaceLayouts_RenderBothThemesAtMinimumSize()
    {
        var bindingErrors = new BindingErrorListener();
        System.Diagnostics.PresentationTraceSources.Refresh();
        System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Add(bindingErrors);
        System.Diagnostics.PresentationTraceSources.DataBindingSource.Switch.Level = System.Diagnostics.SourceLevels.Error;
        await OnSta(async () =>
        {
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (var theme in ThemeService.Themes)
            foreach (var workspace in new[] { "Convert", "Cut", "Compress", "Artwork", "Remix", "About" })
            foreach (var (width, height) in new[] { (900, 650), (1180, 820), (1180, 2300) })
            {
                application.Resources.MergedDictionaries.Clear();
                application.Resources.MergedDictionaries.Add(ThemeService.LoadPalette(theme, backdrop: false));
                application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/CodecTone;component/Themes/Controls.xaml") });
                // Same path as Windows "Animation effects" off: transitions finish instantly, so captures show final states.
                ThemeService.DisableMotion(application.Resources);
                using var vm = new MainViewModel(new TestDialogs());
                var show = workspace switch
                {
                    "Convert" => vm.ShowConvertCommand,
                    "Cut" => vm.ShowCutCommand,
                    "Compress" => vm.ShowCompressCommand,
                    "Artwork" => vm.ShowArtworkCommand,
                    "Remix" => vm.ShowRemixCommand,
                    _ => vm.ShowAboutCommand,
                };
                show.Execute(null);
                if (workspace == "Remix")
                {
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
                using (var stream = System.IO.File.Create(System.IO.Path.Combine(AppContext.BaseDirectory, $"layout-{theme}-{workspace}-{width}x{height}.png"))) encoder.Save(stream);
                await vm.ShutdownAsync();
                window.Close();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            }
            application.Shutdown();
        });
        System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Remove(bindingErrors);
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
        public string? ChooseFolder(string? initialDirectory = null) => null;
        public string? ChooseCoverImage() => null;
        public void Error(string message) => Assert.Fail(message);
        public bool Confirm(string message) { Confirmations++; return false; }
    }
}
