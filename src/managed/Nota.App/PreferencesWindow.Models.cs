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
using Nota.Application.Localization;

namespace Nota.App;

public sealed partial class PreferencesWindow
{
    private readonly IModelStore _models = App.Services.GetRequiredService<IModelStore>();
    private ContentControl? _modelList;              // null while another pane is showing

    private static string ModelKey(string id) => "model:" + id;

    private static readonly string ModelsIntro = Loc.T("preferences.models.ai_models_that_run_on");

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
            Text = Loc.T("preferences.models.onnx_runtime_has_no_build"),
            FontSize = 11, Foreground = TextTertiary, Margin = new Thickness(16, 14),
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
                        new TextBlock { Text = m.Name, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = m.Feature, FontSize = 9, FontFamily = NotaFonts.ValuesFamily, Foreground = TextMuted, VerticalAlignment = VerticalAlignment.Center },
                    },
                },
                new TextBlock { Text = $"{m.Author} · {m.License}", FontSize = 9, FontFamily = NotaFonts.ValuesFamily, Foreground = TextTertiary },
                new TextBlock { Text = m.Description, FontSize = 11, LineHeight = 18, Foreground = TextSecondary, TextWrapping = TextWrapping.Wrap },
            },
        };

        var actions = new StackPanel { Spacing = 8, MinWidth = 96, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        bool busy = Jobs.BusyId is not null;
        if (Jobs.BusyId == ModelKey(m.Id))
            actions.Children.Add(StoreButton(installed ? Loc.T("preferences.models.removing") : Loc.T("preferences.installing"), enabled: false, () => { }));
        else if (installed)
        {
            actions.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6, Height = 26, HorizontalAlignment = HorizontalAlignment.Right,
                Children = { Dot(Brass), new TextBlock { Text = Loc.T("preferences.installed"), FontSize = 10, FontWeight = FontWeight.Medium, Foreground = Brass, VerticalAlignment = VerticalAlignment.Center } },
            });
            if (!runtime) actions.Children.Add(StoreLink(Loc.T("preferences.remove"), null, busy ? null : () => RemoveModel(m)));
        }
        else if (!runtime)
            actions.Children.Add(StoreButton(Loc.T("preferences.install"), enabled: !busy && _models.Runtime is not null, () => InstallModel(m)));
        else
            actions.Children.Add(new TextBlock { Text = Loc.T("preferences.models.comes_with_a_model"), FontSize = 10, Foreground = TextTertiary, HorizontalAlignment = HorizontalAlignment.Right, Height = 26 });

        long download = runtime || installed || _models.Runtime is null ? m.Size : _models.DownloadSize(m.Id);
        var size = new TextBlock
        {
            Text = installed ? Megabytes(m.UnpackedSize) : Megabytes(download),
            FontSize = 9, FontFamily = NotaFonts.UiFamily, Foreground = TextTertiary, HorizontalAlignment = HorizontalAlignment.Right,
        };
        ToolTip.SetTip(size, installed ? Loc.T("preferences.models.on_disk")
            : download > m.Size ? Loc.F("preferences.models.model_ai_runtime_downloaded_once", Megabytes(m.Size), Megabytes(download - m.Size))
            : Loc.T("common.download"));
        actions.Children.Add(size);
        var source = StoreLink(Loc.T("preferences.source"), GlyphKind.PopOut, () => _ = Launcher.LaunchUriAsync(new Uri(m.Source)));
        ToolTip.SetTip(source, Loc.T("preferences.models.open_the_models_project_page"));
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
        Jobs.Install(ModelKey(m.Id), source: 2, m.Name, 0, new StoreProgress(0, Loc.F("preferences.downloading", m.Name)),
            (progress, ct) => _models.InstallAsync(m.Id, progress, ct),
            () => Task.FromResult(m.Id == AiModels.Stems
                ? Loc.F("preferences.models.is_installed_right_click_an", m.Name)
                : Loc.F("preferences.models.is_installed_convert_melody_and", m.Name)));
    }

    private async void RemoveModel(StoreModel m)
    {
        if (Jobs.Busy) return;
        bool last = _models.Models.Count(x => _models.IsInstalled(x.Id)) == 1;
        var ok = await new ConfirmWindow(Loc.T("preferences.models.remove_ai_model"),
            Loc.F("preferences.models.remove_will_offer_to_download", m.Name, m.Feature) + (last ? Loc.T("preferences.models.the_ai_runtime_goes_too") : ""),
            Loc.T("preferences.remove"), Loc.T("common.cancel")).ShowDialog<bool>(this);
        if (!ok) return;
        Jobs.Remove(ModelKey(m.Id), source: 2, Loc.F("preferences.removing", m.Name), async () =>
        {
            await Task.Run(() => _models.Uninstall(m.Id));
            return Loc.F("preferences.was_removed", m.Name);
        });
    }
}
