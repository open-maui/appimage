# Changelog

All notable changes to OpenMaui.AppImage will be documented in this file.

## [1.2.3] - 2026-09-20

### Added

- **WPE WebKit and BlazorWebView awareness.** The dependency report lists WPE WebKit 2.54+ (`libWPEWebKit-2.0.so.1`) as the WebView's native-mode engine with WebKitGTK as the GTK-mode fallback, and promotes WPE to a required dependency when `OpenMaui.Controls.Linux.Blazor` is present. Fedora ships no WPE packages, so the report, the `--host-deps-check` launch message and the install commands carry the extra step (`sudo dnf copr enable philn/wpewebkit`) alongside `dnf install wpewebkit`; Debian/Ubuntu use `libwpewebkit-2.0-1`.

## [1.2.2] - 2026-09-19

> From packager to distribution tool: one-command packaging, automatic tooling, host-dependency intelligence, and release polish (self-update, signing, AppStream). Internally restructured with a full test suite.

### Added

- **One-command packaging.** `openmaui-appimage --project ./MyApp` runs `dotnet publish` itself (host-arch RID, `--rid` to override), locates the publish output, and derives `--name`/`--app-version` from the csproj when not given. `--output` defaults to `<Name>.AppImage` in this mode. `--input` and `--project` are mutually exclusive.
- **appimagetool auto-fetch.** When `appimagetool` is not on PATH, the official continuous build for the host architecture is downloaded once into `$XDG_CACHE_HOME/openmaui-appimage/` and reused (a notice with the URL is printed on first download; the continuous tag publishes no stable checksum, stated plainly). `--no-fetch` forbids network use for locked-down CI.
- **Host runtime dependency report.** The publish tree is scanned for OpenMaui feature assemblies and a concise report of required and feature-gated host libraries (with Fedora and Debian package names) prints after packaging — GStreamer for MediaElement, libcups for printing, appindicator for tray icons, webkit2gtk for WebView, and the base X11/Wayland/fontconfig set.
- **`--host-deps-check`.** Injects a small POSIX-sh probe into the generated AppRun: at launch, missing required libraries produce a friendly message (zenity/kdialog when available, stderr otherwise) with exact `dnf`/`apt` install commands and abort; missing feature-gated libraries only warn. Injected only when OpenMaui assemblies are detected.
- **Self-updating AppImages.** `--update-info <string>` embeds zsync update information via appimagetool `-u` (e.g. the `gh-releases-zsync|user|repo|latest|*.AppImage.zsync` shape).
- **Signing.** `--sign` (and `--sign-key <keyid>`, which implies `--sign`) pass through to appimagetool's GPG signing.
- **Right-click Uninstall.** The installed launcher entry now carries a desktop action, so right-clicking the app in the menu/taskbar offers "Uninstall <App>" directly (runs the AppImage's `--uninstall` flow).
- **AppStream metadata.** `--metainfo` generates `usr/share/metainfo/<app-id>.metainfo.xml` (desktop-application component, launchable, provides, optional `--developer`) for software-center listings; the app id is validated as reverse-DNS with a warning otherwise.


### Changed

- Install refreshes icon caches more aggressively (theme-directory mtime bump for Qt's loader) and the completion dialog notes KDE Plasma's first-install icon-cache behavior (a brand-new icon name may render generic until next login; Plasma-internal, not fixable by an installer).
- Bundled .NET runtime for Flatpak packaging updated 10.0.3 to 10.0.12 (both architectures, official checksums).
- Internal restructure: the single-file tool is now `Commands/` + `Core/` (AppDirBuilder, AppImagePacker, FlatpakPacker, ProjectPublisher, DependencyScanner, AppStreamGenerator, AppImageToolFetcher, ProcessRunner) with an identical CLI surface.
- New test suite: 103 tests covering desktop-file/AppRun generation, argument validation, FUSE detection, publish argument construction and csproj derivation, dependency scanning, the AppRun check block, AppStream generation, and packer argument passthrough.

## [1.1.1] and earlier

Single-command AppImage/Flatpak packager: `--appdir` support for .deb and FHS trees, FUSE-less operation via extract-and-run, executable and icon auto-detection, desktop-file generation with StartupWMClass, first-run installer dialog.
