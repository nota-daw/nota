// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Loc.T for XAML (spec 068): Header="{app:T 'Save as…'}". The English text is the key, as in
// code; a text the language lacks stays English. Windows load their XAML after the language is
// installed, so this is right for everything under a Window. App.axaml loads earlier, in
// Initialize(), so its few texts are set from code in App.InstallLanguage instead.

using System;
using Avalonia.Markup.Xaml;
using Nota.Application.Localization;

namespace Nota.App;

public sealed class TExtension : MarkupExtension
{
    public TExtension(string text) => Text = text;

    /// <summary>The English text, which is also the lookup key.</summary>
    public string Text { get; }

    /// <summary>Separates two meanings of the same English word (see Loc.T).</summary>
    public string? Context { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Text, Context);
}
