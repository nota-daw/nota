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
//             Macro — the kit's eight macros (MacroPanel): a knob each, the name, and where
//                     it goes; a kit loads with its own (KitMacros), mapped onto the pads'
//                     controls and their effects.
//   Pad 190   the selected pad: its one-shot, volume · pan · tune · decay, the choke
//             group, and its instrument's editor. In Macro, the selected macro instead: its
//             receivers with their ranges, and Map · Rename · Clear.
// Both sit in pressed-in sections (style guide); there is no status line.
//
// A pad's colour is its track-palette slot; accent means selection only, and a pad that is
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
    private static int _drumView;          // 0 Pads, 1 Mixer, 2 Chain, 3 Macro
    private static int _drumMacro;         // Macro: the selected macro, 0..7
    private static int _drumBank;          // 0..3 → C1..C4 (notes 36 + bank × 16)
    private static bool _drumFold;         // Pads: hide the empty pads

    private static readonly string[] DrumViews = { "Pads", "Mixer", "Chain", "Macro" };
    private static readonly string[] BankNames = { "C1", "C2", "C3", "C4" };
    private static readonly string[] ChokeNames = { "Off", "1", "2", "3", "4" };
    private const double DrumPadW = 190, DrumTabH = 20, MixRowH = 20;

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

        var readout = DrumMono("", TextSecondary, NotaType.KnobValue);
        readout.HorizontalAlignment = HorizontalAlignment.Right;
        var macros = new DrumMacroSurface(this, a);
        Control view = _drumView switch
        {
            1 => DrumMixer(a, readout),
            2 => DrumChain(a),
            3 => MacroPanel.Grid(_ctx, macros, _drumMacro, m => { _drumMacro = m; DeferRebuild(); }, _irTick, stacked: true),
            _ => DrumGrid(a),
        };
        if (_drumView == 3)
            readout.Text = $"{MacroPanel.Count} slots · {MacroPanel.Assigned(macros)} assigned";
        else if (_drumView != 1)
        {
            int inBank = pads.Count(c => InBank(a.ChainTriggerNote(c)));
            readout.Text = $"{BankNames[_drumBank]} · {inBank}/16 loaded";
        }

        var left = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        left.Children.Add(DrumTabs(readout));
        var strip = DrumStrip(a);
        Grid.SetRow(strip, 1); left.Children.Add(strip);
        Grid.SetRow(view, 2); left.Children.Add(view);

        var panel = DeviceBodyKit.Sec(_drumView == 3 ? MacroPanel.Detail(_ctx, macros, _drumMacro, _irTick) : DrumPadPanel(a), new Thickness(12, 10), DrumPadW);

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = SectionGap };
        body.Children.Add(DeviceBodyKit.Sec(left, new Thickness(12, 10, 12, 12)));
        Grid.SetColumn(panel, 1); body.Children.Add(panel);
        var root = DeviceBodyKit.Frame(body);

        if (_irTick.Count > 0) _ctx.AddDeviceRefresher(() => { for (int i = 0; i < _irTick.Count; i++) _irTick[i](); });
        return root;
    }

    // ---- tab row: Pads · Mixer · Chain · Macro, and what the view shows ----------------

    private Control DrumTabs(TextBlock readout)
    {
        var tabs = DeviceBodyKit.Tabs(DrumViews, () => _drumView, i => { if (_drumView != i) { _drumView = i; DeferRebuild(); } }, out _, readout);
        tabs.Margin = new Thickness(0, 0, 0, 6);
        return tabs;
    }

    // ---- strip: bank · swing · humanize · fold, on one row --------------------------------

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
        }, out _, padX: 4);
        ToolTip.SetTip(bank, "Pad bank: sixteen notes from C1, C2, C3 or C4");

        var swing = SliderRow("Swing", () => E.RackSwing(T), v => E.RackSetSwing(T, (float)v),
            () => NotaNum.Pct(E.RackSwing(T)), out var swingSync, reset: () => E.RackSetSwing(T, 0f), valueWidth: 30);
        ToolTip.SetTip(swing, "Push the off-beat sixteenths late");
        var humanize = SliderRow("Humanize", () => E.RackHumanize(T), v => E.RackSetHumanize(T, (float)v),
            () => NotaNum.Pct(E.RackHumanize(T)), out var humSync, reset: () => E.RackSetHumanize(T, 0f), valueWidth: 30);
        ToolTip.SetTip(humanize, "Nudge every hit's timing at random");
        _irTick.Add(swingSync); _irTick.Add(humSync);

        var fold = DrumButton("Fold", _drumFold, () => { _drumFold = !_drumFold; DeferRebuild(); });
        ToolTip.SetTip(fold, _drumFold ? "Show every pad of the bank" : "Hide the empty pads");
        if (_drumView != 0) fold.AttachedToVisualTree += (_, _) => Inactive.Set(fold, true);   // Fold is a Pads view setting

        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,*,Auto"), ColumnSpacing = 8, Margin = new Thickness(0, 0, 0, 6) };
        g.Children.Add(DrumCap("Bank"));
        Grid.SetColumn(bank, 1); g.Children.Add(bank);
        Grid.SetColumn(swing, 2); g.Children.Add(swing);
        Grid.SetColumn(humanize, 3); g.Children.Add(humanize);
        Grid.SetColumn(fold, 4); g.Children.Add(fold);
        return g;
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
            RowSpacing = 4, ColumnSpacing = 4,
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
            Text = name, FontSize = NotaType.KnobValue, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center,
            Foreground = selected ? AccentBright : TextPrimary, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 6 };
        if (filled) top.Children.Add(new Border { Width = 3, Height = 11, CornerRadius = NotaRadius.Fine, Background = PadHue(chain), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(nameTb, 1); top.Children.Add(nameTb);

        var noteTb = DrumMono(NoteName(note), selected ? AccentBright : filled ? TextTertiary : TextDisabled, 7);
        var chokeTb = DrumMono(choke > 0 ? $"Ch {choke}" : "", TextTertiary, 7);
        var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), VerticalAlignment = VerticalAlignment.Bottom };
        bottom.Children.Add(noteTb);
        Grid.SetColumn(chokeTb, 1); bottom.Children.Add(chokeTb);

        // Name pinned to the top, note to the bottom: a short pad lets the line boxes meet.
        top.VerticalAlignment = VerticalAlignment.Top;
        nameTb.LineHeight = 13; noteTb.LineHeight = 12; chokeTb.LineHeight = 12;   // 25: the boxes meet without overlapping
        var cell = new Panel { Margin = new Thickness(8, 0, 8, 1), Children = { top, bottom } };

        // A loaded pad is raised (soft orange when selected); an empty one is pressed in.
        IBrush restBg = selected ? NotaPalette.AccentSoft : filled ? NotaPalette.SurfaceHover : NotaPalette.Groove;
        IBrush restBorder = Brushes.Transparent;
        var pad = new Border
        {
            Background = restBg, BorderBrush = restBorder, BorderThickness = new Thickness(1),
            CornerRadius = NotaRadius.Field, Cursor = new Cursor(StandardCursorType.Hand), Child = cell,
        };
        if (filled) pad.BindResource(Border.BoxShadowProperty, "Shadow.Lifted");
        ToolTip.SetTip(pad, filled
            ? $"{name} · {NoteName(note)} — click to play, right-click for more"
            : $"{NoteName(note)} — click to add an instrument, or drop a sample here");

        if (filled)
        {
            // A sounding pad lights in its own hue; accent stays the selection.
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
            // Several samples fill this pad and the ones after it, up to the bank's last; the
            // whole drop is one undo step (#236).
            E.BeginUndoGroup();
            try { for (int i = 0; i < items.Count && note + i < BankBase + 16; i++) DropOnPad(a, note + i, items[i]); }
            finally { E.EndUndoGroup(); }
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
        f.Items.Add(MenuKit.Header(PadName(a, chain), null, "Pad"));
        f.Items.Add(new Separator());
        var rename = MenuKit.Item("Rename…", GlyphKind.Edit);
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
        f.Items.Add(MenuKit.Danger("Remove pad", GlyphKind.Trash, () => { a.RemoveChain(chain); _ctx.NotifyChanged(); }));

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
        const string Cols = "92,30,*,30,64";

        // Head and rows keep 8 px on the right for the scroll bar, so it never covers the ch column.
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions(Cols), ColumnSpacing = 6, Height = MixRowH, Margin = new Thickness(0, 0, 8, 0) };
        void H(string t, int col, bool right = false)
        {
            var x = DrumCap(t); if (right) x.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(x, col); head.Children.Add(x);
        }
        H("Pad", 0); H("Note", 1); H("Volume", 2); H("Pan", 3); H("M S ch", 4, right: true);
        var headBar = new Border { BorderBrush = NotaPalette.Rule, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, 2), Child = head };

        var rows = new StackPanel { Spacing = 2, Margin = new Thickness(0, 4, 8, 0) };
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
                double rowH = MixRowH + 2;
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
        return new DockPanel { LastChildFill = true, Children = { headBar, list } };
    }

    private Control DrumMixerRow(IRackAccess a, int c, string cols)
    {
        int note = a.ChainTriggerNote(c);
        bool sel = c == Sel;
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions(cols), ColumnSpacing = 6, Height = MixRowH, Background = sel ? NotaPalette.AccentSoft : Brushes.Transparent };

        var name = new TextBlock
        {
            Text = PadName(a, c), FontSize = NotaType.KnobValue, FontWeight = sel ? FontWeight.SemiBold : FontWeight.Normal, Margin = new Thickness(2, 0, 0, 0),
            Foreground = sel ? AccentBright : TextPrimary, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
        };
        var nameCell = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 4, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand) };
        nameCell.Children.Add(new Border { Width = 3, Height = 11, Margin = new Thickness(4, 0, 0, 0), CornerRadius = NotaRadius.Fine, Background = PadHue(c), VerticalAlignment = VerticalAlignment.Center });
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
        ToolTip.SetTip(vol, "Pad volume · drag up or down, ⌥-click to type, double-click for 0\u2009dB");
        ValueEntry.Attach(vol, new ValueField("Pad volume", () => VolNorm(a.ChainGain(c)), n => VolText(NormVol(n)),
            n => { vol.Norm = n; a.SetChainGain(c, NormVol(n)); }, Default: VolNorm(1f)));
        MidiLearn.Bind(vol, MidiTarget.RackChainGain(T, a.AutomationDeviceIndex, c), "Pad Volume");
        Grid.SetColumn(vol, 2); g.Children.Add(vol);

        var pan = PanDrag(a, c);
        Grid.SetColumn(pan, 3); g.Children.Add(pan);

        int choke = E.RackChainChoke(T, c);
        var mute = MixBtn("M", () => a.ChainMute(c), NotaPalette.Mute, () => a.SetChainMute(c, !a.ChainMute(c)));
        var solo = MixBtn("S", () => a.ChainSolo(c), NotaPalette.Solo, () => a.SetChainSolo(c, !a.ChainSolo(c)));
        var chk = MixBtn(choke > 0 ? choke.ToString() : "·", () => E.RackChainChoke(T, c) > 0, NotaPalette.TextSecondary,
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
        var tb = DrumMono(PanText(a.ChainPan(c)), TextPrimary, 9);
        tb.HorizontalAlignment = HorizontalAlignment.Center;
        var host = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.SizeNorthSouth), Child = tb };
        ToolTip.SetTip(host, "Pad pan · drag up or down, double-click to centre");
        bool drag = false; double lastY = 0;
        host.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(host).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            if ((e.ClickCount == 2 || (e.KeyModifiers & KeyModifiers.Alt) != 0)) { a.SetChainPan(c, 0f); tb.Text = PanText(0f); return; }
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

    // A mixer switch, 16 × 16: raised when off; when on, filled with its colour (mute, solo).
    // The choke group is a number, not a state: pressed in, ink 1 when set, ink 4 when not.
    private Border MixBtn(string text, Func<bool> on, SolidColorBrush accent, Action toggle, bool choke = false)
    {
        var tb = new TextBlock { Text = text, FontSize = NotaType.RowLabel, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (choke) tb.FontFamily = NotaFonts.UiFamily;
        var b = new Border { Width = 18, Height = 16, CornerRadius = NotaRadius.Field, Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = tb };
        if (!choke) b.BindResource(Border.BoxShadowProperty, "Shadow.Lifted");
        void Paint()
        {
            bool v = on();
            if (choke)
            {
                b.Background = NotaPalette.Groove;
                tb.Foreground = v ? TextPrimary : TextDisabled;
            }
            else
            {
                b.Background = v ? accent : NotaPalette.SurfaceHover;
                tb.Foreground = v ? NotaPalette.InkOn(accent) : TextSecondary;
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
        var head = IrChainHead(PadName(a, sc), PadHue(sc), $"{NoteName(note)} · instrument, then its effects");
        var slots = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        slots.Children.Add(IrInstrumentSlot(a, sc));
        for (int d = 0; d < a.ChainDeviceCount(sc); d++) slots.Children.Add(SgDeviceSlot(a, sc, d));
        slots.Children.Add(SgAddDeviceSlot(a, sc));
        var row = new ScrollViewer { Content = slots, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        DockPanel.SetDock(head, Dock.Top);
        return new DockPanel { LastChildFill = true, Children = { head, row } };
    }

    // ==================== the selected pad (190) ====================

    private Control DrumPadPanel(IRackAccess a)
    {
        if (!IsPad(a, Sel))
        {
            var g0 = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
            g0.Children.Add(new Border { Height = DrumTabH, Child = DeviceBodyKit.Title("No pad", TextTertiary) });
            var hint = DrumHint("Drop a sample on a pad, or pick a kit above.");
            Grid.SetRow(hint, 1); g0.Children.Add(hint);
            return g0;
        }
        int c = Sel, note = a.ChainTriggerNote(c);
        var hue = PadHue(c);

        // header (20): dot · name · note · Open chain
        var link = new TextBlock { Text = _drumView == 2 ? "Back to pads" : "Open chain", FontSize = NotaType.RowLabel, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center };
        var linkHost = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = link };
        ToolTip.SetTip(linkHost, _drumView == 2 ? "Show the pads" : "Show the pad's instrument and effects");
        linkHost.PointerEntered += (_, _) => link.Foreground = TextPrimary;
        linkHost.PointerExited += (_, _) => link.Foreground = TextSecondary;
        linkHost.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(linkHost).Properties.IsLeftButtonPressed) return;
            e.Handled = true; _drumView = _drumView == 2 ? 0 : 2; DeferRebuild();
        };
        var head = new Grid { Height = 16, ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*"), ColumnSpacing = 6, Margin = new Thickness(0, 0, 0, 4) };
        head.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = NotaRadius.Fine, Background = hue, VerticalAlignment = VerticalAlignment.Center });
        var nameTb = new TextBlock { Text = PadName(a, c), FontSize = NotaType.Name, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 92 };
        Grid.SetColumn(nameTb, 1); head.Children.Add(nameTb);
        var noteTb = DrumMono(NoteName(note), TextTertiary, 7);
        Grid.SetColumn(noteTb, 2); head.Children.Add(noteTb);
        linkHost.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(linkHost, 3); head.Children.Add(linkHost);
        var headBar = head;

        // the one-shot (40)
        var wave = new PadWave { Hue = hue, TopInset = 14 };
        var srcTb = new TextBlock { FontSize = NotaType.RowLabel, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary, Margin = new Thickness(6, 2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var durTb = DrumMono("", TextTertiary, 7);
        durTb.Margin = new Thickness(6, 2); durTb.HorizontalAlignment = HorizontalAlignment.Right; durTb.VerticalAlignment = VerticalAlignment.Top;
        var waveBox = new Panel { Height = 40, Children = { wave, srcTb, durTb } };
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
                FontSize = NotaType.RowLabel, Foreground = TextTertiary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 12, 0, 0),
            };
            waveBox.Children.Add(none);
        }

        // volume · pan · tune · decay
        Grid Row(string label, Func<double> get, Action<double> set, Func<string> text, Action reset, bool bipolar = false)
        {
            var r = SliderRow(label, get, set, text, out var sync, reset: reset, bipolar: bipolar, labelWidth: 44, valueWidth: 40);
            _irTick.Add(sync);
            return r;
        }
        var volRow = Row("Volume", () => VolNorm(a.ChainGain(c)), v => a.SetChainGain(c, NormVol(v)), () => VolText(a.ChainGain(c)), () => a.SetChainGain(c, 1f));
        MidiLearn.Bind(volRow, MidiTarget.RackChainGain(T, a.AutomationDeviceIndex, c), "Pad Volume");
        var panRow = Row("Pan", () => (a.ChainPan(c) + 1) / 2, v => a.SetChainPan(c, (float)(v * 2 - 1)), () => PanText(a.ChainPan(c)), () => a.SetChainPan(c, 0f), bipolar: true);
        var tuneRow = Row("Tune", () => (E.RackChainTune(T, c) + 48) / 96.0, v => E.RackSetChainTune(T, c, (int)Math.Round(v * 96 - 48)),
            () => NotaNum.Unit(E.RackChainTune(T, c), "+0;−0;0", "st"), () => E.RackSetChainTune(T, c, 0), bipolar: true);
        var decayRow = Row("Decay", () => E.RackChainDecay(T, c), v => E.RackSetChainDecay(T, c, (float)v), () => DecayText(E.RackChainDecay(T, c)), () => E.RackSetChainDecay(T, c, 1f));
        ToolTip.SetTip(tuneRow, "Transpose the note the pad plays, in semitones");
        ToolTip.SetTip(decayRow, "Shorten the pad's tail; hold lets it ring");
        var sliders = new StackPanel { Spacing = 3, Children = { volRow, panRow, tuneRow, decayRow } };

        // choke · instrument
        var chokeSeg = Segments(ChokeNames, () => { int g = E.RackChainChoke(T, c); return g < ChokeNames.Length ? g : -1; },
            i => { E.RackSetChainChoke(T, c, i); DeferRebuild(); }, out var chokeSync, padX: 4);
        _irTick.Add(chokeSync);
        ToolTip.SetTip(chokeSeg, "Pads in the same group cut each other off");
        var chokeRow = new Grid { ColumnDefinitions = new ColumnDefinitions("44,Auto"), ColumnSpacing = 8 };
        chokeRow.Children.Add(DrumCap("Choke"));
        Grid.SetColumn(chokeSeg, 1); chokeRow.Children.Add(chokeSeg);

        Border inst = null!;
        inst = DrumWideButton(InstrumentWord(a, c), () => OpenChainInstrumentGui(a, c, inst));
        ToolTip.SetTip(inst, "Open the pad's instrument");
        var foot = new StackPanel { Spacing = 3, Children = { DeviceBodyKit.Rule(), chokeRow, inst } };
        ((Control)foot.Children[0]).Margin = new Thickness(0, 0, 0, 1);
        var footer = new Border { VerticalAlignment = VerticalAlignment.Bottom, Child = foot };

        var col = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), RowSpacing = 4 };
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
        // Remove + add + note + name is one gesture: one undo step (#236).
        E.BeginUndoGroup();
        try { DropOnPadSteps(a, note, item); }
        finally { E.EndUndoGroup(); }
    }

    private void DropOnPadSteps(IRackAccess a, int note, Nota.Presentation.BrowserItem item)
    {
        int existing = ChainForNote(a, note);
        int added;
        switch (item.Kind)
        {
            case Nota.Presentation.BrowserItemKind.Sample:
                // An occupied pad keeps its chain (#237): level, pan, mute/solo, sends, choke and
                // effects stay, only the sound changes. A Sampler keeps its tune, envelope and
                // filter; another instrument gives way to a fresh Sampler.
                if (existing >= 0 && E.RackSetChainSamplerSample(T, existing, item.Path, note))
                {
                    added = existing;
                    ResetSampleWindow(existing);
                }
                else
                {
                    if (existing >= 0) a.RemoveChain(existing);
                    added = E.RackAddSamplerChain(T, item.Path, note, false);
                    if (added >= 0) E.RackSetChainTriggerNote(T, added, note);
                }
                // Name the pad after the sample — sixteen pads reading "Nota Sampler" say nothing.
                if (added >= 0) E.RackSetChainName(T, added, System.IO.Path.GetFileNameWithoutExtension(item.Path));
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

    // Start / end, reverse and the loop window belong to the sample that was on the pad: a new
    // sample starts from their defaults.
    private static readonly string[] SampleWindowParams = { "start", "end", "reverse", "loopstart", "loopend", "loopxfade" };

    private void ResetSampleWindow(int chain)
    {
        int n = E.RackChainInstrumentParamCount(T, chain);
        for (int p = 0; p < n; p++)
            if (Array.IndexOf(SampleWindowParams, E.RackChainInstrumentParamId(T, chain, p)) >= 0)
                E.RackChainInstrumentParamSet(T, chain, p, E.RackChainInstrumentParamDefault(T, chain, p));
    }

    private void ShowAddPadMenu(IRackAccess a, Control anchor, int note)
    {
        var f = new MenuFlyout();
        void Add(string header, int kind)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) =>
            {
                E.BeginUndoGroup();   // the chain and its note: one undo step
                int c = a.AddChain(kind);
                if (c >= 0) { a.SetChainTriggerNote(c, note); Sel = c; }
                E.EndUndoGroup();
                _ctx.NotifyChanged();
                DeferRebuild();
            };
            f.Items.Add(mi);
        }
        Add("Nota Sampler", 1);
        f.Items.Add(new Separator());
        Add("Nota Volt", 6);
        Add("Nota Physical", 2);
        Add("Nota Aurora", 5);
        Add("Nota Pendulum", 8);
        Add("Nota Operator", 9);
        Add("Nota Grain", 10);
        Add("Nota Flux", 11);
        Add("Nota Monolith", 13);
        Add("Nota Pentad", 14);
        Add("Nota Consort", 15);
        Add("Nota Keys", 16);
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

    private static TextBlock DrumCap(string t) => new()
    {
        Text = NotaText.Label(t), FontSize = NotaType.RowLabel,
        Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center,
    };

    // The old 7 / 8 sizes land on the device scale: 10.5 a label or note, 11 a value.
    private static TextBlock DrumMono(string t, IBrush ink, double fs) => new()
    {
        Text = t, FontSize = fs < 8 ? NotaType.RowLabel : NotaType.KnobValue, Foreground = ink, FontFamily = NotaFonts.UiFamily, VerticalAlignment = VerticalAlignment.Center,
    };

    // An empty state: one Ink 5 line, centred.
    private static TextBlock DrumHint(string t) => new()
    {
        Text = t, FontSize = NotaType.KnobValue, Foreground = TextTertiary, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0),
    };

    // A raised toggle (Fold): soft orange with bright orange text when on.
    private static Border DrumButton(string text, bool on, Action click)
    {
        var b = DeviceCardKit.TextButton(text);
        var tb = (TextBlock)b.Child!;
        b.Height = 24; b.Padding = new Thickness(12, 0); b.VerticalAlignment = VerticalAlignment.Center;
        tb.VerticalAlignment = VerticalAlignment.Center;
        if (on) { b.Background = NotaPalette.AccentSoft; tb.Foreground = AccentBright; tb.FontWeight = FontWeight.SemiBold; }
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
        var b = DeviceCardKit.TextButton(text);
        var tb = (TextBlock)b.Child!;
        b.Height = 22; b.Padding = new Thickness(8, 0); b.HorizontalAlignment = HorizontalAlignment.Stretch;
        tb.HorizontalAlignment = HorizontalAlignment.Center; tb.VerticalAlignment = VerticalAlignment.Center; tb.TextTrimming = TextTrimming.CharacterEllipsis;
        b.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return;
            e.Handled = true; click();
        };
        return b;
    }

    // ---- the kit macros (MacroPanel), over the rack's macros ------------------------------

    // The rack's eight macros ride its plugin params (index = macro). A pad is a slot; its
    // targets are its own controls (volume · pan · tune · decay), its instrument's params and
    // its effects' params.
    private sealed class DrumMacroSurface(RackCardView v, IRackAccess a) : IMacroSurface
    {
        private IAudioEngine E => v.E;
        public int TrackId => v.T;
        public int AutomationDevice => a.AutomationDeviceIndex;
        public int MacroParamIndex(int m) => m;
        public string MacroName(int m) => a.MacroName(m);
        public void SetMacroName(int m, string n) => a.SetMacroName(m, n);
        public string SlotNoun => "pads";
        public IReadOnlyList<int> Slots() => LoadedPads(a);
        public string SlotName(int c) => v.PadName(a, c);
        public IBrush SlotHue(int c) => PadHue(c);
        public int MappingCount() => a.MappingCount();
        public bool TryGetMapping(int i, out RackMacroMapping m) => a.TryGetMapping(i, out m);
        public int AddMapping(int m, int c, int d, int p, float lo, float hi) => a.AddMacroMapping(m, c, d, p, lo, hi);
        public bool RemoveMapping(int i) => a.RemoveMapping(i);
        public bool SetMappingRange(int i, float lo, float hi) => a.SetMappingRange(i, lo, hi);

        public (string Param, string Device) TargetLabel(RackMacroMapping m) => m.DeviceIndex switch
        {
            RackMacroMapping.PadControls => (PadParam(m.ParamIndex), ""),
            < 0 => (a.ChainInstrumentParamName(m.Chain, m.ParamIndex), InstrumentWord(a, m.Chain)),
            _ => (a.ChainDeviceParamName(m.Chain, m.DeviceIndex, m.ParamIndex), a.ChainDeviceName(m.Chain, m.DeviceIndex)),
        };
        public (float Min, float Max) TargetRange(RackMacroMapping m) => m.DeviceIndex switch
        {
            // Tune's ±48 st would squeeze a musical range into a sliver: the bar shows ±24.
            RackMacroMapping.PadControls => m.ParamIndex == RackMacroMapping.PadTune ? (-24f, 24f) : PadRange(m.ParamIndex),
            < 0 => (0f, 1f),
            _ => (a.ChainDeviceParamMin(m.Chain, m.DeviceIndex, m.ParamIndex), a.ChainDeviceParamMax(m.Chain, m.DeviceIndex, m.ParamIndex)),
        };
        public float TargetValue(RackMacroMapping m) => m.DeviceIndex switch
        {
            RackMacroMapping.PadControls => m.ParamIndex switch
            {
                RackMacroMapping.PadVolume => a.ChainGain(m.Chain),
                RackMacroMapping.PadPan => a.ChainPan(m.Chain),
                RackMacroMapping.PadTune => E.RackChainTune(TrackId, m.Chain),
                _ => E.RackChainDecay(TrackId, m.Chain),
            },
            < 0 => a.ChainInstrumentParamGet(m.Chain, m.ParamIndex),
            _ => a.ChainDeviceParamGet(m.Chain, m.DeviceIndex, m.ParamIndex),
        };
        public string FormatTarget(RackMacroMapping m, float x)
        {
            if (m.DeviceIndex >= 0) { var (lo, hi) = TargetRange(m); return MacroPanel.FormatDeviceParam(lo, hi, x); }
            if (m.DeviceIndex != RackMacroMapping.PadControls) return $"{Math.Round(x * 100):0}";
            return m.ParamIndex switch
            {
                RackMacroMapping.PadVolume => x <= 0.001f ? "−∞" : (20 * Math.Log10(x)).ToString("+0;−0;0", NotaNum.Culture),
                RackMacroMapping.PadPan => PanText(x),
                RackMacroMapping.PadTune => Math.Round(x).ToString("+0;−0;0", NotaNum.Culture),
                _ => $"{Math.Round(x * 100):0}",
            };
        }
        public string TargetUnit(RackMacroMapping m)
        {
            if (m.DeviceIndex >= 0) { var (lo, hi) = TargetRange(m); return MacroPanel.DeviceParamUnit(lo, hi); }
            if (m.DeviceIndex != RackMacroMapping.PadControls) return "%";
            return m.ParamIndex switch { RackMacroMapping.PadVolume => "dB", RackMacroMapping.PadPan => "", RackMacroMapping.PadTune => "st", _ => "%" };
        }

        private static string PadParam(int p) => p >= 0 && p < RackMacroMapping.PadParamNames.Length ? RackMacroMapping.PadParamNames[p] : "?";
        private static (float, float) PadRange(int p) => p >= 0 && p < 4 ? (RackMacroMapping.PadParamMin[p], RackMacroMapping.PadParamMax[p]) : (0f, 1f);
        // What a new pad-control mapping sweeps: a musical span rather than the whole control.
        private static (float, float) PadDefault(int p) => p switch
        {
            RackMacroMapping.PadVolume => (0f, 1f),
            RackMacroMapping.PadPan => (-1f, 1f),
            RackMacroMapping.PadTune => (-12f, 12f),
            _ => (0f, 1f),
        };

        public void FillMapMenu(MenuFlyout menu, int macro, Action changed)
        {
            var pads = LoadedPads(a);
            if (pads.Count == 0) { menu.Items.Add(new MenuItem { Header = "No pads to map", IsEnabled = false }); return; }

            // Every pad at once: its own controls, or the same effect on each pad that has it.
            var all = new MenuItem { Header = $"All {pads.Count} pads" };
            for (int p = 0; p < 4; p++)
            {
                int pp = p;
                var (lo, hi) = PadDefault(p);
                var mi = new MenuItem { Header = PadParam(p) };
                mi.Click += (_, _) => { foreach (int c in pads) MacroPanel.Map(this, macro, c, RackMacroMapping.PadControls, pp, lo, hi); changed(); };
                all.Items.Add(mi);
            }
            var kinds = new List<(int Kind, string Name, int Chain, int Dev)>();
            foreach (int c in pads)
                for (int d = 0; d < a.ChainDeviceCount(c); d++)
                {
                    int k = a.ChainDeviceBuiltinKind(c, d);
                    if (k >= 0 && kinds.All(x => x.Kind != k)) kinds.Add((k, a.ChainDeviceName(c, d), c, d));
                }
            if (kinds.Count > 0) all.Items.Add(new Separator());
            foreach (var (kind, dname, c0, d0) in kinds)
            {
                // The pads holding this effect; each maps its first one.
                var holders = pads.Select(c => (c, d: FirstOfKind(c, kind))).Where(x => x.d >= 0).ToList();
                var fx = new MenuItem { Header = $"Every {dname} ({holders.Count} pad{(holders.Count == 1 ? "" : "s")})" };
                for (int p = 0; p < a.ChainDeviceParamCount(c0, d0); p++)
                {
                    int pp = p;
                    var mi = new MenuItem { Header = a.ChainDeviceParamName(c0, d0, p) };
                    mi.Click += (_, _) =>
                    {
                        foreach (var (c, d) in holders)
                            MacroPanel.Map(this, macro, c, d, pp, a.ChainDeviceParamMin(c, d, pp), a.ChainDeviceParamMax(c, d, pp));
                        changed();
                    };
                    fx.Items.Add(mi);
                }
                all.Items.Add(fx);
            }
            menu.Items.Add(all);
            menu.Items.Add(new Separator());

            // One pad: its controls, its instrument, each of its effects.
            foreach (int c in pads)
            {
                var pad = new MenuItem { Header = $"{v.PadName(a, c)}  ·  {NoteName(a.ChainTriggerNote(c))}" };
                var ctl = new MenuItem { Header = "Pad" };
                for (int p = 0; p < 4; p++)
                {
                    var (lo, hi) = PadDefault(p);
                    ctl.Items.Add(MacroPanel.Target(this, PadParam(p), macro, c, RackMacroMapping.PadControls, p, lo, hi, changed,
                        MacroPanel.IsMapped(this, macro, c, RackMacroMapping.PadControls, p)));
                }
                pad.Items.Add(ctl);
                int ipc = a.ChainInstrumentParamCount(c);
                if (ipc > 0)
                {
                    var inst = new MenuItem { Header = InstrumentWord(a, c) };
                    for (int p = 0; p < ipc; p++)
                        inst.Items.Add(MacroPanel.Target(this, a.ChainInstrumentParamName(c, p), macro, c, -1, p, 0f, 1f, changed,
                            MacroPanel.IsMapped(this, macro, c, -1, p)));
                    pad.Items.Add(inst);
                }
                for (int d = 0; d < a.ChainDeviceCount(c); d++)
                {
                    int dpc = a.ChainDeviceParamCount(c, d);
                    if (dpc <= 0) continue;
                    var dev = new MenuItem { Header = a.ChainDeviceName(c, d) };
                    for (int p = 0; p < dpc; p++)
                        dev.Items.Add(MacroPanel.Target(this, a.ChainDeviceParamName(c, d, p), macro, c, d, p,
                            a.ChainDeviceParamMin(c, d, p), a.ChainDeviceParamMax(c, d, p), changed, MacroPanel.IsMapped(this, macro, c, d, p)));
                    pad.Items.Add(dev);
                }
                menu.Items.Add(pad);
            }
        }

        private int FirstOfKind(int c, int kind)
        {
            for (int d = 0; d < a.ChainDeviceCount(c); d++) if (a.ChainDeviceBuiltinKind(c, d) == kind) return d;
            return -1;
        }
    }
}
