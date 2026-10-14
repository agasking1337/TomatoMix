using System.Diagnostics;
using System.Drawing;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TomatoMix.Setup;

internal static class Program
{
    public const string AppName = "TomatoMix";
    public const string AppDisplayName = "TomatoMix Audio Mixer";
    public const string AppVersion = "1.0.1";
    public const string Publisher = "TomatoMix";
    public const string UninstallRegKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\TomatoMix";
    public const string StartupRegKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        string defaultInstallDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            AppName);

        bool isUninstall = args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)
                                      || a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase));
        bool isSilent = args.Any(a => a.Equals("--silent", StringComparison.OrdinalIgnoreCase)
                                   || a.Equals("/S", StringComparison.OrdinalIgnoreCase)
                                   || a.Equals("/quiet", StringComparison.OrdinalIgnoreCase));

        if (isUninstall)
        {
            return RunUninstall(defaultInstallDir, isSilent);
        }

        if (isSilent)
        {
            try
            {
                InstallApplication(defaultInstallDir, createDesktopShortcut: true, createStartMenuShortcut: true, runAtStartup: false, progress: null);
                return 0;
            }
            catch
            {
                return 1;
            }
        }

        using var form = new SetupWizardForm(defaultInstallDir);
        Application.Run(form);
        return 0;
    }

    public static void InstallApplication(
        string installDir,
        bool createDesktopShortcut,
        bool createStartMenuShortcut,
        bool runAtStartup,
        Action<int, string>? progress)
    {
        progress?.Invoke(5, "Stopping running TomatoMix instances...");
        KillRunningProcesses("TomatoMix");

        progress?.Invoke(15, "Creating installation directory...");
        Directory.CreateDirectory(installDir);

        progress?.Invoke(25, "Extracting TomatoMix Audio Mixer files...");
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TomatoMix.Payload.zip")
            ?? throw new InvalidOperationException("Embedded payload.zip not found in installer binary.");

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        int totalEntries = Math.Max(1, archive.Entries.Count);
        int extracted = 0;

        foreach (var entry in archive.Entries)
        {
            string fullPath = Path.GetFullPath(Path.Combine(installDir, entry.FullName));
            if (!fullPath.StartsWith(Path.GetFullPath(installDir), StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(fullPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            entry.ExtractToFile(fullPath, overwrite: true);
            extracted++;

            int pct = 25 + (int)((double)extracted / totalEntries * 55.0);
            progress?.Invoke(pct, $"Installing: {entry.Name}");
        }

        progress?.Invoke(82, "Registering uninstaller...");
        string currentSetupExe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
        string uninstallerExe = Path.Combine(installDir, "Uninstall-TomatoMix.exe");
        if (!string.IsNullOrEmpty(currentSetupExe) && File.Exists(currentSetupExe))
        {
            try
            {
                File.Copy(currentSetupExe, uninstallerExe, overwrite: true);
            }
            catch
            {
            }
        }

        string mainExePath = Path.Combine(installDir, "TomatoMix.exe");

        progress?.Invoke(88, "Creating shortcuts...");
        if (createDesktopShortcut)
        {
            string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string shortcutPath = Path.Combine(desktopDir, $"{AppDisplayName}.lnk");
            CreateWindowsShortcut(shortcutPath, mainExePath, installDir, "Multi-Channel Application Audio Router & Equalizer");
        }

        if (createStartMenuShortcut)
        {
            string programsDir = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            string folder = Path.Combine(programsDir, AppName);
            Directory.CreateDirectory(folder);
            CreateWindowsShortcut(Path.Combine(folder, $"{AppDisplayName}.lnk"), mainExePath, installDir, "Multi-Channel Application Audio Router & Equalizer");
            CreateWindowsShortcut(Path.Combine(folder, $"Uninstall {AppName}.lnk"), uninstallerExe, installDir, "Uninstall TomatoMix Audio Mixer", "--uninstall");
        }

        progress?.Invoke(94, "Registering with Windows Installed Apps...");
        RegisterInWindowsPrograms(installDir, mainExePath, uninstallerExe, runAtStartup);

        progress?.Invoke(100, "Installation complete!");
    }

    private static void RegisterInWindowsPrograms(string installDir, string mainExePath, string uninstallerExe, bool runAtStartup)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(UninstallRegKeyPath);
            if (key != null)
            {
                key.SetValue("DisplayName", AppDisplayName);
                key.SetValue("DisplayVersion", AppVersion);
                key.SetValue("Publisher", Publisher);
                key.SetValue("DisplayIcon", mainExePath);
                key.SetValue("InstallLocation", installDir);
                key.SetValue("UninstallString", $"\"{uninstallerExe}\" --uninstall");
                key.SetValue("QuietUninstallString", $"\"{uninstallerExe}\" --uninstall --silent");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);

                long sizeKb = new DirectoryInfo(installDir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) / 1024;
                key.SetValue("EstimatedSize", (int)Math.Clamp(sizeKb, 1024, int.MaxValue), RegistryValueKind.DWord);
            }

            using var runKey = Registry.CurrentUser.CreateSubKey(StartupRegKeyPath);
            if (runKey != null)
            {
                if (runAtStartup)
                {
                    runKey.SetValue(AppName, $"\"{mainExePath}\"");
                }
                else
                {
                    runKey.DeleteValue(AppName, throwOnMissingValue: false);
                }
            }
        }
        catch
        {
        }
    }

    private static int RunUninstall(string fallbackInstallDir, bool silent)
    {
        string installDir = fallbackInstallDir;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallRegKeyPath);
            var regDir = key?.GetValue("InstallLocation") as string;
            if (!string.IsNullOrWhiteSpace(regDir))
            {
                installDir = regDir;
            }
        }
        catch
        {
        }

        if (!silent)
        {
            var confirm = MessageBox.Show(
                $"Are you sure you want to completely uninstall {AppDisplayName} from your computer?",
                $"Uninstall {AppDisplayName}",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes)
                return 0;
        }

        KillRunningProcesses("TomatoMix");

        try
        {
            string desktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"{AppDisplayName}.lnk");
            if (File.Exists(desktopLnk)) File.Delete(desktopLnk);

            string startMenuFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName);
            if (Directory.Exists(startMenuFolder)) Directory.Delete(startMenuFolder, recursive: true);

            Registry.CurrentUser.DeleteSubKeyTree(UninstallRegKeyPath, throwOnMissingSubKey: false);
            using var runKey = Registry.CurrentUser.OpenSubKey(StartupRegKeyPath, writable: true);
            runKey?.DeleteValue(AppName, throwOnMissingValue: false);
        }
        catch
        {
        }

        // Delete installation files (and schedule self-deletion if running from inside installDir)
        try
        {
            if (Directory.Exists(installDir))
            {
                foreach (var file in Directory.GetFiles(installDir, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        if (!string.Equals(file, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                        {
                            File.Delete(file);
                        }
                    }
                    catch
                    {
                    }
                }

                // Spawn background cmd to remove remaining folder after process exits
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/C timeout /t 2 /nobreak > NUL & rmdir /s /q \"{installDir}\"",
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
        }
        catch
        {
        }

        if (!silent)
        {
            MessageBox.Show(
                $"{AppDisplayName} has been uninstalled.",
                $"Uninstall {AppDisplayName}",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        return 0;
    }

    private static void KillRunningProcesses(string processName)
    {
        try
        {
            int currentPid = Environment.ProcessId;
            foreach (var proc in Process.GetProcessesByName(processName))
            {
                if (proc.Id == currentPid) continue;
                try
                {
                    proc.Kill();
                    proc.WaitForExit(3000);
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    private static void CreateWindowsShortcut(string shortcutPath, string targetPath, string workingDir, string description, string arguments = "")
    {
        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell == null) return;

            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = targetPath;
            shortcut.WorkingDirectory = workingDir;
            shortcut.Description = description;
            shortcut.IconLocation = $"{targetPath},0";
            if (!string.IsNullOrEmpty(arguments))
            {
                shortcut.Arguments = arguments;
            }
            shortcut.Save();
            Marshal.FinalReleaseComObject(shortcut);
            Marshal.FinalReleaseComObject(shell);
        }
        catch
        {
        }
    }
}

internal sealed class SetupWizardForm : Form
{
    private readonly TextBox _pathBox = new();
    private readonly CheckBox _desktopCheck = new() { Text = "Create a desktop shortcut", Checked = true, AutoSize = true };
    private readonly CheckBox _startMenuCheck = new() { Text = "Create Start Menu shortcuts", Checked = true, AutoSize = true };
    private readonly CheckBox _startupCheck = new() { Text = "Start TomatoMix when Windows starts", AutoSize = true };
    private readonly CheckBox _launchAfterCheck = new() { Text = "Launch TomatoMix when setup finishes", Checked = true, AutoSize = true };
    private readonly ProgressBar _progressBar = new() { Minimum = 0, Maximum = 100 };
    private readonly Label _statusLabel = new() { Text = "Ready to install.", AutoSize = false };
    private readonly Button _installButton = new() { Text = "Install" };
    private readonly Button _browseButton = new() { Text = "Browse..." };
    private readonly Button _cancelButton = new() { Text = "Cancel" };
    private bool _isInstalled;

    public SetupWizardForm(string defaultInstallDir)
    {
        Text = "TomatoMix Setup";
        ClientSize = new Size(460, 290);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = SystemFonts.MessageBoxFont ?? Font;

        try
        {
            using var icoStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TomatoMix.App.ico");
            if (icoStream != null) Icon = new Icon(icoStream);
        }
        catch
        {
        }

        var dirLabel = new Label { Text = "Install location:", Location = new Point(16, 16), AutoSize = true };
        _pathBox.Text = defaultInstallDir;
        _pathBox.Location = new Point(16, 38);
        _pathBox.Size = new Size(342, 23);
        _browseButton.Location = new Point(366, 37);
        _browseButton.Size = new Size(78, 25);
        _browseButton.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { SelectedPath = _pathBox.Text };
            if (dlg.ShowDialog(this) == DialogResult.OK) _pathBox.Text = dlg.SelectedPath;
        };

        _desktopCheck.Location = new Point(16, 80);
        _startMenuCheck.Location = new Point(16, 106);
        _startupCheck.Location = new Point(16, 132);
        _launchAfterCheck.Location = new Point(16, 158);

        _statusLabel.Location = new Point(16, 196);
        _statusLabel.Size = new Size(428, 18);
        _progressBar.Location = new Point(16, 218);
        _progressBar.Size = new Size(428, 16);

        _installButton.Location = new Point(288, 250);
        _installButton.Size = new Size(75, 27);
        _installButton.Click += OnInstallClicked;
        _cancelButton.Location = new Point(369, 250);
        _cancelButton.Size = new Size(75, 27);
        _cancelButton.Click += (_, _) => Close();

        AcceptButton = _installButton;
        CancelButton = _cancelButton;
        Controls.AddRange([dirLabel, _pathBox, _browseButton, _desktopCheck, _startMenuCheck, _startupCheck, _launchAfterCheck, _statusLabel, _progressBar, _installButton, _cancelButton]);
    }

    private async void OnInstallClicked(object? sender, EventArgs e)
    {
        string targetDir = _pathBox.Text.Trim();

        if (_isInstalled)
        {
            string exe = Path.Combine(targetDir, "TomatoMix.exe");
            if (_launchAfterCheck.Checked && File.Exists(exe))
            {
                Process.Start(new ProcessStartInfo { FileName = exe, WorkingDirectory = targetDir, UseShellExecute = true });
            }
            Close();
            return;
        }

        if (string.IsNullOrWhiteSpace(targetDir))
        {
            MessageBox.Show(this, "Please choose a valid installation folder.", "Setup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        foreach (Control c in new Control[] { _installButton, _browseButton, _pathBox, _desktopCheck, _startMenuCheck, _startupCheck })
            c.Enabled = false;

        bool desktop = _desktopCheck.Checked, startMenu = _startMenuCheck.Checked, startup = _startupCheck.Checked;

        try
        {
            await Task.Run(() => Program.InstallApplication(targetDir, desktop, startMenu, startup, (pct, msg) =>
            {
                if (!IsDisposed)
                {
                    BeginInvoke(() =>
                    {
                        _progressBar.Value = Math.Clamp(pct, 0, 100);
                        _statusLabel.Text = msg;
                    });
                }
            }));

            _isInstalled = true;
            _statusLabel.Text = "Installation complete.";
            _installButton.Text = "Finish";
            _installButton.Enabled = true;
            _cancelButton.Visible = false;
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Installation failed.";
            foreach (Control c in new Control[] { _installButton, _browseButton, _pathBox, _desktopCheck, _startMenuCheck, _startupCheck })
                c.Enabled = true;
            MessageBox.Show(this, $"Failed to install TomatoMix:\n{ex.Message}", "Setup Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
