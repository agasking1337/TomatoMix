using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using TomatoMix.ViewModels;
using TomatoMix.Views;

namespace TomatoMix;

public partial class App : Application
{
    private MainWindowViewModel? _mainViewModel;
    private MainWindow? _mainWindow;
    private TrayIcon? _trayIcon;
    private NativeMenuItem? _openWindowItem;
    private NativeMenuItem? _masterMuteItem;
    private NativeMenuItem? _micMuteItem;
    private NativeMenuItem? _closeToTrayItem;
    private NativeMenuItem? _runAtStartupItem;
    private EventWaitHandle? _activateEvent;
    private CancellationTokenSource? _ipcCts;
    private bool _suppressTrayMenuCallbacks;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _mainViewModel = new MainWindowViewModel();
            _mainWindow = new MainWindow
            {
                DataContext = _mainViewModel
            };

            _mainViewModel.RequestHideToTray = () => _mainWindow?.HideToTray();
            _mainViewModel.RequestExitApplication = () => ShutdownApplication(desktop);
            _mainViewModel.TrayStateChanged = SyncTrayMenuState;

            _mainWindow.Closed += (_, _) =>
            {
                if (_mainWindow.IsExplicitExitRequested || !_mainViewModel.CloseToTray)
                {
                    ShutdownApplication(desktop);
                }
            };

            InitializeSystemTray(desktop);
            StartSingleInstanceActivationListener();

            bool startInBackground = ShouldStartInBackground(desktop.Args, _mainViewModel);
            if (startInBackground)
            {
                _mainWindow.ShowInTaskbar = false;
                _mainViewModel.SetBackgroundMode(true);
            }
            else
            {
                desktop.MainWindow = _mainWindow;
                _mainWindow.Show();
            }

            desktop.Exit += (_, _) => CleanupResources();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static bool ShouldStartInBackground(string[]? args, MainWindowViewModel vm)
    {
        if (vm.StartMinimizedToTray)
            return true;

        if (args == null || args.Length == 0)
            return false;

        return args.Any(a =>
            string.Equals(a, "--background", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase));
    }

