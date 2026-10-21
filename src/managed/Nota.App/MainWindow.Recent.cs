// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// "Add track ▸ Recent" (spec 034): the window records every instrument the insert service
// puts on a track (and the plain "Instrument" track's Volt) into the settings, and hands the
// arrangement and Session menus the items that add one again after the clicked track. The
// MCP tools record through the same settings (Nota.Mcp InstrumentTools / PluginTools).

using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;
using Nota.Presentation;

namespace Nota.App;

public partial class MainWindow
{
    private IPluginCatalog? RecentCatalog => App.Services.GetService<IPluginCatalog>();

    private void HookRecentInstruments()
    {
        Timeline.RecentInstrumentItems = RecentInstrumentMenuItems;
        if (_session is not null) _session.RecentInstrumentItems = RecentInstrumentMenuItems;
    }

    private void OnInstrumentAdded(BrowserItem item)
    {
        if (_vm is null) return;
        RecentInstrument? entry = item.Kind switch
        {
            BrowserItemKind.BuiltinInstrument when item.BuiltinKind >= 0 => RecentInstrument.Builtin(item.BuiltinKind, item.Name),
            BrowserItemKind.PluginInstrument when RecentCatalog?.Id(item.CatalogIndex) is { Length: > 0 } id => RecentInstrument.Plugin(id, item.Name),
            _ => null,
        };
        if (entry is not null) RecentInstruments.RecordAndSave(_vm.Settings, entry);
    }

    // The instrument a track plays now — after "Add track ▸ Instrument" (Volt) or an
    // instrument preset. Built-in only: a preset never brings a plug-in.
    private void RecordTrackInstrument(int trackId)
    {
        if (_vm is null || trackId <= 0) return;
        int kind = Engine.TrackInstrumentKind(trackId);
        if (kind < 0) return;
        RecentInstruments.RecordAndSave(_vm.Settings, RecentInstrument.Builtin(kind, Engine.DeviceName(trackId, -1)));
    }

    // The menu items for the recent list: each adds a track with that instrument right after
    // the anchor (inside it for a group), as one undo step — like "Add track ▸ Instrument".
    private IReadOnlyList<MenuItem> RecentInstrumentMenuItems(int anchorTrackId)
    {
        if (_vm is null) return [];
        var catalog = RecentCatalog;
        var shown = RecentInstruments.Visible(_vm.Settings.Current.RecentInstruments,
            e => !e.IsPlugin || (catalog?.IndexOfId(e.Id) ?? -1) >= 0);
        return shown.Select(e => MenuKit.Item(e.Name, e.IsPlugin ? GlyphKind.Plug : GlyphKind.Device,
            () => AddRecentInstrument(e, anchorTrackId))).ToList();
    }

    private void AddRecentInstrument(RecentInstrument e, int anchorTrackId)
    {
        if (_vm is null) return;
        BrowserItem item;
        if (e.IsPlugin)
        {
            int index = RecentCatalog?.IndexOfId(e.Id) ?? -1;
            if (index < 0) { _vm.StatusText = $"{e.Name} is no longer installed."; return; }
            item = new BrowserItem { Name = e.Name, Kind = BrowserItemKind.PluginInstrument, CatalogIndex = index };
        }
        else item = new BrowserItem { Name = e.Name, Kind = BrowserItemKind.BuiltinInstrument, BuiltinKind = e.BuiltinKindId };

        int trackId;
        Engine.BeginUndoGroup();
        try
        {
            trackId = Insertion.CreateInstrumentTrack(item, 0.0);   // records it as most recent
            if (trackId > 0) Timeline.PlaceNewTrack(trackId, anchorTrackId);
        }
        finally { Engine.EndUndoGroup(); }
        if (trackId <= 0) { _vm.StatusText = $"Couldn't load {e.Name}."; return; }
        Timeline.Refresh();
        Timeline.RevealTrack(trackId);
        if (anchorTrackId > 0) Timeline.Select(trackId, -1);
        _session?.Refresh();
        if (_modular?.IsVisible == true) _modular.Refresh();
        _vm.StatusText = $"Added {e.Name} (track {trackId})";
    }
}
