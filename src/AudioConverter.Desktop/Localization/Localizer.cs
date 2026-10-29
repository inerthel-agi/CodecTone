using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Windows.Data;
using System.Windows.Markup;

namespace AudioConverter.Desktop.Localization;

/// <summary>
/// UI strings from Strings.resx (English) and Strings.fr.resx (French). The language follows Windows unless the user
/// picks one; switching raises an indexer change so every <see cref="T"/> binding updates without a restart.
/// </summary>
public sealed class Localizer : INotifyPropertyChanged
{
    private static readonly ResourceManager Resources = new("AudioConverter.Desktop.Localization.Strings", typeof(Localizer).Assembly);
    private static readonly CultureInfo SystemCulture = CultureInfo.CurrentUICulture;
    private CultureInfo culture = SystemCulture;

    public static Localizer Instance { get; } = new();
    /// <summary>Language choices in cycle order; null follows the Windows display language.</summary>
    public static IReadOnlyList<string?> Languages { get; } = [null, "en", "fr"];

    public event PropertyChangedEventHandler? PropertyChanged;
    /// <summary>Raised after the language changes so view models can refresh computed text.</summary>
    public event Action? LanguageChanged;

    public string? Language { get; private set; }
    public CultureInfo Culture => culture;
    public string this[string key] => Resources.GetString(key, culture) ?? key;

    public static string T(string key) => Instance[key];
    /// <summary>Localized text, or <paramref name="fallback"/> when the key does not exist.</summary>
    public static string TryT(string key, string fallback) => Resources.GetString(key, Instance.culture) ?? fallback;
    public static string F(string key, params object?[] args) => string.Format(Instance.culture, Instance[key], args);

    /// <summary>Known language code, or null (Windows) for anything else.</summary>
    public static string? Normalize(string? language) => Languages.FirstOrDefault(code => code is not null && code.Equals(language, StringComparison.OrdinalIgnoreCase));
    public static string? Next(string? language) => Languages[(Languages.ToList().IndexOf(Normalize(language)) + 1) % Languages.Count];

    public void SetLanguage(string? language)
    {
        Language = Normalize(language);
        culture = Language is null ? SystemCulture : CultureInfo.GetCultureInfo(Language);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        LanguageChanged?.Invoke();
    }

    /// <summary>Name shown on the language button, e.g. "System (Français)".</summary>
    public string LanguageName(string? language) => Normalize(language) switch
    {
        "en" => "English",
        "fr" => "Français",
        _ => F("LanguageSystem", SystemCulture.TwoLetterISOLanguageName == "fr" ? "Français" : "English"),
    };
}

/// <summary>
/// Shows a list value (enum or option string) in the current language: "High fidelity" looks up "ItemHighFidelity".
/// Values without a key, such as format names, show unchanged.
/// </summary>
public sealed class LocalizeItemConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value?.ToString() ?? "";
        return Localizer.TryT("Item" + new string(System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text).Where(char.IsLetterOrDigit).ToArray()), text);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>XAML: <c>Text="{l:T ConvertTitle}"</c> binds to the localized string and follows language changes.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class T : MarkupExtension
{
    public T() { }
    public T(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Localizer.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
