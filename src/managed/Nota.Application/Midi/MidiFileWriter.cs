// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Nota.Application.Midi;

public static class MidiFileWriter
{
    private const int Ppq = 960;
    private const int MaxVariableLength = 0x0FFFFFFF;
    private readonly record struct Event(long Tick, int Order, long Sequence, byte[] Bytes);

    public static byte[] Write(MidiFileData file, int format = 1)
    {
        ArgumentNullException.ThrowIfNull(file);
        List<byte[]> tracks;
        if (format == 0)
        {
            tracks = new List<byte[]> { WriteFormat0Track(file) };
        }
        else
        {
            tracks = new List<byte[]> { WriteTempoTrack(file) };
            foreach (var part in file.Parts) tracks.Add(WritePartTrack(part));
        }

        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("MThd"));
        WriteU32(writer, 6);
        WriteU16(writer, format == 0 ? 0 : (tracks.Count > 1 ? 1 : 0));
        WriteU16(writer, tracks.Count);
        WriteU16(writer, Ppq);
        foreach (var track in tracks)
        {
            writer.Write(Encoding.ASCII.GetBytes("MTrk"));
            WriteU32(writer, track.Length);
            writer.Write(track);
        }
        writer.Flush();
        return output.ToArray();
    }

    public static void Write(string path, MidiFileData file, int format = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.WriteAllBytes(path, Write(file, format));
    }

    private static byte[] WriteFormat0Track(MidiFileData file)
    {
        var events = new List<Event>();
        long sequence = 0;
        foreach (var tempo in file.TempoPoints)
        {
            if (!(tempo.Bpm > 0) || double.IsNaN(tempo.Bpm) || double.IsInfinity(tempo.Bpm)) continue;
            long micros = (long)Math.Clamp(Math.Round(60_000_000.0 / tempo.Bpm), 1, 0xFFFFFF);
            events.Add(new Event(ToTick(tempo.Beat), 0, sequence++, new byte[]
            {
                0xFF, 0x51, 0x03, (byte)(micros >> 16), (byte)(micros >> 8), (byte)micros
            }));
        }
        if (events.Count == 0)
            events.Add(new Event(0, 0, sequence++, new byte[] { 0xFF, 0x51, 0x03, 0x07, 0xA1, 0x20 }));

        int partIdx = 0;
        foreach (var part in file.Parts)
        {
            int channel = part.IsDrums ? 9 : (partIdx % 16);
            if (channel == 9 && !part.IsDrums) channel = (partIdx + 1) % 16;
            partIdx++;

            foreach (var note in part.Notes)
            {
                int pitch = Math.Clamp(note.Pitch, 0, 127);
                int velocity = Math.Clamp((int)Math.Round(note.Velocity * 127), 1, 127);
                long start = ToTick(note.StartBeat);
                long end = Math.Max(start + 1, ToTick(note.StartBeat + Math.Max(0, note.LengthBeats)));
                events.Add(new Event(start, 2, sequence++, new byte[] { (byte)(0x90 | channel), (byte)pitch, (byte)velocity }));
                events.Add(new Event(end, 0, sequence++, new byte[] { (byte)(0x80 | channel), (byte)pitch, 0 }));
            }
            foreach (var (ctl, points) in part.Controllers)
            {
                if (MidiController.IsReserved(ctl)) continue;
                foreach (var point in points)
                {
                    long tick = ToTick(point.Beat);
                    if (ctl is >= 0 and <= 127)
                    {
                        int value = Math.Clamp((int)Math.Round(point.Value * 127), 0, 127);
                        events.Add(new Event(tick, 1, sequence++, new byte[] { (byte)(0xB0 | channel), (byte)ctl, (byte)value }));
                    }
                    else if (ctl == MidiController.PitchWheel)
                    {
                        int raw = Math.Clamp((int)Math.Round((Math.Clamp(point.Value, -1f, 1f) + 1f) * 8192), 0, 16383);
                        events.Add(new Event(tick, 1, sequence++, new byte[] { (byte)(0xE0 | channel), (byte)(raw & 0x7F), (byte)((raw >> 7) & 0x7F) }));
                    }
                    else if (ctl == MidiController.ChannelPressure)
                    {
                        int value = Math.Clamp((int)Math.Round(point.Value * 127), 0, 127);
                        events.Add(new Event(tick, 1, sequence++, new byte[] { (byte)(0xD0 | channel), (byte)value }));
                    }
                }
            }
        }
        long endTick = events.Count == 0 ? 0 : events.Max(e => e.Tick);
        return EncodeTrack("Merged", events, endTick);
    }

    private static byte[] WriteTempoTrack(MidiFileData file)
    {
        var events = new List<Event>();
        long sequence = 0;
        foreach (var tempo in file.TempoPoints)
        {
            if (!(tempo.Bpm > 0) || double.IsNaN(tempo.Bpm) || double.IsInfinity(tempo.Bpm)) continue;
            long micros = (long)Math.Clamp(Math.Round(60_000_000.0 / tempo.Bpm), 1, 0xFFFFFF);
            events.Add(new Event(ToTick(tempo.Beat), 0, sequence++, new byte[]
            {
                0xFF, 0x51, 0x03, (byte)(micros >> 16), (byte)(micros >> 8), (byte)micros
            }));
        }
        if (events.Count == 0)
            events.Add(new Event(0, 0, sequence++, new byte[] { 0xFF, 0x51, 0x03, 0x07, 0xA1, 0x20 }));
        return EncodeTrack("Tempo", events, events.Max(e => e.Tick));
    }

    private static byte[] WritePartTrack(MidiFilePart part)
    {
        var events = new List<Event>();
        long sequence = 0;
        int channel = part.IsDrums ? 9 : 0;
        foreach (var note in part.Notes)
        {
            int pitch = Math.Clamp(note.Pitch, 0, 127);
            int velocity = Math.Clamp((int)Math.Round(note.Velocity * 127), 1, 127);
            long start = ToTick(note.StartBeat);
            long end = Math.Max(start + 1, ToTick(note.StartBeat + Math.Max(0, note.LengthBeats)));
            events.Add(new Event(start, 2, sequence++, new byte[] { (byte)(0x90 | channel), (byte)pitch, (byte)velocity }));
            events.Add(new Event(end, 0, sequence++, new byte[] { (byte)(0x80 | channel), (byte)pitch, 0 }));
        }
        foreach (var (ctl, points) in part.Controllers)
        {
            if (MidiController.IsReserved(ctl)) continue;
            foreach (var point in points)
            {
                long tick = ToTick(point.Beat);
                if (ctl is >= 0 and <= 127)
                {
                    int value = Math.Clamp((int)Math.Round(point.Value * 127), 0, 127);
                    events.Add(new Event(tick, 1, sequence++, new byte[] { (byte)(0xB0 | channel), (byte)ctl, (byte)value }));
                }
                else if (ctl == MidiController.PitchWheel)
                {
                    int raw = Math.Clamp((int)Math.Round((Math.Clamp(point.Value, -1f, 1f) + 1f) * 8192), 0, 16383);
                    events.Add(new Event(tick, 1, sequence++, new byte[] { (byte)(0xE0 | channel), (byte)(raw & 0x7F), (byte)((raw >> 7) & 0x7F) }));
                }
                else if (ctl == MidiController.ChannelPressure)
                {
                    int value = Math.Clamp((int)Math.Round(point.Value * 127), 0, 127);
                    events.Add(new Event(tick, 1, sequence++, new byte[] { (byte)(0xD0 | channel), (byte)value }));
                }
            }
        }
        long endTick = events.Count == 0 ? 0 : events.Max(e => e.Tick);
        return EncodeTrack(string.IsNullOrWhiteSpace(part.Name) ? "Part" : part.Name, events, endTick);
    }

    private static byte[] EncodeTrack(string name, List<Event> events, long endTick)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        byte[] trackName = Encoding.UTF8.GetBytes(name);
        WriteVarLen(writer, 0);
        writer.Write((byte)0xFF);
        writer.Write((byte)0x03);
        WriteVarLen(writer, trackName.Length);
        writer.Write(trackName);
        long previous = 0;
        foreach (var item in events.OrderBy(e => e.Tick).ThenBy(e => e.Order).ThenBy(e => e.Sequence))
        {
            long tick = Math.Max(previous, item.Tick);
            WriteVarLen(writer, tick - previous);
            writer.Write(item.Bytes);
            previous = tick;
        }
        long finalTick = Math.Max(previous, endTick);
        WriteVarLen(writer, finalTick - previous);
        writer.Write((byte)0xFF);
        writer.Write((byte)0x2F);
        writer.Write((byte)0x00);
        writer.Flush();
        return stream.ToArray();
    }

    private static long ToTick(double beat)
    {
        if (double.IsNaN(beat) || double.IsNegativeInfinity(beat)) return 0;
        if (double.IsPositiveInfinity(beat)) throw new InvalidDataException("MIDI event position is out of range.");
        double tick = Math.Round(Math.Max(0, beat) * Ppq);
        if (tick > long.MaxValue) throw new InvalidDataException("MIDI event position is out of range.");
        return (long)tick;
    }

    private static void WriteVarLen(BinaryWriter writer, long value)
    {
        if (value < 0 || value > MaxVariableLength)
            throw new InvalidDataException("MIDI event spacing exceeds the Standard MIDI File limit.");
        uint v = (uint)value;
        Span<byte> buffer = stackalloc byte[4];
        int index = 3;
        buffer[index] = (byte)(v & 0x7F);
        while ((v >>= 7) > 0) buffer[--index] = (byte)((v & 0x7F) | 0x80);
        writer.Write(buffer[index..]);
    }

    private static void WriteU16(BinaryWriter writer, int value)
    {
        writer.Write((byte)((value >> 8) & 0xFF));
        writer.Write((byte)(value & 0xFF));
    }

    private static void WriteU32(BinaryWriter writer, int value)
    {
        writer.Write((byte)((value >> 24) & 0xFF));
        writer.Write((byte)((value >> 16) & 0xFF));
        writer.Write((byte)((value >> 8) & 0xFF));
        writer.Write((byte)(value & 0xFF));
    }
}
