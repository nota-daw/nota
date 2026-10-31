// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Applying a preset to a device as one undo step that also tags the device with it
// (specs/048-preset-state): undo brings back the previous sound and the previous name.

using System;

namespace Nota.Application.Editing;

public static class PresetEdit
{
    /// <summary>Runs <paramref name="apply"/> (it sets the device's parameters) and tags the
    /// device with the preset, as one undo step. <paramref name="presetId"/> "" = Init (no tag).
    /// A preset that didn't apply (a warning back) leaves the tag as it was.
    /// Returns what <paramref name="apply"/> returned (a warning, "" when fine).</summary>
    public static string Apply(IAudioEngine engine, int trackId, PresetSurface surface, int index,
        string presetId, string name, Func<string> apply)
    {
        int n = surface switch
        {
            PresetSurface.Instrument => engine.PluginParamCount(trackId, -1),
            PresetSurface.MidiEffect => engine.MidiEffectParamCount(trackId, index),
            _ => engine.DeviceParamCount(trackId, index),
        };
        var targets = new MixTarget[Math.Max(0, n) + 1];
        for (int i = 0; i < n; i++)
            targets[i] = surface switch
            {
                PresetSurface.Instrument => MixTarget.PluginParam(trackId, -1, i),
                PresetSurface.MidiEffect => MixTarget.MidiDeviceParam(trackId, index, i),
                _ => MixTarget.DeviceParam(trackId, index, i),
            };
        targets[^1] = MixTarget.PresetTag(trackId, surface, index);
        string warn = "";
        // A group around the gesture: what apply() changes beside the parameters (an EQ-8's band
        // labels, each its own undo entry) undoes with them however few parameters there are.
        engine.BeginUndoGroup();
        try
        {
            MixEdit.Step(engine, () =>
            {
                warn = apply();
                if (warn.Length == 0) engine.SetDevicePresetTag(trackId, surface, index, presetId, name);
            }, targets);
        }
        finally { engine.EndUndoGroup(); }
        return warn;
    }

    /// <summary>Tags a device that a preset just landed on (a new track or device), as an undo step.</summary>
    public static void Tag(IAudioEngine engine, int trackId, PresetSurface surface, int index, string presetId, string name)
        => MixEdit.Step(engine, () => engine.SetDevicePresetTag(trackId, surface, index, presetId, name),
            MixTarget.PresetTag(trackId, surface, index));

    /// <summary>The device's preset name, "" with no tag (Init) or no such device.</summary>
    public static string Name(IAudioEngine engine, int trackId, PresetSurface surface, int index)
        => engine.DevicePresetTag(trackId, surface, index) is var t and > 0 ? engine.PresetTagInfo(t)?.Name ?? "" : "";
}
