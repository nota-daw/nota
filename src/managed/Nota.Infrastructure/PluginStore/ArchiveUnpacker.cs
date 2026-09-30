// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Expands a downloaded plugin release asset. Only ever *unpacks*: installer scripts
// never run (a .pkg is expanded with `pkgutil --expand-full`, which extracts payloads
// without executing anything). Mirrors unpack() in the registry's scripts/registry.py.
//   zip  -> ditto on macOS (keeps bundle symlinks + permissions), else managed
//   tar  -> the system tar (bsdtar on macOS/Windows, GNU tar on Linux: gz/xz/bz2)
//   dmg  -> hdiutil attach (read-only, hidden) + copy + detach          (macOS)
//   pkg  -> pkgutil --expand-full                                         (macOS)
//   deb  -> ar parsed here, then data.tar.* through the system tar

using System.Diagnostics;
using System.IO.Compression;

namespace Nota.Infrastructure;

public static class ArchiveUnpacker
{
    /// <summary>Archive kind from a file name / URL, or null.</summary>
    public static string? KindOf(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.EndsWith(".zip")) return "zip";
        if (n.EndsWith(".tar") || n.EndsWith(".tar.gz") || n.EndsWith(".tgz") || n.EndsWith(".tar.xz")
            || n.EndsWith(".txz") || n.EndsWith(".tar.bz2")) return "tar";
        if (n.EndsWith(".dmg")) return "dmg";
        if (n.EndsWith(".pkg")) return "pkg";
        if (n.EndsWith(".deb")) return "deb";
        return null;
    }

    /// <summary>Expands <paramref name="archive"/> into <paramref name="dest"/> (must not exist yet).</summary>
    public static void Unpack(string archive, string kind, string dest, CancellationToken ct = default)
    {
        switch (kind)
        {
            case "zip":
                if (OperatingSystem.IsMacOS() && !ScanZip(archive))
                    Run("ditto", ["-x", "-k", archive, dest], ct);
                else
                    Unzip(archive, dest);
                break;
            case "tar":
                Directory.CreateDirectory(dest);
                Run("tar", ["-xf", archive, "-C", dest], ct);
                break;
            case "dmg":
                RequireMac(kind);
                UnpackDmg(archive, dest, ct);
                break;
            case "pkg":
                RequireMac(kind);
                Run("pkgutil", ["--expand-full", archive, dest], ct);
                break;
            case "deb":
                UnpackDeb(archive, dest, ct);
                break;
            default:
                throw new PluginStoreException($"Unsupported archive type \"{kind}\".");
        }
    }

    private static void RequireMac(string kind)
    {
        if (!OperatingSystem.IsMacOS()) throw new PluginStoreException($"A .{kind} archive can only be unpacked on macOS.");
    }

    // Refuses entries that would land outside the destination (absolute, drive-rooted or
    // climbing ".."), whichever extractor runs. Returns whether any name uses backslashes:
    // some Windows-built zips store "a\b\c", which only the managed path treats as folders.
    private static bool ScanZip(string archive)
    {
        using var z = ZipFile.OpenRead(archive);
        bool backslashes = false;
        foreach (var e in z.Entries)
        {
            var rel = e.FullName.Replace('\\', '/');
            if (rel.StartsWith('/') || rel.Contains(':') || rel.Split('/').Contains(".."))
                throw new PluginStoreException($"The archive contains an unsafe path: {e.FullName}");
            backslashes |= e.FullName.Contains('\\');
        }
        return backslashes;
    }

    private static void Unzip(string archive, string dest)
    {
        ScanZip(archive);
        var root = Path.GetFullPath(dest) + Path.DirectorySeparatorChar;
        using var z = ZipFile.OpenRead(archive);
        foreach (var e in z.Entries)
        {
            var rel = e.FullName.Replace('\\', '/');
            var target = Path.GetFullPath(Path.Combine(dest, rel));
            if (!target.StartsWith(root, StringComparison.Ordinal) && target + Path.DirectorySeparatorChar != root)
                throw new PluginStoreException($"The archive contains an unsafe path: {e.FullName}");
            if (rel.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            e.ExtractToFile(target, overwrite: true);
            if (!OperatingSystem.IsWindows() && ((e.ExternalAttributes >> 16) & 0b001_001_001) != 0)
                File.SetUnixFileMode(target, (UnixFileMode)0b111_101_101);   // 0755: keep executables executable
        }
    }

    private static void UnpackDmg(string archive, string dest, CancellationToken ct)
    {
        var mount = Directory.CreateTempSubdirectory("nota-dmg-").FullName;
        // "Y" accepts a license agreement if the image carries one (else attach waits on stdin).
        Run("hdiutil", ["attach", "-nobrowse", "-readonly", "-noautoopen", "-mountpoint", mount, archive], ct, stdin: "Y\n");
        try
        {
            Run("ditto", [mount, dest], ct);
        }
        finally
        {
            try { Run("hdiutil", ["detach", mount, "-force"], CancellationToken.None); } catch { /* best-effort */ }
            try { Directory.Delete(mount); } catch { /* best-effort */ }
        }
    }

    // A .deb is an ar archive: "!<arch>\n", then 60-byte headers (name[16] … size[10] at 48) +
    // 2-byte-aligned data. The files live in the data.tar.{gz,xz,bz2} member.
    private static void UnpackDeb(string archive, string dest, CancellationToken ct)
    {
        using var f = File.OpenRead(archive);
        var magic = new byte[8];
        if (f.Read(magic) != 8 || System.Text.Encoding.ASCII.GetString(magic) != "!<arch>\n")
            throw new PluginStoreException("The .deb archive is damaged.");
        var header = new byte[60];
        while (f.Read(header) == 60)
        {
            var name = System.Text.Encoding.ASCII.GetString(header, 0, 16).Trim().TrimEnd('/');
            long size = long.Parse(System.Text.Encoding.ASCII.GetString(header, 48, 10).Trim());
            if (name.StartsWith("data.tar", StringComparison.Ordinal))
            {
                var member = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dest))!, name);
                using (var o = File.Create(member)) CopyExactly(f, o, size);
                try
                {
                    Directory.CreateDirectory(dest);
                    Run("tar", ["-xf", member, "-C", dest], ct);
                }
                finally { File.Delete(member); }
                return;
            }
            f.Seek(size + (size & 1), SeekOrigin.Current);
        }
        throw new PluginStoreException("The .deb archive has no data.");
    }

    private static void CopyExactly(Stream from, Stream to, long count)
    {
        var buf = new byte[1 << 16];
        while (count > 0)
        {
            int n = from.Read(buf, 0, (int)Math.Min(buf.Length, count));
            if (n <= 0) throw new PluginStoreException("The .deb archive is truncated.");
            to.Write(buf, 0, n);
            count -= n;
        }
    }

    /// <summary>Copies a bundle directory (or single-file .vst3) to <paramref name="dest"/>,
    /// preserving symlinks, permissions and code signatures (ditto on macOS).</summary>
    public static void CopyBundle(string src, string dest, CancellationToken ct = default)
    {
        if (OperatingSystem.IsMacOS()) { Run("ditto", [src, dest], ct); return; }
        if (File.Exists(src)) { File.Copy(src, dest, overwrite: true); return; }
        CopyTree(new DirectoryInfo(src), dest);
    }

    private static void CopyTree(DirectoryInfo src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var entry in src.EnumerateFileSystemInfos())
        {
            var target = Path.Combine(dest, entry.Name);
            if (entry.LinkTarget is { } link) { File.CreateSymbolicLink(target, link); continue; }
            if (entry is DirectoryInfo d) CopyTree(d, target);
            else
            {
                File.Copy(entry.FullName, target, overwrite: true);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(target, File.GetUnixFileMode(entry.FullName));
            }
        }
    }

    private static void Run(string exe, string[] args, CancellationToken ct, string? stdin = null)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        Process p;
        try { p = Process.Start(psi) ?? throw new PluginStoreException($"Couldn't start {exe}."); }
        catch (System.ComponentModel.Win32Exception e) { throw new PluginStoreException($"Unpacking needs \"{exe}\", which wasn't found.", e); }
        using (p)
        {
            if (stdin is not null) { p.StandardInput.Write(stdin); p.StandardInput.Close(); }
            var stderr = p.StandardError.ReadToEndAsync(ct);
            _ = p.StandardOutput.ReadToEndAsync(ct);
            try { p.WaitForExitAsync(ct).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { try { p.Kill(entireProcessTree: true); } catch { } throw; }
            if (p.ExitCode != 0)
                throw new PluginStoreException($"{exe} failed ({p.ExitCode}): {stderr.GetAwaiter().GetResult().Trim()}");
        }
    }
}
