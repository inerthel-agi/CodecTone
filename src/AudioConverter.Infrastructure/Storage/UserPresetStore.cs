using System.Text.Json;
using System.Text.Json.Serialization;
using AudioConverter.Core.Remix;

namespace AudioConverter.Infrastructure.Storage;

/// <summary>Saved remix racks in remix-presets.json; a missing or unreadable file means no presets.</summary>
public sealed class UserPresetStore(string? root = null)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private readonly string path = Path.Combine(root ?? AppPaths.Root, "remix-presets.json");

    public IReadOnlyList<UserRemixPreset> Load()
    {
        try
        {
            using var stream = File.OpenRead(path);
            return (JsonSerializer.Deserialize<List<UserRemixPreset>>(stream, Options) ?? [])
                .Where(preset => !string.IsNullOrWhiteSpace(preset.Name) && preset.Effects is not null)
                .ToArray();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return [];
        }
    }

    public void Save(IReadOnlyList<UserRemixPreset> presets)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(presets, Options));
            File.Move(temporary, path, true);
        }
        finally
        {
            try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
