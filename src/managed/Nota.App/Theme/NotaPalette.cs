// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// The single source of truth for palette brushes used by C# custom-drawn controls
// (Render overrides can't cheaply resolve a XAML StaticResource). Every value here
// MIRRORS a Brush.* key in Theme/NotaTheme.axaml — keep the two in sync. Views used
// to each re-declare these hex literals privately; they now alias into this class so
// a colour changes in exactly one place. File-specific tints (low-alpha washes,
// one-off grid shades) intentionally stay local to their view.

using Avalonia.Media;

namespace Nota.App;

internal static class NotaPalette
{
    private static IBrush Hex(string hex) => new SolidColorBrush(Color.Parse(hex));

    // Surfaces (Brush.Bg* / Brush.Surface*)
    public static readonly IBrush BgSunken = Hex("#100F0D");     // Brush.BgSunken / ChromeBg
    public static readonly IBrush BgApp = Hex("#171613");        // Brush.BgApp
    public static readonly IBrush LaneB = Hex("#191814");        // Brush.LaneB
    public static readonly IBrush SurfaceCard = Hex("#1E1C18");  // Brush.SurfaceCard
    public static readonly IBrush SurfaceRaised = Hex("#26231E");// Brush.SurfaceRaised
    public static readonly IBrush SurfaceHover = Hex("#2E2B24"); // Brush.SurfaceHover
    public static readonly IBrush SurfaceActive = Hex("#332F27");// Brush.SurfaceActive

    // Text (Brush.Text*)
    public static readonly IBrush TextPrimary = Hex("#E9E4D8");  // Brush.TextPrimary
    public static readonly IBrush TextSecondary = Hex("#A39D8F");// Brush.TextSecondary
    public static readonly IBrush TextTertiary = Hex("#6E6A5E"); // Brush.TextTertiary
    public static readonly IBrush TextDisabled = Hex("#4A463D"); // Brush.TextDisabled
    public static readonly IBrush TextOnAccent = Hex("#171613"); // Brush.TextOnAccent

    // Borders (Brush.Border*)
    public static readonly IBrush BorderDefault = Hex("#2C2923");// Brush.BorderDefault
    public static readonly IBrush BorderStrong = Hex("#3A362D"); // Brush.BorderStrong

    // Grid lines (Brush.Grid*)
    public static readonly IBrush GridBeat = Hex("#1F1D18");     // Brush.GridBeat
    public static readonly IBrush GridBar = Hex("#2A2721");      // Brush.GridBar

    // Accent (Brush.Accent*)
    public static readonly IBrush Accent = Hex("#D8A03D");       // Brush.Accent (brass)
    public static readonly IBrush AccentHover = Hex("#E8B24C");  // Brush.AccentHover
    public static readonly IBrush AccentBright = Hex("#F0C060"); // Brush.AccentBright
    public static readonly IBrush AccentSubtle = new SolidColorBrush(Color.FromArgb(0x24, 0xD8, 0xA0, 0x3D)); // Brush.AccentSubtle

    // Status (Brush.Success / Warning / Danger)
    public static readonly IBrush Success = Hex("#58B368");      // Brush.Success
    public static readonly IBrush Warning = Hex("#D9C34C");      // Brush.Warning
    public static readonly IBrush Danger = Hex("#D95F4C");       // Brush.Danger
    public static readonly IBrush DangerHover = Hex("#E4715F");  // Brush.DangerHover

    // Track palette (Brush.Track*) — only the shades reused by custom-drawn views.
    public static readonly IBrush Sage = Hex("#6FA383");         // Brush.Track4
    public static readonly IBrush Teal = Hex("#5B9E9C");         // Brush.Track5 (modulation accent)

    // Raw Colors — for the few call sites that need a Color rather than an IBrush.
    public static readonly Color AccentColor = Color.Parse("#D8A03D");       // Brush.Accent
    public static readonly Color AccentBrightColor = Color.Parse("#F0C060"); // Brush.AccentBright
    public static readonly Color SuccessColor = Color.Parse("#58B368");      // Brush.Success

    // The 8-track colour cycle (Brush.Track1..8) + the two return-bus tints. Views index
    // these to colour clips/tracks; keep the order in sync with NotaTheme.axaml.
    public static readonly Color[] TrackColors =
    {
        Color.Parse("#C4756A"), Color.Parse("#C99C55"), Color.Parse("#9BA65D"), Color.Parse("#6FA383"),
        Color.Parse("#5B9E9C"), Color.Parse("#6D8FB5"), Color.Parse("#9B7FA6"), Color.Parse("#B57286"),
    };
    public static readonly Color[] ReturnColors = { Color.Parse("#7C88A0"), Color.Parse("#A08A7C") }; // ReturnA, ReturnB
}
