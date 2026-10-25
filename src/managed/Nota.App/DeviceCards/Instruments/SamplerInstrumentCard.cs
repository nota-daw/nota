// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the Nota Sampler editor (instrument kind 1), on the 700 × 260 frame
// the style guide draws for it (two pressed-in sections, no status line):
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
//   Rail 204    always there: the voice mode (Poly 16 / Mono / Choke), Volume, Pan,
//               Glide, the Output level, Vel → Vol and the output meter.
//
// Values are orange; what the engine is doing now (the voice on the envelope, the cutoff it
// hears, the key that plays) is grey; Crossfade is ink 4 while the loop is off. Every control is a plugin param → automation / persist / clone, and
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
    public string Subtitle => "Built-in";
    public double CardWidth => 700;

    private const double RailW = 204, TabH = 22;
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
            Text = NotaText.Label(t), FontSize = NotaType.DeviceCaption,
            Foreground = c ?? TextTertiary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        static TextBlock Mono(string t, IBrush? c = null, double fs = NotaType.DeviceCaption) => new()
        {
            Text = t, FontSize = fs, Foreground = c ?? TextSecondary,
            VerticalAlignment = VerticalAlignment.Center, FontFamily = NotaFonts.UiFamily,
        };

        // ---- shared builders ----------------------------------------------------------
        Control PKnob(string id, string label, Func<float, string> fmt, double size = NotaSize.Knob, double cellW = 54,
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

        Border Chips(string[] names, Func<int> cur, Action<int> pick, double padX = 6, double fontSize = NotaType.KnobValue, string? learnId = null)
        {
            var seg = Segments(names, cur, iv => { pick(iv); Refresh(); }, out var sync, padX: padX, fontSize: fontSize);
            readouts.Add(sync);
            if (learnId is not null && I(learnId) is var li and >= 0) acc.Learn(seg, li);
            return seg;
        }
        Border ParamChips(string id, string[] names, double padX = 6, double fontSize = NotaType.KnobValue)
            => Chips(names, () => Sel(id, names.Length), iv => Write(id, iv / (double)(names.Length - 1)), padX, fontSize, id);

        Grid Slider(string label, string id, Func<string> text, bool bipolar = false, bool modulation = false, string? tip = null)
        {
            int pi = I(id);
            var row = SliderRow(label, () => G(id), v => { SetP(id, v); Refresh(); }, text, out var sync,
                begin: pi >= 0 ? () => Begin(id) : null,
                end: pi >= 0 ? () => End(id) : null,
                reset: pi >= 0 ? () => { Write(id, Def(id)); Refresh(); } : null,
                bipolar: bipolar, labelWidth: 46, valueWidth: 40, modulation: modulation);
            readouts.Add(sync);
            if (pi >= 0) acc.Learn(row, pi);
            if (tip is not null) ToolTip.SetTip(row, tip);
            return row;
        }

        // A raised text button inside the device (no border, a light top edge); a latching one
        // takes the soft tint with orange text while on; an unavailable one is ink 4.
        Border Button(string text, Action click, string tip, Func<bool>? on = null, Func<bool>? enabled = null, string? learnId = null)
        {
            var tb = new TextBlock { Text = text, FontSize = NotaType.DeviceValue, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var b = new Border
            {
                Height = NotaSize.Dropdown, CornerRadius = NotaRadius.Field, Padding = new Thickness(10, 0),
                Cursor = new Cursor(StandardCursorType.Hand), Child = tb, VerticalAlignment = VerticalAlignment.Center,
            };
            b.BindResource(Border.BoxShadowProperty, "Shadow.Lifted");
            ToolTip.SetTip(b, tip);
            void Paint()
            {
                bool lit = on?.Invoke() ?? false, ok = enabled?.Invoke() ?? true;
                b.Background = lit ? NotaPalette.AccentSoft : NotaPalette.SurfaceHover;
                tb.Foreground = !ok ? TextDisabled : lit ? AccentBright : TextPrimary;
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
            Spacing = 6, VerticalAlignment = VerticalAlignment.Center,
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
        }, padX: 6, learnId: "loopmode");
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
        var sampleBtns = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = { snapBtn, trimBtn } };
        var sampleRow = Row(0, (Labeled("LOOP", loopSeg), "Auto"), (new Panel(), "*"), (xfadeKnob, "Auto"), (gainKnob, "Auto"), (new Panel(), "*"), (new Panel(), "*"), (sampleBtns, "Auto"));

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
        var rootText = Mono(SamplerModel.NoteName(rootv), TextPrimary, NotaType.DeviceValue);
        rootText.HorizontalAlignment = HorizontalAlignment.Center;
        readouts.Add(() => rootText.Text = SamplerModel.NoteName(rootv));
        Border StepBtn(GlyphKind g, int d, string tip)
        {
            var b = new Border
            {
                Width = NotaSize.Dropdown, Height = NotaSize.Dropdown, CornerRadius = NotaRadius.Field,
                Background = NotaPalette.SurfaceHover, Cursor = new Cursor(StandardCursorType.Hand),
                Child = new Glyph(g, 8) { Foreground = TextSecondary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            b.BindResource(Border.BoxShadowProperty, "Shadow.Lifted");
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
            Height = NotaSize.Dropdown, MinWidth = 40, CornerRadius = NotaRadius.Field,
            Background = NotaPalette.Groove, Child = rootText,
        };
        rootBox.BindResource(Border.BoxShadowProperty, "Shadow.Groove");
        rootBox.PointerWheelChanged += (wt, e) =>
        {
            if (WheelInput.Step(e, wt) is not { } s) return;
            e.Handled = true; if (s == 0) return;
            rootv = Math.Clamp(rootv + s, 0, 127); acc.SetRoot(rootv); Refresh();
        };
        ToolTip.SetTip(rootBox, "The root note — scroll to step it");
        var rootStepper = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { StepBtn(GlyphKind.Minus, -1, "Root a semitone down"), rootBox, StepBtn(GlyphKind.Plus, +1, "Root a semitone up") } };
        int detected = SamplerModel.DetectRoot(sampleName);
        var detectBtn = Button("Detect from file name", () => { if (detected >= 0) { rootv = detected; acc.SetRoot(detected); } },
            detected >= 0 ? $"\"{sampleName}\" names {SamplerModel.NoteName(detected)} — make it the root"
                : sampleName.Length > 0 ? $"\"{sampleName}\" names no note (e.g. C4, F#3, Eb2)" : "The sample's file name isn't known",
            on: () => detected >= 0 && detected == rootv, enabled: () => detected >= 0);
        var pitchRow = Row(10,
            (PKnob("transpose", "TRANSPOSE", St, tip: "Shift every note, ±24 semitones"), "Auto"),
            (PKnob("detune", "DETUNE", Cents, tip: "Fine tune, ±50 cents"), "Auto"),
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
        var velWord = Mono("", TextSecondary, NotaType.DeviceCaption);
        readouts.Add(() => velWord.Text = G("velamount") > 0.005f ? NotaNum.Pct(G("velamount")) : "off");
        var velBlock = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Cap("VEL → VOL"), velSwitch, velWord } };
        ToolTip.SetTip(velBlock, "Velocity sets the level — the amount is the rail's VEL → VOL");
        var envRow = Row(16,
            (PKnob("attack", "ATTACK", v => NotaNum.Time(SamplerModel.AttackSec(v))), "Auto"),
            (PKnob("decay", "DECAY", v => NotaNum.Time(SamplerModel.DecaySec(v))), "Auto"),
            (PKnob("sustain", "SUSTAIN", DbLin, NotaSize.Knob, 58), "Auto"),
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
        var typeSeg = ParamChips("filtertype", SamplerModel.FilterNames, padX: 5);
        ToolTip.SetTip(typeSeg, "Off · low-pass · high-pass · band-pass (12\u2009dB/oct)");
        var cutKnob = PKnob("cutoff", "CUTOFF", v => NotaNum.Hz(SamplerModel.CutoffHz(v)), NotaSize.Knob, 50, emphasised: true);
        var resoKnob = PKnob("resonance", "RESO", v => NotaNum.Pct(v), cellW: 44);
        var ktKnob = PKnob("keytrack", "KEYTRACK", v => NotaNum.Pct(v), cellW: 50, tip: "The cutoff follows the key, 0 … 100\u2009%");
        var envBtn = Button("Env → Cutoff", () => Write("envcutoff", G("envcutoff") >= 0.5f ? 0 : 1),
            "Let the amp envelope open (or close) the filter — the depth is ENV", on: () => G("envcutoff") >= 0.5f, learnId: "envcutoff");
        var envAmt = PKnob("envamount", "ENV", Oct, cellW: 46, tip: "How far the envelope moves the cutoff, ±6 octaves");
        var filterBody = new[] { cutKnob, resoKnob, ktKnob };
        foreach (var c in filterBody) DimWhen(c, () => Sel("filtertype", 4) == 0);
        DimWhen(envAmt, () => Sel("filtertype", 4) == 0 || G("envcutoff") < 0.5f);
        var filterRow = Row(8, (Labeled("TYPE", typeSeg), "Auto"), (cutKnob, "Auto"), (resoKnob, "Auto"), (ktKnob, "Auto"),
            (new Panel(), "*"), (envBtn, "Auto"), (envAmt, "Auto"));

        // ---- the tab panel ------------------------------------------------------------
        var views = new Control[] { wave, keys, env, filt };
        var rows = new Control[] { sampleRow, pitchRow, envRow, filterRow };
        var pages = new Control[views.Length];
        for (int i = 0; i < views.Length; i++)
        {
            var rowHost = new Border
            {
                BorderBrush = NotaPalette.Rule, BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 6, 0, 0), Child = rows[i],
            };
            views[i].Margin = new Thickness(0, 0, 0, 6);
            var page = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(0, 6, 0, 0) };
            page.Children.Add(views[i]);
            Grid.SetRow(rowHost, 1); page.Children.Add(rowHost);
            pages[i] = page;
        }
        Action showTab = () => { };
        var hint = Mono("", TextTertiary, NotaType.Eyebrow);
        hint.HorizontalAlignment = HorizontalAlignment.Right;
        var tabStrip = SynthInstrumentCard.TabStrip(TabNames, () => tab, i => { tab = i; showTab(); }, out var syncTabs, hint);
        var tabHost = new Panel();
        foreach (var p in pages) tabHost.Children.Add(p);
        var tabGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        tabGrid.Children.Add(tabStrip);
        Grid.SetRow(tabHost, 1); tabGrid.Children.Add(tabHost);
        var tabPanel = SectionBox(tabGrid);

        // ---- the rail · voices and output -----------------------------------------------
        var voiceSeg = ParamChips("voicemode", SamplerModel.VoiceNames, padX: 5);
        ToolTip.SetTip(voiceSeg, "Poly: 16 voices · Mono: one at a time (legato with Glide) · Choke: a new note cuts the ringing ones");
        var railHead = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var railCap = new TextBlock
        {
            Text = "Voices", FontSize = NotaType.DeviceBody, FontWeight = FontWeight.SemiBold,
            Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center,
        };
        railHead.Children.Add(railCap);
        voiceSeg.HorizontalAlignment = HorizontalAlignment.Right; voiceSeg.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(voiceSeg, 1); railHead.Children.Add(voiceSeg);

        var sliders = new StackPanel
        {
            Spacing = 9,
            Children =
            {
                Slider("VOLUME", "volume", () => DbLin(G("volume")), tip: "The voices' level"),
                Slider("PAN", "pan", () => PanText(G("pan")), bipolar: true),
                Slider("GLIDE", "glide", () => GlideText(G("glide")), tip: "Slide from the last note — in Mono, legato: no retrigger"),
            },
        };
        var outKnob = PKnob("output", "OUTPUT", v => NotaNum.Pct(v), NotaSize.Knob, 40, tip: "The level after everything");
        var velKnob = PKnob("velamount", "VEL → VOL", v => NotaNum.Pct(v), cellW: 52, tip: "How much velocity sets the level");

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
            var mg = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), ColumnSpacing = 5, HorizontalAlignment = HorizontalAlignment.Right, Height = 58, VerticalAlignment = VerticalAlignment.Center };
            mg.Children.Add(meter);
            Grid.SetColumn(meterScale, 1); mg.Children.Add(meterScale);
            meterBlock = mg;
        }
        else meterBlock = new Panel();
        var outRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 10, Margin = new Thickness(0, 10, 0, 0) };
        outRow.Children.Add(outKnob);
        Grid.SetColumn(velKnob, 1); outRow.Children.Add(velKnob);
        Grid.SetColumn(meterBlock, 2); outRow.Children.Add(meterBlock);
        var outHost = new Border { BorderBrush = NotaPalette.Rule, BorderThickness = new Thickness(0, 1, 0, 0), Child = outRow };

        sliders.Margin = new Thickness(0, 8, 0, 12);
        var railGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        railGrid.Children.Add(railHead);
        Grid.SetRow(sliders, 1); railGrid.Children.Add(sliders);
        Grid.SetRow(outHost, 2); railGrid.Children.Add(outHost);
        var rail = SectionBox(railGrid);
        rail.Padding = new Thickness(SectionPad, 8, SectionPad, SectionPad);
        rail.Width = RailW;

        // ---- assembly -----------------------------------------------------------------
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = SectionGap, Margin = new Thickness(0, CardPad, 0, 0) };
        root.Children.Add(tabPanel);
        Grid.SetColumn(rail, 1); root.Children.Add(rail);

        showTab = () =>
        {
            syncTabs();
            for (int i = 0; i < TabNames.Length; i++) pages[i].IsVisible = i == tab;
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

    // A pressed-in section with the guide's 12 of room inside.
    private static Decorator SectionBox(Control child)
    {
        var s = DeviceCardKit.Section(child);
        s.Padding = new Thickness(SectionPad);
        return s;
    }

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
