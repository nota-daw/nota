// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the rack card's Macros tab (specs/040): the chosen macro's name, value,
// response curve (or Mixed) and its targets with their ranges. Filled in by US2.

using Avalonia.Controls;

namespace Nota.App;

internal sealed partial class RackCard
{
    private Control MacrosTab() => Pending("No targets yet. Press Map, then click parameters on the Chains tab.");
}
