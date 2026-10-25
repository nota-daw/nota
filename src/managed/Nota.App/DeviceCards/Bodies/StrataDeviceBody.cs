// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Strata (kind 15) body, drawn to the style guide's picture
// of it (700 × 260, full-bleed). The Layers section is headed by the live strip (bar.beat
// counter · a cycle tick strip that fills as the loop runs · the four transport actions,
// each captioned with what it will do next) over the layer stack (per pass: index, name,
// its own waveform, Play / Muted / Rec state, level and mute; Undo / Export / Clear head it);
// beside it the Loop section (Feedback · Input gain · Speed value bars · Quantize · count-in /
// set-tempo / reverse switches). Transport + editing go through the device
// command channel; state and per-layer waveforms come from the device scope.

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

internal sealed class StrataDeviceBody : IDeviceBody
{
    private const int Feedback = 0, InputGain = 1, Speed = 2, Quantize = 3, CountIn = 4, SetTempo = 5, Reverse = 6;
    // deviceAction ids (mirror Strata.h).
    private const int C_Record = 0, C_Overdub = 1, C_Play = 2, C_Stop = 3, C_Undo = 4, C_Clear = 5, A_LayerMute = 6, A_LayerGain = 7;
    private const int M_Empty = 0, M_Rec = 1, M_Play = 2, M_Over = 3, M_Stop = 4;
    private const int MaxLayers = 8;

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

        var readouts = new List<Action>();
        var scope = new float[6 + MaxLayers * 3];
        var waveBuf = new float[64];

