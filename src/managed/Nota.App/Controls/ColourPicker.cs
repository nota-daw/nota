// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The colour picker of Settings → Appearance → Accent → Custom (spec 084): a
// saturation/brightness area over a hue strip. The spectrum is content, not chrome — like a
// waveform it shows the thing being chosen — so it is drawn into bitmaps from HSV maths
// rather than with gradient brushes (DESIGN.md § Accepted exceptions). Frames are the Rule
// line, handles the selected-handle ink with a seam ring, as on a graph. Left button only;
// a drag reports every step so the caller can preview it live.

using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Nota.App;

internal sealed class ColourPicker : StackPanel
{
    public const double W = 216, AreaH = 132, StripH = 10;

    private readonly SpectrumArea _area;
    private readonly HueStrip _strip;
    private double _h, _s = 1, _v = 1;

    /// <summary>Raised on every step of a drag, with the colour (0xRRGGBB).</summary>
    public event Action<uint>? ColourChanged;

    public ColourPicker()
    {
        Spacing = 8;
        _area = new SpectrumArea(this) { Width = W, Height = AreaH };
        _strip = new HueStrip(this) { Width = W, Height = StripH };
        Children.Add(_area);
        Children.Add(_strip);
    }

    /// <summary>The colour shown, 0xRRGGBB.</summary>
    public uint Rgb => HsvToRgb(_h, _s, _v);

    /// <summary>Show <paramref name="rgb"/> without raising <see cref="ColourChanged"/>. A grey
    /// keeps the hue the strip already had.</summary>
    public void Set(uint rgb)
    {
        var (h, s, v) = RgbToHsv(rgb);
        if (s > 0 && v > 0) _h = h;
        _s = s;
        _v = v;
        _area.InvalidateVisual();
        _strip.InvalidateVisual();
    }

    private void Picked(double? h, double? s, double? v)
    {
        if (h is { } hh) _h = hh;
        if (s is { } ss) _s = ss;
        if (v is { } vv) _v = vv;
        _area.InvalidateVisual();
        _strip.InvalidateVisual();
        ColourChanged?.Invoke(Rgb);
    }

    // ---- colour maths ------------------------------------------------------------------

    public static uint HsvToRgb(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360 / 60;
        double c = v * s, x = c * (1 - Math.Abs(h % 2 - 1)), m = v - c;
        var (r, g, b) = (int)h switch
        {
            0 => (c, x, 0.0), 1 => (x, c, 0.0), 2 => (0.0, c, x),
            3 => (0.0, x, c), 4 => (x, 0.0, c), _ => (c, 0.0, x),
        };
        static uint B(double t) => (uint)Math.Round(Math.Clamp(t, 0, 1) * 255);
        return (B(r + m) << 16) | (B(g + m) << 8) | B(b + m);
    }

