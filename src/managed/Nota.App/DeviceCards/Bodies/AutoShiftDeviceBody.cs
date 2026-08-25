// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Auto Shift (device kind 10) body, mockup 3i: a full
// 700×260 shell with a LIVE strip (detected note + cents meter + key source), a KEY grid
// + SCALE list + Follow-scale-device rail, the two-trace PITCH TRACE plot, and a
// CORRECT (teal) / SHIFT (brass) parameter rail. Auto key / Learn key / Follow are driven
// here from the device's published pitch history + the track's Nota Scale MIDI effect;
// the DSP just snaps to the current Key + Scale, so everything persists/automates.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed class AutoShiftDeviceBody : IDeviceBody
{
    // Param indices — must match AutoShift.h.
    private const int Key = 0, Scale = 1, Amount = 2, Speed = 3, Shift = 4, Mix = 5, Range = 6, Formant = 7, KeySrc = 8, Follow = 9;
    private static readonly string[] Keys = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
    private static readonly string[] Scales = { "Chromatic", "Major", "Minor", "Penta Maj", "Penta Min" };
    private static readonly int[] Masks = { 0x0FFF, 0x0AB5, 0x05AD, 0x0295, 0x04A9 };

    private static readonly IBrush Hdr = new SolidColorBrush(Color.Parse("#1E1C18"));
    private static readonly IBrush Rail = new SolidColorBrush(Color.Parse("#1B1916"));
    private static readonly IBrush Inset = new SolidColorBrush(Color.Parse("#100F0D"));
    private static readonly IBrush Bd = new SolidColorBrush(Color.Parse("#2C2923"));
    private static readonly IBrush Bd2 = new SolidColorBrush(Color.Parse("#221F1A"));
    private static readonly IBrush CardBg = new SolidColorBrush(Color.Parse("#26231E"));
    private static readonly IBrush Amber = new SolidColorBrush(Color.Parse("#D8A03D"));
    private static readonly IBrush AmberLit = new SolidColorBrush(Color.Parse("#F0C060"));
    private static readonly IBrush TealB = new SolidColorBrush(Color.Parse("#5B9E9C"));
    private static readonly IBrush Txt = new SolidColorBrush(Color.Parse("#E9E4D8"));
    private static readonly IBrush MutedB = new SolidColorBrush(Color.Parse("#6E6A5E"));
    private static readonly IBrush Sub = new SolidColorBrush(Color.Parse("#A39D8F"));
    private static readonly IBrush Red = new SolidColorBrush(Color.Parse("#D95F4C"));
    private static readonly IBrush Hue = new SolidColorBrush(Color.Parse("#3A362D"));
    private static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#171613"));

    public double Width => 700;
    public bool FullBleed => true;

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId, di = index;
        float P(int p) => engine.DeviceGetParam(track, di, p);
        void SetP(int p, float v) => engine.DeviceSetParam(track, di, p, v);
        void Begin(int p) => engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void End(int p) => engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        static double Exp(double v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0, 1));
        static TextBlock Mono(string t, IBrush c, double fs = 9) { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.Mono"); return tb; }
        static TextBlock Cap(string t, IBrush? c = null) => new() { Text = t, FontSize = 8, FontWeight = FontWeight.Bold, Foreground = c ?? MutedB, VerticalAlignment = VerticalAlignment.Center };
        static string NoteName(int m) => Keys[((m % 12) + 12) % 12] + (m / 12 - 1);

        int KeyOf() => Math.Clamp((int)Math.Round(P(Key) * 11), 0, 11);
        int ScaleOf() => Math.Clamp((int)Math.Round(P(Scale) * 4), 0, 4);
        int SnapMidi(double m, int key, int mask)
        {
            int b = (int)Math.Round(m);
            for (int d = 0; d <= 6; d++)
                foreach (int s in d == 0 ? new[] { 0 } : new[] { d, -d })
                { int note = b + s, pc = ((note - key) % 12 + 12) % 12; if ((mask >> pc & 1) != 0) return note; }
            return b;
        }

        // ---- pitch trace + live telemetry ----
        var viz = new AutoShiftViz { VerticalAlignment = VerticalAlignment.Stretch };
        var scope = new float[1024];
        var det = new float[512];
        var corr = new float[512];

        var noteBig = new TextBlock { Text = "—", FontSize = 15, FontWeight = FontWeight.Bold, Foreground = AmberLit, VerticalAlignment = VerticalAlignment.Bottom };
        var centsTb = Mono("", Sub); centsTb.VerticalAlignment = VerticalAlignment.Bottom;
        var corrTb = new TextBlock { Text = "", FontSize = 8, Foreground = MutedB, VerticalAlignment = VerticalAlignment.Center };
        var keyScaleTb = Mono("C Major", Txt);

        // bipolar cents meter (fixed 96×8)
        var mCenter = new Border { Width = 1, Background = Hue, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch };
        var mBand = new Border { Background = new SolidColorBrush(Color.FromArgb(0x59, 0xD9, 0x5F, 0x4C)), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Stretch };
        var mMark = new Border { Width = 2, Background = Red, CornerRadius = new CornerRadius(1), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Stretch, Margin = new Thickness(0, 1) };
        var centsMeter = new Panel { Width = 96, Height = 8, Children = { new Border { Background = Inset, CornerRadius = new CornerRadius(4) }, mCenter, mBand, mMark } };
        void SetMeter(double cents)
        {
            cents = Math.Clamp(cents, -50, 50);
            double mx = 48 + cents / 50.0 * 48.0, lo = Math.Min(48, mx);
            mBand.Margin = new Thickness(lo, 0, 0, 0); mBand.Width = Math.Max(1, Math.Abs(mx - 48));
            mMark.Margin = new Thickness(Math.Clamp(mx - 1, 0, 94), 1, 0, 1);
        }

        int AutoKey(int mask)
        {
            var ch = new double[12]; int voiced = 0;
            for (int k = 0; k < det.Length; k++) { float v = det[k]; if (v > 1e-4f) { ch[((int)Math.Round(v * 127) % 12 + 12) % 12] += 1; voiced++; } }
            if (voiced < 12) return -1;
            int best = 0; double bestE = -1;
            for (int key = 0; key < 12; key++) { double e = 0; for (int pc = 0; pc < 12; pc++) if ((mask >> (((pc - key) % 12 + 12) % 12) & 1) != 0) e += ch[pc]; if (e > bestE) { bestE = e; best = key; } }
            return best;
        }
        int FindScaleDevice()
        {
            int n = engine.TrackMidiEffectCount(track);
            for (int i = 0; i < n; i++) if (engine.MidiEffectName(track, i) == "Nota Scale") return i;
            return -1;
        }

        void Tick()
        {
            int cnt = engine.DeviceScope(track, di, scope, scope.Length);
            int pairs = cnt / 2;
            for (int k = 0; k < pairs; k++) { det[k] = scope[2 * k]; corr[k] = scope[2 * k + 1]; }
            viz.SetTraces(det, corr, pairs);

            // Auto key / Follow drive Key from the audio / a Nota Scale device.
            int src = Math.Clamp((int)Math.Round(P(KeySrc) * 2), 0, 2);
            bool follow = P(Follow) >= 0.5f;
            if (follow) { int sd = FindScaleDevice(); if (sd >= 0) { float root = engine.MidiEffectGetParam(track, sd, 0); if (Math.Abs(root - P(Key)) > 1e-3) SetP(Key, root); } }
            else if (src == 0) { int ak = AutoKey(Masks[ScaleOf()]); if (ak >= 0 && ak != KeyOf()) SetP(Key, ak / 11f); }

            // LIVE readout from the instantaneous detected pitch.
            double detNorm = engine.DeviceGainReduction(track, di);
            if (detNorm > 1e-4)
            {
                double dm = detNorm * 127.0;
                int snapped = SnapMidi(dm, KeyOf(), Masks[ScaleOf()]);
                double cents = (dm - snapped) * 100.0;
                noteBig.Text = NoteName(snapped); noteBig.Foreground = AmberLit;
                centsTb.Text = $"{cents:+0;-0;0} ¢";
                corrTb.Text = $"→ corrected to {NoteName(snapped)}";
                SetMeter(cents);
            }
            else { noteBig.Text = "—"; noteBig.Foreground = MutedB; centsTb.Text = ""; corrTb.Text = ""; SetMeter(0); }

            keyScaleTb.Text = $"{Keys[KeyOf()]} {Scales[ScaleOf()]}";
        }
        ctx.AddDeviceRefresher(Tick);

        // ---- horizontal slider (label · track · value) ----
        Control Slider(string label, int p, Func<double, string> fmt, IBrush fill, bool bipolar = false)
        {
            var lbl = new TextBlock { Text = label, FontSize = 8, FontWeight = FontWeight.Bold, Foreground = MutedB, Width = 46, VerticalAlignment = VerticalAlignment.Center };
            var val = Mono(fmt(P(p)), Txt); val.Width = 34; val.TextAlignment = TextAlignment.Right;
            var fillBar = new Border { Height = 3, Background = fill, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var handle = new Border { Width = 8, Height = 9, Background = Sub, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var slot = new Panel { Height = 9, Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent };
            slot.Children.Add(new Border { Height = 3, Background = Inset, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center });
            if (bipolar) slot.Children.Add(new Border { Width = 1, Background = Hue, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch, Margin = new Thickness(0, 1) });
            slot.Children.Add(fillBar); slot.Children.Add(handle);
            void Vis(double v) { double W = slot.Bounds.Width; fillBar.Width = v * W; handle.Margin = new Thickness(Math.Clamp(v * W - 4, 0, Math.Max(0, W - 8)), 0, 0, 0); }
            bool drag = false;
            void SetX(double x) { double v = Math.Clamp(x / Math.Max(1, slot.Bounds.Width), 0, 1); SetP(p, (float)v); Vis(v); val.Text = fmt(v); }
            slot.PointerPressed += (_, e) => { drag = true; Begin(p); e.Pointer.Capture(slot); SetX(e.GetPosition(slot).X); };
            slot.PointerMoved += (_, e) => { if (drag) SetX(e.GetPosition(slot).X); };
            slot.PointerReleased += (_, e) => { if (drag) { drag = false; e.Pointer.Capture(null); End(p); } };
            MidiLearn.Bind(slot, MidiTarget.DeviceParam(track, di, p), label);
            ctx.AddDeviceRefresher(() => { if (!drag) { double v = P(p); Vis(v); val.Text = fmt(v); } });
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 5, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(slot, 1); Grid.SetColumn(val, 2);
            g.Children.Add(lbl); g.Children.Add(slot); g.Children.Add(val);
            return g;
        }

        // ---- segmented / toggle helpers ----
        Control Seg(string[] names, IBrush?[] accents, Func<int> get, Action<int> set)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
            var cells = new Border[names.Length]; var texts = new TextBlock[names.Length];
            void Sync() { int cur = get(); for (int i = 0; i < names.Length; i++) { bool on = i == cur; IBrush ac = accents[i] ?? Amber; cells[i].Background = on ? ac : Brushes.Transparent; texts[i].Foreground = on ? (ac == TealB ? Ink : Ink) : (accents[i] ?? MutedB); texts[i].FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal; } }
            for (int i = 0; i < names.Length; i++)
            {
                int iv = i;
                var t = new TextBlock { Text = names[i], FontSize = 9, Foreground = MutedB, HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(6, 1) };
                var c = new Border { CornerRadius = new CornerRadius(2), Cursor = new Cursor(StandardCursorType.Hand), Child = t };
                c.PointerPressed += (_, e) => { e.Handled = true; set(iv); Sync(); };
                cells[i] = c; texts[i] = t; row.Children.Add(c);
            }
            Sync(); ctx.AddDeviceRefresher(Sync);
            return new Border { Background = Inset, BorderBrush = Bd, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Child = row };
        }
        Control PillToggle(string label, int p, IBrush accent)
        {
            var knob = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = Ink, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(1.5, 0) };
            var pill = new Border { Width = 18, Height = 10, CornerRadius = new CornerRadius(5), Background = CardBg, BorderBrush = Hue, BorderThickness = new Thickness(1), Child = knob };
            var lbl = new TextBlock { Text = label, FontSize = 8, FontWeight = FontWeight.Bold, Foreground = MutedB, VerticalAlignment = VerticalAlignment.Center };
            void Sync() { bool on = P(p) >= 0.5f; pill.Background = on ? accent : CardBg; pill.BorderBrush = on ? Brushes.Transparent : Hue; knob.Background = on ? Ink : MutedB; knob.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left; lbl.Foreground = on ? accent : MutedB; }
            var b = new Border { Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent, Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = { pill, lbl } } };
            b.PointerPressed += (_, e) => { e.Handled = true; SetP(p, P(p) >= 0.5f ? 0f : 1f); Sync(); };
            Sync(); ctx.AddDeviceRefresher(Sync);
            MidiLearn.Bind(b, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            return b;
        }
        Control RailBtn(string label, Action onClick)
        {
            var b = new Border { BorderBrush = Hue, BorderThickness = new Thickness(1), Background = CardBg, CornerRadius = new CornerRadius(3), Padding = new Thickness(0, 2), Cursor = new Cursor(StandardCursorType.Hand),
                Child = new TextBlock { Text = label, FontSize = 9, Foreground = Sub, HorizontalAlignment = HorizontalAlignment.Center } };
            b.PointerPressed += (_, e) => { e.Handled = true; onClick(); };
            return b;
        }

        // ---- KEY grid (6×2) + SCALE list ----
        Control KeyGrid()
        {
            var grid = new Grid { ColumnSpacing = 3, RowSpacing = 3 };
            for (int c = 0; c < 6; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            for (int r = 0; r < 2; r++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var cells = new Border[12]; var texts = new TextBlock[12];
            void Sync() { int cur = KeyOf(); for (int i = 0; i < 12; i++) { bool on = i == cur; cells[i].Background = on ? Amber : Inset; cells[i].BorderBrush = on ? Amber : Bd; texts[i].Foreground = on ? Ink : Sub; } }
            for (int i = 0; i < 12; i++)
            {
                int iv = i;
                var t = Mono(Keys[i], Sub); t.HorizontalAlignment = HorizontalAlignment.Center;
                var c = new Border { Height = 17, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Cursor = new Cursor(StandardCursorType.Hand), Child = t };
                c.PointerPressed += (_, e) => { e.Handled = true; SetP(Key, iv / 11f); if (P(Follow) < 0.5f && Math.Round(P(KeySrc) * 2) == 0) SetP(KeySrc, 0.5f); Sync(); };
                cells[i] = c; texts[i] = t; Grid.SetColumn(c, i % 6); Grid.SetRow(c, i / 6); grid.Children.Add(c);
            }
            Sync(); ctx.AddDeviceRefresher(Sync);
            return grid;
        }
        Control ScaleList()
        {
            var col = new StackPanel { Spacing = 2 };
            var cells = new Border[Scales.Length]; var texts = new TextBlock[Scales.Length];
            void Sync() { int cur = ScaleOf(); for (int i = 0; i < Scales.Length; i++) { bool on = i == cur; cells[i].Background = on ? new SolidColorBrush(Color.FromArgb(0x30, 0xD8, 0xA0, 0x3D)) : Inset; texts[i].Foreground = on ? AmberLit : Sub; texts[i].FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal; } }
            for (int i = 0; i < Scales.Length; i++)
            {
                int iv = i;
                var t = new TextBlock { Text = Scales[i], FontSize = 9, Foreground = Sub, VerticalAlignment = VerticalAlignment.Center };
                var c = new Border { Height = 16, CornerRadius = new CornerRadius(3), Padding = new Thickness(7, 0), Cursor = new Cursor(StandardCursorType.Hand), Child = t };
                c.PointerPressed += (_, e) => { e.Handled = true; SetP(Scale, iv / (float)(Scales.Length - 1)); Sync(); };
                cells[i] = c; texts[i] = t; col.Children.Add(c);
            }
            Sync(); ctx.AddDeviceRefresher(Sync);
            return col;
        }

        // ============ LIVE strip ============
        var detGroup = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = { noteBig, centsTb } };
        var keySrcSeg = Seg(new[] { "Auto", "Manual", "MIDI in" }, new IBrush?[] { Amber, Amber, TealB }, () => Math.Clamp((int)Math.Round(P(KeySrc) * 2), 0, 2), i => SetP(KeySrc, i / 2f));
        MidiLearn.Bind(keySrcSeg, MidiTarget.DeviceParam(track, di, KeySrc), engine.DeviceParamName(track, di, KeySrc));
        var liveRight = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, [DockPanel.DockProperty] = Dock.Right, Children = { Cap("KEY"), keyScaleTb, keySrcSeg } };
        var liveLeft = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { detGroup, centsMeter, corrTb } };
        var liveStrip = new Border { Height = 34, Background = Hdr, BorderBrush = Bd, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(9, 0), Child = new DockPanel { LastChildFill = false, Children = { liveLeft, liveRight } } };

        // ============ key + scale rail (176) ============
        var follow = PillToggle("FOLLOW SCALE DEVICE", Follow, TealB);
        DockPanel.SetDock(follow, Dock.Bottom);
        var keyScaleInner = new DockPanel { LastChildFill = true, Children = { follow,
            new StackPanel { Spacing = 5, Children = { Cap("KEY"), KeyGrid(), Cap("SCALE"), ScaleList() } } } };
        var keyScalePanel = new Border { Width = 176, Background = Rail, BorderBrush = Bd, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(8, 6), Child = keyScaleInner };

        // ============ CORRECT / SHIFT rail (182) ============
        string Pct(double v) => $"{v * 100:0}%";
        var learnBypass = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 4, [DockPanel.DockProperty] = Dock.Bottom, Children = {
            RailBtn("Learn key", () => { int ak = AutoKey(Masks[ScaleOf()]); if (ak >= 0) { SetP(Key, ak / 11f); SetP(KeySrc, 0.5f); } }),
        } };
        var bypassBtn = RailBtn("Bypass", () => { bool b = engine.DeviceBypassed(track, di); engine.SetDeviceBypassed(track, di, !b); ctx.RequestRebuild(); });
        Grid.SetColumn(bypassBtn, 1); learnBypass.Children.Add(bypassBtn);
        var railTop = new StackPanel { Spacing = 4, Children = {
            Cap("CORRECT", TealB),
            Slider("Amount", Amount, Pct, TealB),
            Slider("Speed", Speed, v => $"{Exp(v, 1, 250):0} ms", TealB),
            Slider("Range", Range, v => $"±{1 + v * 11:0} st", TealB),
            new Border { Height = 1, Background = CardBg, Margin = new Thickness(0, 2) },
            Cap("SHIFT"),
            Slider("Shift", Shift, v => $"{(v - 0.5) * 24:+0;-0;0} st", Amber, bipolar: true),
            Slider("Formant", Formant, Pct, Amber),
            Slider("Mix", Mix, Pct, Amber),
        } };
        var rail = new Border { Width = 182, Background = Rail, BorderBrush = Bd, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(8, 6),
            Child = new DockPanel { LastChildFill = true, Children = { learnBypass, railTop } } };

        // ============ pitch trace (fill) ============
        var tracePanel = new Border { Padding = new Thickness(8, 6), Child = viz };

        DockPanel.SetDock(keyScalePanel, Dock.Left); DockPanel.SetDock(rail, Dock.Right);
        var body = new DockPanel { LastChildFill = true, Children = { keyScalePanel, rail, tracePanel } };
        DockPanel.SetDock(liveStrip, Dock.Top);
        Tick();
        return new DockPanel { LastChildFill = true, Children = { liveStrip, body } };
    }
}
