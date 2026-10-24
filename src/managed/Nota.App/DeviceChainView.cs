// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices (mockup 1d): the selected track's device chain as horizontal
// cards, signal order left→right. Each card has a 26px header (power dot =
// bypass, name, type tag, reorder ◀▶, remove ✕, drag handle ⠿) over a body:
// built-in EQ → interactive response curve, Compressor → param bars + GR meter,
// other built-ins → param bars, hosted plugins → editor stub + latency. Then a
// Sends card (M6-1) and a dashed "+ Add device" target. Reads over the engine's
// device API; edits go straight to the engine.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using Nota.Presentation;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

public sealed partial class DeviceChainView : UserControl
{
    // Built-in instrument editor (Synth / Physical): graphs + faders re-read live so
    // automation moves show (RefreshSynthLive). _instLiveViz updates the active card's graphs.
    private Action? _instLiveViz;
    private readonly System.Collections.Generic.List<(int idx, Knob fader, TextBlock val, System.Func<float, string>? fmt)> _instFaders = new();
    // Live device visuals (compressor gain-reduction meter) refreshed on the UI tick.
    private readonly System.Collections.Generic.List<Action> _deviceLiveRefreshers = new();
    // Rack chain param faders that follow their mapped macro (and automation); cleared each Rebuild.
    private readonly System.Collections.Generic.List<Action> _rackParamRefreshers = new();
    private int _rackSelChain;   // which rack/drum chain's device area is shown (persists across Rebuild)

    /// <summary>Raised to save a rack chain's instrument as a preset; arg is the chain index.</summary>
    public event Action<int>? RackPresetSaveRequested;

