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

    /// <summary>
    /// Runs a command with an explicit argument vector (no shell-style quoting
    /// needed, so paths with spaces are safe).
    /// </summary>
    public static async Task<int> RunCommandAsync(string command, IEnumerable<string> arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var arg in arguments)
                psi.ArgumentList.Add(arg);

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

    /// <summary>
    /// Finds <paramref name="command"/> on PATH without spawning a process.
    /// Returns the full path, or null when it is not found / not executable.
    /// </summary>
    public static string? FindOnPath(string command)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
            return null;

        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, command);
            try
            {
                if (File.Exists(candidate) &&
                    (File.GetUnixFileMode(candidate) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0)
                    return candidate;
            }
            catch
            {
                // unreadable PATH entry: skip
            }
        }
        return null;
    }
}
