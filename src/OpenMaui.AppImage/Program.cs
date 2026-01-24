using System.CommandLine;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace OpenMaui.AppImage;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Package .NET MAUI Linux apps as AppImages");

        var inputOption = new Option<DirectoryInfo>(
            aliases: new[] { "--input", "-i" },
            description: "Path to the published .NET app directory")
        { IsRequired = true };

        var outputOption = new Option<FileInfo>(
            aliases: new[] { "--output", "-o" },
            description: "Output AppImage file path")
        { IsRequired = true };

        var nameOption = new Option<string>(
            aliases: new[] { "--name", "-n" },
            description: "Application name (used in .desktop file)")
        { IsRequired = true };

        var execOption = new Option<string>(
            aliases: new[] { "--executable", "-e" },
            description: "Name of the main executable (without extension)");

        var iconOption = new Option<FileInfo?>(
            aliases: new[] { "--icon" },
            description: "Path to application icon (PNG or SVG)");

        var categoryOption = new Option<string>(
            aliases: new[] { "--category", "-c" },
            () => "Utility",
            description: "Desktop category (e.g., Utility, Development, Game)");

        var versionOption = new Option<string>(
            aliases: new[] { "--app-version" },
            () => "1.0.0",
            description: "Application version");

        var commentOption = new Option<string>(
            aliases: new[] { "--comment" },
            description: "Application description/comment");

        rootCommand.AddOption(inputOption);
        rootCommand.AddOption(outputOption);
        rootCommand.AddOption(nameOption);
        rootCommand.AddOption(execOption);
        rootCommand.AddOption(iconOption);
        rootCommand.AddOption(categoryOption);
        rootCommand.AddOption(versionOption);
        rootCommand.AddOption(commentOption);

        rootCommand.SetHandler(async (context) =>
        {
            var input = context.ParseResult.GetValueForOption(inputOption)!;
            var output = context.ParseResult.GetValueForOption(outputOption)!;
            var name = context.ParseResult.GetValueForOption(nameOption)!;
            var exec = context.ParseResult.GetValueForOption(execOption);
            var icon = context.ParseResult.GetValueForOption(iconOption);
            var category = context.ParseResult.GetValueForOption(categoryOption)!;
            var version = context.ParseResult.GetValueForOption(versionOption)!;
            var comment = context.ParseResult.GetValueForOption(commentOption);

            var builder = new AppImageBuilder();
            var result = await builder.BuildAsync(new AppImageOptions
            {
                InputDirectory = input,
                OutputFile = output,
                AppName = name,
                ExecutableName = exec,
                IconPath = icon,
                Category = category,
                Version = version,
                Comment = comment ?? $"{name} - Built with OpenMaui"
            });

            context.ExitCode = result ? 0 : 1;
        });

        return await rootCommand.InvokeAsync(args);
    }
}

public record AppImageOptions
{
    public required DirectoryInfo InputDirectory { get; init; }
    public required FileInfo OutputFile { get; init; }
    public required string AppName { get; init; }
    public string? ExecutableName { get; init; }
    public FileInfo? IconPath { get; init; }
    public required string Category { get; init; }
    public required string Version { get; init; }
    public required string Comment { get; init; }
}

