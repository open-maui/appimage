using System.CommandLine;
using OpenMaui.AppImage.Commands;

namespace OpenMaui.AppImage;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = PackageCommand.Create();
        return await rootCommand.InvokeAsync(args);
    }
}
