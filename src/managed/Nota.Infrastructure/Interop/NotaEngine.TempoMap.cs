// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;
using System.Runtime.InteropServices;

namespace Nota.Infrastructure;

public sealed partial class NotaEngine
{
    public void SetTempoMap(IReadOnlyList<TempoPoint> points)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(points));

        var beats = new double[points.Count];
        var bpms = new double[points.Count];
        var curves = new int[points.Count];
        double previousBeat = -1.0;
        for (int i = 0; i < points.Count; i++)
        {
            var point = points[i];
            if (!double.IsFinite(point.Beat) || point.Beat < 0.0 || point.Beat <= previousBeat
                || !double.IsFinite(point.Bpm) || point.Bpm < 1.0 || point.Bpm > 999.0
                || (int)point.Interpolation < 0 || (int)point.Interpolation > 3)
                throw new ArgumentException("The tempo map must contain sorted, valid tempo points.", nameof(points));
            if (i == 0 && Math.Abs(point.Beat) > 1e-9)
                throw new ArgumentException("The tempo map must begin at beat zero.", nameof(points));
            beats[i] = point.Beat;
            bpms[i] = point.Bpm;
            curves[i] = (int)point.Interpolation;
            previousBeat = point.Beat;
        }

        var hb = GCHandle.Alloc(beats, GCHandleType.Pinned);
        var hv = GCHandle.Alloc(bpms, GCHandleType.Pinned);
        var hc = GCHandle.Alloc(curves, GCHandleType.Pinned);
        try
        {
            Check(NativeMethods.SetTempoMap(_handle, hb.AddrOfPinnedObject(), hv.AddrOfPinnedObject(),
                hc.AddrOfPinnedObject(), points.Count));
        }
        finally
        {
            hc.Free();
            hv.Free();
            hb.Free();
        }
    }

    public IReadOnlyList<TempoPoint> GetTempoMap()
    {
        ThrowIfDisposed();
        int count = NativeMethods.GetTempoMap(_handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0);
        if (count is < 1 or > 4096) throw new InvalidOperationException("The native tempo map is invalid.");

        var beats = new double[count];
        var bpms = new double[count];
        var curves = new int[count];
        var hb = GCHandle.Alloc(beats, GCHandleType.Pinned);
        var hv = GCHandle.Alloc(bpms, GCHandleType.Pinned);
        var hc = GCHandle.Alloc(curves, GCHandleType.Pinned);
        try
        {
            int written = NativeMethods.GetTempoMap(_handle, hb.AddrOfPinnedObject(), hv.AddrOfPinnedObject(),
                hc.AddrOfPinnedObject(), count);
            if (written != count) throw new InvalidOperationException("The native tempo map changed while it was being read.");
            var points = new TempoPoint[count];
            for (int i = 0; i < count; i++)
                points[i] = new TempoPoint(beats[i], bpms[i], (TempoInterpolation)curves[i]);
            return points;
        }
        finally
        {
            hc.Free();
            hv.Free();
            hb.Free();
        }
    }
}
