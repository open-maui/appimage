using OpenMaui.AppImage.Core;
using Xunit;

namespace OpenMaui.AppImage.Tests;

public class ProjectPublisherTests
{
    private static string WriteCsproj(string dir, string fileName, string propertyGroupXml = "")
    {
        var path = Path.Combine(dir, fileName);
        File.WriteAllText(path, $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
{propertyGroupXml}
  </PropertyGroup>
</Project>");
        return path;
    }

    [Fact]
    public void BuildPublishArguments_Uses_Release_Rid_And_SelfContained()
    {
        var args = ProjectPublisher.BuildPublishArguments("/src/MyApp/MyApp.csproj", "linux-x64");

        Assert.Contains("publish \"/src/MyApp/MyApp.csproj\"", args);
        Assert.Contains("-c Release", args);
        Assert.Contains("-r linux-x64", args);
        Assert.Contains("--self-contained true", args);
    }

    [Fact]
    public void GetHostRid_Returns_A_Linux_Rid()
    {
        Assert.StartsWith("linux-", ProjectPublisher.GetHostRid());
    }

    [Fact]
    public void ResolveProjectFile_Accepts_A_Csproj_File()
    {
        var tempDir = Directory.CreateTempSubdirectory("proj-test").FullName;
        try
        {
            var csproj = WriteCsproj(tempDir, "MyApp.csproj");
            Assert.Equal(Path.GetFullPath(csproj), ProjectPublisher.ResolveProjectFile(csproj));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ResolveProjectFile_Accepts_A_Directory_With_A_Single_Csproj()
    {
        var tempDir = Directory.CreateTempSubdirectory("proj-test").FullName;
        try
        {
            var csproj = WriteCsproj(tempDir, "MyApp.csproj");
            Assert.Equal(Path.GetFullPath(csproj), ProjectPublisher.ResolveProjectFile(tempDir));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ResolveProjectFile_Returns_Null_When_No_Csproj_Exists()
    {
        var tempDir = Directory.CreateTempSubdirectory("proj-test").FullName;
        try
        {
            Assert.Null(ProjectPublisher.ResolveProjectFile(tempDir));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ResolveProjectFile_Returns_Null_For_Ambiguous_Directory()
    {
        var tempDir = Directory.CreateTempSubdirectory("proj-test").FullName;
        try
        {
            WriteCsproj(tempDir, "One.csproj");
            WriteCsproj(tempDir, "Two.csproj");
            Assert.Null(ProjectPublisher.ResolveProjectFile(tempDir));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void DeriveAppName_Prefers_AssemblyName()
    {
        var tempDir = Directory.CreateTempSubdirectory("proj-test").FullName;
        try
        {
            var csproj = WriteCsproj(tempDir, "MyApp.csproj", "    <AssemblyName>CoolApp</AssemblyName>");
            Assert.Equal("CoolApp", ProjectPublisher.DeriveAppName(csproj));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void DeriveAppName_Falls_Back_To_File_Stem()
    {
        var tempDir = Directory.CreateTempSubdirectory("proj-test").FullName;
        try
        {
            var csproj = WriteCsproj(tempDir, "MyApp.csproj");
            Assert.Equal("MyApp", ProjectPublisher.DeriveAppName(csproj));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void DeriveAppName_Ignores_MSBuild_Expressions()
    {
        var tempDir = Directory.CreateTempSubdirectory("proj-test").FullName;
        try
        {
            var csproj = WriteCsproj(tempDir, "MyApp.csproj",
                "    <AssemblyName>$(MSBuildProjectName)</AssemblyName>");
            Assert.Equal("MyApp", ProjectPublisher.DeriveAppName(csproj));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void DeriveVersion_Reads_Version_Property()
    {
        var tempDir = Directory.CreateTempSubdirectory("proj-test").FullName;
        try
        {
            var csproj = WriteCsproj(tempDir, "MyApp.csproj", "    <Version>3.1.4</Version>");
            Assert.Equal("3.1.4", ProjectPublisher.DeriveVersion(csproj));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void DeriveVersion_Falls_Back_To_ApplicationDisplayVersion()
    {
        var tempDir = Directory.CreateTempSubdirectory("proj-test").FullName;
        try
        {
            var csproj = WriteCsproj(tempDir, "MyApp.csproj",
                "    <ApplicationDisplayVersion>2.5</ApplicationDisplayVersion>");
            Assert.Equal("2.5", ProjectPublisher.DeriveVersion(csproj));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void DeriveVersion_Returns_Null_When_Absent()
    {
        var tempDir = Directory.CreateTempSubdirectory("proj-test").FullName;
        try
        {
            var csproj = WriteCsproj(tempDir, "MyApp.csproj");
            Assert.Null(ProjectPublisher.DeriveVersion(csproj));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void LocatePublishOutput_Finds_Standard_Layout()
    {
        var tempDir = Directory.CreateTempSubdirectory("proj-test").FullName;
        try
        {
            var publish = Path.Combine(tempDir, "bin", "Release", "net10.0", "linux-x64", "publish");
            Directory.CreateDirectory(publish);
            File.WriteAllText(Path.Combine(publish, "MyApp.dll"), "fake");

            Assert.Equal(publish, ProjectPublisher.LocatePublishOutput(tempDir, "linux-x64"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void LocatePublishOutput_Returns_Null_For_Missing_Rid()
    {
        var tempDir = Directory.CreateTempSubdirectory("proj-test").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(tempDir, "bin", "Release", "net10.0", "linux-x64", "publish"));

            Assert.Null(ProjectPublisher.LocatePublishOutput(tempDir, "linux-arm64"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void LocatePublishOutput_Returns_Null_Without_Bin_Release()
    {
        var tempDir = Directory.CreateTempSubdirectory("proj-test").FullName;
        try
        {
            Assert.Null(ProjectPublisher.LocatePublishOutput(tempDir, "linux-x64"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
