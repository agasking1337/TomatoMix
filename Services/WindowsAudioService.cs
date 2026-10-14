using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using TomatoMix.Models;

namespace TomatoMix.Services;

public class WindowsAudioService : IDisposable
{
    private const int ProcessQueryLimitedInformation = 0x1000;
    private const long SessionRefreshIntervalMs = 5_000;
    private const long DeadPidCacheTtlMs = 30_000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, System.Text.StringBuilder lpExeName, ref int lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    // Create MMDeviceEnumerator on a ThreadPool (MTA) thread so WASAPI COM objects live in the MTA
    // rather than being pinned to the UI STA message loop.
    private readonly MMDeviceEnumerator _enumerator = Task.Run(() => new MMDeviceEnumerator()).GetAwaiter().GetResult();
    private MMDevice? _activeDevice;
    private MMDevice? _activeMicDevice;
    private AudioSessionManager? _hookedSessionManager;
    private readonly object _lock = new();

    private readonly record struct PidInfo(string? Key, string? DisplayName, string? ExePath, long Stamp);
    private readonly record struct CachedSessionGroup(
        int ProcessId,
        string ProcessKey,
        string DisplayName,
        string? ExecutablePath,
        bool IsSystemSounds,
        List<AudioSessionControl> Controls);

    private readonly Dictionary<int, PidInfo> _pidInfoCache = new();
    private readonly Dictionary<string, CachedSessionGroup> _sessionGroupsByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (float Volume, bool Mute)> _desiredProcessStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (float Volume, bool Mute)> _pendingProcessUpdates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AvaloniaBitmap?> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _stateLock = new();
    private readonly object _iconLock = new();

    private volatile bool _sessionsDirty = true;
    private long _lastSessionRefreshTick;
    private int _volumeFlushQueued;
    private (float Volume, bool Mute)? _pendingMasterUpdate;
    private (float Volume, bool Mute)? _pendingMicUpdate;
    private (float Volume, bool Mute) _lastKnownMasterState = (1.0f, false);
    private (float Volume, bool Mute) _lastKnownMicState = (1.0f, false);

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
            _pendingMicUpdate = null;
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
                var pending = _pendingMicUpdate;
                var vol = pending?.Volume ?? _activeMicDevice.AudioEndpointVolume.MasterVolumeLevelScalar;
                var muted = pending?.Mute ?? _activeMicDevice.AudioEndpointVolume.Mute;
                var peak = _activeMicDevice.AudioMeterInformation.MasterPeakValue;
                _lastKnownMicState = (vol, muted);
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
        float clamped = Math.Clamp(scalarVolume, 0.0f, 1.0f);
        lock (_stateLock)
        {
            if (Math.Abs(_lastKnownMicState.Volume - clamped) <= 0.002f && _lastKnownMicState.Mute == isMuted)
                return;

            _lastKnownMicState = (clamped, isMuted);
            _pendingMicUpdate = (clamped, isMuted);
        }
        QueueVolumeFlush();
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
            UnhookSessionManagerLocked();
            _sessionGroupsByKey.Clear();
            _sessionsDirty = true;
            _lastSessionRefreshTick = 0;
            _pendingMasterUpdate = null;

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

    private void UnhookSessionManagerLocked()
    {
        if (_hookedSessionManager != null)
        {
            try
            {
                _hookedSessionManager.OnSessionCreated -= OnAudioSessionCreated;
            }
            catch
            {
            }
            _hookedSessionManager = null;
        }
    }

    private void OnAudioSessionCreated(object sender, IAudioSessionControl newSession)
    {
        _sessionsDirty = true;
    }

