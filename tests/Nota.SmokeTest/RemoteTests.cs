// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Remote: the engine's track-addressed notes (a phone plays its own track, armed or not,
// and never leaks into other tracks), pairing (codes rotate, wrong guesses lock out, tokens
// survive a restart, forgetting revokes), and a phone session over a real WebSocket against
// a running server — pair, receive the project, play a note, change the mixer, respect the
// play-only access level, release held notes when the phone drops.

using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Nota.Application;
using Nota.Infrastructure;
using Nota.Remote;

namespace Nota.SmokeTest;

internal static class RemoteTests
{
    // Energy at one frequency (Goertzel) of interleaved stereo at 48 kHz, left channel.
    private static double Goertzel(float[] st, double hz)
    {
        double w = 2 * Math.PI * hz / 48000.0, c = 2 * Math.Cos(w), s1 = 0, s2 = 0;
        for (int i = 0; i < st.Length; i += 2) { double s0 = st[i] + c * s1 - s2; s2 = s1; s1 = s0; }
        return s1 * s1 + s2 * s2 - c * s1 * s2;
    }

    private static float Rms(float[] buf, int frames)
    {
        double sum = 0;
        for (int i = 0; i < frames * 2; i++) sum += buf[i] * (double)buf[i];
        return (float)Math.Sqrt(sum / (frames * 2));
    }

    // Render a few blocks and return the loudest.
    private static float Loudest(NotaEngine e, int blocks = 8)
    {
        var b = new float[512 * 2];
        float max = 0;
        for (int i = 0; i < blocks; i++) { e.RenderOffline(b, 512); max = Math.Max(max, Rms(b, 512)); }
        return max;
    }

    public static IEnumerable<(bool, string)> Run()
    {
        foreach (var r in Engine()) yield return r;
        foreach (var r in Pairing()) yield return r;
        foreach (var r in Links()) yield return r;
        foreach (var r in Session()) yield return r;
    }

    // ---- USB links (the classifier is pure where it can be tested) --------------------------

    private static IEnumerable<(bool, string)> Links()
    {
        if (OperatingSystem.IsMacOS())
        {
            var ports = RemoteLinks.HardwarePorts();
            yield return (ports.Count > 0 && ports.Keys.All(d => d.Length > 0) && ports.Values.All(p => p.Length > 0),
                $"macOS hardware ports read in-process from SystemConfiguration ({string.Join(", ", ports.Select(kv => kv.Key + "=" + kv.Value))})");

            RemoteLinks.Invalidate();
            int before = RemoteLinks.PortReads, cleared = RemoteLinks.Invalidations;
            for (int i = 0; i < 20; i++) { RemoteLinks.All(); RemoteLinks.OnUsbLink(IPAddress.Parse("192.0.2.2")); }
            // A real network change mid-loop clears the map once more and earns one more read.
            int reads = RemoteLinks.PortReads - before, changes = RemoteLinks.Invalidations - cleared;
            yield return (reads >= 1 && reads <= 1 + changes,
                $"link queries reuse the port map until the network changes (read {reads}× for 40 queries, {changes} network change(s))");
        }

        yield return (RemoteLinks.InSubnet(IPAddress.Parse("192.0.2.2"), IPAddress.Parse("192.0.2.1"), 24),
            "a phone on the USB subnet is recognised (192.0.2.2 in 192.0.2.1/24)");
        yield return (!RemoteLinks.InSubnet(IPAddress.Parse("192.168.1.9"), IPAddress.Parse("192.0.2.1"), 24),
            "a Wi-Fi phone is not on the USB subnet");
        yield return (RemoteLinks.InSubnet(IPAddress.Parse("192.0.2.2"), IPAddress.Parse("192.0.2.1"), 30)
            && !RemoteLinks.InSubnet(IPAddress.Parse("192.0.2.4"), IPAddress.Parse("192.0.2.1"), 30),
            "the prefix length cuts where it should (/30 holds .1 and .2, not .4)");
        yield return (RemoteLinks.InSubnet(IPAddress.Parse("10.0.0.5"), IPAddress.Parse("10.0.0.1"), 0),
            "/0 contains everything");
        yield return (!RemoteLinks.InSubnet(IPAddress.Parse("::1"), IPAddress.Parse("10.0.0.1"), 24)
            && !RemoteLinks.InSubnet(IPAddress.Parse("10.0.0.5"), IPAddress.Parse("10.0.0.1"), 33),
            "nonsense prefixes and IPv6 match nothing");

        // The address in the link and QR (#85): the chosen interface while it is up, else the best.
        var links = new List<RemoteLink>
        {
            new("172.20.10.2", true, "USB", "en7"),
            new("192.168.1.20", false, "Wi-Fi", "en0"),
            new("192.168.1.21", false, "Wi-Fi", "en0"),
            new("10.0.0.5", false, "Ethernet", "en1"),
        };
        yield return (RemoteLinks.Pick(links, null)?.Address == "172.20.10.2" && RemoteLinks.Pick(links, "")?.Address == "172.20.10.2",
            "Automatic shows the best address (USB first)");
        yield return (RemoteLinks.Pick(links, "en1")?.Address == "10.0.0.5", "a chosen interface's address goes in the QR");
        yield return (RemoteLinks.Pick(links, "en0")?.Address == "192.168.1.20", "an interface with two addresses shows its first");
        yield return (RemoteLinks.Pick(links, "utun3")?.Address == "172.20.10.2", "a chosen interface that isn't up falls back to Automatic");
        yield return (RemoteLinks.Pick(new List<RemoteLink>(), "en0") is null, "no network, no address");
    }

