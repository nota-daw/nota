// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The Mackie Control (Logic Control) protocol subset Nota speaks (specs/051, FR-022, research
// R11), as a pure codec: bytes in → a protocol-neutral SurfaceInput, surface state → bytes out.
// Facts from the publicly documented MCU implementation tables (no code or mapping data copied):
//
//   faders        pitch bend, channel 1–8 = strips, 9 = master, 14-bit; touch = notes 104–111 (112 master)
//   V-pots        CC 16–23, relative sign-bit; push = notes 32–39; rings = CC 48–55
//   strip buttons Rec 0–7, Solo 8–15, Mute 16–23, Select 24–31 (LED: same note, 7F / 00)
//   bank/channel  notes 46 / 47 (bank ← →), 48 / 49 (channel ← →)
//   transport     91 rewind, 92 forward, 93 stop, 94 play, 95 record, 86 cycle
//   jog           CC 60, relative sign-bit
//   scribble      F0 00 00 66 <model> 12 <offset> <ASCII…> F7 (0–55 top line, 56–111 bottom, 7 per strip)
//   time display  CC 64–73, right to left
//   meters        channel pressure (strip << 4) | level 0..12
//   device query  F0 00 00 66 <model> 00 F7 — model 14 main unit, 15 extender

namespace Nota.Application.Controllers.Mcu;

public enum SurfaceInputKind
{
    Fader, Touch, VPot, VPotPush, Rec, Solo, Mute, Select,
    BankLeft, BankRight, ChannelLeft, ChannelRight,
    Play, Stop, Record, Loop, Rewind, Forward, Jog, MasterFader, MasterTouch,
    Alive,   // a HUI ping reply
}

/// <summary>A surface event. Fader: <see cref="Value"/> 14-bit; VPot / Jog: signed steps;
/// buttons and touch: <see cref="Pressed"/>.</summary>
public readonly record struct SurfaceInput(SurfaceInputKind Kind, int Strip = -1, int Value = 0, bool Pressed = false);

public enum StripLight { Rec, Solo, Mute, Select }
public enum TransportLight { Play, Stop, Record, Loop }

/// <summary>A control-surface protocol: decode input, encode feedback (MCU, HUI).</summary>
public interface ISurfaceCodec
{
    /// <summary>Characters of a strip name on the scribble strip.</summary>
    int NameWidth { get; }
    SurfaceInput? Decode(int status, int channel, int d1, int d2);
    IEnumerable<byte[]> Fader(int strip, int value14);           // strip 8 = master
    IEnumerable<byte[]> Ring(int strip, int position);
    IEnumerable<byte[]> StripLed(StripLight light, int strip, bool on);
    IEnumerable<byte[]> TransportLed(TransportLight light, bool on);
    IEnumerable<byte[]> Name(int strip, string name);
    IEnumerable<byte[]> Assignment(string text);
    IEnumerable<byte[]> Time(string text, string? was);
    IEnumerable<byte[]> Meter(int strip, int level);
    /// <summary>Everything dark: blank strips, faders down, lights off (on release, FR-004).</summary>
    IEnumerable<byte[]> Blank();
}

public sealed class McuCodec(bool extender = false) : ISurfaceCodec
{
    public const byte ModelMain = 0x14, ModelExtender = 0x15;
    private readonly byte _model = extender ? ModelExtender : ModelMain;

    public int NameWidth => 6;   // six characters and a space between strips

    public byte[] DeviceQuery => new byte[] { 0xF0, 0x00, 0x00, 0x66, _model, 0x00, 0xF7 };

    /// <summary>A relative sign-bit value: bit 6 set = counter-clockwise, the low six bits the count.</summary>
    public static int SignBit(int v) => (v & 0x40) != 0 ? -(v & 0x3F) : v & 0x3F;

    public SurfaceInput? Decode(int status, int channel, int d1, int d2)
    {
        switch (status)
        {
            case 0xE0:
                int v = (d1 & 0x7F) | ((d2 & 0x7F) << 7);
                return channel < 8 ? new SurfaceInput(SurfaceInputKind.Fader, channel, v)
                     : channel == 8 ? new SurfaceInput(SurfaceInputKind.MasterFader, 8, v) : null;
            case 0xB0:
                if (d1 is >= 16 and <= 23) return new SurfaceInput(SurfaceInputKind.VPot, d1 - 16, SignBit(d2));
                if (d1 == 60) return new SurfaceInput(SurfaceInputKind.Jog, -1, SignBit(d2));
                return null;
            case 0x90 or 0x80:
                bool down = status == 0x90 && d2 > 0;
                return d1 switch
                {
                    >= 0 and <= 7 => new SurfaceInput(SurfaceInputKind.Rec, d1, 0, down),
                    >= 8 and <= 15 => new SurfaceInput(SurfaceInputKind.Solo, d1 - 8, 0, down),
                    >= 16 and <= 23 => new SurfaceInput(SurfaceInputKind.Mute, d1 - 16, 0, down),
                    >= 24 and <= 31 => new SurfaceInput(SurfaceInputKind.Select, d1 - 24, 0, down),
                    >= 32 and <= 39 => new SurfaceInput(SurfaceInputKind.VPotPush, d1 - 32, 0, down),
                    46 => new SurfaceInput(SurfaceInputKind.BankLeft, -1, 0, down),
                    47 => new SurfaceInput(SurfaceInputKind.BankRight, -1, 0, down),
                    48 => new SurfaceInput(SurfaceInputKind.ChannelLeft, -1, 0, down),
                    49 => new SurfaceInput(SurfaceInputKind.ChannelRight, -1, 0, down),
                    86 => new SurfaceInput(SurfaceInputKind.Loop, -1, 0, down),
                    91 => new SurfaceInput(SurfaceInputKind.Rewind, -1, 0, down),
                    92 => new SurfaceInput(SurfaceInputKind.Forward, -1, 0, down),
                    93 => new SurfaceInput(SurfaceInputKind.Stop, -1, 0, down),
                    94 => new SurfaceInput(SurfaceInputKind.Play, -1, 0, down),
                    95 => new SurfaceInput(SurfaceInputKind.Record, -1, 0, down),
                    >= 104 and <= 111 => new SurfaceInput(SurfaceInputKind.Touch, d1 - 104, 0, down),
                    112 => new SurfaceInput(SurfaceInputKind.MasterTouch, 8, 0, down),
                    _ => null,
                };
        }
        return null;
    }

