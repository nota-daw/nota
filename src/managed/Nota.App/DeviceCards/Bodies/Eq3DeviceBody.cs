// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota EQ-3 body (three-band isolator, device kind 16), a build of
// the "Nota EQ-3" mockup (700 × 260) on the Crush / Dynamic EQ-8 frame: three band strips on the
// left (LOW / MID / HIGH, each its span, a fader −24 … +6 dB — ±15 dB in the Classic range — its
// value and a KILL), and the response window on the right — the crossover readouts, Output, Range
// and Slope over it; inside, each band's curve in its hue, the brass sum, the output spectrum
// behind, and the two crossovers as handles along the top that drag left / right (a drag
// elsewhere rides the band under the pointer). A status strip closes the card. Every control is
// a device param (normalized 0..1), so automation / MIDI learn / presets / A-B / persistence come
// for free. FullBleed — the shared shell draws the header (name · preset · badge · bypass).

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

internal sealed class Eq3DeviceBody : IDeviceBody
{
    // ── Parameter indices (must match Eq3.h) ─────────────────────────────────
    private const int Low = 0, Mid = 1, High = 2, LowKill = 3, MidKill = 4, HighKill = 5,
                      FreqLo = 6, FreqHi = 7, Slope = 8, Gain = 9, Range = 10;
    // Scope layout (Eq3::S_* / kTele / kSpec).
    private const int S_SampleRate = 5, S_Cpu = 6, S_Analysed = 7, S_Latency = 8,
        kTele = 16, kSpec = 96, kScope = kTele + kSpec;

