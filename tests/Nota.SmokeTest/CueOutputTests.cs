// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Main and Cue on separate output pairs (specs/089, #483). Output placement is driven
// without a device through nota_audio_scatter_test — the same code every backend runs —
// and the routing state through a pretend output device. A Cue pair the device lacks must
// go silent and never fold into Main.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class CueOutputTests
{
    private const int Frames = 64;

    public static IEnumerable<(bool, string)> Run()
    {
        foreach (var r in Placement()) yield return r;
        foreach (var r in Fallbacks()) yield return r;
        foreach (var r in RoutingValidation()) yield return r;
        foreach (var r in SetupRules()) yield return r;
        string wav = Wav();
        try
        {
            foreach (var r in CueTaps(wav)) yield return r;
            foreach (var r in CueBuses(wav)) yield return r;
            foreach (var r in MainUntouched(wav)) yield return r;
        }
        finally { try { File.Delete(wav); } catch { } }
        foreach (var r in Click()) yield return r;
        foreach (var r in SavedCue()) yield return r;
    }

    // ---- the project (US6) -------------------------------------------------------------

    private static IEnumerable<(bool, string)> SavedCue()
    {
        var transport = new TransportState(120.0, 1.0, false, false);
        string bundle = Path.Combine(Path.GetTempPath(), $"nota-cue-{Guid.NewGuid():N}.nota");
        try
        {
            int[] cuedIdx;
            {
                using var e = new NotaEngine();
                int a = e.AddAudioTrack(), b = e.AddAudioTrack(), c = e.AddAudioTrack();
                e.SetTrackCue(a, true);
                e.SetTrackCue(c, true);
                e.CueVolume = 0.5f;
                ProjectService.Save(ProjectService.Capture(e, transport, new List<string>()), bundle, e);
                cuedIdx = new[] { 0, 2 };
                _ = b;
            }
            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(bundle, ProjectService.ManifestName)))!;
            yield return (json["formatVersion"]?.GetValue<int>() == ProjectService.CurrentFormatVersion,
                $"projects save as the current format ({json["formatVersion"]})");
            {
                using var e = new NotaEngine();
                var w = ProjectService.Apply(ProjectService.Load(bundle), e, bundle);
                var ids = Enumerable.Range(0, e.TrackCount).Select(i => e.TryGetTrackInfo(i, out var ti) ? ti.Id : -1).ToArray();
                bool cues = ids.Length == 3 && ids.Select((id, i) => e.GetTrackCue(id) == cuedIdx.Contains(i)).All(x => x);
                yield return (cues && Math.Abs(e.CueVolume - 0.5f) < 1e-6f && w.Count == 0,
                    "save → reopen restores the cued tracks and the Cue level (SC-006)");
            }
            // A v31 project: nothing in Cue, unity Cue level.
            var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(bundle, ProjectService.ManifestName)))!;
            node["formatVersion"] = 31;
            node["mixer"]?.AsObject().Remove("cueVolume");
            foreach (var t in node["tracks"]!.AsArray()) t!.AsObject().Remove("cue");
            File.WriteAllText(Path.Combine(bundle, ProjectService.ManifestName), node.ToJsonString());
            {
                using var e = new NotaEngine();
                e.CueVolume = 0.25f; // whatever the session had before
                var doc = ProjectService.Load(bundle);
                ProjectService.Apply(doc, e, bundle);
                bool none = Enumerable.Range(0, e.TrackCount).All(i => e.TryGetTrackInfo(i, out var ti) && !e.GetTrackCue(ti.Id));
                yield return (doc.FormatVersion == ProjectService.CurrentFormatVersion && none && Math.Abs(e.CueVolume - 1f) < 1e-6f,
                    "a v31 project migrates past v32 with nothing in Cue at unity Cue level");
            }
        }
        finally { try { Directory.Delete(bundle, true); } catch { } }
    }

    // ---- the Cue bus (US2) -------------------------------------------------------------

    private const int Rate = 44100, Block = 512;
    private const float Amp = 0.2f;
    private static readonly double ClipRms = Amp / Math.Sqrt(2);

    private static string Wav()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nota-cue-{Guid.NewGuid():N}.wav");
        var x = new float[Rate * 4 * 2];
        for (int i = 0; i < x.Length / 2; i++) x[2 * i] = x[2 * i + 1] = Amp * (float)Math.Sin(2 * Math.PI * 441 * i / Rate);
        using (var w = new Nota.Infrastructure.WavWriter(path, Rate, 2, WavBitDepth.Float32)) w.WriteFrames(x, x.Length / 2);
        return path;
    }

    private static NotaEngine Engine()
    {
        var e = new NotaEngine();
        e.SetBpm(120);
        return e;
    }

    // Prepare the nodes, roll from bar 1 and render `frames` as the device does: Main and Cue.
    private static (float[] Main, float[] Cue) Play(NotaEngine e, int frames, bool cue = true)
    {
        e.RenderOffline(new float[2], 1, Rate);
        e.Seek(0.0);
        e.Play();
        var main = new float[frames * 2];
        var cueAll = new float[frames * 2];
        var b = new float[Block * 2];
        var c = new float[Block * 2];
        for (int done = 0; done < frames; done += Block)
        {
            int n = Math.Min(Block, frames - done);
            if (cue) e.RenderDeviceBlockCue(b, c, n); else e.RenderDeviceBlock(b, n);
            Array.Copy(b, 0, main, done * 2, n * 2);
            if (cue) Array.Copy(c, 0, cueAll, done * 2, n * 2);
        }
        return (main, cueAll);
    }

    // RMS over the second half (past any start-up ramp).
    private static double Rms(float[] st)
    {
        double s = 0;
        int from = st.Length / 2;
        for (int i = from; i < st.Length; i++) s += st[i] * (double)st[i];
        return Math.Sqrt(s / Math.Max(1, st.Length - from));
    }

    private static bool Near(double a, double b, double rel = 0.03) => Math.Abs(a - b) <= rel * Math.Max(Math.Abs(b), 1e-9);

    private static IEnumerable<(bool, string)> CueTaps(string wav)
    {
        const int N = Rate / 2;
        {
            using var e = Engine();
            int t = e.AddAudioTrack();
            e.AddAudioClip(t, wav, 0.0);
            e.SetTrackCue(t, true);
            e.SetTrackVolume(t, 0f);
            var (main, cue) = Play(e, N);
            yield return (e.GetTrackCue(t), "a track can be put in Cue");
            yield return (Near(Rms(cue), ClipRms) && Rms(main) == 0, $"Cue is pre-fader: fader down, Cue still {Rms(cue):F4} (clip {ClipRms:F4})");
        }
        {
            using var e = Engine();
            int t = e.AddAudioTrack();
            e.AddAudioClip(t, wav, 0.0);
            e.SetTrackCue(t, true);
            e.SetTrackMute(t, true);
            var (main, cue) = Play(e, N);
            yield return (Near(Rms(cue), ClipRms) && Rms(main) == 0, "Cue is pre-mute: a muted cued track is heard in Cue only");
        }
        {
            using var e = Engine();
            int t = e.AddAudioTrack();
            e.AddAudioClip(t, wav, 0.0);
            int other = e.AddAudioTrack();
            e.SetTrackSolo(other, true);
            e.SetTrackCue(t, true);
            var (main, cue) = Play(e, N);
            yield return (Near(Rms(cue), ClipRms) && Rms(main) == 0, "Cue is pre-solo: a solo elsewhere doesn't silence Cue");
        }
        {
            using var e = Engine();
            int t = e.AddAudioTrack();
            e.AddAudioClip(t, wav, 0.0);
            int d = e.AddBuiltinDevice(t, 4); // Utility
            e.DeviceSetParam(t, d, 0, 6.0206f); // Gain ×2
            e.SetTrackCue(t, true);
            var (_, cue) = Play(e, N);
            yield return (Near(Rms(cue), 2 * ClipRms, 0.05), $"Cue is post-devices: +6 dB Utility doubles Cue ({Rms(cue):F4})");
        }
        {
            using var e = Engine();
            int a = e.AddAudioTrack(), b = e.AddAudioTrack(), c = e.AddAudioTrack();
            e.AddAudioClip(a, wav, 0.0);
            e.AddAudioClip(b, wav, 0.0);
            e.AddAudioClip(c, wav, 0.0);
            e.SetTrackCue(a, true);
            e.SetTrackCue(b, true);
            var (_, cue) = Play(e, N);
            yield return (Near(Rms(cue), 2 * ClipRms), "two cued tracks sum in Cue; an uncued one stays out");
        }
        {
            using var e = Engine();
            int t = e.AddAudioTrack();
            e.AddAudioClip(t, wav, 0.0);
            e.SetTrackCue(t, true);
            e.CueVolume = 0.5f;
            var (_, cue) = Play(e, N);
            yield return (Near(Rms(cue), 0.5 * ClipRms), "the Cue level scales Cue");
            yield return (Math.Abs(e.CueMeter().PeakL - Amp * 0.5f) < 0.01f, $"the Cue meter reads after the Cue level ({e.CueMeter().PeakL:F3})");
        }
    }

    private static IEnumerable<(bool, string)> CueBuses(string wav)
    {
        {
            // The dry-vocal case of #483: a monitored input in Cue, muted in Main — the performer
            // hears it, the audience doesn't. Heard with the transport stopped, like monitoring.
            using var e = Engine();
            int mon = e.AddAudioTrack();
            e.SetTrackRecordInput(mon, 0);
            e.SetTrackMonitor(mon, true);
            e.SetTrackMute(mon, true);
            e.SetTrackCue(mon, true);
            e.RenderOffline(new float[2], 1, Rate);
            var sine = new float[8192 * 2];
            for (int i = 0; i < 8192; i++) sine[i * 2] = sine[i * 2 + 1] = 0.5f * MathF.Sin(2f * MathF.PI * 440f * i / Rate);
            e.PushMonitorFramesForTest(sine, 8192);
            var m = new float[2048 * 2];
            var c = new float[2048 * 2];
            e.RenderDeviceBlockCue(m, c, 2048);
            yield return (Rms(c) > 0.1 && m.All(v => v == 0f), $"a monitored input in Cue, muted in Main: heard only in Cue ({Rms(c):F3})");
        }
        const int N = Rate / 2;
        {
            // A muted, cued group: its children feed its Cue tap; nothing reaches Main — not
            // even through a child's send to a return.
            using var e = Engine();
            int grp = e.AddGroupTrack();
            int t = e.AddAudioTrack();
            e.AddAudioClip(t, wav, 0.0);
            e.SetTrackGroup(t, grp);
            int ret = e.AddReturnTrack();
            e.SetTrackSend(t, 0, 1f);
            e.SetTrackMute(grp, true);
            e.SetTrackCue(grp, true);
            var (main, cue) = Play(e, N);
            yield return (Rms(cue) > 0.5 * ClipRms && Rms(main) == 0,
                $"a muted cued group: its children in Cue ({Rms(cue):F4}), Main silent, sends too");
            e.SetTrackCue(grp, false);
            var (main2, _) = Play(e, N);
            yield return (Rms(main2) == 0, "…and the group's mute still silences everything once it leaves Cue");
            _ = ret;
        }
        {
            // Review on #566: a group nested in a muted, cued group still reaches its Cue tap.
            using var e = Engine();
            int outer = e.AddGroupTrack();
            int inner = e.AddGroupTrack();
            e.SetTrackGroup(inner, outer);
            int t = e.AddAudioTrack();
            e.AddAudioClip(t, wav, 0.0);
            e.SetTrackGroup(t, inner);
            e.SetTrackMute(outer, true);
            e.SetTrackCue(outer, true);
            var (main, cue) = Play(e, N);
            yield return (Rms(cue) > 0.5 * ClipRms && Rms(main) == 0,
                $"a nested group under a muted cued group reaches its Cue ({Rms(cue):F4}), Main silent");
        }
        {
            using var e = Engine();
            int t = e.AddAudioTrack();
            e.AddAudioClip(t, wav, 0.0);
            int ret = e.AddReturnTrack();
            e.SetTrackSend(t, 0, 1f);
            e.SetTrackMute(t, false);
            e.SetTrackMute(ret, true);
            e.SetTrackCue(ret, true);
            var (_, cue) = Play(e, N);
            yield return (Rms(cue) > 0.1 * ClipRms, "a muted cued return is heard in Cue");
        }
    }

    // SC-001: whatever Cue does, Main is bit-identical to a render without it.
    private static IEnumerable<(bool, string)> MainUntouched(string wav)
    {
        const int N = Rate / 2;
        float[] Run(Action<NotaEngine, int> setup, bool cue)
        {
            using var e = Engine();
            int t = e.AddAudioTrack();
            e.AddAudioClip(t, wav, 0.0);
            e.SetTrackVolume(t, 0.7f);
            setup(e, t);
            return Play(e, N, cue).Main;
        }
        var reference = Run((_, _) => { }, cue: false);
        var cued = Run((e, t) => { e.SetTrackCue(t, true); e.CueVolume = 2f; }, cue: true);
        var quiet = Run((e, t) => { e.SetTrackCue(t, true); e.CueVolume = 0f; }, cue: true);
        yield return (reference.SequenceEqual(cued) && reference.SequenceEqual(quiet),
            "Main is bit-identical with the track in Cue at any Cue level (SC-001)");
        using (var e = Engine())
        {
            // A bounce has no Cue bus: a click sent to Cue never lands in the file (FR-016).
            e.AddAudioTrack();
            e.SetMetronome(true);
            e.ClickDestination = ClickDestination.Cue;
            e.RenderOffline(new float[2], 1, Rate);
            e.Seek(0.0);
            e.Play();
            var b = new float[Rate * 2];
            e.RenderOffline(b, Rate);
            yield return (b.All(v => v == 0f), "a bounce never carries a click sent to Cue");
        }
    }

    // ---- the click (US3) ---------------------------------------------------------------

    private static IEnumerable<(bool, string)> Click()
    {
        const int N = Rate; // two beats at 120 bpm
        float[] Main(int dest, bool metronome, out float[] cue, float gain = 1f)
        {
            using var e = Engine();
            e.AddAudioTrack();
            e.SetMetronome(metronome);
            e.ClickDestination = (ClickDestination)dest;
            e.SetClickVolume(gain);
            var r = Play(e, N);
            cue = r.Cue;
            return r.Main;
        }
        var off = Main(0, false, out _);
        var toMain = Main(0, true, out var cueWhenMain);
        var toCue = Main(1, true, out var cueWhenCue);
        var both = Main(2, true, out var cueWhenBoth);
        double Peak(float[] x) => x.Length == 0 ? 0 : x.Max(v => Math.Abs((double)v));
        yield return (Peak(toMain) > 0.1 && Peak(cueWhenMain) == 0, "click → Main: in Main, not in Cue (as before)");
        yield return (toCue.SequenceEqual(off) && Peak(cueWhenCue) > 0.1, "click → Cue: Main identical to metronome off (SC-002)");
        int First(float[] x) { for (int i = 0; i < x.Length; i++) if (Math.Abs(x[i]) > 1e-6f) return i / 2; return -1; }
        yield return (First(both) >= 0 && First(both) == First(cueWhenBoth), $"click → Main + Cue: same sample in both (SC-003, frame {First(both)})");
        Main(1, true, out var quiet, gain: 0f);
        yield return (Peak(quiet) == 0, "click level 0 silences the click");
    }

    private static float[] Stereo(float l, float r)
    {
        var b = new float[Frames * 2];
        for (int i = 0; i < Frames; i++) { b[i * 2] = l; b[i * 2 + 1] = r; }
        return b;
    }

    // Every frame of device channel `ch` equals `v`.
    private static bool Channel(float[] dev, int channels, int ch, float v)
    {
        for (int i = 0; i < Frames; i++)
            if (MathF.Abs(dev[i * channels + ch] - v) > 1e-6f) return false;
        return true;
    }

    private static IEnumerable<(bool, string)> Placement()
    {
        using var e = new NotaEngine();
        var main = Stereo(0.25f, -0.25f);
        var cue = Stereo(0.5f, -0.5f);

        yield return (e.SetOutputRouting(0, 2, 2, 2), "Main 1/2 + Cue 3/4 is a valid routing");
        var dev = new float[Frames * 4];
        var st = e.ScatterOutputsForTest(main, cue, dev, 4, Frames);
        yield return (st == OutputRoutingState.Ok, "4 outputs: routing state ok");
        yield return (Channel(dev, 4, 0, 0.25f) && Channel(dev, 4, 1, -0.25f), "Main lands on outputs 1/2");
        yield return (Channel(dev, 4, 2, 0.5f) && Channel(dev, 4, 3, -0.5f), "Cue lands on outputs 3/4");

        yield return (e.SetOutputRouting(2, 2, 0, 2), "Main 3/4 + Cue 1/2 is valid");
        dev = new float[Frames * 4];
        e.ScatterOutputsForTest(main, cue, dev, 4, Frames);
        yield return (Channel(dev, 4, 2, 0.25f) && Channel(dev, 4, 0, 0.5f), "Main can leave 1/2");

        e.SetOutputRouting(0, 2, -1, 2);
        dev = new float[Frames * 6];
        e.ScatterOutputsForTest(main, cue, dev, 6, Frames);
        bool othersZero = true;
        for (int c = 2; c < 6; c++) othersZero &= Channel(dev, 6, c, 0f);
        yield return (othersZero, "no Cue output: every other output is zero");

        e.SetOutputRouting(0, 2, 4, 1); // Cue on output 5 alone
        dev = new float[Frames * 5];
        e.ScatterOutputsForTest(main, Stereo(0.2f, 0.6f), dev, 5, Frames);
        yield return (Channel(dev, 5, 4, 0.4f), "a single-channel Cue gets the L+R sum halved");
    }

    private static IEnumerable<(bool, string)> Fallbacks()
    {
        using var e = new NotaEngine();
        var main = Stereo(0.25f, 0.25f);
        var cue = Stereo(0.5f, 0.5f);

        // Cue 3/4 on a stereo device: silent, never in Main (FR-014, SC-004).
        e.SetOutputRouting(0, 2, 2, 2);
        var dev = new float[Frames * 2];
        var st = e.ScatterOutputsForTest(main, cue, dev, 2, Frames);
        yield return (st == OutputRoutingState.CueUnavailable, "stereo device: Cue reported unavailable");
        yield return (Channel(dev, 2, 0, 0.25f) && Channel(dev, 2, 1, 0.25f), "stereo device: Main is exactly Main");

        // Main 3/4 on a stereo device falls back to 1/2 (FR-015).
        e.SetOutputRouting(2, 2, -1, 2);
        dev = new float[Frames * 2];
        st = e.ScatterOutputsForTest(main, cue, dev, 2, Frames);
        yield return (st == OutputRoutingState.MainFellBack && Channel(dev, 2, 0, 0.25f), "missing Main pair falls back to 1/2");

        // Main 3/4, Cue 1/2 on a stereo device: Main takes 1/2, Cue goes silent.
        e.SetOutputRouting(2, 2, 0, 2);
        dev = new float[Frames * 2];
        st = e.ScatterOutputsForTest(main, cue, dev, 2, Frames);
        yield return (st == (OutputRoutingState.MainFellBack | OutputRoutingState.CueUnavailable) && Channel(dev, 2, 0, 0.25f),
            "fallback Main wins over a Cue on its pair");

        // The engine's own state against a pretend device.
        e.SetOutputRouting(0, 2, 2, 2);
        e.SetTestOutputDevice(4);
        yield return (e.AudioOutputChannelCount() == 4 && e.GetOutputRoutingState() == OutputRoutingState.Ok,
            "4-output test device: routing ok");
        e.SetTestOutputDevice(2);
        yield return (e.GetOutputRoutingState() == OutputRoutingState.CueUnavailable, "2-output test device: Cue unavailable");
        e.SetTestOutputDevice(0);
    }

    private static IEnumerable<(bool, string)> RoutingValidation()
    {
        using var e = new NotaEngine();
        yield return (!e.SetOutputRouting(1, 2, -1, 2), "a stereo pair must start on an odd output (1/2, 3/4 …)");
        yield return (!e.SetOutputRouting(0, 3, -1, 2), "width is 1 or 2");
        yield return (!e.SetOutputRouting(0, 2, 0, 2), "Main and Cue can't share a pair");
        yield return (!e.SetOutputRouting(0, 2, 1, 1), "Cue can't take one of Main's outputs");
        yield return (e.SetOutputRouting(0, 2, -1, 2), "no Cue output is valid");
        yield return (e.SetOutputRouting(0, 1, 1, 1), "two single outputs side by side are valid");
    }

    private static IEnumerable<(bool, string)> SetupRules()
    {
        var s = OutputSetups.Default();
        yield return (s.MainFirst == 1 && s.MainWidth == 2 && !s.HasCue && s.Click == ClickDestination.Main,
            "a new device: Main 1/2, no Cue, click to Main");
        yield return (OutputSetups.Default(asioPair: 2).MainFirst == 3, "ASIO's saved output pair seeds Main (FR-005)");
        var c = OutputSetups.WithCue(s, 3, 2);
        yield return (c.CueFirst == 3, "Cue on 3/4");
        yield return (OutputSetups.WithCue(c, 1, 2).CueFirst == 3, "Cue on Main's pair is refused");
        yield return (OutputSetups.WithMain(c, 3, 2).CueFirst is null, "moving Main onto Cue drops Cue");
        var k = OutputSetups.WithClick(c, ClickDestination.Cue, 0);
        yield return (OutputSetups.WithCue(k, null, 2).Click == ClickDestination.Main, "no Cue output sends the click back to Main");
        yield return (OutputSetups.WithClick(s, ClickDestination.Cue, 0).Click == ClickDestination.Main,
            "the click can't go to a Cue that isn't set");
        yield return (OutputSetups.ClickGain(OutputSetups.WithClick(s, ClickDestination.Main, -60)) == 0f
                      && MathF.Abs(OutputSetups.ClickGain(s) - 1f) < 1e-6f, "click level: −60 dB silent, 0 dB unity");
    }
}
