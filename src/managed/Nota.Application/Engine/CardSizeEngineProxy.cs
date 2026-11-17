// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// An engine view for a device card hosted in the Modular view (specs/101, #656). A Modular node
// has its own S / L size, independent of the same device's card in Detail (FR-014), but every
// card body reads its size from the device's card-size param (an effect's "View", an
// instrument's "view", a MIDI effect's "View") — dozens of call sites. Rather than teach each
// body about Modular, the node hands its body this proxy: reads of that one param answer the
// node's size and writes (the body's own S / L toggle) go to the node, never to the engine, so
// Detail and the undo history are untouched. Every other call reaches the real engine. The same
// pattern lets rack chains reuse instrument editors (RackEngineProxies).

using System.Reflection;

namespace Nota.Application;

/// <summary>Which card-size param a <see cref="CardSizeEngineProxy"/> stands in for.</summary>
public enum CardFamily { Effect, Instrument, MidiEffect }

public class CardSizeEngineProxy : DispatchProxy
{
    public IAudioEngine Inner = null!;
    public int TrackId;
    public CardFamily Family;
    public int Index;            // device / MIDI-effect index (−1 for the instrument)
    public int SizeParam = -1;   // the card-size param's index on that device, −1 when it has none
    public Func<bool> GetMini = () => false;
    public Action<bool> SetMini = _ => { };

    /// <summary>The engine as the card body of (<paramref name="trackId"/>, <paramref name="family"/>,
    /// <paramref name="index"/>) should see it: its card size is <paramref name="getMini"/> /
    /// <paramref name="setMini"/>; everything else is <paramref name="inner"/>.</summary>
    public static IAudioEngine Create(IAudioEngine inner, int trackId, CardFamily family, int index, Func<bool> getMini, Action<bool> setMini)
    {
        var proxy = Create<IAudioEngine, CardSizeEngineProxy>();
        var p = (CardSizeEngineProxy)(object)proxy;
        p.Inner = inner; p.TrackId = trackId; p.Family = family; p.Index = index;
        p.GetMini = getMini; p.SetMini = setMini;
        p.SizeParam = SizeParamOf(inner, trackId, family, index);
        return proxy;
    }

    /// <summary>The card-size param of a device, or −1 when its card has one size.</summary>
    public static int SizeParamOf(IAudioEngine engine, int trackId, CardFamily family, int index)
    {
        switch (family)
        {
            case CardFamily.Instrument: return InstrumentView.Index(engine, trackId);
            case CardFamily.Effect: return DeviceView.Index(engine, trackId, index);
            default:
                int n = engine.MidiEffectParamCount(trackId, index);
                for (int i = n - 1; i >= 0; i--)
                    if (engine.MidiEffectParamName(trackId, index, i) == DeviceView.ParamName) return i;
                return -1;
        }
    }

    // True when (track, device, param) of a get/set call is the wrapped card-size param.
    private bool IsSizeParam(object?[] a)
        => SizeParam >= 0 && (int)a[0]! == TrackId && (int)a[1]! == Index && (int)a[2]! == SizeParam;

    protected override object? Invoke(MethodInfo? m, object?[]? args)
    {
        switch (m?.Name)
        {
            case nameof(IAudioEngine.DeviceGetParam) when Family == CardFamily.Effect && IsSizeParam(args!):
            case nameof(IAudioEngine.PluginParamGet) when Family == CardFamily.Instrument && IsSizeParam(args!):
            case nameof(IAudioEngine.MidiEffectGetParam) when Family == CardFamily.MidiEffect && IsSizeParam(args!):
                return GetMini() ? 1f : 0f;
            case nameof(IAudioEngine.DeviceSetParam) when Family == CardFamily.Effect && IsSizeParam(args!):
            case nameof(IAudioEngine.PluginParamSet) when Family == CardFamily.Instrument && IsSizeParam(args!):
            case nameof(IAudioEngine.MidiEffectSetParam) when Family == CardFamily.MidiEffect && IsSizeParam(args!):
                // 1 = S; 0 = L; an effect's XL (2) has no place in Modular and reads as L.
                SetMini(Math.Abs((float)args![3]! - 1f) < 0.25f);
                return null;
            default:
                try { return m!.Invoke(Inner, args); }
                catch (TargetInvocationException e) when (e.InnerException is not null) { throw e.InnerException; }
        }
    }
}
