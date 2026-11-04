// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Group Link (specs/076, #184): the group flag through the C ABI (undo, copy, save / load),
// the rule that decides which tracks a linked group ties together and which clips match, and
// arming a linked group from one arm button.

using Nota.Application;
using Nota.Application.Editing;
using Nota.Infrastructure;
using Nota.Presentation;

namespace Nota.SmokeTest;

internal static class GroupLinkTests
{
    public static IEnumerable<(bool, string)> Run()
    {
        foreach (var r in Flag()) yield return r;
        foreach (var r in Persisted()) yield return r;
        foreach (var r in Sets()) yield return r;
        foreach (var r in Matching()) yield return r;
        foreach (var r in LinkedArm()) yield return r;
    }

    private static IEnumerable<(bool, string)> Flag()
    {
        using var e = new NotaEngine();
        int a = e.AddAudioTrack(), b = e.AddAudioTrack();
        int g = e.CreateGroup(new[] { a, b });
        yield return (g > 0 && !e.GetTrackEditLink(g), "a new group starts unlinked");
        yield return (e.SetTrackEditLink(g, true) && e.GetTrackEditLink(g), "Link turns on on a group");
        yield return (!e.SetTrackEditLink(a, true) && !e.GetTrackEditLink(a), "Link is refused on an audio track");
        yield return (!e.SetTrackEditLink(99999, true) && !e.GetTrackEditLink(99999), "Link is refused on an unknown track");
        // Setting the same value again adds no undo step: one undo turns it off.
        e.SetTrackEditLink(g, true);
        yield return (e.Undo() && !e.GetTrackEditLink(g), "Link is one undo step (and a repeat set adds none)");
        yield return (e.Redo() && e.GetTrackEditLink(g), "redo turns Link back on");
        int[] copies = e.DuplicateTracks(new[] { g });
        int copy = copies.FirstOrDefault(id => e.GetTrackEditLink(id));
        yield return (copy > 0 && copy != g, "a duplicated group keeps its Link");
    }

    private static IEnumerable<(bool, string)> Persisted()
    {
        using var src = new NotaEngine();
        int a = src.AddAudioTrack(), b = src.AddAudioTrack();
        int g = src.CreateGroup(new[] { a, b });
        src.SetTrackEditLink(g, true);
        var doc = ProjectService.Capture(src, new TransportState(120.0, 1.0, false, false), new List<string>());
        yield return (doc.Tracks.Count(t => t.EditLink == true) == 1 && doc.Tracks.Count(t => t.EditLink is not null) == 1,
            "only the linked group writes EditLink");
        string dir = Path.Combine(Path.GetTempPath(), "nota-smoke-link-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using (var dst = new NotaEngine())
            {
                ProjectService.Apply(doc, dst, dir);
                yield return (Groups(dst).Count(dst.GetTrackEditLink) == 1, "Link survives save and load");
            }
            foreach (var t in doc.Tracks) t.EditLink = null;   // as written before specs/076
            using (var old = new NotaEngine())
            {
                ProjectService.Apply(doc, old, dir);
                yield return (Groups(old).Count > 0 && !Groups(old).Any(old.GetTrackEditLink), "an older project opens unlinked");
            }
        }
        finally { Directory.Delete(dir, true); }
    }

    private static List<int> Groups(NotaEngine e)
    {
        var ids = new List<int>();
        for (int i = 0; i < e.TrackCount; i++) if (e.TryGetTrackInfo(i, out var t) && t.IsGroup) ids.Add(t.Id);
        return ids;
    }

    private static IEnumerable<(bool, string)> Sets()
    {
        // outer(1) ⊃ {A(2), inner(3) ⊃ {B(4), C(5)}}, D(6) top-level, return R(7)
        LinkTrack[] Tree(bool outer, bool inner) => new[]
        {
            new LinkTrack(1, -1, true, false, outer),
            new LinkTrack(2, 1, false, false, false),
            new LinkTrack(3, 1, true, false, inner),
            new LinkTrack(4, 3, false, false, false),
            new LinkTrack(5, 3, false, false, false),
            new LinkTrack(6, -1, false, false, false),
            new LinkTrack(7, -1, false, true, false),
        };
        var off = GroupLink.Build(Tree(false, false));
        yield return (!off.AnyLinked && off.Members(2).Count == 0 && off.RootOf(2) == -1, "unlinked groups link nothing");
        var outer = GroupLink.Build(Tree(true, false));
        yield return (outer.Members(2).SequenceEqual(new[] { 4, 5 }) && outer.RootOf(5) == 1,
            "a linked group links every track below it, nested groups included");
        yield return (!outer.IsLinked(6) && !outer.IsLinked(7) && outer.Members(1).Count == 0 && outer.Members(3).Count == 0,
            "tracks outside, returns and group tracks are never members");
        var inner = GroupLink.Build(Tree(false, true));
        yield return (inner.Members(4).SequenceEqual(new[] { 5 }) && !inner.IsLinked(2), "a linked inner group links only its own tracks");
        var both = GroupLink.Build(Tree(true, true));
        yield return (both.RootOf(4) == 1 && both.Members(4).SequenceEqual(new[] { 2, 5 }), "the outermost linked group wins");
        yield return (outer.ExpandTracks(new[] { 4, 6 }).SequenceEqual(new[] { 4, 6, 2, 5 }), "a range widens to whole link sets");
    }

    private static IEnumerable<(bool, string)> Matching()
    {
        yield return (GroupLink.Matches(4, 8, 4, 8), "clips at the same place match");
        yield return (GroupLink.Matches(4, 8, 4 + 1.0 / 512, 8), "a hair's difference still matches");
        yield return (!GroupLink.Matches(4, 8, 4 + 1.0 / 128, 8 - 1.0 / 128), "a 128th apart does not match");
        yield return (!GroupLink.Matches(4, 8, 4, 6), "the same start but another end does not match");
    }

    private static IEnumerable<(bool, string)> LinkedArm()
    {
        using var e = new NotaEngine();
        int a = e.AddAudioTrack(), b = e.AddAudioTrack(), c = e.AddAudioTrack();
        int g = e.CreateGroup(new[] { a, b });
        bool Armed(int id) { for (int i = 0; i < e.TrackCount; i++) if (e.TryGetTrackInfo(i, out var t) && t.Id == id) return t.Armed != 0; return false; }

        TrackArm.Set(e, a, true, additive: false);
        yield return (Armed(a) && !Armed(b), "unlinked, an arm click arms that track alone");
        TrackArm.Set(e, a, false, additive: false);

        e.SetTrackEditLink(g, true);
        TrackArm.Set(e, c, true, additive: false);
        bool changed = TrackArm.Set(e, a, true, additive: false);
        yield return (changed && Armed(a) && Armed(b) && !Armed(c), "a plain arm click arms the linked set and disarms the rest");
        TrackArm.Set(e, c, true, additive: true);
        yield return (Armed(a) && Armed(b) && Armed(c), "⌘-click outside the set keeps the set armed");
        TrackArm.Set(e, b, false, additive: false);
        yield return (!Armed(a) && !Armed(b) && Armed(c), "disarming one member disarms the set");
        TrackArm.Set(e, b, true, additive: true, linked: false);
        yield return (Armed(b) && !Armed(a), "the ⌥ bypass arms one member alone");
    }
}