public class AppImageBuilder
{
    public async Task<bool> BuildAsync(AppImageOptions options)
    {
        Console.WriteLine($"Building AppImage for {options.AppName}...");
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
        var mainExec = Path.Combine(options.InputDirectory.FullName, execName);
        if (!File.Exists(mainExec))
        {
            // Try with common variations
            var candidates = new[]
            {
                mainExec,
                mainExec + ".dll",
                Path.Combine(options.InputDirectory.FullName, execName.Replace(" ", "")),
                Path.Combine(options.InputDirectory.FullName, execName.Replace(" ", "") + ".dll")
            };

            mainExec = candidates.FirstOrDefault(File.Exists) ?? "";
            if (string.IsNullOrEmpty(mainExec))
            {
                // List available executables
                var dlls = Directory.GetFiles(options.InputDirectory.FullName, "*.dll")
                    .Select(Path.GetFileNameWithoutExtension)
                    .Take(10)
                    .ToList();

                Console.Error.WriteLine($"Error: Could not find executable '{execName}' in {options.InputDirectory.FullName}");
                Console.Error.WriteLine($"Available DLLs: {string.Join(", ", dlls)}");
                Console.Error.WriteLine("Use --executable to specify the correct name.");
                return false;
            }
        }

        // Auto-detect icon if not specified
        if (options.IconPath == null || !options.IconPath.Exists)
        {
            var detectedIcon = FindIcon(options.InputDirectory.FullName, execName);
            if (detectedIcon != null)
            {
                options = options with { IconPath = new FileInfo(detectedIcon) };
                Console.WriteLine($"  Auto-detected icon: {Path.GetFileName(detectedIcon)}");
            }
        }

        // Create temporary AppDir structure
        var tempDir = Path.Combine(Path.GetTempPath(), $"appimage-{Guid.NewGuid():N}");
        var appDir = Path.Combine(tempDir, $"{options.AppName}.AppDir");

        try
        {
            Directory.CreateDirectory(appDir);
            Console.WriteLine($"  Creating AppDir at: {appDir}");

            // Copy application files
            Console.WriteLine("  Copying application files...");
            CopyDirectory(options.InputDirectory.FullName, Path.Combine(appDir, "usr", "bin"));

            // Note: Using zenity for installer dialog (no bundled installer needed)

            // Create AppRun script
            Console.WriteLine("  Creating AppRun script...");
            var appRunPath = Path.Combine(appDir, "AppRun");
            await CreateAppRunScript(appRunPath, options);

            // Create .desktop file
            Console.WriteLine("  Creating .desktop file...");
            var desktopPath = Path.Combine(appDir, $"{SanitizeFileName(options.AppName)}.desktop");
            await CreateDesktopFile(desktopPath, options);

            // Copy or create icon
            Console.WriteLine("  Setting up icon...");
            await SetupIcon(appDir, options);

            // Create the AppImage using appimagetool
            Console.WriteLine("  Creating AppImage...");
            var success = await CreateAppImage(appDir, options.OutputFile.FullName);

            if (success)
            {
                Console.WriteLine();
                Console.WriteLine($"AppImage created successfully: {options.OutputFile.FullName}");
                Console.WriteLine();
                Console.WriteLine("To run:");
                Console.WriteLine($"  chmod +x {options.OutputFile.Name}");
                Console.WriteLine($"  ./{options.OutputFile.Name}");
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

    private async Task CreateAppRunScript(string path, AppImageOptions options)
    {
        var execName = options.ExecutableName ?? options.AppName;
        var script = $@"#!/bin/bash
# AppRun script for OpenMaui applications

SELF=$(readlink -f ""$0"")
HERE=${{SELF%/*}}

# App metadata
export APPIMAGE_NAME=""{options.AppName}""
export APPIMAGE_COMMENT=""{options.Comment}""
export APPIMAGE_CATEGORY=""{options.Category}""
export APPIMAGE_VERSION=""{options.Version}""
EXEC_NAME=""{execName}""

export PATH=""$HERE/usr/bin:$PATH""
export LD_LIBRARY_PATH=""$HERE/usr/bin:$LD_LIBRARY_PATH""
export XDG_DATA_DIRS=""$HERE/usr/share:${{XDG_DATA_DIRS:-/usr/local/share:/usr/share}}""

INSTALLED_MARKER=""$HOME/.local/share/openmaui-installed""
BIN_DIR=""$HOME/.local/bin""
APPS_DIR=""$HOME/.local/share/applications""
ICONS_DIR=""$HOME/.local/share/icons/hicolor/256x256/apps""

# Handle command line flags
if [ ""$1"" = ""--install"" ]; then
    SHOW_INSTALLER=1
    shift
elif [ ""$1"" = ""--uninstall"" ]; then
    echo ""Uninstalling $APPIMAGE_NAME...""
    APPIMAGE_BASENAME=$(basename ""$APPIMAGE"")
    SANITIZED_NAME=$(echo ""$APPIMAGE_NAME"" | tr ' ' '_')
    rm -f ""$BIN_DIR/$APPIMAGE_BASENAME""
    rm -f ""$APPS_DIR/${{SANITIZED_NAME}}.desktop""
    rm -f ""$INSTALLED_MARKER/$APPIMAGE_BASENAME""
    command -v update-desktop-database &> /dev/null && update-desktop-database ""$APPS_DIR"" 2>/dev/null
    if command -v zenity &> /dev/null; then
        zenity --info --title=""Uninstall Complete"" --text=""$APPIMAGE_NAME has been removed."" --width=300 2>/dev/null
    else
        echo ""Uninstallation complete!""
    fi
    exit 0
elif [ ""$1"" = ""--help"" ]; then
    echo ""Usage: $(basename ""$APPIMAGE"") [OPTIONS]""
    echo """"
    echo ""Options:""
    echo ""  --install     Show the installation dialog""
    echo ""  --uninstall   Remove the application""
    echo ""  --help        Show this help message""
    exit 0
fi

# Installation function
do_install() {{
    APPIMAGE_BASENAME=$(basename ""$APPIMAGE"")
    SANITIZED=$(echo ""$APPIMAGE_NAME"" | tr ' ' '_')

    mkdir -p ""$BIN_DIR"" ""$APPS_DIR"" ""$ICONS_DIR"" ""$INSTALLED_MARKER""

    # Copy AppImage
    cp ""$APPIMAGE"" ""$BIN_DIR/$APPIMAGE_BASENAME""
    chmod +x ""$BIN_DIR/$APPIMAGE_BASENAME""

    # Copy icon if available
    for ext in svg png ico; do
        if [ -f ""$HERE/${{SANITIZED}}.${{ext}}"" ]; then
            cp ""$HERE/${{SANITIZED}}.${{ext}}"" ""$ICONS_DIR/${{SANITIZED}}.${{ext}}""
            break
        fi
    done

    # Create .desktop file
    cat > ""$APPS_DIR/${{SANITIZED}}.desktop"" << DESKTOP
[Desktop Entry]
Type=Application
Name=$APPIMAGE_NAME
Comment=$APPIMAGE_COMMENT
Exec=$BIN_DIR/$APPIMAGE_BASENAME
Icon=$SANITIZED
Categories=$APPIMAGE_CATEGORY;
Terminal=false
X-AppImage-Version=$APPIMAGE_VERSION
DESKTOP

    # Mark as installed
    echo ""$(date -Iseconds)"" > ""$INSTALLED_MARKER/$APPIMAGE_BASENAME""

    # Update desktop database
    command -v update-desktop-database &> /dev/null && update-desktop-database ""$APPS_DIR"" 2>/dev/null

    return 0
}}

# Check for first run - show zenity dialog
if [ -n ""$APPIMAGE"" ]; then
    APPIMAGE_BASENAME=$(basename ""$APPIMAGE"")
    if [ ! -f ""$INSTALLED_MARKER/$APPIMAGE_BASENAME"" ] || [ ""$SHOW_INSTALLER"" = ""1"" ]; then
        if command -v zenity &> /dev/null; then
            # Find app icon
            SANITIZED=$(echo ""$APPIMAGE_NAME"" | tr ' ' '_')
            ICON_PATH=""""
            for ext in svg png ico; do
                [ -f ""$HERE/${{SANITIZED}}.${{ext}}"" ] && ICON_PATH=""$HERE/${{SANITIZED}}.${{ext}}"" && break
            done

            ICON_OPT=""""
            [ -n ""$ICON_PATH"" ] && ICON_OPT=""--window-icon=$ICON_PATH""

            CHOICE=$(zenity --question --title=""$APPIMAGE_NAME"" \
                --text=""<b>$APPIMAGE_NAME</b>\nVersion $APPIMAGE_VERSION\n\n$APPIMAGE_COMMENT\n\nWould you like to install this application?"" \
                --ok-label=""Install"" --cancel-label=""Run Without Installing"" \
                --extra-button=""Cancel"" \
                --width=350 $ICON_OPT 2>/dev/null; echo $?)

            case ""$CHOICE"" in
                0)  # Install clicked
                    do_install
                    if [ $? -eq 0 ]; then
                        zenity --info --title=""Installation Complete"" \
                            --text=""$APPIMAGE_NAME has been installed.\n\nYou can find it in your application menu."" \
                            --width=300 $ICON_OPT 2>/dev/null
                    fi
                    ;;
                1)  # Run Without Installing
                    ;;
                *)  # Cancel or closed
                    exit 0
                    ;;
            esac
        fi
    fi
fi

cd ""$HERE/usr/bin""
exec dotnet ""$EXEC_NAME.dll"" ""$@""
";
        await File.WriteAllTextAsync(path, script);
        await RunCommandAsync("chmod", $"+x \"{path}\"");
    }

    private async Task CreateDesktopFile(string path, AppImageOptions options)
    {
        var desktop = $@"[Desktop Entry]
Type=Application
Name={options.AppName}
Comment={options.Comment}
Exec={SanitizeFileName(options.AppName)}
Icon={SanitizeFileName(options.AppName)}
Categories={options.Category};
Terminal=false
X-AppImage-Version={options.Version}
";
        await File.WriteAllTextAsync(path, desktop);
    }

    private async Task SetupIcon(string appDir, AppImageOptions options)
    {
        var iconName = SanitizeFileName(options.AppName);

        if (options.IconPath != null && options.IconPath.Exists)
        {
            // Copy provided icon
            var ext = options.IconPath.Extension.ToLowerInvariant();
            var destIcon = Path.Combine(appDir, $"{iconName}{ext}");
            File.Copy(options.IconPath.FullName, destIcon);

            // Also copy to standard locations
            var iconDirs = new[]
            {
                Path.Combine(appDir, "usr", "share", "icons", "hicolor", "256x256", "apps"),
                Path.Combine(appDir, "usr", "share", "icons", "hicolor", "scalable", "apps")
            };

            foreach (var dir in iconDirs)
            {
                Directory.CreateDirectory(dir);
                var iconPath = Path.Combine(dir, $"{iconName}{ext}");
                if (!File.Exists(iconPath))
                    File.Copy(options.IconPath.FullName, iconPath);
            }
        }
        else
        {
            // Create a simple default icon (placeholder SVG)
            var defaultIcon = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<svg width=""256"" height=""256"" viewBox=""0 0 256 256"" xmlns=""http://www.w3.org/2000/svg"">
  <rect width=""256"" height=""256"" rx=""32"" fill=""#2196F3""/>
  <text x=""128"" y=""160"" font-family=""sans-serif"" font-size=""120"" font-weight=""bold""
        text-anchor=""middle"" fill=""white"">{options.AppName[0]}</text>
</svg>";

            var iconPath = Path.Combine(appDir, $"{iconName}.svg");
            await File.WriteAllTextAsync(iconPath, defaultIcon);

            // Also create in standard location
            var svgDir = Path.Combine(appDir, "usr", "share", "icons", "hicolor", "scalable", "apps");
            Directory.CreateDirectory(svgDir);
            await File.WriteAllTextAsync(Path.Combine(svgDir, $"{iconName}.svg"), defaultIcon);
        }
    }

    private async Task<bool> CreateAppImage(string appDir, string outputPath)
    {
        // Ensure output directory exists
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        // Check if appimagetool is available
        var appImageTool = await FindAppImageTool();
        if (appImageTool == null)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("Error: appimagetool not found.");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Please install appimagetool:");
            Console.Error.WriteLine("  wget https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage");
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

        var result = await RunCommandAsync(appImageTool, $"\"{appDir}\" \"{outputPath}\"",
            new Dictionary<string, string> { ["ARCH"] = arch });

        if (result == 0)
        {
            // Make the AppImage executable
            await RunCommandAsync("chmod", $"+x \"{outputPath}\"");
            return true;
        }

        return false;
    }

    private string? FindIcon(string inputDir, string execName)
    {
        // First, try to find and parse the .csproj to get MauiIcon
        var csprojIcon = TryGetIconFromCsproj(inputDir, execName);
        if (csprojIcon != null)
            return csprojIcon;

        // Fallback to searching for icon files directly
        var extensions = new[] { ".svg", ".png", ".ico" };

        var searchPatterns = new[]
        {
            "appicon_combined",                 // Combined icon for Linux desktop
            "appicon",                          // MAUI standard: appicon.svg
            "AppIcon",                          // AppIcon.svg
            execName,                           // ShellDemo.svg
            execName.ToLowerInvariant(),        // shelldemo.svg
            "icon",                             // icon.svg
            "logo",                             // logo.svg
        };

        var searchDirs = new[]
        {
            Path.Combine(inputDir, "Resources", "AppIcon"),
            Path.Combine(inputDir, "Resources", "Splash"),
            inputDir,
            Path.Combine(inputDir, "Resources"),
            Path.Combine(inputDir, "Resources", "Images"),
        };

        foreach (var dir in searchDirs)
        {
            if (!Directory.Exists(dir)) continue;

            foreach (var pattern in searchPatterns)
            {
                foreach (var ext in extensions)
                {
                    var iconPath = Path.Combine(dir, pattern + ext);
                    if (File.Exists(iconPath))
                        return iconPath;
                }
            }
        }

        return null;
    }

    private string? TryGetIconFromCsproj(string inputDir, string execName)
    {
        // Find the project directory (go up from publish directory)
        var projectDir = FindProjectDirectory(inputDir, execName);
        if (projectDir == null)
            return null;

        // Find .csproj file
        var csprojFiles = Directory.GetFiles(projectDir, "*.csproj");
        if (csprojFiles.Length == 0)
            return null;

        var csprojPath = csprojFiles[0];
        Console.WriteLine($"  Found project: {Path.GetFileName(csprojPath)}");

        try
        {
            var doc = XDocument.Load(csprojPath);
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

            // Find MauiIcon element
            var mauiIcon = doc.Descendants("MauiIcon").FirstOrDefault();
            if (mauiIcon == null)
                return null;

            var includeAttr = mauiIcon.Attribute("Include")?.Value;
            var foregroundAttr = mauiIcon.Attribute("ForegroundFile")?.Value;
            var colorAttr = mauiIcon.Attribute("Color")?.Value ?? "#FFFFFF";
            var bgColorAttr = mauiIcon.Attribute("BackgroundColor")?.Value;

            if (string.IsNullOrEmpty(includeAttr))
                return null;

            // Resolve paths (convert backslashes to forward slashes for Linux)
            var bgPath = Path.Combine(projectDir, includeAttr.Replace('\\', '/'));
            var fgPath = !string.IsNullOrEmpty(foregroundAttr)
                ? Path.Combine(projectDir, foregroundAttr.Replace('\\', '/'))
                : null;

            if (!File.Exists(bgPath))
                return null;

            // If we have both background and foreground, composite them
            if (fgPath != null && File.Exists(fgPath))
            {
                var compositedIcon = CompositeIcon(bgPath, fgPath, bgColorAttr, colorAttr);
                if (compositedIcon != null)
                {
                    Console.WriteLine($"  Composited icon from MauiIcon (bg + fg)");
                    return compositedIcon;
                }
            }

            // Otherwise, just use the background with the BackgroundColor
            if (!string.IsNullOrEmpty(bgColorAttr))
            {
                Console.WriteLine($"  Using MauiIcon background: {Path.GetFileName(bgPath)}");
            }

            return bgPath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Warning: Could not parse csproj: {ex.Message}");
            return null;
        }
    }

    private string? FindProjectDirectory(string inputDir, string execName)
    {
        // The publish directory is typically: ProjectDir/bin/Release/net9.0/linux-arm64/publish
        // We need to go up to find the project directory

        var dir = new DirectoryInfo(inputDir);

        // Go up looking for a .csproj file
        while (dir != null)
        {
            var csprojFiles = dir.GetFiles("*.csproj");
            if (csprojFiles.Length > 0)
                return dir.FullName;

            // Also check if we find a matching project name
            if (dir.GetFiles($"{execName}.csproj").Length > 0)
                return dir.FullName;

            dir = dir.Parent;

            // Don't go too far up (max 10 levels)
            if (dir?.FullName.Split(Path.DirectorySeparatorChar).Length < 3)
                break;
        }

        return null;
    }

    private string? CompositeIcon(string bgPath, string fgPath, string? bgColor, string fgColor)
    {
        try
        {
            // Read the foreground SVG to extract the path
            var fgContent = File.ReadAllText(fgPath);
            var fgDoc = XDocument.Parse(fgContent);

            // Find path elements in the foreground
            var svgNs = XNamespace.Get("http://www.w3.org/2000/svg");
            var pathElements = fgDoc.Descendants(svgNs + "path")
                .Concat(fgDoc.Descendants("path"))
                .ToList();

            if (pathElements.Count == 0)
                return null;

            // Extract path data
            var pathData = string.Join(" ", pathElements
                .Select(p => p.Attribute("d")?.Value)
                .Where(d => !string.IsNullOrEmpty(d)));

            if (string.IsNullOrEmpty(pathData))
                return null;

            // Get the viewBox or size from foreground
            var fgRoot = fgDoc.Root;
            var fgViewBox = fgRoot?.Attribute("viewBox")?.Value ?? "0 0 24 24";

            // Parse viewBox to get dimensions
            var vbParts = fgViewBox.Split(' ');
            var fgWidth = vbParts.Length >= 3 ? double.Parse(vbParts[2]) : 24;
            var fgHeight = vbParts.Length >= 4 ? double.Parse(vbParts[3]) : 24;

            // Create composited SVG (256x256 with rounded corners)
            var finalBgColor = bgColor ?? "#512BD4"; // Default MAUI purple
            var scale = 160.0 / Math.Max(fgWidth, fgHeight); // Scale to fit in 160px centered in 256px
            var offsetX = (256 - fgWidth * scale) / 2;
            var offsetY = (256 - fgHeight * scale) / 2;

            var compositedSvg = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<svg width=""256"" height=""256"" viewBox=""0 0 256 256"" xmlns=""http://www.w3.org/2000/svg"">
  <rect width=""256"" height=""256"" rx=""48"" fill=""{finalBgColor}""/>
  <g transform=""translate({offsetX:F1}, {offsetY:F1}) scale({scale:F3})"">
    <path fill=""{fgColor}"" d=""{pathData}""/>
  </g>
</svg>";

            // Write to temp file
            var tempIcon = Path.Combine(Path.GetTempPath(), $"appicon_{Guid.NewGuid():N}.svg");
            File.WriteAllText(tempIcon, compositedSvg);

            return tempIcon;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Warning: Could not composite icon: {ex.Message}");
            return null;
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
        };

        foreach (var candidate in candidates)
        {
            var result = await RunCommandAsync("which", candidate, captureOutput: true);
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

    private void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.GetFiles(source))
        {
            var destFile = Path.Combine(destination, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (var dir in Directory.GetDirectories(source))
        {
            var destDir = Path.Combine(destination, Path.GetFileName(dir));
            CopyDirectory(dir, destDir);
        }
    }

    private string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new StringBuilder();
        foreach (var c in name)
        {
            if (invalid.Contains(c) || c == ' ')
                sanitized.Append('_');
            else
                sanitized.Append(c);
        }
        return sanitized.ToString();
    }

    private async Task<int> RunCommandAsync(string command, string arguments,
        Dictionary<string, string>? envVars = null, bool captureOutput = false)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = captureOutput,
                RedirectStandardError = captureOutput,
                CreateNoWindow = true
            };

            if (envVars != null)
            {
                foreach (var (key, value) in envVars)
                    psi.EnvironmentVariables[key] = value;
            }

            using var process = Process.Start(psi);
            if (process == null) return -1;

            await process.WaitForExitAsync();
            return process.ExitCode;
        }
        catch
        {
            return -1;
        }
    }
}
