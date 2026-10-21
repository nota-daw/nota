// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The instruments the user added most recently (spec 034), offered under "Add track ▸
// Recent". Kept in the app settings, per user, not in the project. Every route that puts an
// instrument on a track records into it — the browser, the menus, the MCP tools — so the
// rules live here, once, and the smoke test checks them.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Nota.Application;

/// <summary>One recently added instrument. <see cref="Kind"/> is "builtin" (Id = the built-in
/// instrument kind) or "plugin" (Id = the plug-in catalog's stable identifier); identity is
/// (Kind, Id, FactoryPreset).</summary>
public sealed record RecentInstrument(string Kind, string Id, string Name, string? FactoryPreset = null)
{
    public const string BuiltinKind = "builtin", PluginKind = "plugin";

    public bool IsPlugin => Kind == PluginKind;

    /// <summary>The built-in instrument kind, or -1 for a plug-in or a malformed entry.</summary>
    public int BuiltinKindId => Kind == BuiltinKind && int.TryParse(Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out int k) ? k : -1;

    public static RecentInstrument Builtin(int kind, string name)
        => new(BuiltinKind, kind.ToString(CultureInfo.InvariantCulture), name);

    public static RecentInstrument Plugin(string id, string name) => new(PluginKind, id, name);

    public bool SameAs(RecentInstrument other)
        => Kind == other.Kind && Id == other.Id && FactoryPreset == other.FactoryPreset;
}

public static class RecentInstruments
{
    public const int Capacity = 5;

    /// <summary>Puts <paramref name="entry"/> at the top: an entry already present moves up
    /// (no duplicates), and the oldest falls off past <see cref="Capacity"/>.</summary>
    public static void Record(List<RecentInstrument> list, RecentInstrument entry)
    {
        if (string.IsNullOrEmpty(entry.Kind) || string.IsNullOrEmpty(entry.Id)) return;
        list.RemoveAll(e => e is null || e.SameAs(entry));
        list.Insert(0, entry);
        if (list.Count > Capacity) list.RemoveRange(Capacity, list.Count - Capacity);
    }

    /// <summary>The entries to offer, most recent first: well-formed, de-duplicated, at most
    /// <see cref="Capacity"/>, and only those <paramref name="available"/> accepts — a plug-in
    /// that is no longer installed is hidden, not forgotten, so it comes back if reinstalled.</summary>
    public static IReadOnlyList<RecentInstrument> Visible(IEnumerable<RecentInstrument?>? list, Func<RecentInstrument, bool> available)
    {
        var shown = new List<RecentInstrument>();
        if (list is null) return shown;
        foreach (var e in list)
        {
            if (e is null || string.IsNullOrEmpty(e.Kind) || string.IsNullOrEmpty(e.Id) || string.IsNullOrEmpty(e.Name)) continue;
            if (e.Kind is not (RecentInstrument.BuiltinKind or RecentInstrument.PluginKind)) continue;
            if (shown.Any(s => s.SameAs(e)) || !available(e)) continue;
            shown.Add(e);
            if (shown.Count == Capacity) break;
        }
        return shown;
    }

    /// <summary>Records into the app settings and saves them.</summary>
    public static void RecordAndSave(ISettingsService settings, RecentInstrument entry)
    {
        settings.Current.RecentInstruments ??= new();
        Record(settings.Current.RecentInstruments, entry);
        settings.Save();
    }
}
