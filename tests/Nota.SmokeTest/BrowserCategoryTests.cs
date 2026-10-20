// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The browser's category chips (spec 013), driven through BrowserViewModel over the factory
// catalogue and fake plug-ins: with no chip the list is exactly today's; a chip narrows a
// tab to one category and stacks with search and favourites; picks are per tab; plug-ins
// join by the category they report, with an Other chip only when something lands there.
// Run alone with `--browser`.

using System.Text.Json;
using Nota.Infrastructure;
using Nota.Presentation;

namespace Nota.SmokeTest;

internal static class BrowserCategoryTests
{
    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        var lib = new FakeBrowserLibrary();
        var vm = new BrowserViewModel(new EmptyPluginCatalog(), new EmptyPresetLibrary(), new FactoryPresetCatalog(), library: lib);
        var tabs = new (int Tab, BuiltinDeviceType Type, IList<BrowserItem> Rows)[]
        {
            (0, BuiltinDeviceType.Instrument, vm.Instruments), (1, BuiltinDeviceType.AudioEffect, vm.Effects), (2, BuiltinDeviceType.MidiEffect, vm.MidiEffects),
        };
        static List<string> Devices(IList<BrowserItem> rows) => rows.Where(r => !r.IsGroup && r.Depth == 0).Select(r => r.Name).ToList();

        foreach (var (tab, type, rows) in tabs)
        {
            // No chip: today's list, row for row (SC-003).
            var today = new[] { "BUILT-IN" }.Concat(BuiltinDeviceCatalog.All.Where(d => d.Type == type).Select(d => d.Name).OrderBy(x => x)).ToList();
            yield return (rows.Select(r => r.Name).SequenceEqual(today), $"{type}: with no chip the list is today's list");
            yield return (vm.CategoriesOn(tab).SequenceEqual(DeviceCategories.Order(type)),
                $"{type}: chips in order ({string.Join(" · ", vm.CategoriesOn(tab))})");

            // Each chip lists exactly its category; together they cover every device once.
            var covered = new List<string>();
            var wrong = new List<string>();
            foreach (var c in DeviceCategories.Of(type))
            {
                vm.SetCategoryFilter(tab, c.Name);
                var got = Devices(rows);
                if (!got.OrderBy(x => x).SequenceEqual(c.Builtins.OrderBy(x => x))) wrong.Add($"{c.Name}: {string.Join(", ", got)}");
                covered.AddRange(got);
            }
            vm.SetCategoryFilter(tab, "");
            yield return (wrong.Count == 0, wrong.Count == 0 ? $"{type}: each chip lists exactly its category" : $"{type}: {string.Join(" | ", wrong)}");
            yield return (covered.Count == today.Count - 1 && covered.ToHashSet().Count == covered.Count,
                $"{type}: the chips cover every device once ({covered.Count}/{today.Count - 1})");
            yield return (rows.Select(r => r.Name).SequenceEqual(today), $"{type}: clearing the chip restores the whole list");
        }

        // Picks are per tab, and a pick keeps "Group by source"'s header.
        vm.SetCategoryFilter(1, "Reverb & delay");
        yield return (vm.CategoryFilter(1) == "Reverb & delay" && vm.CategoryFilter(0) == "" && Devices(vm.Instruments).Count == 16,
            "a pick narrows only its own tab");
        yield return (vm.Effects.Select(r => r.Name).SequenceEqual(new[] { "BUILT-IN", "Nota Chamber", "Nota Delay", "Nota Reverb" }),
            $"Reverb & delay lists Chamber, Delay, Reverb ({string.Join(", ", vm.Effects.Select(r => r.Name))})");
        yield return (vm.VisibleDeviceCounts(1) == (3, 0), "the status line counts the narrowed list");

        // The chip stacks with search and with the Favorites chip (FR-006).
        vm.SetCategoryFilter(1, "Dynamics");
        vm.FilterTree(1, "Prism");
        yield return (Devices(vm.Effects).SequenceEqual(new[] { "Nota Prism" }), "a chip and a search narrow together");
        vm.FilterTree(1, "Delay");
        yield return (Devices(vm.Effects).Count == 0, "a search with no match inside the chip lists nothing");
        vm.FilterTree(1, "");
        var compressor = vm.Effects.First(r => r.Name == "Nota Compressor");
        lib.SetFavorite(compressor.LibraryKey, true);
        vm.SetLibraryFilter(BrowserFilter.Favorites);
        yield return (Devices(vm.Effects).SequenceEqual(new[] { "Nota Compressor" }), "a chip and the Favorites chip narrow together");
        vm.SetLibraryFilter(BrowserFilter.None);
        lib.SetFavorite(compressor.LibraryKey, false);

