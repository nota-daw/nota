// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Settings → Outputs → Output pairs (specs/089, #483): which output pair of the playback device
// carries Main (what the audience hears) and which carries the private Cue mix, and where the
// metronome click goes. Kept per device in the app settings (OutputSetupAccess); edits apply
// at once, without restarting the device.

using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Nota.Application;
using Nota.Application.Localization;

namespace Nota.App;

public sealed partial class PreferencesWindow
{
    // Click level steps in dB; the first is "off".
    private static readonly double[] ClickLevelSteps = { OutputSetups.ClickDbMin, -18, -12, -9, -6, -3, 0, 3, 6 };

    private ContentControl? _outputsHost;

    private Control OutputsHost(IAudioEngine engine)
    {
        _outputsHost = new ContentControl { Content = OutputsSection(engine) };
        return _outputsHost;
    }

    private void RebuildOutputs(IAudioEngine engine)
    {
        if (_outputsHost is { } host) host.Content = OutputsSection(engine);
    }

    // One pick a picker offers: 1-based first channel and width.
    private readonly record struct OutPick(int First, int Width);

    // The device's outputs as pairs (1/2, 3/4 …) and a lone last output.
    private static List<OutPick> OutputPicks(int count)
    {
        var list = new List<OutPick>();
        for (int c = 1; c + 1 <= count; c += 2) list.Add(new OutPick(c, 2));
        if (count % 2 == 1) list.Add(new OutPick(count, 1));
        return list;
    }

    // "Out 3/4 · Phones" — the device's name for the first channel when it gives one.
    internal static string OutputPickLabel(IAudioEngine engine, int first, int width)
    {
        string pair = width == 2 ? Loc.F("preferences.outputs_pair", first, first + 1) : Loc.F("preferences.outputs_single", first);
        string name = first - 1 < engine.AudioOutputChannelCount() ? engine.AudioOutputChannelName(first - 1).Trim() : "";
        return name.Length > 0 ? $"{pair} · {name}" : pair;
    }

