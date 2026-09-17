// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the Nota Chord editor (MIDI effect kind 1), mockup 3b (700×260 on the
// shared shell): a LIVE strip (chord-type presets · Strum · Keep root · Fold in scale) over
// a body of a SHIFTS stack (six voices — semitone offset + relative velocity, active voices
// lit) beside a RESULT panel (the resulting chord for a reference C3 as note names + a
// two-octave keyboard preview lighting played vs added, plus Voices count + Spread).
// Detune is intentionally absent — the note stream carries integer pitch to the instrument.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nota.App;

internal sealed class ChordMidiBody : IMidiDeviceBody
{
    // Param indices — mirror MidiChord.h.
    private const int Voice1 = 0, Strum = 6, KeepRoot = 7, Spread = 8, Fold = 9, Vel1 = 10;
    private const int Voices = 6;

    private static readonly string[] NoteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
    private static readonly (string name, int[] semis)[] Templates =
    {
        ("Maj7", new[] { 4, 7, 11 }), ("Min7", new[] { 3, 7, 10 }),
        ("Sus4", new[] { 5, 7 }), ("5th", new[] { 7, 12 }),
    };

    private static readonly IBrush HdrBg = NotaPalette.SurfaceCard;
    private static readonly IBrush RailBg = NotaPalette.SurfaceInset;
    private static readonly IBrush Bd = NotaPalette.BorderDefault;
    private static readonly IBrush Inset = NotaPalette.BgSunken;
    private static readonly IBrush Amber = NotaPalette.Accent;
    private static readonly IBrush AmberLit = NotaPalette.AccentBright;
    private static readonly IBrush Txt = NotaPalette.TextPrimary;
    private static readonly IBrush Muted = NotaPalette.TextTertiary;
    private static readonly IBrush Sub = NotaPalette.TextSecondary;
    private static readonly IBrush Green = NotaPalette.Success;
    private static readonly IBrush Card = NotaPalette.SurfaceRaised;
    private static readonly IBrush Ink = NotaPalette.TextOnAccent;
    private static readonly IBrush AmberSubtle = NotaPalette.Wash(NotaPalette.Accent, 0x24);

    public double Width => 700;
    public bool FullBleed => true;

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId, mi = index;
        float G(int p) => engine.MidiEffectGetParam(track, mi, p);
        void S(int p, double v) => engine.MidiEffectSetParam(track, mi, p, (float)v);
        int Semi(int v) => (int)Math.Round(G(Voice1 + v));

