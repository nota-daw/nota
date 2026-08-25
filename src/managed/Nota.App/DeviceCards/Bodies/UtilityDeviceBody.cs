// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Detail · Devices — built-in Nota Utility (kind 4) body, mockup 3j "show the thing
// you can't hear". Three islands: ROUTING (channel mode · mono-below · mute · phase
// invert · reset / gain match) | STEREO FIELD (a goniometer/vectorscope + a
// correlation meter with the mono-risk zone in red) | LEVELS (Gain / Balance / Width
// knobs + IN/OUT peak meters). The scope (peaks · correlation · a decimated ring of
// output L/R pairs) comes from the packed device scope; params drive the knobs.

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

internal sealed class UtilityDeviceBody : IDeviceBody
{
    // Param indices — must match Utility.h.
    private const int Gain = 0, Balance = 1, Width_ = 2, ChannelMode = 3, MonoFreq = 4, MonoBelow = 5, Mute = 6, InvertL = 7, InvertR = 8;
    // Packed scope layout (Utility::scopeRead): meters then kScopePairs (L,R) pairs.
    private const int S_InL = 0, S_InR = 1, S_OutL = 2, S_OutR = 3, S_Corr = 4, kMeters = 5, kPairs = 48, kScope = kMeters + kPairs * 2;

    public double Width => 622;

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine; int track = ctx.TrackId, di = index;
        float P(int p) => engine.DeviceGetParam(track, di, p);
        void SetR(int p, float v) => engine.DeviceSetParam(track, di, p, v);

        var readouts = new List<Action>();
        var scope = new float[kScope];
        var pairs = new float[kPairs * 2];

