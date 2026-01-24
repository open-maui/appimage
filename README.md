# OpenMaui.AppImage

A CLI tool to package .NET MAUI Linux applications as AppImages.

## What is AppImage?

AppImage is a universal Linux package format that allows you to distribute applications as a single executable file that works on most Linux distributions without installation.

## Features

- Package any .NET MAUI Linux app as an AppImage
- **Auto-detection** of executable and icon from your project
- Automatic `.desktop` file generation with proper `StartupWMClass` for taskbar integration
- Built-in installer dialog on first run
- Install/Reinstall/Uninstall support
- No root access required

## Prerequisites

1. **.NET 9 SDK** or later
2. **appimagetool** - Download from [AppImageKit releases](https://github.com/AppImage/AppImageKit/releases)

### Installing appimagetool

```bash
# Download
wget https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage

# Make executable
chmod +x appimagetool-x86_64.AppImage

# Move to PATH
sudo mv appimagetool-x86_64.AppImage /usr/local/bin/appimagetool
```

## Installation

### As a .NET tool (recommended)

```bash
dotnet tool install --global OpenMaui.AppImage
```

### From source

```bash
git clone https://github.com/AuroraNetworks/openmaui-appimage.git
cd openmaui-appimage
dotnet build
```

## Usage

### 1. Publish your MAUI app

```bash
cd YourMauiApp
dotnet publish -c Release -r linux-x64 --self-contained
```

### 2. Create the AppImage

```bash
openmaui-appimage \
    --input bin/Release/net9.0/linux-x64/publish \
    --output YourApp.AppImage \
    --name "Your App"
```

That's it! The tool automatically detects:
- **Executable**: Finds the main executable (ELF binary or .runtimeconfig.json)
- **Icon**: Reads `MauiIcon` from your `.csproj` and composites background + foreground

### Options

| Option | Short | Required | Description |
|--------|-------|----------|-------------|
| `--input` | `-i` | Yes | Path to published .NET app directory |
| `--output` | `-o` | Yes | Output AppImage file path |
| `--name` | `-n` | Yes | Application name |
| `--executable` | `-e` | No | Main executable name (auto-detected if not specified) |
| `--icon` | | No | Path to icon (auto-detected from MauiIcon if not specified) |
| `--category` | `-c` | No | Desktop category (default: Utility) |
| `--app-version` | | No | App version (default: 1.0.0) |
| `--comment` | | No | App description |

### Desktop Categories

Common categories: `Utility`, `Development`, `Game`, `Graphics`, `Network`, `Office`, `AudioVideo`, `System`

## Running the AppImage

### First Run - Installer Dialog

When you run an AppImage for the first time, a dialog appears:

- **Install**: Copies to `~/.local/bin`, creates menu entry and icon
- **Run Only**: Runs the app without installing

### Already Installed

If you run the AppImage again from a different location (e.g., Desktop), you'll see options:

- **Run the application**: Just runs it
- **Reinstall (update)**: Updates the installed version
- **Uninstall**: Removes the app from your system

### From the Launcher

Once installed, clicking the app in your application menu runs it directly (no dialog).

### Command Line Flags

```bash
# Run normally (shows dialog if not installed)
./YourApp.AppImage

# Force install dialog
./YourApp.AppImage --install

# Uninstall
./YourApp.AppImage --uninstall

# Show help
./YourApp.AppImage --help
```

## Example: Packaging ShellDemo

```bash
# Build and publish
cd maui-linux-samples/ShellDemo
dotnet publish -c Release -r linux-x64 --self-contained

# Create AppImage (icon auto-detected from MauiIcon in csproj)
openmaui-appimage \
    -i bin/Release/net9.0/linux-x64/publish \
    -o ShellDemo.AppImage \
    -n "Shell Demo"
```

## How It Works

1. **AppDir Structure**: Creates the standard AppImage directory structure:
   ```
   YourApp.AppDir/
   ├── AppRun              # Entry point script
   ├── YourApp.desktop     # Desktop integration
   ├── YourApp.svg         # Application icon
   └── usr/
       ├── bin/            # Your published .NET app
       │   └── appicon.svg # Runtime icon for window
       └── share/icons/    # XDG icon directories
   ```

2. **AppRun Script**: A bash script that:
   - Sets up the environment (PATH, LD_LIBRARY_PATH)
   - Shows installer dialog on first run
   - Handles `--install`, `--uninstall`, and `--help` flags
   - Launches the .NET application

3. **Desktop Integration**:
   - Creates `.desktop` file with `StartupWMClass` for proper taskbar icon matching
   - Installs icon to XDG icon directories
   - Updates icon cache automatically

4. **appimagetool**: Packages everything into a single executable AppImage file

## Self-Contained vs Framework-Dependent

### Self-Contained (recommended for distribution)
```bash
dotnet publish -c Release -r linux-x64 --self-contained
```
- Works on any Linux system without .NET installed
- Larger AppImage size

### Framework-Dependent (smaller size)
```bash
dotnet publish -c Release -r linux-x64 --self-contained false
```
- Smaller AppImage (~50-100MB smaller)
- Requires .NET runtime on target system

## Troubleshooting

### "appimagetool not found"
Install appimagetool as described in Prerequisites.

### "Could not find executable"
The auto-detection looks for:
1. ELF binaries (self-contained apps)
2. `.runtimeconfig.json` files (framework-dependent apps)

If it fails, use `--executable` to specify the correct name (without extension).

### AppImage won't run
1. Ensure it's executable: `chmod +x YourApp.AppImage`
2. Check for missing dependencies: `./YourApp.AppImage` (errors will be shown)

### Taskbar icon not showing
The app sets `WM_CLASS` and the `.desktop` file includes `StartupWMClass` for proper matching. If issues persist:
1. Reinstall the app to update the `.desktop` file
2. Log out and back in to refresh the desktop environment

### FUSE errors
Some systems require FUSE to mount AppImages. Install with:
```bash
sudo apt install fuse libfuse2  # Debian/Ubuntu
sudo dnf install fuse fuse-libs  # Fedora
```

Or extract and run:
```bash
./YourApp.AppImage --appimage-extract
./squashfs-root/AppRun
```

## License

MIT License - see [LICENSE](LICENSE)
