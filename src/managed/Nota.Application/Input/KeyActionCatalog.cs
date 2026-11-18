// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Every rebindable keyboard action (specs/096, #61) with its scope and default keys. The
// command-palette commands come in from CommandCatalog (same id, its gesture as the default);
// the keys views used to compare by hand — the note map, nudges, transposes, browser and
// Modular keys — are declared here. contracts/actions.md in the spec lists them.

using System;
using System.Collections.Generic;
using System.Linq;
using Nota.Application.Localization;
using Nota.Application.Palette;

namespace Nota.Application.Input;

public static class KeyActionCatalog
{
    private const ShortcutModifiers P = ShortcutModifiers.Primary, S = ShortcutModifiers.Shift, A = ShortcutModifiers.Alt;

    /// <summary>The key tokens a binding may use (research R4), besides A–Z and 0–9.</summary>
    public static readonly IReadOnlySet<string> NamedTokens = new HashSet<string>(StringComparer.Ordinal)
    {
        "Space", "Return", "Tab", "Esc", "⌫", "⌦", "←", "→", "↑", "↓", "PgUp", "PgDn", "Home", "End",
        ",", ".", "/", ";", "'", "[", "]", "\\", "-", "=", "`",
        "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
        "F13", "F14", "F15", "F16", "F17", "F18", "F19", "F20", "F21", "F22", "F23", "F24",
    };

    public static bool IsKnownToken(string key) =>
        key.Length == 1 && (key[0] is >= 'A' and <= 'Z' or >= '0' and <= '9') || NamedTokens.Contains(key);

    /// <summary>Shared editing actions (FR-010): one binding each, read by every view that copies,
    /// pastes, deletes… — tracks, devices, clips, notes, Session, Modular.</summary>
    public static readonly IReadOnlySet<string> Shared = new HashSet<string>(StringComparer.Ordinal)
    {
        "edit.copy", "edit.cut", "edit.paste", "edit.duplicate", "edit.delete", "edit.selectAll", "edit.cancel",
    };

    public static List<KeyAction> Create() => Create(OperatingSystem.IsMacOS());

