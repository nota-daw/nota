// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// HUI (specs/051 US6, FR-026, research R12) on the same surface model as Mackie Control: only
// the bytes differ. Facts from the publicly documented HUI implementation tables:
//
//   buttons   a zone select CC 0x0F <zone>, then CC 0x2F <port> (+0x40 = pressed); lights the same
//             way host → surface with CC 0x0C / 0x2C. Strips are zones 0–7 (port 0 fader touch,
//             1 select, 2 mute, 3 solo, 7 rec arm); zone 0x0A bank / channel (0 channel ←,
//             1 bank ←, 2 channel →, 3 bank →); zone 0x0E transport (1 rewind, 2 forward, 3 stop,
//             4 play, 5 record); zone 0x0F (3 loop)
//   faders    CC 0x00–0x07 high, then 0x20–0x27 low → 14 bits (both directions)
//   V-pots    turned: CC 0x40–0x47, 0x40 + n clockwise, n counter-clockwise; rings: CC 0x10–0x17
//   jog       CC 0x0D, the same relative encoding
//   names     F0 00 00 66 05 00 10 <strip> <4 characters> F7
//   meters    polyphonic pressure A0 <strip> <side << 4 | level 0..12>
//   ping      host sends note-on 0 / 0 about once a second; the surface answers note-on 0 / 7F

using Nota.Application.Controllers.Mcu;

namespace Nota.Application.Controllers.Hui;

public sealed class HuiCodec : ISurfaceCodec
{
    private int _zone = -1;
    private readonly int[] _faderHigh = Enumerable.Repeat(-1, 8).ToArray();

    public int NameWidth => 4;

    /// <summary>The host's ping (note-on 0 velocity 0).</summary>
    public static byte[] Ping => new byte[] { 0x90, 0x00, 0x00 };

    /// <summary>A relative HUI encoder value: 0x40 + n clockwise, n counter-clockwise.</summary>
    public static int Relative(int v) => v > 0x40 ? v - 0x40 : -v;

    public SurfaceInput? Decode(int status, int channel, int d1, int d2)
    {
        if (status == 0x90 && d1 == 0 && d2 == 0x7F) return new SurfaceInput(SurfaceInputKind.Alive);
        if (status != 0xB0) return null;
        switch (d1)
        {
            case 0x0F: _zone = d2; return null;
            case 0x2F when _zone >= 0:
            {
                int port = d2 & 0x0F;
                bool down = (d2 & 0x40) != 0;
                if (_zone is >= 0 and <= 7)
                    return port switch
                    {
                        0 => new SurfaceInput(SurfaceInputKind.Touch, _zone, 0, down),
                        1 => new SurfaceInput(SurfaceInputKind.Select, _zone, 0, down),
                        2 => new SurfaceInput(SurfaceInputKind.Mute, _zone, 0, down),
                        3 => new SurfaceInput(SurfaceInputKind.Solo, _zone, 0, down),
                        7 => new SurfaceInput(SurfaceInputKind.Rec, _zone, 0, down),
                        _ => null,
                    };
                return (_zone, port) switch
                {
                    (0x0A, 0) => new SurfaceInput(SurfaceInputKind.ChannelLeft, -1, 0, down),
                    (0x0A, 1) => new SurfaceInput(SurfaceInputKind.BankLeft, -1, 0, down),
                    (0x0A, 2) => new SurfaceInput(SurfaceInputKind.ChannelRight, -1, 0, down),
                    (0x0A, 3) => new SurfaceInput(SurfaceInputKind.BankRight, -1, 0, down),
                    (0x0E, 1) => new SurfaceInput(SurfaceInputKind.Rewind, -1, 0, down),
                    (0x0E, 2) => new SurfaceInput(SurfaceInputKind.Forward, -1, 0, down),
                    (0x0E, 3) => new SurfaceInput(SurfaceInputKind.Stop, -1, 0, down),
                    (0x0E, 4) => new SurfaceInput(SurfaceInputKind.Play, -1, 0, down),
                    (0x0E, 5) => new SurfaceInput(SurfaceInputKind.Record, -1, 0, down),
                    (0x0F, 3) => new SurfaceInput(SurfaceInputKind.Loop, -1, 0, down),
                    _ => null,
                };
            }
            case >= 0x00 and <= 0x07:
                _faderHigh[d1] = d2;
                return null;
            case >= 0x20 and <= 0x27 when _faderHigh[d1 - 0x20] >= 0:
                return new SurfaceInput(SurfaceInputKind.Fader, d1 - 0x20, (_faderHigh[d1 - 0x20] << 7) | (d2 & 0x7F));
            case >= 0x40 and <= 0x47:
                return new SurfaceInput(SurfaceInputKind.VPot, d1 - 0x40, Relative(d2));
            case 0x0D:
                return new SurfaceInput(SurfaceInputKind.Jog, -1, Relative(d2));
        }
        return null;
    }

