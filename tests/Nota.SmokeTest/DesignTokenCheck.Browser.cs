// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The browser's device categories (spec 013), checked as data: every built-in device sits
// on exactly one shelf of its own tab, every shelf names real devices, and the names read
// in sentence case. A device registered in BuiltinDeviceCatalog without a category fails
// here instead of drifting silently into "Other" (FR-009).


namespace Nota.SmokeTest;

internal static partial class DesignTokenCheck
{
    public static IEnumerable<(bool Ok, string Label)> RunBrowser()
    {
        var types = new[] { BuiltinDeviceType.Instrument, BuiltinDeviceType.AudioEffect, BuiltinDeviceType.MidiEffect };
        var expected = new Dictionary<BuiltinDeviceType, int>
        {
            [BuiltinDeviceType.Instrument] = 16, [BuiltinDeviceType.AudioEffect] = 27, [BuiltinDeviceType.MidiEffect] = 6,
        };
        foreach (var type in types)
        {
            var cats = DeviceCategories.Of(type);
            var devices = BuiltinDeviceCatalog.All.Where(d => d.Type == type).ToList();

            // Every built-in device on exactly one shelf.
            var unshelved = new List<string>();
            var twice = new List<string>();
            foreach (var d in devices)
            {
                int n = cats.Count(c => c.Builtins.Contains(d.Name));
                if (n == 0) unshelved.Add(d.Name);
                else if (n > 1) twice.Add(d.Name);
            }
            yield return (unshelved.Count == 0,
                unshelved.Count == 0 ? $"{type}: every built-in device has a category"
                    : $"{type}: no category for {string.Join(", ", unshelved)} — add it in DeviceCategories.cs");
            yield return (twice.Count == 0,
                twice.Count == 0 ? $"{type}: no device in two categories" : $"{type}: in two categories: {string.Join(", ", twice)}");

            // Every listed name is a real device of this tab (a renamed or removed device fails).
            var names = devices.Select(d => d.Name).ToHashSet();
            var stale = cats.SelectMany(c => c.Builtins.Where(b => !names.Contains(b)).Select(b => $"{c.Name}/{b}")).ToList();
            yield return (stale.Count == 0,
                stale.Count == 0 ? $"{type}: no stale category entries" : $"{type}: not in the catalogue: {string.Join(", ", stale)}");

            var empty = cats.Where(c => c.Builtins.Length == 0).Select(c => c.Name).ToList();
            yield return (empty.Count == 0,
                empty.Count == 0 ? $"{type}: no empty category" : $"{type}: empty categories: {string.Join(", ", empty)}");

            var dupNames = cats.GroupBy(c => c.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            var shouting = cats.Select(c => c.Name).Where(n => !SentenceCase(n) || n == DeviceCategories.Other).ToList();
            yield return (dupNames.Count == 0 && shouting.Count == 0,
                dupNames.Count == 0 && shouting.Count == 0 ? $"{type}: category names unique, sentence case"
                    : $"{type}: category names: {string.Join(", ", dupNames.Concat(shouting))}");

            int shelved = cats.Sum(c => c.Builtins.Length);
            yield return (devices.Count == expected[type] && shelved == expected[type],
                $"{type}: {shelved} shelved of {devices.Count} built-in devices (expected {expected[type]})");
        }
    }

    // "EQ & filter", "Keys & physical": a capital first word, the rest lowercase, except a
    // known acronym (the same short list the label rule keeps upper-case).
    private static bool SentenceCase(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0 || !char.IsUpper(words[0][0])) return false;
        for (int i = 0; i < words.Length; i++)
        {
            var w = words[i];
            if (w is "EQ" or "MIDI" or "LFO" or "&") continue;
            var rest = i == 0 ? w[1..] : w;
            if (rest.Any(char.IsUpper)) return false;
        }
        return true;
    }
}
