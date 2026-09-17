// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the Nota Scale editor (MIDI effect kind 2), mockup 3b (700×260 on the
// shared shell): a LIVE strip (Root stepper · scale-type presets · Fold mode) over a body of
// an interactive NOTE MAP (the 12 pitch classes; click to add/remove one from the scale;
// out-of-scale notes show the arrow to where they fold) beside a right rail (live IN→OUT of
// the last remapped note, Range limiter, Follow Key, Learn/Clear).

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;

namespace Nota.App;

internal sealed class ScaleMidiBody : IMidiDeviceBody
{
    // Param indices — mirror MidiScale.h.
    private const int PRoot = 0, PScale = 1, PMask0 = 3, PFold = 15, PFollowKey = 16, PRangeLo = 17, PRangeHi = 18, PLearn = 19;
    private const int Custom = 10;
    private static readonly string[] Notes = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
    // Chip label → Scale param value (preset index, or Custom).
    private static readonly (string name, int idx)[] Chips = { ("Major", 0), ("Minor", 1), ("Dorian", 3), ("Phryg", 4), ("Penta", 8), ("Custom", Custom) };
    private static readonly int[] PresetMasks = { 2741, 1453, 2477, 1709, 1451, 2773, 1717, 661, 1193, 4095 };
    private static readonly string[] Folds = { "Nearest", "Down", "Up" };

    private static readonly IBrush HdrBg = NotaPalette.SurfaceCard;
    private static readonly IBrush Bd = NotaPalette.BorderDefault;
    private static readonly IBrush Inset = NotaPalette.BgSunken;
    private static readonly IBrush Amber = NotaPalette.Accent;
    private static readonly IBrush AmberLit = NotaPalette.AccentBright;
    private static readonly IBrush Teal = NotaPalette.Teal;
    private static readonly IBrush Txt = NotaPalette.TextPrimary;
    private static readonly IBrush Muted = NotaPalette.TextTertiary;
    private static readonly IBrush Sub = NotaPalette.TextSecondary;
    private static readonly IBrush Green = NotaPalette.Success;
    private static readonly IBrush Card = NotaPalette.SurfaceRaised;
    private static readonly IBrush Ink = NotaPalette.TextOnAccent;
    private static readonly IBrush AmberSubtle = NotaPalette.Wash(NotaPalette.Accent, 0x28);

    public double Width => 700;
    public bool FullBleed => true;

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId, mi = index;
        float G(int p) => engine.MidiEffectGetParam(track, mi, p);
        void S(int p, double v) => engine.MidiEffectSetParam(track, mi, p, (float)v);
        int Gi(int p) => (int)Math.Round(G(p));