    public IEnumerable<byte[]> Fader(int strip, int value14)
    {
        if (strip is < 0 or > 7) yield break;   // HUI has no master fader
        int v = Math.Clamp(value14, 0, 16383);
        yield return new byte[] { 0xB0, (byte)strip, (byte)(v >> 7) };
        yield return new byte[] { 0xB0, (byte)(0x20 + strip), (byte)(v & 0x7F) };
    }

    public IEnumerable<byte[]> Ring(int strip, int position)
    {
        yield return new byte[] { 0xB0, (byte)(0x10 + strip), (byte)Math.Clamp(position, 0, 11) };
    }

    public IEnumerable<byte[]> StripLed(StripLight light, int strip, bool on)
        => Light(strip, light switch { StripLight.Select => 1, StripLight.Mute => 2, StripLight.Solo => 3, _ => 7 }, on);

    public IEnumerable<byte[]> TransportLed(TransportLight light, bool on) => light switch
    {
        TransportLight.Stop => Light(0x0E, 3, on),
        TransportLight.Play => Light(0x0E, 4, on),
        TransportLight.Record => Light(0x0E, 5, on),
        _ => Light(0x0F, 3, on),
    };

    private static IEnumerable<byte[]> Light(int zone, int port, bool on)
    {
        yield return new byte[] { 0xB0, 0x0C, (byte)zone };
        yield return new byte[] { 0xB0, 0x2C, (byte)((on ? 0x40 : 0x00) | port) };
    }

    public IEnumerable<byte[]> Name(int strip, string name)
    {
        var b = new List<byte> { 0xF0, 0x00, 0x00, 0x66, 0x05, 0x00, 0x10, (byte)Math.Clamp(strip, 0, 7) };
        foreach (char c in McuCodec.Fit(name, 4)) b.Add((byte)c);
        b.Add(0xF7);
        yield return b.ToArray();
    }

    public IEnumerable<byte[]> Assignment(string text) { yield break; }   // no wide scribble line on HUI
    public IEnumerable<byte[]> Time(string text, string? was) { yield break; }

    public IEnumerable<byte[]> Meter(int strip, int level)
    {
        int l = Math.Clamp(level, 0, 12);
        yield return new byte[] { 0xA0, (byte)Math.Clamp(strip, 0, 7), (byte)l };
        yield return new byte[] { 0xA0, (byte)Math.Clamp(strip, 0, 7), (byte)(0x10 | l) };
    }

    public IEnumerable<byte[]> Blank()
    {
        for (int s = 0; s < 8; s++)
        {
            foreach (var m in Fader(s, 0)) yield return m;
            foreach (var m in Ring(s, 0)) yield return m;
            foreach (var m in Name(s, "")) yield return m;
            foreach (StripLight l in Enum.GetValues<StripLight>()) foreach (var m in StripLed(l, s, false)) yield return m;
        }
        foreach (TransportLight l in Enum.GetValues<TransportLight>()) foreach (var m in TransportLed(l, false)) yield return m;
    }
}
