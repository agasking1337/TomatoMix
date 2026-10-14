using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TomatoMix.Models;
using TomatoMix.Services;
using TomatoMix.Theme;

namespace TomatoMix.ViewModels;

public partial class ChannelViewModel : ObservableObject
{
    private readonly Action<ChannelViewModel> _onChannelStateChanged;
    private readonly Action<ChannelViewModel> _onDeleteChannelRequested;
    private readonly Action<ChannelViewModel>? _onOpenEqEditorRequested;
    private StatusLevel? _lastStatusLevel;

    public string Id { get; }
    public bool IsPreset { get; }
    public bool CanDelete => !IsPreset;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private bool _isEditingName;

    [ObservableProperty]
    private string _iconGlyph;

    [ObservableProperty]
    private IconName _channelIcon;

    [ObservableProperty]
    private StreamGeometry _channelIconGeometry;

    [ObservableProperty]
    private double _volumePercent; // 0..100

    [ObservableProperty]
    private bool _isMuted;

    [ObservableProperty]
    private bool _isSolo;

    [ObservableProperty]
    private bool _isEffectivelyMuted;

    [ObservableProperty]
    private double _peakLevel; // 0..100

    [ObservableProperty]
    private double _peakBarHeight; // 0..185 px for vertical meter

    [ObservableProperty]
    private IBrush _statusBrush = StatusMapping.ResolveStatusBrush(StatusLevel.Ok);

    // Per-Channel Equalizer Properties
    [ObservableProperty]
    private bool _isEqEnabled;

    [ObservableProperty]
    private string _activeEqProfileName;

    [ObservableProperty]
    private double _eqPreampDb;

    [ObservableProperty]
    private Points _miniEqCurvePoints = new();

    [ObservableProperty]
    private Points _miniEqFillPoints = new();

    public double[] EqGainsDb { get; set; }

    public ObservableCollection<AppSessionViewModel> Apps { get; } = new();

    public StatusLevel ChannelStatus => IsMuted
        ? StatusLevel.Critical
        : IsSolo || IsEffectivelyMuted
            ? StatusLevel.Warning
            : VolumePercent <= 0.01
                ? StatusLevel.Offline
                : StatusLevel.Ok;

    public string VolumePercentText => IsMuted ? "MUTED" : $"{Math.Round(VolumePercent):0}%";
    public string EqStatusBadgeText => IsEqEnabled ? ActiveEqProfileName : "EQ OFF";
    public int AppCount => Apps.Count;
    public bool HasNoApps => Apps.Count == 0;

