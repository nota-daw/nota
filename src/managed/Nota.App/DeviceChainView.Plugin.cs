// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — a hosted plug-in (VST3 / AU) in the shared shell: the header names the
// plug-in and its format, the S / L toggle picks the card size (see PluginCard). The size,
// page and filter ride on the card's extra, so they follow the device through moves. The
// body stands on a picture of the plug-in's window once it has been open (specs/046).

using Avalonia;
using Avalonia.Controls;

namespace Nota.App;

public sealed partial class DeviceChainView
{
    private Control PluginCardFor(ChainKind k, int di, int count)
    {
        var state = Extra(k, di).Plugin;
        var ctx = NewCardContext();
        var info = PluginInfo.Of(_engine, _trackId, di);
        var body = PluginCard.Build(ctx, di, info, state);
        double width = state.Mini ? PluginCard.MiniWidth : PluginCard.FullWidth;
        var toggle = MidiCardHeader.SizeToggle(ctx, () => state.Mini, m => state.Mini = m);
        bool effect = k == ChainKind.Effect;
        var spec = new ShellSpec(
            Name: info.Name, Subtitle: info.Format, DeviceIndex: di, Count: count,
            Bypassed: _engine.DeviceBypassed(_trackId, effect ? di : -1), Bypassable: true,
            CanMove: effect, CanDelete: true, PresetKind: -1, IsInstrument: !effect, Width: width, Kind: k,
            HeaderExtra: toggle, Compact: state.Mini);
        // The picture of the plug-in's window stands behind the info section (PluginCard).
        Control padded = new Border { Padding = new Thickness(NotaSpace.DeviceInset), Child = body };
        return BuildCardShell(spec, padded);
    }
}
