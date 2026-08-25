// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Detail · Devices — the track's built-in Nota Sampler card (instrument kind 1), rebuilt
// to mockup 2o (700×260 on the shared shell). A LIVE strip (Vol / Pan / Cutoff + Voice
// mode) over a body of tab rail (Sample · Pitch · Env · Filter) | tab content. The Sample
// tab is the editable waveform (start/end/loop handles, loop modes + crossfade, Snap
// zero) with an amp-envelope rail; Pitch / Env / Filter group the rest. Zones (multi-
// sample mapping) is intentionally out of scope — this is the single-sample engine.
// The editor body (BuildEditor) is reused for the track Sampler and rack-chain Samplers
// (Drum Rack pads / Instrument Rack chains) via ISamplerAccess.

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

    private static readonly IBrush HdrBg = new SolidColorBrush(Color.Parse("#1E1C18"));
    private static readonly IBrush RailBg = new SolidColorBrush(Color.Parse("#1B1916"));
    private static readonly IBrush Border2 = new SolidColorBrush(Color.Parse("#2C2923"));
    private static readonly IBrush Inset = new SolidColorBrush(Color.Parse("#100F0D"));
    private static readonly IBrush FieldBorder = new SolidColorBrush(Color.Parse("#221F1A"));
    private static readonly IBrush Amber = new SolidColorBrush(Color.Parse("#D8A03D"));
    private static readonly IBrush AmberLit = new SolidColorBrush(Color.Parse("#F0C060"));
    private static readonly IBrush TealC = new SolidColorBrush(Color.Parse("#5B9E9C"));
    private static readonly IBrush TxtC = new SolidColorBrush(Color.Parse("#E9E4D8"));
    private static readonly IBrush MutedC = new SolidColorBrush(Color.Parse("#6E6A5E"));
    private static readonly IBrush GreenC = new SolidColorBrush(Color.Parse("#58B368"));
    private static readonly IBrush RedC = new SolidColorBrush(Color.Parse("#D95F4C"));
    private static readonly IBrush AmberSubtle = new SolidColorBrush(Color.FromArgb(0x28, 0xD8, 0xA0, 0x3D));
    private static readonly IBrush RowLit = new SolidColorBrush(Color.Parse("#26231E"));
    private static readonly IBrush TealSubtle = new SolidColorBrush(Color.FromArgb(0x24, 0x5B, 0x9E, 0x9C));

    private static double Exp(double v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0, 1));

    public Control Build(DeviceCardContext ctx)
    {
        ctx.SetInstLiveViz(null);   // no live graphs — the waveform ticks via the refreshers
        return BuildEditor(ctx.Engine, new TrackSamplerAccess(ctx.Engine, ctx.TrackId), ctx.AddDeviceRefresher);
    }

    // The whole editor, reusable for the track Sampler (TrackSamplerAccess) and a rack
    // chain's Sampler (ChainSamplerAccess, in Drum Rack pads / Instrument Rack chains).
    internal static Control BuildEditor(IAudioEngine engine, ISamplerAccess acc, Action<Action> registerTick)
    {
        int pc = acc.ParamCount();
        var idx = new Dictionary<string, int>();
        for (int i = 0; i < pc; i++) idx[acc.ParamId(i)] = i;
        float G(string id) => idx.TryGetValue(id, out var i) ? acc.ParamGet(i) : 0f;
        int GI(string id, int n) => Math.Clamp((int)Math.Round(G(id) * (n - 1)), 0, n - 1);
        void SetId(string id, double v) { if (idx.TryGetValue(id, out var i)) acc.ParamSet(i, (float)v); }

        var readouts = new List<Action>();

        bool hasSample = acc.Info(out long sampleId, out int root) && sampleId != 0;
        if (!hasSample)
        {
            var prompt = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = {
                new TextBlock { Text = "↓", FontSize = 22, Foreground = MutedC, HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = "Drop a sample here", FontSize = 11, Foreground = TextSecondary, HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = "drag from the Files tab or the browser", FontSize = 9, Foreground = MutedC, HorizontalAlignment = HorizontalAlignment.Center } } };
            return new Border { Background = new SolidColorBrush(Color.Parse("#171613")), Child = prompt };
        }

        // Sample data: peaks for the waveform + raw mono for zero-crossing snapping.
        float[] raw = Array.Empty<float>(); int ch = 1; long frames = 0; double durSec = 0; double sr = 48000;
        if (engine.TryGetSampleInfo(sampleId, out var sinfo))
        {
            raw = engine.ReadSample(sampleId); ch = Math.Max(1, sinfo.Channels); frames = sinfo.Frames; sr = sinfo.SampleRate;
            durSec = sr > 0 ? frames / sr : 0;
        }
        float Mono(long f) { if (f < 0 || f >= frames) return 0; float s = 0; for (int c = 0; c < ch; c++) s += raw[f * ch + c]; return s / ch; }

        // ---- waveform + markers + live playhead ----
        var wave = new SamplerWaveform { Height = 84 };
        wave.SetPeaks(BuildPeaks(raw, ch, frames, 600));
        if (durSec > 0) wave.SetDuration(durSec);
        void SyncWave() => wave.SetMarkers(G("start"), G("end"), G("loopstart"), G("loopend"), (int)Math.Round(G("loopmode") * 2));
        wave.StartChanged += f => { SetId("start", f); SyncWave(); };
        wave.EndChanged += f => { SetId("end", f); SyncWave(); };
        wave.LoopStartChanged += f => { SetId("loopstart", f); SyncWave(); };
        wave.LoopEndChanged += f => { SetId("loopend", f); SyncWave(); };
        wave.GestureBegin = () => acc.BeginGesture("start");
        SyncWave();

        bool IsZeroCross(long c) => c >= 1 && c < frames && ((Mono(c - 1) <= 0 && Mono(c) >= 0) || (Mono(c - 1) >= 0 && Mono(c) <= 0));
        long SnapZero(double p)
        {
            long target = (long)Math.Round(Math.Clamp(p, 0, 1) * (frames - 1));
            if (frames < 2) return target;
            for (long d = 0; d < frames; d++)
            {
                if (IsZeroCross(target - d)) return target - d;
                if (IsZeroCross(target + d)) return target + d;
            }
            return target;
        }

        // ---- shared builders ----
        Control Cap(string t, IBrush? c = null) => new TextBlock { Text = t, FontSize = 8, FontWeight = FontWeight.Bold, Foreground = c ?? MutedC, VerticalAlignment = VerticalAlignment.Center };
        TextBlock MonoTx(string t, IBrush c, double fs = 9) { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.Mono"); return tb; }

        Control Seg(string id, string[] names, double fs = 9, double padX = 6)
        {
            int n = names.Length; var arr = new Border[n];
            void Hi() { int cur = GI(id, n); for (int i = 0; i < n; i++) { bool on = i == cur; arr[i].Background = on ? AmberSubtle : Brushes.Transparent; arr[i].BorderBrush = on ? Amber : Brushes.Transparent; ((TextBlock)arr[i].Child!).Foreground = on ? AmberLit : MutedC; } }
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
            for (int i = 0; i < n; i++) { int iv = i; var c = new Border { CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), Padding = new Thickness(padX, 1), Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = names[i], FontSize = fs, Foreground = MutedC } }; c.PointerPressed += (_, _) => { SetId(id, n > 1 ? iv / (double)(n - 1) : 0); RefreshAll(); }; arr[i] = c; row.Children.Add(c); }
            readouts.Add(Hi);
            return new Border { Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Child = row };
        }

        // Loop segmented (Off/Fwd/Ping/Rev) — maps to loopmode + reverse (append-safe).
        Control LoopSeg()
        {
            var names = new[] { "Off", "Fwd", "Ping", "Rev" }; var arr = new Border[4];
            int Cur() { int lm = (int)Math.Round(G("loopmode") * 2); bool rev = G("reverse") > 0.5f; return lm == 2 ? 2 : lm == 1 ? (rev ? 3 : 1) : 0; }
            void Set(int i) { SetId("loopmode", i == 2 ? 1.0 : i == 0 ? 0.0 : 0.5); SetId("reverse", i == 3 ? 1 : 0); }
            void Hi() { int cur = Cur(); for (int i = 0; i < 4; i++) { bool on = i == cur; arr[i].Background = on ? AmberSubtle : Brushes.Transparent; arr[i].BorderBrush = on ? Amber : Brushes.Transparent; ((TextBlock)arr[i].Child!).Foreground = on ? AmberLit : MutedC; } }
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
            for (int i = 0; i < 4; i++) { int iv = i; var c = new Border { CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), Padding = new Thickness(7, 1), Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = names[i], FontSize = 9, Foreground = MutedC } }; c.PointerPressed += (_, _) => { Set(iv); SyncWave(); RefreshAll(); }; arr[i] = c; row.Children.Add(c); }
            readouts.Add(Hi);
            return new Border { Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Child = row };
        }

        Control OutToggle(string id, string label, bool teal = false)
        {
            var b = new Border { CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Padding = new Thickness(7, 2), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = label, FontSize = 8, FontWeight = FontWeight.Bold } };
            void Hi() { bool on = G(id) > 0.5f; b.Background = on ? (teal ? TealSubtle : AmberSubtle) : Brushes.Transparent; b.BorderBrush = on ? (teal ? TealC : Amber) : Border2; ((TextBlock)b.Child!).Foreground = on ? (teal ? TealC : AmberLit) : MutedC; }
            b.PointerPressed += (_, _) => { SetId(id, G(id) > 0.5f ? 0 : 1); RefreshAll(); };
            readouts.Add(Hi);
            return b;
        }

        Control ActionBtn(string label, Action click)
        {
            var b = new Border { CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.Parse("#3A362D")), Background = RowLit, Padding = new Thickness(8, 2), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = label, FontSize = 9, Foreground = new SolidColorBrush(Color.Parse("#A39D8F")) } };
            b.PointerPressed += (_, _) => click();
            return b;
        }

        Control KUnit(string id, string name, Func<double, string> fmt, bool mod = false, double size = 36, double cellW = 58)
        {
            if (!idx.TryGetValue(id, out var i)) return new Panel();
            var value = MonoTx(fmt(G(id)), TxtC, 9); value.HorizontalAlignment = HorizontalAlignment.Center;
            var knob = new Knob(G(id), 1.0) { Accent = true, ArcColor = mod ? Teal : null, Default = acc.ParamDefault(i), Width = size, Height = size };
            knob.ValueChanged += v => { acc.ParamSet(i, (float)v); value.Text = fmt(v); RefreshAll(); };
            knob.GestureBegin += () => acc.BeginGesture(id);
            knob.GestureEnd += () => acc.EndGesture(id);
            readouts.Add(() => { if (!knob.Dragging) { double v = G(id); if (Math.Abs(v - knob.Value) > 1e-3) knob.Value = v; value.Text = fmt(v); } });
            return KnobCell(name, knob, value, cellW);
        }

        Control HSlider(string id, string label, Func<double, string> fmt, bool bipolar = false, double lw = 44, double vw = 52)
        {
            if (!idx.TryGetValue(id, out var pi)) return new Panel();
            var fill = new Border { Height = 3, Background = Amber, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var track2 = new Border { Height = 3, Background = Inset, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center };
            var center = bipolar ? new Border { Width = 1, Background = new SolidColorBrush(Color.Parse("#3A362D")), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch, Margin = new Thickness(0, 1) } : null;
            var handle = new Border { Width = 8, Height = 10, Background = new SolidColorBrush(Color.Parse("#A39D8F")), CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var slot = new Panel { Height = 11, MinWidth = 46 }; slot.Children.Add(track2); if (center != null) slot.Children.Add(center); slot.Children.Add(fill); slot.Children.Add(handle);
            var val = MonoTx(fmt(G(id)), TxtC, 9); val.Width = vw; val.TextAlignment = TextAlignment.Right;
            bool drag = false;
            void Upd() { double v = G(id); double W = slot.Bounds.Width; double hx = v * W; handle.Margin = new Thickness(Math.Clamp(hx - 4, 0, Math.Max(0, W - 8)), 0, 0, 0); if (bipolar) { double c = W * 0.5; double a = Math.Min(c, hx), b = Math.Max(c, hx); fill.Margin = new Thickness(a, 0, 0, 0); fill.Width = Math.Max(0, b - a); } else { fill.Margin = new Thickness(0); fill.Width = hx; } val.Text = fmt(v); }
            void SetFromX(double x) { double v = Math.Clamp(x / Math.Max(1, slot.Bounds.Width), 0, 1); acc.ParamSet(pi, (float)v); Upd(); }
            slot.PointerPressed += (_, e) => { drag = true; e.Pointer.Capture(slot); acc.BeginGesture(id); SetFromX(e.GetPosition(slot).X); RefreshAll(); };
            slot.PointerMoved += (_, e) => { if (drag) SetFromX(e.GetPosition(slot).X); };
            slot.PointerReleased += (_, e) => { if (drag) { drag = false; e.Pointer.Capture(null); acc.EndGesture(id); } };
            readouts.Add(() => { if (!drag) Upd(); });
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 5, VerticalAlignment = VerticalAlignment.Center };
            var lbl = Cap(label); ((TextBlock)lbl).Width = lw;
            g.Children.Add(lbl); Grid.SetColumn(slot, 1); g.Children.Add(slot); Grid.SetColumn(val, 2); g.Children.Add(val);
            return g;
        }

        int rootv = root;
        Control RootStepper()
        {
            var lbl = MonoTx(NoteName(rootv), TxtC, 10); lbl.Width = 26; lbl.TextAlignment = TextAlignment.Center;
            Border Btn(string t, int d) { var b = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.Parse("#3A362D")), Background = RowLit, Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = t, FontSize = 9, Foreground = new SolidColorBrush(Color.Parse("#A39D8F")), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } }; b.PointerPressed += (_, _) => { rootv = Math.Clamp(rootv + d, 0, 127); acc.SetRoot(rootv); lbl.Text = NoteName(rootv); RefreshAll(); }; return b; }
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Children = { Btn("−", -1), lbl, Btn("+", +1) } };
        }

        // ---- formatters (real units) ----
        string Db(double v) => v <= 0.0011 ? "−∞ dB" : $"{20 * Math.Log10(v):+0.0;−0.0;0.0} dB";
        string Pan(double v) { double p = (v - 0.5) * 2; return Math.Abs(p) < 0.03 ? "C" : p < 0 ? $"L{Math.Abs(p) * 100:0}" : $"R{p * 100:0}"; }
        string Hz(double v) { double fc = Exp(v, 20, 20000); return fc >= 1000 ? $"{fc / 1000:0.0} kHz" : $"{fc:0} Hz"; }
        string St(double v) => $"{(v - 0.5) * 48:+0;-0;0} st";
        string Cent(double v) => $"{(v - 0.5) * 100:+0;-0;0} c";
        string Ms(double v, double lo, double hi) { double s = Exp(v, lo, hi); return s < 1 ? $"{s * 1000:0} ms" : $"{s:0.00} s"; }
        string SusDb(double v) => v <= 0.0011 ? "−∞" : $"{20 * Math.Log10(v):0.0} dB";
        string Pct(double v) => $"{v * 100:0}%";

        // ================= LIVE strip =================
        var voiceSeg = Seg("voicemode", new[] { "Poly 16", "Mono", "Choke" }, 9, 7);
        var live = new Border { Height = 34, Background = HdrBg, BorderBrush = Border2, BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new DockPanel { LastChildFill = false, Margin = new Thickness(9, 0), Children = {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center, Children = {
                    Cap("LIVE", MutedC),
                    HSlider("volume", "VOL", Db, lw: 26, vw: 52),
                    HSlider("pan", "PAN", Pan, bipolar: true, lw: 26, vw: 26),
                    HSlider("cutoff", "CUTOFF", Hz, lw: 40, vw: 52) } },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, [DockPanel.DockProperty] = Dock.Right, Children = {
                    Cap("VOICES"), voiceSeg } } } } };

        // ================= tab rail =================
        var tabNames = new[] { "Sample", "Pitch", "Env", "Filter" };
        var tabBtns = new Border[tabNames.Length];
        var bodyContent = new ContentControl { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        Control[] pages = new Control[tabNames.Length];
        void SelectTab(int t)
        {
            bodyContent.Content = pages[t];
            for (int i = 0; i < tabBtns.Length; i++) { bool on = i == t; tabBtns[i].Background = on ? RowLit : Brushes.Transparent; tabBtns[i].BorderBrush = on ? Amber : Brushes.Transparent; ((TextBlock)tabBtns[i].Child!).Foreground = on ? TxtC : MutedC; }
        }
        var railCol = new StackPanel { Spacing = 2 };
        for (int i = 0; i < tabNames.Length; i++) { int ti = i; var b = new Border { Height = 20, CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 0), BorderThickness = new Thickness(2, 0, 0, 0), Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = tabNames[i], FontSize = 10, FontWeight = FontWeight.Medium, Foreground = MutedC, VerticalAlignment = VerticalAlignment.Center } }; b.PointerPressed += (_, _) => SelectTab(ti); tabBtns[i] = b; railCol.Children.Add(b); }
        var tabRail = new Border { Width = 70, Background = RailBg, BorderBrush = Border2, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(5), Child = railCol };

        // ================= Sample tab =================
        var loopReadout = MonoTx("", AmberLit, 8);
        DockPanel.SetDock(loopReadout, Dock.Right);
        var fileHdr = new DockPanel { LastChildFill = false, Height = 12, Children = {
            MonoTx(sinfoLabel(sr, durSec, rootv), MutedC, 8), loopReadout } };
        readouts.Add(() => loopReadout.Text = LoopReadout(G("loopstart"), G("loopend"), durSec, (int)Math.Round(G("loopmode") * 2)));
        var startFoot = MonoTx("start 0.00", GreenC, 8);
        var endFoot = MonoTx("end 0.00", RedC, 8); endFoot.HorizontalAlignment = HorizontalAlignment.Right;
        readouts.Add(() => { startFoot.Text = $"start {G("start") * durSec:0.00}"; endFoot.Text = $"end {G("end") * durSec:0.00}"; });
        var waveFoot = new DockPanel { LastChildFill = false, Height = 11, Children = { startFoot, endFoot } };
        DockPanel.SetDock(endFoot, Dock.Right);
        var waveIsland = new Border { Background = Inset, BorderBrush = FieldBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 4), Child =
            new DockPanel { LastChildFill = true, Children = { WithDock(fileHdr, Dock.Top), WithDock(waveFoot, Dock.Bottom), wave } } };
        var loopRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Height = 40, Children = {
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = { Cap("LOOP"), LoopSeg() } },
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Width = 150, Children = { Cap("XFADE"), HSlider("loopxfade", "", v => $"{v * 200:0} ms", lw: 0, vw: 46) } },
            ActionSnap() } };
        Control ActionSnap()
        {
            var b = ActionBtn("Snap zero", () => { foreach (var id in new[] { "start", "end", "loopstart", "loopend" }) SetId(id, SnapZero(G(id)) / (double)Math.Max(1, frames - 1)); SyncWave(); RefreshAll(); });
            ((Border)b).HorizontalAlignment = HorizontalAlignment.Right;
            return new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { b } };
        }
        var sampleCenter = new DockPanel { LastChildFill = true, Margin = new Thickness(8, 7), Children = { WithDock(loopRow, Dock.Bottom), waveIsland } };
        var ampRail = BuildAmpRail(G, OutToggle, readouts, Ms, SusDb);
        DockPanel.SetDock(ampRail, Dock.Right);
        pages[0] = new DockPanel { LastChildFill = true, Children = { ampRail, sampleCenter } };

        // ================= Pitch tab =================
        var keys = new SamplerKeys();
        readouts.Add(() => keys.Set(rootv, (G("transpose") - 0.5) * 48));
        var pitchKnobs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = {
            KUnit("transpose", "TRANSPOSE", St), KUnit("detune", "DETUNE", Cent, mod: true) } };
        var rootRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = { Cap("ROOT"), RootStepper() } };
        pages[1] = new DockPanel { LastChildFill = true, Margin = new Thickness(10, 8), Children = {
            WithDock(new Border { Height = 40, Background = Inset, BorderBrush = FieldBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(4), Child = keys, Margin = new Thickness(0, 6, 0, 0) }, Dock.Bottom),
            WithDock(rootRow, Dock.Bottom),
            pitchKnobs } };

        // ================= Env tab =================
        var envBig = new SamplerEnv { Height = 96 };
        readouts.Add(() => envBig.Set(G("attack"), G("decay"), G("sustain"), G("release")));
        var envKnobs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, Children = {
            KUnit("attack", "ATTACK", v => Ms(v, 0.0005, 4.0)), KUnit("decay", "DECAY", v => Ms(v, 0.002, 6.0)),
            KUnit("sustain", "SUSTAIN", SusDb), KUnit("release", "RELEASE", v => Ms(v, 0.002, 6.0)) } };
        pages[2] = new DockPanel { LastChildFill = true, Margin = new Thickness(10, 8), Children = {
            WithDock(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0), [DockPanel.DockProperty] = Dock.Bottom, Children = { envKnobs, OutToggle("velamount", "VEL→VOL", teal: true) } }, Dock.Bottom),
            new Border { Background = Inset, BorderBrush = FieldBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(6), Child = envBig } } };

        // ================= Filter tab =================
        var filtViz = new SamplerFilter { Height = 92 };
        readouts.Add(() => filtViz.Set(GI("filtertype", 4), G("cutoff"), G("resonance")));
        var filtKnobs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, Children = {
            KUnit("cutoff", "CUTOFF", Hz), KUnit("resonance", "RESO", Pct), KUnit("keytrack", "KEY TRACK", Pct, mod: true) } };
        pages[3] = new DockPanel { LastChildFill = true, Margin = new Thickness(10, 8), Children = {
            WithDock(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, HorizontalAlignment = HorizontalAlignment.Center, Children = { Cap("TYPE"), Seg("filtertype", new[] { "Off", "LP", "HP", "BP" }, 9, 8) } }, Dock.Top),
            WithDock(filtKnobs, Dock.Bottom),
            new Border { Background = Inset, BorderBrush = FieldBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(6), Margin = new Thickness(0, 5), Child = filtViz } } };

        // ================= assemble =================
        DockPanel.SetDock(tabRail, Dock.Left);
        var body = new DockPanel { LastChildFill = true, Children = { tabRail, bodyContent } };
        DockPanel.SetDock(live, Dock.Top);
        var root2 = new DockPanel { LastChildFill = true, Background = new SolidColorBrush(Color.Parse("#171613")), Children = { live, body } };

        void RefreshAll() { foreach (var a in readouts) a(); }
        registerTick(() => { wave.SetPlayhead(acc.PlayPosition()); RefreshAll(); });
        SelectTab(0);
        RefreshAll();
        return root2;
    }

    // Amp-envelope rail (Sample tab): teal label + mini curve + A/D/S/R readouts + Vel→Vol.
    private static Control BuildAmpRail(Func<string, float> G, Func<string, string, bool, Control> OutToggle, List<Action> readouts,
        Func<double, double, double, string> Ms, Func<double, string> SusDb)
    {
        var env = new SamplerEnv { Height = 42 };
        readouts.Add(() => env.Set(G("attack"), G("decay"), G("sustain"), G("release")));
        Control Row(string label, Func<double> get, Func<double, string> fmt)
        {
            var v = new TextBlock { FontSize = 9, Foreground = TxtC, VerticalAlignment = VerticalAlignment.Center, [DockPanel.DockProperty] = Dock.Right };
            v.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
            readouts.Add(() => v.Text = fmt(get()));
            return new DockPanel { LastChildFill = false, Children = { new TextBlock { Text = label, FontSize = 8, FontWeight = FontWeight.Bold, Foreground = MutedC, VerticalAlignment = VerticalAlignment.Center }, v } };
        }
        return new Border { Width = 104, Background = RailBg, BorderBrush = Border2, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(8, 6), Child =
            new DockPanel { LastChildFill = false, Children = {
                WithDock(new StackPanel { Spacing = 5, Children = {
                    new TextBlock { Text = "AMP ENV", FontSize = 8, FontWeight = FontWeight.Bold, Foreground = TealC },
                    new Border { Height = 42, Background = Inset, BorderBrush = FieldBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(2), Child = env },
                    Row("ATK", () => G("attack"), v => Ms(v, 0.0005, 4.0)),
                    Row("DEC", () => G("decay"), v => Ms(v, 0.002, 6.0)),
                    Row("SUS", () => G("sustain"), SusDb),
                    Row("REL", () => G("release"), v => Ms(v, 0.002, 6.0)) } }, Dock.Top),
                WithDock(OutToggle("velamount", "VEL → VOL", true), Dock.Bottom) } } };
    }

    private static Control WithDock(Control c, Dock d) { DockPanel.SetDock(c, d); return c; }

    private static string sinfoLabel(double sr, double dur, int root) => $"{sr / 1000:0.#} k · {dur:0.00} s · root {NoteName(root)}";

    private static string LoopReadout(double ls, double le, double dur, int mode) => mode <= 0 ? "no loop" : $"loop {ls * dur:0.00} – {le * dur:0.00} s";

    // Downsample interleaved float samples to min/max pairs per bucket (mono sum).
    private static float[] BuildPeaks(float[] samples, int channels, long frames, int buckets)
    {
        var peaks = new float[buckets * 2];
        if (samples.Length == 0 || channels <= 0) return peaks;
        long fr = samples.Length / channels;
        for (int b = 0; b < buckets; b++)
        {
            long a = b * fr / buckets, e = (b + 1) * fr / buckets;
            if (e <= a) e = Math.Min(fr, a + 1);
            float mn = 1f, mx = -1f;
            for (long f = a; f < e; f++) { float s = 0; for (int c = 0; c < channels; c++) s += samples[f * channels + c]; s /= channels; if (s < mn) mn = s; if (s > mx) mx = s; }
            if (mn > mx) { mn = mx = 0; }
            peaks[b * 2] = mn; peaks[b * 2 + 1] = mx;
        }
        return peaks;
    }
}

