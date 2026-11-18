// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Where a key action is live (specs/096). Window-layer scopes are handled by the main window
// in one fixed order, so they are live together; a focus-layer scope handles its keys first
// while it has the keyboard. The conflict rule in KeyBindings reads this split.

namespace Nota.Application.Input;

public enum KeyScope
{
    /// <summary>The main window (or the floating detail window) has the keyboard.</summary>
    Global,
    /// <summary>The Arrangement is shown (clip nudges, show playhead, clip on/off).</summary>
    Arrangement,
    /// <summary>The computer-keyboard notes — the last thing a key falls through to.</summary>
    Notes,
    /// <summary>The Session grid has focus.</summary>
    Session,
    /// <summary>The piano-roll grid or its controller lanes have focus.</summary>
    PianoRoll,
    /// <summary>The audio clip editor is shown.</summary>
    AudioEditor,
    /// <summary>The device chain or a rack card has focus.</summary>
    Devices,
    /// <summary>The browser's Files tab has focus.</summary>
    Browser,
    /// <summary>The Modular canvas has focus.</summary>
    Modular,
}

public static class KeyScopes
{
    /// <summary>True for the scopes the main window runs together (Global, Arrangement, Notes).</summary>
    public static bool IsWindowLayer(this KeyScope s) => s is KeyScope.Global or KeyScope.Arrangement or KeyScope.Notes;
}
