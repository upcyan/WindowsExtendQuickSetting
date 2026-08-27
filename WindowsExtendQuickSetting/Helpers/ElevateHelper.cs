using System.Diagnostics;
using System.Security.Principal;

namespace WindowsEthernetControl;

public static class ElevateHelper
{
    public static bool CheckElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static bool RequestElevation()
    {
        try
        {
            var exePath = Environment.ProcessPath ?? "";
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Verb = "runas",
                UseShellExecute = true,
                CreateNoWindow = true
            });
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
