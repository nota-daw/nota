// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Preset tags (specs/048): which preset a device was loaded from, and whether it still holds
// that preset's values.

using Nota.Application;

namespace Nota.Infrastructure;

public sealed partial class NotaEngine
{
    public int DevicePresetTag(int trackId, PresetSurface surface, int index)
    { ThrowIfDisposed(); return NativeMethods.DevicePresetTag(_handle, trackId, (int)surface, index); }

    public int SetDevicePresetTag(int trackId, PresetSurface surface, int index, string presetId, string name, float[]? loaded = null)
    {
        ThrowIfDisposed();
        return NativeMethods.DeviceSetPresetTag(_handle, trackId, (int)surface, index, presetId, name, loaded, loaded?.Length ?? 0);
    }

    public PresetTag? PresetTagInfo(int tag)
    {
        ThrowIfDisposed();
        if (tag <= 0) return null;
        var id = new byte[512]; var name = new byte[512];
        int n = NativeMethods.PresetTagInfo(_handle, tag, id, id.Length, name, name.Length, null, 0);
        if (n < 0) return null;
        var values = new float[n];
        if (n > 0) NativeMethods.PresetTagInfo(_handle, tag, id, id.Length, name, name.Length, values, n);
        return new PresetTag(Utf8Z(id), Utf8Z(name), values);
    }

    public int DevicePresetModified(int trackId, PresetSurface surface, int index)
    { ThrowIfDisposed(); return NativeMethods.DevicePresetModified(_handle, trackId, (int)surface, index); }

    public bool PresetTagSelfTest() { ThrowIfDisposed(); return NativeMethods.PresetTagSelfTest(_handle) != 0; }

    private static string Utf8Z(byte[] b)
    {
        int n = System.Array.IndexOf(b, (byte)0);
        return System.Text.Encoding.UTF8.GetString(b, 0, n < 0 ? b.Length : n);
    }
}
