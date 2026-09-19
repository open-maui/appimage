using OpenMaui.AppImage.Core;
using Xunit;

namespace OpenMaui.AppImage.Tests;

public class AppImageToolFetcherTests
{
    [Theory]
    [InlineData("x86_64")]
    [InlineData("aarch64")]
    public void DownloadUrl_Points_At_Official_Continuous_Release(string arch)
    {
        Assert.Equal(
            $"https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-{arch}.AppImage",
            AppImageToolFetcher.GetDownloadUrl(arch));
    }

    [Fact]
    public void MapArchitecture_Returns_A_Known_Release_Arch()
    {
        Assert.Contains(AppImageToolFetcher.MapArchitecture(),
            new[] { "x86_64", "aarch64", "armhf", "i686" });
    }

    [Fact]
    public void CacheDirectory_Respects_XDG_CACHE_HOME()
    {
        var original = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        try
        {
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", "/custom/cache");
            Assert.Equal("/custom/cache/openmaui-appimage", AppImageToolFetcher.GetCacheDirectory());
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", original);
        }
    }

    [Fact]
    public void CacheDirectory_Falls_Back_To_Home_Dot_Cache()
    {
        var original = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        try
        {
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", null);
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Assert.Equal(Path.Combine(home, ".cache", "openmaui-appimage"),
                AppImageToolFetcher.GetCacheDirectory());
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", original);
        }
    }

    [Fact]
    public void CachedToolPath_Is_Arch_Specific()
    {
        var original = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        try
        {
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", "/custom/cache");
            Assert.Equal("/custom/cache/openmaui-appimage/appimagetool-x86_64.AppImage",
                AppImageToolFetcher.GetCachedToolPath("x86_64"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", original);
        }
    }

    [Fact]
    public async Task EnsureToolAsync_Returns_Cached_Copy_Without_Network()
    {
        var tempCache = Directory.CreateTempSubdirectory("fetcher-test").FullName;
        var original = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        try
        {
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", tempCache);
            var cached = AppImageToolFetcher.GetCachedToolPath(AppImageToolFetcher.MapArchitecture());
            Directory.CreateDirectory(Path.GetDirectoryName(cached)!);
            await File.WriteAllTextAsync(cached, "fake-tool");

            // noFetch:true guarantees no network is attempted; the cached copy is used.
            Assert.Equal(cached, await AppImageToolFetcher.EnsureToolAsync(noFetch: true));
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", original);
            Directory.Delete(tempCache, recursive: true);
        }
    }

    [Fact]
    public async Task EnsureToolAsync_Returns_Null_When_NoFetch_And_Nothing_Cached()
    {
        var tempCache = Directory.CreateTempSubdirectory("fetcher-test").FullName;
        var original = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        try
        {
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", tempCache);

            Assert.Null(await AppImageToolFetcher.EnsureToolAsync(noFetch: true));
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", original);
            Directory.Delete(tempCache, recursive: true);
        }
    }
}
