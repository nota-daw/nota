// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Command palette (CP-24): the one registry of user commands. The menu, the keyboard and the
// palette all run a command by its id, so its logic lives in one place. What a command IS
// (id, title, category, shortcut, aliases) is declared in CommandCatalog; what it DOES is bound
// by the app layer, which owns the views (MainWindow.Commands.cs).

namespace Nota.Application.Palette;

/// <summary>A registered command (CP-24).</summary>
public sealed class PaletteCommand
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    /// <summary>The menu / group it belongs to: File, Edit, View, Transport, Track, Session,
    /// Piano roll, Settings, Help.</summary>
    public required string Category { get; init; }
    public string[] Aliases { get; init; } = [];
    /// <summary>The shortcut in glyph notation — "⌘⇧P", "⌥⌘S", "Space", "F2" — or "".</summary>
    public string Gesture { get; init; } = "";
    /// <summary>The main view this command acts in ("Arrangement", "Session", "Modular"), or "".</summary>
    public string View { get; init; } = "";
    /// <summary>Hidden from the palette (the palette's own toggle).</summary>
    public bool HideInPalette { get; init; }
    public SemanticDescriptor? Descriptor { get; init; }
    public Func<PaletteContext, Availability> CanExecute { get; set; } = _ => Availability.Yes;
    public Action<PaletteContext, ApplyMode>? Execute { get; set; }
    public bool IsBound => Execute is not null;
}

public interface ICommandRegistry
{
    IReadOnlyCollection<PaletteCommand> All { get; }
    PaletteCommand? Get(string id);
    /// <summary>Binds a catalog command to its behaviour. Throws for an id the catalog lacks,
    /// so a typo can't silently create a command nobody can reach.</summary>
    void Bind(string id, Action<PaletteContext, ApplyMode> execute, Func<PaletteContext, Availability>? canExecute = null);
    /// <summary>Runs a bound command. The menu and the keyboard run it whatever its
    /// availability (the handler explains in the status line why nothing happened); the palette
    /// passes <paramref name="onlyIfAvailable"/>. False when nothing ran.</summary>
    bool Run(string id, PaletteContext? context = null, ApplyMode mode = ApplyMode.Default, bool onlyIfAvailable = false);
    /// <summary>Raised after a command ran (the palette's frecency listens).</summary>
    event Action<string>? Ran;
}

public sealed class CommandRegistry : ICommandRegistry
{
    private readonly Dictionary<string, PaletteCommand> _byId = new(StringComparer.Ordinal);

    public CommandRegistry() : this(CommandCatalog.Create()) { }

    public CommandRegistry(IEnumerable<PaletteCommand> commands)
    {
        foreach (var c in commands) _byId[c.Id] = c;
    }

    public IReadOnlyCollection<PaletteCommand> All => _byId.Values;

    public PaletteCommand? Get(string id) => _byId.GetValueOrDefault(id);

    public event Action<string>? Ran;

    /// <summary>Supplies the context a command run from the menu or the keyboard sees.</summary>
    public Func<PaletteContext>? ContextProvider { get; set; }

    public void Bind(string id, Action<PaletteContext, ApplyMode> execute, Func<PaletteContext, Availability>? canExecute = null)
    {
        if (!_byId.TryGetValue(id, out var c)) throw new ArgumentException($"Unknown command id '{id}' — declare it in CommandCatalog.", nameof(id));
        c.Execute = execute;
        if (canExecute is not null) c.CanExecute = canExecute;
    }

    public bool Run(string id, PaletteContext? context = null, ApplyMode mode = ApplyMode.Default, bool onlyIfAvailable = false)
    {
        if (!_byId.TryGetValue(id, out var c) || c.Execute is null) return false;
        var ctx = context ?? ContextProvider?.Invoke() ?? new PaletteContext();
        if (onlyIfAvailable && !c.CanExecute(ctx).Ok) return false;
        c.Execute(ctx, mode);
        Ran?.Invoke(id);
        return true;
    }
}
