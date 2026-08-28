#include <windows.h>
#include <commctrl.h>
#include <wininet.h>
#include <wincrypt.h>
#include <filesystem>
#include <string>
#include <thread>
#include <vector>

namespace
{
constexpr wchar_t kAppName[] = L"WindowsExtendQuickSetting";
constexpr int kMainResourceId = 101;
constexpr UINT kStatusMessage = WM_APP + 1;
constexpr UINT kCompletedMessage = WM_APP + 2;
constexpr UINT kProgressMessage = WM_APP + 3;
constexpr int kStatusControlId = 1001;
constexpr int kProgressControlId = 1002;
constexpr wchar_t kDotNetUrl[] = L"https://aka.ms/dotnet/9.0/windowsdesktop-runtime-win-x64.exe";
constexpr wchar_t kAppRuntimeUrl[] = L"https://download.microsoft.com/download/8a854124-ff27-48d0-b82e-e819feb247bd/WindowsAppRuntimeInstall-x64.exe";
constexpr wchar_t kDotNetWingetId[] = L"Microsoft.DotNet.DesktopRuntime.9";
constexpr wchar_t kAppRuntimeWingetId[] = L"Microsoft.WindowsAppRuntime.1.6";

struct InstallContext
{
    HWND window{};
    bool succeeded{};
    bool installDotNet{};
    bool installAppRuntime{};
};

HBRUSH ProgressBackground()
{
    static HBRUSH brush = CreateSolidBrush(RGB(248, 249, 252));
    return brush;
}

HFONT ProgressFont(int height, int weight)
{
    return CreateFontW(height, 0, 0, 0, weight, FALSE, FALSE, FALSE, DEFAULT_CHARSET,
        OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY,
        DEFAULT_PITCH | FF_DONTCARE, L"Segoe UI");
}

LRESULT CALLBACK ProgressWindowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam)
{
    switch (message)
    {
    case WM_CREATE:
    {
        const HWND title = CreateWindowW(L"STATIC", L"正在准备 WindowsExtendQuickSetting", WS_CHILD | WS_VISIBLE,
            28, 28, 504, 30, window, nullptr, nullptr, nullptr);
        SendMessageW(title, WM_SETFONT, reinterpret_cast<WPARAM>(ProgressFont(-21, FW_SEMIBOLD)), TRUE);
        const HWND status = CreateWindowW(L"STATIC", L"准备下载…", WS_CHILD | WS_VISIBLE,
            28, 70, 504, 26, window, reinterpret_cast<HMENU>(static_cast<INT_PTR>(kStatusControlId)), nullptr, nullptr);
        SendMessageW(status, WM_SETFONT, reinterpret_cast<WPARAM>(ProgressFont(-14, FW_NORMAL)), TRUE);
        CreateWindowW(PROGRESS_CLASSW, nullptr, WS_CHILD | WS_VISIBLE | PBS_SMOOTH,
            28, 116, 504, 8, window, reinterpret_cast<HMENU>(static_cast<INT_PTR>(kProgressControlId)), nullptr, nullptr);
        SendMessageW(GetDlgItem(window, kProgressControlId), PBM_SETRANGE32, 0, 100);
        SendMessageW(GetDlgItem(window, kProgressControlId), PBM_SETPOS, 2, 0);
        const HWND hint = CreateWindowW(L"STATIC", L"来自 Microsoft 官方源。请保持网络连接，完成后会自动启动。", WS_CHILD | WS_VISIBLE,
            28, 150, 504, 24, window, nullptr, nullptr, nullptr);
        SendMessageW(hint, WM_SETFONT, reinterpret_cast<WPARAM>(ProgressFont(-13, FW_NORMAL)), TRUE);
        return 0;
    }
    case kStatusMessage:
        SetWindowTextW(GetDlgItem(window, kStatusControlId), reinterpret_cast<const wchar_t*>(lParam));
        return 0;
    case kCompletedMessage:
        reinterpret_cast<InstallContext*>(GetWindowLongPtrW(window, GWLP_USERDATA))->succeeded = wParam != 0;
        PostQuitMessage(0);
        return 0;
    case kProgressMessage:
        SendMessageW(GetDlgItem(window, kProgressControlId), PBM_SETPOS, wParam, 0);
        return 0;
    case WM_CLOSE:
        return 0;
    case WM_CTLCOLORSTATIC:
        SetBkMode(reinterpret_cast<HDC>(wParam), TRANSPARENT);
        SetTextColor(reinterpret_cast<HDC>(wParam), RGB(35, 35, 38));
        return reinterpret_cast<LRESULT>(ProgressBackground());
    case WM_NCCREATE:
        SetWindowLongPtrW(window, GWLP_USERDATA,
            reinterpret_cast<LONG_PTR>(reinterpret_cast<CREATESTRUCTW*>(lParam)->lpCreateParams));
        return TRUE;
    }
    return DefWindowProcW(window, message, wParam, lParam);
}

