// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Turns the raw messages of a profiled control into what it means (specs/051 research R6):
// an absolute position 0..1, or a number of relative steps. Relative encoders come in the
// four conventions controller charts use; 14-bit CCs pair an MSB (CC n) with an LSB (CC n+32);
// NRPN assembles a parameter number (99/98) and data (6/38, or 96/97 increment / decrement).
// Pairing and NRPN state are per (source, channel), so two controllers never mix halves.

namespace Nota.Application.Controllers;

/// <summary>What a control's message means: an absolute position, or relative steps (signed).</summary>
public readonly record struct ControlValue(bool IsRelative, double Position, int Steps)
{
    public static ControlValue Absolute(double position) => new(false, Math.Clamp(position, 0, 1), 0);
    public static ControlValue Relative(int steps) => new(true, 0, steps);
}

public sealed class ControlDecoder
{
    // Last LSB seen per (source, channel, MSB CC 0..31), and whether one was ever seen: a
    // controller that sends only the MSB runs at 7-bit.
    private readonly Dictionary<(int, int, int), int> _lsb = new();
    private readonly Dictionary<(int, int, int), int> _msb = new();
    // NRPN: selected parameter and data MSB per (source, channel).
    private readonly Dictionary<(int, int), (int param, int dataMsb)> _nrpn = new();

    /// <summary>Relative value of one encoder byte in <paramref name="mode"/> (0 = no movement).</summary>
    public static int RelativeSteps(EncoderMode mode, int v) => mode switch
    {
        EncoderMode.RelativeTwos => v == 0 ? 0 : v < 64 ? v : v - 128,
        EncoderMode.RelativeSignBit => (v & 0x3F) * ((v & 0x40) != 0 ? -1 : 1),
        EncoderMode.RelativeOffset => v - 64,
        _ => 0,
    };

    /// <summary>Decode an incoming message for <paramref name="decl"/>; null when it carries no
    /// value for it (a half of a pair still waiting, a note-off, an unrelated NRPN).</summary>
    public ControlValue? Decode(ControlDecl decl, int source, int status, int channel, int d1, int d2)
    {
        switch (decl.Message)
        {
            case ControlMessage.Note:
                if (status is not (0x90 or 0x80)) return null;
                return ControlValue.Absolute(status == 0x90 && d2 > 0 ? d2 / 127.0 : 0.0);

            case ControlMessage.PitchBend:
                if (status != 0xE0) return null;
                return ControlValue.Absolute(((d2 << 7) | d1) / 16383.0);

            case ControlMessage.Cc:
                if (status != 0xB0) return null;
                return decl.Encoder == EncoderMode.Absolute
                    ? ControlValue.Absolute(d2 / 127.0)
                    : ControlValue.Relative(RelativeSteps(decl.Encoder, d2));

            case ControlMessage.Cc14:
            {
                if (status != 0xB0) return null;
                var key = (source, channel, decl.Number);
                if (d1 == decl.Number + 32)   // LSB: refines the last MSB
                {
                    _lsb[key] = d2;
                    return _msb.TryGetValue(key, out int m) ? ControlValue.Absolute(((m << 7) | d2) / 16383.0) : null;
                }
                _msb[key] = d2;
                return _lsb.TryGetValue(key, out int l)
                    ? ControlValue.Absolute(((d2 << 7) | l) / 16383.0)
                    : ControlValue.Absolute(d2 / 127.0);
            }

            case ControlMessage.Nrpn:
                return status == 0xB0 ? Nrpn(decl, source, channel, d1, d2) : null;
        }
        return null;
    }

    /// <summary>Feed the NRPN bookkeeping CCs (99 / 98) even when no declared control matches
    /// them yet, so the parameter number is known when the data arrives.</summary>
    public void Observe(int source, int status, int channel, int d1, int d2)
    {
        if (status != 0xB0) return;
        var key = (source, channel);
        _nrpn.TryGetValue(key, out var s);
        if (d1 == 99) _nrpn[key] = (((d2 & 0x7F) << 7) | (s.param & 0x7F), -1);
        else if (d1 == 98) _nrpn[key] = ((s.param & ~0x7F) | (d2 & 0x7F), -1);
    }

    /// <summary>The NRPN parameter selected on a source's channel, or null when none was.</summary>
    public int? NrpnParam(int source, int channel) => _nrpn.TryGetValue((source, channel), out var s) ? s.param : null;

    private ControlValue? Nrpn(ControlDecl decl, int source, int channel, int d1, int d2)
    {
        var key = (source, channel);
        if (!_nrpn.TryGetValue(key, out var s) || s.param != decl.Number) return null;
        switch (d1)
        {
            case 6:   // data MSB: apply now at 7-bit-in-14 resolution, refined by 38
                _nrpn[key] = (s.param, d2);
                return ControlValue.Absolute((d2 << 7) / 16383.0);
            case 38:
                return s.dataMsb >= 0 ? ControlValue.Absolute(((s.dataMsb << 7) | d2) / 16383.0) : null;
            case 96: return ControlValue.Relative(Math.Max(1, d2));    // increment
            case 97: return ControlValue.Relative(-Math.Max(1, d2));   // decrement
        }
        return null;
    }

    /// <summary>The CC numbers / notes a declared control arrives on, for claiming and routing:
    /// a 14-bit control also owns its LSB, an NRPN control the whole NRPN family.</summary>
    public static IEnumerable<(int status, int number)> Messages(ControlDecl decl) => decl.Message switch
    {
        ControlMessage.Cc => new[] { (0xB0, decl.Number) },
        ControlMessage.Cc14 => new[] { (0xB0, decl.Number), (0xB0, decl.Number + 32) },
        ControlMessage.Nrpn => new[] { (0xB0, 99), (0xB0, 98), (0xB0, 6), (0xB0, 38), (0xB0, 96), (0xB0, 97) },
        ControlMessage.Note => new[] { (0x80, decl.Number) },
        ControlMessage.PitchBend => new[] { (0xE0, 0) },
        _ => Array.Empty<(int, int)>(),
    };
}
