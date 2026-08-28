using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using WindowsEthernetControl.Models;

namespace WindowsEthernetControl.Services;

public class NetworkService
{
    private readonly System.Timers.Timer _refreshTimer;
    private readonly object _syncRoot = new();
    private List<NetworkAdapter> _adapters = new();

    public event EventHandler? AdaptersChanged;

    public IReadOnlyList<NetworkAdapter> Adapters
    {
        get { lock (_syncRoot) return _adapters.ToArray(); }
    }

    public NetworkService()
    {
        _refreshTimer = new System.Timers.Timer(3000);
        _refreshTimer.Elapsed += (s, e) => RefreshAdapters();
    }

    public void StartMonitoring()
    {
        RefreshAdapters();
        _refreshTimer.Start();
    }

    public void StopMonitoring() => _refreshTimer.Stop();

    public List<NetworkAdapter> GetEthernetAdapters()
    {
        lock (_syncRoot) return _adapters.Where(a => a.IsEthernet && !a.IsUsbTethering).ToList();
    }

    public List<NetworkAdapter> GetUsbTetheringAdapters()
    {
        lock (_syncRoot) return _adapters.Where(a => a.IsUsbTethering).ToList();
    }

    public List<NetworkAdapter> GetWirelessAdapters()
    {
        lock (_syncRoot) return _adapters.Where(a => a.IsWireless).ToList();
    }

    public NetworkAdapter? GetAdapterById(string id)
    {
        lock (_syncRoot) return _adapters.FirstOrDefault(a => a.Id == id);
    }

    public NetworkAdapter? GetSelectedAdapter()
    {
        lock (_syncRoot) return _adapters.FirstOrDefault(a => a.IsEthernet && a.IsUp);
    }

    public void RefreshAdapters()
    {
        var newAdapters = new List<NetworkAdapter>();
        var wlanInterfaceIds = GetWlanInterfaceIds();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.NetworkInterfaceType != NetworkInterfaceType.Ethernet &&
                nic.NetworkInterfaceType != NetworkInterfaceType.GigabitEthernet &&
                nic.NetworkInterfaceType != NetworkInterfaceType.FastEthernetFx &&
                nic.NetworkInterfaceType != NetworkInterfaceType.Wireless80211)
                continue;

