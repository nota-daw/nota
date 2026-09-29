// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the Nota Scale editor (MIDI effect kind 2), almanac mockups 1a / 1b.
// Two sizes share every value; the S / L toggle in the shell header flips the card (the
// choice is the device's "View" param, so it persists with the project):
//   • L 700 × 260 — ROOT stepper over the scale TYPE list (Major / Minor / Dorian / Phryg /
//     Penta / Custom, each with its note count) | NOTE MAP: the twelve pitch classes as key
//     cells — scale notes Brass Deep, the root edged Brass Light, the rest naming where they
//     fold; click one to add / remove it (the scale turns Custom) | FOLD: Nearest / Down / Up,
//     the last IN → OUT, Range (± an octave, drag for semitones), Follow key, Learn and Clear —
//     over a status line.
//   • S 260 × 260 — root stepper, type and fold on one row, the note map, IN → OUT. Range,
//     Follow key, Learn and Clear are L-only (presets still set them); the header keeps the
//     same preset picker as L.
// The note sounding now lights its cell and fades after release (engine telemetry, so even a
// note shorter than a frame flashes); the cell it folded onto gets a fainter ring.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;

namespace Nota.App;

internal sealed class ScaleMidiBody : IMidiDeviceBody
{
    // Param indices — mirror MidiScale.h.
    internal const int PRoot = 0, PScale = 1, PTranspose = 2, PMask0 = 3, PFold = 15, PFollowKey = 16,
                       PRangeLo = 17, PRangeHi = 18, PLearn = 19, PView = 20, Custom = 10;
    private static readonly string[] Notes = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
    // Every Scale value's name (0..9 presets, 10 Custom) and mask (bits relative to the root).
    internal static readonly string[] ScaleNames = { "Major", "Minor", "Harm Minor", "Dorian", "Phryg", "Lydian", "Mixolyd", "Penta", "Penta Min", "Chromatic", "Custom" };
    internal static readonly int[] PresetMasks = { 2741, 1453, 2477, 1709, 1451, 2773, 1717, 661, 1193, 4095 };
    // The TYPE list of the mockup → Scale value. The last row is Custom (or names an unlisted preset).
    private static readonly int[] TypeRows = { 0, 1, 3, 4, 7, Custom };
    private static readonly string[] Folds = { "Nearest", "Down", "Up" };

    private static readonly IBrush Ground = NotaPalette.SurfaceInset;
    private static readonly IBrush Island = NotaPalette.SurfaceCard;
    private static readonly IBrush Bd = NotaPalette.BorderDefault;
    private static readonly IBrush Rule = NotaPalette.GraphBorder;
    private static readonly IBrush Well = NotaPalette.BgSunken;
    private static readonly IBrush BrassLit = NotaPalette.AccentBright;
    private static readonly IBrush Txt = NotaPalette.TextPrimary;
    private static readonly IBrush Sub = NotaPalette.TextSecondary;
    private static readonly IBrush Muted = NotaPalette.TextMuted;
    private static readonly IBrush Cap = NotaPalette.TextTertiary;
    private static readonly IBrush Axis = NotaPalette.TextAxis;

    public double Width => 700;
    public bool FullBleed => true;

    private static bool HasLayout(IAudioEngine e, int track, int mi) => e.MidiEffectParamCount(track, mi) > PView;
    private static bool IsMini(IAudioEngine e, int track, int mi) => HasLayout(e, track, mi) && e.MidiEffectGetParam(track, mi, PView) >= 0.5f;
    public double WidthFor(IAudioEngine engine, int trackId, int index) => IsMini(engine, trackId, index) ? 260 : 700;

