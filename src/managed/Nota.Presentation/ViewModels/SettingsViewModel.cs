// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using CommunityToolkit.Mvvm.ComponentModel;
using Nota.Application;

namespace Nota.Presentation;

/// <summary>Backs the Preferences window (M4.1-B). Persists on change; the toolbar
/// side is applied to the layout live (M4.1-E).</summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;

    [ObservableProperty] private bool _toolbarRight;

    public SettingsViewModel(ISettingsService settings)
    {
        _settings = settings;
        _toolbarRight = settings.Current.ToolbarSide == ToolbarSide.Right;
    }

    partial void OnToolbarRightChanged(bool value)
    {
        _settings.Current.ToolbarSide = value ? ToolbarSide.Right : ToolbarSide.Left;
        _settings.Save();
    }
}
