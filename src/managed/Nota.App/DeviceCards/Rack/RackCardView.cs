// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the rack cards, shared by the Instrument Rack (track instrument,
// kind 3), the Audio Effect Rack (a device, builtin kind 5) and the Drum Rack (kind 4).
// All present the same body: a 4×2 grid of macro faders on the left, a chain list (or a
// 4×4 pad grid for the drum rack) in the middle, and the selected chain's device chain
// on the right. The two rack addressings differ only via IRackAccess. Built per-rebuild
// with a DeviceCardContext; all mutable state (selected chain, refreshers, drop glow,
// rebuild) lives in the view behind the context.

using System;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed class RackCardView(DeviceCardContext ctx)
{
    private static readonly string[] RackDeviceNames = { "Nota EQ-3", "Nota EQ-8", "Nota Dynamic EQ-8", "Nota Compressor", "Nota Prism", "Nota Reverb", "Nota Chamber", "Nota Delay", "Nota Utility", "Nota Level", "Nota Ceiling", "Nota Shutter", "Nota Valve", "Nota Auto Filter", "Nota Vintage", "Nota Forge", "Nota Crush", "Nota Orbit", "Nota Auto Shift", "Nota Beat Repeat", "Nota Strata" };
    private static readonly int[] RackDeviceKinds = { 16, 0, 13, 1, 21, 2, 20, 3, 4, 18, 14, 19, 6, 7, 8, 17, 12, 9, 10, 11, 15 };
    private static readonly IBrush RowSel = new SolidColorBrush(Color.FromArgb(0x22, 0xD8, 0xA0, 0x3D)); // selected chain wash

    private readonly DeviceCardContext _ctx = ctx;
    private IAudioEngine E => _ctx.Engine;
    private int T => _ctx.TrackId;
    private int Sel { get => _ctx.SelectedChain; set => _ctx.SelectedChain = value; }

    // ---- Instrument Rack (mockup 2p) shared bits ----
    private static readonly IBrush IrHdr = new SolidColorBrush(Color.Parse("#1E1C18"));
    private static readonly IBrush IrRail = new SolidColorBrush(Color.Parse("#1B1916"));
    private static readonly IBrush IrInset = new SolidColorBrush(Color.Parse("#100F0D"));
    private static readonly IBrush IrBorder = new SolidColorBrush(Color.Parse("#2C2923"));
    private static readonly IBrush IrField = new SolidColorBrush(Color.Parse("#221F1A"));
    private static readonly IBrush IrCard = new SolidColorBrush(Color.Parse("#26231E"));
    private static readonly IBrush IrAmber = new SolidColorBrush(Color.Parse("#D8A03D"));
    private static readonly IBrush IrAmberLit = new SolidColorBrush(Color.Parse("#F0C060"));
    private static readonly IBrush IrTeal = new SolidColorBrush(Color.Parse("#5B9E9C"));
    private static readonly IBrush IrTxt = new SolidColorBrush(Color.Parse("#E9E4D8"));
    private static readonly IBrush IrMuted = new SolidColorBrush(Color.Parse("#6E6A5E"));
    private static readonly IBrush IrRed = new SolidColorBrush(Color.Parse("#D95F4C"));
    private static bool _macroMapMode;     // Instrument Rack: second (Macro-map) view
    private static int _splitAxis;         // 0 = Key zone, 1 = Velocity
    private static bool _drumMixer;        // Drum Rack: false = Pads view, true = Mixer view
    private static int _drumBank;          // Drum Rack: current pad bank 0..3 (C1..C4 → notes 36+bank*16)
    private static bool _drumFold;         // Drum Rack: hide empty pads in the grid
    private static bool _aeMacroMap;       // Audio Effect Rack: macro-map view
    private static bool _aeFold;           // Audio Effect Rack: fold device cards to titles
    private readonly System.Collections.Generic.List<Action> _irTick = new();

    // Rebuild on the NEXT dispatcher cycle, never synchronously inside a pointer handler:
    // tearing down the clicked element mid-dispatch corrupts pointer state (a click would
    // "work once" then stop routing). Structural edits from clicks go through this.
    private void DeferRebuild() => Avalonia.Threading.Dispatcher.UIThread.Post(_ctx.RequestRebuild);

    private static IBrush ChainHue(int c) => new SolidColorBrush(NotaPalette.TrackColors[((c % 8) + 8) % 8]);
    private static TextBlock IrMono(string t, IBrush c, double fs = 8) { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.Mono"); return tb; }
    private static TextBlock IrCap(string t, IBrush? c = null) => new() { Text = t, FontSize = 8, FontWeight = FontWeight.Bold, Foreground = c ?? IrMuted, VerticalAlignment = VerticalAlignment.Center };

    // ---- entry points -----------------------------------------------------

    // Instrument Rack card (the track's instrument), rebuilt to mockup 2p (700×260).
    public Control BuildInstrumentRackCard()
    {
        var a = new InstrumentRackAccess(E, T);
        int chains = a.ChainCount();
        Sel = chains > 0 ? Math.Clamp(Sel, 0, chains - 1) : 0;
        _irTick.Clear();
        Control content = _macroMapMode ? InstrumentRackMacroMap(a) : InstrumentRackMain(a);
        if (_irTick.Count > 0) _ctx.AddDeviceRefresher(() => { for (int i = 0; i < _irTick.Count; i++) _irTick[i](); });
        return new Border { Width = 700, Height = 260, Background = new SolidColorBrush(Color.Parse("#171613")), BorderBrush = IrBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = content };
    }

    private Control InstrumentRackMain(IRackAccess a)
    {
        var strip = IrMacroStrip(a);
        var chainList = IrChainList(a);
        var rail = IrRackRail(a);
        DockPanel.SetDock(chainList, Dock.Left); DockPanel.SetDock(rail, Dock.Right);
        var body = new DockPanel { LastChildFill = true, Children = { chainList, rail, IrDeviceArea(a) } };
        var header = IrHeader(a, "MAIN");
        DockPanel.SetDock(header, Dock.Top); DockPanel.SetDock(strip, Dock.Top);
        return new DockPanel { LastChildFill = true, Children = { header, strip, body } };
    }

    // ---- header (26) ----
    private Control IrHeader(IRackAccess a, string mode)
    {
        int chains = a.ChainCount();
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = {
            new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = Success, VerticalAlignment = VerticalAlignment.Center },
            new TextBlock { Text = "Nota Instrument Rack", FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = IrTxt, VerticalAlignment = VerticalAlignment.Center },
            new TextBlock { Text = $"{chains} CHAIN{(chains == 1 ? "" : "S")}", FontSize = 9, FontWeight = FontWeight.Bold, Foreground = IrMuted, VerticalAlignment = VerticalAlignment.Center } } };
        // Little stereo meter fed by the loudest chain.
        var mL = new Border { Height = 3, Background = Success, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(2) };
        var mR = new Border { Height = 3, Background = Success, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(2) };
        var mBg1 = new Panel { Height = 3, Children = { new Border { Height = 3, Background = IrInset, CornerRadius = new CornerRadius(2) }, mL } };
        var mBg2 = new Panel { Height = 3, Children = { new Border { Height = 3, Background = IrInset, CornerRadius = new CornerRadius(2) }, mR } };
        var meter = new StackPanel { Width = 46, Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { mBg1, mBg2 } };
        _irTick.Add(() => { float pk = 0; for (int c = 0; c < a.ChainCount(); c++) pk = Math.Max(pk, E.RackChainMeter(T, c)); double w = 46 * Math.Clamp(pk, 0, 1); mL.Width = w; mR.Width = w * 0.94; });
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, [DockPanel.DockProperty] = Dock.Right, Children = { meter } };
        var dp = new DockPanel { LastChildFill = false, Margin = new Thickness(9, 0), Children = { left, right } };
        return new Border { Height = 26, Background = IrHdr, BorderBrush = IrBorder, BorderThickness = new Thickness(0, 0, 0, 1), Child = dp };
    }

    // ---- macro strip (34): 8 named horizontal macro sliders ----
    private Control IrMacroStrip(IRackAccess a)
    {
        var grid = new Grid { VerticalAlignment = VerticalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        var lbl = IrCap("MACROS"); Grid.SetColumn(lbl, 0); grid.Children.Add(lbl);
        for (int i = 0; i < 8; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            var cell = IrMacroSlider(a, i); Grid.SetColumn(cell, i + 1); grid.Children.Add(cell);
        }
        grid.ColumnSpacing = 6;
        return new Border { Height = 34, Background = IrHdr, BorderBrush = IrBorder, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(9, 0), Child = grid };
    }

    private Control IrMacroSlider(IRackAccess a, int i)
    {
        int adi = a.AutomationDeviceIndex;
        bool mapped = false;
        for (int m = 0; m < a.MappingCount(); m++) if (a.TryGetMapping(m, out var mm) && mm.Macro == i) { mapped = true; break; }
        var name = new TextBlock { Text = a.MacroName(i), FontSize = 8, Foreground = mapped ? IrTxt : IrMuted, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var val = IrMono("0", IrMuted, 8); val.TextAlignment = TextAlignment.Right;
        var fill = new Border { Height = 3, Background = mapped ? IrAmber : new SolidColorBrush(Color.Parse("#4A463D")), CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var handle = new Border { Width = 6, Height = 8, Background = new SolidColorBrush(Color.Parse("#A39D8F")), CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var slot = new Panel { Height = 10, Children = { new Border { Height = 3, Background = IrInset, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center }, fill, handle } };
        void Vis(double v) { double W = slot.Bounds.Width; fill.Width = v * W; handle.Margin = new Thickness(Math.Clamp(v * W - 3, 0, Math.Max(0, W - 6)), 0, 0, 0); }
        bool drag = false;
        void SetFromX(double x) { double v = Math.Clamp(x / Math.Max(1, slot.Bounds.Width), 0, 1); E.PluginParamSet(T, adi, i, (float)v); Vis(v); _ctx.InvokeRackParamRefreshers(); }
        slot.PointerPressed += (_, e) => { drag = true; e.Pointer.Capture(slot); SetFromX(e.GetPosition(slot).X); };
        slot.PointerMoved += (_, e) => { if (drag) SetFromX(e.GetPosition(slot).X); };
        slot.PointerReleased += (_, e) => { if (drag) { drag = false; e.Pointer.Capture(null); } };
        name.DoubleTapped += (_, _) => PromptRenameMacro(a, i, name);
        _irTick.Add(() => { double v = E.PluginParamGet(T, adi, i); if (!drag) Vis(v); val.Text = $"{v * 100:0}"; });
        var head = new DockPanel { LastChildFill = true, Children = { WithRight(val), name } };
        var cell = new StackPanel { Spacing = 2, Children = { head, slot } };
        // MIDI-learn: a macro is a plugin-param on the rack device (adi = automation device
        // index). Tag the cell so the learn overlay highlights + binds it like any knob.
        MidiLearn.Bind(cell, MidiTarget.PluginParam(T, adi, i), $"Macro · {a.MacroName(i)}", v => Vis(v));
        return cell;
    }

    private void PromptRenameMacro(IRackAccess a, int i, TextBlock label)
    {
        var box = new TextBox { Text = a.MacroName(i), FontSize = 9, Width = 90, Height = 20, Padding = new Thickness(4, 0) };
        var fly = new Flyout { Content = box };
        box.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) { a.SetMacroName(i, box.Text ?? ""); label.Text = a.MacroName(i); fly.Hide(); _ctx.RequestRebuild(); } };
        fly.ShowAt(label);
        box.Focus();
    }

    // ---- chain list (186) ----
    private Control IrChainList(IRackAccess a)
    {
        var rows = new StackPanel { Spacing = 4 };
        int chains = a.ChainCount();
        for (int c = 0; c < chains; c++) rows.Children.Add(IrChainRow(a, c));
        var addBtn = new Border { BorderBrush = new SolidColorBrush(Color.Parse("#3A362D")), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(0, 3),
            Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = "+ Add chain", FontSize = 9, Foreground = IrMuted, HorizontalAlignment = HorizontalAlignment.Center } };
        addBtn.PointerPressed += (_, _) => ShowAddChainMenu(a, addBtn);
        rows.Children.Add(addBtn);

        var header = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 7), Children = { IrCap("CHAINS"), WithRight(new TextBlock { Text = "zone · vel · out", FontSize = 8, Foreground = IrMuted, VerticalAlignment = VerticalAlignment.Center }) } };
        var split = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = { IrCap("SPLIT"), IrSeg(new[] { "Key", "Vel" }, _splitAxis, i => { _splitAxis = i; _ctx.RequestRebuild(); }) } };
        var scroll = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 6, 0) };
        DockPanel.SetDock(header, Dock.Top); DockPanel.SetDock(split, Dock.Bottom);
        var inner = new DockPanel { LastChildFill = true, Children = { header, split, scroll } };
        return new Border { Width = 186, Background = IrRail, BorderBrush = IrBorder, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(8, 6), Child = inner };
    }

    private Control IrChainRow(IRackAccess a, int c)
    {
        bool sel = c == Sel;
        string name = a.ChainInstrumentName(c); if (string.IsNullOrEmpty(name)) name = $"Chain {c + 1}";
        int devs = a.ChainDeviceCount(c);
        E.RackChainZone(T, c, out int kLo, out int kHi, out int vLo, out int vHi);
        var nameTb = new TextBlock { Text = name, FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = sel ? IrAmberLit : IrTxt, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var head = new DockPanel { LastChildFill = true, Children = { WithRight(ChainDelBtn(a, c)), WithRight(IrMono($"{devs} dev", IrMuted)), nameTb } };

        var zone = IrMono($"{NoteName(kLo)}–{NoteName(kHi)}", new SolidColorBrush(Color.Parse("#A39D8F")));
        var vel = IrMono($"{vLo}–{vHi}", IrMuted);
        double db = a.ChainGain(c) <= 0.001 ? -60 : 20 * Math.Log10(a.ChainGain(c));
        var dbT = IrMono(db <= -59 ? "−∞" : $"{db:+0.0;−0.0;0.0}", new SolidColorBrush(Color.Parse("#A39D8F")));
        var meterFill = new Border { Background = Success, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(1) };
        var meter = new Border { Width = 20, Height = 3, Background = IrInset, CornerRadius = new CornerRadius(1), Child = meterFill, VerticalAlignment = VerticalAlignment.Center };
        _irTick.Add(() => meterFill.Width = 20 * Math.Clamp(E.RackChainMeter(T, c), 0, 1));
        var m = IrMsBtn("M", a.ChainMute(c), IrRed, () => { a.SetChainMute(c, !a.ChainMute(c)); _ctx.RequestRebuild(); });
        var s = IrMsBtn("S", a.ChainSolo(c), IrAmber, () => { a.SetChainSolo(c, !a.ChainSolo(c)); _ctx.RequestRebuild(); });
        MidiLearn.Bind(m, MidiTarget.RackChainMute(T, a.AutomationDeviceIndex, c), "Chain Mute");
        MidiLearn.Bind(s, MidiTarget.RackChainSolo(T, a.AutomationDeviceIndex, c), "Chain Solo");
        var info = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Children = { zone, vel, WithRight(dbT), meter, m, s } };
        DockPanel.SetDock(dbT, Dock.Right);

        var row = new Border { Background = sel ? RowSel : IrCard, BorderBrush = sel ? IrAmber : IrBorder, BorderThickness = new Thickness(1, 1, 1, 1), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 4), Cursor = new Cursor(StandardCursorType.Hand),
            Child = new StackPanel { Spacing = 3, Children = { head, info } } };
        row.Background = sel ? RowSel : IrCard;
        // left accent = chain hue, hidden while selected (the amber selection is enough).
        var accent = new Border { Width = 2, Background = sel ? Brushes.Transparent : ChainHue(c), CornerRadius = new CornerRadius(2, 0, 0, 2) };
        var wrap = new DockPanel { LastChildFill = true, Children = { accent, row } };
        DockPanel.SetDock(accent, Dock.Left);
        wrap.PointerPressed += (_, e) => { if (e.GetCurrentPoint(wrap).Properties.IsLeftButtonPressed && Sel != c) { Sel = c; _ctx.RequestRebuild(); } };
        DragDrop.SetAllowDrop(wrap, true);
        DragDrop.AddDragOverHandler(wrap, (_, e) => { if (!BrowserView.IsBrowserDrag || !IsEffect(BrowserView.CurrentDrag)) { e.DragEffects = DragDropEffects.None; return; } e.DragEffects = DragDropEffects.Copy; e.Handled = true; _ctx.HideDropGlow(); row.BorderBrush = AccentBright; });
        DragDrop.AddDragLeaveHandler(wrap, (_, _) => row.BorderBrush = sel ? IrAmber : IrBorder);
        DragDrop.AddDropHandler(wrap, (_, e) => { row.BorderBrush = sel ? IrAmber : IrBorder; if (BrowserView.CurrentDrag is { } it && IsEffect(it)) { Sel = c; DropEffectOnChain(a, c, it); e.Handled = true; } });
        return wrap;
    }

    // ---- device area (center): device slots + key/velocity zone map ----
    private Control IrDeviceArea(IRackAccess a)
    {
        if (a.ChainCount() == 0)
            return new Border { Padding = new Thickness(9, 7), Child = new TextBlock { Text = "Add a chain to build its device chain.", FontSize = 10, Foreground = IrMuted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        int sc = Sel;
        string inm = a.ChainInstrumentName(sc); if (string.IsNullOrEmpty(inm)) inm = "Instrument";
        var hdr = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 7), Children = {
            new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(2), Background = ChainHue(sc), VerticalAlignment = VerticalAlignment.Center },
            new TextBlock { Text = inm, FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = IrTxt, VerticalAlignment = VerticalAlignment.Center },
            new TextBlock { Text = "chain devices · signal left → right", FontSize = 8, Foreground = IrMuted, VerticalAlignment = VerticalAlignment.Center } } };
        var slots = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        if (a.HasInstrumentChains) slots.Children.Add(IrInstrumentSlot(a, sc));
        int dc = a.ChainDeviceCount(sc);
        for (int d = 0; d < dc; d++) slots.Children.Add(IrDeviceSlot(a, sc, d));
        slots.Children.Add(IrAddDeviceSlot(a, sc));
        var row = new ScrollViewer { Content = slots, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var inner = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(hdr, Dock.Top); inner.Children.Add(hdr);
        if (!_foldZone) { var zm = IrZoneMap(a); DockPanel.SetDock(zm, Dock.Bottom); inner.Children.Add(zm); }
        inner.Children.Add(row);
        return new Border { Padding = new Thickness(8, 6), Child = inner };
    }

    // A minimal device "slot" — name + type + two open buttons (full GUI / param knobs)
    // + macro/bypass footer.
    private Border IrSlot(IBrush dot, string name, string kind, bool dim, Action openGui, Action openParams, Control footerLeft, Action? onDelete = null)
    {
        var head = new DockPanel { LastChildFill = true, Height = 18, Children = {
            new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = dot, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), [DockPanel.DockProperty] = Dock.Left },
            WithRight(new TextBlock { Text = kind, FontSize = 7, FontWeight = FontWeight.Bold, Foreground = IrMuted, VerticalAlignment = VerticalAlignment.Center }),
            new TextBlock { Text = name, FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = IrTxt, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis } } };
        if (onDelete != null) head.Children.Insert(1, WithRight(SlotDelBtn(onDelete)));
        var headWrap = new Border { BorderBrush = IrBorder, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(6, 0), Child = head };
        Border MiniBtn(string label, Action act)
        {
            var b = new Border { BorderThickness = new Thickness(1), BorderBrush = IrBorder, Background = IrInset, CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 2), Cursor = new Cursor(StandardCursorType.Hand),
                Child = new TextBlock { Text = label, FontSize = 8, Foreground = new SolidColorBrush(Color.Parse("#A39D8F")), HorizontalAlignment = HorizontalAlignment.Center } };
            b.PointerPressed += (_, e) => { e.Handled = true; act(); };
            return b;
        }
        var center = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 4, Children = { MiniBtn("▤ GUI", openGui), MiniBtn("≡ Params", openParams) } };
        var footer = new Border { Height = 14, BorderBrush = IrBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(6, 0), Child =
            new DockPanel { LastChildFill = true, Children = { WithRight(new TextBlock { Text = "⠿", FontSize = 8, Foreground = IrMuted, VerticalAlignment = VerticalAlignment.Center }), footerLeft } } };
        DockPanel.SetDock(headWrap, Dock.Top); DockPanel.SetDock(footer, Dock.Bottom);
        return new Border { Width = 96, Background = IrCard, BorderBrush = IrBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), ClipToBounds = true, Opacity = dim ? 0.6 : 1.0,
            Child = new DockPanel { LastChildFill = true, Children = { headWrap, footer, center } } };
    }

    // Small borderless ✕ in a device slot header that removes that device from the chain.
    private Border SlotDelBtn(Action onDelete)
    {
        var glyph = new TextBlock { Text = "✕", FontSize = 8, Foreground = IrMuted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var b = new Border { Width = 13, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Stretch, Child = glyph };
        ToolTip.SetTip(b, "Remove device");
        b.PointerEntered += (_, _) => glyph.Foreground = IrRed;
        b.PointerExited += (_, _) => glyph.Foreground = IrMuted;
        b.PointerPressed += (_, e) => { e.Handled = true; onDelete(); };
        return b;
    }

    private int MacrosOnTarget(IRackAccess a, int sc, int dev)
    {
        int n = 0;
        for (int m = 0; m < a.MappingCount(); m++) if (a.TryGetMapping(m, out var mm) && mm.Chain == sc && mm.DeviceIndex == dev) n++;
        return n;
    }

    private Control IrInstrumentSlot(IRackAccess a, int sc)
    {
        int mc = MacrosOnTarget(a, sc, -1);
        var footer = mc > 0 ? new TextBlock { Text = $"{mc} macro", FontSize = 7, FontWeight = FontWeight.Bold, Foreground = IrTeal, VerticalAlignment = VerticalAlignment.Center }
                            : (Control)new TextBlock { Text = "INSTR", FontSize = 7, FontWeight = FontWeight.Bold, Foreground = IrMuted, VerticalAlignment = VerticalAlignment.Center };
        Border slot = null!;
        slot = IrSlot(ChainHue(sc), string.IsNullOrEmpty(a.ChainInstrumentName(sc)) ? "Instrument" : a.ChainInstrumentName(sc),
            a.ChainInstrumentKind(sc) < 0 ? "PLUG" : "INST", false, () => OpenChainInstrumentGui(a, sc, slot), () => OpenChainInstrumentParams(a, sc, slot), footer);
        return slot;
    }

    private Control IrDeviceSlot(IRackAccess a, int sc, int d)
    {
        bool byp = a.ChainDeviceBypassed(sc, d);
        int mc = MacrosOnTarget(a, sc, d);
        var footer = mc > 0 ? (Control)new TextBlock { Text = $"{mc} macro", FontSize = 7, FontWeight = FontWeight.Bold, Foreground = IrTeal, VerticalAlignment = VerticalAlignment.Center }
                            : IrMsBtn("BYP", byp, IrMuted, () => { a.SetChainDeviceBypassed(sc, d, !byp); _ctx.RequestRebuild(); });
        Border slot = null!;
        slot = IrSlot(byp ? IrMuted : Success, a.ChainDeviceName(sc, d), a.ChainDeviceBuiltinKind(sc, d) < 0 ? "PLUG" : "FX", byp, () => OpenChainDeviceGui(a, sc, d, slot), () => OpenChainDeviceParams(a, sc, d, slot), footer,
            () => { a.RemoveChainDevice(sc, d); DeferRebuild(); });
        return slot;
    }

    private static readonly IBrush DashIdle = new SolidColorBrush(Color.Parse("#3A362D"));

    private Control IrAddDeviceSlot(IRackAccess a, int sc)
    {
        var plus = new TextBlock { Text = "+", FontSize = 13, Foreground = IrMuted, HorizontalAlignment = HorizontalAlignment.Center };
        var lbl = new TextBlock { Text = "Device", FontSize = 8, Foreground = IrMuted, HorizontalAlignment = HorizontalAlignment.Center };
        var dash = new Avalonia.Controls.Shapes.Rectangle { Stroke = DashIdle, StrokeThickness = 1, StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 3, 2.5 }, RadiusX = 6, RadiusY = 6 };
        var box = new Panel { Width = 52, Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent,
            Children = { dash, new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 2, Children = { plus, lbl } } } };

        void Idle() { dash.Stroke = DashIdle; dash.Fill = null; plus.Foreground = IrMuted; lbl.Foreground = IrMuted; }
        void Hover() { dash.Stroke = new SolidColorBrush(Color.Parse("#6E6A5E")); plus.Foreground = IrTxt; lbl.Foreground = new SolidColorBrush(Color.Parse("#A39D8F")); }
        void Drop() { dash.Stroke = IrTeal; dash.Fill = new SolidColorBrush(Color.FromArgb(0x22, 0x5B, 0x9E, 0x9C)); plus.Foreground = IrTeal; lbl.Foreground = IrTeal; }

        box.PointerEntered += (_, _) => Hover();
        box.PointerExited += (_, _) => Idle();
        box.PointerPressed += (_, _) => ShowAddDeviceMenu(a, sc, box);
        DragDrop.SetAllowDrop(box, true);
        DragDrop.AddDragOverHandler(box, (_, e) => { if (!BrowserView.IsBrowserDrag || !IsEffect(BrowserView.CurrentDrag)) { e.DragEffects = DragDropEffects.None; return; } e.DragEffects = DragDropEffects.Copy; e.Handled = true; _ctx.HideDropGlow(); Drop(); });
        DragDrop.AddDragLeaveHandler(box, (_, _) => Idle());
        DragDrop.AddDropHandler(box, (_, e) => { Idle(); if (BrowserView.CurrentDrag is { } it && IsEffect(it)) { DropEffectOnChain(a, sc, it); e.Handled = true; } });
        return box;
    }

    // Add-device menu: built-in effects + a "Plug-ins" submenu of installed effect plug-ins.
    private void ShowAddDeviceMenu(IRackAccess a, int sc, Control anchor)
    {
        var f = new MenuFlyout();
        for (int k = 0; k < RackDeviceNames.Length; k++)
        {
            int kk = RackDeviceKinds[k];
            var mi = new MenuItem { Header = RackDeviceNames[k] };
            mi.Click += (_, _) => { a.AddChainDevice(sc, kk); _ctx.RequestRebuild(); };
            f.Items.Add(mi);
        }
        if (App.Services?.GetService(typeof(Nota.Application.IPluginCatalog)) is Nota.Application.IPluginCatalog cat && cat.Count > 0)
        {
            var sub = new MenuItem { Header = "Plug-ins" };
            for (int i = 0; i < cat.Count; i++)
            {
                var desc = cat.Description(i) ?? "";
                if (desc.Contains("| inst |")) continue;   // instruments aren't effects
                var parts = desc.Split('|');
                string nm = parts.Length > 0 ? parts[0].Trim() : desc;
                int idx = i;
                var mi = new MenuItem { Header = nm };
                mi.Click += (_, _) => { if (a.AddPluginChainDevice(sc, idx) >= 0) _ctx.RequestRebuild(); };
                sub.Items.Add(mi);
            }
            if (sub.Items.Count > 0) { f.Items.Add(new Separator()); f.Items.Add(sub); }
        }
        f.ShowAt(anchor, showAtPointer: true);
    }

    private Control IrZoneMap(IRackAccess a)
    {
        int n = a.ChainCount();
        var los = new int[n]; var his = new int[n]; var cols = new IBrush[n];
        for (int c = 0; c < n; c++) { E.RackChainZone(T, c, out int kLo, out int kHi, out int vLo, out int vHi); los[c] = _splitAxis == 0 ? kLo : vLo; his[c] = _splitAxis == 0 ? kHi : vHi; cols[c] = ChainHue(c); }
        var map = new RackZoneMap(los, his, cols, Sel, _splitAxis == 0);
        map.RangeEdited += (c, lo, hi) =>
        {
            E.RackChainZone(T, c, out int kLo, out int kHi, out int vLo, out int vHi);
            if (_splitAxis == 0) E.RackSetChainZone(T, c, lo, hi, vLo, vHi);
            else E.RackSetChainZone(T, c, kLo, kHi, lo, hi);
            _ctx.RequestRebuild();
        };
        return new Border { Height = 38, Background = IrInset, BorderBrush = IrField, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(4), Margin = new Thickness(0, 5, 0, 0), Child = map };
    }

    // ---- rack rail (96): volume · glide · macro-map · fold/save ----
    private Control IrRackRail(IRackAccess a)
    {
        var volVal = IrMono("0.0 dB", IrTxt, 9); volVal.HorizontalAlignment = HorizontalAlignment.Center;
        var vol = new Knob(Math.Clamp(E.RackVolume(T) / 2.0, 0, 1), 1.0) { Accent = true, Width = 40, Height = 40, HorizontalAlignment = HorizontalAlignment.Center };
        string VolDb(double lin) => lin <= 0.001 ? "−∞ dB" : $"{20 * Math.Log10(lin):+0.0;−0.0;0.0} dB";
        vol.ValueChanged += v => { E.RackSetVolume(T, (float)(v * 2.0)); volVal.Text = VolDb(v * 2.0); };
        volVal.Text = VolDb(E.RackVolume(T));
        MidiLearn.Bind(vol, MidiTarget.RackVolume(T, a.AutomationDeviceIndex), "Rack Volume");
        var volCell = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Children = { IrCap("VOLUME"), vol, volVal } };

        var glideVal = IrMono("0 ms", IrTxt, 8); glideVal.VerticalAlignment = VerticalAlignment.Center;
        var gFill = new Border { Height = 3, Background = IrAmber, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var gSlot = new Panel { Height = 8, MinWidth = 30, Children = { new Border { Height = 3, Background = IrInset, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center }, gFill } };
        string GlideMs(double v) => $"{v * 500:0} ms";
        void GUpd() { double v = E.RackGlide(T); gFill.Width = v * gSlot.Bounds.Width; glideVal.Text = GlideMs(v); }
        bool gd = false; void GSet(double x) { double v = Math.Clamp(x / Math.Max(1, gSlot.Bounds.Width), 0, 1); E.RackSetGlide(T, (float)v); GUpd(); }
        gSlot.PointerPressed += (_, e) => { gd = true; e.Pointer.Capture(gSlot); GSet(e.GetPosition(gSlot).X); };
        gSlot.PointerMoved += (_, e) => { if (gd) GSet(e.GetPosition(gSlot).X); };
        gSlot.PointerReleased += (_, e) => { if (gd) { gd = false; e.Pointer.Capture(null); } };
        _irTick.Add(() => { if (!gd) GUpd(); });
        var glideRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 4, Children = { IrCap("GLIDE"), WithCol(gSlot, 1), WithCol(glideVal, 2) } };

        var mapBtn = new Border { BorderBrush = IrTeal, BorderThickness = new Thickness(1), Background = new SolidColorBrush(Color.FromArgb(0x24, 0x5B, 0x9E, 0x9C)), CornerRadius = new CornerRadius(4), Padding = new Thickness(0, 3), Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock { Text = "MACRO MAP", FontSize = 8, FontWeight = FontWeight.Bold, Foreground = IrTeal, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center } };
        mapBtn.PointerPressed += (_, _) => { _macroMapMode = true; _ctx.RequestRebuild(); };

        var fold = IrRailBtn(_foldZone ? "Unfold" : "Fold", () => { _foldZone = !_foldZone; _ctx.RequestRebuild(); });
        var save = IrRailBtn("Save", () => _ctx.RequestPresetSave(-1));
        var foldSave = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 4, [DockPanel.DockProperty] = Dock.Bottom, Children = { fold, WithCol(save, 1) } };

        var top = new StackPanel { Spacing = 6, [DockPanel.DockProperty] = Dock.Top, Children = { IrCap("RACK OUT"), volCell, glideRow, new Border { Height = 1, Background = IrCard }, mapBtn } };
        var inner = new DockPanel { LastChildFill = false, Children = { top, foldSave } };
        return new Border { Width = 96, Background = IrRail, BorderBrush = IrBorder, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(8, 6), Child = inner };
    }

    private static readonly InstrumentCardFactory _irFactory = new();
    private static readonly DeviceCardFactory _irDeviceFactory = new();

    // Full GUI: hosted plug-in → its native window; built-in Sampler → the tabbed editor
    // (drop-to-load); other built-ins → their bespoke editor hosted over the chain's param
    // surface via a proxy engine (so Volt/Aurora/… show their real UI, not just knobs).
    private void OpenChainInstrumentGui(IRackAccess a, int sc, Control anchor)
    {
        int ik = a.ChainInstrumentKind(sc);
        if (ik == -1) { try { E.RackOpenChainInstrumentEditor(T, sc); } catch { /* no-op */ } return; }
        if (ik == 1) { OpenChainSamplerWindow(anchor, sc); return; }
        string nm = string.IsNullOrEmpty(a.ChainInstrumentName(sc)) ? "Instrument" : a.ChainInstrumentName(sc);
        OpenFullUiWindow(anchor, nm, tickReg => BuildChainInstrumentBespoke(ik, sc, tickReg));
    }

    // Host a built-in instrument's own editor for a rack chain: a DispatchProxy redirects
    // the editor's plugin-param calls (track, -1, i) to the chain surface (track, chain, i);
    // a local tick drives its live graphs + knob follow.
    private Control BuildChainInstrumentBespoke(int ik, int sc, Action<Action> tickReg)
    {
        IAudioEngine proxy = System.Reflection.DispatchProxy.Create<IAudioEngine, RackChainEngineProxy>();
        var pp = (RackChainEngineProxy)(object)proxy; pp.Inner = E; pp.Chain = sc;
        var faders = new System.Collections.Generic.List<(int i, Knob k, TextBlock v, Func<float, string>? fmt)>();
        Action? viz = null;
        var ctx = new DeviceCardContext(proxy, T, tickReg, () => { }, (i, k, v, f) => faders.Add((i, k, v, f)), v => viz = v,
            _ => { }, () => 0, _ => { }, tickReg, () => { }, () => { }, _ => { }, () => { });
        var body = _irFactory.Resolve(ik, true).Build(ctx);
        tickReg(() => { viz?.Invoke(); foreach (var f in faders) { if (f.k.Dragging) continue; float v = proxy.PluginParamGet(T, -1, f.i); if (Math.Abs(v - f.k.Value) > 1e-3) { f.k.Value = v; f.v.Text = (f.fmt ?? Pct)(v); } } });
        return new Border { Width = 700, Height = 234, Background = new SolidColorBrush(Color.Parse("#171613")), ClipToBounds = true, Child = body };
    }

    // Sampler pop-out that also accepts a dropped sample file (browser or Finder) → load it
    // into the chain's Sampler and rebuild the editor.
    private void OpenChainSamplerWindow(Control anchor, int sc)
    {
        var acc = new ChainSamplerAccess(E, T, sc);
        var ticks = new System.Collections.Generic.List<Action>();
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        timer.Tick += (_, _) => { for (int i = 0; i < ticks.Count; i++) ticks[i](); };
        var win = new NotaPopupWindow { Title = "Nota Sampler", SizeToContent = SizeToContent.WidthAndHeight, CanResize = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        void Rebuild()
        {
            ticks.Clear();
            var wrap = new Border { Width = 700, Height = 234, Background = new SolidColorBrush(Color.Parse("#171613")), ClipToBounds = true, Child = SamplerInstrumentCard.BuildEditor(E, acc, ticks.Add) };
            DragDrop.SetAllowDrop(wrap, true);
            DragDrop.AddDragOverHandler(wrap, (_, e) => { if (!BrowserView.IsAcceptableDrag(e)) { e.DragEffects = DragDropEffects.None; return; } e.DragEffects = DragDropEffects.Copy; e.Handled = true; });
            DragDrop.AddDropHandler(wrap, (_, e) => { foreach (var it in BrowserView.DroppedItems(e)) if (it.Kind == Nota.Presentation.BrowserItemKind.Sample) { acc.LoadSample(it.Path); Rebuild(); break; } e.Handled = true; });
            win.SetContent(wrap);
        }
        Rebuild();
        win.Opened += (_, _) => timer.Start();
        win.Closed += (_, _) => timer.Stop();
        ShowPopup(win, anchor);
    }
    private void OpenChainInstrumentParams(IRackAccess a, int sc, Control anchor)
    {
        string nm = string.IsNullOrEmpty(a.ChainInstrumentName(sc)) ? "Instrument" : a.ChainInstrumentName(sc);
        OpenFullUiWindow(anchor, nm + " · params", _ => ParamGridContent(nm, a.ChainInstrumentParamCount(sc), p => RackInstParamRow(a, sc, p)));
    }
    private void OpenChainDeviceGui(IRackAccess a, int sc, int d, Control anchor)
    {
        int kind = a.ChainDeviceBuiltinKind(sc, d);
        if (kind < 0) { a.OpenChainDeviceEditor(sc, d); return; }   // hosted plug-in → native window
        string nm = a.ChainDeviceName(sc, d);
        OpenFullUiWindow(anchor, nm, tickReg => BuildChainDeviceBespoke(a, sc, d, kind, tickReg));   // built-in → its real device card
    }

    // Host a built-in effect's own device card for a rack chain: a DispatchProxy redirects
    // the card's Device* param calls onto the chain-device surface (via IRackAccess), so the
    // effect shows its real UI (AutoFilter curve, EQ bands, …) rather than plain knobs.
    private Control BuildChainDeviceBespoke(IRackAccess a, int sc, int d, int kind, Action<Action> tickReg)
    {
        IAudioEngine proxy = System.Reflection.DispatchProxy.Create<IAudioEngine, RackChainDeviceEngineProxy>();
        var pp = (RackChainDeviceEngineProxy)(object)proxy; pp.Inner = E; pp.Access = a; pp.Chain = sc; pp.Dev = d;
        var ctx = new DeviceCardContext(proxy, T, tickReg, () => { }, (_, _, _, _) => { }, _ => { }, _ => { }, () => sc, _ => { }, tickReg, () => { }, () => { }, _ => { }, () => { });
        var body = _irDeviceFactory.Resolve(kind).Build(ctx, a.AutomationDeviceIndex);
        return new Border { Width = 700, Height = 234, Background = new SolidColorBrush(Color.Parse("#171613")), ClipToBounds = true, Child = body };
    }
    private void OpenChainDeviceParams(IRackAccess a, int sc, int d, Control anchor)
    {
        string dnm = a.ChainDeviceName(sc, d);
        OpenFullUiWindow(anchor, dnm + " · params", _ => ParamGridContent(dnm, a.ChainDeviceParamCount(sc, d), p => RackDeviceParamRow(a, sc, d, p)));
    }

    private void ShowAddChainMenu(IRackAccess a, Control anchor)
    {
        var f = new MenuFlyout();
        void Add(string h, int k) { var mi = new MenuItem { Header = h }; mi.Click += (_, _) => { int c = a.AddChain(k); if (c >= 0) Sel = c; _ctx.RequestRebuild(); }; f.Items.Add(mi); }
        Add("Nota Synth", 0); Add("Nota Sampler", 1); Add("Nota Physical", 2); Add("Nota Aurora", 5);
        Add("Nota Volt", 6); Add("Nota Bass", 7); Add("Nota Pendulum", 8); Add("Nota Operator", 9); Add("Nota Grain", 10); Add("Nota Flux", 11); Add("Nota Monolith", 13); Add("Nota Pentad", 14);
        f.ShowAt(anchor, showAtPointer: true);
    }

    private static bool _foldZone;
    private static Control WithRight(Control c) { DockPanel.SetDock(c, Dock.Right); return c; }
    private static Control WithCol(Control c, int col) { Grid.SetColumn(c, col); return c; }

    private Control IrSeg(string[] names, int sel, Action<int> onSel)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
        for (int i = 0; i < names.Length; i++)
        {
            int iv = i; bool on = i == sel;
            var c = new Border { CornerRadius = new CornerRadius(2), Padding = new Thickness(6, 1), Cursor = new Cursor(StandardCursorType.Hand), Background = on ? IrAmber : Brushes.Transparent,
                Child = new TextBlock { Text = names[i], FontSize = 8, FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal, Foreground = on ? new SolidColorBrush(Color.Parse("#171613")) : IrMuted } };
            c.PointerPressed += (_, _) => onSel(iv);
            row.Children.Add(c);
        }
        return new Border { Background = IrInset, BorderBrush = IrBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Child = row };
    }

    private Border IrMsBtn(string t, bool on, IBrush accent, Action onClick)
    {
        var b = new Border { MinWidth = 13, Height = 12, CornerRadius = new CornerRadius(2), BorderThickness = new Thickness(1), BorderBrush = on ? accent : new SolidColorBrush(Color.Parse("#3A362D")), Background = on ? accent : IrCard, Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock { Text = t, FontSize = 7, FontWeight = FontWeight.Bold, Foreground = on ? new SolidColorBrush(Color.Parse("#171613")) : new SolidColorBrush(Color.Parse("#A39D8F")), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(3, 0) } };
        b.PointerPressed += (_, e) => { e.Handled = true; onClick(); };
        return b;
    }

    private Border IrRailBtn(string t, Action onClick)
    {
        var b = new Border { BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.Parse("#3A362D")), Background = IrCard, CornerRadius = new CornerRadius(3), Padding = new Thickness(0, 2), Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock { Text = t, FontSize = 9, Foreground = new SolidColorBrush(Color.Parse("#A39D8F")), HorizontalAlignment = HorizontalAlignment.Center } };
        b.PointerPressed += (_, _) => onClick();
        return b;
    }

    // ---- second view: Macro map (mockup 2p card 2) ----
    private static int _mapMacro;
    // withHeader=false + a custom onDone lets the Audio Effect Rack reuse this view
    // inside the shared device shell (no duplicate instrument header).
    private Control InstrumentRackMacroMap(IRackAccess a, bool withHeader = true, Action? onDone = null)
    {
        var header = withHeader ? IrHeader(a, "MAP") : null;
        // MAPPING strip.
        var done = new Border { BorderBrush = IrTeal, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 2), Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock { Text = "Done", FontSize = 9, Foreground = IrTeal, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center }, [DockPanel.DockProperty] = Dock.Right };
        done.PointerPressed += (_, _) => { if (onDone != null) onDone(); else { _macroMapMode = false; _ctx.RequestRebuild(); } };
        var strip = new Border { Height = 34, Background = IrHdr, BorderBrush = IrBorder, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(9, 0), Child =
            new DockPanel { LastChildFill = false, Children = { done, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = {
                IrCap("MAPPING", IrTeal), new TextBlock { Text = a.MacroName(_mapMacro), FontSize = 9, Foreground = new SolidColorBrush(Color.Parse("#A39D8F")), VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = "→ click any control to add · drag its range below", FontSize = 8, Foreground = IrMuted, VerticalAlignment = VerticalAlignment.Center } } } } } };

        // Macros list (left).
        var macList = new StackPanel { Spacing = 3 };
        macList.Children.Add(IrCap("MACROS"));
        for (int i = 0; i < 8; i++)
        {
            int iv = i; int tCount = 0;
            for (int m = 0; m < a.MappingCount(); m++) if (a.TryGetMapping(m, out var mm) && mm.Macro == i) tCount++;
            bool cur = i == _mapMacro;
            var nm = new TextBlock { Text = a.MacroName(i), FontSize = 9, Foreground = cur ? IrAmberLit : (tCount > 0 ? IrTxt : IrMuted), Width = 60, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            var fill = new Border { Height = 3, Background = IrAmber, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var slot = new Panel { Height = 6, Children = { new Border { Height = 3, Background = IrInset, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center }, fill } };
            _irTick.Add(() => fill.Width = E.PluginParamGet(T, a.AutomationDeviceIndex, iv) * slot.Bounds.Width);
            var cnt = IrMono(tCount.ToString(), cur ? IrTeal : IrMuted);
            var rowg = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 5, Cursor = new Cursor(StandardCursorType.Hand), Children = { nm, WithCol(slot, 1), WithCol(cnt, 2) } };
            rowg.PointerPressed += (_, _) => { _mapMacro = iv; _ctx.RequestRebuild(); };
            macList.Children.Add(rowg);
        }
        var macCol = new Border { Width = 186, Background = IrRail, BorderBrush = IrBorder, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(8, 6), Child = macList };

        // Targets (right).
        var targetStack = new StackPanel { Spacing = 4 };
        int nTargets = 0;
        for (int m = 0; m < a.MappingCount(); m++) if (a.TryGetMapping(m, out var mm) && mm.Macro == _mapMacro) nTargets++;
        targetStack.Children.Add(new DockPanel { LastChildFill = false, Children = { IrCap($"{a.MacroName(_mapMacro).ToUpperInvariant()} → {nTargets} TARGET{(nTargets == 1 ? "" : "S")}"), WithRight(new TextBlock { Text = "min — max", FontSize = 8, Foreground = IrMuted, VerticalAlignment = VerticalAlignment.Center }) } });
        for (int m = 0; m < a.MappingCount(); m++)
        {
            if (!a.TryGetMapping(m, out var mm) || mm.Macro != _mapMacro) continue;
            targetStack.Children.Add(IrMappingRow(a, m, mm));
        }
        targetStack.Children.Add(new Border { BorderBrush = new SolidColorBrush(Color.Parse("#3A362D")), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(0, 4),
            Child = new TextBlock { Text = "click a control in any device to map it here", FontSize = 9, Foreground = IrMuted, HorizontalAlignment = HorizontalAlignment.Center } });

        var curveSeg = IrSeg(new[] { "Linear", "Exp", "Log", "S" }, CommonCurve(a, _mapMacro), i => { for (int m = 0; m < a.MappingCount(); m++) if (a.TryGetMapping(m, out var mm) && mm.Macro == _mapMacro) a.SetMappingCurve(m, i); _ctx.RequestRebuild(); });
        var unmap = new Border { BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0xD9, 0x5F, 0x4C)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(8, 2), Cursor = new Cursor(StandardCursorType.Hand), [DockPanel.DockProperty] = Dock.Right,
            Child = new TextBlock { Text = "Unmap all", FontSize = 9, Foreground = IrRed } };
        unmap.PointerPressed += (_, _) => { for (int m = a.MappingCount() - 1; m >= 0; m--) if (a.TryGetMapping(m, out var mm) && mm.Macro == _mapMacro) a.RemoveMapping(m); _ctx.RequestRebuild(); };
        var bottom = new DockPanel { LastChildFill = false, [DockPanel.DockProperty] = Dock.Bottom, Children = { unmap, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { IrCap("CURVE"), curveSeg } } } };
        var tScroll = new ScrollViewer { Content = targetStack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var tInner = new DockPanel { LastChildFill = true, Children = { bottom, tScroll } };
        var targets = new Border { Padding = new Thickness(9, 6), Child = tInner };

        DockPanel.SetDock(macCol, Dock.Left);
        var body = new DockPanel { LastChildFill = true, Children = { macCol, targets } };
        DockPanel.SetDock(strip, Dock.Top);
        var root = new DockPanel { LastChildFill = true };
        if (header != null) { DockPanel.SetDock(header, Dock.Top); root.Children.Add(header); }
        root.Children.Add(strip); root.Children.Add(body);
        return root;
    }

    private int CommonCurve(IRackAccess a, int macro)
    {
        for (int m = 0; m < a.MappingCount(); m++) if (a.TryGetMapping(m, out var mm) && mm.Macro == macro) return a.MappingCurve(m);
        return 0;
    }

    private Control IrMappingRow(IRackAccess a, int mappingIndex, RackMacroMapping mm)
    {
        string dev = mm.DeviceIndex < 0 ? a.ChainInstrumentName(mm.Chain) : a.ChainDeviceName(mm.Chain, mm.DeviceIndex);
        string pn = mm.DeviceIndex < 0 ? a.ChainInstrumentParamName(mm.Chain, mm.ParamIndex) : a.ChainDeviceParamName(mm.Chain, mm.DeviceIndex, mm.ParamIndex);
        float pMin = 0, pMax = 1;
        if (mm.DeviceIndex >= 0) { pMin = a.ChainDeviceParamMin(mm.Chain, mm.DeviceIndex, mm.ParamIndex); pMax = a.ChainDeviceParamMax(mm.Chain, mm.DeviceIndex, mm.ParamIndex); }
        double span = Math.Max(1e-6, pMax - pMin);
        double fLo = Math.Clamp((mm.RangeMin - pMin) / span, 0, 1), fHi = Math.Clamp((mm.RangeMax - pMin) / span, 0, 1);
        var name = new TextBlock { Text = $"{dev} · {pn}", FontSize = 9, Foreground = IrTxt, Width = 122, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var band = new Border { Height = 6, Background = new SolidColorBrush(Color.FromArgb(0x73, 0x5B, 0x9E, 0x9C)), CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var track = new Panel { Height = 6, MinWidth = 60, Children = { new Border { Height = 6, Background = new SolidColorBrush(Color.Parse("#171613")), CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center }, band } };
        var vals = IrMono($"{mm.RangeMin * 100:0}%–{mm.RangeMax * 100:0}%", new SolidColorBrush(Color.Parse("#A39D8F")));
        void Upd() { double w = track.Bounds.Width; band.Margin = new Thickness(fLo * w, 0, 0, 0); band.Width = Math.Max(2, (fHi - fLo) * w); }
        int drag = 0;  // 1 = min, 2 = max
        void Apply(double x) { double f = Math.Clamp(x / Math.Max(1, track.Bounds.Width), 0, 1); if (drag == 1) fLo = Math.Min(f, fHi); else fHi = Math.Max(f, fLo); Upd(); }
        track.PointerPressed += (_, e) => { double x = e.GetPosition(track).X, w = track.Bounds.Width; drag = Math.Abs(x - fLo * w) <= Math.Abs(x - fHi * w) ? 1 : 2; e.Pointer.Capture(track); Apply(x); };
        track.PointerMoved += (_, e) => { if (drag != 0) Apply(e.GetPosition(track).X); };
        track.PointerReleased += (_, e) => { if (drag != 0) { drag = 0; e.Pointer.Capture(null); a.SetMappingRange(mappingIndex, (float)(pMin + fLo * span), (float)(pMin + fHi * span)); _ctx.RequestRebuild(); } };
        _irTick.Add(Upd);
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 6, Children = { name, WithCol(track, 1), WithCol(vals, 2) } };
        return new Border { Background = IrInset, BorderBrush = IrField, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 4), Child = g };
    }

    // Audio Effect Rack body (a device, kind 5): wrapped by the view's DeviceShell.
    // Audio Effect Rack (mockup 2r): the shared device shell provides the header; this
    // body is the macro strip + (chains | devices | rack rail), with a MODE switch
    // (Parallel / Series / Select). Series swaps the body for stacked chain columns.
    public Control BuildEffectRackBody(int di)
    {
        var a = new EffectRackAccess(E, T, di);
        _irTick.Clear();
        int chains = a.ChainCount();
        Sel = chains > 0 ? Math.Clamp(Sel, 0, chains - 1) : 0;

        Control root;
        if (_aeMacroMap)
        {
            root = InstrumentRackMacroMap(a, false, () => { _aeMacroMap = false; DeferRebuild(); });
        }
        else
        {
            int mode = E.RackDevMode(T, di);
            var strip = IrMacroStrip(a);
            Control body = mode == 1 ? AeSeriesBody(a, di) : AeParallelBody(a, di);
            DockPanel.SetDock(strip, Dock.Top);
            root = new DockPanel { LastChildFill = true, Children = { strip, body } };
        }
        if (_irTick.Count > 0) _ctx.AddDeviceRefresher(() => { for (int i = 0; i < _irTick.Count; i++) _irTick[i](); });
        return new Border { Height = 234, ClipToBounds = true, Child = root };   // fills the full-bleed shell slot (700)
    }

    // Parallel (mode 0) / Select (mode 2): chain list | devices | rack rail.
    private Control AeParallelBody(IRackAccess a, int di)
    {
        var list = AeChainList(a, di);
        var rail = AeRackRail(a, di);
        var devs = AeDeviceArea(a, di);
        DockPanel.SetDock(list, Dock.Left); DockPanel.SetDock(rail, Dock.Right);
        return new DockPanel { LastChildFill = true, Children = { list, rail, devs } };
    }

    // ---- chain list (196) ----
    private Control AeChainList(IRackAccess a, int di)
    {
        int mode = E.RackDevMode(T, di);
        int chains = a.ChainCount();
        string sub = mode == 2 ? $"{AeActiveCount(a, di)} active" : (mode == 1 ? "series" : "parallel · summed");
        var header = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 5), Children = {
            IrCap("CHAINS"), WithRight(new TextBlock { Text = sub, FontSize = 8, Foreground = IrMuted, VerticalAlignment = VerticalAlignment.Center }) } };
        var rows = new StackPanel { Spacing = 4 };
        for (int c = 0; c < chains; c++) rows.Children.Add(AeChainRow(a, di, c));
        var add = new Border { BorderBrush = new SolidColorBrush(Color.Parse("#3A362D")), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(0, 3), Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock { Text = "+ Add chain", FontSize = 9, Foreground = IrMuted, HorizontalAlignment = HorizontalAlignment.Center } };
        add.PointerPressed += (_, _) => { int c = a.AddChain(-1); if (c >= 0) Sel = c; _ctx.RequestRebuild(); };
        rows.Children.Add(add);
        var scroll = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 6, 0) };
        DockPanel.SetDock(header, Dock.Top);
        var inner = new DockPanel { LastChildFill = true, Children = { header, scroll } };
        return new Border { Width = 196, Background = IrRail, BorderBrush = IrBorder, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(8, 6), Child = inner };
    }

    private Control AeChainRow(IRackAccess a, int di, int c)
    {
        bool sel = c == Sel;
        string name = a.ChainInstrumentName(c); if (string.IsNullOrEmpty(name)) name = $"Chain {c + 1}";
        int devs = a.ChainDeviceCount(c);
        var nameTb = new TextBlock { Text = name, FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = sel ? IrAmberLit : IrTxt, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var head = new DockPanel { LastChildFill = true, Children = { WithRight(ChainDelBtn(a, c)), WithRight(IrMono($"{devs} dev", IrMuted)), nameTb } };
        double db = a.ChainGain(c) <= 0.001 ? -60 : 20 * Math.Log10(a.ChainGain(c));
        var dbT = IrMono(db <= -59 ? "−∞" : $"{db:+0.0;−0.0;0.0}", new SolidColorBrush(Color.Parse("#A39D8F")));
        var meterFill = new Border { Background = Success, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(1) };
        var meter = new Border { Height = 3, Background = IrInset, CornerRadius = new CornerRadius(1), Child = meterFill, VerticalAlignment = VerticalAlignment.Center };
        _irTick.Add(() => meterFill.Width = meter.Bounds.Width * Math.Clamp(a.ChainMeter(c), 0, 1));
        var m = IrMsBtn("M", a.ChainMute(c), IrRed, () => { a.SetChainMute(c, !a.ChainMute(c)); _ctx.RequestRebuild(); });
        var s = IrMsBtn("S", a.ChainSolo(c), IrAmber, () => { a.SetChainSolo(c, !a.ChainSolo(c)); _ctx.RequestRebuild(); });
        MidiLearn.Bind(m, MidiTarget.RackChainMute(T, a.AutomationDeviceIndex, c), "Chain Mute");
        MidiLearn.Bind(s, MidiTarget.RackChainSolo(T, a.AutomationDeviceIndex, c), "Chain Solo");
        var meterGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 4, VerticalAlignment = VerticalAlignment.Center };
        meterGrid.Children.Add(dbT); Grid.SetColumn(meter, 1); meterGrid.Children.Add(meter);
        Grid.SetColumn(m, 2); meterGrid.Children.Add(m); Grid.SetColumn(s, 3); meterGrid.Children.Add(s);
        var stack = new StackPanel { Spacing = 3, Children = { head, meterGrid } };
        // Built exactly like the Instrument Rack row (Border + PointerPressed), so it sizes to
        // its content. Clicking is reliable now that the full-bleed body removed the earlier
        // fixed-width overflow that skewed hit-testing inside the device shell.
        var row = new Border { Background = sel ? RowSel : IrCard, BorderBrush = sel ? IrAmber : IrBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 4), Cursor = new Cursor(StandardCursorType.Hand), Child = stack };
        // left accent = chain hue, hidden while selected (the amber selection is enough).
        var accent = new Border { Width = 2, Background = sel ? Brushes.Transparent : ChainHue(c), CornerRadius = new CornerRadius(2, 0, 0, 2) };
        var wrap = new DockPanel { LastChildFill = true, Children = { accent, row } };
        DockPanel.SetDock(accent, Dock.Left);
        wrap.PointerPressed += (_, e) => { if (e.GetCurrentPoint(wrap).Properties.IsLeftButtonPressed && Sel != c) { Sel = c; _ctx.RequestRebuild(); } };
        DragDrop.SetAllowDrop(wrap, true);
        DragDrop.AddDragOverHandler(wrap, (_, e) => { if (!BrowserView.IsBrowserDrag || !IsEffect(BrowserView.CurrentDrag)) { e.DragEffects = DragDropEffects.None; return; } e.DragEffects = DragDropEffects.Copy; e.Handled = true; _ctx.HideDropGlow(); row.BorderBrush = AccentBright; });
        DragDrop.AddDragLeaveHandler(wrap, (_, _) => row.BorderBrush = sel ? IrAmber : IrBorder);
        DragDrop.AddDropHandler(wrap, (_, e) => { row.BorderBrush = sel ? IrAmber : IrBorder; if (BrowserView.CurrentDrag is { } it && IsEffect(it)) { Sel = c; DropEffectOnChain(a, c, it); e.Handled = true; } });
        return wrap;
    }

    // A small borderless ✕ that removes a rack chain (shared by both rack kinds).
    private Control ChainDelBtn(IRackAccess a, int c)
    {
        var glyph = new TextBlock { Text = "✕", FontSize = 9, Foreground = IrMuted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var b = new Border { Width = 14, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Stretch, Child = glyph };
        ToolTip.SetTip(b, "Remove chain");
        b.PointerEntered += (_, _) => glyph.Foreground = IrRed;
        b.PointerExited += (_, _) => glyph.Foreground = IrMuted;
        b.PointerPressed += (_, e) => { e.Handled = true; int n = a.ChainCount(); a.RemoveChain(c); if (Sel > c) Sel--; Sel = Math.Clamp(Sel, 0, Math.Max(0, n - 2)); DeferRebuild(); };
        return b;
    }

    // ---- device area (center): horizontal device cards + (Select) level selector ----
    private Control AeDeviceArea(IRackAccess a, int di)
    {
        if (a.ChainCount() == 0)
            return new Border { Padding = new Thickness(9, 7), Child = new TextBlock { Text = "Add a chain to build its device chain.", FontSize = 10, Foreground = IrMuted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        int sc = Sel, mode = E.RackDevMode(T, di);
        double pdcMs = E.RackDevPdc(T, di) ? 0 : 0;   // (informational; latency in samples is host-side)
        var hdr = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 6), Children = {
            new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(2), Background = ChainHue(sc), VerticalAlignment = VerticalAlignment.Center },
            new TextBlock { Text = $"Chain {sc + 1}", FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = IrTxt, VerticalAlignment = VerticalAlignment.Center },
            new TextBlock { Text = "devices · signal left → right", FontSize = 8, Foreground = IrMuted, VerticalAlignment = VerticalAlignment.Center } } };

        var slots = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        int dc = a.ChainDeviceCount(sc);
        if (_aeFold)
            for (int d = 0; d < dc; d++) slots.Children.Add(AeFoldedSlot(a, sc, d));
        else
            for (int d = 0; d < dc; d++) slots.Children.Add(IrDeviceSlot(a, sc, d));
        slots.Children.Add(IrAddDeviceSlot(a, sc));
        var row = new ScrollViewer { Content = slots, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };

        var inner = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(hdr, Dock.Top); inner.Children.Add(hdr);
        if (mode == 2) { var selMap = AeSelectorMap(a, di); DockPanel.SetDock(selMap, Dock.Bottom); inner.Children.Add(selMap); }
        inner.Children.Add(row);
        return new Border { Padding = new Thickness(8, 6), Child = inner };
    }

    // Folded device: a compact name chip (no GUI/params body).
    private Control AeFoldedSlot(IRackAccess a, int sc, int d)
    {
        bool byp = a.ChainDeviceBypassed(sc, d);
        var chip = new Border { Height = 22, Background = IrCard, BorderBrush = IrBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(7, 0), Opacity = byp ? 0.6 : 1.0, VerticalAlignment = VerticalAlignment.Top,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = {
                new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = byp ? IrMuted : Success, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = a.ChainDeviceName(sc, d), FontSize = 9, Foreground = IrTxt, VerticalAlignment = VerticalAlignment.Center },
                SlotDelBtn(() => { a.RemoveChainDevice(sc, d); DeferRebuild(); }) } } };
        Border b = chip; b.PointerPressed += (_, e) => { e.Handled = true; OpenChainDeviceGui(a, sc, d, b); };
        return chip;
    }

    // ---- chain selector on an input-level axis (Select mode) ----
    private int AeActiveCount(IRackAccess a, int di)
    {
        float sel = E.RackDevLiveSelector(T, di);
        int n = 0, chains = a.ChainCount();
        for (int c = 0; c < chains; c++)
        {
            E.RackDevChainZone(T, di, c, out int lo, out int hi);
            if (sel >= lo / 127.0f && sel <= hi / 127.0f && !a.ChainMute(c)) n++;
        }
        return n;
    }

    private Control AeSelectorMap(IRackAccess a, int di)
    {
        int n = a.ChainCount();
        var los = new int[n]; var his = new int[n]; var cols = new IBrush[n];
        for (int c = 0; c < n; c++) { E.RackDevChainZone(T, di, c, out int lo, out int hi); los[c] = lo; his[c] = hi; cols[c] = ChainHue(c); }
        var map = new RackZoneMap(los, his, cols, Sel, keyMode: false);
        map.RangeEdited += (c, lo, hi) => { E.RackDevSetChainZone(T, di, c, lo, hi); _ctx.RequestRebuild(); };
        bool follow = E.RackDevSelFollow(T, di);
        var marker = new Border { Width = 1.5, Background = IrAmberLit, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Stretch };
        var mapHost = new Panel { Children = { map, marker } };
        _irTick.Add(() => marker.Margin = new Thickness(E.RackDevLiveSelector(T, di) * mapHost.Bounds.Width, 0, 0, 0));
        // When not following the input, click/drag on the axis sets the manual selector.
        if (!follow)
            mapHost.PointerPressed += (_, e) => { if (e.GetCurrentPoint(mapHost).Properties.IsRightButtonPressed) { double x = e.GetPosition(mapHost).X; E.RackDevSetChainSelect(T, di, (float)Math.Clamp(x / Math.Max(1, mapHost.Bounds.Width), 0, 1)); } };

        var readout = IrMono("", IrAmberLit);
        _irTick.Add(() => readout.Text = $"in {(int)Math.Round(E.RackDevLiveSelector(T, di) * 48 - 48)} dB → {AeActiveCount(a, di)} chain{(AeActiveCount(a, di) == 1 ? "" : "s")}");
        var head = new DockPanel { LastChildFill = false, Children = { IrCap("CHAIN SELECTOR"),
            WithRight(DrumChip(follow ? "Follow" : "Manual", follow, () => { E.RackDevSetSelFollow(T, di, !follow); DeferRebuild(); })),
            WithRight(readout) } };
        DockPanel.SetDock(head, Dock.Top);
        var body = new DockPanel { LastChildFill = true, Children = { head, mapHost } };
        return new Border { Height = 52, Background = IrInset, BorderBrush = IrField, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 4), Margin = new Thickness(0, 5, 0, 0), Child = body };
    }

    // ---- rack rail (96): MODE · dry/wet · gain · macro-map · pdc · fold/save ----
    private Control AeRackRail(IRackAccess a, int di)
    {
        int mode = E.RackDevMode(T, di);
        var modeSeg = AeModeSeg(mode, di);

        var dwVal = IrMono("", IrTxt, 9); dwVal.HorizontalAlignment = HorizontalAlignment.Center;
        var dw = new Knob(Math.Clamp(E.RackDevDryWet(T, di), 0, 1), 1.0) { Accent = true, Width = 38, Height = 38, HorizontalAlignment = HorizontalAlignment.Center };
        dw.ValueChanged += v => { E.RackDevSetDryWet(T, di, (float)v); dwVal.Text = $"{v * 100:0} %"; };
        dwVal.Text = $"{E.RackDevDryWet(T, di) * 100:0} %";
        var dwCell = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Children = { IrCap("DRY / WET"), dw, dwVal } };

        string GainDb(double lin) => lin <= 0.001 ? "−∞" : $"{20 * Math.Log10(lin):+0.0;−0.0;0.0}";
        var gainVal = IrMono(GainDb(E.RackDevVolume(T, di)), IrTxt, 8); gainVal.VerticalAlignment = VerticalAlignment.Center;
        var gFill = new Border { Height = 3, Background = IrAmber, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var gSlot = new Panel { Height = 8, MinWidth = 26, Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent, Children = { new Border { Height = 3, Background = IrInset, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center }, gFill } };
        void GUpd() { double v = Math.Clamp(E.RackDevVolume(T, di) / 2.0, 0, 1); gFill.Width = v * gSlot.Bounds.Width; gainVal.Text = GainDb(E.RackDevVolume(T, di)); }
        bool gd = false; void GSet(double x) { double v = Math.Clamp(x / Math.Max(1, gSlot.Bounds.Width), 0, 1); E.RackDevSetVolume(T, di, (float)(v * 2.0)); GUpd(); }
        gSlot.PointerPressed += (_, e) => { gd = true; e.Pointer.Capture(gSlot); GSet(e.GetPosition(gSlot).X); };
        gSlot.PointerMoved += (_, e) => { if (gd) GSet(e.GetPosition(gSlot).X); };
        gSlot.PointerReleased += (_, e) => { if (gd) { gd = false; e.Pointer.Capture(null); } };
        _irTick.Add(() => { if (!gd) GUpd(); });
        MidiLearn.Bind(gSlot, MidiTarget.RackVolume(T, di), "Rack Volume");
        var gainRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 4, VerticalAlignment = VerticalAlignment.Center, Children = { IrCap("GAIN"), WithCol(gSlot, 1), WithCol(gainVal, 2) } };

        var macroMap = new Border { BorderBrush = IrTeal, BorderThickness = new Thickness(1), Background = new SolidColorBrush(Color.FromArgb(0x24, 0x5B, 0x9E, 0x9C)), CornerRadius = new CornerRadius(4), Padding = new Thickness(0, 3), Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock { Text = "MACRO MAP", FontSize = 8, FontWeight = FontWeight.Bold, Foreground = IrTeal, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center } };
        macroMap.PointerPressed += (_, _) => { _aeMacroMap = true; DeferRebuild(); };
        var pdc = AeRailToggle(E.RackDevPdc(T, di) ? "PDC ON" : "PDC OFF", E.RackDevPdc(T, di), () => { E.RackDevSetPdc(T, di, !E.RackDevPdc(T, di)); DeferRebuild(); });

        var fold = IrRailBtn(_aeFold ? "Unfold" : "Fold", () => { _aeFold = !_aeFold; DeferRebuild(); });
        var save = IrRailBtn("Save", () => { try { _ctx.RequestPresetSave(di); } catch { /* no-op */ } });
        var foldSave = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 4, [DockPanel.DockProperty] = Dock.Bottom, Children = { fold, WithCol(save, 1) } };

        var top = new StackPanel { Spacing = 6, [DockPanel.DockProperty] = Dock.Top, Children = {
            new StackPanel { Spacing = 3, Children = { IrCap("MODE"), modeSeg } },
            IrCap("RACK OUT"), dwCell, gainRow,
            new Border { Height = 1, Background = IrCard },
            macroMap, pdc } };
        var inner = new DockPanel { LastChildFill = false, Children = { top, foldSave } };
        return new Border { Width = 96, Background = IrRail, BorderBrush = IrBorder, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(8, 6), Child = inner };
    }

    // Full-width 3-way MODE segment (fits the 96px rail).
    private Control AeModeSeg(int mode, int di)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 1 };
        string[] names = { "Prl", "Ser", "Sel" };
        for (int i = 0; i < 3; i++)
        {
            int iv = i; bool on = i == mode;
            var cell = new Border { Background = on ? IrAmber : Brushes.Transparent, CornerRadius = new CornerRadius(2), Cursor = new Cursor(StandardCursorType.Hand),
                Child = new TextBlock { Text = names[i], FontSize = 8, FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal, Foreground = on ? new SolidColorBrush(Color.Parse("#171613")) : IrMuted, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, Padding = new Thickness(0, 1) } };
            cell.PointerPressed += (_, _) => { E.RackDevSetMode(T, di, iv); DeferRebuild(); };
            Grid.SetColumn(cell, i); g.Children.Add(cell);
        }
        return new Border { Background = IrInset, BorderBrush = IrBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(1), Child = g };
    }

    // A pill toggle (PDC): label on the left, switch on the right, fits the rail width.
    private Control AeRailToggle(string text, bool on, Action onClick)
    {
        var dot = new Border { Width = 18, Height = 10, CornerRadius = new CornerRadius(5), Background = on ? IrTeal : IrCard, BorderBrush = on ? Brushes.Transparent : new SolidColorBrush(Color.Parse("#3A362D")), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, [DockPanel.DockProperty] = Dock.Right,
            Child = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = on ? new SolidColorBrush(Color.Parse("#171613")) : IrMuted, HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(1.5, 0) } };
        var b = new Border { Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent, Child = new DockPanel { LastChildFill = true, Children = { dot,
            new TextBlock { Text = text, FontSize = 8, FontWeight = FontWeight.Bold, Foreground = on ? IrTeal : IrMuted, VerticalAlignment = VerticalAlignment.Center } } } };
        b.PointerPressed += (_, _) => onClick();
        return b;
    }

    // ---- Series mode: chains as stacked columns you build top → bottom ----
    private Control AeSeriesBody(IRackAccess a, int di)
    {
        var rail = AeRackRail(a, di);
        var cols = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        int chains = a.ChainCount();
        for (int c = 0; c < chains; c++) cols.Children.Add(AeSeriesColumn(a, di, c));
        var addCol = new Border { Width = 40, BorderBrush = new SolidColorBrush(Color.Parse("#3A362D")), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock { Text = "+", FontSize = 14, Foreground = IrMuted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        addCol.PointerPressed += (_, _) => { int nc = a.AddChain(-1); if (nc >= 0) Sel = nc; DeferRebuild(); };
        cols.Children.Add(addCol);
        var scroll = new ScrollViewer { Content = cols, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(8, 7) };
        DockPanel.SetDock(rail, Dock.Right);
        return new DockPanel { LastChildFill = true, Children = { rail, scroll } };
    }

    private Control AeSeriesColumn(IRackAccess a, int di, int c)
    {
        double db = a.ChainGain(c) <= 0.001 ? -60 : 20 * Math.Log10(a.ChainGain(c));
        var head = new DockPanel { LastChildFill = true, Children = {
            WithRight(IrMono(db <= -59 ? "−∞" : $"{db:+0.0;−0.0;0.0}", new SolidColorBrush(Color.Parse("#A39D8F")))),
            new TextBlock { Text = $"Chain {c + 1}", FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = IrTxt, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis } } };
        var stack = new StackPanel { Spacing = 4, Children = { head } };
        int dc = a.ChainDeviceCount(c);
        for (int d = 0; d < dc; d++) stack.Children.Add(AeSeriesDeviceRow(a, c, d));
        var addDev = new Border { BorderBrush = new SolidColorBrush(Color.Parse("#3A362D")), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(0, 2), Cursor = new Cursor(StandardCursorType.Hand), [DockPanel.DockProperty] = Dock.Bottom,
            Child = new TextBlock { Text = "+ device", FontSize = 8, Foreground = IrMuted, HorizontalAlignment = HorizontalAlignment.Center } };
        addDev.PointerPressed += (_, _) => ShowAddDeviceMenu(a, c, addDev);
        DockPanel.SetDock(addDev, Dock.Bottom);
        var inner = new DockPanel { LastChildFill = false, Children = { addDev, stack } };
        return new Border { Width = 150, Background = IrRail, BorderBrush = ChainHue(c), BorderThickness = new Thickness(0, 2, 0, 0), CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 5), Child = inner };
    }

    private Control AeSeriesDeviceRow(IRackAccess a, int c, int d)
    {
        bool byp = a.ChainDeviceBypassed(c, d);
        int mc = MacrosOnTarget(a, c, d);
        var rowInner = new DockPanel { LastChildFill = true, Children = {
            WithRight(SlotDelBtn(() => { a.RemoveChainDevice(c, d); DeferRebuild(); })),
            WithRight(new TextBlock { Text = "⠿", FontSize = 8, Foreground = IrMuted, VerticalAlignment = VerticalAlignment.Center }),
            WithRight(mc > 0 ? new TextBlock { Text = $"{mc}M", FontSize = 7, FontWeight = FontWeight.Bold, Foreground = IrTeal, VerticalAlignment = VerticalAlignment.Center } : (Control)new Border()),
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = {
                new Border { Width = 5, Height = 5, CornerRadius = new CornerRadius(3), Background = byp ? IrMuted : Success, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = a.ChainDeviceName(c, d), FontSize = 9, Foreground = IrTxt, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis } } } } };
        var row = new Border { Height = 24, Background = IrCard, BorderBrush = IrBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 0), Opacity = byp ? 0.6 : 1.0, Cursor = new Cursor(StandardCursorType.Hand), Child = rowInner };
        Border b = row; b.PointerPressed += (_, e) => { e.Handled = true; OpenChainDeviceGui(a, c, d, b); };
        return row;
    }

    // Drum Rack card, rebuilt to mockup 2q (700×260): a 4×4 hue-coded pad grid with a
    // selected-pad detail panel, or a Mixer view (every loaded pad as a row). A LIVE strip
    // carries bank / swing / humanize / fold. Pads still audition on click and accept drops.
    public Control BuildDrumRackCard()
    {
        var a = new InstrumentRackAccess(E, T);
        E.SetAuditionTrack(T);   // so clicking a pad plays it without arming
        int chains = a.ChainCount();
        Sel = chains > 0 ? Math.Clamp(Sel, 0, chains - 1) : 0;
        _irTick.Clear();

        var header = DrumHeader(a);
        var strip  = DrumLiveStrip(a);
        Control bodyView = _drumMixer ? DrumMixerView(a) : DrumPadsView(a);
        DockPanel.SetDock(header, Dock.Top); DockPanel.SetDock(strip, Dock.Top);
        var content = new DockPanel { LastChildFill = true, Children = { header, strip, bodyView } };
        if (_irTick.Count > 0) _ctx.AddDeviceRefresher(() => { for (int i = 0; i < _irTick.Count; i++) _irTick[i](); });
        return new Border { Width = 700, Height = 260, Background = new SolidColorBrush(Color.Parse("#171613")), BorderBrush = IrBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = content };
    }

    // ---- header (26): name · pad count · view toggle · meter ----
    private Control DrumHeader(IRackAccess a)
    {
        int pads = 0;
        for (int c = 0; c < a.ChainCount(); c++) if (a.ChainTriggerNote(c) >= 0) pads++;
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = {
            new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = Success, VerticalAlignment = VerticalAlignment.Center },
            new TextBlock { Text = "Nota Drum Rack", FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = IrTxt, VerticalAlignment = VerticalAlignment.Center },
            new TextBlock { Text = $"{pads} PAD{(pads == 1 ? "" : "S")}", FontSize = 9, FontWeight = FontWeight.Bold, Foreground = IrMuted, VerticalAlignment = VerticalAlignment.Center } } };

        var mL = new Border { Height = 3, Background = Success, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(2) };
        var mR = new Border { Height = 3, Background = Success, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(2) };
        var meter = new StackPanel { Width = 46, Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = {
            new Panel { Height = 3, Children = { new Border { Height = 3, Background = IrInset, CornerRadius = new CornerRadius(2) }, mL } },
            new Panel { Height = 3, Children = { new Border { Height = 3, Background = IrInset, CornerRadius = new CornerRadius(2) }, mR } } } };
        _irTick.Add(() => { float pk = 0; for (int c = 0; c < a.ChainCount(); c++) pk = Math.Max(pk, E.RackChainMeter(T, c)); double w = 46 * Math.Clamp(pk, 0, 1); mL.Width = w; mR.Width = w * 0.94; });

        var view = IrSeg(new[] { "Pads", "Mixer" }, _drumMixer ? 1 : 0, i => { _drumMixer = i == 1; _ctx.RequestRebuild(); });
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, [DockPanel.DockProperty] = Dock.Right, Children = { view, meter } };
        var dp = new DockPanel { LastChildFill = false, Margin = new Thickness(9, 0), Children = { left, right } };
        return new Border { Height = 26, Background = IrHdr, BorderBrush = IrBorder, BorderThickness = new Thickness(0, 0, 0, 1), Child = dp };
    }

    // ---- LIVE strip (34): bank · swing · humanize · fold ----
    private Control DrumLiveStrip(IRackAccess a)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(IrCap("BANK"));
        row.Children.Add(IrSeg(new[] { "C1", "C2", "C3", "C4" }, _drumBank, i => { _drumBank = i; _ctx.RequestRebuild(); }));
        row.Children.Add(DrumKitKnob("SWING", 52, IrAmber, () => E.RackSwing(T), v => E.RackSetSwing(T, v)));
        row.Children.Add(DrumKitKnob("HUMANIZE", 46, IrTeal, () => E.RackHumanize(T), v => E.RackSetHumanize(T, v)));
        var fold = DrumChip("Fold", _drumFold, () => { _drumFold = !_drumFold; _ctx.RequestRebuild(); });
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, [DockPanel.DockProperty] = Dock.Right, Children = { fold } };
        var dp = new DockPanel { LastChildFill = false, Margin = new Thickness(9, 0), Children = { row, right } };
        return new Border { Height = 34, Background = IrHdr, BorderBrush = IrBorder, BorderThickness = new Thickness(0, 0, 0, 1), Child = dp };
    }

    // A labelled kit slider (swing / humanize) with a fixed-width groove + live % readout.
    private Control DrumKitKnob(string label, double width, IBrush accent, Func<float> get, Action<float> set)
    {
        var val = IrMono("0 %", IrTxt, 9); val.MinWidth = 30;
        var slot = HSlider(width, accent, () => get(), v => { set((float)v); val.Text = $"{v * 100:0} %"; });
        val.Text = $"{get() * 100:0} %";
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = { IrCap(label), slot, val } };
    }

    private static Border DrumChip(string text, bool on, Action onClick)
    {
        var b = new Border { BorderThickness = new Thickness(1), BorderBrush = on ? IrTeal : new SolidColorBrush(Color.Parse("#3A362D")), Background = on ? new SolidColorBrush(Color.FromArgb(0x24, 0x5B, 0x9E, 0x9C)) : IrCard, CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 2), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = text, FontSize = 9, Foreground = on ? IrTeal : new SolidColorBrush(Color.Parse("#A39D8F")) } };
        b.PointerPressed += (_, e) => { e.Handled = true; onClick(); };
        return b;
    }

    // A fixed-width horizontal groove+fill+handle bound to a 0..1 getter/setter.
    private static Control HSlider(double width, IBrush accent, Func<double> get, Action<double> set)
    {
        var fill = new Border { Height = 3, Background = accent, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var handle = new Border { Width = 8, Height = 9, Background = new SolidColorBrush(Color.Parse("#A39D8F")), CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var slot = new Panel { Width = width, Height = 9, Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent, Children = { new Border { Height = 3, Background = IrInset, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center }, fill, handle } };
        void Vis(double v) { fill.Width = v * width; handle.Margin = new Thickness(Math.Clamp(v * width - 4, 0, Math.Max(0, width - 8)), 0, 0, 0); }
        bool drag = false;
        void SetX(double x) { double v = Math.Clamp(x / width, 0, 1); set(v); Vis(v); }
        slot.PointerPressed += (_, e) => { drag = true; e.Pointer.Capture(slot); SetX(e.GetPosition(slot).X); };
        slot.PointerMoved += (_, e) => { if (drag) SetX(e.GetPosition(slot).X); };
        slot.PointerReleased += (_, e) => { if (drag) { drag = false; e.Pointer.Capture(null); } };
        Vis(Math.Clamp(get(), 0, 1));
        return slot;
    }

    // ==================== Pads view ====================
    private Control DrumPadsView(IRackAccess a)
    {
        var grid = DrumPadGrid(a);
        var panel = DrumSelectedPanel(a);
        DockPanel.SetDock(grid, Dock.Left);
        return new DockPanel { LastChildFill = true, Children = { grid, panel } };
    }

    private Control DrumPadGrid(IRackAccess a)
    {
        int bankBase = 36 + _drumBank * 16;
        var notes = new System.Collections.Generic.List<int>();
        // Hardware order: pad 1 bottom-left, ascending left→right, bottom→top.
        foreach (int rowBase in new[] { bankBase + 12, bankBase + 8, bankBase + 4, bankBase })
            for (int col = 0; col < 4; col++) notes.Add(rowBase + col);
        if (_drumFold) notes = notes.FindAll(nn => ChainForNote(a, nn) >= 0);

        var uni = new UniformGrid { Columns = 4, Rows = _drumFold ? Math.Max(1, (notes.Count + 3) / 4) : 4 };
        foreach (int nn in notes) uni.Children.Add(DrumPad(a, nn));
        return new Border { Width = 314, Padding = new Thickness(8, 7), Child = uni };
    }

    private Control DrumPad(IRackAccess a, int note)
    {
        int chain = ChainForNote(a, note);
        bool filled = chain >= 0;
        bool selected = filled && chain == Sel;
        Color hue = filled ? PadHue(chain) : Colors.Black;
        int choke = filled ? E.RackChainChoke(T, chain) : 0;
        string name = filled ? a.ChainInstrumentName(chain) : NoteName(note);

        var nameTb = new TextBlock { Text = name, FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = filled ? IrTxt : IrMuted, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Top };
        var noteTb = IrMono(NoteName(note), filled ? new SolidColorBrush(Color.Parse("#A39D8F")) : IrMuted, 7);
        var chokeTb = IrMono(choke > 0 ? $"CH {choke}" : "", IrTeal, 7);
        var bottom = new DockPanel { LastChildFill = false, VerticalAlignment = VerticalAlignment.Bottom, Children = { noteTb, WithRight(chokeTb) } };
        var cell = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(5, 4) };
        cell.Children.Add(nameTb);
        Grid.SetRow(bottom, 2); cell.Children.Add(bottom);

        IBrush LoadedBg() => filled ? PadFill(hue, 0.16) : IrCard;
        IBrush SelBg() => PadFill(hue, 0.28);
        IBrush idleBorder = selected ? AccentBright : (filled ? PadFill(hue, 0.5) : IrBorder);
        double idleThick = selected ? 2 : 1;
        var pad = new Border { Margin = new Thickness(2), Background = selected ? SelBg() : LoadedBg(), BorderBrush = idleBorder, BorderThickness = new Thickness(idleThick), CornerRadius = new CornerRadius(5), Cursor = new Cursor(StandardCursorType.Hand), Child = cell };
        ToolTip.SetTip(pad, filled ? $"{name} · {NoteName(note)} — click to play + edit" : $"{NoteName(note)} — click to add · drop a sample here");

        if (filled)
        {
            // Live: brighten the pad while it's sounding (meter > floor).
            _irTick.Add(() => {
                bool hot = E.RackChainMeter(T, chain) > 0.02f;
                pad.Background = hot ? PadFill(hue, 0.42) : (chain == Sel ? SelBg() : LoadedBg());
                pad.BorderBrush = hot ? AccentBright : (chain == Sel ? AccentBright : PadFill(hue, 0.5));
            });
            pad.PointerPressed += (_, e) => {
                var pt = e.GetCurrentPoint(pad).Properties;
                if (pt.IsRightButtonPressed) { e.Handled = true; SelectPad(chain); return; }
                if (pt.IsLeftButtonPressed) { e.Handled = true; e.Pointer.Capture(pad); E.NoteOn(note, 1.0f); }
            };
            pad.PointerReleased += (_, e) => { E.NoteOff(note); e.Pointer.Capture(null); SelectPad(chain); };
        }
        else
        {
            pad.PointerPressed += (_, e) => { e.Handled = true; ShowAddPadMenu(a, pad, note); };
        }

        DragDrop.SetAllowDrop(pad, true);
        DragDrop.AddDragOverHandler(pad, (_, e) => {
            if (!BrowserView.IsAcceptableDrag(e)) { e.DragEffects = DragDropEffects.None; return; }
            e.DragEffects = DragDropEffects.Copy; e.Handled = true; _ctx.HideDropGlow();
            pad.BorderBrush = AccentBright; pad.BorderThickness = new Thickness(2);
        });
        DragDrop.AddDragLeaveHandler(pad, (_, _) => { pad.BorderBrush = idleBorder; pad.BorderThickness = new Thickness(idleThick); });
        DragDrop.AddDropHandler(pad, (_, e) => {
            pad.BorderBrush = idleBorder; pad.BorderThickness = new Thickness(idleThick);
            var items = BrowserView.DroppedItems(e);
            if (items.Count == 0) return;
            int bankTop = (36 + _drumBank * 16) + 16;
            for (int i = 0; i < items.Count && note + i < bankTop; i++) DropOnPad(a, note + i, items[i]);
            e.Handled = true;
        });
        return pad;
    }

    // ---- selected-pad detail panel ----
    private Control DrumSelectedPanel(IRackAccess a)
    {
        var panel = new Border { Background = new SolidColorBrush(Color.Parse("#1B1916")), BorderBrush = IrBorder, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(9, 6) };
        int chains = a.ChainCount();
        if (chains == 0 || Sel < 0 || Sel >= chains || a.ChainTriggerNote(Sel) < 0)
        {
            panel.Child = new TextBlock { Text = "Select a pad to edit it.", FontSize = 10, Foreground = IrMuted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            return panel;
        }
        int c = Sel, note = a.ChainTriggerNote(c);
        int devs = a.ChainDeviceCount(c);
        int choke = E.RackChainChoke(T, c);
        Color hue = PadHue(c);

        var head = new DockPanel { LastChildFill = false, Children = {
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = {
                new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(hue), VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = a.ChainInstrumentName(c), FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = IrTxt, VerticalAlignment = VerticalAlignment.Center },
                IrMono($"{NoteName(note)} · {(choke > 0 ? $"choke CH {choke}" : "no choke")} · {devs} dev", IrMuted) } },
            WithRight(DrumOpenChip(a, c)) } };

        var col = new StackPanel { Spacing = 5 };
        col.Children.Add(head);
        col.Children.Add(DrumSampleBox(a, c, hue));
        col.Children.Add(DrumParamRow("VOLUME", () => VolNorm(a.ChainGain(c)), v => a.SetChainGain(c, NormVol(v)), () => VolText(a.ChainGain(c))));
        col.Children.Add(DrumParamRow("PAN", () => (a.ChainPan(c) + 1) / 2, v => a.SetChainPan(c, (float)(v * 2 - 1)), () => PanText(a.ChainPan(c))));
        col.Children.Add(DrumParamRow("TUNE", () => (E.RackChainTune(T, c) + 48) / 96.0, v => E.RackSetChainTune(T, c, (int)Math.Round(v * 96 - 48)), () => $"{E.RackChainTune(T, c):+0;-0;0} st"));
        col.Children.Add(DrumParamRow("DECAY", () => E.RackChainDecay(T, c), v => E.RackSetChainDecay(T, c, (float)v), () => DecayText(E.RackChainDecay(T, c))));

        var chokeLeft = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = {
            IrCap("CHOKE"),
            IrSeg(new[] { "Off", "1", "2", "3", "4" }, choke, i => { E.RackSetChainChoke(T, c, i); _ctx.RequestRebuild(); }) } };
        var samplerChip = DrumChip("Sampler", false, () => OpenChainInstrumentGui(a, c, panel));
        var chokeRow = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 3, 0, 0), Children = { chokeLeft, WithRight(samplerChip) } };
        DockPanel.SetDock(chokeRow, Dock.Bottom);
        panel.Child = new DockPanel { LastChildFill = true, Children = { chokeRow, col } };
        return panel;
    }

    private Border DrumOpenChip(IRackAccess a, int c)
    {
        Border b = null!;
        b = new Border { Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "▤ open chain", FontSize = 8, Foreground = IrMuted } };
        b.PointerPressed += (_, e) => { e.Handled = true; OpenChainInstrumentGui(a, c, b); };
        return b;
    }

    private Control DrumSampleBox(IRackAccess a, int c, Color hue)
    {
        var title = IrMono("", IrTxt, 8);
        var dur = IrMono("", IrMuted, 8);
        var bars = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        float[] peaks = Array.Empty<float>();
        if (E.RackChainSamplerInfo(T, c, out var si) && si.SampleId != 0 && E.TryGetSampleInfo(si.SampleId, out var sinfo))
        {
            float[] raw = E.ReadSample(si.SampleId);
            int ch = Math.Max(1, sinfo.Channels); long frames = sinfo.Frames; double sr = sinfo.SampleRate;
            peaks = MiniPeaks(raw, ch, frames, 40);
            title.Text = a.ChainInstrumentName(c);
            dur.Text = sr > 0 ? $"{frames / sr:0.00} s" : "";
        }
        else { title.Text = a.ChainInstrumentName(c); dur.Text = "synth"; }

        if (peaks.Length > 0)
            foreach (float p in peaks)
                bars.Children.Add(new Border { Width = 3, Height = Math.Max(1, p * 30), Background = new SolidColorBrush(hue), Opacity = 0.55 + 0.45 * p, CornerRadius = new CornerRadius(1), VerticalAlignment = VerticalAlignment.Center });
        else
            bars.Children.Add(new TextBlock { Text = "— no sample —", FontSize = 8, Foreground = IrMuted, VerticalAlignment = VerticalAlignment.Center });

        var top = new DockPanel { LastChildFill = false, Children = { title, WithRight(dur) } };
        var inner = new DockPanel { LastChildFill = true, Children = { top } };
        DockPanel.SetDock(top, Dock.Top);
        inner.Children.Add(new Border { Child = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = bars }, VerticalAlignment = VerticalAlignment.Center });
        return new Border { Height = 56, Background = IrInset, BorderBrush = IrField, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 4), Child = inner };
    }

    // A param row: label (44) · flexible groove · value (48). Synced via the 60 Hz tick.
    private Control DrumParamRow(string label, Func<double> get, Action<double> set, Func<string> disp)
    {
        var lbl = new TextBlock { Text = label, FontSize = 8, FontWeight = FontWeight.Bold, Foreground = IrMuted, Width = 44, VerticalAlignment = VerticalAlignment.Center };
        var val = IrMono(disp(), IrTxt, 9); val.Width = 48; val.TextAlignment = TextAlignment.Right;
        var fill = new Border { Height = 3, Background = IrAmber, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var handle = new Border { Width = 8, Height = 9, Background = new SolidColorBrush(Color.Parse("#A39D8F")), CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var slot = new Panel { Height = 9, Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent, Children = { new Border { Height = 3, Background = IrInset, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center }, fill, handle } };
        void Vis(double v) { double W = slot.Bounds.Width; fill.Width = v * W; handle.Margin = new Thickness(Math.Clamp(v * W - 4, 0, Math.Max(0, W - 8)), 0, 0, 0); }
        bool drag = false;
        void SetX(double x) { double v = Math.Clamp(x / Math.Max(1, slot.Bounds.Width), 0, 1); set(v); Vis(v); val.Text = disp(); }
        slot.PointerPressed += (_, e) => { drag = true; e.Pointer.Capture(slot); SetX(e.GetPosition(slot).X); };
        slot.PointerMoved += (_, e) => { if (drag) SetX(e.GetPosition(slot).X); };
        slot.PointerReleased += (_, e) => { if (drag) { drag = false; e.Pointer.Capture(null); } };
        _irTick.Add(() => { if (!drag) { Vis(Math.Clamp(get(), 0, 1)); val.Text = disp(); } });
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(slot, 1); Grid.SetColumn(val, 2);
        g.Children.Add(lbl); g.Children.Add(slot); g.Children.Add(val);
        return g;
    }

    // ==================== Mixer view ====================
    private Control DrumMixerView(IRackAccess a)
    {
        var pads = new System.Collections.Generic.List<int>();
        for (int c = 0; c < a.ChainCount(); c++) if (a.ChainTriggerNote(c) >= 0) pads.Add(c);
        pads.Sort((x, y) => a.ChainTriggerNote(x).CompareTo(a.ChainTriggerNote(y)));

        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("96,26,*,40,44"), ColumnSpacing = 6, Height = 22, Margin = new Thickness(9, 0) };
        void H(string t, int col, IBrush? cc = null) { var x = new TextBlock { Text = t, FontSize = 8, FontWeight = FontWeight.Bold, Foreground = cc ?? IrMuted, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(x, col); head.Children.Add(x); }
        H("PAD", 0); H("NOTE", 1); H("VOLUME", 2); H("PAN", 3); H("M S CHK", 4);
        var headBar = new Border { Height = 22, Background = new SolidColorBrush(Color.Parse("#1B1916")), BorderBrush = IrBorder, BorderThickness = new Thickness(0, 0, 0, 1), Child = head };

        var rows = new StackPanel { Spacing = 2 };
        foreach (int c in pads) rows.Children.Add(DrumMixerRow(a, c));
        if (pads.Count == 0) rows.Children.Add(new TextBlock { Text = "No pads loaded — drop samples on the Pads view.", FontSize = 10, Foreground = IrMuted, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });
        var scroll = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(9, 4, 9, 4) };
        DockPanel.SetDock(headBar, Dock.Top);
        return new DockPanel { LastChildFill = true, Children = { headBar, scroll } };
    }

    private Control DrumMixerRow(IRackAccess a, int c)
    {
        int note = a.ChainTriggerNote(c);
        Color hue = PadHue(c);
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("96,26,*,40,44"), ColumnSpacing = 6, Height = 13 };
        var nameCell = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = {
            new Border { Width = 4, Height = 10, CornerRadius = new CornerRadius(1), Background = new SolidColorBrush(hue), VerticalAlignment = VerticalAlignment.Center },
            new TextBlock { Text = a.ChainInstrumentName(c), FontSize = 9, Foreground = c == Sel ? IrAmberLit : IrTxt, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center } } };
        nameCell.PointerPressed += (_, _) => SelectPad(c);
        Grid.SetColumn(nameCell, 0); g.Children.Add(nameCell);
        var noteTb = IrMono(NoteName(note), IrMuted); Grid.SetColumn(noteTb, 1); g.Children.Add(noteTb);

        var fill = new Border { Height = 4, Background = IrAmber, Opacity = 0.75, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var handle = new Border { Width = 7, Height = 10, Background = new SolidColorBrush(Color.Parse("#A39D8F")), CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var vslot = new Panel { Height = 10, Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent, Children = { new Border { Height = 4, Background = IrInset, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center }, fill, handle } };
        void Vis(double v) { double W = vslot.Bounds.Width; fill.Width = v * W; handle.Margin = new Thickness(Math.Clamp(v * W - 3, 0, Math.Max(0, W - 7)), 0, 0, 0); }
        bool drag = false;
        void SetX(double x) { double v = Math.Clamp(x / Math.Max(1, vslot.Bounds.Width), 0, 1); a.SetChainGain(c, NormVol(v)); Vis(v); }
        vslot.PointerPressed += (_, e) => { drag = true; e.Pointer.Capture(vslot); SetX(e.GetPosition(vslot).X); };
        vslot.PointerMoved += (_, e) => { if (drag) SetX(e.GetPosition(vslot).X); };
        vslot.PointerReleased += (_, e) => { if (drag) { drag = false; e.Pointer.Capture(null); } };
        _irTick.Add(() => { if (!drag) Vis(VolNorm(a.ChainGain(c))); });
        MidiLearn.Bind(vslot, MidiTarget.RackChainGain(T, a.AutomationDeviceIndex, c), "Pad Volume");
        Grid.SetColumn(vslot, 2); g.Children.Add(vslot);

        var panTb = IrMono(PanText(a.ChainPan(c)), new SolidColorBrush(Color.Parse("#A39D8F"))); panTb.TextAlignment = TextAlignment.Center; Grid.SetColumn(panTb, 3); g.Children.Add(panTb);

        int choke = E.RackChainChoke(T, c);
        var padMute = IrMsBtn("M", a.ChainMute(c), IrRed, () => { a.SetChainMute(c, !a.ChainMute(c)); _ctx.RequestRebuild(); });
        var padSolo = IrMsBtn("S", a.ChainSolo(c), IrAmber, () => { a.SetChainSolo(c, !a.ChainSolo(c)); _ctx.RequestRebuild(); });
        MidiLearn.Bind(padMute, MidiTarget.RackChainMute(T, a.AutomationDeviceIndex, c), "Pad Mute");
        MidiLearn.Bind(padSolo, MidiTarget.RackChainSolo(T, a.AutomationDeviceIndex, c), "Pad Solo");
        var msc = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = {
            padMute, padSolo,
            IrMsBtn(choke > 0 ? choke.ToString() : "·", choke > 0, IrTeal, () => { E.RackSetChainChoke(T, c, (choke + 1) % 5); _ctx.RequestRebuild(); }) } };
        Grid.SetColumn(msc, 4); g.Children.Add(msc);
        return g;
    }

    // ---- drum helpers: pad hue + value mapping + mini peaks ----
    private static Color PadHue(int chain) => NotaPalette.TrackColors[((chain % 8) + 8) % 8];
    private static IBrush PadFill(Color c, double alpha) => new SolidColorBrush(Color.FromArgb((byte)(Math.Clamp(alpha, 0, 1) * 255), c.R, c.G, c.B));
    private static double VolNorm(float gain) { double db = gain <= 0.001 ? -60 : 20 * Math.Log10(gain); return Math.Clamp((db + 60) / 66.0, 0, 1); }
    private static float NormVol(double v) { double db = -60 + v * 66; return (float)Math.Pow(10, db / 20); }
    private static string VolText(float gain) { double db = gain <= 0.001 ? -60 : 20 * Math.Log10(gain); return db <= -59 ? "−∞" : $"{db:+0.0;−0.0;0.0} dB"; }
    private static string PanText(float pan) { int p = (int)Math.Round(pan * 50); return p == 0 ? "C" : (p < 0 ? $"{-p}L" : $"{p}R"); }
    private static string DecayText(float d) => d >= 0.999f ? "hold" : $"{20 * Math.Pow(100, d):0} ms";
    private static float[] MiniPeaks(float[] raw, int ch, long frames, int buckets)
    {
        var o = new float[buckets];
        if (frames <= 0 || raw.Length == 0) return o;
        long per = Math.Max(1, frames / buckets);
        for (int b = 0; b < buckets; b++)
        {
            long s = (long)b * per; float mx = 0;
            for (long i = 0; i < per; i++) { long f = s + i; if (f >= frames) break; float v = 0; for (int cc = 0; cc < ch; cc++) v += Math.Abs(raw[f * ch + cc]); v /= ch; if (v > mx) mx = v; }
            o[b] = mx;
        }
        float peak = 0; foreach (float v in o) peak = Math.Max(peak, v);
        if (peak > 0) for (int b = 0; b < buckets; b++) o[b] /= peak;
        return o;
    }

    // ---- rack body (macros + chains), reused by both rack types ------------
    // The 4×2 macro grid column, shared by the rack card and the drum-rack card.
    private Control MacroColumn(IRackAccess a)
    {
        var macroGrid = new StackPanel { Spacing = 7 };
        for (int r = 0; r < 4; r++)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(MacroCell(a, r * 2));
            row.Children.Add(MacroCell(a, r * 2 + 1));
            macroGrid.Children.Add(row);
        }
        return new StackPanel { Width = 210, Spacing = 8, Children = { SectionLabel("MACROS"), macroGrid } };
    }

    // Rack layout: macros | chain list | the selected chain's device chain.
    private Control RackBody(IRackAccess a)
    {
        int chains = a.ChainCount();
        Sel = chains > 0 ? Math.Clamp(Sel, 0, chains - 1) : 0;

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 14 };
        var macros = MacroColumn(a);
        var list = ChainListColumn(a);
        var devices = DeviceArea(a, Sel);
        body.Children.Add(macros);
        Grid.SetColumn(list, 1); body.Children.Add(list);
        Grid.SetColumn(devices, 2); body.Children.Add(devices);
        return body;
    }

    // ---- chain list (left): selectable rows with gain/pan/M/S -------------
    private Control ChainListColumn(IRackAccess a)
    {
        var listPanel = new StackPanel { Spacing = 5 };
        int chains = a.ChainCount();
        for (int c = 0; c < chains; c++) listPanel.Children.Add(ChainRow(a, c));
        listPanel.Children.Add(AddChainButton(a));

        var col = new Grid { Width = 182, RowDefinitions = new RowDefinitions("Auto,*") };
        col.Children.Add(SectionLabel("CHAINS"));
        var scroll = new ScrollViewer
        {
            Content = listPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 0, 11, 0),   // reserve room so the thin scrollbar never floats over the ✕ buttons
        };
        Grid.SetRow(scroll, 1); col.Children.Add(scroll);
        return col;
    }

    private Control ChainRow(IRackAccess a, int c)
    {
        bool sel = c == Sel;
        string name = a.HasInstrumentChains ? a.ChainInstrumentName(c) : $"Chain {c + 1}";
        if (string.IsNullOrEmpty(name)) name = $"Chain {c + 1}";

        var nameTb = new TextBlock { Text = name, FontSize = 10, FontWeight = FontWeight.SemiBold, Foreground = sel ? AccentBright : TextPrimary, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var rm = Glyph("✕", true, () => { a.RemoveChain(c); if (Sel > 0) Sel--; _ctx.RequestRebuild(); });
        ToolTip.SetTip(rm, "Remove chain");
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        head.Children.Add(nameTb);
        Grid.SetColumn(rm, 1); head.Children.Add(rm);

        var gain = new MiniFader(a.ChainGain(c), 2.0) { Accent = true, Width = 58 };
        gain.ValueChanged += v => a.SetChainGain(c, (float)v);
        var pan = new MiniFader(a.ChainPan(c) * 0.5 + 0.5, 1.0) { Width = 40 };
        pan.ValueChanged += v => a.SetChainPan(c, (float)(v * 2.0 - 1.0));
        var m = Toggle("M", a.ChainMute(c), Danger, () => { a.SetChainMute(c, !a.ChainMute(c)); _ctx.RequestRebuild(); });
        var s = Toggle("S", a.ChainSolo(c), Brass, () => { a.SetChainSolo(c, !a.ChainSolo(c)); _ctx.RequestRebuild(); });
        var ctrls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Children = { gain, pan, m, s } };

        var row = new Border
        {
            Background = sel ? RowSel : Card2, BorderBrush = sel ? Brass : BorderDef, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 5), Cursor = new Cursor(StandardCursorType.Hand),
            Child = new StackPanel { Spacing = 5, Children = { head, ctrls } },
        };
        // Click the row (not its controls, which consume the press) to select it.
        row.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(row).Properties.IsLeftButtonPressed && Sel != c) { Sel = c; _ctx.RequestRebuild(); }
        };
        // Drop an effect onto a chain to append it (and select the chain).
        DragDrop.SetAllowDrop(row, true);
        DragDrop.AddDragOverHandler(row, (_, e) =>
        {
            if (!BrowserView.IsBrowserDrag || !IsEffect(BrowserView.CurrentDrag)) { e.DragEffects = DragDropEffects.None; return; }
            e.DragEffects = DragDropEffects.Copy; e.Handled = true; _ctx.HideDropGlow();
            row.BorderBrush = AccentBright; row.BorderThickness = new Thickness(2);
        });
        DragDrop.AddDragLeaveHandler(row, (_, _) => { row.BorderBrush = sel ? Brass : BorderDef; row.BorderThickness = new Thickness(1); });
        DragDrop.AddDropHandler(row, (_, e) =>
        {
            row.BorderBrush = sel ? Brass : BorderDef; row.BorderThickness = new Thickness(1);
            if (BrowserView.CurrentDrag is { } it && IsEffect(it)) { Sel = c; DropEffectOnChain(a, c, it); e.Handled = true; }
        });
        return row;
    }

    // ---- device area (right): the selected chain's instrument + effects ----
    private Control DeviceArea(IRackAccess a, int sc, string? title = null)
    {
        var col = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), MinWidth = 300 };
        if (a.ChainCount() == 0)
        {
            col.Children.Add(SectionLabel(title ?? "DEVICES"));
            var hint = new TextBlock { Text = "Add a chain to build its device chain.", FontSize = 10, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetRow(hint, 1); col.Children.Add(hint);
            return col;
        }
        col.Children.Add(SectionLabel(title ?? $"CHAIN {sc + 1} DEVICES"));
        var rowSp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (a.HasInstrumentChains) rowSp.Children.Add(ChainInstrumentCard(a, sc));
        int dc = a.ChainDeviceCount(sc);
        for (int d = 0; d < dc; d++) rowSp.Children.Add(ChainDeviceCard(a, sc, d));
        rowSp.Children.Add(ChainAddDeviceCard(a, sc));
        var scroll = new ScrollViewer
        {
            Content = rowSp, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        Grid.SetRow(scroll, 1); col.Children.Add(scroll);
        return col;
    }

    private Control ChainInstrumentCard(IRackAccess a, int sc)
    {
        int ik = a.ChainInstrumentKind(sc);
        string nm = a.ChainInstrumentName(sc);
        if (string.IsNullOrEmpty(nm)) nm = "Instrument";

        // Compact knob card (like the effect cards). The full bespoke UI opens in a pop-out
        // window via the header's "Full" button — the tabbed Nota Sampler editor for the
        // built-in Sampler, the plug-in's native GUI for a hosted instrument, or a roomy
        // knob grid for everything else.
        var body = new StackPanel { Spacing = 8 };
        if (ik == -1)
            body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { ActionChip("Save preset", () => _ctx.RequestRackPresetSave(sc)) } });
        int pc = a.ChainInstrumentParamCount(sc);
        if (pc == 0) body.Children.Add(new TextBlock { Text = "No parameters", FontSize = 9, Foreground = TextTertiary });
        for (int p = 0; p < pc; p++) body.Children.Add(RackInstParamRow(a, sc, p));

        Control fullBtn;
        if (ik == -1) fullBtn = ActionChip("Full", () => { try { E.RackOpenChainInstrumentEditor(T, sc); } catch { /* no-op */ } });
        else if (ik == 1) { Border b = null!; b = ActionChip("Full", () => OpenChainSamplerWindow(b, sc)); fullBtn = b; }
        else fullBtn = FullUiChip(nm, _ => ParamGridContent(nm, a.ChainInstrumentParamCount(sc), p => RackInstParamRow(a, sc, p)));
        return DeviceCardShell(nm, fullBtn, body, 180);
    }

    // A roomy, non-bespoke "full UI": every param as a knob in a 3-column grid.
    private Control ParamGridContent(string title, int count, Func<int, Control> row)
    {
        var wrap = new WrapPanel { MaxWidth = 456 };
        for (int p = 0; p < count; p++) wrap.Children.Add(new Border { Width = 148, Margin = new Thickness(2, 3), Child = row(p) });
        var scroll = new ScrollViewer { Content = wrap, MaxHeight = 360, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        return new Border { Width = 476, Background = new SolidColorBrush(Color.Parse("#171613")), Padding = new Thickness(10), Child =
            new StackPanel { Spacing = 7, Children = { new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary }, scroll } } };
    }

    // A small header button that pops out a device's full UI in a floating window.
    private Border FullUiChip(string title, Func<Action<Action>, Control> build)
    {
        Border chip = null!;
        chip = ActionChip("Full", () => OpenFullUiWindow(chip, title, build));
        return chip;
    }

    private void OpenFullUiWindow(Control anchor, string title, Func<Action<Action>, Control> build)
    {
        var ticks = new System.Collections.Generic.List<Action>();
        var content = build(ticks.Add);
        var win = new NotaPopupWindow
        {
            Title = title, SizeToContent = SizeToContent.WidthAndHeight, CanResize = false,
            ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        win.SetContent(content);
        Avalonia.Threading.DispatcherTimer? timer = null;
        if (ticks.Count > 0)
        {
            timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            timer.Tick += (_, _) => { for (int i = 0; i < ticks.Count; i++) ticks[i](); };
        }
        win.Opened += (_, _) => timer?.Start();
        win.Closed += (_, _) => timer?.Stop();
        ShowPopup(win, anchor);
    }

    // Show a rack full-UI/params popup over its owner and, if that owner is the main
    // window, wire the popup's MIDI-learn glass to the shared service so its controls
    // can be highlighted/selected while learn is armed.
    private static void ShowPopup(NotaPopupWindow win, Control anchor)
    {
        if (TopLevel.GetTopLevel(anchor) is Window owner)
        {
            if (owner is MainWindow mw) win.EnableMidiLearn(mw.LearnService);
            win.Show(owner);
        }
        else win.Show();
    }

    private Control ChainDeviceCard(IRackAccess a, int sc, int d)
    {
        bool byp = a.ChainDeviceBypassed(sc, d);
        int dc = a.ChainDeviceCount(sc);
        var ctrls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        string dnm = a.ChainDeviceName(sc, d);
        // Full UI: hosted-plugin effect → its native window; built-in effect → roomy knob grid.
        ctrls.Children.Add(a.ChainDeviceBuiltinKind(sc, d) < 0
            ? ActionChip("Full", () => a.OpenChainDeviceEditor(sc, d))
            : FullUiChip(dnm, _ => ParamGridContent(dnm, a.ChainDeviceParamCount(sc, d), p => RackDeviceParamRow(a, sc, d, p))));
        ctrls.Children.Add(Glyph("◀", d > 0, () => { a.MoveChainDevice(sc, d, d - 1); _ctx.RequestRebuild(); }));
        ctrls.Children.Add(Glyph("▶", d < dc - 1, () => { a.MoveChainDevice(sc, d, d + 1); _ctx.RequestRebuild(); }));
        ctrls.Children.Add(Glyph("✕", true, () => { a.RemoveChainDevice(sc, d); _ctx.RequestRebuild(); }));

        var body = new StackPanel { Spacing = 7 };
        body.Children.Add(Toggle(byp ? "BYPASSED" : "ACTIVE", !byp, Success, () => { a.SetChainDeviceBypassed(sc, d, !byp); _ctx.RequestRebuild(); }));
        int pc = a.ChainDeviceParamCount(sc, d);
        for (int p = 0; p < pc; p++) body.Children.Add(RackDeviceParamRow(a, sc, d, p));
        var card = DeviceCardShell(a.ChainDeviceName(sc, d), ctrls, body, 180);
        card.Opacity = byp ? 0.6 : 1.0;
        return card;
    }

    private Control ChainAddDeviceCard(IRackAccess a, int sc)
    {
        var content = new StackPanel
        {
            Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = "＋", FontSize = 16, Foreground = TextTertiary, HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = "Add device", FontSize = 10, Foreground = TextTertiary, HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = "or drop a plug-in", FontSize = 8, Foreground = TextDisabled, HorizontalAlignment = HorizontalAlignment.Center },
            },
        };
        var dash = new Rectangle { Stroke = BorderStrong, StrokeThickness = 1, StrokeDashArray = new AvaloniaList<double>(4, 3), RadiusX = 6, RadiusY = 6 };
        var host = new Grid { Width = 116, Children = { dash, content }, Cursor = new Cursor(StandardCursorType.Hand) };
        host.PointerPressed += (_, _) => ShowAddDeviceMenu(a, sc, host);
        // Also a drop target for effects (built-in / plug-in) → append to this chain.
        DragDrop.SetAllowDrop(host, true);
        DragDrop.AddDragOverHandler(host, (_, e) =>
        {
            if (!BrowserView.IsBrowserDrag || !IsEffect(BrowserView.CurrentDrag)) { e.DragEffects = DragDropEffects.None; return; }
            e.DragEffects = DragDropEffects.Copy; e.Handled = true; _ctx.HideDropGlow();
            dash.Stroke = AccentBright;
        });
        DragDrop.AddDragLeaveHandler(host, (_, _) => dash.Stroke = BorderStrong);
        DragDrop.AddDropHandler(host, (_, e) =>
        {
            dash.Stroke = BorderStrong;
            if (BrowserView.CurrentDrag is { } it && IsEffect(it)) { DropEffectOnChain(a, sc, it); e.Handled = true; }
        });
        return host;
    }

    // A compact device-area card: 24px header (title + optional controls) over a
    // scrollable body.
    private Border DeviceCardShell(string title, Control? headerRight, Control body, double width)
    {
        var titleTb = new TextBlock { Text = title, FontSize = 10, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        headerGrid.Children.Add(titleTb);
        if (headerRight != null) { Grid.SetColumn(headerRight, 1); headerGrid.Children.Add(headerRight); }
        var headerBar = new Border { Height = 24, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(7, 0), Child = headerGrid };
        var bodyHost = new Border
        {
            Padding = new Thickness(8),
            Child = new ScrollViewer
            {
                Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(0, 0, 10, 0),   // reserve room so the thin scrollbar never floats over the M map chips
            },
        };
        var stack = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        stack.Children.Add(headerBar);
        Grid.SetRow(bodyHost, 1); stack.Children.Add(bodyHost);
        return new Border
        {
            Width = width, Background = Card2, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = stack,
        };
    }

    // ---- macros -----------------------------------------------------------
    // A macro rides the rack's plugin-param surface (index == macro), so setting
    // it applies mappings AND it automates/records like any param.
    private Control MacroCell(IRackAccess a, int m)
    {
        int di = a.AutomationDeviceIndex;
        float v = E.PluginParamGet(T, di, m);
        var val = new TextBlock { Text = Pct(v), FontSize = 9, Foreground = TextPrimary, HorizontalAlignment = HorizontalAlignment.Right };
        val.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        top.Children.Add(new TextBlock { Text = "M" + (m + 1), FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = TextTertiary });
        Grid.SetColumn(val, 1); top.Children.Add(val);

        var bar = new Knob(v, 1.0) { Accent = true, HorizontalAlignment = HorizontalAlignment.Center };
        string pid = "macro" + (m + 1);
        bar.ValueChanged += x =>
        {
            E.PluginParamSet(T, di, m, (float)x); val.Text = Pct((float)x);
            _ctx.InvokeRackParamRefreshers();   // mapped param faders follow the macro live
        };
        bar.GestureBegin += () => E.BeginAutomationWrite(T, AutomationTarget.PluginParam, di, -1, pid);
        bar.GestureEnd += () => E.EndAutomationWrite(T, AutomationTarget.PluginParam, di, -1, pid);
        // A macro rides the rack's plugin-param surface, so it's MIDI-learnable like any
        // plugin param — this is the in-model path for driving nested chain params over MIDI
        // (map a param to a macro, then learn the macro).
        MidiLearn.Bind(bar, MidiTarget.PluginParam(T, di, m), "Macro " + (m + 1));
        if (di < 0) _ctx.AddInstFader(m, bar, val);   // live-refresh follows the instrument-rack macros

        var host = new StackPanel { Width = 92, Spacing = 2, Children = { top, bar, MappingLabel(a, m) } };
        host.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(host).Properties.IsRightButtonPressed) { e.Handled = true; ShowMacroMenu(a, host, m); }
        };
        ToolTip.SetTip(host, "Drag to set · right-click to map");
        return host;
    }

    private Control MappingLabel(IRackAccess a, int m)
    {
        string text = "";
        int count = 0, lastChain = -1, lastDev = -2, lastParam = -1;
        int n = a.MappingCount();
        for (int i = 0; i < n; i++)
            if (a.TryGetMapping(i, out var mm) && mm.Macro == m)
            { count++; lastChain = mm.Chain; lastDev = mm.DeviceIndex; lastParam = mm.ParamIndex; }
        if (count == 1)
        {
            string pn = lastDev < 0
                ? a.ChainInstrumentParamName(lastChain, lastParam)
                : a.ChainDeviceParamName(lastChain, lastDev, lastParam);
            text = $"→ C{lastChain + 1} {pn}";
        }
        else if (count > 1) text = $"→ {count} params";
        return new TextBlock { Text = text, FontSize = 8, Foreground = Brass, TextTrimming = TextTrimming.CharacterEllipsis };
    }

    private void ShowMacroMenu(IRackAccess a, Control anchor, int m)
    {
        var f = new MenuFlyout();
        var map = new MenuItem { Header = "Map to parameter" };
        int chains = a.ChainCount();
        for (int c = 0; c < chains; c++)
        {
            var chainItem = new MenuItem { Header = $"Chain {c + 1}" };
            if (a.HasInstrumentChains)
            {
                var instItem = new MenuItem { Header = a.ChainInstrumentName(c) };
                int ipc = a.ChainInstrumentParamCount(c);
                for (int p = 0; p < ipc; p++)
                {
                    int cc = c, pp = p;
                    var pit = new MenuItem { Header = a.ChainInstrumentParamName(c, p) };
                    pit.Click += (_, _) => { a.AddMacroMapping(m, cc, -1, pp, 0f, 1f); _ctx.RequestRebuild(); };
                    instItem.Items.Add(pit);
                }
                if (ipc > 0) chainItem.Items.Add(instItem);
            }
            int dc = a.ChainDeviceCount(c);
            for (int d = 0; d < dc; d++)
            {
                var dItem = new MenuItem { Header = a.ChainDeviceName(c, d) };
                int dpc = a.ChainDeviceParamCount(c, d);
                for (int p = 0; p < dpc; p++)
                {
                    int cc = c, dd = d, pp = p;
                    float lo = a.ChainDeviceParamMin(c, d, p), hi = a.ChainDeviceParamMax(c, d, p);
                    var pit = new MenuItem { Header = a.ChainDeviceParamName(c, d, p) };
                    pit.Click += (_, _) => { a.AddMacroMapping(m, cc, dd, pp, lo, hi); _ctx.RequestRebuild(); };
                    dItem.Items.Add(pit);
                }
                if (dpc > 0) chainItem.Items.Add(dItem);
            }
            map.Items.Add(chainItem);
        }
        f.Items.Add(map);

        int mapped = 0, n = a.MappingCount();
        for (int i = 0; i < n; i++) if (a.TryGetMapping(i, out var mm) && mm.Macro == m) mapped++;
        if (mapped > 0)
        {
            var clr = new MenuItem { Header = $"Clear {mapped} mapping(s)" };
            clr.Click += (_, _) => { ClearMacroMappings(a, m); _ctx.RequestRebuild(); };
            f.Items.Add(clr);
        }
        f.ShowAt(anchor, showAtPointer: true);
    }

    private static void ClearMacroMappings(IRackAccess a, int m)
    {
        for (int i = a.MappingCount() - 1; i >= 0; i--)
            if (a.TryGetMapping(i, out var mm) && mm.Macro == m) a.RemoveMapping(i);
    }

    private static bool IsEffect(Nota.Presentation.BrowserItem? item) =>
        item is { Kind: Nota.Presentation.BrowserItemKind.BuiltinEffect or Nota.Presentation.BrowserItemKind.PluginEffect };

    private void DropEffectOnChain(IRackAccess a, int chain, Nota.Presentation.BrowserItem item)
    {
        if (item.Kind == Nota.Presentation.BrowserItemKind.BuiltinEffect) a.AddChainDevice(chain, item.BuiltinKind);
        else if (item.Kind == Nota.Presentation.BrowserItemKind.PluginEffect) a.AddPluginChainDevice(chain, item.CatalogIndex);
        _ctx.RequestRebuild();
    }

    private Control AddChainButton(IRackAccess a)
    {
        var b = new Border
        {
            BorderBrush = BorderStrong, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
            Padding = new Thickness(8, 5), Cursor = new Cursor(StandardCursorType.Hand), HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock { Text = "＋ Add chain", FontSize = 10, Foreground = TextSecondary },
        };
        b.PointerPressed += (_, _) =>
        {
            if (!a.HasInstrumentChains) { a.AddChain(-1); _ctx.RequestRebuild(); return; }   // effect chain (no instrument)
            var f = new MenuFlyout();
            var s = new MenuItem { Header = "Nota Synth" };
            s.Click += (_, _) => { a.AddChain(0); _ctx.RequestRebuild(); };
            var p = new MenuItem { Header = "Nota Physical" };
            p.Click += (_, _) => { a.AddChain(2); _ctx.RequestRebuild(); };
            var w = new MenuItem { Header = "Nota Aurora" };
            w.Click += (_, _) => { a.AddChain(5); _ctx.RequestRebuild(); };
            var vo = new MenuItem { Header = "Nota Volt" };
            vo.Click += (_, _) => { a.AddChain(6); _ctx.RequestRebuild(); };
            var ba = new MenuItem { Header = "Nota Bass" };
            ba.Click += (_, _) => { a.AddChain(7); _ctx.RequestRebuild(); };
            var pe = new MenuItem { Header = "Nota Pendulum" };
            pe.Click += (_, _) => { a.AddChain(8); _ctx.RequestRebuild(); };
            var op = new MenuItem { Header = "Nota Operator" };
            op.Click += (_, _) => { a.AddChain(9); _ctx.RequestRebuild(); };
            var gr = new MenuItem { Header = "Nota Grain" };
            gr.Click += (_, _) => { a.AddChain(10); _ctx.RequestRebuild(); };
            var fl = new MenuItem { Header = "Nota Flux" };
            fl.Click += (_, _) => { a.AddChain(11); _ctx.RequestRebuild(); };
            var mo = new MenuItem { Header = "Nota Monolith" };
            mo.Click += (_, _) => { a.AddChain(13); _ctx.RequestRebuild(); };
            var pd = new MenuItem { Header = "Nota Pentad" };
            pd.Click += (_, _) => { a.AddChain(14); _ctx.RequestRebuild(); };
            f.Items.Add(s); f.Items.Add(p); f.Items.Add(w); f.Items.Add(vo); f.Items.Add(ba); f.Items.Add(pe); f.Items.Add(op); f.Items.Add(gr); f.Items.Add(fl); f.Items.Add(mo); f.Items.Add(pd);
            f.ShowAt(b, showAtPointer: true);
        };
        return b;
    }

    private Control RackDeviceParamRow(IRackAccess a, int c, int d, int p)
    {
        string name = a.ChainDeviceParamName(c, d, p);
        float min = a.ChainDeviceParamMin(c, d, p), max = a.ChainDeviceParamMax(c, d, p);
        float val = a.ChainDeviceParamGet(c, d, p);
        double span = Math.Max(1e-6, max - min);
        var value = new TextBlock { Text = DeviceParamControls.Fmt(val), FontSize = 9, Foreground = TextPrimary, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 4, 0) };
        value.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        top.Children.Add(new TextBlock { Text = name.ToUpperInvariant(), FontSize = 9, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(value, 1); top.Children.Add(value);
        var map = MapButton(a, c, d, p, min, max); Grid.SetColumn(map, 2); top.Children.Add(map);
        var bar = new Knob((val - min) / span, 1.0) { Accent = true, HorizontalAlignment = HorizontalAlignment.Center };
        bar.ValueChanged += frac => { float v = (float)(min + frac * span); a.ChainDeviceParamSet(c, d, p, v); value.Text = DeviceParamControls.Fmt(v); };
        _ctx.AddRackParamRefresher(() =>
        {
            if (bar.Dragging) return;
            float nv = a.ChainDeviceParamGet(c, d, p);
            float frac = (float)((nv - min) / span);
            if (Math.Abs(frac - bar.Value) > 1e-4) { bar.Value = frac; value.Text = DeviceParamControls.Fmt(nv); }
        });
        return new StackPanel { Spacing = 3, Children = { top, bar } };
    }

    private Control RackInstParamRow(IRackAccess a, int c, int p)
    {
        string name = a.ChainInstrumentParamName(c, p);
        float val = a.ChainInstrumentParamGet(c, p);
        var value = new TextBlock { Text = Pct(val), FontSize = 9, Foreground = TextPrimary, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 4, 0) };
        value.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        top.Children.Add(new TextBlock { Text = name.ToUpperInvariant(), FontSize = 9, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(value, 1); top.Children.Add(value);
        var map = MapButton(a, c, -1, p, 0f, 1f); Grid.SetColumn(map, 2); top.Children.Add(map);
        var bar = new Knob(val, 1.0) { Accent = true, HorizontalAlignment = HorizontalAlignment.Center };
        bar.ValueChanged += v => { a.ChainInstrumentParamSet(c, p, (float)v); value.Text = Pct((float)v); };
        _ctx.AddRackParamRefresher(() =>
        {
            if (bar.Dragging) return;
            float nv = a.ChainInstrumentParamGet(c, p);
            if (Math.Abs(nv - bar.Value) > 1e-4) { bar.Value = nv; value.Text = Pct(nv); }
        });
        return new StackPanel { Spacing = 3, Children = { top, bar } };
    }

    // A small chip next to each parameter that maps it to a macro (or shows/clears
    // the current mapping). deviceIndex -1 = the chain's instrument.
    private Control MapButton(IRackAccess a, int chain, int dev, int param, float lo, float hi)
    {
        int mapped = MappedMacro(a, chain, dev, param);
        var tb = new TextBlock { Text = mapped >= 0 ? $"M{mapped + 1}" : "M", FontSize = 8, FontWeight = FontWeight.SemiBold, Foreground = mapped >= 0 ? OnAccent : TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        var chip = new Border
        {
            Background = mapped >= 0 ? Brass : Card2, BorderBrush = BorderStrong, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3), Padding = new Thickness(4, 0), Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Center, Child = tb,
        };
        ToolTip.SetTip(chip, mapped >= 0 ? $"Mapped to Macro {mapped + 1} — click to change" : "Map to a macro");
        chip.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            var f = new MenuFlyout();
            for (int mm = 0; mm < 8; mm++)
            {
                int m = mm;
                var mi = new MenuItem { Header = $"Macro {m + 1}" };
                mi.Click += (_, _) => { RemoveParamMapping(a, chain, dev, param); a.AddMacroMapping(m, chain, dev, param, lo, hi); _ctx.RequestRebuild(); };
                f.Items.Add(mi);
            }
            if (mapped >= 0)
            {
                var un = new MenuItem { Header = "Unmap" };
                un.Click += (_, _) => { RemoveParamMapping(a, chain, dev, param); _ctx.RequestRebuild(); };
                f.Items.Add(un);
            }
            f.ShowAt(chip, showAtPointer: true);
        };
        return chip;
    }

    private static int MappedMacro(IRackAccess a, int chain, int dev, int param)
    {
        for (int i = 0; i < a.MappingCount(); i++)
            if (a.TryGetMapping(i, out var m) && m.Chain == chain && m.DeviceIndex == dev && m.ParamIndex == param) return m.Macro;
        return -1;
    }

    private static void RemoveParamMapping(IRackAccess a, int chain, int dev, int param)
    {
        for (int i = a.MappingCount() - 1; i >= 0; i--)
            if (a.TryGetMapping(i, out var m) && m.Chain == chain && m.DeviceIndex == dev && m.ParamIndex == param) a.RemoveMapping(i);
    }

    // ---- drum-rack pads ---------------------------------------------------
    private static int ChainForNote(IRackAccess a, int note)
    {
        for (int c = 0; c < a.ChainCount(); c++) if (a.ChainTriggerNote(c) == note) return c;
        return -1;
    }

    // Show a pad's chain in the device area (only rebuilds when the selection changes).
    private void SelectPad(int chain)
    {
        if (chain < 0 || chain == Sel) return;
        Sel = chain;
        _ctx.RequestRebuild();
    }

    // A browser item dropped on a specific pad: sample → Sampler on this note;
    // instrument → that built-in on this note (replacing any existing chain).
    private void DropOnPad(IRackAccess a, int note, Nota.Presentation.BrowserItem item)
    {
        int existing = ChainForNote(a, note);
        switch (item.Kind)
        {
            case Nota.Presentation.BrowserItemKind.Sample:
                if (existing >= 0) a.RemoveChain(existing);
                int cs = E.RackAddSamplerChain(T, item.Path, note, false);
                if (cs >= 0) E.RackSetChainTriggerNote(T, cs, note);
                break;
            case Nota.Presentation.BrowserItemKind.BuiltinInstrument:
                if (existing >= 0) a.RemoveChain(existing);
                int ci = a.AddChain(item.BuiltinKind);
                if (ci >= 0) a.SetChainTriggerNote(ci, note);
                break;
            case Nota.Presentation.BrowserItemKind.PluginInstrument:
                if (existing >= 0) a.RemoveChain(existing);
                int cp = E.RackAddPluginInstrumentChain(T, item.CatalogIndex);
                if (cp >= 0) E.RackSetChainTriggerNote(T, cp, note);
                break;
            default:
                return;   // effects / presets aren't pad content
        }
        _ctx.RequestRebuild();
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
                if (c >= 0) a.SetChainTriggerNote(c, note);
                _ctx.RequestRebuild();
            };
            f.Items.Add(mi);
        }
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
        f.ShowAt(anchor, showAtPointer: true);
    }

    // ---- small shared bits ------------------------------------------------
    private static Control SectionLabel(string text) => new TextBlock
    { Text = text, FontSize = 9, FontWeight = FontWeight.Bold, Foreground = NotaPalette.TextTertiary };

    // A small clickable chip (GUI / Save preset on the chain instrument card).
    private static Border ActionChip(string text, Action onClick)
    {
        var b = new Border
        {
            Background = Card2, BorderBrush = BorderStrong, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 2), Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock { Text = text, FontSize = 9, Foreground = TextSecondary },
        };
        b.PointerPressed += (_, e) => { e.Handled = true; onClick(); };
        return b;
    }

    private static Border Toggle(string text, bool active, IBrush activeBrush, Action onClick)
    {
        var b = new Border
        {
            Background = active ? activeBrush : Card2, BorderBrush = BorderStrong, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 2), Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock { Text = text, FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = active ? OnAccent : TextSecondary },
        };
        b.PointerPressed += (_, e) => { e.Handled = true; onClick(); };
        return b;
    }

    private static Border RackShell(string title, double width, Control body)
    {
        var headerBar = new Border
        {
            Height = 26, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(8, 0),
            Child = new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center },
        };
        var stack = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        stack.Children.Add(headerBar);
        var bodyHost = new Border { Padding = new Thickness(10), Child = body };
        Grid.SetRow(bodyHost, 1);
        stack.Children.Add(bodyHost);
        return new Border
        {
            Width = width, Height = CardH, Background = Raised, BorderBrush = BorderDef,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), ClipToBounds = true, Child = stack,
        };
    }
}