// ---- amp-envelope schematic (ADSR from normalized param values) ----
internal sealed class SamplerEnv : Control
{
    private static readonly IBrush Teal = new SolidColorBrush(Color.Parse("#5B9E9C"));
    private static readonly IBrush TealFill = new SolidColorBrush(Color.FromArgb(0x1F, 0x5B, 0x9E, 0x9C));
    private double _a, _d, _s = 1, _r;
    public void Set(double a, double d, double s, double r) { _a = a; _d = d; _s = s; _r = r; InvalidateVisual(); }
    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height; if (w <= 2 || h <= 2) return;
        double pad = 2, x0 = pad, x1 = w - pad, top = pad, bot = h - pad;
        double aw = (0.05 + 0.30 * _a) * (x1 - x0), dw = (0.05 + 0.30 * _d) * (x1 - x0), rw = (0.05 + 0.30 * _r) * (x1 - x0);
        double sy = top + (1 - _s) * (bot - top);
        double pA = x0 + aw, pD = pA + dw, pR = x1 - rw;
        if (pR < pD) pR = pD;
        var pts = new[] { new Point(x0, bot), new Point(pA, top), new Point(pD, sy), new Point(pR, sy), new Point(x1, bot) };
        var fill = new StreamGeometry();
        using (var g = fill.Open()) { g.BeginFigure(new Point(x0, bot), true); foreach (var p in pts) g.LineTo(p); g.EndFigure(true); }
        ctx.DrawGeometry(TealFill, null, fill);
        var pen = new Pen(Teal, 1.5, lineJoin: PenLineJoin.Round);
        for (int i = 1; i < pts.Length; i++) ctx.DrawLine(pen, pts[i - 1], pts[i]);
    }
}

