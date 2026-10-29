using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace AudioConverter.Desktop.Controls;

/// <summary>
/// Waveform with a time ruler, a draggable start/end selection, an optional playhead and keyboard nudging:
/// arrows move the active handle by 0.1 s (Shift: 1 s), [ and ] pick the handle, Home/End jump to the bounds.
/// </summary>
public sealed class WaveformControl : FrameworkElement
{
    private const double RulerHeight = 22;
    private const double HandleHitWidth = 14;
    private static readonly double[] TickSteps = [0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 1200, 1800, 3600];
    private enum DragHandle { None, Start, End }
    private DragHandle dragging;
    private DragHandle active = DragHandle.Start;

    public static readonly DependencyProperty PeaksProperty = DependencyProperty.Register(
        nameof(Peaks), typeof(double[]), typeof(WaveformControl), new FrameworkPropertyMetadata(Array.Empty<double>(), FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty DurationProperty = DependencyProperty.Register(
        nameof(Duration), typeof(double), typeof(WaveformControl), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StartProperty = DependencyProperty.Register(
        nameof(Start), typeof(double), typeof(WaveformControl), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public static readonly DependencyProperty EndProperty = DependencyProperty.Register(
        nameof(End), typeof(double), typeof(WaveformControl), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public static readonly DependencyProperty PlayheadProperty = DependencyProperty.Register(
        nameof(Playhead), typeof(double), typeof(WaveformControl), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SeekCommandProperty = DependencyProperty.Register(
        nameof(SeekCommand), typeof(ICommand), typeof(WaveformControl));

    public double[] Peaks { get => (double[])GetValue(PeaksProperty); set => SetValue(PeaksProperty, value); }
    public double Duration { get => (double)GetValue(DurationProperty); set => SetValue(DurationProperty, value); }
    public double Start { get => (double)GetValue(StartProperty); set => SetValue(StartProperty, value); }
    public double End { get => (double)GetValue(EndProperty); set => SetValue(EndProperty, value); }
    /// <summary>Playback position in source seconds; NaN hides it.</summary>
    public double Playhead { get => (double)GetValue(PlayheadProperty); set => SetValue(PlayheadProperty, value); }
    /// <summary>Executed with the source time in seconds when the ruler is clicked during playback.</summary>
    public ICommand? SeekCommand { get => (ICommand?)GetValue(SeekCommandProperty); set => SetValue(SeekCommandProperty, value); }

    public WaveformControl()
    {
        MinHeight = 160; Cursor = Cursors.SizeWE; Focusable = true;
        // The empty-state text is drawn, not bound, so redraw it when the language changes.
        Loaded += (_, _) => Localization.Localizer.Instance.LanguageChanged += InvalidateVisual;
        Unloaded += (_, _) => Localization.Localizer.Instance.LanguageChanged -= InvalidateVisual;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var surface = FindBrush("PanelRaisedBrush", Brushes.Black);
        var shade = FindBrush("WindowBrush", Brushes.Black);
        var text = FindBrush("TextBrush", Brushes.White);
        var muted = FindBrush("MutedTextBrush", Brushes.Gray);
        var accent = FindBrush("AccentBrush", Brushes.MediumSpringGreen);
        var border = FindBrush("BorderBrush", Brushes.DimGray);
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        drawingContext.DrawRoundedRectangle(surface, new Pen(border, 1), bounds, 6, 6);
        if (Peaks.Length == 0 || Duration <= 0)
        {
            var empty = Text(Localization.Localizer.T("ChooseAnAudioFileToGenerateWaveform"), 13, muted);
            drawingContext.DrawText(empty, new Point((ActualWidth - empty.Width) / 2, (ActualHeight - empty.Height) / 2));
            return;
        }

        drawingContext.PushClip(new RectangleGeometry(bounds, 6, 6));
        DrawRuler(drawingContext, muted, border);

        var top = RulerHeight + 6;
        var middle = top + (ActualHeight - top - 6) / 2;
        var amplitude = (ActualHeight - top - 12) / 2;
        var startX = ToX(Start);
        var endX = ToX(End);
        var inside = new StreamGeometry();
        var outside = new StreamGeometry();
        using (var insideContext = inside.Open())
        using (var outsideContext = outside.Open())
        {
            for (var x = 0; x < (int)ActualWidth; x++)
            {
                var index = Math.Min((int)(x / ActualWidth * Peaks.Length), Peaks.Length - 1);
                var half = Math.Max(1, Peaks[index] * amplitude);
                var target = x >= startX && x <= endX ? insideContext : outsideContext;
                target.BeginFigure(new Point(x + 0.5, middle - half), false, false);
                target.LineTo(new Point(x + 0.5, middle + half), true, false);
            }
        }
        drawingContext.DrawGeometry(null, new Pen(muted, 1), outside);
        drawingContext.DrawGeometry(null, new Pen(text, 1), inside);

        // Dim everything outside the selection so the kept range reads at a glance.
        drawingContext.PushOpacity(0.45);
        drawingContext.DrawRectangle(shade, null, new Rect(0, RulerHeight, Math.Max(0, startX), ActualHeight - RulerHeight));
        drawingContext.DrawRectangle(shade, null, new Rect(endX, RulerHeight, Math.Max(0, ActualWidth - endX), ActualHeight - RulerHeight));
        drawingContext.Pop();

        DrawHandle(drawingContext, startX, accent, active == DragHandle.Start && IsKeyboardFocused);
        DrawHandle(drawingContext, endX, accent, active == DragHandle.End && IsKeyboardFocused);
        DrawHandleLabel(drawingContext, Start, startX, alignRight: false, text, surface);
        DrawHandleLabel(drawingContext, End, endX, alignRight: true, text, surface);

        if (double.IsFinite(Playhead) && Playhead >= 0 && Playhead <= Duration)
        {
            var x = ToX(Playhead);
            drawingContext.DrawLine(new Pen(text, 1.5), new Point(x, RulerHeight), new Point(x, ActualHeight));
            var marker = new StreamGeometry();
            using (var markerContext = marker.Open())
            {
                markerContext.BeginFigure(new Point(x - 5, RulerHeight - 6), true, true);
                markerContext.LineTo(new Point(x + 5, RulerHeight - 6), true, false);
                markerContext.LineTo(new Point(x, RulerHeight), true, false);
            }
            drawingContext.DrawGeometry(text, null, marker);
        }
        drawingContext.Pop();
    }

    private void DrawRuler(DrawingContext drawingContext, Brush muted, Brush border)
    {
        drawingContext.DrawLine(new Pen(border, 1), new Point(0, RulerHeight), new Point(ActualWidth, RulerHeight));
        var step = TickSteps.FirstOrDefault(candidate => candidate / Duration * ActualWidth >= 70, TickSteps[^1]);
        var tickPen = new Pen(border, 1);
        for (var time = 0d; time <= Duration; time += step)
        {
            var x = ToX(time);
            drawingContext.DrawLine(tickPen, new Point(x + 0.5, RulerHeight - 5), new Point(x + 0.5, RulerHeight));
            var label = Text(FormatClock(time, step < 1), 10.5, muted);
            if (x + 4 + label.Width < ActualWidth) drawingContext.DrawText(label, new Point(x + 4, 4));
        }
    }

    private void DrawHandle(DrawingContext drawingContext, double x, Brush accent, bool focused)
    {
        drawingContext.DrawLine(new Pen(accent, focused ? 3 : 2), new Point(x, RulerHeight), new Point(x, ActualHeight));
        var gripHeight = Math.Min(30, ActualHeight / 4);
        var gripTop = RulerHeight + (ActualHeight - RulerHeight - gripHeight) / 2;
        drawingContext.DrawRoundedRectangle(accent, null, new Rect(x - 4, gripTop, 8, gripHeight), 4, 4);
    }

    private void DrawHandleLabel(DrawingContext drawingContext, double seconds, double x, bool alignRight, Brush text, Brush surface)
    {
        var label = Text(FormatClock(seconds, true), 11, text);
        var left = alignRight ? x - label.Width - 10 : x + 8;
        left = Math.Clamp(left, 4, Math.Max(4, ActualWidth - label.Width - 8));
        var box = new Rect(left - 4, RulerHeight + 6, label.Width + 8, label.Height + 2);
        drawingContext.DrawRoundedRectangle(surface, null, box, 4, 4);
        drawingContext.DrawText(label, new Point(left, RulerHeight + 7));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (Duration <= 0) return;
        var point = e.GetPosition(this);
        if (point.Y <= RulerHeight && SeekCommand is { } seek && seek.CanExecute(ToTime(point.X)))
        {
            seek.Execute(ToTime(point.X)); e.Handled = true; return;
        }
        Focus(); CaptureMouse(); var x = point.X;
        dragging = Math.Abs(x - ToX(Start)) <= Math.Abs(x - ToX(End)) ? DragHandle.Start : DragHandle.End;
        active = dragging;
        MoveHandle(dragging, ToTime(x)); e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var x = e.GetPosition(this).X;
        if (dragging != DragHandle.None) MoveHandle(dragging, ToTime(x));
        else if (Duration > 0) Cursor = e.GetPosition(this).Y <= RulerHeight && SeekCommand?.CanExecute(ToTime(x)) == true ? Cursors.Hand
            : Math.Min(Math.Abs(x - ToX(Start)), Math.Abs(x - ToX(End))) <= HandleHitWidth ? Cursors.SizeWE : Cursors.Arrow;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { dragging = DragHandle.None; ReleaseMouseCapture(); e.Handled = true; }
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); InvalidateVisual(); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Duration <= 0) return;
        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 1 : 0.1;
        var current = active == DragHandle.Start ? Start : End;
        switch (e.Key)
        {
            case Key.Left: MoveHandle(active, current - step); break;
            case Key.Right: MoveHandle(active, current + step); break;
            case Key.Home: active = DragHandle.Start; MoveHandle(active, 0); break;
            case Key.End: active = DragHandle.End; MoveHandle(active, Duration); break;
            case Key.OemOpenBrackets: active = DragHandle.Start; InvalidateVisual(); break;
            case Key.OemCloseBrackets: active = DragHandle.End; InvalidateVisual(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void MoveHandle(DragHandle handle, double time)
    {
        time = Math.Clamp(time, 0, Duration);
        if (handle == DragHandle.Start) Start = Math.Min(time, End - 0.001); else if (handle == DragHandle.End) End = Math.Max(time, Start + 0.001);
    }

    private double ToX(double seconds) => Duration <= 0 ? 0 : seconds / Duration * ActualWidth;
    private double ToTime(double x) => x / Math.Max(1, ActualWidth) * Duration;

    private static string FormatClock(double seconds, bool tenths)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        var clock = time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : time.ToString(@"m\:ss", CultureInfo.InvariantCulture);
        return tenths ? clock + "." + (time.Milliseconds / 100).ToString(CultureInfo.InvariantCulture) : clock;
    }

    private FormattedText Text(string value, double size, Brush brush) =>
        new(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private Brush FindBrush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;
}