    private Control OutputsSection(IAudioEngine engine)
    {
        int count = engine.AudioOutputChannelCount();
        var setup = OutputSetupAccess.Current(engine);
        var state = engine.GetOutputRoutingState();
        if (count <= 0)
            return Section(Loc.T("preferences.outputs_pairs"), 8, Caption(Loc.T("preferences.outputs_no_device")));

        var picks = OutputPicks(count);
        var rows = new List<Control>();

        // Main: every pick of the device; a saved pick the device lacks stays listed as missing.
        var main = Combo(280);
        var mainPicks = new List<OutPick>(picks);
        bool mainMissing = !mainPicks.Contains(new OutPick(setup.MainFirst, setup.MainWidth));
        if (mainMissing) mainPicks.Insert(0, new OutPick(setup.MainFirst, setup.MainWidth));
        foreach (var p in mainPicks)
        {
            bool missing = mainMissing && p == mainPicks[0];
            bool isCue = setup.CueFirst is int cf && OutputSetups.Overlaps(p.First, p.Width, cf, setup.CueWidth);
            main.Items.Add(new ComboBoxItem
            {
                Content = missing ? Loc.F("preferences.outputs_missing", OutputPickLabel(engine, p.First, p.Width)) : OutputPickLabel(engine, p.First, p.Width),
                IsEnabled = !isCue && !missing,
            });
        }
        main.SelectedIndex = mainPicks.IndexOf(new OutPick(setup.MainFirst, setup.MainWidth));
        main.SelectionChanged += (_, _) =>
        {
            int i = main.SelectedIndex;
            if (_loading || i < 0 || i >= mainPicks.Count) return;
            var p = mainPicks[i];
            OutputSetupAccess.Store(engine, OutputSetups.WithMain(OutputSetupAccess.Current(engine), p.First, p.Width));
            RebuildOutputs(engine);
        };
        rows.Add(Row(Loc.T("preferences.outputs_main"), main));
        if (state.HasFlag(OutputRoutingState.MainFellBack))
            rows.Add(Row("", Caption(Loc.T("preferences.outputs_main_fallback"))));

        // Cue: "No cue output" first; Main's pair can't be picked (FR-004).
        if (count <= 2)
        {
            rows.Add(Row(Loc.T("preferences.outputs_cue"), Caption(Loc.F("preferences.outputs_cue_two_outputs", count), muted: true)));
        }
        else
        {
            var cue = Combo(280);
            var cuePicks = new List<OutPick?> { null };
            cue.Items.Add(Loc.T("preferences.outputs_no_cue"));
            bool cueMissing = setup.CueFirst is int sf && !picks.Contains(new OutPick(sf, setup.CueWidth));
            if (cueMissing)
            {
                var p = new OutPick(setup.CueFirst!.Value, setup.CueWidth);
                cuePicks.Add(p);
                cue.Items.Add(new ComboBoxItem { Content = Loc.F("preferences.outputs_missing", OutputPickLabel(engine, p.First, p.Width)), IsEnabled = false });
            }
            foreach (var p in picks)
            {
                cuePicks.Add(p);
                cue.Items.Add(new ComboBoxItem
                {
                    Content = OutputPickLabel(engine, p.First, p.Width),
                    IsEnabled = !OutputSetups.Overlaps(p.First, p.Width, setup.MainFirst, setup.MainWidth),
                });
            }
            cue.SelectedIndex = setup.CueFirst is int f ? cuePicks.IndexOf(new OutPick(f, setup.CueWidth)) : 0;
            cue.SelectionChanged += (_, _) =>
            {
                int i = cue.SelectedIndex;
                if (_loading || i < 0 || i >= cuePicks.Count) return;
                var p = cuePicks[i];
                OutputSetupAccess.Store(engine, OutputSetups.WithCue(OutputSetupAccess.Current(engine), p?.First, p?.Width ?? 2));
                RebuildOutputs(engine);
            };
            rows.Add(Row(Loc.T("preferences.outputs_cue"), cue));
            if (state.HasFlag(OutputRoutingState.CueUnavailable))
                rows.Add(Row("", Caption(Loc.T("preferences.outputs_cue_unavailable"))));
        }

        // Click: Main / Cue / Main + Cue (only Main without a Cue output), and its level.
        var click = Combo(150);
        click.Items.Add(Loc.T("preferences.outputs_click_main"));
        click.Items.Add(new ComboBoxItem { Content = Loc.T("preferences.outputs_click_cue"), IsEnabled = setup.HasCue });
        click.Items.Add(new ComboBoxItem { Content = Loc.T("preferences.outputs_click_both"), IsEnabled = setup.HasCue });
        click.SelectedIndex = (int)setup.Click;
        click.SelectionChanged += (_, _) =>
        {
            if (_loading || click.SelectedIndex < 0) return;
            var cur = OutputSetupAccess.Current(engine);
            OutputSetupAccess.Store(engine, OutputSetups.WithClick(cur, (ClickDestination)click.SelectedIndex, cur.ClickDb));
        };
        rows.Add(Row(Loc.T("preferences.outputs_click"), click));

        var level = Combo(150);
        foreach (var db in ClickLevelSteps)
            level.Items.Add(db <= OutputSetups.ClickDbMin ? Loc.T("preferences.outputs_click_off") : NotaNum.Unit(db, "+0;−0;0", "dB"));
        int li = Array.FindIndex(ClickLevelSteps, d => Math.Abs(d - setup.ClickDb) < 0.01);
        level.SelectedIndex = li >= 0 ? li : Array.IndexOf(ClickLevelSteps, 0.0);
        level.SelectionChanged += (_, _) =>
        {
            int i = level.SelectedIndex;
            if (_loading || i < 0) return;
            var cur = OutputSetupAccess.Current(engine);
            OutputSetupAccess.Store(engine, OutputSetups.WithClick(cur, cur.Click, ClickLevelSteps[i]));
        };
        rows.Add(Row(Loc.T("preferences.outputs_click_level"), level));
        rows.Add(Row("", Caption(Loc.T("preferences.outputs_hint"), muted: true)));
        return Section(Loc.T("preferences.outputs_pairs"), 6, rows.ToArray());
    }
}
