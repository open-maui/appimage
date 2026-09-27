using System.Text;
using System.Text.Json;

namespace OpenMaui.AppImage.Core;

/// <summary>How the packaged .NET app gets its runtime.</summary>
public enum DotNetRuntimeKind
{
    /// <summary>The publish output carries the runtime (libcoreclr.so present).</summary>
    SelfContained,
    /// <summary>The app needs a shared dotnet runtime on the host.</summary>
    FrameworkDependent,
    /// <summary>No .NET runtime markers found (e.g. a non-.NET app).</summary>
    None
}

/// <summary>Target CPU architecture in Debian and RPM spelling.</summary>
/// <param name="Debian">dpkg architecture (amd64, arm64, armhf, all).</param>
/// <param name="Rpm">rpm architecture (x86_64, aarch64, armv7hl, noarch).</param>
public sealed record PackageArchitecture(string Debian, string Rpm)
{
    public static readonly PackageArchitecture X64 = new("amd64", "x86_64");
    public static readonly PackageArchitecture Arm64 = new("arm64", "aarch64");
    public static readonly PackageArchitecture Arm = new("armhf", "armv7hl");
    public static readonly PackageArchitecture Independent = new("all", "noarch");

    public bool IsIndependent => Debian == "all";
}

/// <summary>
/// Everything the .deb and .rpm writers need to describe the package: names,
/// versions, architecture, install layout and descriptive fields. Resolution
/// helpers are pure/static so they are unit-testable.
/// </summary>
public sealed record NativePackageMetadata
{
    /// <summary>Package name (Debian/RPM-safe, lowercase), e.g. "shelldemo".</summary>
    public required string PackageName { get; init; }

    /// <summary>Human-readable application name, e.g. "Shell Demo".</summary>
    public required string AppName { get; init; }

    /// <summary>Reverse-DNS application id; names the /opt directory, desktop file and icon.</summary>
    public required string AppId { get; init; }

    /// <summary>Main executable (file name inside the publish output, without .dll).</summary>
    public required string ExecutableName { get; init; }

    /// <summary>Upstream version as given (csproj / --app-version).</summary>
    public required string Version { get; init; }

    /// <summary>Package release / Debian revision (default "1").</summary>
    public string Release { get; init; } = "1";

    public required PackageArchitecture Architecture { get; init; }
    public required DotNetRuntimeKind Runtime { get; init; }

    /// <summary>Shared-runtime major.minor for framework-dependent apps (e.g. "10.0").</summary>
    public string? FrameworkVersion { get; init; }

    /// <summary>"Name &lt;email&gt;".</summary>
    public required string Maintainer { get; init; }

    public required string License { get; init; }
    public string? Homepage { get; init; }

    /// <summary>One-line summary (Debian synopsis / RPM Summary).</summary>
    public required string Summary { get; init; }

    /// <summary>Longer description; may be empty.</summary>
    public string? Description { get; init; }

    public required string Category { get; init; }

    public string InstallDir => $"/opt/{AppId}";
    public string LauncherPath => $"/usr/bin/{PackageName}";
    public string DesktopFileName => $"{AppId}.desktop";

    /// <summary>Debian version: upstream + "-" + revision.</summary>
    public string DebianVersion => $"{ToDebianUpstreamVersion(Version)}-{Release}";

    /// <summary>RPM Version tag (no hyphens).</summary>
    public string RpmVersion => ToRpmVersion(Version);

    public string DebFileName => $"{PackageName}_{DebianVersion}_{Architecture.Debian}.deb";
    public string RpmFileName => $"{PackageName}-{RpmVersion}-{Release}.{Architecture.Rpm}.rpm";

    public const string DefaultMaintainerEmail = "noreply@localhost";

    /// <summary>
    /// Debian/RPM package name from an application name: lowercase, only
    /// [a-z0-9+.-], starting with an alphanumeric, at least two characters.
    /// </summary>
    public static string SanitizePackageName(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name.ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9' or '+' or '.')
                sb.Append(c);
            else if (c is '-' or '_' or ' ')
            {
                if (sb.Length > 0 && sb[^1] != '-')
                    sb.Append('-');
            }
        }

