// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// One typeface: Geist for every word and every number (style guide, spec 038). Geist's
// digits are proportional by default, so the app turns on its tabular figures ("tnum")
// everywhere: TextElement.FontFeatures on every top level for controls, and
// NotaFormattedText for the custom-drawn layer. Digits then all have the same width and
// a value can change without the text around it jumping. "Geist" is a Reserved Font
// Name, so the bundled files stay unmodified and the feature is applied at shaping time.
//
// This is the C# side of Font.UI in Theme/NotaTheme.axaml, for the custom-drawn layer
// that builds FormattedText itself. Never construct a Typeface from a family name in a
// view: a name the app does not bundle ("Inter", "Menlo", "monospace") silently resolves
// to whatever the OS has.
//
// Geist is bundled (assets/fonts, SIL OFL 1.1). Glyphs it lacks — ■ ▾ ✓ ★ — fall back to
// the system font per character.

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Nota.App;

public static class NotaFonts
{
    public const string UiUri = "avares://Nota.App/Assets/Fonts#Geist";

    public static readonly FontFamily UiFamily = new(UiUri);

    // Geist 400 · 500 · 600 · 700
    public static readonly Typeface Sans = new(UiFamily);
    public static readonly Typeface SansMedium = new(UiFamily, FontStyle.Normal, FontWeight.Medium);
    public static readonly Typeface SansSemiBold = new(UiFamily, FontStyle.Normal, FontWeight.SemiBold);
    public static readonly Typeface SansBold = new(UiFamily, FontStyle.Normal, FontWeight.Bold);

    /// <summary>Tabular figures: every digit the same width. Applied to all text.</summary>
    public static readonly FontFeatureCollection Tabular = FontFeatureCollection.Parse("+tnum");

    /// <summary>Tabular figures for every control that draws text: the default of the
    /// inherited TextElement.FontFeatures on each type that reads it, so nothing has to
    /// set it and a popup's own root gets it too. Call once before the XAML loads.</summary>
    public static void InstallTabular()
    {
        TextElement.FontFeaturesProperty.OverrideDefaultValue<TopLevel>(Tabular);
        TextElement.FontFeaturesProperty.OverrideDefaultValue<TemplatedControl>(Tabular);
        TextBlock.FontFeaturesProperty.OverrideDefaultValue<TextBlock>(Tabular);
        TextElement.FontFeaturesProperty.OverrideDefaultValue<TextPresenter>(Tabular);
    }

    /// <summary>Make Geist the default family, so a control that never names a font — and
    /// <c>FontFamily.Default</c> — still lands on it rather than on the OS face.</summary>
    public static AppBuilder WithNotaFonts(this AppBuilder builder)
        => builder.With(new FontManagerOptions { DefaultFamilyName = UiUri });
}

/// <summary>FormattedText with tabular figures — what every custom-drawn view builds, so
/// numbers drawn in Render() keep their width like the ones in controls.</summary>
public class NotaFormattedText : FormattedText
{
    public NotaFormattedText(string textToFormat, CultureInfo culture, FlowDirection flowDirection,
                             Typeface typeface, double emSize, IBrush? foreground)
        : base(textToFormat, culture, flowDirection, typeface, emSize, foreground)
        => SetFontFeatures(NotaFonts.Tabular);
}

/// <summary>The type scales (the shell hierarchy, and a device scale held to the 260px card), for
/// views that set FontSize in code. Mirrors the TextBlock role classes in NotaTheme.axaml.
/// Labels are sentence case with no tracking: the *Tracking constants are 0 and stay only
/// so older call sites compile — new code should not set LetterSpacing at all.</summary>
internal static class NotaType
{
    // Shell scale
    public const double Title = 13, Heading = 13, Name = 12, Body = 12, Caption = 11,
                        SectionLabel = 11, Readout = 17, Value = 11.5, Eyebrow = 10,
                        ClipName = 11.5, TrackName = 13, KeyLetter = 10;
    public const double SectionLabelTracking = 0, EyebrowTracking = 0;

    // Device scale (style guide): one step under the shell so a 260px card keeps its room for
    // controls: name 12/600, a parameter's value 10 over its name 9.5 (ink 3), section titles and
    // row labels 9.5; switch words and captions 10, titles inside a device 11, a value on a button
    // or in a row 10.5, a tab inside a device 11. 7 is the floor (plot tick labels only).
    public const double DeviceName = 12, DeviceSection = 9.5, RowLabel = 9.5, KnobLabel = 9.5,
                        KnobValue = 10, Axis = 9,
                        DeviceCaption = 10, DeviceBody = 11, DeviceValue = 10.5, DeviceTab = 11;
    public const double DeviceSectionTracking = 0, RowLabelTracking = 0, KnobLabelTracking = 0;

    public const double Floor = 7;
}
