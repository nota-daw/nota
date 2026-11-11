// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The worn theme's typeface and corner radii (spec 087), applied live. Every control keeps
// the value it was written with — a design radius from the style guide's scale, Geist from
// Font.UI — as its base value, and the skin's value is laid over it. So a skin never
// compounds, the default skin is an exact no-op, and switching back restores every control
// without a rebuild.
//
// Not by coercion, the way text size is (NotaTextSize): CornerRadius and Rectangle's radii
// are registered on Border and Rectangle themselves, and Avalonia refuses to override an
// owner's metadata; and a coerced inherited typeface does not reliably come back. So the
// skin's value is set at Animation priority, above local values and styles, worked out from
// the base value (GetBaseValue) whenever that changes and on every skin change, and cleared
// again — by a flag, never touching anyone else's animation — when it matches the base. The custom-drawn layer takes the
// same skin from NotaRadius.*Value and NotaFormattedText, which it reads at render time.
//
// The values role rides on the same overlay (spec 091): a control written with Geist Mono
// (NotaFonts.ValuesFamily) wears the look's values face — Geist under the new look, Geist Mono
// under the classic one, or a skin's font.values — so a look switch re-wears it like a skin.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Nota.Application.Themes;

namespace Nota.App;

internal static class NotaSkin
{
    private static SkinGeometry _geometry = SkinGeometry.Default;
    private static FontFamily? _family;
    private static bool _installed;

    /// <summary>The worn skin's typeface and radii.</summary>
    public static SkinGeometry Geometry => _geometry;

    /// <summary>The worn skin's typeface, when it names one that is installed; otherwise null (Geist).</summary>
    public static FontFamily? Family => _family;

    /// <summary>A radius step as the worn skin draws it.</summary>
    public static double Radius(RadiusStep step) => _geometry.RadiusOf(step);

    /// <summary>A design radius as the worn skin draws it.</summary>
    public static double MapRadius(double value) => _geometry.MapRadius(value);

    public static CornerRadius Map(CornerRadius r) => _geometry.IsDefault ? r
        : new CornerRadius(MapRadius(r.TopLeft), MapRadius(r.TopRight), MapRadius(r.BottomRight), MapRadius(r.BottomLeft));

    /// <summary>The values role (written as Geist Mono, spec 091) becomes the look's values face;
    /// then Geist (by URI, by name, or as the app's default family) becomes the skin's typeface.</summary>
    public static FontFamily MapFamily(FontFamily family)
    {
        if (IsValues(family)) family = NotaFonts.MeasureFamily;
        return _family is not null && IsGeist(family) ? _family : family;
    }

    /// <summary>The same for a typeface, same weight and style — for NotaFormattedText.</summary>
    public static Typeface Map(Typeface face)
    {
        var family = MapFamily(face.FontFamily);
        return ReferenceEquals(family, face.FontFamily) ? face : new Typeface(family, face.Style, face.Weight, face.Stretch);
    }

    // The values role as written: Geist Mono, by URI or by name.
    private static bool IsValues(FontFamily? f)
    {
        if (f is null) return false;
        if (ReferenceEquals(f, NotaFonts.MonoFamily)) return true;
        var first = f.FamilyNames.Count > 0 ? f.FamilyNames[0] : f.Name;
        return first == NotaFonts.MonoFamily.Name;
    }

    // Geist by any of its names: Font.UI, the app's default family, and Fluent's own default
    // ("fonts:Inter#Inter, $Default"), which the window templates carry. Nota ships no Inter,
    // so that one resolves to the default family, Geist.
    private static bool IsGeist(FontFamily? f)
    {
        if (f is null) return false;
        if (ReferenceEquals(f, NotaFonts.UiFamily) || f == FontFamily.Default) return true;
        var first = f.FamilyNames.Count > 0 ? f.FamilyNames[0] : f.Name;
        return first == NotaFonts.UiFamily.Name || first == FontFamily.Default.Name || first == "Inter";
    }

    /// <summary>Hook the coercion in. Once, before the XAML loads.</summary>
    public static void Install()
    {
        if (_installed) return;
        _installed = true;

        // CornerRadius is one property shared by Border, TemplatedControl and ContentPresenter.
        // A base value that changes (a style, a local value) gets the skin laid over it again.
        Border.CornerRadiusProperty.Changed.AddClassHandler<AvaloniaObject>((o, e) =>
        {
            if (e.Priority != BindingPriority.Animation) SkinCorner(o);
        });
        Rectangle.RadiusXProperty.Changed.AddClassHandler<Rectangle>((r, e) =>
        {
            if (e.Priority != BindingPriority.Animation) SkinRadius(r, Rectangle.RadiusXProperty, RadiusXBit);
        });
        Rectangle.RadiusYProperty.Changed.AddClassHandler<Rectangle>((r, e) =>
        {
            if (e.Priority != BindingPriority.Animation) SkinRadius(r, Rectangle.RadiusYProperty, RadiusYBit);
        });

        // The typeface is inherited: a top level that wears it hands it to every text that does
        // not name a family, and the explicit Font.UI setters get it laid over where they land.
        TextElement.FontFamilyProperty.Changed.AddClassHandler<StyledElement>((o, e) =>
        {
            if (e.Priority is not (BindingPriority.Animation or BindingPriority.Inherited) && TakesFont(o)) SkinFont(o);
        });
    }

