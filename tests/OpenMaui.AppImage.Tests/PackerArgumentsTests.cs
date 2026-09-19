using OpenMaui.AppImage.Core;
using Xunit;

namespace OpenMaui.AppImage.Tests;

public class PackerArgumentsTests
{
    [Fact]
    public void Default_Arguments_Are_Just_AppDir_And_Output()
    {
        var args = AppImagePacker.BuildToolArguments("/tmp/My.AppDir", "/tmp/out.AppImage");

        Assert.Equal("\"/tmp/My.AppDir\" \"/tmp/out.AppImage\"", args);
    }

    [Fact]
    public void UpdateInfo_Is_Passed_Through_As_Dash_U()
    {
        var args = AppImagePacker.BuildToolArguments("/tmp/My.AppDir", "/tmp/out.AppImage",
            updateInfo: "gh-releases-zsync|user|repo|latest|*.AppImage.zsync");

        Assert.StartsWith("-u \"gh-releases-zsync|user|repo|latest|*.AppImage.zsync\" ", args);
        Assert.EndsWith("\"/tmp/My.AppDir\" \"/tmp/out.AppImage\"", args);
    }

    [Fact]
    public void Sign_Adds_Sign_Flag()
    {
        var args = AppImagePacker.BuildToolArguments("/tmp/My.AppDir", "/tmp/out.AppImage", sign: true);

        Assert.Contains("--sign", args);
        Assert.DoesNotContain("--sign-key", args);
    }

    [Fact]
    public void SignKey_Adds_Key_And_Implies_Sign()
    {
        var args = AppImagePacker.BuildToolArguments("/tmp/My.AppDir", "/tmp/out.AppImage",
            signKey: "ABCDEF1234567890");

        Assert.Contains("--sign ", args);
        Assert.Contains("--sign-key ABCDEF1234567890", args);
    }

    [Fact]
    public void All_Passthrough_Options_Combine_In_Order()
    {
        var args = AppImagePacker.BuildToolArguments("/tmp/My.AppDir", "/tmp/out.AppImage",
            updateInfo: "zsync|https://example.com/My.AppImage.zsync", sign: true, signKey: "KEY123");

        Assert.Equal(
            "-u \"zsync|https://example.com/My.AppImage.zsync\" --sign --sign-key KEY123 \"/tmp/My.AppDir\" \"/tmp/out.AppImage\"",
            args);
    }
}
