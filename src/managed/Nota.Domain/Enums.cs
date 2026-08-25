// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.

namespace Nota.Domain;

/// <summary>Track flavour. Numeric values mirror the engine's track type codes
/// (0 audio / 1 instrument / 2 return) so mapping across the interop boundary is
/// a plain cast — but Domain code should always use the named members.</summary>
public enum TrackKind
{
    Audio = 0,
    Instrument = 1,
    Return = 2,
}

/// <summary>What produces sound on an instrument track.</summary>
public enum InstrumentKind
{
    Synth,
    Sampler,
    Plugin,
}

/// <summary>Effect device flavour. The five built-ins plus a hosted plug-in.</summary>
public enum DeviceKind
{
    Eq,
    Compressor,
    Reverb,
    Delay,
    Utility,
    Plugin,
}
