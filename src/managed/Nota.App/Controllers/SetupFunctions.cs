// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The functions a controller can be set up for (specs/051 FR-030a–c, specs/110): knobs, faders and
// the master fader, transport, track / bank / page / device, the Session's buttons, extra buttons
// and the jog wheel — each with its prompt, its picture and the profile role it becomes. One list
// for both the step-by-step wizard and the mapping editor, so they always ask for the same things.

using System.Collections.Generic;
using Nota.Application.Controllers;
using Nota.Application.Localization;
using Nota.Application.Palette;

namespace Nota.App.Controllers;

internal enum SetupGroup { Knobs, Faders, Transport, Navigation, Session, Extra }

/// <summary>One half of a pair (previous / next, − / +, undo / redo): its button prompt, name and
/// icon, and the role or command it is.</summary>
internal sealed record SetupHalf(string Prompt, string Name, ControlRoleKind Role, string? Command = null, GlyphKind? Icon = null, string? Word = null)
{
    public FunctionKey Key => Role == ControlRoleKind.Command ? FunctionKey.OfCommand(Command!) : FunctionKey.Of(Role);
}

/// <summary>One function: a wizard step, an editor row.</summary>
internal sealed record SetupFunction(SetupGroup Group, ControlRoleKind Role, int Index, string Prompt, string Name,
    PictogramKind Picture, GlyphKind? Icon = null, string? Word = null, string? Command = null,
    SetupHalf? Left = null, SetupHalf? Right = null)
{
    public bool Button => Picture == PictogramKind.Button;
    /// <summary>A pair: an encoder (both halves at once) or two buttons, left half first.</summary>
    public bool IsPair => Left is not null && Right is not null;

    /// <summary>The function (a pair: its right half).</summary>
    public FunctionKey Key => IsPair ? Right!.Key
        : Role == ControlRoleKind.Command ? FunctionKey.OfCommand(Command!) : FunctionKey.Of(Role, Index);

    /// <summary>Every function this one stands for (a pair: both halves).</summary>
    public IEnumerable<FunctionKey> Keys => IsPair ? new[] { Left!.Key, Right!.Key } : new[] { Key };
}

