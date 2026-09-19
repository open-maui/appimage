using OpenMaui.AppImage.Core;
using Xunit;

namespace OpenMaui.AppImage.Tests;

public class DependencyScannerTests
{
    private static ISet<string> Set(params string[] names) =>
        new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Base_Assembly_Maps_To_Required_Core_Set_And_Optional_Features()
    {
        var result = DependencyScanner.Map(Set(DependencyScanner.BaseAssembly));

        var required = result.Dependencies.Where(d => d.Required).ToList();
        Assert.Contains(required, d => d.Sonames.Contains("libX11.so.6"));
        Assert.Contains(required, d => d.Sonames.Contains("libwayland-client.so.0"));
        Assert.Contains(required, d => d.Sonames.Contains("libfontconfig.so.1"));

        var optional = result.Dependencies.Where(d => !d.Required).ToList();
        Assert.Contains(optional, d => d.Sonames.Contains("libcups.so.2") && d.Feature == "printing");
        Assert.Contains(optional, d => d.Sonames.Contains("libwebkit2gtk-4.1.so.0") && d.Feature == "WebView");
        // Tray accepts either ayatana or legacy appindicator
        Assert.Contains(optional, d =>
            d.Sonames.Contains("libayatana-appindicator3.so.1") &&
            d.Sonames.Contains("libappindicator3.so.1") &&
            d.Feature == "tray icon");
    }

    [Fact]
    public void MediaElement_Assembly_Adds_GStreamer_With_Plugin_Packages()
    {
        var result = DependencyScanner.Map(Set(DependencyScanner.BaseAssembly, DependencyScanner.MediaElementAssembly));

        var gst = Assert.Single(result.Dependencies, d => d.Sonames.Contains("libgstreamer-1.0.so.0"));
        Assert.False(gst.Required);
        Assert.Contains("MediaElement", gst.Feature);
        Assert.Contains("gstreamer1-plugins-base", gst.FedoraPackages);
        Assert.Contains("gstreamer1.0-plugins-good", gst.DebianPackages);
    }

    [Fact]
    public void Maps_Assembly_Adds_A_Note_And_No_Dependencies()
    {
        var result = DependencyScanner.Map(Set(DependencyScanner.MapsAssembly));

        Assert.Empty(result.Dependencies);
        Assert.Contains(result.Notes, n => n.Contains("network"));
    }

    [Fact]
    public void Unrelated_Assemblies_Map_To_Nothing()
    {
        var result = DependencyScanner.Map(Set("System.Text.Json.dll", "MyApp.dll"));

        Assert.Empty(result.DetectedAssemblies);
        Assert.Empty(result.Dependencies);
        Assert.Empty(result.Notes);
    }

    [Fact]
    public void Scan_Finds_Assemblies_In_Nested_Directories()
    {
        var tempDir = Directory.CreateTempSubdirectory("scan-test").FullName;
        try
        {
            // Fake AppDir tree: assemblies under usr/bin
            var binDir = Path.Combine(tempDir, "usr", "bin");
            Directory.CreateDirectory(binDir);
            File.WriteAllText(Path.Combine(binDir, DependencyScanner.BaseAssembly), "fake");
            File.WriteAllText(Path.Combine(binDir, DependencyScanner.MediaElementAssembly), "fake");
            File.WriteAllText(Path.Combine(binDir, "MyApp.dll"), "fake");

            var result = DependencyScanner.Scan(tempDir);

            Assert.Contains(DependencyScanner.BaseAssembly, result.DetectedAssemblies);
            Assert.Contains(DependencyScanner.MediaElementAssembly, result.DetectedAssemblies);
            Assert.Contains(result.Dependencies, d => d.Sonames.Contains("libgstreamer-1.0.so.0"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Scan_Of_Missing_Directory_Returns_Empty_Result()
    {
        var result = DependencyScanner.Scan("/nonexistent-" + Guid.NewGuid().ToString("N"));

        Assert.Empty(result.Dependencies);
    }

    [Fact]
    public void Report_Lists_Required_Optional_And_Install_Commands()
    {
        var result = DependencyScanner.Map(Set(
            DependencyScanner.BaseAssembly,
            DependencyScanner.MediaElementAssembly,
            DependencyScanner.MapsAssembly));

        var report = DependencyScanner.FormatReport(result);

        Assert.Contains("Host runtime dependencies:", report);
        Assert.Contains("Required:", report);
        Assert.Contains("Optional (feature-gated):", report);
        Assert.Contains("libX11.so.6", report);
        Assert.Contains("sudo dnf install", report);
        Assert.Contains("sudo apt install", report);
        Assert.Contains("libx11-6", report);
        Assert.Contains("network access only", report);
    }

    [Fact]
    public void Report_Says_None_Detected_For_Empty_Result()
    {
        var report = DependencyScanner.FormatReport(DependencyScanner.Map(Set("MyApp.dll")));

        Assert.Contains("Host runtime dependencies: none detected", report);
    }
}
