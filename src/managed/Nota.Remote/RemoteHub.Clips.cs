// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// What is in each Session clip, for the phone's Scenes grid (#557; specs/090
// contracts/clips-message.md): the clip's name, colour and length, and either its notes on a
// 1/64-of-the-clip grid or 48 peaks — the picture the desktop's Session slot draws. The engine
// keeps no change counter for slots, so the hub re-reads them round-robin, a few per UI tick
// and only while a phone shows Scenes, and compares a signature of the compact preview: an
// unchanged clip costs one cheap read every second or so and is never re-sent. A slot whose
// state or length moved, or one being recorded, jumps the queue. The reads are the
// control-side ones the desktop Session view already makes on the UI thread; nothing here
// reaches the audio thread.

using System.Text;
using System.Text.Json;
using Nota.Application;

namespace Nota.Remote;

public sealed partial class RemoteHub
{
    internal const int PreviewGrid = 64, PreviewMaxNotes = 128, PreviewPeaks = 48;
    private const int PreviewReadsPerTick = 24, PreviewBatch = 64;
    private const long PreviewRecordMs = 250;

    /// <summary>One slot's preview as its JSON object; <c>Empty</c> for a slot with no clip.</summary>
    private sealed record SlotPreview(string Json, ulong Sig, bool Empty);

    private readonly Dictionary<int, SlotPreview> _previews = new();
    private readonly Dictionary<int, (int State, double Len)> _previewSlotSeen = new();
    private readonly float[] _peakBuf = new float[PreviewPeaks * 2];
    private int _previewCursor;
    private bool _previewsFresh;
    private long _previewRecordAt;

    private static int SlotKey(int trackId, int scene) => trackId * 4096 + scene;

    // The slots the phone's grid has: every track but groups and returns, every scene.
    private List<(TrackRow Track, int Scene)> PreviewSlots()
    {
        int scenes = _engine.SceneCount;
        var list = new List<(TrackRow, int)>();
        foreach (var r in _tracks)
        {
            if (r.Kind is "group" or "return") continue;
            for (int s = 0; s < scenes; s++) list.Add((r, s));
        }
        return list;
    }

    /// <summary>Bring the previews up to date for this tick. Called once per tick, before the
    /// phones are pushed; does nothing while no phone shows Scenes.</summary>
    private void UpdatePreviews(IRemoteHost host, bool anyOnScenes)
    {
        if (!anyOnScenes) { _previewsFresh = false; return; }
        var slots = PreviewSlots();
        var live = new HashSet<int>(slots.Count);
        foreach (var (t, s) in slots) live.Add(SlotKey(t.Id, s));
        foreach (var k in _previews.Keys.Where(k => !live.Contains(k)).ToList()) { _previews.Remove(k); _previewSlotSeen.Remove(k); }

        // Nobody watched since the last full read: read every slot once, so a phone opening
        // Scenes gets the whole grid in its first message.
        if (!_previewsFresh)
        {
            foreach (var (t, s) in slots) ReadPreview(host, t, s);
            _previewsFresh = true;
            return;
        }

        long now = Environment.TickCount64;
        bool recordDue = now - _previewRecordAt >= PreviewRecordMs;
        if (recordDue) _previewRecordAt = now;
        var done = new HashSet<int>();
        foreach (var (t, s) in slots)
        {
            int key = SlotKey(t.Id, s);
            int st = _engine.SessionSlotState(t.Id, s);
            double len = st == 0 ? 0 : _engine.SessionSlotLength(t.Id, s);
            bool moved = !_previewSlotSeen.TryGetValue(key, out var seen) || seen.State != st || Math.Abs(seen.Len - len) > 1e-6;
            bool recording = st is 4 or 5;
            if (moved || (recording && recordDue)) { ReadPreview(host, t, s, st, len); done.Add(key); }
        }
        if (slots.Count == 0) return;
        for (int n = 0; n < Math.Min(PreviewReadsPerTick, slots.Count); n++)
        {
            _previewCursor = (_previewCursor + 1) % slots.Count;
            var (t, s) = slots[_previewCursor];
            if (!done.Contains(SlotKey(t.Id, s))) ReadPreview(host, t, s);
        }
    }

    private void ReadPreview(IRemoteHost host, TrackRow t, int scene) =>
        ReadPreview(host, t, scene, _engine.SessionSlotState(t.Id, scene), 0);

    private void ReadPreview(IRemoteHost host, TrackRow t, int scene, int state, double len)
    {
        int key = SlotKey(t.Id, scene);
        if (state != 0 && len <= 0) len = _engine.SessionSlotLength(t.Id, scene);
        _previewSlotSeen[key] = (state, len);
        string json = state == 0 ? EmptySlotJson(t.Id, scene) : BuildPreview(host, t, scene, state, len);
        ulong sig = Fnv(json);
        if (_previews.TryGetValue(key, out var was) && was.Sig == sig) return;
        _previews[key] = new SlotPreview(json, sig, state == 0);
    }

    private static string EmptySlotJson(int trackId, int scene) => Json(w =>
    {
        w.WriteNumber("id", trackId);
        w.WriteNumber("i", scene);
        w.WriteString("k", "x");
    });

