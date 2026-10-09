// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Command palette (CP-15/CP-19): frecency — how often and how recently an item was applied
// from the palette or the browser — and the Recent list. Kept in Settings (settings.json), so
// it survives restarts and follows the user, not the project.

namespace Nota.Application.Palette;

/// <summary>One item's use record, persisted in <see cref="Settings.PaletteUsage"/>.</summary>
public sealed class PaletteUse
{
    public int Count { get; set; }
    public DateTime LastUtc { get; set; }
}

public sealed class PaletteUsage
{
    private const int MaxEntries = 400, MaxRecent = 5;
    private static readonly TimeSpan HalfLife = TimeSpan.FromDays(7);

    private readonly Settings _settings;
    private readonly Func<DateTime> _now;

    public PaletteUsage(Settings settings, Func<DateTime>? now = null)
    {
        _settings = settings;
        _now = now ?? (() => DateTime.UtcNow);
    }

    /// <summary>Items applied from the palette, most recent first (CP-19: the last five).</summary>
    public IReadOnlyList<string> Recent => _settings.PaletteRecent;

    /// <summary>Records a use. <paramref name="fromPalette"/> also puts it at the top of Recent.</summary>
    public void Record(string id, bool fromPalette)
    {
        if (string.IsNullOrEmpty(id)) return;
        var map = _settings.PaletteUsage;
        if (!map.TryGetValue(id, out var u)) map[id] = u = new PaletteUse();
        u.Count++;
        u.LastUtc = _now();
        if (map.Count > MaxEntries)
        {
            // Forget the stalest quarter rather than growing settings.json without bound.
            foreach (var old in map.OrderBy(kv => Score(kv.Value)).Take(MaxEntries / 4).Select(kv => kv.Key).ToList())
                map.Remove(old);
        }
        if (fromPalette)
        {
            var r = _settings.PaletteRecent;
            r.Remove(id);
            r.Insert(0, id);
            if (r.Count > MaxRecent) r.RemoveRange(MaxRecent, r.Count - MaxRecent);
        }
    }

    /// <summary>0..1: saturates around 15 uses, halves every week unused.</summary>
    public float Frecency(string id)
        => _settings.PaletteUsage.TryGetValue(id, out var u) ? Score(u) : 0f;

    private float Score(PaletteUse u)
    {
        double days = Math.Max(0, (_now() - u.LastUtc).TotalDays);
        double decay = Math.Pow(0.5, days / HalfLife.TotalDays);
        double freq = Math.Min(1.0, Math.Log2(1 + u.Count) / 4.0);
        return (float)(freq * decay);
    }
}
