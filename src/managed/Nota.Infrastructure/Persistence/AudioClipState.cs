// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Everything that makes an audio clip play the way it does — region, gain, pitch, reverse,
// warp (mode, markers, trim), clip envelopes, ADSR — read off the engine and put back onto a
// new clip. The project file saves and loads clips through it, and Separate Stems uses it to
// give each stem a clip shaped exactly like the one it came from, over a different file.

namespace Nota.Infrastructure;

internal static class AudioClipState
{
    /// <summary>The clip's state; <see cref="AudioClipDto.Sample"/> is left for the caller.</summary>
    public static AudioClipDto Capture(IAudioEngine engine, int trackId, int clipIndex, NotaAudioClipInfo ac)
    {
        var dto = new AudioClipDto
        {
            Name = engine.GetClipName(trackId, clipIndex) is { Length: > 0 } an ? an : null,
            Active = !engine.TryGetClipInfo(trackId, clipIndex, out var gci) || gci.IsActive,
            StartBeat = ac.StartBeat,
            SourceOffsetFrames = ac.SourceOffsetFrames,
            LengthFrames = ac.LengthFrames,
            Gain = ac.Gain,
            PitchSemitones = ac.PitchSemitones,
            WarpEnabled = ac.WarpEnabled,
            WarpMode = ac.WarpMode,
            WarpBeats = ac.WarpBeats,
            WarpPlayStart = ac.WarpPlayStart,
            WarpPlayEnd = ac.WarpPlayEnd,
            Reversed = ac.Reversed != 0,
        };
        if (ac.WarpEnabled != 0)
        {
            var ms = new double[128]; var mb = new double[128];
            int nm = Math.Min(engine.GetClipWarpMarkers(trackId, clipIndex, ms, mb), 128);
            if (nm >= 2)
            {
                dto.WarpMarkers = new List<WarpMarkerDto>(nm);
                for (int k = 0; k < nm; k++) dto.WarpMarkers.Add(new WarpMarkerDto { Src = ms[k], Beat = mb[k] });
            }
        }
        var env = engine.GetClipVolumeEnvelope(trackId, clipIndex);   // clip volume envelope (v8)
        if (env.Length > 0) dto.VolumeEnvelope = Array.ConvertAll(env, p => new AutomationPointDto(p));
        var penv = engine.GetClipPanEnvelope(trackId, clipIndex);     // clip pan envelope (v9)
        if (penv.Length > 0) dto.PanEnvelope = Array.ConvertAll(penv, p => new AutomationPointDto(p));
        var adsr = engine.GetClipAdsr(trackId, clipIndex);            // ADSR (v20); identity is omitted
        if (!adsr.IsIdentity) dto.Adsr = new ClipAdsrDto(adsr);
        return dto;
    }

    /// <summary>Adds a clip playing <paramref name="path"/> shaped as <paramref name="ac"/> on
    /// <paramref name="trackId"/>. Returns its index, or -1 when the file can't be loaded.</summary>
    public static int Restore(IAudioEngine engine, int trackId, AudioClipDto ac, string path)
    {
        int ci = engine.AddAudioClipEx(trackId, path, ac.StartBeat, ac.SourceOffsetFrames, ac.LengthFrames, ac.Gain);
        if (ci < 0) return -1;
        if (ac.Name is { Length: > 0 } acn) engine.SetClipName(trackId, ci, acn);
        if (!ac.Active) engine.SetClipActive(trackId, ci, false);   // clip deactivate (v17)
        ApplyShape(engine, trackId, ci, ac);
        return ci;
    }

    /// <summary>Pitch, reverse, warp, envelopes and ADSR onto an existing clip — an
    /// arrangement clip, or a Session slot's take addressed by <see cref="SessionClip.Index"/>.</summary>
    public static void ApplyShape(IAudioEngine engine, int trackId, int ci, AudioClipDto ac)
    {
        if (ac.PitchSemitones != 0) engine.SetClipPitch(trackId, ci, ac.PitchSemitones);
        if (ac.Reversed) engine.SetClipReverse(trackId, ci, true);   // reverse (v19)
        if (ac.WarpEnabled != 0)
        {
            engine.SetClipWarp(trackId, ci, true, ac.WarpMode);
            if (ac.WarpMarkers is { Count: >= 2 } wm)
            {
                var ms = new double[wm.Count]; var mb = new double[wm.Count];
                for (int k = 0; k < wm.Count; k++) { ms[k] = wm[k].Src; mb[k] = wm[k].Beat; }
                engine.SetClipWarpMarkers(trackId, ci, ms, mb);   // also sets warpBeats from the last marker
            }
            else if (ac.WarpBeats > 0) engine.SetClipWarpLength(trackId, ci, ac.WarpBeats);
            if (ac.WarpPlayEnd > 0)   // restore the trim window (v11); 0 = whole warp
                engine.SetClipWarpTrim(trackId, ci, ac.WarpPlayStart, ac.WarpPlayEnd);
        }
        if (ac.VolumeEnvelope is { Length: > 0 } venv)   // clip volume envelope (v8)
            engine.SetClipVolumeEnvelope(trackId, ci, Array.ConvertAll(venv, p => p.ToPoint()));
        if (ac.PanEnvelope is { Length: > 0 } penv)      // clip pan envelope (v9)
            engine.SetClipPanEnvelope(trackId, ci, Array.ConvertAll(penv, p => p.ToPoint()));
        if (ac.Adsr is { } adsr)                          // ADSR (v20)
            engine.SetClipAdsr(trackId, ci, adsr.ToAdsr());
    }
}
