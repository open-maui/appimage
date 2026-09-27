using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace OpenMaui.AppImage.Core;

/// <summary>
/// Supports <c>--project</c>: resolves the .csproj, derives packaging metadata
/// (app name / version) from it, builds the <c>dotnet publish</c> argument line,
/// and locates the publish output afterwards. Everything except the actual
/// publish run is pure/static so it can be unit tested without invoking dotnet.
/// </summary>
public static class ProjectPublisher
{
    /// <summary>
    /// Resolves <paramref name="path"/> (a .csproj file, or a directory containing
    /// exactly one .csproj) to the full path of the project file. Returns null when
    /// no single project file can be determined (missing or ambiguous).
    /// </summary>
    public static string? ResolveProjectFile(string path)
    {
        if (File.Exists(path) && path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            return Path.GetFullPath(path);

        if (Directory.Exists(path))
        {
            var csprojs = Directory.GetFiles(path, "*.csproj", SearchOption.TopDirectoryOnly);
            if (csprojs.Length == 1)
                return Path.GetFullPath(csprojs[0]);
        }

        return null;
    }

    /// <summary>The RID for the host architecture (linux-x64 / linux-arm64 / linux-arm).</summary>
    public static string GetHostRid()
    {
        return RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => "linux-arm64",
            Architecture.Arm => "linux-arm",
            _ => "linux-x64"
        };
    }

    /// <summary>The argument line passed to <c>dotnet</c> to publish the project.</summary>
    public static string BuildPublishArguments(string csprojPath, string rid)
    {
        return $"publish \"{csprojPath}\" -c Release -r {rid} --self-contained true";
    }

    /// <summary>
    /// Derives the application name from the csproj: the AssemblyName property when
    /// set (and not an MSBuild expression), otherwise the project file stem.
    /// </summary>
    public static string DeriveAppName(string csprojPath)
    {
        var assemblyName = ReadProperty(csprojPath, "AssemblyName");
        return string.IsNullOrWhiteSpace(assemblyName)
            ? Path.GetFileNameWithoutExtension(csprojPath)
            : assemblyName;
    }

    /// <summary>
    /// Derives the application version from the csproj (Version property, falling
    /// back to ApplicationDisplayVersion for MAUI projects). Null when neither is set.
    /// </summary>
    public static string? DeriveVersion(string csprojPath)
    {
        return ReadProperty(csprojPath, "Version")
            ?? ReadProperty(csprojPath, "ApplicationDisplayVersion");
    }

    /// <summary>
    /// Packaging metadata read from the csproj for .deb/.rpm output:
    /// ApplicationId (MAUI), Description, Authors, PackageLicenseExpression and
    /// PackageProjectUrl. Missing properties are null.
    /// </summary>
    public static ProjectMetadata DeriveMetadata(string csprojPath)
    {
        return new ProjectMetadata(
            ApplicationId: ReadProperty(csprojPath, "ApplicationId"),
            Description: ReadProperty(csprojPath, "Description"),
            Authors: ReadProperty(csprojPath, "Authors") ?? ReadProperty(csprojPath, "Company"),
            License: ReadProperty(csprojPath, "PackageLicenseExpression"),
            ProjectUrl: ReadProperty(csprojPath, "PackageProjectUrl"));
    }

    /// <summary>
    /// Reads the first non-empty, non-MSBuild-expression value of a property from
    /// the csproj. Namespace-agnostic (handles both SDK-style and legacy projects).
    /// </summary>
    public static string? ReadProperty(string csprojPath, string propertyName)
    {
        try
        {
            var doc = XDocument.Load(csprojPath);
            return doc.Descendants()
                .Where(e => e.Name.LocalName == propertyName)
                .Select(e => e.Value.Trim())
                .FirstOrDefault(v => v.Length > 0 && !v.Contains("$("));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Locates the publish output under &lt;projectDir&gt;/bin/Release/&lt;tfm&gt;/&lt;rid&gt;/publish.
    /// When several TFMs match, the most recently written one wins.
    /// </summary>
    public static string? LocatePublishOutput(string projectDir, string rid)
    {
        var binRelease = Path.Combine(projectDir, "bin", "Release");
        if (!Directory.Exists(binRelease))
            return null;

        string? best = null;
        var bestTime = DateTime.MinValue;

        foreach (var tfmDir in Directory.EnumerateDirectories(binRelease))
        {
            var publishDir = Path.Combine(tfmDir, rid, "publish");
            if (!Directory.Exists(publishDir))
                continue;

            var time = Directory.GetLastWriteTimeUtc(publishDir);
            if (time > bestTime)
            {
                bestTime = time;
                best = publishDir;
            }
        }

        return best;
    }
}

/// <summary>Csproj-derived metadata used for system packages.</summary>
public sealed record ProjectMetadata(
    string? ApplicationId,
    string? Description,
    string? Authors,
    string? License,
    string? ProjectUrl);
