using System.Diagnostics;
using System.Windows.Forms;

namespace WindowsExtendQuickSetting.LiteLauncher;

internal static class Program
{
    private const string RuntimePackageId = "Microsoft.WindowsAppRuntime.1.6";
    private const string MainExecutable = "WindowsExtendQuickSetting.Lite.exe";

    [STAThread]
    private static async Task Main()
    {
        ApplicationConfiguration.Initialize();

        var appPath = Path.Combine(AppContext.BaseDirectory, MainExecutable);
        if (!File.Exists(appPath))
        {
            MessageBox.Show("WindowsExtendQuickSetting 的轻量主程序缺失。", "WindowsExtendQuickSetting",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (!await IsRuntimeInstalledAsync())
        {
            var result = MessageBox.Show(
                "首次运行需要安装 Microsoft Windows App Runtime 1.6。\n\n点击“确定”后将自动从 Microsoft 官方源下载并静默安装；安装完成后会自动启动程序。",
                "WindowsExtendQuickSetting",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Information);
            if (result != DialogResult.OK) return;

            if (!await InstallRuntimeAsync())
            {
                MessageBox.Show("Windows App Runtime 安装失败。请检查网络连接、Microsoft App Installer（winget）和管理员权限。",
                    "WindowsExtendQuickSetting", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }

        Process.Start(new ProcessStartInfo { FileName = appPath, UseShellExecute = true });
        Environment.Exit(0);
    }

    private static async Task<bool> IsRuntimeInstalledAsync()
    {
        var result = await RunWingetAsync($"list --id {RuntimePackageId} --exact --accept-source-agreements");
        return result.ExitCode == 0 && result.Output.Contains(RuntimePackageId, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> InstallRuntimeAsync()
    {
        var result = await RunWingetAsync($"install --id {RuntimePackageId} --exact --silent --accept-package-agreements --accept-source-agreements");
        return result.ExitCode == 0;
    }

    private static async Task<(int ExitCode, string Output)> RunWingetAsync(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (process == null) return (-1, "");
            var output = await process.StandardOutput.ReadToEndAsync();
            output += await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return (process.ExitCode, output);
        }
        catch { return (-1, ""); }
    }
}
