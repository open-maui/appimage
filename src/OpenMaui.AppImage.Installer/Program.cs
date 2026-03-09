using Gtk;
using System.Diagnostics;
using IOPath = System.IO.Path;

namespace OpenMaui.AppImage.Installer;

class Program
{
    static int Main(string[] args)
    {
        // Parse arguments
        string? appName = null;
        string? appImage = null;
        string? iconPath = null;
        string? execPath = null;
        string? comment = null;
        string? category = null;
        string? version = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--name" when i + 1 < args.Length:
                    appName = args[++i];
                    break;
                case "--appimage" when i + 1 < args.Length:
                    appImage = args[++i];
                    break;
                case "--icon" when i + 1 < args.Length:
                    iconPath = args[++i];
                    break;
                case "--exec" when i + 1 < args.Length:
                    execPath = args[++i];
                    break;
                case "--comment" when i + 1 < args.Length:
                    comment = args[++i];
                    break;
                case "--category" when i + 1 < args.Length:
                    category = args[++i];
                    break;
                case "--version" when i + 1 < args.Length:
                    version = args[++i];
                    break;
            }
        }

        // Get from environment if not provided
        appImage ??= Environment.GetEnvironmentVariable("APPIMAGE");
        appName ??= Environment.GetEnvironmentVariable("APPIMAGE_NAME") ?? "Application";
        comment ??= Environment.GetEnvironmentVariable("APPIMAGE_COMMENT") ?? appName;
        category ??= Environment.GetEnvironmentVariable("APPIMAGE_CATEGORY") ?? "Utility";
        version ??= Environment.GetEnvironmentVariable("APPIMAGE_VERSION") ?? "1.0.0";

        if (string.IsNullOrEmpty(appImage))
        {
            Console.Error.WriteLine("Error: --appimage or APPIMAGE environment variable required");
            return 1;
        }

        Application.Init();

        var dialog = new InstallerDialog(appName, appImage, iconPath, execPath, comment, category, version);
        dialog.ShowAll();

        Application.Run();

        return dialog.ResultCode;
    }
}

class InstallerDialog : Window
{
    private readonly string _appName;
    private readonly string _appImagePath;
    private readonly string? _iconPath;
    private readonly string? _execPath;
    private readonly string _comment;
    private readonly string _category;
    private readonly string _version;

    public int ResultCode { get; private set; } = 1;

    public InstallerDialog(string appName, string appImagePath, string? iconPath, string? execPath,
        string comment, string category, string version)
        : base(WindowType.Toplevel)
    {
        _appName = appName;
        _appImagePath = appImagePath;
        _iconPath = iconPath;
        _execPath = execPath;
        _comment = comment;
        _category = category;
        _version = version;

        SetupWindow();
        BuildUI();
    }

    private void SetupWindow()
    {
        Title = $"Install {_appName}";
        SetDefaultSize(400, 300);
        SetPosition(WindowPosition.Center);
        BorderWidth = 20;
        Resizable = false;

        DeleteEvent += (o, e) =>
        {
            ResultCode = 1;
            Application.Quit();
        };
    }

    private void BuildUI()
    {
        var mainBox = new Box(Orientation.Vertical, 20);

        // Icon
        var iconBox = new Box(Orientation.Horizontal, 0);
        iconBox.Halign = Align.Center;

        Image appIcon;
        if (!string.IsNullOrEmpty(_iconPath) && File.Exists(_iconPath))
        {
            try
            {
                var pixbuf = new Gdk.Pixbuf(_iconPath, 96, 96);
                appIcon = new Image(pixbuf);
            }
            catch
            {
                appIcon = CreateDefaultIcon();
            }
        }
        else
        {
            appIcon = CreateDefaultIcon();
        }
        iconBox.PackStart(appIcon, false, false, 0);
        mainBox.PackStart(iconBox, false, false, 0);

        // App name
        var nameLabel = new Label();
        nameLabel.Markup = $"<span size='x-large' weight='bold'>{GLib.Markup.EscapeText(_appName)}</span>";
        nameLabel.Halign = Align.Center;
        mainBox.PackStart(nameLabel, false, false, 0);

        // Version
        var versionLabel = new Label();
        versionLabel.Markup = $"<span size='small' foreground='gray'>Version {GLib.Markup.EscapeText(_version)}</span>";
        versionLabel.Halign = Align.Center;
        mainBox.PackStart(versionLabel, false, false, 0);

        // Description
        var descLabel = new Label(_comment);
        descLabel.Halign = Align.Center;
        descLabel.LineWrap = true;
        descLabel.MaxWidthChars = 40;
        mainBox.PackStart(descLabel, false, false, 10);

        // Install location info
        var installInfo = new Label();
        var binDir = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin");
        installInfo.Markup = $"<span size='small' foreground='gray'>Will install to: {GLib.Markup.EscapeText(binDir)}</span>";
        installInfo.Halign = Align.Center;
        mainBox.PackStart(installInfo, false, false, 0);

        // Buttons
        var buttonBox = new Box(Orientation.Horizontal, 10);
        buttonBox.Halign = Align.Center;
        buttonBox.Valign = Align.End;
        buttonBox.MarginTop = 20;

        var runButton = new Button("Run Without Installing");
        runButton.SetSizeRequest(180, 40);
        runButton.Clicked += OnRunClicked;

        var installButton = new Button("Install");
        installButton.SetSizeRequest(120, 40);
        installButton.Clicked += OnInstallClicked;

        buttonBox.PackStart(runButton, false, false, 0);
        buttonBox.PackStart(installButton, false, false, 0);
        mainBox.PackStart(buttonBox, false, false, 0);

        Add(mainBox);
    }

