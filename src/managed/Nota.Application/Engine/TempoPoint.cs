// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Application;

public enum TempoShape { Step, Linear, Smooth }

/// <summary>Outgoing tempo segment with normalized cubic Bezier handles.</summary>
public readonly record struct TempoPoint(double Beat, double Bpm, TempoShape Shape = TempoShape.Step,
    double X1 = 1.0 / 3, double Y1 = 0, double X2 = 2.0 / 3, double Y2 = 1, double IncomingBpm = 0);

/// <summary>Authoring-only preview compiled by the authoritative engine map.</summary>
public interface ITempoPreview : IDisposable
{
    double BeatToSeconds(double beat);
    double SecondsToBeat(double seconds);
    double TempoAt(double beat);
}