    public ChannelViewModel(
        ChannelConfig config,
        Action<ChannelViewModel> onChannelStateChanged,
        Action<ChannelViewModel> onDeleteChannelRequested,
        Action<ChannelViewModel>? onOpenEqEditorRequested = null)
    {
        Id = config.Id;
        IsPreset = config.IsPreset;
        _name = config.Name;
        _iconGlyph = config.IconGlyph;
        _volumePercent = Math.Clamp(config.Volume * 100.0, 0.0, 100.0);
        _isMuted = config.IsMuted;
        _isSolo = config.IsSolo;
        _isEqEnabled = config.IsEqEnabled;
        _activeEqProfileName = string.IsNullOrWhiteSpace(config.ActiveEqProfileName) ? "FLAT" : config.ActiveEqProfileName;
        _eqPreampDb = Math.Clamp(config.EqPreampDb, -12.0, 12.0);
        EqGainsDb = config.EqGainsDb is { Length: 10 } ? (double[])config.EqGainsDb.Clone() : new double[10];

        _onChannelStateChanged = onChannelStateChanged;
        _onDeleteChannelRequested = onDeleteChannelRequested;
        _onOpenEqEditorRequested = onOpenEqEditorRequested;

        _channelIcon = IconRegistry.ResolveChannelIcon(config.Id, config.IconGlyph);
        _channelIconGeometry = IconRegistry.GetGeometry(_channelIcon);

        RefreshMiniEqCurve();
        RefreshStatusTheme();

        Apps.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(AppCount));
            OnPropertyChanged(nameof(HasNoApps));
        };
    }

    public void RefreshStatusTheme(bool force = false)
    {
        var current = ChannelStatus;
        if (force || _lastStatusLevel != current)
        {
            _lastStatusLevel = current;
            StatusBrush = StatusMapping.ResolveStatusBrush(current);
        }

        if (force)
        {
            foreach (var app in Apps)
            {
                app.RefreshStatusTheme(true);
            }
        }
    }

    public void RefreshMiniEqCurve()
    {
        var (curve, fill) = EqDspService.ComputeGraphPoints(220.0, 36.0, EqGainsDb, EqPreampDb, IsEqEnabled, 48);
        MiniEqCurvePoints = curve;
        MiniEqFillPoints = fill;
        OnPropertyChanged(nameof(EqStatusBadgeText));
    }

    partial void OnIconGlyphChanged(string value)
    {
        ChannelIcon = IconRegistry.ResolveChannelIcon(Id, value);
        ChannelIconGeometry = IconRegistry.GetGeometry(ChannelIcon);
    }

    partial void OnVolumePercentChanged(double value)
    {
        OnPropertyChanged(nameof(VolumePercentText));
        RefreshStatusTheme();
        _onChannelStateChanged(this);
    }

    partial void OnIsMutedChanged(bool value)
    {
        OnPropertyChanged(nameof(VolumePercentText));
        RefreshStatusTheme();
        _onChannelStateChanged(this);
    }

    partial void OnIsSoloChanged(bool value)
    {
        RefreshStatusTheme();
        _onChannelStateChanged(this);
    }

    partial void OnIsEffectivelyMutedChanged(bool value)
    {
        RefreshStatusTheme();
    }

    partial void OnNameChanged(string value)
    {
        _onChannelStateChanged(this);
    }

    partial void OnIsEqEnabledChanged(bool value)
    {
        RefreshMiniEqCurve();
        _onChannelStateChanged(this);
    }

    partial void OnActiveEqProfileNameChanged(string value)
    {
        OnPropertyChanged(nameof(EqStatusBadgeText));
    }

    partial void OnPeakLevelChanged(double value)
    {
        PeakBarHeight = Math.Clamp(value / 100.0 * 185.0, 0.0, 185.0);
    }

    [RelayCommand]
    private void ToggleMute()
    {
        IsMuted = !IsMuted;
    }

    [RelayCommand]
    private void ToggleSolo()
    {
        IsSolo = !IsSolo;
    }

    [RelayCommand]
    private void ToggleEqEnabled()
    {
        IsEqEnabled = !IsEqEnabled;
    }

    [RelayCommand]
    private void OpenEqEditor()
    {
        _onOpenEqEditorRequested?.Invoke(this);
    }

    [RelayCommand]
    private void ToggleEditName()
    {
        IsEditingName = !IsEditingName;
    }

    [RelayCommand]
    private void DeleteChannel()
    {
        if (CanDelete)
        {
            _onDeleteChannelRequested(this);
        }
    }

    public ChannelConfig ToConfig()
    {
        return new ChannelConfig
        {
            Id = Id,
            Name = string.IsNullOrWhiteSpace(Name) ? "CHANNEL" : Name.Trim().ToUpperInvariant(),
            IconGlyph = IconGlyph,
            Volume = Math.Clamp(VolumePercent / 100.0, 0.0, 1.0),
            IsMuted = IsMuted,
            IsSolo = IsSolo,
            IsPreset = IsPreset,
            IsEqEnabled = IsEqEnabled,
            ActiveEqProfileName = ActiveEqProfileName,
            EqPreampDb = EqPreampDb,
            EqGainsDb = (double[])EqGainsDb.Clone()
        };
    }
}
