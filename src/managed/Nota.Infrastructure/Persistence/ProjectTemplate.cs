// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The default new-project content (specs/004-default-project-template): File → New and
// the launch workspace start with an instrument track, an audio track and two returns —
// "Reverb" and "Delay" — so a user can play and send at once. Built from the same
// structural edits as adding tracks by hand, then made the undo baseline so the
// template is the starting point rather than a stack of undoable edits.
// New projects use the Stereo pan law (specs/006-stereo-pan-law); older projects keep Balance.

namespace Nota.Infrastructure;

public static class ProjectTemplate
{
    // Built-in device kinds and their "Wet Only" switch (Reverb.h / Delay.h param order).
    private const int ReverbKind = 2, ReverbWetOnly = 19;
    private const int DelayKind = 3, DelayWetOnly = 22;

    /// <summary>Builds the default new-project content on a freshly reset engine and makes it
    /// the undo baseline. Returns the instrument track id.</summary>
    public static int ApplyDefault(IAudioEngine engine)
    {
        // Same as the toolbar's Add Instrument: Nota Volt with a seed one-bar clip.
        int inst = engine.AddVoltSynthTrack();
        engine.AddMidiClip(inst, 0.0, 4.0);
        engine.AddAudioTrack();

        // Returns are fully wet: a send adds only the effect, the dry signal isn't doubled.
        AddReturn(engine, "Reverb", ReverbKind, ReverbWetOnly);
        AddReturn(engine, "Delay", DelayKind, DelayWetOnly);

        engine.SetPanLaw(PanLaw.Stereo, checkpoint: false);
        engine.ClearUndoHistory();
        return inst;
    }

    private static void AddReturn(IAudioEngine engine, string name, int kind, int wetOnlyParam)
    {
        int track = engine.AddReturnTrack();
        if (track <= 0) return;
        engine.SetTrackName(track, name);
        int dev = engine.AddBuiltinDevice(track, kind);
        if (dev >= 0) engine.DeviceSetParam(track, dev, wetOnlyParam, 1f);
    }
}
