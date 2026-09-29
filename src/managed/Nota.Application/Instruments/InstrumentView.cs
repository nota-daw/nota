// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The S / L card size of a built-in instrument that comes in two sizes (Nota Synth). The
// size is the instrument's own "view" param (0 = L, 1 = S), so it saves with the project's
// state blob and follows a duplicated track; its default in the engine is the instrument's
// default size. It is editor state, not sound: presets neither save nor apply it and it is
// not offered for automation.

namespace Nota.Application;

public static class InstrumentView
{
    public const string ParamId = "view";

    /// <summary>True for the param id that holds the card size — skip it wherever params
    /// are treated as the sound (presets, automation targets, generic knob lists).</summary>
    public static bool IsViewParam(string id) => id == ParamId;

    /// <summary>The index of the instrument's view param, or -1 when it has only one size.</summary>
    public static int Index(IAudioEngine engine, int trackId)
    {
        int pc = engine.PluginParamCount(trackId, -1);
        for (int i = pc - 1; i >= 0; i--)
            if (engine.PluginParamId(trackId, -1, i) == ParamId) return i;
        return -1;
    }

    /// <summary>True when the instrument's card is set to the mini (S) size.</summary>
    public static bool IsMini(IAudioEngine engine, int trackId)
        => Index(engine, trackId) is var i and >= 0 && engine.PluginParamGet(trackId, -1, i) >= 0.5f;

    public static void SetMini(IAudioEngine engine, int trackId, bool mini)
    {
        if (Index(engine, trackId) is var i and >= 0) engine.PluginParamSet(trackId, -1, i, mini ? 1f : 0f);
    }
}
