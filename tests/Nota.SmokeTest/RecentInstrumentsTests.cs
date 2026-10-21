// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// "Add track ▸ Recent" (spec 034): the recent-instruments rules — most recent first, no
// duplicates, five at most, uninstalled plug-ins hidden but kept, malformed entries skipped —
// and that the list survives a settings round trip through JSON.

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Nota.Application;

namespace Nota.SmokeTest;

internal static class RecentInstrumentsTests
{
    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        var volt = RecentInstrument.Builtin(6, "Nota Volt");
        var keys = RecentInstrument.Builtin(16, "Nota Keys");
        var serum = RecentInstrument.Plugin("vst3:serum", "Serum");

        var list = new List<RecentInstrument>();
        RecentInstruments.Record(list, volt);
        RecentInstruments.Record(list, keys);
        RecentInstruments.Record(list, serum);
        yield return (list.Select(e => e.Name).SequenceEqual(new[] { "Serum", "Nota Keys", "Nota Volt" }),
            "recent instruments: most recent first");

        RecentInstruments.Record(list, RecentInstrument.Builtin(6, "Nota Volt"));
        yield return (list.Count == 3 && list[0].SameAs(volt),
            "recent instruments: re-adding moves the entry to the top, no duplicate");

        for (int k = 7; k < 12; k++) RecentInstruments.Record(list, RecentInstrument.Builtin(k, $"Kind {k}"));
        yield return (list.Count == RecentInstruments.Capacity && list[0].BuiltinKindId == 11 && list.All(e => !e.SameAs(volt)),
            $"recent instruments: capped at {RecentInstruments.Capacity}, the oldest drops off");

        var stored = new List<RecentInstrument?> { serum, null, new("plugin", "", "Broken"), new("odd", "x", "Odd"), keys, serum };
        var shown = RecentInstruments.Visible(stored, e => !e.IsPlugin);
        yield return (shown.Count == 1 && shown[0].SameAs(keys) && stored.Contains(serum),
            "recent instruments: an uninstalled plug-in is hidden (kept in the list), malformed entries skipped");

        var settings = new Settings();
        RecentInstruments.Record(settings.RecentInstruments, serum);
        RecentInstruments.Record(settings.RecentInstruments, keys);
        var back = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings));
        yield return (back?.RecentInstruments.Count == 2 && back.RecentInstruments[0].SameAs(keys) && back.RecentInstruments[1].IsPlugin,
            "recent instruments: survive a settings JSON round trip");

        var old = JsonSerializer.Deserialize<Settings>("{\"RecentProjects\":[]}");
        yield return (old is not null && old.RecentInstruments.Count == 0,
            "recent instruments: an older settings file without the list reads as empty");
    }
}
