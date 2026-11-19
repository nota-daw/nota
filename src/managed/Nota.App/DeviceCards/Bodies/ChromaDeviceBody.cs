// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
// HQ scale coloration with a piano-style note selector.
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using Nota.Application.Localization;
using static Nota.App.DeviceCardKit;
namespace Nota.App;

internal sealed class ChromaDeviceBody : IDeviceBody
{
    private const int Root = 0, Scale = 1, Amount = 2, Resonance = 3, Decay = 4, Low = 5, High = 6,
        Mix = 7, Output = 8, NoteC = 9, Morph = 21, Gate = 22, GateThreshold = 23;
    private static readonly string[] Notes = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
    public double Width => 560;

    private static double ExpHz(double v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0, 1));
    private static double NormHz(double hz, double lo, double hi) => Math.Clamp(Math.Log(Math.Clamp(hz, lo, hi) / lo) / Math.Log(hi / lo), 0, 1);

    public Control Build(DeviceCardContext ctx, int index)
    {
        var e = ctx.Engine; int track = ctx.TrackId;
        float P(int p) => e.DeviceGetParam(track, index, p);
        void Begin(int p) => e.BeginAutomationWrite(track, AutomationTarget.DeviceParam, index, p, "");
        void End(int p) => e.EndAutomationWrite(track, AutomationTarget.DeviceParam, index, p, "");
        void Set(int p, double v) { Begin(p); e.DeviceSetParam(track, index, p, (float)Math.Clamp(v, 0, 1)); End(p); }
        var refreshers = new List<Action>();
        void Refresh() { foreach (var f in refreshers) f(); }

        int Mask()
        {
            int scale = Math.Clamp((int)Math.Round(P(Scale) * 4), 0, 4), root = Math.Clamp((int)Math.Round(P(Root) * 11), 0, 11);
            if (scale == 4) { int mask = 0; for (int k = 0; k < 12; k++) if (P(NoteC + k) >= .5f) mask |= 1 << k; return mask; }
            int m = new[] { 0xab5, 0x5ad, 0x295, 0x4a9 }[scale];
            return ((m << root) | (m >> (12 - root))) & 4095;
        }

        Control KnobFor(int p, Func<double, string> format, double size = 34, double cellW = 46)
        {
            var value = new TextBlock { Text = format(P(p)), FontSize = NotaType.KnobValue, Foreground = TextPrimary };
            var knob = new Knob(P(p), 1) { Accent = true, Width = size, Height = size, Default = e.DeviceParamDefault(track, index, p), Format = format };
            knob.ValueChanged += v => { e.DeviceSetParam(track, index, p, (float)v); value.Text = format(v); Refresh(); };
            knob.GestureBegin += () => Begin(p); knob.GestureEnd += () => End(p);
            MidiLearn.Bind(knob, MidiTarget.DeviceParam(track, index, p), e.DeviceParamName(track, index, p));
            var field = ValueEntry.DeviceParam(e, track, index, p, e.DeviceParamName(track, index, p), format, Refresh);
            ValueEntry.AttachAll(knob, field); ValueEntry.AttachAll(value, field);
            refreshers.Add(() => { if (!knob.Dragging) { if (Math.Abs(knob.Value - P(p)) > 1e-5) knob.Value = P(p); value.Text = format(P(p)); } });
            return KnobCell(e.DeviceParamName(track, index, p), knob, value, cellW);
        }

        string[] scales = [Loc.T("device.color.major"), Loc.T("device.color.minor"), Loc.T("device.color.major_pentatonic"), Loc.T("device.color.minor_pentatonic"), Loc.T("device.color.custom")];
        Border Drop(int param, string[] items, double width)
        {
            int Cur() => Math.Clamp((int)Math.Round(P(param) * (items.Length - 1)), 0, items.Length - 1);
            var label = new TextBlock { FontSize = 8.5, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var chev = new Glyph(GlyphKind.ChevronDown, 7) { Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
            DockPanel.SetDock(chev, Dock.Right);
            var box = new Border
            {
                Width = width, Height = 18, Padding = new Thickness(6, 0, 5, 0),
                Background = Sunken, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Cell,
                Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
                Child = new DockPanel { Children = { chev, label } },
            };
            MidiLearn.Bind(box, MidiTarget.DeviceParam(track, index, param), e.DeviceParamName(track, index, param));
            refreshers.Add(() => label.Text = items[Cur()]);
            box.PointerPressed += (_, ev) =>
            {
                if (!ev.GetCurrentPoint(box).Properties.IsLeftButtonPressed) return;
                var fly = new MenuFlyout();
                int cur = Cur();
                for (int i = 0; i < items.Length; i++)
                {
                    int iv = i;
                    var mi = new MenuItem { Header = items[i], ToggleType = MenuItemToggleType.Radio, IsChecked = i == cur };
                    mi.Click += (_, _) => { Set(param, iv / (double)(items.Length - 1)); Refresh(); ctx.NotifyChanged(); };
                    fly.Items.Add(mi);
                }
                fly.ShowAt(box);
                ev.Handled = true;
            };
            return box;
        }

        var rootPicker = Drop(Root, Notes, 44);
        var scalePicker = Drop(Scale, scales, 134);
        var notes = new Canvas { Width = 182, Height = 44 };
        int[] white = [0, 2, 4, 5, 7, 9, 11];
        int[] black = [1, 3, 6, 8, 10];
        double[] blackX = [18, 44, 96, 122, 148];
        void AddNote(int note, double x, double width, double height, bool isBlack)
        {
            var cell = Segments([Notes[note]], () => (Mask() & (1 << note)) != 0 ? 0 : -1, _ =>
            {
                int mask = Mask() ^ (1 << note);
                for (int n = 0; n < 12; n++) Set(NoteC + n, (mask >> n) & 1);
                Set(Scale, 1); Refresh(); ctx.NotifyChanged();
            }, out var sync, fill: true, padX: 0, fontSize: 7.5);
            cell.Background = isBlack ? NotaPalette.Groove : NotaPalette.SurfaceRaised;
            cell.Width = width; cell.Height = height;
            cell.VerticalAlignment = VerticalAlignment.Stretch;
            Canvas.SetLeft(cell, x); Canvas.SetTop(cell, 0);
            notes.Children.Add(cell); refreshers.Add(sync);
        }
        for (int i = 0; i < white.Length; i++) AddNote(white[i], i * 26, 25, 44, false);
        for (int i = 0; i < black.Length; i++) AddNote(black[i], blackX[i], 16, 27, true);

        string Percent(double v) => NotaNum.F($"{v * 100:0}\u2009%");
        string Hz(double v, double lo, double hi)
        {
            double hz = ExpHz(v, lo, hi);
            return hz >= 1000 ? NotaNum.F($"{hz / 1000:0.0}\u2009kHz") : NotaNum.F($"{hz:0}\u2009Hz");
        }

        var spectrum = new ChromaSpectrumView();
        spectrum.EdgeGestureBegin += edge => Begin(edge == 0 ? Low : High);
        spectrum.EdgeGestureEnd += edge => End(edge == 0 ? Low : High);
        spectrum.EdgeChanged += (edge, hz) =>
        {
            if (edge == 0) e.DeviceSetParam(track, index, Low, (float)NormHz(hz, 40, 2000));
            else e.DeviceSetParam(track, index, High, (float)NormHz(hz, 1000, 20000));
            Refresh();
        };
        spectrum.EdgeReset += edge =>
        {
            int p = edge == 0 ? Low : High;
            Set(p, e.DeviceParamDefault(track, index, p));
            Refresh();
        };

        var telemetry = new float[1026];
        var latency = new TextBlock
        {
            FontSize = 7.5, Foreground = TextTertiary,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 4, 0, 0), IsHitTestVisible = false,
        };
        refreshers.Add(() =>
        {
            int count = e.DeviceScope(track, index, telemetry, telemetry.Length);
            if (count >= 2 && telemetry[0] > 0) latency.Text = Loc.F("device.color.latency", NotaNum.F($"{telemetry[1] * 1000 / telemetry[0]:0.0}"));
            double lowHz = ExpHz(P(Low), 40, 2000), highHz = ExpHz(P(High), 1000, 20000);
            double ceiling = count >= 2 && telemetry[0] > 0 ? .49 * telemetry[0] : 20000;
            double effectiveLow = Math.Min(ceiling / 2, lowHz);
            double effectiveHigh = Math.Min(ceiling, Math.Max(2 * effectiveLow, highHz));
            spectrum.Update(telemetry, count, lowHz, highHz, effectiveLow, effectiveHigh);
        });

        var select = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { rootPicker, scalePicker } };
        var keyBlock = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Children = { select, notes } };
        var divider = new Border { Width = 1, Background = NotaPalette.GraphBorder, Margin = new Thickness(8, 2) };
        var resonance = KnobFor(Resonance, Percent);
        var decay = KnobFor(Decay, v => NotaNum.F($"{20 * Math.Pow(50, v):0}\u2009ms"));
        var low = KnobFor(Low, v => Hz(v, 40, 2000));
        var high = KnobFor(High, v => Hz(v, 1000, 20000));
        var knobs = new UniformGrid
        {
            Rows = 1, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                KnobFor(Amount, Percent, 36, 48),
                resonance, decay, low, high,
                KnobFor(Output, v => NotaNum.F($"{v * 36 - 24:0.0}\u2009dB")),
                KnobFor(Mix, Percent, 36, 48),
            },
        };
        var gate = Switch(e.DeviceParamName(track, index, Gate), () => P(Gate) >= .5f,
            () => { Set(Gate, P(Gate) >= .5f ? 0 : 1); Refresh(); }, out var syncGate);
        refreshers.Add(syncGate);
        MidiLearn.Bind(gate, MidiTarget.DeviceParam(track, index, Gate), e.DeviceParamName(track, index, Gate));
        ValueEntry.AttachAll(gate, ValueEntry.DeviceParam(e, track, index, Gate,
            e.DeviceParamName(track, index, Gate), Percent, Refresh));
        var spectral = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                KnobFor(Morph, Percent),
                new Border { Child = gate, VerticalAlignment = VerticalAlignment.Center },
                KnobFor(GateThreshold, v => NotaNum.F($"{v * 80 - 80:0.0}\u2009dBFS"), cellW: 84),
            },
        };
        var controlRows = new StackPanel { Spacing = 3, Children = { knobs, spectral } };
        Grid.SetColumn(divider, 1); Grid.SetColumn(controlRows, 2);
        var deck = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"),
            Margin = new Thickness(6, 4, 6, 4),
            Children = { keyBlock, divider, controlRows },
        };
        DockPanel.SetDock(deck, Dock.Bottom);
        var spectrumHost = new Panel { Margin = new Thickness(6, 5, 6, 2), Children = { spectrum, latency } };
        var body = new DockPanel { LastChildFill = true, Children = { deck, spectrumHost } };
        var grid = Section(body);
        ctx.AddDeviceRefresher(Refresh); Refresh();
        return grid;
    }
}

