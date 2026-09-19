using OpenMaui.AppImage.Core;
using Xunit;

namespace OpenMaui.AppImage.Tests;

public class HostDepsCheckBlockTests
{
    private static IReadOnlyList<HostDependency> BaseDeps() =>
        DependencyScanner.Map(new HashSet<string> { DependencyScanner.BaseAssembly }).Dependencies;

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
    public void Block_Probes_Sonames_Via_Ldconfig()
    {
        var block = DependencyScanner.GenerateAppRunCheckBlock(BaseDeps());

        Assert.Contains("ldconfig -p", block);
        Assert.Contains("om_has \"libX11.so.6\"", block);
        Assert.Contains("om_has \"libwayland-client.so.0\"", block);
        Assert.Contains("om_has \"libfontconfig.so.1\"", block);
    }

    [Fact]
    public void Required_Misses_Abort_With_Distro_Install_Commands()
    {
        var block = DependencyScanner.GenerateAppRunCheckBlock(BaseDeps());

        Assert.Contains("OM_MISSING_REQ", block);
        Assert.Contains("exit 1", block);
        Assert.Contains("sudo dnf install$OM_REQ_DNF", block);
        Assert.Contains("sudo apt install$OM_REQ_APT", block);
        // Required package names are wired into the abort message
        Assert.Contains("libx11-6", block);
    }

    [Fact]
    public void Optional_Misses_Only_Print_A_Note_And_Continue()
    {
        var block = DependencyScanner.GenerateAppRunCheckBlock(BaseDeps());

        Assert.Contains("OM_MISSING_OPT", block);
        Assert.Contains("features disabled", block);
        // Optional deps never populate the aborting variable
        Assert.DoesNotContain("OM_MISSING_REQ=\"$OM_MISSING_REQ libcups.so.2", block);
    }

    [Fact]
    public void Alternative_Sonames_Are_Probed_With_Or()
    {
        var block = DependencyScanner.GenerateAppRunCheckBlock(BaseDeps());

        Assert.Contains("om_has \"libayatana-appindicator3.so.1\" || om_has \"libappindicator3.so.1\"", block);
    }

    [Fact]
    public void Friendly_Message_Uses_Zenity_Then_Kdialog_Then_Stderr()
    {
        var block = DependencyScanner.GenerateAppRunCheckBlock(BaseDeps());

        Assert.Contains("command -v zenity", block);
        Assert.Contains("command -v kdialog", block);
        Assert.Contains(">&2", block);
    }

    [Fact]
    public void AppRun_Script_Injects_Block_When_Provided()
    {
        var block = DependencyScanner.GenerateAppRunCheckBlock(BaseDeps());
        var script = AppDirBuilder.GenerateAppRunScript(MakeOptions(), "ShellDemo", block);

        Assert.Contains("Host dependency check", script);
        Assert.Contains("ldconfig -p", script);
        // Injected before the install/launch logic
        Assert.True(script.IndexOf("ldconfig -p", StringComparison.Ordinal)
                    < script.IndexOf("INSTALLED_MARKER", StringComparison.Ordinal));
    }

    [Fact]
    public void AppRun_Script_Without_Block_Has_No_Dependency_Check()
    {
        var script = AppDirBuilder.GenerateAppRunScript(MakeOptions(), "ShellDemo");

        Assert.DoesNotContain("ldconfig", script);
        Assert.DoesNotContain("Host dependency check", script);
    }

    [Fact]
    public async Task PopulateAsync_With_HostDepsCheck_Injects_Block_For_OpenMaui_App()
    {
        var tempRoot = Directory.CreateTempSubdirectory("depscheck-test").FullName;
        try
        {
            var publishDir = Path.Combine(tempRoot, "publish");
            Directory.CreateDirectory(publishDir);
            await File.WriteAllTextAsync(Path.Combine(publishDir, "MyApp.dll"), "fake");
            await File.WriteAllTextAsync(Path.Combine(publishDir, DependencyScanner.BaseAssembly), "fake");

            var appDir = Path.Combine(tempRoot, "MyApp.AppDir");
            var options = new PackageOptions
            {
                InputDirectory = new DirectoryInfo(publishDir),
                OutputFile = new FileInfo(Path.Combine(tempRoot, "out.AppImage")),
                AppName = "MyApp",
                Category = "Utility",
                Version = "1.0.0",
                Comment = "Test app",
                HostDepsCheck = true
            };

            await new AppDirBuilder().PopulateAsync(appDir, options, "MyApp");

            var appRun = await File.ReadAllTextAsync(Path.Combine(appDir, "AppRun"));
            Assert.Contains("Host dependency check", appRun);
            Assert.Contains("libX11.so.6", appRun);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task PopulateAsync_With_HostDepsCheck_Skips_Block_When_No_OpenMaui_Assemblies()
    {
        var tempRoot = Directory.CreateTempSubdirectory("depscheck-test").FullName;
        try
        {
            var publishDir = Path.Combine(tempRoot, "publish");
            Directory.CreateDirectory(publishDir);
            await File.WriteAllTextAsync(Path.Combine(publishDir, "MyApp.dll"), "fake");

            var appDir = Path.Combine(tempRoot, "MyApp.AppDir");
            var options = new PackageOptions
            {
                InputDirectory = new DirectoryInfo(publishDir),
                OutputFile = new FileInfo(Path.Combine(tempRoot, "out.AppImage")),
                AppName = "MyApp",
                Category = "Utility",
                Version = "1.0.0",
                Comment = "Test app",
                HostDepsCheck = true
            };

            await new AppDirBuilder().PopulateAsync(appDir, options, "MyApp");

            var appRun = await File.ReadAllTextAsync(Path.Combine(appDir, "AppRun"));
            Assert.DoesNotContain("ldconfig", appRun);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
