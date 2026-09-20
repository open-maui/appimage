using System.CommandLine;
using OpenMaui.AppImage.Core;

namespace OpenMaui.AppImage.Commands;

/// <summary>
/// The packaging command: defines the CLI options and orchestrates the
/// AppImage / Flatpak packaging flow.
/// </summary>
public static class PackageCommand
{
    public enum PackageFormat
    {
        AppImage,
        Flatpak
    }

    /// <summary>
    /// Resolves the --format value to a packaging format. Matches the historical
    /// behavior: "flatpak" (case-insensitive) selects Flatpak, anything else
    /// falls through to AppImage.
    /// </summary>
    public static PackageFormat ResolveFormat(string format)
    {
        return format.ToLowerInvariant() == "flatpak"
            ? PackageFormat.Flatpak
            : PackageFormat.AppImage;
    }

    public static RootCommand Create()
    {
        var rootCommand = new RootCommand("Package .NET MAUI Linux apps as AppImages or Flatpaks");

        var inputOption = new Option<DirectoryInfo>(
            aliases: new[] { "--input", "-i" },
            description: "Path to the published .NET app directory");

        var projectOption = new Option<string?>(
            aliases: new[] { "--project", "-p" },
            description: "Path to a .csproj (or a directory containing one). The tool runs 'dotnet publish -c Release -r <rid> --self-contained true' itself and packages the result. Mutually exclusive with --input; --name and --app-version are derived from the csproj when not given.");

        var ridOption = new Option<string?>(
            aliases: new[] { "--rid" },
            description: "Runtime identifier for 'dotnet publish' when using --project (default: host architecture, e.g. linux-x64 or linux-arm64).");

        var outputOption = new Option<FileInfo>(
            aliases: new[] { "--output", "-o" },
            description: "Output file path (.AppImage or .flatpak). Defaults to <Name>.AppImage in the current directory when using --project.");

        var nameOption = new Option<string>(
            aliases: new[] { "--name", "-n" },
            description: "Application name (used in .desktop file). Required with --input; derived from the csproj (AssemblyName or file name) with --project.");

        var execOption = new Option<string>(
            aliases: new[] { "--executable", "-e" },
            description: "Name of the main executable (without extension)");

        var iconOption = new Option<FileInfo?>(
            aliases: new[] { "--icon" },
            description: "Path to application icon (PNG or SVG)");

        var categoryOption = new Option<string>(
            aliases: new[] { "--category", "-c" },
            () => "Utility",
            description: "Desktop category (e.g., Utility, Development, Game)");

        var versionOption = new Option<string>(
            aliases: new[] { "--app-version" },
            () => "1.0.0",
            description: "Application version (derived from the csproj Version property with --project when not given)");

        var commentOption = new Option<string>(
            aliases: new[] { "--comment" },
            description: "Application description/comment");

        var formatOption = new Option<string>(
            aliases: new[] { "--format", "-f" },
            () => "appimage",
            description: "Output format: appimage or flatpak");

        var appIdOption = new Option<string>(
            aliases: new[] { "--app-id" },
            description: "Application ID in reverse-DNS form (e.g. com.example.MyApp). Used for Flatpak and for --metainfo.");

        var appDirOption = new Option<bool>(
            aliases: new[] { "--appdir" },
            description: "Treat --input as a pre-structured AppDir / FHS tree (e.g. a directory extracted from a .deb: usr/bin, usr/lib, usr/share) rather than a flat publish dir. The tree becomes the AppDir root and the executable is resolved under usr/bin. Ideal for wrapping apps whose bundler already lays out an FHS tree (Tauri, Electron, deb packages).");

        var noFuseOption = new Option<bool>(
            aliases: new[] { "--no-fuse" },
            description: "Never use FUSE for appimagetool — always run it in extract-and-run mode. By default FUSE is auto-detected and only bypassed when /dev/fuse is missing; use this when FUSE is present but broken (misconfigured containers, missing libfuse2) so detection would wrongly try to mount.");

        var noFetchOption = new Option<bool>(
            aliases: new[] { "--no-fetch" },
            description: "Forbid network access: never auto-download appimagetool. By default, when appimagetool is not found the official continuous release is downloaded into ~/.cache/openmaui-appimage/ and reused on later runs.");

        var hostDepsCheckOption = new Option<bool>(
            aliases: new[] { "--host-deps-check" },
            description: "Inject a small launch-time check into AppRun that probes required host libraries via 'ldconfig -p' and, when the required base set is missing, shows a friendly message with distro install commands (zenity/kdialog when available) and aborts; missing optional feature libraries only print a note.");

        var updateInfoOption = new Option<string?>(
            aliases: new[] { "--update-info" },
            description: "Update information embedded by appimagetool (-u), enabling zsync self-updating AppImages. Common shape: \"gh-releases-zsync|user|repo|latest|*.AppImage.zsync\".");

        var signOption = new Option<bool>(
            aliases: new[] { "--sign" },
            description: "Sign the AppImage with GPG (appimagetool --sign).");

        var signKeyOption = new Option<string?>(
            aliases: new[] { "--sign-key" },
            description: "GPG key id to sign with (appimagetool --sign-key). Implies --sign.");

        var metainfoOption = new Option<bool>(
            aliases: new[] { "--metainfo" },
            description: "Generate a minimal AppStream metainfo.xml into AppDir/usr/share/metainfo/<app-id>.metainfo.xml (component type=desktop-application). Uses --app-id (should be reverse-DNS), --comment as the summary, and --developer.");

        var developerOption = new Option<string?>(
            aliases: new[] { "--developer" },
            description: "Developer name for the AppStream metainfo (used with --metainfo).");

        rootCommand.AddOption(inputOption);
        rootCommand.AddOption(projectOption);
        rootCommand.AddOption(ridOption);
        rootCommand.AddOption(outputOption);
        rootCommand.AddOption(nameOption);
        rootCommand.AddOption(execOption);
        rootCommand.AddOption(iconOption);
        rootCommand.AddOption(categoryOption);
        rootCommand.AddOption(versionOption);
        rootCommand.AddOption(commentOption);
        rootCommand.AddOption(formatOption);
        rootCommand.AddOption(appIdOption);
        rootCommand.AddOption(appDirOption);
        rootCommand.AddOption(noFuseOption);
        rootCommand.AddOption(noFetchOption);
        rootCommand.AddOption(hostDepsCheckOption);
        rootCommand.AddOption(updateInfoOption);
        rootCommand.AddOption(signOption);
        rootCommand.AddOption(signKeyOption);
        rootCommand.AddOption(metainfoOption);
        rootCommand.AddOption(developerOption);

        rootCommand.AddValidator(result =>
        {
            var hasInput = result.GetValueForOption(inputOption) != null;
            var hasProject = result.GetValueForOption(projectOption) != null;

            if (hasInput && hasProject)
            {
                result.ErrorMessage = "--input and --project are mutually exclusive; pass one or the other.";
            }
            else if (!hasInput && !hasProject)
            {
                result.ErrorMessage = "Either --input (a published app directory) or --project (a .csproj to publish) is required.";
            }
            else if (hasInput && result.GetValueForOption(nameOption) == null)
            {
                result.ErrorMessage = "--name is required with --input (with --project it is derived from the .csproj).";
            }
            else if (hasInput && result.GetValueForOption(outputOption) == null)
            {
                result.ErrorMessage = "--output is required with --input (with --project it defaults to <Name>.AppImage).";
            }
        });

        rootCommand.SetHandler(async (context) =>
        {
            var input = context.ParseResult.GetValueForOption(inputOption);
            var project = context.ParseResult.GetValueForOption(projectOption);
            var rid = context.ParseResult.GetValueForOption(ridOption);
            var output = context.ParseResult.GetValueForOption(outputOption);
            var name = context.ParseResult.GetValueForOption(nameOption);
            var exec = context.ParseResult.GetValueForOption(execOption);
            var icon = context.ParseResult.GetValueForOption(iconOption);
            var category = context.ParseResult.GetValueForOption(categoryOption)!;
            var version = context.ParseResult.GetValueForOption(versionOption)!;
            var versionExplicit = context.ParseResult.FindResultFor(versionOption)?.IsImplicit == false;
            var comment = context.ParseResult.GetValueForOption(commentOption);
            var format = context.ParseResult.GetValueForOption(formatOption)!;
            var appId = context.ParseResult.GetValueForOption(appIdOption);
            var preBuiltAppDir = context.ParseResult.GetValueForOption(appDirOption);
            var noFuse = context.ParseResult.GetValueForOption(noFuseOption);
            var noFetch = context.ParseResult.GetValueForOption(noFetchOption);
            var hostDepsCheck = context.ParseResult.GetValueForOption(hostDepsCheckOption);
            var updateInfo = context.ParseResult.GetValueForOption(updateInfoOption);
            var sign = context.ParseResult.GetValueForOption(signOption);
            var signKey = context.ParseResult.GetValueForOption(signKeyOption);
            var metainfo = context.ParseResult.GetValueForOption(metainfoOption);
            var developer = context.ParseResult.GetValueForOption(developerOption);

            // --project: publish the project ourselves and package the output
            if (project != null)
            {
                var csproj = ProjectPublisher.ResolveProjectFile(project);
                if (csproj == null)
                {
                    Console.Error.WriteLine($"Error: --project '{project}' does not resolve to a single .csproj. Pass the .csproj file, or a directory containing exactly one.");
                    context.ExitCode = 1;
                    return;
                }

                var publishRid = rid ?? ProjectPublisher.GetHostRid();
                name ??= ProjectPublisher.DeriveAppName(csproj);
                if (!versionExplicit)
                    version = ProjectPublisher.DeriveVersion(csproj) ?? version;

                Console.WriteLine($"Publishing {Path.GetFileName(csproj)} ({publishRid}, Release, self-contained)...");
                var publishExit = await ProcessRunner.RunCommandAsync(
                    "dotnet", ProjectPublisher.BuildPublishArguments(csproj, publishRid));
                if (publishExit != 0)
                {
                    Console.Error.WriteLine("Error: dotnet publish failed.");
                    context.ExitCode = 1;
                    return;
                }

                var publishDir = ProjectPublisher.LocatePublishOutput(Path.GetDirectoryName(csproj)!, publishRid);
                if (publishDir == null)
                {
                    Console.Error.WriteLine($"Error: could not locate the publish output under {Path.GetDirectoryName(csproj)}/bin/Release/*/{publishRid}/publish.");
                    context.ExitCode = 1;
                    return;
                }

                Console.WriteLine($"  Publish output: {publishDir}");
                input = new DirectoryInfo(publishDir);
            }

            var isFlatpak = ResolveFormat(format) == PackageFormat.Flatpak;
            output ??= new FileInfo($"{AppDirBuilder.SanitizeFileName(name!)}{(isFlatpak ? ".flatpak" : ".AppImage")}");

            // --metainfo wants a stable reverse-DNS app id
            if (metainfo)
            {
                if (string.IsNullOrEmpty(appId))
                {
                    appId = $"com.openmaui.{AppStreamGenerator.SanitizeIdSegment(name!)}";
                    Console.WriteLine($"Warning: --metainfo used without --app-id; using derived id '{appId}'. Pass --app-id for a stable reverse-DNS id.");
                }
                else if (!AppStreamGenerator.LooksReverseDns(appId))
                {
                    Console.WriteLine($"Warning: --app-id '{appId}' does not look like a reverse-DNS id (e.g. com.example.MyApp); AppStream consumers may reject it.");
                }
            }

            var options = new PackageOptions
            {
                InputDirectory = input!,
                OutputFile = output,
                AppName = name!,
                ExecutableName = exec,
                IconPath = icon,
                Category = category,
                Version = version,
                Comment = comment ?? $"{name} - Built with OpenMaui",
                AppId = appId,
                PreBuiltAppDir = preBuiltAppDir,
                NoFuse = noFuse,
                NoFetch = noFetch,
                HostDepsCheck = hostDepsCheck,
                UpdateInfo = updateInfo,
                Sign = sign,
                SignKey = signKey,
                GenerateMetainfo = metainfo,
                Developer = developer
            };

            bool result;
            if (isFlatpak)
            {
                var packer = new FlatpakPacker();
                result = await packer.BuildAsync(options);
            }
            else
            {
                result = await RunAppImageAsync(options);
            }

            context.ExitCode = result ? 0 : 1;
        });

        return rootCommand;
    }