    private static IEnumerable<(bool, string)> Engine()
    {
        using var e = new NotaEngine();
        e.SetBpm(120);
        int a = e.AddInstrumentTrack();
        int b = e.AddInstrumentTrack();
        e.SetTrackMute(b, false);
        Loudest(e, 2);

        e.TrackNoteOn(a, 60, 0.9f);
        float on = Loudest(e);
        yield return (on > 0.005f, $"a track-addressed note plays its unarmed track (rms={on:F4})");
        e.TrackNoteOff(a, 60);
        Loudest(e, 40);
        float off = Loudest(e, 4);
        yield return (off < 1e-3f, $"its note-off releases it (rms={off:F5})");

        // The same note on the other track only, then mute that track: silence proves it went
        // nowhere else.
        e.SetTrackMute(b, true);
        e.TrackNoteOn(b, 64, 0.9f);
        float leak = Loudest(e);
        yield return (leak < 1e-4f, $"an addressed note plays only its own track (rms={leak:F5} with that track muted)");
        e.TrackNoteOff(b, 64);
        Loudest(e, 40);

        // A finger sliding across pads over Wi-Fi: on/off pairs arrive in one burst, inside one
        // audio block. Live events all sit at offset 0 there and offs sort ahead of ons, so
        // without care each note's off ran before its on and the notes rang forever.
        e.SetTrackMute(b, false);
        foreach (int p in new[] { 60, 62, 64, 65, 67 }) { e.TrackNoteOn(a, p, 0.9f); e.TrackNoteOff(a, p); }
        e.TrackNoteOn(a, 69, 0.9f); e.TrackNoteOff(a, 69); e.TrackNoteOn(a, 69, 0.9f); e.TrackNoteOff(a, 69);
        Loudest(e, 60);
        float burst = Loudest(e, 4);
        yield return (burst < 1e-3f, $"a burst of quick on/off pairs in one block leaves no note hanging (rms={burst:F5})");

        // Ordinary live input keeps its rule: nothing armed, nothing plays.
        e.NoteOn(67, 0.9f);
        float live = Loudest(e);
        yield return (live < 1e-4f, $"ordinary live notes still need an armed track (rms={live:F5})");
        e.NoteOff(67);

        // Recording: a phone note on the take's track is captured; on another track it is not.
        using var r = new NotaEngine();
        r.SetBpm(120);
        int rec = r.AddInstrumentTrack();
        int other = r.AddInstrumentTrack();
        r.SetTrackArmed(rec, true);
        r.SetRecording(true);
        Loudest(r, 4);
        r.TrackNoteOn(rec, 60, 0.8f);
        r.TrackNoteOn(other, 72, 0.8f);
        Loudest(r, 20);
        r.TrackNoteOff(rec, 60);
        r.TrackNoteOff(other, 72);
        Loudest(r, 4);
        r.SetRecording(false);
        r.Poll();
        var notes = new List<int>();
        if (r.TryGetTrackInfo(0, out var ti))
            for (int c = 0; c < ti.ClipCount; c++) notes.AddRange(r.GetClipNotes(rec, c).Select(n => n.Pitch));
        yield return (notes.Contains(60), "a phone note on the armed track is recorded into the take");
        yield return (!notes.Contains(72), "a phone note on another track is not recorded into it");
    }

