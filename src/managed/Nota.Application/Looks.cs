// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The look (spec 091, #572): the new look of the style guide (spec 038), or the classic one —
// 0.59's colours, corners and Geist Mono for values. One user-wide choice worn over any theme,
// stored like the accent. Pure: no UI, so the smoke test walks Resolve and Compose. Compose is
// the one rule for a skin under either look — the skin wins wherever it sets something, the
// look fills in what it leaves open (research R3).

using Nota.Application.Themes;

namespace Nota.Application;

public enum Look { New, Classic }

/// <summary>Arrangement clips: a tint of the track colour (the new look) or a solid block of it
/// with a title band (0.59, the classic look's default).</summary>
public enum ClipStyle { Tinted, Solid }

public static class Looks
{
    public const string NewId = "new", ClassicId = "classic";

    /// <summary>What settings.json says, or New: an empty or unknown value falls back without
    /// throwing, and nothing is written.</summary>
    public static Look Resolve(Settings s) => Parse(s.Look) ?? Look.New;

    /// <summary>Write a choice (empty for New, so a settings file without the key means New).</summary>
    public static void Store(Settings s, Look look) => s.Look = look == Look.Classic ? ClassicId : "";

    /// <summary>The id MCP and settings use.</summary>
    public static string Id(Look look) => look == Look.Classic ? ClassicId : NewId;

    /// <summary>An id back to a look; "" reads as New, anything unknown as null.</summary>
    public static Look? Parse(string? id) => id switch
    {
        null or "" or NewId => Look.New,
        ClassicId => Look.Classic,
        _ => null,
    };

    public const string SolidId = "solid", TintedId = "tinted";

    /// <summary>The clip style in effect: the user's pick, or the look's default (Classic → Solid).</summary>
    public static ClipStyle ClipStyleOf(Settings s) => ClipStyleOf(s, Resolve(s));

    /// <summary>The same in a given look (a preview in a look the user is not wearing).</summary>
    public static ClipStyle ClipStyleOf(Settings s, Look look) => s.ClipStyle switch
    {
        SolidId => ClipStyle.Solid,
        TintedId => ClipStyle.Tinted,
        _ => look == Look.Classic ? ClipStyle.Solid : ClipStyle.Tinted,
    };

    /// <summary>Store a pick; it wins over the look from then on.</summary>
    public static void StoreClipStyle(Settings s, ClipStyle style) => s.ClipStyle = style == ClipStyle.Solid ? SolidId : TintedId;

    /// <summary>0.59's corner scale (3 for controls and cards, 2 for keys, rows and badges) laid
    /// on today's steps. Modal windows and clips share a step with cards and fields now, so they
    /// take 3; radii off the scale shrink by 0.4, near 3/9, and pills stay round (research R4).</summary>
    public static readonly SkinGeometry ClassicGeometry = new(null, 0.4, new Dictionary<RadiusStep, double>
    {
        [RadiusStep.Card] = 3,
        [RadiusStep.Group] = 3,
        [RadiusStep.Field] = 3,
        [RadiusStep.Cell] = 2,
        [RadiusStep.ValueBar] = 2,
        [RadiusStep.Switch] = 2,
        [RadiusStep.Fine] = 2,
    }, null, true);

    /// <summary>The typeface and radii worn for <paramref name="skin"/> under <paramref name="look"/>:
    /// the skin's typefaces as they are (null = the look's own); the skin's radii when its file set
    /// any, else the classic scale under Classic and the design scale under New.</summary>
    public static SkinGeometry Compose(SkinGeometry skin, Look look)
    {
        if (skin.HasRadius || look == Look.New) return skin;
        return new SkinGeometry(skin.FontFamily, ClassicGeometry.Roundness, ClassicGeometry.StepOverrides,
            skin.ValuesFamily, true);
    }
}