// Live dry and shifted spectral energy, with draggable processing-band limits.
internal sealed class ChromaSpectrumView : Control
{
    private const double MinHz = 40.0, MaxHz = 20000.0;
    private readonly float[] levels = new float[512], tunedLevels = new float[512];
    private double lowHz = 150.0, highHz = 8000.0;
    private int hoverEdge = -1, dragEdge = -1;
    private double effectiveLow = 150.0, effectiveHigh = 8000.0;

    public event Action<int, double>? EdgeChanged;
    public event Action<int>? EdgeGestureBegin;
    public event Action<int>? EdgeGestureEnd;
    public event Action<int>? EdgeReset;

    public ChromaSpectrumView() { ClipToBounds = true; MinHeight = 96; }

    public void Update(float[] source, int count, double low, double high, double bandLow, double bandHigh)
    {
        for (int i = 0; i < levels.Length; i++)
        {
            levels[i] = i + 2 < count ? source[i + 2] : 0;
            tunedLevels[i] = i + 514 < count ? source[i + 514] : 0;
        }
        lowHz = Math.Clamp(low, MinHz, MaxHz);
        highHz = Math.Clamp(high, MinHz, MaxHz);
        effectiveLow = bandLow;
        effectiveHigh = bandHigh;
        InvalidateVisual();
    }

