using OpenMaui.AppImage.Core;
using Xunit;

namespace OpenMaui.AppImage.Tests;

public class AppStreamGeneratorTests
{
    [Fact]
    public void Generates_Minimal_Desktop_Application_Component()
    {
        var xml = AppStreamGenerator.Generate(
            "com.example.MyApp", "My App", "A test application",
            "My_App.desktop", "MyApp");

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?>", xml);
        Assert.Contains("<component type=\"desktop-application\">", xml);
        Assert.Contains("<id>com.example.MyApp</id>", xml);
        Assert.Contains("<metadata_license>CC0-1.0</metadata_license>", xml);
        Assert.Contains("<name>My App</name>", xml);
        Assert.Contains("<summary>A test application</summary>", xml);
        Assert.Contains("<launchable type=\"desktop-id\">My_App.desktop</launchable>", xml);
        Assert.Contains("<binary>MyApp</binary>", xml);
        Assert.Contains("</component>", xml);
    }

    [Fact]
    public void Developer_Element_Only_Appears_When_Given()
    {
        var without = AppStreamGenerator.Generate(
            "com.example.MyApp", "My App", "Summary", "My_App.desktop", "MyApp");
        var with = AppStreamGenerator.Generate(
            "com.example.MyApp", "My App", "Summary", "My_App.desktop", "MyApp", "MarketAlly");

        Assert.DoesNotContain("<developer>", without);
        Assert.Contains("<developer>", with);
        Assert.Contains("<name>MarketAlly</name>", with);
    }

    [Fact]
    public void Escapes_Xml_Special_Characters()
    {
        var xml = AppStreamGenerator.Generate(
            "com.example.MyApp", "Tom & Jerry <App>", "Fast & \"furious\"",
            "My_App.desktop", "MyApp");

        Assert.Contains("<name>Tom &amp; Jerry &lt;App&gt;</name>", xml);
        Assert.Contains("<summary>Fast &amp; &quot;furious&quot;</summary>", xml);
        Assert.DoesNotContain("Tom & Jerry", xml);
    }

    [Theory]
    [InlineData("com.example.MyApp", true)]
    [InlineData("io.github.user.app", true)]
    [InlineData("org.gnome.Calculator", true)]
    [InlineData("MyApp", false)]
    [InlineData("com.example", false)]           // needs at least three segments
    [InlineData("com..MyApp", false)]            // empty segment
    [InlineData("com.example.My App", false)]    // whitespace
    [InlineData("com.1example.MyApp", false)]    // segment starting with a digit
    [InlineData("", false)]
    [InlineData(null, false)]
    public void LooksReverseDns_Validates_Shape(string? appId, bool expected)
    {
        Assert.Equal(expected, AppStreamGenerator.LooksReverseDns(appId));
    }

    [Theory]
    [InlineData("My App", "myapp")]
    [InlineData("Shell_Demo 2", "shelldemo2")]
    [InlineData("!!!", "app")]
    public void SanitizeIdSegment_Keeps_Lowercase_Alphanumerics(string input, string expected)
    {
        Assert.Equal(expected, AppStreamGenerator.SanitizeIdSegment(input));
    }

    [Fact]
    public async Task CreateMetainfoFile_Writes_Into_Usr_Share_Metainfo()
    {
        var tempRoot = Directory.CreateTempSubdirectory("metainfo-test").FullName;
        try
        {
            var appDir = Path.Combine(tempRoot, "MyApp.AppDir");
            var options = new PackageOptions
            {
                InputDirectory = new DirectoryInfo(tempRoot),
                OutputFile = new FileInfo(Path.Combine(tempRoot, "out.AppImage")),
                AppName = "My App",
                Category = "Utility",
                Version = "1.0.0",
                Comment = "Test app",
                AppId = "com.example.MyApp",
                GenerateMetainfo = true,
                Developer = "MarketAlly"
            };

            await new AppDirBuilder().CreateMetainfoFile(appDir, options, "MyApp");

            var path = Path.Combine(appDir, "usr", "share", "metainfo", "com.example.MyApp.metainfo.xml");
            Assert.True(File.Exists(path));
            var xml = await File.ReadAllTextAsync(path);
            Assert.Contains("<id>com.example.MyApp</id>", xml);
            Assert.Contains("<launchable type=\"desktop-id\">My_App.desktop</launchable>", xml);
            Assert.Contains("<binary>MyApp</binary>", xml);
            Assert.Contains("<name>MarketAlly</name>", xml);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task CreateMetainfoFile_Derives_Id_When_AppId_Missing()
    {
        var tempRoot = Directory.CreateTempSubdirectory("metainfo-test").FullName;
        try
        {
            var appDir = Path.Combine(tempRoot, "MyApp.AppDir");
            var options = new PackageOptions
            {
                InputDirectory = new DirectoryInfo(tempRoot),
                OutputFile = new FileInfo(Path.Combine(tempRoot, "out.AppImage")),
                AppName = "My App",
                Category = "Utility",
                Version = "1.0.0",
                Comment = "Test app",
                GenerateMetainfo = true
            };

            await new AppDirBuilder().CreateMetainfoFile(appDir, options, "MyApp");

            Assert.True(File.Exists(Path.Combine(
                appDir, "usr", "share", "metainfo", "com.openmaui.myapp.metainfo.xml")));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
