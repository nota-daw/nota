// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Mosaic around the window: the "Create multisample" preview (from a browser folder or
// files dropped on a Mosaic card), the pack presets the library writes for installed sample
// packs (the browser lists them under Nota Mosaic → Packs), and a missing pack's "Install".

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;

namespace Nota.App;

public partial class MainWindow
{
    private void InitMosaic()
    {
        var packs = App.Services.GetService<IMosaicPacks>();
        if (packs is not null)
            packs.Changed += () => Dispatcher.UIThread.Post(() => _vm?.Browser.Rebuild(), DispatcherPriority.Background);
        MosaicInstrumentCard.CreateMultisampleRequested += (track, paths) => OpenMosaicCreate(paths, track);
        MosaicInstrumentCard.InstallPackRequested += _ => ShowPreferences(1);   // Downloads → Sample Packs
    }

    /// <summary>Opens the mapping preview for files / folders. With a Mosaic <paramref name="trackId"/>
    /// the first preset made also loads there; without one it lands on a new Mosaic track.</summary>
    private async void OpenMosaicCreate(IReadOnlyList<string> paths, int trackId)
    {
        var packs = App.Services.GetService<IMosaicPacks>();
        if (packs is null || _vm is null || paths.Count == 0) return;
        var win = new MosaicCreateWindow(packs, paths);
        await win.ShowDialog(this);
        if (win.Created.Count == 0) return;
        _vm.Browser.Rebuild();
        var (_, prog) = win.Created[0];
        var engine = _vm.Engine;
        int t = trackId > 0 && engine.TrackInstrumentKind(trackId) == MosaicModel.Kind ? trackId : -1;
        if (t < 0) { t = engine.AddMosaicTrack(); engine.AddMidiClip(t, 0.0, 4.0); }
        engine.MosaicSetProgram(t, prog.Serialize());
        Timeline.Refresh();
        ShowDevices(t);
        _vm.StatusText = win.Created.Count == 1 ? $"Created the preset {prog.Name}" : $"Created {win.Created.Count} presets · loaded {prog.Name}";
    }
}
