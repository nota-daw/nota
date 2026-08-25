// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;

namespace Nota.App;

/// <summary>
/// Shared decibel ↔ linear-amplitude conversion — the 20·log10 / 10^(dB/20) pair
/// that used to be inlined across the meters, mixer and track headers. Callers keep
/// their own floor thresholds, clamps and display formats (those differ per view).
/// </summary>
internal static class AudioMath
{
    /// <summary>Linear amplitude → dBFS. The caller must guard amp &gt; 0 (log10(0) = -∞).</summary>
    public static double LinToDb(double amp) => 20.0 * Math.Log10(amp);

    /// <summary>dBFS → linear amplitude.</summary>
    public static double DbToLin(double db) => Math.Pow(10.0, db / 20.0);
}