        var readouts = new List<Action>();
        static TextBlock Mono(string t, IBrush c, double fs = 9) { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.Mono"); return tb; }
        static TextBlock Cap(string t, IBrush? c = null, double fs = 8) => new() { Text = t, FontSize = fs, FontWeight = FontWeight.Bold, Foreground = c ?? Muted, VerticalAlignment = VerticalAlignment.Center };
        static string NoteName(int pitch) => Notes[((pitch % 12) + 12) % 12] + (pitch / 12 - 2);   // C3 = 60
        void Refresh() { foreach (var r in readouts) r(); }

        int Root() => Math.Clamp(Gi(PRoot), 0, 11);
        int Mask()
        {
            int sc = Gi(PScale);
            if (sc < Custom) return PresetMasks[Math.Clamp(sc, 0, 9)];
            int m = 0; for (int i = 0; i < 12; i++) if (G(PMask0 + i) >= 0.5f) m |= 1 << i;
            return m;
        }
        // Ensure the mask is editable: copy the effective preset mask into Mask params + switch to Custom.
        void MakeCustom()
        {
            if (Gi(PScale) >= Custom) return;
            int m = Mask();
            for (int i = 0; i < 12; i++) S(PMask0 + i, (m >> i) & 1);
            S(PScale, Custom);
        }
        bool InScale(int absPc) => (Mask() & (1 << (((absPc - Root()) % 12 + 12) % 12))) != 0;
        int SnapRel(int rel, int mask, int fold)
        {
            bool Has(int x) => (mask & (1 << (((x % 12) + 12) % 12))) != 0;
            if (Has(rel)) return ((rel % 12) + 12) % 12;
            if (fold == 1) { for (int d = 1; d < 12; d++) if (Has(rel - d)) return ((rel - d) % 12 + 12) % 12; }
            else if (fold == 2) { for (int d = 1; d < 12; d++) if (Has(rel + d)) return (rel + d) % 12; }
            else { for (int d = 1; d < 7; d++) { if (Has(rel + d)) return (rel + d) % 12; if (Has(rel - d)) return ((rel - d) % 12 + 12) % 12; } }
            return ((rel % 12) + 12) % 12;
        }

        // ---- generic controls ----
        Control Seg(int p, string[] names, Action<int>? extra = null)
        {
            var seg = DeviceCardKit.Segments(names, () => Math.Clamp(Gi(p), 0, names.Length - 1), iv => { S(p, iv); extra?.Invoke(iv); Refresh(); }, out var sync);
            readouts.Add(sync);
            MidiLearn.Bind(seg, MidiTarget.MidiDeviceParam(track, mi, p), engine.MidiEffectParamName(track, mi, p));
            return seg;
        }
        Control Toggle(int p, string label, IBrush accent)
        {
            var host = DeviceCardKit.Switch(label, () => G(p) >= 0.5f, () => { S(p, G(p) >= 0.5f ? 0 : 1); Refresh(); }, out var sync);
            readouts.Add(sync);
            MidiLearn.Bind(host, MidiTarget.MidiDeviceParam(track, mi, p), label);
            return host;
        }
        Control Btn(string label, Action onClick)
        {
            var b = new Border { Background = Card, BorderBrush = Bd, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Badge, Padding = new Thickness(0, 3), HorizontalAlignment = HorizontalAlignment.Stretch, Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = label, FontSize = 9, Foreground = Sub, HorizontalAlignment = HorizontalAlignment.Center } };
            b.PointerPressed += (_, _) => { onClick(); Refresh(); };
            return b;
        }
        Control PitchDrag(int p, double w)   // vertical-drag MIDI pitch shown as a note name
        {
            var tb = Mono(NoteName(Gi(p)), Txt); tb.Width = w; tb.Cursor = new Cursor(StandardCursorType.SizeNorthSouth); tb.TextAlignment = TextAlignment.Center;
            bool drag = false; double sy = 0, sv = 0;
            tb.PointerPressed += (_, e) => { drag = true; sy = e.GetPosition(tb).Y; sv = G(p); e.Pointer.Capture(tb); };
            tb.PointerMoved += (_, e) => { if (drag) { double dv = (sy - e.GetPosition(tb).Y) / 6.0; S(p, Math.Clamp(sv + dv, 0, 127)); tb.Text = NoteName(Gi(p)); } };
            tb.PointerReleased += (_, e) => { if (drag) { drag = false; e.Pointer.Capture(null); } };
            MidiLearn.Bind(tb, MidiTarget.MidiDeviceParam(track, mi, p), engine.MidiEffectParamName(track, mi, p));
            readouts.Add(() => { if (!drag) tb.Text = NoteName(Gi(p)); });
            return tb;
        }

