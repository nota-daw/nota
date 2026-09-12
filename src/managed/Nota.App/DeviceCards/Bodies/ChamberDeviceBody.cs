// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Chamber body (device kind 20), a build of the "Nota Chamber"
// mockup (700 × 260) on the Monolith / Pentad frame: an always-visible BLEND column (the
// convolution ⇄ algorithm split and Dry/Wet), a centre tabbed panel (Convolution / Algorithm /
// EQ · Mod), a right tabbed panel (Levels / Output) and a status strip. The IR view, the
// decay-per-band graph and the tail-EQ curve are controls (drag trims, RT60 markers, EQ
// handles); an audio file dropped on the IR view (or picked with Load…) becomes the user IR.
// Every control is a device param, so automation / MIDI learn / presets / A-B / persistence
// come for free. FullBleed — the shared shell draws the header (name · preset · A/B · meter).

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Nota.Application;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed class ChamberDeviceBody : IDeviceBody
{
    // ── Parameter indices (must match Chamber.h) ─────────────────────────────
    private const int Blend = 0, DryWet = 1, ConvOn = 2, AlgoOn = 3, DryLevel = 4, Routing = 5,
        IrSelect = 6, IrStart = 7, IrDecay = 8, IrAttack = 9, IrSize = 10, ConvPredelay = 11, ConvSync = 12, IrReverse = 13, IrTrueStereo = 14,
        AlgoMode = 15, AlgoDecay = 16, AlgoSize = 17, AlgoDiffusion = 18, AlgoDamping = 19, AlgoPredelay = 20, AlgoSync = 21, AlgoLowDecay = 22, AlgoHighDecay = 23,
        Freeze = 24, FreezeIn = 25, AlgoVintage = 26, ModRate = 27, ModDepth = 28, ShimmerAmount = 29, ShimmerPitch = 30, ShimmerFeedback = 31,
        EqLowCut = 32, EqLowGain = 33, EqHighShelf = 34, EqHighCut = 35, EqPosition = 36,
        DuckAmount = 37, DuckRelease = 38, Output = 39, WidthP = 40, BassMono = 41, Quality = 42, WetOnly = 43, ZeroLatency = 44;
    // Scope telemetry layout (Chamber::S_*).
    private const int S_WetL = 0, S_OutL = 2, S_OutR = 3, S_DuckDb = 4, S_Cpu = 5, S_Latency = 6, S_SampleRate = 7, S_Building = 8,
        S_IrSeconds = 9, S_IrRate = 10, S_IrChannels = 11, S_KernelSeconds = 12, S_UserIr = 16,
        S_ResultSeconds = 19, S_PreviewSeconds = 20, S_PreviewGen = 21, kScope = 22;
    private const int IrCount = 17;   // 16 built-ins + the user slot (normalized 1.0)

    private static readonly string[] Modes = { "Dark Hall", "Plate", "Quartz", "Shimmer" };
    private static readonly string[] SyncNames = { "1/64", "1/32T", "1/32", "1/16T", "1/16", "1/8T", "1/16.", "1/8", "1/4T", "1/8.", "1/4", "1/4.", "1/2" };
    // Fallback for the built-in IR catalogue when the engine can't be asked (a rack chain's
    // proxy); mirrors chamber::irSpec in Chamber.h — the engine list wins when present.
    private static readonly (string Name, string Cat, string Len)[] IrFallback =
    {
        ("Concert Hall · Wide", "HALL", "2.6"), ("Stone Vault", "HALL", "4.4"), ("Cathedral", "HALL", "7.5"), ("Scoring Stage", "HALL", "2.3"),
        ("Wood Chamber", "ROOM", "1.6"), ("Live Room", "ROOM", "1.0"), ("Drum Room", "ROOM", "0.8"), ("Tiled Bathroom", "ROOM", "1.3"),
        ("Vocal Plate", "PLATE", "2.8"), ("Bright Plate", "PLATE", "2.0"), ("Spring Tank", "SPRING", "2.6"), ("Car Park", "SPACE", "3.6"),
        ("Stairwell", "SPACE", "3.2"), ("Forest Clearing", "OUTDOOR", "2.0"), ("Gated Room", "FX", "0.7"), ("Metal Tank", "FX", "3.4"),
    };

    private static readonly IBrush RailBg = NotaPalette.SurfaceInset;
    private static readonly IBrush Panel = NotaPalette.BgApp;
    private static readonly IBrush Border2 = NotaPalette.BorderDefault;
    private static readonly IBrush BorderIn = NotaPalette.GraphBorder;
    private static readonly IBrush Inset = NotaPalette.BgSunken;
    private static readonly IBrush TabBg = NotaPalette.SurfaceCard;
    private static readonly IBrush Amber = NotaPalette.Accent;
    private static readonly IBrush AmberLit = NotaPalette.AccentBright;
    private static readonly IBrush AmberSubtle = NotaPalette.AccentSubtle;
    private static readonly IBrush TealC = NotaPalette.Teal;
    private static readonly IBrush TxtC = NotaPalette.TextPrimary;
    private static readonly IBrush Txt2 = NotaPalette.TextSecondary;
    private static readonly IBrush MutedC = NotaPalette.TextTertiary;
    private static readonly IBrush DimC = NotaPalette.TextDisabled;
    private static readonly IBrush OffPill = NotaPalette.SurfaceRaised;

    public double Width => 700;
    public bool FullBleed => true;
    public string? Subtitle => "HYBRID";

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId, di = index;
        float P(int p) => engine.DeviceGetParam(track, di, p);
        void Begin(int p) => engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void End(int p) => engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void Raw(int p, float v) => engine.DeviceSetParam(track, di, p, Math.Clamp(v, 0f, 1f));
        // A discrete edit (click) is one automation gesture, so it records while the transport does.
        void SetP(int p, float v) { Begin(p); Raw(p, v); End(p); }
        bool On(int p) => P(p) >= 0.5f;
        int Sel(int p, int n) => Math.Clamp((int)Math.Round(P(p) * (n - 1)), 0, n - 1);

        var readouts = new List<Action>();
        var scope = new float[kScope];
        int scN = 0;
        double Sc(int i) => scN > i ? scope[i] : 0;

        // IR catalogue (engine first, fallback table in a rack chain).
        var irList = new List<(string Name, string Cat, string Len)>();
        foreach (var line in engine.DeviceText(track, di, 10).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.Split('\t');
            if (f.Length >= 3) irList.Add((f[0], f[1], f[2]));
        }
        if (irList.Count == 0) irList.AddRange(IrFallback);

        // ---- formatters -------------------------------------------------------------
        static double Exp(double v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0, 1));
        static string Secs(double s) => s >= 10 ? FormattableString.Invariant($"{s:0.0} s") : s >= 1 ? FormattableString.Invariant($"{s:0.00} s") : FormattableString.Invariant($"{s * 1000:0} ms");
        static string Ms(double ms) => ms >= 100 ? FormattableString.Invariant($"{ms:0} ms") : ms >= 10 ? FormattableString.Invariant($"{ms:0.0} ms") : FormattableString.Invariant($"{ms:0.0} ms");
        static string Hz(double hz) => hz >= 1000 ? FormattableString.Invariant($"{hz / 1000:0.0} kHz") : FormattableString.Invariant($"{hz:0} Hz");
        static string Pct(double v) => FormattableString.Invariant($"{v * 100:0} %");
        static string Db(double db) => db <= -99 ? "−∞" : FormattableString.Invariant($"{db:+0.0;−0.0;0.0} dB");
        static string ShortDb(double db) => db <= -99 ? "−∞" : FormattableString.Invariant($"{db:0.0;−0.0;0.0}");
        string Pre(int timeP, int syncP) => On(syncP) ? SyncNames[Sel(timeP, SyncNames.Length)] : Ms(500 * P(timeP) * P(timeP));
        double IrSecs() => Sc(S_IrSeconds);
        double RtMid() => Exp(P(AlgoDecay), 0.2, 20);
        double RtLow() => RtMid() * Exp(P(AlgoLowDecay), 0.25, 4);
        double RtHigh() => RtMid() * Exp(P(AlgoHighDecay), 0.25, 4);
        double PreSec(int timeP, int syncP)
        {
            if (!On(syncP)) return 0.5 * P(timeP) * P(timeP);
            double[] beats = { 1.0 / 16, 1.0 / 12, 1.0 / 8, 1.0 / 6, 1.0 / 4, 1.0 / 3, 3.0 / 8, 1.0 / 2, 2.0 / 3, 3.0 / 4, 1, 1.5, 2 };
            double bpm = scN > 13 && scope[13] > 1 ? scope[13] : 120;
            return beats[Sel(timeP, beats.Length)] * 60.0 / bpm;
        }
        string LowCutF(double v) => v <= 0.001 ? "off" : Hz(Exp(v, 20, 2000));
        string HighCutF(double v) => v >= 0.999 ? "off" : Hz(Exp(v, 1000, 20000));
        static string GainF(double v) { double db = (v - 0.5) * 36; return Math.Abs(db) < 0.05 ? "0 dB" : FormattableString.Invariant($"{db:+0.0;−0.0} dB"); }
        static double ShareDb(double share) => share <= 1e-4 ? -100 : 10 * Math.Log10(share);   // equal-power blend gain
        static double DryDb(double v) { double g = 2 * v * v; return g <= 1e-5 ? -100 : 20 * Math.Log10(g); }
        string Interval() => Sel(ShimmerPitch, 3) switch { 0 => "−12 st", 1 => "+7 st", _ => "+12 st" };

        // ---- small builders ---------------------------------------------------------
        static TextBlock Caps(string t, IBrush? c = null, double fs = 7) => new() { Text = t, FontSize = fs, FontWeight = FontWeight.Bold, Foreground = c ?? MutedC, LetterSpacing = 0.8, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock Mono(string t, double fs, IBrush c)
        { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.Mono"); return tb; }
        static StackPanel Row(double sp, params Control[] cs)
        { var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = sp, VerticalAlignment = VerticalAlignment.Center }; foreach (var c in cs) s.Children.Add(c); return s; }
        static Control Docked(Control c, Dock d) { DockPanel.SetDock(c, d); return c; }
        static Control Col(Control c, int col) { Grid.SetColumn(c, col); return c; }
        static Control GRow(Control c, int row) { Grid.SetRow(c, row); return c; }
        static Border Divider(Control child, double top = 5) => new() { BorderBrush = BorderIn, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, top, 0, 0), Child = child };
        static Border InsetBox(Control child) => new() { Background = Inset, BorderBrush = BorderIn, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), ClipToBounds = true, Child = child };

        // Gauge knob bound to a device param (automation gesture + MIDI learn + live follow).
        Control K(int p, string name, Func<double, string> fmt, double size = 34, double cellW = 48, IBrush? arc = null, Action? onChange = null)
        {
            var val = Mono(fmt(P(p)), 7, TxtC);
            var knob = new Knob(P(p), 1.0) { Accent = true, ArcColor = arc, Default = engine.DeviceParamDefault(track, di, p), Width = size, Height = size };
            knob.ValueChanged += v => { Raw(p, (float)v); val.Text = fmt(v); onChange?.Invoke(); };
            knob.GestureBegin += () => Begin(p);
            knob.GestureEnd += () => End(p);
            MidiLearn.Bind(knob, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            readouts.Add(() => { if (knob.Dragging) return; float c = P(p); if (Math.Abs(c - knob.Value) > 1e-4) knob.Value = c; val.Text = fmt(c); });
            return KnobCell(name, knob, val, cellW);
        }

        // On/off pill bound to a param (> 0.5 = on).
        Control Toggle(int p, string label, Func<bool>? dim = null)
        {
            var pill = new Border { Width = 18, Height = 10, CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Center };
            var dot = new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(4) };
            var host = new Canvas { Width = 18, Height = 10 }; Canvas.SetTop(dot, 1.5); host.Children.Add(dot); pill.Child = host;
            var txt = new TextBlock { Text = label, FontSize = 8, VerticalAlignment = VerticalAlignment.Center };
            void Hi()
            {
                bool on = On(p), d = dim?.Invoke() ?? false;
                pill.Background = on ? (d ? NotaPalette.BorderStrong : Amber) : OffPill; dot.Background = on ? Panel : MutedC;
                Canvas.SetLeft(dot, on ? 9.5 : 1.5); txt.Foreground = on && !d ? TxtC : Txt2;
            }
            var wrap = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Children = { pill } };
            if (label.Length > 0) wrap.Children.Add(txt);
            wrap.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(wrap).Properties.IsLeftButtonPressed) return; SetP(p, On(p) ? 0f : 1f); Hi(); e.Handled = true; };
            readouts.Add(Hi); Hi();
            MidiLearn.Bind(wrap, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            return wrap;
        }

        // Segmented chips over a discrete param (n options spread over 0..1).
        Control Seg(int p, string[] names, double fs = 7, IBrush? accent = null)
        {
            int n = names.Length; var cells = new Border[n];
            var acc = accent ?? Amber;
            void Hi()
            {
                int cur = Sel(p, n);
                for (int i = 0; i < n; i++)
                {
                    bool on = i == cur;
                    cells[i].Background = on ? acc : Brushes.Transparent;
                    var tb = (TextBlock)cells[i].Child!; tb.Foreground = on ? Panel : MutedC; tb.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                }
            }
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
            for (int i = 0; i < n; i++)
            {
                int iv = i;
                var c = new Border { CornerRadius = new CornerRadius(2), Padding = new Thickness(4, 0), Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = names[i], FontSize = fs, Foreground = MutedC } };
                c.PointerPressed += (_, e) => { SetP(p, n > 1 ? iv / (float)(n - 1) : 0f); Hi(); e.Handled = true; };
                cells[i] = c; row.Children.Add(c);
            }
            readouts.Add(Hi); Hi();
            var seg = new Border { Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Child = row };
            MidiLearn.Bind(seg, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            return seg;
        }

        // Horizontal bar slider. get/set map the bar position (0..1) onto the param `p`
        // (CONV / ALGO both drive Blend, from opposite ends).
        Control HBar(int p, Func<double> get, Action<double> set, Func<double, string> fmt, IBrush fillC, Func<bool>? dim = null, double valW = 26)
        {
            var fill = new Border { Height = 3, Background = fillC, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var handle = new Border { Width = 6, Height = 7, Background = Txt2, CornerRadius = new CornerRadius(2) };
            var lay = new Canvas { Height = 9 };
            lay.Children.Add(handle); Canvas.SetTop(handle, 1);
            var canvas = new Panel { Height = 11, MinWidth = 24, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Children = {
                new Border { Height = 3, Background = Inset, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center }, fill, lay } };
            var val = Mono("", 8, TxtC); val.MinWidth = valW; val.TextAlignment = TextAlignment.Right;
            bool drag = false;
            void Vis()
            {
                double v = Math.Clamp(get(), 0, 1), w = canvas.Bounds.Width; if (w <= 0) w = 80;
                fill.Width = Math.Max(0, v * w); Canvas.SetLeft(handle, v * w - 3); val.Text = fmt(v);
                bool d = dim?.Invoke() ?? false;
                fill.Background = d ? NotaPalette.BorderStrong : fillC; handle.Background = d ? MutedC : Txt2; val.Foreground = d ? Txt2 : TxtC;
            }
            void From(PointerEventArgs e) { double w = canvas.Bounds.Width; set(w > 0 ? Math.Clamp(e.GetPosition(canvas).X / w, 0, 1) : 0); Vis(); }
            canvas.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed) return;
                if (e.ClickCount == 2) { Begin(p); Raw(p, engine.DeviceParamDefault(track, di, p)); End(p); Vis(); e.Handled = true; return; }
                drag = true; Begin(p); e.Pointer.Capture(canvas); From(e); e.Handled = true;
            };
            canvas.PointerMoved += (_, e) => { if (drag) From(e); };
            canvas.PointerReleased += (_, e) => { if (drag) { drag = false; End(p); e.Pointer.Capture(null); } };
            canvas.SizeChanged += (_, _) => Vis();
            readouts.Add(() => { if (!drag) Vis(); });
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 5, VerticalAlignment = VerticalAlignment.Center };
            grid.Children.Add(canvas); grid.Children.Add(Col(val, 1));
            MidiLearn.Bind(grid, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            return grid;
        }
        Control Bar(int p, Func<double, string> fmt, Func<bool>? dim = null, double valW = 26)
            => HBar(p, () => P(p), v => Raw(p, (float)v), fmt, Amber, dim, valW);
        Control SliderRow(string label, int p, Func<double, string> fmt, double labW = 48, Func<bool>? dim = null, double valW = 26)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions(FormattableString.Invariant($"{labW},*")), VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(Caps(label)); g.Children.Add(Col(Bar(p, fmt, dim, valW), 1));
            return g;
        }
        // A small text button.
        Border Btn(string text, Action click, string? tip = null)
        {
            var b = new Border { Height = 16, Padding = new Thickness(6, 0), CornerRadius = new CornerRadius(3), Background = OffPill, Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = text, FontSize = 8, Foreground = TxtC, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center } };
            b.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; click(); e.Handled = true; };
            if (tip != null) ToolTip.SetTip(b, tip);
            return b;
        }
        // A latching button over a toggle param (Freeze / Hold in).
        Border Latch(int p, string text, string tip)
        {
            var tb = new TextBlock { Text = text, FontSize = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var b = new Border { Height = 16, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), Child = tb };
            void Hi() { bool on = On(p); b.Background = on ? AmberSubtle : OffPill; b.BorderBrush = on ? Amber : Brushes.Transparent; tb.Foreground = on ? AmberLit : TxtC; tb.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal; }
            b.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; SetP(p, On(p) ? 0f : 1f); Hi(); e.Handled = true; };
            ToolTip.SetTip(b, tip);
            MidiLearn.Bind(b, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            readouts.Add(Hi); Hi();
            return b;
        }

        // ---- user IR loading ------------------------------------------------------------
        var irView = new ChamberIrView();
        var waveL = new float[320]; var waveR = new float[320];
        string waveSig = "";
        // Rendered previews (processed IR, algorithm echogram), refetched when the engine bumps its generation.
        float[] resEnv = Array.Empty<float>(), echoEnv = Array.Empty<float>();
        double previewGen = -1;
        void FetchPreviews()
        {
            if (Sc(S_PreviewGen) == previewGen) return;
            previewGen = Sc(S_PreviewGen);
            var a = new float[360]; var b = new float[360];
            resEnv = engine.DeviceLayerWave(track, di, 2, a, a.Length) > 0 ? a : Array.Empty<float>();
            echoEnv = engine.DeviceLayerWave(track, di, 3, b, b.Length) > 0 ? b : Array.Empty<float>();
        }
        void FetchWave()
        {
            int n0 = engine.DeviceLayerWave(track, di, 0, waveL, waveL.Length);
            int n1 = engine.DeviceLayerWave(track, di, 1, waveR, waveR.Length);
            irView.SetWave(n0 > 0 ? (float[])waveL.Clone() : Array.Empty<float>(), n1 > 0 ? (float[])waveR.Clone() : Array.Empty<float>());
        }
        void LoadIr(string path)
        {
            if (!engine.DeviceLoadFile(track, di, path)) return;
            waveSig = ""; ctx.NotifyChanged();
            foreach (var a in readouts) a();
        }
        async void PickIr(Control anchor)
        {
            var top = TopLevel.GetTopLevel(anchor);
            if (top is null) return;
            var files = await top.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Load impulse response", AllowMultiple = false,
                FileTypeFilter = new[] { new Avalonia.Platform.Storage.FilePickerFileType("Impulse response") { Patterns = new[] { "*.wav", "*.wave", "*.flac", "*.mp3" } } },
            });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } p) LoadIr(p);
        }
        irView.FileDropped += p => { ctx.HideDropGlow(); LoadIr(p); };
        irView.DropHover += ctx.HideDropGlow;   // the IR view owns the highlight, not the panel

        // ======================================================================
        // LEFT — BLEND column (C/A split + Dry/Wet)
        // ======================================================================
        Control Fader(int p, bool split, string label)
        {
            var f = new ChamberFader { Split = split, VerticalAlignment = VerticalAlignment.Stretch, Default = engine.DeviceParamDefault(track, di, p) };
            f.Changed += v => Raw(p, (float)v);
            f.DragStarted += () => Begin(p);
            f.DragEnded += () => End(p);
            readouts.Add(() => f.Set(P(p)));
            MidiLearn.Bind(f, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            ToolTip.SetTip(f, split ? "Blend — convolution (slate, top) ⇄ algorithm (brass, bottom). Double-click: 50 / 50" : "Dry/Wet — double-click resets");
            var lb = Caps(label); lb.HorizontalAlignment = HorizontalAlignment.Center;
            if (!split) readouts.Add(() => lb.Foreground = P(DryWet) > 0.995f || On(WetOnly) ? AmberLit : MutedC);
            return new DockPanel { Children = { Docked(lb, Dock.Bottom), f } };
        }
        var blendTxt = Mono("", 7, AmberLit); blendTxt.HorizontalAlignment = HorizontalAlignment.Center;
        var wetTxt = Mono("", 7, TxtC); wetTxt.HorizontalAlignment = HorizontalAlignment.Center;
        readouts.Add(() =>
        {
            float b = P(Blend);
            blendTxt.Text = FormattableString.Invariant($"{(1 - b) * 100:0} / {b * 100:0}");
            bool wo = On(WetOnly);
            wetTxt.Text = wo ? "SEND" : FormattableString.Invariant($"{P(DryWet) * 100:0} %");
            wetTxt.Foreground = wo || P(DryWet) > 0.995f ? AmberLit : TxtC;
        });
        var blendTitle = Caps("BLEND"); blendTitle.HorizontalAlignment = HorizontalAlignment.Center;
        var faders = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), ColumnSpacing = 9, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 3) };
        faders.Children.Add(Fader(Blend, true, "C/A")); faders.Children.Add(Col(Fader(DryWet, false, "WET"), 1));
        var blendCol = new Border { Width = 56, Background = Panel, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(0, 5, 0, 4),
            Child = new DockPanel { Children = { Docked(blendTitle, Dock.Top), Docked(wetTxt, Dock.Bottom), Docked(blendTxt, Dock.Bottom), faders } } };
        DockPanel.SetDock(blendCol, Dock.Left);

        // ======================================================================
        // CENTRE — Convolution
        // ======================================================================
        Control ConvTab()
        {
            // IR selector: ‹ [name · length · rate ▾] › + Load…
            var irName = new TextBlock { FontSize = 9, Foreground = TxtC, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var irInfo = Mono("", 7, MutedC);
            var irBox = new Border { Height = 16, Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 0), Cursor = new Cursor(StandardCursorType.Hand),
                Child = new DockPanel { Children = { Docked(new TextBlock { Text = "▾", FontSize = 8, Foreground = MutedC, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) }, Dock.Right), Docked(irInfo, Dock.Right), irName } } };
            ToolTip.SetTip(irBox, "Impulse response — built-in rooms, or your own file (drop it on the waveform)");
            MidiLearn.Bind(irBox, MidiTarget.DeviceParam(track, di, IrSelect), "IR");
            readouts.Add(() =>
            {
                int ir = Sel(IrSelect, IrCount);
                string user = ir == IrCount - 1 ? engine.DeviceText(track, di, 2) : "";
                irName.Text = ir < irList.Count ? irList[ir].Name : user.Length > 0 ? user : "User IR — none loaded";
                double s = IrSecs(), sr = Sc(S_IrRate); int ch = (int)Sc(S_IrChannels);
                irInfo.Text = s > 0 ? FormattableString.Invariant($"{s:0.0} s · {sr / 1000:0.#} kHz · {(ch >= 4 ? "4-ch" : ch == 2 ? "stereo" : "mono")}  ") : "";
            });
            void StepIr(int d) { int ir = Sel(IrSelect, IrCount); int max = engine.DeviceText(track, di, 2).Length > 0 ? IrCount - 1 : IrCount - 2; SetP(IrSelect, Math.Clamp(ir + d, 0, max) / (float)(IrCount - 1)); }
            irBox.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(irBox).Properties.IsLeftButtonPressed) return;
                var fly = new MenuFlyout();
                int cur = Sel(IrSelect, IrCount);
                string lastCat = "";
                for (int i = 0; i < irList.Count; i++)
                {
                    if (irList[i].Cat != lastCat)
                    {
                        if (lastCat.Length > 0) fly.Items.Add(new Separator());
                        fly.Items.Add(new MenuItem { Header = irList[i].Cat, IsEnabled = false, FontSize = 9 });
                        lastCat = irList[i].Cat;
                    }
                    int iv = i;
                    var mi = new MenuItem { Header = FormattableString.Invariant($"{irList[i].Name}   {irList[i].Len} s") };
                    if (i == cur) mi.Icon = new Avalonia.Controls.Shapes.Ellipse { Width = 6, Height = 6, Fill = Amber };
                    mi.Click += (_, _) => SetP(IrSelect, iv / (float)(IrCount - 1));
                    fly.Items.Add(mi);
                }
                fly.Items.Add(new Separator());
                string userName = engine.DeviceText(track, di, 2);
                var um = new MenuItem { Header = userName.Length > 0 ? FormattableString.Invariant($"User · {userName}") : "User IR (none loaded)", IsEnabled = userName.Length > 0 };
                if (cur == IrCount - 1) um.Icon = new Avalonia.Controls.Shapes.Ellipse { Width = 6, Height = 6, Fill = Amber };
                um.Click += (_, _) => SetP(IrSelect, 1f);
                fly.Items.Add(um);
                var load = new MenuItem { Header = "Load impulse response…" };
                load.Click += (_, _) => PickIr(irBox);
                fly.Items.Add(load);
                fly.ShowAt(irBox);
                e.Handled = true;
            };
            var irRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto"), ColumnSpacing = 4, Height = 18 };
            irRow.Children.Add(Caps("IR"));
            irRow.Children.Add(Col(Btn("‹", () => StepIr(-1), "Previous IR"), 1));
            irRow.Children.Add(Col(irBox, 2));
            irRow.Children.Add(Col(Btn("›", () => StepIr(1), "Next IR"), 3));
            irRow.Children.Add(Col(Btn("Load…", () => PickIr(irRow), "Load an impulse response (WAV / FLAC / MP3 — mono, stereo or 4-ch true stereo)"), 4));

            // IR view: trims drive IR Start / IR Decay.
            irView.TrimChanged += (s, e) => { Raw(IrStart, (float)s); Raw(IrDecay, (float)e); };
            irView.DragStarted += h => Begin(h == 1 ? IrStart : IrDecay);
            irView.DragEnded += h => End(h == 1 ? IrStart : IrDecay);
            ToolTip.SetTip(irView, "Drag the brass lines to trim the IR (Start · Decay); double-click resets. Drop an audio file here to load it.");
            readouts.Add(() =>
            {
                string sig = FormattableString.Invariant($"{Sel(IrSelect, IrCount)}|{Sc(S_IrSeconds):0.000}|{Sc(S_IrChannels)}|{Sc(S_UserIr)}");
                if (sig != waveSig) { waveSig = sig; FetchWave(); }
                int ir = Sel(IrSelect, IrCount);
                bool noUser = ir == IrCount - 1 && Sc(S_UserIr) < 0.5;
                irView.Set(IrSecs(), P(IrStart), P(IrDecay), 0.5 * P(IrAttack) * P(IrAttack), On(IrReverse), Sc(S_Building) > 0.5,
                           noUser ? "Drop an impulse response here (WAV / FLAC / MP3) or click Load…" : null);
                FetchPreviews();
                irView.SetResult(resEnv, Sc(S_ResultSeconds), PreSec(ConvPredelay, ConvSync));
            });

            var knobs = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*,Auto"), Height = 54 };
            knobs.Children.Add(K(ConvPredelay, "PREDELAY", _ => Pre(ConvPredelay, ConvSync)));
            knobs.Children.Add(Col(K(IrSize, "SIZE", v => FormattableString.Invariant($"{Exp(v, 0.5, 2) * 100:0} %")), 1));
            knobs.Children.Add(Col(K(IrAttack, "ATTACK", v => Ms(500 * v * v)), 2));
            knobs.Children.Add(Col(K(IrDecay, "DECAY", v => IrSecs() > 0 ? Secs(v * IrSecs()) : Pct(v)), 3));
            var toggles = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), Children = {
                Toggle(IrTrueStereo, "True stereo"), Toggle(IrReverse, "Reverse IR"), Toggle(ConvSync, "Sync predelay") } };
            knobs.Children.Add(Col(toggles, 4));

            return new DockPanel { LastChildFill = true, Margin = new Thickness(8, 5, 8, 2), Children = {
                Docked(irRow, Dock.Top),
                Docked(knobs, Dock.Bottom),
                new Border { Margin = new Thickness(0, 4, 0, 2), Child = InsetBox(irView) } } };
        }

        // ======================================================================
        // CENTRE — Algorithm
        // ======================================================================
        Control AlgoTab()
        {
            // MODE chips + tail readout
            var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
            var chipB = new Border[Modes.Length];
            void HiModes()
            {
                int cur = Sel(AlgoMode, Modes.Length);
                for (int i = 0; i < Modes.Length; i++)
                {
                    bool on = i == cur;
                    chipB[i].BorderBrush = on ? Amber : NotaPalette.BorderStrong; chipB[i].Background = on ? AmberSubtle : Brushes.Transparent;
                    var t = (TextBlock)chipB[i].Child!; t.Foreground = on ? AmberLit : Txt2; t.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                }
            }
            for (int i = 0; i < Modes.Length; i++)
            {
                int iv = i;
                var c = new Border { Height = 18, Padding = new Thickness(8, 0), CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand),
                    Child = new TextBlock { Text = Modes[i], FontSize = 9, VerticalAlignment = VerticalAlignment.Center } };
                c.PointerPressed += (_, e) => { SetP(AlgoMode, iv / 3f); HiModes(); e.Handled = true; };
                chipB[i] = c; chips.Children.Add(c);
            }
            MidiLearn.Bind(chips, MidiTarget.DeviceParam(track, di, AlgoMode), "Algo Mode");
            readouts.Add(HiModes); HiModes();
            var tail = Mono("", 7, MutedC);
            readouts.Add(() => tail.Text = On(Freeze) ? "tail ∞ (frozen)" : FormattableString.Invariant($"tail {Secs(RtMid())}"));
            var modeRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 5, Height = 20 };
            modeRow.Children.Add(Caps("MODE")); modeRow.Children.Add(Col(chips, 1));
            tail.HorizontalAlignment = HorizontalAlignment.Right; modeRow.Children.Add(Col(tail, 2));

            // DECAY PER BAND graph
            var graph = new ChamberDecayGraph();
            graph.Changed += (band, rt) =>
            {
                if (band == 1) Raw(AlgoDecay, (float)Math.Clamp(Math.Log(Math.Clamp(rt, 0.2, 20) / 0.2) / Math.Log(100), 0, 1));
                else
                {
                    double mul = Math.Clamp(rt / RtMid(), 0.25, 4);
                    Raw(band == 0 ? AlgoLowDecay : AlgoHighDecay, (float)(0.5 + Math.Log(mul) / Math.Log(16)));
                }
            };
            graph.DragStarted += b => Begin(b == 0 ? AlgoLowDecay : b == 1 ? AlgoDecay : AlgoHighDecay);
            graph.DragEnded += b => End(b == 0 ? AlgoLowDecay : b == 1 ? AlgoDecay : AlgoHighDecay);
            ToolTip.SetTip(graph, "Drag a marker to set that band's RT60 (mid = Decay; low / high follow it as ratios). The Damping knob sets the high crossover.");
            readouts.Add(() =>
            {
                FetchPreviews();
                graph.SetEchogram(echoEnv, Sc(S_PreviewSeconds));
                graph.Set(PreSec(AlgoPredelay, AlgoSync), RtLow(), RtMid(), RtHigh(), On(Freeze), FormattableString.Invariant($"· hf ≥ {Hz(Exp(P(AlgoDamping), 500, 18000))}"));
            });

            var knobs = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*,Auto"), Height = 56 };
            knobs.Children.Add(K(AlgoDecay, "DECAY", v => Secs(Exp(v, 0.2, 20)), 36, 50));
            knobs.Children.Add(Col(K(AlgoSize, "SIZE", v => FormattableString.Invariant($"{Exp(v, 0.4, 2.5) * 100:0} %"), 36, 50), 1));
            knobs.Children.Add(Col(K(AlgoDiffusion, "DIFFUSE", v => Pct(v), 36, 50), 2));
            knobs.Children.Add(Col(K(AlgoDamping, "DAMPING", v => Hz(Exp(v, 500, 18000)), 36, 50), 3));
            knobs.Children.Add(Col(K(AlgoPredelay, "PREDELAY", _ => Pre(AlgoPredelay, AlgoSync), 36, 50), 4));
            var toggles = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), Children = {
                Toggle(Freeze, "Freeze"), Toggle(AlgoVintage, "Vintage"), Toggle(AlgoSync, "Sync") } };
            knobs.Children.Add(Col(toggles, 5));

            return new DockPanel { LastChildFill = true, Margin = new Thickness(8, 5, 8, 2), Children = {
                Docked(modeRow, Dock.Top),
                Docked(knobs, Dock.Bottom),
                new Border { Margin = new Thickness(0, 4, 0, 2), Child = InsetBox(graph) } } };
        }

        // ======================================================================
        // CENTRE — EQ · Mod
        // ======================================================================
        Control EqModTab()
        {
            var eq = new ChamberEqCurve();
            eq.Changed += (h, cut, gain) =>
            {
                if (h == 0) { Raw(EqLowCut, (float)cut); Raw(EqLowGain, (float)gain); }
                else { Raw(EqHighCut, (float)cut); Raw(EqHighShelf, (float)gain); }
            };
            eq.DragStarted += h => { if (h == 0) { Begin(EqLowCut); Begin(EqLowGain); } else { Begin(EqHighCut); Begin(EqHighShelf); } };
            eq.DragEnded += h => { if (h == 0) { End(EqLowCut); End(EqLowGain); } else { End(EqHighCut); End(EqHighShelf); } };
            ToolTip.SetTip(eq, "Brass handle: low cut (X) + low shelf (Y) · grey handle: high cut (X) + high shelf (Y)");
            readouts.Add(() => eq.Set(P(EqLowCut), P(EqLowGain), P(EqHighShelf), P(EqHighCut)));
            var eqKnobs = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), Height = 50 };
            eqKnobs.Children.Add(K(EqLowCut, "LOW CUT", LowCutF, 30, 48));
            eqKnobs.Children.Add(Col(K(EqLowGain, "LOW GAIN", GainF, 30, 48), 1));
            eqKnobs.Children.Add(Col(K(EqHighShelf, "HI SHELF", GainF, 30, 48), 2));
            eqKnobs.Children.Add(Col(K(EqHighCut, "HI CUT", HighCutF, 30, 48), 3));
            var eqCap = Caps("TAIL EQ"); eqCap.Margin = new Thickness(0, 0, 0, 3);
            var left = new Border { Width = 214, BorderBrush = BorderIn, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(7, 5),
                Child = new DockPanel { Children = { Docked(eqCap, Dock.Top), Docked(eqKnobs, Dock.Bottom), new Border { Margin = new Thickness(0, 0, 0, 2), Child = InsetBox(eq) } } } };

            // Modulation
            var lfo = new ChamberLfoView { Height = 34 };
            readouts.Add(() => lfo.Tick(Exp(P(ModRate), 0.05, 8), P(ModDepth)));
            var modGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 4 };
            modGrid.Children.Add(K(ModRate, "RATE", v => { double hz = Exp(v, 0.05, 8); return hz >= 1 ? FormattableString.Invariant($"{hz:0.0} Hz") : FormattableString.Invariant($"{hz:0.00} Hz"); }, 28, 42, TealC));
            modGrid.Children.Add(Col(K(ModDepth, "DEPTH", v => Pct(v), 28, 42, TealC), 1));
            modGrid.Children.Add(Col(new Border { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(2, 1, 0, 0), Child = InsetBox(lfo) }, 2));
            var mod = new StackPanel { Spacing = 2, Children = { Caps("MODULATION"), modGrid } };

            // Shimmer
            bool ShimDim() => Sel(AlgoMode, 4) != 3;
            var shimCap = Caps("SHIMMER · PITCH SHIFT");
            readouts.Add(() => { shimCap.Text = ShimDim() ? "SHIMMER · SHIMMER MODE ONLY" : "SHIMMER · PITCH SHIFT"; shimCap.Foreground = ShimDim() ? DimC : MutedC; });
            var interval = Row(5, Caps("INTERVAL"), Seg(ShimmerPitch, new[] { "−12", "+7", "+12" }));
            var shimGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 6 };
            shimGrid.Children.Add(K(ShimmerAmount, "AMOUNT", v => Pct(v), 28, 44, ChamberInk.Mauve));
            shimGrid.Children.Add(Col(new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Children = { interval, Toggle(ShimmerFeedback, "Feed shimmer back", ShimDim) } }, 1));
            var shim = Divider(new StackPanel { Spacing = 2, Children = { shimCap, shimGrid } }, 4);
            shim.Margin = new Thickness(0, 3, 0, 0);

            // EQ position
            var posHint = Mono("", 7, MutedC); posHint.HorizontalAlignment = HorizontalAlignment.Right;
            readouts.Add(() => posHint.Text = Sel(EqPosition, 3) switch { 0 => "engine input", 2 => "dry + wet", _ => "wet only" });
            var posRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 6 };
            posRow.Children.Add(Caps("EQ POSITION")); posRow.Children.Add(Col(Seg(EqPosition, new[] { "Input", "Tail", "Output" }), 1)); posRow.Children.Add(Col(posHint, 2));
            var pos = Divider(posRow, 4); pos.Margin = new Thickness(0, 3, 0, 0);

            var right = new DockPanel { LastChildFill = false, Margin = new Thickness(7, 5, 7, 3), Children = { Docked(mod, Dock.Top), Docked(shim, Dock.Top), Docked(pos, Dock.Bottom) } };
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            g.Children.Add(left); g.Children.Add(Col(right, 1));
            return g;
        }

        // ======================================================================
        // RIGHT — Levels / Output
        // ======================================================================
        Control LevelsTab()
        {
            Control EngineRow(int onP, string name, bool conv)
            {
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,30,*"), ColumnSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
                g.Children.Add(Toggle(onP, ""));
                g.Children.Add(Col(Caps(name), 1));
                var bar = HBar(Blend, () => conv ? 1 - P(Blend) : P(Blend), v => Raw(Blend, (float)(conv ? 1 - v : v)),
                    v => ShortDb(ShareDb(v)), conv ? ChamberInk.Slate : Amber, () => !On(onP), 26);
                ToolTip.SetTip(bar, conv ? "Convolution share of the blend (drags Blend)" : "Algorithm share of the blend (drags Blend)");
                g.Children.Add(Col(bar, 2));
                return g;
            }
            var route = new Grid { ColumnDefinitions = new ColumnDefinitions("48,*"), VerticalAlignment = VerticalAlignment.Center };
            route.Children.Add(Caps("ROUTE"));
            var rseg = Seg(Routing, new[] { "Parallel", "Serial" });
            ToolTip.SetTip(rseg, "Parallel: both engines hear the input · Serial: the convolution feeds the algorithm");
            route.Children.Add(Col(rseg, 1));
            var dry = new Grid { ColumnDefinitions = new ColumnDefinitions("48,*"), VerticalAlignment = VerticalAlignment.Center };
            dry.Children.Add(Caps("DRY"));
            dry.Children.Add(Col(HBar(DryLevel, () => P(DryLevel), v => Raw(DryLevel, (float)v), v => ShortDb(DryDb(v)), NotaPalette.BorderStrong, () => On(WetOnly), 26), 1));
            var duck = SliderRow("DUCKING", DuckAmount, v => FormattableString.Invariant($"{v * 24:0.0}"));
            var rel = SliderRow("RELEASE", DuckRelease, v => FormattableString.Invariant($"{Exp(v, 20, 2000):0}"));
            ToolTip.SetTip(duck, "Ducking — the wet dips (up to 24 dB) while the input plays");
            var btns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
            btns.Children.Add(Latch(Freeze, "Freeze", "Freeze — hold the algorithm tail forever; the input is muted"));
            btns.Children.Add(Col(Latch(FreezeIn, "Hold in", "Hold in — keep feeding the input into the frozen tail (layering)"), 1));
            var wetBar = new PentadLevelBar { Height = 4, VerticalAlignment = VerticalAlignment.Center };
            var wetDb = Mono("", 8, TxtC);
            readouts.Add(() =>
            {
                double pk = Sc(S_WetL);
                wetBar.SetLinear(pk);
                double db = pk > 1e-5 ? 20 * Math.Log10(pk) : -100;
                wetDb.Text = ShortDb(db); wetDb.Foreground = db > -6 ? NotaPalette.Warning : TxtC;
            });
            var wet = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
            wet.Children.Add(Caps("WET")); wet.Children.Add(Col(wetBar, 1)); wet.Children.Add(Col(wetDb, 2));
            var duckGr = Mono("", 7, MutedC);
            readouts.Add(() => { double gr = Sc(S_DuckDb); duckGr.Text = gr > 0.05 ? FormattableString.Invariant($"−{gr:0.0}") : ""; });

            var g = new Grid { RowDefinitions = new RowDefinitions("*,*,*,*,Auto,*,*,Auto,*,*") };
            Control[] rows = { EngineRow(ConvOn, "CONV", true), EngineRow(AlgoOn, "ALGO", false), route, dry,
                new Border { Height = 1, Background = BorderIn, Margin = new Thickness(0, 2) }, duck, rel,
                new Border { Height = 1, Background = BorderIn, Margin = new Thickness(0, 2) }, btns, wet };
            for (int r = 0; r < rows.Length; r++) g.Children.Add(GRow(rows[r], r));
            return new Border { Padding = new Thickness(8, 4), Child = g };
        }
        Control OutputTab()
        {
            var meterL = new PentadLevelBar { Vertical = true, Width = 11, Height = 56 };
            var meterR = new PentadLevelBar { Vertical = true, Width = 11, Height = 56 };
            readouts.Add(() => { meterL.SetLinear(Sc(S_OutL)); meterR.SetLinear(Sc(S_OutR)); });
            static Control Center(Control c) { c.HorizontalAlignment = HorizontalAlignment.Center; return c; }
            var scale = new Grid { RowDefinitions = new RowDefinitions("*,*,*"), Height = 56, Children = { Mono("0", 7, DimC), GRow(Mono("−12", 7, DimC), 1), GRow(Mono("−48", 7, DimC), 2) } };
            var meters = Row(4, new StackPanel { Spacing = 2, Children = { meterL, Center(Caps("L", MutedC)) } },
                                new StackPanel { Spacing = 2, Children = { meterR, Center(Caps("R", MutedC)) } }, scale);
            meters.VerticalAlignment = VerticalAlignment.Top;
            var top = Row(8, K(Output, "OUTPUT", v => Db(-24 + 36 * v), 40, 54), meters);
            var qual = new Grid { ColumnDefinitions = new ColumnDefinitions("48,*"), VerticalAlignment = VerticalAlignment.Center };
            qual.Children.Add(Caps("QUALITY"));
            var qseg = Seg(Quality, new[] { "Eco", "Mid", "High" });
            ToolTip.SetTip(qseg, "Eco: IR up to 3 s · Mid: 6 s · High: 10 s + cubic-interpolated algorithm modulation");
            qual.Children.Add(Col(qseg, 1));
            var mid = new StackPanel { Spacing = 6, Children = {
                SliderRow("WIDTH", WidthP, v => FormattableString.Invariant($"{v * 200:0}"), 48),
                SliderRow("MONO f", BassMono, v => v <= 0.001 ? "off" : FormattableString.Invariant($"{Exp(v, 30, 500):0}"), 48),
                qual } };
            var wetOnly = Toggle(WetOnly, "Wet only (send)");
            ToolTip.SetTip(wetOnly, "Wet only — 100 % wet, no dry: for a return / send track");
            var zl = Toggle(ZeroLatency, "Zero latency");
            ToolTip.SetTip(zl, "On: exact convolution timing at 0 ms predelay (FIR head, more CPU). Off: lighter — the first block arrives late and is absorbed into the predelay.");
            var bottom = new StackPanel { Spacing = 5, Children = { wetOnly, zl } };
            var body = new DockPanel { LastChildFill = false, Children = {
                Docked(top, Dock.Top),
                Docked(Divider(mid), Dock.Top),
                Docked(Divider(bottom), Dock.Bottom) } };
            ((Control)body.Children[1]).Margin = new Thickness(0, 5, 0, 0);
            return new Border { Padding = new Thickness(8, 5), Child = body };
        }

        // ======================================================================
        // Tab frames
        // ======================================================================
        var centreHost = new ContentControl(); var rightHost = new ContentControl();
        var centreBodies = new Control?[3]; var rightBodies = new Control?[2];
        int centreTab = 0;
        var extras = Mono("", 7, MutedC);
        readouts.Add(() =>
        {
            switch (centreTab)
            {
                case 0:
                    int ch = (int)Sc(S_IrChannels);
                    string ts = !On(IrTrueStereo) || ch <= 1 ? "stereo" : ch >= 4 ? "true stereo" : "mono-in stereo";
                    extras.Text = Sc(S_KernelSeconds) > 0 ? FormattableString.Invariant($"{ts} · {Sc(S_KernelSeconds):0.0} s IR") : ts;
                    extras.Foreground = MutedC; break;
                case 1:
                    extras.Text = On(Freeze) ? "FREEZE active" : On(Routing) && On(ConvOn) ? "fed by convolution" : "";
                    extras.Foreground = On(Freeze) ? AmberLit : MutedC; break;
                default:
                    extras.Text = Sel(EqPosition, 3) switch { 0 => "at the input, before both engines", 2 => "at the output, dry + wet", _ => "on the tail, before Dry/Wet" };
                    extras.Foreground = MutedC; break;
            }
        });
        Control CentreBody(int t) => centreBodies[t] ??= t switch { 1 => AlgoTab(), 2 => EqModTab(), _ => ConvTab() };
        Control RightBody(int t) => rightBodies[t] ??= t == 1 ? OutputTab() : LevelsTab();
        var centre = TabFrame(new[] { "Convolution", "Algorithm", "EQ · Mod" }, centreHost, CentreBody, false, t => { centreTab = t; foreach (var a in readouts) a(); }, extras);
        var rightFrame = TabFrame(new[] { "Levels", "Output" }, rightHost, RightBody, true, _ => { foreach (var a in readouts) a(); }, null);
        var right = new Border { Width = 186, Background = Panel, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), ClipToBounds = true, Child = rightFrame };
        DockPanel.SetDock(right, Dock.Right);
        var centreBox = new Border { Background = Panel, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Margin = new Thickness(5, 0), ClipToBounds = true, Child = centre };

        // ======================================================================
        // Status strip
        // ======================================================================
        var statusLeft = new TextBlock { FontSize = 8, Foreground = Txt2, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var statusRight = Mono("", 8, Txt2);
        string StatusText()
        {
            float b = P(Blend);
            string blend = FormattableString.Invariant($"blend {(1 - b) * 100:0} / {b * 100:0}");
            switch (centreTab)
            {
                case 1:
                    string mode = Modes[Sel(AlgoMode, 4)];
                    if (On(Freeze)) return On(FreezeIn) ? FormattableString.Invariant($"Freeze + hold in: the tail layers the input · {mode}") : FormattableString.Invariant($"Freeze: tail held, input muted · {mode} {Secs(RtMid())}");
                    return FormattableString.Invariant($"{mode} · decay {Secs(RtMid())} · size {Exp(P(AlgoSize), 0.4, 2.5) * 100:0} % · predelay {Pre(AlgoPredelay, AlgoSync)}{(On(Routing) ? " · serial" : "")}");
                case 2:
                    string eqS = FormattableString.Invariant($"tail EQ {LowCutF(P(EqLowCut))} — {HighCutF(P(EqHighCut))}");
                    string shimS = Sel(AlgoMode, 4) == 3 ? FormattableString.Invariant($" · shimmer {Interval()}, {P(ShimmerAmount) * 100:0} %") : "";
                    return FormattableString.Invariant($"{(On(WetOnly) ? "Wet only · " : "")}{eqS}{shimS}");
                default:
                    int ir = Sel(IrSelect, IrCount);
                    string name = ir < irList.Count ? irList[ir].Name : engine.DeviceText(track, di, 2) is { Length: > 0 } u ? u : "no user IR";
                    return FormattableString.Invariant($"{name} · {blend} · predelay {Pre(ConvPredelay, ConvSync)}");
            }
        }
        readouts.Add(() =>
        {
            statusLeft.Text = StatusText();
            statusLeft.Foreground = centreTab == 1 && On(Freeze) ? AmberLit : Txt2;
            double sr = Sc(S_SampleRate), lat = Sc(S_Latency);
            string latS = lat > 0 ? FormattableString.Invariant($"conv +{lat:0} smp (in predelay)") : "latency 0 smp";
            statusRight.Text = sr > 0 ? FormattableString.Invariant($"{sr / 1000:0.#} kHz · {latS} · CPU {Sc(S_Cpu) * 100:0.0} %") : "";
        });
        var statusGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        statusGrid.Children.Add(statusLeft); statusGrid.Children.Add(Col(statusRight, 1));
        var status = new Border { Height = 18, Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(8, 0), Child = statusGrid };
        DockPanel.SetDock(status, Dock.Bottom);

        // ---- assemble ---------------------------------------------------------------
        var bodyRow = new DockPanel { LastChildFill = true, Margin = new Thickness(5), Children = { blendCol, right, centreBox } };
        var root = new DockPanel { LastChildFill = true, Background = RailBg, Children = { status, bodyRow } };

        void Refresh()
        {
            scN = engine.DeviceScope(track, di, scope, kScope);
            foreach (var a in readouts) a();
        }
        ctx.AddDeviceRefresher(Refresh);
        Refresh();
        return root;

        // local: a tab frame (bar + swapping body), optional extras on the right of the bar.
        Control TabFrame(string[] tabs, ContentControl host, Func<int, Control> body, bool centered, Action<int>? changed, Control? extrasCtl)
        {
            int sel = 0; var btns = new Border[tabs.Length];
            void Hi()
            {
                for (int i = 0; i < tabs.Length; i++)
                {
                    bool on = i == sel;
                    btns[i].Background = on ? TabBg : Brushes.Transparent; btns[i].BorderBrush = on ? Amber : Brushes.Transparent;
                    var tb = (TextBlock)btns[i].Child!; tb.Foreground = on ? AmberLit : MutedC; tb.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                }
            }
            var bar = centered ? (Avalonia.Controls.Panel)new UniformGrid { Rows = 1 } : new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < tabs.Length; i++)
            {
                int iv = i;
                var b = new Border { Padding = new Thickness(9, 0), BorderThickness = new Thickness(0, 0, 0, 2), BorderBrush = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand),
                    Child = new TextBlock { Text = tabs[i], FontSize = 9, Foreground = MutedC, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center } };
                b.PointerPressed += (_, _) => { sel = iv; Hi(); host.Content = body(iv); changed?.Invoke(iv); };
                btns[i] = b; bar.Children.Add(b);
            }
            var barDock = new DockPanel { Height = 20, LastChildFill = centered };
            if (extrasCtl != null) { var ex = new Border { Padding = new Thickness(0, 0, 8, 0), Child = extrasCtl }; DockPanel.SetDock(ex, Dock.Right); barDock.Children.Add(ex); }
            if (!centered) DockPanel.SetDock(bar, Dock.Left);
            barDock.Children.Add(bar);
            var barBorder = new Border { BorderBrush = Border2, BorderThickness = new Thickness(0, 0, 0, 1), Child = barDock };
            DockPanel.SetDock(barBorder, Dock.Top);
            Hi(); host.Content = body(sel);
            return new DockPanel { LastChildFill = true, Children = { barBorder, host } };
        }
    }
}
