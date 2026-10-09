// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Browser preset audition: resolves a row (factory preset, saved .notapreset, drum kit, bare
// built-in device) into an offline render plan — the chain to build on an IAuditionRig and the
// phrase to play through it. Instruments get a phrase that suits the sound (a pad holds a
// chord, a bass plays a riff), picked from the preset's category and the device; kits (Drum
// Rack, Nota Rhythm) play a groove on their own pads (PresetAudition.Kits.cs); MIDI effects
// play a phrase that shows what they do into a plain keys patch; audio effects process a demo
// track — a part or a genre loop (PresetAudition.Tracks.cs), chosen per device unless the
// user picks — or the sample selected in the Files tab.

using System.Collections.Generic;
using System.IO;
using Nota.Application;

namespace Nota.Infrastructure;

public sealed partial class PresetAudition : IPresetAudition
{
    private const double Bpm = 120;
    private readonly FactoryPresetCatalog _factory;

    // Saved presets are re-read only when the file changes.
    private readonly Dictionary<string, (DateTime Stamp, PresetDocument Doc)> _userDocs = new(StringComparer.Ordinal);

    private readonly string _defaultKit;

    public PresetAudition(IFactoryPresets factory, IDrumKits? kits = null)
    {
        _factory = factory as FactoryPresetCatalog
            ?? throw new ArgumentException("Preset audition needs the built-in factory catalog.", nameof(factory));
        _defaultKit = kits?.DefaultRhythmKit ?? "kompakt";
    }