        var readouts = new List<Action>();
        static TextBlock Mono(string t, IBrush c, double fs = 9) { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.Mono"); return tb; }
        static TextBlock Cap(string t, IBrush? c = null, double fs = 8) => new() { Text = t, FontSize = fs, FontWeight = FontWeight.Bold, Foreground = c ?? Muted, VerticalAlignment = VerticalAlignment.Center };
        static string IntervalLabel(int s) => s == 0 ? "off" : s == 12 ? "+oct" : s == -12 ? "−oct" : (s > 0 ? "+" : "−") + Math.Abs(s);

        // ---- chord preview (reference root C3=60), mirroring MidiChord.h ----
        static int FoldMajor(int iv) { int[] d = { 0, 2, 4, 5, 7, 9, 11 }; int within = ((iv % 12) + 12) % 12, octs = (iv - within) / 12, best = 0, bd = 99; foreach (int g in d) { int df = Math.Abs(g - within); if (df < bd) { bd = df; best = g; } } return octs * 12 + best; }
        var keys = new ChordKeysViz { VerticalAlignment = VerticalAlignment.Stretch };
        List<int> Chord(int root)
        {
            bool keep = G(KeepRoot) >= 0.5f, fold = G(Fold) >= 0.5f;
            double sp = Math.Clamp(G(Spread) / 100.0, 0, 1);
            var list = new List<int>();
            if (keep) list.Add(root);
            for (int v = 0; v < Voices; v++) { int s = Semi(v); if (s == 0) continue; int p = root + (fold ? FoldMajor(s) : s); if (sp > 0) p += 12 * (int)Math.Floor(sp * (v + 1) + 1e-9); if (p is >= 0 and <= 127) list.Add(p); }
            return list.Distinct().OrderBy(x => x).ToList();
        }
        void Refresh() { foreach (var r in readouts) r(); }

        // ---- generic controls -------------------------------------------------
        Control RawSlider(int p, double min, double max, Func<double, string> fmt, double w, bool bipolar = false, double valW = 40)
        {
            var val = Mono(fmt(G(p)), Txt); val.Width = valW; val.TextAlignment = TextAlignment.Right;
            var fill = new Border { Height = 3, Background = Amber, CornerRadius = NotaRadius.Clip, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var handle = new Border { Width = 7, Height = 9, Background = Sub, CornerRadius = NotaRadius.Clip, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var slot = new Panel { Height = 9, Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent };
            if (w > 0) slot.Width = w;
            slot.Children.Add(new Border { Height = 3, Background = Inset, CornerRadius = NotaRadius.Clip, VerticalAlignment = VerticalAlignment.Center });
            if (bipolar) slot.Children.Add(new Border { Width = 1, Background = Bd, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch, Margin = new Thickness(0, 1) });
            slot.Children.Add(fill); slot.Children.Add(handle);
            double Norm(double v) => (v - min) / (max - min);
            void Vis(double v) { double W = slot.Bounds.Width, n = Norm(v); if (bipolar) { double c = W / 2, x = n * W; fill.Width = Math.Abs(x - c); fill.HorizontalAlignment = HorizontalAlignment.Left; fill.Margin = new Thickness(Math.Min(c, x), 0, 0, 0); } else { fill.Width = n * W; fill.Margin = new Thickness(0); } handle.Margin = new Thickness(Math.Clamp(n * W - 3.5, 0, Math.Max(0, W - 7)), 0, 0, 0); }
            bool drag = false;
            void SetX(double x) { double n = Math.Clamp(x / Math.Max(1, slot.Bounds.Width), 0, 1); double v = min + n * (max - min); S(p, v); Vis(v); val.Text = fmt(v); Refresh(); }
            slot.PointerPressed += (_, e) => { drag = true; e.Pointer.Capture(slot); SetX(e.GetPosition(slot).X); };
            slot.PointerMoved += (_, e) => { if (drag) SetX(e.GetPosition(slot).X); };
            slot.PointerReleased += (_, e) => { if (drag) { drag = false; e.Pointer.Capture(null); } };
            MidiLearn.Bind(slot, MidiTarget.MidiDeviceParam(track, mi, p), engine.MidiEffectParamName(track, mi, p));
            slot.SizeChanged += (_, _) => Vis(G(p));   // flexible-width sliders reposition once laid out
            readouts.Add(() => { if (!drag) { double v = G(p); Vis(v); val.Text = fmt(v); } });
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions(w > 0 ? "Auto,Auto" : "*,Auto"), ColumnSpacing = 5, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(val, 1); g.Children.Add(slot); g.Children.Add(val);
            return g;
        }
        // Vertical-drag numeric (velocity offset).
        Control DragNum(int p, double min, double max, Func<double, string> fmt, double w)
        {
            var tb = Mono(fmt(G(p)), Sub, 8); tb.Width = w; tb.TextAlignment = TextAlignment.Right; tb.Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
            bool drag = false; double sy = 0, sv = 0;
            tb.PointerPressed += (_, e) => { drag = true; sy = e.GetPosition(tb).Y; sv = G(p); e.Pointer.Capture(tb); };
            tb.PointerMoved += (_, e) => { if (drag) { double dv = (sy - e.GetPosition(tb).Y) / 100.0 * (max - min); S(p, Math.Clamp(sv + dv, min, max)); tb.Text = fmt(G(p)); Refresh(); } };
            tb.PointerReleased += (_, e) => { if (drag) { drag = false; e.Pointer.Capture(null); } };
            readouts.Add(() => { if (!drag) tb.Text = fmt(G(p)); });
            return tb;
        }
        Control Toggle(int p, string label)
        {
            var b = new Border { CornerRadius = NotaRadius.Control, BorderThickness = new Thickness(1), Padding = new Thickness(8, 2), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = label, FontSize = 9, FontWeight = FontWeight.SemiBold } };
            void Sync() { bool on = G(p) >= 0.5f; b.Background = on ? NotaPalette.AccentSubtle : Card; b.BorderBrush = on ? NotaPalette.BorderBrass : Bd; ((TextBlock)b.Child!).Foreground = on ? NotaPalette.AccentHover : Sub; }
            b.PointerPressed += (_, _) => { S(p, G(p) >= 0.5f ? 0 : 1); Refresh(); };
            readouts.Add(Sync); Sync();
            MidiLearn.Bind(b, MidiTarget.MidiDeviceParam(track, mi, p), label);
            return b;
        }
        // Chord-type presets: highlight the template matching the current voices (else Custom).
        Control ChordChips()
        {
            var names = Templates.Select(t => t.name).Append("Custom").ToArray();
            var arr = new Border[names.Length];
            int MatchIdx()
            {
                var active = Enumerable.Range(0, Voices).Select(Semi).Where(s => s != 0).OrderBy(x => x).ToArray();
                for (int i = 0; i < Templates.Length; i++) if (active.SequenceEqual(Templates[i].semis.OrderBy(x => x))) return i;
                return Templates.Length;   // Custom
            }
            void Hi() { int cur = MatchIdx(); for (int i = 0; i < names.Length; i++) { bool on = i == cur; arr[i].Background = on ? Amber : Brushes.Transparent; ((TextBlock)arr[i].Child!).Foreground = on ? Ink : Muted; } }
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
            for (int i = 0; i < names.Length; i++)
            {
                int iv = i;
                var c = new Border { CornerRadius = NotaRadius.Badge, Padding = new Thickness(7, 1), Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = names[i], FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = Muted } };
                c.PointerPressed += (_, _) =>
                {
                    if (iv < Templates.Length) { var t = Templates[iv].semis; for (int v = 0; v < Voices; v++) S(Voice1 + v, v < t.Length ? t[v] : 0); }
                    Refresh();
                };
                arr[i] = c; row.Children.Add(c);
            }
            readouts.Add(Hi); Hi();
            return new Border { Background = Inset, BorderBrush = Bd, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Control, Padding = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Child = row };
        }

        // ---- LIVE strip ----
        var strumBlock = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = { Cap("STRUM"), RawSlider(Strum, 0, 120, v => $"{v:0}\u2009ms", 52, false, 40) } };
        var liveL = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { Cap("CHORD"), ChordChips(), strumBlock } };
        var liveR = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Children = { Toggle(KeepRoot, "Keep root"), Toggle(Fold, "Fold in scale") } };
        var liveGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(liveR, 1); liveGrid.Children.Add(liveL); liveGrid.Children.Add(liveR);
        var liveStrip = new Border { Height = 34, Background = HdrBg, BorderBrush = Bd, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(9, 0), Child = liveGrid };

        // ---- SHIFTS stack ----
        Control ShiftRow(int v)
        {
            var dot = new Ellipse { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center };
            var lbl = new TextBlock { FontSize = 9, FontWeight = FontWeight.SemiBold, Width = 30, VerticalAlignment = VerticalAlignment.Center };
            var semi = RawSlider(Voice1 + v, -24, 24, s => { int i = (int)Math.Round(s); return i == 0 ? "·" : (i > 0 ? "+" : "") + i; }, 0, true, 26);
            var vel = DragNum(Vel1 + v, -100, 100, x => { int i = (int)Math.Round(x); return (i >= 0 ? "+" : "") + i; }, 30);
            readouts.Add(() => { int s = Semi(v); bool on = s != 0; dot.Fill = on ? Green : Bd; lbl.Text = IntervalLabel(s); lbl.Foreground = on ? Txt : Muted; });
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), ColumnSpacing = 7, VerticalAlignment = VerticalAlignment.Center };
            var cells = new Control[] { dot, lbl, semi, vel };
            for (int c = 0; c < cells.Length; c++) { Grid.SetColumn(cells[c], c); g.Children.Add(cells[c]); }
            return new Border { Background = NotaPalette.BgApp, BorderBrush = Bd, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Control, Padding = new Thickness(7, 0), Height = 26, Child = g };
        }
        var shiftHead = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Height = 11 };
        var sh1 = Cap("semitones · velocity"); sh1.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(sh1, 1);
        shiftHead.Children.Add(Cap("SHIFTS")); shiftHead.Children.Add(sh1);
        var shiftStack = new StackPanel { Spacing = 3 };
        shiftStack.Children.Add(shiftHead);
        for (int v = 0; v < Voices; v++) shiftStack.Children.Add(ShiftRow(v));
        var shiftPanel = new Border { Width = 330, Padding = new Thickness(8, 6), Child = shiftStack };

        // ---- RESULT panel ----
        var noteText = Mono("", AmberLit); noteText.HorizontalAlignment = HorizontalAlignment.Right;
        var resHead = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Height = 11 };
        Grid.SetColumn(noteText, 1); resHead.Children.Add(Cap("RESULT")); resHead.Children.Add(noteText);
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children =
        {
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { new Border { Width = 8, Height = 8, Background = AmberLit, CornerRadius = NotaRadius.Clip, VerticalAlignment = VerticalAlignment.Center }, Cap("played", Sub) } },
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { new Border { Width = 8, Height = 8, Background = Amber, CornerRadius = NotaRadius.Clip, VerticalAlignment = VerticalAlignment.Center }, Cap("added", Sub) } },
        } };
        var voicesText = Mono("", Txt);
        var voicesRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = { Cap("VOICES"), voicesText } };
        var spreadRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = { Cap("SPREAD"), RawSlider(Spread, 0, 100, v => $"{v:0}\u2009%", 0, false, 34) } };
        var resBottom = new StackPanel { Spacing = 4, Children = { voicesRow, spreadRow } };
        var resDock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(resHead, Dock.Top); DockPanel.SetDock(legend, Dock.Top);
        DockPanel.SetDock(resBottom, Dock.Bottom);
        var keyBox = new Border { Height = 44, Margin = new Thickness(0, 4), Child = keys };
        // Docked children first (top: header+legend, bottom: voices/spread), fill (keyboard) last.
        resDock.Children.Add(resHead); resDock.Children.Add(legend); resDock.Children.Add(resBottom); resDock.Children.Add(keyBox);
        var resPanel = new Border { Background = RailBg, BorderBrush = Bd, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(9, 6), Child = resDock };

        // result-driven readouts
        readouts.Add(() =>
        {
            var chord = Chord(60);
            keys.Set(60, chord);
            noteText.Text = string.Join(" ", chord.Select(p => NoteNames[((p % 12) + 12) % 12] + (p / 12 - 2)));   // C3 = 60
            int active = Enumerable.Range(0, Voices).Count(v => Semi(v) != 0);
            voicesText.Text = $"{active} of {Voices}";
        });

        var body = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(shiftPanel, Dock.Left); body.Children.Add(shiftPanel); body.Children.Add(resPanel);
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.BgApp };
        DockPanel.SetDock(liveStrip, Dock.Top); root.Children.Add(liveStrip); root.Children.Add(body);

        Refresh();
        return root;
    }
}
