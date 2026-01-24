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
        var rootCommand = new RootCommand("Package .NET MAUI Linux apps as AppImages or Flatpaks");

        var inputOption = new Option<DirectoryInfo>(
            aliases: new[] { "--input", "-i" },
            description: "Path to the published .NET app directory")
        { IsRequired = true };

        var outputOption = new Option<FileInfo>(
            aliases: new[] { "--output", "-o" },
            description: "Output file path (.AppImage or .flatpak)")
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

        var formatOption = new Option<string>(
            aliases: new[] { "--format", "-f" },
            () => "appimage",
            description: "Output format: appimage or flatpak");

        var appIdOption = new Option<string>(
            aliases: new[] { "--app-id" },
            description: "Application ID for Flatpak (e.g., com.example.MyApp)");

        rootCommand.AddOption(inputOption);
        rootCommand.AddOption(outputOption);
        rootCommand.AddOption(nameOption);
        rootCommand.AddOption(execOption);
        rootCommand.AddOption(iconOption);
        rootCommand.AddOption(categoryOption);
        rootCommand.AddOption(versionOption);
        rootCommand.AddOption(commentOption);
        rootCommand.AddOption(formatOption);
        rootCommand.AddOption(appIdOption);

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
            var format = context.ParseResult.GetValueForOption(formatOption)!.ToLowerInvariant();
            var appId = context.ParseResult.GetValueForOption(appIdOption);

            var options = new PackageOptions
            {
                InputDirectory = input,
                OutputFile = output,
                AppName = name,
                ExecutableName = exec,
                IconPath = icon,
                Category = category,
                Version = version,
                Comment = comment ?? $"{name} - Built with OpenMaui",
                AppId = appId
            };

            bool result;
            if (format == "flatpak")
            {
                var builder = new FlatpakBuilder();
                result = await builder.BuildAsync(options);
            }
            else
            {
                var builder = new AppImageBuilder();
                result = await builder.BuildAsync(options);
            }

            context.ExitCode = result ? 0 : 1;
        });

        return await rootCommand.InvokeAsync(args);
    }
}

public record PackageOptions
{
    public required DirectoryInfo InputDirectory { get; init; }
    public required FileInfo OutputFile { get; init; }
    public required string AppName { get; init; }
    public string? ExecutableName { get; init; }
    public FileInfo? IconPath { get; init; }
    public required string Category { get; init; }
    public required string Version { get; init; }
    public required string Comment { get; init; }
    public string? AppId { get; init; }  // For Flatpak
}

public class AppImageBuilder
{
    public async Task<bool> BuildAsync(PackageOptions options)
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
        var execName = options.ExecutableName;

        // Auto-detect executable if not specified
        if (string.IsNullOrEmpty(execName))
        {
            execName = AutoDetectExecutable(options.InputDirectory.FullName, options.AppName);
            if (execName != null)
            {
                Console.WriteLine($"  Auto-detected executable: {execName}");
            }
        }

        // Fallback to app name variations
        if (string.IsNullOrEmpty(execName))
        {
            execName = options.AppName;
        }

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

            var found = candidates.FirstOrDefault(File.Exists);
            if (found != null)
            {
                // Update execName to the actual name (without spaces)
                execName = Path.GetFileNameWithoutExtension(found);
                mainExec = found;
            }
            else
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
            await CreateAppRunScript(appRunPath, options, execName);

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