void PostStatus(HWND window, const wchar_t* status)
{
    SendMessageW(window, kStatusMessage, 0, reinterpret_cast<LPARAM>(status));
}

void SetProgress(HWND window, int value)
{
    PostMessageW(window, kProgressMessage, static_cast<WPARAM>(value), 0);
}

std::wstring GetPayloadFingerprint()
{
    const auto resource = FindResourceW(nullptr, MAKEINTRESOURCEW(kMainResourceId), RT_RCDATA);
    if (!resource) return L"missing";
    const auto size = SizeofResource(nullptr, resource);
    const auto loaded = LoadResource(nullptr, resource);
    const auto bytes = static_cast<const unsigned char*>(LockResource(loaded));
    if (!size || !bytes) return L"invalid";

    HCRYPTPROV provider{};
    HCRYPTHASH hash{};
    BYTE digest[32]{};
    DWORD digestSize = sizeof(digest);
    if (CryptAcquireContextW(&provider, nullptr, nullptr, PROV_RSA_AES, CRYPT_VERIFYCONTEXT) &&
        CryptCreateHash(provider, CALG_SHA_256, 0, 0, &hash) &&
        CryptHashData(hash, bytes, size, 0) &&
        CryptGetHashParam(hash, HP_HASHVAL, digest, &digestSize, 0))
    {
        constexpr wchar_t hex[] = L"0123456789abcdef";
        std::wstring fingerprint;
        fingerprint.reserve(digestSize * 2);
        for (DWORD index = 0; index < digestSize; ++index)
        {
            fingerprint.push_back(hex[digest[index] >> 4]);
            fingerprint.push_back(hex[digest[index] & 0x0f]);
        }
        CryptDestroyHash(hash);
        CryptReleaseContext(provider, 0);
        return fingerprint;
    }
    if (hash) CryptDestroyHash(hash);
    if (provider) CryptReleaseContext(provider, 0);
    return L"invalid";
}

std::filesystem::path GetDataDirectory()
{
    wchar_t buffer[MAX_PATH]{};
    GetEnvironmentVariableW(L"LOCALAPPDATA", buffer, MAX_PATH);
    // Content-addressed extraction makes every changed embedded payload use a
    // fresh directory, so an old cached Lite build can never shadow a new one.
    return std::filesystem::path(buffer) / kAppName / L"Lite" / GetPayloadFingerprint();
}

