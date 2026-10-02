// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// What the phones are told, built on the UI tick. A Snapshot computes each piece at most once
// per tick and only when some phone needs it; each phone is sent a piece only when it differs
// from the last one it got. The project (tracks, key, sections) is re-read a few times a second,
// the transport every tick while playing, a screen's live state (mixer meters, XY values, knobs,
// clip states) at 15 Hz to the phones showing that screen.

using System.Globalization;
using System.Text.Json;
using Nota.Application;
using Nota.Application.Samples;

namespace Nota.Remote;

public sealed partial class RemoteHub
{
    private string? _projectJson;            // shared part of the project message, re-read every few ticks
    private List<TrackRow> _tracks = new();
    private readonly Dictionary<(int Track, int Scene), double> _slotStart = new();   // session launch beat (progress)

    internal sealed record TrackRow(int Id, string Name, string Color, string Kind, string Device, int Group, int Index);

    private sealed class Snapshot(RemoteHub hub, IRemoteHost host)
    {
        private string? _transport, _mixer, _session, _learn;
        private readonly Dictionary<int, string> _lit = new();
        private readonly Dictionary<int, NotaNote[]> _clipNotes = new();
        public readonly double Pos = hub._engine.PositionBeats;
        public readonly bool Playing = hub._engine.IsPlaying;

        public string Transport => _transport ??= hub.BuildTransport(host);
        public string Mixer => _mixer ??= hub.BuildMixer(host);
        public string Session => _session ??= hub.BuildSession(this);
        public string Learn => _learn ??= Json(w =>
        {
            w.WriteString("t", "learn");
            w.WriteBoolean("on", host.LearnArmed);
            if (host.LearnPendingName is { } p) w.WriteString("pend", p); else w.WriteNull("pend");
        });
        public string Lit(int track) => _lit.TryGetValue(track, out var s) ? s : _lit[track] = hub.BuildLit(this, track);
        public NotaNote[] ClipNotes(int track, int clip)
        {
            int key = track * 4096 + clip;
            return _clipNotes.TryGetValue(key, out var n) ? n : _clipNotes[key] = hub._engine.GetClipNotes(track, clip);
        }
    }

    private void Push(IRemoteHost host, Snapshot snap, RemoteClient c)
    {
        if (_projectJson is null || _tick % 6 == 0) RefreshProject(host);
        ResolveTrack(host, c);

        string project = _projectJson + $",\"access\":\"{(Access() == RemoteAccess.Control ? "control" : "play")}\",\"you\":{c.TrackId},\"follow\":{(c.Follow ? "true" : "false")}}}";
        if (project != c.SentProject) { c.Send(project); c.SentProject = project; c.SentScreenState = null; }

        string who = BuildWho(c);
        if (who != c.SentWho) { c.Send(who); c.SentWho = who; }

        if (snap.Transport != c.SentTransport) { c.Send(snap.Transport); c.SentTransport = snap.Transport; }
        if (snap.Learn != c.SentLearn) { c.Send(snap.Learn); c.SentLearn = snap.Learn; }

        string screen = c.Screen;
        if (screen is "Pads" or "Keys" && c.TrackId > 0)
        {
            string lit = snap.Lit(c.TrackId);
            if (lit != c.SentLit) { c.Send(lit); c.SentLit = lit; }
        }

        // A screen's live state at 15 Hz (every other UI tick); a new screen gets it at once.
        if (c.SentScreenState is not null && (_tick & 1) == 1) return;
        string? state = screen switch
        {
            "Mixer" => snap.Mixer,
            "Scenes" => snap.Session,
            "XY" => BuildXy(host, snap, c),
            "Macros" => BuildMacros(host, c),
            _ => null,
        };
        if (state is not null && state != c.SentScreenState) { c.Send(state); c.SentScreenState = state; }
        else if (state is null) c.SentScreenState = "";
    }

    // ---- project ------------------------------------------------------------------

