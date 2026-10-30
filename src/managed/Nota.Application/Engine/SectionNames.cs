// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Automatic section names (specs/055). A section whose stored name is empty is automatic: it
// reads "Section N", N its position among all sections in start order, custom-named ones
// counted. The mode lives in the name itself, so the engine's undo snapshot and project.json
// carry it with no extra field, and renumbering never rewrites stored data.

using System.Globalization;

namespace Nota.Application;

public static class SectionNames
{
    public static bool IsAutomatic(string? name) => string.IsNullOrWhiteSpace(name);

    /// <summary>The automatic name for the section at <paramref name="index"/> (0-based, start order).</summary>
    public static string Automatic(int index) => "Section " + (index + 1).ToString(CultureInfo.InvariantCulture);

    public static string Display(string? name, int index) => IsAutomatic(name) ? Automatic(index) : name!;

    /// <summary>What a typed name stores: trimmed, and "" (automatic) when nothing is left.</summary>
    public static string Normalize(string? typed) => typed?.Trim() ?? "";

    /// <summary>Displayed names of sections already in start order.</summary>
    public static IReadOnlyList<string> Displayed(IReadOnlyList<SectionInfo> sections)
        => sections.Select((s, i) => Display(s.Name, i)).ToList();
}