    // The skin values laid over base values, by object and property, so going back to the
    // design scale removes exactly those (and never an animation of someone else's). An
    // Animation-priority SetValue returns the handle that removes it.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AvaloniaObject, Dictionary<int, IDisposable>> Overlays = new();

    private const int CornerBit = 1, RadiusXBit = 2, RadiusYBit = 4, FontBit = 8;

    private static bool TakesFont(AvaloniaObject o) => o is TopLevel or TemplatedControl or TextBlock or TextPresenter;

    // The skin's typeface over a Geist base value (a top level with no value of its own counts
    // as Geist: the app's default family is Geist).
    private static void SkinFont(AvaloniaObject o)
    {
        var p = TextElement.FontFamilyProperty;
        var b = o.GetBaseValue(p);
        FontFamily? baseValue = b.HasValue ? b.Value : o is TopLevel ? FontFamily.Default : null;
        if (baseValue is null)
        {
            Overlay(o, p, FontFamily.Default, FontFamily.Default, FontBit);   // clears an old overlay
            return;
        }
        Overlay(o, p, baseValue, MapFamily(baseValue), FontBit);
    }

    // The skin's radius over the base value, or nothing over it when they agree.
    private static void SkinCorner(AvaloniaObject o)
    {
        var p = Border.CornerRadiusProperty;
        var b = o.GetBaseValue(p);
        var baseValue = b.HasValue ? b.Value : p.GetDefaultValue(o.GetType());
        Overlay(o, p, baseValue, Map(baseValue), CornerBit);
    }

    private static void SkinRadius(Rectangle r, StyledProperty<double> p, int bit)
    {
        var b = r.GetBaseValue(p);
        var baseValue = b.HasValue ? b.Value : p.GetDefaultValue(typeof(Rectangle));
        Overlay(r, p, baseValue, MapRadius(baseValue), bit);
    }

    private static void Overlay<T>(AvaloniaObject o, StyledProperty<T> p, T baseValue, T mapped, int bit)
    {
        Overlays.TryGetValue(o, out var mine);
        IDisposable? old = null;
        mine?.TryGetValue(bit, out old);
        if (EqualityComparer<T>.Default.Equals(mapped, baseValue))
        {
            if (old is null) return;
            mine!.Remove(bit);
            old.Dispose();
            return;
        }
        // Replace, never stack: the new value first, then the old handle goes.
        var handle = o.SetValue(p, mapped, BindingPriority.Animation);
        old?.Dispose();
        if (handle is null) { mine?.Remove(bit); return; }
        (mine ??= Overlays.GetValue(o, _ => new Dictionary<int, IDisposable>()))[bit] = handle;
    }

    /// <summary>Wear <paramref name="geometry"/>. Returns whether anything a control shows changed;
    /// the caller re-coerces (<see cref="Reapply"/>) and repaints.</summary>
    public static bool Set(SkinGeometry geometry)
    {
        var family = geometry.FontFamily is { } name && NotaThemes.IsFontInstalled(name) ? new FontFamily(name) : null;
        bool changed = !_geometry.Equals(geometry) || _family?.Name != family?.Name;
        _geometry = geometry;
        _family = family;
        return changed;
    }

    /// <summary>Re-coerce every open window, so controls built under another skin take this one.</summary>
    public static void Reapply()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
        foreach (var window in desktop.Windows) Reapply(window);
    }

    /// <summary>Re-coerce one tree (a window, or an offscreen preview root).</summary>
    public static void Reapply(Visual root)
    {
        foreach (var v in root.GetSelfAndVisualDescendants())
        {
            // A dropdown's or menu's content lives in its own popup root, outside the window's
            // tree — open now, or shown later (a closed ComboBox also draws its item from there).
            if (v is Avalonia.Controls.Primitives.Popup { Child: { } content }) Reapply(content);
            switch (v)
            {
                case Border or TemplatedControl or ContentPresenter:
                    SkinCorner(v);
                    break;
                case Rectangle r:
                    SkinRadius(r, Rectangle.RadiusXProperty, RadiusXBit);
                    SkinRadius(r, Rectangle.RadiusYProperty, RadiusYBit);
                    break;
            }
            if (TakesFont(v)) SkinFont(v);
            if (v is Layoutable l) l.InvalidateMeasure();
            v.InvalidateVisual();   // custom-drawn radii and text re-read the skin on their next render
        }
    }
}