            if (IsVirtualAdapter(nic) && !LooksLikeUsbTethering(nic))
                continue;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 &&
                wlanInterfaceIds.Count > 0 && !wlanInterfaceIds.Contains(NormalizeInterfaceId(nic.Id)))
                continue;

            var adapter = new NetworkAdapter
            {
                Id = nic.Id,
                Name = nic.Name,
                Description = nic.Description,
                InterfaceName = nic.Name,
                Type = nic.NetworkInterfaceType,
                Status = nic.OperationalStatus,
                Speed = nic.Speed,
                MacAddress = nic.GetPhysicalAddress()
            };

            try
            {
                var ipProps = nic.GetIPProperties();
                adapter.IpAddresses = ipProps.UnicastAddresses
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => a.Address.ToString())
                    .ToList();
                adapter.Gateways = ipProps.GatewayAddresses
                    .Select(g => g.Address.ToString())
                    .ToList();
                adapter.DnsServers = ipProps.DnsAddresses
                    .Where(d => d.AddressFamily == AddressFamily.InterNetwork)
                    .Select(d => d.ToString())
                    .ToList();
            }
            catch { }

            newAdapters.Add(adapter);
        }

        bool changed;
        lock (_syncRoot)
        {
            changed = newAdapters.Count != _adapters.Count ||
                      newAdapters.Any(na => !_adapters.Any(oa => oa.Id == na.Id &&
                          oa.Status == na.Status && oa.IpAddresses.SequenceEqual(na.IpAddresses)));
            _adapters = newAdapters;
        }

        if (changed)
            AdaptersChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsVirtualAdapter(NetworkInterface nic)
    {
        var name = (nic.Name + " " + nic.Description).ToLowerInvariant();
        var block = new[]
        {
            "virtual", "vethernet", "hyper-v", "loopback", "bluetooth",
            "tap", "tun", "vmware",
            "virtualbox", "docker", "ndis", "wfp", "windows filter",
            "microsoft wi-fi direct", "localhost", "isatap", "teredo",
            "6to4", "pseudo", "qos", "npcap", "packet driver",
            "miniport", "kernel debug", "network monitor"
        };
        return block.Any(b => name.Contains(b, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeUsbTethering(NetworkInterface nic)
    {
        var value = $"{nic.Name} {nic.Description}";
        return new[] { "rndis", "mobile", "android", "iphone", "apple", "tether" }
            .Any(keyword => value.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeInterfaceId(string id) => id.Trim().Trim('{', '}').ToUpperInvariant();

    private static HashSet<string> GetWlanInterfaceIds()
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IntPtr client = IntPtr.Zero;
        IntPtr list = IntPtr.Zero;
        try
        {
            if (WlanOpenHandle(2, IntPtr.Zero, out _, out client) != 0) return ids;
            if (WlanEnumInterfaces(client, IntPtr.Zero, out list) != 0 || list == IntPtr.Zero) return ids;
            var count = Marshal.ReadInt32(list);
            var itemSize = Marshal.SizeOf<WLAN_INTERFACE_INFO>();
            var current = IntPtr.Add(list, 8);
            for (var index = 0; index < count; index++)
            {
                var item = Marshal.PtrToStructure<WLAN_INTERFACE_INFO>(current);
                ids.Add(NormalizeInterfaceId(item.InterfaceGuid.ToString()));
                current = IntPtr.Add(current, itemSize);
            }
        }
        catch { }
        finally
        {
            if (list != IntPtr.Zero) WlanFreeMemory(list);
            if (client != IntPtr.Zero) WlanCloseHandle(client, IntPtr.Zero);
        }
        return ids;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WLAN_INTERFACE_INFO
    {
        public Guid InterfaceGuid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Description;
        public int State;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WLAN_PROFILE_INFO
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string ProfileName;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DOT11_SSID
    {
        public uint Length;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Value;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WLAN_AVAILABLE_NETWORK
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string ProfileName;
        public DOT11_SSID Ssid;
        public int BssType;
        public uint BssidCount;
        [MarshalAs(UnmanagedType.Bool)] public bool Connectable;
        public uint NotConnectableReason;
        public uint PhyTypeCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public uint[] PhyTypes;
        [MarshalAs(UnmanagedType.Bool)] public bool MorePhyTypes;
        public uint SignalQuality;
        [MarshalAs(UnmanagedType.Bool)] public bool SecurityEnabled;
        public int DefaultAuthAlgorithm;
        public int DefaultCipherAlgorithm;
        public uint Flags;
        public uint Reserved;
    }

    public sealed class SavedWifiProfile
    {
        public required string Name { get; init; }
        public Guid InterfaceId { get; init; }
    }

    [DllImport("wlanapi.dll")]
    private static extern uint WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiatedVersion, out IntPtr clientHandle);
    [DllImport("wlanapi.dll")]
    private static extern uint WlanEnumInterfaces(IntPtr clientHandle, IntPtr reserved, out IntPtr interfaceList);
    [DllImport("wlanapi.dll")]
    private static extern uint WlanCloseHandle(IntPtr clientHandle, IntPtr reserved);
    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(IntPtr memory);
    [DllImport("wlanapi.dll")]
    private static extern uint WlanGetProfileList(IntPtr clientHandle, ref Guid interfaceGuid, IntPtr reserved, out IntPtr profileList);
    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
    private static extern uint WlanGetProfile(IntPtr clientHandle, ref Guid interfaceGuid, string profileName, IntPtr reserved, out IntPtr profileXml, ref uint flags, out uint grantedAccess);
    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
    private static extern uint WlanDeleteProfile(IntPtr clientHandle, ref Guid interfaceGuid, string profileName, IntPtr reserved);
    [DllImport("wlanapi.dll")]
    private static extern uint WlanScan(IntPtr clientHandle, ref Guid interfaceGuid, IntPtr dot11Ssid, IntPtr ieData, IntPtr reserved);
    [DllImport("wlanapi.dll")]
    private static extern uint WlanGetAvailableNetworkList(IntPtr clientHandle, ref Guid interfaceGuid, uint flags, IntPtr reserved, out IntPtr availableNetworkList);
    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
    private static extern uint WlanSetProfile(IntPtr clientHandle, ref Guid interfaceGuid, uint flags, string profileXml, string? allUserProfileSecurity, [MarshalAs(UnmanagedType.Bool)] bool overwrite, IntPtr reserved, out uint reasonCode);

    public static Task<List<SavedWifiProfile>> GetSavedWifiProfilesAsync() => Task.Run(() =>
    {
        var result = new Dictionary<string, SavedWifiProfile>(StringComparer.OrdinalIgnoreCase);
        IntPtr client = IntPtr.Zero;
        IntPtr interfaces = IntPtr.Zero;
        try
        {
            if (WlanOpenHandle(2, IntPtr.Zero, out _, out client) != 0) return result.Values.ToList();
            if (WlanEnumInterfaces(client, IntPtr.Zero, out interfaces) != 0 || interfaces == IntPtr.Zero) return result.Values.ToList();
            var interfaceCount = Marshal.ReadInt32(interfaces);
            var interfaceSize = Marshal.SizeOf<WLAN_INTERFACE_INFO>();
            var interfacePointer = IntPtr.Add(interfaces, 8);
            for (var interfaceIndex = 0; interfaceIndex < interfaceCount; interfaceIndex++)
            {
                var wlanInterface = Marshal.PtrToStructure<WLAN_INTERFACE_INFO>(interfacePointer);
                IntPtr profiles = IntPtr.Zero;
                try
                {
                    if (WlanGetProfileList(client, ref wlanInterface.InterfaceGuid, IntPtr.Zero, out profiles) == 0 && profiles != IntPtr.Zero)
                    {
                        var profileCount = Marshal.ReadInt32(profiles);
                        var profileSize = Marshal.SizeOf<WLAN_PROFILE_INFO>();
                        var profilePointer = IntPtr.Add(profiles, 8);
                        for (var profileIndex = 0; profileIndex < profileCount; profileIndex++)
                        {
                            var profile = Marshal.PtrToStructure<WLAN_PROFILE_INFO>(profilePointer);
                            if (!string.IsNullOrWhiteSpace(profile.ProfileName))
                                result[$"{wlanInterface.InterfaceGuid:N}|{profile.ProfileName}"] = new SavedWifiProfile { Name = profile.ProfileName, InterfaceId = wlanInterface.InterfaceGuid };
                            profilePointer = IntPtr.Add(profilePointer, profileSize);
                        }
                    }
                }
                finally { if (profiles != IntPtr.Zero) WlanFreeMemory(profiles); }
                interfacePointer = IntPtr.Add(interfacePointer, interfaceSize);
            }
        }
        catch { }
        finally
        {
            if (interfaces != IntPtr.Zero) WlanFreeMemory(interfaces);
            if (client != IntPtr.Zero) WlanCloseHandle(client, IntPtr.Zero);
        }
        return result.Values.OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    });

    public static Task<string?> GetSavedWifiPasswordAsync(SavedWifiProfile profile) => Task.Run(() =>
    {
        IntPtr client = IntPtr.Zero;
        IntPtr xmlPointer = IntPtr.Zero;
        try
        {
            if (WlanOpenHandle(2, IntPtr.Zero, out _, out client) != 0) return null;
            var interfaceId = profile.InterfaceId;
            uint flags = 4; // WLAN_PROFILE_GET_PLAINTEXT_KEY; requires elevated access.
            if (WlanGetProfile(client, ref interfaceId, profile.Name, IntPtr.Zero, out xmlPointer, ref flags, out _) != 0 || xmlPointer == IntPtr.Zero) return null;
            var xml = Marshal.PtrToStringUni(xmlPointer) ?? "";
            var match = Regex.Match(xml, @"(?is)<keyMaterial>(.*?)</keyMaterial>");
            return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : "";
        }
        catch { return null; }
        finally
        {
            if (xmlPointer != IntPtr.Zero) WlanFreeMemory(xmlPointer);
            if (client != IntPtr.Zero) WlanCloseHandle(client, IntPtr.Zero);
        }
    });

    public static Task<bool> DeleteSavedWifiProfileAsync(SavedWifiProfile profile) => Task.Run(() =>
    {
        IntPtr client = IntPtr.Zero;
        try
        {
            if (WlanOpenHandle(2, IntPtr.Zero, out _, out client) != 0) return false;
            var interfaceId = profile.InterfaceId;
            return WlanDeleteProfile(client, ref interfaceId, profile.Name, IntPtr.Zero) == 0;
        }
        catch { return false; }
        finally { if (client != IntPtr.Zero) WlanCloseHandle(client, IntPtr.Zero); }
    });

    public static Task<string?> ExportSavedWifiProfileAsync(SavedWifiProfile profile) => Task.Run(() =>
    {
        IntPtr client = IntPtr.Zero;
        IntPtr xmlPointer = IntPtr.Zero;
        try
        {
            if (WlanOpenHandle(2, IntPtr.Zero, out _, out client) != 0) return null;
            var interfaceId = profile.InterfaceId;
            uint flags = 0;
            return WlanGetProfile(client, ref interfaceId, profile.Name, IntPtr.Zero, out xmlPointer, ref flags, out _) == 0 && xmlPointer != IntPtr.Zero
                ? Marshal.PtrToStringUni(xmlPointer) : null;
        }
        catch { return null; }
        finally
        {
            if (xmlPointer != IntPtr.Zero) WlanFreeMemory(xmlPointer);
            if (client != IntPtr.Zero) WlanCloseHandle(client, IntPtr.Zero);
        }
    });

    public static Task<bool> RestoreSavedWifiProfileAsync(SavedWifiProfile profile, string xml) => Task.Run(() =>
    {
        IntPtr client = IntPtr.Zero;
        try
        {
            if (WlanOpenHandle(2, IntPtr.Zero, out _, out client) != 0) return false;
            var interfaceId = profile.InterfaceId;
            return WlanSetProfile(client, ref interfaceId, 0, xml, null, true, IntPtr.Zero, out _) == 0;
        }
        catch { return false; }
        finally { if (client != IntPtr.Zero) WlanCloseHandle(client, IntPtr.Zero); }
    });

    public static async Task<bool> EnableAdapterAsync(string interfaceName)
    {
        return await RunNetshCommand($"interface set interface {QuoteInterfaceName(interfaceName)} admin=enable");
    }

    public static async Task<bool> DisableAdapterAsync(string interfaceName)
    {
        return await RunNetshCommand($"interface set interface {QuoteInterfaceName(interfaceName)} admin=disable");
    }

    public static async Task<int?> GetInterfaceMetricAsync(string interfaceName)
    {
        try
        {
            var escapedName = interfaceName.Replace("'", "''");
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -Command \"Get-NetIPInterface -InterfaceAlias '{escapedName}' -AddressFamily IPv4 -ErrorAction SilentlyContinue | Sort-Object InterfaceMetric | Select-Object -First 1 -ExpandProperty InterfaceMetric\"",
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
            });
            if (process == null) return null;
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return process.ExitCode == 0 && int.TryParse(output.Trim(), out var metric) ? metric : null;
        }
        catch { return null; }
    }

    public static async Task<bool> SetInterfaceMetricAsync(string interfaceName, int metric)
    {
        if (metric is < 1 or > 9999) return false;
        var ipv4 = await RunNetshCommand($"interface ipv4 set interface {QuoteInterfaceName(interfaceName)} metric={metric}");
        await RunNetshCommand($"interface ipv6 set interface {QuoteInterfaceName(interfaceName)} metric={metric}");
        return ipv4;
    }

    public static Task<bool> SetDohEnabledAsync(bool enabled) =>
        RunNetshCommand($"dns set global doh={(enabled ? "yes" : "no")}");

    public static Task<bool> AddDohServerAsync(string server, string template) =>
        RunNetshCommand($"dns add encryption server={server} dohtemplate=\"{template}\" autoupgrade=yes udpfallback=no");

    public static Task<bool> RemoveDohServerAsync(string server) =>
        RunNetshCommand($"dns delete encryption server={server}");

    public static bool IsValidDohServer(string server, string template) =>
        IPAddress.TryParse(server, out _) &&
        Uri.TryCreate(template, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    public static async Task<string?> GetConnectedWifiSsidAsync()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = "wlan show interfaces",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            });
            if (process == null) return null;
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0) return null;
            var match = Regex.Match(output, @"(?im)^\s*SSID\s*:\s*(.+?)\s*$");
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }
        catch { return null; }
    }

    public static async Task<List<string>> GetAvailableWifiNetworksAsync()
    {
        return await Task.Run(async () =>
        {
            var result = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            IntPtr client = IntPtr.Zero;
            IntPtr interfaces = IntPtr.Zero;
            try
            {
                if (WlanOpenHandle(2, IntPtr.Zero, out _, out client) != 0 || WlanEnumInterfaces(client, IntPtr.Zero, out interfaces) != 0 || interfaces == IntPtr.Zero)
                    return result.Keys.ToList();
                var count = Marshal.ReadInt32(interfaces);
                var interfaceSize = Marshal.SizeOf<WLAN_INTERFACE_INFO>();
                var pointer = IntPtr.Add(interfaces, 8);
                var interfaceIds = new List<Guid>();
                for (var index = 0; index < count; index++)
                {
                    var item = Marshal.PtrToStructure<WLAN_INTERFACE_INFO>(pointer);
                    interfaceIds.Add(item.InterfaceGuid);
                    WlanScan(client, ref item.InterfaceGuid, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                    pointer = IntPtr.Add(pointer, interfaceSize);
                }
                await Task.Delay(1400);
                foreach (var interfaceIdValue in interfaceIds)
                {
                    var interfaceId = interfaceIdValue;
                    IntPtr networks = IntPtr.Zero;
                    try
                    {
                        if (WlanGetAvailableNetworkList(client, ref interfaceId, 3, IntPtr.Zero, out networks) != 0 || networks == IntPtr.Zero) continue;
                        var networkCount = Marshal.ReadInt32(networks);
                        var networkSize = Marshal.SizeOf<WLAN_AVAILABLE_NETWORK>();
                        var networkPointer = IntPtr.Add(networks, 8);
                        for (var index = 0; index < networkCount; index++)
                        {
                            var network = Marshal.PtrToStructure<WLAN_AVAILABLE_NETWORK>(networkPointer);
                            var length = (int)Math.Min(network.Ssid.Length, 32);
                            var name = length > 0 ? Encoding.UTF8.GetString(network.Ssid.Value, 0, length).TrimEnd('\0') : "";
                            if (!string.IsNullOrWhiteSpace(name) && (!result.TryGetValue(name, out var quality) || network.SignalQuality > quality))
                                result[name] = network.SignalQuality;
                            networkPointer = IntPtr.Add(networkPointer, networkSize);
                        }
                    }
                    finally { if (networks != IntPtr.Zero) WlanFreeMemory(networks); }
                }
            }
            catch { }
            finally
            {
                if (interfaces != IntPtr.Zero) WlanFreeMemory(interfaces);
                if (client != IntPtr.Zero) WlanCloseHandle(client, IntPtr.Zero);
            }
            return result.OrderByDescending(pair => pair.Value).Select(pair => pair.Key).ToList();
        });
    }

    public static Task<bool> DisconnectWifiAsync(string interfaceName) =>
        RunNetshUserCommand($"wlan disconnect interface={QuoteInterfaceName(interfaceName)}");

    public static async Task<bool> ConnectWifiAsync(string profileName, string interfaceName)
    {
        if (!await RunNetshUserCommand($"wlan connect name={QuoteInterfaceName(profileName)} interface={QuoteInterfaceName(interfaceName)}"))
            return false;
        return await WaitForWifiConnectionAsync(profileName);
    }

    public static async Task<bool> ConnectWifiWithPasswordAsync(string ssid, string password, string interfaceName)
    {
        if (string.IsNullOrWhiteSpace(ssid) || (!string.IsNullOrEmpty(password) && password.Length is < 8 or > 63))
            return false;

        var escapedSsid = SecurityElement.Escape(ssid) ?? string.Empty;
        var ssidHex = Convert.ToHexString(Encoding.UTF8.GetBytes(ssid));
        var security = string.IsNullOrEmpty(password)
            ? "<authEncryption><authentication>open</authentication><encryption>none</encryption><useOneX>false</useOneX></authEncryption>"
            : $"<authEncryption><authentication>WPA2PSK</authentication><encryption>AES</encryption><useOneX>false</useOneX></authEncryption><sharedKey><keyType>passPhrase</keyType><protected>false</protected><keyMaterial>{SecurityElement.Escape(password)}</keyMaterial></sharedKey>";
        var profile = $"""<?xml version="1.0" encoding="UTF-8"?><WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1"><name>{escapedSsid}</name><SSIDConfig><SSID><hex>{ssidHex}</hex><name>{escapedSsid}</name></SSID></SSIDConfig><connectionType>ESS</connectionType><connectionMode>auto</connectionMode><MSM><security>{security}</security></MSM></WLANProfile>""";
        var profilePath = Path.Combine(Path.GetTempPath(), $"WindowsExtendQuickSetting-{Guid.NewGuid():N}.xml");
        try
        {
            await File.WriteAllTextAsync(profilePath, profile, new UTF8Encoding(false));
            var added = await RunNetshUserCommand($"wlan add profile filename={QuoteInterfaceName(profilePath)} interface={QuoteInterfaceName(interfaceName)} user=current");
            return added && await ConnectWifiAsync(ssid, interfaceName);
        }
        finally
        {
            try { File.Delete(profilePath); } catch { }
        }
    }

    public static Task<bool> ForgetWifiProfileAsync(string ssid, string interfaceName) =>
        RunNetshUserCommand($"wlan delete profile name={QuoteInterfaceName(ssid)} interface={QuoteInterfaceName(interfaceName)}");

    private static async Task<bool> WaitForWifiConnectionAsync(string expectedSsid)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            await Task.Delay(500);
            var connected = await GetConnectedWifiSsidAsync();
            if (string.Equals(connected, expectedSsid, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public enum UsbTetheringResult
    {
        Enabled,
        EnabledWaitingForPhone,
        Disabled,
        AdapterNotFound,
        Failed
    }

    public async Task<UsbTetheringResult> SetUsbTetheringEnabledAsync(bool enabled)
    {
        RefreshAdapters();
        var adapter = GetUsbTetheringAdapters().FirstOrDefault(a => a.IsUp) ?? GetUsbTetheringAdapters().FirstOrDefault();
        if (adapter == null) return UsbTetheringResult.AdapterNotFound;

        var changed = enabled
            ? await EnableAdapterAsync(adapter.InterfaceName)
            : await DisableAdapterAsync(adapter.InterfaceName);
        if (!changed) return UsbTetheringResult.Failed;
        if (!enabled)
        {
            RefreshAdapters();
            return UsbTetheringResult.Disabled;
        }

        for (var attempt = 0; attempt < 20; attempt++)
        {
            await Task.Delay(500);
            RefreshAdapters();
            var current = GetAdapterById(adapter.Id);
            if (current?.IsUp == true && current.IpAddresses.Count > 0 && current.Gateways.Count > 0)
                return UsbTetheringResult.Enabled;
        }
        return UsbTetheringResult.EnabledWaitingForPhone;
    }

    private static async Task<string?> RunNetshReadOnlyAsync(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo { FileName = "netsh", Arguments = arguments, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
            if (process == null) return null;
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return process.ExitCode == 0 ? output : null;
        }
        catch { return null; }
    }

    private static async Task<bool> RunNetshUserCommand(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo { FileName = "netsh", Arguments = arguments, UseShellExecute = false, CreateNoWindow = true });
            if (process == null) return false;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch { return false; }
    }

    private static async Task<bool> RunNetshCommand(string arguments)
    {
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = arguments,
                Verb = "runas",
                CreateNoWindow = true,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(info);
            if (process != null)
            {
                await process.WaitForExitAsync();
                return process.ExitCode == 0;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static string QuoteInterfaceName(string interfaceName) =>
        $"\"{interfaceName.Replace("\"", "\\\"")}\"";

    public enum WifiState
    {
        Disabled,
        EnabledDisconnected,
        Connected
    }

    public static async Task<(WifiState State, string? Ssid, string? InterfaceName)> GetWifiStateAsync()
    {
        try
        {
            var output = await RunNetshReadOnlyAsync("wlan show interfaces");
            if (string.IsNullOrWhiteSpace(output))
                return (WifiState.Disabled, null, null);
            if (output.Contains("no wireless interface", StringComparison.OrdinalIgnoreCase))
                return (WifiState.Disabled, null, null);
            var nameMatch = Regex.Match(output, @"(?im)^\s*Name\s*:\s*(.+?)\s*$");
            if (!nameMatch.Success)
                return (WifiState.Disabled, null, null);
            var iface = nameMatch.Groups[1].Value.Trim();
            var stateMatch = Regex.Match(output, @"(?im)^\s*State\s*:\s*(.+?)\s*$");
            var stateText = stateMatch.Success ? stateMatch.Groups[1].Value.Trim() : string.Empty;
            if (stateText.IndexOf("connected", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var ssidMatch = Regex.Match(output, @"(?im)^\s*SSID\s*:\s*(.+?)\s*$");
                return (WifiState.Connected, ssidMatch.Success ? ssidMatch.Groups[1].Value.Trim() : null, iface);
            }
            return (WifiState.EnabledDisconnected, null, iface);
        }
        catch
        {
            return (WifiState.Disabled, null, null);
        }
    }

    public static async Task<List<SystemDohServer>> GetSystemDohServersAsync()
    {
        var registryServers = ReadSystemDohServersFromRegistry();
        if (registryServers.Count > 0) return registryServers;

        // Fallback for systems where registry access is restricted. This parser is
        // retained for English netsh output, while the registry path is locale-neutral.
        var output = await RunNetshReadOnlyAsync("dns show encryption");
        var list = new List<SystemDohServer>();
        if (string.IsNullOrWhiteSpace(output)) return list;
        var parts = Regex.Split(output, @"(?im)^\s*Encryption settings for\s+(\S+)");
        for (var i = 1; i < parts.Length; i += 2)
        {
            var server = parts[i].Trim();
            var block = i + 1 < parts.Length ? parts[i + 1] : string.Empty;
            if (string.IsNullOrWhiteSpace(server)) continue;
            var tpl = Regex.Match(block, @"(?im)DNS-over-HTTPS template\s*:\s*(\S+)");
            var au = Regex.Match(block, @"(?im)Auto-upgrade\s*:\s*(\S+)");
            var udp = Regex.Match(block, @"(?im)UDP-fallback\s*:\s*(\S+)");
            list.Add(new SystemDohServer
            {
                Server = server,
                Template = tpl.Success ? tpl.Groups[1].Value.Trim() : string.Empty,
                AutoUpgrade = au.Success && au.Groups[1].Value.Equals("yes", StringComparison.OrdinalIgnoreCase),
                UdpFallback = udp.Success && udp.Groups[1].Value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            });
        }
        return list;
    }

    private static List<SystemDohServer> ReadSystemDohServersFromRegistry()
    {
        const string path = @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters\DohWellKnownServers";
        var list = new List<SystemDohServer>();
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(path, false);
            if (root == null) return list;
            foreach (var server in root.GetSubKeyNames())
            {
                if (!IPAddress.TryParse(server, out _)) continue;
                using var key = root.OpenSubKey(server, false);
                var template = key?.GetValue("Template") as string;
                if (!IsValidDohServer(server, template ?? string.Empty)) continue;
                list.Add(new SystemDohServer { Server = server, Template = template! });
            }
        }
        catch { }
        return list.OrderBy(item => item.Server, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static (string Server, string Template)? ParseDohImportLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        var serverMatch = Regex.Match(line, @"(?i)(?:server|address|ip)\s*[:=]\s*([""']?)([^""'\s,;|]+)\1");
        var templateMatch = Regex.Match(line, @"(?i)(?:dohtemplate|template|url)\s*[:=]\s*([""']?)(https://[^""'\s,;|]+)\1");
        var server = serverMatch.Success ? serverMatch.Groups[2].Value.Trim() : string.Empty;
        var template = templateMatch.Success ? templateMatch.Groups[2].Value.Trim() : string.Empty;

        if (string.IsNullOrEmpty(template))
        {
            var url = Regex.Match(line, @"(?i)https://[^\s,""'|;]+", RegexOptions.CultureInvariant);
            if (url.Success) template = url.Value.Trim();
        }
        if (string.IsNullOrEmpty(server))
        {
            var tokens = Regex.Split(line, @"[\s,;|]+")
                .Select(token => token.Trim('"', '\'', '[', ']', '(', ')'));
            server = tokens.FirstOrDefault(token => IPAddress.TryParse(token, out _)) ?? string.Empty;
        }
        if (!IsValidDohServer(server, template)) return null;
        return (server, template);
    }

    public static async Task<bool> SetInterfaceDnsAsync(string interfaceName, string primaryIp, string? backupIp)
    {
        var setOk = await RunNetshCommand($"interface ip set dns name={QuoteInterfaceName(interfaceName)} static {primaryIp}");
        if (!setOk) return false;
        if (!string.IsNullOrWhiteSpace(backupIp))
            await RunNetshCommand($"interface ip add dns name={QuoteInterfaceName(interfaceName)} {backupIp} index=2");
        return true;
    }

    public string? GetActiveInterfaceName()
    {
        lock (_syncRoot)
        {
            var up = _adapters.Where(a => a.IsUp).ToList();
            var preferred = up.FirstOrDefault(a => a.IsEthernet && !a.IsUsbTethering)
                ?? up.FirstOrDefault(a => a.IsWireless)
                ?? up.FirstOrDefault(a => a.IsUsbTethering)
                ?? up.FirstOrDefault();
            return preferred?.InterfaceName;
        }
    }
}

public class SystemDohServer
{
    public string Server { get; set; } = string.Empty;
    public string Template { get; set; } = string.Empty;
    public bool AutoUpgrade { get; set; }
    public bool UdpFallback { get; set; }
}
