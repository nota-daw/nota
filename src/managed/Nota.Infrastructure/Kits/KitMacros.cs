// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The factory kits' macros: the eight knobs a kit loads with, on the Drum Rack and on Nota
// Rhythm alike. A macro drives the pads it names by their role in the kit — a pad control (its
// Tune or Decay) or a param of the pad's own effects (KitFx), so one knob opens every snare and
// clap reverb at once.
//
//   1 Tune         every pad but the bass, ±12 st (±5 on the acoustic and hand-percussion kits).
//   2 Decay        the drums that ring out (kick, snare, clap, toms, percussion): full length at
//                  the top, a tight gate at the bottom — how far down depends on the kit.
//   3 Hats         the hats' decay, on its own knob so the groove's length stays separate.
//   4 Drive/Tape   the kick's parallel saturation (Forge drive and amount).
//   5 Room/Space   the snare and clap reverbs: level and decay.
//   6 Echo/Dub     the hat and percussion delays: level and feedback.
//   7 808/Sub      the tuned bass pads' pitch, on kits that have them.
//   8              left free for the user.
//
// A macro whose targets the kit doesn't have (no reverb on a baked-room snare, no bass pad)
// stays unassigned. Every range is built around the value the kit loads with, so a freshly
// loaded kit sounds exactly as its recipe — the macro sits at that value: Tune in the middle,
// Decay and Hats at the top (full length), the effect macros in the middle, with room to go
// both ways.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Nota.Infrastructure.Kits;

/// <summary>What a macro rule drives on a pad: a pad control, or a param of one of its effects.</summary>
public enum MacroParam { Tune, Decay, Fx }

/// <summary>One kind of target of a kit macro: the pads with one of <paramref name="Roles"/>, the
/// <paramref name="Param"/> (for Fx: <paramref name="FxParam"/> of the pad's device of kind
/// <paramref name="FxKind"/>). <paramref name="Range"/> turns the target's value at load, in its
/// own units (semitones for Tune, 0..1 for Decay, the device's units for Fx), into the mapping's
/// [lo, hi].</summary>
public sealed record MacroRule(PadRole[] Roles, MacroParam Param, Func<double, (double Lo, double Hi)> Range,
                               int FxKind = -1, string FxParam = "");

/// <summary>A kit macro: its slot (0..7), name, the value it loads at, and what it drives.</summary>
public sealed record KitMacro(int Slot, string Name, float Value, IReadOnlyList<MacroRule> Rules);

/// <summary>One mapping of a kit macro, resolved on an instrument: macro <paramref name="Macro"/>
/// drives <paramref name="Target"/> over [lo, hi].</summary>
public readonly record struct MacroPlanItem(int Macro, MacroTarget Target, float Lo, float Hi);

/// <summary>A concrete macro target on a kit instrument: pad/voice <paramref name="Slot"/>,
/// its <paramref name="Param"/>, and for Fx the device index and param index.</summary>
public readonly record struct MacroTarget(int Slot, MacroParam Param, int Device = -1, int FxParamIndex = -1);

public static class KitMacros
{
    public const int Count = 8;

    private static readonly PadRole[] Every = Enum.GetValues<PadRole>().Where(r => r != PadRole.Sub).ToArray();   // the bass stays in key: it has its own
    private static readonly PadRole[] Ringing = { PadRole.Kick, PadRole.Snare, PadRole.Clap, PadRole.Tom, PadRole.Perc };
    private static readonly PadRole[] Hats = { PadRole.ClosedHat, PadRole.OpenHat };
    private static readonly PadRole[] Kicks = { PadRole.Kick };
    private static readonly PadRole[] Spaced = { PadRole.Snare, PadRole.Clap };
    private static readonly PadRole[] Echoed = { PadRole.ClosedHat, PadRole.OpenHat, PadRole.Perc };
    private static readonly PadRole[] Subs = { PadRole.Sub };

