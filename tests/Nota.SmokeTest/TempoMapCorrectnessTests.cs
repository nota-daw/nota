// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class TempoMapCorrectnessTests
{
    private const int Sr = 48000;
    private static readonly TempoPoint[] Map = { new(0, 120), new(4, 60) };
    private static float[] Render(NotaEngine e, int frames)
    {
        var data = new float[frames * 2]; e.RenderOffline(data, frames, Sr); return data;
    }
    private static void Setup(NotaEngine e) { e.SetBpm(120); Render(e, 1); e.SetTempoPoints(Map); }
    private static double Energy(float[] data, double start, double end)
    {
        double sum = 0;
        for (int i = Math.Max(0, (int)(start * Sr) * 2); i < Math.Min(data.Length, (int)(end * Sr) * 2); ++i)
            sum += data[i] * data[i];
        return sum;
    }
    public static IEnumerable<(bool, string)> Run()
    {
        using (var recording = new NotaEngine())
            yield return (recording.AudioRecordSelfTest(), "tempo correctness: mapped loop capture preserves full and partial passes");
        string dir = Path.Combine(Path.GetTempPath(), "nota-tempo-correctness-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string wav = Path.Combine(dir, "source.wav");
            WavWriter.WriteStereo(wav, 8, Sr, i => (.1 + i / (80.0 * Sr), .1 + i / (80.0 * Sr)));
            foreach (bool reverse in new[] { false, true })
            {
                using var edit = new NotaEngine(); Setup(edit); edit.SplitFades = false;
                int track = edit.AddAudioTrack(); edit.AddAudioClip(track, wav, 0);
                edit.SetClipReverse(track, 0, reverse);
                int right = edit.SplitClip(track, 0, 5);
                edit.TryGetAudioClipInfo(track, 0, out var a); edit.TryGetAudioClipInfo(track, right, out var b);
                yield return (right == 1 && a.LengthFrames == 3 * Sr &&
                    Math.Abs(a.SourceOffsetFrames - (reverse ? 5 * Sr : 0)) < 1 &&
                    b.LengthFrames == 5 * Sr && Math.Abs(b.SourceOffsetFrames - (reverse ? 0 : 3 * Sr)) < 1,
                    $"tempo correctness: {(reverse ? "reversed" : "forward")} split at beat 5 cuts the 3-second source boundary");
                yield return (edit.Undo() && edit.TryGetTrackInfo(0, out var ti) && ti.ClipCount == 1,
                    "tempo correctness: mapped split undoes in one step");
                edit.TrimClip(track, 0, 5, 1); edit.TryGetAudioClipInfo(track, 0, out var trim);
                yield return (trim.LengthFrames == Sr && Math.Abs(trim.SourceOffsetFrames - (reverse ? 4 * Sr : 3 * Sr)) < 1,
                    $"tempo correctness: {(reverse ? "reversed" : "forward")} trim uses both mapped endpoints");
            }
            using (var edit = new NotaEngine())
            {
                Setup(edit); edit.SplitFades = false;
                int t = edit.AddAudioTrack(); edit.AddAudioClip(t, wav, 0);
                edit.SplitClipsInRange(new[] { t }, 5, 6);
                edit.TryGetAudioClipInfo(t, 1, out var middle);
                yield return (Math.Abs(middle.SourceOffsetFrames - 3 * Sr) < 1 && middle.LengthFrames == Sr,
                    "tempo correctness: range splits preserve the mapped source slice");
            }
            using (var placement = new NotaEngine())
            {
                Setup(placement);
                int source = placement.AddAudioTrack(), dest = placement.AddAudioTrack();
                placement.AddAudioClip(source, wav, 0); placement.TrimClip(source, 0, 0, 4);
                placement.AddAudioClip(dest, wav, 6); placement.TrimClip(dest, 0, 6, 2);
                placement.CopyClip(source, 0);
                int pasted = placement.PasteClip(dest, 4);
                yield return (placement.TryGetAudioClipInfo(dest, pasted, out var pastedInfo) && pastedInfo.StartBeat == 4,
                    "tempo correctness: paste tests overlap using its mapped destination duration");
                placement.AddAudioClip(source, wav, 6); placement.TrimClip(source, 1, 6, 2);
                placement.DuplicateClip(source, 0);
                yield return (placement.TryGetTrackInfo(0, out var info) && info.ClipCount == 3,
                    "tempo correctness: duplicate preserves a neighbor after its mapped destination end");
            }
            using (var capture = new NotaEngine())
            {
                Setup(capture);
                int track = capture.AddInstrumentTrack(), clip = capture.AddMidiClip(track, 0, 16);
                capture.SetClipNotes(track, clip, new[] { new NotaNote(60, 5, .15, 1), new NotaNote(64, 15, .15, 1) });
                long frames = capture.BeginFreeze(track, 16);
                yield return (frames == 16L * Sr, "tempo correctness: freeze covers 14 mapped seconds plus its 2-second tail");
                capture.Seek(0); capture.Play(); Render(capture, 16 * Sr); capture.EndFreeze(track);
                capture.Seek(0); var frozen = Render(capture, 14 * Sr);
                yield return (Energy(frozen, 3.01, 3.12) > .001 && Energy(frozen, 13.01, 13.12) > .001,
                    "tempo correctness: frozen notes retain their mapped onsets and the ending");
                byte[] state = capture.GetFreezeState(track);
                using var reopened = new NotaEngine(); Setup(reopened);
                int restored = reopened.AddInstrumentTrack(); reopened.SetFreezeState(restored, state);
                reopened.Seek(0); reopened.Play(); var replay = Render(reopened, 14 * Sr);
                yield return (Energy(replay, 3.01, 3.12) > .001 && Energy(replay, 13.01, 13.12) > .001,
                    "tempo correctness: freeze save/reload preserves the capture map");
                foreach (var invalid in new[] { state[..30], (byte[])state.Clone() })
                {
                    if (invalid.Length > 30) Array.Fill(invalid, (byte)255, 20, 4); // excessive point count
                    reopened.SetFreezeState(restored, invalid);
                    yield return (reopened.GetFreezeState(restored).SequenceEqual(state),
                        "tempo correctness: malformed frozen timing leaves the track unchanged");
                }
                reopened.SetTempoPoints(new[] { new TempoPoint(0, 120), new TempoPoint(4, 90) });
                reopened.Seek(0); replay = Render(reopened, 10 * Sr);
                yield return (Energy(replay, 2.68, 2.75) > .001 && Energy(replay, 9.35, 9.42) > .001,
                    "tempo correctness: frozen notes follow musical beats after the tempo map changes");
                capture.UnfreezeTrack(track);
                capture.Seek(0); Render(capture, 1);
                var bounce = new TrackFreezer().BounceRange(capture, track, 4, 8, false, false);
                yield return (bounce?.Length == 8 * Sr,
                    "tempo correctness: a nonzero range bounce uses mapped start and end times");
                var ending = new TrackFreezer().BounceRange(capture, track, 14, 16, false, false);
                yield return (ending?.Length == 4 * Sr && Energy(ending, 1.01, 1.12) > .001,
                    "tempo correctness: a late nonzero-range note survives the bounce");
            }
            using (var legacy = new NotaEngine())
            {
                legacy.SetBpm(120); Render(legacy, 1);
                int track = legacy.AddInstrumentTrack();
                using var bytes = new MemoryStream();
                using (var writer = new BinaryWriter(bytes, System.Text.Encoding.UTF8, leaveOpen: true))
                {
                    writer.Write(Sr * .5); writer.Write((long)Sr);
                    for (int i = 0; i < Sr * 2; ++i) writer.Write(.25f);
                }
                legacy.SetFreezeState(track, bytes.ToArray()); legacy.Seek(0); legacy.Play();
                yield return (legacy.IsTrackFrozen(track) && Energy(Render(legacy, Sr), .1, .9) > 1,
                    "tempo correctness: legacy constant-tempo frozen PCM remains readable");
            }
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
