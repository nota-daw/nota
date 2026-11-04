// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Loc.T for XAML (spec 068, keyed since spec 077): Header="{app:T 'main.save_as'}". The argument
// is a key of the English reference, as in code; a text the language lacks stays English.
// Windows load their XAML after the language is installed, so this is right for everything under
// a Window. App.axaml loads earlier, in Initialize(), so its few texts are set from code in
// App.TranslateAppMenu instead.

using System;
using Avalonia.Markup.Xaml;
using Nota.Application.Localization;

namespace Nota.App;

public sealed class TExtension : MarkupExtension
{
    public TExtension(string key) => Key = key;

    /// <summary>The key of the text in the English reference (en.json).</summary>
    public string Key { get; }

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Key);
}
