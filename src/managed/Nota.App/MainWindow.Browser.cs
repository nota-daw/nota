// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;
using Nota.Presentation;

namespace Nota.App;

public partial class MainWindow
{
    private int _lastInstrumentTrackId;

    // The browser column's width before it was folded to its rail, restored on unfold.
    private GridLength _browserWidth = new(288);

    // Folded, the column hugs the rail and the splitter goes away — there's nothing to resize.
    private void OnBrowserCollapsedChanged(bool collapsed)
    {
        var column = BrowserGrid.ColumnDefinitions[0];
        if (collapsed)
        {
            if (column.Width.IsAbsolute) _browserWidth = column.Width;
            column.MinWidth = 0;
            column.Width = GridLength.Auto;
        }
        else
        {
            column.MinWidth = 160;
            column.Width = _browserWidth;
        }
        BrowserSplitter.IsVisible = !collapsed;
    }

    /// <summary>The fallback target when nothing is selected: the last instrument track added,
    /// but only while it still exists — it may have been deleted, cut or undone since, and
    /// acting on a dead track id silently edits nothing (or re-shows its stale device chain).</summary>
    private int LastInstrumentTrack()
    {
        if (_lastInstrumentTrackId > 0 && TrackIndexOf(_lastInstrumentTrackId) < 0) _lastInstrumentTrackId = 0;
        return _lastInstrumentTrackId;
    }

    // Effects target the selected track, else the last instrument track added.
    private int EffectTarget() => Timeline.SelectedTrackId > 0 ? Timeline.SelectedTrackId : LastInstrumentTrack();

    private HashSet<int> TrackIds()
    {
        var ids = new HashSet<int>();
        for (int i = 0; i < Engine.TrackCount; i++)
            if (Engine.TryGetTrackInfo(i, out var ti)) ids.Add(ti.Id);
        return ids;
    }

    // Double-click / Return on a browser row: instruments start a track, effects go to the
    // selected track (else the last instrument track), presets apply — through the same
    // insert service as drag & drop and the command palette.
    private void OnBrowserItemActivated(BrowserItem item)
    {
        if (_vm is null) return;
        try
        {
            RecordUse(PaletteIdOf(item), fromPalette: false);
            switch (item.Kind)
            {
                case BrowserItemKind.BuiltinInstrument:
                case BrowserItemKind.PluginInstrument:
                {
                    var r = Insertion.Insert(item, new InsertTarget(-1, NewTrack: true));
                    ApplyInsertResult(r, revealDevices: item.BuiltinKind is 1 or 3 or 4 && item.Kind == BrowserItemKind.BuiltinInstrument);
                    break;
                }
                case BrowserItemKind.BuiltinEffect:
                case BrowserItemKind.BuiltinMidiEffect:
                case BrowserItemKind.PluginEffect:
                {
                    int tgt = EffectTarget();
                    if (tgt <= 0) { _vm.StatusText = "Select a track first."; break; }
                    ApplyInsertResult(Insertion.RouteToTrack(item, tgt));
                    break;
                }
                case BrowserItemKind.Sample:
                    _ = ImportAudioInBackgroundAsync(item.Path, -1, 0.0);
                    break;
                case BrowserItemKind.MidiFile:
                    ImportMidiFileToArrangement(item, -1, 0.0);
                    Timeline.Refresh();
                    break;
                case BrowserItemKind.Project:
                    OpenProject(item.Path);
                    return; // OpenProject rebuilds everything itself
                case BrowserItemKind.Preset:
                    ApplyInsertResult(Insertion.ApplyPreset(item, new InsertTarget(Timeline.SelectedTrackId)));
                    break;
            }
            _session?.Refresh();
        }
        catch (Exception ex)
        {
            _vm.StatusText = $"Load failed: {ex.Message}";
        }
    }

    // Reveal a sample / folder / project in the OS file manager (Finder / Explorer / files).
    private void OnBrowserReveal(BrowserItem item)
    {
        if (_vm is null || string.IsNullOrEmpty(item.Path)) return;
        try { RevealInFileManager(item.Path); }
        catch (Exception ex) { _vm.StatusText = $"Reveal failed: {ex.Message}"; }
    }

