using System.Text;
using System.Xml.Linq;

namespace OpenMaui.AppImage.Core;

/// <summary>
/// Builds the AppDir tree for an AppImage: copies the application files and
/// generates the AppRun script, .desktop file, and icon.
/// </summary>
public class AppDirBuilder
{
    /// <summary>
    /// Populates <paramref name="appDir"/> from the input directory and generates
    /// AppRun, the .desktop file, and the icon.
    /// </summary>
    public async Task PopulateAsync(string appDir, PackageOptions options, string execName)
    {
        Directory.CreateDirectory(appDir);
        Console.WriteLine($"  Creating AppDir at: {appDir}");

        // Copy application files
        Console.WriteLine("  Copying application files...");
        if (options.PreBuiltAppDir)
        {
            // Input is already an FHS tree (usr/bin, usr/lib, usr/share, …) — use
            // it directly as the AppDir root so resources under usr/lib/usr/share
            // are preserved. AppRun still execs $HERE/usr/bin/$EXEC_NAME.
            CopyDirectory(options.InputDirectory.FullName, appDir);
        }
        else
        {
            CopyDirectory(options.InputDirectory.FullName, Path.Combine(appDir, "usr", "bin"));
        }

        // Note: Using zenity for installer dialog (no bundled installer needed)

        // Optional launch-time host dependency check (--host-deps-check)
        string? hostDepsCheckBlock = null;
        if (options.HostDepsCheck)
        {
            var scan = DependencyScanner.Scan(options.InputDirectory.FullName);
            if (scan.Dependencies.Count > 0)
            {
                Console.WriteLine("  Injecting host dependency check into AppRun...");
                hostDepsCheckBlock = DependencyScanner.GenerateAppRunCheckBlock(scan.Dependencies);
            }
        }

        // Create AppRun script
        Console.WriteLine("  Creating AppRun script...");
        var appRunPath = Path.Combine(appDir, "AppRun");
        await CreateAppRunScript(appRunPath, options, execName, hostDepsCheckBlock);

        // Create .desktop file
        Console.WriteLine("  Creating .desktop file...");
        var desktopPath = Path.Combine(appDir, $"{SanitizeFileName(options.AppName)}.desktop");
        await CreateDesktopFile(desktopPath, options);

        // Optional AppStream metainfo (--metainfo)
        if (options.GenerateMetainfo)
        {
            Console.WriteLine("  Generating AppStream metainfo...");
            await CreateMetainfoFile(appDir, options, execName);
        }

        // Copy or create icon
        Console.WriteLine("  Setting up icon...");
        await SetupIcon(appDir, options);
    }

    public async Task CreateAppRunScript(string path, PackageOptions options, string execName,
        string? hostDepsCheckBlock = null)
    {
        var script = GenerateAppRunScript(options, execName, hostDepsCheckBlock);
        await File.WriteAllTextAsync(path, script);
        await ProcessRunner.RunCommandAsync("chmod", $"+x \"{path}\"");
    }

    /// <summary>
    /// Writes usr/share/metainfo/&lt;app-id&gt;.metainfo.xml. The app id comes from
    /// options.AppId, falling back to a derived com.openmaui.* id.
    /// </summary>
    public async Task CreateMetainfoFile(string appDir, PackageOptions options, string execName)
    {
        var appId = options.AppId ?? $"com.openmaui.{AppStreamGenerator.SanitizeIdSegment(options.AppName)}";
        var metainfoDir = Path.Combine(appDir, "usr", "share", "metainfo");
        Directory.CreateDirectory(metainfoDir);

        var xml = AppStreamGenerator.Generate(
            appId,
            options.AppName,
            options.Comment,
            desktopFileId: $"{SanitizeFileName(options.AppName)}.desktop",
            binaryName: execName,
            developerName: options.Developer);

        var path = Path.Combine(metainfoDir, $"{appId}.metainfo.xml");
        await File.WriteAllTextAsync(path, xml);
        Console.WriteLine($"    {Path.GetFileName(path)}");
    }

    public static string GenerateAppRunScript(PackageOptions options, string execName,
        string? hostDepsCheckBlock = null)
    {
        var hostDepsSection = string.IsNullOrEmpty(hostDepsCheckBlock)
            ? ""
            : "\n" + hostDepsCheckBlock.TrimEnd('\n') + "\n";
        return $@"#!/bin/bash
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
{hostDepsSection}
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
Actions=Uninstall;

[Desktop Action Uninstall]
Name=Uninstall $APPIMAGE_NAME
Exec=$BIN_DIR/$APPIMAGE_BASENAME --uninstall
DESKTOP

    # Mark as installed
    echo ""$(date -Iseconds)"" > ""$INSTALLED_MARKER/$APPIMAGE_BASENAME""

    # Update desktop database and icon cache
    command -v update-desktop-database &> /dev/null && update-desktop-database ""$APPS_DIR"" 2>/dev/null
    command -v gtk-update-icon-cache &> /dev/null && gtk-update-icon-cache -f -t ""$HOME/.local/share/icons/hicolor"" 2>/dev/null
    # KDE Plasma keeps its own icon cache that ignores gtk-update-icon-cache;
    # Qt's icon loader rescans when theme directory mtimes change, so bump them.
    touch ""$HOME/.local/share/icons/hicolor"" ""$ICONS_DIR_SCALABLE"" ""$ICONS_DIR_256"" 2>/dev/null

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
                    --text=""$APPIMAGE_NAME has been installed.\n\nYou can find it in your application menu.\n\nNote: on KDE Plasma the icon may appear generic until your next login (Plasma caches icons; first install of a new icon only)."" \
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
                    --text=""$APPIMAGE_NAME has been installed.\n\nYou can find it in your application menu.\n\nNote: on KDE Plasma the icon may appear generic until your next login (Plasma caches icons; first install of a new icon only)."" \
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
    }

    public async Task CreateDesktopFile(string path, PackageOptions options)
    {
        await File.WriteAllTextAsync(path, GenerateDesktopFileContent(options));
    }

    public static string GenerateDesktopFileContent(PackageOptions options)
    {
        // WM_CLASS is app name without spaces or underscores (matches X11Window.cs)
        var wmClass = options.AppName.Replace(" ", "").Replace("_", "");
        return $@"[Desktop Entry]
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
    }

    public async Task SetupIcon(string appDir, PackageOptions options)
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

    public string? AutoDetectExecutable(string inputDir, string appName)
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
            catch (Exception ex)
            {
                Console.WriteLine($"  Warning: Could not read {fileName}: {ex.Message}");
            }
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

    public string? FindIcon(string inputDir, string execName)
    {
        // First, try to find and parse the .csproj to get MauiIcon
        var csprojIcon = TryGetIconFromCsproj(inputDir, execName);
        if (csprojIcon != null)
            return csprojIcon;

        // OpenMaui's build copies the MauiIcon file beside the app as
        // appicon_bg.* (the whole icon when there is no ForegroundFile, which
        // would be copied as appicon_fg.svg).
        if (!File.Exists(Path.Combine(inputDir, "appicon_fg.svg")))
        {
            foreach (var ext in new[] { ".svg", ".png" })
            {
                var layer = Path.Combine(inputDir, "appicon_bg" + ext);
                if (File.Exists(layer))
                    return layer;
            }
        }

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
        // The publish directory is typically: ProjectDir/bin/Release/net10.0/linux-arm64/publish
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

    public void CopyDirectory(string source, string destination)
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

    public static string SanitizeFileName(string name)
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
}
