// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Strata (kind 15) body, mockup 3n on the shared
// shell (700×260, full-bleed), in pressed-in sections. A transport section (big
// bar.beat counter · a beat tick strip that fills as the loop runs · the four
// transport actions, each captioned with what it will do next) sits above the layer
// stack (per pass: index, name, its own waveform, Play/Muted/Rec state, level and
// mute) | a Loop rail (Feedback · Input gain · Speed · Quantize · count-in /
// set-tempo / reverse switches · Undo / Export / Clear). Transport + editing go
// through the device command channel; state and per-layer waveforms come from the
// device scope; Export mixes the recorded layers from the device state blob.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Nota.Application;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed class StrataDeviceBody : IDeviceBody
{
    private const int Feedback = 0, InputGain = 1, Speed = 2, Quantize = 3, CountIn = 4, SetTempo = 5, Reverse = 6;
    // deviceAction ids (mirror Strata.h).
    private const int C_Record = 0, C_Overdub = 1, C_Play = 2, C_Stop = 3, C_Undo = 4, C_Clear = 5, A_LayerMute = 6, A_LayerGain = 7;
    private const int M_Empty = 0, M_Rec = 1, M_Play = 2, M_Over = 3, M_Stop = 4;
    private const int MaxLayers = 8;
    // Scope tail, after the layer table (mirror Strata::scopeRead).
    private const int S_CountIn = 6 + MaxLayers * 3, S_LoopBpm = S_CountIn + 1, S_BeatsPerBar = S_CountIn + 2, S_Pending = S_CountIn + 3;
    private const double MinDb = -24, MaxDb = 12;

    public double Width => 700;

    public string? Subtitle => "Looper";   // the processing type, shown as the header badge
    public bool FullBleed => true;

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine; int track = ctx.TrackId, di = index;
        float P(int p) => engine.DeviceGetParam(track, di, p);
        float Mn(int p) => engine.DeviceParamMin(track, di, p);
        float Mx(int p) => engine.DeviceParamMax(track, di, p);
        void SetR(int p, float v) => engine.DeviceSetParam(track, di, p, v);
        void Act(int id, int iarg = 0, float farg = 0) => engine.DeviceAction(track, di, id, iarg, farg);
        bool On(int p) => P(p) > 0.5f;

        var readouts = new List<Action>();
        var scope = new float[S_Pending + 1];
        int scN = 0;
        float Sc(int i) => i < scN ? scope[i] : 0f;
        var waveBuf = new float[96];

        static TextBlock Caps(string t, IBrush? c = null) => new()
        { Text = NotaText.Label(t), FontSize = NotaType.KnobLabel, FontWeight = FontWeight.Medium, Foreground = c ?? TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock Mono(string t, double fs, IBrush c)
        { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.UI"); return tb; }
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static bool Left(PointerPressedEventArgs e, Control c) => e.GetCurrentPoint(c).Properties.IsLeftButtonPressed;

        // ---------- transport ----------
        var barNum = Mono("–", 13, TextPrimary);
        barNum.FontWeight = FontWeight.SemiBold; barNum.MinWidth = 34;
        var tickRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var ticks = new List<Border>();
        int tickBeats = -1;   // beats per tick group the strip was built for (1 = a tick per beat, else per bar)
        var ofBars = new TextBlock { FontSize = NotaType.DeviceCaption, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        void BuildTicks(int count, int every)
        {
            tickRow.Children.Clear(); ticks.Clear();
            for (int i = 0; i < count; i++)
            {
                var t = new Border { Width = i % every == 0 ? 2 : 1, Height = 14, CornerRadius = NotaRadius.Fine };
                ticks.Add(t); tickRow.Children.Add(t);
            }
        }

        // The four transport actions. Neutral at rest; the running one lifts in the accent
        // (Record in the record red), a queued one waits outlined. Each is captioned with what it does next.
        var actNames = new[] { "Record", "Overdub", "Play", "Stop" };
        var actCmds = new[] { C_Record, C_Overdub, C_Play, C_Stop };
        var actTips = new[]
        {
            "Record — start the loop; on the first pass, close it; then record a new layer from the loop's top",
            "Overdub — record a new layer from the next quantize step (on the first pass: close the loop and record layer 2)",
            "Play — play every layer (on the first pass: close the loop)",
            "Stop — stop at the next quantize step and return to the loop's top",
        };
        var actBtn = new Border[4]; var actSub = new TextBlock[4]; var actName = new TextBlock[4];
        var actRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < 4; i++)
        {
            int iv = i;
            actName[i] = new TextBlock { Text = actNames[i], FontSize = 9, FontWeight = FontWeight.SemiBold };
            actSub[i] = new TextBlock { FontSize = 7, Foreground = TextTertiary };
            actBtn[i] = new Border { Height = 26, MinWidth = 76, CornerRadius = NotaRadius.Cell, BorderThickness = new Thickness(1), Padding = new Thickness(8, 0), Cursor = new Cursor(StandardCursorType.Hand),
                Child = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { actName[i], actSub[i] } } };
            actBtn[i].PointerPressed += (_, e) => { if (!Left(e, actBtn[iv])) return; Act(actCmds[iv]); e.Handled = true; };
            ToolTip.SetTip(actBtn[i], actTips[i]);
            actRow.Children.Add(actBtn[i]);
        }
        var transport = new RuleSection { Height = 40, Padding = new Thickness(10, 0, 6, 0),
            Child = new DockPanel { LastChildFill = false, VerticalAlignment = VerticalAlignment.Center, Children = {
                Docked(actRow, Dock.Right),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { barNum, tickRow, ofBars } } } } };

        // ---------- layer stack ----------
        var layerHint = new TextBlock { Text = "Press Record to lay down the first loop", FontSize = 9, Foreground = TextTertiary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var layerRows = new Border[MaxLayers]; var waves = new StrataWave[MaxLayers];
        var stateTxts = new TextBlock[MaxLayers]; var muteBtns = new Border[MaxLayers];
        var layerStack = new StackPanel { Spacing = 3 };
        var countLbl = new TextBlock { FontSize = NotaType.DeviceCaption, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < MaxLayers; i++)
        {
            int iv = i;
            var idx = Mono($"{i + 1}", 9, TextTertiary); idx.Width = 8;
            var nameTxt = new TextBlock { Text = $"Layer {i + 1}", FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary, Width = 46, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            waves[i] = new StrataWave { VerticalAlignment = VerticalAlignment.Stretch, Margin = new Thickness(0, 3) };
            stateTxts[i] = new TextBlock { FontSize = 8, Width = 36, VerticalAlignment = VerticalAlignment.Center };
            // The layer's level: a value bar (drag, double-click resets to 0 dB, double-click the value to type one).
            double NormOf(double db) => (Math.Clamp(db, MinDb, MaxDb) - MinDb) / (MaxDb - MinDb);
            var level = SliderRow("", () => NormOf(Sc(7 + iv * 3)), n => Act(A_LayerGain, iv, (float)(MinDb + n * (MaxDb - MinDb))),
                () => DbText(Sc(7 + iv * 3)), out var levelSync, reset: () => Act(A_LayerGain, iv, 0f), trackWidth: 52, valueWidth: 34,
                textAt: n => DbText(MinDb + n * (MaxDb - MinDb)));
            ToolTip.SetTip(level, $"Layer {i + 1} level");
            readouts.Add(levelSync);
            var mtxt = new TextBlock { Text = "M", FontSize = 8, FontWeight = FontWeight.Medium, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            muteBtns[i] = new Border { Width = 16, Height = 14, CornerRadius = NotaRadius.Cell, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = mtxt };
            muteBtns[i].PointerPressed += (_, e) =>
            {
                if (!Left(e, muteBtns[iv])) return;
                Act(A_LayerMute, iv, Sc(8 + iv * 3) > 0.5f ? 0f : 1f);
                e.Handled = true;
            };
            ToolTip.SetTip(muteBtns[i], $"Mute layer {i + 1}");
            var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center, Children = { idx, nameTxt } };
            var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { stateTxts[i], level, muteBtns[i] } };
            var row = new DockPanel { LastChildFill = true, Children = { Docked(left, Dock.Left), Docked(right, Dock.Right), new Border { Margin = new Thickness(8, 0), Child = waves[i] } } };
            layerRows[i] = new Border { Height = 28, CornerRadius = NotaRadius.Cell, BorderThickness = new Thickness(1), Padding = new Thickness(7, 0, 5, 0), IsVisible = false, Child = row };
            layerStack.Children.Add(layerRows[i]);
        }
        var stackHost = new Grid { Children = { layerStack, layerHint } };
        var layerHead = new DockPanel { LastChildFill = false, Height = 16, Children = { Docked(Caps("LAYERS"), Dock.Left), Docked(countLbl, Dock.Right) } };
        var layers = new RuleSection { Margin = new Thickness(0, 5, 5, 0), Padding = new Thickness(8, 5, 8, 7),
            Child = new DockPanel { LastChildFill = true, Children = { Docked(new Border { Margin = new Thickness(0, 0, 0, 4), Child = layerHead }, Dock.Top), stackHost } } };
        // Rows share the stack's height, so all eight layers fit.
        int shownRows = 0;
        stackHost.SizeChanged += (_, _) => FitRows();
        void FitRows()
        {
            int n = Math.Max(1, shownRows);
            double h = Math.Clamp((stackHost.Bounds.Height - (n - 1) * layerStack.Spacing) / n, 14, 30);
            foreach (var r in layerRows) r.Height = h;
        }

        // ---------- Loop rail ----------
        Control Row(int p, string name, Func<double, string> fmt, bool log)
        {
            double mn = Mn(p), mx = Mx(p);
            double lmn = log ? Math.Log(mn) : mn, lspan = (log ? Math.Log(mx) : mx) - lmn;
            double NormOf(double v) { double x = log ? Math.Log(Math.Clamp(v, mn, mx)) : Math.Clamp(v, mn, mx); return (x - lmn) / Math.Max(1e-9, lspan); }
            double ValOf(double n) { double x = lmn + Math.Clamp(n, 0, 1) * lspan; return log ? Math.Exp(x) : x; }
            float def = p switch { Feedback => 100f, Speed => 1f, _ => 0f };
            var row = SliderRow(name, () => NormOf(P(p)), n => SetR(p, (float)ValOf(n)), () => fmt(P(p)), out var sync,
                begin: () => engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, ""),
                end: () => engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, ""),
                reset: () => SetR(p, def), labelWidth: 54, valueWidth: 40, textAt: n => fmt(ValOf(n)));
            MidiLearn.Bind(row, MidiTarget.DeviceParam(track, di, p), name);
            readouts.Add(sync);
            return row;
        }
        Control Seg(int p, string[] names)
        {
            int n = names.Length;
            var seg = Segments(names, () => Math.Clamp((int)Math.Round(P(p)), 0, n - 1), iv => SetR(p, iv), out var sync);
            readouts.Add(sync);
            MidiLearn.Bind(seg, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            return seg;
        }
        Control Toggle(int p, string label, string tip)
        {
            var b = Switch(label, () => On(p), () => SetR(p, On(p) ? 0f : 1f), out var sync);
            MidiLearn.Bind(b, MidiTarget.DeviceParam(track, di, p), label);
            ToolTip.SetTip(b, tip);
            readouts.Add(sync); return b;
        }
        Control ActionBtn(string label, string tip, Action onClick, Func<bool> enabled)
        {
            var tb = new TextBlock { Text = label, FontSize = NotaType.DeviceBody, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var b = new Border { Height = 18, CornerRadius = NotaRadius.Cell, Background = NotaPalette.SurfaceHover, Cursor = new Cursor(StandardCursorType.Hand), Child = tb };
            b.BindResource(Border.BoxShadowProperty, "Shadow.Lifted");
            b.PointerPressed += (_, e) => { if (!Left(e, b) || !enabled()) return; onClick(); e.Handled = true; };
            ToolTip.SetTip(b, tip);
            readouts.Add(() => tb.Foreground = enabled() ? TextPrimary : TextDisabled);
            return b;
        }
        string PctF(double v) => NotaNum.F($"{v:0} %");
        string SpeedF(double v) => NotaNum.F($"{v:0.00}×");

        bool HasLayers() => (int)Sc(3) > 0;
        var undo = ActionBtn("Undo", "Undo — drop the last layer (or the one recording now)", () => Act(C_Undo), HasLayers);
        Border? root = null;
        var export = ActionBtn("Export", "Export — write the loop as you hear it (every unmuted layer at its level) to a WAV file", ExportWav, () => HasLayers() && Sc(S_LoopBpm) > 0);
        var clear = ActionBtn("Clear", "Clear — remove every layer", () => Act(C_Clear), HasLayers);
        var bottomActions = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 5 };
        Grid.SetColumn(export, 1); Grid.SetColumn(clear, 2);
        bottomActions.Children.Add(undo); bottomActions.Children.Add(export); bottomActions.Children.Add(clear);

        var qRow = new Grid { ColumnDefinitions = new ColumnDefinitions("54,Auto"), ColumnSpacing = 8 };
        var qLbl = new TextBlock { Text = "Quantize", FontSize = NotaType.RowLabel, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        var qSeg = Seg(Quantize, new[] { "Off", "Bar", "1/4" });
        Grid.SetColumn(qSeg, 1); qRow.Children.Add(qLbl); qRow.Children.Add(qSeg);

        var rail = new RuleSection { Width = 186, Margin = new Thickness(0, 5, 0, 0), Padding = new Thickness(8, 5, 8, 7),
            Child = new DockPanel { LastChildFill = false, Children = {
                Docked(bottomActions, Dock.Bottom),
                Docked(new StackPanel { Spacing = 6, Children = {
                    new Border { Height = 16, Child = Caps("LOOP") },
                    Row(Feedback, "Feedback", PctF, false),
                    Row(InputGain, "Input gain", DbText, false),
                    Row(Speed, "Speed", SpeedF, true),
                    qRow,
                    new StackPanel { Spacing = 5, Margin = new Thickness(0, 2, 0, 0), Children = {
                        Toggle(CountIn, "Count-in 1 bar", "Count-in — the first Record waits one bar before it starts"),
                        Toggle(SetTempo, "Set tempo from loop", "Set tempo — when the first loop closes, the song tempo changes so the loop is a whole number of bars"),
                        Toggle(Reverse, "Reverse", "Reverse — play the layers backwards") } } } }, Dock.Top) } } };
        DockPanel.SetDock(rail, Dock.Right);

        root = new Border { Background = NotaPalette.SurfaceCard,
            Child = new DockPanel { LastChildFill = true, Margin = new Thickness(5), Children = { Docked(transport, Dock.Top), rail, layers } } };

        // ---------- export ----------
        // The state blob: version, sample rate, length, bars, layer count, per layer (gain dB,
        // muted, state), then each layer's interleaved stereo PCM. Mixed as Strata plays it:
        // unmuted layers at their level, older ones sunk by Feedback.
        async void ExportWav()
        {
            var top = root is null ? null : TopLevel.GetTopLevel(root);
            if (top is null) return;
            var blob = engine.DeviceGetState(track, di);
            if (blob.Length < 20) return;
            int off = 0;
            int I() { int v = BitConverter.ToInt32(blob, off); off += 4; return v; }
            float F() { float v = BitConverter.ToSingle(blob, off); off += 4; return v; }
            I(); int sr = I(), len = I(); I(); int lc = Math.Clamp(I(), 0, MaxLayers);
            if (len <= 0 || lc <= 0 || blob.Length < 20 + lc * 12 + (long)lc * len * 8) return;
            var gain = new float[lc];
            float fb = Math.Clamp(P(Feedback) * 0.01f, 0f, 1f);
            for (int k = 0; k < lc; k++)
            {
                float db = F(); bool muted = I() != 0; I();
                gain[k] = muted ? 0f : MathF.Pow(10f, db / 20f) * MathF.Pow(fb, lc - 1 - k);
            }
            var mix = new float[len * 2];
            for (int k = 0; k < lc; k++)
            {
                for (int i = 0; i < len * 2; i++) mix[i] += F() * gain[k];
            }
            var file = await top.PickSaveFileAsync(new FilePickerSaveOptions
            {
                Title = "Export the Strata loop to WAV",
                DefaultExtension = "wav",
                SuggestedFileName = "Nota Strata loop.wav",
                FileTypeChoices = new[] { new FilePickerFileType("WAV audio") { Patterns = new[] { "*.wav" } } },
            });
            string? path = file?.TryGetLocalPath();
            if (path is null) return;
            using var w = new Nota.Infrastructure.WavWriter(path, sr > 0 ? sr : 44100, 2, WavBitDepth.Float32);
            w.WriteFrames(mix, len);
        }

        // ---------- live refresh ----------
        bool wasFirstPass = false;
        void Refresh()
        {
            scN = engine.DeviceScope(track, di, scope, scope.Length);
            if (scN < 6) return;
            int mode = (int)Sc(0), loopBars = Math.Max(0, (int)Sc(1)), lc = (int)Sc(3), recL = (int)Sc(4);
            float phase = Sc(2), recProg = Sc(5);
            int bpb = Math.Max(1, (int)Sc(S_BeatsPerBar)); if (scN <= S_BeatsPerBar) bpb = 4;
            int countIn = (int)Sc(S_CountIn), pending = scN > S_Pending ? (int)Sc(S_Pending) : -1;
            bool firstPass = mode == M_Rec && Sc(S_LoopBpm) <= 0;
            bool counting = firstPass && countIn > 0;
            bool closed = lc > 0 && !firstPass;
            int quant = Math.Clamp((int)Math.Round(P(Quantize)), 0, 2);

            // Set tempo from loop: when the first pass closes, the song takes the loop's tempo.
            if (wasFirstPass && closed && On(SetTempo) && Sc(S_LoopBpm) is var lb && lb >= 20 && lb <= 300)
                engine.SetBpm(Math.Round(lb, 2));
            wasFirstPass = firstPass;

            // Counter + beat ticks (a tick per beat, or per bar for a long loop).
            if (counting) barNum.Text = NotaNum.F($"−{countIn}");
            else if (closed && loopBars > 0)
            {
                int beatsTotal = loopBars * bpb;
                int beatPos = Math.Clamp((int)(phase * beatsTotal), 0, beatsTotal - 1);
                barNum.Text = NotaNum.F($"{beatPos / bpb + 1}.{beatPos % bpb + 1}");
            }
            else barNum.Text = "–";
            int count = closed && loopBars > 0 ? loopBars * bpb : 16, every = closed ? bpb : 4;
            if (count > 32) { count = Math.Min(32, loopBars); every = 4; }
            if (ticks.Count != count || tickBeats != every) { BuildTicks(count, every); tickBeats = every; }
            int lit = closed ? (int)Math.Floor(Math.Clamp(phase, 0, 0.9999) * count) + 1 : 0;
            tickRow.IsVisible = closed;   // no grid until the first loop closes
            bool rolling = mode is M_Rec or M_Over or M_Play;   // the progress is the playhead only while it runs
            for (int i = 0; i < ticks.Count; i++)
                ticks[i].Background = closed && rolling && i < lit ? Brass : NotaPalette.BorderStrong;
            ofBars.Text = counting ? "count-in" : firstPass ? "recording the first loop" : closed ? NotaNum.F($"of {loopBars} {(loopBars == 1 ? "bar" : "bars")}") : "";
            countLbl.Text = closed || firstPass ? NotaNum.F($"{lc} of {MaxLayers}") : "";

            // Captions say what each action does now; one that would do nothing dims.
            string when = quant switch { 0 => "now", 1 => "next bar", _ => "next beat" };
            bool full = lc >= MaxLayers, overdubbing = mode == M_Over;
            var subs = new string[4]; var can = new bool[4];
            if (counting) { subs[0] = subs[1] = subs[2] = ""; subs[3] = "cancel count-in"; can[3] = true; }
            else if (mode == M_Empty || lc == 0)
            {
                subs[0] = subs[1] = On(CountIn) ? "after a count-in" : "start the loop"; can[0] = can[1] = true;
                subs[2] = subs[3] = "";
            }
            else if (firstPass)
            {
                subs[0] = "close, then play"; subs[1] = "close, add layer 2"; subs[2] = "close, then play"; subs[3] = "close, then stop";
                can[0] = can[1] = can[2] = can[3] = true;
            }
            else
            {
                subs[0] = full ? "layers full" : overdubbing ? "recording" : NotaNum.F($"layer {lc + 1}, loop top");
                subs[1] = full ? "layers full" : overdubbing ? "recording" : NotaNum.F($"layer {lc + 1}, {when}");
                can[0] = can[1] = !full && !overdubbing;
                subs[2] = mode == M_Stop ? "from the top" : "all layers"; can[2] = mode != M_Play;
                subs[3] = mode == M_Stop ? "stopped" : when == "now" ? "now" : NotaNum.F($"at the {when}"); can[3] = mode != M_Stop;
            }
            int active = counting ? 0 : mode switch { M_Rec => 0, M_Over => 1, M_Play => 2, M_Stop => 3, _ => -1 };
            for (int i = 0; i < 4; i++)
            {
                bool on = i == active, waiting = pending == actCmds[i];
                var ink = i == 0 ? NotaPalette.Record : NotaPalette.Accent;
                actSub[i].Text = waiting ? "waiting…" : subs[i];
                actBtn[i].Background = on ? NotaPalette.Wash(ink, 0x24) : NotaPalette.SurfaceHover;
                actBtn[i].BorderBrush = on ? ink : waiting ? NotaPalette.BorderBrass : Brushes.Transparent;
                actName[i].Foreground = on ? (i == 0 ? NotaPalette.Record : AccentBright) : can[i] ? TextPrimary : TextDisabled;
            }

            // Layers: neutral greys, newest brightest; the recording one in the record red.
            layerHint.IsVisible = lc == 0;
            if (lc != shownRows) { shownRows = lc; FitRows(); }
            var grey = NotaPalette.VizInk.Color;
            for (int k = 0; k < MaxLayers; k++)
            {
                bool vis = k < lc;
                layerRows[k].IsVisible = vis;
                if (!vis) continue;
                bool muted = Sc(8 + k * 3) > 0.5f, rec = recL == k && !counting;
                stateTxts[k].Text = rec ? "Rec" : muted ? "Muted" : mode == M_Stop ? "Stopped" : "Play";
                stateTxts[k].Foreground = rec ? NotaPalette.Record : muted ? TextTertiary : TextSecondary;
                var mtxt = (TextBlock)muteBtns[k].Child!;
                muteBtns[k].Background = muted ? NotaPalette.Mute : Brushes.Transparent;   // mute on = the amber mute fill
                muteBtns[k].BorderBrush = muted ? NotaPalette.Mute : NotaPalette.BorderStrong;
                mtxt.Foreground = muted ? NotaPalette.InkOn(NotaPalette.Mute) : TextTertiary;
                layerRows[k].Background = rec ? NotaPalette.Wash(NotaPalette.Record, 0x14) : Sunken;
                layerRows[k].BorderBrush = rec ? NotaPalette.Record : NotaPalette.GraphBorder;
                int wn = engine.DeviceLayerWave(track, di, k, waveBuf, waveBuf.Length);
                var env = new float[Math.Max(1, wn)];
                for (int i = 0; i < wn; i++) env[i] = Math.Clamp(waveBuf[i], 0f, 1f);
                byte a = (byte)Math.Max(0x70, 0xE6 - (lc - 1 - k) * 0x18);   // older layers fade, as Feedback sinks them
                waves[k].Set(env, Color.FromArgb(a, grey.R, grey.G, grey.B), muted, rec, rec && !firstPass ? recProg : 1f);   // the first pass grows to fill the strip
            }
            foreach (var r in readouts) r();
        }
        ctx.AddDeviceRefresher(Refresh);
        Refresh();
        return root;
    }

    private static string DbText(double v) => NotaNum.F($"{v:+0.0;−0.0;0.0} dB");
}