    private async Task CreateAppRunScript(string path, PackageOptions options, string execName)
    {
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
ICONS_DIR_SCALABLE=""$HOME/.local/share/icons/hicolor/scalable/apps""
ICONS_DIR_256=""$HOME/.local/share/icons/hicolor/256x256/apps""

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
    rm -f ""$ICONS_DIR_SCALABLE/${{SANITIZED_NAME}}.svg""
    rm -f ""$ICONS_DIR_256/${{SANITIZED_NAME}}.png""
    rm -f ""$ICONS_DIR_256/${{SANITIZED_NAME}}.ico""
    rm -f ""$INSTALLED_MARKER/$APPIMAGE_BASENAME""
    command -v update-desktop-database &> /dev/null && update-desktop-database ""$APPS_DIR"" 2>/dev/null
    command -v gtk-update-icon-cache &> /dev/null && gtk-update-icon-cache -f -t ""$HOME/.local/share/icons/hicolor"" 2>/dev/null
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

    mkdir -p ""$BIN_DIR"" ""$APPS_DIR"" ""$ICONS_DIR_SCALABLE"" ""$ICONS_DIR_256"" ""$INSTALLED_MARKER""

    # Copy AppImage
    cp ""$APPIMAGE"" ""$BIN_DIR/$APPIMAGE_BASENAME""
    chmod +x ""$BIN_DIR/$APPIMAGE_BASENAME""

    # Copy icon if available (SVG to scalable, PNG/ICO to 256x256)
    if [ -f ""$HERE/${{SANITIZED}}.svg"" ]; then
        cp ""$HERE/${{SANITIZED}}.svg"" ""$ICONS_DIR_SCALABLE/${{SANITIZED}}.svg""
    elif [ -f ""$HERE/${{SANITIZED}}.png"" ]; then
        cp ""$HERE/${{SANITIZED}}.png"" ""$ICONS_DIR_256/${{SANITIZED}}.png""
    elif [ -f ""$HERE/${{SANITIZED}}.ico"" ]; then
        cp ""$HERE/${{SANITIZED}}.ico"" ""$ICONS_DIR_256/${{SANITIZED}}.ico""
    fi

    # Create .desktop file
    # WM_CLASS is app name without spaces or underscores
    WM_CLASS=$(echo ""$APPIMAGE_NAME"" | tr -d ' _')
    cat > ""$APPS_DIR/${{SANITIZED}}.desktop"" << DESKTOP
[Desktop Entry]
Type=Application
Name=$APPIMAGE_NAME
Comment=$APPIMAGE_COMMENT
Exec=$BIN_DIR/$APPIMAGE_BASENAME
Icon=$SANITIZED
Categories=$APPIMAGE_CATEGORY;
Terminal=false
StartupWMClass=$WM_CLASS
X-AppImage-Version=$APPIMAGE_VERSION
DESKTOP

    # Mark as installed
    echo ""$(date -Iseconds)"" > ""$INSTALLED_MARKER/$APPIMAGE_BASENAME""

    # Update desktop database and icon cache
    command -v update-desktop-database &> /dev/null && update-desktop-database ""$APPS_DIR"" 2>/dev/null
    command -v gtk-update-icon-cache &> /dev/null && gtk-update-icon-cache -f -t ""$HOME/.local/share/icons/hicolor"" 2>/dev/null

    return 0
}}

