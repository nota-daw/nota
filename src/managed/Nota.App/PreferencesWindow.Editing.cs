// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Settings → General → Editing (specs/088): how the editors behave — piano-roll key labels,
// fades on split audio clips, whether a selection opens the detail panel, and which way the
// Session grid runs (specs/108). Moved off Appearance as they were (#427): they are
// behaviour, not the look.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;
using Nota.Application.Editing;
using Nota.Application.Localization;

namespace Nota.App;

public sealed partial class PreferencesWindow
{
    private Control EditingPane()
    {
        var settings = App.Services.GetService<ISettingsService>();

        // Piano-roll key labels: every note for beginners, C-only by default.
        var labelAll = SwitchRow(Loc.T("preferences.label_every_note_in_the"), settings?.Current.PianoRollLabelAllNotes ?? false, on =>
        {
            if (settings is null) return;
            settings.Current.PianoRollLabelAllNotes = on;
            settings.Save();
        });

        // Detail panel (#308): a selection opens a closed panel only when this is on.
        var openOnSelect = SwitchRow(Loc.T("preferences.open_the_detail_panel_when"), settings?.Current.OpenDetailOnSelect ?? false, on =>
        {
            if (settings is null) return;
            settings.Current.OpenDetailOnSelect = on;
            settings.Save();
        });

        var splitFades = SwitchRow(Loc.T("preferences.fade_audio_clips_when_splitting"), settings?.Current.SplitFades ?? true, on =>
        {
            if (settings is null) return;
            settings.Current.SplitFades = on;
            settings.Save();
            if (_main is not null) _main.Engine.SplitFades = on;
        });

        return Sections(
            Section(Loc.T("common.piano_roll"), 10, labelAll,
                Caption(Loc.T("preferences.shows_every_note_name_c"))),
            Section(Loc.T("preferences.arrangement"), 10, splitFades,
                Caption(Loc.T("preferences.splitting_an_audio_clip_adds"))),
            Section(Loc.T("preferences.session"), 10,
                Row(Loc.T("preferences.session_grid"), SessionGridChoice(settings)),
                Caption(Loc.T("preferences.session_grid_hint"))),
            Section(Loc.T("preferences.detail_panel"), 10, openOnSelect,
                Caption(Loc.T("preferences.off_a_click_only_selects"))));
    }

    // Scenes down / Scenes across (specs/108): an open Session view turns at once — it listens
    // to the settings' Changed event.
    private static Control SessionGridChoice(ISettingsService? settings)
    {
        string[] labels = { Loc.T("preferences.scenes_down"), Loc.T("preferences.scenes_across") };
        int cur = SessionGridNav.IsAcross(settings?.Current.SessionGrid) ? 1 : 0;
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        var segs = new ToggleButton[labels.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            int idx = i;
            segs[i] = new ToggleButton { Classes = { "seg" }, FontSize = 11, IsChecked = i == cur, Content = labels[i] };
            segs[i].Click += (_, _) =>
            {
                cur = idx;
                for (int k = 0; k < segs.Length; k++) segs[k].IsChecked = k == cur;   // a toggle never clears itself
                if (settings is null) return;
                string v = idx == 1 ? SessionGridNav.Across : SessionGridNav.Down;
                if (settings.Current.SessionGrid == v) return;
                settings.Current.SessionGrid = v;
                settings.Save();
            };
            strip.Children.Add(segs[i]);
        }
        var box = new Border { Classes = { "segmented" }, Child = strip, HorizontalAlignment = HorizontalAlignment.Left };
        // The Session toolbar's turn key writes the same setting: follow it while the page shows.
        void Follow()
        {
            cur = SessionGridNav.IsAcross(settings?.Current.SessionGrid) ? 1 : 0;
            for (int k = 0; k < segs.Length; k++) segs[k].IsChecked = k == cur;
        }
        if (settings is not null)
        {
            box.AttachedToVisualTree += (_, _) => { settings.Changed += Follow; Follow(); };
            box.DetachedFromVisualTree += (_, _) => settings.Changed -= Follow;
        }
        return box;
    }
}