    private void InitializeSystemTray(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var menu = new NativeMenu();

        _openWindowItem = new NativeMenuItem("Open TomatoMix Mixer");
        _openWindowItem.Click += (_, _) => ShowMainWindow(desktop);

        var hideWindowItem = new NativeMenuItem("Hide to Background Tray");
        hideWindowItem.Click += (_, _) => _mainWindow?.HideToTray();

        _masterMuteItem = new NativeMenuItem("Mute Master Output")
        {
            ToggleType = NativeMenuItemToggleType.CheckBox
        };
        _masterMuteItem.Click += (_, _) =>
        {
            if (_suppressTrayMenuCallbacks || _mainViewModel == null) return;
            _mainViewModel.IsMasterMuted = !_mainViewModel.IsMasterMuted;
        };

        _micMuteItem = new NativeMenuItem("Mute Microphone")
        {
            ToggleType = NativeMenuItemToggleType.CheckBox
        };
        _micMuteItem.Click += (_, _) =>
        {
            if (_suppressTrayMenuCallbacks || _mainViewModel?.MicChannel == null) return;
            _mainViewModel.MicChannel.IsMuted = !_mainViewModel.MicChannel.IsMuted;
        };

        _closeToTrayItem = new NativeMenuItem("Keep Running in Background (Close to Tray)")
        {
            ToggleType = NativeMenuItemToggleType.CheckBox
        };
        _closeToTrayItem.Click += (_, _) =>
        {
            if (_suppressTrayMenuCallbacks || _mainViewModel == null) return;
            _mainViewModel.CloseToTray = !_mainViewModel.CloseToTray;
        };

        _runAtStartupItem = new NativeMenuItem("Start with Windows")
        {
            ToggleType = NativeMenuItemToggleType.CheckBox
        };
        _runAtStartupItem.Click += (_, _) =>
        {
            if (_suppressTrayMenuCallbacks || _mainViewModel == null) return;
            _mainViewModel.RunAtWindowsStartup = !_mainViewModel.RunAtWindowsStartup;
        };

        var exitItem = new NativeMenuItem("Quit TomatoMix");
        exitItem.Click += (_, _) => ShutdownApplication(desktop);

        menu.Items.Add(_openWindowItem);
        menu.Items.Add(hideWindowItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(_masterMuteItem);
        menu.Items.Add(_micMuteItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(_closeToTrayItem);
        menu.Items.Add(_runAtStartupItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(exitItem);

        WindowIcon? icon = null;
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://TomatoMix/Assets/app.ico"));
            icon = new WindowIcon(stream);
        }
        catch
        {
        }

        _trayIcon = new TrayIcon
        {
            Icon = icon,
            ToolTipText = "TomatoMix Audio Mixer",
            Menu = menu,
            IsVisible = true
        };

        _trayIcon.Clicked += (_, _) =>
        {
            if (_mainWindow == null) return;
            if (_mainWindow.IsVisible && _mainWindow.WindowState != WindowState.Minimized)
            {
                _mainWindow.Activate();
            }
            else
            {
                ShowMainWindow(desktop);
            }
        };

        TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
        SyncTrayMenuState();
    }

    private void SyncTrayMenuState()
    {
        if (_mainViewModel == null) return;

        _suppressTrayMenuCallbacks = true;
        try
        {
            if (_masterMuteItem != null)
            {
                _masterMuteItem.IsChecked = _mainViewModel.IsMasterMuted;
            }
            if (_micMuteItem != null && _mainViewModel.MicChannel != null)
            {
                _micMuteItem.IsChecked = _mainViewModel.MicChannel.IsMuted;
            }
            if (_closeToTrayItem != null)
            {
                _closeToTrayItem.IsChecked = _mainViewModel.CloseToTray;
            }
            if (_runAtStartupItem != null)
            {
                _runAtStartupItem.IsChecked = _mainViewModel.RunAtWindowsStartup;
            }
            if (_trayIcon != null)
            {
                string mode = _mainViewModel.IsRunningInBackground ? "Background Active" : "Active";
                _trayIcon.ToolTipText = $"TomatoMix Audio Mixer ({mode})";
            }
        }
        finally
        {
            _suppressTrayMenuCallbacks = false;
        }
    }

    private void ShowMainWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_mainWindow == null) return;
        desktop.MainWindow ??= _mainWindow;
        _mainWindow.RestoreFromTray();
    }

    private void StartSingleInstanceActivationListener()
    {
        try
        {
            _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ActivateInstanceEventName);
            _ipcCts = new CancellationTokenSource();
            var token = _ipcCts.Token;

            Task.Run(() =>
            {
                var handles = new WaitHandle[] { _activateEvent, token.WaitHandle };
                while (!token.IsCancellationRequested)
                {
                    int signaled = WaitHandle.WaitAny(handles);
                    if (signaled == 0)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                            {
                                ShowMainWindow(desktop);
                            }
                        });
                    }
                    else
                    {
                        break;
                    }
                }
            }, token);
        }
        catch
        {
        }
    }

    private void ShutdownApplication(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_mainWindow != null)
        {
            _mainWindow.IsExplicitExitRequested = true;
        }
        CleanupResources();
        desktop.Shutdown();
    }

    private void CleanupResources()
    {
        try
        {
            _ipcCts?.Cancel();
            _ipcCts?.Dispose();
            _ipcCts = null;
        }
        catch
        {
        }

        try
        {
            _activateEvent?.Dispose();
            _activateEvent = null;
        }
        catch
        {
        }

        try
        {
            if (_trayIcon != null)
            {
                _trayIcon.IsVisible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
        }
        catch
        {
        }

        _mainViewModel?.Dispose();
        _mainViewModel = null;
    }
}
