using OpenMaui.AppImage.Core;
using Xunit;

namespace OpenMaui.AppImage.Tests;

public class FuseDetectionTests
{
    [Fact]
    public void Returns_False_When_Device_Is_Missing()
    {
        Assert.False(AppImagePacker.IsFuseAvailable("/nonexistent/dev/fuse-" + Guid.NewGuid().ToString("N")));
    }

    [Fact]
    public void Returns_True_When_Device_Can_Be_Opened_ReadWrite()
    {
        // Simulate a usable /dev/fuse with a read-write openable file.
        var fakeFuse = Path.Combine(Path.GetTempPath(), "fake-fuse-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(fakeFuse, "");
        try
        {
            Assert.True(AppImagePacker.IsFuseAvailable(fakeFuse));
        }
        finally
        {
            File.Delete(fakeFuse);
        }
    }

    [Fact]
    public void Returns_False_When_Device_Exists_But_Cannot_Be_Opened()
    {
        // Simulate the container case: node exists but opening it is denied.
        var fakeFuse = Path.Combine(Path.GetTempPath(), "fake-fuse-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(fakeFuse, "");
        File.SetUnixFileMode(fakeFuse, UnixFileMode.UserRead); // read-only: ReadWrite open fails
        try
        {
            Assert.False(AppImagePacker.IsFuseAvailable(fakeFuse));
        }
        finally
        {
            File.SetUnixFileMode(fakeFuse, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Delete(fakeFuse);
        }
    }
}
