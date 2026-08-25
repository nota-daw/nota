// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// The Mixer lives in its own window (View → Mixer / ⌘M) rather than as a main-view tab.
// The single MixerView instance is hosted in the window while it's open and detached on close.

using System;

namespace Nota.App;

public partial class MainWindow
{
    private MixerWindow? _mixerWindow;

    private void OnMenuMixer(object? sender, EventArgs e) => ToggleMixerWindow();

    /// <summary>Open the Mixer window, or close it if it's already open (⌘M / View → Mixer).</summary>
    internal void ToggleMixerWindow()
    {
        if (_mixerWindow is not null) { _mixerWindow.Close(); return; }
        if (_mixer is null) return;

        var win = new MixerWindow(this);
        win.EnableMidiLearn(_learn);   // faders/knobs stay MIDI-mappable in the window
        win.WindowClosed += () => { if (_mixer is not null) _mixer.IsVisible = false; _mixerWindow = null; };
        _mixerWindow = win;

        _mixer.IsVisible = true;       // so the tick loop updates its meters
        SetHost(win.Host, _mixer);
        _mixer.Refresh();
        win.Show(this);
    }
}
