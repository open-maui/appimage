# OpenMaui.AppImage

Package .NET MAUI Linux apps as universal AppImages — one command from csproj to distributable.

[![NuGet](https://img.shields.io/nuget/v/OpenMaui.AppImage)](https://www.nuget.org/packages/OpenMaui.AppImage)
[![NuGet Downloads](https://img.shields.io/nuget/dt/OpenMaui.AppImage)](https://www.nuget.org/packages/OpenMaui.AppImage)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE.md)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![GitHub](https://img.shields.io/badge/GitHub-open--maui-181717?logo=github)](https://github.com/open-maui/appimage)

**Developed by [MarketAlly Pte Ltd](https://marketally.ai)**

> **AI assistants / automation:** follow [docs/PACKAGING-GUIDE.md](docs/PACKAGING-GUIDE.md) —
> a self-contained, copy-paste-exact reference (commands, options, recipes,
> verification, troubleshooting) intended to be consumed verbatim.

## What is AppImage?

AppImage is a universal Linux package format that allows you to distribute applications as a single executable file that works on most Linux distributions without installation.

## Features

- **One-command packaging** with `--project` — the tool runs `dotnet publish` itself and packages the result
- Package any .NET MAUI Linux app as an AppImage
- **Package a `.deb` or existing AppDir** with `--appdir` — turn any FHS tree (Tauri, Electron, deb packages) into an AppImage, no `linuxdeploy` required
- **appimagetool auto-fetch** — downloads and caches the official release when it isn't installed (`--no-fetch` to forbid network)
- **Works in CI / containers without FUSE** — auto-detects a missing `/dev/fuse` and runs `appimagetool` in extract-and-run mode
- **Auto-detection** of executable, icon, name, and version from your project
- **Host dependency intelligence** — prints the native libraries the target machine needs (GStreamer, webkit2gtk, …), and `--host-deps-check` adds a friendly launch-time check to the AppImage
- **Self-updating AppImages** (`--update-info`), **GPG signing** (`--sign`), and **AppStream metainfo** (`--metainfo`)
- Automatic `.desktop` file generation with proper `StartupWMClass` for taskbar integration
- Built-in installer dialog on first run
- Install/Reinstall/Uninstall support
- No root access required

## Prerequisites

1. **.NET 10 SDK** or later
2. **appimagetool** — *optional*: when it isn't found on the host, the tool automatically downloads the [official continuous release](https://github.com/AppImage/appimagetool/releases) into `~/.cache/openmaui-appimage/` and reuses it on later runs. A clear notice with the download URL is printed on first download (the continuous tag publishes no reliable checksum). Pass `--no-fetch` to forbid network access and install it yourself:

```bash
# Download
wget https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage

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
git clone https://github.com/open-maui/appimage.git
cd appimage
dotnet build
```

## Usage

### Quick Start — one command

```bash
openmaui-appimage --project ./MyApp
```

That's it. The tool:
1. Resolves the `.csproj` (a project directory or the file itself)
2. Runs `dotnet publish -c Release -r linux-x64 --self-contained true` (RID matches the host; override with `--rid`)
3. Derives `--name` from the csproj (`AssemblyName` or file name) and `--app-version` from its `Version` property
4. Packages the publish output as `<Name>.AppImage`
5. Prints a **Host runtime dependencies** report for the target machines

### Manual flow (pre-published apps)

```bash
cd YourMauiApp
dotnet publish -c Release -r linux-x64 --self-contained

openmaui-appimage \
    --input bin/Release/net10.0/linux-x64/publish \
    --output YourApp.AppImage \
    --name "Your App"
```

Either way, the tool automatically detects:
- **Executable**: Finds the main executable (ELF binary or .runtimeconfig.json)
- **Icon**: Reads `MauiIcon` from your `.csproj` and composites background + foreground

### Options

| Option | Short | Required | Description |
|--------|-------|----------|-------------|
| `--input` | `-i` | Yes* | Path to published .NET app directory (*or use `--project`) |
| `--project` | `-p` | Yes* | Path to a `.csproj` (or a directory containing one); the tool publishes it itself. Mutually exclusive with `--input`. |
| `--rid` | | No | RID for `dotnet publish` with `--project` (default: host arch, e.g. `linux-x64`) |
| `--output` | `-o` | With `--input` | Output AppImage file path (defaults to `<Name>.AppImage` with `--project`) |
| `--name` | `-n` | With `--input` | Application name (derived from the csproj with `--project`) |
| `--executable` | `-e` | No | Main executable name (auto-detected if not specified) |
| `--icon` | | No | Path to icon (auto-detected from MauiIcon if not specified) |
| `--category` | `-c` | No | Desktop category (default: Utility) |
| `--app-version` | | No | App version (default: 1.0.0, or the csproj `Version` with `--project`) |
| `--comment` | | No | App description |
| `--app-id` | | No | Reverse-DNS application id (e.g. `com.example.MyApp`), used for Flatpak and `--metainfo` |
| `--appdir` | | No | Treat `--input` as a pre-structured AppDir / FHS tree (e.g. a directory extracted from a `.deb`: `usr/bin`, `usr/lib`, `usr/share`) instead of a flat publish dir. See [Packaging a .deb or existing AppDir](#packaging-a-deb-or-existing-appdir). |
| `--no-fuse` | | No | Always run `appimagetool` in extract-and-run mode instead of FUSE-mounting. FUSE is auto-detected by default; use this when FUSE is present but broken. |
| `--no-fetch` | | No | Forbid network access: never auto-download `appimagetool` |
| `--host-deps-check` | | No | Inject a launch-time host-library check into AppRun. See [Host runtime dependencies](#host-runtime-dependencies). |
| `--update-info` | | No | appimagetool update information (`-u`) for zsync self-updating AppImages. See [Self-updating AppImages](#self-updating-appimages-zsync). |
| `--sign` | | No | GPG-sign the AppImage (appimagetool `--sign`) |
| `--sign-key` | | No | GPG key id to sign with (implies `--sign`) |
| `--metainfo` | | No | Generate AppStream metainfo.xml. See [AppStream metainfo](#appstream-metainfo). |
| `--developer` | | No | Developer name for the AppStream metainfo |

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

> Note (KDE Plasma): the very first install of a new app icon may show a
> generic icon in the launcher/taskbar until the next login — Plasma caches
> icon lookups in-memory and only rescans for brand-new icon names on shell
> restart. Launch feedback and subsequent updates are unaffected.

# Uninstall
./YourApp.AppImage --uninstall

# Show help
./YourApp.AppImage --help
```

## Example: Packaging ShellDemo

```bash
# One command — publish + package
openmaui-appimage --project maui-linux-samples/ShellDemo

# Or the manual flow
cd maui-linux-samples/ShellDemo
dotnet publish -c Release -r linux-x64 --self-contained
openmaui-appimage \
    -i bin/Release/net10.0/linux-x64/publish \
    -o ShellDemo.AppImage \
    -n "Shell Demo"
```

## Host runtime dependencies

AppImages bundle your app but not the host's system libraries. After packaging, the
tool scans the publish tree for OpenMaui feature assemblies and prints a concise
**Host runtime dependencies** report — what the *target* machine needs:

- `OpenMaui.Controls.Linux.dll` → required base set: **libX11**, **libwayland-client**,
  **fontconfig**; plus optional, feature-gated libraries: **libcups** (printing),
  **libayatana-appindicator3**/**libappindicator3** (tray icon), **webkit2gtk-4.1** (WebView)
- `OpenMaui.Controls.Linux.MediaElement.dll` → **GStreamer 1.x** + base/good plugins
- `OpenMaui.Controls.Linux.Maps.dll` → nothing native (network access only)

Each entry lists the Fedora (`dnf`) and Debian/Ubuntu (`apt`) package names.

### `--host-deps-check` — launch-time check

Pass `--host-deps-check` to inject a small, dependency-free POSIX-sh block into the
generated `AppRun`. At launch it probes the needed sonames via `ldconfig -p`:

- **Required base set missing** → shows a friendly message (zenity or kdialog when
  available, stderr otherwise) listing the exact `dnf`/`apt` install commands, then aborts
- **Optional feature libraries missing** → prints a note to stderr and continues
  (the corresponding features are simply unavailable)

```bash
openmaui-appimage --project ./MyApp --host-deps-check
```

## Self-updating AppImages (zsync)

Pass `--update-info` to embed update information (appimagetool `-u`), producing a
`.zsync` file next to the AppImage. Tools like `AppImageUpdate` can then update the
app by downloading only the changed blocks. The common shape for GitHub releases is:

```bash
openmaui-appimage --project ./MyApp \
    --update-info "gh-releases-zsync|myuser|myrepo|latest|MyApp-*.AppImage.zsync"
```

Upload both the produced `.AppImage` and `.zsync` files to your release.

## Signing

```bash
# Sign with your default GPG key
openmaui-appimage --project ./MyApp --sign

# Sign with a specific key (implies --sign)
openmaui-appimage --project ./MyApp --sign-key ABCDEF1234567890
```

These are passed straight through to appimagetool (`--sign` / `--sign-key`), which
embeds a GPG signature into the AppImage.

## AppStream metainfo

`--metainfo` generates a minimal, valid AppStream file at
`AppDir/usr/share/metainfo/<app-id>.metainfo.xml` (component
`type="desktop-application"`, with `launchable`, `provides/binary`, and the summary
from `--comment`), which app stores and software centers read:

```bash
openmaui-appimage --project ./MyApp --metainfo \
    --app-id com.example.MyApp \
    --comment "A great app" \
    --developer "Example Inc."
```

The `--app-id` should be reverse-DNS (`com.example.MyApp`); the tool warns when it
isn't, and derives a `com.openmaui.*` fallback id when the option is omitted.

## Packaging a .deb or existing AppDir

By default the tool expects a **flat publish directory** and copies it into
`AppDir/usr/bin`. That's perfect for `dotnet publish` output, but some apps —
Tauri, Electron, anything you already have as a `.deb` — are laid out as an **FHS
tree** (`usr/bin`, `usr/lib`, `usr/share`). Pass `--appdir` to package one of those
directly: the tree is used as the AppDir root (so resources under `usr/lib` /
`usr/share` are preserved) and the executable is resolved under `usr/bin`.

This turns the tool into a **`.deb` → AppImage** converter — useful for shipping an
AppImage from a bundler that only emits a `.deb`, without pulling in `linuxdeploy`:

```bash
# Extract the .deb into an AppDir tree
dpkg-deb -x YourApp_1.0.0_amd64.deb appdir

# Package it (executable auto-detected under usr/bin, or pass --executable)
openmaui-appimage \
    --appdir \
    -i appdir \
    -o YourApp.AppImage \
    -n "Your App" \
    --executable your-app \
    --icon path/to/icon.svg
```

> Note: like any AppImage, this bundles your app but **not** the host's shared GUI
> libraries (GTK/WebKit/etc.). A self-contained app (or one whose deb already vendors
> its libs) is fully portable; otherwise the target needs those system libraries —
> the same dependency the `.deb` declares.

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
By default the tool downloads appimagetool automatically (cached in
`~/.cache/openmaui-appimage/`). If you passed `--no-fetch` or the download failed,
install it as described in Prerequisites.

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

**Build time (this tool):** handled automatically. `appimagetool` is itself an
AppImage and needs FUSE to run, which is missing on many CI runners and LXC
containers. The tool probes `/dev/fuse` and, when it's unavailable, sets
`APPIMAGE_EXTRACT_AND_RUN=1` so `appimagetool` extracts-and-runs instead of
mounting — so packaging works in containers/CI with no extra flags. (It sets the
env var rather than passing `--appimage-extract-and-run`, so a distro-packaged
native `appimagetool` also works.) If FUSE is *present but broken* (auto-detect
would try to mount and fail), pass **`--no-fuse`** to force extract-and-run.

**Run time (the produced AppImage):** the end user's machine still needs FUSE to
*mount* the AppImage. Install it, or run extracted:
```bash
sudo apt install fuse libfuse2  # Debian/Ubuntu
sudo dnf install fuse fuse-libs  # Fedora

# or, no FUSE needed:
./YourApp.AppImage --appimage-extract-and-run
```

## Development

```bash
# Build the solution
dotnet build

# Run the tests
dotnet test
```

The CLI lives in `src/OpenMaui.AppImage` (`Program.cs` is the entry point,
`Commands/` holds the command definition, `Core/` the packaging logic), with unit
tests in `tests/OpenMaui.AppImage.Tests`.

## License

MIT License - see [LICENSE](LICENSE)
