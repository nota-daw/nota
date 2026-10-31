// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the instrument as a device in the chain (specs/043): the Replace… list
// in the instrument's menu, and Move to track ▸ for every device.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Nota.Application;
using Nota.Presentation;

namespace Nota.App;

public sealed partial class DeviceChainView
{
    // The instruments the Browser lists, as a submenu: built-ins, the two racks, then installed
    // plug-in instruments. Picking one hands it to the host, which swaps it in place.
    private MenuItem ReplaceInstrumentMenu()
    {
        var menu = MenuKit.Item("Replace…", GlyphKind.Device);
        void Add(ItemsControl into, BrowserItem item, GlyphKind glyph)
            => into.Items.Add(MenuKit.Item(item.Name, glyph, () => InstrumentReplaceRequested?.Invoke(item)));

        var builtins = BuiltinDeviceCatalog.OfType(BuiltinDeviceType.Instrument).OrderBy(d => d.Name).ToList();
        foreach (var d in builtins.Where(d => d.Kind is not (3 or 4)))
            Add(menu, new BrowserItem { Name = d.Name, Kind = BrowserItemKind.BuiltinInstrument, BuiltinKind = d.Kind }, GlyphKind.Device);
        menu.Items.Add(new Separator());
        foreach (var d in builtins.Where(d => d.Kind is 3 or 4))
            Add(menu, new BrowserItem { Name = d.Name, Kind = BrowserItemKind.BuiltinInstrument, BuiltinKind = d.Kind }, GlyphKind.Device);

        var plugins = PluginInstruments();
        if (plugins.Count > 0)
        {
            menu.Items.Add(new Separator());
            var sub = MenuKit.Item("Plug-ins", GlyphKind.Plug);
            foreach (var p in plugins) Add(sub, p, GlyphKind.Plug);
            menu.Items.Add(sub);
        }
        return menu;
    }