// Instrument-Rack key/velocity zone map: each chain's range drawn as a coloured bar over
// a keyboard (or a 0..127 strip in velocity mode); drag a bar's ends to edit the range.
internal sealed class RackZoneMap : Control
{
    private static readonly IBrush KeyWhite = new SolidColorBrush(Color.Parse("#2A2721"));
    private static readonly IBrush KeyBlack = new SolidColorBrush(Color.Parse("#151310"));
    private readonly int[] _lo, _hi;
    private readonly IBrush[] _cols;
    private readonly int _sel;
    private readonly bool _keyMode;
    private int _drag = -1, _edge;   // _drag = chain index; _edge: 0 lo, 1 hi, 2 move

    public event Action<int, int, int>? RangeEdited;   // (chain, lo, hi)

    public RackZoneMap(int[] lo, int[] hi, IBrush[] cols, int sel, bool keyMode)
    { _lo = lo; _hi = hi; _cols = cols; _sel = sel; _keyMode = keyMode; MinHeight = 28; ClipToBounds = true; }

    private double BarsH => Math.Max(8, Bounds.Height - 12);
    private double X(int v) => v / 127.0 * Bounds.Width;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        double w = Bounds.Width, bh = BarsH; if (w <= 0 || _lo.Length == 0) return;
        double x = e.GetPosition(this).X, y = e.GetPosition(this).Y;
        double rowH = bh / _lo.Length;
        int c = Math.Clamp((int)(y / rowH), 0, _lo.Length - 1);
        if (y > bh) return;   // keyboard strip
        double lx = X(_lo[c]), hx = X(_hi[c]);
        _drag = c; _edge = Math.Abs(x - lx) < 7 ? 0 : Math.Abs(x - hx) < 7 ? 1 : 2;
        e.Pointer.Capture(this); Apply(x); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e) { if (_drag >= 0) Apply(e.GetPosition(this).X); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    { if (_drag >= 0) { RangeEdited?.Invoke(_drag, _lo[_drag], _hi[_drag]); _drag = -1; e.Pointer.Capture(null); } }

    private void Apply(double x)
    {
        int v = Math.Clamp((int)Math.Round(x / Math.Max(1, Bounds.Width) * 127), 0, 127);
        int c = _drag;
        if (_edge == 0) _lo[c] = Math.Min(v, _hi[c]);
        else if (_edge == 1) _hi[c] = Math.Max(v, _lo[c]);
        else { int span = _hi[c] - _lo[c]; int lo = Math.Clamp(v - span / 2, 0, 127 - span); _lo[c] = lo; _hi[c] = lo + span; }
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height; if (w <= 0 || h <= 0) return;
        double bh = BarsH; int n = Math.Max(1, _lo.Length);
        double rowH = bh / n;
        for (int c = 0; c < _lo.Length; c++)
        {
            double y = c * rowH + 1, hgt = Math.Max(3, rowH - 2);
            double x0 = X(_lo[c]), x1 = X(_hi[c]);
            var col = ((SolidColorBrush)_cols[c]).Color;
            byte al = (byte)(c == _sel ? 0xCC : 0x80);
            ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(al, col.R, col.G, col.B)), null, new Rect(x0, y, Math.Max(2, x1 - x0), hgt), 2, 2);
        }
        // keyboard strip (key mode) or vel gradient ticks.
        double ky = bh + 1, kh = h - bh - 2;
        if (_keyMode)
        {
            int lo = 24, hi = 96; // C1..C7 span for the map
            for (int note = lo; note <= hi; note++)
            {
                int pc = ((note % 12) + 12) % 12;
                bool black = pc is 1 or 3 or 6 or 8 or 10;
                double kx = (note - lo) / (double)(hi - lo) * w, kw = w / (hi - lo);
                ctx.FillRectangle(black ? KeyBlack : KeyWhite, new Rect(kx, ky, Math.Max(1, kw - 0.6), kh));
            }
        }
        else
        {
            ctx.FillRectangle(KeyWhite, new Rect(0, ky, w, kh));
            for (int t = 1; t < 4; t++) ctx.DrawLine(new Pen(KeyBlack, 1), new Point(t / 4.0 * w, ky), new Point(t / 4.0 * w, ky + kh));
        }
    }
}

