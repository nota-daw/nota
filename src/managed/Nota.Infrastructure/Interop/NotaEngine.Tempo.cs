// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System.Runtime.InteropServices;

namespace Nota.Infrastructure;

public sealed partial class NotaEngine
{
    public IReadOnlyList<TempoPoint> TempoPoints
    {
        get
        {
            ThrowIfDisposed();
            var points = new TempoPoint[NativeMethods.TempoPointCount(_handle)];
            for (int i = 0; i < points.Length; ++i)
            {
                Check(NativeMethods.TempoGetPoint(_handle, i, out var p));
                points[i] = new(p.Beat, p.Bpm, (TempoShape)p.Shape, p.X1, p.Y1, p.X2, p.Y2, p.IncomingBpm);
            }
            return points;
        }
    }
    public void SetTempoPoints(IReadOnlyList<TempoPoint> points, bool checkpoint = true)
    {
        ThrowIfDisposed();
        var rows = points.Select(p => new NativeMethods.TempoPointNative
        { Beat = p.Beat, Bpm = p.Bpm, Shape = (int)p.Shape, X1 = p.X1, Y1 = p.Y1, X2 = p.X2, Y2 = p.Y2, IncomingBpm = p.IncomingBpm }).ToArray();
        Check(NativeMethods.TempoSetPoints(_handle, rows, rows.Length, checkpoint ? 1 : 0));
    }
    public double BeatToSeconds(double beat) { ThrowIfDisposed(); return NativeMethods.TempoBeatToSeconds(_handle, beat); }
    public double SecondsToBeat(double seconds) { ThrowIfDisposed(); return NativeMethods.TempoSecondsToBeat(_handle, seconds); }
    public double TempoAt(double beat) { ThrowIfDisposed(); return NativeMethods.TempoBpmAt(_handle, beat); }
    public ITempoPreview CreateTempoPreview(IReadOnlyList<TempoPoint> points)
    {
        ThrowIfDisposed();
        var rows = points.Select(p => new NativeMethods.TempoPointNative
        { Beat = p.Beat, Bpm = p.Bpm, Shape = (int)p.Shape, X1 = p.X1, Y1 = p.Y1, X2 = p.X2, Y2 = p.Y2, IncomingBpm = p.IncomingBpm }).ToArray();
        var handle = NativeMethods.TempoPreviewCreate(rows, rows.Length);
        if (handle == IntPtr.Zero) throw new ArgumentException("Invalid tempo preview.", nameof(points));
        return new TempoPreview(handle);
    }
    private sealed class TempoPreview(IntPtr handle) : ITempoPreview
    {
        private IntPtr _map = handle;
        public double BeatToSeconds(double beat) { ObjectDisposedException.ThrowIf(_map == IntPtr.Zero, this); return NativeMethods.TempoPreviewSeconds(_map, beat); }
        public double SecondsToBeat(double seconds) { ObjectDisposedException.ThrowIf(_map == IntPtr.Zero, this); return NativeMethods.TempoPreviewBeat(_map, seconds); }
        public double TempoAt(double beat) { ObjectDisposedException.ThrowIf(_map == IntPtr.Zero, this); return NativeMethods.TempoPreviewBpm(_map, beat); }
        public void Dispose() { if (_map != IntPtr.Zero) NativeMethods.TempoPreviewDestroy(_map); _map = IntPtr.Zero; }
    }
}

internal static partial class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct TempoPointNative
    {
        public double Beat, Bpm;
        public int Shape;
        public double X1, Y1, X2, Y2;
        public double IncomingBpm;
    }
    [LibraryImport(Lib, EntryPoint = "nota_tempo_point_count")]
    internal static partial int TempoPointCount(IntPtr e);
    [LibraryImport(Lib, EntryPoint = "nota_tempo_get_point")]
    internal static partial NotaResult TempoGetPoint(IntPtr e, int index, out TempoPointNative point);
    [LibraryImport(Lib, EntryPoint = "nota_tempo_set_points")]
    internal static partial NotaResult TempoSetPoints(IntPtr e, [In] TempoPointNative[] points, int count, int checkpoint);
    [LibraryImport(Lib, EntryPoint = "nota_tempo_beat_to_seconds")]
    internal static partial double TempoBeatToSeconds(IntPtr e, double beat);
    [LibraryImport(Lib, EntryPoint = "nota_tempo_seconds_to_beat")]
    internal static partial double TempoSecondsToBeat(IntPtr e, double seconds);
    [LibraryImport(Lib, EntryPoint = "nota_tempo_bpm_at")]
    internal static partial double TempoBpmAt(IntPtr e, double beat);
    [LibraryImport(Lib, EntryPoint = "nota_tempo_preview_create")]
    internal static partial IntPtr TempoPreviewCreate([In] TempoPointNative[] points, int count);
    [LibraryImport(Lib, EntryPoint = "nota_tempo_preview_destroy")]
    internal static partial void TempoPreviewDestroy(IntPtr map);
    [LibraryImport(Lib, EntryPoint = "nota_tempo_preview_seconds")]
    internal static partial double TempoPreviewSeconds(IntPtr map, double beat);
    [LibraryImport(Lib, EntryPoint = "nota_tempo_preview_beat")]
    internal static partial double TempoPreviewBeat(IntPtr map, double seconds);
    [LibraryImport(Lib, EntryPoint = "nota_tempo_preview_bpm")]
    internal static partial double TempoPreviewBpm(IntPtr map, double beat);
}
