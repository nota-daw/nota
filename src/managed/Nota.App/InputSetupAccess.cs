// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The app's side of the Audio I/O setup (specs/086): the current capture device's entry in
// the settings, stored back with a save, the engine told which inputs are switched off (a take
// skips a track on one), and every picker told to refresh. Outside the running app (smoke
// tests, headless harnesses) there are no services and no setup: every input is on.

using Microsoft.Extensions.DependencyInjection;
using Nota.Application;

namespace Nota.App;

internal static class InputSetupAccess
{
    private static ISettingsService? Settings
        => App.Services?.GetService(typeof(ISettingsService)) as ISettingsService;

    // The key of the device the engine captures from, looked up once per device change (Push)
    // rather than on every label: the device the config opens, not the uid it names — under
    // "System default" (uid "") or with the chosen device unplugged that is another device.
    private static string? _key;

    public static string KeyOf(IAudioEngine engine)
        => _key ??= InputSetups.KeyOf(engine.AudioSystem, engine.EffectiveInputUid(), engine.AsioDriver);

    /// <summary>The setup of the device the engine captures from now; null = all defaults.</summary>
    public static InputSetup? Current(IAudioEngine engine)
        => Settings is { } s && s.Current.InputSetups.TryGetValue(KeyOf(engine), out var v) ? v : null;

    /// <summary>The device may have changed (start, Settings → Audio, a device plugged in or
    /// out): find its key again, and hand a setup saved before 0.60's fix under "System
    /// default" (key "device:") to the device that was the default then — the one opened now.</summary>
    public static void Refresh(IAudioEngine engine)
    {
        _key = null;
        string key = KeyOf(engine);
        if (Settings is not { } s || key == InputSetups.KeyOf(engine.AudioSystem, "", engine.AsioDriver)) return;
        string legacy = InputSetups.KeyOf(engine.AudioSystem, "", engine.AsioDriver);
        if (s.Current.InputSetups.Remove(legacy, out var old) && !s.Current.InputSetups.ContainsKey(key))
        {
            s.Current.InputSetups[key] = old;
            s.Save();
        }
    }

    public static void Store(IAudioEngine engine, InputSetup? setup)
    {
        if (Settings is not { } s) return;
        string key = KeyOf(engine);
        if (setup is null) s.Current.InputSetups.Remove(key);
        else s.Current.InputSetups[key] = setup;
        s.Save();
        engine.SetDisabledInputs(InputSetups.OffChannels(Current(engine)));
        InputSetups.NotifyChanged();
    }

    /// <summary>Tell the engine which inputs of the current device are switched off — at
    /// start, after a device change and after every edit.</summary>
    public static void Push(IAudioEngine engine)
    {
        Refresh(engine);
        engine.SetDisabledInputs(InputSetups.OffChannels(Current(engine)));
        InputSetups.NotifyChanged(); // pickers and labels show the device's own names
    }
}
