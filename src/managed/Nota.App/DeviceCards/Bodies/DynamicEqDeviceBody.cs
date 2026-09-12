// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Dynamic EQ-8 (device kind 13) body. A two-curve
// response graph (static brass + momentary teal) fills the left; a LIVE strip drives
// the selected band — frequency & Q stay brass, threshold/range/attack/release are
// teal because they modulate the band's gain — with Sidechain and Solo beside them.
// A band table on the right lists all eight (DYN direction/range + a live GR bar) and
// a 2-second GR history sparkline sits under it. Every control is a generic device
// param → automation + persist for free.

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed class DynamicEqDeviceBody : IDeviceBody
{
    private const int Bands = 8, PerBand = 10;
    private const int On = 0, TypeF = 1, FreqF = 2, GainF = 3, QF = 4, ModeF = 5, ThrF = 6, RangeF = 7, AtkF = 8, RelF = 9;
    private const int OutputP = 80, SidechainP = 81, SoloP = 82;
    private const int LowShelf = 1, Bell = 2, HighShelf = 4;
    private static readonly string[] TypeAbbr = { "HP", "LS", "Bell", "Notch", "HS", "LP" };

    public double Width => 700;
    public bool FullBleed => true;   // manage our own padding so the tall band table fits the card

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId, di = index;
        float P(int p) => engine.DeviceGetParam(track, di, p);
        void SetP(int p, float v) => engine.DeviceSetParam(track, di, p, v);

        var curve = new DynamicEqCurve(engine, track, di) { HorizontalAlignment = HorizontalAlignment.Stretch };
        ctx.AddDeviceRefresher(curve.Tick);

        // ---------------- LIVE strip (selected band) ----------------
        int Cur() => curve.SelectedBand;
        bool HasGain(int b) => (int)Math.Round(P(b * PerBand + TypeF)) is LowShelf or Bell or HighShelf;
        bool IsDyn(int b) => P(b * PerBand + On) > 0.5f && HasGain(b) && (int)Math.Round(P(b * PerBand + ModeF)) != 0;

        // A gauge knob bound to <field> of whatever band is currently selected.
        Control DevKnob(string name, int field, Func<double, string> fmt, bool log, IBrush? arc)
        {
            double Min() => engine.DeviceParamMin(track, di, Cur() * PerBand + field);
            double Max() => engine.DeviceParamMax(track, di, Cur() * PerBand + field);
            double ToNorm(double raw) { double lo = Min(), hi = Max(); return log ? Math.Log(Math.Max(raw, lo) / lo) / Math.Log(hi / lo) : (raw - lo) / (hi - lo); }
            double ToRaw(double n) { double lo = Min(), hi = Max(); return log ? lo * Math.Pow(hi / lo, n) : lo + n * (hi - lo); }

            int pi = Cur() * PerBand + field;
            var value = new TextBlock { Text = fmt(P(pi)), FontSize = 9, Foreground = TextPrimary };
            value.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
            var knob = new Knob(ToNorm(P(pi)), 1.0) { Accent = true, ArcColor = arc, Width = 30, Height = 30 };
            knob.ValueChanged += v => { int p = Cur() * PerBand + field; float raw = (float)ToRaw(v); SetP(p, raw); value.Text = fmt(raw); };
            knob.GestureBegin += () => engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, Cur() * PerBand + field, "");
            knob.GestureEnd += () => engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, Cur() * PerBand + field, "");
            MidiLearn.Bind(knob, MidiTarget.DeviceParam(track, di, Cur() * PerBand + field), name);
            ctx.AddDeviceRefresher(() =>
            {
                if (knob.Dragging) return;
                int p = Cur() * PerBand + field; float raw = P(p);
                double nv = ToNorm(raw);
                if (Math.Abs(nv - knob.Value) > 1e-3) knob.Value = nv;
                value.Text = fmt(raw);
            });
            var cell = KnobCell(name, knob, value, 44);
            return cell;
        }

        static string HzF(double v) => v >= 1000 ? $"{v / 1000:0.0}k" : $"{v:0} Hz";
        static string QF2(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
        static string DbF(double v) => $"{v:+0.0;-0.0;0} dB";
        static string MsF(double v) => v >= 100 ? $"{v:0} ms" : $"{v:0.0} ms";

        var kFreq = DevKnob("FREQ", FreqF, HzF, true, Brass);
        var kQ = DevKnob("Q", QF, QF2, false, Brass);
        var kThr = DevKnob("THRESH", ThrF, v => $"{v:0} dB", false, Teal);
        var kRange = DevKnob("RANGE", RangeF, DbF, false, Teal);
        var kAtk = DevKnob("ATTACK", AtkF, MsF, true, Teal);
        var kRel = DevKnob("RELEASE", RelF, MsF, true, Teal);

        // Mode chips: Static / ↓ Above / ↑ Below
        var modeChips = new Border[3];
        string[] modeLbl = { "STAT", "DUCK", "LIFT" };
        void SyncMode()
        {
            int cur = (int)Math.Round(P(Cur() * PerBand + ModeF));
            bool canDyn = HasGain(Cur());
            for (int i = 0; i < 3; i++)
            {
                bool onc = i == cur;
                modeChips[i].Background = onc ? (i == 0 ? Brass : Teal) : Card2;
                modeChips[i].Opacity = (canDyn || i == 0) ? 1 : 0.4;
                ((TextBlock)modeChips[i].Child!).Foreground = onc ? OnAccent : TextSecondary;
            }
            double dynOp = IsDyn(Cur()) ? 1 : 0.5;
            kThr.Opacity = kRange.Opacity = kAtk.Opacity = kRel.Opacity = dynOp;
        }
        var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        for (int i = 0; i < 3; i++)
        {
            int vi = i;
            var chip = new Border
            {
                Background = Card2, BorderBrush = BorderStrong, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                Padding = new Thickness(7, 3), Cursor = new Cursor(StandardCursorType.Hand),
                Child = new TextBlock { Text = modeLbl[i], FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = TextSecondary },
            };
            chip.PointerPressed += (_, e) => { e.Handled = true; if (HasGain(Cur()) || vi == 0) {
                int bp = Cur() * PerBand;
                // Engaging a dynamic mode with a zero Range would move nothing (no GR ever) —
                // seed a musical default so the band actually reacts: DUCK −6 dB, LIFT +6 dB.
                if (vi != 0 && Math.Abs(P(bp + RangeF)) < 0.01f) SetP(bp + RangeF, vi == 1 ? -6f : 6f);
                SetP(bp + ModeF, vi); SyncMode(); curve.InvalidateVisual(); } };
            modeChips[i] = chip; modeRow.Children.Add(chip);
        }
        MidiLearn.Bind(modeRow, MidiTarget.DeviceParam(track, di, Cur() * PerBand + ModeF), engine.DeviceParamName(track, di, Cur() * PerBand + ModeF));

        // Sidechain + Solo toggles
        Border Toggle(string text, Func<bool> get, Action<bool> set)
        {
            var b = new Border { BorderBrush = BorderStrong, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 4), Cursor = new Cursor(StandardCursorType.Hand),
                Child = new TextBlock { Text = text, FontSize = 9, FontWeight = FontWeight.SemiBold } };
            void Sync() { bool on = get(); b.Background = on ? Teal : Card2; ((TextBlock)b.Child!).Foreground = on ? OnAccent : TextSecondary; }
            b.PointerPressed += (_, e) => { e.Handled = true; set(!get()); Sync(); };
            ctx.AddDeviceRefresher(Sync); Sync();
            return b;
        }
        var scToggle = Toggle("SC", () => P(SidechainP) > 0.5f, on => SetP(SidechainP, on ? 1 : 0));
        MidiLearn.Bind(scToggle, MidiTarget.DeviceParam(track, di, SidechainP), engine.DeviceParamName(track, di, SidechainP));
        var soloToggle = Toggle("SOLO", () => (int)Math.Round(P(SoloP)) == Cur() + 1, on => SetP(SoloP, on ? Cur() + 1 : 0));

        // Compact sidechain source picker (None + every other track).
        var scIds = new List<int> { -1 };
        var scCombo = new ComboBox { FontSize = 9, MinWidth = 92, MaxWidth = 92, VerticalAlignment = VerticalAlignment.Center };
        scCombo.Items.Add("Key: none");
        for (int i = 0; i < engine.TrackCount; i++)
        {
            if (!engine.TryGetTrackInfo(i, out var ti) || ti.Id == track) continue;
            scIds.Add(ti.Id); scCombo.Items.Add($"Key: {i + 1}");
        }
        scCombo.SelectedIndex = Math.Max(0, scIds.IndexOf(engine.DeviceSidechainSource(track, di)));
        scCombo.SelectionChanged += (_, _) => { int s = scCombo.SelectedIndex; if (s >= 0 && s < scIds.Count) { engine.SetDeviceSidechainSource(track, di, scIds[s]); ctx.NotifyChanged(); } };

        var bandTitle = new TextBlock { FontSize = 10, FontWeight = FontWeight.Bold, Foreground = AccentBright, VerticalAlignment = VerticalAlignment.Center, MinWidth = 96 };
        void SyncTitle() { int b = Cur(); int ty = (int)Math.Round(P(b * PerBand + TypeF)); bandTitle.Text = $"BAND {b + 1} · {TypeAbbr[Math.Clamp(ty, 0, 5)]}"; }
        ctx.AddDeviceRefresher(SyncTitle); ctx.AddDeviceRefresher(SyncMode);
        curve.SelectionChanged += () => { SyncTitle(); SyncMode(); };

        Border VSep() => new() { Width = 1, Background = BorderDef, Margin = new Thickness(3, 4) };
        var liveStrip = new Border
        {
            Height = 60, Background = Sunken, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
            Padding = new Thickness(9, 3), Margin = new Thickness(0, 0, 0, 4),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children =
            {
                new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Width = 138, Children = { bandTitle, modeRow } },
                VSep(),
                kFreq, kQ,
                VSep(),
                kThr, kRange, kAtk, kRel,
                VSep(),
                new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = { new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { scToggle, soloToggle } }, scCombo } },
            } },
        };

        // ---------------- band table ----------------
        var table = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto"), ColumnDefinitions = new ColumnDefinitions("16,30,44,44,*") };
        var rowBorders = new Border[Bands];
        for (int b = 0; b < Bands; b++)
        {
            int bb = b;
            var num = new TextBlock { Text = (b + 1).ToString(), FontSize = 9, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center };
            var type = new TextBlock { FontSize = 9 };
            var hz = new TextBlock { FontSize = 9 }; hz.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
            var dyn = new TextBlock { FontSize = 9 }; dyn.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
            var grBar = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = NotaPalette.BorderDefault, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var grFill = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = Teal, HorizontalAlignment = HorizontalAlignment.Left };
            var grWrap = new Grid { Margin = new Thickness(0, 0, 4, 0) }; grWrap.Children.Add(grBar); grWrap.Children.Add(grFill);

            void SyncRow()
            {
                bool on = P(bb * PerBand + On) > 0.5f;
                int ty = Math.Clamp((int)Math.Round(P(bb * PerBand + TypeF)), 0, 5);
                bool dynB = IsDyn(bb);
                var tc = on ? (bb == Cur() ? AccentBright : TextSecondary) : TextDisabled;
                num.Foreground = on ? (dynB ? Teal : Brass) : TextDisabled;
                type.Text = TypeAbbr[ty]; type.Foreground = tc;
                hz.Text = HzF(P(bb * PerBand + FreqF)); hz.Foreground = tc;
                if (dynB) { int m = (int)Math.Round(P(bb * PerBand + ModeF)); dyn.Text = $"{(m == 1 ? "↓" : "↑")}{P(bb * PerBand + RangeF):+0;-0;0}"; dyn.Foreground = Teal; }
                else if (HasGain(bb)) { dyn.Text = "—"; dyn.Foreground = TextTertiary; }
                else { dyn.Text = ""; }
                double gr = Math.Min(1, Math.Abs(curve.Gr(bb)) / 12);
                grFill.Width = Math.Max(0, gr * 60);
                grBar.Width = 60;
                rowBorders[bb].Background = bb == Cur() ? NotaPalette.Wash(NotaPalette.Accent, 0x20) : Brushes.Transparent;
            }
            ctx.AddDeviceRefresher(SyncRow);

            Grid.SetColumn(num, 0); Grid.SetColumn(type, 1); Grid.SetColumn(hz, 2); Grid.SetColumn(dyn, 3); Grid.SetColumn(grWrap, 4);
            var rowGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("16,30,44,44,*"), Height = 11 };
            rowGrid.Children.Add(num); rowGrid.Children.Add(type); rowGrid.Children.Add(hz); rowGrid.Children.Add(dyn); rowGrid.Children.Add(grWrap);
            var rb = new Border { Child = rowGrid, CornerRadius = new CornerRadius(3), Cursor = new Cursor(StandardCursorType.Hand), Padding = new Thickness(2, 0) };
            rb.PointerPressed += (_, e) => { e.Handled = true; curve.Select(bb); };
            rowBorders[b] = rb;
            Grid.SetRow(rb, b); table.Children.Add(rb);
        }

        // ---------------- GR history sparkline ----------------
        var hist = new GrHistoryView(curve) { Height = 22 };
        ctx.AddDeviceRefresher(hist.Tick);

        var rightCol = new StackPanel { Spacing = 3, Width = 236, Children =
        {
            new TextBlock { Text = "BANDS", FontSize = 8, FontWeight = FontWeight.Bold, Foreground = TextTertiary },
            table,
            new TextBlock { Text = "GR HISTORY · 2 s", FontSize = 8, FontWeight = FontWeight.Bold, Foreground = TextTertiary, Margin = new Thickness(0, 1, 0, 0) },
            hist,
        } };

        var graphPanel = new Border { Background = Card2, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(6), Child = curve, HorizontalAlignment = HorizontalAlignment.Stretch };
        var tablePanel = new Border { Background = Card2, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(8, 6), Child = rightCol };

        var mainRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6, Height = 154 };
        Grid.SetColumn(graphPanel, 0); Grid.SetColumn(tablePanel, 1);
        mainRow.Children.Add(graphPanel); mainRow.Children.Add(tablePanel);

        return new Border { Padding = new Thickness(8, 6), Child = new StackPanel { Children = { liveStrip, mainRow } } };
    }
}

