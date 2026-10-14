using System.Text.Json;
using TomatoMix.Models;

namespace TomatoMix.Services;

public class ConfigService
{
    private readonly string _configPath;
    private readonly string _profilesDir;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public ConfigService()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string dir = Path.Combine(appData, "TomatoMix");
        _profilesDir = Path.Combine(dir, "EqProfiles");
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(_profilesDir);
        _configPath = Path.Combine(dir, "mixer_config.json");
    }

    public string ProfilesDirectory => _profilesDir;

    public MixerConfig Load()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                string json = File.ReadAllText(_configPath);
                var cfg = JsonSerializer.Deserialize<MixerConfig>(json, JsonOptions);
                if (cfg != null && cfg.Channels.Count > 0)
                {
                    foreach (var ch in cfg.Channels)
                    {
                        if (ch.EqGainsDb == null || ch.EqGainsDb.Length != 10)
                        {
                            ch.EqGainsDb = new double[10];
                        }
                        if (string.IsNullOrWhiteSpace(ch.ActiveEqProfileName))
                        {
                            ch.ActiveEqProfileName = "FLAT";
                        }
                    }

                    if (cfg.MicEqGainsDb == null || cfg.MicEqGainsDb.Length != 10)
                    {
                        cfg.MicEqGainsDb = new double[10];
                    }
                    if (string.IsNullOrWhiteSpace(cfg.ActiveMicEqProfileName))
                    {
                        cfg.ActiveMicEqProfileName = "FLAT";
                    }

                    // Also load any standalone .eqprofile.json files in EqProfiles folder
                    LoadExternalProfileFiles(cfg);
                    return cfg;
                }
            }
        }
        catch
        {
        }

        return CreateDefaultConfig();
    }

    public void Save(MixerConfig config)
    {
        try
        {
            string json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(_configPath, json);
        }
        catch
        {
        }
    }

    public void SaveEqProfileToFile(EqProfile profile)
    {
        try
        {
            string safeName = string.Join("_", profile.Name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            if (string.IsNullOrWhiteSpace(safeName)) safeName = "CustomProfile";
            string filePath = Path.Combine(_profilesDir, $"{safeName}.eqprofile.json");
            File.WriteAllText(filePath, JsonSerializer.Serialize(profile, JsonOptions));
        }
        catch
        {
        }
    }

    public void DeleteEqProfileFile(string profileName)
    {
        try
        {
            string safeName = string.Join("_", profileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            string filePath = Path.Combine(_profilesDir, $"{safeName}.eqprofile.json");
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
        }
    }

    public bool ExportEqProfile(EqProfile profile, string targetFilePath)
    {
        try
        {
            File.WriteAllText(targetFilePath, JsonSerializer.Serialize(profile, JsonOptions));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public EqProfile? ImportEqProfile(string sourceFilePath)
    {
        try
        {
            if (!File.Exists(sourceFilePath)) return null;
            string json = File.ReadAllText(sourceFilePath);
            var profile = JsonSerializer.Deserialize<EqProfile>(json, JsonOptions);
            if (profile != null)
            {
                profile.IsBuiltIn = false;
                if (profile.GainsDb == null || profile.GainsDb.Length != 10)
                {
                    profile.GainsDb = new double[10];
                }
                return profile;
            }
        }
        catch
        {
        }
        return null;
    }

    private void LoadExternalProfileFiles(MixerConfig cfg)
    {
        try
        {
            foreach (var file in Directory.GetFiles(_profilesDir, "*.eqprofile.json"))
            {
                var p = ImportEqProfile(file);
                if (p != null && !cfg.CustomEqProfiles.Any(x => x.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    cfg.CustomEqProfiles.Add(p);
                }
            }
        }
        catch
        {
        }
    }

    public static List<EqProfile> GetBuiltInEqProfiles()
    {
        return new List<EqProfile>
        {
            new()
            {
                Name = "FLAT",
                Category = "Reference",
                IsBuiltIn = true,
                PreampDb = 0.0,
                GainsDb = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0]
            },
            new()
            {
                Name = "FPS FOOTSTEPS (CS2 / VALORANT)",
                Category = "Gaming",
                IsBuiltIn = true,
                PreampDb = 0.0,
                GainsDb = [-4.5, -3.0, -1.5, 1.5, 3.0, 5.0, 6.5, 5.5, 2.5, 0.0]
            },
            new()
            {
                Name = "IMMERSION / RPG CINEMATIC",
                Category = "Gaming",
                IsBuiltIn = true,
                PreampDb = 0.0,
                GainsDb = [5.0, 4.5, 2.5, 0.0, -1.0, 0.5, 2.5, 4.0, 4.5, 3.5]
            },
            new()
            {
                Name = "BASS BOOST",
                Category = "Music",
                IsBuiltIn = true,
                PreampDb = 0.0,
                GainsDb = [7.5, 6.5, 4.5, 2.0, 0.0, 0.0, 0.5, 1.0, 1.5, 1.0]
            },
            new()
            {
                Name = "PUNCHY V-SHAPE (MUSIC)",
                Category = "Music",
                IsBuiltIn = true,
                PreampDb = 0.0,
                GainsDb = [5.5, 4.5, 2.0, -1.5, -2.5, -1.0, 2.0, 4.5, 5.5, 5.0]
            },
            new()
            {
                Name = "VOICE CLARITY (DISCORD / PODCAST)",
                Category = "Chat",
                IsBuiltIn = true,
                PreampDb = 1.0,
                GainsDb = [-6.0, -4.5, -1.0, 2.5, 4.0, 4.5, 5.0, 3.0, 0.5, -2.0]
            },
            new()
            {
                Name = "WARM BROADCAST",
                Category = "Chat",
                IsBuiltIn = true,
                PreampDb = 0.5,
                GainsDb = [-2.0, 3.5, 4.0, 1.5, -1.0, 1.5, 3.5, 3.0, 1.5, 0.0]
            },
            new()
            {
                Name = "NIGHT MODE (REDUCED SUB-BASS)",
                Category = "Utility",
                IsBuiltIn = true,
                PreampDb = 0.0,
                GainsDb = [-8.0, -6.0, -3.5, 0.0, 2.0, 3.0, 2.5, 1.0, -1.0, -2.0]
            }
        };
    }

    public static MixerConfig CreateDefaultConfig()
    {
        var builtIns = GetBuiltInEqProfiles();
        var fpsPreset = builtIns.First(p => p.Name.StartsWith("FPS"));
        var chatPreset = builtIns.First(p => p.Name.StartsWith("VOICE"));
        var mediaPreset = builtIns.First(p => p.Name.StartsWith("PUNCHY"));
        var flatPreset = builtIns.First(p => p.Name == "FLAT");

        var config = new MixerConfig
        {
            IsDarkTheme = true,
            MasterVolume = 1.0,
            MasterMuted = false,
            MicVolume = 1.0,
            MicMuted = false,
            IsMicEqEnabled = true,
            ActiveMicEqProfileName = chatPreset.Name,
            MicEqPreampDb = chatPreset.PreampDb,
            MicEqGainsDb = (double[])chatPreset.GainsDb.Clone(),
            Channels = new List<ChannelConfig>
            {
                new()
                {
                    Id = "game",
                    Name = "GAME",
                    IconGlyph = "dashboard",
                    Volume = 1.0,
                    IsPreset = true,
                    IsEqEnabled = true,
                    ActiveEqProfileName = fpsPreset.Name,
                    EqPreampDb = fpsPreset.PreampDb,
                    EqGainsDb = (double[])fpsPreset.GainsDb.Clone()
                },
                new()
                {
                    Id = "chat",
                    Name = "CHAT",
                    IconGlyph = "telegram",
                    Volume = 1.0,
                    IsPreset = true,
                    IsEqEnabled = true,
                    ActiveEqProfileName = chatPreset.Name,
                    EqPreampDb = chatPreset.PreampDb,
                    EqGainsDb = (double[])chatPreset.GainsDb.Clone()
                },
                new()
                {
                    Id = "media",
                    Name = "MEDIA",
                    IconGlyph = "play",
                    Volume = 0.85,
                    IsPreset = true,
                    IsEqEnabled = true,
                    ActiveEqProfileName = mediaPreset.Name,
                    EqPreampDb = mediaPreset.PreampDb,
                    EqGainsDb = (double[])mediaPreset.GainsDb.Clone()
                },
                new()
                {
                    Id = "aux",
                    Name = "AUX",
                    IconGlyph = "layers",
                    Volume = 0.90,
                    IsPreset = true,
                    IsEqEnabled = true,
                    ActiveEqProfileName = flatPreset.Name,
                    EqPreampDb = flatPreset.PreampDb,
                    EqGainsDb = (double[])flatPreset.GainsDb.Clone()
                }
            }
        };

        var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["discord"] = "chat",
            ["teams"] = "chat",
            ["ms-teams"] = "chat",
            ["slack"] = "chat",
            ["zoom"] = "chat",
            ["ts3client_win64"] = "chat",
            ["telegram"] = "chat",
            ["whatsapp"] = "chat",

            ["spotify"] = "media",
            ["vlc"] = "media",
            ["itunes"] = "media",
            ["foobar2000"] = "media",
            ["music.ui"] = "media",
            ["chrome"] = "media",
            ["msedge"] = "media",
            ["firefox"] = "media",
            ["brave"] = "media",
            ["opera"] = "media",

            ["system_sounds"] = "aux",
            ["explorer"] = "aux",
            ["obs64"] = "aux"
        };

        foreach (var kv in defaults)
        {
            config.AppChannelAssignments[kv.Key] = kv.Value;
        }

        return config;
    }

    public static string GuessChannelForProcess(string processKey, IEnumerable<string> availableChannelIds)
    {
        var ids = availableChannelIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        string lower = processKey.ToLowerInvariant();
        if (lower is "discord" or "teams" or "ms-teams" or "slack" or "zoom" or "telegram" or "whatsapp" && ids.Contains("chat"))
            return "chat";

        if (lower is "spotify" or "vlc" or "chrome" or "msedge" or "firefox" or "brave" or "opera" && ids.Contains("media"))
            return "media";

        if (lower is "system_sounds" or "explorer" && ids.Contains("aux"))
            return "aux";

        if (ids.Contains("game"))
            return "game";

        return ids.FirstOrDefault() ?? "game";
    }
}
