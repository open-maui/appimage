using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace OpenMaui.AppImage.Core;

/// <summary>
/// Writes a Debian binary package entirely in managed code (no dpkg-deb, no
/// root): an ar archive holding "debian-binary" (2.0), control.tar.gz
/// (control + md5sums) and data.tar.gz (the staged tree). Tar entries are
/// owned by root:root (uid/gid 0) and carry the modes set on the staged tree.
/// No conffiles and no maintainer scripts: desktop-database and icon-cache
/// refreshes are handled by the distro's dpkg triggers.
/// </summary>
public static class DebPackageWriter
{
    public const string DebianBinary = "2.0\n";

    /// <summary>A payload entry: tar name ("./usr/bin/x"), source on disk, directory flag and mode.</summary>
    public sealed record TarItem(string Name, string? SourcePath, bool IsDirectory, UnixFileMode Mode, long Size);

    /// <summary>Generates the DEBIAN/control file.</summary>
    public static string GenerateControl(NativePackageMetadata meta, NativeDependencies deps, long installedSizeKiB)
    {
        var sb = new StringBuilder();
        sb.Append("Package: ").Append(meta.PackageName).Append('\n');
        sb.Append("Version: ").Append(meta.DebianVersion).Append('\n');
        sb.Append("Architecture: ").Append(meta.Architecture.Debian).Append('\n');
        sb.Append("Maintainer: ").Append(OneLine(meta.Maintainer)).Append('\n');
        sb.Append("Installed-Size: ").Append(installedSizeKiB).Append('\n');
        if (deps.DebDepends.Count > 0)
            sb.Append("Depends: ").Append(string.Join(", ", deps.DebDepends)).Append('\n');
        if (deps.DebRecommends.Count > 0)
            sb.Append("Recommends: ").Append(string.Join(", ", deps.DebRecommends)).Append('\n');
        sb.Append("Section: ").Append(NativePackageMetadata.DebianSection(meta.Category)).Append('\n');
        sb.Append("Priority: optional\n");
        if (!string.IsNullOrWhiteSpace(meta.Homepage))
            sb.Append("Homepage: ").Append(OneLine(meta.Homepage)).Append('\n');
        sb.Append("Description: ").Append(OneLine(meta.Summary)).Append('\n');
        foreach (var line in ExtendedDescriptionLines(meta.Description, meta.Summary))
            sb.Append(line).Append('\n');
        return sb.ToString();
    }

    /// <summary>
    /// Debian extended-description lines: each prefixed by a space, blank lines
    /// written as " .". Omitted when the description adds nothing to the summary.
    /// </summary>
    public static IEnumerable<string> ExtendedDescriptionLines(string? description, string summary)
    {
        if (string.IsNullOrWhiteSpace(description) || description.Trim() == summary.Trim())
            yield break;

        foreach (var raw in description.Replace("\r\n", "\n").Trim().Split('\n'))
        {
            var line = raw.TrimEnd();
            yield return line.Length == 0 ? " ." : " " + line;
        }
    }

    /// <summary>
    /// Collects payload entries from the staging root in dpkg order: "./" first,
    /// each directory before its contents, ordinal-sorted names.
    /// </summary>
    public static List<TarItem> CollectEntries(string stagingRoot)
    {
        var items = new List<TarItem> { new("./", null, true, NativeLayoutBuilder.DirMode, 0) };
        Walk(stagingRoot, "./", items);
        return items;
    }

    private static void Walk(string dir, string prefix, List<TarItem> items)
    {
        foreach (var sub in Directory.GetDirectories(dir).OrderBy(d => d, StringComparer.Ordinal))
        {
            var name = $"{prefix}{Path.GetFileName(sub)}/";
            items.Add(new TarItem(name, sub, true, File.GetUnixFileMode(sub), 0));
            Walk(sub, name, items);
        }
        foreach (var file in Directory.GetFiles(dir).OrderBy(f => f, StringComparer.Ordinal))
        {
            items.Add(new TarItem($"{prefix}{Path.GetFileName(file)}", file, false,
                File.GetUnixFileMode(file), new FileInfo(file).Length));
        }
    }

    /// <summary>Installed-Size in KiB as dpkg computes it: per-file ceil(size/1024), plus 1 per directory.</summary>
    public static long ComputeInstalledSizeKiB(IEnumerable<TarItem> items)
    {
        long total = 0;
        foreach (var item in items)
            total += item.IsDirectory ? 1 : (item.Size + 1023) / 1024;
        return total;
    }

