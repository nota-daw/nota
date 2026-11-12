// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Prism body (device kind 21, three-band dynamics), a build of
// the "Nota Prism" mockup (700 × 260) on the #531 card layout: a centre tabbed panel (Bands / Band
// detail / Time), a right panel that follows the tab (Gain reduction · Stage · Detector) over
// Amount · Dry/wet · Output; S is the spectrum over the band count and Amount · Dry/wet · Output. Bands are colour-coded Low = mauve, Mid = neutral ink, High = teal. The spectrum's
// crossover lines, the transfer curve's threshold dots and every slider are controls. Every
// control is a device param, so automation / MIDI learn / presets / A-B / persistence come for
// free. FullBleed — the shared shell draws the header.

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
using Nota.Application.Localization;

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
    private static readonly IBrush Panel = NotaPalette.SurfaceCard;   // a section box on the panel tone
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
    public double WidthFor(IAudioEngine engine, int trackId, int deviceIndex) => CardLayout.WidthFor(engine, trackId, deviceIndex, Width);
    public Control? HeaderAccessory(DeviceCardContext ctx, int deviceIndex) => CardLayout.SizeToggle(ctx, deviceIndex);
    public bool FullBleed => true;
    public string? Subtitle => "Multiband";

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId, di = index;
        float P(int p) => engine.DeviceGetParam(track, di, p);
        void Begin(int p) => engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void End(int p) => engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void Raw(int p, float v) => engine.DeviceSetParam(track, di, p, Math.Clamp(v, 0f, 1f));
        // A discrete edit (click) is one automation gesture, so it records while the transport does.
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
        static string Inv(FormattableString f) => NotaNum.F(f);
        static string DbF(double db) => Math.Abs(db) < 0.05 ? "0.0\u2009dB" : Inv($"{db:+0.0;−0.0}\u2009dB");
        static string DbShort(double db) => Math.Abs(db) < 0.05 ? "0.0" : Inv($"{db:+0.0;−0.0}");
        static string ThrF(double db) => Inv($"{db:0.0}\u2009dB");
        static string RatioAF(double v) { if (v >= 0.999) return "∞ : 1"; double r = 1 / (1 - v); return r < 10 ? Inv($"{r:0.0#} : 1") : Inv($"{r:0} : 1"); }
        static string RatioAShort(double v) { if (v >= 0.999) return "∞:1"; double r = 1 / (1 - v); return r < 10 ? Inv($"{r:0.#}:1") : Inv($"{r:0}:1"); }
        static string RatioBF(double v) { double r = RatioB(v); return Math.Abs(r - 1) < 0.01 ? "1 : 1" : r > 1 ? Inv($"{r:0.0#} : 1") : Inv($"↑ {1 / r:0.0#} : 1"); }
        static string RatioBShort(double v) { double r = RatioB(v); return Math.Abs(r - 1) < 0.01 ? "1:1" : r > 1 ? Inv($"{r:0.0#}") : Inv($"↑{1 / r:0.0}"); }
        static string Ms(double ms) => ms >= 1000 ? Inv($"{ms / 1000:0.00}\u2009s") : ms >= 10 ? Inv($"{ms:0}\u2009ms") : Inv($"{ms:0.0}\u2009ms");
        static string AtkF(double v) => Ms(Exp(v, 0.1, 300));
        static string RelF(double v) => Ms(Exp(v, 5, 3000));
        static string GainF(double v) => DbF((v - 0.5) * 48);
        static string HzF(double hz) => hz >= 1000 ? Inv($"{hz / 1000:0.0}\u2009k") : Inv($"{hz:0}\u2009Hz");
        static string Pct(double v) => Inv($"{v * 100:0}\u2009%");
        string FloorF(int b) => RatioB(P(BP(b, BelowRatio))) < 1 ? Inv($"+{P(BP(b, Floor)) * 48:0.0}\u2009dB") : Inv($"−{P(BP(b, Floor)) * 48:0.0}\u2009dB");

        // ---- small builders ---------------------------------------------------------
        static TextBlock Caps(string t, IBrush? c = null, double fs = NotaType.KnobLabel) => new() { Text = NotaText.Label(t), FontSize = fs, FontWeight = FontWeight.Medium, Foreground = c ?? MutedC, VerticalAlignment = VerticalAlignment.Center };
        // An acronym NotaText.Label does not know (OS, GR) keeps its case: the label as written.
        static TextBlock Acr(string t, IBrush? c = null) { var tb = Caps(t, c); tb.Text = t; return tb; }
        static TextBlock Mono(string t, double fs, IBrush c)
        { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.Measure"); return tb; }
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
            knob.Format = v => fmt(v);   // read-out at any value: typing searches it
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
            var wrap = Switch(label, () => On(p), () => SetP(p, On(p) ? 0f : 1f), out var sync, liveLabel: dynLabel);
            readouts.Add(sync);
            Learn(wrap, p);
            return wrap;
        }

        // Segmented chips over a discrete param (n options spread over 0..1).
        Control Seg(int p, string[] names, Action? changed = null)
        {
            int n = names.Length;
            var seg = DeviceCardKit.Segments(names, () => Sel(p, n), iv => { SetP(p, n > 1 ? iv / (float)(n - 1) : 0f); changed?.Invoke(); }, out var sync);
            readouts.Add(sync);
            Learn(seg, p);
            return seg;
        }

        // Horizontal bar slider over a param, with a mono readout on the right.
        Control Bar(int p, Func<double, string> fmt, Func<IBrush> fillC, Func<bool>? dim = null, double valW = 30, Func<IBrush>? valC = null)
        {
            var row = DeviceCardKit.SliderRow("", () => Math.Clamp(P(p), 0, 1), n => Raw(p, (float)n), () => fmt(Math.Clamp(P(p), 0, 1)), out var sync,
                begin: () => Begin(p), end: () => End(p), reset: () => SetP(p, Def(p)),
                dim: dim, valueWidth: valW);
            readouts.Add(() => { if (row.IsEffectivelyVisible) sync(); });
            Learn(row, p);
            return row;
        }
        Control SliderRow(string label, int p, Func<double, string> fmt, double labW = 50, double valW = 30)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions(Inv($"{labW},*")), VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(Caps(label)); g.Children.Add(Col(Bar(p, fmt, () => NotaPalette.VizInk, null, valW), 1));
            return g;
        }

        // A mono readout that edits its param by vertical drag (~140 px full range); double-click resets.
        // A typeable value for a graph handle (⌥-click it, specs/035).
        ValueField PF(int p, string name, Func<double, string> fmt) => ValueEntry.DeviceParam(engine, track, di, p, name, fmt, () => { foreach (var a in readouts) a(); });

        Control DragVal(int p, Func<double, string> fmt, Func<IBrush> color)
        {
            var tb = Mono("", 8, TxtC); tb.TextAlignment = TextAlignment.Right; tb.HorizontalAlignment = HorizontalAlignment.Stretch;
            var host = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.SizeNorthSouth), Child = tb };
            bool drag = false; double y0 = 0, v0 = 0;
            host.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(host).Properties.IsLeftButtonPressed) return;
                if ((e.ClickCount == 2 || (e.KeyModifiers & KeyModifiers.Alt) != 0)) { SetP(p, Def(p)); e.Handled = true; return; }
                drag = true; y0 = e.GetPosition(host).Y; v0 = P(p); Begin(p); e.Pointer.Capture(host); e.Handled = true;
            };
            host.PointerMoved += (_, e) => { if (drag) { Raw(p, (float)(v0 + (y0 - e.GetPosition(host).Y) / 140.0)); tb.Text = fmt(P(p)); } };
            host.PointerReleased += (_, e) => { if (drag) { drag = false; End(p); e.Pointer.Capture(null); } };
            host.PointerCaptureLost += (_, _) => { if (drag) { drag = false; End(p); } };
            readouts.Add(() => { tb.Text = fmt(P(p)); tb.Foreground = color(); });
            Learn(host, p);
            ValueEntry.Attach(host, ValueEntry.DeviceParam(engine, track, di, p, engine.DeviceParamName(track, di, p), fmt), tb);   // ⌥-click types (specs/035)
            return host;
        }

        // Band name cell: colour bar + name (lit when selected).
        Control BandName(int b, double barH)
        {
            var name = new TextBlock { Text = PrismInk.Names[b], FontSize = 9, VerticalAlignment = VerticalAlignment.Center };
            readouts.Add(() => { bool s = band == b; name.Foreground = s ? PrismInk.BandLit[b] : TxtC; name.FontWeight = s ? FontWeight.SemiBold : FontWeight.Normal; });
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = {
                new Border { Width = 3, Height = barH, CornerRadius = NotaRadius.Field, Background = PrismInk.Band[b] }, name } };
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
                Inactive.Set(row, !act);
            });
            return row;
        }

        // ======================================================================
        // CENTRE — Bands (spectrum + per-band overview)
        // ======================================================================
        PrismSpectrum Spectrum()
        {
            var spec = new PrismSpectrum(engine, track, di) { DefaultLow = Def(XoverLow), DefaultHigh = Def(XoverHigh) };
            spec.CrossoverChanged += (h, v) => Raw(h == 0 ? XoverLow : XoverHigh, (float)v);
            spec.DragStarted += h => Begin(h == 0 ? XoverLow : XoverHigh);
            spec.DragEnded += h => End(h == 0 ? XoverLow : XoverHigh);
            spec.BandClicked += SelectBand;
            ValueEntry.AttachHandles(spec, [PF(XoverLow, Loc.T("device.prism.low_mid_crossover"), v => HzF(XHz(v)))], [PF(XoverHigh, Loc.T("device.prism.mid_high_crossover"), v => HzF(XHz(v)))]);
            ToolTip.SetTip(spec, MenuKit.Keys(Loc.T("device.prism.input_spectrum_fill_and_output")));
            readouts.Add(() => { spec.Set(P(XoverLow), P(XoverHigh), BandCount(), band, Sc(S_SampleRate)); spec.Tick(); });
            return spec;
        }
        Control BandsTab()
        {
            var spec = Spectrum();
            const string cols = "50,*,*,40,40";
            var head = new Grid { ColumnDefinitions = new ColumnDefinitions(cols), Height = 11 };
            var hA = Caps(Loc.T("device.prism.above_compress_caps"), DimC); hA.HorizontalAlignment = HorizontalAlignment.Center;
            var hB = Caps(Loc.T("device.prism.below_expand_caps"), DimC); hB.HorizontalAlignment = HorizontalAlignment.Center;
            var hG = Caps("GAIN", DimC); hG.HorizontalAlignment = HorizontalAlignment.Right;
            var hR = Acr("GR", DimC); hR.HorizontalAlignment = HorizontalAlignment.Right;
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
                ToolTip.SetTip(above, Loc.T("device.prism.compression_threshold_ratio_set_the"));
                g.Children.Add(Col(above, 1));
                bool BelowOff() => !On(BP(bb, BelowOn));
                var below = Bar(BP(bb, BelowThresh), v => BelowOff() ? "off" : Inv($"{ThrB(v):0;−0} · {RatioBShort(P(BP(bb, BelowRatio)))}"), () => PrismInk.Band[bb], BelowOff, 52);
                below.Margin = new Thickness(0, 0, 8, 0);
                // The readout toggles the below section on / off.
                var belowVal = (TextBlock)((Grid)below).Children[1];
                belowVal.Cursor = new Cursor(StandardCursorType.Hand);
                belowVal.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(belowVal).Properties.IsLeftButtonPressed) return; SetP(BP(bb, BelowOn), BelowOff() ? 1f : 0f); e.Handled = true; };
                ToolTip.SetTip(below, Loc.T("device.prism.expansion_upward_compression_threshold_ratio"));
                g.Children.Add(Col(below, 2));
                var gain = DragVal(BP(bb, Gain), v => DbShort((v - 0.5) * 48), () => Math.Abs(P(BP(bb, Gain)) - 0.5f) < 0.001f ? Txt2 : TxtC);
                ToolTip.SetTip(gain, Loc.T("device.prism.band_gain_24_db_drag"));
                g.Children.Add(Col(gain, 3));
                var grTrack = new Border { Width = 32, Height = 4, CornerRadius = NotaRadius.Field, Background = Inset, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
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
            ValueEntry.AttachHandles(tr,
                [PF(BP(b, AboveThresh), "Above threshold", v => ThrF(ThrA(v))), PF(BP(b, AboveRatio), "Above ratio", RatioAF)],
                [PF(BP(b, BelowThresh), "Below threshold", v => ThrF(ThrB(v))), PF(BP(b, BelowRatio), "Below ratio", RatioBF)]);
            tr.ResetRequested += h =>
            {
                int t = BP(b, h == 1 ? AboveThresh : BelowThresh), r = BP(b, h == 1 ? AboveRatio : BelowRatio);
                SetP(t, Def(t)); SetP(r, Def(r));
            };
            ToolTip.SetTip(tr, MenuKit.Keys(Loc.T("device.prism.drag_a_dot_threshold_ratio")));
            readouts.Add(() =>
            {
                if (!tr.IsEffectivelyVisible) return;
                double amt = P(Amount);
                tr.Set(b, ThrA(P(BP(b, AboveThresh))), P(BP(b, AboveRatio)) * amt, On(BP(b, BelowOn)), ThrB(P(BP(b, BelowThresh))),
                       (RatioB(P(BP(b, BelowRatio))) - 1) * amt, P(BP(b, Floor)) * 48, P(BP(b, Knee)) * 24, Sc(S_Level0 + b));
            });
            var trCap = Caps("Transfer · " + PrismInk.Names[b].ToLowerInvariant());
            var kneeRow = new Grid { ColumnDefinitions = new ColumnDefinitions("32,*"), Margin = new Thickness(0, 4, 0, 0) };
            kneeRow.Children.Add(Caps("KNEE"));
            kneeRow.Children.Add(Col(Bar(BP(b, Knee), v => Inv($"{v * 24:0.0}\u2009dB"), () => PrismInk.Band[b], null, 32), 1));
            var left = new Border { Width = 150, BorderBrush = BorderIn, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(7, 5),
                Child = new DockPanel { Children = { Docked(trCap, Dock.Top), Docked(kneeRow, Dock.Bottom), new Border { Margin = new Thickness(0, 4, 0, 0), Child = tr } } } };

            // ABOVE — compression
            var aCap = Caps(Loc.T("device.prism.above_compression_caps"), PrismInk.BandLit[b]);
            var aGr = Mono("", 7, Txt2); aGr.HorizontalAlignment = HorizontalAlignment.Right;
            readouts.Add(() => { double gr = Sc(S_Gr0 + b); aGr.Text = gr > 0.05 ? Inv($"GR −{gr:0.0}\u2009dB") : "GR 0.0\u2009dB"; });
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
            var bCap = Caps(Loc.T("device.prism.below_expansion_caps"));
            readouts.Add(() => { bool up = RatioB(P(BP(b, BelowRatio))) < 0.99; bCap.Text = up ? Loc.T("device.prism.below_upward_compression") : Loc.T("device.prism.below_expansion"); bCap.Foreground = On(BP(b, BelowOn)) ? Txt2 : MutedC; });
            var bToggle = Toggle(BP(b, BelowOn), "", () => On(BP(b, BelowOn)) ? "on" : "off");
            var bHead = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Height = 11 };
            bHead.Children.Add(bCap); bHead.Children.Add(Col(bToggle, 1));
            var bKnobs = new UniformGrid { Rows = 1, VerticalAlignment = VerticalAlignment.Center };
            bKnobs.Children.Add(K(BP(b, BelowThresh), "THRESH", v => ThrF(ThrB(v)), 36, 52, Mauve));
            bKnobs.Children.Add(K(BP(b, BelowRatio), "RATIO", RatioBF, 36, 52, Mauve));
            bKnobs.Children.Add(K(BP(b, BelowAttack), "ATTACK", AtkF, 36, 52, Mauve));
            bKnobs.Children.Add(K(BP(b, BelowRelease), "RELEASE", RelF, 36, 52, Mauve));
            bKnobs.Children.Add(K(BP(b, Floor), "FLOOR", _ => FloorF(b), 36, 52, Mauve));
            readouts.Add(() => Inactive.Set(bKnobs, !On(BP(b, BelowOn)), interactive: true));
            ToolTip.SetTip(bKnobs, Loc.T("device.prism.ratio_above_1_1_expands"));
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
            ToolTip.SetTip(trace, Loc.T("device.prism.the_selected_bands_input_peaks"));
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
                ToolTip.SetTip(auto, Loc.T("device.prism.auto_release_short_peaks_recover"));
                g.Children.Add(Col(auto, 3));
                rows.Children.Add(BandRow(bb, g, 18));
            }
            rows.Height = 74;
            return new DockPanel { LastChildFill = true, Margin = new Thickness(8, 5, 8, 3), Children = {
                Docked(rows, Dock.Bottom),
                new Border { Margin = new Thickness(0, 0, 0, 4), Child = trace } } };
        }

        // ======================================================================
        // RIGHT — the panel that follows the tab: Gain reduction · Stage · Detector
        // ======================================================================
        (Control, Control) MetersPanel()
        {
            var meters = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,Auto"), Margin = new Thickness(0, 4, 0, 0) };
            for (int b = 0; b < 3; b++)
            {
                int bb = b;
                var m = new PrismGrMeter { Band = bb, HorizontalAlignment = HorizontalAlignment.Center };
                var val = Mono("", 7, PrismInk.Band[bb]); val.HorizontalAlignment = HorizontalAlignment.Center;
                var nm = Caps(PrismInk.Names[bb]); nm.HorizontalAlignment = HorizontalAlignment.Center;
                readouts.Add(() =>
                {
                    bool act = BandActive(bb);
                    m.Soloed = act && Sel(Solo, 4) == bb + 1;
                    m.Set(act ? Sc(S_Gr0 + bb) : 0, act ? Sc(S_Boost0 + bb) : 0, On(PeakHold));
                    double gr = Sc(S_Gr0 + bb), bo = Sc(S_Boost0 + bb);
                    val.Text = !act ? "—" : bo > gr + 0.05 ? Inv($"+{bo:0.0}") : gr > 0.05 ? Inv($"−{gr:0.0}") : "0.0";
                    val.Foreground = band == bb ? PrismInk.BandLit[bb] : PrismInk.Band[bb];
                    nm.Foreground = band == bb ? PrismInk.BandLit[bb] : MutedC;
                    Inactive.Set(m, !act, interactive: true);
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
                    soloCells[i].Background = on ? Amber : OffPill; soloCells[i].BorderBrush = on ? Amber : Brushes.Transparent;
                    var t = (TextBlock)soloCells[i].Child!; t.Foreground = on ? NotaPalette.TextOnAccent : act ? Txt2 : DimC; t.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                }
                soloTxt.Text = s >= 0 && BandActive(s) ? PrismInk.Names[s].ToLowerInvariant() : "none"; soloTxt.Foreground = s >= 0 ? AmberLit : MutedC;
            }
            var soloRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
            string[] sl = { "L", "M", "H" };
            for (int i = 0; i < 3; i++)
            {
                int iv = i;
                var c = new Border { Height = 15, Padding = new Thickness(6, 0), CornerRadius = NotaRadius.Cell, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand),
                    Child = new TextBlock { Text = sl[i], FontSize = 8, VerticalAlignment = VerticalAlignment.Center } };
                c.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(c).Properties.IsLeftButtonPressed || !BandActive(iv)) return;
                    SetP(Solo, Sel(Solo, 4) == iv + 1 ? 0f : (iv + 1) / 3f); HiSolo(); e.Handled = true;
                };
                soloCells[i] = c; soloRow.Children.Add(c);
            }
            Learn(soloRow, Solo);
            ToolTip.SetTip(soloRow, Loc.T("device.prism.solo_a_band_hear_only"));
            readouts.Add(HiSolo); HiSolo();
            var soloGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 6 };
            soloGrid.Children.Add(Caps("SOLO")); soloGrid.Children.Add(Col(soloRow, 1)); soloGrid.Children.Add(Col(soloTxt, 2));
            var bottom = Divider(new StackPanel { Spacing = 5, Children = { soloGrid, Toggle(PeakHold, "Peak hold") } });
            bottom.Margin = new Thickness(0, 5, 0, 0);
            return (CardLayout.Head(CardLayout.Title(Loc.T("device.prism.gain_reduction")), ModeSeg()),
                new DockPanel { Margin = new Thickness(8, 2, 8, 5), Children = { Docked(bottom, Dock.Bottom), meters } });
        }
        (Control, Control) DetectorPanel()
        {
            // Sidechain source (None + every other track, and the pads / chains / voices of a
            // track's instrument, specs/050).
            var scName = new TextBlock { FontSize = 8, Foreground = TxtC, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            bool scOk = engine.DeviceAcceptsSidechain(track, di);
            readouts.Add(() =>
            {
                int src = scOk ? engine.DeviceSidechainSource(track, di) : -1;
                scName.Text = !scOk ? Loc.T("device.prism.n_a_in_a_rack") : src < 0 ? Loc.T("device.prism.none_own_input") : SidechainSourcePicker.Name(engine, engine.DeviceSidechainKey(track, di));
                scName.Foreground = src >= 0 ? TxtC : Txt2;
            });
            var scBox = new Border { Height = 16, Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Cell, Padding = new Thickness(6, 0),
                Cursor = new Cursor(StandardCursorType.Hand), IsEnabled = scOk,
                Child = new DockPanel { Children = { Docked(new Glyph(GlyphKind.ChevronDown, 8) { Foreground = MutedC, Margin = new Thickness(5, 0, 0, 0) }, Dock.Right), scName } } };
            ToolTip.SetTip(scBox, Loc.T("device.prism.sidechain_key_every_bands_detector"));
            scBox.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(scBox).Properties.IsLeftButtonPressed || !scOk) return;
                var fly = new MenuFlyout();
                void Changed() { ctx.NotifyChanged(); foreach (var a in readouts) a(); }
                void Item(string name, bool on, Action pick)
                {
                    var mi = new MenuItem { Header = name };
                    mi.ToggleType = MenuItemToggleType.Radio; mi.IsChecked = on;
                    mi.Click += (_, _) => pick();
                    fly.Items.Add(mi);
                }
                Item(Loc.T("device.prism.none_own_input"), engine.DeviceSidechainSource(track, di) < 0, () => { engine.SetDeviceSidechainSource(track, di, -1); Changed(); });
                fly.Items.Add(new Separator());
                foreach (var row in SidechainSourcePicker.MenuRows(engine, track, di, Changed)) fly.Items.Add(SidechainSourcePicker.MenuItemFor(row));
                fly.ShowAt(scBox);
                e.Handled = true;
            };
            var scRow = new Grid { ColumnDefinitions = new ColumnDefinitions("50,*"), VerticalAlignment = VerticalAlignment.Center };
            scRow.Children.Add(Caps("SIDECHAIN")); scRow.Children.Add(Col(scBox, 1));
            var listen = Toggle(ScListen, "Listen");
            ToolTip.SetTip(listen, Loc.T("device.prism.listen_hear_the_detectors_key"));
            var seg = Seg(Detect, new[] { "Peak", "RMS" });
            ToolTip.SetTip(seg, Loc.T("device.prism.detector_peak_follows_every_transient"));
            return (CardLayout.Head(CardLayout.Title(Loc.T("device.prism.detector")), seg), CardLayout.Rows(
                SliderRow("LOOKAHEAD", Lookahead, v => Inv($"{v * 10:0.0}\u2009ms")),
                CardLayout.Rule(),
                scRow,
                listen));
        }
        (Control, Control) StagePanel()
        {
            var mk = Toggle(AutoMakeup, "Auto makeup");
            ToolTip.SetTip(mk, Loc.T("device.prism.auto_makeup_each_band_gets"));
            TextBlock Val() { var t = Mono("", 8, TxtC); t.HorizontalAlignment = HorizontalAlignment.Right; return t; }
            Control Line(string k, TextBlock v)
            {
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                g.Children.Add(new TextBlock { Text = k, FontSize = 8, Foreground = Txt2, VerticalAlignment = VerticalAlignment.Center });
                g.Children.Add(Col(v, 1));
                return g;
            }
            var cap = Caps("");
            var gr = Val(); var boost = Val(); var lvl = Val();
            readouts.Add(() =>
            {
                cap.Text = NotaText.Label(PrismInk.Names[band]);
                cap.Foreground = PrismInk.BandLit[band];
                gr.Text = Sc(S_Gr0 + band) > 0.05 ? Inv($"−{Sc(S_Gr0 + band):0.0}\u2009dB") : "0.0\u2009dB";
                boost.Text = Sc(S_Boost0 + band) > 0.05 ? Inv($"+{Sc(S_Boost0 + band):0.0}\u2009dB") : "0.0\u2009dB";
                lvl.Text = Sc(S_Level0 + band) <= -119 ? "−∞\u2009dB" : Inv($"{Sc(S_Level0 + band):0.0}\u2009dB");
            });
            var box = CardLayout.ReadoutBox(new StackPanel { Spacing = 2, Children = { cap, Line("GR", gr), Line("boost", boost), Line("level", lvl) } });
            var clipTxt = Mono("", 7, TxtC); clipTxt.HorizontalAlignment = HorizontalAlignment.Right;
            readouts.Add(() =>
            {
                double c = Sc(S_ClipDb);
                clipTxt.Text = On(SoftClip) && c > 0.05 ? Inv($"−0.3\u2009dBFS · {c:0.0}") : "−0.3\u2009dBFS";
                clipTxt.Foreground = On(SoftClip) ? (c > 0.05 ? NotaPalette.Warning : TxtC) : MutedC;
            });
            var clipRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 6 };
            var clipSeg = Seg(SoftClip, new[] { "Off", "On" });
            ToolTip.SetTip(clipSeg, Loc.T("device.prism.soft_clip_a_smooth_ceiling"));
            clipRow.Children.Add(Caps("SOFT CLIP")); clipRow.Children.Add(Col(clipSeg, 1)); clipRow.Children.Add(Col(clipTxt, 2));
            return (CardLayout.Head(mk), CardLayout.Rows(box, CardLayout.Rule(), clipRow));
        }

        // ======================================================================
        // Frames: L (tabs, the panel that follows them, Amount · Dry/wet · Output under it) · S (the
        // spectrum over the band count and Amount · Dry/wet · Output)
        // ======================================================================
        int centreTab = view.Centre;
        var extras = new ContentControl { VerticalAlignment = VerticalAlignment.Center };
        Control ModeSeg()
        {
            var seg = Seg(BandsP, new[] { "3", "2", "1" }, () => { if (!BandActive(band)) SelectBand(1); foreach (var a in readouts) a(); });
            ToolTip.SetTip(seg, Loc.T("device.prism.bands_3_low_mid_high"));
            return seg;
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
                var c = new Border { Height = 15, Padding = new Thickness(6, 0), CornerRadius = NotaRadius.Cell, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand),
                    Child = new TextBlock { Text = PrismInk.Names[i], FontSize = 8, VerticalAlignment = VerticalAlignment.Center } };
                c.PointerPressed += (_, e) => { SelectBand(iv); e.Handled = true; };
                cells[i] = c; chips.Children.Add(c);
            }
            readouts.Add(Hi); Hi();
            return chips;
        }
        Control Info(Func<string> text)
        {
            var t = Mono("", 7, MutedC);
            readouts.Add(() => t.Text = text());
            return t;
        }
        var extrasBodies = new Control?[3];
        void SetExtras(int t) => extras.Content = extrasBodies[t] ??= t switch
        {
            1 => BandChips(),
            2 => Info(() => PrismInk.Names[band].ToLowerInvariant() + " · " + AtkF(P(BP(band, Attack))) + " / " + RelF(P(BP(band, Release)))),
            _ => Info(() => BandCount() == 1 ? Loc.T("device.prism.one_band") : BandCount() == 2 ? HzF(XHz(P(XoverLow))) : HzF(XHz(P(XoverLow))) + " · " + HzF(XHz(P(XoverHigh)))),
        };
        Control OutputKnobs(double cw) => CardLayout.Output(
            K(Amount, "AMOUNT", Pct, 34, cw),
            K(Mix, "DRY/WET", Pct, 44, cw + 4),
            K(Output, "OUTPUT", v => DbF((v - 0.5) * 48), 34, cw));

        Control root;
        if (CardLayout.IsMini(ctx, di))
        {
            var spec = Spectrum();
            root = CardLayout.Mini(spec, CardLayout.Line(5, Caps("Bands"), ModeSeg()),
                K(Amount, "AMOUNT", Pct, 34, 56), K(Mix, "DRY/WET", Pct, 34, 56), K(Output, "OUTPUT", v => DbF((v - 0.5) * 48), 34, 56));
        }
        else
        {
            SetExtras(centreTab);
            root = CardLayout.Large(new[] { "Bands", "Band detail", "Time" }, Math.Clamp(centreTab, 0, 2),
                t => t switch { 1 => DetailTab(), 2 => TimeTab(), _ => BandsTab() },
                t => t switch { 1 => StagePanel(), 2 => DetectorPanel(), _ => MetersPanel() },
                extras,
                OutputKnobs(54),
                t => { centreTab = t; view = (band, t, 0); ViewState[(track, di)] = view; SetExtras(t); foreach (var a in readouts) a(); });
        }

        void Refresh()
        {
            scN = engine.DeviceScope(track, di, scope, kScope);
            if (!BandActive(band)) { band = 1; foreach (var a in bandListeners) a(); }
            foreach (var a in readouts) a();
        }
        ctx.AddDeviceRefresher(Refresh);
        Refresh();
        return root;

    }
}
