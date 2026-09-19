using System.CommandLine;
using System.CommandLine.Parsing;
using OpenMaui.AppImage.Commands;
using OpenMaui.AppImage.Core;
using Xunit;

namespace OpenMaui.AppImage.Tests;

public class ArgumentValidationTests
{
    [Fact]
    public void Missing_Input_Is_A_Parse_Error()
    {
        var root = PackageCommand.Create();
        var result = root.Parse("--output out.AppImage --name MyApp");

        Assert.Contains(result.Errors, e => e.Message.Contains("--input"));
    }

    [Fact]
    public void Missing_Output_Is_A_Parse_Error()
    {
        var root = PackageCommand.Create();
        var result = root.Parse("--input publish --name MyApp");

        Assert.Contains(result.Errors, e => e.Message.Contains("--output"));
    }

    [Fact]
    public void Missing_Name_Is_A_Parse_Error()
    {
        var root = PackageCommand.Create();
        var result = root.Parse("--input publish --output out.AppImage");

        Assert.Contains(result.Errors, e => e.Message.Contains("--name"));
    }

    [Fact]
    public void All_Required_Options_Parse_Without_Errors()
    {
        var root = PackageCommand.Create();
        var result = root.Parse("-i publish -o out.AppImage -n MyApp");

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void All_Options_Parse_Without_Errors()
    {
        var root = PackageCommand.Create();
        var result = root.Parse(
            "-i publish -o out.flatpak -n MyApp -e MyApp --icon icon.svg " +
            "-c Development --app-version 2.0.0 --comment Hello -f flatpak " +
            "--app-id com.example.MyApp --appdir --no-fuse");

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Input_And_Project_Are_Mutually_Exclusive()
    {
        var root = PackageCommand.Create();
        var result = root.Parse("--input publish --project ./MyApp --output out.AppImage --name MyApp");

        Assert.Contains(result.Errors, e => e.Message.Contains("mutually exclusive"));
    }

    [Fact]
    public void Project_Alone_Parses_Without_Errors()
    {
        // --name and --output are derived when --project is used
        var root = PackageCommand.Create();
        var result = root.Parse("--project ./MyApp");

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Output_Is_Required_With_Input()
    {
        var root = PackageCommand.Create();
        var result = root.Parse("--input publish --name MyApp");

        Assert.Contains(result.Errors, e => e.Message.Contains("--output"));
    }

    [Fact]
    public void All_New_Options_Parse_Without_Errors()
    {
        var root = PackageCommand.Create();
        var result = root.Parse(
            "--project ./MyApp --rid linux-arm64 --no-fetch --host-deps-check " +
            "--update-info \"gh-releases-zsync|user|repo|latest|*.AppImage.zsync\" " +
            "--sign --sign-key ABC123 --metainfo --app-id com.example.MyApp --developer MarketAlly");

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Unknown_Option_Is_A_Parse_Error()
    {
        var root = PackageCommand.Create();
        var result = root.Parse("-i publish -o out.AppImage -n MyApp --bogus-option");

        Assert.NotEmpty(result.Errors);
    }

    [Theory]
    [InlineData("flatpak", PackageCommand.PackageFormat.Flatpak)]
    [InlineData("FLATPAK", PackageCommand.PackageFormat.Flatpak)]
    [InlineData("appimage", PackageCommand.PackageFormat.AppImage)]
    // Historical behavior: any non-"flatpak" value falls through to AppImage.
    [InlineData("bogus", PackageCommand.PackageFormat.AppImage)]
    public void ResolveFormat_Matches_Historical_Dispatch(string format, PackageCommand.PackageFormat expected)
    {
        Assert.Equal(expected, PackageCommand.ResolveFormat(format));
    }

    [Fact]
    public async Task AppImage_Flow_Fails_For_Missing_Input_Directory()
    {
        var options = new PackageOptions
        {
            InputDirectory = new DirectoryInfo("/nonexistent-input-dir-" + Guid.NewGuid().ToString("N")),
            OutputFile = new FileInfo(Path.Combine(Path.GetTempPath(), "out.AppImage")),
            AppName = "Test App",
            Category = "Utility",
            Version = "1.0.0",
            Comment = "Test"
        };

        Assert.False(await PackageCommand.RunAppImageAsync(options));
    }

    [Fact]
    public async Task Flatpak_Flow_Fails_For_Missing_Input_Directory()
    {
        var options = new PackageOptions
        {
            InputDirectory = new DirectoryInfo("/nonexistent-input-dir-" + Guid.NewGuid().ToString("N")),
            OutputFile = new FileInfo(Path.Combine(Path.GetTempPath(), "out.flatpak")),
            AppName = "Test App",
            Category = "Utility",
            Version = "1.0.0",
            Comment = "Test"
        };

        Assert.False(await new FlatpakPacker().BuildAsync(options));
    }

    [Fact]
    public async Task Flatpak_Flow_Fails_When_Main_Dll_Is_Missing()
    {
        var tempDir = Directory.CreateTempSubdirectory("flatpak-test").FullName;
        try
        {
            var options = new PackageOptions
            {
                InputDirectory = new DirectoryInfo(tempDir),
                OutputFile = new FileInfo(Path.Combine(Path.GetTempPath(), "out.flatpak")),
                AppName = "Test App",
                Category = "Utility",
                Version = "1.0.0",
                Comment = "Test"
            };

            Assert.False(await new FlatpakPacker().BuildAsync(options));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