    internal static void RevealInFileManager(string path)
    {
        if (OperatingSystem.IsMacOS()) Process.Start("open", new[] { "-R", path });
        else if (OperatingSystem.IsWindows()) Process.Start("explorer.exe", $"/select,\"{path}\"");
        else Process.Start("xdg-open", new[] { System.IO.Path.GetDirectoryName(path) ?? path });
    }

    // Delete a project bundle: confirm, then move it to the Trash (recoverable). Falls back
    // to a permanent delete on platforms without a scriptable trash.
    private async void OnBrowserDeleteProject(BrowserItem item)
    {
        if (_vm is null) return;
        bool ok = await new ConfirmWindow("Delete project",
            $"Move “{item.Name}” to the Trash?", "Delete", "Cancel").ShowDialog<bool>(this);
        if (!ok) return;

        bool done = await Task.Run(() => MoveToTrash(item.Path));
        _vm.Browser.RebuildProjects();
        _vm.StatusText = done ? $"Moved {item.Name} to Trash" : $"Couldn't delete {item.Name}";
    }

    private static bool MoveToTrash(string path)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                var psi = new ProcessStartInfo("osascript") { UseShellExecute = false };
                psi.ArgumentList.Add("-e");
                psi.ArgumentList.Add($"tell application \"Finder\" to delete POSIX file \"{path}\"");
                Process.Start(psi)?.WaitForExit();
                return !Directory.Exists(path) && !File.Exists(path);
            }
            // Fallback: permanent delete (Windows/Linux — no portable trash API here).
            if (Directory.Exists(path)) Directory.Delete(path, true);
            else if (File.Exists(path)) File.Delete(path);
            return !Directory.Exists(path) && !File.Exists(path);
        }
        catch { return false; }
    }

    // Open the tag manager. item == null → manage all tags; a device item → create a tag
    // already assigned to that device.
    private async void OnBrowserEditTags(BrowserItem? item)
    {
        var lib = App.Services.GetService<IBrowserLibrary>();
        if (lib is null) return;
        string? key = item is not null && item.LibraryKey.Length > 0 ? item.LibraryKey : null;
        await new TagEditorWindow(lib, key).ShowDialog(this);
    }

    private async void OnSavePreset(int deviceIndex)
    {
        if (_vm is null || _deviceChain is null) return;
        int trackId = _deviceChain.TrackId;
        if (trackId <= 0) { _vm.StatusText = "Select a track first."; return; }

        string label = Engine.DeviceName(trackId, deviceIndex);
        if (string.IsNullOrEmpty(label)) label = "Instrument";
        var name = await new TextPromptWindow("Save preset", "Preset name", label).ShowDialog<string?>(this);
        if (string.IsNullOrEmpty(name)) return;

        try
        {
            if (!_presets.Save(Engine, trackId, deviceIndex, name, _vm.Settings.PresetsFolder()))
            { _vm.StatusText = "Nothing to save (built-in synth has no state)."; return; }
            _vm.Browser.RebuildPresets();
            _vm.StatusText = $"Saved preset {name}";
        }
        catch (Exception ex)
        {
            _vm.StatusText = $"Save preset failed: {ex.Message}";
        }
    }

    // Save a rack chain's instrument (hosted plugins only) as a preset.
    private async void OnSaveRackChainPreset(int chain)
    {
        if (_vm is null || _deviceChain is null) return;
        int trackId = _deviceChain.TrackId;
        if (trackId <= 0) return;

        string label = Engine.RackChainInstrumentName(trackId, chain);
        if (string.IsNullOrEmpty(label)) label = "Instrument";
        var name = await new TextPromptWindow("Save preset", "Preset name", label).ShowDialog<string?>(this);
        if (string.IsNullOrEmpty(name)) return;

        try
        {
            if (!_presets.SaveRackChainInstrument(Engine, trackId, chain, name, _vm.Settings.PresetsFolder()))
            { _vm.StatusText = "Only plug-in chain instruments can be saved as presets."; return; }
            _vm.Browser.RebuildPresets();
            _vm.StatusText = $"Saved preset {name}";
        }
        catch (Exception ex)
        {
            _vm.StatusText = $"Save preset failed: {ex.Message}";
        }
    }
}