        // ---- LIVE strip ----
        var rootName = Mono("C", Txt); rootName.Width = 20; rootName.TextAlignment = TextAlignment.Center;
        Border StepBtn(GlyphKind t, int dir)
        {
            var b = new Border { Width = 16, CornerRadius = NotaRadius.Badge, BorderBrush = Bd, BorderThickness = new Thickness(1), Background = Card, Cursor = new Cursor(StandardCursorType.Hand), Child = new Glyph(t, 8) { Foreground = Sub }, VerticalAlignment = VerticalAlignment.Center };
            b.PointerPressed += (_, _) => { S(PRoot, ((Root() + dir) % 12 + 12) % 12); Refresh(); };
            return b;
        }
        readouts.Add(() => rootName.Text = Notes[Root()]);
        var rootStepper = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Children = { Cap("ROOT"), StepBtn(GlyphKind.ChevronLeft, -1), rootName, StepBtn(GlyphKind.ChevronRight, +1) } };

        // scale chips (highlight the preset matching PScale, or Custom)
        var chipArr = new Border[Chips.Length];
        void ChipsHi() { int sc = Gi(PScale); for (int i = 0; i < Chips.Length; i++) { bool on = Chips[i].idx == sc || (sc >= Custom && Chips[i].idx == Custom); chipArr[i].Background = on ? Amber : Brushes.Transparent; ((TextBlock)chipArr[i].Child!).Foreground = on ? Ink : Muted; } }
        var chipRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
        for (int i = 0; i < Chips.Length; i++) { int iv = i; var c = new Border { CornerRadius = NotaRadius.Badge, Padding = new Thickness(7, 1), Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = Chips[i].name, FontSize = 9, Foreground = Muted } }; c.PointerPressed += (_, _) => { S(PScale, Chips[iv].idx); Refresh(); }; chipArr[i] = c; chipRow.Children.Add(c); }
        readouts.Add(ChipsHi); ChipsHi();
        var scaleChips = new Border { Background = Inset, BorderBrush = Bd, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Control, Padding = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Child = chipRow };

        var foldSeg = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Children = { Cap("FOLD"), Seg(PFold, Folds) } };
        var liveL = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Children = { rootStepper, scaleChips } };
        var liveGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(foldSeg, 1); liveGrid.Children.Add(liveL); liveGrid.Children.Add(foldSeg);
        var liveStrip = new Border { Height = 34, Background = HdrBg, BorderBrush = Bd, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(9, 0), Child = liveGrid };

        // ---- NOTE MAP (6×2 pitch classes) ----
        var remapped = Cap("", Muted); remapped.HorizontalAlignment = HorizontalAlignment.Right;
        var mapHead = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Height = 11 };
        Grid.SetColumn(remapped, 1); mapHead.Children.Add(Cap("NOTE MAP")); mapHead.Children.Add(remapped);
        var grid = new Grid { VerticalAlignment = VerticalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch };
        for (int c = 0; c < 6; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        for (int r = 0; r < 2; r++) grid.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));
        for (int pc = 0; pc < 12; pc++)
        {
            int iv = pc;
            var nlbl = new TextBlock { Text = Notes[pc], FontSize = 9, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
            var arrow = Mono("", Teal, 8); arrow.HorizontalAlignment = HorizontalAlignment.Center;
            var cell = new Border { Margin = new Thickness(2), CornerRadius = NotaRadius.Control, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), Child = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 1, Children = { nlbl, arrow } } };
            cell.PointerPressed += (_, _) => { MakeCustom(); int rel = ((iv - Root()) % 12 + 12) % 12; S(PMask0 + rel, (G(PMask0 + rel) >= 0.5f) ? 0 : 1); Refresh(); };
            readouts.Add(() =>
            {
                bool inScale = InScale(iv), isRoot = iv == Root();
                cell.Background = inScale ? AmberSubtle : Inset;
                cell.BorderBrush = isRoot ? Green : inScale ? Amber : Bd;
                nlbl.Foreground = inScale ? Txt : Muted;
                if (inScale) { arrow.Text = isRoot ? "root" : "•"; arrow.Foreground = isRoot ? Green : Amber; }
                else { int rel = ((iv - Root()) % 12 + 12) % 12; int tgt = (SnapRel(rel, Mask(), Math.Clamp(Gi(PFold), 0, 2)) + Root()) % 12; arrow.Text = "→ " + Notes[tgt]; arrow.Foreground = Teal; }
            });
            Grid.SetColumn(cell, pc % 6); Grid.SetRow(cell, pc / 6); grid.Children.Add(cell);
        }
        var mapDock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(mapHead, Dock.Top); mapDock.Children.Add(mapHead); mapDock.Children.Add(new Border { Margin = new Thickness(0, 4, 0, 0), Child = grid });

        // ---- right rail ----
        var inTxt = Mono("—", Sub, 11); var arrTxt = new TextBlock { Text = "→", FontSize = 9, Foreground = Teal, VerticalAlignment = VerticalAlignment.Center };
        var outTxt = Mono("—", AmberLit, 11); var distTxt = Mono("", Muted, 8); distTxt.HorizontalAlignment = HorizontalAlignment.Right;
        var ioInner = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*") };
        var ioCells = new Control[] { inTxt, arrTxt, outTxt, distTxt };
        for (int c = 0; c < ioCells.Length; c++) { Grid.SetColumn(ioCells[c], c); ((Control)ioCells[c]).Margin = new Thickness(c == 0 ? 0 : 4, 0, 0, 0); ioInner.Children.Add(ioCells[c]); }
        var ioBox = new Border { Background = Inset, BorderBrush = Bd, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Control, Padding = new Thickness(7, 5), Child = ioInner };
        readouts.Add(() =>
        {
            int li = engine.MidiEffectLastIn(track, mi), lo = engine.MidiEffectLastOut(track, mi);
            if (li < 0) { inTxt.Text = "—"; outTxt.Text = "—"; distTxt.Text = ""; }
            else { inTxt.Text = NoteName(li); outTxt.Text = NoteName(lo); int d = lo - li; distTxt.Text = d == 0 ? "0" : (d > 0 ? "+" : "−") + Math.Abs(d) + "\u2009st"; }
        });
        var rangeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Children = { Cap("RANGE"), PitchDrag(PRangeLo, 30), new TextBlock { Text = "–", FontSize = 9, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center }, PitchDrag(PRangeHi, 30) } };
        var followKey = Toggle(PFollowKey, "FOLLOW KEY", Teal);
        var learnBtn = Btn("Learn", () => S(PLearn, 1));
        var clearBtn = Btn("Clear", () => { for (int i = 0; i < 12; i++) S(PMask0 + i, 0); S(PScale, Custom); });
        var btnRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 4 };
        Grid.SetColumn(clearBtn, 1); btnRow.Children.Add(learnBtn); btnRow.Children.Add(clearBtn);
        var railStack = new StackPanel { Spacing = 6, Children = { Cap("LAST IN → OUT"), ioBox, rangeRow, followKey } };
        var railDock = new DockPanel { LastChildFill = false, VerticalAlignment = VerticalAlignment.Stretch };
        DockPanel.SetDock(railStack, Dock.Top); DockPanel.SetDock(btnRow, Dock.Bottom);
        railDock.Children.Add(railStack); railDock.Children.Add(btnRow);
        var rail = new Border { Width = 150, BorderBrush = Bd, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(9, 0, 0, 0), Child = railDock };

        // remapped count in the note-map header
        readouts.Add(() => { int m = Mask(); int inN = 0; for (int i = 0; i < 12; i++) if ((m & (1 << i)) != 0) inN++; remapped.Text = $"{12 - inN} of 12 remapped"; });

        var body = new DockPanel { LastChildFill = true, Margin = new Thickness(9, 7) };
        DockPanel.SetDock(rail, Dock.Right); body.Children.Add(rail); body.Children.Add(mapDock);
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.BgApp };
        DockPanel.SetDock(liveStrip, Dock.Top); root.Children.Add(liveStrip); root.Children.Add(body);

        ctx.AddDeviceRefresher(Refresh);   // live IN→OUT + Follow-Key / Learn feedback
        Refresh();
        return root;
    }
}