    private string BuildPreview(IRemoteHost host, TrackRow t, int scene, int state, double len)
    {
        bool recording = state is 4 or 5;
        bool audio = t.Kind == "audio";
        NotaNote[] notes = audio ? Array.Empty<NotaNote>() : _engine.GetSessionNotes(t.Id, scene);
        // A take being recorded shows whole bars recorded so far, as the desktop slot does.
        if (recording && !audio)
        {
            double end = notes.Length == 0 ? 0 : notes.Max(n => n.StartBeat + n.LengthBeats);
            double sofar = Math.Max(4, Math.Ceiling((end + 1e-6) / 4) * 4);
            len = len > 0 ? Math.Min(len, sofar) : sofar;
        }
        string name = _engine.GetSessionClipName(t.Id, scene);
        if (string.IsNullOrEmpty(name)) name = $"{t.Name} {scene + 1}";
        return Json(w =>
        {
            w.WriteNumber("id", t.Id);
            w.WriteNumber("i", scene);
            w.WriteString("nm", name);
            w.WriteString("col", host.ClipColor(t.Id, scene));
            w.WriteNumber("len", Math.Round(len, 2));
            if (recording) w.WriteBoolean("rec", true);
            if (audio)
            {
                w.WriteString("k", "p");
                int count = recording ? (state == 4 ? _engine.AudioRecordPeaksOf(t.Id, _peakBuf, PreviewPeaks) : 0)
                                      : _engine.GetSessionSlotPeaks(t.Id, scene, _peakBuf, PreviewPeaks);
                w.WriteStartArray("p");
                foreach (int v in Peaks(_peakBuf, count)) w.WriteNumberValue(v);
                w.WriteEndArray();
            }
            else
            {
                w.WriteString("k", "n");
                w.WriteStartArray("n");
                foreach (var (s, l, p) in NoteBars(notes, len))
                {
                    w.WriteStartArray();
                    w.WriteNumberValue(s);
                    w.WriteNumberValue(l);
                    w.WriteNumberValue(p);
                    w.WriteEndArray();
                }
                w.WriteEndArray();
            }
        });
    }

    /// <summary>Peak magnitudes 0–99, scaled to the loudest bucket, as the desktop draws them.</summary>
    internal static int[] Peaks(float[] minMax, int count)
    {
        count = Math.Clamp(count, 0, minMax.Length / 2);
        float max = 1e-6f;
        for (int i = 0; i < count; i++) max = Math.Max(max, Math.Max(Math.Abs(minMax[i * 2]), Math.Abs(minMax[i * 2 + 1])));
        var out_ = new int[count];
        for (int i = 0; i < count; i++)
            out_[i] = (int)Math.Round(Math.Max(Math.Abs(minMax[i * 2]), Math.Abs(minMax[i * 2 + 1])) / max * 99);
        return out_;
    }

    /// <summary>A clip's notes on a 1/64-of-its-length grid: [start, length ≥ 1, pitch], the
    /// bars that land on one pitch row and overlap merged, at most <see cref="PreviewMaxNotes"/>
    /// (the first by start) — the clip's shape at the size a phone cell draws it.</summary>
    internal static List<(int Start, int Len, int Pitch)> NoteBars(IReadOnlyList<NotaNote> notes, double clipLen)
    {
        var bars = new List<(int Start, int Len, int Pitch)>();
        if (clipLen <= 0) return bars;
        foreach (var n in notes)
        {
            if (n.StartBeat >= clipLen || n.Pitch is < 0 or > 127) continue;
            int s = Math.Clamp((int)Math.Floor(n.StartBeat / clipLen * PreviewGrid), 0, PreviewGrid - 1);
            int e = Math.Clamp((int)Math.Ceiling(Math.Min(n.StartBeat + n.LengthBeats, clipLen) / clipLen * PreviewGrid), s + 1, PreviewGrid);
            bars.Add((s, e - s, n.Pitch));
        }
        bars.Sort((a, b) => a.Pitch != b.Pitch ? a.Pitch.CompareTo(b.Pitch) : a.Start.CompareTo(b.Start));
        var merged = new List<(int Start, int Len, int Pitch)>(bars.Count);
        foreach (var b in bars)
        {
            if (merged.Count > 0 && merged[^1].Pitch == b.Pitch && b.Start <= merged[^1].Start + merged[^1].Len)
            {
                var m = merged[^1];
                merged[^1] = (m.Start, Math.Max(m.Start + m.Len, b.Start + b.Len) - m.Start, m.Pitch);
            }
            else merged.Add(b);
        }
        merged.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.Pitch.CompareTo(b.Pitch));
        if (merged.Count > PreviewMaxNotes) merged.RemoveRange(PreviewMaxNotes, merged.Count - PreviewMaxNotes);
        return merged;
    }

    private static ulong Fnv(string s)
    {
        ulong h = 14695981039346656037UL;
        foreach (char ch in s) { h ^= ch; h *= 1099511628211UL; }
        return h;
    }

    /// <summary>Send a phone on Scenes the previews it hasn't got (C1–C3): the whole grid,
    /// batched, when it arrives on Scenes; afterwards only the slots that changed.</summary>
    private void PushPreviews(RemoteClient c)
    {
        if (c.Screen != "Scenes") { c.SentClips = null; return; }
        bool first = c.SentClips is null;
        var sent = c.SentClips ??= new Dictionary<int, ulong>();
        var batch = new List<string>();
        foreach (var (key, p) in _previews)
        {
            if (sent.TryGetValue(key, out var was) ? was == p.Sig : p.Empty) continue;
            if (p.Empty) sent.Remove(key); else sent[key] = p.Sig;
            batch.Add(p.Json);
        }
        foreach (var key in sent.Keys.Where(k => !_previews.ContainsKey(k)).ToList())
        {
            sent.Remove(key);
            batch.Add(EmptySlotJson(key / 4096, key % 4096));
        }
        if (batch.Count == 0 && !first) return;
        for (int i = 0; i < Math.Max(1, batch.Count); i += PreviewBatch)
        {
            var sb = new StringBuilder("{\"t\":\"clips\",\"all\":").Append(first && i == 0 ? "true" : "false").Append(",\"s\":[");
            sb.AppendJoin(',', batch.Skip(i).Take(PreviewBatch));
            c.Send(sb.Append("]}").ToString());
        }
    }
}
