// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Command palette (CP-13.2/13.3): descriptors for presets. A preset inherits its device's
// role and character; its folder says what part it plays or what it is for (Pads → pad,
// Vocals → vocals, Mastering → master), and vocabulary words in its name add the rest
// ("Warm Strings" → warm, strings). Where neither folder nor name names a part, the device's
// own sound / source stands in — so every factory preset has a sound or source and at least
// one character (the coverage test holds that).

namespace Nota.Application.Palette;

public static class PresetDescriptors
{
    // Folder names whose meaning isn't spelled by vocabulary words. Keys are normalised.
    private static readonly Dictionary<string, SemanticDescriptor> Folders = new(StringComparer.Ordinal)
    {
        ["mix & bus"] = new() { Source = ["bus"], Task = ["glue"] },
        ["sub"] = new() { Sound = ["bass"], Character = ["deep"] },
        ["analog"] = new() { Character = ["vintage", "warm"] },
        ["acid"] = new() { Sound = ["bass"], Character = ["gritty"], Genre = ["acid", "techno"] },
        ["growl & wobble"] = new() { Sound = ["bass"], Character = ["aggressive"], Genre = ["dubstep"] },
        ["electric"] = new() { Sound = ["bass"], Character = ["punchy"] },
        ["sequences"] = new() { Sound = ["seq"] },
        ["reactive"] = new() { Character = ["evolving"] },
        ["winds & bowed"] = new() { Sound = ["strings", "brass"] },
        ["basics"] = new() { Character = ["clean"] },
        ["halls"] = new() { Character = ["lush", "wide"] },
        ["rooms"] = new() { Character = ["clean"], Source = ["drums", "vocals"] },
        ["plates & springs"] = new() { Character = ["bright", "vintage"], Source = ["vocals"] },
        ["plates"] = new() { Character = ["bright"], Source = ["vocals"] },
        ["chambers"] = new() { Character = ["warm"] },
        ["creative"] = new() { Character = ["evolving"] },
        ["sends"] = new() { Source = ["bus"] },
        ["short & classic"] = new() { Character = ["vintage"] },
        ["tempo-synced"] = new() { Character = ["clean"] },
        ["tape & character"] = new() { Character = ["vintage", "warm"] },
        ["stereo width"] = new() { Character = ["wide"], Task = ["widen"] },
        ["bass mono"] = new() { Source = ["bass"] },
        ["routing"] = new() { Source = ["bus"], Character = ["clean"] },
        ["gain & loudness"] = new() { Task = ["loudness"], Character = ["clean"] },
        ["clean"] = new() { Character = ["clean"], Source = ["guitar"] },
        ["crunch & blues"] = new() { Character = ["gritty"], Source = ["guitar"] },
        ["rock"] = new() { Character = ["aggressive"], Source = ["guitar"], Genre = ["rock"] },
        ["lead"] = new() { Sound = ["lead"], Source = ["guitar"] },
        ["heavy"] = new() { Character = ["aggressive"], Source = ["guitar"] },
        ["beyond guitar"] = new() { Source = ["synth", "keys"] },
        ["sweeps & tone"] = new() { Character = ["evolving"] },
        ["envelope"] = new() { Character = ["punchy"] },
        ["lfo motion"] = new() { Character = ["evolving"], Task = ["modulation"] },
        ["vinyl"] = new() { Character = ["lofi", "vintage"] },
        ["vhs"] = new() { Character = ["lofi", "vintage"] },
        ["tube"] = new() { Character = ["warm"] },
        ["special"] = new() { Character = ["evolving"] },
        ["pan"] = new() { Character = ["wide"] },
        ["tempo-synced pan"] = new() { Character = ["wide"] },
        ["random"] = new() { Character = ["evolving"] },
        ["vocal tuning"] = new() { Source = ["vocals"], Task = ["tuning"] },
        ["voice types"] = new() { Source = ["vocals"] },
        ["stutters"] = new() { Character = ["aggressive"] },
        ["drums & vocals"] = new() { Source = ["drums", "vocals"] },
        ["retro machines"] = new() { Character = ["lofi", "vintage"] },
        ["subtle texture"] = new() { Character = ["subtle"] },
        ["destroy"] = new() { Character = ["aggressive", "gritty"] },
        ["fold"] = new() { Character = ["gritty"] },
        ["expansion"] = new() { Character = ["punchy"], Task = ["transient"] },
        ["live & fx"] = new() { Character = ["aggressive"] },
        ["spectrum"] = new() { Source = ["master"] },
        ["scope"] = new() { Source = ["master"] },
        ["waterfall"] = new() { Source = ["master"] },
        ["instruments"] = new() { Source = ["synth", "keys"] },
        ["bass & instruments"] = new() { Source = ["bass", "synth"] },
        ["drums & bass"] = new() { Source = ["drums", "bass"] },
        ["rhythmic"] = new() { Character = ["punchy"], Task = ["modulation"] },
        ["utility"] = new() { Character = ["clean"] },
        ["lo-fi & character"] = new() { Character = ["lofi"] },
        ["extended & jazz"] = new() { Genre = ["jazz"], Character = ["lush"] },
        ["chaos"] = new() { Character = ["aggressive", "evolving"] },
        ["ambient & generative"] = new() { Genre = ["ambient"], Character = ["evolving"] },
        ["drones & atmospheres"] = new() { Sound = ["drone", "texture"] },
        ["deep & resonant"] = new() { Character = ["deep"] },
        ["voices & choirs"] = new() { Sound = ["choir"] },
        ["humanize"] = new() { Character = ["subtle"] },
        ["ratchets & rolls"] = new() { Source = ["drums"] },
        ["melodic"] = new() { Sound = ["seq"] },
        ["tine"] = new() { Sound = ["keys"], Character = ["bright"] },
        ["suitcase"] = new() { Sound = ["keys"], Character = ["warm", "wide"] },
        ["reed"] = new() { Sound = ["keys"], Character = ["gritty", "vintage"] },
        ["clav"] = new() { Sound = ["keys"], Character = ["punchy"], Genre = ["funk"] },
        ["character"] = new() { Character = ["lofi", "vintage"] },
    };

