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

    /// <summary>The offered steps, smallest first, with their names (the picker's order).</summary>
    public static IReadOnlyList<(int Step, string Name)> Steps { get; } =
        new[] { (-1, "Smaller"), (0, "Default"), (1, "Larger"), (2, "Largest") };

    /// <summary>The stored value if it is offered, else <see cref="Default"/>.</summary>
    public static int Normalize(int step) => Steps.Any(s => s.Step == step) ? step : Default;
}