    public static List<KeyAction> Create(bool mac)
    {
        var list = new List<KeyAction>();
        foreach (var c in CommandCatalog.Create())
        {
            Shortcut? primary = c.Gesture.Length > 0 ? Shortcut.Parse(c.Gesture) : null;
            Shortcut? alt = null;
            bool noMenuKey = false;
            switch (c.Id)
            {
                case "edit.copy": primary = new(P, "C"); noMenuKey = true; break;
                case "edit.cut": primary = new(P, "X"); noMenuKey = true; break;
                case "edit.paste": primary = new(P, "V"); noMenuKey = true; break;
                case "edit.delete": primary = Shortcut.None("⌫"); alt = Shortcut.None("⌦"); noMenuKey = true; break;
                case "roll.octaveUp": primary = new(S, "↑"); break;
                case "roll.octaveDown": primary = new(S, "↓"); break;
            }
            list.Add(new KeyAction
            {
                Id = c.Id, Title = c.Title, Category = c.Category, Scope = ScopeOf(c), IsCommand = true,
                // Until the menu says which it carries (KeyBindings.SetMenuCommands), assume every
                // command may be in it: the conflict check errs towards a warning, never a clash.
                IsMenuCommand = true,
                DefaultPrimary = primary, DefaultAlternative = alt, NoMenuKey = noMenuKey, Aliases = c.Aliases,
                Fixed = mac && c.Id == "file.exit",
            });
        }

        string arr = Loc.T("keys.group.arrangement"), roll = Loc.T("common.piano_roll"), edit = Loc.T("common.edit"),
            view = Loc.T("commands.view"), notes = Loc.T("keys.group.notes");

        Add("edit.cancel", Loc.T("keys.edit_cancel"), edit, KeyScope.Global, Shortcut.None("Esc"), alias: "escape deselect stop gesture");
        Add("view.cycleDetailTab", Loc.T("keys.view_cycle_detail_tab"), view, KeyScope.Global, Shortcut.None("Tab"), alias: "devices pattern clip tab detail");
        if (!mac) Add("view.openMenu", Loc.T("keys.view_open_menu"), view, KeyScope.Global, Shortcut.None("F10"), alias: "menu bar");

        Add("arr.nudgeLeft", Loc.T("keys.arr_nudge_left"), arr, KeyScope.Arrangement, Shortcut.None("←"));
        Add("arr.nudgeRight", Loc.T("keys.arr_nudge_right"), arr, KeyScope.Arrangement, Shortcut.None("→"));
        Add("arr.nudgeFineLeft", Loc.T("keys.arr_nudge_fine_left"), arr, KeyScope.Arrangement, new(A, "←"));
        Add("arr.nudgeFineRight", Loc.T("keys.arr_nudge_fine_right"), arr, KeyScope.Arrangement, new(A, "→"));
        Add("arr.moveToTrackUp", Loc.T("keys.arr_move_to_track_up"), arr, KeyScope.Arrangement, new(A, "↑"));
        Add("arr.moveToTrackDown", Loc.T("keys.arr_move_to_track_down"), arr, KeyScope.Arrangement, new(A, "↓"));

        // The typing keyboard: the A-row is the white keys from C4, the Q-row the black keys.
        foreach (var (id, key) in NoteKeys) Add(id, NoteTitle(id), notes, KeyScope.Notes, Shortcut.None(key), alias: "computer keyboard note piano");
        Add("notes.octaveDown", Loc.T("keys.notes_octave_down"), notes, KeyScope.Notes, Shortcut.None("Z"), alias: "computer keyboard octave");
        Add("notes.octaveUp", Loc.T("keys.notes_octave_up"), notes, KeyScope.Notes, Shortcut.None("X"), alias: "computer keyboard octave");
        Add("notes.velocityDown", Loc.T("keys.notes_velocity_down"), notes, KeyScope.Notes, Shortcut.None("C"), alias: "computer keyboard velocity");
        Add("notes.velocityUp", Loc.T("keys.notes_velocity_up"), notes, KeyScope.Notes, Shortcut.None("V"), alias: "computer keyboard velocity");

        string devices = Loc.T("keys.group.devices"), browser = Loc.T("keys.group.browser"), modular = Loc.T("keys.group.modular"),
            session = Loc.T("commands.session");
        Add("devices.group", Loc.T("keys.devices_group"), devices, KeyScope.Devices, new(P, "G"), alias: "rack");
        Add("devices.ungroup", Loc.T("keys.devices_ungroup"), devices, KeyScope.Devices, new(P | S, "G"), alias: "rack");

        Add("browser.back", Loc.T("keys.browser_back"), browser, KeyScope.Browser, new(P, "["), mac ? null : new(A, "←"));
        Add("browser.forward", Loc.T("keys.browser_forward"), browser, KeyScope.Browser, new(P, "]"), mac ? null : new(A, "→"));
        Add("browser.up", Loc.T("keys.browser_up"), browser, KeyScope.Browser, new(P, "↑"), mac ? null : new(A, "↑"), alias: "parent folder");
        Add("browser.editPath", Loc.T("keys.browser_edit_path"), browser, KeyScope.Browser, new(P, "L"), alias: "path type paste folder");

        Add("modular.fit", Loc.T("keys.modular_fit"), modular, KeyScope.Modular, Shortcut.None("F"), alias: "zoom all nodes");
        Add("modular.bypass", Loc.T("keys.modular_bypass"), modular, KeyScope.Modular, Shortcut.None("B"), alias: "off disable");

        Add("session.launchSelection", Loc.T("keys.session_launch_selection"), session, KeyScope.Session, Shortcut.None("Return"), alias: "play launch slot scene");
        Add("session.rename", Loc.T("keys.session_rename"), session, KeyScope.Session, Shortcut.None("F2"));

        Add("roll.moveLeft", Loc.T("keys.roll_move_left"), roll, KeyScope.PianoRoll, Shortcut.None("←"), alias: "notes grid");
        Add("roll.moveRight", Loc.T("keys.roll_move_right"), roll, KeyScope.PianoRoll, Shortcut.None("→"), alias: "notes grid");
        Add("roll.transposeUp", Loc.T("keys.roll_transpose_up"), roll, KeyScope.PianoRoll, Shortcut.None("↑"), alias: "semitone pitch");
        Add("roll.transposeDown", Loc.T("keys.roll_transpose_down"), roll, KeyScope.PianoRoll, Shortcut.None("↓"), alias: "semitone pitch");
        Add("roll.shorten", Loc.T("keys.roll_shorten"), roll, KeyScope.PianoRoll, new(S, "←"), alias: "length");
        Add("roll.lengthen", Loc.T("keys.roll_lengthen"), roll, KeyScope.PianoRoll, new(S, "→"), alias: "length");
        Add("roll.velocityUp", Loc.T("keys.roll_velocity_up"), roll, KeyScope.PianoRoll, new(A, "↑"));
        Add("roll.velocityDown", Loc.T("keys.roll_velocity_down"), roll, KeyScope.PianoRoll, new(A, "↓"));
        Add("roll.velocityUpFine", Loc.T("keys.roll_velocity_up_fine"), roll, KeyScope.PianoRoll, new(A | S, "↑"));
        Add("roll.velocityDownFine", Loc.T("keys.roll_velocity_down_fine"), roll, KeyScope.PianoRoll, new(A | S, "↓"));

        return list;

        void Add(string id, string title, string category, KeyScope scope, Shortcut primary, Shortcut? alternative = null, string alias = "") =>
            list.Add(new KeyAction
            {
                Id = id, Title = title, Category = category, Scope = scope,
                DefaultPrimary = primary, DefaultAlternative = alternative,
                Aliases = alias.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            });
    }

