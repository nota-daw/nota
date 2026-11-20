// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Catching one control from what a controller sends (specs/051 FR-030, specs/110 FR-006): collect
// the messages of one deliberate movement, wait until it is done — a button pressed and let go, a
// knob or encoder still for a moment — and classify it (note / CC / 14-bit / NRPN / pitch bend, an
// encoder's relative encoding). For a pair it also tells an encoder (both halves at once) from a
// button (one half). Shared by the setup wizard and the mapping editor, so both learn alike; also
// the readable names of what arrives and what was learned.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Nota.Application.Controllers;
using Nota.Application.Localization;

namespace Nota.App.Controllers;

internal sealed class ControlCatcher
{
    public const long KnobStillMs = 600, StillMs = 350;

    private readonly List<(int Status, int Channel, int D1, int D2)> _events = new();
    private long _lastEventMs;

    public bool Empty => _events.Count == 0;
    public IReadOnlyList<(int Status, int Channel, int D1, int D2)> Events => _events;

    public void Reset() => _events.Clear();

    public void Add((int Status, int Channel, int D1, int D2) e, long nowMs)
    {
        _events.Add(e);
        _lastEventMs = nowMs;
    }

    /// <summary>The control, once the movement is done (null while it goes on or when nothing
    /// usable came). <paramref name="button"/>: a press-and-release is enough; <paramref name="slow"/>:
    /// a knob / encoder / jog / pair, which waits longer for stillness.</summary>
    public ClassifiedControl? Read(long nowMs, bool button, bool slow)
    {
        if (_events.Count == 0) return null;
        bool pressed = button && PressedAndReleased(_events);
        if (!pressed && nowMs - _lastEventMs < (slow ? KnobStillMs : StillMs)) return null;
        var c = ControlClassifier.Classify(_events);
        if (c is null) _events.Clear();
        return c;
    }

    /// <summary>A pair's control: an encoder (or knob) turns — relative steps, or an absolute run
    /// of values; a button sends a note, or a CC that only goes up and back to 0.</summary>
    public bool LooksLikeEncoder(ClassifiedControl c) => LooksLikeEncoder(c, _events);

    public static bool LooksLikeEncoder(ClassifiedControl c, IReadOnlyList<(int Status, int Channel, int D1, int D2)> ev)
    {
        if (c.Message == ControlMessage.Note) return false;
        if (c.Encoder != EncoderMode.Absolute || c.Ambiguous) return true;
        if (c.Message != ControlMessage.Cc) return true;
        var values = ev.Where(e => e.Status == 0xB0 && e.D1 == c.Number).Select(e => e.D2).Distinct().ToList();
        bool button = values.Count <= 2 && values.Contains(0);
        return !button && values.Count >= 3;
    }

    /// <summary>A button's press and release: a note-on then its note-off, or a CC up then down.</summary>
    public static bool PressedAndReleased(IReadOnlyList<(int Status, int Channel, int D1, int D2)> ev)
    {
        bool down = false;
        foreach (var e in ev)
        {
            if (e.Status == 0x90 && e.D2 > 0) down = true;
            else if ((e.Status == 0x80 || (e.Status == 0x90 && e.D2 == 0)) && down) return true;
            else if (e.Status == 0xB0 && e.D2 >= 64) down = true;
            else if (e.Status == 0xB0 && e.D2 < 64 && down) return true;
        }
        return false;
    }

    /// <summary>What just arrived, as the readout shows it.</summary>
    public static string Raw((int Status, int Channel, int D1, int D2) e) => e.Status switch
    {
        0x90 or 0x80 => Loc.F("controllers.setup.raw_note", e.D1, e.Channel + 1, e.D2),
        0xE0 => Loc.F("controllers.setup.raw_bend", e.Channel + 1),
        0xB0 => Loc.F("controllers.setup.raw_cc", e.D1, e.Channel + 1, e.D2),
        _ => Loc.F("controllers.setup.raw_other", e.Status.ToString("X2", CultureInfo.CurrentCulture), e.Channel + 1),
    };

    /// <summary>"CC 21 on channel 1 · Absolute".</summary>
    public static string Describe(ClassifiedControl c) => Loc.F("controllers.setup.found", Short(c), c.Channel + 1, EncoderName(c.Encoder));

    /// <summary>"CC 21", "Note 36", "14-bit CC 7".</summary>
    public static string Short(ClassifiedControl c) => c.Message switch
    {
        ControlMessage.Note => Loc.F("controllers.setup.is_note", c.Number),
        ControlMessage.PitchBend => Loc.T("controllers.setup.is_bend"),
        ControlMessage.Nrpn => Loc.F("controllers.setup.is_nrpn", c.Number),
        ControlMessage.Cc14 => Loc.F("controllers.setup.is_cc14", c.Number),
        _ => Loc.F("controllers.setup.is_cc", c.Number),
    };

    /// <summary>A declared control as an editor chip: "CC 21 · ch 1", "+ relative" for an encoder.</summary>
    public static string Chip(ControlDecl d)
    {
        var c = new ClassifiedControl(d.Message, d.Channel, d.Number, d.Encoder);
        return d.Encoder == EncoderMode.Absolute
            ? Loc.F("controllers.edit.chip", Short(c), d.Channel + 1)
            : Loc.F("controllers.edit.chip_relative", Short(c), d.Channel + 1);
    }

    public static string EncoderName(EncoderMode e) => e switch
    {
        EncoderMode.RelativeTwos => Loc.T("controllers.setup.enc_twos"),
        EncoderMode.RelativeSignBit => Loc.T("controllers.setup.enc_signbit"),
        EncoderMode.RelativeOffset => Loc.T("controllers.setup.enc_offset"),
        _ => Loc.T("controllers.setup.enc_absolute"),
    };
}
