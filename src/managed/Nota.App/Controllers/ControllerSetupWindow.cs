// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Controller setup (specs/051 FR-030, T075, T098): for a controller Nota does not recognise, walk
// through the roles group by group — knobs, faders and the master fader, transport, track / bank /
// page buttons, Session clip buttons, extra buttons (undo, redo, metronome, save, tap) and the jog
// wheel — and learn each from one movement: turn it, and the next step comes; press it, and the next
// step comes. A picture shows which kind of control each step wants. The port is claimed whole
// while the window is open, so nothing plays or fires; what arrives is shown as it comes, so a
// silent control is obvious. Each step's messages are classified (note / CC / 14-bit / NRPN /
// pitch bend, and an encoder's relative encoding from "turn right, then left"); only when the
// reading is ambiguous does the window stop and ask. A control already learned is ignored (a
// jittery knob can't take the next step) and moving it says so. Every step can be skipped, a whole
// group too ("no faders"), and Back redoes the step before. Finish writes a profile into the user
// profiles folder and reloads profiles, so the controller works at once.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Nota.Application.Controllers;
using Nota.Application.Localization;

namespace Nota.App.Controllers;

public sealed class ControllerSetupWindow : NotaWindow
{
    private enum Group { Knobs, Faders, Transport, Navigation, Session, Extra }

    // One half of a pair (previous / next, − / +, undo / redo): its button prompt, name and icon,
    // and the role or command it is.
    private sealed record Half(string Prompt, string Name, ControlRoleKind Role, string? Command = null, GlyphKind? Icon = null, string? Word = null);

    private sealed record Step(Group Group, ControlRoleKind Role, int Index, string Prompt, string Name,
        PictogramKind Picture, GlyphKind? Icon = null, string? Word = null, string? Command = null,
        Half? Left = null, Half? Right = null)
    {
        public bool Button => Picture == PictogramKind.Button;
        /// <summary>A pair: an encoder (both halves at once) or two buttons, left half first.</summary>
        public bool IsPair => Left is not null && Right is not null;
    }

    private const long KnobStillMs = 600, StillMs = 350, AdvanceMs = 550;

    private readonly ControllerService _service;
    private readonly ConnectedController _port;
    private readonly List<Step> _steps = new();
    // By (step, half): half 0 is the step's control (a pair's encoder, or its left button), 1 a
    // pair's right button.
    private readonly Dictionary<(int Step, int Half), LearnedControl> _learned = new();
    private int _half;   // a pair step waiting for its right button
    private readonly List<(int Status, int Channel, int D1, int D2)> _events = new();
    private readonly DispatcherTimer _poll;
    private long _lastEventMs;
    private long _advanceAt;   // > 0: the step was learned, the next one comes at this time
    private int _step;
    private ClassifiedControl? _found;

