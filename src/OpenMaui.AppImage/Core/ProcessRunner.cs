using System.Diagnostics;

namespace OpenMaui.AppImage.Core;

public static class ProcessRunner
{
    public static async Task<int> RunCommandAsync(string command, string arguments,
        Dictionary<string, string>? envVars = null, bool captureOutput = false)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = captureOutput,
                RedirectStandardError = captureOutput,
                CreateNoWindow = true
            };

            if (envVars != null)
            {
                foreach (var (key, value) in envVars)
                    psi.EnvironmentVariables[key] = value;
            }

            using var process = Process.Start(psi);
            if (process == null) return -1;

            await process.WaitForExitAsync();
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  Error running {command}: {ex.Message}");
            return -1;
        }
    }
}
