// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System.Text.Json;
using Nota.Application.Editing;

namespace Nota.SmokeTest;

internal static class MidiLaneHeightTests
{
    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        yield return (JsonSerializer.Deserialize<Settings>("{}")!.PianoRollLaneHeights.Count == 0,
            "lane heights: older preferences keep the original defaults");
        foreach (double value in new[] { -10.0, 0, 48, 72, 112, 480, 99999, double.NaN, double.PositiveInfinity })
            yield return (MidiLaneHeight.Normalize(value, 112) is >= 48 and <= 480,
                $"lane heights: invalid and boundary size {value} remains usable");
        var saved = new Settings { PianoRollLaneHeights = new()
            { ["Velocity"] = 122, ["extra:0:cc:1"] = 157, ["extra:1:cc:11"] = 197 } };
        var loaded = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(saved))!;
        yield return (loaded.PianoRollLaneHeights.Count == 3 && saved.PianoRollLaneHeights.All(
            pair => loaded.PianoRollLaneHeights.GetValueOrDefault(pair.Key) == pair.Value),
            "lane heights: independently sized velocity and CC lanes survive settings reload");
    }
}
