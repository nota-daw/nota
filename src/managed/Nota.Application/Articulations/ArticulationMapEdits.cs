// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Editing an articulation map (specs/085, US5) — the rules the manager window follows, kept here so
// the smoke test checks them: ids are never reused (notes point at them), removing the default
// clears it, actions keep their send order, and a map is checked before it is saved.

using System.Globalization;

namespace Nota.Application.Articulations;

public static class ArticulationMapEdits
{
    /// <summary>A new articulation at the end, with the next unused id; its id.</summary>
    public static int Add(ArticulationMap map, string name)
    {
        int id = NextId(map);
        map.Articulations.Add(new Articulation { Id = id, Name = name, ShortName = ShortOf(name) });
        return id;
    }

    /// <summary>A copy of articulation <paramref name="id"/> right after it, with a new id; its id, or 0.</summary>
    public static int Copy(ArticulationMap map, int id, string name)
    {
        int k = map.Articulations.FindIndex(a => a.Id == id);
        if (k < 0) return 0;
        var c = map.Articulations[k].Clone();
        c.Id = NextId(map);
        c.Name = name;
        c.ShortName = ShortOf(name);
        map.Articulations.Insert(k + 1, c);
        return c.Id;
    }

    /// <summary>Removes articulation <paramref name="id"/>. Notes that used it keep the id and play the
    /// default; the id is remembered in the map's metadata so it is never given to another
    /// articulation (see <see cref="NextId"/>). A removed default leaves the map without one.</summary>
    public static bool Remove(ArticulationMap map, int id)
    {
        int n = map.Articulations.RemoveAll(a => a.Id == id);
        if (n > 0 && map.DefaultArticulationId == id) map.DefaultArticulationId = null;
        if (n > 0) map.Metadata["nota.retiredIds"] = RetiredWith(map, id);
        return n > 0;
    }

    /// <summary>Moves articulation <paramref name="id"/> by <paramref name="delta"/> places (clamped).</summary>
    public static bool Move(ArticulationMap map, int id, int delta)
    {
        int k = map.Articulations.FindIndex(a => a.Id == id);
        if (k < 0) return false;
        int to = Math.Clamp(k + delta, 0, map.Articulations.Count - 1);
        if (to == k) return false;
        var a = map.Articulations[k];
        map.Articulations.RemoveAt(k);
        map.Articulations.Insert(to, a);
        return true;
    }

    public static bool MoveAction(Articulation art, int index, int delta)
    {
        if (index < 0 || index >= art.Actions.Count) return false;
        int to = Math.Clamp(index + delta, 0, art.Actions.Count - 1);
        if (to == index) return false;
        var x = art.Actions[index];
        art.Actions.RemoveAt(index);
        art.Actions.Insert(to, x);
        return true;
    }

    /// <summary>The next id for a new articulation: above every id the map has, and every id it has
    /// ever removed (kept in metadata) — so a note pointing at a removed id never comes back to life
    /// as a different articulation.</summary>
    public static int NextId(ArticulationMap map) => Math.Max(map.NextArticulationId(), Retired(map).DefaultIfEmpty(0).Max() + 1);

    private static IEnumerable<int> Retired(ArticulationMap map) =>
        map.Metadata.TryGetValue("nota.retiredIds", out var s)
            ? s.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => int.TryParse(x, NumberStyles.None, CultureInfo.InvariantCulture, out int v) ? v : 0).Where(v => v > 0)
            : Enumerable.Empty<int>();

    private static string RetiredWith(ArticulationMap map, int id) =>
        string.Join(",", Retired(map).Append(id).Distinct().OrderBy(v => v).Select(v => v.ToString(CultureInfo.InvariantCulture)));

    /// <summary>The name shortened for a lane label: the name itself up to 6 characters, else its first 6.</summary>
    public static string ShortOf(string name) => name.Length <= 6 ? name : name[..6];

    public enum Problem { NoName, ArticulationWithoutName, DuplicateId, InvalidAction, DefaultMissing }

    /// <summary>What would stop a map from being saved as it is. Empty = fine.</summary>
    public static List<(Problem Problem, string Subject)> Check(ArticulationMap map)
    {
        var p = new List<(Problem, string)>();
        if (string.IsNullOrWhiteSpace(map.Name)) p.Add((Problem.NoName, ""));
        var seen = new HashSet<int>();
        foreach (var a in map.Articulations)
        {
            if (string.IsNullOrWhiteSpace(a.Name)) p.Add((Problem.ArticulationWithoutName, "#" + a.Id.ToString(CultureInfo.InvariantCulture)));
            if (a.Id <= 0 || !seen.Add(a.Id)) p.Add((Problem.DuplicateId, a.Name));
            if (a.Actions.Any(x => !x.IsValid())) p.Add((Problem.InvalidAction, a.Name));
        }
        if (map.DefaultArticulationId is int d && map.Find(d) is null) p.Add((Problem.DefaultMissing, ""));
        return p;
    }

    /// <summary>A MIDI data value typed by the user: a number 0..127 or, for a keyswitch, a note name
    /// in Nota's naming (60 = C4; "C#1", "Db1", "c1" all read). Null when it isn't one.</summary>
    public static int? ParseMidiValue(string text, bool noteNames)
    {
        var t = text.Trim();
        if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) return n is >= 0 and <= 127 ? n : null;
        if (!noteNames || t.Length < 2) return null;
        int pc = char.ToUpperInvariant(t[0]) switch { 'C' => 0, 'D' => 2, 'E' => 4, 'F' => 5, 'G' => 7, 'A' => 9, 'B' => 11, _ => -1 };
        if (pc < 0) return null;
        int i = 1;
        if (i < t.Length && (t[i] == '#' || t[i] == '♯')) { pc++; i++; }
        else if (i < t.Length && (t[i] == 'b' || t[i] == '♭')) { pc--; i++; }
        var oct = t[i..].Replace('\u2212', '-');
        if (!int.TryParse(oct, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int octave)) return null;
        int midi = (octave + 1) * 12 + pc;
        return midi is >= 0 and <= 127 ? midi : null;
    }
}
