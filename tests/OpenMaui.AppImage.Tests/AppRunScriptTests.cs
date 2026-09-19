using OpenMaui.AppImage.Core;
using Xunit;

namespace OpenMaui.AppImage.Tests;

public class AppRunScriptTests
{
    private static PackageOptions MakeOptions() => new()
    {
        InputDirectory = new DirectoryInfo("/tmp/does-not-matter"),
        OutputFile = new FileInfo("/tmp/out.AppImage"),
        AppName = "Shell Demo",
        Category = "Utility",
        Version = "1.2.3",
        Comment = "Demo comment"
    };

    [Fact]
    public void Starts_With_Bash_Shebang()
    {
        var script = AppDirBuilder.GenerateAppRunScript(MakeOptions(), "ShellDemo");

        Assert.StartsWith("#!/bin/bash", script);
    }

    [Fact]
    public void Substitutes_App_Metadata()
    {
        var script = AppDirBuilder.GenerateAppRunScript(MakeOptions(), "ShellDemo");

        Assert.Contains("export APPIMAGE_NAME=\"Shell Demo\"", script);
        Assert.Contains("export APPIMAGE_COMMENT=\"Demo comment\"", script);
        Assert.Contains("export APPIMAGE_CATEGORY=\"Utility\"", script);
        Assert.Contains("export APPIMAGE_VERSION=\"1.2.3\"", script);
        Assert.Contains("EXEC_NAME=\"ShellDemo\"", script);
    }

    [Fact]
    public void Contains_SelfContained_And_FrameworkDependent_Launch_Paths()
    {
        var script = AppDirBuilder.GenerateAppRunScript(MakeOptions(), "ShellDemo");

        // Self-contained: exec the native binary
        Assert.Contains("exec \"$HERE/usr/bin/$EXEC_NAME\" \"$@\"", script);
        // Framework-dependent: fall back to dotnet
        Assert.Contains("exec \"$DOTNET_CMD\" \"$EXEC_NAME.dll\" \"$@\"", script);
    }

    [Fact]
    public void Bash_Braces_Are_Not_Mangled_By_Interpolation()
    {
        var script = AppDirBuilder.GenerateAppRunScript(MakeOptions(), "ShellDemo");

        // ${SELF%/*} style expansions must survive C# string interpolation
        Assert.Contains("HERE=${SELF%/*}", script);
        Assert.Contains("${XDG_DATA_DIRS:-/usr/local/share:/usr/share}", script);
        // No leftover doubled braces from the interpolated string
        Assert.DoesNotContain("{{", script);
        Assert.DoesNotContain("}}", script);
    }

    [Fact]
    public async Task CreateAppRunScript_Writes_Executable_File()
    {
        var tempDir = Directory.CreateTempSubdirectory("apprun-test").FullName;
        try
        {
            var path = Path.Combine(tempDir, "AppRun");
            await new AppDirBuilder().CreateAppRunScript(path, MakeOptions(), "ShellDemo");

            Assert.True(File.Exists(path));
            var mode = File.GetUnixFileMode(path);
            Assert.True(mode.HasFlag(UnixFileMode.UserExecute), "AppRun should be executable");
            Assert.Equal(AppDirBuilder.GenerateAppRunScript(MakeOptions(), "ShellDemo"),
                await File.ReadAllTextAsync(path));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