    /// <summary>md5sums: "&lt;md5&gt;  &lt;path without leading ./&gt;" per regular file.</summary>
    public static string GenerateMd5Sums(IEnumerable<TarItem> items)
    {
        var sb = new StringBuilder();
        foreach (var item in items.Where(i => !i.IsDirectory))
        {
            using var fs = File.OpenRead(item.SourcePath!);
            var hash = Convert.ToHexStringLower(MD5.HashData(fs));
            sb.Append(hash).Append("  ").Append(item.Name[2..]).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Writes a tar stream of <paramref name="items"/> (GNU format, root:root).</summary>
    public static void WriteTar(Stream output, IEnumerable<TarItem> items, DateTimeOffset mtime,
        IReadOnlyDictionary<string, byte[]>? inlineFiles = null)
    {
        using var writer = new TarWriter(output, TarEntryFormat.Gnu, leaveOpen: true);
        foreach (var item in items)
        {
            var entry = new GnuTarEntry(item.IsDirectory ? TarEntryType.Directory : TarEntryType.RegularFile, item.Name)
            {
                Mode = item.Mode,
                Uid = 0,
                Gid = 0,
                UserName = "root",
                GroupName = "root",
                ModificationTime = mtime
            };

            Stream? data = null;
            try
            {
                if (!item.IsDirectory)
                {
                    data = inlineFiles != null && inlineFiles.TryGetValue(item.Name, out var bytes)
                        ? new MemoryStream(bytes, writable: false)
                        : File.OpenRead(item.SourcePath!);
                    entry.DataStream = data;
                }
                writer.WriteEntry(entry);
            }
            finally
            {
                data?.Dispose();
            }
        }
    }

    /// <summary>Builds control.tar.gz bytes from the control text and md5sums.</summary>
    public static byte[] BuildControlTarGz(string control, string md5sums, DateTimeOffset mtime)
    {
        var controlBytes = Encoding.UTF8.GetBytes(control);
        var md5Bytes = Encoding.UTF8.GetBytes(md5sums);
        var items = new List<TarItem>
        {
            new("./", null, true, NativeLayoutBuilder.DirMode, 0),
            new("./control", null, false, NativeLayoutBuilder.FileMode, controlBytes.Length),
            new("./md5sums", null, false, NativeLayoutBuilder.FileMode, md5Bytes.Length)
        };
        var inline = new Dictionary<string, byte[]>
        {
            ["./control"] = controlBytes,
            ["./md5sums"] = md5Bytes
        };

        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
            WriteTar(gz, items, mtime, inline);
        return ms.ToArray();
    }

    /// <summary>
    /// Mtime for archive members: SOURCE_DATE_EPOCH when set (reproducible
    /// builds), otherwise now.
    /// </summary>
    public static DateTimeOffset ResolveMtime()
    {
        var sde = Environment.GetEnvironmentVariable("SOURCE_DATE_EPOCH");
        return long.TryParse(sde, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    /// <summary>Writes the .deb for a staged tree.</summary>
    public static void Write(StagedTree staged, NativePackageMetadata meta, NativeDependencies deps, string outputPath)
    {
        var mtime = ResolveMtime();
        var items = CollectEntries(staged.Root);
        var control = GenerateControl(meta, deps, ComputeInstalledSizeKiB(items.Skip(1)));
        var md5sums = GenerateMd5Sums(items);
        var controlTarGz = BuildControlTarGz(control, md5sums, mtime);

        var outputDir = Path.GetDirectoryName(Path.GetFullPath(outputPath))!;
        Directory.CreateDirectory(outputDir);
        var dataTmp = Path.Combine(outputDir, $".{Path.GetFileName(outputPath)}.data-{Guid.NewGuid():N}.tar.gz");
        try
        {
            using (var dataFile = File.Create(dataTmp))
            using (var gz = new GZipStream(dataFile, CompressionLevel.Optimal))
                WriteTar(gz, items, mtime);

            using var output = File.Create(outputPath);
            using var ar = new ArArchiveWriter(output);
            ar.AddEntry("debian-binary", Encoding.ASCII.GetBytes(DebianBinary), mtime);
            ar.AddEntry("control.tar.gz", controlTarGz, mtime);
            using var data = File.OpenRead(dataTmp);
            ar.AddEntry("data.tar.gz", data, data.Length, mtime);
        }
        finally
        {
            try { File.Delete(dataTmp); } catch { }
        }
    }

    private static string OneLine(string value) =>
        value.Replace("\r", " ").Replace("\n", " ").Trim();
}