        var result = sb.ToString().Trim('-', '.', '+');
        if (result.Length == 0)
            result = "app";
        if (result.Length < 2)
            result += "-app";
        return result;
    }

    /// <summary>
    /// Debian upstream version: a leading "v" is dropped, a pre-release
    /// suffix ("1.0.0-beta.1") becomes "~" so it sorts before the release,
    /// disallowed characters become ".", and a non-digit start is prefixed with "0~".
    /// </summary>
    public static string ToDebianUpstreamVersion(string version)
    {
        var v = NormalizeVersionPrefix(version);
        var sb = new StringBuilder();
        var seenPreRelease = false;
        foreach (var c in v)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '.' or '+' or '~')
                sb.Append(c);
            else if (c == '-' && !seenPreRelease)
            {
                sb.Append('~');
                seenPreRelease = true;
            }
            else
                sb.Append('.');
        }

        var result = sb.ToString();
        if (result.Length == 0)
            return "0";
        return char.IsAsciiDigit(result[0]) ? result : "0~" + result;
    }

    /// <summary>
    /// RPM Version tag: no hyphens (a pre-release suffix becomes "~", which
    /// rpm sorts before the release), only [A-Za-z0-9._+~^].
    /// </summary>
    public static string ToRpmVersion(string version)
    {
        var v = NormalizeVersionPrefix(version);
        var sb = new StringBuilder();
        var seenPreRelease = false;
        foreach (var c in v)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '+' or '~' or '^')
                sb.Append(c);
            else if (c == '-' && !seenPreRelease)
            {
                sb.Append('~');
                seenPreRelease = true;
            }
            else
                sb.Append('.');
        }
        return sb.Length == 0 ? "0" : sb.ToString();
    }

    private static string NormalizeVersionPrefix(string version)
    {
        var v = version.Trim();
        if (v.Length > 1 && (v[0] == 'v' || v[0] == 'V') && char.IsAsciiDigit(v[1]))
            v = v[1..];
        return v;
    }

    /// <summary>Safe single path segment for the /opt directory and icon/desktop names.</summary>
    public static string SanitizeAppId(string appId)
    {
        var sb = new StringBuilder();
        foreach (var c in appId)
            sb.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_');
        var result = sb.ToString().Trim('.');
        return result.Length == 0 ? "app" : result;
    }

    /// <summary>Maps a .NET runtime identifier to package architectures (null when unknown).</summary>
    public static PackageArchitecture? ArchitectureFromRid(string? rid)
    {
        return rid switch
        {
            "linux-x64" or "linux-musl-x64" => PackageArchitecture.X64,
            "linux-arm64" or "linux-musl-arm64" => PackageArchitecture.Arm64,
            "linux-arm" or "linux-musl-arm" => PackageArchitecture.Arm,
            _ => null
        };
    }

    /// <summary>
    /// Maps an ELF e_machine value to package architectures (null when the file
    /// is not ELF or the machine is not one we package for).
    /// </summary>
    public static PackageArchitecture? ArchitectureFromElf(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var header = new byte[20];
            if (fs.Read(header, 0, header.Length) < header.Length)
                return null;
            if (header[0] != 0x7F || header[1] != 'E' || header[2] != 'L' || header[3] != 'F')
                return null;

            // e_machine at offset 18; EI_DATA (offset 5) 1 = little-endian
            var machine = header[5] == 1
                ? header[18] | (header[19] << 8)
                : (header[18] << 8) | header[19];
            return machine switch
            {
                0x3E => PackageArchitecture.X64,   // EM_X86_64
                0xB7 => PackageArchitecture.Arm64, // EM_AARCH64
                0x28 => PackageArchitecture.Arm,   // EM_ARM
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Detects the package architecture from the native files of a publish
    /// directory: the main executable, then libcoreclr.so, then any top-level .so.
    /// A tree without native code is architecture-independent.
    /// </summary>
    public static PackageArchitecture DetectArchitecture(string publishDir, string execName)
    {
        var candidates = new List<string>
        {
            Path.Combine(publishDir, execName),
            Path.Combine(publishDir, "libcoreclr.so")
        };
        if (Directory.Exists(publishDir))
            candidates.AddRange(Directory.GetFiles(publishDir, "*.so").OrderBy(f => f, StringComparer.Ordinal));

        foreach (var file in candidates)
        {
            if (!File.Exists(file))
                continue;
            var arch = ArchitectureFromElf(file);
            if (arch != null)
                return arch;
        }
        return PackageArchitecture.Independent;
    }

    /// <summary>Self-contained when the publish output carries libcoreclr.so; framework-dependent when only a runtimeconfig is present.</summary>
    public static DotNetRuntimeKind DetectRuntime(string publishDir, string execName)
    {
        if (File.Exists(Path.Combine(publishDir, "libcoreclr.so")))
            return DotNetRuntimeKind.SelfContained;
        if (File.Exists(Path.Combine(publishDir, $"{execName}.runtimeconfig.json")) ||
            File.Exists(Path.Combine(publishDir, $"{execName}.dll")))
            return DotNetRuntimeKind.FrameworkDependent;
        return DotNetRuntimeKind.None;
    }

    /// <summary>
    /// Reads the Microsoft.NETCore.App major.minor from &lt;exec&gt;.runtimeconfig.json
    /// (e.g. "10.0"), or null when absent/unparsable.
    /// </summary>
    public static string? ReadFrameworkVersion(string publishDir, string execName)
    {
        var path = Path.Combine(publishDir, $"{execName}.runtimeconfig.json");
        if (!File.Exists(path))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("runtimeOptions", out var ro))
                return null;

            IEnumerable<JsonElement> frameworks = Array.Empty<JsonElement>();
            if (ro.TryGetProperty("framework", out var single))
                frameworks = new[] { single };
            else if (ro.TryGetProperty("frameworks", out var many) && many.ValueKind == JsonValueKind.Array)
                frameworks = many.EnumerateArray().ToList();

            foreach (var fw in frameworks)
            {
                if (fw.TryGetProperty("name", out var name) && name.GetString() == "Microsoft.NETCore.App" &&
                    fw.TryGetProperty("version", out var ver) && ver.GetString() is { } v)
                {
                    var parts = v.Split('.');
                    return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : v;
                }
            }
        }
        catch
        {
            // fall through
        }
        return null;
    }

    /// <summary>Ensures a maintainer string carries an email ("Name &lt;email&gt;").</summary>
    public static string NormalizeMaintainer(string maintainer)
    {
        var m = maintainer.Trim();
        return m.Contains('<') && m.Contains('>') ? m : $"{m} <{DefaultMaintainerEmail}>";
    }

    /// <summary>Freedesktop category to a Debian archive section.</summary>
    public static string DebianSection(string category)
    {
        return category.ToLowerInvariant() switch
        {
            "development" => "devel",
            "game" => "games",
            "graphics" => "graphics",
            "audio" => "sound",
            "audiovideo" or "video" => "video",
            "network" => "net",
            "office" => "editors",
            "education" => "education",
            "science" => "science",
            "system" or "settings" => "admin",
            "utility" => "utils",
            _ => "misc"
        };
    }
}
