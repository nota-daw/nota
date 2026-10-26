// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the rack card's Chains tab (specs/040): the chain list and the chosen
// chain's devices left to right as compact cards. Filled in by US1, US2 (mapping) and US6
// (the MIDI section of an Instrument Rack chain).

using Avalonia.Controls;

namespace Nota.App;

internal sealed partial class RackCard
{
    private Control ChainsTab() => Pending(_profile.ChainsHint);
}
