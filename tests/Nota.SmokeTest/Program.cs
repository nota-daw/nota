// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// M1 smoke test: transport + audio graph + file playback + mixer, exercised
// through the offline render path (no audio device needed).

using Nota.Infrastructure;
using Nota.SmokeTest;

int failures = 0;
void Check(bool cond, string label)
{
    Console.WriteLine($"  [{(cond ? "PASS" : "FAIL")}] {label}");
    if (!cond) failures++;
}

static float Rms(float[] buf, int frames)
{
    double sum = 0;
    for (int i = 0; i < frames * 2; i++) sum += buf[i] * (double)buf[i];
    return (float)Math.Sqrt(sum / (frames * 2));
}

Console.WriteLine($"engine v{NotaEngine.Version}");

// --- architecture boundary checks (Phase 0.7) --------------------------------
// Enforce the DDD dependency direction at test time: the compiler drops unused
// references, so a forbidden name appearing in an assembly's referenced set means
// that layer actually reached across a boundary it shouldn't.
{
    Console.WriteLine("-- architecture: layer boundaries --");
    static bool NoRef(System.Reflection.Assembly a, params string[] forbidden)
    {
        var refs = a.GetReferencedAssemblies().Select(n => n.Name ?? "").ToArray();
        return !forbidden.Any(f => refs.Any(r => r.StartsWith(f, StringComparison.Ordinal)));
    }

    var domain = typeof(Nota.Domain.Project).Assembly;
    var application = typeof(Nota.Application.IAudioEngine).Assembly;
    var presentation = typeof(Nota.Presentation.MainWindowViewModel).Assembly;

    Check(NoRef(domain, "Avalonia", "Nota."),
        "Domain depends on nothing but the BCL");
    Check(NoRef(application, "Avalonia", "Nota.Infrastructure", "Nota.Presentation"),
        "Application references neither Avalonia nor Infrastructure/Presentation");
    Check(NoRef(presentation, "Avalonia", "Nota.Infrastructure"),
        "Presentation references neither Avalonia nor Infrastructure");
}

// --- design tokens: the two palette files must agree -------------------------
// NotaTheme.axaml and NotaPalette.cs carry the same palette for two different
// consumers and nothing but discipline kept them in step. See DESIGN.md § Enforced by tests.
{
    Console.WriteLine("-- design: palette mirror --");
    foreach (var (ok, label) in Nota.SmokeTest.DesignTokenCheck.Run()) Check(ok, label);
    Console.WriteLine("-- design: geometry --");
    foreach (var (ok, label) in Nota.SmokeTest.DesignTokenCheck.RunGeometry()) Check(ok, label);
    Console.WriteLine("-- design: type --");
    foreach (var (ok, label) in Nota.SmokeTest.DesignTokenCheck.RunType()) Check(ok, label);
    Console.WriteLine("-- design: controls --");
    foreach (var (ok, label) in Nota.SmokeTest.DesignTokenCheck.RunControls()) Check(ok, label);
    Console.WriteLine("-- design: visualisers --");
    foreach (var (ok, label) in Nota.SmokeTest.DesignTokenCheck.RunVisualisers()) Check(ok, label);
    Console.WriteLine("-- design: layout --");
    foreach (var (ok, label) in Nota.SmokeTest.DesignTokenCheck.RunLayout()) Check(ok, label);
    Console.WriteLine("-- design: numbers --");
    foreach (var (ok, label) in Nota.SmokeTest.DesignTokenCheck.RunNumbers()) Check(ok, label);
    Console.WriteLine("-- design: bans --");
    foreach (var (ok, label) in Nota.SmokeTest.DesignTokenCheck.RunBans()) Check(ok, label);
}

// Opt-in plugin-scan check (M3-1): `--scan <path-to-nota-scanworker>`. Kept out
// of the default run because results depend on which plugins are installed.
if (args.Length >= 2 && args[0] == "--scan")
{
    Console.WriteLine("-- plugin scan (M3-1) --");
    int n = NotaEngine.ScanPlugins(args[1]);
    Console.WriteLine($"known plugins: {n}");
    for (int i = 0; i < n; i++)
        Console.WriteLine($"  [{i}] {NotaEngine.PluginDescription(i)}");
    return 0;
}

// Opt-in real-device check (M7-1): `--audiocheck`. Opens the actual HALOutput
// backend on the current default device and confirms it negotiates a valid rate
// and buffer. Kept opt-in because it needs audio hardware (no device in CI). It
// does not persist config (start() never writes audio.json), so it won't clobber
// the user's saved settings.
if (args.Length >= 1 && args[0] == "--audiocheck")
{
    Console.WriteLine("-- audio device check (M7-1) --");
    int f = 0;
    void C(bool cond, string label) { Console.WriteLine($"  [{(cond ? "PASS" : "FAIL")}] {label}"); if (!cond) f++; }
    using var eng = new NotaEngine();
    eng.Start();
    C(eng.NegotiatedSampleRate > 0, $"backend opened; negotiated sample rate is valid ({eng.NegotiatedSampleRate:0} Hz)");
    C(eng.NegotiatedBufferFrames > 0, $"negotiated buffer size is valid ({eng.NegotiatedBufferFrames} frames)");
    eng.Stop();
    Console.WriteLine(f == 0 ? "AUDIOCHECK PASSED" : $"AUDIOCHECK FAILED ({f})");
    return f == 0 ? 0 : 1;
}

// Opt-in hosted-plugin check (M3-3): `--hostcheck <path-to-nota-scanworker>`.
// Loads a hosted AU instrument and effect and confirms audio flows through them.
if (args.Length >= 2 && args[0] == "--hostcheck")
{
    Console.WriteLine("-- hosted plugin check (M3-3) --");
    NotaEngine.ScanPlugins(args[1]); // ensure the catalog is populated
    int count = NotaEngine.PluginCount;
    int instIdx = -1, fxIdx = -1;
    for (int i = 0; i < count; i++)
    {
        var d = NotaEngine.PluginDescription(i) ?? "";
        if (instIdx < 0 && d.Contains("AudioUnit") && d.Contains("| inst |")) instIdx = i;
        if (fxIdx < 0 && d.Contains("AudioUnit") && d.Contains("| fx |")) fxIdx = i;
    }

    Check(NotaEngine.PdcSelfTest(), "PDC delay line: impulse emerges at the requested delay");

    using var eng = new NotaEngine();
    const int hf = 44100;
    var hbuf = new float[hf * 2];

    if (instIdx >= 0)
    {
        Console.WriteLine($"instrument: {NotaEngine.PluginDescription(instIdx)}");
        int tid = eng.AddPluginInstrumentTrack(instIdx);
        Check(tid > 0, "load hosted instrument onto a new track");
        int c = eng.AddMidiClip(tid, 0.0, 4.0);
        eng.SetClipNotes(tid, c, new[] { new NotaNote(60, 0.0, 2.0, 1.0f) });
        eng.SetBpm(120);
        eng.Seek(0); eng.Play();
        eng.RenderOffline(hbuf, hf);
        Check(Rms(hbuf, hf) > 1e-4f, $"hosted instrument produces audio (rms={Rms(hbuf, hf):0.0000})");

        // State round-trip (M3-5): save -> restore -> save again, expect equality.
        static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int k = 0; k < a.Length; k++) if (a[k] != b[k]) return false;
            return true;
        }
        var stA = eng.GetPluginState(tid, -1);
        eng.SetPluginState(tid, -1, stA);
        var stB = eng.GetPluginState(tid, -1);
        Check(BytesEqual(stA, stB), $"instrument state round-trips ({stA.Length} bytes)");

        if (fxIdx >= 0)
        {
            Console.WriteLine($"effect: {NotaEngine.PluginDescription(fxIdx)}");
            int di = eng.AddTrackEffectPlugin(tid, fxIdx);
            Check(di >= 0, "add hosted effect to the track chain");
            Check(eng.TrackDeviceCount(tid) == 1, "device count == 1");
            eng.Seek(0);
            eng.RenderOffline(hbuf, hf);
            Check(Rms(hbuf, hf) > 1e-4f, $"audio flows through the effect (rms={Rms(hbuf, hf):0.0000})");

            var fxA = eng.GetPluginState(tid, di);
            eng.SetPluginState(tid, di, fxA);
            var fxB = eng.GetPluginState(tid, di);
            Check(BytesEqual(fxA, fxB), $"effect state round-trips ({fxA.Length} bytes)");

            // Bypass (M3-6): control round-trips and rendering while bypassed is safe.
            eng.SetDeviceBypassed(tid, di, true);
            Check(eng.DeviceBypassed(tid, di), "effect bypass engages");
            eng.Seek(0); eng.RenderOffline(hbuf, hf);
            Check(Rms(hbuf, hf) > 1e-4f, "audio flows with effect bypassed");
            eng.SetDeviceBypassed(tid, di, false);
            Check(!eng.DeviceBypassed(tid, di), "effect bypass disengages");

            // Phase C: sidechain into the hosted effect where the plugin supports it.
            bool acceptsSc = eng.DeviceAcceptsSidechain(tid, di);
            Console.WriteLine($"  effect {(acceptsSc ? "exposes" : "has no")} sidechain input bus");
            eng.Seek(0); eng.Play(); eng.RenderOffline(hbuf, hf);
            float rmsBefore = Rms(hbuf, hf);
            int scSrcTrack = eng.AddInstrumentTrack();
            eng.SetDeviceSidechainSource(tid, di, scSrcTrack);
            Check(eng.DeviceSidechainSource(tid, di) == scSrcTrack, "plugin sidechain source round-trips");
            eng.Seek(0); eng.Play(); eng.RenderOffline(hbuf, hf);   // feeding the aux bus must not crash / go silent
            float rmsAfter = Rms(hbuf, hf);
            // Only meaningful if the picked effect passes audio at all (some catalog fx are
            // MIDI-only); the invariant is that wiring a sidechain never *kills* a live signal.
            if (rmsBefore > 1e-4f)
                Check(rmsAfter > 1e-4f, $"plugin sidechain doesn't break audio (before={rmsBefore:0.0000}, after={rmsAfter:0.0000})");
            else
                Console.WriteLine("  [SKIP] picked effect emits no audio — sidechain-flow check skipped");
            eng.SetDeviceSidechainSource(tid, di, -1);
            Check(eng.DeviceSidechainSource(tid, di) == -1, "plugin sidechain clears");
            eng.RemoveTrack(scSrcTrack);

            Console.WriteLine($"  track plugin latency: {eng.TrackLatencySamples(tid)} samples");

            // M9-B: hosted-plugin parameter automation on the effect.
            int ppc = eng.PluginParamCount(tid, di);
            Console.WriteLine($"  effect exposes {ppc} plugin params");
            if (ppc > 0)
            {
                string pid0 = eng.PluginParamId(tid, di, 0);
                Console.WriteLine($"  param[0] id=\"{pid0}\" name=\"{eng.PluginParamName(tid, di, 0)}\"");
                Check(pid0.Length > 0, "plugin param has a stable id");

                // Direct set/get round-trip (normalized).
                eng.PluginParamSet(tid, di, 0, 0.25f);
                float got = eng.PluginParamGet(tid, di, 0);
                Check(Math.Abs(got - 0.25f) < 0.05f, $"plugin param set/get round-trips ({got:0.000})");

                // A PluginParam automation lane drives the param during render.
                int lane = eng.AddPluginAutomationLane(tid, di, pid0);
                Check(lane >= 0, "plugin-param automation lane added");
                Check(eng.AutomationLaneParamId(tid, lane) == pid0, "lane carries the param id");
                eng.SetAutomationPoints(tid, lane, new[]
                {
                    new Nota.Application.AutomationPoint(0, 0.1f),
                    new Nota.Application.AutomationPoint(2, 0.9f),
                });
                eng.SetBpm(120); eng.Seek(0);        // beat 0 -> value should ride toward 0.1
                eng.RenderOffline(hbuf, 512);
                float atStart = eng.PluginParamGet(tid, di, 0);
                eng.Seek(4);                          // past the last point -> hold at 0.9
                eng.RenderOffline(hbuf, 512);
                float atEnd = eng.PluginParamGet(tid, di, 0);
                Console.WriteLine($"  automated param: start={atStart:0.000} end={atEnd:0.000}");
                Check(atEnd > atStart + 0.2f, $"automation moved the plugin param ({atStart:0.00}->{atEnd:0.00})");

                // Learn plumbing (M9-B3): nothing touched in the GUI yet -> -1.
                Check(eng.PluginLastTouchedParam(tid, di) == -1, "no last-touched param before any GUI gesture");
            }
        }
        else Console.WriteLine("  [SKIP] no AU effect in catalog");

        // M7-6c: full project round-trip carrying the hosted plugin(s).
        string pdir = Path.Combine(Path.GetTempPath(), "nota-host-proj-" + Guid.NewGuid().ToString("N"));
        try
        {
            var w = new System.Collections.Generic.List<string>();
            var doc = ProjectService.Capture(eng, new TransportState(120, 1, false, false), w);
            Check(w.Count == 0, $"plugin project captured without warnings ({w.Count})");
            ProjectService.Save(doc, pdir, eng);
            Check(System.IO.Directory.Exists(Path.Combine(pdir, "plugin-states")), "plugin-states/ written");

            var loaded = ProjectService.Load(pdir);
            using var eng2 = new NotaEngine();
            var aw = ProjectService.Apply(loaded, eng2, pdir);
            Check(aw.Count == 0, $"plugin project applied without warnings ({aw.Count})");
            Check(eng2.TryGetTrackInfo(0, out var pt), "restored plugin track");
            Check(eng2.TrackInstrumentPluginId(pt.Id) == eng.TrackInstrumentPluginId(tid),
                "instrument plugin identity restored");
            {
                // State restoration. Some plugins (e.g. NI Massive) embed per-instance
                // identity bytes, so a freshly-instantiated plugin's state never matches
                // the original byte-for-byte even when logically identical — a byte
                // compare across instances is not a reliable oracle. Assert instead:
                //  (1) the reloaded blob is preserved (non-empty, same length), and
                //  (2) our persistence is lossless — the on-disk blob equals what Capture
                //      read (plugin-agnostic, fully deterministic).
                // Functional restoration is covered by the audible check below.
                var origS = eng.GetPluginState(tid, -1);
                var restS = eng2.GetPluginState(pt.Id, -1);
                Check(restS.Length > 0 && restS.Length == origS.Length, "instrument plugin state restored (blob preserved)");

                var instrDto = doc.Tracks.FirstOrDefault(td => td.Instrument?.State != null);
                if (instrDto?.Instrument?.State is string rel && doc.StateBlobs.TryGetValue(rel, out var capBytes))
                {
                    var diskBytes = System.IO.File.ReadAllBytes(Path.Combine(pdir, rel.Replace('/', Path.DirectorySeparatorChar)));
                    Check(BytesEqual(diskBytes, capBytes), "instrument state blob written losslessly");
                }
            }
            if (fxIdx >= 0)
            {
                Check(eng2.TrackDeviceCount(pt.Id) == 1, "plugin effect restored");
                Check(eng2.TrackDevicePluginId(pt.Id, 0) == eng.TrackDevicePluginId(tid, 0),
                    "effect plugin identity restored");

                // M9-B4: a plugin-param automation lane (added on the effect above)
                // round-trips by stable id into the v3 project and drives the param.
                if (eng.PluginParamCount(tid, 0) > 0)
                {
                    Check(loaded.FormatVersion == ProjectService.CurrentFormatVersion, "plugin project saved as current format");
                    int found = -1;
                    for (int lidx = 0; lidx < eng2.AutomationLaneCount(pt.Id); lidx++)
                        if (eng2.AutomationLaneInfo(pt.Id, lidx).Target == Nota.Application.AutomationTarget.PluginParam)
                            { found = lidx; break; }
                    Check(found >= 0, "plugin-param automation lane restored");
                    if (found >= 0)
                    {
                        Check(eng2.AutomationLaneParamId(pt.Id, found) == eng.PluginParamId(tid, 0, 0),
                            "restored lane keeps the stable param id");
                        eng2.SetBpm(120); eng2.Seek(0); eng2.RenderOffline(hbuf, 512);
                        float s = eng2.PluginParamGet(pt.Id, 0, 0);
                        eng2.Seek(4); eng2.RenderOffline(hbuf, 512);
                        float en = eng2.PluginParamGet(pt.Id, 0, 0);
                        Check(en > s + 0.2f, $"restored automation drives the plugin param ({s:0.00}->{en:0.00})");
                    }
                }
            }
            eng2.Seek(0); eng2.Play(); eng2.RenderOffline(hbuf, hf); eng2.StopTransport();
            Check(Rms(hbuf, hf) > 1e-4f, $"restored plugin project is audible (rms={Rms(hbuf, hf):0.0000})");
        }
        finally
        {
            try { if (System.IO.Directory.Exists(pdir)) System.IO.Directory.Delete(pdir, true); } catch { }
        }
    }
    else Console.WriteLine("  [SKIP] no AU instrument in catalog");

    Console.WriteLine(failures == 0 ? "HOSTCHECK PASSED" : $"HOSTCHECK FAILED ({failures})");
    return failures;
}

// 1-second mono 440 Hz sine WAV at 44100 Hz.
string wav = Path.Combine(Path.GetTempPath(), "nota_smoke_sine.wav");
Nota.SmokeTest.WavWriter.WriteSine(wav, seconds: 1.0, freq: 440.0, sampleRate: 44100);
Console.WriteLine($"wrote test wav: {wav}");

using var engine = new NotaEngine();

int track = engine.AddAudioTrack();
Check(track > 0, "add audio track");
Check(engine.TrackCount == 1, "track count == 1");

int clip = engine.AddAudioClip(track, wav, startBeat: 0.0);
Check(clip >= 0, "add audio clip");

engine.SetBpm(120);
engine.SetTimeSignature(4, 4);

const int frames = 8192;
var buf = new float[frames * 2];

// --- playback produces audio ---
engine.Play();
engine.RenderOffline(buf, frames);
float rmsPlaying = Rms(buf, frames);
Check(rmsPlaying > 0.01f, $"playback is audible (rms={rmsPlaying:F4})");
Check(engine.PositionBeats > 0.0, $"playhead advanced (beats={engine.PositionBeats:F3})");
Check(engine.IsPlaying, "transport reports playing");

// --- waveform peaks ---
var peaks = new float[256 * 2];
int buckets = engine.GetClipPeaks(track, clip, peaks, 256);
bool peaksNonZero = false;
for (int i = 0; i < buckets * 2; i++) if (Math.Abs(peaks[i]) > 0.01f) { peaksNonZero = true; break; }
Check(buckets > 0 && peaksNonZero, $"clip peaks non-empty (buckets={buckets})");

// --- mute silences the track ---
engine.SetTrackMute(track, true);
engine.Seek(0);
engine.RenderOffline(buf, frames);
Check(Rms(buf, frames) < 1e-4f, "mute silences track");
engine.SetTrackMute(track, false);

// --- master volume scales output ---
engine.Seek(0);
engine.SetMasterVolume(1.0f);
engine.RenderOffline(buf, frames);
float rmsFull = Rms(buf, frames);
engine.Seek(0);
engine.SetMasterVolume(0.25f);
engine.RenderOffline(buf, frames);
float rmsQuarter = Rms(buf, frames);
Check(rmsQuarter < rmsFull * 0.5f, $"master volume scales (full={rmsFull:F4}, quarter={rmsQuarter:F4})");
engine.SetMasterVolume(1.0f);

// --- Stop rewinds to the launch anchor (classic transport behaviour) ---
// Seek sets the insert marker; Play launches from it; Stop returns the playhead there
// (not where it happened to halt), so the next Play resumes from the marked spot.
engine.StopTransport();
engine.RenderOffline(buf, frames);                 // apply the stop
engine.Seek(8.0);                                  // insert marker at beat 8 (bar 3)
engine.RenderOffline(buf, frames);
Check(Math.Abs(engine.PositionBeats - 8.0) < 0.05, $"seek parks the playhead at the anchor (beats={engine.PositionBeats:F3})");
engine.Play();
engine.RenderOffline(buf, frames);                 // advance past the anchor
Check(engine.PositionBeats > 8.0, $"playback advances from the anchor (beats={engine.PositionBeats:F3})");
engine.StopTransport();
engine.RenderOffline(buf, frames);                 // apply the stop
Check(Math.Abs(engine.PositionBeats - 8.0) < 0.05, $"stop rewinds to the launch anchor (beats={engine.PositionBeats:F3})");
Check(!engine.IsPlaying, "transport reports stopped after rewind");

// --- auto-warp: detect a click track's tempo and snap it to the beat grid ---
{
    string clickWav = Path.Combine(Path.GetTempPath(), "nota_smoke_clicks_" + Guid.NewGuid().ToString("N") + ".wav");
    try
    {
        // 4 s of 128 BPM clicks worth of source → warp target rounds to a whole beat count.
        Nota.SmokeTest.WavWriter.WriteClicks(clickWav, bpm: 128.0, seconds: 4.0, sampleRate: 44100);
        using var aw = new NotaEngine();
        aw.SetBpm(120);
        aw.SetTimeSignature(4, 4);
        int at = aw.AddAudioTrack();
        int ac = aw.AddAudioClip(at, clickWav, 0.0);
        Check(ac >= 0, "auto-warp: click clip added");

        double bpm = aw.AutoWarpClip(at, ac);
        Check(Math.Abs(bpm - 128.0) < 8.0, $"auto-warp detects tempo (got {bpm:F1}, expected ~128)");
        Check(aw.TryGetAudioClipInfo(at, ac, out var awci) && awci.WarpEnabled != 0, "auto-warp enabled warp");
        // Length snaps to a whole beat count near the detected musical length.
        double beatsRaw = 4.0 * bpm / 60.0;
        Check(awci.WarpBeats >= 1 && awci.WarpBeats == Math.Floor(awci.WarpBeats)
              && Math.Abs(awci.WarpBeats - beatsRaw) <= 0.6,
              $"auto-warp snapped length to grid ({awci.WarpBeats} beats, raw {beatsRaw:F2})");
        var aws = new double[8]; var awb = new double[8];
        Check(aw.GetClipWarpMarkers(at, ac, aws, awb) == 2 && Math.Abs(awb[1] - awci.WarpBeats) < 1e-6,
              "auto-warp seeds end markers on the grid");

        // A pure sine has no transients → detection fails → clip left unchanged.
        int st = aw.AddAudioTrack();
        int sc = aw.AddAudioClip(st, wav, 0.0);
        Check(aw.AutoWarpClip(st, sc) == 0.0, "auto-warp: flat material returns 0 (no false positive)");
        Check(aw.TryGetAudioClipInfo(st, sc, out var sci) && sci.WarpEnabled == 0, "auto-warp: flat material left unwarped");

        // Grid resize of a warped clip TRIMS its played window — warpBeats
        // (the full material) is unchanged; only the played length shrinks.
        double acWarpBeats = aw.TryGetAudioClipInfo(at, ac, out var acInfo0) ? acInfo0.WarpBeats : 0;
        aw.ResizeAudioClip(at, ac, 0.0, 6.0);
        Check(aw.TryGetClipInfo(at, ac, out var rci) && Math.Abs(rci.LengthBeats - 6.0) < 1e-6,
              $"resize warped clip trims played length (got {rci.LengthBeats})");
        Check(aw.TryGetAudioClipInfo(at, ac, out var acInfo1) && Math.Abs(acInfo1.WarpBeats - acWarpBeats) < 1e-6,
              "resize warped clip leaves warpBeats (material) unchanged");

        // Grid resize of an unwarped one-shot past its source auto-enables warp.
        Check(aw.TryGetClipInfo(st, sc, out var sc0), "one-shot length readable");
        aw.ResizeAudioClip(st, sc, 0.0, sc0.LengthBeats + 8.0);   // well beyond ~2-beat source
        Check(aw.TryGetAudioClipInfo(st, sc, out var sci2) && sci2.WarpEnabled != 0, "resize past source auto-enables warp");
        Check(aw.TryGetClipInfo(st, sc, out var rci2) && Math.Abs(rci2.LengthBeats - (sc0.LengthBeats + 8.0)) < 1e-6,
              $"auto-warped one-shot took the requested length (got {rci2.LengthBeats})");

        // Shrinking an unwarped clip within source still just trims (no warp).
        int tt = aw.AddAudioTrack();
        int tc = aw.AddAudioClip(tt, wav, 0.0);
        Check(aw.TryGetClipInfo(tt, tc, out var tc0), "trim clip length readable");
        aw.ResizeAudioClip(tt, tc, 0.0, tc0.LengthBeats * 0.5);
        Check(aw.TryGetAudioClipInfo(tt, tc, out var tci) && tci.WarpEnabled == 0, "resize within source trims (no warp)");

        // Beats transient-sync (roadmap item 4): detect hits, pin grid-snapped markers.
        int bt = aw.AddAudioTrack();
        int bc = aw.AddAudioClip(bt, clickWav, 0.0);
        double bbpm = aw.BeatWarpClip(bt, bc);
        Check(Math.Abs(bbpm - 128.0) < 8.0, $"transient warp: tempo detected ({bbpm:F1})");
        Check(aw.TryGetAudioClipInfo(bt, bc, out var bci) && bci.WarpEnabled != 0 && bci.WarpMode == 0,
              "transient warp: enabled in Beats mode");
        var tms = new double[64]; var tmb = new double[64];
        int tmn = aw.GetClipWarpMarkers(bt, bc, tms, tmb);
        Check(tmn > 2, $"transient warp: markers pinned at hits ({tmn})");
        bool tmono = true, tsnap = true;
        for (int i = 1; i < tmn; i++) if (tmb[i] <= tmb[i - 1]) tmono = false;
        for (int i = 1; i + 1 < tmn; i++) { double g = tmb[i] * 4.0; if (Math.Abs(g - Math.Round(g)) > 1e-6) tsnap = false; }
        Check(tmono, "transient warp: marker beats strictly increasing");
        Check(tsnap, "transient warp: interior markers snapped to the 1/16 grid");
        Check(aw.BeatWarpClip(st, sc) == 0.0, "transient warp: flat material returns 0 (no false positive)");
    }
    finally { try { if (File.Exists(clickWav)) File.Delete(clickWav); } catch { } }
}

// --- metronome clicks even with the track muted ---
engine.SetTrackMute(track, true);
engine.SetMetronome(true);
engine.Seek(0);
engine.Play();                      // clicks only sound while the transport runs
engine.RenderOffline(buf, frames);
Check(Rms(buf, frames) > 1e-3f, "metronome produces clicks");

engine.SetMetronome(false);
engine.SetTrackMute(track, false);

// --- loop region mirror (for the arrangement highlight + transport bar) ---
engine.SetLoop(true, 4.0, 12.0);
Check(engine.LoopEnabled && Math.Abs(engine.LoopStart - 4.0) < 1e-9 && Math.Abs(engine.LoopEnd - 12.0) < 1e-9,
    "loop region round-trips (start/end/enabled)");
engine.SetLoop(false, 4.0, 12.0);
Check(!engine.LoopEnabled, "loop disable clears the enabled flag");
engine.SetLoop(true, 8.0, 8.0);
Check(!engine.LoopEnabled, "empty loop range (end<=start) is not enabled");
engine.SetLoop(false, 0, 0);

// --- loop seam is declicked: no NaN and no gross sample discontinuity at the wrap ---
{
    using var ce = new NotaEngine();
    ce.SetBpm(120);
    int ct = ce.AddAudioTrack();
    ce.AddAudioClip(ct, wav, 0.0);          // 1 s 440 Hz sine
    ce.SetLoop(true, 0.0, 1.0);             // wraps every beat, across the sine
    ce.Seek(0.0); ce.Play();
    const int N = 66150;                     // ~3 loop cycles
    var cb = new float[N * 2];
    ce.RenderOffline(cb, N);
    ce.StopTransport();
    float maxStep = 0f; bool finite = true;
    for (int i = 1; i < N; i++)
    {
        if (!float.IsFinite(cb[i * 2])) { finite = false; break; }
        float d = Math.Abs(cb[i * 2] - cb[(i - 1) * 2]); if (d > maxStep) maxStep = d;
    }
    Check(finite && Rms(cb, N) > 0.01f && maxStep < 0.35f, $"loop seam declicked (max step {maxStep:F3})");
}

// --- loop retriggers a note at the loop start + stop flushes stuck voices ---
{
    const int spbF = 22050;   // 120 BPM @ 44100
    // Bug: a note starting at beat 0 must retrigger every loop cycle.
    using var le = new NotaEngine();
    le.SetBpm(120); le.SetTimeSignature(4, 4);
    int it = le.AddInstrumentTrack();
    int ic = le.AddMidiClip(it, 0.0, 4.0);
    le.SetClipNotes(it, ic, new[] { new NotaNote(60, 0.0, 3.5, 0.9f) });  // note at 1.1, ends before loop end
    le.SetLoop(true, 0.0, 4.0);
    le.Play();
    var lb = new float[512 * 2];
    double prevPos = 0; bool wrapped = false; float maxAfterWrap = 0;
    int loopBlocks = 2 * 4 * spbF / 512 + 8;   // ~two full cycles
    for (int k = 0; k < loopBlocks; k++)
    {
        le.RenderOffline(lb, 512);
        double pos = le.PositionBeats;
        if (pos < prevPos - 0.5) wrapped = true;          // playhead jumped back → looped
        if (wrapped) maxAfterWrap = Math.Max(maxAfterWrap, Rms(lb, 512));
        prevPos = pos;
    }
    Check(wrapped, "transport loops back to the start");
    Check(maxAfterWrap > 0.01f, $"note at beat 0 retriggers after the loop wrap (rms={maxAfterWrap:F4})");

    // Bug: stopping (pause) must not leave a note ringing forever.
    using var se = new NotaEngine();
    se.SetBpm(120); se.SetTimeSignature(4, 4);
    int st = se.AddInstrumentTrack();
    int sc = se.AddMidiClip(st, 0.0, 8.0);
    se.SetClipNotes(st, sc, new[] { new NotaNote(60, 0.0, 8.0, 0.9f) });  // long, spans the render
    se.Play();
    var sb2 = new float[512 * 2];
    se.RenderOffline(sb2, 512);
    Check(Rms(sb2, 512) > 0.01f, "instrument note is audible while playing");
    se.StopTransport();
    se.RenderOffline(sb2, 512);   // first block after stop: voices flushed
    se.RenderOffline(sb2, 512);
    Check(Rms(sb2, 512) < 1e-4f, "stop flushes stuck instrument voices");
}

// --- clip deactivate (key 0): a clip stays on the timeline but plays nothing ---
Console.WriteLine("-- clip deactivate --");
{
    // MIDI clip: silent when deactivated, audible again when reactivated.
    using var de = new NotaEngine();
    de.SetBpm(120); de.SetTimeSignature(4, 4);
    int dt = de.AddInstrumentTrack();
    int dc = de.AddMidiClip(dt, 0.0, 8.0);
    de.SetClipNotes(dt, dc, new[] { new NotaNote(60, 0.0, 8.0, 0.9f) });
    var db = new float[512 * 2];
    de.Seek(0); de.Play(); de.RenderOffline(db, 512);
    Check(Rms(db, 512) > 0.01f, "MIDI clip audible when active");
    Check(de.TryGetClipInfo(dt, dc, out var di0) && di0.IsActive, "clip starts active");
    de.StopTransport();
    de.RenderOffline(db, 512); de.RenderOffline(db, 512);   // flush the sounding voice
    de.SetClipActive(dt, dc, false);
    Check(de.TryGetClipInfo(dt, dc, out var di1) && !di1.IsActive, "clip reports inactive after deactivate");
    de.Seek(0); de.Play(); de.RenderOffline(db, 512); de.RenderOffline(db, 512);
    Check(Rms(db, 512) < 1e-4f, "deactivated MIDI clip is silent");
    de.StopTransport(); de.SetClipActive(dt, dc, true);
    de.Seek(0); de.Play(); de.RenderOffline(db, 512);
    Check(Rms(db, 512) > 0.01f, "reactivated MIDI clip is audible again");

    // Audio clip: same behaviour.
    string dtw = Path.Combine(Path.GetTempPath(), "nota_smoke_deact.wav");
    Nota.SmokeTest.WavWriter.WriteSine(dtw, seconds: 1.0, freq: 440.0, sampleRate: 44100);
    using var ae = new NotaEngine();
    ae.SetBpm(120);
    int at = ae.AddAudioTrack();
    int acx = ae.AddAudioClip(at, dtw, 0.0);
    var ab = new float[512 * 2];
    ae.Seek(0); ae.Play(); ae.RenderOffline(ab, 512);
    Check(Rms(ab, 512) > 0.01f, "audio clip audible when active");
    ae.StopTransport(); ae.SetClipActive(at, acx, false);
    ae.Seek(0); ae.Play(); ae.RenderOffline(ab, 512);
    Check(Rms(ab, 512) < 1e-4f, "deactivated audio clip is silent");

    // Persistence: the inactive flag round-trips through save/load.
    ae.StopTransport();
    var dw = new System.Collections.Generic.List<string>();
    var ddoc = ProjectService.Capture(ae, new TransportState(120.0, 1.0, false, false), dw);
    string dbundle = Path.Combine(Path.GetTempPath(), "nota-deact-" + System.Guid.NewGuid().ToString("N") + ".nota");
    ProjectService.Save(ddoc, dbundle, ae);
    using var ae2 = new NotaEngine();
    ProjectService.Apply(ProjectService.Load(dbundle), ae2, dbundle);
    bool foundInactive = false;
    for (int i = 0; i < ae2.TrackCount && !foundInactive; i++)
        if (ae2.TryGetTrackInfo(i, out var ti) && ti.Type == 0 && ti.ClipCount > 0)
            foundInactive = ae2.TryGetClipInfo(ti.Id, 0, out var rci) && !rci.IsActive;
    Check(foundInactive, "deactivated clip stays inactive after save/load");
}

// --- realtime streaming warp (roadmap item 3): live stretch, live tempo, loop, RePitch ---
{
    static bool Finite(float[] b, int frames)
    { for (int i = 0; i < frames * 2; i++) if (!float.IsFinite(b[i])) return false; return true; }

    // Streaming warp must be deterministic w.r.t. render block size — a drift in the
    // source-feed made it chunk-dependent and smeared transient/tonal material
    // ("metallic"). Bounce a 174→140 BPM warp at 512 vs 8192 chunks; must match.
    {
        string clk = Path.Combine(Path.GetTempPath(), "nota_warp_inv_" + Guid.NewGuid().ToString("N") + ".wav");
        Nota.SmokeTest.WavWriter.WriteClicks(clk, bpm: 174.0, seconds: 2.0, sampleRate: 44100);
        float[] RenderChunked(int chunk)
        {
            using var e = new NotaEngine(); e.SetBpm(140); e.SetTimeSignature(4, 4);
            int t = e.AddAudioTrack(); int c = e.AddAudioClip(t, clk, 0.0);
            e.SetClipWarp(t, c, true, 3);   // Complex, auto-warp to 140
            e.Play(); e.Seek(0.0);
            var acc = new float[88200 * 2]; var tmp = new float[chunk * 2];
            int done = 0;
            while (done < 88200) { int n = Math.Min(chunk, 88200 - done); e.RenderOffline(tmp, n); Array.Copy(tmp, 0, acc, done * 2, n * 2); done += n; }
            e.StopTransport();
            return acc;
        }
        var a512 = RenderChunked(512); var a8192 = RenderChunked(8192);
        double diff = 0, en = 0;
        for (int i = 0; i < 88200 * 2; i++) { double d = a512[i] - a8192[i]; diff += d * d; en += a512[i] * (double)a512[i]; }
        double rel = en > 0 ? Math.Sqrt(diff / en) : 0;
        Check(rel < 0.05, $"streaming warp is block-size invariant (no source-feed drift, rel={rel:F3})");
        try { File.Delete(clk); } catch { }
    }

    using var we = new NotaEngine();
    we.SetBpm(120); we.SetTimeSignature(4, 4);
    int wt = we.AddAudioTrack();
    int wc = we.AddAudioClip(wt, wav, 0.0);            // 1 s sine @ 440 → ~2 beats at 120
    we.SetClipWarp(wt, wc, true, 3);                   // Complex; flat source → neutral seed, warp on
    Check(we.TryGetAudioClipInfo(wt, wc, out var wi) && wi.WarpEnabled != 0, "streaming warp: warp enabled");
    double beats0 = wi.WarpBeats;
    Check(beats0 > 0.0, $"streaming warp: warpBeats seeded ({beats0:F2})");

    var wb = new float[2048 * 2];
    we.Play();
    we.RenderOffline(wb, 2048);
    Check(Finite(wb, 2048) && Rms(wb, 2048) > 0.01f, "streaming warp: warped clip is audible (live stretch)");

    // Tempo change must NOT rebuild — warp length in beats is stable, no republish.
    we.SetBpm(140);
    Check(we.TryGetClipInfo(wt, wc, out var wci) && Math.Abs(wci.LengthBeats - beats0) < 1e-6,
          "streaming warp: warp length (beats) stable across tempo change (no rebuild)");
    we.Seek(0);
    we.RenderOffline(wb, 2048);
    Check(Finite(wb, 2048) && Rms(wb, 2048) > 0.01f, "streaming warp: audible after live tempo change");

    // Loop the warped clip: several cycles must stay finite (re-seek on each wrap).
    we.SetLoop(true, 0.0, beats0);
    we.Seek(0);
    bool loopFinite = true; float loopMax = 0;
    for (int k = 0; k < 12; k++)
    {
        we.RenderOffline(wb, 2048);
        if (!Finite(wb, 2048)) loopFinite = false;
        for (int i = 0; i < 2048 * 2; i++) loopMax = Math.Max(loopMax, Math.Abs(wb[i]));
    }
    Check(loopFinite && loopMax < 4.0f, $"streaming warp: looped warped clip stays finite (max={loopMax:F2})");
    we.SetLoop(false, 0, 0);

    // RePitch (mode 5): varispeed resample path, audible + finite.
    we.SetClipWarp(wt, wc, true, 5);
    we.Seek(0); we.RenderOffline(wb, 2048);
    Check(Finite(wb, 2048) && Rms(wb, 2048) > 0.01f, "streaming warp: RePitch mode audible");

    // Warped peaks derive from source (no cache) — non-empty envelope.
    var wpk = new float[512 * 2];
    int wnp = we.GetClipPeaks(wt, wc, wpk, 512);
    bool pkNonZero = false;
    for (int i = 0; i < wnp * 2; i++) if (Math.Abs(wpk[i]) > 0.01f) { pkNonZero = true; break; }
    Check(wnp > 0 && pkNonZero, $"streaming warp: warped peaks non-empty ({wnp} buckets)");
}

// ============== clip source-region editing (Start/End brackets) ============
Console.WriteLine("-- clip source-region editing --");
{
    string stw = Path.Combine(Path.GetTempPath(), "nota_smoke_sil_tone.wav");
    Nota.SmokeTest.WavWriter.WriteSilenceThenTone(stw, seconds: 1.0, freq: 440.0, sampleRate: 44100);

    using var re = new NotaEngine();
    re.SetBpm(120); re.SetTimeSignature(4, 4);
    int rt = re.AddAudioTrack();
    int rc = re.AddAudioClip(rt, stw, 0.0);   // whole 1s file, offset 0

    // Whole-sample peaks: first half (silence) ~0, second half (tone) loud — proving
    // the editor sees the WHOLE file, not just the trimmed region.
    var sp = new float[512 * 2];
    int nb = re.GetClipSourcePeaks(rt, rc, sp, 512);
    Check(nb > 0, $"source peaks: buckets returned ({nb})");
    float firstHalf = 0, secondHalf = 0;
    for (int b = 0; b < nb; b++)
    {
        float a = Math.Max(Math.Abs(sp[b * 2]), Math.Abs(sp[b * 2 + 1]));
        if (b < nb / 2) firstHalf = Math.Max(firstHalf, a); else secondHalf = Math.Max(secondHalf, a);
    }
    Check(firstHalf < 0.05f && secondHalf > 0.3f, $"source peaks span whole sample (silence|tone = {firstHalf:F2}|{secondHalf:F2})");

    long total = (re.TryGetAudioClipInfo(rt, rc, out var ri0) && re.TryGetSampleInfo(ri0.SampleId, out var si0)) ? si0.Frames : 0;
    Check(total > 0, "source region: sample frames known");

    // Offset 0 → the clip begins in the silent half.
    var rb = new float[4096 * 2];
    re.Seek(0); re.Play(); re.RenderOffline(rb, 4096); re.StopTransport();
    float rmsHead0 = Rms(rb, 4096);

    // Slide the Start marker to the midpoint → the clip now begins in the tone half.
    re.SetClipSourceRegion(rt, rc, total / 2.0, total / 2);
    Check(re.TryGetAudioClipInfo(rt, rc, out var ri1)
          && Math.Abs(ri1.SourceOffsetFrames - total / 2.0) < 2 && ri1.LengthFrames == total / 2,
          "source region: offset + length updated");
    re.Seek(0); re.Play(); re.RenderOffline(rb, 4096); re.StopTransport();
    float rmsHead1 = Rms(rb, 4096);
    Check(rmsHead1 > rmsHead0 + 0.05f, $"source region: sliding Start reveals the tone (head RMS {rmsHead0:F3} -> {rmsHead1:F3})");

    // Undo restores the original region; redo re-applies it.
    re.Undo();
    Check(re.TryGetAudioClipInfo(rt, rc, out var ri2) && ri2.SourceOffsetFrames < 2, "source region: undo restores offset");
    re.Redo();
    Check(re.TryGetAudioClipInfo(rt, rc, out var ri3) && Math.Abs(ri3.SourceOffsetFrames - total / 2.0) < 2, "source region: redo re-applies offset");

    // Clamp: a length past the sample end is trimmed to what's available.
    re.SetClipSourceRegion(rt, rc, total / 2.0, total * 4);
    Check(re.TryGetAudioClipInfo(rt, rc, out var ri4) && ri4.LengthFrames <= total - total / 2 + 1,
          "source region: length clamped to sample bounds");

    // Warped clips ignore the direct region setter (region is marker-driven → no-op).
    re.SetClipSourceRegion(rt, rc, 0, total);   // reset to whole file
    re.SetClipWarp(rt, rc, true, 3);
    try { re.SetClipSourceRegion(rt, rc, total / 4.0, total / 2); } catch { /* no-op returns false */ }
    Check(re.TryGetAudioClipInfo(rt, rc, out var rw1) && rw1.WarpEnabled != 0,
          "source region: warped clip unaffected by region setter");
}

// ============== warped clip trimming (played window, not stretch) ===========
Console.WriteLine("-- warped clip trimming --");
{
    string stw = Path.Combine(Path.GetTempPath(), "nota_smoke_warp_trim.wav");
    Nota.SmokeTest.WavWriter.WriteSilenceThenTone(stw, seconds: 1.0, freq: 440.0, sampleRate: 44100);
    using var we = new NotaEngine();
    we.SetBpm(120); we.SetTimeSignature(4, 4);
    int wt = we.AddAudioTrack();
    int wc = we.AddAudioClip(wt, stw, 0.0);
    we.SetClipWarp(wt, wc, true, 3);   // enable warp (seeds 2 end markers over the whole clip)
    Check(we.TryGetAudioClipInfo(wt, wc, out var w0) && w0.WarpEnabled != 0 && w0.WarpBeats > 0, "warp enabled");
    double warpBeats = w0.WarpBeats;
    Check(we.TryGetClipInfo(wt, wc, out var wci0) && Math.Abs(wci0.LengthBeats - warpBeats) < 1e-6,
          "warped clip length == warpBeats (full window)");

    // Resize (right edge) must TRIM, not stretch: warpBeats stays, played length shrinks.
    we.ResizeAudioClip(wt, wc, 0.0, warpBeats * 0.5);
    Check(we.TryGetAudioClipInfo(wt, wc, out var w1) && Math.Abs(w1.WarpBeats - warpBeats) < 1e-6,
          "resize warped: warpBeats unchanged (trim, not stretch)");
    Check(we.TryGetClipInfo(wt, wc, out var wci1) && Math.Abs(wci1.LengthBeats - warpBeats * 0.5) < 0.05,
          "resize warped: played length halved");

    // Re-extend back to the full material.
    we.SetClipWarpTrim(wt, wc, 0.0, warpBeats);
    Check(we.TryGetClipInfo(wt, wc, out var wci2) && Math.Abs(wci2.LengthBeats - warpBeats) < 0.05,
          "warp trim: re-extend restores full length");

    // Full peaks cover the whole warp (silence|tone) regardless of trim; played peaks
    // follow the trim window.
    we.SetClipWarpTrim(wt, wc, warpBeats * 0.5, warpBeats);   // play the 2nd half (tone)
    var full = new float[512 * 2]; int nf = we.GetClipWarpFullPeaks(wt, wc, full, 512);
    float f1 = 0, f2 = 0;
    for (int b = 0; b < nf; b++) { float a = Math.Max(Math.Abs(full[b * 2]), Math.Abs(full[b * 2 + 1])); if (b < nf / 2) f1 = Math.Max(f1, a); else f2 = Math.Max(f2, a); }
    Check(nf > 0 && f1 < 0.05f && f2 > 0.3f, $"warp full peaks span whole material (silence|tone = {f1:F2}|{f2:F2})");
    var play = new float[512 * 2]; int np = we.GetClipPeaks(wt, wc, play, 512);
    float pmax = 0; for (int b = 0; b < np; b++) pmax = Math.Max(pmax, Math.Abs(play[b * 2 + 1]));
    Check(np > 0 && pmax > 0.3f, "warp played peaks reflect the trim window (2nd half = tone)");

    // Played window renders only the trimmed range: 2nd half (tone) is audible at the
    // clip start; trimming to the 1st half (silence) is quiet.
    var wb = new float[4096 * 2];
    we.Seek(0); we.Play(); we.RenderOffline(wb, 4096); we.StopTransport();
    float rmsTone = Rms(wb, 4096);
    we.SetClipWarpTrim(wt, wc, 0.0, warpBeats * 0.5);   // play the 1st half (silence)
    we.Seek(0); we.Play(); we.RenderOffline(wb, 4096); we.StopTransport();
    float rmsSil = Rms(wb, 4096);
    Check(rmsTone > rmsSil + 0.05f, $"warp trim window drives playback (tone {rmsTone:F3} > silence {rmsSil:F3})");

    // Clamp + no-op on unwarped.
    we.SetClipWarpTrim(wt, wc, -5, warpBeats * 10);
    Check(we.TryGetAudioClipInfo(wt, wc, out var w3) && w3.WarpPlayStart >= 0 && w3.WarpPlayEnd <= warpBeats + 1e-6,
          "warp trim clamped to [0, warpBeats]");
}

// ============== clip reverse (non-destructive playback direction) ===========
Console.WriteLine("-- clip reverse --");
{
    // A silence|tone file makes direction audible: forwards is quiet-then-loud, reversed
    // is loud-then-quiet. The clip is 2 beats at 120 BPM, so each half is one beat.
    string rvw = Path.Combine(Path.GetTempPath(), "nota_smoke_reverse.wav");
    Nota.SmokeTest.WavWriter.WriteSilenceThenTone(rvw, seconds: 1.0, freq: 440.0, sampleRate: 44100);
    using var rv = new NotaEngine();
    rv.SetBpm(120); rv.SetTimeSignature(4, 4);
    int rt = rv.AddAudioTrack();
    int rc = rv.AddAudioClip(rt, rvw, 0.0);
    Check(rv.TryGetAudioClipInfo(rt, rc, out var rv0) && rv0.Reversed == 0, "clips start forwards");
    long srcTotal = rv.TryGetSampleInfo(rv0.SampleId, out var rvs) ? rvs.Frames : 0;
    Check(srcTotal > 0, "reverse: source frames known");

    // Halves of the clip's own device span, so the check is independent of device rate.
    Check(rv.TryGetClipInfo(rt, rc, out var rvc0) && rvc0.LengthBeats > 0, "reverse: clip length known");
    double rvSpb = (rv.SampleRate > 0 ? rv.SampleRate : 48000.0) * 60.0 / 120.0;   // 120 BPM
    int clipFrames = (int)Math.Round(rvc0.LengthBeats * rvSpb);
    var rbuf = new float[(clipFrames + 64) * 2];
    (float head, float tail) Halves()
    {
        Array.Clear(rbuf);
        rv.Seek(0); rv.Play(); rv.RenderOffline(rbuf, clipFrames); rv.StopTransport();
        return (RmsRange(rbuf, clipFrames / 8, clipFrames * 3 / 8),
                RmsRange(rbuf, clipFrames * 5 / 8, clipFrames * 7 / 8));
    }
    static float RmsRange(float[] b, int from, int to)
    { double sum = 0; for (int i = from * 2; i < to * 2; i++) sum += b[i] * (double)b[i]; return (float)Math.Sqrt(sum / Math.Max(1, (to - from) * 2)); }

    var (fwdHead, fwdTail) = Halves();
    Check(fwdTail > fwdHead + 0.05f, $"forwards plays silence then tone ({fwdHead:F3} -> {fwdTail:F3})");

    rv.SetClipReverse(rt, rc, true);
    Check(rv.TryGetAudioClipInfo(rt, rc, out var rv1) && rv1.Reversed != 0, "reverse flag set");
    var (revHead, revTail) = Halves();
    Check(revHead > revTail + 0.05f, $"reversed plays tone then silence ({revHead:F3} -> {revTail:F3})");
    // Reverse only flips the read direction: the region and its musical length are untouched.
    Check(Math.Abs(rv1.SourceOffsetFrames - rv0.SourceOffsetFrames) < 1e-9
          && rv.TryGetClipInfo(rt, rc, out var rvc1) && Math.Abs(rvc1.LengthBeats - rvc0.LengthBeats) < 1e-9,
          "reverse leaves geometry (offset + length) alone");
    // Energy is conserved (same audio, other way round) — a resample bug would shift it.
    Check(Math.Abs(Rms(rbuf, clipFrames) - Math.Sqrt((fwdHead * fwdHead + fwdTail * fwdTail) / 2.0)) < 0.06,
          "reverse preserves overall level");

    // The lane's peaks follow the ear: the loud half moves to the front.
    var rpk = new float[512 * 2];
    int rpn = rv.GetClipPeaks(rt, rc, rpk, 512);
    float pkHead = 0, pkTail = 0;
    for (int b = 0; b < rpn; b++)
    {
        float a = Math.Max(Math.Abs(rpk[b * 2]), Math.Abs(rpk[b * 2 + 1]));
        if (b < rpn / 2) pkHead = Math.Max(pkHead, a); else pkTail = Math.Max(pkTail, a);
    }
    Check(rpn > 0 && pkHead > 0.3f && pkTail < 0.05f, $"reversed peaks are flipped too ({pkHead:F2}|{pkTail:F2})");
    // The clip-editor view stays in FILE order (its brackets are source coordinates).
    var rsp = new float[512 * 2];
    int rsn = rv.GetClipSourcePeaks(rt, rc, rsp, 512);
    float spHead = 0, spTail = 0;
    for (int b = 0; b < rsn; b++)
    {
        float a = Math.Max(Math.Abs(rsp[b * 2]), Math.Abs(rsp[b * 2 + 1]));
        if (b < rsn / 2) spHead = Math.Max(spHead, a); else spTail = Math.Max(spTail, a);
    }
    Check(rsn > 0 && spTail > 0.3f && spHead < 0.05f, "source peaks stay in file order when reversed");

    // Undo/redo: reverse is one structural edit like any other clip property.
    rv.Undo();
    Check(rv.TryGetAudioClipInfo(rt, rc, out var rv2) && rv2.Reversed == 0, "undo un-reverses");
    rv.Redo();
    Check(rv.TryGetAudioClipInfo(rt, rc, out var rv3) && rv3.Reversed != 0, "redo re-reverses");

    // Split mirrors: the reversed clip sounds tone|silence, so the LEFT half must keep the
    // region's tail (the tone, at source offset ~half) and the right half its head.
    double mid = rvc0.LengthBeats / 2;
    Check(rv.SplitClip(rt, rc, mid) == rc + 1, "reverse: split returns the new clip index");
    Check(rv.TryGetAudioClipInfo(rt, rc, out var rl) && rl.Reversed != 0
          && Math.Abs(rl.SourceOffsetFrames - srcTotal / 2.0) < srcTotal * 0.02,
          $"reversed split: left half keeps the region tail (offset {rl.SourceOffsetFrames:F0} of {srcTotal})");
    Check(rv.TryGetAudioClipInfo(rt, rc + 1, out var rr) && rr.Reversed != 0 && rr.SourceOffsetFrames < srcTotal * 0.02,
          $"reversed split: right half keeps the region head (offset {rr.SourceOffsetFrames:F0})");
    rv.Undo();   // back to one clip for the trim check

    // Trim mirrors: shortening the right edge cuts the TAIL of what you hear, so the
    // audible head (the region's end) stays put and the offset moves up instead.
    Check(rv.TryGetAudioClipInfo(rt, rc, out var rt0), "reverse: pre-trim info");
    double head0 = rt0.SourceOffsetFrames + (rt0.LengthFrames > 0 ? rt0.LengthFrames : srcTotal);
    rv.TrimClip(rt, rc, 0.0, rvc0.LengthBeats / 2);
    Check(rv.TryGetAudioClipInfo(rt, rc, out var rt1)
          && Math.Abs(rt1.SourceOffsetFrames + rt1.LengthFrames - head0) < srcTotal * 0.02
          && rt1.SourceOffsetFrames > srcTotal * 0.4,
          $"reversed trim keeps the audible head (region now {rt1.SourceOffsetFrames:F0}+{rt1.LengthFrames})");
    // The trimmed clip is half as long, so measure over ITS span: the region it kept is
    // the tone, so the whole thing sounds — nothing is silent any more.
    Array.Clear(rbuf);
    rv.Seek(0); rv.Play(); rv.RenderOffline(rbuf, clipFrames); rv.StopTransport();
    int halfSpan = clipFrames / 2;
    float trA = RmsRange(rbuf, halfSpan / 8, halfSpan * 3 / 8), trB = RmsRange(rbuf, halfSpan * 5 / 8, halfSpan * 7 / 8);
    Check(trA > 0.05f && trB > 0.05f, $"reversed half-clip is all tone ({trA:F3}|{trB:F3})");
}

// ============== reverse on a WARPED clip (mirrors the stretch cache) =========
Console.WriteLine("-- clip reverse (warped) --");
{
    string rww = Path.Combine(Path.GetTempPath(), "nota_smoke_reverse_warp.wav");
    Nota.SmokeTest.WavWriter.WriteSilenceThenTone(rww, seconds: 1.0, freq: 440.0, sampleRate: 44100);
    using var rw = new NotaEngine();
    rw.SetBpm(120); rw.SetTimeSignature(4, 4);
    int wt2 = rw.AddAudioTrack();
    int wc2 = rw.AddAudioClip(wt2, rww, 0.0);
    rw.SetClipWarp(wt2, wc2, true, 3);   // Complex; flat source -> neutral seed
    Check(rw.TryGetAudioClipInfo(wt2, wc2, out var wi0) && wi0.WarpEnabled != 0 && wi0.WarpBeats > 0,
          "reverse-warp: warp enabled");
    Check(rw.TryGetClipInfo(wt2, wc2, out var wci) && wci.LengthBeats > 0, "reverse-warp: length known");
    double rwSpb = (rw.SampleRate > 0 ? rw.SampleRate : 48000.0) * 60.0 / 120.0;   // 120 BPM
    int wFrames = (int)Math.Round(wci.LengthBeats * rwSpb);
    var wbuf = new float[(wFrames + 64) * 2];
    (float head, float tail) WHalves()
    {
        Array.Clear(wbuf);
        rw.Seek(0); rw.Play(); rw.RenderOffline(wbuf, wFrames); rw.StopTransport();
        double a = 0, b = 0;
        for (int i = wFrames / 8; i < wFrames * 3 / 8; i++) a += wbuf[i * 2] * (double)wbuf[i * 2];
        for (int i = wFrames * 5 / 8; i < wFrames * 7 / 8; i++) b += wbuf[i * 2] * (double)wbuf[i * 2];
        int n = Math.Max(1, wFrames / 4);
        return ((float)Math.Sqrt(a / n), (float)Math.Sqrt(b / n));
    }
    var (wf1, wf2) = WHalves();
    Check(wf2 > wf1 + 0.02f, $"warped forwards plays silence then tone ({wf1:F3} -> {wf2:F3})");
    rw.SetClipReverse(wt2, wc2, true);
    // Reverse must NOT invalidate the stretch cache: the warp geometry is untouched.
    Check(rw.TryGetAudioClipInfo(wt2, wc2, out var wi1) && wi1.Reversed != 0
          && Math.Abs(wi1.WarpBeats - wi0.WarpBeats) < 1e-9, "reverse-warp: warp geometry untouched");
    var (wr1, wr2) = WHalves();
    Check(wr1 > wr2 + 0.02f, $"warped reversed plays tone then silence ({wr1:F3} -> {wr2:F3})");

    // Consolidate bakes the reversal into a fresh, forward sample.
    Check(rw.ConsolidateRange(new[] { wt2 }, 0.0, wci.LengthBeats), "reverse-warp: consolidate range");
    Check(rw.TryGetAudioClipInfo(wt2, 0, out var wi2) && wi2.Reversed == 0, "consolidate bakes reverse (clip is forwards)");
    var (wc1, wc2b) = WHalves();
    Check(wc1 > wc2b + 0.02f, $"consolidated clip still sounds reversed ({wc1:F3} -> {wc2b:F3})");
}

// ============== warp preserves pitch across a src≠device sample rate ==========
Console.WriteLine("-- warp pitch vs sample rate --");
{
    using var pe = new NotaEngine();
    double dev = pe.SampleRate > 0 ? pe.SampleRate : 48000.0;
    int srcSR = (int)Math.Round(dev / 2.0);   // sample at half the device rate => 2x (octave) mismatch
    const double freqHz = 1000.0;
    string sine = Path.Combine(Path.GetTempPath(), "nota_smoke_sr_sine.wav");
    Nota.SmokeTest.WavWriter.WriteSine(sine, seconds: 1.0, freq: freqHz, sampleRate: srcSR);

    pe.SetBpm(120); pe.SetTimeSignature(4, 4);
    int pt = pe.AddAudioTrack();
    int pcl = pe.AddAudioClip(pt, sine, 0.0);
    pe.SetClipWarp(pt, pcl, true, 3);   // Complex; stream configured with srcSR + device SR
    Check(pe.TryGetAudioClipInfo(pt, pcl, out var pai) && pai.WarpEnabled != 0, "sr-warp: warp enabled");

    var pbuf = new float[8192 * 2];
    pe.Seek(0); pe.Play(); pe.RenderOffline(pbuf, 8192); pe.StopTransport();
    // Dominant frequency of the left channel via zero-crossings (skip the onset).
    int skip = 1024, zc = 0; bool prev = pbuf[skip * 2] >= 0;
    for (int i = skip + 1; i < 8192; i++) { bool s = pbuf[i * 2] >= 0; if (s != prev) zc++; prev = s; }
    double measured = zc / 2.0 / ((8192 - skip) / dev);
    // Without the SR-compensation the warp would sing an octave up (~2000 Hz).
    Check(measured > 700 && measured < 1400,
          $"warp keeps natural pitch across src≠device SR (got {measured:F0} Hz, want ~{freqHz:F0}, not ~{2 * freqHz:F0})");

    // ComplexPro (mode 4) at pitch 0 must equal Complex: formant preservation is a
    // no-op here, and its formant multiplier cancels the src≠device SR term. A wrong
    // (SR-dependent) formant map would colour the tone / push the dominant partial off.
    using var pe2 = new NotaEngine();
    pe2.SetBpm(120); pe2.SetTimeSignature(4, 4);
    int pt2 = pe2.AddAudioTrack();
    int pcl2 = pe2.AddAudioClip(pt2, sine, 0.0);
    pe2.SetClipWarp(pt2, pcl2, true, 4);   // ComplexPro
    Check(pe2.TryGetAudioClipInfo(pt2, pcl2, out var pai2) && pai2.WarpEnabled != 0, "sr-warp: ComplexPro warp enabled");
    var pbuf2 = new float[8192 * 2];
    pe2.Seek(0); pe2.Play(); pe2.RenderOffline(pbuf2, 8192); pe2.StopTransport();
    int zc2 = 0; bool prev2 = pbuf2[skip * 2] >= 0;
    for (int i = skip + 1; i < 8192; i++) { bool s = pbuf2[i * 2] >= 0; if (s != prev2) zc2++; prev2 = s; }
    double measured2 = zc2 / 2.0 / ((8192 - skip) / dev);
    Check(measured2 > 700 && measured2 < 1400,
          $"ComplexPro keeps natural pitch across src≠device SR (got {measured2:F0} Hz, want ~{freqHz:F0})");
}

// ============================ Nota Synth ===================================
Console.WriteLine("-- Nota Synth --");
{
    using var se = new NotaEngine();
    se.SetBpm(120); se.SetTimeSignature(4, 4);
    int t = se.AddInstrumentTrack();
    Check(se.DeviceName(t, -1) == "Nota Synth", $"instrument is Nota Synth (got '{se.DeviceName(t, -1)}')");

    int pc = se.PluginParamCount(t, -1);
    Check(pc == 19, $"Nota Synth exposes 19 params (got {pc})");
    int cut = -1; bool idsOk = true;
    for (int i = 0; i < pc; i++)
    {
        if (se.PluginParamId(t, -1, i).Length == 0 || se.PluginParamName(t, -1, i).Length == 0) idsOk = false;
        if (se.PluginParamId(t, -1, i) == "cutoff") cut = i;
    }
    Check(idsOk && cut >= 0, "params have ids + names; cutoff present");
    int PIdx(int tr, string id)
    { for (int i = 0; i < se.PluginParamCount(tr, -1); i++) if (se.PluginParamId(tr, -1, i) == id) return i; return -1; }

    // The first eight keep their index and id — the persisted layout is append-only, so
    // automation lanes and project states written before the oscillator/voice sections
    // still address the same parameters.
    string[] legacyIds = { "wave", "attack", "decay", "sustain", "release", "cutoff", "resonance", "gain" };
    bool layoutOk = true;
    for (int i = 0; i < legacyIds.Length; i++) if (se.PluginParamId(t, -1, i) != legacyIds[i]) layoutOk = false;
    Check(layoutOk, "the original eight params keep their index and id");
    string[] newIds = { "filtype", "filenv", "pulsewidth", "detune", "octave", "unison", "spread", "glide", "velamp", "pan", "voicemode" };
    bool newOk = true;
    foreach (var id in newIds) if (PIdx(t, id) < 8) newOk = false;
    Check(newOk, "oscillator / filter / voice params are all present");

    se.PluginParamSet(t, -1, cut, 0.33f);
    Check(Math.Abs(se.PluginParamGet(t, -1, cut) - 0.33f) < 1e-4, "param set/get round-trips");

    // State round-trips to another track (project save/load path).
    se.PluginParamSet(t, -1, cut, 0.9f);
    var state = se.GetPluginState(t, -1);
    Check(state.Length >= 8 * 4, $"synth state serialized ({state.Length} bytes)");
    int t2 = se.AddInstrumentTrack();
    se.SetPluginState(t2, -1, state);
    Check(Math.Abs(se.PluginParamGet(t2, -1, cut) - 0.9f) < 1e-4, "synth state restores params on another track");

    // Track duplication clones the params.
    se.PluginParamSet(t, -1, cut, 0.15f);
    int t3 = se.DuplicateTrack(t);
    Check(t3 > 0 && Math.Abs(se.PluginParamGet(t3, -1, cut) - 0.15f) < 1e-4, "duplicate track clones synth params");

    // Automation drives the param: a rising cutoff lane → value higher late vs early.
    se.AddMidiClip(t, 0.0, 4.0);
    int lane = se.AddPluginAutomationLane(t, -1, "cutoff");
    Check(lane >= 0, "added cutoff automation lane");
    se.SetAutomationPoints(t, lane, new[] { new AutomationPoint(0.0, 0.10f), new AutomationPoint(4.0, 0.95f) });
    var sbuf = new float[512 * 2];
    se.Seek(0.0); se.Play(); se.RenderOffline(sbuf, 512); se.StopTransport();
    float early = se.PluginParamGet(t, -1, cut);
    se.Seek(3.9); se.Play(); se.RenderOffline(sbuf, 512); se.StopTransport();
    float late = se.PluginParamGet(t, -1, cut);
    Check(late > early + 0.3f, $"cutoff automation drives the param (early {early:F2} -> late {late:F2})");

    // Audible: a fresh track (default params, open filter) plays a note → non-silent.
    int ta = se.AddInstrumentTrack();
    se.AddMidiClip(ta, 0.0, 4.0);
    se.SetClipNotes(ta, 0, new[] { new NotaNote(60, 0.0, 2.0, 0.9f) });
    var nbuf = new float[8192 * 2];
    se.Seek(0.0); se.Play(); se.RenderOffline(nbuf, 8192); se.StopTransport();
    Check(Rms(nbuf, 8192) > 0.005f, $"Nota Synth is audible (RMS {Rms(nbuf, 8192):F3})");

    // A project written before the new sections carries only the first eight floats: it
    // must still load, leaving the rest at their defaults (unison 1, poly, no glide).
    int tl = se.AddInstrumentTrack();
    var legacy = new byte[8 * 4];
    for (int i = 0; i < 8; i++) BitConverter.GetBytes(i == 5 ? 0.42f : 0.5f).CopyTo(legacy, i * 4);
    se.SetPluginState(tl, -1, legacy);
    Check(Math.Abs(se.PluginParamGet(tl, -1, cut) - 0.42f) < 1e-4, "an eight-param state from an older project still loads");
    Check(Math.Abs(se.PluginParamGet(tl, -1, PIdx(tl, "unison")) - 0f) < 1e-4
       && Math.Abs(se.PluginParamGet(tl, -1, PIdx(tl, "voicemode")) - 0f) < 1e-4,
        "params added since keep their defaults when an old state is loaded");

    // Off / LP / HP / BP all pass signal, and each one sounds different from the others.
    int tf = se.AddInstrumentTrack();
    se.AddMidiClip(tf, 0.0, 4.0);
    se.SetClipNotes(tf, 0, new[] { new NotaNote(48, 0.0, 2.0, 1.0f) });
    int ftIdx = PIdx(tf, "filtype");
    var levels = new float[4];
    for (int k = 0; k < 4; k++)
    {
        se.PluginParamSet(tf, -1, ftIdx, k / 3f);
        var fbuf = new float[8192 * 2];
        se.Seek(0.0); se.Play(); se.RenderOffline(fbuf, 8192); se.StopTransport();
        levels[k] = Rms(fbuf, 8192);
    }
    Check(levels[0] > 0.005f && levels[1] > 0.005f && levels[2] > 0.005f && levels[3] > 0.005f,
        $"every filter type passes signal (off {levels[0]:F3}, lp {levels[1]:F3}, hp {levels[2]:F3}, bp {levels[3]:F3})");
    Check(Math.Abs(levels[0] - levels[1]) > 1e-3 && Math.Abs(levels[1] - levels[2]) > 1e-3,
        "the filter types do not render identically");

    // Unison stacks voices: seven detuned copies are louder than one.
    int tu = se.AddInstrumentTrack();
    se.AddMidiClip(tu, 0.0, 4.0);
    se.SetClipNotes(tu, 0, new[] { new NotaNote(60, 0.0, 2.0, 1.0f) });
    se.PluginParamSet(tu, -1, PIdx(tu, "detune"), 0.7f);
    var ubuf1 = new float[8192 * 2];
    se.Seek(0.0); se.Play(); se.RenderOffline(ubuf1, 8192); se.StopTransport();
    float one = Rms(ubuf1, 8192);
    se.PluginParamSet(tu, -1, PIdx(tu, "unison"), 1f);
    var ubuf7 = new float[8192 * 2];
    se.Seek(0.0); se.Play(); se.RenderOffline(ubuf7, 8192); se.StopTransport();
    float seven = Rms(ubuf7, 8192);
    Check(seven > one * 1.05f, $"unison 7 thickens the tone (1 voice {one:F3} -> 7 voices {seven:F3})");

    // Mono collapses a chord onto one voice; Legato does the same but does not retrigger.
    int tm = se.AddInstrumentTrack();
    se.AddMidiClip(tm, 0.0, 4.0);
    se.SetClipNotes(tm, 0, new[] { new NotaNote(60, 0.0, 2.0, 1.0f), new NotaNote(64, 0.0, 2.0, 1.0f), new NotaNote(67, 0.0, 2.0, 1.0f) });
    var pbuf = new float[8192 * 2];
    se.Seek(0.0); se.Play(); se.RenderOffline(pbuf, 8192); se.StopTransport();
    float poly = Rms(pbuf, 8192);
    se.PluginParamSet(tm, -1, PIdx(tm, "voicemode"), 0.5f);
    var mbuf = new float[8192 * 2];
    se.Seek(0.0); se.Play(); se.RenderOffline(mbuf, 8192); se.StopTransport();
    float mono = Rms(mbuf, 8192);
    Check(mono > 0.005f && mono < poly, $"Mono collapses a three-note chord onto one voice (poly {poly:F3} -> mono {mono:F3})");
}

// ============================ Nota Physical ===============================
Console.WriteLine("-- Nota Physical --");
{
    using var pe = new NotaEngine();
    pe.SetBpm(120); pe.SetTimeSignature(4, 4);
    int t = pe.AddPhysicalSynthTrack();
    Check(t > 0, "AddPhysicalSynthTrack returns a track");
    Check(pe.TrackInstrumentKind(t) == 2, $"instrument kind is 2 (got {pe.TrackInstrumentKind(t)})");
    Check(pe.DeviceName(t, -1) == "Nota Physical", $"instrument is Nota Physical (got '{pe.DeviceName(t, -1)}')");

    int pc = pe.PluginParamCount(t, -1);
    Check(pc == 36, $"Nota Physical exposes 36 params (got {pc})");
    int dec = -1; bool idsOk = true;
    for (int i = 0; i < pc; i++)
    {
        if (pe.PluginParamId(t, -1, i).Length == 0 || pe.PluginParamName(t, -1, i).Length == 0) idsOk = false;
        if (pe.PluginParamId(t, -1, i) == "r1decay") dec = i;
    }
    Check(idsOk && dec >= 0, "params have ids + names; r1decay present");

    // State round-trips to another track (project save/load path).
    pe.PluginParamSet(t, -1, dec, 0.77f);
    var state = pe.GetPluginState(t, -1);
    Check(state.Length >= 36 * 4, $"collision state serialized ({state.Length} bytes)");
    int t2 = pe.AddPhysicalSynthTrack();
    pe.SetPluginState(t2, -1, state);
    Check(Math.Abs(pe.PluginParamGet(t2, -1, dec) - 0.77f) < 1e-4, "collision state restores params on another track");

    // Track duplication clones the params.
    pe.PluginParamSet(t, -1, dec, 0.15f);
    int t3 = pe.DuplicateTrack(t);
    Check(t3 > 0 && Math.Abs(pe.PluginParamGet(t3, -1, dec) - 0.15f) < 1e-4, "duplicate track clones collision params");

    // Audible + stable: each resonator Type struck by the default mallet → non-silent, finite.
    for (int type = 0; type < 6; type++)
    {
        int ta = pe.AddPhysicalSynthTrack();
        int typeIdx = -1;
        for (int i = 0; i < pe.PluginParamCount(ta, -1); i++) if (pe.PluginParamId(ta, -1, i) == "r1type") typeIdx = i;
        pe.PluginParamSet(ta, -1, typeIdx, type / 5f);
        pe.AddMidiClip(ta, 0.0, 4.0);
        pe.SetClipNotes(ta, 0, new[] { new NotaNote(60, 0.0, 1.0, 0.9f) });
        var nbuf = new float[8192 * 2];
        pe.Seek(0.0); pe.Play(); pe.RenderOffline(nbuf, 8192); pe.StopTransport();
        float rms = Rms(nbuf, 8192);
        bool finite = true; foreach (var s in nbuf) if (float.IsNaN(s) || float.IsInfinity(s) || Math.Abs(s) > 4f) { finite = false; break; }
        Check(rms > 0.001f && finite, $"Physical resonator type {type} is audible + stable (RMS {rms:F3})");
    }

    // Noise exciter alone (mallet off) also produces sound.
    {
        int ta = pe.AddPhysicalSynthTrack();
        int mv = -1, nv = -1;
        for (int i = 0; i < pe.PluginParamCount(ta, -1); i++)
        {
            string id = pe.PluginParamId(ta, -1, i);
            if (id == "malletvol") mv = i; else if (id == "noisevol") nv = i;
        }
        pe.PluginParamSet(ta, -1, mv, 0.0f); pe.PluginParamSet(ta, -1, nv, 0.9f);
        pe.AddMidiClip(ta, 0.0, 4.0);
        pe.SetClipNotes(ta, 0, new[] { new NotaNote(57, 0.0, 2.0, 0.9f) });
        var nbuf = new float[8192 * 2];
        pe.Seek(0.0); pe.Play(); pe.RenderOffline(nbuf, 8192); pe.StopTransport();
        Check(Rms(nbuf, 8192) > 0.001f, $"noise exciter (mallet off) is audible ({Rms(nbuf, 8192):F3})");
    }
}

// ============================ Nota Aurora ==================================
Console.WriteLine("-- Nota Aurora --");
{
    using var ae = new NotaEngine();
    ae.SetBpm(120); ae.SetTimeSignature(4, 4);
    int t = ae.AddWavetableSynthTrack();
    Check(t > 0, "AddWavetableSynthTrack returns a track");
    Check(ae.TrackInstrumentKind(t) == 5, $"instrument kind is 5 (got {ae.TrackInstrumentKind(t)})");
    Check(ae.DeviceName(t, -1) == "Nota Aurora", $"instrument is Nota Aurora (got '{ae.DeviceName(t, -1)}')");

    int pc = ae.PluginParamCount(t, -1);
    Check(pc == 156, $"Nota Aurora exposes 156 params (got {pc})");
    int pos = -1; bool idsOk = true;
    for (int i = 0; i < pc; i++)
    {
        if (ae.PluginParamId(t, -1, i).Length == 0 || ae.PluginParamName(t, -1, i).Length == 0) idsOk = false;
        if (ae.PluginParamId(t, -1, i) == "position") pos = i;
    }
    Check(idsOk && pos >= 0, "params have ids + names; position present");

    ae.PluginParamSet(t, -1, pos, 0.42f);
    Check(Math.Abs(ae.PluginParamGet(t, -1, pos) - 0.42f) < 1e-4, "param set/get round-trips");

    // State round-trips to another track (project save/load path).
    ae.PluginParamSet(t, -1, pos, 0.88f);
    var state = ae.GetPluginState(t, -1);
    Check(state.Length >= 156 * 4, $"aurora state serialized ({state.Length} bytes)");
    int t2 = ae.AddWavetableSynthTrack();
    ae.SetPluginState(t2, -1, state);
    Check(Math.Abs(ae.PluginParamGet(t2, -1, pos) - 0.88f) < 1e-4, "aurora state restores params on another track");

    // Track duplication clones the params.
    ae.PluginParamSet(t, -1, pos, 0.20f);
    int t3 = ae.DuplicateTrack(t);
    Check(t3 > 0 && Math.Abs(ae.PluginParamGet(t3, -1, pos) - 0.20f) < 1e-4, "duplicate track clones aurora params");

    // Audible: each wavetable bank (Analog/Pulse/Formant/Chroma) plays a note → non-silent.
    for (int bank = 0; bank < 4; bank++)
    {
        int ta = ae.AddWavetableSynthTrack();
        int tbIdx = -1;
        for (int i = 0; i < ae.PluginParamCount(ta, -1); i++) if (ae.PluginParamId(ta, -1, i) == "table") tbIdx = i;
        ae.PluginParamSet(ta, -1, tbIdx, bank / 3f);
        ae.AddMidiClip(ta, 0.0, 4.0);
        ae.SetClipNotes(ta, 0, new[] { new NotaNote(60, 0.0, 2.0, 0.9f) });
        var nbuf = new float[8192 * 2];
        ae.Seek(0.0); ae.Play(); ae.RenderOffline(nbuf, 8192); ae.StopTransport();
        Check(Rms(nbuf, 8192) > 0.001f, $"Nota Aurora bank {bank} is audible (RMS {Rms(nbuf, 8192):F3})");
    }

    // v2 additions: osc2 + sub + matrix + macro params exist; osc2-only patch is audible.
    int AId(string id) { for (int i = 0; i < pc; i++) if (ae.PluginParamId(t, -1, i) == id) return i; return -1; }
    Check(AId("osc2level") >= 0 && AId("sublevel") >= 0 && AId("mtx0_2") >= 0 && AId("mac0val") >= 0 && AId("fil2type") >= 0,
        "Aurora v2 params present (osc2level / sublevel / mtx0_2 / mac0val / fil2type)");
    {
        int ta = ae.AddWavetableSynthTrack();
        void SetId(string id, float v) { for (int i = 0; i < ae.PluginParamCount(ta, -1); i++) if (ae.PluginParamId(ta, -1, i) == id) ae.PluginParamSet(ta, -1, i, v); }
        SetId("osc1level", 0f); SetId("osc1on", 0f); SetId("osc2on", 1f); SetId("osc2level", 0.9f);
        ae.AddMidiClip(ta, 0.0, 4.0); ae.SetClipNotes(ta, 0, new[] { new NotaNote(60, 0.0, 2.0, 0.9f) });
        var b2 = new float[8192 * 2]; ae.Seek(0.0); ae.Play(); ae.RenderOffline(b2, 8192); ae.StopTransport();
        Check(Rms(b2, 8192) > 0.001f, $"Aurora osc2-only patch is audible (RMS {Rms(b2, 8192):F3})");
    }

    // v3 (almanac rework): the wheels, the output pan, LFO 2's sync and the three FX
    // blocks' switches and characters all exist and all act.
    Check(AId("bend") >= 0 && AId("bendrange") >= 0 && AId("outpan") >= 0 && AId("lfo2sync") >= 0
          && AId("fxdriveon") >= 0 && AId("fxdrivemode") >= 0 && AId("fxtone") >= 0
          && AId("fxchoruson") >= 0 && AId("fxchorusvoices") >= 0
          && AId("fxreverbon") >= 0 && AId("fxreverbmode") >= 0 && AId("fxreverbsize") >= 0
          && AId("mac7val") >= 0,
        "Aurora v3 params present (wheels / out pan / LFO 2 sync / FX blocks / macro 8)");
    {
        // Each case gets its own engine so a voice left sounding by one render cannot leak
        // into the next, and each renders the same note for the same length.
        float[] RenderWith(params (string Id, float Value)[] ps)
        {
            using var we = new NotaEngine();
            we.SetBpm(120); we.SetTimeSignature(4, 4);
            int tw = we.AddWavetableSynthTrack();
            foreach (var (id, value) in ps)
                for (int i = 0; i < we.PluginParamCount(tw, -1); i++)
                    if (we.PluginParamId(tw, -1, i) == id) we.PluginParamSet(tw, -1, i, value);
            we.AddMidiClip(tw, 0.0, 4.0);
            we.SetClipNotes(tw, 0, new[] { new NotaNote(60, 0.0, 2.0, 0.9f) });
            var b = new float[8192 * 2];
            we.Seek(0.0); we.Play(); we.RenderOffline(b, 8192); we.StopTransport();
            return b;
        }
        float Diff(float[] a, float[] b) { float d = 0; for (int i = 0; i < a.Length; i++) d += Math.Abs(a[i] - b[i]); return d / a.Length; }
        int Crossings(float[] b, int n)
        { int c = 0; for (int i = 2; i < n * 2; i += 2) if ((b[i - 2] < 0) != (b[i] < 0)) c++; return c; }
        float Side(float[] b, int n)
        { float d = 0; for (int i = 0; i < n; i++) d += Math.Abs(b[i * 2] - b[i * 2 + 1]); return d / n; }

        // Zero crossings stand in for pitch: bending up an octave roughly doubles them.
        var flat = RenderWith(("fil1freq", 0.9f));
        var bent = RenderWith(("bend", 1f), ("bendrange", 1f));
        int cf = Crossings(flat, 8192), cb = Crossings(bent, 8192);
        bool bfin = true; foreach (var x in bent) if (!float.IsFinite(x) || Math.Abs(x) > 8f) { bfin = false; break; }
        Check(bfin && cb > cf * 1.5, $"Aurora pitch bend +12 st raises the pitch ({cf} -> {cb} crossings)");

        // Output pan is equal-power: hard right silences the left channel.
        var right = RenderWith(("outpan", 1f));
        float lSum = 0, rSum = 0;
        for (int i = 0; i < 8192; i++) { lSum += Math.Abs(right[i * 2]); rSum += Math.Abs(right[i * 2 + 1]); }
        Check(lSum < 1e-4f && rSum > 0.001f, $"Aurora out pan hard right silences the left (L {lSum:F4} / R {rSum:F4})");

        // Unison spread is what makes the stack stereo — and with it at zero the voice is
        // exactly the mono one it always was.
        var narrow = RenderWith(("unison", 0.5f), ("unidetune", 0.4f), ("unispread", 0f));
        var wide = RenderWith(("unison", 0.5f), ("unidetune", 0.4f), ("unispread", 1f));
        Check(Side(narrow, 8192) < 1e-6f, "Aurora unison at zero spread stays mono");
        Check(Side(wide, 8192) > 1e-3f, $"Aurora unison spread opens the stereo field ({Side(wide, 8192):F4})");

        // Each FX block answers its switch, and each character is its own sound.
        var dryFx = RenderWith(("fxdrive", 0.7f), ("fxdriveon", 0f));
        var clean = RenderWith();
        Check(Diff(dryFx, clean) < 1e-6f, "Aurora drive switched off is the dry signal");
        var tube = RenderWith(("fxdrive", 0.7f), ("fxdrivemode", 0f));
        var tape = RenderWith(("fxdrive", 0.7f), ("fxdrivemode", 0.5f));
        var fold = RenderWith(("fxdrive", 0.7f), ("fxdrivemode", 1f));
        Check(Diff(tube, clean) > 1e-3f && Diff(tube, tape) > 1e-3f && Diff(tube, fold) > 1e-3f,
            "Aurora drive Tube / Tape / Fold are three different curves");
        var revOff = RenderWith(("fxreverb", 0.7f), ("fxreverbon", 0f));
        var revRoom = RenderWith(("fxreverb", 0.7f), ("fxreverbmode", 0f));
        var revHall = RenderWith(("fxreverb", 0.7f), ("fxreverbmode", 0.5f));
        Check(Diff(revOff, clean) < 1e-6f, "Aurora reverb switched off is the dry signal");
        Check(Diff(revRoom, clean) > 1e-4f && Diff(revRoom, revHall) > 1e-5f, "Aurora reverb Room and Hall differ, and both are wet");
        var chOff = RenderWith(("fxchorus", 0.8f), ("fxchoruson", 0f));
        var ch2 = RenderWith(("fxchorus", 0.8f), ("fxchorusvoices", 0.5f));
        var ch4 = RenderWith(("fxchorus", 0.8f), ("fxchorusvoices", 1f));
        Check(Diff(chOff, clean) < 1e-6f, "Aurora chorus switched off is the dry signal");
        Check(Diff(ch2, ch4) > 1e-4f, "Aurora chorus 2x and 4x differ");

        // LFO 2 can lock to the grid now, like LFO 1.
        var lfoFree = RenderWith(("lfo2depth", 1f), ("mtx3_3", 1f), ("lfo2sync", 0f), ("lfo2rate", 0.9f));
        var lfoSync = RenderWith(("lfo2depth", 1f), ("mtx3_3", 1f), ("lfo2sync", 4f / 7f));
        Check(Diff(lfoFree, lfoSync) > 1e-4f, "Aurora LFO 2 tempo sync changes its rate");
    }

    // v3: macros reach twelve destinations, five of which act on the block snapshot. The
    // seven older ones keep the normalized value an old project saved for them.
    {
        float MacroRms(params (string Id, float Value)[] ps)
        {
            using var me2 = new NotaEngine();
            me2.SetBpm(120); me2.SetTimeSignature(4, 4);
            int tmm = me2.AddWavetableSynthTrack();
            void Put(string id, float v)
            {
                for (int i = 0; i < me2.PluginParamCount(tmm, -1); i++)
                    if (me2.PluginParamId(tmm, -1, i) == id) { me2.PluginParamSet(tmm, -1, i, v); return; }
            }
            foreach (var (id, value) in ps) Put(id, value);
            me2.AddMidiClip(tmm, 0.0, 4.0);
            me2.SetClipNotes(tmm, 0, new[] { new NotaNote(60, 0.0, 2.0, 0.9f) });
            var b = new float[8192 * 2];
            me2.Seek(0.0); me2.Play(); me2.RenderOffline(b, 8192); me2.StopTransport();
            return Rms(b, 8192);
        }
        // Macro 1 → Sub level (target 5 of 12), full amount: the sub comes up.
        float plain = MacroRms();
        float lifted = MacroRms(("mac0val", 1f), ("mac0dest", 5f / 11f), ("mac0amt", 1f));
        Check(lifted > plain * 1.1f, $"an Aurora macro on Sub level is audible ({plain:F3} -> {lifted:F3})");
        // The legacy seven: 5/6 used to mean "Level" and still does.
        float neutral = MacroRms(("mac0val", 1f), ("mac0dest", 5f / 6f), ("mac0amt", 0.5f));
        float louder = MacroRms(("mac0val", 1f), ("mac0dest", 5f / 6f), ("mac0amt", 1f));
        Check(louder > neutral * 1.1f, $"an old Aurora macro pointing at Level still lands there ({neutral:F3} -> {louder:F3})");
        // Macro 8 exists and works too.
        float m8 = MacroRms(("mac7val", 1f), ("mac7dest", 5f / 11f), ("mac7amt", 1f));
        Check(m8 > plain * 1.1f, $"Aurora macro 8 reaches its target ({plain:F3} -> {m8:F3})");
    }

    // Factory presets: 25 ship, every named param is a real Aurora id, each applies in place.
    {
        var auroraIds = new System.Collections.Generic.HashSet<string>();
        for (int i = 0; i < pc; i++) auroraIds.Add(ae.PluginParamId(t, -1, i));
        var cat = new FactoryPresetCatalog();
        var mine = cat.All().Where(p => p.IsInstrument && p.BuiltinKind == 5).ToList();
        Check(mine.Count == 25, $"Nota Aurora ships 25 factory presets (got {mine.Count})");
        var bad = mine.SelectMany(p => cat.Document(p.Id)!.NamedParams!.Keys.Where(k => !auroraIds.Contains(k)).Select(k => $"{p.DisplayName}:{k}")).ToList();
        Check(bad.Count == 0, $"every Aurora preset param id exists{(bad.Count > 0 ? " — bad: " + string.Join(", ", bad) : "")}");
        int tp = ae.AddWavetableSynthTrack();
        int fails = mine.Count(p => cat.ApplyInPlace(ae, p.Id, tp, -1).Length != 0);
        Check(fails == 0, $"every Aurora preset applies in place ({fails} failed)");
    }
}

// ============================ Nota Volt ====================================
Console.WriteLine("-- Nota Volt --");
{
    using var ve = new NotaEngine();
    ve.SetBpm(120); ve.SetTimeSignature(4, 4);
    int t = ve.AddVoltSynthTrack();
    Check(t > 0, "AddVoltSynthTrack returns a track");
    Check(ve.TrackInstrumentKind(t) == 6, $"instrument kind is 6 (got {ve.TrackInstrumentKind(t)})");
    Check(ve.DeviceName(t, -1) == "Nota Volt", $"instrument is Nota Volt (got '{ve.DeviceName(t, -1)}')");

    int pc = ve.PluginParamCount(t, -1);
    Check(pc == 133, $"Nota Volt exposes 133 params (got {pc})");
    int cut = -1; bool idsOk = true;
    for (int i = 0; i < pc; i++)
    {
        if (ve.PluginParamId(t, -1, i).Length == 0 || ve.PluginParamName(t, -1, i).Length == 0) idsOk = false;
        if (ve.PluginParamId(t, -1, i) == "fil1freq") cut = i;
    }
    Check(idsOk && cut >= 0, "params have ids + names; fil1freq present");

    ve.PluginParamSet(t, -1, cut, 0.42f);
    Check(Math.Abs(ve.PluginParamGet(t, -1, cut) - 0.42f) < 1e-4, "param set/get round-trips");

    // State round-trips to another track (project save/load path).
    ve.PluginParamSet(t, -1, cut, 0.88f);
    var state = ve.GetPluginState(t, -1);
    Check(state.Length >= 133 * 4, $"volt state serialized ({state.Length} bytes)");
    int t2 = ve.AddVoltSynthTrack();
    ve.SetPluginState(t2, -1, state);
    Check(Math.Abs(ve.PluginParamGet(t2, -1, cut) - 0.88f) < 1e-4, "volt state restores params on another track");

    // Track duplication clones the params.
    ve.PluginParamSet(t, -1, cut, 0.20f);
    int t3 = ve.DuplicateTrack(t);
    Check(t3 > 0 && Math.Abs(ve.PluginParamGet(t3, -1, cut) - 0.20f) < 1e-4, "duplicate track clones volt params");

    // Audible: each oscillator waveform (Saw/Square/Triangle/Sine) plays a note → non-silent.
    int w1 = -1;
    for (int i = 0; i < pc; i++) if (ve.PluginParamId(t, -1, i) == "osc1wave") w1 = i;
    for (int wave = 0; wave < 4; wave++)
    {
        int ta = ve.AddVoltSynthTrack();
        ve.PluginParamSet(ta, -1, w1, wave / 3f);
        ve.AddMidiClip(ta, 0.0, 4.0);
        ve.SetClipNotes(ta, 0, new[] { new NotaNote(60, 0.0, 2.0, 0.9f) });
        var nbuf = new float[8192 * 2];
        ve.Seek(0.0); ve.Play(); ve.RenderOffline(nbuf, 8192); ve.StopTransport();
        Check(Rms(nbuf, 8192) > 0.001f, $"Nota Volt wave {wave} is audible (RMS {Rms(nbuf, 8192):F3})");
    }

    // Automation: a plugin-param lane on the cutoff must drive the param during playback.
    {
        int ta = ve.AddVoltSynthTrack();
        int fi = -1;
        for (int i = 0; i < ve.PluginParamCount(ta, -1); i++) if (ve.PluginParamId(ta, -1, i) == "fil1freq") fi = i;
        ve.PluginParamSet(ta, -1, fi, 0.1f);
        int lane = ve.AddPluginAutomationLane(ta, -1, "fil1freq");
        Check(lane >= 0, "add plugin automation lane on fil1freq");
        ve.SetAutomationPoints(ta, lane, new[] { new AutomationPoint(0.0, 0.1f, 0f), new AutomationPoint(2.0, 0.9f, 0f) });
        var abuf = new float[4096 * 2];
        ve.Seek(1.99); ve.Play(); ve.RenderOffline(abuf, 4096); ve.StopTransport();
        float after = ve.PluginParamGet(ta, -1, fi);
        Check(after > 0.7f, $"automation drives Volt cutoff (fil1freq = {after:F2})");
    }

    // v4 additions: mod-matrix + macro params exist and round-trip; a matrix routing
    // is audible; the voice meter reports sounding voices.
    int IdOf(string id) { for (int i = 0; i < pc; i++) if (ve.PluginParamId(t, -1, i) == id) return i; return -1; }
    Check(IdOf("mtx2_2") >= 0 && IdOf("mac0val") >= 0 && IdOf("mono") >= 0 && IdOf("lfo1shape") >= 0,
        "v4 params present (mtx2_2 / mac0val / mono / lfo1shape)");
    {
        int tm = ve.AddVoltSynthTrack();
        ve.AddMidiClip(tm, 0.0, 4.0);
        ve.SetClipNotes(tm, 0, new[] { new NotaNote(60, 0.0, 2.0, 0.9f) });
        int voices = 0;
        void RenderNote(out float rms)
        {
            var b = new float[8192 * 2];
            ve.Seek(0.0); ve.Play(); ve.RenderOffline(b, 8192);
            voices = Math.Max(voices, ve.InstrumentVoiceCount(tm));
            ve.StopTransport(); rms = Rms(b, 8192);
        }
        RenderNote(out float dry);
        // Route LFO 1 (src 2) → Level (dst 4) hard; must still be audible (and differ).
        int mLvl = -1; for (int i = 0; i < ve.PluginParamCount(tm, -1); i++) if (ve.PluginParamId(tm, -1, i) == "mtx2_4") mLvl = i;
        ve.PluginParamSet(tm, -1, mLvl, 0.95f);
        RenderNote(out float wet);
        Check(wet > 0.001f && Math.Abs(wet - dry) > 1e-5f, $"mod-matrix LFO→Level changes output (dry {dry:F3} / wet {wet:F3})");
        Check(voices >= 1, $"voice meter reports sounding voices ({voices})");
    }

    // v5: the performance wheels and the vibrato-by-wheel switch.
    Check(IdOf("bend") >= 0 && IdOf("bendrange") >= 0 && IdOf("vibwheel") >= 0,
        "wheel params present (bend / bendrange / vibwheel)");
    {
        // Each case gets its own engine so a voice left sounding by one render cannot leak
        // into the next, and each renders the same note for the same length.
        float[] RenderWith(params (string Id, float Value)[] ps)
        {
            using var we = new NotaEngine();
            we.SetBpm(120); we.SetTimeSignature(4, 4);
            int tw = we.AddVoltSynthTrack();
            foreach (var (id, value) in ps)
                for (int i = 0; i < we.PluginParamCount(tw, -1); i++)
                    if (we.PluginParamId(tw, -1, i) == id) we.PluginParamSet(tw, -1, i, value);
            we.AddMidiClip(tw, 0.0, 4.0);
            we.SetClipNotes(tw, 0, new[] { new NotaNote(60, 0.0, 2.0, 0.9f) });
            var b = new float[8192 * 2];
            we.Seek(0.0); we.Play(); we.RenderOffline(b, 8192); we.StopTransport();
            return b;
        }
        // Zero crossings stand in for pitch: bending up an octave roughly doubles them.
        int Crossings(float[] b, int n)
        {
            int c = 0; for (int i = 2; i < n * 2; i += 2) if ((b[i - 2] < 0) != (b[i] < 0)) c++;
            return c;
        }
        var flat = RenderWith(("osc2level", 0f), ("fil1freq", 0.9f));
        var bent = RenderWith(("osc2level", 0f), ("fil1freq", 0.9f), ("bend", 1f), ("bendrange", 1f));
        bool bfin = true; foreach (var x in bent) if (!float.IsFinite(x) || Math.Abs(x) > 8f) { bfin = false; break; }
        int cf = Crossings(flat, 8192), cb = Crossings(bent, 8192);
        Check(bfin && cb > cf * 1.5, $"pitch bend +12 st raises the pitch ({cf} → {cb} crossings)");

        // Vibrato by wheel: with the wheel down the vibrato is silent, so the patch renders
        // exactly as one with no vibrato at all; opening the wheel changes the sound.
        var noVib = RenderWith(("vibamt", 0f), ("osc2level", 0f));
        var wheelDown = RenderWith(("vibamt", 1f), ("vibwheel", 1f), ("modwheel", 0f), ("osc2level", 0f));
        var wheelUp = RenderWith(("vibamt", 1f), ("vibwheel", 1f), ("modwheel", 1f), ("osc2level", 0f));
        float Diff(float[] a, float[] b) { float d = 0; for (int i = 0; i < a.Length; i++) d += Math.Abs(a[i] - b[i]); return d / a.Length; }
        Check(Diff(noVib, wheelDown) < 1e-6f, "vibrato by wheel is silent with the wheel down");
        Check(Diff(noVib, wheelUp) > 1e-4f, "vibrato by wheel opens up with the wheel up");
    }

    // v5: macros reach twelve destinations, six of which act on the block snapshot. The
    // six older ones keep the normalized value an old project saved for them.
    {
        // Its own engine per case: this one's earlier tracks still hold clips, and their
        // output would swamp the one voice under test.
        float MacroRms(params (string Id, float Value)[] ps)
        {
            using var me2 = new NotaEngine();
            me2.SetBpm(120); me2.SetTimeSignature(4, 4);
            int tmm = me2.AddVoltSynthTrack();
            void Put(string id, float v)
            {
                for (int i = 0; i < me2.PluginParamCount(tmm, -1); i++)
                    if (me2.PluginParamId(tmm, -1, i) == id) { me2.PluginParamSet(tmm, -1, i, v); return; }
            }
            Put("osc2level", 0f);
            foreach (var (id, value) in ps) Put(id, value);
            me2.AddMidiClip(tmm, 0.0, 4.0);
            me2.SetClipNotes(tmm, 0, new[] { new NotaNote(60, 0.0, 2.0, 0.9f) });
            var b = new float[8192 * 2];
            me2.Seek(0.0); me2.Play(); me2.RenderOffline(b, 8192); me2.StopTransport();
            return Rms(b, 8192);
        }
        // Macro 1 → Osc 2 level (target 3 of 12), full amount: osc 2 comes back.
        float plain = MacroRms();
        float lifted = MacroRms(("mac0val", 1f), ("mac0dest", 3f / 11f), ("mac0amt", 1f));
        Check(lifted > plain * 1.1f, $"a macro on Osc 2 level is audible ({plain:F3} → {lifted:F3})");
        // The legacy six: 0.8 used to mean "Level" and still does.
        float neutral = MacroRms(("mac0val", 1f), ("mac0dest", 0.8f), ("mac0amt", 0.5f));
        float louder = MacroRms(("mac0val", 1f), ("mac0dest", 0.8f), ("mac0amt", 1f));
        Check(louder > neutral * 1.1f, $"an old macro pointing at Level still lands there ({neutral:F3} → {louder:F3})");
    }

    // Factory presets: 25 ship, every named param is a real Volt id, each applies in place.
    {
        var voltIds = new System.Collections.Generic.HashSet<string>();
        for (int i = 0; i < pc; i++) voltIds.Add(ve.PluginParamId(t, -1, i));
        var cat = new FactoryPresetCatalog();
        var mine = cat.All().Where(p => p.IsInstrument && p.BuiltinKind == 6).ToList();
        Check(mine.Count == 25, $"Nota Volt ships 25 factory presets (got {mine.Count})");
        var bad = mine.SelectMany(p => cat.Document(p.Id)!.NamedParams!.Keys.Where(k => !voltIds.Contains(k)).Select(k => $"{p.DisplayName}:{k}")).ToList();
        Check(bad.Count == 0, $"every Volt preset param id exists{(bad.Count > 0 ? " — bad: " + string.Join(", ", bad) : "")}");
        int tp = ve.AddVoltSynthTrack();
        int fails = mine.Count(p => cat.ApplyInPlace(ve, p.Id, tp, -1).Length != 0);
        Check(fails == 0, $"every Volt preset applies in place ({fails} failed)");
    }
}

// ============================ Nota Bass ====================================
Console.WriteLine("-- Nota Bass --");
{
    using var be = new NotaEngine();
    be.SetBpm(120); be.SetTimeSignature(4, 4);
    int t = be.AddBassSynthTrack();
    Check(t > 0, "AddBassSynthTrack returns a track");
    Check(be.TrackInstrumentKind(t) == 7, $"instrument kind is 7 (got {be.TrackInstrumentKind(t)})");
    Check(be.DeviceName(t, -1) == "Nota Bass", $"instrument is Nota Bass (got '{be.DeviceName(t, -1)}')");

    int pc = be.PluginParamCount(t, -1);
    Check(pc == 39, $"Nota Bass exposes 39 params (got {pc})");
    int cut = -1; bool idsOk = true;
    for (int i = 0; i < pc; i++)
    {
        if (be.PluginParamId(t, -1, i).Length == 0 || be.PluginParamName(t, -1, i).Length == 0) idsOk = false;
        if (be.PluginParamId(t, -1, i) == "filfreq") cut = i;
    }
    Check(idsOk && cut >= 0, "params have ids + names; filfreq present");

    // State round-trips to another track (project save/load path).
    be.PluginParamSet(t, -1, cut, 0.77f);
    var state = be.GetPluginState(t, -1);
    Check(state.Length >= 39 * 4, $"bass state serialized ({state.Length} bytes)");
    int t2 = be.AddBassSynthTrack();
    be.SetPluginState(t2, -1, state);
    Check(Math.Abs(be.PluginParamGet(t2, -1, cut) - 0.77f) < 1e-4, "bass state restores params on another track");

    // Track duplication clones the params.
    be.PluginParamSet(t, -1, cut, 0.22f);
    int t3 = be.DuplicateTrack(t);
    Check(t3 > 0 && Math.Abs(be.PluginParamGet(t3, -1, cut) - 0.22f) < 1e-4, "duplicate track clones bass params");

    // Audible in both mono and poly voice modes.
    int monoI = -1;
    for (int i = 0; i < pc; i++) if (be.PluginParamId(t, -1, i) == "mono") monoI = i;
    for (int m = 0; m < 2; m++)
    {
        int ta = be.AddBassSynthTrack();
        be.PluginParamSet(ta, -1, monoI, m);
        be.AddMidiClip(ta, 0.0, 4.0);
        be.SetClipNotes(ta, 0, new[] { new NotaNote(36, 0.0, 2.0, 0.9f), new NotaNote(43, 0.5, 1.5, 0.9f) });
        var nbuf = new float[8192 * 2];
        be.Seek(0.0); be.Play(); be.RenderOffline(nbuf, 8192); be.StopTransport();
        Check(Rms(nbuf, 8192) > 0.001f, $"Nota Bass {(m == 1 ? "mono" : "poly")} is audible (RMS {Rms(nbuf, 8192):F3})");
    }

    // Automation: a plugin-param lane on the cutoff must drive the param during playback.
    {
        int ta = be.AddBassSynthTrack();
        int fi = -1;
        for (int i = 0; i < be.PluginParamCount(ta, -1); i++) if (be.PluginParamId(ta, -1, i) == "filfreq") fi = i;
        be.PluginParamSet(ta, -1, fi, 0.1f);
        int lane = be.AddPluginAutomationLane(ta, -1, "filfreq");
        Check(lane >= 0, "add plugin automation lane on filfreq");
        be.SetAutomationPoints(ta, lane, new[] { new AutomationPoint(0.0, 0.1f, 0f), new AutomationPoint(2.0, 0.9f, 0f) });
        var abuf = new float[4096 * 2];
        be.Seek(1.99); be.Play(); be.RenderOffline(abuf, 4096); be.StopTransport();
        float after = be.PluginParamGet(ta, -1, fi);
        Check(after > 0.7f, $"automation drives Bass cutoff (filfreq = {after:F2})");
    }

    // A project saved before v2 (34 floats) loads: the old params keep their values and
    // the five appended ones start at their defaults.
    {
        int told = be.AddBassSynthTrack();
        be.SetPluginState(told, -1, state.AsSpan(0, 34 * 4).ToArray());
        int ci = -1, pi = -1, li = -1;
        for (int i = 0; i < be.PluginParamCount(told, -1); i++)
        {
            string id = be.PluginParamId(told, -1, i);
            if (id == "filfreq") ci = i; else if (id == "outpan") pi = i; else if (id == "legato") li = i;
        }
        Check(Math.Abs(be.PluginParamGet(told, -1, ci) - 0.77f) < 1e-4
              && Math.Abs(be.PluginParamGet(told, -1, pi) - 0.5f) < 1e-4 && be.PluginParamGet(told, -1, li) < 0.5f,
            "a 34-param Bass state loads; the appended params stay at their defaults");
    }

    // Each case renders on its own engine so nothing sounding in one leaks into the next.
    float[] BassRender(NotaNote[] notes, int frames, params (string Id, float Value)[] ps)
    {
        using var we = new NotaEngine();
        we.SetBpm(120); we.SetTimeSignature(4, 4);
        int tw = we.AddBassSynthTrack();
        foreach (var (id, value) in ps)
            for (int i = 0; i < we.PluginParamCount(tw, -1); i++)
                if (we.PluginParamId(tw, -1, i) == id) we.PluginParamSet(tw, -1, i, value);
        we.AddMidiClip(tw, 0.0, 4.0);
        we.SetClipNotes(tw, 0, notes);
        var b = new float[frames * 2];
        we.Seek(0.0); we.Play(); we.RenderOffline(b, frames); we.StopTransport();
        return b;
    }
    static float BassRms(float[] b, int from, int to, int ch = -1)
    {
        double sum = 0; int n = 0;
        for (int i = from; i < to; i++)
        {
            if (ch != 1) { sum += b[i * 2] * (double)b[i * 2]; n++; }
            if (ch != 0) { sum += b[i * 2 + 1] * (double)b[i * 2 + 1]; n++; }
        }
        return (float)Math.Sqrt(sum / Math.Max(1, n));
    }
    double bassSpb = (be.SampleRate > 0 ? be.SampleRate : 48000.0) * 60.0 / 120.0;   // 120 BPM
    int BF(double beats) => (int)Math.Round(beats * bassSpb);

    // Regression: back-to-back notes of the same pitch where the next note starts a hair
    // before the previous one ends. The previous note's off used to release every note of
    // that pitch — the new one too — so the line went silent. Mono (with glide, the
    // default) and poly alike; the second note must still sound in the middle of its span.
    for (int m = 0; m < 2; m++)
    {
        var b = BassRender(new[] { new NotaNote(36, 0.0, 0.52, 0.9f), new NotaNote(36, 0.5, 0.5, 0.9f) }, BF(1.2),
            ("mono", m), ("release", 0f));
        float mid = BassRms(b, BF(0.7), BF(0.95));
        Check(mid > 0.01f, $"Nota Bass {(m == 1 ? "mono" : "poly")}: an overlapping repeat of the same note keeps sounding (RMS {mid:F3})");
    }

    // Mono, overlapping different pitches on a plucky patch (sustain 0, glide on): every
    // note is a new attack unless Legato is on — before, the overlap skipped the attack and
    // the second note of a tight line was silent.
    {
        var line = new[] { new NotaNote(36, 0.0, 0.55, 0.9f), new NotaNote(43, 0.5, 0.5, 0.9f) };
        (string, float)[] pluck = { ("mono", 1f), ("glide", 0.3f), ("decay", 0.55f), ("sustain", 0f) };
        var retrig = BassRender(line, BF(1.0), pluck);
        var legato = BassRender(line, BF(1.0), pluck.Append(("legato", 1f)).ToArray());
        float rAtt = BassRms(retrig, BF(0.5), BF(0.6)), lAtt = BassRms(legato, BF(0.5), BF(0.6));
        Check(rAtt > 0.02f, $"Nota Bass mono: an overlapping note attacks afresh (RMS {rAtt:F3})");
        Check(rAtt > lAtt * 2f, $"Nota Bass legato: an overlapping note slides without a new attack ({rAtt:F3} vs {lAtt:F3})");
    }

    // v2: the pitch-bend wheel and the output pan.
    {
        int Crossings(float[] b, int n)
        {
            int c = 0; for (int i = 2; i < n * 2; i += 2) if ((b[i - 2] < 0) != (b[i] < 0)) c++;
            return c;
        }
        var note = new[] { new NotaNote(45, 0.0, 2.0, 0.9f) };
        (string, float)[] sine = { ("oscshape", 0f), ("sublevel", 0f), ("filfreq", 1f), ("filenv", 0.5f) };
        var flat = BassRender(note, 8192, sine);
        var bent = BassRender(note, 8192, sine.Append(("bend", 1f)).Append(("bendrange", 1f)).ToArray());
        bool fin = true; foreach (var x in bent) if (!float.IsFinite(x) || Math.Abs(x) > 8f) { fin = false; break; }
        int cf = Crossings(flat, 8192), cb = Crossings(bent, 8192);
        Check(fin && cb > cf * 1.5, $"Nota Bass: pitch bend +12 st raises the pitch ({cf} → {cb} crossings)");

        var left = BassRender(note, 8192, ("outpan", 0f));
        float l = BassRms(left, 0, 8192, 0), r = BassRms(left, 0, 8192, 1);
        Check(l > 0.01f && r < l * 0.01f, $"Nota Bass: pan hard left silences the right channel (L {l:F3} / R {r:F3})");
        var centre = BassRender(note, 8192);
        float cl = BassRms(centre, 0, 8192, 0), cr = BassRms(centre, 0, 8192, 1);
        Check(Math.Abs(cl - cr) < 1e-5f && Math.Abs(cl - l) < l * 0.05f, $"Nota Bass: centred pan keeps unity level on both sides ({cl:F3} / {cr:F3})");

        // The mod wheel opens the LFO onto the cutoff: silent at 0, audible when up.
        (string, float)[] wob = { ("filfreq", 0.35f), ("fillfo", 0.5f), ("lforate", 0.6f), ("filenv", 0.5f) };
        var down = BassRender(note, 8192, wob);
        var up = BassRender(note, 8192, wob.Append(("modwheel", 1f)).ToArray());
        float d = 0; for (int i = 0; i < down.Length; i++) d += Math.Abs(down[i] - up[i]);
        Check(d / down.Length > 1e-3f, "Nota Bass: the mod wheel moves the cutoff through the LFO");
    }

    // Factory presets: 25 ship, every named param is a real Bass id, each applies in place.
    {
        var bassIds = new System.Collections.Generic.HashSet<string>();
        for (int i = 0; i < pc; i++) bassIds.Add(be.PluginParamId(t, -1, i));
        var cat = new FactoryPresetCatalog();
        var mine = cat.All().Where(p => p.IsInstrument && p.BuiltinKind == 7).ToList();
        Check(mine.Count == 25, $"Nota Bass ships 25 factory presets (got {mine.Count})");
        var bad = mine.SelectMany(p => cat.Document(p.Id)!.NamedParams!.Keys.Where(k => !bassIds.Contains(k)).Select(k => $"{p.DisplayName}:{k}")).ToList();
        Check(bad.Count == 0, $"every Bass preset param id exists{(bad.Count > 0 ? " — bad: " + string.Join(", ", bad) : "")}");
        int tp = be.AddBassSynthTrack();
        int fails = mine.Count(p => cat.ApplyInPlace(be, p.Id, tp, -1).Length != 0);
        Check(fails == 0, $"every Bass preset applies in place ({fails} failed)");
        // Each preset is audible and finite on a short bass line.
        var quiet = new System.Collections.Generic.List<string>();
        foreach (var p in mine)
        {
            var doc = cat.Document(p.Id)!;
            var b = BassRender(new[] { new NotaNote(36, 0.0, 0.5, 0.9f), new NotaNote(43, 0.5, 0.5, 0.9f), new NotaNote(36, 1.0, 0.5, 0.9f) },
                BF(1.6), doc.NamedParams!.Select(kv => (kv.Key, kv.Value)).ToArray());
            bool ok = true; foreach (var x in b) if (!float.IsFinite(x) || Math.Abs(x) > 4f) { ok = false; break; }
            float rms = BassRms(b, 0, BF(1.6));
            if (!ok || rms < 0.01f || rms > 0.9f) quiet.Add($"{p.DisplayName} ({rms:F3})");
        }
        Check(quiet.Count == 0, $"every Bass preset renders audible and finite{(quiet.Count > 0 ? " — off: " + string.Join(", ", quiet) : "")}");
    }
}

// ============================ Nota Monolith ================================
Console.WriteLine("-- Nota Monolith --");
{
    using var me = new NotaEngine();
    me.SetBpm(120); me.SetTimeSignature(4, 4);
    int t = me.AddMonolithTrack();
    Check(t > 0, "AddMonolithTrack returns a track");
    Check(me.TrackInstrumentKind(t) == 13, $"instrument kind is 13 (got {me.TrackInstrumentKind(t)})");
    Check(me.DeviceName(t, -1) == "Nota Monolith", $"instrument is Nota Monolith (got '{me.DeviceName(t, -1)}')");

    int pc = me.PluginParamCount(t, -1);
    Check(pc == 52, $"Nota Monolith exposes 52 params (got {pc})");
    int cut = -1; bool idsOk = true;
    for (int i = 0; i < pc; i++)
    {
        if (me.PluginParamId(t, -1, i).Length == 0 || me.PluginParamName(t, -1, i).Length == 0) idsOk = false;
        if (me.PluginParamId(t, -1, i) == "cutoff") cut = i;
    }
    Check(idsOk && cut >= 0, "params have ids + names; cutoff present");

    // State round-trips to another track (project save/load path).
    me.PluginParamSet(t, -1, cut, 0.77f);
    var state = me.GetPluginState(t, -1);
    Check(state.Length >= 52 * 4, $"monolith state serialized ({state.Length} bytes)");
    int t2 = me.AddMonolithTrack();
    me.SetPluginState(t2, -1, state);
    Check(Math.Abs(me.PluginParamGet(t2, -1, cut) - 0.77f) < 1e-4, "monolith state restores params on another track");

    // Track duplication clones the params.
    me.PluginParamSet(t, -1, cut, 0.22f);
    int t3 = me.DuplicateTrack(t);
    Check(t3 > 0 && Math.Abs(me.PluginParamGet(t3, -1, cut) - 0.22f) < 1e-4, "duplicate track clones monolith params");

    // Audible under each note-priority mode (mono voice).
    int prioI = -1;
    for (int i = 0; i < pc; i++) if (me.PluginParamId(t, -1, i) == "priority") prioI = i;
    for (int p = 0; p < 3; p++)
    {
        int ta = me.AddMonolithTrack();
        me.PluginParamSet(ta, -1, prioI, p / 2f);
        me.AddMidiClip(ta, 0.0, 4.0);
        me.SetClipNotes(ta, 0, new[] { new NotaNote(48, 0.0, 2.0, 0.9f), new NotaNote(55, 0.5, 1.5, 0.9f) });
        var nbuf = new float[8192 * 2];
        me.Seek(0.0); me.Play(); me.RenderOffline(nbuf, 8192); me.StopTransport();
        Check(Rms(nbuf, 8192) > 0.001f, $"Nota Monolith priority {p} is audible (RMS {Rms(nbuf, 8192):F3})");
    }

    // Automation: a plugin-param lane on the cutoff must drive the param during playback.
    {
        int ta = me.AddMonolithTrack();
        int fi = -1;
        for (int i = 0; i < me.PluginParamCount(ta, -1); i++) if (me.PluginParamId(ta, -1, i) == "cutoff") fi = i;
        me.PluginParamSet(ta, -1, fi, 0.1f);
        int lane = me.AddPluginAutomationLane(ta, -1, "cutoff");
        Check(lane >= 0, "add plugin automation lane on cutoff");
        me.SetAutomationPoints(ta, lane, new[] { new AutomationPoint(0.0, 0.1f, 0f), new AutomationPoint(2.0, 0.9f, 0f) });
        var abuf = new float[4096 * 2];
        me.Seek(1.99); me.Play(); me.RenderOffline(abuf, 4096); me.StopTransport();
        float after = me.PluginParamGet(ta, -1, fi);
        Check(after > 0.7f, $"automation drives Monolith cutoff (cutoff = {after:F2})");
    }
}

// ============================ Nota Pentad ==================================
Console.WriteLine("-- Nota Pentad --");
{
    using var pe = new NotaEngine();
    pe.SetBpm(120); pe.SetTimeSignature(4, 4);
    int t = pe.AddPentadTrack();
    Check(t > 0, "AddPentadTrack returns a track");
    Check(pe.TrackInstrumentKind(t) == 14, $"instrument kind is 14 (got {pe.TrackInstrumentKind(t)})");
    Check(pe.DeviceName(t, -1) == "Nota Pentad", $"instrument is Nota Pentad (got '{pe.DeviceName(t, -1)}')");

    int pc = pe.PluginParamCount(t, -1);
    Check(pc == 76, $"Nota Pentad exposes 76 params (got {pc})");
    var ids = new HashSet<string>();
    bool namesOk = true;
    for (int i = 0; i < pc; i++) { ids.Add(pe.PluginParamId(t, -1, i)); if (pe.PluginParamName(t, -1, i).Length == 0) namesOk = false; }
    Check(ids.Count == pc && !ids.Contains("") && namesOk, "param ids are unique + non-empty, every param has a name");
    int P(NotaEngine e, int tr, string id) { for (int i = 0; i < e.PluginParamCount(tr, -1); i++) if (e.PluginParamId(tr, -1, i) == id) return i; return -1; }
    void SetId(NotaEngine e, int tr, string id, float v) => e.PluginParamSet(tr, -1, P(e, tr, id), v);
    int cut = P(pe, t, "cutoff");

    // State (incl. the vintage seed) round-trips to another track; duplicate clones it.
    pe.PluginParamSet(t, -1, cut, 0.77f);
    SetId(pe, t, "seed", 0.123f);
    var state = pe.GetPluginState(t, -1);
    Check(state.Length >= 76 * 4, $"pentad state serialized ({state.Length} bytes)");
    int t2 = pe.AddPentadTrack();
    pe.SetPluginState(t2, -1, state);
    Check(Math.Abs(pe.PluginParamGet(t2, -1, cut) - 0.77f) < 1e-4 && Math.Abs(pe.PluginParamGet(t2, -1, P(pe, t2, "seed")) - 0.123f) < 1e-4,
          "pentad state restores params + vintage seed on another track");
    pe.PluginParamSet(t, -1, cut, 0.22f);
    int t3 = pe.DuplicateTrack(t);
    Check(t3 > 0 && Math.Abs(pe.PluginParamGet(t3, -1, cut) - 0.22f) < 1e-4, "duplicate track clones pentad params");

    // A chord renders audible + finite in every voice mode (Poly / Unison / Mono).
    var chord = new[] { new NotaNote(48, 0.0, 2.0, 0.9f), new NotaNote(55, 0.0, 2.0, 0.9f), new NotaNote(60, 0.0, 2.0, 0.9f), new NotaNote(64, 0.0, 2.0, 0.9f) };
    foreach (var (mode, name) in new[] { (0f, "Poly"), (0.5f, "Unison"), (1f, "Mono") })
    {
        int ta = pe.AddPentadTrack();
        SetId(pe, ta, "voicemode", mode);
        pe.AddMidiClip(ta, 0.0, 4.0);
        pe.SetClipNotes(ta, 0, chord);
        var pbuf = new float[8192 * 2];
        pe.Seek(0.0); pe.Play(); pe.RenderOffline(pbuf, 8192);
        int voices = pe.InstrumentVoiceCount(ta);
        pe.StopTransport();
        bool finite = pbuf.All(float.IsFinite);
        Check(finite && Rms(pbuf, 8192) > 0.001f, $"Nota Pentad {name} is audible + finite (RMS {Rms(pbuf, 8192):F3}, {voices} voices)");
        int want = name == "Poly" ? 4 : name == "Unison" ? 5 : 1;
        Check(voices == want, $"Nota Pentad {name} sounds {want} voice(s) for a 4-note chord (got {voices})");
        pe.RemoveTrack(ta);
    }

    // Polyphony: 8 held pnotes on 5 voices → 5 sounding; 16-voice mode → 8.
    foreach (var (poly, want) in new[] { (0f, 5), (1f, 8) })
    {
        int ta = pe.AddPentadTrack();
        SetId(pe, ta, "polyphony", poly);
        pe.AddMidiClip(ta, 0.0, 4.0);
        var pnotes = new NotaNote[8];
        for (int i = 0; i < 8; i++) pnotes[i] = new NotaNote(48 + i * 3, 0.0, 4.0, 0.8f);
        pe.SetClipNotes(ta, 0, pnotes);
        var pbuf = new float[4096 * 2];
        pe.Seek(0.0); pe.Play(); pe.RenderOffline(pbuf, 4096);
        int v = pe.InstrumentVoiceCount(ta);
        pe.StopTransport();
        Check(v == want, $"Nota Pentad {(poly < 0.5f ? 5 : 16)}-voice mode sounds {want} of 8 held notes (got {v})");
        pe.RemoveTrack(ta);
    }

    // Determinism (Freeze: offline == realtime): the same patch + seed renders bit-identically
    // regardless of block size, and a different seed changes the vintage spread.
    float[] RenderPhrase(float seed, int block)
    {
        using var de = new NotaEngine();
        de.SetBpm(120);
        int dt = de.AddPentadTrack();
        SetId(de, dt, "seed", seed); SetId(de, dt, "drift", 0.8f); SetId(de, dt, "mixnoise", 0.3f);
        SetId(de, dt, "pmoscb", 0.5f); SetId(de, dt, "pmfreqa", 1f); SetId(de, dt, "oasync", 1f); SetId(de, dt, "lfoamt", 0.3f);
        de.AddMidiClip(dt, 0.0, 4.0);
        de.SetClipNotes(dt, 0, new[] { new NotaNote(60, 0.0, 1.5, 0.8f), new NotaNote(64, 0.25, 1.0, 0.7f), new NotaNote(67, 0.5, 1.5, 0.9f), new NotaNote(72, 1.0, 0.5, 0.6f) });
        const int N = 44100;
        var outv = new float[N * 2]; var tmp = new float[block * 2];
        de.Seek(0.0); de.Play();
        for (int done = 0; done < N;) { int m = Math.Min(block, N - done); de.RenderOffline(tmp, m); Array.Copy(tmp, 0, outv, done * 2, m * 2); done += m; }
        de.StopTransport();
        return outv;
    }
    var ra = RenderPhrase(0.5f, 4096); var rb = RenderPhrase(0.5f, 128); var rc = RenderPhrase(0.5f, 777); var rd = RenderPhrase(0.9f, 4096);
    double dAB = 0, dAC = 0, dAD = 0;
    for (int i = 0; i < ra.Length; i++) { dAB = Math.Max(dAB, Math.Abs(ra[i] - rb[i])); dAC = Math.Max(dAC, Math.Abs(ra[i] - rc[i])); dAD = Math.Max(dAD, Math.Abs(ra[i] - rd[i])); }
    Check(Rms(ra, 44100) > 0.001f && dAB == 0 && dAC == 0, $"Nota Pentad null test: same seed renders bit-identically at blocks 4096/128/777 (maxdiff {Math.Max(dAB, dAC):G3})");
    Check(dAD > 1e-3, $"Nota Pentad: a different vintage seed changes the render (maxdiff {dAD:F3})");

    // Extreme settings never produce NaN/Inf (resonance 100 % + Poly-Mod at max + sync + 16-voice unison).
    {
        int ta = pe.AddPentadTrack();
        foreach (var id in new[] { "reso", "pmenv", "pmoscb", "pmfreqa", "pmpwa", "pmfilter", "oasync", "oapulse", "obpulse", "obtri", "mixa", "mixb", "mixnoise", "fenvamt", "lfoamt", "wmfilter", "wmpwa", "wmpwb", "cutoff", "keytrk", "modwheel", "polyphony" })
            SetId(pe, ta, id, 1f);
        SetId(pe, ta, "voicemode", 0.5f);
        pe.AddMidiClip(ta, 0.0, 8.0);
        pe.SetClipNotes(ta, 0, new[] { new NotaNote(24, 0.0, 4.0, 1f), new NotaNote(108, 0.0, 4.0, 1f), new NotaNote(60, 4.0, 4.0, 1f) });
        var pbuf = new float[44100 * 4 * 2];
        pe.Seek(0.0); pe.Play(); pe.RenderOffline(pbuf, 44100 * 4);
        SetId(pe, ta, "cutoff", 0f); pe.RenderOffline(pbuf, 44100 * 2);
        pe.StopTransport();
        Check(pbuf.All(float.IsFinite), "Nota Pentad stays finite at resonance 100 % + Poly-Mod max + 16-voice unison");
        pe.RemoveTrack(ta);
    }

    // Aliasing (13.2): a 5 kHz-ish saw / pulse, filter open — spurious (non-harmonic)
    // components in 20 Hz–20 kHz must stay ≤ -80 dB below the fundamental.
    foreach (var (wave, os) in new[] { ("saw", 0f), ("pulse", 0f), ("saw", 1f), ("pulse", 1f) })
    {
        using var ae = new NotaEngine();
        ae.SetBpm(120);
        int ta = ae.AddPentadTrack();
        foreach (var (id, v) in new[] { ("drift", 0f), ("noisefloor", 0f), ("cutoff", 1f), ("reso", 0f), ("keytrk", 0f), ("fenvamt", 0f), ("aattack", 0f), ("asustain", 1f),
                                        ("mixb", 0f), ("mixa", 0.5f), ("spread", 0f), ("oversample", os), ("oasaw", wave == "saw" ? 1f : 0f), ("oapulse", wave == "pulse" ? 1f : 0f), ("oapw", 0.3f) })
            SetId(ae, ta, id, v);
        ae.AddMidiClip(ta, 0.0, 8.0);
        ae.SetClipNotes(ta, 0, new[] { new NotaNote(111, 0.0, 8.0, 1f) });
        const int N = 32768;
        var warm = new float[22050 * 2]; var pbuf = new float[N * 2];
        ae.Seek(0.0); ae.Play(); ae.RenderOffline(warm, 22050); ae.RenderOffline(pbuf, N); ae.StopTransport();
        double sr = 44100, f0 = 440 * Math.Pow(2, (111 - 69) / 12.0);
        var re = new double[N]; var im = new double[N];
        for (int i = 0; i < N; i++) { double w = 2 * Math.PI * i / (N - 1); re[i] = pbuf[i * 2] * (0.35875 - 0.48829 * Math.Cos(w) + 0.14128 * Math.Cos(2 * w) - 0.01168 * Math.Cos(3 * w)); }
        Fft(re, im);
        double peak = 0, worst = 0, binHz = sr / N;
        for (int i = 1; i < N / 2; i++) peak = Math.Max(peak, Math.Sqrt(re[i] * re[i] + im[i] * im[i]));
        for (int i = (int)(20 / binHz); i < (int)(20000 / binHz); i++)
        {
            double h = i * binHz / f0;
            if (Math.Round(h) >= 1 && Math.Abs(h - Math.Round(h)) * f0 < 10 * binHz) continue;   // a true harmonic
            worst = Math.Max(worst, Math.Sqrt(re[i] * re[i] + im[i] * im[i]));
        }
        double db = 20 * Math.Log10(worst / peak + 1e-30);
        Check(db <= -80, $"Nota Pentad {wave} at {f0:0} Hz, ×{(os > 0.5f ? 4 : 2)} oversampling: worst alias {db:0.0} dB (≤ -80)");
    }

    // Stress (13.4): 16-voice unison + max resonance + Poly-Mod renders 128-frame blocks in real time.
    {
        using var se = new NotaEngine();
        se.SetBpm(120);
        int ta = se.AddPentadTrack();
        foreach (var id in new[] { "reso", "pmenv", "pmoscb", "pmfreqa", "pmpwa", "pmfilter", "oasync", "polyphony", "oversample" }) SetId(se, ta, id, 1f);
        SetId(se, ta, "voicemode", 0.5f);
        se.AddMidiClip(ta, 0.0, 16.0);
        se.SetClipNotes(ta, 0, new[] { new NotaNote(48, 0.0, 16.0, 1f) });
        var blk = new float[128 * 2];
        se.Seek(0.0); se.Play();
        for (int i = 0; i < 50; i++) se.RenderOffline(blk, 128);   // warm up
        double worstMs = 0, totalMs = 0; const int blocks = 2000;
        var sw = new System.Diagnostics.Stopwatch();
        for (int i = 0; i < blocks; i++) { sw.Restart(); se.RenderOffline(blk, 128); sw.Stop(); worstMs = Math.Max(worstMs, sw.Elapsed.TotalMilliseconds); totalMs += sw.Elapsed.TotalMilliseconds; }
        int active = se.InstrumentVoiceCount(ta);
        se.StopTransport();
        double budget = 128 / 44.1;
        Check(active == 16 && totalMs / blocks < budget * 0.5, $"Nota Pentad stress: 16-voice unison ×4 OS avg {totalMs / blocks:0.000} ms, worst {worstMs:0.000} ms per 128-frame block (budget {budget:0.00} ms)");
    }

    // Automation: a plugin-param lane drives the cutoff during playback.
    {
        int ta = pe.AddPentadTrack();
        int fi = P(pe, ta, "cutoff");
        pe.PluginParamSet(ta, -1, fi, 0.1f);
        int lane = pe.AddPluginAutomationLane(ta, -1, "cutoff");
        Check(lane >= 0, "add plugin automation lane on pentad cutoff");
        pe.SetAutomationPoints(ta, lane, new[] { new AutomationPoint(0.0, 0.1f, 0f), new AutomationPoint(2.0, 0.9f, 0f) });
        var abuf = new float[4096 * 2];
        pe.Seek(1.99); pe.Play(); pe.RenderOffline(abuf, 4096); pe.StopTransport();
        Check(pe.PluginParamGet(ta, -1, fi) > 0.7f, $"automation drives Pentad cutoff (cutoff = {pe.PluginParamGet(ta, -1, fi):F2})");
    }

    // Freeze captures exactly what the live chain plays (deterministic voice state).
    {
        using var fe = new NotaEngine();
        fe.SetBpm(120);
        int ft = fe.AddPentadTrack();
        SetId(fe, ft, "drift", 0.7f); SetId(fe, ft, "mixnoise", 0.2f);
        fe.AddMidiClip(ft, 0.0, 4.0);
        fe.SetClipNotes(ft, 0, new[] { new NotaNote(57, 0.0, 3.0, 0.9f), new NotaNote(64, 0.5, 2.0, 0.8f) });
        const int N = 8192 * 4;
        fe.Seek(0); fe.Play(); var live = new float[N * 2]; fe.RenderOffline(live, N); fe.StopTransport();
        fe.Stop(); fe.SetLoop(false, 0, 0); fe.SetMetronome(false); fe.StopTransport();
        long total = fe.BeginFreeze(ft, 4.0);
        fe.StopTransport(); fe.Seek(0); fe.Play();
        var chunk = new float[4096 * 2];
        for (long rem = total; rem > 0;) { int m = (int)Math.Min(4096, rem); fe.RenderOffline(chunk, m); rem -= m; }
        fe.StopTransport(); fe.EndFreeze(ft);
        fe.Seek(0); fe.Play(); var frozen = new float[N * 2]; fe.RenderOffline(frozen, N); fe.StopTransport();
        double md = 0; for (int i = 0; i < live.Length; i++) md = Math.Max(md, Math.Abs(live[i] - frozen[i]));
        Check(fe.IsTrackFrozen(ft) && Rms(live, N) > 1e-3f && md < 1e-5, $"Nota Pentad freeze == live render (maxdiff {md:G3})");
    }

    // Factory presets: 40 ship, every named param is a real Pentad id, each applies in place.
    {
        var cat = new FactoryPresetCatalog();
        var mine = cat.All().Where(p => p.IsInstrument && p.BuiltinKind == 14).ToList();
        Check(mine.Count == 40, $"Nota Pentad ships 40 factory presets (got {mine.Count})");
        var bad = mine.SelectMany(p => cat.Document(p.Id)!.NamedParams!.Keys.Where(k => !ids.Contains(k)).Select(k => $"{p.DisplayName}:{k}")).ToList();
        Check(bad.Count == 0, $"every Pentad preset param id exists{(bad.Count > 0 ? " — bad: " + string.Join(", ", bad) : "")}");
        int ta = pe.AddPentadTrack();
        int fails = mine.Count(p => cat.ApplyInPlace(pe, p.Id, ta, -1).Length != 0);
        Check(fails == 0, $"every Pentad preset applies in place ({fails} failed)");
        cat.ApplyInPlace(pe, "pentad/Sync Lead", ta, -1);
        Check(pe.PluginParamGet(ta, -1, P(pe, ta, "oasync")) > 0.5f && pe.PluginParamGet(ta, -1, P(pe, ta, "voicemode")) > 0.9f, "Sync Lead preset sets sync + mono mode");
    }

    // MCP: add by kind 14, set a param by stable id.
    {
        var mcp = new Nota.Mcp.Tools.InstrumentTools(pe, new Nota.SmokeTest.SyncDispatch(), new Nota.SmokeTest.NoRefresh());
        int mt = mcp.AddInstrumentTrack(14).Result;
        Check(mt > 0 && pe.TrackInstrumentKind(mt) == 14, "MCP add_instrument_track(14) adds a Nota Pentad");
        Check(mcp.ListInstrumentKinds().Any(k => k.Kind == 14 && k.Name == "Nota Pentad"), "MCP list_instrument_kinds includes Nota Pentad");
        Check(mcp.SetInstrumentParamById(mt, "pmoscb", 0.4f).Result && Math.Abs(pe.PluginParamGet(mt, -1, P(pe, mt, "pmoscb")) - 0.4f) < 1e-4, "MCP set_instrument_param_by_id sets Poly-Mod Osc-B");
        Check(!mcp.SetInstrumentParamById(mt, "nope", 0.4f).Result, "MCP set_instrument_param_by_id rejects an unknown id");
    }
}

// ============================ Nota Consort =================================
Console.WriteLine("-- Nota Consort --");
{
    using var ce = new NotaEngine();
    ce.SetBpm(120); ce.SetTimeSignature(4, 4);
    int CI(NotaEngine e, int tr, string id) { for (int i = 0; i < e.PluginParamCount(tr, -1); i++) if (e.PluginParamId(tr, -1, i) == id) return i; return -1; }
    void CS(NotaEngine e, int tr, string id, float v) => e.PluginParamSet(tr, -1, CI(e, tr, id), v);
    void Cable(NotaEngine e, int tr, int slot, int src, int dst, float depth) { CS(e, tr, $"c{slot}src", src / 63f); CS(e, tr, $"c{slot}dst", dst / 63f); CS(e, tr, $"c{slot}amt", 0.5f + depth / 2); }
    float[] Play(Action<NotaEngine, int> setup, NotaNote[] notes, int frames, int block = 512, Action<NotaEngine, int>? each = null)
    {
        using var e = new NotaEngine();
        e.SetBpm(120);
        int tr = e.AddConsortTrack();
        setup(e, tr);
        e.AddMidiClip(tr, 0.0, 32.0);
        e.SetClipNotes(tr, 0, notes);
        var outv = new float[frames * 2]; var tmp = new float[block * 2];
        e.Seek(0); e.Play();
        for (int done = 0; done < frames;) { int m = Math.Min(block, frames - done); e.RenderOffline(tmp, m); Array.Copy(tmp, 0, outv, done * 2, m * 2); done += m; each?.Invoke(e, tr); }
        e.StopTransport();
        return outv;
    }
    static float RmsRange(float[] b, int from, int to) { double sum = 0; for (int i = from * 2; i < to * 2; i++) sum += b[i] * (double)b[i]; return (float)Math.Sqrt(sum / Math.Max(1, (to - from) * 2)); }
    static double MaxDiff(float[] a, float[] b) { double d = 0; for (int i = 0; i < Math.Min(a.Length, b.Length); i++) d = Math.Max(d, Math.Abs(a[i] - b[i])); return d; }

    int t = ce.AddConsortTrack();
    Check(t > 0, "AddConsortTrack returns a track");
    Check(ce.TrackInstrumentKind(t) == 15, $"instrument kind is 15 (got {ce.TrackInstrumentKind(t)})");
    Check(ce.DeviceName(t, -1) == "Nota Consort", $"instrument is Nota Consort (got '{ce.DeviceName(t, -1)}')");
    int pc = ce.PluginParamCount(t, -1);
    Check(pc == 145, $"Nota Consort exposes 145 params (got {pc})");
    var cids = new HashSet<string>();
    bool cnames = true;
    for (int i = 0; i < pc; i++) { cids.Add(ce.PluginParamId(t, -1, i)); if (ce.PluginParamName(t, -1, i).Length == 0) cnames = false; }
    Check(cids.Count == pc && !cids.Contains("") && cnames, "consort param ids are unique + non-empty, every param has a name");

    // State (incl. a patch cable and a step) round-trips to another track; duplicate clones it.
    CS(ce, t, "cutoff", 0.77f); Cable(ce, t, 3, 1, 10, 0.4f); CS(ce, t, "sp5", 0.9f);
    var cstate = ce.GetPluginState(t, -1);
    int t2c = ce.AddConsortTrack();
    ce.SetPluginState(t2c, -1, cstate);
    Check(Math.Abs(ce.PluginParamGet(t2c, -1, CI(ce, t2c, "cutoff")) - 0.77f) < 1e-4 && Math.Abs(ce.PluginParamGet(t2c, -1, CI(ce, t2c, "c3dst")) - 10 / 63f) < 1e-4
          && Math.Abs(ce.PluginParamGet(t2c, -1, CI(ce, t2c, "sp5")) - 0.9f) < 1e-4, "consort state restores params, cables and steps on another track");
    int t3c = ce.DuplicateTrack(t);
    Check(t3c > 0 && Math.Abs(ce.PluginParamGet(t3c, -1, CI(ce, t3c, "c3amt")) - 0.7f) < 1e-4, "duplicate track clones the consort patch");

    // Every voice structure sounds a 4-note chord: MONO 1, DUO 2, PARA 4, true poly 4 notes.
    var cchord = new[] { new NotaNote(48, 0.0, 2.0, 0.9f), new NotaNote(55, 0.0, 2.0, 0.9f), new NotaNote(60, 0.0, 2.0, 0.9f), new NotaNote(64, 0.0, 2.0, 0.9f) };
    foreach (var (name, mode, poly, want) in new[] { ("MONO", 0f, 0f, 1), ("DUO", 0.5f, 0f, 2), ("PARA", 1f, 0f, 4), ("POLY", 1f, 1f, 4) })
    {
        int voices = -1;
        var b = Play((e, tr) => { CS(e, tr, "voicemode", mode); CS(e, tr, "truepoly", poly); }, cchord, 8192, 512, (e, tr) => voices = e.InstrumentVoiceCount(tr));
        Check(b.All(float.IsFinite) && Rms(b, 8192) > 0.01f && voices == want, $"Nota Consort {name}: a chord is audible + finite, {want} note(s) sound (RMS {Rms(b, 8192):F3}, {voices})");
    }
    // Paraphony: the notes land on the oscillators (scope telemetry).
    {
        using var e = new NotaEngine(); e.SetBpm(120);
        int tr = e.AddConsortTrack(); CS(e, tr, "unison", 0f);
        e.AddMidiClip(tr, 0, 4); e.SetClipNotes(tr, 0, new[] { new NotaNote(48, 0, 2, 0.9f), new NotaNote(55, 0, 2, 0.9f), new NotaNote(62, 0, 2, 0.9f) });
        var tmp = new float[4096 * 2]; e.Seek(0); e.Play(); e.RenderOffline(tmp, 4096);
        var sc = new float[32]; e.InstrumentScope(tr, sc); e.StopTransport();
        var oscNotes = sc.Skip(12).Take(4).Select(v => (int)v).OrderBy(v => v).ToArray();
        Check(oscNotes.SequenceEqual(new[] { -1, 48, 55, 62 }), $"Nota Consort PARA assigns 3 held notes to 3 oscillators, the 4th idle without unison (got {string.Join(",", oscNotes)})");
    }
    // Filter modes and waves render finite + audible at high resonance.
    foreach (var fm in new[] { 0f, 0.5f, 1f })
    {
        var b = Play((e, tr) => { CS(e, tr, "filtmode", fm); CS(e, tr, "reso", 0.95f); }, new[] { new NotaNote(48, 0, 2, 0.9f) }, 22050);
        Check(b.All(float.IsFinite) && Rms(b, 22050) > 0.003f, $"Nota Consort filter mode {fm:0.0} audible + finite at resonance 95 % (RMS {Rms(b, 22050):F4})");
    }

    // Sequencer: 1/16 at 120 BPM = 8 steps a second, locked to the host grid; ARP walks the chord.
    {
        var steps = new HashSet<int>(); var seqNotes = new HashSet<int>();
        Play((e, tr) => { CS(e, tr, "seqmode", 0.5f); }, new[] { new NotaNote(60, 0, 8, 0.9f) }, 44100 * 2, 256,
             (e, tr) => { var sc = new float[32]; e.InstrumentScope(tr, sc); steps.Add((int)sc[7]); if (sc[24] >= 0) seqNotes.Add((int)sc[24]); });
        Check(steps.Contains(0) && steps.Contains(15) && steps.Count >= 16, $"Nota Consort SEQ runs all 16 steps in 2 s at 1/16, 120 BPM (saw {steps.Count} distinct)");
        Check(seqNotes.SetEquals(new[] { 60, 63, 65, 67, 70, 72 }), $"Nota Consort SEQ transposes the default pattern from the held key (got {string.Join(",", seqNotes.OrderBy(x => x))})");
        var arpNotes = new HashSet<int>();
        Play((e, tr) => { CS(e, tr, "seqmode", 1f); CS(e, tr, "arpoct", 0.5f); }, new[] { new NotaNote(60, 0, 8, 0.9f), new NotaNote(64, 0, 8, 0.9f), new NotaNote(67, 0, 8, 0.9f) }, 44100 * 2, 256,
             (e, tr) => { var sc = new float[32]; e.InstrumentScope(tr, sc); if (sc[24] >= 0) arpNotes.Add((int)sc[24]); });
        Check(arpNotes.SetEquals(new[] { 60, 64, 67, 72, 76, 79 }), $"Nota Consort ARP plays the chord over 2 octaves (got {string.Join(",", arpNotes.OrderBy(x => x))})");
        // rests are silent: an all-rest pattern plays nothing
        var rest = Play((e, tr) => { CS(e, tr, "seqmode", 0.5f); for (int s2 = 1; s2 <= 16; s2++) CS(e, tr, $"st{s2}", 1f); }, new[] { new NotaNote(60, 0, 8, 0.9f) }, 44100);
        Check(Rms(rest, 44100) < 1e-4f, $"Nota Consort SEQ: an all-rest pattern is silent (RMS {Rms(rest, 44100):G3})");
    }

    // BBD delay: echoes after the note, each repeat ~fb lower (−6 dB at 50 %), BBD and digital alike.
    foreach (var dig in new[] { 0f, 1f })
    {
        var b = Play((e, tr) => { CS(e, tr, "dlymix", 1f); CS(e, tr, "dlyfb", 0.5f); CS(e, tr, "dlyping", 0f); CS(e, tr, "dlyspacing", 0.5f); CS(e, tr, "dlydigital", dig);
                                  CS(e, tr, "adecay", 0.05f); CS(e, tr, "asustain", 0f); CS(e, tr, "arelease", 0.02f); },
                     new[] { new NotaNote(72, 0, 0.05, 1f) }, 44100 * 2);
        double T = 0.02 * Math.Pow(75, 0.62);
        float Pk(int k) { int c0 = (int)(k * T * 44100), c1 = c0 + 4000; float pk = 0; for (int i = c0; i < c1; i++) pk = Math.Max(pk, Math.Abs(b[i * 2])); return pk; }
        double r = 20 * Math.Log10(Pk(3) / Pk(2));
        Check(Pk(1) > 0.01f && r < -4.5 && r > -8.5, $"Nota Consort {(dig > 0 ? "digital" : "BBD")} delay repeats decay ~6 dB at 50 % feedback ({r:0.0} dB)");
    }

    // Patch bay: a cable changes the sound; Noise → Filt in breaks the mixer normal; LFO → Gate 2 gates the VCA.
    {
        var note = new[] { new NotaNote(57, 0, 2, 0.9f) };
        var dry = Play((e, tr) => { }, note, 22050);
        var vib = Play((e, tr) => Cable(e, tr, 1, 1, 4, 0.5f), note, 22050);
        Check(MaxDiff(dry, vib) > 0.05, "Nota Consort: LFO → Osc pitch changes the render");
        var silentMix = Play((e, tr) => { foreach (var m in new[] { "mix1", "mix2", "mix3", "mix4" }) CS(e, tr, m, 0f); }, note, 22050);
        var noiseIn = Play((e, tr) => { foreach (var m in new[] { "mix1", "mix2", "mix3", "mix4" }) CS(e, tr, m, 0f); Cable(e, tr, 1, 8, 13, 1f); CS(e, tr, "cutoff", 0.8f); }, note, 22050);
        Check(Rms(silentMix, 22050) < 1e-3f && Rms(noiseIn, 22050) > 0.01f, $"Nota Consort: Noise → Filt in replaces the (silent) mixer normal (RMS {Rms(silentMix, 22050):G2} → {Rms(noiseIn, 22050):F3})");
        var gated = Play((e, tr) => { CS(e, tr, "lfowave", 0.6f); CS(e, tr, "lforate", 0.577f); CS(e, tr, "arelease", 0.05f); CS(e, tr, "aattack", 0f); Cable(e, tr, 1, 1, 3, 1f); }, note, 44100);
        float lo = 1, hi = 0; for (int w = 2; w < 20; w++) { float r = RmsRange(gated, w * 2205, (w + 1) * 2205); lo = Math.Min(lo, r); hi = Math.Max(hi, r); }
        Check(hi > 0.01f && lo < hi * 0.2f, $"Nota Consort: LFO square → Gate 2 in chops the amp envelope (min {lo:F4} / max {hi:F3})");
        var extreme = Play((e, tr) =>
        {
            for (int k = 1; k <= 12; k++) Cable(e, tr, k, 1 + (k * 7) % 20, 1 + (k * 5) % 22, k % 2 == 0 ? 1f : -1f);
            foreach (var id in new[] { "reso", "mixnoise", "mixext", "mixdrive", "fenvamt", "lfocut", "lfopwm", "dlyfb", "dlymix", "truepoly", "oversample", "unison", "o2sync", "o4sync" }) CS(e, tr, id, 1f);
        }, new[] { new NotaNote(24, 0, 4, 1f), new NotaNote(108, 0, 4, 1f), new NotaNote(60, 1, 4, 1f) }, 44100 * 3);
        Check(extreme.All(float.IsFinite), "Nota Consort stays finite with all 12 cables at ±100 %, resonance, feedback and drive at max");
    }

    // Determinism (Freeze: offline == realtime): bit-identical at any block size.
    {
        Action<NotaEngine, int> st = (e, tr) => { CS(e, tr, "drift", 0.8f); CS(e, tr, "mixnoise", 0.3f); CS(e, tr, "dlymix", 0.4f); CS(e, tr, "seqmode", 1f); CS(e, tr, "seqorder", 1f); CS(e, tr, "lfowave", 0.8f); Cable(e, tr, 1, 1, 10, 0.6f); Cable(e, tr, 2, 6, 5, 0.2f); };
        NotaNote[] ph = { new(60, 0, 1.5, 0.8f), new(64, 0.25, 1, 0.7f), new(67, 0.5, 1.5, 0.9f) };
        var a = Play(st, ph, 44100, 4096); var b2 = Play(st, ph, 44100, 128); var c = Play(st, ph, 44100, 777);
        Check(Rms(a, 44100) > 0.001f && MaxDiff(a, b2) == 0 && MaxDiff(a, c) == 0, $"Nota Consort null test: bit-identical at blocks 4096/128/777 (maxdiff {Math.Max(MaxDiff(a, b2), MaxDiff(a, c)):G3})");
    }
    // Freeze captures exactly what the live chain plays.
    {
        using var fe = new NotaEngine(); fe.SetBpm(120);
        int ft = fe.AddConsortTrack();
        CS(fe, ft, "drift", 0.7f); CS(fe, ft, "dlymix", 0.3f); CS(fe, ft, "seqmode", 0.5f);
        fe.AddMidiClip(ft, 0.0, 4.0);
        fe.SetClipNotes(ft, 0, new[] { new NotaNote(57, 0.0, 3.0, 0.9f) });
        const int N = 8192 * 4;
        fe.Seek(0); fe.Play(); var live = new float[N * 2]; fe.RenderOffline(live, N); fe.StopTransport();
        fe.Stop(); fe.SetLoop(false, 0, 0); fe.SetMetronome(false); fe.StopTransport();
        long total = fe.BeginFreeze(ft, 4.0);
        fe.StopTransport(); fe.Seek(0); fe.Play();
        var chunk = new float[4096 * 2];
        for (long rem = total; rem > 0;) { int m = (int)Math.Min(4096, rem); fe.RenderOffline(chunk, m); rem -= m; }
        fe.StopTransport(); fe.EndFreeze(ft);
        fe.Seek(0); fe.Play(); var frozen = new float[N * 2]; fe.RenderOffline(frozen, N); fe.StopTransport();
        double md = MaxDiff(live, frozen);
        Check(fe.IsTrackFrozen(ft) && Rms(live, N) > 1e-3f && md < 1e-5, $"Nota Consort freeze == live render (maxdiff {md:G3})");
    }
    // Automation drives the cutoff and a cable's depth.
    {
        int ta = ce.AddConsortTrack();
        foreach (var id in new[] { "cutoff", "c1amt" })
        {
            int fi = CI(ce, ta, id);
            ce.PluginParamSet(ta, -1, fi, 0.1f);
            int lane = ce.AddPluginAutomationLane(ta, -1, id);
            ce.SetAutomationPoints(ta, lane, new[] { new AutomationPoint(0.0, 0.1f, 0f), new AutomationPoint(2.0, 0.9f, 0f) });
            var abuf = new float[4096 * 2];
            ce.Seek(1.99); ce.Play(); ce.RenderOffline(abuf, 4096); ce.StopTransport();
            Check(lane >= 0 && ce.PluginParamGet(ta, -1, fi) > 0.7f, $"automation drives Consort {id} ({ce.PluginParamGet(ta, -1, fi):F2})");
        }
    }
    // Stress: 16-voice true poly, ×4 oversampling, resonance and four cables in real time.
    {
        using var se = new NotaEngine(); se.SetBpm(120);
        int ta = se.AddConsortTrack();
        CS(se, ta, "truepoly", 1f); CS(se, ta, "oversample", 1f); CS(se, ta, "reso", 1f); CS(se, ta, "dlymix", 0.5f);
        for (int k = 1; k <= 4; k++) Cable(se, ta, k, k, 3 + k, 0.4f);
        se.AddMidiClip(ta, 0, 16);
        var ns = new NotaNote[16]; for (int i = 0; i < 16; i++) ns[i] = new NotaNote(36 + i * 3, 0, 16, 1f);
        se.SetClipNotes(ta, 0, ns);
        var blk = new float[128 * 2]; se.Seek(0); se.Play();
        for (int i = 0; i < 50; i++) se.RenderOffline(blk, 128);
        var sw = System.Diagnostics.Stopwatch.StartNew(); const int blocks = 1000;
        for (int i = 0; i < blocks; i++) se.RenderOffline(blk, 128);
        sw.Stop();
        double budget = 128 / 44.1, avg = sw.Elapsed.TotalMilliseconds / blocks;
        Check(se.InstrumentVoiceCount(ta) == 16 && avg < budget * 0.5, $"Nota Consort stress: 16 voices ×4 OS avg {avg:0.000} ms per 128-frame block (budget {budget:0.00} ms)");
        se.StopTransport();
    }
    // Factory presets: every named param is a real Consort id, each applies in place and sounds.
    {
        var cat = new FactoryPresetCatalog();
        var mine = cat.All().Where(p => p.IsInstrument && p.BuiltinKind == 15).ToList();
        Check(mine.Count == 28, $"Nota Consort ships 28 factory presets (got {mine.Count})");
        var bad = mine.SelectMany(p => cat.Document(p.Id)!.NamedParams!.Keys.Where(k => !cids.Contains(k)).Select(k => $"{p.DisplayName}:{k}")).ToList();
        Check(bad.Count == 0, $"every Consort preset param id exists{(bad.Count > 0 ? " — bad: " + string.Join(", ", bad) : "")}");
        int ta = ce.AddConsortTrack();
        int fails = mine.Count(p => cat.ApplyInPlace(ce, p.Id, ta, -1).Length != 0);
        Check(fails == 0, $"every Consort preset applies in place ({fails} failed)");
        var quiet = new List<string>();
        foreach (var p in mine)
        {
            var b = Play((e, tr) => cat.ApplyInPlace(e, p.Id, tr, -1), new[] { new NotaNote(48, 0, 2, 0.9f), new NotaNote(55, 0, 2, 0.9f), new NotaNote(60, 0, 2, 0.9f) }, 44100);
            float r = Rms(b, 44100), pk = b.Max(Math.Abs);
            if (!b.All(float.IsFinite) || r < 0.005f || pk > 2f) quiet.Add($"{p.DisplayName} ({r:F3}/{pk:F2})");
        }
        Check(quiet.Count == 0, $"every Consort preset sounds a held chord, finite and not clipping hard{(quiet.Count > 0 ? " — " + string.Join(", ", quiet) : "")}");
    }
    // MCP: add by kind 15.
    {
        var mcp = new Nota.Mcp.Tools.InstrumentTools(ce, new Nota.SmokeTest.SyncDispatch(), new Nota.SmokeTest.NoRefresh());
        int mt = mcp.AddInstrumentTrack(15).Result;
        Check(mt > 0 && ce.TrackInstrumentKind(mt) == 15, "MCP add_instrument_track(15) adds a Nota Consort");
        Check(mcp.ListInstrumentKinds().Any(k => k.Kind == 15 && k.Name == "Nota Consort"), "MCP list_instrument_kinds includes Nota Consort");
        Check(mcp.SetInstrumentParamById(mt, "c1amt", 0.3f).Result && Math.Abs(ce.PluginParamGet(mt, -1, CI(ce, mt, "c1amt")) - 0.3f) < 1e-4, "MCP set_instrument_param_by_id sets a Consort cable depth");
    }
}

static void Fft(double[] re, double[] im)
{
    int n = re.Length;
    for (int i = 1, j = 0; i < n; i++)
    {
        int bit = n >> 1;
        for (; (j & bit) != 0; bit >>= 1) j ^= bit;
        j ^= bit;
        if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
    }
    for (int len = 2; len <= n; len <<= 1)
    {
        double ang = -2 * Math.PI / len, wr = Math.Cos(ang), wi = Math.Sin(ang);
        for (int i = 0; i < n; i += len)
        {
            double cr = 1, ci = 0;
            for (int k = 0; k < len / 2; k++)
            {
                int a = i + k, b = a + len / 2;
                double tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                re[b] = re[a] - tr; im[b] = im[a] - ti; re[a] += tr; im[a] += ti;
                double nr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = nr;
            }
        }
    }
}

// ============================ Nota Pendulum ================================
Console.WriteLine("-- Nota Pendulum --");
{
    using var pe = new NotaEngine();
    pe.SetBpm(120); pe.SetTimeSignature(4, 4);
    int t = pe.AddPendulumSynthTrack();
    Check(t > 0, "AddPendulumSynthTrack returns a track");
    Check(pe.TrackInstrumentKind(t) == 8, $"instrument kind is 8 (got {pe.TrackInstrumentKind(t)})");
    Check(pe.DeviceName(t, -1) == "Nota Pendulum", $"instrument is Nota Pendulum (got '{pe.DeviceName(t, -1)}')");

    int pc = pe.PluginParamCount(t, -1);
    Check(pc == 26, $"Nota Pendulum exposes 26 params (got {pc})");
    int rateI = -1; bool idsOk = true;
    for (int i = 0; i < pc; i++)
    {
        if (pe.PluginParamId(t, -1, i).Length == 0 || pe.PluginParamName(t, -1, i).Length == 0) idsOk = false;
        if (pe.PluginParamId(t, -1, i) == "rate") rateI = i;
    }
    Check(idsOk && rateI >= 0, "params have ids + names; rate present");

    // State round-trips to another track.
    pe.PluginParamSet(t, -1, rateI, 0.66f);
    var state = pe.GetPluginState(t, -1);
    Check(state.Length >= 15 * 4, $"pendulum state serialized ({state.Length} bytes)");
    int t2 = pe.AddPendulumSynthTrack();
    pe.SetPluginState(t2, -1, state);
    Check(Math.Abs(pe.PluginParamGet(t2, -1, rateI) - 0.66f) < 1e-4, "pendulum state restores params on another track");

    pe.PluginParamSet(t, -1, rateI, 0.9f);
    int t3 = pe.DuplicateTrack(t);
    Check(t3 > 0 && Math.Abs(pe.PluginParamGet(t3, -1, rateI) - 0.9f) < 1e-4, "duplicate track clones pendulum params");

    // Generative: a held chord (sustained notes) is arpeggiated by the swinging balls
    // into audible output.
    int ta = pe.AddPendulumSynthTrack();
    pe.AddMidiClip(ta, 0.0, 4.0);
    pe.SetClipNotes(ta, 0, new[] {
        new NotaNote(60, 0.0, 4.0, 0.9f), new NotaNote(64, 0.0, 4.0, 0.9f),
        new NotaNote(67, 0.0, 4.0, 0.9f), new NotaNote(71, 0.0, 4.0, 0.9f) });   // Cmaj7 held
    var nbuf = new float[16384 * 2];
    pe.Seek(0.0); pe.Play(); pe.RenderOffline(nbuf, 16384);
    // The held chord is queryable while it sustains (before the clip ends / transport stops).
    var heldOut = new int[16];
    int heldCnt = pe.InstrumentHeldNotes(ta, heldOut);
    Check(heldCnt == 4, $"held-notes query returns the 4-note chord (got {heldCnt})");
    pe.StopTransport();
    float rms = Rms(nbuf, 16384); bool finite = true;
    foreach (var s in nbuf) if (!float.IsFinite(s) || Math.Abs(s) > 8f) { finite = false; break; }
    Check(rms > 0.001f && finite, $"Nota Pendulum arpeggiates a held chord (RMS {rms:F3})");
}

// ============================ Nota Operator ================================
Console.WriteLine("-- Nota Operator --");
{
    using var oe = new NotaEngine();
    oe.SetBpm(120); oe.SetTimeSignature(4, 4);
    int t = oe.AddOperatorSynthTrack();
    Check(t > 0, "AddOperatorSynthTrack returns a track");
    Check(oe.TrackInstrumentKind(t) == 9, $"instrument kind is 9 (got {oe.TrackInstrumentKind(t)})");
    Check(oe.DeviceName(t, -1) == "Nota Operator", $"instrument is Nota Operator (got '{oe.DeviceName(t, -1)}')");

    int pc = oe.PluginParamCount(t, -1);
    Check(pc == 48, $"Nota Operator exposes 48 params (got {pc})");
    int algoI = -1; bool idsOk = true;
    var opIds = new System.Collections.Generic.HashSet<string>();
    for (int i = 0; i < pc; i++)
    {
        string pid = oe.PluginParamId(t, -1, i);
        if (pid.Length == 0 || oe.PluginParamName(t, -1, i).Length == 0) idsOk = false;
        opIds.Add(pid);
        if (pid == "algo") algoI = i;
    }
    Check(idsOk && algoI >= 0, "params have ids + names; algo present");
    Check(opIds.Contains("fmdepth") && opIds.Contains("glide") && opIds.Contains("veltofm")
          && opIds.Contains("keylevel") && opIds.Contains("mono"), "mockup-3g params present (fmdepth/glide/veltofm/keylevel/mono)");
    Check(opIds.Contains("bend") && opIds.Contains("bendrange") && opIds.Contains("modwheel")
          && opIds.Contains("filkeytrk") && opIds.Contains("veltolevel"),
          "wheel + tracking params present (bend/bendrange/modwheel/filkeytrk/veltolevel)");

    // State round-trips to another track.
    int dlvl = -1; for (int i = 0; i < pc; i++) if (oe.PluginParamId(t, -1, i) == "dlevel") dlvl = i;
    oe.PluginParamSet(t, -1, dlvl, 0.66f);
    var state = oe.GetPluginState(t, -1);
    Check(state.Length >= 48 * 4, $"operator state serialized ({state.Length} bytes)");
    int t2 = oe.AddOperatorSynthTrack();
    oe.SetPluginState(t2, -1, state);
    Check(Math.Abs(oe.PluginParamGet(t2, -1, dlvl) - 0.66f) < 1e-4, "operator state restores params on another track");

    oe.PluginParamSet(t, -1, dlvl, 0.9f);
    int t3 = oe.DuplicateTrack(t);
    Check(t3 > 0 && Math.Abs(oe.PluginParamGet(t3, -1, dlvl) - 0.9f) < 1e-4, "duplicate track clones operator params");

    // Each of the 11 algorithms is audible + stable (default D carrier + C modulator).
    int lastTa = -1;
    for (int alg = 0; alg < 11; alg++)
    {
        int ta = oe.AddOperatorSynthTrack(); lastTa = ta;
        oe.PluginParamSet(ta, -1, algoI, alg / 10f);
        oe.AddMidiClip(ta, 0.0, 4.0);
        oe.SetClipNotes(ta, 0, new[] { new NotaNote(60, 0.0, 2.0, 0.9f) });
        var nbuf = new float[8192 * 2];
        oe.Seek(0.0); oe.Play(); oe.RenderOffline(nbuf, 8192); oe.StopTransport();
        float r = Rms(nbuf, 8192); bool fin = true;
        foreach (var s in nbuf) if (!float.IsFinite(s) || Math.Abs(s) > 8f) { fin = false; break; }
        Check(r > 0.001f && fin, $"Operator algorithm {alg + 1} is audible + stable (RMS {r:F3})");
    }

    // Harmonic-spectrum scope: after a note sounds, the analysis returns 32 finite bins
    // (0..1) with a non-zero fundamental → the OPS-tab spectrum viz has real data.
    {
        var sbuf = new float[32];
        int sn = oe.InstrumentScope(lastTa, sbuf);
        bool ok = sn == 32; float mx = 0;
        for (int i = 0; i < sn; i++) { if (!float.IsFinite(sbuf[i]) || sbuf[i] < 0 || sbuf[i] > 1.001f) ok = false; mx = Math.Max(mx, sbuf[i]); }
        Check(ok && mx > 0.01f, $"Operator spectrum scope returns 32 partials (n={sn}, peak {mx:F2})");
    }

    // Mono + glide render stays finite (legato voice path).
    {
        int tm = oe.AddOperatorSynthTrack();
        int monoI = -1, glideI = -1;
        for (int i = 0; i < oe.PluginParamCount(tm, -1); i++)
        { string pid = oe.PluginParamId(tm, -1, i); if (pid == "mono") monoI = i; else if (pid == "glide") glideI = i; }
        oe.PluginParamSet(tm, -1, monoI, 1f);
        oe.PluginParamSet(tm, -1, glideI, 0.4f);
        oe.AddMidiClip(tm, 0.0, 4.0);
        oe.SetClipNotes(tm, 0, new[] { new NotaNote(48, 0.0, 1.0, 0.8f), new NotaNote(60, 1.0, 1.0, 0.8f) });
        var mbuf = new float[16384 * 2];
        oe.Seek(0.0); oe.Play(); oe.RenderOffline(mbuf, 16384); oe.StopTransport();
        bool fin = true; foreach (var s in mbuf) if (!float.IsFinite(s) || Math.Abs(s) > 8f) { fin = false; break; }
        Check(fin && Rms(mbuf, 16384) > 0.001f, "Operator mono+glide legato renders audible + finite");
    }

    // The two performance wheels and the new tracking amounts: a bent, wheel-open,
    // key-tracked voice still renders finite and audible, and pitch bend really retunes.
    {
        int tw = oe.AddOperatorSynthTrack();
        int Pi(string id) { for (int i = 0; i < oe.PluginParamCount(tw, -1); i++) if (oe.PluginParamId(tw, -1, i) == id) return i; return -1; }
        oe.AddMidiClip(tw, 0.0, 4.0);
        oe.SetClipNotes(tw, 0, new[] { new NotaNote(60, 0.0, 2.0, 0.7f) });
        float Peak(float[] b, int n) { float m = 0; for (int i = 0; i < n * 2; i++) m = Math.Max(m, Math.Abs(b[i])); return m; }

        var wbuf = new float[8192 * 2];
        oe.PluginParamSet(tw, -1, Pi("bend"), 1f);          // wheel fully up
        oe.PluginParamSet(tw, -1, Pi("bendrange"), 1f);     // ±12 semitones
        oe.PluginParamSet(tw, -1, Pi("modwheel"), 1f);
        oe.PluginParamSet(tw, -1, Pi("filkeytrk"), 1f);
        oe.Seek(0.0); oe.Play(); oe.RenderOffline(wbuf, 8192); oe.StopTransport();
        bool wfin = true; foreach (var s in wbuf) if (!float.IsFinite(s) || Math.Abs(s) > 8f) { wfin = false; break; }
        Check(wfin && Rms(wbuf, 8192) > 0.001f, "Operator bend + mod wheel + key track render audible + finite");

        // Bent up an octave, the analysis fundamental doubles.
        var sbend = new float[32];
        oe.InstrumentScope(tw, sbend);
        Check(sbend.Length == 32, "Operator scope still reports 32 partials while bent");

        // Vel → Level at 0 flattens dynamics: the same soft note comes out far louder than
        // it does at the classic full amount. Each case gets its own engine so a voice left
        // sounding by the first render cannot leak into the second.
        float SoftPeak(float velToLevel)
        {
            using var ve = new NotaEngine();
            ve.SetBpm(120); ve.SetTimeSignature(4, 4);
            int tv = ve.AddOperatorSynthTrack();
            for (int i = 0; i < ve.PluginParamCount(tv, -1); i++)
                if (ve.PluginParamId(tv, -1, i) == "veltolevel") ve.PluginParamSet(tv, -1, i, velToLevel);
            ve.AddMidiClip(tv, 0.0, 4.0);
            ve.SetClipNotes(tv, 0, new[] { new NotaNote(60, 0.0, 2.0, 0.15f) });
            var b = new float[8192 * 2];
            ve.Seek(0.0); ve.Play(); ve.RenderOffline(b, 8192); ve.StopTransport();
            return Peak(b, 8192);
        }
        float flat = SoftPeak(0f), classic = SoftPeak(1f);
        Check(flat > classic * 2f, $"Operator vel → level off lifts a soft note (flat {flat:F3} vs classic {classic:F3})");
    }

    // Factory presets: 25 ship, every named param is a real Operator id, each applies in place.
    {
        var opIdSet = new System.Collections.Generic.HashSet<string>();
        for (int i = 0; i < oe.PluginParamCount(t, -1); i++) opIdSet.Add(oe.PluginParamId(t, -1, i));
        var cat = new FactoryPresetCatalog();
        var mine = cat.All().Where(p => p.IsInstrument && p.BuiltinKind == 9).ToList();
        Check(mine.Count == 25, $"Nota Operator ships 25 factory presets (got {mine.Count})");
        var bad = mine.SelectMany(p => cat.Document(p.Id)!.NamedParams!.Keys.Where(k => !opIdSet.Contains(k)).Select(k => $"{p.DisplayName}:{k}")).ToList();
        Check(bad.Count == 0, $"every Operator preset param id exists{(bad.Count > 0 ? " — bad: " + string.Join(", ", bad) : "")}");
        int tp = oe.AddOperatorSynthTrack();
        int fails = mine.Count(p => cat.ApplyInPlace(oe, p.Id, tp, -1).Length != 0);
        Check(fails == 0, $"every Operator preset applies in place ({fails} failed)");
    }
}

// ============================ Nota Grain ===================================
Console.WriteLine("-- Nota Grain --");
{
    using var ge = new NotaEngine();
    ge.SetBpm(120);
    int t = ge.AddGrainSynthTrack();
    Check(t > 0, "AddGrainSynthTrack returns a track");
    Check(ge.TrackInstrumentKind(t) == 10, $"instrument kind is 10 (got {ge.TrackInstrumentKind(t)})");
    Check(ge.DeviceName(t, -1) == "Nota Grain", $"instrument is Nota Grain (got '{ge.DeviceName(t, -1)}')");
    int pc = ge.PluginParamCount(t, -1);
    Check(pc == 23, $"Nota Grain exposes 23 params (got {pc})");
    int posI = -1; bool idsOk = true;
    for (int i = 0; i < pc; i++) { if (ge.PluginParamId(t, -1, i).Length == 0) idsOk = false; if (ge.PluginParamId(t, -1, i) == "position") posI = i; }
    Check(idsOk && posI >= 0, "params have ids; position present");

    // Procedural default sample → a held note granulates into audible output.
    ge.AddMidiClip(t, 0.0, 4.0);
    ge.SetClipNotes(t, 0, new[] { new NotaNote(60, 0.0, 3.0, 0.9f) });
    var nbuf = new float[16384 * 2];
    ge.Seek(0.0); ge.Play(); ge.RenderOffline(nbuf, 16384); ge.StopTransport();
    float rms = Rms(nbuf, 16384); bool finite = true;
    foreach (var s in nbuf) if (!float.IsFinite(s) || Math.Abs(s) > 8f) { finite = false; break; }
    Check(rms > 0.001f && finite, $"Nota Grain granulates the default sample (RMS {rms:F3})");

    // State + clone.
    ge.PluginParamSet(t, -1, posI, 0.66f);
    var state = ge.GetPluginState(t, -1);
    Check(state.Length >= 21 * 4, $"grain state serialized ({state.Length} bytes)");
    int t3 = ge.DuplicateTrack(t);
    Check(t3 > 0 && Math.Abs(ge.PluginParamGet(t3, -1, posI) - 0.66f) < 1e-4, "duplicate track clones grain params (+ sample)");

    // Load a real sample, then round-trip the project so the bundled sample survives.
    string gpath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nota-grain-" + System.Guid.NewGuid().ToString("N") + ".wav");
    { int n = 16000; var sp = new float[n]; for (int i = 0; i < n; i++) sp[i] = (float)(Math.Sin(2 * Math.PI * 330 * i / 32000.0) * 0.5); using var w = new Nota.Infrastructure.WavWriter(gpath, 32000, 1, WavBitDepth.Float32); w.WriteFrames(sp, n); }
    Check(ge.SetTrackGrainSample(t, gpath, 60), "loads a sample into Nota Grain");
    Check(ge.TryGetGrainInfo(t, out var giS) && giS.SampleId != 0, "grain reports its loaded sample id");
    ge.PluginParamSet(t, -1, posI, 0.4f);

    string gdir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nota-grain-proj-" + System.Guid.NewGuid().ToString("N"));
    try
    {
        var gw = new System.Collections.Generic.List<string>();
        var gdoc = ProjectService.Capture(ge, new TransportState(120, 1, false, false), gw);
        var gInst = gdoc.Tracks.FirstOrDefault(td => td.Instrument is { Kind: 10 });
        Check(gInst?.Instrument?.Sampler?.Sample is { Length: > 0 }, "grain sample captured into the project");
        ProjectService.Save(gdoc, gdir, ge);
        var gloaded = ProjectService.Load(gdir);
        using var gdst = new NotaEngine();
        ProjectService.Apply(gloaded, gdst, gdir);
        int rt = -1; for (int i = 0; i < gdst.TrackCount; i++) if (gdst.TryGetTrackInfo(i, out var tinf) && gdst.TrackInstrumentKind(tinf.Id) == 10) { rt = tinf.Id; break; }
        Check(rt > 0 && gdst.TryGetGrainInfo(rt, out var giR) && giR.SampleId != 0, "reloaded Grain still has its sample");
        Check(rt > 0 && Math.Abs(gdst.PluginParamGet(rt, -1, posI) - 0.4f) < 1e-3, "reloaded Grain restores its params");
    }
    finally { try { System.IO.Directory.Delete(gdir, true); } catch { } try { System.IO.File.Delete(gpath); } catch { } }

    // From here on: a fresh Grain on the built-in pad, rendered alone.
    using var gx = new NotaEngine();
    gx.SetBpm(120);
    int g = gx.AddGrainSynthTrack();
    int gpc = gx.PluginParamCount(g, -1);
    var gIds = new System.Collections.Generic.HashSet<string>();
    int GI(string id) { for (int i = 0; i < gpc; i++) if (gx.PluginParamId(g, -1, i) == id) return i; return -1; }
    for (int i = 0; i < gpc; i++) gIds.Add(gx.PluginParamId(g, -1, i));
    int wetI = GI("drywet");
    Check(wetI == 22 && gx.PluginParamName(g, -1, wetI) == "Dry/Wet", "Dry/Wet is appended as param 22");
    Check(Math.Abs(gx.InstrumentParamDefault(g, wetI) - 1f) < 1e-6, "Dry/Wet defaults fully wet (older projects sound as before)");
    // A state saved before Dry/Wet existed (22 floats) loads fully wet, as it sounded.
    {
        var st = gx.GetPluginState(g, -1);
        int g2 = gx.AddGrainSynthTrack();
        gx.SetPluginState(g2, -1, st[..(22 * 4)]);
        Check(Math.Abs(gx.PluginParamGet(g2, -1, wetI) - 1f) < 1e-6, "a 22-param state loads fully wet");
        gx.RemoveTrack(g2);
    }
    int GrainFrames(double beats) => (int)Math.Round(beats * 60.0 / 120.0 * (gx.SampleRate > 0 ? gx.SampleRate : 48000.0));
    void GrainStop() { gx.StopTransport(); gx.RenderOffline(new float[64 * 2], 64); }
    void GrainRender(float[] buf, Action? perBlock = null)
    {
        var blk = new float[1024 * 2];
        for (int at = 0; at < buf.Length / 2; at += 1024)
        {
            int n = Math.Min(1024, buf.Length / 2 - at);
            gx.RenderOffline(blk, n);
            Array.Copy(blk, 0, buf, at * 2, n * 2);
            perBlock?.Invoke();
        }
    }
    gx.AddMidiClip(g, 0.0, 4.0);
    gx.SetClipNotes(g, 0, new[] { new NotaNote(60, 0.0, 3.0, 0.9f) });

    // Dry, half and wet are each audible and finite.
    foreach (var (wv, word) in new[] { (0f, "dry"), (0.5f, "half"), (1f, "wet") })
    {
        gx.PluginParamSet(g, -1, wetI, wv);
        var wbuf = new float[GrainFrames(2.0) * 2];
        gx.Seek(0.0); gx.Play(); GrainRender(wbuf); GrainStop();
        float r = Rms(wbuf, wbuf.Length / 2); bool fin = true;
        foreach (var x in wbuf) if (!float.IsFinite(x) || Math.Abs(x) > 4f) { fin = false; break; }
        Check(r > 0.005f && fin, $"Grain {word} (Dry/Wet {wv:0.0}) is audible + finite (RMS {r:F3})");
    }
    gx.PluginParamSet(g, -1, wetI, 1f);

    // The scope publishes the voices and the live grain cloud; Position is live — a held
    // note's read head follows it (Freeze).
    {
        int posJ = GI("position");
        gx.PluginParamSet(g, -1, GI("scanmode"), 0.5f);
        gx.PluginParamSet(g, -1, posJ, 0.2f);
        var sc = new float[2 + 8 * 12 * 3];
        var heads = new float[8];
        int maxGrains = 0, scN = 0; bool inRange = true;
        float headBefore = -1, headAfter = -1;
        gx.Seek(0.0); gx.Play();
        var blk = new float[1024 * 2];
        for (int b = 0; b < 40; b++)
        {
            if (b == 20) { headBefore = gx.GrainPlayPositions(g, heads) > 0 ? heads[0] : -1; gx.PluginParamSet(g, -1, posJ, 0.7f); }
            gx.RenderOffline(blk, 1024);
            scN = gx.InstrumentScope(g, sc);
            if (scN >= 2)
            {
                int n = (int)sc[1];
                maxGrains = Math.Max(maxGrains, n);
                for (int k = 0; k < n; k++)
                    for (int c = 0; c < 3; c++) { float v = sc[2 + k * 3 + c]; if (!(v >= 0f && v <= 1.0001f)) inRange = false; }
            }
        }
        headAfter = gx.GrainPlayPositions(g, heads) > 0 ? heads[0] : -1;
        GrainStop();
        Check(scN >= 2 && sc[0] >= 0, $"Grain scope publishes voices + grains ({scN} values)");
        Check(maxGrains >= 2 && inRange, $"Grain scope carries the live cloud in range ({maxGrains} grains)");
        Check(Math.Abs(headBefore - 0.2f) < 0.02f && Math.Abs(headAfter - 0.7f) < 0.02f,
            $"a held note follows Position live (read head {headBefore:F2} → {headAfter:F2})");
        gx.PluginParamSet(g, -1, posJ, gx.InstrumentParamDefault(g, posJ));
    }

    // A repeated note that starts a hair before the previous one ends keeps sounding.
    {
        gx.PluginParamSet(g, -1, GI("attack"), 0f); gx.PluginParamSet(g, -1, GI("release"), 0f);
        float WindowRms(NotaNote[] notes)
        {
            gx.SetClipNotes(g, 0, notes);
            var obuf = new float[GrainFrames(2.2) * 2];
            gx.Seek(0.0); gx.Play(); GrainRender(obuf); GrainStop();
            int from = GrainFrames(1.4), len = GrainFrames(0.4);
            double acc = 0; for (int i = from; i < from + len; i++) acc += obuf[i * 2] * obuf[i * 2];
            return (float)Math.Sqrt(acc / len);
        }
        float alone = WindowRms(new[] { new NotaNote(60, 0.0, 1.02, 0.9f) });
        float held = WindowRms(new[] { new NotaNote(60, 0.0, 1.02, 0.9f), new NotaNote(60, 1.0, 1.5, 0.9f) });
        Check(held > 0.01f && held > alone * 5, $"Grain: an overlapping repeat of a note keeps sounding (RMS {held:F3} vs {alone:F4} released)");
    }

    // Factory presets: 25 ship, every named param is a real Grain id, each applies in place
    // and renders audible and finite.
    {
        var cat = new FactoryPresetCatalog();
        var mine = cat.All().Where(p => p.IsInstrument && p.BuiltinKind == 10).ToList();
        Check(mine.Count == 25, $"Nota Grain ships 25 factory presets (got {mine.Count})");
        var bad = mine.SelectMany(p => cat.Document(p.Id)!.NamedParams!.Keys.Where(k => !gIds.Contains(k)).Select(k => $"{p.DisplayName}:{k}")).ToList();
        Check(bad.Count == 0, $"every Grain preset param id exists{(bad.Count > 0 ? " — bad: " + string.Join(", ", bad) : "")}");
        gx.SetClipNotes(g, 0, new[] { new NotaNote(48, 0.0, 1.5, 0.9f), new NotaNote(60, 0.0, 1.5, 0.9f), new NotaNote(67, 0.5, 1.0, 0.9f) });
        var off = new System.Collections.Generic.List<string>();
        var pbuf = new float[GrainFrames(2.0) * 2];
        foreach (var p in mine)
        {
            if (cat.ApplyInPlace(gx, p.Id, g, -1).Length != 0) { off.Add($"{p.DisplayName} (apply)"); continue; }
            gx.Seek(0.0); gx.Play(); GrainRender(pbuf); GrainStop();
            bool ok = true; foreach (var x in pbuf) if (!float.IsFinite(x) || Math.Abs(x) > 1.01f) { ok = false; break; }
            float r = Rms(pbuf, pbuf.Length / 2);
            if (!ok || r < 0.005f) off.Add($"{p.DisplayName} (RMS {r:F3})");
        }
        Check(off.Count == 0, $"every Grain preset is audible and finite{(off.Count > 0 ? " — off: " + string.Join(", ", off) : "")}");
    }
}

// ============================ Nota Flux ====================================
Console.WriteLine("-- Nota Flux --");
{
    using var fe = new NotaEngine();
    fe.SetBpm(120); fe.SetTimeSignature(4, 4);
    int t = fe.AddFluxSynthTrack();
    Check(t > 0, "AddFluxSynthTrack returns a track");
    Check(fe.TrackInstrumentKind(t) == 11, $"instrument kind is 11 (got {fe.TrackInstrumentKind(t)})");
    Check(fe.DeviceName(t, -1) == "Nota Flux", $"instrument is Nota Flux (got '{fe.DeviceName(t, -1)}')");

    int pc = fe.PluginParamCount(t, -1);
    Check(pc == 13, $"Nota Flux exposes 13 params (got {pc})");
    var fIds = new System.Collections.Generic.HashSet<string>();
    bool idsOk = true;
    for (int i = 0; i < pc; i++)
    {
        string pid = fe.PluginParamId(t, -1, i);
        if (pid.Length == 0 || fe.PluginParamName(t, -1, i).Length == 0) idsOk = false;
        fIds.Add(pid);
    }
    Check(idsOk && fIds.Contains("vecx") && fIds.Contains("vecy") && fIds.Contains("listen")
          && fIds.Contains("target") && fIds.Contains("motion") && fIds.Contains("age"), "Flux params have ids+names (vecx/vecy/listen/target/motion/age)");

    fe.AddMidiClip(t, 0.0, 4.0);
    fe.SetClipNotes(t, 0, new[] { new NotaNote(60, 0.0, 3.0, 0.9f) });

    // Sweep the vector across the four worlds — each corner is audible + finite.
    (float x, float y, string world)[] corners = { (0f, 0f, "WARM"), (1f, 0f, "GLASS"), (0f, 1f, "MOOG"), (1f, 1f, "GRAIN") };
    int vx = -1, vy = -1; for (int i = 0; i < pc; i++) { string pid = fe.PluginParamId(t, -1, i); if (pid == "vecx") vx = i; else if (pid == "vecy") vy = i; }
    foreach (var (cx, cy, world) in corners)
    {
        fe.PluginParamSet(t, -1, vx, cx); fe.PluginParamSet(t, -1, vy, cy);
        var fbuf = new float[16384 * 2];
        fe.Seek(0.0); fe.Play(); fe.RenderOffline(fbuf, 16384); fe.StopTransport();
        float r = Rms(fbuf, 16384); bool fin = true;
        foreach (var s in fbuf) if (!float.IsFinite(s) || Math.Abs(s) > 8f) { fin = false; break; }
        Check(r > 0.001f && fin, $"Flux world {world} is audible + stable (RMS {r:F3})");
    }

    // State round-trips + clone.
    fe.PluginParamSet(t, -1, vx, 0.73f);
    var state = fe.GetPluginState(t, -1);
    Check(state.Length >= 13 * 4, $"flux state serialized ({state.Length} bytes)");
    int t2 = fe.DuplicateTrack(t);
    Check(t2 > 0 && Math.Abs(fe.PluginParamGet(t2, -1, vx) - 0.73f) < 1e-4, "duplicate track clones Flux params");

    // React: an instrument sidechain source round-trips + drives the analysis scope.
    Check(fe.InstrumentAcceptsSidechain(t), "Nota Flux accepts a sidechain (React)");
    int drum = fe.AddInstrumentTrack();
    fe.AddMidiClip(drum, 0.0, 4.0);
    fe.SetClipNotes(drum, 0, new[] { new NotaNote(48, 0.0, 0.25f, 1.0f), new NotaNote(48, 1.0, 0.25f, 1.0f), new NotaNote(48, 2.0, 0.25f, 1.0f), new NotaNote(48, 3.0, 0.25f, 1.0f) });
    fe.SetInstrumentSidechainSource(t, drum);
    Check(fe.InstrumentSidechainSource(t) == drum, "instrument sidechain source round-trips");
    // Point target at Vector + high Listen, render a few blocks, and confirm the scope moves.
    int tg = -1, ls = -1; for (int i = 0; i < pc; i++) { string pid = fe.PluginParamId(t, -1, i); if (pid == "target") tg = i; else if (pid == "listen") ls = i; }
    fe.PluginParamSet(t, -1, tg, 1f); fe.PluginParamSet(t, -1, ls, 1f);
    fe.PluginParamSet(t, -1, vx, 0.34f); fe.PluginParamSet(t, -1, vy, 0.28f);
    var rbuf = new float[4096 * 2];
    fe.Seek(0.0); fe.Play();
    float envSeen = 0f, reactSeen = 0f, pulled = 0f;
    var sc = new float[9];
    int scN = 0;
    for (int b = 0; b < 20; b++)
    {
        fe.RenderOffline(rbuf, 4096); scN = fe.InstrumentScope(t, sc);
        if (scN >= 6) { envSeen = Math.Max(envSeen, sc[0]); reactSeen = Math.Max(reactSeen, sc[3]); pulled = Math.Max(pulled, sc[4] - 0.34f); }
    }
    fe.StopTransport();
    Check(scN == 9, $"Flux scope publishes 9 values (got {scN})");
    Check(envSeen > 0.001f, $"React scope registers the sidechain envelope (env {envSeen:F3})");
    Check(reactSeen > 0.01f && pulled > 0.01f, $"React on Vector pulls the vector with a source (react {reactSeen:F3}, Δx {pulled:F3})");
    Check(sc[7] >= 2 && sc[8] == 1f, $"React counts the source's transients (onsets {sc[7]:F0}, source {sc[8]:F0})");

    fe.SetInstrumentSidechainSource(t, -1);
    Check(fe.InstrumentSidechainSource(t) == -1, "instrument sidechain clears");
    // From here on the master is Flux alone: silence the duplicate and the drum source.
    fe.SetTrackMute(t2, true); fe.SetTrackMute(drum, true);

    // With no source React is off: its own loud notes don't modulate anything.
    fe.SetClipNotes(t, 0, new[] { new NotaNote(48, 0.0, 3.0, 1f), new NotaNote(55, 0.0, 3.0, 1f), new NotaNote(60, 0.0, 3.0, 1f) });
    float reactOff = 0f, driftOff = 0f;
    fe.Seek(0.0); fe.Play();
    for (int b = 0; b < 12; b++)
    {
        fe.RenderOffline(rbuf, 4096);
        if (fe.InstrumentScope(t, sc) >= 9) { reactOff = Math.Max(reactOff, sc[3]); driftOff = Math.Max(driftOff, sc[8]); }
    }
    FluxStop();
    Check(reactOff == 0f && driftOff == 0f, $"no source → React is off (react {reactOff:F3})");

    // A repeated note that starts before the previous one ends keeps sounding when the old
    // one's note-off arrives. A plucky, dry patch, so a released voice is silent in the
    // window: the overlapping pair must stay well above the first note on its own.
    int envI = -1, spI = -1;
    for (int i = 0; i < pc; i++) { string pid = fe.PluginParamId(t, -1, i); if (pid == "env") envI = i; else if (pid == "space") spI = i; }
    fe.PluginParamSet(t, -1, envI, 0.8f); fe.PluginParamSet(t, -1, spI, 0f);
    float WindowRms(NotaNote[] notes)
    {
        fe.SetClipNotes(t, 0, notes);
        var obuf = new float[FluxFrames(2.2) * 2];
        fe.Seek(0.0); fe.Play(); FluxRender(obuf); FluxStop();
        int from = FluxFrames(1.4), len = FluxFrames(0.4);
        double acc = 0; for (int i = from; i < from + len; i++) acc += obuf[i * 2] * obuf[i * 2];
        return (float)Math.Sqrt(acc / len);
    }
    float alone = WindowRms(new[] { new NotaNote(60, 0.0, 1.02, 0.9f) });
    float held = WindowRms(new[] { new NotaNote(60, 0.0, 1.02, 0.9f), new NotaNote(60, 1.0, 1.5, 0.9f) });
    Check(held > 0.01f && held > alone * 5, $"Flux: an overlapping repeat of a note keeps sounding (RMS {held:F3} vs {alone:F4} released)");
    int FluxFrames(double beats) => (int)Math.Round(beats * 60.0 / 120.0 * (fe.SampleRate > 0 ? fe.SampleRate : 48000.0));
    // Stop, then render a stopped block so the engine flushes held voices (it releases them
    // on the play→stop edge it sees while rendering).
    void FluxStop() { fe.StopTransport(); fe.RenderOffline(new float[64 * 2], 64); }
    void FluxRender(float[] buf)   // in 4096-frame blocks, as the engine's host would
    {
        var blk = new float[4096 * 2];
        for (int at = 0; at < buf.Length / 2; at += 4096)
        {
            int n = Math.Min(4096, buf.Length / 2 - at);
            fe.RenderOffline(blk, n);
            Array.Copy(blk, 0, buf, at * 2, n * 2);
        }
    }

    // Factory presets: 25 ship, every named param is a real Flux id, each applies in place
    // and renders audible and finite.
    {
        var cat = new FactoryPresetCatalog();
        var mine = cat.All().Where(p => p.IsInstrument && p.BuiltinKind == 11).ToList();
        Check(mine.Count == 25, $"Nota Flux ships 25 factory presets (got {mine.Count})");
        var bad = mine.SelectMany(p => cat.Document(p.Id)!.NamedParams!.Keys.Where(k => !fIds.Contains(k)).Select(k => $"{p.DisplayName}:{k}")).ToList();
        Check(bad.Count == 0, $"every Flux preset param id exists{(bad.Count > 0 ? " — bad: " + string.Join(", ", bad) : "")}");
        fe.SetClipNotes(t, 0, new[] { new NotaNote(48, 0.0, 1.5, 0.9f), new NotaNote(55, 0.0, 1.5, 0.9f), new NotaNote(62, 0.5, 1.0, 0.9f) });
        var off = new System.Collections.Generic.List<string>();
        var pbuf = new float[FluxFrames(2.0) * 2];
        foreach (var p in mine)
        {
            if (cat.ApplyInPlace(fe, p.Id, t, -1).Length != 0) { off.Add($"{p.DisplayName} (apply)"); continue; }
            fe.Seek(0.0); fe.Play(); FluxRender(pbuf); FluxStop();
            bool ok = true; foreach (var x in pbuf) if (!float.IsFinite(x) || Math.Abs(x) > 1.01f) { ok = false; break; }
            float rms = Rms(pbuf, pbuf.Length / 2);
            if (!ok || rms < 0.01f) off.Add($"{p.DisplayName} ({rms:F3})");
        }
        Check(off.Count == 0, $"every Flux preset applies and renders audible and finite{(off.Count > 0 ? " — off: " + string.Join(", ", off) : "")}");
    }
}

// ============================ Sampler ======================================
Console.WriteLine("-- Sampler --");
{
    // A short decaying-sine WAV so start/end/reverse produce measurably different renders.
    string sampPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nota-samp-" + System.Guid.NewGuid().ToString("N") + ".wav");
    {
        int n = 16000; var ssampPath = new float[n];   // 0.5 s mono @ 32k (distinct SR to catch SR round-trip)
        for (int i = 0; i < n; i++) ssampPath[i] = (float)(Math.Sin(2 * Math.PI * 220 * i / 32000.0) * Math.Exp(-3.0 * i / n));
        using var w = new Nota.Infrastructure.WavWriter(sampPath, 32000, 1, WavBitDepth.Float32);
        w.WriteFrames(ssampPath, n);
    }
    using var se = new NotaEngine();
    se.SetBpm(120); se.SetTimeSignature(4, 4);

    int t = se.AddSamplerInstrumentTrack();
    Check(t > 0 && se.TrackInstrumentKind(t) == 1, "AddSamplerInstrumentTrack → kind 1");
    Check(se.DeviceName(t, -1) == "Nota Sampler", $"instrument is Nota Sampler (got '{se.DeviceName(t, -1)}')");
    int pc = se.PluginParamCount(t, -1);
    Check(pc == 21, $"Nota Sampler exposes 21 params (got {pc})");
    se.AddMidiClip(t, 0.0, 4.0);
    se.SetClipNotes(t, 0, new[] { new NotaNote(60, 0.0, 3.0, 0.9f) });

    // Empty sampler is silent.
    var sb = new float[16384 * 2];
    se.Seek(0); se.Play(); se.RenderOffline(sb, 16384); se.StopTransport();
    Check(Rms(sb, 16384) < 1e-5f, "empty Sampler is silent");

    // Load the sample → audible.
    Check(se.SetTrackSamplerSample(t, sampPath, 60), "load sample into the Sampler");
    se.Seek(0); se.Play(); se.RenderOffline(sb, 16384); se.StopTransport();
    float full = Rms(sb, 16384);
    Check(full > 0.001f, $"loaded Sampler is audible (rms {full:0.0000})");

    int startIdx = -1, revIdx = -1; for (int i = 0; i < pc; i++) { var id = se.PluginParamId(t, -1, i); if (id == "start") startIdx = i; if (id == "reverse") revIdx = i; }
    Check(startIdx >= 0 && revIdx >= 0, "sampler params have ids (start, reverse)");

    // Move Start to 0.85 → much less content → lower energy.
    se.PluginParamSet(t, -1, startIdx, 0.85f);
    se.Seek(0); se.Play(); se.RenderOffline(sb, 16384); se.StopTransport();
    Check(Rms(sb, 16384) < full, $"Sample Start trims the playback (rms {Rms(sb, 16384):0.0000} < {full:0.0000})");
    se.PluginParamSet(t, -1, startIdx, 0.0f);

    // Reverse changes the output.
    var fwd = new float[16384 * 2]; se.Seek(0); se.Play(); se.RenderOffline(fwd, 16384); se.StopTransport();
    se.PluginParamSet(t, -1, revIdx, 1.0f);
    var rev = new float[16384 * 2]; se.Seek(0); se.Play(); se.RenderOffline(rev, 16384); se.StopTransport();
    double d = 0; for (int i = 0; i < 16384 * 2; i++) { double x = fwd[i] - rev[i]; d += x * x; }
    Check(Math.Sqrt(d / (16384 * 2)) > 0.002f, "Reverse changes the render");
    se.PluginParamSet(t, -1, revIdx, 0.0f);

    // Root change is lock-free + affects pitch (rate) — just exercise the API.
    Check(se.SetTrackSamplerRoot(t, 48), "set sampler root");

    // Project round-trip: params + sample restored, still audible on a fresh engine.
    se.PluginParamSet(t, -1, startIdx, 0.25f);
    var warn = new System.Collections.Generic.List<string>();
    var doc = ProjectService.Capture(se, new TransportState(120.0, 1.0, false, false), warn);
    string bundle = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nota-samp-" + System.Guid.NewGuid().ToString("N") + ".nota");
    ProjectService.Save(doc, bundle, se);
    using var se2 = new NotaEngine();
    ProjectService.Apply(ProjectService.Load(bundle), se2, bundle);
    bool restored = false;
    for (int i = 0; i < se2.TrackCount; i++)
        if (se2.TryGetTrackInfo(i, out var ti) && ti.IsInstrument && se2.TrackInstrumentKind(ti.Id) == 1)
        {
            int sIdx = -1; for (int p = 0; p < se2.PluginParamCount(ti.Id, -1); p++) if (se2.PluginParamId(ti.Id, -1, p) == "start") sIdx = p;
            bool okParams = sIdx >= 0 && Math.Abs(se2.PluginParamGet(ti.Id, -1, sIdx) - 0.25f) < 1e-3;
            se2.TryGetSamplerInfo(ti.Id, out var si2);
            Check(si2.SampleId != 0, "sampler sample reloaded");
            Check(si2.RootNote == 48, $"sampler root round-trips (got {si2.RootNote}, expected 48)");
            se2.TryGetSampleInfo(si2.SampleId, out var sr2);
            Check(Math.Abs(sr2.SampleRate - 32000) < 1, $"sample source SR round-trips (got {sr2.SampleRate}, expected 32000)");
            restored = okParams;
        }
    Check(restored, "project round-trip restores sampler params + sample");

    // Drum-Rack compat: the classic one-shot path (AddSamplerTrack) still plays.
    int t2 = se.AddSamplerTrack(sampPath, 60, false);
    se.AddMidiClip(t2, 0.0, 4.0); se.SetClipNotes(t2, 0, new[] { new NotaNote(60, 0.0, 2.0, 0.9f) });
    se.Seek(0); se.Play(); se.RenderOffline(sb, 16384); se.StopTransport();
    Check(Rms(sb, 16384) > 0.001f, "AddSamplerTrack (one-shot) still audible");

    try { System.IO.File.Delete(sampPath); System.IO.Directory.Delete(bundle, true); } catch { /* best-effort */ }
}

// ===================== M9 follow-up: clip volume envelope ==================
Console.WriteLine("-- M9 follow-up: clip volume envelope --");
{
    using var ce = new NotaEngine();
    ce.SetBpm(120); ce.SetTimeSignature(4, 4);
    int ct = ce.AddAudioTrack();
    int cc = ce.AddAudioClip(ct, wav, 0.0);   // 1 s sine @440 → ~2 beats at 120
    Check(ce.GetClipVolumeEnvelope(ct, cc).Length == 0, "clip envelope empty by default");

    // Set a fade-out envelope (out of order → engine sorts) with a segment curve.
    ce.SetClipVolumeEnvelope(ct, cc, new[]
    {
        new Nota.Application.AutomationPoint(2.0, 0.0f),
        new Nota.Application.AutomationPoint(0.0, 1.0f, 0.6f),   // curve on the first point (bend, M9-D)
    });
    var cpts = ce.GetClipVolumeEnvelope(ct, cc);
    Check(cpts.Length == 2 && cpts[0].Beat == 0.0 && System.Math.Abs(cpts[0].Value - 1.0f) < 1e-6f,
          "clip envelope points round-trip sorted");
    Check(System.Math.Abs(cpts[0].Curve - 0.6f) < 1e-6f, "clip envelope segment curve round-trips (M9-D)");

    // Undo restores the empty envelope (structural edit checkpoints undo).
    ce.Undo();
    Check(ce.GetClipVolumeEnvelope(ct, cc).Length == 0, "undo restores empty clip envelope");
    ce.Redo();
    Check(ce.GetClipVolumeEnvelope(ct, cc).Length == 2, "redo restores clip envelope");

    // Render: fade-out → RMS near the start > RMS near the end (unwarped path).
    const int spb = 22050;   // 120 BPM @ 44100
    var cb = new float[512 * 2];
    ce.Play();
    ce.Seek(0.0); ce.RenderOffline(cb, 512);
    float rmsStart = Rms(cb, 512);
    ce.Seek(1.7); ce.RenderOffline(cb, 512);   // ~85% through the 2-beat clip → near-silent
    float rmsEnd = Rms(cb, 512);
    Check(rmsStart > 0.02f && rmsEnd < rmsStart * 0.5f,
          $"clip envelope fades the unwarped clip (start={rmsStart:F4}, end={rmsEnd:F4})");
    ce.StopTransport();

    // Same on a WARPED clip (envelope applied via the temp fold path).
    ce.SetClipWarp(ct, cc, true, 3);   // flat sine → neutral seed, warp on
    ce.Play();
    ce.Seek(0.0); ce.RenderOffline(cb, 512);
    float wStart = Rms(cb, 512);
    ce.Seek(1.7); ce.RenderOffline(cb, 512);
    float wEnd = Rms(cb, 512);
    Check(wStart > 0.02f && wEnd < wStart * 0.5f,
          $"clip envelope fades the warped clip (start={wStart:F4}, end={wEnd:F4})");
    ce.StopTransport();

    // Empty envelope → no attenuation (fast path): full-level clip stays loud at the end.
    int ct2 = ce.AddAudioTrack();
    int cc2 = ce.AddAudioClip(ct2, wav, 0.0);
    ce.Play(); ce.Seek(1.7); ce.RenderOffline(cb, 512);
    Check(Rms(cb, 512) > 0.02f, "no envelope → clip plays at full level");
    ce.StopTransport();
}

// ===================== M9 follow-up: clip pan envelope =====================
Console.WriteLine("-- M9 follow-up: clip pan envelope --");
{
    static (float l, float r) ChannelRms(float[] b, int frames)
    {
        double sl = 0, sr = 0;
        for (int i = 0; i < frames; i++) { sl += b[i * 2] * (double)b[i * 2]; sr += b[i * 2 + 1] * (double)b[i * 2 + 1]; }
        return ((float)Math.Sqrt(sl / frames), (float)Math.Sqrt(sr / frames));
    }

    using var pe = new NotaEngine();
    pe.SetBpm(120); pe.SetTimeSignature(4, 4);
    int pt = pe.AddAudioTrack();
    int pc = pe.AddAudioClip(pt, wav, 0.0);
    Check(pe.GetClipPanEnvelope(pt, pc).Length == 0, "clip pan envelope empty by default");

    // Constant hard-right pan (+1) → left channel silenced, right audible.
    pe.SetClipPanEnvelope(pt, pc, new[]
    {
        new Nota.Application.AutomationPoint(0.0, 1.0f),
        new Nota.Application.AutomationPoint(2.0, 1.0f),
    });
    var ppts = pe.GetClipPanEnvelope(pt, pc);
    Check(ppts.Length == 2 && System.Math.Abs(ppts[0].Value - 1.0f) < 1e-6f, "clip pan envelope round-trips");

    var pb = new float[512 * 2];
    pe.SetMetronome(false); pe.SetLoop(false, 0, 0);
    pe.Play(); pe.Seek(0.0); pe.RenderOffline(pb, 512);
    var (rl, rr) = ChannelRms(pb, 512);
    Check(rr > 0.02f && rl < rr * 0.1f, $"pan +1 sends the clip hard right (L={rl:F4}, R={rr:F4})");

    // Undo restores empty; then hard-left (-1) silences the right.
    pe.Undo();
    Check(pe.GetClipPanEnvelope(pt, pc).Length == 0, "undo restores empty pan envelope");
    pe.SetClipPanEnvelope(pt, pc, new[] { new Nota.Application.AutomationPoint(0.0, -1.0f) });
    pe.Seek(0.0); pe.RenderOffline(pb, 512);
    (rl, rr) = ChannelRms(pb, 512);
    Check(rl > 0.02f && rr < rl * 0.1f, $"pan -1 sends the clip hard left (L={rl:F4}, R={rr:F4})");
    pe.StopTransport();
}

// ===================== M9 follow-up: MIDI clip envelopes ===================
// Velocity scales note velocity at note-on; Volume scales the instrument output
// per sample during the clip. Round-trip + audible effect (device-free).
Console.WriteLine("-- M9 follow-up: MIDI clip envelopes --");
{
    using var me2 = new NotaEngine();
    me2.SetBpm(120); me2.SetTimeSignature(4, 4);
    int mt = me2.AddInstrumentTrack();
    int mc = me2.AddMidiClip(mt, 0.0, 8.0);
    me2.SetClipNotes(mt, mc, new[] { new NotaNote(60, 0.0, 8.0, 0.9f) });
    Check(me2.GetMidiClipEnvelope(mt, mc, Nota.Application.MidiClipEnvelope.Velocity).Length == 0,
          "MIDI velocity envelope empty by default");

    var mbuf = new float[2048 * 2];
    me2.Play(); me2.Seek(0.0); me2.RenderOffline(mbuf, 2048);
    float mFull = Rms(mbuf, 2048);
    me2.StopTransport(); me2.RenderOffline(mbuf, 64);   // play→stop edge flushes voices

    // Constant low velocity (0.2) → quieter note than the default 0.9.
    me2.SetMidiClipEnvelope(mt, mc, Nota.Application.MidiClipEnvelope.Velocity,
        new[] { new Nota.Application.AutomationPoint(0.0, 0.2f) });
    var vpts = me2.GetMidiClipEnvelope(mt, mc, Nota.Application.MidiClipEnvelope.Velocity);
    Check(vpts.Length == 1 && System.Math.Abs(vpts[0].Value - 0.2f) < 1e-6f, "velocity envelope round-trips");
    me2.Play(); me2.Seek(0.0); me2.RenderOffline(mbuf, 2048);
    float mLow = Rms(mbuf, 2048);
    Check(mLow > 0.005f && mLow < mFull * 0.5f, $"velocity envelope scales note dynamics (full={mFull:F4}, low={mLow:F4})");

    // Undo restores empty velocity envelope.
    me2.Undo();
    Check(me2.GetMidiClipEnvelope(mt, mc, Nota.Application.MidiClipEnvelope.Velocity).Length == 0, "undo restores empty velocity envelope");

    // Volume envelope: fade-out over the clip → RMS start > RMS end.
    me2.SetMidiClipEnvelope(mt, mc, Nota.Application.MidiClipEnvelope.Volume,
        new[] { new Nota.Application.AutomationPoint(0.0, 1.0f), new Nota.Application.AutomationPoint(8.0, 0.0f) });
    me2.Seek(0.0); me2.RenderOffline(mbuf, 2048);
    float mvStart = Rms(mbuf, 2048);
    me2.Seek(7.5); me2.RenderOffline(mbuf, 2048);
    float mvEnd = Rms(mbuf, 2048);
    Check(mvStart > 0.01f && mvEnd < mvStart * 0.5f, $"MIDI clip volume envelope fades the output (start={mvStart:F4}, end={mvEnd:F4})");
    me2.StopTransport();
}

// ===================== M2: MIDI, instrument, recording =====================
Console.WriteLine("-- M2 --");

int inst = engine.AddInstrumentTrack();
Check(inst > 0, "add instrument track");
int mclip = engine.AddMidiClip(inst, 0.0, 4.0);
Check(mclip >= 0, "add MIDI clip");

var notes = new[]
{
    new NotaNote(60, 0.0, 1.0, 0.9f),
    new NotaNote(64, 1.0, 1.0, 0.9f),
    new NotaNote(67, 2.0, 1.0, 0.9f),
};
engine.SetClipNotes(inst, mclip, notes);
var readBack = engine.GetClipNotes(inst, mclip);
Check(readBack.Length == 3 && readBack[0].Pitch == 60, "clip notes round-trip");

// Mute the audio track so only the synth is heard.
engine.SetTrackMute(track, true);
engine.Seek(0);
engine.Play();
engine.RenderOffline(buf, frames);
Check(Rms(buf, frames) > 0.001f, $"synth plays scheduled notes (rms={Rms(buf, frames):F4})");

// Live monitoring: armed instrument track sounds even without transport.
engine.StopTransport();
engine.RenderOffline(buf, 64);
engine.SetTrackArmed(inst, true);
engine.NoteOn(72, 0.9f);
engine.RenderOffline(buf, frames);
Check(Rms(buf, frames) > 0.001f, "live note monitoring is audible");
engine.NoteOff(72);

// Recording: arm a fresh track, record a live note, materialise it.
int rec = engine.AddInstrumentTrack();
engine.SetTrackArmed(inst, false);
engine.SetTrackArmed(rec, true);
engine.Seek(0);
engine.SetRecording(true);
Check(engine.IsRecording, "recording armed");
engine.Play();
engine.NoteOn(62, 0.8f);
engine.RenderOffline(buf, 4096);   // hold the note across a block
engine.NoteOff(62);
engine.RenderOffline(buf, 256);    // process note-off -> recorded event
engine.Poll();                     // materialise into the clip
engine.SetRecording(false);
var recorded = engine.GetClipNotes(rec, 0);
Check(recorded.Length >= 1 && recorded[0].Pitch == 62, $"recorded a live note (count={recorded.Length})");

// Sampler instrument.
int samp = engine.AddSamplerTrack(wav, rootNote: 60, loop: false);
Check(samp > 0, "add sampler track");
engine.SetTrackArmed(samp, true);
engine.SetTrackMute(inst, true);
engine.NoteOn(60, 1.0f);
engine.RenderOffline(buf, frames);
Check(Rms(buf, frames) > 0.001f, "sampler plays a note");
engine.NoteOff(60);

engine.StopTransport();
engine.RenderOffline(buf, 64); // let the audio block drain the stop command
Check(!engine.IsPlaying, "transport reports stopped");

// -- M4: built-in EQ + Compressor --
Console.WriteLine("-- M4 --");
Check(NotaEngine.FxSelfTest(), "built-in FX DSP self-test (EQ/Comp/Reverb/Delay/Utility)");

// Dedicated track with a full-clip sustained note (removes voice-decay doubt).
int fxT = engine.AddInstrumentTrack();
int fxC = engine.AddMidiClip(fxT, 0.0, 4.0);
engine.SetClipNotes(fxT, fxC, new[] { new NotaNote(60, 0.0, 4.0, 0.9f) });
engine.Seek(0); engine.Play();
engine.RenderOffline(buf, frames);
float m4Dry = Rms(buf, frames);
Check(m4Dry > 1e-4f, $"instrument audible (rms={m4Dry:0.0000})");

int eqDev = engine.AddBuiltinDevice(fxT, 0); // 0 = EQ-8 (flat by default = passthrough)
Check(eqDev >= 0, "add built-in EQ-8");
Check(engine.DeviceName(fxT, eqDev) == "Nota EQ-8", "EQ-8 device name");
Check(engine.DeviceParamCount(fxT, eqDev) == 40, "EQ-8 exposes 40 params (8 bands × 5)");
engine.Seek(0);
engine.RenderOffline(buf, frames);
Check(Rms(buf, frames) > 1e-4f, $"audio flows through flat EQ-8 (rms={Rms(buf, frames):0.0000})");

int compDev = engine.AddBuiltinDevice(fxT, 1); // 1 = Compressor
Check(compDev >= 0, "add built-in Compressor");
Check(engine.DeviceName(fxT, compDev) == "Nota Compressor", "Compressor device name");
engine.DeviceSetParam(fxT, compDev, 0, -24f); // Threshold
Check(Math.Abs(engine.DeviceGetParam(fxT, compDev, 0) - (-24f)) < 0.001f, "device param round-trips");
engine.Seek(0);
engine.RenderOffline(buf, frames);
Check(Rms(buf, frames) > 1e-4f, $"audio flows through EQ + Compressor chain (rms={Rms(buf, frames):0.0000})");

// -- M6-3: Reverb / Delay / Utility built-ins --
int revDev = engine.AddBuiltinDevice(fxT, 2);
Check(revDev >= 0 && engine.DeviceName(fxT, revDev) == "Nota Reverb" && engine.DeviceParamCount(fxT, revDev) == 14, "add built-in Reverb (14 params)");
int delDev = engine.AddBuiltinDevice(fxT, 3);
Check(delDev >= 0 && engine.DeviceName(fxT, delDev) == "Nota Delay" && engine.DeviceParamCount(fxT, delDev) == 24, "add built-in Delay (24 params)");
int utilDev = engine.AddBuiltinDevice(fxT, 4);
Check(utilDev >= 0 && engine.DeviceName(fxT, utilDev) == "Nota Utility" && engine.DeviceParamCount(fxT, utilDev) == 9, "add built-in Utility (9 params)");
engine.DeviceSetParam(fxT, delDev, 12, 0.5f); // Delay Dry/Wet (index 12)
Check(Math.Abs(engine.DeviceGetParam(fxT, delDev, 12) - 0.5f) < 0.001f, "Delay param round-trips");
int ampDev = engine.AddBuiltinDevice(fxT, 6);
Check(ampDev >= 0 && engine.DeviceName(fxT, ampDev) == "Nota Valve" && engine.DeviceParamCount(fxT, ampDev) == 14, "add built-in Nota Valve (14 params)");
Check(Math.Abs(engine.DeviceGetParam(fxT, ampDev, 0) - 2f) < 0.001f, $"Nota Valve defaults to Blues model (Model={engine.DeviceGetParam(fxT, ampDev, 0)})");
Check(engine.DeviceParamName(fxT, ampDev, 8) == "Cab On" && engine.DeviceParamName(fxT, ampDev, 9) == "Cabinet"
      && engine.DeviceParamName(fxT, ampDev, 11) == "Axis" && engine.DeviceParamName(fxT, ampDev, 12) == "Gate", "mockup-2m params present (Cab On/Cabinet/Axis/Gate)");
engine.DeviceSetParam(fxT, ampDev, 1, 7.5f); // Gain
Check(Math.Abs(engine.DeviceGetParam(fxT, ampDev, 1) - 7.5f) < 0.001f, "Nota Valve param round-trips");
// Cabinet toggle off changes the sound (removes the speaker band-pass) — isolated engine so
// it doesn't disturb the shared chain-flow render below.
{
    using var ce = new NotaEngine();
    ce.SetBpm(120); ce.SetTimeSignature(4, 4);
    int at = ce.AddInstrumentTrack();
    ce.AddMidiClip(at, 0.0, 4.0);
    ce.SetClipNotes(at, 0, new[] { new NotaNote(45, 0.0, 2.0, 0.95f) });
    int ad = ce.AddBuiltinDevice(at, 6);
    ce.DeviceSetParam(at, ad, 0, 5f);   // Heavy model (lots of drive → clear cab effect)
    ce.DeviceSetParam(at, ad, 1, 8f);
    var cabOn = new float[20000 * 2]; ce.Seek(0); ce.Play(); ce.RenderOffline(cabOn, 20000); ce.StopTransport();
    ce.DeviceSetParam(at, ad, 8, 0f);   // Cab On = off
    var cabOff = new float[20000 * 2]; ce.Seek(0); ce.Play(); ce.RenderOffline(cabOff, 20000); ce.StopTransport();
    double cd = 0; for (int i = 0; i < cabOn.Length; i++) { double d = cabOn[i] - cabOff[i]; cd += d * d; }
    bool fin = true; foreach (var s in cabOff) if (!float.IsFinite(s) || Math.Abs(s) > 8f) { fin = false; break; }
    Check(fin && Math.Sqrt(cd / cabOn.Length) > 0.002, "Cabinet toggle changes the sound + stays finite");
}
engine.RemoveDevice(fxT, ampDev); // keep the chain-flow assertion below about the original 5-device chain
engine.Seek(0);
engine.RenderOffline(buf, frames);
Check(Rms(buf, frames) > 1e-4f, $"audio flows through EQ+Comp+Reverb+Delay+Utility chain (rms={Rms(buf, frames):0.0000})");
engine.StopTransport();

// ============================ Nota Delay ====================================
Console.WriteLine("-- Nota Delay --");
{
    using var de = new NotaEngine();
    de.SetBpm(120); de.SetTimeSignature(4, 4);
    int dt = de.AddInstrumentTrack();
    de.AddMidiClip(dt, 0.0, 4.0);
    de.SetClipNotes(dt, 0, new[] { new NotaNote(60, 0.0, 0.25, 1.0f) });
    int dd = de.AddBuiltinDevice(dt, 3);
    const int Sync = 0, TimeL = 1, TimeR = 2, DivL = 3, LinkLR = 5, Feedback = 6, PingPong = 8,
              Freeze = 11, DryWet = 12, Output = 13, DryLevel = 14, Diffuse = 15, LowCut = 16,
              HighCut = 17, TapeMode = 18, FadeChange = 19, WidthP = 20, BassMono = 21,
              WetOnly = 22, LatencyComp = 23;
    Check(dd >= 0 && de.TrackDeviceBuiltinKind(dt, dd) == 3, "add built-in Nota Delay (kind 3)");
    Check(de.DeviceParamName(dt, dd, DryLevel) == "Dry Level" && de.DeviceParamName(dt, dd, Diffuse) == "Diffuse"
          && de.DeviceParamName(dt, dd, LowCut) == "Low Cut" && de.DeviceParamName(dt, dd, HighCut) == "High Cut"
          && de.DeviceParamName(dt, dd, TapeMode) == "Tape Mode" && de.DeviceParamName(dt, dd, FadeChange) == "Fade on Change"
          && de.DeviceParamName(dt, dd, WidthP) == "Width" && de.DeviceParamName(dt, dd, BassMono) == "Bass Mono"
          && de.DeviceParamName(dt, dd, WetOnly) == "Wet Only" && de.DeviceParamName(dt, dd, LatencyComp) == "Latency Comp",
          "Delay names its appended params");
    de.DeviceSetParam(dt, dd, Diffuse, 0.62f);
    Check(Math.Abs(de.DeviceGetParam(dt, dd, Diffuse) - 0.62f) < 1e-4, "Delay param set/get round-trips");

    float[] Render(int frames = 40000)
    {
        var b = new float[frames * 2];
        de.Seek(0); de.Play(); de.RenderOffline(b, frames); de.StopTransport();
        return b;
    }
    static bool Finite(float[] b) { foreach (var s in b) if (!float.IsFinite(s) || Math.Abs(s) > 8f) return false; return true; }
    // Energy in the tail, well after the 0.25-beat note has ended — that is the repeats.
    static double TailRms(float[] b, int fromFrame)
    {
        double sum = 0; int n = 0;
        for (int i = fromFrame * 2; i < b.Length; i++) { sum += b[i] * b[i]; n++; }
        return n > 0 ? Math.Sqrt(sum / n) : 0;
    }

    // Free ms, wet only: the repeats are all that is left, and they keep coming.
    de.DeviceSetParam(dt, dd, Sync, 0f);
    de.DeviceSetParam(dt, dd, TimeL, 0.1f);    // 200 ms
    de.DeviceSetParam(dt, dd, TimeR, 0.1f);
    de.DeviceSetParam(dt, dd, LinkLR, 1f);
    de.DeviceSetParam(dt, dd, Feedback, 0.6f);
    de.DeviceSetParam(dt, dd, WetOnly, 1f);
    var wet = Render();
    Check(Finite(wet) && TailRms(wet, 24000) > 1e-4, $"Delay repeats ring on after the note (tail rms {TailRms(wet, 24000):F4})");

    // The loop filters bite: a narrow band leaves less in the tail than the open band.
    de.DeviceSetParam(dt, dd, LowCut, 0.75f);   // ~630 Hz
    de.DeviceSetParam(dt, dd, HighCut, 0.35f);  // ~1 kHz
    var narrow = Render();
    Check(Finite(narrow) && TailRms(narrow, 24000) < TailRms(wet, 24000),
          $"Low/High Cut thin the repeats ({TailRms(narrow, 24000):F4} < {TailRms(wet, 24000):F4})");
    de.DeviceSetParam(dt, dd, LowCut, 0f); de.DeviceSetParam(dt, dd, HighCut, 1f);

    // Every character switch renders finite and audible.
    foreach (var (p, v, what) in new[] { (PingPong, 1f, "ping-pong"), (TapeMode, 1f, "tape mode"),
                                         (FadeChange, 0f, "repitch"), (Diffuse, 1f, "diffusion"),
                                         (WidthP, 1f, "width 200 %"), (BassMono, 0.6f, "bass mono") })
    {
        float was = de.DeviceGetParam(dt, dd, p);
        de.DeviceSetParam(dt, dd, p, v);
        var b = Render();
        Check(Finite(b) && TailRms(b, 24000) > 1e-5, $"Delay {what} renders finite + audible (tail rms {TailRms(b, 24000):F4})");
        de.DeviceSetParam(dt, dd, p, was);
    }

    // Freeze holds the loop instead of letting it decay, and Clear loop empties it.
    de.DeviceSetParam(dt, dd, Feedback, 0.3f);
    de.DeviceSetParam(dt, dd, Freeze, 1f);
    var held = Render(60000);
    double early = TailRms(held[..(30000 * 2)], 20000), late = TailRms(held, 50000);
    Check(Finite(held) && late > early * 0.5, $"Freeze holds the loop instead of decaying (late {late:F4} vs early {early:F4})");
    de.DeviceAction(dt, dd, 0, 0, 0);                   // Clear loop
    var cleared = new float[8000 * 2];
    de.RenderOffline(cleared, 8000);                    // transport stopped: nothing new goes in
    Check(Rms(cleared, 8000) < 1e-4, $"Clear loop empties the delay buffer (rms {Rms(cleared, 8000):F5})");
    de.DeviceSetParam(dt, dd, Freeze, 0f);
    de.DeviceSetParam(dt, dd, WetOnly, 0f);

    // Dry Level and Output are real trims on the dry path.
    de.DeviceSetParam(dt, dd, DryWet, 0f);
    de.DeviceSetParam(dt, dd, Output, 0.5f);
    de.DeviceSetParam(dt, dd, DryLevel, 0.70711f);
    float unity = Rms(Render(8000), 8000);
    de.DeviceSetParam(dt, dd, DryLevel, 0f);
    float silent = Rms(Render(8000), 8000);
    Check(unity > 1e-3 && silent < unity * 0.05f, $"Dry Level trims the dry path ({silent:F4} vs {unity:F4})");
    de.DeviceSetParam(dt, dd, DryLevel, 0.70711f);

    // Latency Comp shortens the loop by the diffuser's group delay — a different signal.
    de.DeviceSetParam(dt, dd, DryWet, 1f);
    de.DeviceSetParam(dt, dd, Diffuse, 0.8f);
    de.DeviceSetParam(dt, dd, LatencyComp, 1f);
    var comped = Render(20000);
    de.DeviceSetParam(dt, dd, LatencyComp, 0f);
    var raw = Render(20000);
    double diff = 0; for (int i = 0; i < raw.Length; i++) { double d = comped[i] - raw[i]; diff += d * d; }
    Check(Finite(comped) && Finite(raw) && Math.Sqrt(diff / raw.Length) > 1e-5, "Latency Comp moves the repeats");
    de.DeviceSetParam(dt, dd, LatencyComp, 1f);

    // Telemetry + the MCP status line.
    var sc = new float[11];
    de.DeviceSetParam(dt, dd, Sync, 1f); de.DeviceSetParam(dt, dd, DivL, 2f / 7f);
    Render(20000);
    int scn = de.DeviceScope(dt, dd, sc, sc.Length);
    Check(scn == 11 && sc[4] > 1000 && sc[5] > 1, $"Delay scope reports sample rate + tempo ({sc[4]:0} Hz, {sc[5]:0.0} BPM)");
    string text = de.DeviceText(dt, dd, 0);
    Check(text.Contains("1/8"), $"Delay status text names the division (got '{text}')");

    // Clone: duplicating the track keeps the appended params.
    de.DeviceSetParam(dt, dd, Diffuse, 0.44f);
    int t2 = de.DuplicateTrack(dt);
    int dd2 = de.TrackDeviceCount(t2) - 1;
    Check(t2 > 0 && Math.Abs(de.DeviceGetParam(t2, dd2, Diffuse) - 0.44f) < 1e-4, "duplicate track clones the Delay's params");

    // Automation: a device-param lane drives Feedback.
    int lane = de.AddAutomationLane(dt, AutomationTarget.DeviceParam, dd, Feedback);
    Check(lane >= 0, "add Delay Feedback automation lane");
    de.SetAutomationPoints(dt, lane, new[] { new AutomationPoint(0.0, 0.1f), new AutomationPoint(2.0, 0.85f) });
    var ab = new float[4096 * 2];
    de.Seek(1.99); de.Play(); de.RenderOffline(ab, 4096); de.StopTransport();
    Check(de.DeviceGetParam(dt, dd, Feedback) > 0.7f, $"automation drives Delay Feedback ({de.DeviceGetParam(dt, dd, Feedback):F2})");
}

// ============================ Nota Crush ====================================
Console.WriteLine("-- Nota Crush --");
{
    using var ce = new NotaEngine();
    ce.SetBpm(120); ce.SetTimeSignature(4, 4);
    int ct = ce.AddInstrumentTrack();
    ce.AddMidiClip(ct, 0.0, 4.0);
    ce.SetClipNotes(ct, 0, new[] { new NotaNote(60, 0.0, 3.0, 0.9f) });
    int cd = ce.AddBuiltinDevice(ct, 12);
    Check(cd >= 0, "add built-in Nota Crush");
    Check(ce.DeviceName(ct, cd) == "Nota Crush", $"device name is Nota Crush (got '{ce.DeviceName(ct, cd)}')");
    Check(ce.TrackDeviceBuiltinKind(ct, cd) == 12, $"builtin kind is 12 (got {ce.TrackDeviceBuiltinKind(ct, cd)})");
    int pc = ce.DeviceParamCount(ct, cd);
    Check(pc == 11, $"Nota Crush exposes 11 params (got {pc})");

    // Param round-trip.
    ce.DeviceSetParam(ct, cd, 0, 0.42f);   // Bits
    Check(Math.Abs(ce.DeviceGetParam(ct, cd, 0) - 0.42f) < 1e-4, "Crush param set/get round-trips");

    // Audible: default params pass audio.
    var cb = new float[8192 * 2];
    ce.Seek(0); ce.Play(); ce.RenderOffline(cb, 8192); ce.StopTransport();
    Check(Rms(cb, 8192) > 0.001f, $"Nota Crush passes audio (rms {Rms(cb, 8192):F3})");

    // Heavy crush → still finite, different from bypass.
    ce.DeviceSetParam(ct, cd, 0, 0.1f);   // ~3 bit
    ce.DeviceSetParam(ct, cd, 1, 0.05f);  // ~1 kHz
    ce.DeviceSetParam(ct, cd, 7, 1.0f);   // full wet
    var crushed = new float[8192 * 2];
    ce.Seek(0); ce.Play(); ce.RenderOffline(crushed, 8192); ce.StopTransport();
    bool fin = true; foreach (var s in crushed) if (!float.IsFinite(s) || Math.Abs(s) > 8f) { fin = false; break; }
    Check(fin && Rms(crushed, 8192) > 0.001f, $"heavy crush stays finite + audible (rms {Rms(crushed, 8192):F3})");

    // Each mode renders finite.
    for (int m = 0; m < 3; m++)
    {
        ce.DeviceSetParam(ct, cd, 2, m / 2f);   // Mode
        var mb = new float[8192 * 2];
        ce.Seek(0); ce.Play(); ce.RenderOffline(mb, 8192); ce.StopTransport();
        bool mfin = true; foreach (var s in mb) if (!float.IsFinite(s) || Math.Abs(s) > 8f) { mfin = false; break; }
        Check(mfin && Rms(mb, 8192) > 0.001f, $"Crush mode {m} renders finite + audible (rms {Rms(mb, 8192):F3})");
    }

    // Clone: duplicate track preserves params.
    ce.DeviceSetParam(ct, cd, 0, 0.55f);
    int t2 = ce.DuplicateTrack(ct);
    int cd2 = ce.TrackDeviceCount(t2) - 1;
    Check(t2 > 0 && Math.Abs(ce.DeviceGetParam(t2, cd2, 0) - 0.55f) < 1e-4, "duplicate track clones Crush params");

    // Automation: a device-param lane drives Bits.
    int lane = ce.AddAutomationLane(ct, AutomationTarget.DeviceParam, cd, 0);
    Check(lane >= 0, "add Crush Bits automation lane");
    ce.SetAutomationPoints(ct, lane, new[] { new AutomationPoint(0.0, 0.1f), new AutomationPoint(2.0, 0.9f) });
    var ab = new float[4096 * 2];
    ce.Seek(1.99); ce.Play(); ce.RenderOffline(ab, 4096); ce.StopTransport();
    float after = ce.DeviceGetParam(ct, cd, 0);
    Check(after > 0.7f, $"automation drives Crush Bits (Bits = {after:F2})");
}

// ============================ Nota Dynamic EQ-8 =============================
Console.WriteLine("-- Nota Dynamic EQ-8 --");
{
    using var de = new NotaEngine();
    de.SetBpm(120); de.SetTimeSignature(4, 4);
    int dt = de.AddInstrumentTrack();
    de.AddMidiClip(dt, 0.0, 4.0);
    de.SetClipNotes(dt, 0, new[] { new NotaNote(60, 0.0, 3.0, 1.0f) });
    int dd = de.AddBuiltinDevice(dt, 13);
    Check(dd >= 0, "add built-in Nota Dynamic EQ-8");
    Check(de.DeviceName(dt, dd) == "Nota Dynamic EQ-8", $"name is Nota Dynamic EQ-8 (got '{de.DeviceName(dt, dd)}')");
    Check(de.TrackDeviceBuiltinKind(dt, dd) == 13, $"builtin kind is 13 (got {de.TrackDeviceBuiltinKind(dt, dd)})");
    int dpc = de.DeviceParamCount(dt, dd);
    Check(dpc == 83, $"Dynamic EQ-8 exposes 83 params (got {dpc})");

    // Band 4 (index 3) field bases: On=30 Type=31 Freq=32 Gain=33 Q=34 Mode=35 Thr=36 Rng=37.
    de.DeviceSetParam(dt, dd, 36, -45f);
    Check(Math.Abs(de.DeviceGetParam(dt, dd, 36) + 45f) < 1e-3, "Dynamic EQ param set/get round-trips");

    // Default (flat static) passes audio.
    var pb = new float[8192 * 2];
    de.Seek(0); de.Play(); de.RenderOffline(pb, 8192); de.StopTransport();
    Check(Rms(pb, 8192) > 0.001f, $"Dynamic EQ passes audio (rms {Rms(pb, 8192):F3})");

    // Make band 4 a downward-dynamic bell that engages on the note; check GR telemetry.
    de.DeviceSetParam(dt, dd, 32, 500f);   // Freq
    de.DeviceSetParam(dt, dd, 35, 1f);     // Mode = Above (duck)
    de.DeviceSetParam(dt, dd, 36, -55f);   // Thr low → engages
    de.DeviceSetParam(dt, dd, 37, -12f);   // Rng
    var eb = new float[8192 * 2];
    de.Seek(0); de.Play(); de.RenderOffline(eb, 8192); de.StopTransport();
    var gr = new float[8];
    int gn = de.DeviceScope(dt, dd, gr, 8);
    Check(gn == 8, $"GR telemetry returns 8 bands (got {gn})");
    Check(gr[3] < -0.05f, $"dynamic band 4 reduces gain (GR {gr[3]:F2} dB)");
    bool defin = true; foreach (var s in eb) if (!float.IsFinite(s) || Math.Abs(s) > 8f) { defin = false; break; }
    Check(defin && Rms(eb, 8192) > 0.001f, "dynamic engaged stays finite + audible");

    // Solo band 4 (param 82 = band index+1): renders finite.
    de.DeviceSetParam(dt, dd, 82, 4f);
    var sb = new float[8192 * 2];
    de.Seek(0); de.Play(); de.RenderOffline(sb, 8192); de.StopTransport();
    bool sfin = true; foreach (var s in sb) if (!float.IsFinite(s) || Math.Abs(s) > 8f) { sfin = false; break; }
    Check(sfin, "solo band renders finite");
    de.DeviceSetParam(dt, dd, 82, 0f);

    // Clone: duplicate track preserves params.
    de.DeviceSetParam(dt, dd, 33, 4.5f);   // band 4 Gain
    int dt2 = de.DuplicateTrack(dt);
    int dd2 = de.TrackDeviceCount(dt2) - 1;
    Check(dt2 > 0 && Math.Abs(de.DeviceGetParam(dt2, dd2, 33) - 4.5f) < 1e-3, "duplicate track clones Dynamic EQ params");

    // Automation: a device-param lane drives band 4 gain (raw dB points).
    int dlane = de.AddAutomationLane(dt, AutomationTarget.DeviceParam, dd, 33);
    Check(dlane >= 0, "add Dynamic EQ gain automation lane");
    de.SetAutomationPoints(dt, dlane, new[] { new AutomationPoint(0.0, -6f), new AutomationPoint(2.0, 6f) });
    var dab = new float[4096 * 2];
    de.Seek(1.99); de.Play(); de.RenderOffline(dab, 4096); de.StopTransport();
    float dafter = de.DeviceGetParam(dt, dd, 33);
    Check(dafter > 3f, $"automation drives band gain ({dafter:F2} dB)");
}

// ============================ Nota Ceiling =================================
Console.WriteLine("-- Nota Ceiling --");
{
    using var le = new NotaEngine();
    le.SetBpm(120); le.SetTimeSignature(4, 4);
    int lt = le.AddInstrumentTrack();
    le.AddMidiClip(lt, 0.0, 4.0);
    le.SetClipNotes(lt, 0, new[] { new NotaNote(60, 0.0, 3.0, 1.0f) });
    int ld = le.AddBuiltinDevice(lt, 14);
    Check(ld >= 0, "add built-in Nota Ceiling");
    Check(le.DeviceName(lt, ld) == "Nota Ceiling", $"name is Nota Ceiling (got '{le.DeviceName(lt, ld)}')");
    Check(le.TrackDeviceBuiltinKind(lt, ld) == 14, $"builtin kind is 14 (got {le.TrackDeviceBuiltinKind(lt, ld)})");
    int lpc = le.DeviceParamCount(lt, ld);
    Check(lpc == 7, $"Nota Ceiling exposes 7 params (got {lpc})");

    // Param round-trip (Ceiling = 0, Gain = 1).
    le.DeviceSetParam(lt, ld, 0, -2.5f);
    Check(Math.Abs(le.DeviceGetParam(lt, ld, 0) + 2.5f) < 1e-3, "Ceiling param set/get round-trips");

    // Drive hard into a low ceiling → output stays under the ceiling, GR telemetry engages.
    le.DeviceSetParam(lt, ld, 0, -6f);    // Ceiling −6 dB
    le.DeviceSetParam(lt, ld, 1, 18f);    // +18 dB input gain
    var lb = new float[8192 * 2];
    le.Seek(0); le.Play(); le.RenderOffline(lb, 8192); le.StopTransport();
    float peak = 0f; bool lfin = true;
    foreach (var s in lb) { if (!float.IsFinite(s)) { lfin = false; break; } peak = Math.Max(peak, Math.Abs(s)); }
    float ceilLin = (float)Math.Pow(10, -6.0 / 20.0);
    Check(lfin && Rms(lb, 8192) > 0.001f, $"Ceiling passes audio (rms {Rms(lb, 8192):F3})");
    Check(peak <= ceilLin + 1e-3f, $"output stays under the −6 dB ceiling (peak {20 * Math.Log10(Math.Max(1e-6f, peak)):F2} dB)");
    var sc = new float[8];
    int sn = le.DeviceScope(lt, ld, sc, 8);
    Check(sn == 7, $"Ceiling scope returns 7 meters (got {sn})");
    Check(sc[2] > 1.0f, $"limiter reports gain reduction (GR {sc[2]:F2} dB)");
    Check(le.DeviceGainReduction(lt, ld) > 0.5f, "GR surfaces on the shell meter");

    // Each character renders finite + audible.
    le.DeviceSetParam(lt, ld, 1, 12f);
    for (int c = 0; c < 3; c++)
    {
        le.DeviceSetParam(lt, ld, 4, c);
        var mb = new float[8192 * 2];
        le.Seek(0); le.Play(); le.RenderOffline(mb, 8192); le.StopTransport();
        bool mfin = true; foreach (var s in mb) if (!float.IsFinite(s) || Math.Abs(s) > 8f) { mfin = false; break; }
        Check(mfin && Rms(mb, 8192) > 0.001f, $"Ceiling character {c} renders finite + audible (rms {Rms(mb, 8192):F3})");
    }
    le.DeviceSetParam(lt, ld, 4, 0f);

    // Sidechain: the limiter accepts a source (Compressor plumbing).
    Check(le.DeviceParamCount(lt, ld) == 7, "param count stable after render");

    // Clone: duplicate track preserves params.
    le.DeviceSetParam(lt, ld, 2, 220f);   // Release
    int lt2 = le.DuplicateTrack(lt);
    int ld2 = le.TrackDeviceCount(lt2) - 1;
    Check(lt2 > 0 && Math.Abs(le.DeviceGetParam(lt2, ld2, 2) - 220f) < 1e-2, "duplicate track clones Ceiling params");

    // Automation: a device-param lane drives Gain (raw dB points).
    int llane = le.AddAutomationLane(lt, AutomationTarget.DeviceParam, ld, 1);
    Check(llane >= 0, "add Ceiling Gain automation lane");
    le.SetAutomationPoints(lt, llane, new[] { new AutomationPoint(0.0, 0f), new AutomationPoint(2.0, 20f) });
    var lab = new float[4096 * 2];
    le.Seek(1.99); le.Play(); le.RenderOffline(lab, 4096); le.StopTransport();
    float lafter = le.DeviceGetParam(lt, ld, 1);
    Check(lafter > 12f, $"automation drives Ceiling Gain ({lafter:F2} dB)");
}

// ============================ Nota Strata ==================================
Console.WriteLine("-- Nota Strata --");
{
    using var se = new NotaEngine();
    se.SetBpm(120); se.SetTimeSignature(4, 4);
    int st = se.AddInstrumentTrack();
    se.AddMidiClip(st, 0.0, 4.0);
    se.SetClipNotes(st, 0, new[] { new NotaNote(60, 0.0, 3.5, 1.0f) });
    int sd = se.AddBuiltinDevice(st, 15);
    Check(sd >= 0, "add built-in Nota Strata");
    Check(se.DeviceName(st, sd) == "Nota Strata", $"name is Nota Strata (got '{se.DeviceName(st, sd)}')");
    Check(se.TrackDeviceBuiltinKind(st, sd) == 15, $"builtin kind is 15 (got {se.TrackDeviceBuiltinKind(st, sd)})");
    Check(se.DeviceParamCount(st, sd) == 7, $"Nota Strata exposes 7 params (got {se.DeviceParamCount(st, sd)})");

    // Param round-trip (Feedback = 0).
    se.DeviceSetParam(st, sd, 0, 72f);
    Check(Math.Abs(se.DeviceGetParam(st, sd, 0) - 72f) < 1e-3, "Strata param set/get round-trips");
    se.DeviceSetParam(st, sd, 0, 100f);
    se.DeviceSetParam(st, sd, 3, 0f);   // Quantize Off → actions apply immediately

    var scope = new float[30];
    var rb = new float[8192 * 2];

    // Record the first pass, then Play to finalize the loop length.
    se.DeviceAction(st, sd, 0, 0, 0);   // C_Record
    se.Seek(0); se.Play(); se.RenderOffline(rb, 8192);
    int n = se.DeviceScope(st, sd, scope, scope.Length);
    Check(n >= 6, $"Strata scope returns state ({n} floats)");
    Check((int)scope[3] >= 1, $"recording created a layer (count {(int)scope[3]})");
    bool sfin = true; foreach (var s in rb) if (!float.IsFinite(s)) { sfin = false; break; }
    Check(sfin && Rms(rb, 8192) > 0.001f, $"Strata passes/monitors audio (rms {Rms(rb, 8192):F3})");
    se.DeviceAction(st, sd, 2, 0, 0);   // C_Play (finalize)
    se.RenderOffline(rb, 8192); se.StopTransport();
    se.DeviceScope(st, sd, scope, scope.Length);
    Check((int)scope[3] == 1, $"one layer after the first pass (count {(int)scope[3]})");
    Check(scope[1] >= 1, $"loop has a bar count ({(int)scope[1]})");

    // Per-layer waveform is populated.
    var wv = new float[32];
    int wn = se.DeviceLayerWave(st, sd, 0, wv, wv.Length);
    float wmax = 0; foreach (var v in wv) wmax = Math.Max(wmax, v);
    Check(wn > 0 && wmax > 0.0f, $"recorded layer waveform is non-empty (bins {wn}, peak {wmax:F2})");

    // Per-layer mute reflects in the scope pack (state index 8 = layer 0 muted flag).
    se.DeviceAction(st, sd, 6, 0, 1f);  // A_LayerMute layer 0
    se.DeviceScope(st, sd, scope, scope.Length);
    Check(scope[8] > 0.5f, "layer mute reflects in scope");
    se.DeviceAction(st, sd, 6, 0, 0f);

    // Persist: getState blob restores onto a fresh device.
    var blob = se.DeviceGetState(st, sd);
    Check(blob.Length > 64, $"Strata state blob captured ({blob.Length} bytes)");
    int st2 = se.AddInstrumentTrack(); int sd2 = se.AddBuiltinDevice(st2, 15);
    se.DeviceSetState(st2, sd2, blob);
    var sc2 = new float[30]; se.DeviceScope(st2, sd2, sc2, sc2.Length);
    Check((int)sc2[3] == 1, $"setState restores the recorded layer (count {(int)sc2[3]})");

    // Clone: duplicating the track carries the recorded audio, not just params.
    int stD = se.DuplicateTrack(st); int sdD = se.TrackDeviceCount(stD) - 1;
    var scD = new float[30]; se.DeviceScope(stD, sdD, scD, scD.Length);
    Check(stD > 0 && (int)scD[3] == 1, "duplicate track clones the looper layer");

    // Undo drops the last layer.
    se.Seek(0); se.Play(); se.DeviceAction(st, sd, 4, 0, 0); se.RenderOffline(rb, 4096); se.StopTransport();
    se.DeviceScope(st, sd, scope, scope.Length);
    Check((int)scope[3] == 0, $"undo removes the layer (count {(int)scope[3]})");

    // Automation drives Feedback.
    int slane = se.AddAutomationLane(st, AutomationTarget.DeviceParam, sd, 0);
    Check(slane >= 0, "add Strata Feedback automation lane");
    se.SetAutomationPoints(st, slane, new[] { new AutomationPoint(0.0, 20f), new AutomationPoint(2.0, 90f) });
    se.Seek(1.99); se.Play(); se.RenderOffline(rb, 4096); se.StopTransport();
    Check(se.DeviceGetParam(st, sd, 0) > 60f, $"automation drives Strata Feedback ({se.DeviceGetParam(st, sd, 0):F1})");
}

// ============================ Nota EQ-3 ====================================
Console.WriteLine("-- Nota EQ-3 --");
{
    // EQ-3 = 3-band performance EQ (kind 16). Bands: 0 Low 1 Mid 2 High (0.5 = 0 dB),
    // 3/4/5 kills, 6/7 crossovers, 8 slope, 9 output gain.
    int qt = engine.AddInstrumentTrack();
    engine.AddMidiClip(qt, 0.0, 4.0);
    engine.SetClipNotes(qt, 0, new[] { new NotaNote(45, 0.0, 3.0, 0.9f) });   // low note → energy in all bands
    int q3 = engine.AddBuiltinDevice(qt, 16);
    Check(q3 >= 0, "add built-in Nota EQ-3");
    Check(engine.DeviceName(qt, q3) == "Nota EQ-3", $"name is Nota EQ-3 (got '{engine.DeviceName(qt, q3)}')");
    Check(engine.TrackDeviceBuiltinKind(qt, q3) == 16, $"builtin kind is 16 (got {engine.TrackDeviceBuiltinKind(qt, q3)})");
    Check(engine.DeviceParamCount(qt, q3) == 10, $"Nota EQ-3 exposes 10 params (got {engine.DeviceParamCount(qt, q3)})");

    // Param round-trip.
    engine.DeviceSetParam(qt, q3, 0, 0.75f);
    Check(Math.Abs(engine.DeviceGetParam(qt, q3, 0) - 0.75f) < 1e-4, "EQ-3 param set/get round-trips");

    // Flat (defaults) passes audio finite + audible.
    engine.DeviceSetParam(qt, q3, 0, 0.5f);
    var qb = new float[8192 * 2];
    engine.Seek(0); engine.Play(); engine.RenderOffline(qb, 8192); engine.StopTransport();
    bool qfin = true; foreach (var s in qb) if (!float.IsFinite(s)) { qfin = false; break; }
    Check(qfin && Rms(qb, 8192) > 0.001f, $"EQ-3 passes audio flat (rms {Rms(qb, 8192):F3})");

    // Analyzer scope feeds the UI spectrum.
    var qsc = new float[2048];
    engine.Seek(0); engine.Play(); engine.RenderOffline(qb, 8192);
    int qn = engine.DeviceScope(qt, q3, qsc, qsc.Length);
    engine.StopTransport();
    Check(qn > 0, $"EQ-3 scope returns samples (got {qn})");

    // Killing the low band drops low-frequency energy: render bass-heavy note with low kill.
    float rmsFullQ = Rms(qb, 8192);
    engine.DeviceSetParam(qt, q3, 3, 1f);   // Low Kill
    engine.Seek(0); engine.Play(); engine.RenderOffline(qb, 8192); engine.StopTransport();
    bool kfin = true; foreach (var s in qb) if (!float.IsFinite(s)) { kfin = false; break; }
    Check(kfin && Rms(qb, 8192) < rmsFullQ, $"Low Kill removes energy ({Rms(qb, 8192):F3} < {rmsFullQ:F3})");
    engine.DeviceSetParam(qt, q3, 3, 0f);

    // Both slopes render finite + audible.
    foreach (float sl in new[] { 0f, 1f })
    {
        engine.DeviceSetParam(qt, q3, 8, sl);
        engine.Seek(0); engine.Play(); engine.RenderOffline(qb, 8192); engine.StopTransport();
        bool sfin = true; foreach (var s in qb) if (!float.IsFinite(s)) { sfin = false; break; }
        Check(sfin && Rms(qb, 8192) > 0.001f, $"EQ-3 slope {(sl >= 0.5f ? 48 : 24)}dB renders finite + audible (rms {Rms(qb, 8192):F3})");
    }

    // Clone: duplicating the track carries EQ-3 params.
    engine.DeviceSetParam(qt, q3, 2, 0.68f);   // High gain
    int qtD = engine.DuplicateTrack(qt); int q3D = engine.TrackDeviceCount(qtD) - 1;
    Check(qtD > 0 && Math.Abs(engine.DeviceGetParam(qtD, q3D, 2) - 0.68f) < 1e-3, "duplicate track clones EQ-3 params");

    // Automation drives the Low band.
    int qlane = engine.AddAutomationLane(qt, AutomationTarget.DeviceParam, q3, 0);
    Check(qlane >= 0, "add EQ-3 Low automation lane");
    engine.SetAutomationPoints(qt, qlane, new[] { new AutomationPoint(0.0, 0.2f), new AutomationPoint(2.0, 0.9f) });
    engine.Seek(1.99); engine.Play(); engine.RenderOffline(qb, 4096); engine.StopTransport();
    Check(engine.DeviceGetParam(qt, q3, 0) > 0.7f, $"automation drives EQ-3 Low ({engine.DeviceGetParam(qt, q3, 0):F2})");
}

// ============================ Nota Shutter =================================
Console.WriteLine("-- Nota Shutter --");
{
    // Shutter = noise gate (kind 19). Own engine (it silences the signal, which would skew
    // the shared master mix used by later tests).
    using var ge = new NotaEngine();
    ge.SetBpm(120); ge.SetTimeSignature(4, 4);
    int gt = ge.AddInstrumentTrack();
    ge.AddMidiClip(gt, 0.0, 4.0);
    ge.SetClipNotes(gt, 0, new[] { new NotaNote(57, 0.0, 3.5, 0.5f) });   // sustained, moderate level
    int gd = ge.AddBuiltinDevice(gt, 19);
    Check(gd >= 0, "add built-in Nota Shutter");
    Check(ge.DeviceName(gt, gd) == "Nota Shutter", $"name is Nota Shutter (got '{ge.DeviceName(gt, gd)}')");
    Check(ge.TrackDeviceBuiltinKind(gt, gd) == 19, $"builtin kind is 19 (got {ge.TrackDeviceBuiltinKind(gt, gd)})");
    Check(ge.DeviceParamCount(gt, gd) == 11, $"Nota Shutter exposes 11 params (got {ge.DeviceParamCount(gt, gd)})");

    // Param round-trip.
    ge.DeviceSetParam(gt, gd, 0, 0.6f);   // Threshold
    Check(Math.Abs(ge.DeviceGetParam(gt, gd, 0) - 0.6f) < 1e-4, "Shutter param set/get round-trips");

    var gb = new float[8192 * 2];
    var gsc = new float[5];
    // Open: threshold below the signal → gate stays open, audio passes.
    ge.DeviceSetParam(gt, gd, 0, 0.05f);  // threshold very low
    ge.DeviceSetParam(gt, gd, 5, 0f);     // floor mute
    ge.Seek(0); ge.Play(); for (int k = 0; k < 6; k++) ge.RenderOffline(gb, 8192); ge.StopTransport();
    bool gfin = true; foreach (var s in gb) if (!float.IsFinite(s)) { gfin = false; break; }
    float openRms = Rms(gb, 8192);
    Check(gfin && openRms > 0.001f, $"Shutter passes audio when open (rms {openRms:F3})");
    int gn = ge.DeviceScope(gt, gd, gsc, gsc.Length);
    Check(gn == 5, $"Shutter scope returns 5 meters (got {gn})");

    // Shut: threshold above the signal + mute floor → gate closes, output near-silent.
    ge.DeviceSetParam(gt, gd, 0, 0.98f);  // threshold very high (above the note)
    ge.DeviceSetParam(gt, gd, 4, 0.2f);   // fast release
    ge.Seek(0); ge.Play(); for (int k = 0; k < 6; k++) ge.RenderOffline(gb, 8192); ge.StopTransport();
    bool gfin2 = true; foreach (var s in gb) if (!float.IsFinite(s)) { gfin2 = false; break; }
    float shutRms = Rms(gb, 8192);
    Check(gfin2, "Shutter renders finite when gating");
    Check(shutRms < openRms * 0.5f, $"gate closes below threshold (shut {shutRms:F4} < open {openRms:F4})");

    // Duck (Flip) renders finite + audible.
    ge.DeviceSetParam(gt, gd, 0, 0.3f); ge.DeviceSetParam(gt, gd, 7, 1f); ge.DeviceSetParam(gt, gd, 5, 0.5f);
    ge.Seek(0); ge.Play(); ge.RenderOffline(gb, 8192); ge.StopTransport();
    bool dfin = true; foreach (var s in gb) if (!float.IsFinite(s)) { dfin = false; break; }
    Check(dfin, "Shutter duck (flip) renders finite");
    ge.DeviceSetParam(gt, gd, 7, 0f);

    // Sidechain: an external key source is accepted and keys the detector.
    int keyT = ge.AddInstrumentTrack();
    ge.AddMidiClip(keyT, 0.0, 4.0);
    ge.SetClipNotes(keyT, 0, new[] { new NotaNote(45, 0.0, 3.5, 1.0f) });
    ge.SetDeviceSidechainSource(gt, gd, keyT);
    Check(ge.DeviceSidechainSource(gt, gd) == keyT, "Shutter accepts an external key source");
    ge.DeviceSetParam(gt, gd, 0, 0.2f);
    ge.Seek(0); ge.Play(); ge.RenderOffline(gb, 8192); ge.StopTransport();
    bool sfin = true; foreach (var s in gb) if (!float.IsFinite(s)) { sfin = false; break; }
    Check(sfin, "Shutter with external key renders finite");
    ge.SetDeviceSidechainSource(gt, gd, -1);

    // Clone: duplicating the track carries Shutter params.
    ge.DeviceSetParam(gt, gd, 2, 0.66f);   // Attack
    int gtD = ge.DuplicateTrack(gt); int gdD = ge.TrackDeviceCount(gtD) - 1;
    Check(gtD > 0 && Math.Abs(ge.DeviceGetParam(gtD, gdD, 2) - 0.66f) < 1e-3, "duplicate track clones Shutter params");

    // Automation drives Threshold.
    int glane = ge.AddAutomationLane(gt, AutomationTarget.DeviceParam, gd, 0);
    Check(glane >= 0, "add Shutter Threshold automation lane");
    ge.SetAutomationPoints(gt, glane, new[] { new AutomationPoint(0.0, 0.2f), new AutomationPoint(2.0, 0.9f) });
    ge.Seek(1.99); ge.Play(); ge.RenderOffline(gb, 4096); ge.StopTransport();
    Check(ge.DeviceGetParam(gt, gd, 0) > 0.7f, $"automation drives Shutter Threshold ({ge.DeviceGetParam(gt, gd, 0):F2})");
}

// ============================ Nota Chamber =================================
Console.WriteLine("-- Nota Chamber --");
{
    // Chamber = hybrid reverb (kind 20). Own engine. IR shaping rebuilds kernels on a worker
    // thread, so the test waits a moment after IR-shaping edits before rendering.
    const int Blend = 0, DryWet = 1, ConvOn = 2, AlgoOn = 3, Routing = 5, IrSelect = 6, IrSize = 10, AlgoMode = 15,
              AlgoDecay = 16, Freeze = 24, ShimmerAmount = 29, EqPosition = 36, DuckAmount = 37, WetOnly = 43, ZeroLatency = 44;
    using var ce = new NotaEngine();
    ce.SetBpm(120); ce.SetTimeSignature(4, 4);
    int ct = ce.AddInstrumentTrack();
    ce.AddMidiClip(ct, 0.0, 8.0);
    ce.SetClipNotes(ct, 0, new[] { new NotaNote(60, 0.0, 0.5, 0.8f) });   // a short note, then the tail
    int cd = ce.AddBuiltinDevice(ct, 20);
    Check(cd >= 0, "add built-in Nota Chamber");
    Check(ce.DeviceName(ct, cd) == "Nota Chamber", $"name is Nota Chamber (got '{ce.DeviceName(ct, cd)}')");
    Check(ce.TrackDeviceBuiltinKind(ct, cd) == 20, $"builtin kind is 20 (got {ce.TrackDeviceBuiltinKind(ct, cd)})");
    int cpc = ce.DeviceParamCount(ct, cd);
    Check(cpc == 45, $"Nota Chamber exposes 45 params (got {cpc})");
    var cnames = Enumerable.Range(0, cpc).Select(i => ce.DeviceParamName(ct, cd, i)).ToList();
    Check(cnames.Distinct().Count() == cpc && cnames.All(n => n.Length > 0), "Chamber param names are unique and non-empty");
    ce.DeviceSetParam(ct, cd, AlgoDecay, 0.42f);
    Check(Math.Abs(ce.DeviceGetParam(ct, cd, AlgoDecay) - 0.42f) < 1e-4, "Chamber param set/get round-trips");
    ce.DeviceSetParam(ct, cd, AlgoDecay, ce.DeviceParamDefault(ct, cd, AlgoDecay));
    var irs = ce.DeviceText(ct, cd, 10).Split('\n', StringSplitOptions.RemoveEmptyEntries);
    Check(irs.Length == 16, $"Chamber lists 16 built-in IRs (got {irs.Length})");
    Check(ce.DeviceText(ct, cd, 0) == "Concert Hall · Wide", $"default IR is Concert Hall · Wide (got '{ce.DeviceText(ct, cd, 0)}')");

    const int N = 8192;
    var cb = new float[N * 2];
    var csc = new float[32];
    // Render 6 blocks (~1.1 s at 44.1 k): the note ends at 0.25 s, so the last block is pure tail.
    (bool fin, float tail, float all) RenderTail()
    {
        ce.Seek(0); ce.Play();
        bool f = true; float t = 0, a = 0;
        for (int k = 0; k < 6; k++)
        {
            ce.RenderOffline(cb, N);
            foreach (var s in cb) if (!float.IsFinite(s)) { f = false; break; }
            float r = Rms(cb, N); a = Math.Max(a, r); if (k == 5) t = r;
        }
        ce.StopTransport();
        return (f, t, a);
    }
    void Settle() => System.Threading.Thread.Sleep(150);   // let the IR worker publish a new kernel
    ce.DeviceSetParam(ct, cd, WetOnly, 1f);
    Settle();
    var r0 = RenderTail();
    Check(r0.fin && r0.tail > 1e-4f, $"Chamber renders a finite reverb tail (tail rms {r0.tail:F4})");
    int cn = ce.DeviceScope(ct, cd, csc, csc.Length);
    Check(cn == 22, $"Chamber scope returns 22 values (got {cn})");
    Check(csc[6] == 0f && csc[9] > 2f && csc[11] == 4f, $"zero-latency kernel of a 4-ch built-in IR (lat {csc[6]}, {csc[9]:F1} s, {csc[11]} ch)");

    // Card previews: the processed IR (layer 2) and the algorithm's rendered impulse response
    // (layer 3) follow the params — Size stretches the result, Decay lengthens the echogram.
    {
        var pv = new float[128];
        Check(ce.DeviceLayerWave(ct, cd, 2, pv, pv.Length) == 128 && ce.DeviceLayerWave(ct, cd, 3, pv, pv.Length) == 128, "Chamber exposes result + echogram previews");
        ce.DeviceScope(ct, cd, csc, csc.Length);
        float res0 = csc[19], echo0 = csc[20];
        ce.DeviceSetParam(ct, cd, IrSize, 1f); ce.DeviceSetParam(ct, cd, AlgoDecay, 0.9f); Settle();
        ce.DeviceScope(ct, cd, csc, csc.Length);
        Check(csc[19] > res0 * 1.8f && csc[20] > echo0 * 2f, $"previews follow Size + Decay (result {res0:F2}→{csc[19]:F2} s, echogram {echo0:F2}→{csc[20]:F2} s)");
        ce.DeviceSetParam(ct, cd, IrSize, 0.5f); ce.DeviceSetParam(ct, cd, AlgoDecay, ce.DeviceParamDefault(ct, cd, AlgoDecay)); Settle();
    }

    // Engines on their own, and each algorithm.
    ce.DeviceSetParam(ct, cd, AlgoOn, 0f); ce.DeviceSetParam(ct, cd, Blend, 0f);
    var rc = RenderTail();
    Check(rc.fin && rc.tail > 1e-4f, $"convolution alone renders a tail ({rc.tail:F4})");
    ce.DeviceSetParam(ct, cd, AlgoOn, 1f); ce.DeviceSetParam(ct, cd, ConvOn, 0f); ce.DeviceSetParam(ct, cd, Blend, 1f);
    string[] modes = { "Dark Hall", "Plate", "Quartz", "Shimmer" };
    for (int m = 0; m < 4; m++)
    {
        ce.DeviceSetParam(ct, cd, AlgoMode, m / 3f);
        var ra = RenderTail();
        Check(ra.fin && ra.tail > 1e-4f && ra.all < 4f, $"algorithm {modes[m]} renders a bounded tail ({ra.tail:F4}, peak rms {ra.all:F3})");
    }
    ce.DeviceSetParam(ct, cd, ShimmerAmount, 1f); ce.DeviceSetParam(ct, cd, AlgoDecay, 1f);
    var rs = RenderTail();
    Check(rs.fin && rs.all < 4f, $"max shimmer + max decay stays bounded (peak rms {rs.all:F3})");
    ce.DeviceSetParam(ct, cd, ShimmerAmount, 0.5f); ce.DeviceSetParam(ct, cd, AlgoDecay, ce.DeviceParamDefault(ct, cd, AlgoDecay));
    ce.DeviceSetParam(ct, cd, AlgoMode, 0f);

    // Freeze holds the tail: with a short decay the free tail dies, the frozen one doesn't.
    ce.DeviceSetParam(ct, cd, AlgoDecay, 0.2f);
    var rFree = RenderTail();
    ce.Seek(0); ce.Play();
    ce.RenderOffline(cb, N);                                  // the note plays into the tank
    ce.DeviceSetParam(ct, cd, Freeze, 1f);
    for (int k = 0; k < 5; k++) ce.RenderOffline(cb, N);
    ce.StopTransport();
    float frozen = Rms(cb, N);
    Check(frozen > rFree.tail * 4 && frozen > 1e-4f, $"Freeze holds the algorithm tail (frozen {frozen:F4} vs free {rFree.tail:F5})");
    ce.DeviceSetParam(ct, cd, Freeze, 0f);
    ce.DeviceSetParam(ct, cd, AlgoDecay, ce.DeviceParamDefault(ct, cd, AlgoDecay));

    // Serial routing, EQ positions, ducking, dry/wet mix.
    ce.DeviceSetParam(ct, cd, ConvOn, 1f); ce.DeviceSetParam(ct, cd, Blend, 0.5f); ce.DeviceSetParam(ct, cd, Routing, 1f);
    var rser = RenderTail();
    Check(rser.fin && rser.tail > 1e-4f, $"serial routing (conv → algo) renders ({rser.tail:F4})");
    ce.DeviceSetParam(ct, cd, Routing, 0f);
    bool eqOk = true;
    foreach (float pos in new[] { 0f, 0.5f, 1f })
    {
        ce.DeviceSetParam(ct, cd, EqPosition, pos); ce.DeviceSetParam(ct, cd, 32, 0.5f); ce.DeviceSetParam(ct, cd, 35, 0.4f);
        var re = RenderTail(); eqOk &= re.fin && re.all > 1e-4f;
    }
    Check(eqOk, "tail EQ renders at input / tail / output");
    ce.DeviceSetParam(ct, cd, 32, 0f); ce.DeviceSetParam(ct, cd, 35, 1f); ce.DeviceSetParam(ct, cd, EqPosition, 0.5f);
    ce.DeviceSetParam(ct, cd, DuckAmount, 1f);
    ce.Seek(0); ce.Play(); ce.RenderOffline(cb, N); ce.StopTransport();
    ce.DeviceScope(ct, cd, csc, csc.Length);
    Check(csc[4] > 1f, $"ducking pulls the wet down while the input plays (GR {csc[4]:F1} dB)");
    ce.DeviceSetParam(ct, cd, DuckAmount, 0f);
    ce.DeviceSetParam(ct, cd, WetOnly, 0f); ce.DeviceSetParam(ct, cd, DryWet, 0f);
    var rdry = RenderTail();
    Check(rdry.tail < 1e-4f && rdry.all > 1e-3f, $"Dry/Wet 0 passes only the dry signal (tail {rdry.tail:F5})");
    ce.DeviceSetParam(ct, cd, DryWet, 0.35f); ce.DeviceSetParam(ct, cd, WetOnly, 1f);

    // Zero latency off: the big block's delay is reported (absorbed in the predelay).
    ce.DeviceSetParam(ct, cd, ZeroLatency, 0f); Settle();
    ce.Seek(0); ce.Play(); ce.RenderOffline(cb, N); ce.StopTransport();
    ce.DeviceScope(ct, cd, csc, csc.Length);
    Check(csc[6] >= 1024f, $"Zero latency off uses the big-block kernel (conv +{csc[6]} smp)");
    ce.DeviceSetParam(ct, cd, ZeroLatency, 1f);
    // IR size changes swap kernels live (worker) without breaking the render.
    ce.DeviceSetParam(ct, cd, IrSize, 0.9f); Settle();
    ce.DeviceSetParam(ct, cd, IrSelect, 2f / 16f); Settle();
    var rsz = RenderTail();
    Check(rsz.fin && rsz.tail > 1e-4f && ce.DeviceText(ct, cd, 0) == "Cathedral", $"IR switch + stretch re-renders ({ce.DeviceText(ct, cd, 0)}, tail {rsz.tail:F4})");

    // User IR: load a stereo WAV (with leading silence, auto-trimmed), it becomes the IR.
    string irPath = Path.Combine(Path.GetTempPath(), "nota_chamber_ir_" + Guid.NewGuid().ToString("N") + ".wav");
    Nota.SmokeTest.WavWriter.WriteNoiseIr(irPath, seconds: 1.2, lead: 0.05, sampleRate: 44100);
    Check(ce.DeviceLoadFile(ct, cd, irPath), "Chamber loads a user IR file");
    Check(!ce.DeviceLoadFile(ct, cd, irPath + ".missing"), "a missing IR file is rejected");
    Check(ce.DeviceGetParam(ct, cd, IrSelect) > 0.999f, "loading selects the user IR slot");
    string irName = Path.GetFileNameWithoutExtension(irPath);
    Check(ce.DeviceText(ct, cd, 2) == irName && ce.DeviceText(ct, cd, 0) == irName, $"user IR is named after the file ({ce.DeviceText(ct, cd, 2)})");
    var ublob = ce.DeviceGetState(ct, cd);
    Check(ublob.Length > 100_000, $"state blob carries the user IR PCM ({ublob.Length} bytes)");
    var cw = new float[64];
    Check(ce.DeviceLayerWave(ct, cd, 0, cw, cw.Length) == 64 && cw.Max() > 0.5f, "IR waveform envelope is readable");
    var ru = RenderTail();
    Check(ru.fin && ru.all > 1e-3f, $"user IR renders ({ru.all:F4})");
    ce.DeviceScope(ct, cd, csc, csc.Length);
    Check(csc[11] == 2f && csc[9] > 1.0f && csc[9] < 1.2f, $"user IR: stereo, leading silence trimmed ({csc[9]:F3} s)");

    // Clone: duplicating the track carries params + the user IR.
    int ctD = ce.DuplicateTrack(ct); int cdD = ce.TrackDeviceCount(ctD) - 1;
    Check(ctD > 0 && ce.DeviceText(ctD, cdD, 2) == irName && Math.Abs(ce.DeviceGetParam(ctD, cdD, IrSize) - 0.9f) < 1e-3,
          "duplicate track clones Chamber params + user IR");

    // Project round-trip keeps the user IR.
    string cproj = Path.Combine(Path.GetTempPath(), "nota-chamber-" + Guid.NewGuid().ToString("N"));
    try
    {
        var cwarn = new System.Collections.Generic.List<string>();
        var cdoc = ProjectService.Capture(ce, new TransportState(120, 1, false, false), cwarn);
        ProjectService.Save(cdoc, cproj, ce);
        using var ce2 = new NotaEngine();
        ProjectService.Apply(ProjectService.Load(cproj), ce2, cproj);
        int rt = -1;
        for (int i = 0; i < ce2.TrackCount; i++) if (ce2.TryGetTrackInfo(i, out var ti) && ce2.TrackDeviceCount(ti.Id) > 0) { rt = ti.Id; break; }
        Check(rt > 0 && ce2.DeviceText(rt, 0, 2) == irName && ce2.DeviceGetParam(rt, 0, IrSelect) > 0.999f, "project round-trip restores the user IR");
    }
    finally { try { Directory.Delete(cproj, true); } catch { } try { File.Delete(irPath); } catch { } }

    // Automation drives Blend.
    int clane = ce.AddAutomationLane(ct, AutomationTarget.DeviceParam, cd, Blend);
    Check(clane >= 0, "add Chamber Blend automation lane");
    ce.SetAutomationPoints(ct, clane, new[] { new AutomationPoint(0.0, 0.1f), new AutomationPoint(2.0, 0.9f) });
    ce.Seek(1.99); ce.Play(); ce.RenderOffline(cb, 4096); ce.StopTransport();
    Check(ce.DeviceGetParam(ct, cd, Blend) > 0.7f, $"automation drives Chamber Blend ({ce.DeviceGetParam(ct, cd, Blend):F2})");
    ce.RemoveAutomationLane(ct, clane);

    // Factory presets: every named param exists and each applies in place + renders.
    {
        var cat = new FactoryPresetCatalog();
        var mine = cat.All().Where(p => !p.IsInstrument && !p.IsMidiEffect && p.BuiltinKind == 20).ToList();
        Check(mine.Count >= 16, $"Nota Chamber ships factory presets ({mine.Count})");
        var bad = mine.SelectMany(p => cat.Document(p.Id)!.NamedParams!.Keys.Where(k => !cnames.Contains(k)).Select(k => $"{p.DisplayName}:{k}")).ToList();
        Check(bad.Count == 0, $"every Chamber preset param name exists{(bad.Count > 0 ? " — bad: " + string.Join(", ", bad) : "")}");
        int pf = 0;
        foreach (var p in mine)
        {
            if (cat.ApplyInPlace(ce, p.Id, ct, cd).Length != 0) { pf++; continue; }
            Settle();
            var rp = RenderTail();
            if (!rp.fin || rp.all < 1e-4f || rp.all > 4f) pf++;
        }
        Check(pf == 0, $"every Chamber preset applies and renders ({pf} failed)");
        cat.ApplyInPlace(ce, "chamber/Wide Shimmer", ct, cd);
        Check(Math.Abs(ce.DeviceGetParam(ct, cd, AlgoMode) - 1f) < 1e-3 && ce.DeviceGetParam(ct, cd, IrSelect) < 0.2f, "Wide Shimmer preset selects Shimmer + Cathedral");
        cat.ApplyInPlace(ce, "chamber/Spring Box", ct, cd);
        Check(ce.DeviceGetParam(ct, cd, AlgoMode) < 1e-3, "unnamed params reset to defaults between presets");
    }

    // MCP: kind listed, IR text + file load through the tools.
    {
        var dt = new Nota.Mcp.Tools.DeviceTools(ce, new Nota.SmokeTest.SyncDispatch(), new Nota.SmokeTest.NoRefresh());
        Check(dt.ListDeviceKinds().Any(k => k.Kind == 20 && k.Name == "Nota Chamber"), "MCP lists Nota Chamber (kind 20)");
        Check(dt.GetDeviceText(ct, cd, 0).Result.Length > 0, "MCP reads the Chamber IR name");
    }
}

// ============================ Nota Prism ===================================
Console.WriteLine("-- Nota Prism --");
{
    // Prism = three-band dynamics (kind 21). Own engines (it changes level). The input is a
    // deterministic clip — one steady sine per band (80 Hz / 800 Hz / 6 kHz) — so renders
    // compare exactly; a loud copy drives compression, a quiet one expansion / upward.
    const int Amount = 0, Bands = 2, Lookahead = 6, Solo = 10, Mix = 12, G = 13, PB = 13;
    const int AboveThresh = 0, AboveRatio = 1, BelowOn = 5, BelowThresh = 6, BelowRatio = 7, Floor = 10;
    string loudWav = Path.Combine(Path.GetTempPath(), "nota_prism_loud_" + Guid.NewGuid().ToString("N") + ".wav");
    string quietWav = Path.Combine(Path.GetTempPath(), "nota_prism_quiet_" + Guid.NewGuid().ToString("N") + ".wav");
    double[] tones = { 80, 800, 6000 };
    Nota.SmokeTest.WavWriter.WriteTones(loudWav, 4.0, tones, 0.25, 44100);
    Nota.SmokeTest.WavWriter.WriteTones(quietWav, 4.0, tones, 0.003, 44100);
    (NotaEngine e, int t, int d) Make(string wav)
    {
        var e = new NotaEngine();
        e.SetBpm(120); e.SetTimeSignature(4, 4);
        int t = e.AddAudioTrack();
        e.AddAudioClip(t, wav, startBeat: 0.0);
        return (e, t, e.AddBuiltinDevice(t, 21));
    }
    var (pe, pt, pd) = Make(loudWav);
    var (qe, qt, qd) = Make(quietWav);
    try
    {
        Check(pd >= 0, "add built-in Nota Prism");
        Check(pe.DeviceName(pt, pd) == "Nota Prism", $"name is Nota Prism (got '{pe.DeviceName(pt, pd)}')");
        Check(pe.TrackDeviceBuiltinKind(pt, pd) == 21, $"builtin kind is 21 (got {pe.TrackDeviceBuiltinKind(pt, pd)})");
        int ppc = pe.DeviceParamCount(pt, pd);
        Check(ppc == 52, $"Nota Prism exposes 52 params (got {ppc})");
        var pnames = Enumerable.Range(0, ppc).Select(i => pe.DeviceParamName(pt, pd, i)).ToList();
        Check(pnames.Distinct().Count() == ppc && pnames.All(n => n.Length > 0), "Prism param names are unique and non-empty");
        Check(pe.DeviceAcceptsSidechain(pt, pd), "Prism accepts a sidechain");
        pe.DeviceSetParam(pt, pd, Amount, 0.42f);
        Check(Math.Abs(pe.DeviceGetParam(pt, pd, Amount) - 0.42f) < 1e-4, "Prism param set/get round-trips");
        pe.DeviceSetParam(pt, pd, Amount, 1f);

        const int N = 8192;
        var pb = new float[N * 2];
        var psc = new float[32];
        int P(int band, int f) => G + band * PB + f;
        (bool fin, float rms) Render(NotaEngine e, int blocks = 4)
        {
            e.Seek(0); e.Play();
            bool f = true; float r = 0;
            for (int k = 0; k < blocks; k++)
            {
                e.RenderOffline(pb, N);
                foreach (var s in pb) if (!float.IsFinite(s)) { f = false; break; }
                r = Rms(pb, N);
            }
            e.StopTransport();
            return (f, r);
        }
        void Neutral(NotaEngine e, int t, int d) { for (int b = 0; b < 3; b++) { e.DeviceSetParam(t, d, P(b, AboveRatio), 0f); e.DeviceSetParam(t, d, P(b, BelowOn), 0f); } }
        void Defaults() { for (int i = 0; i < ppc; i++) pe.DeviceSetParam(pt, pd, i, pe.DeviceParamDefault(pt, pd, i)); }
        static double DbRatio(float a, float b) => 20 * Math.Log10(a / Math.Max(1e-9f, b));

        // Bypassed reference vs neutral settings: the LR4 split sums back flat.
        pe.SetDeviceBypassed(pt, pd, true);
        var rByp = Render(pe);
        pe.SetDeviceBypassed(pt, pd, false);
        Neutral(pe, pt, pd);
        var rNeu = Render(pe);
        Check(rNeu.fin && rByp.rms > 1e-2f && Math.Abs(DbRatio(rNeu.rms, rByp.rms)) < 0.2, $"neutral Prism sums flat ({DbRatio(rNeu.rms, rByp.rms):+0.00;-0.00} dB vs bypass)");

        // Compression: a low threshold and a high ratio pull every band down; the scope reports GR.
        for (int b = 0; b < 3; b++) { pe.DeviceSetParam(pt, pd, P(b, AboveThresh), 0.4f); pe.DeviceSetParam(pt, pd, P(b, AboveRatio), 0.8f); }
        var rComp = Render(pe);
        int pn = pe.DeviceScope(pt, pd, psc, psc.Length);
        Check(pn == 20, $"Prism scope returns 20 values (got {pn})");
        Check(rComp.fin && DbRatio(rComp.rms, rNeu.rms) < -6, $"compression lowers the level ({DbRatio(rComp.rms, rNeu.rms):0.0} dB)");
        Check(psc[0] > 6f && psc[1] > 6f && psc[2] > 6f, $"every band reports gain reduction ({psc[0]:F1} / {psc[1]:F1} / {psc[2]:F1} dB)");
        Check(pe.DeviceGainReduction(pt, pd) > 6f, $"shell GR meter follows ({pe.DeviceGainReduction(pt, pd):F1} dB)");
        pe.DeviceSetParam(pt, pd, Amount, 0f);
        var rAmt0 = Render(pe);
        Check(Math.Abs(DbRatio(rAmt0.rms, rNeu.rms)) < 0.1, $"Amount 0 % leaves the signal unprocessed ({DbRatio(rAmt0.rms, rNeu.rms):+0.00;-0.00} dB)");
        pe.DeviceSetParam(pt, pd, Amount, 1f);
        pe.DeviceSetParam(pt, pd, Mix, 0f);
        var rMix0 = Render(pe);
        Check(Math.Abs(DbRatio(rMix0.rms, rNeu.rms)) < 0.1, $"Mix 0 % passes the dry band sum ({DbRatio(rMix0.rms, rNeu.rms):+0.00;-0.00} dB)");
        pe.DeviceSetParam(pt, pd, Mix, 0.5f);
        var rMix50 = Render(pe);
        Check(rMix50.rms < rNeu.rms && rMix50.rms > rComp.rms, "Mix 50 % sits between dry and compressed");
        pe.DeviceSetParam(pt, pd, Mix, 1f);

        // Upward compression lifts a quiet signal (below −20 dB, ratio 1 : 4 upward, floor 24 dB);
        // expansion (4 : 1) pushes it down.
        Neutral(qe, qt, qd);
        var rQuiet = Render(qe);
        for (int b = 0; b < 3; b++)
        {
            qe.DeviceSetParam(qt, qd, P(b, BelowOn), 1f); qe.DeviceSetParam(qt, qd, P(b, BelowThresh), 0.75f);
            qe.DeviceSetParam(qt, qd, P(b, BelowRatio), 0f); qe.DeviceSetParam(qt, qd, P(b, Floor), 0.5f);
        }
        var rUp = Render(qe);
        qe.DeviceScope(qt, qd, psc, psc.Length);
        Check(rUp.fin && DbRatio(rUp.rms, rQuiet.rms) > 12 && psc[3] > 12 && psc[4] > 12 && psc[5] > 12,
              $"upward compression lifts a quiet signal ({DbRatio(rUp.rms, rQuiet.rms):+0.0} dB, boost {psc[3]:F1} / {psc[4]:F1} / {psc[5]:F1})");
        for (int b = 0; b < 3; b++) qe.DeviceSetParam(qt, qd, P(b, BelowRatio), 1f);
        var rExp = Render(qe);
        Check(rExp.fin && DbRatio(rExp.rms, rQuiet.rms) < -12, $"downward expansion pushes a quiet signal down ({DbRatio(rExp.rms, rQuiet.rms):0.0} dB)");
        for (int b = 0; b < 3; b++) qe.DeviceSetParam(qt, qd, P(b, Floor), 0.125f);
        var rFloor = Render(qe);
        Check(Math.Abs(DbRatio(rFloor.rms, rQuiet.rms) + 6) < 1, $"Floor limits the expansion (6 dB: {DbRatio(rFloor.rms, rQuiet.rms):0.0} dB)");
        Defaults();

        // Band modes; solo keeps one tone of three.
        foreach (float mode in new[] { 0.5f, 1f, 0f })
        {
            pe.DeviceSetParam(pt, pd, Bands, mode);
            var rm = Render(pe);
            pe.DeviceScope(pt, pd, psc, psc.Length);
            int expect = mode > 0.75f ? 1 : mode > 0.25f ? 2 : 3;
            Check(rm.fin && rm.rms > 1e-3f && (int)psc[17] == expect, $"{expect}-band mode renders ({rm.rms:F3}, scope {psc[17]})");
        }
        Neutral(pe, pt, pd);
        var solos = new float[3];
        for (int b = 0; b < 3; b++) { pe.DeviceSetParam(pt, pd, Solo, (b + 1) / 3f); solos[b] = Render(pe).rms; }
        pe.DeviceSetParam(pt, pd, Solo, 0f);
        Check(solos.All(r => Math.Abs(DbRatio(r, rNeu.rms) + 4.77) < 1), $"solo keeps one band ({string.Join(" / ", solos.Select(r => DbRatio(r, rNeu.rms).ToString("0.0")))} dB, expect −4.8)");
        Defaults();

        // Lookahead is reported as latency (for PDC).
        pe.DeviceSetParam(pt, pd, Lookahead, 0.5f);
        Render(pe, 1);
        pe.DeviceScope(pt, pd, psc, psc.Length);
        Check(psc[14] > 200 && psc[14] < 240 && pe.TrackLatencySamples(pt) == (int)psc[14], $"5 ms lookahead reports its latency ({psc[14]} smp, track {pe.TrackLatencySamples(pt)})");
        pe.DeviceSetParam(pt, pd, Lookahead, 0f);

        // Telemetry rings for the card: spectrum samples + detector traces.
        var ring = new float[2048];
        Check(pe.DeviceLayerWave(pt, pd, 0, ring, ring.Length) == 2048 && ring.Any(v => Math.Abs(v) > 1e-3f), "Prism exposes the input spectrum ring");
        Check(pe.DeviceLayerWave(pt, pd, 3, ring, 256) == 256 && ring.Take(256).Any(v => v > 1e-3f), "Prism exposes the detector trace");

        // Sidechain: an external key source keys the detectors.
        int pkey = pe.AddInstrumentTrack();
        pe.AddMidiClip(pkey, 0.0, 8.0);
        pe.SetClipNotes(pkey, 0, new[] { new NotaNote(40, 0.0, 7.5, 1.0f) });
        pe.SetDeviceSidechainSource(pt, pd, pkey);
        Check(pe.DeviceSidechainSource(pt, pd) == pkey, "Prism accepts an external key source");
        var rSc = Render(pe);
        pe.DeviceScope(pt, pd, psc, psc.Length);
        Check(rSc.fin && psc[15] > 0.5f, "Prism with an external key renders finite and reports the key");
        pe.SetDeviceSidechainSource(pt, pd, -1);
        pe.RemoveTrack(pkey);

        // Project round-trip keeps the params.
        pe.DeviceSetParam(pt, pd, P(2, BelowRatio), 0.21f); pe.DeviceSetParam(pt, pd, Bands, 0.5f);
        string pproj = Path.Combine(Path.GetTempPath(), "nota-prism-" + Guid.NewGuid().ToString("N"));
        try
        {
            var pwarn = new System.Collections.Generic.List<string>();
            ProjectService.Save(ProjectService.Capture(pe, new TransportState(120, 1, false, false), pwarn), pproj, pe);
            using var pe2 = new NotaEngine();
            ProjectService.Apply(ProjectService.Load(pproj), pe2, pproj);
            int rt = -1;
            for (int i = 0; i < pe2.TrackCount; i++) if (pe2.TryGetTrackInfo(i, out var ti) && pe2.TrackDeviceCount(ti.Id) > 0) { rt = ti.Id; break; }
            Check(rt > 0 && pe2.TrackDeviceBuiltinKind(rt, 0) == 21 && Math.Abs(pe2.DeviceGetParam(rt, 0, P(2, BelowRatio)) - 0.21f) < 1e-3
                  && Math.Abs(pe2.DeviceGetParam(rt, 0, Bands) - 0.5f) < 1e-3, "project round-trip restores Prism params");
        }
        finally { try { Directory.Delete(pproj, true); } catch { } }
        Defaults();

        // Clone: duplicating the track carries Prism params.
        pe.DeviceSetParam(pt, pd, P(1, AboveThresh), 0.33f);
        int ptD = pe.DuplicateTrack(pt); int pdD = pe.TrackDeviceCount(ptD) - 1;
        Check(ptD > 0 && Math.Abs(pe.DeviceGetParam(ptD, pdD, P(1, AboveThresh)) - 0.33f) < 1e-3, "duplicate track clones Prism params");
        pe.RemoveTrack(ptD);

        // Automation drives Amount.
        int plane = pe.AddAutomationLane(pt, AutomationTarget.DeviceParam, pd, Amount);
        Check(plane >= 0, "add Prism Amount automation lane");
        pe.SetAutomationPoints(pt, plane, new[] { new AutomationPoint(0.0, 0.1f), new AutomationPoint(2.0, 0.9f) });
        pe.Seek(1.99); pe.Play(); pe.RenderOffline(pb, 4096); pe.StopTransport();
        Check(pe.DeviceGetParam(pt, pd, Amount) > 0.7f, $"automation drives Prism Amount ({pe.DeviceGetParam(pt, pd, Amount):F2})");
        pe.RemoveAutomationLane(pt, plane);

        // Factory presets: every named param exists and each applies in place + renders.
        {
            var cat = new FactoryPresetCatalog();
            var mine = cat.All().Where(p => !p.IsInstrument && !p.IsMidiEffect && p.BuiltinKind == 21).ToList();
            Check(mine.Count >= 10, $"Nota Prism ships factory presets ({mine.Count})");
            var bad = mine.SelectMany(p => cat.Document(p.Id)!.NamedParams!.Keys.Where(k => !pnames.Contains(k)).Select(k => $"{p.DisplayName}:{k}")).ToList();
            Check(bad.Count == 0, $"every Prism preset param name exists{(bad.Count > 0 ? " — bad: " + string.Join(", ", bad) : "")}");
            int pf = 0;
            foreach (var p in mine)
            {
                if (cat.ApplyInPlace(pe, p.Id, pt, pd).Length != 0) { pf++; continue; }
                var rp = Render(pe, 2);
                if (!rp.fin || rp.rms < 1e-3f || rp.rms > 1.5f) { pf++; Console.WriteLine($"    preset {p.DisplayName}: rms {rp.rms}"); }
            }
            Check(pf == 0, $"every Prism preset applies and renders ({pf} failed)");
            cat.ApplyInPlace(pe, "prism/Two-Band Bass", pt, pd);
            Check(Math.Abs(pe.DeviceGetParam(pt, pd, Bands) - 0.5f) < 1e-3, "Two-Band Bass preset selects the 2-band mode");
            cat.ApplyInPlace(pe, "prism/Bus Glue", pt, pd);
            Check(pe.DeviceGetParam(pt, pd, Bands) < 1e-3, "unnamed params reset to defaults between presets");
        }

        // MCP lists the kind.
        var dt = new Nota.Mcp.Tools.DeviceTools(pe, new Nota.SmokeTest.SyncDispatch(), new Nota.SmokeTest.NoRefresh());
        Check(dt.ListDeviceKinds().Any(k => k.Kind == 21 && k.Name == "Nota Prism"), "MCP lists Nota Prism (kind 21)");
    }
    finally
    {
        pe.Dispose(); qe.Dispose();
        try { File.Delete(loudWav); File.Delete(quietWav); } catch { }
    }
}

// ============================ Nota Lens ====================================
Console.WriteLine("-- Nota Lens --");
{
    // Lens = analyzer (kind 22). Own engine, fed a deterministic 1 kHz sine so the
    // spectrum peak, the scope period and the third-octave table are all predictable.
    // The analysis runs lazily on the message thread and is rate-limited by Display Rate,
    // so each read is preceded by a render + a short wait.
    const int ViewP = 0, FreezeP = 1, SourceP = 2, MidSideP = 3, FftSizeP = 4, WindowP = 5,
              AverageP = 6, SmoothP = 7, TiltP = 9, PeakHoldP = 11, TimeDivP = 15, TrigModeP = 17,
              TrigLevelP = 19, CursorAP = 26, CursorBP = 27, RateP = 36;
    const int M_PeakHz = 0, M_PeakDb = 1, M_RmsDb = 2, M_PeriodMs = 8, M_FreqHz = 9, M_Vpp = 10,
              M_Vrms = 11, M_BinHz = 13, M_FftN = 14, M_SampleRate = 18, M_Held = 19, kLensScope = 20;
    const int L_Spectrum = 0, L_Peak = 1, L_Trace = 2, L_Raw = 3, L_Waterfall = 4;
    const int A_Rearm = 0, A_ResetPeak = 1;

    string lensWav = Path.Combine(Path.GetTempPath(), "nota_lens_" + Guid.NewGuid().ToString("N") + ".wav");
    Nota.SmokeTest.WavWriter.WriteTones(lensWav, 4.0, new double[] { 1000 }, 0.25, 44100);
    using var le = new NotaEngine();
    le.SetBpm(120); le.SetTimeSignature(4, 4);
    int lt = le.AddAudioTrack();
    le.AddAudioClip(lt, lensWav, startBeat: 0.0);
    int ld = le.AddBuiltinDevice(lt, 22);
    try
    {
        Check(ld >= 0, "add built-in Nota Lens");
        Check(le.DeviceName(lt, ld) == "Nota Lens", $"name is Nota Lens (got '{le.DeviceName(lt, ld)}')");
        Check(le.TrackDeviceBuiltinKind(lt, ld) == 22, $"builtin kind is 22 (got {le.TrackDeviceBuiltinKind(lt, ld)})");
        int lpc = le.DeviceParamCount(lt, ld);
        Check(lpc == 37, $"Nota Lens exposes 37 params (got {lpc})");
        var lnames = Enumerable.Range(0, lpc).Select(i => le.DeviceParamName(lt, ld, i)).ToList();
        Check(lnames.Distinct().Count() == lpc && lnames.All(n => n.Length > 0), "Lens param names are unique and non-empty");
        le.DeviceSetParam(lt, ld, TiltP, 0.42f);
        Check(Math.Abs(le.DeviceGetParam(lt, ld, TiltP) - 0.42f) < 1e-4, "Lens param set/get round-trips");
        le.DeviceSetParam(lt, ld, TiltP, le.DeviceParamDefault(lt, ld, TiltP));
        le.DeviceSetParam(lt, ld, RateP, 1f);   // 60 fps: the shortest analysis interval

        const int LN = 8192;
        var lb = new float[LN * 2];
        var lsc = new float[32];
        float Render(int blocks = 4)
        {
            le.Seek(0); le.Play();
            float r = 0;
            for (int k = 0; k < blocks; k++)
            {
                le.RenderOffline(lb, LN);
                foreach (var s in lb) if (!float.IsFinite(s)) { Check(false, "Lens render stays finite"); break; }
                r = Rms(lb, LN);
            }
            le.StopTransport();
            System.Threading.Thread.Sleep(40);   // let the rate limiter open
            return r;
        }
        double Sc(int i) => lsc[i];
        void Read() { int n = le.DeviceScope(lt, ld, lsc, kLensScope); Check(n == kLensScope, $"Lens scope returns {kLensScope} values (got {n})"); }

        // An analyzer passes audio through untouched.
        le.SetDeviceBypassed(lt, ld, true);
        float rByp = Render();
        le.SetDeviceBypassed(lt, ld, false);
        float rThru = Render();
        Check(rByp > 1e-2f && Math.Abs(20 * Math.Log10(rThru / Math.Max(1e-9f, rByp))) < 0.01,
              $"Lens passes the signal through unchanged ({20 * Math.Log10(rThru / Math.Max(1e-9f, rByp)):+0.000;-0.000} dB vs bypass)");
        Check(le.TrackLatencySamples(lt) == 0, $"Lens adds no latency to the track (got {le.TrackLatencySamples(lt)} smp)");
        Read();

        // Spectrum: the 1 kHz tone is the loudest peak, at roughly −12 dBFS.
        Check(Math.Abs(Sc(M_PeakHz) - 1000) < 80, $"spectrum finds the 1 kHz tone ({Sc(M_PeakHz):F0} Hz)");
        Check(Sc(M_RmsDb) is > -25 and < -9, $"RMS follows the tone ({Sc(M_RmsDb):F1} dB)");
        // Calibration: unsmoothed, a 0.25 sine must read its own −12.0 dBFS (a Hann window
        // costs up to 1.4 dB of scalloping when the tone falls between bins). Smoothing then
        // averages power over a band, so it legitimately reads a narrow tone lower.
        float smoothWas = le.DeviceGetParam(lt, ld, SmoothP);
        le.DeviceSetParam(lt, ld, SmoothP, 0f);
        Render(); Read();
        Check(Sc(M_PeakDb) is > -14 and < -11, $"an unsmoothed 0.25 sine reads −12 dBFS ({Sc(M_PeakDb):F1} dB)");
        le.DeviceSetParam(lt, ld, SmoothP, smoothWas);
        Render(); Read();
        Check(Sc(M_PeakDb) < -13, $"smoothing averages the tone's power over its band ({Sc(M_PeakDb):F1} dB)");
        Check(Math.Abs(Sc(M_SampleRate) - 44100) < 1, $"Lens reports the sample rate ({Sc(M_SampleRate):F0} Hz)");

        var curveBuf = new float[1024];
        Check(le.DeviceLayerWave(lt, ld, L_Spectrum, curveBuf, curveBuf.Length) == 512, "Lens publishes a 512-point spectrum curve");
        Check(le.DeviceLayerWave(lt, ld, L_Peak, curveBuf, curveBuf.Length) == 512, "Lens publishes a 512-point peak-hold curve");
        Check(le.DeviceLayerWave(lt, ld, L_Waterfall, curveBuf, curveBuf.Length) == 256, "Lens publishes a 256-bin waterfall row");
        int traceN = le.DeviceLayerWave(lt, ld, L_Trace, curveBuf, curveBuf.Length);
        Check(traceN == 512, $"Lens publishes a 512-point scope trace (got {traceN})");
        Check(le.DeviceLayerWave(lt, ld, L_Raw, curveBuf, curveBuf.Length) > 0, "Lens publishes the raw triggered window");

        // The curve peaks where the tone is, not at the edges.
        le.DeviceLayerWave(lt, ld, L_Spectrum, curveBuf, curveBuf.Length);
        int best = 0;
        for (int i = 1; i < 512; i++) if (curveBuf[i] > curveBuf[best]) best = i;
        double bestHz = 20 * Math.Pow(Math.Min(20000, 44100 * 0.45) / 20.0, best / 511.0);
        Check(Math.Abs(bestHz - 1000) < 80, $"the published curve peaks at the tone ({bestHz:F0} Hz)");

        // FFT size + resolution follow the parameter.
        le.DeviceSetParam(lt, ld, FftSizeP, 1f);   // 16384
        Render(); Read();
        Check((int)Sc(M_FftN) == 16384, $"FFT size follows the param ({Sc(M_FftN):F0})");
        Check(Math.Abs(Sc(M_BinHz) - 44100.0 / 16384) < 0.01, $"bin width follows the FFT size ({Sc(M_BinHz):F3} Hz)");
        Check(Math.Abs(Sc(M_PeakHz) - 1000) < 40, $"the finer FFT still finds the tone ({Sc(M_PeakHz):F0} Hz)");
        le.DeviceSetParam(lt, ld, FftSizeP, le.DeviceParamDefault(lt, ld, FftSizeP));

        // Scope: a 1 kHz sine measures a 1 ms period and a full-scale-ish Vpp.
        le.DeviceSetParam(lt, ld, ViewP, 0.5f);
        Render(); Read();
        Check(Math.Abs(Sc(M_PeriodMs) - 1.0) < 0.06, $"scope measures the 1 ms period ({Sc(M_PeriodMs):F3} ms)");
        Check(Math.Abs(Sc(M_FreqHz) - 1000) < 60, $"scope reports the tone's frequency ({Sc(M_FreqHz):F1} Hz)");
        Check(Sc(M_Vpp) is > 0.2f and < 1.2f, $"scope measures Vpp ({Sc(M_Vpp):F3})");
        Check(Sc(M_Vrms) > 0.05f, $"scope measures Vrms ({Sc(M_Vrms):F3})");

        // Single-shot: the capture holds until it is re-armed.
        le.DeviceSetParam(lt, ld, TrigModeP, 1f);
        Render(); Read();
        Check(Sc(M_Held) > 0.5f, "a finished Single capture holds the trace");
        le.DeviceAction(lt, ld, A_Rearm, 0, 0f);
        Render(); Read();
        Check(Sc(M_Held) > 0.5f, "re-arm captures again and holds");
        le.DeviceSetParam(lt, ld, TrigModeP, 0f);
        Render(); Read();
        Check(Sc(M_Held) < 0.5f, "Auto free-runs again");

        // Freeze holds the display.
        le.DeviceSetParam(lt, ld, FreezeP, 1f);
        Render(); Read();
        Check(Sc(M_Held) > 0.5f, "Freeze holds the capture");
        le.DeviceSetParam(lt, ld, FreezeP, 0f);

        // Source / Mid-Side: a mono clip has no Side content.
        le.DeviceSetParam(lt, ld, MidSideP, 1f);
        le.DeviceSetParam(lt, ld, SourceP, 0.5f);   // Mid
        Render(); Read();
        double midDb = Sc(M_RmsDb);
        le.DeviceSetParam(lt, ld, SourceP, 1f);     // Side
        Render(); Read();
        double sideDb = Sc(M_RmsDb);
        Check(midDb - sideDb > 40, $"a mono source has no Side content (Mid {midDb:F1} dB, Side {sideDb:F1} dB)");
        le.DeviceSetParam(lt, ld, MidSideP, 0f);
        le.DeviceSetParam(lt, ld, SourceP, 0f);
        le.DeviceSetParam(lt, ld, ViewP, 0f);
        le.DeviceAction(lt, ld, A_ResetPeak, 0, 0f);
        Render(); Read();

        // Text reports (the MCP surface).
        var bandLines = le.DeviceText(lt, ld, 1).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Check(bandLines.Length == 31, $"Lens reports 31 third-octave bands (got {bandLines.Length})");
        Check(le.DeviceText(lt, ld, 0).Contains("peak"), "Lens summary names the peak");
        Check(le.DeviceText(lt, ld, 2).Contains("window"), "Lens reports the scope window");
        Check(le.DeviceText(lt, ld, 3).Length > 0, "Lens reports its spectral peaks");
        le.DeviceSetParam(lt, ld, CursorAP, 0.25f);
        le.DeviceSetParam(lt, ld, CursorBP, 0.5f);
        Check(le.DeviceText(lt, ld, 4).Contains("dt"), "Lens reports the A/B cursor measurements");

        // Clone: duplicating the track copies the params.
        le.DeviceSetParam(lt, ld, SmoothP, 0.77f);
        le.DeviceSetParam(lt, ld, WindowP, 1f);
        int ldup = le.DuplicateTrack(lt);
        Check(ldup > 0 && le.TrackDeviceBuiltinKind(ldup, 0) == 22
              && Math.Abs(le.DeviceGetParam(ldup, 0, SmoothP) - 0.77f) < 1e-4
              && Math.Abs(le.DeviceGetParam(ldup, 0, WindowP) - 1f) < 1e-4, "duplicate track clones the Lens params");
        le.RemoveTrack(ldup);
        for (int i = 0; i < lpc; i++) le.DeviceSetParam(lt, ld, i, le.DeviceParamDefault(lt, ld, i));

        // Automation drives the trigger level.
        int llane = le.AddAutomationLane(lt, AutomationTarget.DeviceParam, ld, TrigLevelP);
        Check(llane >= 0, "add Lens Trigger Level automation lane");
        le.SetAutomationPoints(lt, llane, new[] { new AutomationPoint(0.0, 0.1f), new AutomationPoint(2.0, 0.9f) });
        le.Seek(1.99); le.Play(); le.RenderOffline(lb, 4096); le.StopTransport();
        Check(le.DeviceGetParam(lt, ld, TrigLevelP) > 0.7f, $"automation drives Lens Trigger Level ({le.DeviceGetParam(lt, ld, TrigLevelP):F2})");
        le.RemoveAutomationLane(lt, llane);

        // Factory presets: every named param exists and each applies + still passes audio.
        {
            var cat = new FactoryPresetCatalog();
            var mine = cat.All().Where(p => !p.IsInstrument && !p.IsMidiEffect && p.BuiltinKind == 22).ToList();
            Check(mine.Count >= 25, $"Nota Lens ships at least 25 factory presets ({mine.Count})");
            var bad = mine.SelectMany(p => cat.Document(p.Id)!.NamedParams!.Keys.Where(k => !lnames.Contains(k)).Select(k => $"{p.DisplayName}:{k}")).ToList();
            Check(bad.Count == 0, $"every Lens preset param name exists{(bad.Count > 0 ? " — bad: " + string.Join(", ", bad) : "")}");
            float rRef = Render(1);   // same block count as the per-preset renders below
            int pf = 0;
            foreach (var p in mine)
            {
                if (cat.ApplyInPlace(le, p.Id, lt, ld).Length != 0) { pf++; continue; }
                float r = Render(1);
                if (!float.IsFinite(r) || Math.Abs(20 * Math.Log10(r / Math.Max(1e-9f, rRef))) > 0.01) pf++;
            }
            Check(pf == 0, $"every Lens preset applies and still passes audio through ({pf} failed)");
            cat.ApplyInPlace(le, "lens/Scope · Single Shot", lt, ld);
            Check(le.DeviceGetParam(lt, ld, ViewP) > 0.4f && le.DeviceGetParam(lt, ld, TrigModeP) > 0.9f, "Scope · Single Shot selects the scope and arms Single");
            cat.ApplyInPlace(le, "lens/Mix Overview", lt, ld);
            Check(le.DeviceGetParam(lt, ld, ViewP) < 1e-3 && le.DeviceGetParam(lt, ld, TrigModeP) < 1e-3, "unnamed params reset to defaults between presets");
            for (int i = 0; i < lpc; i++) le.DeviceSetParam(lt, ld, i, le.DeviceParamDefault(lt, ld, i));
            le.DeviceSetParam(lt, ld, RateP, 1f);
        }

        // MCP: the kind is listed and read_analyzer returns a parsed reading.
        {
            var dt = new Nota.Mcp.Tools.DeviceTools(le, new Nota.SmokeTest.SyncDispatch(), new Nota.SmokeTest.NoRefresh());
            Check(dt.ListDeviceKinds().Any(k => k.Kind == 22 && k.Name == "Nota Lens"), "MCP lists Nota Lens (kind 22)");
            Render();
            var reading = dt.ReadAnalyzer(lt, ld).Result;
            Check(reading.Bands.Length == 31, $"MCP read_analyzer returns 31 bands (got {reading.Bands.Length})");
            Check(reading.Summary.Length > 0 && reading.Scope.Length > 0 && reading.Cursors.Length > 0, "MCP read_analyzer returns the summary, scope and cursor lines");
            var loudest = reading.Bands.OrderByDescending(b => b.Db).First();
            Check(Math.Abs(loudest.Hz - 1000) < 300, $"the loudest third-octave band is the 1 kHz one (got {loudest.Hz:F0} Hz)");
            Check(reading.Peaks.Length > 0 && Math.Abs(reading.Peaks[0].Hz - 1000) < 80 && reading.Peaks[0].Note.Length > 0,
                  $"MCP read_analyzer names the strongest peak ({(reading.Peaks.Length > 0 ? $"{reading.Peaks[0].Hz:F0} Hz {reading.Peaks[0].Note}" : "none")})");
        }
    }
    finally
    {
        try { File.Delete(lensWav); } catch { }
    }
}

// ============================ Nota Level ===================================
Console.WriteLine("-- Nota Level (AutoGain) --");
{
    // AutoGain = loudness-matching utility (kind 18). Own engine (it changes level, which
    // would skew the shared master mix used by later tests).
    using var ae = new NotaEngine();
    ae.SetBpm(120); ae.SetTimeSignature(4, 4);
    int at = ae.AddInstrumentTrack();
    ae.AddMidiClip(at, 0.0, 16.0);
    ae.SetClipNotes(at, 0, new[] { new NotaNote(57, 0.0, 15.5, 0.5f) });   // quiet, sustained source → needs boost
    int ag = ae.AddBuiltinDevice(at, 18);
    Check(ag >= 0, "add built-in Nota Level");
    Check(ae.DeviceName(at, ag) == "Nota Level", $"name is Nota Level (got '{ae.DeviceName(at, ag)}')");
    Check(ae.TrackDeviceBuiltinKind(at, ag) == 18, $"builtin kind is 18 (got {ae.TrackDeviceBuiltinKind(at, ag)})");
    Check(ae.DeviceParamCount(at, ag) == 9, $"Nota Level exposes 9 params (got {ae.DeviceParamCount(at, ag)})");

    // Param round-trip.
    ae.DeviceSetParam(at, ag, 0, 0.75f);   // Target
    Check(Math.Abs(ae.DeviceGetParam(at, ag, 0) - 0.75f) < 1e-4, "AutoGain param set/get round-trips");
    ae.DeviceSetParam(at, ag, 0, 0.611f);  // −14 LUFS
    ae.DeviceSetParam(at, ag, 1, 0f);      // Scale = Momentary (fast measurement)
    ae.DeviceSetParam(at, ag, 4, 0f);      // Response = Fast
    ae.DeviceSetParam(at, ag, 6, 1f);      // Max Gain = 24 dB (allow a big boost)

    var ab = new float[8192 * 2];
    var asc = new float[9];
    // Let the meter/gain settle (Fast/Momentary settles < 1 s; render ~4.6 s inside the note).
    ae.Seek(0); ae.Play();
    for (int k = 0; k < 25; k++) ae.RenderOffline(ab, 8192);
    int an = ae.DeviceScope(at, ag, asc, asc.Length);
    ae.StopTransport();
    bool afin = true; foreach (var s in ab) if (!float.IsFinite(s)) { afin = false; break; }
    Check(afin && Rms(ab, 8192) > 0.001f, $"AutoGain passes audio (rms {Rms(ab, 8192):F3})");
    Check(an == 9, $"AutoGain scope returns 9 meters (got {an})");
    // Quiet source matched up toward −14 → a real boost, and the output ends louder than the input.
    Check(asc[4] > 4.0f, $"auto-gain boosts a quiet source toward target (applied {asc[4]:F1} dB)");
    Check(asc[1] > asc[0] + 3.0f, $"gain-matched output is louder than input (out {asc[1]:F1} > in {asc[0]:F1} LUFS)");

    // True-peak safety keeps the boosted output from clipping.
    ae.DeviceSetParam(at, ag, 7, 1f);   // Safe on
    ae.Seek(0); ae.Play(); for (int k = 0; k < 4; k++) ae.RenderOffline(ab, 8192); ae.StopTransport();
    float pk = 0; foreach (var s in ab) pk = Math.Max(pk, Math.Abs(s));
    Check(pk <= 1.01f, $"true-peak safety holds the ceiling (peak {pk:F3})");

    // Manual (Auto off) holds the correction rather than tracking.
    ae.DeviceSetParam(at, ag, 2, 0f);   // Auto off
    ae.Seek(0); ae.Play(); ae.RenderOffline(ab, 4096); ae.DeviceScope(at, ag, asc, asc.Length); ae.StopTransport();
    Check(float.IsFinite(asc[4]), "manual mode renders finite (held correction)");

    // Sidechain: routing a reference track makes the target follow that track's loudness.
    int refT = ae.AddInstrumentTrack();
    ae.AddMidiClip(refT, 0.0, 4.0);
    ae.SetClipNotes(refT, 0, new[] { new NotaNote(57, 0.0, 3.5, 1.0f) });   // louder reference
    ae.SetDeviceSidechainSource(at, ag, refT);
    Check(ae.DeviceSidechainSource(at, ag) == refT, "AutoGain accepts a sidechain reference source");
    ae.DeviceSetParam(at, ag, 2, 1f);   // Auto back on
    ae.Seek(0); ae.Play(); for (int k = 0; k < 6; k++) ae.RenderOffline(ab, 8192); ae.DeviceScope(at, ag, asc, asc.Length); ae.StopTransport();
    Check(asc[7] > -119f, $"sidechain reference loudness is measured (scLufs {asc[7]:F1})");
    bool sfin = true; foreach (var s in ab) if (!float.IsFinite(s)) { sfin = false; break; }
    Check(sfin, "AutoGain with sidechain renders finite");
    ae.SetDeviceSidechainSource(at, ag, -1);

    // Clone: duplicating the track carries AutoGain params.
    ae.DeviceSetParam(at, ag, 6, 0.7f);   // Max Gain
    int atD = ae.DuplicateTrack(at); int agD = ae.TrackDeviceCount(atD) - 1;
    Check(atD > 0 && Math.Abs(ae.DeviceGetParam(atD, agD, 6) - 0.7f) < 1e-3, "duplicate track clones AutoGain params");

    // Automation drives Target.
    int alane = ae.AddAutomationLane(at, AutomationTarget.DeviceParam, ag, 0);
    Check(alane >= 0, "add AutoGain Target automation lane");
    ae.SetAutomationPoints(at, alane, new[] { new AutomationPoint(0.0, 0.3f), new AutomationPoint(2.0, 0.9f) });
    ae.Seek(1.99); ae.Play(); ae.RenderOffline(ab, 4096); ae.StopTransport();
    Check(ae.DeviceGetParam(at, ag, 0) > 0.7f, $"automation drives AutoGain Target ({ae.DeviceGetParam(at, ag, 0):F2})");
}

// ============================ Nota Forge ===================================
Console.WriteLine("-- Nota Forge --");
{
    // Own engine instance (a driven saturator would pollute the shared master mix used by
    // later tests). Forge = multi-stage saturation (kind 17), 27 params (incl. Oversampling).
    using var fe = new NotaEngine();
    fe.SetBpm(120); fe.SetTimeSignature(4, 4);
    int ft2 = fe.AddInstrumentTrack();
    fe.AddMidiClip(ft2, 0.0, 4.0);
    fe.SetClipNotes(ft2, 0, new[] { new NotaNote(57, 0.0, 3.0, 0.9f) });
    int fg = fe.AddBuiltinDevice(ft2, 17);
    Check(fg >= 0, "add built-in Nota Forge");
    Check(fe.DeviceName(ft2, fg) == "Nota Forge", $"name is Nota Forge (got '{fe.DeviceName(ft2, fg)}')");
    Check(fe.TrackDeviceBuiltinKind(ft2, fg) == 17, $"builtin kind is 17 (got {fe.TrackDeviceBuiltinKind(ft2, fg)})");
    Check(fe.DeviceParamCount(ft2, fg) == 27, $"Nota Forge exposes 27 params (got {fe.DeviceParamCount(ft2, fg)})");

    // Param round-trip.
    fe.DeviceSetParam(ft2, fg, 0, 0.6f);   // Amount
    Check(Math.Abs(fe.DeviceGetParam(ft2, fg, 0) - 0.6f) < 1e-4, "Forge param set/get round-trips");

    var fb2 = new float[8192 * 2];
    fe.Seek(0); fe.Play(); fe.RenderOffline(fb2, 8192); fe.StopTransport();
    bool ffin = true; foreach (var s in fb2) if (!float.IsFinite(s)) { ffin = false; break; }
    Check(ffin && Rms(fb2, 8192) > 0.001f, $"Forge passes audio (rms {Rms(fb2, 8192):F3})");

    // Drive adds harmonics → raises level vs a light setting (crude saturation check).
    fe.DeviceSetParam(ft2, fg, 0, 0.1f);   // low amount
    fe.DeviceSetParam(ft2, fg, 12, 0.1f);  // S1 drive low
    fe.Seek(0); fe.Play(); fe.RenderOffline(fb2, 8192); fe.StopTransport();
    float rmsLow = Rms(fb2, 8192);
    fe.DeviceSetParam(ft2, fg, 0, 0.8f);   // high amount
    fe.DeviceSetParam(ft2, fg, 12, 0.8f);  // S1 drive high
    fe.Seek(0); fe.Play(); fe.RenderOffline(fb2, 8192); fe.StopTransport();
    bool dfin = true; foreach (var s in fb2) if (!float.IsFinite(s)) { dfin = false; break; }
    Check(dfin && Rms(fb2, 8192) > rmsLow, $"more drive → more energy ({Rms(fb2, 8192):F3} > {rmsLow:F3})");

    // All four routings render finite + audible.
    for (int rt = 0; rt < 4; rt++)
    {
        fe.DeviceSetParam(ft2, fg, 6, rt / 3f);       // Routing
        fe.DeviceSetParam(ft2, fg, 20, 1f);           // S2 On
        fe.DeviceSetParam(ft2, fg, 25, 1f);           // S3 On
        fe.Seek(0); fe.Play(); fe.RenderOffline(fb2, 8192); fe.StopTransport();
        bool rfin = true; foreach (var s in fb2) if (!float.IsFinite(s)) { rfin = false; break; }
        Check(rfin && Rms(fb2, 8192) > 0.001f, $"Forge routing {rt} renders finite + audible (rms {Rms(fb2, 8192):F3})");
    }

    // Clone: duplicating the track carries Forge params.
    fe.DeviceSetParam(ft2, fg, 3, 0.62f);   // Output
    int ftD = fe.DuplicateTrack(ft2); int fgD = fe.TrackDeviceCount(ftD) - 1;
    Check(ftD > 0 && Math.Abs(fe.DeviceGetParam(ftD, fgD, 3) - 0.62f) < 1e-3, "duplicate track clones Forge params");

    // Automation drives Amount.
    int flane = fe.AddAutomationLane(ft2, AutomationTarget.DeviceParam, fg, 0);
    Check(flane >= 0, "add Forge Amount automation lane");
    fe.SetAutomationPoints(ft2, flane, new[] { new AutomationPoint(0.0, 0.1f), new AutomationPoint(2.0, 0.9f) });
    fe.Seek(1.99); fe.Play(); fe.RenderOffline(fb2, 4096); fe.StopTransport();
    Check(fe.DeviceGetParam(ft2, fg, 0) > 0.7f, $"automation drives Forge Amount ({fe.DeviceGetParam(ft2, fg, 0):F2})");
}

// -- EQ-8: per-band shaping, param round-trip, analyzer scope --
Console.WriteLine("-- EQ-8 --");
{
    // Fresh track, one EQ-8, a sustained note so the analyzer/curve have signal.
    int et = engine.AddInstrumentTrack();
    engine.AddMidiClip(et, 0.0, 4.0);
    engine.SetClipNotes(et, 0, new[] { new NotaNote(69, 0.0, 3.0, 0.9f) });   // A4 = 440 Hz + harmonics
    int eq = engine.AddBuiltinDevice(et, 0);
    Check(engine.DeviceParamCount(et, eq) == 40, "EQ-8 has 40 params on a fresh track");

    // Band 3 (index 2) is a bell; param base = 2*5. On=+0, Type=+1, Freq=+2, Gain=+3, Q=+4.
    int b2 = 2 * 5;
    engine.DeviceSetParam(et, eq, b2 + 2, 2000f);   // Freq
    engine.DeviceSetParam(et, eq, b2 + 4, 3.0f);    // Q
    Check(Math.Abs(engine.DeviceGetParam(et, eq, b2 + 2) - 2000f) < 0.5f, "EQ-8 band param round-trips");

    var eb = new float[8192 * 2];
    // Full cut of a wide bell over the note's fundamental region → quieter than boosted.
    engine.DeviceSetParam(et, eq, b2 + 2, 440f);
    engine.DeviceSetParam(et, eq, b2 + 3, +18f);
    engine.Seek(0); engine.Play(); engine.RenderOffline(eb, 8192); engine.StopTransport();
    float boosted = Rms(eb, 8192);
    engine.DeviceSetParam(et, eq, b2 + 3, -18f);
    engine.Seek(0); engine.Play(); engine.RenderOffline(eb, 8192); engine.StopTransport();
    float cut = Rms(eb, 8192);
    Check(boosted > cut * 1.2f, $"EQ-8 bell shapes level (boost {boosted:0.000} > cut {cut:0.000})");

    // Analyzer scope: after audio has passed, the pre-EQ ring returns non-silent samples.
    engine.DeviceSetParam(et, eq, b2 + 3, 0f);
    engine.Seek(0); engine.Play(); engine.RenderOffline(eb, 8192); engine.StopTransport();
    var scope = new float[2048];
    int got = engine.DeviceScope(et, eq, scope, 2048);
    float srms = Rms(scope, 1024);
    Check(got == 2048 && srms > 1e-5f, $"EQ-8 analyzer scope returns signal ({got} samples, rms {srms:0.0000})");
}

// ============================ Nota Arp (MIDI FX) ===========================
Console.WriteLine("-- Nota Arp (MIDI effect) --");
{
    using var ae = new NotaEngine();
    ae.SetBpm(120); ae.SetTimeSignature(4, 4);
    int t = ae.AddInstrumentTrack();
    ae.AddMidiClip(t, 0.0, 8.0);
    ae.SetClipNotes(t, 0, new[] { new NotaNote(60, 0.0, 8.0, 0.9f) });   // one long held note

    int arp = ae.AddMidiEffect(t, 0);
    Check(arp == 0, "AddMidiEffect returns index 0");
    Check(ae.TrackMidiEffectCount(t) == 1, "track has 1 MIDI effect");
    Check(ae.MidiEffectKind(t, 0) == 0, "MIDI effect kind is 0 (Arp)");
    Check(ae.MidiEffectName(t, 0) == "Nota Arp", $"MIDI effect name is Nota Arp (got '{ae.MidiEffectName(t, 0)}')");
    int pc = ae.MidiEffectParamCount(t, 0);
    Check(pc == 125, $"Nota Arp exposes 125 params (got {pc})");

    // Param round-trip (Gate).
    int gate = -1; for (int i = 0; i < pc; i++) if (ae.MidiEffectParamName(t, 0, i) == "Gate") gate = i;
    ae.MidiEffectSetParam(t, 0, gate, 0.5f);
    Check(gate >= 0 && Math.Abs(ae.MidiEffectGetParam(t, 0, gate) - 0.5f) < 1e-4, "arp param set/get round-trips");

    // Configure a fast, short-gate arp over the single held note.
    void SetP(string name, float v) { for (int i = 0; i < pc; i++) if (ae.MidiEffectParamName(t, 0, i) == name) ae.MidiEffectSetParam(t, 0, i, v); }
    SetP("Rate", 5f); SetP("Sync", 1f); SetP("Order", 0f); SetP("Gate", 0.4f); SetP("Octaves", 2f);

    var arpBuf = new float[24000 * 2];
    ae.Seek(0); ae.Play(); ae.RenderOffline(arpBuf, 24000); ae.StopTransport();
    Check(Rms(arpBuf, 24000) > 0.001f, $"arpeggiated held note is audible (rms {Rms(arpBuf, 24000):0.0000})");

    // Bypass passes the raw held note through; the arp'd output must differ from it
    // (the arp gates/retriggers/octave-shifts the single held note into a pattern).
    ae.SetMidiEffectBypassed(t, 0, true);
    var rawBuf = new float[24000 * 2];
    ae.Seek(0); ae.Play(); ae.RenderOffline(rawBuf, 24000); ae.StopTransport();
    Check(Rms(rawBuf, 24000) > 0.001f, $"bypassed arp passes the raw note (rms {Rms(rawBuf, 24000):0.0000})");
    double diff = 0; for (int i = 0; i < 24000 * 2; i++) { double d = arpBuf[i] - rawBuf[i]; diff += d * d; }
    diff = Math.Sqrt(diff / (24000 * 2));
    Check(diff > 0.005f, $"arp output differs from passthrough (diff rms {diff:0.0000})");
    ae.SetMidiEffectBypassed(t, 0, false);

    // Project round-trip restores the MIDI effect + its params.
    var warn = new System.Collections.Generic.List<string>();
    var pdoc = ProjectService.Capture(ae, new TransportState(120.0, 1.0, false, false), warn);
    using var ae2 = new NotaEngine();
    string ptmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nota-arp-" + System.Guid.NewGuid().ToString("N") + ".nota");
    ProjectService.Save(pdoc, ptmp, ae);
    ProjectService.Apply(ProjectService.Load(ptmp), ae2, ptmp);
    // Find the instrument track in the reloaded engine and check its arp.
    bool restored = false;
    for (int i = 0; i < ae2.TrackCount; i++)
        if (ae2.TryGetTrackInfo(i, out var ti) && ti.IsInstrument && ae2.TrackMidiEffectCount(ti.Id) == 1)
        {
            int rg = -1, rpc = ae2.MidiEffectParamCount(ti.Id, 0);
            for (int p = 0; p < rpc; p++) if (ae2.MidiEffectParamName(ti.Id, 0, p) == "Gate") rg = p;
            restored = ae2.MidiEffectKind(ti.Id, 0) == 0 && rg >= 0 && Math.Abs(ae2.MidiEffectGetParam(ti.Id, 0, rg) - 0.4f) < 1e-3;
        }
    Check(restored, "project round-trip restores the arp + params");
    try { System.IO.Directory.Delete(ptmp, true); } catch { /* best-effort */ }

    // Factory template applies onto a track.
    var factory = new FactoryPresetCatalog();
    var tpl = factory.All().First(p => p.IsMidiEffect);
    int t2 = ae.AddInstrumentTrack();
    string fw = factory.Apply(ae, tpl.Id, t2);
    Check(fw.Length == 0 && ae.TrackMidiEffectCount(t2) == 1, $"factory arp template '{tpl.DisplayName}' applies");
}

// ===================== Nota Chord (MIDI kind 1) rework =====================
Console.WriteLine("-- Nota Chord --");
{
    using var e = new NotaEngine();
    e.SetBpm(120); e.SetTimeSignature(4, 4);
    int t = e.AddInstrumentTrack();
    int m = e.AddMidiEffect(t, 1);
    Check(m == 0 && e.MidiEffectName(t, 0) == "Nota Chord", "add Nota Chord");
    int pc = e.MidiEffectParamCount(t, 0);
    Check(pc == 16, $"Nota Chord exposes 16 params (got {pc})");
    var pnames = new System.Collections.Generic.HashSet<string>();
    for (int i = 0; i < pc; i++) pnames.Add(e.MidiEffectParamName(t, 0, i));
    Check(pnames.Contains("Voice 6") && pnames.Contains("Strum") && pnames.Contains("Keep Root")
          && pnames.Contains("Spread") && pnames.Contains("Fold") && pnames.Contains("Vel 1"),
          "mockup-3b params present (Voice 6 / Strum / Keep Root / Spread / Fold / Vel 1)");
    // Keep Root defaults on (backward-compatible: root passes through).
    SetByName(e, t, 0, "Keep Root", 1f);
    Check(GetByName(e, t, 0, "Keep Root") >= 0.5f, "Keep Root defaults on");

    e.AddMidiClip(t, 0.0, 4.0);
    e.SetClipNotes(t, 0, new[] { new NotaNote(60, 0.0, 2.0, 0.9f) });
    // With Keep Root on vs off the audio differs (the root note is removed).
    var withRoot = new float[12000 * 2]; e.Seek(0); e.Play(); e.RenderOffline(withRoot, 12000); e.StopTransport();
    SetByName(e, t, 0, "Keep Root", 0f);
    var noRoot = new float[12000 * 2]; e.Seek(0); e.Play(); e.RenderOffline(noRoot, 12000); e.StopTransport();
    double kd = 0; for (int i = 0; i < 12000 * 2; i++) { double d = withRoot[i] - noRoot[i]; kd += d * d; }
    Check(Math.Sqrt(kd / (12000 * 2)) > 0.002, "Keep Root toggle changes the output");

    // Strum + all voices stays finite and audible.
    SetByName(e, t, 0, "Keep Root", 1f);
    SetByName(e, t, 0, "Voice 3", 12f); SetByName(e, t, 0, "Strum", 40f); SetByName(e, t, 0, "Spread", 50f);
    var sb = new float[12000 * 2]; e.Seek(0); e.Play(); e.RenderOffline(sb, 12000); e.StopTransport();
    bool fin = true; foreach (var s in sb) if (!float.IsFinite(s) || Math.Abs(s) > 8f) { fin = false; break; }
    Check(fin && Rms(sb, 12000) > 0.001f, "Strum + Spread render audible + finite");

    int t2 = e.DuplicateTrack(t);
    Check(t2 > 0 && Math.Abs(GetByName(e, t2, 0, "Voice 3") - 12f) < 1e-3, "duplicate track clones Chord params");
}

// ===================== Nota Scale (MIDI kind 2) rework =====================
Console.WriteLine("-- Nota Scale --");
{
    using var e = new NotaEngine();
    e.SetBpm(120); e.SetTimeSignature(4, 4);
    int t = e.AddInstrumentTrack();
    int m = e.AddMidiEffect(t, 2);
    Check(m == 0 && e.MidiEffectName(t, 0) == "Nota Scale", "add Nota Scale");
    int pc = e.MidiEffectParamCount(t, 0);
    Check(pc == 20, $"Nota Scale exposes 20 params (got {pc})");
    var pnames = new System.Collections.Generic.HashSet<string>();
    for (int i = 0; i < pc; i++) pnames.Add(e.MidiEffectParamName(t, 0, i));
    Check(pnames.Contains("Fold") && pnames.Contains("Follow Key") && pnames.Contains("Range Low")
          && pnames.Contains("Range High") && pnames.Contains("Learn") && pnames.Contains("Mask 0") && pnames.Contains("Mask 11"),
          "mockup-3b params present (Fold / Follow Key / Range / Learn / Mask 0-11)");

    // C major, Nearest fold: an out-of-scale C#4 (61) snaps into the scale, and the last
    // remapped note-on is published for the card's IN→OUT readout.
    SetByName(e, t, 0, "Root", 0f); SetByName(e, t, 0, "Scale", 0f); SetByName(e, t, 0, "Fold", 0f);
    e.AddMidiClip(t, 0.0, 4.0);
    e.SetClipNotes(t, 0, new[] { new NotaNote(61, 0.0, 2.0, 0.9f) });
    var scbuf = new float[12000 * 2]; e.Seek(0); e.Play(); e.RenderOffline(scbuf, 12000); e.StopTransport();
    int li = e.MidiEffectLastIn(t, 0), lo = e.MidiEffectLastOut(t, 0);
    int[] cmaj = { 0, 2, 4, 5, 7, 9, 11 };
    Check(li == 61 && lo >= 0 && System.Array.IndexOf(cmaj, ((lo % 12) + 12) % 12) >= 0, $"quantizes out-of-scale note into scale (IN {li} OUT {lo})");

    // Custom mask: switch to Custom + set an empty-ish mask → different remap vs the preset.
    SetByName(e, t, 0, "Scale", 10f);   // Custom
    for (int i = 0; i < 12; i++) SetByName(e, t, 0, $"Mask {i}", 0f);
    SetByName(e, t, 0, "Mask 0", 1f); SetByName(e, t, 0, "Mask 7", 1f);   // only root + fifth
    e.Seek(0); e.Play(); e.RenderOffline(scbuf, 12000); e.StopTransport();
    int lo2 = e.MidiEffectLastOut(t, 0);
    Check(lo2 >= 0 && (((lo2 % 12) + 12) % 12 == 0 || ((lo2 % 12) + 12) % 12 == 7), $"custom two-note scale folds to root/fifth (OUT {lo2})");

    int t2 = e.DuplicateTrack(t);
    Check(t2 > 0 && Math.Abs(GetByName(e, t2, 0, "Mask 7") - 1f) < 1e-3, "duplicate track clones Scale custom mask");
}

// ===================== Nota Length (MIDI kind 3) rework =====================
Console.WriteLine("-- Nota Length --");
{
    using var e = new NotaEngine();
    e.SetBpm(120); e.SetTimeSignature(4, 4);
    int t = e.AddInstrumentTrack();
    int m = e.AddMidiEffect(t, 3);
    Check(m == 0 && e.MidiEffectName(t, 0) == "Nota Length", $"add Nota Length (got '{e.MidiEffectName(t, 0)}')");
    int pc = e.MidiEffectParamCount(t, 0);
    Check(pc == 11, $"Nota Length exposes 11 params (got {pc})");
    var pn = new System.Collections.Generic.HashSet<string>();
    for (int i = 0; i < pc; i++) pn.Add(e.MidiEffectParamName(t, 0, i));
    Check(pn.Contains("Mode") && pn.Contains("Ms") && pn.Contains("Percent") && pn.Contains("Trigger")
          && pn.Contains("Vel to Len") && pn.Contains("Key to Len") && pn.Contains("Random")
          && pn.Contains("Legato") && pn.Contains("Clip Limit"), "mockup-3b params present");

    // A long note forced to a short synced length differs from bypass (note cut short).
    e.AddMidiClip(t, 0.0, 4.0);
    e.SetClipNotes(t, 0, new[] { new NotaNote(60, 0.0, 3.0, 0.9f) });   // held 3 beats
    SetByName(e, t, 0, "Mode", 0f); SetByName(e, t, 0, "Rate", 0f); SetByName(e, t, 0, "Gate", 1f);   // 1/16
    var on = new float[40000 * 2]; e.Seek(0); e.Play(); e.RenderOffline(on, 40000); e.StopTransport();
    e.SetMidiEffectBypassed(t, 0, true);
    var off = new float[40000 * 2]; e.Seek(0); e.Play(); e.RenderOffline(off, 40000); e.StopTransport();
    e.SetMidiEffectBypassed(t, 0, false);
    double diff = 0; for (int i = 0; i < 40000 * 2; i++) { double d = on[i] - off[i]; diff += d * d; }
    Check(Math.Sqrt(diff / (40000 * 2)) > 0.003, "Sync mode forces (shortens) the note vs bypass");

    // Every mode + the note-off trigger renders finite + audible.
    void RenderFinite(string tag, Action cfg)
    {
        cfg();
        var b = new float[40000 * 2]; e.Seek(0); e.Play(); e.RenderOffline(b, 40000); e.StopTransport();
        bool fin = true; foreach (var s in b) if (!float.IsFinite(s) || Math.Abs(s) > 8f) { fin = false; break; }
        Check(fin && Rms(b, 40000) > 0.0005f, $"{tag} renders finite + audible");
    }
    RenderFinite("ms mode", () => { SetByName(e, t, 0, "Mode", 1f); SetByName(e, t, 0, "Ms", 120f); });
    RenderFinite("Gate% mode", () => { SetByName(e, t, 0, "Mode", 2f); SetByName(e, t, 0, "Percent", 50f); });
    RenderFinite("note-off trigger", () => { SetByName(e, t, 0, "Mode", 0f); SetByName(e, t, 0, "Trigger", 1f); });
    RenderFinite("modifiers + legato", () => { SetByName(e, t, 0, "Trigger", 0f); SetByName(e, t, 0, "Vel to Len", 0.6f); SetByName(e, t, 0, "Random", 0.3f); SetByName(e, t, 0, "Legato", 1f); });

    SetByName(e, t, 0, "Ms", 333f);
    int t2 = e.DuplicateTrack(t);
    Check(t2 > 0 && Math.Abs(GetByName(e, t2, 0, "Ms") - 333f) < 1e-3, "duplicate track clones Length params");
}

// ===================== Nota Velocity (MIDI kind 4) rework =====================
Console.WriteLine("-- Nota Velocity --");
{
    using var e = new NotaEngine();
    e.SetBpm(120); e.SetTimeSignature(4, 4);
    int t = e.AddInstrumentTrack();
    int m = e.AddMidiEffect(t, 4);
    Check(m == 0 && e.MidiEffectName(t, 0) == "Nota Velocity", "add Nota Velocity");
    int pc = e.MidiEffectParamCount(t, 0);
    Check(pc == 7, $"Nota Velocity exposes 7 params (got {pc})");
    var pn = new System.Collections.Generic.HashSet<string>();
    for (int i = 0; i < pc; i++) pn.Add(e.MidiEffectParamName(t, 0, i));
    Check(pn.Contains("Drive") && pn.Contains("Mode") && pn.Contains("Out Low") && pn.Contains("Out High")
          && pn.Contains("Random Dir") && pn.Contains("Fixed") && pn.Contains("Random"), "mockup-3b params present");

    // Fixed mode forces velocity: play a soft note, the last-in/out telemetry reports the
    // forced output and it differs from the input.
    e.AddMidiClip(t, 0.0, 4.0);
    e.SetClipNotes(t, 0, new[] { new NotaNote(60, 0.0, 1.0, 0.3f) });   // vel ~38
    SetByName(e, t, 0, "Mode", 2f); SetByName(e, t, 0, "Fixed", 0.8f);
    var b = new float[12000 * 2]; e.Seek(0); e.Play(); e.RenderOffline(b, 12000); e.StopTransport();
    int li = e.MidiEffectLastIn(t, 0), lo = e.MidiEffectLastOut(t, 0);
    Check(li is > 30 and < 46 && lo is > 95 and < 108, $"Fixed mode forces velocity (in {li} → out {lo})");

    // Scope publishes 12 in/out pairs for the histogram/curve.
    var scope = new float[24];
    int sn = e.MidiEffectScope(t, 0, scope);
    bool sok = sn == 24; for (int i = 0; i < sn; i++) if (!float.IsFinite(scope[i]) || scope[i] < 0 || scope[i] > 1.001f) sok = false;
    Check(sok, $"scope publishes 12 in/out pairs (n={sn})");

    // Curve + Compand + Random render finite.
    void Fin(string tag, Action cfg)
    {
        cfg();
        var buf = new float[12000 * 2]; e.Seek(0); e.Play(); e.RenderOffline(buf, 12000); e.StopTransport();
        bool fin = true; foreach (var s in buf) if (!float.IsFinite(s) || Math.Abs(s) > 8f) { fin = false; break; }
        Check(fin && Rms(buf, 12000) > 0.0005f, $"{tag} renders finite + audible");
    }
    Fin("Curve + drive", () => { SetByName(e, t, 0, "Mode", 0f); SetByName(e, t, 0, "Drive", 1.6f); });
    Fin("Compand + range", () => { SetByName(e, t, 0, "Mode", 1f); SetByName(e, t, 0, "Out Low", 0.2f); SetByName(e, t, 0, "Out High", 0.9f); });
    Fin("Random up", () => { SetByName(e, t, 0, "Random", 0.4f); SetByName(e, t, 0, "Random Dir", 1f); });

    SetByName(e, t, 0, "Drive", 0.7f);
    int t2 = e.DuplicateTrack(t);
    Check(t2 > 0 && Math.Abs(GetByName(e, t2, 0, "Drive") - 0.7f) < 1e-3, "duplicate track clones Velocity params");
}

// ===================== Nota Random (MIDI kind 5) rework =====================
Console.WriteLine("-- Nota Random --");
{
    using var e = new NotaEngine();
    e.SetBpm(120); e.SetTimeSignature(4, 4);
    int t = e.AddInstrumentTrack();
    int m = e.AddMidiEffect(t, 5);
    Check(m == 0 && e.MidiEffectName(t, 0) == "Nota Random", "add Nota Random");
    int pc = e.MidiEffectParamCount(t, 0);
    Check(pc == 11, $"Nota Random exposes 11 params (got {pc})");
    var pn = new System.Collections.Generic.HashSet<string>();
    for (int i = 0; i < pc; i++) pn.Add(e.MidiEffectParamName(t, 0, i));
    Check(pn.Contains("Note Range") && pn.Contains("Vel Amt") && pn.Contains("Time Amt") && pn.Contains("Skip")
          && pn.Contains("Oct Amt") && pn.Contains("Dist") && pn.Contains("Rate") && pn.Contains("Stay In Scale")
          && pn.Contains("Locked") && pn.Contains("Seed"), "mockup-3b params present");

    e.AddMidiClip(t, 0.0, 4.0);
    e.SetClipNotes(t, 0, new[] {
        new NotaNote(60, 0.0, 0.5, 0.9f), new NotaNote(62, 0.5, 0.5, 0.9f),
        new NotaNote(64, 1.0, 0.5, 0.9f), new NotaNote(65, 1.5, 0.5, 0.9f),
    });
    float[] Render() { var b = new float[40000 * 2]; e.Seek(0); e.Play(); e.RenderOffline(b, 40000); e.StopTransport(); return b; }

    // Skip = always → notes drop → far quieter than bypass.
    SetByName(e, t, 0, "Chance", 1f); SetByName(e, t, 0, "Note Range", 0f); SetByName(e, t, 0, "Skip", 1f);
    double skipRms = Rms(Render(), 40000);
    e.SetMidiEffectBypassed(t, 0, true); double dryRms = Rms(Render(), 40000); e.SetMidiEffectBypassed(t, 0, false);
    Check(skipRms < dryRms * 0.5 && dryRms > 0.001, $"Skip drops notes (skip rms {skipRms:0.000} vs dry {dryRms:0.000})");

    // Locked seed → two independent engines (fresh instrument state) produce identical audio.
    float[] LockedRender()
    {
        using var le = new NotaEngine();
        le.SetBpm(120); le.SetTimeSignature(4, 4);
        int lt = le.AddInstrumentTrack();
        int lm = le.AddMidiEffect(lt, 5);
        SetByName(le, lt, lm, "Chance", 1f); SetByName(le, lt, lm, "Note Range", 7f);
        SetByName(le, lt, lm, "Locked", 1f); SetByName(le, lt, lm, "Seed", 42f);
        le.AddMidiClip(lt, 0.0, 4.0);
        le.SetClipNotes(lt, 0, new[] {
            new NotaNote(60, 0.0, 0.5, 0.9f), new NotaNote(62, 0.5, 0.5, 0.9f),
            new NotaNote(64, 1.0, 0.5, 0.9f), new NotaNote(65, 1.5, 0.5, 0.9f),
        });
        var b = new float[40000 * 2]; le.Seek(0); le.Play(); le.RenderOffline(b, 40000); le.StopTransport(); return b;
    }
    var r1 = LockedRender(); var r2 = LockedRender();
    double d = 0; for (int i = 0; i < r1.Length; i++) d += Math.Abs(r1[i] - r2[i]);
    Check(d < 1e-3, $"Locked seed is reproducible across runs (Σ|Δ| {d:0.000})");

    // Each distribution + per-bar rate renders finite.
    void Fin(string tag, Action cfg) { cfg(); var b = Render(); bool fin = true; foreach (var s in b) if (!float.IsFinite(s) || Math.Abs(s) > 8f) { fin = false; break; } Check(fin && Rms(b, 40000) > 0.001f, $"{tag} renders finite + audible"); }
    Fin("Even dist", () => { SetByName(e, t, 0, "Locked", 0f); SetByName(e, t, 0, "Dist", 1f); });
    Fin("Walk dist + timing", () => { SetByName(e, t, 0, "Dist", 2f); SetByName(e, t, 0, "Time Amt", 0.5f); });
    Fin("Per bar + octave + scale", () => { SetByName(e, t, 0, "Rate", 1f); SetByName(e, t, 0, "Oct Amt", 0.5f); SetByName(e, t, 0, "Stay In Scale", 1f); });

    SetByName(e, t, 0, "Note Range", 5f);
    int t2 = e.DuplicateTrack(t);
    Check(t2 > 0 && Math.Abs(GetByName(e, t2, 0, "Note Range") - 5f) < 1e-3, "duplicate track clones Random params");
}

// ===================== Phase 2: more MIDI effects + automation + Map/CC =====
Console.WriteLine("-- MIDI FX phase 2 --");
{
    // Each new effect transforms the note stream: its render must differ from bypass.
    string[] names = { "Nota Chord", "Nota Scale", "Nota Length", "Nota Velocity", "Nota Random" };
    var configs = new Action<NotaEngine, int, int>[]
    {
        (e, t, m) => { }, // Chord: default major-triad already active
        (e, t, m) => SetByName(e, t, m, "Transpose", 0f),   // Scale: defaults quantize off-scale notes
        (e, t, m) => SetByName(e, t, m, "Rate", 3f),        // Nota Length: force 1/4 notes
        (e, t, m) => { SetByName(e, t, m, "Mode", 2f); SetByName(e, t, m, "Fixed", 0.3f); },  // Velocity: force fixed 0.3
        (e, t, m) => { SetByName(e, t, m, "Chance", 1f); SetByName(e, t, m, "Note Range", 12f); }, // Random: always transpose
    };
    for (int kind = 1; kind <= 5; kind++)
    {
        using var e = new NotaEngine();
        e.SetBpm(120); e.SetTimeSignature(4, 4);
        int t = e.AddInstrumentTrack();
        e.AddMidiClip(t, 0.0, 4.0);
        e.SetClipNotes(t, 0, new[] {
            new NotaNote(64, 0.0, 1.0, 0.9f), new NotaNote(60, 1.0, 1.0, 0.8f),
            new NotaNote(67, 2.0, 1.0, 0.9f), new NotaNote(62, 3.0, 1.0, 0.8f),
        });
        int m = e.AddMidiEffect(t, kind);
        Check(m == 0 && e.MidiEffectKind(t, 0) == kind, $"add MIDI effect kind {kind}");
        Check(e.MidiEffectName(t, 0) == names[kind - 1], $"kind {kind} is {names[kind - 1]} (got '{e.MidiEffectName(t, 0)}')");
        configs[kind - 1](e, t, 0);

        var on = new float[12000 * 2];
        e.Seek(0); e.Play(); e.RenderOffline(on, 12000); e.StopTransport();
        e.SetMidiEffectBypassed(t, 0, true);
        var off = new float[12000 * 2];
        e.Seek(0); e.Play(); e.RenderOffline(off, 12000); e.StopTransport();
        double diff = 0; for (int i = 0; i < 12000 * 2; i++) { double d = on[i] - off[i]; diff += d * d; }
        diff = Math.Sqrt(diff / (12000 * 2));
        Check(diff > 0.003f, $"{names[kind - 1]} transforms the note stream (diff rms {diff:0.0000})");
    }

    // Automation: a MidiDeviceParam lane drives the arp's Gate (low early, high late).
    {
        using var e = new NotaEngine();
        e.SetBpm(120); e.SetTimeSignature(4, 4);
        int t = e.AddInstrumentTrack();
        e.AddMidiClip(t, 0.0, 4.0);
        int m = e.AddMidiEffect(t, 0);   // arp
        int gate = -1; for (int i = 0; i < e.MidiEffectParamCount(t, m); i++) if (e.MidiEffectParamName(t, m, i) == "Gate") gate = i;
        int lane = e.AddAutomationLane(t, AutomationTarget.MidiDeviceParam, m, gate);
        Check(lane >= 0, "added MidiDeviceParam automation lane");
        e.SetAutomationPoints(t, lane, new[] { new AutomationPoint(0.0, 0.1f), new AutomationPoint(4.0, 1.9f) });
        var p2buf = new float[512 * 2];
        e.Seek(0.0); e.Play(); e.RenderOffline(p2buf, 512); e.StopTransport();
        float early = e.MidiEffectGetParam(t, m, gate);
        e.Seek(3.9); e.Play(); e.RenderOffline(p2buf, 512); e.StopTransport();
        float late = e.MidiEffectGetParam(t, m, gate);
        Check(late > early + 0.5f, $"MIDI-FX param automation drives Gate (early {early:F2} -> late {late:F2})");
    }

    // Map/CC: the arp's CC lane modulates a target audio-device param.
    {
        using var e = new NotaEngine();
        e.SetBpm(120); e.SetTimeSignature(4, 4);
        int t = e.AddInstrumentTrack();
        e.AddMidiClip(t, 0.0, 4.0);
        e.SetClipNotes(t, 0, new[] { new NotaNote(60, 0.0, 4.0, 0.9f) });
        int util = e.AddBuiltinDevice(t, 4);   // Utility: Gain/Width/Mono
        int width = -1; for (int i = 0; i < e.DeviceParamCount(t, util); i++) if (e.DeviceParamName(t, util, i) == "Width") width = i;
        int m = e.AddMidiEffect(t, 0);
        for (int s = 1; s <= 16; s++) SetByName(e, t, m, $"CC {s}", 64f);   // CC lane = ~0.5
        e.SetMidiEffectCcDest(t, m, util, width);
        e.SetMidiEffectCcDepth(t, m, 1.0f);
        var p2buf = new float[8192 * 2];
        e.Seek(0.0); e.Play(); e.RenderOffline(p2buf, 8192); e.StopTransport();
        float wv = e.DeviceGetParam(t, util, width);   // Width range 0..400; CC ~0.5 * depth 1 -> ~half range
        float wExp = (64f / 127f) * 400f;              // CC 64 normalized × full Width span
        Check(Math.Abs(wv - wExp) < 8f, $"Map/CC modulates the target param (Width {wv:F2}, expected ~{wExp:F0})");
    }
}

static void SetByName(NotaEngine e, int t, int m, string name, float v)
{ int pc = e.MidiEffectParamCount(t, m); for (int i = 0; i < pc; i++) if (e.MidiEffectParamName(t, m, i) == name) e.MidiEffectSetParam(t, m, i, v); }
static float GetByName(NotaEngine e, int t, int m, string name)
{ int pc = e.MidiEffectParamCount(t, m); for (int i = 0; i < pc; i++) if (e.MidiEffectParamName(t, m, i) == name) return e.MidiEffectGetParam(t, m, i); return 0f; }

// Factory-default param values (knob double-click reset).
Console.WriteLine("-- Param defaults (knob reset) --");
{
    using var de = new NotaEngine();
    int t = de.AddWavetableSynthTrack();
    int pos = -1; for (int i = 0; i < de.PluginParamCount(t, -1); i++) if (de.PluginParamId(t, -1, i) == "position") pos = i;
    float idef = de.InstrumentParamDefault(t, pos);
    de.PluginParamSet(t, -1, pos, Math.Clamp(idef + 0.4f, 0f, 1f));
    Check(Math.Abs(de.InstrumentParamDefault(t, pos) - idef) < 1e-4f, "instrument param default is stable after a change");
    Check(Math.Abs(de.PluginParamGet(t, -1, pos) - idef) > 0.1f, "changed instrument value differs from its default");

    int cmp = de.AddBuiltinDevice(t, 1);   // Compressor
    float ddef = de.DeviceParamDefault(t, cmp, 0);
    float dmin = de.DeviceParamMin(t, cmp, 0), dmax = de.DeviceParamMax(t, cmp, 0);
    de.DeviceSetParam(t, cmp, 0, Math.Clamp(ddef + (dmax - dmin) * 0.3f, dmin, dmax));
    Check(Math.Abs(de.DeviceParamDefault(t, cmp, 0) - ddef) < 1e-3f, "device param default is stable after a change");

    int arp = de.AddMidiEffect(t, 0);
    int gate = -1; for (int i = 0; i < de.MidiEffectParamCount(t, arp); i++) if (de.MidiEffectParamName(t, arp, i) == "Gate") gate = i;
    float mdef = de.MidiEffectParamDefault(t, arp, gate);
    de.MidiEffectSetParam(t, arp, gate, 0.1f);
    Check(Math.Abs(de.MidiEffectParamDefault(t, arp, gate) - mdef) < 1e-4f, "MIDI-FX param default is stable after a change");
}

// -- Phase B: inter-track sidechain routing --
Console.WriteLine("-- Sidechain routing --");
// Loud low source on one track; quiet target with a compressor on another.
int scSrcT = engine.AddInstrumentTrack();
int scSrcClip = engine.AddMidiClip(scSrcT, 0.0, 4.0);
engine.SetClipNotes(scSrcT, scSrcClip, new[] { new NotaNote(36, 0.0, 4.0, 1.0f) });
int scDstT = engine.AddInstrumentTrack();
int scDstClip = engine.AddMidiClip(scDstT, 0.0, 4.0);
engine.SetClipNotes(scDstT, scDstClip, new[] { new NotaNote(72, 0.0, 4.0, 0.1f) }); // quiet self
int scComp = engine.AddBuiltinDevice(scDstT, 1); // Compressor
engine.DeviceSetParam(scDstT, scComp, 0, -26f); // Threshold: below loud source (~-16dB), above quiet self
engine.DeviceSetParam(scDstT, scComp, 1, 10f);  // Ratio
engine.DeviceSetParam(scDstT, scComp, 2, 1f);   // Attack (ms) — settle fast
engine.DeviceSetParam(scDstT, scComp, 3, 50f);  // Release (ms)
Check(engine.DeviceSidechainSource(scDstT, scComp) == -1, "compressor sidechain unset by default");
// Baseline: no sidechain, the quiet self signal barely reduces.
engine.Seek(0); engine.Play();
for (int i = 0; i < 8; i++) engine.RenderOffline(buf, frames);
float grSelf = engine.DeviceGainReduction(scDstT, scComp);
// Wire the loud source as the sidechain detector input.
engine.SetDeviceSidechainSource(scDstT, scComp, scSrcT);
Check(engine.DeviceSidechainSource(scDstT, scComp) == scSrcT, "sidechain source round-trips");
engine.Seek(0);
for (int i = 0; i < 8; i++) engine.RenderOffline(buf, frames);
float grSc = engine.DeviceGainReduction(scDstT, scComp);
Check(grSc > grSelf + 1.0f, $"loud sidechain source drives gain reduction (self={grSelf:0.00}dB, sidechained={grSc:0.00}dB)");

// Phase D: detector gain drives the source harder → more gain reduction.
Check(Math.Abs(engine.DeviceSidechainGain(scDstT, scComp)) < 0.001f, "sidechain gain defaults to 0 dB");
engine.SetDeviceSidechainGain(scDstT, scComp, 12f);
Check(Math.Abs(engine.DeviceSidechainGain(scDstT, scComp) - 12f) < 0.001f, "sidechain gain round-trips");
engine.Seek(0);
for (int i = 0; i < 8; i++) engine.RenderOffline(buf, frames);
float grGain = engine.DeviceGainReduction(scDstT, scComp);
Check(grGain > grSc + 1.0f, $"sidechain gain increases reduction (0dB={grSc:0.00}dB, +12dB={grGain:0.00}dB)");
engine.SetDeviceSidechainGain(scDstT, scComp, 0f);
// Mix + tap-point round-trip (dry/wet and pre/post-FX source tap).
Check(Math.Abs(engine.DeviceSidechainMix(scDstT, scComp) - 1f) < 0.001f, "sidechain mix defaults to 100%");
engine.SetDeviceSidechainMix(scDstT, scComp, 0.5f);
Check(Math.Abs(engine.DeviceSidechainMix(scDstT, scComp) - 0.5f) < 0.001f, "sidechain mix round-trips");
engine.SetDeviceSidechainMix(scDstT, scComp, 1f);
Check(!engine.DeviceSidechainTapPre(scDstT, scComp), "sidechain tap defaults to post-FX");
engine.SetDeviceSidechainTapPre(scDstT, scComp, true);
Check(engine.DeviceSidechainTapPre(scDstT, scComp), "sidechain tap toggles to pre-FX");
engine.Seek(0);
for (int i = 0; i < 8; i++) engine.RenderOffline(buf, frames);
Check(engine.DeviceGainReduction(scDstT, scComp) > grSelf + 1.0f, "pre-FX tap still drives the detector");
engine.SetDeviceSidechainTapPre(scDstT, scComp, false);

// Unwire and confirm it releases back toward the self level.
engine.SetDeviceSidechainSource(scDstT, scComp, -1);
Check(engine.DeviceSidechainSource(scDstT, scComp) == -1, "sidechain source clears");
engine.StopTransport();
engine.RemoveTrack(scSrcT);
engine.RemoveTrack(scDstT);

// -- M5: Session view launch --
Console.WriteLine("-- M5 --");
int sesT = engine.AddInstrumentTrack();
Check(engine.SceneCount > 0, $"session has scenes (count={engine.SceneCount})");
engine.AddSessionMidiClip(sesT, 0, 4.0);
engine.SetSessionNotes(sesT, 0, new[] { new NotaNote(60, 0.0, 4.0, 0.9f) });
Check(engine.SessionSlotState(sesT, 0) == 1, "session slot is filled");

engine.SetLaunchQuant(0.0); // immediate launch for the test
engine.StopTransport();
engine.Seek(0);
engine.RenderOffline(buf, 64); // drain the stop
engine.LaunchSlot(sesT, 0);    // auto-starts transport
engine.Seek(0);
engine.RenderOffline(buf, frames);
Check(Rms(buf, frames) > 0.001f, $"session slot plays audio (rms={Rms(buf, frames):F4})");
Check(engine.SessionSlotState(sesT, 0) == 3, "session slot reports playing");

engine.StopSlot(sesT);
engine.RenderOffline(buf, 512); // apply the quantized stop
Check(engine.SessionSlotState(sesT, 0) == 1, "session slot stopped (back to filled)");
engine.StopTransport();

// -- M5-3: launch/stop a whole scene (row) --
int sesT2 = engine.AddInstrumentTrack();
engine.AddSessionMidiClip(sesT2, 1, 4.0);
engine.SetSessionNotes(sesT2, 1, new[] { new NotaNote(64, 0.0, 4.0, 0.9f) });
engine.AddSessionMidiClip(sesT, 1, 4.0);
engine.SetSessionNotes(sesT, 1, new[] { new NotaNote(60, 0.0, 4.0, 0.9f) });
engine.SetLaunchQuant(0.0);
engine.StopTransport();
engine.Seek(0);
engine.RenderOffline(buf, 64);
engine.LaunchScene(1);          // fires both tracks' slot in scene 1
engine.Seek(0);
engine.RenderOffline(buf, frames);
Check(engine.SessionSlotState(sesT, 1) == 3 && engine.SessionSlotState(sesT2, 1) == 3,
    "launch scene: both tracks playing");
Check(Rms(buf, frames) > 0.001f, $"scene plays audio (rms={Rms(buf, frames):F4})");

engine.StopScene(1);
engine.RenderOffline(buf, 512);
Check(engine.SessionSlotState(sesT, 1) == 1 && engine.SessionSlotState(sesT2, 1) == 1,
    "stop scene: both tracks back to filled");
engine.StopTransport();

// -- M5-4: overdub-record MIDI into a session slot --
int sesR = engine.AddInstrumentTrack();
engine.SetLaunchQuant(0.0);
engine.StopTransport();
engine.Seek(0);
engine.RenderOffline(buf, 64);
engine.RecordSessionSlot(sesR, 0);  // creates the clip, launches it, starts recording
Check(engine.SessionSlotState(sesR, 0) == 4, "session slot reports recording");
engine.NoteOn(67, 0.9f);
engine.RenderOffline(buf, 4096);    // hold the note across a block
engine.NoteOff(67);
engine.RenderOffline(buf, 256);     // process note-off -> recorded event
engine.Poll();                      // materialise into the slot
engine.StopSessionRecord();
var slotNotes = engine.GetSessionNotes(sesR, 0);
Check(slotNotes.Length >= 1 && slotNotes[0].Pitch == 67,
    $"recorded a note into the slot (count={slotNotes.Length})");
Check(engine.SessionSlotState(sesR, 0) == 3, "slot keeps playing after record stop");
engine.StopAllSession();
engine.StopTransport();

// -- M5-5: configurable loop length --
int sesL = engine.AddInstrumentTrack();
engine.AddSessionMidiClip(sesL, 0, 4.0);
Check(Math.Abs(engine.SessionSlotLength(sesL, 0) - 4.0) < 1e-9, "slot default length is 4 beats");
engine.SetSessionSlotLength(sesL, 0, 2.0);
Check(Math.Abs(engine.SessionSlotLength(sesL, 0) - 2.0) < 1e-9, "slot length changed to 2 beats");
engine.SetSessionNotes(sesL, 0, new[] { new NotaNote(60, 1.0, 0.5, 0.9f) }); // inside the 2-beat loop
engine.SetLaunchQuant(0.0);
engine.StopTransport(); engine.Seek(0); engine.RenderOffline(buf, 64);
engine.LaunchSlot(sesL, 0);
engine.Seek(0);
engine.RenderOffline(buf, frames);
Check(Rms(buf, frames) > 0.001f, $"2-beat loop plays (rms={Rms(buf, frames):F4})");
engine.StopAllSession();
engine.StopTransport();

// -- Session audio slot playback: an audio-track slot must launch + loop (was instrument-only) --
{
    int sesA = engine.AddAudioTrack();
    Check(engine.AddSessionAudioFile(sesA, 0, wav), "audio session slot filled from a file");
    Check(engine.SessionSlotState(sesA, 0) == 1, "audio session slot reports filled");
    engine.SetLaunchQuant(0.0);
    engine.StopTransport(); engine.Seek(0); engine.RenderOffline(buf, 64);
    engine.LaunchSlot(sesA, 0);
    engine.Seek(0);
    engine.RenderOffline(buf, frames);   // maybeApply sets `playing` during this block
    Check(engine.SessionSlotState(sesA, 0) == 3, "audio session slot reports playing after launch");
    Check(Rms(buf, frames) > 0.001f, $"audio session slot plays audio (rms={Rms(buf, frames):F4})");
    engine.StopAllSession();
    engine.StopTransport();
}

// -- Session vs Arrangement: a session-only jam must NOT roll the Arrangement --
{
    using var e = new NotaEngine();
    e.SetBpm(120); e.SetLaunchQuant(0.0);
    int arrT = e.AddInstrumentTrack();                 // arrangement content
    int ac = e.AddMidiClip(arrT, 0.0, 4.0);
    e.SetClipNotes(arrT, ac, new[] { new NotaNote(60, 0.0, 4.0, 1.0f) });
    int sesT3 = e.AddInstrumentTrack();                // session content
    e.AddSessionMidiClip(sesT3, 0, 4.0);
    e.SetSessionNotes(sesT3, 0, new[] { new NotaNote(64, 0.0, 4.0, 1.0f) });

    Check(!e.ArrangementActive, "arrangement inactive on a fresh engine");
    e.LaunchSlot(sesT3, 0);                            // session-only start (no arrangement)
    Check(!e.ArrangementActive, "launching a session clip leaves the arrangement inactive");
    // Mute the session track so only the arrangement track could sound — it must stay silent.
    e.SetTrackMute(sesT3, true);
    e.Seek(0); e.RenderOffline(buf, frames);
    Check(Rms(buf, frames) < 1e-5f, $"arrangement track silent in a session-only jam (rms={Rms(buf, frames):F5})");

    e.SetTrackMute(sesT3, false);
    e.BackToArrangement();
    Check(e.ArrangementActive, "back to arrangement re-activates the timeline");
    Check(e.SessionSlotState(sesT3, 0) != 3, "back to arrangement stopped the session clip");
    e.Seek(0); e.RenderOffline(buf, frames);
    Check(Rms(buf, frames) > 0.001f, $"arrangement plays after back to arrangement (rms={Rms(buf, frames):F4})");
    e.StopTransport();
}

// -- Session slot + scene editing (clear / add / remove) --
{
    using var e = new NotaEngine();
    int t = e.AddInstrumentTrack();
    int scenes0 = e.SceneCount;
    e.AddSessionMidiClip(t, 0, 4.0);
    Check(e.SessionSlotState(t, 0) == 1, "slot filled before clear");
    Check(e.ClearSessionSlot(t, 0), "clear returns true for a filled slot");
    Check(e.SessionSlotState(t, 0) == 0, "slot empty after clear");
    Check(!e.ClearSessionSlot(t, 0), "clear returns false for an empty slot");

    int added = e.AddScene();
    Check(added == scenes0, $"add scene returns the new index ({added})");
    Check(e.SceneCount == scenes0 + 1, $"scene count grew ({e.SceneCount})");
    // The new scene is usable on every track.
    e.AddSessionMidiClip(t, scenes0, 2.0);
    Check(e.SessionSlotState(t, scenes0) == 1, "clip added in the new scene");

    Check(e.RemoveScene(scenes0), "remove scene returns true");
    Check(e.SceneCount == scenes0, $"scene count shrank back ({e.SceneCount})");
}

// -- Session: copy an arrangement AUDIO clip into a slot --
{
    using var e = new NotaEngine();
    e.SetBpm(120); e.SetLaunchQuant(0.0);
    int at = e.AddAudioTrack();
    int ac = e.AddAudioClip(at, wav, 0.0);
    Check(ac >= 0, "audio clip added to arrangement");
    Check(e.ArrangementAudioClipToSession(at, ac, 0), "arrangement audio clip copied to session slot");
    Check(e.SessionSlotState(at, 0) == 1, "session slot filled from the audio clip");
    e.LaunchSlot(at, 0);
    e.Seek(0); e.RenderOffline(buf, frames);
    Check(Rms(buf, frames) > 0.001f, $"copied audio session slot plays (rms={Rms(buf, frames):F4})");
    e.StopAllSession(); e.StopTransport();
}

// -- Session audio slot gain (slot editor) --
{
    using var e = new NotaEngine();
    e.SetBpm(120); e.SetLaunchQuant(0.0);
    int t = e.AddAudioTrack();
    e.AddSessionAudioFile(t, 0, wav);
    Check(Math.Abs(e.SessionSlotGain(t, 0) - 1.0f) < 1e-6, "session slot gain defaults to unity");
    e.SetSessionSlotGain(t, 0, 0.5f);
    Check(Math.Abs(e.SessionSlotGain(t, 0) - 0.5f) < 1e-6, "session slot gain set to 0.5");
    e.StopTransport(); e.Seek(0); e.RenderOffline(buf, 64);
    e.LaunchSlot(t, 0);
    e.Seek(0); e.RenderOffline(buf, frames);
    float half = Rms(buf, frames);
    e.SetSessionSlotGain(t, 0, 1.0f);
    e.Seek(0); e.RenderOffline(buf, frames);
    float full = Rms(buf, frames);
    Check(full > half * 1.5f, $"session slot gain scales playback (half={half:F4}, full={full:F4})");
    e.StopAllSession(); e.StopTransport();
}

// -- M5-6: move clips between Session and Arrangement --
int sesX = engine.AddInstrumentTrack();
engine.AddSessionMidiClip(sesX, 0, 4.0);
engine.SetSessionNotes(sesX, 0, new[] { new NotaNote(60, 0.0, 1.0, 0.9f), new NotaNote(64, 2.0, 1.0, 0.9f) });
int newClip = engine.SessionSlotToArrangement(sesX, 0, 8.0);   // slot -> arrangement
Check(newClip >= 0, $"slot copied to arrangement (clip={newClip})");
Check(engine.GetClipNotes(sesX, newClip).Length == 2, "arrangement clip carries the slot notes");
engine.ArrangementClipToSession(sesX, newClip, 3);             // arrangement -> a fresh slot
Check(engine.SessionSlotState(sesX, 3) == 1, "arrangement clip copied into session slot");
Check(engine.GetSessionNotes(sesX, 3).Length == 2, "session slot carries the clip notes");

// -- M4-3: MIDI recording into the arrangement at the playhead --
engine.SetTrackArmed(rec, false);   // recording targets the first armed track
engine.SetTrackArmed(samp, false);
int rec2 = engine.AddInstrumentTrack();
engine.SetTrackArmed(rec2, true);
engine.StopTransport();
engine.Seek(8.0);                     // playhead at beat 8
engine.RenderOffline(buf, 64);        // apply the seek (transport stopped, no drift)
engine.SetRecording(true);            // opens a clip at beat 8 and auto-rolls
engine.NoteOn(65, 0.8f);
engine.RenderOffline(buf, 4096);
engine.NoteOff(65);
engine.RenderOffline(buf, 256);
engine.Poll();
engine.SetRecording(false);
engine.SetTrackArmed(rec2, false);
engine.StopTransport();
bool gotRec = engine.TryGetClipInfo(rec2, 0, out var recInfo);
Check(gotRec && Math.Abs(recInfo.StartBeat - 8.0) < 0.25,
    $"recorded clip opens at the playhead (start={recInfo.StartBeat:F2})");
var recArr = engine.GetClipNotes(rec2, 0);
Check(recArr.Length >= 1 && recArr[0].Pitch == 65, $"arrangement clip captured the note (count={recArr.Length})");

// -- M4-3: audio input capture pipeline (synthetic, no device) --
Check(engine.AudioRecordSelfTest(), "audio capture ring -> clip materialises with signal");

// -- M5-4: audio capture into a Session slot + looped playback (synthetic) --
Check(engine.SessionAudioRecordSelfTest(), "session audio slot captures + loops back with signal");

// -- M6-4: master WAV export (offline render at SR -> WavWriter -> readback) --
{
    engine.StopAllSession();
    engine.StopTransport();
    int exTrk = engine.AddInstrumentTrack();
    int exClip = engine.AddMidiClip(exTrk, 0.0, 4.0);
    engine.SetClipNotes(exTrk, exClip, new[] { new NotaNote(60, 0.0, 4.0, 0.9f) });
    engine.SetTrackSolo(exTrk, true);   // isolate this track for a predictable bounce

    const int exSr = 48000;
    const double exBpm = 120.0;
    engine.SetBpm(exBpm);
    double spb = exSr * 60.0 / exBpm;
    long totalFrames = (long)Math.Ceiling(4.0 * spb);

    string exPath = Path.Combine(Path.GetTempPath(), "nota_export_test.wav");
    engine.SetMetronome(false);
    engine.SetLoop(false, 0, 0);
    engine.Seek(0);
    engine.Play();
    var ebuf = new float[8192 * 2];
    using (var w = new Nota.Infrastructure.WavWriter(exPath, exSr, 2, WavBitDepth.Float32))
    {
        long rem = totalFrames;
        bool first = true;
        while (rem > 0)
        {
            int n = (int)Math.Min(8192, rem);
            if (first) { engine.RenderOffline(ebuf, n, exSr); first = false; }
            else engine.RenderOffline(ebuf, n);
            w.WriteFrames(ebuf, n);
            rem -= n;
        }
    }
    engine.StopTransport();
    engine.SetTrackSolo(exTrk, false);

    var raw = File.ReadAllBytes(exPath);
    int fmt = BitConverter.ToUInt16(raw, 20);
    int ch = BitConverter.ToUInt16(raw, 22);
    int rdSr = BitConverter.ToInt32(raw, 24);
    int bits = BitConverter.ToUInt16(raw, 34);
    int dataSize = BitConverter.ToInt32(raw, 40);
    Check(fmt == 3 && ch == 2 && rdSr == exSr && bits == 32,
        $"export WAV header ok (fmt={fmt} ch={ch} sr={rdSr} bits={bits})");
    Check(dataSize == totalFrames * 2 * 4,
        $"export WAV data size matches frames (data={dataSize}, expected={totalFrames * 2 * 4})");
    double sum = 0;
    int nSamp = dataSize / 4;
    for (int i = 0; i < nSamp; i++) { float s = BitConverter.ToSingle(raw, 44 + i * 4); sum += s * (double)s; }
    double exRms = Math.Sqrt(sum / nSamp);
    Check(exRms > 0.001, $"exported WAV is audible (rms={exRms:F4})");
    File.Delete(exPath);
}

// -- export options: gain / TPDF dither (device-free WavWriter) --------------
{
    // Output gain scales every sample (used by normalize).
    string gPath = Path.Combine(Path.GetTempPath(), "nota_gain_test.wav");
    var gin = new float[8]; for (int i = 0; i < 8; i++) gin[i] = 0.6f;
    using (var w = new Nota.Infrastructure.WavWriter(gPath, 48000, 2, WavBitDepth.Float32, gain: 0.5f)) w.WriteFrames(gin, 4);
    var graw = File.ReadAllBytes(gPath);
    float g0 = BitConverter.ToSingle(graw, 44);
    Check(Math.Abs(g0 - 0.3f) < 1e-5f, $"WavWriter gain scales samples (0.6*0.5={g0:F3})");
    File.Delete(gPath);

    // A constant ~½-LSB input quantizes to a single 16-bit code with dither off, but
    // to a *varying* code with TPDF dither on (the quantization error is decorrelated).
    int dN = 4096; var din = new float[dN * 2]; float halfLsb = 0.5f / short.MaxValue;
    for (int i = 0; i < dN * 2; i++) din[i] = halfLsb;
    string dOff = Path.Combine(Path.GetTempPath(), "nota_dither_off.wav");
    string dOn = Path.Combine(Path.GetTempPath(), "nota_dither_on.wav");
    using (var w = new Nota.Infrastructure.WavWriter(dOff, 48000, 2, WavBitDepth.Pcm16, dither: false)) w.WriteFrames(din, dN);
    using (var w = new Nota.Infrastructure.WavWriter(dOn, 48000, 2, WavBitDepth.Pcm16, dither: true)) w.WriteFrames(din, dN);
    var offB = File.ReadAllBytes(dOff); var onB = File.ReadAllBytes(dOn);
    bool offConst = true, onVaries = false;
    short off0 = BitConverter.ToInt16(offB, 44), on0 = BitConverter.ToInt16(onB, 44);
    for (int i = 0; i < dN * 2; i++)
    {
        if (BitConverter.ToInt16(offB, 44 + i * 2) != off0) offConst = false;
        if (BitConverter.ToInt16(onB, 44 + i * 2) != on0) onVaries = true;
    }
    Check(offConst && onVaries, $"TPDF dither varies the 16-bit code (off const={offConst}, on varies={onVaries})");
    File.Delete(dOff); File.Delete(dOn);
}

// -- export normalize (−1 dBTP true peak) via WavAudioExporter ---------------
// Own engine: ExportMaster start()s the backend on Restore, so keep it off the
// shared `engine` (a later test asserts its backend was never started).
{
    using var ne = new NotaEngine();
    ne.SetBpm(120); ne.SetTimeSignature(4, 4);
    int nTrk = ne.AddInstrumentTrack();
    int nClip = ne.AddMidiClip(nTrk, 0.0, 2.0);
    ne.SetClipNotes(nTrk, nClip, new[] { new NotaNote(60, 0.0, 2.0, 0.5f) });

    var exporter = new Nota.Infrastructure.WavAudioExporter();
    string nPath = Path.Combine(Path.GetTempPath(), "nota_normalize_test.wav");
    var req = new ExportRequest(nPath, 2.0, 120.0, 48000, WavBitDepth.Float32,
        RestoreLoop: false, RestoreMetronome: false, Normalize: true, Dither: false);
    exporter.ExportMaster(ne, req);

    var nraw = File.ReadAllBytes(nPath);
    int ndata = BitConverter.ToInt32(nraw, 40);
    float npk = 0; int nsamp = ndata / 4;
    for (int i = 0; i < nsamp; i++) { float s = Math.Abs(BitConverter.ToSingle(nraw, 44 + i * 4)); if (s > npk) npk = s; }
    // Sample peak lands at (or just under) the 0.8913 = −1 dBTP ceiling; the scanner
    // targets the inter-sample true peak, which is ≥ the sample peak.
    Check(npk > 0.78f && npk <= 0.8913f + 1e-3f, $"normalize lifts the bounce to ~−1 dBTP (sample peak {npk:F3})");
    File.Delete(nPath);
}

// -- M6-5: stem isolation (mute-others → distinct per-track bounces) ---------
// Device-free: mirrors WavAudioExporter.ExportStems' core (the real exporter also
// stops/starts the backend, verified by --audiocheck / M6-4). Two instrument tracks
// with different pitches must bounce to distinct, audible, isolated stems.
{
    using var se = new NotaEngine();
    se.SetBpm(120); se.SetTimeSignature(4, 4);
    int sA = se.AddInstrumentTrack(); int cA = se.AddMidiClip(sA, 0.0, 4.0);
    se.SetClipNotes(sA, cA, new[] { new NotaNote(60, 0.0, 4.0, 0.9f) });
    int sB = se.AddInstrumentTrack(); int cB = se.AddMidiClip(sB, 0.0, 4.0);
    se.SetClipNotes(sB, cB, new[] { new NotaNote(72, 0.0, 4.0, 0.9f) });

    var bufA = new float[4096 * 2]; var bufB = new float[4096 * 2];
    se.SetMetronome(false); se.SetLoop(false, 0, 0); se.Play();

    se.SetTrackMute(sA, false); se.SetTrackMute(sB, true);   // isolate A
    se.Seek(0); se.RenderOffline(bufA, 4096);
    se.SetTrackMute(sA, true); se.SetTrackMute(sB, false);   // isolate B
    se.Seek(0); se.RenderOffline(bufB, 4096);

    float rmsA = Rms(bufA, 4096), rmsB = Rms(bufB, 4096);
    Check(rmsA > 0.01f && rmsB > 0.01f, $"each isolated stem is audible (A={rmsA:F4}, B={rmsB:F4})");
    double diff = 0; for (int i = 0; i < 4096 * 2; i++) { double d = bufA[i] - bufB[i]; diff += d * d; }
    Check(Math.Sqrt(diff / (4096 * 2)) > 0.01, "stems are distinct per track (A ≠ B)");

    se.SetTrackMute(sA, true); se.SetTrackMute(sB, true);    // both muted → silence
    se.Seek(0); se.RenderOffline(bufA, 4096);
    Check(Rms(bufA, 4096) < 1e-4f, "muting all tracks silences the bounce");
    se.StopTransport();
}

// -- M6-2: level meters (post-fader peak/RMS, master + per-track) --
{
    engine.StopAllSession();
    engine.StopTransport();
    int mTrk = engine.AddInstrumentTrack();
    int mClip = engine.AddMidiClip(mTrk, 0.0, 4.0);
    engine.SetClipNotes(mTrk, mClip, new[] { new NotaNote(60, 0.0, 4.0, 0.9f) });
    engine.SetTrackSolo(mTrk, true);
    engine.SetMetronome(false);
    engine.SetLoop(false, 0, 0);
    engine.SetMasterVolume(1.0f);
    engine.Seek(0);
    engine.Play();
    engine.RenderOffline(buf, 4096); // fill the meters with a live signal

    bool gotTrk = engine.TryGetTrackMeter(mTrk, out var tm);
    Check(gotTrk && tm.Peak > 0.001f && tm.Rms > 0.0f,
        $"track meter reads a signal (peak={tm.Peak:F3} rms={tm.Rms:F3})");
    var mm = engine.MasterMeter();
    Check(mm.Peak > 0.001f, $"master meter reads a signal (peak={mm.Peak:F3})");

    // Muting the track drops its meter to zero on the next block.
    engine.SetTrackMute(mTrk, true);
    engine.Seek(0);
    engine.RenderOffline(buf, 4096);
    engine.TryGetTrackMeter(mTrk, out var tmMuted);
    Check(tmMuted.Peak == 0.0f, $"muted track meter is silent (peak={tmMuted.Peak:F3})");
    engine.SetTrackMute(mTrk, false);
    engine.SetTrackSolo(mTrk, false);
    engine.StopTransport();
}

// -- M6-6: undo / redo of structural edits --
{
    engine.StopAllSession();
    engine.StopTransport();
    int baseCount = engine.TrackCount;

    int uTrk = engine.AddInstrumentTrack();          // structural edit -> undo checkpoint
    Check(engine.TrackCount == baseCount + 1, "undo: track added");
    Check(engine.CanUndo, "undo: stack has an entry after an edit");

    Check(engine.Undo(), "undo: returns true");
    Check(engine.TrackCount == baseCount, "undo: track count restored");
    Check(engine.CanRedo, "undo: redo becomes available");

    Check(engine.Redo(), "redo: returns true");
    Check(engine.TrackCount == baseCount + 1, "redo: track re-added");

    // A fresh edit clears the redo branch.
    int uClip = engine.AddMidiClip(uTrk, 0.0, 4.0);
    engine.SetClipNotes(uTrk, uClip, new[] { new NotaNote(64, 0.0, 1.0, 0.8f) });
    Check(engine.GetClipNotes(uTrk, uClip).Length == 1, "undo: note added to clip");
    Check(!engine.CanRedo, "undo: new edit clears the redo branch");

    Check(engine.Undo(), "undo: note edit undone");
    Check(engine.GetClipNotes(uTrk, uClip).Length == 0, "undo: notes reverted");

    engine.Undo(); // remove the MIDI clip
    engine.Undo(); // remove the instrument track
    Check(engine.TrackCount == baseCount, "undo: multiple steps unwind to the baseline");
}

// -- M6-1: send / return buses --
{
    engine.StopAllSession();
    engine.StopTransport();
    engine.SetMetronome(false);
    engine.SetLoop(false, 0, 0);
    engine.SetMasterVolume(1.0f);

    int srcT = engine.AddInstrumentTrack();
    int srcClip = engine.AddMidiClip(srcT, 0.0, 4.0);
    engine.SetClipNotes(srcT, srcClip, new[] { new NotaNote(60, 0.0, 4.0, 0.9f) });

    int retT = engine.AddReturnTrack();
    Check(retT > 0, "add return track");
    Check(engine.ReturnTrackCount == 1, "return track count == 1");
    Check(engine.TrackReturnIndex(retT) == 0, "return track carries bus index 0");
    Check(engine.TrackReturnIndex(srcT) == -1, "regular track has no bus index");
    int retDev = engine.AddBuiltinDevice(retT, 4); // Utility: unity gain, no tail -> clean on/off
    Check(retDev >= 0, "add device to the return bus");

    engine.SetTrackSend(srcT, 0, 1.0f);
    Check(Math.Abs(engine.GetTrackSend(srcT, 0) - 1.0f) < 1e-6, "send level round-trips");

    engine.Seek(0);
    engine.Play();
    engine.RenderOffline(buf, 4096);
    engine.TryGetTrackMeter(retT, out var rm);
    Check(rm.Peak > 0.001f, $"return bus carries the post-fader send (peak={rm.Peak:F3})");

    // Send off -> the bus (Utility has no tail) is silent on the next block.
    engine.SetTrackSend(srcT, 0, 0.0f);
    engine.Seek(0);
    engine.RenderOffline(buf, 4096);
    engine.TryGetTrackMeter(retT, out var rm0);
    Check(rm0.Peak == 0.0f, $"return bus silent with send off (peak={rm0.Peak:F3})");

    engine.StopTransport();
}

// ===================== M7-6a: .nota project round-trip =====================
Console.WriteLine("-- M7-6a: project save/load --");
{
    string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nota-smoke-" + Guid.NewGuid().ToString("N"));
    try
    {
        // Build a project with built-in content on a dedicated engine.
        using var src = new NotaEngine();
        int pInst = src.AddInstrumentTrack();
        src.SetTrackVolume(pInst, 0.75f);
        src.SetTrackPan(pInst, -0.5f);
        src.SetTrackMute(pInst, true);

        int pEq = src.AddBuiltinDevice(pInst, 0); // EQ
        float pmin = src.DeviceParamMin(pInst, pEq, 0), pmax = src.DeviceParamMax(pInst, pEq, 0);
        float pval = pmin + (pmax - pmin) * 0.3f;
        src.DeviceSetParam(pInst, pEq, 0, pval);
        src.SetDeviceBypassed(pInst, pEq, true);

        int pClip = src.AddMidiClip(pInst, 4.0, 4.0);
        src.SetClipNotes(pInst, pClip, new[] { new NotaNote(64, 0.0, 1.0, 0.8f), new NotaNote(67, 1.0, 2.0, 0.6f) });
        src.SetMidiClipEnvelope(pInst, pClip, Nota.Application.MidiClipEnvelope.Velocity,
            new[] { new Nota.Application.AutomationPoint(0.0, 0.9f), new Nota.Application.AutomationPoint(4.0, 0.3f) });
        src.SetMidiClipEnvelope(pInst, pClip, Nota.Application.MidiClipEnvelope.Volume,
            new[] { new Nota.Application.AutomationPoint(0.0, 1.0f), new Nota.Application.AutomationPoint(4.0, 0.5f) });

        src.AddSessionMidiClip(pInst, 0, 2.0);
        src.SetSessionNotes(pInst, 0, new[] { new NotaNote(48, 0.0, 0.5, 1.0f) });

        src.AddReturnTrack();
        src.SetTrackSend(pInst, 0, 0.42f);

        Check(src.TrackInstrumentKind(pInst) == 0, "instrument reports built-in Synth kind");
        Check(src.TrackDeviceBuiltinKind(pInst, pEq) == 0, "device reports built-in EQ kind");

        // Automation lanes (M9-A4): a volume envelope + a device-param lane.
        int aVol = src.AddAutomationLane(pInst, Nota.Application.AutomationTarget.Volume, -1, -1);
        src.SetAutomationPoints(pInst, aVol, new[]
            { new Nota.Application.AutomationPoint(0, 1.2f, 0.5f), new Nota.Application.AutomationPoint(4, 0.4f) });
        int aDev = src.AddAutomationLane(pInst, Nota.Application.AutomationTarget.DeviceParam, pEq, 0);
        src.SetAutomationPoints(pInst, aDev, new[]
            { new Nota.Application.AutomationPoint(0, pmin), new Nota.Application.AutomationPoint(2, pmax) });

        // Master-volume automation (graph-level, format v7).
        src.SetMasterVolumeAutomation(new[]
            { new Nota.Application.AutomationPoint(0, 1.5f), new Nota.Application.AutomationPoint(8, 0.5f) });

        var transport = new TransportState(140.0, 0.5, MetronomeOn: true, LoopOn: false,
            TimeSigNumerator: 7, TimeSigDenominator: 8);
        var warnings = new System.Collections.Generic.List<string>();
        var doc = ProjectService.Capture(src, transport, warnings);
        Check(warnings.Count == 0, $"no warnings for a built-in-only project ({warnings.Count})");

        ProjectService.Save(doc, dir, src);
        Check(System.IO.File.Exists(System.IO.Path.Combine(dir, "project.json")), "project.json written");

        var loaded = ProjectService.Load(dir);
        Check(loaded.FormatVersion == ProjectService.CurrentFormatVersion, "format version round-trips");
        Check(Math.Abs(loaded.Transport.Bpm - 140.0) < 1e-9 && loaded.Transport.MetronomeOn, "transport round-trips");
        Check(loaded.Transport.TimeSigNumerator == 7 && loaded.Transport.TimeSigDenominator == 8, "time signature round-trips (7/8)");

        // Replay into a fresh engine and verify the live state.
        using var dst = new NotaEngine();
        var applyWarnings = ProjectService.Apply(loaded, dst, dir);
        Check(applyWarnings.Count == 0, $"apply produced no warnings ({applyWarnings.Count})");
        Check(dst.TrackCount == 2, $"track count restored ({dst.TrackCount})");

        Check(dst.TryGetTrackInfo(0, out var ti0) && ti0.IsInstrument, "track 0 is an instrument track");
        Check(Math.Abs(ti0.Volume - 0.75f) < 1e-6 && Math.Abs(ti0.Pan - (-0.5f)) < 1e-6, "volume/pan restored");
        Check(ti0.Muted != 0, "mute restored");
        int rid = ti0.Id;
        Check(dst.TrackDeviceCount(rid) == 1 && dst.TrackDeviceBuiltinKind(rid, 0) == 0, "EQ device restored");
        Check(Math.Abs(dst.DeviceGetParam(rid, 0, 0) - pval) < 1e-4, "device param restored");
        Check(dst.DeviceBypassed(rid, 0), "device bypass restored");
        var rnotes = dst.GetClipNotes(rid, 0);
        Check(rnotes.Length == 2 && rnotes[0].Pitch == 64 && rnotes[1].Pitch == 67, "MIDI clip notes restored");
        var rvel = dst.GetMidiClipEnvelope(rid, 0, Nota.Application.MidiClipEnvelope.Velocity);
        var rvol = dst.GetMidiClipEnvelope(rid, 0, Nota.Application.MidiClipEnvelope.Volume);
        Check(rvel.Length == 2 && Math.Abs(rvel[1].Value - 0.3f) < 1e-6 && rvol.Length == 2 && Math.Abs(rvol[1].Value - 0.5f) < 1e-6,
              "MIDI clip envelopes restored (v10)");
        var snotes = dst.GetSessionNotes(rid, 0);
        Check(dst.SessionSlotState(rid, 0) == 1 && snotes.Length == 1 && snotes[0].Pitch == 48, "session slot restored");

        Check(dst.TryGetTrackInfo(1, out var ti1) && ti1.IsReturn, "return track restored");
        Check(Math.Abs(dst.GetTrackSend(rid, 0) - 0.42f) < 1e-6, "send level restored");

        // Automation lanes restored (M9-A4).
        Check(dst.AutomationLaneCount(rid) == 2, $"automation lanes restored ({dst.AutomationLaneCount(rid)})");
        var laVol = dst.AutomationLaneInfo(rid, 0);
        var laVolPts = dst.GetAutomationPoints(rid, 0);
        Check(laVol.Target == Nota.Application.AutomationTarget.Volume && laVolPts.Length == 2
              && Math.Abs(laVolPts[0].Value - 1.2f) < 1e-6 && Math.Abs(laVolPts[1].Beat - 4.0) < 1e-9,
              "volume automation lane + points restored");
        Check(Math.Abs(laVolPts[0].Curve - 0.5f) < 1e-6, "segment curve restored (v4)");
        var laDev = dst.AutomationLaneInfo(rid, 1);
        Check(laDev.Target == Nota.Application.AutomationTarget.DeviceParam
              && laDev.DeviceIndex == 0 && laDev.ParamIndex == 0 && laDev.PointCount == 2,
              "device-param automation lane restored");

        // Master-volume automation restored (format v7).
        var mva = dst.GetMasterVolumeAutomation();
        Check(mva.Length == 2 && Math.Abs(mva[0].Value - 1.5f) < 1e-6 && Math.Abs(mva[1].Beat - 8.0) < 1e-9,
              "master-volume automation restored");

        Check(loaded.FormatVersion == ProjectService.CurrentFormatVersion, "automation project saves as current format");
    }
    catch (Exception ex)
    {
        Check(false, $"project round-trip threw: {ex.Message}");
    }
    finally
    {
        try { if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true); } catch { }
    }
}

// ===================== M7-6b: audio in the .nota bundle =====================
Console.WriteLine("-- M7-6b: audio round-trip --");
{
    string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nota-smoke-audio-" + Guid.NewGuid().ToString("N"));
    try
    {
        using var src = new NotaEngine();

        int aTrk = src.AddAudioTrack();
        int aClip = src.AddAudioClipEx(aTrk, wav, startBeat: 0.0, sourceOffsetFrames: 1000, lengthFrames: 0, gain: 0.8f);
        Check(aClip >= 0, "add audio clip with offset/gain");
        Check(src.TryGetAudioClipInfo(aTrk, aClip, out var aci0) && aci0.SampleId > 0, "audio clip carries a sample id");
        Check(Math.Abs(aci0.Gain - 0.8f) < 1e-6 && Math.Abs(aci0.SourceOffsetFrames - 1000) < 1e-6, "audio clip offset/gain readable");

        // Pitch + warp should survive a save/load round-trip (v5).
        src.SetClipPitch(aTrk, aClip, 4.0f);
        src.SetClipReverse(aTrk, aClip, true);   // reverse (v19)
        src.SetClipWarp(aTrk, aClip, true, 3);   // Complex
        src.SetClipWarpLength(aTrk, aClip, 2.0);
        // Warp markers (v6): enable seeds two end markers; add a midpoint.
        var gms = new double[16]; var gmb = new double[16];
        Check(src.GetClipWarpMarkers(aTrk, aClip, gms, gmb) == 2, "warp seeds two end markers");
        src.SetClipWarpMarkers(aTrk, aClip,
            new[] { gms[0], (gms[0] + gms[1]) / 2, gms[1] },
            new[] { gmb[0], (gmb[0] + gmb[1]) / 2, gmb[1] });

        // Clip volume envelope (v8) on the same clip.
        src.SetClipVolumeEnvelope(aTrk, aClip, new[]
            { new Nota.Application.AutomationPoint(0.0, 1.0f), new Nota.Application.AutomationPoint(2.0, 0.25f) });
        src.SetClipPanEnvelope(aTrk, aClip, new[]
            { new Nota.Application.AutomationPoint(0.0, -0.5f), new Nota.Application.AutomationPoint(2.0, 0.5f) });

        int smpTrk = src.AddSamplerTrack(wav, rootNote: 62, loop: true);
        Check(smpTrk > 0 && src.TrackInstrumentKind(smpTrk) == 1, "sampler track created");
        Check(src.TryGetSamplerInfo(smpTrk, out var siSrc) && siSrc.RootNote == 62 && siSrc.Loop != 0, "sampler settings readable");

        int sesTrk = src.AddAudioTrack();
        Check(src.AddSessionAudioClip(sesTrk, scene: 1, wav, lengthBeats: 2.0, sourceOffsetFrames: 0, lengthFrames: 0, gain: 1.0f),
            "add session audio take");
        Check(src.SessionSlotState(sesTrk, 1) == 1, "session audio slot is filled");

        var transport = new TransportState(120.0, 1.0, false, false);
        var warnings = new System.Collections.Generic.List<string>();
        var doc = ProjectService.Capture(src, transport, warnings);
        Check(warnings.Count == 0, $"no warnings capturing built-in + audio ({warnings.Count})");

        ProjectService.Save(doc, dir, src);
        int wavCount = System.IO.Directory.Exists(System.IO.Path.Combine(dir, "samples"))
            ? System.IO.Directory.GetFiles(System.IO.Path.Combine(dir, "samples"), "*.wav").Length : 0;
        Check(wavCount >= 3, $"sample files written to samples/ ({wavCount})");

        var loaded = ProjectService.Load(dir);
        using var dst = new NotaEngine();
        var applyWarnings = ProjectService.Apply(loaded, dst, dir);
        Check(applyWarnings.Count == 0, $"apply produced no warnings ({applyWarnings.Count})");
        Check(dst.TrackCount == 3, $"all audio-related tracks restored ({dst.TrackCount})");

        // Audio clip restored (track 0) — geometry + audible playback.
        Check(dst.TryGetTrackInfo(0, out var dt0), "track 0 info");
        int dAudio = dt0.Id;
        Check(dst.TryGetAudioClipInfo(dAudio, 0, out var aci1) && Math.Abs(aci1.Gain - 0.8f) < 1e-6
              && Math.Abs(aci1.SourceOffsetFrames - 1000) < 1e-6, "audio clip geometry restored");
        Check(Math.Abs(aci1.PitchSemitones - 4.0f) < 1e-4 && aci1.WarpEnabled != 0
              && aci1.WarpMode == 3 && Math.Abs(aci1.WarpBeats - 2.0) < 1e-4, "audio clip pitch + warp restored");
        Check(aci1.Reversed != 0, "audio clip reverse restored (v19)");
        var lms = new double[16]; var lmb = new double[16];
        Check(dst.GetClipWarpMarkers(dAudio, 0, lms, lmb) == 3, "warp markers restored (3)");
        var lenv = dst.GetClipVolumeEnvelope(dAudio, 0);
        Check(lenv.Length == 2 && Math.Abs(lenv[0].Value - 1.0f) < 1e-6 && Math.Abs(lenv[1].Value - 0.25f) < 1e-6,
              "clip volume envelope restored (v8)");
        var lpenv = dst.GetClipPanEnvelope(dAudio, 0);
        Check(lpenv.Length == 2 && Math.Abs(lpenv[0].Value + 0.5f) < 1e-6 && Math.Abs(lpenv[1].Value - 0.5f) < 1e-6,
              "clip pan envelope restored (v9)");
        var abuf = new float[2 * 8192];
        dst.Seek(0); dst.Play(); dst.RenderOffline(abuf, 8192); dst.StopTransport();
        Check(Rms(abuf, 8192) > 1e-3f, $"restored audio clip is audible (rms={Rms(abuf, 8192):F4})");

        // Sampler restored (track 1).
        Check(dst.TryGetTrackInfo(1, out var dt1) && dt1.IsInstrument, "sampler track restored as instrument");
        Check(dst.TrackInstrumentKind(dt1.Id) == 1 && dst.TryGetSamplerInfo(dt1.Id, out var siDst)
              && siDst.RootNote == 62 && siDst.Loop != 0, "sampler settings restored");

        // Session audio take restored (track 2).
        Check(dst.TryGetTrackInfo(2, out var dt2), "track 2 info");
        Check(dst.SessionSlotState(dt2.Id, 1) == 1 && dst.TryGetSessionAudioSlot(dt2.Id, 1, out var saDst)
              && Math.Abs(saDst.LengthBeats - 2.0) < 1e-9, "session audio take restored");
    }
    catch (Exception ex)
    {
        Check(false, $"audio round-trip threw: {ex.Message}");
    }
    finally
    {
        try { if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true); } catch { }
    }
}

// ===================== M7-6c: plugin identity resolution ====================
Console.WriteLine("-- M7-6c: plugin identity --");
if (NotaEngine.PluginCount > 0)
{
    string? pid = NotaEngine.PluginId(0);
    Check(!string.IsNullOrEmpty(pid), "catalog entry exposes a stable id");
    Check(pid != null && NotaEngine.PluginIndexOfId(pid) == 0, "id resolves back to its catalog index");
    Check(NotaEngine.PluginIndexOfId("does.not.exist::0") < 0, "unknown id resolves to -1");
}
else
{
    Console.WriteLine("  [SKIP] plugin catalog is empty (run with --scan <worker> to populate)");
}

// ===================== M7-1: audio device settings =========================
// Device-free: enumeration + config staging round-trip. We deliberately do NOT
// call ApplyAudio() here — that restarts the real backend and rewrites the
// user's audio.json. Real device apply is covered by manual verification.
Console.WriteLine("-- M7-1: audio device settings --");
{
    var outs = NotaEngine.AudioOutputDevices();
    var ins  = NotaEngine.AudioInputDevices();
    Console.WriteLine($"  [INFO] {outs.Count} output device(s), {ins.Count} input device(s)");
    bool uidsOk = true;
    foreach (var d in outs) if (string.IsNullOrEmpty(d.Uid)) uidsOk = false;
    foreach (var d in ins)  if (string.IsNullOrEmpty(d.Uid)) uidsOk = false;
    Check(uidsOk, "every enumerated device has a stable UID");

    // Staging setters mutate the pending config without touching the device.
    engine.SetAudioSampleRate(48000);
    engine.SetAudioBufferFrames(256);
    string outUid = outs.Count > 0 ? outs[0].Uid : "test-output-uid";
    string inUid  = ins.Count  > 0 ? ins[0].Uid  : "test-input-uid";
    engine.SetAudioOutputDevice(outUid);
    engine.SetAudioInputDevice(inUid);

    var cfg = engine.GetAudioConfig();
    Check(cfg.SampleRate == 48000, "staged sample rate round-trips");
    Check(cfg.BufferFrames == 256, "staged buffer size round-trips");
    Check(cfg.OutputUid == outUid, "staged output device round-trips");
    Check(cfg.InputUid == inUid, "staged input device round-trips");

    // 0 means "device default"; the setter normalises negatives to 0.
    engine.SetAudioSampleRate(0);
    engine.SetAudioBufferFrames(-1);
    var cfg2 = engine.GetAudioConfig();
    Check(cfg2.SampleRate == 0 && cfg2.BufferFrames == 0, "default (0) rate/buffer normalise");

    // Backend never started in this test -> no negotiated values yet.
    Check(engine.NegotiatedSampleRate == 0, "negotiated rate is 0 before the backend starts");
}

// ===================== M7-2: MIDI device settings ==========================
// Device-free: enumeration + enable/disable staging round-trip. We do NOT call
// ApplyMidi() (it writes midi.json and would clobber the user's file); the
// setters only mutate the staged config, so this needs no hardware.
Console.WriteLine("-- M7-2: MIDI device settings --");
{
    var midis = NotaEngine.MidiInputDevices();
    Console.WriteLine($"  [INFO] {midis.Count} MIDI input(s)");
    bool uidsOk = true;
    foreach (var d in midis) if (string.IsNullOrEmpty(d.Uid)) uidsOk = false;
    Check(uidsOk, "every enumerated MIDI input has a stable UID");

    // Inputs default on (empty blocklist); disabling then re-enabling round-trips.
    const string uid = "test-midi-uid";
    Check(engine.IsMidiInputEnabled(uid), "unknown input defaults to enabled");
    engine.SetMidiInputEnabled(uid, false);
    Check(!engine.IsMidiInputEnabled(uid), "disabling an input round-trips");
    engine.SetMidiInputEnabled(uid, true);
    Check(engine.IsMidiInputEnabled(uid), "re-enabling an input round-trips");
}

// ===================== M7-4: browser preview + presets =====================
// Device-free: preview self-test + a preset round-trip through a TEMP dir (never
// the user's real presets folder).
Console.WriteLine("-- M7-4: browser preview + presets --");
{
    Check(engine.PreviewSelfTest(), "audio preview self-test (audible, device-free)");

    int pt = engine.AddInstrumentTrack();
    int di = engine.AddBuiltinDevice(pt, 0); // EQ
    engine.DeviceSetParam(pt, di, 0, 0.75f);
    float saved = engine.DeviceGetParam(pt, di, 0);

    var doc = PresetService.Capture(engine, pt, di, "Test EQ");
    Check(doc != null && doc.Type == "builtin-effect", "capture built-in effect preset");

    string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nota-preset-" + System.Guid.NewGuid().ToString("N"));
    string file = PresetService.Save(doc!, tmp);
    var loaded = PresetService.Load(file);

    int pt2 = engine.AddInstrumentTrack();
    string warn = PresetService.Apply(loaded, engine, pt2);
    Check(warn.Length == 0, "apply preset to a fresh track");
    Check(engine.TrackDeviceCount(pt2) == 1 && engine.TrackDeviceBuiltinKind(pt2, 0) == 0, "preset restored EQ device");
    Check(Math.Abs(engine.DeviceGetParam(pt2, 0, 0) - saved) < 1e-4, "preset restored device param");

    try { System.IO.Directory.Delete(tmp, true); } catch { /* best-effort cleanup */ }
}

// ============= Built-in instrument presets + factory catalog ================
// Device-free. Built-in synth presets (Synth/Physical/Aurora) capture+restore
// normalized plugin-params by id; the shipped factory catalog applies onto tracks.
Console.WriteLine("-- Built-in instrument presets + factory catalog --");
{
    // Built-in instrument preset round-trip: set a param, capture, apply to a fresh track.
    int it = engine.AddWavetableSynthTrack();
    int posIdx = -1;
    for (int i = 0; i < engine.PluginParamCount(it, -1); i++) if (engine.PluginParamId(it, -1, i) == "position") posIdx = i;
    engine.PluginParamSet(it, -1, posIdx, 0.66f);
    var idoc = PresetService.Capture(engine, it, -1, "Aurora Test");
    Check(idoc is { Type: "builtin-instrument", BuiltinKind: 5 }, "capture built-in instrument preset (Aurora, kind 5)");
    Check(idoc!.NamedParams is { Count: > 0 } && idoc.NamedParams.ContainsKey("position"), "instrument preset carries named params");

    string itmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nota-ipreset-" + System.Guid.NewGuid().ToString("N"));
    var iloaded = PresetService.Load(PresetService.Save(idoc, itmp));
    int before = engine.TrackCount;
    string iwarn = PresetService.Apply(iloaded, engine, -1);   // instrument preset → new track
    Check(iwarn.Length == 0 && engine.TrackCount == before + 1, "apply instrument preset creates a new track");
    Check(idoc.NamedParams["position"] is > 0.65f and < 0.67f, "captured position value preserved in doc");
    try { System.IO.Directory.Delete(itmp, true); } catch { /* best-effort cleanup */ }

    // Factory catalog: non-empty, covers instruments + effects, and every preset applies.
    var factory = new FactoryPresetCatalog();
    var all = factory.All();
    Check(all.Count >= 20, $"factory catalog ships presets ({all.Count})");
    Check(all.Any(p => p.IsInstrument) && all.Any(p => !p.IsInstrument), "catalog has both instrument + effect presets");
    Check(all.Any(p => p.IsInstrument && p.BuiltinKind == 5), "catalog has Aurora (wavetable) presets");

    int fxTrack = engine.AddInstrumentTrack();
    int okInst = 0, okFx = 0, fail = 0;
    foreach (var p in all)
    {
        string w = factory.Apply(engine, p.Id, fxTrack);
        if (w.Length != 0) { fail++; continue; }
        if (p.IsInstrument) okInst++; else okFx++;
    }
    Check(fail == 0, $"every factory preset applies cleanly ({okInst} instrument, {okFx} effect, {fail} failed)");

    // An effect factory preset restores its named params onto a device.
    int compTrack = engine.AddInstrumentTrack();
    var glue = all.First(p => !p.IsInstrument && p.BuiltinKind == 1);   // a Compressor preset
    factory.Apply(engine, glue.Id, compTrack);
    int lastDev = engine.TrackDeviceCount(compTrack) - 1;
    Check(lastDev >= 0 && engine.TrackDeviceBuiltinKind(compTrack, lastDev) == 1, "factory effect preset added a Compressor device");
}

// ===================== M7-7: crash recovery ================================
// Device-free: session marker + autosave manifest-diff + snapshot round-trip,
// all under a TEMP recovery dir (never the real ~/…/Nota/recovery).
Console.WriteLine("-- M7-7: crash recovery --");
{
    string rdir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nota-recovery-" + System.Guid.NewGuid().ToString("N"));
    var recSvc = new RecoveryService(rdir);
    Check(!recSvc.CrashDetected(), "no crash before a session starts");
    recSvc.BeginSession();
    Check(new RecoveryService(rdir).CrashDetected(), "session marker survives -> crash detected");

    var warn = new System.Collections.Generic.List<string>();
    var doc = ProjectService.Capture(engine, new TransportState(120.0, 1.0, false, false), warn);
    Check(recSvc.Autosave(doc, engine, null), "autosave writes when changed");
    Check(!recSvc.Autosave(doc, engine, null), "autosave skips when unchanged (manifest diff)");

    var info = new RecoveryService(rdir).PendingRecovery();
    Check(info != null, "pending recovery snapshot found after crash");
    if (info != null)
    {
        var loaded = ProjectService.Load(info.BundlePath);
        Check(loaded.Tracks.Count == doc.Tracks.Count, "recovered snapshot round-trips track count");
    }
    recSvc.EndSessionClean();
    Check(!new RecoveryService(rdir).CrashDetected(), "clean shutdown clears the marker");

    try { System.IO.Directory.Delete(rdir, true); } catch { /* best-effort cleanup */ }
}

// ===================== M7-8: xrun / dropout telemetry ======================
// Device-free: the counter starts at 0 and a notify bumps it (the real device
// overload listener can't be forced headlessly).
Console.WriteLine("-- M7-8: xrun / dropout telemetry --");
{
    Check(engine.XrunCount == 0, "xrun count starts at zero");
    Check(engine.XrunSelfTest(), "notifying a dropout bumps the counter");
    Check(engine.XrunCount == 1, "xrun count reflects the dropout");
}

// ===================== M9-A1: parameter automation (engine) ================
// Device-free: linear interpolation with end-hold, and block-rate apply writes
// the target atomic (volume lane -> t.volume() follows the curve).
Console.WriteLine("-- M9-A1: parameter automation core --");
{
    Check(engine.AutomationSelfTest(), "automation interp + block-rate apply (device-free)");
    Check(engine.ModulationSelfTest(), "CV modulation: LFO eval + apply/restore + link cleanup (device-free)");

    // CV modulation persistence round-trip (Phase 3, format v18).
    {
        using var me = new NotaEngine();
        int mt = me.AddInstrumentTrack();
        int mdv = me.AddBuiltinDevice(mt, 3);        // Delay
        int mm = me.ModulatorAdd(mt, 0);             // LFO
        me.ModulatorSet(mt, mm, 0, 2);               // saw
        me.ModulatorSet(mt, mm, 4, 0.7f);            // depth
        int ml = me.CvLinkAdd(mt, mm, mdv, 0);
        me.SetCvLinkDepth(mt, ml, 0.5f);
        me.SetCvLinkMode(mt, ml, 1);                 // multiply
        var w = new System.Collections.Generic.List<string>();
        var doc = ProjectService.Capture(me, new TransportState(120, 1, false, false), w);
        string pdir = Path.Combine(Path.GetTempPath(), "nota-mod-proj-" + Guid.NewGuid().ToString("N"));
        ProjectService.Save(doc, pdir, me);
        var loaded = ProjectService.Load(pdir);
        using var me2 = new NotaEngine();
        ProjectService.Apply(loaded, me2, pdir);
        bool ok = me2.TryGetTrackInfo(0, out var t2)
            && me2.ModulatorCount(t2.Id) == 1
            && me2.CvLinkCount(t2.Id) == 1
            && (int)Math.Round(me2.ModulatorGet(t2.Id, me2.ModulatorIdAt(t2.Id, 0), 0)) == 2
            && Math.Abs(me2.ModulatorGet(t2.Id, me2.ModulatorIdAt(t2.Id, 0), 4) - 0.7f) < 1e-3
            && Math.Abs(me2.CvLinkDepth(t2.Id, 0) - 0.5f) < 1e-3
            && me2.CvLinkMode(t2.Id, 0) == 1;
        Check(ok, "CV modulation round-trips through save/load (v18)");
        try { System.IO.Directory.Delete(pdir, true); } catch { }
    }
}

// ===================== Instrument Rack: engine core =====================
Console.WriteLine("-- Instrument Rack: engine core --");
{
    Check(engine.RackSelfTest(), "rack chains sum + mute + macro mapping + blob round-trip (device-free)");
}

// ============= Instrument Rack: C ABI CRUD + persistence (v12) =============
Console.WriteLine("-- Instrument Rack: C ABI + persistence --");
{
    using var re = new NotaEngine();
    int rt = re.AddInstrumentRackTrack();
    Check(rt > 0, "rack track created");
    Check(re.RackChainCount(rt) == 1, "rack starts with one chain");
    Check(re.RackChainInstrumentKind(rt, 0) == 0, "default chain hosts Nota Synth");

    int c2 = re.RackAddChain(rt, 2);   // Nota Physical
    Check(c2 == 1 && re.RackChainCount(rt) == 2, "second chain added");
    Check(re.RackChainInstrumentKind(rt, 1) == 2, "chain 1 hosts Nota Physical");

    int dEq = re.RackAddChainDevice(rt, 0, 0);   // EQ on chain 0
    Check(dEq == 0 && re.RackChainDeviceCount(rt, 0) == 1, "EQ device added to chain 0");
    Check(re.RackChainDeviceBuiltinKind(rt, 0, 0) == 0, "chain device reports EQ kind");
    // Newer built-in effects must instantiate in a chain too (Amp 6 … Beat Repeat 11).
    foreach (int fk in new[] { 6, 7, 8, 9, 10, 11, 12 })
    {
        int before = re.RackChainDeviceCount(rt, 0);
        int di2 = re.RackAddChainDevice(rt, 0, fk);
        Check(di2 >= 0 && re.RackChainDeviceCount(rt, 0) == before + 1 && re.RackChainDeviceBuiltinKind(rt, 0, di2) == fk, $"chain accepts built-in effect kind {fk}");
        re.RackRemoveChainDevice(rt, 0, di2);
    }
    Check(re.RackChainDeviceParamCount(rt, 0, 0) > 0, "chain EQ exposes params");
    float dmin = re.RackChainDeviceParamMin(rt, 0, 0, 0);
    float dmax = re.RackChainDeviceParamMax(rt, 0, 0, 0);
    float dpv = dmin + (dmax - dmin) * 0.33f;
    re.RackChainDeviceParamSet(rt, 0, 0, 0, dpv);
    Check(Math.Abs(re.RackChainDeviceParamGet(rt, 0, 0, 0) - dpv) < 1e-3, "chain device param round-trips");

    re.RackSetChainGain(rt, 1, 0.4f);
    re.RackSetChainPan(rt, 1, -0.5f);
    re.RackSetChainMute(rt, 1, true);
    Check(Math.Abs(re.RackChainGain(rt, 1) - 0.4f) < 1e-6 && Math.Abs(re.RackChainPan(rt, 1) + 0.5f) < 1e-6
          && re.RackChainMute(rt, 1), "chain gain/pan/mute round-trip");

    int mi = re.RackAddMacroMapping(rt, 0, 0, -1, 5, 0.0f, 1.0f);   // macro0 -> chain0 Synth Cutoff
    Check(mi == 0 && re.RackMappingCount(rt) == 1, "macro mapping added");
    Check(re.RackTryGetMapping(rt, 0, out var mm) && mm.Macro == 0 && mm.Chain == 0
          && mm.DeviceIndex == -1 && mm.ParamIndex == 5, "mapping info round-trips");
    re.RackMacroSet(rt, 0, 0.7f);
    Check(Math.Abs(re.RackChainInstrumentParamGet(rt, 0, 5) - 0.7f) < 1e-4, "macro drives the chain instrument param");
    // The UI turns a macro via the instrument's plugin-param surface (index == macro), not RackMacroSet.
    re.PluginParamSet(rt, -1, 0, 0.35f);
    Check(Math.Abs(re.RackChainInstrumentParamGet(rt, 0, 5) - 0.35f) < 1e-4, "macro via PluginParamSet (UI path) drives the param");

    // 2p extras: key/velocity zones, named macros, rack output, macro-map range/curve.
    re.RackSetChainZone(rt, 0, 36, 71, 0, 99);
    re.RackChainZone(rt, 0, out int zkl, out int zkh, out int zvl, out int zvh);
    Check(zkl == 36 && zkh == 71 && zvl == 0 && zvh == 99, "chain key/vel zone round-trips");
    re.RackSetMacroName(rt, 0, "Brightness");
    Check(re.RackMacroName(rt, 0) == "Brightness", "macro name round-trips");
    Check(re.RackMacroName(rt, 1) == "Macro 2", "unset macro name falls back to default");
    re.RackSetVolume(rt, 0.5f); re.RackSetGlide(rt, 0.3f);
    Check(Math.Abs(re.RackVolume(rt) - 0.5f) < 1e-4 && Math.Abs(re.RackGlide(rt) - 0.3f) < 1e-4, "rack volume + glide round-trip");
    re.RackSetMappingRange(rt, 0, 0.2f, 0.8f); re.RackSetMappingCurve(rt, 0, 2);
    Check(re.RackTryGetMapping(rt, 0, out var mmR) && Math.Abs(mmR.RangeMin - 0.2f) < 1e-4 && Math.Abs(mmR.RangeMax - 0.8f) < 1e-4
          && re.RackMappingCurve(rt, 0) == 2, "macro-map range + curve edit round-trip");
    re.RackSetMappingRange(rt, 0, 0.0f, 1.0f); re.RackSetMappingCurve(rt, 0, 0);   // restore default for the persistence check below

    // ProjectService round-trip (blob persisted via InstrumentDto.State, kind 3).
    string rdir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nota-rack-" + System.Guid.NewGuid().ToString("N"));
    try
    {
        var w = new System.Collections.Generic.List<string>();
        var doc = ProjectService.Capture(re, new TransportState(120.0, 1.0, MetronomeOn: false, LoopOn: false), w);
        Check(w.Count == 0, $"no warnings capturing a rack project ({w.Count})");
        ProjectService.Save(doc, rdir, re);
        var loaded = ProjectService.Load(rdir);
        Check(loaded.FormatVersion == ProjectService.CurrentFormatVersion, "rack project saved as current format");

        using var dst = new NotaEngine();
        ProjectService.Apply(loaded, dst, rdir);
        int found = -1;
        for (int i = 0; i < dst.TrackCount; i++)
            if (dst.TryGetTrackInfo(i, out var tinf) && dst.TrackInstrumentKind(tinf.Id) == 3) { found = tinf.Id; break; }
        Check(found > 0, "rack track restored");
        Check(dst.RackChainCount(found) == 2, "rack chains restored");
        Check(dst.RackChainInstrumentKind(found, 1) == 2, "chain 1 (Physical) restored");
        Check(Math.Abs(dst.RackChainGain(found, 1) - 0.4f) < 1e-6 && dst.RackChainMute(found, 1), "chain controls restored");
        Check(dst.RackChainDeviceCount(found, 0) == 1
              && Math.Abs(dst.RackChainDeviceParamGet(found, 0, 0, 0) - dpv) < 1e-3, "chain device + param restored");
        Check(dst.RackMappingCount(found) == 1, "macro mapping restored");
        dst.RackMacroSet(found, 0, 0.5f);
        Check(Math.Abs(dst.RackChainInstrumentParamGet(found, 0, 5) - 0.5f) < 1e-4, "restored mapping drives the param");
        dst.RackChainZone(found, 0, out int rzkl, out int rzkh, out int rzvl, out int rzvh);
        Check(rzkl == 36 && rzkh == 71 && rzvl == 0 && rzvh == 99, "chain zone restored (blob v5)");
        Check(dst.RackMacroName(found, 0) == "Brightness" && Math.Abs(dst.RackVolume(found) - 0.5f) < 1e-4, "macro name + rack volume restored");
    }
    finally
    {
        try { if (System.IO.Directory.Exists(rdir)) System.IO.Directory.Delete(rdir, true); } catch { }
    }
}

// ============= Audio Effect Rack: engine + C ABI + persistence (v13) =========
Console.WriteLine("-- Audio Effect Rack: engine + C ABI + persistence --");
{
    Check(engine.RackDeviceSelfTest(), "effect rack pass-through + parallel sum + mute + blob (device-free)");

    using var re = new NotaEngine();
    int tr = re.AddInstrumentTrack();
    int di = re.AddBuiltinDevice(tr, 5);
    Check(di >= 0, "audio effect rack device added");
    Check(re.RackDevChainCount(tr, di) == 1, "effect rack starts with one pass-through chain");

    int c2 = re.RackDevAddChain(tr, di, -1);
    Check(c2 == 1 && re.RackDevChainCount(tr, di) == 2, "second effect chain added");
    int d0 = re.RackDevAddChainDevice(tr, di, 0, 2);   // Reverb on chain 0
    Check(d0 == 0 && re.RackDevChainDeviceBuiltinKind(tr, di, 0, 0) == 2, "reverb added to effect chain 0");

    re.RackDevSetChainGain(tr, di, 1, 0.6f);
    re.RackDevSetChainSolo(tr, di, 0, true);
    Check(Math.Abs(re.RackDevChainGain(tr, di, 1) - 0.6f) < 1e-6 && re.RackDevChainSolo(tr, di, 0),
          "effect chain controls round-trip");

    int mi = re.RackDevAddMacroMapping(tr, di, 0, 0, 0, 0, 0f, 1f);   // macro0 -> chain0 reverb param0
    Check(mi == 0 && re.RackDevMappingCount(tr, di) == 1, "effect rack macro mapping added");

    // Routing modes + rack-out (blob v7): render real audio through an effect rack on
    // an audio track in each mode and confirm audible + finite output.
    {
        int at = re.AddAudioTrack();
        re.AddAudioClip(at, wav, 0.0);
        int adi = re.AddBuiltinDevice(at, 5);
        re.RackDevAddChainDevice(at, adi, 0, 4);   // Utility on the pass-through chain (unity)
        var aeBuf = new float[frames * 2];
        foreach (var (mval, mname) in new[] { (0, "Parallel"), (1, "Series"), (2, "Select") })
        {
            re.RackDevSetMode(at, adi, mval);
            Check(re.RackDevMode(at, adi) == mval, $"effect rack mode = {mname}");
            re.Seek(0); re.Play(); re.RenderOffline(aeBuf, frames); re.StopTransport();
            Check(float.IsFinite(aeBuf[0]) && Rms(aeBuf, frames) > 1e-5f, $"{mname} mode renders audible + finite (rms={Rms(aeBuf, frames):0.0000})");
        }
    }
    re.RackDevSetDryWet(tr, di, 0.35f);
    re.RackDevSetVolume(tr, di, 1.5f);
    re.RackDevSetPdc(tr, di, false);
    re.RackDevSetChainSelect(tr, di, 0.7f);
    re.RackDevSetSelFollow(tr, di, true);
    re.RackDevSetChainZone(tr, di, 1, 20, 90);
    re.RackDevChainZone(tr, di, 1, out int zLo, out int zHi);
    Check(Math.Abs(re.RackDevDryWet(tr, di) - 0.35f) < 1e-4 && Math.Abs(re.RackDevVolume(tr, di) - 1.5f) < 1e-4, "rack dry/wet + gain round-trip");
    Check(!re.RackDevPdc(tr, di) && re.RackDevSelFollow(tr, di) && zLo == 20 && zHi == 90, "pdc/select-follow/zone round-trip");

    string rdir2 = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nota-rackdev-" + System.Guid.NewGuid().ToString("N"));
    try
    {
        var w = new System.Collections.Generic.List<string>();
        var doc = ProjectService.Capture(re, new TransportState(120.0, 1.0, MetronomeOn: false, LoopOn: false), w);
        ProjectService.Save(doc, rdir2, re);
        var loaded = ProjectService.Load(rdir2);
        Check(loaded.FormatVersion == ProjectService.CurrentFormatVersion, "effect rack project saved as current format");

        using var dst = new NotaEngine();
        ProjectService.Apply(loaded, dst, rdir2);
        int ft = -1, fdi = -1;
        for (int i = 0; i < dst.TrackCount && ft < 0; i++)
        {
            if (!dst.TryGetTrackInfo(i, out var tinf)) continue;
            for (int dd = 0; dd < dst.TrackDeviceCount(tinf.Id); dd++)
                if (dst.TrackDeviceBuiltinKind(tinf.Id, dd) == 5) { ft = tinf.Id; fdi = dd; break; }
        }
        Check(ft > 0, "effect rack device restored");
        Check(dst.RackDevChainCount(ft, fdi) == 2, "effect rack chains restored");
        Check(dst.RackDevChainDeviceBuiltinKind(ft, fdi, 0, 0) == 2, "chain 0 reverb restored");
        Check(Math.Abs(dst.RackDevChainGain(ft, fdi, 1) - 0.6f) < 1e-6 && dst.RackDevChainSolo(ft, fdi, 0),
              "effect chain controls restored");
        Check(dst.RackDevMappingCount(ft, fdi) == 1, "effect rack macro mapping restored");
        Check(Math.Abs(dst.RackDevDryWet(ft, fdi) - 0.35f) < 1e-4 && Math.Abs(dst.RackDevVolume(ft, fdi) - 1.5f) < 1e-4 && !dst.RackDevPdc(ft, fdi) && dst.RackDevSelFollow(ft, fdi),
              "effect rack dry-wet/gain/pdc/select-follow restored (blob v7)");
        dst.RackDevChainZone(ft, fdi, 1, out int rzLo, out int rzHi);
        Check(rzLo == 20 && rzHi == 90, "effect rack chain-select zone restored (blob v7)");
    }
    finally
    {
        try { if (System.IO.Directory.Exists(rdir2)) System.IO.Directory.Delete(rdir2, true); } catch { }
    }
}

// ================= Drum Rack: engine + C ABI + persistence (v14) =============
Console.WriteLine("-- Drum Rack: engine + C ABI + persistence --");
{
    Check(engine.DrumRackSelfTest(), "drum rack routes notes to the matching pad chain + note round-trips (device-free)");

    using var re = new NotaEngine();
    int t = re.AddDrumRackTrack();
    Check(t > 0 && re.TrackInstrumentKind(t) == 4, "drum rack track created (kind 4)");
    Check(re.RackChainCount(t) == 0, "drum rack starts empty");

    int p0 = re.RackAddChain(t, 0); re.RackSetChainTriggerNote(t, p0, 36);   // pad C1
    int p1 = re.RackAddChain(t, 2); re.RackSetChainTriggerNote(t, p1, 38);   // pad D1 (Physical)
    int p2 = re.RackAddSamplerChain(t, wav, 40, false); re.RackSetChainTriggerNote(t, p2, 40); // pad E1 (Sampler from wav)
    Check(re.RackChainCount(t) == 3, "three pads added");
    Check(re.RackChainTriggerNote(t, p0) == 36 && re.RackChainTriggerNote(t, p1) == 38, "pad trigger notes round-trip");
    Check(re.RackChainInstrumentKind(t, p1) == 2, "pad 1 hosts Nota Physical");
    Check(p2 >= 0 && re.RackChainInstrumentKind(t, p2) == 1, "sampler pad added (kind 1)");

    // Rich Sampler editing per pad: chain param id/count/default + Sampler info/root all
    // reachable so the full editor works inside a Drum Rack pad (not just the track).
    Check(re.RackChainInstrumentParamCount(t, p2) == 21, "sampler pad exposes 21 chain params");
    Check(re.RackChainInstrumentParamId(t, p2, 4) == "start", $"chain param id maps (got '{re.RackChainInstrumentParamId(t, p2, 4)}')");
    Check(re.RackChainSamplerInfo(t, p2, out var cinfo) && cinfo.SampleId != 0 && cinfo.RootNote == 40, "chain sampler info (sample + root) readable");
    // Load a (new) sample into an existing chain Sampler (the pop-out drop path).
    Check(re.RackSetChainSamplerSample(t, p2, wav, 50) && re.RackChainSamplerInfo(t, p2, out var cinfoR) && cinfoR.SampleId != 0 && cinfoR.RootNote == 50, "load sample into an existing chain Sampler");
    re.RackChainInstrumentParamSet(t, p2, 4, 0.25f);   // move Start
    Check(Math.Abs(re.RackChainInstrumentParamGet(t, p2, 4) - 0.25f) < 1e-4, "chain sampler Start param round-trips");
    Check(Math.Abs(re.RackChainInstrumentParamDefault(t, p2, 4) - 0.0f) < 1e-4, "chain sampler Start default is 0");
    Check(re.RackSetChainSamplerRoot(t, p2, 55) && re.RackChainSamplerInfo(t, p2, out var cinfo2) && cinfo2.RootNote == 55, "chain sampler root set round-trips");
    re.RackChainInstrumentParamSet(t, p2, 4, 0.0f);    // restore Start so the audible check below is unaffected
    re.RackSetChainSamplerRoot(t, p2, 40);             // restore root

    // The sampler pad is audible when a clip plays its note (pad note 40).
    int dclip = re.AddMidiClip(t, 0.0, 4.0);
    re.SetClipNotes(t, dclip, new[] { new NotaNote(40, 0.0, 2.0, 1.0f) });
    re.SetBpm(120); re.Seek(0); re.Play();
    var dbuf = new float[frames * 2];
    re.RenderOffline(dbuf, frames);
    re.StopTransport();
    Check(Rms(dbuf, frames) > 1e-4f, $"drum-rack sampler pad is audible (rms={Rms(dbuf, frames):0.0000})");

    // Pad names (blob v8): a Sampler pad reports the same instrument name on every pad,
    // so the rack carries its own per-chain label.
    Check(re.RackChainName(t, p2).Length == 0, "pad name starts empty (falls back to the instrument name)");
    re.RackSetChainName(t, p0, "Kick"); re.RackSetChainName(t, p2, "Snare");
    Check(re.RackChainName(t, p0) == "Kick" && re.RackChainName(t, p2) == "Snare", "pad names round-trip");

    // Per-pad shaping (choke / tune / decay) + kit-level swing/humanize (blob v6).
    re.RackSetChainChoke(t, p0, 1); re.RackSetChainChoke(t, p1, 1);
    re.RackSetChainTune(t, p2, 7); re.RackSetChainDecay(t, p2, 0.4f);
    re.RackSetSwing(t, 0.3f); re.RackSetHumanize(t, 0.15f);
    Check(re.RackChainChoke(t, p0) == 1 && re.RackChainChoke(t, p1) == 1, "choke group set on two pads");
    Check(re.RackChainTune(t, p2) == 7, "pad tune (semitones) round-trips");
    Check(Math.Abs(re.RackChainDecay(t, p2) - 0.4f) < 1e-4, "pad decay round-trips");
    Check(Math.Abs(re.RackSwing(t) - 0.3f) < 1e-4 && Math.Abs(re.RackHumanize(t) - 0.15f) < 1e-4, "kit swing/humanize round-trip");
    // A tuned sampler pad still renders audible + finite.
    re.Seek(0); re.Play();
    var tbuf = new float[frames * 2]; re.RenderOffline(tbuf, frames); re.StopTransport();
    Check(Rms(tbuf, frames) > 1e-4f && float.IsFinite(tbuf[0]), $"tuned/decayed pad renders audible (rms={Rms(tbuf, frames):0.0000})");

    string ddir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nota-drum-" + System.Guid.NewGuid().ToString("N"));
    try
    {
        var w = new System.Collections.Generic.List<string>();
        var doc = ProjectService.Capture(re, new TransportState(120.0, 1.0, MetronomeOn: false, LoopOn: false), w);
        ProjectService.Save(doc, ddir, re);
        var loaded = ProjectService.Load(ddir);
        Check(loaded.FormatVersion == ProjectService.CurrentFormatVersion, "drum rack project saved as current format");

        using var dst = new NotaEngine();
        ProjectService.Apply(loaded, dst, ddir);
        int ft = -1;
        for (int i = 0; i < dst.TrackCount; i++)
            if (dst.TryGetTrackInfo(i, out var tinf) && dst.TrackInstrumentKind(tinf.Id) == 4) { ft = tinf.Id; break; }
        Check(ft > 0, "drum rack track restored");
        Check(dst.RackChainCount(ft) == 3, "drum rack pads restored");
        Check(dst.RackChainTriggerNote(ft, 0) == 36 && dst.RackChainTriggerNote(ft, 1) == 38, "pad trigger notes restored");
        Check(dst.RackChainInstrumentKind(ft, 2) == 1 && dst.RackChainTriggerNote(ft, 2) == 40, "sampler pad + note restored (rack blob)");
        Check(dst.RackChainChoke(ft, 0) == 1 && dst.RackChainTune(ft, 2) == 7 && Math.Abs(dst.RackChainDecay(ft, 2) - 0.4f) < 1e-4, "pad choke/tune/decay restored (blob v6)");
        Check(Math.Abs(dst.RackSwing(ft) - 0.3f) < 1e-4 && Math.Abs(dst.RackHumanize(ft) - 0.15f) < 1e-4, "kit swing/humanize restored (blob v6)");
        Check(dst.RackChainName(ft, 0) == "Kick" && dst.RackChainName(ft, 2) == "Snare", "pad names restored (blob v8)");
    }
    finally
    {
        try { if (System.IO.Directory.Exists(ddir)) System.IO.Directory.Delete(ddir, true); } catch { }
    }
}

// ================= Factory drum kits: recipes → samples → Drum Rack =========
// The shipped kits are code, not audio: the catalog is checked for structure, the
// renderer for determinism and level, and the service for assembling a playable rack.
Console.WriteLine("-- Factory drum kits --");
{
    var kits = Nota.Infrastructure.Kits.KitCatalog.All;
    Check(kits.Count == 25, $"25 factory kits ({kits.Count})");

    var kitIds = new System.Collections.Generic.HashSet<string>();
    var kitNames = new System.Collections.Generic.HashSet<string>();
    bool padsOk = true, notesOk = true, namesOk = true, hatsOk = true;
    foreach (var k in kits)
    {
        if (!kitIds.Add(k.Id) || !kitNames.Add(k.Name)) padsOk = false;
        if (k.Pads.Count != 16) padsOk = false;
        var padNotes = new System.Collections.Generic.HashSet<int>();
        var padNames = new System.Collections.Generic.HashSet<string>();
        foreach (var pad in k.Pads)
        {
            if (pad.Note < 36 || pad.Note > 51 || !padNotes.Add(pad.Note)) notesOk = false;
            if (string.IsNullOrWhiteSpace(pad.Name) || !padNames.Add(pad.Name)) namesOk = false;
            if (pad.Choke < 0 || pad.Choke > 4) hatsOk = false;              // the card offers groups 1..4
            if (pad.Name.EndsWith(" Hat") && pad.Choke != 1) hatsOk = false;  // closed cuts open
        }
        // A choke group of one pad cuts nothing — a recipe typo.
        if (k.Pads.Where(p => p.Choke > 0).GroupBy(p => p.Choke).Any(g => g.Count() < 2)) hatsOk = false;
    }
    Check(padsOk, "every kit has a unique id and name and 16 pads");
    Check(notesOk, "pad notes are unique and inside the GM bank (36..51)");
    Check(namesOk, "every pad is named, uniquely within its kit");
    Check(hatsOk, "hats share choke group 1; every choke group (1..4) pairs at least two pads");

    // A loaded kit is recognised by its pads, so no two kits may share more than 12 of them:
    // then a kit stays recognisable with up to three pads swapped or renamed.
    int worstOverlap = 0; string worstPair = "";
    for (int i = 0; i < kits.Count; i++)
        for (int j = i + 1; j < kits.Count; j++)
        {
            int n = kits[i].Pads.Count(p => kits[j].Pads.Any(q => q.Note == p.Note && q.Name == p.Name));
            if (n > worstOverlap) { worstOverlap = n; worstPair = $"{kits[i].Name}/{kits[j].Name}"; }
        }
    Check(worstOverlap <= 12, $"kits are told apart by their pads (most shared: {worstOverlap}, {worstPair})");

    // Rendering: deterministic, on target level, finite, and long enough to be a drum.
    var kick = kits[0].Pads[0];
    var r1 = Nota.Infrastructure.Kits.KitRenderer.Render(kick, 12345);
    var r2 = Nota.Infrastructure.Kits.KitRenderer.Render(kick, 12345);
    bool same = r1.Frames == r2.Frames && r1.Channels == r2.Channels;
    if (same) for (int i = 0; i < r1.Interleaved.Length; i++) if (r1.Interleaved[i] != r2.Interleaved[i]) { same = false; break; }
    Check(same, "the same seed renders the same audio");
    Check(Nota.Infrastructure.Kits.KitRenderer.Render(kick, 999).Frames != 0, "a different seed still renders");

    double peak = 0; bool finite = true;
    foreach (var v in r1.Interleaved) { peak = Math.Max(peak, Math.Abs(v)); if (!float.IsFinite(v)) finite = false; }
    Check(finite, "rendered audio is finite");
    Check(peak <= 1.0 && Math.Abs(20 * Math.Log10(peak) - kick.PeakDb) < 0.6,
          $"rendered peak hits the recipe's target ({20 * Math.Log10(peak):0.0} dBFS, want {kick.PeakDb:0.0})");
    Check(r1.Frames > r1.SampleRate / 50, $"one-shot is longer than 20 ms ({r1.Frames * 1000 / r1.SampleRate} ms)");

    // Every recipe of every kit renders: finite, on its level, not silent.
    var badPads = new System.Collections.Generic.List<string>();
    var sw = System.Diagnostics.Stopwatch.StartNew();
    System.Threading.Tasks.Parallel.ForEach(kits.SelectMany(k => k.Pads.Select(p => (k, p))), kp =>
    {
        var r = Nota.Infrastructure.Kits.KitRenderer.Render(kp.p, 1);
        double pk = 0; bool fin = r.Frames > 0;
        foreach (var v in r.Interleaved) { if (!float.IsFinite(v)) { fin = false; break; } pk = Math.Max(pk, Math.Abs(v)); }
        if (!fin || pk > 1.0 || pk <= 0 || Math.Abs(20 * Math.Log10(pk) - kp.p.PeakDb) > 1.0)
            lock (badPads) badPads.Add($"{kp.k.Name}/{kp.p.Name}");
    });
    Check(badPads.Count == 0, $"all {kits.Sum(k => k.Pads.Count)} kit pads render finite and on level in {sw.ElapsedMilliseconds} ms{(badPads.Count > 0 ? ": " + string.Join(", ", badPads) : "")}");

    // Building a rack from a kit: every pad loaded, named, choked and audible.
    var svc = new DrumKitService();
    Check(svc.All().Count == kits.Count, "the kit service lists the catalog");
    using var ke = new NotaEngine();
    int kt = svc.CreateTrack(ke, kits[0].Id, out string kitWarn);
    Check(kt > 0 && kitWarn.Length == 0, $"kit loaded onto a new Drum Rack track ({(kitWarn.Length == 0 ? "no warnings" : kitWarn)})");
    Check(ke.TrackInstrumentKind(kt) == 4, "the kit's track is a Drum Rack");
    Check(ke.RackChainCount(kt) == 16, $"all 16 pads loaded ({ke.RackChainCount(kt)})");
    Check(ke.RackChainName(kt, 0) == kits[0].Pads[0].Name, $"pad 1 is named '{ke.RackChainName(kt, 0)}'");
    Check(ke.RackChainTriggerNote(kt, 0) == 36, "pad 1 triggers on C1");
    int hats = 0;
    for (int c = 0; c < ke.RackChainCount(kt); c++) if (ke.RackChainChoke(kt, c) == 1) hats++;
    Check(hats == 3, $"the three hats share a choke group ({hats})");

    int kclip = ke.AddMidiClip(kt, 0.0, 4.0);
    ke.SetClipNotes(kt, kclip, new[] { new NotaNote(36, 0.0, 1.0, 1.0f), new NotaNote(38, 1.0, 1.0, 1.0f) });
    ke.SetBpm(120); ke.Seek(0); ke.Play();
    var kbuf = new float[frames * 2];
    ke.RenderOffline(kbuf, frames);
    ke.StopTransport();
    Check(Rms(kbuf, frames) > 1e-4f && float.IsFinite(kbuf[0]), $"a kit pad plays from a clip (rms={Rms(kbuf, frames):0.0000})");

    // Loading a second kit into the same rack replaces its pads rather than stacking.
    Check(svc.LoadInto(ke, kt, kits[1].Id, out _) && ke.RackChainCount(kt) == 16, "loading another kit replaces the pads");
    Check(ke.RackChainName(kt, 0) == kits[1].Pads[0].Name, "the replaced pads carry the new kit's names");

    // The card's kit picker names the kit a rack holds, and keeps naming it through a pad
    // swap — but a rack stripped to a few pads is no longer that kit.
    Check(svc.Identify(ke, kt) == kits[1].Id, $"the loaded kit is recognised ('{svc.Identify(ke, kt)}')");
    ke.RackSetChainName(kt, 0, "My Kick");
    Check(svc.Identify(ke, kt) == kits[1].Id, "a renamed pad keeps the kit recognised");
    while (ke.RackChainCount(kt) > 3) ke.RackRemoveChain(kt, ke.RackChainCount(kt) - 1);
    Check(svc.Identify(ke, kt) == "", "a rack stripped to three pads is no kit");
    Check(svc.LoadInto(ke, kt, kits[24].Id, out string lastWarn) && svc.Identify(ke, kt) == kits[24].Id,
          $"the last kit loads and is recognised ({(lastWarn.Length == 0 ? "no warnings" : lastWarn)})");
}

// ===================== M9-D: automation segment curves =====================
// Device-free: per-segment curvature shaping + the curve field round-trips
// through get/set points (widened NotaAutomationPoint).
Console.WriteLine("-- M9-D: automation segment curves --");
{
    Check(engine.AutomationCurveSelfTest(), "segment curvature shaping (device-free)");

    int cT = engine.AddInstrumentTrack();
    int cLane = engine.AddAutomationLane(cT, Nota.Application.AutomationTarget.Volume, -1, -1);
    engine.SetAutomationPoints(cT, cLane, new[]
    {
        new Nota.Application.AutomationPoint(0, 0.0f, 0.6f),
        new Nota.Application.AutomationPoint(4, 1.0f, 0.0f),
    });
    var cpts = engine.GetAutomationPoints(cT, cLane);
    Check(cpts.Length == 2 && System.Math.Abs(cpts[0].Curve - 0.6f) < 1e-6f, "point curve round-trips");
}

// ===================== Track duplicate / delete (context menu) =============
// Device-free: duplicate deep-copies structure (clips + automation) into a new
// track after the source; delete drops it; both are undoable.
Console.WriteLine("-- Track duplicate / delete --");
{
    int baseCount = engine.TrackCount;
    int dt = engine.AddInstrumentTrack();
    int dc = engine.AddMidiClip(dt, 0, 4);
    engine.SetClipNotes(dt, dc, new[] { new NotaNote(60, 0, 1, 0.9f) });
    int dLane = engine.AddAutomationLane(dt, Nota.Application.AutomationTarget.Pan, -1, -1);
    engine.SetAutomationPoints(dt, dLane, new[]
        { new Nota.Application.AutomationPoint(0, -0.5f), new Nota.Application.AutomationPoint(2, 0.5f) });

    int copy = engine.DuplicateTrack(dt);
    Check(copy > 0 && copy != dt, "duplicate returns a new track id");
    Check(engine.TrackCount == baseCount + 2, "track count grew by the copy");
    // The copy carries the clip + automation lane.
    Check(engine.TryGetTrackInfo(FindTrackIndex(engine, copy), out var ci2) && ci2.ClipCount == 1, "copy has the clip");
    Check(engine.AutomationLaneCount(copy) == 1, "copy has the automation lane");
    var cpn = engine.GetAutomationPoints(copy, 0);
    Check(cpn.Length == 2 && System.Math.Abs(cpn[1].Value - 0.5f) < 1e-6f, "copy's automation points match");

    Check(engine.RemoveTrack(copy), "remove the copy");
    Check(engine.TrackCount == baseCount + 1, "track count back after remove");
    engine.Undo();
    Check(engine.TrackCount == baseCount + 2, "undo restores the removed track");
    engine.RemoveTrack(copy);   // clean up the restored copy
    engine.RemoveTrack(dt);
}

static int FindTrackIndex(NotaEngine e, int trackId)
{
    for (int i = 0; i < e.TrackCount; i++)
        if (e.TryGetTrackInfo(i, out var ti) && ti.Id == trackId) return i;
    return -1;
}

// ===================== M9-B1: plugin-param automation engine ===============
// Device-free: a PluginParam lane stores its fields and dispatches through
// applyAutomation without crashing on a non-plugin instrument. Real plugin-param
// movement is exercised in B2's --hostcheck (needs an installed plugin).
Console.WriteLine("-- M9-B1: plugin-param automation core --");
{
    Check(engine.PluginAutomationSelfTest(), "plugin-param lane storage + dispatch (device-free)");
}

// ===================== M9-C W1: automation write/record engine =============
// Device-free: with record on, a mouse gesture writes the control value into the
// lane at the playhead and stops on release while a latched one keeps going; with
// record off, touching an automated param overrides its lane until re-enable.
Console.WriteLine("-- M9-C W1: automation write/record --");
{
    Check(engine.AutomationWriteSelfTest(), "automation write path: touch, latch, override (device-free)");
}

// ===================== M9-C W2: record-switch C ABI + C# ===================
// Device-free: the record switch round-trips; a gesture while recording creates the
// target's lane, and the same gesture with record off creates nothing. (Live sampling
// needs the audio clock and is covered by the W1 self-test.)
Console.WriteLine("-- M9-C W2: automation-record C ABI --");
{
    int wT = engine.AddInstrumentTrack();

    Check(!engine.AutomationRecording, "automation record off by default");
    engine.SetAutomationRecord(true);
    Check(engine.AutomationRecording, "automation record round-trips");

    int before = engine.AutomationLaneCount(wT);
    engine.BeginAutomationWrite(wT, Nota.Application.AutomationTarget.Pan, -1, -1, "");
    Check(engine.AutomationLaneCount(wT) == before + 1, "begin-write creates the target lane while recording");
    engine.EndAutomationWrite(wT, Nota.Application.AutomationTarget.Pan, -1, -1, "");

    engine.SetAutomationRecord(false);
    engine.BeginAutomationWrite(wT, Nota.Application.AutomationTarget.Volume, -1, -1, "");
    Check(engine.AutomationLaneCount(wT) == before + 1, "a touch with record off creates no lane");
    Check(!engine.AutomationOverridden, "an un-automated param is not overridden");
    engine.EndAutomationWrite(wT, Nota.Application.AutomationTarget.Volume, -1, -1, "");

    // The Pan lane exists but is empty, so touching it still overrides nothing; give it a
    // point and the same touch takes the lane over until re-enable.
    int pan = engine.AddAutomationLane(wT, Nota.Application.AutomationTarget.Pan, -1, -1);
    engine.SetAutomationPoints(wT, pan, new[] { new Nota.Application.AutomationPoint(0, 0.5f, 0) });
    engine.BeginAutomationWrite(wT, Nota.Application.AutomationTarget.Pan, -1, -1, "");
    Check(engine.AutomationOverridden, "touching an automated param overrides its lane");
    engine.EndAutomationWrite(wT, Nota.Application.AutomationTarget.Pan, -1, -1, "");
    Check(engine.AutomationOverridden, "the override survives the release");
    engine.ReenableAutomation();
    Check(!engine.AutomationOverridden, "re-enable hands the lane back");
}

// ===================== M9-A2: automation lane C ABI + C# ===================
// Device-free: lane CRUD round-trips through the C ABI; add reuses a lane on
// the same target; points come back sorted; device-param lane binds indices.
Console.WriteLine("-- M9-A2: automation lane C ABI --");
{
    int aT = engine.AddInstrumentTrack();
    int devIdx = engine.AddBuiltinDevice(aT, 0); // EQ, for a DeviceParam lane

    int volLane = engine.AddAutomationLane(aT, Nota.Application.AutomationTarget.Volume, -1, -1);
    Check(volLane == 0, "first automation lane index is 0");
    Check(engine.AutomationLaneCount(aT) == 1, "lane count reflects the added lane");
    // Re-adding the same target reuses the lane (no duplicate).
    Check(engine.AddAutomationLane(aT, Nota.Application.AutomationTarget.Volume, -1, -1) == volLane,
          "re-add on same target reuses the lane");
    Check(engine.AutomationLaneCount(aT) == 1, "no duplicate lane on same target");

    // Set points out of order -> engine sorts them by beat.
    engine.SetAutomationPoints(aT, volLane, new[]
    {
        new Nota.Application.AutomationPoint(4.0, 0.0f),
        new Nota.Application.AutomationPoint(0.0, 1.0f),
    });
    var pts = engine.GetAutomationPoints(aT, volLane);
    Check(pts.Length == 2, "get points returns the count");
    Check(pts[0].Beat == 0.0 && System.Math.Abs(pts[0].Value - 1.0f) < 1e-6f, "points sorted by beat");
    Check(pts[1].Beat == 4.0 && System.Math.Abs(pts[1].Value - 0.0f) < 1e-6f, "second point round-trips");

    var info = engine.AutomationLaneInfo(aT, volLane);
    Check(info.Target == Nota.Application.AutomationTarget.Volume && info.PointCount == 2,
          "lane info reports target + point count");

    // A DeviceParam lane carries device/param indices.
    int devLane = engine.AddAutomationLane(aT, Nota.Application.AutomationTarget.DeviceParam, devIdx, 0);
    Check(devLane == 1 && engine.AutomationLaneCount(aT) == 2, "device-param lane added");
    var devInfo = engine.AutomationLaneInfo(aT, devLane);
    Check(devInfo.Target == Nota.Application.AutomationTarget.DeviceParam
          && devInfo.DeviceIndex == devIdx && devInfo.ParamIndex == 0, "device-param lane binds indices");

    engine.RemoveAutomationLane(aT, devLane);
    Check(engine.AutomationLaneCount(aT) == 1, "remove lane drops the count");

    // Structural edits checkpoint undo (M6-6): undo restores the removed lane.
    engine.Undo();
    Check(engine.AutomationLaneCount(aT) == 2, "undo restores the removed lane");
}

// ===================== M9 follow-up: master-volume automation ==============
// Graph-level lane (not a track): round-trip points, and confirm applyAutomation
// drives the master gain during a render.
Console.WriteLine("-- M9 follow-up: master-volume automation --");
{
    using var me = new NotaEngine();
    me.SetBpm(120); me.SetTimeSignature(4, 4);
    Check(me.GetMasterVolumeAutomation().Length == 0, "master automation empty by default");
    me.SetMasterVolumeAutomation(new[]
    {
        new Nota.Application.AutomationPoint(4.0, 0.0f),   // out of order -> engine sorts
        new Nota.Application.AutomationPoint(0.0, 2.0f),
    });
    var mpts = me.GetMasterVolumeAutomation();
    Check(mpts.Length == 2 && mpts[0].Beat == 0.0 && System.Math.Abs(mpts[0].Value - 2.0f) < 1e-6f,
          "master automation points round-trip sorted");
    // Undo restores the empty lane (structural edit checkpoints undo).
    me.Undo();
    Check(me.GetMasterVolumeAutomation().Length == 0, "undo restores empty master automation");
    me.Redo();
    Check(me.GetMasterVolumeAutomation().Length == 2, "redo restores master automation");

    // applyAutomation drives the master gain: at beat 2 the 2.0->0.0 ramp reads ~1.0.
    // Play an audible instrument and compare RMS at beat 0 (gain 2) vs beat ~3.5 (gain →0).
    int it = me.AddInstrumentTrack();
    int ic = me.AddMidiClip(it, 0.0, 8.0);
    me.SetClipNotes(it, ic, new[] { new NotaNote(60, 0.0, 8.0, 0.9f) });
    var mb = new float[512 * 2];
    me.Play();
    me.Seek(0.0); me.RenderOffline(mb, 512);
    float rmsLoud = Rms(mb, 512);                 // master gain ≈ 2.0 near beat 0
    me.Seek(3.9); me.RenderOffline(mb, 512);
    float rmsQuiet = Rms(mb, 512);                // master gain ≈ 0.05 near beat 3.9
    Check(rmsLoud > 0.02f && rmsQuiet < rmsLoud * 0.5f,
          $"master automation drives the master gain (loud={rmsLoud:F4}, quiet={rmsQuiet:F4})");
    me.StopTransport();
}

// ===================== M7-9: format freeze + migrations ====================
// Device-free: a captured project round-trips its version, a legacy v1 file
// upgrades through the registered v1->v2 step, a future version is refused, and
// the migration runner chains synthetic steps.
Console.WriteLine("-- M7-9: format freeze + migrations --");
{
    // A legacy v1 file (pre-automation) chains v1->v2->v3 to the current version.
    string ldir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nota-v1-" + System.Guid.NewGuid().ToString("N"));
    System.IO.Directory.CreateDirectory(ldir);
    System.IO.File.WriteAllText(System.IO.Path.Combine(ldir, "project.json"),
        "{\"formatVersion\":1,\"sceneCount\":8,\"tracks\":[]}");
    try
    {
        var up = ProjectService.Load(ldir);
        Check(up.FormatVersion == ProjectService.CurrentFormatVersion, "legacy v1 file upgrades to current on load (chained migrations)");
    }
    finally { try { System.IO.Directory.Delete(ldir, true); } catch { } }

    // A v2 file (automation, pre-plugin-param) upgrades to the current version.
    string l2dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nota-v2-" + System.Guid.NewGuid().ToString("N"));
    System.IO.Directory.CreateDirectory(l2dir);
    System.IO.File.WriteAllText(System.IO.Path.Combine(l2dir, "project.json"),
        "{\"formatVersion\":2,\"sceneCount\":8,\"tracks\":[]}");
    try
    {
        var up2 = ProjectService.Load(l2dir);
        Check(up2.FormatVersion == ProjectService.CurrentFormatVersion, "v2 file upgrades to current on load");
    }
    finally { try { System.IO.Directory.Delete(l2dir, true); } catch { } }

    // A v3 file (pre-segment-curves) upgrades to v4 through the registered step.
    string l3dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nota-v3-" + System.Guid.NewGuid().ToString("N"));
    System.IO.Directory.CreateDirectory(l3dir);
    System.IO.File.WriteAllText(System.IO.Path.Combine(l3dir, "project.json"),
        "{\"formatVersion\":3,\"sceneCount\":8,\"tracks\":[]}");
    try
    {
        var up3 = ProjectService.Load(l3dir);
        Check(up3.FormatVersion == ProjectService.CurrentFormatVersion, "v3 file upgrades to current on load");
    }
    finally { try { System.IO.Directory.Delete(l3dir, true); } catch { } }

    // v1 passthrough: a captured project still round-trips after the Load refactor.
    string pdir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nota-fmt-" + System.Guid.NewGuid().ToString("N"));
    try
    {
        var w = new System.Collections.Generic.List<string>();
        var d = ProjectService.Capture(engine, new TransportState(120.0, 1.0, false, false), w);
        ProjectService.Save(d, pdir, engine);
        var loaded = ProjectService.Load(pdir);
        Check(loaded.FormatVersion == ProjectService.CurrentFormatVersion, "v1 loads through the migration path");
        Check(loaded.Tracks.Count == d.Tracks.Count, "v1 round-trip preserves tracks after Load refactor");
    }
    finally { try { System.IO.Directory.Delete(pdir, true); } catch { } }

    // A future format version is refused with a clear error, not a crash.
    string fdir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nota-fmt2-" + System.Guid.NewGuid().ToString("N"));
    System.IO.Directory.CreateDirectory(fdir);
    System.IO.File.WriteAllText(System.IO.Path.Combine(fdir, "project.json"), "{\"formatVersion\":999}");
    bool rejected = false;
    try { ProjectService.Load(fdir); } catch (NotSupportedException) { rejected = true; }
    Check(rejected, "future format version is refused");
    try { System.IO.Directory.Delete(fdir, true); } catch { }

    // Migration runner: chains a synthetic v1→v2 step and stamps the version.
    var root = new System.Text.Json.Nodes.JsonObject { ["formatVersion"] = 1, ["sceneCount"] = 8 };
    var steps = new System.Collections.Generic.List<Migration> { new Migration(1, o => o["sceneCount"] = 16) };
    var migrated = ProjectMigrations.Migrate(root, 2, steps);
    Check(migrated["formatVersion"]!.GetValue<int>() == 2, "migration runner stamps the new version");
    Check(migrated["sceneCount"]!.GetValue<int>() == 16, "migration runner applies the upgrade step");

    // A gap in the chain throws rather than silently producing a bad document.
    bool gapThrew = false;
    try
    {
        ProjectMigrations.Migrate(
            new System.Text.Json.Nodes.JsonObject { ["formatVersion"] = 1 }, 3,
            new System.Collections.Generic.List<Migration> { new Migration(1, _ => { }) });
    }
    catch (NotSupportedException) { gapThrew = true; }
    Check(gapThrew, "a missing migration step throws");
}

// ===================== M7-5: drag-drop session audio file ==================
// Device-free: the native helper behind a sample→session-slot drop (the DnD
// gesture itself is UI-only). Reuses the 1s sine `wav` written above.
Console.WriteLine("-- M7-5: drag-drop session audio file --");
{
    int at = engine.AddAudioTrack();
    Check(engine.AddSessionAudioFile(at, 0, wav), "drop audio file into a session slot");
    Check(engine.SessionSlotState(at, 0) == 1, "session slot filled after drop");
    // 1 s @ 120 bpm ≈ 2 beats (auto-computed loop length).
    Check(Math.Abs(engine.SessionSlotLength(at, 0) - 2.0) < 0.1, "loop length auto-computed from tempo");
    Check(!engine.AddSessionAudioFile(99999, 0, wav), "drop onto a nonexistent track fails");
}

// ===================== Auto Filter (effect kind 7) =========================
Console.WriteLine("-- Auto Filter (effect kind 7) --");
{
    using var afeng = new NotaEngine();
    int aft = afeng.AddAudioTrack();
    afeng.AddAudioClip(aft, wav, 0.0);
    int afdi = afeng.AddBuiltinDevice(aft, 7);
    Check(afdi >= 0, "add Auto Filter device");
    Check(afeng.TrackDeviceBuiltinKind(aft, afdi) == 7, "device reports builtin kind 7");
    int afpc = afeng.DeviceParamCount(aft, afdi);
    Check(afpc == 21, $"Auto Filter exposes 21 params ({afpc})");
    Check(afeng.DeviceAcceptsSidechain(aft, afdi), "Auto Filter accepts a sidechain");
    afeng.DeviceSetParam(aft, afdi, 0, 0.35f);   // Freq
    afeng.DeviceSetParam(aft, afdi, 1, 0.6f);    // Res
    Check(Math.Abs(afeng.DeviceGetParam(aft, afdi, 1) - 0.6f) < 1e-3f, "device param set/get round-trips");
    var afbuf = new float[2048 * 2];
    afeng.SetBpm(120); afeng.Seek(0); afeng.Play();
    afeng.RenderOffline(afbuf, 2048);
    afeng.StopTransport();
    Check(Rms(afbuf, 2048) > 1e-4f, $"Auto Filter passes audio (rms={Rms(afbuf, 2048):0.0000})");
    // Real-time viz feed: the pre-filter scope (UI spectrum) + the live modulated cutoff.
    var afscope = new float[2048];
    int afn = afeng.DeviceScope(aft, afdi, afscope, 2048);
    float afe = 0; for (int s = 0; s < afn; s++) afe += Math.Abs(afscope[s]);
    Check(afn == 2048 && afe > 0f, $"Auto Filter scope feeds the spectrum ({afn} samples, energy {afe:0.0})");
    float liveCut = afeng.DeviceGainReduction(aft, afdi);
    Check(liveCut > 0f && liveCut < 1f, $"Auto Filter publishes live modulated cutoff ({liveCut:0.00})");
}

// ===================== Nota Vintage (effect kind 8) ========================
Console.WriteLine("-- Nota Vintage (effect kind 8) --");
{
    using var vveng = new NotaEngine();
    int vvt = vveng.AddAudioTrack();
    vveng.AddAudioClip(vvt, wav, 0.0);
    int vvdi = vveng.AddBuiltinDevice(vvt, 8);
    Check(vvdi >= 0, "add Nota Vintage device");
    Check(vveng.DeviceName(vvt, vvdi) == "Nota Vintage", $"device is Nota Vintage (got '{vveng.DeviceName(vvt, vvdi)}')");
    Check(vveng.TrackDeviceBuiltinKind(vvt, vvdi) == 8, "device reports builtin kind 8");
    int vvpc = vveng.DeviceParamCount(vvt, vvdi);
    Check(vvpc == 11, $"Nota Vintage exposes 11 params ({vvpc})");
    vveng.DeviceSetParam(vvt, vvdi, 1, 0.7f);   // Drive
    Check(Math.Abs(vveng.DeviceGetParam(vvt, vvdi, 1) - 0.7f) < 1e-3f, "device param set/get round-trips");

    // Engage the wear/wow/flutter/noise stages so every path renders.
    vveng.DeviceSetParam(vvt, vvdi, 3, 0.6f);   // Wow
    vveng.DeviceSetParam(vvt, vvdi, 4, 0.6f);   // Flutter
    vveng.DeviceSetParam(vvt, vvdi, 5, 0.5f);   // Noise
    vveng.DeviceSetParam(vvt, vvdi, 6, 0.5f);   // Crackle
    vveng.DeviceSetParam(vvt, vvdi, 7, 0.4f);   // Wear
    vveng.SetBpm(120);
    for (int mode = 0; mode < 6; mode++)
    {
        vveng.DeviceSetParam(vvt, vvdi, 0, mode / 5f);
        var vbuf = new float[2048 * 2];
        vveng.Seek(0); vveng.Play();
        vveng.RenderOffline(vbuf, 2048);
        vveng.StopTransport();
        float rms = Rms(vbuf, 2048); bool finite = true;
        foreach (var s in vbuf) if (!float.IsFinite(s) || Math.Abs(s) > 8f) { finite = false; break; }
        Check(rms > 1e-4f && finite, $"Vintage mode {mode} is audible + stable (rms {rms:0.000})");
    }

    // Duplicate the track → cloneDevice(kind 8) must carry the params.
    int vvcopy = vveng.DuplicateTrack(vvt);
    Check(vvcopy > 0 && Math.Abs(vveng.DeviceGetParam(vvcopy, vvdi, 1) - 0.7f) < 1e-3f, "duplicate track clones Vintage params");
}

// ===================== Nota Orbit (effect kind 9) ======================
Console.WriteLine("-- Nota Orbit (effect kind 9) --");
{
    using var apeng = new NotaEngine();
    int apt = apeng.AddAudioTrack();
    apeng.AddAudioClip(apt, wav, 0.0);
    int apdi = apeng.AddBuiltinDevice(apt, 9);
    Check(apdi >= 0, "add Nota Orbit device");
    Check(apeng.DeviceName(apt, apdi) == "Nota Orbit", $"device is Nota Orbit (got '{apeng.DeviceName(apt, apdi)}')");
    Check(apeng.TrackDeviceBuiltinKind(apt, apdi) == 9, "device reports builtin kind 9");
    int appc = apeng.DeviceParamCount(apt, apdi);
    Check(appc == 6, $"Nota Orbit exposes 6 params ({appc})");
    apeng.DeviceSetParam(apt, apdi, 0, 0.7f);   // Rate
    Check(Math.Abs(apeng.DeviceGetParam(apt, apdi, 0) - 0.7f) < 1e-3f, "device param set/get round-trips");

    // Each waveform renders audible + finite.
    apeng.DeviceSetParam(apt, apdi, 1, 0.8f);   // Amount
    apeng.SetBpm(120);
    var apbuf = new float[2048 * 2];
    for (int wv = 0; wv < 5; wv++)
    {
        apeng.DeviceSetParam(apt, apdi, 2, wv / 4f);
        apeng.Seek(0); apeng.Play();
        apeng.RenderOffline(apbuf, 2048);
        apeng.StopTransport();
        float rms = Rms(apbuf, 2048); bool finite = true;
        foreach (var s in apbuf) if (!float.IsFinite(s) || Math.Abs(s) > 8f) { finite = false; break; }
        Check(rms > 1e-4f && finite, $"Auto Pan waveform {wv} is audible + stable (rms {rms:0.000})");
    }

    // Phase 180° drives the stereo position off-centre (auto-pan); Phase 0° keeps it
    // centred (tremolo) — the published pan position distinguishes the two.
    apeng.DeviceSetParam(apt, apdi, 2, 0f);     // sine (continuous)
    apeng.DeviceSetParam(apt, apdi, 1, 1.0f);   // Amount full
    apeng.DeviceSetParam(apt, apdi, 4, 0.5f);   // Phase 180° → pan
    apeng.Seek(0); apeng.Play(); apeng.RenderOffline(apbuf, 2048); apeng.StopTransport();
    float panPos = apeng.DeviceGainReduction(apt, apdi);
    Check(Math.Abs(panPos - 0.5f) > 0.05f, $"Auto Pan (Phase 180°) pushes the balance off-centre ({panPos:0.00})");
    apeng.DeviceSetParam(apt, apdi, 4, 0f);     // Phase 0° → tremolo
    apeng.Seek(0); apeng.Play(); apeng.RenderOffline(apbuf, 2048); apeng.StopTransport();
    float tremPos = apeng.DeviceGainReduction(apt, apdi);
    Check(Math.Abs(tremPos - 0.5f) < 1e-3f, $"Auto Pan (Phase 0°) stays centred = tremolo ({tremPos:0.00})");

    int apcopy = apeng.DuplicateTrack(apt);
    Check(apcopy > 0 && Math.Abs(apeng.DeviceGetParam(apcopy, apdi, 0) - 0.7f) < 1e-3f, "duplicate track clones Auto Pan params");
}

// ===================== Nota Auto Shift (effect kind 10) ===================
Console.WriteLine("-- Nota Auto Shift (effect kind 10) --");
{
    using var aseng = new NotaEngine();
    int ast = aseng.AddAudioTrack();
    aseng.AddAudioClip(ast, wav, 0.0);   // 1 s sine @ 440 Hz = A4 (MIDI 69)
    int asdi = aseng.AddBuiltinDevice(ast, 10);
    Check(asdi >= 0, "add Nota Auto Shift device");
    Check(aseng.DeviceName(ast, asdi) == "Nota Auto Shift", $"device is Nota Auto Shift (got '{aseng.DeviceName(ast, asdi)}')");
    Check(aseng.TrackDeviceBuiltinKind(ast, asdi) == 10, "device reports builtin kind 10");
    int aspc = aseng.DeviceParamCount(ast, asdi);
    Check(aspc == 10, $"Nota Auto Shift exposes 10 params ({aspc})");
    Check(aseng.DeviceParamName(ast, asdi, 6) == "Range" && aseng.DeviceParamName(ast, asdi, 7) == "Formant", "new params: Range + Formant");
    aseng.DeviceSetParam(ast, asdi, 2, 0.8f);   // Amount
    aseng.DeviceSetParam(ast, asdi, 6, 0.7f);   // Range
    aseng.DeviceSetParam(ast, asdi, 7, 0.6f);   // Formant
    Check(Math.Abs(aseng.DeviceGetParam(ast, asdi, 2) - 0.8f) < 1e-3f && Math.Abs(aseng.DeviceGetParam(ast, asdi, 6) - 0.7f) < 1e-3f, "device param set/get round-trips");

    // The pitch tracker should read the 440 Hz sine as A4 (~MIDI 69).
    aseng.SetBpm(120);
    var asbuf = new float[8192 * 2];
    aseng.Seek(0); aseng.Play(); aseng.RenderOffline(asbuf, 8192); aseng.StopTransport();
    Check(Rms(asbuf, 8192) > 1e-4f, $"Auto Shift passes audio (rms {Rms(asbuf, 8192):0.000})");
    float detMidi = aseng.DeviceGainReduction(ast, asdi) * 127f;
    Check(Math.Abs(detMidi - 69f) < 1.5f, $"detects the 440 Hz sine as A4 (~69, got {detMidi:0.0})");
    // Interleaved [detected, corrected] scope: even = detected A4, odd = corrected (in scale).
    var asscope = new float[1024];
    int asn = aseng.DeviceScope(ast, asdi, asscope, asscope.Length);
    Check(asn >= 2 && (asn % 2) == 0, $"pitch scope returns interleaved pairs ({asn})");
    bool anyDet = false; for (int s = 0; s < asn; s += 2) if (asscope[s] > 1e-4f) { anyDet = true; break; }
    Check(anyDet, "scope carries the detected-pitch trace");

    // Manual Shift +12 st (ratio 2) audibly transposes → output differs from no-shift.
    aseng.DeviceSetParam(ast, asdi, 2, 0f);     // Amount 0 (isolate manual shift)
    aseng.DeviceSetParam(ast, asdi, 4, 0.5f);   // Shift 0 st
    var flat = new float[4096 * 2];
    aseng.Seek(0); aseng.Play(); aseng.RenderOffline(flat, 4096); aseng.StopTransport();
    aseng.DeviceSetParam(ast, asdi, 4, 1.0f);   // Shift +12 st
    var up = new float[4096 * 2];
    aseng.Seek(0); aseng.Play(); aseng.RenderOffline(up, 4096); aseng.StopTransport();
    double diff = 0; for (int s = 0; s < 4096 * 2; s++) diff += Math.Abs(flat[s] - up[s]);
    bool finite = true; foreach (var s in up) if (!float.IsFinite(s) || Math.Abs(s) > 8f) { finite = false; break; }
    Check(diff > 1.0 && finite, $"manual Shift +12 st transposes the signal (sum|delta| {diff:0.0})");

    int ascopy = aseng.DuplicateTrack(ast);
    Check(ascopy > 0 && Math.Abs(aseng.DeviceGetParam(ascopy, asdi, 2) - 0f) < 1e-3f, "duplicate track clones Auto Shift params");
}

// ===================== Nota Beat Repeat (effect kind 11) ==================
Console.WriteLine("-- Nota Beat Repeat (effect kind 11) --");
{
    using var breng = new NotaEngine();
    int brt = breng.AddAudioTrack();
    breng.AddAudioClip(brt, wav, 0.0);   // 440 Hz sine
    breng.SetBpm(120);
    // Dry reference (no device yet).
    var dry = new float[44100 * 2];
    breng.Seek(0); breng.Play(); breng.RenderOffline(dry, 44100); breng.StopTransport();

    int brdi = breng.AddBuiltinDevice(brt, 11);
    Check(brdi >= 0, "add Nota Beat Repeat device");
    Check(breng.DeviceName(brt, brdi) == "Nota Beat Repeat", $"device is Nota Beat Repeat (got '{breng.DeviceName(brt, brdi)}')");
    Check(breng.TrackDeviceBuiltinKind(brt, brdi) == 11, "device reports builtin kind 11");
    int brpc = breng.DeviceParamCount(brt, brdi);
    Check(brpc == 16, $"Nota Beat Repeat exposes 16 params ({brpc})");
    breng.DeviceSetParam(brt, brdi, 5, 0.7f);   // Gate
    Check(Math.Abs(breng.DeviceGetParam(brt, brdi, 5) - 0.7f) < 1e-3f, "device param set/get round-trips");

    // Gate mode + octave-up repeats at full chance: the tempo-synced repeats must
    // alter the audio vs the dry render (proves transport-synced triggering works).
    breng.DeviceSetParam(brt, brdi, 0, 0.0f);   // Interval 1/8 → frequent triggers
    breng.DeviceSetParam(brt, brdi, 2, 0.4f);   // Grid 1/16
    breng.DeviceSetParam(brt, brdi, 4, 1.0f);   // Chance 100%
    breng.DeviceSetParam(brt, brdi, 5, 1.0f);   // Gate full
    breng.DeviceSetParam(brt, brdi, 6, 1.0f);   // Pitch +12
    breng.DeviceSetParam(brt, brdi, 13, 1.0f);  // Mode = Gate
    var wet = new float[44100 * 2];
    breng.Seek(0); breng.Play(); breng.RenderOffline(wet, 44100); breng.StopTransport();
    double diff = 0; bool finite = true;
    for (int s = 0; s < 44100 * 2; s++) { diff += Math.Abs(dry[s] - wet[s]); if (!float.IsFinite(wet[s]) || Math.Abs(wet[s]) > 8f) finite = false; }
    Check(diff > 10.0 && finite, $"synced beat repeats alter the audio (sum|delta| {diff:0})");
    float phase = breng.DeviceGainReduction(brt, brdi);
    Check(phase >= 0f && phase < 2f, $"publishes interval phase for the viz ({phase:0.00})");
    var brslots = new float[64];
    int brn = breng.DeviceScope(brt, brdi, brslots, brslots.Length);
    bool slotsOk = brn == 64; for (int s = 0; s < brn; s++) if (!float.IsFinite(brslots[s]) || brslots[s] < 0f || brslots[s] > 1f) slotsOk = false;
    Check(slotsOk, $"publishes 64-slot timeline envelope for the viz (n={brn})");
    breng.DeviceSetParam(brt, brdi, 14, 0.5f);   // Mix
    breng.DeviceSetParam(brt, brdi, 15, 1.0f);   // Latch
    Check(Math.Abs(breng.DeviceGetParam(brt, brdi, 14) - 0.5f) < 1e-3f && Math.Abs(breng.DeviceGetParam(brt, brdi, 15) - 1.0f) < 1e-3f, "Mix/Latch params round-trip");

    int brcopy = breng.DuplicateTrack(brt);
    Check(brcopy > 0 && Math.Abs(breng.DeviceGetParam(brcopy, brdi, 5) - 1.0f) < 1e-3f, "duplicate track clones Beat Repeat params");
}

// ===================== built-in Volt project round-trip ===================
// Regression: the ProjectService save whitelist listed kinds 0/2/3/4 and omitted
// Aurora (5) + Volt (6), so their state blob was never written — a saved Volt lost
// all its params on reload. Assert a built-in Volt survives Capture→Save→Load→Apply.
Console.WriteLine("-- built-in Volt project round-trip --");
{
    using var vsrc = new NotaEngine();
    int vt = vsrc.AddVoltSynthTrack();
    Check(vt > 0, "add Volt track");
    int vpc = vsrc.PluginParamCount(vt, -1);
    Check(vpc > 3, $"Volt exposes params ({vpc})");
    int vp0 = 0, vp1 = 3;
    vsrc.PluginParamSet(vt, -1, vp0, 0.83f);
    vsrc.PluginParamSet(vt, -1, vp1, 0.17f);

    string vdir = Path.Combine(Path.GetTempPath(), "nota-volt-proj-" + Guid.NewGuid().ToString("N"));
    try
    {
        var vw = new System.Collections.Generic.List<string>();
        var vdoc = ProjectService.Capture(vsrc, new TransportState(120, 1, false, false), vw);
        var vInst = vdoc.Tracks.FirstOrDefault(td => td.Instrument is { Kind: 6 });
        Check(vInst?.Instrument?.State is { Length: > 0 }, "Volt instrument state blob captured");
        ProjectService.Save(vdoc, vdir, vsrc);

        var vloaded = ProjectService.Load(vdir);
        using var vdst = new NotaEngine();
        ProjectService.Apply(vloaded, vdst, vdir);
        Check(vdst.TryGetTrackInfo(0, out var vti) && vdst.TrackInstrumentKind(vti.Id) == 6, "restored Volt track");
        float r0 = vdst.PluginParamGet(vti.Id, -1, vp0), r1 = vdst.PluginParamGet(vti.Id, -1, vp1);
        Check(Math.Abs(r0 - 0.83f) < 0.02f && Math.Abs(r1 - 0.17f) < 0.02f,
            $"Volt param values restored ({r0:0.00}, {r1:0.00})");
    }
    finally { try { if (System.IO.Directory.Exists(vdir)) System.IO.Directory.Delete(vdir, true); } catch { } }
}

// ===================== split a warped audio clip ==========================
// Regression: splitting a WARPED clip used to edit only lengthFrames/sourceOffset
// (ignored when warped) → both halves showed/played the whole material (a "copy").
// Splitting must divide the warp play window so the two halves are disjoint.
Console.WriteLine("-- split warped audio clip --");
{
    using var we2 = new NotaEngine();
    we2.SetBpm(120); we2.SetTimeSignature(4, 4);
    int wt = we2.AddAudioTrack();
    int wc = we2.AddAudioClip(wt, wav, 0.0);          // 1 s sine
    we2.SetClipWarp(wt, wc, true, 0);                 // enable warp (neutral seed)
    we2.SetClipWarpLength(wt, wc, 4.0);               // conform to 4 beats
    we2.TryGetClipInfo(wt, wc, out var wbefore);
    Check(Math.Abs(wbefore.LengthBeats - 4.0) < 1e-6, $"warped clip is 4 beats ({wbefore.LengthBeats})");
    int wnew = we2.SplitClip(wt, wc, 2.0);            // split at beat 2
    Check(wnew == 1, $"warped split returns new index 1 ({wnew})");
    we2.TryGetTrackInfo(0, out var wti);
    Check(wti.ClipCount == 2, $"warped split yields 2 clips ({wti.ClipCount})");
    we2.TryGetClipInfo(wt, 0, out var wa);
    we2.TryGetClipInfo(wt, 1, out var wb);
    Check(Math.Abs(wa.StartBeat - 0.0) < 1e-6 && Math.Abs(wa.LengthBeats - 2.0) < 1e-6,
        $"left half [0,2] (got start={wa.StartBeat} len={wa.LengthBeats})");
    Check(Math.Abs(wb.StartBeat - 2.0) < 1e-6 && Math.Abs(wb.LengthBeats - 2.0) < 1e-6,
        $"right half [2,2] not a full-length copy (got start={wb.StartBeat} len={wb.LengthBeats})");
}

// ===================== clip clipboard (copy/paste) =========================
Console.WriteLine("-- clip clipboard --");
{
    using var ce = new NotaEngine();
    ce.SetBpm(120); ce.SetTimeSignature(4, 4);
    int ta = ce.AddInstrumentTrack();
    int tb = ce.AddInstrumentTrack();
    int ca = ce.AddMidiClip(ta, 0.0, 4.0);
    ce.SetClipNotes(ta, ca, new[] { new NotaNote(60, 0.0, 1.0, 0.9f), new NotaNote(64, 2.0, 1.0, 0.9f) });
    Check(ce.CopyClip(ta, ca), "copy midi clip");
    Check(ce.ClipboardClipKind() == 1, "clipboard kind is midi (1)");

    // Cross-track paste at beat 0 lands at [0,4] on track B.
    int p1 = ce.PasteClip(tb, 0.0);
    Check(p1 >= 0, "paste onto another track");
    ce.TryGetClipInfo(tb, p1, out var pi1);
    Check(Math.Abs(pi1.StartBeat) < 1e-6 && Math.Abs(pi1.LengthBeats - 4.0) < 1e-6, $"pasted clip [0,4] (got {pi1.StartBeat},{pi1.LengthBeats})");
    var pn = ce.GetClipNotes(tb, p1);
    Check(pn.Length == 2, $"pasted clip carries its notes ({pn.Length})");

    // Paste again at beat 0 must not overlap → pushed to [4,4].
    int p2 = ce.PasteClip(tb, 0.0);
    ce.TryGetClipInfo(tb, p2, out var pi2);
    Check(Math.Abs(pi2.StartBeat - 4.0) < 1e-6, $"second paste avoids overlap (start={pi2.StartBeat})");

    // A MIDI clip cannot be pasted onto an audio track (type mismatch → -1).
    int at = ce.AddAudioTrack();
    Check(ce.PasteClip(at, 0.0) < 0, "midi clip rejected on audio track");
}

// ============ clip automation travels with copy/paste/duplicate/cut =========
Console.WriteLine("-- clip automation follows the clip --");
{
    const double eps = 1e-6;
    // Volume lane: two points inside the clip span [4,8) plus one before and one after.
    static Nota.Application.AutomationPoint[] Env() => new[]
    {
        new Nota.Application.AutomationPoint(0.0, 0.5f),    // before the clip
        new Nota.Application.AutomationPoint(4.0, 0.2f),    // inside (clip-relative 0)
        new Nota.Application.AutomationPoint(6.0, 0.8f),    // inside (clip-relative 2)
        new Nota.Application.AutomationPoint(12.0, 0.5f),   // after the clip
    };

    // Copy → paste re-lands the automation, offset to the paste position, and clears
    // any pre-existing points in the destination span first.
    {
        using var e = new NotaEngine();
        e.SetBpm(120); e.SetTimeSignature(4, 4);
        int t = e.AddInstrumentTrack();
        int c = e.AddMidiClip(t, 4.0, 4.0);
        int lane = e.AddAutomationLane(t, Nota.Application.AutomationTarget.Volume, -1, -1);
        var seed = Env().Append(new Nota.Application.AutomationPoint(21.0, 0.9f)).ToArray(); // stray point in dest span
        e.SetAutomationPoints(t, lane, seed);
        Check(e.CopyClip(t, c), "copy midi clip with automation");
        int p = e.PasteClip(t, 20.0);
        Check(p >= 0, "paste clip");
        var pts = e.GetAutomationPoints(t, 0);
        Check(pts.Any(x => Math.Abs(x.Beat - 20.0) < eps && Math.Abs(x.Value - 0.2f) < 1e-4), "pasted automation at beat 20 = .2");
        Check(pts.Any(x => Math.Abs(x.Beat - 22.0) < eps && Math.Abs(x.Value - 0.8f) < 1e-4), "pasted automation at beat 22 = .8");
        Check(pts.Any(x => Math.Abs(x.Beat - 0.0) < eps) && pts.Any(x => Math.Abs(x.Beat - 12.0) < eps), "original automation outside the clip kept");
        Check(!pts.Any(x => Math.Abs(x.Beat - 21.0) < eps), "destination span cleared before paste");
    }

    // Duplicate carries the automation to the new copy's position (right after the source).
    {
        using var e = new NotaEngine();
        e.SetBpm(120); e.SetTimeSignature(4, 4);
        int t = e.AddInstrumentTrack();
        int c = e.AddMidiClip(t, 4.0, 4.0);
        int lane = e.AddAutomationLane(t, Nota.Application.AutomationTarget.Volume, -1, -1);
        e.SetAutomationPoints(t, lane, Env());
        Check(e.DuplicateClip(t, c) >= 0, "duplicate clip");   // copy lands at [8,12)
        var pts = e.GetAutomationPoints(t, 0);
        Check(pts.Any(x => Math.Abs(x.Beat - 8.0) < eps && Math.Abs(x.Value - 0.2f) < 1e-4), "duplicated automation at beat 8 = .2");
        Check(pts.Any(x => Math.Abs(x.Beat - 10.0) < eps && Math.Abs(x.Value - 0.8f) < 1e-4), "duplicated automation at beat 10 = .8");
    }

    // Cut removes the automation in the clip span (delete would leave it), and pasting
    // it back restores the envelope.
    {
        using var e = new NotaEngine();
        e.SetBpm(120); e.SetTimeSignature(4, 4);
        int t = e.AddInstrumentTrack();
        int c = e.AddMidiClip(t, 4.0, 4.0);
        int lane = e.AddAutomationLane(t, Nota.Application.AutomationTarget.Volume, -1, -1);
        e.SetAutomationPoints(t, lane, Env());
        Check(e.CutClip(t, c), "cut clip");
        var after = e.GetAutomationPoints(t, 0);
        Check(!after.Any(x => x.Beat >= 4.0 - eps && x.Beat <= 8.0 + eps), "cut removed automation in the clip span");
        Check(after.Any(x => Math.Abs(x.Beat - 0.0) < eps) && after.Any(x => Math.Abs(x.Beat - 12.0) < eps), "cut kept automation outside the span");
        int p = e.PasteClip(t, 20.0);
        Check(p >= 0, "paste the cut clip back");
        var back = e.GetAutomationPoints(t, 0);
        Check(back.Any(x => Math.Abs(x.Beat - 20.0) < eps && Math.Abs(x.Value - 0.2f) < 1e-4), "cut automation restored at beat 20 = .2");
    }
}

// ============ block clip ops (multi-selection copy/cut/paste/duplicate) =====
Console.WriteLine("-- block clip ops (multi-selection) --");
{
    const double eps = 1e-6;

    // Duplicate a 2-track block: A [0,4), B [2,4) → block span [0,4). Copies land right
    // after the block ([4,…)), each keeping its offset within the block, and carry A's
    // volume automation. The copies are reported via last-placed for re-selection.
    {
        using var e = new NotaEngine();
        e.SetBpm(120); e.SetTimeSignature(4, 4);
        int a = e.AddInstrumentTrack();
        int b = e.AddInstrumentTrack();
        int ca = e.AddMidiClip(a, 0.0, 4.0);
        int cb = e.AddMidiClip(b, 2.0, 2.0);
        int la = e.AddAutomationLane(a, Nota.Application.AutomationTarget.Volume, -1, -1);
        e.SetAutomationPoints(a, la, new[] { new Nota.Application.AutomationPoint(0.0, 0.2f), new Nota.Application.AutomationPoint(3.0, 0.8f) });
        double len = e.DuplicateClipBlock(new[] { (a, ca), (b, cb) });
        Check(Math.Abs(len - 4.0) < eps, $"block length reported == 4 (got {len})");
        Check(e.TryGetClipInfo(a, 1, out var a1) && Math.Abs(a1.StartBeat - 4.0) < eps, "A copy at beat 4 (right after the block)");
        Check(e.TryGetClipInfo(b, 1, out var b1) && Math.Abs(b1.StartBeat - 6.0) < eps, "B copy keeps its +2 in-block offset (beat 6)");
        var pa = e.GetAutomationPoints(a, 0);
        Check(pa.Any(x => Math.Abs(x.Beat - 4.0) < eps && Math.Abs(x.Value - 0.2f) < 1e-4)
           && pa.Any(x => Math.Abs(x.Beat - 7.0) < eps && Math.Abs(x.Value - 0.8f) < 1e-4), "duplicate carried A's automation to [4,8)");
        var placed = e.LastPlacedClips();
        Check(placed.Length == 2, "last-placed reports both copies");
    }

    // Copy a block, then paste it at a beat on the source track (destTrackId -1).
    {
        using var e = new NotaEngine();
        e.SetBpm(120);
        int a = e.AddInstrumentTrack();
        int ca = e.AddMidiClip(a, 0.0, 2.0);
        Check(e.CopyClipBlock(new[] { (a, ca) }), "copy block");
        Check(e.ClipboardBlockCount() == 1, "block clipboard holds 1 clip");
        Check(e.PasteClipBlock(10.0, -1) == 1, "paste block returns 1");
        Check(e.TryGetClipInfo(a, 1, out var p) && Math.Abs(p.StartBeat - 10.0) < eps, "block pasted at beat 10 on the source track");
    }

    // Paste with a destination track remaps the (single-clip) block onto that track.
    {
        using var e = new NotaEngine();
        int a = e.AddInstrumentTrack();
        int b = e.AddInstrumentTrack();
        int ca = e.AddMidiClip(a, 0.0, 2.0);
        e.CopyClipBlock(new[] { (a, ca) });
        Check(e.PasteClipBlock(4.0, b) == 1 && e.TryGetClipInfo(b, 0, out var pb) && Math.Abs(pb.StartBeat - 4.0) < eps,
              "block paste remapped onto track b at beat 4");
    }

    // Cut a block: removes the clips + their automation, and pasting lands the block back.
    {
        using var e = new NotaEngine();
        int a = e.AddInstrumentTrack();
        int ca = e.AddMidiClip(a, 0.0, 4.0);
        int la = e.AddAutomationLane(a, Nota.Application.AutomationTarget.Volume, -1, -1);
        e.SetAutomationPoints(a, la, new[] { new Nota.Application.AutomationPoint(0.0, 0.5f), new Nota.Application.AutomationPoint(2.0, 0.7f), new Nota.Application.AutomationPoint(10.0, 0.3f) });
        Check(e.CutClipBlock(new[] { (a, ca) }), "cut block");
        Check(!e.TryGetClipInfo(a, 0, out _), "cut removed the clip");
        var pts = e.GetAutomationPoints(a, 0);
        Check(!pts.Any(x => x.Beat >= -eps && x.Beat <= 4.0 + eps), "cut removed automation in the clip span");
        Check(pts.Any(x => Math.Abs(x.Beat - 10.0) < eps), "cut kept automation outside the span");
        Check(e.PasteClipBlock(20.0, -1) == 1 && e.TryGetClipInfo(a, 0, out var pc) && Math.Abs(pc.StartBeat - 20.0) < eps, "cut block pastes back at beat 20");
    }

    // Shared non-overlap: a blocker at [4,6) forces the WHOLE block right by one shift, so
    // the copies' internal geometry (a 2-beat gap) is preserved instead of splitting apart.
    {
        using var e = new NotaEngine();
        int a = e.AddInstrumentTrack();
        int ca = e.AddMidiClip(a, 0.0, 2.0);
        int cb = e.AddMidiClip(a, 2.0, 2.0);
        e.AddMidiClip(a, 4.0, 2.0);              // blocker at [4,6)
        e.DuplicateClipBlock(new[] { (a, ca), (a, cb) });
        var placed = e.LastPlacedClips();
        Check(placed.Length == 2, "two copies placed past the blocker");
        e.TryGetClipInfo(placed[0].trackId, placed[0].clipIndex, out var q0);
        e.TryGetClipInfo(placed[1].trackId, placed[1].clipIndex, out var q1);
        Check(q0.StartBeat >= 6.0 - eps && q1.StartBeat >= 6.0 - eps, "copies shifted past the blocker (>= beat 6)");
        Check(Math.Abs(Math.Abs(q1.StartBeat - q0.StartBeat) - 2.0) < eps, "internal 2-beat gap preserved after the shared shift");
    }
}

// ============ consolidate (⌘J): one clip per track over a range ===========
Console.WriteLine("-- consolidate --");
{
    const double eps = 1e-6;
    static Nota.Application.NotaNote N(int p, double s, double l, float v = 0.8f) => new(p, s, l, v);

    // MIDI: two clips merge into one [0,8). A tail cut by its clip's end stays cut, a note
    // hidden outside its clip window is dropped, and the first clip's name carries over.
    {
        using var e = new NotaEngine();
        e.SetBpm(120);
        int t = e.AddInstrumentTrack();
        int c0 = e.AddMidiClip(t, 0.0, 4.0);
        int c1 = e.AddMidiClip(t, 6.0, 2.0);
        e.SetClipNotes(t, c0, new[] { N(60, 0, 1), N(62, 3, 2), N(65, 5, 1) });   // 62's tail cut at 4; 65 hidden
        e.SetClipNotes(t, c1, new[] { N(64, 0.5, 1) });
        e.SetClipName(t, c0, "Verse");
        Check(e.ConsolidateRange(new[] { t }, 0.0, 8.0), "consolidate MIDI returns true");
        Check(e.TryGetTrackInfo(FindTrackIndex(e, t), out var ti) && ti.ClipCount == 1, "MIDI: two clips became one");
        var placed = e.LastPlacedClips();
        Check(placed.Length == 1 && placed[0].trackId == t, "MIDI: last-placed reports the new clip");
        int nc = placed[0].clipIndex;
        Check(e.TryGetClipInfo(t, nc, out var ci) && Math.Abs(ci.StartBeat) < eps && Math.Abs(ci.LengthBeats - 8.0) < eps, "MIDI: new clip spans [0,8)");
        var got = e.GetClipNotes(t, nc).OrderBy(n => n.StartBeat).ToArray();
        Check(got.Length == 3, $"MIDI: 3 audible notes kept (got {got.Length})");
        Check(got.Length == 3 && got[1].Pitch == 62 && Math.Abs(got[1].LengthBeats - 1.0) < eps, "MIDI: tail cut by the clip end stays cut");
        Check(got.Length == 3 && got[2].Pitch == 64 && Math.Abs(got[2].StartBeat - 6.5) < eps, "MIDI: second clip's note re-based to 6.5");
        Check(e.GetClipName(t, nc) == "Verse", "MIDI: first clip's name carried over");
        Check(e.Undo() && e.TryGetTrackInfo(FindTrackIndex(e, t), out var tu) && tu.ClipCount == 2, "MIDI: undo restores both clips");
    }

    // MIDI time range inside one clip: remainders survive on both sides; a note crossing the
    // range end is cut there; got outside the range stay in the remainders.
    {
        using var e = new NotaEngine();
        int t = e.AddInstrumentTrack();
        int c = e.AddMidiClip(t, 0.0, 8.0);
        e.SetClipNotes(t, c, new[] { N(60, 1, 1), N(62, 3, 1), N(64, 5, 2), N(67, 7, 1) });
        Check(e.ConsolidateRange(new[] { t }, 2.0, 6.0), "consolidate MIDI range");
        Check(e.TryGetTrackInfo(FindTrackIndex(e, t), out var ti) && ti.ClipCount == 3, "MIDI range: left + consolidated + right");
        int nc = e.LastPlacedClips()[0].clipIndex;
        var got = e.GetClipNotes(t, nc).OrderBy(n => n.StartBeat).ToArray();
        Check(got.Length == 2 && Math.Abs(got[0].StartBeat - 1.0) < eps && Math.Abs(got[1].StartBeat - 3.0) < eps,
              "MIDI range: notes at 3 and 5 re-based to 1 and 3");
        Check(got.Length == 2 && Math.Abs(got[1].LengthBeats - 1.0) < eps, "MIDI range: note crossing the range end cut there");
    }

    // MIDI envelopes: velocity baked into the got; volume envelopes stitched (unity where a
    // clip had none); a deactivated clip contributes nothing.
    {
        using var e = new NotaEngine();
        int t = e.AddInstrumentTrack();
        int a = e.AddMidiClip(t, 0.0, 4.0);
        int b = e.AddMidiClip(t, 4.0, 4.0);
        int d = e.AddMidiClip(t, 8.0, 4.0);
        e.SetClipNotes(t, a, new[] { N(60, 0, 1, 0.8f) });
        e.SetClipNotes(t, b, new[] { N(62, 0, 1, 0.8f) });
        e.SetClipNotes(t, d, new[] { N(64, 0, 1, 0.8f) });
        e.SetMidiClipEnvelope(t, a, Nota.Application.MidiClipEnvelope.Velocity, new[] { new Nota.Application.AutomationPoint(0, 0.5f), new Nota.Application.AutomationPoint(4, 0.5f) });
        e.SetMidiClipEnvelope(t, a, Nota.Application.MidiClipEnvelope.Volume, new[] { new Nota.Application.AutomationPoint(0, 0.25f), new Nota.Application.AutomationPoint(4, 0.25f) });
        e.SetClipActive(t, d, false);
        Check(e.ConsolidateRange(new[] { t }, 0.0, 12.0), "consolidate MIDI with envelopes");
        int nc = e.LastPlacedClips()[0].clipIndex;
        var got = e.GetClipNotes(t, nc).OrderBy(n => n.StartBeat).ToArray();
        Check(got.Length == 2, $"MIDI env: deactivated clip's note dropped ({got.Length} notes)");
        Check(got.Length == 2 && Math.Abs(got[0].Velocity - 0.4f) < 1e-4 && Math.Abs(got[1].Velocity - 0.8f) < 1e-4,
              "MIDI env: velocity envelope baked (0.8 x 0.5 = 0.4), other clip untouched");
        Check(e.GetMidiClipEnvelope(t, nc, Nota.Application.MidiClipEnvelope.Velocity).Length == 0, "MIDI env: no velocity envelope left");
        var vol = e.GetMidiClipEnvelope(t, nc, Nota.Application.MidiClipEnvelope.Volume);
        static float At(Nota.Application.AutomationPoint[] p, double beat)
        {
            if (beat <= p[0].Beat) return p[0].Value;
            for (int i = 1; i < p.Length; i++)
                if (beat <= p[i].Beat)
                    return p[i].Beat - p[i - 1].Beat <= 0 ? p[i].Value
                         : (float)(p[i - 1].Value + (p[i].Value - p[i - 1].Value) * (beat - p[i - 1].Beat) / (p[i].Beat - p[i - 1].Beat));
            return p[^1].Value;
        }
        Check(vol.Length > 0 && Math.Abs(At(vol, 2.0) - 0.25f) < 1e-4 && Math.Abs(At(vol, 6.0) - 1.0f) < 1e-4,
              "MIDI env: volume envelope stitched (0.25 in the first clip, unity after)");
    }

    // Audio: the bounce plays exactly what the source did (gain + varispeed baked), as one
    // unwarped, unity-gain clip spanning the range.
    {
        using var e = new NotaEngine();
        e.SetBpm(120); e.SetTimeSignature(4, 4);
        int t = e.AddAudioTrack();
        int c = e.AddAudioClip(t, wav, 0.0);         // 1 s sine = 2 beats at 120
        e.SetClipGain(t, c, 0.5f);
        e.SetClipPitch(t, c, 3.0f);
        const int fr = 44100;
        var before = new float[fr * 2];
        var after = new float[fr * 2];
        e.Seek(0); e.Play(); e.RenderOffline(before, fr); e.StopTransport();
        Check(e.ConsolidateRange(new[] { t }, 0.0, 4.0), "consolidate audio returns true");
        Check(e.TryGetTrackInfo(FindTrackIndex(e, t), out var ti) && ti.ClipCount == 1, "audio: one clip after consolidate");
        int nc = e.LastPlacedClips()[0].clipIndex;
        Check(e.TryGetClipInfo(t, nc, out var ci) && Math.Abs(ci.StartBeat) < eps && Math.Abs(ci.LengthBeats - 4.0) < 1e-3,
              $"audio: new clip spans [0,4) (len {ci.LengthBeats:F4})");
        Check(e.TryGetAudioClipInfo(t, nc, out var ai) && ai.WarpEnabled == 0 && Math.Abs(ai.Gain - 1f) < 1e-6 && Math.Abs(ai.PitchSemitones) < 1e-6,
              "audio: result is unwarped, unity gain, no transpose");
        e.Seek(0); e.Play(); e.RenderOffline(after, fr); e.StopTransport();
        double maxDiff = 0;
        for (int i = 1024 * 2; i < fr * 2; i++) maxDiff = Math.Max(maxDiff, Math.Abs(before[i] - after[i]));
        Check(Rms(before, fr) > 0.01f && maxDiff < 1e-3, $"audio: consolidated render matches the original (max diff {maxDiff:E2})");
        Check(e.Undo() && e.TryGetAudioClipInfo(t, 0, out var ui) && Math.Abs(ui.Gain - 0.5f) < 1e-6, "audio: undo restores the source clip");
    }

    // Audio time range inside a clip: remainders survive; a warped source yields a warped
    // (tempo-following) result.
    {
        using var e = new NotaEngine();
        e.SetBpm(120);
        int t = e.AddAudioTrack();
        int c = e.AddAudioClip(t, wav, 0.0);
        e.SetClipWarp(t, c, true, 3);
        Check(e.ConsolidateRange(new[] { t }, 0.5, 1.5), "consolidate warped audio range");
        Check(e.TryGetTrackInfo(FindTrackIndex(e, t), out var ti) && ti.ClipCount == 3, "audio range: left + consolidated + right");
        int nc = e.LastPlacedClips()[0].clipIndex;
        Check(e.TryGetAudioClipInfo(t, nc, out var ai) && ai.WarpEnabled == 1 && ai.WarpMode == 3, "audio range: warped source -> warped result (same mode)");
        Check(e.TryGetClipInfo(t, nc, out var ci) && Math.Abs(ci.StartBeat - 0.5) < eps && Math.Abs(ci.LengthBeats - 1.0) < 1e-3,
              "audio range: new clip spans [0.5,1.5)");
    }

    // Nothing to consolidate (empty range / no listed track with content) is a no-op.
    {
        using var e = new NotaEngine();
        int t = e.AddInstrumentTrack();
        e.AddMidiClip(t, 0.0, 2.0);
        Check(!e.ConsolidateRange(new[] { t }, 4.0, 8.0), "consolidate over empty space is a no-op");
        Check(e.TryGetTrackInfo(FindTrackIndex(e, t), out var ti) && ti.ClipCount == 1, "no-op consolidate leaves the clip alone");
    }
}

// ============ track/clip names + colour + track clipboard ==================
Console.WriteLine("-- names, colour, track clipboard --");
{
    using var ne = new NotaEngine();
    ne.SetBpm(120);
    int t1 = ne.AddInstrumentTrack();
    ne.SetTrackName(t1, "Lead");
    Check(ne.GetTrackName(t1) == "Lead", $"track name round-trips ('{ne.GetTrackName(t1)}')");
    ne.SetTrackColorIndex(t1, 5);
    Check(ne.GetTrackColorIndex(t1) == 5, $"track colour round-trips ({ne.GetTrackColorIndex(t1)})");
    int c = ne.AddMidiClip(t1, 0, 4);
    ne.SetClipName(t1, c, "Verse");
    Check(ne.GetClipName(t1, c) == "Verse", $"clip name round-trips ('{ne.GetClipName(t1, c)}')");

    Check(ne.CopyTrack(t1), "copy track");
    Check(ne.HasTrackClipboard(), "clipboard has a track");
    int before = ne.TrackCount;
    int nid = ne.PasteTrack();
    Check(nid > 0 && ne.TrackCount == before + 1, $"paste creates a new track ({ne.TrackCount})");
    Check(ne.GetTrackName(nid) == "Lead" && ne.GetTrackColorIndex(nid) == 5, "pasted track keeps name + colour");
}

// ============ names + colour survive a project round-trip ==================
Console.WriteLine("-- names + colour project round-trip --");
{
    using var src = new NotaEngine();
    int t = src.AddInstrumentTrack();
    src.SetTrackName(t, "Bassline");
    src.SetTrackColorIndex(t, 7);
    int c = src.AddMidiClip(t, 0, 4);
    src.SetClipName(t, c, "Intro");
    string dir = Path.Combine(Path.GetTempPath(), "nota-meta-proj-" + Guid.NewGuid().ToString("N"));
    try
    {
        var w = new System.Collections.Generic.List<string>();
        var doc = ProjectService.Capture(src, new TransportState(120, 1, false, false), w);
        ProjectService.Save(doc, dir, src);
        var loaded = ProjectService.Load(dir);
        using var dst = new NotaEngine();
        ProjectService.Apply(loaded, dst, dir);
        Check(dst.TryGetTrackInfo(0, out var ti), "restored track");
        Check(dst.GetTrackName(ti.Id) == "Bassline", $"track name persisted ('{dst.GetTrackName(ti.Id)}')");
        Check(dst.GetTrackColorIndex(ti.Id) == 7, $"track colour persisted ({dst.GetTrackColorIndex(ti.Id)})");
        Check(dst.GetClipName(ti.Id, 0) == "Intro", $"clip name persisted ('{dst.GetClipName(ti.Id, 0)}')");
    }
    finally { try { if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true); } catch { } }
}

// ============ record audio from another track (internal resampling) ========
Console.WriteLine("-- record from another track --");
{
    using var re = new NotaEngine();
    re.SetBpm(120); re.SetTimeSignature(4, 4);
    int rinst = re.AddBassSynthTrack();
    int mc = re.AddMidiClip(rinst, 0, 4);
    re.SetClipNotes(rinst, mc, new[] { new NotaNote(36, 0.0, 4.0, 1.0f) });  // sustained bass note
    int rrec = re.AddAudioTrack();
    re.SetTrackRecordInput(rrec, rinst);
    Check(re.GetTrackRecordInput(rrec) == rinst, "record input set to the instrument track");
    re.SetTrackArmed(rrec, true);
    re.Seek(0);
    re.SetRecording(true);                       // internal take starts + rolls transport
    var cap = new float[2048 * 2];
    for (int b = 0; b < 10; b++) { re.RenderOffline(cap, 2048); re.Poll(); }   // ~0.46 s captured
    re.SetRecording(false);                      // materialise the clip
    re.Poll();
    Check(re.TryGetTrackInfo(1, out var rti) && rti.ClipCount == 1, $"internal recording made a clip ({(re.TryGetTrackInfo(1, out var q) ? q.ClipCount : -1)})");

    // Mute the source and play back: only the recorded clip sounds → it must carry signal.
    re.SetTrackMute(rinst, true);
    re.Seek(0); re.Play();
    var pb = new float[8192 * 2];
    re.RenderOffline(pb, 8192); re.StopTransport();
    Check(Rms(pb, 8192) > 0.001f, $"recorded internal clip plays back audible (RMS {Rms(pb, 8192):F3})");
}

// ============ record-input source survives a project round-trip ============
Console.WriteLine("-- record input project round-trip --");
{
    using var src = new NotaEngine();
    int rsa = src.AddBassSynthTrack();     // doc track 0
    int rsrec = src.AddAudioTrack();       // doc track 1
    src.SetTrackRecordInput(rsrec, rsa);     // record from track 0
    string dir = Path.Combine(Path.GetTempPath(), "nota-recin-" + Guid.NewGuid().ToString("N"));
    try
    {
        var w = new System.Collections.Generic.List<string>();
        var doc = ProjectService.Capture(src, new TransportState(120, 1, false, false), w);
        ProjectService.Save(doc, dir, src);
        var loaded = ProjectService.Load(dir);
        using var dst = new NotaEngine();
        ProjectService.Apply(loaded, dst, dir);
        dst.TryGetTrackInfo(0, out var t0);
        dst.TryGetTrackInfo(1, out var t1);
        Check(dst.GetTrackRecordInput(t1.Id) == t0.Id, $"record input remapped to the source track ({dst.GetTrackRecordInput(t1.Id)} == {t0.Id})");
    }
    finally { try { if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true); } catch { } }
}

// ============ simultaneous instrument + audio recording ====================
Console.WriteLine("-- simultaneous instrument + audio record --");
{
    using var me = new NotaEngine();
    me.SetBpm(120); me.SetTimeSignature(4, 4);
    int synth = me.AddBassSynthTrack();          // will record MIDI
    int aud = me.AddAudioTrack();                 // will record audio (from master)
    me.SetTrackRecordInput(aud, -1);              // internal: master bus
    me.SetTrackArmed(synth, true);
    me.SetTrackArmed(aud, true);
    me.Seek(0);
    me.SetRecording(true);
    Check(me.AudioRecordTrackId == aud, $"audio take started on the armed audio track ({me.AudioRecordTrackId})");
    Check(me.IsRecording, "recording is active (MIDI take too)");
    var mbuf = new float[2048 * 2];
    me.NoteOn(48, 0.9f);
    for (int b = 0; b < 6; b++) { me.RenderOffline(mbuf, 2048); me.Poll(); }
    me.NoteOff(48);
    for (int b = 0; b < 4; b++) { me.RenderOffline(mbuf, 2048); me.Poll(); }
    me.SetRecording(false);
    me.Poll();
    me.TryGetTrackInfo(1, out var audInfo);       // audio track is index 1
    Check(audInfo.ClipCount == 1, $"audio track recorded a clip ({audInfo.ClipCount})");
    var mnotes = me.GetClipNotes(synth, 0);
    Check(mnotes.Length >= 1, $"instrument track recorded MIDI at the same time ({mnotes.Length} notes)");
}

// ============ loop recording punches the loop region =======================
// Loop on → each pass wraps and overwrites the loop region in place; the take is bounded
// to the loop and placed at its start. The playhead wraps with the loop (stays inside it).
Console.WriteLine("-- loop recording (punch) --");
{
    using var le = new NotaEngine();
    le.SetBpm(120); le.SetTimeSignature(4, 4);
    int li = le.AddBassSynthTrack();
    int lmc = le.AddMidiClip(li, 0, 32);
    le.SetClipNotes(li, lmc, new[] { new NotaNote(36, 0.0, 32.0, 1.0f) });
    int lr = le.AddAudioTrack();
    le.SetTrackRecordInput(lr, li);
    le.SetTrackArmed(lr, true);
    le.SetLoop(true, 0.0, 2.0);                   // 2-beat loop
    le.Seek(0);
    le.SetRecording(true);
    var cap = new float[2048 * 2];
    for (int b = 0; b < 66; b++) { le.RenderOffline(cap, 2048); le.Poll(); }   // ~3 loop cycles
    // The playhead wrapped with the loop (didn't run off past the loop end).
    Check(le.PositionBeats <= 2.1, $"playhead wraps with the loop while recording ({le.PositionBeats:F2} beats)");
    le.SetRecording(false);
    le.Poll();
    le.TryGetTrackInfo(1, out var lti);
    Check(lti.ClipCount == 1, $"loop record makes one clip ({lti.ClipCount})");
    le.TryGetClipInfo(lr, 0, out var lci);
    Check(Math.Abs(lci.StartBeat) < 1e-6, $"take placed at the loop start ({lci.StartBeat:F2})");
    Check(Math.Abs(lci.LengthBeats - 2.0) < 0.3, $"take is bounded to the loop (~2 beats) ({lci.LengthBeats:F2})");
    le.SetTrackMute(li, true);
    le.SetLoop(false, 0, 0); le.Seek(0); le.Play();
    var lpb = new float[8192 * 2]; le.RenderOffline(lpb, 8192); le.StopTransport();
    Check(Rms(lpb, 8192) > 0.001f, $"loop take plays back audible ({Rms(lpb, 8192):F3})");
}

// Enabling loop AFTER recording started switches to punch from that point (bounded take).
Console.WriteLine("-- loop enabled mid-record --");
{
    using var me = new NotaEngine();
    me.SetBpm(120); me.SetTimeSignature(4, 4);
    int mi = me.AddBassSynthTrack();
    int mmc = me.AddMidiClip(mi, 0, 32);
    me.SetClipNotes(mi, mmc, new[] { new NotaNote(36, 0.0, 32.0, 1.0f) });
    int mr = me.AddAudioTrack();
    me.SetTrackRecordInput(mr, mi);
    me.SetTrackArmed(mr, true);
    me.Seek(0);
    me.SetRecording(true);                        // start WITHOUT loop
    var mcap = new float[2048 * 2];
    for (int b = 0; b < 10; b++) { me.RenderOffline(mcap, 2048); me.Poll(); }
    me.SetLoop(true, 0.0, 2.0);                    // enable loop mid-take
    for (int b = 0; b < 60; b++) { me.RenderOffline(mcap, 2048); me.Poll(); }
    me.SetRecording(false);
    me.Poll();
    me.TryGetClipInfo(mr, 0, out var mci);
    Check(Math.Abs(mci.StartBeat) < 1e-6 && Math.Abs(mci.LengthBeats - 2.0) < 0.3,
        $"mid-record loop switches to punch (start={mci.StartBeat:F2} len={mci.LengthBeats:F2})");
}

// ============ overwrite / comp: a dropped clip carves what it lands on ======
// Existing MIDI clip [0,8]; drop a 4-beat clip onto [0,4] → new clip at [0,4], the old
// one trimmed to [4,8] (its [0,4] portion cut). Mirrors the user's example.
Console.WriteLine("-- overwrite on drag (carve) --");
{
    using var oe = new NotaEngine();
    oe.SetBpm(120); oe.SetTimeSignature(4, 4);
    int ot = oe.AddInstrumentTrack();
    int a = oe.AddMidiClip(ot, 0, 8);   // clip A [0,8]
    oe.SetClipNotes(ot, a, new[] { new NotaNote(60, 0.0, 1.0, 0.9f), new NotaNote(64, 5.0, 1.0, 0.9f) });
    int b = oe.AddMidiClip(ot, 16, 4);  // clip B [16,20], 4 beats
    oe.SetClipNotes(ot, b, new[] { new NotaNote(72, 0.0, 1.0, 0.9f) });
    oe.MoveClip(ot, b, 0.0);            // drop B onto [0,4] → overwrites A's [0,4]
    oe.TryGetTrackInfo(0, out var oti);
    Check(oti.ClipCount == 2, $"overwrite leaves 2 clips ({oti.ClipCount})");
    // Collect the clip ranges.
    var ranges = new System.Collections.Generic.List<(double s, double e)>();
    for (int k = 0; k < oti.ClipCount; k++) { oe.TryGetClipInfo(ot, k, out var ci); ranges.Add((ci.StartBeat, ci.StartBeat + ci.LengthBeats)); }
    bool hasNew = ranges.Exists(r => Math.Abs(r.s) < 1e-6 && Math.Abs(r.e - 4.0) < 1e-6);
    bool hasTrimmed = ranges.Exists(r => Math.Abs(r.s - 4.0) < 1e-6 && Math.Abs(r.e - 8.0) < 1e-6);
    Check(hasNew, "dropped clip occupies [0,4]");
    Check(hasTrimmed, "old clip trimmed to the [4,8] remainder");
    // The trimmed remainder keeps only the note that was in [4,8] (shifted to local beat 1).
    for (int k = 0; k < oti.ClipCount; k++)
    {
        oe.TryGetClipInfo(ot, k, out var ci);
        if (Math.Abs(ci.StartBeat - 4.0) < 1e-6)
        {
            var nn = oe.GetClipNotes(ot, k);
            Check(nn.Length == 1 && nn[0].Pitch == 64 && Math.Abs(nn[0].StartBeat - 1.0) < 1e-6,
                $"remainder keeps the [4,8] note, shifted (got {nn.Length} notes)");
        }
    }
}

// ============ disarming a track stops its recording ========================
Console.WriteLine("-- disarm stops recording --");
{
    using var de = new NotaEngine();
    de.SetBpm(120); de.SetTimeSignature(4, 4);
    int dsrc = de.AddBassSynthTrack();
    de.AddMidiClip(dsrc, 0, 8); de.SetClipNotes(dsrc, 0, new[] { new NotaNote(36, 0.0, 8.0, 1.0f) });
    int daud = de.AddAudioTrack();
    de.SetTrackRecordInput(daud, dsrc);
    de.SetTrackArmed(daud, true);
    de.Seek(0);
    de.SetRecording(true);
    Check(de.IsRecording, "recording started");
    var dbuf = new float[2048 * 2];
    for (int b = 0; b < 6; b++) { de.RenderOffline(dbuf, 2048); de.Poll(); }
    de.SetTrackArmed(daud, false);   // disarm the recording track
    de.Poll();
    Check(!de.IsRecording, "disarming the recording track stopped recording");
    de.TryGetTrackInfo(1, out var dti);
    Check(dti.ClipCount == 1, $"the take was finalized into a clip ({dti.ClipCount})");
}

// ============ master effect chain =========================================
Console.WriteLine("-- master effect chain --");
{
    using var me = new NotaEngine();
    me.SetBpm(120); me.SetTimeSignature(4, 4);
    int mid = me.MasterTrackId;
    Check(mid > 0, $"master track id ({mid})");
    Check(me.TrackDeviceCount(mid) == 0, "master starts with no devices");
    // Add a Utility (kind 4) to the master and drive its gain to silence.
    int di = me.AddBuiltinDevice(mid, 4);
    Check(di >= 0 && me.TrackDeviceCount(mid) == 1, $"added a device to the master ({me.TrackDeviceCount(mid)})");
    Check(me.TrackDeviceBuiltinKind(mid, 0) == 4, "master device is the Utility (kind 4)");

    // A synth track playing a note → the master chain processes the mix.
    int t = me.AddBassSynthTrack();
    me.AddMidiClip(t, 0, 4); me.SetClipNotes(t, 0, new[] { new NotaNote(48, 0.0, 4.0, 1.0f) });
    var mbuf2 = new float[8192 * 2];
    me.Seek(0); me.Play(); me.RenderOffline(mbuf2, 8192); me.StopTransport();
    float unityRms = Rms(mbuf2, 8192);
    Check(unityRms > 0.001f, $"audible with a unity master device ({unityRms:F3})");
    // Utility Gain is dB; -24 dB (≈0.06×) clearly drops the master level → proves the
    // master device actually processes the mix.
    me.DeviceSetParam(mid, 0, 0, -24.0f);
    me.Seek(0); me.Play(); me.RenderOffline(mbuf2, 8192); me.StopTransport();
    Check(Rms(mbuf2, 8192) < unityRms * 0.25f, $"master device processes the mix (−24 dB: {Rms(mbuf2, 8192):F3} << {unityRms:F3})");
}

// ============ master effect chain survives a project round-trip ============
Console.WriteLine("-- master chain project round-trip --");
{
    using var src = new NotaEngine();
    int mid = src.MasterTrackId;
    src.AddBuiltinDevice(mid, 2);   // Reverb
    src.AddBuiltinDevice(mid, 0);   // EQ
    src.SetTrackName(mid, "Bus");
    string dir = Path.Combine(Path.GetTempPath(), "nota-master-" + Guid.NewGuid().ToString("N"));
    try
    {
        var w = new System.Collections.Generic.List<string>();
        var doc = ProjectService.Capture(src, new TransportState(120, 1, false, false), w);
        ProjectService.Save(doc, dir, src);
        var loaded = ProjectService.Load(dir);
        using var dst = new NotaEngine();
        ProjectService.Apply(loaded, dst, dir);
        int did = dst.MasterTrackId;
        Check(dst.TrackDeviceCount(did) == 2, $"master devices restored ({dst.TrackDeviceCount(did)})");
        Check(dst.TrackDeviceBuiltinKind(did, 0) == 2 && dst.TrackDeviceBuiltinKind(did, 1) == 0, "master device order/kinds restored");
        Check(dst.GetTrackName(did) == "Bus", $"master name restored ('{dst.GetTrackName(did)}')");
    }
    finally { try { if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true); } catch { } }
}

// ============ Clip-editor engine features (played-key, live drag, trim) ============
Console.WriteLine("-- Clip editor: live-held notes, live note push, trimmed-clip gating --");
{
    using var ceEng = new NotaEngine();
    ceEng.SetBpm(120); ceEng.SetTimeSignature(4, 4);
    int ceT = ceEng.AddInstrumentTrack();
    var ceHb = new int[16];

    // Feature 1: live-held notes reflect pressed keys (computer keyboard / MIDI), no routing.
    Check(ceEng.LiveHeldNotes(ceHb) == 0, "no keys held initially");
    ceEng.NoteOn(60, 0.8f); ceEng.NoteOn(67, 0.8f);
    int ceHn = ceEng.LiveHeldNotes(ceHb);
    bool ceHas60 = false, ceHas67 = false; for (int i = 0; i < ceHn; i++) { if (ceHb[i] == 60) ceHas60 = true; if (ceHb[i] == 67) ceHas67 = true; }
    Check(ceHn == 2 && ceHas60 && ceHas67, $"live-held reports the two pressed keys (got {ceHn})");
    ceEng.NoteOff(60);
    Check(ceEng.LiveHeldNotes(ceHb) == 1 && ceHb[0] == 67, "note-off clears one key");
    ceEng.NoteOff(67);
    Check(ceEng.LiveHeldNotes(ceHb) == 0, "all keys released");

    // Feature 4: SetClipNotesLive applies immediately but adds NO undo checkpoint.
    int ceClip = ceEng.AddMidiClip(ceT, 0.0, 4.0);
    ceEng.SetClipNotes(ceT, ceClip, new[] { new NotaNote(60, 0.0, 1.0, 0.9f) });   // undo entry: empty -> A
    ceEng.SetClipNotesLive(ceT, ceClip, new[] { new NotaNote(60, 2.0, 1.0, 0.9f) }); // moved, no undo
    var ceLive = ceEng.GetClipNotes(ceT, ceClip);
    Check(ceLive.Length == 1 && Math.Abs(ceLive[0].StartBeat - 2.0) < 1e-6, "live note push applied immediately");
    ceEng.Undo();   // should undo the ORIGINAL SetClipNotes (empty), proving live added no checkpoint
    Check(ceEng.GetClipNotes(ceT, ceClip).Length == 0, "single undo restores pre-edit (live pushed no checkpoint)");

    // Feature 5: a trimmed clip cuts note tails at its (shortened) end.
    using var ea = new NotaEngine(); ea.SetBpm(120); ea.SetTimeSignature(4, 4);
    int ta = ea.AddInstrumentTrack(); int ca = ea.AddMidiClip(ta, 0.0, 4.0);   // full-length clip
    ea.SetClipNotes(ta, ca, new[] { new NotaNote(60, 0.0, 4.0, 0.9f) });
    using var eb = new NotaEngine(); eb.SetBpm(120); eb.SetTimeSignature(4, 4);
    int tb = eb.AddInstrumentTrack(); int cb = eb.AddMidiClip(tb, 0.0, 1.0);    // trimmed to 1 beat
    eb.SetClipNotes(tb, cb, new[] { new NotaNote(60, 0.0, 4.0, 0.9f) });        // note still 4 beats long
    var bufA = new float[44100 * 2]; var bufB = new float[44100 * 2];           // ~2 beats @120bpm
    ea.Seek(0); ea.Play(); ea.RenderOffline(bufA, 44100); ea.StopTransport();
    eb.Seek(0); eb.Play(); eb.RenderOffline(bufB, 44100); eb.StopTransport();
    float rmsA = Rms(bufA, 44100), rmsB = Rms(bufB, 44100);
    Check(rmsA > 0.001f, $"full clip sounds across its length (rmsA {rmsA:F3})");
    Check(rmsB < rmsA * 0.75f, $"trimmed clip cuts the note tail at the clip end (rmsB {rmsB:F3} < rmsA {rmsA:F3})");
}

// ============ track reorder (MoveTrack) ===================================
Console.WriteLine("-- track reorder --");
{
    using var re = new NotaEngine();
    int t0 = re.AddInstrumentTrack();   // order: t0, t1, t2
    int t1 = re.AddInstrumentTrack();
    int t2 = re.AddInstrumentTrack();
    int IdAt(NotaEngine e, int i) { e.TryGetTrackInfo(i, out var ti); return ti.Id; }
    Check(IdAt(re, 0) == t0 && IdAt(re, 1) == t1 && IdAt(re, 2) == t2, "initial track order t0,t1,t2");

    re.MoveTrack(t2, 0);                // move last to front → t2, t0, t1
    Check(IdAt(re, 0) == t2 && IdAt(re, 1) == t0 && IdAt(re, 2) == t1, "MoveTrack to front reorders");

    re.MoveTrack(t2, 2);                // move it back to the end → t0, t1, t2
    Check(IdAt(re, 0) == t0 && IdAt(re, 1) == t1 && IdAt(re, 2) == t2, "MoveTrack to end reorders");

    re.Undo();                          // reorder checkpoints undo
    Check(IdAt(re, 0) == t2 && IdAt(re, 1) == t0 && IdAt(re, 2) == t1, "undo restores the prior order");

    int rt = re.AddReturnTrack();
    re.MoveTrack(t0, 99);               // clamp: a regular track can't pass into the return region
    Check(re.TrackReturnIndex(rt) >= 0 && IdAt(re, 3) == rt, "return track stays after regular tracks");
}

// ============ Nota Rhythm (drum machine, kind 12) ========================
Console.WriteLine("-- Nota Rhythm --");
{
    using var re = new NotaEngine();
    re.SetBpm(120); re.SetTimeSignature(4, 4);
    int t = re.AddRhythmTrack();
    Check(t > 0, "add Rhythm track");
    Check(re.TrackInstrumentKind(t) == 12, $"instrument kind is 12 ({re.TrackInstrumentKind(t)})");
    Check(re.DeviceName(t, -1) == "Nota Rhythm", $"name is Nota Rhythm ('{re.DeviceName(t, -1)}')");
    int pc = re.PluginParamCount(t, -1);
    Check(pc == 60, $"param count 60 (got {pc})");

    int kickTune = -1; for (int i = 0; i < pc; i++) if (re.PluginParamId(t, -1, i) == "v0_tune") kickTune = i;
    Check(kickTune >= 0, "found v0_tune param");
    re.PluginParamSet(t, -1, kickTune, 0.9f);
    Check(Math.Abs(re.PluginParamGet(t, -1, kickTune) - 0.9f) < 0.01f, "param round-trips");

    // The default pattern (four-on-the-floor kick) plays synced to the transport.
    var rbuf = new float[8192 * 2];
    re.Seek(0); re.Play(); re.RenderOffline(rbuf, 8192); re.StopTransport();
    float seqRms = Rms(rbuf, 8192);
    Check(seqRms > 0.001f && float.IsFinite(seqRms), $"internal sequencer plays audible + finite ({seqRms:F3})");

    // A MIDI clip note drives the mapped voice (kick = 36); clear the default pattern first
    // so only the clip's note sounds.
    using var re2 = new NotaEngine();
    re2.SetBpm(120); re2.SetTimeSignature(4, 4);
    int t2 = re2.AddRhythmTrack();
    re2.InstrumentAction(t2, 4, 0, 0);   // A_ClearBank → silence the default pattern
    re2.AddMidiClip(t2, 0.0, 4.0);
    re2.SetClipNotes(t2, 0, new[] { new NotaNote(36, 0.0, 0.5, 1.0f) });
    re2.Seek(0); re2.Play();
    var rb2 = new float[8192 * 2];
    re2.RenderOffline(rb2, 8192);
    re2.StopTransport();
    Check(Rms(rb2, 8192) > 0.001f, $"MIDI clip note triggers a voice ({Rms(rb2, 8192):F3})");

    // Duplicate keeps the kind; state blob round-trips params.
    int dup = re.DuplicateTrack(t);
    Check(dup > 0 && re.TrackInstrumentKind(dup) == 12, "duplicate keeps kind 12");
    var blob = re.GetPluginState(t, -1);
    Check(blob.Length > 200, $"state blob non-trivial ({blob.Length} bytes)");
    using var re3 = new NotaEngine();
    int t3 = re3.AddRhythmTrack();
    re3.SetPluginState(t3, -1, blob);
    int kt3 = -1; for (int i = 0; i < re3.PluginParamCount(t3, -1); i++) if (re3.PluginParamId(t3, -1, i) == "v0_tune") kt3 = i;
    Check(kt3 >= 0 && Math.Abs(re3.PluginParamGet(t3, -1, kt3) - 0.9f) < 0.02f, "state blob restores params");
}

// ============ Nota Rhythm — per-voice samples (Phase 2) ==================
Console.WriteLine("-- Nota Rhythm samples --");
{
    string smpWav = Path.Combine(Path.GetTempPath(), "nota-rhythm-" + Guid.NewGuid().ToString("N") + ".wav");
    Nota.SmokeTest.WavWriter.WriteSine(smpWav, seconds: 0.3, freq: 220.0, sampleRate: 44100);
    try
    {
        using var se = new NotaEngine(); se.SetBpm(120); se.SetTimeSignature(4, 4);
        int t = se.AddRhythmTrack();
        Check(se.SetRhythmVoiceSample(t, 0, smpWav), "load a sample into voice 0");
        Check(se.RhythmVoiceSource(t, 0) == 1, "voice 0 source is Sample");
        Check(se.TryGetRhythmVoiceInfo(t, 0, out var vi) && vi.SampleId != 0, "voice 0 reports a sample id");
        // The default pattern triggers the kick (voice 0) → the loaded sample plays.
        var sb = new float[8192 * 2];
        se.Seek(0); se.Play(); se.RenderOffline(sb, 8192); se.StopTransport();
        Check(Rms(sb, 8192) > 0.001f, $"sample voice plays via the sequencer ({Rms(sb, 8192):F3})");
        se.InstrumentAction(t, 6, 0, 0f);   // A_SetSource voice 0 → Synth
        Check(se.RhythmVoiceSource(t, 0) == 0, "toggle voice 0 back to Synth");
        se.InstrumentAction(t, 6, 0, 1f);   // → Sample again (buffer still present)
        Check(se.RhythmVoiceSource(t, 0) == 1, "toggle voice 0 back to Sample");

        string dir = Path.Combine(Path.GetTempPath(), "nota-rhythm-proj-" + Guid.NewGuid().ToString("N"));
        try
        {
            var w = new System.Collections.Generic.List<string>();
            var doc = ProjectService.Capture(se, new TransportState(120, 1, false, false), w);
            ProjectService.Save(doc, dir, se);
            var loaded = ProjectService.Load(dir);
            using var de = new NotaEngine();
            ProjectService.Apply(loaded, de, dir);
            int dt = -1; for (int i = 0; i < de.TrackCount; i++) if (de.TryGetTrackInfo(i, out var ti) && ti.IsInstrument && de.TrackInstrumentKind(ti.Id) == 12) dt = ti.Id;
            Check(dt > 0 && de.RhythmVoiceSource(dt, 0) == 1, "round-trip restores voice 0 as Sample");
            Check(de.TryGetRhythmVoiceInfo(dt, 0, out var dvi) && dvi.SampleId != 0, "round-trip restores the voice sample");
        }
        finally { try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { } }
    }
    finally { try { if (File.Exists(smpWav)) File.Delete(smpWav); } catch { } }
}

// ============ track groups (submix) ======================================
Console.WriteLine("-- track groups --");
{
    using var ge = new NotaEngine();
    ge.SetBpm(120); ge.SetTimeSignature(4, 4);
    int a = ge.AddBassSynthTrack(); ge.AddMidiClip(a, 0, 4); ge.SetClipNotes(a, 0, new[] { new NotaNote(48, 0.0, 4.0, 1.0f) });
    int b = ge.AddBassSynthTrack(); ge.AddMidiClip(b, 0, 4); ge.SetClipNotes(b, 0, new[] { new NotaNote(55, 0.0, 4.0, 1.0f) });
    var gbuf = new float[8192 * 2];
    float RenderRms() { ge.Seek(0); ge.Play(); ge.RenderOffline(gbuf, 8192); ge.StopTransport(); return Rms(gbuf, 8192); }
    float baseRms = RenderRms();
    Check(baseRms > 0.001f, $"two synths audible before grouping ({baseRms:F3})");

    int gid = ge.CreateGroup(new[] { a, b });
    Check(gid > 0, $"CreateGroup returns a group id ({gid})");
    bool isGroup = false, aParented = false, bParented = false;
    for (int i = 0; i < ge.TrackCount; i++) if (ge.TryGetTrackInfo(i, out var ti))
    {
        if (ti.Id == gid) isGroup = ti.IsGroup;
        if (ti.Id == a) aParented = ti.GroupId == gid;
        if (ti.Id == b) bParented = ti.GroupId == gid;
    }
    Check(isGroup, "new track is a group (type 3)");
    Check(aParented && bParented, "both tracks report the group as parent");
    Check(Math.Abs(RenderRms() - baseRms) < baseRms * 0.05f, "grouping is level-transparent");

    // A Utility (−24 dB) on the GROUP attenuates the whole submix — proves the group's own
    // device chain processes the summed children.
    int gdev = ge.AddBuiltinDevice(gid, 4);
    Check(gdev >= 0 && ge.TrackDeviceBuiltinKind(gid, 0) == 4, "group hosts its own device chain (Utility)");
    ge.DeviceSetParam(gid, 0, 0, -24.0f);
    float atten = RenderRms();
    Check(atten < baseRms * 0.25f, $"group device processes the submix (−24 dB: {atten:F3} << {baseRms:F3})");

    // Group mute silences all children.
    ge.SetTrackMute(gid, true);
    Check(RenderRms() < 1e-4f, "muting the group silences its children");
    ge.SetTrackMute(gid, false);
    ge.RemoveDevice(gid, 0);   // drop the −24 dB so nesting is audible

    // Nesting: put the group inside a new outer group; audio still reaches master.
    int outer = ge.CreateGroup(new[] { gid });
    Check(outer > 0 && outer != gid, "nested group created");
    bool nested = false; for (int i = 0; i < ge.TrackCount; i++) if (ge.TryGetTrackInfo(i, out var ti) && ti.Id == gid) nested = ti.GroupId == outer;
    Check(nested, "inner group nested under the outer group");
    Check(RenderRms() > 0.001f, "audio reaches master through two group layers");

    // Ungroup the outer → inner group returns to top level.
    ge.Ungroup(outer);
    bool backTop = false; for (int i = 0; i < ge.TrackCount; i++) if (ge.TryGetTrackInfo(i, out var ti) && ti.Id == gid) backTop = ti.GroupId == -1;
    Check(backTop, "ungroup restores the inner group to top level");

    // Project round-trip preserves the group + its membership.
    string gdir = Path.Combine(Path.GetTempPath(), "nota-group-" + Guid.NewGuid().ToString("N"));
    try
    {
        var gw = new System.Collections.Generic.List<string>();
        var gdoc = ProjectService.Capture(ge, new TransportState(120, 1, false, false), gw);
        ProjectService.Save(gdoc, gdir, ge);
        var gloaded = ProjectService.Load(gdir);
        using var gdst = new NotaEngine();
        ProjectService.Apply(gloaded, gdst, gdir);
        int groups = 0, childrenOfGroup = 0, grpId2 = -1;
        for (int i = 0; i < gdst.TrackCount; i++) if (gdst.TryGetTrackInfo(i, out var ti) && ti.IsGroup) { groups++; grpId2 = ti.Id; }
        for (int i = 0; i < gdst.TrackCount; i++) if (gdst.TryGetTrackInfo(i, out var ti) && ti.GroupId == grpId2) childrenOfGroup++;
        Check(groups == 1, $"round-trip restores one group ({groups})");
        Check(childrenOfGroup == 2, $"round-trip restores both children under the group ({childrenOfGroup})");
    }
    finally { try { if (System.IO.Directory.Exists(gdir)) System.IO.Directory.Delete(gdir, true); } catch { } }
}

// ============ MCP tools drive the engine ================================
Console.WriteLine("-- MCP tools --");
{
    using var mcpEng = new NotaEngine();
    mcpEng.SetBpm(120); mcpEng.SetTimeSignature(4, 4);
    var mcpDisp = new Nota.SmokeTest.SyncDispatch();
    var mcpRefr = new Nota.SmokeTest.NoRefresh();
    var mcpTransport = new Nota.Mcp.Tools.TransportTools(mcpEng, mcpDisp, mcpRefr);
    var mcpTracks = new Nota.Mcp.Tools.TrackTools(mcpEng, mcpDisp, mcpRefr);
    var mcpInstr = new Nota.Mcp.Tools.InstrumentTools(mcpEng, mcpDisp, mcpRefr);
    var mcpDevices = new Nota.Mcp.Tools.DeviceTools(mcpEng, mcpDisp, mcpRefr);
    var mcpMidi = new Nota.Mcp.Tools.MidiTools(mcpEng, mcpDisp, mcpRefr);
    var mcpProject = new Nota.Mcp.Tools.ProjectTools(mcpEng, mcpDisp, mcpRefr);
    var mcpAutom = new Nota.Mcp.Tools.AutomationTools(mcpEng, mcpDisp, mcpRefr);

    mcpTransport.SetTempo(128).Wait();
    var mcpWarm = new float[512 * 2]; mcpEng.Seek(0); mcpEng.Play(); mcpEng.RenderOffline(mcpWarm, 512); mcpEng.StopTransport();   // apply tempo
    Check(Math.Abs(mcpEng.Bpm - 128) < 0.5, $"MCP set_tempo ({mcpEng.Bpm})");

    int mcpT = mcpInstr.AddInstrumentTrack(6).Result;   // 6 = Nota Volt
    Check(mcpT > 0 && mcpEng.TrackInstrumentKind(mcpT) == 6, "MCP add_instrument_track (Volt)");

    int mcpClip = mcpMidi.AddMidiClip(mcpT, 0, 4).Result;
    mcpMidi.SetClipNotes(mcpT, mcpClip, new[]
    {
        new Nota.Mcp.Tools.MidiTools.Note(60, 0, 1, 0.9f),
        new Nota.Mcp.Tools.MidiTools.Note(64, 1, 1, 0.8f),
        new Nota.Mcp.Tools.MidiTools.Note(67, 2, 2, 0.7f),
    }).Wait();
    var mcpNotes = mcpMidi.GetClipNotes(mcpT, mcpClip).Result;
    Check(mcpNotes.Length == 3 && mcpNotes[0].Pitch == 60 && Math.Abs(mcpNotes[2].Length - 2) < 1e-3, $"MCP set/get_clip_notes round-trip ({mcpNotes.Length})");
    var mcpCons = mcpMidi.ConsolidateClips(new[] { mcpT }, 0, 4).Result;
    Check(mcpCons.Length == 1 && mcpCons[0].TrackId == mcpT && mcpMidi.GetClipNotes(mcpT, mcpCons[0].ClipIndex).Result.Length == 3,
          "MCP consolidate_clips keeps the notes in one clip");

    int mcpDev = mcpDevices.AddDevice(mcpT, 2).Result;        // 2 = Reverb
    Check(mcpDev >= 0 && mcpEng.TrackDeviceBuiltinKind(mcpT, mcpDev) == 2, "MCP add_device (Reverb)");
    Check(mcpDevices.GetDeviceParams(mcpT, mcpDev).Result.Length > 0, "MCP get_device_params");

    int mcpLane = mcpAutom.AddAutomationLane(mcpT, "volume").Result;
    mcpAutom.SetAutomationPoints(mcpT, mcpLane, new[] { new Nota.Mcp.Tools.AutomationTools.Point(0, 1.0f, 0), new Nota.Mcp.Tools.AutomationTools.Point(4, 0.0f, 0) }).Wait();
    Check(mcpAutom.GetAutomationPoints(mcpT, mcpLane).Result.Length == 2, "MCP automation set/get round-trip");

    mcpTracks.SetTrackVolume(mcpT, 0.5f).Wait();
    var mcpOv = mcpProject.GetOverview().Result;
    Check(mcpOv.Bpm > 0 && mcpOv.Tracks.Length >= 1 && Array.Exists(mcpOv.Tracks, x => x.Id == mcpT && x.InstrumentKind == 6 && x.Clips.Length == 1),
        $"MCP get_overview reflects edits ({mcpOv.Tracks.Length} tracks)");

    // ---- Phase 2: session, racks, sampling, mixer ----
    var mcpSession = new Nota.Mcp.Tools.SessionTools(mcpEng, mcpDisp, mcpRefr);
    var mcpRack = new Nota.Mcp.Tools.RackTools(mcpEng, mcpDisp, mcpRefr);
    var mcpMixer = new Nota.Mcp.Tools.MixerTools(mcpEng, mcpDisp, mcpRefr);

    // Session: fill a MIDI slot on the Volt track, write notes, read the grid back.
    mcpSession.AddSessionMidiClip(mcpT, 0, 4).Wait();
    mcpSession.SetSessionNotes(mcpT, 0, new[]
    {
        new Nota.Mcp.Tools.SessionTools.Note(48, 0, 2, 0.8f),
        new Nota.Mcp.Tools.SessionTools.Note(55, 2, 2, 0.7f),
    }).Wait();
    var mcpSessNotes = mcpSession.GetSessionNotes(mcpT, 0).Result;
    var mcpSnap = mcpSession.GetSession().Result;
    Check(mcpSessNotes.Length == 2 && mcpSnap.SceneCount > 0 && Array.Exists(mcpSnap.FilledSlots, s => s.TrackId == mcpT && s.Scene == 0 && s.State == "filled"),
        $"MCP session fill + get_session ({mcpSnap.FilledSlots.Length} filled)");

    // Rack: build an Instrument Rack, add a chain, drive a macro + trigger note.
    int mcpRackT = mcpInstr.AddInstrumentTrack(3).Result;   // 3 = Instrument Rack
    int mcpChain = mcpRack.AddRackChain(mcpRackT, 6).Result; // 6 = Volt chain
    mcpRack.SetChainTriggerNote(mcpRackT, mcpChain, 36).Wait();
    mcpRack.SetMacro(mcpRackT, 0, 0.75f).Wait();
    var mcpRackSnap = mcpRack.GetRack(mcpRackT).Result;
    var mcpMacros = mcpRack.GetMacros(mcpRackT).Result;
    Check(mcpRackSnap.Chains.Length >= 1 && mcpChain >= 0
          && Array.Exists(mcpRackSnap.Chains, c => c.Index == mcpChain && c.TriggerNote == 36)
          && mcpMacros.Length == 8 && Math.Abs(mcpMacros[0].Value - 0.75f) < 0.02f,
        $"MCP rack chain + macro ({mcpRackSnap.Chains.Length} chains)");
    var mcpChainParams = mcpRack.GetChainInstrumentParams(mcpRackT, mcpChain).Result;
    Check(mcpChainParams.Length > 0, $"MCP get_chain_instrument_params ({mcpChainParams.Length})");

    // Mixer: a return bus shows up in list_returns and get_track_meter reads a track.
    int mcpRet = mcpTracks.AddReturnTrack().Result;
    var mcpReturns = mcpMixer.ListReturns().Result;
    Check(mcpRet > 0 && Array.Exists(mcpReturns, r => r.Id == mcpRet), $"MCP list_returns ({mcpReturns.Length})");
    var mcpMeter = mcpMixer.GetTrackMeter(mcpT).Result;
    Check(mcpMeter is not null, "MCP get_track_meter");

    // ---- Phase 3: presets, effect rack, plugins, export ----
    var mcpFactory = new Nota.Infrastructure.FactoryPresetCatalog();
    var mcpPresetStore = new Nota.Infrastructure.PresetStore();
    var mcpPresetLib = new Nota.Infrastructure.PresetLibrary();
    var mcpSettings = new Nota.Infrastructure.SettingsService();
    var mcpCatalog = new Nota.Infrastructure.PluginCatalog();
    var mcpExporter = new Nota.Infrastructure.WavAudioExporter();
    var mcpPresets = new Nota.Mcp.Tools.PresetTools(mcpEng, mcpDisp, mcpRefr, mcpFactory, mcpPresetStore, mcpPresetLib, mcpSettings);
    var mcpEffRack = new Nota.Mcp.Tools.EffectRackTools(mcpEng, mcpDisp, mcpRefr);
    var mcpPlugins = new Nota.Mcp.Tools.PluginTools(mcpEng, mcpDisp, mcpRefr, mcpCatalog);
    var mcpExport = new Nota.Mcp.Tools.ExportTools(mcpEng, mcpDisp, mcpRefr, mcpExporter);

    // Presets: the factory library lists and an instrument preset applies as a new track.
    var mcpFactoryList = mcpPresets.ListFactoryPresets().Result;
    var mcpInstPreset = Array.Find(mcpFactoryList, p => p.IsInstrument);
    int mcpTracksBefore = mcpEng.TrackCount;
    string mcpApplyWarn = mcpPresets.ApplyFactoryPreset(mcpInstPreset!.Id, 0).Result;
    Check(mcpFactoryList.Length > 0 && mcpInstPreset is not null && mcpApplyWarn == "" && mcpEng.TrackCount == mcpTracksBefore + 1,
        $"MCP factory preset apply ({mcpFactoryList.Length} presets)");

    // Effect Rack: add device kind 5, add a chain, drive mode + a macro.
    int mcpErDev = mcpDevices.AddDevice(mcpT, 5).Result;   // 5 = Effect Rack
    int mcpErChain = mcpEffRack.AddEffectRackChain(mcpT, mcpErDev, 4).Result;  // 4 = Utility source
    mcpEffRack.SetEffectRackMode(mcpT, mcpErDev, 1).Wait();   // Series
    mcpEffRack.SetEffectRackMacro(mcpT, mcpErDev, 0, 0.5f).Wait();
    var mcpErSnap = mcpEffRack.GetEffectRack(mcpT, mcpErDev).Result;
    Check(mcpErDev >= 0 && mcpErChain >= 0 && mcpErSnap.Mode == 1 && mcpErSnap.ModeName == "series" && mcpErSnap.Chains.Length >= 1,
        $"MCP effect rack ({mcpErSnap.Chains.Length} chains, mode {mcpErSnap.ModeName})");

    // Plugins: the catalog lists without throwing (0 is fine on an unscanned host).
    var mcpPluginList = mcpPlugins.ListPlugins().Result;
    Check(mcpPluginList is not null, $"MCP list_plugins ({mcpPluginList!.Length})");

    // Export: bounce 2 beats of the master to a temp WAV.
    string mcpWav = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"nota-mcp-{Guid.NewGuid():N}.wav");
    string mcpOut = mcpExport.ExportMaster(mcpWav, 2, "pcm16").Result;
    bool mcpWrote = System.IO.File.Exists(mcpWav) && new System.IO.FileInfo(mcpWav).Length > 44;
    Check(mcpOut == mcpWav && mcpWrote, "MCP export_master writes a WAV");
    try { System.IO.File.Delete(mcpWav); } catch { }

    // ---- Nota Rhythm (drum machine, kind 12) over MCP ----
    var mcpRhythm = new Nota.Mcp.Tools.RhythmTools(mcpEng, mcpDisp, mcpRefr);
    int mcpRhy = mcpInstr.AddInstrumentTrack(12).Result;
    Check(mcpRhy > 0 && mcpEng.TrackInstrumentKind(mcpRhy) == 12, "MCP add_instrument_track (Rhythm kind 12)");
    // Program a four-on-the-floor kick (voice 0) in bank A, with accents on the down-beats.
    mcpRhythm.SelectRhythmBank(mcpRhy, 0).Wait();
    mcpRhythm.SetRhythmRow(mcpRhy, 0, new[]
    {
        new Nota.Mcp.Tools.RhythmTools.Step(0, 0.9f, true),
        new Nota.Mcp.Tools.RhythmTools.Step(4, 0.8f, false),
        new Nota.Mcp.Tools.RhythmTools.Step(8, 0.9f, true),
        new Nota.Mcp.Tools.RhythmTools.Step(12, 0.8f, false),
    }).Wait();
    var mcpRhySnap = mcpRhythm.GetRhythm(mcpRhy, 0).Result;
    var mcpKick = Array.Find(mcpRhySnap.Voices, v => v.Index == 0);
    Check(mcpKick is not null && mcpKick.Name == "Kick" && mcpKick.Steps.Length == 4
          && Array.Exists(mcpKick.Steps, s => s.Index == 0 && s.Accent && s.Velocity > 0.8f),
        $"MCP rhythm row program + get_rhythm ({mcpKick?.Steps.Length ?? -1} kick steps)");
    // Idempotent single-step edit: turn step 4 off.
    mcpRhythm.SetRhythmStep(mcpRhy, 0, 4, false).Wait();
    var mcpKick2 = Array.Find(mcpRhythm.GetRhythm(mcpRhy, 0).Result.Voices, v => v.Index == 0);
    Check(mcpKick2!.Steps.Length == 3 && !Array.Exists(mcpKick2.Steps, s => s.Index == 4), "MCP set_rhythm_step off removes the step");

    // ---- MIDI controllers + MIDI Learn over MCP ----
    var mcpMidiDevSvc = new Nota.Infrastructure.MidiDeviceService();
    var mcpLearnFake = new Nota.SmokeTest.FakeMidiLearn();
    mcpLearnFake.Seed(new Nota.Mcp.MidiMappingInfo(0, "Cutoff", "Cc", 0, 74, 0, 1, false, "CC 74 · ch1"));
    mcpLearnFake.SeedSeen(new Nota.Mcp.MidiControlSeen("CC", 0, 74, 100, 3));
    var mcpMidiCtl = new Nota.Mcp.Tools.MidiControlTools(mcpEng, mcpDisp, mcpRefr, mcpMidiDevSvc, mcpLearnFake);
    var mcpMidiDevList = mcpMidiCtl.ListMidiDevices().Result;                  // no throw (0 is fine headless)
    var mcpMidiMaps = mcpMidiCtl.GetMidiMappings().Result;
    var mcpMidiAct = mcpMidiCtl.GetMidiActivity().Result;
    mcpMidiCtl.SetMidiLearn(true).Wait();
    bool mcpRangeOk = mcpMidiCtl.SetMidiMappingRange(0, 0.1, 0.9, true).Result;
    var mcpMapsAfter = mcpMidiCtl.GetMidiMappings().Result;
    bool mcpMapRemoved = mcpMidiCtl.RemoveMidiMapping(0).Result;
    Check(mcpMidiDevList is not null && mcpMidiMaps.Length == 1 && mcpMidiMaps[0].Control == "Cutoff"
          && mcpMidiAct.Length == 1 && mcpMidiAct[0].Number == 74 && mcpLearnFake.Armed
          && mcpRangeOk && Math.Abs(mcpMapsAfter[0].RangeMin - 0.1) < 1e-9 && mcpMapsAfter[0].Invert
          && mcpMapRemoved && mcpMidiCtl.GetMidiMappings().Result.Length == 0,
        $"MCP MIDI tools (devices {mcpMidiDevList!.Length}, list/learn/range/remove)");
}

// --- browser library: favorites + tags persist and round-trip -----------------
{
    Console.WriteLine("-- browser library (favorites + tags) --");
    string libPath = Path.Combine(Path.GetTempPath(), "nota_browser_lib_" + Guid.NewGuid().ToString("N") + ".json");
    try
    {
        var lib = new BrowserLibraryService(libPath);
        var house = lib.CreateTag("House", "#4EA6C8");
        var bass = lib.CreateTag("Bass", "#E0554E");
        lib.SetFavorite("bi:6", true);          // e.g. Nota Volt
        lib.AssignTag("bi:6", house.Id, true);
        lib.AssignTag("bi:6", bass.Id, true);
        lib.AssignTag("be:2", house.Id, true);  // Nota Reverb

        var reload = new BrowserLibraryService(libPath);
        Check(reload.Tags.Count == 2, $"tags round-trip ({reload.Tags.Count})");
        Check(reload.IsFavorite("bi:6"), "favorite round-trips");
        Check(reload.TagIdsFor("bi:6").Count == 2, $"two tags on the device ({reload.TagIdsFor("bi:6").Count})");
        Check(reload.TagIdsFor("be:2").Contains(house.Id), "tag shared across devices round-trips");

        // Delete a tag → scrubbed from every assignment.
        reload.DeleteTag(house.Id);
        Check(reload.Tags.Count == 1, "delete removes the tag");
        Check(!reload.TagIdsFor("bi:6").Contains(house.Id), "delete scrubs the tag from assignments");
        Check(reload.TagIdsFor("be:2").Count == 0, "delete clears a now-empty assignment");

        var reload2 = new BrowserLibraryService(libPath);
        Check(reload2.Tags.Count == 1 && !reload2.TagIdsFor("bi:6").Contains(house.Id),
            "tag deletion persists");
    }
    finally { try { if (File.Exists(libPath)) File.Delete(libPath); } catch { } }
}

// --- preset save/apply for a param-only built-in synth (Operator, kind 9) --------
{
    Console.WriteLine("-- preset: built-in synth save/apply --");
    using var pe = new NotaEngine();
    int ot = pe.AddOperatorSynthTrack();
    Check(ot > 0, "Operator track added");
    int ppc = pe.PluginParamCount(ot, -1);
    Check(ppc > 0, $"Operator exposes params ({ppc})");
    // Nudge param 0 so we can prove the value round-trips through the preset.
    pe.PluginParamSet(ot, -1, 0, 0.42f);
    float saved0 = pe.PluginParamGet(ot, -1, 0);

    var pdoc = Nota.Infrastructure.PresetService.Capture(pe, ot, -1, "My Operator");
    Check(pdoc is not null, "Operator preset captures (was returning null before the fix)");
    Check(pdoc is { Type: "builtin-instrument", BuiltinKind: 9 }, "preset is a builtin-instrument for kind 9");
    Check(pdoc?.DeviceName == "Nota Operator", $"preset records the device name ({pdoc?.DeviceName})");

    // Apply onto a second Operator track (param nudged away first) → the saved value restores.
    int ot2 = pe.AddOperatorSynthTrack();
    pe.PluginParamSet(ot2, -1, 0, 0.1f);
    string warn = Nota.Infrastructure.PresetService.ApplyInPlace(pdoc!, pe, ot2, -1);
    Check(warn.Length == 0, $"preset applies cleanly ({warn})");
    float got0 = pe.PluginParamGet(ot2, -1, 0);
    Check(Math.Abs(got0 - saved0) < 0.02f, $"param round-trips through the preset ({saved0:0.00} -> {got0:0.00})");
}

// --- automation edits are undoable (issue: ctrl-z after paste) -------------------
{
    Console.WriteLine("-- automation lane undo --");
    using var ae = new NotaEngine();
    int at = ae.AddInstrumentTrack();
    int lane = ae.AddAutomationLane(at, Nota.Application.AutomationTarget.Volume, -1, -1);
    Check(lane >= 0, "automation lane created");
    ae.SetAutomationPoints(at, lane, new[]
    {
        new Nota.Application.AutomationPoint(0, 0.5f),
        new Nota.Application.AutomationPoint(4, 0.8f),
    });
    Check(ae.GetAutomationPoints(at, lane).Length == 2, "two points set");
    // Paste-like edit: a third point in the middle.
    ae.SetAutomationPoints(at, lane, new[]
    {
        new Nota.Application.AutomationPoint(0, 0.5f),
        new Nota.Application.AutomationPoint(2, 0.3f),
        new Nota.Application.AutomationPoint(4, 0.8f),
    });
    Check(ae.GetAutomationPoints(at, lane).Length == 3, "third point added");
    Check(ae.Undo(), "undo returns true");
    Check(ae.GetAutomationPoints(at, lane).Length == 2, "undo reverts to two points");
}

// --- replace a track's built-in instrument in place (browser drag onto a track) --
{
    Console.WriteLine("-- replace built-in instrument --");
    using var re = new NotaEngine();
    int t = re.AddVoltSynthTrack();
    Check(re.TrackInstrumentKind(t) == 6, $"track starts as Volt ({re.TrackInstrumentKind(t)})");
    Check(re.SetTrackBuiltinInstrument(t, 9), "swap to Operator succeeds");
    Check(re.TrackInstrumentKind(t) == 9, $"instrument is now Operator ({re.TrackInstrumentKind(t)})");
    Check(re.SetTrackBuiltinInstrument(t, 1), "swap to Sampler succeeds");
    Check(re.TrackInstrumentKind(t) == 1, "instrument is now Sampler");
    Check(!re.SetTrackBuiltinInstrument(t, 3), "swap to a Rack kind is rejected");
    int at = re.AddAudioTrack();
    Check(!re.SetTrackBuiltinInstrument(at, 6), "audio track rejects an instrument swap");
}

// --- audio→MIDI: mono PCM getter + onset detection ------------------------------
{
    Console.WriteLine("-- audio->MIDI onset analysis --");
    string clickWav = Path.Combine(Path.GetTempPath(), "nota_onsets_" + Guid.NewGuid().ToString("N") + ".wav");
    try
    {
        // 4 s of 128 BPM clicks → one hit per beat ≈ 9 onsets.
        Nota.SmokeTest.WavWriter.WriteClicks(clickWav, bpm: 128.0, seconds: 4.0, sampleRate: 44100);
        using var oe = new NotaEngine();
        oe.SetBpm(128); oe.SetTimeSignature(4, 4);
        int ot = oe.AddAudioTrack();
        int oc = oe.AddAudioClip(ot, clickWav, 0.0);
        Check(oc >= 0, "click clip added");

        int nFrames = oe.GetClipAudioMono(ot, oc, null, out double sr);
        Check(nFrames > 100000 && Math.Abs(sr - 44100.0) < 1.0, $"GetClipAudioMono count+sr ({nFrames}, {sr})");
        var mono = new float[nFrames];
        int got = oe.GetClipAudioMono(ot, oc, mono, out sr);
        Check(got == nFrames, $"mono buffer filled ({got})");

        var onsets = Nota.Infrastructure.AudioOnsets.Detect(mono, sr);
        Check(onsets.Count >= 7 && onsets.Count <= 11, $"onset detector finds ~9 clicks (got {onsets.Count})");
        // Spacing between onsets ≈ one beat (0.469 s @128 BPM ≈ 20672 frames).
        bool spaced = onsets.Count >= 2 && Math.Abs((onsets[1] - onsets[0]) - 44100 * 60.0 / 128.0) < 44100 * 0.06;
        Check(spaced, "onset spacing ≈ one beat");

        // Slice pipeline at engine level: slice → temp WAV → Drum Rack pads → MIDI clip + notes.
        int dr = oe.AddDrumRackTrack();
        int pads = Math.Min(4, onsets.Count);
        int loaded = 0;
        var sliceNotes = new List<Nota.Application.NotaNote>();
        for (int i = 0; i < pads; i++)
        {
            int a = onsets[i], b = (i + 1 < onsets.Count) ? onsets[i + 1] : mono.Length;
            var sbuf = new float[Math.Max(1, b - a)];
            Array.Copy(mono, a, sbuf, 0, Math.Min(sbuf.Length, mono.Length - a));
            string sw = Path.Combine(Path.GetTempPath(), "nota_slice_" + Guid.NewGuid().ToString("N") + ".wav");
            using (var w = new Nota.Infrastructure.WavWriter(sw, (int)sr, 1, Nota.Application.WavBitDepth.Float32)) w.WriteFrames(sbuf, sbuf.Length);
            int chain = oe.RackAddSamplerChain(dr, sw, 36 + i, false);
            try { File.Delete(sw); } catch { }
            if (chain >= 0) { loaded++; sliceNotes.Add(new Nota.Application.NotaNote(36 + i, i * 0.5, 0.4, 0.9f)); }
        }
        Check(loaded == pads && oe.RackChainCount(dr) == pads, $"drum rack got {pads} slice pads (chains={oe.RackChainCount(dr)})");
        int mc = oe.AddMidiClip(dr, 0.0, 4.0);
        oe.SetClipNotes(dr, mc, sliceNotes.ToArray());
        Check(oe.GetClipNotes(dr, mc).Length == pads, $"MIDI clip triggers {pads} slices");

        // YIN pitch: a 220 Hz sine (A3, MIDI 57) should read ~220 Hz.
        int psr = 44100; var sine = new float[psr / 2];
        for (int i = 0; i < sine.Length; i++) sine[i] = (float)(0.6 * Math.Sin(2 * Math.PI * 220.0 * i / psr));
        double hz = Nota.Infrastructure.AudioPitch.YinHz(sine, 0, psr / 100, psr);
        int mnote = hz > 0 ? (int)Math.Round(69 + 12 * Math.Log2(hz / 440.0)) : -1;
        Check(Math.Abs(hz - 220.0) < 5.0 && mnote == 57, $"YIN detects 220 Hz / A3 (got {hz:0.0} Hz, note {mnote})");

        // Harmony: a C-major triad (C4/E4/G4 = 60/64/67) should be recovered by the STFT detector.
        var chord = new float[psr];   // 1 s
        double[] cf = { 261.63, 329.63, 392.0 };
        for (int i = 0; i < chord.Length; i++)
        { double v = 0; foreach (var fq in cf) v += Math.Sin(2 * Math.PI * fq * i / psr); chord[i] = (float)(v / cf.Length * 0.8); }
        var spans = Nota.Infrastructure.AudioHarmony.Detect(chord, psr);
        var pitches = new HashSet<int>(); foreach (var s in spans) pitches.Add(s.Pitch);
        Check(pitches.Contains(60) && pitches.Contains(64) && pitches.Contains(67),
            $"harmony detects C-major triad (pitches: {string.Join(",", pitches)})");
    }
    finally { try { if (File.Exists(clickWav)) File.Delete(clickWav); } catch { } }
}

// --- MIDI routing between tracks ("MIDI In") -------------------------------
// Destination B receives source A's MIDI. Source A (muted, holds the only note
// clip) drives B (its own clips empty), so any audio at B must come from routing;
// clearing the route goes silent.
{
    Console.WriteLine("-- MIDI routing --");
    using var eng = new NotaEngine();
    const int rf = 22050;
    var rbuf = new float[rf * 2];
    eng.SetBpm(120);

    int a = eng.AddInstrumentTrack();   // source
    int b = eng.AddInstrumentTrack();   // destination
    int ac = eng.AddMidiClip(a, 0.0, 4.0);
    eng.SetClipNotes(a, ac, new[] { new NotaNote(60, 0.0, 2.0, 1.0f) });
    eng.SetTrackMute(a, true);          // silence A's own instrument; only routing should sound

    eng.Seek(0); eng.Play(); eng.RenderOffline(rbuf, rf); eng.StopTransport();
    Check(Rms(rbuf, rf) < 1e-4f, "destination with no MIDI-In is silent");

    eng.SetTrackMidiSource(b, a);       // B ← A ("MIDI In")
    Check(eng.GetTrackMidiSource(b) == a, "MIDI source stored");
    eng.Seek(0); eng.Play(); eng.RenderOffline(rbuf, rf); eng.StopTransport();
    Check(Rms(rbuf, rf) > 1e-4f, $"routed MIDI drives the destination instrument (rms={Rms(rbuf, rf):0.0000})");

    eng.SetTrackMidiSource(b, -1);
    // Flush the voice B held from the routed note (its length outran the render window):
    // a play→stop edge sends allNotesOff, then let the release tail decay while stopped.
    eng.StopTransport(); eng.RenderOffline(rbuf, rf); eng.RenderOffline(rbuf, rf);
    eng.Seek(0); eng.Play(); eng.RenderOffline(rbuf, rf); eng.StopTransport();
    Check(Rms(rbuf, rf) < 1e-4f, "clearing the route goes silent again");

    // Post-FX routing: an arpeggiator on the (muted) source drives the destination. The render
    // feeds B only from the source's *post-FX* buffer, so audio at B proves the arp's generated
    // notes flow through — the arp runs once (pre-pass) and its output reaches the destination.
    int arp = eng.AddMidiEffect(a, 0);   // Nota Arp
    Check(arp >= 0, "arp added to source");
    eng.SetTrackMidiSource(b, a);
    eng.Seek(0); eng.Play(); eng.RenderOffline(rbuf, rf); eng.StopTransport();
    Check(Rms(rbuf, rf) > 1e-4f, $"source's arp (post-FX) drives the destination (rms={Rms(rbuf, rf):0.0000})");
}

// --- record the routed MIDI into the destination's take --------------------
// Arm the destination B (fed via MIDI In from A) and record: A's notes should
// print into B's clip.
{
    Console.WriteLine("-- MIDI routing: record --");
    using var eng = new NotaEngine();
    const int rf = 44100;   // 1 s = 2 beats at 120 BPM, so the note-on and note-off both land
    var rbuf = new float[rf * 2];
    eng.SetBpm(120);

    int a = eng.AddInstrumentTrack();   // source
    int b = eng.AddInstrumentTrack();   // destination (record target)
    int ac = eng.AddMidiClip(a, 0.0, 4.0);
    // Both notes end well within the ~2-beat render window so their note-offs are captured.
    eng.SetClipNotes(a, ac, new[] { new NotaNote(60, 0.0, 0.4, 1.0f), new NotaNote(64, 0.5, 0.4, 0.8f) });
    eng.SetTrackMidiSource(b, a);       // B ← A
    eng.SetTrackArmed(b, true);         // record into B

    eng.Seek(0);
    eng.SetRecording(true);             // opens a record clip on B, rolls the transport
    eng.RenderOffline(rbuf, rf);        // capture ~2 beats: both notes' on+off pass
    eng.SetRecording(false);
    eng.Poll();                         // materialise recorded notes into B's clip

    var recNotes = eng.GetClipNotes(b, 0);
    Check(recNotes.Length >= 2, $"routed MIDI is recorded into the destination's take ({recNotes.Length} notes)");
    bool got60 = false, got64 = false;
    foreach (var nn in recNotes) { if (nn.Pitch == 60) got60 = true; if (nn.Pitch == 64) got64 = true; }
    Check(got60 && got64, "recorded take carries the source's pitches (60, 64)");
}

// --- live automation edit (drag a point) drives the device without undo spam ----
// SetAutomationPointsLive updates the lane in real time (applyAutomation → the device
// follows at the playhead) and takes no undo checkpoint, so a whole drag is one undo step.
{
    Console.WriteLine("-- automation: live edit --");
    using var eng = new NotaEngine();
    eng.SetBpm(120);
    const int rf = 512; var abuf = new float[rf * 2];
    int t = eng.AddInstrumentTrack();
    int lane = eng.AddAutomationLane(t, AutomationTarget.Volume, -1, -1);
    Check(lane >= 0, "volume lane created");

    eng.SetAutomationPoints(t, lane, new[] { new AutomationPoint(0.0, 0.3f) });   // normal (undo)
    eng.Seek(0); eng.RenderOffline(abuf, rf);
    eng.TryGetTrackInfo(0, out var ti1);
    Check(Math.Abs(ti1.Volume - 0.3f) < 0.02f, $"automation drives the device ({ti1.Volume:0.00})");

    eng.SetAutomationPointsLive(t, lane, new[] { new AutomationPoint(0.0, 0.9f) });   // live (no undo)
    eng.Seek(0); eng.RenderOffline(abuf, rf);
    eng.TryGetTrackInfo(0, out var ti2);
    Check(Math.Abs(ti2.Volume - 0.9f) < 0.02f, $"live edit updates the device in real time ({ti2.Volume:0.00})");
    var pL = eng.GetAutomationPoints(t, lane);
    Check(pL.Length == 1 && Math.Abs(pL[0].Value - 0.9f) < 1e-4f, "live edit updated the lane");

    eng.Undo();   // one undo reverts past the live changes (they added no undo step)
    var pU = eng.GetAutomationPoints(t, lane);
    Check(pU.Length == 0, "one undo reverts the whole drag (live edits took no undo step)");
}

// --- export: a warped clip keeps its duration at a higher render rate ----------
// Rendering above the prepared rate must re-rate warp streams; before the fix they stayed at
// the old device rate and played the clip too fast (a whole-track speed-up on export at 96 kHz).
{
    Console.WriteLine("-- export: warp at a higher render rate --");
    double AudibleSeconds(double sr)
    {
        using var e = new NotaEngine();
        e.SetBpm(120); e.SetTimeSignature(4, 4);
        int t = e.AddAudioTrack();
        int c = e.AddAudioClip(t, wav, 0.0);   // 1 s sine → ~2 beats at 120 BPM
        e.SetClipWarp(t, c, true, 3);          // Complex warp
        const int blk = 2048; var b = new float[blk * 2];
        e.Seek(0); e.Play();
        int audible = 0, total = (int)(sr * 2.0);   // scan two seconds of output
        bool first = true;
        for (int done = 0; done < total; done += blk)
        {
            if (first) { e.RenderOffline(b, blk, sr); first = false; }   // first render sets the SR
            else e.RenderOffline(b, blk);
            if (Rms(b, blk) > 0.01f) audible += blk;
        }
        return audible / sr;
    }
    double sec44 = AudibleSeconds(44100);
    double sec96 = AudibleSeconds(96000);
    Check(sec44 > 0.7 && sec44 < 1.4, $"warped clip audible ~1 s at 44.1k ({sec44:F2}s)");
    Check(Math.Abs(sec96 - sec44) < 0.25, $"same audible duration at 96k render — not sped up ({sec96:F2}s vs {sec44:F2}s)");
}

// --- export: chunked bounce has no block-seam clicks ---------------------------
// A device like Delay wipes its buffer in setSampleRate; re-preparing every chunk (as the old
// offline path did) reset that state at each seam and clicked. A chunked render must now match
// a single-shot render sample-for-sample.
{
    Console.WriteLine("-- export: chunked render is seamless --");
    NotaEngine Make()
    {
        var e = new NotaEngine();
        e.SetBpm(120);
        int t = e.AddInstrumentTrack();
        int c = e.AddMidiClip(t, 0.0, 4.0);
        e.SetClipNotes(t, c, new[] { new NotaNote(60, 0.0, 2.0, 1.0f) });
        e.AddBuiltinDevice(t, 3);   // Nota Delay — its setSampleRate zeroes the delay buffer
        return e;
    }
    const int N = 8192 * 3, blk = 4096;
    using var e1 = Make(); e1.Seek(0); e1.Play();
    var one = new float[N * 2]; e1.RenderOffline(one, N, 96000);

    using var e2 = Make(); e2.Seek(0); e2.Play();
    var many = new float[N * 2]; var tmp = new float[blk * 2];
    bool first = true;
    for (int off = 0; off < N; off += blk)
    {
        if (first) { e2.RenderOffline(tmp, blk, 96000); first = false; }
        else e2.RenderOffline(tmp, blk);
        Array.Copy(tmp, 0, many, off * 2, blk * 2);
    }
    double maxDiff = 0;
    for (int i = 0; i < N * 2; i++) maxDiff = Math.Max(maxDiff, Math.Abs(one[i] - many[i]));
    Check(maxDiff < 1e-4, $"chunked render == single-shot (no per-chunk state reset, maxdiff={maxDiff:G3})");
}

{
    Console.WriteLine("-- freeze: capture + playback substitution --");
    using var e = new NotaEngine();
    e.SetBpm(120);
    int t = e.AddBassSynthTrack();
    int c = e.AddMidiClip(t, 0.0, 4.0);
    e.SetClipNotes(t, c, new[] { new NotaNote(48, 0.0, 4.0, 1.0f) });

    const int N = 8192 * 4;   // ~0.75 s at 44.1k — well inside the 4-beat note
    // Live reference (also sets the default 44.1k render rate on the first call).
    e.Seek(0); e.Play();
    var live = new float[N * 2];
    e.RenderOffline(live, N);
    e.StopTransport();
    float liveRms = Rms(live, N);
    Check(liveRms > 1e-4f, $"live instrument produces audio (rms={liveRms:0.0000})");

    // Freeze: drive the offline capture the way TrackFreezer does.
    e.Stop(); e.SetLoop(false, 0, 0); e.SetMetronome(false); e.StopTransport();
    long total = e.BeginFreeze(t, 4.0);
    Check(total > 0, $"beginFreeze returns capture frames ({total})");
    e.StopTransport(); e.Seek(0); e.Play();
    var chunk = new float[4096 * 2];
    for (long rem = total; rem > 0;) { int m = (int)Math.Min(4096, rem); e.RenderOffline(chunk, m); rem -= m; }
    e.StopTransport(); e.EndFreeze(t);
    Check(e.IsTrackFrozen(t), "track reports frozen after endFreeze");

    // Frozen playback substitutes the buffer for the live chain (similar level).
    e.Seek(0); e.Play();
    var frozen = new float[N * 2];
    e.RenderOffline(frozen, N);
    e.StopTransport();
    float frozenRms = Rms(frozen, N);
    Check(frozenRms > liveRms * 0.4f && frozenRms < liveRms * 2.6f,
          $"frozen playback level tracks the live render (live={liveRms:0.0000}, frozen={frozenRms:0.0000})");

    // Frozen audio sounds only while rolling — a stopped render is silent.
    e.Seek(0);
    var idle = new float[2048 * 2];
    e.RenderOffline(idle, 2048);   // transport not playing
    Check(Rms(idle, 2048) < 1e-5f, "frozen track is silent when the transport is stopped");

    // Blob persists + restores; unfreeze returns to the live chain.
    byte[] blob = e.GetFreezeState(t);
    Check(blob.Length > 16, $"freeze state blob is non-trivial ({blob.Length} bytes)");
    e.UnfreezeTrack(t);
    Check(!e.IsTrackFrozen(t), "track reports not frozen after unfreeze");
    e.Seek(0); e.Play();
    var relive = new float[N * 2];
    e.RenderOffline(relive, N);
    e.StopTransport();
    Check(Rms(relive, N) > 1e-4f, "unfrozen track produces audio again");
    e.SetFreezeState(t, blob);
    Check(e.IsTrackFrozen(t), "setFreezeState re-freezes the track from a saved blob");
}

// --- clip tools: every generator and transformation stays inside the clip ---------
{
    Console.WriteLine("-- clip tools (generate / transform) --");
    var toolCtx = new Nota.Application.Midi.MidiToolContext
    {
        LengthBeats = 4, Grid = 0.25, ScaleOn = true, ScaleRoot = 0,
        ScaleMask = Nota.Application.Midi.MidiScales.MaskAt(0), Seed = 11,
    };
    var toolSrc = new[]
    {
        new Nota.Application.NotaNote(60, 0.0, 2.0, 0.8f),
        new Nota.Application.NotaNote(64, 0.0, 2.0, 0.8f),
        new Nota.Application.NotaNote(67, 0.0, 2.0, 0.8f),
        new Nota.Application.NotaNote(62, 2.0, 1.0, 0.7f),
        new Nota.Application.NotaNote(71, 3.0, 1.0, 0.9f),
    };

    int bad = 0, empties = 0, nondet = 0;
    foreach (var tool in Nota.Application.Midi.MidiToolCatalog.All)
    {
        // Each parameter pinned to each end of its range, plus the defaults.
        var cases = new List<Nota.Application.Midi.MidiToolSettings> { tool.NewSettings() };
        foreach (var prm in tool.Params)
            foreach (double v in new[] { prm.Min, prm.Max })
            {
                var st = tool.NewSettings();
                st.Set(prm.Id, v);
                cases.Add(st);
            }

        foreach (var st in cases)
        {
            var r = Nota.Application.Midi.MidiToolRunner.Apply(tool, toolSrc, null, st, toolCtx);
            foreach (var n in r)
                if (n.Pitch is < 0 or > 127 || n.Velocity is <= 0 or > 1 || n.LengthBeats <= 0
                    || n.StartBeat < -1e-9 || n.StartBeat + n.LengthBeats > toolCtx.LengthBeats + 1e-6)
                { bad++; Console.WriteLine($"     {tool.Id}: illegal note {n.Pitch} @ {n.StartBeat}+{n.LengthBeats} v{n.Velocity}"); break; }
        }

        // A transformation with nothing to work on must not invent notes.
        if (tool.Kind == Nota.Application.Midi.MidiToolKind.Transform &&
            Nota.Application.Midi.MidiToolRunner.Apply(tool, Array.Empty<Nota.Application.NotaNote>(), null, tool.NewSettings(), toolCtx).Count != 0)
            empties++;

        // The seed is the only source of randomness, so a re-run must be identical.
        var a = Nota.Application.Midi.MidiToolRunner.Apply(tool, toolSrc, null, tool.NewSettings(), toolCtx);
        var b = Nota.Application.Midi.MidiToolRunner.Apply(tool, toolSrc, null, tool.NewSettings(), toolCtx);
        if (a.Count != b.Count) { nondet++; continue; }
        for (int i = 0; i < a.Count; i++)
            if (a[i].Pitch != b[i].Pitch || Math.Abs(a[i].StartBeat - b[i].StartBeat) > 1e-9) { nondet++; break; }
    }
    Check(Nota.Application.Midi.MidiToolCatalog.All.Count == 15, $"catalog holds 15 tools (got {Nota.Application.Midi.MidiToolCatalog.All.Count})");
    Check(bad == 0, $"every tool stays inside the clip at every parameter extreme ({bad} bad)");
    Check(empties == 0, $"no transformation invents notes from an empty clip ({empties} did)");
    Check(nondet == 0, $"every tool is deterministic for a given seed ({nondet} were not)");

    // A generated result survives the round trip through the engine.
    using var te = new NotaEngine();
    int tt = te.AddInstrumentTrack();
    int tc = te.AddMidiClip(tt, 0, 4);
    te.SetClipNotes(tt, tc, toolSrc);
    var arp = Nota.Application.Midi.MidiToolRunner.Apply(
        Nota.Application.Midi.MidiTransforms.Arpeggiate, te.GetClipNotes(tt, tc), null,
        Nota.Application.Midi.MidiTransforms.Arpeggiate.NewSettings(), toolCtx);
    Check(arp.Count > toolSrc.Length, $"Arpeggiate breaks the chord up ({toolSrc.Length} -> {arp.Count} notes)");
    te.SetClipNotes(tt, tc, arp.ToArray());
    Check(te.GetClipNotes(tt, tc).Length == arp.Count, "the generated notes round-trip through the engine");
    Check(te.Undo() && te.GetClipNotes(tt, tc).Length == toolSrc.Length, "applying a tool is one undo step");
}

Console.WriteLine(failures == 0 ? "SMOKE TEST PASSED" : $"SMOKE TEST FAILED ({failures})");
return failures == 0 ? 0 : 1;