    public static (double H, double S, double V) RgbToHsv(uint rgb)
    {
        double r = ((rgb >> 16) & 0xFF) / 255.0, g = ((rgb >> 8) & 0xFF) / 255.0, b = (rgb & 0xFF) / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = d == 0 ? 0 : max == r ? 60 * (((g - b) / d) % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        return (h < 0 ? h + 360 : h, max == 0 ? 0 : d / max, max);
    }

    // ---- the two surfaces --------------------------------------------------------------

    // Shared: a bitmap the size of the control, the frame, left-button drag with capture.
    private abstract class Surface : Control
    {
        protected readonly ColourPicker Owner;
        private WriteableBitmap? _bmp;
        private double _builtFor = double.NaN;
        private bool _drag;

        protected Surface(ColourPicker owner)
        {
            Owner = owner;
            Cursor = new Cursor(StandardCursorType.Cross);
            ClipToBounds = false;
        }

        // The pixel at (x, y) of a w × h bitmap, ARGB.
        protected abstract uint Pixel(int x, int y, int w, int h);
        // What the bitmap depends on (the area's hue; the strip never changes).
        protected abstract double Key { get; }
        protected abstract void Pick(double fx, double fy);
        protected abstract void DrawHandle(DrawingContext ctx, Rect r);

        public override void Render(DrawingContext ctx)
        {
            var r = new Rect(Bounds.Size);
            if (r.Width < 2 || r.Height < 2) return;
            var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            int pw = (int)Math.Ceiling(r.Width * scale), ph = (int)Math.Ceiling(r.Height * scale);
            if (_bmp is null || _bmp.PixelSize.Width != pw || _bmp.PixelSize.Height != ph || !Key.Equals(_builtFor))
            {
                _bmp?.Dispose();
                _bmp = new WriteableBitmap(new PixelSize(pw, ph), new Vector(96 * scale, 96 * scale), PixelFormat.Bgra8888, AlphaFormat.Premul);
                using var fb = _bmp.Lock();
                var row = new int[pw];
                for (int y = 0; y < ph; y++)
                {
                    for (int x = 0; x < pw; x++) row[x] = unchecked((int)Pixel(x, y, pw, ph));
                    Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, pw);
                }
                _builtFor = Key;
            }
            var rr = new RoundedRect(r, NotaRadius.ControlValue);
            using (ctx.PushClip(rr)) ctx.DrawImage(_bmp, new Rect(_bmp.Size), r);
            ctx.DrawRectangle(null, new Pen(NotaPalette.Rule, 1), new RoundedRect(r.Deflate(0.5), NotaRadius.ControlValue));
            DrawHandle(ctx, r);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _drag = true;
            e.Pointer.Capture(this);
            PickAt(e.GetPosition(this));
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            if (_drag) PickAt(e.GetPosition(this));
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            if (!_drag) return;
            _drag = false;
            e.Pointer.Capture(null);
        }

        private void PickAt(Point p) =>
            Pick(Math.Clamp(p.X / Math.Max(1, Bounds.Width), 0, 1), Math.Clamp(p.Y / Math.Max(1, Bounds.Height), 0, 1));

        // A round handle: the selected-handle ink, ringed by the seam so it reads on any colour.
        protected static void Ring(DrawingContext ctx, Point c, double radius)
        {
            ctx.DrawEllipse(null, new Pen(NotaPalette.Seam, 1), c, radius + 1.5, radius + 1.5);
            ctx.DrawEllipse(null, new Pen(NotaPalette.HandleSel, 2), c, radius, radius);
        }
    }

    // Saturation left → right, brightness top → bottom, at the picker's hue.
    private sealed class SpectrumArea : Surface
    {
        public SpectrumArea(ColourPicker owner) : base(owner) { }
        protected override double Key => Owner._h;
        protected override uint Pixel(int x, int y, int w, int h) =>
            0xFF000000 | HsvToRgb(Owner._h, x / (double)Math.Max(1, w - 1), 1 - y / (double)Math.Max(1, h - 1));
        protected override void Pick(double fx, double fy) => Owner.Picked(null, fx, 1 - fy);
        protected override void DrawHandle(DrawingContext ctx, Rect r) =>
            Ring(ctx, new Point(r.Width * Owner._s, r.Height * (1 - Owner._v)), 5);
    }

    // The hue wheel unrolled, left → right.
    private sealed class HueStrip : Surface
    {
        public HueStrip(ColourPicker owner) : base(owner) { }
        protected override double Key => 0;
        protected override uint Pixel(int x, int y, int w, int h) => 0xFF000000 | HsvToRgb(360.0 * x / Math.Max(1, w - 1), 1, 1);
        protected override void Pick(double fx, double fy) => Owner.Picked(fx * 359.999, null, null);
        protected override void DrawHandle(DrawingContext ctx, Rect r)
        {
            double x = Math.Round(r.Width * Owner._h / 360);
            var bar = new Rect(x - 2, -2, 4, r.Height + 4);
            ctx.DrawRectangle(NotaPalette.HandleSel, new Pen(NotaPalette.Seam, 1), new RoundedRect(bar, NotaRadius.BarValue));
        }
    }
}
