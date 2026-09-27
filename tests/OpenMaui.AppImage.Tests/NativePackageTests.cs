using System.CommandLine;
using System.CommandLine.Parsing;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using OpenMaui.AppImage.Commands;
using OpenMaui.AppImage.Core;
using Xunit;

namespace OpenMaui.AppImage.Tests;

/// <summary>Metadata, dependency mapping and format selection for .deb/.rpm output.</summary>
public class NativePackageMetadataTests
{
    [Theory]
    [InlineData("ShellDemo", "shelldemo")]
    [InlineData("My Cool App", "my-cool-app")]
    [InlineData("Todo_App 2", "todo-app-2")]
    [InlineData("--Weird!!Name--", "weirdname")]
    [InlineData("X", "x-app")]
    [InlineData("!!!", "app")]
    public void SanitizePackageName_Produces_Debian_And_Rpm_Safe_Names(string input, string expected)
    {
        Assert.Equal(expected, NativePackageMetadata.SanitizePackageName(input));
    }

    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("v2.0.0", "2.0.0")]
    [InlineData("1.0.0-beta.1", "1.0.0~beta.1")]
    [InlineData("1.0.0-rc-2", "1.0.0~rc.2")]
    [InlineData("alpha", "0~alpha")]
    public void Debian_Upstream_Version_Is_Normalized(string input, string expected)
    {
        Assert.Equal(expected, NativePackageMetadata.ToDebianUpstreamVersion(input));
    }

    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("v10.0.110", "10.0.110")]
    [InlineData("1.0.0-beta.1", "1.0.0~beta.1")]
    [InlineData("1.0 build", "1.0.build")]
    public void Rpm_Version_Has_No_Hyphens(string input, string expected)
    {
        var v = NativePackageMetadata.ToRpmVersion(input);
        Assert.Equal(expected, v);
        Assert.DoesNotContain('-', v);
    }

    [Theory]
    [InlineData("linux-x64", "amd64", "x86_64")]
    [InlineData("linux-arm64", "arm64", "aarch64")]
    [InlineData("linux-arm", "armhf", "armv7hl")]
    public void Architecture_From_Rid(string rid, string deb, string rpm)
    {
        var arch = NativePackageMetadata.ArchitectureFromRid(rid);
        Assert.NotNull(arch);
        Assert.Equal(deb, arch!.Debian);
        Assert.Equal(rpm, arch.Rpm);
    }

    [Fact]
    public void Architecture_From_Elf_Header_Reads_E_Machine()
    {
        var dir = Directory.CreateTempSubdirectory("elf-test").FullName;
        try
        {
            var arm = Path.Combine(dir, "arm");
            File.WriteAllBytes(arm, TestFiles.ElfHeader(0xB7));
            var x64 = Path.Combine(dir, "x64");
            File.WriteAllBytes(x64, TestFiles.ElfHeader(0x3E));
            var text = Path.Combine(dir, "text");
            File.WriteAllText(text, "not an elf file at all");

            Assert.Equal(PackageArchitecture.Arm64, NativePackageMetadata.ArchitectureFromElf(arm));
            Assert.Equal(PackageArchitecture.X64, NativePackageMetadata.ArchitectureFromElf(x64));
            Assert.Null(NativePackageMetadata.ArchitectureFromElf(text));

            // A tree without native code is architecture-independent
            var managed = Directory.CreateDirectory(Path.Combine(dir, "managed")).FullName;
            File.WriteAllText(Path.Combine(managed, "App.dll"), "MZ");
            Assert.Equal(PackageArchitecture.Independent, NativePackageMetadata.DetectArchitecture(managed, "App"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Runtime_Detection_And_Framework_Version()
    {
        var dir = Directory.CreateTempSubdirectory("rt-test").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "App.dll"), "MZ");
            File.WriteAllText(Path.Combine(dir, "App.runtimeconfig.json"),
                """{"runtimeOptions":{"tfm":"net10.0","framework":{"name":"Microsoft.NETCore.App","version":"10.0.3"}}}""");

            Assert.Equal(DotNetRuntimeKind.FrameworkDependent, NativePackageMetadata.DetectRuntime(dir, "App"));
            Assert.Equal("10.0", NativePackageMetadata.ReadFrameworkVersion(dir, "App"));

            File.WriteAllBytes(Path.Combine(dir, "libcoreclr.so"), TestFiles.ElfHeader(0x3E));
            Assert.Equal(DotNetRuntimeKind.SelfContained, NativePackageMetadata.DetectRuntime(dir, "App"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void File_Names_Follow_Distro_Conventions()
    {
        var meta = TestFiles.Metadata() with { Version = "1.2.0-beta", Release = "3" };

        Assert.Equal("myapp_1.2.0~beta-3_amd64.deb", meta.DebFileName);
        Assert.Equal("myapp-1.2.0~beta-3.x86_64.rpm", meta.RpmFileName);
        Assert.Equal("/opt/com.example.MyApp", meta.InstallDir);
        Assert.Equal("/usr/bin/myapp", meta.LauncherPath);
    }

    [Theory]
    [InlineData("Jane Doe", "Jane Doe <noreply@localhost>")]
    [InlineData("Jane Doe <jane@example.com>", "Jane Doe <jane@example.com>")]
    public void Maintainer_Always_Carries_An_Email(string input, string expected)
    {
        Assert.Equal(expected, NativePackageMetadata.NormalizeMaintainer(input));
    }

    [Fact]
    public void Csproj_Metadata_Is_Derived()
    {
        var dir = Directory.CreateTempSubdirectory("csproj-meta").FullName;
        try
        {
            var path = Path.Combine(dir, "App.csproj");
            File.WriteAllText(path, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <ApplicationId>com.example.app</ApplicationId>
                    <Description>A longer description.</Description>
                    <Authors>Example Corp</Authors>
                    <PackageLicenseExpression>MIT</PackageLicenseExpression>
                    <PackageProjectUrl>https://example.com/app</PackageProjectUrl>
                  </PropertyGroup>
                </Project>
                """);

            var meta = ProjectPublisher.DeriveMetadata(path);
            Assert.Equal("com.example.app", meta.ApplicationId);
            Assert.Equal("A longer description.", meta.Description);
            Assert.Equal("Example Corp", meta.Authors);
            Assert.Equal("MIT", meta.License);
            Assert.Equal("https://example.com/app", meta.ProjectUrl);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

public class NativeDependencyMapperTests
{
    private static ISet<string> Set(params string[] names) =>
        new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void SelfContained_Base_App_Maps_Runtime_And_Required_Libraries()
    {
        var scan = DependencyScanner.Map(Set(DependencyScanner.BaseAssembly));
        var deps = NativeDependencyMapper.Map(scan, DotNetRuntimeKind.SelfContained);

        foreach (var d in new[] { "libc6", "libstdc++6", "zlib1g", "libx11-6", "libwayland-client0", "libfontconfig1", "libgtk-3-0" })
            Assert.Contains(d, deps.DebDepends);
        Assert.Contains(deps.DebDepends, d => d.StartsWith("libicu") && d.Contains(" | "));

        foreach (var r in new[] { "glibc", "libstdc++", "zlib", "openssl-libs", "libicu", "libX11", "libwayland-client", "fontconfig", "gtk3" })
            Assert.Contains(r, deps.RpmRequires);

        // Feature-gated libraries are weak on both
        Assert.Contains("libcups2", deps.DebRecommends);
        Assert.Contains("cups-libs", deps.RpmRecommends);
        Assert.Contains("libwebkit2gtk-4.1-0", deps.DebRecommends);
        Assert.DoesNotContain("libcups2", deps.DebDepends);
    }

    [Fact]
    public void Wpe_Is_Weak_And_Version_Qualified_On_Both_Formats()
    {
        var scan = DependencyScanner.Map(Set(DependencyScanner.BaseAssembly));
        var deps = NativeDependencyMapper.Map(scan, DotNetRuntimeKind.SelfContained);

        Assert.Contains("libwpewebkit-2.0-1 (>= 2.54)", deps.DebRecommends);
        Assert.Contains("wpewebkit >= 2.54", deps.RpmRecommends);
        Assert.DoesNotContain(deps.DebDepends, d => d.Contains("wpewebkit"));
        Assert.DoesNotContain(deps.RpmRequires, d => d.Contains("wpewebkit"));
    }

    [Fact]
    public void Blazor_Wpe_Is_Never_A_Hard_Dependency()
    {
        // WPE is required for BlazorWebView, but Fedora needs a COPR and only
        // Debian testing/sid ship 2.54: a hard dependency would make the
        // package uninstallable on stock Fedora, Debian 13 and Ubuntu.
        var scan = DependencyScanner.Map(Set(DependencyScanner.BaseAssembly, DependencyScanner.BlazorAssembly));
        var deps = NativeDependencyMapper.Map(scan, DotNetRuntimeKind.SelfContained);

        Assert.DoesNotContain(deps.RpmRequires, r => r.StartsWith("wpewebkit"));
        Assert.Contains("wpewebkit >= 2.54", deps.RpmRecommends);
        Assert.DoesNotContain(deps.DebDepends, d => d.Contains("wpewebkit"));
        Assert.Contains("libwpewebkit-2.0-1 (>= 2.54)", deps.DebRecommends);

        Assert.Contains(deps.Notes, n => n.Contains("philn/wpewebkit"));
        Assert.Contains(deps.Notes, n => n.Contains("Debian testing/sid"));
    }

    [Fact]
    public void GStreamer_Packages_Are_Split_Into_Separate_Recommends()
    {
        var scan = DependencyScanner.Map(Set(DependencyScanner.BaseAssembly, DependencyScanner.MediaElementAssembly));
        var deps = NativeDependencyMapper.Map(scan, DotNetRuntimeKind.SelfContained);

        Assert.Contains("gstreamer1-plugins-good", deps.RpmRecommends);
        Assert.Contains("gstreamer1.0-plugins-base", deps.DebRecommends);
        Assert.DoesNotContain(deps.RpmRecommends, r => r.Contains(' ') && !r.Contains(">="));
    }

    [Fact]
    public void FrameworkDependent_App_Depends_On_The_Shared_Runtime()
    {
        var deps = NativeDependencyMapper.Map(DependencyScanner.Map(Set("App.dll")),
            DotNetRuntimeKind.FrameworkDependent, "10.0");

        Assert.Equal(new[] { "dotnet-runtime-10.0" }, deps.DebDepends);
        Assert.Equal(new[] { "dotnet-runtime-10.0" }, deps.RpmRequires);
        Assert.DoesNotContain(deps.DebDepends, d => d.StartsWith("libicu"));
    }

    [Fact]
    public void Non_DotNet_Tree_Has_No_Runtime_Dependencies()
    {
        var deps = NativeDependencyMapper.Map(DependencyScanner.Map(Set("App.dll")), DotNetRuntimeKind.None);

        Assert.Empty(deps.DebDepends);
        Assert.Empty(deps.RpmRequires);
        Assert.Empty(deps.DebRecommends);
    }

    [Fact]
    public void Relationships_Are_Deduplicated()
    {
        var scan = DependencyScanner.Map(Set(DependencyScanner.BaseAssembly, DependencyScanner.BlazorAssembly));
        var deps = NativeDependencyMapper.Map(scan, DotNetRuntimeKind.SelfContained);

        Assert.Equal(deps.DebDepends.Count, deps.DebDepends.Distinct().Count());
        Assert.Equal(deps.DebRecommends.Count, deps.DebRecommends.Distinct().Count());
        Assert.Equal(deps.RpmRecommends.Count, deps.RpmRecommends.Distinct().Count());
        Assert.Empty(deps.RpmRecommends.Intersect(deps.RpmRequires));
    }
}

public class FormatSelectionTests
{
    [Theory]
    [InlineData("appimage", new[] { PackageCommand.PackageFormat.AppImage })]
    [InlineData("DEB", new[] { PackageCommand.PackageFormat.Deb })]
    [InlineData("rpm", new[] { PackageCommand.PackageFormat.Rpm })]
    [InlineData("deb,rpm", new[] { PackageCommand.PackageFormat.Deb, PackageCommand.PackageFormat.Rpm })]
    [InlineData("all", new[] { PackageCommand.PackageFormat.AppImage, PackageCommand.PackageFormat.Deb, PackageCommand.PackageFormat.Rpm })]
    [InlineData("flatpak,deb,deb", new[] { PackageCommand.PackageFormat.Flatpak, PackageCommand.PackageFormat.Deb })]
    public void ParseFormats_Accepts_Single_Combined_And_All(string input, PackageCommand.PackageFormat[] expected)
    {
        Assert.Equal(expected, PackageCommand.ParseFormats(input));
    }

    [Fact]
    public void ParseFormats_Defaults_To_AppImage_And_Rejects_Unknown()
    {
        Assert.Equal(new[] { PackageCommand.PackageFormat.AppImage }, PackageCommand.ParseFormats(null));
        Assert.Null(PackageCommand.ParseFormats("snap"));
        Assert.Null(PackageCommand.ParseFormats("deb,bogus"));
    }

    [Fact]
    public void Unknown_Format_Is_A_Parse_Error()
    {
        var result = PackageCommand.Create().Parse("-i publish -o out.AppImage -n MyApp -f snap");
        Assert.Contains(result.Errors, e => e.Message.Contains("Unknown --format"));
    }

    [Theory]
    [InlineData("-i publish -n MyApp -f deb")]
    [InlineData("-i publish -n MyApp -f rpm")]
    [InlineData("-i publish -n MyApp -f all")]
    [InlineData("--project ./MyApp -f deb,rpm -o dist")]
    public void Native_Formats_Do_Not_Require_Output(string args)
    {
        Assert.Empty(PackageCommand.Create().Parse(args).Errors);
    }

    [Fact]
    public void Package_Metadata_Options_Parse()
    {
        var result = PackageCommand.Create().Parse(
            "--project ./MyApp -f deb --package-name myapp --maintainer \"Jane <jane@example.com>\" " +
            "--license MIT --homepage https://example.com --release 2");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task Native_Flow_Fails_For_Missing_Input_Directory()
    {
        var options = new PackageOptions
        {
            InputDirectory = new DirectoryInfo("/nonexistent-input-dir-" + Guid.NewGuid().ToString("N")),
            OutputFile = new FileInfo(Path.Combine(Path.GetTempPath(), "out.deb")),
            AppName = "Test App",
            Category = "Utility",
            Version = "1.0.0",
            Comment = "Test"
        };

        Assert.False(await PackageCommand.RunNativeAsync(options,
            new[] { PackageCommand.PackageFormat.Deb }, null, Path.GetTempPath()));
    }
}

public class DebPackageWriterTests
{
    [Fact]
    public void Control_File_Has_Required_Fields_And_Relationships()
    {
        var meta = TestFiles.Metadata() with { Homepage = "https://example.com", Description = "Line one.\n\nLine three." };
        var deps = new NativeDependencies(
            new[] { "libc6", "libicu76 | libicu74" }, new[] { "libcups2" },
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

        var control = DebPackageWriter.GenerateControl(meta, deps, 1234);

        Assert.Contains("Package: myapp\n", control);
        Assert.Contains("Version: 1.0.0-1\n", control);
        Assert.Contains("Architecture: amd64\n", control);
        Assert.Contains("Maintainer: Jane Doe <jane@example.com>\n", control);
        Assert.Contains("Installed-Size: 1234\n", control);
        Assert.Contains("Depends: libc6, libicu76 | libicu74\n", control);
        Assert.Contains("Recommends: libcups2\n", control);
        Assert.Contains("Section: utils\n", control);
        Assert.Contains("Homepage: https://example.com\n", control);
        Assert.Contains("Description: A test app\n Line one.\n .\n Line three.\n", control);
        Assert.EndsWith("\n", control);
        Assert.DoesNotContain("\n\n", control);
    }

    [Fact]
    public void Control_File_Omits_Empty_Relationship_Fields()
    {
        var deps = new NativeDependencies(Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

        var control = DebPackageWriter.GenerateControl(TestFiles.Metadata(), deps, 1);

        Assert.DoesNotContain("Depends:", control);
        Assert.DoesNotContain("Recommends:", control);
        Assert.DoesNotContain("Homepage:", control);
    }

    [Fact]
    public void Ar_Archive_Round_Trips()
    {
        using var ms = new MemoryStream();
        var mtime = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        using (var ar = new ArArchiveWriter(ms, leaveOpen: true))
        {
            ar.AddEntry("debian-binary", Encoding.ASCII.GetBytes("2.0\n"), mtime);
            ar.AddEntry("odd.bin", new byte[] { 1, 2, 3 }, mtime);
            ar.AddEntry("empty", Array.Empty<byte>(), mtime);
        }

        var members = TestFiles.ReadAr(ms.ToArray());

        Assert.Equal(new[] { "debian-binary", "odd.bin", "empty" }, members.Select(m => m.Name));
        Assert.Equal("2.0\n", Encoding.ASCII.GetString(members[0].Data));
        Assert.Equal(new byte[] { 1, 2, 3 }, members[1].Data);
        Assert.Empty(members[2].Data);
        Assert.All(members, m =>
        {
            Assert.Equal("0", m.Uid);
            Assert.Equal("0", m.Gid);
            Assert.Equal("100644", m.Mode);
            Assert.Equal("1700000000", m.Mtime);
        });
    }

    [Fact]
    public void Ar_Rejects_Long_Member_Names()
    {
        using var ms = new MemoryStream();
        using var ar = new ArArchiveWriter(ms);
        Assert.Throws<ArgumentException>(() =>
            ar.AddEntry("a-name-longer-than-15", Array.Empty<byte>(), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Staged_Tree_Has_Fhs_Layout_And_Explicit_Modes()
    {
        using var t = new TestFiles.StagedFixture();

        Assert.True(File.Exists(Path.Combine(t.Root, "opt/com.example.MyApp/MyApp")));
        Assert.True(File.Exists(Path.Combine(t.Root, "usr/bin/myapp")));
        Assert.True(File.Exists(Path.Combine(t.Root, "usr/share/applications/com.example.MyApp.desktop")));
        Assert.True(File.Exists(Path.Combine(t.Root, "usr/share/icons/hicolor/scalable/apps/com.example.MyApp.svg")));
        Assert.True(File.Exists(Path.Combine(t.Root, "usr/share/metainfo/com.example.MyApp.metainfo.xml")));
        Assert.True(File.Exists(Path.Combine(t.Root, "opt/com.example.MyApp/appicon.svg")));

        Assert.Contains("/usr/bin/myapp", t.Staged.Files);
        Assert.Contains("/usr/share/applications/com.example.MyApp.desktop", t.Staged.Files);

        var exec = NativeLayoutBuilder.ExecMode;
        var file = NativeLayoutBuilder.FileMode;
        Assert.Equal(exec, File.GetUnixFileMode(Path.Combine(t.Root, "opt/com.example.MyApp/MyApp")));
        Assert.Equal(exec, File.GetUnixFileMode(Path.Combine(t.Root, "usr/bin/myapp")));
        Assert.Equal(exec, File.GetUnixFileMode(Path.Combine(t.Root, "opt/com.example.MyApp/createdump")));
        Assert.Equal(file, File.GetUnixFileMode(Path.Combine(t.Root, "opt/com.example.MyApp/libcoreclr.so")));
        Assert.Equal(file, File.GetUnixFileMode(Path.Combine(t.Root, "opt/com.example.MyApp/MyApp.dll")));
        Assert.Equal(file, File.GetUnixFileMode(Path.Combine(t.Root, "opt/com.example.MyApp/sub/data.json")));
        Assert.Equal(NativeLayoutBuilder.DirMode, File.GetUnixFileMode(Path.Combine(t.Root, "opt/com.example.MyApp/sub")));
    }

    [Fact]
    public void Data_Tar_Round_Trips_With_Root_Ownership_And_Modes()
    {
        using var t = new TestFiles.StagedFixture();
        var items = DebPackageWriter.CollectEntries(t.Root);

        using var ms = new MemoryStream();
        DebPackageWriter.WriteTar(ms, items, DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
        ms.Position = 0;

        var entries = new List<(string Name, TarEntryType Type, UnixFileMode Mode, int Uid, int Gid, string User, byte[] Data)>();
        using (var reader = new TarReader(ms))
        {
            TarEntry? e;
            while ((e = reader.GetNextEntry(copyData: true)) != null)
            {
                var data = Array.Empty<byte>();
                if (e.DataStream != null)
                {
                    using var buf = new MemoryStream();
                    e.DataStream.CopyTo(buf);
                    data = buf.ToArray();
                }
                entries.Add((e.Name, e.EntryType, e.Mode, e.Uid, e.Gid, ((PosixTarEntry)e).UserName, data));
            }
        }

        Assert.Equal("./", entries[0].Name);
        Assert.All(entries, e =>
        {
            Assert.Equal(0, e.Uid);
            Assert.Equal(0, e.Gid);
            Assert.Equal("root", e.User);
            Assert.StartsWith("./", e.Name);
        });

        // Every directory precedes its contents
        var names = entries.Select(e => e.Name).ToList();
        foreach (var e in entries.Skip(1))
        {
            var parent = e.Name.TrimEnd('/');
            parent = parent[..(parent.LastIndexOf('/') + 1)];
            Assert.True(names.IndexOf(parent) < names.IndexOf(e.Name), $"{parent} must precede {e.Name}");
        }

        var launcher = entries.Single(e => e.Name == "./usr/bin/myapp");
        Assert.Equal(NativeLayoutBuilder.ExecMode, launcher.Mode);
        Assert.Contains("exec \"/opt/com.example.MyApp/MyApp\" \"$@\"", Encoding.UTF8.GetString(launcher.Data));

        Assert.Equal(NativeLayoutBuilder.FileMode, entries.Single(e => e.Name == "./opt/com.example.MyApp/libcoreclr.so").Mode);
        Assert.All(entries.Where(e => e.Type == TarEntryType.Directory), d => Assert.Equal(NativeLayoutBuilder.DirMode, d.Mode));
    }

    [Fact]
    public void Deb_File_Has_Members_In_Order_And_Valid_Md5sums()
    {
        using var t = new TestFiles.StagedFixture();
        var output = Path.Combine(t.Work, "out", "myapp.deb");
        var deps = NativeDependencyMapper.Map(DependencyScanner.Map(new HashSet<string>()), DotNetRuntimeKind.SelfContained);

        DebPackageWriter.Write(t.Staged, t.Meta, deps, output);

        var members = TestFiles.ReadAr(File.ReadAllBytes(output));
        Assert.Equal(new[] { "debian-binary", "control.tar.gz", "data.tar.gz" }, members.Select(m => m.Name));
        Assert.Equal("2.0\n", Encoding.ASCII.GetString(members[0].Data));

        var control = TestFiles.ReadTarGz(members[1].Data);
        Assert.Equal(new[] { "./", "./control", "./md5sums" }, control.Keys);
        var controlText = Encoding.UTF8.GetString(control["./control"]);
        Assert.Contains("Package: myapp\n", controlText);
        Assert.Contains("Depends: libc6", controlText);
        Assert.Matches(@"Installed-Size: \d+\n", controlText);

        var data = TestFiles.ReadTarGz(members[2].Data);
        Assert.Contains("./usr/bin/myapp", data.Keys);
        Assert.Contains("./opt/com.example.MyApp/MyApp.dll", data.Keys);

        // md5sums covers every regular file, paths without "./"
        var md5Lines = Encoding.UTF8.GetString(control["./md5sums"]).TrimEnd('\n').Split('\n');
        var regularFiles = data.Where(kv => !kv.Key.EndsWith('/')).ToList();
        Assert.Equal(regularFiles.Count, md5Lines.Length);
        foreach (var line in md5Lines)
        {
            var hash = line[..32];
            var path = line[34..];
            Assert.Equal("  ", line[32..34]);
            Assert.Equal(Convert.ToHexStringLower(MD5.HashData(data["./" + path])), hash);
        }

        // No stray temp files next to the output
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(output)!));
    }

    [Fact]
    public void Extended_Description_Is_Skipped_When_Equal_To_Summary()
    {
        Assert.Empty(DebPackageWriter.ExtendedDescriptionLines("Same", "Same"));
        Assert.Empty(DebPackageWriter.ExtendedDescriptionLines(null, "Same"));
    }
}

public class NativeLayoutBuilderTests
{
    [Fact]
    public void Launcher_Execs_The_Opt_Binary_Or_Dotnet()
    {
        var meta = TestFiles.Metadata();
        Assert.Equal("#!/bin/sh\n# My App launcher (generated by openmaui-appimage)\nexec \"/opt/com.example.MyApp/MyApp\" \"$@\"\n",
            NativeLayoutBuilder.GenerateLauncherScript(meta));

        var fdd = meta with { Runtime = DotNetRuntimeKind.FrameworkDependent };
        Assert.Contains("exec dotnet \"/opt/com.example.MyApp/MyApp.dll\" \"$@\"", NativeLayoutBuilder.GenerateLauncherScript(fdd));
    }

    [Fact]
    public void Desktop_Entry_Uses_Launcher_And_App_Id_Icon()
    {
        var entry = NativeLayoutBuilder.GenerateDesktopEntry(TestFiles.Metadata());

        Assert.StartsWith("[Desktop Entry]\n", entry);
        Assert.Contains("Name=My App\n", entry);
        Assert.Contains("Exec=myapp\n", entry);
        Assert.Contains("Icon=com.example.MyApp\n", entry);
        Assert.Contains("Categories=Utility;\n", entry);
        Assert.Contains("StartupWMClass=MyApp\n", entry);
        Assert.DoesNotContain("X-AppImage", entry);
    }

    [Fact]
    public void Hicolor_Target_Depends_On_Icon_Type_And_Size()
    {
        var dir = Directory.CreateTempSubdirectory("icon-test").FullName;
        try
        {
            var svg = Path.Combine(dir, "a.svg");
            File.WriteAllText(svg, "<svg/>");
            var png48 = Path.Combine(dir, "b.png");
            File.WriteAllBytes(png48, TestFiles.PngHeader(48, 48));
            var png100 = Path.Combine(dir, "c.png");
            File.WriteAllBytes(png100, TestFiles.PngHeader(100, 100));
            var ico = Path.Combine(dir, "d.ico");
            File.WriteAllBytes(ico, new byte[] { 0, 0, 1, 0 });

            Assert.Equal("/usr/share/icons/hicolor/scalable/apps/id.svg", NativeLayoutBuilder.HicolorTarget(svg, "id"));
            Assert.Equal("/usr/share/icons/hicolor/48x48/apps/id.png", NativeLayoutBuilder.HicolorTarget(png48, "id"));
            Assert.Equal("/usr/share/icons/hicolor/256x256/apps/id.png", NativeLayoutBuilder.HicolorTarget(png100, "id"));
            Assert.Null(NativeLayoutBuilder.HicolorTarget(ico, "id"));
            Assert.Equal((48, 48), NativeLayoutBuilder.ReadPngSize(png48));
            Assert.Null(NativeLayoutBuilder.ReadPngSize(svg));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void File_Mode_Rules()
    {
        var dir = Directory.CreateTempSubdirectory("mode-test").FullName;
        try
        {
            var exe = Path.Combine(dir, "App");
            File.WriteAllBytes(exe, TestFiles.ElfHeader(0x3E));
            var so = Path.Combine(dir, "libfoo.so");
            File.WriteAllBytes(so, TestFiles.ElfHeader(0x3E));
            var soVersioned = Path.Combine(dir, "libbar.so.1");
            File.WriteAllBytes(soVersioned, TestFiles.ElfHeader(0x3E));
            var script = Path.Combine(dir, "run.sh");
            File.WriteAllText(script, "#!/bin/sh\necho hi\n");
            File.SetUnixFileMode(script, NativeLayoutBuilder.ExecMode);
            var plainScript = Path.Combine(dir, "notes.sh");
            File.WriteAllText(plainScript, "#!/bin/sh\n");
            File.SetUnixFileMode(plainScript, NativeLayoutBuilder.FileMode);
            var dll = Path.Combine(dir, "App.dll");
            File.WriteAllText(dll, "MZ");
            File.SetUnixFileMode(dll, NativeLayoutBuilder.ExecMode); // publish sometimes marks dlls 0744/0755

            Assert.Equal(NativeLayoutBuilder.ExecMode, NativeLayoutBuilder.DecideFileMode(exe));
            Assert.Equal(NativeLayoutBuilder.FileMode, NativeLayoutBuilder.DecideFileMode(so));
            Assert.Equal(NativeLayoutBuilder.FileMode, NativeLayoutBuilder.DecideFileMode(soVersioned));
            Assert.Equal(NativeLayoutBuilder.ExecMode, NativeLayoutBuilder.DecideFileMode(script));
            Assert.Equal(NativeLayoutBuilder.FileMode, NativeLayoutBuilder.DecideFileMode(plainScript));
            Assert.Equal(NativeLayoutBuilder.FileMode, NativeLayoutBuilder.DecideFileMode(dll));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

public class RpmPackageBuilderTests
{
    private static readonly StagedTree Staged = new("/tmp/stage root", new[]
    {
        "/usr/bin/myapp",
        "/usr/share/applications/com.example.MyApp.desktop",
        "/usr/share/icons/hicolor/scalable/apps/com.example.MyApp.svg"
    });

    [Fact]
    public void Spec_Declares_Metadata_Dependencies_And_Files()
    {
        var meta = TestFiles.Metadata() with { License = "MIT", Homepage = "https://example.com" };
        var deps = new NativeDependencies(Array.Empty<string>(), Array.Empty<string>(),
            new[] { "glibc", "gtk3" }, new[] { "wpewebkit >= 2.54" },
            new[] { "WPE on Fedora: sudo dnf copr enable philn/wpewebkit && sudo dnf install wpewebkit" });

        var spec = RpmPackageBuilder.GenerateSpec(meta, deps, Staged, vendor: "Example Corp");

        Assert.Contains("Name:           myapp\n", spec);
        Assert.Contains("Version:        1.0.0\n", spec);
        Assert.Contains("Release:        1\n", spec);
        Assert.Contains("License:        MIT\n", spec);
        Assert.Contains("URL:            https://example.com\n", spec);
        Assert.Contains("Packager:       Jane Doe <jane@example.com>\n", spec);
        Assert.Contains("Vendor:         Example Corp\n", spec);
        Assert.Contains("AutoReqProv:    no\n", spec);
        Assert.Contains("Requires:       glibc\nRequires:       gtk3\n", spec);
        Assert.Contains("Recommends:     wpewebkit >= 2.54\n", spec);
        Assert.DoesNotContain("BuildArch", spec);

        // Payload must not be stripped or split
        Assert.Contains("%global __os_install_post %{nil}", spec);
        Assert.Contains("%global debug_package %{nil}", spec);

        Assert.Contains("philn/wpewebkit", spec[spec.IndexOf("%description", StringComparison.Ordinal)..]);
        Assert.Contains("cp -a '/tmp/stage root/.' %{buildroot}/", spec);
        Assert.Contains("%files\n%defattr(-,root,root,-)\n\"/opt/com.example.MyApp\"\n\"/usr/bin/myapp\"\n", spec);
        Assert.Contains("%changelog\n* ", spec);
    }

    [Fact]
    public void Spec_Escapes_Macros_In_Free_Text_And_Marks_Noarch()
    {
        var meta = TestFiles.Metadata() with
        {
            Summary = "100% managed",
            Architecture = PackageArchitecture.Independent
        };
        var deps = new NativeDependencies(Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

        var spec = RpmPackageBuilder.GenerateSpec(meta, deps, Staged);

        Assert.Contains("Summary:        100%% managed\n", spec);
        Assert.Contains("BuildArch:      noarch\n", spec);
    }

    [Fact]
    public void BuildArguments_Use_Private_Topdir_And_Target()
    {
        var args = RpmPackageBuilder.BuildArguments("/w/x.spec", "/w/rpmbuild", PackageArchitecture.Arm64);

        Assert.Equal("-bb", args[0]);
        Assert.Contains("_topdir /w/rpmbuild", args);
        Assert.Equal("aarch64", args[args.IndexOf("--target") + 1]);
        Assert.Equal("/w/x.spec", args[^1]);

        var noarch = RpmPackageBuilder.BuildArguments("/w/x.spec", "/w/rpmbuild", PackageArchitecture.Independent);
        Assert.DoesNotContain("--target", noarch);
    }

    [Fact]
    public void Missing_Rpmbuild_Message_Names_Install_Commands()
    {
        Assert.Contains("sudo dnf install rpm-build", RpmPackageBuilder.MissingRpmbuildMessage);
        Assert.Contains("sudo apt install rpm", RpmPackageBuilder.MissingRpmbuildMessage);
    }

    [Fact]
    public async Task Rpmbuild_Produces_A_Package_When_Available()
    {
        // Integration check; a no-op on hosts without rpmbuild.
        if (ProcessRunner.FindOnPath("rpmbuild") == null || ProcessRunner.FindOnPath("rpm") == null)
            return;

        using var t = new TestFiles.StagedFixture();
        var output = Path.Combine(t.Work, "out", t.Meta.RpmFileName);
        var deps = NativeDependencyMapper.Map(
            DependencyScanner.Map(new HashSet<string> { DependencyScanner.BaseAssembly, DependencyScanner.BlazorAssembly }),
            DotNetRuntimeKind.SelfContained);

        Assert.True(await RpmPackageBuilder.BuildAsync(t.Staged, t.Meta, deps, output, t.Work));
        Assert.True(File.Exists(output));

        var requires = await TestFiles.RunAsync("rpm", "-qp", "--requires", output);
        Assert.Contains("gtk3", requires);
        Assert.DoesNotContain("wpewebkit", requires);
        var recommends = await TestFiles.RunAsync("rpm", "-qp", "--recommends", output);
        Assert.Contains("wpewebkit >= 2.54", recommends);

        var list = await TestFiles.RunAsync("rpm", "-qlvp", output);
        Assert.Matches(@"-rwxr-xr-x\s+1 root\s+root\s+\d+ .* /usr/bin/myapp", list);
        Assert.Matches(@"-rw-r--r--\s+1 root\s+root\s+\d+ .* /opt/com.example.MyApp/libcoreclr.so", list);
    }
}

/// <summary>Shared fixtures and readers for the native-package tests.</summary>
internal static class TestFiles
{
    public static NativePackageMetadata Metadata() => new()
    {
        PackageName = "myapp",
        AppName = "My App",
        AppId = "com.example.MyApp",
        ExecutableName = "MyApp",
        Version = "1.0.0",
        Architecture = PackageArchitecture.X64,
        Runtime = DotNetRuntimeKind.SelfContained,
        Maintainer = "Jane Doe <jane@example.com>",
        License = "LicenseRef-Proprietary",
        Summary = "A test app",
        Category = "Utility"
    };

    public static byte[] ElfHeader(ushort machine)
    {
        var bytes = new byte[64];
        bytes[0] = 0x7F; bytes[1] = (byte)'E'; bytes[2] = (byte)'L'; bytes[3] = (byte)'F';
        bytes[4] = 2; // 64-bit
        bytes[5] = 1; // little-endian
        bytes[18] = (byte)(machine & 0xFF);
        bytes[19] = (byte)(machine >> 8);
        return bytes;
    }

    public static byte[] PngHeader(int width, int height)
    {
        var bytes = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        bytes[11] = 13;
        "IHDR"u8.ToArray().CopyTo(bytes, 12);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), height);
        return bytes;
    }

    public sealed record ArMember(string Name, string Mtime, string Uid, string Gid, string Mode, byte[] Data);

    /// <summary>Independent ar reader (common format) for round-trip checks.</summary>
    public static List<ArMember> ReadAr(byte[] archive)
    {
        Assert.Equal("!<arch>\n", Encoding.ASCII.GetString(archive, 0, 8));
        var members = new List<ArMember>();
        var pos = 8;
        while (pos < archive.Length)
        {
            var header = Encoding.ASCII.GetString(archive, pos, 60);
            Assert.Equal("`\n", header[58..60]);
            var size = int.Parse(header[48..58].Trim());
            var data = archive.AsSpan(pos + 60, size).ToArray();
            members.Add(new ArMember(header[..16].Trim(), header[16..28].Trim(), header[28..34].Trim(),
                header[34..40].Trim(), header[40..48].Trim(), data));
            pos += 60 + size + (size % 2);
        }
        Assert.Equal(archive.Length, pos);
        return members;
    }

    /// <summary>Reads a .tar.gz into name → content (insertion ordered).</summary>
    public static Dictionary<string, byte[]> ReadTarGz(byte[] tarGz)
    {
        var result = new Dictionary<string, byte[]>();
        using var gz = new GZipStream(new MemoryStream(tarGz), CompressionMode.Decompress);
        using var reader = new TarReader(gz);
        TarEntry? e;
        while ((e = reader.GetNextEntry(copyData: true)) != null)
        {
            using var buf = new MemoryStream();
            e.DataStream?.CopyTo(buf);
            result[e.Name] = buf.ToArray();
        }
        return result;
    }

    public static async Task<string> RunAsync(string command, params string[] args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(command)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(psi)!;
        var stdout = await p.StandardOutput.ReadToEndAsync();
        await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return stdout;
    }

    /// <summary>A fake self-contained publish dir staged into an FHS tree.</summary>
    public sealed class StagedFixture : IDisposable
    {
        public string Work { get; }
        public string Root => Staged.Root;
        public StagedTree Staged { get; }
        public NativePackageMetadata Meta { get; } = Metadata();

        public StagedFixture()
        {
            Work = Directory.CreateTempSubdirectory("native-pkg-test").FullName;
            var publish = Directory.CreateDirectory(Path.Combine(Work, "publish")).FullName;

            File.WriteAllBytes(Path.Combine(publish, "MyApp"), ElfHeader(0x3E));
            File.WriteAllBytes(Path.Combine(publish, "createdump"), ElfHeader(0x3E));
            File.WriteAllBytes(Path.Combine(publish, "libcoreclr.so"), ElfHeader(0x3E));
            File.WriteAllText(Path.Combine(publish, "MyApp.dll"), "MZ managed");
            File.SetUnixFileMode(Path.Combine(publish, "MyApp.dll"),
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); // 0700, like odd publish output
            Directory.CreateDirectory(Path.Combine(publish, "sub"));
            File.WriteAllText(Path.Combine(publish, "sub", "data.json"), "{}");

            var icon = Path.Combine(Work, "icon.svg");
            File.WriteAllText(icon, "<svg xmlns=\"http://www.w3.org/2000/svg\"/>");

            Staged = NativeLayoutBuilder.Stage(Path.Combine(Work, "root"), publish, Meta,
                new FileInfo(icon), generateMetainfo: true, developer: "Example Corp");
        }

        public void Dispose()
        {
            try { Directory.Delete(Work, recursive: true); } catch { }
        }
    }
}
