// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Prism body (device kind 21, three-band dynamics), a build of
// the "Nota Prism" picture of the style guide (700 × 260): an always-visible Global rail (Amount
// + Output as upright value bars), a centre tabbed panel (Bands / Band detail / Time) and a right
// tabbed panel (Meters / Output), each a pressed-in section. The bands are greys told apart by
// shade. The picked band and tabs are view state for the session only. The spectrum's crossover lines, the transfer curve's threshold dots and every
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

    private static readonly IBrush AmberLit = NotaPalette.AccentBright;
    private static readonly IBrush RowSel = NotaPalette.Wash(NotaPalette.TextPrimary, 0x0C);
    private static readonly IBrush TxtC = NotaPalette.TextPrimary;
    private static readonly IBrush Txt2 = NotaPalette.TextSecondary;
    private static readonly IBrush MutedC = NotaPalette.TextTertiary;
    private static readonly IBrush DimC = NotaPalette.TextDisabled;

    // The selected band and tabs survive a card rebuild (keyed by track + device slot).
    private static readonly Dictionary<(int, int), (int Band, int Centre, int Right)> ViewState = new();

    public double Width => 700;
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
        // Five knobs share the row: the dB readouts at the ends ("−18.0 dB") get wider columns.
        static Grid KnobRow(params Control[] knobs)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("6*,5*,5*,5*,6*"), VerticalAlignment = VerticalAlignment.Center };
            for (int i = 0; i < knobs.Length; i++) { Grid.SetColumn(knobs[i], i); g.Children.Add(knobs[i]); }
            return g;
        }

        // ---- small builders ---------------------------------------------------------
        static TextBlock Caps(string t, IBrush? c = null) => DeviceBodyKit.Label(t, c ?? MutedC);
        // An acronym NotaText.Label does not know (OS, GR) keeps its case: the label as written.
        static TextBlock Acr(string t, IBrush? c = null) { var tb = Caps(t, c); tb.Text = t; return tb; }
        static TextBlock Mono(string t, IBrush c) => DeviceBodyKit.Value(t, c);
        static Control Docked(Control c, Dock d) { DockPanel.SetDock(c, d); return c; }
        static Control Col(Control c, int col) { Grid.SetColumn(c, col); return c; }
        static Control GRow(Control c, int row) { Grid.SetRow(c, row); return c; }
        static Border Divider(Control child, double top = 8) => new() { BorderBrush = NotaPalette.Rule, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, top, 0, 0), Child = child };
        Control Learn(Control c, int p) { MidiLearn.Bind(c, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p)); return c; }

        // Gauge knob bound to a device param (automation gesture + MIDI learn + live follow).
        Control K(int p, string name, Func<double, string> fmt, double cellW = 56, bool inline = false)
        {
            var val = Mono(fmt(P(p)), TxtC);
            var knob = new Knob(P(p), 1.0) { Accent = true, Inline = inline, Default = Def(p) };
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
        Control Bar(int p, Func<double, string> fmt, Func<IBrush> fillC, Func<bool>? dim = null, double valW = 36, Func<IBrush>? valC = null)
        {
            var row = DeviceCardKit.SliderRow("", () => Math.Clamp(P(p), 0, 1), n => Raw(p, (float)n), () => fmt(Math.Clamp(P(p), 0, 1)), out var sync,
                begin: () => Begin(p), end: () => End(p), reset: () => SetP(p, Def(p)),
                dim: dim, valueWidth: valW);
            readouts.Add(() => { if (row.IsEffectivelyVisible) sync(); });
            Learn(row, p);
            return row;
        }
        Control SliderRow(string label, int p, Func<double, string> fmt, double labW = 62, double valW = 40)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions(Inv($"{labW},*")), VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(Caps(label)); g.Children.Add(Col(Bar(p, fmt, () => NotaPalette.VizInk, null, valW), 1));
            return g;
        }

        // A mono readout that edits its param by vertical drag (~140 px full range); double-click resets.
        Control DragVal(int p, Func<double, string> fmt, Func<IBrush> color)
        {
            var tb = Mono("", TxtC); tb.TextAlignment = TextAlignment.Right; tb.HorizontalAlignment = HorizontalAlignment.Stretch; tb.VerticalAlignment = VerticalAlignment.Center;
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
            return host;
        }

        // Band name cell: colour bar + name (lit when selected).
        Control BandName(int b, double barH)
        {
            var name = new TextBlock { Text = PrismInk.Names[b], FontSize = NotaType.KnobValue, VerticalAlignment = VerticalAlignment.Center };
            readouts.Add(() => { bool s = band == b; name.Foreground = s ? TxtC : Txt2; name.FontWeight = s ? FontWeight.SemiBold : FontWeight.Normal; });
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), Children = {
                new Border { Width = 3, Height = barH, CornerRadius = NotaRadius.Fine, Background = PrismInk.Band[b] }, name } };
        }

        // A clickable table row for band b: selects the band; dimmed + inert when the band is off.
        Border BandRow(int b, Grid content, double height)
        {
            content.Height = height;
            var row = new Border { CornerRadius = NotaRadius.Cell, Background = Brushes.Transparent, Child = content };
            row.PointerPressed += (_, e) => { if (e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) SelectBand(b); };
            bool? wasAct = null;
            readouts.Add(() =>
            {
                bool act = BandActive(b);
                row.Background = band == b && act ? RowSel : Brushes.Transparent;
                // Only on a change: Inactive.Set(false) would undo the rows' own dims (an off Below).
                if (wasAct != act) { wasAct = act; Inactive.Set(row, !act); }
            });
            return row;
        }

        // ======================================================================
        // LEFT — GLOBAL column (Amount + Output)
        // ======================================================================
        DeviceBodyKit.RailFader Fader(int p, string label, Func<string> text, string tip)
        {
            var f = DeviceBodyKit.Upright(label, () => P(p), v => Raw(p, (float)v), text, out var val, out var sync,
                begin: () => Begin(p), end: () => End(p), reset: () => SetP(p, Def(p)));
            readouts.Add(sync);
            Learn(f, p);
            ToolTip.SetTip(f, tip);
            return new DeviceBodyKit.RailFader(label, f, val);
        }
        var globCol = DeviceBodyKit.Sec(DeviceBodyKit.FaderRail("Global", true,
            Fader(Amount, "Amount", () => Pct(P(Amount)), "Amount — scales every band's compression and expansion (0\u2009% = none). Double-click: 100\u2009%"),
            Fader(Output, "Out", () => DbShort((P(Output) - 0.5) * 48), "Output gain ±24\u2009dB — double-click: 0\u2009dB")),
            new Thickness(8, 8, 8, 6), 84);   // wide enough that "Amount" and "Out" do not touch
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

            const string cols = "54,*,*,40,46";
            var head = new Grid { ColumnDefinitions = new ColumnDefinitions(cols), Height = 16 };
            var hA = Caps("Above · compress"); hA.HorizontalAlignment = HorizontalAlignment.Center;
            var hB = Caps("Below · expand"); hB.HorizontalAlignment = HorizontalAlignment.Center;
            var hG = Caps("Gain"); hG.HorizontalAlignment = HorizontalAlignment.Right;
            var hR = Acr("GR"); hR.HorizontalAlignment = HorizontalAlignment.Right;
            head.Children.Add(Caps("Band")); head.Children.Add(Col(hA, 1)); head.Children.Add(Col(hB, 2)); head.Children.Add(Col(hG, 3)); head.Children.Add(Col(hR, 4));
            var rows = new StackPanel { Children = { head } };
            for (int b = 0; b < 3; b++)
            {
                int bb = b;
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions(cols) };
                g.Children.Add(BandName(bb, 16));
                var above = Bar(BP(bb, AboveThresh), v => Inv($"{ThrA(v):0;−0} · {RatioAShort(P(BP(bb, AboveRatio)))}"), () => PrismInk.Band[bb], null, 56,
                                () => band == bb ? PrismInk.BandLit[bb] : TxtC);
                above.Margin = new Thickness(0, 0, 8, 0);
                ToolTip.SetTip(above, "Compression threshold · ratio (set the ratio in Band detail)");
                g.Children.Add(Col(above, 1));
                bool BelowOff() => !On(BP(bb, BelowOn));
                var below = Bar(BP(bb, BelowThresh), v => BelowOff() ? "off" : Inv($"{ThrB(v):0;−0} · {RatioBShort(P(BP(bb, BelowRatio)))}"), () => PrismInk.Band[bb], BelowOff, 56);
                below.Margin = new Thickness(0, 0, 8, 0);
                // The readout toggles the below section on / off.
                var belowVal = (TextBlock)((Grid)below).Children[1];
                belowVal.Cursor = new Cursor(StandardCursorType.Hand);
                belowVal.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(belowVal).Properties.IsLeftButtonPressed) return; SetP(BP(bb, BelowOn), BelowOff() ? 1f : 0f); e.Handled = true; };
                ToolTip.SetTip(below, "Expansion / upward-compression threshold · ratio — click the readout to switch it on or off");
                g.Children.Add(Col(below, 2));
                var gain = DragVal(BP(bb, Gain), v => DbShort((v - 0.5) * 48), () => Math.Abs(P(BP(bb, Gain)) - 0.5f) < 0.001f ? Txt2 : TxtC);
                ToolTip.SetTip(gain, "Band gain ±24\u2009dB — drag up / down, double-click resets");
                g.Children.Add(Col(gain, 3));
                var grTrack = new Border { Width = 34, Height = 3, CornerRadius = NotaRadius.Fine, Background = NotaPalette.Well, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
                var grFill = new Border { Background = PrismInk.Band[bb], HorizontalAlignment = HorizontalAlignment.Right };
                grTrack.Child = grFill;
                readouts.Add(() => grFill.Width = 34 * Math.Clamp(Sc(S_Gr0 + bb) / 12, 0, 1));
                g.Children.Add(Col(grTrack, 4));
                rows.Children.Add(BandRow(bb, g, 20));
            }
            rows.Spacing = 2;
            return new DockPanel { LastChildFill = true, Margin = new Thickness(0, 6, 0, 0), Children = {
                Docked(rows, Dock.Bottom),
                new Border { Margin = new Thickness(0, 0, 0, 8), Child = spec } } };
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
            var trCap = Caps("Transfer · " + PrismInk.Names[b].ToLowerInvariant());
            var kneeRow = new Grid { ColumnDefinitions = new ColumnDefinitions("32,*"), Margin = new Thickness(0, 6, 0, 0) };
            kneeRow.Children.Add(Caps("Knee"));
            kneeRow.Children.Add(Col(Bar(BP(b, Knee), v => Inv($"{v * 24:0.0}\u2009dB"), () => PrismInk.Band[b], null, 44), 1));
            var left = new Border { Width = 132, BorderBrush = NotaPalette.Rule, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(0, 6, 8, 0),
                Child = new DockPanel { Children = { Docked(trCap, Dock.Top), Docked(kneeRow, Dock.Bottom), new Border { Margin = new Thickness(0, 4, 0, 0), Child = tr } } } };

            // ABOVE — compression
            var aCap = Caps("Above · compression", Txt2);
            var aGr = Caps("", Txt2); aGr.HorizontalAlignment = HorizontalAlignment.Right;
            readouts.Add(() => { double gr = Sc(S_Gr0 + b); aGr.Text = gr > 0.05 ? Inv($"GR −{gr:0.0}\u2009dB") : "GR 0.0\u2009dB"; });
            var aHead = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Height = 16 };
            aHead.Children.Add(aCap); aHead.Children.Add(Col(aGr, 1));
            var aKnobs = KnobRow(
                K(BP(b, AboveThresh), "Thresh", v => ThrF(ThrA(v))),
                K(BP(b, AboveRatio), "Ratio", RatioAF),
                K(BP(b, Attack), "Attack", AtkF),
                K(BP(b, Release), "Release", RelF),
                K(BP(b, Gain), "Gain", GainF));
            var aSec = new DockPanel { Children = { Docked(aHead, Dock.Top), aKnobs } };

            // BELOW — expansion / upward compression
            var bCap = Caps("Below · expansion");
            readouts.Add(() => { bool up = RatioB(P(BP(b, BelowRatio))) < 0.99; bCap.Text = up ? "Below · upward compression" : "Below · expansion"; bCap.Foreground = On(BP(b, BelowOn)) ? Txt2 : DimC; });
            var bToggle = Toggle(BP(b, BelowOn), "", () => On(BP(b, BelowOn)) ? "on" : "off");
            var bHead = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Height = 16 };
            bHead.Children.Add(bCap); bHead.Children.Add(Col(bToggle, 1));
            var bKnobs = KnobRow(
                K(BP(b, BelowThresh), "Thresh", v => ThrF(ThrB(v))),
                K(BP(b, BelowRatio), "Ratio", RatioBF),
                K(BP(b, BelowAttack), "Attack", AtkF),
                K(BP(b, BelowRelease), "Release", RelF),
                K(BP(b, Floor), "Floor", _ => FloorF(b)));
            readouts.Add(() => Inactive.Set(bKnobs, !On(BP(b, BelowOn)), interactive: true));
            ToolTip.SetTip(bKnobs, "Ratio above 1 : 1 expands (quiet parts get quieter); below 1 : 1 (↑) compresses upward (quiet parts are lifted). Floor limits either to ± that many dB.");
            var bSec = Divider(new DockPanel { Children = { Docked(bHead, Dock.Top), bKnobs } }, 4);

            var right = new Grid { RowDefinitions = new RowDefinitions("*,*"), Margin = new Thickness(8, 4, 0, 0) };
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
            ToolTip.SetTip(trace, "The selected band's input peaks (grey) and what its compressor's detector makes of them (the last ~1.5\u2009s), against the threshold.");
            const string cols = "54,*,*,40";
            var head = new Grid { ColumnDefinitions = new ColumnDefinitions(cols), Height = 16 };
            var hA = Caps("Attack"); hA.HorizontalAlignment = HorizontalAlignment.Center;
            var hR = Caps("Release"); hR.HorizontalAlignment = HorizontalAlignment.Center;
            var hU = Caps("Auto"); hU.HorizontalAlignment = HorizontalAlignment.Right;
            head.Children.Add(Caps("Band")); head.Children.Add(Col(hA, 1)); head.Children.Add(Col(hR, 2)); head.Children.Add(Col(hU, 3));
            var rows = new StackPanel { Children = { head } };
            for (int b = 0; b < 3; b++)
            {
                int bb = b;
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions(cols) };
                g.Children.Add(BandName(bb, 14));
                IBrush Lit() => band == bb ? PrismInk.BandLit[bb] : TxtC;
                var atk = Bar(BP(bb, Attack), AtkF, () => PrismInk.Band[bb], null, 46, Lit); atk.Margin = new Thickness(0, 0, 8, 0);
                var rel = Bar(BP(bb, Release), RelF, () => PrismInk.Band[bb], null, 46, Lit); rel.Margin = new Thickness(0, 0, 8, 0);
                g.Children.Add(Col(atk, 1)); g.Children.Add(Col(rel, 2));
                var auto = Toggle(BP(bb, AutoRelease), ""); auto.HorizontalAlignment = HorizontalAlignment.Right;
                ToolTip.SetTip(auto, "Auto release — short peaks recover fast, sustained compression recovers slowly");
                g.Children.Add(Col(auto, 3));
                rows.Children.Add(BandRow(bb, g, 19));
            }
            rows.Spacing = 2;
            return new DockPanel { LastChildFill = true, Margin = new Thickness(0, 6, 0, 0), Children = {
                Docked(rows, Dock.Bottom),
                new Border { Margin = new Thickness(0, 0, 0, 8), Child = trace } } };
        }

        // ======================================================================
        // RIGHT — Meters / Output
        // ======================================================================
        Control MetersTab()
        {
            var meters = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,Auto"), Margin = new Thickness(0, 8, 0, 0) };
            for (int b = 0; b < 3; b++)
            {
                int bb = b;
                var m = new PrismGrMeter { Band = bb, HorizontalAlignment = HorizontalAlignment.Center };
                var val = Mono("", TxtC); val.HorizontalAlignment = HorizontalAlignment.Center;
                var nm = Caps(PrismInk.Names[bb]); nm.HorizontalAlignment = HorizontalAlignment.Center;
                readouts.Add(() =>
                {
                    bool act = BandActive(bb);
                    m.Soloed = act && Sel(Solo, 4) == bb + 1;
                    m.Set(act ? Sc(S_Gr0 + bb) : 0, act ? Sc(S_Boost0 + bb) : 0, On(PeakHold));
                    double gr = Sc(S_Gr0 + bb), bo = Sc(S_Boost0 + bb);
                    val.Text = !act ? "—" : bo > gr + 0.05 ? Inv($"+{bo:0.0}") : gr > 0.05 ? Inv($"−{gr:0.0}") : "0.0";
                    val.Foreground = act ? TxtC : DimC;
                    nm.Foreground = band == bb ? TxtC : act ? MutedC : DimC;
                    nm.FontWeight = band == bb ? FontWeight.SemiBold : FontWeight.Normal;
                    Inactive.Set(m, !act, interactive: true);
                });
                var col = new DockPanel { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Children = { Docked(nm, Dock.Bottom), Docked(val, Dock.Bottom), m } };
                ((Control)val).Margin = new Thickness(0, 6, 0, 2);
                col.PointerPressed += (_, e) => { if (e.GetCurrentPoint(col).Properties.IsLeftButtonPressed) SelectBand(bb); };
                meters.Children.Add(Col(col, b));
            }
            var scale = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,*,Auto,Auto"), Margin = new Thickness(4, 0, 0, 0),
                Children = { Caps("0"), GRow(Caps("−6"), 2), GRow(Caps("−12"), 4), GRow(new Border { Height = 30 }, 5) } };
            meters.Children.Add(Col(scale, 3));

            // Solo L / M / H (one band at a time; click the lit one to clear).
            var soloCells = new Border[3];
            var soloTxt = Caps(""); soloTxt.HorizontalAlignment = HorizontalAlignment.Right;
            void HiSolo()
            {
                int s = Sel(Solo, 4) - 1;
                for (int i = 0; i < 3; i++)
                {
                    bool on = s == i, act = BandActive(i);
                    soloCells[i].Background = on ? NotaPalette.AccentSoft : NotaPalette.SurfaceHover;
                    var t = (TextBlock)soloCells[i].Child!; t.Foreground = on ? AmberLit : act ? Txt2 : DimC; t.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                }
                soloTxt.Text = s >= 0 && BandActive(s) ? PrismInk.Names[s].ToLowerInvariant() : "none"; soloTxt.Foreground = s >= 0 ? TxtC : MutedC;
            }
            var soloRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            string[] sl = { "L", "M", "H" };
            for (int i = 0; i < 3; i++)
            {
                int iv = i;
                var c = new Border { Width = 22, Height = 20, CornerRadius = NotaRadius.Cell, Cursor = new Cursor(StandardCursorType.Hand),
                    Child = new TextBlock { Text = sl[i], FontSize = NotaType.RowLabel, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center } };
                c.BindResource(Border.BoxShadowProperty, "Shadow.Lifted");
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
            var soloGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 12 };
            soloGrid.Children.Add(Caps("Solo")); soloGrid.Children.Add(Col(soloRow, 1)); soloGrid.Children.Add(Col(soloTxt, 2));
            var bottom = Divider(new StackPanel { Spacing = 8, Children = { soloGrid, Toggle(PeakHold, "Peak hold") } });
            bottom.Margin = new Thickness(0, 6, 0, 0);
            var title = DeviceBodyKit.Title("Gain reduction"); title.Margin = new Thickness(0, 8, 0, 0);
            return new DockPanel { Children = { Docked(title, Dock.Top), Docked(bottom, Dock.Bottom), meters } };
        }
        Control OutputTab()
        {
            // 160 high: the knob and meter in one 36 row, the bars, sidechain and switches, soft clip.
            var top = DeviceBodyKit.OutputHead(K(Output, "Output", v => DbF((v - 0.5) * 48), 96, inline: true), () => Sc(S_OutL), () => Sc(S_OutR), readouts.Add);
            top.Margin = new Thickness(0, 4, 0, 0);

            // Sidechain source (None + every other track).
            var scName = new TextBlock { FontSize = NotaType.KnobValue, Foreground = TxtC, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            bool scOk = engine.DeviceAcceptsSidechain(track, di);
            string SrcName(int id) { string n = TrackNames.Of(engine, id); return n == "—" ? "None" : n; }
            readouts.Add(() =>
            {
                int src = scOk ? engine.DeviceSidechainSource(track, di) : -1;
                scName.Text = !scOk ? "n/a in a rack" : src < 0 ? "Own input" : SrcName(src);
                scName.Foreground = src >= 0 ? TxtC : Txt2;
            });
            var scBox = new Border { Height = 20, Background = NotaPalette.Groove, CornerRadius = NotaRadius.Field, Padding = new Thickness(8, 0),
                Cursor = new Cursor(StandardCursorType.Hand), IsEnabled = scOk,
                Child = new DockPanel { Children = { Docked(new Glyph(GlyphKind.ChevronDown, 8) { Foreground = MutedC, Margin = new Thickness(5, 0, 0, 0) }, Dock.Right), scName } } };
            ToolTip.SetTip(scBox, "Sidechain — key every band's detector from another track (split into the same bands)");
            scBox.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(scBox).Properties.IsLeftButtonPressed || !scOk) return;
                var fly = new MenuFlyout();
                int cur = engine.DeviceSidechainSource(track, di);
                void Item(string name, int id)
                {
                    var mi = new MenuItem { Header = name };
                    mi.ToggleType = MenuItemToggleType.Radio; mi.IsChecked = id == cur;
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
            scBox.BindResource(Border.BoxShadowProperty, "Shadow.Groove");
            var scRow = new Grid { ColumnDefinitions = new ColumnDefinitions("62,*"), VerticalAlignment = VerticalAlignment.Center };
            scRow.Children.Add(Caps("Sidechain")); scRow.Children.Add(Col(scBox, 1));
            var listen = Toggle(ScListen, "Listen");
            ToolTip.SetTip(listen, "Listen — hear the detector's key signal (the sidechain, or the input), per solo band");
            var mk = Toggle(AutoMakeup, "Auto makeup");
            ToolTip.SetTip(mk, "Auto makeup — each band gets back about half of its compression at 0\u2009dBFS");
            var toggles = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
            toggles.Children.Add(listen); toggles.Children.Add(Col(mk, 1));
            var mid = new Grid { RowDefinitions = new RowDefinitions("20,20,24,18"), Children = {
                SliderRow("Lookahead", Lookahead, v => Inv($"{v * 10:0.0}\u2009ms")),
                GRow(SliderRow("Mix", Mix, Pct), 1), GRow(scRow, 2), GRow(toggles, 3) } };

            var clipTxt = Mono("", TxtC); clipTxt.HorizontalAlignment = HorizontalAlignment.Right; clipTxt.FontSize = NotaType.RowLabel;
            readouts.Add(() =>
            {
                double c = Sc(S_ClipDb);
                clipTxt.Text = On(SoftClip) && c > 0.05 ? Inv($"−0.3\u2009dBFS · {c:0.0}") : "−0.3\u2009dBFS";
                clipTxt.Foreground = On(SoftClip) ? (c > 0.05 ? NotaPalette.Warning : TxtC) : MutedC;
            });
            var clipRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 6 };
            var clipSeg = Seg(SoftClip, new[] { "Off", "On" });
            ToolTip.SetTip(clipSeg, "Soft clip — a smooth ceiling at −0.3\u2009dBFS after the output gain");
            clipRow.Children.Add(Caps("Soft clip")); clipRow.Children.Add(Col(clipSeg, 1)); clipRow.Children.Add(Col(clipTxt, 2));
            var midB = Divider(mid, 4); midB.Margin = new Thickness(0, 4, 0, 0);
            return new DockPanel { LastChildFill = false, Children = {
                Docked(top, Dock.Top), Docked(midB, Dock.Top), Docked(Divider(clipRow, 6), Dock.Bottom) } };
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
            ToolTip.SetTip(seg, "Bands — 3: Low / Mid / High · 2: Low + Mid (Mid runs to 20\u2009kHz) · 1: Mid, full band");
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { Caps("Mode"), seg } };
        }
        Control BandChips()
        {
            // The picked band as segments (an inactive band cannot be picked: SelectBand ignores it).
            var seg = DeviceCardKit.Segments(PrismInk.Names, () => band, SelectBand, out var sync);
            readouts.Add(sync);
            return seg;
        }
        Control DetectExtras()
        {
            var seg = Seg(Detect, new[] { "Peak", "RMS" });
            ToolTip.SetTip(seg, "Detector — Peak follows every transient; RMS (10\u2009ms) follows loudness, smoother");
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { Caps("Detect"), seg } };
        }
        var extrasBodies = new Control?[3];
        void SetExtras(int t) => extras.Content = extrasBodies[t] ??= t switch { 1 => BandChips(), 2 => DetectExtras(), _ => ModeExtras() };
        Control CentreBody(int t) => centreBodies[t] ??= t switch { 1 => DetailTab(), 2 => TimeTab(), _ => BandsTab() };
        Control RightBody(int t) => rightBodies[t] ??= t == 1 ? OutputTab() : MetersTab();
        SetExtras(centreTab);
        var centreTabs = DeviceBodyKit.Tabs(new[] { "Bands", "Band detail", "Time" }, () => centreTab, t =>
        {
            centreTab = t; view = (band, t, view.Right); ViewState[(track, di)] = view; SetExtras(t);
            centreHost.Content = CentreBody(t);
            foreach (var a in readouts) a();
        }, out _, extras);
        centreHost.Content = CentreBody(centreTab);
        int rightTab = view.Right;
        var rightTabs = DeviceBodyKit.Tabs(new[] { "Meters", "Output" }, () => rightTab, t =>
        {
            rightTab = t; view = (band, view.Centre, t); ViewState[(track, di)] = view;
            rightHost.Content = RightBody(t);
            foreach (var a in readouts) a();
        }, out _);
        rightHost.Content = RightBody(rightTab);
        var right = DeviceBodyKit.Sec(new DockPanel { Children = { Docked(rightTabs, Dock.Top), rightHost } }, SectionPad, 190);
        DockPanel.SetDock(right, Dock.Right);
        var centreBox = DeviceBodyKit.Sec(new DockPanel { Children = { Docked(centreTabs, Dock.Top), centreHost } });
        centreBox.Margin = new Thickness(SectionGap, 0);

        // ---- assemble ---------------------------------------------------------------
        DockPanel.SetDock(globCol, Dock.Left);
        var root = DeviceBodyKit.Frame(new DockPanel { LastChildFill = true, Children = { globCol, right, centreBox } });

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