    private void RefreshProject(IRemoteHost host)
    {
        var rows = new List<TrackRow>();
        int n = _engine.TrackCount;
        for (int i = 0; i < n; i++)
        {
            if (!_engine.TryGetTrackInfo(i, out var ti)) continue;
            string kind = ti.IsGroup ? "group" : ti.IsReturn ? "return" : !ti.IsInstrument ? "audio"
                : _engine.TrackInstrumentKind(ti.Id) is RemoteParams.DrumRackKind or RemoteParams.RhythmKind ? "drum" : "inst";
            string dev = kind switch
            {
                "audio" => "Audio",
                "group" => "Group",
                "return" => "Return",
                _ => RemoteParams.InstrumentName(_engine, ti.Id),
            };
            rows.Add(new TrackRow(ti.Id, _engine.GetTrackName(ti.Id), host.TrackColor(ti.Id), kind, dev, ti.GroupId, i));
        }
        _tracks = rows;

        _projectJson = Json(w =>
        {
            w.WriteString("t", "proj");
            w.WriteString("host", HostName);
            w.WriteBoolean("dark", host.DarkTheme);
            w.WriteStartArray("tracks");
            foreach (var r in rows)
            {
                w.WriteStartObject();
                w.WriteNumber("id", r.Id);
                w.WriteString("name", r.Name.Length > 0 ? r.Name : $"Track {r.Index + 1}");
                w.WriteString("color", r.Color);
                w.WriteString("kind", r.Kind);
                w.WriteString("dev", r.Device);
                w.WriteNumber("grp", r.Group);
                if (r.Kind == "drum") WritePads(w, r.Id);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            var key = MusicalKey.FromCode(host.ProjectKeyCode);
            if (key is { Mode: not KeyMode.Note } k)
            {
                w.WriteStartObject("key");
                w.WriteNumber("root", k.Tonic);
                w.WriteBoolean("minor", k.Mode == KeyMode.Minor);
                w.WriteString("name", k.Long);
                w.WriteEndObject();
            }
            else w.WriteNull("key");

            w.WriteNumber("scenes", _engine.SceneCount);
            w.WriteStartArray("sections");
            foreach (var (name, beat) in host.Sections)
            {
                w.WriteStartArray();
                w.WriteStringValue(name);
                w.WriteNumberValue(Math.Round(beat, 3));
                w.WriteEndArray();
            }
            w.WriteEndArray();
        });
        // Left open: Push appends the per-phone fields and the closing brace.
        _projectJson = _projectJson[..^1];
    }

    // The pads a drum track has: a Drum Rack's chains by trigger note, Nota Rhythm's eight voices.
    private void WritePads(Utf8JsonWriter w, int trackId)
    {
        w.WriteStartArray("pads");
        if (_engine.TrackInstrumentKind(trackId) == RemoteParams.RhythmKind)
        {
            for (int v = 0; v < RhythmModel.MidiNotes.Length; v++)
            {
                w.WriteStartArray();
                w.WriteNumberValue(RhythmModel.MidiNotes[v]);
                w.WriteStringValue(RhythmModel.VoiceNames[v]);
                w.WriteEndArray();
            }
        }
        else
        {
            int chains = _engine.RackChainCount(trackId);
            for (int ch = 0; ch < chains; ch++)
            {
                int note = _engine.RackChainTriggerNote(trackId, ch);
                if (note is < 0 or > 127) continue;
                w.WriteStartArray();
                w.WriteNumberValue(note);
                w.WriteStringValue(_engine.RackChainName(trackId, ch));
                w.WriteEndArray();
            }
        }
        w.WriteEndArray();
    }

    /// <summary>Keep the phone on a real track: a new phone starts on Nota's selection (or the
    /// first instrument), a deleted track hands over to its neighbour, Follow tracks Nota.</summary>
    private void ResolveTrack(IRemoteHost host, RemoteClient c)
    {
        if (_tracks.Count == 0) { c.TrackId = 0; return; }
        bool Playable(TrackRow r) => r.Kind is "drum" or "inst" or "audio";
        if (c.Follow && host.SelectedTrackId > 0 && host.SelectedTrackId != c.TrackId
            && _tracks.FirstOrDefault(r => r.Id == host.SelectedTrackId) is { } sel && Playable(sel))
        {
            c.TrackId = sel.Id;
            _devicesDirty = true;
            return;
        }
        int idx = _tracks.FindIndex(r => r.Id == c.TrackId);
        if (idx >= 0) { c.LastTrackIndex = idx; return; }

        bool wasDeleted = c.TrackId > 0;
        TrackRow? pick = null;
        if (wasDeleted)
            pick = _tracks.Skip(Math.Min(c.LastTrackIndex, _tracks.Count - 1)).FirstOrDefault(Playable)
                ?? _tracks.LastOrDefault(Playable);
        else
            pick = _tracks.FirstOrDefault(r => r.Id == host.SelectedTrackId && r.Kind is "drum" or "inst")
                ?? _tracks.FirstOrDefault(r => r.Kind is "drum" or "inst")
                ?? _tracks.FirstOrDefault(Playable);
        c.TrackId = pick?.Id ?? 0;
        _devicesDirty = true;
        if (wasDeleted && pick is not null) Toast(c, $"That track was deleted in Nota. Now playing {pick.Name}.");
    }

    private string BuildWho(RemoteClient self)
    {
        var groups = _clients.Values.Where(c => c.Authenticated && c != self && c.TrackId > 0)
            .GroupBy(c => c.TrackId).OrderBy(g => g.Key);
        return Json(w =>
        {
            w.WriteString("t", "who");
            w.WriteStartObject("p");
            foreach (var g in groups)
            {
                w.WriteStartArray(g.Key.ToString(CultureInfo.InvariantCulture));
                foreach (var c in g) w.WriteStringValue(ShortName(c.Name));
                w.WriteEndArray();
            }
            w.WriteEndObject();
        });
    }

    // ---- transport -------------------------------------------------------------------

    private string BuildTransport(IRemoteHost host) => Json(w =>
    {
        w.WriteString("t", "tp");
        w.WriteBoolean("play", _engine.IsPlaying);
        w.WriteBoolean("rec", host.Recording);
        w.WriteBoolean("loop", host.LoopOn);
        w.WriteBoolean("met", host.MetronomeOn);
        w.WriteNumber("pos", Math.Round(_engine.PositionBeats, 3));
        w.WriteNumber("bpm", Math.Round(_engine.Bpm, 2));
        w.WriteNumber("ls", Math.Round(_engine.LoopStart, 3));
        w.WriteNumber("le", Math.Round(_engine.LoopEnd, 3));
        w.WriteBoolean("can", _engine.CanUndo);
    });

    // ---- sounding notes ----------------------------------------------------------------
    // The pads and keys light for what the track is playing: its arrangement MIDI clips and a
    // playing Session slot, plus what other phones hold on it. (Own touches light on the phone.)

    private string BuildLit(Snapshot snap, int trackId)
    {
        var set = new SortedSet<int>();
        if (snap.Playing && TrackInfo(trackId) is { } ti && ti.IsInstrument)
        {
            if (_engine.ArrangementActive)
                for (int ci = 0; ci < ti.ClipCount; ci++)
                {
                    if (!_engine.TryGetClipInfo(trackId, ci, out var info) || !info.IsMidi || info.Active == 0) continue;
                    double local = snap.Pos - info.StartBeat;
                    if (local < 0 || local >= info.LengthBeats) continue;
                    foreach (var n in snap.ClipNotes(trackId, ci))
                        if (local >= n.StartBeat && local < n.StartBeat + n.LengthBeats) set.Add(n.Pitch);
                }
            int scenes = _engine.SceneCount;
            for (int s = 0; s < scenes; s++)
            {
                if (_engine.SessionSlotState(trackId, s) != 3) continue;
                double len = _engine.SessionSlotLength(trackId, s);
                if (len <= 0) continue;
                double start = SlotStart(trackId, s, snap.Pos);
                double local = ((snap.Pos - start) % len + len) % len;
                foreach (var n in _engine.GetSessionNotes(trackId, s))
                    if (local >= n.StartBeat && local < n.StartBeat + n.LengthBeats) set.Add(n.Pitch);
            }
        }
        foreach (var c in _clients.Values)
            if (c.Authenticated && c.TrackId == trackId)
                foreach (int p in c.HeldPitches()) set.Add(p);
        return Json(w =>
        {
            w.WriteString("t", "lit");
            w.WriteNumber("id", trackId);
            w.WriteStartArray("n");
            foreach (int p in set) w.WriteNumberValue(p);
            w.WriteEndArray();
        });
    }

    private NotaTrackInfo? TrackInfo(int trackId)
    {
        var row = _tracks.FirstOrDefault(r => r.Id == trackId);
        if (row is not null && _engine.TryGetTrackInfo(row.Index, out var ti) && ti.Id == trackId) return ti;
        int n = _engine.TrackCount;
        for (int i = 0; i < n; i++)
            if (_engine.TryGetTrackInfo(i, out var t2) && t2.Id == trackId) return t2;
        return null;
    }

    // Where a playing slot started, for its progress line: first seen playing, on the bar.
    private double SlotStart(int trackId, int scene, double pos)
    {
        if (!_slotStart.TryGetValue((trackId, scene), out double s))
            _slotStart[(trackId, scene)] = s = Math.Floor(pos / 4) * 4;
        return s;
    }

    // ---- mixer ---------------------------------------------------------------------------

    private string BuildMixer(IRemoteHost host) => Json(w =>
    {
        w.WriteString("t", "mix");
        w.WriteStartArray("ch");
        int n = _engine.TrackCount;
        for (int i = 0; i < n; i++)
        {
            if (!_engine.TryGetTrackInfo(i, out var ti)) continue;
            _engine.TryGetTrackMeter(ti.Id, out var m);
            w.WriteStartArray();
            w.WriteNumberValue(ti.Id);
            w.WriteNumberValue(Math.Round(ti.Volume, 4));
            w.WriteNumberValue(Math.Round(ti.Pan, 3));
            w.WriteNumberValue(ti.Muted);
            w.WriteNumberValue(ti.Soloed);
            w.WriteNumberValue(ti.Armed);
            w.WriteNumberValue(MeterPos(m.PeakL));
            w.WriteNumberValue(MeterPos(m.PeakR));
            w.WriteStringValue(_heldFaders.TryGetValue(ti.Id, out var h) ? h.Name : "");
            w.WriteNumberValue(h.Conn);
            w.WriteEndArray();
        }
        w.WriteEndArray();
        var mm = _engine.MasterMeter();
        w.WriteStartArray("m");
        w.WriteNumberValue(Math.Round(host.MasterVolume, 4));
        w.WriteNumberValue(MeterPos(mm.PeakL));
        w.WriteNumberValue(MeterPos(mm.PeakR));
        w.WriteStringValue(_heldFaders.TryGetValue(MasterFaderId, out var mh) ? mh.Name : "");
        w.WriteNumberValue(mh.Conn);
        w.WriteEndArray();
    });

    // A meter's height: −60..+6 dBFS onto 0..1.
    private static double MeterPos(float peak)
        => peak <= 1e-6f ? 0 : Math.Round(Math.Clamp((20 * Math.Log10(peak) + 60) / 66, 0, 1), 3);

    // ---- XY -------------------------------------------------------------------------------

    private string BuildXy(IRemoteHost host, Snapshot snap, RemoteClient c)
    {
        var def = c.TrackId > 0 ? RemoteParams.DefaultXy(_engine, c.TrackId) : null;
        return Json(w =>
        {
            w.WriteString("t", "xy");
            w.WriteNumber("id", c.TrackId);
            w.WriteBoolean("ok", def is not null);
            w.WriteBoolean("auto", host.Recording && snap.Playing);
            void Axis(string name, RemoteParam? p, int ctl, int tiltCtl)
            {
                w.WriteStartObject(name);
                if (p is not null)
                {
                    double v = RemoteParams.Get(_engine, p);
                    w.WriteString("n", p.Name);
                    w.WriteString("d", p.Device);
                    w.WriteNumber("v", Math.Round(v, 4));
                    w.WriteString("txt", RemoteParams.Text(_engine, p, v));
                }
                WriteMap(w, "map", host.PhoneMappingName(ctl));
                WriteMap(w, "tilt", host.PhoneMappingName(tiltCtl));
                w.WriteEndObject();
            }
            Axis("x", def?.X, PhoneControls.XyX, PhoneControls.TiltX);
            Axis("y", def?.Y, PhoneControls.XyY, PhoneControls.TiltY);
        });
    }

    private static void WriteMap(Utf8JsonWriter w, string name, string? value)
    {
        if (value is null) w.WriteNull(name); else w.WriteString(name, value);
    }

    // ---- macros -----------------------------------------------------------------------------

    private List<RemoteMacroDevice> MacroDevicesFor(int trackId)
    {
        var row = _tracks.FirstOrDefault(r => r.Id == trackId);
        if (row is null || row.Kind is "group") return new();
        return RemoteParams.MacroDevices(_engine, trackId, row.Kind is "drum" or "inst");
    }

    private string BuildMacros(IRemoteHost host, RemoteClient c)
    {
        var devs = MacroDevicesFor(c.TrackId);
        if (c.MacroDevice >= devs.Count) c.MacroDevice = 0;
        return Json(w =>
        {
            w.WriteString("t", "mac");
            w.WriteNumber("id", c.TrackId);
            w.WriteNumber("di", c.MacroDevice);
            w.WriteStartArray("devs");
            foreach (var d in devs) w.WriteStringValue(d.Name);
            w.WriteEndArray();
            w.WriteStartArray("k");
            if (devs.Count > 0)
            {
                var ps = devs[c.MacroDevice].Params;
                for (int i = 0; i < 8; i++)
                {
                    w.WriteStartObject();
                    if (i < ps.Count)
                    {
                        double v = RemoteParams.Get(_engine, ps[i]);
                        w.WriteString("n", ps[i].Name);
                        w.WriteNumber("v", Math.Round(v, 4));
                        w.WriteString("txt", RemoteParams.Text(_engine, ps[i], v));
                        w.WriteBoolean("a", RemoteParams.Automated(_engine, ps[i]));
                    }
                    WriteMap(w, "map", host.PhoneMappingName(PhoneControls.Macro1 + i));
                    w.WriteEndObject();
                }
            }
            w.WriteEndArray();
        });
    }

    // ---- session --------------------------------------------------------------------------------

    private string BuildSession(Snapshot snap)
    {
        int scenes = _engine.SceneCount;
        return Json(w =>
        {
            w.WriteString("t", "ses");
            w.WriteNumber("n", scenes);
            w.WriteStartArray("tr");
            foreach (var r in _tracks)
            {
                if (r.Kind is "group" or "return") continue;
                w.WriteStartObject();
                w.WriteNumber("id", r.Id);
                w.WriteStartArray("c");
                for (int s = 0; s < scenes; s++)
                {
                    int st = _engine.SessionSlotState(r.Id, s);
                    if (st != 3) _slotStart.Remove((r.Id, s));
                    double len = st == 0 ? 0 : _engine.SessionSlotLength(r.Id, s);
                    double prog = 0;
                    if (st == 3 && len > 0)
                    {
                        double start = SlotStart(r.Id, s, snap.Pos);
                        prog = (((snap.Pos - start) % len) + len) % len / len;
                    }
                    w.WriteStartArray();
                    w.WriteNumberValue(st);
                    w.WriteNumberValue(Math.Round(len, 2));
                    w.WriteNumberValue(Math.Round(prog, 3));
                    w.WriteEndArray();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
        });
    }
}
