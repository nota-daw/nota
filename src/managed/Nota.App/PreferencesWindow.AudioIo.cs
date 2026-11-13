// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Settings → Outputs and Settings → Inputs (specs/088): which device Nota plays through and
// records from. Outputs carries the test tone. Inputs lists every input of the capture device
// (specs/086, #353 option 2) with the name the device gives it, a switch and a name of the
// user's; then the stereo pairs, which can be named too. Track input pickers list only
// switched-on inputs, under these names. Kept per device in the app settings
// (InputSetupAccess); edits apply at once. Under ASIO one driver is both sides: it is picked
// on the Audio page; Outputs picks its Main and Cue pairs (specs/089), Inputs its input pair.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using Nota.Application.Localization;

namespace Nota.App;

public sealed partial class PreferencesWindow
{
    private const double InputNameW = 200, InputDeviceW = 170;

    private Control OutputsPane()
    {
        if (_main is null) return Caption(Loc.T("preferences.audio_settings_are_unavailable_in"));
        _loading = true;
        var engine = _main.Engine;
        var rows = new List<Control>();
        if (IsAsio(engine))
        {
            rows.Add(AsioDeviceRow(engine));   // the output pair is Main, below (specs/089)
        }
        else
        {
            var combo = Combo(280);
            FillDeviceCombo(combo, _outputUids, _audioDevices.OutputDevices(), engine.GetAudioConfig().OutputUid);
            combo.SelectionChanged += (_, _) => { int i = combo.SelectedIndex; if (i >= 0 && i < _outputUids.Count) { engine.SetAudioOutputDevice(_outputUids[i]); Apply(); } };
            rows.Add(Row(Loc.T("common.output"), combo));
        }
        var body = Sections(
            Section(Loc.T("preferences.audio_device"), 6, rows.ToArray()),
            OutputsHost(engine),   // Main and Cue pairs, the click (specs/089)
            Section(Loc.T("preferences.test"), 8, TestToneRow()));
        _loading = false;
        return body;
    }

    private Control InputsPane()
    {
        if (_main is null) return Caption(Loc.T("preferences.audio_settings_are_unavailable_in"));
        _loading = true;
        var engine = _main.Engine;
        var rows = new List<Control>();
        if (IsAsio(engine))
        {
            rows.Add(AsioDeviceRow(engine));
            if (engine.ActiveAudioSystem == AudioSystem.Asio)
            {
                var ins = engine.AsioChannels(true);
                rows.Add(Row(Loc.T("common.input"), ins.Count > 0 ? PairCombo(engine, input: true, ins) : Caption(Loc.T("preferences.no_inputs_on_this_driver"))));
            }
        }
        else
        {
            var combo = Combo(280);
            FillDeviceCombo(combo, _inputUids, _audioDevices.InputDevices(), engine.GetAudioConfig().InputUid);
            combo.SelectionChanged += (_, _) => { int i = combo.SelectedIndex; if (i >= 0 && i < _inputUids.Count) { engine.SetAudioInputDevice(_inputUids[i]); Apply(); } };
            rows.Add(Row(Loc.T("common.input"), combo));
        }
        var body = Sections(
            Section(Loc.T("preferences.audio_device"), 6, rows.ToArray()),
            InputsHost(engine));
        _loading = false;
        return body;
    }

    private static bool IsAsio(IAudioEngine engine) =>
        OperatingSystem.IsWindows() && engine.AsioAvailable && engine.AudioSystem == AudioSystem.Asio;

    // "Driver   Focusrite USB ASIO — the ASIO driver is both input and output.  [Change on the Audio page]"
    private Control AsioDeviceRow(IAudioEngine engine)
    {
        string uid = engine.AsioDriver;
        string name = uid;
        foreach (var d in engine.AsioDrivers())
            if (string.Equals(d.Uid, uid, StringComparison.OrdinalIgnoreCase)) name = d.Name;
        if (name.Length == 0) name = Loc.T("preferences.asio_caps");
        var go = Btn(Loc.T("preferences.change_on_audio_page"));
        go.HorizontalAlignment = HorizontalAlignment.Left;
        go.Click += (_, _) => Select("Audio");
        return Row(Loc.T("preferences.driver"), new StackPanel
        {
            Spacing = 8,
            Children = { Caption(Loc.F("preferences.asio_device_on_audio", name)), go },
        });
    }

