namespace AudioConverter.Core.Remix;

public static class RemixPresetCatalog
{
    public static IReadOnlyList<RemixPresetDefinition> All { get; } =
    [
        Item(RemixPreset.SlowedReverb, "Slowed + Reverb", RemixPresetCategory.SpeedPitch, "Slower and lower, with a soft room."),
        Item(RemixPreset.DeepSlowed, "Deep Slowed", RemixPresetCategory.SpeedPitch, "Much slower and darker, with a long tail."),
        Item(RemixPreset.SpedUpReverb, "Sped Up + Reverb", RemixPresetCategory.SpeedPitch, "Faster and higher, with a light room."),
        Item(RemixPreset.Nightcore, "Nightcore", RemixPresetCategory.SpeedPitch, "Fast, high and bright, then loudness-matched."),
        Item(RemixPreset.HalfTime, "Half-Time", RemixPresetCategory.SpeedPitch, "Half speed, one octave down."),
        Item(RemixPreset.WarmBass, "Warm Bass", RemixPresetCategory.BassPunch, "A gentle low-end lift."),
        Item(RemixPreset.BassBoost, "Bass Boost", RemixPresetCategory.BassPunch, "A strong bass shelf around 90 Hz."),
        Item(RemixPreset.DeepBass, "Deep Bass", RemixPresetCategory.BassPunch, "Heavy low end with softer highs."),
        Item(RemixPreset.SubFocus, "Sub Focus", RemixPresetCategory.BassPunch, "Tight sub-bass, rumble removed."),
        Item(RemixPreset.ClubPunch, "Club Punch", RemixPresetCategory.BassPunch, "Compressed, loud and punchy low end."),
        Item(RemixPreset.LightRoom, "Light Room", RemixPresetCategory.Atmosphere, "A short, subtle room."),
        Item(RemixPreset.DreamyReverb, "Dreamy Reverb", RemixPresetCategory.Atmosphere, "A long, soft reverb with mellow highs."),
        Item(RemixPreset.WideHall, "Wide Hall", RemixPresetCategory.Atmosphere, "A large hall and a wider stereo image."),
        Item(RemixPreset.AmbientWash, "Ambient Wash", RemixPresetCategory.Atmosphere, "A very long, dark reverb wash."),
        Item(RemixPreset.EchoSpace, "Echo Space", RemixPresetCategory.Atmosphere, "Repeating echoes in a medium room."),
        Item(RemixPreset.VocalBoost, "Vocal Boost", RemixPresetCategory.VocalClarity, "Pushes the midrange where voices sit."),
        Item(RemixPreset.VocalPresence, "Vocal Presence", RemixPresetCategory.VocalClarity, "Clearer, steadier vocals."),
        Item(RemixPreset.SoftVocal, "Soft Vocal", RemixPresetCategory.VocalClarity, "Smooth vocals with a little room."),
        Item(RemixPreset.DeMud, "De-Mud", RemixPresetCategory.VocalClarity, "Cuts boomy low mids."),
        Item(RemixPreset.ClearMix, "Clear Mix", RemixPresetCategory.VocalClarity, "A cleaner, more detailed mix."),
        Item(RemixPreset.LoFi, "Lo-Fi", RemixPresetCategory.ColorTexture, "Slightly slow, muffled and dusty."),
        Item(RemixPreset.Telephone, "Telephone", RemixPresetCategory.ColorTexture, "Narrow, midrange-only phone sound."),
        Item(RemixPreset.Radio, "Radio", RemixPresetCategory.ColorTexture, "Band-limited, compressed AM radio."),
        Item(RemixPreset.VintageWarm, "Vintage Warm", RemixPresetCategory.ColorTexture, "Warm lows and rounded highs."),
        Item(RemixPreset.DarkTone, "Dark Tone", RemixPresetCategory.ColorTexture, "Softer, darker highs."),
        Item(RemixPreset.BrightTone, "Bright Tone", RemixPresetCategory.ColorTexture, "More air and sparkle."),
        Item(RemixPreset.Earrape, "EARRAPE", RemixPresetCategory.ColorTexture, "Extreme distortion and loudness."),
        Item(RemixPreset.CleanMaster, "Clean Master", RemixPresetCategory.Mastering, "Light glue at -14 LUFS."),
        Item(RemixPreset.LoudMaster, "Loud Master", RemixPresetCategory.Mastering, "Dense and loud at -9 LUFS."),
        Item(RemixPreset.DynamicMaster, "Dynamic Master", RemixPresetCategory.Mastering, "Keeps dynamics, -14 LUFS."),
        Item(RemixPreset.WideMaster, "Wide Master", RemixPresetCategory.Mastering, "Glued and wider, -14 LUFS."),
        Item(RemixPreset.StreamingMaster, "Streaming -14 LUFS", RemixPresetCategory.Mastering, "Matches common streaming loudness."),
        Item(RemixPreset.ClubMaster, "Club Master -10 LUFS", RemixPresetCategory.Mastering, "Loud, punchy master for club play."),
    ];

    public static IReadOnlyList<RemixPresetDefinition> For(RemixPresetCategory category) =>
        All.Where(item => item.Category == category).ToArray();

    private static RemixPresetDefinition Item(RemixPreset preset, string name, RemixPresetCategory category, string description) =>
        new(preset, name, category, description);
}
