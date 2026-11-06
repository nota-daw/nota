// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the Nota Sampler editor (instrument kind 1), on the 700 × 260 frame
// the almanac draws for it (header 24 · body · status 18):
//
//   Centre      a tab panel — Sample · Pitch · Env · Filter — each a window over a 66 px
//               row of controls, with the tab's reading beside the tabs:
//               Sample — the waveform with Start / End, the loop and its crossfade
//                        (drag them); Loop Off / Fwd / Ping / Rev, Crossfade, Gain, Snap
//                        to zero and Trim silence.
//               Pitch  — the keyboard (click a key for the root, the key that plays the
//                        sample at its own pitch outlined); Transpose, Detune, Keytrack,
//                        the root stepper and Detect from file name.
//               Env    — the amp envelope (drag its nodes), A / D / S / R and Vel → Vol.
//               Filter — the SVF's real response (drag: cutoff × resonance); Type,
//                        Cutoff, Reso, Keytrack and Env → Cutoff with its depth.
//   Rail 186    always there: the voice mode (Poly 16 / Mono / Choke), Volume, Pan,
//               Glide, the Output level, Vel → Vol and the output meter.
//
// Neutral ink is the parameter itself; teal is detune and modulation (keytrack, velocity, the
// envelope's reach) and what the engine is doing now (the voice on the envelope, the
// cutoff it hears). Every control is a plugin param → automation / persist / clone, and
// the card follows automation live. The editor (BuildEditor) also serves a rack chain's
// Sampler (Drum Rack pads / Instrument Rack chains) through ISamplerAccess.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.DeviceCardKit;
using Nota.Application.Localization;

namespace Nota.App;

internal sealed class SamplerInstrumentCard : IInstrumentCard
{
    public bool BodyOnly => true;
    public string Subtitle => "Built-in";
    public double CardWidth => 700;

    private const double RailW = 186, TabH = 20, RowH = 66, StatusH = 18;
    private static readonly string[] TabNames = { "Sample", "Pitch", "Env", "Filter" };
    private static readonly string[] TabHints = { "start · end · loop · crossfade", "root · transpose · detune · keytrack", "a · d · s · r · vel", "type · cutoff · reso · keytrack" };

    public Control Build(DeviceCardContext ctx)
        => BuildEditor(ctx.Engine, new TrackSamplerAccess(ctx.Engine, ctx.TrackId), ctx.AddDeviceRefresher, ctx.SetInstLiveViz);

