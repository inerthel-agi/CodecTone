using System.ComponentModel;
using System.Diagnostics;
using AudioConverter.Desktop.Mvvm;
using AudioConverter.Desktop.Services;
using AudioConverter.Infrastructure.Shell;

namespace AudioConverter.Desktop.ViewModels;

/// <summary>Window shell: navigation, theme, the shared host, and one view model per workspace.</summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly IReadOnlyList<WorkspaceBase> workspaces;
    private object activePage;
    private string theme = "Oled";
    private bool disposed;

    public MainViewModel(IDialogService dialogs)
    {
        Host = new WorkspaceHost(dialogs);
        Convert = new ConvertViewModel(Host);
        Cut = new CutViewModel(Host);
        Compress = new CompressViewModel(Host);
        Artwork = new ArtworkViewModel(Host);
        Remix = new RemixViewModel(Host);
        Tags = new TagsViewModel(Host);
        Normalize = new NormalizeViewModel(Host);
        Inspect = new InspectViewModel(Host);
        Split = new SplitViewModel(Host);
        About = new AboutViewModel(Host);
        workspaces = [Convert, Cut, Compress, Artwork, Remix, Tags, Normalize, Inspect, Split];
        activePage = Convert;
        Tools =
        [
            new NavItem("Convert", "GlyphConvert", Convert, Show),
            new NavItem("Compress", "GlyphCompress", Compress, Show),
            new NavItem("Normalize", "GlyphNormalize", Normalize, Show),
            new NavItem("Cut", "GlyphCut", Cut, Show),
            new NavItem("Split album", "GlyphSplit", Split, Show),
            new NavItem("Remix", "GlyphRemix", Remix, Show),
            new NavItem("Tags", "GlyphTags", Tags, Show),
            new NavItem("Artwork", "GlyphArtwork", Artwork, Show),
            new NavItem("Inspect", "GlyphInspect", Inspect, Show),
        ];
        AboutItem = new NavItem("About", "GlyphAbout", About, Show);
        UpdateNavigation();
        ToggleThemeCommand = new AsyncRelayCommand(ToggleThemeAsync);
        OpenOutputFolderCommand = new RelayCommand(() => Host.OpenFolder(Host.LastOutputFolder));
        Host.PropertyChanged += OnHostChanged;
    }

    public WorkspaceHost Host { get; }
    public ConvertViewModel Convert { get; }
    public CutViewModel Cut { get; }
    public CompressViewModel Compress { get; }
    public ArtworkViewModel Artwork { get; }
    public RemixViewModel Remix { get; }
    public TagsViewModel Tags { get; }
    public NormalizeViewModel Normalize { get; }
    public InspectViewModel Inspect { get; }
    public SplitViewModel Split { get; }
    public AboutViewModel About { get; }
    /// <summary>Sidebar entries for the tools, in display order.</summary>
    public IReadOnlyList<NavItem> Tools { get; }
    public NavItem AboutItem { get; }
    public AsyncRelayCommand ToggleThemeCommand { get; }
    public RelayCommand OpenOutputFolderCommand { get; }

    /// <summary>The page shown in the content area: a workspace or the About page.</summary>
    public object ActivePage => activePage;
    public WorkspaceBase? ActiveWorkspace => activePage as WorkspaceBase;
    public bool IsToolWorkspace => ActiveWorkspace is not null;
    public bool ShowOpenOutputFolder => !Host.IsBusy && Host.LastOutputFolder is not null && Host.LastOutputWorkspace == ActiveWorkspace;
    public string Theme { get => theme; private set { if (Set(ref theme, value)) { Raise(nameof(ThemeButtonText)); Raise(nameof(ThemeButtonToolTip)); } } }
    public string ThemeButtonText => $"Theme · {ThemeService.DisplayName(Theme)}";
    public string ThemeButtonToolTip => $"Switch to {ThemeService.DisplayName(ThemeService.Next(Theme))}";

    public async Task InitializeAsync()
    {
        await Host.LoadSettingsAsync();
        if (Host.IsClosing) return;
        Theme = ThemeService.Normalize(Host.Settings.Theme);
        Convert.OutputFolder = Host.Settings.LastOutputDirectory ?? "";
        ThemeService.Apply(Theme);
        Host.TryLocateFfmpeg();
    }

    public async Task AcceptDropAsync(IReadOnlyList<string> paths)
    {
        if (Host.IsBusy || Host.IsClosing || ActiveWorkspace is not { } workspace) return;
        await workspace.AcceptDropAsync(paths);
    }

    public async Task ShutdownAsync()
    {
        await Host.BeginShutdownAsync();
        foreach (var workspace in workspaces) await workspace.ShutdownAsync();
        Dispose();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Host.PropertyChanged -= OnHostChanged;
        Host.Dispose();
    }

    public void Show(object page)
    {
        if (ReferenceEquals(activePage, page)) return;
        activePage = page;
        UpdateNavigation();
        foreach (var name in new[] { nameof(ActivePage), nameof(ActiveWorkspace), nameof(IsToolWorkspace), nameof(ShowOpenOutputFolder) })
            Raise(name);
    }

    private void UpdateNavigation()
    {
        foreach (var item in Tools.Append(AboutItem)) item.IsActive = ReferenceEquals(item.Page, activePage);
    }

    private void OnHostChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkspaceHost.IsBusy) or nameof(WorkspaceHost.LastOutputFolder)) Raise(nameof(ShowOpenOutputFolder));
    }

    private async Task ToggleThemeAsync()
    {
        try
        {
            Theme = ThemeService.Next(Theme);
            ThemeService.Apply(Theme);
            await Host.SaveSettingsAsync(settings => settings with { Theme = Theme });
        }
        catch (Exception error) { Host.Dialogs.Error($"Unable to save theme settings: {error.Message}"); }
    }
}

/// <summary>A sidebar entry; <see cref="GlyphKey"/> names an icon resource in Themes/Controls.xaml.</summary>
public sealed class NavItem(string label, string glyphKey, object page, Action<object> show) : ObservableObject
{
    private bool isActive;
    public string Label { get; } = label;
    public string GlyphKey { get; } = glyphKey;
    public object Page { get; } = page;
    public RelayCommand ShowCommand { get; } = new(() => show(page));
    public bool IsActive { get => isActive; set { if (Set(ref isActive, value)) Raise(nameof(State)); } }
    /// <summary>"Active" or "Inactive", the value the NavButton style triggers on.</summary>
    public string State => IsActive ? "Active" : "Inactive";
}

public sealed class AboutViewModel(WorkspaceHost host)
{
    public string ProductVersion => typeof(AboutViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.2.0";
    public RelayCommand OpenGitHubCommand { get; } = new(() =>
    {
        try { ExternalLinkService.OpenGitHubProfile(); }
        catch (Exception error) { host.Dialogs.Error($"Unable to open GitHub: {error.Message}"); }
    });
    public RelayCommand OpenDiagnosticsCommand { get; } = new(() =>
    {
        host.Log.Write("Diagnostics", "Opened by user");
        try { Process.Start(new ProcessStartInfo(host.Log.FilePath) { UseShellExecute = true }); }
        catch (Exception error) { host.Dialogs.Error(error.Message); }
    });
}