        // ---- shared small controls (mirroring the Ceiling body) --------------
        Control Toggle(int p, string label, IBrush tint)
        {
            var b = new Border { CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Padding = new Thickness(7, 2), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = label, FontSize = 8, FontWeight = FontWeight.Bold } };
            void Hi() { bool on = P(p) > 0.5f; b.Background = on ? AccentSubtleB : Sunken; b.BorderBrush = on ? tint : BorderDef; ((TextBlock)b.Child!).Foreground = on ? tint : TextTertiary; }
            b.PointerPressed += (_, _) => { SetR(p, P(p) > 0.5f ? 0f : 1f); Hi(); };
            MidiLearn.Bind(b, MidiTarget.DeviceParam(track, di, p), label);
            readouts.Add(Hi); Hi(); return b;
        }
        // Horizontal log slider for the mono-below cutoff.
        Control FreqSlider(double tw)
        {
            double mn = 20, mx = 2000, lmn = Math.Log(mn), lspan = Math.Log(mx) - lmn;
            double Norm(double v) => (Math.Log(Math.Clamp(v, mn, mx)) - lmn) / lspan;
            double Val(double n) => Math.Exp(lmn + Math.Clamp(n, 0, 1) * lspan);
            var trk = new Border { Width = tw, Height = 3, Background = Sunken, CornerRadius = new CornerRadius(2) };
            var fill = new Border { Height = 3, Background = Brass, CornerRadius = new CornerRadius(2) };
            var handle = new Border { Width = 8, Height = 9, Background = new SolidColorBrush(Color.Parse("#A39D8F")), CornerRadius = new CornerRadius(2) };
            var canvas = new Canvas { Width = tw, Height = 9, Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
            Canvas.SetTop(trk, 3); Canvas.SetTop(fill, 3); Canvas.SetTop(handle, 0);
            canvas.Children.Add(trk); canvas.Children.Add(fill); canvas.Children.Add(handle);
            var val = new TextBlock { FontSize = 9, Foreground = TextPrimary, MinWidth = 44, VerticalAlignment = VerticalAlignment.Center };
            val.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
            bool drag = false;
            void Vis(double v) { double n = Norm(v); fill.Width = Math.Max(0, n * tw); Canvas.SetLeft(handle, n * tw - 4); val.Text = $"{v:0} Hz"; }
            void From(PointerEventArgs e) { double n = Math.Clamp(e.GetPosition(canvas).X / tw, 0, 1); float v = (float)Val(n); SetR(MonoFreq, v); Vis(v); }
            canvas.PointerPressed += (_, e) => { drag = true; engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, MonoFreq, ""); e.Pointer.Capture(canvas); From(e); };
            canvas.PointerMoved += (_, e) => { if (drag) From(e); };
            canvas.PointerReleased += (_, e) => { if (drag) { drag = false; engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, MonoFreq, ""); e.Pointer.Capture(null); } };
            MidiLearn.Bind(canvas, MidiTarget.DeviceParam(track, di, MonoFreq), engine.DeviceParamName(track, di, MonoFreq));
            readouts.Add(() => { if (!drag) Vis(P(MonoFreq)); });
            Vis(P(MonoFreq));
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { canvas, val } };
        }
        static TextBlock Cap(string t) => new() { Text = t, FontSize = 8, FontWeight = FontWeight.Bold, Foreground = TextTertiary };
        Border Button(string text, Action onClick)
        {
            var b = new Border { CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), BorderBrush = BorderStrong, Background = Card2, Padding = new Thickness(8, 3), Cursor = new Cursor(StandardCursorType.Hand),
                Child = new TextBlock { Text = text, FontSize = 9, Foreground = TextSecondary } };
            b.PointerPressed += (_, _) => onClick();
            return b;
        }

        // ---------- ROUTING island -------------------------------------------
        var modeChips = ChipRow(new[] { "Stereo", "Left", "Right", "Swap" },
            () => (int)Math.Round(P(ChannelMode)), v => SetR(ChannelMode, v));
        MidiLearn.Bind(modeChips, MidiTarget.DeviceParam(track, di, ChannelMode), engine.DeviceParamName(track, di, ChannelMode));
        var monoRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center,
            Children = { Toggle(MonoBelow, "MONO", Teal), FreqSlider(76) } };
        var phaseRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5,
            Children = { Toggle(Mute, "MUTE", Danger), Toggle(InvertL, "Ø L", Brass), Toggle(InvertR, "Ø R", Brass) } };
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = {
            Button("Reset", () => { for (int p = 0; p < engine.DeviceParamCount(track, di); p++) SetR(p, engine.DeviceParamDefault(track, di, p)); ctx.RequestRebuild(); }),
            Button("Gain match", GainMatch) } };
        var routing = Island("ROUTING", 206, new StackPanel { Spacing = 7, Children = {
            Cap("CHANNEL MODE"), modeChips,
            Cap("MONO BELOW"), monoRow,
            Cap("PHASE"), phaseRow,
            new Border { Height = 1, Background = Div(), Margin = new Thickness(0, 2) },
            btnRow } });

        // ---------- STEREO FIELD island (the hero visual) --------------------
        var gonio = new Goniometer { VerticalAlignment = VerticalAlignment.Stretch };
        var corr = new CorrMeter { Height = 12 };
        var corrVal = new TextBlock { FontSize = 9, Foreground = TextPrimary, MinWidth = 34, TextAlignment = TextAlignment.Right };
        corrVal.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        var widthVal = new TextBlock { FontSize = 8, Foreground = TextSecondary, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        widthVal.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        var fieldHead = new DockPanel { LastChildFill = false, Children = { Cap("STEREO FIELD") } };
        DockPanel.SetDock(widthVal, Dock.Right); fieldHead.Children.Add(widthVal);
        var corrRow = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 6, 0, 0),
            Children = { } };
        var corrLbl = new TextBlock { Text = "CORR", FontSize = 8, FontWeight = FontWeight.Bold, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        DockPanel.SetDock(corrLbl, Dock.Left); corrRow.Children.Add(corrLbl);
        DockPanel.SetDock(corrVal, Dock.Right); corrRow.Children.Add(corrVal);
        corrRow.Children.Add(new Border { Child = corr, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        var fieldStack = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(fieldHead, Dock.Top); fieldStack.Children.Add(fieldHead);
        DockPanel.SetDock(corrRow, Dock.Bottom); fieldStack.Children.Add(corrRow);
        fieldStack.Children.Add(new Border { Child = gonio, Margin = new Thickness(0, 6, 0, 0) });
        var field = Island("", 206, fieldStack, headerless: true, custom: fieldStack);

        // ---------- LEVELS island --------------------------------------------
        Action<float> inL, inR, outL, outR;
        var inMeter = MeterPair("IN", out inL, out inR);
        var outMeter = MeterPair("OUT", out outL, out outR);
        var knobRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center, Children = {
            DeviceParamControls.ParamRow(ctx, di, Gain),
            DeviceParamControls.ParamRow(ctx, di, Balance),
            DeviceParamControls.ParamRow(ctx, di, Width_) } };
        foreach (var c in knobRow.Children) if (c is StackPanel sp) sp.Width = 52;
        var metersRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0),
            Children = { inMeter, outMeter } };
        var levels = Island("LEVELS", 172, new StackPanel { Spacing = 2, Children = { knobRow, metersRow } });

        // ---------- assemble + live refresh ----------------------------------
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { routing, field, levels } };
        var rootBody = new Border { Padding = new Thickness(8), Child = row };

        void GainMatch()
        {
            engine.DeviceScope(track, di, scope, scope.Length);
            float ip = Math.Max(scope[S_InL], scope[S_InR]), op = Math.Max(scope[S_OutL], scope[S_OutR]);
            if (ip <= 1e-5f || op <= 1e-5f) return;
            double delta = 20.0 * Math.Log10(ip / op);
            float g = (float)Math.Clamp(P(Gain) + delta, -24.0, 24.0);
            engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, Gain, "");
            SetR(Gain, g);
            engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, Gain, "");
        }

        void Refresh()
        {
            int cnt = engine.DeviceScope(track, di, scope, scope.Length);
            if (cnt >= kScope)
            {
                Array.Copy(scope, kMeters, pairs, 0, kPairs * 2);
                gonio.Set(pairs, kPairs);
                float c = scope[S_Corr];
                corr.Set(c);
                corrVal.Text = $"{c:+0.00;-0.00;0.00}";
                corrVal.Foreground = c < 0 ? Danger : c < 0.3f ? Brass : TextPrimary;
                inL(scope[S_InL]); inR(scope[S_InR]); outL(scope[S_OutL]); outR(scope[S_OutR]);
            }
            widthVal.Text = $"width {P(Width_):0} %";
            foreach (var a in readouts) a();
        }
        ctx.AddDeviceRefresher(Refresh);
        Refresh();
        return rootBody;
    }

    private static IBrush Div() => new SolidColorBrush(Color.Parse("#26231E"));

    // A titled bordered island panel (fixed width). headerless/custom lets the
    // stereo-field island supply its own dock layout that fills the height.
    private static Control Island(string title, double width, Control body, bool headerless = false, Control? custom = null)
    {
        Control inner = headerless ? custom! : new StackPanel { Spacing = 6, Children = {
            new TextBlock { Text = title, FontSize = 9, FontWeight = FontWeight.Bold, Foreground = TextSecondary }, body } };
        return new Border { Width = width, Background = Card2, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7), Padding = new Thickness(9, 8), Child = inner };
    }

    // IN/OUT peak meter: two vertical bars (L/R) with peak-hold + a caption. Returns
    // the control and per-channel setters (linear peak in, dB internally).
    private static Control MeterPair(string label, out Action<float> setL, out Action<float> setR)
    {
        Border Bar(out Action<float> set)
        {
            const double H = 96;
            var fill = new Border { Background = Success, CornerRadius = new CornerRadius(1), VerticalAlignment = VerticalAlignment.Bottom, Height = 0 };
            var track = new Border { Width = 9, Height = H, Background = Sunken, CornerRadius = new CornerRadius(2), ClipToBounds = true, Child = fill };
            float hold = -120;
            set = peak =>
            {
                float db = peak > 1e-6f ? 20f * (float)Math.Log10(peak) : -120f;
                if (db > hold) hold = db; else hold -= 2.0f;   // ~fast attack, slow decay per tick
                double n = Math.Clamp((hold + 60.0) / 60.0, 0, 1);   // -60..0 dB
                fill.Height = n * H;
                fill.Background = hold > -0.2f ? Danger : hold > -6f ? Brass : Success;
            };
            return track;
        }
        var l = Bar(out setL); var r = Bar(out setR);
        var bars = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center, Children = { l, r } };
        var cap = new TextBlock { Text = label, FontSize = 8, FontWeight = FontWeight.Bold, Foreground = TextTertiary, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0) };
        return new StackPanel { Spacing = 0, Children = { bars, cap } };
    }

    // Goniometer / vectorscope: plots recent output (L,R) pairs in mid/side
    // coordinates (mono = vertical, anti-phase = horizontal), over L/R diagonal axes.
    private sealed class Goniometer : Control
    {
        private static readonly IBrush Bg = NotaPalette.BgSunken;
        private static readonly IBrush Grid = new SolidColorBrush(Color.Parse("#1E1C18"));
        private static readonly IBrush Diag = new SolidColorBrush(Color.Parse("#26231E"));
        private static readonly IBrush Dot = new SolidColorBrush(Color.FromArgb(0xCC, 0x7F, 0xCC, 0xE1));
        private float[]? _p; private int _n;

        public void Set(float[] p, int n) { _p = p; _n = n; InvalidateVisual(); }

        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height;
            if (w <= 2 || h <= 2) return;
            ctx.DrawRectangle(Bg, null, new RoundedRect(new Rect(0, 0, w, h), 4));
            double cx = w / 2, cy = h / 2;
            ctx.DrawLine(new Pen(Grid, 1), new Point(cx, 0), new Point(cx, h));
            ctx.DrawLine(new Pen(Grid, 1), new Point(0, cy), new Point(w, cy));
            var dash = new Pen(Diag, 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
            ctx.DrawLine(dash, new Point(0, h), new Point(w, 0));
            ctx.DrawLine(dash, new Point(0, 0), new Point(w, h));
            if (_p is null || _n <= 0) return;
            // Instantaneous samples sit well below the peak the meters show, so a raw plot
            // collapses to a dot near the centre. Boost so program material fills the scope
            // (loud peaks clip at the edges, which is the expected goniometer behaviour).
            const double gain = 2.4;
            double scale = Math.Min(w, h) * 0.5 * gain;
            using var clip = ctx.PushClip(new Rect(0, 0, w, h));
            for (int k = 0; k < _n; k++)
            {
                float l = _p[k * 2], r = _p[k * 2 + 1];
                double x = cx + (l - r) * 0.5 * scale;   // side → horizontal
                double y = cy - (l + r) * 0.5 * scale;   // mid  → vertical
                ctx.FillRectangle(Dot, new Rect(x - 0.9, y - 0.9, 1.8, 1.8));
            }
        }
    }

    // Correlation meter: −1 (left) … +1 (right); the negative "mono-risk" half is
    // tinted red, a needle marks the current value (red when out of phase).
    private sealed class CorrMeter : Control
    {
        private static readonly IBrush Bg = NotaPalette.BgSunken;
        private static readonly IBrush Risk = new SolidColorBrush(Color.FromArgb(0x33, 0xD9, 0x5F, 0x4C));
        private static readonly IBrush Tick = new SolidColorBrush(Color.Parse("#3A362D"));
        private float _c = 1;
        public void Set(float c) { _c = Math.Clamp(c, -1, 1); InvalidateVisual(); }
        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height;
            if (w <= 2 || h <= 2) return;
            var rr = new RoundedRect(new Rect(0, 0, w, h), 3);
            ctx.DrawRectangle(Bg, null, rr);
            using (ctx.PushClip(rr)) ctx.FillRectangle(Risk, new Rect(0, 0, w / 2, h));   // corr < 0 zone
            ctx.DrawLine(new Pen(Tick, 1), new Point(w / 2, 0), new Point(w / 2, h));
            double pos = (_c + 1) / 2 * w;
            var needle = _c < 0 ? NotaPalette.Danger : _c < 0.3f ? NotaPalette.Accent : NotaPalette.Success;
            ctx.FillRectangle(needle, new Rect(Math.Clamp(pos - 1.5, 0, w - 3), 0, 3, h));
        }
    }
}
