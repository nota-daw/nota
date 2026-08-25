// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.

namespace Nota.Domain;

/// <summary>A single MIDI note inside a clip or session slot. Times are relative
/// to the clip start, in beats.</summary>
public sealed class Note
{
    public Pitch Pitch { get; set; }
    public Beats Start { get; set; }
    public Beats Length { get; set; }
    public Velocity Velocity { get; set; } = Velocity.Full;

    public Note() { }

    public Note(Pitch pitch, Beats start, Beats length, Velocity velocity)
    {
        Pitch = pitch;
        Start = start;
        Length = length;
        Velocity = velocity;
    }
}
