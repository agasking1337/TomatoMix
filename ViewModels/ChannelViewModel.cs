using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TomatoMix.Models;
using TomatoMix.Services;

namespace TomatoMix.ViewModels;

public partial class ChannelViewModel : ObservableObject
{
    private readonly Action<ChannelViewModel> _onChannelStateChanged;
    private readonly Action<ChannelViewModel> _onDeleteChannelRequested;
    private readonly Action<ChannelViewModel>? _onOpenEqEditorRequested;

    public string Id { get; }
    public bool IsPreset { get; }
    public bool CanDelete => !IsPreset;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private bool _isEditingName;

    [ObservableProperty]
    private string _accentHex;

    [ObservableProperty]
    private IBrush _accentBrush;

    [ObservableProperty]
    private IBrush _accentGlowBrush;

    [ObservableProperty]
    private string _iconGlyph;

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
    private double _peakBarHeight; // 0..220 px for vertical meter

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
        _accentHex = config.AccentHex;
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

        var color = Color.TryParse(config.AccentHex, out var c) ? c : Color.Parse("#00E5FF");
        _accentBrush = new SolidColorBrush(color);
        _accentGlowBrush = new SolidColorBrush(Color.FromArgb(45, color.R, color.G, color.B));
        _channelIconGeometry = ResolveChannelIconGeometry(config.Id, config.IconGlyph);

        RefreshMiniEqCurve();

        Apps.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(AppCount));
            OnPropertyChanged(nameof(HasNoApps));
        };
    }

    public void RefreshMiniEqCurve()
    {
        var (curve, fill) = EqDspService.ComputeGraphPoints(220.0, 36.0, EqGainsDb, EqPreampDb, IsEqEnabled, 48);
        MiniEqCurvePoints = curve;
        MiniEqFillPoints = fill;
        OnPropertyChanged(nameof(EqStatusBadgeText));
    }

    partial void OnAccentHexChanged(string value)
    {
        if (Color.TryParse(value, out var c))
        {
            AccentBrush = new SolidColorBrush(c);
            AccentGlowBrush = new SolidColorBrush(Color.FromArgb(45, c.R, c.G, c.B));
            foreach (var app in Apps)
            {
                app.AccentBrush = AccentBrush;
            }
        }
    }

    partial void OnVolumePercentChanged(double value)
    {
        OnPropertyChanged(nameof(VolumePercentText));
        _onChannelStateChanged(this);
    }

    partial void OnIsMutedChanged(bool value)
    {
        OnPropertyChanged(nameof(VolumePercentText));
        _onChannelStateChanged(this);
    }

    partial void OnIsSoloChanged(bool value)
    {
        _onChannelStateChanged(this);
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

    private static StreamGeometry ResolveChannelIconGeometry(string channelId, string iconKey)
    {
        string key = (channelId ?? iconKey ?? "").ToLowerInvariant();
        string pathData = key switch
        {
            // Gamepad SVG
            "game" => "M6,9 H8 V11 H10 V13 H8 V15 H6 V13 H4 V11 H6 Z M15.5,10.5 A1,1 0 1,0 15.5,12.5 A1,1 0 1,0 15.5,10.5 Z M18,12.5 A1,1 0 1,0 18,14.5 A1,1 0 1,0 18,12.5 Z M6,6 H18 A5,5 0 0,1 23,11 V14 A4,4 0 0,1 19,18 C17.5,18 16.3,17.2 15.3,16 H8.7 C7.7,17.2 6.5,18 5,18 A4,4 0 0,1 1,14 V11 A5,5 0 0,1 6,6 Z",
            // Microphone / Chat SVG
            "chat" => "M12,2 A3,3 0 0,1 15,5 V11 A3,3 0 0,1 9,11 V5 A3,3 0 0,1 12,2 Z M19,10 V11 A7,7 0 0,1 13,17.92 V20 H16 V22 H8 V20 H11 V17.92 A7,7 0 0,1 5,11 V10 H7 V11 A5,5 0 0,0 17,11 V10 H19 Z",
            // Music Note SVG
            "media" => "M12,3 V13.55 C11.41,13.21 10.73,13 10,13 C7.79,13 6,14.79 6,17 C6,19.21 7.79,21 10,21 C12.21,21 14,19.21 14,17 V7 H18 V3 H12 Z",
            // Lightning / Aux SVG
            "aux" => "M13,2 L3,14 H12 L11,22 L21,10 H12 L13,2 Z",
            // Sliders / Custom Channel SVG
            _ => "M3,17 V19 H9 V17 H3 Z M3,5 V7 H13 V5 H3 Z M13,21 V19 H21 V17 H13 V15 H11 V21 H13 Z M7,9 V11 H3 V13 H7 V15 H9 V9 H7 Z M21,13 V11 H11 V13 H21 Z M15,9 H17 V7 H21 V5 H17 V3 H15 V9 Z"
        };

        return StreamGeometry.Parse(pathData);
    }

    public ChannelConfig ToConfig()
    {
        return new ChannelConfig
        {
            Id = Id,
            Name = string.IsNullOrWhiteSpace(Name) ? "CHANNEL" : Name.Trim().ToUpperInvariant(),
            AccentHex = AccentHex,
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