    private (double x0, double x1, double top, double bot) Plot()
        => (6, Math.Max(7, Bounds.Width - 6), NotaGraph.PlotTop, Math.Max(NotaGraph.PlotTop + 10, Bounds.Height - NotaGraph.PlotFloor));

    private double X(double hz)
    {
        var (x0, x1, _, _) = Plot();
        return x0 + Math.Log(Math.Clamp(hz, MinHz, MaxHz) / MinHz) / Math.Log(MaxHz / MinHz) * (x1 - x0);
    }

    private double HzAt(double x)
    {
        var (x0, x1, _, _) = Plot();
        return MinHz * Math.Pow(MaxHz / MinHz, Math.Clamp((x - x0) / Math.Max(1, x1 - x0), 0, 1));
    }

    private int EdgeAt(Point p)
    {
        double dl = Math.Abs(p.X - X(lowHz)), dh = Math.Abs(p.X - X(highHz));
        if (Math.Min(dl, dh) > 6) return -1;
        return dl <= dh ? 0 : 1;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (dragEdge >= 0) { EdgeChanged?.Invoke(dragEdge, HzAt(p.X)); return; }
        int h = EdgeAt(p);
        if (h != hoverEdge)
        {
            hoverEdge = h;
            Cursor = h >= 0 ? new Cursor(StandardCursorType.SizeWestEast) : Cursor.Default;
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (dragEdge < 0 && hoverEdge >= 0) { hoverEdge = -1; Cursor = Cursor.Default; InvalidateVisual(); }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        int edge = EdgeAt(e.GetPosition(this));
        if (edge < 0) return;
        if (e.ClickCount == 2 || (e.KeyModifiers & KeyModifiers.Alt) != 0)
        {
            EdgeReset?.Invoke(edge);
            e.Handled = true;
            return;
        }
        dragEdge = edge;
        EdgeGestureBegin?.Invoke(edge);
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (dragEdge < 0) return;
        int edge = dragEdge;
        dragEdge = -1;
        e.Pointer.Capture(null);
        EdgeGestureEnd?.Invoke(edge);
        InvalidateVisual();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        if (dragEdge >= 0) { int edge = dragEdge; dragEdge = -1; EdgeGestureEnd?.Invoke(edge); InvalidateVisual(); }
        base.OnPointerCaptureLost(e);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var bounds = new Rect(0, 0, w, h);
        NotaGraph.Window(context, bounds);

        var (x0, x1, top, bot) = Plot();
        double plotW = Math.Max(1, x1 - x0), plotH = Math.Max(1, bot - top);
        var inKeyInk = NotaPalette.Accent;
        var outKeyInk = NotaPalette.Steel;

        // Continuous vertical falloff, a narrow core, and a faint lateral shoulder.
        // Intensity remains continuous: there are no horizontal tiles or shade buckets.
        void DrawSpectrum(float[] data, IBrush ink)
        {
            double step = plotW / data.Length;
            using var clip = context.PushClip(new Rect(x0, top, plotW, plotH));
            for (int i = 0; i < data.Length; i++)
            {
                double level = Math.Clamp(data[i], 0, 1);
                if (!double.IsFinite(level) || level <= 0) continue;
                double intensity = level * level;
                double x = x0 + (i + .5) * step;
                double core = Math.Max(.5, step * .65);
                using (context.PushOpacity(intensity * .15))
                    context.DrawRectangle(ink, null, new Rect(x - step * 1.2, top, step * 2.4, plotH));
                using (context.PushOpacity(intensity * .85))
                    context.DrawRectangle(ink, null, new Rect(x - core * .5, top, core, plotH));
            }
        }
        DrawSpectrum(levels, NotaPalette.ChromaDrySpectrum);
        DrawSpectrum(tunedLevels, NotaPalette.ChromaTunedSpectrum);

        // 4. Out-of-band dimming and draggable Low / High cutoff lines.
        double xl = X(effectiveLow), xh = X(effectiveHigh);
        var outsideWash = NotaPalette.Wash(NotaPalette.BgSunken, 0x50);
        if (xl > x0 + 1) context.DrawRectangle(outsideWash, null, new Rect(x0, top, xl - x0, plotH));
        if (xh < x1 - 1) context.DrawRectangle(outsideWash, null, new Rect(xh, top, x1 - xh, plotH));

        for (int edge = 0; edge < 2; edge++)
        {
            bool hot = edge == dragEdge || (dragEdge < 0 && edge == hoverEdge);
            double ex = Math.Round(X(edge == 0 ? lowHz : highHz)) + 0.5;
            var pen = new Pen(
                hot ? inKeyInk : NotaPalette.TextSecondary,
                hot ? 1.6 : 1.0,
                hot ? null : new DashStyle(new double[] { 2, 2 }, 0));
            context.DrawLine(pen, new Point(ex, top), new Point(ex, bot));
        }

        // 5. Graph chrome: title, legend, and frequency axis labels.
        NotaGraph.Title(context, bounds, Loc.T("device.color.spectrum"));
        NotaGraph.Legend(context, w - 6, 4,
            (Loc.T("device.color.dry"), inKeyInk, NotaGraph.Mark.Area),
            (Loc.T("device.color.tuned"), outKeyInk, NotaGraph.Mark.Area));
        NotaGraph.Axis(context, bounds, NotaGraph.Corner.BottomLeft, "40\u2009Hz");
        NotaGraph.Axis(context, bounds, NotaGraph.Corner.BottomRight, "20\u2009k");
        foreach (var (f, txt) in new[] { (100.0, "100"), (1000.0, "1\u2009k"), (10000.0, "10\u2009k") })
        {
            var ft = NotaGraph.AxisText(txt);
            double tx = X(f) - ft.Width * 0.5;
            if (tx > x0 + 28 && tx + ft.Width < x1 - 28)
                context.DrawText(ft, new Point(tx, h - 4 - ft.Height + 1));
        }
    }
}
