// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Bypassing one Session clip's follow actions (#499): the clip keeps looping while its A/B,
// chances, time and Jump stay as set; other clips' follow actions still run; a launch by hand
// still works; turned back on, the action fires at the end of the follow time then running,
// never at once for a time that ran out while it was bypassed; the bypass is saved with the
// project (format 33), and an older project opens with nothing bypassed.

using System.Text.Json.Nodes;
using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class FollowBypassTests
{
    public static IEnumerable<(bool, string)> Run()
    {
        foreach (var r in BypassHoldsTheClip()) yield return r;
        foreach (var r in NoCatchUpAfterBypass()) yield return r;
        foreach (var r in SavedWithTheProject()) yield return r;
    }

    // Render until the transport has moved `beats` (the offline rate isn't the device's).
    private static void Beats(NotaEngine e, double beats)
    {
        var pb = new float[256 * 2];
        double from = e.PositionBeats;
        for (int guard = 0; guard < 20000 && e.PositionBeats - from < beats; guard++) { e.RenderOffline(pb, 256); e.Poll(); }
    }

    // A track with two one-bar clips; the first moves to the next after one beat.
    private static int FollowTrack(NotaEngine e)
    {
        int t = e.AddInstrumentTrack();
        for (int s = 0; s < 2; s++)
        {
            e.AddSessionMidiClip(t, s, 4.0);
            e.SetSessionNotes(t, s, new[] { new NotaNote(60 + s * 4, 0.0, 1.0, 0.9f) });
        }
        e.TryGetSessionClipProps(t, 0, out var p);
        p.FollowA = (int)SessionFollowAction.Next;
        p.ChanceA = 100;
        p.ChanceB = 0;
        p.FollowBeats = 1.0;
        e.SetSessionClipProps(t, 0, p);
        return t;
    }

    private static void Bypass(NotaEngine e, int t, bool on)
    {
        e.TryGetSessionClipProps(t, 0, out var p);
        p.FollowBypass = on ? 1 : 0;
        e.SetSessionClipProps(t, 0, p);
    }

    private static NotaEngine Rolling()
    {
        var e = new NotaEngine();
        e.SetBpm(120);
        e.SetMetronome(false);
        e.SetLaunchQuant(0.0);
        e.SessionFollow = true;
        return e;
    }

    private static IEnumerable<(bool, string)> BypassHoldsTheClip()
    {
        using var e = Rolling();
        int held = FollowTrack(e), other = FollowTrack(e);
        Bypass(e, held, true);
        yield return (e.TryGetSessionClipProps(held, 0, out var p) && p.FollowBypass == 1 && p.FollowA == (int)SessionFollowAction.Next
                      && p.ChanceA == 100 && Math.Abs(p.FollowBeats - 1.0) < 1e-9,
            "a bypassed clip keeps its follow action, chance and time");
        e.Seek(0);
        e.LaunchScene(0);
        Beats(e, 2.5);
        yield return (e.SessionPlayingSlot(held) == 0, $"a bypassed clip keeps playing past its follow time (playing {e.SessionPlayingSlot(held)})");
        yield return (e.SessionPlayingSlot(other) == 1, $"another clip's follow action still runs (playing {e.SessionPlayingSlot(other)})");
        e.LaunchSlot(held, 1);
        Beats(e, 0.25);
        yield return (e.SessionPlayingSlot(held) == 1, "a bypassed clip's track still launches by hand");
        e.StopAllSession();
        e.StopTransport();
    }

    // Two follow times pass while bypassed; turned back on half-way through the third, nothing
    // fires until that one ends.
    private static IEnumerable<(bool, string)> NoCatchUpAfterBypass()
    {
        using var e = Rolling();
        int t = FollowTrack(e);
        Bypass(e, t, true);
        e.Seek(0);
        e.LaunchSlot(t, 0);
        Beats(e, 2.5);
        Bypass(e, t, false);
        Beats(e, 0.3);
        yield return (e.SessionPlayingSlot(t) == 0,
            $"turning the bypass off doesn't fire a follow time that ran out while bypassed (playing {e.SessionPlayingSlot(t)})");
        Beats(e, 0.5);
        yield return (e.SessionPlayingSlot(t) == 1,
            $"turned back on, the follow action fires at the end of the follow time then running (playing {e.SessionPlayingSlot(t)})");
        e.StopAllSession();
        e.StopTransport();
    }

    private static IEnumerable<(bool, string)> SavedWithTheProject()
    {
        string bundle = Path.Combine(Path.GetTempPath(), $"nota-follow-bypass-{Guid.NewGuid():N}.nota");
        try
        {
            {
                using var e = Rolling();
                int t = FollowTrack(e);
                Bypass(e, t, true);
                ProjectService.Save(ProjectService.Capture(e, new TransportState(120.0, 1.0, false, false), new List<string>()), bundle, e);
            }
            {
                using var e = new NotaEngine();
                ProjectService.Apply(ProjectService.Load(bundle), e, bundle);
                int t = e.TryGetTrackInfo(0, out var ti) ? ti.Id : -1;
                yield return (e.TryGetSessionClipProps(t, 0, out var p) && p.FollowBypass == 1 && p.FollowA == (int)SessionFollowAction.Next,
                    "a clip's follow bypass and its follow action survive save → reopen");
                yield return (e.TryGetSessionClipProps(t, 1, out var q) && q.FollowBypass == 0, "a clip not bypassed reopens not bypassed");
            }
            // A v32 project: no slot is bypassed.
            string manifest = Path.Combine(bundle, ProjectService.ManifestName);
            var node = JsonNode.Parse(File.ReadAllText(manifest))!;
            node["formatVersion"] = 32;
            foreach (var track in node["tracks"]!.AsArray())
                foreach (var slot in track!["sessionSlots"]?.AsArray() ?? new JsonArray())
                    slot!.AsObject().Remove("followBypass");
            File.WriteAllText(manifest, node.ToJsonString());
            {
                using var e = new NotaEngine();
                var doc = ProjectService.Load(bundle);
                ProjectService.Apply(doc, e, bundle);
                int t = e.TryGetTrackInfo(0, out var ti) ? ti.Id : -1;
                yield return (doc.FormatVersion == ProjectService.CurrentFormatVersion && e.TryGetSessionClipProps(t, 0, out var p)
                              && p.FollowBypass == 0 && p.FollowA == (int)SessionFollowAction.Next,
                    "a v32 project opens with its follow actions not bypassed");
            }
        }
        finally { try { Directory.Delete(bundle, true); } catch { } }
    }
}
