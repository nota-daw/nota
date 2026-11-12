// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Settings → Library → Sample folders (spec 092, #346): the list the browser's Files tab
// shows, in place of the single Samples-folder picker. Built like the plug-in scan folders
// list (a well of selectable rows over a row of buttons); folders can also be dropped onto
// the well from the OS file manager. The list re-renders whenever it changes, here or from
// the browser.

using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Nota.Application;
using Nota.Application.Localization;

namespace Nota.App;

public sealed partial class PreferencesWindow
{
    private int _selSampleFolder;

    private Control SampleFoldersSection(ISettingsService settings)
    {
        var list = new StackPanel();
        var well = new Border
        {
            Background = Sunken, BorderBrush = Divider, BorderThickness = new Thickness(1),
            CornerRadius = NotaRadius.Group, Padding = new Thickness(4), MinHeight = 96, Child = list,
        };
        DragDrop.SetAllowDrop(well, true);
        var note = new TextBlock { FontSize = 10, Foreground = TextTertiary, TextWrapping = TextWrapping.Wrap, IsVisible = false };

        var addBtn = Btn(Loc.T("preferences.add_folder"));
        var defaultBtn = Btn(Loc.T("browser.set_as_default"));
        var renameBtn = Btn(Loc.T("common.rename"));
        var showBtn = Btn(Loc.T("common.show"));
        var removeBtn = Btn(Loc.T("preferences.remove"));

        void Note(string? text) { note.Text = text ?? ""; note.IsVisible = !string.IsNullOrEmpty(text); }

        void Render()
        {
            var folders = settings.ResolvedSampleFolders();
            list.Children.Clear();
            if (_selSampleFolder >= folders.Count) _selSampleFolder = folders.Count - 1;
            if (_selSampleFolder < 0) _selSampleFolder = 0;
            foreach (var f in folders)
            {
                bool sel = f.Index == _selSampleFolder;
                var name = new TextBlock
                {
                    Text = f.Label, FontSize = 11, FontWeight = FontWeight.SemiBold,
                    Foreground = f.Available ? TextPrimary : TextTertiary,
                    VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                };
                var path = new TextBlock
                {
                    Text = f.Path, FontSize = 10, FontFamily = NotaFonts.UiFamily, Foreground = sel ? TextSecondary : TextTertiary,
                    VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                };
                string tagText = (f.IsDefault, f.Available) switch
                {
                    (true, true) => Loc.T("preferences.default"),
                    (true, false) => Loc.T("preferences.default_unavailable"),
                    (false, false) => Loc.T("browser.unavailable"),
                    _ => "",
                };
                var tag = new TextBlock { Text = tagText, FontSize = 9, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center };
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10 };
                grid.Children.Add(name);
                Grid.SetColumn(path, 1); grid.Children.Add(path);
                Grid.SetColumn(tag, 2); grid.Children.Add(tag);
                int idx = f.Index;
                var row = new Border
                {
                    Height = 28, Padding = new Thickness(8, 0), CornerRadius = NotaRadius.Field,
                    Background = sel ? AccentSubtle : Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = grid,
                };
                ToolTip.SetTip(row, f.Path);
                row.PointerPressed += (_, _) => { _selSampleFolder = idx; Render(); };
                list.Children.Add(row);
            }
            var cur = folders.Count > 0 ? folders[_selSampleFolder] : null;
            removeBtn.IsEnabled = cur is not null && folders.Count > 1;
            defaultBtn.IsEnabled = cur is { IsDefault: false, Available: true };
            renameBtn.IsEnabled = cur is not null;
            showBtn.IsEnabled = cur is { Available: true };
        }

        ResolvedSampleFolder? Selected()
        {
            var folders = settings.ResolvedSampleFolders();
            return _selSampleFolder >= 0 && _selSampleFolder < folders.Count ? folders[_selSampleFolder] : null;
        }

        addBtn.Click += async (_, _) =>
        {
            Activate();
            var picked = await this.PickFoldersAsync(new FolderPickerOpenOptions { Title = Loc.T("preferences.add_sample_folder"), AllowMultiple = true });
            var paths = picked.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
            if (paths.Count == 0) return;
            Note(SampleFolderUi.AddAll(settings, paths));
            _selSampleFolder = settings.ResolvedSampleFolders().Count - 1;
            Render();
        };
        defaultBtn.Click += (_, _) => { if (Selected() is { } f) settings.SetDefaultSampleFolder(f.Index); };
        renameBtn.Click += async (_, _) => { if (Selected() is { } f) await SampleFolderUi.RenameAsync(this, settings, f); };
        showBtn.Click += (_, _) => { if (Selected() is { } f) { try { MainWindow.RevealInFileManager(f.Path); } catch { /* no file manager */ } } };
        removeBtn.Click += async (_, _) => { if (Selected() is { } f) await SampleFolderUi.RemoveAsync(this, settings, f); };

        well.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = BrowserView.ExternalPaths(e).Any(System.IO.Directory.Exists) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        });
        well.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            var dirs = BrowserView.ExternalPaths(e).Where(System.IO.Directory.Exists).ToList();
            if (dirs.Count == 0) return;
            Note(SampleFolderUi.AddAll(settings, dirs));
            e.Handled = true;
        });

        // The list can change from the browser too (Add folder…, a dropped folder, Remove).
        Action changed = () => Avalonia.Threading.Dispatcher.UIThread.Post(Render);
        well.AttachedToVisualTree += (_, _) => { settings.SampleFoldersChanged -= changed; settings.SampleFoldersChanged += changed; Render(); };
        well.DetachedFromVisualTree += (_, _) => settings.SampleFoldersChanged -= changed;
        Render();

        return Section(Loc.T("preferences.sample_folders"), 10,
            well,
            new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8, Children = { addBtn, defaultBtn, renameBtn, showBtn, removeBtn } },
            note,
            Caption(Loc.T("preferences.sample_folders_caption")));
    }
}
