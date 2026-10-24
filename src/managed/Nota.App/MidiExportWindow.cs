// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nota.App;

public sealed class MidiExportOptions
{
    public string ExportDirectory { get; set; } = "";
    public string FileName { get; set; } = "Nota MIDI.mid";
    public int Format { get; set; } = 1; // 0 = single track, 1 = multitrack
    public HashSet<int> SelectedTrackIds { get; } = new();
    public HashSet<int> SelectedControllers { get; } = new(Enumerable.Range(0, 128));
    public bool ExportTempo { get; set; } = true;
    public bool ExportTracksAsSeparateFiles { get; set; } = false;
}

public sealed record MidiExportTrack(int Id, string Name);

public sealed class MidiExportWindow : NotaWindow
{
    private readonly MidiExportOptions _options = new();
    public MidiExportOptions Options => _options;

    public MidiExportWindow(string defaultPath, string defaultFileName, IReadOnlyList<MidiExportTrack>? midiTracks = null)
    {
        Title = "Export MIDI";
        CanResize = false;
        CanMinimize = false;
        CanMaximize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _options.ExportDirectory = defaultPath;
        _options.FileName = defaultFileName;
        if (midiTracks is not null)
            foreach (var track in midiTracks) _options.SelectedTrackIds.Add(track.Id);

        var content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 14,
            Width = 440,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        // 1. Destination
        content.Children.Add(new TextBlock { Text = "Export file path", Classes = { "SectionLabel" }, HorizontalAlignment = HorizontalAlignment.Center });
        var pathGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var pathBox = new TextBox
        {
            Text = Path.Combine(defaultPath, defaultFileName),
            IsReadOnly = true,
            Focusable = false,
            VerticalAlignment = VerticalAlignment.Center,
            Height = 28,
            Padding = new Avalonia.Thickness(6, 4),
            Cursor = new Cursor(StandardCursorType.Arrow),
        };
        ToolTip.SetTip(pathBox, "Non-editable. Click 'Select path' to choose location.");
        var btnSelectPath = new Button { Content = "Select path", Classes = { "cell" }, Height = 28, Margin = new Avalonia.Thickness(8, 0, 0, 0) };
        btnSelectPath.Click += async (_, _) =>
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is null) return;
            if (_options.ExportTracksAsSeparateFiles)
            {
                var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
                {
                    Title = "Select Folder for Exported MIDI Files"
                });
                if (folders.Count > 0 && folders[0] is { } folder)
                {
                    string localPath = folder.Path.LocalPath;
                    _options.ExportDirectory = localPath;
                    pathBox.Text = localPath;
                }
            }
            else
            {
                var file = await topLevel.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
                {
                    Title = "Select Export MIDI File Path",
                    SuggestedFileName = _options.FileName,
                    DefaultExtension = "mid"
                });
                if (file is not null)
                {
                    string localPath = file.Path.LocalPath;
                    pathBox.Text = localPath;
                    var dir = Path.GetDirectoryName(localPath);
                    var name = Path.GetFileName(localPath);
                    if (!string.IsNullOrEmpty(dir)) _options.ExportDirectory = dir;
                    if (!string.IsNullOrEmpty(name)) _options.FileName = name;
                }
            }
        };
        Grid.SetColumn(pathBox, 0);
        Grid.SetColumn(btnSelectPath, 1);
        pathGrid.Children.Add(pathBox);
        pathGrid.Children.Add(btnSelectPath);
        content.Children.Add(pathGrid);

        // 2. Format
        content.Children.Add(new TextBlock { Text = "MIDI format", Classes = { "SectionLabel" }, HorizontalAlignment = HorizontalAlignment.Center });
        var formatCombo = new ComboBox
        {
            ItemsSource = new[] { "Format 0 (Single track)", "Format 1 (Multitrack)" },
            SelectedIndex = 1,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Height = 28
        };
        formatCombo.SelectionChanged += (_, _) => _options.Format = formatCombo.SelectedIndex;
        content.Children.Add(formatCombo);

        if (midiTracks is { Count: > 0 })
        {
            content.Children.Add(new TextBlock { Text = "Project tracks", Classes = { "SectionLabel" }, HorizontalAlignment = HorizontalAlignment.Center });
            var trackHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
            var tracksAll = new Button { Content = "Select all", Classes = { "cell" }, Height = 26 };
            var tracksNone = new Button { Content = "Select none", Classes = { "cell" }, Height = 26 };
            trackHeader.Children.Add(tracksAll);
            trackHeader.Children.Add(tracksNone);
            content.Children.Add(trackHeader);

            var trackPanel = new StackPanel { Spacing = 4 };
            var trackChecks = new List<(MidiExportTrack Track, CheckBox CheckBox)>();
            foreach (var track in midiTracks)
            {
                var check = new CheckBox
                {
                    Content = track.Name,
                    IsChecked = true,
                    MinHeight = 24,
                    FontSize = 12
                };
                check.Click += (_, _) =>
                {
                    if (check.IsChecked == true) _options.SelectedTrackIds.Add(track.Id);
                    else _options.SelectedTrackIds.Remove(track.Id);
                };
                trackChecks.Add((track, check));
                trackPanel.Children.Add(check);
            }
            var trackScroll = new ScrollViewer
            {
                Height = 110,
                Content = trackPanel,
                HorizontalAlignment = HorizontalAlignment.Center,
                Width = 400
            };
            content.Children.Add(trackScroll);

            tracksAll.Click += (_, _) =>
            {
                foreach (var (track, check) in trackChecks)
                {
                    _options.SelectedTrackIds.Add(track.Id);
                    check.IsChecked = true;
                }
            };
            tracksNone.Click += (_, _) =>
            {
                _options.SelectedTrackIds.Clear();
                foreach (var (_, check) in trackChecks) check.IsChecked = false;
            };
        }

        // 3. CC Automation Filter
        content.Children.Add(new TextBlock { Text = "MIDI CC automation filter", Classes = { "SectionLabel" }, HorizontalAlignment = HorizontalAlignment.Center });
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
                IsChecked = true,
                MinHeight = 24,
                FontSize = 12
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
            Height = 140,
            Content = ccPanel,
            HorizontalAlignment = HorizontalAlignment.Center,
            Width = 400
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

        // 4. Options
        content.Children.Add(new TextBlock { Text = "Options", Classes = { "SectionLabel" }, HorizontalAlignment = HorizontalAlignment.Center });
        var optionsPanel = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        var tempoCheck = new CheckBox { Content = "Export tempo automation", IsChecked = true, MinHeight = 26, FontSize = 12 };
        tempoCheck.Click += (_, _) => _options.ExportTempo = tempoCheck.IsChecked == true;
        optionsPanel.Children.Add(tempoCheck);

        var separateCheck = new CheckBox { Content = "Export tracks as separate MIDI files", IsChecked = false, MinHeight = 26, FontSize = 12 };
        separateCheck.Click += (_, _) =>
        {
            _options.ExportTracksAsSeparateFiles = separateCheck.IsChecked == true;
            if (_options.ExportTracksAsSeparateFiles)
                pathBox.Text = _options.ExportDirectory;
            else
                pathBox.Text = Path.Combine(_options.ExportDirectory, _options.FileName);
        };
        optionsPanel.Children.Add(separateCheck);
        content.Children.Add(optionsPanel);

        // Buttons
        var okBtn = new Button { Content = "Export", Classes = { "primary" }, Height = 28, Width = 90, HorizontalContentAlignment = HorizontalAlignment.Center, FontWeight = FontWeight.SemiBold };
        var cancelBtn = new Button { Content = "Cancel", Classes = { "cell" }, Height = 28, Width = 90, HorizontalContentAlignment = HorizontalAlignment.Center };

        okBtn.Click += (_, _) => Close(true);
        cancelBtn.Click += (_, _) => Close(false);

        var bottomButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 12,
            Margin = new Avalonia.Thickness(0, 16, 0, 0),
            Children = { cancelBtn, okBtn }
        };
        content.Children.Add(bottomButtons);

        SetBody(content);
    }
}
