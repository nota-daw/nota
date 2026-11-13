// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Custom keyboard shortcuts (specs/096, #61): the key-action catalog, the conflict rule,
// overrides in settings (only changed slots), reset, and what happens to a user key when a
// later version ships a default on the same chord.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Nota.Application;
using Nota.Application.Input;
using Nota.Application.Palette;

namespace Nota.SmokeTest;

internal static class KeyBindingTests
{
    // The default pairs that share a chord on purpose (research R3): each still works where
    // it always did. A new pair fails this test until it is added here, deliberately.
    private static readonly HashSet<string> DefaultPairs = new(StringComparer.Ordinal)
    {
        "browser.editPath|transport.loop",   // ⌘L: the Files browser's path field while it has focus
    };

    private const ShortcutModifiers P = ShortcutModifiers.Primary, S = ShortcutModifiers.Shift, A = ShortcutModifiers.Alt;

    public static IEnumerable<(bool, string)> Run()
    {
        foreach (bool mac in new[] { true, false })
        {
            var catalog = KeyActionCatalog.Create(mac);
            string os = mac ? "macOS" : "Windows/Linux";
            var dup = catalog.GroupBy(a => a.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            yield return (dup.Count == 0, dup.Count == 0 ? $"{os}: {catalog.Count} key actions, ids unique" : "duplicate key-action ids: " + string.Join(", ", dup));
            var missing = CommandCatalog.Create().Select(c => c.Id).Where(id => catalog.All(a => a.Id != id)).ToList();
            yield return (missing.Count == 0, missing.Count == 0 ? $"{os}: every palette command is a key action" : "commands missing: " + string.Join(", ", missing));
            var badTokens = catalog.SelectMany(a => new[] { a.DefaultPrimary, a.DefaultAlternative }.Where(k => k is not null)
                .Where(k => !KeyActionCatalog.IsKnownToken(k!.Value.Key)).Select(k => $"{a.Id}={k}")).ToList();
            yield return (badTokens.Count == 0, badTokens.Count == 0 ? $"{os}: every default key uses a known token" : "unknown tokens: " + string.Join(", ", badTokens));

            var kb = New(mac, new Settings());
            var collisions = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var a in catalog)
                foreach (var slot in new[] { KeySlot.Primary, KeySlot.Alternative })
                    if (a.Default(slot) is { } d)
                        foreach (var u in kb.FindConflicts(a.Id, slot, d, exemptDefaults: false))
                            collisions.Add(string.CompareOrdinal(a.Id, u.Action.Id) < 0 ? $"{a.Id}|{u.Action.Id}" : $"{u.Action.Id}|{a.Id}");
            var unexpected = collisions.Where(c => !DefaultPairs.Contains(c)).ToList();
            yield return (unexpected.Count == 0, unexpected.Count == 0 ? $"{os}: the shipped defaults collide only where intended ({string.Join(", ", collisions)})"
                : $"{os}: default keys that collide: {string.Join(", ", unexpected)}");
            var reserved = catalog.Where(a => !a.Fixed && a.DefaultPrimary is { } d && ReservedKeys.Reason(d, mac ? KeyPlatform.Mac : KeyPlatform.Windows) is not null)
                .Select(a => a.Id).ToList();
            yield return (reserved.Count == 0, reserved.Count == 0 ? $"{os}: no editable default sits on a system-reserved key" : "reserved defaults: " + string.Join(", ", reserved));
        }

        // ---- overrides round-trip, only changed slots stored -------------------------------
        var settings = new Settings();
        int saves = 0;
        var b = New(true, settings, () => saves++);
        var cmdT = new Shortcut(P, "T");
        b.Set("edit.split", KeySlot.Primary, cmdT);
        yield return (b.Matches("edit.split", cmdT) && !b.Matches("edit.split", new Shortcut(P, "E")) && b.IsEdited("edit.split") && saves == 1,
            "rebinding Split to ⌘T: ⌘T splits, ⌘E doesn't, the row is edited, saved once");
        yield return (settings.KeyBindings.Count == 1 && settings.KeyBindings["edit.split"].Primary == "⌘T" && settings.KeyBindings["edit.split"].Alternative is null,
            "only the changed slot is stored (Primary \"⌘T\", Alternative left to the default)");
        var reread = New(true, settings);
        yield return (reread.Matches("edit.split", cmdT), "the custom key is read back from settings");
        b.Set("edit.split", KeySlot.Primary, new Shortcut(P, "E"));
        yield return (settings.KeyBindings.Count == 0 && !b.IsEdited("edit.split"), "setting the default again drops the entry and the edited mark");
        b.Set("transport.metronome", KeySlot.Primary, null);
        yield return (b.Current("transport.metronome") is null && settings.KeyBindings["transport.metronome"].Primary == "" && b.IsEdited("transport.metronome"),
            "Clear leaves the action unassigned (\"\") and edited");

        // ---- unreadable settings ----------------------------------------------------------
        var junk = new Settings();
        junk.KeyBindings["nope.gone"] = new KeyBindingOverride { Primary = "⌘K" };
        junk.KeyBindings["edit.split"] = new KeyBindingOverride { Primary = "⌘@@" };
        var warnings = new List<string>();
        KeyBindings? jb = null;
        try { jb = new KeyBindings(KeyActionCatalog.Create(true), junk, platform: KeyPlatform.Mac, warn: warnings.Add); } catch { }
        yield return (jb is not null && jb.Matches("edit.split", new Shortcut(P, "E")) && warnings.Count == 2,
            $"an unknown action and an unreadable key are skipped with a warning each ({warnings.Count}); Split keeps ⌘E");

        // ---- damaged entries don't stop the app (review on nota#597) -----------------------
        var nulls = new Settings();
        nulls.KeyBindings["edit.split"] = null!;
        KeyBindings? nb = null;
        try { nb = new KeyBindings(KeyActionCatalog.Create(true), nulls, platform: KeyPlatform.Mac); } catch { }
        var noDict = new Settings { KeyBindings = null! };
        KeyBindings? db = null;
        try { db = new KeyBindings(KeyActionCatalog.Create(true), noDict, platform: KeyPlatform.Mac); } catch { }
        yield return (nb is not null && nb.Matches("edit.split", new Shortcut(P, "E")) && db is not null && noDict.KeyBindings is not null,
            "a null entry and a null KeyBindings in settings load as defaults, no exception");

        // ---- reserved and navigation keys -------------------------------------------------
        yield return (b.Check("edit.split", new Shortcut(P, "Q")) == KeyCheck.Reserved, "⌘Q is refused on macOS");
        var win = New(false, new Settings());
        yield return (win.Check("edit.split", new Shortcut(A, "F4")) == KeyCheck.Reserved, "Alt+F4 is refused on Windows");
        yield return (b.Check("session.rename", Shortcut.None("↑")) == KeyCheck.Navigation, "↑ is fixed for Session navigation");
        yield return (b.Check("edit.split", new Shortcut(P, "K")) == KeyCheck.Ok, "⌘K is free to use");
        yield return (b.Check("session.rename", new Shortcut(A, "↑")) == KeyCheck.Navigation
                      && b.Check("session.rename", new Shortcut(S, "←")) == KeyCheck.Navigation
                      && b.Check("session.rename", new Shortcut(P, "↑")) == KeyCheck.Ok,
            "⌥↑ and ⇧← are the Session grid's navigation too; ⌘↑ is free there");

        // ---- conflicts --------------------------------------------------------------------
        var cmdD = new Shortcut(P, "D");
        var c1 = b.FindConflicts("roll.transposeUp", KeySlot.Primary, cmdD);
        yield return (c1.Any(u => u.Action.Id == "edit.duplicate"), "⌘D for Transpose up conflicts with Duplicate (a menu key reaches every scope)");
        var shiftF = new Shortcut(S, "F");
        var c2 = b.FindConflicts("arr.nudgeLeft", KeySlot.Primary, shiftF);
        var s2 = b.FindShadows("arr.nudgeLeft", shiftF);
        yield return (c2.Count == 0 && s2.Any(u => u.Action.Id == "roll.fitNotes"),
            "⇧F for Nudge left is no conflict — the piano roll's Fit to notes is noted, it wins while it has focus");
        var c3 = b.FindConflicts("edit.split", KeySlot.Primary, new Shortcut(P, "S"));
        yield return (c3.Any(u => u.Action.Id == "file.save"), "⌘S for Split conflicts with Save (both window-wide)");
        var c4 = b.FindConflicts("notes.c", KeySlot.Primary, Shortcut.None("P"));
        yield return (c4.Any(u => u.Action.Id == "view.showPlayhead"), "P for a note conflicts with Show playhead (both window-wide)");
        var c5 = b.FindConflicts("edit.delete", KeySlot.Alternative, Shortcut.None("⌫"));
        yield return (c5.Any(u => u.Action.Id == "edit.delete" && u.Slot == KeySlot.Primary), "the same key in both slots of one action is flagged");

        saves = 0;
        b.Set("roll.transposeUp", KeySlot.Primary, cmdD, c1);
        yield return (b.Matches("roll.transposeUp", cmdD) && b.Current("edit.duplicate") is null && b.IsEdited("edit.duplicate") && saves == 1,
            "Replace moves ⌘D to Transpose up and leaves Duplicate unassigned — one change, saved once");
        var rc = b.ResetConflicts("edit.duplicate");
        yield return (rc.Any(u => u.Action.Id == "roll.transposeUp"), "resetting Duplicate first reports that ⌘D is now Transpose up");
        b.Reset("edit.duplicate", rc);
        yield return (b.Matches("edit.duplicate", cmdD) && !b.Matches("roll.transposeUp", cmdD), "Reset with Replace gives ⌘D back to Duplicate");
        b.ResetAll();
        yield return (!b.AnyEdited && settings.KeyBindings.Count == 0, "Reset all leaves nothing edited");

        // ---- a new default on a user's key -------------------------------------------------
        var upd = new Settings();
        upd.KeyBindings["edit.split"] = new KeyBindingOverride { Primary = "⌘S" };
        var ub = New(true, upd);
        yield return (ub.Matches("edit.split", new Shortcut(P, "S")) && ub.Current("file.save") is null && ub.NeedsDecision.Contains("file.save"),
            "a user key on a chord a default also uses: the user's key wins, the default rests under \"Needs a decision\"");

        var alt = new Settings();
        alt.KeyBindings["edit.split"] = new KeyBindingOverride { Primary = "⌦" };
        var ab = New(true, alt);
        yield return (ab.NeedsDecision.Contains("edit.delete") && ab.NeedsDecisionSlot("edit.delete") == KeySlot.Alternative
                      && ab.Matches("edit.delete", Shortcut.None("⌫")),
            "a user key on Delete's alternative ⌦: the decision is about the alternative, ⌫ keeps working");

        // ---- notes ------------------------------------------------------------------------
        yield return (b.Matches("notes.c", Shortcut.None("A")) && b.Matches("notes.c2", Shortcut.None("K")) && b.Matches("notes.octaveUp", Shortcut.None("X")),
            "the note map: A plays C, K the C above, X raises the octave");
    }

