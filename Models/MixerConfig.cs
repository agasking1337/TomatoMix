namespace TomatoMix.Models;

public class MixerConfig
{
    public bool IsDarkTheme { get; set; } = true;
    public string? SelectedDeviceId { get; set; }
    public string? SelectedMicDeviceId { get; set; }
    public double MasterVolume { get; set; } = 1.0;
    public bool MasterMuted { get; set; }
    public double MicVolume { get; set; } = 1.0;
    public bool MicMuted { get; set; }
    public bool IsMicEqEnabled { get; set; } = true;
    public string ActiveMicEqProfileName { get; set; } = "VOICE CLARITY (DISCORD / PODCAST)";
    public double MicEqPreampDb { get; set; } = 0.0;
    public double[] MicEqGainsDb { get; set; } = new double[10];
    public List<ChannelConfig> Channels { get; set; } = new();
    public List<EqProfile> CustomEqProfiles { get; set; } = new();
    public Dictionary<string, string> AppChannelAssignments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> AppRelativeVolumes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class ChannelConfig
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string IconGlyph { get; set; } = "settings";
    public double Volume { get; set; } = 1.0;
    public bool IsMuted { get; set; }
    public bool IsSolo { get; set; }
    public bool IsPreset { get; set; }

    // Per-Channel EQ Settings
    public bool IsEqEnabled { get; set; } = true;
    public string ActiveEqProfileName { get; set; } = "FLAT";
    public double EqPreampDb { get; set; } = 0.0;
    public double[] EqGainsDb { get; set; } = new double[10];
}

public class EqProfile
{
    public static readonly double[] StandardFrequencies =
    [
        31.0, 62.0, 125.0, 250.0, 500.0, 1000.0, 2000.0, 4000.0, 8000.0, 16000.0
    ];

    public static readonly string[] StandardFrequencyLabels =
    [
        "31 Hz", "62 Hz", "125 Hz", "250 Hz", "500 Hz", "1 kHz", "2 kHz", "4 kHz", "8 kHz", "16 kHz"
    ];

    public string Name { get; set; } = "FLAT";
    public string Category { get; set; } = "Custom";
    public bool IsBuiltIn { get; set; }
    public double PreampDb { get; set; }
    public double[] GainsDb { get; set; } = new double[10];

    public EqProfile Clone() => new()
    {
        Name = Name,
        Category = Category,
        IsBuiltIn = IsBuiltIn,
        PreampDb = PreampDb,
        GainsDb = GainsDb.Length == 10 ? (double[])GainsDb.Clone() : new double[10]
    };

    public override string ToString() => IsBuiltIn ? $"[PRESET] {Name}" : $"[USER] {Name}";
}

public class AudioDeviceInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }

    public override string ToString() => IsDefault ? $"{Name} (Default)" : Name;
}

public class AudioSessionData
{
    public int ProcessId { get; set; }
    public string ProcessKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? ExecutablePath { get; set; }
    public float CurrentPeak { get; set; }
    public float RawVolume { get; set; }
    public bool RawMute { get; set; }
    public bool IsSystemSounds { get; set; }
}