bool DownloadFile(const wchar_t* url, const std::filesystem::path& destination, HWND window, const wchar_t* label)
{
    HINTERNET internet = InternetOpenW(kAppName, INTERNET_OPEN_TYPE_PRECONFIG, nullptr, nullptr, 0);
    if (!internet) return false;
    HINTERNET request = InternetOpenUrlW(internet, url, nullptr, 0, INTERNET_FLAG_RELOAD | INTERNET_FLAG_NO_CACHE_WRITE, 0);
    if (!request) { InternetCloseHandle(internet); return false; }

    DWORD statusCode = 0, statusSize = sizeof(statusCode);
    if (!HttpQueryInfoW(request, HTTP_QUERY_STATUS_CODE | HTTP_QUERY_FLAG_NUMBER,
        &statusCode, &statusSize, nullptr) || statusCode < 200 || statusCode >= 300)
    {
        InternetCloseHandle(request);
        InternetCloseHandle(internet);
        return false;
    }

    DWORD totalBytes = 0, size = sizeof(totalBytes);
    HttpQueryInfoW(request, HTTP_QUERY_CONTENT_LENGTH | HTTP_QUERY_FLAG_NUMBER, &totalBytes, &size, nullptr);
    HANDLE file = CreateFileW(destination.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) { InternetCloseHandle(request); InternetCloseHandle(internet); return false; }

    std::vector<BYTE> buffer(64 * 1024);
    DWORD read = 0, written = 0, downloaded = 0;
    bool succeeded = true;
    while (true)
    {
        if (!InternetReadFile(request, buffer.data(), static_cast<DWORD>(buffer.size()), &read)) { succeeded = false; break; }
        if (!read) break;
        if (!WriteFile(file, buffer.data(), read, &written, nullptr) || written != read) { succeeded = false; break; }
        downloaded += read;
        const int percent = totalBytes ? static_cast<int>((static_cast<unsigned long long>(downloaded) * 100) / totalBytes) : 5;
        SetProgress(window, percent);
        const std::wstring status = std::wstring(L"正在下载 ") + label + L"…  " + std::to_wstring(percent) + L"%";
        PostStatus(window, status.c_str());
    }
    CloseHandle(file);
    InternetCloseHandle(request);
    InternetCloseHandle(internet);
    return succeeded && downloaded > 0 && (!totalBytes || downloaded == totalBytes);
}

bool RunElevatedInstaller(const std::filesystem::path& installer, const wchar_t* arguments)
{
    SHELLEXECUTEINFOW info{ sizeof(info) };
    info.fMask = SEE_MASK_NOCLOSEPROCESS;
    info.lpVerb = L"runas";
    info.lpFile = installer.c_str();
    info.lpParameters = arguments;
    info.nShow = SW_HIDE;
    if (!ShellExecuteExW(&info) || !info.hProcess) return false;
    WaitForSingleObject(info.hProcess, INFINITE);
    DWORD exitCode = 1;
    GetExitCodeProcess(info.hProcess, &exitCode);
    CloseHandle(info.hProcess);
    return exitCode == 0 || exitCode == 3010;
}

std::filesystem::path GetWingetPath()
{
    wchar_t buffer[MAX_PATH]{};
    if (!GetEnvironmentVariableW(L"LOCALAPPDATA", buffer, MAX_PATH)) return {};
    const auto path = std::filesystem::path(buffer) / L"Microsoft" / L"WindowsApps" / L"winget.exe";
    // std::filesystem::exists() can throw on these App Execution Alias reparse points.
    const DWORD attributes = GetFileAttributesW(path.c_str());
    return (attributes != INVALID_FILE_ATTRIBUTES && !(attributes & FILE_ATTRIBUTE_DIRECTORY)) ? path : std::filesystem::path();
}

// winget runs unelevated per-user here; package installers elevate themselves via UAC.
bool WingetInstall(const std::filesystem::path& winget, const wchar_t* packageId)
{
    std::wstring arguments = std::wstring(L"install --id ") + packageId +
        L" --exact --silent --disable-interactivity --accept-package-agreements --accept-source-agreements";
    STARTUPINFOW startup{ sizeof(startup) };
    startup.wShowWindow = SW_HIDE;
    startup.dwFlags = STARTF_USESHOWWINDOW;
    PROCESS_INFORMATION process{};
    std::wstring commandLine = L"\"" + winget.wstring() + L"\" " + arguments;
    if (!CreateProcessW(winget.c_str(), commandLine.data(), nullptr, nullptr, false,
        CREATE_NO_WINDOW, nullptr, nullptr, &startup, &process)) return false;
    WaitForSingleObject(process.hProcess, INFINITE);
    DWORD exitCode = 1;
    GetExitCodeProcess(process.hProcess, &exitCode);
    CloseHandle(process.hThread);
    CloseHandle(process.hProcess);
    return exitCode == 0 || exitCode == 3010;
}


