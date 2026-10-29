namespace AudioConverter.Core.Remix;

public static class RemixRackValidator
{
    private const int MaximumEffects = 16;

    public static void Validate(IReadOnlyList<RemixEffect> effects, double sourceDurationSeconds)
    {
        ArgumentNullException.ThrowIfNull(effects);
        if (!double.IsFinite(sourceDurationSeconds) || sourceDurationSeconds <= 0)
            throw new ArgumentException("Source duration must be positive.", nameof(sourceDurationSeconds));
        if (effects.Count > MaximumEffects) throw new ArgumentException($"A remix rack supports at most {MaximumEffects} effects.");

        var active = effects.Where(effect => effect.Enabled).ToArray();
        RejectDuplicate<TempoPitchEffect>(active, "Tempo/Pitch");
        RejectDuplicate<LoudnessNormalizeEffect>(active, "Loudness Normalize");
        RejectDuplicate<FadeInEffect>(active, "Fade In");
        RejectDuplicate<FadeOutEffect>(active, "Fade Out");

        foreach (var effect in active) ValidateEffect(effect, sourceDurationSeconds);

        var normalizeIndex = Array.FindIndex(active, effect => effect is LoudnessNormalizeEffect);
        if (normalizeIndex >= 0 && active.Skip(normalizeIndex + 1).Any(IsGainOrToneEffect))
            throw new ArgumentException("Loudness Normalize must follow gain and tone effects.");

        var firstFade = Array.FindIndex(active, effect => effect is FadeInEffect or FadeOutEffect);
        if (firstFade >= 0 && active.Skip(firstFade).Any(effect => effect is not FadeInEffect and not FadeOutEffect))
            throw new ArgumentException("Fade effects must remain at the end of the rack.");
    }

    public static double CalculateOutputDuration(double sourceDurationSeconds, IReadOnlyList<RemixEffect> effects)
    {
        var rate = effects.OfType<TempoPitchEffect>().FirstOrDefault(effect => effect.Enabled)?.Rate ?? 1;
        return sourceDurationSeconds / rate;
    }

    /// <summary>Allowed range of an effect parameter (0-based, in record order). The UI sliders use the same ranges.</summary>
    public static (double Minimum, double Maximum) Bounds(RemixEffectKind kind, int parameter, double sourceDurationSeconds) => (kind, parameter) switch
    {
        (RemixEffectKind.TempoPitch, 0) => (0.5, 2),
        (RemixEffectKind.Bass, 0) => (-12, 18),
        (RemixEffectKind.Bass, 1) => (40, 250),
        (RemixEffectKind.Equalizer, 0 or 1 or 2) => (-18, 18),
        (RemixEffectKind.Reverb, 0) => (0, 1),
        (RemixEffectKind.Reverb, 1) => (0.2, 8),
        (RemixEffectKind.Reverb, 2) => (0, 1),
        (RemixEffectKind.Echo, 0) => (20, 2000),
        (RemixEffectKind.Echo, 1) => (0, 0.9),
        (RemixEffectKind.Echo, 2) => (0, 1),
        (RemixEffectKind.Volume, 0) => (-24, 12),
        (RemixEffectKind.FadeIn or RemixEffectKind.FadeOut, 0) => (0, Math.Min(30, sourceDurationSeconds)),
        (RemixEffectKind.LoudnessNormalize, 0) => (-24, -8),
        (RemixEffectKind.Compressor, 0) => (-40, -3),
        (RemixEffectKind.Compressor, 1) => (1, 10),
        (RemixEffectKind.Compressor, 2) => (0, 12),
        (RemixEffectKind.StereoWidth, 0) => (0, 2),
        (RemixEffectKind.HighPass, 0) => (20, 500),
        (RemixEffectKind.LowPass, 0) => (1_000, 20_000),
        (RemixEffectKind.SoftLimiter, 0) => (-6, -0.1),
        (RemixEffectKind.Distortion, 0) => (0, 36),
        (RemixEffectKind.Distortion, 1) => (0.0001, 1),
        (RemixEffectKind.Distortion, 2) => (1, 16),
        (RemixEffectKind.BitCrusher, 0) => (1, 24),
        (RemixEffectKind.BitCrusher, 1) => (1, 250),
        (RemixEffectKind.BitCrusher, 2) => (0, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(parameter), $"{kind} has no parameter {parameter}."),
    };

