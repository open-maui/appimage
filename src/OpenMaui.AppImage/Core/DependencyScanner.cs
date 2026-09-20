using System.Text;

namespace OpenMaui.AppImage.Core;

/// <summary>
/// A native host library an OpenMaui feature needs at runtime.
/// </summary>
/// <param name="DisplayName">Human-readable library/feature name.</param>
/// <param name="Sonames">Sonames to probe (any one satisfies the dependency).</param>
/// <param name="Required">True = the app cannot start without it; false = feature-gated.</param>
/// <param name="Feature">The feature the dependency enables (null for the required core set).</param>
/// <param name="FedoraPackages">Package name(s) on Fedora (dnf).</param>
/// <param name="DebianPackages">Package name(s) on Debian/Ubuntu (apt).</param>
public sealed record HostDependency(
    string DisplayName,
    string[] Sonames,
    bool Required,
    string? Feature,
    string FedoraPackages,
    string DebianPackages);

public sealed record DependencyScanResult(
    IReadOnlyList<string> DetectedAssemblies,
    IReadOnlyList<HostDependency> Dependencies,
    IReadOnlyList<string> Notes);

/// <summary>
/// Scans a publish/AppDir tree for OpenMaui feature assemblies and maps them to
/// the native libraries the host must provide (AppImages do not bundle them).
/// Also generates the optional AppRun launch-time check block (--host-deps-check).
/// </summary>
public static class DependencyScanner
{
    public const string BaseAssembly = "OpenMaui.Controls.Linux.dll";
    public const string MediaElementAssembly = "OpenMaui.Controls.Linux.MediaElement.dll";
    public const string MapsAssembly = "OpenMaui.Controls.Linux.Maps.dll";

