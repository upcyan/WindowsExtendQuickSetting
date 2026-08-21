using System.IO;
using System.Text.Json;
using WindowsEthernetControl.Models;
using Microsoft.Win32;

namespace WindowsEthernetControl.Services;

public class SettingsService
{
    private const string SETTINGS_FILE = "settings.json";
    private AppSettings _settings = new();
    private string? _settingsPath;

    public AppSettings Settings => _settings;

    public void Load()
    {
        try
        {
            _settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WindowsExtendQuickSetting",
                SETTINGS_FILE);

            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                _settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch
        {
            _settings = new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            _settingsPath ??= Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WindowsExtendQuickSetting",
                SETTINGS_FILE);

            var dir = Path.GetDirectoryName(_settingsPath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);
        }
        catch { }
    }

    public void SetStartWithWindows(bool enabled)
    {
        _settings.StartWithWindows = enabled;
        Save();

        try
        {
            var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key != null)
            {
                if (enabled)
                {
                    var exePath = Environment.ProcessPath ?? "";
                    key.SetValue("WindowsExtendQuickSetting", $"\"{exePath}\"");
                }
                else
                {
                    key.DeleteValue("WindowsExtendQuickSetting", false);
                }
                key.Close();
            }
        }
        catch { }
    }

    public bool IsStartWithWindowsEnabled()
    {
        try
        {
            var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", false);
            if (key != null)
            {
                var value = key.GetValue("WindowsExtendQuickSetting");
                key.Close();
                return value != null;
            }
        }
        catch { }
        return false;
    }
}
