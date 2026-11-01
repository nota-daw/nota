// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Putting a note's velocity back to the default in the piano roll's velocity lane (specs/069):
// a double-click on a stem, or Reset velocity in its right-click menu. This is the one place
// that says what the default is and which notes a reset touches; the lane only handles the
// pointer and the undo timing. The default is fixed: it is not the "last touched" velocity a
// newly drawn note reuses, or the reset would do nothing right after an edit.

using System;
using System.Collections.Generic;

namespace Nota.Application.Editing;

public static class VelocityReset
{
    /// <summary>The velocity a freshly created note gets before any note was touched, and the
    /// one the playable keys use: 0.8, about 102 of 127.</summary>
    public const float Default = 0.8f;

    // Half a MIDI step: velocities closer than this are the same velocity to the user.
    private const float Tolerance = 0.5f / 127f;

    /// <summary>Whether a velocity already is the default, to within half a MIDI step.</summary>
    public static bool IsDefault(float velocity) => MathF.Abs(velocity - Default) < Tolerance;

    /// <summary>The notes a reset on the stem of note <paramref name="hit"/> changes: every
    /// selected note when the stem belongs to the selection, else that note alone. Locked notes
    /// (a reference clip's) and notes already at the default are left out; so is anything out of
    /// range. Empty means there is nothing to do, so no undo step and a disabled menu item.</summary>
    public static List<int> Targets(IReadOnlyCollection<int> selection, int hit,
                                    IReadOnlyList<float> velocities, Func<int, bool> isLocked)
    {
        var picked = new List<int>();
        if (hit < 0 || hit >= velocities.Count) return picked;
        IEnumerable<int> candidates = selection.Contains(hit) ? selection : new[] { hit };
        foreach (int i in candidates)
            if (i >= 0 && i < velocities.Count && !isLocked(i) && !IsDefault(velocities[i]) && !picked.Contains(i))
                picked.Add(i);
        picked.Sort();
        return picked;
    }
}
