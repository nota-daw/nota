// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Detail · Devices — built-in Nota Strata (kind 15) body, mockup 3n on the shared
// shell (700×260, full-bleed). A LIVE strip (big bar.beat counter · a cycle tick
// strip that fills as the loop runs · the four transport actions, each captioned
// with what it will do next) sits above a body of the layer stack (per pass: index,
// name, its own waveform, PLAY/MUTED/REC state, level and mute) | a LOOP rail
// (Feedback teal · Input gain · Speed · Quantize · count-in / set-tempo / reverse
// switches · Undo / Export / Clear). Transport + editing go through the device
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
    private static readonly Color[] Palette =
    {
        Color.Parse("#C99C55"), Color.Parse("#9BA65D"), Color.Parse("#6FA383"), Color.Parse("#C4756A"),
        Color.Parse("#6E93C4"), Color.Parse("#B07FC0"), Color.Parse("#C0A24E"), Color.Parse("#7FB0A0"),
    };

    public double Width => 700;
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

        Control Lbl(string t, double fs, IBrush c) => new TextBlock { Text = t, FontSize = fs, FontWeight = FontWeight.Bold, Foreground = c, VerticalAlignment = VerticalAlignment.Center };

        // ---------- LIVE strip ----------
        var barNum = new TextBlock { Text = "–", FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = AccentBright, VerticalAlignment = VerticalAlignment.Center, MinWidth = 30 };
        barNum.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        var ticks = new Border[16];
        var tickRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < 16; i++) { ticks[i] = new Border { Width = i % 4 == 0 ? 2 : 1, Height = 14, Background = Sunken, CornerRadius = new CornerRadius(1) }; tickRow.Children.Add(ticks[i]); }
        var ofBars = new TextBlock { Text = "of – bars", FontSize = 8, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center };

        // Transport actions. Each button is captioned with what it does next; the
        // active mode's button lights in its colour.
        var actNames = new[] { "Record", "Overdub", "Play", "Stop" };
        var actCmds = new[] { C_Record, C_Overdub, C_Play, C_Stop };
        var actColors = new[] { Color.Parse("#D95F4C"), Color.Parse("#D8A03D"), Color.Parse("#58B368"), Color.Parse("#A39D8F") };
        var actBtn = new Border[4]; var actSub = new TextBlock[4]; var actName = new TextBlock[4];
        var actRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < 4; i++)
        {
            int iv = i;
            actName[i] = new TextBlock { Text = actNames[i], FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(actColors[i]) };
            actSub[i] = new TextBlock { Text = "", FontSize = 7, Foreground = TextTertiary };
            actBtn[i] = new Border { Height = 24, MinWidth = 62, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), BorderBrush = BorderStrong, Background = Card2, Padding = new Thickness(9, 0), Cursor = new Cursor(StandardCursorType.Hand),
                Child = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { actName[i], actSub[i] } } };
            actBtn[i].PointerPressed += (_, _) => Act(actCmds[iv]);
            actRow.Children.Add(actBtn[i]);
        }
        DockPanel.SetDock(actRow, Dock.Right);
        var live = new Border { Height = 34, Background = new SolidColorBrush(Color.Parse("#1E1C18")), BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new DockPanel { LastChildFill = false, Margin = new Thickness(9, 0), VerticalAlignment = VerticalAlignment.Center, Children = {
                actRow,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { barNum, tickRow, ofBars } } } } };

        // ---------- layer stack ----------
        var layerHint = new TextBlock { Text = "Press Record to lay down the first loop", FontSize = 10, Foreground = TextTertiary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var layerRows = new Border[MaxLayers]; var waves = new StrataWave[MaxLayers];
        var stateTxts = new TextBlock[MaxLayers]; var dbTxts = new TextBlock[MaxLayers]; var muteBtns = new Border[MaxLayers]; var idxTxts = new TextBlock[MaxLayers];
        var layerStack = new StackPanel { Spacing = 3 };
        var countLbl = new TextBlock { Text = "", FontSize = 8, Foreground = TextTertiary, HorizontalAlignment = HorizontalAlignment.Right };
        for (int i = 0; i < MaxLayers; i++)
        {
            int iv = i;
            idxTxts[i] = new TextBlock { Text = $"{i + 1}", FontSize = 9, Foreground = TextTertiary, Width = 8, VerticalAlignment = VerticalAlignment.Center };
            idxTxts[i].BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
            var nameTxt = new TextBlock { Text = $"Layer {i + 1}", FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary, Width = 58, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            waves[i] = new StrataWave { Height = 20, VerticalAlignment = VerticalAlignment.Center };
            stateTxts[i] = new TextBlock { Text = "PLAY", FontSize = 8, FontWeight = FontWeight.Bold, Foreground = Success, Width = 36, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            dbTxts[i] = new TextBlock { Text = "0.0", FontSize = 9, Foreground = TextSecondary, Width = 30, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.SizeNorthSouth) };
            dbTxts[i].BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
            // Vertical drag on the dB readout sets that layer's level.
            bool drag = false; double startY = 0, startDb = 0;
            dbTxts[i].PointerPressed += (_, e) => { drag = true; startY = e.GetPosition(dbTxts[iv]).Y; startDb = scope[7 + iv * 3]; e.Pointer.Capture(dbTxts[iv]); };
            dbTxts[i].PointerMoved += (_, e) => { if (drag) { double db = Math.Clamp(startDb + (startY - e.GetPosition(dbTxts[iv]).Y) * 0.15, -24, 12); Act(A_LayerGain, iv, (float)db); dbTxts[iv].Text = $"{db:+0.0;-0.0;0.0}"; } };
            dbTxts[i].PointerReleased += (_, e) => { drag = false; e.Pointer.Capture(null); };
            muteBtns[i] = new Border { Width = 16, Height = 14, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), BorderBrush = BorderDef, Background = Sunken, Cursor = new Cursor(StandardCursorType.Hand),
                Child = new TextBlock { Text = "M", FontSize = 8, FontWeight = FontWeight.Bold, Foreground = TextTertiary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            muteBtns[i].PointerPressed += (_, _) => { bool nowMuted = scope[8 + iv * 3] <= 0.5f; Act(A_LayerMute, iv, nowMuted ? 1f : 0f); };
            var row = new DockPanel { LastChildFill = true, Children = { } };
            var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center, Children = { idxTxts[i], nameTxt } };
            var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center, Children = { stateTxts[i], dbTxts[i], muteBtns[i] } };
            DockPanel.SetDock(left, Dock.Left); DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(left); row.Children.Add(right); row.Children.Add(new Border { Margin = new Thickness(7, 0), Child = waves[i] });
            layerRows[i] = new Border { Height = 36, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1, 1, 1, 1), Padding = new Thickness(7, 0), IsVisible = false, Child = row };
            layerStack.Children.Add(layerRows[i]);
        }
        var layerHead = new DockPanel { LastChildFill = false, Children = { Lbl("LAYERS", 8, TextTertiary) } };
        DockPanel.SetDock(countLbl, Dock.Right); layerHead.Children.Add(countLbl);
        var layerCol = new DockPanel { LastChildFill = true, Margin = new Thickness(8, 6), Children = { } };
        DockPanel.SetDock(layerHead, Dock.Top); layerCol.Children.Add(layerHead);
        var stackHost = new Grid { Children = { layerStack, layerHint } };
        layerCol.Children.Add(stackHost);

        // ---------- LOOP rail ----------
        Control HSlider(int p, string name, Func<double, string> fmt, bool log, IBrush accent)
        {
            double mn = Mn(p), mx = Mx(p);
            double lmn = log ? Math.Log(mn) : mn, lspan = (log ? Math.Log(mx) : mx) - lmn;
            double NormOf(double v) { double x = log ? Math.Log(Math.Clamp(v, mn, mx)) : Math.Clamp(v, mn, mx); return (x - lmn) / Math.Max(1e-9, lspan); }
            double ValOf(double n) { double x = lmn + Math.Clamp(n, 0, 1) * lspan; return log ? Math.Exp(x) : x; }
            double tw = 84;
            var trk = new Border { Width = tw, Height = 3, Background = Sunken, CornerRadius = new CornerRadius(2) };
            var fill = new Border { Height = 3, Background = accent, CornerRadius = new CornerRadius(2) };
            var handle = new Border { Width = 8, Height = 9, Background = new SolidColorBrush(Color.Parse("#A39D8F")), CornerRadius = new CornerRadius(2) };
            var canvas = new Canvas { Width = tw, Height = 9, Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
            Canvas.SetTop(trk, 3); Canvas.SetTop(fill, 3); Canvas.SetTop(handle, 0);
            canvas.Children.Add(trk); canvas.Children.Add(fill); canvas.Children.Add(handle);
            var val = new TextBlock { FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            val.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
            bool drag = false;
            void Vis(double v) { double nn = NormOf(v); fill.Width = Math.Max(0, nn * tw); Canvas.SetLeft(handle, nn * tw - 4); val.Text = fmt(v); }
            void From(PointerEventArgs e) { double nn = Math.Clamp(e.GetPosition(canvas).X / tw, 0, 1); float v = (float)ValOf(nn); SetR(p, v); Vis(v); }
            canvas.PointerPressed += (_, e) => { drag = true; engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, ""); e.Pointer.Capture(canvas); From(e); };
            canvas.PointerMoved += (_, e) => { if (drag) From(e); };
            canvas.PointerReleased += (_, e) => { if (drag) { drag = false; engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, ""); e.Pointer.Capture(null); } };
            MidiLearn.Bind(canvas, MidiTarget.DeviceParam(track, di, p), name);
            readouts.Add(() => { if (!drag) Vis(P(p)); });
            Vis(P(p));
            var lbl = new TextBlock { Text = name, FontSize = 8, FontWeight = FontWeight.Bold, Foreground = ReferenceEquals(accent, Teal) ? Teal : TextTertiary, Width = 52, VerticalAlignment = VerticalAlignment.Center };
            var head = new DockPanel { LastChildFill = false, Children = { lbl } };
            DockPanel.SetDock(val, Dock.Right); head.Children.Add(val);
            return new StackPanel { Spacing = 2, Children = { head, canvas } };
        }
        Control Seg(int p, string[] names)
        {
            int n = names.Length; var arr = new Border[n];
            void Hi() { int cur = Math.Clamp((int)Math.Round(P(p)), 0, n - 1); for (int i = 0; i < n; i++) { bool on = i == cur; arr[i].Background = on ? Brass : Brushes.Transparent; ((TextBlock)arr[i].Child!).Foreground = on ? OnAccent : TextTertiary; ((TextBlock)arr[i].Child!).FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal; } }
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
            for (int i = 0; i < n; i++) { int iv = i; arr[i] = new Border { CornerRadius = new CornerRadius(2), Padding = new Thickness(0, 1), MinWidth = 26, Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = names[i], FontSize = 8, Foreground = TextTertiary, HorizontalAlignment = HorizontalAlignment.Center } }; arr[i].PointerPressed += (_, _) => { SetR(p, iv); Hi(); }; row.Children.Add(arr[i]); }
            readouts.Add(Hi); Hi();
            var seg = new Border { Background = Sunken, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(1), Child = row };
            MidiLearn.Bind(seg, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            return seg;
        }
        Control Toggle(int p, string label, IBrush tint)
        {
            var dot = new Border { Width = 18, Height = 10, CornerRadius = new CornerRadius(5), Background = Sunken, Child = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = TextTertiary, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center } };
            var txt = new TextBlock { Text = label, FontSize = 8, FontWeight = FontWeight.Bold, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center };
            void Hi() { bool on = P(p) > 0.5f; var inner = (Border)dot.Child!; dot.Background = on ? tint : Sunken; inner.Background = on ? (IBrush)new SolidColorBrush(Color.Parse("#171613")) : TextTertiary; inner.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left; inner.Margin = on ? new Thickness(0, 0, 2, 0) : new Thickness(2, 0, 0, 0); txt.Foreground = on ? tint : TextTertiary; }
            var b = new Border { Cursor = new Cursor(StandardCursorType.Hand), Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { dot, txt } } };
            b.PointerPressed += (_, _) => { SetR(p, P(p) > 0.5f ? 0f : 1f); Hi(); };
            MidiLearn.Bind(b, MidiTarget.DeviceParam(track, di, p), label);
            readouts.Add(Hi); Hi(); return b;
        }
        Control ActionBtn(string label, IBrush color, Action onClick)
        {
            var b = new Border { CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), BorderBrush = BorderStrong, Background = Card2, Padding = new Thickness(0, 2), Cursor = new Cursor(StandardCursorType.Hand),
                Child = new TextBlock { Text = label, FontSize = 9, Foreground = color, HorizontalAlignment = HorizontalAlignment.Center } };
            b.PointerPressed += (_, _) => onClick();
            return b;
        }
        string PctF(double v) => $"{v:0} %"; string DbF(double v) => $"{v:+0.0;-0.0;0.0}"; string SpeedF(double v) => $"{v:0.00}×";

        var undo = ActionBtn("Undo", TextSecondary, () => Act(C_Undo));
        var export = ActionBtn("Export", TextSecondary, () => ctx.NotifyChanged());
        var clear = ActionBtn("Clear", new SolidColorBrush(Color.Parse("#D95F4C")), () => Act(C_Clear));
        var bottomActions = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 4, Margin = new Thickness(0, 4, 0, 0) };
        Grid.SetColumn(undo, 0); Grid.SetColumn(export, 1); Grid.SetColumn(clear, 2);
        bottomActions.Children.Add(undo); bottomActions.Children.Add(export); bottomActions.Children.Add(clear);

        var qRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new TextBlock { Text = "QUANTIZE", FontSize = 8, FontWeight = FontWeight.Bold, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center }, Seg(Quantize, new[] { "Off", "Bar", "1/4" }) } };
        var rail = new Border { Width = 174, Background = new SolidColorBrush(Color.Parse("#1B1916")), BorderBrush = BorderDef, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(8, 6),
            Child = new StackPanel { Spacing = 5, Children = {
                Lbl("LOOP", 8, TextTertiary),
                HSlider(Feedback, "FEEDBACK", PctF, false, Teal),
                HSlider(InputGain, "INPUT GAIN", DbF, false, Brass),
                HSlider(Speed, "SPEED", SpeedF, true, Brass),
                qRow,
                new Border { Height = 1, Background = new SolidColorBrush(Color.Parse("#26231E")) },
                Toggle(CountIn, "COUNT-IN 1 BAR", Teal),
                Toggle(SetTempo, "SET TEMPO FROM LOOP", Teal),
                Toggle(Reverse, "REVERSE", Brass),
                new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Children = { bottomActions } } } } };
        DockPanel.SetDock(rail, Dock.Right);

        var body = new DockPanel { LastChildFill = true, Children = { rail, layerCol } };

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
            ofBars.Text = $"of {(loopBars > 0 ? loopBars : 0)} bars";
            int lit = (int)Math.Round(Math.Clamp(phase, 0, 1) * 16);
            for (int i = 0; i < 16; i++) ticks[i].Background = (layers > 0 && i < lit) ? Brass : Sunken;
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
                actBtn[i].Background = on ? new SolidColorBrush(Color.FromArgb(0x28, actColors[i].R, actColors[i].G, actColors[i].B)) : Card2;
                actBtn[i].BorderBrush = on ? new SolidColorBrush(actColors[i]) : BorderStrong;
            }

            layerHint.IsVisible = layers == 0;
            for (int k = 0; k < MaxLayers; k++)
            {
                bool vis = k < layers;
                layerRows[k].IsVisible = vis;
                if (!vis) continue;
                int st = (int)scope[6 + k * 3]; float gainDb = scope[7 + k * 3]; bool muted = scope[8 + k * 3] > 0.5f;
                bool rec = recL == k;
                stateTxts[k].Text = rec ? "REC" : muted ? "MUTED" : "PLAY";
                stateTxts[k].Foreground = rec ? new SolidColorBrush(Color.Parse("#D95F4C")) : muted ? TextTertiary : Success;
                dbTxts[k].Text = $"{gainDb:+0.0;-0.0;0.0}";
                var mtxt = (TextBlock)muteBtns[k].Child!;
                muteBtns[k].Background = muted ? new SolidColorBrush(Color.Parse("#3A2320")) : Sunken;
                muteBtns[k].BorderBrush = muted ? new SolidColorBrush(Color.Parse("#D95F4C")) : BorderDef;
                mtxt.Foreground = muted ? new SolidColorBrush(Color.Parse("#D95F4C")) : TextTertiary;
                var col = Palette[k % Palette.Length];
                layerRows[k].Background = rec ? new SolidColorBrush(Color.FromArgb(0x1A, 0xD9, 0x5F, 0x4C)) : muted ? new SolidColorBrush(Color.Parse("#1B1916")) : new SolidColorBrush(Color.FromArgb(0x0D, 0xD8, 0xA0, 0x3D));
                layerRows[k].BorderBrush = rec ? new SolidColorBrush(Color.Parse("#D95F4C")) : muted ? BorderDef : new SolidColorBrush(Color.FromArgb(0x73, col.R, col.G, col.B));
                int wn = engine.DeviceLayerWave(track, di, k, waveBuf, waveBuf.Length);
                var env = new float[Math.Max(1, wn)];
                for (int i = 0; i < wn; i++) env[i] = Math.Clamp(waveBuf[i], 0f, 1f);
                waves[k].Set(env, col, muted, rec, rec ? recProg : 1f);
            }
            foreach (var a in readouts) a();
        }
        ctx.AddDeviceRefresher(Refresh);
        Refresh();

        DockPanel.SetDock(live, Dock.Top);
        return new DockPanel { LastChildFill = true, Background = new SolidColorBrush(Color.Parse("#171613")), Children = { live, body } };
    }
}
