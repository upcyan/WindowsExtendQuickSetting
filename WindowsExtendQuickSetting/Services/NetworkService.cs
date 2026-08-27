using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net;
using System.Text.RegularExpressions;
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

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.NetworkInterfaceType != NetworkInterfaceType.Ethernet &&
                nic.NetworkInterfaceType != NetworkInterfaceType.GigabitEthernet &&
                nic.NetworkInterfaceType != NetworkInterfaceType.FastEthernetFx &&
                nic.NetworkInterfaceType != NetworkInterfaceType.Wireless80211)
                continue;

            if (IsVirtualAdapter(nic))
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

    public static async Task<bool> EnableAdapterAsync(string interfaceName)
    {
        return await RunNetshCommand($"interface set interface {QuoteInterfaceName(interfaceName)} admin=enable");
    }

    public static async Task<bool> DisableAdapterAsync(string interfaceName)
    {
        return await RunNetshCommand($"interface set interface {QuoteInterfaceName(interfaceName)} admin=disable");
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
        var output = await RunNetshReadOnlyAsync("wlan show networks mode=bssid");
        return Regex.Matches(output ?? "", @"(?im)^s*SSIDs+d+s*:s*(.+?)s*$")
            .Select(match => match.Groups[1].Value.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static Task<bool> DisconnectWifiAsync(string interfaceName) =>
        RunNetshUserCommand($"wlan disconnect interface={QuoteInterfaceName(interfaceName)}");

    public static Task<bool> ConnectWifiAsync(string profileName, string interfaceName) =>
        RunNetshUserCommand($"wlan connect name={QuoteInterfaceName(profileName)} interface={QuoteInterfaceName(interfaceName)}");

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
}
