// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Geist for every word and every number (style guide, spec 038). Geist's
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
// A skin may name a system typeface instead (spec 087): NotaSkin coerces controls' Geist to it
// and NotaFormattedText swaps it in when the text is built, so cached Typeface fields follow.
//
// Two roles (spec 091): words, and values and readouts — the numbers with their units, tempo,
// position and counters that 0.59 set in Geist Mono. The values role is Font.Measure in XAML and
// Measure* here. Under the new look it IS Geist (the same family object, so nothing renders
// differently and a skin's typeface reaches it as before); under the classic look it is Geist
// Mono; a skin may name its own (format 3, font.values). NotaThemeService sets it with
// SetMeasure; read Measure* when the text is built, never into a static readonly field, or a
// live switch passes the text by.
//
// Geist and Geist Mono are bundled (assets/fonts, SIL OFL 1.1). Glyphs they lack — ■ ▾ ✓ ★,
// and Cyrillic in Geist Mono — fall back to the system font per character.

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

    /// <summary>Geist Mono: the values role under the classic look (spec 091).</summary>
    public const string MeasureMonoUri = "avares://Nota.App/Assets/Fonts#Geist Mono";
    public static readonly FontFamily MonoFamily = new(MeasureMonoUri);

    /// <summary>What a code-built control names to take the values role: Geist Mono as written,
    /// worn by NotaSkin as the look's values face — Geist under New, Geist Mono under Classic, or
    /// a skin's font.values — and re-worn live on every switch, like a skin's typeface. (A Run is
    /// not re-worn: give it <see cref="MeasureFamily"/> when it is built.)</summary>
    public static FontFamily ValuesFamily => MonoFamily;

    private static FontFamily _measure = UiFamily;
    private static Typeface _measureFace = new(UiFamily),
                            _measureMedium = new(UiFamily, FontStyle.Normal, FontWeight.Medium),
                            _measureBold = new(UiFamily, FontStyle.Normal, FontWeight.Bold);

    /// <summary>The family values and readouts are set in now: Geist (new look), Geist Mono
    /// (classic) or a skin's values typeface.</summary>
    public static FontFamily MeasureFamily => _measure;
    public static Typeface Measure => _measureFace;
    public static Typeface MeasureMedium => _measureMedium;
    public static Typeface MeasureBold => _measureBold;

    /// <summary>Changes whenever a typeface text is drawn in changes — the values face (a look
    /// switch, a skin's font.values) or the words face (a skin's font.family). A view that caches
    /// a measured text width keys it on this as well as on the Text size step.</summary>
    public static int FaceStamp { get; private set; }

    /// <summary>Mark the faces changed (NotaSkin calls it when a skin's typeface changes).</summary>
    internal static void TouchFaces() => FaceStamp++;

    /// <summary>Wear <paramref name="family"/> for values and readouts: the Measure* faces here and
    /// the Font.Measure resource every values text style reads. Returns whether it changed.</summary>
    public static bool SetMeasure(FontFamily family)
    {
        bool changed = !Equals(family, _measure) && family.ToString() != _measure.ToString();
        if (changed) FaceStamp++;
        _measure = family;
        _measureFace = new Typeface(family);
        _measureMedium = new Typeface(family, FontStyle.Normal, FontWeight.Medium);
        _measureBold = new Typeface(family, FontStyle.Normal, FontWeight.Bold);
        if (Avalonia.Application.Current is { } app) app.Resources["Font.Measure"] = family;
        return changed;
    }

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
/// numbers drawn in Render() keep their width like the ones in controls, and take the
/// Text size step like every control's text.</summary>
public class NotaFormattedText : FormattedText
{
    public NotaFormattedText(string textToFormat, CultureInfo culture, FlowDirection flowDirection,
                             Typeface typeface, double emSize, IBrush? foreground)
        : base(textToFormat, culture, flowDirection, NotaSkin.Map(typeface), NotaTextSize.Apply(emSize), foreground)
        => SetFontFeatures(NotaFonts.Tabular);

    /// <summary>Text drawn by <paramref name="owner"/>: inside a device card it keeps its size.</summary>
    public NotaFormattedText(string textToFormat, CultureInfo culture, FlowDirection flowDirection,
                             Typeface typeface, double emSize, IBrush? foreground, AvaloniaObject? owner)
        : base(textToFormat, culture, flowDirection, NotaSkin.Map(typeface), NotaTextSize.Apply(emSize, owner), foreground)
        => SetFontFeatures(NotaFonts.Tabular);
}

/// <summary>The type scales (the shell hierarchy, and a device scale held to the 260px card), for
/// views that set FontSize in code. Mirrors the TextBlock role classes in NotaTheme.axaml.
/// Labels are sentence case with no tracking: the *Tracking constants are 0 and stay only
/// so older call sites compile — new code should not set LetterSpacing at all.</summary>
internal static class NotaType
{
    // Shell scale
    public const double Title = 12, Heading = 12, Name = 11, Body = 11, Caption = 10,
                        SectionLabel = 10, Readout = 16, Value = 10.5, Eyebrow = 9.5,
                        ClipName = 10, TrackName = 12, KeyLetter = 9.5;
    public const double SectionLabelTracking = 0, EyebrowTracking = 0;

    // Device scale — 7 is the floor (plot tick labels only); a label is 8 or more. Switch
    // words and captions are 8, a value on a button or in the header 11, a tab inside a device 9.
    public const double DeviceName = 11, DeviceSection = 10.5, RowLabel = 9, KnobLabel = 8,
                        KnobValue = 8, Axis = 7.5,
                        DeviceCaption = 8, DeviceBody = 9, DeviceValue = 11, DeviceTab = 9;
    public const double DeviceSectionTracking = 0, RowLabelTracking = 0, KnobLabelTracking = 0;

    public const double Floor = 7;
}
