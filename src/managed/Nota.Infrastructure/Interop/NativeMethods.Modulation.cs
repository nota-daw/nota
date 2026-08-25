// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.

using System.Runtime.InteropServices;

namespace Nota.Infrastructure;

/// <summary>CV modulation (Phase 3, Modular editor): LFO modulator sources + CV links to device params.</summary>
/// <remarks>Part of <see cref="NotaEngine"/>'s P/Invoke surface; see nota_engine.h.</remarks>
internal static partial class NativeMethods
{
    [LibraryImport(Lib, EntryPoint = "nota_engine_modulation_selftest")]
    internal static partial int ModulationSelfTest(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_track_add_modulator")]
    internal static partial int AddModulator(IntPtr engine, int trackId, int kind);

    [LibraryImport(Lib, EntryPoint = "nota_track_remove_modulator")]
    internal static partial NotaResult RemoveModulator(IntPtr engine, int trackId, int modId);

    [LibraryImport(Lib, EntryPoint = "nota_track_modulator_count")]
    internal static partial int ModulatorCount(IntPtr engine, int trackId);

    [LibraryImport(Lib, EntryPoint = "nota_track_modulator_id_at")]
    internal static partial int ModulatorIdAt(IntPtr engine, int trackId, int index);

    [LibraryImport(Lib, EntryPoint = "nota_track_modulator_kind")]
    internal static partial int ModulatorKind(IntPtr engine, int trackId, int modId);

    [LibraryImport(Lib, EntryPoint = "nota_track_modulator_get")]
    internal static partial float ModulatorGet(IntPtr engine, int trackId, int modId, int field);

    [LibraryImport(Lib, EntryPoint = "nota_track_modulator_set")]
    internal static partial NotaResult ModulatorSet(IntPtr engine, int trackId, int modId, int field, float value);

    [LibraryImport(Lib, EntryPoint = "nota_track_modulator_value")]
    internal static partial float ModulatorValue(IntPtr engine, int trackId, int modId);

    [LibraryImport(Lib, EntryPoint = "nota_track_modulator_scope")]
    internal static partial int ModulatorScope(IntPtr engine, int trackId, int modId, [Out] float[] outv, int cap);

    [LibraryImport(Lib, EntryPoint = "nota_track_add_cv_link")]
    internal static partial int AddCvLink(IntPtr engine, int trackId, int modId, int deviceIndex, int paramIndex);

    [LibraryImport(Lib, EntryPoint = "nota_track_add_cv_link_to")]
    internal static partial int AddCvLinkTo(IntPtr engine, int trackId, int modId, int targetTrack, int deviceIndex, int paramIndex);

    [LibraryImport(Lib, EntryPoint = "nota_track_add_cv_link_from_param")]
    internal static partial int AddCvLinkFromParam(IntPtr engine, int trackId, int srcDevice, int srcParam, int targetTrack, int targetDevice, int targetParam);

    [LibraryImport(Lib, EntryPoint = "nota_track_add_cv_link_to_target")]
    internal static partial int AddCvLinkToTarget(IntPtr engine, int trackId, int modId, int targetKind, int targetTrack, int targetDevice, int targetParam);

    [LibraryImport(Lib, EntryPoint = "nota_track_add_cv_link_from_param_target")]
    internal static partial int AddCvLinkFromParamTarget(IntPtr engine, int trackId, int srcDevice, int srcParam, int targetKind, int targetTrack, int targetDevice, int targetParam);

    [LibraryImport(Lib, EntryPoint = "nota_track_cv_link_target_kind")]
    internal static partial int CvLinkTargetKind(IntPtr engine, int trackId, int index);

    [LibraryImport(Lib, EntryPoint = "nota_track_param_modulated")]
    internal static partial int ParamModulated(IntPtr engine, int targetKind, int trackId, int deviceIndex, int paramIndex);

    [LibraryImport(Lib, EntryPoint = "nota_track_cv_link_source_kind")]
    internal static partial int CvLinkSourceKind(IntPtr engine, int trackId, int index);

    [LibraryImport(Lib, EntryPoint = "nota_track_cv_link_source_device")]
    internal static partial int CvLinkSourceDevice(IntPtr engine, int trackId, int index);

    [LibraryImport(Lib, EntryPoint = "nota_track_cv_link_source_param")]
    internal static partial int CvLinkSourceParam(IntPtr engine, int trackId, int index);

    [LibraryImport(Lib, EntryPoint = "nota_track_cv_link_target_track")]
    internal static partial int CvLinkTargetTrack(IntPtr engine, int trackId, int index);

    [LibraryImport(Lib, EntryPoint = "nota_track_remove_cv_link")]
    internal static partial NotaResult RemoveCvLink(IntPtr engine, int trackId, int index);

    [LibraryImport(Lib, EntryPoint = "nota_track_cv_link_count")]
    internal static partial int CvLinkCount(IntPtr engine, int trackId);

    [LibraryImport(Lib, EntryPoint = "nota_track_cv_link_source")]
    internal static partial int CvLinkSource(IntPtr engine, int trackId, int index);

    [LibraryImport(Lib, EntryPoint = "nota_track_cv_link_device")]
    internal static partial int CvLinkDevice(IntPtr engine, int trackId, int index);

    [LibraryImport(Lib, EntryPoint = "nota_track_cv_link_param")]
    internal static partial int CvLinkParam(IntPtr engine, int trackId, int index);

    [LibraryImport(Lib, EntryPoint = "nota_track_cv_link_depth")]
    internal static partial float CvLinkDepth(IntPtr engine, int trackId, int index);

    [LibraryImport(Lib, EntryPoint = "nota_track_cv_link_mode")]
    internal static partial int CvLinkMode(IntPtr engine, int trackId, int index);

    [LibraryImport(Lib, EntryPoint = "nota_track_set_cv_link_depth")]
    internal static partial NotaResult SetCvLinkDepth(IntPtr engine, int trackId, int index, float depth);

    [LibraryImport(Lib, EntryPoint = "nota_track_set_cv_link_mode")]
    internal static partial NotaResult SetCvLinkMode(IntPtr engine, int trackId, int index, int mode);

    [LibraryImport(Lib, EntryPoint = "nota_track_cv_link_base")]
    internal static partial float CvLinkBase(IntPtr engine, int trackId, int index);

    [LibraryImport(Lib, EntryPoint = "nota_track_set_cv_link_base")]
    internal static partial NotaResult SetCvLinkBase(IntPtr engine, int trackId, int index, float baseValue);

    [LibraryImport(Lib, EntryPoint = "nota_track_device_param_modulated")]
    internal static partial int DeviceParamModulated(IntPtr engine, int trackId, int deviceIndex, int paramIndex);
}