internal static class SetupFunctions
{
    public static List<SetupFunction> Build()
    {
        var steps = new List<SetupFunction>();
        for (int i = 0; i < 8; i++)
            steps.Add(new SetupFunction(SetupGroup.Knobs, ControlRoleKind.Knob, i, Loc.F("controllers.setup.knob", i + 1), ControllerService.KnobLabel(i), PictogramKind.Knob));
        for (int i = 0; i < 8; i++)
            steps.Add(new SetupFunction(SetupGroup.Faders, ControlRoleKind.Fader, i, Loc.F("controllers.setup.fader", i + 1), Loc.F("controllers.control.fader", i + 1), PictogramKind.Fader));
        steps.Add(new SetupFunction(SetupGroup.Faders, ControlRoleKind.MasterFader, 0, Loc.T("controllers.setup.master"), Loc.T("controllers.control.master_fader"), PictogramKind.Fader));

        void Btn(SetupGroup g, ControlRoleKind role, string prompt, string name, GlyphKind? icon = null, string? word = null, string? command = null)
            => steps.Add(new SetupFunction(g, role, 0, prompt, name, PictogramKind.Button, icon, word, command));
        Btn(SetupGroup.Transport, ControlRoleKind.Play, Loc.T("controllers.setup.play"), Loc.T("controllers.control.play"), GlyphKind.Play);
        Btn(SetupGroup.Transport, ControlRoleKind.Stop, Loc.T("controllers.setup.stop"), Loc.T("controllers.control.stop"), GlyphKind.Stop);
        Btn(SetupGroup.Transport, ControlRoleKind.Record, Loc.T("controllers.setup.record"), Loc.T("controllers.control.record"), GlyphKind.Record);
        Btn(SetupGroup.Transport, ControlRoleKind.Loop, Loc.T("controllers.setup.loop"), Loc.T("controllers.control.loop"), GlyphKind.Loop);
        void Pair(SetupGroup g, string prompt, string name, SetupHalf left, SetupHalf right)
            => steps.Add(new SetupFunction(g, right.Role, 0, prompt, name, PictogramKind.Pair, Command: right.Command, Left: left, Right: right));
        Pair(SetupGroup.Transport, Loc.T("controllers.setup.pair_wind"), Loc.T("controllers.setup.pair_wind_name"),
            new SetupHalf(Loc.T("controllers.setup.rewind"), Loc.T("controllers.control.rewind"), ControlRoleKind.Rewind, Icon: GlyphKind.StepLeft),
            new SetupHalf(Loc.T("controllers.setup.forward"), Loc.T("controllers.control.forward"), ControlRoleKind.Forward, Icon: GlyphKind.StepRight));

        Pair(SetupGroup.Navigation, Loc.T("controllers.setup.pair_tracks"), Loc.T("controllers.setup.pair_tracks_name"),
            new SetupHalf(Loc.T("controllers.setup.track_prev"), Loc.T("controllers.control.track_prev"), ControlRoleKind.TrackPrev, Icon: GlyphKind.ChevronLeft),
            new SetupHalf(Loc.T("controllers.setup.track_next"), Loc.T("controllers.control.track_next"), ControlRoleKind.TrackNext, Icon: GlyphKind.ChevronRight));
        Pair(SetupGroup.Navigation, Loc.T("controllers.setup.pair_banks"), Loc.T("controllers.setup.pair_banks_name"),
            new SetupHalf(Loc.T("controllers.setup.bank_prev"), Loc.T("controllers.control.bank_prev"), ControlRoleKind.BankPrev, Word: Loc.T("controllers.setup.bank_prev_word")),
            new SetupHalf(Loc.T("controllers.setup.bank_next"), Loc.T("controllers.control.bank_next"), ControlRoleKind.BankNext, Word: Loc.T("controllers.setup.bank_next_word")));
        Pair(SetupGroup.Navigation, Loc.T("controllers.setup.pair_pages"), Loc.T("controllers.setup.pair_pages_name"),
            new SetupHalf(Loc.T("controllers.setup.page_prev"), Loc.T("controllers.control.page_prev"), ControlRoleKind.PagePrev, Word: Loc.T("controllers.setup.page_prev_word")),
            new SetupHalf(Loc.T("controllers.setup.page_next"), Loc.T("controllers.control.page_next"), ControlRoleKind.PageNext, Word: Loc.T("controllers.setup.page_next_word")));
        // specs/107: step the knobs along the track's chain.
        Pair(SetupGroup.Navigation, Loc.T("controllers.setup.pair_devices"), Loc.T("controllers.setup.pair_devices_name"),
            new SetupHalf(Loc.T("controllers.setup.device_prev"), Loc.T("commands.device_focus_previous"), ControlRoleKind.Command, "device.focusPrevious", Word: Loc.T("controllers.setup.device_prev_word")),
            new SetupHalf(Loc.T("controllers.setup.device_next"), Loc.T("commands.device_focus_next"), ControlRoleKind.Command, "device.focusNext", Word: Loc.T("controllers.setup.device_next_word")));

        void Cmd(SetupGroup g, string id, string prompt, string name, GlyphKind? icon = null, string? word = null)
            => Btn(g, ControlRoleKind.Command, prompt, name, icon, word, id);
        Pair(SetupGroup.Session, Loc.T("controllers.setup.pair_clips"), Loc.T("controllers.setup.pair_clips_name"),
            new SetupHalf(Loc.T("controllers.setup.prev_scene"), Loc.T("commands.session_select_prev_scene"), ControlRoleKind.Command, "session.selectUp", GlyphKind.ChevronUp),
            new SetupHalf(Loc.T("controllers.setup.next_scene"), Loc.T("commands.session_select_next_scene"), ControlRoleKind.Command, "session.selectDown", GlyphKind.ChevronDown));
        Pair(SetupGroup.Session, Loc.T("controllers.setup.pair_session_tracks"), Loc.T("controllers.setup.pair_session_tracks_name"),
            new SetupHalf(Loc.T("controllers.setup.prev_track"), Loc.T("commands.session_select_prev_track"), ControlRoleKind.Command, "session.selectLeft", GlyphKind.ChevronLeft),
            new SetupHalf(Loc.T("controllers.setup.next_track"), Loc.T("commands.session_select_next_track"), ControlRoleKind.Command, "session.selectRight", GlyphKind.ChevronRight));
        Cmd(SetupGroup.Session, "session.toggleSlot", Loc.T("controllers.setup.slot_launch"), Loc.T("commands.session_toggle_slot"), GlyphKind.Play);
        Cmd(SetupGroup.Session, "session.stopTrack", Loc.T("controllers.setup.slot_stop"), Loc.T("commands.session_stop_track"), GlyphKind.Stop);
        Cmd(SetupGroup.Session, "session.launchScene", Loc.T("controllers.setup.scene_launch"), Loc.T("commands.launch_scene"), GlyphKind.Session);
        Cmd(SetupGroup.Session, "session.stopAll", Loc.T("controllers.setup.stop_all"), Loc.T("commands.stop_all_clips"), word: Loc.T("controllers.setup.stop_all_word"));

        Pair(SetupGroup.Extra, Loc.T("controllers.setup.pair_undo"), Loc.T("controllers.setup.pair_undo_name"),
            new SetupHalf(Loc.T("controllers.setup.undo"), Loc.T("common.undo"), ControlRoleKind.Command, "edit.undo", Word: Loc.T("common.undo")),
            new SetupHalf(Loc.T("controllers.setup.redo"), Loc.T("commands.redo"), ControlRoleKind.Command, "edit.redo", Word: Loc.T("commands.redo")));
        Pair(SetupGroup.Extra, Loc.T("controllers.setup.pair_tempo"), Loc.T("controllers.setup.pair_tempo_name"),
            new SetupHalf(Loc.T("controllers.setup.tempo_down"), Loc.T("commands.tempo_down"), ControlRoleKind.Command, "transport.tempoDown", Word: Loc.T("controllers.setup.tempo_down_word")),
            new SetupHalf(Loc.T("controllers.setup.tempo_up"), Loc.T("commands.tempo_up"), ControlRoleKind.Command, "transport.tempoUp", Word: Loc.T("controllers.setup.tempo_up_word")));
        Cmd(SetupGroup.Extra, "transport.metronome", Loc.T("controllers.setup.metronome"), Loc.T("controllers.setup.metronome_name"), GlyphKind.Metronome);
        Cmd(SetupGroup.Extra, "file.save", Loc.T("controllers.setup.save"), Loc.T("commands.save_project"), GlyphKind.Save);
        Cmd(SetupGroup.Extra, "transport.tapTempo", Loc.T("controllers.setup.tap"), Loc.T("controllers.setup.tap_name"), word: Loc.T("controllers.setup.tap_name"));
        steps.Add(new SetupFunction(SetupGroup.Extra, ControlRoleKind.Jog, 0, Loc.T("controllers.setup.jog"), Loc.T("controllers.control.jog"), PictogramKind.Jog));
        return steps;
    }

