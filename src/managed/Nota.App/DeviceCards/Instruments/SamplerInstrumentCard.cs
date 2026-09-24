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
// Brass is the parameter itself; teal is detune and modulation (keytrack, velocity, the
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

namespace Nota.App;

internal sealed class SamplerInstrumentCard : IInstrumentCard
{
    public bool BodyOnly => true;
    public string Subtitle => "BUILT-IN";
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
            Text = t, FontSize = NotaType.KnobLabel, FontWeight = FontWeight.Bold,
            LetterSpacing = NotaType.KnobLabelTracking, Foreground = c ?? TextTertiary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        static TextBlock Mono(string t, IBrush? c = null, double fs = 7) => new()
        {
            Text = t, FontSize = fs, Foreground = c ?? TextSecondary,
            VerticalAlignment = VerticalAlignment.Center, FontFamily = NotaFonts.MonoFamily,
        };

        // ---- shared builders ----------------------------------------------------------
        Control PKnob(string id, string label, Func<float, string> fmt, double size = NotaSize.KnobSecondary, double cellW = 54,
            IBrush? arc = null, bool emphasised = false, string? tip = null)
        {
            int pi = I(id);
            if (pi < 0) return new Panel();
            var value = new TextBlock { Text = fmt(G(id)), Foreground = TextPrimary };
            var knob = new Knob(G(id), 1.0) { Accent = true, ArcColor = arc, Default = acc.ParamDefault(pi), Width = size, Height = size };
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
            var cell = KnobCell(label, knob, value, cellW, emphasised, emphasised ? AccentBright : null);
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

        // A raised text button; a latching one turns brass-washed while on.
        Border Button(string text, Action click, string tip, Func<bool>? on = null, Func<bool>? enabled = null, string? learnId = null)
        {
            var tb = new TextBlock { Text = text, FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var b = new Border
            {
                Height = 17, CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), Padding = new Thickness(8, 0),
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
        string WindowText() => $"{NotaNum.Str(G("start") * durSec, "0.00")} → {NotaNum.Unit(G("end") * durSec, "0.00", "s")}";
        string LoopWord()
        {
            int l = LoopSel();
            return l switch
            {
                0 => "no loop",
                _ => $"{SamplerModel.LoopNames[l].ToLowerInvariant()} {NotaNum.Str(G("loopstart") * durSec, "0.00")} – {NotaNum.Unit(G("loopend") * durSec, "0.00", "s")}",
            };
        }

        // ================= Sample tab =================
        var wave = new SamplerWaveView();
        wave.SetEmpty("Drop a sample here");
        if (hasSample) wave.SetPeaks(SamplerModel.Peaks(raw, ch, 1200), durSec);
        ToolTip.SetTip(wave, hasSample ? "Drag Start / End (and the loop markers when looping) · double-click a marker to reset it" : "Drag a sample from the Files tab or the browser");
        wave.DragBegin += hnd => Begin(SamplerWaveView.HandleId(hnd));
        wave.DragEnd += hnd => End(SamplerWaveView.HandleId(hnd));
        wave.Moved += (hnd, f) => { SetP(SamplerWaveView.HandleId(hnd), f); Refresh(); };
        wave.ResetHandle += hnd => { var id = SamplerWaveView.HandleId(hnd); Write(id, Def(id)); Refresh(); };
        readouts.Add(() => wave.Set(G("start"), G("end"), G("loopstart"), G("loopend"), LoopSel(), SamplerModel.CrossfadeSec(G("loopxfade")),
            hasSample ? $"{NotaNum.Str(sr / 1000, "0.#")} k · {NotaNum.Unit(durSec, "0.00", "s")} · root {SamplerModel.NoteName(rootv)}" : "",
            hasSample ? LoopWord() : ""));

        var loopSeg = Chips(SamplerModel.LoopNames, LoopSel, iv =>
        {
            var (lm, rev) = SamplerModel.LoopValues(iv);
            Write("loopmode", lm); Write("reverse", rev);
        }, padX: 7, learnId: "loopmode");
        ToolTip.SetTip(loopSeg, "Off plays once · Fwd loops · Ping bounces · Rev loops backwards");
        var xfadeKnob = PKnob("loopxfade", "CROSSFADE", v => NotaNum.Time(SamplerModel.CrossfadeSec(v)), tip: "Blends the loop's end into its start (forward loops)");
        DimWhen(xfadeKnob, () => LoopSel() != 1);
        var gainKnob = PKnob("gain", "GAIN", v => NotaNum.Db(SamplerModel.GainDb(v), signed: true), tip: "The sample's level before the envelope");
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
        var snapBtn = Button("Snap to zero", SnapAll, "Move Start, End and the loop to the nearest zero crossing — no clicks", enabled: () => hasSample);
        var trimBtn = Button("Trim silence", Trim, "Move Start and End to where the sound begins and fades out (−48\u2009dB)", enabled: () => hasSample);
        snapBtn.HorizontalAlignment = HorizontalAlignment.Stretch; trimBtn.HorizontalAlignment = HorizontalAlignment.Stretch;
        var sampleBtns = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Children = { snapBtn, trimBtn } };
        var sampleRow = Row(14, (Labeled("LOOP", loopSeg), "Auto"), (xfadeKnob, "Auto"), (gainKnob, "Auto"), (new Panel(), "*"), (sampleBtns, "Auto"));

        // ================= Pitch tab =================
        var keys = new SamplerKeysView();
        ToolTip.SetTip(keys, "Click a key to make it the root — the key that plays the sample as recorded");
        keys.RootPicked += k => { rootv = k; acc.SetRoot(k); Refresh(); };
        int NaturalKey()
        {
            // The key that plays the sample at its own pitch: root − (transpose + detune) / keytrack.
            double tr = G("pitchtrack");
            if (tr < 0.01) return -1;
            double off = SamplerModel.TransposeSt(G("transpose")) + SamplerModel.DetuneCents(G("detune")) / 100.0;
            return (int)Math.Round(rootv - off / tr);
        }
        readouts.Add(() => keys.Set(rootv, NaturalKey(), snap.Live ? snap.Note : -1, G("pitchtrack"), "the key sets the playback speed"));
        var rootText = Mono(SamplerModel.NoteName(rootv), TextPrimary, 9);
        rootText.HorizontalAlignment = HorizontalAlignment.Center;
        readouts.Add(() => rootText.Text = SamplerModel.NoteName(rootv));
        Border StepBtn(GlyphKind g, int d, string tip)
        {
            var b = new Border
            {
                Width = 16, Height = 17, CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1),
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
            Height = 17, MinWidth = 40, CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), BorderBrush = BorderDef,
            Background = NotaPalette.BgSunken, Child = rootText,
        };
        rootBox.PointerWheelChanged += (_, e) => { rootv = Math.Clamp(rootv + (e.Delta.Y > 0 ? 1 : -1), 0, 127); acc.SetRoot(rootv); Refresh(); e.Handled = true; };
        ToolTip.SetTip(rootBox, "The root note — scroll to step it");
        var rootStepper = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { StepBtn(GlyphKind.Minus, -1, "Root a semitone down"), rootBox, StepBtn(GlyphKind.Plus, +1, "Root a semitone up") } };
        int detected = SamplerModel.DetectRoot(sampleName);
        var detectBtn = Button("Detect from file name", () => { if (detected >= 0) { rootv = detected; acc.SetRoot(detected); } },
            detected >= 0 ? $"\"{sampleName}\" names {SamplerModel.NoteName(detected)} — make it the root"
                : sampleName.Length > 0 ? $"\"{sampleName}\" names no note (e.g. C4, F#3, Eb2)" : "The sample's file name isn't known",
            on: () => detected >= 0 && detected == rootv, enabled: () => detected >= 0);
        var pitchRow = Row(16,
            (PKnob("transpose", "TRANSPOSE", St, tip: "Shift every note, ±24 semitones"), "Auto"),
            (PKnob("detune", "DETUNE", Cents, arc: Teal, tip: "Fine tune, ±50 cents"), "Auto"),
            (PKnob("pitchtrack", "KEYTRACK", v => NotaNum.Pct(v), tip: "How far the key moves the pitch — 100\u2009% chromatic, 0\u2009% every key plays the root"), "Auto"),
            (Labeled("ROOT", rootStepper), "Auto"),
            (new Panel(), "*"),
            (detectBtn, "Auto"));

