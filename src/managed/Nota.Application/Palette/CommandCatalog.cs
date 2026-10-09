// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Command palette (CP-24): every user command Nota has — the menu items, the view commands,
// transport, Settings pages — declared once with its id, title, category, shortcut and
// aliases. MainWindow.axaml's menu items reference these ids (app:Commands.Id) and take
// their shortcut from here; MainWindow.Commands.cs binds what each one does. The palette
// smoke test checks that every id is bound and every shortcut is listed in
// Settings → Shortcuts.

namespace Nota.Application.Palette;

public static class CommandCatalog
{
    /// <summary>The palette's own toggle — the one shortcut that works from every window.</summary>
    public const string Palette = "view.palette";

    /// <summary>Fresh command instances (a registry binds behaviour onto them).</summary>
    public static List<PaletteCommand> Create() => new()
    {
        // ---- File -------------------------------------------------------------------------
        C("file.new", "New Project", "File", "⌘N", "create blank empty"),
        C("file.open", "Open Project…", "File", "⌘O", "load"),
        C("file.save", "Save Project", "File", "⌘S", "store write"),
        C("file.saveAs", "Save Project As…", "File", "⌘⇧S", "copy rename"),
        C("file.saveVersion", "Save Version with Note…", "File", "⌥⌘S", "snapshot history commit"),
        C("file.import", "Import Audio…", "File", "", "add file wav"),
        C("file.export", "Export Audio…", "File", "⌘⇧E", "render bounce mixdown stems"),

        // ---- Edit -------------------------------------------------------------------------
        C("edit.undo", "Undo", "Edit", "⌘Z", "back revert"),
        C("edit.redo", "Redo", "Edit", "⌘⇧Z", "forward"),
        C("edit.duplicate", "Duplicate", "Edit", "⌘D", "copy clone"),
        C("edit.split", "Split at Playhead", "Edit", "⌘E", "cut slice", view: "Arrangement"),
        C("edit.consolidate", "Consolidate", "Edit", "⌘J", "join merge glue", view: "Arrangement"),
        C("edit.duplicateTime", "Duplicate Time", "Edit", "⌘⇧D", "repeat range", view: "Arrangement"),
        C("edit.insertSilence", "Insert Silence", "Edit", "⌘⇧I", "gap space", view: "Arrangement"),
        C("edit.lockEnvelopes", "Lock Envelopes", "Edit", "", "automation pin"),
        C("edit.cut", "Cut", "Edit", "", ""),
        C("edit.copy", "Copy", "Edit", "", ""),
        C("edit.paste", "Paste", "Edit", "", ""),
        C("edit.pasteBounced", "Paste Bounced Audio", "Edit", "⌘⇧V", "render resample", view: "Arrangement"),
        C("edit.copyToOtherView", "Copy Clips to Session / Arrangement", "Edit", "⌘⇧C", "transfer"),
        C("edit.delete", "Delete", "Edit", "", "remove erase"),
        C("edit.selectAll", "Select All Clips", "Edit", "⌘A", "everything", view: "Arrangement"),
        C("edit.toggleClipActive", "Activate / Deactivate Clip", "Edit", "0", "mute disable", view: "Arrangement"),

        // ---- View -------------------------------------------------------------------------
        C("view.arrangement", "Show Arrangement", "View", "", "timeline linear"),
        C("view.session", "Show Session", "View", "", "clips grid launch"),
        C("view.modular", "Show Modular", "View", "", "graph patch cv"),
        C("view.mixer", "Show Mixer", "View", "⌘⇧M", "faders channels"),
        C("view.connectPhone", "Connect Phone…", "View", "", "remote mobile qr"),
        C("view.toggleBrowser", "Toggle Browser", "View", "", "library sidebar"),
        C("view.toggleDetail", "Toggle Detail Panel", "View", "", "devices clip"),
        C("view.showDevices", "Show Devices", "View", "", "chain detail"),
        C("view.popOutDetail", "Open Devices in a Window", "View", "", "detach float pop out"),
        C("view.toggleOverview", "Toggle Overview", "View", "", "minimap", view: "Arrangement"),
        C("view.toggleSections", "Toggle Sections", "View", "", "markers locators", view: "Arrangement"),
        C("view.follow", "Follow Playhead", "View", "", "scroll"),
        C("view.clipNames", "Cycle Clip Names", "View", "", "labels", view: "Arrangement"),
        C("view.automation", "Toggle Automation Mode", "View", "⌘⇧A", "envelopes lanes", view: "Arrangement"),
        C("view.history", "Show Version History", "View", "", "undo history versions snapshots"),
        C("view.midiLearn", "MIDI Learn", "View", "", "map controller"),
        C(Palette, "Command Palette", "View", "⌘⇧P", "search", hide: true),

        // ---- Transport --------------------------------------------------------------------
        C("transport.playStop", "Play / Stop", "Transport", "Space", "start pause"),
        C("transport.stop", "Stop", "Transport", "Return", "halt"),
        C("transport.record", "Record", "Transport", "⌘R", "arm capture"),
        C("transport.loop", "Toggle Loop", "Transport", "⌘L", "cycle repeat"),
        C("transport.metronome", "Metronome", "Transport", "⌘M", "click"),
        C("transport.tapTempo", "Tap Tempo", "Transport", "", "bpm beat tap"),

        // ---- Track ------------------------------------------------------------------------
        C("track.newInstrument", "New MIDI Track", "Track", "", "add instrument track"),
        C("track.newAudio", "New Audio Track", "Track", "", "add"),
        C("track.newReturn", "New Return Track", "Track", "", "add send bus aux"),
        C("track.group", "Group Tracks", "Track", "⌘G", "folder bus", view: "Arrangement"),
        C("track.ungroup", "Ungroup Tracks", "Track", "⌘⇧G", "", view: "Arrangement"),
        C("track.freeze", "Freeze Track", "Track", "", "render bounce cpu"),

        // ---- Session ----------------------------------------------------------------------
        C("session.launchScene", "Launch Scene", "Session", "", "play row", view: "Session"),
        C("session.stopAll", "Stop All Clips", "Session", "", "halt", view: "Session"),
        C("session.insertScene", "Insert Scene", "Session", "⌘I", "add scene row", view: "Session"),

        // ---- Piano roll -------------------------------------------------------------------
        C("roll.quantize", "Quantize Notes", "Piano roll", "", "quantise grid snap align"),
        C("roll.octaveUp", "Transpose Notes Up an Octave", "Piano roll", "", "pitch shift"),
        C("roll.octaveDown", "Transpose Notes Down an Octave", "Piano roll", "", "pitch shift"),
        C("roll.removeOutOfScale", "Remove Out-of-Scale Notes", "Piano roll", "", "key fix"),

        // ---- Settings ---------------------------------------------------------------------
        C("prefs.open", "Settings", "Settings", "⌘,", "preferences options"),
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
        C("help.whatsNew", "What's New", "Help", "", "changelog release notes"),
        C("help.about", "About Nota", "Help", "", "version"),
    };

    private static PaletteCommand C(string id, string title, string category, string gesture, string aliases,
        string view = "", bool hide = false) => new()
    {
        Id = id, Title = title, Category = category, Gesture = gesture, View = view, HideInPalette = hide,
        Aliases = aliases.Length == 0 ? [] : aliases.Split(' ', StringSplitOptions.RemoveEmptyEntries),
    };
}
