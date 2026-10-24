// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Geometry for code-built views — the C# mirror of the Radius.* / Control.* / Space.*
// keys in Theme/NotaTheme.axaml, the way NotaPalette mirrors Brush.*. Geometry does not
// change with the variant, so these are plain constants. The smoke test fails if a value
// here and its XAML key drift apart.
//
// Radius follows the style guide's scale (spec 038): 9 on the largest surfaces down to 2 on
// the smallest parts, and 0 for docked panels, which meet their neighbours on a seam.

using Avalonia;

namespace Nota.App;

internal static class NotaRadius
{
    public const double DockedValue = 0, CardValue = 9, GroupValue = 7, FieldValue = 6, CellValue = 5,
                        ValueBarValue = 4, SwitchValue = 3, FineValue = 2;

    /// <summary>0 — a docked panel; it meets its neighbours on a 1px seam.</summary>
    public static readonly CornerRadius Docked = new(DockedValue);
    /// <summary>9 — device card, toolbar view group, modal window.</summary>
    public static readonly CornerRadius Card = new(CardValue);
    /// <summary>7 — icon button, chip, segment group, device section, search, menu.</summary>
    public static readonly CornerRadius Group = new(GroupValue);
    /// <summary>6 — field, dropdown, clip, toggle pill, row highlight, graph window.</summary>
    public static readonly CornerRadius Field = new(FieldValue);
    /// <summary>5 — one segment cell, track button, EQ band cell, tooltip, inner key.</summary>
    public static readonly CornerRadius Cell = new(CellValue);
    /// <summary>4 — value bar, fader cap, overview strip.</summary>
    public static readonly CornerRadius ValueBar = new(ValueBarValue);
    /// <summary>3 — switch track.</summary>
    public static readonly CornerRadius Switch = new(SwitchValue);
    /// <summary>2 — switch knob, notes, bars inside a plot.</summary>
    public static readonly CornerRadius Fine = new(FineValue);

    /// <summary>Rounded top only — a header strip or a stripe sitting on a body.</summary>
    public static CornerRadius Top(double r) => new(r, r, 0, 0);
}

internal static class NotaSize
{
    public const double Chip = 24;        // filter chip
    public const double Seg = 20;         // one segment cell
    public const double Shell = 26;       // shell button, field, dropdown
    public const double SegGroup = 24;    // segment group: a 20 cell in a 2px well
    public const double Dropdown = 22;    // dropdown menu
    public const double Search = 30;      // browser search
    public const double Icon = 28;        // icon button: toolbar, rail
    public const double Transport = 28;   // transport key, an icon button
    public const double Play = 36;        // Play is the one wide transport key
    public const double Console = 32;     // the transport keys' well: 28px icon buttons in 2px of well
    public const double TransportBar = 44; // the docked transport row
    public const double Knob = 26;        // a device knob, with its centre cap
    public const double KnobInline = 22;  // a device knob with its label beside it (Glide, EQ band, oscillator rows)
    public const double KnobFlat = 28;    // a knob outside a device card: no cap
    public const double ParamCell = 56;   // knob 26 + 2 + value line 13 + 2 + label line 13
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
    public const double Gutter = 1;       // between docked panels: the seam
    public const double Inset = 20;
}
