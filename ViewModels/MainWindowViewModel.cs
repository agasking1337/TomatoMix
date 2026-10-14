using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TomatoMix.Models;
using TomatoMix.Services;
using TomatoMix.Theme;

namespace TomatoMix.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly WindowsAudioService _audioService = new();
    private readonly ConfigService _configService = new();
    private readonly EqualizerApoService _apoService = new();
    private readonly RealtimeEqAudioEngine _dspEngine = new();
    private readonly DispatcherTimer _meterTimer;
    private readonly Dictionary<string, AppSessionViewModel> _allSessionsByKey = new(StringComparer.OrdinalIgnoreCase);
    private MixerConfig _config;
    private bool _suppressDeviceSelection;
    private bool _suppressMicDeviceSelection;
    private bool _suppressMasterUpdate;
    private bool _suppressMicUpdate;
    private bool _suppressEqProfileSelection;
    private bool _suppressEqBandSync;
    private bool _lastAnySolo;
    private int _tickCount;
    private int _pollInFlight;

    private static readonly string[] CustomChannelIcons =
    [
        "settings",
        "dashboard",
        "terminal",
        "play",
        "layers",
        "star"
    ];

    public ObservableCollection<AudioDeviceInfo> OutputDevices { get; } = new();
    public ObservableCollection<AudioDeviceInfo> InputDevices { get; } = new();
    public ObservableCollection<ChannelViewModel> Channels { get; } = new();
    public ObservableCollection<EqProfile> AvailableEqProfiles { get; } = new();
    public ObservableCollection<EqBandViewModel> ActiveEqBands { get; } = new();

    [ObservableProperty]
    private bool _isDarkTheme = true;

    [ObservableProperty]
    private ChannelViewModel _micChannel = null!;

    [ObservableProperty]
    private AudioDeviceInfo? _selectedDevice;

    [ObservableProperty]
    private AudioDeviceInfo? _selectedMicDevice;

    [ObservableProperty]
    private bool _isMicMonitoring;

    [ObservableProperty]
    private double _masterVolumePercent = 100.0;

    [ObservableProperty]
    private bool _isMasterMuted;

    [ObservableProperty]
    private double _masterPeakLevel;

    [ObservableProperty]
    private double _masterPeakBarHeight;

    [ObservableProperty]
    private bool _isAddChannelPopupOpen;

    [ObservableProperty]
    private string _newChannelName = string.Empty;

    [ObservableProperty]
    private string _newChannelIconKey = "settings";

    [ObservableProperty]
    private double _chatMixBalance; // -100 (Full Game) .. 0 (Center) .. +100 (Full Chat)

    [ObservableProperty]
    private double _gameChannelPeakLevel;

    [ObservableProperty]
    private double _chatChannelPeakLevel;

    [ObservableProperty]
    private int _totalRoutedAppsCount;

    [ObservableProperty]
    private string _statusMessage = "SONAR CORE ENGINE ACTIVE";

    [ObservableProperty]
    private StatusLevel _engineStatusLevel = StatusLevel.Ok;

    [ObservableProperty]
    private IBrush _engineStatusBrush = StatusMapping.ResolveStatusBrush(StatusLevel.Ok);

    [ObservableProperty]
    private IBrush _masterStatusBrush = StatusMapping.ResolveStatusBrush(StatusLevel.Ok);

    // ==================== PER-CHANNEL EQ STUDIO STATE ====================
    [ObservableProperty]
    private bool _isEqEditorOpen;

    [ObservableProperty]
    private ChannelViewModel? _eqTargetChannel;

    [ObservableProperty]
    private EqProfile? _selectedEqProfile;

    [ObservableProperty]
    private string _customEqProfileNameInput = string.Empty;

    [ObservableProperty]
    private double _activeEqPreampDb;

    [ObservableProperty]
    private Points _largeEqCurvePoints = new();

    [ObservableProperty]
    private Points _largeEqFillPoints = new();

    [ObservableProperty]
    private bool _isAuditioningEq;

    [ObservableProperty]
    private bool _isApoInstalled;

    [ObservableProperty]
    private string _apoStatusText = "CHECKING APO...";

    [ObservableProperty]
    private IBrush _apoStatusBrush = StatusMapping.ResolveStatusBrush(StatusLevel.Unknown);

    public IconName ThemeToggleIcon => IsDarkTheme ? IconName.Sun : IconName.Moon;
    public string ThemeToggleLabel => IsDarkTheme ? "LIGHT" : "DARK";

    public bool CanDeleteSelectedEqProfile => SelectedEqProfile is { IsBuiltIn: false };
    public string ActiveEqPreampText => ActiveEqPreampDb >= 0 ? $"+{ActiveEqPreampDb:0.0} dB" : $"{ActiveEqPreampDb:0.0} dB";
    public string EqProfilesFolderPath => _configService.ProfilesDirectory;
    public string AuditionButtonText => IsAuditioningEq ? "STOP TEST LOOP" : "AUDITION EQ";

    public string MasterVolumePercentText => IsMasterMuted ? "MUTED" : $"{Math.Round(MasterVolumePercent):0}%";
    public string ChatMixLabelText
    {
        get
        {
            if (Math.Abs(ChatMixBalance) < 3) return "BALANCED";
            return ChatMixBalance < 0
                ? $"GAME +{Math.Round(Math.Abs(ChatMixBalance)):0}%"
                : $"CHAT +{Math.Round(ChatMixBalance):0}%";
        }
    }

    public double GameMixPercent => ChatMixBalance > 0
        ? Math.Clamp((1.0 - (ChatMixBalance / 100.0)) * 100.0, 0.0, 100.0)
        : 100.0;

    public double ChatMixPercent => ChatMixBalance < 0
        ? Math.Clamp((1.0 - (Math.Abs(ChatMixBalance) / 100.0)) * 100.0, 0.0, 100.0)
        : 100.0;

    public string GameMixPercentText => $"{Math.Round(GameMixPercent):0}%";
    public string ChatMixPercentText => $"{Math.Round(ChatMixPercent):0}%";

    public MainWindowViewModel()
    {
        _config = _configService.Load();
        _isDarkTheme = _config.IsDarkTheme;
        ThemeTokens.SetTheme(_isDarkTheme);

        InitializeEqBands();
        LoadEqProfilesList();
        InitializeMicChannel();
        LoadDevices();
        LoadMicDevices();
        LoadChannelsFromConfig();
        SyncMasterFromHardware();
        SyncMicFromHardware();
        ScanAndRouteAudioSessions();
        SyncHardwareEqualizerApo();
        RefreshAllStatusBrushes();

        _meterTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50) // 20 FPS smooth meters + session discovery
        };
        _meterTimer.Tick += OnMeterTimerTick;
        _meterTimer.Start();
    }

    partial void OnIsDarkThemeChanged(bool value)
    {
        ThemeTokens.SetTheme(value);
        OnPropertyChanged(nameof(ThemeToggleIcon));
        OnPropertyChanged(nameof(ThemeToggleLabel));
        _config.IsDarkTheme = value;
        SchedulePersist();
        RefreshAllStatusBrushes(force: true);
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
        StatusMessage = IsDarkTheme ? "THEME: DARK MONOCHROME" : "THEME: LIGHT MONOCHROME";
    }

    private void RefreshAllStatusBrushes(bool force = false)
    {
        var masterLevel = IsMasterMuted
            ? StatusLevel.Critical
            : SelectedDevice == null
                ? StatusLevel.Offline
                : StatusLevel.Ok;
        MasterStatusBrush = StatusMapping.ResolveStatusBrush(masterLevel);

        bool anySolo = Channels.Any(c => c.IsSolo);
        EngineStatusLevel = SelectedDevice == null
            ? StatusLevel.Offline
            : IsMasterMuted
                ? StatusLevel.Critical
                : anySolo || IsMicMonitoring
                    ? StatusLevel.Warning
                    : StatusLevel.Ok;
        EngineStatusBrush = StatusMapping.ResolveStatusBrush(EngineStatusLevel);

        var apoLevel = string.Equals(ApoStatusText, "CHECKING APO...", StringComparison.OrdinalIgnoreCase)
            ? StatusLevel.Unknown
            : IsApoInstalled
                ? StatusLevel.Ok
                : StatusLevel.Warning;
        ApoStatusBrush = StatusMapping.ResolveStatusBrush(apoLevel);

        MicChannel?.RefreshStatusTheme(force);
        foreach (var ch in Channels)
        {
            ch.RefreshStatusTheme(force);
        }
    }

    private void InitializeEqBands()
    {
        ActiveEqBands.Clear();
        for (int i = 0; i < 10; i++)
        {
            ActiveEqBands.Add(new EqBandViewModel(
                i,
                EqProfile.StandardFrequencies[i],
                EqProfile.StandardFrequencyLabels[i],
                0.0,
                OnActiveEqBandGainChanged));
        }
    }

    private void LoadEqProfilesList()
    {
        AvailableEqProfiles.Clear();
        foreach (var builtIn in ConfigService.GetBuiltInEqProfiles())
        {
            AvailableEqProfiles.Add(builtIn);
        }

        foreach (var custom in _config.CustomEqProfiles)
        {
            custom.IsBuiltIn = false;
            if (!AvailableEqProfiles.Any(p => p.Name.Equals(custom.Name, StringComparison.OrdinalIgnoreCase)))
            {
                AvailableEqProfiles.Add(custom);
            }
        }
    }

    private void LoadDevices()
    {
        _suppressDeviceSelection = true;
        OutputDevices.Clear();
        var devices = _audioService.GetOutputDevices();
        foreach (var d in devices)
        {
            OutputDevices.Add(d);
        }

        var chosen = OutputDevices.FirstOrDefault(d => d.Id == _config.SelectedDeviceId)
                     ?? OutputDevices.FirstOrDefault(d => d.IsDefault)
                     ?? OutputDevices.FirstOrDefault();

        SelectedDevice = chosen;
        _audioService.SelectDevice(chosen?.Id);
        _suppressDeviceSelection = false;
    }

    private void InitializeMicChannel()
    {
        var micCfg = new ChannelConfig
        {
            Id = "mic",
            Name = "MIC",
            IconGlyph = "tunnel",
            Volume = _config.MicVolume,
            IsMuted = _config.MicMuted,
            IsPreset = true,
            IsEqEnabled = _config.IsMicEqEnabled,
            ActiveEqProfileName = _config.ActiveMicEqProfileName,
            EqPreampDb = _config.MicEqPreampDb,
            EqGainsDb = _config.MicEqGainsDb is { Length: 10 } ? (double[])_config.MicEqGainsDb.Clone() : new double[10]
        };

        MicChannel = new ChannelViewModel(micCfg, OnMicChannelStateChanged, _ => { }, OpenChannelEqStudio);
    }

    private void LoadMicDevices()
    {
        _suppressMicDeviceSelection = true;
        InputDevices.Clear();
        var micDevices = _audioService.GetInputDevices();
        foreach (var d in micDevices)
        {
            InputDevices.Add(d);
        }

        var chosen = InputDevices.FirstOrDefault(d => d.Id == _config.SelectedMicDeviceId)
                     ?? InputDevices.FirstOrDefault(d => d.IsDefault)
                     ?? InputDevices.FirstOrDefault();

        SelectedMicDevice = chosen;
        _audioService.SelectMicDevice(chosen?.Id);
        _suppressMicDeviceSelection = false;
    }

    partial void OnSelectedMicDeviceChanged(AudioDeviceInfo? value)
    {
        if (_suppressMicDeviceSelection || value == null) return;

        _audioService.SelectMicDevice(value.Id);
        _config.SelectedMicDeviceId = value.Id;
        _configService.Save(_config);

        SyncMicFromHardware();
        if (IsMicMonitoring)
        {
            RestartMicMonitor();
        }
        StatusMessage = $"MIC INPUT: {value.Name.ToUpperInvariant()}";
    }

    private void SyncMicFromHardware()
    {
        if (MicChannel == null) return;
        _suppressMicUpdate = true;
        var (vol, muted, peak) = _audioService.GetMicState();
        MicChannel.VolumePercent = Math.Clamp(vol * 100.0, 0.0, 100.0);
        MicChannel.IsMuted = muted;
        MicChannel.PeakLevel = Math.Clamp(peak * 100.0, 0.0, 100.0);
        _suppressMicUpdate = false;
    }

    private void OnMicChannelStateChanged(ChannelViewModel mic)
    {
        if (_suppressMicUpdate) return;

        _audioService.SetMicVolume((float)(mic.VolumePercent / 100.0), mic.IsMuted);
        _config.MicVolume = mic.VolumePercent / 100.0;
        _config.MicMuted = mic.IsMuted;
        _config.IsMicEqEnabled = mic.IsEqEnabled;
        _config.ActiveMicEqProfileName = mic.ActiveEqProfileName;
        _config.MicEqPreampDb = mic.EqPreampDb;
        _config.MicEqGainsDb = (double[])mic.EqGainsDb.Clone();
        SchedulePersist();

        if (IsMicMonitoring)
        {
            _dspEngine.UpdateMicEqParameters(mic.EqGainsDb, mic.EqPreampDb, mic.IsEqEnabled);
        }

        if (EqTargetChannel == mic)
        {
            RefreshLargeEqCurve();
            ScheduleApoSync();
        }
    }

    partial void OnIsMicMonitoringChanged(bool value)
    {
        if (value)
        {
            RestartMicMonitor();
            StatusMessage = "MIC SIDETONE / LISTEN ENABLED (REAL-TIME 10-BAND BIQUAD EQ)";
        }
        else
        {
            _dspEngine.StopMicMonitor();
            StatusMessage = "MIC SIDETONE DISABLED";
        }
        RefreshAllStatusBrushes();
    }

    private void RestartMicMonitor()
    {
        var dev = _audioService.GetClonedMicDevice(SelectedMicDevice?.Id);
        _dspEngine.StartMicMonitor(dev, MicChannel.EqGainsDb, MicChannel.EqPreampDb, MicChannel.IsEqEnabled);
    }

    partial void OnSelectedDeviceChanged(AudioDeviceInfo? value)
    {
        if (_suppressDeviceSelection || value == null) return;

        _audioService.SelectDevice(value.Id);
        _config.SelectedDeviceId = value.Id;
        _configService.Save(_config);

        _allSessionsByKey.Clear();
        foreach (var ch in Channels)
        {
            ch.Apps.Clear();
        }

        SyncMasterFromHardware();
        ScanAndRouteAudioSessions();
        StatusMessage = $"OUTPUT: {value.Name.ToUpperInvariant()}";
    }

    private void LoadChannelsFromConfig()
    {
        Channels.Clear();
        foreach (var chCfg in _config.Channels)
        {
            var vm = new ChannelViewModel(chCfg, OnChannelStateChanged, OnDeleteChannelRequested, OpenChannelEqStudio);
            Channels.Add(vm);
        }
    }

    private void SyncMasterFromHardware()
    {
        _suppressMasterUpdate = true;
        var (vol, muted, peak) = _audioService.GetMasterState();
        MasterVolumePercent = Math.Clamp(vol * 100.0, 0.0, 100.0);
        IsMasterMuted = muted;
        MasterPeakLevel = Math.Clamp(peak * 100.0, 0.0, 100.0);
        _suppressMasterUpdate = false;
    }

    partial void OnMasterVolumePercentChanged(double value)
    {
        OnPropertyChanged(nameof(MasterVolumePercentText));
        if (_suppressMasterUpdate) return;

        _audioService.SetMasterVolume((float)(value / 100.0), IsMasterMuted);
        _config.MasterVolume = value / 100.0;
        SchedulePersist();
    }

    partial void OnIsMasterMutedChanged(bool value)
    {
        OnPropertyChanged(nameof(MasterVolumePercentText));
        RefreshAllStatusBrushes();
        if (_suppressMasterUpdate) return;

        _audioService.SetMasterVolume((float)(MasterVolumePercent / 100.0), value);
        _config.MasterMuted = value;
        SchedulePersist();
    }

    partial void OnMasterPeakLevelChanged(double value)
    {
        MasterPeakBarHeight = Math.Clamp(value / 100.0 * 185.0, 0.0, 185.0);
    }

    partial void OnChatMixBalanceChanged(double value)
    {
        OnPropertyChanged(nameof(ChatMixLabelText));
        OnPropertyChanged(nameof(GameMixPercent));
        OnPropertyChanged(nameof(ChatMixPercent));
        OnPropertyChanged(nameof(GameMixPercentText));
        OnPropertyChanged(nameof(ChatMixPercentText));

        bool anySolo = Channels.Any(c => c.IsSolo);
        foreach (var ch in Channels)
        {
            if (ch.Id.Equals("game", StringComparison.OrdinalIgnoreCase)
                || ch.Id.Equals("chat", StringComparison.OrdinalIgnoreCase))
            {
                ApplyChannelVolumesToAudioEngine(ch, anySolo);
            }
        }
    }

    // ==================== PER-CHANNEL EQ STUDIO & PROFILE SAVER METHODS ====================

    public void OpenChannelEqStudio(ChannelViewModel channel)
    {
        EqTargetChannel = channel;
        _suppressEqBandSync = true;
        _suppressEqProfileSelection = true;

        ActiveEqPreampDb = channel.EqPreampDb;
        for (int i = 0; i < 10 && i < ActiveEqBands.Count; i++)
        {
            ActiveEqBands[i].SetGainSilent(channel.EqGainsDb[i]);
        }

        SelectedEqProfile = AvailableEqProfiles.FirstOrDefault(p =>
            p.Name.Equals(channel.ActiveEqProfileName, StringComparison.OrdinalIgnoreCase));

        CustomEqProfileNameInput = SelectedEqProfile is { IsBuiltIn: false }
            ? SelectedEqProfile.Name
            : $"{channel.Name} CUSTOM";

        _suppressEqProfileSelection = false;
        _suppressEqBandSync = false;

        RefreshLargeEqCurve();
        OnPropertyChanged(nameof(CanDeleteSelectedEqProfile));
        IsEqEditorOpen = true;
        SyncHardwareEqualizerApo();
        StatusMessage = $"OPENED 10-BAND PARAMETRIC EQ // {channel.Name}";
    }

    [RelayCommand]
    private void CloseEqEditor()
    {
        if (IsAuditioningEq)
        {
            IsAuditioningEq = false;
        }
        IsEqEditorOpen = false;
        SyncHardwareEqualizerApo();
    }

    partial void OnIsAuditioningEqChanged(bool value)
    {
        OnPropertyChanged(nameof(AuditionButtonText));
        if (value && EqTargetChannel != null)
        {
            _dspEngine.StartTestSoundAudition(
                EqTargetChannel.EqGainsDb,
                EqTargetChannel.EqPreampDb,
                EqTargetChannel.IsEqEnabled);
            StatusMessage = $"PLAYING LIVE 10-BAND BIQUAD DSP TEST LOOP ({EqTargetChannel.Name})";
        }
        else
        {
            _dspEngine.Stop();
            StatusMessage = "STOPPED EQ TEST LOOP";
        }
    }

    [RelayCommand]
    private void ToggleAuditionEq()
    {
        IsAuditioningEq = !IsAuditioningEq;
    }

    [RelayCommand]
    private void OpenOrInstallEqualizerApo()
    {
        if (_apoService.DetectEqualizerApoConfigDirectory())
        {
            if (!_apoService.LaunchEqualizerApoConfigurator())
            {
                _apoService.OpenEqualizerApoDownloadPage();
            }
            SyncHardwareEqualizerApo();
        }
        else
        {
            _apoService.OpenEqualizerApoDownloadPage();
            StatusMessage = "OPENED EQUALIZER APO DOWNLOAD PAGE — INSTALL TO ENABLE SYSTEM-WIDE 0ms HARDWARE APO";
        }
    }

    private void SyncHardwareEqualizerApo()
    {
        var (installed, summary) = _apoService.SyncLiveEqualizerApo(
            Channels,
            IsEqEditorOpen ? EqTargetChannel : null);
        IsApoInstalled = installed;
        ApoStatusText = summary;
        RefreshAllStatusBrushes();

        if (IsAuditioningEq && EqTargetChannel != null)
        {
            _dspEngine.UpdateEqParameters(
                EqTargetChannel.EqGainsDb,
                EqTargetChannel.EqPreampDb,
                EqTargetChannel.IsEqEnabled);
        }

        if (IsMicMonitoring && MicChannel != null)
        {
            _dspEngine.UpdateMicEqParameters(
                MicChannel.EqGainsDb,
                MicChannel.EqPreampDb,
                MicChannel.IsEqEnabled);
        }
    }

    [RelayCommand]
    private void OpenMicEqStudio()
    {
        if (MicChannel != null)
        {
            OpenChannelEqStudio(MicChannel);
        }
    }

    [RelayCommand]
    private void SelectEqChannelTab(ChannelViewModel? channel)
    {
        if (channel != null)
        {
            OpenChannelEqStudio(channel);
        }
    }

    partial void OnSelectedEqProfileChanged(EqProfile? value)
    {
        OnPropertyChanged(nameof(CanDeleteSelectedEqProfile));
        if (_suppressEqProfileSelection || value == null || EqTargetChannel == null)
            return;

        _suppressEqBandSync = true;
        ActiveEqPreampDb = value.PreampDb;
        EqTargetChannel.EqPreampDb = value.PreampDb;

        for (int i = 0; i < 10; i++)
        {
            double g = i < value.GainsDb.Length ? value.GainsDb[i] : 0.0;
            EqTargetChannel.EqGainsDb[i] = g;
            ActiveEqBands[i].SetGainSilent(g);
        }

        EqTargetChannel.ActiveEqProfileName = value.Name;
        if (!value.IsBuiltIn)
        {
            CustomEqProfileNameInput = value.Name;
        }
        _suppressEqBandSync = false;

        EqTargetChannel.RefreshMiniEqCurve();
        RefreshLargeEqCurve();
        ApplyAllChannelVolumesToAudioEngine();
        SaveChannelStateToConfig();
        StatusMessage = $"APPLIED EQ PROFILE '{value.Name}' TO {EqTargetChannel.Name}";
    }

    partial void OnActiveEqPreampDbChanged(double value)
    {
        OnPropertyChanged(nameof(ActiveEqPreampText));
        if (_suppressEqBandSync || EqTargetChannel == null)
            return;

        EqTargetChannel.EqPreampDb = Math.Clamp(value, -12.0, 12.0);
        EqTargetChannel.RefreshMiniEqCurve();
        RefreshLargeEqCurve();
        ApplyActiveEqTargetUpdates();
        SaveChannelStateToConfig();
    }

    private void OnActiveEqBandGainChanged(EqBandViewModel band)
    {
        if (_suppressEqBandSync || EqTargetChannel == null)
            return;

        EqTargetChannel.EqGainsDb[band.BandIndex] = band.GainDb;

        if (!EqTargetChannel.ActiveEqProfileName.EndsWith("*"))
        {
            EqTargetChannel.ActiveEqProfileName = $"{EqTargetChannel.ActiveEqProfileName} *";
        }

        EqTargetChannel.RefreshMiniEqCurve();
        RefreshLargeEqCurve();
        ApplyActiveEqTargetUpdates();
        SaveChannelStateToConfig();
    }

    private void ApplyActiveEqTargetUpdates()
    {
        if (EqTargetChannel == null) return;

        if (EqTargetChannel != MicChannel)
        {
            ApplyChannelVolumesToAudioEngine(EqTargetChannel, _lastAnySolo);
        }

        if (IsAuditioningEq)
        {
            _dspEngine.UpdateEqParameters(
                EqTargetChannel.EqGainsDb,
                EqTargetChannel.EqPreampDb,
                EqTargetChannel.IsEqEnabled);
        }

        if (IsMicMonitoring && MicChannel != null && EqTargetChannel == MicChannel)
        {
            _dspEngine.UpdateMicEqParameters(
                MicChannel.EqGainsDb,
                MicChannel.EqPreampDb,
                MicChannel.IsEqEnabled);
        }
    }

    public void RefreshLargeEqCurve()
    {
        if (EqTargetChannel == null) return;
        var (curve, fill) = EqDspService.ComputeGraphPoints(
            740.0,
            150.0,
            EqTargetChannel.EqGainsDb,
            EqTargetChannel.EqPreampDb,
            EqTargetChannel.IsEqEnabled,
            110);
        LargeEqCurvePoints = curve;
        LargeEqFillPoints = fill;
    }

    [RelayCommand]
    private void ResetActiveChannelEqToFlat()
    {
        if (EqTargetChannel == null) return;
        var flat = AvailableEqProfiles.FirstOrDefault(p => p.Name == "FLAT");
        if (flat != null)
        {
            SelectedEqProfile = null;
            SelectedEqProfile = flat;
        }
    }

    [RelayCommand]
    private void SaveCurrentEqProfile()
    {
        if (EqTargetChannel == null) return;

        string cleanName = string.IsNullOrWhiteSpace(CustomEqProfileNameInput)
            ? $"{EqTargetChannel.Name} CUSTOM"
            : CustomEqProfileNameInput.Trim().ToUpperInvariant();

        // Prevent overwriting built-in presets with the exact same name
        if (AvailableEqProfiles.Any(p => p.IsBuiltIn && p.Name.Equals(cleanName, StringComparison.OrdinalIgnoreCase)))
        {
            cleanName = $"{cleanName} (USER)";
            CustomEqProfileNameInput = cleanName;
        }

        var existingCustom = _config.CustomEqProfiles.FirstOrDefault(p =>
            p.Name.Equals(cleanName, StringComparison.OrdinalIgnoreCase));

        if (existingCustom != null)
        {
            existingCustom.PreampDb = EqTargetChannel.EqPreampDb;
            existingCustom.GainsDb = (double[])EqTargetChannel.EqGainsDb.Clone();
        }
        else
        {
            existingCustom = new EqProfile
            {
                Name = cleanName,
                Category = "Custom",
                IsBuiltIn = false,
                PreampDb = EqTargetChannel.EqPreampDb,
                GainsDb = (double[])EqTargetChannel.EqGainsDb.Clone()
            };
            _config.CustomEqProfiles.Add(existingCustom);
        }

        var existingInDropdown = AvailableEqProfiles.FirstOrDefault(p =>
            !p.IsBuiltIn && p.Name.Equals(cleanName, StringComparison.OrdinalIgnoreCase));
        if (existingInDropdown != null)
        {
            existingInDropdown.PreampDb = existingCustom.PreampDb;
            existingInDropdown.GainsDb = (double[])existingCustom.GainsDb.Clone();
        }
        else
        {
            AvailableEqProfiles.Add(existingCustom);
            existingInDropdown = existingCustom;
        }

        _configService.SaveEqProfileToFile(existingCustom);
        EqTargetChannel.ActiveEqProfileName = cleanName;

        _suppressEqProfileSelection = true;
        SelectedEqProfile = existingInDropdown;
        _suppressEqProfileSelection = false;

        OnPropertyChanged(nameof(CanDeleteSelectedEqProfile));
        SaveChannelStateToConfig();
        StatusMessage = $"SAVED EQ PROFILE '{cleanName}' TO DISK";
    }

    [RelayCommand]
    private void DeleteSelectedEqProfile()
    {
        if (SelectedEqProfile == null || SelectedEqProfile.IsBuiltIn)
            return;

        string deletedName = SelectedEqProfile.Name;
        _config.CustomEqProfiles.RemoveAll(p => p.Name.Equals(deletedName, StringComparison.OrdinalIgnoreCase));
        _configService.DeleteEqProfileFile(deletedName);
        AvailableEqProfiles.Remove(SelectedEqProfile);

        var flat = AvailableEqProfiles.FirstOrDefault(p => p.Name == "FLAT");
        SelectedEqProfile = flat;
        SaveChannelStateToConfig();
        StatusMessage = $"DELETED CUSTOM EQ PROFILE: {deletedName}";
    }

    [RelayCommand]
    private void OpenEqProfilesFolder()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _configService.ProfilesDirectory,
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    // ==================== GENERAL MIXER COMMANDS ====================

    [RelayCommand]
    private void ResetChatMix()
    {
        ChatMixBalance = 0;
        StatusMessage = "CHATMIX: BALANCED";
    }

    [RelayCommand]
    private void SetChatMixPreset(string? balanceParam)
    {
        if (double.TryParse(balanceParam, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double target))
        {
            ChatMixBalance = Math.Clamp(target, -100.0, 100.0);
        }
    }

    [RelayCommand]
    private void ToggleMasterMute()
    {
        IsMasterMuted = !IsMasterMuted;
    }

    [RelayCommand]
    private void RefreshDevicesAndSessions()
    {
        LoadDevices();
        LoadMicDevices();
        _audioService.RequestSessionRefresh();
        ScanAndRouteAudioSessions();
        StatusMessage = "AUDIO ENGINE REFRESHED";
    }

    [RelayCommand]
    private void OpenAddChannelPopup()
    {
        int customIndex = Channels.Count(c => !c.IsPreset);
        NewChannelName = $"CUSTOM {customIndex + 1}";
        NewChannelIconKey = CustomChannelIcons[customIndex % CustomChannelIcons.Length];
        IsAddChannelPopupOpen = true;
    }

    [RelayCommand]
    private void CancelAddChannel()
    {
        IsAddChannelPopupOpen = false;
    }

    [RelayCommand]
    private void SelectNewChannelIcon(string? iconKey)
    {
        if (!string.IsNullOrWhiteSpace(iconKey))
        {
            NewChannelIconKey = iconKey;
        }
    }

    [RelayCommand]
    private void ConfirmAddChannel()
    {
        string cleanName = string.IsNullOrWhiteSpace(NewChannelName)
            ? "CUSTOM"
            : NewChannelName.Trim().ToUpperInvariant();

        string id = $"custom_{Guid.NewGuid():N}"[..15];
        var cfg = new ChannelConfig
        {
            Id = id,
            Name = cleanName,
            IconGlyph = NewChannelIconKey,
            Volume = 1.0,
            IsPreset = false,
            IsEqEnabled = true,
            ActiveEqProfileName = "FLAT",
            EqPreampDb = 0.0,
            EqGainsDb = new double[10]
        };

        _config.Channels.Add(cfg);
        var vm = new ChannelViewModel(cfg, OnChannelStateChanged, OnDeleteChannelRequested, OpenChannelEqStudio);
        Channels.Add(vm);
        _configService.Save(_config);

        IsAddChannelPopupOpen = false;
        StatusMessage = $"ADDED CHANNEL: {cleanName}";
    }

    [RelayCommand]
    private void ClearAllSolos()
    {
        foreach (var ch in Channels)
        {
            ch.IsSolo = false;
        }
    }

    private void OnChannelStateChanged(ChannelViewModel channel)
    {
        if (EqTargetChannel == channel)
        {
            RefreshLargeEqCurve();
        }

        bool anySolo = Channels.Any(c => c.IsSolo);
        if (anySolo != _lastAnySolo)
        {
            _lastAnySolo = anySolo;
            ApplyAllChannelVolumesToAudioEngine();
        }
        else
        {
            ApplyChannelVolumesToAudioEngine(channel, anySolo);
            ScheduleApoSync();
        }

        SaveChannelStateToConfig();
    }

    private void OnDeleteChannelRequested(ChannelViewModel channelToDelete)
    {
        if (channelToDelete.IsPreset) return;

        var fallbackChannel = Channels.FirstOrDefault(c => c.Id == "game") ?? Channels.FirstOrDefault(c => c != channelToDelete);
        if (fallbackChannel != null)
        {
            var appsToMove = channelToDelete.Apps.ToList();
            foreach (var app in appsToMove)
            {
                MoveAppToChannelInternal(app, fallbackChannel.Id);
            }
        }

        if (EqTargetChannel == channelToDelete)
        {
            IsEqEditorOpen = false;
            EqTargetChannel = null;
        }

        Channels.Remove(channelToDelete);
        SaveChannelStateToConfig();
        ApplyAllChannelVolumesToAudioEngine();
        StatusMessage = $"DELETED CHANNEL: {channelToDelete.Name}";
    }

    private void OnAppVolumeOrMuteChanged(AppSessionViewModel app)
    {
        _config.AppRelativeVolumes[app.ProcessKey] = Math.Clamp(app.RelativeVolume / 100.0, 0.0, 1.0);
        ApplySingleAppVolumeToAudioEngine(app, syncApo: false);
        SchedulePersist();
    }

    public void MoveAppToChannelInternal(AppSessionViewModel app, string targetChannelId)
    {
        var sourceChannel = Channels.FirstOrDefault(c => c.Id.Equals(app.ChannelId, StringComparison.OrdinalIgnoreCase));
        var targetChannel = Channels.FirstOrDefault(c => c.Id.Equals(targetChannelId, StringComparison.OrdinalIgnoreCase));
        if (targetChannel == null || sourceChannel == targetChannel) return;

        sourceChannel?.Apps.Remove(app);
        app.ChannelId = targetChannel.Id;

        if (!targetChannel.Apps.Contains(app))
        {
            targetChannel.Apps.Add(app);
        }

        _config.AppChannelAssignments[app.ProcessKey] = targetChannel.Id;
        SchedulePersist();

        ApplySingleAppVolumeToAudioEngine(app);
        StatusMessage = $"ROUTED {app.DisplayName.ToUpperInvariant()} → {targetChannel.Name}";
    }

    private void OnMeterTimerTick(object? sender, EventArgs e)
    {
        if (Interlocked.CompareExchange(ref _pollInFlight, 1, 0) != 0)
            return;

        Task.Run(() =>
        {
            try
            {
                var masterState = _audioService.GetMasterState();
                var micState = _audioService.GetMicState();
                var activeSessions = _audioService.GetActiveSessions();

                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        ApplyPolledAudioState(masterState, micState, activeSessions);
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _pollInFlight, 0);
                    }
                }, DispatcherPriority.Background);
            }
            catch
            {
                Interlocked.Exchange(ref _pollInFlight, 0);
            }
        });
    }

    private void ApplyPolledAudioState(
        (float volume, bool isMuted, float peak) masterState,
        (float volume, bool isMuted, float peak) micState,
        List<AudioSessionData> activeSessions)
    {
        _tickCount++;

        var (mVol, mMuted, mPeak) = masterState;
        MasterPeakLevel = Math.Clamp(mPeak * 100.0, 0.0, 100.0);

        if (Math.Abs(MasterVolumePercent - mVol * 100.0) > 1.0 || IsMasterMuted != mMuted)
        {
            _suppressMasterUpdate = true;
            MasterVolumePercent = Math.Clamp(mVol * 100.0, 0.0, 100.0);
            IsMasterMuted = mMuted;
            _suppressMasterUpdate = false;
        }

        var (micVol, micMuted, micPeak) = micState;
        double targetMicPeak = micMuted ? 0.0 : Math.Clamp(micPeak * 100.0, 0.0, 100.0);
        MicChannel.PeakLevel = targetMicPeak >= MicChannel.PeakLevel
            ? targetMicPeak
            : Math.Max(0.0, MicChannel.PeakLevel * 0.78);

        if (Math.Abs(MicChannel.VolumePercent - micVol * 100.0) > 1.0 || MicChannel.IsMuted != micMuted)
        {
            _suppressMicUpdate = true;
            MicChannel.VolumePercent = Math.Clamp(micVol * 100.0, 0.0, 100.0);
            MicChannel.IsMuted = micMuted;
            _suppressMicUpdate = false;
        }

        RouteAndAnimateSessions(activeSessions);
    }

    private void ScanAndRouteAudioSessions()
    {
        var activeSessions = _audioService.GetActiveSessions();
        RouteAndAnimateSessions(activeSessions);
    }

    private void RouteAndAnimateSessions(List<AudioSessionData> activeSessions)
    {
        var activeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var availableChannelIds = Channels.Select(c => c.Id).ToList();
        if (availableChannelIds.Count == 0) return;

        foreach (var session in activeSessions)
        {
            activeKeys.Add(session.ProcessKey);

            if (!_allSessionsByKey.TryGetValue(session.ProcessKey, out var appVm))
            {
                if (!_config.AppChannelAssignments.TryGetValue(session.ProcessKey, out var assignedChannelId)
                    || !availableChannelIds.Any(id => id.Equals(assignedChannelId, StringComparison.OrdinalIgnoreCase)))
                {
                    assignedChannelId = ConfigService.GuessChannelForProcess(session.ProcessKey, availableChannelIds);
                    _config.AppChannelAssignments[session.ProcessKey] = assignedChannelId;
                }

                double relVol = _config.AppRelativeVolumes.TryGetValue(session.ProcessKey, out var savedRel)
                    ? savedRel
                    : 1.0;

                var icon = _audioService.GetAppIcon(session.ProcessKey, session.ExecutablePath);
                appVm = new AppSessionViewModel(
                    session.ProcessKey,
                    session.ProcessId,
                    session.DisplayName,
                    icon,
                    assignedChannelId,
                    relVol,
                    OnAppVolumeOrMuteChanged,
                    MoveAppToChannelInternal);

                var channelVm = Channels.FirstOrDefault(c => c.Id.Equals(assignedChannelId, StringComparison.OrdinalIgnoreCase))
                                ?? Channels[0];

                appVm.ChannelId = channelVm.Id;
                channelVm.Apps.Add(appVm);
                _allSessionsByKey[session.ProcessKey] = appVm;

                ApplySingleAppVolumeToAudioEngine(appVm);
            }
            else
            {
                appVm.ProcessId = session.ProcessId;
            }

            double targetPeak = Math.Clamp(session.CurrentPeak * 100.0, 0.0, 100.0);
            if (targetPeak >= appVm.PeakLevel)
            {
                appVm.PeakLevel = targetPeak;
            }
            else
            {
                appVm.PeakLevel = Math.Max(0.0, appVm.PeakLevel * 0.78);
            }
            appVm.AdvanceWaveAnimation();
        }

        if (_tickCount % 20 == 0)
        {
            var expiredKeys = _allSessionsByKey.Keys.Where(k => !activeKeys.Contains(k)).ToList();
            foreach (var expired in expiredKeys)
            {
                if (_allSessionsByKey.TryGetValue(expired, out var vm))
                {
                    var ch = Channels.FirstOrDefault(c => c.Id.Equals(vm.ChannelId, StringComparison.OrdinalIgnoreCase));
                    ch?.Apps.Remove(vm);
                    _allSessionsByKey.Remove(expired);
                }
            }
        }

        bool anySolo = Channels.Any(c => c.IsSolo);
        foreach (var ch in Channels)
        {
            ch.IsEffectivelyMuted = ch.IsMuted || (anySolo && !ch.IsSolo);
            if (ch.Apps.Count == 0 || ch.IsEffectivelyMuted)
            {
                ch.PeakLevel = Math.Max(0.0, ch.PeakLevel * 0.75);
            }
            else
            {
                double maxAppPeak = ch.Apps.Max(a => a.IsMuted ? 0.0 : a.PeakLevel);
                double scaledByFader = maxAppPeak * (ch.VolumePercent / 100.0);
                ch.PeakLevel = scaledByFader >= ch.PeakLevel
                    ? scaledByFader
                    : Math.Max(0.0, ch.PeakLevel * 0.78);
            }
        }

        var gameCh = Channels.FirstOrDefault(c => c.Id.Equals("game", StringComparison.OrdinalIgnoreCase));
        var chatCh = Channels.FirstOrDefault(c => c.Id.Equals("chat", StringComparison.OrdinalIgnoreCase));
        GameChannelPeakLevel = gameCh?.PeakLevel ?? 0.0;
        ChatChannelPeakLevel = chatCh?.PeakLevel ?? 0.0;

        TotalRoutedAppsCount = _allSessionsByKey.Count;
    }

    private void ApplyChannelVolumesToAudioEngine(ChannelViewModel ch, bool anySolo)
    {
        ch.IsEffectivelyMuted = ch.IsMuted || (anySolo && !ch.IsSolo);
        double eqTrimScalar = EqDspService.ComputeEffectiveEqTrimScalar(
            ch.EqGainsDb,
            ch.EqPreampDb,
            ch.IsEqEnabled);

        foreach (var app in ch.Apps)
        {
            ApplySingleAppVolumeToAudioEngine(app, ch, anySolo, false, eqTrimScalar);
        }
    }

    private void ApplyAllChannelVolumesToAudioEngine()
    {
        bool anySolo = Channels.Any(c => c.IsSolo);
        _lastAnySolo = anySolo;
        foreach (var ch in Channels)
        {
            ApplyChannelVolumesToAudioEngine(ch, anySolo);
        }
        RefreshAllStatusBrushes();
        ScheduleApoSync();
    }

    private DispatcherTimer? _persistTimer;
    private bool _pendingSave;
    private bool _pendingChannelConfigSync;
    private bool _pendingApoSync;

    private void SchedulePersist()
    {
        _pendingSave = true;
        EnsurePersistTimer();
    }

    private void ScheduleApoSync()
    {
        _pendingApoSync = true;
        EnsurePersistTimer();
    }

    private void EnsurePersistTimer()
    {
        if (_persistTimer == null)
        {
            _persistTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _persistTimer.Tick += (_, _) => FlushPersist();
        }
        // Reset timer on each change so disk/APO writes happen only after slider dragging settles
        _persistTimer.Stop();
        _persistTimer.Start();
    }

    private void FlushPersist()
    {
        _persistTimer?.Stop();
        if (_pendingChannelConfigSync)
        {
            _pendingChannelConfigSync = false;
            _config.Channels = Channels.Select(c => c.ToConfig()).ToList();
            if (MicChannel != null)
            {
                _config.MicVolume = MicChannel.VolumePercent / 100.0;
                _config.MicMuted = MicChannel.IsMuted;
                _config.IsMicEqEnabled = MicChannel.IsEqEnabled;
                _config.ActiveMicEqProfileName = MicChannel.ActiveEqProfileName;
                _config.MicEqPreampDb = MicChannel.EqPreampDb;
                _config.MicEqGainsDb = (double[])MicChannel.EqGainsDb.Clone();
            }
        }
        if (_pendingSave)
        {
            _pendingSave = false;
            _configService.Save(_config);
        }
        if (_pendingApoSync)
        {
            _pendingApoSync = false;
            SyncHardwareEqualizerApo();
        }
    }

    private void ApplySingleAppVolumeToAudioEngine(
        AppSessionViewModel app,
        ChannelViewModel? channel = null,
        bool? anySoloOverride = null,
        bool syncApo = true,
        double? eqTrimScalarOverride = null)
    {
        channel ??= Channels.FirstOrDefault(c => c.Id.Equals(app.ChannelId, StringComparison.OrdinalIgnoreCase));
        if (channel == null) return;

        bool anySolo = anySoloOverride ?? Channels.Any(c => c.IsSolo);
        bool channelMuted = channel.IsMuted || (anySolo && !channel.IsSolo);
        bool effectiveMute = channelMuted || app.IsMuted;

        double chatMixMultiplier = 1.0;
        if (channel.Id.Equals("game", StringComparison.OrdinalIgnoreCase) && ChatMixBalance > 0)
        {
            chatMixMultiplier = Math.Clamp(1.0 - (ChatMixBalance / 100.0), 0.0, 1.0);
        }
        else if (channel.Id.Equals("chat", StringComparison.OrdinalIgnoreCase) && ChatMixBalance < 0)
        {
            chatMixMultiplier = Math.Clamp(1.0 - (Math.Abs(ChatMixBalance) / 100.0), 0.0, 1.0);
        }

        double eqTrimScalar = eqTrimScalarOverride ?? EqDspService.ComputeEffectiveEqTrimScalar(
            channel.EqGainsDb,
            channel.EqPreampDb,
            channel.IsEqEnabled);

        double effectiveVol = (channel.VolumePercent / 100.0)
                              * (app.RelativeVolume / 100.0)
                              * chatMixMultiplier
                              * eqTrimScalar;

        _audioService.ApplyProcessVolume(app.ProcessKey, (float)effectiveVol, effectiveMute);
        if (syncApo) ScheduleApoSync();
    }

    private void SaveChannelStateToConfig()
    {
        _pendingChannelConfigSync = true;
        SchedulePersist();
        ScheduleApoSync();
    }

    public void Dispose()
    {
        _meterTimer.Stop();
        FlushPersist();
        _dspEngine.Dispose();
        _audioService.Dispose();
    }
}
