namespace OpenMaui.AppImage.Core;

/// <summary>
/// Package relationships for the .deb and .rpm outputs, per distro family.
/// </summary>
public sealed record NativeDependencies(
    IReadOnlyList<string> DebDepends,
    IReadOnlyList<string> DebRecommends,
    IReadOnlyList<string> RpmRequires,
    IReadOnlyList<string> RpmRecommends,
    IReadOnlyList<string> Notes);

/// <summary>
/// Maps the <see cref="DependencyScanner"/> result (plus the .NET runtime
/// flavor) to Debian Depends/Recommends and RPM Requires/Recommends.
///
/// Rules:
/// - Required host libraries become Depends (deb) / Requires (rpm).
/// - Feature-gated libraries become Recommends on both.
/// - A required library whose Fedora package lives outside the stock
///   repositories (it carries a FedoraHint, e.g. WPE WebKit from the
///   philn/wpewebkit COPR) is only a weak Recommends on RPM, so the package
///   still installs on stock Fedora; a note explains the extra step.
/// - Likewise a library with a DebianHint (WPE WebKit 2.54 exists only in
///   Debian testing/sid; Debian 13 has 2.48 and Ubuntu none) is never a
///   hard Depends, only a Recommends.
/// - A MinVersion version-qualifies the relationship on both formats
///   ("libwpewebkit-2.0-1 (>= 2.54)", "wpewebkit >= 2.54") so an older
///   package does not satisfy it.
/// - Self-contained apps pull the .NET runtime's native prerequisites
///   (glibc, libstdc++, zlib, OpenSSL, ICU); framework-dependent apps depend
///   on the distro's dotnet-runtime-X.Y package instead.
/// </summary>
public static class NativeDependencyMapper
{
    /// <summary>Debian/Ubuntu: native prerequisites of a self-contained .NET app.</summary>
    public static readonly string[] DebianDotNetRuntimeDepends =
    {
        "libc6",
        "libgcc-s1",
        "libstdc++6",
        "zlib1g",
        "libssl3t64 | libssl3",
        // ICU sonames are versioned per release (Ubuntu 22.04: 70, Debian 12: 72,
        // Ubuntu 24.04: 74, Debian 13: 76 ...); any one satisfies the runtime.
        "libicu78 | libicu77 | libicu76 | libicu74 | libicu72 | libicu70"
    };

    /// <summary>Fedora/RHEL: native prerequisites of a self-contained .NET app.</summary>
    public static readonly string[] FedoraDotNetRuntimeRequires =
    {
        "glibc",
        "libgcc",
        "libstdc++",
        "zlib",
        "openssl-libs",
        "libicu"
    };

    public static NativeDependencies Map(DependencyScanResult scan, DotNetRuntimeKind runtime,
        string? frameworkVersion = null)
    {
        var debDepends = new List<string>();
        var debRecommends = new List<string>();
        var rpmRequires = new List<string>();
        var rpmRecommends = new List<string>();
        var notes = new List<string>();

        switch (runtime)
        {
            case DotNetRuntimeKind.SelfContained:
                debDepends.AddRange(DebianDotNetRuntimeDepends);
                rpmRequires.AddRange(FedoraDotNetRuntimeRequires);
                break;
            case DotNetRuntimeKind.FrameworkDependent:
                var fw = frameworkVersion ?? "10.0";
                debDepends.Add($"dotnet-runtime-{fw}");
                rpmRequires.Add($"dotnet-runtime-{fw}");
                notes.Add($"Framework-dependent publish: the package depends on the distro's dotnet-runtime-{fw}. Publish self-contained (the --project default) to avoid it.");
                break;
        }

        foreach (var dep in scan.Dependencies)
        {
            var debPkgs = SplitPackages(dep.DebianPackages)
                .Select(p => dep.MinVersion == null ? p : $"{p} (>= {dep.MinVersion})").ToList();
            var rpmPkgs = SplitPackages(dep.FedoraPackages)
                .Select(p => dep.MinVersion == null ? p : $"{p} >= {dep.MinVersion}").ToList();
            var rpmNames = SplitPackages(dep.FedoraPackages).ToList();

            if (dep.DebianHint != null)
            {
                // Not installable on every Debian/Ubuntu release: apt skips an
                // unsatisfiable Recommends, a Depends would block installation.
                debRecommends.AddRange(debPkgs);
                notes.Add($"{dep.DisplayName} ({dep.Feature ?? "core"}) is only a Recommends in the .deb: {dep.DebianHint}.");
            }

            if (dep.Required)
            {
                if (dep.DebianHint == null)
                    debDepends.AddRange(debPkgs);
                if (dep.FedoraHint != null)
                {
                    // Not in stock Fedora: a hard Requires would make the rpm uninstallable.
                    rpmRecommends.AddRange(rpmPkgs);
                    notes.Add($"{dep.DisplayName} is required ({dep.Feature ?? "core"}) but Fedora does not ship it; the rpm only Recommends {string.Join(" ", rpmNames)}. Install it first with: {dep.FedoraHint} && sudo dnf install {string.Join(" ", rpmNames)}");
                }
                else
                {
                    rpmRequires.AddRange(rpmPkgs);
                }
            }
            else
            {
                if (dep.DebianHint == null)
                    debRecommends.AddRange(debPkgs);
                rpmRecommends.AddRange(rpmPkgs);
                if (dep.FedoraHint != null)
                    notes.Add($"{dep.DisplayName} ({dep.Feature}) on Fedora comes from a COPR: {dep.FedoraHint} && sudo dnf install {string.Join(" ", rpmNames)}");
            }
        }

        return new NativeDependencies(
            Dedupe(debDepends),
            Dedupe(debRecommends, exclude: debDepends),
            Dedupe(rpmRequires),
            Dedupe(rpmRecommends, exclude: rpmRequires),
            notes);
    }

    private static IEnumerable<string> SplitPackages(string packages) =>
        packages.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static List<string> Dedupe(IEnumerable<string> items, IEnumerable<string>? exclude = null)
    {
        var seen = new HashSet<string>(exclude ?? Array.Empty<string>(), StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var item in items)
        {
            if (seen.Add(item))
                result.Add(item);
        }
        return result;
    }
}
