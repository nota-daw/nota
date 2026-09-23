// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Physical editor (instrument kind 2): a modal voice in
// the 700 × 260 card the almanac draws for it. The chain reads left to right — what
// strikes, what rings, what comes out; the tabs pick what the centre shows.
//
//   Centre      two tabs:
//               Exciter   — the mallet (level, stiffness, strike noise, colour) › the
//                           noise burst: its filter type, a draggable ADSR with the
//                           stage times under it, level, envelope → filter, freq, reso.
//               Resonator — bank 1 / 2, on / off, the material type and the structure
//                           (1→2 serial, 1+2 parallel); the bank's partials as the engine
//                           tunes them (drag: ↔ ratio, ↕ bright) beside its seven knobs
//                           and the 1+2 mix.
//   Rail 186    Output: Poly / Mono, tune, fine and note-off rows, volume, pan and the
//               track's meter.
//
// Brass is the parameter itself; teal is modulation. Everything is a plugin param →
// automation / persist / clone, and the card follows automation live.

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

internal sealed class PhysicalInstrumentCard : IInstrumentCard
{
    public bool BodyOnly => true;
    public string Subtitle => "MODAL";
    public double CardWidth => 700;

    public string? VoiceLabel(IAudioEngine engine, int trackId, int active)
    {
        int mi = -1;
        for (int i = 0, n = engine.PluginParamCount(trackId, -1); i < n; i++)
            if (engine.PluginParamId(trackId, -1, i) == "mono") { mi = i; break; }
        return mi >= 0 && engine.PluginParamGet(trackId, -1, mi) >= 0.5f ? "MONO" : $"{Math.Max(0, active)}/{PhysicalModel.Voices}";
    }

    private const double RailW = 186, TabH = 20, StatusH = 18, KnobS = 30;

    private static readonly string[] TabNames = { "Exciter", "Resonator" };
    private static readonly string[] TypeNames = PhysicalModel.TypeNames;

