// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Command palette (CP-24): the providers that fill the index — one per kind of item — and the
// mapping from a palette context to an insertion target (CP-3). Providers read the browser's
// library and the engine; they build plain PaletteItems and may run on a worker thread.

using Nota.Application;
using Nota.Application.Palette;

namespace Nota.Presentation;

/// <summary>Every registered command that is bound and meant for the palette.</summary>
public sealed class ActionPaletteProvider(ICommandRegistry registry) : IPaletteProvider
{
    public string Source => "actions";

    public IReadOnlyList<PaletteItem> Build() => registry.All
        .Where(c => c.IsBound && !c.HideInPalette)
        .Select(c => new PaletteItem
        {
            Kind = PaletteKind.Action, Id = "a:" + c.Id, Name = c.Title, Sub = c.Category, Gesture = c.Gesture,
            View = c.View, Aliases = c.Aliases, Descriptor = c.Descriptor, Payload = c.Id,
        }).ToList();
}

/// <summary>Every track — instrument, audio, group, return — and the master.</summary>
public sealed class TrackPaletteProvider(IAudioEngine engine) : IPaletteProvider
{
    public string Source => "tracks";

    public IReadOnlyList<PaletteItem> Build()
    {
        var list = new List<PaletteItem>();
        for (int i = 0; i < engine.TrackCount; i++)
        {
            if (!engine.TryGetTrackInfo(i, out var ti)) continue;
            string name = engine.GetTrackName(ti.Id);
            if (string.IsNullOrWhiteSpace(name)) name = $"Track {ti.Id}";
            string type, alias;
            if (ti.IsGroup) { type = "Group"; alias = "group folder"; }
            else if (ti.IsReturn) { type = "Return"; alias = "return send bus aux"; }
            else if (ti.IsInstrument)
            {
                string inst = engine.DeviceName(ti.Id, -1);
                type = inst.Length > 0 ? "MIDI · " + inst : "MIDI";
                alias = "midi instrument";
            }
            else { type = "Audio"; alias = "audio"; }
            list.Add(new PaletteItem
            {
                Kind = PaletteKind.Track, Id = "t:" + ti.Id, Name = name, Sub = type, Tag = "Track",
                Aliases = alias.Split(' '), Payload = ti.Id,
            });
        }
        if (engine.MasterTrackId > 0)
            list.Add(new PaletteItem
            {
                Kind = PaletteKind.Track, Id = "t:" + engine.MasterTrackId, Name = "Master", Sub = "Master", Tag = "Track",
                Aliases = ["master", "main", "output"], Payload = engine.MasterTrackId,
            });
        return list;
    }
}

/// <summary>Built-in devices and scanned plug-ins, with their descriptors (CP-13.1, CP-13.4).</summary>
public sealed class DevicePaletteProvider(BrowserViewModel browser) : IPaletteProvider
{
    public string Source => "devices";

    public IReadOnlyList<PaletteItem> Build()
    {
        var list = new List<PaletteItem>();
        foreach (var tree in new[] { browser.InstrumentTree, browser.EffectTree, browser.MidiTree })
            foreach (var d in tree) list.Add(Item(d));
        return list;
    }

    internal PaletteItem Item(BrowserItem d)
    {
        var role = RoleOf(d.Kind);
        SemanticDescriptor desc;
        string sub, device = "", folder;
        if (d.IsPlugin)
        {
            desc = PluginDescriptors.For(d.Name, d.Vendor, d.Kind == BrowserItemKind.PluginInstrument, d.Category);
            sub = string.Join(" · ", new[] { d.Vendor, d.Format }.Where(s => s.Length > 0));
            device = d.Vendor;
            folder = d.Category.Replace('|', ' ').Replace('/', ' ');
        }
        else
        {
            var b = BuiltinDeviceCatalog.All.FirstOrDefault(x => x.LibraryKey == d.LibraryKey);
            desc = b?.Descriptor ?? SemanticDescriptor.Empty;
            sub = $"{TypeLabel(role)} · {d.Sub}";
            folder = TypeLabel(role);
        }
        return new PaletteItem
        {
            Kind = PaletteKind.Device, Id = "d:" + d.LibraryKey, Name = d.Name, Sub = sub,
            Tag = d.IsPlugin ? "Plug-in" : TypeLabel(role), Role = role, IsPlugin = d.IsPlugin,
            IsFavorite = browser.IsFavorite(d), Descriptor = desc, Device = device, Folder = folder,
            UserTags = browser.TagTitlesFor(d).ToArray(), Payload = d, CanPreview = true,
        };
    }

    internal static PaletteDeviceRole RoleOf(BrowserItemKind k) => k switch
    {
        BrowserItemKind.BuiltinInstrument or BrowserItemKind.PluginInstrument => PaletteDeviceRole.Instrument,
        BrowserItemKind.BuiltinMidiEffect => PaletteDeviceRole.MidiEffect,
        _ => PaletteDeviceRole.AudioEffect,
    };

