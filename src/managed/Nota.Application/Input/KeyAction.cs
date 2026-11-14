// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// One rebindable keyboard action (specs/096): what Preferences → Shortcuts lists and what a
// key handler asks about. Commands from CommandCatalog keep their ids; view-level actions
// (note map, nudges, transposes…) are declared in KeyActionCatalog beside them.

namespace Nota.Application.Input;

public sealed class KeyAction
{
    /// <summary>Stable across versions — it keys the user's settings.</summary>
    public required string Id { get; init; }
    public required string Title { get; init; }
    /// <summary>The palette category (File, Edit, Piano roll…), for filtering.</summary>
    public string Category { get; init; } = "";
    public KeyScope Scope { get; init; }
    public Shortcut? DefaultPrimary { get; init; }
    public Shortcut? DefaultAlternative { get; init; }
    /// <summary>A command-palette command (it has a palette entry and may have a menu item).</summary>
    public bool IsCommand { get; init; }
    /// <summary>A menu item carries this action's key (set from the menu at start-up). On macOS a
    /// menu key equivalent fires before any view sees the key, so it reaches every scope.</summary>
    public bool IsMenuCommand { get; set; }
    /// <summary>The menu shows no key for it (copy / cut / paste / delete — a menu key
    /// equivalent would take them from text fields).</summary>
    public bool NoMenuKey { get; init; }
    /// <summary>Listed but not editable (macOS ⌘Q, which the system's Quit owns).</summary>
    public bool Fixed { get; init; }
    public string[] Aliases { get; init; } = [];

    public Shortcut? Default(KeySlot slot) => slot == KeySlot.Primary ? DefaultPrimary : DefaultAlternative;

    public override string ToString() => Id;
}

/// <summary>An action has a primary key and one optional alternative (FR-009).</summary>
public enum KeySlot { Primary, Alternative }
