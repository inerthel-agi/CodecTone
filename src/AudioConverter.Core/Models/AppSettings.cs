namespace AudioConverter.Core.Models;

/// <param name="Preferences">Per-tool choices such as "Convert.Format"; unknown keys are ignored on load.</param>
/// <param name="Language">UI language code ("en", "fr"); null follows the Windows display language.</param>
public sealed record AppSettings(
    string Theme = "Oled",
    string? LastOutputDirectory = null,
    IReadOnlyDictionary<string, string>? Preferences = null,
    string? Language = null);