    public AuditionPlan? Plan(AuditionSubject s, string trackId, string? samplePath, out string reason)
    {
        reason = "";
        PresetDocument? doc;
        string category = "", keyBase;
        switch (s.Kind)
        {
            case AuditionSubjectKind.Preset when s.Path.StartsWith("factory:", StringComparison.Ordinal):
            {
                string id = s.Path["factory:".Length..];
                doc = _factory.Document(id);
                category = _factory.Info(id)?.Category ?? "";
                keyBase = s.Path;
                break;
            }
            case AuditionSubjectKind.Preset when s.Path.EndsWith(PresetService.Extension, StringComparison.OrdinalIgnoreCase):
            {
                doc = LoadUser(s.Path, out var stamp);
                keyBase = $"{s.Path}@{stamp.Ticks}";
                break;
            }
            case AuditionSubjectKind.Preset when s.Path.StartsWith("kit:", StringComparison.Ordinal):
                return KitPlan(s.Path, s.Path[4..], rhythm: false, out reason);
            case AuditionSubjectKind.Preset when s.Path.StartsWith("rhythmkit:", StringComparison.Ordinal):
                return KitPlan(s.Path, s.Path[10..], rhythm: true, out reason);
            case AuditionSubjectKind.Preset:
                reason = "Preset not found.";
                return null;
            // A fresh Drum Rack / Nota Rhythm is heard with the kit a new Rhythm starts with.
            case AuditionSubjectKind.Instrument when s.BuiltinKind is 4 or 12:
                return KitPlan($"device:inst:{s.BuiltinKind}:{_defaultKit}", _defaultKit, rhythm: s.BuiltinKind == 12, out reason);
            case AuditionSubjectKind.Instrument:
                doc = new PresetDocument { Type = "builtin-instrument", BuiltinKind = s.BuiltinKind };
                keyBase = $"device:inst:{s.BuiltinKind}";
                break;
            case AuditionSubjectKind.AudioEffect:
                doc = new PresetDocument { Type = "builtin-effect", BuiltinKind = s.BuiltinKind };
                keyBase = $"device:fx:{s.BuiltinKind}";
                break;
            case AuditionSubjectKind.MidiEffect:
                doc = new PresetDocument { Type = "builtin-midi-effect", BuiltinKind = s.BuiltinKind };
                keyBase = $"device:midi:{s.BuiltinKind}";
                break;
            default:
                return null;
        }
        if (doc is null) { reason = "Preset not found."; return null; }

        var named = doc.NamedParams ?? new Dictionary<string, float>();
        switch (doc.Type)
        {
            case "builtin-instrument":
            {
                int kind = doc.BuiltinKind;
                if (kind is 3 or 4) { reason = "Racks play what their chains hold — nothing to preview on its own."; return null; }
                var phrase = PhraseFor(kind, category, s.Name);
                return new AuditionPlan(keyBase, DeviceName(doc, kind, instrument: true), phrase.Label, false, "", "",
                    rig =>
                    {
                        if (!rig.SetInstrument(kind)) return false;
                        foreach (var (id, v) in named)
                            if (!InstrumentView.IsViewParam(id)) rig.InstrumentParam(id, v);
                        // A Nota Grain factory preset is heard on its own source.
                        if (kind == 10 && !string.IsNullOrEmpty(doc.GrainSource))
                        {
                            var src = Grain.GrainSources.ById(doc.GrainSource);
                            var path = Grain.GrainSourceLibrary.Ensure(doc.GrainSource);
                            if (src is null || path is null || !rig.SetSamplerSample(path, src.Root)) return false;
                        }
                        return true;
                    },
                    phrase.Notes, Bpm, phrase.Beats, phrase.Tail, phrase.Rolling);
            }
            case "builtin-midi-effect":
            {
                int kind = doc.BuiltinKind;
                var phrase = MidiPhrase(kind);
                return new AuditionPlan(keyBase, DeviceName(doc, kind, midi: true), phrase.Label, false, "", "",
                    rig =>
                    {
                        if (!rig.SetInstrument(0)) return false;
                        foreach (var (id, v) in MidiHostPatch) rig.InstrumentParam(id, v);
                        int mi = rig.AddMidiEffect(kind);
                        if (mi < 0) return false;
                        foreach (var (name, v) in named) if (name != "View") rig.MidiParam(mi, name, v);
                        return true;
                    },
                    phrase.Notes, Bpm, phrase.Beats, phrase.Tail, true);
            }
            case "builtin-effect":
            {
                int kind = doc.BuiltinKind;
                if (kind == 5) { reason = "The Effect Rack plays what its chains hold — nothing to preview on its own."; return null; }
                string id = trackId == AuditionTrackIds.Auto ? AutoTrack(kind) : trackId;
                if (id == AuditionTrackIds.Sample && string.IsNullOrEmpty(samplePath)) id = AutoTrack(kind);
                var track = id == AuditionTrackIds.Sample ? null : TrackById(id) ?? TrackById(AutoTrack(kind))!;
                string key = track is null ? $"{keyBase}|sample:{samplePath}" : $"{keyBase}|{track.Info.Id}";
                string name = track?.Info.Name ?? Path.GetFileNameWithoutExtension(samplePath)!;
                return new AuditionPlan(key, DeviceName(doc, kind), $"through {name}", true, track?.Info.Id ?? AuditionTrackIds.Sample, name,
                    rig =>
                    {
                        bool ok = track is null ? rig.SetSourceFile(samplePath!, 8) : UseTrack(rig, track);
                        if (!ok) return false;
                        int di = rig.AddDevice(kind);
                        if (di < 0) return false;
                        foreach (var (pn, v) in named) rig.DeviceParam(di, pn, v);
                        // EQ-3 presets from before Range was added were made in the Classic law.
                        if (kind == 16 && doc.NamedParams is { Count: > 0 } && !named.ContainsKey("Range")) rig.DeviceParam(di, "Range", 0);
                        return true;
                    },
                    Array.Empty<AuditionNote>(), track?.Bpm ?? Bpm, 0, 4.0, true);
            }
            default:
                reason = "Plug-in presets can't be previewed — load the plug-in to hear them.";
                return null;
        }
    }

    private PresetDocument? LoadUser(string path, out DateTime stamp)
    {
        stamp = DateTime.MinValue;
        try
        {
            stamp = File.GetLastWriteTimeUtc(path);
            if (_userDocs.TryGetValue(path, out var hit) && hit.Stamp == stamp) return hit.Doc;
            var doc = PresetService.Load(path);
            _userDocs[path] = (stamp, doc);
            return doc;
        }
        catch { return null; }
    }

