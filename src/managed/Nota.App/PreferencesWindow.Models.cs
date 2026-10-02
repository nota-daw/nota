// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Preferences → Downloads → AI Models: install / remove the models behind Separate Stems and
// Convert to MIDI (IModelStore), and see the ONNX Runtime they share, which comes with the first
// model and goes with the last. Shares the job state (DownloadJobs) with Plug-ins and Sample Packs:
// one download at a time, shown in the same dock. A model's busy key is "model:<id>".

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;

namespace Nota.App;

public sealed partial class PreferencesWindow
{
    private readonly IModelStore _models = App.Services.GetRequiredService<IModelStore>();
    private ContentControl? _modelList;              // null while another pane is showing

    private static string ModelKey(string id) => "model:" + id;

    private const string ModelsIntro = "AI models that run on this computer: the audio you give them never leaves it. Each downloads once from where its authors publish it and is checked against the checksum Nota pins for it.";

    // The AI Models third of Downloads (PreferencesWindow.Downloads.cs). No toolbar: the list is short.
    private (Control? Toolbar, Control Body) ModelParts()
    {
        var list = _modelList = new ContentControl();
        var body = new StackPanel { Spacing = 14, Children = { list } };
        body.DetachedFromVisualTree += (_, _) => { if (_modelList == list) _modelList = null; };
        RenderModelList();
        return (null, body);
    }

    private void RenderModelList()
    {
        if (_modelList is null) return;
        var rows = new List<Control>();
        foreach (var m in _models.Models) rows.Add(ModelRow(m, runtime: false));
        if (_models.Runtime is { } rt) rows.Add(ModelRow(rt, runtime: true));
        else rows.Add(new TextBlock
        {
            Text = "ONNX Runtime has no build for this computer, so the models can't run here.",
            FontSize = 12, Foreground = TextTertiary, Margin = new Thickness(16, 14),
        });
        _modelList.Content = Table(rows);
    }

    private Control ModelRow(StoreModel m, bool runtime)
    {
        bool installed = _models.IsInstalled(m.Id);
        var text = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new WrapPanel
                {
                    Children =
                    {
                        new TextBlock { Text = m.Name, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = m.Feature.ToUpperInvariant(), FontSize = 9, LetterSpacing = 0.7, FontFamily = NotaFonts.MonoFamily, Foreground = TextMuted, VerticalAlignment = VerticalAlignment.Center },
                    },
                },
                new TextBlock { Text = $"{m.Author} · {m.License}", FontSize = 10, FontFamily = NotaFonts.MonoFamily, Foreground = TextTertiary },
                new TextBlock { Text = m.Description, FontSize = 12, LineHeight = 18, Foreground = TextSecondary, TextWrapping = TextWrapping.Wrap },
            },
        };

        var actions = new StackPanel { Spacing = 8, MinWidth = 96, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        bool busy = Jobs.BusyId is not null;
        if (Jobs.BusyId == ModelKey(m.Id))
            actions.Children.Add(StoreButton(installed ? "Removing…" : "Installing…", enabled: false, () => { }));
        else if (installed)
        {
            actions.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6, Height = 26, HorizontalAlignment = HorizontalAlignment.Right,
                Children = { Dot(Brass), new TextBlock { Text = "Installed", FontSize = 11, FontWeight = FontWeight.Medium, Foreground = Brass, VerticalAlignment = VerticalAlignment.Center } },
            });
            if (!runtime) actions.Children.Add(StoreLink("Remove", null, busy ? null : () => RemoveModel(m)));
        }
        else if (!runtime)
            actions.Children.Add(StoreButton("Install", enabled: !busy && _models.Runtime is not null, () => InstallModel(m)));
        else
            actions.Children.Add(new TextBlock { Text = "Comes with a model", FontSize = 11, Foreground = TextTertiary, HorizontalAlignment = HorizontalAlignment.Right, Height = 26 });

        long download = runtime || installed || _models.Runtime is null ? m.Size : _models.DownloadSize(m.Id);
        var size = new TextBlock
        {
            Text = installed ? Megabytes(m.UnpackedSize) : Megabytes(download),
            FontSize = 10, FontFamily = NotaFonts.MonoFamily, Foreground = TextTertiary, HorizontalAlignment = HorizontalAlignment.Right,
        };
        ToolTip.SetTip(size, installed ? "On disk"
            : download > m.Size ? $"{Megabytes(m.Size)} model + {Megabytes(download - m.Size)} AI runtime, downloaded once"
            : "Download");
        actions.Children.Add(size);
        var source = StoreLink("Source", GlyphKind.PopOut, () => _ = Launcher.LaunchUriAsync(new Uri(m.Source)));
        ToolTip.SetTip(source, "Open the model's project page");
        actions.Children.Add(source);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 16, Margin = new Thickness(16, 14, 14, 14) };
        grid.Children.Add(text);
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);
        return grid;
    }

    private void InstallModel(StoreModel m)
    {
        // Size 0: the model and the runtime download one after the other, each reporting its own
        // fraction, so a byte count would mix two files.
        Jobs.Install(ModelKey(m.Id), source: 2, m.Name, 0, new StoreProgress(0, $"Downloading {m.Name}…"),
            (progress, ct) => _models.InstallAsync(m.Id, progress, ct),
            () => Task.FromResult(m.Id == AiModels.Stems
                ? $"{m.Name} is installed — right-click an audio clip and choose Separate Stems."
                : $"{m.Name} is installed — Convert Melody and Convert Harmony now use it."));
    }

    private async void RemoveModel(StoreModel m)
    {
        if (Jobs.Busy) return;
        bool last = _models.Models.Count(x => _models.IsInstalled(x.Id)) == 1;
        var ok = await new ConfirmWindow("Remove AI model",
            $"Remove {m.Name}? {m.Feature} will offer to download it again." + (last ? " The AI runtime goes too." : ""),
            "Remove", "Cancel").ShowDialog<bool>(this);
        if (!ok) return;
        Jobs.Remove(ModelKey(m.Id), source: 2, $"Removing {m.Name}…", async () =>
        {
            await Task.Run(() => _models.Uninstall(m.Id));
            return $"{m.Name} was removed.";
        });
    }
}