    // Installed plug-in instruments, by name (the catalog line reads "Name | Format | inst | Vendor | …").
    private List<BrowserItem> PluginInstruments()
    {
        var list = new List<BrowserItem>();
        if (_catalog is not { } catalog) return list;
        for (int i = 0; i < catalog.Count; i++)
        {
            string? desc = catalog.Description(i);
            if (desc is null || !desc.Contains("| inst |")) continue;
            var parts = desc.Split('|');
            string name = parts[0].Trim(), fmt = parts.Length > 1 ? parts[1].Trim() : "";
            list.Add(new BrowserItem { Name = fmt.Length > 0 ? $"{name} ({fmt})" : name, Kind = BrowserItemKind.PluginInstrument, CatalogIndex = i });
        }
        return list.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // The shown track's Instrument Rack card, for drops that come from elsewhere (a Session column).
    private RackCardView? _instrumentRackView;

    /// <summary>Hands an instrument preset to the shown track's Instrument Rack card, which adds it
    /// as a new chain. A folded rack has no card on screen: one is built for the drop. False when
    /// the shown track has no Instrument Rack.</summary>
    internal bool DropOnInstrumentRack(BrowserItem item)
    {
        if (_engine.TrackInstrumentKind(_trackId) != 3) return false;
        var v = _instrumentRackView;
        if (v is null)
        {
            v = new RackCardView(NewCardContext());
            v.BuildInstrumentRackBody(out _);
        }
        v.DropAsNewChain(item);
        return true;
    }

    /// <summary>After a device moved or was copied to another track (specs/043): on the source the
    /// cards after it close the gap (a moved instrument's track is gone), on the target the cards
    /// after the insert shift up, and the device's own preset label and A/B slots follow it — a
    /// copy takes only the label, as a paste does. Call before the panel rebuilds.</summary>
    internal void DeviceTransferred(DeviceMoveRequest r, int newIndex)
    {
        var k = r.Family switch { DeviceFamily.Midi => ChainKind.Midi, DeviceFamily.Instrument => ChainKind.Instrument, _ => ChainKind.Effect };
        int srcIdx = k == ChainKind.Instrument ? -1 : r.Index, dstIdx = k == ChainKind.Instrument ? -1 : newIndex;
        TrackExtras(r.SrcTrackId).TryGetValue(ExtraKey(k, srcIdx), out var own);
        // A moved instrument's track is gone; its card state stays under it for an undo.
        if (!r.Copy && k != ChainKind.Instrument) RemapExtras(r.SrcTrackId, k, i => i == srcIdx ? -1 : i > srcIdx ? i - 1 : i);
        if (k != ChainKind.Instrument)
        {
            int last = (k == ChainKind.Midi ? _engine.TrackMidiEffectCount(r.DstTrackId) : _engine.TrackDeviceCount(r.DstTrackId)) - 1;
            if (dstIdx != last) RemapExtras(r.DstTrackId, k, i => i >= dstIdx ? i + 1 : i);
        }
        var dst = TrackExtras(r.DstTrackId);
        // The instrument it replaced keeps its card state aside: an undo brings that instrument
        // back (another id, so ExtraFor swaps the two), a redo this one.
        dst.Remove(ExtraKey(k, dstIdx), out var displaced);
        if (own is null) { if (displaced != null && k == ChainKind.Instrument) dst[ExtraKey(k, dstIdx)] = new CardExtra { Sig = CardSig(r.DstTrackId, k, dstIdx), Wrapped = displaced }; return; }
        string sig = CardSig(r.DstTrackId, k, dstIdx);
        var landed = r.Copy ? new CardExtra { Sig = sig, Preset = own.Preset, PresetId = own.PresetId } : own;
        if (!r.Copy) own.Sig = sig;
        if (k == ChainKind.Instrument) landed.Wrapped = displaced;
        dst[ExtraKey(k, dstIdx)] = landed;
    }

    // Move to track ▸: every track that can take the device, this one left out — audio effects
    // go on any chain (returns and the master too), MIDI effects and the instrument only on
    // instrument tracks. The host does the move (one undo step) and asks before a track goes.
    private IEnumerable<Control> MoveToTrackGroup(ChainKind kind, int di)
    {
        var menu = MenuKit.Item("Move to track", GlyphKind.Track);
        foreach (var (id, name) in TracksThatTake(_engine, kind, _trackId))
            menu.Items.Add(MenuKit.Item(name, null, () => MoveRequested?.Invoke(new DeviceMoveRequest(_trackId, FamilyOf(kind), di, id, false))));
        menu.IsEnabled = menu.Items.Count > 0;
        return new Control[] { menu };
    }

    // Move to track ▸ for several selected devices (specs/044): tracks that can take all of them.
    // The host moves them in one undo step, the instrument last (it takes the source track).
    private IEnumerable<Control> MoveSelectionToTrackGroup()
    {
        var menu = MenuKit.Item("Move to track", GlyphKind.Track);
        var kind = _sel.HasInstrument || _sel.Indices(DeviceSection.Midi).Any() ? ChainKind.Midi : ChainKind.Effect;
        foreach (var (id, name) in TracksThatTake(_engine, kind, _trackId))
            menu.Items.Add(MenuKit.Item(name, null, () =>
            {
                var moves = _sel.Members.Select(m => new DeviceMoveRequest(_trackId, FamilyOf(KindOf(m.Section)), m.Index, id, false)).ToList();
                ClearDeviceSelection();
                MovesRequested?.Invoke(moves);
            }));
        menu.IsEnabled = menu.Items.Count > 0;
        return new Control[] { menu };
    }

    internal static DeviceFamily FamilyOf(ChainKind k)
        => k switch { ChainKind.Midi => DeviceFamily.Midi, ChainKind.Instrument => DeviceFamily.Instrument, _ => DeviceFamily.Audio };

    /// <summary>The tracks a device of <paramref name="kind"/> can move to, in track order, without
    /// <paramref name="exceptTrackId"/>: (id, name).</summary>
    internal static List<(int Id, string Name)> TracksThatTake(IAudioEngine engine, ChainKind kind, int exceptTrackId)
    {
        var list = new List<(int, string)>();
        for (int i = 0; i < engine.TrackCount; i++)
        {
            if (!engine.TryGetTrackInfo(i, out var ti) || ti.Id == exceptTrackId) continue;
            if (kind != ChainKind.Effect && !(ti.IsInstrument)) continue;
            list.Add((ti.Id, TrackNames.Of(engine, ti)));   // as the Arrangement names it
        }
        if (kind == ChainKind.Effect && engine.MasterTrackId != exceptTrackId) list.Add((engine.MasterTrackId, "Master"));
        return list;
    }

    internal static bool CanTake(IAudioEngine engine, ChainKind kind, int trackId, int exceptTrackId)
    {
        if (trackId == exceptTrackId || trackId == 0) return false;
        if (kind == ChainKind.Effect) return true;
        for (int i = 0; i < engine.TrackCount; i++)
            if (engine.TryGetTrackInfo(i, out var ti) && ti.Id == trackId) return ti.IsInstrument;
        return false;
    }
}

/// <summary>Move (or, <paramref name="Copy"/>, copy) a device to another track (specs/043).</summary>
public sealed record DeviceMoveRequest(int SrcTrackId, DeviceFamily Family, int Index, int DstTrackId, bool Copy);
