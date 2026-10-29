// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Engine views that let an editor written for a track's instrument drive an instrument inside
// a rack chain instead. The editors call IAudioEngine as if the instrument were the track's
// own; these DispatchProxies redirect those calls to the chain, so one editor serves both
// (the Mosaic editor on a chain, the Drum Rack card on a Drum Rack nested in a chain,
// specs/041). They live here, not in the app, so the MCP server applies presets through them.

using System.Collections.Concurrent;
using System.Reflection;

namespace Nota.Application;

/// <summary>The instrument in chain <see cref="Chain"/> of the track's Instrument Rack, seen as
/// the track's instrument: its plugin-param calls (trackId, deviceIndex −1, i) go to the chain
/// surface (trackId, chain, i); live telemetry, actions and Nota Mosaic's program come from the
/// chain instrument; automation writes are no-ops (chain params follow macros); everything else
/// reaches the real engine.</summary>
public class RackChainEngineProxy : DispatchProxy
{
    public IAudioEngine Inner = null!;
    public int Chain;

    public static IAudioEngine Create(IAudioEngine inner, int chain)
    {
        var proxy = Create<IAudioEngine, RackChainEngineProxy>();
        var pp = (RackChainEngineProxy)(object)proxy; pp.Inner = inner; pp.Chain = chain;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? m, object?[]? args)
    {
        switch (m?.Name)
        {
            case nameof(IAudioEngine.PluginParamCount):     return Inner.RackChainInstrumentParamCount((int)args![0]!, Chain);
            case nameof(IAudioEngine.PluginParamId):        return Inner.RackChainInstrumentParamId((int)args![0]!, Chain, (int)args![2]!);
            case nameof(IAudioEngine.PluginParamGet):       return Inner.RackChainInstrumentParamGet((int)args![0]!, Chain, (int)args![2]!);
            case nameof(IAudioEngine.PluginParamSet):       Inner.RackChainInstrumentParamSet((int)args![0]!, Chain, (int)args![2]!, (float)args![3]!); return null;
            case nameof(IAudioEngine.InstrumentParamDefault): return Inner.RackChainInstrumentParamDefault((int)args![0]!, Chain, (int)args![1]!);
            case nameof(IAudioEngine.TrackInstrumentKind):  return Inner.RackChainInstrumentKind((int)args![0]!, Chain);
            case nameof(IAudioEngine.BeginAutomationWrite):
            case nameof(IAudioEngine.EndAutomationWrite):   return null;
            // Live telemetry comes from the chain instrument, not the track's rack instrument.
            case nameof(IAudioEngine.InstrumentScope):      return Inner.RackChainInstrumentScope((int)args![0]!, Chain, (float[])args![1]!);
            case nameof(IAudioEngine.InstrumentVoiceCount): return Inner.RackChainInstrumentVoiceCount((int)args![0]!, Chain);
            case nameof(IAudioEngine.InstrumentHeldNotes):  return Inner.RackChainInstrumentHeldNotes((int)args![0]!, Chain, (int[])args![1]!);
            case nameof(IAudioEngine.InstrumentAction):     Inner.RackChainInstrumentAction((int)args![0]!, Chain, (int)args![1]!, (int)args![2]!, (float)args![3]!); return null;
            // Nota Mosaic in a chain (specs/041). Through Inner's rack address −1, which a
            // NestedRackEngineProxy under it turns into its nested Drum Rack (a Mosaic pad).
            // A factory Nota Grain preset loads its source sample into the chain's Grain.
            case nameof(IAudioEngine.SetTrackGrainSample): return Inner.RackDevChainGrainSetSample((int)args![0]!, -1, Chain, (string)args![1]!, (int)args![2]!);
            case nameof(IAudioEngine.MosaicSetProgram):     return Inner.RackDevChainMosaicSetProgram((int)args![0]!, -1, Chain, (string)args![1]!, (bool)args![2]!);
            case nameof(IAudioEngine.MosaicProgram):        return Inner.RackDevChainMosaicProgram((int)args![0]!, -1, Chain);
            case nameof(IAudioEngine.MosaicZoneSampleId):   return Inner.RackDevChainMosaicZoneSampleId((int)args![0]!, -1, Chain, (int)args![1]!);
            case nameof(IAudioEngine.TryGetMosaicStatus):
            {
                bool ok = Inner.RackDevTryGetChainMosaicStatus((int)args![0]!, -1, Chain, out var st);
                args[1] = st;
                return ok;
            }
            default: return m!.Invoke(Inner, args);
        }
    }
}

