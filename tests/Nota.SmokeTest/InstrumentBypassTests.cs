// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The instrument card's on/off switch (#148): device index -1 is the instrument. Off fades the
// instrument out over one block, releases its voices and then renders exact silence; the audio
// effects after it keep running (a reverb tail rings out), the MIDI effects before it and the
// effects' own switches are untouched; back on, it plays the next note. Survives duplicate and
// save / load; a project without the flag loads with the instrument on. MCP reaches it too.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class InstrumentBypassTests
{
    private const int Block = 512;

    private static float[] Render(NotaEngine e, int blocks)
    {
        var all = new float[blocks * Block * 2];
        var b = new float[Block * 2];
        for (int i = 0; i < blocks; i++) { e.RenderOffline(b, Block); Array.Copy(b, 0, all, i * Block * 2, Block * 2); }
        return all;
    }
    private static double Peak(float[] st, int fromBlock = 0, int toBlock = -1)
    {
        int to = toBlock < 0 ? st.Length : toBlock * Block * 2;
        double p = 0;
        for (int i = fromBlock * Block * 2; i < to; i++) p = Math.Max(p, Math.Abs(st[i]));
        return p;
    }

    // Which tracks' instruments are off, in track order ("1" = off): "101", say.
    private static string Pattern(NotaEngine e)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < e.TrackCount; i++)
            if (e.TryGetTrackInfo(i, out var ti)) sb.Append(e.DeviceBypassed(ti.Id, -1) ? '1' : '0');
        return sb.ToString();
    }

    public static IEnumerable<(bool, string)> Run()
    {
        // ---- off / on with a held note ----------------------------------------------------
        using (var e = new NotaEngine())
        {
            int t = e.AddInstrumentTrack();
            yield return (!e.DeviceBypassed(t, -1), "a new instrument is on");
            e.TrackNoteOn(t, 60, 0.8f);
            double on = Peak(Render(e, 8), 2);
            e.SetDeviceBypassed(t, -1, true);
            yield return (e.DeviceBypassed(t, -1), "the instrument's switch turns off (device index -1)");
            var off = Render(e, 6);
            // The first block fades out: it starts near the playing level and ends at zero.
            double head = 0, tail = Math.Abs(off[(Block - 1) * 2]) + Math.Abs(off[(Block - 1) * 2 + 1]);
            for (int i = 0; i < 64; i++) head = Math.Max(head, Math.Abs(off[i]));
            yield return (on > 0.01 && head > on * 0.2 && tail < on * 0.02, $"switching off fades the instrument out over one block (playing {on:F3}, start {head:F3}, end {tail:F5})");
            yield return (Peak(off, 1) == 0.0, $"once off, the instrument renders exact silence (peak {Peak(off, 1):E1})");
            e.SetDeviceBypassed(t, -1, false);
            double held = Peak(Render(e, 4));
            yield return (held == 0.0, $"back on, the note held across the switch stays released (peak {held:E1})");
            e.TrackNoteOff(t, 60);
            e.TrackNoteOn(t, 64, 0.8f);
            double again = Peak(Render(e, 6), 2);
            yield return (again > 0.01, $"back on, the next note sounds (peak {again:F3})");
            e.TrackNoteOff(t, 64);
        }

        // ---- the effects after it keep running, and keep their own switches ---------------
        using (var e = new NotaEngine())
        {
            int t = e.AddInstrumentTrack();
            int rv = e.AddBuiltinDevice(t, 2);   // Reverb
            int arp = e.AddMidiEffect(t, 0);
            e.TrackNoteOn(t, 60, 0.8f);
            Render(e, 40);
            e.SetDeviceBypassed(t, -1, true);
            var off = Render(e, 12);
            yield return (Peak(off, 4, 12) > 1e-4, $"a reverb after the instrument rings out after it is switched off (peak {Peak(off, 4, 12):E1})");
            yield return (rv >= 0 && !e.DeviceBypassed(t, rv) && (arp < 0 || !e.MidiEffectBypassed(t, arp)),
                "switching the instrument off leaves the effects' own switches alone");
            e.SetDeviceBypassed(t, rv, true);
            yield return (e.DeviceBypassed(t, -1) && e.DeviceBypassed(t, rv), "an effect's switch and the instrument's are separate");
            e.TrackNoteOff(t, 60);
        }

        // ---- clip playback ---------------------------------------------------------------
        using (var e = new NotaEngine())
        {
            e.SetBpm(120);
            int t = e.AddInstrumentTrack();
            e.AddMidiClip(t, 0.0, 8.0);
            e.SetClipNotes(t, 0, new[] { new NotaNote(60, 0.0, 4.0, 0.8f), new NotaNote(64, 2.0, 4.0, 0.8f) });
            e.SetDeviceBypassed(t, -1, true);
            e.Seek(0); e.Play();
            double p = Peak(Render(e, 300));
            e.StopTransport();
            yield return (p == 0.0, $"an instrument that is off stays silent while its clip plays (peak {p:E1})");
        }

        // ---- duplicate, save / load, older projects --------------------------------------
        string dir = Path.Combine(Path.GetTempPath(), "nota-smoke-instoff-" + Guid.NewGuid().ToString("N") + ".nota");
        var transport = new TransportState(120.0, 1.0, false, false);
        string saved = "";
        try
        {
            using (var src = new NotaEngine())
            {
                int a = src.AddInstrumentTrack();
                int b = src.AddInstrumentTrack();
                src.SetDeviceBypassed(a, -1, true);
                int dup = src.DuplicateTrack(a);
                yield return (dup > 0 && src.DeviceBypassed(dup, -1) && !src.DeviceBypassed(b, -1), "a duplicated track keeps its instrument off");
                saved = Pattern(src);
                ProjectService.Save(ProjectService.Capture(src, transport, new List<string>()), dir, src);
            }
            using (var dst = new NotaEngine())
            {
                var warn = ProjectService.Apply(ProjectService.Load(dir), dst, dir);
                string loaded = Pattern(dst);
                yield return (warn.Count == 0 && saved.Length == 3 && saved.Contains('1') && saved.Contains('0') && loaded == saved,
                    $"save / load keeps which instruments are off (saved {saved}, loaded {loaded}, warnings {warn.Count})");
            }
            // A project written before the switch existed has no flag: its instruments load on.
            string manifest = Path.Combine(dir, ProjectService.ManifestName);
            var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
            int stripped = 0;
            foreach (var tr in root["tracks"]!.AsArray())
                if (tr?["instrument"] is System.Text.Json.Nodes.JsonObject inst && inst.Remove("bypassed")) stripped++;
            File.WriteAllText(manifest, root.ToJsonString());
            using (var dst = new NotaEngine())
            {
                var warn = ProjectService.Apply(ProjectService.Load(dir), dst, dir);
                int a = dst.TryGetTrackInfo(0, out var ta) ? ta.Id : -1;
                yield return (stripped == 3 && warn.Count == 0 && a > 0 && !dst.DeviceBypassed(a, -1),
                    $"a project without the flag loads with its instruments on (stripped {stripped})");
            }
        }
        finally { try { Directory.Delete(dir, true); } catch { } }

        // ---- MCP ---------------------------------------------------------------------------
        using (var e = new NotaEngine())
        {
            int t = e.AddInstrumentTrack();
            var dev = new Nota.Mcp.Tools.DeviceTools(e, new SyncDispatch(), new NoRefresh());
            var proj = new Nota.Mcp.Tools.ProjectTools(e, new SyncDispatch(), new NoRefresh());
            dev.SetDeviceBypass(t, -1, true).GetAwaiter().GetResult();
            var ov = proj.GetOverview().GetAwaiter().GetResult();
            yield return (e.DeviceBypassed(t, -1) && ov.Tracks.Length == 1 && ov.Tracks[0].InstrumentOff,
                "MCP set_device_bypass with device index -1 turns the instrument off, and get_overview shows it");
        }
    }
}
