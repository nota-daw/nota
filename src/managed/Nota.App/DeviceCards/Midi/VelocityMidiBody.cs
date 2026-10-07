// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the Nota Velocity editor (MIDI effect kind 4), almanac mockups 1a / 1b.
// Two sizes share every value; the S / L toggle in the shell header flips the card (the
// choice is the device's "View" param, so it persists with the project):
//   • L 700 × 260 — MODE (Curve / Compand / Fixed as a list, then Drive — or the Fixed value —
//     with its slider and what it does) | TRANSFER (input across, output up: the curve, the
//     band Random can spread a note over, the Out range as dashed lines, the last 12 notes as
//     dots) | OUT RANGE (a two-handle range, the Random switch + amount + Both / Up / Down, a
//     LAST 12 NOTES histogram: outline in, fill out) — over a status line.
//   • S 260 × 260 — the mode chip over the TRANSFER window and four knobs (Drive or Value ·
//     Random · Low · High); the footer's "random both" steps Both → Up → Down → off.
// Dots and bars are the real notes the device shaped (telemetry), not a demo. The maths is
// VelocityModel, a mirror of MidiVelocity.h, so picture == sound.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;

namespace Nota.App;

/// <summary>Nota Velocity's transfer model in musical units — mirrors MidiVelocity.h.</summary>
internal static class VelocityModel
{
    internal const int PDrive = 0, PFixed = 1, POutLo = 2, PRandom = 3, PMode = 4, POutHi = 5, PRandomDir = 6, PRandomOn = 7, PView = 8;
    internal static readonly string[] Modes = { "Curve", "Compand", "Fixed" };
    internal static readonly string[] ModeHints = { "x^1/d", "S", "=" };
    internal static readonly string[] Dirs = { "Both", "Up", "Down" };
    internal const double DriveMin = 0.25, DriveMax = 4.0, RandomSpan = 64;

    internal static int Mode(Func<int, float> g) => Math.Clamp((int)Math.Round(g(PMode)), 0, 2);
    internal static int Dir(Func<int, float> g) => Math.Clamp((int)Math.Round(g(PRandomDir)), 0, 2);
    internal static bool RandomOn(Func<int, float> g) => g(PRandomOn) >= 0.5f;
    internal static double Drive(Func<int, float> g) => Math.Clamp(g(PDrive), 0.05, DriveMax);

    /// <summary>Drive's 0..1 control position: drive = 4^(2f − 1), so 1.00 sits in the middle.</summary>
    internal static double DriveNorm(double d) => Math.Clamp((Math.Log(Math.Max(DriveMin, d), 4) + 1) / 2, 0, 1);
    internal static double DriveFromNorm(double n) => Math.Round(Math.Pow(4, Math.Clamp(n, 0, 1) * 2 - 1), 2);

    /// <summary>The Fixed value / Out range ends as MIDI velocities 1..127.</summary>
    internal static int Vel(float norm) => Math.Clamp((int)Math.Round(norm * 127), 1, 127);
    internal static int Fixed(Func<int, float> g) => Vel(g(PFixed));
    internal static (int Lo, int Hi) Range(Func<int, float> g)
    {
        int lo = Vel(g(POutLo)), hi = Vel(g(POutHi));
        return lo <= hi ? (lo, hi) : (hi, lo);
    }
    /// <summary>Random as ± velocity (0..64).</summary>
    internal static int RandomAmt(Func<int, float> g) => (int)Math.Round(Math.Clamp(g(PRandom), 0, 1) * RandomSpan);

    /// <summary>The transfer shape 0..1 → 0..1 (MidiVelocity::transfer).</summary>
    internal static double Shape(double n, int mode, double drive, double fixedN)
    {
        n = Math.Clamp(n, 0, 1);
        if (mode == 2) return fixedN;
        if (mode == 1)
        {
            double c = 2 * n - 1;
            return Math.Clamp(0.5 + Math.Sign(c) * Math.Pow(Math.Abs(c), 1 / drive) * 0.5, 0, 1);
        }
        return Math.Pow(n, 1 / drive);
    }

