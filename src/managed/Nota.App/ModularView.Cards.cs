// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Device cards inside Modular nodes (specs/101, #656). A node for a chain device (MIDI effect,
// instrument, audio effect, plug-in) shows the same card body Detail draws — under Modular's own
// header and port block, so the ports keep their place and the chain-only shell (reorder, fold,
// XL, presets) stays in Detail. Each node also has XS, the compact parameter list Modular always
// had. The size is the node's own: kept beside its position in the layout sidecar and shown to
// the body through CardSizeEngineProxy, so switching it never resizes the Detail card. A device
// whose card comes in S starts at S, any other at L.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nota.Application;
using Nota.Application.Localization;

namespace Nota.App;

/// <summary>A Modular node's size: XS is the parameter list, S and L the device's card.</summary>
internal enum ModularNodeSize { Unset = 0, XS = 1, S = 2, L = 3 }

public sealed partial class ModularView
{
    private const double CardBodyH = DeviceCardKit.CardH - DeviceCardKit.HeaderH;   // a Detail card's body height

    private readonly Dictionary<string, ModularNodeSize> _savedSize = new();          // PosKey → chosen size (UI-only, sidecar)
    private readonly Dictionary<string, PluginCard.ViewState> _pluginState = new();   // PosKey → a plug-in card's page / filter
    private readonly Dictionary<string, int> _rackChain = new();                      // PosKey → the rack chain shown
    private readonly DeviceCardFactory _cardFactory = new();
    private readonly InstrumentCardFactory _instrumentFactory = new();
    private readonly MidiDeviceCardFactory _midiFactory = new();
    private readonly List<Action> _instLiveViz = new();

    /// <summary>The shipped presets, for cards that list them inside their body (rack chains).
    /// Set by the host; null leaves those lists empty.</summary>
    public IFactoryPresets? Factory { get; set; }

    private const int EffectRackKind = 5, InstrumentRackKind = 3, DrumRackKind = 4, MidiRackKind = 6;

    private static bool HasCard(GNode n) => n.Slot is Slot.Device or Slot.MidiFx or Slot.Instrument;

    private CardFamily FamilyOf(GNode n) => n.Slot switch
    {
        Slot.Instrument => CardFamily.Instrument,
        Slot.MidiFx => CardFamily.MidiEffect,
        _ => CardFamily.Effect,
    };

    /// <summary>True when the device's Detail card comes in S (research R2): a size param on a
    /// built-in, or a hosted plug-in's compact card. Racks are L only.</summary>
    private bool HasSmall(GNode n)
    {
        int t = _trackId;
        switch (n.Slot)
        {
            case Slot.Instrument:
            {
                int k = _engine.TrackInstrumentKind(t);
                if (k is InstrumentRackKind or DrumRackKind) return false;
                return k == -1 || InstrumentView.Index(_engine, t) >= 0;
            }
            case Slot.MidiFx:
                if (_engine.MidiEffectKind(t, n.SlotIndex) == MidiRackKind) return false;
                return CardSizeEngineProxy.SizeParamOf(_engine, t, CardFamily.MidiEffect, n.SlotIndex) >= 0;
            case Slot.Device:
            {
                int k = _engine.TrackDeviceBuiltinKind(t, n.SlotIndex);
                if (k == EffectRackKind) return false;
                return k == -1 || DeviceView.Index(_engine, t, n.SlotIndex) >= 0;
            }
            default: return false;
        }
    }

    /// <summary>The size a chain-device node shows: its saved choice, else S when the card has
    /// S, else L. S on a device without S reads as L.</summary>
    private ModularNodeSize ResolveSize(GNode n)
    {
        var s = _savedSize.TryGetValue(PosKey(n), out var v) ? v : ModularNodeSize.Unset;
        if (s == ModularNodeSize.Unset) s = n.HasSmall ? ModularNodeSize.S : ModularNodeSize.L;
        if (s == ModularNodeSize.S && !n.HasSmall) s = ModularNodeSize.L;
        return s;
    }

    /// <summary>Sizes a freshly built chain-device node (before its view is built).</summary>
    private void PrepareSize(GNode n)
    {
        if (!HasCard(n)) return;
        n.HasSmall = HasSmall(n);
        n.Size = ResolveSize(n);
    }