# Check for first run or if already installed - show zenity dialog
# Skip dialog entirely if running from installed location (~/.local/bin)
if [ -n ""$APPIMAGE"" ]; then
    APPIMAGE_BASENAME=$(basename ""$APPIMAGE"")
    APPIMAGE_DIR=$(dirname ""$APPIMAGE"")
    SANITIZED=$(echo ""$APPIMAGE_NAME"" | tr ' ' '_')

    # If running from installed location, just run the app (no dialog)
    if [ ""$APPIMAGE_DIR"" = ""$BIN_DIR"" ]; then
        : # Skip to running the app
    elif [ ""$SHOW_INSTALLER"" = ""1"" ]; then
        # Forced install dialog
        if command -v zenity &> /dev/null; then
            ICON_PATH=""""
            for ext in svg png ico; do
                [ -f ""$HERE/${{SANITIZED}}.${{ext}}"" ] && ICON_PATH=""$HERE/${{SANITIZED}}.${{ext}}"" && break
            done
            ICON_OPT=""""
            [ -n ""$ICON_PATH"" ] && ICON_OPT=""--window-icon=$ICON_PATH""

            zenity --question --title=""$APPIMAGE_NAME"" \
                --text=""<b>$APPIMAGE_NAME</b>\nVersion $APPIMAGE_VERSION\n\n$APPIMAGE_COMMENT\n\nWould you like to install this application?"" \
                --ok-label=""Install"" --cancel-label=""Run Only"" \
                --width=350 $ICON_OPT 2>/dev/null
            ZENITY_EXIT=$?

            if [ $ZENITY_EXIT -eq 0 ]; then
                do_install
                zenity --info --title=""Installation Complete"" \
                    --text=""$APPIMAGE_NAME has been installed.\n\nYou can find it in your application menu."" \
                    --width=300 $ICON_OPT 2>/dev/null
            elif [ $ZENITY_EXIT -ne 1 ]; then
                exit 0
            fi
        fi
    elif [ -f ""$APPS_DIR/${{SANITIZED}}.desktop"" ]; then
        # Already installed and running from different location - show options
        if command -v zenity &> /dev/null; then
            ICON_PATH=""""
            for ext in svg png ico; do
                [ -f ""$HERE/${{SANITIZED}}.${{ext}}"" ] && ICON_PATH=""$HERE/${{SANITIZED}}.${{ext}}"" && break
            done
            ICON_OPT=""""
            [ -n ""$ICON_PATH"" ] && ICON_OPT=""--window-icon=$ICON_PATH""

            CHOICE=$(zenity --list --title=""$APPIMAGE_NAME"" \
                --text=""<b>$APPIMAGE_NAME</b> is already installed.\n\nWhat would you like to do?"" \
                --radiolist --column="" "" --column=""Action"" \
                TRUE ""Run the application"" \
                FALSE ""Reinstall (update)"" \
                FALSE ""Uninstall"" \
                --width=400 --height=380 $ICON_OPT 2>/dev/null)
            ZENITY_EXIT=$?

            if [ $ZENITY_EXIT -ne 0 ]; then
                exit 0
            fi

            case ""$CHOICE"" in
                ""Run the application"")
                    :
                    ;;
                ""Reinstall (update)"")
                    do_install
                    zenity --info --title=""Update Complete"" \
                        --text=""$APPIMAGE_NAME has been updated."" \
                        --width=300 $ICON_OPT 2>/dev/null
                    ;;
                ""Uninstall"")
                    rm -f ""$BIN_DIR/$APPIMAGE_BASENAME""
                    rm -f ""$APPS_DIR/${{SANITIZED}}.desktop""
                    rm -f ""$ICONS_DIR_SCALABLE/${{SANITIZED}}.svg""
                    rm -f ""$ICONS_DIR_256/${{SANITIZED}}.png""
                    rm -f ""$ICONS_DIR_256/${{SANITIZED}}.ico""
                    rm -f ""$INSTALLED_MARKER/$APPIMAGE_BASENAME""
                    command -v update-desktop-database &> /dev/null && update-desktop-database ""$APPS_DIR"" 2>/dev/null
                    command -v gtk-update-icon-cache &> /dev/null && gtk-update-icon-cache -f -t ""$HOME/.local/share/icons/hicolor"" 2>/dev/null
                    zenity --info --title=""Uninstall Complete"" \
                        --text=""$APPIMAGE_NAME has been removed."" \
                        --width=300 $ICON_OPT 2>/dev/null
                    exit 0
                    ;;
            esac
        fi
    else
        # Not installed - show install dialog
        if command -v zenity &> /dev/null; then
            ICON_PATH=""""
            for ext in svg png ico; do
                [ -f ""$HERE/${{SANITIZED}}.${{ext}}"" ] && ICON_PATH=""$HERE/${{SANITIZED}}.${{ext}}"" && break
            done
            ICON_OPT=""""
            [ -n ""$ICON_PATH"" ] && ICON_OPT=""--window-icon=$ICON_PATH""

            zenity --question --title=""$APPIMAGE_NAME"" \
                --text=""<b>$APPIMAGE_NAME</b>\nVersion $APPIMAGE_VERSION\n\n$APPIMAGE_COMMENT\n\nWould you like to install this application?"" \
                --ok-label=""Install"" --cancel-label=""Run Only"" \
                --width=350 $ICON_OPT 2>/dev/null
            ZENITY_EXIT=$?

            if [ $ZENITY_EXIT -eq 0 ]; then
                do_install
                zenity --info --title=""Installation Complete"" \
                    --text=""$APPIMAGE_NAME has been installed.\n\nYou can find it in your application menu."" \
                    --width=300 $ICON_OPT 2>/dev/null
            elif [ $ZENITY_EXIT -ne 1 ]; then
                exit 0
            fi
        fi
    fi