    // The whole editor, for the track Sampler (TrackSamplerAccess) and a rack chain's
    // Sampler (ChainSamplerAccess). `registerTick` runs at 60 Hz; `setLiveViz` (track
    // only) hands the card's refresh to the automation live-follow.
    internal static Control BuildEditor(IAudioEngine engine, ISamplerAccess acc, Action<Action> registerTick, Action<Action?>? setLiveViz = null)
    {
        int pc = acc.ParamCount();
        var idx = new Dictionary<string, int>();
        for (int i = 0; i < pc; i++) idx[acc.ParamId(i)] = i;
        int I(string id) => idx.TryGetValue(id, out var i) ? i : -1;
        float G(string id) => I(id) is var i and >= 0 ? acc.ParamGet(i) : 0f;
        void SetP(string id, double v) { if (I(id) is var i and >= 0) acc.ParamSet(i, (float)Math.Clamp(v, 0, 1)); }
        void Begin(string id) { if (I(id) >= 0) acc.BeginGesture(id); }
        void End(string id) { if (I(id) >= 0) acc.EndGesture(id); }
        void Write(string id, double v) { Begin(id); SetP(id, v); End(id); }
        float Def(string id) => I(id) is var i and >= 0 ? acc.ParamDefault(i) : 0f;
        int Sel(string id, int n) => SamplerModel.Index(G(id), n);
        int LoopSel() => SamplerModel.LoopIndex(G("loopmode"), G("reverse"));

        var readouts = new List<Action>();
        int tab = 0;
        Action refresh = () => { };
        void Refresh() => refresh();

        // ---- the sample ---------------------------------------------------------------
        bool hasSample = acc.Info(out long sampleId, out int rootv) && sampleId != 0;
        float[] raw = Array.Empty<float>(); int ch = 1; long frames = 0; double sr = 0, durSec = 0;
        if (hasSample && engine.TryGetSampleInfo(sampleId, out var sinfo))
        {
            raw = engine.ReadSample(sampleId); ch = Math.Max(1, sinfo.Channels); frames = sinfo.Frames; sr = sinfo.SampleRate;
            durSec = sr > 0 ? frames / sr : 0;
        }
        hasSample = hasSample && frames > 1;
        string sampleName = hasSample ? engine.SampleName(sampleId) : "";
        var snap = new SamplerModel.Snapshot();
        var scope = new float[SamplerModel.ScopeLength];

        static TextBlock Cap(string t, IBrush? c = null) => new()
        {
            Text = NotaText.Label(t), FontSize = NotaType.KnobLabel, FontWeight = FontWeight.Medium,
            Foreground = c ?? TextTertiary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        static TextBlock Mono(string t, IBrush? c = null, double fs = 7) => new()
        {
            Text = t, FontSize = fs, Foreground = c ?? TextSecondary,
            VerticalAlignment = VerticalAlignment.Center, FontFamily = NotaFonts.UiFamily,
        };

        // ---- shared builders ----------------------------------------------------------
        Control PKnob(string id, string label, Func<float, string> fmt, double size = NotaSize.KnobSecondary, double cellW = 54,
            IBrush? arc = null, bool emphasised = false, string? tip = null)
        {
            int pi = I(id);
            if (pi < 0) return new Panel();
            var value = new TextBlock { Text = fmt(G(id)), Foreground = TextPrimary };
            var knob = new Knob(G(id), 1.0) { Accent = true, ArcColor = arc, Default = acc.ParamDefault(pi), Width = size, Height = size };
            knob.Format = v => fmt((float)v);
            knob.ValueChanged += v => { acc.ParamSet(pi, (float)v); value.Text = fmt((float)v); Refresh(); };
            knob.GestureBegin += () => Begin(id);
            knob.GestureEnd += () => End(id);
            readouts.Add(() =>
            {
                if (knob.Dragging) return;
                float v = G(id);
                if (Math.Abs(v - knob.Value) > 1e-4) knob.Value = v;
                value.Text = fmt(v);
            });
            acc.Learn(knob, pi);
            var cell = KnobCell(label, knob, value, cellW, emphasised, emphasised ? TextPrimary : null);
            cell.VerticalAlignment = VerticalAlignment.Center;
            if (tip is not null) ToolTip.SetTip(cell, tip);
            return cell;
        }

        Border Chips(string[] names, Func<int> cur, Action<int> pick, double padX = 6, double fontSize = 7, string? learnId = null)
        {
            var seg = Segments(names, cur, iv => { pick(iv); Refresh(); }, out var sync, padX: padX, fontSize: fontSize);
            readouts.Add(sync);
            if (learnId is not null && I(learnId) is var li and >= 0) acc.Learn(seg, li);
            return seg;
        }
        Border ParamChips(string id, string[] names, double padX = 6, double fontSize = 7)
            => Chips(names, () => Sel(id, names.Length), iv => Write(id, iv / (double)(names.Length - 1)), padX, fontSize, id);

        Grid Slider(string label, string id, Func<string> text, bool bipolar = false, bool modulation = false, string? tip = null)
        {
            int pi = I(id);
            var row = SliderRow(label, () => G(id), v => { SetP(id, v); Refresh(); }, text, out var sync,
                begin: pi >= 0 ? () => Begin(id) : null,
                end: pi >= 0 ? () => End(id) : null,
                reset: pi >= 0 ? () => { Write(id, Def(id)); Refresh(); } : null,
                bipolar: bipolar, labelWidth: 42, valueWidth: 36, modulation: modulation);
            row.ColumnSpacing = 6;
            readouts.Add(sync);
            if (pi >= 0) acc.Learn(row, pi);
            if (tip is not null) ToolTip.SetTip(row, tip);
            return row;
        }

        // A raised text button; a latching one turns accent-washed while on.
        Border Button(string text, Action click, string tip, Func<bool>? on = null, Func<bool>? enabled = null, string? learnId = null)
        {
            var tb = new TextBlock { Text = text, FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var b = new Border
            {
                Height = 17, CornerRadius = NotaRadius.Cell, BorderThickness = new Thickness(1), Padding = new Thickness(8, 0),
                Cursor = new Cursor(StandardCursorType.Hand), Child = tb, VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(b, tip);
            void Paint()
            {
                bool lit = on?.Invoke() ?? false, ok = enabled?.Invoke() ?? true;
                b.Background = lit ? NotaPalette.AccentSubtle : NotaPalette.SurfaceRaised;
                b.BorderBrush = lit ? NotaPalette.BorderBrass : BorderDef;
                tb.Foreground = !ok ? TextTertiary : lit ? AccentBright : NotaPalette.TextStrong;
                b.Cursor = ok ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
            }
            b.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed || !(enabled?.Invoke() ?? true)) return;
                click(); Paint(); Refresh(); e.Handled = true;
            };
            readouts.Add(Paint);
            Paint();
            if (learnId is not null && I(learnId) is var li and >= 0) acc.Learn(b, li);
            return b;
        }

        static Grid Row(double spacing, params (Control C, string W)[] cols)
        {
            var g = new Grid { ColumnSpacing = spacing, VerticalAlignment = VerticalAlignment.Stretch };
            for (int i = 0; i < cols.Length; i++)
            {
                g.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Parse(cols[i].W)));
                cols[i].C.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(cols[i].C, i); g.Children.Add(cols[i].C);
            }
            return g;
        }
        static StackPanel Labeled(string cap, Control c) => new()
        {
            Spacing = 4, VerticalAlignment = VerticalAlignment.Center,
            Children = { Cap(cap), c },
        };
        // Dims a subtree when `inactive` flips (it stays editable) — only on change.
        void DimWhen(Control c, Func<bool> inactive)
        {
            bool? was = null;
            readouts.Add(() => { bool now = inactive(); if (was != now) { Inactive.Set(c, now, interactive: true); was = now; } });
        }

