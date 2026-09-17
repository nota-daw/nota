// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Two typefaces and one hard rule between them: Geist for names and words, Geist Mono
// for anything that is a measurement — position, tempo, dB, Hz, ms, percentages,
// counters, format badges. Mono is monospaced, so digits keep their width as a knob turns.
//
// This is the C# side of Font.UI / Font.Mono in Theme/NotaTheme.axaml, for the
// custom-drawn layer that builds FormattedText itself. Never construct a Typeface from a
// family name in a view: a name the app does not bundle ("Inter", "Menlo", "monospace")
// silently resolves to whatever the OS has, which is how half the visualisers ended up
// in a different face from the chrome around them.
//
// Both families are bundled (assets/fonts, SIL OFL 1.1). Glyphs they lack — the thin
// space, ■ ▾ ✓ ★ — fall back to the system font per character.

using Avalonia;
using Avalonia.Media;

namespace Nota.App;

public static class NotaFonts
{
    public const string UiUri = "avares://Nota.App/Assets/Fonts#Geist";
    public const string MonoUri = "avares://Nota.App/Assets/Fonts#Geist Mono";

    public static readonly FontFamily UiFamily = new(UiUri);
    public static readonly FontFamily MonoFamily = new(MonoUri);

    // Geist 400 · 500 · 600 · 700
    public static readonly Typeface Sans = new(UiFamily);
    public static readonly Typeface SansMedium = new(UiFamily, FontStyle.Normal, FontWeight.Medium);
    public static readonly Typeface SansSemiBold = new(UiFamily, FontStyle.Normal, FontWeight.SemiBold);
    public static readonly Typeface SansBold = new(UiFamily, FontStyle.Normal, FontWeight.Bold);

    // Geist Mono 400 · 500 (· 700 for the rare bold readout)
    public static readonly Typeface Mono = new(MonoFamily);
    public static readonly Typeface MonoMedium = new(MonoFamily, FontStyle.Normal, FontWeight.Medium);
    public static readonly Typeface MonoBold = new(MonoFamily, FontStyle.Normal, FontWeight.Bold);

    /// <summary>Make Geist the default family, so a control that never names a font — and
    /// <c>FontFamily.Default</c> — still lands on it rather than on the OS face.</summary>
    public static AppBuilder WithNotaFonts(this AppBuilder builder)
        => builder.With(new FontManagerOptions { DefaultFamilyName = UiUri });
}

/// <summary>The type scales from the almanac, for views that set FontSize in code. Mirrors
/// the TextBlock role classes in NotaTheme.axaml. Tracking is in px, already multiplied out
/// from the em value at that size.</summary>
internal static class NotaType
{
    // Shell scale
    public const double Title = 26, Heading = 13, Name = 12, Body = 11, Caption = 11,
                        SectionLabel = 10, Readout = 13, Value = 10, Eyebrow = 9;
    public const double SectionLabelTracking = 1.4, EyebrowTracking = 1.62;

    // Device scale — 7 is the floor, and only for a caps label or its value.
    public const double DeviceName = 12, DeviceSection = 9, RowLabel = 8, KnobLabel = 7,
                        KnobValue = 7, Axis = 7;
    public const double DeviceSectionTracking = 1.08, RowLabelTracking = 0.8, KnobLabelTracking = 0.56;

    public const double Floor = 7;
}
