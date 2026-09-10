// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

    // Effects target the selected track, else the last instrument track added.
    private int EffectTarget() => Timeline.SelectedTrackId > 0 ? Timeline.SelectedTrackId : _lastInstrumentTrackId;

    // Applies a preset browser row: factory presets (Path "factory:<id>") resolve through
    // the shipped catalog; user presets load from disk. Instrument presets create a new
    // track (targetTrackId is ignored for them); effect presets target the track.
    private string ApplyPresetItem(BrowserItem item, int targetTrackId)
        => item.Path.StartsWith("factory:", StringComparison.Ordinal)
            ? _factory.Apply(Engine, item.Path["factory:".Length..], targetTrackId)
            : _presets.ApplyFromFile(Engine, item.Path, targetTrackId);

    private void OnBrowserItemActivated(BrowserItem item)
    {
        if (_vm is null) return;
        try
        {
            switch (item.Kind)
            {
                case BrowserItemKind.BuiltinInstrument:
                {
                    int t = item.BuiltinKind switch
                    {
                        4 => Engine.AddDrumRackTrack(),
                        3 => Engine.AddInstrumentRackTrack(),
                        2 => Engine.AddPhysicalSynthTrack(),
                        5 => Engine.AddWavetableSynthTrack(),
                        6 => Engine.AddVoltSynthTrack(),
                        7 => Engine.AddBassSynthTrack(),
                        8 => Engine.AddPendulumSynthTrack(),
                        9 => Engine.AddOperatorSynthTrack(),
                        10 => Engine.AddGrainSynthTrack(),
                        11 => Engine.AddFluxSynthTrack(),
                        12 => Engine.AddRhythmTrack(),
                        13 => Engine.AddMonolithTrack(),
                        14 => Engine.AddPentadTrack(),
                        1 => Engine.AddSamplerInstrumentTrack(),
                        _ => Engine.AddInstrumentTrack(),
                    };
                    Engine.AddMidiClip(t, 0.0, 4.0);
                    if (item.BuiltinKind == 1) ShowDevices(t);   // reveal the Sampler card (drop a sample onto it)
                    _lastInstrumentTrackId = t;
                    Timeline.Refresh();
                    if (item.BuiltinKind is 3 or 4) ShowDevices(t);   // reveal the rack card
                    _vm.StatusText = $"Added {item.Name} (track {t})";
                    break;
                }
                case BrowserItemKind.PluginInstrument:
                {
                    int t = Engine.AddPluginInstrumentTrack(item.CatalogIndex);
                    Engine.AddMidiClip(t, 0.0, 4.0);
                    _lastInstrumentTrackId = t;
                    Timeline.Refresh();
                    _vm.StatusText = $"Added {item.Name} (track {t})";
                    break;
                }
                case BrowserItemKind.BuiltinEffect:
                {
                    int tgt = EffectTarget();
                    if (tgt <= 0) { _vm.StatusText = "Select a track first."; break; }
                    Engine.AddBuiltinDevice(tgt, item.BuiltinKind);
                    Timeline.Refresh();
                    ShowDevices(tgt); // reveal the updated device chain
                    _vm.StatusText = $"Added {item.Name} to track {tgt}";
                    break;
                }
                case BrowserItemKind.BuiltinMidiEffect:
                {
                    int tgt = EffectTarget();
                    if (tgt <= 0) { _vm.StatusText = "Select a track first."; break; }
                    Engine.AddMidiEffect(tgt, item.BuiltinKind);
                    Timeline.Refresh();
                    ShowDevices(tgt); // MIDI effect cards sit before the instrument
                    _vm.StatusText = $"Added {item.Name} to track {tgt}";
                    break;
                }
                case BrowserItemKind.PluginEffect:
                {
                    int tgt = EffectTarget();
                    if (tgt <= 0) { _vm.StatusText = "Select a track first."; break; }
                    if (Engine.AddTrackEffectPlugin(tgt, item.CatalogIndex) < 0)
                    { _vm.StatusText = "Failed to load effect."; break; }
                    Timeline.Refresh();
                    ShowDevices(tgt); // reveal the updated device chain
                    _vm.StatusText = $"Added {item.Name} to track {tgt}";
                    break;
                }
                case BrowserItemKind.Sample:
                {
                    int t = Engine.AddAudioTrack();
                    int c = Engine.AddAudioClip(t, item.Path, 0.0);
                    AutoWarpImported(t, c);
                    Timeline.Refresh();
                    _vm.StatusText = $"Added {item.Name} (track {t})";
                    break;
                }
                case BrowserItemKind.Project:
                    OpenProject(item.Path);
                    return; // OpenProject rebuilds everything itself
                case BrowserItemKind.Preset:
                {
                    string warn = ApplyPresetItem(item, Timeline.SelectedTrackId);
                    Timeline.Refresh();
                    if (Timeline.SelectedTrackId > 0) ShowDevices(Timeline.SelectedTrackId);
                    _vm.StatusText = warn.Length == 0 ? $"Applied preset {item.Name}" : warn;
                    break;
                }
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

    private static void RevealInFileManager(string path)
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

    private void OnBrowserPreview(BrowserItem item)
    {
        if (_vm is null || string.IsNullOrEmpty(item.Path)) return;
        try
        {
            Engine.PreviewFile(item.Path);
            _vm.StatusText = $"Previewing {item.Name}";
        }
        catch (Exception ex)
        {
            _vm.StatusText = $"Preview failed: {ex.Message}";
        }
    }

    private async void OnSavePreset(int deviceIndex)
    {
        if (_vm is null || _deviceChain is null) return;
        int trackId = _deviceChain.TrackId;
        if (trackId <= 0) { _vm.StatusText = "Select a track first."; return; }

        string label = deviceIndex < 0 ? "Instrument" : Engine.DeviceName(trackId, deviceIndex);
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
