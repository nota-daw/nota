// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Consort editor (instrument kind 15), 780 × 260 in three
// pressed-in sections (style guide, spec 038): an always-visible wheels column, a centre
// tabbed panel (Osc · Mix / Filters · Env / LFO · Delay / Seq / Patch) and a right tabbed
// panel (Voices / Output). The patch bay's cable count and its Open button (the entry to
// the full-card patch overlay) sit in the card header. The patch bay has
// three views of the same twelve cables: the source × destination matrix and the jack strip
// inside the Patch tab, and the overlay with all 42 points. Every control is a plugin-param
// (cables included), so automation / MIDI learn / presets / persistence come for free.
// BodyOnly — the shared shell draws the header (name / preset / A-B / meter).

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Nota.Application;
using static Nota.App.DeviceCardKit;
using static Nota.App.InstrumentCardKit;

namespace Nota.App;

internal sealed class ConsortInstrumentCard : IInstrumentCard
{
    private static readonly IBrush Border2 = NotaPalette.Rule;
    private static readonly IBrush BorderIn = NotaPalette.Rule;
    private static readonly IBrush AmberLit = NotaPalette.AccentBright;
    private static readonly IBrush TxtC = NotaPalette.TextPrimary;
    private static readonly IBrush Txt2 = NotaPalette.TextSecondary;
    private static readonly IBrush MutedC = NotaPalette.TextTertiary;
    private static readonly IBrush DimC = NotaPalette.TextDisabled;

    private static readonly string[] Feet = { "32′", "16′", "8′", "4′", "2′" };
    private static readonly string[] SyncNames = { "8 bar", "4 bar", "2 bar", "1 bar", "1/2", "1/4.", "1/4", "1/8.", "1/4T", "1/8", "1/8T", "1/16", "1/16T", "1/32" };
    private static readonly string[] SeqRates = { "1/4", "1/8", "1/8T", "1/16", "1/16T", "1/32" };
    private static readonly string[] LfoNames = { "sine", "ramp up", "ramp down", "square", "S&H", "smooth random" };
    private const int Cables = 12;

    // View state that survives a rebuild (preset change, undo): per track.
    private sealed class ViewState
    {
        public int Centre, Right, PatchView, LastNonPatch, Brush;
        public bool Overlay;
        public Control? Header;   // the patch-bay count and Open button, handed to the shell's header
        public readonly List<int> RowPrefs = new() { 1, 2, 10, 6, 15 };   // LFO out, Env 1, Seq pitch, Osc 3, Velocity
        public readonly List<int> ColPrefs = new() { 4, 9, 16, 10, 14 };   // Osc pitch, PWM, Delay time, Filt 1, VCA CV
    }
    private static readonly Dictionary<int, ViewState> Views = new();

    public bool BodyOnly => true;
    public string Subtitle => "Paraphonic";
    public double CardWidth => 780;

    // Build runs first with the same context: it leaves the patch bay's header part here.
    public Control? HeaderAccessory(DeviceCardContext ctx)
        => Views.TryGetValue(ctx.TrackId, out var v) ? v.Header : null;

    public string? VoiceLabel(IAudioEngine engine, int trackId, int active)
    {
        // Fixed layout (Consort.h): voicemode = 7, truepoly = 8; verified, with a scan fallback.
        float G(string id, int at)
        {
            if (engine.PluginParamId(trackId, -1, at) == id) return engine.PluginParamGet(trackId, -1, at);
            int n = engine.PluginParamCount(trackId, -1);
            for (int i = 0; i < n; i++) if (engine.PluginParamId(trackId, -1, i) == id) return engine.PluginParamGet(trackId, -1, i);
            return 0;
        }
        int a = Math.Max(0, active);
        if (G("truepoly", 8) > 0.5f) return $"Poly {a}/16";
        return Math.Clamp((int)Math.Round(G("voicemode", 7) * 2), 0, 2) switch { 0 => "Mono", 1 => $"Duo {a}/2", _ => $"Para {a}/4" };
    }

