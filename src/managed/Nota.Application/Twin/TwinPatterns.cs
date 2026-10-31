// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Twin's sequencer patterns from the app's side (specs/055 US8): the pattern block of the
// instrument's state (a mirror of TwinSeq.cpp's layout), a text form of a pattern's steps for
// presets and MCP, and "To clip" — a pattern written out as an ordinary MIDI clip, its motion
// lanes as automation.
//
// Text form, one row per step: notes separated by spaces, then optional ":velocity" (1..127) and
// ":gate" ("50%" or "tie"); "." is a rest. E.g. "C3 E3 G3:100:50%", ".", "A2:90:tie".

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Nota.Application.Twin;

public static class TwinPatterns
{
    public const int Patterns = 8, Steps = 64, StepNotes = 6, Lanes = 4;
    public const int GateTie = 101;
    public const ushort MotionUnset = 0xFFFF;
    private const uint Magic = 0x31505754; // "TWP1"
    private const uint StateMagic = 0x314E5754; // "TWN1"

    public sealed class Step
    {
        public List<int> Notes = new();
        public int Velocity = 100;
        public int Gate = 50; // 1..100 %, GateTie
        public bool IsRest => Notes.Count == 0;
    }
    public sealed class Lane
    {
        public int Target = -1; // param index
        public bool Smooth;
        public ushort[] Values = Enumerable.Repeat(MotionUnset, Steps).ToArray();
    }
    public sealed class Pattern
    {
        public int Length = 16;
        public Step[] Steps = Enumerable.Range(0, TwinPatterns.Steps).Select(_ => new Step()).ToArray();
        public Lane[] Lanes = Enumerable.Range(0, TwinPatterns.Lanes).Select(_ => new Lane()).ToArray();
    }

    // ---- the pattern block ------------------------------------------------------------------

    /// <summary>The patterns inside a Twin state blob ("TWN1"), or null when it carries none.</summary>
    public static Pattern[]? FromState(byte[] state)
    {
        if (state.Length < 8 || BitConverter.ToUInt32(state, 0) != StateMagic) return null;
        int at = 8 + 4 * (int)BitConverter.ToUInt32(state, 4);
        if (state.Length < at + 4) return null;
        int bytes = (int)BitConverter.ToUInt32(state, at);
        return state.Length >= at + 4 + bytes ? Decode(state[(at + 4)..(at + 4 + bytes)]) : null;
    }

    public static Pattern[]? Decode(byte[] b)
    {
        if (b.Length < 4 || BitConverter.ToUInt32(b, 0) != Magic) return null;
        int p = 4;
        int U8() => p < b.Length ? b[p++] : 0;
        ushort U16() { int lo = U8(), hi = U8(); return (ushort)(lo | hi << 8); }
        var set = new Pattern[Patterns];
        for (int i = 0; i < Patterns; i++)
        {
            var pat = new Pattern { Length = Math.Clamp(U8(), 1, Steps) };
            foreach (var s in pat.Steps)
            {
                int n = Math.Min((int)U8(), StepNotes);
                var notes = new int[StepNotes];
                for (int k = 0; k < StepNotes; k++) notes[k] = U8();
                s.Notes = notes.Take(n).ToList();
                s.Velocity = U8();
                s.Gate = U8();
            }
            foreach (var l in pat.Lanes)
            {
                l.Target = (short)U16();
                l.Smooth = U8() != 0;
                for (int k = 0; k < Steps; k++) l.Values[k] = U16();
            }
            set[i] = pat;
        }
        return p <= b.Length ? set : null;
    }

    public static byte[] Encode(IReadOnlyList<Pattern> set)
    {
        var b = new List<byte>();
        void U8(int v) => b.Add((byte)v);
        void U16(int v) { b.Add((byte)v); b.Add((byte)(v >> 8)); }
        b.AddRange(BitConverter.GetBytes(Magic));
        for (int i = 0; i < Patterns; i++)
        {
            var pat = i < set.Count ? set[i] : new Pattern();
            U8(pat.Length);
            foreach (var s in pat.Steps)
            {
                U8(s.Notes.Count);
                for (int k = 0; k < StepNotes; k++) U8(k < s.Notes.Count ? s.Notes[k] : 0);
                U8(Math.Clamp(s.Velocity, 1, 127));
                U8(Math.Clamp(s.Gate, 1, GateTie));
            }
            foreach (var l in pat.Lanes)
            {
                U16(l.Target);
                U8(l.Smooth ? 1 : 0);
                foreach (var v in l.Values) U16(v);
            }
        }
        return b.ToArray();
    }

