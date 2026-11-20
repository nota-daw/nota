// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The controller mapping editor's model (specs/110, #973): remap one function of a profile
// without walking the whole wizard — learn, move a control another function had, clear, revert,
// add any command, take the pre-filled wizard's result — and write the profile back with only the
// edited entries changed (init / release, LED feedback, source and hand edits kept). Pure: no
// engine, no window. Also runnable alone: `--controller-remap`.

using System.Text.Json.Nodes;
using Nota.Application.Controllers;

namespace Nota.SmokeTest;

internal static class ControllerRemapTests
{
    // A shipped-style profile: init and release, LED feedback on Play, a motor fader, a two-way
    // track encoder, an undo / redo pair as two buttons, a pad that is no wizard function.
    private const string Profile = """
    {
      "format": 1,
      "id": "acme.keys",
      "name": "Acme Keys",
      "protocol": "generic",
      "source": "Acme manual p. 12",
      "match": { "portNames": ["^Acme Keys$"] },
      "init": ["F0 00 20 6B 7F 42 02 00 40 50 01 F7"],
      "release": ["F0 00 20 6B 7F 42 02 00 40 50 00 F7"],
      "pickup": true,
      // hand-written note: the pads stay on channel 10
      "controls": [
        { "role": "knob", "index": 1, "message": "cc", "channel": 1, "number": 74, "encoder": "relative-twos" },
        { "role": "fader", "index": 1, "message": "cc", "channel": 1, "number": 7, "motorised": true },
        { "role": "play", "message": "note", "channel": 1, "number": 94, "feedback": { "on": "90 5E 7F", "off": "90 5E 00" } },
        { "role": "stop", "message": "note", "channel": 1, "number": 93 },
        { "role": "track-next", "roleLeft": "track-prev", "message": "cc", "channel": 1, "number": 60, "encoder": "relative-twos" },
        { "role": "command", "command": "edit.undo", "message": "cc", "channel": 1, "number": 42 },
        { "role": "command", "command": "edit.redo", "message": "cc", "channel": 1, "number": 43 },
        { "role": "pad", "index": 1, "message": "note", "channel": 10, "number": 36 }
      ]
    }
    """;

    private static readonly FunctionKey Play = FunctionKey.Of(ControlRoleKind.Play);
    private static readonly FunctionKey Stop = FunctionKey.Of(ControlRoleKind.Stop);
    private static readonly FunctionKey Loop = FunctionKey.Of(ControlRoleKind.Loop);
    private static readonly FunctionKey Undo = FunctionKey.OfCommand("edit.undo");
    private static readonly FunctionKey Redo = FunctionKey.OfCommand("edit.redo");
    private static readonly FunctionKey Metronome = FunctionKey.OfCommand("transport.metronome");
    private static readonly FunctionKey TrackNext = FunctionKey.Of(ControlRoleKind.TrackNext);
    private static readonly FunctionKey TrackPrev = FunctionKey.Of(ControlRoleKind.TrackPrev);

    private static ClassifiedControl Cc(int n, EncoderMode e = EncoderMode.Absolute) => new(ControlMessage.Cc, 0, n, e);
    private static ClassifiedControl Note(int n) => new(ControlMessage.Note, 0, n, EncoderMode.Absolute);

    private static ProfileMapping Open(ProfileOrigin origin = ProfileOrigin.Shipped)
    {
        var (p, _) = ProfileLoader.Load(Profile, "acme.keys.json", origin);
        return new ProfileMapping(p!, Profile);
    }

    private static ControllerProfile Reload(ProfileMapping m)
        => ProfileLoader.Load(m.ToJson(), "acme.keys.json", ProfileOrigin.User).Profile!;

