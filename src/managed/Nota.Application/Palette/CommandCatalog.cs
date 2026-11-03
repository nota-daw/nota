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
using Nota.Application.Localization;

namespace Nota.Application.Palette;

public static class CommandCatalog
{
    /// <summary>The palette's own toggle — the one shortcut that works from every window.</summary>
    public const string Palette = "view.palette";

    /// <summary>Fresh command instances (a registry binds behaviour onto them).</summary>
    public static List<PaletteCommand> Create() => new()
    {
        // ---- File -------------------------------------------------------------------------
        C("file.new", Loc.T("New project"), Loc.T("File"), Shortcut.Cmd('N'), "create blank empty"),
        C("file.open", Loc.T("Open project…"), Loc.T("File"), Shortcut.Cmd('O'), "load"),
        C("file.save", Loc.T("Save project"), Loc.T("File"), Shortcut.Cmd('S'), "store write"),
        C("file.saveAs", Loc.T("Save project as…"), Loc.T("File"), Shortcut.CmdShift('S'), "copy rename"),
        C("file.saveVersion", Loc.T("Save version with note…"), Loc.T("File"), Shortcut.CmdAlt('S'), "snapshot history commit"),
        C("file.import", Loc.T("Import audio…"), Loc.T("File"), "", "add file wav"),
        C("file.export", Loc.T("Export audio…"), Loc.T("File"), Shortcut.CmdShift('E'), "render bounce mixdown stems"),
        C("file.exit", Loc.T("Exit Nota"), Loc.T("File"), Shortcut.Cmd('Q'), "quit close leave"),

        // ---- Edit -------------------------------------------------------------------------
        C("edit.undo", Loc.T("Undo"), Loc.T("Edit"), Shortcut.Cmd('Z'), "back revert"),
        C("edit.redo", Loc.T("Redo"), Loc.T("Edit"), Shortcut.CmdShift('Z'), "forward"),
        C("edit.duplicate", Loc.T("Duplicate"), Loc.T("Edit"), Shortcut.Cmd('D'), "copy clone"),
        C("edit.split", Loc.T("Split at playhead"), Loc.T("Edit"), Shortcut.Cmd('E'), "cut slice", view: "Arrangement"),
        C("edit.fadeSplits", Loc.T("Fade at splits"), Loc.T("Edit"), "", "crossfade declick", view: "Arrangement"),
        C("edit.consolidate", Loc.T("Consolidate"), Loc.T("Edit"), Shortcut.Cmd('J'), "join merge glue", view: "Arrangement"),
        C("edit.duplicateTime", Loc.T("Duplicate time"), Loc.T("Edit"), Shortcut.CmdShift('D'), "repeat range", view: "Arrangement"),
        C("edit.insertSilence", Loc.T("Insert silence"), Loc.T("Edit"), Shortcut.CmdShift('I'), "gap space", view: "Arrangement"),
        C("edit.lockEnvelopes", Loc.T("Lock envelopes"), Loc.T("Edit"), "", "automation pin"),
        C("edit.cut", Loc.T("Cut"), Loc.T("Edit"), "", ""),
        C("edit.copy", Loc.T("Copy"), Loc.T("Edit"), "", ""),
        C("edit.paste", Loc.T("Paste"), Loc.T("Edit"), "", ""),
        C("edit.pasteBounced", Loc.T("Paste bounced audio"), Loc.T("Edit"), Shortcut.CmdShift('V'), "render resample", view: "Arrangement"),
        C("edit.copyToOtherView", Loc.T("Copy clips to Session / Arrangement"), Loc.T("Edit"), Shortcut.CmdShift('C'), "transfer"),
        C("edit.delete", Loc.T("Delete"), Loc.T("Edit"), "", "remove erase"),
        C("edit.selectAll", Loc.T("Select all clips"), Loc.T("Edit"), Shortcut.Cmd('A'), "everything", view: "Arrangement"),
        C("edit.toggleClipActive", Loc.T("Activate / deactivate clip"), Loc.T("Edit"), Shortcut.None('0'), "mute disable", view: "Arrangement"),

        // ---- View -------------------------------------------------------------------------
        C("view.arrangement", Loc.T("Show Arrangement"), Loc.T("View"), "", "timeline linear"),
        C("view.session", Loc.T("Show Session"), Loc.T("View"), "", "clips grid launch"),
        C("view.modular", Loc.T("Show Modular"), Loc.T("View"), "", "graph patch cv"),
        C("view.mixer", Loc.T("Show Mixer"), Loc.T("View"), Shortcut.CmdShift('M'), "faders channels"),
        C("view.mixerFloat", Loc.T("Mixer in its own window"), Loc.T("View"), "", "dock undock float pop out mixer window"),
        C("view.connectPhone", Loc.T("Connect phone…"), Loc.T("View"), "", "remote mobile qr"),
        C("view.toggleBrowser", Loc.T("Toggle browser"), Loc.T("View"), "", "library sidebar"),
        C("view.toggleDetail", Loc.T("Toggle detail panel"), Loc.T("View"), "", "devices clip"),
        C("view.maximizeDetail", Loc.T("Clip editor fills the window"), Loc.T("View"), Shortcut.CmdShift('F'), "maximize maximise full detail panel piano roll restore split"),
        C("view.showDevices", Loc.T("Show devices"), Loc.T("View"), "", "chain detail"),
        C("view.popOutDetail", Loc.T("Open devices in a window"), Loc.T("View"), "", "detach float pop out"),
        C("view.toggleOverview", Loc.T("Toggle overview"), Loc.T("View"), "", "minimap", view: "Arrangement"),
        C("view.toggleSections", Loc.T("Toggle sections"), Loc.T("View"), "", "markers locators", view: "Arrangement"),
        C("edit.resetSectionName", Loc.T("Reset section name"), Loc.T("Edit"), "", "default automatic rename section at playhead", view: "Arrangement"),
        C("edit.moveSectionEarlier", Loc.T("Move section earlier"), Loc.T("Edit"), "", "rearrange reorder section content left before previous at playhead", view: "Arrangement"),
        C("edit.moveSectionLater", Loc.T("Move section later"), Loc.T("Edit"), "", "rearrange reorder section content right after next at playhead", view: "Arrangement"),
        C("edit.duplicateSectionWithContent", Loc.T("Duplicate section with content"), Loc.T("Edit"), "", "copy repeat section clips automation ripple at playhead", view: "Arrangement"),
        C("edit.deleteSectionWithContent", Loc.T("Delete section with content"), Loc.T("Edit"), "", "remove cut section clips automation ripple close gap at playhead", view: "Arrangement"),
        C("view.follow", Loc.T("Follow playhead"), Loc.T("View"), "", "scroll"),
        C("view.showPlayhead", Loc.T("Show playhead"), Loc.T("View"), Shortcut.None('P'), "find locate scroll reveal cursor position", view: "Arrangement"),
        C("view.clipNames", Loc.T("Cycle clip names"), Loc.T("View"), "", "labels", view: "Arrangement"),
        C("view.headersRight", Loc.T("Track headers on the right"), Loc.T("View"), "", "track list side left right swap mirror ableton", view: "Arrangement"),
        C("view.automation", Loc.T("Toggle automation mode"), Loc.T("View"), Shortcut.CmdShift('A'), "envelopes lanes", view: "Arrangement"),
        C("view.history", Loc.T("Show version history"), Loc.T("View"), "", "undo history versions snapshots"),
        C("view.midiLearn", Loc.T("MIDI learn"), Loc.T("View"), "", "map controller"),
        C("view.fullscreen", Loc.T("Toggle full screen"), Loc.T("View"), Shortcut.None("F11"), "maximize immersive hide chrome"),
        C(Palette, Loc.T("Command palette"), Loc.T("View"), Shortcut.CmdShift('P'), "search", hide: true),

        // ---- Transport --------------------------------------------------------------------
        C("transport.playStop", Loc.T("Play / stop"), Loc.T("Transport"), Shortcut.None("Space"), "start pause"),
        C("transport.playPause", Loc.T("Play / pause"), Loc.T("Transport"), Shortcut.Shift("Space"), "resume continue hold"),
        C("transport.stop", Loc.T("Stop"), Loc.T("Transport"), Shortcut.None("Return"), "halt"),
        C("transport.record", Loc.T("Record"), Loc.T("Transport"), Shortcut.Cmd('R'), "arm capture"),
        C("transport.armOnSelect", Loc.T("Auto-arm selected track"), Loc.T("Transport"), "", "arm on select follow selection record keyboard"),
        C("transport.loop", Loc.T("Toggle loop"), Loc.T("Transport"), Shortcut.Cmd('L'), "cycle repeat"),
        C("transport.metronome", Loc.T("Metronome"), Loc.T("Transport"), Shortcut.Cmd('M'), "click"),
        C("transport.loopRecordMode", Loc.T("Loop record: overdub / replace"), Loc.T("Transport"), Shortcut.CmdShift('R'), "overdub replace take pass loop record"),
        C("transport.tapTempo", Loc.T("Tap tempo"), Loc.T("Transport"), "", "bpm beat tap"),

        // ---- Track ------------------------------------------------------------------------
        C("track.newInstrument", Loc.T("New MIDI track"), Loc.T("Track"), "", "add instrument track"),
        C("track.newAudio", Loc.T("New audio track"), Loc.T("Track"), "", "add"),
        C("track.newReturn", Loc.T("New return track"), Loc.T("Track"), "", "add send bus aux"),
        C("track.group", Loc.T("Group tracks"), Loc.T("Track"), Shortcut.Cmd('G'), "folder bus", view: "Arrangement"),
        C("track.ungroup", Loc.T("Ungroup tracks"), Loc.T("Track"), Shortcut.CmdShift('G'), "", view: "Arrangement"),
        C("track.freeze", Loc.T("Freeze track"), Loc.T("Track"), "", "render bounce cpu"),

        // ---- Session ----------------------------------------------------------------------
        C("session.launchScene", Loc.T("Launch scene"), Loc.T("Session"), "", "play row", view: "Session"),
        C("session.stopAll", Loc.T("Stop all clips"), Loc.T("Session"), "", "halt", view: "Session"),
        C("session.insertScene", Loc.T("Insert scene"), Loc.T("Session"), Shortcut.Cmd('I'), "add scene row", view: "Session"),

        // ---- Piano roll -------------------------------------------------------------------
        C("roll.quantize", Loc.T("Quantize notes"), Loc.T("Piano roll"), "", "quantise grid snap align"),
        C("roll.octaveUp", Loc.T("Transpose notes up an octave"), Loc.T("Piano roll"), "", "pitch shift"),
        C("roll.octaveDown", Loc.T("Transpose notes down an octave"), Loc.T("Piano roll"), "", "pitch shift"),
        C("roll.removeOutOfScale", Loc.T("Remove out-of-scale notes"), Loc.T("Piano roll"), "", "key fix"),
        C("roll.fitNotes", Loc.T("Fit to notes"), Loc.T("Piano roll"), Shortcut.Shift('F'), "zoom vertical rows height"),
        C("roll.fold", Loc.T("Fold the piano roll"), Loc.T("Piano roll"), Shortcut.Alt('F').WithShift(), "fold scale rows drum pads names hide keys"),
        C("roll.editSelected", Loc.T("Edit selected clips together"), Loc.T("Piano roll"), "", "multi clip several open piano roll", view: "Arrangement"),
        C("roll.trackMode", Loc.T("Toggle clip editor track mode"), Loc.T("Piano roll"), "", "track mode whole track all clips every clip piano roll", view: "Arrangement"),

        // ---- Grid (the arrangement or the piano roll — whichever was used last) -----------
        C("grid.narrower", Loc.T("Narrow grid"), Loc.T("Grid"), Shortcut.Cmd('1'), "finer smaller snap"),
        C("grid.wider", Loc.T("Widen grid"), Loc.T("Grid"), Shortcut.Cmd('2'), "coarser larger snap"),
        C("grid.triplets", Loc.T("Toggle triplet grid"), Loc.T("Grid"), Shortcut.Cmd('3'), "triplet 1/8T swing"),
        C("grid.snap", Loc.T("Toggle snap to grid"), Loc.T("Grid"), Shortcut.Cmd('4'), "magnet free"),
        C("grid.adaptive", Loc.T("Toggle adaptive grid"), Loc.T("Grid"), Shortcut.Cmd('5'), "auto zoom fixed"),

        // ---- Settings ---------------------------------------------------------------------
        C("prefs.open", Loc.T("Settings"), Loc.T("Settings"), Shortcut.Cmd(','), "preferences options"),
        C("prefs.audio", Loc.T("Settings: Audio"), Loc.T("Settings"), "", "preferences device driver sample rate buffer"),
        C("prefs.midi", Loc.T("Settings: MIDI"), Loc.T("Settings"), "", "preferences controller keyboard"),
        C("prefs.gamepads", Loc.T("Settings: Gamepads"), Loc.T("Settings"), "", "preferences joystick controller"),
        C("prefs.remote", Loc.T("Settings: Remote"), Loc.T("Settings"), "", "preferences phone"),
        C("prefs.plugins", Loc.T("Settings: Plug-ins"), Loc.T("Settings"), "", "preferences vst3 au scan folders"),
        C("prefs.downloads", Loc.T("Settings: Downloads"), Loc.T("Settings"), "", "preferences store models sample packs"),
        C("prefs.library", Loc.T("Settings: Library"), Loc.T("Settings"), "", "preferences folders"),
        C("prefs.appearance", Loc.T("Settings: Appearance"), Loc.T("Settings"), "", "preferences theme dark light language langue francais english"),
        C("prefs.shortcuts", Loc.T("Settings: Shortcuts"), Loc.T("Settings"), "", "preferences hotkeys keys keyboard"),
        C("prefs.updates", Loc.T("Settings: Updates"), Loc.T("Settings"), "", "preferences update automatic check version"),

        // ---- Help -------------------------------------------------------------------------
        C("help.docs", Loc.T("Documentation"), Loc.T("Help"), "", "manual guide"),
        C("help.whatsNew", Loc.T("What's new"), Loc.T("Help"), "", "changelog release notes"),
        C("help.checkUpdates", Loc.T("Check for updates…"), Loc.T("Help"), "", "update upgrade new version download install"),
        C("help.about", Loc.T("About Nota"), Loc.T("Help"), "", "version"),
    };

    private static PaletteCommand C(string id, string title, string category, string gesture, string aliases,
        string view = "", bool hide = false) => new()
    {
        Id = id, Title = title, Category = category, Gesture = gesture, View = view, HideInPalette = hide,
        Aliases = aliases.Length == 0 ? [] : aliases.Split(' ', StringSplitOptions.RemoveEmptyEntries),
    };
}
