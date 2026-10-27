// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the rack cards. The Instrument Rack (track instrument, kind 3), the
// Audio Effect Rack (a device, builtin kind 5) and the MIDI Effect Rack (a MIDI effect,
// kind 6) share one card, built in the RackCard.*.cs partials (specs/040); this file holds
// their entry points and what the card and the Drum Rack (kind 4, RackCardView.Drum.cs)
// share: the device windows for a chain's instrument and effects, the add menus, Browser
// drops and macro menus. The racks' addressing differs only via IRackAccess. Built
// per-rebuild with a DeviceCardContext; the selected chain, refreshers, drop glow and
// rebuild live in the view behind the context.

using System;
using System.Collections.Generic;
using System.Linq;
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

internal sealed partial class RackCardView(DeviceCardContext ctx)
{
    private static readonly string[] RackDeviceNames = { "Nota EQ-3", "Nota EQ-8", "Nota Dynamic EQ-8", "Nota Compressor", "Nota Prism", "Nota Reverb", "Nota Chamber", "Nota Delay", "Nota Utility", "Nota Level", "Nota Lens", "Nota Ceiling", "Nota Shutter", "Nota Valve", "Nota Auto Filter", "Nota Vintage", "Nota Forge", "Nota Crush", "Nota Chorus", "Nota Flanger", "Nota Phaser", "Nota Orbit", "Nota Auto Shift", "Nota Beat Repeat", "Nota Strata", "Nota Vocoder" };
    private static readonly int[] RackDeviceKinds = { 16, 0, 13, 1, 21, 2, 20, 3, 4, 18, 22, 14, 19, 6, 7, 8, 17, 12, 25, 23, 24, 9, 10, 11, 15, 26 };
    private static readonly IBrush RowSel = NotaPalette.Wash(NotaPalette.Accent, 0x22); // selected chain wash

    private readonly DeviceCardContext _ctx = ctx;
    private IAudioEngine E => _ctx.Engine;
    // A Drum Rack nested in an Instrument Rack chain, shown through NestedRackEngineProxy
    // (specs/041): no kit timing and no MIDI Learn targets of its own.
    private bool Nested => E is NestedRackEngineProxy;
    private int T => _ctx.TrackId;
    // The chosen chain: each shared rack card keeps its own (several racks can sit on one
    // track); the Drum Rack uses the device view's.
    private bool _ownSel;
    private int Sel
    {
        get => _ownSel ? _rs.SelectedChain : _ctx.SelectedChain;
        set { if (_ownSel) _rs.SelectedChain = value; else _ctx.SelectedChain = value; }
    }

    // ---- Instrument Rack (mockup 2p) shared bits ----
    private static readonly IBrush IrHdr = NotaPalette.SurfaceCard;
    private static readonly IBrush IrRail = NotaPalette.SurfaceInset;
    private static readonly IBrush IrInset = NotaPalette.BgSunken;
    private static readonly IBrush IrBorder = NotaPalette.BorderDefault;
    private static readonly IBrush IrField = NotaPalette.GraphBorder;
    private static readonly IBrush IrCard = NotaPalette.SurfaceRaised;
    private static readonly IBrush IrAmber = NotaPalette.Accent;
    private static readonly IBrush IrAmberLit = NotaPalette.AccentBright;   // selection only
    private static readonly IBrush IrTeal = NotaPalette.Teal;
    private static readonly IBrush IrTxt = NotaPalette.TextPrimary;
    private static readonly IBrush IrMuted = NotaPalette.TextTertiary;
    private static readonly IBrush IrRed = NotaPalette.AccentHover;   // a state, not an alert: red is kept for recording and overload
    private readonly System.Collections.Generic.List<Action> _irTick = new();

    // Rebuild on the NEXT dispatcher cycle, never synchronously inside a pointer handler:
    // tearing down the clicked element mid-dispatch corrupts pointer state (a click would
    // "work once" then stop routing). Structural edits from clicks go through this.
    private void DeferRebuild() => Avalonia.Threading.Dispatcher.UIThread.Post(_ctx.RequestRebuild);

    // The palette slot itself, not a copy of its colour: these brushes are handed to
    // Borders that outlive a theme change, and a snapshot would freeze the chain hues.
    private static IBrush ChainHue(int c) => NotaPalette.TrackBrushes[((c % 8) + 8) % 8];
    private static TextBlock IrCap(string t, IBrush? c = null) => new() { Text = NotaText.Label(t), FontSize = 8, FontWeight = FontWeight.Medium, Foreground = c ?? IrMuted, VerticalAlignment = VerticalAlignment.Center };

    // ---- entry points -----------------------------------------------------

    // The rack bodies (700 × 238 under the shared device shell's header, which draws the
    // name, presets and "Rack · N chains").
    public Control BuildInstrumentRackBody(out int chains)
    {
        var a = new InstrumentRackAccess(E, T);
        chains = a.ChainCount();
        return RcBody(BuildRackCard(a, -1));
    }

