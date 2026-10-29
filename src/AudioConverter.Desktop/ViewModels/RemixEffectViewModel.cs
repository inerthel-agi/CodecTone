using AudioConverter.Core.Remix;
using AudioConverter.Desktop.Mvvm;

namespace AudioConverter.Desktop.ViewModels;

public sealed class RemixEffectViewModel : ObservableObject
{
    private readonly Action changed;
    private bool enabled = true;
    private double first;
    private double second;
    private double third;

    public RemixEffectViewModel(RemixEffectKind kind, Action changed)
    {
        Kind = kind;
        this.changed = changed;
        (first, second, third) = Defaults(kind);
    }

    public RemixEffectKind Kind { get; }
    public string Name => Kind switch
    {
        RemixEffectKind.TempoPitch => "Tempo / Pitch",
        RemixEffectKind.LoudnessNormalize => "Loudness Normalize",
        RemixEffectKind.FadeIn => "Fade In",
        RemixEffectKind.FadeOut => "Fade Out",
        RemixEffectKind.BitCrusher => "Bit Crusher",
        _ => Kind.ToString(),
    };
    public bool Enabled { get => enabled; set { if (Set(ref enabled, value)) changed(); } }
    public double First { get => first; set { if (Set(ref first, value)) changed(); } }
    public double Second { get => second; set { if (Set(ref second, value)) changed(); } }
    public double Third { get => third; set { if (Set(ref third, value)) changed(); } }
    // Slider ranges come from the validator; fades use the 30 s cap because the source length is checked on export.
    public double FirstMinimum => Range(0).Minimum;
    public double FirstMaximum => Range(0).Maximum;
    public double FirstStep => Step(0);
    public double SecondMinimum => HasSecond ? Range(1).Minimum : 0;
    public double SecondMaximum => HasSecond ? Range(1).Maximum : 1;
    public double SecondStep => HasSecond ? Step(1) : 1;
    public double ThirdMinimum => HasThird ? Range(2).Minimum : 0;
    public double ThirdMaximum => HasThird ? Range(2).Maximum : 1;
    public double ThirdStep => HasThird ? Step(2) : 1;
    private (double Minimum, double Maximum) Range(int parameter) => RemixRackValidator.Bounds(Kind, parameter, double.PositiveInfinity);
    private double Step(int parameter)
    {
        if (Kind == RemixEffectKind.Distortion && parameter == 2) return 1;
        var (minimum, maximum) = Range(parameter);
        var span = maximum - minimum;
        return span <= 2 ? 0.01 : span <= 50 ? 0.5 : span <= 500 ? 1 : 10;
    }
    public bool HasSecond =>Kind is RemixEffectKind.Bass or RemixEffectKind.Equalizer or RemixEffectKind.Reverb or RemixEffectKind.Echo or RemixEffectKind.Compressor or RemixEffectKind.Distortion or RemixEffectKind.BitCrusher;
    public bool HasThird => Kind is RemixEffectKind.Equalizer or RemixEffectKind.Reverb or RemixEffectKind.Echo or RemixEffectKind.Compressor or RemixEffectKind.Distortion or RemixEffectKind.BitCrusher;
    public string FirstLabel => Kind switch
    {
        RemixEffectKind.TempoPitch => "Rate · 0.50–2.00×",
        RemixEffectKind.Bass => "Gain · dB",
        RemixEffectKind.Equalizer => "Low · dB",
        RemixEffectKind.Reverb => "Wet mix · 0–1",
        RemixEffectKind.Echo => "Delay · ms",
        RemixEffectKind.Volume => "Gain · dB",
        RemixEffectKind.FadeIn or RemixEffectKind.FadeOut => "Duration · seconds",
        RemixEffectKind.LoudnessNormalize => "Target · LUFS",
        RemixEffectKind.Compressor => "Threshold · dB",
        RemixEffectKind.StereoWidth => "Width · 0–2",
        RemixEffectKind.HighPass or RemixEffectKind.LowPass => "Frequency · Hz",
        RemixEffectKind.SoftLimiter => "Ceiling · dB",
        RemixEffectKind.Distortion => "Drive · dB",
        RemixEffectKind.BitCrusher => "Bit depth · 1–24",
        _ => "Value",
    };
    public string SecondLabel => Kind switch
    {
        RemixEffectKind.Bass => "Frequency · Hz",
        RemixEffectKind.Equalizer => "Mid · dB",
        RemixEffectKind.Reverb => "Decay · seconds",
        RemixEffectKind.Echo => "Feedback · 0–0.9",
        RemixEffectKind.Compressor => "Ratio · 1–10",
        RemixEffectKind.Distortion => "Clip threshold · 0–1",
        RemixEffectKind.BitCrusher => "Sample reduction · 1–250",
        _ => "",
    };
    public string ThirdLabel => Kind switch
    {
        RemixEffectKind.Equalizer => "High · dB",
        RemixEffectKind.Reverb => "Room size · 0–1",
        RemixEffectKind.Echo => "Wet mix · 0–1",
        RemixEffectKind.Compressor => "Makeup · dB",
        RemixEffectKind.Distortion => "Oversample · 1–16",
        RemixEffectKind.BitCrusher => "Wet mix · 0–1",
        _ => "",
    };

