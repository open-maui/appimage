# Packaging OpenMaui Linux apps with OpenMaui.AppImage

Reference for packaging a .NET MAUI Linux (OpenMaui) application as an AppImage.
Written to be followed verbatim by a human or an AI coding assistant working
inside a consumer project. Every command is copy-paste exact. Version: tool 1.2.3.

## TL;DR — the golden path

```bash
dotnet tool install --global OpenMaui.AppImage   # once per machine
openmaui-appimage --project ./MyApp              # from the solution root
```

That single command: runs `dotnet publish -c Release -r linux-x64 --self-contained true`
on the project, derives the app name and version from the csproj, builds the AppDir,
downloads `appimagetool` into `~/.cache/openmaui-appimage/` if it is not on PATH,
and produces `MyApp.AppImage` in the current directory.

Do NOT hand-write AppDir trees, call `appimagetool` directly, or run
`dotnet publish` separately unless you need the two-step flow below — the tool
owns those steps and gets the details (AppRun, desktop file, icon, installer,
StartupWMClass) right.

## Install / verify

```bash
dotnet tool install --global OpenMaui.AppImage
openmaui-appimage --help        # confirms installation; full option list
```

Update with `dotnet tool update --global OpenMaui.AppImage`.

## Command reference

`--project` and `--input` are mutually exclusive; exactly one is required.

| Option | Meaning | Default |
|---|---|---|
| `--project`, `-p <path>` | Csproj (or directory containing exactly one). The tool publishes it and packages the output. | — |
| `--rid <rid>` | Runtime identifier for publish. | host arch (`linux-x64`/`linux-arm64`) |
| `--input`, `-i <dir>` | Pre-published output directory (two-step flow). | — |
| `--output`, `-o <file>` | Output AppImage path. | `<Name>.AppImage` (with `--project`); required with `--input` |
| `--name <name>` | Application display name. | derived from csproj with `--project`; required with `--input` |
| `--app-version <v>` | Version stamped into the AppImage. | derived from csproj `Version` |
| `--exec <name>` | Executable name inside the publish output. | auto-detected |
| `--icon <file>` | Icon (SVG preferred, PNG/ICO accepted). | auto-detected from project (MauiIcon) |
| `--category <cat>` | Freedesktop menu category. | `Utility` |
| `--comment <text>` | Desktop-entry comment / AppStream summary. | generic |
| `--app-id <id>` | Reverse-DNS id (e.g. `com.example.myapp`). | derived (warned) |
| `--format <appimage\|flatpak>` | Output format. | `appimage` |
| `--appdir` | Treat `--input` as a ready FHS tree (deb contents, Tauri/Electron dirs). | off |
| `--no-fuse` | Force appimagetool extract-and-run (containers/CI without `/dev/fuse`). Auto-detected anyway. | auto |
| `--no-fetch` | Forbid network (no appimagetool auto-download). Fails if appimagetool is absent. | off |
| `--host-deps-check` | Inject a launch-time host-library check into AppRun (see below). | off |
| `--update-info <s>` | Embed zsync update info (appimagetool `-u`), e.g. `gh-releases-zsync\|user\|repo\|latest\|*.AppImage.zsync`. | off |
| `--sign` / `--sign-key <id>` | GPG-sign the AppImage (`--sign-key` implies `--sign`). | off |
| `--metainfo` | Generate AppStream `usr/share/metainfo/<app-id>.metainfo.xml`. Use with a real `--app-id`. | off |
| `--developer <name>` | Developer name for AppStream metadata. | — |

Exit code 0 = success (the final line prints `AppImage created successfully: <path>`);
non-zero = failure with the reason on stderr.

## Recipes

### Local development build
```bash
openmaui-appimage --project ./MyApp
```

### Release build with everything
```bash
openmaui-appimage --project ./MyApp \
  --app-id com.example.myapp \
  --comment "Short one-line description" \
  --category Utility \
  --metainfo --developer "Example Corp" \
  --host-deps-check \
  --update-info "gh-releases-zsync|example|myapp|latest|MyApp-*.AppImage.zsync" \
  --sign
```

