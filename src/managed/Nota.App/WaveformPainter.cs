// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

/// <summary>
/// The default waveform look for audio clips on the lanes and in the clip editor (#267): the
/// min/max peak envelope as one anti-aliased outline in a lighter ink, the RMS body inside it in
/// the full ink. Peak buckets are folded into device-pixel columns (every bucket under a column
/// counts, so nothing aliases away when zoomed out) and buckets wider than a column become one
/// vertex at their centre, joined by straight edges, so a zoomed-in wave slopes instead of
/// stepping. One instance per view; it reuses its buffers across repaints.
/// </summary>
internal sealed class WaveformPainter
{
    // Peak envelope over the clip body: ink at this alpha, the body at full ink.
    private const byte PeakAlpha = 0x8C;

    private double[] _x = new double[512];
    private float[] _lo = new float[512], _hi = new float[512], _rms = new float[512];
    private int _n;
    private double _col = 1;
    private bool _hasRms;
    private double _minX, _maxX;

    private bool _open;
    private long _key;
    private double _cx0, _cx1, _cSq, _cW;
    private float _cLo, _cHi;

    /// <summary>The ink for the peak envelope around a body drawn in <paramref name="ink"/>.</summary>
    public static IBrush PeakInk(IBrush ink) => ink is SolidColorBrush s ? NotaPalette.Wash(s, PeakAlpha) : ink;

    /// <summary>The device-pixel scale a control renders at (2 on a Retina display).</summary>
    public static double Scale(Visual v) => TopLevel.GetTopLevel(v)?.RenderScaling ?? 1.0;

    public void Begin(double renderScaling)
    {
        _col = 1.0 / Math.Max(0.25, renderScaling);
        _n = 0;
        _open = false;
        _hasRms = true;
        _minX = double.MaxValue;
        _maxX = double.MinValue;
    }

    /// <summary>One peak bucket spanning x0..x1 (DIPs), gain already applied. Buckets arrive
    /// in screen order, left→right or right→left. A negative rms means none is known: the
    /// wave then draws its envelope only.</summary>
    public void Add(double x0, double x1, float min, float max, float rms)
    {
        if (x1 < x0) (x0, x1) = (x1, x0);
        if (min > max) return;   // "no data" bucket
        if (rms < 0) _hasRms = false;
        _minX = Math.Min(_minX, x0);
        _maxX = Math.Max(_maxX, x1);
        double w = x1 - x0;
        if (w >= _col)
        {
            Flush();
            Emit((x0 + x1) / 2, min, max, rms);
            return;
        }
        long key = (long)Math.Floor((x0 + x1) / 2 / _col);
        if (_open && key != _key) Flush();
        if (!_open)
        {
            (_open, _key, _cx0, _cx1, _cLo, _cHi, _cSq, _cW) = (true, key, x0, x1, min, max, 0, 0);
        }
        else
        {
            _cx0 = Math.Min(_cx0, x0); _cx1 = Math.Max(_cx1, x1);
            _cLo = Math.Min(_cLo, min); _cHi = Math.Max(_cHi, max);
        }
        double ww = Math.Max(w, 1e-9);
        _cSq += (double)rms * rms * ww;
        _cW += ww;
    }

    /// <summary>Fills what was added: the envelope in <paramref name="peakInk"/>, the RMS body
    /// in <paramref name="bodyInk"/>, about <paramref name="mid"/> with ±1 at ±<paramref name="amp"/>.</summary>
    public void Fill(DrawingContext ctx, double mid, double amp, IBrush peakInk, IBrush bodyInk)
    {
        Flush();
        if (_n == 0 || amp <= 0) return;
        if (_x[0] > _x[_n - 1]) Reverse();
        // Hold the outermost buckets out to their edges, so the wave starts and ends where the
        // audio does rather than half a bucket in.
        if (_minX < _x[0] - 1e-6) Insert(0, _minX);
        if (_maxX > _x[_n - 1] + 1e-6) Insert(_n, _maxX);

        // Never thinner than a device pixel: silence still reads as a line.
        double minSpan = _col / amp;
        for (int i = 0; i < _n; i++)
        {
            double span = _hi[i] - _lo[i];
            if (span >= minSpan) continue;
            double c = (_hi[i] + _lo[i]) / 2;
            _lo[i] = (float)(c - minSpan / 2);
            _hi[i] = (float)(c + minSpan / 2);
        }

        var peak = new StreamGeometry();
        using (var g = peak.Open())
        {
            g.BeginFigure(new Point(_x[0], mid - _hi[0] * amp), true);
            for (int i = 1; i < _n; i++) g.LineTo(new Point(_x[i], mid - _hi[i] * amp));
            for (int i = _n - 1; i >= 0; i--) g.LineTo(new Point(_x[i], mid - _lo[i] * amp));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(peakInk, null, peak);
        if (!_hasRms) return;

        // The body is ±RMS about the centre line, cut to the bucket's own min..max: a bucket of a
        // few samples on one side of zero (zoomed to the samples) then thins to the trace
        // instead of filling down to the centre.
        var body = new StreamGeometry();
        using (var g = body.Open())
        {
            g.BeginFigure(new Point(_x[0], mid - Up(0) * amp), true);
            for (int i = 1; i < _n; i++) g.LineTo(new Point(_x[i], mid - Up(i) * amp));
            for (int i = _n - 1; i >= 0; i--) g.LineTo(new Point(_x[i], mid - Down(i) * amp));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(bodyInk, null, body);
    }

    private double Up(int i) => Math.Clamp(_rms[i], _lo[i], _hi[i]);
    private double Down(int i) => Math.Clamp(-_rms[i], _lo[i], _hi[i]);

    private void Flush()
    {
        if (!_open) return;
        _open = false;
        Emit((_cx0 + _cx1) / 2, _cLo, _cHi, (float)Math.Sqrt(_cSq / Math.Max(_cW, 1e-12)));
    }

    private void Emit(double x, float lo, float hi, float rms)
    {
        if (_n == _x.Length) Grow();
        _x[_n] = x; _lo[_n] = lo; _hi[_n] = hi; _rms[_n] = Math.Max(0f, rms);
        _n++;
    }

    private void Insert(int at, double x)
    {
        int from = at == 0 ? 0 : _n - 1;
        float lo = _lo[from], hi = _hi[from], rms = _rms[from];
        if (_n == _x.Length) Grow();
        if (at < _n)
        {
            Array.Copy(_x, at, _x, at + 1, _n - at);
            Array.Copy(_lo, at, _lo, at + 1, _n - at);
            Array.Copy(_hi, at, _hi, at + 1, _n - at);
            Array.Copy(_rms, at, _rms, at + 1, _n - at);
        }
        _x[at] = x; _lo[at] = lo; _hi[at] = hi; _rms[at] = rms;
        _n++;
    }

    private void Reverse()
    {
        Array.Reverse(_x, 0, _n);
        Array.Reverse(_lo, 0, _n);
        Array.Reverse(_hi, 0, _n);
        Array.Reverse(_rms, 0, _n);
    }

    private void Grow()
    {
        int n = _x.Length * 2;
        Array.Resize(ref _x, n);
        Array.Resize(ref _lo, n);
        Array.Resize(ref _hi, n);
        Array.Resize(ref _rms, n);
    }
}
