// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Surface feedback by difference (specs/051, FR-023, research R11): what the surface should show
// is compared with what it was last sent, and only the changes become MIDI — so a quiet session
// sends nothing, and SC-004's latency holds at the UI tick. A full refresh (on connect, bank
// change, project load) sends everything. A fader whose touch sensor is held is never driven
// (FR-024): the hand on it wins, and it gets its position once released.

namespace Nota.Application.Controllers.Mcu;

public sealed class FeedbackDiffer(ISurfaceCodec codec)
{
    private SurfaceState? _sent;

    /// <summary>Forget what was sent: the next <see cref="Diff"/> sends everything.</summary>
    public void Invalidate() => _sent = null;

    /// <summary>The messages that bring the surface from what was sent to <paramref name="want"/>.
    /// <paramref name="touched"/>: strips 0–7 and 8 (master) whose fader is held.</summary>
    public List<byte[]> Diff(SurfaceState want, IReadOnlyList<bool> touched)
    {
        var o = new List<byte[]>();
        var was = _sent;
        bool full = was is null;
        for (int s = 0; s < SurfaceState.Strips; s++)
        {
            var w = want.Strip[s];
            var p = full ? default : was!.Strip[s];
            bool held = s < touched.Count && touched[s];
            if ((full || w.Fader != p.Fader) && !held) o.AddRange(codec.Fader(s, w.Fader));
            if (full || w.PanRing != p.PanRing) o.AddRange(codec.Ring(s, w.PanRing));
            if (full || w.Arm != p.Arm) o.AddRange(codec.StripLed(StripLight.Rec, s, w.Arm));
            if (full || w.Solo != p.Solo) o.AddRange(codec.StripLed(StripLight.Solo, s, w.Solo));
            if (full || w.Mute != p.Mute) o.AddRange(codec.StripLed(StripLight.Mute, s, w.Mute));
            if (full || w.Select != p.Select) o.AddRange(codec.StripLed(StripLight.Select, s, w.Select));
            if (full || w.Name != p.Name) o.AddRange(codec.Name(s, w.Name));
            // Surfaces let a meter fall by themselves: send a level while there is one.
            if (w.Meter > 0 || (!full && p.Meter > 0)) o.AddRange(codec.Meter(s, w.Meter));
        }
        bool masterHeld = touched.Count > 8 && touched[8];
        if ((full || want.MasterFader != was!.MasterFader) && !masterHeld) o.AddRange(codec.Fader(8, want.MasterFader));
        if (full || want.Play != was!.Play) o.AddRange(codec.TransportLed(TransportLight.Play, want.Play));
        if (full || want.Stop != was!.Stop) o.AddRange(codec.TransportLed(TransportLight.Stop, want.Stop));
        if (full || want.Record != was!.Record) o.AddRange(codec.TransportLed(TransportLight.Record, want.Record));
        if (full || want.Loop != was!.Loop) o.AddRange(codec.TransportLed(TransportLight.Loop, want.Loop));
        if (full || want.Time != was!.Time) o.AddRange(codec.Time(want.Time, full ? null : was!.Time));
        if (full || want.Assignment != was!.Assignment) o.AddRange(codec.Assignment(want.Assignment));

        // A held fader keeps its last sent position, so its new one goes out on release.
        var sent = want.Clone();
        for (int s = 0; s < SurfaceState.Strips; s++)
            if (s < touched.Count && touched[s]) sent.Strip[s] = sent.Strip[s] with { Fader = full ? -1 : was!.Strip[s].Fader };
        if (masterHeld) sent.MasterFader = full ? -1 : was!.MasterFader;
        _sent = sent;
        return o;
    }
}
