// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Pentad editor (instrument kind 14), a build of the
// "Nota Pentad" mockup (700×260) on the same frame as Monolith: an always-visible WHEELS
// column (pitch / mod), a centre tabbed panel (Oscillators / Filter · Amp / Poly Mod), a
// right tabbed panel (Mixer / Output) and a status strip with the Voice setup flyout
// (polyphony, allocation, oversampling, vintage seed …). The five-voice activity strip and
// the output meters read the instrument's scope telemetry. Every control is a plugin-param,
// so automation / MIDI learn / presets / persistence come for free. BodyOnly — the shared
// shell draws the header (name / preset / A-B / meter).

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Nota.Application;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed class PentadInstrumentCard : IInstrumentCard
{
    private static readonly IBrush RailBg = NotaPalette.SurfaceInset;
    private static readonly IBrush Panel = NotaPalette.BgApp;
    private static readonly IBrush Border2 = NotaPalette.BorderDefault;
    private static readonly IBrush BorderIn = NotaPalette.GraphBorder;
    private static readonly IBrush Inset = NotaPalette.BgSunken;
    private static readonly IBrush TabBg = NotaPalette.SurfaceCard;
    private static readonly IBrush Amber = NotaPalette.Accent;
    private static readonly IBrush AmberLit = NotaPalette.AccentBright;
    private static readonly IBrush TealC = NotaPalette.Teal;
    private static readonly IBrush TxtC = NotaPalette.TextPrimary;
    private static readonly IBrush Txt2 = NotaPalette.TextSecondary;
    private static readonly IBrush MutedC = NotaPalette.TextTertiary;
    private static readonly IBrush DimC = NotaPalette.TextDisabled;
    private static readonly IBrush Handle = NotaPalette.TextSecondary;
    private static readonly IBrush OffPill = NotaPalette.SurfaceRaised;
    private static readonly IBrush AmberSubtle = NotaPalette.AccentSubtle;

    private static readonly string[] Feet = { "32′", "16′", "8′", "4′" };
    private static readonly string[] SyncNames = { "8 bar", "4 bar", "2 bar", "1 bar", "1/2", "1/4.", "1/4", "1/8.", "1/4T", "1/8", "1/8T", "1/16", "1/16T", "1/32" };

    public bool BodyOnly => true;
    public string Subtitle => "ANALOG POLY";

    public string? VoiceLabel(IAudioEngine engine, int trackId, int active)
    {
        int n = engine.PluginParamCount(trackId, -1);
        for (int i = 0; i < n; i++)
            if (engine.PluginParamId(trackId, -1, i) == "polyphony")
                return $"{Math.Max(0, active)}/{Poly(engine.PluginParamGet(trackId, -1, i))}";
        return null;
    }

    private static int Poly(float v) => v < 0.25f ? 5 : v < 0.75f ? 10 : 16;

    public Control Build(DeviceCardContext ctx)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId;
        int pc = engine.PluginParamCount(track, -1);
        var idx = new Dictionary<string, int>();
        for (int i = 0; i < pc; i++) idx[engine.PluginParamId(track, -1, i)] = i;
        float G(string id) => idx.TryGetValue(id, out var i) ? engine.PluginParamGet(track, -1, i) : 0f;
        int I(string id) => idx.TryGetValue(id, out var i) ? i : -1;
        bool On(string id) => G(id) > 0.5f;
        int Sel(string id, int n) => Math.Clamp((int)Math.Round(G(id) * (n - 1)), 0, n - 1);
        // A discrete edit (click) is one automation gesture, so Touch/Latch/Write record it.
        void SetP(string id, float v)
        {
            if (I(id) is not (var i and >= 0)) return;
            engine.BeginAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id);
            engine.PluginParamSet(track, -1, i, Math.Clamp(v, 0f, 1f));
            engine.EndAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id);
        }

        var readouts = new List<Action>();
        var scope = new float[32];
        var filtCurve = new PentadFilterCurve();
        var fEnvCurve = new PentadEnvCurve { Accent = Amber };
        var aEnvCurve = new PentadEnvCurve { Accent = TealC };
        var voiceStrips = new List<PentadVoiceStrip>();
        var meterL = new PentadLevelBar { Vertical = true, Width = 11, Height = 56 };
        var meterR = new PentadLevelBar { Vertical = true, Width = 11, Height = 56 };
        var sumBar = new PentadLevelBar { Height = 4, VerticalAlignment = VerticalAlignment.Center };
        var sumTxt = MonoText("", 8, TxtC);
        var voiceCountTxt = MonoText("", 7, Txt2);
        int scN = 0;

        void Refresh()
        {
            filtCurve.Set(G("cutoff"), G("reso"), On("lowcomp")); filtCurve.InvalidateVisual();
            fEnvCurve.Set(G("fattack"), G("fdecay"), G("fsustain"), G("frelease")); fEnvCurve.InvalidateVisual();
            aEnvCurve.Set(G("aattack"), G("adecay"), G("asustain"), On("releaseon") ? G("arelease") : 0); aEnvCurve.InvalidateVisual();
            scN = engine.InstrumentScope(track, scope);
            if (scN >= 24)
            {
                int poly = (int)scope[0];
                foreach (var vs in voiceStrips) vs.Set(poly, scope.AsSpan(8, 16));
                meterL.SetLinear(scope[2]); meterR.SetLinear(scope[3]);
                sumBar.SetLinear(scope[4] * 0.6);
                sumTxt.Text = scope[4] > 1e-4 ? $"{20 * Math.Log10(scope[4] * 0.6):0.0}" : "−∞";
                voiceCountTxt.Text = $"{(int)scope[1]} / {poly}";
            }
            foreach (var a in readouts) a();
        }

        // ---- small builders --------------------------------------------------------
        static TextBlock Lbl(string t, double fs, IBrush c, FontWeight w = FontWeight.Bold)
            => new() { Text = t, FontSize = fs, FontWeight = w, Foreground = c, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock Caps(string t) => new() { Text = t, FontSize = 7, FontWeight = FontWeight.Bold, Foreground = MutedC, VerticalAlignment = VerticalAlignment.Center, LetterSpacing = 0.8 };
        static TextBlock MonoText(string t, double fs, IBrush c)
        { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.Mono"); return tb; }
        static StackPanel Row(double sp, params Control[] cs)
        { var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = sp, VerticalAlignment = VerticalAlignment.Center }; foreach (var c in cs) s.Children.Add(c); return s; }
        static Control Docked(Control c, Dock d) { DockPanel.SetDock(c, d); return c; }
        static Control Col(Control c, int col) { Grid.SetColumn(c, col); return c; }
        static Border Divider(Control child, double top = 5) => new() { BorderBrush = BorderIn, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, top, 0, 0), Child = child };

        Control K(string id, string name, Func<float, string> fmt, bool mod = false, double sz = 26, double cw = 40)
            => InstrumentControls.InstKnob(ctx, idx, id, name, Refresh, fmt, sz, cw, mod ? TealC : null);

        // On/off pill toggle backed by a param (> 0.5 = on).
        Control Toggle(string id, string label, List<Action>? into = null)
        {
            var pill = new Border { Width = 18, Height = 10, CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Center };
            var dot = new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(4) };
            var host = new Canvas { Width = 18, Height = 10 }; Canvas.SetTop(dot, 1.5); host.Children.Add(dot); pill.Child = host;
            var txt = new TextBlock { Text = label, FontSize = 8, Foreground = MutedC, VerticalAlignment = VerticalAlignment.Center };
            void Hi() { bool on = On(id); pill.Background = on ? Amber : OffPill; dot.Background = on ? Panel : MutedC; Canvas.SetLeft(dot, on ? 9.5 : 1.5); txt.Foreground = on ? TxtC : Txt2; }
            var wrap = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Children = { pill } };
            if (label.Length > 0) wrap.Children.Add(txt);
            wrap.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(wrap).Properties.IsLeftButtonPressed) return; SetP(id, On(id) ? 0f : 1f); Refresh(); e.Handled = true; };
            (into ?? readouts).Add(Hi); Hi();
            if (I(id) is var pi and >= 0) MidiLearn.Bind(wrap, MidiTarget.PluginParam(track, -1, pi), label.Length > 0 ? label : id);
            return wrap;
        }

        // Segmented chips. values[i] is written on click; the lit chip is the nearest value.
        Control Chips(string id, string[] names, float[]? values = null, List<Action>? into = null, double fs = 7)
        {
            int n = names.Length;
            values ??= BuildValues(n);
            var arr = new Border[n];
            void Hi()
            {
                float cur = G(id); int best = 0;
                for (int i = 1; i < n; i++) if (Math.Abs(values[i] - cur) < Math.Abs(values[best] - cur)) best = i;
                bool exact = Math.Abs(values[best] - cur) < 0.02f;
                for (int i = 0; i < n; i++)
                {
                    bool on = i == best && exact;
                    arr[i].Background = on ? Amber : Brushes.Transparent;
                    var tb = (TextBlock)arr[i].Child!; tb.Foreground = on ? Panel : MutedC; tb.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                }
            }
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
            for (int i = 0; i < n; i++)
            {
                int iv = i;
                var c = new Border { CornerRadius = new CornerRadius(2), Padding = new Thickness(4, 0), Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = names[i], FontSize = fs, Foreground = MutedC } };
                c.PointerPressed += (_, e) => { SetP(id, values[iv]); Refresh(); e.Handled = true; };
                arr[i] = c; row.Children.Add(c);
            }
            (into ?? readouts).Add(Hi); Hi();
            var seg = new Border { Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Child = row };
            if (I(id) is var pi and >= 0) MidiLearn.Bind(seg, MidiTarget.PluginParam(track, -1, pi), id);
            return seg;
        }
        static float[] BuildValues(int n) { var v = new float[n]; for (int i = 0; i < n; i++) v[i] = n > 1 ? i / (float)(n - 1) : 0f; return v; }

        // Waveform on/off chip (the P5's independent, summable wave switches).
        Control WaveChip(string id, int wave, double w = 22, double h = 16)
        {
            var ic = new PentadWaveIcon(wave) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var b = new Border { Width = w, Height = h, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Cursor = new Cursor(StandardCursorType.Hand), Child = ic };
            void Hi() { bool on = On(id); b.Background = on ? AmberSubtle : Brushes.Transparent; b.BorderBrush = on ? Amber : NotaPalette.BorderStrong; ic.Stroke = on ? AmberLit : MutedC; ic.InvalidateVisual(); }
            b.PointerPressed += (_, e) => { SetP(id, On(id) ? 0f : 1f); Refresh(); e.Handled = true; };
            readouts.Add(Hi); Hi();
            if (I(id) is var pi and >= 0) MidiLearn.Bind(b, MidiTarget.PluginParam(track, -1, pi), id);
            return b;
        }

        // Horizontal fill slider bound to a param; `dim` greys it out (inactive route).
        Control HSlider(string id, Func<double, string> fmt, double valW = 24, Func<bool>? dim = null, List<Action>? into = null)
        {
            int pi = I(id);
            var fill = new Border { Height = 3, Background = Amber, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var handle = new Border { Width = 6, Height = 7, Background = Handle, CornerRadius = new CornerRadius(2) };
            var lay = new Canvas { Height = 9 };
            lay.Children.Add(handle); Canvas.SetTop(handle, 1);
            var canvas = new Panel { Height = 11, MinWidth = 24, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Children = {
                new Border { Height = 3, Background = Inset, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center }, fill, lay } };
            var val = MonoText("", 8, TxtC); val.MinWidth = valW; val.TextAlignment = TextAlignment.Right;
            bool drag = false;
            void Vis(double v)
            {
                double w = canvas.Bounds.Width; if (w <= 0) w = 80;
                fill.Width = Math.Max(0, v * w); Canvas.SetLeft(handle, v * w - 3); val.Text = fmt(v);
                bool d = dim?.Invoke() ?? false;
                fill.Background = d ? NotaPalette.BorderStrong : Amber; handle.Background = d ? MutedC : Handle; val.Foreground = d ? Txt2 : TxtC;
            }
            void From(PointerEventArgs e) { double w = canvas.Bounds.Width; double v = w > 0 ? Math.Clamp(e.GetPosition(canvas).X / w, 0, 1) : 0; if (pi >= 0) engine.PluginParamSet(track, -1, pi, (float)v); Vis(v); Refresh(); }
            canvas.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed) return;
                if (e.ClickCount == 2 && pi >= 0) { SetP(id, engine.InstrumentParamDefault(track, pi)); Refresh(); e.Handled = true; return; }
                drag = true; if (pi >= 0) engine.BeginAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id); e.Pointer.Capture(canvas); From(e); e.Handled = true;
            };
            canvas.PointerMoved += (_, e) => { if (drag) From(e); };
            canvas.PointerReleased += (_, e) => { if (drag) { drag = false; if (pi >= 0) engine.EndAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id); e.Pointer.Capture(null); } };
            canvas.SizeChanged += (_, _) => Vis(G(id));
            (into ?? readouts).Add(() => { if (!drag) Vis(G(id)); });
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 5, VerticalAlignment = VerticalAlignment.Center };
            grid.Children.Add(canvas); grid.Children.Add(Col(val, 1));
            if (pi >= 0) MidiLearn.Bind(grid, MidiTarget.PluginParam(track, -1, pi), id);
            return grid;
        }
        // Label + slider on one grid row.
        Control SliderRow(string label, string id, Func<double, string> fmt, double labW = 38, double valW = 24, Func<bool>? dim = null, List<Action>? into = null)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions($"{labW},*"), VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(Caps(label)); g.Children.Add(Col(HSlider(id, fmt, valW, dim, into), 1));
            return g;
        }

        // Vertical wheel bound to a param (pitch springs back to centre).
        Control VWheel(string id, string name, Func<double, string> fmt, IBrush lit, bool spring)
        {
            int pi = I(id);
            var grad = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(NotaPalette.SurfaceRaised.Color, 0), new GradientStop(NotaPalette.BgSunken.Color, 0.5), new GradientStop(NotaPalette.SurfaceRaised.Color, 1) } };
            var bar = new Border { Width = 16, Background = grad, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), VerticalAlignment = VerticalAlignment.Stretch };
            var mark = new Border { Height = 2, Width = 12, Background = lit, CornerRadius = new CornerRadius(1) };
            var lay = new Canvas { Width = 16 };
            lay.Children.Add(mark); Canvas.SetLeft(mark, 2);
            var host = new Panel { Width = 16, VerticalAlignment = VerticalAlignment.Stretch, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.SizeNorthSouth), Children = { bar, lay } };
            bool drag = false; DispatcherTimer? springT = null;
            void Vis(double v) { double h = host.Bounds.Height; if (h <= 0) h = 90; Canvas.SetTop(mark, (1 - v) * (h - 2)); }
            void From(PointerEventArgs e) { double h = host.Bounds.Height; double v = h > 0 ? Math.Clamp(1 - e.GetPosition(host).Y / h, 0, 1) : 0.5; if (pi >= 0) engine.PluginParamSet(track, -1, pi, (float)v); Vis(v); Refresh(); }
            void Spring()
            {
                springT?.Stop();
                springT = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                springT.Tick += (_, _) =>
                {
                    double v = G(id); v += (0.5 - v) * 0.32;
                    if (Math.Abs(v - 0.5) < 0.002) { v = 0.5; springT!.Stop(); }
                    if (pi >= 0) engine.PluginParamSet(track, -1, pi, (float)v); Vis(v); Refresh();
                };
                springT.Start();
            }
            host.PointerPressed += (_, e) => { springT?.Stop(); drag = true; if (pi >= 0) engine.BeginAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id); e.Pointer.Capture(host); From(e); };
            host.PointerMoved += (_, e) => { if (drag) From(e); };
            host.PointerReleased += (_, e) => { if (drag) { drag = false; if (pi >= 0) engine.EndAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id); e.Pointer.Capture(null); if (spring) Spring(); } };
            host.SizeChanged += (_, _) => Vis(G(id));
            var nm = new TextBlock { Text = name, FontSize = 7, FontWeight = FontWeight.Bold, Foreground = MutedC, HorizontalAlignment = HorizontalAlignment.Center };
            readouts.Add(() => { if (!drag) Vis(G(id)); bool live = Math.Abs(G(id) - (spring ? 0.5f : 0f)) > 0.01f; nm.Foreground = live ? AmberLit : MutedC; });
            var col = new DockPanel { HorizontalAlignment = HorizontalAlignment.Center };
            DockPanel.SetDock(nm, Avalonia.Controls.Dock.Bottom);
            col.Children.Add(nm); col.Children.Add(host);
            if (pi >= 0) MidiLearn.Bind(col, MidiTarget.PluginParam(track, -1, pi), name);
            return col;
        }

        // ---- formatters -------------------------------------------------------------
        static double Exp(double lo, double hi, double v) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0, 1));
        static string Time(double s) => s >= 1 ? $"{s:0.0} s" : s >= 0.0995 ? $"{s * 1000:0} ms" : $"{s * 1000:0.#} ms";
        static string ShortTime(double s) => s >= 1 ? $"{s:0.0}s" : $"{s * 1000:0}ms";
        string Atk(float v) => ShortTime(Exp(0.0005, 10, v));
        string Dec(float v) => ShortTime(Exp(0.002, 15, v));
        string Glide(double v) => v < 0.002 ? "off" : Time(Exp(0.005, 10, v));
        string HzFmt(double hz) => hz >= 1000 ? $"{hz / 1000:0.00}k" : $"{hz:0}";
        string Cut(float v) => HzFmt(20 * Math.Pow(1000, v));
        static string Pct(double v) => $"{v * 100:0} %";
        static string Tenths(double v) => $"{v * 10:0.0}";
        string LfoRate(float v) => On("lfosync") ? SyncNames[Math.Clamp((int)Math.Round(v * 13), 0, 13)] : $"{Exp(0.05, 20, v):0.0#} Hz";
        string VolDb(float v) { double g = 2 * v * v; return g <= 1e-4 ? "−∞" : $"{20 * Math.Log10(g):0.0} dB"; }
        int BendSt() => 1 + Math.Clamp((int)Math.Round(G("bendrange") * 11), 0, 11);

        // ======================================================================
        // LEFT — wheels
        // ======================================================================
        var bendTxt = MonoText("", 7, AmberLit); var modTxt = MonoText("", 7, MutedC);
        bendTxt.HorizontalAlignment = HorizontalAlignment.Center; modTxt.HorizontalAlignment = HorizontalAlignment.Center;
        readouts.Add(() => { bendTxt.Text = $"±{BendSt()} st"; modTxt.Text = $"{G("modwheel") * 100:0} %"; modTxt.Foreground = G("modwheel") > 0.01f ? AmberLit : MutedC; });
        var wheelsBody = new DockPanel { LastChildFill = true };
        var wTitle = Caps("WHEELS"); wTitle.HorizontalAlignment = HorizontalAlignment.Center; wTitle.Margin = new Thickness(0, 0, 0, 4);
        wheelsBody.Children.Add(Docked(wTitle, Avalonia.Controls.Dock.Top));
        wheelsBody.Children.Add(Docked(modTxt, Avalonia.Controls.Dock.Bottom));
        wheelsBody.Children.Add(Docked(bendTxt, Avalonia.Controls.Dock.Bottom));
        wheelsBody.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 3), Children = {
            VWheel("bend", "PITCH", v => "", Amber, spring: true),
            VWheel("modwheel", "MOD", v => "", AmberLit, spring: false) } });
        var wheels = new Border { Width = 56, Background = Panel, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(0, 5), Child = wheelsBody };
        DockPanel.SetDock(wheels, Avalonia.Controls.Dock.Left);

        // ======================================================================
        // CENTRE — Oscillators
        // ======================================================================
        const string OscCols = "18,40,40,40,*,62";
        Control Cell(Control c, int col, HorizontalAlignment ha = HorizontalAlignment.Center) { c.HorizontalAlignment = ha; c.VerticalAlignment = VerticalAlignment.Center; return Col(new Panel { Children = { c } }, col); }
        Control OscRow(string o)
        {
            bool isA = o == "a";
            string p = "o" + o;
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions(OscCols) };
            g.Children.Add(Cell(Lbl(isA ? "A" : "B", 10, TxtC, FontWeight.SemiBold), 0, HorizontalAlignment.Left));
            g.Children.Add(Cell(K($"{p}oct", "", v => Feet[Math.Clamp((int)Math.Round(v * 3), 0, 3)], false, 26, 40), 1));
            g.Children.Add(Cell(K($"{p}semi", "", v => { int s = (int)Math.Round((v - 0.5f) * 24); return s == 0 ? "0 st" : $"{s:+0;-0} st"; }, false, 26, 40), 2));
            g.Children.Add(Cell(K($"{p}fine", "", v => { double c = (v - 0.5) * 100; return Math.Abs(c) < 0.5 ? "0 c" : $"{c:+0;-0} c"; }, false, 26, 40), 3));
            var chips = isA ? Row(3, WaveChip("oasaw", 0), WaveChip("oapulse", 1)) : Row(3, WaveChip("obsaw", 0), WaveChip("obtri", 2), WaveChip("obpulse", 1));
            var pw = HSlider($"{p}pw", v => Pct(v), 30, () => !On($"{p}pulse"));
            pw.Width = 110;
            g.Children.Add(Cell(Row(6, chips, pw), 4));
            Control opts = isA ? Toggle("oasync", "Sync")
                : new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Children = { Toggle("oblofreq", "Lo freq"), Toggle("obkbd", "Kbd") } };
            g.Children.Add(Cell(opts, 5, HorizontalAlignment.Right));
            return new Border { BorderBrush = BorderIn, BorderThickness = new Thickness(0, 1, 0, 0), Child = g };
        }
        Control OscTab()
        {
            var hdr = new Grid { ColumnDefinitions = new ColumnDefinitions(OscCols), Height = 13 };
            string[] h = { "OSC", "OCT", "SEMI", "FINE", "SHAPE · PULSE WIDTH", "OPTIONS" };
            for (int i = 0; i < h.Length; i++)
            {
                var t = new TextBlock { Text = h[i], FontSize = 7, FontWeight = FontWeight.Bold, Foreground = DimC, LetterSpacing = 0.8, VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = i == 0 ? HorizontalAlignment.Left : i == h.Length - 1 ? HorizontalAlignment.Right : HorizontalAlignment.Center };
                hdr.Children.Add(Col(t, i));
            }
            // bottom row: glide · voice mode · voices
            var glide = Row(4, K("glide", "GLIDE", v => Glide(v), false, 26, 44), Chips("glidemode", new[] { "Off", "On", "Leg" }));
            var mode = Row(6, Caps("MODE"), Chips("voicemode", new[] { "Poly", "Uni", "Mono" }));
            var strip = new PentadVoiceStrip { Width = 84, Height = 14 }; voiceStrips.Add(strip);
            var voices = Row(6, Caps("VOICES"), strip);
            var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), ColumnSpacing = 14, Height = 50 };
            bottom.Children.Add(glide); bottom.Children.Add(Col(mode, 1)); bottom.Children.Add(Col(voices, 3));
            var rows = new Grid { RowDefinitions = new RowDefinitions("*,*") };
            var rb = OscRow("b"); Grid.SetRow(rb, 1);
            rows.Children.Add(OscRow("a")); rows.Children.Add(rb);
            return new DockPanel { LastChildFill = true, Margin = new Thickness(6, 0, 6, 2), Children = {
                Docked(hdr, Avalonia.Controls.Dock.Top),
                Docked(new Border { BorderBrush = BorderIn, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 2, 0, 0), Child = bottom }, Avalonia.Controls.Dock.Bottom),
                rows } };
        }

        // ======================================================================
        // CENTRE — Filter · Amp
        // ======================================================================
        Control FilterPanel()
        {
            filtCurve.Changed = (c, r) =>
            {
                if (I("cutoff") is var ci and >= 0) engine.PluginParamSet(track, -1, ci, (float)c);
                if (I("reso") is var ri and >= 0) engine.PluginParamSet(track, -1, ri, (float)r);
                Refresh();
            };
            filtCurve.DragStarted += () => { engine.BeginAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, "cutoff"); engine.BeginAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, "reso"); };
            filtCurve.DragEnded += () => { engine.EndAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, "cutoff"); engine.EndAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, "reso"); };
            var graph = new Border { Background = Inset, BorderBrush = BorderIn, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 3, 0, 3), Child = filtCurve };
            var keyPct = MonoText("", 7, Txt2);
            readouts.Add(() => keyPct.Text = Pct(G("keytrk")));
            var key = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Children = { Caps("KEY TRK"), Chips("keytrk", new[] { "0", "½", "1" }), keyPct } };
            var knobs = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,Auto") };
            knobs.Children.Add(K("cutoff", "CUTOFF", Cut, false, 32, 44));
            knobs.Children.Add(Col(K("reso", "RESON", v => Tenths(v), false, 32, 44), 1));
            knobs.Children.Add(Col(K("fenvamt", "ENV AMT", v => Tenths(v), true, 32, 44), 2));
            knobs.Children.Add(Col(key, 3));
            var body = new DockPanel { LastChildFill = true, Children = { Docked(Caps("FILTER"), Avalonia.Controls.Dock.Top), Docked(knobs, Avalonia.Controls.Dock.Bottom), graph } };
            return new Border { Width = 196, BorderBrush = BorderIn, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(7, 4), Child = body };
        }
        Control EnvPanel(string title, string pre, PentadEnvCurve curve, bool amp)
        {
            var times = MonoText("", 7, Txt2);
            readouts.Add(() => times.Text = $"{Atk(G(pre + "attack"))} · {Dec(G(pre + "decay"))} · {G(pre + "sustain") * 100:0}% · {(On("releaseon") ? Dec(G(pre + "release")) : "min")}");
            var head = new DockPanel { Height = 12, LastChildFill = false, Margin = new Thickness(0, 0, 0, 2) };
            head.Children.Add(Docked(Caps(title), Avalonia.Controls.Dock.Left));
            if (amp) head.Children.Add(Docked(Toggle("releaseon", "Rel"), Avalonia.Controls.Dock.Right));
            head.Children.Add(Docked(new Border { Margin = new Thickness(6, 0), Child = times }, Avalonia.Controls.Dock.Right));
            var graph = new Border { Background = Inset, BorderBrush = BorderIn, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = curve };
            var knobs = Row(0, K(pre + "attack", "A", Atk, amp, 24, 29), K(pre + "decay", "D", Dec, amp, 24, 29), K(pre + "sustain", "S", v => Pct(v), amp, 24, 29), K(pre + "release", "R", Dec, amp, 24, 29));
            knobs.Margin = new Thickness(4, 0, 0, 0);
            return new DockPanel { LastChildFill = true, Children = { Docked(head, Avalonia.Controls.Dock.Top), Docked(knobs, Avalonia.Controls.Dock.Right), graph } };
        }
        Control FilterAmpTab()
        {
            var envs = new Grid { RowDefinitions = new RowDefinitions("*,*"), Margin = new Thickness(7, 4) };
            var fe = EnvPanel("FILTER ENV", "f", fEnvCurve, false);
            var ae = Divider(EnvPanel("AMP ENV", "a", aEnvCurve, true), 4); ae.Margin = new Thickness(0, 4, 0, 0); Grid.SetRow(ae, 1);
            envs.Children.Add(fe); envs.Children.Add(ae);
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            g.Children.Add(FilterPanel()); g.Children.Add(Col(envs, 1));
            return g;
        }

        // ======================================================================
        // CENTRE — Poly Mod · LFO · Wheel Mod
        // ======================================================================
        string Dests()
        {
            var d = new List<string>();
            if (On("pmfreqa")) d.Add("Freq A"); if (On("pmpwa")) d.Add("PW A"); if (On("pmfilter")) d.Add("Filter");
            return d.Count == 0 ? "no dest" : string.Join(" + ", d);
        }
        int ActiveRoutes()
        {
            int dn = (On("pmfreqa") ? 1 : 0) + (On("pmpwa") ? 1 : 0) + (On("pmfilter") ? 1 : 0);
            return dn * ((G("pmenv") > 0.001f ? 1 : 0) + (G("pmoscb") > 0.001f ? 1 : 0));
        }
        Control RouteRow(string src, string id, bool isPm, Func<string> dest, string? dimWhenZeroOf = null)
        {
            var srcT = new TextBlock { Text = src, FontSize = 8, Foreground = TxtC, Width = 58, VerticalAlignment = VerticalAlignment.Center };
            var destT = new TextBlock { FontSize = 8, Foreground = TxtC, Width = 84, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            bool Dim() => G(id) < 0.001f || (isPm && !On("pmfreqa") && !On("pmpwa") && !On("pmfilter"));
            readouts.Add(() => { destT.Text = dest(); bool d = Dim(); srcT.Foreground = d ? Txt2 : TxtC; destT.Foreground = d ? Txt2 : TxtC; });
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*"), ColumnSpacing = 6, Height = 18 };
            g.Children.Add(srcT);
            g.Children.Add(Col(new TextBlock { Text = "→", FontSize = 8, Foreground = DimC, VerticalAlignment = VerticalAlignment.Center }, 1));
            g.Children.Add(Col(destT, 2));
            g.Children.Add(Col(HSlider(id, v => Tenths(v), 26, Dim), 3));
            return g;
        }
        Control PolyModTab()
        {
            var dests = Row(12, Caps("DEST"), Toggle("pmfreqa", "Osc A freq"), Toggle("pmpwa", "Osc A PW"), Toggle("pmfilter", "Filter cutoff"));
            dests.Height = 18;
            var vel = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 12, Height = 18, Children = {
                SliderRow("VEL → AMP", "velamp", v => Pct(v), 52, 30),
                Col(SliderRow("VEL → FILT", "velfilt", v => Pct(v), 52, 30), 1) } };
            var at = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 12, Height = 18, Children = {
                SliderRow("AT → CUT", "atcutoff", v => Pct(v), 52, 30),
                Col(SliderRow("AT → LFO", "atlfo", v => Pct(v), 52, 30), 1) } };
            var routes = new StackPanel { Spacing = 1, Children = {
                RouteRow("Filter env", "pmenv", true, Dests),
                RouteRow("Osc B", "pmoscb", true, Dests),
                dests, vel, at } };

            // LFO
            var lfoWaves = Row(2, WaveChip("lfotri", 2, 20, 14), WaveChip("lfosaw", 0, 20, 14), WaveChip("lfosquare", 3, 20, 14));
            var lfo = new StackPanel { Spacing = 2, Children = {
                Caps("LFO"),
                Row(4, K("lforate", "RATE", LfoRate, false, 28, 38), K("lfoamt", "AMOUNT", v => Pct(v), true, 28, 38),
                    new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Children = { lfoWaves, Chips("lfosync", new[] { "Free", "Sync" }) } }) } };
            // Wheel mod
            var wmGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), RowSpacing = 3, ColumnSpacing = 6 };
            void W(string id, string l, int r, int c) { var t = Toggle(id, l); Grid.SetRow(t, r); Grid.SetColumn(t, c); wmGrid.Children.Add(t); }
            W("wmfreqa", "Freq A", 0, 0); W("wmfreqb", "Freq B", 0, 1); W("wmfilter", "Filter", 0, 2);
            W("wmpwa", "PW A", 1, 0); W("wmpwb", "PW B", 1, 1);
            var mixRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), ColumnSpacing = 5, Children = {
                Caps("SOURCE"), Col(Lbl("LFO", 7, DimC, FontWeight.Normal), 1), Col(HSlider("wmmix", v => Pct(v), 26), 2), Col(Lbl("NOISE", 7, DimC, FontWeight.Normal), 3) } };
            var wm = new StackPanel { Spacing = 3, Children = { Caps("WHEEL MOD — DESTINATIONS"), wmGrid, mixRow } };
            var wmBox = new Border { BorderBrush = BorderIn, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(10, 0, 0, 0), Child = wm };
            var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10, Children = { lfo, Col(wmBox, 1) } };

            return new DockPanel { LastChildFill = true, Margin = new Thickness(8, 3, 8, 2), Children = {
                Docked(Caps("POLY MOD — SOURCE → DESTINATION"), Avalonia.Controls.Dock.Top),
                Docked(Divider(bottom, 4), Avalonia.Controls.Dock.Bottom),
                new Border { Padding = new Thickness(0, 2, 0, 0), Child = routes } } };
        }

        // ======================================================================
        // RIGHT — Mixer / Output
        // ======================================================================
        Control MixRow(string onId, string lvlId, string name)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,34,*"), ColumnSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(Toggle(onId, ""));
            g.Children.Add(Col(Caps(name), 1));
            g.Children.Add(Col(HSlider(lvlId, v => Tenths(v), 20, () => !On(onId)), 2));
            return g;
        }
        Control MixerTab()
        {
            bool UniOff() => G("voicemode") < 0.25f && G("unison") < 0.25f;
            var noiseType = Chips("noisecolor", new[] { "White", "Pink" }); noiseType.Margin = new Thickness(24, 0, 0, 0);
            var uni = Row(6, new TextBlock { Text = "UNISON", FontSize = 7, FontWeight = FontWeight.Bold, Foreground = MutedC, Width = 38, VerticalAlignment = VerticalAlignment.Center }, Chips("unison", new[] { "Off", "×2", "×5" }));
            var det = SliderRow("DETUNE", "unidetune", v => $"{v * 50:0} c", 38, 26, UniOff);
            var sum = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 6, Children = { Caps("SUM"), Col(sumBar, 1), Col(sumTxt, 2) } };
            var g = new Grid { RowDefinitions = new RowDefinitions("*,*,*,*,Auto,*,*,*") };
            Control[] rows = { MixRow("mixaon", "mixa", "OSC A"), MixRow("mixbon", "mixb", "OSC B"), MixRow("mixnoiseon", "mixnoise", "NOISE"), noiseType,
                new Border { Height = 1, Background = BorderIn, Margin = new Thickness(0, 3) }, uni, det, sum };
            for (int r = 0; r < rows.Length; r++) { rows[r].VerticalAlignment = r == 4 ? VerticalAlignment.Center : VerticalAlignment.Center; Grid.SetRow(rows[r], r); g.Children.Add(rows[r]); }
            return new Border { Padding = new Thickness(6, 4), Child = g };
        }
        Control OutputTab()
        {
            var scale = new Grid { RowDefinitions = new RowDefinitions("*,*,*"), Height = 56, Children = {
                MonoText("0", 7, DimC), WithRow(MonoText("−12", 7, DimC), 1), WithRow(MonoText("−48", 7, DimC), 2) } };
            var meters = Row(4, new StackPanel { Spacing = 2, Children = { meterL, Center(Lbl("L", 7, MutedC, FontWeight.Normal)) } },
                                new StackPanel { Spacing = 2, Children = { meterR, Center(Lbl("R", 7, MutedC, FontWeight.Normal)) } }, scale);
            meters.VerticalAlignment = VerticalAlignment.Top;
            var top = Row(10, K("volume", "VOLUME", VolDb, false, 40, 54), meters);
            var bend = Row(6, new TextBlock { Text = "BEND", FontSize = 7, FontWeight = FontWeight.Bold, Foreground = MutedC, Width = 38, VerticalAlignment = VerticalAlignment.Center },
                Chips("bendrange", new[] { "2", "5", "7", "12" }, new[] { 1 / 11f, 4 / 11f, 6 / 11f, 1f }), Lbl("st", 7, MutedC, FontWeight.Normal));
            var velF = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand) };
            {   // Velocity → filter: a quick switch over the Velocity Filter amount (0 ↔ 50 %).
                var pill = new Border { Width = 18, Height = 10, CornerRadius = new CornerRadius(5) };
                var dot = new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(4) };
                var host = new Canvas { Width = 18, Height = 10 }; Canvas.SetTop(dot, 1.5); host.Children.Add(dot); pill.Child = host;
                var tx = new TextBlock { Text = "Velocity → filter", FontSize = 8, VerticalAlignment = VerticalAlignment.Center };
                velF.Child = Row(5, pill, tx);
                void Hi() { bool on = G("velfilt") > 0.001f; pill.Background = on ? Amber : OffPill; dot.Background = on ? Panel : MutedC; Canvas.SetLeft(dot, on ? 9.5 : 1.5); tx.Foreground = on ? TxtC : Txt2; }
                velF.PointerPressed += (_, e) => { SetP("velfilt", G("velfilt") > 0.001f ? 0f : 0.5f); Refresh(); e.Handled = true; };
                readouts.Add(Hi); Hi();
            }
            var strip = new PentadVoiceStrip { Width = 90, Height = 14 }; voiceStrips.Add(strip);
            var voices = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 6, Children = { Caps("VOICES"), Col(strip, 1), Col(Right(voiceCountTxt), 2) } };
            var body = new DockPanel { LastChildFill = false, Children = {
                Docked(top, Avalonia.Controls.Dock.Top),
                Docked(Divider(new StackPanel { Spacing = 6, Children = { SliderRow("SPREAD", "spread", v => Pct(v), 38, 30), bend, velF } }), Avalonia.Controls.Dock.Top),
                Docked(Divider(voices), Avalonia.Controls.Dock.Bottom) } };
            ((Control)body.Children[1]).Margin = new Thickness(0, 6, 0, 0);
            return new Border { Padding = new Thickness(6, 4), Child = body };
        }
        static Control WithRow(Control c, int r) { Grid.SetRow(c, r); return c; }
        static Control Center(Control c) { c.HorizontalAlignment = HorizontalAlignment.Center; return c; }
        static Control Right(Control c) { c.HorizontalAlignment = HorizontalAlignment.Right; return c; }

        // ======================================================================
        // Tab frames + extras
        // ======================================================================
        var centreHost = new ContentControl();
        var rightHost = new ContentControl();
        var extrasHost = new ContentControl { VerticalAlignment = VerticalAlignment.Center };
        var tabBodies = new Control?[3]; var rightBodies = new Control?[2];
        int centreTab = 0;

        // Drift slider + seed re-roll (Oscillators tab bar)
        var reroll = new Border { Padding = new Thickness(3, 0), CornerRadius = new CornerRadius(3), Background = OffPill, Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "⟳", FontSize = 9, Foreground = Txt2 } };
        ToolTip.SetTip(reroll, "Re-roll the vintage seed (new per-voice spread)");
        reroll.PointerPressed += (_, e) => { SetP("seed", (float)Random.Shared.NextDouble()); Refresh(); e.Handled = true; };
        var driftSl = HSlider("drift", v => $"{v * 100:0}%", 22); driftSl.Width = 76;
        Control[] extras = {
            Row(5, Caps("DRIFT"), driftSl, reroll),
            MonoText("4-pole LP · 24 dB/oct", 7, MutedC),
            MonoText("", 7, MutedC) };
        readouts.Add(() => { if (extras[2] is TextBlock t) { int n = ActiveRoutes(); t.Text = $"per voice · {n} active route{(n == 1 ? "" : "s")}"; } });

        Control CentreBody(int t) => tabBodies[t] ??= t switch { 1 => FilterAmpTab(), 2 => PolyModTab(), _ => OscTab() };
        Control RightBody(int t) => rightBodies[t] ??= t == 1 ? OutputTab() : MixerTab();

        var statusLeft = new TextBlock { FontSize = 8, Foreground = Txt2, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var centre = TabFrame(new[] { "Oscillators", "Filter · Amp", "Poly Mod" }, centreHost, CentreBody, false, t => { centreTab = t; extrasHost.Content = extras[t]; Refresh(); }, extrasHost);
        var rightFrame = TabFrame(new[] { "Mixer", "Output" }, rightHost, RightBody, true, null, null);
        var right = new Border { Width = 186, Background = Panel, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Child = rightFrame };
        DockPanel.SetDock(right, Avalonia.Controls.Dock.Right);
        var centreBox = new Border { Background = Panel, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Margin = new Thickness(5, 0), Child = centre };

        // ======================================================================
        // Status strip + Voice setup flyout
        // ======================================================================
        string StatusText()
        {
            int poly = Poly(G("polyphony"));
            string modeS = Sel("voicemode", 3) switch { 1 => $"Unison ×{poly}", 2 => "Mono", _ => $"Poly {poly}" };
            int busy = scN >= 2 ? (int)scope[1] : 0;
            return centreTab switch
            {
                1 => $"Filter env → cutoff {G("fenvamt") * 10:0.0} · key tracking {KeyName(G("keytrk"))}{(G("velfilt") > 0.001f ? " · velocity → filter" : "")}{(On("lowcomp") ? " · bass comp" : "")}",
                2 => $"Poly Mod: {ActiveRoutes()} route{(ActiveRoutes() == 1 ? "" : "s")} · LFO {LfoRate(G("lforate"))} {LfoShape()} · unison {(G("unison") < 0.25f ? "off" : G("unison") < 0.75f ? "×2" : "×5")}, detune {G("unidetune") * 50:0} c",
                _ => $"{modeS} · {busy} voice{(busy == 1 ? "" : "s")} busy · glide {Glide(G("glide"))} · drift {G("drift") * 100:0} %",
            };
        }
        static string KeyName(float v) => Math.Abs(v - 0.5f) < 0.02f ? "½" : Math.Abs(v - 1f) < 0.02f ? "full" : v < 0.02f ? "off" : $"{v * 100:0} %";
        string LfoShape()
        {
            var s = new List<string>();
            if (On("lfotri")) s.Add("tri"); if (On("lfosaw")) s.Add("saw"); if (On("lfosquare")) s.Add("square");
            return s.Count == 0 ? "(no wave)" : string.Join("+", s);
        }
        var statusRight = MonoText("", 8, Txt2);
        readouts.Add(() =>
        {
            statusLeft.Text = StatusText();
            if (scN >= 8) statusRight.Text = $"{scope[7] / 1000:0.#} kHz · ×{(int)scope[6]} OS · CPU {scope[5] * 100:0.0} %";
        });

        var flyReadouts = new List<Action>();
        Flyout? setupFly = null;
        Control SetupPanel()
        {
            var seedTxt = MonoText("", 8, TxtC);
            flyReadouts.Add(() => seedTxt.Text = $"#{(int)Math.Round(G("seed") * 16777215):X6}");
            var rr = new Border { Padding = new Thickness(6, 1), CornerRadius = new CornerRadius(3), Background = OffPill, Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = "Re-roll", FontSize = 8, Foreground = TxtC } };
            rr.PointerPressed += (_, e) => { SetP("seed", (float)Random.Shared.NextDouble()); Refresh(); foreach (var a in flyReadouts) a(); e.Handled = true; };
            Control R(string label, Control c) { var g = new Grid { ColumnDefinitions = new ColumnDefinitions("92,*"), Height = 20 }; g.Children.Add(Caps(label)); c.HorizontalAlignment = HorizontalAlignment.Left; g.Children.Add(Col(c, 1)); return g; }
            var tune = HSlider("tune", v => { double c = (v - 0.5) * 200; return Math.Abs(c) < 0.5 ? "0 c" : $"{c:+0;-0} c"; }, 30, null, flyReadouts); tune.Width = 130;
            var at = HSlider("aftertouch", v => Pct(v), 30, null, flyReadouts); at.Width = 130;
            var p = new StackPanel { Spacing = 2, Width = 250, Children = {
                Lbl("VOICE SETUP", 9, TxtC),
                new Border { Height = 4 },
                R("VOICES", Chips("polyphony", new[] { "5", "10", "16" }, null, flyReadouts, 8)),
                R("ALLOCATION", Chips("alloc", new[] { "Rotate", "Oldest" }, null, flyReadouts, 8)),
                R("GLIDE", Chips("glidemode", new[] { "Off", "On", "Legato" }, null, flyReadouts, 8)),
                R("RELEASE", Toggle("releaseon", "Release switch", flyReadouts)),
                R("OVERSAMPLING", Chips("oversample", new[] { "×2", "×4" }, null, flyReadouts, 8)),
                R("BASS COMP", Toggle("lowcomp", "Restore lows at high Q", flyReadouts)),
                R("NOISE FLOOR", Toggle("noisefloor", "−100 dBFS hiss", flyReadouts)),
                R("VINTAGE SEED", Row(8, seedTxt, rr)),
                R("MASTER TUNE", tune),
                R("AFTERTOUCH", at) } };
            return new Border { Padding = new Thickness(4, 2), Child = p };
        }
        var voiceBtn = new Border { Height = 14, Padding = new Thickness(6, 0), CornerRadius = new CornerRadius(3), Background = OffPill, Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "Voice ▾", FontSize = 8, Foreground = TxtC, VerticalAlignment = VerticalAlignment.Center } };
        ToolTip.SetTip(voiceBtn, "Voice setup: polyphony, allocation, glide mode, oversampling, vintage seed");
        voiceBtn.PointerPressed += (_, e) =>
        {
            setupFly ??= new Flyout { Content = SetupPanel(), Placement = PlacementMode.TopEdgeAlignedRight };
            foreach (var a in flyReadouts) a();
            setupFly.ShowAt(voiceBtn);
            e.Handled = true;
        };
        readouts.Add(() => { if (setupFly?.IsOpen == true) foreach (var a in flyReadouts) a(); });

        var statusGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8 };
        statusGrid.Children.Add(statusLeft); statusGrid.Children.Add(Col(voiceBtn, 1)); statusGrid.Children.Add(Col(statusRight, 2));
        var status = new Border { Height = 18, Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(8, 0), Child = statusGrid };
        DockPanel.SetDock(status, Avalonia.Controls.Dock.Bottom);

        // ---- assemble ---------------------------------------------------------------
        var bodyRow = new DockPanel { LastChildFill = true, Margin = new Thickness(5), Children = { wheels, right, centreBox } };
        var root = new DockPanel { LastChildFill = true, Background = RailBg, Children = { status, bodyRow } };

        ctx.SetInstLiveViz(Refresh);
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
            var bar = centered ? (Panel)new UniformGrid { Rows = 1 } : new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < tabs.Length; i++)
            {
                int iv = i;
                var b = new Border { Padding = new Thickness(9, 0), BorderThickness = new Thickness(0, 0, 0, 2), BorderBrush = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand),
                    Child = new TextBlock { Text = tabs[i], FontSize = 9, Foreground = MutedC, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center } };
                b.PointerPressed += (_, _) => { sel = iv; Hi(); host.Content = body(iv); changed?.Invoke(iv); };
                btns[i] = b; bar.Children.Add(b);
            }
            var barDock = new DockPanel { Height = 20, LastChildFill = centered };
            if (extrasCtl != null) { var ex = new Border { Padding = new Thickness(0, 0, 8, 0), Child = extrasCtl }; DockPanel.SetDock(ex, Avalonia.Controls.Dock.Right); barDock.Children.Add(ex); }
            if (!centered) DockPanel.SetDock(bar, Avalonia.Controls.Dock.Left);
            barDock.Children.Add(bar);
            var barBorder = new Border { BorderBrush = Border2, BorderThickness = new Thickness(0, 0, 0, 1), Child = barDock };
            DockPanel.SetDock(barBorder, Avalonia.Controls.Dock.Top);
            Hi(); host.Content = body(sel); changed?.Invoke(sel);
            return new DockPanel { LastChildFill = true, Children = { barBorder, host } };
        }
    }
}