    private readonly ControlPictogram _picture = new() { Width = 96, Height = 96, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _counter = new() { Classes = { "SectionLabel" } };
    private readonly TextBlock _prompt = new() { FontSize = 15, FontWeight = Avalonia.Media.FontWeight.SemiBold, Foreground = NotaPalette.TextPrimary, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBlock _hearing = new() { Classes = { "Mono" }, FontSize = 11, Foreground = NotaPalette.TextSecondary, VerticalAlignment = VerticalAlignment.Center };
    private readonly Avalonia.Controls.Shapes.Ellipse _activity = new() { Width = 6, Height = 6, Fill = NotaPalette.TextDisabled, VerticalAlignment = VerticalAlignment.Center };
    private long _activityMs;
    private readonly TextBlock _detected = new() { FontSize = 12, Foreground = NotaPalette.TextSecondary, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly WrapPanel _groups = new() { Orientation = Orientation.Horizontal };
    private readonly WrapPanel _learnedChips = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock _learnedLabel = new() { Classes = { "SectionLabel" } };
    private readonly Border _progressFill = new() { Height = 2, CornerRadius = NotaRadius.Fine, Background = NotaPalette.TextSecondary, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Border _progressTrack = new() { Height = 2, CornerRadius = NotaRadius.Fine, Background = NotaPalette.SurfaceInset };
    private readonly ComboBox _encoder = new() { IsVisible = false, MinWidth = 220 };
    private readonly Button _next = new() { Classes = { "primary" }, IsVisible = false };
    private readonly Button _skipGroup = new() { Classes = { "ghost" } };
    private readonly Button _back = new() { Classes = { "ghost" } };

    public ControllerSetupWindow(ControllerService service, ConnectedController port)
    {
        _service = service;
        _port = port;
        Title = Loc.F("controllers.setup.title", port.Name);
        Width = 600;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = NotaPalette.BgApp;

        BuildSteps();

        foreach (var key in new[] { "controllers.setup.enc_absolute", "controllers.setup.enc_twos", "controllers.setup.enc_signbit", "controllers.setup.enc_offset" })
            _encoder.Items.Add(EncoderName(key));
        _encoder.SelectedIndex = 0;

        _next.Content = Loc.T("controllers.setup.next");
        _next.Click += (_, _) => Accept();
        var skip = new Button { Content = Loc.T("controllers.setup.skip"), Classes = { "ghost" } };
        skip.Click += (_, _) => Advance();
        _skipGroup.Click += (_, _) => SkipGroup();
        _back.Content = Loc.T("controllers.setup.back");
        _back.Click += (_, _) => Back();
        var again = new Button { Content = Loc.T("controllers.setup.again"), Classes = { "ghost" } };
        again.Click += (_, _) => ResetStep();
        var finish = new Button { Content = Loc.T("controllers.setup.finish"), Classes = { "ghost" } };
        finish.Click += (_, _) => Finish();
        var cancel = new Button { Content = Loc.T("common.cancel"), Classes = { "ghost" } };
        cancel.Click += (_, _) => Close();

        finish.Classes.Clear(); finish.Classes.Add("primary");   // the dialog's action: weight, not colour

        // The step: the picture in a round well on the left; what to do, what arrives and what
        // was read on the right; the step's own buttons under it.
        var dial = new Panel
        {
            Width = 120, Height = 120, VerticalAlignment = VerticalAlignment.Top,
            Children = { new Avalonia.Controls.Shapes.Ellipse { Fill = NotaPalette.BgSunken }, _picture },
        };
        var hearing = new Border
        {
            Background = NotaPalette.BgSunken, CornerRadius = NotaRadius.Field, Padding = new Thickness(8, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { _activity, _hearing } },
        };
        var text = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { _counter, _prompt, hearing, _detected, _encoder } };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 20, MinHeight = 120 };
        Grid.SetColumn(text, 1);
        row.Children.Add(dial); row.Children.Add(text);
        var stepButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
        stepButtons.Children.Add(_back); stepButtons.Children.Add(again); stepButtons.Children.Add(_skipGroup);
        stepButtons.Children.Add(skip); stepButtons.Children.Add(_next);
        var stage = new Border
        {
            Background = NotaPalette.Well, CornerRadius = NotaRadius.Card, Padding = new Thickness(20, 18, 16, 12),
            Child = new StackPanel { Spacing = 14, Children = { row, stepButtons } },
        };

        // Where you are: the groups (done ticked, the current one picked, click one to go there)
        // and a thin line for the whole walk.
        _groups.ItemSpacing = 6;
        _groups.LineSpacing = 6;
        var progress = new Panel { Children = { _progressTrack, _progressFill } };
        _progressTrack.SizeChanged += (_, _) => ShowProgress();

        _learnedChips.ItemSpacing = 6;
        _learnedChips.LineSpacing = 6;

        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        Grid.SetColumn(finish, 2);
        footer.Children.Add(cancel); footer.Children.Add(finish);

        SetBody(new StackPanel
        {
            Children =
            {
                new StackPanel
                {
                    Margin = new Thickness(20, 16, 20, 16), Spacing = 14,
                    Children =
                    {
                        new TextBlock { Text = Loc.T("controllers.setup.caption"), Classes = { "Caption" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                        _groups, progress, stage,
                        new StackPanel { Spacing = 6, Children = { _learnedLabel, _learnedChips } },
                    },
                },
                new Border
                {
                    BorderBrush = NotaPalette.Seam, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(20, 10),
                    Child = footer,
                },
            },
        });

        _service.Capture(port.Uid);
        _service.TakeCaptured();
        _poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _poll.Tick += (_, _) => Poll();
        _poll.Start();
        Closed += (_, _) => { _poll.Stop(); _service.Capture(null); };
        ShowStep();
    }

    private static string EncoderName(string key) => key switch
    {
        "controllers.setup.enc_twos" => Loc.T("controllers.setup.enc_twos"),
        "controllers.setup.enc_signbit" => Loc.T("controllers.setup.enc_signbit"),
        "controllers.setup.enc_offset" => Loc.T("controllers.setup.enc_offset"),
        _ => Loc.T("controllers.setup.enc_absolute"),
    };

    private void BuildSteps()
    {
        for (int i = 0; i < 8; i++)
            _steps.Add(new Step(Group.Knobs, ControlRoleKind.Knob, i, Loc.F("controllers.setup.knob", i + 1), ControllerService.KnobLabel(i), PictogramKind.Knob));
        for (int i = 0; i < 8; i++)
            _steps.Add(new Step(Group.Faders, ControlRoleKind.Fader, i, Loc.F("controllers.setup.fader", i + 1), Loc.F("controllers.control.fader", i + 1), PictogramKind.Fader));
        _steps.Add(new Step(Group.Faders, ControlRoleKind.MasterFader, 0, Loc.T("controllers.setup.master"), Loc.T("controllers.control.master_fader"), PictogramKind.Fader));

        void Btn(Group g, ControlRoleKind role, string prompt, string name, GlyphKind? icon = null, string? word = null, string? command = null)
            => _steps.Add(new Step(g, role, 0, prompt, name, PictogramKind.Button, icon, word, command));
        Btn(Group.Transport, ControlRoleKind.Play, Loc.T("controllers.setup.play"), Loc.T("controllers.control.play"), GlyphKind.Play);
        Btn(Group.Transport, ControlRoleKind.Stop, Loc.T("controllers.setup.stop"), Loc.T("controllers.control.stop"), GlyphKind.Stop);
        Btn(Group.Transport, ControlRoleKind.Record, Loc.T("controllers.setup.record"), Loc.T("controllers.control.record"), GlyphKind.Record);
        Btn(Group.Transport, ControlRoleKind.Loop, Loc.T("controllers.setup.loop"), Loc.T("controllers.control.loop"), GlyphKind.Loop);
        void Pair(Group g, string prompt, string name, Half left, Half right)
            => _steps.Add(new Step(g, right.Role, 0, prompt, name, PictogramKind.Pair, Command: right.Command, Left: left, Right: right));
        Pair(Group.Transport, Loc.T("controllers.setup.pair_wind"), Loc.T("controllers.setup.pair_wind_name"),
            new Half(Loc.T("controllers.setup.rewind"), Loc.T("controllers.control.rewind"), ControlRoleKind.Rewind, Icon: GlyphKind.StepLeft),
            new Half(Loc.T("controllers.setup.forward"), Loc.T("controllers.control.forward"), ControlRoleKind.Forward, Icon: GlyphKind.StepRight));

        Pair(Group.Navigation, Loc.T("controllers.setup.pair_tracks"), Loc.T("controllers.setup.pair_tracks_name"),
            new Half(Loc.T("controllers.setup.track_prev"), Loc.T("controllers.control.track_prev"), ControlRoleKind.TrackPrev, Icon: GlyphKind.ChevronLeft),
            new Half(Loc.T("controllers.setup.track_next"), Loc.T("controllers.control.track_next"), ControlRoleKind.TrackNext, Icon: GlyphKind.ChevronRight));
        Pair(Group.Navigation, Loc.T("controllers.setup.pair_banks"), Loc.T("controllers.setup.pair_banks_name"),
            new Half(Loc.T("controllers.setup.bank_prev"), Loc.T("controllers.control.bank_prev"), ControlRoleKind.BankPrev, Word: Loc.T("controllers.setup.bank_prev_word")),
            new Half(Loc.T("controllers.setup.bank_next"), Loc.T("controllers.control.bank_next"), ControlRoleKind.BankNext, Word: Loc.T("controllers.setup.bank_next_word")));
        Pair(Group.Navigation, Loc.T("controllers.setup.pair_pages"), Loc.T("controllers.setup.pair_pages_name"),
            new Half(Loc.T("controllers.setup.page_prev"), Loc.T("controllers.control.page_prev"), ControlRoleKind.PagePrev, Word: Loc.T("controllers.setup.page_prev_word")),
            new Half(Loc.T("controllers.setup.page_next"), Loc.T("controllers.control.page_next"), ControlRoleKind.PageNext, Word: Loc.T("controllers.setup.page_next_word")));
        // specs/107: step the knobs along the track's chain.
        Pair(Group.Navigation, Loc.T("controllers.setup.pair_devices"), Loc.T("controllers.setup.pair_devices_name"),
            new Half(Loc.T("controllers.setup.device_prev"), Loc.T("commands.device_focus_previous"), ControlRoleKind.Command, "device.focusPrevious", Word: Loc.T("controllers.setup.device_prev_word")),
            new Half(Loc.T("controllers.setup.device_next"), Loc.T("commands.device_focus_next"), ControlRoleKind.Command, "device.focusNext", Word: Loc.T("controllers.setup.device_next_word")));

        void Cmd(Group g, string id, string prompt, string name, GlyphKind? icon = null, string? word = null)
            => Btn(g, ControlRoleKind.Command, prompt, name, icon, word, id);
        Pair(Group.Session, Loc.T("controllers.setup.pair_clips"), Loc.T("controllers.setup.pair_clips_name"),
            new Half(Loc.T("controllers.setup.prev_scene"), Loc.T("commands.session_select_prev_scene"), ControlRoleKind.Command, "session.selectUp", GlyphKind.ChevronUp),
            new Half(Loc.T("controllers.setup.next_scene"), Loc.T("commands.session_select_next_scene"), ControlRoleKind.Command, "session.selectDown", GlyphKind.ChevronDown));
        Pair(Group.Session, Loc.T("controllers.setup.pair_session_tracks"), Loc.T("controllers.setup.pair_session_tracks_name"),
            new Half(Loc.T("controllers.setup.prev_track"), Loc.T("commands.session_select_prev_track"), ControlRoleKind.Command, "session.selectLeft", GlyphKind.ChevronLeft),
            new Half(Loc.T("controllers.setup.next_track"), Loc.T("commands.session_select_next_track"), ControlRoleKind.Command, "session.selectRight", GlyphKind.ChevronRight));
        Cmd(Group.Session, "session.toggleSlot", Loc.T("controllers.setup.slot_launch"), Loc.T("commands.session_toggle_slot"), GlyphKind.Play);
        Cmd(Group.Session, "session.stopTrack", Loc.T("controllers.setup.slot_stop"), Loc.T("commands.session_stop_track"), GlyphKind.Stop);
        Cmd(Group.Session, "session.launchScene", Loc.T("controllers.setup.scene_launch"), Loc.T("commands.launch_scene"), GlyphKind.Session);
        Cmd(Group.Session, "session.stopAll", Loc.T("controllers.setup.stop_all"), Loc.T("commands.stop_all_clips"), word: Loc.T("controllers.setup.stop_all_word"));

        Pair(Group.Extra, Loc.T("controllers.setup.pair_undo"), Loc.T("controllers.setup.pair_undo_name"),
            new Half(Loc.T("controllers.setup.undo"), Loc.T("common.undo"), ControlRoleKind.Command, "edit.undo", Word: Loc.T("common.undo")),
            new Half(Loc.T("controllers.setup.redo"), Loc.T("commands.redo"), ControlRoleKind.Command, "edit.redo", Word: Loc.T("commands.redo")));
        Pair(Group.Extra, Loc.T("controllers.setup.pair_tempo"), Loc.T("controllers.setup.pair_tempo_name"),
            new Half(Loc.T("controllers.setup.tempo_down"), Loc.T("commands.tempo_down"), ControlRoleKind.Command, "transport.tempoDown", Word: Loc.T("controllers.setup.tempo_down_word")),
            new Half(Loc.T("controllers.setup.tempo_up"), Loc.T("commands.tempo_up"), ControlRoleKind.Command, "transport.tempoUp", Word: Loc.T("controllers.setup.tempo_up_word")));
        Cmd(Group.Extra, "transport.metronome", Loc.T("controllers.setup.metronome"), Loc.T("controllers.setup.metronome_name"), GlyphKind.Metronome);
        Cmd(Group.Extra, "file.save", Loc.T("controllers.setup.save"), Loc.T("commands.save_project"), GlyphKind.Save);
        Cmd(Group.Extra, "transport.tapTempo", Loc.T("controllers.setup.tap"), Loc.T("controllers.setup.tap_name"), word: Loc.T("controllers.setup.tap_name"));
        _steps.Add(new Step(Group.Extra, ControlRoleKind.Jog, 0, Loc.T("controllers.setup.jog"), Loc.T("controllers.control.jog"), PictogramKind.Jog));
    }

    private static string GroupName(Group g) => g switch
    {
        Group.Knobs => Loc.T("controllers.setup.group_knobs"),
        Group.Faders => Loc.T("controllers.setup.group_faders"),
        Group.Transport => Loc.T("controllers.setup.group_transport"),
        Group.Navigation => Loc.T("controllers.setup.group_navigation"),
        Group.Session => Loc.T("controllers.setup.group_session"),
        _ => Loc.T("controllers.setup.group_extra"),
    };

    private void ShowStep()
    {
        var s = _steps[_step];
        var inGroup = _steps.Where(x => x.Group == s.Group).ToList();
        _counter.Text = Loc.F("controllers.setup.group_step", GroupTitle(s.Group), inGroup.IndexOf(s) + 1, inGroup.Count, _step + 1, _steps.Count);
        _half = 0;
        _prompt.Text = s.Prompt;
        if (s.IsPair) _picture.ShowPair(s.Left!.Icon, s.Left.Word, s.Right!.Icon, s.Right.Word);
        else _picture.Show(s.Picture, s.Icon, s.Word);
        _skipGroup.Content = Loc.F("controllers.setup.skip_group", GroupName(s.Group));
        _skipGroup.IsVisible = inGroup.IndexOf(s) < inGroup.Count - 1;
        _back.IsEnabled = _step > 0;
        ResetStep();
        ShowGroups();
        ShowProgress();
    }

    private void ResetStep()
    {
        _events.Clear();
        _found = null;
        _advanceAt = 0;
        _service.TakeCaptured();
        _hearing.Text = Loc.T("controllers.setup.hearing_none");
        _detected.Text = Loc.T("controllers.setup.waiting");
        _detected.Foreground = NotaPalette.TextSecondary;
        _encoder.IsVisible = false;
        _next.IsVisible = false;
        _picture.Learned = false;
        if (_half == 0) { _learned.Remove((_step, 0)); _learned.Remove((_step, 1)); }   // redoing it
        else _learned.Remove((_step, 1));
        ShowSummary();
    }

    // Collect the step's messages; once the control has been still for a moment (a button: once
    // it was pressed and let go), read them. A learned step moves on by itself.
    private void Poll()
    {
        long now = Environment.TickCount64;
        if (_activityMs > 0 && now - _activityMs > 160) { _activity.Fill = NotaPalette.TextDisabled; _activityMs = 0; }
        if (_advanceAt > 0)
        {
            _service.TakeCaptured();   // what the control still sends belongs to the step just learned
            if (now >= _advanceAt) Advance();
            return;
        }
        var got = _service.TakeCaptured();
        if (got.Count > 0)
        {
            var last = got[^1];
            _hearing.Text = Raw(last);
            _activity.Fill = NotaPalette.Success;   // like the MIDI light: something arrived
            _activityMs = now;
            foreach (var ev in got)
            {
                if (LearnedFor(ev) is { } other)
                {
                    // An already learned control: say so, but don't let it take this step.
                    _detected.Text = Loc.F("controllers.setup.already", _steps[other].Name);
                    _detected.Foreground = NotaPalette.Warning;
                    continue;
                }
                _events.Add(ev);
                _lastEventMs = now;
            }
        }
        if (_events.Count == 0 || _found is not null) return;
        var s = _steps[_step];
        bool pressed = (s.Button || s.IsPair) && PressedAndReleased(_events);
        long still = s.Picture is PictogramKind.Knob or PictogramKind.Encoder or PictogramKind.Jog or PictogramKind.Pair ? KnobStillMs : StillMs;
        if (!pressed && now - _lastEventMs < still) return;
        var c = ControlClassifier.Classify(_events);
        if (c is not { } found) { _events.Clear(); return; }
        if (s.IsPair && (_half == 1 || !LooksLikeEncoder(found, _events)))
        {
            LearnHalf(found);   // a button: this half now, the other one next
            return;
        }
        _found = found;
        if (found.Ambiguous)
        {
            _detected.Text = Loc.F("controllers.setup.ambiguous", found.Number, found.Channel + 1);
            _encoder.SelectedIndex = 1;
            _encoder.IsVisible = true;
            _next.IsVisible = true;
            return;
        }
        Learn(found);
    }

    // A pair's control: an encoder (or knob) turns — relative steps, or an absolute run of values;
    // a button sends a note, or a CC that only goes up and back to 0.
    private static bool LooksLikeEncoder(ClassifiedControl c, List<(int Status, int Channel, int D1, int D2)> ev)
    {
        if (c.Message == ControlMessage.Note) return false;
        if (c.Encoder != EncoderMode.Absolute || c.Ambiguous) return true;
        if (c.Message != ControlMessage.Cc) return true;
        var values = ev.Where(e => e.Status == 0xB0 && e.D1 == c.Number).Select(e => e.D2).Distinct().ToList();
        bool button = values.Count <= 2 && values.Contains(0);
        return !button && values.Count >= 3;
    }

    // A pair learned as buttons: the left one first, then the right one.
    private void LearnHalf(ClassifiedControl c)
    {
        var s = _steps[_step];
        var half = _half == 0 ? s.Left! : s.Right!;
        _learned[(_step, _half)] = new LearnedControl(half.Role, 0, c, half.Command);
        _detected.Text = Loc.F("controllers.setup.learned", half.Name, Describe(c));
        _detected.Foreground = NotaPalette.Success;
        ShowSummary();
        if (_half == 0)
        {
            // Now the other button: its own prompt and picture.
            _half = 1;
            _events.Clear();
            _found = null;
            _prompt.Text = s.Right!.Prompt;
            _picture.Show(PictogramKind.Button, s.Right.Icon, s.Right.Word);
            return;
        }
        _found = c;
        _picture.Learned = true;
        _advanceAt = Environment.TickCount64 + AdvanceMs;
        ShowProgress();
    }

    // A button's press and release: a note-on then its note-off, or a CC up then down.
    private static bool PressedAndReleased(List<(int Status, int Channel, int D1, int D2)> ev)
    {
        bool down = false;
        foreach (var e in ev)
        {
            if (e.Status == 0x90 && e.D2 > 0) down = true;
            else if ((e.Status == 0x80 || (e.Status == 0x90 && e.D2 == 0)) && down) return true;
            else if (e.Status == 0xB0 && e.D2 >= 64) down = true;
            else if (e.Status == 0xB0 && e.D2 < 64 && down) return true;
        }
        return false;
    }

    private static string Raw((int Status, int Channel, int D1, int D2) e) => e.Status switch
    {
        0x90 or 0x80 => Loc.F("controllers.setup.raw_note", e.D1, e.Channel + 1, e.D2),
        0xE0 => Loc.F("controllers.setup.raw_bend", e.Channel + 1),
        0xB0 => Loc.F("controllers.setup.raw_cc", e.D1, e.Channel + 1, e.D2),
        _ => Loc.F("controllers.setup.raw_other", e.Status.ToString("X2", System.Globalization.CultureInfo.CurrentCulture), e.Channel + 1),
    };

    // The step an incoming message's control was already learned for, or null.
    private int? LearnedFor((int Status, int Channel, int D1, int D2) e)
    {
        foreach (var ((step, _), l) in _learned)
        {
            var c = l.Control;
            if (c.Channel != e.Channel) continue;
            bool hit = c.Message switch
            {
                ControlMessage.Note => e.Status is 0x90 or 0x80 && e.D1 == c.Number,
                ControlMessage.PitchBend => e.Status == 0xE0,
                ControlMessage.Cc => e.Status == 0xB0 && e.D1 == c.Number,
                ControlMessage.Cc14 => e.Status == 0xB0 && (e.D1 == c.Number || e.D1 == c.Number + 32),
                _ => false,   // NRPN shares CC 98 / 99 / 6 / 38 with every other NRPN control
            };
            if (hit) return step;
        }
        return null;
    }

    private static string Describe(ClassifiedControl c)
    {
        string what = c.Message switch
        {
            ControlMessage.Note => Loc.F("controllers.setup.is_note", c.Number),
            ControlMessage.PitchBend => Loc.T("controllers.setup.is_bend"),
            ControlMessage.Nrpn => Loc.F("controllers.setup.is_nrpn", c.Number),
            ControlMessage.Cc14 => Loc.F("controllers.setup.is_cc14", c.Number),
            _ => Loc.F("controllers.setup.is_cc", c.Number),
        };
        string enc = c.Encoder switch
        {
            EncoderMode.RelativeTwos => Loc.T("controllers.setup.enc_twos"),
            EncoderMode.RelativeSignBit => Loc.T("controllers.setup.enc_signbit"),
            EncoderMode.RelativeOffset => Loc.T("controllers.setup.enc_offset"),
            _ => Loc.T("controllers.setup.enc_absolute"),
        };
        return Loc.F("controllers.setup.found", what, c.Channel + 1, enc);
    }

    // The Next button: only for an encoder whose encoding the user picks.
    private void Accept()
    {
        if (_found is not { } c) return;
        c = c with
        {
            Ambiguous = false,
            Encoder = _encoder.SelectedIndex switch
            {
                1 => EncoderMode.RelativeTwos, 2 => EncoderMode.RelativeSignBit, 3 => EncoderMode.RelativeOffset, _ => EncoderMode.Absolute,
            },
        };
        Learn(c);
        Advance();
    }

    private void Learn(ClassifiedControl c)
    {
        var s = _steps[_step];
        // A jog wheel is an endless control: if it read as absolute, it is still a wheel.
        _learned[(_step, 0)] = s.IsPair
            ? new LearnedControl(s.Right!.Role, 0, c, s.Right.Command,
                s.Left!.Role == ControlRoleKind.Command ? s.Left.Command : null,
                s.Left.Role == ControlRoleKind.Command ? null : s.Left.Role)   // an encoder: both halves
            : new LearnedControl(s.Role, s.Index, c, s.Command);
        _found = c;
        _detected.Text = Loc.F("controllers.setup.learned", s.Name, Describe(c));
        _detected.Foreground = NotaPalette.Success;
        _encoder.IsVisible = false;
        _next.IsVisible = false;
        _picture.Learned = true;
        _advanceAt = Environment.TickCount64 + AdvanceMs;
        ShowSummary();
        ShowProgress();
    }

    // The learned controls as chips; a click goes back to that step to redo it.
    private void ShowSummary()
    {
        _learnedChips.Children.Clear();
        _learnedLabel.Text = _learned.Count == 0 ? Loc.T("controllers.setup.learned_none") : Loc.F("controllers.setup.learned_count", _learned.Count);
        foreach (var ((step, half), l) in _learned.OrderBy(l => l.Key))
        {
            int at = step;
            var st = _steps[at];
            string chipName = !st.IsPair ? st.Name
                : l.LeftRole is not null || l.CommandLeft is not null ? st.Name   // the encoder: the pair's name
                : half == 0 ? st.Left!.Name : st.Right!.Name;
            var chip = new Button
            {
                Classes = { "chip" },
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 6,
                    Children =
                    {
                        new TextBlock { Text = chipName, Foreground = NotaPalette.TextPrimary, VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = Short(l.Control), Classes = { "Mono" }, Foreground = NotaPalette.TextSecondary, VerticalAlignment = VerticalAlignment.Center },
                    },
                },
            };
            ToolTip.SetTip(chip, Loc.T("controllers.setup.redo_tip"));
            chip.Click += (_, _) => { _step = at; ShowStep(); };
            _learnedChips.Children.Add(chip);
        }
    }

    private static string Short(ClassifiedControl c) => c.Message switch
    {
        ControlMessage.Note => Loc.F("controllers.setup.is_note", c.Number),
        ControlMessage.PitchBend => Loc.T("controllers.setup.is_bend"),
        ControlMessage.Nrpn => Loc.F("controllers.setup.is_nrpn", c.Number),
        ControlMessage.Cc14 => Loc.F("controllers.setup.is_cc14", c.Number),
        _ => Loc.F("controllers.setup.is_cc", c.Number),
    };

    // The group strip: done groups ticked, the current one picked (soft tint), the rest quiet.
    private void ShowGroups()
    {
        _groups.Children.Clear();
        var current = _steps[_step].Group;
        foreach (var g in Enum.GetValues<Group>())
        {
            int first = _steps.FindIndex(x => x.Group == g);
            if (first < 0) continue;
            bool here = g == current, done = first < _step && !here;
            int learned = _learned.Keys.Count(k => _steps[k.Step].Group == g);
            var label = new TextBlock
            {
                Text = GroupTitle(g) + (learned > 0 ? "  " + learned.ToString(System.Globalization.CultureInfo.CurrentCulture) : ""),
                FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
                Foreground = here ? NotaPalette.AccentBright : done ? NotaPalette.TextSecondary : NotaPalette.TextTertiary,
                FontWeight = here ? Avalonia.Media.FontWeight.SemiBold : Avalonia.Media.FontWeight.Normal,
            };
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            if (done) content.Children.Add(new Glyph(GlyphKind.Check, 10) { Foreground = NotaPalette.TextSecondary, VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(label);
            var chip = new Border
            {
                Height = 24, Padding = new Thickness(10, 0), CornerRadius = NotaRadius.Group,
                Background = here ? NotaPalette.AccentSoft : done ? NotaPalette.Well : Avalonia.Media.Brushes.Transparent,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                Child = content,
            };
            chip.PointerPressed += (_, _) => { _step = first; ShowStep(); };
            _groups.Children.Add(chip);
        }
    }

    // The strip's short names.
    private static string GroupTitle(Group g) => g switch
    {
        Group.Knobs => Loc.T("controllers.setup.tab_knobs"),
        Group.Faders => Loc.T("controllers.setup.tab_faders"),
        Group.Transport => Loc.T("controllers.setup.tab_transport"),
        Group.Navigation => Loc.T("controllers.setup.tab_navigation"),
        Group.Session => Loc.T("controllers.setup.tab_session"),
        _ => Loc.T("controllers.setup.tab_extra"),
    };

    private void ShowProgress()
    {
        double w = _progressTrack.Bounds.Width;
        _progressFill.Width = w <= 0 ? 0 : w * (_step + (_advanceAt > 0 ? 1 : 0)) / Math.Max(1, _steps.Count);
    }

    private void Advance()
    {
        _advanceAt = 0;
        if (_step + 1 >= _steps.Count) { Finish(); return; }
        _step++;
        ShowStep();
    }

    private void SkipGroup()
    {
        var g = _steps[_step].Group;
        int next = _steps.FindIndex(_step, x => x.Group != g);
        if (next < 0) { Finish(); return; }
        _step = next;
        ShowStep();
    }

    private void Back()
    {
        if (_half == 1) { ShowStep(); return; }   // the pair's first button again
        if (_step == 0) return;
        _step--;
        ShowStep();   // ResetStep drops what was learned for it
    }

    private void Finish()
    {
        _advanceAt = 0;
        if (_learned.Count > 0)
        {
            string json = ProfileWriter.Write(_port.Name, _port.Identity, _learned.OrderBy(l => l.Key).Select(l => l.Value).ToList());
            _service.SaveUserProfile(ProfileWriter.IdFor(_port.Name) + ".json", json);
            _service.SetChoice(_port.Uid, null, true);   // automatic: the new profile matches it
        }
        Close();
    }
}
