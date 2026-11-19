// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Where one controller is looking (specs/051): the first track of its bank of eight, the page
// of the focused device, and what its knobs do. Session state, kept per controller — two
// identical controllers bank independently — and reset when a project opens.

namespace Nota.Application.Controllers;

public enum KnobModeKind { Device, Volume, Pan, Send }

public readonly record struct KnobMode(KnobModeKind Kind, int Send = 0)
{
    public static readonly KnobMode Device = new(KnobModeKind.Device);
    /// <summary>For MCP and logs ("Device", "Pan", "Send" + the send letter); the UI labels it through Loc.</summary>
    public override string ToString() => Kind == KnobModeKind.Send ? string.Concat(Kind.ToString(), " ", ((char)('A' + Send)).ToString()) : Kind.ToString();
}

public sealed class ControllerState
{
    public const int BankSize = 8;

    public int BankOrigin { get; private set; }
    public int Page { get; set; }
    public KnobMode KnobMode { get; set; } = KnobMode.Device;
    public bool PickupOn { get; set; } = true;

    /// <summary>Shift the bank by <paramref name="delta"/> tracks (±8 bank, ±1 channel). A bank
    /// step never starts past the last full bank; a channel step never past the last track.
    /// Returns whether the origin moved.</summary>
    public bool BankStep(int delta, int trackCount)
    {
        int max = Math.Abs(delta) >= BankSize ? Math.Max(0, trackCount - BankSize) : Math.Max(0, trackCount - 1);
        int to = Math.Clamp(BankOrigin + delta, 0, max);
        if (delta > 0 && to < BankOrigin) to = BankOrigin;   // past the last full bank by channel steps: "next" stays
        if (to == BankOrigin) return false;
        BankOrigin = to;
        Touch();
        return true;
    }

    /// <summary>Move the bank the least amount that puts track <paramref name="index"/> inside it
    /// (after Track ◀ / ▶ selected a track, specs/107 FR-007). Returns whether the origin moved.</summary>
    public bool BankFollow(int index, int trackCount)
    {
        if (index < 0 || index >= trackCount) return false;
        int to = BankOrigin;
        if (index < BankOrigin) to = index;
        else if (index >= BankOrigin + BankSize) to = index - BankSize + 1;
        to = Math.Clamp(to, 0, Math.Max(0, trackCount - 1));
        if (to == BankOrigin) return false;
        BankOrigin = to;
        Touch();
        return true;
    }

    /// <summary>Keep the origin on a track that exists after tracks were removed.</summary>
    public void Clamp(int trackCount) => BankOrigin = Math.Clamp(BankOrigin, 0, Math.Max(0, trackCount - 1));

    /// <summary>Bumped on every change of what the controls point at (bank, page, mode, focus):
    /// pickup lets go of every control when it moves (FR-016a, FR-019).</summary>
    public int Epoch { get; private set; }

    public void Touch() => Epoch++;

    /// <summary>A mode button: Device, Volume and Pan switch to that mode; Send steps through the
    /// sends — A, B, … — and after the last one goes back to Device (FR-016a). Returns whether
    /// the mode changed.</summary>
    public bool StepMode(KnobModeKind kind, int sendCount)
    {
        var to = kind switch
        {
            KnobModeKind.Send when sendCount <= 0 => KnobMode.Device,
            KnobModeKind.Send when KnobMode.Kind == KnobModeKind.Send => KnobMode.Send + 1 < sendCount ? new KnobMode(KnobModeKind.Send, KnobMode.Send + 1) : KnobMode.Device,
            KnobModeKind.Send => new KnobMode(KnobModeKind.Send, 0),
            _ => new KnobMode(kind),
        };
        return SetMode(to);
    }

    /// <summary>Set the knob mode (the on-screen indicator picks one directly).</summary>
    public bool SetMode(KnobMode mode)
    {
        if (mode == KnobMode) return false;
        KnobMode = mode;
        Touch();
        return true;
    }

    /// <summary>Step the focused device's page by <paramref name="delta"/>, clamped to its pages.</summary>
    public bool PageStep(int delta, int pageCount)
    {
        int to = Math.Clamp(Page + delta, 0, Math.Max(0, pageCount - 1));
        if (to == Page) return false;
        Page = to;
        Touch();
        return true;
    }

    /// <summary>The knobs follow another device: back to its first page.</summary>
    public void FocusMoved()
    {
        Page = 0;
        Touch();
    }

    /// <summary>A project opened: first bank, first page, knobs on the device. Pickup stays.</summary>
    public void Reset()
    {
        BankOrigin = 0;
        Page = 0;
        KnobMode = KnobMode.Device;
        Touch();
    }
}