    // The Inputs section sits in a host so a device change can rebuild it alone: the list is
    // the device's inputs, its names and switches its own entry in the settings.
    private ContentControl? _inputsHost;

    private Control InputsHost(IAudioEngine engine)
    {
        _inputsHost = new ContentControl { Content = InputsSection(engine) };
        return _inputsHost;
    }

    private void RebuildInputs(IAudioEngine engine)
    {
        if (_inputsHost is { } host) host.Content = InputsSection(engine);
    }

    private Control InputsSection(IAudioEngine engine)
    {
        int count = engine.AudioInputChannelCount();
        if (count <= 0) count = engine.ProbeAudioInput();
        if (count <= 0)
            return Section(Loc.T("preferences.inputs"), 8, Caption(Loc.T("preferences.inputs_no_device")));

        var rows = new List<Control> { Caption(Loc.T("preferences.inputs_hint"), muted: true) };
        for (int c = 0; c < count; c++) rows.Add(InputRow(engine, c));
        if (count >= 2)
        {
            rows.Add(new Border { Height = 6 });
            rows.Add(Caption(Loc.T("preferences.inputs_pairs"), muted: true));
            for (int c = 0; c + 1 < count; c += 2) rows.Add(PairRow(engine, c));
        }
        return Section(Loc.T("preferences.inputs"), 4, rows.ToArray());
    }

    // "In 3   [switch]   Mic/Line 3   [ name… ]"
    private Control InputRow(IAudioEngine engine, int channel)
    {
        var setup = InputSetupAccess.Current(engine);
        var pick = InputPick.Mono(channel);
        string device = InputPickList.DeviceName(engine.AudioInputChannelName, channel);
        var sw = new ToggleSwitch(InputSetups.IsOn(setup, channel), large: false) { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        sw.Changed += on => InputSetupAccess.Store(engine, InputSetups.WithOn(InputSetupAccess.Current(engine), channel, on));
        ToolTip.SetTip(sw, Loc.T("preferences.inputs_switch_tip"));
        var deviceName = new TextBlock
        {
            Text = device.Length > 0 ? device : Loc.F("record_input_pick.audio_input_mono", channel + 1), FontSize = 12,
            Foreground = TextTertiary, Width = InputDeviceW, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var name = NameBox(engine, pick, device.Length > 0 ? device : Loc.F("record_input_pick.in_n", channel + 1));
        return InputFormRow(Loc.F("record_input_pick.in_n", channel + 1), InputLine(sw, deviceName, name));
    }

    private Control PairRow(IAudioEngine engine, int first)
    {
        var pick = InputPick.Pair(first);
        var name = NameBox(engine, pick, Loc.F("record_input_pick.in_pair", first + 1, first + 2));
        return InputFormRow(Loc.F("record_input_pick.in_pair", first + 1, first + 2), InputLine(null, null, name));
    }

    // The user's name for an input or pair: committed on Enter or leaving the field; empty
    // goes back to the device's name.
    private TextBox NameBox(IAudioEngine engine, InputPick pick, string placeholder)
    {
        var box = new TextBox
        {
            Text = InputSetups.NameOf(InputSetupAccess.Current(engine), pick) ?? "", PlaceholderText = placeholder,
            Classes = { "field" }, FontSize = 12, Width = InputNameW, MaxLength = 40, VerticalAlignment = VerticalAlignment.Center,
        };
        void Commit()
        {
            string now = box.Text?.Trim() ?? "";
            if (now == (InputSetups.NameOf(InputSetupAccess.Current(engine), pick) ?? "")) return;
            InputSetupAccess.Store(engine, InputSetups.WithName(InputSetupAccess.Current(engine), pick, now));
        }
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
        box.LostFocus += (_, _) => Commit();
        return box;
    }

    // Switch · device name · user name in fixed columns, so pairs' names line up with the inputs'.
    private static Control InputLine(Control? sw, Control? deviceName, Control name)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"40,{InputDeviceW + 12},Auto") };
        if (sw is not null) grid.Children.Add(sw);
        if (deviceName is not null) { Grid.SetColumn(deviceName, 1); grid.Children.Add(deviceName); }
        Grid.SetColumn(name, 2);
        grid.Children.Add(name);
        return grid;
    }

    private static Control InputFormRow(string label, Control control)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{LabelCol},*"), MinHeight = 30 };
        grid.Children.Add(RowLabel(label));
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }
}
