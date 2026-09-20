using System.Text;

namespace OpenMaui.AppImage.Core;

public class FlatpakPacker
{
    public async Task<bool> BuildAsync(PackageOptions options)
    {
        Console.WriteLine($"Building Flatpak for {options.AppName}...");
        Console.WriteLine($"  Input: {options.InputDirectory.FullName}");
        Console.WriteLine($"  Output: {options.OutputFile.FullName}");

        // Validate input directory
        if (!options.InputDirectory.Exists)
        {
            Console.Error.WriteLine($"Error: Input directory does not exist: {options.InputDirectory.FullName}");
            return false;
        }

        // Find the main executable
        var execName = options.ExecutableName ?? options.AppName;
        var mainDll = Path.Combine(options.InputDirectory.FullName, $"{execName}.dll");
        if (!File.Exists(mainDll))
        {
            Console.Error.WriteLine($"Error: Could not find {execName}.dll in {options.InputDirectory.FullName}");
            return false;
        }

        // Generate app ID if not provided
        var appId = options.AppId ?? $"com.openmaui.{SanitizeAppId(options.AppName)}";
        Console.WriteLine($"  App ID: {appId}");

        // Create temporary build directory
        var tempDir = Path.Combine(Path.GetTempPath(), $"flatpak-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempDir);

            // Setup Flatpak runtime (install if needed)
            Console.WriteLine("  Checking Flatpak runtime...");
            await EnsureRuntimeInstalled();

            // Create manifest
            Console.WriteLine("  Creating manifest...");
            var manifestPath = Path.Combine(tempDir, $"{appId}.yaml");
            await CreateManifest(manifestPath, options, appId, execName);

            // Create app files directory structure
            Console.WriteLine("  Preparing app files...");
            var filesDir = Path.Combine(tempDir, "files");
            Directory.CreateDirectory(filesDir);

            // Copy application files
            var appDir = Path.Combine(filesDir, "app");
            CopyDirectory(options.InputDirectory.FullName, appDir);

            // Setup icon
            await SetupIcon(tempDir, filesDir, options, appId);

            // Create desktop file
            await CreateDesktopFile(filesDir, options, appId, execName);

            // Build the Flatpak
            Console.WriteLine("  Building Flatpak (this may take a while)...");
            var success = await BuildFlatpak(tempDir, manifestPath, options.OutputFile.FullName, appId);

            if (success)
            {
                Console.WriteLine();
                Console.WriteLine($"Flatpak created successfully: {options.OutputFile.FullName}");
                Console.WriteLine();
                Console.WriteLine("To install:");
                Console.WriteLine($"  flatpak install --user {options.OutputFile.Name}");
                Console.WriteLine();
                Console.WriteLine("To run:");
                Console.WriteLine($"  flatpak run {appId}");
            }

            return success;
        }
        finally
        {
            // Cleanup temp directory
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
            catch { }
        }
    }

    private async Task EnsureRuntimeInstalled()
    {
        // Check if freedesktop runtime is installed
        var result = await ProcessRunner.RunCommandAsync("flatpak", "info org.freedesktop.Platform//23.08", captureOutput: true);
        if (result != 0)
        {
            Console.WriteLine("    Installing Freedesktop runtime...");
            await ProcessRunner.RunCommandAsync("flatpak", "install -y --user flathub org.freedesktop.Platform//23.08 org.freedesktop.Sdk//23.08");
        }
    }

