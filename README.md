# OpenMaui.AppImage

A CLI tool to package .NET MAUI Linux applications as AppImages.

## What is AppImage?

AppImage is a universal Linux package format that allows you to distribute applications as a single executable file that works on most Linux distributions without installation.

## Features

- Package any .NET MAUI Linux app as an AppImage
- Automatic `.desktop` file generation
- Icon embedding
- Built-in install/uninstall support
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
dotnet publish -c Release -r linux-x64 --self-contained false
```

### 2. Create the AppImage

```bash
openmaui-appimage \
    --input bin/Release/net9.0/linux-x64/publish \
    --output YourApp.AppImage \
    --name "Your App" \
    --executable YourMauiApp \
    --icon path/to/icon.png \
    --category Utility \
    --version 1.0.0
```

### Options

| Option | Short | Required | Description |
|--------|-------|----------|-------------|
| `--input` | `-i` | Yes | Path to published .NET app directory |
| `--output` | `-o` | Yes | Output AppImage file path |
| `--name` | `-n` | Yes | Application name |
| `--executable` | `-e` | No | Main executable name (defaults to app name) |
| `--icon` | | No | Path to icon (PNG or SVG) |
| `--category` | `-c` | No | Desktop category (default: Utility) |
| `--version` | `-v` | No | App version (default: 1.0.0) |
| `--comment` | | No | App description |

### Desktop Categories

Common categories: `Utility`, `Development`, `Game`, `Graphics`, `Network`, `Office`, `AudioVideo`, `System`

## Running the AppImage

```bash
# Make executable (first time only)
chmod +x YourApp.AppImage

# Run
./YourApp.AppImage

# Install to system (adds to app menu)
./YourApp.AppImage --install

# Uninstall
./YourApp.AppImage --uninstall
```

## Example: Packaging ShellDemo

```bash
# Build and publish
cd maui-linux-samples/ShellDemo
dotnet publish -c Release -r linux-x64 --self-contained false

# Create AppImage
openmaui-appimage \
    -i bin/Release/net9.0/linux-x64/publish \
    -o ShellDemo.AppImage \
    -n "OpenMaui Shell Demo" \
    -e ShellDemo \
    --icon Resources/Images/logo_only.svg \
    -c Utility \
    -v 1.0.0 \
    --comment "OpenMaui Controls Demonstration"
```

## How It Works

1. **AppDir Structure**: Creates the standard AppImage directory structure:
   ```
   YourApp.AppDir/
   ├── AppRun              # Entry point script
   ├── YourApp.desktop     # Desktop integration
   ├── YourApp.svg         # Application icon
   └── usr/
       └── bin/            # Your published .NET app
   ```

2. **AppRun Script**: A bash script that:
   - Sets up the environment (PATH, LD_LIBRARY_PATH)
   - Handles `--install` and `--uninstall` flags
   - Launches the .NET application via `dotnet`

3. **appimagetool**: Packages everything into a single executable AppImage file

## Self-Contained vs Framework-Dependent

### Framework-Dependent (smaller, requires .NET runtime)
```bash
dotnet publish -c Release -r linux-x64 --self-contained false
```
- Smaller AppImage (~50-100MB smaller)
- Requires .NET runtime on target system

### Self-Contained (larger, no dependencies)
```bash
dotnet publish -c Release -r linux-x64 --self-contained true
```
- Larger AppImage
- Works on any Linux system without .NET installed
- Recommended for distribution

## Troubleshooting

### "appimagetool not found"
Install appimagetool as described in Prerequisites.

### "Could not find executable"
Use `--executable` to specify the correct DLL name (without .dll extension).

### AppImage won't run
1. Ensure it's executable: `chmod +x YourApp.AppImage`
2. Check for missing dependencies: `./YourApp.AppImage` (errors will be shown)

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
