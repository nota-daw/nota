// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Chamber body (device kind 20), a build of the "Nota Chamber"
// mockup, laid out the #531 way (CardLayout): a centre panel with Convolution / Algorithm /
// EQ · Mod / Output tabs — the IR (Predelay, Size, Attack beside it, the IR picker under it), the
// decay per band (Decay, Size, Damping beside it, the mode under it), the tail EQ (the cuts
// beside it, its position under it), the output meters (Width, Mono f beside them, the quality
// under them) — and a right panel with no tabs of its own that follows them: Convolution (on,
// true stereo, reverse, sync), Algorithm (on, Freeze, Diffuse, Predelay, Hold in, Vintage, Sync),
// Mod (Rate, Depth, the shimmer), Mix (routing, the conv ⇄ algo blend, ducking, wet only, zero
// latency) — over Dry · Dry/wet · Output. S is the IR over its picker and Predelay, Size,
// Attack, Dry/wet. The IR view, the decay-per-band graph and the tail-EQ
// curve are controls (drag trims, RT60 markers, EQ handles); an audio file dropped on the IR
// view (or picked with Load…) becomes the user IR. Every control is a device param, so
// automation / MIDI learn / presets / A-B / persistence come for free.
// FullBleed — the shared shell draws the header (name · preset · A/B · meter).

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Nota.Application;
using static Nota.App.DeviceCardKit;
using Nota.Application.Localization;

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
        (Loc.T("device.chamber.concert_hall_wide"), "HALL", "2.6"), ("Stone Vault", "HALL", "4.4"), ("Cathedral", "HALL", "7.5"), ("Scoring Stage", "HALL", "2.3"),
        ("Wood Chamber", "ROOM", "1.6"), ("Live Room", "ROOM", "1.0"), ("Drum Room", "ROOM", "0.8"), ("Tiled Bathroom", "ROOM", "1.3"),
        ("Vocal Plate", "PLATE", "2.8"), ("Bright Plate", "PLATE", "2.0"), ("Spring Tank", "SPRING", "2.6"), ("Car Park", "SPACE", "3.6"),
        ("Stairwell", "SPACE", "3.2"), ("Forest Clearing", "OUTDOOR", "2.0"), ("Gated Room", "FX", "0.7"), ("Metal Tank", "FX", "3.4"),
    };

    private static readonly IBrush RailBg = NotaPalette.SurfaceInset;
    private static readonly IBrush Panel = NotaPalette.SurfaceCard;   // a section box on the panel tone
    private static readonly IBrush Border2 = NotaPalette.BorderDefault;
    private static readonly IBrush BorderIn = NotaPalette.GraphBorder;
    private static readonly IBrush Inset = NotaPalette.BgSunken;
    private static readonly IBrush TabBg = NotaPalette.SurfaceCard;
    private static readonly IBrush Amber = NotaPalette.Accent;
    private static readonly IBrush AmberLit = NotaPalette.AccentBright;
    private static readonly IBrush AmberSubtle = NotaPalette.AccentSubtle;
    private static readonly IBrush TealC = DeviceCardKit.ModInk;
    private static readonly IBrush TxtC = NotaPalette.TextPrimary;
    private static readonly IBrush Txt2 = NotaPalette.TextSecondary;
    private static readonly IBrush MutedC = NotaPalette.TextTertiary;
    private static readonly IBrush DimC = NotaPalette.TextDisabled;
    private static readonly IBrush OffPill = NotaPalette.SurfaceRaised;

    public double Width => 700;
    public double WidthFor(IAudioEngine engine, int trackId, int deviceIndex) => CardLayout.WidthFor(engine, trackId, deviceIndex, Width);
    public Control? HeaderAccessory(DeviceCardContext ctx, int deviceIndex) => CardLayout.SizeToggle(ctx, deviceIndex);
    public bool FullBleed => true;
    public string? Subtitle => "Hybrid";

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
        // A typeable value for a fader or a graph handle (⌥-click it, specs/035).
        ValueField CF(int p, string name, Func<double, string> fmt) => ValueEntry.DeviceParam(engine, track, di, p, name, fmt, () => { foreach (var a in readouts) a(); });
        static string Secs(double s) => s >= 10 ? NotaNum.F($"{s:0.0}\u2009s") : s >= 1 ? NotaNum.F($"{s:0.00}\u2009s") : NotaNum.F($"{s * 1000:0}\u2009ms");
        static string Ms(double ms) => ms >= 100 ? NotaNum.F($"{ms:0}\u2009ms") : ms >= 10 ? NotaNum.F($"{ms:0.0}\u2009ms") : NotaNum.F($"{ms:0.0}\u2009ms");
        static string Hz(double hz) => hz >= 1000 ? NotaNum.F($"{hz / 1000:0.0}\u2009k") : NotaNum.F($"{hz:0}\u2009Hz");
        static string Pct(double v) => NotaNum.F($"{v * 100:0}\u2009%");
        static string Db(double db) => db <= -99 ? "−∞" : NotaNum.F($"{db:+0.0;−0.0;0.0}\u2009dB");
        static string ShortDb(double db) => db <= -99 ? "−∞" : NotaNum.F($"{db:0.0;−0.0;0.0}");
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
        static string GainF(double v) { double db = (v - 0.5) * 36; return Math.Abs(db) < 0.05 ? "0\u2009dB" : NotaNum.F($"{db:+0.0;−0.0}\u2009dB"); }
        static double DryDb(double v) { double g = 2 * v * v; return g <= 1e-5 ? -100 : 20 * Math.Log10(g); }

        // ---- small builders ---------------------------------------------------------
        static TextBlock Caps(string t, IBrush? c = null, double fs = NotaType.KnobLabel) => new() { Text = NotaText.Label(t), FontSize = fs, FontWeight = FontWeight.Medium, Foreground = c ?? MutedC, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock Mono(string t, double fs, IBrush c)
        { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.UI"); return tb; }
        static StackPanel Row(double sp, params Control[] cs)
        { var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = sp, VerticalAlignment = VerticalAlignment.Center }; foreach (var c in cs) s.Children.Add(c); return s; }
        static Control Docked(Control c, Dock d) { DockPanel.SetDock(c, d); return c; }
        static Control Col(Control c, int col) { Grid.SetColumn(c, col); return c; }
        static Border InsetBox(Control child) => new() { Background = Inset, BorderBrush = BorderIn, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Field, ClipToBounds = true, Child = child };

        // Gauge knob bound to a device param (automation gesture + MIDI learn + live follow).
        Control K(int p, string name, Func<double, string> fmt, double size = 34, double cellW = 48, IBrush? arc = null, Action? onChange = null)
        {
            var val = Mono(fmt(P(p)), 7, TxtC);
            var knob = new Knob(P(p), 1.0) { Accent = true, ArcColor = arc, Default = engine.DeviceParamDefault(track, di, p), Width = size, Height = size };
            knob.Format = v => fmt(v);   // read-out at any value: typing searches it
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
            var wrap = Switch(label, () => On(p), () => SetP(p, On(p) ? 0f : 1f), out var sync, dim);
            readouts.Add(sync);
            MidiLearn.Bind(wrap, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            return wrap;
        }

        // Segmented chips over a discrete param (n options spread over 0..1).
        Control Seg(int p, string[] names, double fs = 7, IBrush? accent = null)
        {
            int n = names.Length;
            var seg = DeviceCardKit.Segments(names, () => Sel(p, n), iv => SetP(p, n > 1 ? iv / (float)(n - 1) : 0f), out var sync);
            readouts.Add(sync);
            MidiLearn.Bind(seg, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            return seg;
        }

        // Horizontal bar slider. get/set map the bar position (0..1) onto the param `p`
        // (CONV / ALGO both drive Blend, from opposite ends).
        Control HBar(int p, Func<double> get, Action<double> set, Func<double, string> fmt, IBrush fillC, Func<bool>? dim = null, double valW = 26)
        {
            var row = DeviceCardKit.SliderRow("", () => Math.Clamp(get(), 0, 1), set, () => fmt(Math.Clamp(get(), 0, 1)), out var sync,
                begin: () => Begin(p), end: () => End(p),
                reset: () => { Begin(p); Raw(p, engine.DeviceParamDefault(track, di, p)); End(p); },
                dim: dim, valueWidth: valW);
            readouts.Add(sync);
            MidiLearn.Bind(row, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            return row;
        }
        Control Bar(int p, Func<double, string> fmt, Func<bool>? dim = null, double valW = 26)
            => HBar(p, () => P(p), v => Raw(p, (float)v), fmt, NotaPalette.VizInk, dim, valW);
        Control SliderRow(string label, int p, Func<double, string> fmt, double labW = 48, Func<bool>? dim = null, double valW = 26)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions(NotaNum.F($"{labW},*")), VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(Caps(label)); g.Children.Add(Col(Bar(p, fmt, dim, valW), 1));
            return g;
        }
        // A small text button.
        Border Btn(string text, Action click, string? tip = null)
        {
            var b = new Border { Height = 16, Padding = new Thickness(6, 0), CornerRadius = NotaRadius.Cell, Background = OffPill, Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = text, FontSize = 8, Foreground = TxtC, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center } };
            b.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; click(); e.Handled = true; };
            if (tip != null) ToolTip.SetTip(b, tip);
            return b;
        }
        // A small button carrying a drawn glyph instead of a typed character.
        Border GlyphBtn(GlyphKind kind, Action click, string tip)
        {
            var b = Btn("", click, tip);
            b.Child = new Glyph(kind, 8) { Foreground = TxtC };
            return b;
        }
        // A latching button over a toggle param (Freeze / Hold in).
        Border Latch(int p, string text, string tip)
        {
            var tb = new TextBlock { Text = text, FontSize = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var b = new Border { Height = 16, CornerRadius = NotaRadius.Cell, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), Child = tb };
            void Hi() { bool on = On(p); b.Background = on ? NotaPalette.AccentSubtle : OffPill; b.BorderBrush = on ? NotaPalette.BorderBrass : Brushes.Transparent; tb.Foreground = on ? NotaPalette.AccentHover : TxtC; tb.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal; }
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
            var files = await top.PickFilesAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Load impulse response", AllowMultiple = false,
                FileTypeFilter = new[] { new Avalonia.Platform.Storage.FilePickerFileType("Impulse response") { Patterns = new[] { "*.wav", "*.wave", "*.flac", "*.mp3" } } },
            });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } p) LoadIr(p);
        }
        irView.FileDropped += p => { ctx.HideDropGlow(); LoadIr(p); };
        irView.DropHover += ctx.HideDropGlow;   // the IR view owns the highlight, not the panel

        // ======================================================================
        // CENTRE — Convolution (the IR: Predelay · Size · Attack beside it, the IR picker under it)
        // ======================================================================
        Control IrPicker(double boxW = 210, bool loadBtn = true)
        {
            var irName = new TextBlock { FontSize = 9, Foreground = TxtC, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var irInfo = Mono("", 7, MutedC);
            var irBox = new Border { Height = 16, Width = boxW, Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Cell, Padding = new Thickness(6, 0), Cursor = new Cursor(StandardCursorType.Hand),
                Child = new DockPanel { Children = { Docked(new Glyph(GlyphKind.ChevronDown, 8) { Foreground = MutedC, Margin = new Thickness(6, 0, 0, 0) }, Dock.Right), Docked(irInfo, Dock.Right), irName } } };
            ToolTip.SetTip(irBox, Loc.T("device.chamber.impulse_response_built_in_rooms"));
            MidiLearn.Bind(irBox, MidiTarget.DeviceParam(track, di, IrSelect), "IR");
            readouts.Add(() =>
            {
                int ir = Sel(IrSelect, IrCount);
                string user = ir == IrCount - 1 ? engine.DeviceText(track, di, 2) : "";
                irName.Text = ir < irList.Count ? irList[ir].Name : user.Length > 0 ? user : Loc.T("device.chamber.user_ir_none_loaded_2");
                double s = IrSecs(), sr = Sc(S_IrRate); int ch = (int)Sc(S_IrChannels);
                irInfo.Text = s > 0 ? NotaNum.F($"{s:0.0} s · {sr / 1000:0.#} kHz · {(ch >= 4 ? "4-ch" : ch == 2 ? "stereo" : "mono")}  ") : "";
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
                        fly.Items.Add(MenuKit.Caption(irList[i].Cat));
                        lastCat = irList[i].Cat;
                    }
                    int iv = i;
                    var mi = new MenuItem { Header = NotaNum.F($"{irList[i].Name}   {irList[i].Len} s") };
                    mi.ToggleType = MenuItemToggleType.Radio; mi.IsChecked = i == cur;
                    mi.Click += (_, _) => SetP(IrSelect, iv / (float)(IrCount - 1));
                    fly.Items.Add(mi);
                }
                fly.Items.Add(new Separator());
                string userName = engine.DeviceText(track, di, 2);
                var um = new MenuItem { Header = userName.Length > 0 ? NotaNum.F($"User · {userName}") : Loc.T("device.chamber.user_ir_none_loaded"), IsEnabled = userName.Length > 0 };
                um.ToggleType = MenuItemToggleType.Radio; um.IsChecked = cur == IrCount - 1;
                um.Click += (_, _) => SetP(IrSelect, 1f);
                fly.Items.Add(um);
                var load = new MenuItem { Header = Loc.T("device.chamber.load_impulse_response") };
                load.Click += (_, _) => PickIr(irBox);
                fly.Items.Add(load);
                fly.ShowAt(irBox);
                e.Handled = true;
            };
            var row = Row(4, GlyphBtn(GlyphKind.ChevronLeft, () => StepIr(-1), "Previous IR"), irBox, GlyphBtn(GlyphKind.ChevronRight, () => StepIr(1), "Next IR"));
            if (loadBtn) row.Children.Add(Btn(Loc.T("device.chamber.load"), () => PickIr(row), Loc.T("device.chamber.load_an_impulse_response_wav")));
            return row;
        }
        bool irWired = false;
        Control IrView()
        {
            if (!irWired)
            {
                irWired = true;
                // IR view: trims drive IR Start / IR Decay.
                irView.TrimChanged += (s, e) => { Raw(IrStart, (float)s); Raw(IrDecay, (float)e); };
                irView.DragStarted += h => Begin(h == 1 ? IrStart : IrDecay);
                irView.DragEnded += h => End(h == 1 ? IrStart : IrDecay);
                ValueEntry.AttachHandles(irView, [CF(IrStart, "IR start", v => IrSecs() > 0 ? Secs(v * IrSecs()) : Pct(v))],
                    [CF(IrDecay, "IR decay", v => IrSecs() > 0 ? Secs(v * IrSecs()) : Pct(v))]);
                ToolTip.SetTip(irView, MenuKit.Keys(Loc.T("device.chamber.drag_the_trim_lines_to")));
                readouts.Add(() =>
                {
                    string sig = NotaNum.F($"{Sel(IrSelect, IrCount)}|{Sc(S_IrSeconds):0.000}|{Sc(S_IrChannels)}|{Sc(S_UserIr)}");
                    if (sig != waveSig) { waveSig = sig; FetchWave(); }
                    int ir = Sel(IrSelect, IrCount);
                    bool noUser = ir == IrCount - 1 && Sc(S_UserIr) < 0.5;
                    irView.Set(IrSecs(), P(IrStart), P(IrDecay), 0.5 * P(IrAttack) * P(IrAttack), On(IrReverse), Sc(S_Building) > 0.5,
                               noUser ? Loc.T("device.chamber.drop_an_impulse_response_here") : null);
                    FetchPreviews();
                    irView.SetResult(resEnv, Sc(S_ResultSeconds), PreSec(ConvPredelay, ConvSync));
                });
            }
            return InsetBox(irView);
        }
        Control ConvPreKnob(double cellW) => K(ConvPredelay, "PREDELAY", _ => Pre(ConvPredelay, ConvSync), 34, cellW);
        Control IrSizeKnob(double cellW) => K(IrSize, "SIZE", v => NotaNum.F($"{Exp(v, 0.5, 2) * 100:0} %"), 34, cellW);
        Control AttackKnob(double cellW) => K(IrAttack, "ATTACK", v => Ms(500 * v * v), 34, cellW);
        Control ConvTab() => CardLayout.View(IrView(), CardLayout.KnobColumn(ConvPreKnob(50), IrSizeKnob(50), AttackKnob(50)), IrPicker());

        // ======================================================================
        // CENTRE — Algorithm (the decay per band: Decay · Size · Damping beside it, the mode under it)
        // ======================================================================
        Control AlgoTab()
        {
            var chips = DeviceCardKit.Segments(Modes, () => Sel(AlgoMode, Modes.Length), i => SetP(AlgoMode, i / 3f), out var hiModes);
            MidiLearn.Bind(chips, MidiTarget.DeviceParam(track, di, AlgoMode), "Algo Mode");
            readouts.Add(hiModes);
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
            ValueEntry.AttachHandles(graph, [CF(AlgoLowDecay, "Low decay", v => Secs(RtMid() * Math.Pow(16, v - 0.5)))],
                [CF(AlgoDecay, "Decay", v => Secs(Exp(v, 0.2, 20)))], [CF(AlgoHighDecay, "High decay", v => Secs(RtMid() * Math.Pow(16, v - 0.5)))]);
            ToolTip.SetTip(graph, MenuKit.Keys(Loc.T("device.chamber.drag_a_marker_to_set")));
            readouts.Add(() =>
            {
                FetchPreviews();
                graph.SetEchogram(echoEnv, Sc(S_PreviewSeconds));
                graph.Set(PreSec(AlgoPredelay, AlgoSync), RtLow(), RtMid(), RtHigh(), On(Freeze), NotaNum.F($"· hf ≥ {Hz(Exp(P(AlgoDamping), 500, 18000))}"));
            });
            return CardLayout.View(InsetBox(graph),
                CardLayout.KnobColumn(K(AlgoDecay, "DECAY", v => Secs(Exp(v, 0.2, 20)), 34, 50), K(AlgoSize, "SIZE", v => NotaNum.F($"{Exp(v, 0.4, 2.5) * 100:0} %"), 34, 50),
                    K(AlgoDamping, "DAMPING", v => Hz(Exp(v, 500, 18000)), 34, 50)),
                chips);
        }

        // ======================================================================
        // CENTRE — EQ · Mod (the tail EQ: Low cut · High cut beside it, the EQ position under it;
        // the shelves drag on the curve)
        // ======================================================================
        Control EqTab()
        {
            var eq = new ChamberEqCurve();
            eq.Changed += (h, cut, gain) =>
            {
                if (h == 0) { Raw(EqLowCut, (float)cut); Raw(EqLowGain, (float)gain); }
                else { Raw(EqHighCut, (float)cut); Raw(EqHighShelf, (float)gain); }
            };
            eq.DragStarted += h => { if (h == 0) { Begin(EqLowCut); Begin(EqLowGain); } else { Begin(EqHighCut); Begin(EqHighShelf); } };
            eq.DragEnded += h => { if (h == 0) { End(EqLowCut); End(EqLowGain); } else { End(EqHighCut); End(EqHighShelf); } };
            ValueEntry.AttachHandles(eq, [CF(EqLowCut, "Low cut", LowCutF), CF(EqLowGain, "Low shelf", GainF)],
                [CF(EqHighCut, "High cut", HighCutF), CF(EqHighShelf, "High shelf", GainF)]);
            ToolTip.SetTip(eq, MenuKit.Keys(Loc.T("device.chamber.left_handle_low_cut_x")));
            readouts.Add(() => eq.Set(P(EqLowCut), P(EqLowGain), P(EqHighShelf), P(EqHighCut)));
            return CardLayout.View(InsetBox(eq),
                CardLayout.KnobColumn(K(EqLowCut, "LOW-CUT", LowCutF, 34, 50), K(EqHighCut, "HIGH-CUT", HighCutF, 34, 52)),
                Seg(EqPosition, new[] { "Input", "Tail", "Output" }));
        }

        // ======================================================================
        // CENTRE — Output (the meters: Width · Mono f beside them, the quality under them)
        // ======================================================================
        Control OutTab()
        {
            Control MeterRow(string ch, Func<double> lin)
            {
                var bar = new PentadLevelBar { Height = 8, VerticalAlignment = VerticalAlignment.Center };
                var db = Mono("", 9, TxtC); db.Width = 40; db.TextAlignment = TextAlignment.Right;
                readouts.Add(() => { double v = lin(); bar.SetLinear(v); db.Text = v > 1e-5 ? ShortDb(20 * Math.Log10(v)) : "−∞"; });
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("30,*,Auto"), ColumnSpacing = 8 };
                g.Children.Add(Caps(ch)); g.Children.Add(Col(bar, 1)); g.Children.Add(Col(db, 2));
                return g;
            }
            var duckGr = Mono("", 9, MutedC);
            readouts.Add(() => { double gr = Sc(S_DuckDb); duckGr.Text = gr > 0.05 ? NotaNum.F($"ducking −{gr:0.0} dB") : Loc.T("device.chamber.no_ducking"); });
            var meters = new StackPanel { Spacing = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0), Children = {
                MeterRow(Loc.T("device.chamber.out_l"), () => Sc(S_OutL)), MeterRow(Loc.T("device.chamber.out_r"), () => Sc(S_OutR)), MeterRow("WET", () => Sc(S_WetL)), duckGr } };
            var qseg = Seg(Quality, new[] { "Eco", "Mid", "High" });
            ToolTip.SetTip(qseg, Loc.T("device.chamber.eco_ir_up_to_3"));
            var mono = K(BassMono, "Mono f", v => v <= 0.001 ? "off" : NotaNum.F($"{Exp(v, 30, 500):0} Hz"), 34, 50);
            return CardLayout.View(InsetBox(meters), CardLayout.KnobColumn(K(WidthP, "WIDTH", v => NotaNum.F($"{v * 200:0} %"), 34, 50), mono), qseg);
        }

        // ======================================================================
        // RIGHT — follows the centre tab: Convolution · Algorithm · Mod · Mix
        // ======================================================================
        (Control, Control) ConvPanel() => (CardLayout.Head(Toggle(ConvOn, Loc.T("device.chamber.convolution"))), CardLayout.Rows(
            Toggle(IrTrueStereo, "True stereo"),
            Toggle(IrReverse, "Reverse IR"),
            Toggle(ConvSync, "Sync predelay"),
            CardLayout.Rule(),
            Info(() =>
            {
                int ch = (int)Sc(S_IrChannels);
                string ts = !On(IrTrueStereo) || ch <= 1 ? "stereo" : ch >= 4 ? "true stereo" : "mono-in stereo";
                return Sc(S_KernelSeconds) > 0 ? NotaNum.F($"{ts} · {Sc(S_KernelSeconds):0.0} s IR") : ts;
            })));
        (Control, Control) AlgoPanel() => (CardLayout.Head(Toggle(AlgoOn, Loc.T("device.chamber.algorithm")), Latch(Freeze, "Freeze", Loc.T("device.chamber.freeze_hold_the_algorithm_tail"))),
            CardLayout.Rows(
                SliderRow("DIFFUSE", AlgoDiffusion, v => Pct(v), 52, valW: 34),
                SliderRow("PREDELAY", AlgoPredelay, _ => Pre(AlgoPredelay, AlgoSync), 52, valW: 34),
                CardLayout.Rule(),
                CardLayout.Pair(Latch(FreezeIn, "Hold in", Loc.T("device.chamber.hold_in_keep_feeding_the")), Toggle(AlgoVintage, "Vintage")),
                Toggle(AlgoSync, "Sync predelay")));
        (Control, Control) ModPanel()
        {
            bool ShimDim() => Sel(AlgoMode, 4) != 3;
            var interval = Seg(ShimmerPitch, new[] { "−12", "+7", "+12" });
            return (CardLayout.Head(CardLayout.Title(Loc.T("device.chamber.mod"))), CardLayout.Rows(
                SliderRow("RATE", ModRate, v => { double hz = Exp(v, 0.05, 8); return hz >= 1 ? NotaNum.F($"{hz:0.0} Hz") : NotaNum.F($"{hz:0.00} Hz"); }, 52, valW: 34),
                SliderRow("DEPTH", ModDepth, v => Pct(v), 52, valW: 34),
                CardLayout.Rule(),
                SliderRow("SHIMMER", ShimmerAmount, v => Pct(v), 52, ShimDim, 34),
                CardLayout.LabelRow("INTERVAL", interval, 52),
                Toggle(ShimmerFeedback, Loc.T("device.chamber.feed_shimmer_back"), ShimDim)));
        }
        (Control, Control) MixPanel()
        {
            var blend = HBar(Blend, () => P(Blend), v => Raw(Blend, (float)v), v => NotaNum.F($"{(1 - v) * 100:0}:{v * 100:0}"), NotaPalette.VizInk, null, 38);
            ToolTip.SetTip(blend, Loc.T("device.chamber.convolution_share_of_the_blend"));
            var rseg = Seg(Routing, new[] { "Parallel", "Serial" });
            ToolTip.SetTip(rseg, Loc.T("device.chamber.parallel_both_engines_hear_the"));
            var duck = SliderRow("DUCKING", DuckAmount, v => NotaNum.F($"{v * 24:0.0} dB"), 52, valW: 38);
            ToolTip.SetTip(duck, Loc.T("device.chamber.ducking_the_wet_dips_up"));
            var wetOnly = Toggle(WetOnly, Loc.T("common.wet_only_send"));
            ToolTip.SetTip(wetOnly, Loc.T("device.chamber.wet_only_100_wet_no"));
            var zl = Toggle(ZeroLatency, "Zero latency");
            ToolTip.SetTip(zl, Loc.T("device.chamber.on_exact_convolution_timing_at"));
            return (CardLayout.Head(CardLayout.Title(Loc.T("device.chamber.mix")), rseg), CardLayout.Rows(
                CardLayout.LabelRow("BLEND", blend, 52, stretch: true),
                duck,
                SliderRow("RELEASE", DuckRelease, v => NotaNum.F($"{Exp(v, 20, 2000):0} ms"), 52, valW: 38),
                CardLayout.Rule(),
                wetOnly,
                zl));
        }
        Control Info(Func<string> text)
        {
            var t = Mono("", 8, Txt2);
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            readouts.Add(() => t.Text = text());
            return t;
        }

        // ======================================================================
        // Frames: L (four views, the panel that follows them, Dry · Dry/wet · Output under it) ·
        // S (the IR over its picker and Predelay · Size · Attack · Dry/wet)
        // ======================================================================
        int centreTab = 0;
        var extras = Mono("", 7, MutedC);
        readouts.Add(() =>
        {
            switch (centreTab)
            {
                case 1:
                    extras.Text = string.Join(" · ", new[] { On(Freeze) ? "Freeze active" : "", NotaNum.F($"tail {Secs(RtMid())}"), On(Routing) && On(ConvOn) ? "fed by convolution" : "" }.Where(x => x.Length > 0));
                    extras.Foreground = On(Freeze) ? AmberLit : MutedC; break;
                case 2:
                    extras.Text = Sel(EqPosition, 3) switch { 0 => Loc.T("device.chamber.at_the_input_before_both"), 2 => Loc.T("device.chamber.at_the_output_dry_wet"), _ => Loc.T("device.chamber.on_the_tail_before_dry") };
                    extras.Foreground = MutedC; break;
                case 3:
                    extras.Text = NotaNum.F($"latency {Sc(S_Latency):0} smp · CPU {Sc(S_Cpu) * 100:0} %");
                    extras.Foreground = MutedC; break;
                default:
                    extras.Text = Pre(ConvPredelay, ConvSync) + " predelay · " + (On(ConvOn) ? "on" : "off");
                    extras.Foreground = MutedC; break;
            }
        });
        Control WetKnob(double cellW, double size)
        {
            var k = K(DryWet, "DRY/WET", v => NotaNum.F($"{v * 100:0} %"), size, cellW);
            ToolTip.SetTip(k, Loc.T("device.chamber.dry_wet_double_click_resets"));
            return k;
        }
        Control root = CardLayout.IsMini(ctx, di)
            ? CardLayout.Mini(IrView(), IrPicker(170, false), ConvPreKnob(56), IrSizeKnob(56), AttackKnob(56), WetKnob(56, 34))
            : CardLayout.Large(new[] { "Convolution", "Algorithm", "EQ · Mod", Loc.T("device.chamber.tab_output") }, 0,
                t => t switch { 1 => AlgoTab(), 2 => EqTab(), 3 => OutTab(), _ => ConvTab() },
                t => t switch { 1 => AlgoPanel(), 2 => ModPanel(), 3 => MixPanel(), _ => ConvPanel() },
                extras,
                CardLayout.Output(K(DryLevel, "DRY", v => ShortDb(DryDb(v)), 34, 54), WetKnob(58, 44), K(Output, "OUTPUT", v => Db(-24 + 36 * v), 34, 54)),
                t => { centreTab = t; foreach (var a in readouts) a(); });

        void Refresh()
        {
            scN = engine.DeviceScope(track, di, scope, kScope);
            foreach (var a in readouts) a();
        }
        ctx.AddDeviceRefresher(Refresh);
        Refresh();
        return root;

    }
}
