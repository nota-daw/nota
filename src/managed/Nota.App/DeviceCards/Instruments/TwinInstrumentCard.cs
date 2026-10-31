// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Twin editor (instrument kind 18, specs/055), first cut (US1):
// the voice of one part in a 700 × 260 card.
//
//   Morph 64    the part's Morph — on screen on every tab, it is what the instrument is about.
//   Centre      eight tabs, each a signal chain of islands:
//               Osc    — Osc 1 ▸ Osc 2 ▸ Noise: wave (triangle → saw → square), width, Metal,
//                        tuning, level and the balance between the two filters; Osc 2's sync,
//                        FM into Osc 1 and into the multimode cutoff, low-frequency mode.
//               Filter — Multimode ▸ Route ▸ Ladder, with Brute feedback and drive in between.
//               Env    — filter, amp and mod envelopes.
//               LFO    — three LFOs (one at a time) and the vibrato depth (LFO 1, by the mod wheel).
//               Matrix — the routes (up to 64: source → destination, a depth per side, so the
//                        depth morphs too) and the part's eight macros, which are sources.
//               FX     — the part's effects: Mod FX (chorus / flanger / phaser / ensemble) ▸ Delay
//                        (free or synced, ping-pong) ▸ Reverb, each switchable, all of it morphing;
//                        the section's own switch bypasses it for Nota's effect devices.
//               Seq    — the sequencer (a pattern of steps on a grid of sixteen: notes, velocity,
//                        gate or tie; pattern, length, rate, swing, transpose / latch, step record,
//                        motion lanes, "To clip") or the arpeggiator (mode, octaves, gate, hold).
//               Voice  — the voice mode, the voice cap, glide, unison detune, stereo spread,
//               drift, bend range, the part's volume and pan, and the expression pad (X → the
//               multimode cutoff, Y → the morph, Z → vibrato depth, until the matrix routes them).
//
// The A / B switch under Morph picks the side every knob edits (and so the side automation and
// MIDI learn write to, FR-024); each knob also ticks the other side's value outside its groove
// and, while the morph sits between, dots where the sound is now. A→B, B→A and Swap rewrite a
// side in one undo step. Single / Layer / Split and the Lower / Upper switch sit at the right of
// the tab row (US9): every tab edits the part chosen there; Voice → Part sets the split key
// (learnable from a played key), the voice share and the part's octave, and its Parts menu
// copies, swaps or loads the parts.
// Layout (specs/055/design): sections butt on rules with no chevrons between them, and the
// sections of a tab keep one row order — header, graph or chooser, knob rows, slider rows — so
// their knob rows share a baseline. There is no status line: the header badge and the side
// switch already say it. Neutral ink is the parameter; teal marks modulation amounts; the
// accent only what is live or chosen; an effect that is off greys its body (Inactive).
// Everything is a plugin param, so the card follows automation live.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed class TwinInstrumentCard : IInstrumentCard
{
    public bool BodyOnly => true;
    public string Subtitle => "Morphing poly";
    public double CardWidth => 700;

    private const double MorphW = 70, TabH = 22;
    // The side each track's card edits (0 = A, 1 = B), its part (0 = Lower, 1 = Upper) and its
    // open tab: editor state, kept across rebuilds (the side and part switches rebuild the card).
    private static readonly Dictionary<int, int> EditSides = new();
    private static readonly Dictionary<int, int> EditParts = new();
    private static readonly Dictionary<int, int> OpenTabs = new();

    private static readonly string[] TabNames = { "Osc", "Filter", "Env", "LFO", "Matrix", "FX", "Seq", "Voice" };
    private static readonly string[] SeqRates = { "1/4", "1/8", "1/8T", "1/16", "1/16T", "1/32" };
    private static readonly string[] ArpModes = { "Up", "Down", "Up/down", "Random", "As played", "Chord" };
    private static readonly int[] SeqLengths = { 4, 8, 12, 16, 24, 32, 48, 64 };
    private static readonly string[] ModFxTypes = { "Chorus", "Flanger", "Phaser", "Ensemble" };
    private static readonly string[] DelayDivs = { "1/16", "1/8T", "1/8", "1/8.", "1/4T", "1/4", "1/4.", "1/2" };
    private static readonly string[] VoiceModes = { "Poly", "Mono", "Legato", "Unison" };
    private static readonly string[] Layouts = { "Single", "Layer", "Split" };
    private static readonly string[] PartNames = { "Lower", "Upper" };
    private static readonly string[] SeqTargets = { "Lower", "Upper", "Both" };
    private static readonly string[] Slopes = { "12", "24" };
    private static readonly string[] LfoShapes = { "Sine", "Tri", "Saw", "Square", "S&H", "Smooth" };
    private static readonly string[] LfoModes = { "Voice", "Global" };
    private static readonly string[] SpreadModes = { "Voice", "Key", "Unison" };
    private static readonly string[] SyncNames = { "8 bars", "4 bars", "2 bars", "1 bar", "1/2", "1/4.", "1/4", "1/8.", "1/4T", "1/8", "1/8T", "1/16", "1/16T", "1/32" };

    // The engine's own maps (TwinVoice.h), so a readout says what it does.
    private static double ExpMap(float v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0f, 1f));
    private static string Pct(float v) => NotaNum.Pct(v);
    private static string Bip(float v) => NotaNum.Unit((v - 0.5f) * 200, "+0;−0;0", "%");
    private static string Cutoff(float v) => NotaNum.Hz(20 * Math.Pow(1000, Math.Clamp(v, 0f, 1f)));
    private static string EnvOct(float v) => NotaNum.Unit((v - 0.5f) * 16, "+0.0;−0.0;0", "oct");
    private static string Coarse(float v) => NotaNum.Unit(Math.Clamp(Math.Round(v * 48), 0, 48) - 24, "+0;−0;0", "st");
    private static string Fine(float v) => NotaNum.Unit(Math.Round((v - 0.5f) * 100), "+0;−0;0", "c");
    private static string Attack(float v) => NotaNum.Time(ExpMap(v, 0.0005, 10));
    private static string Decay(float v) => NotaNum.Time(ExpMap(v, 0.003, 15));
    private static string Delay(float v) => v < 0.002f ? "off" : NotaNum.Time(ExpMap(v, 0.001, 5));
    private static string Glide(float v) => v < 0.002f ? "off" : NotaNum.Time(ExpMap(v, 0.005, 5));
    private static string Fade(float v) => v < 0.002f ? "off" : NotaNum.Time(ExpMap(v, 0.01, 10));
    private static string LfoRate(float v)
    {
        double hz = ExpMap(v, 0.02, 40);
        return hz >= 9.995 ? NotaNum.Hz(hz) : NotaNum.Unit(hz, hz < 0.995 ? "0.00" : "0.0", "Hz");
    }
    private static string Wave(float v) => v <= 0.02f ? "tri" : v < 0.48f ? "tri–saw" : v <= 0.52f ? "saw" : v < 0.98f ? "saw–sqr" : "square";
    private static string Width(float v) => NotaNum.Pct(0.05f + 0.9f * v);
    private static string Semis(float v) => NotaNum.Unit(v, "0.00", "st");
    private static string Detune(float v) => NotaNum.Unit(Math.Round(v * 50), "±0", "c");
    private static string Voices(float v) => (4 + Math.Clamp((int)Math.Round(v * 8), 0, 8)).ToString(NotaNum.Culture);
    private static int LowerShare(float share, int cap) => Math.Clamp(2 + 2 * Math.Clamp((int)Math.Round(share * 4), 0, 4), 2, cap - 2);
    private static string SplitKey(float v) => NoteName(Math.Clamp((int)Math.Round(v * 127), 0, 127));
    private static string Octave(float v) => NotaNum.Unit(Math.Clamp((int)Math.Round(v * 4), 0, 4) - 2, "+0;−0;0", "oct");
    private static string PanText(float v)
    {
        double p = (v - 0.5) * 200;
        return Math.Abs(p) < 0.5 ? "C" : (p < 0 ? "L" : "R") + NotaNum.Str(Math.Abs(p), "0");
    }

    // Two sizes (the instrument's "view" param, InstrumentView): L — every tab — and S, the
    // performance view: Morph and the side switch, the pad, and four sound knobs.
    public double WidthFor(IAudioEngine engine, int trackId) => InstrumentView.IsMini(engine, trackId) ? 260 : CardWidth;
    public Control? HeaderAccessory(DeviceCardContext ctx)
        => MidiCardHeader.SizeToggle(ctx, () => InstrumentView.IsMini(ctx.Engine, ctx.TrackId), m => InstrumentView.SetMini(ctx.Engine, ctx.TrackId, m));

    public string? VoiceLabel(IAudioEngine engine, int trackId, int active)
    {
        int n = engine.PluginParamCount(trackId, -1);
        for (int i = 0; i < n; i++)
            if (engine.PluginParamId(trackId, -1, i) == "voices")
                return $"{active}/{Voices(engine.PluginParamGet(trackId, -1, i))}";
        return null;
    }

    public Control Build(DeviceCardContext ctx)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId;
        int pc = engine.PluginParamCount(track, -1);
        var idx = new Dictionary<string, int>();
        for (int i = 0; i < pc; i++) idx[engine.PluginParamId(track, -1, i)] = i;
        int I(string id) => idx.TryGetValue(id, out var i) ? i : -1;
        float G(string id) => I(id) is var i and >= 0 ? engine.PluginParamGet(track, -1, i) : 0f;
        void SetP(string id, float v) { if (I(id) is var i and >= 0) engine.PluginParamSet(track, -1, i, Math.Clamp(v, 0f, 1f)); }
        void Begin(string id) { if (I(id) >= 0) engine.BeginAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id); }
        void End(string id) { if (I(id) >= 0) engine.EndAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id); }
        int Sel(string id, int n) => Math.Clamp((int)Math.Round(G(id) * (n - 1)), 0, n - 1);

        int sideIx = EditSides.TryGetValue(track, out var es) ? es : 0;
        int layout = Sel("mode", 3);
        int partIx = layout == 0 ? 0 : EditParts.TryGetValue(track, out var ep) ? ep : 0;
        string PartKey = partIx == 0 ? "lo" : "up", Part = PartKey + ".";
        string Side = Part + (sideIx == 0 ? "a." : "b."), Other = Part + (sideIx == 0 ? "b." : "a.");
        var readouts = new List<Action>();
        int tab = OpenTabs.TryGetValue(track, out var ot) ? ot : 0;
        Nota.Application.Twin.TwinPatterns.Pattern[]? pats = null; // the sequencer's patterns (Seq tab)
        // Inactive looks, applied after the readouts (a switch's repaint would undo them).
        var dims = new List<Action>();
        Action showTab = () => { };

        void Refresh()
        {
            foreach (var r in readouts) r();
            foreach (var d in dims) d();
        }

        static TextBlock Cap(string t) => new()
        {
            Text = NotaText.Label(t), FontSize = NotaType.KnobLabel, FontWeight = FontWeight.Medium,
            Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center,
        };
        static TextBlock Title(string t) => new()
        {
            Text = NotaText.Label(t), FontSize = NotaType.DeviceSection, FontWeight = FontWeight.SemiBold,
            Foreground = NotaPalette.TextMuted, VerticalAlignment = VerticalAlignment.Center,
        };

        // A knob cell on a param id; the learn name is the param's full display name.
        // `rows`: a knob in a rebuilt route row follows through that row list instead of the
        // card's live-follow registry, so rebuilding the rows leaves nothing behind.
        Control PKnob(string id, string label, Func<float, string> fmt, double size = 34, double cellW = 46, IBrush? arc = null, List<Action>? rows = null)
        {
            if (!idx.TryGetValue(id, out var pi)) return new Panel();
            var value = new TextBlock { Text = fmt(G(id)), Foreground = TextPrimary };
            var knob = new Knob(G(id), 1.0)
            {
                Accent = true, ArcColor = arc, Default = engine.InstrumentParamDefault(track, pi),
                Width = size, Height = size,
            };
            knob.Format = v => fmt((float)v);
            knob.ValueChanged += v => { engine.PluginParamSet(track, -1, pi, (float)v); value.Text = fmt((float)v); Refresh(); };
            knob.GestureBegin += () => Begin(id);
            knob.GestureEnd += () => End(id);
            if (rows is null) ctx.AddInstFader(pi, knob, value, fmt);
            else rows.Add(() => { if (!knob.Dragging) { knob.Value = G(id); value.Text = fmt(G(id)); } });
            MidiLearn.Bind(knob, MidiTarget.PluginParam(track, -1, pi), engine.PluginParamName(track, -1, pi));
            if (id.StartsWith(Side, StringComparison.Ordinal) && I(Other + id[Side.Length..]) >= 0)
            {
                string oid = Other + id[Side.Length..];
                void Marks()
                {
                    float own = G(id), other = G(oid), m = G(Part + "morph");
                    float a = sideIx == 0 ? own : other, b = sideIx == 0 ? other : own;
                    float sounding = a + (b - a) * m;
                    knob.OtherValue = Math.Abs(other - own) > 1e-4f ? other : double.NaN;
                    knob.SoundingValue = Math.Abs(sounding - own) > 2e-3f ? sounding : double.NaN;
                    ToolTip.SetTip(knob, $"A {fmt(a)} · B {fmt(b)} · now {fmt(sounding)}");
                }
                (rows ?? readouts).Add(Marks);
                Marks();
            }
            return KnobCell(label, knob, value, cellW);
        }

        Control Chips(string id, string[] names, double padX = 6)
        {
            int n = names.Length;
            var seg = Segments(names, () => Sel(id, n), iv => { SetP(id, iv / (float)(n - 1)); Refresh(); }, out var sync, padX: padX);
            readouts.Add(sync);
            if (I(id) is var pi and >= 0) MidiLearn.Bind(seg, MidiTarget.PluginParam(track, -1, pi), engine.PluginParamName(track, -1, pi));
            return seg;
        }

        Control Toggle(string label, string id)
        {
            var host = Switch(label, () => G(id) >= 0.5f, () => { SetP(id, G(id) >= 0.5f ? 0f : 1f); Refresh(); }, out var sync);
            readouts.Add(sync);
            if (I(id) is var pi and >= 0) MidiLearn.Bind(host, MidiTarget.PluginParam(track, -1, pi), engine.PluginParamName(track, -1, pi));
            return host;
        }

        static StackPanel Row(params Control[] cells)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var c in cells) p.Children.Add(c);
            return p;
        }

        // A section of a tab: title (and an optional control at its right) on a 16 px header row,
        // over rows. `w` fixes its width; 0 lets it take what the tab has left (the last one).
        Control Island(string title, Control? head, double w, params Control[] rows)
        {
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Height = 16 };
            header.Children.Add(Title(title));
            if (head is not null) { head.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(head, 1); header.Children.Add(head); }
            var stack = new StackPanel { Spacing = 4, Margin = new Thickness(8, 5, 8, 4) };
            stack.Children.Add(header);
            foreach (var r in rows) stack.Children.Add(r);
            var s = Section(stack);
            if (w > 0) s.Width = w;
            return s;
        }
        // A tab's sections side by side, butted on their rules; the last one fills.
        static DockPanel Chain(params Control[] islands)
        {
            var p = new DockPanel { LastChildFill = true };
            foreach (var c in islands) { DockPanel.SetDock(c, Dock.Left); p.Children.Add(c); }
            return p;
        }
        // A row that keeps a fixed height, so the rows after it line up across sections.
        static Control Fixed(double h, Control? c = null) => new Border { Height = h, Child = c, VerticalAlignment = VerticalAlignment.Top };

        // A slider row on a param, with words under its track (the ends of a balance, or the marks
        // of a continuous choice). `bare` drops the label and value columns: the marks say it.
        Control PSlider(string id, string label, Func<float, string> fmt, string[] marks, bool bare = false, double labelW = 38, double valueW = 34)
        {
            if (!idx.TryGetValue(id, out var pi)) return new Panel();
            float def = engine.InstrumentParamDefault(track, pi);
            var row = SliderRow(bare ? "" : label, () => G(id), v => { SetP(id, (float)v); Refresh(); }, () => bare ? "" : fmt(G(id)), out var sync,
                begin: () => Begin(id), end: () => End(id), reset: () => { Begin(id); SetP(id, def); End(id); Refresh(); },
                labelWidth: bare ? 0 : labelW, valueWidth: bare ? 0 : valueW);
            readouts.Add(sync);
            MidiLearn.Bind(row, MidiTarget.PluginParam(track, -1, pi), engine.PluginParamName(track, -1, pi));
            ToolTip.SetTip(row, label);
            return new StackPanel { Spacing = 0, Children = { row, SliderMarks(marks, bare ? 0 : labelW + 8, bare ? 8 : valueW + 8) } };
        }

        // A value well on a param (the ADSR times under an envelope graph), named under it.
        Control PWell(string id, string label, Func<float, string> fmt)
        {
            if (!idx.TryGetValue(id, out var pi)) return new Panel();
            var well = new TwinValueWell { Format = v => fmt((float)v), Default = engine.InstrumentParamDefault(track, pi) };
            well.Value = G(id);
            well.ValueChanged += v => { SetP(id, (float)v); Refresh(); };
            well.GestureBegin += () => Begin(id);
            well.GestureEnd += () => End(id);
            readouts.Add(() => { if (!well.Dragging) well.Value = G(id); });
            MidiLearn.Bind(well, MidiTarget.PluginParam(track, -1, pi), engine.PluginParamName(track, -1, pi));
            ValueEntry.Attach(well, ValueEntry.Probing(label, () => G(id), v => { SetP(id, (float)v); Refresh(); }, () => fmt(G(id)),
                () => Begin(id), () => End(id), reset: () => { SetP(id, (float)well.Default); Refresh(); }), well.Readout);
            var name = new TextBlock
            {
                Text = NotaText.Label(label), FontSize = NotaType.KnobLabel, FontWeight = FontWeight.Medium, Foreground = TextSecondary,
                HorizontalAlignment = HorizontalAlignment.Center, LineHeight = 10,
            };
            return new StackPanel { Spacing = 1, Children = { well, name } };
        }

        // A hint line inside a section (Ink 3, 8 px).
        static TextBlock Note(string t) => new()
        {
            Text = t, FontSize = NotaType.KnobLabel, Foreground = TextTertiary, TextWrapping = TextWrapping.Wrap, LineHeight = 11,
        };

        // A section body that goes inactive while `off` holds (the effect is switched off, the
        // param does nothing in this mode); it stays editable.
        T DimWhen<T>(T c, Func<bool> off) where T : Control
        {
            bool last = false;
            dims.Add(() => { bool o = off(); if (o || last) Inactive.Set(c, o, interactive: true); last = o; });
            return c;
        }

        string S(string id) => Side + id;
        // A graph bound to the side being edited; it redraws with the card's readouts.
        T Graph<T>(T g, double w, double h) where T : TwinParamGraph
        {
            g.Width = w; g.Height = h;
            g.Get = G; g.Set = (id, v) => { SetP(id, v); Refresh(); }; g.Begin = Begin; g.End = End;
            g.Bind(Side);
            readouts.Add(g.Refresh);
            return g;
        }
        static string Bal(float v) => v <= 0.005f ? "multi" : v >= 0.995f ? "ladder" : NotaNum.Pct(v);
        // ---- Osc tab ------------------------------------------------------------
        // Each source: its wave over the full width (drag sideways for Wave), two knob rows, and
        // the balance between the two filters as a slider with its ends named.
        string[] FilterEnds = { "multi", "ladder" };
        Control Osc(int n)
        {
            string o = $"o{n}";
            var top = Row(PKnob(S(o + "wave"), "Wave", Wave), PKnob(S(o + "pw"), "Width", Width),
                PKnob(S(o + "metal"), "Metal", Pct), PKnob(S(o + "level"), "Level", Pct));
            var tune = n == 1
                ? Row(PKnob(S(o + "coarse"), "Coarse", Coarse), PKnob(S(o + "fine"), "Fine", Fine))
                : Row(PKnob(S(o + "coarse"), "Coarse", Coarse), PKnob(S(o + "fine"), "Fine", Fine),
                    PKnob(S("fm21"), "FM 1", Pct, arc: Teal), PKnob(S("fmfilt"), "FM cut", Pct, arc: Teal));
            tune.HorizontalAlignment = HorizontalAlignment.Left;
            var head = n == 1 ? null : new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Toggle("Sync", S("sync")), Toggle("Low", S("o2lo")) } };
            return Island($"Osc {n}", head, 212, Graph(new TwinWaveView(n), 196, 34), top, Fixed(53, tune), PSlider(S(o + "bal"), "To filter", Bal, FilterEnds));
        }
        var noiseKnobs = Row(PKnob(S("noiselevel"), "Level", Pct), PKnob(S("noisecolor"), "Colour", v => v < 0.2f ? "white" : v < 0.6f ? "pink" : "red"));
        noiseKnobs.HorizontalAlignment = HorizontalAlignment.Left;
        var oscBody = Chain(Osc(1), Osc(2),
            Island("Noise", null, 0, Graph(new TwinNoiseView(), 166, 34), noiseKnobs, Fixed(53), PSlider(S("noisebal"), "To filter", Bal, FilterEnds)));

        // ---- Filter tab ---------------------------------------------------------
        // Mode and Route are continuous (they morph), so they are sliders whose marks name the
        // regions; the route draws itself under its slider.
        const double CutW = 64;
        var filterBody = Chain(
            Island("Multimode", null, 144,
                Fixed(24, PSlider(S("mmmode"), "Mode", v => v < 0.17f ? "LP" : v < 0.5f ? "BP" : v < 0.83f ? "HP" : "notch", new[] { "LP", "BP", "HP", "notch" }, bare: true)),
                Row(PKnob(S("mmcut"), "Cutoff", Cutoff, 44, CutW), PKnob(S("mmres"), "Resonance", Pct, 44, CutW)),
                Row(PKnob(S("mmkey"), "Key", Pct, cellW: CutW, arc: Teal), PKnob(S("mmenv"), "Env", EnvOct, cellW: CutW, arc: Teal))),
            Island("Route", null, 132,
                Fixed(24, PSlider(S("route"), "Route", v => v <= 0.01f ? "series" : v >= 0.99f ? "parallel" : Pct(v), new[] { "series", "parallel" }, bare: true)),
                Graph(new TwinRouteView(), 116, 62),
                Row(PKnob(S("brute"), "Brute", Pct, cellW: 58), PKnob(S("drive"), "Drive", Pct, cellW: 58))),
            Island("Ladder", Chips(S("ldslope"), Slopes), 144,
                Fixed(24, Note("Slope in dB per octave")),
                Row(PKnob(S("ldcut"), "Cutoff", Cutoff, 44, CutW), PKnob(S("ldres"), "Resonance", Pct, 44, CutW)),
                Row(PKnob(S("ldkey"), "Key", Pct, cellW: CutW, arc: Teal), PKnob(S("ldenv"), "Env", EnvOct, cellW: CutW, arc: Teal))),
            Island("Response", null, 0, Graph(new TwinFilterView(), 172, 172)));

        // ---- Env tab ------------------------------------------------------------
        // An envelope: drag its handles on the graph (attack · decay and sustain · release), or the
        // four named times under it; the knobs keep velocity and the curve (delay for the mod env).
        Control Env(string title, string e, bool mod, double w)
        {
            var times = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), ColumnSpacing = 3 };
            var cells = new[] { PWell(S(e + "a"), "Attack", Attack), PWell(S(e + "d"), "Decay", Decay), PWell(S(e + "s"), "Sustain", Pct), PWell(S(e + "r"), "Release", Decay) };
            for (int k = 0; k < 4; k++) { Grid.SetColumn(cells[k], k); times.Children.Add(cells[k]); }
            var knobs = mod
                ? Row(PKnob(S(e + "dly"), "Delay", Delay, cellW: 58), PKnob(S(e + "vel"), "Velocity", Pct, cellW: 58, arc: Teal))
                : Row(PKnob(S(e + "vel"), "Velocity", Pct, cellW: 58, arc: Teal), PKnob(S(e + "curve"), "Curve", Pct, cellW: 58));
            return Island(title, mod ? Toggle("Loop", S("menvloop")) : null, w, Graph(new TwinEnvView(e, mod), 186, 70), times, knobs);
        }
        var envBody = Chain(Env("Filter env", "fenv", false, 204), Env("Amp env", "aenv", false, 204), Env("Mod env", "menv", true, 0));

        // ---- LFO tab ------------------------------------------------------------
        // One LFO at a time (the picker in the header): shape, switches, then rate, fade and amount
        // as main knobs beside a large graph with the live phase.
        int lfoSel = 0;
        var lfoGraphs = new TwinLfoView[3];
        Control Lfo(int n)
        {
            string l = $"lfo{n}";
            double ks = n == 3 ? 34 : 44, kw = n == 3 ? 44 : 64;
            var rateCell = PKnob(S(l + "rate"), "Rate", LfoRate, ks, kw);
            var divCell = PKnob(S(l + "div"), "Division", v => SyncNames[Math.Clamp((int)Math.Round(v * 13), 0, 13)], ks, kw);
            readouts.Add(() => { bool sync = G(S(l + "sync")) >= 0.5f; rateCell.IsVisible = !sync; divCell.IsVisible = sync; });
            var switches = new DockPanel { LastChildFill = false };
            var sw = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { Toggle("Sync", S(l + "sync")), Toggle("Retrig", S(l + "retrig")) } };
            var mode = Chips(S(l + "mode"), LfoModes, 5);
            DockPanel.SetDock(sw, Dock.Left); DockPanel.SetDock(mode, Dock.Right);
            switches.Children.Add(sw); switches.Children.Add(mode);
            var shapes = Segments(LfoShapes, () => Sel(S(l + "shape"), 6), iv => { SetP(S(l + "shape"), iv / 5f); Refresh(); }, out var shapeSync, fill: true, padX: 2);
            readouts.Add(shapeSync);
            if (I(S(l + "shape")) is var si and >= 0) MidiLearn.Bind(shapes, MidiTarget.PluginParam(track, -1, si), engine.PluginParamName(track, -1, si));
            var knobs = n == 3
                ? Row(rateCell, divCell, PKnob(S(l + "fade"), "Fade", Fade, cellW: kw), PKnob(S(l + "amt"), "Amount", Pct, cellW: kw, arc: Teal),
                    PKnob(S("lfo3sym"), "Symmetry", Pct, cellW: kw), PKnob(S("lfo3curve"), "Curve", Bip, cellW: kw))
                : Row(rateCell, divCell, PKnob(S(l + "fade"), "Fade", Fade, ks, kw), PKnob(S(l + "amt"), "Amount", Pct, ks, kw, Teal));
            knobs.HorizontalAlignment = HorizontalAlignment.Left;
            var body = new StackPanel { Spacing = 6, Width = 232, Children = { shapes, switches, knobs } };
            var shape = Graph(new TwinLfoView(n), 240, 166);
            lfoGraphs[n - 1] = shape;
            var page = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(body, Dock.Left); page.Children.Add(body);
            shape.HorizontalAlignment = HorizontalAlignment.Right;
            page.Children.Add(shape);
            return page;
        }
        var lfoPages = new Control[] { Lfo(1), Lfo(2), Lfo(3) };
        var lfoPick = Segments(new[] { "LFO 1", "LFO 2", "LFO 3" }, () => lfoSel, iv =>
        {
            lfoSel = iv;
            for (int k = 0; k < lfoPages.Length; k++) lfoPages[k].IsVisible = k == lfoSel;
            Refresh();
        }, out var lfoPickSync, padX: 5);
        readouts.Add(lfoPickSync);
        var lfoHost = new Panel();
        foreach (var pg in lfoPages) lfoHost.Children.Add(pg);
        for (int k = 1; k < lfoPages.Length; k++) lfoPages[k].IsVisible = false;
        var vibNote = Note("LFO 1 × mod wheel");
        vibNote.HorizontalAlignment = HorizontalAlignment.Center;
        var lfoBody = Chain(Island("LFO", lfoPick, 500, lfoHost),
            Island("Vibrato", null, 0, Row(PKnob(S("vibamt"), "Depth", Semis, 44, 80, Teal)), vibNote));

        // ---- the tab strip ----------------------------------------------------
        // ---- expression pad -----------------------------------------------------
        Control PadView(double size = 104, bool legend = true, double width = 0)
        {
            var pad = new TwinExpressionPad { Width = width > 0 ? width : size + 8, Height = size };
            pad.XyChanged = (x, y) => { SetP("padx", (float)x); SetP("pady", (float)y); Refresh(); };
            pad.ZChanged = z => { SetP("padz", (float)z); Refresh(); };
            pad.GestureBegin = () => { Begin("padx"); Begin("pady"); Begin("padz"); };
            pad.GestureEnd = () => { End("padx"); End("pady"); End("padz"); };
            pad.Reset = () => { SetP("padx", 0.5f); SetP("pady", 0.5f); Refresh(); };
            readouts.Add(() => pad.SetValues(G("padx"), G("pady"), G("padz")));
            if (I("padx") is var pi and >= 0) MidiLearn.Bind(pad, MidiTarget.PluginParam(track, -1, pi), "Pad X");
            var legendText = new TextBlock { FontSize = NotaType.KnobValue, FontFamily = NotaFonts.MonoFamily, Foreground = TextSecondary, HorizontalAlignment = HorizontalAlignment.Center };
            readouts.Add(() => legendText.Text = $"X {Bip(G("padx"))} · Y {Bip(G("pady"))} · Z {Pct(G("padz"))}");
            ToolTip.SetTip(pad, "X → multimode cutoff · Y → morph · hold for Z → vibrato");
            return legend ? new StackPanel { Spacing = 3, Children = { pad, legendText } } : pad;
        }

        // ---- Matrix tab ---------------------------------------------------------
        // Routes as rows (a 29 × 77 grid would not read at this size): source ▾ → destination ▾,
        // the depth on the side being edited, and remove. Rows rebuild on any route edit.
        var routeRows = new StackPanel { Spacing = 0 };
        var rowReadouts = new List<Action>();
        readouts.Add(() => { foreach (var r in rowReadouts) r(); });
        var routeCount = new TextBlock { FontSize = NotaType.KnobValue, FontFamily = NotaFonts.MonoFamily, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center };
        var fullNote = new TextBlock { FontSize = NotaType.KnobLabel, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, IsVisible = false, Text = "Matrix full (64 routes)" };
        string sideKey = sideIx == 0 ? "a" : "b";
        Control Picker(Func<string> text, string tip, Action<Control> open)
        {
            var tb = new TextBlock { FontSize = NotaType.RowLabel, Foreground = TextPrimary, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 120 };
            var host = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Padding = new Thickness(3, 0), Child = Glyph.WithChevron(tb, 7), VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(host, tip);
            rowReadouts.Add(() => tb.Text = text());
            tb.Text = text();
            host.PointerPressed += (_, e) => { if (e.GetCurrentPoint(host).Properties.IsLeftButtonPressed) { open(host); e.Handled = true; } };
            return host;
        }
        void ShowSources(Control at, Action<int> pick)
        {
            var f = new MenuFlyout();
            for (int k = 1; k < Nota.Application.Twin.TwinMatrix.Sources.Length; k++)
            {
                int kv = k;
                var mi = new MenuItem { Header = Nota.Application.Twin.TwinMatrix.Sources[k] };
                mi.Click += (_, _) => pick(kv);
                f.Items.Add(mi);
            }
            f.ShowAt(at);
        }
        void ShowDestinations(Control at, Action<int> pick)
        {
            var f = new MenuFlyout();
            var groups = new Dictionary<string, MenuItem>();
            for (int k = 1; k < Nota.Application.Twin.TwinMatrix.DestinationCount; k++)
            {
                int kv = k;
                string name = Nota.Application.Twin.TwinMatrix.DestinationName(engine, track, k);
                string grp = Nota.Application.Twin.TwinMatrix.DestinationGroup(engine, track, k);
                var mi = new MenuItem { Header = grp.Length > 0 ? name[(grp.Length + 1)..] : name };
                mi.Click += (_, _) => pick(kv);
                if (grp.Length == 0) { f.Items.Add(mi); continue; }
                if (!groups.TryGetValue(grp, out var g)) { g = new MenuItem { Header = grp }; groups[grp] = g; f.Items.Add(g); }
                g.Items.Add(mi);
            }
            f.ShowAt(at);
        }
        // A route's depth on the edited side: a centre-zero teal slider (it is a modulation
        // amount) with the other side's depth as its mark, and the value beside it.
        Control DepthCell(string amtId)
        {
            if (!idx.TryGetValue(amtId, out var pi)) return new Panel();
            string oid = Other + amtId[Side.Length..];
            static string Fmt(float v) => NotaNum.Unit((v - 0.5f) * 200, "+0;−0;0", "%");
            SliderTrack? bar = null;
            var row = SliderRow("", () => G(amtId), v => engine.PluginParamSet(track, -1, pi, (float)v), () => Fmt(G(amtId)), out var sync,
                begin: () => Begin(amtId), end: () => End(amtId), reset: () => { Begin(amtId); SetP(amtId, 0.5f); End(amtId); },
                bipolar: true, modulation: true, valueWidth: 34, withTrack: t => bar = t);
            row.ColumnSpacing = 4;
            MidiLearn.Bind(row, MidiTarget.PluginParam(track, -1, pi), engine.PluginParamName(track, -1, pi));
            void Sync()
            {
                sync();
                if (bar is not null) bar.Marker = Math.Abs(G(oid) - G(amtId)) > 1e-4f ? G(oid) : double.NaN;
                ToolTip.SetTip(row, $"Depth · A {Fmt(G(Part + "a." + amtId[5..]))} · B {Fmt(G(Part + "b." + amtId[5..]))}");
            }
            rowReadouts.Add(Sync);
            Sync();
            return row;
        }
        const string MatrixCols = "14,104,10,138,*,14";
        void BuildRoutes()
        {
            routeRows.Children.Clear();
            rowReadouts.Clear();
            var routes = Nota.Application.Twin.TwinMatrix.Routes(engine, track, PartKey);
            routeCount.Text = $"{routes.Count} / {Nota.Application.Twin.TwinMatrix.Slots}";
            foreach (var rt in routes)
            {
                int slot = rt.Slot;
                string srcId = $"{Part}m{slot}.src", dstId = $"{Part}m{slot}.dst", amtId = $"{Part}{sideKey}.m{slot}amt";
                Nota.Application.Twin.TwinMatrix.Route Now() => Nota.Application.Twin.TwinMatrix.Routes(engine, track, PartKey).Find(x => x.Slot == slot);
                void Rewrite(int src, int dst)
                {
                    var n = Now();
                    Nota.Application.Twin.TwinMatrix.Add(engine, track, src, dst, n.DepthA, n.DepthB, PartKey, slot);
                    BuildRoutes(); Refresh();
                }
                var num = new TextBlock { Text = slot.ToString(NotaNum.Culture), FontSize = NotaType.KnobValue, FontFamily = NotaFonts.MonoFamily, Foreground = NotaPalette.TextFaint, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
                var src = Picker(() => Nota.Application.Twin.TwinMatrix.Sources[Math.Clamp((int)Math.Round(G(srcId) * 63), 0, Nota.Application.Twin.TwinMatrix.Sources.Length - 1)],
                    "Source", h => ShowSources(h, k => Rewrite(k, Now().Destination)));
                var dst = Picker(() => Nota.Application.Twin.TwinMatrix.DestinationName(engine, track, (int)Math.Round(G(dstId) * 255)),
                    "Destination", h => ShowDestinations(h, k => Rewrite(Now().Source, k)));
                var arrow = new Glyph(GlyphKind.ChevronRight, 7) { Foreground = NotaPalette.TextFaint, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                var depth = DepthCell(amtId);
                var remove = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = new Glyph(GlyphKind.Close, 8) { Foreground = TextTertiary } };
                ToolTip.SetTip(remove, "Remove the route");
                remove.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(remove).Properties.IsLeftButtonPressed) return;
                    Nota.Application.Twin.TwinMatrix.Remove(engine, track, slot, PartKey);
                    BuildRoutes(); Refresh(); e.Handled = true;
                };
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions(MatrixCols), ColumnSpacing = 4, Height = 20 };
                Control[] cols = { num, src, arrow, dst, depth, remove };
                for (int k = 0; k < cols.Length; k++) { Grid.SetColumn(cols[k], k); row.Children.Add(cols[k]); }
                routeRows.Children.Add(new Border { BorderBrush = NotaPalette.Rule, BorderThickness = new Thickness(0, 1, 0, 0), Child = row });
            }
            fullNote.IsVisible = false;
        }
        var addRoute = TextButton("+ Route");
        addRoute.Padding = new Thickness(6, 1);
        ToolTip.SetTip(addRoute, "Add a route (LFO 1 → Ladder Cutoff) in the first free slot");
        addRoute.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(addRoute).Properties.IsLeftButtonPressed) return;
            int slot = Nota.Application.Twin.TwinMatrix.Add(engine, track, 1, Nota.Application.Twin.TwinMatrix.DestinationOfSide("ldcut"), 0.25f, 0.25f, PartKey);
            BuildRoutes();
            fullNote.IsVisible = slot < 0;
            Refresh(); e.Handled = true;
        };
        BuildRoutes();
        var routesHead = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { fullNote, routeCount, addRoute } };
        var colHeads = new Grid { ColumnDefinitions = new ColumnDefinitions(MatrixCols), ColumnSpacing = 4, Height = 12 };
        foreach (var (text, col) in new[] { ("Source", 1), ("Destination", 3), ($"Depth {(sideIx == 0 ? "A" : "B")}", 4) })
        {
            var h = Cap(text);
            Grid.SetColumn(h, col); colHeads.Children.Add(h);
        }
        var routesScroll = new ScrollViewer { Height = 152, Content = routeRows, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Control Macros()
        {
            var g = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 2, LineSpacing = 4 };
            for (int k = 1; k <= 8; k++) g.Children.Add(PKnob(Part + $"macro{k}", $"Macro {k}", Pct, cellW: 44));
            return g;
        }
        var matrixBody = Chain(
            Island("Routes", routesHead, 412, colHeads, routesScroll),
            Island("Macros", null, 0, Macros(), Note("Macros are sources in the matrix.")));

        // ---- FX tab -------------------------------------------------------------
        // Mod FX ▸ Delay ▸ Reverb, each with a preview of what it does; an effect that is off greys
        // its body (it stays editable), so nothing inside it shows the accent. "All" bypasses the
        // section, for Nota's own effect devices.
        const double FxW = 178, FxK = 54;
        var dlyTimeCell = PKnob(S("dlytime"), "Time", v => NotaNum.Time(Math.Max(0.001, v * 2.0)), 44, FxK);
        var dlyDivCell = PKnob(S("dlydiv"), "Division", v => DelayDivs[Math.Clamp((int)Math.Round(v * 7), 0, 7)], 44, FxK);
        readouts.Add(() => { bool sync = G(S("dlysync")) >= 0.5f; dlyTimeCell.IsVisible = !sync; dlyDivCell.IsVisible = sync; });
        Control FxBody(string onId, params Control[] rows)
        {
            var p = new StackPanel { Spacing = 4 };
            foreach (var r in rows) p.Children.Add(r);
            return DimWhen(p, () => G(S(onId)) < 0.5f);
        }
        var modFxType = Segments(ModFxTypes, () => Sel(S("modfxtype"), 4), iv => { SetP(S("modfxtype"), iv / 3f); Refresh(); }, out var modFxTypeSync, fill: true, padX: 1, fontSize: 8);
        readouts.Add(modFxTypeSync);
        if (I(S("modfxtype")) is var mti and >= 0) MidiLearn.Bind(modFxType, MidiTarget.PluginParam(track, -1, mti), engine.PluginParamName(track, -1, mti));
        var fxBody = Chain(
            Island("Mod FX", Toggle("On", S("modfxon")), 190, FxBody("modfxon",
                modFxType, Graph(new TwinFxView(TwinFxView.Kind.ModFx), 174, 40),
                Row(PKnob(S("modfxrate"), "Rate", Pct, 44, FxK), PKnob(S("modfxdepth"), "Depth", Pct, 44, FxK), PKnob(S("modfxmix"), "Mix", Pct, 44, FxK)))),
            Island("Delay", Toggle("On", S("dlyon")), FxW, FxBody("dlyon",
                Fixed(18, Row(Toggle("Sync", S("dlysync")), new Border { Width = 6 }, Toggle("Ping-pong", S("dlypingpong")))),
                Graph(new TwinFxView(TwinFxView.Kind.Delay), 162, 40),
                Row(dlyTimeCell, dlyDivCell, PKnob(S("dlyfb"), "Feedback", Pct, 44, FxK), PKnob(S("dlymix"), "Mix", Pct, 44, FxK)))),
            Island("Reverb", Toggle("On", S("revon")), 166, FxBody("revon",
                Fixed(18), Graph(new TwinFxView(TwinFxView.Kind.Reverb), 150, 40),
                Row(PKnob(S("revsize"), "Size", Pct, 44, 49), PKnob(S("revdamp"), "Damping", Pct, 44, 49), PKnob(S("revmix"), "Mix", Pct, 44, 49)))),
            Island("All", null, 0, Toggle("On", Part + "fxon"), Note("Off bypasses all three, to use Nota's effect devices instead.")));

        // ---- Seq tab ------------------------------------------------------------
        // The patterns are structural state read from the state blob (re-read when the engine's
        // pattern revision moves); edits go through the instrument's action channel, one undo
        // step each, on the pattern the sequencer plays (seq.pattern).
        pats = null;
        int seenRev = -1, page = 0, laneSel = 0, lastPlay = -1;
        bool stepRec = false, motionRec = false;
        var grid = new TwinStepGrid { Height = 92 };
        int EditPat() => Sel("seq.pattern", 8);
        Nota.Application.Twin.TwinPatterns.Pattern? Pat() => pats?[EditPat()];
        void LoadPats() { pats = Nota.Application.Twin.TwinPatterns.FromState(engine.GetPluginState(track, -1)); }
        void SyncGrid()
        {
            grid.Pattern = Pat();
            int len = grid.Pattern?.Length ?? 16;
            page = Math.Clamp(page, 0, (len - 1) / 16);
            grid.Page = page;
            grid.Lane = laneSel;
            grid.InvalidateVisual();
        }
        void SeqAct(Nota.Application.Twin.TwinPatterns.Act a, int iarg = 0, float farg = 0f)
        {
            Nota.Application.Twin.TwinPatterns.Do(engine, track, Nota.Application.Twin.TwinPatterns.Act.SelectPattern, EditPat());
            Nota.Application.Twin.TwinPatterns.Do(engine, track, a, iarg, farg);
            LoadPats(); SyncGrid(); Refresh();
        }
        var stepInfo = new TextBlock { FontSize = NotaType.KnobValue, FontFamily = NotaFonts.MonoFamily, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, Width = 130, TextTrimming = TextTrimming.CharacterEllipsis };
        void ShowStep()
        {
            var p = Pat();
            stepInfo.Text = p is null || grid.Selected < 0 || grid.Selected >= p.Length ? "click a step"
                : $"step {grid.Selected + 1}: {Nota.Application.Twin.TwinPatterns.StepText(p.Steps[grid.Selected])}";
        }
        grid.SelectStep = _ => ShowStep();
        grid.CommitVelocity = (st, v) => SeqAct(Nota.Application.Twin.TwinPatterns.Act.SetStepVelocity, st, v / 127f);
        grid.CommitGate = (st, g) => SeqAct(Nota.Application.Twin.TwinPatterns.Act.SetStepGate, st, g >= Nota.Application.Twin.TwinPatterns.GateTie ? 1.01f : g / 100f);
        readouts.Add(() => { if (pats is null) LoadPats(); SyncGrid(); ShowStep(); });

        Control SeqPicker(Func<string> text, string tip, Func<string[]> items, Action<int> pick)
        {
            var tb = new TextBlock { FontSize = NotaType.RowLabel, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center };
            var host = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Padding = new Thickness(3, 0), Child = Glyph.WithChevron(tb, 7), VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(host, tip);
            readouts.Add(() => tb.Text = text());
            tb.Text = text();
            host.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(host).Properties.IsLeftButtonPressed) return;
                var f = new MenuFlyout();
                var list = items();
                for (int k = 0; k < list.Length; k++) { int kv = k; var mi = new MenuItem { Header = list[k] }; mi.Click += (_, _) => pick(kv); f.Items.Add(mi); }
                f.ShowAt(host); e.Handled = true;
            };
            return host;
        }
        Control TextAction(string text, string tip, Action act)
        {
            var b = TextButton(text);
            b.Padding = new Thickness(6, 1);
            ToolTip.SetTip(b, tip);
            b.VerticalAlignment = VerticalAlignment.Center;
            b.PointerPressed += (_, e) => { if (e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) { act(); e.Handled = true; } };
            return b;
        }
        // A text action that is on (recording, learning): a solid accent key with derived ink.
        static void Lit(Control c, bool on)
        {
            if (c is not Border b) return;
            b.Background = on ? Brass : Raised;
            if (b.Child is TextBlock t) t.Foreground = on ? OnAccent : TextPrimary;
        }
        void SetParam(string id, float v) { Begin(id); SetP(id, v); End(id); Refresh(); }
        var patPick = SeqPicker(() => $"Pattern {EditPat() + 1}", "The pattern that plays and that you edit (switches at the end of the running one)",
            () => Enumerable.Range(1, 8).Select(i => $"Pattern {i}").ToArray(), k => { SetParam("seq.pattern", k / 7f); page = 0; LoadPats(); SyncGrid(); });
        var lenPick = SeqPicker(() => $"{Pat()?.Length ?? 16} steps", "Pattern length", () => SeqLengths.Select(n => $"{n} steps").ToArray(),
            k => SeqAct(Nota.Application.Twin.TwinPatterns.Act.SetLength, SeqLengths[k]));
        var ratePick = SeqPicker(() => SeqRates[Sel("seq.rate", 6)], "Step rate", () => SeqRates, k => SetParam("seq.rate", k / 5f));
        var recBtn = TextAction("Record", "Step record: play a chord, lift it, and it fills the next step", () =>
        {
            stepRec = !stepRec;
            Nota.Application.Twin.TwinPatterns.Do(engine, track, Nota.Application.Twin.TwinPatterns.Act.SelectPattern, EditPat());
            Nota.Application.Twin.TwinPatterns.Do(engine, track, Nota.Application.Twin.TwinPatterns.Act.StepRecordArm, stepRec ? 1 : 0);
            Refresh();
        });
        readouts.Add(() => Lit(recBtn, stepRec));
        var toClip = TextAction("To clip", "Write this pattern as a MIDI clip at the playhead's bar and switch the sequencer off", () =>
        {
            double at = Math.Floor(engine.PositionBeats / 4.0) * 4.0;
            Nota.Application.Twin.TwinPatterns.ToClip(engine, track, EditPat(), at);
            ctx.NotifyChanged(); Refresh();
        });
        var pagePick = Segments(new[] { "1–16", "17–32", "33–48", "49–64" }, () => page, k => { page = k; SyncGrid(); }, out var pageSync, padX: 3);
        readouts.Add(() => { pageSync(); pagePick.IsVisible = (Pat()?.Length ?? 16) > 16; });
        var restBtn = TextAction("Rest", "Make the selected step a rest", () => { if (grid.Selected >= 0) SeqAct(Nota.Application.Twin.TwinPatterns.Act.ClearStep, grid.Selected); });
        var laneTargets = new List<(string Name, int Index)> { ("None", -1), ("Morph", I(Part + "morph")), ("Pad X", I("padx")), ("Pad Y", I("pady")), ("Pad Z", I("padz")) };
        foreach (var id in Nota.Application.Twin.TwinMatrix.SideDestinationIds)
            if (I(Side + id) is var li and >= 0) laneTargets.Add((Nota.Application.Twin.TwinMatrix.DestinationName(engine, track, Nota.Application.Twin.TwinMatrix.DestinationOfSide(id)) + $" ({(sideIx == 0 ? "A" : "B")})", li));
        var lanePick = Segments(new[] { "1", "2", "3", "4" }, () => laneSel, k => { laneSel = k; SyncGrid(); Refresh(); }, out var laneSync, padX: 4);
        readouts.Add(laneSync);
        var targetPick = SeqPicker(() =>
            {
                int tg = Pat()?.Lanes[laneSel].Target ?? -1;
                return tg < 0 ? "No target" : laneTargets.FirstOrDefault(x => x.Index == tg).Name ?? engine.PluginParamName(track, -1, tg);
            }, "What this motion lane sets, step by step (on the side being edited)", () => laneTargets.Select(x => x.Name).ToArray(),
            k => SeqAct(Nota.Application.Twin.TwinPatterns.Act.MotionTarget, laneSel << 16 | (laneTargets[k].Index < 0 ? 0xFFFF : laneTargets[k].Index)));
        var motionRecBtn = TextAction("Rec motion", "Record the lane: each step takes the target's value as the pattern passes it", () =>
        {
            motionRec = !motionRec;
            Nota.Application.Twin.TwinPatterns.Do(engine, track, Nota.Application.Twin.TwinPatterns.Act.MotionRecordArm, motionRec ? laneSel : -1);
            Refresh();
        });
        readouts.Add(() => Lit(motionRecBtn, motionRec));
        var clearLane = TextAction("Clear", "Clear the lane", () => SeqAct(Nota.Application.Twin.TwinPatterns.Act.ClearMotion, laneSel));
        var smoothSw = Switch("Smooth", () => Pat()?.Lanes[laneSel].Smooth ?? false,
            () => SeqAct(Nota.Application.Twin.TwinPatterns.Act.MotionMode, laneSel, Pat()?.Lanes[laneSel].Smooth == true ? 0f : 1f), out var smoothSync);
        ToolTip.SetTip(smoothSw, "Glide from step to step instead of jumping");
        readouts.Add(smoothSync);
        // Live: the playing step, the record cursor, chords played while recording, recorded motion.
        var seqScope = new float[32];
        ctx.AddDeviceRefresher(() =>
        {
            if (engine.InstrumentScope(track, seqScope) < 25) return;
            if ((int)seqScope[3] != seenRev) { seenRev = (int)seqScope[3]; LoadPats(); }
            int play = (int)seqScope[0];
            if (stepRec) Nota.Application.Twin.TwinPatterns.Do(engine, track, Nota.Application.Twin.TwinPatterns.Act.StepRecordPoll);
            if (motionRec && play >= 0 && play != lastPlay && Pat()?.Lanes[laneSel].Target is int tg and >= 0)
                Nota.Application.Twin.TwinPatterns.Do(engine, track, Nota.Application.Twin.TwinPatterns.Act.MotionSet, laneSel << 8 | play, engine.PluginParamGet(track, -1, tg));
            lastPlay = play;
            grid.PlayStep = play;
            grid.RecStep = (int)seqScope[24];
            SyncGrid();
        });
        // Swing as a table-row knob with its value beside it (the row stays one line tall).
        Control SeqSwing()
        {
            int si = I("seq.swing");
            var value = new TextBlock { Width = 30, FontSize = NotaType.KnobValue, FontFamily = NotaFonts.MonoFamily, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center };
            var knob = new Knob(G("seq.swing"), 1.0) { Inline = true, Accent = true, Default = 0, Format = v => Pct((float)v) };
            knob.ValueChanged += v => { SetP("seq.swing", (float)v); value.Text = Pct((float)v); };
            knob.GestureBegin += () => Begin("seq.swing");
            knob.GestureEnd += () => End("seq.swing");
            if (si >= 0) { ctx.AddInstFader(si, knob, value, Pct); MidiLearn.Bind(knob, MidiTarget.PluginParam(track, -1, si), "Sequencer Swing"); }
            value.Text = Pct(G("seq.swing"));
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { knob, value } };
        }
        var steps = Note("Steps");
        var pageRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { steps, pagePick } };
        readouts.Add(() => steps.IsVisible = pagePick.IsVisible);
        Control Bar(Control left, Control? right = null)
        {
            var d = new DockPanel { LastChildFill = false, Height = 18 };
            if (right is not null) { DockPanel.SetDock(right, Dock.Right); d.Children.Add(right); }
            DockPanel.SetDock(left, Dock.Left); d.Children.Add(left);
            return d;
        }
        StackPanel Line(params Control[] cs)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            foreach (var c in cs) { c.VerticalAlignment = VerticalAlignment.Center; p.Children.Add(c); }
            return p;
        }
        var seqPage = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                Bar(Line(patPick, lenPick, ratePick, Cap("Swing"), SeqSwing(), Toggle("Transpose", "seq.transpose"), Toggle("Latch", "seq.latch"))),
                grid,
                Bar(Line(Cap("Motion"), lanePick, targetPick, smoothSw, motionRecBtn, clearLane), pageRow),
                Bar(Line(stepInfo, restBtn), Note("Click a step to edit it; hold keys while Record is on to enter chords.")),
            },
        };
        var arpHint = Note("Hold a chord to hear it; Hold keeps it after you let go.");
        arpHint.Width = 150;
        var arpKnobs = Row(PKnob("arp.gate", "Gate", Pct, 44, 64), PKnob("seq.swing", "Swing", Pct, 44, 64));
        arpKnobs.HorizontalAlignment = HorizontalAlignment.Left;
        var arpPage = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Bar(Line(Cap("Mode"), SeqPicker(() => ArpModes[Sel("arp.mode", 6)], "Arpeggio order", () => ArpModes, k => SetParam("arp.mode", k / 5f)),
                    Cap("Rate"), SeqPicker(() => SeqRates[Sel("seq.rate", 6)], "Step rate", () => SeqRates, k => SetParam("seq.rate", k / 5f)),
                    Cap("Octaves"), Chips("arp.oct", new[] { "1", "2", "3", "4" }, 4), Toggle("Hold", "arp.hold"))),
                Line(arpKnobs, arpHint),
            },
        };
        readouts.Add(() => { bool arp = G("seq.kind") >= 0.5f; seqPage.IsVisible = !arp; arpPage.IsVisible = arp; recBtn.IsVisible = !arp; toClip.IsVisible = !arp; });
        // The header carries what acts on the whole sequencer: the part it plays, record, to clip,
        // on, and Seq / Arp.
        var seqHead = Line();
        if (layout != 0)
        {
            var target = Chips("seq.target", SeqTargets, 4);
            ToolTip.SetTip(target, "The part the sequencer or arpeggiator plays");
            seqHead.Children.Add(Cap("Plays"));
            seqHead.Children.Add(target);
        }
        foreach (var c in new[] { recBtn, toClip, Toggle("On", "seq.on"), Chips("seq.kind", new[] { "Seq", "Arp" }, 5) }) { c.VerticalAlignment = VerticalAlignment.Center; seqHead.Children.Add(c); }
        var seqBody = Chain(Island("Sequencer", seqHead, 0, new Panel { Children = { seqPage, arpPage } }));

        // ---- Voice tab ----------------------------------------------------------
        // Voices ▸ Stereo ▸ Output (in Layer / Split the part's own section: volume, pan, octave,
        // the split key — learnable from a played key, Split only — and the voice share) ▸ Pad.
        // The section's Parts menu copies, swaps or loads the parts.
        // Parts: copy / swap them, or load a factory sound into one (Upper of a Single patch → Layer).
        // The user's saved Nota Twin presets (the app's presets folder), with their params.
        static IEnumerable<(string Path, string Title, IReadOnlyDictionary<string, float> Params)> UserTwinPresets()
        {
            if (App.Services?.GetService(typeof(IPresetLibrary)) is not IPresetLibrary lib
                || App.Services.GetService(typeof(IPresetStore)) is not IPresetStore store
                || App.Services.GetService(typeof(ISettingsService)) is not ISettingsService settings) yield break;
            foreach (var p in lib.List(settings.PresetsFolder()))
                if (p.Type == "builtin-instrument" && store.Describe(p.Path) is { BuiltinKind: Nota.Application.Twin.TwinMatrix.Kind }
                    && store.InstrumentParams(p.Path) is { } prm)
                    yield return (p.Path, p.DisplayName, prm);
        }
        Control partsMenu = null!;
        partsMenu = TextAction("Parts", "Copy or swap the parts, or load a preset into one", () =>
        {
            var f = new MenuFlyout();
            void Item(ItemsControl into, string header, Action act)
            {
                var mi = new MenuItem { Header = header };
                mi.Click += (_, _) => { act(); ctx.NotifyChanged(); ctx.RequestRebuild(); };
                into.Items.Add(mi);
            }
            var menu = new MenuItem(); // a holder: its items move into the flyout below
            if (layout != 0)
            {
                Item(menu, "Copy Lower → Upper", () => Nota.Application.Twin.TwinParts.Apply(engine, track, Nota.Application.Twin.TwinParts.Edit.CopyLowerToUpper));
                Item(menu, "Copy Upper → Lower", () => Nota.Application.Twin.TwinParts.Apply(engine, track, Nota.Application.Twin.TwinParts.Edit.CopyUpperToLower));
                Item(menu, "Swap parts", () => Nota.Application.Twin.TwinParts.Apply(engine, track, Nota.Application.Twin.TwinParts.Edit.Swap));
            }
            foreach (var (part, name) in new[] { ("lo", "Load into Lower"), ("up", "Load into Upper") })
            {
                var sub = new MenuItem { Header = name };
                foreach (var p in ctx.Factory?.All() ?? Array.Empty<FactoryPresetInfo>())
                {
                    if (!p.IsInstrument || p.BuiltinKind != Nota.Application.Twin.TwinMatrix.Kind || ctx.Factory!.InstrumentParams(p.Id) is not { } prm) continue;
                    Item(sub, p.DisplayName, () =>
                    {
                        Nota.Application.Twin.TwinParts.LoadIntoPart(engine, track, prm, part);
                        EditParts[track] = part == "lo" ? 0 : 1;
                    });
                }
                // The user's own Twin presets, after the factory ones.
                bool first = true;
                foreach (var (path, title, prm) in UserTwinPresets())
                {
                    if (first && sub.Items.Count > 0) sub.Items.Add(new Separator());
                    first = false;
                    Item(sub, title, () =>
                    {
                        Nota.Application.Twin.TwinParts.LoadIntoPart(engine, track, prm, part);
                        EditParts[track] = part == "lo" ? 0 : 1;
                    });
                }
                sub.IsEnabled = sub.Items.Count > 0;
                menu.Items.Add(sub);
            }
            foreach (var it in menu.Items.Cast<MenuItem>().ToList()) { menu.Items.Remove(it); f.Items.Add(it); }
            f.ShowAt(partsMenu);
        });
        Control BendRange()
        {
            var seg = Chips("bendrange", new[] { "±2", "±5", "±12", "±24" }, 5);
            ToolTip.SetTip(seg, "Pitch-bend range in semitones");
            return new StackPanel { Spacing = 3, Children = { Cap("Bend range"), seg } };
        }
        Control Output()
        {
            if (layout == 0)
                return Island("Output", partsMenu, 140,
                    Row(PKnob(Part + "vol", "Volume", Pct, 44, 58), PKnob(Part + "pan", "Pan", PanText, 44, 58)), BendRange());
            bool learn = false;
            int lastKey = int.MinValue;
            var learnBtn = TextAction("Learn", "Play a key: it becomes the first key of the Upper part", () => { learn = !learn; lastKey = int.MinValue; Refresh(); });
            readouts.Add(() => Lit(learnBtn, learn));
            var keyScope = new float[32];
            ctx.AddDeviceRefresher(() =>
            {
                if (!learn || engine.InstrumentScope(track, keyScope) < 26) return;
                int k = (int)keyScope[25];
                if (lastKey != int.MinValue && k >= 0 && k != lastKey) { learn = false; SetParam("splitkey", k / 127f); }
                lastKey = k;
            });
            string ShareText(float v)
            {
                int cap = 4 + Math.Clamp((int)Math.Round(G("voices") * 8), 0, 8), lo = LowerShare(v, cap);
                return $"{lo} + {cap - lo}";
            }
            var second = layout == 2
                ? Row(PKnob("splitkey", "Split", SplitKey, cellW: 46), PKnob("share", "Voices", ShareText, cellW: 46), new Border { Padding = new Thickness(2, 10, 0, 0), Child = learnBtn })
                : Row(PKnob("share", "Voices", ShareText, cellW: 46));
            second.HorizontalAlignment = HorizontalAlignment.Left;
            var first = Row(PKnob(Part + "vol", "Volume", Pct, cellW: 46), PKnob(Part + "pan", "Pan", PanText, cellW: 46), PKnob(Part + "octave", "Octave", Octave, cellW: 46));
            first.HorizontalAlignment = HorizontalAlignment.Left;
            return Island($"{PartNames[partIx]} part", partsMenu, 160, first, second, BendRange());
        }
        int VoiceMode() => Sel(Part + "voicemode", 4);
        var detune = DimWhen(PKnob(S("unidetune"), "Detune", Detune, 44, 58), () => VoiceMode() != 3);
        var detuneNote = Note("Detune works in Unison.");
        readouts.Add(() => detuneNote.IsVisible = VoiceMode() != 3);
        var spreadBy = Chips(S("spreadmode"), SpreadModes, 5);
        var padNote = Note("X cutoff · Y morph · Z vibrato");
        var voiceBody = Chain(
            Island("Voices", Chips(Part + "voicemode", VoiceModes, 4), 200,
                Row(PKnob("voices", "Voices", Voices, 44, 58), PKnob(Part + "glide", "Glide", Glide, 44, 58), detune),
                Toggle("Glide only legato", Part + "glidelegato"), detuneNote),
            Island("Stereo", null, layout == 0 ? 132 : 118,
                Row(PKnob(S("spread"), "Spread", Pct, 44, 54), PKnob(S("drift"), "Drift", Pct, 44, 54)),
                new StackPanel { Spacing = 3, Children = { Cap("Spread by"), spreadBy } }),
            Output(),
            Island("Pad", null, 0, PadView(layout == 0 ? 104 : 96), padNote));

        var bodies = new Control[] { oscBody, filterBody, envBody, lfoBody, matrixBody, fxBody, seqBody, voiceBody };
        var tabCells = new Border[TabNames.Length];
        var tabTexts = new TextBlock[TabNames.Length];
        var tabRow = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < TabNames.Length; i++)
        {
            int iv = i;
            var tb = new TextBlock { Text = TabNames[i], FontSize = NotaType.DeviceSection, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var cell = new Border { Padding = new Thickness(7, 0), BorderThickness = new Thickness(0, 0, 0, 2), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = tb };
            cell.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed) return;
                tab = iv; OpenTabs[track] = iv; showTab(); e.Handled = true;
            };
            cell.PointerEntered += (_, _) => { if (iv != tab) tb.Foreground = TextPrimary; };
            cell.PointerExited += (_, _) => { if (iv != tab) tb.Foreground = TextTertiary; };
            tabCells[i] = cell; tabTexts[i] = tb; tabRow.Children.Add(cell);
        }
        // The chosen tab: heading ink over a 2 px accent underline (chosen is what the accent is for).
        showTab = () =>
        {
            for (int i = 0; i < TabNames.Length; i++)
            {
                bool on = i == tab;
                tabCells[i].BorderBrush = on ? Brass : Brushes.Transparent;
                tabTexts[i].Foreground = on ? NotaPalette.TextHeading : TextTertiary;
                bodies[i].IsVisible = on;
            }
            Refresh();
        };
        var tabHost = new Panel { ClipToBounds = true };
        foreach (var b in bodies) tabHost.Children.Add(b);
        var tabGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        // Right of the tabs: in Layer / Split the part every tab edits, then Single / Layer / Split.
        var layoutSeg = Segments(Layouts, () => layout, iv =>
        {
            if (iv == layout) return;
            SetParam("mode", iv / 2f);
            ctx.RequestRebuild();
        }, out var layoutSync, padX: 6);
        ToolTip.SetTip(layoutSeg, "One sound, two layered on every key, or two split across the keyboard");
        readouts.Add(layoutSync);
        var partBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 0, 0, 2), VerticalAlignment = VerticalAlignment.Center, Children = { layoutSeg } };
        if (layout != 0)
        {
            var partSeg = Segments(PartNames, () => partIx, iv =>
            {
                if (iv == partIx) return;
                EditParts[track] = iv;
                ctx.RequestRebuild();
            }, out var partSync, padX: 6);
            ToolTip.SetTip(partSeg, "The part every tab edits");
            readouts.Add(partSync);
            partBar.Children.Insert(0, partSeg);
        }
        var tabBar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        tabBar.Children.Add(tabRow);
        Grid.SetColumn(partBar, 1); tabBar.Children.Add(partBar);
        tabGrid.Children.Add(new Border { Height = TabH, BorderBrush = NotaPalette.Rule, BorderThickness = new Thickness(0, 0, 0, 1), Child = tabBar });
        Grid.SetRow(tabHost, 1); tabGrid.Children.Add(tabHost);
        var tabPanel = Section(tabGrid);

        // ---- the morph column ---------------------------------------------------
        // Morph (main knob), where it sounds now between A and B (the edited end in the accent),
        // the side the knobs edit, and the side copies as quiet actions.
        var scope = new float[32];
        Control MorphColumn(bool actions)
        {
            var col = new StackPanel { Spacing = 3, Margin = new Thickness(0, 2, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, Width = 58 };
            var title = Title("Morph"); title.HorizontalAlignment = HorizontalAlignment.Center;
            col.Children.Add(title);
            col.Children.Add(PKnob(Part + "morph", "", v => NotaNum.Pct(v), 44, 58));
            // The part (accent) and every sounding voice (dots), live, with A and B at the ends.
            var strip = new TwinMorphStrip { Height = 8, VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(strip, "The morph as it sounds now: the part, and each sounding voice");
            TextBlock End(string t, bool edited) => new()
            {
                Text = t, FontFamily = NotaFonts.MonoFamily, FontSize = NotaType.KnobValue, VerticalAlignment = VerticalAlignment.Center,
                Foreground = edited ? Brass : NotaPalette.TextFaint,
            };
            var ends = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 2 };
            var ea = End("A", sideIx == 0); var eb = End("B", sideIx == 1);
            Grid.SetColumn(strip, 1); Grid.SetColumn(eb, 2);
            ends.Children.Add(ea); ends.Children.Add(strip); ends.Children.Add(eb);
            col.Children.Add(ends);
            ctx.AddDeviceRefresher(() =>
            {
                int got = engine.InstrumentScope(track, scope);
                if (got < 23) return;
                int cap = 4 + Math.Clamp((int)Math.Round(G("voices") * 8), 0, 8), lo = layout == 0 ? cap : LowerShare(G("share"), cap);
                strip.Set(scope[6 + partIx], partIx == 0 ? scope.AsSpan(8, lo) : scope.AsSpan(8 + lo, cap - lo));
                for (int l = 0; l < 3; l++)
                    if (lfoGraphs[l] is { } lg) { lg.Phase = scope[20 + l]; lg.Refresh(); }
            });
            var editCap = Cap("Edit"); editCap.HorizontalAlignment = HorizontalAlignment.Center; editCap.Margin = new Thickness(0, 8, 0, 0);
            col.Children.Add(editCap);
            var sideSeg = Segments(new[] { "A", "B" }, () => sideIx, iv =>
            {
                if (iv == sideIx) return;
                EditSides[track] = iv;
                ctx.RequestRebuild();
            }, out var sideSync, fill: true, fontSize: NotaType.DeviceSection);
            readouts.Add(sideSync);
            ToolTip.SetTip(sideSeg, "The side the knobs edit");
            col.Children.Add(sideSeg);
            if (!actions) return col;
            // A quiet action: no frame at rest, a hover tone; it rewrites a side in one undo step.
            Control SideAction(string text, string tip, Nota.Application.Twin.TwinSides.Edit edit)
            {
                var tb = new TextBlock { Text = text, FontSize = NotaType.RowLabel, FontWeight = FontWeight.Medium, Foreground = TextSecondary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var b = new Border { Height = 16, CornerRadius = NotaRadius.Clip, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = tb };
                b.PointerEntered += (_, _) => { b.Background = NotaPalette.SurfaceHover; tb.Foreground = TextPrimary; };
                b.PointerExited += (_, _) => { b.Background = Brushes.Transparent; tb.Foreground = TextSecondary; };
                ToolTip.SetTip(b, tip);
                b.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return;
                    Nota.Application.Twin.TwinSides.Apply(engine, track, edit, PartKey);
                    ctx.NotifyChanged();
                    ctx.RequestRebuild();
                    e.Handled = true;
                };
                return b;
            }
            col.Children.Add(new StackPanel
            {
                Spacing = 2, Margin = new Thickness(0, 8, 0, 0),
                Children =
                {
                    SideAction("A → B", "Copy side A onto side B", Nota.Application.Twin.TwinSides.Edit.CopyAToB),
                    SideAction("B → A", "Copy side B onto side A", Nota.Application.Twin.TwinSides.Edit.CopyBToA),
                    SideAction("Swap", "Swap sides A and B", Nota.Application.Twin.TwinSides.Edit.Swap),
                },
            });
            return col;
        }

        // ---- assembly -----------------------------------------------------------
        bool mini = InstrumentView.IsMini(engine, track);
        var morphPanel = Section(MorphColumn(!mini));
        morphPanel.Width = mini ? 64 : MorphW;
        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 0,
            Margin = new Thickness(NotaSpace.DeviceInset),
        };
        body.Children.Add(morphPanel);
        Control main = tabPanel;
        if (mini)
        {
            // S: the same params, fewer of them — Morph and A / B, the pad, four sound knobs.
            var knobs = Row(PKnob(S("ldcut"), "Cutoff", Cutoff, cellW: 42), PKnob(S("ldres"), "Reso", Pct, cellW: 42),
                PKnob(S("aenva"), "Attack", Attack, cellW: 42), PKnob(S("aenvr"), "Release", Decay, cellW: 42));
            var pad = PadView(124, legend: false, width: 172);
            pad.HorizontalAlignment = HorizontalAlignment.Center;
            var stack = new DockPanel { LastChildFill = false, Margin = new Thickness(6, 2, 0, 0) };
            DockPanel.SetDock(pad, Dock.Top); DockPanel.SetDock(knobs, Dock.Bottom);
            stack.Children.Add(pad); stack.Children.Add(knobs);
            main = Section(stack);
        }
        Grid.SetColumn(main, 1); body.Children.Add(main);
        var root = new Border { Background = NotaPalette.SurfaceCard, Child = body };

        ctx.SetInstLiveViz(Refresh);
        showTab();
        return root;
    }
}
