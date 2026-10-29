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
    /// <summary>True when <paramref name="value"/> is the text of <paramref name="key"/> in any supported language.</summary>
    public static bool IsText(string? value, string key) =>
        Languages.Any(code => Resources.GetString(key, code is null ? SystemCulture : CultureInfo.GetCultureInfo(code)) == value);

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
/// Values without a key, such as format names, show unchanged. Bound as a MultiBinding with
/// <see cref="Localizer.Language"/> as second value, the text follows language changes.
/// </summary>
public sealed class LocalizeItemConverter : IValueConverter, IMultiValueConverter
{
    /// <summary>"High fidelity" → "ItemHighFidelity", TempoPitch → "ItemTempoPitch".</summary>
    public static string Key(string text) =>
        "Item" + new string(string.Concat(text.Split(' ').Select(word => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..])).Where(char.IsLetterOrDigit).ToArray());

    public static string Localize(object? value)
    {
        var text = value?.ToString() ?? "";
        return Localizer.TryT(Key(text), text);
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => Localize(value);
    /// <summary>With a ConverterParameter prefix, values are (id, fallback text, language): "PresetDescription" + id.</summary>
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture) =>
        parameter is string prefix ? Localizer.TryT(prefix + values[0], values.ElementAtOrDefault(1)?.ToString() ?? "") : Localize(values.FirstOrDefault());
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) => [];
}

/// <summary>Formats the first value with the resource named by ConverterParameter; the language is the second value.</summary>
public sealed class LocalizedFormatConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture) => Localizer.F((string)parameter!, values.FirstOrDefault());
    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) => [];
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
