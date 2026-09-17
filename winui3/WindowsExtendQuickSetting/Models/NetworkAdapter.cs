using System.Net.NetworkInformation;

namespace WindowsEthernetControl.Models;

public class NetworkAdapter
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string InterfaceName { get; set; } = "";
    public NetworkInterfaceType Type { get; set; }
    public OperationalStatus Status { get; set; }
    public long Speed { get; set; }
    public PhysicalAddress? MacAddress { get; set; }
    public List<string> IpAddresses { get; set; } = new();
    public List<string> Gateways { get; set; } = new();
    public List<string> DnsServers { get; set; } = new();
    public bool IsEthernet => Type is NetworkInterfaceType.Ethernet
        or NetworkInterfaceType.GigabitEthernet
        or NetworkInterfaceType.FastEthernetFx;
    public bool IsWireless => Type == NetworkInterfaceType.Wireless80211;
    public bool IsUsbTethering
    {
        get
        {
            var value = $"{Name} {Description}";
            return IsEthernet && new[] { "rndis", "mobile", "android", "iphone", "apple", "tether" }
                .Any(keyword => value.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }
    }
    public bool IsUp => Status == OperationalStatus.Up;

    public string SpeedText => Speed switch
    {
        >= 1_000_000_000 => $"{Speed / 1_000_000_000} Gbps",
        >= 1_000_000 => $"{Speed / 1_000_000} Mbps",
        >= 1_000 => $"{Speed / 1_000} Kbps",
        _ => $"{Speed} bps"
    };

    public string GetDisplayInfo(string fallbackIp = "N/A")
    {
        var ip = IpAddresses.FirstOrDefault() ?? fallbackIp;
        return $"IP: {ip} | {SpeedText}";
    }
}
