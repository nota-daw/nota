// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// One frame of a device page's LED display (specs/113, FR-011): the page's picture under a
// header row — "DEVICE PAGE" on the left, the page's short readout on the right — or, while a
// knob turns, an inverted bar with the knob's name and value. A picture that throws (a device
// removed mid-frame) leaves the bars instead of a broken frame.

using Nota.Application.Controllers;

namespace Nota.Application.Displays;

public static class DisplayRenderer
{
    public const int HeaderRows = 8;

    /// <summary>Draw <paramref name="c"/>'s page into <paramref name="f"/>; <paramref name="overlay"/>
    /// (e.g. "CUTOFF 1.20 KHZ") replaces the header while a knob turns.</summary>
    public static void Render(DisplayContext c, LedFrame f, string? overlay)
    {
        f.Clear();
        var page = DeviceDisplays.Find(c.TypeKey, c.Page, c.PageName);
        string readout = "";
        try
        {
            if (page is not null) page.Draw(c, f); else DeviceDisplays.Bars(c, f);
            readout = page?.Readout?.Invoke(c) ?? "";
        }
        catch (Exception)
        {
            f.Clear();
            try { DeviceDisplays.Bars(c, f); } catch (Exception) { f.Clear(); }
        }
        Header(f, c.DeviceName, c.PageName, readout, overlay);
    }

    /// <summary>A frame that only says <paramref name="message"/> ("NO DEVICES").</summary>
    public static void Message(LedFrame f, string message, string title = "")
    {
        f.Clear();
        if (title.Length > 0) Header(f, title, "", "", null);
        string m = LedFrame.Fit(message, LedFrame.W - 4);
        f.Text(m, (LedFrame.W - LedFrame.TextWidth(m)) / 2, (LedFrame.Top + LedFrame.Bottom) / 2 - 2, 10);
    }

    public static void Header(LedFrame f, string device, string page, string readout, string? overlay)
    {
        for (int i = 0; i < LedFrame.W * HeaderRows; i++) f.Dots[i] = 0;
        if (overlay is { Length: > 0 })
        {
            f.Rect(0, 0, LedFrame.W, 7, 13);
            f.Text(LedFrame.Fit(overlay, LedFrame.W - 4), 2, 1, 0, overwrite: true);
            return;
        }
        string right = LedFrame.Fit(readout, 48);
        int rw = LedFrame.TextWidth(right);
        string left = LedFrame.Fit(page.Length > 0 ? $"{device} {page}" : device, LedFrame.W - rw - (rw > 0 ? 6 : 0));
        f.Text(left, 0, 0, 15);
        if (rw > 0) f.Text(right, LedFrame.W - rw, 0, 8);
        f.HLine(0, LedFrame.W - 1, 6, 2, dotted: true);
    }

    /// <summary>The overlay text for a turned knob: its name and value, upper case.</summary>
    public static string Overlay(string name, string value) => $"{name} {value}".Trim();

    /// <summary>A short device name for the header: "Nota Volt" → "Volt".</summary>
    public static string ShortName(string name)
        => name.StartsWith("Nota ", StringComparison.OrdinalIgnoreCase) ? name[5..] : name;
}
