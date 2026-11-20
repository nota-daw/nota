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
//
// Learn everything again (specs/110 FR-016): opened from the mapping editor with the profile as a
// seed, each step starts with the function's current control — Next (or Enter) keeps it, a new
// movement replaces it, a skip clears it — and Finish hands the result back to the editor, which
// saves it keeping everything the wizard doesn't learn.

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
    private const long AdvanceMs = 550;

    private readonly ControllerService _service;
    private readonly ConnectedController _port;
    private readonly List<SetupFunction> _steps = SetupFunctions.Build();
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
    // Learn everything again: the controls the profile had, not yet kept or replaced.
    private readonly ProfileMapping? _seed;
    private readonly HashSet<(int Step, int Half)> _seeded = new();

    /// <summary>A seeded run's result once Finish was pressed (null: cancelled or not seeded): every
    /// function learned or kept, as (function, its left half for a two-way control, control).</summary>
    public List<(FunctionKey Right, FunctionKey? Left, ClassifiedControl Control)>? Result { get; private set; }

    /// <summary>The functions the wizard covers: what a seeded result replaces.</summary>
    public IEnumerable<FunctionKey> Scope => _steps.SelectMany(s => s.Keys);

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

    public ControllerSetupWindow(ControllerService service, ConnectedController port, ProfileMapping? seed = null)
    {
        _service = service;
        _port = port;
        _seed = seed;
        Title = Loc.F("controllers.setup.title", port.Name);
        Width = 600;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = NotaPalette.BgApp;

        if (seed is not null) Prefill(seed);

        foreach (var key in new[] { "controllers.setup.enc_absolute", "controllers.setup.enc_twos", "controllers.setup.enc_signbit", "controllers.setup.enc_offset" })
            _encoder.Items.Add(EncoderName(key));
        _encoder.SelectedIndex = 0;

        _next.Content = Loc.T("controllers.setup.next");
        _next.Click += (_, _) => Accept();
        var skip = new Button { Content = Loc.T("controllers.setup.skip"), Classes = { "ghost" } };
        skip.Click += (_, _) => { DropStep(_step); Advance(); };
        _skipGroup.Click += (_, _) => SkipGroup();
        _back.Content = Loc.T("controllers.setup.back");
        _back.Click += (_, _) => Back();
        var again = new Button { Content = Loc.T("controllers.setup.again"), Classes = { "ghost" } };
        again.Click += (_, _) => { DropStep(_step); ResetStep(); };
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
        KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter && _next.IsVisible) { Accept(); e.Handled = true; }
        };
        ShowStep();
    }

    private static string EncoderName(string key) => key switch
    {
        "controllers.setup.enc_twos" => Loc.T("controllers.setup.enc_twos"),
        "controllers.setup.enc_signbit" => Loc.T("controllers.setup.enc_signbit"),
        "controllers.setup.enc_offset" => Loc.T("controllers.setup.enc_offset"),
        _ => Loc.T("controllers.setup.enc_absolute"),
    };

    // The profile's controls as already learned steps (Learn everything again).
    private void Prefill(ProfileMapping seed)
    {
        for (int i = 0; i < _steps.Count; i++)
        {
            var s = _steps[i];
            if (s.IsPair)
            {
                var right = seed.EntryFor(s.Right!.Key);
                if (right?.Decl is { } rd && right.Left == s.Left!.Key)
                    Seed(i, 0, new LearnedControl(s.Right.Role, 0, Classified(rd), s.Right.Command,
                        s.Left.Role == ControlRoleKind.Command ? s.Left.Command : null,
                        s.Left.Role == ControlRoleKind.Command ? null : s.Left.Role));
                else
                {
                    if (seed.ControlOf(s.Left!.Key) is { } ld && seed.EntryFor(s.Left.Key)!.Left is null)
                        Seed(i, 0, new LearnedControl(s.Left.Role, 0, Classified(ld), s.Left.Command));
                    if (right?.Decl is { } rb && right.Left is null)
                        Seed(i, 1, new LearnedControl(s.Right.Role, 0, Classified(rb), s.Right.Command));
                }
            }
            else if (seed.EntryFor(s.Key) is { Decl: { } d, Left: null })
                Seed(i, 0, new LearnedControl(s.Role, s.Index, Classified(d), s.Command));
        }

        void Seed(int step, int half, LearnedControl l) { _learned[(step, half)] = l; _seeded.Add((step, half)); }
        static ClassifiedControl Classified(ControlDecl d) => new(d.Message, d.Channel, d.Number, d.Encoder);
    }

    private bool SeededHere => _seeded.Contains((_step, 0)) || _seeded.Contains((_step, 1));

    // The step learns nothing: skipped, or redone.
    private void DropStep(int step)
    {
        _learned.Remove((step, 0)); _learned.Remove((step, 1));
        _seeded.Remove((step, 0)); _seeded.Remove((step, 1));
    }

    // A control the profile had on a later step, learned here: it moves to this step.
    private void TakeFromLater(ClassifiedControl c)
    {
        foreach (var k in _seeded.Where(k => k.Step > _step && _learned.TryGetValue(k, out var l) && Same(l.Control, c)).ToList())
        {
            _learned.Remove(k);
            _seeded.Remove(k);
        }
    }

    private static bool Same(ClassifiedControl a, ClassifiedControl b) => a.Message == b.Message && a.Channel == b.Channel && a.Number == b.Number;

    private void ShowStep()
    {
        var s = _steps[_step];
        var inGroup = _steps.Where(x => x.Group == s.Group).ToList();
        _counter.Text = Loc.F("controllers.setup.group_step", SetupFunctions.GroupTitle(s.Group), inGroup.IndexOf(s) + 1, inGroup.Count, _step + 1, _steps.Count);
        _half = 0;
        _prompt.Text = s.Prompt;
        if (s.IsPair) _picture.ShowPair(s.Left!.Icon, s.Left.Word, s.Right!.Icon, s.Right.Word);
        else _picture.Show(s.Picture, s.Icon, s.Word);
        _skipGroup.Content = Loc.F("controllers.setup.skip_group", SetupFunctions.GroupName(s.Group));
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
        _next.Content = Loc.T("controllers.setup.next");
        _picture.Learned = false;
        if (_half == 0 && SeededHere)
        {
            // Learn everything again: the control it is on now — keep it, or move another one.
            var now = string.Join(" · ", new[] { 0, 1 }.Where(h => _learned.ContainsKey((_step, h))).Select(h => ControlCatcher.Short(_learned[(_step, h)].Control)));
            _detected.Text = Loc.F("controllers.setup.current", now);
            _next.Content = Loc.T("controllers.setup.keep");
            _next.IsVisible = true;
            _picture.Learned = true;
        }
        else if (_half == 0) { _learned.Remove((_step, 0)); _learned.Remove((_step, 1)); }   // redoing it
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
            _hearing.Text = ControlCatcher.Raw(last);
            _activity.Fill = NotaPalette.Success;   // like the MIDI light: something arrived
            _activityMs = now;
            foreach (var ev in got)
            {
                var learnedFor = LearnedFor(ev);
                if (learnedFor == _step && SeededHere && _half == 0)
                {
                    KeepSeeded();   // Learn everything again: the control it already has — kept
                    return;
                }
                if (learnedFor is { } other)
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
        bool pressed = (s.Button || s.IsPair) && ControlCatcher.PressedAndReleased(_events);
        long still = s.Picture is PictogramKind.Knob or PictogramKind.Encoder or PictogramKind.Jog or PictogramKind.Pair ? ControlCatcher.KnobStillMs : ControlCatcher.StillMs;
        if (!pressed && now - _lastEventMs < still) return;
        var c = ControlClassifier.Classify(_events);
        if (c is not { } found) { _events.Clear(); return; }
        if (_half == 0 && SeededHere) DropStep(_step);   // a new control replaces the one it had
        TakeFromLater(found);
        if (s.IsPair && (_half == 1 || !ControlCatcher.LooksLikeEncoder(found, _events)))
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

    // A pair learned as buttons: the left one first, then the right one.
    private void LearnHalf(ClassifiedControl c)
    {
        var s = _steps[_step];
        var half = _half == 0 ? s.Left! : s.Right!;
        _learned[(_step, _half)] = new LearnedControl(half.Role, 0, c, half.Command);
        _detected.Text = Loc.F("controllers.setup.learned", half.Name, ControlCatcher.Describe(c));
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

    // The step an incoming message's control was already learned for, or null.
    private int? LearnedFor((int Status, int Channel, int D1, int D2) e)
    {
        foreach (var ((step, half), l) in _learned)
        {
            if (step > _step && _seeded.Contains((step, half))) continue;   // a later step's old control: free to take
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

    // The Next button: only for an encoder whose encoding the user picks.
    private void Accept()
    {
        if (_found is null && _half == 0 && SeededHere) { KeepSeeded(); return; }
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

    // Learn everything again: the step keeps the control it had.
    private void KeepSeeded()
    {
        _seeded.Remove((_step, 0)); _seeded.Remove((_step, 1));
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
        _detected.Text = Loc.F("controllers.setup.learned", s.Name, ControlCatcher.Describe(c));
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
                        new TextBlock { Text = ControlCatcher.Short(l.Control), Classes = { "Mono" }, Foreground = NotaPalette.TextSecondary, VerticalAlignment = VerticalAlignment.Center },
                    },
                },
            };
            ToolTip.SetTip(chip, Loc.T("controllers.setup.redo_tip"));
            chip.Click += (_, _) => { _step = at; ShowStep(); };
            _learnedChips.Children.Add(chip);
        }
    }

    // The group strip: done groups ticked, the current one picked (soft tint), the rest quiet.
    private void ShowGroups()
    {
        _groups.Children.Clear();
        var current = _steps[_step].Group;
        foreach (var g in Enum.GetValues<SetupGroup>())
        {
            int first = _steps.FindIndex(x => x.Group == g);
            if (first < 0) continue;
            bool here = g == current, done = first < _step && !here;
            int learned = _learned.Keys.Count(k => _steps[k.Step].Group == g);
            var label = new TextBlock
            {
                Text = SetupFunctions.GroupTitle(g) + (learned > 0 ? "  " + learned.ToString(System.Globalization.CultureInfo.CurrentCulture) : ""),
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
        for (int i = _step; i < _steps.Count && _steps[i].Group == g; i++) DropStep(i);   // "no more knobs"
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
        if (_seed is not null)
        {
            Result = _learned.OrderBy(l => l.Key).Select(l =>
            {
                var v = l.Value;
                var right = v.Role == ControlRoleKind.Command ? FunctionKey.OfCommand(v.Command!) : FunctionKey.Of(v.Role, v.Index);
                FunctionKey? left = v.LeftRole is { } lr ? FunctionKey.Of(lr) : v.CommandLeft is { } cl ? FunctionKey.OfCommand(cl) : null;
                return (right, left, v.Control);
            }).ToList();
            Close();
            return;
        }
        if (_learned.Count > 0)
        {
            string json = ProfileWriter.Write(_port.Name, _port.Identity, _learned.OrderBy(l => l.Key).Select(l => l.Value).ToList());
            _service.SaveUserProfile(ProfileWriter.IdFor(_port.Name) + ".json", json);
            _service.SetChoice(_port.Uid, null, true);   // automatic: the new profile matches it
        }
        Close();
    }
}
