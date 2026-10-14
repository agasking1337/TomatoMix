using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TomatoMix.ViewModels;

public partial class AppSessionViewModel : ObservableObject
{
    private readonly Action<AppSessionViewModel> _onVolumeOrMuteChanged;
    private readonly Action<AppSessionViewModel, string> _onMoveToChannelRequested;

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
    private IBrush _accentBrush = new SolidColorBrush(Color.Parse("#00E5FF"));

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
    }

    partial void OnRelativeVolumeChanged(double value)
    {
        OnPropertyChanged(nameof(VolumePercentText));
        _onVolumeOrMuteChanged(this);
    }

    partial void OnIsMutedChanged(bool value)
    {
        _onVolumeOrMuteChanged(this);
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
