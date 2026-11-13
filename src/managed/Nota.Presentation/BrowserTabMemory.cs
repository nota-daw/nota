// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The browser keeps two "last tab" memories (spec 095, #592): one for Arrangement / Session
// and one for Modular, whose rail carries the Modular tab only there. The first time Modular
// opens it shows the Modular tab; after that whichever tab was last picked in Modular.
// Runtime only, like the browser's last tab has always been.

namespace Nota.Presentation;

public sealed class BrowserTabMemory
{
    private readonly int _modularTab;
    private int _outside;
    private int? _inModular;

    public BrowserTabMemory(int startTab, int modularTab)
    {
        _modularTab = modularTab;
        _outside = startTab == modularTab ? 0 : startTab;
    }

    /// <summary>True between <see cref="Enter"/> and <see cref="Leave"/>.</summary>
    public bool InModular { get; private set; }

    /// <summary>The user picked <paramref name="tab"/> in the current context.</summary>
    public void Picked(int tab)
    {
        if (InModular) _inModular = tab;
        else if (tab != _modularTab) _outside = tab;   // never on the rail outside Modular
    }

    /// <summary>Modular became the view: the tab to show.</summary>
    public int Enter()
    {
        InModular = true;
        return _inModular ?? _modularTab;
    }

    /// <summary>Modular stopped being the view: the tab to show.</summary>
    public int Leave()
    {
        InModular = false;
        return _outside;
    }
}
