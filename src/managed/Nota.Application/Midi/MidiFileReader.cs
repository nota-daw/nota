// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Standard MIDI File (.mid / .midi) import: formats 0, 1 and 2 with PPQ timing, tempo
// meta-events, channel controllers, pitch wheel and channel pressure. Times are quarter-note beats.

using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Nota.Application.Midi;

/// <summary>One note-bearing part of a MIDI file: a file track, or — for a format-0 file
/// that packs several instruments into one track — one MIDI channel of it.</summary>
public sealed class MidiFilePart
{
    public string Name { get; init; } = "";
    /// <summary>Every note sits on MIDI channel 10 (the General MIDI drum channel).</summary>
    public bool IsDrums { get; init; }
    public int Channel { get; set; } = -1;
    public List<NotaNote> Notes { get; } = new();
    public SortedDictionary<int, List<AutomationPoint>> Controllers { get; } = new();
}

public readonly record struct MidiTempoPoint(double Beat, double Bpm);

public sealed class MidiFileData
{
    /// <summary>Parts that carry at least one note, in file order.</summary>
    public List<MidiFilePart> Parts { get; } = new();
    public List<MidiTempoPoint> TempoPoints { get; } = new();

    /// <summary>End of the last note, rounded up to a whole 4/4 bar (at least one bar).</summary>
    public double LengthBeats { get; set; }

    /// <summary>Every part merged into one sorted note list.</summary>
    public List<NotaNote> AllNotes()
    {
        var all = new List<NotaNote>();
        foreach (var p in Parts) all.AddRange(p.Notes);
        return MidiToolUtil.Sorted(all);
    }
}

public static class MidiFileReader
{
    public static readonly string[] Extensions = { ".mid", ".midi", ".smf" };