    /// <summary>Scans <paramref name="rootDir"/> recursively and maps found assemblies.</summary>
    public static DependencyScanResult Scan(string rootDir)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(rootDir))
        {
            foreach (var file in Directory.EnumerateFiles(rootDir, "*.dll", SearchOption.AllDirectories))
                names.Add(Path.GetFileName(file));
        }
        return Map(names);
    }

    /// <summary>Pure assembly-name → host-dependency mapping (testable without a file tree).</summary>
    public static DependencyScanResult Map(ISet<string> assemblyFileNames)
    {
        var detected = new List<string>();
        var deps = new List<HostDependency>();
        var notes = new List<string>();

        if (assemblyFileNames.Contains(BaseAssembly))
        {
            detected.Add(BaseAssembly);

            // Required core set: the UI cannot come up without these.
            deps.Add(new HostDependency("libX11", new[] { "libX11.so.6" },
                Required: true, Feature: null, "libX11", "libx11-6"));
            deps.Add(new HostDependency("libwayland-client", new[] { "libwayland-client.so.0" },
                Required: true, Feature: null, "libwayland-client", "libwayland-client0"));
            deps.Add(new HostDependency("fontconfig", new[] { "libfontconfig.so.1" },
                Required: true, Feature: null, "fontconfig", "libfontconfig1"));

            // Optional, feature-gated.
            deps.Add(new HostDependency("libcups", new[] { "libcups.so.2" },
                Required: false, Feature: "printing", "cups-libs", "libcups2"));
            deps.Add(new HostDependency("appindicator", new[] { "libayatana-appindicator3.so.1", "libappindicator3.so.1" },
                Required: false, Feature: "tray icon", "libayatana-appindicator-gtk3", "libayatana-appindicator3-1"));
            deps.Add(new HostDependency("webkit2gtk-4.1", new[] { "libwebkit2gtk-4.1.so.0" },
                Required: false, Feature: "WebView", "webkit2gtk4.1", "libwebkit2gtk-4.1-0"));
        }

        if (assemblyFileNames.Contains(MediaElementAssembly))
        {
            detected.Add(MediaElementAssembly);
            deps.Add(new HostDependency("GStreamer 1.x", new[] { "libgstreamer-1.0.so.0" },
                Required: false, Feature: "MediaElement (audio/video playback)",
                "gstreamer1 gstreamer1-plugins-base gstreamer1-plugins-good",
                "libgstreamer1.0-0 gstreamer1.0-plugins-base gstreamer1.0-plugins-good"));
        }

        if (assemblyFileNames.Contains(MapsAssembly))
        {
            detected.Add(MapsAssembly);
            notes.Add("Maps (OpenMaui.Controls.Linux.Maps): no native host dependencies - needs network access only.");
        }

        return new DependencyScanResult(detected, deps, notes);
    }

    /// <summary>Concise "Host runtime dependencies" report printed after packaging.</summary>
    public static string FormatReport(DependencyScanResult result)
    {
        var sb = new StringBuilder();

        if (result.Dependencies.Count == 0 && result.Notes.Count == 0)
        {
            sb.AppendLine("Host runtime dependencies: none detected (no OpenMaui.Controls.Linux assemblies found).");
            return sb.ToString();
        }

        sb.AppendLine("Host runtime dependencies:");

        var required = result.Dependencies.Where(d => d.Required).ToList();
        var optional = result.Dependencies.Where(d => !d.Required).ToList();

        if (required.Count > 0)
        {
            sb.AppendLine("  Required:");
            foreach (var d in required)
                sb.AppendLine($"    {d.DisplayName} ({string.Join(" or ", d.Sonames)})  [Fedora: {d.FedoraPackages}] [Debian/Ubuntu: {d.DebianPackages}]");
        }

        if (optional.Count > 0)
        {
            sb.AppendLine("  Optional (feature-gated):");
            foreach (var d in optional)
                sb.AppendLine($"    {d.DisplayName} - {d.Feature} ({string.Join(" or ", d.Sonames)})  [Fedora: {d.FedoraPackages}] [Debian/Ubuntu: {d.DebianPackages}]");
        }

        foreach (var note in result.Notes)
            sb.AppendLine($"  Note: {note}");

        if (required.Count > 0)
        {
            sb.AppendLine($"  Install required:  Fedora:        sudo dnf install {string.Join(" ", required.Select(d => d.FedoraPackages))}");
            sb.AppendLine($"                     Debian/Ubuntu: sudo apt install {string.Join(" ", required.Select(d => d.DebianPackages))}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generates the POSIX-sh check block injected into AppRun by --host-deps-check.
    /// Probes sonames via ldconfig -p; missing required libs abort with a friendly
    /// message (zenity/kdialog when available, stderr otherwise) listing distro
    /// install commands; missing optional libs only print a note and continue.
    /// </summary>
    public static string GenerateAppRunCheckBlock(IReadOnlyList<HostDependency> dependencies)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# --- Host dependency check (generated by openmaui-appimage --host-deps-check) ---");
        sb.AppendLine("if command -v ldconfig >/dev/null 2>&1; then");
        sb.AppendLine("    OM_LDCACHE=$(ldconfig -p 2>/dev/null)");
        sb.AppendLine("    om_has() { case \"$OM_LDCACHE\" in *\"$1\"*) return 0 ;; esac; return 1; }");
        sb.AppendLine("    OM_MISSING_REQ=\"\"");
        sb.AppendLine("    OM_MISSING_OPT=\"\"");
        sb.AppendLine("    OM_REQ_DNF=\"\"");
        sb.AppendLine("    OM_REQ_APT=\"\"");

        foreach (var dep in dependencies)
        {
            var probe = string.Join(" || ", dep.Sonames.Select(s => $"om_has \"{s}\""));
            if (dep.Required)
            {
                sb.AppendLine($"    {probe} || {{ OM_MISSING_REQ=\"$OM_MISSING_REQ {dep.Sonames[0]}\"; OM_REQ_DNF=\"$OM_REQ_DNF {dep.FedoraPackages}\"; OM_REQ_APT=\"$OM_REQ_APT {dep.DebianPackages}\"; }}");
            }
            else
            {
                sb.AppendLine($"    {probe} || OM_MISSING_OPT=\"$OM_MISSING_OPT {dep.DisplayName} ({dep.Feature});\"");
            }
        }

        sb.AppendLine("    if [ -n \"$OM_MISSING_REQ\" ]; then");
        sb.AppendLine("        OM_MSG=\"$APPIMAGE_NAME cannot start - missing required system libraries:$OM_MISSING_REQ");
        sb.AppendLine();
        sb.AppendLine("Install them with:");
        sb.AppendLine("  Fedora:        sudo dnf install$OM_REQ_DNF");
        sb.AppendLine("  Debian/Ubuntu: sudo apt install$OM_REQ_APT\"");
        sb.AppendLine("        if command -v zenity >/dev/null 2>&1; then");
        sb.AppendLine("            zenity --error --title=\"$APPIMAGE_NAME\" --text=\"$OM_MSG\" --width=420 2>/dev/null");
        sb.AppendLine("        elif command -v kdialog >/dev/null 2>&1; then");
        sb.AppendLine("            kdialog --title \"$APPIMAGE_NAME\" --error \"$OM_MSG\" 2>/dev/null");
        sb.AppendLine("        fi");
        sb.AppendLine("        printf '%s\\n' \"$OM_MSG\" >&2");
        sb.AppendLine("        exit 1");
        sb.AppendLine("    fi");
        sb.AppendLine("    if [ -n \"$OM_MISSING_OPT\" ]; then");
        sb.AppendLine("        printf 'Note: optional host libraries missing (features disabled):%s\\n' \"$OM_MISSING_OPT\" >&2");
        sb.AppendLine("    fi");
        sb.AppendLine("fi");
        sb.AppendLine("# --- End host dependency check ---");
        return sb.ToString();
    }
}