// Wraps an engine so a built-in instrument's own editor can drive a rack CHAIN instrument:
// its plugin-param calls (trackId, deviceIndex −1, i) are redirected to the chain surface
// (trackId, chain, i); automation writes are no-ops (chain params follow macros); everything
// else delegates to the real engine.
internal class RackChainEngineProxy : System.Reflection.DispatchProxy
{
    public Nota.Application.IAudioEngine Inner = null!;
    public int Chain;
    protected override object? Invoke(System.Reflection.MethodInfo? m, object?[]? args)
    {
        switch (m?.Name)
        {
            case nameof(Nota.Application.IAudioEngine.PluginParamCount):     return Inner.RackChainInstrumentParamCount((int)args![0]!, Chain);
            case nameof(Nota.Application.IAudioEngine.PluginParamId):        return Inner.RackChainInstrumentParamId((int)args![0]!, Chain, (int)args![2]!);
            case nameof(Nota.Application.IAudioEngine.PluginParamGet):       return Inner.RackChainInstrumentParamGet((int)args![0]!, Chain, (int)args![2]!);
            case nameof(Nota.Application.IAudioEngine.PluginParamSet):       Inner.RackChainInstrumentParamSet((int)args![0]!, Chain, (int)args![2]!, (float)args![3]!); return null;
            case nameof(Nota.Application.IAudioEngine.InstrumentParamDefault): return Inner.RackChainInstrumentParamDefault((int)args![0]!, Chain, (int)args![1]!);
            case nameof(Nota.Application.IAudioEngine.BeginAutomationWrite):
            case nameof(Nota.Application.IAudioEngine.EndAutomationWrite):   return null;
            case nameof(Nota.Application.IAudioEngine.InstrumentVoiceCount): return -1;
            case nameof(Nota.Application.IAudioEngine.InstrumentHeldNotes):  return 0;
            default: return m!.Invoke(Inner, args);
        }
    }
}

