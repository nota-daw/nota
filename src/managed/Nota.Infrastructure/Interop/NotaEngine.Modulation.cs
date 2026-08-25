// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Infrastructure;

public sealed partial class NotaEngine
{
    /// <summary>Device-free self-test (Phase 3): LFO eval + apply/restore + link cleanup.</summary>
    public bool ModulationSelfTest()
    { ThrowIfDisposed(); return NativeMethods.ModulationSelfTest(_handle) != 0; }

    public int ModulatorAdd(int trackId, int kind)
    { ThrowIfDisposed(); return NativeMethods.AddModulator(_handle, trackId, kind); }
    public void ModulatorRemove(int trackId, int modId)
    { ThrowIfDisposed(); NativeMethods.RemoveModulator(_handle, trackId, modId); }
    public int ModulatorCount(int trackId)
    { ThrowIfDisposed(); return NativeMethods.ModulatorCount(_handle, trackId); }
    public int ModulatorIdAt(int trackId, int index)
    { ThrowIfDisposed(); return NativeMethods.ModulatorIdAt(_handle, trackId, index); }
    public int ModulatorKind(int trackId, int modId)
    { ThrowIfDisposed(); return NativeMethods.ModulatorKind(_handle, trackId, modId); }
    public float ModulatorGet(int trackId, int modId, int field)
    { ThrowIfDisposed(); return NativeMethods.ModulatorGet(_handle, trackId, modId, field); }
    public void ModulatorSet(int trackId, int modId, int field, float value)
    { ThrowIfDisposed(); NativeMethods.ModulatorSet(_handle, trackId, modId, field, value); }
    public float ModulatorValue(int trackId, int modId)
    { ThrowIfDisposed(); return NativeMethods.ModulatorValue(_handle, trackId, modId); }
    public int ModulatorScope(int trackId, int modId, float[] outv)
    { ThrowIfDisposed(); return NativeMethods.ModulatorScope(_handle, trackId, modId, outv, outv.Length); }

    public int CvLinkAdd(int trackId, int modId, int deviceIndex, int paramIndex)
    { ThrowIfDisposed(); return NativeMethods.AddCvLink(_handle, trackId, modId, deviceIndex, paramIndex); }
    public int CvLinkAddTo(int trackId, int modId, int targetTrack, int deviceIndex, int paramIndex)
    { ThrowIfDisposed(); return NativeMethods.AddCvLinkTo(_handle, trackId, modId, targetTrack, deviceIndex, paramIndex); }
    public int CvLinkAddFromParam(int trackId, int srcDevice, int srcParam, int targetTrack, int targetDevice, int targetParam)
    { ThrowIfDisposed(); return NativeMethods.AddCvLinkFromParam(_handle, trackId, srcDevice, srcParam, targetTrack, targetDevice, targetParam); }
    public int CvLinkAddToTarget(int trackId, int modId, int targetKind, int targetTrack, int targetDevice, int targetParam)
    { ThrowIfDisposed(); return NativeMethods.AddCvLinkToTarget(_handle, trackId, modId, targetKind, targetTrack, targetDevice, targetParam); }
    public int CvLinkAddFromParamToTarget(int trackId, int srcDevice, int srcParam, int targetKind, int targetTrack, int targetDevice, int targetParam)
    { ThrowIfDisposed(); return NativeMethods.AddCvLinkFromParamTarget(_handle, trackId, srcDevice, srcParam, targetKind, targetTrack, targetDevice, targetParam); }
    public int CvLinkTargetKind(int trackId, int index)
    { ThrowIfDisposed(); return NativeMethods.CvLinkTargetKind(_handle, trackId, index); }
    public bool ParamModulated(int targetKind, int trackId, int deviceIndex, int paramIndex)
    { ThrowIfDisposed(); return NativeMethods.ParamModulated(_handle, targetKind, trackId, deviceIndex, paramIndex) != 0; }
    public int CvLinkSourceKind(int trackId, int index)
    { ThrowIfDisposed(); return NativeMethods.CvLinkSourceKind(_handle, trackId, index); }
    public int CvLinkSourceDevice(int trackId, int index)
    { ThrowIfDisposed(); return NativeMethods.CvLinkSourceDevice(_handle, trackId, index); }
    public int CvLinkSourceParam(int trackId, int index)
    { ThrowIfDisposed(); return NativeMethods.CvLinkSourceParam(_handle, trackId, index); }
    public int CvLinkTargetTrack(int trackId, int index)
    { ThrowIfDisposed(); return NativeMethods.CvLinkTargetTrack(_handle, trackId, index); }
    public void CvLinkRemove(int trackId, int index)
    { ThrowIfDisposed(); NativeMethods.RemoveCvLink(_handle, trackId, index); }
    public int CvLinkCount(int trackId)
    { ThrowIfDisposed(); return NativeMethods.CvLinkCount(_handle, trackId); }
    public int CvLinkSource(int trackId, int index)
    { ThrowIfDisposed(); return NativeMethods.CvLinkSource(_handle, trackId, index); }
    public int CvLinkDevice(int trackId, int index)
    { ThrowIfDisposed(); return NativeMethods.CvLinkDevice(_handle, trackId, index); }
    public int CvLinkParam(int trackId, int index)
    { ThrowIfDisposed(); return NativeMethods.CvLinkParam(_handle, trackId, index); }
    public float CvLinkDepth(int trackId, int index)
    { ThrowIfDisposed(); return NativeMethods.CvLinkDepth(_handle, trackId, index); }
    public int CvLinkMode(int trackId, int index)
    { ThrowIfDisposed(); return NativeMethods.CvLinkMode(_handle, trackId, index); }
    public void SetCvLinkDepth(int trackId, int index, float depth)
    { ThrowIfDisposed(); NativeMethods.SetCvLinkDepth(_handle, trackId, index, depth); }
    public void SetCvLinkMode(int trackId, int index, int mode)
    { ThrowIfDisposed(); NativeMethods.SetCvLinkMode(_handle, trackId, index, mode); }
    public float CvLinkBase(int trackId, int index)
    { ThrowIfDisposed(); return NativeMethods.CvLinkBase(_handle, trackId, index); }
    public void SetCvLinkBase(int trackId, int index, float baseValue)
    { ThrowIfDisposed(); NativeMethods.SetCvLinkBase(_handle, trackId, index, baseValue); }
    public bool DeviceParamModulated(int trackId, int deviceIndex, int paramIndex)
    { ThrowIfDisposed(); return NativeMethods.DeviceParamModulated(_handle, trackId, deviceIndex, paramIndex) != 0; }
}