        // ---- words --------------------------------------------------------------------
        string St(float v) => NotaNum.Unit(SamplerModel.TransposeSt(v), "+0;−0;0", "st");
        string Cents(float v) => NotaNum.Unit(SamplerModel.DetuneCents(v), "+0;−0;0", "c");
        string DbLin(float v) => NotaNum.Db(SamplerModel.VolumeDb(v), floor: -80);
        string PanText(float v) { double p = SamplerModel.PanSigned(v); return Math.Abs(p) < 0.015 ? "C" : (p < 0 ? "L" : "R") + NotaNum.Str(Math.Abs(p) * 100, "0"); }
        string GlideText(float v) { double s = SamplerModel.GlideSec(v); return s <= 0 ? NotaNum.Unit(0, "0", "ms") : NotaNum.Time(s); }
        string Oct(float v) => NotaNum.Unit(SamplerModel.EnvOctaves(v), "+0.0;−0.0;0.0", "oct");
        string LoopWord()
        {
            int l = LoopSel();
            return l switch
            {
                0 => Loc.T("device.sampler.no_loop"),
                _ => $"{SamplerModel.LoopNames[l].ToLowerInvariant()} {NotaNum.Str(G("loopstart") * durSec, "0.00")} – {NotaNum.Unit(G("loopend") * durSec, "0.00", "s")}",
            };
        }

        // ================= Sample tab =================
        var wave = new SamplerWaveView();
        wave.SetEmpty(Loc.T("device.sampler.drop_a_sample_here"));
        if (hasSample) wave.SetPeaks(SamplerModel.Peaks(raw, ch, 1200), durSec);
        ToolTip.SetTip(wave, hasSample ? MenuKit.Keys(Loc.T("device.sampler.drag_start_end_and_the")) : Loc.T("device.sampler.drag_a_sample_from_the"));
        wave.DragBegin += hnd => Begin(SamplerWaveView.HandleId(hnd));
        wave.DragEnd += hnd => End(SamplerWaveView.HandleId(hnd));
        wave.Moved += (hnd, f) => { SetP(SamplerWaveView.HandleId(hnd), f); Refresh(); };
        wave.ResetHandle += hnd => { var id = SamplerWaveView.HandleId(hnd); Write(id, Def(id)); Refresh(); };
        // ⌥-click a marker to type where it sits, in seconds into the file (specs/035).
        // Held to the same limits a drag keeps: Start before End, the loop inside them.
        ValueField WF(int h)
        {
            string id = SamplerWaveView.HandleId(h);
            double Limit(double f) => h switch
            {
                0 => Math.Min(f, G("end") - 0.001),
                1 => Math.Max(f, G("start") + 0.001),
                2 => Math.Clamp(Math.Min(f, G("loopend") - 0.001), G("start"), G("end")),
                _ => Math.Clamp(Math.Max(f, G("loopstart") + 0.001), G("start"), G("end")),
            };
            return PF(id, new[] { "Start", "End", "Loop start", "Loop end" }[h], v => NotaNum.Unit(v * durSec, "0.000", "s"))
                with { Set = v => SetP(id, Limit(Math.Clamp(v, 0, 1))) };
        }
        ValueEntry.AttachHandles(wave, [WF(0)], [WF(1)], [WF(2)], [WF(3)]);
        readouts.Add(() => wave.Set(G("start"), G("end"), G("loopstart"), G("loopend"), LoopSel(), SamplerModel.CrossfadeSec(G("loopxfade")),
            hasSample ? $"{NotaNum.Str(sr / 1000, "0.#")} k · {NotaNum.Unit(durSec, "0.00", "s")} · root {SamplerModel.NoteName(rootv)}" : "",
            hasSample ? LoopWord() : ""));