    // ---- the scale model (mirrors MidiScale.h) -------------------------------------------------
    internal static int RootOf(Func<int, float> g) => (((int)Math.Round(g(PRoot))) % 12 + 12) % 12;
    internal static int ScaleOf(Func<int, float> g) => Math.Clamp((int)Math.Round(g(PScale)), 0, Custom);
    internal static int MaskOf(Func<int, float> g)
    {
        int sc = ScaleOf(g);
        if (sc < Custom) return PresetMasks[sc];
        int m = 0; for (int i = 0; i < 12; i++) if (g(PMask0 + i) >= 0.5f) m |= 1 << i;
        return m;
    }
    internal static int Count(int mask) { int n = 0; for (int i = 0; i < 12; i++) n += mask >> i & 1; return n; }
    internal static string KeyTag(Func<int, float> g) => $"{Notes[RootOf(g)]} {ScaleNames[ScaleOf(g)]}";

    public Control? HeaderAccessory(DeviceCardContext ctx, int index)
    {
        var e = ctx.Engine; int t = ctx.TrackId;
        if (!HasLayout(e, t, index)) return null;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        if (!IsMini(e, t, index))
        {
            var tag = Mono("", Cap, 8); tag.LetterSpacing = 0.6;
            void Sync() => tag.Text = KeyTag(p => e.MidiEffectGetParam(t, index, p)).ToUpperInvariant();
            Sync();
            ctx.AddDeviceRefresher(Sync);
            ToolTip.SetTip(tag, "The key in effect: root and scale");
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
        if (!HasLayout(engine, track, mi)) return new TextBlock { Text = "Nota Scale", Margin = new Thickness(8) };
        float G(int p) => engine.MidiEffectGetParam(track, mi, p);
        void S(int p, double v) => engine.MidiEffectSetParam(track, mi, p, (float)v);
        void Begin(int p) => engine.BeginAutomationWrite(track, AutomationTarget.MidiDeviceParam, mi, p, "");
        void End(int p) => engine.EndAutomationWrite(track, AutomationTarget.MidiDeviceParam, mi, p, "");
        void Set(int p, double v) { Begin(p); S(p, v); End(p); }
        void Learn(Control c, int p, string name) => MidiLearn.Bind(c, MidiTarget.MidiDeviceParam(track, mi, p), name);
        bool Bypassed() => engine.MidiEffectBypassed(track, mi);

        bool mini = IsMini(engine, track, mi);
        var readouts = new List<Action>();
        void Refresh() { foreach (var r in readouts) r(); }

        int Root() => RootOf(G);
        int Sc() => ScaleOf(G);
        int Mask() => MaskOf(G);
        int Fold() => Math.Clamp((int)Math.Round(G(PFold)), 0, 2);
        string TypeName() => ScaleNames[Sc()];
        int Remapped() => Mask() == 0 ? 0 : 12 - Count(Mask());

        // Editing the notes needs the mask in the Mask params: copy a preset's there and go Custom.
        void MakeCustom()
        {
            if (Sc() >= Custom) return;
            int m = Mask();
            for (int i = 0; i < 12; i++) Set(PMask0 + i, (m >> i) & 1);
            Set(PScale, Custom);
        }
        void PickType(int sc) { if (sc == Custom) MakeCustom(); else Set(PScale, sc); Refresh(); }
        void ToggleNote(int pc)
        {
            MakeCustom();
            int rel = ((pc - Root()) % 12 + 12) % 12;
            Set(PMask0 + rel, G(PMask0 + rel) >= 0.5f ? 0 : 1);
            Refresh();
        }
        void StepRoot(int d) { Set(PRoot, ((Root() + d) % 12 + 12) % 12); Refresh(); }
        // Clear leaves the root alone in a Custom scale — build up from there, or Learn.
        void Clear() { for (int i = 0; i < 12; i++) Set(PMask0 + i, i == 0 ? 1 : 0); Set(PScale, Custom); Refresh(); }

        // ---- live telemetry: IN → OUT and the note-map animation ------------------------------------
        var scope = new float[32];
        var lastIn = new int[12]; var lastOut = new int[12]; bool primed = false;
        int li = -1, lo = -1;
        var map = new ScaleNoteMap { Mini = mini };
        map.Toggled += ToggleNote;
        ToolTip.SetTip(map, "Click a note to add it to or take it out of the scale (the scale turns Custom)");
        void Poll()
        {
            int n = engine.MidiEffectScope(track, mi, scope);
            bool by = Bypassed();
            li = n >= 2 && !by ? (int)scope[0] : -1;
            lo = n >= 2 && !by ? (int)scope[1] : -1;
            int heldIn = 0, heldOut = 0, hitIn = 0, hitOut = 0;
            if (n >= 32)
            {
                for (int pc = 0; pc < 12; pc++)
                {
                    int ci = (int)scope[8 + pc], co = (int)scope[20 + pc];
                    if (primed && ci != lastIn[pc]) hitIn |= 1 << pc;
                    if (primed && co != lastOut[pc]) hitOut |= 1 << pc;
                    lastIn[pc] = ci; lastOut[pc] = co;
                }
                primed = true;
                heldIn = (int)scope[4]; heldOut = (int)scope[5];
            }
            if (by) heldIn = heldOut = hitIn = hitOut = 0;
            map.Animate(heldIn, heldOut, hitIn, hitOut);
        }
        readouts.Add(() => map.SetScale(Root(), Mask(), Fold()));

        Poll();
        Control rootView = mini ? BuildMini() : BuildLarge();
        ctx.AddDeviceRefresher(() => { Poll(); Refresh(); });
        Refresh();
        return rootView;

        // =========================================================================================
        Control BuildLarge()
        {
            // ---- ROOT + TYPE ----
            var list = new StackPanel { Margin = new Thickness(0, 3) };
            for (int i = 0; i < TypeRows.Length; i++)
            {
                int sc = TypeRows[i];
                bool last = i == TypeRows.Length - 1;
                bool On() => last ? Array.IndexOf(TypeRows, Sc()) < 0 || Sc() == Custom : Sc() == sc;
                var bar = new Border { Width = 2, Margin = new Thickness(0, 3), HorizontalAlignment = HorizontalAlignment.Left };
                var name = new TextBlock { Text = ScaleNames[sc], FontSize = 9, VerticalAlignment = VerticalAlignment.Center };
                var count = Mono("", Axis, 7); count.HorizontalAlignment = HorizontalAlignment.Right;
                var rowB = new Border { Height = 18, Cursor = new Cursor(StandardCursorType.Hand),
                    Child = new Panel { Children = { bar, new DockPanel { Margin = new Thickness(8, 0), Children = { WithDock(count, Dock.Right), name } } } } };
                bool hover = false;
                void Paint()
                {
                    bool on = On();
                    rowB.Background = on ? NotaPalette.AccentSubtle : hover ? NotaPalette.GridBeat : Brushes.Transparent;
                    bar.Background = on ? NotaPalette.Accent : Brushes.Transparent;
                    name.Foreground = on ? Txt : Sub; name.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                    // The Custom row names an unlisted preset scale when one is in effect (Lydian …).
                    if (last) name.Text = on ? TypeName() : "Custom";
                    count.Text = (last ? (on ? Count(Mask()) : Count(CustomMask())) : Count(PresetMasks[sc])).ToString();
                }
                int CustomMask() { int m = 0; for (int k = 0; k < 12; k++) if (G(PMask0 + k) >= 0.5f) m |= 1 << k; return m; }
                rowB.PointerEntered += (_, _) => { hover = true; Paint(); };
                rowB.PointerExited += (_, _) => { hover = false; Paint(); };
                rowB.PointerPressed += (_, ev) =>
                {
                    if (!ev.GetCurrentPoint(rowB).Properties.IsLeftButtonPressed) return;
                    PickType(last ? Custom : sc); ev.Handled = true;
                };
                ToolTip.SetTip(rowB, last ? "Custom: your own notes — click the note map to edit" : $"{ScaleNames[sc]} — {Count(PresetMasks[sc])} notes");
                Learn(rowB, PScale, "Scale");
                readouts.Add(Paint);
                list.Children.Add(rowB);
            }
            var typeIsland = IslandBox(IslandHead(Caps("ROOT"), RootStepper()), list, 100);

            // ---- NOTE MAP ----
            var remapped = Mono("", Cap, 7);
            readouts.Add(() => remapped.Text = Mask() == 0 ? "empty · notes pass" : $"{Remapped()} of 12 remapped");
            var mapIsland = IslandBox(IslandHead(Caps("NOTE MAP"), remapped), new Border { Padding = new Thickness(8, 6), Child = map }, double.NaN);

            // ---- FOLD ----
            var foldSeg = FoldSeg(13);
            var ioIn = Mono("—", Sub, 13); var ioOut = Mono("—", BrassLit, 13);
            var ioBox = new Border
            {
                Height = 30, Background = Well, BorderBrush = Rule, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Badge,
                Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center,
                    Children = { ioIn, new TextBlock { Text = "→", FontSize = 10, Foreground = Cap, VerticalAlignment = VerticalAlignment.Center }, ioOut } },
            };
            ToolTip.SetTip(ioBox, "The last note played and what it came out as");
            readouts.Add(() => { ioIn.Text = li >= 0 ? DeviceCardKit.NoteName(li) : "—"; ioOut.Text = lo >= 0 ? DeviceCardKit.NoteName(lo) : "—"; });
            var io = new StackPanel { Spacing = 3, Children = { Caps("LAST IN → OUT"), ioBox } };

            var rangeLbl = Caps("RANGE"); rangeLbl.Width = 34;
            var range = new Grid { ColumnDefinitions = new ColumnDefinitions("34,*,*"), ColumnSpacing = 6 };
            var rLo = RangeBox(PRangeLo, "Range low"); var rHi = RangeBox(PRangeHi, "Range high");
            Grid.SetColumn(rLo, 1); Grid.SetColumn(rHi, 2);
            range.Children.Add(rangeLbl); range.Children.Add(rLo); range.Children.Add(rHi);

            var follow = FollowSwitch();
            var learnBtn = LearnButton();
            var clearBtn = Button("Clear", Clear, "Clear the scale down to the root (Custom)");
            var btns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 4 };
            Grid.SetColumn(clearBtn, 1); btns.Children.Add(learnBtn); btns.Children.Add(clearBtn);

            var foldBody = new DockPanel { Margin = new Thickness(8, 6), Children =
            {
                WithDock(btns, Dock.Bottom),
                new StackPanel { Spacing = 7, Children = { foldSeg, io, range, follow } },
            } };
            var foldIsland = IslandBox(IslandHead(Caps("FOLD"), null), foldBody, 160);

            typeIsland.Margin = new Thickness(0, 0, 5, 0); foldIsland.Margin = new Thickness(5, 0, 0, 0);
            var islands = new DockPanel { Children = { WithDock(typeIsland, Dock.Left), WithDock(foldIsland, Dock.Right), mapIsland } };

            var statusL = new TextBlock { FontSize = 8, Foreground = Sub, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var statusR = Mono("", Sub);
            readouts.Add(() =>
            {
                int t = (int)Math.Round(G(PTranspose));
                statusL.Text = $"{Notes[Root()]} {TypeName()} · {Count(Mask())} notes · fold {Folds[Fold()].ToLowerInvariant()} · "
                             + $"{DeviceCardKit.NoteName(RangeLo())}–{DeviceCardKit.NoteName(RangeHi())}"
                             + (t != 0 ? $" · transpose {(t > 0 ? "+" : "−")}{Math.Abs(t)}" : "")
                             + (G(PFollowKey) >= 0.5f ? " · follow key" : "")
                             + (G(PLearn) >= 0.5f ? " · learning" : "");
                statusR.Text = Bypassed() ? "bypassed" : "MIDI · latency 0 smp";
            });
            return Framed(islands, statusL, statusR);
        }

        // =========================================================================================
        Control BuildMini()
        {
            var top = new DockPanel { Children = { WithDock(RootStepper(), Dock.Left), WithDock(TypeDrop(), Dock.Left), FoldSeg(12) } };
            ((Control)top.Children[1]).Margin = new Thickness(5, 0);

            var ioIn = Mono("—", Sub, 11); var ioOut = Mono("—", BrassLit, 11);
            var ioBar = new Border
            {
                Height = 24, Background = Well, BorderBrush = Rule, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Badge, Padding = new Thickness(8, 0),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children =
                    { Mono("IN → OUT", Cap, 7), ioIn, new TextBlock { Text = "→", FontSize = 9, Foreground = Cap, VerticalAlignment = VerticalAlignment.Center }, ioOut } },
            };
            readouts.Add(() => { ioIn.Text = li >= 0 ? DeviceCardKit.NoteName(li) : "—"; ioOut.Text = lo >= 0 ? DeviceCardKit.NoteName(lo) : "—"; });

            var inner = new DockPanel { Children =
            {
                WithDock(top, Dock.Top), WithDock(ioBar, Dock.Bottom), new Border { Margin = new Thickness(0, 6), Child = map },
            } };
            var island = new Border { Background = Island, BorderBrush = Bd, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile,
                Padding = new Thickness(6), Child = inner };

            var statusL = new TextBlock { FontSize = 8, Foreground = Sub, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var statusR = Mono("", Sub);
            readouts.Add(() =>
            {
                statusL.Text = $"{Notes[Root()]} {TypeName()} · {Count(Mask())} notes";
                statusR.Text = Bypassed() ? "bypassed" : Mask() == 0 ? "empty" : $"{Remapped()} of 12 remapped";
            });
            return Framed(island, statusL, statusR);
        }

        // ---- shared pieces -----------------------------------------------------------------------
        int RangeLo() => Math.Clamp((int)Math.Round(G(PRangeLo)), 0, 127);
        int RangeHi() => Math.Clamp((int)Math.Round(G(PRangeHi)), 0, 127);

        Control Framed(Control main, TextBlock left, TextBlock right)
        {
            var status = new Border { Height = 18, Background = Well, BorderBrush = Bd, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(8, 0),
                Child = new DockPanel { Children = { WithDock(right, Dock.Right), left } } };
            right.Margin = new Thickness(8, 0, 0, 0);
            return new DockPanel { Background = Ground, Children = { WithDock(status, Dock.Bottom), new Border { Padding = new Thickness(5), Child = main } } };
        }

        Control IslandHead(Control left, Control? right)
        {
            var d = new DockPanel { Margin = new Thickness(8, 0, right is null ? 8 : 4, 0) };
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

        Border Chip(Control child, double w, double h = 14)
        {
            return new Border { Width = w, Height = h, CornerRadius = NotaRadius.Badge, Background = NotaPalette.TrackOff, Cursor = new Cursor(StandardCursorType.Hand),
                VerticalAlignment = VerticalAlignment.Center, Child = child };
        }

        // ‹ C › — click to step, scroll too. Follow key moves it on its own.
        Control RootStepper()
        {
            Border Arrow(GlyphKind glyph, int d)
            {
                var b = Chip(new Glyph(glyph, 7) { Foreground = Sub, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, 14);
                b.PointerPressed += (_, ev) => { if (!ev.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; StepRoot(d); ev.Handled = true; };
                b.PointerEntered += (_, _) => b.Background = NotaPalette.SurfaceHover;
                b.PointerExited += (_, _) => b.Background = NotaPalette.TrackOff;
                return b;
            }
            var name = Mono("", BrassLit, 9); name.FontWeight = FontWeight.SemiBold; name.Width = 20; name.TextAlignment = TextAlignment.Center;
            readouts.Add(() => name.Text = Notes[Root()]);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent,
                Children = { Arrow(GlyphKind.ChevronLeft, -1), name, Arrow(GlyphKind.ChevronRight, 1) } };
            row.PointerWheelChanged += (_, ev) => { StepRoot(ev.Delta.Y > 0 ? 1 : -1); ev.Handled = true; };
            ToolTip.SetTip(row, "Root — click the arrows or scroll");
            Learn(row, PRoot, "Root");
            return row;
        }

        Control FoldSeg(double h)
        {
            var seg = DeviceCardKit.Segments(Folds, Fold, i => { Set(PFold, i); Refresh(); }, out var sync, fill: true, padX: 2, fontSize: 8);
            seg.Height = h + 4;
            readouts.Add(sync);
            ToolTip.SetTip(seg, "Fold — where an out-of-scale note goes: the nearest scale note (a tie goes up), the one below or the one above");
            Learn(seg, PFold, "Fold");
            return seg;
        }

        // The mini card's scale type: a dropdown (scroll steps through the list).
        Control TypeDrop()
        {
            var name = new TextBlock { FontSize = 9, Foreground = Txt, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var box = new Border
            {
                Width = 54, Height = 16, Background = NotaPalette.GridBeat, BorderBrush = Bd, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Badge,
                Padding = new Thickness(6, 0), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
                Child = new DockPanel { Children = { WithDock(new Glyph(GlyphKind.ChevronDown, 7) { Foreground = Cap, VerticalAlignment = VerticalAlignment.Center }, Dock.Right), name } },
            };
            readouts.Add(() => name.Text = TypeName());
            box.PointerPressed += (_, ev) =>
            {
                if (!ev.GetCurrentPoint(box).Properties.IsLeftButtonPressed) return;
                var fly = new MenuFlyout();
                foreach (int sc in TypeRows)
                {
                    int iv = sc;
                    var item = new MenuItem { Header = ScaleNames[sc] };
                    if (Sc() == sc) item.Icon = new Avalonia.Controls.Shapes.Ellipse { Width = 6, Height = 6, Fill = NotaPalette.Accent };
                    item.Click += (_, _) => PickType(iv);
                    fly.Items.Add(item);
                }
                fly.ShowAt(box);
                ev.Handled = true;
            };
            box.PointerWheelChanged += (_, ev) =>
            {
                int at = Array.IndexOf(TypeRows, Sc()); if (at < 0) at = TypeRows.Length - 1;
                PickType(TypeRows[Math.Clamp(at + (ev.Delta.Y < 0 ? 1 : -1), 0, TypeRows.Length - 1)]); ev.Handled = true;
            };
            ToolTip.SetTip(box, "Scale type");
            Learn(box, PScale, "Scale");
            return box;
        }

        // − C-1 + : an octave per click; drag the note up / down for semitones; double-click resets.
        Control RangeBox(int p, string label)
        {
            int Get() => p == PRangeLo ? RangeLo() : RangeHi();
            void Put(int v)
            {
                v = p == PRangeLo ? Math.Clamp(v, 0, RangeHi()) : Math.Clamp(v, RangeLo(), 127);
                S(p, v); Refresh();
            }
            TextBlock Sign(string s) => new() { Text = s, Width = 12, FontSize = 9, Foreground = Muted, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var dec = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = Sign("−") };
            var inc = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = Sign("+") };
            var val = Mono("", Txt, 8); val.TextAlignment = TextAlignment.Center; val.HorizontalAlignment = HorizontalAlignment.Stretch;
            var valHit = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.SizeNorthSouth), Child = val };
            dec.PointerPressed += (_, ev) => { if (!ev.GetCurrentPoint(dec).Properties.IsLeftButtonPressed) return; Begin(p); Put(Get() - 12); End(p); ev.Handled = true; };
            inc.PointerPressed += (_, ev) => { if (!ev.GetCurrentPoint(inc).Properties.IsLeftButtonPressed) return; Begin(p); Put(Get() + 12); End(p); ev.Handled = true; };
            bool drag = false; double y0 = 0; int v0 = 0;
            valHit.PointerPressed += (_, ev) =>
            {
                if (!ev.GetCurrentPoint(valHit).Properties.IsLeftButtonPressed) return;
                ev.Handled = true;
                if (ev.ClickCount == 2) { Begin(p); Put(p == PRangeLo ? 0 : 127); End(p); return; }
                drag = true; y0 = ev.GetPosition(valHit).Y; v0 = Get(); ev.Pointer.Capture(valHit); Begin(p);
            };
            valHit.PointerMoved += (_, ev) => { if (drag) Put(v0 + (int)Math.Round((y0 - ev.GetPosition(valHit).Y) / 4)); };
            void Stop() { if (!drag) return; drag = false; End(p); }
            valHit.PointerReleased += (_, ev) => { if (drag) ev.Pointer.Capture(null); Stop(); };
            valHit.PointerCaptureLost += (_, _) => Stop();
            valHit.PointerWheelChanged += (_, ev) => { Begin(p); Put(Get() + (ev.Delta.Y > 0 ? 1 : -1)); End(p); ev.Handled = true; };
            readouts.Add(() => val.Text = DeviceCardKit.NoteName(Get()));
            var box = new Border
            {
                Height = 14, Background = Well, BorderBrush = Bd, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Badge,
                Child = new DockPanel { Children = { WithDock(dec, Dock.Left), WithDock(inc, Dock.Right), valHit } },
            };
            ToolTip.SetTip(box, $"{label} — ± an octave, drag or scroll the note for semitones, double-click resets. Notes outside the range pass through.");
            Learn(box, p, label);
            return box;
        }

        Control FollowSwitch()
        {
            var sw = new SwitchTrack { Width = SwitchTrack.W, Height = SwitchTrack.H, VerticalAlignment = VerticalAlignment.Center };
            var lbl = new TextBlock { Text = "Follow key · from input", FontSize = 8, VerticalAlignment = VerticalAlignment.Center };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Children = { sw, lbl } };
            row.PointerPressed += (_, ev) =>
            {
                if (!ev.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
                Set(PFollowKey, G(PFollowKey) >= 0.5f ? 0 : 1); Refresh(); ev.Handled = true;
            };
            readouts.Add(() => { bool on = G(PFollowKey) >= 0.5f; sw.IsOn = on; lbl.Foreground = on ? Txt : Muted; });
            ToolTip.SetTip(row, "Follow key — the root moves to the key that best fits what you play (keeping the scale type)");
            Learn(row, PFollowKey, "Follow Key");
            return row;
        }

        Border Button(string text, Action click, string tip)
        {
            var tb = new TextBlock { Text = text, FontSize = 8, Foreground = Txt, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var b = new Border { Height = 16, CornerRadius = NotaRadius.Badge, Background = NotaPalette.TrackOff, BorderBrush = NotaPalette.TrackOff, BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand), Child = tb };
            b.PointerEntered += (_, _) => { if (b.Tag is null) b.Background = NotaPalette.SurfaceHover; };
            b.PointerExited += (_, _) => { if (b.Tag is null) b.Background = NotaPalette.TrackOff; };
            b.PointerPressed += (_, ev) => { if (!ev.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; click(); ev.Handled = true; };
            ToolTip.SetTip(b, tip);
            return b;
        }

        // Learn is a mode: "Learning…" while it collects the notes you play into a fresh Custom scale.
        Control LearnButton()
        {
            var b = Button("Learn", () => { Set(PLearn, G(PLearn) >= 0.5f ? 0 : 1); Refresh(); },
                "Learn — play the notes of your scale; they pass through untouched and become the Custom scale. Click again to finish.");
            var tb = (TextBlock)b.Child!;
            readouts.Add(() =>
            {
                bool on = G(PLearn) >= 0.5f;
                b.Tag = on ? "on" : null;
                b.Background = on ? NotaPalette.AccentSubtle : b.IsPointerOver ? NotaPalette.SurfaceHover : NotaPalette.TrackOff;
                b.BorderBrush = on ? NotaPalette.BorderBrass : NotaPalette.TrackOff;
                tb.Foreground = on ? NotaPalette.AccentHover : Txt;
                tb.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                tb.Text = on ? "Learning…" : "Learn";
            });
            Learn(b, PLearn, "Learn");
            return b;
        }
    }
}