    /// <summary>Output (0..1) before Random for an input 0..1: the shape, into the Out range.</summary>
    internal static double Base(Func<int, float> g, double n)
    {
        var (lo, hi) = RangeN(g);
        return lo + Shape(n, Mode(g), Drive(g), Math.Clamp(g(PFixed), 0, 1)) * (hi - lo);
    }
    internal static (double Lo, double Hi) RangeN(Func<int, float> g)
    {
        double lo = Math.Clamp(g(POutLo), 0, 1), hi = Math.Clamp(g(POutHi), 0, 1);
        if (lo > hi) (lo, hi) = (hi, lo);
        return (Math.Max(lo, 1 / 127.0), Math.Max(hi, 1 / 127.0));
    }
    /// <summary>Random's (down, up) reach in 0..1 units — (0, 0) while the switch is off.</summary>
    internal static (double Down, double Up) Spread(Func<int, float> g)
    {
        if (!RandomOn(g)) return (0, 0);
        double r = Math.Clamp(g(PRandom), 0, 1) * RandomSpan / 127.0;
        return Dir(g) switch { 1 => (0, r), 2 => (r, 0), _ => (r, r) };
    }

    internal static string DriveText(Func<int, float> g) => Mode(g) == 2 ? Fixed(g).ToString() : $"{Drive(g):0.00}";
    internal static string RandomText(Func<int, float> g)
    {
        int r = RandomAmt(g);
        return Dir(g) switch { 1 => $"+{r}", 2 => $"−{r}", _ => $"±{r}" };
    }
    internal static string DriveHint(Func<int, float> g)
    {
        int mode = Mode(g); double d = Drive(g);
        if (mode == 2) return "every note at this velocity";
        if (Math.Abs(d - 1) < 0.005) return "linear";
        return mode == 0 ? (d > 1 ? "soft notes louder" : "soft notes quieter")
                         : (d > 1 ? "stretched to the edges" : "squeezed to the middle");
    }
}

internal sealed class VelocityMidiBody : IMidiDeviceBody
{
    private const int PDrive = VelocityModel.PDrive, PFixed = VelocityModel.PFixed, POutLo = VelocityModel.POutLo,
                      PRandom = VelocityModel.PRandom, PMode = VelocityModel.PMode, POutHi = VelocityModel.POutHi,
                      PRandomDir = VelocityModel.PRandomDir, PRandomOn = VelocityModel.PRandomOn;
    internal const int PView = VelocityModel.PView;
    private static readonly string[] Modes = VelocityModel.Modes;
    private static readonly string[] Dirs = VelocityModel.Dirs;

    private static readonly IBrush Ground = NotaPalette.SurfaceInset;
    private static readonly IBrush Island = NotaPalette.SurfaceCard;
    private static readonly IBrush Bd = NotaPalette.BorderDefault;
    private static readonly IBrush Rule = NotaPalette.GraphBorder;
    private static readonly IBrush Well = NotaPalette.BgSunken;
    private static readonly IBrush BrassLit = NotaPalette.AccentBright;
    private static readonly IBrush Txt = NotaPalette.TextPrimary;
    private static readonly IBrush Sub = NotaPalette.TextSecondary;
    private static readonly IBrush Cap = NotaPalette.TextTertiary;
    private static readonly IBrush Axis = NotaPalette.TextAxis;
    private static readonly IBrush AxisLbl = NotaPalette.TextDisabled;

    public double Width => 700;
    public bool FullBleed => true;

    private static bool HasLayout(IAudioEngine e, int track, int mi) => e.MidiEffectParamCount(track, mi) > PView;
    private static bool IsMini(IAudioEngine e, int track, int mi) => HasLayout(e, track, mi) && e.MidiEffectGetParam(track, mi, PView) >= 0.5f;
    public double WidthFor(IAudioEngine engine, int trackId, int index) => IsMini(engine, trackId, index) ? 260 : 700;

