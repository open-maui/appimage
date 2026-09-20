using System.Text;
using System.Text.RegularExpressions;

namespace OpenMaui.AppImage.Core;

/// <summary>
/// Generates a minimal, valid AppStream metainfo.xml for a desktop application
/// (written to AppDir/usr/share/metainfo/&lt;app-id&gt;.metainfo.xml). Pure string
/// generation so it is fully unit-testable.
/// </summary>
public static class AppStreamGenerator
{
    /// <summary>
    /// Builds the metainfo XML. <paramref name="desktopFileId"/> is the .desktop
    /// file name (the launchable id); <paramref name="binaryName"/> fills
    /// &lt;provides&gt;&lt;binary&gt;.
    /// </summary>
    public static string Generate(
        string appId,
        string appName,
        string summary,
        string desktopFileId,
        string binaryName,
        string? developerName = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<component type=\"desktop-application\">");
        sb.AppendLine($"  <id>{Escape(appId)}</id>");
        sb.AppendLine("  <metadata_license>CC0-1.0</metadata_license>");
        sb.AppendLine($"  <name>{Escape(appName)}</name>");
        sb.AppendLine($"  <summary>{Escape(summary)}</summary>");
        sb.AppendLine("  <description>");
        sb.AppendLine($"    <p>{Escape(summary)}</p>");
        sb.AppendLine("  </description>");
        sb.AppendLine($"  <launchable type=\"desktop-id\">{Escape(desktopFileId)}</launchable>");
        sb.AppendLine("  <provides>");
        sb.AppendLine($"    <binary>{Escape(binaryName)}</binary>");
        sb.AppendLine("  </provides>");
        if (!string.IsNullOrWhiteSpace(developerName))
        {
            sb.AppendLine("  <developer>");
            sb.AppendLine($"    <name>{Escape(developerName)}</name>");
            sb.AppendLine("  </developer>");
        }
        sb.AppendLine("</component>");
        return sb.ToString();
    }

    /// <summary>
    /// Whether the app id looks like a reverse-DNS id (at least three dot-separated
    /// segments, each starting with a letter), e.g. com.example.MyApp.
    /// </summary>
    public static bool LooksReverseDns(string? appId)
    {
        return appId != null && Regex.IsMatch(appId,
            @"^[A-Za-z][A-Za-z0-9_-]*(\.[A-Za-z][A-Za-z0-9_-]*){2,}$");
    }

    /// <summary>Lowercase alphanumeric segment for deriving a fallback app id.</summary>
    public static string SanitizeIdSegment(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(c);
        }
        return sb.Length > 0 ? sb.ToString() : "app";
    }

    public static string Escape(string value)
    {
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}
