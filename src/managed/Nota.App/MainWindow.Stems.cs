// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Separate Stems (audio clip menu): splits the clip into Drums / Bass / Other / Vocals tracks
// with the htdemucs model (IClipAi). The first use offers to download the model, and the AI
// runtime with it; the separation itself runs behind a modal progress dialog with Cancel.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;

namespace Nota.App;

public partial class MainWindow
{
    private async void OnSeparateStems(int trackId, int clipIndex)
    {
        if (_vm is null) return;
        var ai = App.Services.GetRequiredService<IClipAi>();
        if (!await EnsureModelAsync(AiModels.Stems, "Separate Stems")) return;

        StemJob job;
        try { job = ai.ReadStemSource(trackId, clipIndex); }
        catch (InvalidOperationException ex) { _vm.StatusText = ex.Message; return; }

        StemResult? stems = null;
        try
        {
            bool done = await RunBlockingAsync("Separate Stems", "Preparing audio…", async (prog, ct) =>
            {
                var p = new Progress<(double Fraction, string Label)>(r => prog.Report(ProgressReport.At(r.Fraction, r.Label)));
                stems = await Task.Run(() => ai.Separate(job, p, ct), ct);
            });
            if (!done || stems is null) { _vm.StatusText = "Separate Stems cancelled."; return; }

            int group = ai.ApplyStems(job, stems);
            Timeline.Refresh();
            if (group > 0) Timeline.Select(group, -1);
            _session?.Refresh();
            _vm.StatusText = $"Separated \"{job.Name}\" into drums, bass, other and vocals.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or StoreException)
        {
            _vm.StatusText = $"Separate Stems failed: {ex.Message}";
        }
        catch (Exception ex)
        {
            App.Services.GetRequiredService<ILogSink>().Error("Separate Stems failed", ex);
            _vm.StatusText = $"Separate Stems failed: {ex.Message}";
        }
        finally
        {
            stems?.Dispose();
        }
    }

    /// <summary>True when the model is installed, or the user agreed to download it and the
    /// download finished.</summary>
    private async Task<bool> EnsureModelAsync(string id, string feature)
    {
        var store = App.Services.GetRequiredService<IModelStore>();
        if (store.IsInstalled(id) && store.RuntimePath is not null) return true;
        if (_vm is null) return false;
        if (store.Runtime is null)
        {
            _vm.StatusText = $"{feature} isn't available on this computer.";
            return false;
        }

        var model = store.Models.First(m => m.Id == id);
        bool withRuntime = store.RuntimePath is null;
        string what = withRuntime ? $"the {model.Name} model and the AI runtime it runs on" : $"the {model.Name} model";
        bool ok = await new ConfirmWindow(feature,
            $"{feature} uses an AI model that runs on this computer. Download {what} ({Size(store.DownloadSize(id))}) once? " +
            "You can remove it later in Settings → Downloads → AI Models.",
            "Download", "Cancel").ShowDialog<bool>(this);
        if (!ok) return false;

        try
        {
            bool done = await RunBlockingAsync(feature, "Downloading…", async (prog, ct) =>
            {
                var p = new Progress<StoreProgress>(r => prog.Report(r.Fraction < 0 ? ProgressReport.Indeterminate(r.Message) : ProgressReport.At(r.Fraction, r.Message)));
                await store.InstallAsync(id, p, ct);
            });
            if (!done) _vm.StatusText = "Download cancelled.";
            return done && store.IsInstalled(id);
        }
        catch (StoreException ex)
        {
            _vm.StatusText = ex.Message;
            return false;
        }
    }

    private static string Size(long bytes)
        => bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0} GB" : $"{Math.Max(1, bytes / (double)(1 << 20)):0} MB";
}