    private static IEnumerable<(bool, string)> Pairing()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nota-remote-test-" + Guid.NewGuid().ToString("N"));
        string store = Path.Combine(dir, "trusted.json");
        var p = new RemotePairing(store);
        string code = p.Code;
        yield return (code.Length == 4 && code.All(char.IsDigit), "the code is four digits");

        var wrong = code == "0000" ? "0001" : "0000";
        yield return (p.Pair(wrong, "x", "1.2.3.4", out var err1) is null && err1 == "code", "a wrong code is refused");
        var ok = p.Pair(code, "Egor's iPhone", "1.2.3.4", out _);
        yield return (ok is not null && ok.Value.Device.Name == "Egor's iPhone", "the right code pairs and names the phone");
        yield return (p.Code != code, "the code changes after a pairing");
        yield return (p.Pair(code, "again", "1.2.3.5", out _) is null, "a used code can't pair a second phone");

        // Five wrong codes from one address lock it out, even for the right code.
        string sender = "10.0.0.9";
        string? last = null;
        for (int i = 0; i < 5; i++) p.Pair(p.Code == "1111" ? "2222" : "1111", "y", sender, out last);
        yield return (last == "locked" && p.Pair(p.Code, "y", sender, out var e2) is null && e2 == "locked", "five wrong codes lock the sender out");