    private async Task CreateManifest(string path, PackageOptions options, string appId, string execName)
    {
        // Create a Flatpak manifest that bundles the .NET app
        var manifest = $@"app-id: {appId}
runtime: org.freedesktop.Platform
runtime-version: '23.08'
sdk: org.freedesktop.Sdk
command: run-app.sh

finish-args:
  - --share=ipc
  - --share=network
  - --socket=x11
  - --socket=wayland
  - --socket=pulseaudio
  - --device=dri
  - --filesystem=home
  - --talk-name=org.freedesktop.Notifications

modules:
  - name: dotnet-runtime
    buildsystem: simple
    build-commands:
      - install -d /app/dotnet
      - tar -xzf dotnet-runtime-*.tar.gz -C /app/dotnet
    sources:
      - type: file
        url: {GetDotnetRuntimeInfo().url}
        sha512: {GetDotnetRuntimeInfo().sha512}

  - name: {SanitizeAppId(options.AppName)}
    buildsystem: simple
    build-commands:
      - install -d /app/app
      - cp -r app/* /app/app/
      - install -Dm755 run-app.sh /app/bin/run-app.sh
      - install -Dm644 {appId}.desktop /app/share/applications/{appId}.desktop
      - install -Dm644 {appId}.svg /app/share/icons/hicolor/scalable/apps/{appId}.svg
    sources:
      - type: dir
        path: files
      - type: script
        dest-filename: run-app.sh
        commands:
          - 'export DOTNET_ROOT=/app/dotnet'
          - 'export PATH=$DOTNET_ROOT:$PATH'
          - 'exec /app/dotnet/dotnet /app/app/{execName}.dll ""$@""'
";
        await File.WriteAllTextAsync(path, manifest);
    }

    private async Task SetupIcon(string tempDir, string filesDir, PackageOptions options, string appId)
    {
        var iconPath = options.IconPath?.FullName;

        // Try to find icon from csproj if not provided
        if (string.IsNullOrEmpty(iconPath) || !File.Exists(iconPath))
        {
            iconPath = FindIcon(options.InputDirectory.FullName, options.ExecutableName ?? options.AppName);
        }

        var destIcon = Path.Combine(filesDir, $"{appId}.svg");

        if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
        {
            File.Copy(iconPath, destIcon, overwrite: true);
        }
        else
        {
            // Create default icon
            var defaultIcon = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<svg width=""256"" height=""256"" viewBox=""0 0 256 256"" xmlns=""http://www.w3.org/2000/svg"">
  <rect width=""256"" height=""256"" rx=""48"" fill=""#512BD4""/>
  <text x=""128"" y=""170"" font-family=""sans-serif"" font-size=""140"" font-weight=""bold""
        text-anchor=""middle"" fill=""white"">{options.AppName[0]}</text>
</svg>";
            await File.WriteAllTextAsync(destIcon, defaultIcon);
        }
    }

    private async Task CreateDesktopFile(string filesDir, PackageOptions options, string appId, string execName)
    {
        var desktop = $@"[Desktop Entry]
Type=Application
Name={options.AppName}
Comment={options.Comment}
Exec=run-app.sh
Icon={appId}
Categories={options.Category};
Terminal=false
";
        var desktopPath = Path.Combine(filesDir, $"{appId}.desktop");
        await File.WriteAllTextAsync(desktopPath, desktop);
    }

    private async Task<bool> BuildFlatpak(string tempDir, string manifestPath, string outputPath, string appId)
    {
        var buildDir = Path.Combine(tempDir, "build");
        var repoDir = Path.Combine(tempDir, "repo");

        // Build the flatpak
        var buildResult = await ProcessRunner.RunCommandAsync("flatpak-builder",
            $"--force-clean --user --repo=\"{repoDir}\" \"{buildDir}\" \"{manifestPath}\"");

        if (buildResult != 0)
        {
            Console.Error.WriteLine("Error: flatpak-builder failed");
            return false;
        }

        // Create the bundle
        var bundleResult = await ProcessRunner.RunCommandAsync("flatpak",
            $"build-bundle \"{repoDir}\" \"{outputPath}\" {appId}");

        return bundleResult == 0;
    }

    private string? FindIcon(string inputDir, string execName)
    {
        var extensions = new[] { ".svg", ".png" };
        var patterns = new[] { "appicon_combined", "appicon", execName.ToLowerInvariant(), "icon" };
        var dirs = new[]
        {
            Path.Combine(inputDir, "Resources", "AppIcon"),
            inputDir
        };

        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var pattern in patterns)
            {
                foreach (var ext in extensions)
                {
                    var path = Path.Combine(dir, pattern + ext);
                    if (File.Exists(path)) return path;
                }
            }
        }
        return null;
    }

    private void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }
        foreach (var dir in Directory.GetDirectories(source))
        {
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
        }
    }

    private string SanitizeAppId(string name)
    {
        var result = new StringBuilder();
        foreach (var c in name.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
                result.Append(c);
        }
        return result.ToString();
    }

    private string GetArchitecture()
    {
        return System.Runtime.InteropServices.RuntimeInformation.OSArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            System.Runtime.InteropServices.Architecture.X64 => "x64",
            _ => "x64"
        };
    }

    private (string url, string sha512) GetDotnetRuntimeInfo()
    {
        // .NET 10.0.12 runtime - for Flatpak bundling
        // Update these when new .NET 10 releases are published
        // Checksums: https://builds.dotnet.microsoft.com/dotnet/checksums/10.0.12-sha.txt
        var arch = GetArchitecture();
        var version = "10.0.12";
        return arch switch
        {
            "arm64" => (
                $"https://builds.dotnet.microsoft.com/dotnet/Runtime/{version}/dotnet-runtime-{version}-linux-arm64.tar.gz",
                "ac1e0090391085ba70c7457adaaa8f6cc20dc216b9b368b4227a427de271708d783380c7b955b7be4897ff24849fbe5c3b7ec1cb1c38345a725de9a3812280eb"
            ),
            _ => (
                $"https://builds.dotnet.microsoft.com/dotnet/Runtime/{version}/dotnet-runtime-{version}-linux-x64.tar.gz",
                "58388fdde4f13bd703c7a6f7defb3300b17e42ba5fe8bca50066f80f64ac7406620dcdfb4acc1eff7992750c8cc5cff8369dd12d09730c8a9e8760d6032f7f6e"
            )
        };
    }
}