        // ================= Env tab =================
        var env = new SamplerEnvView();
        ToolTip.SetTip(env, "Drag the nodes: attack, decay and sustain (up / down), release · Shift for fine steps · double-click resets");
        env.DragBegin += Begin;
        env.DragEnd += End;
        env.Changed += (id, v) => { SetP(id, v); Refresh(); };
        env.Reset += id => { Write(id, Def(id)); Refresh(); };
        string EnvTl() => $"{NotaNum.Time(SamplerModel.AttackSec(G("attack")))} · {NotaNum.Time(SamplerModel.DecaySec(G("decay")))} · {DbLin(G("sustain"))} · {NotaNum.Time(SamplerModel.ReleaseSec(G("release")))}";
        readouts.Add(() => env.Set(G("attack"), G("decay"), G("sustain"), G("release"), snap, EnvTl(),
            hasSample ? NotaNum.Unit((G("end") - G("start")) * durSec, "0.00", "s") : ""));
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
        ToolTip.SetTip(velBlock, "Velocity sets the level — the amount is the rail's VEL → VOL");
        var envRow = Row(16,
            (PKnob("attack", "ATTACK", v => NotaNum.Time(SamplerModel.AttackSec(v))), "Auto"),
            (PKnob("decay", "DECAY", v => NotaNum.Time(SamplerModel.DecaySec(v))), "Auto"),
            (PKnob("sustain", "SUSTAIN", DbLin, NotaSize.KnobMain, 58), "Auto"),
            (PKnob("release", "RELEASE", v => NotaNum.Time(SamplerModel.ReleaseSec(v))), "Auto"),
            (new Panel(), "*"),
            (velBlock, "Auto"));