    /// <summary>The macros <paramref name="kit"/> loads with, by slot.</summary>
    public static IReadOnlyList<KitMacro> For(KitDefinition kit)
    {
        // The kit's character: how far the pitch goes, how short the gate gets, and what the
        // effect knobs are called.
        bool acoustic = kit.Id is "atelier" or "terra" or "velvet" or "brass-room" or "pit" or "lagoon";
        double semis = acoustic ? 5 : 12;
        double floor = kit.Id switch
        {
            "aether" or "titan" or "volta" or "yard" => 0.15,   // long tails: the knob can go a long way
            "pixel" or "byte" or "micron" => 0.35,              // already short: keep a hit a hit
            _ => 0.25,
        };
        bool bigSpace = kit.FxSpace >= 1.3;
        bool eightOhEight = kit.Pads.Any(p => KitFx.RoleOf(p) == PadRole.Sub && p.Name.StartsWith("808", StringComparison.Ordinal));

        return new List<KitMacro>
        {
            new(0, "Tune", 0.5f, new[] { new MacroRule(Every, MacroParam.Tune, cur => (cur - semis, cur + semis)) }),
            new(1, "Decay", 1f, new[] { new MacroRule(Ringing, MacroParam.Decay, cur => (Math.Min(floor, cur), cur)) }),
            new(2, "Hats", 1f, new[] { new MacroRule(Hats, MacroParam.Decay, cur => (Math.Min(0.15, cur), cur)) }),
            new(3, kit.FxTape ? "Tape" : "Drive", 0.5f, new[]
            {
                Fx(Kicks, KitFx.Forge, "S1 Drive", cur => AroundFrom(0, cur)),
                Fx(Kicks, KitFx.Forge, "Amount", cur => AroundFrom(0, cur)),
            }),
            new(4, bigSpace ? "Space" : "Room", 0.5f, new[]
            {
                Fx(Spaced, KitFx.Reverb, "Dry/Wet", cur => AroundFrom(0, cur)),
                Fx(Spaced, KitFx.Reverb, "Dry Level", cur => AroundFrom(KitFx.UnityDry(0), cur)),   // the dry stays near unity
                Fx(Spaced, KitFx.Reverb, "Decay", cur => AroundFrom(Math.Max(0, cur - 0.2), cur)),
            }),
            new(5, kit.Id == "yard" ? "Dub" : "Echo", 0.5f, new[]
            {
                Fx(Echoed, KitFx.Delay, "Dry/Wet", cur => AroundFrom(0, cur)),
                Fx(Echoed, KitFx.Delay, "Dry Level", cur => AroundFrom(KitFx.UnityDry(0), cur)),
                Fx(Echoed, KitFx.Delay, "Feedback", cur => AroundFrom(0, cur)),
            }),
            new(6, eightOhEight ? "808" : "Sub", 0.5f, new[] { new MacroRule(Subs, MacroParam.Tune, cur => (cur - 12, cur + 12)) }),
        };
    }

    private static MacroRule Fx(PadRole[] roles, int kind, string param, Func<double, (double, double)> range)
        => new(roles, MacroParam.Fx, range, kind, param);

    // A range from `lo` whose midpoint is `cur`: the macro loads at 0.5 on the kit's own value
    // and goes as far past it as it goes below.
    private static (double, double) AroundFrom(double lo, double cur) => (lo, lo + (cur - lo) * 2);

    /// <summary>Resolves the kit's macros on an instrument. <paramref name="pads"/> lists the
    /// instrument's slots (pad chains or Rhythm voices) with the kit pad each holds;
    /// <paramref name="current"/> reads a target's value at load (in the rule's units, see
    /// <see cref="MacroRule"/>), <paramref name="findFx"/> finds a slot's device of a kind and a
    /// param of it by name ((-1, -1) = none). Macros that end up with no target are dropped.</summary>
    public static (IReadOnlyList<KitMacro> Macros, IReadOnlyList<MacroPlanItem> Plan) Resolve(
        KitDefinition kit, IReadOnlyList<(int Slot, KitPad Pad)> pads,
        Func<MacroTarget, double> current, Func<int, int, string, (int Device, int Param)> findFx)
    {
        var used = new List<KitMacro>();
        var plan = new List<MacroPlanItem>();
        foreach (var macro in For(kit))
        {
            int before = plan.Count;
            foreach (var rule in macro.Rules)
                foreach (var (slot, pad) in pads)
                {
                    if (Array.IndexOf(rule.Roles, KitFx.RoleOf(pad)) < 0) continue;
                    var target = new MacroTarget(slot, rule.Param);
                    if (rule.Param == MacroParam.Fx)
                    {
                        var (dev, p) = findFx(slot, rule.FxKind, rule.FxParam);
                        if (dev < 0 || p < 0) continue;
                        target = target with { Device = dev, FxParamIndex = p };
                    }
                    var (lo, hi) = rule.Range(current(target));
                    plan.Add(new MacroPlanItem(macro.Slot, target, (float)lo, (float)hi));
                }
            if (plan.Count > before) used.Add(macro);
        }
        return (used, plan);
    }
}
