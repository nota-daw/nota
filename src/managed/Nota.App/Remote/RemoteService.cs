// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Owns Nota Remote's server, hub and pairing for the app, and keeps the server in step with
// Settings → Remote (on/off, port, access). The window ticks the hub on its UI clock and is
// its IRemoteHost; this class is what the top-bar button, the Connect Phone popup and the
// Settings page talk to.

using System;
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
    }

    public void RaiseChanged() => Changed?.Invoke();

    /// <summary>The address a phone opens: http://192.168.1.5:7788 (null without a network).</summary>
    public string? Url
    {
        get
        {
            var ip = RemoteServer.LanAddress();
            return ip is null ? null : $"http://{ip}:{_server.Port}";
        }
    }

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
