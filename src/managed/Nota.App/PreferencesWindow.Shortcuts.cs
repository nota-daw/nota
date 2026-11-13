// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Settings → Shortcuts as an editor (specs/096, #61). Every keyboard row is a binding: click
// its key, press a new one, confirm. Before anything is saved the row names the actions that
// already answer that key where this one is live (Replace / Cancel), refuses keys the system
// keeps, and notes reuses elsewhere that don't clash. Edited rows carry an accent dot, their
// default and a reset; the header has "Edited only" and Reset all. Mouse gestures, list and
// typing keys stay as plain read-only rows (ShortcutLayout).

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.App.Input;
using Nota.Application.Input;
using Nota.Application.Localization;

namespace Nota.App;

public partial class PreferencesWindow
{
    private enum EditKind { None, Capture, Reset }

    // The one row being edited: recording a key (Capture) or confirming a reset that takes a
    // key from another action (Reset).
    private EditKind _edit;
    private string? _editId;
    private KeySlot _editSlot;
    private Shortcut? _proposed;
    private string? _refusal;
    private bool _editedOnly;
    private bool _captureHooked;
    private readonly Dictionary<string, Decorator> _rowHosts = new(StringComparer.Ordinal);
    private Action? _renderShortcuts;

    private static KeyBindings Bindings => Keys.Bindings;

    private Control ShortcutsPane()
    {
        var groups = new StackPanel { Spacing = 18 };
        var filter = new TextBox { PlaceholderText = Loc.T("preferences.filter_shortcuts"), Text = _shortcutFilter, Classes = { "search" }, FontSize = 11 };
        var resetAll = Btn(Loc.T("keys.ui.reset_all"));
        resetAll.Click += async (_, _) =>
        {
            EndEdit();
            var ok = await new ConfirmWindow(Loc.T("keys.ui.reset_all_title"), Loc.T("keys.ui.reset_all_message"),
                Loc.T("keys.ui.reset_all_confirm"), Loc.T("common.cancel")).ShowDialog<bool>(this);
            if (ok) Bindings.ResetAll();
        };
        var editedOnly = SwitchRow(Loc.T("keys.ui.edited_only"), _editedOnly, on => { _editedOnly = on; _renderShortcuts?.Invoke(); });

        void Render()
        {
            groups.Children.Clear();
            _rowHosts.Clear();
            resetAll.IsEnabled = Bindings.AnyEdited;
            // Each word must appear in the row's action, its keys or its group's title, so
            // "keyboard" finds the whole "Play notes (computer keyboard)" group (#40) and "cmd t"
            // the action on ⌘T (FR-017).
            var words = _shortcutFilter.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            // A filter that reads as one key ("⌘T", "cmd t", "ctrl+shift+t") finds that key.
            var chord = KeyQuery(_shortcutFilter);

            if (Bindings.NeedsDecision.Count > 0 && words.Length == 0)
            {
                var rows = Bindings.NeedsDecision.Select(id => (Control)ActionRowHost(id, needsDecision: true)).ToList();
                groups.Children.Add(Group(Loc.T("keys.ui.needs_decision"), rows));
            }
            foreach (var g in ShortcutLayout.Build(Bindings.Actions.ToList(), MainWindow.HasMenuKey))
            {
                var shown = new List<Control>();
                foreach (var r in g.Rows)
                {
                    if (r.ActionId is { } id)
                    {
                        if (_editedOnly && !Bindings.IsEdited(id) && id != _editId) continue;
                        if (chord is { } c ? !Bindings.Matches(id, c) : !words.All(w => Matches(w, g.Title) || ActionMatches(w, id))) continue;
                        shown.Add(ActionRowHost(id, needsDecision: false));
                    }
                    else
                    {
                        if (_editedOnly) continue;
                        if (chord is { } c ? !r.Key.Split(' ').Contains(c.ToGlyphs())
                            : !words.All(w => Matches(w, g.Title) || Matches(w, MenuKit.Keys(r.Text)) || Matches(w, MenuKit.Keys(r.Key)))) continue;
                        shown.Add(FixedShortcutRow(r.Key, r.Text));
                    }
                }
                if (shown.Count > 0) groups.Children.Add(Group(g.Title, shown));
            }
            if (groups.Children.Count == 0) groups.Children.Add(Caption(_editedOnly ? Loc.T("keys.ui.nothing_edited") : Loc.T("preferences.nothing_matches")));
        }
        _renderShortcuts = Render;
        filter.TextChanged += (_, _) => { _shortcutFilter = filter.Text ?? ""; Render(); };
        HookCapture();
        Render();

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 16 };
        var search = SearchField(filter, 320);
        header.Children.Add(search);
        Grid.SetColumn(editedOnly, 1);
        editedOnly.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(editedOnly);
        Grid.SetColumn(resetAll, 2);
        header.Children.Add(resetAll);

