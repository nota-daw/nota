// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Controller auto-mapping (specs/051, #100): the engine's MIDI port layer (claims, the surface
// queue, SysEx, output) and the pure controller logic in Nota.Application/Controllers (profiles,
// matching). Hardware-free: the engine's test ports stand in for a connected controller. Also
// runnable alone: `--controllers`.
//
// Seed-profile sources (specs/051 T003, research R14) — each shipped profile's `source` cites one:
// - Arturia KeyLab Essential 61 mk3 (the maintainer's reference device): no public programmer's
//   reference. Its DAW functions use the Mackie Control protocol on the separate "MCU/HUI" port
//   (MIDI Control Center → Device Settings → transport mode MCU), standard MIDI on the "MIDI"
//   port. User manual: https://www.manualslib.com/manual/3092667/Arturia-Keylab-Essential-Mk3.html;
//   https://support.arturia.com/hc/en-us/articles/8905862388508-KeyLab-Essential-mk3-DAW-Integration
//   Port names and the identity reply are to be read off the real device.
// - Novation Launchkey [MK3] Programmer's Reference Guide v1 (DAW mode on the "DAW In/Out"
//   port; enter with 9F 0C 7F, leave with 9F 0C 00):
//   https://fael-downloads-prod.focusrite.com/customer/prod/downloads/launchkey_mk3_programmer_s_reference_guide_v1_en.pdf
// - Behringer X-TOUCH MINI Quick Start Guide, MIDI map (Standard mode: layer A ch 11, encoders
//   CC 1–8; RX table: CC 127 mode select, LED rings CC 9–16): https://www.bhphotovideo.com/lit_files/1142312.pdf
// - Akai MPK mini mk3 User Guide (opened from the MPK mini Editor, Help → Open User Guide): it
//   documents 8 assignable 360° knobs but no default CC chart — defaults must be read from the
//   editor's factory program before the profile is written.
// - Mackie Control: no official wire-protocol document is published. Facts come from Apple's
//   "Logic Pro 9 Control Surfaces Support" (button assignments) and the protocol tables in
//   https://github.com/NicoG60/TouchMCU/blob/main/doc/mackie_control_protocol.md — protocol facts
//   only, no code or mapping data copied; check that document's licence before citing it in a
//   shipped profile.

