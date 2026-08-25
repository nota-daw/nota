// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.

using Avalonia;
using Avalonia.Controls;

namespace Nota.App;

internal static class ControlExtensions
{
    /// <summary>
    /// Binds <paramref name="property"/> to the dynamic theme resource
    /// <paramref name="key"/> (e.g. "Brush.TextPrimary", "Font.Mono").
    /// Use this in code-built views instead of <c>TryFindResource</c>: those views
    /// are constructed during MainWindow's XAML load, before they attach to the
    /// visual tree, so <c>TryFindResource</c> returns the magenta fallback. The
    /// resource observable re-resolves the token once the control attaches.
    /// </summary>
    public static void BindResource(this Control control, AvaloniaProperty property, string key)
        => control.Bind(property, control.GetResourceObservable(key));
}
