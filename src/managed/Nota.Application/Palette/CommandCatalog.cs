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
        C("file.new", Loc.T("common.new_project"), Loc.T("common.file"), Shortcut.Cmd('N'), "create blank empty"),
        C("file.open", Loc.T("commands.open_project"), Loc.T("common.file"), Shortcut.Cmd('O'), "load"),
        C("file.save", Loc.T("commands.save_project"), Loc.T("common.file"), Shortcut.Cmd('S'), "store write"),
        C("file.saveAs", Loc.T("commands.save_project_as"), Loc.T("common.file"), Shortcut.CmdShift('S'), "copy rename"),
        C("file.saveVersion", Loc.T("commands.save_version_with_note"), Loc.T("common.file"), Shortcut.CmdAlt('S'), "snapshot history commit"),
        C("file.import", Loc.T("commands.import_audio"), Loc.T("common.file"), "", "add file wav"),
        C("file.export", Loc.T("commands.export_audio"), Loc.T("common.file"), Shortcut.CmdShift('E'), "render bounce mixdown stems"),
        C("file.exit", Loc.T("commands.exit_nota"), Loc.T("common.file"), Shortcut.Cmd('Q'), "quit close leave"),

        // ---- Edit -------------------------------------------------------------------------
        C("edit.undo", Loc.T("common.undo"), Loc.T("common.edit"), Shortcut.Cmd('Z'), "back revert"),
        C("edit.redo", Loc.T("commands.redo"), Loc.T("common.edit"), Shortcut.CmdShift('Z'), "forward"),
        C("edit.duplicate", Loc.T("common.duplicate"), Loc.T("common.edit"), Shortcut.Cmd('D'), "copy clone"),
        C("edit.split", Loc.T("commands.split_at_playhead"), Loc.T("common.edit"), Shortcut.Cmd('E'), "cut slice", view: "Arrangement"),
        C("edit.fadeSplits", Loc.T("commands.fade_at_splits"), Loc.T("common.edit"), "", "crossfade declick", view: "Arrangement"),
        C("edit.consolidate", Loc.T("common.consolidate"), Loc.T("common.edit"), Shortcut.Cmd('J'), "join merge glue", view: "Arrangement"),
        C("edit.duplicateTime", Loc.T("common.duplicate_time"), Loc.T("common.edit"), Shortcut.CmdShift('D'), "repeat range", view: "Arrangement"),
        C("edit.insertSilence", Loc.T("common.insert_silence"), Loc.T("common.edit"), Shortcut.CmdShift('I'), "gap space", view: "Arrangement"),
        C("edit.lockEnvelopes", Loc.T("commands.lock_envelopes"), Loc.T("common.edit"), "", "automation pin"),
        C("edit.cut", Loc.T("common.cut"), Loc.T("common.edit"), "", ""),
        C("edit.copy", Loc.T("common.copy"), Loc.T("common.edit"), "", ""),
        C("edit.paste", Loc.T("common.paste"), Loc.T("common.edit"), "", ""),
        C("edit.pasteBounced", Loc.T("common.paste_bounced_audio"), Loc.T("common.edit"), Shortcut.CmdShift('V'), "render resample", view: "Arrangement"),
        C("edit.copyToOtherView", Loc.T("commands.copy_clips_to_session_arrangement"), Loc.T("common.edit"), Shortcut.CmdShift('C'), "transfer"),
        C("edit.delete", Loc.T("common.delete"), Loc.T("common.edit"), "", "remove erase"),
        C("edit.selectAll", Loc.T("commands.select_all_clips"), Loc.T("common.edit"), Shortcut.Cmd('A'), "everything", view: "Arrangement"),
        C("edit.toggleClipActive", Loc.T("commands.activate_deactivate_clip"), Loc.T("common.edit"), Shortcut.None('0'), "mute disable", view: "Arrangement"),

        // ---- View -------------------------------------------------------------------------
        C("view.arrangement", Loc.T("commands.show_arrangement"), Loc.T("commands.view"), "", "timeline linear"),
        C("view.session", Loc.T("commands.show_session"), Loc.T("commands.view"), "", "clips grid launch"),
        C("view.modular", Loc.T("commands.show_modular"), Loc.T("commands.view"), "", "graph patch cv"),
        C("view.mixer", Loc.T("commands.show_mixer"), Loc.T("commands.view"), Shortcut.CmdShift('M'), "faders channels"),
        C("view.mixerFloat", Loc.T("commands.mixer_in_its_own_window"), Loc.T("commands.view"), "", "dock undock float pop out mixer window"),
        C("view.connectPhone", Loc.T("commands.connect_phone"), Loc.T("commands.view"), "", "remote mobile qr"),
        C("view.articulationMaps", Loc.T("commands.articulation_maps"), Loc.T("commands.view"), "", "expression map keyswitch articulation manager library"),
        C("view.toggleBrowser", Loc.T("commands.toggle_browser"), Loc.T("commands.view"), "", "library sidebar"),
        C("view.toggleDetail", Loc.T("commands.toggle_detail_panel"), Loc.T("commands.view"), "", "devices clip"),
        C("view.maximizeDetail", Loc.T("commands.clip_editor_fills_the_window"), Loc.T("commands.view"), Shortcut.CmdShift('F'), "maximize maximise full detail panel piano roll restore split"),
        C("view.showDevices", Loc.T("commands.show_devices"), Loc.T("commands.view"), "", "chain detail"),
        C("view.popOutDetail", Loc.T("commands.open_devices_in_a_window"), Loc.T("commands.view"), "", "detach float pop out"),
        C("view.toggleOverview", Loc.T("commands.toggle_overview"), Loc.T("commands.view"), "", "minimap", view: "Arrangement"),
        C("view.toggleSections", Loc.T("commands.toggle_sections"), Loc.T("commands.view"), "", "markers locators", view: "Arrangement"),
        C("edit.resetSectionName", Loc.T("commands.reset_section_name"), Loc.T("common.edit"), "", "default automatic rename section at playhead", view: "Arrangement"),
        C("edit.moveSectionEarlier", Loc.T("commands.move_section_earlier"), Loc.T("common.edit"), "", "rearrange reorder section content left before previous at playhead", view: "Arrangement"),
        C("edit.moveSectionLater", Loc.T("commands.move_section_later"), Loc.T("common.edit"), "", "rearrange reorder section content right after next at playhead", view: "Arrangement"),
        C("edit.duplicateSectionWithContent", Loc.T("commands.duplicate_section_with_content"), Loc.T("common.edit"), "", "copy repeat section clips automation ripple at playhead", view: "Arrangement"),
        C("edit.deleteSectionWithContent", Loc.T("commands.delete_section_with_content"), Loc.T("common.edit"), "", "remove cut section clips automation ripple close gap at playhead", view: "Arrangement"),
        C("view.follow", Loc.T("commands.follow_playhead"), Loc.T("commands.view"), "", "scroll"),
        C("view.showPlayhead", Loc.T("commands.show_playhead"), Loc.T("commands.view"), Shortcut.None('P'), "find locate scroll reveal cursor position", view: "Arrangement"),
        C("view.clipNames", Loc.T("commands.cycle_clip_names"), Loc.T("commands.view"), "", "labels", view: "Arrangement"),
        C("view.headersRight", Loc.T("commands.track_headers_on_the_right"), Loc.T("commands.view"), "", "track list side left right swap mirror ableton", view: "Arrangement"),
        C("view.automation", Loc.T("commands.toggle_automation_mode"), Loc.T("commands.view"), Shortcut.CmdShift('A'), "envelopes lanes", view: "Arrangement"),
        C("view.history", Loc.T("commands.show_version_history"), Loc.T("commands.view"), "", "undo history versions snapshots"),
        C("view.midiLearn", Loc.T("commands.midi_learn"), Loc.T("commands.view"), "", "map controller"),
        C("view.fullscreen", Loc.T("commands.toggle_full_screen"), Loc.T("commands.view"), Shortcut.None("F11"), "maximize immersive hide chrome"),
        C(Palette, Loc.T("commands.command_palette"), Loc.T("commands.view"), Shortcut.CmdShift('P'), "search", hide: true),

        // ---- Transport --------------------------------------------------------------------
        C("transport.playStop", Loc.T("common.play_stop"), Loc.T("common.transport"), Shortcut.None("Space"), "start pause"),
        C("transport.playPause", Loc.T("commands.play_pause"), Loc.T("common.transport"), Shortcut.Shift("Space"), "resume continue hold"),
        C("transport.stop", Loc.T("common.stop"), Loc.T("common.transport"), Shortcut.None("Return"), "halt"),
        C("transport.record", Loc.T("common.record"), Loc.T("common.transport"), Shortcut.Cmd('R'), "arm capture"),
        C("transport.retryTake", Loc.T("commands.discard_and_retry_take"), Loc.T("common.transport"), Shortcut.Cmd('.'), "undo restart retry discard take again redo record over punch"),
        C("transport.armOnSelect", Loc.T("commands.auto_arm_selected_track"), Loc.T("common.transport"), "", "arm on select follow selection record keyboard"),
        C("transport.loop", Loc.T("commands.toggle_loop"), Loc.T("common.transport"), Shortcut.Cmd('L'), "cycle repeat"),
        C("transport.metronome", Loc.T("common.metronome"), Loc.T("common.transport"), Shortcut.Cmd('M'), "click"),
        C("transport.loopRecordMode", Loc.T("commands.loop_record_overdub_replace"), Loc.T("common.transport"), Shortcut.CmdShift('R'), "overdub replace take pass loop record"),
        C("transport.tapTempo", Loc.T("commands.tap_tempo"), Loc.T("common.transport"), "", "bpm beat tap"),

        // ---- Track ------------------------------------------------------------------------
        C("track.newInstrument", Loc.T("commands.new_midi_track"), Loc.T("common.track"), "", "add instrument track"),
        C("track.newAudio", Loc.T("commands.new_audio_track"), Loc.T("common.track"), "", "add"),
        C("track.newReturn", Loc.T("commands.new_return_track"), Loc.T("common.track"), "", "add send bus aux"),
        C("track.group", Loc.T("commands.group_tracks"), Loc.T("common.track"), Shortcut.Cmd('G'), "folder bus", view: "Arrangement"),
        C("track.ungroup", Loc.T("commands.ungroup_tracks"), Loc.T("common.track"), Shortcut.CmdShift('G'), "", view: "Arrangement"),
        C("track.freeze", Loc.T("commands.freeze_track"), Loc.T("common.track"), "", "render bounce cpu"),

        // ---- Session ----------------------------------------------------------------------
        C("session.launchScene", Loc.T("commands.launch_scene"), Loc.T("commands.session"), "", "play row", view: "Session"),
        C("session.stopAll", Loc.T("commands.stop_all_clips"), Loc.T("commands.session"), "", "halt", view: "Session"),
        C("session.insertScene", Loc.T("commands.insert_scene"), Loc.T("commands.session"), Shortcut.Cmd('I'), "add scene row", view: "Session"),

        // ---- Piano roll -------------------------------------------------------------------
        C("roll.quantize", Loc.T("commands.quantize_notes"), Loc.T("common.piano_roll"), "", "quantise grid snap align"),
        C("roll.octaveUp", Loc.T("commands.transpose_notes_up_an_octave"), Loc.T("common.piano_roll"), "", "pitch shift"),
        C("roll.octaveDown", Loc.T("commands.transpose_notes_down_an_octave"), Loc.T("common.piano_roll"), "", "pitch shift"),
        C("roll.removeOutOfScale", Loc.T("commands.remove_out_of_scale_notes"), Loc.T("common.piano_roll"), "", "key fix"),
        C("roll.fitNotes", Loc.T("commands.fit_to_notes"), Loc.T("common.piano_roll"), Shortcut.Shift('F'), "zoom vertical rows height"),
        C("roll.fold", Loc.T("commands.fold_the_piano_roll"), Loc.T("common.piano_roll"), Shortcut.Alt('F').WithShift(), "fold scale rows drum pads names hide keys"),
        C("roll.editSelected", Loc.T("commands.edit_selected_clips_together"), Loc.T("common.piano_roll"), "", "multi clip several open piano roll", view: "Arrangement"),
        C("roll.trackMode", Loc.T("commands.toggle_clip_editor_track_mode"), Loc.T("common.piano_roll"), "", "track mode whole track all clips every clip piano roll", view: "Arrangement"),

        // ---- Grid (the arrangement or the piano roll — whichever was used last) -----------
        C("grid.narrower", Loc.T("commands.narrow_grid"), Loc.T("common.grid"), Shortcut.Cmd('1'), "finer smaller snap"),
        C("grid.wider", Loc.T("commands.widen_grid"), Loc.T("common.grid"), Shortcut.Cmd('2'), "coarser larger snap"),
        C("grid.triplets", Loc.T("commands.toggle_triplet_grid"), Loc.T("common.grid"), Shortcut.Cmd('3'), "triplet 1/8T swing"),
        C("grid.snap", Loc.T("commands.toggle_snap_to_grid"), Loc.T("common.grid"), Shortcut.Cmd('4'), "magnet free"),
        C("grid.adaptive", Loc.T("commands.toggle_adaptive_grid"), Loc.T("common.grid"), Shortcut.Cmd('5'), "auto zoom fixed"),

        // ---- Settings ---------------------------------------------------------------------
        C("prefs.open", Loc.T("common.settings"), Loc.T("common.settings"), Shortcut.Cmd(','), "preferences options"),
        C("prefs.audio", Loc.T("commands.settings_audio"), Loc.T("common.settings"), "", "preferences driver asio sample rate buffer latency cpu"),
        C("prefs.outputs", Loc.T("commands.settings_outputs"), Loc.T("common.settings"), "", "preferences output device speakers headphones test tone"),
        C("prefs.inputs", Loc.T("commands.settings_inputs"), Loc.T("common.settings"), "", "preferences input device channels microphone names stereo pairs"),
        C("prefs.midi", Loc.T("commands.settings_midi"), Loc.T("common.settings"), "", "preferences controller keyboard"),
        C("prefs.gamepads", Loc.T("commands.settings_gamepads"), Loc.T("common.settings"), "", "preferences joystick controller"),
        C("prefs.remote", Loc.T("commands.settings_remote"), Loc.T("common.settings"), "", "preferences phone"),
        C("prefs.mcp", Loc.T("commands.settings_mcp"), Loc.T("common.settings"), "", "preferences ai mcp claude assistant server port"),
        C("prefs.plugins", Loc.T("commands.settings_plug_ins"), Loc.T("common.settings"), "", "preferences vst3 au scan folders"),
        C("prefs.downloads", Loc.T("commands.settings_downloads"), Loc.T("common.settings"), "", "preferences store models sample packs"),
        C("prefs.library", Loc.T("commands.settings_library"), Loc.T("common.settings"), "", "preferences folders"),
        C("prefs.appearance", Loc.T("commands.settings_appearance"), Loc.T("common.settings"), "", "preferences theme dark light skin accent scale text size waveform"),
        C("prefs.language", Loc.T("commands.settings_language"), Loc.T("common.settings"), "", "preferences language langue francais english russian"),
        C("prefs.editing", Loc.T("commands.settings_editing"), Loc.T("common.settings"), "", "preferences piano roll note names labels split fades detail panel"),
        C("prefs.shortcuts", Loc.T("commands.settings_shortcuts"), Loc.T("common.settings"), "", "preferences hotkeys keys keyboard"),
        C("prefs.updates", Loc.T("commands.settings_updates"), Loc.T("common.settings"), "", "preferences update automatic check version"),

        // ---- Help -------------------------------------------------------------------------
        C("help.docs", Loc.T("common.documentation"), Loc.T("commands.help"), "", "manual guide"),
        C("help.whatsNew", Loc.T("common.whats_new"), Loc.T("commands.help"), "", "changelog release notes"),
        C("help.checkUpdates", Loc.T("common.check_for_updates"), Loc.T("commands.help"), "", "update upgrade new version download install"),
        C("help.about", Loc.T("common.about_nota"), Loc.T("commands.help"), "", "version"),
    };

    private static PaletteCommand C(string id, string title, string category, string gesture, string aliases,
        string view = "", bool hide = false) => new()
    {
        Id = id, Title = title, Category = category, Gesture = gesture, View = view, HideInPalette = hide,
        Aliases = aliases.Length == 0 ? [] : aliases.Split(' ', StringSplitOptions.RemoveEmptyEntries),
    };
}
