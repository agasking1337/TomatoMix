using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using TomatoMix.Models;

namespace TomatoMix.Services;

public class WindowsAudioService : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private MMDevice? _activeDevice;
    private MMDevice? _activeMicDevice;
    private readonly object _lock = new();
    private const long PidCacheTtl = 30_000;
    private readonly record struct PidInfo(string? Key, string? DisplayName, string? ExePath, long Stamp);
    private readonly Dictionary<int, PidInfo> _pidInfoCache = new();
    private readonly Dictionary<string, AvaloniaBitmap?> _iconCache = new(StringComparer.OrdinalIgnoreCase);

    public List<AudioDeviceInfo> GetInputDevices()
    {
        var result = new List<AudioDeviceInfo>();
        try
        {
            string? defaultId = null;
            try
            {
                using var def = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                defaultId = def?.ID;
            }
            catch
            {
            }

            var devices = _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            foreach (var dev in devices)
            {
                result.Add(new AudioDeviceInfo
                {
                    Id = dev.ID,
                    Name = dev.FriendlyName,
                    IsDefault = dev.ID == defaultId
                });
            }
        }
        catch
        {
        }
        return result;
    }

    public void SelectMicDevice(string? deviceId = null)
    {
        lock (_lock)
        {
            try
            {
                _activeMicDevice?.Dispose();
                if (!string.IsNullOrEmpty(deviceId))
                {
                    _activeMicDevice = _enumerator.GetDevice(deviceId);
                }
                else
                {
                    _activeMicDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                }
            }
            catch
            {
                try
                {
                    _activeMicDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
                }
                catch
                {
                    _activeMicDevice = null;
                }
            }
        }
    }

    public (float volume, bool isMuted, float peak) GetMicState()
    {
        lock (_lock)
        {
            if (_activeMicDevice == null)
            {
                SelectMicDevice(null);
            }

            if (_activeMicDevice == null)
            {
                return (1.0f, false, 0.0f);
            }

            try
            {
                var vol = _activeMicDevice.AudioEndpointVolume.MasterVolumeLevelScalar;
                var muted = _activeMicDevice.AudioEndpointVolume.Mute;
                var peak = _activeMicDevice.AudioMeterInformation.MasterPeakValue;
                return (vol, muted, peak);
            }
            catch
            {
                return (1.0f, false, 0.0f);
            }
        }
    }

    public void SetMicVolume(float scalarVolume, bool isMuted)
    {
        lock (_lock)
        {
            if (_activeMicDevice == null) return;
            try
            {
                float clamped = Math.Clamp(scalarVolume, 0.0f, 1.0f);
                if (Math.Abs(_activeMicDevice.AudioEndpointVolume.MasterVolumeLevelScalar - clamped) > 0.002f)
                {
                    _activeMicDevice.AudioEndpointVolume.MasterVolumeLevelScalar = clamped;
                }
                if (_activeMicDevice.AudioEndpointVolume.Mute != isMuted)
                {
                    _activeMicDevice.AudioEndpointVolume.Mute = isMuted;
                }
            }
            catch
            {
            }
        }
    }

    public MMDevice? GetClonedMicDevice(string? deviceId)
    {
        try
        {
            if (!string.IsNullOrEmpty(deviceId))
            {
                return _enumerator.GetDevice(deviceId);
            }
            return _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
        }
        catch
        {
            return null;
        }
    }

    public List<AudioDeviceInfo> GetOutputDevices()
    {
        var result = new List<AudioDeviceInfo>();
        try
        {
            string? defaultId = null;
            try
            {
                using var def = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                defaultId = def?.ID;
            }
            catch
            {
            }

            var devices = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var dev in devices)
            {
                result.Add(new AudioDeviceInfo
                {
                    Id = dev.ID,
                    Name = dev.FriendlyName,
                    IsDefault = dev.ID == defaultId
                });
            }
        }
        catch
        {
        }
        return result;
    }

    public void SelectDevice(string? deviceId = null)
    {
        lock (_lock)
        {
            try
            {
                _activeDevice?.Dispose();
                if (!string.IsNullOrEmpty(deviceId))
                {
                    _activeDevice = _enumerator.GetDevice(deviceId);
                }
                else
                {
                    _activeDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                }
            }
            catch
            {
                try
                {
                    _activeDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                }
                catch
                {
                    _activeDevice = null;
                }
            }
        }
    }

    public string? GetActiveDeviceId()
    {
        lock (_lock)
        {
            return _activeDevice?.ID;
        }
    }

    public string GetActiveDeviceName()
    {
        lock (_lock)
        {
            try
            {
                return _activeDevice?.FriendlyName ?? "No Output Device";
            }
            catch
            {
                return "No Output Device";
            }
        }
    }

    public (float volume, bool isMuted, float peak) GetMasterState()
    {
        lock (_lock)
        {
            if (_activeDevice == null)
            {
                SelectDevice(null);
            }

            if (_activeDevice == null)
            {
                return (1.0f, false, 0.0f);
            }

            try
            {
                var vol = _activeDevice.AudioEndpointVolume.MasterVolumeLevelScalar;
                var muted = _activeDevice.AudioEndpointVolume.Mute;
                var peak = _activeDevice.AudioMeterInformation.MasterPeakValue;
                return (vol, muted, peak);
            }
            catch
            {
                return (1.0f, false, 0.0f);
            }
        }
    }

    public void SetMasterVolume(float scalarVolume, bool isMuted)
    {
        lock (_lock)
        {
            if (_activeDevice == null) return;
            try
            {
                float clamped = Math.Clamp(scalarVolume, 0.0f, 1.0f);
                if (Math.Abs(_activeDevice.AudioEndpointVolume.MasterVolumeLevelScalar - clamped) > 0.002f)
                {
                    _activeDevice.AudioEndpointVolume.MasterVolumeLevelScalar = clamped;
                }
                if (_activeDevice.AudioEndpointVolume.Mute != isMuted)
                {
                    _activeDevice.AudioEndpointVolume.Mute = isMuted;
                }
            }
            catch
            {
            }
        }
    }

    public List<AudioSessionData> GetActiveSessions()
    {
        var list = new List<AudioSessionData>();
        lock (_lock)
        {
            if (_activeDevice == null)
            {
                SelectDevice(null);
            }
            if (_activeDevice == null) return list;

            try
            {
                var sessionManager = _activeDevice.AudioSessionManager;
                sessionManager.RefreshSessions();
                var sessions = sessionManager.Sessions;
                int currentAppPid = Environment.ProcessId;
                long now = Environment.TickCount64;
                if (_pidInfoCache.Count > 512)
                {
                    foreach (var k in _pidInfoCache.Where(kv => now - kv.Value.Stamp >= PidCacheTtl).Select(kv => kv.Key).ToList())
                        _pidInfoCache.Remove(k);
                }

                var groupedByKey = new Dictionary<string, AudioSessionData>(StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < sessions.Count; i++)
                {
                    var s = sessions[i];
                    try
                    {
                        var state = s.State;
                        if (state == AudioSessionState.AudioSessionStateExpired)
                            continue;

                        int pid = (int)s.GetProcessID;
                        if (pid == currentAppPid)
                            continue;

                        bool isSys = s.IsSystemSoundsSession || pid == 0;
                        string processKey;
                        string displayName;
                        string? exePath = null;

                        if (isSys)
                        {
                            processKey = "system_sounds";
                            displayName = "System Sounds";
                        }
                        else
                        {
                            if (_pidInfoCache.TryGetValue(pid, out var cachedInfo) && now - cachedInfo.Stamp < PidCacheTtl)
                            {
                                if (cachedInfo.Key == null) continue;
                                processKey = cachedInfo.Key;
                                displayName = cachedInfo.DisplayName!;
                                exePath = cachedInfo.ExePath;
                            }
                            else
                            {
                                try
                                {
                                    using var proc = Process.GetProcessById(pid);
                                    string procName = proc.ProcessName;
                                    if (string.IsNullOrWhiteSpace(procName))
                                    {
                                        _pidInfoCache[pid] = new PidInfo(null, null, null, now);
                                        continue;
                                    }

                                    processKey = procName.ToLowerInvariant();
                                    try
                                    {
                                        exePath = proc.MainModule?.FileName;
                                    }
                                    catch
                                    {
                                    }

                                    string? title = null;
                                    try
                                    {
                                        if (!string.IsNullOrWhiteSpace(exePath))
                                        {
                                            var versionInfo = FileVersionInfo.GetVersionInfo(exePath);
                                            title = versionInfo.FileDescription;
                                        }
                                    }
                                    catch
                                    {
                                    }

                                    if (string.IsNullOrWhiteSpace(title))
                                    {
                                        title = !string.IsNullOrWhiteSpace(s.DisplayName) && !s.DisplayName.StartsWith("@")
                                            ? s.DisplayName
                                            : FormatProcessName(procName);
                                    }

                                    displayName = title!;
                                    _pidInfoCache[pid] = new PidInfo(processKey, displayName, exePath, now);
                                }
                                catch
                                {
                                    _pidInfoCache[pid] = new PidInfo(null, null, null, now);
                                    continue;
                                }
                            }
                        }

                        float peak = 0f;
                        try
                        {
                            peak = s.AudioMeterInformation.MasterPeakValue;
                        }
                        catch
                        {
                        }

                        float rawVol = 1f;
                        bool rawMute = false;
                        try
                        {
                            rawVol = s.SimpleAudioVolume.Volume;
                            rawMute = s.SimpleAudioVolume.Mute;
                        }
                        catch
                        {
                        }

                        if (groupedByKey.TryGetValue(processKey, out var existing))
                        {
                            if (peak > existing.CurrentPeak)
                            {
                                existing.CurrentPeak = peak;
                            }
                            if (existing.ExecutablePath == null && exePath != null)
                            {
                                existing.ExecutablePath = exePath;
                            }
                        }
                        else
                        {
                            groupedByKey[processKey] = new AudioSessionData
                            {
                                ProcessId = pid,
                                ProcessKey = processKey,
                                DisplayName = displayName,
                                ExecutablePath = exePath,
                                CurrentPeak = peak,
                                RawVolume = rawVol,
                                RawMute = rawMute,
                                IsSystemSounds = isSys
                            };
                        }
                    }
                    catch
                    {
                    }
                }

                list.AddRange(groupedByKey.Values);
            }
            catch
            {
            }
        }
        return list;
    }

    public void ApplyProcessVolume(string processKey, float effectiveVolume, bool effectiveMute)
    {
        lock (_lock)
        {
            if (_activeDevice == null) return;
            try
            {
                var sessions = _activeDevice.AudioSessionManager.Sessions;
                float clampedVol = Math.Clamp(effectiveVolume, 0.0f, 1.0f);

                for (int i = 0; i < sessions.Count; i++)
                {
                    var s = sessions[i];
                    try
                    {
                        if (s.State == AudioSessionState.AudioSessionStateExpired)
                            continue;

                        int pid = (int)s.GetProcessID;
                        bool isSys = s.IsSystemSoundsSession || pid == 0;
                        string key;
                        if (isSys)
                        {
                            key = "system_sounds";
                        }
                        else
                        {
                            if (_pidInfoCache.TryGetValue(pid, out var cachedInfo) && Environment.TickCount64 - cachedInfo.Stamp < PidCacheTtl)
                            {
                                if (cachedInfo.Key == null) continue;
                                key = cachedInfo.Key;
                            }
                            else
                            {
                                try
                                {
                                    using var proc = Process.GetProcessById(pid);
                                    key = proc.ProcessName.ToLowerInvariant();
                                }
                                catch
                                {
                                    continue;
                                }
                            }
                        }

                        if (string.Equals(key, processKey, StringComparison.OrdinalIgnoreCase))
                        {
                            if (Math.Abs(s.SimpleAudioVolume.Volume - clampedVol) > 0.003f)
                            {
                                s.SimpleAudioVolume.Volume = clampedVol;
                            }
                            if (s.SimpleAudioVolume.Mute != effectiveMute)
                            {
                                s.SimpleAudioVolume.Mute = effectiveMute;
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }
    }

    public AvaloniaBitmap? GetAppIcon(string processKey, string? executablePath)
    {
        if (_iconCache.TryGetValue(processKey, out var cached))
            return cached;

        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            _iconCache[processKey] = null;
            return null;
        }

        try
        {
            using var sysIcon = Icon.ExtractAssociatedIcon(executablePath);
            if (sysIcon != null)
            {
                using var bmp = sysIcon.ToBitmap();
                using var ms = new MemoryStream();
                bmp.Save(ms, ImageFormat.Png);
                ms.Position = 0;
                var avaloniaBitmap = new AvaloniaBitmap(ms);
                _iconCache[processKey] = avaloniaBitmap;
                return avaloniaBitmap;
            }
        }
        catch
        {
        }

        _iconCache[processKey] = null;
        return null;
    }

    private static string FormatProcessName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Unknown App";
        return char.ToUpperInvariant(name[0]) + name[1..];
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _activeDevice?.Dispose();
            _activeMicDevice?.Dispose();
            _enumerator.Dispose();
        }
    }
}
