// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Sidechain sub-sources (specs/050, #190): a Compressor on one track keys off a single Drum Rack
// pad (or Instrument Rack chain) on another, Pre or Post, and only that pad drives it. The key
// stays on its pad through edits, sleeps while the pad is deleted and wakes on undo, survives a
// save, and a muted "ghost" pad still drives it. Whole tracks and parts share a pool of 16 routes.
// The pads play generated sine bursts, so levels are deterministic. Also runnable alone:
// `--sidechain-parts`.

using System.Text.Json.Nodes;
using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class SidechainPartTests
{
    private const int CompKind = 1, Thresh = 0, Ratio = 1, Attack = 2, Release = 3;
    private const int KickNote = 36, HatNote = 42;

    public static IEnumerable<(bool ok, string label)> Run()
    {
        foreach (var r in KickPad()) yield return r;
        foreach (var r in StableKey()) yield return r;
        foreach (var r in InstrumentRackChain()) yield return r;
        foreach (var r in RhythmVoice()) yield return r;
        foreach (var r in RoutePool()) yield return r;
        foreach (var r in FluxReact()) yield return r;
        foreach (var r in EveryConsumer()) yield return r;
        foreach (var r in Mcp()) yield return r;
        foreach (var r in FrozenSource()) yield return r;
        foreach (var r in CopiesKeepTheKey()) yield return r;
        foreach (var r in SaveReopen()) yield return r;
    }

    // A 0.25 s sine burst at half scale: a stand-in drum hit with a known level.
    private static string Wav(float hz)
    {
        const int rate = 44100;
        var path = Path.Combine(Path.GetTempPath(), $"nota-scpart-{Guid.NewGuid():N}.wav");
        var x = new float[rate / 4 * 2];
        for (int i = 0; i < x.Length / 2; i++) x[2 * i] = x[2 * i + 1] = 0.5f * (float)Math.Sin(2 * Math.PI * hz * i / rate);
        using (var w = new Nota.Infrastructure.WavWriter(path, rate, 2, WavBitDepth.Float32)) w.WriteFrames(x, x.Length / 2);
        return path;
    }

    // Drums: a Drum Rack with a hat pad (chain 0) and a kick pad (chain 1). Bass: an empty audio
    // track with a hard Compressor, so any reduction comes from the key alone.
    private sealed class Rig : IDisposable
    {
        public readonly NotaEngine E = new();
        public readonly int Drums, Bass, Comp;
        public Rig()
        {
            E.SetBpm(120);
            Drums = E.AddDrumRackTrack();
            E.SetTrackName(Drums, "Drums");
            int hat = E.RackAddSamplerChain(Drums, Wav(3000), HatNote, false);
            E.RackSetChainTriggerNote(Drums, hat, HatNote);
            E.RackSetChainName(Drums, hat, "Hat");
            int kick = E.RackAddSamplerChain(Drums, Wav(60), KickNote, false);
            E.RackSetChainTriggerNote(Drums, kick, KickNote);
            E.RackSetChainName(Drums, kick, "Kick");
            E.AddMidiClip(Drums, 0.0, 4.0);
            Bass = E.AddAudioTrack();
            Comp = E.AddBuiltinDevice(Bass, CompKind);
            E.DeviceSetParam(Bass, Comp, Thresh, -40f);
            E.DeviceSetParam(Bass, Comp, Ratio, 10f);
            E.DeviceSetParam(Bass, Comp, Attack, 1f);
            E.DeviceSetParam(Bass, Comp, Release, 50f);
        }
        public int Chain(string name)
        {
            for (int c = 0; c < E.RackChainCount(Drums); c++)
                if (E.RackChainName(Drums, c) == name) return c;
            return -1;
        }
        // Only these pads play: four quarter-beat hits.
        public void Play(params int[] notes)
        {
            var ns = new List<NotaNote>();
            foreach (int n in notes)
                for (int b = 0; b < 4; b++) ns.Add(new NotaNote(n, b, 0.25, 1.0f));
            E.SetClipNotes(Drums, 0, ns.ToArray());
        }
        // The deepest reduction over two seconds, read block by block, and the master's RMS.
        public (float gr, double rms) Run()
        {
            const int block = 512, blocks = 172;
            var buf = new float[block * 2];
            float gr = 0;
            double sum = 0;
            E.Seek(0);
            E.Play();
            for (int i = 0; i < blocks; i++)
            {
                E.RenderOffline(buf, block);
                gr = Math.Max(gr, E.DeviceGainReduction(Bass, Comp));
                foreach (var v in buf) sum += v * v;
            }
            E.StopTransport();
            return (gr, Math.Sqrt(sum / (blocks * block * 2)));
        }
        public void Dispose() => E.Dispose();
    }

    private static IEnumerable<(bool, string)> KickPad()
    {
        using var rig = new Rig();
        var e = rig.E;
        int kick = rig.Chain("Kick"), hat = rig.Chain("Hat");
        yield return (e.SidechainRouteCapacity() == 16 && e.SidechainRoutesFree() == 16, "sc parts: a pool of 16 routes, all free");
        yield return (e.TrackPartCount(rig.Drums) == 2 && e.TrackPartName(rig.Drums, kick) == "Kick" && e.TrackPartName(rig.Drums, hat) == "Hat"
                      && e.TrackPartCount(rig.Bass) == 0, "sc parts: a Drum Rack lists its pads by name; an audio track has none");

        // Whole-track key, as before: the hat alone ducks the bass (and is loud enough to).
        e.SetDeviceSidechainSource(rig.Bass, rig.Comp, rig.Drums);
        rig.Play(HatNote);
        var whole = rig.Run();
        yield return (whole.gr > 3 && e.DeviceSidechainKey(rig.Bass, rig.Comp) == new SidechainKey(rig.Drums, -1),
            $"sc parts: a whole-track key still ducks on the hat ({whole.gr:F1} dB)");

        // Key off the kick pad: the hat no longer ducks, the kick does — Post and Pre.
        foreach (bool pre in new[] { false, true })
        {
            string tap = pre ? "Pre" : "Post";
            e.SetDeviceSidechainTapPre(rig.Bass, rig.Comp, pre);
            yield return (e.SetDeviceSidechainKey(rig.Bass, rig.Comp, rig.Drums, kick) == 0
                          && e.DeviceSidechainKey(rig.Bass, rig.Comp) == new SidechainKey(rig.Drums, kick),
                $"sc parts: key the Compressor off the kick pad ({tap})");
            rig.Play(HatNote);
            var h = rig.Run();
            rig.Play(KickNote);
            var k = rig.Run();
            yield return (h.gr < 0.1f && k.gr > 3, $"sc parts: {tap} — only the kick pad ducks (hat {h.gr:F2} dB, kick {k.gr:F1} dB)");
        }
        yield return (e.SidechainRouteInUse(rig.Drums, kick) && !e.SidechainRouteInUse(rig.Drums, -1) && e.SidechainRoutesFree() == 15,
            "sc parts: the kick pad holds one route, the whole track none");

        // Pre is before the pad's own level, Post after it.
        e.RackSetChainGain(rig.Drums, kick, 0f);
        rig.Play(KickNote);
        e.SetDeviceSidechainTapPre(rig.Bass, rig.Comp, true);
        var preSilent = rig.Run();
        e.SetDeviceSidechainTapPre(rig.Bass, rig.Comp, false);
        var postSilent = rig.Run();
        yield return (preSilent.gr > 3 && postSilent.gr < 0.1f,
            $"sc parts: a pad at zero gain still keys Pre ({preSilent.gr:F1} dB), not Post ({postSilent.gr:F2} dB)");
        e.RackSetChainGain(rig.Drums, kick, 1f);

        // Ghost kick: muted (or solo-gated) it leaves the mix but still drives the key.
        var loud = rig.Run();
        e.RackSetChainMute(rig.Drums, kick, true);
        var ghost = rig.Run();
        yield return (ghost.gr > 3 && ghost.rms < loud.rms * 0.05,
            $"sc parts: a muted kick pad keeps ducking ({ghost.gr:F1} dB) but is not heard (rms {ghost.rms:E1} vs {loud.rms:E1})");
        e.RackSetChainMute(rig.Drums, kick, false);
        e.RackSetChainSolo(rig.Drums, hat, true);
        var gated = rig.Run();
        yield return (gated.gr > 3 && gated.rms < loud.rms * 0.05, $"sc parts: a solo-gated kick pad keeps ducking ({gated.gr:F1} dB)");
        e.RackSetChainSolo(rig.Drums, hat, false);

        // The whole-track setter clears the part.
        e.SetDeviceSidechainSource(rig.Bass, rig.Comp, rig.Drums);
        yield return (e.DeviceSidechainKey(rig.Bass, rig.Comp) == new SidechainKey(rig.Drums, -1), "sc parts: a whole-track key clears the part");
        yield return (e.SetDeviceSidechainKey(rig.Bass, rig.Comp, rig.Drums, 5) == -1 && e.SetDeviceSidechainKey(rig.Bass, rig.Comp, 9999, -1) == -1,
            "sc parts: no such part or track is refused");
    }

    private static IEnumerable<(bool, string)> StableKey()
    {
        using var rig = new Rig();
        var e = rig.E;
        e.SetDeviceSidechainKey(rig.Bass, rig.Comp, rig.Drums, rig.Chain("Kick"));
        rig.Play(KickNote);

        // Other pads come and go: the key follows the kick.
        e.RackRemoveChain(rig.Drums, rig.Chain("Hat"));
        yield return (e.DeviceSidechainKey(rig.Bass, rig.Comp).Part == 0 && rig.Run().gr > 3,
            "sc parts: deleting the pad before it moves the key with the kick");
        int dup = e.RackDuplicateChain(rig.Drums, 0);
        int added = e.RackAddSamplerChain(rig.Drums, Wav(500), 50, false);
        yield return (dup == 1 && added == 2 && e.DeviceSidechainKey(rig.Bass, rig.Comp).Part == 0,
            "sc parts: a duplicated and an added pad leave the key on the original kick");
        e.RackRemoveChain(rig.Drums, 2);
        e.RackRemoveChain(rig.Drums, 1);
        e.RackSetChainName(rig.Drums, 0, "Kick 909");
        yield return (e.TrackPartName(rig.Drums, e.DeviceSidechainKey(rig.Bass, rig.Comp).Part) == "Kick 909",
            "sc parts: a renamed pad keeps its key, under its new name");

        // The kick goes: the key sleeps (no reduction); undo brings both back.
        e.RackRemoveChain(rig.Drums, 0);
        var gone = e.DeviceSidechainKey(rig.Bass, rig.Comp);
        yield return (gone == new SidechainKey(rig.Drums, SidechainKey.Missing) && e.SidechainRoutesFree() == 16,
            "sc parts: deleting the keyed pad leaves the key without a part or a route");
        yield return (e.Undo() && e.DeviceSidechainKey(rig.Bass, rig.Comp) == new SidechainKey(rig.Drums, 0) && rig.Run().gr > 3,
            "sc parts: undo brings the pad back and its key ducks again");

        // The Drum Rack gives way to a synth: no pads, so the key sleeps the same way.
        yield return (e.SetTrackBuiltinInstrument(rig.Drums, 0) && e.TrackPartCount(rig.Drums) == 0
                      && e.DeviceSidechainKey(rig.Bass, rig.Comp).Part == SidechainKey.Missing,
            "sc parts: replacing the Drum Rack leaves the key without a part");
    }

    private static IEnumerable<(bool, string)> InstrumentRackChain()
    {
        using var e = new NotaEngine();
        e.SetBpm(120);
        int rack = e.AddInstrumentRackTrack();
        int lo = e.RackAddSamplerChain(rack, Wav(80), 48, false);
        int hi = e.RackAddSamplerChain(rack, Wav(2000), 72, false);
        e.RackSetChainZone(rack, lo, 0, 59, 0, 127);
        e.RackSetChainZone(rack, hi, 60, 127, 0, 127);
        e.RackSetChainName(rack, lo, "Sub");
        e.AddMidiClip(rack, 0.0, 4.0);
        int bass = e.AddAudioTrack();
        int comp = e.AddBuiltinDevice(bass, CompKind);
        e.DeviceSetParam(bass, comp, Thresh, -40f);
        e.DeviceSetParam(bass, comp, Ratio, 10f);
        e.DeviceSetParam(bass, comp, Attack, 1f);
        float Gr(int note)
        {
            e.SetClipNotes(rack, 0, new[] { new NotaNote(note, 0, 0.5, 1f), new NotaNote(note, 2, 0.5, 1f) });
            var buf = new float[512 * 2];
            float g = 0;
            e.Seek(0); e.Play();
            for (int i = 0; i < 172; i++) { e.RenderOffline(buf, 512); g = Math.Max(g, e.DeviceGainReduction(bass, comp)); }
            e.StopTransport();
            return g;
        }
        yield return (e.TrackPartCount(rack) == 2 && e.TrackPartName(rack, lo) == "Sub",
            "sc parts: an Instrument Rack lists its chains (named, or by instrument)");
        yield return (e.SetDeviceSidechainKey(bass, comp, rack, lo) == 0, "sc parts: key a Compressor off an Instrument Rack chain");
        float high = Gr(72), low = Gr(48);
        yield return (high < 0.1f && low > 3, $"sc parts: only the keyed chain ducks (other {high:F2} dB, keyed {low:F1} dB)");
        e.RackSetChainMute(rack, lo, true);
        yield return (Gr(48) > 3, "sc parts: a muted keyed chain still ducks");
    }

    // Nota Rhythm: voice 1 (Kick, note 36) and voice 5 (Closed Hat, note 42) from a clip, the
    // default pattern cleared. A Utility on the kick voice turned all the way down mutes Post only.
    private static IEnumerable<(bool, string)> RhythmVoice()
    {
        using var e = new NotaEngine();
        e.SetBpm(120);
        int rh = e.AddRhythmTrack();
        e.InstrumentAction(rh, 4, 0, 0);   // A_ClearBank: no default pattern
        e.AddMidiClip(rh, 0.0, 4.0);
        int bass = e.AddAudioTrack();
        int comp = e.AddBuiltinDevice(bass, CompKind);
        e.DeviceSetParam(bass, comp, Thresh, -40f);
        e.DeviceSetParam(bass, comp, Ratio, 10f);
        e.DeviceSetParam(bass, comp, Attack, 1f);
        float Gr(int note)
        {
            var ns = new List<NotaNote>();
            for (int b = 0; b < 4; b++) ns.Add(new NotaNote(note, b, 0.25, 1f));
            e.SetClipNotes(rh, 0, ns.ToArray());
            var buf = new float[512 * 2];
            float g = 0;
            e.Seek(0); e.Play();
            for (int i = 0; i < 172; i++) { e.RenderOffline(buf, 512); g = Math.Max(g, e.DeviceGainReduction(bass, comp)); }
            e.StopTransport();
            return g;
        }
        yield return (e.TrackPartCount(rh) == 8 && e.TrackPartName(rh, 0) == "1 Kick" && e.TrackPartName(rh, 4) == "5 Closed Hat",
            "sc parts: Nota Rhythm lists its eight voices");
        yield return (e.SetDeviceSidechainKey(bass, comp, rh, 0) == 0, "sc parts: key a Compressor off Rhythm's kick voice");
        float hat = Gr(HatNote), kick = Gr(KickNote);
        yield return (hat < 0.1f && kick > 3, $"sc parts: only the kick voice ducks (hat {hat:F2} dB, kick {kick:F1} dB)");

        int u = e.RhythmAddVoiceDevice(rh, 0, 4);   // Utility on the kick voice
        int gain = -1;
        for (int p = 0; p < e.RhythmVoiceDeviceParamCount(rh, 0, u); p++)
            if (e.RhythmVoiceDeviceParamName(rh, 0, u, p) == "Gain") gain = p;
        if (gain >= 0) e.RhythmVoiceDeviceParamSet(rh, 0, u, gain, e.RhythmVoiceDeviceParamMin(rh, 0, u, gain));
        float post = Gr(KickNote);
        e.SetDeviceSidechainTapPre(bass, comp, true);
        float pre = Gr(KickNote);
        yield return (u >= 0 && gain >= 0 && post < pre - 10 && pre > 3,
            $"sc parts: a voice FX turned down weakens Post ({post:F1} dB), not Pre ({pre:F1} dB)");
    }

    // Nota Flux's React keyed off the kick pad: its envelope follower moves on kicks only.
    private static IEnumerable<(bool, string)> FluxReact()
    {
        using var rig = new Rig();
        var e = rig.E;
        int flux = e.AddFluxSynthTrack();
        int kick = rig.Chain("Kick");
        yield return (e.SetInstrumentSidechainKey(flux, rig.Drums, kick) == 0
                      && e.InstrumentSidechainKey(flux) == new SidechainKey(rig.Drums, kick) && e.InstrumentSidechainSource(flux) == rig.Drums,
            "sc parts: key Flux's React off the kick pad");
        float Env(int note)
        {
            rig.Play(note);
            var buf = new float[512 * 2];
            var sc = new float[9];
            float env = 0;
            e.Seek(0); e.Play();
            for (int i = 0; i < 172; i++)
            {
                e.RenderOffline(buf, 512);
                if (e.InstrumentScope(flux, sc) >= 1) env = Math.Max(env, sc[0]);
            }
            e.StopTransport();
            return env;
        }
        float hat = Env(HatNote), kickEnv = Env(KickNote);
        yield return (hat < 1e-4f && kickEnv > 0.001f, $"sc parts: React follows only the kick pad (hat {hat:E1}, kick {kickEnv:F3})");
        e.SetInstrumentSidechainSource(flux, rig.Drums);
        yield return (e.InstrumentSidechainKey(flux) == new SidechainKey(rig.Drums, -1), "sc parts: a whole-track React source clears the part");
    }

    // Every built-in that takes a key can take a pad: the key round-trips and holds a route.
    private static IEnumerable<(bool, string)> EveryConsumer()
    {
        using var rig = new Rig();
        var e = rig.E;
        int kick = rig.Chain("Kick");
        var kinds = new List<int>();
        int host = e.AddAudioTrack();
        for (int kind = 0; kind < 40; kind++)
        {
            int d = e.AddBuiltinDevice(host, kind);
            if (d < 0) continue;
            if (!e.DeviceAcceptsSidechain(host, d) || kind == 10) { e.RemoveDevice(host, d); continue; }   // Auto Shift keys on MIDI
            if (e.SetDeviceSidechainKey(host, d, rig.Drums, kick) == 0 && e.DeviceSidechainKey(host, d) == new SidechainKey(rig.Drums, kick))
                kinds.Add(kind);
            else
                yield return (false, $"sc parts: built-in kind {kind} takes a pad key");
        }
        yield return (kinds.Count >= 7 && e.SidechainRoutesFree() == 15,
            $"sc parts: every keyed built-in takes the kick pad, sharing one route (kinds {string.Join(", ", kinds)})");
    }

    // MCP: list the pads, key a device off one, read it back by name.
    private static IEnumerable<(bool, string)> Mcp()
    {
        using var rig = new Rig();
        var e = rig.E;
        int kick = rig.Chain("Kick");
        var tools = new Nota.Mcp.Tools.DeviceTools(e, new SyncDispatch(), new NoRefresh());
        var list = tools.ListSidechainSources().Result;
        var drums = list.Sources.FirstOrDefault(x => x.TrackId == rig.Drums);
        yield return (list.RouteCapacity == 16 && drums is { Parts.Length: 2 } && drums.Parts[kick].Name == "Kick",
            "sc parts: list_sidechain_sources lists the Drum Rack's pads");
        tools.SetDeviceSidechain(rig.Bass, rig.Comp, rig.Drums, tapPre: true, sourcePart: kick).Wait();
        var got = tools.GetDeviceSidechain(rig.Bass, rig.Comp).Result;
        yield return (got.SourceTrackId == rig.Drums && got.SourcePart == kick && got.SourceName == "Drums · Kick" && got.TapPre,
            $"sc parts: set_device_sidechain keys a pad; get_device_sidechain reads \"{got.SourceName}\"");
        bool threw = false;
        try { tools.SetDeviceSidechain(rig.Bass, rig.Comp, rig.Drums, sourcePart: 7).Wait(); }
        catch (Exception x) when (x is ArgumentException or AggregateException) { threw = true; }
        yield return (threw && e.DeviceSidechainKey(rig.Bass, rig.Comp).Part == kick, "sc parts: a part that doesn't exist is an error and keeps the key");
        tools.SetDeviceSidechain(rig.Bass, rig.Comp, rig.Drums).Wait();
        yield return (e.DeviceSidechainKey(rig.Bass, rig.Comp) == new SidechainKey(rig.Drums, -1), "sc parts: set_device_sidechain without a part keys the whole track");
    }

    // A frozen Drum Rack plays its freeze buffer, not its pads: a pad key goes quiet while the
    // whole-track key keeps working; unfreezing brings the pad key back.
    private static IEnumerable<(bool, string)> FrozenSource()
    {
        using var rig = new Rig();
        var e = rig.E;
        int kick = rig.Chain("Kick");
        rig.Play(KickNote);
        e.SetDeviceSidechainKey(rig.Bass, rig.Comp, rig.Drums, kick);
        float live = rig.Run().gr;
        e.SetMetronome(false);
        e.StopTransport();
        long total = e.BeginFreeze(rig.Drums, 4.0);
        e.Seek(0); e.Play();
        var chunk = new float[4096 * 2];
        for (long rem = total; rem > 0;) { int m = (int)Math.Min(4096, rem); e.RenderOffline(chunk, m); rem -= m; }
        e.StopTransport();
        e.EndFreeze(rig.Drums);
        float frozenPart = rig.Run().gr;
        e.SetDeviceSidechainSource(rig.Bass, rig.Comp, rig.Drums);
        float frozenWhole = rig.Run().gr;
        yield return (total > 0 && e.IsTrackFrozen(rig.Drums) && live > 3 && frozenPart < 0.1f && frozenWhole > 3,
            $"sc parts: a frozen track's pad key is silent ({frozenPart:F2} dB), its whole-track key still ducks ({frozenWhole:F1} dB)");
        e.SetDeviceSidechainKey(rig.Bass, rig.Comp, rig.Drums, kick);
        e.UnfreezeTrack(rig.Drums);
        float back = rig.Run().gr;
        yield return (!e.IsTrackFrozen(rig.Drums) && back > 3, $"sc parts: unfrozen, the pad key ducks again ({back:F1} dB)");
    }

    // A keyed device copied and pasted, or its whole track duplicated, keeps the pad key.
    private static IEnumerable<(bool, string)> CopiesKeepTheKey()
    {
        using var rig = new Rig();
        var e = rig.E;
        var key = new SidechainKey(rig.Drums, rig.Chain("Kick"));
        e.SetDeviceSidechainKey(rig.Bass, rig.Comp, key.Source, key.Part);
        bool copied = e.CopyDevice(rig.Bass, rig.Comp);
        int pasted = e.PasteDevice(rig.Bass, -1);
        yield return (copied && pasted > rig.Comp && e.DeviceSidechainKey(rig.Bass, pasted) == key,
            "sc parts: a copied and pasted Compressor keeps its pad key");
        int dup = e.DuplicateTrack(rig.Bass);
        yield return (dup > 0 && e.DeviceSidechainKey(dup, rig.Comp) == key, "sc parts: a duplicated track's Compressor keeps its pad key");
        yield return (e.SidechainRouteInUse(key.Source, key.Part) && e.SidechainRoutesFree() == 15,
            "sc parts: all those keys share the pad's one route");
    }

    private static IEnumerable<(bool, string)> RoutePool()
    {
        using var e = new NotaEngine();
        int host = e.AddAudioTrack();
        var comps = new List<int>();
        var srcs = new List<int>();
        for (int i = 0; i < 17; i++)
        {
            comps.Add(e.AddBuiltinDevice(host, CompKind));
            srcs.Add(e.AddAudioTrack());
        }
        bool first16 = true;
        for (int i = 0; i < 16; i++) first16 &= e.SetDeviceSidechainKey(host, comps[i], srcs[i], -1) == 0;
        yield return (first16 && e.SidechainRoutesFree() == 0, "sc parts: 16 distinct sources fill the pool");
        yield return (e.SetDeviceSidechainKey(host, comps[16], srcs[16], -1) == -2 && e.DeviceSidechainKey(host, comps[16]).Source == -1,
            "sc parts: a 17th source is refused and leaves the key as it was");
        yield return (e.SetDeviceSidechainKey(host, comps[16], srcs[0], -1) == 0 && e.SidechainRouteInUse(srcs[0], -1),
            "sc parts: a further key on a source in use shares its route");
        yield return (e.SetDeviceSidechainKey(host, comps[1], srcs[16], -1) == 0 && e.DeviceSidechainKey(host, comps[1]).Source == srcs[16],
            "sc parts: with the pool full, a key can move to a new source when it was the old one's only listener");
    }

    private static IEnumerable<(bool, string)> SaveReopen()
    {
        using var rig = new Rig();
        var e = rig.E;
        int kick = rig.Chain("Kick");
        e.SetDeviceSidechainKey(rig.Bass, rig.Comp, rig.Drums, kick);
        e.SetDeviceSidechainTapPre(rig.Bass, rig.Comp, true);
        int comp2 = e.AddBuiltinDevice(rig.Bass, CompKind);
        e.SetDeviceSidechainSource(rig.Bass, comp2, rig.Drums);
        string dir = Path.Combine(Path.GetTempPath(), "nota-scpart-" + Guid.NewGuid().ToString("N"));
        try
        {
            var doc = ProjectService.Capture(e, new TransportState(120, 1, false, false), new List<string>());
            ProjectService.Save(doc, dir, e);
            using var e2 = new NotaEngine();
            var warnings = ProjectService.Apply(ProjectService.Load(dir), e2, dir);
            int drums = -1, bass = -1;
            for (int i = 0; i < e2.TrackCount; i++)
                if (e2.TryGetTrackInfo(i, out var ti))
                {
                    if (e2.RackChainCount(ti.Id) == 2) drums = ti.Id;
                    else if (e2.TrackDeviceCount(ti.Id) == 2) bass = ti.Id;
                }
            yield return (drums > 0 && bass > 0 && warnings.Count == 0, "sc parts: the project reopens");
            yield return (e2.DeviceSidechainKey(bass, 0) == new SidechainKey(drums, kick) && e2.DeviceSidechainTapPre(bass, 0)
                          && e2.TrackPartName(drums, kick) == "Kick",
                "sc parts: a reopened key is on the same pad, with its tap");
            yield return (e2.DeviceSidechainKey(bass, 1) == new SidechainKey(drums, -1), "sc parts: a whole-track key reopens whole");
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }

        // A v27 project has no parts: it migrates on and every key stays on its whole track.
        var old = ProjectMigrations.Migrate(new JsonObject { ["formatVersion"] = 27 });
        yield return (old["formatVersion"]?.GetValue<int>() == ProjectService.CurrentFormatVersion
                      && new DeviceDto().SidechainPart == -1 && new InstrumentDto().SidechainPart == -1 && new NestedSidechainDto().Part == -1,
            "sc parts: a v27 project migrates and its keys stay whole-track");
    }
}
