namespace OpenMaui.AppImage.Core;

/// <summary>
/// Invokes appimagetool to pack a prepared AppDir into an AppImage,
/// including FUSE detection / extract-and-run fallback.
/// </summary>
public class AppImagePacker
{
    public async Task<bool> PackAsync(string appDir, string outputPath, bool noFuse = false,
        bool noFetch = false, string? updateInfo = null, bool sign = false, string? signKey = null)
    {
        // Ensure output directory exists
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        // Check if appimagetool is available; auto-fetch it when allowed
        var appImageTool = await FindAppImageTool();
        if (appImageTool == null && !noFetch)
            appImageTool = await AppImageToolFetcher.EnsureToolAsync(noFetch: false);

        if (appImageTool == null)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine(noFetch
                ? "Error: appimagetool not found (and --no-fetch forbids downloading it)."
                : "Error: appimagetool not found and could not be downloaded.");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Please install appimagetool:");
            Console.Error.WriteLine("  wget https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage");
            Console.Error.WriteLine("  chmod +x appimagetool-x86_64.AppImage");
            Console.Error.WriteLine("  sudo mv appimagetool-x86_64.AppImage /usr/local/bin/appimagetool");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Or install via package manager:");
            Console.Error.WriteLine("  Ubuntu/Debian: sudo apt install appimagetool");
            Console.Error.WriteLine("  Fedora: sudo dnf install appimagetool");
            Console.Error.WriteLine("  Arch: yay -S appimagetool-bin");
            return false;
        }

        // Set ARCH environment variable
        var arch = Environment.GetEnvironmentVariable("ARCH") ?? "x86_64";

        var envVars = new Dictionary<string, string> { ["ARCH"] = arch };

        // appimagetool is itself distributed as an AppImage, so on hosts without a
        // usable /dev/fuse (LXC containers, minimal CI images) it can't mount itself
        // and hangs or fails. When FUSE isn't available we tell the AppImage runtime
        // to extract-and-run instead. We set the ENV VAR rather than passing the
        // --appimage-extract-and-run FLAG: the AppImage runtime honors the env var,
        // and a distro-packaged (native ELF) appimagetool simply ignores it —
        // whereas the flag would be an unknown argument that breaks the native tool.
        if (noFuse || !IsFuseAvailable())
        {
            envVars["APPIMAGE_EXTRACT_AND_RUN"] = "1";
            Console.WriteLine(noFuse
                ? "  --no-fuse set — running appimagetool in extract-and-run mode."
                : "  No usable /dev/fuse detected — running appimagetool in extract-and-run mode.");
        }

        var arguments = BuildToolArguments(appDir, outputPath, updateInfo, sign, signKey);
        var result = await ProcessRunner.RunCommandAsync(appImageTool, arguments, envVars);

        if (result == 0)
        {
            // Make the AppImage executable
            await ProcessRunner.RunCommandAsync("chmod", $"+x \"{outputPath}\"");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Builds the appimagetool argument line: optional update information (-u,
    /// zsync self-updating), optional GPG signing (--sign / --sign-key — a key id
    /// implies --sign), then the AppDir and output path. Pure and testable.
    /// </summary>
    public static string BuildToolArguments(string appDir, string outputPath,
        string? updateInfo = null, bool sign = false, string? signKey = null)
    {
        var args = new List<string>();

        if (!string.IsNullOrEmpty(updateInfo))
        {
            args.Add("-u");
            args.Add($"\"{updateInfo}\"");
        }

        if (sign || !string.IsNullOrEmpty(signKey))
            args.Add("--sign");

        if (!string.IsNullOrEmpty(signKey))
        {
            args.Add("--sign-key");
            args.Add(signKey);
        }

        args.Add($"\"{appDir}\"");
        args.Add($"\"{outputPath}\"");
        return string.Join(" ", args);
    }

    /// <summary>
    /// Whether FUSE is usable on this host. Needed to *mount* an AppImage-packaged
    /// appimagetool; absent in most LXC containers and minimal CI images. Probes the
    /// /dev/fuse device (existence + openability, since some images expose the node
    /// but deny it without CAP_SYS_ADMIN). Any uncertainty returns false so the
    /// caller falls back to the always-safe extract-and-run path.
    /// </summary>
    public static bool IsFuseAvailable(string fuseDevicePath = "/dev/fuse")
    {
        if (!OperatingSystem.IsLinux())
            return false;
        try
        {
            if (!File.Exists(fuseDevicePath))
                return false;
            using var fs = new FileStream(fuseDevicePath, FileMode.Open, FileAccess.ReadWrite);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string?> FindAppImageTool()
    {
        var candidates = new[]
        {
            "appimagetool",
            "/usr/local/bin/appimagetool",
            "/usr/bin/appimagetool",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "bin", "appimagetool"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "appimagetool"),
            // Previously auto-fetched copy
            AppImageToolFetcher.GetCachedToolPath(AppImageToolFetcher.MapArchitecture()),
        };

        foreach (var candidate in candidates)
        {
            var result = await ProcessRunner.RunCommandAsync("which", candidate, captureOutput: true);
            if (result == 0)
                return candidate;

            if (File.Exists(candidate))
                return candidate;
        }

        // Try to find any appimagetool AppImage
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var searchDirs = new[] { homeDir, "/usr/local/bin", "/opt" };

        foreach (var dir in searchDirs)
        {
            if (Directory.Exists(dir))
            {
                var tools = Directory.GetFiles(dir, "appimagetool*", SearchOption.TopDirectoryOnly);
                if (tools.Length > 0)
                    return tools[0];
            }
        }

        return null;
    }
}
