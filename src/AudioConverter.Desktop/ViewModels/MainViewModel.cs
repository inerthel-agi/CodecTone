using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using AudioConverter.Desktop.Mvvm;
using AudioConverter.Desktop.Services;
using AudioConverter.Infrastructure.Shell;
using L = AudioConverter.Desktop.Localization.Localizer;

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
        ToggleLanguageCommand = new AsyncRelayCommand(ToggleLanguageAsync);
        OpenOutputFolderCommand = new RelayCommand(() => Host.OpenFolder(Host.LastOutputFolder));
        Host.PropertyChanged += OnHostChanged;
        L.Instance.LanguageChanged += OnLanguageChanged;
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
    public AsyncRelayCommand ToggleLanguageCommand { get; }
    public RelayCommand OpenOutputFolderCommand { get; }

    /// <summary>The page shown in the content area: a workspace or the About page.</summary>
    public object ActivePage => activePage;
    public WorkspaceBase? ActiveWorkspace => activePage as WorkspaceBase;
    public bool IsToolWorkspace => ActiveWorkspace is not null;
    public bool ShowOpenOutputFolder => !Host.IsBusy && Host.LastOutputFolder is not null && Host.LastOutputWorkspace == ActiveWorkspace;
    public string Theme { get => theme; private set { if (Set(ref theme, value)) { Raise(nameof(ThemeButtonText)); Raise(nameof(ThemeButtonToolTip)); } } }
    public string ThemeButtonText => L.F("ThemeButton", ThemeService.DisplayName(Theme));
    public string ThemeButtonToolTip => L.F("SwitchTo", ThemeService.DisplayName(ThemeService.Next(Theme)));
    public string LanguageButtonText => L.F("LanguageButton", L.Instance.LanguageName(L.Instance.Language));
    public string LanguageButtonToolTip => L.F("SwitchTo", L.Instance.LanguageName(L.Next(L.Instance.Language)));

    public async Task InitializeAsync()
    {
        await Host.LoadSettingsAsync();
        if (Host.IsClosing) return;
        Theme = ThemeService.Normalize(Host.Settings.Theme);
        L.Instance.SetLanguage(Host.Settings.Language);
        Convert.OutputFolder = Host.Settings.LastOutputDirectory ?? "";
        var preferences = Host.Settings.Preferences ?? new Dictionary<string, string>();
        foreach (var workspace in workspaces) workspace.LoadPreferences(preferences);
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
        await SavePreferencesAsync();
        Dispose();
    }

    /// <summary>Stores every tool's choices so the next launch starts where this one ended.</summary>
    public async Task SavePreferencesAsync()
    {
        var preferences = new Dictionary<string, string>(Host.Settings.Preferences ?? new Dictionary<string, string>());
        foreach (var workspace in workspaces) workspace.SavePreferences(preferences);
        try { await Host.SaveSettingsAsync(settings => settings with { Preferences = preferences }); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Host.Log.Write("Settings", error.Message); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Host.PropertyChanged -= OnHostChanged;
        L.Instance.LanguageChanged -= OnLanguageChanged;
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

    private void OnLanguageChanged()
    {
        foreach (var item in Tools.Append(AboutItem)) item.RefreshText();
        Host.RefreshText();
        Raise(string.Empty);
    }

    private async Task ToggleLanguageAsync()
    {
        try
        {
            L.Instance.SetLanguage(L.Next(L.Instance.Language));
            await Host.SaveSettingsAsync(settings => settings with { Language = L.Instance.Language });
        }
        catch (Exception error) { Host.Dialogs.Error(L.F("UnableToSaveSettings", error.Message)); }
    }

    private async Task ToggleThemeAsync()
    {
        try
        {
            Theme = ThemeService.Next(Theme);
            ThemeService.Apply(Theme);
            await Host.SaveSettingsAsync(settings => settings with { Theme = Theme });
        }
        catch (Exception error) { Host.Dialogs.Error(L.F("UnableToSaveThemeSettings", error.Message)); }
    }
}

/// <summary>A sidebar entry; <see cref="GlyphKey"/> names an icon resource in Themes/Controls.xaml.</summary>
public sealed class NavItem(string label, string glyphKey, object page, Action<object> show) : ObservableObject
{
    private bool isActive;
    /// <summary>English name; it also names the label's resource key ("Split album" → "ItemSplitAlbum").</summary>
    public string Name { get; } = label;
    public string Label => Localization.LocalizeItemConverter.Localize(Name);
    public string GlyphKey { get; } = glyphKey;
    public object Page { get; } = page;
    public RelayCommand ShowCommand { get; } = new(() => show(page));
    public bool IsActive { get => isActive; set { if (Set(ref isActive, value)) Raise(nameof(State)); } }
    /// <summary>"Active" or "Inactive", the value the NavButton style triggers on.</summary>
    public string State => IsActive ? "Active" : "Inactive";
    public void RefreshText() => Raise(nameof(Label));
    /// <summary>Screen readers name the sidebar list item from this.</summary>
    public override string ToString() => Label;
}

public sealed class AboutViewModel(WorkspaceHost host)
{
    public string ProductVersion => typeof(AboutViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.3.0";
    public RelayCommand OpenGitHubCommand { get; } = new(() =>
    {
        try { ExternalLinkService.OpenGitHubProfile(); }
        catch (Exception error) { host.Dialogs.Error(L.F("UnableToOpenGitHub", error.Message)); }
    });
    public RelayCommand OpenDiagnosticsCommand { get; } = new(() =>
    {
        host.Log.Write("Diagnostics", "Opened by user");
        try { Process.Start(new ProcessStartInfo(host.Log.FilePath) { UseShellExecute = true }); }
        catch (Exception error) { host.Dialogs.Error(error.Message); }
    });
}
