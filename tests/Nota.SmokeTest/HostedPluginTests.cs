// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Hosted plug-ins, with a real VST3 (the Nota Pentad build, -DNOTA_BUILD_PENTAD_VST3=ON):
// - Duplicate track and device copy make a second instance with the source's state (#454: the
//   copy came back empty, the instance's own description naming a binary no format loads).
// - A host block longer than the plug-in was prepared for plays in pieces (#447: plug-ins were
//   prepared for 16384 frames, which sfizz refuses), each piece with its own notes, so one
//   16384-frame block renders the same as the same span in 512-frame blocks.
// Opt-in, since it needs the plug-in built and scanned:
//   dotnet Nota.SmokeTest.dll --hosted-plugin <folder holding "Nota Pentad.vst3"> <nota-scanworker>

using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class HostedPluginTests
{
    public static IEnumerable<(bool ok, string label)> Run(string vst3Dir, string scanWorker)
    {
        NotaEngine.AddScanPath(vst3Dir);
        int n = NotaEngine.ScanPlugins(scanWorker, vst3Dir, true);
        int idx = -1;
        for (int i = 0; i < n; i++)
            if (NotaEngine.PluginDescription(i)?.StartsWith("Nota Pentad", StringComparison.Ordinal) == true) idx = i;
        yield return (idx >= 0, $"the Nota Pentad VST3 is in the catalog ({n} plug-ins)");
        if (idx < 0) yield break;

        foreach (var r in Copies(idx)) yield return r;
        foreach (var r in LongBlocks(idx)) yield return r;
    }

    private static IEnumerable<(bool ok, string label)> Copies(int idx)
    {
        using var e = new NotaEngine();
        int t = e.AddPluginInstrumentTrack(idx);
        int fx = e.AddTrackEffectPlugin(t, idx);
        e.PluginParamSet(t, -1, 3, 0.123f);
        e.PluginParamSet(t, fx, 5, 0.321f);
        string id = e.TrackInstrumentPluginId(t);

        int[] dup = e.DuplicateTracks(new[] { t });
        int d = dup.Length == 1 ? dup[0] : -1;
        yield return (d > 0 && e.TrackInstrumentPluginId(d) == id,
            $"duplicate track copies the plug-in instrument (got '{(d > 0 ? e.TrackInstrumentPluginId(d) : "")}')");
        yield return (d > 0 && Math.Abs(e.PluginParamGet(d, -1, 3) - 0.123f) < 1e-4,
            "…with its state");
        yield return (d > 0 && e.TrackDevicePluginId(d, 0) == id && Math.Abs(e.PluginParamGet(d, 0, 5) - 0.321f) < 1e-4,
            "…and its plug-in effect, with its state");

        bool copied = e.CopyDevice(t, fx);
        int at = copied ? e.PasteDevice(t, 1) : -1;
        yield return (at >= 0 && e.TrackDevicePluginId(t, at) == id && Math.Abs(e.PluginParamGet(t, at, 5) - 0.321f) < 1e-4,
            "copy + paste of a plug-in effect makes a second instance with its state");
    }

    private static IEnumerable<(bool ok, string label)> LongBlocks(int idx)
    {
        float[] Render(int block)
        {
            using var e = new NotaEngine();
            e.SetBpm(120);
            int t = e.AddPluginInstrumentTrack(idx);
            e.AddMidiClip(t, 0.0, 4.0);
            // Notes starting inside the long block's later pieces (beat 0.5 = 11025 frames).
            e.SetClipNotes(t, 0, new[] { new NotaNote(60, 0.0, 1.0, 0.8f), new NotaNote(67, 0.2, 0.6, 0.7f), new NotaNote(72, 0.5, 0.5, 0.9f) });
            const int N = 32768;
            var outv = new float[N * 2];
            var tmp = new float[block * 2];
            e.Seek(0.0);
            e.Play();
            for (int done = 0; done < N;)
            {
                int m = Math.Min(block, N - done);
                e.RenderOffline(tmp, m);
                Array.Copy(tmp, 0, outv, done * 2, m * 2);
                done += m;
            }
            e.StopTransport();
            return outv;
        }

        var big = Render(16384);
        var small = Render(512);
        double diff = 0, sq = 0;
        for (int i = 0; i < big.Length; i++)
        {
            diff = Math.Max(diff, Math.Abs(big[i] - small[i]));
            sq += small[i] * (double)small[i];
        }
        double rms = Math.Sqrt(sq / big.Length);
        // The later notes must sound in the second half of the first 16384 frames.
        double late = 0;
        for (int i = 11025 * 2; i < 16384 * 2; i++) late = Math.Max(late, Math.Abs(big[i]));
        yield return (rms > 1e-3 && late > 1e-3 && diff < 1e-5,
            $"a 16384-frame block plays in pieces like 512-frame blocks, notes in place (maxdiff {diff:G3}, rms {rms:F4})");
    }
}
