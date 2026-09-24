// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Consort editor (instrument kind 15), a build of the
// "Nota Consort" mockup (700 × 260) on the Monolith / Pentad frame: an always-visible
// WHEELS column, a centre tabbed panel (Osc · Mix / Filters · Env / LFO · Delay / Seq /
// Patch), a right tabbed panel (Voices / Output), the collapsed PATCH BAY row (active
// cables + the entry to the full-card patch overlay) and a status strip. The patch bay has
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

namespace Nota.App;

internal sealed class ConsortInstrumentCard : IInstrumentCard
{
    private static readonly IBrush RailBg = NotaPalette.SurfaceInset;
    private static readonly IBrush Panel = NotaPalette.TextOnAccent; // dark ink over an engaged fill
    private static readonly IBrush Border2 = NotaPalette.BorderDefault;
    private static readonly IBrush BorderIn = NotaPalette.GraphBorder;
    private static readonly IBrush Inset = NotaPalette.BgSunken;
    private static readonly IBrush TabBg = NotaPalette.SurfaceCard;
    private static readonly IBrush Amber = NotaPalette.Accent;
    private static readonly IBrush AmberLit = NotaPalette.AccentBright;
    private static readonly IBrush TealC = NotaPalette.Teal;
    private static readonly IBrush TxtC = NotaPalette.TextPrimary;
    private static readonly IBrush Txt2 = NotaPalette.TextSecondary;
    private static readonly IBrush MutedC = NotaPalette.TextTertiary;
    private static readonly IBrush DimC = NotaPalette.TextDisabled;
    private static readonly IBrush Handle = NotaPalette.TextSecondary;
    private static readonly IBrush OffPill = NotaPalette.SurfaceRaised;
    private static readonly IBrush AmberSubtle = NotaPalette.AccentSubtle;

    private static readonly string[] Feet = { "32′", "16′", "8′", "4′", "2′" };
    private static readonly string[] SyncNames = { "8 bar", "4 bar", "2 bar", "1 bar", "1/2", "1/4.", "1/4", "1/8.", "1/4T", "1/8", "1/8T", "1/16", "1/16T", "1/32" };
    private static readonly string[] SeqRates = { "1/4", "1/8", "1/8T", "1/16", "1/16T", "1/32" };
    private static readonly string[] LfoNames = { "sine", "ramp up", "ramp down", "square", "S&H", "smooth random" };
    // Compact names for the patch-bay row chips (index == jack index).
    private static readonly string[] SrcChip = { "", "LFO", "Env1", "Env2", "Osc1", "Osc2", "Osc3", "Osc4", "Noise", "Filt", "Seq", "SeqGt", "Clk", "Kbd", "KbdGt", "Vel", "AT", "Wheel", "Att1", "Att2", "Sum" };
    private static readonly string[] DstChip = { "", "Rate", "Gate1", "Gate2", "Pitch", "Osc1", "Osc2", "Osc3", "Osc4", "PWM", "Filt", "Filt2", "Res", "FiltIn", "VCA", "VCAin", "Dly", "DlyFb", "DlyMix", "Ext", "Att1", "Att2", "Sum" };
    private const int Cables = 12;

    // View state that survives a rebuild (preset change, undo): per track.
    private sealed class ViewState
    {
        public int Centre, Right, PatchView, LastNonPatch, Brush;
        public bool Overlay;
        public readonly List<int> RowPrefs = new() { 1, 2, 10, 6, 15 };   // LFO out, Env 1, Seq pitch, Osc 3, Velocity
        public readonly List<int> ColPrefs = new() { 4, 9, 16, 10, 14 };   // Osc pitch, PWM, Delay time, Filt 1, VCA CV
    }
    private static readonly Dictionary<int, ViewState> Views = new();

    public bool BodyOnly => true;
    public string Subtitle => "PARAPHONIC";

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
        if (G("truepoly", 8) > 0.5f) return $"POLY {a}/16";
        return Math.Clamp((int)Math.Round(G("voicemode", 7) * 2), 0, 2) switch { 0 => "MONO", 1 => $"DUO {a}/2", _ => $"PARA {a}/4" };
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
        static TextBlock Lbl(string t, double fs, IBrush c, FontWeight w = FontWeight.Bold)
            => new() { Text = t, FontSize = fs, FontWeight = w, Foreground = c, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock Caps(string t) => new() { Text = t, FontSize = 7, FontWeight = FontWeight.Bold, Foreground = MutedC, VerticalAlignment = VerticalAlignment.Center, LetterSpacing = 0.8 };
        static TextBlock MonoText(string t, double fs, IBrush c)
        { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.Mono"); return tb; }
        static StackPanel Row(double sp, params Control[] cs)
        { var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = sp, VerticalAlignment = VerticalAlignment.Center }; foreach (var c in cs) s.Children.Add(c); return s; }
        static Control Docked(Control c, Dock d) { DockPanel.SetDock(c, d); return c; }
        static Control Col(Control c, int col) { Grid.SetColumn(c, col); return c; }
        static Control RowAt(Control c, int r) { Grid.SetRow(c, r); return c; }
        static Border Divider(Control child, double top = 5) => new() { BorderBrush = BorderIn, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, top, 0, 0), Child = child };
        static Control Right(Control c) { c.HorizontalAlignment = HorizontalAlignment.Right; return c; }
        static Control Center(Control c) { c.HorizontalAlignment = HorizontalAlignment.Center; return c; }

        Control K(string id, string name, Func<float, string> fmt, bool mod = false, double sz = Knob.SizeSecondary, double cw = 44, bool inline = false)
            => InstrumentControls.InstKnob(ctx, idx, id, name, Refresh, fmt, sz, cw, mod ? TealC : null, inline);

        // A compact horizontal knob: [inline knob] value — for the oscillator table.
        Control MiniKnob(string id, Func<float, string> fmt, double valW, IBrush valColor)
        {
            if (I(id) is not (var i and >= 0)) return new Panel();
            var value = MonoText(fmt(G(id)), 8, valColor); value.Width = valW;
            var knob = new Knob(G(id), 1.0) { Accent = true, Inline = true, Default = engine.InstrumentParamDefault(track, i) };
            knob.ValueChanged += v => { engine.PluginParamSet(track, -1, i, (float)v); value.Text = fmt((float)v); Refresh(); };
            knob.GestureBegin += () => Begin(id);
            knob.GestureEnd += () => End(id);
            ctx.AddInstFader(i, knob, value, fmt);
            MidiLearn.Bind(knob, MidiTarget.PluginParam(track, -1, i), id);
            return Row(4, knob, value);
        }

        // On/off pill toggle backed by a param (> 0.5 = on).
        Control Toggle(string id, string label, Func<bool>? dim = null)
        {
            var wrap = Switch(label, () => On(id), () => { SetP(id, On(id) ? 0f : 1f); Refresh(); }, out var sync, dim);
            cur.Add(sync);
            if (I(id) is var pi and >= 0) MidiLearn.Bind(wrap, MidiTarget.PluginParam(track, -1, pi), label.Length > 0 ? label : id);
            return wrap;
        }

        // Segmented chips. values[i] is written on click; the lit chip is the nearest value.
        Control Chips(string id, string[] names, float[]? values = null, double fs = 7, Func<bool>? dim = null)
        {
            var vals = values ?? BuildValues(names.Length);
            var seg = DeviceCardKit.Segments(names, () => DeviceCardKit.NearestExact(G(id), vals), iv => { SetP(id, vals[iv]); Refresh(); }, out var sync, dim: dim, padX: 4, fontSize: fs);
            cur.Add(sync);
            if (I(id) is var pi and >= 0) MidiLearn.Bind(seg, MidiTarget.PluginParam(track, -1, pi), id);
            return seg;
        }
        static float[] BuildValues(int n) { var v = new float[n]; for (int i = 0; i < n; i++) v[i] = n > 1 ? i / (float)(n - 1) : 0f; return v; }

