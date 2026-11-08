// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The card size of a built-in audio effect that comes in more than one size (Nota Auto Filter,
// #531). The size is the device's own "View" param (0 = L, 1 = S, and 2 = XL for the devices
// whose View goes up to 2: Nota EQ-8 and Dynamic EQ-8, #540), so it saves with the
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

    /// <summary>The card size as it is set now; L for a device with one size.</summary>
    public static CardSize Size(IAudioEngine engine, int trackId, int deviceIndex)
    {
        int i = Index(engine, trackId, deviceIndex);
        if (i < 0) return CardSize.L;
        int v = (int)MathF.Round(engine.DeviceGetParam(trackId, deviceIndex, i));
        return v switch { 1 => CardSize.S, 2 => CardSize.XL, _ => CardSize.L };
    }

    /// <summary>Sets the card size. XL is written only for a device whose View goes up to 2.</summary>
    public static void SetSize(IAudioEngine engine, int trackId, int deviceIndex, CardSize size)
    {
        int i = Index(engine, trackId, deviceIndex);
        if (i < 0 || (size == CardSize.XL && !SupportsXlParam(engine, trackId, deviceIndex))) return;
        engine.DeviceSetParam(trackId, deviceIndex, i, (float)size);
    }

    /// <summary>True when the device's View param can hold XL (its max is 2).</summary>
    public static bool SupportsXlParam(IAudioEngine engine, int trackId, int deviceIndex)
        => Index(engine, trackId, deviceIndex) is var i and >= 0 && engine.DeviceParamMax(trackId, deviceIndex, i) >= 1.5f;

    /// <summary>True when the device's card is set to the mini (S) size.</summary>
    public static bool IsMini(IAudioEngine engine, int trackId, int deviceIndex) => Size(engine, trackId, deviceIndex) == CardSize.S;

    public static void SetMini(IAudioEngine engine, int trackId, int deviceIndex, bool mini)
        => SetSize(engine, trackId, deviceIndex, mini ? CardSize.S : CardSize.L);
}

/// <summary>A device card's size: L (the full card), S (260 wide), XL (the panel above the chain, #540).</summary>
public enum CardSize { L = 0, S = 1, XL = 2 }