    /// <summary>A Twin state blob that carries only patterns (no params): what a preset stores.</summary>
    public static byte[] StateWithPatterns(IReadOnlyList<Pattern> set)
    {
        var block = Encode(set);
        var b = new List<byte>();
        b.AddRange(BitConverter.GetBytes(StateMagic));
        b.AddRange(BitConverter.GetBytes(0u));
        b.AddRange(BitConverter.GetBytes((uint)block.Length));
        b.AddRange(block);
        return b.ToArray();
    }

    // ---- text ----------------------------------------------------------------------------------

    private static readonly string[] Names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
    public static string NoteName(int n) => $"{Names[n % 12]}{n / 12 - 1}";
    public static int ParseNote(string s)
    {
        s = s.Trim();
        int i = 1;
        if (s.Length > 1 && (s[1] == '#' || s[1] == 'b')) i = 2;
        int pc = Array.FindIndex(Names, x => string.Equals(x, s[..1], StringComparison.OrdinalIgnoreCase));
        if (pc < 0 || !int.TryParse(s[i..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int oct)) return -1;
        if (i == 2) pc += s[1] == '#' ? 1 : -1;
        int n = (oct + 1) * 12 + pc;
        return n is >= 0 and <= 127 ? n : -1;
    }

    public static string StepText(Step s)
    {
        if (s.IsRest) return ".";
        string gate = s.Gate >= GateTie ? "tie" : $"{s.Gate}%";
        return $"{string.Join(' ', s.Notes.Select(NoteName))}:{s.Velocity}:{gate}";
    }

    /// <summary>Parses one row; null and an error message when it can't.</summary>
    public static Step? ParseStep(string row, out string error)
    {
        error = "";
        row = row.Trim();
        var s = new Step();
        if (row == "." || row.Length == 0) return s;
        var parts = row.Split(':');
        foreach (var t in parts[0].Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int n = ParseNote(t);
            if (n < 0) { error = $"note '{t}'"; return null; }
            if (s.Notes.Count < StepNotes && !s.Notes.Contains(n)) s.Notes.Add(n);
        }
        if (parts.Length > 1 && parts[1].Length > 0)
        {
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) { error = $"velocity '{parts[1]}'"; return null; }
            s.Velocity = Math.Clamp(v, 1, 127);
        }
        if (parts.Length > 2 && parts[2].Length > 0)
        {
            string g = parts[2].Trim().TrimEnd('%');
            if (g.Equals("tie", StringComparison.OrdinalIgnoreCase)) s.Gate = GateTie;
            else if (int.TryParse(g, NumberStyles.Integer, CultureInfo.InvariantCulture, out int gv)) s.Gate = Math.Clamp(gv, 1, 100);
            else { error = $"gate '{parts[2]}'"; return null; }
        }
        return s;
    }

    // ---- editing (Twin::Act, contracts/c-abi.md) -------------------------------------------------

    public enum Act
    {
        SelectPattern = 0, SetLength, ClearStep, AddStepNote, RemoveStepNote, SetStepVelocity, SetStepGate,
        StepRecordArm, StepRecordAdvance, ClearPattern, CopyPattern, MotionTarget, MotionMode, MotionSet,
        MotionRecordArm, ClearMotion, StepRecordPoll,
    }

    public static void Do(IAudioEngine e, int trackId, Act a, int iarg = 0, float farg = 0f) => e.InstrumentAction(trackId, (int)a, iarg, farg);

    /// <summary>Replaces a pattern's steps with `rows` (the text form; the length becomes the row
    /// count) — one undo step. Returns "" or what it could not read.</summary>
    public static string WritePattern(IAudioEngine e, int trackId, int pattern, IReadOnlyList<string> rows)
    {
        if (rows.Count is < 1 or > Steps) return $"{rows.Count} rows — a pattern has 1..{Steps}";
        var steps = new List<Step>();
        for (int i = 0; i < rows.Count; i++)
        {
            var st = ParseStep(rows[i], out var err);
            if (st is null) return $"row {i + 1}: {err}";
            steps.Add(st);
        }
        e.BeginUndoGroup();
        try
        {
            Do(e, trackId, Act.SelectPattern, pattern);
            Do(e, trackId, Act.SetLength, steps.Count);
            for (int i = 0; i < steps.Count; i++)
            {
                Do(e, trackId, Act.ClearStep, i);
                foreach (var n in steps[i].Notes) Do(e, trackId, Act.AddStepNote, i << 8 | n, steps[i].Velocity / 127f);
                Do(e, trackId, Act.SetStepVelocity, i, steps[i].Velocity / 127f);
                Do(e, trackId, Act.SetStepGate, i, steps[i].Gate >= GateTie ? 1.01f : steps[i].Gate / 100f);
            }
        }
        finally
        {
            e.EndUndoGroup();
        }
        return "";
    }

