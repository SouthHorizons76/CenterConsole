using System.Diagnostics;

namespace CenterConsole.Core.Services;

public interface IStartupService
{
    /// <summary>Creates or removes the scheduled task that launches CenterConsole at logon.</summary>
    void SetEnabled(bool enabled);
}

/// <summary>
/// Registers CenterConsole to launch at logon via a Scheduled Task rather than the classic
/// HKCU...\Run registry key. CenterConsole's manifest requires administrator, and a Run-key entry
/// for an elevated app re-triggers a UAC consent prompt on every single login; a task with
/// /RL HIGHEST launches pre-elevated instead, because the consent already happened when this
/// (already-elevated) process registered it.
/// </summary>
public sealed class StartupService : IStartupService
{
    private const string TaskName = "CenterConsole";

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            string exePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("Could not determine the running executable's path.");

            RunSchTasks($"/Create /TN \"{TaskName}\" /TR \"\\\"{exePath}\\\"\" /SC ONLOGON /RL HIGHEST /F");
        }
        else
        {
            // Exit code is 0 if the task existed and was removed, non-zero (e.g. "not found") if there
            // was nothing to remove - either way there's nothing further to do.
            RunSchTasks($"/Delete /TN \"{TaskName}\" /F");
        }
    }

    private static void RunSchTasks(string arguments)
    {
        var startInfo = new ProcessStartInfo("schtasks.exe", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(startInfo)!;
        process.WaitForExit();
    }
}