// Like RackChainEngineProxy but for a rack CHAIN DEVICE: redirects a device card's
// Device* param calls onto the chain-device surface via IRackAccess (works for both
// the Instrument Rack (di=-1) and the Audio Effect Rack (di>=0)).
internal class RackChainDeviceEngineProxy : System.Reflection.DispatchProxy
{
    public Nota.Application.IAudioEngine Inner = null!;
    public IRackAccess Access = null!;
    public int Chain, Dev;
    protected override object? Invoke(System.Reflection.MethodInfo? m, object?[]? args)
    {
        switch (m?.Name)
        {
            case nameof(Nota.Application.IAudioEngine.DeviceParamCount):   return Access.ChainDeviceParamCount(Chain, Dev);
            case nameof(Nota.Application.IAudioEngine.DeviceParamName):    return Access.ChainDeviceParamName(Chain, Dev, (int)args![2]!);
            case nameof(Nota.Application.IAudioEngine.DeviceParamMin):     return Access.ChainDeviceParamMin(Chain, Dev, (int)args![2]!);
            case nameof(Nota.Application.IAudioEngine.DeviceParamMax):     return Access.ChainDeviceParamMax(Chain, Dev, (int)args![2]!);
            case nameof(Nota.Application.IAudioEngine.DeviceGetParam):     return Access.ChainDeviceParamGet(Chain, Dev, (int)args![2]!);
            case nameof(Nota.Application.IAudioEngine.DeviceSetParam):     Access.ChainDeviceParamSet(Chain, Dev, (int)args![2]!, (float)args![3]!); return null;
            case nameof(Nota.Application.IAudioEngine.DeviceParamDefault): return Access.ChainDeviceParamGet(Chain, Dev, (int)args![2]!);   // no default surface for chain devices
            case nameof(Nota.Application.IAudioEngine.DeviceName):         return Access.ChainDeviceName(Chain, Dev);
            case nameof(Nota.Application.IAudioEngine.DeviceBypassed):     return Access.ChainDeviceBypassed(Chain, Dev);
            case nameof(Nota.Application.IAudioEngine.SetDeviceBypassed):  Access.SetChainDeviceBypassed(Chain, Dev, (bool)args![2]!); return null;
            case nameof(Nota.Application.IAudioEngine.DeviceGainReduction):   return 0f;
            case nameof(Nota.Application.IAudioEngine.DeviceLoadFile):        return false;   // chain devices keep params only
            case nameof(Nota.Application.IAudioEngine.DeviceText):            return "";
            // No telemetry surface for chain devices — passing through would read the
            // track's top-level device at the same index.
            case nameof(Nota.Application.IAudioEngine.DeviceScope):           return 0;
            case nameof(Nota.Application.IAudioEngine.DeviceLayerWave):       return 0;
            case nameof(Nota.Application.IAudioEngine.DeviceAcceptsSidechain): return false;
            case nameof(Nota.Application.IAudioEngine.DeviceSidechainSource):  return -1;
            case nameof(Nota.Application.IAudioEngine.DeviceSidechainGain):    return 0f;
            case nameof(Nota.Application.IAudioEngine.SetDeviceSidechainSource):
            case nameof(Nota.Application.IAudioEngine.SetDeviceSidechainGain):
            case nameof(Nota.Application.IAudioEngine.BeginAutomationWrite):
            case nameof(Nota.Application.IAudioEngine.EndAutomationWrite):   return null;
            default: return m!.Invoke(Inner, args);
        }
    }
}
