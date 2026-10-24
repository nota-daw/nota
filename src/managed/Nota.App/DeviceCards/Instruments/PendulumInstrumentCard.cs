// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the Nota Pendulum editor (instrument kind 8): arpeggios out of
// bounces, in an 806 × 260 card. Held notes form a chord; balls swing across it and every
// degree a ball crosses plays that note.
//
//   Centre      two tabs:
//               Balls — Free / Sync · the division (or the free swing time) · the Motion
//                       curve; under them the bipolar Rate as a value bar on its own row;
//                       then one lane per ball (x = pitch, the wall it came from, the wall it
//                       strikes, the time to the wall ahead) over the bar strip the
//                       generated notes fall into. The space says "why this note", the
//                       strip "when".
//               Voice — what the bounces sound with: the wave, tone / bright / FM / note
//                       length, the envelope and volume; the last note and a level bar
//                       per sounding voice.
//   Rail 250    the balls themselves (division · rate · phase · note), always visible;
//               under them the tab's own controls — count, sort, quantize, Hold / First
//               note / Reset on Balls; spread (rate, detune, pan, humanize), the scale and
//               Humanize / Reset on Voice.
//
// Everything live comes from the engine's telemetry (PendulumModel), so the lanes are the
// balls the audio thread is actually swinging. Orange marks values and the ball striking a
// wall; where a ball is going is grey. Every control is a plugin param → automation /
// persist / clone, and the card follows automation live.

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
    public string Subtitle => "Gen keys";
    public double CardWidth => 806;

    public string? VoiceLabel(IAudioEngine engine, int trackId, int active)
    {
        for (int i = 0, n = engine.PluginParamCount(trackId, -1); i < n; i++)
            if (engine.PluginParamId(trackId, -1, i) == "balls")
                return $"{PendulumModel.BallCount(engine.PluginParamGet(trackId, -1, i))} balls";
        return null;
    }

    private const double RailW = 250, BarH = 28;
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
        ToolTip.SetTip(lanes, "One lane per ball: x is pitch, low to high. Crossing a chord degree plays that note; the grey lead shows the wall ahead");
        var bar = new PendulumBarView { Height = BarH };
        ToolTip.SetTip(bar, "The bar: generated notes land on the 1/16 grid — this bar in neutral ink with the note just played in accent, the last bar dimmer ahead of the playhead");
        var ballList = new PendulumBallList();
        var voiceBars = new PendulumVoiceBars { VerticalAlignment = VerticalAlignment.Center };

        static TextBlock Cap(string t, IBrush? c = null) => new()
        {
            Text = NotaText.Label(t), FontSize = NotaType.KnobLabel,
            Foreground = c ?? TextTertiary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        static TextBlock Mono(string t, IBrush? c = null, double fs = NotaType.KnobValue) => new()
        {
            Text = t, FontSize = fs, Foreground = c ?? TextSecondary,
            VerticalAlignment = VerticalAlignment.Center, FontFamily = NotaFonts.UiFamily,
        };

        var hint = Mono("", TextTertiary, NotaType.Eyebrow);
        hint.HorizontalAlignment = HorizontalAlignment.Right;
        hint.TextTrimming = TextTrimming.CharacterEllipsis;
        var lastNote = Mono("", TextTertiary, NotaType.KnobLabel);
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
        }

        // ---- shared builders ----------------------------------------------------------
        Control PKnob(string id, string label, Func<float, string> fmt, double cellW = 54)
        {
            if (!idx.TryGetValue(id, out var pi)) return new Panel();
            var value = new TextBlock { Text = fmt(G(id)), Foreground = TextPrimary };
            var knob = new Knob(G(id), 1.0)
            {
                Accent = true, Default = engine.InstrumentParamDefault(track, pi),
            };
            knob.Format = v => fmt((float)v);
            knob.ValueChanged += v => { engine.PluginParamSet(track, -1, pi, (float)v); value.Text = fmt((float)v); Refresh(); };
            knob.GestureBegin += () => Begin(id);
            knob.GestureEnd += () => End(id);
            ctx.AddInstFader(pi, knob, value, fmt);
            MidiLearn.Bind(knob, MidiTarget.PluginParam(track, -1, pi), engine.PluginParamName(track, -1, pi));
            var cell = KnobCell(label, knob, value, cellW);
            cell.VerticalAlignment = VerticalAlignment.Center;
            return cell;
        }

        Border Chips(string id, string[] names, double padX = 6, double fontSize = NotaType.KnobValue, Func<bool>? dim = null)
        {
            int n = names.Length;
            var seg = Segments(names, () => Sel(id, n), iv => { Write(id, iv / (float)(n - 1)); Refresh(); }, out var sync,
                dim: dim, padX: padX, fontSize: fontSize);
            readouts.Add(sync);
            if (I(id) is var pi and >= 0) MidiLearn.Bind(seg, MidiTarget.PluginParam(track, -1, pi), engine.PluginParamName(track, -1, pi));
            return seg;
        }

        Grid Slider(string label, string id, Func<string> text, bool bipolar = false,
            double labelWidth = 40, double valueWidth = 40)
        {
            int pi = I(id);
            var row = SliderRow(label, () => G(id), v => { SetP(id, (float)v); Refresh(); }, text, out var sync,
                begin: pi >= 0 ? () => Begin(id) : null,
                end: pi >= 0 ? () => End(id) : null,
                reset: pi >= 0 ? () => { Write(id, engine.InstrumentParamDefault(track, pi)); Refresh(); } : null,
                bipolar: bipolar, labelWidth: labelWidth, valueWidth: valueWidth);
            readouts.Add(sync);
            if (pi >= 0) MidiLearn.Bind(row, MidiTarget.PluginParam(track, -1, pi), engine.PluginParamName(track, -1, pi));
            return row;
        }

        // A rail button: raised at rest; a latching one rises in the soft tint, its word in
        // orange, while on.
        Border RailButton(string text, Action click, Func<bool>? on = null, string? id = null)
        {
            var tb = new TextBlock
            {
                Text = text, FontSize = NotaType.Value,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            var b = new Border
            {
                Height = NotaSize.Dropdown, CornerRadius = NotaRadius.Field, Padding = new Thickness(8, 0),
                Cursor = new Cursor(StandardCursorType.Hand), Child = tb,
            };
            b.BindResource(Border.BoxShadowProperty, "Shadow.Lifted");
            void Paint()
            {
                bool lit = on?.Invoke() ?? false;
                b.Background = lit ? NotaPalette.AccentSoft : NotaPalette.SurfaceHover;
                tb.Foreground = lit ? AccentBright : TextPrimary;
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
        var syncSeg = Chips("sync", new[] { "Free", "Sync" });
        ToolTip.SetTip(syncSeg, "Sync: a swing takes a note division of the tempo · Free: it takes a time in seconds");
        var divSeg = Chips("division", PendulumModel.DivNames);
        ToolTip.SetTip(divSeg, "How long one swing (low → high → low) takes at +100\u2009%");
        var timeRow = Slider("Time", "freerate", () => Secs(PendulumModel.FreeSeconds(G("freerate"))), labelWidth: 0, valueWidth: 36);
        timeRow.Width = 150;
        ToolTip.SetTip(timeRow, "Free: seconds per swing at +100\u2009%");
        var cycleHost = new Panel { VerticalAlignment = VerticalAlignment.Center, Children = { divSeg, timeRow } };
        readouts.Add(() => { divSeg.IsVisible = Synced(); timeRow.IsVisible = !Synced(); });
        var rateRow = Slider("Rate", "rate", () => NotaNum.Unit(PendulumModel.RateSigned(G("rate")) * 100, "+0;−0;0", "%"), bipolar: true, labelWidth: 30, valueWidth: 44);
        ToolTip.SetTip(rateRow, "Swing speed — centre stops the balls, left of centre runs them in reverse");
        var motionSeg = Chips("motion", PendulumModel.MotionNames);
        ToolTip.SetTip(motionSeg, "The swing curve — Pendulum slows at the walls, Linear keeps one speed, Bounce settles with a ripple");
        var motionCap = Cap("Motion");
        var ballStrip = Row((syncSeg, "Auto"), (cycleHost, "Auto"), (new Panel(), "*"), (motionCap, "Auto"), (motionSeg, "Auto"));
        ballStrip.ColumnSpacing = 6;
        motionCap.Margin = new Thickness(0, 0, 2, 0);

        var ballsBody = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), Margin = new Thickness(0, 4, 0, 0) };
        rateRow.Margin = new Thickness(0, 2, 0, 0);
        lanes.Margin = new Thickness(0, 6, 0, 0);
        bar.Margin = new Thickness(0, 6, 0, 0);
        ballsBody.Children.Add(ballStrip);
        Grid.SetRow(rateRow, 1); ballsBody.Children.Add(rateRow);
        Grid.SetRow(lanes, 2); ballsBody.Children.Add(lanes);
        Grid.SetRow(bar, 3); ballsBody.Children.Add(bar);

        // ---- Voice tab ----------------------------------------------------------------
        var waveSeg = Chips("wave", PendulumModel.WaveNames);
        ToolTip.SetTip(waveSeg, "The voice: soft Keys, bright Glass, Saw, hollow Square or inharmonic Bell");
        var voiceStrip = Row((waveSeg, "Auto"), (new Panel(), "*"), (lastNote, "Auto"), (voiceBars, "Auto"));
        voiceStrip.ColumnSpacing = 8;

        var toneRow = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 30, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                PKnob("tone", "Tone", PendulumModel.ToneWord),
                PKnob("bright", "Bright", v => NotaNum.Pct(v)),
                PKnob("fm", "FM", v => NotaNum.Pct(v)),
                PKnob("notelen", "Length", v => Secs(PendulumModel.NoteSeconds(v))),
            },
        };
        var envRow = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 30, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                PKnob("attack", "Attack", v => Secs(PendulumModel.AttackSeconds(v))),
                PKnob("decay", "Decay", v => Secs(PendulumModel.DecaySeconds(v))),
                PKnob("release", "Release", v => Secs(PendulumModel.ReleaseSeconds(v))),
                PKnob("volume", "Volume", DbOf),
            },
        };
        var envHost = new Border { BorderBrush = NotaPalette.Rule, BorderThickness = new Thickness(0, 1, 0, 0), Child = envRow };
        var voiceBody = new Grid { RowDefinitions = new RowDefinitions("Auto,*,*"), Margin = new Thickness(0, 4, 0, 0) };
        voiceBody.Children.Add(voiceStrip);
        Grid.SetRow(toneRow, 1); voiceBody.Children.Add(toneRow);
        Grid.SetRow(envHost, 2); voiceBody.Children.Add(envHost);

        // ---- the tab strip ------------------------------------------------------------
        var bodies = new Control[] { ballsBody, voiceBody };
        Action syncTabs = () => { };
        var tabStrip = SynthInstrumentCard.TabStrip(TabNames, () => tab, i => { tab = i; showTab(); }, out var tabSync, hint);
        syncTabs = tabSync;

        var tabHost = new Panel();
        foreach (var b in bodies) tabHost.Children.Add(b);
        var tabGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        tabGrid.Children.Add(tabStrip);
        Grid.SetRow(tabHost, 1); tabGrid.Children.Add(tabHost);
        var tabPanel = SectionBox(tabGrid);

        // ---- rail · the balls ---------------------------------------------------------
        var railCap = new TextBlock { Text = "Balls", FontSize = NotaType.Body, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center };
        var railSub = Mono("rate · phase · note", TextTertiary, NotaType.Eyebrow); railSub.HorizontalAlignment = HorizontalAlignment.Right;
        var railHead = new Grid { Height = 20, ColumnDefinitions = new ColumnDefinitions("Auto,*") };
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
                Width = NotaSize.Dropdown, Height = NotaSize.Dropdown, CornerRadius = NotaRadius.Field,
                Background = NotaPalette.SurfaceHover, Cursor = new Cursor(StandardCursorType.Hand),
                Child = new Glyph(g, 9) { Foreground = TextPrimary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            b.BindResource(Border.BoxShadowProperty, "Shadow.Lifted");
            ToolTip.SetTip(b, tip);
            b.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return;
                StepBalls(d); Refresh(); e.Handled = true;
            };
            return b;
        }
        var steppers = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { StepBtn(GlyphKind.Minus, -1, "One ball fewer (2 at least)"), StepBtn(GlyphKind.Plus, +1, "One ball more (6 at most)") } };
        var countCap = Cap("Balls"); countCap.FontSize = NotaType.Body;
        var countRow = Row((countCap, "Auto"), (countText, "Auto"), (new Panel(), "*"), (steppers, "Auto"));
        countRow.ColumnSpacing = 8;
        if (I("balls") is var bi and >= 0) MidiLearn.Bind(countRow, MidiTarget.PluginParam(track, -1, bi), engine.PluginParamName(track, -1, bi));

        var sortSeg = Chips("chordsort", new[] { "Up", "Down" }, padX: 5);
        ToolTip.SetTip(sortSeg, "Up: the low wall plays the chord's lowest note · Down: its highest");
        var quantSeg = Chips("quantize", PendulumModel.QuantNames, padX: 5);
        ToolTip.SetTip(quantSeg, "Quantize: a crossing waits for the next 1/16 or 1/8 before it plays");
        var sortCap = Cap("Sort"); sortCap.FontSize = NotaType.Body;
        var quantCap = Cap("Quant"); quantCap.FontSize = NotaType.Body;
        var sortRow = Row((sortCap, "Auto"), (sortSeg, "Auto"), (new Panel(), "*"), (quantCap, "Auto"), (quantSeg, "Auto"));

        var holdBtn = RailButton("Hold", () => Write("hold", G("hold") >= 0.5f ? 0 : 1), () => G("hold") >= 0.5f, "hold");
        ToolTip.SetTip(holdBtn, "Latch the chord — it keeps swinging after you let go; the next chord replaces it");
        var firstBtn = RailButton("First note", () => Write("firstnote", G("firstnote") >= 0.5f ? 0 : 1), () => G("firstnote") >= 0.5f, "firstnote");
        ToolTip.SetTip(firstBtn, "Restart every ball from its start when a new chord begins");
        var resetBtn = RailButton("Reset", () => SetP("reset", 1), id: "reset");
        ToolTip.SetTip(resetBtn, "Restart every ball's swing now");
        var actionRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 6 };
        actionRow.Children.Add(holdBtn);
        Grid.SetColumn(firstBtn, 1); actionRow.Children.Add(firstBtn);
        Grid.SetColumn(resetBtn, 2); actionRow.Children.Add(resetBtn);

        var ballsFoot = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), RowSpacing = 5 };
        ballsFoot.Children.Add(countRow);
        Grid.SetRow(sortRow, 2); ballsFoot.Children.Add(sortRow);
        Grid.SetRow(actionRow, 3); ballsFoot.Children.Add(actionRow);

        // Voice-tab foot: spread, scale, Humanize / Reset.
        string Cents(float v) => NotaNum.Unit(PendulumModel.DetuneCents(v), "0", "c");
        var spreadBlock = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                Slider("Spread", "spread", () => NotaNum.Pct(G("spread")), labelWidth: 44),
                Slider("Detune", "detune", () => Cents(G("detune")), labelWidth: 44),
                Slider("Pan", "panspread", () => NotaNum.Pct(G("panspread")), labelWidth: 44),
                Slider("Human", "humanize", () => NotaNum.Pct(G("humanize")), labelWidth: 44),
            },
        };
        ToolTip.SetTip(spreadBlock.Children[0], "Spread: how far the balls' rates drift apart — each ball a little faster than the one before");
        ToolTip.SetTip(spreadBlock.Children[1], "Detune of the chorus partner inside each voice");
        ToolTip.SetTip(spreadBlock.Children[2], "Fan the notes across the stereo field by where the ball is");
        ToolTip.SetTip(spreadBlock.Children[3], "Level and pitch jitter per note");
        var spreadHost = new Border { BorderBrush = NotaPalette.Rule, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 5, 0, 0), Child = spreadBlock };

        var scaleText = Mono("", TextPrimary, NotaType.Value);
        scaleText.TextTrimming = TextTrimming.CharacterEllipsis;
        readouts.Add(() => scaleText.Text = PendulumModel.ScaleText(G("root"), G("scalemode")));
        var scaleBox = new Border
        {
            Height = NotaSize.Dropdown, CornerRadius = NotaRadius.Field,
            Background = NotaPalette.SurfaceHover, Padding = new Thickness(8, 0, 6, 0), Cursor = new Cursor(StandardCursorType.Hand),
            Child = new DockPanel
            {
                Children =
                {
                    new Glyph(GlyphKind.ChevronDown, 8) { Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0), [DockPanel.DockProperty] = Dock.Right },
                    scaleText,
                },
            },
        };
        scaleBox.BindResource(Border.BoxShadowProperty, "Shadow.Lifted");
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
                mi.ToggleType = MenuItemToggleType.Radio; mi.IsChecked = m == cur;
                mi.Click += (_, _) => { Write("scalemode", mv / (float)(PendulumModel.ModeNames.Length - 1)); Refresh(); };
                fly.Items.Add(mi);
            }
            fly.Items.Add(new Separator());
            var rootMenu = new MenuItem { Header = $"Root · {PendulumModel.RootNames[curRoot]}" };
            for (int r = 0; r < 12; r++)
            {
                int rv = r;
                var ri = new MenuItem { Header = PendulumModel.RootNames[r] };
                ri.ToggleType = MenuItemToggleType.Radio; ri.IsChecked = r == curRoot;
                ri.Click += (_, _) => { Write("root", rv / 11f); Refresh(); };
                rootMenu.Items.Add(ri);
            }
            fly.Items.Add(rootMenu);
            fly.ShowAt(scaleBox);
            e.Handled = true;
        };
        scaleBox.PointerWheelChanged += (_, e) =>
        {
            if (WheelInput.Step(e) is not { } s) return;
            e.Handled = true; if (s == 0) return;
            int n = PendulumModel.ModeNames.Length;
            int m = Math.Clamp(Sel("scalemode", n) - s, 0, n - 1);
            Write("scalemode", m / (float)(n - 1)); Refresh();
        };
        if (I("scalemode") is var smi and >= 0) MidiLearn.Bind(scaleBox, MidiTarget.PluginParam(track, -1, smi), engine.PluginParamName(track, -1, smi));

        float lastHuman = G("humanize") > 0.005f ? G("humanize") : 0.3f;
        var humanBtn = RailButton("Humanize", () =>
        {
            if (G("humanize") > 0.005f) { lastHuman = G("humanize"); Write("humanize", 0); }
            else Write("humanize", lastHuman);
        }, () => G("humanize") > 0.005f, "humanize");
        ToolTip.SetTip(humanBtn, "Humanize on / off — the amount is the Human value bar");
        var spreadResetBtn = RailButton("Reset", () =>
        {
            foreach (var id in new[] { "spread", "detune", "panspread", "humanize", "root", "scalemode" }) Write(id, Def(id));
        });
        ToolTip.SetTip(spreadResetBtn, "Reset spread, humanize and scale to their defaults");
        // The scale menu, Humanize and Reset share one row so the ball list keeps some room.
        var voiceBtns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 6 };
        voiceBtns.Children.Add(scaleBox);
        Grid.SetColumn(humanBtn, 1); voiceBtns.Children.Add(humanBtn);
        Grid.SetColumn(spreadResetBtn, 2); voiceBtns.Children.Add(spreadResetBtn);

        var voiceFoot = new Grid { RowDefinitions = new RowDefinitions("*,Auto,Auto"), RowSpacing = 5 };
        Grid.SetRow(spreadHost, 1); voiceFoot.Children.Add(spreadHost);
        Grid.SetRow(voiceBtns, 2); voiceFoot.Children.Add(voiceBtns);

        // The list keeps its natural height (rows of 17 / 16) while it fits; otherwise it
        // takes what the foot leaves and the rows tighten.
        var feet = new Panel { Children = { ballsFoot, voiceFoot } };
        var railBody = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 5, Margin = new Thickness(0, 2, 0, 0) };
        railBody.Children.Add(ballList);
        Grid.SetRow(feet, 1); railBody.Children.Add(feet);
        (int, int, double, double) fitKey = default;
        void FitList()
        {
            var key = (tab, Count(), railBody.Bounds.Width, railBody.Bounds.Height);
            if (key == fitKey) return;
            fitKey = key;
            bool balls = tab == 0;
            ballList.Gap = 3;
            ballList.MaxRow = 18;
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
        railGrid.Children.Add(railHead);
        Grid.SetRow(railBody, 1); railGrid.Children.Add(railBody);
        var rail = SectionBox(railGrid);
        rail.Width = RailW;
        tabPanel.Padding = rail.Padding = new Thickness(SectionPad);

        // ---- words: the tab hint ---------------------------------------------------------
        string Cycle() => Synced() ? "sync " + PendulumModel.DivNames[Sel("division", 5)] : "free " + Secs(PendulumModel.FreeSeconds(G("freerate")));
        string RateText() => NotaNum.Unit(PendulumModel.RateSigned(G("rate")) * 100, "+0;−0;0", "%");
        string MotionWord() => PendulumModel.MotionNames[Sel("motion", 4)].ToLowerInvariant();
        string Hint() => tab == 0
            ? $"{Cycle()} · {MotionWord()} · {RateText()}"
            : $"{PendulumModel.WaveNames[Sel("wave", 5)].ToLowerInvariant()} · {PendulumModel.ToneWord(G("tone"))} · {Secs(PendulumModel.AttackSeconds(G("attack")))}";
        showTab = () =>
        {
            syncTabs();
            for (int i = 0; i < TabNames.Length; i++) bodies[i].IsVisible = i == tab;
            ballsFoot.IsVisible = tab == 0;
            voiceFoot.IsVisible = tab == 1;
            Refresh();
        };

        // ---- assembly -----------------------------------------------------------------
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = SectionGap };
        root.Children.Add(tabPanel);
        Grid.SetColumn(rail, 1); root.Children.Add(rail);

        ctx.AddDeviceRefresher(Live);
        ctx.SetInstLiveViz(Refresh);
        showTab();
        Live();
        return root;
    }

    // A pressed-in section (style guide).
    private static Decorator SectionBox(Control child) => DeviceCardKit.Section(child);
}