        var token = ok!.Value.Token;
        var reload = new RemotePairing(store);
        yield return (reload.Authenticate(token)?.Name == "Egor's iPhone", "a token survives a restart");
        yield return (reload.Authenticate(token + "x") is null, "a wrong token is refused");
        yield return (!File.ReadAllText(store).Contains(token), "the token itself is never stored");
        reload.Forget(ok.Value.Device.Id);
        yield return (reload.Authenticate(token) is null, "a forgotten phone's token stops working");
        try { Directory.Delete(dir, true); } catch { }
    }

    private sealed class FakeHost : IRemoteHost
    {
        public string HostName => "Test Mac";
        // Two appearances the test switches between: Nota in a dark theme with a separate
        // light-mode pick, then Nota in its light pick (so one id serves two roles).
        public static readonly RemoteTheme Dark = Theme("test-dark", true, "#1E1F22"), Light = Theme("test-light", false, "#C9CBCF");
        public static readonly RemoteAppearance DarkOn = new(Dark, Dark, Light, "#17181A", "#FFFFFF");
        public static readonly RemoteAppearance LightOn = new(Light, Dark, Light, "#17181A", "#FFFFFF");
        // A user skin (spec 087) with its own typeface and square corners (specs/090).
        public static readonly RemoteTheme Skin = Theme("test-skin", true, "#101010") with
        {
            Font = "IBM Plex Sans",
            Radii = new Dictionary<string, double> { ["card"] = 0, ["group"] = 0, ["field"] = 0, ["cell"] = 0, ["valueBar"] = 0, ["switch"] = 0, ["fine"] = 0 },
        };
        public static readonly RemoteAppearance SkinOn = new(Skin, Dark, Light, "#17181A", "#FFFFFF");
        public RemoteAppearance Appearance { get; set; } = DarkOn;
        private static RemoteTheme Theme(string id, bool dark, string ground) =>
            new(id, id, dark, new Dictionary<string, string> { ["BgApp"] = ground, ["Accent"] = "#FF9F1C", ["ShadowBevel"] = "#FFFFFF12" });
        public string TrackColor(int trackId) => "#58B368";
        public string ClipColor(int trackId, int scene) => "#4FA3E0";
        public int SelectedTrackId { get; set; }
        public int ProjectKeyCode => -1;
        public IReadOnlyList<(string Name, double StartBeat)> Sections => new[] { ("Intro", 0.0), ("Drop", 16.0) };
        public bool Recording { get; set; }
        public bool LoopOn { get; set; }
        public bool MetronomeOn { get; set; }
        public int Plays;
        public void Play() => Plays++;
        public void Stop() { }
        public void SetRecord(bool on) => Recording = on;
        public void SetLoop(bool on) => LoopOn = on;
        public void SetLoopRange(double s, double e) { }
        public void SetMetronome(bool on) => MetronomeOn = on;
        public void SetBpm(double bpm) { }
        public double MasterVolume { get; set; } = 1;
        public bool Undo() => false;
        public int Refreshes;
        public void Refresh() => Refreshes++;
        public bool LearnArmed => false;
        public string? LearnPendingName => null;
        public PhoneControlResult PhoneControl(int controlId, int trackId, double norm) => PhoneControlResult.None;
        public string? PhoneMappingName(int controlId, int trackId) => null;
        public void DevicesChanged() { }
        public void Activity() { }
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    /// <summary>The status line a raw request (no client-side path normalization) gets.</summary>
    private static int RawStatus(int port, string request)
    {
        try
        {
            using var c = new TcpClient("127.0.0.1", port);
            c.GetStream().Write(Encoding.UTF8.GetBytes(request));
            var buf = new byte[512];
            int n = c.GetStream().Read(buf, 0, buf.Length);
            var parts = Encoding.UTF8.GetString(buf, 0, n).Split(' ');
            return parts.Length > 1 && int.TryParse(parts[1], out int st) ? st : 0;
        }
        catch { return 0; }
    }

    private static IEnumerable<(bool, string)> Session()
    {
        using var e = new NotaEngine();
        e.SetBpm(120);
        int inst = e.AddInstrumentTrack();
        var pairing = new RemotePairing(null);
        var access = RemoteAccess.Control;
        var host = new FakeHost();
        var hub = new RemoteHub(e, pairing) { Host = host, Access = () => access, HostName = "Test Mac" };
        var server = new RemoteServer();
        int port = FreePort();
        bool started = true;
        try { server.StartAsync(hub, port).GetAwaiter().GetResult(); }
        catch { started = false; }
        yield return (started, $"the server starts on port {port}");
        if (!started) yield break;

        var http = new HttpClient();
        string page = "";
        try { page = http.GetStringAsync($"http://127.0.0.1:{port}/").GetAwaiter().GetResult(); } catch { }
        yield return (page.Contains("id=\"app\"") && page.Contains("manifest.webmanifest"), "the built phone app is served at / (not the not-built placeholder)");

        // Every asset the page references must be served: a bare MapFallback matches only
        // dotless paths, so index.html's script would 404 and the page would stay empty.
        var assets = Regex.Matches(page, "(?:src|href)=\"\\.//?([^\"]+)\"").Select(m => "/" + m.Groups[1].Value.TrimStart('/')).Distinct().ToList();
        int served = 0;
        foreach (var a in assets)
            try { if ((int)http.GetAsync($"http://127.0.0.1:{port}{a}").GetAwaiter().GetResult().StatusCode == 200) served++; } catch { }
        yield return (assets.Count > 0 && served == assets.Count, $"every asset the page references is served ({served}/{assets.Count})");

        // HttpClient normalizes /../../etc/passwd to /etc/passwd, which must come back as the
        // app shell — nothing on the computer's disk is ever served. A literal ../ sent over a
        // raw socket (curl --path-as-is) is refused outright.
        string shell = "";
        try { shell = http.GetStringAsync($"http://127.0.0.1:{port}/../../etc/passwd").GetAwaiter().GetResult(); } catch { }
        yield return (shell.Contains("id=\"app\"") && !shell.Contains("root:"), "a path walk-out gets the app shell, never a file");
        yield return (RawStatus(port, "GET /../secrets.txt HTTP/1.1\r\nHost: x\r\nConnection: close\r\n\r\n") is 400 or 404,
            "a literal ../ over a raw socket is refused");

        var ws = new ClientWebSocket();
        ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws"), CancellationToken.None).GetAwaiter().GetResult();
        var inbox = new List<JsonElement>();
        var cts = new CancellationTokenSource();
        var reader = Task.Run(async () =>
        {
            var buf = new byte[1 << 16];
            while (ws.State == WebSocketState.Open && !cts.IsCancellationRequested)
            {
                int len = 0;
                WebSocketReceiveResult r;
                try
                {
                    do { r = await ws.ReceiveAsync(new ArraySegment<byte>(buf, len, buf.Length - len), cts.Token); len += r.Count; }
                    while (!r.EndOfMessage);
                }
                catch { break; }
                if (r.MessageType == WebSocketMessageType.Close) break;
                var doc = JsonDocument.Parse(buf.AsMemory(0, len));
                lock (inbox) inbox.Add(doc.RootElement.Clone());
            }
        });
        void Send(object m) => ws.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(m)), WebSocketMessageType.Text, true, CancellationToken.None).GetAwaiter().GetResult();
        JsonElement? Wait(string t, int ms = 2000, Func<JsonElement, bool>? where = null)
        {
            var until = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < until)
            {
                hub.Tick();
                lock (inbox)
                {
                    var hit = inbox.FirstOrDefault(m => m.GetProperty("t").GetString() == t && (where is null || where(m)));
                    if (hit.ValueKind != JsonValueKind.Undefined) { inbox.Remove(hit); return hit; }
                }
                Thread.Sleep(20);
            }
            return null;
        }
        // Polls a state change that sends no reply — not a fixed sleep, which a slow CI
        // runner outlasts.
        void WaitFor(Func<bool> done, int ms = 2000)
        {
            var until = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < until && !done()) { hub.Tick(); Thread.Sleep(20); }
        }

        Send(new { t = "hello", token = "nope", name = "x" });
        var denied = Wait("denied");
        yield return (denied?.GetProperty("why").GetString() == "forgotten", "an unknown token is turned away");
        Send(new { t = "mute", id = inst, on = true });
        Thread.Sleep(80); hub.Tick();
        yield return (e.TryGetTrackInfo(0, out var t0) && t0.Muted == 0, "nothing is accepted before pairing");

        Send(new { t = "pair", code = pairing.Code, name = "Anna's iPhone", screen = "Mixer" });
        var welcome = Wait("welcome");
        yield return (welcome?.GetProperty("token").GetString()?.Length > 20 && welcome?.GetProperty("host").GetString() == "Test Mac",
            "the code pairs: welcome with a token and the host's name");
        var proj = Wait("proj");
        yield return (proj is { } pj && pj.GetProperty("tracks").GetArrayLength() == 1 && pj.GetProperty("you").GetInt32() == inst,
            "the phone gets the project and is put on the instrument track");

        // The theme (specs/014 contracts/theme-message.md): the worn theme and both picks,
        // each once with its tokens, and sent again only when it changes.
        var theme = Wait("theme");
        bool ThemeOk(JsonElement? m, string on) => m is { } tm
            && tm.GetProperty("on").GetString() == on
            && tm.GetProperty("dark").GetString() == "test-dark" && tm.GetProperty("light").GetString() == "test-light"
            && tm.GetProperty("ink").GetArrayLength() == 2
            && tm.GetProperty("th").EnumerateObject().Select(p => p.Name).OrderBy(n => n).SequenceEqual(new[] { "test-dark", "test-light" })
            && tm.GetProperty("th").EnumerateObject().All(p => p.Value.GetProperty("c").EnumerateObject().Count() == 3
                && p.Value.GetProperty("c").TryGetProperty("BgApp", out _));
        yield return (ThemeOk(theme, "test-dark") && theme!.Value.GetProperty("th").GetProperty("test-dark").GetProperty("c").GetProperty("ShadowBevel").GetString() == "#FFFFFF12",
            "the phone is told the theme Nota wears and both picks, each with its colours");
        yield return (proj is { } pd && pd.GetProperty("dark").GetBoolean(), "the project still says dark for an older page");
        yield return (Wait("theme", 200) is null, "an unchanged theme is not sent again");
        host.Appearance = FakeHost.LightOn;
        var theme2 = Wait("theme");
        yield return (ThemeOk(theme2, "test-light") && theme2!.Value.GetProperty("th").GetProperty("test-light").GetProperty("dark").GetBoolean() == false,
            "a theme change in Nota reaches the phone");
        yield return (Wait("theme", 200) is null, "and is sent once");
        yield return (Wait("proj", 1000, m => !m.GetProperty("dark").GetBoolean()) is not null, "the project's dark flag follows the theme");
        // A skin's typeface and corners travel with its colours (specs/090 contracts/theme-message.md).
        host.Appearance = FakeHost.SkinOn;
        var skin = Wait("theme");
        var skinTh = skin?.GetProperty("th");
        yield return (skinTh is { } sk && sk.GetProperty("test-skin").GetProperty("font").GetString() == "IBM Plex Sans"
                && sk.GetProperty("test-skin").GetProperty("r").EnumerateObject().Count() == 7
                && sk.GetProperty("test-skin").GetProperty("r").GetProperty("card").GetDouble() == 0,
            "a skin's typeface and all seven corner radii reach the phone");
        yield return (skinTh is { } sb && !sb.GetProperty("test-light").TryGetProperty("font", out _) && !sb.GetProperty("test-light").TryGetProperty("r", out _),
            "a theme on the design scale carries neither a typeface nor radii");
        host.Appearance = FakeHost.DarkOn;
        yield return (Wait("theme") is not null, "leaving the skin re-sends the theme");
        yield return (hub.Devices.Count == 1 && hub.Devices[0].Name == "Anna's iPhone", "Nota lists the phone");

        // Clip previews on Scenes (#557; specs/090 contracts/clips-message.md): sent to a phone
        // on Scenes only, the whole grid once, then only what changed; notes capped.
        e.AddSessionMidiClip(inst, 0, 8);
        e.SetSessionNotes(inst, 0, new[] { new NotaNote(60, 0, 1, 0.8f), new NotaNote(64, 2, 2, 0.8f), new NotaNote(67, 4, 4, 0.8f) });
        yield return (Wait("clips", 300) is null, "no clip previews while the phone is off Scenes");
        Send(new { t = "sub", s = "Scenes" });
        var clips = Wait("clips");
        JsonElement? Slot(JsonElement? m, int scene) => m is { } cm
            ? cm.GetProperty("s").EnumerateArray().Cast<JsonElement?>().FirstOrDefault(x => x!.Value.GetProperty("id").GetInt32() == inst && x.Value.GetProperty("i").GetInt32() == scene)
            : null;
        var s0 = Slot(clips, 0);
        yield return (clips?.GetProperty("all").GetBoolean() == true && s0 is { } c0 && c0.GetProperty("k").GetString() == "n"
                && c0.GetProperty("n").GetArrayLength() == 3 && c0.GetProperty("col").GetString() == "#4FA3E0"
                && c0.GetProperty("nm").GetString()!.Length > 0 && c0.GetProperty("len").GetDouble() == 8
                && c0.GetProperty("n")[1].EnumerateArray().Select(v => v.GetInt32()).SequenceEqual(new[] { 16, 16, 64 }),
            "opening Scenes sends every filled slot: name, colour, length and its notes on the 1/64 grid");
        yield return (Wait("clips", 1500) is null, "an unchanged clip is not sent again");
        e.SetSessionNotes(inst, 0, Enumerable.Range(0, 300).Select(i => new NotaNote(36 + i % 60, i * 8.0 / 300, 0.01, 0.8f)).ToArray());
        var edited = Wait("clips", 3000);
        var s0b = Slot(edited, 0);
        yield return (edited?.GetProperty("all").GetBoolean() == false && s0b is { } cb && cb.GetProperty("n").GetArrayLength() is > 0 and <= 128
                && edited.Value.GetProperty("s").GetArrayLength() == 1,
            "an edit re-sends just that slot, with at most 128 note bars");
        Send(new { t = "sub", s = "Mixer" });
        Thread.Sleep(60); hub.Tick();
        e.SetSessionNotes(inst, 0, new[] { new NotaNote(48, 0, 1, 0.8f) });
        yield return (Wait("clips", 800) is null, "a phone off Scenes gets no previews when clips change");
        Send(new { t = "sub", s = "Scenes" });
        var again = Wait("clips");
        yield return (again?.GetProperty("all").GetBoolean() == true && Slot(again, 0)?.GetProperty("n").GetArrayLength() == 1,
            "coming back to Scenes sends the current grid once");
        // An audio clip's preview is its waveform: 48 magnitudes scaled to the loudest bucket.
        string rampWav = Path.Combine(Path.GetTempPath(), $"nota-remote-ramp-{Guid.NewGuid():N}.wav");
        var ramp = new float[48000 * 2];
        for (int i = 0; i < ramp.Length; i++) ramp[i] = (float)(0.8 * i / ramp.Length * Math.Sin(2 * Math.PI * 220 * i / 48000.0));
        using (var ww = new Nota.Infrastructure.WavWriter(rampWav, 48000, 1, WavBitDepth.Float32)) ww.WriteFrames(ramp, ramp.Length);
        int aud = e.AddAudioTrack();
        bool audioIn = e.AddSessionAudioFile(aud, 0, rampWav);
        var withAudio = Wait("clips", 3000, m => m.GetProperty("s").EnumerateArray().Any(x => x.GetProperty("id").GetInt32() == aud));
        var pk = withAudio?.GetProperty("s").EnumerateArray().First(x => x.GetProperty("id").GetInt32() == aud);
        var peaks = pk is { } pp && pp.TryGetProperty("p", out var pa) ? pa.EnumerateArray().Select(v => v.GetInt32()).ToArray() : Array.Empty<int>();
        yield return (audioIn && pk?.GetProperty("k").GetString() == "p" && peaks.Length == 48 && peaks.Max() == 99 && peaks.All(v => v is >= 0 and <= 99)
                && peaks[0] < peaks[^1],
            $"an audio clip's preview is 48 peaks scaled to its loudest part (first {peaks.FirstOrDefault()}, last {peaks.LastOrDefault()})");
        e.RemoveTrack(aud);
        try { File.Delete(rampWav); } catch { }

        // A big grid at the real UI clock (33 ms): an edit that keeps a slot's state and length
        // (only its notes change) still reaches the phone within a second — the sweep is sized
        // by time, not by a fixed number of slots per tick.
        var big = Enumerable.Range(0, 15).Select(_ => e.AddInstrumentTrack()).ToArray();
        int scenesBefore = e.SceneCount;
        while (e.SceneCount < 32) e.AddScene();
        foreach (var bt in big.Prepend(inst))
            for (int sc = 1; sc < 32; sc++) { e.AddSessionMidiClip(bt, sc, 4); e.SetSessionNotes(bt, sc, new[] { new NotaNote(60, 0, 1, 0.8f) }); }
        Send(new { t = "sub", s = "Mixer" });
        Thread.Sleep(60); hub.Tick();
        Send(new { t = "sub", s = "Scenes" });
        Wait("clips", 3000);
        for (int k = 0; k < 40; k++) { hub.Tick(); Thread.Sleep(33); }
        lock (inbox) inbox.RemoveAll(m => m.GetProperty("t").GetString() == "clips");
        int far = big[^1];
        e.SetSessionNotes(far, 31, new[] { new NotaNote(72, 1, 2, 0.8f) });
        var sw = System.Diagnostics.Stopwatch.StartNew();
        JsonElement? farMsg = null;
        while (sw.ElapsedMilliseconds < 2500 && farMsg is null)
        {
            hub.Tick();
            lock (inbox)
            {
                var hit = inbox.FirstOrDefault(m => m.GetProperty("t").GetString() == "clips"
                    && m.GetProperty("s").EnumerateArray().Any(x => x.GetProperty("id").GetInt32() == far && x.GetProperty("i").GetInt32() == 31));
                if (hit.ValueKind != JsonValueKind.Undefined) farMsg = hit;
            }
            Thread.Sleep(33);
        }
        long farMs = sw.ElapsedMilliseconds;
        yield return (farMsg is not null && farMs < 1000, $"on a 16 × 32 grid at a 33 ms tick a note edit reaches the phone within a second ({farMs} ms)");
        foreach (var bt in big) e.RemoveTrack(bt);
        while (e.SceneCount > scenesBefore && e.RemoveScene(e.SceneCount - 1)) { }
        Send(new { t = "sub", s = "Mixer" });

        Send(new { t = "ping", c = 1 });
        yield return (Wait("pong") is not null, "a ping is answered");

        Loudest(e, 2);
        Send(new { t = "on", p = 60, v = 0.9 });
        Thread.Sleep(60);
        float note = Loudest(e);
        yield return (note > 0.005f, $"a pad hit from the phone sounds on its track (rms={note:F4})");

        // Expressive keys: the held note bends an octave (C4 → C5) through the phone's "x".
        yield return (proj is { } pm && pm.GetProperty("tracks")[0].TryGetProperty("mpe", out var mf) && mf.GetBoolean(),
            "the project marks a track whose instrument takes MPE");
        Send(new { t = "x", p = 60, d = 0, v = 12.0 });
        Send(new { t = "x", p = 61, d = 0, v = 12.0 });   // not held: ignored
        Thread.Sleep(60);
        var xb = new float[512 * 2 * 12];
        { var b = new float[512 * 2]; for (int k = 0; k < 4; k++) e.RenderOffline(b, 512, 48000); for (int k = 0; k < 12; k++) { e.RenderOffline(b, 512, 48000); Array.Copy(b, 0, xb, k * 1024, 1024); } }
        double c4 = Goertzel(xb, 261.63), c5 = Goertzel(xb, 523.25);
        yield return (c5 > c4 * 4, $"the phone's expression bends its held note (C4/C5 energy {c4:E1}/{c5:E1})");

        // Dropping the link releases the held note (as when a gamepad is unplugged).
        ws.Abort();
        cts.Cancel();
        Thread.Sleep(200);
        hub.Tick();
        Loudest(e, 60);
        float after = Loudest(e, 4);
        yield return (after < 1e-3f, $"a phone that drops has its held notes released (rms={after:F5})");
        yield return (hub.Devices.Count == 0, "the dropped phone leaves the list");

        // Reconnect with the token, then the mixer.
        string token = welcome!.Value.GetProperty("token").GetString()!;
        ws = new ClientWebSocket();
        ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws"), CancellationToken.None).GetAwaiter().GetResult();
        cts = new CancellationTokenSource();
        lock (inbox) inbox.Clear();
        reader = Task.Run(async () =>
        {
            var buf = new byte[1 << 16];
            while (ws.State == WebSocketState.Open && !cts.IsCancellationRequested)
            {
                int len = 0;
                WebSocketReceiveResult r;
                try
                {
                    do { r = await ws.ReceiveAsync(new ArraySegment<byte>(buf, len, buf.Length - len), cts.Token); len += r.Count; }
                    while (!r.EndOfMessage);
                }
                catch { break; }
                if (r.MessageType == WebSocketMessageType.Close) break;
                var doc = JsonDocument.Parse(buf.AsMemory(0, len));
                lock (inbox) inbox.Add(doc.RootElement.Clone());
            }
        });
        Send(new { t = "hello", token, name = "Anna's iPhone", screen = "Mixer" });
        yield return (Wait("welcome") is not null && !Wait("welcome", 200).HasValue, "the token reconnects without a code");
        var mix = Wait("mix");
        yield return (mix is { } mx && mx.GetProperty("ch").GetArrayLength() == 1, "the Mixer screen gets channel state");


        // The phone's ping claims the notes it holds: a lost off (a browser swallowed the
        // pointer-up) is healed by the next ping, and a genuinely held note is left alone.
        Send(new { t = "on", p = 64, v = 0.9 });
        Thread.Sleep(60);
        float ringing = Loudest(e);
        Send(new { t = "ping", c = 2, rtt = 4, h = new int[0] });
        Thread.Sleep(100);
        Loudest(e, 30);
        float healed = Loudest(e, 4);
        yield return (ringing > 0.005f && healed < 1e-3f,
            $"a note the phone stops claiming in its ping is released (rms={ringing:F4}→{healed:F5})");
        Send(new { t = "on", p = 65, v = 0.9 });
        Thread.Sleep(60);
        Send(new { t = "ping", c = 3, rtt = 4, h = new[] { 65 } });
        Thread.Sleep(100);
        yield return (Loudest(e) > 0.005f, "a note the phone still claims keeps sounding");
        Send(new { t = "off", p = 65 });
        Loudest(e, 30);

        float remoteVol0 = e.TryGetTrackInfo(0, out var rv0) ? rv0.Volume : -1;
        Send(new { t = "vol", id = inst, v = 0.5, ph = 2 });
        Send(new { t = "mute", id = inst, on = true });
        WaitFor(() => e.TryGetTrackInfo(0, out var t) && Math.Abs(t.Volume - 0.5f) < 1e-3 && t.Muted == 1 && host.Refreshes > 0);
        yield return (e.TryGetTrackInfo(0, out var t1) && Math.Abs(t1.Volume - 0.5f) < 1e-3 && t1.Muted == 1, "fader and mute from the phone reach the track");
        // A phone touch is one undo step (specs/009-mix-param-undo).
        e.Undo();
        e.TryGetTrackInfo(0, out var tu);
        yield return (tu.Volume == remoteVol0, $"undo takes back the phone's fader move ({tu.Volume}, want {remoteVol0})");
        e.Redo();
        yield return (host.Refreshes > 0, "Nota redraws after a phone edit");
        Send(new { t = "play" });
        WaitFor(() => host.Plays == 1);
        yield return (host.Plays == 1, "transport goes through Nota's transport");


        access = RemoteAccess.PlayNotes;
        Send(new { t = "mute", id = inst, on = false });
        var toast = Wait("toast");
        yield return (toast is not null && e.TryGetTrackInfo(0, out var t2) && t2.Muted == 1, "a play-only phone can't change the mixer, and is told why");

        // Forgetting in Nota drops the phone and its token.
        hub.Forget(hub.Devices[0].DeviceId);
        yield return (Wait("denied")?.GetProperty("why").GetString() == "forgotten", "forgetting the phone in Nota tells it to pair again");
        yield return (pairing.Authenticate(token) is null, "the forgotten token no longer works");

        cts.Cancel();
        try { ws.Abort(); } catch { }
        server.StopAsync().GetAwaiter().GetResult();
    }
}