    /// <summary>A row for any palette command put on a spare button (specs/110 FR-002a).</summary>
    public static SetupFunction ForCommand(string id)
        => new(SetupGroup.Extra, ControlRoleKind.Command, 0, Loc.T("controllers.setup.command_prompt"), CommandTitle(id), PictogramKind.Button,
            Word: CommandTitle(id), Command: id);

    private static Dictionary<string, PaletteCommand>? _commands;

    /// <summary>The palette's commands by id.</summary>
    public static IReadOnlyDictionary<string, PaletteCommand> Commands
    {
        get
        {
            if (_commands is null)
            {
                _commands = new Dictionary<string, PaletteCommand>(System.StringComparer.Ordinal);
                foreach (var c in CommandCatalog.Create()) _commands.TryAdd(c.Id, c);
            }
            return _commands;
        }
    }

    public static string CommandTitle(string id) => Commands.TryGetValue(id, out var c) ? c.Title : Loc.F("controllers.control.command", id);

    /// <summary>The group's full name (the wizard's "skip all …").</summary>
    public static string GroupName(SetupGroup g) => g switch
    {
        SetupGroup.Knobs => Loc.T("controllers.setup.group_knobs"),
        SetupGroup.Faders => Loc.T("controllers.setup.group_faders"),
        SetupGroup.Transport => Loc.T("controllers.setup.group_transport"),
        SetupGroup.Navigation => Loc.T("controllers.setup.group_navigation"),
        SetupGroup.Session => Loc.T("controllers.setup.group_session"),
        _ => Loc.T("controllers.setup.group_extra"),
    };

    /// <summary>The strip's short names.</summary>
    public static string GroupTitle(SetupGroup g) => g switch
    {
        SetupGroup.Knobs => Loc.T("controllers.setup.tab_knobs"),
        SetupGroup.Faders => Loc.T("controllers.setup.tab_faders"),
        SetupGroup.Transport => Loc.T("controllers.setup.tab_transport"),
        SetupGroup.Navigation => Loc.T("controllers.setup.tab_navigation"),
        SetupGroup.Session => Loc.T("controllers.setup.tab_session"),
        _ => Loc.T("controllers.setup.tab_extra"),
    };
}
