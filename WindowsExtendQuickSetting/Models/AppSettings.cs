namespace WindowsEthernetControl.Models;

public class AppSettings
{
    public bool StartWithWindows { get; set; } = false;
    public bool ShowEthernetCard { get; set; } = true;
    public string Language { get; set; } = "zh-CN";
    public string BackdropEffect { get; set; } = "Acrylic";
    public bool DnsOverHttpsEnabled { get; set; }
    public List<DohServerEntry> DohServers { get; set; } = new();
    public string? LastSelectedAdapterId { get; set; }
    public string? PrimaryDohServer { get; set; }
    public string? BackupDohServer { get; set; }
}