// ---- filter magnitude response schematic ----
internal sealed class SamplerFilter : Control
{
    private static readonly IBrush Amber = new SolidColorBrush(Color.Parse("#D8A03D"));
    private static readonly IBrush Fill = new SolidColorBrush(Color.FromArgb(0x1C, 0xD8, 0xA0, 0x3D));
    private static readonly IBrush Grid = new SolidColorBrush(Color.FromArgb(0x40, 0x3A, 0x36, 0x2D));
    private int _type; private double _cut = 1, _reso;
    public void Set(int type, double cut, double reso) { _type = type; _cut = cut; _reso = reso; InvalidateVisual(); }
    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height; if (w <= 2 || h <= 2) return;
        for (int i = 1; i < 4; i++) { double gx = i / 4.0 * w; ctx.DrawLine(new Pen(Grid, 1), new Point(gx, 0), new Point(gx, h)); }
        double cx = Math.Clamp(_cut, 0.02, 0.98) * w, mid = h * 0.55, peak = mid - (h * 0.42) * (0.3 + _reso);
        var pts = new List<Point>();
        for (int i = 0; i <= 60; i++)
        {
            double x = (double)i / 60; double px = x * w; double mag;
            double d = (x - _cut) * 6.0;
            switch (_type)
            {
                case 1: mag = x <= _cut ? 1.0 : 1.0 / (1.0 + d * d); break;               // LP
                case 2: mag = x >= _cut ? 1.0 : 1.0 / (1.0 + d * d); break;               // HP
                case 3: mag = 1.0 / (1.0 + d * d * 2.0); break;                            // BP
                default: mag = 1.0; break;                                                 // Off (flat)
            }
            double reBump = (_type > 0 && Math.Abs(px - cx) < w * 0.06) ? _reso * 0.5 : 0;
            double y = mid - (mag + reBump) * (mid - (h * 0.12));
            pts.Add(new Point(px, Math.Clamp(y, 2, h - 2)));
        }
        var fill = new StreamGeometry();
        using (var g = fill.Open()) { g.BeginFigure(new Point(0, h), true); foreach (var p in pts) g.LineTo(p); g.LineTo(new Point(w, h)); g.EndFigure(true); }
        ctx.DrawGeometry(Fill, null, fill);
        var pen = new Pen(Amber, 1.5, lineJoin: PenLineJoin.Round);
        for (int i = 1; i < pts.Count; i++) ctx.DrawLine(pen, pts[i - 1], pts[i]);
        if (_type > 0) ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0x66, 0xF0, 0xC0, 0x60)), 1) { DashStyle = DashStyle.Dash }, new Point(cx, 0), new Point(cx, h));
    }
}