    /// <summary>The Shortcuts page's layout with the shipped (macOS) defaults, as JSON.</summary>
    public static string Dump()
    {
        var actions = KeyActionCatalog.Create(mac: true);
        var byId = actions.ToDictionary(a => a.Id);
        var groups = ShortcutLayout.Build(actions, menuKey: true).Select(g => new
        {
            title = g.Title,
            rows = g.Rows.Select(r => r.ActionId is { } id
                ? new { keys = string.Join("   ", new[] { byId[id].DefaultPrimary, byId[id].DefaultAlternative }.Where(k => k is not null).Select(k => k!.Value.ToGlyphs())),
                        text = byId[id].Title, id = (string?)id, editable = !byId[id].Fixed }
                : new { keys = r.Key, text = r.Text, id = (string?)null, editable = false }).ToList(),
        });
        return System.Text.Json.JsonSerializer.Serialize(new { groups }, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }

    private static KeyBindings New(bool mac, Settings s, Action? save = null)
    {
        var kb = new KeyBindings(KeyActionCatalog.Create(mac), s, save, mac ? KeyPlatform.Mac : KeyPlatform.Windows);
        kb.SetMenuCommands(MenuIds());
        return kb;
    }

    private static List<string>? _menuIds;

    // The commands MainWindow.axaml puts in the menu.
    private static List<string> MenuIds()
    {
        if (_menuIds is not null) return _menuIds;
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Nota.sln"))) dir = Path.GetDirectoryName(dir);
        var axaml = dir is null ? "" : File.ReadAllText(Path.Combine(dir, "src/managed/Nota.App/MainWindow.axaml"));
        return _menuIds = Regex.Matches(axaml, "app:Commands\\.Id=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();
    }
}
