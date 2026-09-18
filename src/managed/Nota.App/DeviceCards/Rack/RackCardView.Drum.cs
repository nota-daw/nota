// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the Nota Drum Rack body (instrument kind 4), in the 700 × 260 card
// the almanac draws for it. The shared shell draws the header: the kit picker (a factory
// kit replaces the pads), A/B in its context menu, and the pad count.
//
//   Pads 1fr  three views on a tab strip, under a strip with the bank (C1–C4, sixteen
//             notes each), the kit's swing and humanize, and Fold:
//             Pads  — a 4 × 4 grid, pad 1 bottom-left as on hardware. A click plays the
//                     pad and selects it; right-click has rename, choke, chain and remove;
//                     an empty pad offers an instrument; samples dropped on a pad load
//                     there (several fill the pads after it). Fold hides the empty pads.
//             Mixer — the bank's loaded pads as rows: volume, pan, mute · solo · choke.
//             Chain — the selected pad's device chain: its instrument and insert effects.
//   Pad 186   the selected pad: its one-shot, volume · pan · tune · decay, the choke
//             group, and its instrument's editor.
//   Status    the selection and the kit, in words.
//
// A pad's colour is its track-palette slot; brass means selection only, and a pad that is
// sounding lights in its own hue.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed partial class RackCardView
{
    private static int _drumView;          // 0 Pads, 1 Mixer, 2 Chain
    private static int _drumBank;          // 0..3 → C1..C4 (notes 36 + bank × 16)
    private static bool _drumFold;         // Pads: hide the empty pads

    private static readonly string[] DrumViews = { "Pads", "Mixer", "Chain" };
    private static readonly string[] BankNames = { "C1", "C2", "C3", "C4" };
    private static readonly string[] ChokeNames = { "Off", "1", "2", "3", "4" };
    private const double DrumPadW = 186, DrumTabH = 20, DrumStripH = 22, DrumStatusH = 18, MixRowH = 13;

    private static int BankBase => 36 + _drumBank * 16;
    private static bool InBank(int note) => note >= BankBase && note < BankBase + 16;

    // ---- entry point --------------------------------------------------------

    /// <summary>The Drum Rack card's body, for the shared shell. <paramref name="padCount"/>
    /// is the number of loaded pads, for the header badge.</summary>
    public Control BuildDrumRackBody(out int padCount)
    {
        var a = new InstrumentRackAccess(E, T);
        E.SetAuditionTrack(T);   // so clicking a pad plays it without arming the track
        _irTick.Clear();
        var pads = LoadedPads(a);
        padCount = pads.Count;
        if (!IsPad(a, Sel)) Sel = FirstPad(a, pads);

        var readout = DrumMono("", TextTertiary, NotaType.Axis);
        readout.Margin = new Thickness(0, 0, 8, 0);
        readout.HorizontalAlignment = HorizontalAlignment.Right;
        Control view = _drumView switch
        {
            1 => DrumMixer(a, readout),
            2 => DrumChain(a),
            _ => DrumGrid(a),
        };
        if (_drumView != 1)
        {
            int inBank = pads.Count(c => InBank(a.ChainTriggerNote(c)));
            readout.Text = $"{BankNames[_drumBank]} · {inBank}/16 loaded";
        }

        var left = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        left.Children.Add(DrumTabs(readout));
        var strip = DrumStrip(a);
        Grid.SetRow(strip, 1); left.Children.Add(strip);
        Grid.SetRow(view, 2); left.Children.Add(view);

        var panel = DrumBox(DrumPadPanel(a));
        panel.Width = DrumPadW;

        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = NotaSpace.DeviceGap,
            Margin = new Thickness(NotaSpace.DeviceGap),
        };
        body.Children.Add(DrumBox(left));
        Grid.SetColumn(panel, 1); body.Children.Add(panel);

        var statusHost = DrumStatus(a, pads);
        DockPanel.SetDock(statusHost, Dock.Bottom);
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.Gutter, Children = { statusHost, body } };

        if (_irTick.Count > 0) _ctx.AddDeviceRefresher(() => { for (int i = 0; i < _irTick.Count; i++) _irTick[i](); });
        return root;
    }

    // ---- tab strip (20): Pads · Mixer · Chain, and what the view shows ----------------

    private Control DrumTabs(TextBlock readout)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < DrumViews.Length; i++)
        {
            int iv = i; bool on = i == _drumView;
            var tb = new TextBlock
            {
                Text = DrumViews[i], FontSize = NotaType.DeviceSection, VerticalAlignment = VerticalAlignment.Center,
                Foreground = on ? AccentBright : TextTertiary, FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal,
            };
            var cell = new Border
            {
                Padding = new Thickness(8, 0), BorderThickness = new Thickness(0, 0, 0, 2),
                BorderBrush = on ? Brass : Brushes.Transparent, Background = on ? NotaPalette.SurfaceRaised : Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand), Child = tb,
            };
            cell.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed) return;
                e.Handled = true;
                if (_drumView != iv) { _drumView = iv; DeferRebuild(); }
            };
            row.Children.Add(cell);
        }
        var g = new Grid { Height = DrumTabH, ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        g.Children.Add(row);
        Grid.SetColumn(readout, 1); g.Children.Add(readout);
        return new Border { BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Child = g };
    }

    // ---- strip (22): bank · swing · humanize · fold ------------------------------------

    private Control DrumStrip(IRackAccess a)
    {
        var bank = Segments(BankNames, () => _drumBank, i =>
        {
            if (i == _drumBank) return;
            _drumBank = i;
            // Follow the bank with the selection, so the pad panel shows a pad you can see.
            int first = FirstPadInBank(a);
            if (first >= 0) Sel = first;
            DeferRebuild();
        }, out _, padX: 5);
        ToolTip.SetTip(bank, "Pad bank: sixteen notes from C1, C2, C3 or C4");

        var swing = SliderRow("SWING", () => E.RackSwing(T), v => E.RackSetSwing(T, (float)v),
            () => NotaNum.Pct(E.RackSwing(T)), out var swingSync, reset: () => E.RackSetSwing(T, 0f), valueWidth: 28);
        ToolTip.SetTip(swing, "Push the off-beat sixteenths late");
        var humanize = SliderRow("HUMANIZE", () => E.RackHumanize(T), v => E.RackSetHumanize(T, (float)v),
            () => NotaNum.Pct(E.RackHumanize(T)), out var humSync, reset: () => E.RackSetHumanize(T, 0f), valueWidth: 28);
        ToolTip.SetTip(humanize, "Nudge every hit's timing at random");
        _irTick.Add(swingSync); _irTick.Add(humSync);

        var fold = DrumButton("Fold", _drumFold, () => { _drumFold = !_drumFold; DeferRebuild(); });
        ToolTip.SetTip(fold, _drumFold ? "Show every pad of the bank" : "Hide the empty pads");
        if (_drumView != 0) fold.AttachedToVisualTree += (_, _) => Inactive.Set(fold, true);   // Fold is a Pads view setting

        var g = new Grid
        {
            Height = DrumStripH, ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,*,Auto"), ColumnSpacing = 8,
            Margin = new Thickness(7, 0),
        };
        g.Children.Add(DrumCap("BANK"));
        Grid.SetColumn(bank, 1); g.Children.Add(bank);
        Grid.SetColumn(swing, 2); g.Children.Add(swing);
        Grid.SetColumn(humanize, 3); g.Children.Add(humanize);
        Grid.SetColumn(fold, 4); g.Children.Add(fold);
        return new Border { BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 0, 0, 1), Child = g };
    }

    // ==================== Pads view ====================

    private Control DrumGrid(IRackAccess a)
    {
        // Hardware order: the bank's first note bottom-left, rising left → right, bottom → top.
        // Folded, the loaded pads close up in the same order.
        var notes = new List<int>();
        for (int n = BankBase; n < BankBase + 16; n++)
            if (!_drumFold || ChainForNote(a, n) >= 0) notes.Add(n);

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("*,*,*,*"), ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
            RowSpacing = 4, ColumnSpacing = 4, Margin = new Thickness(5),
        };
        for (int k = 0; k < notes.Count; k++)
        {
            var pad = DrumPad(a, notes[k]);
            Grid.SetRow(pad, 3 - k / 4); Grid.SetColumn(pad, k % 4);
            grid.Children.Add(pad);
        }
        if (notes.Count == 0)
        {
            var empty = DrumHint($"No pads in bank {BankNames[_drumBank]}.");
            Grid.SetRowSpan(empty, 4); Grid.SetColumnSpan(empty, 4);
            grid.Children.Add(empty);
        }
        return grid;
    }

    private Control DrumPad(IRackAccess a, int note)
    {
        int chain = ChainForNote(a, note);
        bool filled = chain >= 0, selected = filled && chain == Sel;
        int choke = filled ? E.RackChainChoke(T, chain) : 0;
        string name = filled ? PadName(a, chain) : "";

        var nameTb = new TextBlock
        {
            Text = name, FontSize = 8, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center,
            Foreground = selected ? AccentBright : TextPrimary, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 4 };
        if (filled) top.Children.Add(new Border { Width = 3, Height = 9, CornerRadius = NotaRadius.Bar, Background = PadHue(chain), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(nameTb, 1); top.Children.Add(nameTb);

        var noteTb = DrumMono(NoteName(note), selected ? AccentBright : filled ? TextTertiary : TextDisabled, 7);
        var chokeTb = DrumMono(choke > 0 ? $"CH {choke}" : "", NotaPalette.TealBright, 7);
        var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), VerticalAlignment = VerticalAlignment.Bottom };
        bottom.Children.Add(noteTb);
        Grid.SetColumn(chokeTb, 1); bottom.Children.Add(chokeTb);

        var cell = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(5, 3) };
        cell.Children.Add(top);
        Grid.SetRow(bottom, 2); cell.Children.Add(bottom);

        IBrush restBg = selected ? NotaPalette.AccentSubtle : filled ? NotaPalette.BgSunken : NotaPalette.SurfaceCard;
        IBrush restBorder = selected ? Brass : NotaPalette.GraphBorder;
        var pad = new Border
        {
            Background = restBg, BorderBrush = restBorder, BorderThickness = new Thickness(1),
            CornerRadius = NotaRadius.Badge, Cursor = new Cursor(StandardCursorType.Hand), ClipToBounds = true, Child = cell,
        };
        ToolTip.SetTip(pad, filled
            ? $"{name} · {NoteName(note)} — click to play, right-click for more"
            : $"{NoteName(note)} — click to add an instrument, or drop a sample here");

        if (filled)
        {
            // A sounding pad lights in its own hue; brass stays the selection.
            var hot = PadFill(PadHue(chain), 0.22);
            _irTick.Add(() => pad.Background = E.RackChainMeter(T, chain) > 0.02f ? hot : restBg);
            pad.PointerPressed += (_, e) =>
            {
                var pt = e.GetCurrentPoint(pad).Properties;
                if (pt.IsRightButtonPressed) { e.Handled = true; ShowPadMenu(a, pad, chain); return; }
                if (!pt.IsLeftButtonPressed) return;
                e.Handled = true; e.Pointer.Capture(pad); E.NoteOn(note, 1.0f);
            };
            pad.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Left) return;
                E.NoteOff(note); e.Pointer.Capture(null); SelectPad(chain);
            };
        }
        else
        {
            pad.PointerEntered += (_, _) => pad.BorderBrush = NotaPalette.BorderStrong;
            pad.PointerExited += (_, _) => pad.BorderBrush = restBorder;
            pad.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(pad).Properties.IsLeftButtonPressed) return;
                e.Handled = true; ShowAddPadMenu(a, pad, note);
            };
        }

        DragDrop.SetAllowDrop(pad, true);
        DragDrop.AddDragOverHandler(pad, (_, e) =>
        {
            if (!BrowserView.IsAcceptableDrag(e)) { e.DragEffects = DragDropEffects.None; return; }
            e.DragEffects = DragDropEffects.Copy; e.Handled = true; _ctx.HideDropGlow();
            pad.BorderBrush = AccentBright;   // the drop target: colour only, never a thicker edge
        });
        DragDrop.AddDragLeaveHandler(pad, (_, _) => pad.BorderBrush = restBorder);
        DragDrop.AddDropHandler(pad, (_, e) =>
        {
            pad.BorderBrush = restBorder;
            var items = BrowserView.DroppedItems(e);
            if (items.Count == 0) return;
            // Several samples fill this pad and the ones after it, up to the bank's last.
            for (int i = 0; i < items.Count && note + i < BankBase + 16; i++) DropOnPad(a, note + i, items[i]);
            e.Handled = true;
        });
        return pad;
    }

    // Right-click on a loaded pad: rename, choke group, its chain, remove. The pad is
    // selected too; the card rebuilds once the menu closes.
    private void ShowPadMenu(IRackAccess a, Control anchor, int chain)
    {
        Sel = chain;
        var f = new MenuFlyout();
        var rename = new MenuItem { Header = "Rename…" };
        rename.Click += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => PromptRenamePad(a, anchor, chain));
        f.Items.Add(rename);

        var chokeMenu = new MenuItem { Header = "Choke group" };
        int cur = E.RackChainChoke(T, chain);
        for (int g = 0; g < ChokeNames.Length; g++)
        {
            int gv = g;
            var mi = new MenuItem { Header = g == 0 ? "None" : $"Group {g}", ToggleType = MenuItemToggleType.Radio, IsChecked = g == cur };
            mi.Click += (_, _) => E.RackSetChainChoke(T, chain, gv);
            chokeMenu.Items.Add(mi);
        }
        f.Items.Add(chokeMenu);

        var open = new MenuItem { Header = "Open chain" };
        open.Click += (_, _) => _drumView = 2;
        f.Items.Add(open);
        f.Items.Add(new Separator());
        var remove = new MenuItem { Header = "Remove pad" };
        remove.Click += (_, _) => { a.RemoveChain(chain); _ctx.NotifyChanged(); };
        f.Items.Add(remove);

        bool renaming = false;
        rename.Click += (_, _) => renaming = true;
        f.Closed += (_, _) => { if (!renaming) DeferRebuild(); };
        f.ShowAt(anchor, showAtPointer: true);
    }

    private void PromptRenamePad(IRackAccess a, Control anchor, int chain)
    {
        var box = new TextBox { Text = PadName(a, chain), FontSize = 9, Width = 120, Height = 20, Padding = new Thickness(4, 0) };
        var fly = new Flyout { Content = box };
        void Commit()
        {
            string n = (box.Text ?? "").Trim();
            E.RackSetChainName(T, chain, n);   // empty falls back to the instrument's name
            _ctx.NotifyChanged();
        }
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Commit(); fly.Hide(); }
            else if (e.Key == Key.Escape) fly.Hide();
        };
        fly.Closed += (_, _) => DeferRebuild();
        fly.ShowAt(anchor);
        box.SelectAll();
        box.Focus();
    }

    // ==================== Mixer view ====================

    private Control DrumMixer(IRackAccess a, TextBlock readout)
    {
        var pads = LoadedPads(a).Where(c => InBank(a.ChainTriggerNote(c))).ToList();
        const string Cols = "86,26,*,26,48";

        var head = new Grid { ColumnDefinitions = new ColumnDefinitions(Cols), ColumnSpacing = 6, Height = MixRowH };
        void H(string t, int col, bool right = false)
        {
            var x = DrumCap(t); if (right) x.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(x, col); head.Children.Add(x);
        }
        H("PAD", 0); H("NOTE", 1); H("VOLUME", 2); H("PAN", 3); H("M S CH", 4, right: true);
        var headBar = new Border { BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 0, 0, 1), Child = head };

        var rows = new StackPanel { Spacing = 1, Margin = new Thickness(0, 2, 0, 0) };
        foreach (int c in pads) rows.Children.Add(DrumMixerRow(a, c, Cols));
        Control list;
        if (pads.Count == 0)
        {
            list = DrumHint($"No pads in bank {BankNames[_drumBank]} — drop samples on the Pads view.");
            readout.Text = $"{BankNames[_drumBank]} · empty";
        }
        else
        {
            var scroll = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            // The rows in view, e.g. "C1 · 1–12 of 16".
            void Visible()
            {
                double rowH = MixRowH + 1;
                int first = (int)Math.Floor(scroll.Offset.Y / rowH);
                int shown = Math.Max(1, (int)Math.Floor((scroll.Viewport.Height - 2) / rowH));
                int last = Math.Min(pads.Count, first + shown);
                readout.Text = last - first >= pads.Count
                    ? $"{BankNames[_drumBank]} · {pads.Count} pad{(pads.Count == 1 ? "" : "s")}"
                    : $"{BankNames[_drumBank]} · {first + 1}–{last} of {pads.Count}";
            }
            scroll.ScrollChanged += (_, _) => Visible();
            readout.Text = $"{BankNames[_drumBank]} · {pads.Count} pad{(pads.Count == 1 ? "" : "s")}";
            list = scroll;
        }
        DockPanel.SetDock(headBar, Dock.Top);
        return new DockPanel { LastChildFill = true, Margin = new Thickness(7, 0, 7, 4), Children = { headBar, list } };
    }

    private Control DrumMixerRow(IRackAccess a, int c, string cols)
    {
        int note = a.ChainTriggerNote(c);
        bool sel = c == Sel;
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions(cols), ColumnSpacing = 6, Height = MixRowH, Background = sel ? NotaPalette.AccentSubtle : Brushes.Transparent };

        var name = new TextBlock
        {
            Text = PadName(a, c), FontSize = 8, FontWeight = sel ? FontWeight.SemiBold : FontWeight.Medium,
            Foreground = sel ? AccentBright : TextPrimary, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
        };
        var nameCell = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 4, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand) };
        nameCell.Children.Add(new Border { Width = 3, Height = 8, CornerRadius = NotaRadius.Bar, Background = PadHue(c), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(name, 1); nameCell.Children.Add(name);
        nameCell.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(nameCell).Properties.IsLeftButtonPressed) return;
            e.Handled = true; SelectPad(c);
        };
        ToolTip.SetTip(nameCell, "Select the pad");
        g.Children.Add(nameCell);

        var noteTb = DrumMono(NoteName(note), sel ? AccentBright : TextTertiary, 7);
        Grid.SetColumn(noteTb, 1); g.Children.Add(noteTb);

        var vol = new SliderTrack { Norm = VolNorm(a.ChainGain(c)), Reset = () => a.SetChainGain(c, 1f) };
        vol.Changed += v => a.SetChainGain(c, NormVol(v));
        _irTick.Add(() => { if (!vol.Dragging) vol.Norm = VolNorm(a.ChainGain(c)); });
        ToolTip.SetTip(vol, "Pad volume · drag up or down, double-click for 0\u2009dB");
        MidiLearn.Bind(vol, MidiTarget.RackChainGain(T, a.AutomationDeviceIndex, c), "Pad Volume");
        Grid.SetColumn(vol, 2); g.Children.Add(vol);

        var pan = PanDrag(a, c);
        Grid.SetColumn(pan, 3); g.Children.Add(pan);

        int choke = E.RackChainChoke(T, c);
        var mute = MixBtn("M", () => a.ChainMute(c), NotaPalette.AccentHover, () => a.SetChainMute(c, !a.ChainMute(c)));
        var solo = MixBtn("S", () => a.ChainSolo(c), Brass, () => a.SetChainSolo(c, !a.ChainSolo(c)));
        var chk = MixBtn(choke > 0 ? choke.ToString() : "·", () => E.RackChainChoke(T, c) > 0, NotaPalette.Teal,
            () => { E.RackSetChainChoke(T, c, (E.RackChainChoke(T, c) + 1) % ChokeNames.Length); DeferRebuild(); }, choke: true);
        ToolTip.SetTip(mute, "Mute the pad"); ToolTip.SetTip(solo, "Solo the pad"); ToolTip.SetTip(chk, "Choke group: click for the next");
        MidiLearn.Bind(mute, MidiTarget.RackChainMute(T, a.AutomationDeviceIndex, c), "Pad Mute");
        MidiLearn.Bind(solo, MidiTarget.RackChainSolo(T, a.AutomationDeviceIndex, c), "Pad Solo");
        var msc = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Children = { mute, solo, chk } };
        Grid.SetColumn(msc, 4); g.Children.Add(msc);
        return g;
    }

    // Pan as a mono readout you drag: up is right, as on every value control; double-click centres.
    private Control PanDrag(IRackAccess a, int c)
    {
        var tb = DrumMono(PanText(a.ChainPan(c)), TextSecondary, 7);
        tb.HorizontalAlignment = HorizontalAlignment.Center;
        var host = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.SizeNorthSouth), Child = tb };
        ToolTip.SetTip(host, "Pad pan · drag up or down, double-click to centre");
        bool drag = false; double lastY = 0;
        host.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(host).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            if (e.ClickCount == 2) { a.SetChainPan(c, 0f); tb.Text = PanText(0f); return; }
            drag = true; lastY = e.GetPosition(host).Y; e.Pointer.Capture(host);
        };
        host.PointerMoved += (_, e) =>
        {
            if (!drag) return;
            double y = e.GetPosition(host).Y, dy = lastY - y; lastY = y;
            bool fine = (e.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Meta)) != 0;
            float v = (float)Math.Clamp(a.ChainPan(c) + dy / (fine ? 700.0 : 70.0), -1, 1);
            a.SetChainPan(c, v); tb.Text = PanText(v);
        };
        host.PointerReleased += (_, e) => { if (drag) { drag = false; e.Pointer.Capture(null); } };
        host.PointerCaptureLost += (_, _) => drag = false;
        _irTick.Add(() => { if (!drag) tb.Text = PanText(a.ChainPan(c)); });
        return host;
    }

    // A mixer switch, 13 × 11: raised when off; when on, filled with its colour (mute, solo)
    // or washed in teal (the choke group, which is a number, not a state).
    private Border MixBtn(string text, Func<bool> on, IBrush accent, Action toggle, bool choke = false)
    {
        var tb = new TextBlock { Text = text, FontSize = 7, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (choke) tb.FontFamily = NotaFonts.MonoFamily;
        var b = new Border { Width = 13, Height = 11, CornerRadius = NotaRadius.Clip, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), Child = tb };
        void Paint()
        {
            bool v = on();
            if (choke)
            {
                b.Background = v ? NotaPalette.Wash(NotaPalette.Teal, 0x33) : NotaPalette.BgSunken;
                b.BorderBrush = v ? accent : NotaPalette.GraphBorder;
                tb.Foreground = v ? NotaPalette.TealBright : TextDisabled;
            }
            else
            {
                b.Background = v ? accent : NotaPalette.SurfaceRaised;
                b.BorderBrush = v ? accent : BorderDef;
                tb.Foreground = v ? OnAccent : TextSecondary;
            }
        }
        b.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return;
            e.Handled = true; toggle(); Paint();
        };
        _irTick.Add(Paint);
        Paint();
        return b;
    }

    // ==================== Chain view ====================

    private Control DrumChain(IRackAccess a)
    {
        if (!IsPad(a, Sel)) return DrumHint("Select a pad to see its device chain.");
        int sc = Sel, note = a.ChainTriggerNote(sc);
        var head = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 0, 0, 6),
            Children =
            {
                new Border { Width = 6, Height = 6, CornerRadius = NotaRadius.Clip, Background = PadHue(sc), VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = PadName(a, sc), FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center },
                DrumMono(NoteName(note), TextTertiary, 7),
                new TextBlock { Text = "instrument, then its effects · signal left → right", FontSize = 8, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center },
            },
        };
        var slots = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        slots.Children.Add(IrInstrumentSlot(a, sc));
        for (int d = 0; d < a.ChainDeviceCount(sc); d++) slots.Children.Add(IrDeviceSlot(a, sc, d));
        slots.Children.Add(IrAddDeviceSlot(a, sc));
        var row = new ScrollViewer { Content = slots, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        DockPanel.SetDock(head, Dock.Top);
        return new DockPanel { LastChildFill = true, Margin = new Thickness(8, 6), Children = { head, row } };
    }

    // ==================== the selected pad (186) ====================

    private Control DrumPadPanel(IRackAccess a)
    {
        if (!IsPad(a, Sel))
        {
            var g0 = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
            g0.Children.Add(new Border
            {
                Height = DrumTabH, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(8, 0),
                Child = new TextBlock { Text = "No pad", FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center },
            });
            var hint = DrumHint("Drop a sample on a pad, or pick a kit above.");
            Grid.SetRow(hint, 1); g0.Children.Add(hint);
            return g0;
        }
        int c = Sel, note = a.ChainTriggerNote(c);
        var hue = PadHue(c);

        // header (20): dot · name · note · Open chain
        var link = new TextBlock { Text = _drumView == 2 ? "Back to pads" : "Open chain", FontSize = 7, Foreground = NotaPalette.TextMuted, VerticalAlignment = VerticalAlignment.Center };
        var linkHost = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = link };
        ToolTip.SetTip(linkHost, _drumView == 2 ? "Show the pads" : "Show the pad's instrument and effects");
        linkHost.PointerEntered += (_, _) => link.Foreground = TextPrimary;
        linkHost.PointerExited += (_, _) => link.Foreground = NotaPalette.TextMuted;
        linkHost.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(linkHost).Properties.IsLeftButtonPressed) return;
            e.Handled = true; _drumView = _drumView == 2 ? 0 : 2; DeferRebuild();
        };
        var head = new Grid { Height = DrumTabH, ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*"), ColumnSpacing = 5, Margin = new Thickness(8, 0) };
        head.Children.Add(new Border { Width = 6, Height = 6, CornerRadius = NotaRadius.Clip, Background = hue, VerticalAlignment = VerticalAlignment.Center });
        var nameTb = new TextBlock { Text = PadName(a, c), FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 92 };
        Grid.SetColumn(nameTb, 1); head.Children.Add(nameTb);
        var noteTb = DrumMono(NoteName(note), TextTertiary, 7);
        Grid.SetColumn(noteTb, 2); head.Children.Add(noteTb);
        linkHost.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(linkHost, 3); head.Children.Add(linkHost);
        var headBar = new Border { BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Child = head };

        // the one-shot (46)
        var wave = new PadWave { Hue = hue, TopInset = 12 };
        var srcTb = new TextBlock { FontSize = 7, FontWeight = FontWeight.SemiBold, Foreground = TextStrongInk, Margin = new Thickness(6, 3), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var durTb = DrumMono("", TextTertiary, 7);
        durTb.Margin = new Thickness(6, 3); durTb.HorizontalAlignment = HorizontalAlignment.Right; durTb.VerticalAlignment = VerticalAlignment.Top;
        var waveBox = new Panel { Height = 46, Children = { wave, srcTb, durTb } };
        srcTb.Text = a.ChainInstrumentName(c);
        if (E.RackChainSamplerInfo(T, c, out var si) && si.SampleId != 0 && E.TryGetSampleInfo(si.SampleId, out var sinfo))
        {
            wave.Peaks = MiniPeaks(E.ReadSample(si.SampleId), Math.Max(1, sinfo.Channels), sinfo.Frames, 96);
            durTb.Text = sinfo.SampleRate > 0 ? NotaNum.Time(sinfo.Frames / sinfo.SampleRate) : "";
            _irTick.Add(() => { float p = E.RackChainSamplerPlayPosition(T, c); wave.Play = p < 0 ? -1 : p; });
        }
        else
        {
            durTb.Text = a.ChainInstrumentKind(c) == 1 ? "empty" : "synth";
            var none = new TextBlock
            {
                Text = a.ChainInstrumentKind(c) == 1 ? "No sample — drop one on the pad" : "A synth voice, no sample",
                FontSize = 7, Foreground = TextTertiary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 12, 0, 0),
            };
            waveBox.Children.Add(none);
        }

        // volume · pan · tune · decay
        Grid Row(string label, Func<double> get, Action<double> set, Func<string> text, Action reset, bool bipolar = false)
        {
            var r = SliderRow(label, get, set, text, out var sync, reset: reset, bipolar: bipolar, labelWidth: 40, valueWidth: 38);
            _irTick.Add(sync);
            return r;
        }
        var volRow = Row("VOLUME", () => VolNorm(a.ChainGain(c)), v => a.SetChainGain(c, NormVol(v)), () => VolText(a.ChainGain(c)), () => a.SetChainGain(c, 1f));
        MidiLearn.Bind(volRow, MidiTarget.RackChainGain(T, a.AutomationDeviceIndex, c), "Pad Volume");
        var panRow = Row("PAN", () => (a.ChainPan(c) + 1) / 2, v => a.SetChainPan(c, (float)(v * 2 - 1)), () => PanText(a.ChainPan(c)), () => a.SetChainPan(c, 0f), bipolar: true);
        var tuneRow = Row("TUNE", () => (E.RackChainTune(T, c) + 48) / 96.0, v => E.RackSetChainTune(T, c, (int)Math.Round(v * 96 - 48)),
            () => NotaNum.Unit(E.RackChainTune(T, c), "+0;−0;0", "st"), () => E.RackSetChainTune(T, c, 0), bipolar: true);
        var decayRow = Row("DECAY", () => E.RackChainDecay(T, c), v => E.RackSetChainDecay(T, c, (float)v), () => DecayText(E.RackChainDecay(T, c)), () => E.RackSetChainDecay(T, c, 1f));
        ToolTip.SetTip(tuneRow, "Transpose the note the pad plays, in semitones");
        ToolTip.SetTip(decayRow, "Shorten the pad's tail; hold lets it ring");
        var sliders = new StackPanel { Spacing = 6, Children = { volRow, panRow, tuneRow, decayRow } };

        // choke · instrument
        var chokeSeg = Segments(ChokeNames, () => { int g = E.RackChainChoke(T, c); return g < ChokeNames.Length ? g : -1; },
            i => { E.RackSetChainChoke(T, c, i); DeferRebuild(); }, out var chokeSync, padX: 5);
        _irTick.Add(chokeSync);
        ToolTip.SetTip(chokeSeg, "Pads in the same group cut each other off");
        var chokeRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 6 };
        chokeRow.Children.Add(DrumCap("CHOKE"));
        Grid.SetColumn(chokeSeg, 1); chokeRow.Children.Add(chokeSeg);

        Border inst = null!;
        inst = DrumWideButton(InstrumentWord(a, c), () => OpenChainInstrumentGui(a, c, inst));
        ToolTip.SetTip(inst, "Open the pad's instrument");
        var foot = new StackPanel { Spacing = 5, Children = { chokeRow, inst } };
        var footer = new Border { BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 5, 0, 0), VerticalAlignment = VerticalAlignment.Bottom, Child = foot };

        var col = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), RowSpacing = 6, Margin = new Thickness(8, 6) };
        col.Children.Add(waveBox);
        Grid.SetRow(sliders, 1); col.Children.Add(sliders);
        Grid.SetRow(footer, 2); col.Children.Add(footer);

        DockPanel.SetDock(headBar, Dock.Top);
        return new DockPanel { LastChildFill = true, Children = { headBar, col } };
    }

    // The instrument button's word: what the pad plays, without the maker's prefix.
    private static string InstrumentWord(IRackAccess a, int c)
    {
        string n = a.ChainInstrumentName(c);
        if (string.IsNullOrEmpty(n)) return "Instrument";
        return n.StartsWith("Nota ", StringComparison.Ordinal) ? n[5..] : n;
    }

    // ---- status strip (18) -----------------------------------------------------------

    private Control DrumStatus(IRackAccess a, List<int> pads)
    {
        var status = new TextBlock { FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var meta = DrumMono("", TextSecondary, 8);
        int groups = pads.Select(c => E.RackChainChoke(T, c)).Where(g => g > 0).Distinct().Count();
        meta.Text = $"{pads.Count} PAD{(pads.Count == 1 ? "" : "S")} · {groups} CHOKE GROUP{(groups == 1 ? "" : "S")}"
            + (E.SampleRate > 0 ? " · " + NotaNum.Unit(E.SampleRate / 1000.0, "0.#", "kHz") : "");

        string Timing() => $"swing {NotaNum.Pct(E.RackSwing(T))} · humanize {NotaNum.Pct(E.RackHumanize(T))}";
        string PadWords(int c)
        {
            string src = E.RackChainSamplerInfo(T, c, out var si) && si.SampleId != 0 && E.TryGetSampleInfo(si.SampleId, out var inf) && inf.SampleRate > 0
                ? $"sampler {NotaNum.Time(inf.Frames / inf.SampleRate)}"
                : InstrumentWord(a, c).ToLowerInvariant();
            int g = E.RackChainChoke(T, c);
            return $"{PadName(a, c)} {NoteName(a.ChainTriggerNote(c))} selected · {src} · choke {(g > 0 ? g.ToString() : "off")}";
        }
        string Summary()
        {
            string bank = $"Bank {BankNames[_drumBank]}";
            if (_drumView == 1)
            {
                var inBank = pads.Where(c => InBank(a.ChainTriggerNote(c))).ToList();
                var chokes = inBank.GroupBy(c => E.RackChainChoke(T, c)).Where(gr => gr.Key > 0).OrderBy(gr => gr.Key)
                    .Select(gr => $"choke group {gr.Key}: {string.Join(", ", gr.Select(c => PadName(a, c)))}").ToList();
                return $"{bank} · {inBank.Count} pad{(inBank.Count == 1 ? "" : "s")}" + (chokes.Count > 0 ? " · " + string.Join(" · ", chokes) : " · no choke groups");
            }
            if (!IsPad(a, Sel)) return $"{bank} · no pad selected · {Timing()}";
            if (_drumView == 2)
            {
                int d = a.ChainDeviceCount(Sel);
                return $"{PadName(a, Sel)} {NoteName(a.ChainTriggerNote(Sel))} · {a.ChainInstrumentName(Sel)} → {d} effect{(d == 1 ? "" : "s")} → pad volume, pan";
            }
            return $"{bank} · {PadWords(Sel)} · {Timing()}";
        }
        status.Text = Summary();
        _irTick.Add(() => status.Text = Summary());

        var g = new Grid { Height = DrumStatusH, ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        g.Children.Add(status);
        Grid.SetColumn(meta, 1); g.Children.Add(meta);
        return new Border
        {
            Height = DrumStatusH, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 1, 0, 0),
            Background = NotaPalette.SurfaceAbyss, Padding = new Thickness(8, 0), Child = g,
        };
    }

    // ---- pads: lookup, selection, loading ----------------------------------------------

    private static int ChainForNote(IRackAccess a, int note)
    {
        for (int c = 0; c < a.ChainCount(); c++) if (a.ChainTriggerNote(c) == note) return c;
        return -1;
    }

    private static bool IsPad(IRackAccess a, int c) => c >= 0 && c < a.ChainCount() && a.ChainTriggerNote(c) >= 0;

    // Loaded pads, lowest note first.
    private static List<int> LoadedPads(IRackAccess a)
    {
        var list = new List<int>();
        for (int c = 0; c < a.ChainCount(); c++) if (a.ChainTriggerNote(c) >= 0) list.Add(c);
        list.Sort((x, y) => a.ChainTriggerNote(x).CompareTo(a.ChainTriggerNote(y)));
        return list;
    }

    private static int FirstPadInBank(IRackAccess a)
    {
        foreach (int c in LoadedPads(a)) if (InBank(a.ChainTriggerNote(c))) return c;
        return -1;
    }

    private static int FirstPad(IRackAccess a, List<int> pads)
    {
        int inBank = FirstPadInBank(a);
        return inBank >= 0 ? inBank : pads.Count > 0 ? pads[0] : -1;
    }

    // A pad's display name: the chain name if set, else the chain instrument's.
    private string PadName(IRackAccess a, int chain)
    {
        string n = E.RackChainName(T, chain);
        return n.Length > 0 ? n : a.ChainInstrumentName(chain);
    }

    // Select a pad and show it (rebuilds only when the selection changes).
    private void SelectPad(int chain)
    {
        if (chain < 0 || chain == Sel) return;
        Sel = chain;
        DeferRebuild();
    }

    // A browser item dropped on a specific pad: sample → a Sampler on this note; instrument
    // → that instrument on this note. Either replaces what the pad held.
    private void DropOnPad(IRackAccess a, int note, Nota.Presentation.BrowserItem item)
    {
        int existing = ChainForNote(a, note);
        int added;
        switch (item.Kind)
        {
            case Nota.Presentation.BrowserItemKind.Sample:
                if (existing >= 0) a.RemoveChain(existing);
                added = E.RackAddSamplerChain(T, item.Path, note, false);
                if (added >= 0)
                {
                    E.RackSetChainTriggerNote(T, added, note);
                    // Name the pad after the sample — sixteen pads reading "Nota Sampler" say nothing.
                    E.RackSetChainName(T, added, System.IO.Path.GetFileNameWithoutExtension(item.Path));
                }
                break;
            case Nota.Presentation.BrowserItemKind.BuiltinInstrument:
                if (existing >= 0) a.RemoveChain(existing);
                added = a.AddChain(item.BuiltinKind);
                if (added >= 0) a.SetChainTriggerNote(added, note);
                break;
            case Nota.Presentation.BrowserItemKind.PluginInstrument:
                if (existing >= 0) a.RemoveChain(existing);
                added = E.RackAddPluginInstrumentChain(T, item.CatalogIndex);
                if (added >= 0) E.RackSetChainTriggerNote(T, added, note);
                break;
            default:
                return;   // effects and presets aren't pad content
        }
        if (added >= 0) Sel = added;
        _ctx.NotifyChanged();
        DeferRebuild();
    }

    private void ShowAddPadMenu(IRackAccess a, Control anchor, int note)
    {
        var f = new MenuFlyout();
        void Add(string header, int kind)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) =>
            {
                int c = a.AddChain(kind);
                if (c >= 0) { a.SetChainTriggerNote(c, note); Sel = c; }
                _ctx.NotifyChanged();
                DeferRebuild();
            };
            f.Items.Add(mi);
        }
        Add("Nota Sampler", 1);
        f.Items.Add(new Separator());
        Add("Nota Synth", 0);
        Add("Nota Physical", 2);
        Add("Nota Aurora", 5);
        Add("Nota Volt", 6);
        Add("Nota Bass", 7);
        Add("Nota Pendulum", 8);
        Add("Nota Operator", 9);
        Add("Nota Grain", 10);
        Add("Nota Flux", 11);
        Add("Nota Monolith", 13);
        Add("Nota Pentad", 14);
        Add("Nota Consort", 15);
        f.ShowAt(anchor, showAtPointer: true);
    }

    // ---- value mapping ---------------------------------------------------------------

    // Pad hues are the track palette slots themselves, so a pad re-tints with the theme.
    private static SolidColorBrush PadHue(int chain) => NotaPalette.TrackBrushes[((chain % 8) + 8) % 8];
    private static IBrush PadFill(SolidColorBrush c, double alpha) => NotaPalette.Wash(c, (byte)(Math.Clamp(alpha, 0, 1) * 255));
    // Volume: −60 … +6 dB over the slider, silence at the bottom.
    private static double VolNorm(float gain) { double db = gain <= 0.001 ? -60 : 20 * Math.Log10(gain); return Math.Clamp((db + 60) / 66.0, 0, 1); }
    private static float NormVol(double v) { double db = -60 + v * 66; return v <= 0 ? 0f : (float)Math.Pow(10, db / 20); }
    private static string VolText(float gain) => NotaNum.Db(gain <= 0.001 ? double.NegativeInfinity : 20 * Math.Log10(gain), signed: true, floor: -59.5);
    private static string PanText(float pan) { int p = (int)Math.Round(pan * 50); return p == 0 ? "C" : (p < 0 ? $"{-p}L" : $"{p}R"); }
    // The engine's pad decay: 20 ms … 2 s, and "hold" (no shaping) at the top.
    private static string DecayText(float d) => d >= 0.999f ? "hold" : NotaNum.Time(0.02 * Math.Pow(100, d));

    private static float[] MiniPeaks(float[] raw, int ch, long frames, int buckets)
    {
        var o = new float[buckets];
        if (frames <= 0 || raw.Length == 0) return o;
        long per = Math.Max(1, frames / buckets);
        for (int b = 0; b < buckets; b++)
        {
            long s = (long)b * per; float mx = 0;
            for (long i = 0; i < per; i++)
            {
                long f = s + i; if (f >= frames || (f + 1) * ch > raw.Length) break;
                float v = 0; for (int cc = 0; cc < ch; cc++) v += Math.Abs(raw[f * ch + cc]);
                v /= ch; if (v > mx) mx = v;
            }
            o[b] = mx;
        }
        float peak = 0; foreach (float v in o) peak = Math.Max(peak, v);
        if (peak > 0) for (int b = 0; b < buckets; b++) o[b] /= peak;
        return o;
    }

    // ---- small builders --------------------------------------------------------------

    private static readonly IBrush TextStrongInk = NotaPalette.TextStrong;

    // A device section: card ground, hairline, radius 6.
    private static Border DrumBox(Control child) => new()
    {
        Background = NotaPalette.SurfaceCard, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
        CornerRadius = NotaRadius.Tile, ClipToBounds = true, Child = child,
    };

    private static TextBlock DrumCap(string t) => new()
    {
        Text = t, FontSize = NotaType.KnobLabel, FontWeight = FontWeight.Bold, LetterSpacing = NotaType.KnobLabelTracking,
        Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center,
    };

    private static TextBlock DrumMono(string t, IBrush ink, double fs) => new()
    {
        Text = t, FontSize = fs, Foreground = ink, FontFamily = NotaFonts.MonoFamily, VerticalAlignment = VerticalAlignment.Center,
    };

    // An empty state: one Ink 5 line, centred.
    private static TextBlock DrumHint(string t) => new()
    {
        Text = t, FontSize = 8, Foreground = TextTertiary, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0),
    };

    // A small raised toggle (Fold): brass edge and Brass Light text when on.
    private static Border DrumButton(string text, bool on, Action click)
    {
        var tb = new TextBlock { Text = text, FontSize = 7, FontWeight = FontWeight.SemiBold, Foreground = on ? AccentBright : TextStrongInk, VerticalAlignment = VerticalAlignment.Center };
        var b = new Border
        {
            Height = 14, Padding = new Thickness(6, 0), CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1),
            Background = on ? NotaPalette.AccentSubtle : NotaPalette.SurfaceRaised, BorderBrush = on ? Brass : BorderDef,
            Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = tb,
        };
        b.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return;
            e.Handled = true; click();
        };
        return b;
    }

    // A full-width raised button (the pad's instrument).
    private static Border DrumWideButton(string text, Action click)
    {
        var tb = new TextBlock { Text = text, FontSize = 7, FontWeight = FontWeight.SemiBold, Foreground = TextStrongInk, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var b = new Border
        {
            Height = 16, CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1),
            Background = NotaPalette.SurfaceRaised, BorderBrush = BorderDef, Cursor = new Cursor(StandardCursorType.Hand), Child = tb,
        };
        b.PointerEntered += (_, _) => b.Background = NotaPalette.SurfaceHover;
        b.PointerExited += (_, _) => b.Background = NotaPalette.SurfaceRaised;
        b.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return;
            e.Handled = true; click();
        };
        return b;
    }
}
