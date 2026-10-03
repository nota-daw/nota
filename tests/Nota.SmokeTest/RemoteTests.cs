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
using Nota.Infrastructure;
using Nota.Remote;

namespace Nota.SmokeTest;

internal static class RemoteTests
{
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
        var ports = RemoteLinks.ParseHardwarePorts(
            "Hardware Port: Wi-Fi\nDevice: en0\nEthernet Address: aa\n\n" +
            "Hardware Port: iPhone USB\nDevice: en7\nEthernet Address: bb\n\n" +
            "Hardware Port: Thunderbolt Bridge\nDevice: bridge0\nEthernet Address: cc\n\n" +
            "Hardware Port: USB 10/100/1000 LAN\nDevice: en8\nEthernet Address: dd\n");
        yield return (ports.Count == 4 && ports["en7"] == "iPhone USB" && ports["en8"].Contains("USB"),
            "macOS hardware ports parse to their devices (iPhone USB on en7)");

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
        public bool DarkTheme => true;
        public string TrackColor(int trackId) => "#58B368";
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
        public PhoneControlResult PhoneControl(int controlId, double norm) => PhoneControlResult.None;
        public string? PhoneMappingName(int controlId) => null;
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
        yield return (hub.Devices.Count == 1 && hub.Devices[0].Name == "Anna's iPhone", "Nota lists the phone");

        Send(new { t = "ping", c = 1 });
        yield return (Wait("pong") is not null, "a ping is answered");

        Loudest(e, 2);
        Send(new { t = "on", p = 60, v = 0.9 });
        Thread.Sleep(60);
        float note = Loudest(e);
        yield return (note > 0.005f, $"a pad hit from the phone sounds on its track (rms={note:F4})");

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

        Send(new { t = "vol", id = inst, v = 0.5, ph = 2 });
        Send(new { t = "mute", id = inst, on = true });
        Wait("none", 150);
        yield return (e.TryGetTrackInfo(0, out var t1) && Math.Abs(t1.Volume - 0.5f) < 1e-3 && t1.Muted == 1, "fader and mute from the phone reach the track");
        yield return (host.Refreshes > 0, "Nota redraws after a phone edit");
        Send(new { t = "play" });
        Wait("none", 120);
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