fi

cd ""$HERE/usr/bin""

# Check if this is a self-contained app (native executable exists)
if [ -x ""$HERE/usr/bin/$EXEC_NAME"" ]; then
    exec ""$HERE/usr/bin/$EXEC_NAME"" ""$@""
else
    # Framework-dependent - find and use dotnet
    DOTNET_CMD=""dotnet""
    if ! command -v dotnet &> /dev/null; then
        for d in /usr/share/dotnet /usr/lib/dotnet /opt/dotnet ""$HOME/.dotnet""; do
            [ -x ""$d/dotnet"" ] && DOTNET_CMD=""$d/dotnet"" && break
        done
    fi
    exec ""$DOTNET_CMD"" ""$EXEC_NAME.dll"" ""$@""
fi
";
        await File.WriteAllTextAsync(path, script);
        await RunCommandAsync("chmod", $"+x \"{path}\"");
    }

    private async Task CreateDesktopFile(string path, PackageOptions options)
    {
        // WM_CLASS is app name without spaces or underscores (matches X11Window.cs)
        var wmClass = options.AppName.Replace(" ", "").Replace("_", "");
        var desktop = $@"[Desktop Entry]
Type=Application
Name={options.AppName}
Comment={options.Comment}
Exec={SanitizeFileName(options.AppName)}
Icon={SanitizeFileName(options.AppName)}
Categories={options.Category};
Terminal=false
StartupWMClass={wmClass}
X-AppImage-Version={options.Version}
";
        await File.WriteAllTextAsync(path, desktop);
    }

    private async Task SetupIcon(string appDir, PackageOptions options)
    {
        var iconName = SanitizeFileName(options.AppName);

        if (options.IconPath != null && options.IconPath.Exists)
        {
            // Copy provided icon
            var ext = options.IconPath.Extension.ToLowerInvariant();
            var destIcon = Path.Combine(appDir, $"{iconName}{ext}");
            File.Copy(options.IconPath.FullName, destIcon);

            // Also copy to standard locations for desktop integration
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

            // Also copy to usr/bin as appicon.svg/png for runtime window icon
            // The app looks for appicon.svg or appicon.png in AppContext.BaseDirectory
            var runtimeIconPath = Path.Combine(appDir, "usr", "bin", $"appicon{ext}");
            if (!File.Exists(runtimeIconPath))
                File.Copy(options.IconPath.FullName, runtimeIconPath);
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

            // Also create in usr/bin as appicon.svg for runtime window icon
            var runtimeIconPath = Path.Combine(appDir, "usr", "bin", "appicon.svg");
            await File.WriteAllTextAsync(runtimeIconPath, defaultIcon);
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

    private string? AutoDetectExecutable(string inputDir, string appName)
    {
        // Strategy 1: Look for a native executable (ELF file without extension)
        // These are created when publishing with --self-contained
        var files = Directory.GetFiles(inputDir);
        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            // Skip files with extensions (DLLs, configs, etc.)
            if (fileName.Contains('.')) continue;
            // Skip common non-app files
            if (fileName == "createdump") continue;

            // Check if it's an executable (has execute permission or is ELF)
            try
            {
                var firstBytes = new byte[4];
                using (var fs = File.OpenRead(file))
                {
                    fs.Read(firstBytes, 0, 4);
                }
                // ELF magic number: 0x7F 'E' 'L' 'F'
                if (firstBytes[0] == 0x7F && firstBytes[1] == 'E' && firstBytes[2] == 'L' && firstBytes[3] == 'F')
                {
                    return fileName;
                }
            }
            catch { }
        }

        // Strategy 2: Look for a .dll that matches the app name pattern
        var sanitizedName = appName.Replace(" ", "");
        var dllCandidates = new[]
        {
            $"{sanitizedName}.dll",
            $"{appName}.dll",
        };

        foreach (var dll in dllCandidates)
        {
            if (File.Exists(Path.Combine(inputDir, dll)))
            {
                return Path.GetFileNameWithoutExtension(dll);
            }
        }

        // Strategy 3: Look for any .dll with a matching .runtimeconfig.json (indicates main app)
        var runtimeConfigs = Directory.GetFiles(inputDir, "*.runtimeconfig.json");
        foreach (var config in runtimeConfigs)
        {
            var baseName = Path.GetFileNameWithoutExtension(config).Replace(".runtimeconfig", "");
            var dllPath = Path.Combine(inputDir, baseName + ".dll");
            if (File.Exists(dllPath))
            {
                // Skip Microsoft/System DLLs
                if (!baseName.StartsWith("Microsoft.") && !baseName.StartsWith("System."))
                {
                    return baseName;
                }
            }
        }

        return null;
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
            var svgNs = XNamespace.Get("http://www.w3.org/2000/svg");

            // Find path elements in the foreground
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

            // Check if paths are Material Icons style (coordinates in 0-24 range)
            // by looking at the path coordinates
            var isMaterialIcon = pathData.Split(new[] { ' ', 'M', 'L', 'H', 'V', 'C', 'S', 'Q', 'T', 'A', 'Z', 'm', 'l', 'h', 'v', 'c', 's', 'q', 't', 'a', 'z' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(s => double.TryParse(s, out var v) && v >= 0)
                .Select(s => double.Parse(s))
                .DefaultIfEmpty(0)
                .Max() <= 30; // Material icons use 24x24 coordinate system

            double sourceSize = isMaterialIcon ? 24.0 : 456.0;

            // Create composited SVG (256x256 with rounded corners)
            var finalBgColor = bgColor ?? "#512BD4"; // Default MAUI purple

            // Scale foreground to fit nicely (about 160px in a 256px icon)
            var scale = 160.0 / sourceSize;
            var offsetX = (256 - sourceSize * scale) / 2;
            var offsetY = (256 - sourceSize * scale) / 2;

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

public class FlatpakBuilder
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
        var result = await RunCommandAsync("flatpak", "info org.freedesktop.Platform//23.08", captureOutput: true);
        if (result != 0)
        {
            Console.WriteLine("    Installing Freedesktop runtime...");
            await RunCommandAsync("flatpak", "install -y --user flathub org.freedesktop.Platform//23.08 org.freedesktop.Sdk//23.08");
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
        sha256: {GetDotnetRuntimeInfo().sha256}

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
        var buildResult = await RunCommandAsync("flatpak-builder",
            $"--force-clean --user --repo=\"{repoDir}\" \"{buildDir}\" \"{manifestPath}\"");

        if (buildResult != 0)
        {
            Console.Error.WriteLine("Error: flatpak-builder failed");
            return false;
        }

        // Create the bundle
        var bundleResult = await RunCommandAsync("flatpak",
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

    private (string url, string sha256) GetDotnetRuntimeInfo()
    {
        // .NET 9.0.12 runtime download URLs and checksums (latest stable)
        var arch = GetArchitecture();
        return arch switch
        {
            "arm64" => (
                "https://builds.dotnet.microsoft.com/dotnet/Runtime/9.0.12/dotnet-runtime-9.0.12-linux-arm64.tar.gz",
                "a3a67b4e0e8d0f9255eb18a5036208c80d9ca271cfa43ec6e4db769578a2f127"
            ),
            "x64" => (
                "https://builds.dotnet.microsoft.com/dotnet/Runtime/9.0.12/dotnet-runtime-9.0.12-linux-x64.tar.gz",
                "804aa8357eb498bfc82a403182c43aaad05c3c982f98d1752df9b5b476e572fd"
            ),
            _ => (
                "https://builds.dotnet.microsoft.com/dotnet/Runtime/9.0.12/dotnet-runtime-9.0.12-linux-x64.tar.gz",
                "804aa8357eb498bfc82a403182c43aaad05c3c982f98d1752df9b5b476e572fd"
            )
        };
    }

    private async Task<int> RunCommandAsync(string command, string arguments, bool captureOutput = false)
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
