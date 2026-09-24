// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the Nota Pendulum editor (instrument kind 8): arpeggios out of
// bounces, in the 700 × 260 card the almanac draws for it. Held notes form a chord; balls
// swing across it and every degree a ball crosses plays that note.
//
//   Centre      two tabs:
//               Balls — Free / Sync · the division (or the free swing time) · bipolar
//                       Rate · the Motion curve; then one lane per ball (x = pitch, the
//                       wall it came from, the wall it strikes, the time to the wall ahead)
//                       over the bar strip the generated notes fall into. The space says
//                       "why this note", the strip "when".
//               Voice — what the bounces sound with: the wave, tone / bright / FM / note
//                       length, the envelope and volume; the last note and a level bar
//                       per sounding voice.
//   Rail 186    the balls themselves (division · rate · phase · note), always visible;
//               under them the tab's own controls — count, sort, quantize, Hold / First
//               note / Reset on Balls; spread (rate, detune, pan, humanize), the scale and
//               Humanize / Reset on Voice.
//
// Everything live comes from the engine's telemetry (PendulumModel), so the lanes are the
// balls the audio thread is actually swinging. Brass is the parameter, teal is where a
// ball is going and the spread (modulation). Every control is a plugin param →
// automation / persist / clone, and the card follows automation live.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed class PendulumInstrumentCard : IInstrumentCard
{
    public bool BodyOnly => true;
    public string Subtitle => "GEN KEYS";
    public double CardWidth => 700;

    public string? VoiceLabel(IAudioEngine engine, int trackId, int active)
    {
        for (int i = 0, n = engine.PluginParamCount(trackId, -1); i < n; i++)
            if (engine.PluginParamId(trackId, -1, i) == "balls")
                return $"{PendulumModel.BallCount(engine.PluginParamGet(trackId, -1, i))} balls";
        return null;
    }

    private const double RailW = 186, TabH = 20, StripH = 22, StatusH = 18, BarH = 34;
    private static readonly string[] TabNames = { "Balls", "Voice" };

    private static string Secs(double s) => NotaNum.Time(s);
    private static string DbOf(float v) => NotaNum.Db(v <= 0.001f ? double.NegativeInfinity : 20 * Math.Log10(v));

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
        float Def(string id) => I(id) is var i and >= 0 ? engine.InstrumentParamDefault(track, i) : 0f;
        int Sel(string id, int n) => PendulumModel.Index(G(id), n);
        int Count() => PendulumModel.BallCount(G("balls"));
        bool Synced() => G("sync") >= 0.5f;
        string DivLabel() => Synced() ? PendulumModel.DivNames[Sel("division", 5)] : NotaNum.Unit(PendulumModel.FreeSeconds(G("freerate")), "0.0", "s");

        var readouts = new List<Action>();
        int tab = 0;
        var snap = new PendulumModel.Snapshot();
        var scope = new float[PendulumModel.ScopeLength];
        var heldBuf = new int[16];
        int[] held = Array.Empty<int>();

        var lanes = new PendulumLanesView { VerticalAlignment = VerticalAlignment.Stretch };
        ToolTip.SetTip(lanes, "One lane per ball: x is pitch, low to high. Crossing a chord degree plays that note; teal shows the wall ahead");
        var bar = new PendulumBarView { Height = BarH };
        ToolTip.SetTip(bar, "The bar: generated notes land on the 1/16 grid — brass this bar, deep brass the last bar ahead of the playhead");
        var ballList = new PendulumBallList();
        var voiceBars = new PendulumVoiceBars { VerticalAlignment = VerticalAlignment.Center };

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

        var hint = Mono("", TextTertiary);
        hint.HorizontalAlignment = HorizontalAlignment.Right; hint.Margin = new Thickness(0, 0, 8, 0);
        hint.TextTrimming = TextTrimming.CharacterEllipsis;
        var status = new TextBlock { FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var meta = Mono("", TextSecondary, 8);
        var lastNote = Mono("", TextTertiary);
        Action showTab = () => { };

        // ---- live state -------------------------------------------------------------
        // The engine's snapshot; inside a rack chain the scope is empty, so the balls are
        // swung here from the params instead (same curves, not sample-locked).
        var simPhase = new double[PendulumModel.MaxBalls];
        for (int b = 0; b < simPhase.Length; b++) simPhase[b] = (double)b / simPhase.Length;
        var clock = Stopwatch.StartNew();
        double lastT = 0;
        void Simulate()
        {
            double now = clock.Elapsed.TotalSeconds, dt = Math.Clamp(now - lastT, 0, 0.1);
            lastT = now;
            int count = Count(), motion = Sel("motion", 4);
            double cyc = PendulumModel.CycleSeconds(G, engine.Bpm) * Math.Abs(PendulumModel.RateSigned(G("rate")) * 2.0);
            snap.Balls = count; snap.Held = held.Length;
            for (int b = 0; b < PendulumModel.MaxBalls; b++)
            {
                double rate = PendulumModel.BallRate(G("rate"), G("spread"), b);
                double inc = cyc > 0 && double.IsFinite(cyc) ? rate * 2.0 / cyc : 0;   // phase per second
                double ph = simPhase[b] + inc * dt;
                ph -= Math.Floor(ph);
                simPhase[b] = ph;
                int dir = Math.Abs(inc) < 1e-9 ? 0 : (inc > 0 ? ph < 0.5 : ph > 0.5) ? 1 : -1;
                double next = inc > 0 ? (ph < 0.5 ? 0.5 : 1.0) : (ph > 0.5 ? 0.5 : 0.0);
                double toWall = dir == 0 ? -1 : Math.Abs(next - ph) / Math.Abs(inc);
                snap.BallState[b] = new PendulumModel.Ball(ph, PendulumModel.Position(ph, motion), dir, -1, toWall, 1e9, rate, -1);
            }
            double bpb = 4, beat = now * engine.Bpm / 60.0;
            snap.BarPos = beat / bpb - Math.Floor(beat / bpb);
            snap.StepCount = 16;
        }

        void Poll()
        {
            int n = engine.InstrumentScope(track, scope);
            PendulumModel.Parse(scope.AsSpan(0, Math.Max(0, n)), snap);
            int hn = engine.InstrumentHeldNotes(track, heldBuf);
            if (hn != held.Length) held = new int[hn];
            Array.Copy(heldBuf, held, hn);
            if (!snap.Live) Simulate();
        }

        void Refresh()
        {
            foreach (var r in readouts) r();
            hint.Text = Hint();
            status.Text = Summary();
        }

        void Live()
        {
            Poll();
            int count = Count();
            var sorted = held;
            if (G("chordsort") >= 0.5f) { sorted = (int[])held.Clone(); Array.Reverse(sorted); }
            lanes.Set(snap, count, Sel("motion", 4), sorted, DivLabel());
            bar.Set(snap, PendulumModel.QuantNames[Sel("quantize", 3)], G("chordsort") >= 0.5f ? "down" : "up");
            ballList.Set(snap, count, DivLabel());
            voiceBars.Set(snap.Levels);
            int voices = snap.Live ? snap.Voices : Math.Max(0, engine.InstrumentVoiceCount(track));
            lastNote.Text = $"last note {PendulumModel.NoteName(snap.LastPitch)} · {voices} {(voices == 1 ? "voice" : "voices")}";
            double sr = engine.SampleRate > 0 ? engine.SampleRate : 48000;
            meta.Text = $"{voices}/{PendulumModel.Voices} voices · {NotaNum.Unit(sr / 1000, "0.#", "kHz")} · {NotaNum.Bpm(engine.Bpm)} BPM · CPU {NotaNum.Unit(engine.CpuLoad * 100, "0.0", "%")}";
            status.Text = Summary();
        }

        // ---- shared builders ----------------------------------------------------------
        Control PKnob(string id, string label, Func<float, string> fmt, double size = NotaSize.KnobSecondary, double cellW = 54, IBrush? arc = null)
        {
            if (!idx.TryGetValue(id, out var pi)) return new Panel();
            var value = new TextBlock { Text = fmt(G(id)), Foreground = TextPrimary };
            var knob = new Knob(G(id), 1.0)
            {
                Accent = true, ArcColor = arc, Default = engine.InstrumentParamDefault(track, pi),
                Width = size, Height = size,
            };
            knob.ValueChanged += v => { engine.PluginParamSet(track, -1, pi, (float)v); value.Text = fmt((float)v); Refresh(); };
            knob.GestureBegin += () => Begin(id);
            knob.GestureEnd += () => End(id);
            ctx.AddInstFader(pi, knob, value, fmt);
            MidiLearn.Bind(knob, MidiTarget.PluginParam(track, -1, pi), engine.PluginParamName(track, -1, pi));
            var cell = KnobCell(label, knob, value, cellW);
            cell.VerticalAlignment = VerticalAlignment.Center;
            return cell;
        }

        Border Chips(string id, string[] names, double padX = 5, double fontSize = 8, Func<bool>? dim = null)
        {
            int n = names.Length;
            var seg = Segments(names, () => Sel(id, n), iv => { Write(id, iv / (float)(n - 1)); Refresh(); }, out var sync,
                dim: dim, padX: padX, fontSize: fontSize);
            readouts.Add(sync);
            if (I(id) is var pi and >= 0) MidiLearn.Bind(seg, MidiTarget.PluginParam(track, -1, pi), engine.PluginParamName(track, -1, pi));
            return seg;
        }

        Grid Slider(string label, string id, Func<string> text, bool bipolar = false, bool modulation = false,
            double labelWidth = 30, double valueWidth = 26)
        {
            int pi = I(id);
            var row = SliderRow(label, () => G(id), v => { SetP(id, (float)v); Refresh(); }, text, out var sync,
                begin: pi >= 0 ? () => Begin(id) : null,
                end: pi >= 0 ? () => End(id) : null,
                reset: pi >= 0 ? () => { Write(id, engine.InstrumentParamDefault(track, pi)); Refresh(); } : null,
                bipolar: bipolar, labelWidth: labelWidth, valueWidth: valueWidth, modulation: modulation);
            row.ColumnSpacing = 5;
            readouts.Add(sync);
            if (pi >= 0) MidiLearn.Bind(row, MidiTarget.PluginParam(track, -1, pi), engine.PluginParamName(track, -1, pi));
            return row;
        }

        // A rail button: raised at rest; a latching one turns brass-washed while on.
        Border RailButton(string text, Action click, Func<bool>? on = null, string? id = null, double height = 16)
        {
            var tb = new TextBlock
            {
                Text = text, FontSize = NotaType.KnobLabel, FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            var b = new Border
            {
                Height = height, CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand), Child = tb,
            };
            void Paint()
            {
                bool lit = on?.Invoke() ?? false;
                b.Background = lit ? NotaPalette.AccentSubtle : NotaPalette.SurfaceRaised;
                b.BorderBrush = lit ? NotaPalette.BorderBrass : BorderDef;
                tb.Foreground = lit ? AccentBright : NotaPalette.TextStrong;
            }
            b.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return;
                click(); Paint(); Refresh(); e.Handled = true;
            };
            readouts.Add(Paint);
            Paint();
            if (id is not null && I(id) is var pi and >= 0) MidiLearn.Bind(b, MidiTarget.PluginParam(track, -1, pi), engine.PluginParamName(track, -1, pi));
            return b;
        }

        static Grid Row(params (Control C, string W)[] cols)
        {
            var g = new Grid { ColumnSpacing = 4 };
            for (int i = 0; i < cols.Length; i++)
            {
                g.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Parse(cols[i].W)));
                Grid.SetColumn(cols[i].C, i); g.Children.Add(cols[i].C);
            }
            return g;
        }

        // ---- Balls tab ----------------------------------------------------------------
        var syncSeg = Chips("sync", new[] { "Free", "Sync" }, padX: 5, fontSize: 7);
        ToolTip.SetTip(syncSeg, "Sync: a swing takes a note division of the tempo · Free: it takes a time in seconds");
        var divSeg = Chips("division", PendulumModel.DivNames, padX: 4, fontSize: 7);
        ToolTip.SetTip(divSeg, "How long one swing (low → high → low) takes at +100\u2009%");
        var timeRow = Slider("TIME", "freerate", () => Secs(PendulumModel.FreeSeconds(G("freerate"))), labelWidth: 0, valueWidth: 32);
        timeRow.Width = 100;
        ToolTip.SetTip(timeRow, "Free: seconds per swing at +100\u2009%");
        var cycleHost = new Panel { VerticalAlignment = VerticalAlignment.Center, Children = { divSeg, timeRow } };
        readouts.Add(() => { divSeg.IsVisible = Synced(); timeRow.IsVisible = !Synced(); });
        var rateRow = Slider("RATE", "rate", () => NotaNum.Unit(PendulumModel.RateSigned(G("rate")) * 100, "+0;−0;0", "%"), bipolar: true, labelWidth: 0, valueWidth: 28);
        ToolTip.SetTip(rateRow, "Swing speed — centre stops the balls, left of centre runs them in reverse");
        var motionSeg = Chips("motion", PendulumModel.MotionNames, padX: 4, fontSize: 7);
        ToolTip.SetTip(motionSeg, "The swing curve — Pendulum slows at the walls, Linear keeps one speed, Bounce settles with a ripple");
        var ballStrip = Row((syncSeg, "Auto"), (cycleHost, "Auto"), (rateRow, "*"), (Cap("MOTION"), "Auto"), (motionSeg, "Auto"));
        ballStrip.ColumnSpacing = 7;
        ballStrip.Margin = new Thickness(7, 0);

        var ballsView = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 4, Margin = new Thickness(7, 5) };
        ballsView.Children.Add(lanes);
        Grid.SetRow(bar, 1); ballsView.Children.Add(bar);
        var ballsBody = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        ballsBody.Children.Add(StripBox(ballStrip));
        Grid.SetRow(ballsView, 1); ballsBody.Children.Add(ballsView);

        // ---- Voice tab ----------------------------------------------------------------
        var waveSeg = Chips("wave", PendulumModel.WaveNames, padX: 6, fontSize: 7);
        ToolTip.SetTip(waveSeg, "The voice: soft Keys, bright Glass, Saw, hollow Square or inharmonic Bell");
        var voiceStrip = Row((waveSeg, "Auto"), (new Panel(), "*"), (lastNote, "Auto"), (voiceBars, "Auto"));
        voiceStrip.ColumnSpacing = 7;
        voiceStrip.Margin = new Thickness(7, 0);

        var toneRow = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 22, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                PKnob("tone", "TONE", PendulumModel.ToneWord),
                PKnob("bright", "BRIGHT", v => NotaNum.Pct(v)),
                PKnob("fm", "FM", v => NotaNum.Pct(v), arc: Teal),
                PKnob("notelen", "LENGTH", v => Secs(PendulumModel.NoteSeconds(v))),
            },
        };
        var envRow = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                PKnob("attack", "ATTACK", v => Secs(PendulumModel.AttackSeconds(v))),
                PKnob("decay", "DECAY", v => Secs(PendulumModel.DecaySeconds(v))),
                PKnob("release", "RELEASE", v => Secs(PendulumModel.ReleaseSeconds(v))),
                PKnob("volume", "VOLUME", DbOf),
            },
        };
        var toneHost = new Border { BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 0, 0, 1), Child = toneRow };
        var voiceView = new Grid { RowDefinitions = new RowDefinitions("*,*"), Margin = new Thickness(8, 0, 8, 4) };
        voiceView.Children.Add(toneHost);
        Grid.SetRow(envRow, 1); voiceView.Children.Add(envRow);
        var voiceBody = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        voiceBody.Children.Add(StripBox(voiceStrip));
        Grid.SetRow(voiceView, 1); voiceBody.Children.Add(voiceView);

        // ---- the tab strip ------------------------------------------------------------
        var bodies = new Control[] { ballsBody, voiceBody };
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

        var tabHost = new Panel();
        foreach (var b in bodies) tabHost.Children.Add(b);
        var tabGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        tabGrid.Children.Add(HeaderStrip(tabStrip));
        Grid.SetRow(tabHost, 1); tabGrid.Children.Add(tabHost);
        var tabPanel = SectionBox(tabGrid);

        // ---- rail · the balls ---------------------------------------------------------
        var railCap = Cap("BALLS"); railCap.LetterSpacing = NotaType.SectionLabelTracking; railCap.Margin = new Thickness(8, 0, 0, 0);
        var railSub = Mono("rate · phase · note", TextTertiary); railSub.HorizontalAlignment = HorizontalAlignment.Right; railSub.Margin = new Thickness(0, 0, 8, 0);
        var railHead = new Grid { Height = TabH, ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        railHead.Children.Add(railCap);
        Grid.SetColumn(railSub, 1); railHead.Children.Add(railSub);

        // Balls-tab foot: count stepper, sort + quantize, Hold / First note / Reset.
        var countText = Mono("", TextPrimary);
        readouts.Add(() => countText.Text = Count().ToString(NotaNum.Culture));
        void StepBalls(int d) => Write("balls", PendulumModel.BallCountNorm(Count() + d));
        Border StepBtn(GlyphKind g, int d, string tip)
        {
            var b = new Border
            {
                Width = 16, Height = 14, CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1),
                BorderBrush = BorderDef, Background = NotaPalette.SurfaceRaised, Cursor = new Cursor(StandardCursorType.Hand),
                Child = new Glyph(g, 7) { Foreground = NotaPalette.TextStrong, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            ToolTip.SetTip(b, tip);
            b.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return;
                StepBalls(d); Refresh(); e.Handled = true;
            };
            return b;
        }
        var steppers = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Children = { StepBtn(GlyphKind.Minus, -1, "One ball fewer (2 at least)"), StepBtn(GlyphKind.Plus, +1, "One ball more (6 at most)") } };
        var countRow = Row((Cap("BALLS"), "Auto"), (countText, "Auto"), (new Panel(), "*"), (steppers, "Auto"));
        countRow.ColumnSpacing = 6;
        if (I("balls") is var bi and >= 0) MidiLearn.Bind(countRow, MidiTarget.PluginParam(track, -1, bi), engine.PluginParamName(track, -1, bi));

        var sortSeg = Chips("chordsort", new[] { "Up", "Down" }, padX: 4, fontSize: 7);
        ToolTip.SetTip(sortSeg, "Up: the low wall plays the chord's lowest note · Down: its highest");
        var quantSeg = Chips("quantize", PendulumModel.QuantNames, padX: 3, fontSize: 7);
        ToolTip.SetTip(quantSeg, "Quantize: a crossing waits for the next 1/16 or 1/8 before it plays");
        var sortRow = Row((Cap("SORT"), "Auto"), (sortSeg, "Auto"), (new Panel(), "*"), (Cap("QUANT"), "Auto"), (quantSeg, "Auto"));

        var holdBtn = RailButton("Hold", () => Write("hold", G("hold") >= 0.5f ? 0 : 1), () => G("hold") >= 0.5f, "hold");
        ToolTip.SetTip(holdBtn, "Latch the chord — it keeps swinging after you let go; the next chord replaces it");
        var firstBtn = RailButton("First note", () => Write("firstnote", G("firstnote") >= 0.5f ? 0 : 1), () => G("firstnote") >= 0.5f, "firstnote");
        ToolTip.SetTip(firstBtn, "Restart every ball from its start when a new chord begins");
        var resetBtn = RailButton("Reset", () => SetP("reset", 1), id: "reset");
        ToolTip.SetTip(resetBtn, "Restart every ball's swing now");
        var actionRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 4 };
        actionRow.Children.Add(holdBtn);
        Grid.SetColumn(firstBtn, 1); actionRow.Children.Add(firstBtn);
        Grid.SetColumn(resetBtn, 2); actionRow.Children.Add(resetBtn);

        var ballsFoot = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), RowSpacing = 4 };
        ballsFoot.Children.Add(countRow);
        Grid.SetRow(sortRow, 2); ballsFoot.Children.Add(sortRow);
        Grid.SetRow(actionRow, 3); ballsFoot.Children.Add(actionRow);

        // Voice-tab foot: spread, scale, Humanize / Reset.
        string Cents(float v) => NotaNum.Unit(PendulumModel.DetuneCents(v), "0", "c");
        var spreadBlock = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                Cap("SPREAD"),
                Slider("RATE", "spread", () => NotaNum.Pct(G("spread")), modulation: true, labelWidth: 40),
                Slider("DETUNE", "detune", () => Cents(G("detune")), modulation: true, labelWidth: 40),
                Slider("PAN", "panspread", () => NotaNum.Pct(G("panspread")), modulation: true, labelWidth: 40),
                Slider("HUMAN", "humanize", () => NotaNum.Pct(G("humanize")), modulation: true, labelWidth: 40),
            },
        };
        ToolTip.SetTip(spreadBlock.Children[1], "How far the balls' rates drift apart — each ball a little faster than the one before");
        ToolTip.SetTip(spreadBlock.Children[2], "Detune of the chorus partner inside each voice");
        ToolTip.SetTip(spreadBlock.Children[3], "Fan the notes across the stereo field by where the ball is");
        ToolTip.SetTip(spreadBlock.Children[4], "Level and pitch jitter per note");
        var spreadHost = new Border { BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 3, 0, 0), Child = spreadBlock };

        var scaleText = Mono("", TextPrimary);
        readouts.Add(() => scaleText.Text = PendulumModel.ScaleText(G("root"), G("scalemode")));
        var scaleBox = new Border
        {
            Height = 14, CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), BorderBrush = BorderDef,
            Background = NotaPalette.BgSunken, Padding = new Thickness(6, 0), Cursor = new Cursor(StandardCursorType.Hand),
            Child = new DockPanel
            {
                Children =
                {
                    new Glyph(GlyphKind.ChevronDown, 7) { Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center, [DockPanel.DockProperty] = Dock.Right },
                    scaleText,
                },
            },
        };
        ToolTip.SetTip(scaleBox, "Snap the generated notes to a scale");
        scaleBox.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(scaleBox).Properties.IsLeftButtonPressed) return;
            var fly = new MenuFlyout();
            int cur = Sel("scalemode", PendulumModel.ModeNames.Length), curRoot = Sel("root", 12);
            for (int m = 0; m < PendulumModel.ModeNames.Length; m++)
            {
                int mv = m;
                var mi = new MenuItem { Header = PendulumModel.ModeNames[m] };
                if (m == cur) mi.Icon = new Avalonia.Controls.Shapes.Ellipse { Width = 6, Height = 6, Fill = Brass };
                mi.Click += (_, _) => { Write("scalemode", mv / (float)(PendulumModel.ModeNames.Length - 1)); Refresh(); };
                fly.Items.Add(mi);
            }
            fly.Items.Add(new Separator());
            var rootMenu = new MenuItem { Header = $"Root · {PendulumModel.RootNames[curRoot]}" };
            for (int r = 0; r < 12; r++)
            {
                int rv = r;
                var ri = new MenuItem { Header = PendulumModel.RootNames[r] };
                if (r == curRoot) ri.Icon = new Avalonia.Controls.Shapes.Ellipse { Width = 6, Height = 6, Fill = Brass };
                ri.Click += (_, _) => { Write("root", rv / 11f); Refresh(); };
                rootMenu.Items.Add(ri);
            }
            fly.Items.Add(rootMenu);
            fly.ShowAt(scaleBox);
            e.Handled = true;
        };
        scaleBox.PointerWheelChanged += (_, e) =>
        {
            int n = PendulumModel.ModeNames.Length;
            int m = Math.Clamp(Sel("scalemode", n) + (e.Delta.Y < 0 ? 1 : -1), 0, n - 1);
            Write("scalemode", m / (float)(n - 1)); Refresh(); e.Handled = true;
        };
        if (I("scalemode") is var smi and >= 0) MidiLearn.Bind(scaleBox, MidiTarget.PluginParam(track, -1, smi), engine.PluginParamName(track, -1, smi));
        var scaleRow = Row((Cap("SCALE"), "Auto"), (scaleBox, "*"));
        scaleRow.ColumnSpacing = 5;

        float lastHuman = G("humanize") > 0.005f ? G("humanize") : 0.3f;
        var humanBtn = RailButton("Humanize", () =>
        {
            if (G("humanize") > 0.005f) { lastHuman = G("humanize"); Write("humanize", 0); }
            else Write("humanize", lastHuman);
        }, () => G("humanize") > 0.005f, "humanize", 14);
        ToolTip.SetTip(humanBtn, "Humanize on / off — the amount is the HUMAN slider");
        var spreadResetBtn = RailButton("Reset", () =>
        {
            foreach (var id in new[] { "spread", "detune", "panspread", "humanize", "root", "scalemode" }) Write(id, Def(id));
        }, height: 14);
        ToolTip.SetTip(spreadResetBtn, "Reset spread, humanize and scale to their defaults");
        var voiceBtns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 4 };
        voiceBtns.Children.Add(humanBtn);
        Grid.SetColumn(spreadResetBtn, 1); voiceBtns.Children.Add(spreadResetBtn);

        var voiceFoot = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), RowSpacing = 3 };
        voiceFoot.Children.Add(spreadHost);
        Grid.SetRow(scaleRow, 2); voiceFoot.Children.Add(scaleRow);
        Grid.SetRow(voiceBtns, 3); voiceFoot.Children.Add(voiceBtns);

        // The list keeps its natural height (rows of 17 / 16) while it fits; otherwise it
        // takes what the foot leaves and the rows tighten.
        var feet = new Panel { Children = { ballsFoot, voiceFoot } };
        var railBody = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 4, Margin = new Thickness(8, 5, 8, 5) };
        railBody.Children.Add(ballList);
        Grid.SetRow(feet, 1); railBody.Children.Add(feet);
        (int, int, double, double) fitKey = default;
        void FitList()
        {
            var key = (tab, Count(), railBody.Bounds.Width, railBody.Bounds.Height);
            if (key == fitKey) return;
            fitKey = key;
            bool balls = tab == 0;
            ballList.Gap = balls ? 4 : 3;
            ballList.MaxRow = balls ? 17 : 16;
            int n = Count();
            double natural = n * ballList.MaxRow + (n - 1) * ballList.Gap;
            double avail = railBody.Bounds.Height;
            if (avail <= 0) { ballList.Height = natural; return; }
            Control foot = balls ? ballsFoot : voiceFoot;
            foot.Measure(new Size(railBody.Bounds.Width, double.PositiveInfinity));
            double footMin = foot.DesiredSize.Height;
            ballList.Height = Math.Max(n * 7, Math.Min(natural, avail - footMin - railBody.RowSpacing));
        }
        readouts.Add(FitList);
        railBody.SizeChanged += (_, _) => FitList();

        var railGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        railGrid.Children.Add(HeaderStrip(railHead));
        Grid.SetRow(railBody, 1); railGrid.Children.Add(railBody);
        var rail = SectionBox(railGrid);
        rail.Width = RailW;

        // ---- words: the tab hint and the status line -----------------------------------
        string Cycle() => Synced() ? "sync " + PendulumModel.DivNames[Sel("division", 5)] : "free " + Secs(PendulumModel.FreeSeconds(G("freerate")));
        string RateText() => NotaNum.Unit(PendulumModel.RateSigned(G("rate")) * 100, "+0;−0;0", "%");
        string MotionWord() => PendulumModel.MotionNames[Sel("motion", 4)].ToLowerInvariant();
        string Hint() => tab == 0
            ? $"{Cycle()} · {MotionWord()} · {RateText()}"
            : $"{PendulumModel.WaveNames[Sel("wave", 5)].ToLowerInvariant()} · {PendulumModel.ToneWord(G("tone"))} · {Secs(PendulumModel.AttackSeconds(G("attack")))}";
        string Summary()
        {
            if (tab == 0)
            {
                int n = Count();
                var notes = new List<string>();
                for (int b = 0; b < n; b++) notes.Add(PendulumModel.NoteName(snap.BallState[b].Pitch));
                string chord = held.Length == 0 ? "hold a chord" : string.Join(" ", notes);
                string s = $"{(Synced() ? "Sync " + PendulumModel.DivNames[Sel("division", 5)] : "Free " + Secs(PendulumModel.FreeSeconds(G("freerate"))))} · {MotionWord()} · rate {RateText()}"
                    + $" · {n} balls → {chord} · sort {(G("chordsort") >= 0.5f ? "down" : "up")} · quantize {PendulumModel.QuantNames[Sel("quantize", 3)].ToLowerInvariant()}";
                if (G("hold") >= 0.5f) s += " · hold";
                return s;
            }
            string sc = PendulumModel.ScaleText(G("root"), G("scalemode"));
            return $"{PendulumModel.WaveNames[Sel("wave", 5)]} · {PendulumModel.ToneWord(G("tone"))} · "
                + $"{NotaNum.Str(PendulumModel.AttackSeconds(G("attack")) * 1000, "0")} / {NotaNum.Str(PendulumModel.DecaySeconds(G("decay")) * 1000, "0")} / "
                + $"{NotaNum.Str(PendulumModel.ReleaseSeconds(G("release")) * 1000, "0")} ms · {DbOf(G("volume"))} · spread rate {NotaNum.Pct(G("spread"))} · "
                + $"detune {Cents(G("detune"))} · pan {NotaNum.Pct(G("panspread"))}"
                + (G("humanize") > 0.005f ? $" · humanize {NotaNum.Pct(G("humanize"))}" : "")
                + (sc == "Off" ? "" : $" · {sc}");
        }

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
            ballsFoot.IsVisible = tab == 0;
            voiceFoot.IsVisible = tab == 1;
            Refresh();
        };

        // ---- status strip -------------------------------------------------------------
        var statusBar = new Grid { Height = StatusH, ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        statusBar.Children.Add(status);
        Grid.SetColumn(meta, 1); statusBar.Children.Add(meta);
        var statusHost = new Border
        {
            Height = StatusH, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 1, 0, 0),
            Background = NotaPalette.SurfaceAbyss, Padding = new Thickness(8, 0), Child = statusBar,
        };

        // ---- assembly -----------------------------------------------------------------
        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = NotaSpace.DeviceGap,
            Margin = new Thickness(NotaSpace.DeviceGap),
        };
        body.Children.Add(tabPanel);
        Grid.SetColumn(rail, 1); body.Children.Add(rail);

        DockPanel.SetDock(statusHost, Dock.Bottom);
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.Gutter, Children = { statusHost, body } };

        ctx.AddDeviceRefresher(Live);
        ctx.SetInstLiveViz(Refresh);
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

    // A tab's control strip under the tab row: 22 tall, a fainter hairline under it.
    private static Border StripBox(Control child) => new()
    {
        Height = StripH, BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 0, 0, 1), Child = child,
    };
}
