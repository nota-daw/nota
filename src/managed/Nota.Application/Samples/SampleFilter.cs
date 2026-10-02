// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Application.Samples;

/// <summary>The Files tab's sample filter — "loops · 120–128 · A minor". Every part is
/// optional; a sample the index hasn't analysed yet passes only the empty filter.</summary>
public sealed record SampleFilter(SampleKind Kind = SampleKind.Unknown, double BpmMin = 0, double BpmMax = 0, MusicalKey? Key = null)
{
    public static readonly SampleFilter Empty = new();

    public bool IsEmpty => Kind == SampleKind.Unknown && !HasBpm && Key is null;
    public bool HasBpm => BpmMin > 0 || BpmMax > 0;

    public bool Matches(SampleInfo? s)
    {
        if (IsEmpty) return true;
        if (s is null) return false;
        if (Kind != SampleKind.Unknown && s.Kind != Kind) return false;
        if (HasBpm)
        {
            if (s.Bpm <= 0) return false;
            // Ranges are inclusive of what the row shows: 127.96 reads "128" and passes 120–128.
            double b = Math.Round(s.Bpm, 1);
            if (BpmMin > 0 && b < BpmMin - 0.05) return false;
            if (BpmMax > 0 && b > BpmMax + 0.05) return false;
        }
        // A key matches itself and its relative (A minor shows C major loops — same notes).
        if (Key is { } k && (s.Key is not { } sk || !sk.FitsIn(k))) return false;
        return true;
    }

    /// <summary>"120–128", "≥ 120", "≤ 90", "" — the BPM chip's caption.</summary>
    public string BpmCaption => (BpmMin > 0, BpmMax > 0) switch
    {
        (true, true) when Math.Abs(BpmMin - BpmMax) < 0.05 => SampleInfo.FormatBpm(BpmMin),
        (true, true) => $"{SampleInfo.FormatBpm(BpmMin)}–{SampleInfo.FormatBpm(BpmMax)}",
        (true, false) => $"≥ {SampleInfo.FormatBpm(BpmMin)}",
        (false, true) => $"≤ {SampleInfo.FormatBpm(BpmMax)}",
        _ => "",
    };
}