    // --- device names ------------------------------------------------------------

    private static readonly Dictionary<int, string> InstrumentNames = new()
    {
        [0] = "Nota Synth", [1] = "Nota Sampler", [2] = "Nota Physical", [5] = "Nota Aurora", [6] = "Nota Volt",
        [7] = "Nota Bass", [8] = "Nota Pendulum", [9] = "Nota Operator", [10] = "Nota Grain", [11] = "Nota Flux",
        [12] = "Nota Rhythm", [13] = "Nota Monolith", [14] = "Nota Pentad", [15] = "Nota Consort", [16] = "Nota Keys",
    };
    private static readonly Dictionary<int, string> MidiNames = new()
    {
        [0] = "Nota Arp", [1] = "Nota Chord", [2] = "Nota Scale", [3] = "Nota Length", [4] = "Nota Velocity", [5] = "Nota Random",
    };
    private static readonly Dictionary<int, string> EffectNames = new()
    {
        [0] = "Nota EQ-8", [1] = "Nota Compressor", [2] = "Nota Reverb", [3] = "Nota Delay", [4] = "Nota Utility",
        [6] = "Nota Valve", [7] = "Nota Auto Filter", [8] = "Nota Vintage", [9] = "Nota Orbit", [10] = "Nota Auto Shift",
        [11] = "Nota Beat Repeat", [12] = "Nota Crush", [13] = "Nota Dynamic EQ-8", [14] = "Nota Ceiling",
        [15] = "Nota Strata", [16] = "Nota EQ-3", [17] = "Nota Forge", [18] = "Nota Level", [19] = "Nota Shutter",
        [20] = "Nota Chamber", [21] = "Nota Prism", [22] = "Nota Lens", [23] = "Nota Flanger", [24] = "Nota Phaser",
        [25] = "Nota Chorus",
    };

    private static string DeviceName(PresetDocument doc, int kind, bool instrument = false, bool midi = false)
    {
        if (!string.IsNullOrEmpty(doc.DeviceName)) return doc.DeviceName;
        var map = instrument ? InstrumentNames : midi ? MidiNames : EffectNames;
        return map.TryGetValue(kind, out var n) ? n : "";
    }

    // --- phrases -------------------------------------------------------------------
    // Two bars at 120 BPM unless a sound needs longer (a slow pad). Pitches are MIDI notes.

    private sealed record Phrase(string Label, AuditionNote[] Notes, double Beats, double Tail, bool Rolling = false);

    private static AuditionNote N(double at, double len, int pitch, float vel = 0.75f) => new(at, len, pitch, vel);

    private static IEnumerable<AuditionNote> Chord(double at, double len, float vel, params int[] pitches)
    {
        foreach (int p in pitches) yield return N(at, len, p, vel);
    }

    private static AuditionNote[] Cat(params IEnumerable<AuditionNote>[] parts)
    {
        var l = new List<AuditionNote>();
        foreach (var p in parts) l.AddRange(p);
        return l.ToArray();
    }

    // Cmaj9 → Am9, voiced wide, held long enough for a slow attack to bloom.
    private static readonly Phrase Pad = new("pad chord", Cat(
        Chord(0, 3.8, 0.7f, 48, 55, 64, 71, 74),
        Chord(4, 3.8, 0.7f, 45, 52, 60, 67, 71)), 8, 4.5);

    // A syncopated riff with an octave jump and two overlapping (glide / legato) moves.
    private static readonly Phrase BassRiff = new("bass riff", new[]
    {
        N(0, 0.45, 36, 0.95f), N(0.75, 0.25, 36, 0.7f), N(1, 0.45, 48, 0.85f), N(1.5, 0.5, 46, 0.75f),
        N(2, 0.7, 43, 0.9f), N(2.75, 0.5, 41, 0.75f), N(3.5, 0.5, 39, 0.8f),
        N(4, 0.45, 36, 0.95f), N(4.75, 0.25, 36, 0.7f), N(5, 0.45, 48, 0.85f), N(5.5, 0.6, 43, 0.75f),
        N(6, 0.95, 44, 0.9f), N(6.9, 0.6, 43, 0.8f), N(7.5, 0.45, 36, 0.85f),
    }, 8, 1.5);

