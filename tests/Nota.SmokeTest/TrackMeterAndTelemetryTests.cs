// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Batch track-meter C ABI parity, DeviceTelemetry gating (AlwaysOff / OnDemand /
// AlwaysOn + 3s observation lease), AutoFilter S_SampleRate scope invariant under
// gated CPU telemetry, and nested rack propagation (#1050).

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class TrackMeterAndTelemetryTests
{
    private const int Block = 512, Rate = 48000;
    private const int EqKind = 0, ReverbKind = 2, RackDeviceKind = 5, AutoFilterKind = 7;
    private const int EqCpuSlot = 4, ReverbCpuSlot = 3, AutoFilterLfoOnParam = 16;
    private const int AutoFilterCpuSlot = 8, AutoFilterRateSlot = 9;
    private const int ConsortCpuSlot = 4, PentadCpuSlot = 5, PentadKind = 14;

    private static string ToneClip()
    {
        string path = Path.Combine(Path.GetTempPath(), "nota-meter-telemetry-tone.wav");
        if (!File.Exists(path))
        {
            var rng = new Random(7);
            WavWriter.WriteStereo(path, 6, Rate, i =>
            {
                double s = 0.25 * Math.Sin(2 * Math.PI * 220.0 * i / Rate) + (rng.NextDouble() * 0.1 - 0.05);
                return (s, s * 0.8);
            });
        }
        return path;
    }

    private static void RenderBlocks(NotaEngine e, int blocks)
    {
        var buf = new float[Block * 2];
        for (int i = 0; i < blocks; i++)
            e.RenderOffline(buf, Block, Rate);
    }

    private static bool MeterEquals(in NotaMeter a, in NotaMeter b) =>
        a.PeakL == b.PeakL && a.PeakR == b.PeakR && a.RmsL == b.RmsL && a.RmsR == b.RmsR;

    private static (bool ok, string label) CheckBatchTrackMeters()
    {
        using var e = new NotaEngine();
        if (e.ReadTrackMeters(Span<TrackMeterReading>.Empty) != 0)
            return (false, "empty engine ReadTrackMeters == 0");
        if (e.DeviceTelemetry != DeviceTelemetryMode.OnDemand)
            return (false, "default DeviceTelemetry is OnDemand");

        bool rejectedInvalid = false;
        try { e.DeviceTelemetry = (DeviceTelemetryMode)99; }
        catch (NotaEngineException) { rejectedInvalid = true; }
        if (!rejectedInvalid)
            return (false, "invalid DeviceTelemetry rejects argument");

        string clip = ToneClip();
        for (int i = 0; i < 100; i++)
        {
            if (i % 5 == 0)
            {
                int t = e.AddInstrumentTrack();
                int c = e.AddMidiClip(t, 0.0, 4.0);
                e.SetClipNotes(t, c, [new NotaNote(60 + (i % 12), 0.0, 3.5, 0.8f)]);
            }
            else
            {
                int t = e.AddAudioTrack();
                e.AddAudioClip(t, clip, 0.0);
            }
        }

        RenderBlocks(e, 1);
        e.Seek(0);
        e.Play();
        RenderBlocks(e, 6);

        int countOnly = e.ReadTrackMeters(Span<TrackMeterReading>.Empty);
        if (countOnly != 100)
            return (false, $"query count for 100 tracks == 100 (got {countOnly})");

        var truncated = new TrackMeterReading[37];
        int totalAfterTrunc = e.ReadTrackMeters(truncated);
        if (totalAfterTrunc != 100)
            return (false, $"truncated ReadTrackMeters still returns total 100 (got {totalAfterTrunc})");
        for (int i = 0; i < truncated.Length; i++)
        {
            if (!e.TryGetTrackMeter(truncated[i].TrackId, out var single) || !MeterEquals(truncated[i].Meter, single))
                return (false, $"truncated entry {i} matches TryGetTrackMeter");
        }

        var all = new TrackMeterReading[120];
        int filled = e.ReadTrackMeters(all);
        if (filled != 100)
            return (false, $"full ReadTrackMeters returns 100 (got {filled})");

        var seenIds = new HashSet<int>();
        var batch = new TrackMeterBatch();
        batch.Refresh(e);
        int activeCount = 0;
        for (int i = 0; i < filled; i++)
        {
            var entry = all[i];
            if (!seenIds.Add(entry.TrackId))
                return (false, $"duplicate track id {entry.TrackId} in ReadTrackMeters");
            if (!e.TryGetTrackMeter(entry.TrackId, out var single) || !MeterEquals(entry.Meter, single))
                return (false, $"entry {i} (track {entry.TrackId}) matches TryGetTrackMeter");
            if (!batch.TryGet(entry.TrackId, out var batched) || !MeterEquals(entry.Meter, batched))
                return (false, $"TrackMeterBatch entry matches track {entry.TrackId}");
            if (entry.Meter.PeakL > 1e-4f && entry.Meter.RmsL > 1e-4f)
                activeCount++;
        }
        return (activeCount >= 90, $"100-track batch meter parity & live levels (active={activeCount}/100)");
    }

    private static (bool ok, string label) CheckTelemetryModesAndLease()
    {
        using var e = new NotaEngine();
        int t = e.AddAudioTrack();
        e.AddAudioClip(t, ToneClip(), 0.0);
        int rev = e.AddBuiltinDevice(t, ReverbKind);
        int eq = e.AddBuiltinDevice(t, EqKind);

        RenderBlocks(e, 1);
        e.Seek(0);
        e.Play();

        // Unobserved OnDemand must skip clock reads until first scope read.
        RenderBlocks(e, 8);
        var scope = new float[64];
        e.DeviceScope(t, rev, scope, scope.Length);
        float beforeObserveRev = scope[ReverbCpuSlot];
        e.DeviceScope(t, eq, scope, scope.Length);
        float beforeObserveEq = scope[EqCpuSlot];
        if (beforeObserveRev != 0f || beforeObserveEq != 0f)
            return (false, $"unobserved OnDemand reports 0 CPU (rev={beforeObserveRev}, eq={beforeObserveEq})");

        // Scope reads opened the 3 s lease: rendering more blocks populates CPU.
        RenderBlocks(e, 6);
        e.DeviceScope(t, rev, scope, scope.Length);
        float observedRev = scope[ReverbCpuSlot];
        if (!(observedRev > 0f && float.IsFinite(observedRev)))
            return (false, $"observed OnDemand measures Reverb CPU > 0 (got {observedRev})");

        // AlwaysOff zeroes immediately and does not measure even under continuous scope reads.
        e.DeviceTelemetry = DeviceTelemetryMode.AlwaysOff;
        e.DeviceScope(t, rev, scope, scope.Length);
        if (scope[ReverbCpuSlot] != 0f)
            return (false, $"AlwaysOff clears CPU load immediately (got {scope[ReverbCpuSlot]})");
        RenderBlocks(e, 6);
        e.DeviceScope(t, rev, scope, scope.Length);
        if (scope[ReverbCpuSlot] != 0f)
            return (false, $"AlwaysOff keeps CPU 0 across blocks (got {scope[ReverbCpuSlot]})");

        // AlwaysOn measures without any scope reads.
        e.DeviceTelemetry = DeviceTelemetryMode.AlwaysOn;
        RenderBlocks(e, 6);
        e.DeviceScope(t, rev, scope, scope.Length);
        if (!(scope[ReverbCpuSlot] > 0f))
            return (false, $"AlwaysOn measures CPU without prior scope (got {scope[ReverbCpuSlot]})");

        // Reset cleanly via AlwaysOff, open one OnDemand lease, then let 4 s expire without scope reads.
        e.DeviceTelemetry = DeviceTelemetryMode.AlwaysOff;
        RenderBlocks(e, 1);
        e.DeviceTelemetry = DeviceTelemetryMode.OnDemand;
        e.DeviceScope(t, rev, scope, scope.Length); // arms lease
        RenderBlocks(e, 4);
        e.DeviceScope(t, rev, scope, scope.Length); // renews lease & reads > 0
        if (!(scope[ReverbCpuSlot] > 0f))
            return (false, $"OnDemand renewed lease measures > 0 (got {scope[ReverbCpuSlot]})");

        int expireBlocks = (4 * Rate) / Block + 4;
        RenderBlocks(e, expireBlocks);
        e.DeviceScope(t, rev, scope, scope.Length);
        if (scope[ReverbCpuSlot] != 0f)
            return (false, $"OnDemand 3s lease expires cleanly when unobserved (got {scope[ReverbCpuSlot]})");

        return (true, "telemetry AlwaysOff / OnDemand / AlwaysOn and 3s lease");
    }

    private static (bool ok, string label) CheckAutoFilterScopeUnderGatedCpu()
    {
        using var e = new NotaEngine();
        e.DeviceTelemetry = DeviceTelemetryMode.AlwaysOff;
        int t = e.AddAudioTrack();
        e.AddAudioClip(t, ToneClip(), 0.0);
        int af = e.AddBuiltinDevice(t, AutoFilterKind);
        e.DeviceSetParam(t, af, AutoFilterLfoOnParam, 1.0f);

        RenderBlocks(e, 1);
        e.Seek(0);
        e.Play();
        RenderBlocks(e, 2);

        var scope = new float[64];
        e.DeviceScope(t, af, scope, scope.Length);
        bool ok = scope[AutoFilterCpuSlot] == 0f && scope[AutoFilterRateSlot] >= 44100f;
        return (ok, $"AutoFilter S_SampleRate populated under AlwaysOff (cpu={scope[AutoFilterCpuSlot]}, sr={scope[AutoFilterRateSlot]})");
    }

    private static (bool ok, string label) CheckInstrumentsAndNestedRacks()
    {
        using var e = new NotaEngine();
        int consortT = e.AddConsortTrack();
        int consortC = e.AddMidiClip(consortT, 0.0, 4.0);
        e.SetClipNotes(consortT, consortC, [new NotaNote(48, 0.0, 3.5, 0.9f)]);

        int pentadT = e.AddPentadTrack();
        int pentadC = e.AddMidiClip(pentadT, 0.0, 4.0);
        e.SetClipNotes(pentadT, pentadC, [new NotaNote(60, 0.0, 3.5, 0.9f)]);

        int instRackT = e.AddInstrumentRackTrack();
        int instRackCh = e.RackAddChain(instRackT, PentadKind);
        int instRackC = e.AddMidiClip(instRackT, 0.0, 4.0);
        e.SetClipNotes(instRackT, instRackC, [new NotaNote(64, 0.0, 3.5, 0.9f)]);

        int fxRackT = e.AddAudioTrack();
        e.AddAudioClip(fxRackT, ToneClip(), 0.0);
        int fxRackDev = e.AddBuiltinDevice(fxRackT, RackDeviceKind);
        int fxChain = e.RackDevAddChain(fxRackT, fxRackDev, -1);
        int fxReverb = e.RackDevAddChainDevice(fxRackT, fxRackDev, fxChain, ReverbKind);

        e.DeviceTelemetry = DeviceTelemetryMode.AlwaysOn;
        RenderBlocks(e, 1);
        e.Seek(0);
        e.Play();
        RenderBlocks(e, 6);

        var scope = new float[256];
        e.InstrumentScope(consortT, scope);
        float consortCpu = scope[ConsortCpuSlot];
        e.InstrumentScope(pentadT, scope);
        float pentadCpu = scope[PentadCpuSlot];
        e.RackChainInstrumentScope(instRackT, instRackCh, scope);
        float rackPentadCpu = scope[PentadCpuSlot];
        e.RackDevChainDeviceScope(fxRackT, fxRackDev, fxChain, fxReverb, scope, scope.Length);
        float rackReverbCpu = scope[ReverbCpuSlot];

        if (!(consortCpu > 0f && pentadCpu > 0f && rackPentadCpu > 0f && rackReverbCpu > 0f))
            return (false, $"AlwaysOn propagates to Consort/Pentad & nested racks (c={consortCpu}, p={pentadCpu}, rp={rackPentadCpu}, rr={rackReverbCpu})");

        // Switch AlwaysOff and also add a new device inside the rack chain to verify commit propagation.
        e.DeviceTelemetry = DeviceTelemetryMode.AlwaysOff;
        int fxEq = e.RackDevAddChainDevice(fxRackT, fxRackDev, fxChain, EqKind);
        RenderBlocks(e, 4);

        e.InstrumentScope(consortT, scope);
        if (scope[ConsortCpuSlot] != 0f) return (false, "Consort AlwaysOff zeroes ScCpu");
        e.InstrumentScope(pentadT, scope);
        if (scope[PentadCpuSlot] != 0f) return (false, "Pentad AlwaysOff zeroes ScCpu");
        e.RackChainInstrumentScope(instRackT, instRackCh, scope);
        if (scope[PentadCpuSlot] != 0f) return (false, "Rack chain Pentad AlwaysOff zeroes ScCpu");
        e.RackDevChainDeviceScope(fxRackT, fxRackDev, fxChain, fxReverb, scope, scope.Length);
        if (scope[ReverbCpuSlot] != 0f) return (false, "Rack chain Reverb AlwaysOff zeroes S_Cpu");
        e.RackDevChainDeviceScope(fxRackT, fxRackDev, fxChain, fxEq, scope, scope.Length);
        if (scope[EqCpuSlot] != 0f) return (false, "Newly committed rack chain Eq inherits AlwaysOff");

        return (true, "Consort, Pentad, and nested RackDevice/RackInstrument telemetry propagation");
    }

    public static IEnumerable<(bool ok, string label)> Run()
    {
        yield return CheckBatchTrackMeters();
        yield return CheckTelemetryModesAndLease();
        yield return CheckAutoFilterScopeUnderGatedCpu();
        yield return CheckInstrumentsAndNestedRacks();
    }
}
