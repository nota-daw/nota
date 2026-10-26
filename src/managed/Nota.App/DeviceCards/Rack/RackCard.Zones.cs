// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the rack card's Zones / Select tab (specs/040): key and velocity zones
// with a hover probe (Instrument, MIDI) and select ranges with the selector (Audio, MIDI).
// Filled in by US1, US3 and US4.

using Avalonia.Controls;

namespace Nota.App;

internal sealed partial class RackCard
{
    private Control ZonesTab() => Pending(_profile.KeyZones ? "Drag zone edges · hover to test a note" : "Drag the selector or map it to a macro");
}
