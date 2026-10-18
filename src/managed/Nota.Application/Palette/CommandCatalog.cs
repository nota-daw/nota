// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Command palette (CP-24): every user command Nota has — the menu items, the view commands,
// transport, Settings pages — declared once with its id, title, category, shortcut and
// aliases. MainWindow.axaml's menu items reference these ids (app:Commands.Id) and take
// their shortcut from here; MainWindow.Commands.cs binds what each one does. The palette
// smoke test checks that every id is bound and every shortcut is listed in
// Settings → Shortcuts.

using Nota.Application.Input;

namespace Nota.Application.Palette;

public static class CommandCatalog
{
    /// <summary>The palette's own toggle — the one shortcut that works from every window.</summary>
    public const string Palette = "view.palette";

    /// <summary>Fresh command instances (a registry binds behaviour onto them).</summary>
    public static List<PaletteCommand> Create() => new()
    {
        // ---- File -------------------------------------------------------------------------
        C("file.new", "New project", "File", Shortcut.Cmd('N'), "create blank empty"),
        C("file.open", "Open project…", "File", Shortcut.Cmd('O'), "load"),
        C("file.save", "Save project", "File", Shortcut.Cmd('S'), "store write"),
        C("file.saveAs", "Save project as…", "File", Shortcut.CmdShift('S'), "copy rename"),
        C("file.saveVersion", "Save version with note…", "File", Shortcut.CmdAlt('S'), "snapshot history commit"),
        C("file.import", "Import audio…", "File", "", "add file wav"),
        C("file.export", "Export audio…", "File", Shortcut.CmdShift('E'), "render bounce mixdown stems"),

        // ---- Edit -------------------------------------------------------------------------
        C("edit.undo", "Undo", "Edit", Shortcut.Cmd('Z'), "back revert"),
        C("edit.redo", "Redo", "Edit", Shortcut.CmdShift('Z'), "forward"),
        C("edit.duplicate", "Duplicate", "Edit", Shortcut.Cmd('D'), "copy clone"),
        C("edit.split", "Split at playhead", "Edit", Shortcut.Cmd('E'), "cut slice", view: "Arrangement"),
        C("edit.consolidate", "Consolidate", "Edit", Shortcut.Cmd('J'), "join merge glue", view: "Arrangement"),
        C("edit.duplicateTime", "Duplicate time", "Edit", Shortcut.CmdShift('D'), "repeat range", view: "Arrangement"),
        C("edit.insertSilence", "Insert silence", "Edit", Shortcut.CmdShift('I'), "gap space", view: "Arrangement"),
        C("edit.lockEnvelopes", "Lock envelopes", "Edit", "", "automation pin"),
        C("edit.cut", "Cut", "Edit", "", ""),
        C("edit.copy", "Copy", "Edit", "", ""),
        C("edit.paste", "Paste", "Edit", "", ""),
        C("edit.pasteBounced", "Paste bounced audio", "Edit", Shortcut.CmdShift('V'), "render resample", view: "Arrangement"),
        C("edit.copyToOtherView", "Copy clips to Session / Arrangement", "Edit", Shortcut.CmdShift('C'), "transfer"),
        C("edit.delete", "Delete", "Edit", "", "remove erase"),
        C("edit.selectAll", "Select all clips", "Edit", Shortcut.Cmd('A'), "everything", view: "Arrangement"),
        C("edit.toggleClipActive", "Activate / deactivate clip", "Edit", Shortcut.None('0'), "mute disable", view: "Arrangement"),

        // ---- View -------------------------------------------------------------------------
        C("view.arrangement", "Show Arrangement", "View", "", "timeline linear"),
        C("view.session", "Show Session", "View", "", "clips grid launch"),
        C("view.modular", "Show Modular", "View", "", "graph patch cv"),
        C("view.mixer", "Show Mixer", "View", Shortcut.CmdShift('M'), "faders channels"),
        C("view.connectPhone", "Connect phone…", "View", "", "remote mobile qr"),
        C("view.toggleBrowser", "Toggle browser", "View", "", "library sidebar"),
        C("view.toggleDetail", "Toggle detail panel", "View", "", "devices clip"),
        C("view.showDevices", "Show devices", "View", "", "chain detail"),
        C("view.popOutDetail", "Open devices in a window", "View", "", "detach float pop out"),
        C("view.toggleOverview", "Toggle overview", "View", "", "minimap", view: "Arrangement"),
        C("view.toggleSections", "Toggle sections", "View", "", "markers locators", view: "Arrangement"),
        C("view.follow", "Follow playhead", "View", "", "scroll"),
        C("view.showPlayhead", "Show playhead", "View", Shortcut.None('P'), "find locate scroll reveal cursor position", view: "Arrangement"),
        C("view.clipNames", "Cycle clip names", "View", "", "labels", view: "Arrangement"),
        C("view.automation", "Toggle automation mode", "View", Shortcut.CmdShift('A'), "envelopes lanes", view: "Arrangement"),
        C("view.history", "Show version history", "View", "", "undo history versions snapshots"),
        C("view.midiLearn", "MIDI learn", "View", "", "map controller"),
        C("view.fullscreen", "Toggle full screen", "View", Shortcut.None("F11"), "maximize immersive hide chrome"),
        C(Palette, "Command palette", "View", Shortcut.CmdShift('P'), "search", hide: true),

        // ---- Transport --------------------------------------------------------------------
        C("transport.playStop", "Play / stop", "Transport", Shortcut.None("Space"), "start pause"),
        C("transport.playPause", "Play / pause", "Transport", Shortcut.Shift("Space"), "resume continue hold"),
        C("transport.stop", "Stop", "Transport", Shortcut.None("Return"), "halt"),
        C("transport.record", "Record", "Transport", Shortcut.Cmd('R'), "arm capture"),
        C("transport.loop", "Toggle loop", "Transport", Shortcut.Cmd('L'), "cycle repeat"),
        C("transport.metronome", "Metronome", "Transport", Shortcut.Cmd('M'), "click"),
        C("transport.loopRecordMode", "Loop record: overdub / replace", "Transport", Shortcut.CmdShift('R'), "overdub replace take pass loop record"),
        C("transport.tapTempo", "Tap tempo", "Transport", "", "bpm beat tap"),

        // ---- Track ------------------------------------------------------------------------
        C("track.newInstrument", "New MIDI track", "Track", "", "add instrument track"),
        C("track.newAudio", "New audio track", "Track", "", "add"),
        C("track.newReturn", "New return track", "Track", "", "add send bus aux"),
        C("track.group", "Group tracks", "Track", Shortcut.Cmd('G'), "folder bus", view: "Arrangement"),
        C("track.ungroup", "Ungroup tracks", "Track", Shortcut.CmdShift('G'), "", view: "Arrangement"),
        C("track.freeze", "Freeze track", "Track", "", "render bounce cpu"),

        // ---- Session ----------------------------------------------------------------------
        C("session.launchScene", "Launch scene", "Session", "", "play row", view: "Session"),
        C("session.stopAll", "Stop all clips", "Session", "", "halt", view: "Session"),
        C("session.insertScene", "Insert scene", "Session", Shortcut.Cmd('I'), "add scene row", view: "Session"),

        // ---- Piano roll -------------------------------------------------------------------
        C("roll.quantize", "Quantize notes", "Piano roll", "", "quantise grid snap align"),
        C("roll.octaveUp", "Transpose notes up an octave", "Piano roll", "", "pitch shift"),
        C("roll.octaveDown", "Transpose notes down an octave", "Piano roll", "", "pitch shift"),
        C("roll.removeOutOfScale", "Remove out-of-scale notes", "Piano roll", "", "key fix"),
        C("roll.fitNotes", "Fit to notes", "Piano roll", Shortcut.Shift('F'), "zoom vertical rows height"),

        // ---- Grid (the arrangement or the piano roll — whichever was used last) -----------
        C("grid.narrower", "Narrow grid", "Grid", Shortcut.Cmd('1'), "finer smaller snap"),
        C("grid.wider", "Widen grid", "Grid", Shortcut.Cmd('2'), "coarser larger snap"),
        C("grid.triplets", "Toggle triplet grid", "Grid", Shortcut.Cmd('3'), "triplet 1/8T swing"),
        C("grid.snap", "Toggle snap to grid", "Grid", Shortcut.Cmd('4'), "magnet free"),
        C("grid.adaptive", "Toggle adaptive grid", "Grid", Shortcut.Cmd('5'), "auto zoom fixed"),

        // ---- Settings ---------------------------------------------------------------------
        C("prefs.open", "Settings", "Settings", Shortcut.Cmd(','), "preferences options"),
        C("prefs.audio", "Settings: Audio", "Settings", "", "preferences device driver sample rate buffer"),
        C("prefs.midi", "Settings: MIDI", "Settings", "", "preferences controller keyboard"),
        C("prefs.gamepads", "Settings: Gamepads", "Settings", "", "preferences joystick controller"),
        C("prefs.remote", "Settings: Remote", "Settings", "", "preferences phone"),
        C("prefs.plugins", "Settings: Plug-ins", "Settings", "", "preferences vst3 au scan folders"),
        C("prefs.downloads", "Settings: Downloads", "Settings", "", "preferences store models sample packs"),
        C("prefs.library", "Settings: Library", "Settings", "", "preferences folders"),
        C("prefs.appearance", "Settings: Appearance", "Settings", "", "preferences theme dark light"),
        C("prefs.shortcuts", "Settings: Shortcuts", "Settings", "", "preferences hotkeys keys keyboard"),

        // ---- Help -------------------------------------------------------------------------
        C("help.docs", "Documentation", "Help", "", "manual guide"),
        C("help.whatsNew", "What's new", "Help", "", "changelog release notes"),
        C("help.about", "About Nota", "Help", "", "version"),
    };

    private static PaletteCommand C(string id, string title, string category, string gesture, string aliases,
        string view = "", bool hide = false) => new()
    {
        Id = id, Title = title, Category = category, Gesture = gesture, View = view, HideInPalette = hide,
        Aliases = aliases.Length == 0 ? [] : aliases.Split(' ', StringSplitOptions.RemoveEmptyEntries),
    };
}