    public double Width => 700;
    public bool FullBleed => true;
    public string? Subtitle => "EQ";

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId, di = index;
        int pc = engine.DeviceParamCount(track, di);
        float P(int p) => p < pc ? engine.DeviceGetParam(track, di, p) : 0f;
        void Begin(int p) => engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void End(int p) => engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void Raw(int p, double v) { if (p < pc) engine.DeviceSetParam(track, di, p, (float)Math.Clamp(v, 0, 1)); }
        // A discrete edit (a click) is one automation gesture, so it records while the transport does.
        void SetP(int p, double v) { Begin(p); Raw(p, v); End(p); }
        void Learn(Control c, int p) => MidiLearn.Bind(c, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));

        bool Iso() => pc <= Range || P(Range) >= 0.5f;
        bool Killed(int b) => P(LowKill + b) >= 0.5f;
        double BandDb(int b) => Eq3Math.GainDb(P(Low + b), Iso());
        double F1() => Eq3Math.LoHz(P(FreqLo));
        double F2() => Eq3Math.HiHz(P(FreqHi));
        bool Lr8() => P(Slope) >= 0.5f;
        static double OutDb(double v) => (v - 0.5) * 48;

        var readouts = new List<Action>();
        void RefreshAll() { for (int i = 0; i < readouts.Count; i++) readouts[i](); }
        var scope = new float[kScope];
        int scN = 0;
        double Sc(int i) => scN > i ? scope[i] : 0;

        // ---- small builders ---------------------------------------------------------
        static TextBlock Caps(string t, IBrush? c = null, double fs = 7) => new()
        { Text = t, FontSize = fs, FontWeight = FontWeight.Bold, Foreground = c ?? TextTertiary, LetterSpacing = 0.8, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock Mono(string t, double fs, IBrush c)
        { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.Mono"); return tb; }
        static StackPanel Row(double sp, params Control[] cs)
        { var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = sp, VerticalAlignment = VerticalAlignment.Center }; foreach (var c in cs) s.Children.Add(c); return s; }
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }

        // Band edits in dB (the Range law decides the param value).
        void GainRaw(int b, double db) => Raw(Low + b, Eq3Math.GainNorm(db, Iso()));
        void ResetBand(int b)
        {
            Begin(Low + b); GainRaw(b, 0); End(Low + b);
            if (Killed(b)) SetP(LowKill + b, 0);
            RefreshAll();
        }

        // ======================================================================
        // Band strips
        // ======================================================================
        string Span(int b) => b switch
        {
            0 => "20 – " + Eq3Math.HzShort(F1()),
            1 => Eq3Math.HzShort(F1()) + " – " + Eq3Math.HzShort(F2()),
            _ => Eq3Math.HzShort(F2()) + " – 20k",
        };

        Control KillButton(int b)
        {
            var hue = Eq3Math.Hue(b);
            var tb = new TextBlock { Text = "KILL", FontSize = 8, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var btn = new Border
            {
                Width = 48, Height = 15, CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center,
                Cursor = new Cursor(StandardCursorType.Hand), Child = tb,
            };
            void Paint()
            {
                bool on = Killed(b);
                btn.Background = on ? hue : Brushes.Transparent;
                btn.BorderBrush = on ? hue : NotaPalette.BorderStrong;
                tb.Foreground = on ? NotaPalette.TextOnAccent : TextSecondary;
            }
            btn.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(btn).Properties.IsLeftButtonPressed) return;   // right-click bubbles (MIDI learn)
                SetP(LowKill + b, Killed(b) ? 0 : 1); RefreshAll(); e.Handled = true;
            };
            ToolTip.SetTip(btn, $"Kill the {Eq3Math.Names[b].ToLowerInvariant()} band — removes it outright, with a 5\u2009ms glide");
            Learn(btn, LowKill + b);
            readouts.Add(Paint); Paint();
            return btn;
        }

        Control Strip(int b)
        {
            var hue = Eq3Math.Hue(b);
            var name = Caps(Eq3Math.Names[b], hue, 8); name.HorizontalAlignment = HorizontalAlignment.Center; name.LetterSpacing = 1;
            var span = Mono("", 7, TextTertiary); span.HorizontalAlignment = HorizontalAlignment.Center;
            var val = Mono("", 9, TextPrimary); val.HorizontalAlignment = HorizontalAlignment.Center;
            var fader = new Eq3Fader { Margin = new Thickness(0, 3, 0, 0) };
            fader.GestureBegin += () => Begin(Low + b);
            fader.GestureEnd += () => End(Low + b);
            fader.Changed += db => { GainRaw(b, db); RefreshAll(); };
            fader.ResetRequested += () => ResetBand(b);
            ToolTip.SetTip(fader, "Drag up / down for the band's gain (Shift for fine steps); double-click resets to 0\u2009dB and lifts the kill");
            Learn(fader, Low + b);
            readouts.Add(() =>
            {
                bool k = Killed(b), iso = Iso();
                double db = BandDb(b);
                if (!fader.Dragging) fader.Set(db, k, Eq3Math.TopDb(iso), Eq3Math.BottomDb(iso), hue);
                span.Text = Span(b);
                val.Text = k ? "−∞" : Eq3Math.Db(db);
                val.Foreground = k ? TextTertiary : Math.Abs(db) > 0.05 ? AccentBright : TextPrimary;
            });
            var col = new DockPanel
            {
                LastChildFill = true, Margin = new Thickness(0, 4, 0, 6),
                Children =
                {
                    Docked(name, Dock.Top), Docked(span, Dock.Top),
                    Docked(new Border { Margin = new Thickness(0, 3, 0, 0), Child = KillButton(b) }, Dock.Bottom),
                    Docked(new Border { Margin = new Thickness(0, 3, 0, 0), Child = val }, Dock.Bottom),
                    fader,
                },
            };
            var bar = new Border { Height = 2, Background = hue, CornerRadius = NotaRadius.Top(NotaRadius.TileValue) };
            return new Border
            {
                Width = 62, Margin = new Thickness(0, 0, 5, 0), Background = Card2, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
                CornerRadius = NotaRadius.Tile, ClipToBounds = true,
                Child = new DockPanel { Children = { Docked(bar, Dock.Top), col } },
            };
        }

        // ======================================================================
        // Response window
        // ======================================================================
        var graph = new Eq3Graph();
        graph.XoverBegin += w => Begin(w == 0 ? FreqLo : FreqHi);
        graph.XoverEnd += w => End(w == 0 ? FreqLo : FreqHi);
        graph.XoverChanged += (w, hz) => { if (w == 0) Raw(FreqLo, Eq3Math.LoNorm(hz)); else Raw(FreqHi, Eq3Math.HiNorm(hz)); RefreshAll(); };
        graph.XoverReset += w =>
        {
            if (w == 0) SetP(FreqLo, Eq3Math.LoNorm(Math.Min(250, F2() / 2)));
            else SetP(FreqHi, Eq3Math.HiNorm(Math.Max(2500, F1() * 2)));
            RefreshAll();
        };
        graph.GainBegin += b => Begin(Low + b);
        graph.GainEnd += b => End(Low + b);
        graph.GainChanged += (b, db) => { GainRaw(b, db); RefreshAll(); };
        graph.GainReset += ResetBand;
        Learn(graph, FreqLo);
        ToolTip.SetTip(graph, "Each band in its colour, the sum in brass, the output spectrum behind. Drag a crossover handle left / right; drag anywhere else to ride that band's gain. Double-click resets.");

        // Header: crossover readouts · Output · Range · Slope.
        Control XRead(string label, Func<string> text)
        {
            var v = Mono("", 8, TextPrimary);
            readouts.Add(() => v.Text = text());
            return Row(4, new TextBlock { Text = label, FontSize = 7, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center }, v);
        }

        // Output: a mono readout that drags up / down, ±24 dB.
        Control OutControl()
        {
            var v = Mono("", 8, TextPrimary);
            var box = new Border { Background = Brushes.Transparent, Padding = new Thickness(2, 0), Cursor = new Cursor(StandardCursorType.SizeNorthSouth), Child = v, VerticalAlignment = VerticalAlignment.Center };
            bool drag = false; double y0 = 0, v0 = 0;
            box.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(box).Properties.IsLeftButtonPressed) return;
                e.Handled = true;
                if (e.ClickCount == 2) { SetP(Gain, 0.5); RefreshAll(); return; }
                drag = true; y0 = e.GetPosition(box).Y; v0 = P(Gain);
                e.Pointer.Capture(box); Begin(Gain);
            };
            box.PointerMoved += (_, e) =>
            {
                if (!drag) return;
                bool fine = (e.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Meta)) != 0;
                double y = e.GetPosition(box).Y;
                double db = Math.Round(OutDb(Math.Clamp(v0 + (y0 - y) / 140.0 * (fine ? 0.2 : 1), 0, 1)) * 10) / 10;   // 0.1 dB steps
                y0 = y; v0 = 0.5 + db / 48;
                Raw(Gain, v0); RefreshAll();
            };
            box.PointerReleased += (_, e) => { if (!drag) return; drag = false; e.Pointer.Capture(null); End(Gain); };
            readouts.Add(() =>
            {
                double db = OutDb(P(Gain));
                v.Text = Eq3Math.Db(db) + "\u2009dB";
                v.Foreground = Math.Abs(db) > 0.05 ? AccentBright : TextPrimary;
            });
            ToolTip.SetTip(box, "Output gain, ±24\u2009dB — drag up / down (Shift for fine), double-click resets");
            Learn(box, Gain);
            return Row(4, Caps("OUT"), box);
        }

        Control RangeSeg()
        {
            var seg = Segments(new[] { "+6", "±15" }, () => Iso() ? 0 : 1, i =>
            {
                bool want = i == 0, iso = Iso();
                if (want == iso || pc <= Range) return;
                var dbs = new double[3];
                for (int b = 0; b < 3; b++) dbs[b] = BandDb(b);
                SetP(Range, want ? 1 : 0);
                for (int b = 0; b < 3; b++) SetP(Low + b, Eq3Math.GainNorm(dbs[b], want));   // keep each band's dB
                RefreshAll();
            }, out var sync, padX: 4);
            readouts.Add(sync);
            Learn(seg, Range);
            ToolTip.SetTip(seg, "Fader range — +6: −24 … +6\u2009dB, the isolator throw; ±15: −15 … +15\u2009dB, the classic EQ-3 range. The bands keep their dB.");
            return Row(4, Caps("RANGE"), seg);
        }

        Control SlopeSeg()
        {
            var seg = Segments(new[] { "24\u2009dB", "48\u2009dB" }, () => Lr8() ? 1 : 0, i => { SetP(Slope, i); RefreshAll(); }, out var sync, padX: 4);
            readouts.Add(sync);
            Learn(seg, Slope);
            ToolTip.SetTip(seg, "Crossover slope — Linkwitz-Riley 24 or 48\u2009dB/oct: steeper touches the neighbours less");
            return Row(4, Caps("SLOPE"), seg);
        }

        var headLeft = Row(8, Caps("CROSSOVER"),
            XRead("low / mid", () => Eq3Math.Hz(F1())),
            XRead("mid / high", () => Eq3Math.Hz(F2())));
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, Margin = new Thickness(8, 0) };
        head.Children.Add(new Border { ClipToBounds = true, Child = headLeft });   // trims rather than runs under the controls
        var headRight = Row(8, OutControl(), RangeSeg(), SlopeSeg());
        Grid.SetColumn(headRight, 1); head.Children.Add(headRight);
        var headBar = new Border { Height = 20, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Child = head };
        var graphBox = new Border
        {
            Background = Card2, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile, ClipToBounds = true,
            Child = new DockPanel { Children = { Docked(headBar, Dock.Top), new Border { Padding = new Thickness(5), Child = graph } } },
        };

        // ======================================================================
        // Status strip
        // ======================================================================
        var statusLeft = new TextBlock { FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var statusRight = Mono("", 8, TextSecondary);
        readouts.Add(() =>
        {
            var parts = new List<string> { (Lr8() ? "LR 48" : "LR 24") + "\u2009dB/oct", Eq3Math.Hz(F1()) + " / " + Eq3Math.Hz(F2()) };
            var killed = new List<string>();
            for (int b = 0; b < 3; b++) if (Killed(b)) killed.Add(Eq3Math.Names[b].ToLowerInvariant());
            if (killed.Count > 0) parts.Add("kill " + string.Join(", ", killed));
            if (!Iso()) parts.Add("range ±15\u2009dB");
            double o = OutDb(P(Gain));
            if (Math.Abs(o) > 0.05) parts.Add("out " + Eq3Math.Db(o) + "\u2009dB");
            statusLeft.Text = string.Join(" · ", parts);
            double sr = Sc(S_SampleRate);
            statusRight.Text = sr > 0 ? NotaNum.F($"{sr / 1000:0.#}\u2009kHz · latency {Sc(S_Latency):0} smp · CPU {Sc(S_Cpu) * 100:0.0}\u2009%") : "";
        });
        var statusGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        statusGrid.Children.Add(statusLeft);
        Grid.SetColumn(statusRight, 1); statusGrid.Children.Add(statusRight);
        var status = new Border { Height = 18, Background = Sunken, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(8, 0), Child = statusGrid };

        // ---- assemble ---------------------------------------------------------------
        var bodyRow = new DockPanel { LastChildFill = true, Margin = new Thickness(5) };
        for (int b = 0; b < 3; b++) bodyRow.Children.Add(Docked(Strip(b), Dock.Left));
        bodyRow.Children.Add(graphBox);
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.SurfaceInset, Children = { Docked(status, Dock.Bottom), bodyRow } };

        void Refresh()
        {
            scN = engine.DeviceScope(track, di, scope, kScope);
            if (!graph.Dragging)
                graph.Set(BandDb(0), BandDb(1), BandDb(2), Killed(0), Killed(1), Killed(2), F1(), F2(), Lr8(), Iso());
            graph.SetSpectrum(scope, kTele, scN >= kScope ? kSpec : 0, Sc(S_Analysed) > 0.5);
            RefreshAll();
        }
        ctx.AddDeviceRefresher(Refresh);
        Refresh();
        return root;
    }
}