    public Control Build(DeviceCardContext ctx)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId;
        if (!Views.TryGetValue(track, out var vs)) Views[track] = vs = new ViewState();
        int pc = engine.PluginParamCount(track, -1);
        var idx = new Dictionary<string, int>();
        for (int i = 0; i < pc; i++) idx[engine.PluginParamId(track, -1, i)] = i;
        float G(string id) => idx.TryGetValue(id, out var i) ? engine.PluginParamGet(track, -1, i) : 0f;
        int I(string id) => idx.TryGetValue(id, out var i) ? i : -1;
        bool On(string id) => G(id) > 0.5f;
        int Sel(string id, int n) => Math.Clamp((int)Math.Round(G(id) * (n - 1)), 0, n - 1);
        void Begin(string id) => engine.BeginAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id);
        void End(string id) => engine.EndAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id);
        List<ConsortCable>? cableCache = null;   // one read of the 36 cable params per tick / edit
        void Raw(string id, float v) { if (I(id) is var i and >= 0) { engine.PluginParamSet(track, -1, i, Math.Clamp(v, 0f, 1f)); cableCache = null; } }
        // A discrete edit (click) is one automation gesture, so it records while the transport does.
        void SetP(string id, float v) { if (I(id) < 0) return; Begin(id); Raw(id, v); End(id); }

        var common = new List<Action>();
        var tabReadouts = new[] { new List<Action>(), new List<Action>(), new List<Action>(), new List<Action>(), new List<Action>() };
        var rightReadouts = new[] { new List<Action>(), new List<Action>() };
        var overlayReadouts = new List<Action>();
        List<Action> cur = common;   // helpers register their refreshers here while a body is built
        var scope = new float[32];
        int scN = 0;
        Action refreshAll = () => { };
        Action patchShow = () => { };
        void Refresh() => refreshAll();

        // ---- small builders --------------------------------------------------------
        static TextBlock Caps(string t) => Cap(t);
        // A read-out; sizes under the device scale's floor are lifted to it (values 11, notes 10.5).
        static TextBlock MonoText(string t, double fs, IBrush c)
        { var tb = new TextBlock { Text = t, FontSize = fs < 9.5 ? NotaType.RowLabel : fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.UI"); return tb; }
        static StackPanel Row(double sp, params Control[] cs)
        { var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = sp, VerticalAlignment = VerticalAlignment.Center }; foreach (var c in cs) s.Children.Add(c); return s; }
        static Control Docked(Control c, Dock d) { DockPanel.SetDock(c, d); return c; }   // (also InstrumentCardKit.Docked)
        static Control Col(Control c, int col) { Grid.SetColumn(c, col); return c; }
        static Control RowAt(Control c, int r) { Grid.SetRow(c, r); return c; }
        static Border Divider(Control child, double top = 5) => new() { BorderBrush = BorderIn, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, top, 0, 0), Child = child };
        static Control Right(Control c) { c.HorizontalAlignment = HorizontalAlignment.Right; return c; }
        static Control Center(Control c) { c.HorizontalAlignment = HorizontalAlignment.Center; return c; }

        Control K(string id, string name, Func<float, string> fmt, bool mod = false, double sz = Knob.Size, double cw = 48, bool inline = false)
            => InstrumentControls.InstKnob(ctx, idx, id, name, Refresh, fmt, sz, inline ? Math.Max(cw, 56) : cw, null, inline);

        // A compact horizontal knob: [inline knob] value — for the oscillator table.
        Control MiniKnob(string id, Func<float, string> fmt, double valW, IBrush valColor)
        {
            if (I(id) is not (var i and >= 0)) return new Panel();
            var value = MonoText(fmt(G(id)), NotaType.KnobValue, valColor); value.Width = valW;
            var knob = new Knob(G(id), 1.0) { Accent = true, Inline = true, Default = engine.InstrumentParamDefault(track, i) };
            knob.Format = v => fmt((float)v);
            knob.ValueChanged += v => { engine.PluginParamSet(track, -1, i, (float)v); value.Text = fmt((float)v); Refresh(); };
            knob.GestureBegin += () => Begin(id);
            knob.GestureEnd += () => End(id);
            ctx.AddInstFader(i, knob, value, fmt);
            MidiLearn.Bind(knob, MidiTarget.PluginParam(track, -1, i), id);
            ValueEntry.Attach(knob, engine.PluginParamName(track, -1, i), value);
            return Row(6, knob, value);
        }

        // On/off pill toggle backed by a param (> 0.5 = on).
        Control Toggle(string id, string label, Func<bool>? dim = null, string? learn = null)
        {
            var wrap = Switch(label, () => On(id), () => { SetP(id, On(id) ? 0f : 1f); Refresh(); }, out var sync, dim);
            cur.Add(sync);
            if (I(id) is var pi and >= 0) MidiLearn.Bind(wrap, MidiTarget.PluginParam(track, -1, pi), learn ?? (label.Length > 0 ? label : id));
            return wrap;
        }

        // Segmented chips. values[i] is written on click; the lit chip is the nearest value.
        Control Chips(string id, string[] names, float[]? values = null, double fs = 7, Func<bool>? dim = null, double padX = 5)
        {
            var vals = values ?? BuildValues(names.Length);
            var seg = DeviceCardKit.Segments(names, () => DeviceCardKit.NearestExact(G(id), vals), iv => { SetP(id, vals[iv]); Refresh(); }, out var sync, dim: dim, padX: padX);
            cur.Add(sync);
            if (I(id) is var pi and >= 0) MidiLearn.Bind(seg, MidiTarget.PluginParam(track, -1, pi), id);
            return seg;
        }
        static float[] BuildValues(int n) { var v = new float[n]; for (int i = 0; i < n; i++) v[i] = n > 1 ? i / (float)(n - 1) : 0f; return v; }

        // A segmented control over view state (not a param).
        Control ViewSeg(string[] names, Func<int> get, Action<int> set)
        {
            var seg = DeviceCardKit.Segments(names, get, set, out var sync, padX: 5);
            common.Add(sync);
            return seg;
        }

        // Horizontal fill slider bound to a param; `dim` greys it out.
        Control HSlider(string id, Func<double, string> fmt, double valW = 24, Func<bool>? dim = null, bool bipolar = false)
        {
            int pi = I(id);
            var row = DeviceCardKit.SliderRow("", () => G(id), n => { if (pi >= 0) engine.PluginParamSet(track, -1, pi, (float)n); Refresh(); }, () => fmt(G(id)), out var sync,
                begin: () => { if (pi >= 0) Begin(id); }, end: () => { if (pi >= 0) End(id); },
                reset: pi >= 0 ? () => { SetP(id, engine.InstrumentParamDefault(track, pi)); Refresh(); } : null,
                bipolar: bipolar, dim: dim, valueWidth: valW);
            cur.Add(sync);
            if (pi >= 0) MidiLearn.Bind(row, MidiTarget.PluginParam(track, -1, pi), id);
            return row;
        }

        // ---- formatters -------------------------------------------------------------
        static double Exp(double lo, double hi, double v) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0, 1));
        static string Time(double s) => s >= 1 ? $"{s:0.0}\u2009s" : s >= 0.0995 ? $"{s * 1000:0}\u2009ms" : $"{s * 1000:0.#}\u2009ms";
        static string ShortTime(double s) => s >= 1 ? $"{s:0.0}\u2009s" : $"{s * 1000:0}\u2009ms";
        static string Pct(double v) => $"{v * 100:0}\u2009%";
        static string Tenths(double v) => $"{v * 10:0.0}";
        static string HzFmt(double hz) => hz >= 1000 ? $"{hz / 1000:0.0}\u2009k" : $"{hz:0}\u2009Hz";
        static string Signed(double v, string f) => Math.Abs(v) < 0.005 ? (0.0).ToString(f) : v.ToString("+" + f + ";-" + f);
        string Foot(float v) => Feet[Math.Clamp((int)Math.Round(v * 4), 0, 4)];
        string Freq(float v) => Signed((v - 0.5) * 14, "0.00");
        string Cut(float v) => HzFmt(20 * Math.Pow(1000, v));
        string Space(float v) => Signed((v - 0.5) * 6, "0.0") + "\u2009oct";
        string EnvAmt(float v) => Signed((v - 0.5) * 20, "0.0");
        string Atk(float v) => ShortTime(Exp(0.001, 10, v));
        string Dec(float v) => ShortTime(Exp(0.003, 15, v));
        string GlideF(double v) => v < 0.002 ? "off" : Time(Exp(0.005, 5, v));
        string LfoRate(float v) => On("lfosync") ? SyncNames[Math.Clamp((int)Math.Round(v * 13), 0, 13)] : $"{Exp(0.05, 30, v):0.0#}\u2009Hz";
        static double LfoCentsOf(double v) { double b = (v - 0.5) * 2; return b * Math.Abs(b) * 1200; }
        string LfoCents(float v) { double c = LfoCentsOf(v); return Math.Abs(c) < 0.5 ? "0\u2009c" : $"{c:+0;−0}\u2009c"; }
        static double DlySec(double v) => Exp(0.02, 1.5, v);
        string DlyMs(float v) => $"{DlySec(v) * 1000:0}\u2009ms";
        string DlySpc(float v) => $"{(v - 0.5) * 1000:+0;−0;0}\u2009ms";
        string VolDb(float v) { double g = 2 * v * v; return g <= 1e-4 ? "−∞" : $"{20 * Math.Log10(g):0.0}\u2009dB"; }
        string Tune(double v) => Signed((v - 0.5) * 4, "0.0") + "\u2009st";
        int BendSt() => 1 + Math.Clamp((int)Math.Round(G("bendrange") * 11), 0, 11);
        string Depth(double v) => $"{(v - 0.5) * 200:+0;−0;0}";

        // ======================================================================
        // Patch-bay model: 12 cable slots of (source, destination, depth) params
        // ======================================================================
        string SrcId(int k) => $"c{k + 1}src";
        string DstId(int k) => $"c{k + 1}dst";
        string AmtId(int k) => $"c{k + 1}amt";
        List<ConsortCable> CableList() => cableCache ??= ReadCables();
        List<ConsortCable> ReadCables()
        {
            var l = new List<ConsortCable>();
            for (int k = 0; k < Cables; k++)
            {
                int s = ConsortJacks.FromNorm(G(SrcId(k))), d = ConsortJacks.FromNorm(G(DstId(k)));
                if (s <= 0 || d <= 0 || s >= ConsortJacks.Sources.Length || d >= ConsortJacks.Dests.Length) continue;
                l.Add(new ConsortCable(k, s, d, (G(AmtId(k)) - 0.5f) * 2f));
            }
            return l;
        }
        int FindCable(int s, int d) { foreach (var c in CableList()) if (c.Src == s && c.Dst == d) return c.Slot; return -1; }
        bool AddCable(int s, int d, float depth = 0.5f)
        {
            int slot = FindCable(s, d);
            if (slot >= 0) { SetP(AmtId(slot), 0.5f + depth / 2); Refresh(); return true; }
            var used = CableList().Select(c => c.Slot).ToHashSet();
            for (int k = 0; k < Cables; k++)
            {
                if (used.Contains(k)) continue;
                SetP(SrcId(k), ConsortJacks.ToNorm(s)); SetP(DstId(k), ConsortJacks.ToNorm(d)); SetP(AmtId(k), 0.5f + depth / 2);
                Refresh(); return true;
            }
            return false;   // all twelve slots in use
        }
        void RemoveSlot(int k) { SetP(SrcId(k), 0f); SetP(DstId(k), 0f); SetP(AmtId(k), 0.75f); Refresh(); }
        void RemoveAtJack(ConsortJacks.Jack j) { foreach (var c in CableList()) if (j.Out ? c.Src == j.Index : c.Dst == j.Index) RemoveSlot(c.Slot); }
        void ClearAll() { foreach (var c in CableList()) RemoveSlot(c.Slot); }
        IBrush? PatchedAt(bool isOut, params int[] jacks)
        {
            foreach (var c in CableList())
                if (isOut ? jacks.Contains(c.Src) : jacks.Contains(c.Dst)) return ConsortJacks.ToneBrush(ConsortJacks.Src(c.Src).Tone);
            return null;
        }
        string CableName(ConsortCable c) => $"{ConsortJacks.Src(c.Src).Name} → {ConsortJacks.Dst(c.Dst).Name}";

        // Right-click / click on a jack: its cables (remove) + quick patch to any opposite jack.
        void JackMenu(ConsortJacks.Jack j, Control anchor)
        {
            var f = new MenuFlyout();
            f.Items.Add(MenuKit.Header(j.Name, null, $"{(j.Out ? "Output" : "Input")}{(ConsortJacks.Normalled.Contains(j.Index) && !j.Out ? " · normalled" : "")}"));
            f.Items.Add(new Separator());
            var sub = new MenuItem { Header = j.Out ? "Patch to" : "Patch from" };
            var list = j.Out ? ConsortJacks.Dests : ConsortJacks.Sources;
            for (int i = 1; i < list.Length; i++)
            {
                var o = list[i];
                var mi = new MenuItem { Header = o.Name };
                mi.Click += (_, _) => { if (j.Out) AddCable(j.Index, o.Index); else AddCable(o.Index, j.Index); };
                sub.Items.Add(mi);
            }
            f.Items.Add(sub);
            // Removing a cable is destructive: last, in danger ink.
            var mine = CableList().Where(c => j.Out ? c.Src == j.Index : c.Dst == j.Index).ToList();
            if (mine.Count > 0) f.Items.Add(new Separator());
            foreach (var c in mine)
            {
                int slot = c.Slot;
                f.Items.Add(MenuKit.Danger($"Remove {CableName(c)}  {c.Depth * 100:+0;−0;0}", GlyphKind.Trash, () => RemoveSlot(slot)));
            }
            f.ShowAt(anchor, showAtPointer: true);
        }

        // ======================================================================
        // LEFT — wheels
        // ======================================================================
        var bendWheel = Wheel(ctx, idx, "bend", 0.5, true, Refresh, common);
        var modWheel = Wheel(ctx, idx, "modwheel", 0, false, Refresh, common);
        var bendTxt = Value(); var modTxt = Value();
        var pitchCap = Cap("Pitch"); var modCap = Cap("Mod");
        common.Add(() =>
        {
            bendTxt.Text = $"±{BendSt()}\u2009st"; modTxt.Text = $"{G("modwheel") * 100:0}\u2009%";
            modTxt.Foreground = G("modwheel") > 0.01f ? TxtC : MutedC;
            pitchCap.Foreground = Math.Abs(G("bend") - 0.5f) > 0.01f ? Txt2 : MutedC;
            modCap.Foreground = G("modwheel") > 0.01f ? Txt2 : MutedC;
        });
        var wheels = WheelsPanel(bendWheel, modWheel, bendTxt, modTxt, pitchCap, modCap);
        DockPanel.SetDock(wheels, Avalonia.Controls.Dock.Left);

        // ======================================================================
        // CENTRE — Osc · Mix
        // ======================================================================
        const string OscCols = "18,60,108,*,96";
        Control Cell(Control c, int col, HorizontalAlignment ha = HorizontalAlignment.Center) { c.HorizontalAlignment = ha; c.VerticalAlignment = VerticalAlignment.Center; return Col(new Panel { Children = { c } }, col); }
        // A groove of icon cells (waves, LFO shapes): the picked one raised, its icon orange.
        Control IconStrip(string id, int n, Func<int, Control> icon, Action<Control, IBrush> ink, string[] tips, double cellW)
        {
            var row = new Grid { ColumnSpacing = 2 };
            var chips = new Border[n]; var icons = new Control[n];
            for (int w = 0; w < n; w++)
            {
                int wv = w;
                row.ColumnDefinitions.Add(new ColumnDefinition(cellW, GridUnitType.Pixel));
                var ic = icon(w); ic.HorizontalAlignment = HorizontalAlignment.Center; ic.VerticalAlignment = VerticalAlignment.Center;
                var b = new Border { Height = NotaSize.Seg, CornerRadius = NotaRadius.Cell, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = ic };
                ToolTip.SetTip(b, tips[w]);
                b.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return;
                    SetP(id, n > 1 ? wv / (float)(n - 1) : 0f); Refresh(); e.Handled = true;
                };
                Grid.SetColumn(b, w);
                chips[w] = b; icons[w] = ic; row.Children.Add(b);
            }
            cur.Add(() =>
            {
                int s = Sel(id, n);
                for (int w = 0; w < n; w++) { bool on = w == s; PaintCell(chips[w], on); ink(icons[w], on ? AmberLit : Txt2); icons[w].InvalidateVisual(); }
            });
            var groove = Groove(row);
            groove.HorizontalAlignment = HorizontalAlignment.Left;
            if (I(id) is var pi and >= 0) MidiLearn.Bind(groove, MidiTarget.PluginParam(track, -1, pi), id);
            return groove;
        }
        Control WaveSel(string id)
        {
            int[] icon = { 2, 0, 3, 1 };   // triangle, saw, square, pulse (PentadWaveIcon glyphs)
            string[] tip = { "Triangle", "Saw", "Square", "Pulse (width = PWM)" };
            return IconStrip(id, 4, w => new PentadWaveIcon(icon[w], 16, 9), (c, b) => ((PentadWaveIcon)c).Stroke = b, tip, 28);
        }
        // Hard sync: a word that lights when on (click toggles).
        Control SyncChip(string id, string text)
        {
            var tb = Cap(text);
            var b = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = tb };
            ToolTip.SetTip(b, "Hard sync — click to switch");
            b.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; SetP(id, On(id) ? 0f : 1f); Refresh(); e.Handled = true; };
            cur.Add(() => tb.Foreground = On(id) ? AmberLit : MutedC);
            if (I(id) is var pi and >= 0) MidiLearn.Bind(b, MidiTarget.PluginParam(track, -1, pi), id);
            return b;
        }
        Control OscRow(int n)
        {
            string p = $"o{n}";
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions(OscCols) };
            g.Children.Add(Cell(Title(n.ToString()), 0, HorizontalAlignment.Left));
            g.Children.Add(Cell(MiniKnob($"{p}oct", Foot, 24, TxtC), 1, HorizontalAlignment.Left));
            g.Children.Add(n == 1 ? Cell(Value("master", Txt2), 2, HorizontalAlignment.Left) : Cell(MiniKnob($"{p}freq", Freq, 44, TxtC), 2, HorizontalAlignment.Left));
            g.Children.Add(Cell(WaveSel($"{p}wave"), 3));
            var jd = new ConsortJackDot(11);
            ToolTip.SetTip(jd, $"Osc {n} pitch input (patch bay)");
            cur.Add(() => jd.Set(PatchedAt(false, 4 + n, 4)));
            Control right = n == 2 ? SyncChip("o2sync", "sync→1") : n == 4 ? SyncChip("o4sync", "sync→3") : Cap("—", DimC);
            var jr = Row(6, jd, right); jr.Margin = new Thickness(40, 0, 0, 0);
            g.Children.Add(Cell(jr, 4, HorizontalAlignment.Left));
            return new Border { BorderBrush = BorderIn, BorderThickness = new Thickness(0, 1, 0, 0), Child = g };
        }
        // A mixer cell: name · a short value bar · its value, right-aligned.
        Control MixCell(string label, string id)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("38,*"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            g.Children.Add(Cap(label));
            var bar = ParamRow(ctx, idx, id, "", v => Tenths(v), Refresh, cur, trackWidth: 36, valueWidth: 20, learnName: label);
            g.Children.Add(Col(bar, 1));
            return g;
        }
        Control OscTab()
        {
            var hdr = new Grid { ColumnDefinitions = new ColumnDefinitions(OscCols), Height = 19 };
            string[] h = { "#", "Oct", "Freq", "Wave", "Sync / CV" };
            for (int i = 0; i < h.Length; i++)
            {
                var t = Cap(h[i], DimC);
                t.HorizontalAlignment = i <= 2 ? HorizontalAlignment.Left : i == h.Length - 1 ? HorizontalAlignment.Right : HorizontalAlignment.Center;
                hdr.Children.Add(Col(t, i));
            }
            var rows = new StackPanel();
            for (int n = 1; n <= 4; n++) { var r = OscRow(n); r.Height = 24; rows.Children.Add(r); }
            var mix = new Grid { ColumnDefinitions = new ColumnDefinitions("39,108,108,108,*"), RowDefinitions = new RowDefinitions("*,*"), Height = 44, Margin = new Thickness(0, 4, 0, 0) };
            var mixLbl = Cap("Mixer"); Grid.SetRowSpan(mixLbl, 2); mix.Children.Add(mixLbl);
            mix.Children.Add(Col(MixCell("Osc 1", "mix1"), 1)); mix.Children.Add(RowAt(Col(MixCell("Osc 2", "mix2"), 1), 1));
            mix.Children.Add(Col(MixCell("Osc 3", "mix3"), 2)); mix.Children.Add(RowAt(Col(MixCell("Osc 4", "mix4"), 2), 1));
            mix.Children.Add(Col(MixCell("Noise", "mixnoise"), 3)); mix.Children.Add(RowAt(Col(MixCell("Ext", "mixext"), 3), 1));
            var ext = mix.Children[^1]; ToolTip.SetTip(ext, "Ext: normalled to the VCA output (Moog feedback) — patch Ext in to replace it");
            var drive = Toggle("mixdrive", "Drive", learn: "Mixer drive"); drive.HorizontalAlignment = HorizontalAlignment.Right;
            mix.Children.Add(Col(drive, 4));
            var noise = Chips("noisecolor", new[] { "White", "Pink" }); noise.HorizontalAlignment = HorizontalAlignment.Right;
            mix.Children.Add(RowAt(Col(noise, 4), 1));
            return new DockPanel { LastChildFill = true, Children = {
                Docked(hdr, Avalonia.Controls.Dock.Top),
                Docked(rows, Avalonia.Controls.Dock.Top),
                Docked(new Border { BorderBrush = BorderIn, BorderThickness = new Thickness(0, 1, 0, 0), Child = mix }, Avalonia.Controls.Dock.Top),
                new Panel() } };
        }

        // ======================================================================
        // CENTRE — Filters · Env
        // ======================================================================
        Control FilterPanel()
        {
            var curve = new ConsortFilterCurve();
            curve.Changed = (c, r) => { Raw("cutoff", (float)c); Raw("reso", (float)r); Refresh(); };
            curve.DragStarted += () => { Begin("cutoff"); Begin("reso"); };
            curve.DragEnded += () => { End("cutoff"); End("reso"); };
            cur.Add(() => curve.Set(G("cutoff"), G("reso"), (G("spacing") - 0.5) * 6, Sel("filtmode", 3), On("basscomp")));
            var graph = GraphWindow(curve); graph.Margin = new Thickness(0, 6, 0, 4);
            var mode = DeviceCardKit.Segments(new[] { "HP/LP ser", "LP/LP st", "HP/LP st" }, () => Sel("filtmode", 3), iv => { SetP("filtmode", iv / 2f); Refresh(); }, out var modeSync, fill: true, padX: 3);
            cur.Add(modeSync);
            ToolTip.SetTip(mode, "Dual ladder, 24 dB/oct: high-pass into low-pass (series), or two low-passes / HP + LP split across the stereo field");
            if (I("filtmode") is var fm and >= 0) MidiLearn.Bind(mode, MidiTarget.PluginParam(track, -1, fm), "filtmode");
            var knobs = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*") };
            knobs.Children.Add(K("cutoff", "Cutoff", Cut));
            knobs.Children.Add(Col(K("reso", "Reso", v => Tenths(v)), 1));
            knobs.Children.Add(Col(K("spacing", "Spacing", Space), 2));
            knobs.Children.Add(Col(K("fenvamt", "Envelope", EnvAmt), 3));
            var j1 = new ConsortJackDot(11); var j2 = new ConsortJackDot(11);
            ToolTip.SetTip(j1, "Filt 1 cutoff input"); ToolTip.SetTip(j2, "Filt 2 cutoff input");
            cur.Add(() => { j1.Set(PatchedAt(false, 10)); j2.Set(PatchedAt(false, 11)); });
            var kbd = Chips("kbdtrk", new[] { "0", "½", "1" }, null, 7, null, 4);
            ToolTip.SetTip(kbd, "Keyboard tracking of the cutoff");
            var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), ColumnSpacing = 10, Height = 24, Margin = new Thickness(0, 4, 0, 0) };
            bottom.Children.Add(Row(6, Cap("Kbd"), kbd));
            bottom.Children.Add(Col(Toggle("basscomp", "Bass comp"), 1));
            bottom.Children.Add(Col(Row(4, j1, j2), 3));
            var body = new DockPanel { LastChildFill = true, Children = { Docked(mode, Avalonia.Controls.Dock.Top), Docked(bottom, Avalonia.Controls.Dock.Bottom), Docked(knobs, Avalonia.Controls.Dock.Bottom), graph } };
            return new Border { Width = 216, BorderBrush = BorderIn, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(0, 6, 12, 0), Child = body };
        }
        Control EnvPanel(string title, string pre)
        {
            var curve = new PentadEnvCurve { Accent = NotaPalette.VizInk };
            cur.Add(() => { curve.Set(G(pre + "attack"), G(pre + "decay"), G(pre + "sustain"), G(pre + "release")); curve.InvalidateVisual(); });
            var head = Cap(title); head.Height = 20;
            var graph = GraphWindow(curve);
            var knobs = new Grid { ColumnDefinitions = new ColumnDefinitions("68,68"), RowDefinitions = new RowDefinitions("*,*"), Margin = new Thickness(6, 0, 0, 0) };
            knobs.Children.Add(K(pre + "attack", "A", Atk, false, Knob.SizeInline, 68, true));
            knobs.Children.Add(Col(K(pre + "decay", "D", Dec, false, Knob.SizeInline, 68, true), 1));
            knobs.Children.Add(RowAt(K(pre + "sustain", "S", v => Pct(v), false, Knob.SizeInline, 68, true), 1));
            knobs.Children.Add(RowAt(Col(K(pre + "release", "R", Dec, false, Knob.SizeInline, 68, true), 1), 1));
            return new DockPanel { LastChildFill = true, Children = { Docked(head, Avalonia.Controls.Dock.Top), Docked(knobs, Avalonia.Controls.Dock.Right), graph } };
        }
        Control FilterEnvTab()
        {
            var envs = new Grid { RowDefinitions = new RowDefinitions("*,*"), Margin = new Thickness(12, 2, 0, 0) };
            var fe = EnvPanel("Filter envelope", "f"); fe.Margin = new Thickness(0, 0, 0, 5);
            var ae = Divider(EnvPanel("Amp envelope", "a"), 2); Grid.SetRow(ae, 1);
            envs.Children.Add(fe); envs.Children.Add(ae);
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            g.Children.Add(FilterPanel()); g.Children.Add(Col(envs, 1));
            return g;
        }

        // ======================================================================
        // CENTRE — LFO · Delay
        // ======================================================================
        Control LfoPanel()
        {
            var shapes = IconStrip("lfowave", 6, s => new ConsortLfoIcon(s), (c, b) => ((ConsortLfoIcon)c).Stroke = b, LfoNames, 27);
            var jo = new ConsortJackDot(12); var jr = new ConsortJackDot(12);
            ToolTip.SetTip(jo, "LFO out (patch bay)"); ToolTip.SetTip(jr, "LFO rate input (patch bay)");
            cur.Add(() => { jo.Set(PatchedAt(true, 1)); jr.Set(PatchedAt(false, 1)); });
            var head = new DockPanel { Height = 22, LastChildFill = false, Children = { Docked(Cap("LFO"), Avalonia.Controls.Dock.Left), Docked(Row(4, jo, jr), Avalonia.Controls.Dock.Right) } };
            var knobs = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), Margin = new Thickness(0, 8, 0, 0) };
            knobs.Children.Add(K("lforate", "Rate", LfoRate));
            knobs.Children.Add(Col(K("lfopitch", "Pitch", LfoCents), 1));
            knobs.Children.Add(Col(K("lfocut", "Cutoff", v => Pct(v)), 2));
            knobs.Children.Add(Col(K("lfopwm", "PWM", v => Pct(v)), 3));
            // The switch word carries the synced division ("Sync 1/4"), refreshed with the card.
            var syncT = Switch("Sync", () => On("lfosync"), () => { SetP("lfosync", On("lfosync") ? 0f : 1f); Refresh(); }, out var syncPaint,
                liveLabel: () => On("lfosync") ? $"Sync {SyncNames[Math.Clamp((int)Math.Round(G("lforate") * 13), 0, 13)]}" : "Sync");
            cur.Add(syncPaint);
            if (I("lfosync") is var psync and >= 0) MidiLearn.Bind(syncT, MidiTarget.PluginParam(track, -1, psync), "Sync");
            var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Height = 24 };
            var dest = Chips("lfodest", new[] { "All osc", "2 + 4" }, null, 7, null, 4);
            bottom.Children.Add(syncT); bottom.Children.Add(Col(dest, 1));
            ToolTip.SetTip(dest, "LFO pitch → all oscillators, or only 2 and 4");
            var body = new DockPanel { LastChildFill = false, Children = { Docked(head, Avalonia.Controls.Dock.Top), Docked(shapes, Avalonia.Controls.Dock.Top), Docked(knobs, Avalonia.Controls.Dock.Top), Docked(Divider(bottom, 4), Avalonia.Controls.Dock.Bottom) } };
            return new Border { Width = 202, BorderBrush = BorderIn, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(0, 4, 12, 0), Child = body };
        }
        Control DelayPanel()
        {
            var view = new ConsortDelayView();
            var times = Cap("");
            cur.Add(() =>
            {
                double tl = DlySec(G("dlytime")), tr = Math.Clamp(tl + (G("dlyspacing") - 0.5), 0.005, 2.0);
                view.Set(tl, tr, G("dlyfb"), G("dlymix"), On("dlyping"), On("dlydigital"));
                times.Text = $"L {tl * 1000:0} · R {tr * 1000:0} ms";
            });
            var title = Cap("");
            cur.Add(() => title.Text = On("dlydigital") ? "Delay · digital" : "Analog delay · BBD");
            var head = new DockPanel { Height = 22, LastChildFill = false, Children = { Docked(title, Avalonia.Controls.Dock.Left), Docked(times, Avalonia.Controls.Dock.Right) } };
            var graph = GraphWindow(view); graph.Margin = new Thickness(0, 0, 0, 4);
            var knobs = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*") };
            knobs.Children.Add(K("dlytime", "Time", DlyMs, false, Knob.Size, 56));
            knobs.Children.Add(Col(K("dlyspacing", "Spacing", DlySpc, false, Knob.Size, 56), 1));
            knobs.Children.Add(Col(K("dlyfb", "Feedback", v => Pct(v), false, Knob.Size, 56), 2));
            knobs.Children.Add(Col(K("dlymix", "Mix", v => Pct(v), false, Knob.Size, 56), 3));
            var opts = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Height = 24, Margin = new Thickness(0, 4, 0, 0) };
            opts.Children.Add(Chips("dlyping", new[] { "Ping", "Stereo" }, new[] { 1f, 0f }, 7, null, 5));
            var dig = Toggle("dlydigital", "Digital"); dig.HorizontalAlignment = HorizontalAlignment.Right;
            opts.Children.Add(Col(dig, 1));
            return new DockPanel { LastChildFill = true, Margin = new Thickness(12, 4, 0, 0), Children = { Docked(head, Avalonia.Controls.Dock.Top), Docked(opts, Avalonia.Controls.Dock.Bottom), Docked(knobs, Avalonia.Controls.Dock.Bottom), graph } };
        }
        Control LfoDelayTab()
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            g.Children.Add(LfoPanel()); g.Children.Add(Col(DelayPanel(), 1));
            return g;
        }

        // ======================================================================
        // CENTRE — Seq
        // ======================================================================
        int[] StepTypes() { var t = new int[16]; for (int s = 0; s < 16; s++) t[s] = Math.Clamp((int)Math.Round(G($"st{s + 1}") * 3), 0, 3); return t; }
        int[] StepPitches() { var p = new int[16]; for (int s = 0; s < 16; s++) p[s] = Math.Clamp((int)Math.Round((G($"sp{s + 1}") - 0.5) * 48), -24, 24); return p; }
        int SeqLen() => 1 + Math.Clamp((int)Math.Round(G("seqlen") * 15), 0, 15);
        int Ratchet() => 2 + Math.Clamp((int)Math.Round(G("seqratchet") * 2), 0, 2);
        int PlayStep() => scN > 7 ? (int)scope[7] : -1;
        Control SeqTab()
        {
            // length: a draggable number
            var lenTxt = Value("");
            var lenBox = new Border { Padding = new Thickness(3, 0), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.SizeNorthSouth), Child = lenTxt };
            ToolTip.SetTip(lenBox, "Sequence length — drag up / down");
            { bool drag = false; double y0 = 0; float v0 = 0;
              lenBox.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(lenBox).Properties.IsLeftButtonPressed) return; drag = true; y0 = e.GetPosition(lenBox).Y; v0 = G("seqlen"); Begin("seqlen"); e.Pointer.Capture(lenBox); e.Handled = true; };
              lenBox.PointerMoved += (_, e) => { if (!drag) return; float v = Math.Clamp(v0 + (float)(y0 - e.GetPosition(lenBox).Y) / 120f, 0, 1); v = (float)Math.Round(v * 15) / 15f; Raw("seqlen", v); Refresh(); };
              lenBox.PointerReleased += (_, e) => { if (drag) { drag = false; End("seqlen"); e.Pointer.Capture(null); } };
              lenBox.PointerCaptureLost += (_, _) => { if (drag) { drag = false; End("seqlen"); } }; }
            tabReadouts[3].Add(() => lenTxt.Text = $"{SeqLen()}");
            // legend = paint brushes: a groove of cells, the current brush raised
            string[] names = { "Note", "Ratchet", "Tie", "Rest" };
            IBrush[] cols = { NotaPalette.VizInk, NotaPalette.TextSecondary, NotaPalette.TextTertiary, NotaPalette.TextDisabled };
            var legendRow = new StackPanel { Orientation = Orientation.Horizontal };
            var legItems = new Border[4]; var legText = new TextBlock[4];
            for (int b = 0; b < 4; b++)
            {
                int bv = b;
                var sw = new Border { Width = 8, Height = 8, CornerRadius = NotaRadius.Cell, BorderBrush = cols[b], BorderThickness = new Thickness(1), Background = b == 3 ? Brushes.Transparent : cols[b], VerticalAlignment = VerticalAlignment.Center };
                legText[b] = new TextBlock { Text = names[b], FontSize = NotaType.KnobValue, VerticalAlignment = VerticalAlignment.Center };
                var it = new Border { Height = NotaSize.Seg, Padding = new Thickness(6, 0), CornerRadius = NotaRadius.Cell, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = Row(5, sw, legText[b]) };
                ToolTip.SetTip(it, $"Paint {names[b].ToLowerInvariant()} steps (Alt-click a step: rest)");
                it.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(it).Properties.IsLeftButtonPressed) return; vs.Brush = bv; Refresh(); e.Handled = true; };
                legItems[b] = it; legendRow.Children.Add(it);
            }
            tabReadouts[3].Add(() => { for (int b = 0; b < 4; b++) { bool on = b == vs.Brush; PaintCell(legItems[b], on); legText[b].Foreground = on ? AmberLit : MutedC; } });
            var legend = Groove(legendRow); legend.HorizontalAlignment = HorizontalAlignment.Left;
            List<Action> saved = cur; cur = tabReadouts[3];
            var ratchet = Chips("seqratchet", new[] { "×2", "×3", "×4" }, null, 7, null, 5);
            ToolTip.SetTip(ratchet, "Ratchet repeats per step");
            var mode = Chips("seqmode", new[] { "Off", "Seq", "Arp" });
            ToolTip.SetTip(mode, "Off · Seq (steps transposed by the key) · Arp (the held chord over 1–3 octaves)");
            var oct = Chips("arpoct", new[] { "1", "2", "3" }, null, 7, () => Sel("seqmode", 3) != 2, 6);
            var octCap = Cap("Oct");
            tabReadouts[3].Add(() => octCap.Foreground = Sel("seqmode", 3) != 2 ? DimC : MutedC);

            var clkDot = new ConsortJackDot(12); var gateDot = new ConsortJackDot(12);
            ToolTip.SetTip(clkDot, "Clock out (patch bay)"); ToolTip.SetTip(gateDot, "Seq gate out (patch bay)");
            tabReadouts[3].Add(() => { clkDot.Set(PatchedAt(true, 12)); gateDot.Set(PatchedAt(true, 11, 10)); });
            var top = new DockPanel { Height = 26, LastChildFill = false };
            top.Children.Add(Docked(Row(10, mode, Row(6, octCap, oct)), Avalonia.Controls.Dock.Left));
            top.Children.Add(Docked(Row(14, Row(2, Cap("Steps"), lenBox, Cap("/ 16")), Row(4, Cap("Clk"), gateDot, clkDot)), Avalonia.Controls.Dock.Right));
            var paint = new DockPanel { Height = 26, LastChildFill = false };
            paint.Children.Add(Docked(legend, Avalonia.Controls.Dock.Left));
            paint.Children.Add(Docked(ratchet, Avalonia.Controls.Dock.Right));

            var grid = new ConsortStepGrid { Height = 22, Margin = new Thickness(0, 4, 0, 4) };
            var gestureIds = new HashSet<string>();
            grid.PaintStarted += () => gestureIds.Clear();
            grid.Paint += (s, erase) =>
            {
                string id = $"st{s + 1}";
                if (gestureIds.Add(id)) Begin(id);
                int t = erase ? 3 : vs.Brush;
                Raw(id, t / 3f); Refresh();
            };
            grid.PaintEnded += () => { foreach (var id in gestureIds) End(id); gestureIds.Clear(); };
            var lane = new ConsortPitchLane();
            lane.DragStarted += s => Begin($"sp{s + 1}");
            lane.DragEnded += s => End($"sp{s + 1}");
            lane.PitchChanged += (s, v) => { Raw($"sp{s + 1}", 0.5f + v / 48f); Refresh(); };
            tabReadouts[3].Add(() =>
            {
                var types = StepTypes();
                grid.Set(types, SeqLen(), PlayStep(), Ratchet());
                lane.Set(StepPitches(), types, SeqLen(), PlayStep(), Sel("seqmode", 3) == 2);
            });
            var laneBox = GraphWindow(lane);

            var swingSl = ParamRow(ctx, idx, "seqswing", "", v => $"{50 + v * 25:0} %", Refresh, cur, trackWidth: 60, valueWidth: 30, learnName: "Swing");
            var rateTxt = Value("");
            var rateBox = new Border { Background = Brushes.Transparent, Padding = new Thickness(2, 0), Cursor = new Cursor(StandardCursorType.Hand), Child = Glyph.WithChevron(rateTxt, 8) };
            ToolTip.SetTip(rateBox, "Step rate (synced to the host tempo)");
            tabReadouts[3].Add(() => rateTxt.Text = SeqRates[Sel("seqrate", SeqRates.Length)]);
            rateBox.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(rateBox).Properties.IsLeftButtonPressed) return;
                var f = new MenuFlyout();
                for (int r = 0; r < SeqRates.Length; r++) { int rv = r; var mi = new MenuItem { Header = SeqRates[r] }; mi.Click += (_, _) => { SetP("seqrate", rv / (float)(SeqRates.Length - 1)); Refresh(); }; f.Items.Add(mi); }
                f.ShowAt(rateBox); e.Handled = true;
            };
            if (I("seqrate") is var ri and >= 0) MidiLearn.Bind(rateBox, MidiTarget.PluginParam(track, -1, ri), "seqrate");
            var order = Chips("seqorder", new[] { "Fwd", "Back", "Rnd" }, null, 7, null, 5);
            ToolTip.SetTip(order, "Step order: forward, backward, random");
            var bottom = new DockPanel { LastChildFill = false, Height = 26, Children = {
                Docked(Toggle("seqlatch", "Latch"), Avalonia.Controls.Dock.Right),
                Docked(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Children = {
                    Row(6, Cap("Rate"), rateBox),
                    Row(8, Cap("Swing"), swingSl),
                    order } }, Avalonia.Controls.Dock.Left) } };
            cur = saved;
            return new DockPanel { LastChildFill = true, Margin = new Thickness(0, 4, 0, 0), Children = {
                Docked(top, Avalonia.Controls.Dock.Top), Docked(paint, Avalonia.Controls.Dock.Top), Docked(grid, Avalonia.Controls.Dock.Top),
                Docked(new Border { Padding = new Thickness(0, 4, 0, 0), Child = bottom }, Avalonia.Controls.Dock.Bottom),
                laneBox } };
        }

        // ======================================================================
        // CENTRE — Patch (matrix / jack strip)
        // ======================================================================
        int[] MatrixRows()
        {
            var l = new List<int>();
            foreach (var c in CableList()) if (!l.Contains(c.Src) && l.Count < 5) l.Add(c.Src);
            foreach (var p in vs.RowPrefs) if (!l.Contains(p) && l.Count < 5) l.Add(p);
            return l.ToArray();
        }
        int[] MatrixCols()
        {
            var l = new List<int>();
            foreach (var c in CableList()) if (!l.Contains(c.Dst) && l.Count < 5) l.Add(c.Dst);
            foreach (var p in vs.ColPrefs) if (!l.Contains(p) && l.Count < 5) l.Add(p);
            return l.ToArray();
        }
        Control MatrixView()
        {
            var m = new ConsortPatchMatrix();
            string? gest = null;
            m.GestureBegin += (s, d) => { int k = FindCable(s, d); if (k >= 0) { gest = AmtId(k); Begin(gest); } };
            m.GestureEnd += (s, d) => { if (gest != null) End(gest); gest = null; };
            m.DepthChanged += (s, d, v) =>
            {
                int k = FindCable(s, d);
                if (k < 0) { if (AddCable(s, d, v)) { k = FindCable(s, d); if (k >= 0) { gest = AmtId(k); Begin(gest); } } return; }
                Raw(AmtId(k), 0.5f + v / 2); Refresh();
            };
            m.Removed += (s, d) => { int k = FindCable(s, d); if (k >= 0) RemoveSlot(k); };
            m.HeaderClicked += (isRow, i, rect) =>
            {
                var f = new MenuFlyout();
                var list = isRow ? ConsortJacks.Sources : ConsortJacks.Dests;
                for (int j = 1; j < list.Length; j++)
                {
                    int jv = j;
                    var mi = new MenuItem { Header = list[j].Name };
                    mi.Click += (_, _) => { var prefs = isRow ? vs.RowPrefs : vs.ColPrefs; prefs.Remove(jv); prefs.Insert(0, jv); Refresh(); };
                    f.Items.Add(mi);
                }
                f.ShowAt(m, showAtPointer: true);
            };
            tabReadouts[4].Add(() => m.Set(MatrixRows(), MatrixCols(), CableList()));
            List<Action> saved = cur; cur = tabReadouts[4];
            var a1 = HSlider("atten1", v => Depth(v), 30, null, true); a1.Width = 96;
            var a2 = HSlider("atten2", v => Depth(v), 30, null, true); a2.Width = 96;
            cur = saved;
            var si = new ConsortJackDot(12); var so = new ConsortJackDot(12);
            ToolTip.SetTip(si, "Sum in"); ToolTip.SetTip(so, "Sum out");
            tabReadouts[4].Add(() => { si.Set(PatchedAt(false, 22)); so.Set(PatchedAt(true, 20)); });
            var util = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Height = 26, Children = {
                Row(8, Cap("Atten 1"), a1), Row(8, Cap("Atten 2"), a2), Row(5, Cap("Sum"), si, so) } };
            ToolTip.SetTip(util, "Attenuators scale whatever is patched into Atten 1/2 in; Sum adds its inputs");
            return new DockPanel { LastChildFill = true, Children = { Docked(Divider(util, 2), Avalonia.Controls.Dock.Bottom), new Border { Padding = new Thickness(0, 0, 0, 4), Child = m } } };
        }
        Control CableListView(List<Action> into)
        {
            var host = new StackPanel { Spacing = 3 };
            var rowReadouts = new List<Action>();
            string? sig = null;
            void Rebuild()
            {
                host.Children.Clear(); rowReadouts.Clear();
                var cs = CableList();
                if (cs.Count == 0) host.Children.Add(Cap("No cables", DimC));
                foreach (var c in cs)
                {
                    int slot = c.Slot;
                    var dot = new Border { Width = 6, Height = 6, CornerRadius = NotaRadius.Cell, Background = ConsortJacks.ToneBrush(ConsortJacks.Src(c.Src).Tone), VerticalAlignment = VerticalAlignment.Center };
                    var x = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = new Glyph(GlyphKind.Close, 8) { Foreground = MutedC } };
                    x.PointerPressed += (_, e) => { RemoveSlot(slot); e.Handled = true; };
                    var name = new TextBlock { Text = $"{ConsortJacks.ShortName(ConsortJacks.Src(c.Src).Name)} → {ConsortJacks.ShortName(ConsortJacks.Dst(c.Dst).Name)}", FontSize = NotaType.KnobValue, Foreground = TxtC, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                    var hdr = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 5 };
                    hdr.Children.Add(dot); hdr.Children.Add(Col(name, 1)); hdr.Children.Add(Col(x, 2));
                    List<Action> saved = cur; cur = rowReadouts;
                    var sl = HSlider(AmtId(slot), v => Depth(v), 24, null, true);
                    cur = saved;
                    var dep = new Grid { ColumnDefinitions = new ColumnDefinitions("40,*") };
                    dep.Children.Add(Caps("Depth")); dep.Children.Add(Col(sl, 1));
                    host.Children.Add(new Border { BorderBrush = BorderIn, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 1, 0, 3), Child = new StackPanel { Spacing = 2, Children = { hdr, dep } } });
                }
            }
            into.Add(() =>
            {
                string s = string.Join(",", CableList().Select(c => $"{c.Slot}:{c.Src}:{c.Dst}"));
                if (s != sig) { sig = s; Rebuild(); }
                foreach (var a in rowReadouts) a();
            });
            return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = host };
        }
        ConsortJackField StripField()
        {
            ConsortJacks.Jack S(int i) => ConsortJacks.Sources[i];
            ConsortJacks.Jack D(int i) => ConsortJacks.Dests[i];
            var rows = new List<List<ConsortJackField.Group>>
            {
                new() { new("LFO", new[] { S(1), D(1) }), new("ENV", new[] { S(2), S(3), D(2), D(3) }), new("OSC OUT", new[] { S(4), S(5), S(6), S(7), S(8) }), new("SEQ", new[] { S(10), S(11), S(12) }) },
                new() { new("OSC IN", new[] { D(4), D(5), D(6), D(7), D(8), D(9) }), new("FILTER", new[] { D(10), D(11), D(12), D(13), S(9) }), new("VCA", new[] { D(14), D(15), D(19) }) },
                new() { new("DELAY", new[] { D(16), D(17), D(18) }), new("KBD", new[] { S(13), S(14), S(15), S(16), S(17) }), new("UTIL", new[] { D(20), S(18), D(21), S(19), D(22), S(20) }) },
            };
            var f = new ConsortJackField(rows, false);
            f.Connect += (s, d) => AddCable(s, d);
            f.Pull += RemoveAtJack;
            f.Context += JackMenu;
            return f;
        }
        Control CablesView()
        {
            var field = StripField();
            tabReadouts[4].Add(() => field.SetCables(CableList()));
            var list = CableListView(tabReadouts[4]);
            var hint = Cap("Drag jack to jack · Alt-click to pull · right-click for a list", DimC); hint.TextWrapping = TextWrapping.Wrap; hint.TextTrimming = TextTrimming.None;
            var side = new DockPanel { LastChildFill = true, Children = { Docked(new Border { Margin = new Thickness(0, 0, 0, 3), Child = Caps("Cables") }, Avalonia.Controls.Dock.Top), Docked(hint, Avalonia.Controls.Dock.Bottom), list } };
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,150") };
            g.Children.Add(new Border { Padding = new Thickness(0, 2, 8, 0), Child = field });
            g.Children.Add(Col(new Border { BorderBrush = BorderIn, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(10, 3, 0, 2), Child = side }, 1));
            return g;
        }
        Control PatchTab()
        {
            var host = new ContentControl();
            Control? mat = null, cab = null;
            void Show() { host.Content = vs.PatchView == 0 ? (mat ??= MatrixView()) : (cab ??= CablesView()); Refresh(); }
            patchShow = Show;
            Show();
            return new Border { Padding = new Thickness(0, 6, 0, 0), Child = host };
        }

        // ======================================================================
        // RIGHT — Voices / Output
        // ======================================================================
        bool Poly() => On("truepoly");
        Control Line(string cap, Control c, Control? right = null)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("51,*,Auto"), Height = 22 };
            g.Children.Add(Cap(cap)); g.Children.Add(Col(c, 1)); if (right != null) g.Children.Add(Col(right, 2));
            return g;
        }
        Control RuleLine() => new Border { Height = 1, Background = BorderIn, Margin = new Thickness(0, 3) };
        Control VoicesTab()
        {
            var cells = new Border[4]; var txt = new TextBlock[4];
            var cellRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), ColumnSpacing = 3, VerticalAlignment = VerticalAlignment.Center };
            for (int i = 0; i < 4; i++)
            {
                txt[i] = Value("—", DimC); txt[i].HorizontalAlignment = HorizontalAlignment.Center;
                cells[i] = new Border { Height = NotaSize.Seg + 4, CornerRadius = NotaRadius.Group, Background = NotaPalette.Groove, Child = txt[i] };
                cells[i].BindResource(Border.BoxShadowProperty, "Shadow.Groove");
                ToolTip.SetTip(cells[i], $"Oscillator {i + 1}'s note (paraphonic) · the latest voices in true poly");
                Grid.SetColumn(cells[i], i);
                cellRow.Children.Add(cells[i]);
            }
            var sounding = new bool[4];
            rightReadouts[0].Add(() =>
            {
                for (int i = 0; i < 4; i++)
                {
                    int note = scN > 12 + i ? (int)scope[12 + i] : -1;
                    float lv = scN > 16 + i ? scope[16 + i] : 0;
                    bool on = note >= 0, lit = on && lv > 0.01f;
                    txt[i].Text = on ? NoteName(note) : "—"; txt[i].Foreground = lit ? AmberLit : on ? Txt2 : DimC;
                    if (lit == sounding[i]) continue;
                    sounding[i] = lit;
                    cells[i].Background = lit ? NotaPalette.AccentSoft : NotaPalette.Groove;
                    cells[i].BindResource(Border.BoxShadowProperty, lit ? "Shadow.Lifted" : "Shadow.Groove");
                }
            });
            var gated = Cap("gated", DimC);
            var gatedBox = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = gated, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(gatedBox, "Gated glide: only between overlapping (legato) notes — click to switch");
            gatedBox.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(gatedBox).Properties.IsLeftButtonPressed) return; SetP("glidegated", On("glidegated") ? 0 : 1); Refresh(); e.Handled = true; };
            rightReadouts[0].Add(() => gated.Foreground = On("glidegated") ? AmberLit : DimC);
            if (I("glidegated") is var gg and >= 0) MidiLearn.Bind(gatedBox, MidiTarget.PluginParam(track, -1, gg), "glidegated");
            var mode = Chips("voicemode", new[] { "Mono", "Duo", "Para" }, null, 7, Poly, 5);
            var body = new StackPanel { Margin = new Thickness(0, 5, 0, 0), Children = {
                Line("Mode", mode),
                Line("Voices", cellRow),
                Line("Glide", ParamRow(ctx, idx, "glide", "", v => GlideF(v), Refresh, cur, valueWidth: 30, learnName: "Glide")),
                Line("Type", Chips("glidetype", new[] { "LCR", "LCT", "EXP" }, null, 7, null, 5), gatedBox),
                RuleLine(),
                new StackPanel { Children = { Toggle("multitrig", "Multi trig", Poly), Toggle("truepoly", "True poly (16)"), Toggle("unison", "Unison detune") } },
                RuleLine(),
                Line("Drift", ParamRow(ctx, idx, "drift", "", v => Pct(v), Refresh, cur, valueWidth: 34, learnName: "Drift")) } };
            foreach (var sw in ((StackPanel)body.Children[5]).Children) sw.Height = 14;
            body.Children[6].Margin = new Thickness(0, 2, 0, 3);   // Drift's bar ends inside the 160 content height
            body.Children[7].Height = 20;
            return body;
        }
        Control OutputTab()
        {
            var meterL = new PentadLevelBar { Vertical = true, Width = 10, Height = 44 };
            var meterR = new PentadLevelBar { Vertical = true, Width = 10, Height = 44 };
            rightReadouts[1].Add(() => { if (scN > 3) { meterL.SetLinear(scope[2]); meterR.SetLinear(scope[3]); } });
            TextBlock Tick(string t) => Cap(t, DimC, NotaGraph.AxisSize);
            var scale = new Grid { RowDefinitions = new RowDefinitions("*,*,*"), Height = 44, Children = { Tick("0"), RowAt(Tick("−12"), 1), RowAt(Tick("−48"), 2) } };
            ((TextBlock)scale.Children[0]).VerticalAlignment = VerticalAlignment.Top;
            ((TextBlock)scale.Children[2]).VerticalAlignment = VerticalAlignment.Bottom;
            var meters = Row(4, new StackPanel { Spacing = 2, Children = { meterL, Center(Cap("L", MutedC)) } },
                                new StackPanel { Spacing = 2, Children = { meterR, Center(Cap("R", MutedC)) } }, scale);
            meters.VerticalAlignment = VerticalAlignment.Top;
            var top = Row(16, K("volume", "Volume", VolDb, false, Knob.Size, 56), meters);
            top.Margin = new Thickness(0, 3, 0, 1);
            var os = Chips("oversample", new[] { "×1", "×2", "×4" }, null, 7, null, 6);
            var body = new StackPanel { Children = {
                top,
                RuleLine(),
                Line("Tune", ParamRow(ctx, idx, "tune", "", v => Tune(v), Refresh, cur, valueWidth: 40, bipolar: true, learnName: "Tune")),
                Line("Bend", ParamRow(ctx, idx, "bendrange", "", v => $"±{1 + Math.Clamp((int)Math.Round(v * 11), 0, 11)} st", Refresh, cur, valueWidth: 40, learnName: "Bend range")),
                new StackPanel { Children = { Toggle("velvca", "Velocity → VCA"), Toggle("atcut", "Aftertouch → cut") } },
                Line("Oversample", os) } };
            ((Grid)body.Children[^1]).ColumnDefinitions[0].Width = new GridLength(72);
            foreach (var sw in ((StackPanel)body.Children[4]).Children) sw.Height = 15;
            return body;
        }

        // ======================================================================
        // Tab rows + extras
        // ======================================================================
        var centreHost = new ContentControl();
        var rightHost = new ContentControl();
        var extrasHost = new ContentControl { VerticalAlignment = VerticalAlignment.Center };
        var tabBodies = new Control?[5]; var rightBodies = new Control?[2];
        var patchCountTxt = Cap("");
        Control?[] extras = new Control?[5];
        {
            List<Action> saved = cur;
            cur = tabReadouts[0];
            var pwSl = ParamRow(ctx, idx, "pw", "", v => Pct(v), Refresh, cur, trackWidth: 56, valueWidth: 34, learnName: "PWM");
            var pwm = Row(8, Cap("PWM"), pwSl);
            extras[0] = pwm;
            ToolTip.SetTip(pwm, "Pulse width of the pulse wave (square stays 50 %)");
            cur = saved;
            extras[4] = Row(8, patchCountTxt, ViewSeg(new[] { "Matrix", "Cables" }, () => vs.PatchView, v => { vs.PatchView = v; patchShow(); }));
            common.Add(() => patchCountTxt.Text = $"{CableList().Count} / {Cables}");
        }
        Control CentreBody(int t)
        {
            if (tabBodies[t] != null) return tabBodies[t]!;
            List<Action> saved = cur; cur = tabReadouts[t];
            var b = t switch { 1 => FilterEnvTab(), 2 => LfoDelayTab(), 3 => SeqTab(), 4 => PatchTab(), _ => OscTab() };
            cur = saved;
            return tabBodies[t] = b;
        }
        Control RightBody(int t)
        {
            if (rightBodies[t] != null) return rightBodies[t]!;
            List<Action> saved = cur; cur = rightReadouts[t];
            var b = t == 1 ? OutputTab() : VoicesTab();
            cur = saved;
            return rightBodies[t] = b;
        }

        vs.Centre = Math.Clamp(vs.Centre, 0, 4); vs.Right = Math.Clamp(vs.Right, 0, 1);
        void PickCentre(int t) { vs.Centre = t; if (t != 4) vs.LastNonPatch = t; centreHost.Content = CentreBody(t); extrasHost.Content = extras[t]; Refresh(); }
        var centreTabs = Tabs(new[] { "Osc · Mix", "Filters · Env", "LFO · Delay", "Seq", "Patch" }, () => vs.Centre, PickCentre, out var centreTabSync, extrasHost);
        void selectCentre(int t) { PickCentre(t); centreTabSync(); }
        PickCentre(vs.Centre);
        void PickRight(int t) { vs.Right = t; rightHost.Content = RightBody(t); Refresh(); }
        var rightTabs = Tabs(new[] { "Voices", "Output" }, () => vs.Right, PickRight, out _);
        PickRight(vs.Right);
        var right = Pane(new DockPanel { Children = { Docked(rightTabs, Avalonia.Controls.Dock.Top), rightHost } });
        right.Width = 200;
        DockPanel.SetDock(right, Avalonia.Controls.Dock.Right);
        var centreBox = Pane(new DockPanel { Children = { Docked(centreTabs, Avalonia.Controls.Dock.Top), centreHost } });
        centreBox.Margin = new Thickness(SectionGap, 0);

        // ======================================================================
        // Patch bay: the header part (cable count + Open) and the full-card overlay
        // ======================================================================
        var overlay = new Border { IsVisible = false, Background = NotaPalette.SurfaceCard, CornerRadius = NotaRadius.Group, Focusable = true };
        void OpenOverlay(bool open) { vs.Overlay = open; overlay.IsVisible = open; if (open) { overlay.Focus(); } Refresh(); }
        var countTxt = Value("");
        var openTxt = new TextBlock { FontSize = NotaType.DeviceCaption, Foreground = TxtC, VerticalAlignment = VerticalAlignment.Center };
        var openBtn = TextButton("");
        openBtn.Child = Glyph.WithChevron(openTxt, 8);
        openBtn.VerticalAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(openBtn, "Open the full patch bay (all 42 points) · on the Patch tab: back to the last panel");
        openBtn.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(openBtn).Properties.IsLeftButtonPressed) return;
            if (vs.Overlay) OpenOverlay(false);
            else if (vs.Centre == 4) selectCentre(vs.LastNonPatch);
            else OpenOverlay(true);
            e.Handled = true;
        };
        common.Add(() =>
        {
            var cs = CableList();
            countTxt.Text = cs.Count == 1 ? "1 cable" : $"{cs.Count} cables";
            ToolTip.SetTip(countTxt, cs.Count == 0 ? "No cables — the normals run the voice" : string.Join("\n", cs.Select(c => $"{CableName(c)} · depth {c.Depth * 100:+0;−0;0}")));
            openTxt.Text = vs.Overlay ? "Close" : vs.Centre == 4 ? "Collapse" : "Open";
        });
        var header = Row(10, Cap("Patch bay", MutedC, NotaType.DeviceCaption), countTxt, openBtn);
        header.Margin = new Thickness(6, 0, 0, 0);
        vs.Header = header;

        Control OverlayBody()
        {
            ConsortJacks.Jack S(int i) => ConsortJacks.Sources[i];
            ConsortJacks.Jack D(int i) => ConsortJacks.Dests[i];
            var cols = new List<List<ConsortJackField.Group>>
            {
                new() { new("MODULATION", new[] { S(1), D(1), S(2), S(3), D(2), D(3), S(17), S(16) }) },
                new() { new("OSCILLATORS", new[] { D(4), D(5), D(6), D(7), D(8), D(9), S(4), S(5), S(6), S(7) }) },
                new() { new("FILTERS / VCA", new[] { D(10), D(11), D(12), D(13), S(9), D(14), D(15), D(19) }) },
                new() { new("DELAY / SEQ", new[] { D(16), D(17), D(18), S(10), S(11), S(12) }) },
                new() { new("KBD / UTIL", new[] { S(13), S(14), S(15), S(8), D(20), S(18), D(21), S(19), D(22), S(20) }) },
            };
            var field = new ConsortJackField(cols, true);
            field.Connect += (s, d) => AddCable(s, d);
            field.Pull += RemoveAtJack;
            field.Context += JackMenu;
            overlayReadouts.Add(() => field.SetCables(CableList()));
            var info = Cap("");
            overlayReadouts.Add(() => info.Text = $"{CableList().Count} cables · {ConsortJacks.Points} points");
            Border Btn(string t, Action a)
            {
                var b = TextButton(t); b.VerticalAlignment = VerticalAlignment.Center;
                b.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; a(); e.Handled = true; };
                return b;
            }
            var view = ViewSeg(new[] { "Cables", "Matrix" }, () => 0, v => { if (v == 1) { vs.PatchView = 0; OpenOverlay(false); selectCentre(4); patchShow(); } });
            var topGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto,Auto"), ColumnSpacing = 10 };
            topGrid.Children.Add(Title("Patch bay"));
            topGrid.Children.Add(Col(info, 1)); topGrid.Children.Add(Col(view, 2));
            topGrid.Children.Add(Col(Btn("Clear all", ClearAll), 4)); topGrid.Children.Add(Col(Btn("Close", () => OpenOverlay(false)), 5));
            var top = new Border { Height = 30, BorderBrush = Border2, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(12, 0), Child = topGrid };
            // bottom: cable list with draggable depths
            var bottomHost = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
            string? sig = null;
            overlayReadouts.Add(() =>
            {
                var cs = CableList();
                string s = string.Join(",", cs.Select(c => $"{c.Slot}:{c.Src}:{c.Dst}:{c.Depth:0.00}"));
                if (s == sig) return;
                sig = s; bottomHost.Children.Clear();
                foreach (var c in cs)
                {
                    int slot = c.Slot;
                    var tone = ConsortJacks.ToneBrush(ConsortJacks.Src(c.Src).Tone);
                    var dep = Value($"{c.Depth * 100:+0;−0;0}", AmberLit);
                    var depBox = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.SizeNorthSouth), Child = dep };
                    ToolTip.SetTip(depBox, "Depth — drag up / down (double-click: +50)");
                    bool drag = false; double y0 = 0; float v0 = 0;
                    depBox.PointerPressed += (_, e) =>
                    {
                        if (!e.GetCurrentPoint(depBox).Properties.IsLeftButtonPressed) return;
                        if ((e.ClickCount == 2 || (e.KeyModifiers & KeyModifiers.Alt) != 0)) { SetP(AmtId(slot), 0.75f); Refresh(); e.Handled = true; return; }
                        drag = true; y0 = e.GetPosition(depBox).Y; v0 = G(AmtId(slot)); Begin(AmtId(slot)); e.Pointer.Capture(depBox); e.Handled = true;
                    };
                    depBox.PointerMoved += (_, e) => { if (!drag) return; float v = Math.Clamp(v0 + (float)(y0 - e.GetPosition(depBox).Y) / 200f, 0, 1); Raw(AmtId(slot), v); dep.Text = $"{(v - 0.5f) * 200:+0;−0;0}"; };
                    depBox.PointerReleased += (_, e) => { if (drag) { drag = false; End(AmtId(slot)); e.Pointer.Capture(null); Refresh(); } };
                    depBox.PointerCaptureLost += (_, _) => { if (drag) { drag = false; End(AmtId(slot)); Refresh(); } };
                    bottomHost.Children.Add(Row(5, new Border { Width = 7, Height = 7, CornerRadius = NotaRadius.Field, Background = tone, VerticalAlignment = VerticalAlignment.Center },
                        Value(CableName(c)), depBox));
                }
            });
            var note = Cap("A cable breaks the normal at Gate / Filt in / VCA in / Ext in · loops run with a one-sample delay", DimC);
            var botGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
            botGrid.Children.Add(new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxWidth = 470, Content = bottomHost });
            botGrid.Children.Add(Col(Right(note), 1));
            var bottom = new Border { Height = 24, BorderBrush = Border2, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(12, 0), Child = botGrid };
            return new DockPanel { LastChildFill = true, Children = { Docked(top, Avalonia.Controls.Dock.Top), Docked(bottom, Avalonia.Controls.Dock.Bottom), new Border { Padding = new Thickness(8, 3, 8, 2), Child = field } } };
        }
        overlay.Child = OverlayBody();
        overlay.KeyDown += (_, e) => { if (e.Key == Key.Escape) { OpenOverlay(false); e.Handled = true; } };

        // ---- assemble ---------------------------------------------------------------
        var bodyRow = new DockPanel { LastChildFill = true, Children = { wheels, right, centreBox } };
        var root = new Panel { Margin = new Thickness(0, CardPad, 0, 0), Children = { bodyRow, overlay } };

        refreshAll = () =>
        {
            cableCache = null;   // automation / presets may have moved the cables
            scN = engine.InstrumentScope(track, scope);
            foreach (var a in common) a();
            foreach (var a in tabReadouts[Math.Clamp(vs.Centre, 0, 4)]) a();
            foreach (var a in rightReadouts[Math.Clamp(vs.Right, 0, 1)]) a();
            if (vs.Overlay) foreach (var a in overlayReadouts) a();
        };
        ctx.SetInstLiveViz(Refresh);
        if (vs.Overlay) OpenOverlay(true);
        Refresh();
        return root;
    }
}