// 2-second per-band gain-reduction history, newest at the right. Duck (negative GR)
// draws downward in teal; lift (positive) upward in brass — the same colour logic as
// the graph, so the history reads at a glance whether the release is too fast.
internal sealed class GrHistoryView : Control
{
    private readonly DynamicEqCurve _curve;
    private readonly float[] _buf = new float[DynamicEqCurve.HistLen];
    private static readonly IBrush Bg = NotaPalette.BgSunken;
    private static readonly IPen Mid = new Pen(NotaPalette.BorderStrong, 1);
    private static readonly IBrush DuckFill = NotaPalette.Wash(NotaPalette.Teal, 0x40);
    private static readonly IPen DuckPen = new Pen(NotaPalette.Teal, 1.2);
    private static readonly IPen LiftPen = new Pen(NotaPalette.Accent, 1.2);

    public GrHistoryView(DynamicEqCurve curve) { _curve = curve; MinHeight = 28; }
    public void Tick() => InvalidateVisual();

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        ctx.FillRectangle(Bg, new Rect(0, 0, w, h), 4);
        double mid = h / 2;
        ctx.DrawLine(Mid, new Point(0, mid), new Point(w, mid));
        _curve.FillHistory(_curve.SelectedBand, _buf);
        const double range = 12;   // ±12 dB full scale
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(new Point(0, mid), true);
            for (int i = 0; i < _buf.Length; i++)
            {
                double x = w * i / (_buf.Length - 1);
                double y = mid - Math.Clamp(_buf[i] / range, -1, 1) * (mid - 2);
                g.LineTo(new Point(x, y));
            }
            g.LineTo(new Point(w, mid));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(DuckFill, null, geo);
        // outline
        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            for (int i = 0; i < _buf.Length; i++)
            {
                double x = w * i / (_buf.Length - 1);
                double y = mid - Math.Clamp(_buf[i] / range, -1, 1) * (mid - 2);
                if (i == 0) g.BeginFigure(new Point(x, y), false); else g.LineTo(new Point(x, y));
            }
        }
        ctx.DrawGeometry(null, _curve.Gr(_curve.SelectedBand) > 0 ? LiftPen : DuckPen, line);
    }
}
