// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota EQ-8 body (device kind 0), a build of the "Nota EQ-8" mockup
// (700 × 260), laid out like Dynamic EQ-8: a row of eight band chips over the response graph
// (nodes drag right on the curve; the ANALYZER Pre / Post / Off switch sits in its corner), a
// panel on the right edits the selected band (type, FREQ / GAIN / Q — RESO on the cuts —, the
// cut's SLOPE, its CHANNEL: stereo, mid, side, left or right) and the device's GLOBAL controls
// (Auto gain, SCALE, OUTPUT), and a status strip carries the summary, the output with the auto
// gain in effect, and the engine. The live readings come from the engine (Eq.h scopeRead).
// Every control is a device param (raw units), so automation / MIDI learn / presets / A-B /
// persistence come for free. FullBleed — the shared shell draws the header (name · preset ·
// badge · bypass).

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

internal sealed class EqDeviceBody : IDeviceBody
{
    public double Width => 700;   // the almanac device format: 700 × 260
    public bool FullBleed => true;
    public string? Subtitle => "EQUALIZER";   // the processing type, shown as the header badge

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId, di = index;
        int pc = Math.Min(engine.DeviceParamCount(track, di), Eq8.ParamCount);
        var mn = new float[Eq8.ParamCount];
        var mx = new float[Eq8.ParamCount];
        for (int p = 0; p < Eq8.ParamCount; p++)
        {
            mn[p] = p < pc ? engine.DeviceParamMin(track, di, p) : 0;
            mx[p] = p < pc ? engine.DeviceParamMax(track, di, p) : 1;
            if (mx[p] <= mn[p]) mx[p] = mn[p] + 1;
        }
        // Defaults of the appended params, for a device that somehow lacks them.
        float P(int p) => p < pc ? engine.DeviceGetParam(track, di, p) : p == Eq8.ScaleP ? 100f : p == Eq8.AnalyzerP ? Eq8.AnaPost : 0f;
        float B(int b, int f) => P(Eq8.P(b, f));
        void Begin(int p) => engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void End(int p) => engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void Raw(int p, float v) { if (p < pc) engine.DeviceSetParam(track, di, p, Math.Clamp(v, mn[p], mx[p])); }
        // A discrete edit (a click) is one automation gesture, so it records while the transport does.
        void SetP(int p, float v) { Begin(p); Raw(p, v); End(p); }
        void Learn(Control c, int p) { if (p < pc) MidiLearn.Bind(c, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p)); }

        var readouts = new List<Action>();
        var bandReadouts = new List<Action>();
        var scope = new float[Eq8.kScope];
        int scN = 0;
        double Sc(int i) => scN > i ? scope[i] : 0;

        var curve = new Eq8Curve(engine, track, di);
        void RefreshAll()
        {
            for (int i = 0; i < readouts.Count; i++) readouts[i]();
            for (int i = 0; i < bandReadouts.Count; i++) bandReadouts[i]();
            curve.InvalidateVisual();
        }
        int Sel() => curve.SelectedBand;
        bool BandOn(int b) => B(b, Eq8.On) > 0.5f;
        int TypeOf(int b) => Math.Clamp((int)Math.Round(B(b, Eq8.TypeF)), 0, 5);
        int SlopeOf(int b) => Math.Clamp((int)Math.Round(P(Eq8.SlopeBase + b)), 0, 2);
        int ChanOf(int b) => Math.Clamp((int)Math.Round(P(Eq8.ChannelBase + b)), 0, 4);
        bool Capable(int b) => Eq8.HasGain(TypeOf(b));
        bool Cut(int b) => Eq8.IsCut(TypeOf(b));
        bool AutoOn() => P(Eq8.AutoGainP) >= 0.5f;

        // ---- small builders ---------------------------------------------------------
        static TextBlock Caps(string t, IBrush? c = null, double fs = 7) => new()
        { Text = t, FontSize = fs, FontWeight = FontWeight.Bold, Foreground = c ?? TextTertiary, LetterSpacing = 0.8, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock Mono(string t, double fs, IBrush c)
        { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.Mono"); return tb; }
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }

        // ======================================================================
        // LEFT — band chips over the response graph
        // ======================================================================
        var chipRow = new UniformGrid { Rows = 1, Margin = new Thickness(3, 0), VerticalAlignment = VerticalAlignment.Center };
        for (int b = 0; b < Eq8.Bands; b++)
        {
            int bb = b;
            var num = Mono((b + 1).ToString(), 8, Brass); num.FontWeight = FontWeight.Bold;
            var type = new TextBlock { FontSize = 7, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0) };
            var chn = new TextBlock { FontSize = 7, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0) };
            var hz = Mono("", 7, TextPrimary); hz.HorizontalAlignment = HorizontalAlignment.Right;
            var inner = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*"), Margin = new Thickness(4, 0) };
            inner.Children.Add(num); inner.Children.Add(Col(type, 1)); inner.Children.Add(Col(chn, 2)); inner.Children.Add(Col(hz, 3));
            var chip = new Border
            {
                Height = 15, Margin = new Thickness(1, 0), CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), ClipToBounds = true,
                Cursor = new Cursor(StandardCursorType.Hand), Child = inner,
            };
            ToolTip.SetTip(chip, NotaNum.F($"Band {b + 1} — click to edit it, double-click to switch it on / off"));
            chip.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(chip).Properties.IsLeftButtonPressed) return;
                if (e.ClickCount == 2) SetP(Eq8.P(bb, Eq8.On), BandOn(bb) ? 0 : 1);
                curve.SelectedBand = bb; RefreshAll(); e.Handled = true;
            };
            Learn(chip, Eq8.P(bb, Eq8.On));
            readouts.Add(() =>
            {
                bool on = BandOn(bb), sel = bb == Sel();
                int c = ChanOf(bb);
                num.Foreground = !on ? TextDisabled : sel ? AccentBright : Brass;
                type.Text = Eq8.TypeShort[TypeOf(bb)];
                type.Foreground = on ? TextSecondary : TextDisabled;
                chn.Text = c == Eq8.St ? "" : Eq8.ChannelShort[c];
                chn.IsVisible = c != Eq8.St;
                chn.Foreground = on ? Eq8.ChannelInk(c, bright: true) ?? TextSecondary : TextDisabled;
                hz.Text = Eq8.HzShort(B(bb, Eq8.FreqF));
                hz.Foreground = on ? TextPrimary : TextDisabled;
                chip.BorderBrush = sel ? Brass : Brushes.Transparent;
                chip.Background = sel ? Raised : Sunken;
            });
            chipRow.Children.Add(chip);
        }
        var chipStrip = new Border { Height = 20, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Child = chipRow };
        DockPanel.SetDock(chipStrip, Dock.Top);
        ToolTip.SetTip(curve, "Drag a node — frequency and gain · wheel over a node — Q · double-click a node — band on / off · "
            + "double-click empty space — a new bell there · right-click a node — type, slope, channel");

        // ANALYZER Pre / Post / Off over the graph's top-left corner.
        var anaSeg = Segments(Eq8.AnalyzerSeg, () => Math.Clamp((int)Math.Round(P(Eq8.AnalyzerP)), 0, 2),
            i => { SetP(Eq8.AnalyzerP, i); RefreshAll(); }, out var anaSync, padX: 4);
        Learn(anaSeg, Eq8.AnalyzerP);
        ToolTip.SetTip(anaSeg, "Analyzer — Pre: the input spectrum · Post: the output over a dimmer input · Off");
        readouts.Add(anaSync);
        var anaBar = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 5, Margin = new Thickness(5, 3), HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top, Children = { Caps("ANALYZER"), anaSeg },
        };
        anaBar.RenderTransform = new ScaleTransform(0.85, 0.85);
        anaBar.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
        var graphPanel = new Border
        {
            Background = Card2, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile,
            ClipToBounds = true, Margin = new Thickness(0, 0, 5, 0),
            Child = new DockPanel { LastChildFill = true, Children = { chipStrip, new Border { Padding = new Thickness(5), Child = new Panel { Children = { curve, anaBar } } } } },
        };

        // ======================================================================
        // RIGHT — the selected band + the device's global controls (rebuilt when the selection
        // moves, so MIDI learn and automation bind to that band's own params)
        // ======================================================================
        var bandHost = new ContentControl();

        Control BandPanel(int b)
        {
            bandReadouts.Clear();
            int pOn = Eq8.P(b, Eq8.On), pType = Eq8.P(b, Eq8.TypeF), pF = Eq8.P(b, Eq8.FreqF), pG = Eq8.P(b, Eq8.GainF),
                pQ = Eq8.P(b, Eq8.QF), pSlope = Eq8.SlopeBase + b, pChan = Eq8.ChannelBase + b;

            // 0..1 mappings (frequency and Q are log)
            bool IsLog(int p) => p == pF || p == pQ;
            double ToN(int p, double v) => IsLog(p)
                ? Math.Log(Math.Clamp(v, mn[p], mx[p]) / mn[p]) / Math.Log(mx[p] / mn[p])
                : (Math.Clamp(v, mn[p], mx[p]) - mn[p]) / (mx[p] - mn[p]);
            double FromN(int p, double n) { n = Math.Clamp(n, 0, 1); return IsLog(p) ? mn[p] * Math.Pow(mx[p] / mn[p], n) : mn[p] + n * (mx[p] - mn[p]); }
            double DefOf(int p) => p == pF ? 1000 : p == pG ? 0 : p == pQ ? 0.71 : engine.DeviceParamDefault(track, di, p);

            // ---- header: badge · name · ON ----
            var badgeTxt = Mono((b + 1).ToString(), 8, OnAccent); badgeTxt.FontWeight = FontWeight.Bold; badgeTxt.HorizontalAlignment = HorizontalAlignment.Center;
            var badge = new Border { Width = 13, Height = 13, CornerRadius = NotaRadius.Pill, VerticalAlignment = VerticalAlignment.Center, Child = badgeTxt };
            var name = new TextBlock { FontSize = 9, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            var onSw = Switch("On", () => BandOn(b), () => { SetP(pOn, BandOn(b) ? 0 : 1); RefreshAll(); }, out var onSync);
            Learn(onSw, pOn);
            ToolTip.SetTip(onSw, "Band on — off leaves the band out of the signal (double-click its node does the same)");
            bandReadouts.Add(onSync);
            bandReadouts.Add(() =>
            {
                bool on = BandOn(b);
                badge.Background = on ? AccentBright : NotaPalette.TextAxis;
                badgeTxt.Foreground = on ? OnAccent : TextTertiary;
                name.Text = Eq8.TypeNames[TypeOf(b)].ToUpperInvariant();
                name.Foreground = on ? AccentBright : TextTertiary;
            });
            var head = new DockPanel { Height = 20, Margin = new Thickness(8, 0), Children = { Docked(onSw, Dock.Right), badge, name } };
            var headBorder = new Border { BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Child = head };

            // ---- type ----
            var typeSeg = Segments(Eq8.TypeSeg, () => TypeOf(b), i => { SetP(pType, i); RefreshAll(); }, out var typeSync, fill: true, padX: 0);
            Learn(typeSeg, pType);
            ToolTip.SetTip(typeSeg, "Type — high-pass, low shelf, bell, notch, high shelf, low-pass; only shelves and bells take gain");
            bandReadouts.Add(typeSync);

            // ---- knobs ----
            Control K(int p, string label, Func<string> fmt, string tip, Func<string>? liveLabel = null, Func<bool>? inactive = null)
            {
                var val = Mono(fmt(), 7, TextPrimary);
                var knob = new Knob(ToN(p, P(p)), 1.0) { Accent = true, Default = ToN(p, DefOf(p)), Width = 32, Height = 32 };
                knob.ValueChanged += v => { Raw(p, (float)FromN(p, v)); val.Text = fmt(); RefreshAll(); };
                knob.GestureBegin += () => Begin(p);
                knob.GestureEnd += () => End(p);
                Learn(knob, p);
                ToolTip.SetTip(knob, tip);
                var cell = KnobCell(label, knob, val, 54);
                TextBlock? lbl = null;
                if (cell is Panel pl) foreach (var c in pl.Children) if (c is TextBlock tb && tb != val) { lbl = tb; break; }
                bool? wasInactive = null;
                bandReadouts.Add(() =>
                {
                    if (!knob.Dragging) { double c = ToN(p, P(p)); if (Math.Abs(c - knob.Value) > 1e-4) knob.Value = c; }
                    val.Text = fmt();
                    if (lbl is not null && liveLabel is not null) lbl.Text = liveLabel();
                    bool ina = inactive?.Invoke() ?? false;
                    if (wasInactive != ina) { Inactive.Set(cell, ina); wasInactive = ina; }
                });
                return cell;
            }
            var knobs = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), Children =
            {
                K(pF, "FREQ", () => Eq8.HzShort(B(b, Eq8.FreqF)) + (B(b, Eq8.FreqF) < 1000 ? " Hz" : ""), "Frequency — or drag the node left / right"),
                Col(K(pG, "GAIN", () => Capable(b) ? Eq8.Db(B(b, Eq8.GainF)) + " dB" : "—", "Gain — or drag the node up / down (Scale multiplies it)",
                    inactive: () => !Capable(b)), 1),
                Col(K(pQ, "Q", () => NotaNum.F($"{B(b, Eq8.QF):0.00}"), "Q — the width (the resonance on the cuts); or the wheel over the node",
                    liveLabel: () => Cut(b) ? "RESO" : "Q"), 2),
            } };

            // ---- slope + channel ----
            Control SegRow(string label, Control seg, Func<bool>? dim = null, bool rule = false)
            {
                var lbl = new Border { Width = 40, Child = Caps(label) };
                var row = new DockPanel { Children = { Docked(lbl, Dock.Left), seg } };
                if (dim is not null)
                {
                    bool? was = null;
                    bandReadouts.Add(() => { bool d = dim(); if (was != d) { row.Opacity = d ? 0.4 : 1; was = d; } });
                }
                return rule
                    ? new Border { BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 5, 0, 0), Child = row }
                    : row;
            }
            var slopeSeg = Segments(Eq8.SlopeSeg, () => SlopeOf(b), i => { if (Cut(b)) { SetP(pSlope, i); RefreshAll(); } },
                out var slopeSync, fill: true, padX: 0, dim: () => !Cut(b));
            Learn(slopeSeg, pSlope);
            ToolTip.SetTip(slopeSeg, "Slope — how steep the high-pass / low-pass falls: 12, 24 or 48\u2009dB per octave");
            bandReadouts.Add(slopeSync);
            var chanSeg = Segments(Eq8.ChannelSeg, () => ChanOf(b), i => { SetP(pChan, i); RefreshAll(); }, out var chanSync, fill: true, padX: 0);
            Learn(chanSeg, pChan);
            ToolTip.SetTip(chanSeg, "Channel — St: both channels · Mid: the centre · Side: the edges · L / R: one channel alone");
            bandReadouts.Add(chanSync);

            // ---- global: Auto gain, Scale, Output ----
            var autoSw = Switch("Auto gain", AutoOn, () => { SetP(Eq8.AutoGainP, AutoOn() ? 0 : 1); RefreshAll(); }, out var autoSync);
            Learn(autoSw, Eq8.AutoGainP);
            ToolTip.SetTip(autoSw, "Auto gain — adds the opposite of the shelves' and bells' average level, so edits are judged at the same loudness");
            bandReadouts.Add(autoSync);
            var globalRow = new Border
            {
                BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 5, 0, 0),
                Child = new DockPanel { Children = { Docked(autoSw, Dock.Right), Caps("GLOBAL") } },
            };
            Control SliderRow(string label, int p, Func<string> fmt, string tip, double def)
            {
                var trk = new SliderTrack { Bipolar = true, Ink = Teal, Reset = () => { SetP(p, (float)def); RefreshAll(); } };
                trk.Changed += v => { Raw(p, (float)(mn[p] + v * (mx[p] - mn[p]))); RefreshAll(); };
                trk.GestureBegin += () => Begin(p);
                trk.GestureEnd += () => End(p);
                Learn(trk, p);
                ToolTip.SetTip(trk, tip);
                var val = Mono(fmt(), 8, TextPrimary); val.TextAlignment = TextAlignment.Right; val.HorizontalAlignment = HorizontalAlignment.Right;
                bandReadouts.Add(() =>
                {
                    if (!trk.Dragging) trk.Norm = (P(p) - mn[p]) / (mx[p] - mn[p]);
                    val.Text = fmt();
                    val.Foreground = Math.Abs(P(p) - def) > 0.05 ? AccentBright : TextPrimary;
                });
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("40,*,40"), ColumnSpacing = 6, Height = 11 };
                g.Children.Add(Caps(label));
                g.Children.Add(Col(trk, 1));
                g.Children.Add(Col(val, 2));
                return g;
            }
            var sliders = new StackPanel
            {
                Spacing = 5, Children =
                {
                    SliderRow("SCALE", Eq8.ScaleP, () => NotaNum.F($"{P(Eq8.ScaleP):0}\u2009%"),
                        "Scale — multiplies every shelf and bell gain: 100\u2009% as set, 0\u2009% flat, 200\u2009% twice as much", 100),
                    SliderRow("OUTPUT", Eq8.OutputP, () => Eq8.Db(P(Eq8.OutputP)) + " dB", "Output — the level after the EQ, ±12\u2009dB", 0),
                },
            };

            var body = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*,Auto,*,Auto,4,Auto,*,Auto,5,Auto"), Margin = new Thickness(8, 6, 8, 6),
            };
            void Row(Control c, int r) { Grid.SetRow(c, r); body.Children.Add(c); }
            Row(typeSeg, 0); Row(knobs, 2);
            Row(SegRow("SLOPE", slopeSeg, () => !Cut(b), rule: true), 4);
            Row(SegRow("CHANNEL", chanSeg), 6);
            Row(globalRow, 8); Row(sliders, 10);
            var panel = new DockPanel { LastChildFill = true, Children = { Docked(headBorder, Dock.Top), body } };
            for (int i = 0; i < bandReadouts.Count; i++) bandReadouts[i]();
            return panel;
        }

        var right = new Border
        {
            Width = 186, Background = Card2, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile,
            ClipToBounds = true, Child = bandHost,
        };
        DockPanel.SetDock(right, Dock.Right);
        bandHost.Content = BandPanel(Sel());
        curve.SelectionChanged += () => { bandHost.Content = BandPanel(Sel()); RefreshAll(); };
        curve.Edited += RefreshAll;

        // ======================================================================
        // Status strip: summary · output · engine
        // ======================================================================
        var statusLeft = new TextBlock { FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        string StatusText()
        {
            int s = Sel(), nOn = 0, nMs = 0;
            for (int b = 0; b < Eq8.Bands; b++)
            {
                if (!BandOn(b)) continue;
                nOn++;
                if (ChanOf(b) is Eq8.Mid or Eq8.Side) nMs++;
            }
            var parts = new List<string> { NotaNum.F($"{nOn} bands") };
            if (nMs > 0) parts.Add(NotaNum.F($"M/S {nMs}"));
            parts.Add(NotaNum.F($"scale {P(Eq8.ScaleP):0}\u2009%"));
            string band = NotaNum.F($"B{s + 1} {Eq8.TypeNames[TypeOf(s)]} {Eq8.Hz(B(s, Eq8.FreqF))}");
            if (Capable(s)) band += " " + Eq8.Db(B(s, Eq8.GainF)) + " dB";
            if (Cut(s)) band += NotaNum.F($" · {Eq8.SlopeDb(SlopeOf(s))}\u2009dB/oct");
            if (ChanOf(s) != Eq8.St) band += " · " + Eq8.ChannelNames[ChanOf(s)].ToLowerInvariant();
            parts.Add(band);
            return string.Join(" · ", parts);
        }

        // OUT: a mono readout that drags vertically (±12 dB), double-click resets to 0; it shows the
        // auto gain on top when that is on.
        var outTb = Mono("", 8, TextSecondary);
        var outBox = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.SizeNorthSouth), VerticalAlignment = VerticalAlignment.Center, Child = outTb };
        bool outDrag = false; double outY = 0;
        outBox.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(outBox).Properties.IsLeftButtonPressed) return;
            if (e.ClickCount == 2) { SetP(Eq8.OutputP, 0); RefreshAll(); e.Handled = true; return; }
            outDrag = true; outY = e.GetPosition(outBox).Y; e.Pointer.Capture(outBox); Begin(Eq8.OutputP); e.Handled = true;
        };
        outBox.PointerMoved += (_, e) =>
        {
            if (!outDrag) return;
            double y = e.GetPosition(outBox).Y, dy = outY - y; outY = y;
            bool fine = (e.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Meta)) != 0;
            Raw(Eq8.OutputP, P(Eq8.OutputP) + (float)(dy * 24.0 / (fine ? 1400.0 : 140.0)));
            RefreshAll();
        };
        void OutEnd() { if (!outDrag) return; outDrag = false; End(Eq8.OutputP); }
        outBox.PointerReleased += (_, e) => { OutEnd(); e.Pointer.Capture(null); };
        outBox.PointerCaptureLost += (_, _) => OutEnd();
        Learn(outBox, Eq8.OutputP);
        ToolTip.SetTip(outBox, "Output (plus the auto gain in effect) — drag up / down (Shift for fine), double-click for 0\u2009dB");
        readouts.Add(() =>
        {
            float o = P(Eq8.OutputP);
            double total = o + (AutoOn() ? Sc(Eq8.S_AutoGain) : 0);
            outTb.Text = "OUT " + Eq8.Db(total) + " dB" + (AutoOn() ? " auto" : "");
            outTb.Foreground = Math.Abs(total) > 0.05 ? AccentBright : TextSecondary;
        });
        var engineRead = Mono("", 8, TextTertiary);
        readouts.Add(() =>
        {
            statusLeft.Text = StatusText();
            double sr = Sc(Eq8.S_SampleRate);
            engineRead.Text = sr > 0 ? NotaNum.F($"{sr / 1000:0.#}\u2009kHz · latency 0 smp · CPU {Sc(Eq8.S_Cpu) * 100:0.0}\u2009%") : "";
        });
        var statusRight = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Children = { outBox, engineRead } };
        var statusGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        statusGrid.Children.Add(statusLeft);
        statusGrid.Children.Add(Col(statusRight, 1));
        var status = new Border { Height = 18, Background = Sunken, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(8, 0), Child = statusGrid };
        DockPanel.SetDock(status, Dock.Bottom);

        // ---- assemble ---------------------------------------------------------------
        var bodyRow = new DockPanel { LastChildFill = true, Margin = new Thickness(5), Children = { right, graphPanel } };
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.SurfaceInset, Children = { status, bodyRow } };

        void Refresh()
        {
            scN = engine.DeviceScope(track, di, scope, Eq8.kScope);
            curve.Update(scope, scN);
            for (int i = 0; i < readouts.Count; i++) readouts[i]();
            for (int i = 0; i < bandReadouts.Count; i++) bandReadouts[i]();
        }
        ctx.AddDeviceRefresher(Refresh);
        Refresh();
        return root;
    }
}
