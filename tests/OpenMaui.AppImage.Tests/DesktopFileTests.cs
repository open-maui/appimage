using OpenMaui.AppImage.Core;
using Xunit;

namespace OpenMaui.AppImage.Tests;

public class DesktopFileTests
{
    private static PackageOptions MakeOptions(string appName = "My App") => new()
    {
        InputDirectory = new DirectoryInfo("/tmp/does-not-matter"),
        OutputFile = new FileInfo("/tmp/out.AppImage"),
        AppName = appName,
        Category = "Development",
        Version = "2.3.4",
        Comment = "A test application"
    };

    [Fact]
    public void Contains_All_Required_Fields()
    {
        var content = AppDirBuilder.GenerateDesktopFileContent(MakeOptions());

        Assert.StartsWith("[Desktop Entry]", content);
        Assert.Contains("Type=Application", content);
        Assert.Contains("Name=My App", content);
        Assert.Contains("Comment=A test application", content);
        Assert.Contains("Categories=Development;", content);
        Assert.Contains("Terminal=false", content);
        Assert.Contains("X-AppImage-Version=2.3.4", content);
    }

    [Fact]
    public void Exec_And_Icon_Use_Sanitized_Name()
    {
        var content = AppDirBuilder.GenerateDesktopFileContent(MakeOptions("My App"));

        Assert.Contains("Exec=My_App", content);
        Assert.Contains("Icon=My_App", content);
    }

    [Theory]
    [InlineData("My App", "MyApp")]
    [InlineData("Shell_Demo", "ShellDemo")]
    [InlineData("My_Cool App", "MyCoolApp")]
    [InlineData("Simple", "Simple")]
    public void StartupWMClass_Strips_Spaces_And_Underscores(string appName, string expectedWmClass)
    {
        var content = AppDirBuilder.GenerateDesktopFileContent(MakeOptions(appName));

        Assert.Contains($"StartupWMClass={expectedWmClass}", content);
    }

    [Theory]
    [InlineData("My App", "My_App")]
    [InlineData("a/b c", "a_b_c")]
    [InlineData("NoChange", "NoChange")]
    public void SanitizeFileName_Escapes_Invalid_Chars_And_Spaces(string input, string expected)
    {
        Assert.Equal(expected, AppDirBuilder.SanitizeFileName(input));
    }
}
