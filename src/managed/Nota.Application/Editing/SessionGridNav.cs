// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Walking the Session grid by meaning, not by screen direction (specs/108). The grid can run
// either way — Scenes down (a scene per row) or Scenes across (a scene per column) — so an
// arrow key is turned into a step by what is on screen, while the Session selection commands
// (and the controllers that run them) always step by scene or by track. Kept pure so the
// smoke test checks the rules without a view.

namespace Nota.Application.Editing;

/// <summary>A selection move on the Session grid, by what it means.</summary>
public enum SessionStep { PrevScene, NextScene, PrevTrack, NextTrack }

/// <summary>An arrow key as pressed.</summary>
public enum GridArrow { Up, Down, Left, Right }

/// <summary>What is selected: a slot on a track (or group), a scene, or a Return / Master (no slots).</summary>
public enum GridSelKind { Slot, Scene, Bus }

/// <summary>A selection by position: <paramref name="Index"/> is the track's index among the
/// launchable tracks (Slot) or the bus's index among Returns + Master (Bus); unused for Scene.</summary>
public readonly record struct GridPos(GridSelKind Kind, int Index, int Scene);

public static class SessionGridNav
{
    /// <summary>The <c>Settings.SessionGrid</c> value that turns the grid; anything else is Scenes down.</summary>
    public const string Across = "across";
    public const string Down = "down";

    public static bool IsAcross(string? setting) => setting == Across;

    /// <summary>The step an arrow key makes on the grid as drawn: Scenes down puts scenes on ↑/↓
    /// and tracks on ←/→; Scenes across the other way round.</summary>
    public static SessionStep FromArrow(GridArrow arrow, bool across) => (arrow, across) switch
    {
        (GridArrow.Up, false) => SessionStep.PrevScene,
        (GridArrow.Down, false) => SessionStep.NextScene,
        (GridArrow.Left, false) => SessionStep.PrevTrack,
        (GridArrow.Right, false) => SessionStep.NextTrack,
        (GridArrow.Left, true) => SessionStep.PrevScene,
        (GridArrow.Right, true) => SessionStep.NextScene,
        (GridArrow.Up, true) => SessionStep.PrevTrack,
        _ => SessionStep.NextTrack,
    };

    /// <summary>Where a step lands. Rules (unchanged from the arrow keys before specs/108, now by
    /// meaning): before the first track is the scene itself; after the last track come the
    /// Returns and the Master, which have no slots, so scene steps do nothing there. Returns
    /// <paramref name="at"/> itself when there is nowhere to go.</summary>
    public static GridPos Step(GridPos at, SessionStep step, int tracks, int buses, int scenes)
    {
        int s = at.Scene, last = System.Math.Max(0, scenes - 1);
        bool sceneStep = step is SessionStep.PrevScene or SessionStep.NextScene;
        int Scene(SessionStep st) => st == SessionStep.PrevScene ? System.Math.Max(0, s - 1) : System.Math.Min(last, s + 1);

        switch (at.Kind)
        {
            case GridSelKind.Scene:
                if (sceneStep) return at with { Scene = Scene(step) };
                if (step == SessionStep.NextTrack && tracks > 0) return new GridPos(GridSelKind.Slot, 0, s);
                return at;

            case GridSelKind.Bus:
                if (sceneStep) return at;
                int b = at.Index;
                if (step == SessionStep.PrevTrack)
                {
                    if (b > 0) return new GridPos(GridSelKind.Bus, b - 1, s);
                    return tracks > 0 ? new GridPos(GridSelKind.Slot, tracks - 1, s) : new GridPos(GridSelKind.Scene, 0, s);
                }
                return b >= 0 && b < buses - 1 ? new GridPos(GridSelKind.Bus, b + 1, s) : at;

            default:
                if (tracks <= 0) return at;
                int i = System.Math.Clamp(at.Index, 0, tracks - 1);
                if (sceneStep) return new GridPos(GridSelKind.Slot, i, Scene(step));
                if (step == SessionStep.PrevTrack)
                    return i == 0 ? new GridPos(GridSelKind.Scene, 0, s) : new GridPos(GridSelKind.Slot, i - 1, s);
                if (i == tracks - 1) return buses > 0 ? new GridPos(GridSelKind.Bus, 0, s) : at with { Index = i };
                return new GridPos(GridSelKind.Slot, i + 1, s);
        }
    }
}