bool RunProcess(const std::wstring& file, const std::wstring& arguments)
{
    std::wstring commandLine = L"\"" + file + L"\" " + arguments;
    STARTUPINFOW startup{ sizeof(startup) };
    PROCESS_INFORMATION process{};
    if (!CreateProcessW(nullptr, commandLine.data(), nullptr, nullptr, false, CREATE_NO_WINDOW, nullptr, nullptr, &startup, &process)) return false;
    WaitForSingleObject(process.hProcess, INFINITE);
    DWORD exitCode = 1;
    GetExitCodeProcess(process.hProcess, &exitCode);
    CloseHandle(process.hThread);
    CloseHandle(process.hProcess);
    return exitCode == 0;
}

// .NET Desktop Runtime 9 (x64): pure filesystem probe of the shared framework store.
// A higher major runtime alone does not satisfy the default .NET major roll-forward policy.
bool IsDotNetDesktopRuntimeInstalled()
{
    wchar_t buffer[MAX_PATH]{};
    if (!ExpandEnvironmentStringsW(L"%ProgramFiles%\\dotnet\\shared\\Microsoft.WindowsDesktop.App", buffer, MAX_PATH)) return false;
    const std::filesystem::path root = buffer;
    std::error_code ec;
    if (!std::filesystem::exists(root, ec)) return false;
    for (const auto& entry : std::filesystem::directory_iterator(root, ec))
    {
        if (!entry.is_directory(ec)) continue;
        int major = 0;
        if (swscanf_s(entry.path().filename().c_str(), L"%d", &major) == 1 && major == 9) return true;
    }
    return false;
}

// Windows App Runtime: canonical probe via the bootstrap DLL shipped with the app.
// MddBootstrapInitialize2 succeeds only when a compatible runtime is registered for the machine.
typedef HRESULT(WINAPI* MddBootstrapInitialize2Fn)(UINT32 majorMinorVersion, PCWSTR versionTag, ULONGLONG minVersion, UINT32 options);
typedef VOID(WINAPI* MddBootstrapShutdownFn)();
bool IsWindowsAppRuntimeInstalled(const std::filesystem::path& dataDirectory)
{
    const auto dll = dataDirectory / L"Microsoft.WindowsAppRuntime.Bootstrap.dll";
    HMODULE module = LoadLibraryW(dll.c_str());
    if (!module)
    {
        // Payload not extracted yet; try loading by name from the system search path.
        module = LoadLibraryW(L"Microsoft.WindowsAppRuntime.Bootstrap.dll");
    }
    if (!module) return false;

    const auto initialize = reinterpret_cast<MddBootstrapInitialize2Fn>(GetProcAddress(module, "MddBootstrapInitialize2"));
    const auto shutdown = reinterpret_cast<MddBootstrapShutdownFn>(GetProcAddress(module, "MddBootstrapShutdown"));
    bool installed = false;
    if (initialize)
    {
        // 1.6 = 0x00010006
        installed = SUCCEEDED(initialize(0x00010006, nullptr, 0, 0));
        if (installed && shutdown) shutdown();
    }
    FreeLibrary(module);
    return installed;
}

