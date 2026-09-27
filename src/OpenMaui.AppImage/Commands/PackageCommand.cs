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
        Flatpak,
        Deb,
        Rpm
    }

    /// <summary>
    /// Parses the --format value: one of appimage, flatpak, deb, rpm, or all
    /// (appimage + deb + rpm), or a comma-separated combination such as
    /// "deb,rpm". Case-insensitive. Returns null when any token is unknown.
    /// </summary>
    public static List<PackageFormat>? ParseFormats(string? format)
    {
        var result = new List<PackageFormat>();
        if (string.IsNullOrWhiteSpace(format))
            return new List<PackageFormat> { PackageFormat.AppImage };

        foreach (var token in format.Split(new[] { ',', ' ', '+' }, StringSplitOptions.RemoveEmptyEntries))
        {
            IEnumerable<PackageFormat> add = token.ToLowerInvariant() switch
            {
                "appimage" => new[] { PackageFormat.AppImage },
                "flatpak" => new[] { PackageFormat.Flatpak },
                "deb" => new[] { PackageFormat.Deb },
                "rpm" => new[] { PackageFormat.Rpm },
                "all" => new[] { PackageFormat.AppImage, PackageFormat.Deb, PackageFormat.Rpm },
                _ => Array.Empty<PackageFormat>()
            };
            if (!add.Any())
                return null;
            foreach (var f in add)
            {
                if (!result.Contains(f))
                    result.Add(f);
            }
        }
        return result.Count == 0 ? null : result;
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
        var rootCommand = new RootCommand("Package .NET MAUI Linux apps as AppImages, Flatpaks, .deb or .rpm packages");

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
            description: "Output file path (.AppImage, .flatpak, .deb or .rpm). Defaults to <Name>.AppImage (or <pkg>_<ver>-<rel>_<arch>.deb / <pkg>-<ver>-<rel>.<arch>.rpm) in the current directory. When several formats are selected, this is an output directory.");

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
            description: "Output format: appimage, flatpak, deb, rpm, or all (appimage + deb + rpm). Comma-separated combinations such as deb,rpm are accepted.");

        var appIdOption = new Option<string>(
            aliases: new[] { "--app-id" },
            description: "Application ID in reverse-DNS form (e.g. com.example.MyApp). Used for Flatpak, --metainfo, and the .deb/.rpm layout (/opt/<app-id>, desktop file and icon names; default there: the csproj ApplicationId).");

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
            description: "Developer name for the AppStream metainfo (used with --metainfo); also the default maintainer name for .deb/.rpm.");

        var packageNameOption = new Option<string?>(
            aliases: new[] { "--package-name" },
            description: "Package name for .deb/.rpm and the /usr/bin launcher (default: lowercased app name, e.g. shelldemo).");

        var maintainerOption = new Option<string?>(
            aliases: new[] { "--maintainer" },
            description: "Package maintainer as \"Name <email>\" for .deb (Maintainer) and .rpm (Packager). Default: --developer, else the csproj Authors, else the app name.");

        var licenseOption = new Option<string?>(
            aliases: new[] { "--license" },
            description: "License (SPDX expression) for the .rpm License tag. Default: csproj PackageLicenseExpression, else 'LicenseRef-Proprietary'.");

        var homepageOption = new Option<string?>(
            aliases: new[] { "--homepage" },
            description: "Project homepage for .deb (Homepage) and .rpm (URL). Default: csproj PackageProjectUrl.");

        var releaseOption = new Option<string>(
            aliases: new[] { "--release" },
            () => "1",
            description: "Package release number (.rpm Release, .deb revision).");

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
        rootCommand.AddOption(packageNameOption);
        rootCommand.AddOption(maintainerOption);
        rootCommand.AddOption(licenseOption);
        rootCommand.AddOption(homepageOption);
        rootCommand.AddOption(releaseOption);

        rootCommand.AddValidator(result =>
        {
            var hasInput = result.GetValueForOption(inputOption) != null;
            var hasProject = result.GetValueForOption(projectOption) != null;
            var formats = ParseFormats(result.GetValueForOption(formatOption));
            // A single AppImage/Flatpak target keeps the historical --output requirement
            // with --input; .deb/.rpm and multi-format runs name their files themselves.
            var needsExplicitOutput = formats is { Count: 1 } &&
                formats[0] is PackageFormat.AppImage or PackageFormat.Flatpak;

            if (formats == null)
            {
                result.ErrorMessage = $"Unknown --format '{result.GetValueForOption(formatOption)}'. Use appimage, flatpak, deb, rpm, all, or a comma-separated combination (e.g. deb,rpm).";
            }
            else if (hasInput && hasProject)
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
            else if (hasInput && needsExplicitOutput && result.GetValueForOption(outputOption) == null)
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
            var packageName = context.ParseResult.GetValueForOption(packageNameOption);
            var maintainer = context.ParseResult.GetValueForOption(maintainerOption);
            var license = context.ParseResult.GetValueForOption(licenseOption);
            var homepage = context.ParseResult.GetValueForOption(homepageOption);
            var release = context.ParseResult.GetValueForOption(releaseOption)!;
            var formats = ParseFormats(format)!;
            var wantsNative = formats.Contains(PackageFormat.Deb) || formats.Contains(PackageFormat.Rpm);
            ProjectMetadata? projectMeta = null;

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
                projectMeta = ProjectPublisher.DeriveMetadata(csproj);
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

            // --output: a file for a single format, a directory for several
            var multiFormat = formats.Count > 1;
            var outputDir = multiFormat && output != null ? output.FullName : null;
            var explicitOutput = multiFormat ? null : output;

            // System packages default their app id to the csproj ApplicationId
            if (wantsNative && string.IsNullOrEmpty(appId) && !string.IsNullOrEmpty(projectMeta?.ApplicationId))
                appId = projectMeta.ApplicationId;

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
                OutputFile = explicitOutput ?? new FileInfo(Path.Combine(outputDir ?? ".", $"{AppDirBuilder.SanitizeFileName(name!)}.AppImage")),
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
                Developer = developer,
                PackageName = packageName,
                Maintainer = maintainer ?? developer ?? projectMeta?.Authors,
                License = license ?? projectMeta?.License,
                Homepage = homepage ?? projectMeta?.ProjectUrl,
                PackageRelease = release,
                Description = projectMeta?.Description
            };

            var allOk = true;
            var failed = new List<string>();

            if (formats.Contains(PackageFormat.AppImage))
            {
                if (!await RunAppImageAsync(options))
                {
                    allOk = false;
                    failed.Add("appimage");
                }
            }

            if (formats.Contains(PackageFormat.Flatpak))
            {
                var flatpakOutput = explicitOutput ?? new FileInfo(Path.Combine(outputDir ?? ".", $"{AppDirBuilder.SanitizeFileName(name!)}.flatpak"));
                if (!await new FlatpakPacker().BuildAsync(options with { OutputFile = flatpakOutput }))
                {
                    allOk = false;
                    failed.Add("flatpak");
                }
            }

            if (wantsNative)
            {
                var nativeFormats = formats.Where(f => f is PackageFormat.Deb or PackageFormat.Rpm).ToList();
                if (!await RunNativeAsync(options, nativeFormats, explicitOutput, outputDir))
                {
                    allOk = false;
                    failed.Add(string.Join("/", nativeFormats.Select(f => f.ToString().ToLowerInvariant())));
                }
            }

            if (multiFormat && !allOk)
                Console.Error.WriteLine($"Error: packaging failed for: {string.Join(", ", failed)}");

            context.ExitCode = allOk ? 0 : 1;
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

        var execName = ResolveExecutable(appFilesDir, options.AppName, options.ExecutableName);
        if (execName == null)
            return false;

        options = ResolveIcon(options, execName);

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

    /// <summary>
    /// Resolves the main executable name in <paramref name="appFilesDir"/>: the
    /// explicit --executable, else auto-detection (ELF, name-matching dll,
    /// runtimeconfig), else the app name; tolerates a ".dll" suffix and spaces.
    /// Prints an error listing candidate DLLs and returns null when not found.
    /// </summary>
    public static string? ResolveExecutable(string appFilesDir, string appName, string? explicitName)
    {
        var appDirBuilder = new AppDirBuilder();
        var execName = explicitName;

        // Auto-detect executable if not specified
        if (string.IsNullOrEmpty(execName))
        {
            execName = appDirBuilder.AutoDetectExecutable(appFilesDir, appName);
            if (execName != null)
            {
                Console.WriteLine($"  Auto-detected executable: {execName}");
            }
        }

        // Fallback to app name variations
        if (string.IsNullOrEmpty(execName))
        {
            execName = appName;
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
                return null;
            }
        }

        return execName;
    }

    /// <summary>Auto-detects the icon (csproj MauiIcon, then well-known file names) when --icon is absent.</summary>
    public static PackageOptions ResolveIcon(PackageOptions options, string execName)
    {
        if (options.IconPath == null || !options.IconPath.Exists)
        {
            var detectedIcon = new AppDirBuilder().FindIcon(options.InputDirectory.FullName, execName);
            if (detectedIcon != null)
            {
                options = options with { IconPath = new FileInfo(detectedIcon) };
                Console.WriteLine($"  Auto-detected icon: {Path.GetFileName(detectedIcon)}");
            }
        }
        return options;
    }

    /// <summary>
    /// Builds the resolved metadata for .deb/.rpm output from the options and
    /// the publish directory (architecture and runtime flavor are detected
    /// from the files).
    /// </summary>
    public static NativePackageMetadata BuildNativeMetadata(PackageOptions options, string execName)
    {
        var publishDir = options.InputDirectory.FullName;
        var appId = NativePackageMetadata.SanitizeAppId(
            options.AppId ?? $"com.openmaui.{AppStreamGenerator.SanitizeIdSegment(options.AppName)}");
        var runtime = NativePackageMetadata.DetectRuntime(publishDir, execName);

        return new NativePackageMetadata
        {
            PackageName = NativePackageMetadata.SanitizePackageName(options.PackageName ?? options.AppName),
            AppName = options.AppName,
            AppId = appId,
            ExecutableName = execName,
            Version = options.Version,
            Release = string.IsNullOrWhiteSpace(options.PackageRelease) ? "1" : options.PackageRelease.Trim(),
            Architecture = NativePackageMetadata.DetectArchitecture(publishDir, execName),
            Runtime = runtime,
            FrameworkVersion = runtime == DotNetRuntimeKind.FrameworkDependent
                ? NativePackageMetadata.ReadFrameworkVersion(publishDir, execName)
                : null,
            Maintainer = NativePackageMetadata.NormalizeMaintainer(
                string.IsNullOrWhiteSpace(options.Maintainer) ? options.AppName : options.Maintainer),
            License = string.IsNullOrWhiteSpace(options.License) ? "LicenseRef-Proprietary" : options.License,
            Homepage = options.Homepage,
            Summary = options.Comment,
            Description = options.Description,
            Category = options.Category
        };
    }

    /// <summary>
    /// The .deb/.rpm flow: resolves executable, icon and metadata, maps the
    /// scanned host dependencies to package relationships, stages the FHS tree
    /// once, and writes each requested package (.deb in managed code, .rpm via
    /// rpmbuild).
    /// </summary>
    public static async Task<bool> RunNativeAsync(PackageOptions options, IReadOnlyList<PackageFormat> formats,
        FileInfo? explicitOutput, string? outputDir)
    {
        var label = string.Join(" and ", formats.Select(f => "." + f.ToString().ToLowerInvariant()));
        Console.WriteLine($"Building {label} for {options.AppName}...");
        Console.WriteLine($"  Input: {options.InputDirectory.FullName}");

        if (!options.InputDirectory.Exists)
        {
            Console.Error.WriteLine($"Error: Input directory does not exist: {options.InputDirectory.FullName}");
            return false;
        }

        if (options.PreBuiltAppDir)
        {
            Console.Error.WriteLine("Error: --appdir is only supported for AppImage output; .deb/.rpm take a publish directory (--input) or --project.");
            return false;
        }

        // Fail fast before staging when rpmbuild is missing
        if (formats.Contains(PackageFormat.Rpm) && ProcessRunner.FindOnPath("rpmbuild") == null)
        {
            Console.Error.WriteLine("Error: " + RpmPackageBuilder.MissingRpmbuildMessage);
            return false;
        }

        var execName = ResolveExecutable(options.InputDirectory.FullName, options.AppName, options.ExecutableName);
        if (execName == null)
            return false;

        options = ResolveIcon(options, execName);
        var meta = BuildNativeMetadata(options, execName);

        if (!meta.Maintainer.Contains('<') || meta.Maintainer.Contains($"<{NativePackageMetadata.DefaultMaintainerEmail}>"))
            Console.WriteLine($"  Note: maintainer defaults to '{meta.Maintainer}'; pass --maintainer \"Name <email>\" for published packages.");

        var scan = DependencyScanner.Scan(options.InputDirectory.FullName);
        var deps = NativeDependencyMapper.Map(scan, meta.Runtime, meta.FrameworkVersion);

        Console.WriteLine($"  Package: {meta.PackageName} {meta.Version} ({meta.Architecture.Debian}/{meta.Architecture.Rpm})");
        Console.WriteLine($"  App id:  {meta.AppId} -> {meta.InstallDir}, launcher {meta.LauncherPath}");

        var workDir = Path.Combine(Path.GetTempPath(), $"openmaui-pkg-{Guid.NewGuid():N}");
        try
        {
            Console.WriteLine("  Staging install tree...");
            var staged = NativeLayoutBuilder.Stage(Path.Combine(workDir, "root"), options.InputDirectory.FullName,
                meta, options.IconPath, options.GenerateMetainfo, options.Developer);

            var ok = true;
            var produced = new List<string>();

            if (formats.Contains(PackageFormat.Deb))
            {
                var debPath = explicitOutput?.FullName ?? Path.GetFullPath(Path.Combine(outputDir ?? ".", meta.DebFileName));
                Console.WriteLine("  Writing .deb...");
                try
                {
                    DebPackageWriter.Write(staged, meta, deps, debPath);
                    produced.Add(debPath);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error: writing the .deb failed: {ex.Message}");
                    ok = false;
                }
            }

            if (formats.Contains(PackageFormat.Rpm))
            {
                var rpmPath = explicitOutput?.FullName ?? Path.GetFullPath(Path.Combine(outputDir ?? ".", meta.RpmFileName));
                Console.WriteLine("  Running rpmbuild...");
                if (await RpmPackageBuilder.BuildAsync(staged, meta, deps, rpmPath, workDir, options.Developer))
                    produced.Add(rpmPath);
                else
                    ok = false;
            }

            if (produced.Count > 0)
            {
                Console.WriteLine();
                foreach (var path in produced)
                    Console.WriteLine($"Package created successfully: {path}");
                Console.WriteLine();

                if (deps.DebDepends.Count + deps.DebRecommends.Count > 0)
                {
                    Console.WriteLine("Package relationships:");
                    if (formats.Contains(PackageFormat.Deb))
                    {
                        Console.WriteLine($"  deb Depends:    {string.Join(", ", deps.DebDepends)}");
                        if (deps.DebRecommends.Count > 0)
                            Console.WriteLine($"  deb Recommends: {string.Join(", ", deps.DebRecommends)}");
                    }
                    if (formats.Contains(PackageFormat.Rpm))
                    {
                        Console.WriteLine($"  rpm Requires:   {string.Join(", ", deps.RpmRequires)}");
                        if (deps.RpmRecommends.Count > 0)
                            Console.WriteLine($"  rpm Recommends: {string.Join(", ", deps.RpmRecommends)}");
                    }
                    foreach (var note in deps.Notes)
                        Console.WriteLine($"  Note: {note}");
                    Console.WriteLine();
                }

                Console.WriteLine("To install:");
                foreach (var path in produced)
                {
                    var file = Path.GetFileName(path);
                    Console.WriteLine(path.EndsWith(".deb", StringComparison.Ordinal)
                        ? $"  sudo apt install ./{file}"
                        : $"  sudo dnf install ./{file}");
                }
                Console.WriteLine($"Then run '{meta.PackageName}' or launch {meta.AppName} from the application menu.");
            }

            return ok;
        }
        finally
        {
            try
            {
                if (Directory.Exists(workDir))
                    Directory.Delete(workDir, recursive: true);
            }
            catch { }
        }
    }
}
