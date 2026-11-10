// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System.Collections.Generic;
using System.Linq;

namespace Nota.Application;

/// <summary>Text size (Settings → Appearance, spec 038): a step in points added to every text
/// the app draws, independent of the interface scale.</summary>
public static class TextSize
{
    public const int Default = 0;

    /// <summary>The offered steps, smallest first (the picker's order).</summary>
    public static IReadOnlyList<int> Steps { get; } = new[] { -1, 0, 1, 2 };

    /// <summary>The stored value if it is offered, else <see cref="Default"/>.</summary>
    public static int Normalize(int step) => Steps.Contains(step) ? step : Default;
}
