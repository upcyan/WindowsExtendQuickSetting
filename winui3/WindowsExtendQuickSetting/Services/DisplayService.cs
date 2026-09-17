using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WindowsEthernetControl.Services;

public static class DisplayService
{
    private const uint QdcOnlyActivePaths = 0x2;
    private const uint GetAdvancedColorInfo = 9;
    private const uint SetAdvancedColorState = 10;
    public static bool LastModeWasExtend { get; private set; } = true;

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
            var script = "$devices=@(Get-PnpDevice -PresentOnly:$false | Where-Object { ($_.Class -eq 'Display' -or $_.Class -eq 'Monitor') -and $_.FriendlyName -match 'Virtual|Indirect|IDD' }); if($devices.Count -eq 0){exit 2}; $devices | Enable-PnpDevice -Confirm:$false -ErrorAction Stop; Start-Sleep -Milliseconds 500; $ready=@($devices | ForEach-Object { Get-PnpDevice -InstanceId $_.InstanceId -ErrorAction SilentlyContinue } | Where-Object Status -eq 'OK'); if($ready.Count -eq 0){exit 3}";
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe", Arguments = $"-NoProfile -NonInteractive -Command \"{script}\"",
                UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden
            });
            process?.WaitForExit();
            return process?.ExitCode == 0;
        }
        catch { return false; }
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
        LastModeWasExtend = extended;
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
