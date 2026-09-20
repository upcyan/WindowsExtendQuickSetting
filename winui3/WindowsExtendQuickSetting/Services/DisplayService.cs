using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WindowsEthernetControl.Services;

public static class DisplayService
{
    private const uint QdcOnlyActivePaths = 0x2;
    private const uint GetAdvancedColorInfo = 9;
    private const uint SetAdvancedColorState = 10;
    private const string SettingsKey = @"HKEY_CURRENT_USER\Software\WindowsExtendQuickSetting.Native";
    private static DateTime _lastAutoAttempt = DateTime.MinValue;
    public static bool LastModeWasExtend { get; private set; } = true;

    public enum VirtualDriverState { Absent, Disabled, Ready }

    public static bool SwitchMode(bool extend)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "DisplaySwitch.exe"),
                Arguments = extend ? "/extend" : "/clone",
                UseShellExecute = true
            });
            LastModeWasExtend = extend;
            return true;
        }
        catch { return false; }
    }

    public static bool OpenLayout()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:display") { UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }

    public static bool EnableInstalledVirtualDisplay()
    {
        if (GetHdrState().ActiveDisplays != 0) return false;
        try
        {
            var script = "$devices=@(Get-PnpDevice | Where-Object { ($_.Class -eq 'Display' -or $_.Class -eq 'Monitor') -and $_.FriendlyName -match '" + VirtualDriverMatch + "' }); if($devices.Count -eq 0){exit 2}; $devices | Enable-PnpDevice -Confirm:$false -ErrorAction Stop; Start-Sleep -Milliseconds 500; $ready=@($devices | ForEach-Object { Get-PnpDevice -InstanceId $_.InstanceId -ErrorAction SilentlyContinue } | Where-Object Status -eq 'OK'); if($ready.Count -eq 0){exit 3}";
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe", Arguments = $"-NoProfile -NonInteractive -Command \"{script}\"",
                UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden
            });
            if (process == null) return false;
            // An enable that takes 3 minutes is not coming back; kill it instead
            // of blocking forever (caller runs this off the UI thread).
            if (!process.WaitForExit(180000))
            {
                try { process.Kill(true); } catch { }
                return false;
            }
            return process.ExitCode == 0;
        }
        catch { return false; }
    }

    // Matches IddCx-style virtual display adapters from any vendor: Parsec,
    // ToDesk, usbmmidd, spacedesk, open-source VirtualDisplayDriver, etc.
    private const string VirtualDriverMatch = "Virtual|Indirect|IDD|Parsec|ToDesk|usbmmidd|spacedesk|SuperDisplay|Duet|VDD";

    // Non-elevated probe for installed IddCx-style virtual display devices.
    // Present-only, matching the native build's DIGCF_PRESENT enumeration, so
    // leftover non-present device records never read as "Disabled".
    public static VirtualDriverState GetVirtualDriverState()
    {
        try
        {
            var script = "$d=@(Get-PnpDevice | Where-Object { ($_.Class -eq 'Display' -or $_.Class -eq 'Monitor') -and $_.FriendlyName -match '" + VirtualDriverMatch + "' }); if($d.Count -eq 0){exit 2}; if(@($d | Where-Object Status -eq 'OK').Count -gt 0){exit 0}else{exit 1}";
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe", Arguments = $"-NoProfile -NonInteractive -Command \"{script}\"",
                UseShellExecute = false, CreateNoWindow = true
            });
            if (process == null) return VirtualDriverState.Absent;
            // Reading ExitCode on a timed-out process throws; kill and report a
            // clean Absent instead so callers can branch on the enum.
            if (!process.WaitForExit(30000))
            {
                try { process.Kill(true); } catch { }
                return VirtualDriverState.Absent;
            }
            return process.ExitCode switch
            {
                0 => VirtualDriverState.Ready,
                1 => VirtualDriverState.Disabled,
                _ => VirtualDriverState.Absent
            };
        }
        catch { return VirtualDriverState.Absent; }
    }

    // Installs a signed driver package dropped into the "drivers" folder next
    // to the executable (pnputil, elevated). Any IddCx-compatible INF works.
    public static bool InstallVirtualDisplayDriver()
    {
        try
        {
            var dir = AppContext.BaseDirectory;
            foreach (var pattern in new[] { "drivers", "." })
            {
                var inf = Directory.GetFiles(Path.Combine(dir, pattern), "*.inf").FirstOrDefault();
                if (inf == null) continue;
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "pnputil.exe", Arguments = $"/add-driver \"{inf}\" /install",
                    UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden
                });
                process?.WaitForExit(180000);
                return process is { HasExited: true, ExitCode: 0 };
            }
            return false;
        }
        catch { return false; }
    }

    // Shared with the native build so both front-ends honor the same setting.
    public static bool AutoEnable
    {
        get => Registry.GetValue(SettingsKey, "VddAutoEnable", 0) is int value && value != 0;
        set => Registry.SetValue(SettingsKey, "VddAutoEnable", value ? 1 : 0, RegistryValueKind.DWord);
    }

    // Auto-enable the virtual display when no physical monitor is active.
    // The 60s cooldown (extended to 10min after failures) keeps elevated
    // re-prompts from turning into a loop.
    public static bool TryAutoEnsureVirtualDisplay()
    {
        if (!AutoEnable || GetHdrState().ActiveDisplays != 0) return false;
        if ((DateTime.UtcNow - _lastAutoAttempt).TotalSeconds < 60) return false;
        _lastAutoAttempt = DateTime.UtcNow;
        if (GetVirtualDriverState() != VirtualDriverState.Disabled) return false;
        if (EnableInstalledVirtualDisplay()) return true;
        _lastAutoAttempt = DateTime.UtcNow.AddMinutes(9);
        return false;
    }

    public static (int ActiveDisplays, bool Supported, bool Enabled, bool Extended) GetHdrState()
    {
        if (!GetActivePaths(out var paths)) return (0, false, false, false);
        var supported = false;
        var enabled = false;
        var extended = paths.Length > 1;
        for (var i = 0; i < paths.Length && extended; i++)
        {
            for (var j = i + 1; j < paths.Length; j++)
            {
                var first = paths[i].SourceInfo;
                var second = paths[j].SourceInfo;
                if (first.Id == second.Id && first.AdapterId.LowPart == second.AdapterId.LowPart && first.AdapterId.HighPart == second.AdapterId.HighPart)
                {
                    extended = false;
                    break;
                }
            }
        }
        foreach (var path in paths)
        {
            var info = new GetAdvancedColor
            {
                Header = new DeviceInfoHeader { Type = GetAdvancedColorInfo, Size = (uint)Marshal.SizeOf<GetAdvancedColor>(), AdapterId = path.TargetInfo.AdapterId, Id = path.TargetInfo.Id }
            };
            if (DisplayConfigGetDeviceInfo(ref info.Header) == 0)
            {
                supported |= (info.Value & 1) != 0;
                enabled |= (info.Value & 2) != 0;
            }
        }
        return (paths.Length, supported, enabled, extended);
    }


    public static bool SetHdr(bool enabled)
    {
        if (!GetActivePaths(out var paths)) return false;
        var changed = false;
        foreach (var path in paths)
        {
            var state = new SetAdvancedColor
            {
                Header = new DeviceInfoHeader { Type = SetAdvancedColorState, Size = (uint)Marshal.SizeOf<SetAdvancedColor>(), AdapterId = path.TargetInfo.AdapterId, Id = path.TargetInfo.Id },
                Enable = enabled ? 1u : 0u
            };
            changed |= DisplayConfigSetDeviceInfo(ref state.Header) == 0;
        }
        if (!changed) return false;
        var actual = GetHdrState();
        return actual.Supported && actual.Enabled == enabled;
    }

    private static bool GetActivePaths(out PathInfo[] paths)
    {
        paths = Array.Empty<PathInfo>();
        if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var pathCount, out var modeCount) != 0) return false;
        paths = new PathInfo[pathCount];
        var modes = new ModeInfo[modeCount];
        return QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) == 0;
    }

    [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint LowPart; public int HighPart; }
    [StructLayout(LayoutKind.Sequential)] private struct Rational { public uint Numerator; public uint Denominator; }
    [StructLayout(LayoutKind.Sequential)] private struct SourceInfo { public Luid AdapterId; public uint Id; public uint ModeInfoIdx; public uint StatusFlags; }
    [StructLayout(LayoutKind.Sequential)] private struct TargetInfo { public Luid AdapterId; public uint Id; public uint ModeInfoIdx; public int OutputTechnology; public int Rotation; public int Scaling; public Rational RefreshRate; public int ScanLineOrdering; [MarshalAs(UnmanagedType.Bool)] public bool TargetAvailable; public uint StatusFlags; }
    [StructLayout(LayoutKind.Sequential)] private struct PathInfo { public SourceInfo SourceInfo; public TargetInfo TargetInfo; public uint Flags; }
    [StructLayout(LayoutKind.Explicit, Size = 64)] private struct ModeUnion { }
    [StructLayout(LayoutKind.Sequential)] private struct ModeInfo { public int InfoType; public uint Id; public Luid AdapterId; public ModeUnion Data; }
    [StructLayout(LayoutKind.Sequential)] private struct DeviceInfoHeader { public uint Type; public uint Size; public Luid AdapterId; public uint Id; }
    [StructLayout(LayoutKind.Sequential)] private struct GetAdvancedColor { public DeviceInfoHeader Header; public uint Value; public int ColorEncoding; public uint BitsPerColorChannel; }
    [StructLayout(LayoutKind.Sequential)] private struct SetAdvancedColor { public DeviceInfoHeader Header; public uint Enable; }

    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint paths, [Out] PathInfo[] pathInfo, ref uint modes, [Out] ModeInfo[] modeInfo, IntPtr topologyId);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref DeviceInfoHeader header);
    [DllImport("user32.dll")] private static extern int DisplayConfigSetDeviceInfo(ref DeviceInfoHeader header);
}