    // A melody with a legato run (overlaps) and a long last note for vibrato / filter motion.
    private static readonly Phrase Lead = new("lead line", new[]
    {
        N(0, 0.5, 60), N(0.5, 0.5, 63), N(1, 1.0, 67, 0.85f), N(2, 0.3, 70), N(2.25, 0.3, 72), N(2.5, 0.3, 70),
        N(2.75, 1.25, 67, 0.8f), N(4, 0.5, 65), N(4.5, 0.55, 67), N(5, 0.55, 70), N(5.5, 2.3, 72, 0.9f),
    }, 8, 2.0);

    // Chords with a melody on top, then a stab — shows attack, body and release.
    private static readonly Phrase Keys = new("keys", Cat(
        Chord(0, 1.4, 0.75f, 48, 60, 64, 67, 71), new[] { N(1.5, 0.45, 76, 0.7f), N(2, 0.45, 74, 0.65f), N(2.5, 0.9, 72, 0.7f) },
        Chord(4, 1.4, 0.75f, 45, 57, 60, 64, 67), new[] { N(5.5, 0.45, 72, 0.7f), N(6, 0.45, 71, 0.65f) },
        Chord(6.5, 1.2, 0.8f, 43, 55, 59, 62, 65)), 8, 2.5);

    // Rising and falling arpeggio in the upper register, finishing on a chord.
    private static readonly Phrase Pluck = new("arpeggio", Cat(
        new[]
        {
            N(0, 0.45, 60), N(0.5, 0.45, 64), N(1, 0.45, 67), N(1.5, 0.45, 72), N(2, 0.45, 76, 0.85f), N(2.5, 0.45, 72),
            N(3, 0.45, 67), N(3.5, 0.45, 64), N(4, 0.45, 57), N(4.5, 0.45, 60), N(5, 0.45, 64), N(5.5, 0.45, 69, 0.85f),
        },
        Chord(6, 1.5, 0.8f, 60, 64, 67, 72)), 8, 3.0);

    // A slow, low drone with a fifth and a late ninth — lets textures and sweeps evolve.
    private static readonly Phrase Texture = new("texture", Cat(
        Chord(0, 6.5, 0.7f, 36, 43, 55), new[] { N(3, 3.5, 62, 0.55f) }), 8, 4.5);

    // A held chord for arp / sequencer synths: they make the rhythm themselves.
    private static readonly Phrase Held = new("held chord", Cat(Chord(0, 7.5, 0.8f, 48, 55, 60, 63, 67)), 8, 3.0, Rolling: true);

    // GM drum map (Nota Rhythm): kick 36, snare 38, clap 39, rim 37, hats 42 / 46, toms 45 / 41.
    private static readonly Phrase Groove = new("groove", Cat(
        new[] { N(0, 0.25, 36, 1f), N(1.75, 0.25, 36, 0.8f), N(2, 0.25, 36, 0.95f), N(4, 0.25, 36, 1f), N(5.75, 0.25, 36, 0.8f), N(6, 0.25, 36, 0.95f) },
        new[] { N(1, 0.25, 38, 0.9f), N(3, 0.25, 38, 0.9f), N(5, 0.25, 38, 0.9f), N(7, 0.25, 39, 0.9f), N(3.75, 0.25, 37, 0.6f) },
        new[] { N(6.5, 0.25, 45, 0.8f), N(6.75, 0.25, 41, 0.8f), N(7.5, 0.25, 46, 0.7f) },
        Hats()), 8, 1.5);