        // A segmented control over view state (not a param).
        Control ViewSeg(string[] names, Func<int> get, Action<int> set)
        {
            var arr = new Border[names.Length];
            void Hi() { for (int i = 0; i < arr.Length; i++) { bool on = i == get(); arr[i].Background = on ? Amber : Brushes.Transparent; var tb = (TextBlock)arr[i].Child!; tb.Foreground = on ? Panel : MutedC; tb.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal; } }
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
            for (int i = 0; i < names.Length; i++)
            {
                int iv = i;
                var c = new Border { CornerRadius = NotaRadius.Clip, Padding = new Thickness(5, 0), Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = names[i], FontSize = 8 } };
                c.PointerPressed += (_, e) => { set(iv); Hi(); e.Handled = true; };
                arr[i] = c; row.Children.Add(c);
            }
            Hi();
            return new Border { Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Badge, Padding = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Child = row };
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
        Control SliderRow(string label, string id, Func<double, string> fmt, double labW = 40, double valW = 26, Func<bool>? dim = null, bool bipolar = false)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions($"{labW},*"), VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(Caps(label)); g.Children.Add(Col(HSlider(id, fmt, valW, dim, bipolar), 1));
            return g;
        }

        // Vertical wheel bound to a param (pitch springs back to centre).
        Control VWheel(string id, string name, IBrush lit, bool spring)
        {
            int pi = I(id);
            IBrush grad = NotaPalette.BgSunken;
            var bar = new Border { Width = 16, Background = grad, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Pill, VerticalAlignment = VerticalAlignment.Stretch };
            var mark = new Border { Height = 2, Width = 12, Background = lit, CornerRadius = NotaRadius.Bar };
            var lay = new Canvas { Width = 16 };
            lay.Children.Add(mark); Canvas.SetLeft(mark, 2);
            var host = new Panel { Width = 16, VerticalAlignment = VerticalAlignment.Stretch, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.SizeNorthSouth), Children = { bar, lay } };
            bool drag = false; DispatcherTimer? springT = null;
            void Vis(double v) { double h = host.Bounds.Height; if (h <= 0) h = 90; Canvas.SetTop(mark, (1 - v) * (h - 2)); }
            void From(PointerEventArgs e) { double h = host.Bounds.Height; double v = h > 0 ? Math.Clamp(1 - e.GetPosition(host).Y / h, 0, 1) : 0.5; if (pi >= 0) engine.PluginParamSet(track, -1, pi, (float)v); Vis(v); Refresh(); }
            void Spring()
            {
                springT?.Stop();
                springT = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                springT.Tick += (_, _) =>
                {
                    double v = G(id); v += (0.5 - v) * 0.32;
                    if (Math.Abs(v - 0.5) < 0.002) { v = 0.5; springT!.Stop(); }
                    if (pi >= 0) engine.PluginParamSet(track, -1, pi, (float)v); Vis(v); Refresh();
                };
                springT.Start();
            }
            host.PointerPressed += (_, e) => { springT?.Stop(); drag = true; if (pi >= 0) Begin(id); e.Pointer.Capture(host); From(e); };
            host.PointerMoved += (_, e) => { if (drag) From(e); };
            host.PointerReleased += (_, e) => { if (drag) { drag = false; if (pi >= 0) End(id); e.Pointer.Capture(null); if (spring) Spring(); } };
            host.SizeChanged += (_, _) => Vis(G(id));
            var nm = new TextBlock { Text = name, FontSize = 7, FontWeight = FontWeight.Bold, Foreground = MutedC, HorizontalAlignment = HorizontalAlignment.Center };
            common.Add(() => { if (!drag) Vis(G(id)); bool live = Math.Abs(G(id) - (spring ? 0.5f : 0f)) > 0.01f; nm.Foreground = live ? AmberLit : MutedC; });
            var col = new DockPanel { HorizontalAlignment = HorizontalAlignment.Center };
            DockPanel.SetDock(nm, Avalonia.Controls.Dock.Bottom);
            col.Children.Add(nm); col.Children.Add(host);
            if (pi >= 0) MidiLearn.Bind(col, MidiTarget.PluginParam(track, -1, pi), name);
            return col;
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
        string CableChip(ConsortCable c) => $"{SrcChip[Math.Min(c.Src, SrcChip.Length - 1)]}→{DstChip[Math.Min(c.Dst, DstChip.Length - 1)]}";

        // Right-click / click on a jack: its cables (remove) + quick patch to any opposite jack.
        void JackMenu(ConsortJacks.Jack j, Control anchor)
        {
            var f = new MenuFlyout();
            f.Items.Add(new MenuItem { Header = $"{j.Name} · {(j.Out ? "output" : "input")}{(ConsortJacks.Normalled.Contains(j.Index) && !j.Out ? " (normalled)" : "")}", IsEnabled = false });
            var mine = CableList().Where(c => j.Out ? c.Src == j.Index : c.Dst == j.Index).ToList();
            foreach (var c in mine)
            {
                var mi = new MenuItem { Header = $"Remove {CableName(c)}  {c.Depth * 100:+0;−0;0}" };
                int slot = c.Slot; mi.Click += (_, _) => RemoveSlot(slot);
                f.Items.Add(mi);
            }
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
            f.ShowAt(anchor, showAtPointer: true);
        }

        // ======================================================================
        // LEFT — wheels
        // ======================================================================
        var bendTxt = MonoText("", 7, AmberLit); var modTxt = MonoText("", 7, MutedC);
        bendTxt.HorizontalAlignment = HorizontalAlignment.Center; modTxt.HorizontalAlignment = HorizontalAlignment.Center;
        common.Add(() => { bendTxt.Text = $"±{BendSt()}\u2009st"; modTxt.Text = $"{G("modwheel") * 100:0}\u2009%"; modTxt.Foreground = G("modwheel") > 0.01f ? AmberLit : MutedC; });
        var wheelsBody = new DockPanel { LastChildFill = true };
        var wTitle = Caps("WHEELS"); wTitle.HorizontalAlignment = HorizontalAlignment.Center; wTitle.Margin = new Thickness(0, 0, 0, 4);
        wheelsBody.Children.Add(Docked(wTitle, Avalonia.Controls.Dock.Top));
        wheelsBody.Children.Add(Docked(modTxt, Avalonia.Controls.Dock.Bottom));
        wheelsBody.Children.Add(Docked(bendTxt, Avalonia.Controls.Dock.Bottom));
        wheelsBody.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 3), Children = {
            VWheel("bend", "PITCH", Amber, spring: true),
            VWheel("modwheel", "MOD", AmberLit, spring: false) } });
        var wheels = new Border { Width = 52, Background = Panel, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile, Padding = new Thickness(0, 5), Child = wheelsBody };
        DockPanel.SetDock(wheels, Avalonia.Controls.Dock.Left);

        // ======================================================================
        // CENTRE — Osc · Mix
        // ======================================================================
        const string OscCols = "14,54,70,*,74";
        Control Cell(Control c, int col, HorizontalAlignment ha = HorizontalAlignment.Center) { c.HorizontalAlignment = ha; c.VerticalAlignment = VerticalAlignment.Center; return Col(new Panel { Children = { c } }, col); }
        Control WaveSel(string id)
        {
            int[] icon = { 2, 0, 3, 1 };   // triangle, saw, square, pulse (PentadWaveIcon glyphs)
            string[] tip = { "Triangle", "Saw", "Square", "Pulse (width = PWM)" };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
            var chips = new Border[4]; var icons = new PentadWaveIcon[4];
            for (int w = 0; w < 4; w++)
            {
                int wv = w;
                var ic = new PentadWaveIcon(icon[w]) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var b = new Border { Width = 24, Height = 16, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Badge, Cursor = new Cursor(StandardCursorType.Hand), Child = ic };
                ToolTip.SetTip(b, tip[w]);
                b.PointerPressed += (_, e) => { SetP(id, wv / 3f); Refresh(); e.Handled = true; };
                chips[w] = b; icons[w] = ic; row.Children.Add(b);
            }
            cur.Add(() =>
            {
                int s = Sel(id, 4);
                for (int w = 0; w < 4; w++) { bool on = w == s; chips[w].Background = on ? Amber : Brushes.Transparent; chips[w].BorderBrush = on ? Amber : NotaPalette.BorderStrong; icons[w].Stroke = on ? NotaPalette.TextOnAccent : MutedC; icons[w].InvalidateVisual(); }
            });
            if (I(id) is var pi and >= 0) MidiLearn.Bind(row, MidiTarget.PluginParam(track, -1, pi), id);
            return row;
        }
        Control SyncChip(string id, string text)
        {
            var tb = MonoText(text, 8, MutedC);
            var b = new Border { Padding = new Thickness(4, 1), CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), Child = tb };
            ToolTip.SetTip(b, "Hard sync");
            b.PointerPressed += (_, e) => { SetP(id, On(id) ? 0f : 1f); Refresh(); e.Handled = true; };
            cur.Add(() => { bool on = On(id); tb.Foreground = on ? NotaPalette.AccentHover : DimC; b.BorderBrush = on ? NotaPalette.BorderBrass : Brushes.Transparent; b.Background = on ? NotaPalette.AccentSubtle : Brushes.Transparent; });
            if (I(id) is var pi and >= 0) MidiLearn.Bind(b, MidiTarget.PluginParam(track, -1, pi), id);
            return b;
        }
        Control OscRow(int n)
        {
            string p = $"o{n}";
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions(OscCols) };
            g.Children.Add(Cell(Lbl(n.ToString(), 10, TxtC, FontWeight.SemiBold), 0, HorizontalAlignment.Left));
            g.Children.Add(Cell(MiniKnob($"{p}oct", Foot, 22, AmberLit), 1, HorizontalAlignment.Left));
            g.Children.Add(n == 1 ? Cell(MonoText("master", 8, MutedC), 2, HorizontalAlignment.Left) : Cell(MiniKnob($"{p}freq", Freq, 38, TxtC), 2, HorizontalAlignment.Left));
            g.Children.Add(Cell(WaveSel($"{p}wave"), 3));
            var jd = new ConsortJackDot(11);
            ToolTip.SetTip(jd, $"Osc {n} pitch input (patch bay)");
            cur.Add(() => jd.Set(PatchedAt(false, 4 + n, 4)));
            Control right = n == 2 ? SyncChip("o2sync", "sync→1") : n == 4 ? SyncChip("o4sync", "sync→3") : MonoText("—", 8, DimC);
            g.Children.Add(Cell(Row(5, jd, right), 4, HorizontalAlignment.Right));
            return new Border { BorderBrush = BorderIn, BorderThickness = new Thickness(0, 1, 0, 0), Child = g };
        }
        Control MixCell(string label, string id)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("32,*"), VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(Caps(label)); g.Children.Add(Col(HSlider(id, v => Tenths(v), 20), 1));
            return g;
        }
        Control OscTab()
        {
            var hdr = new Grid { ColumnDefinitions = new ColumnDefinitions(OscCols), Height = 13 };
            string[] h = { "#", "OCT", "FREQ", "WAVE", "SYNC / CV" };
            for (int i = 0; i < h.Length; i++)
            {
                var t = new TextBlock { Text = h[i], FontSize = 7, FontWeight = FontWeight.Bold, Foreground = DimC, LetterSpacing = 0.8, VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = i == 0 || i == 1 || i == 2 ? HorizontalAlignment.Left : i == h.Length - 1 ? HorizontalAlignment.Right : HorizontalAlignment.Center };
                hdr.Children.Add(Col(t, i));
            }
            var rows = new Grid { RowDefinitions = new RowDefinitions("*,*,*,*") };
            for (int n = 1; n <= 4; n++) rows.Children.Add(RowAt(OscRow(n), n - 1));
            var mix = new Grid { ColumnDefinitions = new ColumnDefinitions("38,*,*,*,Auto"), RowDefinitions = new RowDefinitions("*,*"), ColumnSpacing = 8, Height = 40 };
            mix.Children.Add(RowAt(Caps("MIXER"), 0)); Grid.SetRowSpan(mix.Children[^1], 2);
            mix.Children.Add(Col(MixCell("OSC 1", "mix1"), 1)); mix.Children.Add(RowAt(Col(MixCell("OSC 2", "mix2"), 1), 1));
            mix.Children.Add(Col(MixCell("OSC 3", "mix3"), 2)); mix.Children.Add(RowAt(Col(MixCell("OSC 4", "mix4"), 2), 1));
            mix.Children.Add(Col(MixCell("NOISE", "mixnoise"), 3)); mix.Children.Add(RowAt(Col(MixCell("EXT", "mixext"), 3), 1));
            var ext = mix.Children[^1]; ToolTip.SetTip(ext, "EXT: normalled to the VCA output (Moog feedback) — patch Ext in to replace it");
            mix.Children.Add(Col(Toggle("mixdrive", "Mixer drive"), 4));
            mix.Children.Add(RowAt(Col(Chips("noisecolor", new[] { "White", "Pink" }), 4), 1));
            return new DockPanel { LastChildFill = true, Margin = new Thickness(7, 0, 7, 3), Children = {
                Docked(hdr, Avalonia.Controls.Dock.Top),
                Docked(new Border { BorderBrush = BorderIn, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 3, 0, 0), Child = mix }, Avalonia.Controls.Dock.Bottom),
                rows } };
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
            var graph = new Border { Background = Inset, BorderBrush = BorderIn, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Control, Margin = new Thickness(0, 3, 0, 2), Child = curve };
            var head = new DockPanel { Height = 12, LastChildFill = false, Children = { Docked(Caps("DUAL LADDER"), Avalonia.Controls.Dock.Left), Docked(MonoText("24\u2009dB/oct", 7, Txt2), Avalonia.Controls.Dock.Right) } };
            var knobs = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*") };
            knobs.Children.Add(K("cutoff", "CUTOFF", Cut, false, Knob.SizeSecondary, 50));
            knobs.Children.Add(Col(K("reso", "RESO", v => Tenths(v), false, Knob.SizeSecondary, 46), 1));
            knobs.Children.Add(Col(K("spacing", "SPACING", Space, false, Knob.SizeSecondary, 50), 2));
            knobs.Children.Add(Col(K("fenvamt", "ENVELOPE", EnvAmt, true, Knob.SizeSecondary, 46), 3));
            var j1 = new ConsortJackDot(11); var j2 = new ConsortJackDot(11);
            ToolTip.SetTip(j1, "Filt 1 cutoff input"); ToolTip.SetTip(j2, "Filt 2 cutoff input");
            cur.Add(() => { j1.Set(PatchedAt(false, 10)); j2.Set(PatchedAt(false, 11)); });
            var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), ColumnSpacing = 8, Height = 18 };
            bottom.Children.Add(Row(5, Caps("KEYTRACK"), Chips("kbdtrk", new[] { "0", "½", "1" })));
            bottom.Children.Add(Col(Toggle("basscomp", "Bass comp"), 1));
            bottom.Children.Add(Col(Row(4, j1, j2), 3));
            var body = new DockPanel { LastChildFill = true, Children = { Docked(head, Avalonia.Controls.Dock.Top), Docked(bottom, Avalonia.Controls.Dock.Bottom), Docked(knobs, Avalonia.Controls.Dock.Bottom), graph } };
            return new Border { Width = 222, BorderBrush = BorderIn, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(7, 4), Child = body };
        }
        Control EnvPanel(string title, string pre, bool amp)
        {
            var curve = new PentadEnvCurve { Accent = amp ? TealC : Amber };
            var times = MonoText("", 7, Txt2);
            cur.Add(() =>
            {
                curve.Set(G(pre + "attack"), G(pre + "decay"), G(pre + "sustain"), G(pre + "release")); curve.InvalidateVisual();
                times.Text = $"{Atk(G(pre + "attack"))} · {Dec(G(pre + "decay"))} · {G(pre + "sustain") * 100:0}\u2009% · {Dec(G(pre + "release"))}";
            });
            var head = new DockPanel { Height = 12, LastChildFill = false, Margin = new Thickness(0, 0, 0, 2) };
            head.Children.Add(Docked(Caps(title), Avalonia.Controls.Dock.Left));
            head.Children.Add(Docked(times, Avalonia.Controls.Dock.Right));
            var graph = new Border { Background = Inset, BorderBrush = BorderIn, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Control, Child = curve };
            var knobs = Row(0, K(pre + "attack", "A", Atk, amp, Knob.SizeInline, 30, true), K(pre + "decay", "D", Dec, amp, Knob.SizeInline, 30, true), K(pre + "sustain", "S", v => Pct(v), amp, Knob.SizeInline, 30, true), K(pre + "release", "R", Dec, amp, Knob.SizeInline, 30, true));
            knobs.Margin = new Thickness(4, 0, 0, 0);
            return new DockPanel { LastChildFill = true, Children = { Docked(head, Avalonia.Controls.Dock.Top), Docked(knobs, Avalonia.Controls.Dock.Right), graph } };
        }
        Control FilterEnvTab()
        {
            var envs = new Grid { RowDefinitions = new RowDefinitions("*,*"), Margin = new Thickness(7, 4) };
            var fe = EnvPanel("FILTER ENVELOPE", "f", false);
            var ae = Divider(EnvPanel("AMPLITUDE ENVELOPE", "a", true), 4); ae.Margin = new Thickness(0, 4, 0, 0); Grid.SetRow(ae, 1);
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
            var shapes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
            var chips = new Border[6]; var icons = new ConsortLfoIcon[6];
            for (int s = 0; s < 6; s++)
            {
                int sv = s;
                var ic = new ConsortLfoIcon(s) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var b = new Border { Width = 26, Height = 17, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Badge, Cursor = new Cursor(StandardCursorType.Hand), Child = ic };
                ToolTip.SetTip(b, LfoNames[s]);
                b.PointerPressed += (_, e) => { SetP("lfowave", sv / 5f); Refresh(); e.Handled = true; };
                chips[s] = b; icons[s] = ic; shapes.Children.Add(b);
            }
            cur.Add(() => { int s = Sel("lfowave", 6); for (int i = 0; i < 6; i++) { bool on = i == s; chips[i].Background = on ? Amber : Brushes.Transparent; chips[i].BorderBrush = on ? Amber : NotaPalette.BorderStrong; icons[i].Stroke = on ? NotaPalette.TextOnAccent : MutedC; icons[i].InvalidateVisual(); } });
            if (I("lfowave") is var pw and >= 0) MidiLearn.Bind(shapes, MidiTarget.PluginParam(track, -1, pw), "lfowave");
            var jo = new ConsortJackDot(12); var jr = new ConsortJackDot(12);
            ToolTip.SetTip(jo, "LFO out (patch bay)"); ToolTip.SetTip(jr, "LFO rate input (patch bay)");
            cur.Add(() => { jo.Set(PatchedAt(true, 1)); jr.Set(PatchedAt(false, 1)); });
            var head = new DockPanel { Height = 14, LastChildFill = false, Children = { Docked(Caps("LFO"), Avalonia.Controls.Dock.Left), Docked(Row(4, jo, jr), Avalonia.Controls.Dock.Right) } };
            var knobs = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), Margin = new Thickness(0, 4, 0, 0) };
            knobs.Children.Add(K("lforate", "RATE", LfoRate, true, Knob.SizeSecondary, 44));
            knobs.Children.Add(Col(K("lfopitch", "PITCH", LfoCents, true, Knob.SizeSecondary, 44), 1));
            knobs.Children.Add(Col(K("lfocut", "CUTOFF", v => Pct(v), true, Knob.SizeSecondary, 44), 2));
            knobs.Children.Add(Col(K("lfopwm", "PWM", v => Pct(v), true, Knob.SizeSecondary, 44), 3));
            // The switch word carries the synced division ("SYNC 1/4"), refreshed with the card.
            var syncT = Switch("Sync", () => On("lfosync"), () => { SetP("lfosync", On("lfosync") ? 0f : 1f); Refresh(); }, out var syncPaint,
                liveLabel: () => On("lfosync") ? $"Sync {SyncNames[Math.Clamp((int)Math.Round(G("lforate") * 13), 0, 13)]}" : "Sync");
            cur.Add(syncPaint);
            if (I("lfosync") is var psync and >= 0) MidiLearn.Bind(syncT, MidiTarget.PluginParam(track, -1, psync), "Sync");
            var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Height = 18 };
            bottom.Children.Add(syncT); bottom.Children.Add(Col(Chips("lfodest", new[] { "All osc", "2 + 4" }), 1));
            ToolTip.SetTip(bottom.Children[1], "LFO pitch → all oscillators, or only 2 and 4");
            var body = new DockPanel { LastChildFill = false, Children = { Docked(head, Avalonia.Controls.Dock.Top), Docked(shapes, Avalonia.Controls.Dock.Top), Docked(knobs, Avalonia.Controls.Dock.Top), Docked(Divider(bottom, 4), Avalonia.Controls.Dock.Bottom) } };
            return new Border { Width = 196, BorderBrush = BorderIn, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(7, 4), Child = body };
        }
        Control DelayPanel()
        {
            var view = new ConsortDelayView();
            var times = MonoText("", 7, Txt2);
            cur.Add(() =>
            {
                double tl = DlySec(G("dlytime")), tr = Math.Clamp(tl + (G("dlyspacing") - 0.5), 0.005, 2.0);
                view.Set(tl, tr, G("dlyfb"), G("dlymix"), On("dlyping"), On("dlydigital"));
                times.Text = $"L {tl * 1000:0} · R {tr * 1000:0}\u2009ms";
            });
            var head = new DockPanel { Height = 14, LastChildFill = false, Children = { Docked(Caps(""), Avalonia.Controls.Dock.Left), Docked(times, Avalonia.Controls.Dock.Right) } };
            var title = (TextBlock)head.Children[0];
            cur.Add(() => title.Text = On("dlydigital") ? "DELAY · DIGITAL" : "ANALOG DELAY · BBD");
            var graph = new Border { Background = Inset, BorderBrush = BorderIn, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Control, Margin = new Thickness(0, 2, 0, 3), Child = view };
            var knobs = Row(0, K("dlytime", "TIME", DlyMs, false, Knob.SizeSecondary, 44), K("dlyspacing", "SPACING", DlySpc, false, Knob.SizeSecondary, 44), K("dlyfb", "FEEDBACK", v => Pct(v), false, Knob.SizeSecondary, 46), K("dlymix", "MIX", v => Pct(v), false, Knob.SizeSecondary, 40));
            var opts = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { Chips("dlyping", new[] { "Ping", "Stereo" }, new[] { 1f, 0f }), Toggle("dlydigital", "Digital") } };
            var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            bottom.Children.Add(knobs); bottom.Children.Add(Col(Right(opts), 1));
            return new DockPanel { LastChildFill = true, Margin = new Thickness(7, 4), Children = { Docked(head, Avalonia.Controls.Dock.Top), Docked(bottom, Avalonia.Controls.Dock.Bottom), graph } };
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
            var lenTxt = MonoText("", 9, AmberLit);
            var lenBox = new Border { Padding = new Thickness(3, 0), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.SizeNorthSouth), Child = lenTxt };
            ToolTip.SetTip(lenBox, "Sequence length — drag up / down");
            { bool drag = false; double y0 = 0; float v0 = 0;
              lenBox.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(lenBox).Properties.IsLeftButtonPressed) return; drag = true; y0 = e.GetPosition(lenBox).Y; v0 = G("seqlen"); Begin("seqlen"); e.Pointer.Capture(lenBox); e.Handled = true; };
              lenBox.PointerMoved += (_, e) => { if (!drag) return; float v = Math.Clamp(v0 + (float)(y0 - e.GetPosition(lenBox).Y) / 120f, 0, 1); v = (float)Math.Round(v * 15) / 15f; Raw("seqlen", v); Refresh(); };
              lenBox.PointerReleased += (_, e) => { if (drag) { drag = false; End("seqlen"); e.Pointer.Capture(null); } }; }
            tabReadouts[3].Add(() => lenTxt.Text = $"{SeqLen()}");
            // legend = paint brushes
            string[] names = { "note", "ratchet", "tie", "rest" };
            IBrush[] cols = { Amber, ChamberInk.Mauve, TealC, NotaPalette.BorderStrong };
            var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center };
            var legItems = new Border[4];
            for (int b = 0; b < 4; b++)
            {
                int bv = b;
                var sw = new Border { Width = 9, Height = 9, CornerRadius = NotaRadius.Clip, BorderBrush = cols[b], BorderThickness = new Thickness(1), Background = b == 3 ? Inset : new SolidColorBrush(((ISolidColorBrush)cols[b]).Color, 0.35) };
                var it = new Border { Background = Brushes.Transparent, Padding = new Thickness(3, 1), CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), Child = Row(3, sw, new TextBlock { Text = names[b], FontSize = 8, Foreground = Txt2, VerticalAlignment = VerticalAlignment.Center }) };
                ToolTip.SetTip(it, $"Paint {names[b]} steps (Alt-click a step: rest)");
                it.PointerPressed += (_, e) => { vs.Brush = bv; Refresh(); e.Handled = true; };
                legItems[b] = it; legend.Children.Add(it);
            }
            tabReadouts[3].Add(() => { for (int b = 0; b < 4; b++) legItems[b].BorderBrush = b == vs.Brush ? NotaPalette.AccentSubtle : Brushes.Transparent; });
            List<Action> saved = cur; cur = tabReadouts[3];
            var ratchet = Chips("seqratchet", new[] { "×2", "×3", "×4" });
            ToolTip.SetTip(ratchet, "Ratchet repeats per step");
            var top = new DockPanel { Height = 16, LastChildFill = false };
            top.Children.Add(Docked(Row(4, Caps("STEPS"), lenBox, MonoText("/ 16", 8, MutedC)), Avalonia.Controls.Dock.Left));
            top.Children.Add(Docked(Row(8, legend, ratchet), Avalonia.Controls.Dock.Right));

            var grid = new ConsortStepGrid { Height = 24, Margin = new Thickness(0, 3, 0, 3) };
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
            var laneBox = new Border { Background = Inset, BorderBrush = BorderIn, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Control, Child = lane };

            var clkDot = new ConsortJackDot(12); var gateDot = new ConsortJackDot(12);
            ToolTip.SetTip(clkDot, "Clock out (patch bay)"); ToolTip.SetTip(gateDot, "Seq gate out (patch bay)");
            tabReadouts[3].Add(() => { clkDot.Set(PatchedAt(true, 12)); gateDot.Set(PatchedAt(true, 11, 10)); });
            var swingSl = HSlider("seqswing", v => $"{50 + v * 25:0}\u2009%", 28); swingSl.Width = 84;
            var rateTxt = MonoText("", 9, AmberLit);
            var rateBox = new Border { Background = Brushes.Transparent, Padding = new Thickness(2, 0), Cursor = new Cursor(StandardCursorType.Hand), Child = Glyph.WithChevron(rateTxt, 7) };
            ToolTip.SetTip(rateBox, "Step rate (synced to the host tempo)");
            tabReadouts[3].Add(() => rateTxt.Text = SeqRates[Sel("seqrate", SeqRates.Length)]);
            rateBox.PointerPressed += (_, e) =>
            {
                var f = new MenuFlyout();
                for (int r = 0; r < SeqRates.Length; r++) { int rv = r; var mi = new MenuItem { Header = SeqRates[r] }; mi.Click += (_, _) => { SetP("seqrate", rv / (float)(SeqRates.Length - 1)); Refresh(); }; f.Items.Add(mi); }
                f.ShowAt(rateBox); e.Handled = true;
            };
            if (I("seqrate") is var ri and >= 0) MidiLearn.Bind(rateBox, MidiTarget.PluginParam(track, -1, ri), "seqrate");
            var bottom = new DockPanel { LastChildFill = false, Height = 20, Children = {
                Docked(Row(4, Caps("CLK"), gateDot, clkDot), Avalonia.Controls.Dock.Right),
                Docked(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = {
                    Row(4, Caps("RATE"), rateBox),
                    Row(4, Caps("SWING"), swingSl),
                    Row(4, Caps("ORDER"), Chips("seqorder", new[] { "Fwd", "Back", "Rnd" })),
                    Toggle("seqlatch", "Latch") } }, Avalonia.Controls.Dock.Left) } };
            cur = saved;
            return new DockPanel { LastChildFill = true, Margin = new Thickness(7, 3, 7, 2), Children = {
                Docked(top, Avalonia.Controls.Dock.Top), Docked(grid, Avalonia.Controls.Dock.Top),
                Docked(new Border { Padding = new Thickness(0, 3, 0, 0), Child = bottom }, Avalonia.Controls.Dock.Bottom),
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
            var a1 = HSlider("atten1", v => Depth(v), 26, null, true); a1.Width = 70;
            var a2 = HSlider("atten2", v => Depth(v), 26, null, true); a2.Width = 70;
            cur = saved;
            var si = new ConsortJackDot(12); var so = new ConsortJackDot(12);
            ToolTip.SetTip(si, "Sum in"); ToolTip.SetTip(so, "Sum out");
            tabReadouts[4].Add(() => { si.Set(PatchedAt(false, 22)); so.Set(PatchedAt(true, 20)); });
            var util = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Height = 20, Children = {
                Caps("UTIL"), Row(5, Caps("ATTEN 1"), a1), Row(5, Caps("ATTEN 2"), a2), Row(4, Caps("SUM"), si, so) } };
            ToolTip.SetTip(util, "Attenuators scale whatever is patched into Atten 1/2 in; Sum adds its inputs");
            return new DockPanel { LastChildFill = true, Children = { Docked(Divider(util, 3), Avalonia.Controls.Dock.Bottom), m } };
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
                if (cs.Count == 0) host.Children.Add(new TextBlock { Text = "No cables", FontSize = 8, Foreground = DimC });
                foreach (var c in cs)
                {
                    int slot = c.Slot;
                    var dot = new Border { Width = 6, Height = 6, CornerRadius = NotaRadius.Badge, Background = ConsortJacks.ToneBrush(ConsortJacks.Src(c.Src).Tone), VerticalAlignment = VerticalAlignment.Center };
                    var x = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = new Glyph(GlyphKind.Close, 8) { Foreground = MutedC } };
                    x.PointerPressed += (_, e) => { RemoveSlot(slot); e.Handled = true; };
                    var name = new TextBlock { Text = $"{ConsortJacks.ShortName(ConsortJacks.Src(c.Src).Name)} → {ConsortJacks.ShortName(ConsortJacks.Dst(c.Dst).Name)}", FontSize = 9, Foreground = TxtC, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                    var hdr = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 5 };
                    hdr.Children.Add(dot); hdr.Children.Add(Col(name, 1)); hdr.Children.Add(Col(x, 2));
                    List<Action> saved = cur; cur = rowReadouts;
                    var sl = HSlider(AmtId(slot), v => Depth(v), 24, null, true);
                    cur = saved;
                    var dep = new Grid { ColumnDefinitions = new ColumnDefinitions("34,*") };
                    dep.Children.Add(Caps("DEPTH")); dep.Children.Add(Col(sl, 1));
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
            var hint = new TextBlock { Text = "Drag jack to jack · Alt-click to pull · right-click for a list", FontSize = 8, Foreground = DimC, TextWrapping = TextWrapping.Wrap };
            var side = new DockPanel { LastChildFill = true, Children = { Docked(new Border { Margin = new Thickness(0, 0, 0, 3), Child = Caps("CABLES") }, Avalonia.Controls.Dock.Top), Docked(hint, Avalonia.Controls.Dock.Bottom), list } };
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,144") };
            g.Children.Add(new Border { Padding = new Thickness(2, 2, 4, 0), Child = field });
            g.Children.Add(Col(new Border { BorderBrush = BorderIn, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(7, 3, 0, 2), Child = side }, 1));
            return g;
        }
        Control PatchTab()
        {
            var host = new ContentControl();
            Control? mat = null, cab = null;
            void Show() { host.Content = vs.PatchView == 0 ? (mat ??= MatrixView()) : (cab ??= CablesView()); Refresh(); }
            patchShow = Show;
            Show();
            return new Border { Padding = new Thickness(7, 3, 7, 2), Child = host };
        }

        // ======================================================================
        // RIGHT — Voices / Output
        // ======================================================================
        bool Poly() => On("truepoly");
        Control VoicesTab()
        {
            var cells = new Border[4]; var txt = new TextBlock[4];
            var cellRow = new UniformGrid { Rows = 1, Columns = 4 };
            for (int i = 0; i < 4; i++)
            {
                txt[i] = MonoText("—", 8, DimC); txt[i].HorizontalAlignment = HorizontalAlignment.Center;
                cells[i] = new Border { Height = 18, Margin = new Thickness(1.5, 0), CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), Child = txt[i] };
                ToolTip.SetTip(cells[i], $"Oscillator {i + 1}'s note (paraphonic) · the latest voices in true poly");
                cellRow.Children.Add(cells[i]);
            }
            rightReadouts[0].Add(() =>
            {
                for (int i = 0; i < 4; i++)
                {
                    int note = scN > 12 + i ? (int)scope[12 + i] : -1;
                    float lv = scN > 16 + i ? scope[16 + i] : 0;
                    bool on = note >= 0;
                    txt[i].Text = on ? NoteName(note) : "—"; txt[i].Foreground = on ? (lv > 0.01f ? AmberLit : Txt2) : DimC;
                    cells[i].BorderBrush = on && lv > 0.01f ? Amber : NotaPalette.BorderStrong;
                    cells[i].Background = on && lv > 0.01f ? AmberSubtle : Brushes.Transparent;
                }
            });
            var gated = MonoText("gated", 8, DimC);
            var gatedBox = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = gated, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(gatedBox, "Gated glide: only between overlapping (legato) notes");
            gatedBox.PointerPressed += (_, e) => { SetP("glidegated", On("glidegated") ? 0 : 1); Refresh(); e.Handled = true; };
            rightReadouts[0].Add(() => gated.Foreground = On("glidegated") ? AmberLit : DimC);
            Control Line(string cap, Control c, Control? right = null)
            {
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("46,*,Auto"), Height = 20 };
                g.Children.Add(Caps(cap)); g.Children.Add(Col(c, 1)); if (right != null) g.Children.Add(Col(right, 2));
                return g;
            }
            var mode = Chips("voicemode", new[] { "MONO", "DUO", "PARA" }, null, 7, Poly);
            var body = new StackPanel { Spacing = 1, Children = {
                Line("MODE", mode),
                Line("VOICES", cellRow),
                Line("GLIDE", HSlider("glide", v => GlideF(v), 38)),
                Line("TYPE", Chips("glidetype", new[] { "LCR", "LCT", "EXP" }), gatedBox),
                new Border { Height = 1, Background = BorderIn, Margin = new Thickness(0, 3) },
                new StackPanel { Spacing = 4, Margin = new Thickness(0, 2), Children = { Toggle("multitrig", "Multi trig", Poly), Toggle("truepoly", "True poly (16)"), Toggle("unison", "Unison detune") } },
                new Border { Height = 1, Background = BorderIn, Margin = new Thickness(0, 3) },
                Line("DRIFT", HSlider("drift", v => Pct(v), 30)) } };
            return new Border { Padding = new Thickness(8, 5), Child = body };
        }
        Control OutputTab()
        {
            var meterL = new PentadLevelBar { Vertical = true, Width = 11, Height = 56 };
            var meterR = new PentadLevelBar { Vertical = true, Width = 11, Height = 56 };
            rightReadouts[1].Add(() => { if (scN > 3) { meterL.SetLinear(scope[2]); meterR.SetLinear(scope[3]); } });
            var scale = new Grid { RowDefinitions = new RowDefinitions("*,*,*"), Height = 56, Children = {
                MonoText("0", 7, DimC), RowAt(MonoText("−12", 7, DimC), 1), RowAt(MonoText("−48", 7, DimC), 2) } };
            var meters = Row(4, new StackPanel { Spacing = 2, Children = { meterL, Center(Lbl("L", 7, MutedC, FontWeight.Normal)) } },
                                new StackPanel { Spacing = 2, Children = { meterR, Center(Lbl("R", 7, MutedC, FontWeight.Normal)) } }, scale);
            meters.VerticalAlignment = VerticalAlignment.Top;
            var top = Row(12, K("volume", "VOLUME", VolDb, false, Knob.SizeMain, 56), meters);
            var body = new DockPanel { LastChildFill = false, Children = {
                Docked(top, Avalonia.Controls.Dock.Top),
                Docked(Divider(new StackPanel { Spacing = 4, Children = {
                    SliderRow("TUNE", "tune", v => Tune(v), 40, 34, null, true),
                    SliderRow("BEND", "bendrange", v => $"±{1 + Math.Clamp((int)Math.Round(v * 11), 0, 11)}\u2009st", 40, 34),
                    Toggle("velvca", "Velocity → VCA"),
                    Toggle("atcut", "Aftertouch → cut") } }), Avalonia.Controls.Dock.Top),
                Docked(Divider(Row(8, Caps("OVERSAMPLE"), Chips("oversample", new[] { "×1", "×2", "×4" }))), Avalonia.Controls.Dock.Bottom) } };
            ((Control)body.Children[1]).Margin = new Thickness(0, 5, 0, 0);
            return new Border { Padding = new Thickness(8, 5), Child = body };
        }

        // ======================================================================
        // Tab frames + extras
        // ======================================================================
        var centreHost = new ContentControl();
        var rightHost = new ContentControl();
        var extrasHost = new ContentControl { VerticalAlignment = VerticalAlignment.Center };
        var tabBodies = new Control?[5]; var rightBodies = new Control?[2];
        var patchCountTxt = MonoText("", 8, Txt2);
        Control[] extras = new Control[5];
        {
            List<Action> saved = cur;
            cur = tabReadouts[0];
            var pwSl = HSlider("pw", v => Pct(v), 26); pwSl.Width = 84;
            extras[0] = Row(6, Caps("PWM"), pwSl);
            ToolTip.SetTip(extras[0], "Pulse width of the pulse wave (square stays 50\u2009%)");
            cur = tabReadouts[1];
            extras[1] = Chips("filtmode", new[] { "HP/LP ser", "LP/LP st", "HP/LP st" });
            cur = tabReadouts[2];
            var dlyTxt = MonoText("", 7.5, MutedC);
            tabReadouts[2].Add(() => dlyTxt.Text = $"{(On("dlydigital") ? "digital" : "BBD")} · {(On("dlyping") ? "ping-pong" : "stereo")}");
            extras[2] = dlyTxt;
            cur = tabReadouts[3];
            extras[3] = Row(6, Chips("seqmode", new[] { "Off", "SEQ", "ARP" }), Caps("OCT"), Chips("arpoct", new[] { "1", "2", "3" }, null, 7, () => Sel("seqmode", 3) != 2));
            ToolTip.SetTip(extras[3], "Off · SEQ (steps transposed by the key) · ARP (the held chord over 1–3 octaves)");
            cur = saved;
            extras[4] = Row(6, ViewSeg(new[] { "Matrix", "Cables" }, () => vs.PatchView, v => { vs.PatchView = v; patchShow(); }), patchCountTxt);
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

        var (centre, selectCentre) = TabFrame(new[] { "Osc · Mix", "Filters · Env", "LFO · Delay", "Seq", "Patch" }, centreHost, CentreBody, false, vs.Centre,
            t => { vs.Centre = t; if (t != 4) vs.LastNonPatch = t; extrasHost.Content = extras[t]; Refresh(); }, extrasHost);
        var (rightFrame, _) = TabFrame(new[] { "Voices", "Output" }, rightHost, RightBody, true, vs.Right, t => { vs.Right = t; Refresh(); }, null);
        var right = new Border { Width = 172, Background = Panel, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile, Child = rightFrame };
        DockPanel.SetDock(right, Avalonia.Controls.Dock.Right);
        var centreBox = new Border { Background = Panel, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile, Margin = new Thickness(5, 0), Child = centre };

        // ======================================================================
        // Patch-bay row (collapsed) + overlay
        // ======================================================================
        var overlay = new Border { IsVisible = false, Background = RailBg, Focusable = true };
        void OpenOverlay(bool open) { vs.Overlay = open; overlay.IsVisible = open; if (open) { overlay.Focus(); } Refresh(); }
        var chipsHost = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
        var countTxt = MonoText("", 8, AmberLit);
        var openTxt = new TextBlock { FontSize = 8, Foreground = TxtC, VerticalAlignment = VerticalAlignment.Center };
        var openBtn = new Border { Height = 13, Padding = new Thickness(6, 0), CornerRadius = NotaRadius.Badge, Background = OffPill, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = openTxt };
        ToolTip.SetTip(openBtn, "Open the full patch bay (all 42 points) · on the Patch tab: back to the last panel");
        openBtn.PointerPressed += (_, e) =>
        {
            if (vs.Centre == 4) selectCentre(vs.LastNonPatch);
            else OpenOverlay(true);
            e.Handled = true;
        };
        string? stripSig = null;
        common.Add(() =>
        {
            var cs = CableList();
            string s = string.Join(",", cs.Select(c => $"{c.Src}:{c.Dst}"));
            if (s != stripSig)
            {
                stripSig = s;
                chipsHost.Children.Clear();
                foreach (var c in cs)
                {
                    var chip = Row(3, new Border { Width = 6, Height = 6, CornerRadius = NotaRadius.Badge, Background = ConsortJacks.ToneBrush(ConsortJacks.Src(c.Src).Tone), VerticalAlignment = VerticalAlignment.Center },
                                      MonoText(CableChip(c), 8, Txt2));
                    chip.Background = Brushes.Transparent; chip.Cursor = new Cursor(StandardCursorType.Hand);
                    ToolTip.SetTip(chip, $"{CableName(c)} · depth {c.Depth * 100:+0;−0;0}");
                    chip.PointerPressed += (_, e) => { selectCentre(4); e.Handled = true; };
                    chipsHost.Children.Add(chip);
                }
                if (cs.Count == 0) chipsHost.Children.Add(MonoText("no cables — the normals run the voice", 8, DimC));
            }
            countTxt.Text = cs.Count == 1 ? "1 cable" : $"{cs.Count} cables";
            bool onPatch = vs.Centre == 4;
            openTxt.Text = onPatch ? "Collapse ⌃" : "Open ⌄";
            openBtn.BorderBrush = onPatch ? Amber : Brushes.Transparent;
        });
        var stripGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 8 };
        stripGrid.Children.Add(Caps("PATCH BAY")); stripGrid.Children.Add(Col(chipsHost, 1)); stripGrid.Children.Add(Col(countTxt, 2)); stripGrid.Children.Add(Col(openBtn, 3));
        var strip = new Border { Height = 16, Background = RailBg, BorderBrush = Border2, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(8, 0), Child = stripGrid };
        DockPanel.SetDock(strip, Avalonia.Controls.Dock.Bottom);

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
            var info = MonoText("", 8, Txt2);
            overlayReadouts.Add(() => info.Text = $"{CableList().Count} cables · {ConsortJacks.Points} points");
            Border Btn(string t, Action a)
            {
                var b = new Border { Height = 16, Padding = new Thickness(7, 0), CornerRadius = NotaRadius.Badge, Background = OffPill, Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = t, FontSize = 9, Foreground = TxtC, VerticalAlignment = VerticalAlignment.Center } };
                b.PointerPressed += (_, e) => { a(); e.Handled = true; };
                return b;
            }
            var view = ViewSeg(new[] { "Cables", "Matrix" }, () => 0, v => { if (v == 1) { vs.PatchView = 0; OpenOverlay(false); selectCentre(4); patchShow(); } });
            var topGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto,Auto"), ColumnSpacing = 10 };
            topGrid.Children.Add(Lbl("Patch bay", 10, AmberLit, FontWeight.SemiBold));
            topGrid.Children.Add(Col(info, 1)); topGrid.Children.Add(Col(view, 2));
            topGrid.Children.Add(Col(Btn("Clear all", ClearAll), 4)); topGrid.Children.Add(Col(Btn("Close", () => OpenOverlay(false)), 5));
            var top = new Border { Height = 26, BorderBrush = Border2, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(10, 0), Child = topGrid };
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
                    var dep = MonoText($"{c.Depth * 100:+0;−0;0}", 8, tone);
                    var depBox = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.SizeNorthSouth), Child = dep };
                    ToolTip.SetTip(depBox, "Depth — drag up / down (double-click: +50)");
                    bool drag = false; double y0 = 0; float v0 = 0;
                    depBox.PointerPressed += (_, e) =>
                    {
                        if (!e.GetCurrentPoint(depBox).Properties.IsLeftButtonPressed) return;
                        if (e.ClickCount == 2) { SetP(AmtId(slot), 0.75f); Refresh(); e.Handled = true; return; }
                        drag = true; y0 = e.GetPosition(depBox).Y; v0 = G(AmtId(slot)); Begin(AmtId(slot)); e.Pointer.Capture(depBox); e.Handled = true;
                    };
                    depBox.PointerMoved += (_, e) => { if (!drag) return; float v = Math.Clamp(v0 + (float)(y0 - e.GetPosition(depBox).Y) / 200f, 0, 1); Raw(AmtId(slot), v); dep.Text = $"{(v - 0.5f) * 200:+0;−0;0}"; };
                    depBox.PointerReleased += (_, e) => { if (drag) { drag = false; End(AmtId(slot)); e.Pointer.Capture(null); Refresh(); } };
                    bottomHost.Children.Add(Row(4, new Border { Width = 7, Height = 7, CornerRadius = NotaRadius.Control, Background = tone, VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = CableName(c), FontSize = 9, Foreground = TxtC, VerticalAlignment = VerticalAlignment.Center }, depBox));
                }
            });
            var note = new TextBlock { Text = "A cable breaks the normal at Gate / Filt in / VCA in / Ext in · loops run with a one-sample delay", FontSize = 8, Foreground = DimC, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var botGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") , ColumnSpacing = 12 };
            botGrid.Children.Add(new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxWidth = 470, Content = bottomHost });
            botGrid.Children.Add(Col(Right(note), 1));
            var bottom = new Border { Height = 22, BorderBrush = Border2, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(10, 0), Child = botGrid };
            return new DockPanel { LastChildFill = true, Children = { Docked(top, Avalonia.Controls.Dock.Top), Docked(bottom, Avalonia.Controls.Dock.Bottom), new Border { Padding = new Thickness(6, 2), Child = field } } };
        }
        overlay.Child = OverlayBody();
        overlay.KeyDown += (_, e) => { if (e.Key == Key.Escape) { OpenOverlay(false); e.Handled = true; } };

        // ======================================================================
        // Status strip
        // ======================================================================
        string ModeName() => Poly() ? "True poly" : Sel("voicemode", 3) switch { 0 => "MONO", 1 => "DUO", _ => "PARA" };
        string FiltModeName() => Sel("filtmode", 3) switch { 0 => "HP/LP series", 1 => "LP/LP parallel stereo", _ => "HP/LP stereo" };
        string StatusText()
        {
            int busy = scN > 1 ? (int)scope[1] : 0;
            switch (vs.Centre)
            {
                case 1: return $"{FiltModeName()} · spacing {Space(G("spacing"))} · env amount {EnvAmt(G("fenvamt"))}{(On("basscomp") ? " · bass comp" : "")}";
                case 2:
                {
                    string rate = On("lfosync") ? $"sync {LfoRate(G("lforate"))}" : LfoRate(G("lforate"));
                    string dest = On("lfodest") ? "osc 2 + 4" : "all osc";
                    return $"LFO {LfoNames[Sel("lfowave", 6)]} {rate} → {dest} · {(On("dlydigital") ? "digital delay" : "BBD")} {(On("dlyping") ? "ping-pong" : "stereo")}, mix {G("dlymix") * 100:0}\u2009%";
                }
                case 3:
                {
                    int m = Sel("seqmode", 3);
                    if (m == 0) return "Sequencer off · pick SEQ or ARP, then hold a key";
                    var types = StepTypes(); int len = SeqLen();
                    int rat = types.Take(len).Count(t => t == 1), ties = types.Take(len).Count(t => t == 2);
                    string swing = $"swing {50 + G("seqswing") * 25:0}\u2009%";
                    return m == 1
                        ? $"{len} steps · {rat} ratchet{(rat == 1 ? "" : "s")} ×{Ratchet()} · {ties} tie{(ties == 1 ? "" : "s")} · {swing} · transposed from the keyboard"
                        : $"Arp {new[] { "up", "down", "random" }[Sel("seqorder", 3)]} over {1 + Sel("arpoct", 3)}\u2009oct · {SeqRates[Sel("seqrate", 6)]} · {swing}{(On("seqlatch") ? " · latched" : "")}";
                }
                case 4:
                {
                    var broken = CableList().Where(c => ConsortJacks.Normalled.Contains(c.Dst)).Select(c => ConsortJacks.Dst(c.Dst).Name).Distinct().ToList();
                    string nb = broken.Count == 0 ? "all normals intact" : "normals broken at " + string.Join(" and ", broken);
                    return (vs.PatchView == 0 ? "Matrix view · drag a cell for depth, Alt-click to remove · " : $"Jack strip · {ConsortJacks.Points} points · ") + nb;
                }
                default:
                {
                    var sync = new List<string>();
                    if (On("o2sync")) sync.Add("osc 2 sync → 1"); if (On("o4sync")) sync.Add("osc 4 sync → 3");
                    return $"{ModeName()} · {busy} {(Poly() ? "voice" : "note")}{(busy == 1 ? "" : "s")}{(sync.Count > 0 ? " · " + string.Join(", ", sync) : "")} · drift {G("drift") * 100:0}\u2009%";
                }
            }
        }
        var statusLeft = new TextBlock { FontSize = 8, Foreground = Txt2, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var statusRight = MonoText("", 8, Txt2);
        common.Add(() =>
        {
            statusLeft.Text = StatusText();
            if (scN > 8)
            {
                string rate = $"{scope[6] / 1000:0.#}\u2009kHz";
                statusRight.Text = vs.Centre == 3 ? $"{rate} · sync to host {scope[8]:0}\u2009BPM · CPU {scope[4] * 100:0}\u2009%"
                                                  : $"{rate} · ×{(int)scope[5]} OS · 0 smp · CPU {scope[4] * 100:0}\u2009%";
            }
        });
        var statusGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        statusGrid.Children.Add(statusLeft); statusGrid.Children.Add(Col(statusRight, 1));
        var status = new Border { Height = 18, Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(8, 0), Child = statusGrid };
        DockPanel.SetDock(status, Avalonia.Controls.Dock.Bottom);

        // ---- assemble ---------------------------------------------------------------
        var bodyRow = new DockPanel { LastChildFill = true, Margin = new Thickness(5, 5, 5, 4), Children = { wheels, right, centreBox } };
        var main = new DockPanel { LastChildFill = true, Background = RailBg, Children = { status, strip, bodyRow } };
        var root = new Panel { Children = { main, overlay } };

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

        // local: a tab frame (bar + swapping body), optional extras on the right of the bar.
        (Control, Action<int>) TabFrame(string[] tabs, ContentControl host, Func<int, Control> body, bool centered, int initial, Action<int>? changed, Control? extrasCtl)
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
            void Select(int i) { sel = i; Hi(); host.Content = body(i); changed?.Invoke(i); }
            var bar = centered ? (Avalonia.Controls.Panel)new UniformGrid { Rows = 1 } : new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < tabs.Length; i++)
            {
                int iv = i;
                var b = new Border { Padding = new Thickness(8, 0), BorderThickness = new Thickness(0, 0, 0, 2), BorderBrush = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand),
                    Child = new TextBlock { Text = tabs[i], FontSize = 9, Foreground = MutedC, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center } };
                b.PointerPressed += (_, _) => Select(iv);
                btns[i] = b; bar.Children.Add(b);
            }
            var barDock = new DockPanel { Height = 20, LastChildFill = centered };
            if (extrasCtl != null) { var ex = new Border { Padding = new Thickness(0, 0, 7, 0), Child = extrasCtl }; DockPanel.SetDock(ex, Avalonia.Controls.Dock.Right); barDock.Children.Add(ex); }
            if (!centered) DockPanel.SetDock(bar, Avalonia.Controls.Dock.Left);
            barDock.Children.Add(bar);
            var barBorder = new Border { BorderBrush = Border2, BorderThickness = new Thickness(0, 0, 0, 1), Child = barDock };
            DockPanel.SetDock(barBorder, Avalonia.Controls.Dock.Top);
            Select(sel);
            return (new DockPanel { LastChildFill = true, Children = { barBorder, host } }, Select);
        }
    }
}