    private static string Pct(float v) => NotaNum.Pct(v);
    private static string Bip(float v) => NotaNum.Unit((v - 0.5f) * 200, "+0;−0;0", "%");
    private static string Secs(double s) => NotaNum.Time(s);
    private static string Semis(double st, string f = "+0;−0;0") => NotaNum.Unit(st, f, "st");
    private static string PanText(float v)
    {
        double p = (v - 0.5) * 200;
        return Math.Abs(p) < 0.5 ? "C" : (p < 0 ? "L" : "R") + NotaNum.Str(Math.Abs(p), "0");
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
        void Write(string id, float v) { Begin(id); SetP(id, v); End(id); }
        int Sel(string id, int n) => Math.Clamp((int)Math.Round(G(id) * (n - 1)), 0, n - 1);
        (int, string) P(string id) => (I(id), id);
        bool IsMono() => G("mono") >= 0.5f;
        bool R2On() => G("r2on") >= 0.5f;
        bool Serial() => G("structure") < 0.5f;
        string TypeWord(string pre) => TypeNames[PhysicalModel.TypeIndex(G(pre + "type"))].ToLowerInvariant();

        var readouts = new List<Action>();
        int tab = 0, bank = 0;
        var scope = new float[PhysicalModel.ScopeLength];

        var noiseEnv = new VoltEnv(engine, track) { VerticalAlignment = VerticalAlignment.Stretch, MinWidth = 90, MinHeight = 50 };
        noiseEnv.Target("NOISE ENV", P("noisea"), P("noised"), P("noises"), P("noiser"));
        var partials = new PhysPartialsView { VerticalAlignment = VerticalAlignment.Stretch };
        ToolTip.SetTip(partials, "Partials as the engine tunes them — drag ↔ to spread the series (Ratio), ↕ to tilt the highs (Bright)");

        static TextBlock Cap(string t, IBrush? c = null) => new()
        {
            Text = t, FontSize = NotaType.KnobLabel, FontWeight = FontWeight.Bold,
            LetterSpacing = NotaType.KnobLabelTracking, Foreground = c ?? TextTertiary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        static TextBlock Mono(string t, IBrush? c = null, double fs = 8) => new()
        {
            Text = t, FontSize = fs, Foreground = c ?? TextSecondary,
            VerticalAlignment = VerticalAlignment.Center, FontFamily = NotaFonts.MonoFamily,
        };

        var hint = Mono("", TextTertiary, NotaType.Axis);
        hint.HorizontalAlignment = HorizontalAlignment.Right; hint.Margin = new Thickness(0, 0, 8, 0);
        hint.TextTrimming = TextTrimming.CharacterEllipsis;
        var status = new TextBlock { FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var meta = Mono("", TextSecondary);
        Action showTab = () => { };

        // The partial window: the selected bank's modes from the engine's scope, the other
        // bank behind when it sounds, and how many of them the reference note can hold.
        void RefreshPartials()
        {
            int n = engine.InstrumentScope(track, scope);
            var s = scope.AsSpan(0, Math.Max(0, n));
            var mine = PhysicalModel.Bank(s, bank);
            var other = PhysicalModel.Bank(s, 1 - bank);
            bool r2 = R2On();
            double sr = engine.SampleRate > 0 ? engine.SampleRate : 48000;
            double f0 = n > 2 && scope[2] > 0 ? scope[2] : 261.63;
            string pre = bank == 0 ? "r1" : "r2";
            int audible = PhysicalModel.AudiblePartials(mine, f0, sr);
            partials.Set($"PARTIALS · {TypeWord(pre).ToUpperInvariant()}", mine, r2 ? other : Array.Empty<(double, double, double)>(),
                sr * 0.49 / f0, $"{audible} partials · inharm {NotaNum.Pct(G(pre + "inharm"))}", bank == 1 && !r2);
        }

        void Refresh()
        {
            noiseEnv.Refresh();
            foreach (var r in readouts) r();
            RefreshPartials();
            hint.Text = Hint();
            status.Text = Summary();
        }

        // ---- shared builders --------------------------------------------------
        Control PKnob(string id, string label, Func<float, string> fmt, double size = KnobS, double cellW = 48,
            IBrush? arc = null, Func<bool>? dim = null)
        {
            if (!idx.TryGetValue(id, out var pi)) return new Panel();
            var value = new TextBlock
            {
                Text = fmt(G(id)), FontSize = NotaType.KnobValue, FontFamily = NotaFonts.MonoFamily,
                Foreground = TextPrimary, HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center, LineHeight = 10,
            };
            var knob = new Knob(G(id), 1.0)
            {
                Accent = true, ArcColor = arc, Default = engine.InstrumentParamDefault(track, pi),
                Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center,
            };
            knob.ValueChanged += v => { engine.PluginParamSet(track, -1, pi, (float)v); value.Text = fmt((float)v); Refresh(); };
            knob.GestureBegin += () => Begin(id);
            knob.GestureEnd += () => End(id);
            ctx.AddInstFader(pi, knob, value, fmt);
            MidiLearn.Bind(knob, MidiTarget.PluginParam(track, -1, pi), engine.PluginParamName(track, -1, pi));
            if (dim is not null) readouts.Add(() => knob.IsDim = dim());
            var cell = KnobCell(label, knob, value, cellW);
            cell.HorizontalAlignment = HorizontalAlignment.Center;
            cell.VerticalAlignment = VerticalAlignment.Center;
            return cell;
        }

        Border Chips(string id, string[] names, Func<int>? current = null, Action<int>? pick = null,
            Func<bool>? dim = null, double padX = 5)
        {
            int n = names.Length;
            var seg = Segments(names, current ?? (() => Sel(id, n)),
                pick ?? (iv => { Write(id, iv / (float)(n - 1)); Refresh(); }), out var sync, dim: dim, padX: padX);
            readouts.Add(sync);
            if (I(id) is var pi and >= 0) MidiLearn.Bind(seg, MidiTarget.PluginParam(track, -1, pi), engine.PluginParamName(track, -1, pi));
            return seg;
        }

        Grid Slider(string label, string id, Func<string> text, bool bipolar = false, Func<bool>? dim = null)
        {
            int pi = I(id);
            var row = SliderRow(label, () => G(id), v => { SetP(id, (float)v); Refresh(); }, text, out var sync,
                begin: pi >= 0 ? () => Begin(id) : null,
                end: pi >= 0 ? () => End(id) : null,
                reset: pi >= 0 ? () => { SetP(id, engine.InstrumentParamDefault(track, pi)); Refresh(); } : null,
                bipolar: bipolar, dim: dim, labelWidth: 46, valueWidth: 30);
            readouts.Add(sync);
            if (pi >= 0) MidiLearn.Bind(row, MidiTarget.PluginParam(track, -1, pi), engine.PluginParamName(track, -1, pi));
            return row;
        }

        // Knobs packed into a grid of equal cells, rows spread over the height.
        static Grid KnobGrid(int cols, params Control[] ks)
        {
            int rows = (ks.Length + cols - 1) / cols;
            var g = new Grid { VerticalAlignment = VerticalAlignment.Stretch };
            for (int c = 0; c < cols; c++) g.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            for (int r = 0; r < rows; r++) g.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));
            for (int i = 0; i < ks.Length; i++) { Grid.SetColumn(ks[i], i % cols); Grid.SetRow(ks[i], i / cols); g.Children.Add(ks[i]); }
            return g;
        }

        // ---- Exciter tab ------------------------------------------------------
        var malletGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 3 };
        {
            var knobs = KnobGrid(2,
                PKnob("malletvol", "VOLUME", Pct),
                PKnob("malletstiff", "STIFF", Pct),
                PKnob("malletnoise", "NOISE", Pct),
                PKnob("malletcolor", "COLOR", Pct));
            ToolTip.SetTip(knobs, "Stiffness sets the contact time: soft = long and dark, hard = short and bright");
            malletGrid.Children.Add(Cap("MALLET"));
            Grid.SetRow(knobs, 1); malletGrid.Children.Add(knobs);
        }

