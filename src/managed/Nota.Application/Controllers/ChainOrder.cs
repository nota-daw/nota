// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Next / Previous device (specs/107 FR-010): the order a track's devices are walked in — the
// MIDI effects, then the instrument, then the audio effects, as the Devices panel lays them out.
// A rack is one device (stepping does not go into its chains); a device inside a rack steps
// from its rack. Pure over the engine's counts, so the smoke test checks it directly.

using System.Collections.Generic;

namespace Nota.Application.Controllers;

public static class ChainOrder
{
    /// <summary>The track's top-level devices in chain order.</summary>
    public static List<DeviceAddress> Of(IAudioEngine e, int trackId)
    {
        var list = new List<DeviceAddress>();
        if (trackId <= 0) return list;
        for (int i = 0, n = e.TrackMidiEffectCount(trackId); i < n; i++) list.Add(new DeviceAddress(trackId, FocusSection.Midi, i));
        if (e.TrackInstrumentKind(trackId) != -2) list.Add(DeviceAddress.Instrument(trackId));
        for (int i = 0, n = e.TrackDeviceCount(trackId); i < n; i++) list.Add(new DeviceAddress(trackId, FocusSection.Audio, i));
        return list;
    }

    /// <summary>The device <paramref name="delta"/> steps from <paramref name="current"/> (its
    /// rack when it sits inside one); the first device when <paramref name="current"/> isn't in
    /// the chain any more; null past either end or on an empty chain.</summary>
    public static DeviceAddress? Step(IReadOnlyList<DeviceAddress> order, DeviceAddress? current, int delta)
    {
        if (order.Count == 0) return null;
        int at = -1;
        if (current is { } c)
        {
            var top = TopLevel(c);
            for (int i = 0; i < order.Count; i++) if (order[i] == top) { at = i; break; }
        }
        if (at < 0) return order[0];
        int to = at + delta;
        return to < 0 || to >= order.Count ? null : order[to];
    }

    /// <summary>The top-level device an address belongs to (a rack's inner device → the rack).</summary>
    public static DeviceAddress TopLevel(DeviceAddress a)
        => new(a.TrackId, a.Section, a.Section == FocusSection.Instrument ? -1 : a.Index);
}
