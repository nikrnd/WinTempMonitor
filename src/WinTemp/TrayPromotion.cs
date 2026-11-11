using Microsoft.Win32;

namespace WinTemp;

/// <summary>
/// Windows 11 salva le icone del tray in HKCU\Control Panel\NotifyIconSettings\&lt;id&gt;.
/// Il valore DWORD "IsPromoted" = 1 mostra l'icona sempre sulla barra invece che dentro la freccetta.
/// La voce viene creata da Explorer solo dopo che l'icona è comparsa la prima volta.
/// </summary>
public static class TrayPromotion
{
    private const string Root = @"Control Panel\NotifyIconSettings";

    /// <summary>Imposta lo stato; restituisce false se Explorer non ha ancora creato la voce.</summary>
    public static bool TrySet(bool promoted)
    {
        bool found = false;
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(Root);
            if (root == null) return false;
            foreach (var name in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(name, writable: true);
                if (key?.GetValue("ExecutablePath") is not string path || !IsOurExe(path)) continue;
                found = true;
                if (key.GetValue("IsPromoted") is not int cur || cur != (promoted ? 1 : 0))
                    key.SetValue("IsPromoted", promoted ? 1 : 0, RegistryValueKind.DWord);
            }
        }
        catch { }
        return found;
    }

    private static bool IsOurExe(string registryPath)
    {
        var exe = Environment.ProcessPath;
        if (exe == null) return false;
        // il percorso può essere salvato con un GUID di cartella nota al posto di "C:\Program Files"
        var resolved = registryPath
            .Replace("{6D809377-6AF0-444B-8957-A3773F02200E}", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase)
            .Replace("{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), StringComparison.OrdinalIgnoreCase);
        return string.Equals(resolved, exe, StringComparison.OrdinalIgnoreCase);
    }
}