using Nota.Application;
using Nota.Application.Controllers;
using Nota.Application.Controllers.Hui;
using Nota.Application.Controllers.Mcu;
using Nota.Application.Palette;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class ControllerTests
{
    public static IEnumerable<(bool ok, string label)> Run()
    {
        foreach (var r in SurfaceLayer()) yield return r;
        foreach (var r in Profiles()) yield return r;
        foreach (var r in Matching()) yield return r;
        foreach (var r in Decoding()) yield return r;
        foreach (var r in Routing()) yield return r;
        foreach (var r in Overrides()) yield return r;
        foreach (var r in ReviewFixes()) yield return r;
        foreach (var r in ShippedEndToEnd()) yield return r;
        foreach (var r in DevicePages()) yield return r;
        foreach (var r in KnobFocus()) yield return r;
        foreach (var r in Pickups()) yield return r;
        foreach (var r in Mackie()) yield return r;
        foreach (var r in MackieEndToEnd()) yield return r;
        foreach (var r in Hui()) yield return r;
        foreach (var r in Classifier()) yield return r;
        foreach (var r in PagesStore()) yield return r;
        foreach (var r in FocusStepping()) yield return r;
    }

    // specs/107: Track ◀ / ▶ select tracks (the bank follows), Next / Previous device walk the chain.
    private static IEnumerable<(bool, string)> FocusStepping()
    {
        // The walk order: groups before their members, a collapsed group's members left out.
        var rows = new[] { (1, -1), (2, -1), (3, 2), (4, 2), (5, -1) };
        var open = ControllerTrackStep.Order(rows, _ => false);
        var folded = ControllerTrackStep.Order(rows, g => g == 2);
        yield return (open.SequenceEqual(new[] { 1, 2, 3, 4, 5 }) && folded.SequenceEqual(new[] { 1, 2, 5 }),
            "track step: the order is the track list, a collapsed group's members skipped");
        yield return (ControllerTrackStep.Step(open, 3, 1) == 4 && ControllerTrackStep.Step(open, 3, -1) == 2
                      && ControllerTrackStep.Step(open, 5, 1) is null && ControllerTrackStep.Step(open, 1, -1) is null
                      && ControllerTrackStep.Step(open, 99, 1) == 1 && ControllerTrackStep.Step(new List<int>(), 1, 1) is null,
            "track step: neighbours, ends stop, no selection starts at the first track");

        var st = new ControllerState();
        bool inside = !st.BankFollow(7, 20) && st.BankOrigin == 0;
        bool past = st.BankFollow(8, 20) && st.BankOrigin == 1;
        bool back = st.BankFollow(0, 20) && st.BankOrigin == 0;
        bool far = st.BankFollow(15, 20) && st.BankOrigin == 8;
        yield return (inside && past && back && far && !st.BankFollow(-1, 20) && !st.BankFollow(20, 20),
            "track step: the bank moves the least amount that keeps the selected track inside it");

        using var e = new NotaEngine();
        int t = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(t, 6);   // Volt
        int arp = e.AddMidiEffect(t, 0);
        int comp = e.AddBuiltinDevice(t, 1);
        int eq = e.AddBuiltinDevice(t, 16);
        var order = ChainOrder.Of(e, t);
        var m0 = new DeviceAddress(t, FocusSection.Midi, arp);
        var inst = DeviceAddress.Instrument(t);
        var c1 = new DeviceAddress(t, FocusSection.Audio, comp);
        var e2 = new DeviceAddress(t, FocusSection.Audio, eq);
        yield return (order.SequenceEqual(new[] { m0, inst, c1, e2 }),
            "device step: chain order is MIDI effects, instrument, audio effects");
        yield return (ChainOrder.Step(order, m0, 1) == inst && ChainOrder.Step(order, inst, 1) == c1 && ChainOrder.Step(order, e2, -1) == c1
                      && ChainOrder.Step(order, e2, 1) is null && ChainOrder.Step(order, m0, -1) is null
                      && ChainOrder.Step(order, new DeviceAddress(t, FocusSection.Audio, 9), 1) == m0
                      && ChainOrder.Step(order, null, 1) == m0,
            "device step: neighbours, ends stop, a missing device starts at the first");
        yield return (ChainOrder.Step(order, new DeviceAddress(t, FocusSection.Audio, comp, new RackPath(0, 1)), 1) == e2
                      && ChainOrder.Of(e, e.AddAudioTrack()).Count == 0,
            "device step: a device inside a rack steps from its rack; an empty chain has none");
    }

    // US5 (T069): the setup wizard's classifier.
    private static IEnumerable<(bool, string)> Classifier()
    {
        static List<(int, int, int, int)> Cc(int ch, int n, params int[] v) => v.Select(x => (0xB0, ch, n, x)).ToList();
        var sweep = Cc(0, 74, Enumerable.Range(20, 40).ToArray());
        var pair = new List<(int, int, int, int)> { (0xB0, 1, 7, 64), (0xB0, 1, 39, 10), (0xB0, 1, 7, 65), (0xB0, 1, 39, 3) };
        var nrpn = new List<(int, int, int, int)> { (0xB0, 2, 99, 1), (0xB0, 2, 98, 5), (0xB0, 2, 6, 64), (0xB0, 2, 38, 0) };
        yield return (ControlClassifier.Classify(sweep) == new ClassifiedControl(ControlMessage.Cc, 0, 74, EncoderMode.Absolute)
                      && ControlClassifier.Classify(pair) == new ClassifiedControl(ControlMessage.Cc14, 1, 7, EncoderMode.Absolute)
                      && ControlClassifier.Classify(nrpn) == new ClassifiedControl(ControlMessage.Nrpn, 2, (1 << 7) | 5, EncoderMode.Absolute),
            "classify: a sweep is an absolute CC; n then n + 32 is 14-bit; 99/98 + 6/38 is NRPN");
        yield return (ControlClassifier.Classify(Cc(0, 16, 1, 1, 0x7F, 0x7F))?.Encoder == EncoderMode.RelativeTwos
                      && ControlClassifier.Classify(Cc(0, 16, 1, 1, 0x41, 0x41))?.Encoder == EncoderMode.RelativeSignBit
                      && ControlClassifier.Classify(Cc(0, 16, 0x41, 0x41, 0x3F, 0x3F))?.Encoder == EncoderMode.RelativeOffset,
            "classify: right then left tells two's complement, sign-bit and binary offset apart");
        yield return (ControlClassifier.Classify(Cc(0, 16, 1, 1, 1)) is { Ambiguous: true }
                      && ControlClassifier.Classify(new List<(int, int, int, int)> { (0x90, 9, 36, 100), (0x80, 9, 36, 0) }) == new ClassifiedControl(ControlMessage.Note, 9, 36, EncoderMode.Absolute)
                      && ControlClassifier.Classify(new List<(int, int, int, int)> { (0xE0, 3, 0, 64) }) is { Message: ControlMessage.PitchBend, Channel: 3 },
            "classify: one direction only is ambiguous (the wizard asks); a pad is a note; pitch bend is pitch bend");

        var learned = new List<LearnedControl>
        {
            new(ControlRoleKind.Knob, 0, new ClassifiedControl(ControlMessage.Cc, 0, 16, EncoderMode.RelativeSignBit)),
            new(ControlRoleKind.Fader, 1, new ClassifiedControl(ControlMessage.Cc14, 0, 7, EncoderMode.Absolute)),
            new(ControlRoleKind.Play, 0, new ClassifiedControl(ControlMessage.Note, 9, 94, EncoderMode.Absolute)),
        };
        var reply = IdentityReply.TryParse(new byte[] { 0xF0, 0x7E, 0x7F, 0x06, 0x02, 0x00, 0x20, 0x29, 0x36, 0x01, 0x00, 0x00, 1, 2, 3, 4, 0xF7 });
        string json = ProfileWriter.Write("My Keys (61)", reply, learned);
        var (wp, ww) = ProfileLoader.Load(json, "user.my-keys-61.json", ProfileOrigin.User);
        yield return (wp is { Id: "user.my-keys-61", Controls.Count: 3 } && ww.Count == 0 && wp.Controls[0].Encoder == EncoderMode.RelativeSignBit
                      && wp.Controls[1] is { Message: ControlMessage.Cc14, Role.Index: 1 } && wp.Controls[2] is { Channel: 9, Number: 94 }
                      && ProfileMatcher.Match(new[] { wp }, "My Keys (61)", reply) is not null && ProfileMatcher.Match(new[] { wp }, "My Keys (61) 2", reply) is null,
            $"wizard: the profile it writes loads clean and matches that port exactly{(ww.Count > 0 ? " — " + ww[0] : "")}");
    }

    // US5 (T070): edited pages, global and per project.
    private static IEnumerable<(bool, string)> PagesStore()
    {
        var dir = Directory.CreateTempSubdirectory("nota-pages-").FullName;
        try
        {
            string global = Path.Combine(dir, "controllers", "pages.json"), project = Path.Combine(dir, "song.nota");
            Directory.CreateDirectory(project);
            var store = new DevicePagesStore(global);
            store.LoadGlobal();
            var mine = new[] { new DevicePage("Mine", new string?[] { "Ratio", null, "Thresh" }) };
            store.Edit("builtin:fx:compressor", mine);
            var reread = DevicePagesFile.Parse(File.ReadAllText(global), out var err);
            yield return (err is null && reread!["builtin:fx:compressor"][0] is { Name: "Mine", Params: ["Ratio", null, "Thresh"] },
                "pages: an edit writes the global pages.json (null = an empty knob)");

            store.SaveProject(project, new[] { "builtin:fx:compressor", "builtin:inst:volt" });
            var copy = DevicePagesFile.Parse(File.ReadAllText(Path.Combine(project, DevicePagesStore.ProjectFileName)), out _)!;
            yield return (copy.Count == 1 && copy.ContainsKey("builtin:fx:compressor"), "pages: a saved project keeps the edited pages of the types it contains");

            // Elsewhere the user edits again; the project's copy still wins while it is open.
            var other = new DevicePagesStore(global);
            other.LoadGlobal();
            other.Edit("builtin:fx:compressor", new[] { new DevicePage("Later", new string?[] { "Makeup" }) });
            string globalBefore = File.ReadAllText(global);
            other.LoadProject(project);
            yield return (other.PagesFor("builtin:fx:compressor")![0].Name == "Mine" && File.ReadAllText(global) == globalBefore,
                "pages: the project's copy wins while it is open, and opening it leaves the global file alone");
            other.Reset("builtin:fx:compressor");
            yield return (other.PagesFor("builtin:fx:compressor") is null && !File.ReadAllText(global).Contains("compressor"),
                "pages: reset removes the type from both copies");
            yield return (DevicePagesFile.Parse("{\"format\": 2, \"types\": {}}", out var e2) is null && e2 is not null,
                "pages: a file in a newer format is refused");
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    private static string Hex(IEnumerable<byte[]> msgs) => string.Join(" | ", msgs.Select(m => string.Join(' ', m.Select(b => b.ToString("X2")))));

    // US4 (T060, T061): the MCU codec and feedback by difference.
    private static IEnumerable<(bool, string)> Mackie()
    {
        var c = new McuCodec();
        yield return (c.Decode(0xE0, 2, 0x7F, 0x7F) == new SurfaceInput(SurfaceInputKind.Fader, 2, 16383)
                      && c.Decode(0xE0, 8, 0, 0x40) == new SurfaceInput(SurfaceInputKind.MasterFader, 8, 0x40 << 7),
            "MCU: pitch bend ch 3 → fader 3 (14-bit), ch 9 → master");
        yield return (c.Decode(0x90, 0, 106, 0x7F) == new SurfaceInput(SurfaceInputKind.Touch, 2, 0, true)
                      && c.Decode(0x80, 0, 106, 0) == new SurfaceInput(SurfaceInputKind.Touch, 2, 0, false),
            "MCU: note 106 on / off → fader 3 touched / let go");
        yield return (c.Decode(0xB0, 0, 17, 0x41) == new SurfaceInput(SurfaceInputKind.VPot, 1, -1)
                      && c.Decode(0xB0, 0, 17, 0x03) == new SurfaceInput(SurfaceInputKind.VPot, 1, 3)
                      && c.Decode(0xB0, 0, 60, 0x42) == new SurfaceInput(SurfaceInputKind.Jog, -1, -2),
            "MCU: V-pot 2 and the jog wheel decode sign-bit steps");
        yield return (c.Decode(0x90, 0, 3, 0x7F) is { Kind: SurfaceInputKind.Rec, Strip: 3 } && c.Decode(0x90, 0, 11, 0x7F) is { Kind: SurfaceInputKind.Solo, Strip: 3 }
                      && c.Decode(0x90, 0, 19, 0x7F) is { Kind: SurfaceInputKind.Mute, Strip: 3 } && c.Decode(0x90, 0, 27, 0x7F) is { Kind: SurfaceInputKind.Select, Strip: 3 },
            "MCU: notes 0 / 8 / 16 / 24 + n → rec / solo / mute / select of strip n");
        var nav = new[] { 46, 47, 48, 49, 91, 92, 93, 94, 95, 86 }.Select(n => c.Decode(0x90, 0, n, 0x7F)?.Kind).ToArray();
        yield return (nav.SequenceEqual(new SurfaceInputKind?[] { SurfaceInputKind.BankLeft, SurfaceInputKind.BankRight, SurfaceInputKind.ChannelLeft, SurfaceInputKind.ChannelRight,
                          SurfaceInputKind.Rewind, SurfaceInputKind.Forward, SurfaceInputKind.Stop, SurfaceInputKind.Play, SurfaceInputKind.Record, SurfaceInputKind.Loop }),
            "MCU: bank 46/47, channel 48/49, transport 91–95 and cycle 86");

        var name = c.Name(2, "Bass Synth").Single();
        yield return (Hex(new[] { name }) == "F0 00 00 66 14 12 0E 42 61 73 73 20 53 20 F7", $"MCU: strip 3's name is a scribble SysEx at offset 14 ({Hex(new[] { name })})");
        var time = c.Time("  1 1 1000", null).ToList();
        yield return (time.Count == 10 && time[0] is [0xB0, 0x40, (byte)'0'] && time[9] is [0xB0, 0x49, 0x20], "MCU: the time display is CC 64–73, right to left");

        var d = new FeedbackDiffer(c);
        var want = new SurfaceState();
        for (int s = 0; s < 8; s++) want.Strip[s] = SurfaceStripState.Empty with { TrackId = s + 1, Name = $"T{s + 1}", Fader = 1000 };
        var touched = new bool[9];
        var first = d.Diff(want, touched);
        var again = d.Diff(want, touched);
        yield return (first.Count(m => m[0] == 0xF0 && m[5] == 0x12) >= 8 && again.Count == 0,
            $"feedback: a full refresh sends all eight names; an unchanged surface sends nothing ({first.Count} then {again.Count})");
        want.Strip[1] = want.Strip[1] with { Fader = 5000 };
        touched[1] = true;
        var held = d.Diff(want, touched);
        touched[1] = false;
        var released = d.Diff(want, touched);
        yield return (held.Count == 0 && released is [[0xE1, _, _]],
            "feedback: a touched fader is not driven; its position goes out when it is let go (FR-024)");
        want.Play = true;
        want.Strip[0] = want.Strip[0] with { Mute = true };
        var two = d.Diff(want, touched);
        yield return (two.Count == 2 && two.Any(m => m is [0x90, 94, 0x7F]) && two.Any(m => m is [0x90, 16, 0x7F]), "feedback: one message per changed light");
        d.Invalidate();
        yield return (d.Diff(want, touched).Count(m => m[0] == 0xF0 && m[5] == 0x12) >= 8, "feedback: after a bank change (invalidate) all strips are sent again");
        yield return (SurfaceState.PanToRing(0) == 6 && SurfaceState.PanToRing(-1) == 1 && SurfaceState.PanToRing(1) == 11
                      && SurfaceState.MeterLevel(1.0) == 12 && SurfaceState.MeterLevel(0) == 0 && SurfaceState.TimeText(4.5, 4) == "  2 1 3000",
            "surface model: pan ring, meter scale and the time display text");
    }

    // US4 (T068): a test port with the generic Mackie Control profile, through the engine.
    private static IEnumerable<(bool, string)> MackieEndToEnd()
    {
        var dir = Path.Combine(RepoRoot() ?? ".", "src", "managed", "Nota.App", "Controllers", "Profiles", "generic.mackie-control.json");
        var (p, w) = ProfileLoader.Load(File.Exists(dir) ? File.ReadAllText(dir) : "{}", "generic.mackie-control.json", ProfileOrigin.Shipped);
        yield return (p is { Protocol: ControllerProtocol.Mcu, Pickup: false } && w.Count == 0 && ProfileMatcher.Match(new[] { p }, "X-Touch", null) is not null
                      && ProfileMatcher.Match(new[] { p }, "X-TOUCH MINI", null) is null,
            "generic Mackie Control profile: whole-port MCU, motor faders (no pickup), matches an X-Touch but not the Mini");
        if (p is null) yield break;
        using var e = new NotaEngine();
        int src = e.MidiTestPortAdd("Smoke MCU");
        e.SetMidiClaims(RoleRouter.BuildClaims(p, src, new HashSet<(bool, int, int)>()).ToArray());
        var buf = new int[5 * 16];
        e.PollMidiSurfaceEvents(buf);
        e.MidiTestInject(src, new byte[] { 0xE0, 0x00, 0x40, 0x90, 47, 0x7F });
        int n = e.PollMidiSurfaceEvents(buf);
        var codec = new McuCodec();
        var ins = Enumerable.Range(0, n).Select(i => codec.Decode(buf[i * 5 + 1], buf[i * 5 + 2], buf[i * 5 + 3], buf[i * 5 + 4])).ToList();
        yield return (n == 2 && ins[0] is { Kind: SurfaceInputKind.Fader, Strip: 0, Value: 0x40 << 7 } && ins[1] is { Kind: SurfaceInputKind.BankRight, Pressed: true },
            "MCU port: pitch bend ch 1 and bank → arrive on the surface queue and decode");
        var d = new FeedbackDiffer(codec);
        var want = new SurfaceState();
        want.Strip[0] = SurfaceStripState.Empty with { TrackId = 1, Fader = 12000, Name = "Kick" };
        foreach (var m in d.Diff(want, new bool[9])) e.MidiSend("test:Smoke MCU", m);
        var sent = new byte[8192];
        int ns = e.MidiTestTakeSent(src, sent);
        var bytes = sent.Take(ns).ToArray();
        bool hasBend = Enumerable.Range(0, ns - 2).Any(i => bytes[i] == 0xE0 && bytes[i + 1] == (12000 & 0x7F) && bytes[i + 2] == (12000 >> 7));
        int scribbles = Enumerable.Range(0, ns - 5).Count(i => bytes[i] == 0xF0 && bytes[i + 4] == 0x14 && bytes[i + 5] == 0x12);
        yield return (hasBend && scribbles >= 8, $"MCU port: track 1's volume goes out as pitch bend ch 1 with eight scribble strips ({scribbles})");
    }

    // US6 (T077): the HUI codec.
    private static IEnumerable<(bool, string)> Hui()
    {
        var h = new HuiCodec();
        var none = h.Decode(0xB0, 0, 0x0F, 2);
        var down = h.Decode(0xB0, 0, 0x2F, 0x40 | 2);
        var up = h.Decode(0xB0, 0, 0x2F, 2);
        yield return (none is null && down == new SurfaceInput(SurfaceInputKind.Mute, 2, 0, true) && up == new SurfaceInput(SurfaceInputKind.Mute, 2, 0, false),
            "HUI: zone 2 + port 2 on / off → mute of strip 3");
        h.Decode(0xB0, 0, 0x0F, 0x0E);
        yield return (h.Decode(0xB0, 0, 0x2F, 0x44) is { Kind: SurfaceInputKind.Play, Pressed: true } && h.Decode(0xB0, 0, 0x2F, 0x45) is { Kind: SurfaceInputKind.Record },
            "HUI: zone 0x0E ports 4 / 5 → play / record");
        h.Decode(0xB0, 0, 0x03, 0x60);
        yield return (h.Decode(0xB0, 0, 0x23, 0x10) == new SurfaceInput(SurfaceInputKind.Fader, 3, (0x60 << 7) | 0x10), "HUI: fader 4 high + low CC → 14 bits");
        yield return (h.Decode(0xB0, 0, 0x41, 0x43) == new SurfaceInput(SurfaceInputKind.VPot, 1, 3) && h.Decode(0xB0, 0, 0x41, 0x02) == new SurfaceInput(SurfaceInputKind.VPot, 1, -2)
                      && Hex(h.Ring(1, 6)) == "B0 11 06",
            "HUI: V-pot 2 turns (0x40 + n / n) and its ring is CC 0x11");
        yield return (Hex(h.Name(1, "Drums")) == "F0 00 00 66 05 00 10 01 44 72 75 6D F7" && h.Decode(0x90, 0, 0, 0x7F) is { Kind: SurfaceInputKind.Alive }
                      && HuiCodec.Ping is [0x90, 0x00, 0x00],
            "HUI: a 4-character strip name SysEx; the ping and its reply");
    }

    // US3 (T055): soft takeover (research R7).
    private static IEnumerable<(bool, string)> Pickups()
    {
        var a = new object(); var b = new object();
        var p = new PickupState();
        bool m1 = p.Accept(a, 0.1, 0.8), m2 = p.Accept(a, 0.5, 0.8);
        bool m3 = p.Accept(a, 0.9, 0.8);
        yield return (!m1 && !m2 && m3 && p.Caught, "pickup: below the value nothing moves; a jump across it catches and applies");
        bool m4 = p.Accept(a, 0.85, 0.9);   // value still what this control wrote? 0.9 → yes
        yield return (m4, "pickup: once caught the control follows");
        bool m5 = p.Accept(a, 0.6, 0.3);    // someone else set 0.3
        yield return (!m5 && !p.Caught, "pickup: a change by anything else lets go again");
        var q = new PickupState();
        yield return (q.Accept(a, 0.5 + 1.5 / 127, 0.5), "pickup: within 2/127 of the value catches at once");
        bool t1 = q.Accept(b, 0.1, 0.9);
        yield return (!t1, "pickup: pointing the control at another target lets go");
        var r = new PickupState();
        r.Accept(a, 0.2, 0.2);
        r.ReadBack(0.25);                   // a stepped parameter settled on its step
        yield return (r.Accept(a, 0.3, 0.25), "pickup: a stepped value that rounded is not taken for an outside change");
        r.Release();
        yield return (!r.Accept(a, 0.9, 0.25), "pickup: released (bank, page, mode) it waits again");
    }

    // US2 (T040): every built-in kind has pages, and every name on them resolves on a fresh
    // device of that kind — a renamed parameter or a new kind without pages fails here, by name.
    private static IEnumerable<(bool, string)> DevicePages()
    {
        var known = DevicePageCatalog.Known.ToList();
        var missing = BuiltinDeviceCatalog.All
            .Select(d => (Section: d.Type switch { BuiltinDeviceType.Instrument => FocusSection.Instrument, BuiltinDeviceType.AudioEffect => FocusSection.Audio, _ => FocusSection.Midi }, d.Kind, d.Name))
            .Where(d => !known.Any(k => k.Section == d.Section && k.Kind == d.Kind)).Select(d => d.Name).ToList();
        yield return (missing.Count == 0, $"device pages: every built-in kind has controller pages{(missing.Count > 0 ? " — missing: " + string.Join(", ", missing) : "")}");

        using var e = new NotaEngine();
        var unresolved = new List<string>();
        var noPages = new List<string>();
        foreach (var (section, kind, key) in known)
        {
            int t = e.AddInstrumentTrack();
            DeviceAddress a;
            switch (section)
            {
                case FocusSection.Instrument: e.SetTrackBuiltinInstrument(t, kind); a = DeviceAddress.Instrument(t); break;
                case FocusSection.Audio: a = new DeviceAddress(t, FocusSection.Audio, e.AddBuiltinDevice(t, kind)); break;
                default: a = new DeviceAddress(t, FocusSection.Midi, e.AddMidiEffect(t, kind)); break;
            }
            var f = FocusedDevice.Build(e, a, null);
            if (f is null || f.TypeKey != key || f.PageCount == 0) { noPages.Add($"{key} ({section} {kind})"); continue; }
            for (int p = 0; p < f.PageCount; p++)
                for (int k = 0; k < f.Pages[p].Params.Count; k++)
                    if (f.Pages[p].Params[k] is { } r && f.Target(p, k) is null) unresolved.Add($"{key} page {p + 1}: \"{r}\"");
        }
        yield return (noPages.Count == 0, $"device pages: each kind resolves to its own pages on a fresh device{(noPages.Count > 0 ? " — " + string.Join(", ", noPages) : "")}");
        yield return (unresolved.Count == 0, $"device pages: every parameter on them exists{(unresolved.Count > 0 ? " — " + string.Join("; ", unresolved.Take(8)) : "")}");
        yield return (known.Count(k => k.TypeKey.StartsWith("rack:")) == 4 && DevicePageCatalog.Builtin("rack:audio") is [{ Params: ["macro:1", _, _, _, _, _, _, "macro:8"] }],
            "device pages: racks show their eight macros");

        var ids = Enumerable.Range(0, 17).Select(i => $"p{i}").ToList();
        var def = DevicePageCatalog.PluginDefault(ids);
        yield return (def.Count == 3 && def[0].Params.Count == 8 && def[2].Params is ["id:p16"] && def[1].Params[0] == "id:p8",
            "device pages: a plug-in without pages gets its parameters in order, eight per page (FR-015)");
    }

    private sealed class FakePages(string key, params DevicePage[] pages) : IDevicePageSource
    {
        public IReadOnlyList<DevicePage>? PagesFor(string typeKey) => typeKey == key ? pages : null;
    }

    // US2 (T041): the focus rule, pages, an unresolved reference, and the knob modes.
    private static IEnumerable<(bool, string)> KnobFocus()
    {
        using var e = new NotaEngine();
        int inst = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(inst, 6);           // Volt
        int fx = e.AddBuiltinDevice(inst, 1);           // Compressor
        int audio = e.AddAudioTrack();
        int eq = e.AddBuiltinDevice(audio, 16);         // EQ-3 on a track with no instrument
        int empty = e.AddAudioTrack();
        yield return (FocusedDevice.DefaultFor(e, inst) == DeviceAddress.Instrument(inst)
                      && FocusedDevice.DefaultFor(e, audio) == new DeviceAddress(audio, FocusSection.Audio, eq)
                      && FocusedDevice.DefaultFor(e, empty) is null,
            "focus: a track's instrument, else its first device, else nothing");

        var volt = FocusedDevice.Build(e, DeviceAddress.Instrument(inst), null)!;
        var comp = FocusedDevice.Build(e, new DeviceAddress(inst, FocusSection.Audio, fx), null)!;
        yield return (volt.TypeKey == "builtin:inst:volt" && comp.TypeKey == "builtin:fx:compressor"
                      && volt.Target(0, 0) is { Kind: ControllerTargetKind.PluginParam, Device: -1 } && e.PluginParamName(inst, -1, volt.Target(0, 0)!.Value.Param) == "Filter 1 Freq"
                      && comp.Target(0, 1) is { Kind: ControllerTargetKind.DeviceParam } ct && e.DeviceParamName(inst, fx, ct.Param) == "Ratio",
            "focus: knob 1 on Volt is Filter 1 Freq, knob 2 on the Compressor is Ratio");

        var edited = new FakePages("builtin:fx:compressor", new DevicePage("Mine", new[] { "Makeup", "No Such Param", null, "Thresh" }));
        var mine = FocusedDevice.Build(e, new DeviceAddress(inst, FocusSection.Audio, fx), edited)!;
        yield return (mine.PageCount == 1 && mine.PageName(0) == "Mine" && mine.Target(0, 0) is not null && mine.Target(0, 1) is null
                      && mine.Target(0, 2) is null && mine.Target(0, 3) is not null && mine.Target(0, 7) is null,
            "focus: edited pages win; a name that does not resolve and a null leave the knob empty");
        yield return (FocusedDevice.Build(e, new DeviceAddress(inst, FocusSection.Audio, 5), null) is null,
            "focus: a device that is gone resolves to nothing");

        var st = new ControllerState();
        bool p1 = st.PageStep(1, volt.PageCount), p2 = st.PageStep(10, volt.PageCount);
        int last = st.Page;
        bool p3 = st.PageStep(1, volt.PageCount);
        st.PageStep(-10, volt.PageCount);
        yield return (p1 && p2 && last == volt.PageCount - 1 && !p3 && st.Page == 0,
            "pages: page steps clamp at the first and the last page");

        var tracks = new List<int> { 11, 12, 13, 14 };
        Func<int, bool> isReturn = id => id == 13;
        int epoch = st.Epoch;
        st.SetMode(new KnobMode(KnobModeKind.Pan));
        var pan = RoleRouter.KnobTarget(st, 2, tracks, volt, isReturn);
        yield return (pan == new ControllerTarget(ControllerTargetKind.TrackPan, 13) && st.Epoch != epoch,
            "knob modes: Pan on bank origin 0 sends knob 3 to track 3's pan, and re-arms pickup");
        st.StepMode(KnobModeKind.Send, 2);
        var s1 = RoleRouter.KnobTarget(st, 1, tracks, volt, isReturn);
        var s3 = RoleRouter.KnobTarget(st, 2, tracks, volt, isReturn);
        st.StepMode(KnobModeKind.Send, 2);
        var sB = RoleRouter.KnobTarget(st, 0, tracks, volt, isReturn);
        st.StepMode(KnobModeKind.Send, 2);
        yield return (s1 == new ControllerTarget(ControllerTargetKind.TrackSend, 12, -1, 0) && s3 is null
                      && sB == new ControllerTarget(ControllerTargetKind.TrackSend, 11, -1, 1) && st.KnobMode == KnobMode.Device,
            "knob modes: Send A, then B; a return is skipped; after the last send back to Device");
        yield return (RoleRouter.KnobTarget(st, 0, tracks, volt, isReturn) == volt.Target(0, 0) && RoleRouter.KnobTarget(st, 0, tracks, null, isReturn) is null,
            "knob modes: Device follows the focused device's page; with nothing focused the knob does nothing");
    }

    // The engine's MIDI port layer (PR 1): the native self-test, then a test port standing in
    // for a controller — a claimed CC goes to the surface queue only, an unclaimed note still
    // plays and reaches MIDI Learn, and while learn listens the claimed CC reaches both.
    private static IEnumerable<(bool, string)> SurfaceLayer()
    {
        int st = NotaEngine.MidiSurfaceSelfTest();
        yield return (st == 0, $"MIDI surface self-test: claims, SysEx, loopback (failed check {st})");

        using var e = new NotaEngine();
        int src = e.MidiTestPortAdd("Smoke Surface");
        var port = e.MidiPorts().FirstOrDefault(p => p.Name == "Smoke Surface");
        yield return (src >= 0 && port.Source == src && port.OutputUid == "test:Smoke Surface",
            "a test port is listed with its source and loopback output");

        var learn = new int[4 * 16];
        var surface = new int[5 * 16];
        e.PollMidiControlEvents(learn); e.PollMidiSurfaceEvents(surface);   // start empty
        bool claimed = e.SetMidiClaims(new[] { MidiClaim.Cc(src, 0, 20) });
        e.MidiTestInject(src, new byte[] { 0xB0, 20, 64, 0x90, 60, 100 });
        int ns = e.PollMidiSurfaceEvents(surface);
        int nl = e.PollMidiControlEvents(learn);
        yield return (claimed && ns == 1 && surface[0] == src && surface[1] == 0xB0 && surface[3] == 20 && surface[4] == 64,
            "a claimed CC reaches the surface queue");
        yield return (nl == 1 && learn[0] == 1 && learn[2] == 60,
            "the claimed CC is not learnable, the unclaimed note is");
        e.MidiTestInject(src, new byte[] { 0x80, 60, 0 });   // release the note it played

        e.SetMidiLearnListening(true);
        e.MidiTestInject(src, new byte[] { 0xB0, 20, 1 });
        ns = e.PollMidiSurfaceEvents(surface);
        nl = e.PollMidiControlEvents(learn);
        e.SetMidiLearnListening(false);
        yield return (ns == 1 && nl == 1 && learn[0] == 0 && learn[2] == 20,
            "while MIDI Learn listens a claimed CC reaches both queues");

        yield return (!e.SetMidiClaims(new[] { new MidiClaim(src, 0, 0xC0, 0, 0) }),
            "a program-change claim is refused");

        // With no claims, only an identity request opens the paired input's SysEx window; the
        // loopback keeps what was sent.
        e.SetMidiClaims(ReadOnlySpan<MidiClaim>.Empty);
        bool sent = e.MidiSend(port.OutputUid, new byte[] { 0xF0, 0x7E, 0x7F, 0x06, 0x01, 0xF7 });
        var outBuf = new byte[64];
        int no = e.MidiTestTakeSent(src, outBuf);
        e.MidiTestInject(src, new byte[] { 0xF0, 0x7E, 0x00, 0x06, 0x02, 0x00, 0x20, 0x6B, 0x02, 0x00, 0x05, 0x54, 1, 0, 0, 0, 0xF7 });
        var sx = new byte[512];
        int nx = e.PollMidiSysex(out int sxSrc, sx);
        yield return (sent && no == 6 && outBuf[0] == 0xF0 && outBuf[4] == 0x01,
            "sent bytes reach the test port's loopback");
        yield return (nx == 17 && sxSrc == src && sx[4] == 0x02,
            "the identity reply is let in after an identity request");
    }

    // The example from contracts/controller-profile.md, in the shape users will copy.
    private const string Example = """
        {
          "format": 1,
          "id": "arturia.keylab-essential-61",
          "name": "Arturia KeyLab Essential 61",
          "protocol": "generic",
          "source": "Arturia KeyLab Essential — User Manual, MIDI implementation chart",
          "match": {
            "portNames": ["^KeyLab Essential 61( MIDI)?$"],
            "identity": { "manufacturer": "00 20 6B", "family": "02 00", "model": "05 54" }
          },
          "init": ["F0 00 20 6B 7F 42 02 00 40 51 00 F7"],
          "release": [],
          "pickup": true,
          "controls": [
            { "role": "knob",  "index": 1, "message": "cc", "channel": 1, "number": 74, "encoder": "relative-twos" },
            { "role": "fader", "index": 1, "message": "cc", "channel": 1, "number": 73 },
            { "role": "play",  "message": "note", "channel": 1, "number": 94, "feedback": { "on": "90 5E 7F", "off": "90 5E 00" } },
            { "role": "page-next", "message": "cc", "channel": 1, "number": 113 },
            { "role": "fader", "index": 9, "message": "pitchbend", "channel": 9, "motorised": true },
            { "role": "pad",   "index": 1, "message": "note", "channel": 10, "number": 36 },
          ]
        }
        """;

    private static string With(string from, string to) => Example.Replace(from, to);

    private static IEnumerable<(bool, string)> Profiles()
    {
        var (p, w) = ProfileLoader.Load(Example, "keylab.json", ProfileOrigin.Shipped);
        yield return (p is not null && w.Count == 0 && p.Controls.Count == 6 && p.Init.Count == 1 && p.Pickup
                      && p.Identity is { Manufacturer.Length: 3 } && p.PortNames.Count == 1,
            "profile: the contract example loads whole");
        var knob = p?.Controls[0];
        yield return (knob is { Role: { Kind: ControlRoleKind.Knob, Index: 0 }, Channel: 0, Number: 74, Encoder: EncoderMode.RelativeTwos },
            "profile: a knob keeps its 0-based index, channel and encoder mode");
        var bend = p?.Controls[4];
        yield return (bend is { Message: ControlMessage.PitchBend, Channel: 8, Motorised: true, Role.Index: 8 },
            "profile: a motorised pitch-bend fader");
        yield return (p?.Controls[2].Feedback?.On is [0x90, 0x5E, 0x7F] && p.Controls[5].Role.IsClaimed == false && p.Controls[2].Role.IsClaimed,
            "profile: LED feedback bytes; pads are never claimed");

        var (p2, w2) = ProfileLoader.Load(With("\"format\": 1", "\"format\": 2"), "f2.json", ProfileOrigin.User);
        yield return (p2 is null && w2.Count == 1 && w2[0].Contains("newer Nota"), "profile: a newer format is refused with a reason");

        var (p3, w3) = ProfileLoader.Load(Example.Replace("\"match\": {", "\"nomatch\": {"), "nm.json", ProfileOrigin.User);
        yield return (p3 is null && w3[0].StartsWith("nm.json:"), "profile: no \"match\" → skipped, the file is named");

        var (p4, w4) = ProfileLoader.Load(With("\"number\": 73 }", "\"number\": 74, \"encoder\": \"relative-twos\" }"), "dup.json", ProfileOrigin.User);
        yield return (p4 is { Controls.Count: 5 } && w4.Any(m => m.Contains("same message")), "profile: a duplicate control is skipped with a warning");

        const string CmdJson = """
            { "format": 1, "id": "cmd", "name": "Cmd", "match": { "portNames": ["^cmd$"] }, "controls": [
              { "role": "command", "command": "edit.undo", "message": "cc", "channel": 1, "number": 42 },
              { "role": "command", "message": "cc", "channel": 1, "number": 43 },
              { "role": "command", "command": "session.selectDown", "commandLeft": "session.selectUp", "message": "cc", "channel": 1, "number": 44, "encoder": "relative-twos" } ] }
            """;
        var (pc, wc) = ProfileLoader.Load(CmdJson, "cmd.json", ProfileOrigin.User);
        yield return (pc is { Controls: [{ Role: { Kind: ControlRoleKind.Command, Command: "edit.undo", IsClaimed: true } }, { Role.CommandLeft: "session.selectUp" }] } && wc.Count == 1,
            "profile: a command button keeps its command id; one without an id is skipped with a warning");

        var (p5, w5) = ProfileLoader.Load(With("F0 00 20 6B 7F 42 02 00 40 51 00 F7", "XYZ"), "hex.json", ProfileOrigin.User);
        yield return (p5 is { Init.Count: 0 } && w5.Any(m => m.Contains("\"init\"")), "profile: bad init hex is skipped, the profile still loads");

        var (p6, _) = ProfileLoader.Load(With("\"source\": \"Arturia KeyLab Essential — User Manual, MIDI implementation chart\",", ""), "nosrc.json", ProfileOrigin.Shipped);
        var (p7, _) = ProfileLoader.Load(With("\"source\": \"Arturia KeyLab Essential — User Manual, MIDI implementation chart\",", ""), "nosrc.json", ProfileOrigin.User);
        yield return (p6 is null && p7 is not null, "profile: a shipped profile must cite its source, a user one need not");

        var (p8, w8) = ProfileLoader.Load("{ not json", "broken.json", ProfileOrigin.User);
        yield return (p8 is null && w8.Count == 1 && w8[0].Contains("not valid JSON"), "profile: broken JSON is reported, not thrown");
    }

    private static IEnumerable<(bool, string)> Matching()
    {
        ControllerProfile Load(string json, string file, ProfileOrigin o) => ProfileLoader.Load(json, file, o).Profile!;
        var byIdentity = Load(Example, "a-identity.json", ProfileOrigin.Shipped);
        var byName = Load(With("\"id\": \"arturia.keylab-essential-61\"", "\"id\": \"by-name\"")
            .Replace("\"identity\": { \"manufacturer\": \"00 20 6B\", \"family\": \"02 00\", \"model\": \"05 54\" }", "\"identity\": null"), "b-name.json", ProfileOrigin.Shipped);
        var vague = Load(With("\"id\": \"arturia.keylab-essential-61\"", "\"id\": \"vague\"")
            .Replace(", \"family\": \"02 00\", \"model\": \"05 54\"", ""), "c-vague.json", ProfileOrigin.Shipped);
        var reply = IdentityReply.TryParse(new byte[] { 0xF0, 0x7E, 0x00, 0x06, 0x02, 0x00, 0x20, 0x6B, 0x02, 0x00, 0x05, 0x54, 1, 0, 0, 0, 0xF7 });
        var shipped = new[] { byName, vague, byIdentity };

        yield return (reply is { Manufacturer.Length: 3 } && reply.Model[1] == 0x54, "identity reply: 3-byte manufacturer parsed");
        var short1 = IdentityReply.TryParse(new byte[] { 0xF0, 0x7E, 0x10, 0x06, 0x02, 0x47, 0x32, 0x00, 0x19, 0x00, 1, 0, 0, 0, 0xF7 });
        yield return (short1 is { Manufacturer: [0x47] } && short1.Family[0] == 0x32, "identity reply: 1-byte manufacturer parsed");
        yield return (IdentityReply.TryParse(new byte[] { 0xF0, 0x7E, 0x00, 0x06, 0x02, 0xF7 }) is null, "identity reply: a truncated reply is rejected");

        yield return (ProfileMatcher.Match(shipped, "KeyLab Essential 61", reply) == byIdentity,
            "match: the most specific identity wins over a vaguer one and over the port name");
        yield return (ProfileMatcher.Match(shipped, "KeyLab Essential 61", null) is { } m && m.PortNames.Count > 0,
            "match: no reply → the port name decides");
        var user = Load(With("\"id\": \"arturia.keylab-essential-61\"", "\"id\": \"mine\""), "z-user.json", ProfileOrigin.User);
        yield return (ProfileMatcher.Match(shipped.Append(user).ToList(), "KeyLab Essential 61", reply) == user,
            "match: a user profile beats shipped ones");
        yield return (ProfileMatcher.Match(shipped, "Launchpad", null) is null, "match: nothing fits → plain MIDI");
        yield return (ProfileMatcher.Match(new[] { byIdentity }, "KeyLab Essential 61 MCU", reply) is null,
            "match: a profile naming both an identity and port names needs both (a multi-port device's other ports stay plain)");
    }

    private static ControlDecl Decl(ControlRoleKind kind, ControlMessage msg, int number, EncoderMode enc = EncoderMode.Absolute, int index = 0, int channel = 0)
        => new(new ControlRole(kind, index), msg, channel, number, enc, false, null);

    private static IEnumerable<(bool, string)> Decoding()
    {
        var d = new ControlDecoder();
        int Rel(EncoderMode m, int v) => ControlDecoder.RelativeSteps(m, v);
        yield return (Rel(EncoderMode.RelativeTwos, 1) == 1 && Rel(EncoderMode.RelativeTwos, 127) == -1 && Rel(EncoderMode.RelativeTwos, 65) == -63,
            "decode: relative two's complement (1 → +1, 127 → −1, 65 → −63)");
        yield return (Rel(EncoderMode.RelativeSignBit, 1) == 1 && Rel(EncoderMode.RelativeSignBit, 65) == -1 && Rel(EncoderMode.RelativeSignBit, 0x45) == -5,
            "decode: relative sign-bit (1 → +1, 65 → −1)");
        yield return (Rel(EncoderMode.RelativeOffset, 65) == 1 && Rel(EncoderMode.RelativeOffset, 63) == -1 && Rel(EncoderMode.RelativeOffset, 64) == 0,
            "decode: relative binary offset (65 → +1, 63 → −1)");

        var abs = Decl(ControlRoleKind.Fader, ControlMessage.Cc, 7);
        var v = d.Decode(abs, 0, 0xB0, 0, 7, 127);
        yield return (v is { IsRelative: false, Position: 1.0 }, "decode: absolute 7-bit CC");

        var c14 = Decl(ControlRoleKind.Fader, ControlMessage.Cc14, 1);
        var msbOnly = d.Decode(c14, 0, 0xB0, 0, 1, 64);
        var lsb = d.Decode(c14, 0, 0xB0, 0, 33, 127);
        var msb2 = d.Decode(c14, 0, 0xB0, 0, 1, 64);
        var otherSource = d.Decode(c14, 1, 0xB0, 0, 1, 64);
        yield return (msbOnly is { Position: var p1 } && Math.Abs(p1 - 64 / 127.0) < 1e-9
                      && lsb is { Position: var p2 } && Math.Abs(p2 - ((64 << 7) | 127) / 16383.0) < 1e-9
                      && msb2 is { Position: var p3 } && Math.Abs(p3 - p2) < 1e-9
                      && otherSource is { Position: var p4 } && Math.Abs(p4 - 64 / 127.0) < 1e-9,
            "decode: 14-bit CC pairs MSB + LSB per source; MSB alone runs at 7-bit");

        var bend = Decl(ControlRoleKind.Fader, ControlMessage.PitchBend, 0);
        yield return (d.Decode(bend, 0, 0xE0, 0, 0x7F, 0x7F) is { Position: 1.0 } && d.Decode(bend, 0, 0xE0, 0, 0, 0) is { Position: 0.0 },
            "decode: a pitch-bend fader is 14-bit 0..1");

        var nrpn = Decl(ControlRoleKind.Knob, ControlMessage.Nrpn, 300);
        d.Observe(0, 0xB0, 2, 99, 300 >> 7);
        d.Observe(0, 0xB0, 2, 98, 300 & 0x7F);
        var n6 = d.Decode(nrpn with { Channel = 2 }, 0, 0xB0, 2, 6, 100);
        var n38 = d.Decode(nrpn with { Channel = 2 }, 0, 0xB0, 2, 38, 5);
        var inc = d.Decode(nrpn with { Channel = 2 }, 0, 0xB0, 2, 96, 1);
        var dec = d.Decode(nrpn with { Channel = 2 }, 0, 0xB0, 2, 97, 3);
        d.Observe(0, 0xB0, 3, 99, 0); d.Observe(0, 0xB0, 3, 98, 1);
        var wrongParam = d.Decode(nrpn with { Channel = 3 }, 0, 0xB0, 3, 6, 100);
        yield return (n6 is { IsRelative: false } && n38 is { Position: var np } && Math.Abs(np - ((100 << 7) | 5) / 16383.0) < 1e-9
                      && inc is { Steps: 1 } && dec is { Steps: -3 } && wrongParam is null,
            "decode: NRPN 99/98 + 6/38 assembles a 14-bit value, 96/97 step, per channel and parameter");

        var note = Decl(ControlRoleKind.Play, ControlMessage.Note, 94);
        yield return (d.Decode(note, 0, 0x90, 0, 94, 127) is { Position: 1.0 } && d.Decode(note, 0, 0x90, 0, 94, 0) is { Position: 0.0 }
                      && d.Decode(note, 0, 0x80, 0, 94, 64) is { Position: 0.0 },
            "decode: a note button reads 1 on press, 0 on release (note-off or velocity 0)");
    }

    // Story 7 (T091 / T095): unbinds and pins, global vs project, track positions; page edits.
    private static IEnumerable<(bool, string)> Overrides()
    {
        var dir = Directory.CreateTempSubdirectory("nota-overrides-").FullName;
        try
        {
            string global = Path.Combine(dir, "controllers", "overrides.json"), project = Path.Combine(dir, "song.nota");
            Directory.CreateDirectory(project);
            const int Play = 8, Volume = 3;   // MidiTargetKind.TransportPlay / TrackVolume as saved
            var trackIds = new[] { 101, 102, 103 };
            int IdAt(int i) => i >= 0 && i < trackIds.Length ? trackIds[i] : -1;
            int IndexOf(int id) => Array.IndexOf(trackIds, id);

            var o = new ControllerOverrides(global);
            o.LoadGlobal();
            o.LoadProject(null, IdAt);
            var play = new StoredTarget(Play);
            var vol3 = new StoredTarget(Volume, 103);
            o.Unbind(play);
            o.Pin("arturia.keylab-essential-61-mk3-daw", "cc:1:105", "Fader 1", vol3);
            yield return (o.IsUnbound(play) && o.PinOf("arturia.keylab-essential-61-mk3-daw", "cc:1:105")?.Target == vol3 && o.PinsFor(vol3).Count() == 1,
                "overrides: unbind Play, pin fader 1 to track 3's volume");

            o.SaveProject(project, IndexOf);
            var fresh = new ControllerOverrides(global);
            fresh.LoadGlobal();
            fresh.LoadProject(null, IdAt);
            yield return (fresh.IsUnbound(play) && !fresh.Pins.Any(),
                "overrides: a track-free unbind is global; a track pin isn't (another project doesn't see it)");

            trackIds = new[] { 201, 202, 203 };   // the project reopened: new runtime ids, same positions
            fresh.LoadProject(project, IdAt);
            yield return (fresh.Pins.SingleOrDefault() is { Target: { Kind: Volume, TrackId: 203 }, Label: "Fader 1" } && fresh.IsUnbound(play),
                "overrides: the project brings its track pin back by position");

            fresh.Reset(play);
            fresh.Pin("arturia.keylab-essential-61-mk3-daw", "cc:1:105", "Fader 1", new StoredTarget(Volume, 201));
            yield return (!fresh.IsUnbound(play) && fresh.Pins.Count() == 1 && fresh.Pins.Single().Target.TrackId == 201,
                "overrides: Back to default drops the unbind; re-pinning a control moves its one pin");
            var g2 = new ControllerOverrides(global); g2.LoadGlobal(); g2.LoadProject(null, IdAt);
            yield return (!g2.IsUnbound(play), "overrides: Back to default reaches the global file too");

            yield return (ControllerOverrides.ControlKey(new ControlDecl(new ControlRole(ControlRoleKind.Fader), ControlMessage.Cc, 0, 105, EncoderMode.Absolute, false, null)) == "cc:1:105",
                "overrides: a control's key is its message, channel (1-based) and number");
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }

        var pages = new List<DevicePage>
        {
            new("Main", new string?[] { "Cutoff", "Reso", null, "Drive", null, null, null, null }),
            new("Env", new string?[] { "Attack", "Decay", null, null, null, null, null, null }),
        };
        var un = FocusedDevice.WithoutParam(pages, "Drive");
        yield return (un[0].Params[3] is null && un[0].Params[0] == "Cutoff" && un[1].Params[0] == "Attack",
            "page edit: Unbind empties the parameter's slot only");
        var moved = FocusedDevice.WithParamAt(pages, "Attack", 0, 3);
        yield return (moved[0].Params[3] == "Attack" && moved[1].Params[0] is null && !moved[0].Params.Contains("Drive"),
            "page edit: Bind to ▸ Knob 4 takes the slot (Drive leaves it) and off its old page");
        var far = FocusedDevice.WithParamAt(pages, "Reso", 2, 0);
        yield return (far.Count == 3 && far[2].Params[0] == "Reso" && far[0].Params[1] is null,
            "page edit: a page past the end is added");
    }

    // Review follow-ups on nota#928.
    private static IEnumerable<(bool, string)> ReviewFixes()
    {
        // Two NRPN knobs on one channel: the selected parameter picks the declaration.
        var n1 = new ControlDecl(new ControlRole(ControlRoleKind.Knob, 0), ControlMessage.Nrpn, 0, 300, EncoderMode.Absolute, false, null);
        var n2 = new ControlDecl(new ControlRole(ControlRoleKind.Knob, 1), ControlMessage.Nrpn, 0, 301, EncoderMode.Absolute, false, null);
        var prof = new ControllerProfile("t", "T", ControllerProtocol.Generic, null, Array.Empty<System.Text.RegularExpressions.Regex>(), null,
            Array.Empty<byte[]>(), Array.Empty<byte[]>(), true, false, new[] { n1, n2 }, ProfileOrigin.User, "t.json");
        var dec = new ControlDecoder();
        foreach (var (d1, d2) in new[] { (99, 301 >> 7), (98, 301 & 0x7F) }) dec.Observe(0, 0xB0, 0, d1, d2);
        var found = RoleRouter.Find(prof, 0xB0, 0, 6, dec.NrpnParam(0, 0));
        var value = found is null ? null : dec.Decode(found, 0, 0xB0, 0, 6, 100);
        yield return (ReferenceEquals(found, n2) && value is { IsRelative: false }, "NRPN: the second knob on a channel is found by its selected parameter");

        // A soft note press still presses a button.
        var r = new RoleRouter();
        var playNote = new ControlDecl(new ControlRole(ControlRoleKind.Play), ControlMessage.Note, 0, 60, EncoderMode.Absolute, false, null);
        var soft = r.Route(playNote, ControlValue.Absolute(20 / 127.0), new ControllerState(), new List<int>());
        yield return (soft is [{ Kind: ControllerActionKind.Trigger }], "a note button fires at velocity 20 too");

        // Mistyped numbers are warnings, not exceptions.
        const string Mistyped = """
            { "format": 1, "id": "m", "name": "M", "match": { "portNames": ["^m$"] }, "controls": [
              { "role": "knob", "index": "1", "message": "cc", "channel": 1, "number": 20 },
              { "role": "knob", "index": 2, "message": "cc", "channel": "1", "number": 21 },
              { "role": "knob", "index": 3, "message": "cc", "channel": 1, "number": null } ] }
            """;
        bool threw = false;
        (ControllerProfile? P, IReadOnlyList<string> W) loaded = (null, Array.Empty<string>());
        try { loaded = ProfileLoader.Load(Mistyped, "m.json", ProfileOrigin.User); } catch (InvalidOperationException) { threw = true; }
        yield return (!threw && loaded.P is { Controls.Count: 0 } && loaded.W.Count == 3, "profile: string / null numbers are skipped with warnings, not thrown");
        bool pagesThrew = false;
        try { DevicePagesFile.Parse("""{ "format": "1", "types": {} }""", out _); } catch (InvalidOperationException) { pagesThrew = true; }
        yield return (!pagesThrew, "device pages: a string format is an error, not an exception");

        // A rack pin keeps its whole address through the project file.
        var rackGain = new StoredTarget(11, 101, 2, 3, -1);   // RackChainGain: chain 2 of the rack at device 3
        var json = ControllerOverrides.Write(Array.Empty<StoredTarget>(), new[] { new ControlPin("p", "cc:1:20", "Knob 1", rackGain) }, id => id == 101 ? 0 : -1, true);
        var back = ControllerOverrides.Parse(json, i => i == 0 ? 201 : -1);
        yield return (back?.Pins.SingleOrDefault()?.Target == rackGain with { TrackId = 201 }, "overrides: a rack chain pin keeps its rack and chain");

        // Value feedback fills its template.
        var ring = ControlFeedback.Fill("B0 10 vv", 16383);
        var motor = ControlFeedback.Fill("E0 ll hh", 8192);
        yield return (ring is [0xB0, 0x10, 0x7F] && motor is [0xE0, 0x00, 0x40] && ControlFeedback.Fill("zz vv", 1) is null,
            "value feedback: vv / ll hh filled, a non-hex template refused");
    }

    private static IEnumerable<(bool, string)> Routing()
    {
        var play = Decl(ControlRoleKind.Play, ControlMessage.Note, 94);
        var ccPlay = Decl(ControlRoleKind.Play, ControlMessage.Cc, 20);
        var fader2 = Decl(ControlRoleKind.Fader, ControlMessage.Cc, 7, index: 1);
        var relFader = Decl(ControlRoleKind.Fader, ControlMessage.Cc, 8, EncoderMode.RelativeTwos, index: 0);
        var bankNext = Decl(ControlRoleKind.BankNext, ControlMessage.Note, 47);
        var pad = Decl(ControlRoleKind.Pad, ControlMessage.Note, 36, channel: 9);
        var button = Decl(ControlRoleKind.Button, ControlMessage.Cc, 30, index: 0);
        var r = new RoleRouter();
        var st = new ControllerState();
        var tracks = Enumerable.Range(101, 20).ToList();   // 20 track ids

        var a1 = r.Route(play, ControlValue.Absolute(1), st, tracks);
        var a2 = r.Route(play, ControlValue.Absolute(1), st, tracks);   // still held: no second fire
        r.Route(play, ControlValue.Absolute(0), st, tracks);
        var a3 = r.Route(play, ControlValue.Absolute(1), st, tracks);
        var c1 = r.Route(ccPlay, ControlValue.Absolute(1), st, tracks);
        var c2 = r.Route(ccPlay, ControlValue.Absolute(0.9), st, tracks);
        yield return (a1 is [{ Kind: ControllerActionKind.Trigger, Target.Kind: ControllerTargetKind.TransportPlay }] && a2.Count == 0 && a3.Count == 1
                      && c1.Count == 1 && c2.Count == 0,
            "route: a transport button fires once per press (note or CC)");
        var undo = new ControlDecl(new ControlRole(ControlRoleKind.Command, 0, "edit.undo"), ControlMessage.Cc, 0, 42, EncoderMode.Absolute, false, null);
        var u1 = r.Route(undo, ControlValue.Absolute(1), st, tracks);
        var u2 = r.Route(undo, ControlValue.Absolute(1), st, tracks);
        r.Route(undo, ControlValue.Absolute(0), st, tracks);
        yield return (u1 is [{ Kind: ControllerActionKind.Command, Command: "edit.undo" }] && u2.Count == 0,
            "route: a command button runs its command once per press");
        var wheel = new ControlDecl(new ControlRole(ControlRoleKind.Command, 0, "session.selectDown", "session.selectUp"), ControlMessage.Cc, 0, 116, EncoderMode.RelativeOffset, false, null);
        var w1 = r.Route(wheel, ControlValue.Relative(2), st, tracks);
        var w2 = r.Route(wheel, ControlValue.Relative(-1), st, tracks);
        yield return (w1 is [{ Kind: ControllerActionKind.Command, Command: "session.selectDown", Index: 2 }] && w2 is [{ Command: "session.selectUp", Index: 1 }],
            "route: a two-way encoder runs its right command per step right, its left command per step left");
        var knobWheel = wheel with { Encoder = EncoderMode.Absolute };
        var k0 = r.Route(knobWheel, ControlValue.Absolute(64 / 127.0), st, tracks);
        var k1 = r.Route(knobWheel, ControlValue.Absolute(66 / 127.0), st, tracks);
        var k2 = r.Route(knobWheel, ControlValue.Absolute(65 / 127.0), st, tracks);
        yield return (k0.Count == 0 && k1 is [{ Command: "session.selectDown", Index: 2 }] && k2 is [{ Command: "session.selectUp", Index: 1 }],
            "route: an absolute knob as a two-way control steps by its value going up or down");
        var trackWheel = new ControlDecl(new ControlRole(ControlRoleKind.TrackNext, LeftKind: ControlRoleKind.TrackPrev), ControlMessage.Cc, 0, 50, EncoderMode.RelativeTwos, false, null);
        var tw1 = r.Route(trackWheel, ControlValue.Relative(3), st, tracks);
        var tw2 = r.Route(trackWheel, ControlValue.Relative(-1), st, tracks);
        yield return (tw1.Count == 3 && tw1.All(x => x is { Kind: ControllerActionKind.TrackStep, Index: 1 }) && tw2 is [{ Kind: ControllerActionKind.TrackStep, Index: -1 }],
            "route: an encoder for next / previous track selects one track per click, either way (specs/107)");

        // The wizard's profile keeps a two-way role and a two-way command; a wrong pair is refused.
        var learnedPair = new List<LearnedControl>
        {
            new(ControlRoleKind.TrackNext, 0, new ClassifiedControl(ControlMessage.Cc, 0, 50, EncoderMode.RelativeTwos), LeftRole: ControlRoleKind.TrackPrev),
            new(ControlRoleKind.Command, 0, new ClassifiedControl(ControlMessage.Cc, 0, 51, EncoderMode.RelativeTwos), "transport.tempoUp", "transport.tempoDown"),
        };
        var (pp, pw) = ProfileLoader.Load(ProfileWriter.Write("Pair Keys", null, learnedPair), "user.pair-keys.json", ProfileOrigin.User);
        yield return (pp is { Controls: [{ Role: { Kind: ControlRoleKind.TrackNext, LeftKind: ControlRoleKind.TrackPrev } }, { Role: { Command: "transport.tempoUp", CommandLeft: "transport.tempoDown" } }] } && pw.Count == 0,
            "wizard profile: an encoder for tracks (roleLeft) and one for tempo (commandLeft) load back");
        const string BadPair = """
            { "format": 1, "id": "bp", "name": "BP", "match": { "portNames": ["^bp$"] }, "controls": [
              { "role": "track-next", "roleLeft": "bank-prev", "message": "cc", "channel": 1, "number": 50, "encoder": "relative-twos" } ] }
            """;
        var (bp, bw) = ProfileLoader.Load(BadPair, "bp.json", ProfileOrigin.User);
        yield return (bp is { Controls: [{ Role.LeftKind: null }] } && bw.Count == 1, "profile: roleLeft that isn't the other half is ignored with a warning");

        st.BankStep(8, tracks.Count);
        var f = r.Route(fader2, ControlValue.Absolute(0.5), st, tracks);
        yield return (f is [{ Kind: ControllerActionKind.SetNormalized, Target: { Kind: ControllerTargetKind.TrackVolume, TrackId: 110 }, Value: 0.5 }],
            "route: fader 2 with the bank at track 9 drives track 10's volume");
        var rf = r.Route(relFader, ControlValue.Relative(-2), st, tracks);
        yield return (rf is [{ Kind: ControllerActionKind.Nudge, Value: var nv }] && Math.Abs(nv + 2 * RoleRouter.StepFraction) < 1e-12,
            "route: a relative fader nudges by its steps");

        var b = new ControllerState();
        bool m1 = b.BankStep(8, 20), m2 = b.BankStep(8, 20), m3 = b.BankStep(8, 20);
        int atEnd = b.BankOrigin;
        b.BankStep(-8, 20); b.BankStep(-8, 20); bool m4 = b.BankStep(-8, 20);
        var small = new ControllerState();
        bool m5 = small.BankStep(8, 5);
        var ch = new ControllerState();
        for (int i = 0; i < 30; i++) ch.BankStep(1, 20);
        yield return (m1 && m2 && !m3 && atEnd == 12 && b.BankOrigin == 0 && !m4 && !m5 && ch.BankOrigin == 19,
            "route: banks clamp at the last full bank and at 0; channel steps stop at the last track");

        var bn = r.Route(bankNext, ControlValue.Absolute(1), st, tracks);
        yield return (bn is [{ Kind: ControllerActionKind.BankStep, Index: 8 }], "route: bank-next asks for +8");

        var profile = new ControllerProfile("t", "T", ControllerProtocol.Generic, null, Array.Empty<System.Text.RegularExpressions.Regex>(), null,
            Array.Empty<byte[]>(), Array.Empty<byte[]>(), true, false, new[] { play, fader2, pad, button, Decl(ControlRoleKind.Fader, ControlMessage.Cc14, 3, index: 2) },
            ProfileOrigin.User, "t.json");
        var none = new HashSet<(bool, int, int)>();
        var claims = RoleRouter.BuildClaims(profile, 4, none);
        yield return (claims.Count == 4 && claims.All(c => c.Source == 4 && c.WholePort == 0)
                      && claims.Any(c => c is { Status: 0x80, Data1: 94 }) && claims.Any(c => c is { Status: 0xB0, Data1: 35 })
                      && !claims.Any(c => c.Data1 == 36 || c.Data1 == 30),
            "claims: declared roles (a 14-bit fader with its LSB), never pads or buttons");
        var learned = new HashSet<(bool, int, int)> { (false, 0, 7) };
        var claims2 = RoleRouter.BuildClaims(profile, 4, learned);
        yield return (claims2.Count == 3 && !claims2.Any(c => c is { Status: 0xB0, Data1: 7 }),
            "claims: a control learned with MIDI Learn is released to it (FR-012)");
        var whole = RoleRouter.BuildClaims(profile with { ClaimAll = true }, 4, none);
        var mcu = RoleRouter.BuildClaims(profile with { Protocol = ControllerProtocol.Mcu }, 4, none);
        yield return (whole is [{ WholePort: 1, Source: 4 }] && mcu is [{ WholePort: 1 }],
            "claims: claimAll and MCU profiles take the whole port");
        yield return (RoleRouter.Find(profile, 0x90, 0, 94) == play && RoleRouter.Find(profile, 0xB0, 0, 35) is { Message: ControlMessage.Cc14 }
                      && RoleRouter.Find(profile, 0xB0, 1, 7) is null,
            "route: a message finds its declared control (LSB of a 14-bit pair too), not on another channel");
    }

    private static string? RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "Nota.sln"))) d = d.Parent;
        return d?.FullName;
    }

    // The shipped profiles, as files, through the engine: a test port named like the device is
    // recognised, its claims keep role messages away from the instruments, and the routed actions
    // are the transport and the bank's faders. Unclaimed notes still reach the learn copy.
    private static IEnumerable<(bool, string)> ShippedEndToEnd()
    {
        var dir = Path.Combine(RepoRoot() ?? ".", "src", "managed", "Nota.App", "Controllers", "Profiles");
        var files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.json").Order().ToArray() : Array.Empty<string>();
        var loaded = files.Select(f => ProfileLoader.Load(File.ReadAllText(f), Path.GetFileName(f), ProfileOrigin.Shipped)).ToList();
        var warnings = loaded.SelectMany(l => l.Warnings).ToList();
        var profiles = loaded.Where(l => l.Profile is not null).Select(l => l.Profile!).ToList();
        yield return (files.Length >= 2 && profiles.Count == files.Length && warnings.Count == 0,
            $"shipped profiles load clean ({profiles.Count}/{files.Length}{(warnings.Count > 0 ? ": " + warnings[0] : "")})");
        yield return (profiles.All(p => p.Source is { Length: > 10 }), "every shipped profile cites its source document");

        // Every profile file is embedded in the app under its own name (a file name like
        // "x-touch-mini" once passed for a culture and went into a satellite assembly).
        string config = AppContext.BaseDirectory.Contains("/Debug/") ? "Debug" : "Release";
        var appDll = Path.Combine(RepoRoot() ?? ".", "src", "managed", "Nota.App", "bin", config, "net10.0", "Nota.App.dll");
        if (File.Exists(appDll))
        {
            using var fs = File.OpenRead(appDll);
            using var pe = new System.Reflection.PortableExecutable.PEReader(fs);
            var md = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
            var names = md.ManifestResources.Select(h => md.GetString(md.GetManifestResource(h).Name)).ToHashSet();
            var missing = files.Select(f => "Nota.Controllers." + Path.GetFileName(f)).Where(n => !names.Contains(n)).ToList();
            yield return (missing.Count == 0, $"every shipped profile is embedded in Nota.App{(missing.Count > 0 ? " — missing: " + string.Join(", ", missing) : "")}");
        }

        var keylabReply = IdentityReply.TryParse(new byte[] { 0xF0, 0x7E, 0x7F, 0x06, 0x02, 0x00, 0x20, 0x6B, 0x02, 0x00, 0x05, 0x74, 0x57, 0x09, 0x00, 0x01, 0xF7 });
        var keylab = ProfileMatcher.Match(profiles, "KL Essential 61 mk3 MCU/HUI", null);
        var keylabMidi = ProfileMatcher.Match(profiles, "KL Essential 61 mk3 MIDI", keylabReply);   // the port that answers
        var xtouch = ProfileMatcher.Match(profiles, "X-TOUCH MINI", null);
        yield return (keylab?.Id == "arturia.keylab-essential-61-mk3" && keylabMidi?.Id == "arturia.keylab-essential-61-mk3-daw"
                      && ProfileMatcher.Match(profiles, "KL Essential 61 mk3 MIDI", null) is null && xtouch?.Id == "behringer.x-touch-mini",
            "KeyLab mk3's MCU port, its MIDI port (DAW program, by identity) and the X-Touch Mini are recognised");
        // A command button names a command the app declares (a typo would make the button dead).
        var commandIds = CommandCatalog.Create().Select(c => c.Id).ToHashSet();
        var badCommands = profiles.SelectMany(p => p.Controls.Where(c => c.Role.Kind == ControlRoleKind.Command && (!commandIds.Contains(c.Role.Command!) || (c.Role.CommandLeft is { } l && !commandIds.Contains(l))))
            .Select(c => $"{p.Id}: {c.Role.Command}")).ToList();
        yield return (badCommands.Count == 0, $"every shipped command button names a declared command{(badCommands.Count > 0 ? " — unknown: " + string.Join(", ", badCommands) : "")}");
        var kd = keylabMidi?.Controls ?? Array.Empty<ControlDecl>();
        yield return (kd.Count(c => c.Role.Kind == ControlRoleKind.Knob) == 9 && kd.Count(c => c.Role.Kind == ControlRoleKind.Fader) == 8
                      && kd.Any(c => c.Role is { Kind: ControlRoleKind.Command, Command: "edit.undo" } && c.Number == 42)
                      && kd.Any(c => c.Role.Kind == ControlRoleKind.Jog && c.Number == 116 && c.Encoder == EncoderMode.RelativeOffset),
            "KeyLab DAW profile: 9 knobs, 8 faders + master, Undo on CC 42, the main encoder as a jog");
        var lk = ProfileMatcher.Match(profiles, "Launchkey MK3 49 LKMK3 DAW Out", null);
        yield return (lk is { Id: "novation.launchkey-mk3", Init.Count: 2, Release.Count: 1 } && ProfileMatcher.Match(profiles, "Launchkey MK3 49 LKMK3 MIDI Out", null) is null
                      && lk.Controls.Count(c => c.Role.Kind == ControlRoleKind.Knob) == 8 && lk.Controls.Count(c => c.Role.Kind == ControlRoleKind.Fader) == 8,
            "Launchkey MK3: its DAW port is recognised (DAW mode on connect, off on release), its MIDI port stays plain");
        if (keylab is null || xtouch is null) yield break;

        using var e = new NotaEngine();
        int kl = e.MidiTestPortAdd("KL Essential 61 mk3 MCU/HUI");
        int xt = e.MidiTestPortAdd("X-TOUCH MINI");
        var none = new HashSet<(bool, int, int)>();
        var claims = RoleRouter.BuildClaims(keylab, kl, none).Concat(RoleRouter.BuildClaims(xtouch, xt, none)).ToArray();
        bool set = e.SetMidiClaims(claims);
        var learnBuf = new int[4 * 32];
        var surf = new int[5 * 32];
        e.PollMidiControlEvents(learnBuf); e.PollMidiSurfaceEvents(surf);

        // KeyLab DAW port: Play, fader 1 to the middle, a touch note nothing declares.
        e.MidiTestInject(kl, new byte[] { 0x90, 94, 0x7F, 0xE0, 0x00, 0x40, 0x90, 104, 0x7F });
        // X-Touch Mini: encoder 1, a button (unclaimed), a key on another channel (unclaimed).
        e.MidiTestInject(xt, new byte[] { 0xBA, 1, 100, 0x9A, 8, 0x7F, 0x90, 60, 100 });
        int ns = e.PollMidiSurfaceEvents(surf);
        int nl = e.PollMidiControlEvents(learnBuf);

        var router = new RoleRouter();
        var decoder = new ControlDecoder();
        var state = new ControllerState();
        var actions = new List<ControllerAction>();
        var tracks = new List<int> { 42, 43 };
        var mcu = new McuCodec();
        var mcuInputs = new List<SurfaceInput>();
        for (int i = 0; i < ns; i++)
        {
            int src = surf[i * 5], st = surf[i * 5 + 1], ch = surf[i * 5 + 2], d1 = surf[i * 5 + 3], d2 = surf[i * 5 + 4];
            if (src == kl) { if (mcu.Decode(st, ch, d1, d2) is { } inp) mcuInputs.Add(inp); continue; }   // an MCU port: the codec
            var prof = xtouch;
            decoder.Observe(src, st, ch, d1, d2);
            if (RoleRouter.Find(prof, st, ch, d1) is not { } decl) continue;
            if (decoder.Decode(decl, src, st, ch, d1, d2) is not { } v) continue;
            actions.AddRange(router.Route(decl, v, state, tracks));
        }
        yield return (set && ns == 4, $"the KeyLab's whole DAW port and the X-Touch's encoder are claimed ({ns} surface events)");
        yield return (keylab.Protocol == ControllerProtocol.Mcu
                      && mcuInputs.Contains(new SurfaceInput(SurfaceInputKind.Play, -1, 0, true))
                      && mcuInputs.Contains(new SurfaceInput(SurfaceInputKind.Fader, 0, 0x40 << 7))
                      && mcuInputs.Contains(new SurfaceInput(SurfaceInputKind.Touch, 0, 0, true))
                      && actions.Any(a => a is { Kind: ControllerActionKind.Knob, Index: 0 }),
            "KeyLab (Mackie Control): Play, fader 1 and its touch decode; X-Touch encoder 1 → knob 1");
        yield return (nl == 2 && learnBuf[0] == 1 && learnBuf[2] == 8 && learnBuf[4] == 1 && learnBuf[6] == 60,
            "an X-Touch button and a key stay unclaimed: they still reach MIDI Learn and the instruments");

        var a2 = new ControllerState(); var b2 = new ControllerState();
        a2.BankStep(8, 20);
        yield return (a2.BankOrigin == 8 && b2.BankOrigin == 0, "two controllers bank independently");
    }
}
