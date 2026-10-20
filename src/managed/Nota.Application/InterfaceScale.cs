// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The app-wide interface scale (spec 027, #38): which percentages Preferences offers and
// what a stored value means. Pure — no UI, no I/O — so the smoke test can walk it; the app
// layer applies the factor to its windows. A hand-edited or future value never throws: it
// reads as 100 %.

using System.Collections.Generic;
using System.Linq;

namespace Nota.Application;

public static class InterfaceScale
{
    public const int Default = 100;

    /// <summary>The offered scales in percent, smallest first (the picker's order).</summary>
    public static IReadOnlyList<int> Presets { get; } = new[] { 75, 100, 125, 150, 200 };

    /// <summary>The stored value if it is a preset, else <see cref="Default"/>.</summary>
    public static int Normalize(int percent) => Presets.Contains(percent) ? percent : Default;

    /// <summary>The layout factor for a stored value (1.25 for 125).</summary>
    public static double Factor(int percent) => Normalize(percent) / 100.0;
}
