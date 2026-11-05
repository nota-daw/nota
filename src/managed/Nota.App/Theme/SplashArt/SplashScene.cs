// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The splash art's scene (specs/074): what SplashArtComposer makes of a version and what
// SplashArtView draws. It is a list of primitives that name colour *roles*, not colours,
// so it is theme-free: the same scene re-tints with the theme, and the smoke test can
// compare two scenes for equality. No Avalonia types here — the smoke test links this file.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Nota.App;

internal enum SplashLayout { TextLeft, TextRight, TextBottom }
internal enum SplashFamily { Roll, Rings, Strata, Panels, Lissajous, Faders }
internal enum SplashPaletteMode { Mono, Analog, Pair, Triad }
internal enum SplashWash { None, Radial, Linear }

/// <summary>A colour role. Every role is a theme token (see <see cref="SplashTokens"/>);
/// there are deliberately no semantic roles (mute, solo, record, danger …).</summary>
internal enum SplashRole { Ground, App, Panel, Raised, Hover, Seam, Rule, Grid, Faint, Tertiary, Ink, Accent, Track }

internal readonly record struct SPoint(double X, double Y);
internal readonly record struct SRect(double X, double Y, double W, double H);

/// <summary>A role, optionally shaded toward black or toward the ground, at an alpha.</summary>
internal readonly record struct ColorRef(SplashRole Role, int TrackIndex = 0, double TowardBlack = 0,
                                         double TowardGround = 0, double Alpha = 1)
{
    public ColorRef Darker(double k) => this with { TowardBlack = k };
    public ColorRef ToGround(double k) => this with { TowardGround = k };
    public ColorRef At(double alpha) => this with { Alpha = alpha };
    public bool IsNeutral => Role is not (SplashRole.Track or SplashRole.Accent);
}

internal readonly record struct SStop(double Offset, ColorRef Color);

/// <summary>Bounds: gradient points relative to the shape (0..1). Art: absolute card coordinates.</summary>
internal enum PaintSpace { Bounds, Art }

internal abstract record Paint
{
    public abstract IEnumerable<ColorRef> Colors { get; }
}
internal sealed record SolidPaint(ColorRef Color) : Paint
{
    public override IEnumerable<ColorRef> Colors => new[] { Color };
}
internal sealed record LinearPaint(EqList<SStop> Stops, SPoint Start, SPoint End, PaintSpace Space) : Paint
{
    public override IEnumerable<ColorRef> Colors => Stops.Select(s => s.Color);
}
internal sealed record RadialPaint(EqList<SStop> Stops, SPoint Center, double Radius, PaintSpace Space) : Paint
{
    public override IEnumerable<ColorRef> Colors => Stops.Select(s => s.Color);
}

internal abstract record Prim
{
    public abstract IEnumerable<Paint> Paints { get; }
}
internal sealed record RectPrim(SRect R, double Radius, Paint? Fill, Paint? Stroke, double StrokeWidth) : Prim
{
    public override IEnumerable<Paint> Paints => new[] { Fill, Stroke }.OfType<Paint>();
}
internal sealed record LinePrim(SPoint A, SPoint B, Paint Stroke, double StrokeWidth) : Prim
{
    public override IEnumerable<Paint> Paints => new[] { Stroke };
}
internal sealed record PolylinePrim(EqList<SPoint> Points, Paint? Stroke, Paint? Fill, double StrokeWidth) : Prim
{
    public override IEnumerable<Paint> Paints => new[] { Fill, Stroke }.OfType<Paint>();
}
internal sealed record PolygonPrim(EqList<SPoint> Points, Paint Fill) : Prim
{
    public override IEnumerable<Paint> Paints => new[] { Fill };
}
internal sealed record CirclePrim(SPoint C, double R, Paint? Fill, Paint? Stroke, double StrokeWidth) : Prim
{
    public override IEnumerable<Paint> Paints => new[] { Fill, Stroke }.OfType<Paint>();
}

internal sealed record SplashScene(
    string Version,
    SplashLayout Layout,
    SRect ArtRect,
    SRect TextRect,
    SplashFamily Family,
    SplashFamily? Overlay,
    SplashPaletteMode PaletteMode,
    EqList<int> Palette,
    double StrokeWeight,
    double ColourShare,
    SplashWash Wash,
    EqList<Prim> Items,
    int OverlayStart)
{
    public const double Width = 420, Height = 300;
}

/// <summary>The role → theme-token table, shared by the app's renderer (NotaPalette.ByKey)
/// and the smoke test's review sheet (theme JSON), so both resolve a role the same way.</summary>
internal static class SplashTokens
{
    public static string Name(ColorRef c) => c.Role switch
    {
        SplashRole.Ground => "BgSunken",
        SplashRole.App => "BgApp",
        // Where the role and the token share a name, nameof keeps the two in step.
        SplashRole.Panel => nameof(SplashRole.Panel),
        SplashRole.Raised => "SurfaceRaised",
        SplashRole.Hover => "SurfaceHover",
        SplashRole.Seam => nameof(SplashRole.Seam),
        SplashRole.Rule => nameof(SplashRole.Rule),
        SplashRole.Grid => "VizGrid",
        SplashRole.Faint => "TextFaint",
        SplashRole.Tertiary => "TextTertiary",
        SplashRole.Ink => "VizInk",
        SplashRole.Accent => nameof(SplashRole.Accent),
        SplashRole.Track => nameof(SplashRole.Track) + (Math.Clamp(c.TrackIndex, 0, 8) + 1),
        _ => "BgSunken",
    };

    /// <summary>Applies the shades and alpha to a resolved token colour (8-bit channels).</summary>
    public static (byte A, byte R, byte G, byte B) Shade(ColorRef c, (byte R, byte G, byte B) token, (byte R, byte G, byte B) ground)
    {
        double r = token.R, g = token.G, b = token.B;
        if (c.TowardGround > 0)
        {
            r += (ground.R - r) * c.TowardGround; g += (ground.G - g) * c.TowardGround; b += (ground.B - b) * c.TowardGround;
        }
        if (c.TowardBlack > 0) { r *= 1 - c.TowardBlack; g *= 1 - c.TowardBlack; b *= 1 - c.TowardBlack; }
        static byte B8(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);
        return (B8(c.Alpha * 255), B8(r), B8(g), B8(b));
    }
}

/// <summary>An immutable list with value equality, so records holding it compare by content.</summary>
internal readonly struct EqList<T> : IEquatable<EqList<T>>, IReadOnlyList<T>
{
    private readonly T[] _items;
    public EqList(IEnumerable<T> items) => _items = items.ToArray();
    private T[] Items => _items ?? Array.Empty<T>();
    public int Count => Items.Length;
    public T this[int i] => Items[i];
    public bool Equals(EqList<T> other) => Items.AsSpan().SequenceEqual(other.Items, EqualityComparer<T>.Default);
    public override bool Equals(object? obj) => obj is EqList<T> o && Equals(o);
    public override int GetHashCode()
    {
        var h = new HashCode();
        foreach (var x in Items) h.Add(x);
        return h.ToHashCode();
    }
    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => Items.GetEnumerator();
}