        // ---------- Live strip ----------
        var barNum = new TextBlock { Text = "–", FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center, MinWidth = 18 };
        barNum.FontFamily = NotaFonts.UiFamily;
        var ticks = new Border[16];
        var tickRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < 16; i++) { ticks[i] = new Border { Width = i % 4 == 0 ? 2 : 1.5, Height = 14, Background = TextDisabled, CornerRadius = NotaRadius.Fine }; tickRow.Children.Add(ticks[i]); }
        var ofBars = DeviceBodyKit.Note("of – bars", TextTertiary);

        // Transport actions: raised buttons, each captioned with what it does next. Record's
        // name keeps the record red; the active mode's button lights (the record wash, or the
        // soft orange for Overdub and Play).
        var actNames = new[] { "Record", "Overdub", "Play", "Stop" };
        var actCmds = new[] { C_Record, C_Overdub, C_Play, C_Stop };
        var actBtn = new Border[4]; var actSub = new TextBlock[4]; var actName = new TextBlock[4];
        var actRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < 4; i++)
        {
            int iv = i;
            actName[i] = new TextBlock { Text = actNames[i], FontSize = NotaType.Caption, FontWeight = FontWeight.SemiBold, Foreground = i == 0 ? NotaPalette.Record : TextPrimary, LineHeight = 13 };
            actSub[i] = new TextBlock { Text = "", FontSize = NotaType.RowLabel, Foreground = TextTertiary, LineHeight = 13 };
            actBtn[i] = new Border { Height = 30, Width = 70, CornerRadius = NotaRadius.Field, Background = NotaPalette.SurfaceHover, Padding = new Thickness(6, 0), Cursor = new Cursor(StandardCursorType.Hand),
                Child = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { actName[i], actSub[i] } } };
            actBtn[i].BindResource(Border.BoxShadowProperty, "Shadow.Lifted");
            actBtn[i].PointerPressed += (_, _) => Act(actCmds[iv]);
            actRow.Children.Add(actBtn[i]);
        }
        DockPanel.SetDock(actRow, Dock.Right);
        var live = new Border { BorderBrush = NotaPalette.Rule, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, 8),
            Child = new DockPanel { LastChildFill = false, VerticalAlignment = VerticalAlignment.Center, Children = {
                actRow,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { barNum, tickRow, ofBars } } } } };

        // ---------- layer stack ----------
        var layerHint = new TextBlock { Text = "Press Record to lay down the first loop", FontSize = NotaType.Caption, Foreground = TextSecondary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var layerRows = new Border[MaxLayers]; var waves = new StrataWave[MaxLayers];
        var stateTxts = new TextBlock[MaxLayers]; var dbTxts = new TextBlock[MaxLayers]; var muteBtns = new Border[MaxLayers]; var idxTxts = new TextBlock[MaxLayers];
        var layerStack = new StackPanel { Spacing = 2, Margin = new Thickness(4) };
        var countLbl = DeviceBodyKit.Note("", TextTertiary);
        for (int i = 0; i < MaxLayers; i++)
        {
            int iv = i;
            idxTxts[i] = new TextBlock { Text = $"{i + 1}", FontSize = NotaType.RowLabel, Foreground = TextTertiary, Width = 10, VerticalAlignment = VerticalAlignment.Center, FontFamily = NotaFonts.UiFamily };
            var nameTxt = new TextBlock { Text = $"Layer {i + 1}", FontSize = NotaType.KnobValue, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary, Width = 58, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            waves[i] = new StrataWave { Height = 18, VerticalAlignment = VerticalAlignment.Center };
            stateTxts[i] = new TextBlock { Text = "Play", FontSize = NotaType.RowLabel, Foreground = TextSecondary, Width = 40, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            dbTxts[i] = new TextBlock { Text = "0.0", FontSize = NotaType.KnobValue, Foreground = TextPrimary, Width = 32, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.SizeNorthSouth), FontFamily = NotaFonts.UiFamily };
            // Vertical drag on the dB readout sets that layer's level.
            bool drag = false; double startY = 0, startDb = 0;
            dbTxts[i].PointerPressed += (_, e) => { drag = true; startY = e.GetPosition(dbTxts[iv]).Y; startDb = scope[7 + iv * 3]; e.Pointer.Capture(dbTxts[iv]); };
            dbTxts[i].PointerMoved += (_, e) => { if (drag) { double db = Math.Clamp(startDb + (startY - e.GetPosition(dbTxts[iv]).Y) * 0.15, -24, 12); Act(A_LayerGain, iv, (float)db); dbTxts[iv].Text = $"{db:+0.0;−0.0;0.0}"; } };
            dbTxts[i].PointerReleased += (_, e) => { drag = false; e.Pointer.Capture(null); };
            dbTxts[i].PointerCaptureLost += (_, _) => drag = false;
            muteBtns[i] = new Border { Width = 20, Height = 18, CornerRadius = NotaRadius.Cell, Background = NotaPalette.SurfaceHover, Cursor = new Cursor(StandardCursorType.Hand),
                Child = new TextBlock { Text = "M", FontSize = NotaType.RowLabel, Foreground = TextSecondary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            muteBtns[i].BindResource(Border.BoxShadowProperty, "Shadow.Lifted");
            muteBtns[i].PointerPressed += (_, _) => { bool nowMuted = scope[8 + iv * 3] <= 0.5f; Act(A_LayerMute, iv, nowMuted ? 1f : 0f); };
            var row = new DockPanel { LastChildFill = true, Children = { } };
            var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center, Children = { idxTxts[i], nameTxt } };
            var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center, Children = { stateTxts[i], dbTxts[i], muteBtns[i] } };
            DockPanel.SetDock(left, Dock.Left); DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(left); row.Children.Add(right); row.Children.Add(new Border { Margin = new Thickness(7, 0), Child = waves[i] });
            layerRows[i] = new Border { Height = 28, CornerRadius = NotaRadius.Cell, Padding = new Thickness(8, 0), IsVisible = false, Child = row };
            layerStack.Children.Add(layerRows[i]);
        }
        // Undo / Export / Clear act on the layers, so they head the stack (the Loop rail keeps the settings).
        var layerActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        var layerTitle = DeviceBodyKit.Title("Layers"); layerTitle.VerticalAlignment = VerticalAlignment.Center;
        countLbl.VerticalAlignment = VerticalAlignment.Center; countLbl.Margin = new Thickness(10, 0, 0, 0);
        var layerHead = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 8, 0, 6), Children = { layerTitle } };
        DockPanel.SetDock(layerActions, Dock.Right); layerHead.Children.Add(layerActions);
        layerHead.Children.Add(countLbl);
        var layerCol = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(live, Dock.Top); layerCol.Children.Add(live);
        DockPanel.SetDock(layerHead, Dock.Top); layerCol.Children.Add(layerHead);
        var stackHost = new Border { Background = NotaPalette.Groove, CornerRadius = NotaRadius.Field, ClipToBounds = true, Child = new Grid { Children = { layerStack, layerHint } } };
        layerCol.Children.Add(stackHost);

        // ---------- Loop rail ----------
        // A value-bar row over a param (`log` = a log map, for Speed): label · bar · value.
        Control HSlider(int p, string name, Func<double, string> fmt, bool log)
        {
            double mn = Mn(p), mx = Mx(p);
            double lmn = log ? Math.Log(mn) : mn, lspan = (log ? Math.Log(mx) : mx) - lmn;
            double NormOf(double v) { double x = log ? Math.Log(Math.Clamp(v, mn, mx)) : Math.Clamp(v, mn, mx); return (x - lmn) / Math.Max(1e-9, lspan); }
            double ValOf(double n) { double x = lmn + Math.Clamp(n, 0, 1) * lspan; return log ? Math.Exp(x) : x; }
            void Begin() => engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
            void End() => engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
            var row = SliderRow(name, () => NormOf(P(p)), n => SetR(p, (float)ValOf(n)), () => fmt(P(p)), out var sync,
                Begin, End, () => { Begin(); SetR(p, engine.DeviceParamDefault(track, di, p)); End(); },
                labelWidth: 70, valueWidth: 48);
            MidiLearn.Bind(row, MidiTarget.DeviceParam(track, di, p), name);
            readouts.Add(sync);
            return row;
        }
        Control Seg(int p, string[] names)
        {
            int n = names.Length;
            var seg = DeviceCardKit.Segments(names, () => Math.Clamp((int)Math.Round(P(p)), 0, n - 1), iv => SetR(p, iv), out var sync, minSegWidth: 28);
            readouts.Add(sync);
            MidiLearn.Bind(seg, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            return seg;
        }
        Control Toggle(int p, string label)
        {
            var b = Switch(label, () => P(p) > 0.5f, () => SetR(p, P(p) > 0.5f ? 0f : 1f), out var sync);
            MidiLearn.Bind(b, MidiTarget.DeviceParam(track, di, p), label);
            readouts.Add(sync); return b;
        }
        Control ActionBtn(string label, Action onClick)
        {
            var b = TextButton(label);
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            ((TextBlock)b.Child!).HorizontalAlignment = HorizontalAlignment.Center;
            b.PointerPressed += (_, _) => onClick();
            return b;
        }
        string PctF(double v) => $"{v:0}\u2009%"; string DbF(double v) => $"{v:+0.0;−0.0;0.0}"; string SpeedF(double v) => $"{v:0.00}×";

        var undo = ActionBtn("Undo", () => Act(C_Undo));
        var export = ActionBtn("Export", () => ctx.NotifyChanged());
        var clear = ActionBtn("Clear", () => Act(C_Clear));
        foreach (var b in new[] { undo, export, clear }) { ((Border)b).MinWidth = 58; layerActions.Children.Add(b); }

        var qLbl = DeviceBodyKit.Label("Quantize"); qLbl.Width = 70;
        var qRow = new StackPanel { Orientation = Orientation.Horizontal, Children = { qLbl, Seg(Quantize, new[] { "Off", "Bar", "1/4" }) } };
        var loopTitle = DeviceBodyKit.Title("Loop");
        var toggles = new StackPanel { Spacing = 1, Children = {
            Toggle(CountIn, "Count-in 1 bar"),
            Toggle(SetTempo, "Set tempo from loop"),
            Toggle(Reverse, "Reverse") } };
        var railBody = new DockPanel { LastChildFill = false, Children = {
            DeviceBodyKit.Dock(new StackPanel { Spacing = 5, Children = {
                loopTitle,
                HSlider(Feedback, "Feedback", PctF, false),
                HSlider(InputGain, "Input gain", DbF, false),
                HSlider(Speed, "Speed", SpeedF, true),
                qRow,
                DeviceBodyKit.Rule(1),
                toggles } }, Dock.Top) } };
        var rail = DeviceBodyKit.Sec(railBody, SectionPad, 206);
        DockPanel.SetDock(rail, Dock.Right);
        var layerSec = DeviceBodyKit.Sec(layerCol);
        layerSec.Margin = new Thickness(0, 0, SectionGap, 0);

        var body = DeviceBodyKit.Frame(new DockPanel { LastChildFill = true, Children = { rail, layerSec } });

        // ---------- live refresh ----------
        void Refresh()
        {
            int cnt = engine.DeviceScope(track, di, scope, scope.Length);
            if (cnt < 6) return;
            int mode = (int)scope[0], loopBars = Math.Max(0, (int)scope[1]), layers = (int)scope[3], recL = (int)scope[4];
            float phase = scope[2], recProg = scope[5];

            if (layers > 0 && loopBars > 0)
            {
                int barPos = Math.Clamp((int)(phase * loopBars), 0, loopBars - 1);
                int beat = ((int)(phase * loopBars * 4)) % 4;
                barNum.Text = $"{barPos + 1} . {beat + 1}";
            }
            else barNum.Text = "–";
            ofBars.Text = loopBars == 1 ? "of 1 bar" : $"of {loopBars} bars";
            int lit = (int)Math.Round(Math.Clamp(phase, 0, 1) * 16);
            bool rolling = mode is M_Rec or M_Over or M_Play;   // the progress is the playhead only while it runs
            for (int i = 0; i < 16; i++) ticks[i].Background = (layers > 0 && i < lit) ? (rolling ? Brass : NotaPalette.VizInk) : TextDisabled;
            countLbl.Text = layers > 0 ? $"{layers} of {MaxLayers} · one cycle shown" : "";

            // Action captions + active highlight.
            actSub[0].Text = mode == M_Empty || layers == 0 ? "start loop" : "arm next";
            actSub[1].Text = layers < MaxLayers ? $"onto layer {Math.Min(MaxLayers, layers + 1)}" : "full";
            actSub[2].Text = "all layers";
            actSub[3].Text = "at bar end";
            int active = mode switch { M_Rec => 0, M_Over => 1, M_Play => 2, M_Stop => 3, _ => -1 };
            for (int i = 0; i < 4; i++)
            {
                bool on = i == active;
                actBtn[i].Background = !on ? NotaPalette.SurfaceHover : i == 0 ? NotaPalette.Wash(NotaPalette.Record, 0x28) : i == 3 ? NotaPalette.Groove : NotaPalette.AccentSoft;
                if (i is 1 or 2) actName[i].Foreground = on ? AccentBright : TextPrimary;
            }

            layerHint.IsVisible = layers == 0;
            for (int k = 0; k < MaxLayers; k++)
            {
                bool vis = k < layers;
                layerRows[k].IsVisible = vis;
                if (!vis) continue;
                int st = (int)scope[6 + k * 3]; float gainDb = scope[7 + k * 3]; bool muted = scope[8 + k * 3] > 0.5f;
                bool rec = recL == k;
                stateTxts[k].Text = rec ? "Rec" : muted ? "Muted" : "Play";
                stateTxts[k].Foreground = rec ? NotaPalette.Record : muted ? TextDisabled : TextSecondary;
                dbTxts[k].Text = $"{gainDb:+0.0;−0.0;0.0}";
                var mtxt = (TextBlock)muteBtns[k].Child!;
                muteBtns[k].Background = muted ? NotaPalette.Mute : NotaPalette.SurfaceHover;   // mute on = the amber mute fill
                mtxt.Foreground = muted ? NotaPalette.InkOn(NotaPalette.Mute) : TextSecondary;
                // Layers are told apart by shade, not hue: alternate greys; muted = ink 4.
                var col = muted ? NotaPalette.TextDisabled.Color : (k % 2 == 0 ? NotaPalette.VizInk.Color : NotaPalette.TextTertiary.Color);
                layerRows[k].Background = rec ? NotaPalette.Wash(NotaPalette.Record, 0x1A) : NotaPalette.Wash(NotaPalette.TextPrimary, 0x08);
                int wn = engine.DeviceLayerWave(track, di, k, waveBuf, waveBuf.Length);
                var env = new float[Math.Max(1, wn)];
                for (int i = 0; i < wn; i++) env[i] = Math.Clamp(waveBuf[i], 0f, 1f);
                waves[k].Set(env, col, muted, rec, rec ? recProg : 1f);
            }
            foreach (var a in readouts) a();
        }
        ctx.AddDeviceRefresher(Refresh);
        Refresh();

        return body;
    }
}
