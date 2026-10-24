// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application.Midi;

namespace Nota.App;

public enum TempoImportMode
{
    Replace,
    Adaptive
}

public sealed class MidiImportOptions
{
    public HashSet<int> SelectedChannels { get; } = new(Enumerable.Range(0, 16));
    public bool MergeToSingleTrack { get; set; }
    public HashSet<int> SelectedControllers { get; } = new(Enumerable.Range(0, 128));
    public bool ImportTempo { get; set; } = true;
    public TempoImportMode TempoMode { get; set; } = TempoImportMode.Replace;
}

public sealed class MidiImportWindow : NotaWindow
{
    private readonly MidiImportOptions _options = new();
    public MidiImportOptions Options => _options;

    public MidiImportWindow(MidiFileData file)
    {
        Title = "Import MIDI";
        CanResize = false;
        CanMinimize = false;
        CanMaximize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 14,
            Width = 440,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        // 1. Channel Selection (16 buttons in 2 rows of 8)
        content.Children.Add(new TextBlock { Text = "MIDI channels", Classes = { "SectionLabel" }, HorizontalAlignment = HorizontalAlignment.Center });
        var row1 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
        var row2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };

        for (int ch = 0; ch < 16; ch++)
        {
            int channelIndex = ch;
            var btn = new ToggleButton
            {
                Content = (channelIndex + 1).ToString(),
                IsChecked = true,
                Width = 42,
                Height = 26,
                Classes = { "cell" }
            };
            btn.Click += (_, _) =>
            {
                if (btn.IsChecked == true) _options.SelectedChannels.Add(channelIndex);
                else _options.SelectedChannels.Remove(channelIndex);
            };
            if (ch < 8) row1.Children.Add(btn);
            else row2.Children.Add(btn);
        }

        var chanContainer = new StackPanel { Spacing = 4 };
        chanContainer.Children.Add(row1);
        chanContainer.Children.Add(row2);
        content.Children.Add(chanContainer);

        // Merge switch
        var mergeSwitch = new CheckBox
        {
            Content = "Merge to single track",
            IsChecked = false,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        mergeSwitch.Click += (_, _) => _options.MergeToSingleTrack = mergeSwitch.IsChecked == true;
        content.Children.Add(mergeSwitch);

        // 2. Automation CC selection
        content.Children.Add(new TextBlock { Text = "MIDI CC automation", Classes = { "SectionLabel" }, HorizontalAlignment = HorizontalAlignment.Center });
        var ccHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        var btnSelectAll = new Button { Content = "Select all", Classes = { "cell" }, Height = 26 };
        var btnSelectNone = new Button { Content = "Select none", Classes = { "cell" }, Height = 26 };
        ccHeader.Children.Add(btnSelectAll);
        ccHeader.Children.Add(btnSelectNone);
        content.Children.Add(ccHeader);

        var ccPanel = new StackPanel { Spacing = 4 };
        for (int c = 0; c < 128; c++)
        {
            int ccIndex = c;
            var ccCheck = new CheckBox
            {
                Content = $"CC {ccIndex}: {Nota.Application.MidiController.Name(ccIndex)}",
                IsChecked = true
            };
            ccCheck.Click += (_, _) =>
            {
                if (ccCheck.IsChecked == true) _options.SelectedControllers.Add(ccIndex);
                else _options.SelectedControllers.Remove(ccIndex);
            };
            ccPanel.Children.Add(ccCheck);
        }

        var ccScroll = new ScrollViewer
        {
            Height = 120,
            Content = ccPanel,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        content.Children.Add(ccScroll);

        btnSelectAll.Click += (_, _) =>
        {
            for (int c = 0; c < 128; c++) _options.SelectedControllers.Add(c);
            foreach (CheckBox cb in ccPanel.Children.OfType<CheckBox>()) cb.IsChecked = true;
        };
        btnSelectNone.Click += (_, _) =>
        {
            _options.SelectedControllers.Clear();
            foreach (CheckBox cb in ccPanel.Children.OfType<CheckBox>()) cb.IsChecked = false;
        };

        // 3. Tempo Import options
        content.Children.Add(new TextBlock { Text = "Tempo import", Classes = { "SectionLabel" }, HorizontalAlignment = HorizontalAlignment.Center });
        var tempoImportCheck = new CheckBox
        {
            Content = "Import tempo map from MIDI",
            IsChecked = true,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var modeCombo = new ComboBox
        {
            ItemsSource = new[] { "Replace", "Adaptive" },
            SelectedIndex = 0,
            IsEnabled = true,
            Width = 120,
            Height = 26,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        tempoImportCheck.Click += (_, _) =>
        {
            _options.ImportTempo = tempoImportCheck.IsChecked == true;
            modeCombo.IsEnabled = _options.ImportTempo;
        };
        modeCombo.SelectionChanged += (_, _) =>
        {
            _options.TempoMode = modeCombo.SelectedIndex == 1 ? TempoImportMode.Adaptive : TempoImportMode.Replace;
        };

        var tempoPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        tempoPanel.Children.Add(tempoImportCheck);
        tempoPanel.Children.Add(modeCombo);
        content.Children.Add(tempoPanel);

        // Buttons
        var okBtn = new Button { Content = "Import", Classes = { "primary" }, Height = 26, Width = 90, HorizontalContentAlignment = HorizontalAlignment.Center, FontWeight = FontWeight.SemiBold };
        var cancelBtn = new Button { Content = "Cancel", Classes = { "cell" }, Height = 26, Width = 90, HorizontalContentAlignment = HorizontalAlignment.Center };

        okBtn.Click += (_, _) => Close(true);
        cancelBtn.Click += (_, _) => Close(false);

        var bottomButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 8,
            Margin = new Avalonia.Thickness(0, 16, 0, 0),
            Children = { cancelBtn, okBtn }
        };
        content.Children.Add(bottomButtons);

        SetBody(content);
    }
}
