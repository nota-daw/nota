// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The status bar's sample rate · buffer size readout (#373, specs/055). A click opens the
// buffer menu: the sizes Settings → Audio offers, each with its block latency, and Audio
// settings… under them. Under ASIO the driver owns the buffer and WASAPI exclusive uses the
// device's own period, so there the menu says so instead of offering sizes.

using System.Collections.Generic;
using Avalonia.Controls;
using Nota.Presentation;

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
        var items = new List<Control> { MenuKit.Caption("Buffer size") };
        switch (vm.BufferOwner)
        {
            case BufferOwner.Driver:
                items.Add(MenuKit.Item("Driver settings…", click: () => engine.OpenAsioControlPanel()));
                items.Add(MenuKit.Caption("Set in the ASIO driver's settings"));
                break;
            case BufferOwner.Device:
                items.Add(MenuKit.Caption("Exclusive mode uses the device's buffer"));
                break;
            default:
                double rate = engine.NegotiatedSampleRate > 0 ? engine.NegotiatedSampleRate : engine.SampleRate;
                int staged = engine.GetAudioConfig().BufferFrames;
                bool recording = engine.IsRecording;
                foreach (int frames in AudioFormatStatus.BufferSizes)
                {
                    int size = frames;
                    var mi = MenuKit.Item(size == 0 ? "Device default" : size.ToString(),
                        click: () => { if (size != engine.GetAudioConfig().BufferFrames) vm.SetAudioBufferFrames(size); },
                        enabled: !recording);
                    mi.ToggleType = MenuItemToggleType.Radio;
                    mi.IsChecked = size == staged;
                    MenuKit.SetTrailing(mi, AudioFormatStatus.Latency(rate, size));
                    items.Add(mi);
                }
                if (recording) items.Add(MenuKit.Caption("Stop recording to change it"));
                break;
        }
        items.Add(new Separator());
        items.Add(MenuKit.Item("Audio settings…", GlyphKind.Levels, () => ShowPreferencesPage("Audio")));
        return items.ToArray();
    }
}