    /// <summary>Switches a node to <paramref name="size"/>, in place: the node keeps its top-left
    /// corner and its cables; nodes the user placed don't move (FR-018). Saved with the layout.</summary>
    internal void SetNodeSize(GNode n, ModularNodeSize size)
    {
        if (!HasCard(n) || size == ModularNodeSize.Unset || (size == ModularNodeSize.S && !n.HasSmall)) return;
        _savedSize[PosKey(n)] = size;
        // The resized node keeps its corner; nodes the user placed stay where they are, and the
        // auto-placed rest of the chain re-flows around the new width so nothing overlaps.
        _savedPos[PosKey(n)] = n.World;
        if (n.Size == size) return;
        // Rebuild later: the click that asked may still be running inside the old view.
        Dispatcher.UIThread.Post(Rebuild);
    }

    // ---- the card body -----------------------------------------------------------

    /// <summary>A card context for one node: the body's live refreshers run on Modular's tick,
    /// its engine is the node's size proxy, and the chain-only callbacks (presets, XL) do nothing.</summary>
    private DeviceCardContext NodeCardContext(GNode n, IAudioEngine engine)
    {
        int t = _trackId;
        string key = PosKey(n);
        return new DeviceCardContext(
            engine, t,
            _liveKnobs.Add,
            () => Changed?.Invoke(),
            (i, knob, val, fmt) => _liveKnobs.Add(() =>
            {
                if (knob.Dragging) return;
                float v = _engine.PluginParamGet(t, -1, i);
                if (Math.Abs(v - knob.Value) > 1e-3) { knob.Value = v; val.Text = (fmt ?? DeviceCardKit.Pct)(v); }
            }),
            viz => { if (viz is not null) _instLiveViz.Add(viz); },
            _ => { },                                   // presets are saved from Detail's header
            () => _rackChain.TryGetValue(key, out int c) ? c : 0,
            c => _rackChain[key] = c,
            _liveKnobs.Add,
            () => { },
            () => Dispatcher.UIThread.Post(() => { Rebuild(); Changed?.Invoke(); }),
            _ => { },
            () => { },
            Factory,
            s => StatusRequested?.Invoke(s),
            canXl: false, popout: false);
    }

    /// <summary>The node's Detail card body at its size, sized like Detail's (its width, the card
    /// body height), or null when it can't be built (the node falls back to XS).</summary>
    /// <summary>Tests only: make a node's card body fail to build, to exercise the XS fallback.</summary>
    internal Func<GNode, bool>? FailCardForTest;

    private Control? BuildCardBody(GNode n)
    {
        int t = _trackId, i = n.SlotIndex;
        string key = PosKey(n);
        bool mini = n.Size == ModularNodeSize.S;
        var proxy = CardSizeEngineProxy.Create(_engine, t, FamilyOf(n), n.Slot == Slot.Instrument ? -1 : i,
            () => n.Size == ModularNodeSize.S,
            m => SetNodeSize(n, m ? ModularNodeSize.S : ModularNodeSize.L));
        var ctx = NodeCardContext(n, proxy);

        Control body;
        double width;
        bool fullBleed = false;
        try
        {
            if (FailCardForTest?.Invoke(n) == true) throw new InvalidOperationException("test: card body fails");
            switch (n.Slot)
            {
                case Slot.Device:
                {
                    int kind = _engine.TrackDeviceBuiltinKind(t, i);
                    if (kind == -1)
                    {
                        var st = PluginState(key, mini);
                        body = PluginCard.Build(ctx, i, PluginInfo.Of(_engine, t, i), st);
                        width = st.Mini ? PluginCard.MiniWidth : PluginCard.FullWidth;
                        fullBleed = true;
                    }
                    else if (kind == EffectRackKind)
                    {
                        body = new RackCardView(ctx).BuildEffectRackBody(i); width = 700; fullBleed = true;
                    }
                    else
                    {
                        var strategy = _cardFactory.Resolve(kind);
                        body = strategy.Build(ctx, i);
                        width = strategy.AutoWidth ? double.NaN : strategy.WidthFor(proxy, t, i);
                        fullBleed = strategy.FullBleed;
                    }
                    break;
                }
                case Slot.MidiFx:
                {
                    int kind = _engine.MidiEffectKind(t, i);
                    if (kind == MidiRackKind) { body = new RackCardView(ctx).BuildMidiRackBody(i); width = 700; fullBleed = true; break; }
                    var mb = _midiFactory.Resolve(kind);
                    body = mb.Build(ctx, i);
                    width = mb.WidthFor(proxy, t, i);
                    fullBleed = mb.FullBleed;
                    break;
                }
                case Slot.Instrument:
                {
                    int kind = _engine.TrackInstrumentKind(t);
                    if (kind == InstrumentRackKind) { body = new RackCardView(ctx).BuildInstrumentRackBody(out _); width = 700; fullBleed = true; break; }
                    if (kind == DrumRackKind) { body = new RackCardView(ctx).BuildDrumRackBody(out _); width = 700; fullBleed = true; break; }
                    if (kind == -1)
                    {
                        var st = PluginState(key, mini);
                        body = PluginCard.Build(ctx, -1, PluginInfo.Of(_engine, t, -1), st);
                        width = st.Mini ? PluginCard.MiniWidth : PluginCard.FullWidth;
                        fullBleed = true;
                        break;
                    }
                    var strategy = _instrumentFactory.Resolve(kind, _engine.PluginParamCount(t, -1) > 0);
                    body = strategy.Build(ctx);
                    // A card that still draws its own frame is hosted whole, at its own size.
                    if (!strategy.BodyOnly) return Host(body, double.NaN, double.NaN, fullBleed: true, n.Bypassed);
                    width = strategy.WidthFor(proxy, t);
                    fullBleed = true;
                    break;
                }
                default: return null;
            }
        }
        catch (Exception ex)
        {
            // A body that can't be hosted here must not take the graph down: show XS instead.
            Console.Error.WriteLine($"[modular] card body for {n.Title} failed: {ex.Message}");
            return null;
        }
        var host = Host(body, width, CardBodyH, fullBleed, n.Bypassed);
        AttachModulateMenu(host, n);
        return host;
    }