        // ================= Filter tab =================
        var filt = new SamplerFilterView();
        ToolTip.SetTip(filt, "Drag: left / right for the cutoff, up / down for the resonance · Shift for fine steps · double-click resets");
        filt.DragBegin += () => { Begin("cutoff"); Begin("resonance"); };
        filt.DragEnd += () => { End("cutoff"); End("resonance"); };
        filt.Changed += (id, v) => { SetP(id, v); Refresh(); };
        filt.Reset += () => { Write("cutoff", Def("cutoff")); Write("resonance", Def("resonance")); Refresh(); };
        string FiltTl()
        {
            int t = Sel("filtertype", 4);
            if (t == 0) return "off · the sample passes untouched";
            return $"{SamplerModel.FilterNames[t]} 12\u2009dB/oct · {NotaNum.Hz(SamplerModel.CutoffHz(G("cutoff")))} · reso {NotaNum.Pct(G("resonance"))}";
        }
        readouts.Add(() => filt.Set(Sel("filtertype", 4), G("cutoff"), G("resonance"), snap.Live ? snap.CutoffHz : -1, FiltTl()));
        var typeSeg = ParamChips("filtertype", SamplerModel.FilterNames, padX: 7);
        ToolTip.SetTip(typeSeg, "Off · low-pass · high-pass · band-pass (12\u2009dB/oct)");
        var cutKnob = PKnob("cutoff", "CUTOFF", v => NotaNum.Hz(SamplerModel.CutoffHz(v)), NotaSize.KnobMain, 58, emphasised: true);
        var resoKnob = PKnob("resonance", "RESO", v => NotaNum.Pct(v));
        var ktKnob = PKnob("keytrack", "KEYTRACK", v => NotaNum.Pct(v), arc: Teal, tip: "The cutoff follows the key, 0 … 100\u2009%");
        var envBtn = Button("Env → Cutoff", () => Write("envcutoff", G("envcutoff") >= 0.5f ? 0 : 1),
            "Let the amp envelope open (or close) the filter — the depth is ENV", on: () => G("envcutoff") >= 0.5f, learnId: "envcutoff");
        var envAmt = PKnob("envamount", "ENV", Oct, arc: Teal, tip: "How far the envelope moves the cutoff, ±6 octaves");
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
        ToolTip.SetTip(voiceSeg, "Poly: 16 voices · Mono: one at a time (legato with Glide) · Choke: a new note cuts the ringing ones");
        var railHead = new Grid { Height = TabH, ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(8, 0) };
        var railCap = Cap("VOICES"); railCap.LetterSpacing = NotaType.SectionLabelTracking;
        railHead.Children.Add(railCap);
        voiceSeg.HorizontalAlignment = HorizontalAlignment.Right; voiceSeg.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(voiceSeg, 1); railHead.Children.Add(voiceSeg);