    public void RequestSessionRefresh()
    {
        _sessionsDirty = true;
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
                var pending = _pendingMasterUpdate;
                var vol = pending?.Volume ?? _activeDevice.AudioEndpointVolume.MasterVolumeLevelScalar;
                var muted = pending?.Mute ?? _activeDevice.AudioEndpointVolume.Mute;
                var peak = _activeDevice.AudioMeterInformation.MasterPeakValue;
                _lastKnownMasterState = (vol, muted);
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
        float clamped = Math.Clamp(scalarVolume, 0.0f, 1.0f);
        lock (_stateLock)
        {
            if (Math.Abs(_lastKnownMasterState.Volume - clamped) <= 0.002f && _lastKnownMasterState.Mute == isMuted)
                return;

            _lastKnownMasterState = (clamped, isMuted);
            _pendingMasterUpdate = (clamped, isMuted);
        }
        QueueVolumeFlush();
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
                long now = Environment.TickCount64;
                if (_sessionsDirty || _sessionGroupsByKey.Count == 0 || now - _lastSessionRefreshTick >= SessionRefreshIntervalMs)
                {
                    RefreshSessionGroupsLocked(now);
                }

                foreach (var group in _sessionGroupsByKey.Values)
                {
                    float maxPeak = 0f;
                    int validControls = 0;

                    for (int i = group.Controls.Count - 1; i >= 0; i--)
                    {
                        var s = group.Controls[i];
                        try
                        {
                            if (s.State == AudioSessionState.AudioSessionStateExpired)
                            {
                                group.Controls.RemoveAt(i);
                                continue;
                            }

                            validControls++;
                            float peak = s.AudioMeterInformation.MasterPeakValue;
                            if (peak > maxPeak)
                            {
                                maxPeak = peak;
                            }
                        }
                        catch
                        {
                            group.Controls.RemoveAt(i);
                        }
                    }

                    if (validControls == 0)
                    {
                        _sessionsDirty = true;
                        continue;
                    }

                    list.Add(new AudioSessionData
                    {
                        ProcessId = group.ProcessId,
                        ProcessKey = group.ProcessKey,
                        DisplayName = group.DisplayName,
                        ExecutablePath = group.ExecutablePath,
                        CurrentPeak = maxPeak,
                        IsSystemSounds = group.IsSystemSounds
                    });
                }
            }
            catch
            {
                _sessionsDirty = true;
            }
        }
        return list;
    }

    private void RefreshSessionGroupsLocked(long now)
    {
        if (_activeDevice == null) return;

        var sessionManager = _activeDevice.AudioSessionManager;
        if (!ReferenceEquals(_hookedSessionManager, sessionManager))
        {
            UnhookSessionManagerLocked();
            _hookedSessionManager = sessionManager;
            try
            {
                _hookedSessionManager.OnSessionCreated += OnAudioSessionCreated;
            }
            catch
            {
            }
        }

        _sessionsDirty = false;
        _lastSessionRefreshTick = now;

        sessionManager.RefreshSessions();
        var sessions = sessionManager.Sessions;
        int currentAppPid = Environment.ProcessId;
        var activePids = new HashSet<int>();

        _sessionGroupsByKey.Clear();

        for (int i = 0; i < sessions.Count; i++)
        {
            var s = sessions[i];
            try
            {
                if (s.State == AudioSessionState.AudioSessionStateExpired)
                    continue;

                int pid = (int)s.GetProcessID;
                if (pid == currentAppPid)
                    continue;

                activePids.Add(pid);
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
                    if (!TryResolvePidMetadataLocked(pid, s, now, out processKey, out displayName, out exePath))
                        continue;
                }

                if (_sessionGroupsByKey.TryGetValue(processKey, out var existingGroup))
                {
                    existingGroup.Controls.Add(s);
                    if (existingGroup.ExecutablePath == null && exePath != null)
                    {
                        _sessionGroupsByKey[processKey] = existingGroup with { ExecutablePath = exePath };
                    }
                }
                else
                {
                    _sessionGroupsByKey[processKey] = new CachedSessionGroup(
                        pid,
                        processKey,
                        displayName,
                        exePath,
                        isSys,
                        new List<AudioSessionControl> { s });
                }

                // Ensure any newly discovered session immediately receives its desired volume/mute state
                (float Volume, bool Mute) desired;
                bool hasDesired;
                lock (_stateLock)
                {
                    hasDesired = _desiredProcessStates.TryGetValue(processKey, out desired);
                }
                if (hasDesired)
                {
                    ApplyVolumeToControlSafe(s, desired.Volume, desired.Mute);
                }
            }
            catch
            {
            }
        }

        if (_pidInfoCache.Count > 256)
        {
            var stalePids = _pidInfoCache
                .Where(kv => !activePids.Contains(kv.Key) && now - kv.Value.Stamp >= DeadPidCacheTtlMs)
                .Select(kv => kv.Key)
                .ToList();
            foreach (var k in stalePids)
            {
                _pidInfoCache.Remove(k);
            }
        }
    }

    private bool TryResolvePidMetadataLocked(
        int pid,
        AudioSessionControl session,
        long now,
        out string processKey,
        out string displayName,
        out string? exePath)
    {
        processKey = string.Empty;
        displayName = string.Empty;
        exePath = null;

        if (_pidInfoCache.TryGetValue(pid, out var cachedInfo))
        {
            if (cachedInfo.Key != null)
            {
                // Keep active PIDs fresh so they never all expire simultaneously every 30s
                _pidInfoCache[pid] = cachedInfo with { Stamp = now };
                processKey = cachedInfo.Key;
                displayName = cachedInfo.DisplayName!;
                exePath = cachedInfo.ExePath;
                return true;
            }

            if (now - cachedInfo.Stamp < DeadPidCacheTtlMs)
            {
                return false;
            }
        }

        try
        {
            exePath = TryGetProcessExecutablePathFast(pid);
            string? procName = !string.IsNullOrWhiteSpace(exePath)
                ? Path.GetFileNameWithoutExtension(exePath)
                : null;

            if (string.IsNullOrWhiteSpace(procName))
            {
                using var proc = Process.GetProcessById(pid);
                procName = proc.ProcessName;
            }

            if (string.IsNullOrWhiteSpace(procName))
            {
                _pidInfoCache[pid] = new PidInfo(null, null, null, now);
                return false;
            }

            processKey = procName.ToLowerInvariant();
            string? title = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
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
                string? sessionDisplayName = null;
                try
                {
                    sessionDisplayName = session.DisplayName;
                }
                catch
                {
                }

                title = !string.IsNullOrWhiteSpace(sessionDisplayName) && !sessionDisplayName.StartsWith("@")
                    ? sessionDisplayName
                    : FormatProcessName(procName);
            }

            displayName = title!;
            _pidInfoCache[pid] = new PidInfo(processKey, displayName, exePath, now);
            return true;
        }
        catch
        {
            _pidInfoCache[pid] = new PidInfo(null, null, null, now);
            return false;
        }
    }

    private static string? TryGetProcessExecutablePathFast(int pid)
    {
        IntPtr handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero)
            return null;

        try
        {
            var sb = new System.Text.StringBuilder(1024);
            int size = sb.Capacity;
            if (QueryFullProcessImageName(handle, 0, sb, ref size) && size > 0)
            {
                return sb.ToString(0, size);
            }
        }
        catch
        {
        }
        finally
        {
            CloseHandle(handle);
        }

        return null;
    }

    public void ApplyProcessVolume(string processKey, float effectiveVolume, bool effectiveMute)
    {
        float clampedVol = Math.Clamp(effectiveVolume, 0.0f, 1.0f);
        lock (_stateLock)
        {
            if (_desiredProcessStates.TryGetValue(processKey, out var prev)
                && Math.Abs(prev.Volume - clampedVol) <= 0.002f
                && prev.Mute == effectiveMute)
            {
                return;
            }
            _desiredProcessStates[processKey] = (clampedVol, effectiveMute);
        }

        _pendingProcessUpdates[processKey] = (clampedVol, effectiveMute);
        QueueVolumeFlush();
    }

    private void QueueVolumeFlush()
    {
        if (Interlocked.CompareExchange(ref _volumeFlushQueued, 1, 0) == 0)
        {
            Task.Run(FlushPendingVolumes);
        }
    }

    private void FlushPendingVolumes()
    {
        try
        {
            while (true)
            {
                (float Volume, bool Mute)? masterUpdate;
                (float Volume, bool Mute)? micUpdate;
                lock (_stateLock)
                {
                    masterUpdate = _pendingMasterUpdate;
                    _pendingMasterUpdate = null;
                    micUpdate = _pendingMicUpdate;
                    _pendingMicUpdate = null;
                }

                var processKeys = _pendingProcessUpdates.Keys.ToList();

                if (masterUpdate == null && micUpdate == null && processKeys.Count == 0)
                    break;

                lock (_lock)
                {
                    if (masterUpdate is { } m && _activeDevice != null)
                    {
                        try
                        {
                            if (Math.Abs(_activeDevice.AudioEndpointVolume.MasterVolumeLevelScalar - m.Volume) > 0.002f)
                            {
                                _activeDevice.AudioEndpointVolume.MasterVolumeLevelScalar = m.Volume;
                            }
                            if (_activeDevice.AudioEndpointVolume.Mute != m.Mute)
                            {
                                _activeDevice.AudioEndpointVolume.Mute = m.Mute;
                            }
                        }
                        catch
                        {
                        }
                    }

                    if (micUpdate is { } mic && _activeMicDevice != null)
                    {
                        try
                        {
                            if (Math.Abs(_activeMicDevice.AudioEndpointVolume.MasterVolumeLevelScalar - mic.Volume) > 0.002f)
                            {
                                _activeMicDevice.AudioEndpointVolume.MasterVolumeLevelScalar = mic.Volume;
                            }
                            if (_activeMicDevice.AudioEndpointVolume.Mute != mic.Mute)
                            {
                                _activeMicDevice.AudioEndpointVolume.Mute = mic.Mute;
                            }
                        }
                        catch
                        {
                        }
                    }

                    foreach (var key in processKeys)
                    {
                        if (!_pendingProcessUpdates.TryRemove(key, out var target))
                            continue;

                        if (_sessionGroupsByKey.TryGetValue(key, out var group))
                        {
                            for (int i = 0; i < group.Controls.Count; i++)
                            {
                                ApplyVolumeToControlSafe(group.Controls[i], target.Volume, target.Mute);
                            }
                        }
                    }
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _volumeFlushQueued, 0);
            bool hasMore;
            lock (_stateLock)
            {
                hasMore = _pendingMasterUpdate != null || _pendingMicUpdate != null || !_pendingProcessUpdates.IsEmpty;
            }
            if (hasMore)
            {
                QueueVolumeFlush();
            }
        }
    }

    private static void ApplyVolumeToControlSafe(AudioSessionControl control, float clampedVol, bool effectiveMute)
    {
        try
        {
            if (control.State == AudioSessionState.AudioSessionStateExpired)
                return;

            if (Math.Abs(control.SimpleAudioVolume.Volume - clampedVol) > 0.003f)
            {
                control.SimpleAudioVolume.Volume = clampedVol;
            }
            if (control.SimpleAudioVolume.Mute != effectiveMute)
            {
                control.SimpleAudioVolume.Mute = effectiveMute;
            }
        }
        catch
        {
        }
    }

    public AvaloniaBitmap? GetAppIcon(string processKey, string? executablePath)
    {
        lock (_iconLock)
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
            UnhookSessionManagerLocked();
            _sessionGroupsByKey.Clear();
            _activeDevice?.Dispose();
            _activeMicDevice?.Dispose();
            _enumerator.Dispose();
        }
    }
}
