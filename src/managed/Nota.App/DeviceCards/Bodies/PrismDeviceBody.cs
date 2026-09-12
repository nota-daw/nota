// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Prism body (device kind 21, three-band dynamics), a build of
// the "Nota Prism" mockup (700 × 260) on the Chamber frame: an always-visible GLOBAL column
// (Amount + Output), a centre tabbed panel (Bands / Band detail / Time), a right tabbed panel
// (Meters / Output) and a status strip. Bands are colour-coded Low = mauve, Mid = brass,
// High = teal. The spectrum's crossover lines, the transfer curve's threshold dots and every
// slider are controls. Every control is a device param, so automation / MIDI learn / presets /
// A-B / persistence come for free. FullBleed — the shared shell draws the header.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed class PrismDeviceBody : IDeviceBody
{
    // ── Parameter indices (must match Prism.h) ───────────────────────────────
    private const int Amount = 0, Output = 1, BandsP = 2, XoverLow = 3, XoverHigh = 4, Detect = 5, Lookahead = 6, AutoMakeup = 7,
        SoftClip = 8, ScListen = 9, Solo = 10, PeakHold = 11, Mix = 12, Global = 13, PerBand = 13;
    private const int AboveThresh = 0, AboveRatio = 1, Attack = 2, Release = 3, Gain = 4, BelowOn = 5, BelowThresh = 6, BelowRatio = 7,
        BelowAttack = 8, BelowRelease = 9, Floor = 10, Knee = 11, AutoRelease = 12;
    private static int BP(int band, int field) => Global + band * PerBand + field;
    // Scope telemetry layout (Prism::S_*).
    private const int S_Gr0 = 0, S_Boost0 = 3, S_Level0 = 6, S_OutL = 9, S_OutR = 10, S_Cpu = 12, S_SampleRate = 13, S_Latency = 14,
        S_ScActive = 15, S_ClipDb = 16, kScope = 20;
    private const int TraceLen = 256;

    private static readonly IBrush RailBg = NotaPalette.SurfaceInset;
    private static readonly IBrush Panel = NotaPalette.BgApp;
    private static readonly IBrush Border2 = NotaPalette.BorderDefault;
    private static readonly IBrush BorderIn = PrismInk.InnerBorder;
    private static readonly IBrush Inset = NotaPalette.BgSunken;
    private static readonly IBrush TabBg = NotaPalette.SurfaceCard;
    private static readonly IBrush Amber = NotaPalette.Accent;
    private static readonly IBrush AmberLit = NotaPalette.AccentBright;
    private static readonly IBrush AmberSubtle = NotaPalette.AccentSubtle;
    private static readonly IBrush RowSel = NotaPalette.Wash(NotaPalette.Accent, 0x10);
    private static readonly IBrush TxtC = NotaPalette.TextPrimary;
    private static readonly IBrush Txt2 = NotaPalette.TextSecondary;
    private static readonly IBrush MutedC = NotaPalette.TextTertiary;
    private static readonly IBrush DimC = NotaPalette.TextDisabled;
    private static readonly IBrush OffPill = NotaPalette.SurfaceRaised;
    private static readonly IBrush Mauve = PrismInk.Band[0];

    // The selected band and tabs survive a card rebuild (keyed by track + device slot).
    private static readonly Dictionary<(int, int), (int Band, int Centre, int Right)> ViewState = new();

    public double Width => 700;
    public bool FullBleed => true;
    public string? Subtitle => "MULTIBAND";

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId, di = index;
        float P(int p) => engine.DeviceGetParam(track, di, p);
        void Begin(int p) => engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void End(int p) => engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void Raw(int p, float v) => engine.DeviceSetParam(track, di, p, Math.Clamp(v, 0f, 1f));
        // A discrete edit (click) is one automation gesture, so Touch / Latch / Write record it.
        void SetP(int p, float v) { Begin(p); Raw(p, v); End(p); }
        bool On(int p) => P(p) >= 0.5f;
        int Sel(int p, int n) => Math.Clamp((int)Math.Round(P(p) * (n - 1)), 0, n - 1);
        float Def(int p) => engine.DeviceParamDefault(track, di, p);

        var readouts = new List<Action>();
        var scope = new float[kScope];
        int scN = 0;
        double Sc(int i) => scN > i ? scope[i] : 0;

        var view = ViewState.TryGetValue((track, di), out var vs) ? vs : (Band: 1, Centre: 0, Right: 0);
        int band = view.Band;
        var bandListeners = new List<Action>();
        void SelectBand(int b)
        {
            if (!BandActive(b)) return;
            band = b; ViewState[(track, di)] = (band, view.Centre, view.Right);
            foreach (var a in bandListeners) a();
            foreach (var a in readouts) a();
        }

        // ---- mappings / formatters ---------------------------------------------------
        int BandCount() => 3 - Sel(BandsP, 3);
        bool BandActive(int b) => b == 1 || (b == 0 && BandCount() >= 2) || (b == 2 && BandCount() >= 3);
        static double Exp(double v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0, 1));
        static double ThrA(double v) => -60 + 60 * v;
        static double ThrB(double v) => -80 + 80 * v;
        static double RatioB(double v) => Math.Pow(4, (v - 0.5) * 2);
        static double XHz(double v) => Exp(v, 20, 20000);
        static string Inv(FormattableString f) => FormattableString.Invariant(f);
        static string DbF(double db) => Math.Abs(db) < 0.05 ? "0.0 dB" : Inv($"{db:+0.0;−0.0} dB");
        static string DbShort(double db) => Math.Abs(db) < 0.05 ? "0.0" : Inv($"{db:+0.0;−0.0}");
        static string ThrF(double db) => Inv($"{db:0;−0} dB");
        static string RatioAF(double v) { if (v >= 0.999) return "∞ : 1"; double r = 1 / (1 - v); return r < 10 ? Inv($"{r:0.0#} : 1") : Inv($"{r:0} : 1"); }
        static string RatioAShort(double v) { if (v >= 0.999) return "∞:1"; double r = 1 / (1 - v); return r < 10 ? Inv($"{r:0.#}:1") : Inv($"{r:0}:1"); }
        static string RatioBF(double v) { double r = RatioB(v); return Math.Abs(r - 1) < 0.01 ? "1 : 1" : r > 1 ? Inv($"{r:0.0#} : 1") : Inv($"↑ {1 / r:0.0#} : 1"); }
        static string RatioBShort(double v) { double r = RatioB(v); return Math.Abs(r - 1) < 0.01 ? "1:1" : r > 1 ? Inv($"{r:0.0#}") : Inv($"↑{1 / r:0.0}"); }
        static string Ms(double ms) => ms >= 1000 ? Inv($"{ms / 1000:0.00} s") : ms >= 10 ? Inv($"{ms:0} ms") : Inv($"{ms:0.0} ms");
        static string AtkF(double v) => Ms(Exp(v, 0.1, 300));
        static string RelF(double v) => Ms(Exp(v, 5, 3000));
        static string GainF(double v) => DbF((v - 0.5) * 48);
        static string HzF(double hz) => hz >= 1000 ? Inv($"{hz / 1000:0.0#} kHz") : Inv($"{hz:0} Hz");
        static string Pct(double v) => Inv($"{v * 100:0} %");
        string FloorF(int b) => RatioB(P(BP(b, BelowRatio))) < 1 ? Inv($"+{P(BP(b, Floor)) * 48:0} dB") : Inv($"−{P(BP(b, Floor)) * 48:0} dB");

        // ---- small builders ---------------------------------------------------------
        static TextBlock Caps(string t, IBrush? c = null, double fs = 7) => new() { Text = t, FontSize = fs, FontWeight = FontWeight.Bold, Foreground = c ?? MutedC, LetterSpacing = 0.8, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock Mono(string t, double fs, IBrush c)
        { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.Mono"); return tb; }
        static Control Docked(Control c, Dock d) { DockPanel.SetDock(c, d); return c; }
        static Control Col(Control c, int col) { Grid.SetColumn(c, col); return c; }
        static Control GRow(Control c, int row) { Grid.SetRow(c, row); return c; }
        static Border Divider(Control child, double top = 5) => new() { BorderBrush = BorderIn, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, top, 0, 0), Child = child };
        Control Learn(Control c, int p) { MidiLearn.Bind(c, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p)); return c; }

        // Gauge knob bound to a device param (automation gesture + MIDI learn + live follow).
        Control K(int p, string name, Func<double, string> fmt, double size = 34, double cellW = 50, IBrush? arc = null)
        {
            var val = Mono(fmt(P(p)), 7, TxtC);
            var knob = new Knob(P(p), 1.0) { Accent = true, ArcColor = arc, Default = Def(p), Width = size, Height = size };
            knob.ValueChanged += v => { Raw(p, (float)v); val.Text = fmt(v); };
            knob.GestureBegin += () => Begin(p);
            knob.GestureEnd += () => End(p);
            Learn(knob, p);
            readouts.Add(() => { if (knob.Dragging || !knob.IsEffectivelyVisible) return; float c = P(p); if (Math.Abs(c - knob.Value) > 1e-4) knob.Value = c; val.Text = fmt(c); });
            return KnobCell(name, knob, val, cellW);
        }

        // On/off pill bound to a param (> 0.5 = on).
        Control Toggle(int p, string label, Func<string>? dynLabel = null)
        {
            var pill = new Border { Width = 18, Height = 10, CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Center };
            var dot = new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(4) };
            var host = new Canvas { Width = 18, Height = 10 }; Canvas.SetTop(dot, 1.5); host.Children.Add(dot); pill.Child = host;
            var txt = new TextBlock { Text = label, FontSize = 8, VerticalAlignment = VerticalAlignment.Center };
            void Hi()
            {
                bool on = On(p);
                pill.Background = on ? Amber : OffPill; dot.Background = on ? Panel : MutedC;
                Canvas.SetLeft(dot, on ? 9.5 : 1.5); txt.Foreground = on ? TxtC : Txt2;
                if (dynLabel != null) txt.Text = dynLabel();
            }
            var wrap = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Children = { pill } };
            if (label.Length > 0 || dynLabel != null) wrap.Children.Add(txt);
            wrap.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(wrap).Properties.IsLeftButtonPressed) return; SetP(p, On(p) ? 0f : 1f); Hi(); e.Handled = true; };
            readouts.Add(Hi); Hi();
            Learn(wrap, p);
            return wrap;
        }

        // Segmented chips over a discrete param (n options spread over 0..1).
        Control Seg(int p, string[] names, Action? changed = null)
        {
            int n = names.Length; var cells = new Border[n];
            void Hi()
            {
                int cur = Sel(p, n);
                for (int i = 0; i < n; i++)
                {
                    bool on = i == cur;
                    cells[i].Background = on ? Amber : Brushes.Transparent;
                    var tb = (TextBlock)cells[i].Child!; tb.Foreground = on ? Panel : MutedC; tb.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                }
            }
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
            for (int i = 0; i < n; i++)
            {
                int iv = i;
                var c = new Border { CornerRadius = new CornerRadius(2), Padding = new Thickness(4, 0), Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = names[i], FontSize = 7, Foreground = MutedC } };
                c.PointerPressed += (_, e) => { SetP(p, n > 1 ? iv / (float)(n - 1) : 0f); Hi(); changed?.Invoke(); e.Handled = true; };
                cells[i] = c; row.Children.Add(c);
            }
            readouts.Add(Hi); Hi();
            var seg = new Border { Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Child = row };
            Learn(seg, p);
            return seg;
        }

        // Horizontal bar slider over a param, with a mono readout on the right.
        Control Bar(int p, Func<double, string> fmt, Func<IBrush> fillC, Func<bool>? dim = null, double valW = 30, Func<IBrush>? valC = null)
        {
            var fill = new Border { Height = 3, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var handle = new Border { Width = 6, Height = 7, CornerRadius = new CornerRadius(2) };
            var lay = new Canvas { Height = 9 };
            lay.Children.Add(handle); Canvas.SetTop(handle, 1);
            var canvas = new Avalonia.Controls.Panel { Height = 11, MinWidth = 20, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Children = {
                new Border { Height = 3, Background = Inset, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center }, fill, lay } };
            var val = Mono("", 8, TxtC); val.Width = valW; val.TextAlignment = TextAlignment.Right; val.TextTrimming = TextTrimming.None;
            bool drag = false;
            void Vis()
            {
                double v = Math.Clamp(P(p), 0, 1), w = canvas.Bounds.Width; if (w <= 0) w = 60;
                fill.Width = Math.Max(0, v * w); Canvas.SetLeft(handle, v * w - 3); val.Text = fmt(v);
                bool d = dim?.Invoke() ?? false;
                fill.Background = d ? NotaPalette.BorderStrong : fillC(); handle.Background = d ? MutedC : Txt2;
                val.Foreground = d ? MutedC : valC?.Invoke() ?? TxtC;
            }
            void From(PointerEventArgs e) { double w = canvas.Bounds.Width; Raw(p, (float)(w > 0 ? Math.Clamp(e.GetPosition(canvas).X / w, 0, 1) : 0)); Vis(); }
            canvas.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed) return;
                if (e.ClickCount == 2) { SetP(p, Def(p)); Vis(); e.Handled = true; return; }
                drag = true; Begin(p); e.Pointer.Capture(canvas); From(e); e.Handled = true;
            };
            canvas.PointerMoved += (_, e) => { if (drag) From(e); };
            canvas.PointerReleased += (_, e) => { if (drag) { drag = false; End(p); e.Pointer.Capture(null); } };
            canvas.SizeChanged += (_, _) => Vis();
            readouts.Add(() => { if (!drag && canvas.IsEffectivelyVisible) Vis(); });
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 5, VerticalAlignment = VerticalAlignment.Center };
            grid.Children.Add(canvas); grid.Children.Add(Col(val, 1));
            Learn(grid, p);
            return grid;
        }
        Control SliderRow(string label, int p, Func<double, string> fmt, double labW = 50, double valW = 30)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions(Inv($"{labW},*")), VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(Caps(label)); g.Children.Add(Col(Bar(p, fmt, () => Amber, null, valW), 1));
            return g;
        }

        // A mono readout that edits its param by vertical drag (~140 px full range); double-click resets.
        Control DragVal(int p, Func<double, string> fmt, Func<IBrush> color)
        {
            var tb = Mono("", 8, TxtC); tb.TextAlignment = TextAlignment.Right; tb.HorizontalAlignment = HorizontalAlignment.Stretch;
            var host = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.SizeNorthSouth), Child = tb };
            bool drag = false; double y0 = 0, v0 = 0;
            host.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(host).Properties.IsLeftButtonPressed) return;
                if (e.ClickCount == 2) { SetP(p, Def(p)); e.Handled = true; return; }
                drag = true; y0 = e.GetPosition(host).Y; v0 = P(p); Begin(p); e.Pointer.Capture(host); e.Handled = true;
            };
            host.PointerMoved += (_, e) => { if (drag) { Raw(p, (float)(v0 + (y0 - e.GetPosition(host).Y) / 140.0)); tb.Text = fmt(P(p)); } };
            host.PointerReleased += (_, e) => { if (drag) { drag = false; End(p); e.Pointer.Capture(null); } };
            readouts.Add(() => { tb.Text = fmt(P(p)); tb.Foreground = color(); });
            Learn(host, p);
            return host;
        }

        // Band name cell: colour bar + name (lit when selected).
        Control BandName(int b, double barH)
        {
            var name = new TextBlock { Text = PrismInk.Names[b], FontSize = 9, VerticalAlignment = VerticalAlignment.Center };
            readouts.Add(() => { bool s = band == b; name.Foreground = s ? PrismInk.BandLit[b] : TxtC; name.FontWeight = s ? FontWeight.SemiBold : FontWeight.Normal; });
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = {
                new Border { Width = 3, Height = barH, CornerRadius = new CornerRadius(2), Background = PrismInk.Band[b] }, name } };
        }

        // A clickable table row for band b: selects the band; dimmed + inert when the band is off.
        Border BandRow(int b, Grid content, double height)
        {
            content.Height = height;
            var row = new Border { BorderBrush = BorderIn, BorderThickness = new Thickness(0, 1, 0, 0), Background = Brushes.Transparent, Child = content };
            row.PointerPressed += (_, e) => { if (e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) SelectBand(b); };
            readouts.Add(() =>
            {
                bool act = BandActive(b);
                row.Background = band == b && act ? RowSel : Brushes.Transparent;
                row.Opacity = act ? 1 : 0.32; row.IsHitTestVisible = act;
            });
            return row;
        }

        // ======================================================================
        // LEFT — GLOBAL column (Amount + Output)
        // ======================================================================
        Control Fader(int p, string label, string tip)
        {
            var f = new ChamberFader { VerticalAlignment = VerticalAlignment.Stretch, Default = Def(p) };
            f.Changed += v => Raw(p, (float)v);
            f.DragStarted += () => Begin(p);
            f.DragEnded += () => End(p);
            readouts.Add(() => f.Set(P(p)));
            Learn(f, p);
            ToolTip.SetTip(f, tip);
            var lb = Caps(label); lb.HorizontalAlignment = HorizontalAlignment.Center;
            return new DockPanel { Children = { Docked(lb, Dock.Bottom), f } };
        }
        var amtTxt = Mono("", 7, AmberLit); amtTxt.HorizontalAlignment = HorizontalAlignment.Center;
        var outTxt = Mono("", 7, TxtC); outTxt.HorizontalAlignment = HorizontalAlignment.Center;
        readouts.Add(() => { amtTxt.Text = Pct(P(Amount)); outTxt.Text = DbShort((P(Output) - 0.5) * 48); });
        var globTitle = Caps("GLOBAL"); globTitle.HorizontalAlignment = HorizontalAlignment.Center;
        var faders = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), ColumnSpacing = 9, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 3) };
        faders.Children.Add(Fader(Amount, "AMT", "Amount — scales every band's compression and expansion (0 % = none). Double-click: 100 %"));
        faders.Children.Add(Col(Fader(Output, "OUT", "Output gain ±24 dB — double-click: 0 dB"), 1));
        var globCol = new Border { Width = 56, Background = Panel, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(0, 5, 0, 4),
            Child = new DockPanel { Children = { Docked(globTitle, Dock.Top), Docked(outTxt, Dock.Bottom), Docked(amtTxt, Dock.Bottom), faders } } };
        DockPanel.SetDock(globCol, Dock.Left);

        // ======================================================================
        // CENTRE — Bands (spectrum + per-band overview)
        // ======================================================================
        Control BandsTab()
        {
            var spec = new PrismSpectrum(engine, track, di) { DefaultLow = Def(XoverLow), DefaultHigh = Def(XoverHigh) };
            spec.CrossoverChanged += (h, v) => Raw(h == 0 ? XoverLow : XoverHigh, (float)v);
            spec.DragStarted += h => Begin(h == 0 ? XoverLow : XoverHigh);
            spec.DragEnded += h => End(h == 0 ? XoverLow : XoverHigh);
            spec.BandClicked += SelectBand;
            ToolTip.SetTip(spec, "Input spectrum (fill) and output (line). Drag a crossover line to move it; double-click resets. Click a band to select it.");
            readouts.Add(() => { spec.Set(P(XoverLow), P(XoverHigh), BandCount(), band, Sc(S_SampleRate)); spec.Tick(); });

            const string cols = "50,*,*,40,40";
            var head = new Grid { ColumnDefinitions = new ColumnDefinitions(cols), Height = 11 };
            var hA = Caps("ABOVE — COMPRESS", DimC); hA.HorizontalAlignment = HorizontalAlignment.Center;
            var hB = Caps("BELOW — EXPAND", DimC); hB.HorizontalAlignment = HorizontalAlignment.Center;
            var hG = Caps("GAIN", DimC); hG.HorizontalAlignment = HorizontalAlignment.Right;
            var hR = Caps("GR", DimC); hR.HorizontalAlignment = HorizontalAlignment.Right;
            head.Children.Add(Caps("BAND", DimC)); head.Children.Add(Col(hA, 1)); head.Children.Add(Col(hB, 2)); head.Children.Add(Col(hG, 3)); head.Children.Add(Col(hR, 4));
            var rows = new StackPanel { Children = { head } };
            for (int b = 0; b < 3; b++)
            {
                int bb = b;
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions(cols) };
                g.Children.Add(BandName(bb, 12));
                var above = Bar(BP(bb, AboveThresh), v => Inv($"{ThrA(v):0;−0} · {RatioAShort(P(BP(bb, AboveRatio)))}"), () => PrismInk.Band[bb], null, 52,
                                () => band == bb ? PrismInk.BandLit[bb] : TxtC);
                above.Margin = new Thickness(0, 0, 8, 0);
                ToolTip.SetTip(above, "Compression threshold · ratio (set the ratio in Band detail)");
                g.Children.Add(Col(above, 1));
                bool BelowOff() => !On(BP(bb, BelowOn));
                var below = Bar(BP(bb, BelowThresh), v => BelowOff() ? "off" : Inv($"{ThrB(v):0;−0} · {RatioBShort(P(BP(bb, BelowRatio)))}"), () => PrismInk.Band[bb], BelowOff, 52);
                below.Margin = new Thickness(0, 0, 8, 0);
                // The readout toggles the below section on / off.
                var belowVal = (TextBlock)((Grid)below).Children[1];
                belowVal.Cursor = new Cursor(StandardCursorType.Hand);
                belowVal.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(belowVal).Properties.IsLeftButtonPressed) return; SetP(BP(bb, BelowOn), BelowOff() ? 1f : 0f); e.Handled = true; };
                ToolTip.SetTip(below, "Expansion / upward-compression threshold · ratio — click the readout to switch it on or off");
                g.Children.Add(Col(below, 2));
                var gain = DragVal(BP(bb, Gain), v => DbShort((v - 0.5) * 48), () => Math.Abs(P(BP(bb, Gain)) - 0.5f) < 0.001f ? Txt2 : TxtC);
                ToolTip.SetTip(gain, "Band gain ±24 dB — drag up / down, double-click resets");
                g.Children.Add(Col(gain, 3));
                var grTrack = new Border { Width = 32, Height = 4, CornerRadius = new CornerRadius(2), Background = Inset, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
                var grFill = new Border { Background = PrismInk.Band[bb], HorizontalAlignment = HorizontalAlignment.Right };
                grTrack.Child = grFill;
                readouts.Add(() => grFill.Width = 32 * Math.Clamp(Sc(S_Gr0 + bb) / 12, 0, 1));
                g.Children.Add(Col(grTrack, 4));
                rows.Children.Add(BandRow(bb, g, 22));
            }
            rows.Height = 88;
            return new DockPanel { LastChildFill = true, Margin = new Thickness(8, 5, 8, 3), Children = {
                Docked(rows, Dock.Bottom),
                new Border { Margin = new Thickness(0, 0, 0, 4), Child = spec } } };
        }

        // ======================================================================
        // CENTRE — Band detail (transfer curve + above / below knobs), one panel per band
        // ======================================================================
        Control DetailPanel(int b)
        {
            var tr = new PrismTransfer();
            int dragThr = 0, dragRat = 0; float thr0 = 0, rat0 = 0;
            tr.DragStarted += h =>
            {
                dragThr = BP(b, h == 1 ? AboveThresh : BelowThresh); dragRat = BP(b, h == 1 ? AboveRatio : BelowRatio);
                thr0 = P(dragThr); rat0 = P(dragRat); Begin(dragThr); Begin(dragRat);
                if (h == 2 && !On(BP(b, BelowOn))) SetP(BP(b, BelowOn), 1f);   // grabbing the below dot switches it on
            };
            tr.Dragged += (h, dx, dy) =>
            {
                double spanDb = h == 1 ? 60 : 80;   // param span of that threshold
                Raw(dragThr, (float)(thr0 + dx * 72 / spanDb));
                Raw(dragRat, (float)(rat0 + dy / (h == 1 ? 160.0 : 240.0)));   // drag down = more processing
                foreach (var a in readouts) a();
            };
            tr.DragEnded += _ => { End(dragThr); End(dragRat); };
            tr.ResetRequested += h =>
            {
                int t = BP(b, h == 1 ? AboveThresh : BelowThresh), r = BP(b, h == 1 ? AboveRatio : BelowRatio);
                SetP(t, Def(t)); SetP(r, Def(r));
            };
            ToolTip.SetTip(tr, "Drag a dot: ← → threshold, ↑ ↓ ratio (down = more). The bright dot is compression above, the grey one expansion below. Double-click a dot resets it.");
            readouts.Add(() =>
            {
                if (!tr.IsEffectivelyVisible) return;
                double amt = P(Amount);
                tr.Set(b, ThrA(P(BP(b, AboveThresh))), P(BP(b, AboveRatio)) * amt, On(BP(b, BelowOn)), ThrB(P(BP(b, BelowThresh))),
                       (RatioB(P(BP(b, BelowRatio))) - 1) * amt, P(BP(b, Floor)) * 48, P(BP(b, Knee)) * 24, Sc(S_Level0 + b));
            });
            var trCap = Caps("TRANSFER · " + PrismInk.Names[b].ToUpperInvariant());
            var kneeRow = new Grid { ColumnDefinitions = new ColumnDefinitions("32,*"), Margin = new Thickness(0, 4, 0, 0) };
            kneeRow.Children.Add(Caps("KNEE"));
            kneeRow.Children.Add(Col(Bar(BP(b, Knee), v => Inv($"{v * 24:0.#} dB"), () => PrismInk.Band[b], null, 32), 1));
            var left = new Border { Width = 150, BorderBrush = BorderIn, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(7, 5),
                Child = new DockPanel { Children = { Docked(trCap, Dock.Top), Docked(kneeRow, Dock.Bottom), new Border { Margin = new Thickness(0, 4, 0, 0), Child = tr } } } };

            // ABOVE — compression
            var aCap = Caps("ABOVE — COMPRESSION", PrismInk.BandLit[b]);
            var aGr = Mono("", 7, Txt2); aGr.HorizontalAlignment = HorizontalAlignment.Right;
            readouts.Add(() => { double gr = Sc(S_Gr0 + b); aGr.Text = gr > 0.05 ? Inv($"GR −{gr:0.0} dB") : "GR 0.0 dB"; });
            var aHead = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Height = 11 };
            aHead.Children.Add(aCap); aHead.Children.Add(Col(aGr, 1));
            var aKnobs = new UniformGrid { Rows = 1, VerticalAlignment = VerticalAlignment.Center };
            aKnobs.Children.Add(K(BP(b, AboveThresh), "THRESH", v => ThrF(ThrA(v)), 36, 52));
            aKnobs.Children.Add(K(BP(b, AboveRatio), "RATIO", RatioAF, 36, 52));
            aKnobs.Children.Add(K(BP(b, Attack), "ATTACK", AtkF, 36, 52));
            aKnobs.Children.Add(K(BP(b, Release), "RELEASE", RelF, 36, 52));
            aKnobs.Children.Add(K(BP(b, Gain), "GAIN", GainF, 36, 52));
            var aSec = new DockPanel { Children = { Docked(aHead, Dock.Top), aKnobs } };

            // BELOW — expansion / upward compression
            var bCap = Caps("BELOW — EXPANSION");
            readouts.Add(() => { bool up = RatioB(P(BP(b, BelowRatio))) < 0.99; bCap.Text = up ? "BELOW — UPWARD COMPRESSION" : "BELOW — EXPANSION"; bCap.Foreground = On(BP(b, BelowOn)) ? Txt2 : MutedC; });
            var bToggle = Toggle(BP(b, BelowOn), "", () => On(BP(b, BelowOn)) ? "on" : "off");
            var bHead = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Height = 11 };
            bHead.Children.Add(bCap); bHead.Children.Add(Col(bToggle, 1));
            var bKnobs = new UniformGrid { Rows = 1, VerticalAlignment = VerticalAlignment.Center };
            bKnobs.Children.Add(K(BP(b, BelowThresh), "THRESH", v => ThrF(ThrB(v)), 36, 52, Mauve));
            bKnobs.Children.Add(K(BP(b, BelowRatio), "RATIO", RatioBF, 36, 52, Mauve));
            bKnobs.Children.Add(K(BP(b, BelowAttack), "ATTACK", AtkF, 36, 52, Mauve));
            bKnobs.Children.Add(K(BP(b, BelowRelease), "RELEASE", RelF, 36, 52, Mauve));
            bKnobs.Children.Add(K(BP(b, Floor), "FLOOR", _ => FloorF(b), 36, 52, Mauve));
            readouts.Add(() => bKnobs.Opacity = On(BP(b, BelowOn)) ? 1 : 0.45);
            ToolTip.SetTip(bKnobs, "Ratio above 1 : 1 expands (quiet parts get quieter); below 1 : 1 (↑) compresses upward (quiet parts are lifted). Floor limits either to ± that many dB.");
            var bSec = Divider(new DockPanel { Children = { Docked(bHead, Dock.Top), bKnobs } }, 4);

            var right = new Grid { RowDefinitions = new RowDefinitions("*,*"), Margin = new Thickness(7, 5, 7, 3) };
            right.Children.Add(aSec); right.Children.Add(GRow(bSec, 1));
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            g.Children.Add(left); g.Children.Add(Col(right, 1));
            return g;
        }
        Control DetailTab()
        {
            var host = new Avalonia.Controls.Panel();
            var panels = new Control[3];
            for (int b = 0; b < 3; b++) { panels[b] = DetailPanel(b); host.Children.Add(panels[b]); }
            void Show() { for (int b = 0; b < 3; b++) panels[b].IsVisible = b == band; }
            bandListeners.Add(Show); Show();
            return host;
        }

        // ======================================================================
        // CENTRE — Time (detector trace + per-band attack / release / auto release)
        // ======================================================================
        Control TimeTab()
        {
            var trace = new PrismEnvTrace();
            var tIn = new float[TraceLen]; var tDet = new float[TraceLen];
            readouts.Add(() =>
            {
                if (!trace.IsEffectivelyVisible) return;
                int n = engine.DeviceLayerWave(track, di, 2 + band * 2, tIn, TraceLen);
                int m = engine.DeviceLayerWave(track, di, 3 + band * 2, tDet, TraceLen);
                trace.Set(tIn, tDet, Math.Min(n, m), band, ThrA(P(BP(band, AboveThresh))), On(BP(band, BelowOn)), ThrB(P(BP(band, BelowThresh))));
            });
            ToolTip.SetTip(trace, "The selected band's input peaks (grey) and what its compressor's detector makes of them (the last ~1.5 s), against the threshold.");
            const string cols = "50,*,*,40";
            var head = new Grid { ColumnDefinitions = new ColumnDefinitions(cols), Height = 11 };
            var hA = Caps("ATTACK", DimC); hA.HorizontalAlignment = HorizontalAlignment.Center;
            var hR = Caps("RELEASE", DimC); hR.HorizontalAlignment = HorizontalAlignment.Center;
            var hU = Caps("AUTO", DimC); hU.HorizontalAlignment = HorizontalAlignment.Right;
            head.Children.Add(Caps("BAND", DimC)); head.Children.Add(Col(hA, 1)); head.Children.Add(Col(hR, 2)); head.Children.Add(Col(hU, 3));
            var rows = new StackPanel { Children = { head } };
            for (int b = 0; b < 3; b++)
            {
                int bb = b;
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions(cols) };
                g.Children.Add(BandName(bb, 11));
                IBrush Lit() => band == bb ? PrismInk.BandLit[bb] : TxtC;
                var atk = Bar(BP(bb, Attack), AtkF, () => PrismInk.Band[bb], null, 38, Lit); atk.Margin = new Thickness(0, 0, 8, 0);
                var rel = Bar(BP(bb, Release), RelF, () => PrismInk.Band[bb], null, 38, Lit); rel.Margin = new Thickness(0, 0, 8, 0);
                g.Children.Add(Col(atk, 1)); g.Children.Add(Col(rel, 2));
                var auto = Toggle(BP(bb, AutoRelease), ""); auto.HorizontalAlignment = HorizontalAlignment.Right;
                ToolTip.SetTip(auto, "Auto release — short peaks recover fast, sustained compression recovers slowly");
                g.Children.Add(Col(auto, 3));
                rows.Children.Add(BandRow(bb, g, 18));
            }
            rows.Height = 74;
            return new DockPanel { LastChildFill = true, Margin = new Thickness(8, 5, 8, 3), Children = {
                Docked(rows, Dock.Bottom),
                new Border { Margin = new Thickness(0, 0, 0, 4), Child = trace } } };
        }

        // ======================================================================
        // RIGHT — Meters / Output
        // ======================================================================
        Control MetersTab()
        {
            var meters = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,Auto"), Margin = new Thickness(0, 4, 0, 0) };
            for (int b = 0; b < 3; b++)
            {
                int bb = b;
                var m = new PrismGrMeter { Band = bb, HorizontalAlignment = HorizontalAlignment.Center };
                var val = Mono("", 7, PrismInk.Band[bb]); val.HorizontalAlignment = HorizontalAlignment.Center;
                var nm = Caps(PrismInk.Names[bb].ToUpperInvariant()); nm.HorizontalAlignment = HorizontalAlignment.Center;
                readouts.Add(() =>
                {
                    bool act = BandActive(bb);
                    m.Soloed = act && Sel(Solo, 4) == bb + 1;
                    m.Set(act ? Sc(S_Gr0 + bb) : 0, act ? Sc(S_Boost0 + bb) : 0, On(PeakHold));
                    double gr = Sc(S_Gr0 + bb), bo = Sc(S_Boost0 + bb);
                    val.Text = !act ? "—" : bo > gr + 0.05 ? Inv($"+{bo:0.0}") : gr > 0.05 ? Inv($"−{gr:0.0}") : "0.0";
                    val.Foreground = band == bb ? PrismInk.BandLit[bb] : PrismInk.Band[bb];
                    nm.Foreground = band == bb ? PrismInk.BandLit[bb] : MutedC;
                    m.Opacity = act ? 1 : 0.3;
                });
                var col = new DockPanel { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Children = { Docked(nm, Dock.Bottom), Docked(val, Dock.Bottom), m } };
                ((Control)val).Margin = new Thickness(0, 3, 0, 1);
                col.PointerPressed += (_, e) => { if (e.GetCurrentPoint(col).Properties.IsLeftButtonPressed) SelectBand(bb); };
                meters.Children.Add(Col(col, b));
            }
            var scale = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,*,Auto,Auto"), Margin = new Thickness(2, 0, 0, 0),
                Children = { Mono("0", 7, DimC), GRow(Mono("−6", 7, DimC), 2), GRow(Mono("−12", 7, DimC), 4), GRow(new Border { Height = 25 }, 5) } };
            meters.Children.Add(Col(scale, 3));

            // Solo L / M / H (one band at a time; click the lit one to clear).
            var soloCells = new Border[3];
            var soloTxt = Mono("", 7, MutedC); soloTxt.HorizontalAlignment = HorizontalAlignment.Right;
            void HiSolo()
            {
                int s = Sel(Solo, 4) - 1;
                for (int i = 0; i < 3; i++)
                {
                    bool on = s == i, act = BandActive(i);
                    soloCells[i].Background = on ? AmberSubtle : OffPill; soloCells[i].BorderBrush = on ? Amber : Brushes.Transparent;
                    var t = (TextBlock)soloCells[i].Child!; t.Foreground = on ? AmberLit : act ? Txt2 : DimC; t.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                }
                soloTxt.Text = s >= 0 && BandActive(s) ? PrismInk.Names[s].ToLowerInvariant() : "none"; soloTxt.Foreground = s >= 0 ? AmberLit : MutedC;
            }
            var soloRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
            string[] sl = { "L", "M", "H" };
            for (int i = 0; i < 3; i++)
            {
                int iv = i;
                var c = new Border { Height = 15, Padding = new Thickness(6, 0), CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand),
                    Child = new TextBlock { Text = sl[i], FontSize = 8, VerticalAlignment = VerticalAlignment.Center } };
                c.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(c).Properties.IsLeftButtonPressed || !BandActive(iv)) return;
                    SetP(Solo, Sel(Solo, 4) == iv + 1 ? 0f : (iv + 1) / 3f); HiSolo(); e.Handled = true;
                };
                soloCells[i] = c; soloRow.Children.Add(c);
            }
            Learn(soloRow, Solo);
            ToolTip.SetTip(soloRow, "Solo a band — hear only that band (click again to clear)");
            readouts.Add(HiSolo); HiSolo();
            var soloGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 6 };
            soloGrid.Children.Add(Caps("SOLO")); soloGrid.Children.Add(Col(soloRow, 1)); soloGrid.Children.Add(Col(soloTxt, 2));
            var bottom = Divider(new StackPanel { Spacing = 5, Children = { soloGrid, Toggle(PeakHold, "Peak hold") } });
            bottom.Margin = new Thickness(0, 5, 0, 0);
            return new DockPanel { Margin = new Thickness(8, 6, 8, 5), Children = { Docked(Caps("GAIN REDUCTION"), Dock.Top), Docked(bottom, Dock.Bottom), meters } };
        }
        Control OutputTab()
        {
            var meterL = new PentadLevelBar { Vertical = true, Width = 11, Height = 50 };
            var meterR = new PentadLevelBar { Vertical = true, Width = 11, Height = 50 };
            readouts.Add(() => { meterL.SetLinear(Sc(S_OutL)); meterR.SetLinear(Sc(S_OutR)); });
            static Control Center(Control c) { c.HorizontalAlignment = HorizontalAlignment.Center; return c; }
            var scale = new Grid { RowDefinitions = new RowDefinitions("*,*,*"), Height = 50, Children = { Mono("0", 7, DimC), GRow(Mono("−12", 7, DimC), 1), GRow(Mono("−48", 7, DimC), 2) } };
            var meters = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Top, Children = {
                new StackPanel { Spacing = 2, Children = { meterL, Center(Caps("L")) } },
                new StackPanel { Spacing = 2, Children = { meterR, Center(Caps("R")) } }, scale } };
            var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { K(Output, "OUTPUT", v => DbF((v - 0.5) * 48), 38, 54), meters } };

            // Sidechain source (None + every other track).
            var scName = new TextBlock { FontSize = 8, Foreground = TxtC, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            bool scOk = engine.DeviceAcceptsSidechain(track, di);
            string SrcName(int id)
            {
                for (int i = 0; i < engine.TrackCount; i++)
                    if (engine.TryGetTrackInfo(i, out var ti) && ti.Id == id)
                    { string n = engine.GetTrackName(id); return n.Length > 0 ? n : Inv($"Track {i + 1}"); }
                return "None";
            }
            readouts.Add(() =>
            {
                int src = scOk ? engine.DeviceSidechainSource(track, di) : -1;
                scName.Text = !scOk ? "n/a in a rack" : src < 0 ? "None — own input" : SrcName(src);
                scName.Foreground = src >= 0 ? TxtC : Txt2;
            });
            var scBox = new Border { Height = 16, Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 0),
                Cursor = new Cursor(StandardCursorType.Hand), IsEnabled = scOk,
                Child = new DockPanel { Children = { Docked(new TextBlock { Text = "▾", FontSize = 8, Foreground = MutedC, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0) }, Dock.Right), scName } } };
            ToolTip.SetTip(scBox, "Sidechain — key every band's detector from another track (split into the same bands)");
            scBox.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(scBox).Properties.IsLeftButtonPressed || !scOk) return;
                var fly = new MenuFlyout();
                int cur = engine.DeviceSidechainSource(track, di);
                void Item(string name, int id)
                {
                    var mi = new MenuItem { Header = name };
                    if (id == cur) mi.Icon = new Avalonia.Controls.Shapes.Ellipse { Width = 6, Height = 6, Fill = Amber };
                    mi.Click += (_, _) => { engine.SetDeviceSidechainSource(track, di, id); ctx.NotifyChanged(); foreach (var a in readouts) a(); };
                    fly.Items.Add(mi);
                }
                Item("None — own input", -1);
                fly.Items.Add(new Separator());
                for (int i = 0; i < engine.TrackCount; i++)
                    if (engine.TryGetTrackInfo(i, out var ti) && ti.Id != track) Item(SrcName(ti.Id), ti.Id);
                fly.ShowAt(scBox);
                e.Handled = true;
            };
            var scRow = new Grid { ColumnDefinitions = new ColumnDefinitions("50,*"), VerticalAlignment = VerticalAlignment.Center };
            scRow.Children.Add(Caps("SIDECHAIN")); scRow.Children.Add(Col(scBox, 1));
            var listen = Toggle(ScListen, "Listen");
            ToolTip.SetTip(listen, "Listen — hear the detector's key signal (the sidechain, or the input), per solo band");
            var mk = Toggle(AutoMakeup, "Auto makeup");
            ToolTip.SetTip(mk, "Auto makeup — each band gets back about half of its compression at 0 dBFS");
            var toggles = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
            toggles.Children.Add(listen); toggles.Children.Add(Col(mk, 1));
            var mid = new StackPanel { Spacing = 5, Children = {
                SliderRow("LOOKAHEAD", Lookahead, v => Inv($"{v * 10:0.0} ms")),
                SliderRow("MIX", Mix, Pct), scRow, toggles } };

            var clipTxt = Mono("", 7, TxtC); clipTxt.HorizontalAlignment = HorizontalAlignment.Right;
            readouts.Add(() =>
            {
                double c = Sc(S_ClipDb);
                clipTxt.Text = On(SoftClip) && c > 0.05 ? Inv($"−0.3 dBFS · {c:0.0}") : "−0.3 dBFS";
                clipTxt.Foreground = On(SoftClip) ? (c > 0.05 ? NotaPalette.Warning : TxtC) : MutedC;
            });
            var clipRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 6 };
            var clipSeg = Seg(SoftClip, new[] { "Off", "On" });
            ToolTip.SetTip(clipSeg, "Soft clip — a smooth ceiling at −0.3 dBFS after the output gain");
            clipRow.Children.Add(Caps("SOFT CLIP")); clipRow.Children.Add(Col(clipSeg, 1)); clipRow.Children.Add(Col(clipTxt, 2));
            var midB = Divider(mid); midB.Margin = new Thickness(0, 4, 0, 0);
            return new DockPanel { LastChildFill = false, Margin = new Thickness(8, 5, 8, 5), Children = {
                Docked(top, Dock.Top), Docked(midB, Dock.Top), Docked(Divider(clipRow), Dock.Bottom) } };
        }

        // ======================================================================
        // Tab frames
        // ======================================================================
        var centreHost = new ContentControl(); var rightHost = new ContentControl();
        var centreBodies = new Control?[3]; var rightBodies = new Control?[2];
        int centreTab = view.Centre;
        var extras = new ContentControl { VerticalAlignment = VerticalAlignment.Center };
        Control ModeExtras()
        {
            var seg = Seg(BandsP, new[] { "3", "2", "1" }, () => { if (!BandActive(band)) SelectBand(1); foreach (var a in readouts) a(); });
            ToolTip.SetTip(seg, "Bands — 3: Low / Mid / High · 2: Low + Mid (Mid runs to 20 kHz) · 1: Mid, full band");
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { Caps("MODE"), seg } };
        }
        Control BandChips()
        {
            var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            var cells = new Border[3];
            void Hi()
            {
                for (int i = 0; i < 3; i++)
                {
                    bool on = i == band, act = BandActive(i);
                    cells[i].BorderBrush = on ? PrismInk.Band[i] : NotaPalette.BorderStrong;
                    cells[i].Background = on ? PrismInk.Alpha(PrismInk.BandColor[i], 0x29) : Brushes.Transparent;
                    var t = (TextBlock)cells[i].Child!; t.Foreground = on ? PrismInk.BandLit[i] : act ? Txt2 : DimC; t.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                }
            }
            for (int i = 0; i < 3; i++)
            {
                int iv = i;
                var c = new Border { Height = 15, Padding = new Thickness(6, 0), CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand),
                    Child = new TextBlock { Text = PrismInk.Names[i], FontSize = 8, VerticalAlignment = VerticalAlignment.Center } };
                c.PointerPressed += (_, e) => { SelectBand(iv); e.Handled = true; };
                cells[i] = c; chips.Children.Add(c);
            }
            readouts.Add(Hi); Hi();
            return chips;
        }
        Control DetectExtras()
        {
            var seg = Seg(Detect, new[] { "Peak", "RMS" });
            ToolTip.SetTip(seg, "Detector — Peak follows every transient; RMS (10 ms) follows loudness, smoother");
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { Caps("DETECT"), seg } };
        }
        var extrasBodies = new Control?[3];
        void SetExtras(int t) => extras.Content = extrasBodies[t] ??= t switch { 1 => BandChips(), 2 => DetectExtras(), _ => ModeExtras() };
        Control CentreBody(int t) => centreBodies[t] ??= t switch { 1 => DetailTab(), 2 => TimeTab(), _ => BandsTab() };
        Control RightBody(int t) => rightBodies[t] ??= t == 1 ? OutputTab() : MetersTab();
        SetExtras(centreTab);
        var centre = TabFrame(new[] { "Bands", "Band detail", "Time" }, centreTab, centreHost, CentreBody, false, t =>
        {
            centreTab = t; view = (band, t, view.Right); ViewState[(track, di)] = view; SetExtras(t); foreach (var a in readouts) a();
        }, extras);
        var rightFrame = TabFrame(new[] { "Meters", "Output" }, view.Right, rightHost, RightBody, true, t =>
        {
            view = (band, view.Centre, t); ViewState[(track, di)] = view; foreach (var a in readouts) a();
        }, null);
        var right = new Border { Width = 186, Background = Panel, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), ClipToBounds = true, Child = rightFrame };
        DockPanel.SetDock(right, Dock.Right);
        var centreBox = new Border { Background = Panel, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Margin = new Thickness(5, 0), ClipToBounds = true, Child = centre };

        // ======================================================================
        // Status strip
        // ======================================================================
        var statusLeft = new TextBlock { FontSize = 8, Foreground = Txt2, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var statusRight = Mono("", 8, Txt2);
        string StatusText()
        {
            int nb = BandCount();
            int solo = Sel(Solo, 4) - 1;
            if (On(ScListen)) return "Listening to the detector key" + (solo >= 0 ? Inv($" · {PrismInk.Names[solo]} band") : "");
            switch (centreTab)
            {
                case 1:
                {
                    int b = band;
                    string head = solo == b ? Inv($"{PrismInk.Names[b]} solo") : PrismInk.Names[b];
                    string below = On(BP(b, BelowOn)) ? Inv($"below {ThrB(P(BP(b, BelowThresh))):0;−0} dB {RatioBF(P(BP(b, BelowRatio))).Replace(" ", "")}") : "below off";
                    return Inv($"{head} · above {ThrA(P(BP(b, AboveThresh))):0;−0} dB {RatioAF(P(BP(b, AboveRatio))).Replace(" ", "")} · {below} · knee {P(BP(b, Knee)) * 24:0.#} dB");
                }
                case 2:
                {
                    var autos = Enumerable.Range(0, 3).Where(b => BandActive(b) && On(BP(b, AutoRelease))).Select(b => PrismInk.Names[b]).ToList();
                    int src = engine.DeviceAcceptsSidechain(track, di) ? engine.DeviceSidechainSource(track, di) : -1;
                    string sc = src >= 0 ? "sidechain: " + engine.GetTrackName(src) : "own input";
                    return Inv($"{(On(Detect) ? "RMS" : "Peak")} detector · {(autos.Count > 0 ? "auto release on " + string.Join(", ", autos) : "fixed release")} · {sc}");
                }
                default:
                    string xo = nb == 3 ? Inv($"crossovers {HzF(XHz(P(XoverLow)))} / {HzF(XHz(P(XoverHigh)))}") : nb == 2 ? Inv($"crossover {HzF(XHz(P(XoverLow)))}") : "full band";
                    return Inv($"{nb} {(nb == 1 ? "band" : "bands")} · {xo} · amount {P(Amount) * 100:0} %{(solo >= 0 && BandActive(solo) ? " · " + PrismInk.Names[solo] + " solo" : "")}");
            }
        }
        readouts.Add(() =>
        {
            statusLeft.Text = StatusText();
            statusLeft.Foreground = (centreTab == 1 && Sel(Solo, 4) - 1 == band) || On(ScListen) ? AmberLit : Txt2;
            double sr = Sc(S_SampleRate), lat = Sc(S_Latency);
            string la = lat > 0 && sr > 0 ? Inv($"lookahead {lat * 1000 / sr:0.#} ms") : "no lookahead";
            string sc = Sc(S_ScActive) > 0.5 ? " · SC" : "";
            statusRight.Text = sr > 0 ? Inv($"{sr / 1000:0.#} kHz · {la}{sc} · CPU {Sc(S_Cpu) * 100:0.0} %") : "";
        });
        var statusGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        statusGrid.Children.Add(statusLeft); statusGrid.Children.Add(Col(statusRight, 1));
        var status = new Border { Height = 18, Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(8, 0), Child = statusGrid };
        DockPanel.SetDock(status, Dock.Bottom);

        // ---- assemble ---------------------------------------------------------------
        var bodyRow = new DockPanel { LastChildFill = true, Margin = new Thickness(5), Children = { globCol, right, centreBox } };
        var root = new DockPanel { LastChildFill = true, Background = RailBg, Children = { status, bodyRow } };

        void Refresh()
        {
            scN = engine.DeviceScope(track, di, scope, kScope);
            if (!BandActive(band)) { band = 1; foreach (var a in bandListeners) a(); }
            foreach (var a in readouts) a();
        }
        ctx.AddDeviceRefresher(Refresh);
        Refresh();
        return root;

        // local: a tab frame (bar + swapping body), optional extras on the right of the bar.
        Control TabFrame(string[] tabs, int initial, ContentControl host, Func<int, Control> body, bool centered, Action<int>? changed, Control? extrasCtl)
        {
            int sel = Math.Clamp(initial, 0, tabs.Length - 1); var btns = new Border[tabs.Length];
            void Hi()
            {
                for (int i = 0; i < tabs.Length; i++)
                {
                    bool on = i == sel;
                    btns[i].Background = on ? TabBg : Brushes.Transparent; btns[i].BorderBrush = on ? Amber : Brushes.Transparent;
                    var tb = (TextBlock)btns[i].Child!; tb.Foreground = on ? AmberLit : MutedC; tb.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                }
            }
            var bar = centered ? (Avalonia.Controls.Panel)new UniformGrid { Rows = 1 } : new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < tabs.Length; i++)
            {
                int iv = i;
                var b = new Border { Padding = new Thickness(9, 0), BorderThickness = new Thickness(0, 0, 0, 2), BorderBrush = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand),
                    Child = new TextBlock { Text = tabs[i], FontSize = 9, Foreground = MutedC, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center } };
                b.PointerPressed += (_, _) => { sel = iv; Hi(); host.Content = body(iv); changed?.Invoke(iv); };
                btns[i] = b; bar.Children.Add(b);
            }
            var barDock = new DockPanel { Height = 20, LastChildFill = centered };
            if (extrasCtl != null) { var ex = new Border { Padding = new Thickness(0, 0, 8, 0), Child = extrasCtl }; DockPanel.SetDock(ex, Dock.Right); barDock.Children.Add(ex); }
            if (!centered) DockPanel.SetDock(bar, Dock.Left);
            barDock.Children.Add(bar);
            var barBorder = new Border { BorderBrush = Border2, BorderThickness = new Thickness(0, 0, 0, 1), Child = barDock };
            DockPanel.SetDock(barBorder, Dock.Top);
            Hi(); host.Content = body(sel);
            return new DockPanel { LastChildFill = true, Children = { barBorder, host } };
        }
    }
}
