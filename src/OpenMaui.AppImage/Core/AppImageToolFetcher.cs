using System.Runtime.InteropServices;

namespace OpenMaui.AppImage.Core;

/// <summary>
/// Auto-fetches the official appimagetool continuous release when it is not
/// installed on the host, caching it under $XDG_CACHE_HOME/openmaui-appimage/
/// (or ~/.cache/openmaui-appimage/). The URL / cache-path logic is pure and
/// testable; only <see cref="EnsureToolAsync"/> touches the network.
/// </summary>
public static class AppImageToolFetcher
{
    public const string ReleaseBaseUrl =
        "https://github.com/AppImage/appimagetool/releases/download/continuous";

    /// <summary>The appimagetool release architecture suffix for the host CPU.</summary>
    public static string MapArchitecture()
    {
        return RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => "aarch64",
            Architecture.Arm => "armhf",
            Architecture.X86 => "i686",
            _ => "x86_64"
        };
    }

    public static string GetDownloadUrl(string arch)
    {
        return $"{ReleaseBaseUrl}/appimagetool-{arch}.AppImage";
    }

    /// <summary>$XDG_CACHE_HOME/openmaui-appimage, falling back to ~/.cache/openmaui-appimage.</summary>
    public static string GetCacheDirectory()
    {
        var xdgCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        var baseDir = !string.IsNullOrEmpty(xdgCache)
            ? xdgCache
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
        return Path.Combine(baseDir, "openmaui-appimage");
    }

    public static string GetCachedToolPath(string arch)
    {
        return Path.Combine(GetCacheDirectory(), $"appimagetool-{arch}.AppImage");
    }

    /// <summary>
    /// Returns a usable appimagetool path: the cached copy when present, otherwise
    /// downloads the continuous release (unless <paramref name="noFetch"/> forbids
    /// network access). Returns null when the tool cannot be provided.
    /// </summary>
    public static async Task<string?> EnsureToolAsync(bool noFetch)
    {
        var arch = MapArchitecture();
        var cachedPath = GetCachedToolPath(arch);

        if (File.Exists(cachedPath))
        {
            Console.WriteLine($"  Using cached appimagetool: {cachedPath}");
            return cachedPath;
        }

        if (noFetch)
            return null;

        var url = GetDownloadUrl(arch);
        Console.WriteLine();
        Console.WriteLine("  appimagetool not found on this host — downloading the official continuous release:");
        Console.WriteLine($"    {url}");
        Console.WriteLine($"    -> {cachedPath}");
        Console.WriteLine("  Note: the continuous tag publishes no reliable checksum, so the download cannot be");
        Console.WriteLine("  verified. Pass --no-fetch to forbid network access and install appimagetool yourself.");
        Console.WriteLine();

        var tempPath = cachedPath + ".tmp";
        try
        {
            Directory.CreateDirectory(GetCacheDirectory());

            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromMinutes(5);
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using (var file = File.Create(tempPath))
            {
                await response.Content.CopyToAsync(file);
            }

            File.Move(tempPath, cachedPath, overwrite: true);
            File.SetUnixFileMode(cachedPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

            Console.WriteLine("  appimagetool downloaded and cached for future runs.");
            return cachedPath;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  Error downloading appimagetool: {ex.Message}");
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            return null;
        }
    }
}
