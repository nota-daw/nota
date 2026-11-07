// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The S / L card size of a built-in audio effect that comes in two sizes (Nota Auto Filter,
// #531). The size is the device's own "View" param (0 = L, 1 = S), so it saves with the
// project and follows a duplicated device; old projects open in L. It is editor state, not
// sound: presets neither save nor apply it (the engine skips "View" by name), A/B leaves it
// alone and it is not offered for automation.

namespace Nota.Application;

public static class DeviceView
{
    public const string ParamName = "View";

    /// <summary>True for the param name that holds the card size.</summary>
    public static bool IsViewParam(string name) => name == ParamName;

    /// <summary>The index of the device's View param, or -1 when it has only one size.</summary>
    public static int Index(IAudioEngine engine, int trackId, int deviceIndex)
    {
        int pc = engine.DeviceParamCount(trackId, deviceIndex);
        for (int i = pc - 1; i >= 0; i--)
            if (engine.DeviceParamName(trackId, deviceIndex, i) == ParamName) return i;
        return -1;
    }

    /// <summary>True when the device's card is set to the mini (S) size.</summary>
    public static bool IsMini(IAudioEngine engine, int trackId, int deviceIndex)
        => Index(engine, trackId, deviceIndex) is var i and >= 0 && engine.DeviceGetParam(trackId, deviceIndex, i) >= 0.5f;

    public static void SetMini(IAudioEngine engine, int trackId, int deviceIndex, bool mini)
    {
        if (Index(engine, trackId, deviceIndex) is var i and >= 0) engine.DeviceSetParam(trackId, deviceIndex, i, mini ? 1f : 0f);
    }
}
