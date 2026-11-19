// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Which profile fits a connected controller (specs/051 research R5). The MIDI Identity Reply
// survives renamed ports (Windows appends indices, ALSA names differ), so it beats the port
// name; port names are the fallback for controllers that never answer. A device with several
// ports answers the same identity on each (or on one only), so a profile that names both an
// identity and port names needs both to match. User profiles beat shipped ones, a more specific
// identity beats a vaguer one, then file-name order decides.

namespace Nota.Application.Controllers;

/// <summary>The fields of a Universal Identity Reply (F0 7E id 06 02 mfr family model version F7).</summary>
public sealed record IdentityReply(byte[] Manufacturer, byte[] Family, byte[] Model, byte[] Version)
{
    /// <summary>The Universal Identity Request, to any device id.</summary>
    public static readonly byte[] Request = { 0xF0, 0x7E, 0x7F, 0x06, 0x01, 0xF7 };

    public static IdentityReply? TryParse(ReadOnlySpan<byte> m)
    {
        if (m.Length < 5 || m[0] != 0xF0 || m[1] != 0x7E || m[3] != 0x06 || m[4] != 0x02 || m[^1] != 0xF7) return null;
        int i = 5;
        int mfrLen = (i < m.Length && m[i] == 0x00) ? 3 : 1;   // 00 xx xx = extended manufacturer id
        if (m.Length != i + mfrLen + 2 + 2 + 4 + 1) return null;
        var mfr = m.Slice(i, mfrLen).ToArray(); i += mfrLen;
        var family = m.Slice(i, 2).ToArray(); i += 2;
        var model = m.Slice(i, 2).ToArray(); i += 2;
        var version = m.Slice(i, 4).ToArray();
        return new IdentityReply(mfr, family, model, version);
    }
}

public static class ProfileMatcher
{
    public static ControllerProfile? Match(IReadOnlyList<ControllerProfile> profiles, string portName, IdentityReply? identity)
    {
        ControllerProfile? best = null;
        (int origin, int how, int specificity) bestScore = (-1, -1, -1);
        foreach (var p in profiles.OrderBy(p => p.FileName, StringComparer.Ordinal))
        {
            int how = -1, specificity = 0;
            bool nameOk = p.PortNames.Any(r => SafeIsMatch(r, portName));
            if (identity is not null && p.Identity is { } id && Matches(id, identity) && (p.PortNames.Count == 0 || nameOk))
            {
                how = 1;
                specificity = id.Specificity;
            }
            else if (nameOk)
                how = 0;
            if (how < 0) continue;
            var score = (p.Origin == ProfileOrigin.User ? 1 : 0, how, specificity);
            if (score.CompareTo(bestScore) > 0) { best = p; bestScore = score; }
        }
        return best;
    }

    private static bool Matches(IdentityMatch m, IdentityReply r)
        => m.Manufacturer.AsSpan().SequenceEqual(r.Manufacturer)
           && (m.Family is null || m.Family.AsSpan().SequenceEqual(r.Family))
           && (m.Model is null || m.Model.AsSpan().SequenceEqual(r.Model));

    private static bool SafeIsMatch(System.Text.RegularExpressions.Regex r, string s)
    {
        try { return r.IsMatch(s); }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { return false; }
    }
}