bool InstallDependenciesWithProgress(bool installDotNet, bool installAppRuntime)
{
    INITCOMMONCONTROLSEX controls{ sizeof(controls), ICC_PROGRESS_CLASS };
    InitCommonControlsEx(&controls);

    InstallContext context{};
    context.installDotNet = installDotNet;
    context.installAppRuntime = installAppRuntime;
    WNDCLASSEXW windowClass{ sizeof(windowClass) };
    windowClass.lpfnWndProc = ProgressWindowProc;
    windowClass.hInstance = GetModuleHandleW(nullptr);
    windowClass.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    windowClass.hbrBackground = ProgressBackground();
    windowClass.lpszClassName = L"WindowsExtendQuickSetting.InstallProgress";
    RegisterClassExW(&windowClass);

    context.window = CreateWindowExW(WS_EX_DLGMODALFRAME, windowClass.lpszClassName, kAppName,
        WS_CAPTION | WS_SYSMENU, CW_USEDEFAULT, CW_USEDEFAULT, 576, 250,
        nullptr, nullptr, windowClass.hInstance, &context);
    if (!context.window) return false;
    ShowWindow(context.window, SW_SHOW);
    UpdateWindow(context.window);

    std::thread installer([&context]
    {
        const auto temporaryDirectory = GetDataDirectory() / L"installers";
        std::filesystem::create_directories(temporaryDirectory);
        const auto dotNetInstaller = temporaryDirectory / L"windowsdesktop-runtime.exe";
        const auto appRuntimeInstaller = temporaryDirectory / L"windowsappruntime.exe";
        const auto winget = GetWingetPath();

        bool dotNetSucceeded = !context.installDotNet;
        bool appRuntimeSucceeded = !context.installAppRuntime;

        // Each dependency: try winget first, fall back to direct URL + official installer.
        if (context.installDotNet)
        {
            SetProgress(context.window, 1);
            PostStatus(context.window, L"正在通过 winget 安装 .NET Desktop Runtime…");
            bool installed = !winget.empty() && WingetInstall(winget, kDotNetWingetId);
            if (!installed)
            {
                PostStatus(context.window, L"winget 不可用，正在下载 .NET Desktop Runtime 官方安装器…");
                SetProgress(context.window, 1);
                installed = DownloadFile(kDotNetUrl, dotNetInstaller, context.window, L".NET Desktop Runtime") &&
                    (PostStatus(context.window, L"正在安装 .NET Desktop Runtime…"),
                     RunElevatedInstaller(dotNetInstaller, L"/install /quiet /norestart"));
            }
            dotNetSucceeded = installed;
            if (!dotNetSucceeded)
            {
                PostMessageW(context.window, kCompletedMessage, 0, 0);
                return;
            }
        }

        if (context.installAppRuntime)
        {
            SetProgress(context.window, dotNetSucceeded && context.installDotNet ? 50 : 1);
            PostStatus(context.window, L"正在通过 winget 安装 Windows App Runtime…");
            bool installed = !winget.empty() && WingetInstall(winget, kAppRuntimeWingetId);
            if (!installed)
            {
                PostStatus(context.window, L"winget 不可用，正在下载 Windows App Runtime 官方安装器…");
                SetProgress(context.window, 1);
                installed = DownloadFile(kAppRuntimeUrl, appRuntimeInstaller, context.window, L"Windows App Runtime") &&
                    (PostStatus(context.window, L"正在安装 Windows App Runtime…"),
                     RunElevatedInstaller(appRuntimeInstaller, L"--quiet"));
            }
            appRuntimeSucceeded = installed;
        }

        if (appRuntimeSucceeded) PostStatus(context.window, L"依赖已安装完成，正在启动…");
        SetProgress(context.window, appRuntimeSucceeded ? 100 : 0);
        PostMessageW(context.window, kCompletedMessage, appRuntimeSucceeded, 0);
    });

    MSG message;
    while (GetMessageW(&message, nullptr, 0, 0) > 0)
    {
        TranslateMessage(&message);
        DispatchMessageW(&message);
    }
    installer.join();
    DestroyWindow(context.window);
    return context.succeeded;
}

