// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Settings → General → Editing (specs/088): how the editors behave — piano-roll key labels,
// fades on split audio clips, and whether a selection opens the detail panel. Moved off
// Appearance as they were (#427): they are behaviour, not the look.

using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;
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
            Section(Loc.T("preferences.detail_panel"), 10, openOnSelect,
                Caption(Loc.T("preferences.off_a_click_only_selects"))));
    }
}
