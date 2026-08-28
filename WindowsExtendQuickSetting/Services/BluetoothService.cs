using Windows.Devices.Radios;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using System.Diagnostics;

namespace WindowsEthernetControl.Services;

public static class BluetoothService
{
    public sealed class AdapterEntry
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required Radio Radio { get; init; }
    }

    public sealed class DeviceEntry
    {
        public required DeviceInformation Device { get; init; }
        public string Id => Device.Id;
        public string Name => string.IsNullOrWhiteSpace(Device.Name) ? "Bluetooth device" : Device.Name;
        public bool IsPaired => Device.Pairing.IsPaired;
        public bool IsConnected => Device.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var value) && value is true;
    }

    public static async Task<Radio?> GetRadioAsync()
        => await GetRadioAsync(RadioKind.Bluetooth);

    public static async Task<List<Radio>> GetBluetoothRadiosAsync()
    {
        var access = await Radio.RequestAccessAsync();
        if (access != RadioAccessStatus.Allowed) return new List<Radio>();
        return (await Radio.GetRadiosAsync()).Where(radio => radio.Kind == RadioKind.Bluetooth).ToList();
    }

    public static async Task<List<AdapterEntry>> GetBluetoothAdaptersAsync()
    {
        var result = new List<AdapterEntry>();
        try
        {
            var properties = new[] { "System.Devices.ModelName", "System.ItemNameDisplay" };
            var devices = await DeviceInformation.FindAllAsync(BluetoothAdapter.GetDeviceSelector(), properties);
            foreach (var device in devices)
            {
                var adapter = await BluetoothAdapter.FromIdAsync(device.Id);
                var radio = adapter == null ? null : await adapter.GetRadioAsync();
                if (radio == null) continue;
                var model = device.Properties.TryGetValue("System.Devices.ModelName", out var value) ? value as string : null;
                var name = !string.IsNullOrWhiteSpace(model) && !string.Equals(model, device.Name, StringComparison.OrdinalIgnoreCase)
                    ? $"{device.Name} · {model}" : device.Name;
                result.Add(new AdapterEntry { Id = device.Id, Name = string.IsNullOrWhiteSpace(name) ? radio.Name : name, Radio = radio });
            }
        }
        catch { }
        if (result.Count == 0)
            result.AddRange((await GetBluetoothRadiosAsync()).Select((radio, index) => new AdapterEntry { Id = $"radio-{index}", Name = radio.Name, Radio = radio }));
        return result;
    }

    public static async Task<bool> SwitchBluetoothAdapterAsync(AdapterEntry target, IReadOnlyList<AdapterEntry> adapters)
    {
        foreach (var adapter in adapters)
        {
            if (adapter.Id != target.Id && adapter.Radio.State == RadioState.On && !await SetEnabledAsync(adapter.Radio, false))
                return false;
        }
        return await SetEnabledAsync(target.Radio, true);
    }

    public static async Task<Radio?> GetWifiRadioAsync()
        => await GetRadioAsync(RadioKind.WiFi);

    private static async Task<Radio?> GetRadioAsync(RadioKind kind)
    {
        var access = await Radio.RequestAccessAsync();
        if (access != RadioAccessStatus.Allowed) return null;
        return (await Radio.GetRadiosAsync()).FirstOrDefault(r => r.Kind == kind);
    }

    public static async Task<bool> SetEnabledAsync(Radio radio, bool enabled) =>
        await radio.SetStateAsync(enabled ? RadioState.On : RadioState.Off) == RadioAccessStatus.Allowed;

    public static async Task<bool> SetExclusivelyEnabledAsync(Radio target, bool enabled)
    {
        if (!enabled) return await SetEnabledAsync(target, false);
        foreach (var radio in await GetBluetoothRadiosAsync())
        {
            if (!string.Equals(radio.Name, target.Name, StringComparison.OrdinalIgnoreCase) && radio.State == RadioState.On &&
                await radio.SetStateAsync(RadioState.Off) != RadioAccessStatus.Allowed)
                return false;
        }
        return await SetEnabledAsync(target, true);
    }

    public static async Task<List<DeviceEntry>> GetDevicesAsync()
    {
        var properties = new[] { "System.Devices.Aep.IsConnected" };
        var selectors = new[]
        {
            BluetoothDevice.GetDeviceSelectorFromPairingState(true),
            BluetoothDevice.GetDeviceSelectorFromPairingState(false),
            BluetoothLEDevice.GetDeviceSelectorFromPairingState(true),
            BluetoothLEDevice.GetDeviceSelectorFromPairingState(false)
        };
        var devices = new Dictionary<string, DeviceInformation>(StringComparer.OrdinalIgnoreCase);
        foreach (var selector in selectors)
        {
            try
            {
                var found = await DeviceInformation.FindAllAsync(selector, properties, DeviceInformationKind.AssociationEndpoint);
                foreach (var device in found)
                    devices[device.Id] = device;
            }
            catch { }
        }
        return devices.Values
            .Where(device => !string.IsNullOrWhiteSpace(device.Name))
            .Select(device => new DeviceEntry { Device = device })
            .OrderByDescending(device => device.IsConnected)
            .ThenByDescending(device => device.IsPaired)
            .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static async Task<bool> PairAsync(DeviceEntry entry)
    {
        if (entry.IsPaired) return true;
        var result = await entry.Device.Pairing.PairAsync(DevicePairingProtectionLevel.Default);
        return result.Status is DevicePairingResultStatus.Paired or DevicePairingResultStatus.AlreadyPaired;
    }

    public static async Task<bool> UnpairAsync(DeviceEntry entry)
    {
        if (!entry.IsPaired) return true;
        var result = await entry.Device.Pairing.UnpairAsync();
        return result.Status is DeviceUnpairingResultStatus.Unpaired or DeviceUnpairingResultStatus.AlreadyUnpaired;
    }

    public static bool LaunchFileTransfer(bool receive)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "fsquirt.exe",
                Arguments = receive ? "-receive" : "-send",
                UseShellExecute = true
            });
            return true;
        }
        catch { return false; }
    }
}