    /// <summary>A factory or user preset's descriptor (CP-13.2/13.3): the device's role,
    /// character and aliases, then whatever the folder and name say. When those name no part
    /// (sound) or use (source), the device's own stand in. <paramref name="userTags"/> are the
    /// user's tag titles; a vocabulary word among them counts like a word in the name.</summary>
    public static SemanticDescriptor For(SemanticDescriptor device, string folder, string name, IEnumerable<string>? userTags = null)
    {
        var own = new SemanticDescriptor { Role = device.Role, Description = device.Description };
        if (folder.Length > 0 && Folders.TryGetValue(TextNorm.Normalize(folder), out var f)) own = own.Merge(f);
        var texts = new List<string> { folder, name };
        if (userTags is not null) texts.AddRange(userTags);
        own = own.WithTokensFrom(texts.ToArray());
        // The device's character is the preset's too; its part and use only when the preset
        // names none (a Volt bass preset is not also a pad).
        own = own with
        {
            Character = own.Character.Union(device.Character).ToArray(),
            Aliases = [],   // the device's slang finds the device, not each of its presets
            Genre = own.Genre.Length > 0 ? own.Genre : device.Genre,
        };
        if (own.Sound.Length == 0 && own.Source.Length == 0)
            own = own with { Sound = device.Sound, Source = device.Source };
        if (own.Character.Length == 0) own = own with { Character = [DefaultCharacter(device)] };
        return own;
    }

    /// <summary>Only what the preset itself says — folder and name — so a search can prefer a
    /// preset that names the asked-for term over one that merely inherits it.</summary>
    public static SemanticDescriptor Own(string folder, string name)
    {
        var own = new SemanticDescriptor();
        if (folder.Length > 0 && Folders.TryGetValue(TextNorm.Normalize(folder), out var f)) own = own.Merge(f);
        return own.WithTokensFrom(folder, name);
    }

    // A device that says nothing about its character (a utility, a MIDI tool) reads as clean.
    private static string DefaultCharacter(SemanticDescriptor device) => device.Role switch
    {
        "random" or "arp" => "evolving",
        "rack" or "chord" => "lush",
        _ => "clean",
    };
}
