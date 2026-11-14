// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// What Preferences → Shortcuts lists, in order (specs/096): one group per category of the
// key-action catalog — so every action appears exactly once and a new one can't be forgotten —
// with the fixed rows beside them: mouse gestures, list-navigation and text keys, the gamepad
// (FR-004, FR-011). Fixed keys are written with the macOS glyphs; the page spells them.

using System;
using System.Collections.Generic;
using System.Linq;
using Nota.Application.Localization;

namespace Nota.Application.Input;

/// <summary>A row: an editable action (<see cref="ActionId"/>), or a fixed key / gesture.</summary>
public sealed record ShortcutRow(string? ActionId, string Key = "", string Text = "")
{
    public bool IsFixed => ActionId is null;
}

public sealed record ShortcutGroup(string Title, IReadOnlyList<ShortcutRow> Rows);

public static class ShortcutLayout
{
    public static List<ShortcutGroup> Build(IReadOnlyList<KeyAction> actions, bool menuKey)
    {
        // Category order: the menus first, then the views that handle their own keys.
        string file = Loc.T("common.file"), edit = Loc.T("common.edit"), view = Loc.T("commands.view"),
            transport = Loc.T("common.transport"), track = Loc.T("common.track"), arrangement = Loc.T("keys.group.arrangement"),
            grid = Loc.T("common.grid"), session = Loc.T("commands.session"), roll = Loc.T("common.piano_roll"),
            devices = Loc.T("keys.group.devices"), modular = Loc.T("keys.group.modular"), browser = Loc.T("keys.group.browser"),
            notes = Loc.T("keys.group.notes"), settings = Loc.T("common.settings"), help = Loc.T("commands.help");
        string[] order = [file, edit, view, transport, track, arrangement, grid, session, roll, devices, modular, browser, notes, settings, help];

        var fixedRows = new Dictionary<string, List<ShortcutRow>>(StringComparer.Ordinal)
        {
            [view] =
            [
                F(Loc.T("preferences.double_click_a_panel_edge"), Loc.T("preferences.reset_the_detail_panel_the")),
            ],
            [transport] =
            [
                F(Loc.T("preferences.double_click_loop_brace"), Loc.T("preferences.loop_on_off_in_the")),
                F(Loc.T("preferences.drag_loop_edge_drag_loop"), Loc.T("preferences.resize_that_end_of_the")),
                F(Loc.T("preferences.right_click_ruler"), Loc.T("preferences.loop_on_off_loop_the_2")),
                F(Loc.T("preferences.click_the_position_read_out"), Loc.T("preferences.switch_it_between_bars_and")),
            ],
            [arrangement] =
            [
                F(Loc.T("preferences.drag_below_the_last_track"), Loc.T("preferences.move_the_clips_to_a")),
                F("⇧-click   ⌘-click", Loc.T("preferences.select_a_range_of_tracks")),
            ],
            [grid] =
            [
                F(Loc.T("preferences.alt_while_dragging"), Loc.T("preferences.move_freely_ignoring_the_grid")),
                F(Loc.T("preferences.audio_clip_editor_on_adsr"), Loc.T("preferences.cmd_1_5_act_on_the_arrangement")),
            ],
            [session] =
            [
                F("← → ↑ ↓", Loc.T("preferences.move_the_selection_between_slots")),
                F(Loc.T("preferences.drag_clip"), Loc.T("preferences.move_it_to_another_slot")),
                F(Loc.T("preferences.drag_scene"), Loc.T("preferences.reorder_scenes")),
                F(Loc.T("preferences.double_click_slot"), Loc.T("preferences.edit_the_clip_an_empty")),
                F(Loc.T("preferences.double_click_header"), Loc.T("preferences.rename_the_track")),
                F(Loc.T("preferences.right_click_header_or_grid"), Loc.T("preferences.add_an_instrument_audio_or")),
            ],
            [roll] =
            [
                F(Loc.T("preferences.wheel_over_a_note"), Loc.T("preferences.change_its_velocity_the_whole")),
                F(Loc.T("preferences.double_click_empty_grid"), Loc.T("preferences.add_a_note_with_the")),
                F(Loc.T("preferences.double_click_and_hold_drag"), Loc.T("preferences.shape_the_new_note_move")),
                F(Loc.T("preferences.drag_note_edge"), Loc.T("preferences.change_the_notes_start_or")),
                F(Loc.T("preferences.click_drag_the_ruler"), Loc.T("preferences.move_the_playhead_there_drag_2")),
                F(Loc.T("preferences.drag_the_playhead_line"), Loc.T("preferences.scrub_the_playhead_on_the")),
                F(Loc.T("preferences.drag_velocity_stem"), Loc.T("preferences.set_velocity_selected_notes_move")),
                F(Loc.T("preferences.double_click_velocity_stem"), Loc.T("preferences.put_the_note_back_to")),
                F(Loc.T("preferences.right_click_velocity_stem"), Loc.T("preferences.reset_velocity_from_a_menu")),
                F(Loc.T("preferences.drag_in_the_bend_pres"), Loc.T("preferences.draw_the_selected_notes_mpe")),
                F(Loc.T("preferences.double_click_in_an_mpe"), Loc.T("preferences.add_a_point_on_a")),
                F(Loc.T("preferences.click_the_bend_lanes_range"), Loc.T("preferences.pick_how_far_the_bend")),
                F(Loc.T("preferences.click_the_controller_tab"), Loc.T("preferences.show_primary_controller_lane")),
                F(Loc.T("preferences.click_lane_controller_dropdown"), Loc.T("preferences.choose_lane_controller")),
                F(Loc.T("preferences.controller_lane_buttons"), Loc.T("preferences.manage_controller_lanes")),
                F(Loc.T("preferences.drag_in_the_sustain_lane"), Loc.T("preferences.draw_a_pedal_block_on")),
                F(Loc.T("preferences.drag_in_a_controller_lane"), Loc.T("preferences.draw_the_controllers_curve_draws")),
                F(Loc.T("preferences.drag_lane_divider"), Loc.T("preferences.resize_lane_remember_height")),
                F(Loc.T("preferences.click_a_clip_in_the"), Loc.T("preferences.make_it_the_active_clip")),
                F(Loc.T("preferences.cmd_alt_wheel"), Loc.T("preferences.zoom_rows_vertical_zoom")),
                F(Loc.T("preferences.click_a_key"), Loc.T("preferences.play_it_on_the_tracks")),
                F(Loc.T("preferences.drag_on_the_keys"), Loc.T("preferences.zoom_rows_down_taller_up")),
                F("⌥ + pinch", Loc.T("preferences.zoom_rows_trackpad")),
            ],
            [devices] =
            [
                F("⇧-click   ⌘-click", Loc.T("preferences.select_a_range_of_devices")),
                F(Loc.T("preferences.drag_a_selected_devices_header"), Loc.T("preferences.move_the_selected_devices_of")),
                F(Loc.T("preferences.drag_a_device_onto_a"), Loc.T("preferences.move_it_there_out_of")),
                F(Loc.T("preferences.drag_a_device_onto_a_2"), Loc.T("preferences.copy_it_there")),
            ],
            [modular] =
            [
                F(Loc.T("keys.fixed.space_drag"), Loc.T("keys.fixed.pan_the_canvas")),
            ],
            [browser] =
            [
                F("Return", Loc.T("preferences.open_the_selected_folder_browse")),
            ],
        };
        if (menuKey) fixedRows[view].Insert(0, F("⌥", Loc.T("keys.fixed.alt_tap_opens_the_menu")));

        var groups = new List<ShortcutGroup>();
        var byCategory = actions.GroupBy(a => a.Category).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        foreach (var cat in order.Concat(byCategory.Keys.Where(k => !order.Contains(k))).Distinct())
        {
            var rows = new List<ShortcutRow>();
            if (byCategory.TryGetValue(cat, out var list)) rows.AddRange(list.Select(a => new ShortcutRow(a.Id)));
            if (fixedRows.TryGetValue(cat, out var extra)) rows.AddRange(extra);
            if (rows.Count > 0) groups.Add(new ShortcutGroup(cat, rows));
        }

        groups.Add(new(Loc.T("tempo.shortcuts"),
        [
            F(Loc.T("tempo.shortcut_add"), Loc.T("tempo.shortcut_add_action")),
            F(Loc.T("tempo.shortcut_shape"), Loc.T("tempo.shortcut_shape_action")),
            F(Loc.T("tempo.shortcut_select"), Loc.T("tempo.shortcut_selection")),
            F(Loc.T("tempo.shortcut_map"), Loc.T("tempo.shortcut_map_action")),
            F(Loc.T("tempo.shortcut_map_alt"), Loc.T("tempo.shortcut_map_alt_action")),
        ]));

        // Fixed only: keys of lists and dialogs, and what the mouse, trackpad and gamepad do.
        groups.Add(new(Loc.T("preferences.command_palette"),
        [
            F("↑ ↓   PgUp PgDn", Loc.T("preferences.move_through_the_results")),
            F("Return", Loc.T("preferences.apply_where_the_palette_was")),
            F("⌘Return", Loc.T("preferences.an_instrument_or_instrument_preset")),
            F("⌥Return", Loc.T("preferences.replace_the_selected_device_instead")),
            F("⇧Return", Loc.T("preferences.apply_and_keep_the_palette")),
            F("Tab   ⇧Tab", Loc.T("preferences.filter_all_actions_tracks_devices")),
            F("> @ + # ~", Loc.T("preferences.type_first_to_filter_actions")),
            F("Esc", Loc.T("preferences.close_and_go_back_where")),
        ]));
        groups.Add(new(Loc.T("preferences.racks"),
        [
            F(Loc.T("preferences.drag_a_zone_edge"), Loc.T("preferences.set_a_chains_key_velocity")),
            F(Loc.T("preferences.hover_the_zones"), Loc.T("preferences.see_which_chains_a_note")),
            F(Loc.T("preferences.double_click_a_chain_or"), Loc.T("preferences.rename_it")),
            F(Loc.T("preferences.right_click_a_macro"), Loc.T("preferences.map_it_to_a_parameter")),
            F(Loc.T("preferences.drag_a_devices_header"), Loc.T("preferences.move_it_within_its_chain")),
            F(Loc.T("preferences.click_a_device"), Loc.T("preferences.select_it_for_c_x")),
        ]));
        groups.Add(new(Loc.T("preferences.audio_clip_editor"),
        [
            F(Loc.T("preferences.click_drag_the_ruler"), Loc.T("preferences.move_the_playhead_there_drag")),
            F(Loc.T("preferences.drag_the_playhead_line"), Loc.T("preferences.scrub_the_playhead")),
            F(Loc.T("preferences.double_click_the_wave"), Loc.T("preferences.add_a_warp_marker_on")),
            F(Loc.T("preferences.drag_a_warp_marker"), Loc.T("preferences.align_the_audio_to_the")),
            F(Loc.T("preferences.drag_double_click_tempo"), Loc.T("preferences.set_the_tempo_the_clip")),
        ]));
        groups.Add(new(Loc.T("preferences.browser_preview"),
        [
            F("↑ ↓", Loc.T("preferences.move_through_samples_presets_and")),
            F(Loc.T("preferences.click_play_button"), Loc.T("preferences.play_stop_the_selected_sample")),
            F(Loc.T("preferences.click_waveform"), Loc.T("preferences.play_from_that_point")),
            F(Loc.T("preferences.click_wave_spec"), Loc.T("preferences.switch_the_well_between_the")),
            F(Loc.T("preferences.scroll_on_the_track_button"), Loc.T("preferences.step_through_the_demo_tracks")),
            F(Loc.T("preferences.alt_click_volume"), Loc.T("preferences.type_the_preview_volume_double")),
        ]));
        groups.Add(new(Loc.T("preferences.gamepad_macos"),
        [
            F(Loc.T("preferences.face_buttons"), Loc.T("preferences.play_c4_d4_e4_f4")),
            F(Loc.T("preferences.shoulder_buttons"), Loc.T("preferences.play_g4_a4_b4_c5")),
            F("D-pad ↑ / ↓", Loc.T("preferences.shift_the_gamepad_octave")),
            F("D-pad ← / →", Loc.T("preferences.lower_raise_velocity")),
            F(Loc.T("preferences.any_button"), Loc.T("preferences.mappable_through_midi_learn_a")),
            F(Loc.T("preferences.sticks_triggers"), Loc.T("preferences.mappable_as_continuous_controls_a")),
        ]));
        groups.Add(new(Loc.T("preferences.mouse"), MouseRows()));
        return groups;
    }