    private static IEnumerable<AuditionNote> Hats()
    {
        for (int i = 0; i < 12; i++) yield return N(i * 0.5, 0.25, i == 7 ? 46 : 42, i % 2 == 0 ? 0.75f : 0.5f);
    }

    private static Phrase PhraseFor(int kind, string category, string name)
    {
        if (kind == 12) return Groove;
        if (kind is 8 or 15) return Held;   // Pendulum, Consort: generative, rolling
        string c = (category + " " + name).ToLowerInvariant();
        bool Has(params string[] words) { foreach (var w in words) if (c.Contains(w)) return true; return false; }
        if (Has("sequence", "arp", "generative")) return Held;
        if (Has("bass", "sub", "acid", "growl", "wobble", "808")) return BassRiff;
        if (Has("pad", "ambient", "ensemble", "string", "choir", "bowed", "waterfall", "swell")) return Pad;
        if (Has("texture", "drone", "sweep", " fx", "motion", "noise")) return Texture;
        if (Has("lead", "brass", "solo", "whistle")) return Lead;
        if (Has("bell", "mallet", "pluck", "harp", "music box", "percussion", "perc", "marimba", "vibra")) return Pluck;
        if (Has("keys", "piano", "organ", "electric", "rhodes")) return Keys;
        return kind switch
        {
            7 => BassRiff,           // Nota Bass
            13 => Lead,              // Nota Monolith (mono)
            2 => Pluck,              // Nota Physical (struck bodies)
            10 => Texture,           // Nota Grain
            _ => Keys,
        };
    }

    // --- MIDI effects ------------------------------------------------------------------
    // Played into a plain Nota Synth keys patch — short enough that note length and velocity read.

    private static readonly (string Id, float Value)[] MidiHostPatch =
    {
        ("wave", 0.667f), ("attack", 0f), ("decay", 0.55f), ("sustain", 0.5f), ("release", 0.35f),
        ("cutoff", 0.7f), ("resonance", 0.1f), ("filenv", 0.4f), ("velamp", 0.8f), ("gain", 0.8f),
    };

    private static Phrase MidiPhrase(int kind) => kind switch
    {
        0 => new("held chord", Cat(Chord(0, 3.9, 0.8f, 48, 55, 60, 64), Chord(4, 3.9, 0.8f, 45, 52, 57, 60)), 8, 2.0),
        1 => new("melody", Seq(0.9f, 1.0, 0.8f, 60, 62, 64, 65, 67, 69, 71, 72), 8, 2.0),
        2 => new("chromatic run", Seq(0.45f, 0.5, 0.75f, 60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 71, 69, 67), 8, 1.5),
        3 => new("mixed lengths", new[]
        {
            N(0, 0.2, 60), N(0.5, 0.2, 64), N(1, 1.5, 67), N(3, 0.1, 72), N(3.25, 0.1, 72), N(3.5, 0.4, 71),
            N(4, 2.0, 69), N(6, 0.25, 67), N(6.5, 0.25, 64), N(7, 0.9, 60),
        }, 8, 1.5),
        4 => new("velocity ramp", VelocityRamp(), 8, 1.5),
        _ => new("repeating line", Seq(0.45f, 0.5, 0.75f, 60, 64, 67, 72, 67, 64, 60, 64, 67, 72, 67, 64, 60, 67, 72, 76), 8, 1.5),
    };

    private static AuditionNote[] Seq(float len, double step, float vel, params int[] pitches)
    {
        var n = new AuditionNote[pitches.Length];
        for (int i = 0; i < n.Length; i++) n[i] = N(i * step, len * step, pitches[i], vel);
        return n;
    }

    private static AuditionNote[] VelocityRamp()
    {
        var n = new List<AuditionNote>();
        int[] line = { 60, 64, 67, 64 };
        for (int i = 0; i < 16; i++)
        {
            float v = 0.15f + 0.85f * (i < 8 ? i / 7f : (15 - i) / 7f);
            n.Add(N(i * 0.5, 0.4, line[i % 4], v));
        }
        return n.ToArray();
    }
}