/// <summary>The Drum Rack in chain <see cref="RackChainEngineProxy.Chain"/> of the track's
/// Instrument Rack, seen as the track's Drum Rack, so the Drum Rack card runs on it unchanged.
/// Its macros are that chain instrument's params (the base proxy); every instrument-rack call
/// <c>RackFoo(trackId, …)</c> becomes <c>RackDevFoo(trackId, nested, …)</c>, and a
/// <c>RackDevFoo(trackId, −1, …)</c> (the track's rack) the same. A Rack* call with no RackDev
/// twin is an Instrument Rack feature a Drum Rack doesn't have (mono, glide, chain MIDI
/// effects): it answers the type's default and never reaches the outer rack.</summary>
public class NestedRackEngineProxy : RackChainEngineProxy
{
    /// <summary>The nested rack's address for RackDev* calls.</summary>
    public int Nested => RackMacroMapping.NestedRackBase - Chain;

    public static new IAudioEngine Create(IAudioEngine inner, int chain)
    {
        var proxy = Create<IAudioEngine, NestedRackEngineProxy>();
        var pp = (NestedRackEngineProxy)(object)proxy; pp.Inner = inner; pp.Chain = chain;
        return proxy;
    }

    private static readonly ConcurrentDictionary<MethodInfo, MethodInfo?> Twins = new();

    // RackFoo(int trackId, rest…) → RackDevFoo(int trackId, int deviceIndex, rest…).
    private static MethodInfo? TwinOf(MethodInfo m) => Twins.GetOrAdd(m, k =>
    {
        var ps = k.GetParameters();
        var want = new[] { typeof(int), typeof(int) }.Concat(ps.Skip(1).Select(p => p.ParameterType)).ToArray();
        return typeof(IAudioEngine).GetMethod("RackDev" + k.Name[4..], want);
    });

    protected override object? Invoke(MethodInfo? m, object?[]? args)
    {
        string name = m?.Name ?? "";
        if (name.StartsWith("RackDev", StringComparison.Ordinal))
        {
            if (args is { Length: >= 2 } && args[1] is -1) args[1] = Nested;
            return m!.Invoke(Inner, args);
        }
        if (name.StartsWith("Rack", StringComparison.Ordinal) && args is { Length: >= 1 } && args[0] is int)
        {
            var twin = TwinOf(m!);
            if (twin == null) return Default(m!, args);
            var a = new object?[args.Length + 1];
            a[0] = args[0]; a[1] = Nested;
            Array.Copy(args, 1, a, 2, args.Length - 1);
            var r = twin.Invoke(Inner, a);
            for (int i = 1; i < args.Length; i++) args[i] = a[i + 1];   // out parameters
            return r;
        }
        return base.Invoke(m, args);
    }

    private static object? Default(MethodInfo m, object?[] args)
    {
        var ps = m.GetParameters();
        for (int i = 0; i < ps.Length; i++)
            if (ps[i].IsOut) args[i] = ps[i].ParameterType.GetElementType() is { IsValueType: true } et ? Activator.CreateInstance(et) : null;
        var rt = m.ReturnType;
        if (rt == typeof(void)) return null;
        if (rt == typeof(string)) return "";
        if (rt == typeof(int)) return m.Name.EndsWith("Count", StringComparison.Ordinal) ? 0 : -1;
        return rt.IsValueType ? Activator.CreateInstance(rt) : null;
    }
}
