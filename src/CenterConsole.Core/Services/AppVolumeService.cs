using CenterConsole.Core.Models;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace CenterConsole.Core.Services;

public interface IAppVolumeService : IDisposable
{
    /// <summary>Active audio sessions on the current default output device, one entry per process
    /// (sessions sharing a process name, e.g. multiple Chrome tabs, are merged into one entry).</summary>
    IReadOnlyList<AppVolumeTarget> GetActiveSessions();

    void SetVolume(string processName, float volume);
    void AdjustVolume(string processName, float delta);
    void SetMuted(string processName, bool muted);

    /// <summary>Raised when a new audio session appears (process starts playing audio).</summary>
    event EventHandler? SessionsChanged;
}

public sealed class AppVolumeService : IAppVolumeService
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly AudioSessionManager.SessionCreatedDelegate _sessionCreatedHandler;

    public event EventHandler? SessionsChanged;

    public AppVolumeService()
    {
        _sessionCreatedHandler = (_, _) => SessionsChanged?.Invoke(this, EventArgs.Empty);
        TryGetDefaultRenderDevice()?.Dispose(); // touch once to fail fast if no render device at all
        SubscribeToSessionCreation();
    }

    private void SubscribeToSessionCreation()
    {
        using var device = TryGetDefaultRenderDevice();
        if (device is not null)
            device.AudioSessionManager.OnSessionCreated += _sessionCreatedHandler;
    }

    public IReadOnlyList<AppVolumeTarget> GetActiveSessions()
    {
        using var device = TryGetDefaultRenderDevice();
        if (device is null)
            return Array.Empty<AppVolumeTarget>();

        var byProcess = new Dictionary<string, (int Pid, float Volume, bool Muted)>(StringComparer.OrdinalIgnoreCase);
        var sessions = device.AudioSessionManager.Sessions;

        for (int i = 0; i < sessions.Count; i++)
        {
            var session = sessions[i];
            if (session.State == AudioSessionState.AudioSessionStateExpired || session.IsSystemSoundsSession)
                continue;

            int pid = (int)session.GetProcessID;
            string processName = TryGetProcessName(pid);
            if (processName.Length == 0)
                continue; // process already exited

            byProcess[processName] = (pid, session.SimpleAudioVolume.Volume, session.SimpleAudioVolume.Mute);
        }

        return byProcess
            .Select(kvp => new AppVolumeTarget(kvp.Value.Pid, kvp.Key, kvp.Key, kvp.Value.Volume, kvp.Value.Muted))
            .OrderBy(t => t.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void SetVolume(string processName, float volume)
    {
        float clamped = Math.Clamp(volume, 0f, 1f);
        ForEachSessionForProcess(processName, session => session.SimpleAudioVolume.Volume = clamped);
    }

    public void AdjustVolume(string processName, float delta)
    {
        ForEachSessionForProcess(processName, session =>
        {
            float newVolume = Math.Clamp(session.SimpleAudioVolume.Volume + delta, 0f, 1f);
            session.SimpleAudioVolume.Volume = newVolume;
        });
    }

    public void SetMuted(string processName, bool muted)
    {
        ForEachSessionForProcess(processName, session => session.SimpleAudioVolume.Mute = muted);
    }

    private void ForEachSessionForProcess(string processName, Action<AudioSessionControl> apply)
    {
        using var device = TryGetDefaultRenderDevice();
        if (device is null)
            return;

        var sessions = device.AudioSessionManager.Sessions;
        for (int i = 0; i < sessions.Count; i++)
        {
            var session = sessions[i];
            if (session.State == AudioSessionState.AudioSessionStateExpired || session.IsSystemSoundsSession)
                continue;

            int pid = (int)session.GetProcessID;
            if (string.Equals(TryGetProcessName(pid), processName, StringComparison.OrdinalIgnoreCase))
                apply(session);
        }
    }

    private static string TryGetProcessName(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return string.Empty; // process has exited
        }
    }

    private MMDevice? TryGetDefaultRenderDevice()
    {
        try
        {
            return _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return null; // no default output device configured
        }
    }

    public void Dispose()
    {
        using var device = TryGetDefaultRenderDevice();
        if (device is not null)
            device.AudioSessionManager.OnSessionCreated -= _sessionCreatedHandler;

        _enumerator.Dispose();
    }
}
