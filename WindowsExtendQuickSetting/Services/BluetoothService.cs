using Windows.Devices.Radios;

namespace WindowsEthernetControl.Services;

public static class BluetoothService
{
    public static async Task<Radio?> GetRadioAsync()
    {
        var access = await Radio.RequestAccessAsync();
        if (access != RadioAccessStatus.Allowed) return null;
        return (await Radio.GetRadiosAsync()).FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
    }

    public static async Task<bool> SetEnabledAsync(Radio radio, bool enabled) =>
        await radio.SetStateAsync(enabled ? RadioState.On : RadioState.Off) == RadioAccessStatus.Allowed;
}