    // The shared card fills the shell slot under the header (700 × 238).
    private static Control RcBody(Control card) => new Border { Height = DeviceCardKit.CardH - DeviceCardKit.HeaderH, ClipToBounds = true, Child = card };

    // A minimal device "slot" — name + type + two open buttons (full GUI / param knobs)
    // + macro/bypass footer.
    private Border IrSlot(IBrush dot, string name, string kind, bool dim, Action openGui, Action openParams, Control footerLeft, Action? onDelete = null)
    {
        var head = new DockPanel { LastChildFill = true, Height = 18, Children = {
            new Border { Width = 6, Height = 6, CornerRadius = NotaRadius.Badge, Background = dot, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), [DockPanel.DockProperty] = Dock.Left },
            WithRight(new TextBlock { Text = kind, FontSize = 7, FontWeight = FontWeight.Medium, Foreground = IrMuted, VerticalAlignment = VerticalAlignment.Center }),
            new TextBlock { Text = name, FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = IrTxt, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis } } };
        if (onDelete != null) head.Children.Insert(1, WithRight(SlotDelBtn(onDelete)));
        var headWrap = new Border { BorderBrush = IrBorder, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(6, 0), Child = head };
        Border MiniBtn(string label, Action act)
        {
            var b = new Border { BorderThickness = new Thickness(1), BorderBrush = IrBorder, Background = IrInset, CornerRadius = NotaRadius.Badge, Padding = new Thickness(6, 2), Cursor = new Cursor(StandardCursorType.Hand),
                Child = new TextBlock { Text = label, FontSize = 8, Foreground = NotaPalette.TextSecondary, HorizontalAlignment = HorizontalAlignment.Center } };
            b.PointerPressed += (_, e) => { e.Handled = true; act(); };
            return b;
        }
        var center = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 4, Children = { MiniBtn("GUI", openGui), MiniBtn("Params", openParams) } };
        var footer = new Border { Height = 14, BorderBrush = IrBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(6, 0), Child =
            new DockPanel { LastChildFill = true, Children = { WithRight(new Glyph(GlyphKind.Grip, 8) { Foreground = IrMuted }), footerLeft } } };
        DockPanel.SetDock(headWrap, Dock.Top); DockPanel.SetDock(footer, Dock.Bottom);
        var slotCard = new Border { Width = 96, Background = IrCard, BorderBrush = IrBorder, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Panel, ClipToBounds = true,
            Child = new DockPanel { LastChildFill = true, Children = { headWrap, footer, center } } };
        if (dim) slotCard.AttachedToVisualTree += (_, _) => Inactive.Set(slotCard, true, interactive: true);   // no opacity for state
        return slotCard;
    }

    // Small borderless ✕ in a device slot header that removes that device from the chain.
    private Border SlotDelBtn(Action onDelete)
    {
        var glyph = new Glyph(GlyphKind.Close, 8) { Foreground = IrMuted };
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
        var footer = mc > 0 ? new TextBlock { Text = $"{mc} macro", FontSize = 7, FontWeight = FontWeight.Medium, Foreground = IrTeal, VerticalAlignment = VerticalAlignment.Center }
                            : (Control)new TextBlock { Text = "Instr", FontSize = 7, FontWeight = FontWeight.Medium, Foreground = IrMuted, VerticalAlignment = VerticalAlignment.Center };
        Border slot = null!;
        slot = IrSlot(ChainHue(sc), string.IsNullOrEmpty(a.ChainInstrumentName(sc)) ? "Instrument" : a.ChainInstrumentName(sc),
            a.ChainInstrumentKind(sc) < 0 ? "Plug" : "Inst", false, () => OpenChainInstrumentGui(a, sc, slot), () => OpenChainInstrumentParams(a, sc, slot), footer);
        return slot;
    }

    private Control IrDeviceSlot(IRackAccess a, int sc, int d)
    {
        bool byp = a.ChainDeviceBypassed(sc, d);
        int mc = MacrosOnTarget(a, sc, d);
        var footer = mc > 0 ? (Control)new TextBlock { Text = $"{mc} macro", FontSize = 7, FontWeight = FontWeight.Medium, Foreground = IrTeal, VerticalAlignment = VerticalAlignment.Center }
                            : IrMsBtn("Byp", byp, NotaPalette.TextTertiary, () => { a.SetChainDeviceBypassed(sc, d, !byp); _ctx.RequestRebuild(); });
        Border slot = null!;
        slot = IrSlot(byp ? IrMuted : Success, a.ChainDeviceName(sc, d), a.ChainDeviceBuiltinKind(sc, d) < 0 ? "Plug" : "FX", byp, () => OpenChainDeviceGui(a, sc, d, slot), () => OpenChainDeviceParams(a, sc, d, slot), footer,
            () => { a.RemoveChainDevice(sc, d); DeferRebuild(); });
        return slot;
    }

    private static readonly IBrush DashIdle = NotaPalette.BorderStrong;

    private Control IrAddDeviceSlot(IRackAccess a, int sc)
    {
        var plus = new Glyph(GlyphKind.Plus, 10) { Foreground = IrMuted };
        var lbl = new TextBlock { Text = "Device", FontSize = 8, Foreground = IrMuted, HorizontalAlignment = HorizontalAlignment.Center };
        var dash = new Avalonia.Controls.Shapes.Rectangle { Stroke = DashIdle, StrokeThickness = 1, StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 3, 2.5 }, RadiusX = NotaRadius.ControlValue, RadiusY = NotaRadius.ControlValue, IsVisible = false };
        var box = new Panel { Width = 52, Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent,
            Children = { dash, new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 2, Children = { plus, lbl } } } };

        // The dashed outline shows only while an effect is dragged over the slot (spec 012).
        void Idle() { dash.IsVisible = false; dash.Stroke = DashIdle; dash.Fill = null; plus.Foreground = IrMuted; lbl.Foreground = IrMuted; }
        void Hover() { dash.Stroke = NotaPalette.TextTertiary; plus.Foreground = IrTxt; lbl.Foreground = NotaPalette.TextSecondary; }
        void Drop() { dash.IsVisible = true; dash.Stroke = IrTeal; dash.Fill = NotaPalette.Wash(NotaPalette.Teal, 0x22); plus.Foreground = IrTeal; lbl.Foreground = IrTeal; }

        ToolTip.SetTip(box, "Add device — click for the list, or drop an effect here");
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
        if (a.HostsPlugins && App.Services?.GetService(typeof(Nota.Application.IPluginCatalog)) is Nota.Application.IPluginCatalog cat && cat.Count > 0)
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

    private static readonly InstrumentCardFactory _irFactory = new();
    private static readonly DeviceCardFactory _irDeviceFactory = new();

    // Full GUI: hosted plug-in → its native window; built-in Sampler → the tabbed editor
    // (drop-to-load); other built-ins → their bespoke editor hosted over the chain's param
    // surface via a proxy engine (so Volt/Aurora/… show their real UI, not just knobs) — both
    // in the device window, with the card's presets and, for a two-size instrument, S / L.
    private void OpenChainInstrumentGui(IRackAccess a, int sc, Control anchor)
    {
        int ik = a.ChainInstrumentKind(sc);
        if (ik == -1) { try { E.RackOpenChainInstrumentEditor(T, sc); } catch { /* no-op */ } return; }
        if (ik == 1) { OpenChainSamplerWindow(a, anchor, sc, InstrumentPresets(a, sc, ik)); return; }
        if (ik == 4 && a is InstrumentRackAccess && !Nested) { OpenNestedDrumRackWindow(a, sc, anchor); return; }
        string nm = string.IsNullOrEmpty(a.ChainInstrumentName(sc)) ? "Instrument" : a.ChainInstrumentName(sc);
        var strategy = _irFactory.Resolve(ik, true);
        var proxy = ChainInstrumentProxy(sc);
        var presets = InstrumentPresets(a, sc, ik);
        DeviceWindow win = null!;
        win = new DeviceWindow(nm, E.GetTrackName(T), strategy.Subtitle, tickReg =>
        {
            bool two = InstrumentView.Index(proxy, T) >= 0;
            double w = strategy.BodyOnly ? strategy.WidthFor(proxy, T) : 700;
            return new DeviceWindowPage(BuildChainInstrumentBespoke(ik, sc, tickReg, w), w, CardBodyH, WindowPresets(presets, () => win.Refill()),
                two ? new DeviceWindowSize(InstrumentView.IsMini(proxy, T), m => InstrumentView.SetMini(proxy, T, m)) : null);
        }, status: () => InstrumentStatus(a, sc, proxy, strategy)) { ContextTrackId = T };
        win.ShowFrom(anchor);
    }

    private const double CardBodyH = DeviceCardKit.CardH - DeviceCardKit.HeaderH;

    // A Drum Rack in an Instrument Rack chain (specs/041): the Drum Rack card itself, built over
    // a NestedRackEngineProxy so every call it makes addresses the nested rack, in the device
    // window. The factory kits are the window's presets, as on a Drum Rack track.
    private void OpenNestedDrumRackWindow(IRackAccess a, int sc, Control anchor)
    {
        var proxy = NestedRackEngineProxy.Create(E, sc);
        int sel = 0;
        var kits = App.Services?.GetService(typeof(IDrumKits)) as IDrumKits;
        DeviceWindow win = null!;
        win = new DeviceWindow("Nota Drum Rack", E.GetTrackName(T), "Drums", tickReg =>
        {
            // The chain may have changed under the open window: show nothing rather than drive another rack.
            if (a.ChainInstrumentKind(sc) != 4)
                return new DeviceWindowPage(new TextBlock { Text = "This chain no longer holds a Drum Rack.", Foreground = TextTertiary, Margin = new Thickness(12) }, 700, CardBodyH);
            var refreshers = new List<Action>();
            var ctx = new DeviceCardContext(proxy, T, tickReg, _ctx.NotifyChanged, (_, _, _, _) => { }, _ => { }, _ => { }, () => sel, v => sel = v,
                refreshers.Add, () => { foreach (var r in refreshers) r(); }, () => Avalonia.Threading.Dispatcher.UIThread.Post(() => { win.Refill(); _ctx.RequestRebuild(); }),
                _ => { }, () => { }, _ctx.Factory, _ctx.Status);
            tickReg(() => { foreach (var r in refreshers) r(); });
            var body = new RackCardView(ctx).BuildDrumRackBody(out _);
            DeviceWindowPresets? presets = null;
            if (kits is not null)
            {
                var all = kits.All().ToList();
                string cur = kits.Identify(proxy, T);
                presets = new DeviceWindowPresets(all.Select(k => k.Name).ToList(), all.FindIndex(k => k.Id == cur), i =>
                {
                    kits.LoadInto(proxy, T, all[i].Id, out string warn);
                    if (warn.Length > 0) _ctx.Status(warn);
                    sel = 0;
                    win.Refill();
                    _ctx.RequestRebuild();
                });
            }
            return new DeviceWindowPage(new Border { Width = 700, Height = CardBodyH, ClipToBounds = true, Child = body }, 700, CardBodyH, presets);
        }, status: () => $"{ChainLocation(a, sc)} · {E.RackDevChainCount(T, RackMacroMapping.NestedRackBase - sc)} pads") { ContextTrackId = T };
        win.ShowFrom(anchor);
    }

    // ---- device window: where the device lives + a live line for its status bar ----

    // "Drum Rack · pad C1 Kick", "Instrument Rack · chain 2 Lead", "Audio Effect Rack ·
    // chain 1", "Nota Rhythm · voice 3".
    private string ChainLocation(IRackAccess a, int sc)
    {
        switch (a)
        {
            case InstrumentRackAccess:
                string n = E.RackChainName(T, sc);
                if (E.TrackInstrumentKind(T) == 4)
                    return $"Drum Rack · pad {NoteName(a.ChainTriggerNote(sc))}{(n.Length > 0 ? " " + n : "")}";
                return $"Instrument Rack · chain {sc + 1}{(n.Length > 0 ? " " + n : "")}";
            case EffectRackAccess:
                return $"Audio Effect Rack · chain {sc + 1}";
            case RhythmVoiceAccess:
                return $"Nota Rhythm · voice {sc + 1}";
            default:
                return $"chain {sc + 1}";
        }
    }

    private string InstrumentStatus(IRackAccess a, int sc, IAudioEngine proxy, IInstrumentCard strategy)
    {
        string where = ChainLocation(a, sc);
        int v = proxy.InstrumentVoiceCount(T);
        if (v < 0) return where;
        string voices = strategy.VoiceLabel(proxy, T, v) ?? $"{Math.Max(0, v)}/16";
        return $"{where} · voices {voices}";
    }

    private string DeviceStatus(IRackAccess a, int sc, int d)
    {
        int n = a.ChainDeviceCount(sc);
        return d < n ? $"{ChainLocation(a, sc)} · effect {d + 1} of {n}" : ChainLocation(a, sc);
    }

    // A chain effect's bypass for its window; a flip there rebuilds the rack card so the
    // slot follows (the window reads the state live, so a flip on the card follows too).
    private DeviceWindowBypass ChainDeviceBypass(IRackAccess a, int sc, int d) => new(
        () => d < a.ChainDeviceCount(sc) && a.ChainDeviceBypassed(sc, d),
        b =>
        {
            if (d >= a.ChainDeviceCount(sc)) return;
            a.SetChainDeviceBypassed(sc, d, b);
            _ctx.RequestRebuild();
        });

    // The chain presets as the window's picker; applying one re-fills the window (so every
    // control shows the new values) and nudges the rack's inline faders.
    private DeviceWindowPresets? WindowPresets(ChainPresets presets, Action refill)
    {
        var items = presets.Items;
        if (items.Count == 0) return null;
        int cur = -1;
        if (_chainPresetIds.TryGetValue(presets.Key, out var curId))
            for (int i = 0; i < items.Count; i++) if (items[i].Id == curId) { cur = i; break; }
        return new DeviceWindowPresets(items.Select(p => p.Name).ToList(), cur, i =>
        {
            presets.Apply(items[i].Id);
            _chainPresetIds[presets.Key] = items[i].Id;
            _ctx.InvokeRackParamRefreshers();
            refill();
        });
    }

    // ---- full-UI popup header: the device's name + its factory-preset picker ----

    // A chain slot's preset list and how to apply one in place. Key names the slot (rack
    // addressing + chain + device + kind) so the header remembers the last preset applied.
    private sealed record ChainPresets(string Key, IReadOnlyList<(string Id, string Name)> Items, Action<string> Apply);
    private static readonly Dictionary<string, string> _chainPresetIds = new();

    private List<(string Id, string Name)> FactoryItems(int kind, bool instrument)
        => _ctx.Factory is { } f
            ? f.All().Where(p => p.BuiltinKind == kind && p.IsInstrument == instrument && !p.IsMidiEffect).Select(p => (p.Id, p.DisplayName)).ToList()
            : new();

    // Factory presets apply through the same proxies the bespoke editors use, so the
    // preset code sees the chain instrument / device as if it were the track's own.
    private ChainPresets InstrumentPresets(IRackAccess a, int sc, int ik)
        => new($"{a.GetType().Name}:{T}:{a.AutomationDeviceIndex}:{sc}:i{ik}", FactoryItems(ik, instrument: true),
            id => _ctx.Factory?.ApplyInPlace(ChainInstrumentProxy(sc), id, T, -1));
    private ChainPresets DevicePresets(IRackAccess a, int sc, int d, int kind)
        => new($"{a.GetType().Name}:{T}:{a.AutomationDeviceIndex}:{sc}:{d}:d{kind}", FactoryItems(kind, instrument: false),
            id => _ctx.Factory?.ApplyInPlace(ChainDeviceProxy(a, sc, d, kind), id, T, d));

    private IAudioEngine ChainInstrumentProxy(int sc) => RackChainEngineProxy.Create(E, sc);
    private IAudioEngine ChainDeviceProxy(IRackAccess a, int sc, int d, int kind)
    {
        IAudioEngine proxy = System.Reflection.DispatchProxy.Create<IAudioEngine, RackChainDeviceEngineProxy>();
        var pp = (RackChainDeviceEngineProxy)(object)proxy; pp.Inner = E; pp.Access = a; pp.Chain = sc; pp.Dev = d; pp.Kind = kind;
        return proxy;
    }

    // Host a built-in instrument's own editor for a rack chain: a DispatchProxy redirects
    // the editor's plugin-param calls (track, -1, i) to the chain surface (track, chain, i);
    // a local tick drives its live graphs + knob follow.
    private Control BuildChainInstrumentBespoke(int ik, int sc, Action<Action> tickReg, double width = 700)
    {
        var proxy = ChainInstrumentProxy(sc);
        var faders = new System.Collections.Generic.List<(int i, Knob k, TextBlock v, Func<float, string>? fmt)>();
        Action? viz = null;
        var ctx = new DeviceCardContext(proxy, T, tickReg, () => { }, (i, k, v, f) => faders.Add((i, k, v, f)), v => viz = v,
            _ => { }, () => 0, _ => { }, tickReg, () => { }, () => { }, _ => { }, () => { }, _ctx.Factory);
        var body = _irFactory.Resolve(ik, true).Build(ctx);
        tickReg(() => { viz?.Invoke(); foreach (var f in faders) { if (f.k.Dragging) continue; float v = proxy.PluginParamGet(T, -1, f.i); if (Math.Abs(v - f.k.Value) > 1e-3) { f.k.Value = v; f.v.Text = (f.fmt ?? Pct)(v); } } });
        return new Border { Width = width, Height = CardBodyH, Background = NotaPalette.BgApp, ClipToBounds = true, Child = body };
    }

    // Sampler pop-out that also accepts a dropped sample file (browser or Finder) → load it
    // into the chain's Sampler and rebuild the editor.
    private void OpenChainSamplerWindow(IRackAccess a, Control anchor, int sc, ChainPresets presets)
    {
        var acc = new ChainSamplerAccess(E, T, sc);
        DeviceWindow win = null!;
        win = new DeviceWindow("Nota Sampler", E.GetTrackName(T), _irFactory.Resolve(1, true).Subtitle, tickReg =>
        {
            var wrap = new Border { Width = 700, Height = CardBodyH, Background = NotaPalette.BgApp, ClipToBounds = true, Child = SamplerInstrumentCard.BuildEditor(E, acc, tickReg) };
            DragDrop.SetAllowDrop(wrap, true);
            DragDrop.AddDragOverHandler(wrap, (_, e) => { if (!BrowserView.IsAcceptableDrag(e)) { e.DragEffects = DragDropEffects.None; return; } e.DragEffects = DragDropEffects.Copy; e.Handled = true; });
            DragDrop.AddDropHandler(wrap, (_, e) => { foreach (var it in BrowserView.DroppedItems(e)) if (it.Kind == Nota.Presentation.BrowserItemKind.Sample) { acc.LoadSample(it.Path); win.Refill(); break; } e.Handled = true; });
            return new DeviceWindowPage(wrap, 700, CardBodyH, WindowPresets(presets, () => win.Refill()));
        }, status: () => InstrumentStatus(a, sc, ChainInstrumentProxy(sc), _irFactory.Resolve(1, true))) { ContextTrackId = T };
        win.ShowFrom(anchor);
    }
    private void OpenChainInstrumentParams(IRackAccess a, int sc, Control anchor)
    {
        string nm = string.IsNullOrEmpty(a.ChainInstrumentName(sc)) ? "Instrument" : a.ChainInstrumentName(sc);
        OpenParamWindow(anchor, nm, a.ChainInstrumentParamCount(sc), p => RackInstParamRow(a, sc, p), () => InstrumentStatus(a, sc, ChainInstrumentProxy(sc), _irFactory.Resolve(a.ChainInstrumentKind(sc), true)));
    }
    private void OpenChainDeviceGui(IRackAccess a, int sc, int d, Control anchor)
    {
        int kind = a.ChainDeviceBuiltinKind(sc, d);
        if (kind < 0) { a.OpenChainDeviceEditor(sc, d); return; }   // hosted plug-in → native window
        string nm = a.ChainDeviceName(sc, d);
        var strategy = _irDeviceFactory.Resolve(kind);
        var presets = DevicePresets(a, sc, d, kind);
        DeviceWindow win = null!;
        win = new DeviceWindow(nm, E.GetTrackName(T), strategy.Subtitle ?? "", tickReg =>   // built-in → its real device card
        {
            double w = strategy.AutoWidth ? double.NaN : strategy.Width;
            return new DeviceWindowPage(BuildChainDeviceBespoke(a, sc, d, kind, tickReg), w, CardBodyH, WindowPresets(presets, () => win.Refill()));
        }, ChainDeviceBypass(a, sc, d), () => DeviceStatus(a, sc, d)) { ContextTrackId = T };
        win.ShowFrom(anchor);
    }

    // Host a built-in effect's own device card for a rack chain: a DispatchProxy redirects
    // the card's Device* param calls onto the chain-device surface (via IRackAccess), so the
    // effect shows its real UI (AutoFilter curve, EQ bands, …) rather than plain knobs.
    private Control BuildChainDeviceBespoke(IRackAccess a, int sc, int d, int kind, Action<Action> tickReg)
    {
        var proxy = ChainDeviceProxy(a, sc, d, kind);
        var ctx = new DeviceCardContext(proxy, T, tickReg, () => { }, (_, _, _, _) => { }, _ => { }, _ => { }, () => sc, _ => { }, tickReg, () => { }, () => { }, _ => { }, () => { }, _ctx.Factory);
        var strategy = _irDeviceFactory.Resolve(kind);
        var body = strategy.Build(ctx, a.AutomationDeviceIndex);
        // As in the device chain: the almanac body inset (6) unless the body pads itself.
        return strategy.FullBleed ? body : new Border { Padding = new Thickness(NotaSpace.DeviceInset), Child = body };
    }
    private void OpenChainDeviceParams(IRackAccess a, int sc, int d, Control anchor)
    {
        string dnm = a.ChainDeviceName(sc, d);
        OpenParamWindow(anchor, dnm, a.ChainDeviceParamCount(sc, d), p => RackDeviceParamRow(a, sc, d, p), () => DeviceStatus(a, sc, d), ChainDeviceBypass(a, sc, d));
    }

    private void ShowAddChainMenu(IRackAccess a, Control anchor)
    {
        var f = new MenuFlyout();
        void Add(string h, int k) { var mi = new MenuItem { Header = h }; mi.Click += (_, _) => { int c = a.AddChain(k); if (c >= 0) Sel = c; _ctx.RequestRebuild(); }; f.Items.Add(mi); }
        Add("Nota Volt", 6); Add("Nota Sampler", 1); Add("Nota Physical", 2); Add("Nota Aurora", 5);
        Add("Nota Pendulum", 8); Add("Nota Operator", 9); Add("Nota Grain", 10); Add("Nota Flux", 11); Add("Nota Monolith", 13); Add("Nota Pentad", 14); Add("Nota Consort", 15); Add("Nota Keys", 16);
        Add("Nota Mosaic", 17);
        f.Items.Add(new Separator()); Add("Nota Drum Rack", 4);   // racks nest one level (specs/041)
        f.ShowAt(anchor, showAtPointer: true);
    }

    private static Control WithRight(Control c) { DockPanel.SetDock(c, Dock.Right); return c; }
    private static Control WithCol(Control c, int col) { Grid.SetColumn(c, col); return c; }

    private Border IrMsBtn(string t, bool on, SolidColorBrush fill, Action onClick)
    {
        var b = new Border { MinWidth = 13, Height = 12, CornerRadius = NotaRadius.Clip, BorderThickness = new Thickness(1), BorderBrush = on ? fill : NotaPalette.BorderStrong, Background = on ? fill : IrCard, Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock { Text = t, FontSize = 7, FontWeight = FontWeight.Medium, Foreground = on ? NotaPalette.InkOn(fill) : NotaPalette.TextSecondary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(3, 0) } };
        b.PointerPressed += (_, e) => { e.Handled = true; onClick(); };
        return b;
    }

    // ---- second view: Macro map (mockup 2p card 2) ----
    // Audio Effect Rack body (a device, kind 5): wrapped by the view's DeviceShell.
    // Audio Effect Rack (mockup 2r): the shared device shell provides the header; this
    // body is the macro strip + (chains | devices | rack rail), with a MODE switch
    // (Parallel / Series / Select). Series swaps the body for stacked chain columns.
    public Control BuildEffectRackBody(int di) => RcBody(BuildRackCard(new EffectRackAccess(E, T, di), di));

    public Control BuildMidiRackBody(int mi) => RcBody(BuildRackCard(new MidiRackAccess(E, T, mi), mi));

    // A roomy, non-bespoke "full UI": every param as a knob in a 3-column grid (the name
    // is on the device window's title bar).
    private static Control ParamGridContent(int count, Func<int, Control> row)
    {
        var wrap = new WrapPanel { MaxWidth = 456 };
        for (int p = 0; p < count; p++) wrap.Children.Add(new Border { Width = 148, Margin = new Thickness(2, 3), Child = row(p) });
        var scroll = new ScrollViewer { Content = wrap, MaxHeight = 360, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        return new Border { Padding = new Thickness(10), Child = scroll };
    }

    // Every param as knobs, in the device window (no presets or sizes: it is not the card).
    private void OpenParamWindow(Control anchor, string name, int count, Func<int, Control> row, Func<string> status, DeviceWindowBypass? bypass = null)
    {
        var win = new DeviceWindow(name, E.GetTrackName(T), "Params",
            _ => new DeviceWindowPage(ParamGridContent(count, row), 476, double.NaN), bypass,
            () => $"{status()} · {count} parameter{(count == 1 ? "" : "s")}") { ContextTrackId = T };
        win.ShowFrom(anchor);
    }

    // A compact device-area card: 24px header (title + optional controls) over a
    // scrollable body.
    private Border DeviceCardShell(string title, Control? headerRight, Control body, double width)
    {
        var titleTb = new TextBlock { Text = title, FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
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
            CornerRadius = NotaRadius.Panel, ClipToBounds = true, Child = stack,
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
        if (!Nested) MidiLearn.Bind(bar, MidiTarget.PluginParam(T, di, m), "Macro " + (m + 1));
        bar.Format = x => Pct((float)x);
        ValueEntry.Attach(bar, "Macro " + (m + 1), val);
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
            string pn = lastDev == RackMacroMapping.PadControls && lastParam is >= 0 and < 4
                ? RackMacroMapping.PadParamNames[lastParam]
                : lastDev < 0
                ? a.ChainInstrumentParamName(lastChain, lastParam)
                : a.ChainDeviceParamName(lastChain, lastDev, lastParam);
            text = $"→ C{lastChain + 1} {pn}";
        }
        else if (count > 1) text = $"→ {count} params";
        return new TextBlock { Text = text, FontSize = 8, Foreground = NotaPalette.TealBright, TextTrimming = TextTrimming.CharacterEllipsis };
    }

    private void ShowMacroMenu(IRackAccess a, Control anchor, int m)
    {
        var f = new MenuFlyout();
        f.Items.Add(MenuKit.Header(a.MacroName(m), null, $"Macro · {m + 1}"));
        f.Items.Add(new Separator());
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
                    pit.Click += (_, _) => { RcAddMapping(m, cc, -1, pp, 0f, 1f); _ctx.RequestRebuild(); };
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
                    pit.Click += (_, _) => { RcAddMapping(m, cc, dd, pp, lo, hi); _ctx.RequestRebuild(); };
                    dItem.Items.Add(pit);
                }
                if (dpc > 0) chainItem.Items.Add(dItem);
            }
            map.Items.Add(chainItem);
        }
        f.Items.Add(map);
        if (a.Kind != RackKind.Instrument)
        {
            var sel = new MenuItem { Header = "Map to the selector" };
            sel.Click += (_, _) => { RcAddMapping(m, 0, RackMacroMapping.RackControl, 0, 0f, 1f); _ctx.RequestRebuild(); };
            f.Items.Add(sel);
        }

        int mapped = 0, n = a.MappingCount();
        for (int i = 0; i < n; i++) if (a.TryGetMapping(i, out var mm) && mm.Macro == m) mapped++;
        if (mapped > 0)
        {
            f.Items.Add(new Separator());
            f.Items.Add(MenuKit.Danger(mapped == 1 ? "Clear 1 mapping" : $"Clear {mapped} mappings", GlyphKind.Trash,
                () => { ClearMacroMappings(a, m); _ctx.RequestRebuild(); }));
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

    /// <summary>One chain's insert effects as a row of device slots, then the add slot — the
    /// Drum Rack pad's chain strip without its instrument. Nota Rhythm shows a voice's FX with
    /// it, through <see cref="RhythmVoiceAccess"/>.</summary>
    public Control BuildEffectStrip(IRackAccess a, int chain)
    {
        var slots = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        for (int d = 0; d < a.ChainDeviceCount(chain); d++) slots.Children.Add(IrDeviceSlot(a, chain, d));
        slots.Children.Add(IrAddDeviceSlot(a, chain));
        return new ScrollViewer { Content = slots, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    private void DropEffectOnChain(IRackAccess a, int chain, Nota.Presentation.BrowserItem item)
    {
        if (item.Kind == Nota.Presentation.BrowserItemKind.PluginEffect && !a.HostsPlugins) return;
        if (item.Kind == Nota.Presentation.BrowserItemKind.BuiltinEffect) a.AddChainDevice(chain, item.BuiltinKind);
        else if (item.Kind == Nota.Presentation.BrowserItemKind.PluginEffect) a.AddPluginChainDevice(chain, item.CatalogIndex);
        _ctx.RequestRebuild();
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
        top.Children.Add(new TextBlock { Text = NotaText.Label(name), FontSize = 9, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(value, 1); top.Children.Add(value);
        var map = MapButton(a, c, d, p, min, max); Grid.SetColumn(map, 2); top.Children.Add(map);
        var bar = new Knob((val - min) / span, 1.0) { Accent = true, HorizontalAlignment = HorizontalAlignment.Center };
        bar.ValueChanged += frac => { float v = (float)(min + frac * span); a.ChainDeviceParamSet(c, d, p, v); value.Text = DeviceParamControls.Fmt(v); };
        bar.Format = frac => DeviceParamControls.Fmt((float)(min + frac * span));
        ValueEntry.Attach(bar, name, value);
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
        top.Children.Add(new TextBlock { Text = NotaText.Label(name), FontSize = 9, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(value, 1); top.Children.Add(value);
        var map = MapButton(a, c, -1, p, 0f, 1f); Grid.SetColumn(map, 2); top.Children.Add(map);
        var bar = new Knob(val, 1.0) { Accent = true, HorizontalAlignment = HorizontalAlignment.Center };
        bar.ValueChanged += v => { a.ChainInstrumentParamSet(c, p, (float)v); value.Text = Pct((float)v); };
        bar.Format = v => Pct((float)v);
        ValueEntry.Attach(bar, name, value);
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
            CornerRadius = NotaRadius.Badge, Padding = new Thickness(4, 0), Cursor = new Cursor(StandardCursorType.Hand),
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

    // ---- small shared bits ------------------------------------------------
    private static Control SectionLabel(string text) => new TextBlock
    { Text = NotaText.Label(text), FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = NotaPalette.TextTertiary };

    private static Border Toggle(string text, bool active, SolidColorBrush activeBrush, Action onClick)
    {
        var b = new Border
        {
            Background = active ? activeBrush : Card2, BorderBrush = BorderStrong, BorderThickness = new Thickness(1),
            CornerRadius = NotaRadius.Control, Padding = new Thickness(6, 2), Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock { Text = text, FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = active ? NotaPalette.InkOn(activeBrush) : TextSecondary },
        };
        b.PointerPressed += (_, e) => { e.Handled = true; onClick(); };
        return b;
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
    public int Kind = -1;   // built-in effect kind, for factory defaults (-1 = hosted plug-in)
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
            case nameof(Nota.Application.IAudioEngine.DeviceParamDefault): return Kind >= 0 ? Inner.BuiltinDeviceParamDefault(Kind, (int)args![2]!) : Access.ChainDeviceParamGet(Chain, Dev, (int)args![2]!);
            case nameof(Nota.Application.IAudioEngine.DeviceName):         return Access.ChainDeviceName(Chain, Dev);
            case nameof(Nota.Application.IAudioEngine.DeviceBypassed):     return Access.ChainDeviceBypassed(Chain, Dev);
            case nameof(Nota.Application.IAudioEngine.SetDeviceBypassed):  Access.SetChainDeviceBypassed(Chain, Dev, (bool)args![2]!); return null;
            case nameof(Nota.Application.IAudioEngine.DeviceLoadFile):        return false;   // chain devices keep params only
            // Live telemetry (meters, scopes, curves) comes from the chain device itself —
            // passing through would read/command the track's top-level device at the same index.
            case nameof(Nota.Application.IAudioEngine.DeviceGainReduction):   return Access.ChainDeviceGainReduction(Chain, Dev);
            case nameof(Nota.Application.IAudioEngine.DeviceText):            return Access.ChainDeviceText(Chain, Dev, (int)args![2]!);
            case nameof(Nota.Application.IAudioEngine.DeviceScope):           return Access.ChainDeviceScope(Chain, Dev, (float[])args![2]!, (int)args![3]!);
            case nameof(Nota.Application.IAudioEngine.DeviceLayerWave):       return Access.ChainDeviceLayerWave(Chain, Dev, (int)args![2]!, (float[])args![3]!, (int)args![4]!);
            case nameof(Nota.Application.IAudioEngine.DeviceAction):          Access.ChainDeviceAction(Chain, Dev, (int)args![2]!, (int)args![3]!, (float)args![4]!); return null;
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
