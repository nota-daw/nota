// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// P/Invoke for preset tags (specs/048): see nota_engine.h "Preset tags".

using System.Runtime.InteropServices;

namespace Nota.Infrastructure;

internal static partial class NativeMethods
{
    [LibraryImport(Lib, EntryPoint = "nota_track_device_preset_tag")]
    internal static partial int DevicePresetTag(IntPtr engine, int trackId, int surface, int index);

    [LibraryImport(Lib, EntryPoint = "nota_track_device_set_preset_tag", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int DeviceSetPresetTag(IntPtr engine, int trackId, int surface, int index,
        string? presetId, string? name, [In] float[]? baseValues, int baseCount);

    [LibraryImport(Lib, EntryPoint = "nota_preset_tag_info")]
    internal static partial int PresetTagInfo(IntPtr engine, int tag, [Out] byte[] presetId, int idCap,
        [Out] byte[] name, int nameCap, [Out] float[]? baseValues, int baseCap);

    [LibraryImport(Lib, EntryPoint = "nota_track_device_preset_modified")]
    internal static partial int DevicePresetModified(IntPtr engine, int trackId, int surface, int index);

    [LibraryImport(Lib, EntryPoint = "nota_engine_preset_tag_selftest")]
    internal static partial int PresetTagSelfTest(IntPtr engine);
}
