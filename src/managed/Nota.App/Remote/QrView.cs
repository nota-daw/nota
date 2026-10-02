// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A QR code drawn as geometry. Phone cameras read dark modules on a light ground reliably and
// an inverted code poorly, so the tile is light in both variants: Ink 0 under Graphite, Raised
// under Paper, with the modules in the opposite ink. Colours are read at render time.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Net.Codecrete.QrCodeGenerator;

namespace Nota.App;

internal sealed class QrView : Control
{
    private QrCode? _qr;
    private string? _text;

    public string? Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value;
            _qr = string.IsNullOrEmpty(value) ? null : QrCode.EncodeText(value, QrCode.Ecc.Medium);
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext ctx)
    {
        bool dark = Avalonia.Application.Current?.ActualThemeVariant != ThemeVariant.Light;
        var ground = dark ? NotaPalette.TextHeading : NotaPalette.SurfaceRaised;
        var ink = dark ? NotaPalette.BgApp : NotaPalette.TextHeading;
        var r = new Rect(Bounds.Size);
        ctx.DrawRectangle(ground, null, r, NotaRadius.Panel.TopLeft, NotaRadius.Panel.TopLeft);
        if (_qr is null) return;

        const int quiet = 3;   // modules of margin; the tile's own padding adds the rest
        int n = _qr.Size + quiet * 2;
        double cell = System.Math.Floor(System.Math.Min(r.Width, r.Height) / n * 4) / 4;
        double ox = (r.Width - cell * _qr.Size) / 2, oy = (r.Height - cell * _qr.Size) / 2;
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            for (int y = 0; y < _qr.Size; y++)
                for (int x = 0; x < _qr.Size; x++)
                {
                    if (!_qr.GetModule(x, y)) continue;
                    double px = ox + x * cell, py = oy + y * cell;
                    g.BeginFigure(new Point(px, py), true);
                    g.LineTo(new Point(px + cell, py));
                    g.LineTo(new Point(px + cell, py + cell));
                    g.LineTo(new Point(px, py + cell));
                    g.EndFigure(true);
                }
        }
        ctx.DrawGeometry(ink, null, geo);
    }
}
