// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the rack card's right panel (specs/040): the chosen chain (mix, zones,
// duplicate, delete) and the rack's output (Rack out / Output). Filled in by US1, US3 and US4.

using Avalonia.Controls;

namespace Nota.App;

internal sealed partial class RackCard
{
    private Control ChainPanel() => Pending("Chain");

    private Control OutputPanel() => Pending(_profile.OutputTab);

    private string StatusText()
    {
        int n = ChainCount, m = MappedMacroCount();
        return $"{n} chain{(n == 1 ? "" : "s")} · {m} macro{(m == 1 ? "" : "s")} mapped";
    }
}
