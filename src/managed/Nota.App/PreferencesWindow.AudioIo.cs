// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Preferences → Audio → Inputs (specs/086, #353 option 2): every input of the capture device
// with the name the device gives it, a switch and a name of the user's; then the stereo
// pairs, which can be named too. Track input pickers list only switched-on inputs, under these
// names. Kept per device in the app settings (InputSetupAccess); edits apply at once.

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
