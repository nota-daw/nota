// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Pairing for Nota Remote. The server listens on the local network, so nothing is accepted
// without either the current four-digit code (shown in Nota, inside the QR) or a device token
// handed out by an earlier pairing. Codes rotate every two minutes and after every pairing;
// wrong guesses lock the sender out for a while, so 10 000 codes can't be walked. Tokens are
// stored hashed, with the device's name and when it was last seen — Settings → Remote lists
// them, and forgetting one makes that phone scan again.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Nota.Remote;

/// <summary>A phone that paired once and may reconnect without a code.</summary>
public sealed class TrustedDevice
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>SHA-256 of the device token, hex. The token itself only lives on the phone.</summary>
    public string TokenHash { get; set; } = "";
    public DateTime Paired { get; set; }
    public DateTime LastSeen { get; set; }
}

public sealed class RemotePairing
{
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(2);
    private const int MaxFailures = 5;
    private static readonly TimeSpan Lockout = TimeSpan.FromSeconds(30);

    private readonly object _gate = new();
    private readonly string? _storePath;
    private readonly List<TrustedDevice> _trusted = new();
    private readonly Dictionary<string, (int fails, DateTime until)> _failures = new();
    private string _code = "";
    private DateTime _codeIssued;

    /// <summary>The trusted list or the code changed (Settings and the popup redraw).</summary>
    public event Action? Changed;

    /// <param name="storePath">trusted.json; null keeps the list in memory (tests).</param>
    public RemotePairing(string? storePath)
    {
        _storePath = storePath;
        Load();
        NewCode();
    }

    /// <summary>The current code, rotated when it has expired.</summary>
    public string Code
    {
        get
        {
            lock (_gate)
            {
                if (DateTime.UtcNow - _codeIssued >= CodeLifetime) NewCodeLocked();
                return _code;
            }
        }
    }

    /// <summary>How long the current code has left.</summary>
    public TimeSpan CodeRemaining
    {
        get
        {
            lock (_gate)
            {
                var left = CodeLifetime - (DateTime.UtcNow - _codeIssued);
                return left < TimeSpan.Zero ? TimeSpan.Zero : left;
            }
        }
    }

    public void NewCode()
    {
        lock (_gate) NewCodeLocked();
        Changed?.Invoke();
    }

    private void NewCodeLocked()
    {
        string prev = _code;
        do _code = RandomNumberGenerator.GetInt32(0, 10000).ToString("D4");
        while (_code == prev);
        _codeIssued = DateTime.UtcNow;
    }

    public IReadOnlyList<TrustedDevice> Trusted
    {
        get { lock (_gate) return _trusted.Select(Clone).ToList(); }
    }

    /// <summary>Pair a new phone with the code. Returns its device id and token, or null with
    /// <paramref name="error"/> = "code" (wrong / expired) or "locked" (too many tries).</summary>
    public (TrustedDevice Device, string Token)? Pair(string code, string name, string sender, out string? error)
    {
        TrustedDevice dev;
        string token;
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            _failures.TryGetValue(sender, out var f);
            if (f.until > now) { error = "locked"; return null; }
            if (now - _codeIssued >= CodeLifetime) NewCodeLocked();
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(code ?? ""), Encoding.ASCII.GetBytes(_code)))
            {
                int fails = f.fails + 1;
                bool locked = fails >= MaxFailures;
                _failures[sender] = locked ? (0, now + Lockout) : (fails, DateTime.MinValue);
                error = locked ? "locked" : "code";
                return null;
            }
            _failures.Remove(sender);
            token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            dev = new TrustedDevice
            {
                Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant(),
                Name = CleanName(name),
                TokenHash = Hash(token),
                Paired = now,
                LastSeen = now,
            };
            _trusted.Add(dev);
            NewCodeLocked();   // a code is good for one phone
            SaveLocked();
        }
        error = null;
        Changed?.Invoke();
        return (Clone(dev), token);
    }

    /// <summary>The trusted device a token belongs to, or null (never paired, or forgotten).</summary>
    public TrustedDevice? Authenticate(string token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        string h = Hash(token);
        lock (_gate)
        {
            var d = _trusted.FirstOrDefault(x => CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(x.TokenHash), Encoding.ASCII.GetBytes(h)));
            if (d is null) return null;
            d.LastSeen = DateTime.UtcNow;
            SaveLocked();
            return Clone(d);
        }
    }

    public void Rename(string id, string name)
    {
        lock (_gate)
        {
            var d = _trusted.FirstOrDefault(x => x.Id == id);
            if (d is null) return;
            d.Name = CleanName(name);
            SaveLocked();
        }
        Changed?.Invoke();
    }

    public void Touch(string id)
    {
        lock (_gate)
        {
            var d = _trusted.FirstOrDefault(x => x.Id == id);
            if (d is null) return;
            d.LastSeen = DateTime.UtcNow;
            SaveLocked();
        }
    }

    /// <summary>Forget a phone: its token stops working and it has to scan a code again.</summary>
    public bool Forget(string id)
    {
        bool removed;
        lock (_gate)
        {
            removed = _trusted.RemoveAll(x => x.Id == id) > 0;
            if (removed) SaveLocked();
        }
        if (removed) Changed?.Invoke();
        return removed;
    }

    public static string CleanName(string? name)
    {
        var s = new string((name ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (s.Length > 40) s = s[..40];
        return s.Length == 0 ? "Phone" : s;
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static TrustedDevice Clone(TrustedDevice d) => new()
    {
        Id = d.Id, Name = d.Name, TokenHash = d.TokenHash, Paired = d.Paired, LastSeen = d.LastSeen,
    };

    private void Load()
    {
        if (_storePath is null || !File.Exists(_storePath)) return;
        try
        {
            var list = JsonSerializer.Deserialize<List<TrustedDevice>>(File.ReadAllText(_storePath));
            if (list is not null) _trusted.AddRange(list.Where(d => d.Id.Length > 0 && d.TokenHash.Length > 0));
        }
        catch { /* an unreadable list means every phone pairs again — never block start-up */ }
    }

    private void SaveLocked()
    {
        if (_storePath is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);
            string tmp = _storePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_trusted, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, _storePath, overwrite: true);
        }
        catch { /* best effort: the phone keeps working this session */ }
    }
}
