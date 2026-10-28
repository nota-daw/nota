// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The picture of a hosted plug-in's own window behind its device card (specs/046): the
// engine keeps a JPEG of the window as it was last hidden; this draws it covering the area
// — one uniform scale, anchored to the top where plug-ins put their logo, centred across,
// cropped, never stretched — under NotaPalette.PluginPreviewWash, the card's own surface
// colour, so the card's text reads as on the plain card in every theme. Decoration only: it takes no clicks.
// No preview yet → draws nothing, the card looks as it always did.

using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Nota.Application;

namespace Nota.App;

internal sealed class PluginPreviewBackdrop : Control
{
    // Decoded pictures by serial: cards are rebuilt often, the pictures rarely change.
    private const int CacheSize = 32;
    private static readonly Dictionary<int, Bitmap> Cache = new();
    private static readonly LinkedList<int> CacheOrder = new();

    private readonly IAudioEngine _engine;
    private readonly int _track, _device;
    private int _serial = -1;
    private Bitmap? _image;

    public PluginPreviewBackdrop(IAudioEngine engine, int track, int deviceIndex)
    {
        _engine = engine; _track = track; _device = deviceIndex;
        IsHitTestVisible = false;
        ClipToBounds = true;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);   // downscaled on Retina-less screens
        Sync();
    }

    /// <summary>Picks up a new capture: cheap enough for the device refresher's tick (an int
    /// compare); the window's own close button hides it without telling the app.</summary>
    public void Sync()
    {
        int serial;
        try { serial = _engine.PluginPreviewSerial(_track, _device); }
        catch { return; }   // the device is gone (the card is about to rebuild)
        if (serial == _serial) return;
        _serial = serial;
        _image = serial == 0 ? null : Decode(serial);
        InvalidateVisual();
    }

    private Bitmap? Decode(int serial)
    {
        if (Cache.TryGetValue(serial, out var hit)) return hit;
        try
        {
            var jpeg = _engine.GetPluginPreview(_track, _device);
            if (jpeg.Length == 0) return null;
            using var ms = new MemoryStream(jpeg);
            var bmp = new Bitmap(ms);
            Cache[serial] = bmp;
            CacheOrder.AddLast(serial);
            // Evicted pictures may still be on a card: leave them to the GC rather than dispose.
            if (CacheOrder.Count > CacheSize) { Cache.Remove(CacheOrder.First!.Value); CacheOrder.RemoveFirst(); }
            return bmp;
        }
        catch { return null; }   // a damaged blob: the plain card
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        NotaPalette.Changed += OnThemeChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        NotaPalette.Changed -= OnThemeChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnThemeChanged() => InvalidateVisual();

    public override void Render(DrawingContext ctx)
    {
        if (_image is not { } img) return;
        double w = Bounds.Width, h = Bounds.Height, iw = img.Size.Width, ih = img.Size.Height;
        if (w <= 0 || h <= 0 || iw <= 0 || ih <= 0) return;

        // Cover: the larger of the two fits, so no edge is left empty and nothing stretches.
        double s = Math.Max(w / iw, h / ih);
        double dw = iw * s, dh = ih * s;
        var dest = new Rect((w - dw) / 2, 0, dw, dh);
        using (ctx.PushClip(new Rect(0, 0, w, h)))
            ctx.DrawImage(img, new Rect(img.Size), dest);

        ctx.FillRectangle(NotaPalette.PluginPreviewWash, new Rect(0, 0, w, h));
    }
}
