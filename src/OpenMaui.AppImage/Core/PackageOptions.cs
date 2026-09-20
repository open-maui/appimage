namespace OpenMaui.AppImage.Core;

public record PackageOptions
{
    public required DirectoryInfo InputDirectory { get; init; }
    public required FileInfo OutputFile { get; init; }
    public required string AppName { get; init; }
    public string? ExecutableName { get; init; }
    public FileInfo? IconPath { get; init; }
    public required string Category { get; init; }
    public required string Version { get; init; }
    public required string Comment { get; init; }
    public string? AppId { get; init; }  // For Flatpak

    /// <summary>
    /// When true, <see cref="InputDirectory"/> is already a structured AppDir/FHS
    /// tree (usr/bin, usr/lib, …), e.g. extracted from a .deb — use it as the AppDir
    /// root and resolve the executable under usr/bin, instead of copying a flat
    /// publish dir into usr/bin.
    /// </summary>
    public bool PreBuiltAppDir { get; init; }

    /// <summary>
    /// Force appimagetool to run in extract-and-run mode instead of FUSE-mounting,
    /// regardless of whether /dev/fuse looks available. For hosts where FUSE is
    /// present but broken.
    /// </summary>
    public bool NoFuse { get; init; }

    /// <summary>
    /// Forbid network access: never auto-download appimagetool when it is not
    /// found on the host.
    /// </summary>
    public bool NoFetch { get; init; }

    /// <summary>
    /// Inject a POSIX-sh host-dependency check block into the generated AppRun
    /// that probes required sonames via ldconfig at launch.
    /// </summary>
    public bool HostDepsCheck { get; init; }

    /// <summary>
    /// appimagetool update information string (-u), e.g.
    /// "gh-releases-zsync|user|repo|latest|*.AppImage.zsync" — enables zsync
    /// self-updating AppImages.
    /// </summary>
    public string? UpdateInfo { get; init; }

    /// <summary>Sign the AppImage with GPG (appimagetool --sign).</summary>
    public bool Sign { get; init; }

    /// <summary>GPG key id to sign with (appimagetool --sign-key). Implies <see cref="Sign"/>.</summary>
    public string? SignKey { get; init; }

    /// <summary>
    /// Generate a minimal AppStream metainfo.xml into
    /// AppDir/usr/share/metainfo/&lt;app-id&gt;.metainfo.xml.
    /// </summary>
    public bool GenerateMetainfo { get; init; }

    /// <summary>Developer name for the AppStream metainfo (&lt;developer&gt;&lt;name&gt;).</summary>
    public string? Developer { get; init; }
}