    internal static string TypeLabel(PaletteDeviceRole r) => r switch
    {
        PaletteDeviceRole.Instrument => "Instrument",
        PaletteDeviceRole.MidiEffect => "MIDI FX",
        _ => "Audio FX",
    };
}

/// <summary>Factory presets and kits (under their devices) and the user's saved presets.</summary>
public sealed class PresetPaletteProvider(BrowserViewModel browser) : IPaletteProvider
{
    public string Source => "presets";

    public IReadOnlyList<PaletteItem> Build()
    {
        var list = new List<PaletteItem>();
        foreach (var tree in new[] { browser.InstrumentTree, browser.EffectTree, browser.MidiTree })
            foreach (var dev in tree)
            {
                if (dev.IsPlugin) continue;
                var role = DevicePaletteProvider.RoleOf(dev.Kind);
                var desc = BuiltinDeviceCatalog.All.FirstOrDefault(x => x.LibraryKey == dev.LibraryKey)?.Descriptor ?? SemanticDescriptor.Empty;
                foreach (var c in dev.Children)
                {
                    if (c.Kind == BrowserItemKind.Folder) foreach (var p in c.Children) list.Add(Item(p, dev.Name, c.Name, role, desc));
                    else if (c.Kind == BrowserItemKind.Preset) list.Add(Item(c, dev.Name, DeviceInsertService.IsKit(c) ? "Kits" : "", role, desc));
                }
            }
        // User presets: category folder → device folder → preset.
        foreach (var cat in browser.UserPresetTree)
            foreach (var devNode in cat.Children)
                foreach (var p in devNode.Children)
                {
                    if (p.Kind != BrowserItemKind.Preset) continue;
                    var role = cat.Name switch
                    {
                        "Instruments" => PaletteDeviceRole.Instrument,
                        "MIDI Effects" => PaletteDeviceRole.MidiEffect,
                        _ => PaletteDeviceRole.AudioEffect,
                    };
                    var b = BuiltinDeviceCatalog.All.FirstOrDefault(x => x.Name == devNode.Name);
                    var desc = b?.Descriptor ?? PluginDescriptors.For(devNode.Name, "", role == PaletteDeviceRole.Instrument);
                    list.Add(Item(p, devNode.Name, "", role, desc, user: true));
                }
        return list;
    }

    private static PaletteItem Item(BrowserItem p, string device, string folder, PaletteDeviceRole role, SemanticDescriptor deviceDesc, bool user = false)
    {
        bool kit = DeviceInsertService.IsKit(p);
        var origin = user ? PresetOrigin.User : kit ? PresetOrigin.Kit : PresetOrigin.Factory;
        string sub = folder.Length > 0 ? $"{device} · {folder}" : device;
        if (user) sub += " · user";
        return new PaletteItem
        {
            Kind = PaletteKind.Preset, Id = "p:" + p.Path, Name = p.Name, Sub = sub, Tag = "Preset", Role = role,
            PresetOrigin = origin, Descriptor = PresetDescriptors.For(deviceDesc, folder, p.Name),
            OwnDescriptor = PresetDescriptors.Own(folder, p.Name),
            Device = device, Folder = folder, Payload = p, CanPreview = !kit && !user,
        };
    }
}

/// <summary>Nodes that exist only in Modular (CP-6a.5).</summary>
public sealed class ModularPaletteProvider : IPaletteProvider
{
    public string Source => "modular";

    public IReadOnlyList<PaletteItem> Build() => ModularCatalog.All.Select(n => new PaletteItem
    {
        Kind = PaletteKind.Modular, Id = "m:" + n.Kind, Name = n.Name, Sub = "Modular · " + n.Sub, Tag = "Modular",
        Role = PaletteDeviceRole.Modular, Descriptor = n.Descriptor, Payload = n.Kind,
    }).ToList();
}

public static class PaletteInsert
{
    /// <summary>Where the palette puts a device or preset (CP-3): in Devices next to the selected
    /// card (⌥ replaces it), elsewhere at the end of its section; ⌘ asks for a new track.</summary>
    public static InsertTarget TargetFor(PaletteContext ctx, PaletteItem item, ApplyMode mode)
    {
        bool newTrack = (mode & ApplyMode.NewTrack) != 0 && item.Role == PaletteDeviceRole.Instrument;
        var t = new InsertTarget(ctx.TrackId, NewTrack: newTrack, InPlace: true);
        if (ctx.Origin != PaletteOrigin.Devices || item.Role is PaletteDeviceRole.Instrument or PaletteDeviceRole.None) return t;
        bool midi = item.Role == PaletteDeviceRole.MidiEffect;
        bool replace = (mode & ApplyMode.Replace) != 0;
        var ins = ctx.Insert;
        if (ins.Index >= 0 && ins.Section == (midi ? ChainSection.Midi : ChainSection.Audio))
            return t with { Placement = replace ? InsertPlacement.Replace : InsertPlacement.Insert, Index = replace ? ins.Index : ins.Index + 1, Midi = midi };
        if (!midi && ins.Section == ChainSection.Instrument)
            return t with { Placement = InsertPlacement.Insert, Index = 0, Midi = false };   // right after the instrument
        return t;
    }
}