        var noiseGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 3 };
        {
            var envCap = Mono("", Teal, NotaType.Axis);
            envCap.HorizontalAlignment = HorizontalAlignment.Right;
            readouts.Add(() =>
            {
                double oct = PhysicalModel.NoiseEnvOct(G("noiseenv"));
                envCap.Text = Math.Abs(oct) < 0.05 ? "env → freq off" : "env → freq " + NotaNum.Unit(oct, "+0.0;−0.0;0", "oct");
            });
            var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 5 };
            var typeChips = Chips("noisetype", PhysicalModel.NoiseTypes);
            head.Children.Add(Cap("NOISE"));
            Grid.SetColumn(typeChips, 1); head.Children.Add(typeChips);
            Grid.SetColumn(envCap, 2); head.Children.Add(envCap);

            // The ADSR editor with its stage times along the bottom edge.
            var envTimes = Mono("", NotaPalette.TextAxis, NotaType.Axis);
            envTimes.HorizontalAlignment = HorizontalAlignment.Right; envTimes.VerticalAlignment = VerticalAlignment.Bottom;
            envTimes.Margin = new Thickness(0, 0, 6, 1); envTimes.IsHitTestVisible = false;
            readouts.Add(() => envTimes.Text =
                $"{Secs(PhysicalModel.NoiseAttack(G("noisea")))} · {Secs(PhysicalModel.NoiseDecay(G("noised")))} · "
                + $"{Pct(G("noises"))} · {Secs(PhysicalModel.NoiseRelease(G("noiser")))}");
            var envHost = new Panel { Children = { noiseEnv, envTimes } };

            var knobs = KnobGrid(2,
                PKnob("noisevol", "VOLUME", Pct, cellW: 46),
                PKnob("noiseenv", "ENV", Bip, cellW: 46, arc: Teal),
                PKnob("noisefreq", "FREQ", v => NotaNum.Hz(PhysicalModel.NoiseFreqHz(v)), cellW: 46),
                PKnob("noisereso", "RESO", Pct, cellW: 46));
            knobs.Width = 96;
            var noiseBody = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 5 };
            noiseBody.Children.Add(envHost);
            Grid.SetColumn(knobs, 1); noiseBody.Children.Add(knobs);

            noiseGrid.Children.Add(head);
            Grid.SetRow(noiseBody, 1); noiseGrid.Children.Add(noiseBody);
        }

        var exciterGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("104,10,*"), ColumnSpacing = 5 };
        exciterGrid.Children.Add(malletGrid);
        var arrow = new Glyph(GlyphKind.ChevronRight, 9) { Foreground = NotaPalette.BorderStrong, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(arrow, 1); exciterGrid.Children.Add(arrow);
        Grid.SetColumn(noiseGrid, 2); exciterGrid.Children.Add(noiseGrid);
        var exciterBody = new Border { Padding = new Thickness(6, 5), Child = exciterGrid };

        // ---- Resonator tab ----------------------------------------------------
        // Both banks' controls are built once; the 1 / 2 segment shows one set.
        Control MixCell(Func<bool> dim)
        {
            int pi = I("resmix");
            var trackC = new SliderTrack
            {
                Width = 44, HorizontalAlignment = HorizontalAlignment.Center,
                Reset = pi >= 0 ? () => { Write("resmix", engine.InstrumentParamDefault(track, pi)); Refresh(); } : null,
            };
            var val = Mono("", TextPrimary, NotaType.KnobValue);
            val.HorizontalAlignment = HorizontalAlignment.Center;
            var cap = Cap("MIX 1+2"); cap.HorizontalAlignment = HorizontalAlignment.Center;
            trackC.Changed += v => { SetP("resmix", (float)v); Refresh(); };
            trackC.GestureBegin += () => Begin("resmix");
            trackC.GestureEnd += () => End("resmix");
            readouts.Add(() =>
            {
                bool d = dim();
                if (!trackC.Dragging) trackC.Norm = G("resmix");
                trackC.IsDim = d;
                var (g1, g2) = PhysicalModel.MixGains(G("resmix"));
                val.Text = $"{NotaNum.Str(g1 * 100, "0")} / {NotaNum.Str(g2 * 100, "0")}";
                val.Foreground = d ? TextDisabled : TextPrimary;
                cap.Foreground = d ? TextDisabled : TextTertiary;
            });
            var cell = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Children = { cap, trackC, val } };
            ToolTip.SetTip(cell, "Res 1 against Res 2 — both at full level in the middle; in 1→2, res 1's own sound against the body it rings");
            if (pi >= 0) MidiLearn.Bind(cell, MidiTarget.PluginParam(track, -1, pi), "Res Mix");
            return cell;
        }

        var typeHosts = new Control[2];
        var knobHosts = new Control[2];
        for (int b = 0; b < 2; b++)
        {
            string pre = b == 0 ? "r1" : "r2";
            Func<bool>? off = b == 1 ? () => !R2On() : null;
            typeHosts[b] = Chips(pre + "type", TypeNames, dim: off, padX: 4);
            knobHosts[b] = KnobGrid(4,
                PKnob(pre + "decay", "DECAY", v => Secs(PhysicalModel.DecaySeconds(v)), dim: off),
                PKnob(pre + "material", "MATERIAL", Pct, dim: off),
                PKnob(pre + "bright", "BRIGHT", Pct, dim: off),
                PKnob(pre + "inharm", "INHARM", Pct, dim: off),
                PKnob(pre + "ratio", "RATIO", v => NotaNum.Str(PhysicalModel.RatioExponent(v), "0.00"), dim: off),
                PKnob(pre + "hit", "HIT", Pct, dim: off),
                PKnob(pre + "tune", "TUNE", v => Semis(PhysicalModel.BankSemis(v), "+0.0;−0.0;0"), dim: off),
                MixCell(() => !R2On()));
        }

        partials.StartH = () => G(bank == 0 ? "r1ratio" : "r2ratio");
        partials.StartV = () => G(bank == 0 ? "r1bright" : "r2bright");
        partials.GestureBegin += () => { string pre = bank == 0 ? "r1" : "r2"; Begin(pre + "ratio"); Begin(pre + "bright"); };
        partials.GestureEnd += () => { string pre = bank == 0 ? "r1" : "r2"; End(pre + "ratio"); End(pre + "bright"); };
        partials.DragH += v => { SetP(bank == 0 ? "r1ratio" : "r2ratio", (float)v); Refresh(); };
        partials.DragV += v => { SetP(bank == 0 ? "r1bright" : "r2bright", (float)v); Refresh(); };

        var bankSeg = Segments(new[] { "1", "2" }, () => bank, iv => { bank = iv; showTab(); }, out var bankSync, padX: 5);
        readouts.Add(bankSync);
        ToolTip.SetTip(bankSeg, "Which resonator the controls below edit");
        // On / Off belongs to the bank on screen: bank 1 always rings, bank 2 is switchable.
        var onSeg = Chips("r2on", new[] { "Off", "On" }, () => bank == 0 || R2On() ? 1 : 0,
            iv => { if (bank == 1) { Write("r2on", iv); Refresh(); } }, dim: () => bank == 0);
        ToolTip.SetTip(onSeg, "Resonator 2 on / off — resonator 1 always rings");
        var structSeg = Chips("structure", new[] { "1→2", "1+2" }, dim: () => !R2On(), padX: 4);
        ToolTip.SetTip(structSeg, "1→2: resonator 1 rings into resonator 2 · 1+2: both struck, mixed");
        var typeHost = new Panel { Children = { typeHosts[0], typeHosts[1] } };

        var resHead = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto,Auto"), ColumnSpacing = 5, Margin = new Thickness(6, 0) };
        {
            var structCap = Cap("STRUCT"); structCap.HorizontalAlignment = HorizontalAlignment.Right;
            var cs = new Control[] { bankSeg, onSeg, typeHost, new Panel(), structCap, structSeg };
            for (int i = 0; i < cs.Length; i++) { Grid.SetColumn(cs[i], i); resHead.Children.Add(cs[i]); }
        }
        var resHeadHost = new Border { Height = TabH, BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 0, 0, 1), Child = resHead };

        var knobHost = new Panel { Children = { knobHosts[0], knobHosts[1] } };
        var resBody = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*"), ColumnSpacing = 6, Margin = new Thickness(6, 5) };
        resBody.Children.Add(partials);
        Grid.SetColumn(knobHost, 1); resBody.Children.Add(knobHost);

        var resonatorGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        resonatorGrid.Children.Add(resHeadHost);
        Grid.SetRow(resBody, 1); resonatorGrid.Children.Add(resBody);

        // ---- the tab strip ----------------------------------------------------
        var bodies = new Control[] { exciterBody, resonatorGrid };
        var tabCells = new Border[TabNames.Length];
        var tabTexts = new TextBlock[TabNames.Length];
        var tabRow = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < TabNames.Length; i++)
        {
            int iv = i;
            var tb = new TextBlock { Text = TabNames[i], FontSize = NotaType.DeviceSection, VerticalAlignment = VerticalAlignment.Center };
            var cell = new Border
            {
                Padding = new Thickness(8, 0), BorderThickness = new Thickness(0, 0, 0, 2),
                Cursor = new Cursor(StandardCursorType.Hand), Child = tb,
            };
            cell.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed) return;
                tab = iv; showTab(); e.Handled = true;
            };
            tabCells[i] = cell; tabTexts[i] = tb; tabRow.Children.Add(cell);
        }
        var tabStrip = new Grid { Height = TabH, ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        tabStrip.Children.Add(tabRow);
        Grid.SetColumn(hint, 1); tabStrip.Children.Add(hint);

        showTab = () =>
        {
            for (int i = 0; i < TabNames.Length; i++)
            {
                bool on = i == tab;
                tabCells[i].Background = on ? NotaPalette.SurfaceRaised : Brushes.Transparent;
                tabCells[i].BorderBrush = on ? Brass : Brushes.Transparent;
                tabTexts[i].Foreground = on ? AccentBright : TextTertiary;
                tabTexts[i].FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                bodies[i].IsVisible = on;
            }
            for (int b = 0; b < 2; b++) { typeHosts[b].IsVisible = b == bank; knobHosts[b].IsVisible = b == bank; }
            Refresh();
        };

        var tabHost = new Panel();
        foreach (var b in bodies) tabHost.Children.Add(b);
        var tabGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        tabGrid.Children.Add(HeaderStrip(tabStrip));
        Grid.SetRow(tabHost, 1); tabGrid.Children.Add(tabHost);
        var tabPanel = SectionBox(tabGrid);

        // ---- rail · Output ----------------------------------------------------
        var monoChips = Chips("mono", new[] { "Poly", "Mono" }, () => IsMono() ? 1 : 0, iv => { Write("mono", iv); Refresh(); });
        monoChips.HorizontalAlignment = HorizontalAlignment.Right;
        monoChips.Margin = new Thickness(0, 0, 6, 0);
        ToolTip.SetTip(monoChips, "Poly: 8 voices ring over each other · Mono: a new strike chokes the sounding note");
        var outCap = Cap("OUTPUT"); outCap.Margin = new Thickness(8, 0, 0, 0);
        var outHead = new Grid { Height = TabH, ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        outHead.Children.Add(outCap);
        Grid.SetColumn(monoChips, 1); outHead.Children.Add(monoChips);

        var noteOffRow = Slider("NOTE OFF", "noteoff", () => Pct(G("noteoff")));
        ToolTip.SetTip(noteOffRow, "How hard a released key damps the body — at 0 it rings on, fully up it stops at once");
        var outTop = new StackPanel
        {
            Spacing = 6, Margin = new Thickness(8, 6, 8, 0),
            Children =
            {
                Slider("TUNE", "tune", () => Semis(Math.Round(PhysicalModel.TuneSemis(G("tune")))), bipolar: true),
                Slider("FINE", "fine", () => NotaNum.Unit(Math.Round(PhysicalModel.FineCents(G("fine"))), "+0;−0;0", "c"), bipolar: true),
                noteOffRow,
            },
        };

        var meter = new MeterBar { VerticalAlignment = VerticalAlignment.Stretch, Width = MeterScale.StereoWidth };
        ctx.AddDeviceRefresher(() => { if (engine.TryGetTrackMeter(track, out var m)) meter.Push(m); });
        var meterScale = new Grid { VerticalAlignment = VerticalAlignment.Stretch, RowDefinitions = new RowDefinitions(string.Join(",", ScaleRows(0, -12, -48))) };
        for (int i = 0; i < 3; i++)
        {
            var c = Mono(i == 0 ? "0" : i == 1 ? "−12" : "−48", NotaPalette.TextAxis, NotaType.Axis);
            Grid.SetRow(c, i * 2 + 1); meterScale.Children.Add(c);
        }
        var meterBlock = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), ColumnSpacing = 4,
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 9),
        };
        meterBlock.Children.Add(meter);
        Grid.SetColumn(meterScale, 1); meterBlock.Children.Add(meterScale);

        var outBottom = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), VerticalAlignment = VerticalAlignment.Stretch };
        var volCell = PKnob("volume", "VOLUME", Pct, NotaSize.KnobMain, 48);
        var panCell = PKnob("pan", "PAN", PanText, NotaSize.KnobSecondary, 42);
        outBottom.Children.Add(volCell);
        Grid.SetColumn(panCell, 1); outBottom.Children.Add(panCell);
        Grid.SetColumn(meterBlock, 2); outBottom.Children.Add(meterBlock);

        var outGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        outGrid.Children.Add(outTop);
        var outBottomFrame = new Border
        {
            BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(8, 6, 8, 5), Padding = new Thickness(0, 5, 0, 0), Child = outBottom,
        };
        Grid.SetRow(outBottomFrame, 1); outGrid.Children.Add(outBottomFrame);

        var railGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        railGrid.Children.Add(HeaderStrip(outHead));
        Grid.SetRow(outGrid, 1); railGrid.Children.Add(outGrid);
        var rail = SectionBox(railGrid);
        rail.Width = RailW;

        // ---- status strip -----------------------------------------------------
        string Exciters()
        {
            var parts = new List<string>();
            if (G("malletvol") > 0.001f) parts.Add($"mallet {Pct(G("malletvol"))}");
            if (G("noisevol") > 0.001f) parts.Add($"noise {Pct(G("noisevol"))}");
            return parts.Count == 0 ? "no exciter" : string.Join(" → ", parts);
        }
        string StructWord() => !R2On() ? "res 1 only" : Serial() ? "1→2" : "1+2";
        string BankOf() => bank == 0 ? "r1" : "r2";

        // The line at the right of the tab strip: what this tab holds, in a few words.
        string Hint() => tab switch
        {
            0 => $"{Exciters()} → {TypeWord("r1")}",
            _ => $"{StructWord()} · {TypeWord(BankOf())} · decay {Secs(PhysicalModel.DecaySeconds(G(BankOf() + "decay")))}",
        };
        string Summary()
        {
            if (tab == 0)
            {
                var s = G("malletvol") > 0.001f
                    ? $"Mallet {Pct(G("malletvol"))} · stiff {Pct(G("malletstiff"))} ({NotaNum.Unit(PhysicalModel.ContactMs(G("malletstiff")), "0.0", "ms")})"
                    : "Mallet off";
                s += G("noisevol") > 0.001f
                    ? $" · noise {Pct(G("noisevol"))} {PhysicalModel.NoiseTypes[Sel("noisetype", 3)]} {NotaNum.Hz(PhysicalModel.NoiseFreqHz(G("noisefreq")))} · env → freq {NotaNum.Unit(PhysicalModel.NoiseEnvOct(G("noiseenv")), "+0.0;−0.0;0", "oct")}"
                    : " · noise off";
                return s + $" → resonator {TypeWord("r1")}";
            }
            string pre = BankOf();
            bool on = bank == 0 || R2On();
            var line = $"Resonator {bank + 1} {TypeWord(pre)} {(on ? "on" : "off")} · decay {Secs(PhysicalModel.DecaySeconds(G(pre + "decay")))} · "
                + $"bright {Pct(G(pre + "bright"))} · inharm {Pct(G(pre + "inharm"))} · structure {StructWord()}";
            if (R2On())
            {
                var (g1, g2) = PhysicalModel.MixGains(G("resmix"));
                line += $" · mix {NotaNum.Str(g1 * 100, "0")} / {NotaNum.Str(g2 * 100, "0")}";
            }
            return line;
        }
        ctx.AddDeviceRefresher(() =>
        {
            int v = Math.Max(0, engine.InstrumentVoiceCount(track));
            string mode = IsMono() ? "MONO" : $"POLY {v}/{PhysicalModel.Voices}";
            double sr = engine.SampleRate > 0 ? engine.SampleRate : 48000;
            meta.Text = $"{mode} · NOTE OFF {Secs(PhysicalModel.NoteOffSeconds(G("noteoff")))} · {NotaNum.Unit(sr / 1000, "0.#", "kHz")}";
            RefreshPartials();   // follows the struck note (the partials' reach under Nyquist)
        });
        var statusBar = new Grid { Height = StatusH, ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        statusBar.Children.Add(status);
        Grid.SetColumn(meta, 1); statusBar.Children.Add(meta);
        var statusHost = new Border
        {
            Height = StatusH, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 1, 0, 0),
            Background = NotaPalette.SurfaceAbyss, Padding = new Thickness(8, 0), Child = statusBar,
        };

        // ---- assembly ---------------------------------------------------------
        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = NotaSpace.DeviceGap,
            Margin = new Thickness(NotaSpace.DeviceGap),
        };
        body.Children.Add(tabPanel);
        Grid.SetColumn(rail, 1); body.Children.Add(rail);

        DockPanel.SetDock(statusHost, Dock.Bottom);
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.Gutter, Children = { statusHost, body } };

        ctx.SetInstLiveViz(Refresh);
        showTab();
        return root;
    }

    // A device section: card ground, hairline, radius 6 — the almanac's section box.
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

    /// <summary>Row definitions that put a caption for each dB mark at its place on the
    /// meter's scale: a star gap, the caption (Auto), … and a star gap for the remainder.</summary>
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
