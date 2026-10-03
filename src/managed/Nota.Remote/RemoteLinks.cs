// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The addresses a phone can reach Nota on. A cable beats Wi-Fi — USB tethering (Android) or
// Internet Sharing (macOS) gives the computer a second address on the phone link, with
// millisecond latency and no radio to drop — so when one is up it leads the QR. The same
// server socket serves every interface; this only decides what Nota shows.

using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Nota.Remote;

/// <summary>One address a phone could open. <paramref name="Name"/> says what it is ("USB",
/// "Wi-Fi") for the popup's rows.</summary>
public readonly record struct RemoteLink(string Address, bool Usb, string Name);

public static class RemoteLinks
{
    /// <summary>Every usable address, USB first (it is the better link when it exists), then
    /// Wi-Fi, then Ethernet — interfaces with a gateway ahead of ones without.</summary>
    public static List<RemoteLink> All()
    {
        var usb = UsbDevices();
        var links = new List<(RemoteLink Link, int Rank)>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (!Usable(ni)) continue;
                bool isUsb = IsUsb(ni, usb);
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ua.Address)) continue;
                    var b = ua.Address.GetAddressBytes();
                    if (b[0] == 169 && b[1] == 254) continue;   // link-local: no DHCP
                    bool wifi = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
                    int rank = (isUsb ? 0 : 1) * 4 + (HasGateway(ni) ? 0 : 2) + (wifi ? 0 : 1);
                    links.Add((new RemoteLink(ua.Address.ToString(), isUsb, isUsb ? "USB" : wifi ? "Wi-Fi" : "Ethernet"), rank));
                }
            }
        }
        catch (NetworkInformationException) { }
        return links.OrderBy(l => l.Rank).Select(l => l.Link).ToList();
    }

    /// <summary>Whether <paramref name="ip"/> sits on a USB link (the device rows and the
    /// phone's "USB" badge).</summary>
    public static bool OnUsbLink(IPAddress? ip)
    {
        if (ip is null) return false;
        var usb = UsbDevices();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (!Usable(ni) || !IsUsb(ni, usb)) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork && InSubnet(ip, ua.Address, ua.PrefixLength))
                        return true;
            }
        }
        catch (NetworkInformationException) { }
        return false;
    }

    /// <summary>Network <paramref name="address"/>/<paramref name="prefix"/> contains
    /// <paramref name="ip"/> (both IPv4).</summary>
    public static bool InSubnet(IPAddress ip, IPAddress address, int prefix)
    {
        if (prefix is < 0 or > 32) return false;
        uint mask = prefix == 0 ? 0 : uint.MaxValue << (32 - prefix);
        return ToUint(ip) is { } a && ToUint(address) is { } n && (a & mask) == (n & mask);
    }

    private static uint? ToUint(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return b.Length == 4 ? (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]) : null;
    }

    private static bool Usable(NetworkInterface ni)
    {
        if (ni.OperationalStatus != OperationalStatus.Up
            || ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            return false;
        string n = ni.Name.ToLowerInvariant();
        return !(n.StartsWith("bridge") || n.StartsWith("vmnet") || n.StartsWith("docker")
                 || n.StartsWith("veth") || n.StartsWith("awdl") || n.StartsWith("llw") || n.Contains("virtual"));
    }

    private static bool HasGateway(NetworkInterface ni)
        => ni.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork);

    // ---- which devices are USB -----------------------------------------------------------

    private static bool IsUsb(NetworkInterface ni, Dictionary<string, string> macPorts)
    {
        if (OperatingSystem.IsMacOS() && macPorts.TryGetValue(ni.Name, out var port) && port is not null)
            return port.Contains("USB", StringComparison.OrdinalIgnoreCase)
                || port.Contains("Android", StringComparison.OrdinalIgnoreCase);
        // Windows names the adapter ("Remote NDIS based Internet Sharing Device", "Apple
        // Mobile Device Ethernet"); Linux names the port (usb0, enx…, rndis…).
        string s = (OperatingSystem.IsWindows() ? ni.Description : ni.Name) + " " + ni.Description;
        return s.Contains("usb", StringComparison.OrdinalIgnoreCase)
            || s.Contains("rndis", StringComparison.OrdinalIgnoreCase)
            || s.Contains("apple mobile", StringComparison.OrdinalIgnoreCase)
            || s.Contains("android", StringComparison.OrdinalIgnoreCase)
            || (OperatingSystem.IsLinux() && (ni.Name.StartsWith("enx", StringComparison.Ordinal)
                || ni.Name.StartsWith("usb", StringComparison.Ordinal)));
    }

    /// <summary>macOS: device (en7) → hardware port ("iPhone USB"). Cached; the helper it
    /// shells out to is not free and ports appear only when something is plugged in.</summary>
    private static Dictionary<string, string> UsbDevices()
    {
        if (!OperatingSystem.IsMacOS()) return new();
        lock (Gate)
        {
            if (DateTime.UtcNow - _portsAt < TimeSpan.FromSeconds(10)) return _ports;
            _ports = ListHardwarePorts();
            _portsAt = DateTime.UtcNow;
            return _ports;
        }
    }

    private static readonly object Gate = new();
    private static Dictionary<string, string> _ports = new();
    private static DateTime _portsAt = DateTime.MinValue;

    /// <summary>Parses `networksetup -listallhardwareports`: blocks of "Hardware Port:", then
    /// "Device:". Public so the smoke test can check it without shelling out.</summary>
    public static Dictionary<string, string> ParseHardwarePorts(string output)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        string port = "";
        foreach (var line in output.Split('\n'))
        {
            string l = line.Trim();
            if (l.StartsWith("Hardware Port:", StringComparison.Ordinal)) port = l["Hardware Port:".Length..].Trim();
            else if (l.StartsWith("Device:", StringComparison.Ordinal) && port.Length > 0)
            {
                map[l["Device:".Length..].Trim()] = port;
                port = "";
            }
        }
        return map;
    }

    private static Dictionary<string, string> ListHardwarePorts()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("/usr/sbin/networksetup", "-listallhardwareports")
                { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
            if (p is null) return new();
            string text = p.StandardOutput.ReadToEnd();
            p.WaitForExit(2000);
            return ParseHardwarePorts(text);
        }
        catch { return new(); }
    }
}
