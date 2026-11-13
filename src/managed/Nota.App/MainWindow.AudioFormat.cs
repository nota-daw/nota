// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The status bar's sample rate · buffer size readout (#373, specs/061). A click opens the
// buffer menu: the sizes Settings → Audio offers, each with its block latency, and Audio
// settings… under them. Under ASIO the driver owns the buffer and WASAPI exclusive uses the
// device's own period, so there the menu says so instead of offering sizes.

using System.Collections.Generic;
using Avalonia.Controls;
using Nota.Presentation;
using Nota.Application.Localization;

namespace Nota.App;

public partial class MainWindow
{
    private void SetUpAudioFormatButton()
    {
        AudioFormatButton.Click += (_, _) =>
        {
            if (_vm is null) return;
            var flyout = new MenuFlyout { Placement = PlacementMode.TopEdgeAlignedRight };
            foreach (var item in AudioFormatMenu(_vm)) flyout.Items.Add(item);
            flyout.ShowAt(AudioFormatButton);
        };
    }

    private Control[] AudioFormatMenu(MainWindowViewModel vm)
    {
        var engine = vm.Engine;
        var items = new List<Control> { MenuKit.Caption(Loc.T("main.audio_format.buffer_size")) };
        switch (vm.BufferOwner)
        {
            case BufferOwner.Driver:
                items.Add(MenuKit.Item(Loc.T("main.audio_format.driver_settings"), click: () => engine.OpenAsioControlPanel()));
                items.Add(MenuKit.Caption(Loc.T("main.audio_format.set_in_the_asio_drivers")));
                break;
            case BufferOwner.Device:
                items.Add(MenuKit.Caption(Loc.T("main.audio_format.exclusive_mode_uses_the_devices")));
                break;
            default:
                double rate = engine.NegotiatedSampleRate > 0 ? engine.NegotiatedSampleRate : engine.SampleRate;
                int staged = engine.GetAudioConfig().BufferFrames;
                bool recording = engine.IsRecording;
                foreach (int frames in AudioFormatStatus.BufferSizes)
                {
                    int size = frames;
                    var mi = MenuKit.Item(size == 0 ? Loc.T("main.audio_format.device_default") : size.ToString(),
                        click: () => { if (size != engine.GetAudioConfig().BufferFrames) vm.SetAudioBufferFrames(size); },
                        enabled: !recording);
                    mi.ToggleType = MenuItemToggleType.Radio;
                    mi.IsChecked = size == staged;
                    MenuKit.SetTrailing(mi, AudioFormatStatus.Latency(rate, size));
                    items.Add(mi);
                }
                if (recording) items.Add(MenuKit.Caption(Loc.T("main.audio_format.stop_recording_to_change_it")));
                break;
        }
        items.Add(new Separator());
        items.Add(MenuKit.Item(Loc.T("main.audio_format.audio_settings"), GlyphKind.Levels, ShowAudioPreferences));
        return items.ToArray();
    }

    /// <summary>Settings on the Audio page (sample rate, buffer — the format menu's way in).</summary>
    internal void ShowAudioPreferences() => ShowPreferencesPage("Audio");

    /// <summary>Settings on the Inputs page (the input setup, specs/086 and 088).</summary>
    internal void ShowInputPreferences() => ShowPreferencesPage("Inputs");
}