    /// <summary>A right-click on a card knob opens Modular's parameter menu — modulate from a
    /// source, edit / remove its modulation, expose it as a CV output — as on an XS row; Type
    /// value… and Reset stay in it. Caught on the way down, before the knob's own menu. The
    /// instrument's knobs keep their card menu (its parameters are patched by dragging, as at XS).</summary>
    private void AttachModulateMenu(Control host, GNode n)
    {
        host.AddHandler(ContextRequestedEvent, (_, e) =>
        {
            if (e.Handled || e.Source is not Visual from) return;
            foreach (var anc in from.GetSelfAndVisualAncestors())
            {
                if (ReferenceEquals(anc, host)) break;
                if (anc is not Control c || TargetOfControl(c, n) is not { } r) continue;
                if (r.Kind == 1) return;
                e.Handled = true;
                ShowModulateMenu(c, r.Kind, r.Device, r.Param, ParamName(_trackId, r.Kind, r.Device, r.Param));
                return;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private PluginCard.ViewState PluginState(string key, bool mini)
    {
        if (!_pluginState.TryGetValue(key, out var st)) _pluginState[key] = st = new PluginCard.ViewState();
        st.Mini = mini;
        return st;
    }

    // The body in a box the size of a Detail card body; the card's own inset unless it bleeds;
    // a fixed text size and Tab scope like Detail's card; the inactive look while bypassed.
    private static Control Host(Control body, double width, double height, bool fullBleed, bool bypassed)
    {
        var host = new Border
        {
            Width = width, Height = height, ClipToBounds = true, Background = NotaPalette.SurfaceRaised,
            BorderBrush = Divider, BorderThickness = new Thickness(0, 1, 0, 0),
            Child = fullBleed ? body : new Border { Padding = new Thickness(NotaSpace.DeviceInset), Child = body },
        };
        ValueEntry.SetScope(host, true);
        NotaTextSize.SetFixed(host, true);
        if (bypassed) host.AttachedToVisualTree += (_, _) => Inactive.Set(host, true, interactive: true);
        return host;
    }

    // ---- the size chip in the header -----------------------------------------------

    /// <summary>XS · S · L in the node header (S only when the card has it) — the same segments
    /// as Detail's S / L toggle. Picking one resizes the node in place.</summary>
    private Control SizeChip(GNode n)
    {
        var sizes = new List<ModularNodeSize> { ModularNodeSize.XS };
        if (n.HasSmall) sizes.Add(ModularNodeSize.S);
        sizes.Add(ModularNodeSize.L);
        var labels = sizes.ConvertAll(z => z switch
        {
            ModularNodeSize.XS => Loc.T("modular.size_xs"),
            ModularNodeSize.S => Loc.T("modular.size_s"),
            _ => Loc.T("modular.size_l"),
        });
        var seg = DeviceCardKit.Segments(labels.ToArray(), () => sizes.IndexOf(n.Size),
            i => SetNodeSize(n, sizes[i]), out _, padX: 0, minSegWidth: 18, fontSize: 8);
        var wrap = new Border { Child = seg, VerticalAlignment = VerticalAlignment.Center };
        // A press on the chip is not a header drag.
        wrap.AddHandler(PointerPressedEvent, (_, e) => e.Handled = true, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        ToolTip.SetTip(wrap, Loc.T("modular.size_tip"));
        return wrap;
    }
}