    public RemixEffect ToModel() => Kind switch
    {
        RemixEffectKind.TempoPitch => new TempoPitchEffect(First, Enabled),
        RemixEffectKind.Bass => new BassEffect(First, Second, Enabled),
        RemixEffectKind.Equalizer => new EqualizerEffect(First, Second, Third, Enabled),
        RemixEffectKind.Reverb => new ReverbEffect(First, Second, Third, Enabled),
        RemixEffectKind.Echo => new EchoEffect(First, Second, Third, Enabled),
        RemixEffectKind.Volume => new VolumeEffect(First, Enabled),
        RemixEffectKind.FadeIn => new FadeInEffect(First, Enabled),
        RemixEffectKind.FadeOut => new FadeOutEffect(First, Enabled),
        RemixEffectKind.LoudnessNormalize => new LoudnessNormalizeEffect(First, Enabled),
        RemixEffectKind.Compressor => new CompressorEffect(First, Second, Third, Enabled),
        RemixEffectKind.StereoWidth => new StereoWidthEffect(First, Enabled),
        RemixEffectKind.HighPass => new HighPassEffect(First, Enabled),
        RemixEffectKind.LowPass => new LowPassEffect(First, Enabled),
        RemixEffectKind.SoftLimiter => new SoftLimiterEffect(First, Enabled),
        RemixEffectKind.Distortion => new DistortionEffect(First, Second, Third, Enabled),
        RemixEffectKind.BitCrusher => new BitCrusherEffect(First, Second, Third, Enabled),
        _ => throw new ArgumentOutOfRangeException(),
    };

    public static RemixEffectViewModel From(RemixEffect effect, Action changed)
    {
        var viewModel = new RemixEffectViewModel(effect.Kind, changed) { Enabled = effect.Enabled };
        (viewModel.first, viewModel.second, viewModel.third) = effect switch
        {
            TempoPitchEffect value => (value.Rate, 0, 0),
            BassEffect value => (value.GainDb, value.FrequencyHz, 0),
            EqualizerEffect value => (value.LowGainDb, value.MidGainDb, value.HighGainDb),
            ReverbEffect value => (value.Mix, value.DecaySeconds, value.RoomSize),
            EchoEffect value => (value.DelayMilliseconds, value.Feedback, value.Mix),
            VolumeEffect value => (value.GainDb, 0, 0),
            FadeInEffect value => (value.DurationSeconds, 0, 0),
            FadeOutEffect value => (value.DurationSeconds, 0, 0),
            LoudnessNormalizeEffect value => (value.TargetLufs, 0, 0),
            CompressorEffect value => (value.ThresholdDb, value.Ratio, value.MakeupDb),
            StereoWidthEffect value => (value.Width, 0, 0),
            HighPassEffect value => (value.FrequencyHz, 0, 0),
            LowPassEffect value => (value.FrequencyHz, 0, 0),
            SoftLimiterEffect value => (value.CeilingDb, 0, 0),
            DistortionEffect value => (value.DriveDb, value.Threshold, value.Oversample),
            BitCrusherEffect value => (value.Bits, value.Samples, value.Mix),
            _ => (0, 0, 0),
        };
        return viewModel;
    }

    private static (double, double, double) Defaults(RemixEffectKind kind) => kind switch
    {
        RemixEffectKind.TempoPitch => (1, 0, 0),
        RemixEffectKind.Bass => (6, 90, 0),
        RemixEffectKind.Equalizer => (0, 0, 0),
        RemixEffectKind.Reverb => (0.25, 2, 0.6),
        RemixEffectKind.Echo => (250, 0.35, 0.25),
        RemixEffectKind.Volume => (0, 0, 0),
        RemixEffectKind.FadeIn or RemixEffectKind.FadeOut => (3, 0, 0),
        RemixEffectKind.LoudnessNormalize => (-14, 0, 0),
        RemixEffectKind.Compressor => (-18, 2, 1),
        RemixEffectKind.StereoWidth => (1.1, 0, 0),
        RemixEffectKind.HighPass => (30, 0, 0),
        RemixEffectKind.LowPass => (18_000, 0, 0),
        RemixEffectKind.SoftLimiter => (-1, 0, 0),
        RemixEffectKind.Distortion => (12, 0.3, 4),
        RemixEffectKind.BitCrusher => (8, 4, 0.5),
        _ => (0, 0, 0),
    };
}

public sealed class MetadataTagViewModel : ObservableObject
{
    private string key = "";
    private string value = "";
    public string Key { get => key; set => Set(ref key, value); }
    public string Value { get => value; set => Set(ref this.value, value); }
}
