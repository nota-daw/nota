// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The addresses a phone can reach Nota on. A cable beats Wi-Fi — USB tethering (Android) or
// Internet Sharing (macOS) gives the computer a second address on the phone link, with
// millisecond latency and no radio to drop — so when one is up it leads the QR. The same
// server socket serves every interface; this only decides what Nota shows.

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Nota.Remote;

/// <summary>One address a phone could open. <paramref name="Name"/> says what it is ("USB",
/// "Wi-Fi") for the popup's rows; <paramref name="Interface"/> is the system's name for its
/// network interface (en0, eth0, "Wi-Fi"), which a chosen address is remembered by (#85).</summary>
public readonly record struct RemoteLink(string Address, bool Usb, string Name, string Interface);

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
                    links.Add((new RemoteLink(ua.Address.ToString(), isUsb, isUsb ? "USB" : wifi ? "Wi-Fi" : "Ethernet", ni.Name), rank));
                }
            }
        }
        catch (NetworkInformationException) { }
        return links.OrderBy(l => l.Rank).Select(l => l.Link).ToList();
    }

    /// <summary>The link the QR and the shown address use (#85): the first address of the
    /// interface named <paramref name="chosen"/> while it is up, else the best one
    /// (<paramref name="links"/>[0], as <see cref="All"/> ranks them) — so a chosen interface
    /// that goes away falls back to Automatic, and comes back with it. Null without a network.</summary>
    public static RemoteLink? Pick(IReadOnlyList<RemoteLink> links, string? chosen)
    {
        if (links.Count == 0) return null;
        if (!string.IsNullOrEmpty(chosen))
            foreach (var l in links)
                if (l.Interface == chosen) return l;
        return links[0];
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

    /// <summary>macOS: device (en7) → hardware port ("iPhone USB"). Read in-process from
    /// SystemConfiguration and kept until the network changes — ports appear only when something
    /// is plugged in, and that always brings an address with it. Never a child process: spawning
    /// one from this app stalls the whole process long enough to drop out the audio callback.</summary>
    private static Dictionary<string, string> UsbDevices()
    {
        if (!OperatingSystem.IsMacOS()) return new();
        lock (Gate)
        {
            if (_ports is not null) return _ports;
            _ports = MacHardwarePorts.Read();
            PortReads++;
            return _ports;
        }
    }

    static RemoteLinks()
    {
        NetworkChange.NetworkAddressChanged += (_, _) => Invalidate();
    }

    /// <summary>Forget the device → port map; the next query reads it again.</summary>
    public static void Invalidate()
    {
        lock (Gate) { _ports = null; Invalidations++; }
    }

    /// <summary>The macOS device → hardware-port map, as the USB check sees it. Public for the
    /// smoke test.</summary>
    public static IReadOnlyDictionary<string, string> HardwarePorts() => UsbDevices();

    /// <summary>How many times the port map has been read since launch. Public so the smoke test
    /// can check that link queries reuse it.</summary>
    public static int PortReads { get; private set; }

    /// <summary>How many times the map has been forgotten (a network change or
    /// <see cref="Invalidate"/>). Public so the smoke test can allow for a real network change
    /// landing mid-check.</summary>
    public static int Invalidations { get; private set; }

    private static readonly object Gate = new();
    private static Dictionary<string, string>? _ports;
}

/// <summary>SCNetworkInterfaceCopyAll: the same device/port pairs `networksetup
/// -listallhardwareports` prints, without leaving the process.</summary>
internal static class MacHardwarePorts
{
    private const string SystemConfiguration = "/System/Library/Frameworks/SystemConfiguration.framework/SystemConfiguration";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint Utf8 = 0x08000100;   // kCFStringEncodingUTF8

    public static Dictionary<string, string> Read()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            IntPtr all = SCNetworkInterfaceCopyAll();   // owned: released below
            if (all == IntPtr.Zero) return map;
            try
            {
                long n = CFArrayGetCount(all);
                for (long i = 0; i < n; i++)
                {
                    IntPtr ni = CFArrayGetValueAtIndex(all, i);   // borrowed from the array
                    string? device = Str(SCNetworkInterfaceGetBSDName(ni));
                    string? port = Str(SCNetworkInterfaceGetLocalizedDisplayName(ni));
                    if (!string.IsNullOrEmpty(device) && !string.IsNullOrEmpty(port)) map[device] = port;
                }
            }
            finally { CFRelease(all); }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { }
        return map;
    }

    // A borrowed CFString (a Get, not a Copy) as UTF-8 text.
    private static string? Str(IntPtr cf)
    {
        if (cf == IntPtr.Zero) return null;
        var buf = new byte[256];
        if (!CFStringGetCString(cf, buf, buf.Length, Utf8)) return null;
        int len = Array.IndexOf(buf, (byte)0);
        return System.Text.Encoding.UTF8.GetString(buf, 0, len < 0 ? buf.Length : len);
    }

    [DllImport(SystemConfiguration)] private static extern IntPtr SCNetworkInterfaceCopyAll();
    [DllImport(SystemConfiguration)] private static extern IntPtr SCNetworkInterfaceGetBSDName(IntPtr networkInterface);
    [DllImport(SystemConfiguration)] private static extern IntPtr SCNetworkInterfaceGetLocalizedDisplayName(IntPtr networkInterface);
    [DllImport(CoreFoundation)] private static extern long CFArrayGetCount(IntPtr array);
    [DllImport(CoreFoundation)] private static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, long index);
    [DllImport(CoreFoundation)] [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool CFStringGetCString(IntPtr str, byte[] buffer, long bufferSize, uint encoding);
    [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr cf);
}