    /// <summary>The note-map actions and their default keys, from C4 to C5.</summary>
    public static readonly (string Id, string Key)[] NoteKeys =
    {
        ("notes.c", "A"), ("notes.cs", "W"), ("notes.d", "S"), ("notes.ds", "E"), ("notes.e", "D"),
        ("notes.f", "F"), ("notes.fs", "T"), ("notes.g", "G"), ("notes.gs", "Y"), ("notes.a", "H"),
        ("notes.as", "U"), ("notes.b", "J"), ("notes.c2", "K"),
    };

    private static string NoteTitle(string id) => id switch
    {
        "notes.c" => Loc.T("keys.notes_c"),
        "notes.cs" => Loc.T("keys.notes_cs"),
        "notes.d" => Loc.T("keys.notes_d"),
        "notes.ds" => Loc.T("keys.notes_ds"),
        "notes.e" => Loc.T("keys.notes_e"),
        "notes.f" => Loc.T("keys.notes_f"),
        "notes.fs" => Loc.T("keys.notes_fs"),
        "notes.g" => Loc.T("keys.notes_g"),
        "notes.gs" => Loc.T("keys.notes_gs"),
        "notes.a" => Loc.T("keys.notes_a"),
        "notes.as" => Loc.T("keys.notes_as"),
        "notes.b" => Loc.T("keys.notes_b"),
        "notes.c2" => Loc.T("keys.notes_c2"),
        _ => id,
    };

    // Commands keep their palette view: Arrangement-view keys are window-wide but live only
    // while the Arrangement shows; piano-roll and Session commands belong to their grids.
    private static KeyScope ScopeOf(PaletteCommand c)
    {
        if (Shared.Contains(c.Id)) return KeyScope.Global;
        if (c.Id.StartsWith("roll.", StringComparison.Ordinal) && c.View.Length == 0) return KeyScope.PianoRoll;
        return c.View switch
        {
            "Arrangement" => KeyScope.Arrangement,
            "Session" => KeyScope.Session,
            _ => KeyScope.Global,
        };
    }
}