        return new StackPanel
        {
            Spacing = 18,
            Children = { VelocityWheelSection(), header, Caption(Loc.T("keys.ui.how_to_edit")), groups },
        };
    }

    private void OnBindingsChanged() => _renderShortcuts?.Invoke();

    private static Control Group(string title, List<Control> rows)
    {
        var head = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 4),
            Children =
            {
                new TextBlock { Text = title, Classes = { "SectionLabel" }, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = rows.Count.ToString(), FontSize = 8.5, FontFamily = NotaFonts.ValuesFamily, Foreground = TextDisabled, VerticalAlignment = VerticalAlignment.Center },
            },
        };
        return new StackPanel { Spacing = 6, Children = { head, Table(rows) } };
    }

    // ---- filter --------------------------------------------------------------------------

    private static bool Matches(string word, string text) => text.Contains(word, StringComparison.OrdinalIgnoreCase);

    private static bool ActionMatches(string w, string id)
    {
        var a = Bindings.Get(id);
        if (a is null) return false;
        if (Matches(w, a.Title) || Matches(w, a.Category) || a.Aliases.Any(x => Matches(w, x))) return true;
        foreach (var slot in new[] { KeySlot.Primary, KeySlot.Alternative })
            if (Bindings.Current(id, slot) is { } k && KeyWords(k).Any(t => Matches(w, t))) return true;
        return false;
    }

    // The filter as one key, when it reads as one: modifier names or glyphs, then a key —
    // "⌘T", "⌥⇧↑", "cmd t", "Ctrl+Shift+T", "shift f". Null for anything else (plain words).
    private static Shortcut? KeyQuery(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return null;
        var mods = ShortcutModifiers.None;
        bool mac = OperatingSystem.IsMacOS();
        int i = 0;
        for (; i < text.Length && text[i] is KeyGlyphs.Cmd or KeyGlyphs.Option or KeyGlyphs.Ctrl or KeyGlyphs.Shift; i++)
            mods |= text[i] switch
            {
                KeyGlyphs.Cmd => ShortcutModifiers.Primary, KeyGlyphs.Option => ShortcutModifiers.Alt,
                KeyGlyphs.Ctrl => ShortcutModifiers.Ctrl, _ => ShortcutModifiers.Shift,
            };
        var parts = text[i..].Split([' ', '+', '-'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        bool named = i > 0;
        for (int p = 0; p < parts.Length - 1; p++)
        {
            var m = parts[p].ToLowerInvariant() switch
            {
                "cmd" or "command" => ShortcutModifiers.Primary,
                "ctrl" or "control" => mac ? ShortcutModifiers.Ctrl : ShortcutModifiers.Primary,
                "alt" or "opt" or "option" => ShortcutModifiers.Alt,
                "shift" => ShortcutModifiers.Shift,
                _ => (ShortcutModifiers?)null,
            };
            if (m is null) return null;
            mods |= m.Value;
            named = true;
        }
        if (!named) return null;   // a lone word stays a word
        string key = parts[^1].Length == 1 ? parts[^1].ToUpperInvariant() : parts[^1];
        return KeyActionCatalog.IsKnownToken(key) ? new Shortcut(mods, key) : null;
    }

    // A key written every way someone might type it: ⌘⇧T, Ctrl+Shift+T, "cmd shift t".
    private static IEnumerable<string> KeyWords(Shortcut k)
    {
        yield return k.ToGlyphs();
        yield return k.ToPlatformString(mac: false);
        var names = new List<string>();
        if ((k.Modifiers & ShortcutModifiers.Ctrl) != 0) names.Add("ctrl");
        if ((k.Modifiers & ShortcutModifiers.Alt) != 0) names.AddRange(["opt", "option", "alt"]);
        if ((k.Modifiers & ShortcutModifiers.Primary) != 0) names.AddRange(["cmd", "command", "ctrl"]);
        if ((k.Modifiers & ShortcutModifiers.Shift) != 0) names.Add("shift");
        yield return string.Join(" ", names) + " " + k.Key;
    }

    // ---- rows ----------------------------------------------------------------------------

    private Control ActionRowHost(string id, bool needsDecision)
    {
        var host = new Decorator { Child = ActionRow(id, needsDecision) };
        if (!needsDecision) _rowHosts[id] = host;
        return host;
    }

    private void RefreshRow(string id)
    {
        if (_rowHosts.TryGetValue(id, out var host)) host.Child = ActionRow(id, needsDecision: false);
        else _renderShortcuts?.Invoke();
    }

    private Control ActionRow(string id, bool needsDecision)
    {
        var a = Bindings.Get(id)!;
        bool editing = _edit != EditKind.None && _editId == id;
        bool edited = Bindings.IsEdited(id);

        Control keys;
        if (editing && _edit == EditKind.Capture) keys = CaptureField();
        else
        {
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            wrap.Children.Add(KeySlotButton(a, KeySlot.Primary));
            if (!a.Fixed && (Bindings.Current(id) is not null || Bindings.Current(id, KeySlot.Alternative) is not null))
                wrap.Children.Add(KeySlotButton(a, KeySlot.Alternative));
            keys = wrap;
        }

        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center };
        if (edited) title.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = Brass, VerticalAlignment = VerticalAlignment.Center });
        title.Children.Add(new TextBlock
        {
            Text = MenuKit.Keys(a.Title), FontSize = 11, LineHeight = 17, Foreground = TextSecondary,
            VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap,
        });

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (edited && !editing)
        {
            right.Children.Add(new TextBlock { Text = Loc.T("keys.ui.default"), FontSize = 10, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center });
            right.Children.Add(Caps(a.DefaultPrimary, quiet: true));
            var reset = new Button
            {
                Content = new Glyph(GlyphKind.Cycle, 11) { Foreground = TextSecondary }, Padding = new Thickness(5, 3),
                Background = Brushes.Transparent, CornerRadius = NotaRadius.Field, VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(reset, Loc.T("keys.ui.reset_to_default"));
            AutomationProperties.SetName(reset, Loc.T("keys.ui.reset_to_default"));
            reset.Click += (_, _) => StartReset(id);
            right.Children.Add(reset);
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{LabelCol + 40},*,Auto"), ColumnSpacing = 12, MinHeight = 20, Margin = new Thickness(12, 7) };
        grid.Children.Add(keys);
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        var stack = new StackPanel { Spacing = 0, Children = { grid } };
        if (needsDecision) stack.Children.Add(NeedsDecisionLine(a));
        if (editing) stack.Children.Add(EditLine(a));
        return stack;
    }

    // One slot's key as a key-cap button; empty: "—" for the primary, a quiet "+" to add an
    // alternative. A fixed action (macOS ⌘Q) shows its caps without a button.
    private Control KeySlotButton(KeyAction a, KeySlot slot)
    {
        var key = Bindings.Current(a.Id, slot);
        if (a.Fixed) return Caps(key, quiet: false);
        Control face = key is { } k ? Caps(k, quiet: false)
            : new TextBlock
            {
                Text = slot == KeySlot.Primary ? "—" : "+", FontSize = 11, Foreground = TextTertiary,
                Margin = new Thickness(4, 0), VerticalAlignment = VerticalAlignment.Center,
            };
        var b = new Button
        {
            Content = face, Padding = new Thickness(3, 1), Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            CornerRadius = NotaRadius.Field, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        string tip = slot == KeySlot.Primary ? Loc.T("keys.ui.click_to_change") : Loc.T("keys.ui.add_alternative");
        ToolTip.SetTip(b, tip);
        AutomationProperties.SetName(b, $"{a.Title}: {tip}");
        b.Click += (_, _) => StartCapture(a.Id, slot);
        return b;
    }

    private static Control Caps(Shortcut? key, bool quiet)
    {
        var wrap = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (key is not { } k)
        {
            wrap.Children.Add(new TextBlock { Text = "—", FontSize = 11, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center });
            return wrap;
        }
        // The default beside an edited key is quiet text, not caps: it is not the live key.
        if (quiet)
        {
            wrap.Children.Add(new TextBlock { Text = k.ToPlatformString(), FontSize = 10, FontFamily = NotaFonts.ValuesFamily, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center });
            return wrap;
        }
        foreach (var cap in k.ToKeyCaps()) wrap.Children.Add(KeyCap(cap));
        return wrap;
    }

    // The capture field: an accent outline while waiting, the proposed key once pressed.
    private Control CaptureField()
    {
        Control inner = _proposed is { } p ? Caps(p, quiet: false)
            : new TextBlock { Text = Loc.T("keys.ui.press_a_shortcut"), FontSize = 11, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center };
        return new Border
        {
            BorderBrush = Brass, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Field, Background = AccentSubtle,
            Padding = new Thickness(6, 2), MinHeight = 24, HorizontalAlignment = HorizontalAlignment.Left, Child = inner,
        };
    }

    private Control NeedsDecisionLine(KeyAction a)
    {
        var d = a.DefaultPrimary;
        var taker = d is { } k ? Bindings.Actions.FirstOrDefault(x => x.Id != a.Id && Bindings.Matches(x.Id, k)) : null;
        var text = new TextBlock
        {
            Text = MenuKit.Keys(Loc.F("keys.ui.default_taken", d?.ToGlyphs() ?? "", taker?.Title ?? "")),
            FontSize = 10.5, Foreground = NotaPalette.Warning, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
        };
        var leave = Btn(Loc.T("keys.ui.leave_unassigned"));
        leave.Click += (_, _) => Bindings.Set(a.Id, KeySlot.Primary, null);
        return new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(12 + LabelCol + 52, 0, 12, 8),
            Children = { text, leave },
        };
    }

    // Under the row being edited: what the proposed key would do, and the buttons.
    private Control EditLine(KeyAction a)
    {
        var lines = new StackPanel { Spacing = 4 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var cancel = Btn(Loc.T("common.cancel"));
        cancel.Click += (_, _) => EndEdit(a.Id);

        if (_edit == EditKind.Reset)
        {
            var conflicts = Bindings.ResetConflicts(a.Id);
            foreach (var c in conflicts)
                lines.Children.Add(Line(Loc.F("keys.ui.used_by", Bindings.Current(c.Action.Id, c.Slot)?.ToGlyphs() ?? "",
                    c.Action.Title, ScopeName(c.Action.Scope)), NotaPalette.Warning));
            var replace = Btn(Loc.T("keys.ui.replace"));
            replace.Classes.Add("primary");
            replace.Click += (_, _) => { var id = a.Id; EndEdit(); Bindings.Reset(id, conflicts); };
            buttons.Children.Add(replace);
            buttons.Children.Add(cancel);
        }
        else
        {
            if (_refusal is not null) lines.Children.Add(Line(_refusal, NotaPalette.DangerBright));
            List<KeyUse> conflicts = [];
            if (_proposed is { } p && _refusal is null)
            {
                conflicts = Bindings.FindConflicts(a.Id, _editSlot, p);
                foreach (var c in conflicts)
                    lines.Children.Add(Line(c.Action.Id == a.Id
                        ? Loc.F("keys.ui.already_this_action", p.ToGlyphs())
                        : Loc.F("keys.ui.used_by", p.ToGlyphs(), c.Action.Title, ScopeName(c.Action.Scope)), NotaPalette.Warning));
                if (conflicts.Count == 0)
                    foreach (var s in Bindings.FindShadows(a.Id, p).Take(3))
                        lines.Children.Add(Line(s.Action.Scope.IsWindowLayer()
                            // This action's view has the keyboard first: it wins there.
                            ? Loc.F("keys.ui.shadow", p.ToGlyphs(), s.Action.Title, ScopeName(a.Scope))
                            // The other action's view wins while it has the keyboard.
                            : Loc.F("keys.ui.shadowed", p.ToGlyphs(), s.Action.Title, ScopeName(s.Action.Scope)), TextTertiary));
            }
            if (_proposed is { } chosen && _refusal is null)
            {
                bool clash = conflicts.Count > 0;
                bool self = conflicts.Any(c => c.Action.Id == a.Id);
                var ok = Btn(clash ? Loc.T("keys.ui.replace") : Loc.T("keys.ui.confirm"));
                ok.Classes.Add("primary");
                ok.IsEnabled = !self;
                var id = a.Id; var slot = _editSlot; var replace = conflicts;
                ok.Click += (_, _) => { EndEdit(); Bindings.Set(id, slot, chosen, replace); };
                buttons.Children.Add(ok);
            }
            buttons.Children.Add(cancel);
            var clear = Btn(Loc.T("common.clear"));
            clear.IsEnabled = Bindings.Current(a.Id, _editSlot) is not null;
            clear.Click += (_, _) => { var id = a.Id; var slot = _editSlot; EndEdit(); Bindings.Set(id, slot, null); };
            buttons.Children.Add(clear);
            var esc = Btn(Loc.T("keys.ui.assign_esc"));
            esc.Click += (_, _) => Propose(Shortcut.None("Esc"));
            buttons.Children.Add(esc);
        }
        lines.Children.Add(buttons);
        return new Border { Margin = new Thickness(12, 0, 12, 10), Child = lines };

        static TextBlock Line(string text, IBrush brush) => new()
        {
            Text = MenuKit.Keys(text), FontSize = 10.5, Foreground = brush, TextWrapping = TextWrapping.Wrap,
        };
    }

    private static string ScopeName(KeyScope s) => s switch
    {
        KeyScope.Global => Loc.T("keys.scope.global"),
        KeyScope.Arrangement => Loc.T("keys.scope.arrangement"),
        KeyScope.Notes => Loc.T("keys.scope.notes"),
        KeyScope.Session => Loc.T("keys.scope.session"),
        KeyScope.PianoRoll => Loc.T("keys.scope.piano_roll"),
        KeyScope.AudioEditor => Loc.T("keys.scope.audio_editor"),
        KeyScope.Devices => Loc.T("keys.scope.devices"),
        KeyScope.Browser => Loc.T("keys.scope.browser"),
        _ => Loc.T("keys.scope.modular"),
    };

    // ---- editing state -------------------------------------------------------------------

    private void StartCapture(string id, KeySlot slot)
    {
        var previous = _editId;
        _edit = EditKind.Capture;
        _editId = id;
        _editSlot = slot;
        _proposed = null;
        _refusal = null;
        SetCapturing(true);
        if (previous is not null && previous != id) RefreshRow(previous);
        RefreshRow(id);
    }

    private void StartReset(string id)
    {
        if (Bindings.ResetConflicts(id).Count == 0) { EndEdit(); Bindings.Reset(id); return; }
        var previous = _editId;
        SetCapturing(false);
        _edit = EditKind.Reset;
        _editId = id;
        _proposed = null;
        _refusal = null;
        if (previous is not null && previous != id) RefreshRow(previous);
        RefreshRow(id);
    }

    private void EndEdit(string? refresh = null)
    {
        var id = _editId;
        _edit = EditKind.None;
        _editId = null;
        _proposed = null;
        _refusal = null;
        SetCapturing(false);
        if ((refresh ?? id) is { } row) RefreshRow(row);
    }

    private void Propose(Shortcut chord)
    {
        if (_editId is null) return;
        _proposed = chord;
        _refusal = Bindings.Check(_editId, chord) switch
        {
            KeyCheck.Reserved => Loc.F("keys.ui.reserved", chord.ToGlyphs(), ReservedKeys.Reason(chord, Bindings.Platform) ?? ""),
            KeyCheck.Navigation => Loc.F("keys.ui.navigation", chord.ToGlyphs()),
            KeyCheck.Fixed => Loc.T("keys.ui.fixed"),
            _ => null,
        };
        RefreshRow(_editId);
    }

    // While a row records, no window runs a shortcut: MainWindow and the views check
    // Keys.Capturing, and on macOS the menu's key equivalents come off (they fire first).
    private void SetCapturing(bool on)
    {
        if (Keys.Capturing == on) return;
        Keys.Capturing = on;
        if ((Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is MainWindow mw)
            mw.SuspendMenuGestures(on);
    }

    private void HookCapture()
    {
        if (_captureHooked) return;
        _captureHooked = true;
        // Once per window, however often the page is rebuilt: a key changed anywhere redraws it.
        Keys.Bindings.Changed += OnBindingsChanged;
        Closed += (_, _) => { Keys.Bindings.Changed -= OnBindingsChanged; EndEdit(); };
        AddHandler(KeyDownEvent, OnCaptureKey, RoutingStrategies.Tunnel, handledEventsToo: true);
        // Leaving the window cancels the recording (the next key would go elsewhere).
        Deactivated += (_, _) => { if (_edit == EditKind.Capture) EndEdit(); };
    }

    private void OnCaptureKey(object? sender, KeyEventArgs e)
    {
        if (_edit != EditKind.Capture || _editId is null) return;
        e.Handled = true;   // nothing else sees a key while one is being recorded
        if (KeyTokens.IsModifierKey(e.Key)) return;   // a bare modifier: keep waiting
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None) { EndEdit(); return; }   // Assign Esc is a button
        if (KeyTokens.HasSystemKey(e.KeyModifiers))
        {
            _proposed = null;
            _refusal = Loc.F("keys.ui.reserved", OperatingSystem.IsWindows() ? "Win" : "Super", OperatingSystem.IsWindows() ? "Windows" : "Linux");
            RefreshRow(_editId);
            return;
        }
        if (KeyTokens.Chord(e) is not { } chord)
        {
            _proposed = null;
            _refusal = Loc.T("keys.ui.unsupported_key");
            RefreshRow(_editId);
            return;
        }
        // Return confirms a proposal that needs no decision, as a dialog's default button does.
        if (_proposed is { } p && p != chord && chord == Shortcut.None("Return") && _refusal is null
            && Bindings.FindConflicts(_editId, _editSlot, p).Count == 0)
        {
            var id = _editId; var slot = _editSlot;
            EndEdit();
            Bindings.Set(id, slot, p);
            return;
        }
        Propose(chord);
    }
}