    /// <summary>
    /// The AppImage packaging flow: validates the input, resolves the executable
    /// and icon, builds the AppDir (via <see cref="AppDirBuilder"/>), and packs it
    /// with appimagetool (via <see cref="AppImagePacker"/>).
    /// </summary>
    public static async Task<bool> RunAppImageAsync(PackageOptions options)
    {
        Console.WriteLine($"Building AppImage for {options.AppName}...");
        Console.WriteLine($"  Input: {options.InputDirectory.FullName}");
        Console.WriteLine($"  Output: {options.OutputFile.FullName}");

        // Validate input directory
        if (!options.InputDirectory.Exists)
        {
            Console.Error.WriteLine($"Error: Input directory does not exist: {options.InputDirectory.FullName}");
            return false;
        }

        var appDirBuilder = new AppDirBuilder();

        // Directory that actually holds the executable: for a pre-built AppDir
        // (e.g. an extracted .deb) that's <input>/usr/bin; for a flat publish dir
        // it's the input directory itself.
        var appFilesDir = options.PreBuiltAppDir
            ? Path.Combine(options.InputDirectory.FullName, "usr", "bin")
            : options.InputDirectory.FullName;

        if (options.PreBuiltAppDir && !Directory.Exists(appFilesDir))
        {
            Console.Error.WriteLine($"Error: --appdir was given but {appFilesDir} does not exist. Expected an FHS tree (usr/bin/...).");
            return false;
        }

        // Find the main executable
        var execName = options.ExecutableName;

        // Auto-detect executable if not specified
        if (string.IsNullOrEmpty(execName))
        {
            execName = appDirBuilder.AutoDetectExecutable(appFilesDir, options.AppName);
            if (execName != null)
            {
                Console.WriteLine($"  Auto-detected executable: {execName}");
            }
        }

        // Fallback to app name variations
        if (string.IsNullOrEmpty(execName))
        {
            execName = options.AppName;
        }

        var mainExec = Path.Combine(appFilesDir, execName);
        if (!File.Exists(mainExec))
        {
            // Try with common variations
            var candidates = new[]
            {
                mainExec,
                mainExec + ".dll",
                Path.Combine(appFilesDir, execName.Replace(" ", "")),
                Path.Combine(appFilesDir, execName.Replace(" ", "") + ".dll")
            };

            var found = candidates.FirstOrDefault(File.Exists);
            if (found != null)
            {
                // Update execName to the actual name (without spaces)
                execName = Path.GetFileNameWithoutExtension(found);
                mainExec = found;
            }
            else
            {
                // List available executables
                var dlls = Directory.GetFiles(appFilesDir, "*.dll")
                    .Select(Path.GetFileNameWithoutExtension)
                    .Take(10)
                    .ToList();

                Console.Error.WriteLine($"Error: Could not find executable '{execName}' in {appFilesDir}");
                Console.Error.WriteLine($"Available DLLs: {string.Join(", ", dlls)}");
                Console.Error.WriteLine("Use --executable to specify the correct name.");
                return false;
            }
        }

        // Auto-detect icon if not specified
        if (options.IconPath == null || !options.IconPath.Exists)
        {
            var detectedIcon = appDirBuilder.FindIcon(options.InputDirectory.FullName, execName);
            if (detectedIcon != null)
            {
                options = options with { IconPath = new FileInfo(detectedIcon) };
                Console.WriteLine($"  Auto-detected icon: {Path.GetFileName(detectedIcon)}");
            }
        }

        // Create temporary AppDir structure
        var tempDir = Path.Combine(Path.GetTempPath(), $"appimage-{Guid.NewGuid():N}");
        var appDir = Path.Combine(tempDir, $"{options.AppName}.AppDir");

        try
        {
            await appDirBuilder.PopulateAsync(appDir, options, execName);

            // Create the AppImage using appimagetool
            Console.WriteLine("  Creating AppImage...");
            var packer = new AppImagePacker();
            var success = await packer.PackAsync(appDir, options.OutputFile.FullName,
                options.NoFuse, options.NoFetch, options.UpdateInfo, options.Sign, options.SignKey);

            if (success)
            {
                Console.WriteLine();
                Console.WriteLine($"AppImage created successfully: {options.OutputFile.FullName}");
                Console.WriteLine();

                // Host runtime dependency report (always; concise)
                var scan = DependencyScanner.Scan(options.InputDirectory.FullName);
                Console.Write(DependencyScanner.FormatReport(scan));
                Console.WriteLine();

                Console.WriteLine("To run:");
                Console.WriteLine($"  chmod +x {options.OutputFile.Name}");
                Console.WriteLine($"  ./{options.OutputFile.Name}");
            }

            return success;
        }
        finally
        {
            // Cleanup temp directory
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
            catch { }
        }
    }
}