    private readonly IAudioEngine _engine;
    private readonly IFactoryPresets _factory;
    private readonly IPluginCatalog? _catalog;   // maps a copied plugin's id → catalog index for paste
    private readonly IDrumKits? _kits;           // the Drum Rack card's preset list
    private readonly IMosaicPacks? _mosaic;      // Nota Mosaic's pack presets, beside its factory ones
    private readonly DeviceCardFactory _cardFactory = new();
    private readonly InstrumentCardFactory _instrumentFactory = new();
    private readonly MidiDeviceCardFactory _midiFactory = new();
    // The chain sits close to the panel edges: a 6px inset all round and 6px between cards —
    // just room for the selected card's focus ring (3px gap + 1.5px ring) — so the cards fill
    // the Devices panel instead of floating in it.
    internal const double ChainInset = 6;
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal, Spacing = ChainInset, Margin = new Thickness(ChainInset) };
    private int _trackId = -1;

    /// <summary>Raised after a change that affects track headers (remove/reorder).</summary>
    public event Action? Changed;

    /// <summary>Raised when this view reorders / removes effects (midi=false) or MIDI effects
    /// (midi=true) on a track: map is old chain index → new index, or -1 when the device is gone.
    /// Lets index-addressed state elsewhere (the arrangement's automation target) follow it.</summary>
    public event Action<int, bool, Func<int, int>>? DevicesRemapped;

    /// <summary>Raised to save a device/instrument as a preset (M7-4c); arg is the
    /// device index, or -1 for the track's instrument. MainWindow does the prompt+save.</summary>
    public event Action<int>? PresetSaveRequested;

    /// <summary>Raised when a browser item is dropped on the device panel; MainWindow
    /// routes it (effect → the shown track at the target slot; instrument → replaces the
    /// track's instrument, or a rack chain / drum pad).</summary>
    public event Action<BrowserItem, DeviceDropTarget>? ItemDropped;

    /// <summary>A one-line message for the status bar (e.g. a kit that loaded incompletely).</summary>
    public event Action<string>? StatusMessage;

    /// <summary>The "+" at the end of the chain was clicked: MainWindow opens the browser on
    /// Audio effects with the keyboard in its search.</summary>
    public event Action? AddDeviceRequested;

    // Accent overlay shown while a browser item is dragged over the panel.
    private readonly Border _dropGlow = new()
    {
        IsVisible = false, IsHitTestVisible = false,
        BorderThickness = new Thickness(2), CornerRadius = NotaRadius.Body,
        BorderBrush = NotaPalette.AccentBright,
        Background = NotaPalette.Wash(NotaPalette.AccentBright, 0x14),
        Margin = new Thickness(2),
    };

    public DeviceChainView(IAudioEngine engine, IFactoryPresets factory, IPluginCatalog? catalog = null, IDrumKits? kits = null, IMosaicPacks? mosaic = null)
    {
        _engine = engine;
        _factory = factory;
        _catalog = catalog;
        _kits = kits;
        _mosaic = mosaic;
        Focusable = true;   // so Ctrl+C/X/V + Delete on a selected device reach OnKeyDown
        var scroller = new ScrollViewer
        {
            Content = _row,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        // The chain only scrolls sideways, and the scroller ignores a plain (vertical) mouse wheel,
        // so a wheel no card control took moves the chain instead of doing nothing (#262).
        scroller.PointerWheelChanged += (_, e) =>
        {
            if (e.Handled || !WheelInput.IsVertical(e)) return;
            double max = Math.Max(0, scroller.Extent.Width - scroller.Viewport.Width);
            double x = Math.Clamp(scroller.Offset.X - WheelInput.Pixels(e.Delta.Y), 0, max);
            if (Math.Abs(x - scroller.Offset.X) < 0.01) return;
            scroller.Offset = scroller.Offset.WithX(x);
            e.Handled = true;
        };
        Content = new Panel { Children = { scroller, _dropGlow, _dropLayer } };
        HookBrowserDrop();
    }

    public void Show(int trackId) { _trackId = trackId; _rackSelChain = 0; _selDeviceIndex = -1; Rebuild(); }

    /// <summary>Forgets every card's remembered preset label + A/B slots (a project was
    /// opened / created, so track ids no longer mean the same tracks).</summary>
    public void ForgetCardState() => _cardExtra.Clear();

    /// <summary>Forgets the instrument card's remembered preset label + A/B slots on a
    /// track (its instrument was just swapped in place, possibly for one of the same kind).</summary>
    public void ForgetInstrumentState(int trackId)
    { if (_cardExtra.TryGetValue(trackId, out var d)) d.Remove(ExtraKey(ChainKind.Instrument, -1)); }

    /// <summary>Records a preset applied outside the card (from the browser) so its card shows
    /// the name and ‹ › continue from it. <paramref name="presetId"/> is the factory id, or ""
    /// for a user preset. The card's A/B slots start fresh for the new device.</summary>
    internal void RememberPreset(int trackId, ChainKind kind, int deviceIndex, string name, string presetId)
    {
        if (trackId <= 0) return;
        TrackExtras(trackId).Remove(ExtraKey(kind, deviceIndex));   // a new device: drop any stale entry
        var e = ExtraFor(trackId, kind, deviceIndex);
        e.Preset = name; e.PresetId = presetId;
    }
    public void Refresh() { if (_trackId > 0) Rebuild(); }
    public int TrackId => _trackId;

    private bool IsInstrumentTrack(int trackId)
    {
        int n = _engine.TrackCount;
        for (int i = 0; i < n; i++)
            if (_engine.TryGetTrackInfo(i, out var ti) && ti.Id == trackId) return ti.IsInstrument;
        return false;
    }

    private bool IsReturnTrack(int trackId) => _engine.TrackReturnIndex(trackId) >= 0;

    private bool IsGroupTrack(int trackId)
    {
        int n = _engine.TrackCount;
        for (int i = 0; i < n; i++)
            if (_engine.TryGetTrackInfo(i, out var ti) && ti.Id == trackId) return ti.IsGroup;
        return false;
    }

    // Clearing the row takes cards out of the tree, and a card losing pointer capture can
    // ask for a rebuild from inside this one; the nested call then filled the row and this
    // one appended a second set of cards after it (#202). Run it once more afterwards instead.
    private bool _rebuilding, _rebuildAgain;

    private void Rebuild()
    {
        if (_rebuilding) { _rebuildAgain = true; return; }
        _rebuilding = true;
        try
        {
            do { _rebuildAgain = false; RebuildRow(); } while (_rebuildAgain);
        }
        finally { _rebuilding = false; }
    }

    private void RebuildRow()
    {
        _row.Children.Clear();
        _instrumentCard = null;
        _instLiveViz = null; _instFaders.Clear();   // rebuilt only when a built-in instrument card is shown
        _deviceLiveRefreshers.Clear();               // compressor GR meter/curve
        _rackParamRefreshers.Clear();                // rack chain param faders (macro live-follow)
        _engine.SetAuditionTrack(-1);                // re-armed by the Drum Rack card if shown (pad audition)
        if (_trackId <= 0) { _row.Children.Add(Hint("Select a track")); return; }

        // MIDI effects sit before the instrument (they shape the note stream).
        int midiCount = _engine.TrackMidiEffectCount(_trackId);
        for (int i = 0; i < midiCount; i++) _row.Children.Add(MidiDeviceCard(i, midiCount));

        if (IsInstrumentTrack(_trackId)) _row.Children.Add(_instrumentCard = InstrumentCard());

        int count = _engine.TrackDeviceCount(_trackId);
        for (int i = 0; i < count; i++) _row.Children.Add(DeviceCard(i, count));

        int returns = _engine.ReturnTrackCount;
        if (returns > 0 && !IsReturnTrack(_trackId) && !IsGroupTrack(_trackId)) _row.Children.Add(SendsCard());

        _row.Children.Add(AddDeviceCard());
    }

    // ---- device card ------------------------------------------------------

    private Control DeviceCard(int index, int count)
    {
        int kind = _engine.TrackDeviceBuiltinKind(_trackId, index);
        bool bypassed = _engine.DeviceBypassed(_trackId, index);
        string name = _engine.DeviceName(_trackId, index);
        if (_engine.DeviceFolded(_trackId, index)) return FoldedStrip(ChainKind.Effect, index, name, bypassable: true, bypassed);   // built-in or plug-in

        Control body;
        double width;
        bool fullBleed = false;
        string tag = kind >= 0 ? "BUILT-IN" : "PLUGIN";
        if (kind == -1) return PluginCardFor(ChainKind.Effect, index, count);   // hosted VST3 / AU
        if (kind == 5)   // Audio Effect Rack — full-bleed body (fills the shell; no body inset
        {                // that would overflow its fixed width and skew hit-testing).
            body = new RackCardView(NewCardContext()).BuildEffectRackBody(index); width = 700; fullBleed = true;
        }
        else
        {
            var strategy = _cardFactory.Resolve(kind);
            body = strategy.Build(NewCardContext(), index);
            width = strategy.AutoWidth ? double.NaN : strategy.Width;   // NaN → card sizes to content
            fullBleed = strategy.FullBleed;
            if (strategy.Subtitle is { } sub) tag = sub;
        }
        // Effects share the same shell as instruments. Non-full-bleed bodies get the
        // almanac body inset (6).
        var spec = new ShellSpec(
            Name: name, Subtitle: tag, DeviceIndex: index, Count: count, Bypassed: bypassed, Bypassable: true,
            CanMove: true, CanDelete: true, PresetKind: kind, IsInstrument: false, Width: width, Kind: ChainKind.Effect);
        return BuildCardShell(spec, fullBleed ? body : new Border { Padding = new Thickness(NotaSpace.DeviceInset), Child = body });
    }

    // A fresh seam over this view's engine/track + live-refresh registry + orchestration
    // callbacks, handed to a card strategy. Cheap; only built while (re)building the row.
    private DeviceCardContext NewCardContext()
        => new(_engine, _trackId, _deviceLiveRefreshers.Add, () => Changed?.Invoke(),
               (i, k, v, f) => _instFaders.Add((i, k, v, f)),
               viz => _instLiveViz = viz,
               i => PresetSaveRequested?.Invoke(i),
               () => _rackSelChain, v => _rackSelChain = v,
               _rackParamRefreshers.Add,
               () => { foreach (var r in _rackParamRefreshers) r(); },
               Rebuild,
               c => RackPresetSaveRequested?.Invoke(c),
               ClearDropHints,
               _factory,
               s => StatusMessage?.Invoke(s));

    // ---- instrument / sends / add ----------------------------------------

    private Control SendsCard()
    {
        var knobs = new WrapPanel();
        int shown = 0;
        for (int i = 0; i < _engine.TrackCount; i++)
        {
            if (!_engine.TryGetTrackInfo(i, out var ti) || !ti.IsReturn) continue;
            int bus = _engine.TrackReturnIndex(ti.Id);
            if (bus < 0) continue;
            char letter = (char)('A' + bus);

            var value = new TextBlock { Text = Pct(_engine.GetTrackSend(_trackId, bus)), FontSize = 8, Foreground = TextPrimary };
            value.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
            var knob = new Knob(_engine.GetTrackSend(_trackId, bus), 1.0) { Accent = true, Default = 0, Format = v => Pct((float)v) };
            int b = bus;
            knob.ValueChanged += v => { _engine.SetTrackSend(_trackId, b, (float)v); value.Text = Pct((float)v); };
            // Follow the Session / Mixer send faders and automation (#138).
            _deviceLiveRefreshers.Add(() =>
            {
                if (knob.Dragging) return;
                float v = _engine.GetTrackSend(_trackId, b);
                if (Math.Abs(v - knob.Value) > 1e-3) { knob.Value = v; value.Text = Pct(v); }
            });
            knobs.Children.Add(KnobCell($"Send {letter}", knob, value));
            shown++;
        }
        Control body = shown > 0 ? knobs : new TextBlock { Text = "No return tracks", FontSize = 9, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        var spec = new ShellSpec(
            Name: "Sends", Subtitle: "", DeviceIndex: -2, Count: 0, Bypassed: false, Bypassable: false,
            CanMove: false, CanDelete: false, PresetKind: -1, IsInstrument: false, Width: shown > 2 ? 200 : 140,
            Kind: ChainKind.Effect, Compact: true, Utility: true);
        return BuildCardShell(spec, new Border { Padding = new Thickness(10), Child = body });
    }

    // The end of the chain: a slim "+" key. Its dashed outline shows only while a browser
    // drag is over the chain, so an empty dock stays quiet (spec 012).
    private Rectangle? _addDash;

    private Control AddDeviceCard()
    {
        // Avalonia Border can't dash its own stroke; overlay a dashed rectangle.
        _addDash = new Rectangle
        {
            Stroke = BorderStrong, StrokeThickness = 1, IsVisible = false,
            StrokeDashArray = new AvaloniaList<double>(4, 3), RadiusX = NotaRadius.ControlValue, RadiusY = NotaRadius.ControlValue,
        };
        var plus = new Glyph(GlyphKind.Plus, 12) { Foreground = TextTertiary };
        var host = new Grid { Children = { _addDash, plus } };
        var card = new Border
        {
            Width = 28, Height = CardH, Background = Brushes.Transparent, Child = host,
            CornerRadius = NotaRadius.Control, Cursor = new Cursor(StandardCursorType.Hand),
        };
        // A real button (#97): a click opens the browser on Audio effects, typing into its search.
        card.PointerEntered += (_, _) => { card.BindResource(Border.BackgroundProperty, "Brush.SurfaceHover"); plus.BindResource(Glyph.ForegroundProperty, "Brush.TextSecondary"); };
        card.PointerExited += (_, _) => { card.Background = Brushes.Transparent; plus.Foreground = TextTertiary; };
        card.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            AddDeviceRequested?.Invoke();
            e.Handled = true;
        };
        ToolTip.SetTip(card, "Add an audio effect: search the browser, or drop a device here");
        return card;
    }

}