        var sliders = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                Slider("VOLUME", "volume", () => DbLin(G("volume")), tip: "The voices' level"),
                Slider("PAN", "pan", () => PanText(G("pan")), bipolar: true),
                Slider("GLIDE", "glide", () => GlideText(G("glide")), tip: "Slide from the last note — in Mono, legato: no retrigger"),
            },
        };
        var outKnob = PKnob("output", "OUTPUT", v => NotaNum.Pct(v), NotaSize.KnobMain, 50, tip: "The level after everything");
        var velKnob = PKnob("velamount", "VEL → VOL", v => NotaNum.Pct(v), cellW: 50, arc: Teal, tip: "How much velocity sets the level");

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

        // ---- the status line ----------------------------------------------------------
        var status = new TextBlock { FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var meta = Mono("", TextSecondary, 8);
        string EnvWord()
        {
            double a = SamplerModel.AttackSec(G("attack")), s = G("sustain"), rel = SamplerModel.ReleaseSec(G("release"));
            if (s < 0.05) return "a pluck — it decays to silence while held";
            if (a < 0.01 && s > 0.89 && rel < 0.05) return "almost a gate — the sample is heard whole";
            if (a > 0.3) return "a slow swell";
            if (rel > 1.0) return "a long tail after release";
            return "held at the sustain level";
        }
        string Summary()
        {
            if (!hasSample) return "No sample · drop one from the Files tab or the browser";
            switch (tab)
            {
                case 0:
                {
                    string name = sampleName.Length > 0 ? sampleName + " · " : "";
                    int l = LoopSel();
                    string loop = l == 0 ? "loop off, crossfade not applied"
                        : l == 1 ? $"{LoopWord()} · crossfade {NotaNum.Time(SamplerModel.CrossfadeSec(G("loopxfade")))}"
                        : $"{LoopWord()} · no crossfade";
                    string gain = Math.Abs(SamplerModel.GainDb(G("gain"))) > 0.05 ? $" · gain {NotaNum.Db(SamplerModel.GainDb(G("gain")), signed: true)}" : "";
                    return $"{name}{WindowText()} · {loop}{gain}";
                }
                case 1:
                {
                    int nat = NaturalKey();
                    string tail = nat == rootv ? "the sample plays at its own pitch on the root"
                        : nat < 0 ? "every key plays the same pitch"
                        : $"{SamplerModel.NoteName(nat)} plays the sample at its own pitch";
                    return $"Root {SamplerModel.NoteName(rootv)} · transpose {St(G("transpose"))} · detune {Cents(G("detune"))} · keytrack {NotaNum.Pct(G("pitchtrack"))} — {tail}";
                }
                case 2:
                    return $"Env {EnvTl()} — {EnvWord()}"
                        + (G("velamount") > 0.005f ? $" · velocity {NotaNum.Pct(G("velamount"))}" : " · velocity off");
                default:
                {
                    int t = Sel("filtertype", 4);
                    if (t == 0) return "Filter off — the sample passes untouched";
                    string kt = G("keytrack") > 0.005f ? $"keytrack {NotaNum.Pct(G("keytrack"))}" : "keytrack off";
                    string ev = G("envcutoff") >= 0.5f ? $" · env {Oct(G("envamount"))}" : "";
                    string live = snap.Live && snap.CutoffHz > 0 ? $" · now {NotaNum.Hz(snap.CutoffHz)}" : "";
                    return $"Filter {SamplerModel.FilterNames[t]} · cutoff {NotaNum.Hz(SamplerModel.CutoffHz(G("cutoff")))} · reso {NotaNum.Pct(G("resonance"))} · {kt}{ev}{live}";
                }
            }
        }
        var statusBar = new Grid { Height = StatusH, ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        statusBar.Children.Add(status);
        Grid.SetColumn(meta, 1); statusBar.Children.Add(meta);
        var statusHost = new Border
        {
            Height = StatusH, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 1, 0, 0),
            Background = NotaPalette.SurfaceAbyss, Padding = new Thickness(8, 0), Child = statusBar,
        };

        // ---- assembly -----------------------------------------------------------------
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = NotaSpace.DeviceGap, Margin = new Thickness(NotaSpace.DeviceGap) };
        body.Children.Add(tabPanel);
        Grid.SetColumn(rail, 1); body.Children.Add(rail);
        DockPanel.SetDock(statusHost, Dock.Bottom);
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.Gutter, Children = { statusHost, body } };

        showTab = () =>
        {
            for (int i = 0; i < TabNames.Length; i++)
            {
                bool on = i == tab;
                tabCells[i].Background = on ? NotaPalette.SurfaceRaised : Brushes.Transparent;
                tabCells[i].BorderBrush = on ? Brass : Brushes.Transparent;
                tabTexts[i].Foreground = on ? AccentBright : TextTertiary;
                tabTexts[i].FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                pages[i].IsVisible = on;
            }
            hint.Text = TabHints[tab];
            Refresh();
        };
        refresh = () =>
        {
            foreach (var r in readouts) r();
            status.Text = Summary();
        };
        void Live()
        {
            int n = acc.Scope(scope);
            SamplerModel.Parse(scope.AsSpan(0, Math.Max(0, n)), snap);
            if (acc.Info(out long sid, out int r) && sid == sampleId) rootv = r;   // the root may move from MCP / undo
            wave.SetPlayhead(snap.Live ? snap.PlayPos : acc.PlayPosition());
            int voices = snap.Live ? snap.Voices : 0;
            double esr = engine.SampleRate > 0 ? engine.SampleRate : 48000;
            meta.Text = hasSample
                ? $"{NotaNum.Unit(esr / 1000, "0.#", "kHz")} · {NotaNum.Unit(durSec, "0.00", "s")} sample · {voices}/{SamplerModel.Voices} voices · CPU {NotaNum.Unit(engine.CpuLoad * 100, "0.0", "%")}"
                : $"{NotaNum.Unit(esr / 1000, "0.#", "kHz")} · CPU {NotaNum.Unit(engine.CpuLoad * 100, "0.0", "%")}";
            Refresh();
        }
        registerTick(Live);
        setLiveViz?.Invoke(Refresh);
        showTab();
        Live();
        return root;
    }

    // A section: card ground, hairline, radius 6 — the almanac's section box.
    private static Border SectionBox(Control child) => new()
    {
        Background = NotaPalette.SurfaceCard, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
        CornerRadius = NotaRadius.Tile, ClipToBounds = true, Child = child,
    };

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
