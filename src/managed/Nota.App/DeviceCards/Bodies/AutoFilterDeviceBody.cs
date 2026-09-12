// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Auto Filter (kind 7) body, rebuilt to mockup 2g (the
// 700×260 standard shell, superseding the old 1440-wide 2b). A LIVE strip (filter type
// icons · slope · Freq · Res sliders · Clean/Analog) over a body of graph | modulation
// column (ENV + LFO as two teal lanes, each a switch + shape + three knobs) | a 108px
// output rail (Drive / Dry-Wet / Gain + a compact sidechain). The graph keeps top billing.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed class AutoFilterDeviceBody : IDeviceBody
{
    // Param indices — must match AutoFilter.h.
    private const int Freq = 0, Res = 1, Type = 2, Slope = 3, Morph = 4, EnvAmt = 5, EnvAtt = 6,
                      EnvRel = 7, EnvHold = 8, LfoAmt = 9, LfoRate = 10, LfoWave = 11, LfoMorph = 12,
                      Drive = 13, DryWet = 14, EnvOn = 15, LfoOn = 16, Gain = 17, Circuit = 18,
                      LfoSync = 19, LfoPhase = 20;

    private static readonly string[] DivNames = { "2/1", "1/1", "1/2", "1/4", "1/8", "1/16", "1/32", "1/64" };

    private static readonly IBrush HdrBg = NotaPalette.SurfaceCard;
    private static readonly IBrush RailBg = NotaPalette.SurfaceInset;
    private static readonly IBrush Border2 = NotaPalette.BorderDefault;
    private static readonly IBrush Inset = NotaPalette.BgSunken;
    private static readonly IBrush FieldBorder = NotaPalette.GraphBorder;
    private static readonly IBrush Amber = NotaPalette.Accent;
    private static readonly IBrush AmberLit = NotaPalette.AccentBright;
    private static readonly IBrush TealC = NotaPalette.Teal;
    private static readonly IBrush TxtC = NotaPalette.TextPrimary;
    private static readonly IBrush MutedC = NotaPalette.TextTertiary;
    private static readonly IBrush AmberSubtle = NotaPalette.Wash(NotaPalette.Accent, 0x28);
    private static readonly IBrush TealSubtle = NotaPalette.Wash(NotaPalette.Teal, 0x24);
    private static readonly IBrush RowLit = NotaPalette.SurfaceRaised;

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

        var curve = new AutoFilterCurve(engine, track, di) { VerticalAlignment = VerticalAlignment.Stretch };
        void SyncCurve() => curve.Set(P(Freq), P(Res), (int)Math.Round(P(Type) * 3), P(Morph), P(Slope) >= 0.5f);
        curve.GestureBegin += () => { Begin(Freq); Begin(Res); };
        curve.GestureEnd += () => { End(Freq); End(Res); };
        curve.CutoffChanged += v => { SetP(Freq, (float)v); SyncCurve(); };
        curve.ResChanged += v => { SetP(Res, (float)v); SyncCurve(); };
        ctx.AddDeviceRefresher(curve.Tick);

        // ---- formatters ----
        string Hz(double v) { double f = Exp(v, 30, 18000); return f >= 1000 ? $"{f / 1000:0.00} kHz" : $"{(int)Math.Round(f)} Hz"; }
        static string PctF(double v) => $"{v * 100:0}%";
        static string Bip(double v) => $"{(v - 0.5) * 200:+0;-0;0}%";
        static string GainF(double v) => $"{(v - 0.5) * 48:+0.0;-0.0;0.0}";
        static string Ms(double v, double lo, double hi) { double m = lo * Math.Pow(hi / lo, v); return m >= 100 ? $"{m:0}ms" : $"{m:0.0}ms"; }
        string AttF(double v) => Ms(v, 0.1, 500);
        string RelF(double v) => Ms(v, 1, 2000);
        string RateF(double v) => P(LfoSync) >= 0.5f ? DivNames[Math.Clamp((int)Math.Round(v * 7), 0, 7)] : $"{Exp(v, 0.01, 40):0.00}Hz";

        var readouts = new List<Action>();

        // ---- gauge-knob cell (own live-follow so unit text isn't overwritten) ----
        Control Cell(string name, int p, Func<double, string> fmt, IBrush? arc = null, double size = 34, double cellW = 0)
        {
            var value = new TextBlock { Text = fmt(P(p)), FontSize = 9, Foreground = TxtC };
            value.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
            var knob = new Knob(P(p), 1.0) { Accent = true, ArcColor = arc, Default = engine.DeviceParamDefault(track, di, p), Width = size, Height = size };
            knob.ValueChanged += v => { SetP(p, (float)v); value.Text = fmt(v); SyncCurve(); };
            knob.GestureBegin += () => Begin(p);
            knob.GestureEnd += () => End(p);
            MidiLearn.Bind(knob, MidiTarget.DeviceParam(track, di, p), name);
            readouts.Add(() => { if (!knob.Dragging) { float c = P(p); if (Math.Abs(c - knob.Value) > 1e-3) knob.Value = c; value.Text = fmt(P(p)); } });
            return KnobCell(name, knob, value, cellW > 0 ? cellW : size + 20);
        }
        Control Cap(string t, IBrush? c = null) => new TextBlock { Text = t, FontSize = 8, FontWeight = FontWeight.Bold, Foreground = c ?? MutedC, VerticalAlignment = VerticalAlignment.Center };

        // Segmented pill over a normalized param.
        Control Seg(int p, string[] opts, bool teal = false)
        {
            int n = opts.Length; var cells = new Border[n]; var texts = new TextBlock[n];
            void Sync() { int cur = (int)Math.Round(P(p) * (n - 1)); for (int i = 0; i < n; i++) { bool on = i == cur; cells[i].Background = on ? (teal ? TealSubtle : AmberSubtle) : Brushes.Transparent; cells[i].BorderBrush = on ? (teal ? TealC : Amber) : Brushes.Transparent; texts[i].Foreground = on ? (teal ? TealC : AmberLit) : MutedC; } }
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
            for (int i = 0; i < n; i++) { int iv = i; var tb = new TextBlock { Text = opts[i], FontSize = 9, Foreground = MutedC }; var c = new Border { CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), Padding = new Thickness(6, 1), Cursor = new Cursor(StandardCursorType.Hand), Child = tb }; c.PointerPressed += (_, e) => { e.Handled = true; SetP(p, n > 1 ? iv / (float)(n - 1) : 0f); SyncCurve(); RefreshAll(); }; cells[i] = c; texts[i] = tb; row.Children.Add(c); }
            readouts.Add(Sync);
            var seg = new Border { Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Child = row };
            MidiLearn.Bind(seg, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            return seg;
        }

        // Horizontal param slider (LIVE strip + rails).
        Control HSlider(int p, string label, Func<double, string> fmt, double lw, double vw, bool bipolar = false)
        {
            var fill = new Border { Height = 3, Background = Amber, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var track2 = new Border { Height = 3, Background = Inset, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center };
            var center = bipolar ? new Border { Width = 1, Background = NotaPalette.BorderStrong, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch, Margin = new Thickness(0, 1) } : null;
            var handle = new Border { Width = 8, Height = 10, Background = NotaPalette.TextSecondary, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var slot = new Panel { Height = 11, MinWidth = 40 }; slot.Children.Add(track2); if (center != null) slot.Children.Add(center); slot.Children.Add(fill); slot.Children.Add(handle);
            var val = new TextBlock { Text = fmt(P(p)), FontSize = 9, Foreground = TxtC, VerticalAlignment = VerticalAlignment.Center }; val.BindResource(TextBlock.FontFamilyProperty, "Font.Mono"); if (vw > 0) { val.Width = vw; val.TextAlignment = TextAlignment.Right; }
            bool drag = false;
            void Upd() { double v = P(p); double W = slot.Bounds.Width; double hx = v * W; handle.Margin = new Thickness(Math.Clamp(hx - 4, 0, Math.Max(0, W - 8)), 0, 0, 0); if (bipolar) { double c = W * 0.5; double a = Math.Min(c, hx), b = Math.Max(c, hx); fill.Margin = new Thickness(a, 0, 0, 0); fill.Width = Math.Max(0, b - a); } else fill.Width = hx; val.Text = fmt(v); }
            void SetFromX(double x) { double v = Math.Clamp(x / Math.Max(1, slot.Bounds.Width), 0, 1); SetP(p, (float)v); SyncCurve(); Upd(); }
            slot.PointerPressed += (_, e) => { drag = true; e.Pointer.Capture(slot); Begin(p); SetFromX(e.GetPosition(slot).X); };
            slot.PointerMoved += (_, e) => { if (drag) SetFromX(e.GetPosition(slot).X); };
            slot.PointerReleased += (_, e) => { if (drag) { drag = false; e.Pointer.Capture(null); End(p); } };
            readouts.Add(() => { if (!drag) Upd(); });
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 5, VerticalAlignment = VerticalAlignment.Center };
            if (lw > 0) { var lbl = Cap(label); ((TextBlock)lbl).Width = lw; g.Children.Add(lbl); }
            Grid.SetColumn(slot, 1); g.Children.Add(slot); Grid.SetColumn(val, 2); g.Children.Add(val);
            MidiLearn.Bind(g, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            return g;
        }

        // Filter-type icons row (icon-only) for the LIVE strip.
        Control TypeRow()
        {
            var cells = new Border[4]; var icons = new IIconColor[4];
            void Sync() { int cur = (int)Math.Round(P(Type) * 3); for (int i = 0; i < 4; i++) { bool on = i == cur; cells[i].Background = on ? AmberSubtle : Inset; cells[i].BorderBrush = on ? Amber : Border2; icons[i].Color = on ? AmberLit : MutedC; } }
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
            for (int i = 0; i < 4; i++) { int iv = i; var ic = new FilterTypeIcon(i, MutedC); var c = new Border { Width = 34, Height = 22, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Cursor = new Cursor(StandardCursorType.Hand), Child = new Border { Child = ic, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } }; c.PointerPressed += (_, e) => { e.Handled = true; SetP(Type, iv / 3f); SyncCurve(); RefreshAll(); }; cells[i] = c; icons[i] = ic; row.Children.Add(c); }
            readouts.Add(Sync);
            MidiLearn.Bind(row, MidiTarget.DeviceParam(track, di, Type), engine.DeviceParamName(track, di, Type));
            return row;
        }

        // Vertical LFO wave picker (icon + label).
        Control WavePicker()
        {
            var names = new[] { "Sine", "Tri", "Saw", "Sqr", "S&H" };
            var cells = new Border[5];
            var icons = new IIconColor[5];
            var texts = new TextBlock[5];

            void Sync()
            {
                var cur = (int)Math.Round(P(LfoWave) * 4);
                for (var i = 0; i < 5; i++)
                {
                    var on = i == cur;
                    cells[i].Background = on ? TealSubtle : Inset;
                    cells[i].BorderBrush = on ? TealC : Border2;
                    icons[i].Color = on ? TealC : MutedC;
                    texts[i].Foreground = on ? TealC : MutedC;
                }
            }

            var col = new UniformGrid { ColumnSpacing = 2, Rows = 1 };
            for (var i = 0; i < 5; i++)
            {
                var iv = i;
                var ic = new LfoWaveIcon(i, MutedC);
                var tb = new TextBlock
                {
                    Text = names[i], FontSize = 7, FontWeight = FontWeight.SemiBold, Foreground = MutedC,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var c = new Border
                {
                    BorderThickness = new Thickness(1), 
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(3, 2),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Child = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 3,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment =  HorizontalAlignment.Center,
                        Children = {
                            new Border
                            {
                                Child = ic,
                                VerticalAlignment = VerticalAlignment.Center,
                            }, tb }
                    }
                };
                c.PointerPressed += (_, e) =>
                {
                    e.Handled = true;
                    SetP(LfoWave, iv / 4f);
                    RefreshAll();
                };
                cells[i] = c;
                icons[i] = ic;
                texts[i] = tb;
                col.Children.Add(c);
            }

            readouts.Add(Sync);
            MidiLearn.Bind(col, MidiTarget.DeviceParam(track, di, LfoWave), engine.DeviceParamName(track, di, LfoWave));
            return col;
        }

        // Small teal on/off toggle backed by a 0/1 param.
        Control MiniToggle(int p, string label)
        {
            var sw = new ToggleSwitch(P(p) >= 0.5f);
            sw.Changed += on => { SetP(p, on ? 1f : 0f); SyncCurve(); };
            readouts.Add(() => { bool on = P(p) >= 0.5f; if (sw.IsOn != on) sw.IsOn = on; });
            var mt = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = { sw, new TextBlock { Text = label, FontSize = 9, FontWeight = FontWeight.Bold, Foreground = TxtC, VerticalAlignment = VerticalAlignment.Center } } };
            MidiLearn.Bind(mt, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            return mt;
        }

        // Clickable "Sync"/"Stereo N°" chips (LFO header).
        Control ToggleChip(int p, Func<double, string> fmt, bool cycle = false)
        {
            var tb = new TextBlock { Text = fmt(P(p)), FontSize = 8, Foreground = MutedC, VerticalAlignment = VerticalAlignment.Center };
            var b = new Border { CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), Padding = new Thickness(5, 1), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = tb };
            void Sync() { bool on = P(p) >= 0.5f; b.Background = on ? TealSubtle : Brushes.Transparent; b.BorderBrush = on ? TealC : Border2; tb.Foreground = on ? TealC : MutedC; tb.Text = fmt(P(p)); }
            b.PointerPressed += (_, e) => { e.Handled = true; if (cycle) { float nx = P(p) + 0.25f; if (nx > 1.001f) nx = 0f; SetP(p, nx); } else SetP(p, P(p) >= 0.5f ? 0f : 1f); Sync(); };
            readouts.Add(Sync);
            MidiLearn.Bind(b, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            return b;
        }

        // ================= LIVE strip =================
        var live = new Border { Height = 34, Background = HdrBg, BorderBrush = Border2, BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new DockPanel { LastChildFill = false, Margin = new Thickness(9, 0), Children = {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9, VerticalAlignment = VerticalAlignment.Center, Children = {
                    TypeRow(),
                    Seg(Slope, new[] { "12", "24" }),
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Width = 150, Children = { Cap("FREQ"), HSlider(Freq, "", Hz, 0, 52) } },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Width = 116, Children = { Cap("RES"), HSlider(Res, "", PctF, 0, 34) } } } },
                new StackPanel { [DockPanel.DockProperty] = Dock.Right, VerticalAlignment = VerticalAlignment.Center, Children = { Seg(Circuit, new[] { "Clean", "Analog" }) } } } } };

        // ================= graph =================
        var graph = new Border { Padding = new Thickness(8, 7), Child = new Border { Background = Inset, BorderBrush = FieldBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = curve, ClipToBounds = true } };

        // ================= modulation column (ENV + LFO lanes) =================
        Control LaneHeader(int onParam, string name, params Control[] trailing)
        {
            var dp = new DockPanel { LastChildFill = false, Height = 13 };
            var lead = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { } };
            lead.Children.Add(MiniToggle(onParam, name));
            lead.Children.Add(new TextBlock { Text = "→ Freq", FontSize = 8, Foreground = MutedC, VerticalAlignment = VerticalAlignment.Center });
            dp.Children.Add(lead);
            var trail = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, [DockPanel.DockProperty] = Dock.Right };
            foreach (var c in trailing) trail.Children.Add(c);
            dp.Children.Add(trail);
            return dp;
        }
        var envAmtRead = new TextBlock { FontSize = 8, Foreground = TealC, VerticalAlignment = VerticalAlignment.Center }; envAmtRead.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        readouts.Add(() => envAmtRead.Text = Bip(P(EnvAmt)));

        var envLaneHeader = LaneHeader(EnvOn, "ENV", ToggleChip(EnvHold, _ => "Hold"), envAmtRead);
        var envLaneKnobs = new UniformGrid()
        {
            ColumnSpacing = 6,
            Rows = 1,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment =  VerticalAlignment.Center,
            Children =
            {
                Cell("ATTACK", EnvAtt, AttF),
                Cell("RELEASE", EnvRel, RelF),
                Cell("AMOUNT", EnvAmt, Bip, TealC)
            }
        };
        var envLane = new Grid()
        {
            Margin = new Thickness(9, 6),
            Children = { envLaneHeader, envLaneKnobs },
            RowSpacing = 6,
            RowDefinitions = new RowDefinitions("Auto,*"),
        };
        Grid.SetRow(envLane, 0);
        Grid.SetRow(envLaneKnobs, 1);
        
        // ================= LFO LANE HEADER =================
        var lfoLaneHeader = LaneHeader(
            LfoOn, 
            "LFO", 
            ToggleChip(LfoSync, v => v >= 0.5 ? "Sync" : "Free"),
            ToggleChip(LfoPhase, v => $"{v * 180:0}°", cycle: true));
        var lfoLaneWavePicker = WavePicker();
        var lfoLaneKnobs = new UniformGrid()
        {
            ColumnSpacing = 6,
            Rows = 1,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment =  VerticalAlignment.Center,
            Children =
            {
                Cell("RATE", LfoRate, RateF),
                Cell("AMT", LfoAmt, PctF, TealC),
                Cell("MORPH", LfoMorph, PctF, TealC),
            }
        };
        var lfoLane = new Grid()
        {
            Margin = new Thickness(9, 6),
            RowDefinitions = new RowDefinitions("Auto, Auto, *"),
            RowSpacing = 6,
            Children = { lfoLaneHeader, lfoLaneWavePicker, lfoLaneKnobs }
        };
        Grid.SetRow(lfoLaneHeader, 0);
        Grid.SetRow(lfoLaneWavePicker, 1);
        Grid.SetRow(lfoLaneKnobs, 2);

        var modCol = new Border { Background = RailBg, BorderBrush = RowLit, BorderThickness = new Thickness(1, 0, 1, 0), Child =
            new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), Children = {
                envLane,
                new Border { Height = 1, Background = RowLit, [Grid.RowProperty] = 1 },
                new Border { Child = lfoLane, [Grid.RowProperty] = 2 } } } };

        // ================= output rail + sidechain =================
        var driveCell = Cell("DRIVE", Drive, PctF);
        var dryWetCell = Cell("DRY/WET", DryWet, PctF, TealC);
        var outRailKnobs = new StackPanel()
        {
            Orientation =  Orientation.Horizontal,
            Children = { driveCell, dryWetCell },
            Spacing = 3,
            HorizontalAlignment =  HorizontalAlignment.Center,
            VerticalAlignment =   VerticalAlignment.Center,
        };
        var gainGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 5,
            Children =
            {
                Cap("GAIN"),
                WithCol(HSlider(Gain, "", GainF, 0, 30), 1)
            },
            Margin = new Thickness(0,0,0,6)
        };
        var sideChainGrid = BuildSidechain(engine, track, di, Cap, MutedC);
        
        var outRail = new Grid()
        {
            Margin =  new Thickness(7, 6),
            RowDefinitions =  new RowDefinitions("*,Auto,Auto"),
            Children =
            {
                outRailKnobs,
                gainGrid,
                sideChainGrid,
            }
        };
        Grid.SetRow(outRailKnobs, 0);
        Grid.SetRow(gainGrid, 1);
        Grid.SetRow(sideChainGrid, 2);

        // ================= assemble =================
        DockPanel.SetDock(outRail, Dock.Right);
        DockPanel.SetDock(modCol, Dock.Right);
        var body = new DockPanel
        { 
            LastChildFill = true,
            Children = { outRail, modCol, graph }
        };
        DockPanel.SetDock(live, Dock.Top);
        var root = new DockPanel
        {
            LastChildFill = true,
            Background = NotaPalette.BgApp,
            Children = { live, body }
        };

        void RefreshAll() { foreach (var a in readouts) a(); }
        SyncCurve();
        ctx.AddDeviceRefresher(() => { SyncCurve(); RefreshAll(); });
        RefreshAll();
        return root;
    }

    private static Control WithDock(Control c, Dock d) { DockPanel.SetDock(c, d); return c; }
    private static Control WithCol(Control c, int col) { Grid.SetColumn(c, col); return c; }

    // Compact sidechain block for the output rail: switch + source + gain slider.
    private Control BuildSidechain(IAudioEngine engine, int track, int di, Func<string, IBrush?, Control> Cap, IBrush muted)
    {
        var ids = new List<int> { -1 };
        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 9, Height = 22, Padding = new Thickness(5, 0) };
        combo.Items.Add("None");
        for (int i = 0; i < engine.TrackCount; i++)
        {
            if (!engine.TryGetTrackInfo(i, out var ti) || ti.Id == track) continue;
            string kind = ti.IsReturn ? "Return" : ti.IsInstrument ? "Inst" : "Audio";
            ids.Add(ti.Id); combo.Items.Add($"{i + 1} · {kind}");
        }
        int src0 = engine.DeviceSidechainSource(track, di);
        combo.SelectedIndex = Math.Max(0, ids.IndexOf(src0));
        var sw = new ToggleSwitch(src0 >= 0);

        // Sidechain gain slider (dB, ±24, bipolar).
        var gVal = new TextBlock { Text = $"{engine.DeviceSidechainGain(track, di):0.0}", FontSize = 9, Foreground = TxtC, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, Width = 28 };
        gVal.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        var gFill = new Border { Height = 3, Background = NotaPalette.BorderStrong, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var gTrack = new Border { Height = 3, Background = Inset, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center };
        var gHandle = new Border { Width = 8, Height = 9, Background = NotaPalette.TextSecondary, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var gSlot = new Panel { Height = 10, MinWidth = 34, Children = { gTrack, gFill, gHandle } };
        void GUpd() { double db = engine.DeviceSidechainGain(track, di); double v = Math.Clamp((db + 24) / 48.0, 0, 1); double W = gSlot.Bounds.Width; gHandle.Margin = new Thickness(Math.Clamp(v * W - 4, 0, Math.Max(0, W - 8)), 0, 0, 0); gFill.Width = v * W; gVal.Text = $"{db:0.0}"; }
        bool gd = false;
        void GSet(double x) { double v = Math.Clamp(x / Math.Max(1, gSlot.Bounds.Width), 0, 1); engine.SetDeviceSidechainGain(track, di, (float)(v * 48 - 24)); GUpd(); }
        gSlot.PointerPressed += (_, e) => { gd = true; e.Pointer.Capture(gSlot); GSet(e.GetPosition(gSlot).X); };
        gSlot.PointerMoved += (_, e) => { if (gd) GSet(e.GetPosition(gSlot).X); };
        gSlot.PointerReleased += (_, e) => { if (gd) { gd = false; e.Pointer.Capture(null); } };

        var body = new StackPanel { Spacing = 4, Opacity = sw.IsOn ? 1.0 : 0.45, Children = {
            combo,
            new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 4, Children = { Cap("GAIN", muted), WithCol(gSlot, 1), WithCol(gVal, 2) } } } };

        void Reflect(int src) { bool on = src >= 0; body.Opacity = on ? 1.0 : 0.45; if (sw.IsOn != on) sw.IsOn = on; }
        combo.SelectionChanged += (_, _) => { int sel = combo.SelectedIndex; if (sel < 0 || sel >= ids.Count) return; engine.SetDeviceSidechainSource(track, di, ids[sel]); Reflect(ids[sel]); };
        sw.Changed += on => { int src = on ? (ids.Count > 1 ? ids[1] : -1) : -1; engine.SetDeviceSidechainSource(track, di, src); combo.SelectedIndex = Math.Max(0, ids.IndexOf(src)); Reflect(src); };
        Avalonia.Threading.Dispatcher.UIThread.Post(GUpd);

        var hdr = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 0, 4), Children = { sw, Cap("SIDECHAIN", muted) } };
        return new StackPanel { Children = { new Border { Height = 1, Background = RowLit }, hdr, body } };
    }
}

// DockPanel dock helper that also adds the child to the panel (top-docked lane headers).
internal static class DockExt
{
    public static void SetDock2(this DockPanel dp, Control child, Dock dock) { DockPanel.SetDock(child, dock); dp.Children.Add(child); }
}
