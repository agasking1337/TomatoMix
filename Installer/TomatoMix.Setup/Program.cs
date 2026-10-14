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
    public const string AppVersion = "1.0.0";
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
    private readonly TextBox _pathBox;
    private readonly CheckBox _desktopCheck;
    private readonly CheckBox _startMenuCheck;
    private readonly CheckBox _startupCheck;
    private readonly CheckBox _launchAfterCheck;
    private readonly ProgressBar _progressBar;
    private readonly Label _statusLabel;
    private readonly Button _installButton;
    private readonly Button _browseButton;
    private readonly Button _cancelButton;
    private bool _isInstalled;

    public SetupWizardForm(string defaultInstallDir)
    {
        Text = "TomatoMix Audio Mixer - Setup";
        Size = new Size(560, 440);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(16, 19, 27);
        ForeColor = Color.FromArgb(241, 245, 249);
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

        try
        {
            using var icoStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TomatoMix.App.ico");
            if (icoStream != null)
            {
                Icon = new Icon(icoStream);
            }
        }
        catch
        {
        }

        // Top Accent Strip
        var accentStrip = new Panel
        {
            Dock = DockStyle.Top,
            Height = 4,
            BackColor = Color.FromArgb(255, 85, 0)
        };
        Controls.Add(accentStrip);

        // Header Panel
        var headerPanel = new Panel
        {
            Location = new Point(0, 4),
            Size = new Size(560, 76),
            BackColor = Color.FromArgb(21, 25, 36)
        };

        var titleLabel = new Label
        {
            Text = "TOMATOMIX // AUDIO MIXER SETUP",
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(24, 16),
            AutoSize = true
        };

        var subLabel = new Label
        {
            Text = "Install Multi-Channel Application Audio Router, Mic DSP & 10-Band Equalizer (v1.0.0)",
            Font = new Font("Segoe UI", 9f, FontStyle.Regular),
            ForeColor = Color.FromArgb(148, 163, 184),
            Location = new Point(24, 44),
            AutoSize = true
        };

        headerPanel.Controls.Add(titleLabel);
        headerPanel.Controls.Add(subLabel);
        Controls.Add(headerPanel);

        // Install Folder Label
        var dirLabel = new Label
        {
            Text = "INSTALLATION DIRECTORY",
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(148, 163, 184),
            Location = new Point(24, 98),
            AutoSize = true
        };
        Controls.Add(dirLabel);

        _pathBox = new TextBox
        {
            Text = defaultInstallDir,
            Location = new Point(24, 120),
            Size = new Size(406, 28),
            BackColor = Color.FromArgb(25, 30, 43),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9.5f)
        };
        Controls.Add(_pathBox);

        _browseButton = new Button
        {
            Text = "BROWSE...",
            Location = new Point(438, 119),
            Size = new Size(88, 27),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(30, 36, 50),
            ForeColor = Color.White,
            Cursor = Cursors.Hand
        };
        _browseButton.FlatAppearance.BorderColor = Color.FromArgb(51, 65, 85);
        _browseButton.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog
            {
                SelectedPath = _pathBox.Text,
                Description = "Select destination folder for TomatoMix Audio Mixer"
            };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _pathBox.Text = dlg.SelectedPath;
            }
        };
        Controls.Add(_browseButton);

        // Options Checkboxes
        _desktopCheck = new CheckBox
        {
            Text = "Create a Desktop shortcut",
            Checked = true,
            Location = new Point(26, 166),
            AutoSize = true,
            ForeColor = Color.FromArgb(226, 232, 240)
        };
        Controls.Add(_desktopCheck);

        _startMenuCheck = new CheckBox
        {
            Text = "Create Start Menu program shortcuts",
            Checked = true,
            Location = new Point(26, 194),
            AutoSize = true,
            ForeColor = Color.FromArgb(226, 232, 240)
        };
        Controls.Add(_startMenuCheck);

        _startupCheck = new CheckBox
        {
            Text = "Launch TomatoMix automatically when Windows starts",
            Checked = false,
            Location = new Point(26, 222),
            AutoSize = true,
            ForeColor = Color.FromArgb(226, 232, 240)
        };
        Controls.Add(_startupCheck);

        _launchAfterCheck = new CheckBox
        {
            Text = "Launch TomatoMix Audio Mixer when setup finishes",
            Checked = true,
            Location = new Point(26, 250),
            AutoSize = true,
            ForeColor = Color.FromArgb(255, 110, 38),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };
        Controls.Add(_launchAfterCheck);

        // Status & Progress Bar
        _statusLabel = new Label
        {
            Text = "Ready to install TomatoMix Audio Mixer.",
            Location = new Point(24, 292),
            Size = new Size(500, 20),
            ForeColor = Color.FromArgb(148, 163, 184)
        };
        Controls.Add(_statusLabel);

        _progressBar = new ProgressBar
        {
            Location = new Point(24, 316),
            Size = new Size(502, 16),
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Style = ProgressBarStyle.Continuous
        };
        Controls.Add(_progressBar);

        // Bottom Buttons
        _cancelButton = new Button
        {
            Text = "CANCEL",
            Location = new Point(310, 352),
            Size = new Size(98, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(30, 36, 50),
            ForeColor = Color.FromArgb(203, 213, 225),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        _cancelButton.FlatAppearance.BorderColor = Color.FromArgb(51, 65, 85);
        _cancelButton.Click += (_, _) => Close();
        Controls.Add(_cancelButton);

        _installButton = new Button
        {
            Text = "INSTALL NOW",
            Location = new Point(416, 352),
            Size = new Size(110, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(255, 85, 0),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        _installButton.FlatAppearance.BorderSize = 0;
        _installButton.Click += OnInstallClicked;
        Controls.Add(_installButton);
    }

    private async void OnInstallClicked(object? sender, EventArgs e)
    {
        if (_isInstalled)
        {
            if (_launchAfterCheck.Checked)
            {
                string exe = Path.Combine(_pathBox.Text.Trim(), "TomatoMix.exe");
                if (File.Exists(exe))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = exe,
                        WorkingDirectory = _pathBox.Text.Trim(),
                        UseShellExecute = true
                    });
                }
            }
            Close();
            return;
        }

        string targetDir = _pathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(targetDir))
        {
            MessageBox.Show(this, "Please choose a valid installation directory.", "Setup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _installButton.Enabled = false;
        _browseButton.Enabled = false;
        _pathBox.ReadOnly = true;
        _desktopCheck.Enabled = false;
        _startMenuCheck.Enabled = false;
        _startupCheck.Enabled = false;

        bool desktop = _desktopCheck.Checked;
        bool startMenu = _startMenuCheck.Checked;
        bool startup = _startupCheck.Checked;

        try
        {
            await Task.Run(() =>
            {
                Program.InstallApplication(
                    targetDir,
                    desktop,
                    startMenu,
                    startup,
                    (pct, msg) =>
                    {
                        if (!IsDisposed)
                        {
                            BeginInvoke(() =>
                            {
                                _progressBar.Value = Math.Clamp(pct, 0, 100);
                                _statusLabel.Text = msg;
                            });
                        }
                    });
            });

            _isInstalled = true;
            _installButton.Text = "FINISH";
            _installButton.BackColor = Color.FromArgb(16, 185, 129);
            _installButton.Enabled = true;
            _cancelButton.Visible = false;
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Installation failed: {ex.Message}";
            _installButton.Enabled = true;
            _browseButton.Enabled = true;
            _pathBox.ReadOnly = false;
            MessageBox.Show(this, $"Failed to install TomatoMix:\n{ex.Message}", "Setup Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