    private Image CreateDefaultIcon()
    {
        // Create a simple colored square as default icon
        var pixbuf = new Gdk.Pixbuf(Gdk.Colorspace.Rgb, true, 8, 96, 96);
        pixbuf.Fill(0x2196F3FF); // Blue color
        return new Image(pixbuf);
    }

    private void OnRunClicked(object? sender, EventArgs e)
    {
        ResultCode = 0; // Success - run the app
        Hide();
        Application.Quit();
    }

    private void OnInstallClicked(object? sender, EventArgs e)
    {
        try
        {
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var binDir = IOPath.Combine(homeDir, ".local", "bin");
            var applicationsDir = IOPath.Combine(homeDir, ".local", "share", "applications");
            var iconsDir = IOPath.Combine(homeDir, ".local", "share", "icons", "hicolor", "256x256", "apps");
            var markerDir = IOPath.Combine(homeDir, ".local", "share", "openmaui-installed");

            // Create directories
            Directory.CreateDirectory(binDir);
            Directory.CreateDirectory(applicationsDir);
            Directory.CreateDirectory(iconsDir);
            Directory.CreateDirectory(markerDir);

            var appImageName = IOPath.GetFileName(_appImagePath);
            var sanitizedName = SanitizeName(_appName);
            var destPath = IOPath.Combine(binDir, appImageName);

            // Copy AppImage
            File.Copy(_appImagePath, destPath, overwrite: true);

            // Make executable
            var chmod = Process.Start(new ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"+x \"{destPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            chmod?.WaitForExit();

            // Copy icon if available
            string iconName = sanitizedName;
            if (!string.IsNullOrEmpty(_iconPath) && File.Exists(_iconPath))
            {
                var iconExt = IOPath.GetExtension(_iconPath);
                var destIcon = IOPath.Combine(iconsDir, $"{sanitizedName}{iconExt}");
                File.Copy(_iconPath, destIcon, overwrite: true);
            }

            // Create .desktop file
            var wmClass = _appName.Replace(" ", "").Replace("_", "");
            var desktopContent = $@"[Desktop Entry]
Type=Application
Name={_appName}
Comment={_comment}
Exec={destPath}
Icon={iconName}
Categories={_category};
Terminal=false
StartupWMClass={wmClass}
X-AppImage-Version={_version}
";
            var desktopPath = IOPath.Combine(applicationsDir, $"{sanitizedName}.desktop");
            File.WriteAllText(desktopPath, desktopContent);

            // Create marker file
            File.WriteAllText(IOPath.Combine(markerDir, appImageName), DateTime.Now.ToString("O"));

            // Update desktop database
            try
            {
                var updateDb = Process.Start(new ProcessStartInfo
                {
                    FileName = "update-desktop-database",
                    Arguments = applicationsDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                });
                updateDb?.WaitForExit();
            }
            catch { }

            // Show success message
            var successDialog = new MessageDialog(
                this,
                DialogFlags.Modal,
                MessageType.Info,
                ButtonsType.Ok,
                $"{_appName} has been installed successfully!\n\nYou can find it in your application menu.");
            successDialog.Title = "Installation Complete";
            successDialog.Run();
            successDialog.Destroy();

            ResultCode = 2; // Installed - run the app
            Hide();
            Application.Quit();
        }
        catch (Exception ex)
        {
            var errorDialog = new MessageDialog(
                this,
                DialogFlags.Modal,
                MessageType.Error,
                ButtonsType.Ok,
                $"Installation failed: {ex.Message}");
            errorDialog.Title = "Installation Error";
            errorDialog.Run();
            errorDialog.Destroy();
        }
    }

    private string SanitizeName(string name)
    {
        var invalid = IOPath.GetInvalidFileNameChars();
        var result = new System.Text.StringBuilder();
        foreach (var c in name)
        {
            if (invalid.Contains(c) || c == ' ')
                result.Append('_');
            else
                result.Append(c);
        }
        return result.ToString();
    }
}
