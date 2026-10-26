// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the rack card's macro column (specs/040): eight macros, the Map key,
// and each macro's target count. Filled in by US1 (turning) and US2 (mapping in place).

using Avalonia.Controls;

namespace Nota.App;

internal sealed partial class RackCard
{
    private Control MacroColumn() => Pending("Macros");
}