        // Expanding a device while a chip is picked shows its presets beneath it (FR-007).
        vm.SetCategoryFilter(1, "");
        vm.SetCategoryFilter(0, "Synths");
        var synth = vm.Instruments.First(r => r.Name == "Nota Volt");
        vm.ToggleExpand(synth);
        int si = vm.Instruments.IndexOf(synth);
        yield return (si >= 0 && si + 1 < vm.Instruments.Count && vm.Instruments[si + 1].Depth > 0, "expanding a device under a chip lists its presets");
        vm.ToggleExpand(synth);
        vm.SetCategoryFilter(0, "");

        // Plug-ins join the categories from the category they report (FR-005).
        var mapping = new (bool Inst, string Reported, string Expected)[]
        {
            (false, "Fx/Reverb", "Reverb & delay"), (false, "Fx|Dynamics|EQ", "Dynamics"), (false, "Fx/Pitch Shift", "Pitch & time"),
            (false, "Fx/Analyzer", "Utility"), (true, "Instrument/Synth/Sampler", "Synths"), (true, "Instrument/Drum", "Drums"),
            (false, "Effect", DeviceCategories.Other), (false, "", DeviceCategories.Other), (false, "Fx", DeviceCategories.Other),
        };
        var mapped = mapping.Where(m => DeviceCategories.ForPlugin(m.Inst, m.Reported) != m.Expected)
            .Select(m => $"{m.Reported} → {DeviceCategories.ForPlugin(m.Inst, m.Reported)}").ToList();
        yield return (mapped.Count == 0, mapped.Count == 0 ? "plug-in categories map by their first recognised token" : $"plug-in mapping: {string.Join(", ", mapped)}");

        var plugins = new FakePluginCatalog(
            "Ambience | VST3 | fx | Smart | Fx/Reverb",
            "Mystery | AU | fx | Acme | Effect",
            "Ana | VST3 | inst | Sonic | Instrument/Synth");
        var pvm = new BrowserViewModel(plugins, new EmptyPresetLibrary(), new FactoryPresetCatalog());
        pvm.SetCategoryFilter(1, "Reverb & delay");
        var reverbs = pvm.Effects.Select(r => r.Name).ToList();
        yield return (reverbs.SequenceEqual(new[] { "BUILT-IN", "Nota Chamber", "Nota Delay", "Nota Reverb", "PLUG-INS", "Ambience" }),
            $"a plug-in joins its category, after the built-ins ({string.Join(", ", reverbs)})");
        yield return (pvm.CategoriesOn(1).Last() == DeviceCategories.Other, "an unrecognised plug-in adds an Other chip, last");
        pvm.SetCategoryFilter(1, DeviceCategories.Other);
        yield return (Devices(pvm.Effects).SequenceEqual(new[] { "Mystery" }), "the Other chip lists it");
        pvm.SetCategoryFilter(0, "Synths");
        yield return (Devices(pvm.Instruments).Last() == "Ana", "a synth plug-in is under Synths");
        yield return (!pvm.CategoriesOn(0).Contains(DeviceCategories.Other) && !pvm.CategoriesOn(2).Contains(DeviceCategories.Other),
            "no Other chip where nothing lands there");

        // A rescan that removes the picked category's last device clears the pick with its chip.
        pvm.SetCategoryFilter(1, DeviceCategories.Other);
        plugins.Set("Ambience | VST3 | fx | Smart | Fx/Reverb", "Ana | VST3 | inst | Sonic | Instrument/Synth");
        pvm.Rebuild();
        yield return (pvm.CategoryFilter(1) == "" && !pvm.CategoriesOn(1).Contains(DeviceCategories.Other) && Devices(pvm.Effects).Count == 28
                      && pvm.CategoryFilter(0) == "Synths",
            "a rescan that empties the picked category clears the pick; other picks stay");

        // An existing settings file has no key: the chips are on (FR-003).
        var old = JsonSerializer.Deserialize<Settings>("{}")!;
        yield return (old.BrowserCategoryChips, "settings without the new key show the category chips");
    }
}