// ---- mini keyboard marking the sampler's root (+ transposed pitch) ----
internal sealed class SamplerKeys : Control
{
    private static readonly IBrush White = new SolidColorBrush(Color.Parse("#2A2721"));
    private static readonly IBrush Black = new SolidColorBrush(Color.Parse("#151310"));
    private static readonly IBrush RootB = new SolidColorBrush(Color.Parse("#D8A03D"));
    private static readonly IBrush TransB = new SolidColorBrush(Color.FromArgb(0x88, 0x5B, 0x9E, 0x9C));
    private int _root = 60; private double _transpose;
    public void Set(int root, double transpose) { _root = root; _transpose = transpose; InvalidateVisual(); }
    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height; if (w <= 2 || h <= 2) return;
        int lo = _root - 12, hi = _root + 12; int n = hi - lo + 1; double kw = w / n;
        int played = (int)Math.Round(_root + _transpose);
        for (int i = 0; i < n; i++)
        {
            int note = lo + i; int pc = ((note % 12) + 12) % 12;
            bool blackKey = pc is 1 or 3 or 6 or 8 or 10;
            IBrush b = note == _root ? RootB : note == played ? TransB : blackKey ? Black : White;
            ctx.FillRectangle(b, new Rect(i * kw + 0.5, 2, kw - 1, h - 4));
        }
    }
}