    public static bool IsMidiFile(string path)
    {
        var ext = Path.GetExtension(path);
        foreach (var e in Extensions)
            if (string.Equals(e, ext, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public static MidiFileData Read(string path) => Parse(File.ReadAllBytes(path));

    public static MidiFileData Parse(byte[] data)
    {
        int pos = 0;
        if (data.Length < 14 || Encoding.ASCII.GetString(data, 0, 4) != "MThd")
            throw new InvalidDataException("Not a standard MIDI file.");
        int hdrLen = (int)ReadU32(data, 4);
        int format = ReadU16(data, 8);
        int ntracks = ReadU16(data, 10);
        int division = ReadU16(data, 12);
        if ((division & 0x8000) != 0)
            throw new InvalidDataException("SMPTE-timed MIDI files aren't supported.");
        double ppq = division > 0 ? division : 480;
        pos = 8 + hdrLen;

        var result = new MidiFileData();
        var tempoEvents = new List<MidiTempoPoint>();
        double end = 0;
        for (int t = 0; t < ntracks && pos + 8 <= data.Length; t++)
        {
            string id = Encoding.ASCII.GetString(data, pos, 4);
            int len = (int)ReadU32(data, pos + 4);
            pos += 8;
            int trackEnd = Math.Min(data.Length, pos + len);
            if (id == "MTrk")
                foreach (var part in ReadTrack(data, pos, trackEnd, ppq, splitChannels: format == 0, tempoEvents: tempoEvents))
                {
                    result.Parts.Add(part);
                    foreach (var n in part.Notes) end = Math.Max(end, n.StartBeat + n.LengthBeats);
                    foreach (var points in part.Controllers.Values)
                        foreach (var p in points) end = Math.Max(end, p.Beat);
                }
            pos = trackEnd;   // unknown chunks are skipped
        }
        var orderedTempos = tempoEvents.OrderBy(p => p.Beat).ToList();
        for (int i = 0; i < orderedTempos.Count;)
        {
            int j = i + 1;
            while (j < orderedTempos.Count && Math.Abs(orderedTempos[j].Beat - orderedTempos[i].Beat) < 1e-9) j++;
            result.TempoPoints.Add(orderedTempos[j - 1]);
            i = j;
        }
        if (result.TempoPoints.Count > 0 && result.TempoPoints[0].Beat > 1e-9)
            result.TempoPoints.Insert(0, new MidiTempoPoint(0, 120.0));
        foreach (var p in result.TempoPoints) end = Math.Max(end, p.Beat);
        result.LengthBeats = Math.Max(4.0, Math.Ceiling(end / 4.0 - 1e-9) * 4.0);
        return result;
    }

    private static IEnumerable<MidiFilePart> ReadTrack(byte[] d, int pos, int end, double ppq, bool splitChannels, List<MidiTempoPoint> tempoEvents)
    {
        string name = "";
        long tick = 0;
        int running = 0;
        // Open notes keyed by channel*128 + pitch; queued so overlapping same-pitch notes pair FIFO.
        var open = new Dictionary<int, Queue<(long tick, int vel)>>();
        var byChannel = new SortedDictionary<int, List<NotaNote>>();
        var controllersByChannel = new SortedDictionary<int, SortedDictionary<int, List<AutomationPoint>>>();

        void AddController(int ch, int ctl, float value)
        {
            if (MidiController.IsReserved(ctl)) return;
            if (!controllersByChannel.TryGetValue(ch, out var map))
                controllersByChannel[ch] = map = new SortedDictionary<int, List<AutomationPoint>>();
            if (!map.TryGetValue(ctl, out var points)) map[ctl] = points = new List<AutomationPoint>();
            double beat = tick / ppq;
            if (points.Count > 0 && Math.Abs(points[^1].Beat - beat) < 1e-9)
            {
                var last = points[^1];
                last.Value = value;
                points[^1] = last;
            }
            else if (points.Count == 0 || Math.Abs(points[^1].Value - value) > 1e-6f)
                points.Add(new AutomationPoint(beat, value));
        }

        void Close(int ch, int pitch, long at)
        {
            if (!open.TryGetValue(ch * 128 + pitch, out var q) || q.Count == 0) return;
            var (on, vel) = q.Dequeue();
            double start = on / ppq, lenBeats = Math.Max(MidiToolUtil.MinLength, (at - on) / ppq);
            if (!byChannel.TryGetValue(ch, out var list)) byChannel[ch] = list = new();
            list.Add(new NotaNote(pitch, start, lenBeats, vel / 127f));
        }

        while (pos < end)
        {
            tick += ReadVarLen(d, ref pos, end);
            if (pos >= end) break;
            int status = d[pos];
            if (status == 0xFF)
            {
                if (pos + 1 >= end) break;
                int type = d[pos + 1];
                pos += 2;
                int len = (int)ReadVarLen(d, ref pos, end);
                if (type == 0x03 && name.Length == 0 && pos + len <= end)
                    name = Encoding.Latin1.GetString(d, pos, len).Trim('\0', ' ');
                if (type == 0x51 && len == 3 && pos + 3 <= end)
                {
                    int microsPerQuarter = (d[pos] << 16) | (d[pos + 1] << 8) | d[pos + 2];
                    if (microsPerQuarter > 0)
                        tempoEvents.Add(new MidiTempoPoint(tick / ppq, 60_000_000.0 / microsPerQuarter));
                }
                pos = Math.Min(end, pos + len);
                if (type == 0x2F) break;
                continue;
            }
            if (status is 0xF0 or 0xF7)
            {
                pos++;
                pos += (int)ReadVarLen(d, ref pos, end);
                continue;
            }
            if ((status & 0x80) != 0) { running = status; pos++; }
            else if (running == 0) break;   // data byte with no running status: corrupt
            int kind = running & 0xF0, ch = running & 0x0F;
            int dataLen = kind is 0xC0 or 0xD0 ? 1 : 2;
            if (pos + dataLen > end) break;
            int a = d[pos], b = dataLen > 1 ? d[pos + 1] : 0;
            pos += dataLen;
            if (kind == 0x90 && b > 0)
            {
                int key = ch * 128 + (a & 0x7F);
                if (!open.TryGetValue(key, out var q)) open[key] = q = new();
                q.Enqueue((tick, b));
            }
            else if (kind == 0x80 || (kind == 0x90 && b == 0))
                Close(ch, a & 0x7F, tick);
            else if (kind == 0xB0)
                AddController(ch, a & 0x7F, (b & 0x7F) / 127f);
            else if (kind == 0xE0)
            {
                int raw = ((b & 0x7F) << 7) | (a & 0x7F);
                AddController(ch, MidiController.PitchWheel, Math.Clamp((raw - 8192) / 8192f, -1f, 1f));
            }
            else if (kind == 0xD0)
                AddController(ch, MidiController.ChannelPressure, (a & 0x7F) / 127f);
        }
        // Notes never released: end them at the track's last event.
        foreach (var (key, q) in open)
            while (q.Count > 0) Close(key / 128, key % 128, Math.Max(tick, q.Peek().tick + (long)ppq / 4));

        var channels = new SortedSet<int>(byChannel.Keys);
        foreach (int ch in controllersByChannel.Keys) channels.Add(ch);
        if (channels.Count == 0) yield break;

        MidiFilePart BuildPart(string partName, IEnumerable<int> channelIds)
        {
            var ids = channelIds.ToArray();
            var part = new MidiFilePart { Name = partName, IsDrums = ids.Length == 1 && ids[0] == 9, Channel = ids.Length == 1 ? ids[0] : -1 };
            foreach (int ch in ids)
            {
                if (byChannel.TryGetValue(ch, out var notes)) part.Notes.AddRange(notes);
                if (!controllersByChannel.TryGetValue(ch, out var map)) continue;
                foreach (var (ctl, points) in map)
                {
                    if (!part.Controllers.TryGetValue(ctl, out var combined))
                        part.Controllers[ctl] = combined = new List<AutomationPoint>();
                    combined.AddRange(points);
                }
            }
            part.Notes.Sort(static (x, y) => x.StartBeat.CompareTo(y.StartBeat));
            foreach (int ctl in part.Controllers.Keys.ToArray())
                part.Controllers[ctl] = part.Controllers[ctl].OrderBy(p => p.Beat).ToList();
            return part;
        }

        if (!splitChannels || channels.Count <= 1)
        {
            var part = BuildPart(name, channels);
            if (part.Notes.Count == 0 && part.Controllers.Count == 0) yield break;
            yield return part;
            yield break;
        }
        foreach (int ch in channels)
        {
            var part = BuildPart(ch == 9 ? "Drums" : $"Ch {ch + 1}", new[] { ch });
            if (part.Notes.Count > 0 || part.Controllers.Count > 0) yield return part;
        }
    }

    private static long ReadVarLen(byte[] d, ref int pos, int end)
    {
        long v = 0;
        for (int i = 0; i < 4 && pos < end; i++)
        {
            byte b = d[pos++];
            v = (v << 7) | (uint)(b & 0x7F);
            if ((b & 0x80) == 0) break;
        }
        return v;
    }

    private static int ReadU16(byte[] d, int p) => (d[p] << 8) | d[p + 1];
    private static uint ReadU32(byte[] d, int p) => (uint)((d[p] << 24) | (d[p + 1] << 16) | (d[p + 2] << 8) | d[p + 3]);
}