    private static void ValidateEffect(RemixEffect effect, double sourceDuration)
    {
        void Require(double value, int parameter, string name)
        {
            var (minimum, maximum) = Bounds(effect.Kind, parameter, sourceDuration);
            if (!double.IsFinite(value) || value < minimum || value > maximum)
                throw new ArgumentException($"{name} must be between {minimum} and {maximum}.");
        }

        switch (effect)
        {
            case TempoPitchEffect value: Require(value.Rate, 0, "Tempo/Pitch rate"); break;
            case BassEffect value:
                Require(value.GainDb, 0, "Bass gain");
                Require(value.FrequencyHz, 1, "Bass frequency");
                break;
            case EqualizerEffect value:
                Require(value.LowGainDb, 0, "Low EQ gain");
                Require(value.MidGainDb, 1, "Mid EQ gain");
                Require(value.HighGainDb, 2, "High EQ gain");
                break;
            case ReverbEffect value:
                Require(value.Mix, 0, "Reverb mix");
                Require(value.DecaySeconds, 1, "Reverb decay");
                Require(value.RoomSize, 2, "Reverb room size");
                break;
            case EchoEffect value:
                Require(value.DelayMilliseconds, 0, "Echo delay");
                Require(value.Feedback, 1, "Echo feedback");
                Require(value.Mix, 2, "Echo mix");
                break;
            case VolumeEffect value: Require(value.GainDb, 0, "Volume gain"); break;
            case FadeInEffect value: Require(value.DurationSeconds, 0, "Fade In duration"); break;
            case FadeOutEffect value: Require(value.DurationSeconds, 0, "Fade Out duration"); break;
            case LoudnessNormalizeEffect value: Require(value.TargetLufs, 0, "Loudness target"); break;
            case CompressorEffect value:
                Require(value.ThresholdDb, 0, "Compressor threshold");
                Require(value.Ratio, 1, "Compressor ratio");
                Require(value.MakeupDb, 2, "Compressor makeup");
                break;
            case StereoWidthEffect value: Require(value.Width, 0, "Stereo width"); break;
            case HighPassEffect value: Require(value.FrequencyHz, 0, "High-pass frequency"); break;
            case LowPassEffect value: Require(value.FrequencyHz, 0, "Low-pass frequency"); break;
            case SoftLimiterEffect value: Require(value.CeilingDb, 0, "Limiter ceiling"); break;
            case DistortionEffect value:
                Require(value.DriveDb, 0, "Distortion drive");
                Require(value.Threshold, 1, "Distortion threshold");
                Require(value.Oversample, 2, "Distortion oversample");
                if (value.Oversample != Math.Truncate(value.Oversample))
                    throw new ArgumentException("Distortion oversample must be an integer.");
                break;
            case BitCrusherEffect value:
                Require(value.Bits, 0, "Bit Crusher bits");
                Require(value.Samples, 1, "Bit Crusher samples");
                Require(value.Mix, 2, "Bit Crusher mix");
                break;
        }
    }

    private static bool IsGainOrToneEffect(RemixEffect effect) =>
        effect is BassEffect or EqualizerEffect or ReverbEffect or EchoEffect or VolumeEffect
            or CompressorEffect or StereoWidthEffect or HighPassEffect or LowPassEffect
            or DistortionEffect or BitCrusherEffect;

    private static void RejectDuplicate<T>(IEnumerable<RemixEffect> effects, string name)
        where T : RemixEffect
    {
        if (effects.OfType<T>().Skip(1).Any()) throw new ArgumentException($"{name} can appear only once.");
    }
}