    private static JsonArray Controls(string json) => (JsonNode.Parse(json, documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip })!["controls"] as JsonArray)!;

    public static IEnumerable<(bool ok, string label)> Run()
    {
        // US1: one function, nothing else changes.
        var m = Open();
        yield return (!m.HasChanges && m.ControlOf(Undo) is { Number: 42 } && m.ControlOf(Metronome) is null,
            "remap: opens on the profile's controls; a function it doesn't have is empty");
        var before = Controls(m.ToJson());
        m.Assign(Undo, Cc(50));
        var after = Controls(m.ToJson());
        int differing = Enumerable.Range(0, before.Count).Count(i => !JsonNode.DeepEquals(before[i], after[i]));
        yield return (m.HasChanges && m.IsChanged(Undo) && !m.IsChanged(Redo) && !m.IsChanged(Play)
                      && after.Count == before.Count && differing == 1 && Reload(m).Controls.Single(c => c.Role.Command == "edit.undo").Number == 50,
            "remap: Undo onto another button changes that one entry only (SC-002)");

        var same = Open();
        same.Assign(Play, Note(94));
        yield return (!same.HasChanges, "remap: learning the control a function already has changes nothing");

        // FR-012: everything around the controls is kept.
        string text = Open().ToJson();
        var root = JsonNode.Parse(text, documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip })!;
        var reloaded = ProfileLoader.Load(m.ToJson(), "acme.keys.json", ProfileOrigin.User).Profile!;
        yield return ((string?)root["source"] == "Acme manual p. 12" && reloaded.Init.Count == 1 && reloaded.Release.Count == 1
                      && reloaded.Id == "acme.keys" && reloaded.Pickup && reloaded.PortNames.Count == 1
                      && reloaded.Controls.Single(c => c.Role.Kind == ControlRoleKind.Play).Feedback?.On is { Length: 3 }
                      && reloaded.Controls.Single(c => c.Role.Kind == ControlRoleKind.Fader).Motorised,
            "remap: id, match, init / release, source, pickup, feedback and motor flags survive a save");

        // US2: a control another function has.
        var mv = Open();
        var conflicts = mv.ConflictsFor(Note(94), Stop);
        yield return (conflicts.Count == 1 && conflicts[0].Right == Play && !mv.HasChanges,
            "remap: learning Play's button for Stop names Play and changes nothing yet");
        mv.Assign(Stop, Note(94));
        var mvp = Reload(mv);
        yield return (mv.ControlOf(Play) is null && mv.ControlOf(Stop) is { Number: 94 } && mv.IsChanged(Play) && mv.IsChanged(Stop)
                      && mvp.Controls.Count(c => c.Number == 94 && c.Message == ControlMessage.Note) == 1
                      && mvp.Controls.Single(c => c.Role.Kind == ControlRoleKind.Stop).Feedback?.On is { Length: 3 },
            "remap: Move — Play has no control, Stop has the button and keeps that button's LED feedback");

        var enc = Open();
        yield return (enc.ConflictsFor(Cc(60, EncoderMode.RelativeTwos), Loop).Single() is { Right: var r, Left: var l } && r == TrackNext && l == TrackPrev,
            "remap: the conflict for a two-way encoder names the pair");
        enc.Assign(Loop, Cc(60));
        yield return (enc.ControlOf(TrackNext) is null && enc.ControlOf(TrackPrev) is null && enc.ControlOf(Loop) is { Number: 60 },
            "remap: moving a pair's encoder clears both halves (US2.4)");

        // Pairs.
        var pr = Open();
        pr.Assign(TrackNext, Note(20));
        yield return (pr.ControlOf(TrackNext) is { Number: 20 } && pr.ControlOf(TrackPrev) is null && !Reload(pr).Controls.Any(c => c.Number == 60),
            "remap: one half of an encoder pair onto a button — the encoder leaves both halves (it can't turn one way only)");
        var pe = Open();
        pe.Assign(Redo, Cc(70, EncoderMode.RelativeTwos), Undo);
        var pep = Reload(pe);
        yield return (pe.ControlOf(Undo) is { Number: 70 } && pe.ControlOf(Redo) is { Number: 70 }
                      && pep.Controls.Single(c => c.Number == 70).Role is { Command: "edit.redo", CommandLeft: "edit.undo" }
                      && !pep.Controls.Any(c => c.Number is 42 or 43),
            "remap: undo / redo onto one encoder — one two-way entry, both buttons free");

        // US4: clear and revert.
        var cl = Open();
        cl.Clear(Play);
        yield return (cl.ControlOf(Play) is null && cl.IsChanged(Play) && !Reload(cl).Controls.Any(c => c.Role.Kind == ControlRoleKind.Play),
            "remap: Clear — no control does Play");
        cl.Revert(Play);
        yield return (!cl.HasChanges && cl.ToJson() == Open().ToJson(), "remap: Revert brings Play's control back, the document as it was");
        var cl2 = Open();
        cl2.Clear(TrackPrev);
        yield return (cl2.ControlOf(TrackNext) is null && cl2.ControlOf(TrackPrev) is null, "remap: clearing one half of an encoder pair clears the encoder");
        cl2.Revert(TrackPrev);
        yield return (cl2.ControlOf(TrackNext) is { Number: 60 } && cl2.ControlOf(TrackPrev) is { Number: 60 } && !cl2.HasChanges,
            "remap: reverting a half brings the encoder back for both");
        var rv = Open();
        rv.Assign(Stop, Note(94));
        var inWay = rv.Revert(Play);
        yield return (inWay.Count == 1 && inWay[0].Right == Stop && rv.ControlOf(Play) is null,
            "remap: Revert is refused while its control does something else");

        // US6: any command.
        var cmd = Open();
        cmd.Assign(FunctionKey.OfCommand("edit.duplicate"), Note(30));
        var cmdp = Reload(cmd);
        yield return (cmdp.Controls.Any(c => c.Role is { Kind: ControlRoleKind.Command, Command: "edit.duplicate" } && c.Number == 30),
            "remap: a palette command on a spare button round-trips through the loader");
        cmd.Clear(FunctionKey.OfCommand("edit.duplicate"));
        yield return (!cmd.HasChanges, "remap: a command row without a control isn't written");

        // US7: the pre-filled wizard, Enter through every step.
        var wz = Open();
        var scope = new[] { FunctionKey.Of(ControlRoleKind.Knob, 0), FunctionKey.Of(ControlRoleKind.Fader, 0), Play, Stop, Loop, TrackNext, TrackPrev, Undo, Redo, Metronome };
        var learnedSame = new List<(FunctionKey, FunctionKey?, ClassifiedControl)>
        {
            (FunctionKey.Of(ControlRoleKind.Knob, 0), null, Cc(74, EncoderMode.RelativeTwos)), (FunctionKey.Of(ControlRoleKind.Fader, 0), null, Cc(7)),
            (Play, null, Note(94)), (Stop, null, Note(93)), (TrackNext, TrackPrev, Cc(60, EncoderMode.RelativeTwos)),
            (Undo, null, Cc(42)), (Redo, null, Cc(43)),
        };
        wz.ReplaceAll(learnedSame, scope);
        yield return (!wz.HasChanges, "remap: the pre-filled wizard kept on every step leaves the profile as it was");
        var wz2 = Open();
        wz2.ReplaceAll(learnedSame.Where(x => x.Item1 != Stop), scope);
        yield return (wz2.ControlOf(Stop) is null && wz2.Entries.Any(e => e.Decl?.Role.Kind == ControlRoleKind.Pad),
            "remap: a step the wizard skipped is cleared; controls outside the wizard (a pad) stay");

        // Comments in the file survive a save; a pair abandoned half-way goes back.
        string withNotes = Profile.Replace("{ \"role\": \"stop\"", "// Stop is the square button\n        { \"role\": \"stop\"");
        var (np, _) = ProfileLoader.Load(withNotes, "acme.keys.json", ProfileOrigin.Shipped);
        var notes = new ProfileMapping(np!, withNotes);
        yield return (notes.ToJson() == withNotes, "remap: nothing changed writes the file back byte for byte");
        notes.Assign(Metronome, Cc(50));
        string saved = notes.ToJson();
        yield return (saved.Contains("// hand-written note: the pads stay on channel 10") && saved.Contains("// Stop is the square button")
                      && ProfileLoader.Load(saved, "acme.keys.json", ProfileOrigin.User).Profile!.Controls.Any(c => c.Role.Command == "transport.metronome" && c.Number == 50),
            "remap: comments in the profile survive an edit, and the new entry loads");
        // A comment before the separator (`entry /* note */, entry`) stays, and the comma isn't doubled.
        string inline = Profile.Replace("\"channel\": 1, \"number\": 93 },", "\"channel\": 1, \"number\": 93 } /* the square one */,");
        var (ip, _) = ProfileLoader.Load(inline, "acme.keys.json", ProfileOrigin.Shipped);
        var im = new ProfileMapping(ip!, inline);
        im.Assign(Metronome, Cc(51));
        string isaved = im.ToJson();
        var (ireload, iwarn) = ProfileLoader.Load(isaved, "acme.keys.json", ProfileOrigin.User);
        yield return (inline != Profile && isaved.Contains("/* the square one */") && ireload is { Controls.Count: 9 } && iwarn.Count == 0,
            $"remap: a comment before a separating comma survives an edit and the file still loads{(iwarn.Count > 0 ? " — " + iwarn[0] : "")}");

        var snap = Open();
        var before2 = snap.Snapshot();
        snap.Assign(Undo, Note(70));
        snap.Restore(before2);
        yield return (!snap.HasChanges, "remap: a snapshot puts back a pair whose second button never came");

        // Find by touch.
        var f = Open();
        yield return (f.Find(0x90, 0, 94)?.Right == Play && f.Find(0xB0, 0, 60)?.Left == TrackPrev && f.Find(0xB0, 0, 99) is null,
            "remap: a message finds the function its control does; an unmapped one finds nothing");
        const string nrpnProfile = """{ "format": 1, "id": "acme.n", "name": "N", "match": { "portNames": ["^N$"] }, "controls": [ { "role": "knob", "index": 1, "message": "nrpn", "channel": 1, "number": 300 } ] }""";
        var nm = new ProfileMapping(ProfileLoader.Load(nrpnProfile, "acme.n.json", ProfileOrigin.User).Profile!, nrpnProfile);
        yield return (nm.FindNrpn(0, 300)?.Right == FunctionKey.Of(ControlRoleKind.Knob, 0) && nm.FindNrpn(0, 301) is null,
            "remap: an NRPN control is found by its parameter number");
    }
}
