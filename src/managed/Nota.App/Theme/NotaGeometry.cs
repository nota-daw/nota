// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Geometry for code-built views — the C# mirror of the Radius.* / Control.* / Space.*
// keys in Theme/NotaTheme.axaml, the way NotaPalette mirrors Brush.*. Geometry does not
// change with the variant, so these are plain constants. The smoke test fails if a value
// here and its XAML key drift apart.
//
// Radius follows the size of the object, not its role: a clip and a slider track are
// both 2 because both are thin, not because they do the same job.

using Avalonia;

namespace Nota.App;

internal static class NotaRadius
{
    public const double BarValue = 1, ClipValue = 2, BadgeValue = 3, ControlValue = 4,
                        TileValue = 5, PanelValue = 6, BodyValue = 8, PillValue = 999;

    /// <summary>1 — spectrum and level bars.</summary>
    public static readonly CornerRadius Bar = new(BarValue);
    /// <summary>2 — clip, indicator, slider track.</summary>
    public static readonly CornerRadius Clip = new(ClipValue);
    /// <summary>3 — small segment, badge inside a device.</summary>
    public static readonly CornerRadius Badge = new(BadgeValue);
    /// <summary>4 — graph window, button, dropdown.</summary>
    public static readonly CornerRadius Control = new(ControlValue);
    /// <summary>5 — transport button, search field, tile.</summary>
    public static readonly CornerRadius Tile = new(TileValue);
    /// <summary>6 — panel, device section, transport module.</summary>
    public static readonly CornerRadius Panel = new(PanelValue);
    /// <summary>8 — device body, outer window frame.</summary>
    public static readonly CornerRadius Body = new(BodyValue);
    /// <summary>Half the height, whatever the height is — Avalonia clamps an oversized radius.</summary>
    public static readonly CornerRadius Pill = new(PillValue);

    /// <summary>Rounded top only — a header strip or a stripe sitting on a body.</summary>
    public static CornerRadius Top(double r) => new(r, r, 0, 0);
}

internal static class NotaSize
{
    public const double Chip = 20;        // filter chip
    public const double Seg = 24;         // tab segment
    public const double Shell = 26;       // shell button, field, dropdown
    public const double SegGroup = 30;    // segment container
    public const double Transport = 28;   // transport button
    public const double Play = 40;        // Play is the one wide transport button
    public const double Console = 42;     // transport module: the recess inside the bar
    public const double TransportBar = 60; // the transport island
    public const double KnobSecondary = 34;
    public const double KnobRegular = 36;
    public const double KnobMain = 44;
    public const double KnobInline = 24;  // a knob inside a table row (Consort oscillators, ADSR strip)
    public const double ParamCell = 53;   // knob 34 + label line 9 + value line 10
}

internal static class NotaSpace
{
    // Inside a device: 2px units.
    public const double DeviceHair = 2;
    public const double DeviceGap = 5;
    public const double DeviceInset = 6;
    public const double DeviceInsetWide = 8;
    // In the shell: 4px units.
    public const double Tile = 8;
    public const double Gutter = 12;
    public const double Inset = 20;
}