bool ExtractPayload(const std::filesystem::path& destination)
{
    auto resource = FindResourceW(nullptr, MAKEINTRESOURCEW(kMainResourceId), RT_RCDATA);
    if (!resource) return false;
    auto size = SizeofResource(nullptr, resource);
    auto loaded = LoadResource(nullptr, resource);
    auto data = LockResource(loaded);
    if (!size || !data) return false;

    std::filesystem::create_directories(destination.parent_path());
    HANDLE file = CreateFileW(destination.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return false;
    DWORD written = 0;
    const bool success = WriteFile(file, data, size, &written, nullptr) && written == size;
    CloseHandle(file);
    return success;
}

bool IsPayloadComplete(const std::filesystem::path& directory)
{
    return std::filesystem::exists(directory / L".ready") &&
        std::filesystem::exists(directory / L"WindowsExtendQuickSetting.App.exe") &&
        std::filesystem::exists(directory / L"WindowsExtendQuickSetting.App.dll") &&
        std::filesystem::exists(directory / L"WindowsExtendQuickSetting.App.runtimeconfig.json") &&
        std::filesystem::exists(directory / L"Microsoft.WindowsAppRuntime.Bootstrap.dll") &&
        std::filesystem::exists(directory / L"resources.pri");
}

bool MarkPayloadReady(const std::filesystem::path& directory)
{
    const auto marker = directory / L".ready";
    const HANDLE file = CreateFileW(marker.c_str(), GENERIC_WRITE, 0, nullptr,
        CREATE_ALWAYS, FILE_ATTRIBUTE_HIDDEN, nullptr);
    if (file == INVALID_HANDLE_VALUE) return false;
    CloseHandle(file);
    return true;
}
}

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int)
{
    const auto dataDirectory = GetDataDirectory();
    const auto mainProgram = dataDirectory / L"WindowsExtendQuickSetting.App.exe";
    const auto payloadArchive = dataDirectory / L"payload.zip";

    if (!IsPayloadComplete(dataDirectory))
    {
        // Lite is a single-file distribution, but WinUI requires loose native and
        // resource files at runtime. Materialize them only on the first launch.
        std::error_code cleanupError;
        std::filesystem::remove_all(dataDirectory, cleanupError);
        if (!ExtractPayload(payloadArchive))
        {
            MessageBoxW(nullptr, L"无法释放轻量程序资源。", kAppName, MB_OK | MB_ICONERROR);
            return 1;
        }
        const auto command = L"-NoProfile -NonInteractive -WindowStyle Hidden -Command \"Expand-Archive -LiteralPath '" +
            payloadArchive.wstring() + L"' -DestinationPath '" + dataDirectory.wstring() + L"' -Force\"";
        if (!RunProcess(L"powershell.exe", command) ||
            !std::filesystem::exists(mainProgram) ||
            !std::filesystem::exists(dataDirectory / L"WindowsExtendQuickSetting.App.dll") ||
            !std::filesystem::exists(dataDirectory / L"Microsoft.WindowsAppRuntime.Bootstrap.dll") ||
            !std::filesystem::exists(dataDirectory / L"resources.pri") ||
            !MarkPayloadReady(dataDirectory))
        {
            MessageBoxW(nullptr, L"无法初始化轻量主程序。", kAppName, MB_OK | MB_ICONERROR);
            return 1;
        }
        std::error_code removeArchiveError;
        std::filesystem::remove(payloadArchive, removeArchiveError);
    }

    // Detect actual system state every launch; install only what is missing.
    const bool dotNetOk = IsDotNetDesktopRuntimeInstalled();
    const bool appRuntimeOk = IsWindowsAppRuntimeInstalled(dataDirectory);

    if (!dotNetOk || !appRuntimeOk)
    {
        std::wstring missing;
        if (!dotNetOk) missing += L"\n· .NET Desktop Runtime 9";
        if (!appRuntimeOk) missing += L"\n· Microsoft Windows App Runtime 1.6";
        const std::wstring prompt =
            std::wstring(L"检测到系统缺少以下依赖：") + missing +
            L"\n\n点击“确定”后将自动从 Microsoft 官方源下载并安装缺失的组件。";
        if (MessageBoxW(nullptr, prompt.c_str(), kAppName, MB_OKCANCEL | MB_ICONINFORMATION) != IDOK) return 0;

        if (!InstallDependenciesWithProgress(!dotNetOk, !appRuntimeOk))
        {
            MessageBoxW(nullptr, L"依赖安装失败。请检查网络连接和管理员权限。", kAppName, MB_OK | MB_ICONERROR);
            return 1;
        }
        if ((!dotNetOk && !IsDotNetDesktopRuntimeInstalled()) ||
            (!appRuntimeOk && !IsWindowsAppRuntimeInstalled(dataDirectory)))
        {
            MessageBoxW(nullptr, L"依赖安装完成后验证失败，请重启系统后重试。", kAppName, MB_OK | MB_ICONERROR);
            return 1;
        }
    }

    const auto launchResult = reinterpret_cast<INT_PTR>(
        ShellExecuteW(nullptr, L"open", mainProgram.c_str(), nullptr, dataDirectory.c_str(), SW_SHOWNORMAL));
    if (launchResult <= 32)
    {
        MessageBoxW(nullptr, L"主程序启动失败。", kAppName, MB_OK | MB_ICONERROR);
        return 1;
    }
    return 0;
}
