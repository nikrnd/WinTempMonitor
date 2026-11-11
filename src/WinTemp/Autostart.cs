using System.Diagnostics;

namespace WinTemp;

/// <summary>
/// Avvio automatico tramite Utilità di pianificazione: serve perché l'app richiede
/// privilegi di amministratore, e una voce "Run" del registro mostrerebbe il prompt UAC a ogni accesso.
/// </summary>
public static class Autostart
{
    private const string TaskName = "WinTemp";

    public static bool IsEnabled() => Run($"/Query /TN \"{TaskName}\"") == 0;

    public static bool Enable()
    {
        var exe = Environment.ProcessPath ?? Application.ExecutablePath;
        return Run($"/Create /F /TN \"{TaskName}\" /SC ONLOGON /RL HIGHEST /DELAY 0000:15 /TR \"\\\"{exe}\\\"\"") == 0;
    }

    public static bool Disable() => Run($"/Delete /F /TN \"{TaskName}\"") == 0;

    private static int Run(string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("schtasks.exe", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
            p.WaitForExit(10000);
            return p.ExitCode;
        }
        catch { return -1; }
    }
}