### CI (no network for tooling, no FUSE)
Preinstall appimagetool in the runner image, then:
```bash
openmaui-appimage --project ./MyApp --no-fetch --no-fuse -o out/MyApp.AppImage
```
`--no-fuse` is auto-detected when `/dev/fuse` is absent; passing it explicitly is harmless.

### Two-step flow (custom publish arguments)
```bash
dotnet publish ./MyApp -c Release -r linux-x64 --self-contained true -p:SomeFlag=true
openmaui-appimage -i ./MyApp/bin/Release/net10.0/linux-x64/publish -n "MyApp" -o MyApp.AppImage
```

### Package an existing .deb / FHS tree
```bash
dpkg-deb -x package.deb tree/
openmaui-appimage -i tree/ --appdir -n "MyApp" --exec myapp -o MyApp.AppImage
```

## What the produced AppImage does

- First run shows an install dialog (Install / run once). Install copies the
  AppImage to `~/.local/bin/`, writes a launcher entry and icon, and registers
  an uninstall action.
- `./MyApp.AppImage --install`, `--uninstall`, `--help` are built in.
- The installed launcher entry has a right-click "Uninstall" action.
- Uninstall removes everything it installed.

## Host runtime dependencies

AppImages bundle the app and .NET, NOT desktop system libraries. After packaging,
the tool prints a "Host runtime dependencies" report for OpenMaui apps:
required (libX11, libwayland-client, fontconfig — present on any desktop distro)
and feature-gated (GStreamer for MediaElement, libcups for printing,
libayatana-appindicator for tray icons, WPE WebKit 2.54+ for the WebView in
native mode with webkit2gtk-4.1 as the GTK-mode fallback), with Fedora and
Debian package names. An app that ships `OpenMaui.Controls.Linux.Blazor`
(BlazorWebView) makes WPE WebKit a required dependency.

WPE on Fedora is not in the official repositories; the report and the launch-time
check print the extra step: `sudo dnf copr enable philn/wpewebkit` before
`sudo dnf install wpewebkit`. Debian/Ubuntu: `sudo apt install libwpewebkit-2.0-1`.

`--host-deps-check` bakes a launch-time check into the AppImage: missing
required libraries produce a dialog/stderr message with the exact
`dnf`/`apt` command and abort; missing feature-gated libraries only warn.
Recommended for apps distributed to end users.

## Verification steps (run after packaging)

```bash
test -x MyApp.AppImage && echo executable
./MyApp.AppImage --help                        # AppRun responds without installing
./MyApp.AppImage --appimage-extract >/dev/null && ls squashfs-root/  # inspect contents
rm -rf squashfs-root
```

Launching the app itself requires a display; do not attempt in headless CI.

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| `appimagetool not found` with `--no-fetch` | Install appimagetool on PATH, or drop `--no-fetch`. |
| dlopen/FUSE errors running the AppImage in a container | Run the AppImage with `--appimage-extract-and-run`, or install `fuse3`. |
| `--project` fails: multiple csproj files | Pass the csproj file path, not the directory. |
| NU1900 warnings during publish | Unreachable NuGet feed during vulnerability lookup; harmless. |
| Package downgrade NU1605 SkiaSharp | The app pins an older SkiaSharp than OpenMaui requires; align the app's pin with the OpenMaui packages. |
| Icon generic in KDE launcher after FIRST install | Plasma caches icon lookups in-memory; shows after next login or `systemctl --user restart plasma-plasmashell.service`. First install of a new icon name only; not fixable by an installer. |
| App crashes only inside AppImage, works from publish dir | Check the host-deps report; run `./MyApp.AppImage --appimage-extract` and diff against the publish dir. |

## Rules for AI assistants

- Prefer `--project`; fall back to `--input` only when custom publish flags are needed.
- Never call `appimagetool` or write AppRun/.desktop files manually for OpenMaui apps.
- Never bundle GStreamer/webkit2gtk into the AppImage; they are host dependencies by design — use `--host-deps-check` instead.
- Use `--app-id` (reverse-DNS) whenever `--metainfo` is used.
- Treat a missing final `AppImage created successfully:` line as failure even if the exit code was swallowed by a shell pipeline.