        var loopSeg = Chips(SamplerModel.LoopNames, LoopSel, iv =>
        {
            var (lm, rev) = SamplerModel.LoopValues(iv);
            Write("loopmode", lm); Write("reverse", rev);
        }, padX: 7, learnId: "loopmode");
        ToolTip.SetTip(loopSeg, Loc.T("device.sampler.off_plays_once_fwd_loops"));
        var xfadeKnob = PKnob("loopxfade", "CROSSFADE", v => NotaNum.Time(SamplerModel.CrossfadeSec(v)), tip: Loc.T("device.sampler.blends_the_loops_end_into"));
        DimWhen(xfadeKnob, () => LoopSel() != 1);
        var gainKnob = PKnob("gain", "GAIN", v => NotaNum.Db(SamplerModel.GainDb(v), signed: true), tip: Loc.T("device.sampler.the_samples_level_before_the"));
        void SnapAll()
        {
            foreach (var id in new[] { "start", "end", "loopstart", "loopend" }) Write(id, SamplerModel.SnapZero(raw, ch, frames, G(id)));
        }
        void Trim()
        {
            if (SamplerModel.TrimSilence(raw, ch, frames) is not { } t) return;
            Write("start", t.Start); Write("end", t.End);
            if (G("loopstart") < t.Start) Write("loopstart", t.Start);
            if (G("loopend") > t.End) Write("loopend", t.End);
        }
        var snapBtn = Button(Loc.T("device.sampler.snap_to_zero"), SnapAll, Loc.T("device.sampler.move_start_end_and_the"), enabled: () => hasSample);
        var trimBtn = Button(Loc.T("device.sampler.trim_silence"), Trim, Loc.T("device.sampler.move_start_and_end_to"), enabled: () => hasSample);
        snapBtn.HorizontalAlignment = HorizontalAlignment.Stretch; trimBtn.HorizontalAlignment = HorizontalAlignment.Stretch;
        var sampleBtns = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Children = { snapBtn, trimBtn } };
        var sampleRow = Row(14, (Labeled("LOOP", loopSeg), "Auto"), (xfadeKnob, "Auto"), (gainKnob, "Auto"), (new Panel(), "*"), (sampleBtns, "Auto"));

        // ================= Pitch tab =================
        var keys = new SamplerKeysView();
        ToolTip.SetTip(keys, Loc.T("device.sampler.click_a_key_to_make"));
        keys.RootPicked += k => { rootv = k; acc.SetRoot(k); Refresh(); };
        int NaturalKey()
        {
            // The key that plays the sample at its own pitch: root − (transpose + detune) / keytrack.
            double tr = G("pitchtrack");
            if (tr < 0.01) return -1;
            double off = SamplerModel.TransposeSt(G("transpose")) + SamplerModel.DetuneCents(G("detune")) / 100.0;
            return (int)Math.Round(rootv - off / tr);
        }
        readouts.Add(() => keys.Set(rootv, NaturalKey(), snap.Live ? snap.Note : -1, G("pitchtrack"), Loc.T("device.sampler.the_key_sets_the_playback")));
        var rootText = Mono(SamplerModel.NoteName(rootv), TextPrimary, 9);
        rootText.HorizontalAlignment = HorizontalAlignment.Center;
        readouts.Add(() => rootText.Text = SamplerModel.NoteName(rootv));
        Border StepBtn(GlyphKind g, int d, string tip)
        {
            var b = new Border
            {
                Width = 16, Height = 17, CornerRadius = NotaRadius.Cell, BorderThickness = new Thickness(1),
                BorderBrush = BorderDef, Background = NotaPalette.SurfaceRaised, Cursor = new Cursor(StandardCursorType.Hand),
                Child = new Glyph(g, 7) { Foreground = NotaPalette.TextStrong, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            ToolTip.SetTip(b, tip);
            b.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return;
                rootv = Math.Clamp(rootv + d, 0, 127); acc.SetRoot(rootv); Refresh(); e.Handled = true;
            };
            return b;
        }
        var rootBox = new Border
        {
            Height = 17, MinWidth = 40, CornerRadius = NotaRadius.Cell, BorderThickness = new Thickness(1), BorderBrush = BorderDef,
            Background = NotaPalette.BgSunken, Child = rootText,
        };
        rootBox.PointerWheelChanged += (wt, e) =>
        {
            if (WheelInput.Step(e, wt) is not { } s) return;
            e.Handled = true; if (s == 0) return;
            rootv = Math.Clamp(rootv + s, 0, 127); acc.SetRoot(rootv); Refresh();
        };
        ToolTip.SetTip(rootBox, Loc.T("device.sampler.the_root_note_scroll_to"));
        var rootStepper = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { StepBtn(GlyphKind.Minus, -1, Loc.T("device.sampler.root_a_semitone_down")), rootBox, StepBtn(GlyphKind.Plus, +1, Loc.T("device.sampler.root_a_semitone_up")) } };
        int detected = SamplerModel.DetectRoot(sampleName);
        var detectBtn = Button(Loc.T("device.sampler.detect_from_file_name"), () => { if (detected >= 0) { rootv = detected; acc.SetRoot(detected); } },
            detected >= 0 ? Loc.F("device.sampler.names_make_it_the_root", sampleName, SamplerModel.NoteName(detected))
                : sampleName.Length > 0 ? $"\"{sampleName}\" names no note (e.g. C4, F#3, Eb2)" : Loc.T("device.sampler.the_samples_file_name_isnt"),
            on: () => detected >= 0 && detected == rootv, enabled: () => detected >= 0);
        var pitchRow = Row(16,
            (PKnob("transpose", "TRANSPOSE", St, tip: Loc.T("device.sampler.shift_every_note_24_semitones")), "Auto"),
            (PKnob("detune", "DETUNE", Cents, arc: Teal, tip: Loc.T("device.sampler.fine_tune_50_cents")), "Auto"),
            (PKnob("pitchtrack", "KEYTRACK", v => NotaNum.Pct(v), tip: Loc.T("device.sampler.how_far_the_key_moves")), "Auto"),
            (Labeled("ROOT", rootStepper), "Auto"),
            (new Panel(), "*"),
            (detectBtn, "Auto"));

        // ================= Env tab =================
        // The shared envelope graph (#531): drag the breakpoints, bend decay and release by their
        // diamonds, ⌥-click to type; the dot is the loudest voice.
        var env = new VoltEnv(new SamplerEnvParams(acc)) { VerticalAlignment = VerticalAlignment.Stretch, MinHeight = 50 };
        ToolTip.SetTip(env, MenuKit.Keys(Loc.T("device.sampler.drag_the_nodes_attack_decay")));
        (int, string) EP(string id) => (I(id), id);
        env.Target("ENV", EP("attack"), EP("decay"), EP("sustain"), EP("release"));
        env.Shape(EP("dcurve"), EP("rcurve"), EP("envexp"));
        env.Format = (k, n) => k switch
        {
            0 => NotaNum.Time(SamplerModel.AttackSec((float)n)), 1 => NotaNum.Time(SamplerModel.DecaySec((float)n)),
            2 => DbLin((float)n), _ => NotaNum.Time(SamplerModel.ReleaseSec((float)n)),
        };
        env.Seconds = (k, n) => k switch { 0 => SamplerModel.AttackSec((float)n), 1 => SamplerModel.DecaySec((float)n), _ => SamplerModel.ReleaseSec((float)n) };
        readouts.Add(() => { env.SetPlay(snap.Live ? snap.EnvStage : 4, snap.EnvProgress); env.Refresh(); });
        float lastVel = G("velamount") > 0.005f ? G("velamount") : 1f;
        var velSwitch = Switch("", () => G("velamount") > 0.005f, () =>
        {
            if (G("velamount") > 0.005f) { lastVel = G("velamount"); Write("velamount", 0); }
            else Write("velamount", lastVel);
            Refresh();
        }, out var velSync);
        readouts.Add(velSync);
        if (I("velamount") is var vli and >= 0) acc.Learn(velSwitch, vli);
        var velWord = Mono("", TextSecondary, 8);
        readouts.Add(() => velWord.Text = G("velamount") > 0.005f ? NotaNum.Pct(G("velamount")) : "off");
        var velBlock = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Cap("VEL → VOL"), velSwitch, velWord } };
        ToolTip.SetTip(velBlock, Loc.T("device.sampler.velocity_sets_the_level_the"));
        var curveSeg = ParamChips("envexp", new[] { Loc.T("device.env.lin"), Loc.T("device.env.exp") });
        var envRow = Row(16,
            (PKnob("attack", "ATTACK", v => NotaNum.Time(SamplerModel.AttackSec(v))), "Auto"),
            (PKnob("decay", "DECAY", v => NotaNum.Time(SamplerModel.DecaySec(v))), "Auto"),
            (PKnob("sustain", "SUSTAIN", DbLin, NotaSize.KnobMain, 58), "Auto"),
            (PKnob("release", "RELEASE", v => NotaNum.Time(SamplerModel.ReleaseSec(v))), "Auto"),
            (new Panel(), "*"),
            (Labeled("CURVE", curveSeg), "Auto"),
            (velBlock, "Auto"));

        // ================= Filter tab =================
        var filt = new SamplerFilterView();
        ToolTip.SetTip(filt, MenuKit.Keys(Loc.T("device.sampler.drag_left_right_for_the")));
        filt.DragBegin += () => { Begin("cutoff"); Begin("resonance"); };
        filt.DragEnd += () => { End("cutoff"); End("resonance"); };
        filt.Changed += (id, v) => { SetP(id, v); Refresh(); };
        filt.Reset += () => { Write("cutoff", Def("cutoff")); Write("resonance", Def("resonance")); Refresh(); };
        // ⌥-click a node / the filter window to type it (specs/035).
        ValueField PF(string id, string name, Func<float, string> fmt) => new(name, () => G(id), v => fmt((float)v), v => SetP(id, v),
            () => Begin(id), () => End(id), I(id) >= 0 ? Def(id) : null) { After = Refresh };
        ValueEntry.AttachAll(filt, PF("cutoff", "Cutoff", v => NotaNum.Hz(SamplerModel.CutoffHz(v))), PF("resonance", "Resonance", v => NotaNum.Pct(v)));
        string FiltTl()
        {
            int t = Sel("filtertype", 4);
            if (t == 0) return Loc.T("device.sampler.off_the_sample_passes_untouched");
            return $"{SamplerModel.FilterNames[t]} 12\u2009dB/oct · {NotaNum.Hz(SamplerModel.CutoffHz(G("cutoff")))} · reso {NotaNum.Pct(G("resonance"))}";
        }
        readouts.Add(() => filt.Set(Sel("filtertype", 4), G("cutoff"), G("resonance"), snap.Live ? snap.CutoffHz : -1, FiltTl()));
        var typeSeg = ParamChips("filtertype", SamplerModel.FilterNames, padX: 7);
        ToolTip.SetTip(typeSeg, Loc.T("device.sampler.off_low_pass_high_pass"));
        var cutKnob = PKnob("cutoff", "CUTOFF", v => NotaNum.Hz(SamplerModel.CutoffHz(v)), NotaSize.KnobMain, 58, emphasised: true);
        var resoKnob = PKnob("resonance", "RESO", v => NotaNum.Pct(v));
        var ktKnob = PKnob("keytrack", "KEYTRACK", v => NotaNum.Pct(v), arc: Teal, tip: Loc.T("device.sampler.the_cutoff_follows_the_key"));
        var envBtn = Button("Env → Cutoff", () => Write("envcutoff", G("envcutoff") >= 0.5f ? 0 : 1),
            Loc.T("device.sampler.let_the_amp_envelope_open"), on: () => G("envcutoff") >= 0.5f, learnId: "envcutoff");
        var envAmt = PKnob("envamount", "ENV", Oct, arc: Teal, tip: Loc.T("device.sampler.how_far_the_envelope_moves"));
        var filterBody = new[] { cutKnob, resoKnob, ktKnob };
        foreach (var c in filterBody) DimWhen(c, () => Sel("filtertype", 4) == 0);
        DimWhen(envAmt, () => Sel("filtertype", 4) == 0 || G("envcutoff") < 0.5f);
        var filterRow = Row(14, (Labeled("TYPE", typeSeg), "Auto"), (cutKnob, "Auto"), (resoKnob, "Auto"), (ktKnob, "Auto"),
            (new Panel(), "*"), (envBtn, "Auto"), (envAmt, "Auto"));

        // ---- the tab panel ------------------------------------------------------------
        var views = new Control[] { wave, keys, env, filt };
        var rows = new Control[] { sampleRow, pitchRow, envRow, filterRow };
        var pages = new Control[views.Length];
        for (int i = 0; i < views.Length; i++)
        {
            var rowHost = new Border
            {
                Height = RowH, BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 3, 0, 0), Child = rows[i],
            };
            var page = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 5, Margin = new Thickness(7, 5, 7, 0) };
            page.Children.Add(views[i]);
            Grid.SetRow(rowHost, 1); page.Children.Add(rowHost);
            pages[i] = page;
        }
        var tabCells = new Border[TabNames.Length];
        var tabTexts = new TextBlock[TabNames.Length];
        var tabRow = new StackPanel { Orientation = Orientation.Horizontal };
        Action showTab = () => { };
        for (int i = 0; i < TabNames.Length; i++)
        {
            int iv = i;
            var tb = new TextBlock { Text = TabNames[i], FontSize = NotaType.DeviceSection, VerticalAlignment = VerticalAlignment.Center };
            var cell = new Border { Padding = new Thickness(7, 0), BorderThickness = new Thickness(0, 0, 0, 2), Cursor = new Cursor(StandardCursorType.Hand), Child = tb };
            cell.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed) return;
                tab = iv; showTab(); e.Handled = true;
            };
            tabCells[i] = cell; tabTexts[i] = tb; tabRow.Children.Add(cell);
        }
        var hint = Mono("", TextTertiary);
        hint.HorizontalAlignment = HorizontalAlignment.Right; hint.Margin = new Thickness(0, 0, 8, 0);
        var tabStrip = new Grid { Height = TabH, ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        tabStrip.Children.Add(tabRow);
        Grid.SetColumn(hint, 1); tabStrip.Children.Add(hint);
        var tabHost = new Panel();
        foreach (var p in pages) tabHost.Children.Add(p);
        var tabGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        tabGrid.Children.Add(HeaderStrip(tabStrip));
        Grid.SetRow(tabHost, 1); tabGrid.Children.Add(tabHost);
        var tabPanel = SectionBox(tabGrid);

        // ---- the rail · voices and output -----------------------------------------------
        var voiceSeg = ParamChips("voicemode", SamplerModel.VoiceNames, padX: 5);
        ToolTip.SetTip(voiceSeg, Loc.T("device.sampler.poly_16_voices_mono_one"));
        var railHead = new Grid { Height = TabH, ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(8, 0) };
        var railCap = Cap("VOICES");
        railHead.Children.Add(railCap);
        voiceSeg.HorizontalAlignment = HorizontalAlignment.Right; voiceSeg.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(voiceSeg, 1); railHead.Children.Add(voiceSeg);

        var sliders = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                Slider("VOLUME", "volume", () => DbLin(G("volume")), tip: Loc.T("device.sampler.the_voices_level")),
                Slider("PAN", "pan", () => PanText(G("pan")), bipolar: true),
                Slider("GLIDE", "glide", () => GlideText(G("glide")), tip: Loc.T("device.sampler.slide_from_the_last_note")),
            },
        };
        var outKnob = PKnob("output", "OUTPUT", v => NotaNum.Pct(v), NotaSize.KnobMain, 50, tip: Loc.T("device.sampler.the_level_after_everything"));
        var velKnob = PKnob("velamount", "VEL → VOL", v => NotaNum.Pct(v), cellW: 50, arc: Teal, tip: Loc.T("device.sampler.how_much_velocity_sets_the"));

        Control meterBlock;
        if (acc.TryMeter(out _))
        {
            var meter = new MeterBar { VerticalAlignment = VerticalAlignment.Stretch, Width = MeterScale.StereoWidth };
            registerTick(() => { if (acc.TryMeter(out var m)) meter.Push(m); });
            var meterScale = new Grid { VerticalAlignment = VerticalAlignment.Stretch, RowDefinitions = new RowDefinitions(string.Join(",", ScaleRows(0, -12, -48))) };
            for (int i = 0; i < 3; i++)
            {
                var c = Mono(i == 0 ? "0" : i == 1 ? "−12" : "−48", NotaPalette.TextAxis, NotaType.Axis);
                Grid.SetRow(c, i * 2 + 1); meterScale.Children.Add(c);
            }
            var mg = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), ColumnSpacing = 4, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 0, 2) };
            mg.Children.Add(meter);
            Grid.SetColumn(meterScale, 1); mg.Children.Add(meterScale);
            meterBlock = mg;
        }
        else meterBlock = new Panel();
        var outRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 4 };
        outRow.Children.Add(outKnob);
        Grid.SetColumn(velKnob, 1); outRow.Children.Add(velKnob);
        Grid.SetColumn(meterBlock, 2); outRow.Children.Add(meterBlock);
        var outHost = new Border { BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 5, 0, 0), Child = outRow };

        var railBody = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 6, Margin = new Thickness(8, 6, 8, 5) };
        railBody.Children.Add(sliders);
        Grid.SetRow(outHost, 1); railBody.Children.Add(outHost);
        var railGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        railGrid.Children.Add(HeaderStrip(railHead));
        Grid.SetRow(railBody, 1); railGrid.Children.Add(railBody);
        var rail = SectionBox(railGrid);
        rail.Width = RailW;

        // ---- assembly -----------------------------------------------------------------
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = NotaSpace.DeviceGap, Margin = new Thickness(NotaSpace.DeviceGap) };
        body.Children.Add(tabPanel);
        Grid.SetColumn(rail, 1); body.Children.Add(rail);
        var root = new Border { Background = NotaPalette.SurfaceCard, Child = body };

        showTab = () =>
        {
            for (int i = 0; i < TabNames.Length; i++)
            {
                bool on = i == tab;
                tabCells[i].Background = on ? NotaPalette.SurfaceRaised : Brushes.Transparent;
                tabCells[i].BorderBrush = on ? Brass : Brushes.Transparent;
                tabTexts[i].Foreground = on ? TextPrimary : TextTertiary;
                tabTexts[i].FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                pages[i].IsVisible = on;
            }
            hint.Text = TabHints[tab];
            Refresh();
        };
        refresh = () =>
        {
            foreach (var r in readouts) r();
        };
        void Live()
        {
            int n = acc.Scope(scope);
            SamplerModel.Parse(scope.AsSpan(0, Math.Max(0, n)), snap);
            if (acc.Info(out long sid, out int r) && sid == sampleId) rootv = r;   // the root may move from MCP / undo
            wave.SetPlayhead(snap.Live ? snap.PlayPos : acc.PlayPosition());
            Refresh();
        }
        registerTick(Live);
        setLiveViz?.Invoke(Refresh);
        showTab();
        Live();
        return root;
    }

    // A section: card ground, hairline, radius 6 — the almanac's section box.
    private static Decorator SectionBox(Control child) => DeviceCardKit.Section(child);

    // A section's own header strip: a hairline under it, nothing else.
    private static Border HeaderStrip(Control child) => new()
    {
        BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Child = child,
    };

    private static IEnumerable<string> ScaleRows(params double[] dbs)
    {
        double prev = 0;
        foreach (var db in dbs)
        {
            double fromTop = 1.0 - MeterScale.NormDb(db);
            yield return Math.Max(0, fromTop - prev).ToString("0.000", NotaNum.Culture) + "*";
            yield return "Auto";
            prev = fromTop;
        }
        yield return Math.Max(0, 1.0 - prev).ToString("0.000", NotaNum.Culture) + "*";
    }
}