    // ---- To clip -------------------------------------------------------------------------------

    private static readonly double[] RateBeats = { 1.0, 0.5, 1.0 / 3.0, 0.25, 1.0 / 6.0, 0.125 };

    /// <summary>Writes a pattern of the track's Twin as a MIDI clip at `startBeat` (notes, velocities,
    /// gates, ties and the swing) and its motion lanes as automation over the clip, then turns the
    /// sequencer off — one undo step. Returns the clip index, or -1.</summary>
    public static int ToClip(IAudioEngine e, int trackId, int pattern, double startBeat)
    {
        if (e.TrackInstrumentKind(trackId) != TwinMatrix.Kind) return -1;
        var set = FromState(e.GetPluginState(trackId, -1));
        if (set is null || pattern < 0 || pattern >= Patterns) return -1;
        var pat = set[pattern];
        var at = new Dictionary<string, int>();
        int pc = e.PluginParamCount(trackId, -1);
        for (int i = 0; i < pc; i++) at[e.PluginParamId(trackId, -1, i)] = i;
        float G(string id) => e.PluginParamGet(trackId, -1, at[id]);
        double sb = RateBeats[Math.Clamp((int)Math.Round(G("seq.rate") * 5), 0, 5)];
        double swing = G("seq.swing") * 0.5 * sb;
        double On(int k) => k * sb + ((k & 1) == 1 ? swing : 0);

        var notes = new List<NotaNote>();
        for (int k = 0; k < pat.Length; k++)
        {
            var s = pat.Steps[k];
            foreach (var n in s.Notes)
            {
                // A tie runs on through the following steps that hold the same note.
                if (k > 0 && pat.Steps[k - 1].Gate >= GateTie && pat.Steps[k - 1].Notes.Contains(n)) continue;
                int last = k;
                while (pat.Steps[last].Gate >= GateTie && last + 1 < pat.Length && pat.Steps[last + 1].Notes.Contains(n)) last++;
                var end = pat.Steps[last];
                double off = On(last) + (end.Gate >= GateTie ? sb : end.Gate * 0.01 * sb);
                notes.Add(new NotaNote(n, On(k), off - On(k), s.Velocity / 127f));
            }
        }
        double length = Math.Max(sb, pat.Length * sb);
        e.BeginUndoGroup();
        try
        {
            int clip = e.AddMidiClip(trackId, startBeat, length);
            if (clip < 0) return -1;
            e.SetClipNotes(trackId, clip, notes.ToArray());
            foreach (var lane in pat.Lanes)
            {
                if (lane.Target < 0 || lane.Target >= pc || lane.Values.Take(pat.Length).All(v => v == MotionUnset)) continue;
                int li = e.AddPluginAutomationLane(trackId, -1, e.PluginParamId(trackId, -1, lane.Target));
                if (li < 0) continue;
                var pts = new List<AutomationPoint>();
                for (int k = 0; k < pat.Length; k++)
                {
                    if (lane.Values[k] == MotionUnset) continue;
                    float v = lane.Values[k] / 65534f;
                    pts.Add(new AutomationPoint(startBeat + On(k), v));
                    if (!lane.Smooth) pts.Add(new AutomationPoint(startBeat + On(k) + sb * 0.999, v));
                }
                e.SetAutomationPoints(trackId, li, pts.ToArray());
            }
            int seqOn = at["seq.on"];
            int g = e.BeginMixGesture(MixTarget.PluginParam(trackId, -1, seqOn)); // undoes with the clip
            e.PluginParamSet(trackId, -1, seqOn, 0f);
            e.EndMixGesture(g);
            return clip;
        }
        finally
        {
            e.EndUndoGroup();
        }
    }
}
