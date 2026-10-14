using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Win32;
using TomatoMix.Models;
using TomatoMix.ViewModels;

namespace TomatoMix.Services;

public class EqualizerApoService
{
    private const string IncludeFileName = "tomatomix_eq.txt";
    private const string IncludeDirective = "Include: tomatomix_eq.txt";

    private string? _cachedConfigDir;
    private readonly string _fallbackAppDataFile;

    public EqualizerApoService()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string dir = Path.Combine(appData, "TomatoMix");
        Directory.CreateDirectory(dir);
        _fallbackAppDataFile = Path.Combine(dir, IncludeFileName);
        DetectEqualizerApoConfigDirectory();
    }

    public bool IsInstalled => !string.IsNullOrEmpty(_cachedConfigDir) && Directory.Exists(_cachedConfigDir);

    public string ActiveConfigPath => IsInstalled
        ? Path.Combine(_cachedConfigDir!, IncludeFileName)
        : _fallbackAppDataFile;

    public string? EqualizerApoDirectory => string.IsNullOrEmpty(_cachedConfigDir)
        ? null
        : Directory.GetParent(_cachedConfigDir)?.FullName;

    public bool DetectEqualizerApoConfigDirectory()
    {
        var candidates = new List<string>
        {
            @"C:\Program Files\EqualizerAPO\config",
            @"C:\Program Files (x86)\EqualizerAPO\config"
        };

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\EqualizerAPO");
            var installPath = key?.GetValue("InstallPath") as string;
            if (!string.IsNullOrWhiteSpace(installPath))
            {
                candidates.Insert(0, Path.Combine(installPath, "config"));
            }
        }
        catch
        {
        }

        foreach (var dir in candidates)
        {
            if (Directory.Exists(dir))
            {
                _cachedConfigDir = dir;
                EnsureMainConfigIncludesTomatoMix(dir);
                return true;
            }
        }

        _cachedConfigDir = null;
        return false;
    }

    private static void EnsureMainConfigIncludesTomatoMix(string configDir)
    {
        try
        {
            string mainConfigPath = Path.Combine(configDir, "config.txt");
            if (!File.Exists(mainConfigPath))
            {
                File.WriteAllText(mainConfigPath, $"{IncludeDirective}{Environment.NewLine}");
                return;
            }

            string existing = File.ReadAllText(mainConfigPath);
            if (!existing.Contains(IncludeFileName, StringComparison.OrdinalIgnoreCase))
            {
                string prefix = existing.EndsWith('\n') || existing.Length == 0 ? "" : Environment.NewLine;
                File.AppendAllText(mainConfigPath, $"{prefix}{IncludeDirective}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }

    /// <summary>
    /// Computes the effective 10-band EQ curve across active channels (or the focused EQ Studio channel)
    /// and writes it immediately to Equalizer APO's live config file (0ms reload latency).
    /// </summary>
    public (bool wroteToApoDir, string summary) SyncLiveEqualizerApo(
        IReadOnlyList<ChannelViewModel> channels,
        ChannelViewModel? currentlyEditedChannel)
    {
        DetectEqualizerApoConfigDirectory();

        // Determine effective 10-band gains + preamp to push to Equalizer APO:
        // 1. If a channel is currently open in the EQ Studio, or only specific channels have active audio apps,
        //    blend active channels weighted by their live activity (or prioritize the channel being edited in EQ Studio).
        double[] finalGains = new double[10];
        double finalPreamp = 0.0;
        string sourceLabel = "FLAT";

        if (currentlyEditedChannel != null)
        {
            if (currentlyEditedChannel.IsEqEnabled)
            {
                for (int i = 0; i < 10; i++)
                {
                    finalGains[i] = currentlyEditedChannel.EqGainsDb[i];
                }
                finalPreamp = currentlyEditedChannel.EqPreampDb;
                sourceLabel = $"{currentlyEditedChannel.Name} ({currentlyEditedChannel.ActiveEqProfileName})";
            }
            else
            {
                sourceLabel = $"{currentlyEditedChannel.Name} (BYPASSED)";
            }
        }
        else
        {
            // Find channels that are enabled, not muted, and either have apps or non-flat EQ
            var activeWithApps = channels
                .Where(c => c.IsEqEnabled && !c.IsEffectivelyMuted && c.Apps.Count > 0)
                .ToList();

            if (activeWithApps.Count == 0)
            {
                activeWithApps = channels.Where(c => c.IsEqEnabled && !c.IsEffectivelyMuted).ToList();
            }

            if (activeWithApps.Count > 0)
            {
                // Weight by channel volume (and boost weight if apps in that channel are actively playing sound)
                double totalWeight = 0.0;
                foreach (var ch in activeWithApps)
                {
                    double w = (ch.VolumePercent / 100.0) * (ch.PeakLevel > 1.0 ? 3.0 : 1.0);
                    if (w <= 0.01) w = 0.1;
                    totalWeight += w;

                    for (int i = 0; i < 10; i++)
                    {
                        finalGains[i] += ch.EqGainsDb[i] * w;
                    }
                    finalPreamp += ch.EqPreampDb * w;
                }

                if (totalWeight > 0)
                {
                    for (int i = 0; i < 10; i++)
                    {
                        finalGains[i] /= totalWeight;
                    }
                    finalPreamp /= totalWeight;
                }

                sourceLabel = activeWithApps.Count == 1
                    ? $"{activeWithApps[0].Name} ({activeWithApps[0].ActiveEqProfileName})"
                    : $"MULTI-CHANNEL MIX ({activeWithApps.Count} CH)";
            }
        }

        // Auto anti-clipping headroom if user hasn't manually lowered preamp enough for high boosts
        double maxBandBoost = finalGains.Max();
        double effectivePreamp = finalPreamp;
        if (maxBandBoost > 0 && effectivePreamp + maxBandBoost > 3.0)
        {
            effectivePreamp = Math.Min(effectivePreamp, -(maxBandBoost * 0.55));
        }

        var sb = new StringBuilder();
        sb.AppendLine($"# TomatoMix Live Sonar Equalizer APO Configuration");
        sb.AppendLine($"# Active Source: {sourceLabel}");
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "Preamp: {0:0.00} dB", effectivePreamp));

        for (int i = 0; i < 10; i++)
        {
            double freq = EqProfile.StandardFrequencies[i];
            double gain = Math.Clamp(finalGains[i], -15.0, 15.0);
            sb.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "Filter {0}: ON PK Fc {1:0} Hz Gain {2:0.00} dB Q 1.41",
                i + 1,
                freq,
                gain));
        }

        string content = sb.ToString();

        // Always write to %AppData%\TomatoMix\tomatomix_eq.txt
        try
        {
            File.WriteAllText(_fallbackAppDataFile, content);
        }
        catch
        {
        }

        // If Equalizer APO is installed, write directly to C:\Program Files\EqualizerAPO\config\tomatomix_eq.txt
        if (IsInstalled && _cachedConfigDir != null)
        {
            try
            {
                EnsureMainConfigIncludesTomatoMix(_cachedConfigDir);
                string apoFile = Path.Combine(_cachedConfigDir, IncludeFileName);
                File.WriteAllText(apoFile, content);
                return (true, $"APO LIVE: {sourceLabel}");
            }
            catch (UnauthorizedAccessException)
            {
                return (false, "APO PERMISSION NEEDED (Run TomatoMix as Admin or grant write to EqualizerAPO\\config)");
            }
            catch
            {
            }
        }

        return (false, "EQUALIZER APO NOT DETECTED (Click 'APO HOOK' to install or use 'AUDITION EQ')");
    }

    public bool LaunchEqualizerApoConfigurator()
    {
        var dir = EqualizerApoDirectory;
        if (string.IsNullOrEmpty(dir)) return false;

        string configurator = Path.Combine(dir, "Configurator.exe");
        string editor = Path.Combine(dir, "Editor.exe");
        string target = File.Exists(configurator) ? configurator : editor;
        if (!File.Exists(target)) return false;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void OpenEqualizerApoDownloadPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://sourceforge.net/projects/equalizerapo/",
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }
}
