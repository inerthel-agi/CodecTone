namespace AudioConverter.Core.Remix;

public enum RemixPreset
{
    BassBoost,
    SlowedReverb,
    SpedUpReverb,
    Nightcore,
    DeepBass,
    VocalBoost,
    DreamyReverb,
    LoFi,
    Telephone,
    DeepSlowed,
    HalfTime,
    WarmBass,
    SubFocus,
    ClubPunch,
    LightRoom,
    WideHall,
    AmbientWash,
    EchoSpace,
    VocalPresence,
    SoftVocal,
    DeMud,
    ClearMix,
    Radio,
    VintageWarm,
    DarkTone,
    BrightTone,
    CleanMaster,
    LoudMaster,
    DynamicMaster,
    WideMaster,
    StreamingMaster,
    ClubMaster,
    Earrape,
}

public enum RemixPresetCategory
{
    SpeedPitch,
    BassPunch,
    Atmosphere,
    VocalClarity,
    ColorTexture,
    Mastering,
}

public enum RemixIntensity { Light, Medium, Strong }

public sealed record AudioAnalysis(
    double IntegratedLufs,
    double TruePeakDb,
    double LoudnessRange,
    double CrestFactorDb,
    double LowEnergyDb,
    double MidEnergyDb,
    double HighEnergyDb,
    double StereoWidth,
    double DurationSeconds,
    int SampleRate,
    int Channels);

public sealed record RemixPresetDefinition(RemixPreset Preset, string Name, RemixPresetCategory Category, string Description = "");

public sealed record AdaptiveRemixRack(
    IReadOnlyList<RemixEffect> Rack,
    bool IsAdaptive,
    string Explanation);

public enum RemixEffectKind
{
    TempoPitch,
    Bass,
    Equalizer,
    Reverb,
    Echo,
    Volume,
    FadeIn,
    FadeOut,
    LoudnessNormalize,
    Compressor,
    StereoWidth,
    HighPass,
    LowPass,
    SoftLimiter,
    Distortion,
    BitCrusher,
}

public abstract record RemixEffect(RemixEffectKind Kind, bool Enabled = true);

public sealed record TempoPitchEffect(double Rate, bool IsEnabled = true)
    : RemixEffect(RemixEffectKind.TempoPitch, IsEnabled);

public sealed record BassEffect(double GainDb, double FrequencyHz, bool IsEnabled = true)
    : RemixEffect(RemixEffectKind.Bass, IsEnabled);

public sealed record EqualizerEffect(double LowGainDb, double MidGainDb, double HighGainDb, bool IsEnabled = true)
    : RemixEffect(RemixEffectKind.Equalizer, IsEnabled);

public sealed record ReverbEffect(double Mix, double DecaySeconds, double RoomSize = 0.6, bool IsEnabled = true)
    : RemixEffect(RemixEffectKind.Reverb, IsEnabled);

public sealed record EchoEffect(double DelayMilliseconds, double Feedback, double Mix, bool IsEnabled = true)
    : RemixEffect(RemixEffectKind.Echo, IsEnabled);

public sealed record VolumeEffect(double GainDb, bool IsEnabled = true)
    : RemixEffect(RemixEffectKind.Volume, IsEnabled);

public sealed record FadeInEffect(double DurationSeconds, bool IsEnabled = true)
    : RemixEffect(RemixEffectKind.FadeIn, IsEnabled);

public sealed record FadeOutEffect(double DurationSeconds, bool IsEnabled = true)
    : RemixEffect(RemixEffectKind.FadeOut, IsEnabled);

public sealed record LoudnessNormalizeEffect(double TargetLufs, bool IsEnabled = true)
    : RemixEffect(RemixEffectKind.LoudnessNormalize, IsEnabled);

public sealed record CompressorEffect(double ThresholdDb, double Ratio, double MakeupDb, bool IsEnabled = true)
    : RemixEffect(RemixEffectKind.Compressor, IsEnabled);

public sealed record StereoWidthEffect(double Width, bool IsEnabled = true)
    : RemixEffect(RemixEffectKind.StereoWidth, IsEnabled);

public sealed record HighPassEffect(double FrequencyHz, bool IsEnabled = true)
    : RemixEffect(RemixEffectKind.HighPass, IsEnabled);

public sealed record LowPassEffect(double FrequencyHz, bool IsEnabled = true)
    : RemixEffect(RemixEffectKind.LowPass, IsEnabled);

public sealed record SoftLimiterEffect(double CeilingDb, bool IsEnabled = true)
    : RemixEffect(RemixEffectKind.SoftLimiter, IsEnabled);

public sealed record DistortionEffect(double DriveDb, double Threshold, double Oversample, bool IsEnabled = true)
    : RemixEffect(RemixEffectKind.Distortion, IsEnabled);

public sealed record BitCrusherEffect(double Bits, double Samples, double Mix, bool IsEnabled = true)
    : RemixEffect(RemixEffectKind.BitCrusher, IsEnabled);

public enum CoverArtAction
{
    Keep,
    Replace,
    Remove,
}

public sealed record RemixMetadata(
    string? Title = null,
    string? Artist = null,
    string? Album = null,
    string? AlbumArtist = null,
    string? Genre = null,
    string? Date = null,
    string? Track = null,
    string? Disc = null,
    string? Comment = null,
    IReadOnlyDictionary<string, string>? CustomTags = null,
    CoverArtAction CoverAction = CoverArtAction.Keep,
    string? CoverPath = null);
