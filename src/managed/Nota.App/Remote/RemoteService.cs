// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Owns Nota Remote's server, hub and pairing for the app, and keeps the server in step with
// Settings → Remote (on/off, port, access). The window ticks the hub on its UI clock and is
// its IRemoteHost; this class is what the top-bar button, the Connect Phone popup and the
// Settings page talk to.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Nota.Application;
using Nota.Infrastructure;
using Nota.Remote;

namespace Nota.App;

public sealed class RemoteService
{
    private readonly ISettingsService _settings;
    private readonly ILogSink _log;
    private readonly RemoteServer _server = new();

    public RemoteHub Hub { get; }
    public RemotePairing Pairing { get; }

    /// <summary>Started, stopped, failed, a phone came or went, the code rotated.</summary>
    public event Action? Changed;

    public bool Running => _server.Running;
    /// <summary>The port the server listens on (0 before the first start).</summary>
    public int Port => _server.Port;
    /// <summary>Why the server isn't running although Remote is on (the port is taken…).</summary>
    public string? Error { get; private set; }

    public RemoteService(IAudioEngine engine, ISettingsService settings, ILogSink log)
    {
        _settings = settings;
        _log = log;
        Pairing = new RemotePairing(Path.Combine(NotaPaths.SubDir("remote"), "trusted.json"));
        Hub = new RemoteHub(engine, Pairing)
        {
            Access = () => _settings.Current.RemoteAccess == 0 ? RemoteAccess.PlayNotes : RemoteAccess.Control,
            HostName = ComputerName(),
        };
        Pairing.Changed += RaiseChanged;
        // A cable plugged in, Wi-Fi dropped: the addresses (and the QR) follow within a beat.
        System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged += (_, _) => OnNetworkChanged();
    }

    private void OnNetworkChanged()
    {
        _linksAt = DateTime.MinValue;   // the USB map may have changed with the addresses
        _networkRetry?.Dispose();
        _networkRetry = new System.Threading.Timer(_ => { RaiseChanged(); _networkRetry?.Dispose(); _networkRetry = null; },
            null, 800, -1);
    }

    private System.Threading.Timer? _networkRetry;
    private DateTime _linksAt = DateTime.MinValue;
    private List<RemoteLink> _links = new();

    public void RaiseChanged() => Changed?.Invoke();

    /// <summary>The addresses phones can open, USB first, then Wi-Fi/Ethernet (cached a beat —
    /// the popup asks every second).</summary>
    public IReadOnlyList<RemoteLink> Links
    {
        get
        {
            if (DateTime.UtcNow - _linksAt > TimeSpan.FromSeconds(1))
            {
                _links = RemoteLinks.All();
                _linksAt = DateTime.UtcNow;
            }
            return _links;
        }
    }

    /// <summary>The address a phone opens: the USB one when a cable link is up (millisecond
    /// latency, no radio), else Wi-Fi/Ethernet. Null without a network.</summary>
    public string? Url => Links.Count > 0 ? $"http://{Links[0].Address}:{_server.Port}" : null;

    /// <summary>What the QR holds: the address with the current code in the fragment, so the
    /// phone pairs the moment the page opens (a fragment never leaves the phone).</summary>
    public string? QrText => Url is { } u ? $"{u}/#p={Pairing.Code}" : null;

    /// <summary>Reconcile the server with Settings.</summary>
    public async Task ApplyAsync()
    {
        var s = _settings.Current;
        try
        {
            if (s.RemoteEnabled && _server.Running && _server.Port != s.RemotePort)
            {
                Hub.DisconnectAll();
                await _server.StopAsync();
            }
            if (s.RemoteEnabled && !_server.Running)
            {
                await _server.StartAsync(Hub, s.RemotePort);
                Error = null;
                _log.Info($"Nota Remote listening on port {s.RemotePort} ({Url ?? "no network"})");
            }
            else if (!s.RemoteEnabled && _server.Running)
            {
                Hub.DisconnectAll();
                await _server.StopAsync();
                Error = null;
                _log.Info("Nota Remote stopped");
            }
        }
        catch (Exception e)
        {
            Error = e is IOException || e.InnerException is System.Net.Sockets.SocketException
                ? $"Port {s.RemotePort} is in use by another app. Pick another port."
                : e.Message;
            _log.Error($"Nota Remote: {e.Message}");
        }
        RaiseChanged();
    }

    /// <summary>Stop on quit, releasing every phone's held notes before the engine goes.</summary>
    public async Task ShutdownAsync()
    {
        Hub.DisconnectAll();
        await _server.StopAsync();
    }

    // The name people know the computer by ("Studio Mac"), not its network host name.
    private static string ComputerName()
    {
        if (OperatingSystem.IsMacOS())
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo("/usr/sbin/scutil", "--get ComputerName")
                    { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
                if (p is not null)
                {
                    string name = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit(1000);
                    if (name.Length > 0) return name;
                }
            }
            catch { /* fall back to the host name */ }
        }
        return Environment.MachineName;
    }
}
