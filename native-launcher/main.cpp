#include <windows.h>
#include <commctrl.h>
#include <wininet.h>
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

struct InstallContext
{
    HWND window{};
    bool succeeded{};
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

std::filesystem::path GetDataDirectory()
{
    wchar_t buffer[MAX_PATH]{};
    GetEnvironmentVariableW(L"LOCALAPPDATA", buffer, MAX_PATH);
    return std::filesystem::path(buffer) / kAppName / L"Lite";
}

bool DownloadFile(const wchar_t* url, const std::filesystem::path& destination, HWND window, const wchar_t* label)
{
    HINTERNET internet = InternetOpenW(kAppName, INTERNET_OPEN_TYPE_PRECONFIG, nullptr, nullptr, 0);
    if (!internet) return false;
    HINTERNET request = InternetOpenUrlW(internet, url, nullptr, 0, INTERNET_FLAG_RELOAD | INTERNET_FLAG_NO_CACHE_WRITE, 0);
    if (!request) { InternetCloseHandle(internet); return false; }

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
    return succeeded;
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

bool InstallDependenciesWithProgress()
{
    INITCOMMONCONTROLSEX controls{ sizeof(controls), ICC_PROGRESS_CLASS };
    InitCommonControlsEx(&controls);

    InstallContext context{};
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

        SetProgress(context.window, 1);
        const bool dotNetDownloaded = DownloadFile(kDotNetUrl, dotNetInstaller, context.window, L".NET Desktop Runtime");
        if (dotNetDownloaded) PostStatus(context.window, L"正在安装 .NET Desktop Runtime…");
        const bool dotNetInstalled = dotNetDownloaded && RunElevatedInstaller(dotNetInstaller, L"/install /quiet /norestart");

        SetProgress(context.window, 1);
        const bool appRuntimeDownloaded = dotNetInstalled && DownloadFile(kAppRuntimeUrl, appRuntimeInstaller, context.window, L"Windows App Runtime");
        if (appRuntimeDownloaded) PostStatus(context.window, L"正在安装 Windows App Runtime…");
        const bool appRuntimeInstalled = appRuntimeDownloaded && RunElevatedInstaller(appRuntimeInstaller, L"--quiet");
        if (appRuntimeInstalled) PostStatus(context.window, L"依赖已安装完成，正在启动…");
        SetProgress(context.window, appRuntimeInstalled ? 100 : 0);
        PostMessageW(context.window, kCompletedMessage, appRuntimeInstalled, 0);
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
    auto data = LoadResource(nullptr, resource);
    if (!size || !data) return false;

    std::filesystem::create_directories(destination.parent_path());
    HANDLE file = CreateFileW(destination.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return false;
    DWORD written = 0;
    const bool success = WriteFile(file, data, size, &written, nullptr) && written == size;
    CloseHandle(file);
    return success;
}
}

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int)
{
    const auto dataDirectory = GetDataDirectory();
    const auto mainProgram = dataDirectory / L"WindowsExtendQuickSetting.App.exe";
    const auto payloadArchive = dataDirectory / L"payload.zip";
    const auto installedMarker = dataDirectory / L"runtime-installed.marker";

    if (!std::filesystem::exists(installedMarker))
    {
        const auto choice = MessageBoxW(nullptr,
            L"首次运行需要安装 .NET Desktop Runtime 9 和 Microsoft Windows App Runtime 1.6。\n\n"
            L"点击“确定”后将显示安装进度，并从 Microsoft 官方源自动下载和安装。",
            kAppName, MB_OKCANCEL | MB_ICONINFORMATION);
        if (choice != IDOK) return 0;

        if (!InstallDependenciesWithProgress())
        {
            MessageBoxW(nullptr, L"依赖安装失败。请检查网络连接、winget 和管理员权限。", kAppName, MB_OK | MB_ICONERROR);
            return 1;
        }
        std::filesystem::create_directories(dataDirectory);
        HANDLE marker = CreateFileW(installedMarker.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (marker != INVALID_HANDLE_VALUE) CloseHandle(marker);
    }

    if (!std::filesystem::exists(mainProgram) && !ExtractPayload(payloadArchive))
    {
        MessageBoxW(nullptr, L"无法释放轻量程序资源。", kAppName, MB_OK | MB_ICONERROR);
        return 1;
    }

    if (!std::filesystem::exists(mainProgram))
    {
        const auto command = L"-NoProfile -NonInteractive -WindowStyle Hidden -Command \"Expand-Archive -LiteralPath '" +
            payloadArchive.wstring() + L"' -DestinationPath '" + dataDirectory.wstring() + L"' -Force\"";
        if (!RunProcess(L"powershell.exe", command) || !std::filesystem::exists(mainProgram))
        {
            MessageBoxW(nullptr, L"无法初始化轻量主程序。", kAppName, MB_OK | MB_ICONERROR);
            return 1;
        }
    }

    ShellExecuteW(nullptr, L"open", mainProgram.c_str(), nullptr, dataDirectory.c_str(), SW_SHOWNORMAL);
    return 0;
}
