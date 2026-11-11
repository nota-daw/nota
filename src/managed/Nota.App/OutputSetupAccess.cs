// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The app's side of the output setup (specs/089): the playback device's entry in the
// settings, stored back with a save and pushed to the engine — Main and Cue pairs, and where
// the click goes. Pushed at start, after every audio restart (the device may be another one
// now, with its own setup) and after every edit. Outside the running app there is no setup:
// Main on 1/2, no Cue, as before this feature.

using Nota.Application;

namespace Nota.App;

internal static class OutputSetupAccess
{
    private static ISettingsService? Settings
        => App.Services?.GetService(typeof(ISettingsService)) as ISettingsService;

    private static string? _key;

    public static string KeyOf(IAudioEngine engine)
        => _key ??= OutputSetups.KeyOf(engine.AudioSystem, engine.EffectiveOutputUid(), engine.AsioDriver);

    /// <summary>The setup of the device the engine plays on now (defaults when never set up;
    /// under ASIO Main starts on the output pair chosen before, FR-005).</summary>
    public static OutputSetup Current(IAudioEngine engine)
    {
        if (Settings is { } s && s.Current.OutputSetups.TryGetValue(KeyOf(engine), out var v)) return v;
        return OutputSetups.Default(engine.AudioSystem == AudioSystem.Asio ? engine.AsioPair(false) : 0);
    }

    public static void Store(IAudioEngine engine, OutputSetup setup)
    {
        if (Settings is { } s)
        {
            s.Current.OutputSetups[KeyOf(engine)] = setup;
            s.Save();
        }
        Send(engine, setup);
        OutputSetups.NotifyChanged();
    }

    /// <summary>Find the device's key again and tell the engine its setup.</summary>
    public static void Push(IAudioEngine engine)
    {
        _key = null;
        Send(engine, Current(engine));
        OutputSetups.NotifyChanged();
    }

    private static void Send(IAudioEngine engine, OutputSetup s)
    {
        int cueFirst = s.CueFirst is int c ? c - 1 : -1;
        if (!engine.SetOutputRouting(s.MainFirst - 1, s.MainWidth, cueFirst, s.CueWidth))
            engine.SetOutputRouting(0, 2, -1, 2); // a hand-edited settings file: back to the default
        engine.ClickDestination = s.HasCue ? s.Click : ClickDestination.Main;
        engine.SetClickVolume(OutputSetups.ClickGain(s));
    }
}
