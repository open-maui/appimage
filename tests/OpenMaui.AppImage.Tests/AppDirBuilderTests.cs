using OpenMaui.AppImage.Core;
using Xunit;

namespace OpenMaui.AppImage.Tests;

public class AppDirBuilderTests
{
    [Fact]
    public async Task PopulateAsync_Builds_Complete_AppDir_From_Flat_Publish_Dir()
    {
        var tempRoot = Directory.CreateTempSubdirectory("appdir-test").FullName;
        try
        {
            // Fake flat publish directory
            var publishDir = Path.Combine(tempRoot, "publish");
            Directory.CreateDirectory(publishDir);
            await File.WriteAllTextAsync(Path.Combine(publishDir, "MyApp.dll"), "fake");
            await File.WriteAllTextAsync(Path.Combine(publishDir, "MyApp.runtimeconfig.json"), "{}");

            var appDir = Path.Combine(tempRoot, "My App.AppDir");
            var options = new PackageOptions
            {
                InputDirectory = new DirectoryInfo(publishDir),
                OutputFile = new FileInfo(Path.Combine(tempRoot, "out.AppImage")),
                AppName = "My App",
                Category = "Utility",
                Version = "1.0.0",
                Comment = "Test app"
            };

            await new AppDirBuilder().PopulateAsync(appDir, options, "MyApp");

            // App files copied into usr/bin
            Assert.True(File.Exists(Path.Combine(appDir, "usr", "bin", "MyApp.dll")));
            // AppRun generated and executable
            var appRun = Path.Combine(appDir, "AppRun");
            Assert.True(File.Exists(appRun));
            Assert.True(File.GetUnixFileMode(appRun).HasFlag(UnixFileMode.UserExecute));
            // Desktop file named after the sanitized app name
            var desktop = Path.Combine(appDir, "My_App.desktop");
            Assert.True(File.Exists(desktop));
            Assert.Contains("StartupWMClass=MyApp", await File.ReadAllTextAsync(desktop));
            // Default icon generated (no icon supplied)
            Assert.True(File.Exists(Path.Combine(appDir, "My_App.svg")));
            Assert.True(File.Exists(Path.Combine(appDir, "usr", "bin", "appicon.svg")));
            Assert.True(File.Exists(Path.Combine(
                appDir, "usr", "share", "icons", "hicolor", "scalable", "apps", "My_App.svg")));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task PopulateAsync_Uses_Input_As_AppDir_Root_For_PreBuilt_Tree()
    {
        var tempRoot = Directory.CreateTempSubdirectory("appdir-test").FullName;
        try
        {
            // Fake FHS tree (extracted .deb)
            var inputDir = Path.Combine(tempRoot, "extracted");
            Directory.CreateDirectory(Path.Combine(inputDir, "usr", "bin"));
            Directory.CreateDirectory(Path.Combine(inputDir, "usr", "lib", "myapp"));
            await File.WriteAllTextAsync(Path.Combine(inputDir, "usr", "bin", "myapp"), "fake");
            await File.WriteAllTextAsync(Path.Combine(inputDir, "usr", "lib", "myapp", "data.bin"), "fake");

            var appDir = Path.Combine(tempRoot, "MyApp.AppDir");
            var options = new PackageOptions
            {
                InputDirectory = new DirectoryInfo(inputDir),
                OutputFile = new FileInfo(Path.Combine(tempRoot, "out.AppImage")),
                AppName = "MyApp",
                Category = "Utility",
                Version = "1.0.0",
                Comment = "Test app",
                PreBuiltAppDir = true
            };

            await new AppDirBuilder().PopulateAsync(appDir, options, "myapp");

            // Tree preserved at AppDir root, not nested under usr/bin/usr/bin
            Assert.True(File.Exists(Path.Combine(appDir, "usr", "bin", "myapp")));
            Assert.True(File.Exists(Path.Combine(appDir, "usr", "lib", "myapp", "data.bin")));
            Assert.False(Directory.Exists(Path.Combine(appDir, "usr", "bin", "usr")));
            Assert.True(File.Exists(Path.Combine(appDir, "AppRun")));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task SetupIcon_Copies_Provided_Icon_To_All_Locations()
    {
        var tempRoot = Directory.CreateTempSubdirectory("icon-test").FullName;
        try
        {
            var iconFile = Path.Combine(tempRoot, "icon.png");
            await File.WriteAllTextAsync(iconFile, "fake-png");
            var appDir = Path.Combine(tempRoot, "App.AppDir");
            // In the real flow the app files are copied into usr/bin before SetupIcon runs
            Directory.CreateDirectory(Path.Combine(appDir, "usr", "bin"));

            var options = new PackageOptions
            {
                InputDirectory = new DirectoryInfo(tempRoot),
                OutputFile = new FileInfo(Path.Combine(tempRoot, "out.AppImage")),
                AppName = "My App",
                Category = "Utility",
                Version = "1.0.0",
                Comment = "Test",
                IconPath = new FileInfo(iconFile)
            };

            await new AppDirBuilder().SetupIcon(appDir, options);

            Assert.True(File.Exists(Path.Combine(appDir, "My_App.png")));
            Assert.True(File.Exists(Path.Combine(
                appDir, "usr", "share", "icons", "hicolor", "256x256", "apps", "My_App.png")));
            Assert.True(File.Exists(Path.Combine(
                appDir, "usr", "share", "icons", "hicolor", "scalable", "apps", "My_App.png")));
            Assert.True(File.Exists(Path.Combine(appDir, "usr", "bin", "appicon.png")));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void AutoDetectExecutable_Finds_Dll_Matching_App_Name()
    {
        var tempDir = Directory.CreateTempSubdirectory("exec-test").FullName;
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "ShellDemo.dll"), "fake");

            var result = new AppDirBuilder().AutoDetectExecutable(tempDir, "Shell Demo");

            Assert.Equal("ShellDemo", result);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void AutoDetectExecutable_Finds_Main_Dll_Via_RuntimeConfig()
    {
        var tempDir = Directory.CreateTempSubdirectory("exec-test").FullName;
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "SomeOtherName.dll"), "fake");
            File.WriteAllText(Path.Combine(tempDir, "SomeOtherName.runtimeconfig.json"), "{}");

            var result = new AppDirBuilder().AutoDetectExecutable(tempDir, "Unrelated Name");

            Assert.Equal("SomeOtherName", result);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void AutoDetectExecutable_Finds_Native_Elf_Binary()
    {
        var tempDir = Directory.CreateTempSubdirectory("exec-test").FullName;
        try
        {
            // ELF magic: 0x7F 'E' 'L' 'F'
            File.WriteAllBytes(Path.Combine(tempDir, "nativeapp"),
                new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F', 0, 0, 0, 0 });
            File.WriteAllText(Path.Combine(tempDir, "nativeapp.dll"), "fake");

            var result = new AppDirBuilder().AutoDetectExecutable(tempDir, "Whatever");

            Assert.Equal("nativeapp", result);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