    private static ShortcutRow F(string key, string text) => new(null, key, text);

    private static List<ShortcutRow> MouseRows() =>
    [
        F(Loc.T("preferences.double_click_clip"), Loc.T("preferences.open_in_the_clip_editor")),
        F(Loc.T("preferences.double_click_a_track_header"), Loc.T("preferences.open_its_devices_in_the")),
        F(Loc.T("preferences.double_click_a_browser_instrument"), Loc.T("preferences.swap_it_onto_the_selected")),
        F(Loc.T("preferences.alt_double_click_a_browser"), Loc.T("preferences.always_start_a_new_track")),
        F(Loc.T("preferences.right_click_the_metronome"), Loc.T("preferences.count_in_before_play_record")),
        F(Loc.T("preferences.drag_an_audio_clips_adsr"), Loc.T("preferences.shape_attack_decay_sustain_release")),
        F(Loc.T("preferences.click_track_header"), Loc.T("preferences.add_the_track_to_the")),
        F(Loc.T("preferences.click_a_groups_link_key"), Loc.T("preferences.link_the_groups_tracks_arming")),
        F(Loc.T("preferences.click_a_clip_or_arm"), Loc.T("preferences.in_a_linked_group_select")),
        F(Loc.T("preferences.right_click_track_headers"), Loc.T("preferences.a_multi_selection_gets_its")),
        F(Loc.T("preferences.drag_a_tracks_bottom_edge"), Loc.T("preferences.resize_the_track_header_or")),
        F(Loc.T("preferences.drag_a_tracks_bottom_edge_2"), Loc.T("preferences.resize_every_track_at_once")),
        F(Loc.T("preferences.double_click_a_tracks_bottom"), Loc.T("preferences.back_to_the_standard_height")),
        F(Loc.T("preferences.drag_a_returns_or_the"), Loc.T("preferences.resize_that_row_header_or")),
        F("⌘-click / ⇧-click return headers", Loc.T("preferences.select_several_returns_they_resize")),
        F(Loc.T("preferences.click_a_height_key_in"), Loc.T("preferences.set_every_track_to_compact")),
        F(Loc.T("preferences.drag_the_track_list_edge"), Loc.T("preferences.widen_or_narrow_the_track")),
        F(Loc.T("preferences.drag_sections_lane"), Loc.T("preferences.mark_a_new_section_over")),
        F(Loc.T("preferences.click_section"), Loc.T("preferences.jump_the_playhead_to_its")),
        F(Loc.T("preferences.double_click_section"), Loc.T("preferences.rename_it_right_click_reset")),
        F(Loc.T("preferences.return_esc_renaming_a_section"), Loc.T("preferences.keep_the_name_empty_automatic")),
        F(Loc.T("preferences.drag_section"), Loc.T("preferences.rearrange_the_song_it_and")),
        F(Loc.T("preferences.drag_section_2"), Loc.T("preferences.move_only_the_section_marker")),
        F(Loc.T("preferences.drag_section_edge"), Loc.T("preferences.resize_it_snapped_to_bars")),
        F(Loc.T("preferences.right_click_section"), Loc.T("preferences.move_earlier_later_duplicate_or")),
        F(Loc.T("preferences.drag_overview_strip"), Loc.T("preferences.drag_sideways_to_scroll_up")),
        F(Loc.T("preferences.drag_overview_edge"), Loc.T("preferences.zoom_by_resizing_the_viewport")),
        F(Loc.T("preferences.double_click_overview"), Loc.T("preferences.fit_the_whole_project_on")),
        F(Loc.T("preferences.drag_a_device_header"), Loc.T("preferences.reorder_devices_or_right_click")),
        F(Loc.T("preferences.right_click_a_device_header"), Loc.T("preferences.a_b_compare_move_copy")),
        F(Loc.T("preferences.delete_device_selected"), Loc.T("preferences.remove_the_selected_device_the")),
        F(Loc.T("preferences.drag_an_audio_clip_onto"), Loc.T("preferences.load_its_audio_into_the")),
        F(Loc.T("preferences.drag_an_audio_clip_out"), Loc.T("preferences.take_it_as_a_sample")),
        F(Loc.T("preferences.drag_clip_2"), Loc.T("preferences.position_freely_ignoring_the_grid")),
        F(Loc.T("preferences.wheel_over_the_device_chain"), Loc.T("preferences.scroll_it_sideways_over_an")),
        F(Loc.T("preferences.drag_knob"), Loc.T("preferences.change_a_device_value_hold")),
        F(Loc.T("preferences.drag_slider"), Loc.T("preferences.left_right_or_up_down")),
        F(Loc.T("preferences.click_a_knob_fader_or"), Loc.T("preferences.type_a_value_units_like")),
        F(Loc.T("preferences.return_esc_tab_tab_typing"), Loc.T("preferences.set_it_cancel_set_it")),
        F(Loc.T("preferences.click_a_graph_handle_envelope"), Loc.T("preferences.type_its_values_a_node")),
        F(Loc.T("preferences.double_click_a_knob_fader"), Loc.T("preferences.reset_the_value_to_its")),
        F(Loc.T("preferences.double_click_a_mod_matrix"), Loc.T("preferences.clear_the_route_volt_aurora")),
        F(Loc.T("preferences.right_click_a_knob_fader"), Loc.T("preferences.type_value_reset_to_default")),
        F(Loc.T("preferences.drag_jack_jack"), Loc.T("preferences.patch_a_cable_in_nota")),
        F(Loc.T("preferences.click_a_rhythm_step"), Loc.T("preferences.step_on_off_accent_quiet")),
        F(Loc.T("preferences.click_a_rhythm_voice"), Loc.T("preferences.select_and_play_it_drop")),
    ];
}
