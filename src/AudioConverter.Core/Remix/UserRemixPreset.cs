namespace AudioConverter.Core.Remix;

/// <summary>A rack the user saved under a name. Values follow the effect's parameter order (see RemixRackValidator.Bounds).</summary>
public sealed record UserRemixPreset(string Name, IReadOnlyList<UserRemixEffect> Effects)
{
    public const int MaximumNameLength = 40;

    /// <summary>Trimmed name, or an error message when the name is empty or too long.</summary>
    public static (string? Name, string? Error) ValidateName(string? name)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0) return (null, "Enter a name for the preset.");
        if (trimmed.Length > MaximumNameLength) return (null, $"Preset names are limited to {MaximumNameLength} characters.");
        return (trimmed, null);
    }
}

public sealed record UserRemixEffect(RemixEffectKind Kind, bool Enabled, double First, double Second, double Third);
