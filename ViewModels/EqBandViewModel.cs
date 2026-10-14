using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TomatoMix.ViewModels;

public partial class EqBandViewModel : ObservableObject
{
    private readonly Action<EqBandViewModel> _onGainChanged;
    private bool _suppressNotification;

    public int BandIndex { get; }
    public double FrequencyHz { get; }
    public string FrequencyLabel { get; }

    [ObservableProperty]
    private double _gainDb; // -12.0 .. +12.0 dB

    public string GainText => GainDb >= 0 ? $"+{GainDb:0.0} dB" : $"{GainDb:0.0} dB";

    public EqBandViewModel(int bandIndex, double frequencyHz, string frequencyLabel, double initialGainDb, Action<EqBandViewModel> onGainChanged)
    {
        BandIndex = bandIndex;
        FrequencyHz = frequencyHz;
        FrequencyLabel = frequencyLabel;
        _gainDb = Math.Clamp(initialGainDb, -12.0, 12.0);
        _onGainChanged = onGainChanged;
    }

    partial void OnGainDbChanged(double value)
    {
        OnPropertyChanged(nameof(GainText));
        if (!_suppressNotification)
        {
            _onGainChanged(this);
        }
    }

    public void SetGainSilent(double gainDb)
    {
        _suppressNotification = true;
        GainDb = Math.Clamp(gainDb, -12.0, 12.0);
        _suppressNotification = false;
    }

    [RelayCommand]
    private void ResetBand()
    {
        GainDb = 0.0;
    }
}