    public IEnumerable<byte[]> Fader(int strip, int value14)
    {
        int v = Math.Clamp(value14, 0, 16383);
        yield return new byte[] { (byte)(0xE0 | Math.Clamp(strip, 0, 8)), (byte)(v & 0x7F), (byte)(v >> 7) };
    }

    public IEnumerable<byte[]> Ring(int strip, int position)
    {
        yield return new byte[] { 0xB0, (byte)(0x30 + strip), (byte)Math.Clamp(position, 0, 11) };   // single-dot mode
    }

    public IEnumerable<byte[]> StripLed(StripLight light, int strip, bool on)
    {
        int note = light switch { StripLight.Rec => 0, StripLight.Solo => 8, StripLight.Mute => 16, _ => 24 } + strip;
        yield return Led(note, on);
    }

    public IEnumerable<byte[]> TransportLed(TransportLight light, bool on)
    {
        yield return Led(light switch { TransportLight.Play => 94, TransportLight.Stop => 93, TransportLight.Record => 95, _ => 86 }, on);
    }

    public static byte[] Led(int note, bool on) => new byte[] { 0x90, (byte)note, (byte)(on ? 0x7F : 0x00) };

    public IEnumerable<byte[]> Name(int strip, string name)
    {
        yield return Scribble(strip * 7, Fit(name, NameWidth) + " ");
    }

    public IEnumerable<byte[]> Assignment(string text)
    {
        yield return Scribble(56, Fit(text, 56));
    }

    /// <summary>Text on the scribble strip at <paramref name="offset"/> (0–111).</summary>
    public byte[] Scribble(int offset, string text)
    {
        var b = new List<byte> { 0xF0, 0x00, 0x00, 0x66, _model, 0x12, (byte)Math.Clamp(offset, 0, 111) };
        foreach (char ch in text) b.Add(ch is >= ' ' and <= '~' ? (byte)ch : (byte)'?');
        b.Add(0xF7);
        return b.ToArray();
    }

    public IEnumerable<byte[]> Time(string text, string? was)
    {
        text = text.Length > 10 ? text[^10..] : text.PadLeft(10);
        for (int i = 0; i < 10; i++)
        {
            char c = text[9 - i];   // CC 64 is the rightmost digit
            if (was is { Length: 10 } && was[9 - i] == c) continue;
            yield return new byte[] { 0xB0, (byte)(0x40 + i), SevenSegment(c) };
        }
    }

    // The display's character set: digits and space as ASCII in six bits, letters 1–26.
    private static byte SevenSegment(char c) => c switch
    {
        >= '0' and <= '9' => (byte)c,
        >= 'A' and <= 'Z' => (byte)(c - 'A' + 1),
        >= 'a' and <= 'z' => (byte)(c - 'a' + 1),
        '-' => 0x2D,
        _ => 0x20,
    };

    public IEnumerable<byte[]> Meter(int strip, int level)
    {
        yield return new byte[] { 0xD0, (byte)((Math.Clamp(strip, 0, 7) << 4) | Math.Clamp(level, 0, 12)) };
    }

    public IEnumerable<byte[]> Blank()
    {
        yield return Scribble(0, new string(' ', 112));
        for (int s = 0; s <= 8; s++) foreach (var m in Fader(s, 0)) yield return m;
        for (int s = 0; s < 8; s++)
        {
            foreach (var m in Ring(s, 0)) yield return m;
            foreach (StripLight l in Enum.GetValues<StripLight>()) foreach (var m in StripLed(l, s, false)) yield return m;
        }
        foreach (TransportLight l in Enum.GetValues<TransportLight>()) foreach (var m in TransportLed(l, false)) yield return m;
        foreach (var m in Time("", null)) yield return m;
    }

    /// <summary>ASCII, cut or padded to <paramref name="width"/>.</summary>
    public static string Fit(string s, int width)
    {
        var t = new string((s ?? "").Select(c => c is >= ' ' and <= '~' ? c : '?').ToArray());
        return t.Length > width ? t[..width] : t.PadRight(width);
    }
}
