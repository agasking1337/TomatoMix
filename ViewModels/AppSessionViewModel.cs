using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TomatoMix.Theme;

namespace TomatoMix.ViewModels;

public partial class AppSessionViewModel : ObservableObject
{
    private readonly Action<AppSessionViewModel> _onVolumeOrMuteChanged;
    private readonly Action<AppSessionViewModel, string> _onMoveToChannelRequested;
    private double _wavePhase;
    private bool _isWaveFlat;
    private StatusLevel? _lastStatusLevel;

    public string ProcessKey { get; }
    public int ProcessId { get; set; }

    [ObservableProperty]
    private string _displayName;

    [ObservableProperty]
    private Bitmap? _appIcon;

    [ObservableProperty]
    private double _relativeVolume = 100.0; // 0..100%

    [ObservableProperty]
    private bool _isMuted;

    [ObservableProperty]
    private double _peakLevel; // 0..100 for UI progress bar

    [ObservableProperty]
    private string _channelId;

    [ObservableProperty]
    private IBrush _statusBrush = StatusMapping.ResolveStatusBrush(StatusLevel.Ok);

    [ObservableProperty]
    private Points _waveCurvePoints = new();

    [ObservableProperty]
    private Points _waveFillPoints = new();

    [ObservableProperty]
    private double _eqBar1Height = 2.0;

    [ObservableProperty]
    private double _eqBar2Height = 2.0;

    [ObservableProperty]
    private double _eqBar3Height = 2.0;

    [ObservableProperty]
    private double _eqBar4Height = 2.0;

    public StatusLevel SessionStatus => IsMuted
        ? StatusLevel.Critical
        : PeakLevel > 1.0
            ? StatusLevel.Ok
            : StatusLevel.Unknown;

    public string VolumePercentText => $"{Math.Round(RelativeVolume):0}%";

    public string Initials => string.IsNullOrWhiteSpace(DisplayName)
        ? "?"
        : DisplayName.Substring(0, Math.Min(2, DisplayName.Length)).ToUpperInvariant();

    public AppSessionViewModel(
        string processKey,
        int processId,
        string displayName,
        Bitmap? appIcon,
        string channelId,
        double relativeVolumeScalar,
        Action<AppSessionViewModel> onVolumeOrMuteChanged,
        Action<AppSessionViewModel, string> onMoveToChannelRequested)
    {
        ProcessKey = processKey;
        ProcessId = processId;
        _displayName = displayName;
        _appIcon = appIcon;
        _channelId = channelId;
        _relativeVolume = Math.Clamp(relativeVolumeScalar * 100.0, 0.0, 100.0);
        _onVolumeOrMuteChanged = onVolumeOrMuteChanged;
        _onMoveToChannelRequested = onMoveToChannelRequested;
        SetFlatWave();
        RefreshStatusTheme();
    }

    private void SetFlatWave()
    {
        const double width = 180.0;
        const double midY = 11.0;
        const double bottomY = 22.0;

        EqBar1Height = 2.0;
        EqBar2Height = 2.0;
        EqBar3Height = 2.0;
        EqBar4Height = 2.0;

        var curve = new Points { new Point(0, midY), new Point(width, midY) };
        var fill = new Points
        {
            new Point(0, bottomY),
            new Point(0, midY),
            new Point(width, midY),
            new Point(width, bottomY)
        };

        WaveCurvePoints = curve;
        WaveFillPoints = fill;
        _isWaveFlat = true;
    }

    public void AdvanceWaveAnimation()
    {
        double activeLevel = IsMuted ? 0.0 : Math.Clamp(PeakLevel / 100.0, 0.0, 1.0);
        if (activeLevel < 0.015)
        {
            if (!_isWaveFlat)
            {
                SetFlatWave();
            }
            return;
        }

        _isWaveFlat = false;
        _wavePhase = (_wavePhase + 0.45 + activeLevel * 0.35) % (Math.PI * 200.0);

        EqBar1Height = 2.0 + 12.0 * activeLevel * (0.45 + 0.55 * Math.Abs(Math.Sin(_wavePhase * 1.15)));
        EqBar2Height = 2.0 + 12.0 * activeLevel * (0.55 + 0.45 * Math.Abs(Math.Sin(_wavePhase * 1.65 + 1.1)));
        EqBar3Height = 2.0 + 12.0 * activeLevel * (0.50 + 0.50 * Math.Abs(Math.Sin(_wavePhase * 1.35 + 2.3)));
        EqBar4Height = 2.0 + 12.0 * activeLevel * (0.40 + 0.60 * Math.Abs(Math.Sin(_wavePhase * 1.90 + 0.7)));

        const int steps = 28;
        const double width = 180.0;
        const double midY = 11.0;
        const double bottomY = 22.0;

        var curve = new Points();
        var fill = new Points { new Point(0, bottomY) };

        double volNorm = Math.Clamp(RelativeVolume / 100.0, 0.0, 1.0);
        double maxAmp = Math.Min(9.0, 2.0 + activeLevel * 7.5);
        for (int i = 0; i <= steps; i++)
        {
            double t = (double)i / steps;
            double x = t * width;
            double edgeEnvelope = Math.Sin(t * Math.PI);
            // Taper wave past the slider thumb position so the active volume region comes alive
            double sliderMask = t <= volNorm
                ? 1.0
                : Math.Max(0.12, Math.Exp(-(t - volNorm) * 8.0));
            double wave = Math.Sin(t * Math.PI * 4.5 - _wavePhase) * 0.62
                          + Math.Sin(t * Math.PI * 8.0 + _wavePhase * 1.35) * 0.38;
            double y = Math.Clamp(midY - wave * maxAmp * edgeEnvelope * sliderMask, 1.5, 20.5);
            var pt = new Point(x, y);
            curve.Add(pt);
            fill.Add(pt);
        }

        fill.Add(new Point(width, bottomY));
        WaveCurvePoints = curve;
        WaveFillPoints = fill;
    }

    public void RefreshStatusTheme(bool force = false)
    {
        var current = SessionStatus;
        if (!force && _lastStatusLevel == current)
            return;

        _lastStatusLevel = current;
        StatusBrush = StatusMapping.ResolveStatusBrush(current);
    }

    partial void OnRelativeVolumeChanged(double value)
    {
        OnPropertyChanged(nameof(VolumePercentText));
        _onVolumeOrMuteChanged(this);
    }

    partial void OnIsMutedChanged(bool value)
    {
        RefreshStatusTheme();
        _onVolumeOrMuteChanged(this);
    }

    partial void OnPeakLevelChanged(double value)
    {
        RefreshStatusTheme();
    }

    [RelayCommand]
    private void ToggleMute()
    {
        IsMuted = !IsMuted;
    }

    [RelayCommand]
    private void MoveToChannel(string? targetChannelId)
    {
        if (!string.IsNullOrWhiteSpace(targetChannelId) && !string.Equals(targetChannelId, ChannelId, StringComparison.OrdinalIgnoreCase))
        {
            _onMoveToChannelRequested(this, targetChannelId);
        }
    }
}