    public Control? HeaderAccessory(DeviceCardContext ctx, int index)
    {
        var e = ctx.Engine; int t = ctx.TrackId;
        if (!HasLayout(e, t, index)) return null;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        if (!IsMini(e, t, index))
        {
            var tag = Mono("", Cap, 8); tag.LetterSpacing = 0.6;
            void Sync() => tag.Text = Modes[VelocityModel.Mode(p => e.MidiEffectGetParam(t, index, p))].ToUpperInvariant();
            Sync();
            ctx.AddDeviceRefresher(Sync);
            ToolTip.SetTip(tag, "Velocity mode");
            row.Children.Add(tag); row.Children.Add(MidiCardHeader.LearnButton(ctx));
        }
        row.Children.Add(MidiCardHeader.SizeToggle(ctx, index, PView, () => IsMini(e, t, index)));
        return row;
    }

    // ---- small builders -------------------------------------------------------------------------
    private static TextBlock Caps(string t, IBrush? c = null, double fs = 7) => new()
    { Text = t, FontSize = fs, FontWeight = FontWeight.Bold, LetterSpacing = 0.8, Foreground = c ?? Cap, VerticalAlignment = VerticalAlignment.Center };
    private static TextBlock Mono(string t, IBrush c, double fs = 8) => new()
    { Text = t, FontSize = fs, FontFamily = NotaFonts.MonoFamily, Foreground = c, VerticalAlignment = VerticalAlignment.Center };
    private static T WithDock<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }

    // ---- body -------------------------------------------------------------------------------------
    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine; int track = ctx.TrackId, mi = index;
        if (!HasLayout(engine, track, mi)) return new TextBlock { Text = "Nota Velocity", Margin = new Thickness(8) };
        float G(int p) => engine.MidiEffectGetParam(track, mi, p);
        float Def(int p) => engine.MidiEffectParamDefault(track, mi, p);
        void S(int p, double v) => engine.MidiEffectSetParam(track, mi, p, (float)v);
        void Begin(int p) => engine.BeginAutomationWrite(track, AutomationTarget.MidiDeviceParam, mi, p, "");
        void End(int p) => engine.EndAutomationWrite(track, AutomationTarget.MidiDeviceParam, mi, p, "");
        void Set1(int p, double v) { Begin(p); S(p, v); End(p); }
        void Learn(Control c, int p, string name) => MidiLearn.Bind(c, MidiTarget.MidiDeviceParam(track, mi, p), name);
        bool Bypassed() => engine.MidiEffectBypassed(track, mi);
        int Mode() => VelocityModel.Mode(G);
        bool IsFixed() => Mode() == 2;

        bool mini = IsMini(engine, track, mi);
        var readouts = new List<Action>();
        void Refresh() { foreach (var r in readouts) r(); }

        // ---- live notes from the device (oldest → newest) ------------------------------------------
        var scope = new float[25];
        var notes = new List<(double In, double Out)>(12);
        long count = 0;
        void Poll()
        {
            int n = engine.MidiEffectScope(track, mi, scope);
            count = n >= 25 ? (long)scope[24] : 0;
            notes.Clear();
            int have = (int)Math.Min(12, count);
            for (int k = 12 - have; k < 12 && 2 * k + 1 < n; k++) notes.Add((scope[2 * k], scope[2 * k + 1]));
        }
        (int In, int Out)? Last() => notes.Count > 0 ? ((int)Math.Round(notes[^1].In * 127), (int)Math.Round(notes[^1].Out * 127)) : null;

        // DRIVE / VALUE: one control whose target follows the mode.
        int DriveParam() => IsFixed() ? PFixed : PDrive;
        double DriveNorm() => IsFixed() ? (VelocityModel.Fixed(G) - 1) / 126.0 : VelocityModel.DriveNorm(VelocityModel.Drive(G));
        void DriveSet(double n)
        {
            if (IsFixed()) S(PFixed, Math.Round(1 + Math.Clamp(n, 0, 1) * 126) / 127.0);
            else S(PDrive, VelocityModel.DriveFromNorm(n));
        }
        string DriveLabel() => IsFixed() ? "VALUE" : "DRIVE";

        // The Out range in whole velocities, keeping low below high.
        void SetLo(double v) { var (_, hi) = VelocityModel.Range(G); S(POutLo, Math.Clamp(Math.Round(v), 1, hi - 1) / 127.0); }
        void SetHi(double v) { var (lo, _) = VelocityModel.Range(G); S(POutHi, Math.Clamp(Math.Round(v), lo + 1, 127) / 127.0); }

        Poll();
        Control rootView = mini ? BuildMini() : BuildLarge();
        ctx.AddDeviceRefresher(() => { Poll(); Refresh(); });
        Refresh();
        return rootView;

        // =========================================================================================
        Control BuildLarge()
        {
            // ---- MODE ----
            var list = new StackPanel { Margin = new Thickness(0, 3) };
            for (int i = 0; i < Modes.Length; i++)
            {
                int m = i;
                var bar = new Border { Width = 2, Margin = new Thickness(0, 3), HorizontalAlignment = HorizontalAlignment.Left };
                var name = new TextBlock { Text = Modes[m], FontSize = 9, VerticalAlignment = VerticalAlignment.Center };
                var hint = Mono(VelocityModel.ModeHints[m], Axis, 7); hint.HorizontalAlignment = HorizontalAlignment.Right;
                var rowB = new Border { Height = 18, Cursor = new Cursor(StandardCursorType.Hand),
                    Child = new Panel { Children = { bar, new DockPanel { Margin = new Thickness(8, 0), Children = { WithDock(hint, Dock.Right), name } } } } };
                bool hover = false;
                void Paint()
                {
                    bool on = Mode() == m;
                    rowB.Background = on ? NotaPalette.AccentSubtle : hover ? NotaPalette.GridBeat : Brushes.Transparent;
                    bar.Background = on ? NotaPalette.Accent : Brushes.Transparent;
                    name.Foreground = on ? Txt : Sub; name.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                }
                rowB.PointerEntered += (_, _) => { hover = true; Paint(); };
                rowB.PointerExited += (_, _) => { hover = false; Paint(); };
                rowB.PointerPressed += (_, ev) =>
                {
                    if (!ev.GetCurrentPoint(rowB).Properties.IsLeftButtonPressed) return;
                    Set1(PMode, m); Refresh(); ev.Handled = true;
                };
                ToolTip.SetTip(rowB, m switch
                {
                    0 => "Curve — bend the response: Drive above 1 lifts soft notes, below 1 pushes them down",
                    1 => "Compand — the same around the middle: above 1 stretches to the edges, below 1 squeezes to the middle",
                    _ => "Fixed — every note comes out at one velocity",
                });
                Learn(rowB, PMode, "Mode");
                readouts.Add(Paint);
                list.Children.Add(rowB);
            }

            var dLabel = Caps("DRIVE");
            var dValue = Mono("", BrassLit, 13); dValue.FontWeight = FontWeight.Medium; dValue.HorizontalAlignment = HorizontalAlignment.Right;
            int dGesture = -1;
            var dTrack = new SliderTrack { Height = 11, Reset = () => { int p = DriveParam(); Set1(p, Def(p)); Refresh(); } };
            dTrack.Changed += n => { DriveSet(n); Refresh(); };
            dTrack.GestureBegin += () => { dGesture = DriveParam(); Begin(dGesture); };
            dTrack.GestureEnd += () => { if (dGesture >= 0) End(dGesture); dGesture = -1; };
            ToolTip.SetTip(dTrack, "Drag up / down, Shift for fine, double-click resets");
            Learn(dTrack, PDrive, "Drive");
            ValueEntry.Attach(dValue, DriveNorm, DriveSet, () => VelocityModel.DriveText(G),
                () => { dGesture = DriveParam(); Begin(dGesture); }, () => { End(dGesture); dGesture = -1; }, Refresh);
            var dHint = new TextBlock { FontSize = 7, Foreground = Cap, TextTrimming = TextTrimming.CharacterEllipsis };
            readouts.Add(() =>
            {
                dLabel.Text = DriveLabel(); dValue.Text = VelocityModel.DriveText(G); dHint.Text = VelocityModel.DriveHint(G);
                if (!dTrack.Dragging) dTrack.Norm = DriveNorm();
            });
            var driveBox = new Border
            {
                BorderBrush = Rule, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(8, 6),
                Child = new StackPanel { Spacing = 3, Children = { new DockPanel { Children = { WithDock(dValue, Dock.Right), dLabel } }, dTrack, dHint } },
            };
            var modeIsland = IslandBox(IslandHead(Caps("MODE"), null), new DockPanel { Children = { WithDock(driveBox, Dock.Bottom), WithDock(list, Dock.Top) } }, 120);

            // ---- TRANSFER ----
            var viz = new VelocityTransferViz { Full = true };
            ToolTip.SetTip(viz, "Input velocity across, output up. Dashed — the Out range; the band — where Random can put a note; dots — the last notes played");
            var plotInfo = Mono("", BrassLit, 7);
            readouts.Add(() =>
            {
                viz.Set(n => VelocityModel.Base(G, n), VelocityModel.RangeN(G).Lo, VelocityModel.RangeN(G).Hi, VelocityModel.Spread(G), notes, count, Bypassed());
                plotInfo.Text = IsFixed() ? $"fixed {VelocityModel.Fixed(G)}"
                    : $"drive {VelocityModel.Drive(G):0.00} · {(VelocityModel.RandomOn(G) ? VelocityModel.RandomText(G) : "no")} random";
            });
            var axis = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*"), Height = 9 };
            var a1 = Mono("in 1", AxisLbl, 7); var a2 = Mono("64", AxisLbl, 7); var a3 = Mono("127", AxisLbl, 7);
            a3.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(a2, 1); Grid.SetColumn(a3, 2);
            axis.Children.Add(a1); axis.Children.Add(a2); axis.Children.Add(a3);
            var plot = new DockPanel { Margin = new Thickness(8, 6, 8, 4), Children = { WithDock(axis, Dock.Bottom), viz } };
            axis.Margin = new Thickness(0, 2, 0, 0);
            var transferIsland = IslandBox(IslandHead(Caps("TRANSFER"), plotInfo), plot, double.NaN);

            // ---- OUT RANGE ----
            var rangeTxt = Mono("", Txt, 8);
            var range = new RangeSliderTrack
            {
                Reset = () => { Begin(POutLo); Begin(POutHi); S(POutLo, Def(POutLo)); S(POutHi, Def(POutHi)); End(POutLo); End(POutHi); Refresh(); },
            };
            range.GestureBegin += h => Begin(h == 0 ? POutLo : POutHi);
            range.GestureEnd += h => End(h == 0 ? POutLo : POutHi);
            range.Changed += (h, n) => { if (h == 0) SetLo(1 + n * 126); else SetHi(1 + n * 126); Refresh(); };
            ToolTip.SetTip(range, "Out range — drag a handle, double-click resets to 1 – 127");
            Learn(range, POutHi, "Out High");
            readouts.Add(() =>
            {
                var (lo, hi) = VelocityModel.Range(G);
                rangeTxt.Text = $"{lo} – {hi}";
                if (!range.Dragging) { range.Lo = (lo - 1) / 126.0; range.Hi = (hi - 1) / 126.0; }
            });

            var rndSw = new SwitchTrack();
            var rndLbl = Caps("RANDOM");
            var rndVal = Mono("", Txt, 8); rndVal.HorizontalAlignment = HorizontalAlignment.Right;
            var rndRow = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand),
                Child = new DockPanel { Children = { WithDock(rndVal, Dock.Right), new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { rndSw, rndLbl } } } } };
            rndRow.PointerPressed += (_, ev) =>
            {
                if (!ev.GetCurrentPoint(rndRow).Properties.IsLeftButtonPressed) return;
                Set1(PRandomOn, VelocityModel.RandomOn(G) ? 0 : 1); Refresh(); ev.Handled = true;
            };
            ToolTip.SetTip(rndRow, "Random — spread each note's velocity (the amount is kept while it is off)");
            Learn(rndRow, PRandomOn, "Random On");
            var rndTrack = new SliderTrack { Height = 11, Reset = () => { Set1(PRandom, Def(PRandom)); Refresh(); } };
            rndTrack.Changed += n => { S(PRandom, Math.Round(n * VelocityModel.RandomSpan) / VelocityModel.RandomSpan); Refresh(); };
            rndTrack.GestureBegin += () => Begin(PRandom);
            rndTrack.GestureEnd += () => End(PRandom);
            ToolTip.SetTip(rndTrack, "Random amount, ±0 – 64 velocity — drag up / down, double-click resets");
            Learn(rndTrack, PRandom, "Random");
            var dirSeg = DeviceCardKit.Segments(Dirs, () => VelocityModel.Dir(G), i => { Set1(PRandomDir, i); Refresh(); }, out var dirSync,
                fill: true, padX: 2, fontSize: 8);
            ToolTip.SetTip(dirSeg, "Random both ways, only up, or only down");
            Learn(dirSeg, PRandomDir, "Random Dir");
            readouts.Add(() =>
            {
                bool on = VelocityModel.RandomOn(G);
                rndSw.IsOn = on;
                rndLbl.Foreground = on ? Txt : Cap;
                rndVal.Foreground = on ? Txt : Cap;
                rndVal.Text = VelocityModel.RandomText(G);
                rndTrack.IsDim = !on;
                if (!rndTrack.Dragging) rndTrack.Norm = Math.Clamp(G(PRandom), 0, 1);
                dirSync();
            });
            var rndBox = new Border { BorderBrush = Rule, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 5, 0, 0),
                Child = new StackPanel { Spacing = 5, Children = { rndRow, rndTrack, dirSeg } } };

            var hist = new VelocityHistoryViz();
            ToolTip.SetTip(hist, "The last 12 notes: outline — velocity in, fill — velocity out");
            readouts.Add(() => hist.Set(notes, count, Bypassed()));
            var histBox = new StackPanel { Spacing = 3, Children = { Caps("LAST 12 NOTES"), hist } };

            var outBody = new DockPanel { Margin = new Thickness(8, 6), Children =
            {
                WithDock(histBox, Dock.Bottom),
                new StackPanel { Spacing = 6, Children = { range, rndBox } },
            } };
            var outIsland = IslandBox(IslandHead(Caps("OUT RANGE"), rangeTxt), outBody, 160);

            modeIsland.Margin = new Thickness(0, 0, 5, 0); outIsland.Margin = new Thickness(5, 0, 0, 0);
            var islands = new DockPanel { Children = { WithDock(modeIsland, Dock.Left), WithDock(outIsland, Dock.Right), transferIsland } };

            var statusL = new TextBlock { FontSize = 8, Foreground = Sub, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var statusR = Mono("", Sub);
            readouts.Add(() =>
            {
                var (lo, hi) = VelocityModel.Range(G);
                string rnd = VelocityModel.RandomOn(G) ? $"{VelocityModel.RandomText(G)} {Dirs[VelocityModel.Dir(G)].ToLowerInvariant()}" : "off";
                statusL.Text = $"{Modes[Mode()]} · {(IsFixed() ? "value " + VelocityModel.Fixed(G) : $"drive {VelocityModel.Drive(G):0.00}")} · random {rnd} · out {lo}–{hi}";
                statusR.Text = Bypassed() ? "bypassed" : Last() is { } l ? $"last {l.In} → {l.Out}" : "MIDI";
            });
            return Framed(islands, statusL, statusR);
        }

        // =========================================================================================
        Control BuildMini()
        {
            // Mode chip: click for the next mode, Shift-click for the previous.
            var modeTb = new TextBlock { FontSize = 9, Foreground = Txt, VerticalAlignment = VerticalAlignment.Center };
            var chev = new Glyph(GlyphKind.ChevronDown, 7) { Foreground = Cap, VerticalAlignment = VerticalAlignment.Center };
            var modeChip = new Border { Height = 14, Width = 70, CornerRadius = NotaRadius.Badge, Background = NotaPalette.GridBeat, BorderBrush = Bd, BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 0), Cursor = new Cursor(StandardCursorType.Hand), Margin = new Thickness(0, 0, 6, 0),
                Child = new DockPanel { Children = { WithDock(chev, Dock.Right), modeTb } } };
            modeChip.PointerPressed += (_, ev) =>
            {
                if (!ev.GetCurrentPoint(modeChip).Properties.IsLeftButtonPressed) return;
                int d = ev.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 2 : 1;
                Set1(PMode, (Mode() + d) % 3); Refresh(); ev.Handled = true;
            };
            ToolTip.SetTip(modeChip, "Velocity mode: Curve / Compand / Fixed — click for the next, Shift-click for the previous");
            Learn(modeChip, PMode, "Mode");
            var hint = Mono("", Cap, 7); hint.HorizontalAlignment = HorizontalAlignment.Right;
            var head = new DockPanel { Height = 14, Children = { WithDock(modeChip, Dock.Left), hint } };

            var viz = new VelocityTransferViz { Full = false };
            ToolTip.SetTip(viz, "Input across, output up · dashed — the Out range · band — Random · dots — the last notes");
            var lastIO = Mono("", BrassLit, 7);
            lastIO.HorizontalAlignment = HorizontalAlignment.Right; lastIO.VerticalAlignment = VerticalAlignment.Top; lastIO.Margin = new Thickness(0, 3, 5, 0);
            lastIO.IsHitTestVisible = false;
            var plot = new Panel { Margin = new Thickness(0, 4), Children = { viz, lastIO } };

            var knobs = new Grid { Height = 53, ColumnDefinitions = new ColumnDefinitions("*,*,*,*") };
            var cells = new[]
            {
                KnobCell(DriveLabel, DriveParam, DriveNorm, DriveSet, () => VelocityModel.DriveText(G), () => IsFixed() ? (Def(PFixed) * 127 - 1) / 126 : VelocityModel.DriveNorm(Def(PDrive))),
                KnobCell(() => "RANDOM", () => PRandom, () => Math.Clamp(G(PRandom), 0, 1), n =>
                {
                    S(PRandom, Math.Round(n * VelocityModel.RandomSpan) / VelocityModel.RandomSpan);
                    if (!VelocityModel.RandomOn(G)) S(PRandomOn, 1);   // turning Random up switches it on
                }, () => VelocityModel.RandomOn(G) ? VelocityModel.RandomText(G) : "off", () => Def(PRandom), dim: () => !VelocityModel.RandomOn(G)),
                KnobCell(() => "LOW", () => POutLo, () => (VelocityModel.Range(G).Lo - 1) / 126.0, n => SetLo(1 + n * 126), () => VelocityModel.Range(G).Lo.ToString(), () => 0),
                KnobCell(() => "HIGH", () => POutHi, () => (VelocityModel.Range(G).Hi - 1) / 126.0, n => SetHi(1 + n * 126), () => VelocityModel.Range(G).Hi.ToString(), () => 1),
            };
            for (int i = 0; i < cells.Length; i++) { Grid.SetColumn(cells[i], i); knobs.Children.Add(cells[i]); }

            var inner = new DockPanel { Children = { WithDock(head, Dock.Top), WithDock(knobs, Dock.Bottom), plot } };
            var island = new Border { Background = Island, BorderBrush = Bd, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile,
                Padding = new Thickness(6, 5, 6, 0), Child = inner };
            readouts.Add(() =>
            {
                modeTb.Text = Modes[Mode()];
                hint.Text = VelocityModel.DriveHint(G);
                viz.Set(n => VelocityModel.Base(G, n), VelocityModel.RangeN(G).Lo, VelocityModel.RangeN(G).Hi, VelocityModel.Spread(G), notes, count, Bypassed());
                lastIO.Text = Last() is { } l ? $"{l.In} → {l.Out}" : "";
            });

            // Footer: "random both" steps Both → Up → Down → off → Both (Shift-click backwards).
            var statusL = new TextBlock { FontSize = 8, Foreground = Sub, VerticalAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent };
            statusL.PointerEntered += (_, _) => statusL.Foreground = Txt;
            statusL.PointerExited += (_, _) => statusL.Foreground = Sub;
            statusL.PointerPressed += (_, ev) =>
            {
                if (!ev.GetCurrentPoint(statusL).Properties.IsLeftButtonPressed) return;
                int state = VelocityModel.RandomOn(G) ? VelocityModel.Dir(G) : 3;   // 0 both · 1 up · 2 down · 3 off
                state = (state + (ev.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 3 : 1)) % 4;
                Begin(PRandomOn); Begin(PRandomDir);
                if (state == 3) S(PRandomOn, 0); else { S(PRandomOn, 1); S(PRandomDir, state); }
                End(PRandomOn); End(PRandomDir);
                Refresh(); ev.Handled = true;
            };
            ToolTip.SetTip(statusL, "Random: both ways → up → down → off — click to step, Shift-click back");
            Learn(statusL, PRandomDir, "Random Dir");
            var statusR = Mono("", Sub);
            readouts.Add(() =>
            {
                statusL.Text = VelocityModel.RandomOn(G) ? $"random {Dirs[VelocityModel.Dir(G)].ToLowerInvariant()}" : "random off";
                var (lo, hi) = VelocityModel.Range(G);
                statusR.Text = Bypassed() ? "bypassed" : $"out {lo} – {hi}";
            });
            return Framed(island, statusL, statusR);
        }

        // ---- shared pieces -----------------------------------------------------------------------
        Control Framed(Control main, TextBlock left, TextBlock right)
        {
            var status = new Border { Height = 18, Background = Well, BorderBrush = Bd, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(8, 0),
                Child = new DockPanel { Children = { WithDock(right, Dock.Right), left } } };
            right.Margin = new Thickness(8, 0, 0, 0);
            return new DockPanel { Background = Ground, Children = { WithDock(status, Dock.Bottom), new Border { Padding = new Thickness(5), Child = main } } };
        }

        Control IslandHead(Control left, Control? right)
        {
            var d = new DockPanel { Margin = new Thickness(8, 0) };
            if (right is not null) { right.HorizontalAlignment = HorizontalAlignment.Right; d.Children.Add(WithDock(right, Dock.Right)); }
            d.Children.Add(left);
            return new Border { Height = 20, BorderBrush = Bd, BorderThickness = new Thickness(0, 0, 0, 1), Child = d };
        }

        Border IslandBox(Control head, Control content, double width)
        {
            var b = new Border { Background = Island, BorderBrush = Bd, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile, ClipToBounds = true,
                Child = new DockPanel { Children = { WithDock(head, Dock.Top), content } } };
            if (!double.IsNaN(width)) b.Width = width;
            return b;
        }

        // A mini-view knob: 34px gauge, caps label, mono value. Label and target param may follow the mode (DRIVE / VALUE).
        Control KnobCell(Func<string> label, Func<int> param, Func<double> norm, Action<double> set, Func<string> text, Func<double> resetNorm, Func<bool>? dim = null)
        {
            var knob = new Knob(norm(), 1.0) { Accent = true, Width = 34, Height = 34, HorizontalAlignment = HorizontalAlignment.Center, Default = resetNorm() };
            int gesture = -1;
            knob.ValueChanged += n => { set(n); Refresh(); };
            knob.GestureBegin += () => { gesture = param(); Begin(gesture); if (gesture == PRandom) Begin(PRandomOn); };
            knob.GestureEnd += () => { if (gesture >= 0) End(gesture); if (gesture == PRandom) End(PRandomOn); gesture = -1; };
            Learn(knob, param(), label());
            var lbl = new TextBlock { FontSize = 7, FontWeight = FontWeight.Bold, LetterSpacing = 0.6, Foreground = Cap, HorizontalAlignment = HorizontalAlignment.Center, LineHeight = 9 };
            var v = Mono("", Sub, 7); v.HorizontalAlignment = HorizontalAlignment.Center;
            readouts.Add(() =>
            {
                double n = norm();
                if (!knob.Dragging && Math.Abs(knob.Value - n) > 1e-6) knob.Value = n;
                knob.Default = resetNorm();
                knob.IsDim = dim?.Invoke() ?? false;
                lbl.Text = label(); v.Text = text();
            });
            return new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.SizeNorthSouth),
                Children = { knob, lbl, v } };
        }
    }
}
