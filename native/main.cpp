#define NOMINMAX
#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>
#include <windowsx.h>
#include <shellapi.h>
#include <wlanapi.h>
#include <netioapi.h>
#include <setupapi.h>
#include <devguid.h>
#include <cfgmgr32.h>
#include <bluetoothapis.h>
#include <dwmapi.h>
#include <string>
#include <vector>
#include <algorithm>
#include <iphlpapi.h>

namespace {
constexpr wchar_t kClassName[] = L"WindowsExtendQuickSetting.Native";
constexpr wchar_t kTitle[] = L"WindowsExtendQuickSetting";
constexpr UINT kTrayMessage = WM_APP + 1;
// Posted by the virtual-display worker thread once install/enable finished.
constexpr UINT kVddWorkMessage = WM_APP + 2;
constexpr UINT_PTR kTrayId = 1;
const GUID kTrayGuid = { 0x7e67d7a3, 0x09cc, 0x4cb2, { 0x9d, 0xb0, 0xd0, 0xec, 0xda, 0x8c, 0xc6, 0x3d } };
constexpr UINT kTrayOpen = 1001;
constexpr UINT kTraySettings = 1002;
constexpr UINT kTrayExit = 1003;
constexpr UINT_PTR kRefreshTimer = 2;
constexpr UINT_PTR kFadeTimer = 3;
constexpr UINT_PTR kResizeTimer = 4;
constexpr UINT_PTR kToastTimer = 5;
constexpr UINT_PTR kActivateTimer = 6;
constexpr int kWindowWidth = 368;
constexpr int kCompactHeight = 570;
constexpr int kDetailsHeight = 660;
constexpr int kPageHeight = 650;
constexpr int kContentLeft = 24;
constexpr int kContentRight = 344;

HWND g_window{};
NOTIFYICONDATAW g_tray{};
HICON g_trayIcon{};
HANDLE g_mutex{};
HANDLE g_activateEvent{};
bool g_wifiOn = true;
bool g_ethernetOn = false;
bool g_bluetoothOn = true;
bool g_bluetoothScanning = false;
bool g_bluetoothRecoveryPending = false;
bool g_detailsVisible = false;
bool g_currentNetworkExpanded = true;
bool g_hoverCurrentNetworkChevron = false;
int g_detailsKind = 0;
std::wstring g_status = L"Wi-Fi · 正在获取网络状态";

struct WifiNetwork {
    std::wstring name; ULONG signal{}; bool secured{}; bool saved{}; GUID interfaceId{};
    DOT11_AUTH_ALGORITHM auth{}; DOT11_CIPHER_ALGORITHM cipher{};
};
struct WifiAdapter { std::wstring name; GUID interfaceId{}; std::wstring ssid; bool connected{}; };
std::vector<WifiNetwork> g_wifiNetworks;
std::vector<WifiAdapter> g_wifiAdapters;
size_t g_wifiAdapterIndex = 0;
bool g_wifiAdapterSelectedByUser = false;
GUID g_wifiAdapterId{};
bool g_wifiAdapterIdSet = false;
size_t g_wifiAvailableOffset = 0;
bool g_wifiAvailableExpanded = true;
struct SavedWifi { std::wstring name; GUID interfaceId{}; };
std::vector<SavedWifi> g_savedWifi;
size_t g_wifiSavedOffset = 0;
bool g_wifiSavedExpanded = false;
std::wstring g_wifiSearch;
bool g_wifiSavedSearchActive = false;
std::wstring g_lastForgottenName, g_lastForgottenXml;
GUID g_lastForgottenInterface{};
std::wstring g_toastText;
std::wstring g_toastAction;
int g_toastActionLeft = 0;
int g_toastActionRight = 0;
struct BluetoothAdapter { std::wstring name; DEVINST devinst{}; std::wstring instanceId; bool enabled{}; };
std::vector<BluetoothAdapter> g_bluetoothAdapters;
struct BluetoothDevice { std::wstring name; BLUETOOTH_ADDRESS address{}; bool paired{}; bool connected{}; };
std::vector<BluetoothDevice> g_bluetoothDevices;
size_t g_bluetoothDeviceOffset = 0;
struct DohServer { std::wstring address; std::wstring templateUrl; };
std::vector<DohServer> g_dohServers;
std::wstring g_dohSearch;
size_t g_dohOffset = 0;
int g_dohPicker = 0; // 0 = closed, 1 = primary, 2 = backup
std::wstring g_dohPickerSearch;
bool g_dohEnabled = false;
bool g_dohApplied = false;
size_t g_dohPrimary = 0;
size_t g_dohBackup = 1;
enum class Page { Home, Settings, Doh };
Page g_page = Page::Home;
bool g_startWithWindows = false;
bool g_isZh = true;
int g_hoverTile = 0;
bool g_hoverTileExpand = false;
BYTE g_windowAlpha = 255;
bool g_fadeHiding = false;
int g_resizeTargetHeight = kCompactHeight;
std::wstring g_usbInterface;
bool g_usbOn = false;
bool g_displayExtended = true;
bool g_hdrSupported = false;
bool g_hdrEnabled = false;
UINT32 g_activeDisplayCount = 0;
bool g_vddAuto = false;
DWORD g_vddNextAllowedTick = 0;
bool g_vddWorkInProgress = false;
struct PromptContext { std::wstring label; std::wstring value; HWND edit{}; bool accepted{}; bool multiline{}; bool password{}; };
struct ChoiceContext { std::wstring label; std::vector<std::wstring> choices; HWND list{}; int selected{ -1 }; };
WNDPROC g_choiceListOriginalProc{};
struct NetworkAdapter { std::wstring name; NET_LUID luid{}; ULONG metric{}; bool up{}; };
std::vector<NetworkAdapter> g_networkAdapters;
std::wstring g_currentNetworkInfo = L"正在获取网络接口";
std::wstring g_currentIpInfo = L"IP: —";
std::wstring g_currentDnsInfo = L"DNS: —";

const wchar_t* Tr(const wchar_t* zh, const wchar_t* en) {
    return g_isZh ? zh : en;
}

std::wstring GetNativeVersion() {
    wchar_t path[MAX_PATH]{};
    if (!GetModuleFileNameW(nullptr, path, ARRAYSIZE(path))) return L"—";
    DWORD ignored{};
    const auto size = GetFileVersionInfoSizeW(path, &ignored);
    if (!size) return L"—";
    std::vector<BYTE> data(size);
    if (!GetFileVersionInfoW(path, 0, size, data.data())) return L"—";
    VS_FIXEDFILEINFO* info{};
    UINT infoSize{};
    if (!VerQueryValueW(data.data(), L"\\", reinterpret_cast<void**>(&info), &infoSize) || !info || infoSize < sizeof(*info)) return L"—";
    return std::to_wstring(HIWORD(info->dwFileVersionMS)) + L"." + std::to_wstring(LOWORD(info->dwFileVersionMS)) + L"." + std::to_wstring(HIWORD(info->dwFileVersionLS));
}

int DesiredWindowHeight() {
    if (g_page != Page::Home) return kPageHeight;
    return g_detailsVisible ? kDetailsHeight : kCompactHeight;
}

RECT PopupBoundsNearTaskbar(int height, bool followCursor) {
    HMONITOR monitor{};
    if (followCursor) {
        POINT cursor{};
        GetCursorPos(&cursor);
        monitor = MonitorFromPoint(cursor, MONITOR_DEFAULTTONEAREST);
    } else {
        monitor = MonitorFromWindow(g_window, MONITOR_DEFAULTTONEAREST);
    }
    MONITORINFO info{ sizeof(info) };
    if (!monitor || !GetMonitorInfoW(monitor, &info)) {
        SystemParametersInfoW(SPI_GETWORKAREA, 0, &info.rcWork, 0);
        info.rcMonitor = info.rcWork;
    }
    const auto& screen = info.rcMonitor;
    const auto& work = info.rcWork;
    const bool taskbarLeft = work.left > screen.left;
    const bool taskbarRight = work.right < screen.right;
    const bool taskbarTop = work.top > screen.top;
    int x = work.right - kWindowWidth - 16;
    int y = work.bottom - height - 8;
    if (taskbarLeft) x = work.left + 8;
    else if (taskbarRight) x = work.right - kWindowWidth - 8;
    if (taskbarTop) y = work.top + 8;
    x = std::clamp(x, static_cast<int>(work.left), std::max(static_cast<int>(work.left), static_cast<int>(work.right) - kWindowWidth));
    y = std::clamp(y, static_cast<int>(work.top), std::max(static_cast<int>(work.top), static_cast<int>(work.bottom) - height));
    return { x, y, x + kWindowWidth, y + height };
}

int CurrentNetworkTop() {
    if (!g_detailsVisible) return g_currentNetworkExpanded ? 378 : 434;
    // Keep this card anchored above the footer. Collapsing it releases its
    // former height to the expanded quick-settings content above.
    return g_currentNetworkExpanded ? 462 : 518;
}

int WifiSavedHeaderTop() {
    if (!g_wifiAvailableExpanded) return 228;
    // When Saved Wi-Fi is collapsed, keep its header visible at the bottom and
    // give every intervening row to the available-network list.
    return g_wifiSavedExpanded ? (200 + CurrentNetworkTop() - 20) / 2 : CurrentNetworkTop() - 44;
}
int WifiSavedListTop() { return WifiSavedHeaderTop() + 28; }
int WifiAvailableVisibleRows() {
    if (!g_wifiAvailableExpanded) return 0;
    const int bottom = WifiSavedHeaderTop() - 8;
    return std::max(1, (bottom - 228) / 24);
}
int WifiSavedVisibleRows() {
    if (!g_wifiSavedExpanded) return 0;
    return std::max(1, (CurrentNetworkTop() - 20 - WifiSavedListTop()) / 24);
}

void ShowToast(const std::wstring& text, const std::wstring& action = L"") {
    g_toastText = text;
    g_toastAction = action;
    KillTimer(g_window, kToastTimer);
    // Keep toast dwell time aligned with the WinUI3 host (2.6 seconds).
    SetTimer(g_window, kToastTimer, 2600, nullptr);
    InvalidateRect(g_window, nullptr, FALSE);
}

bool RunHidden(const std::wstring& command) {
    SHELLEXECUTEINFOW execute{ sizeof(execute) };
    execute.fMask = SEE_MASK_NOCLOSEPROCESS;
    execute.lpVerb = L"runas";
    execute.lpFile = L"cmd.exe";
    const std::wstring arguments = L"/d /c " + command;
    execute.lpParameters = arguments.c_str();
    execute.nShow = SW_HIDE;
    if (!ShellExecuteExW(&execute) || !execute.hProcess) return false;
    WaitForSingleObject(execute.hProcess, INFINITE);
    DWORD exitCode = 1;
    GetExitCodeProcess(execute.hProcess, &exitCode);
    CloseHandle(execute.hProcess);
    return exitCode == 0;
}

void RefreshDisplayState() {
    UINT32 pathCount{}, modeCount{};
    g_activeDisplayCount = 0;
    g_hdrSupported = false;
    g_hdrEnabled = false;
    if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, &pathCount, &modeCount) != ERROR_SUCCESS) return;
    std::vector<DISPLAYCONFIG_PATH_INFO> paths(pathCount);
    std::vector<DISPLAYCONFIG_MODE_INFO> modes(modeCount);
    if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, &pathCount, paths.data(), &modeCount, modes.data(), nullptr) != ERROR_SUCCESS) return;
    g_activeDisplayCount = pathCount;
    g_displayExtended = pathCount > 1;
    for (UINT32 first = 0; first < pathCount && g_displayExtended; ++first) {
        for (UINT32 second = first + 1; second < pathCount; ++second) {
            const auto& a = paths[first].sourceInfo;
            const auto& b = paths[second].sourceInfo;
            if (a.id == b.id && a.adapterId.LowPart == b.adapterId.LowPart && a.adapterId.HighPart == b.adapterId.HighPart) {
                g_displayExtended = false;
                break;
            }
        }
    }
    for (UINT32 index = 0; index < pathCount; ++index) {
        DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO info{};
        info.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO;
        info.header.size = sizeof(info);
        info.header.adapterId = paths[index].targetInfo.adapterId;
        info.header.id = paths[index].targetInfo.id;
        if (DisplayConfigGetDeviceInfo(&info.header) == ERROR_SUCCESS && info.advancedColorSupported) {
            g_hdrSupported = true;
            g_hdrEnabled = g_hdrEnabled || info.advancedColorEnabled;
        }
    }
}

bool SetHdrEnabled(bool enabled) {
    UINT32 pathCount{}, modeCount{};
    if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, &pathCount, &modeCount) != ERROR_SUCCESS) return false;
    std::vector<DISPLAYCONFIG_PATH_INFO> paths(pathCount);
    std::vector<DISPLAYCONFIG_MODE_INFO> modes(modeCount);
    if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, &pathCount, paths.data(), &modeCount, modes.data(), nullptr) != ERROR_SUCCESS) return false;
    bool changed = false;
    for (UINT32 index = 0; index < pathCount; ++index) {
        DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO info{};
        info.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO;
        info.header.size = sizeof(info);
        info.header.adapterId = paths[index].targetInfo.adapterId;
        info.header.id = paths[index].targetInfo.id;
        if (DisplayConfigGetDeviceInfo(&info.header) != ERROR_SUCCESS || !info.advancedColorSupported) continue;
        DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE state{};
        state.header.type = DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE;
        state.header.size = sizeof(state);
        state.header.adapterId = info.header.adapterId;
        state.header.id = info.header.id;
        state.enableAdvancedColor = enabled ? 1 : 0;
        changed = DisplayConfigSetDeviceInfo(&state.header) == ERROR_SUCCESS || changed;
    }
    RefreshDisplayState();
    return changed && g_hdrSupported && g_hdrEnabled == enabled;
}

bool SwitchDisplayMode(bool extend) {
    wchar_t systemPath[MAX_PATH]{};
    if (!GetSystemDirectoryW(systemPath, ARRAYSIZE(systemPath))) return false;
    const std::wstring executable = std::wstring(systemPath) + L"\\DisplaySwitch.exe";
    const auto result = reinterpret_cast<INT_PTR>(ShellExecuteW(nullptr, L"open", executable.c_str(), extend ? L"/extend" : L"/clone", nullptr, SW_HIDE));
    if (result <= 32) return false;
    g_displayExtended = extend;
    return true;
}

// ---------------------------------------------------------------------------
// Virtual display (IddCx) integration. Any installed IddCx-style virtual
// display adapter works — Parsec, ToDesk, usbmmidd, spacedesk, open-source
// VirtualDisplayDriver, etc. A signed driver package may also be dropped into
// the "drivers" folder next to the executable to install from scratch.
// ---------------------------------------------------------------------------

bool IsVirtualDisplayName(const wchar_t* name) {
    static const wchar_t* keywords[] = {
        L"virtual", L"indirect", L"idd", L"parsec", L"todesk", L"usbmmidd",
        L"spacedesk", L"superdisplay", L"duet", L"vdd"
    };
    std::wstring value = name;
    std::transform(value.begin(), value.end(), value.begin(), ::towlower);
    return std::any_of(std::begin(keywords), std::end(keywords), [&](const wchar_t* keyword) {
        return value.find(keyword) != std::wstring::npos;
    });
}

std::wstring AppDirectory() {
    wchar_t path[MAX_PATH]{};
    if (GetModuleFileNameW(nullptr, path, ARRAYSIZE(path)) == 0) return L"";
    std::wstring dir(path);
    const auto slash = dir.find_last_of(L"\\/");
    return slash == std::wstring::npos ? L"" : dir.substr(0, slash);
}

bool FindVirtualDriverInf(std::wstring& infPath) {
    const auto dir = AppDirectory();
    if (dir.empty()) return false;
    for (const wchar_t* pattern : { L"\\drivers\\*.inf", L"\\*.inf" }) {
        WIN32_FIND_DATAW data{};
        const HANDLE find = FindFirstFileW((dir + pattern).c_str(), &data);
        if (find != INVALID_HANDLE_VALUE) {
            infPath = dir + (pattern[0] == L'\\' && wcscmp(pattern, L"\\drivers\\*.inf") == 0 ? L"\\drivers\\" : L"\\") + data.cFileName;
            FindClose(find);
            return true;
        }
    }
    return false;
}

// 0 = at least one virtual display device running, 1 = present but disabled,
// 2 = no virtual display device installed.
int DetectVirtualDisplayState() {
    const GUID classes[] = { GUID_DEVCLASS_DISPLAY, GUID_DEVCLASS_MONITOR };
    bool any = false, ready = false;
    for (const auto& cls : classes) {
        const HDEVINFO set = SetupDiGetClassDevsW(&cls, nullptr, nullptr, DIGCF_PRESENT);
        if (set == INVALID_HANDLE_VALUE) continue;
        SP_DEVINFO_DATA data{ sizeof(data) };
        for (DWORD index = 0; SetupDiEnumDeviceInfo(set, index, &data); ++index) {
            wchar_t name[256]{};
            DWORD required{};
            if (!SetupDiGetDeviceRegistryPropertyW(set, &data, SPDRP_FRIENDLYNAME, nullptr,
                reinterpret_cast<PBYTE>(name), sizeof(name), &required)) continue;
            if (!IsVirtualDisplayName(name)) continue;
            any = true;
            ULONG status{}, problem{};
            if (CM_Get_DevNode_Status(&status, &problem, data.DevInst, 0) == CR_SUCCESS && (status & DN_STARTED)) ready = true;
        }
        SetupDiDestroyDeviceInfoList(set);
    }
    return !any ? 2 : (ready ? 0 : 1);
}

bool EnableVirtualDisplayDevices() {
    const GUID classes[] = { GUID_DEVCLASS_DISPLAY, GUID_DEVCLASS_MONITOR };
    bool any = false, enabled = false;
    for (const auto& cls : classes) {
        const HDEVINFO set = SetupDiGetClassDevsW(&cls, nullptr, nullptr, DIGCF_PRESENT);
        if (set == INVALID_HANDLE_VALUE) continue;
        SP_DEVINFO_DATA data{ sizeof(data) };
        for (DWORD index = 0; SetupDiEnumDeviceInfo(set, index, &data); ++index) {
            wchar_t name[256]{};
            DWORD required{};
            if (!SetupDiGetDeviceRegistryPropertyW(set, &data, SPDRP_FRIENDLYNAME, nullptr,
                reinterpret_cast<PBYTE>(name), sizeof(name), &required)) continue;
            if (!IsVirtualDisplayName(name)) continue;
            any = true;
            SP_PROPCHANGE_PARAMS params{};
            params.ClassInstallHeader.cbSize = sizeof(params.ClassInstallHeader);
            params.ClassInstallHeader.InstallFunction = DIF_PROPERTYCHANGE;
            params.StateChange = DICS_ENABLE;
            params.Scope = DICS_FLAG_GLOBAL;
            if (SetupDiSetClassInstallParamsW(set, &data, &params.ClassInstallHeader, sizeof(params)) &&
                SetupDiCallClassInstaller(DIF_PROPERTYCHANGE, set, &data))
                enabled = true;
        }
        SetupDiDestroyDeviceInfoList(set);
    }
    return any && enabled;
}

bool RunElevatedWait(const wchar_t* executable, const wchar_t* arguments) {
    SHELLEXECUTEINFOW info{ sizeof(info) };
    info.fMask = SEE_MASK_NOASYNC | SEE_MASK_NOCLOSEPROCESS | SEE_MASK_FLAG_DDEWAIT;
    info.lpVerb = L"runas";
    info.lpFile = executable;
    info.lpParameters = arguments;
    info.nShow = SW_HIDE;
    if (!ShellExecuteExW(&info) || !info.hProcess) return false;
    const bool finished = WaitForSingleObject(info.hProcess, 180000) == WAIT_OBJECT_0;
    CloseHandle(info.hProcess);
    return finished;
}

bool InstallVirtualDisplayDriver() {
    std::wstring inf;
    if (!FindVirtualDriverInf(inf)) return false;
    const std::wstring arguments = L"/add-driver \"" + inf + L"\" /install";
    return RunElevatedWait(L"pnputil.exe", arguments.c_str());
}

// The worker reports one of these via kVddWorkMessage: 1 = enabled,
// 2 = already ready, 3 = no driver found, 4 = enable failed.
struct VddWork { HWND window; bool install; };

DWORD WINAPI VddWorkThread(LPVOID param) {
    auto* work = static_cast<VddWork*>(param);
    int result = 0;
    if (work->install) {
        if (!InstallVirtualDisplayDriver()) result = 3;
    }
    if (result == 0) {
        const int state = DetectVirtualDisplayState();
        if (state == 1 && EnableVirtualDisplayDevices()) result = 1;
        else if (state == 0) result = 2;
        else if (state == 2) result = 3;
        else result = 4;
    }
    PostMessageW(work->window, kVddWorkMessage, static_cast<WPARAM>(result), 0);
    delete work;
    return 0;
}

void RefreshVddAuto() {
    DWORD value{}, size = sizeof(value), type{};
    HKEY key{};
    if (RegOpenKeyExW(HKEY_CURRENT_USER, L"Software\\WindowsExtendQuickSetting.Native", 0, KEY_READ, &key) != ERROR_SUCCESS) return;
    if (RegQueryValueExW(key, L"VddAutoEnable", nullptr, &type, reinterpret_cast<BYTE*>(&value), &size) == ERROR_SUCCESS && type == REG_DWORD)
        g_vddAuto = value != 0;
    RegCloseKey(key);
}

void SaveVddAuto() {
    HKEY key{};
    if (RegCreateKeyExW(HKEY_CURRENT_USER, L"Software\\WindowsExtendQuickSetting.Native", 0, nullptr, 0,
        KEY_SET_VALUE, nullptr, &key, nullptr) != ERROR_SUCCESS) return;
    const DWORD value = g_vddAuto ? 1 : 0;
    RegSetValueExW(key, L"VddAutoEnable", 0, REG_DWORD, reinterpret_cast<const BYTE*>(&value), sizeof(value));
    RegCloseKey(key);
}

// Called from the periodic refresh timer: enables the virtual display
// automatically when the setting is on and no physical monitor is active.
void AutoVirtualDisplayCheck() {
    if (!g_vddAuto || g_activeDisplayCount != 0 || g_vddWorkInProgress) return;
    const DWORD now = GetTickCount();
    // Signed diff keeps the comparison correct across GetTickCount()'s
    // 49.7-day wraparound; unsigned subtraction would turn a future deadline
    // into a huge value and defeat the backoff entirely.
    if (g_vddNextAllowedTick != 0 && static_cast<LONG>(now - g_vddNextAllowedTick) < 0) return;
    g_vddNextAllowedTick = now + 60000;
    if (DetectVirtualDisplayState() != 1) return;
    if (EnableVirtualDisplayDevices()) {
        RefreshDisplayState();
        InvalidateRect(g_window, nullptr, FALSE);
        ShowToast(Tr(L"未检测到实体屏幕，已自动启用虚拟屏", L"No physical display detected; virtual display enabled"));
    } else {
        // Failed attempts (missing admin rights, broken driver) back off ~10 min.
        g_vddNextAllowedTick = now + 600000;
    }
}

bool IsLightTaskbar() {
    DWORD value{};
    DWORD size = sizeof(value);
    return RegGetValueW(HKEY_CURRENT_USER,
        L"Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize",
        L"SystemUsesLightTheme", RRF_RT_REG_DWORD, nullptr, &value, &size) == ERROR_SUCCESS && value != 0;
}

HICON LoadTrayIcon() {
    wchar_t path[MAX_PATH]{};
    if (GetModuleFileNameW(nullptr, path, ARRAYSIZE(path)) == 0) return nullptr;
    std::wstring iconPath(path);
    const auto slash = iconPath.find_last_of(L"\\/");
    if (slash == std::wstring::npos) return nullptr;
    iconPath.resize(slash + 1);
    iconPath += L"Assets\\NetworkControlIcon";
    iconPath += IsLightTaskbar() ? L"Dark.ico" : L"Light.ico";
    // Let Explorer select the closest embedded ICO resolution instead of
    // resampling an arbitrary 33 px bitmap, which keeps the monochrome tray
    // glyph crisp at every taskbar scale.
    return static_cast<HICON>(LoadImageW(nullptr, iconPath.c_str(), IMAGE_ICON, 0, 0,
        LR_LOADFROMFILE | LR_DEFAULTSIZE));
}

std::wstring RunCapture(const std::wstring& command) {
    SECURITY_ATTRIBUTES security{ sizeof(security), nullptr, TRUE };
    HANDLE read{}, write{};
    if (!CreatePipe(&read, &write, &security, 0)) return {};
    SetHandleInformation(read, HANDLE_FLAG_INHERIT, 0);
    STARTUPINFOW startup{ sizeof(startup) }; startup.dwFlags = STARTF_USESHOWWINDOW | STARTF_USESTDHANDLES; startup.wShowWindow = SW_HIDE; startup.hStdOutput = write; startup.hStdError = write;
    PROCESS_INFORMATION process{};
    std::wstring line = L"cmd.exe /d /c " + command;
    if (!CreateProcessW(nullptr, line.data(), nullptr, nullptr, TRUE, CREATE_NO_WINDOW, nullptr, nullptr, &startup, &process)) { CloseHandle(read); CloseHandle(write); return {}; }
    CloseHandle(write);
    std::string bytes; char buffer[1024]; DWORD count{};
    while (ReadFile(read, buffer, sizeof(buffer), &count, nullptr) && count) bytes.append(buffer, count);
    WaitForSingleObject(process.hProcess, INFINITE); CloseHandle(read); CloseHandle(process.hThread); CloseHandle(process.hProcess);
    if (bytes.empty()) return {};
    const int size = MultiByteToWideChar(CP_ACP, 0, bytes.data(), static_cast<int>(bytes.size()), nullptr, 0);
    std::wstring output(size, L'\0'); MultiByteToWideChar(CP_ACP, 0, bytes.data(), static_cast<int>(bytes.size()), output.data(), size);
    return output;
}

bool IsSafeNetshInterfaceName(const std::wstring& name) {
    return !name.empty() && name.find_first_of(L"\"\r\n&|<>") == std::wstring::npos;
}

bool SetWifiEnabled(bool enabled) {
    HANDLE client{};
    DWORD negotiated{};
    if (WlanOpenHandle(2, nullptr, &negotiated, &client) != ERROR_SUCCESS) return false;
    PWLAN_INTERFACE_INFO_LIST interfaces{};
    bool changed = false;
    if (WlanEnumInterfaces(client, nullptr, &interfaces) == ERROR_SUCCESS && interfaces) {
        for (DWORD index = 0; index < interfaces->dwNumberOfItems; ++index) {
            MIB_IF_ROW2 row{};
            row.InterfaceGuid = interfaces->InterfaceInfo[index].InterfaceGuid;
            if (GetIfEntry2(&row) != NO_ERROR || row.Alias[0] == L'\0') continue;
            const std::wstring alias(row.Alias);
            if (!IsSafeNetshInterfaceName(alias)) continue;
            changed = RunHidden(L"netsh interface set interface name=\"" + alias + L"\" admin=" + (enabled ? L"enable" : L"disable")) || changed;
        }
        WlanFreeMemory(interfaces);
    }
    WlanCloseHandle(client, nullptr);
    return changed;
}

bool IsVirtualNetworkAdapter(const IP_ADAPTER_ADDRESSES* address);
bool LooksLikeUsbTethering(const IP_ADAPTER_ADDRESSES* address);

bool SetEthernetEnabled(bool enabled) {
    constexpr ULONG flags = GAA_FLAG_INCLUDE_ALL_INTERFACES | GAA_FLAG_INCLUDE_PREFIX;
    ULONG size = 16 * 1024;
    std::vector<BYTE> buffer(size);
    auto addresses = reinterpret_cast<PIP_ADAPTER_ADDRESSES>(buffer.data());
    if (GetAdaptersAddresses(AF_UNSPEC, flags, nullptr, addresses, &size) == ERROR_BUFFER_OVERFLOW) {
        buffer.resize(size);
        addresses = reinterpret_cast<PIP_ADAPTER_ADDRESSES>(buffer.data());
    }
    if (GetAdaptersAddresses(AF_UNSPEC, flags, nullptr, addresses, &size) != NO_ERROR) return false;

    struct EthernetTarget { NET_LUID luid{}; std::wstring alias; };
    std::vector<EthernetTarget> targets;
    for (auto* address = addresses; address; address = address->Next) {
        if (address->IfType != IF_TYPE_ETHERNET_CSMACD ||
            IsVirtualNetworkAdapter(address) || LooksLikeUsbTethering(address)) continue;
        MIB_IF_ROW2 row{};
        row.InterfaceLuid = address->Luid;
        if (GetIfEntry2(&row) != NO_ERROR || row.Alias[0] == L'\0') continue;
        const std::wstring alias(row.Alias);
        if (IsSafeNetshInterfaceName(alias))
            targets.push_back({ address->Luid, alias });
    }
    if (targets.empty()) return false;

    bool commandSucceeded = true;
    for (const auto& target : targets) {
        MIB_IF_ROW2 before{};
        before.InterfaceLuid = target.luid;
        if (GetIfEntry2(&before) != NO_ERROR) { commandSucceeded = false; continue; }
        const bool alreadyEnabled = before.AdminStatus == NET_IF_ADMIN_STATUS_UP;
        if (alreadyEnabled == enabled) continue;
        commandSucceeded = RunHidden(L"netsh interface set interface name=\"" + target.alias +
            L"\" admin=" + (enabled ? L"enable" : L"disable")) && commandSucceeded;
    }

    bool verified = true;
    for (const auto& target : targets) {
        MIB_IF_ROW2 after{};
        after.InterfaceLuid = target.luid;
        if (GetIfEntry2(&after) != NO_ERROR ||
            (after.AdminStatus == NET_IF_ADMIN_STATUS_UP) != enabled) verified = false;
    }
    return commandSucceeded && verified;
}
void RefreshDohServers() {
    g_dohServers.clear();
    HKEY root{};
    if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, L"SYSTEM\\CurrentControlSet\\Services\\Dnscache\\Parameters\\DohWellKnownServers", 0, KEY_READ, &root) != ERROR_SUCCESS) return;
    for (DWORD index = 0;; ++index) {
        wchar_t name[64]{};
        DWORD length = ARRAYSIZE(name);
        if (RegEnumKeyExW(root, index, name, &length, nullptr, nullptr, nullptr, nullptr) != ERROR_SUCCESS) break;
        HKEY server{};
        if (RegOpenKeyExW(root, name, 0, KEY_READ, &server) != ERROR_SUCCESS) continue;
        wchar_t templateUrl[1024]{};
        DWORD type{};
        DWORD size = sizeof(templateUrl);
        if (RegQueryValueExW(server, L"Template", nullptr, &type, reinterpret_cast<BYTE*>(templateUrl), &size) == ERROR_SUCCESS && type == REG_SZ)
            g_dohServers.push_back({ name, templateUrl });
        RegCloseKey(server);
    }
    RegCloseKey(root);
}

void RefreshDohEnabled() {
    // EnableAutoDoh is the stable system state behind the localized netsh output.
    // Keep netsh only as a fallback for older Windows versions without this value.
    DWORD value{};
    DWORD size = sizeof(value);
    if (RegGetValueW(HKEY_LOCAL_MACHINE, L"SYSTEM\\CurrentControlSet\\Services\\Dnscache\\Parameters",
        L"EnableAutoDoh", RRF_RT_REG_DWORD, nullptr, &value, &size) == ERROR_SUCCESS) {
        g_dohEnabled = value != 0;
        return;
    }
    auto output = RunCapture(L"netsh dns show global");
    std::transform(output.begin(), output.end(), output.begin(), towlower);
    g_dohEnabled = output.find(L"doh") != std::wstring::npos &&
        (output.find(L"yes") != std::wstring::npos || output.find(L"enabled") != std::wstring::npos || output.find(L"启用") != std::wstring::npos);
}

bool SetDohEnabled(bool enabled) {
    if (!RunHidden(std::wstring(L"netsh dns set global doh=") + (enabled ? L"yes" : L"no"))) return false;
    g_dohEnabled = enabled;
    if (!enabled) g_dohApplied = false;
    RefreshDohServers();
    return true;
}

LRESULT CALLBACK PromptProc(HWND hwnd, UINT message, WPARAM wparam, LPARAM lparam) {
    auto* context = reinterpret_cast<PromptContext*>(GetWindowLongPtrW(hwnd, GWLP_USERDATA));
    if (message == WM_NCCREATE) {
        context = static_cast<PromptContext*>(reinterpret_cast<CREATESTRUCTW*>(lparam)->lpCreateParams);
        SetWindowLongPtrW(hwnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(context));
        return TRUE;
    }
    if (message == WM_CREATE && context) {
        CreateWindowW(L"STATIC", context->label.c_str(), WS_CHILD | WS_VISIBLE, 20, 18, 340, 24, hwnd, nullptr, nullptr, nullptr);
        const DWORD editStyle = WS_CHILD | WS_VISIBLE | WS_TABSTOP |
            (context->multiline ? (ES_MULTILINE | ES_AUTOVSCROLL | WS_VSCROLL | ES_WANTRETURN) : ES_AUTOHSCROLL) |
            (context->password ? ES_PASSWORD : 0);
        context->edit = CreateWindowExW(WS_EX_CLIENTEDGE, L"EDIT", L"", editStyle, 20, 48, 340, context->multiline ? 118 : 28, hwnd, reinterpret_cast<HMENU>(1), nullptr, nullptr);
        const int buttonY = context->multiline ? 180 : 94;
        CreateWindowW(L"BUTTON", Tr(L"确定", L"OK"), WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_DEFPUSHBUTTON, 194, buttonY, 76, 28, hwnd, reinterpret_cast<HMENU>(IDOK), nullptr, nullptr);
        CreateWindowW(L"BUTTON", Tr(L"取消", L"Cancel"), WS_CHILD | WS_VISIBLE | WS_TABSTOP, 284, buttonY, 76, 28, hwnd, reinterpret_cast<HMENU>(IDCANCEL), nullptr, nullptr);
        SetFocus(context->edit);
        return 0;
    }
    if (message == WM_COMMAND && context) {
        if (LOWORD(wparam) == IDOK) {
            const int length = GetWindowTextLengthW(context->edit);
            std::wstring value(static_cast<size_t>(length) + 1, L'\0');
            GetWindowTextW(context->edit, value.data(), length + 1);
            value.resize(static_cast<size_t>(length));
            context->value = value;
            context->accepted = !context->value.empty();
            DestroyWindow(hwnd);
            return 0;
        }
        if (LOWORD(wparam) == IDCANCEL) { DestroyWindow(hwnd); return 0; }
    }
    if (message == WM_CLOSE) { DestroyWindow(hwnd); return 0; }
    return DefWindowProcW(hwnd, message, wparam, lparam);
}

LRESULT CALLBACK ChoiceListProc(HWND hwnd, UINT message, WPARAM wparam, LPARAM lparam) {
    if (message == WM_KEYDOWN && wparam == VK_RETURN) {
        PostMessageW(GetParent(hwnd), WM_COMMAND, MAKEWPARAM(IDOK, 0), 0);
        return 0;
    }
    return CallWindowProcW(g_choiceListOriginalProc, hwnd, message, wparam, lparam);
}

LRESULT CALLBACK ChoiceProc(HWND hwnd, UINT message, WPARAM wparam, LPARAM lparam) {
    auto* context = reinterpret_cast<ChoiceContext*>(GetWindowLongPtrW(hwnd, GWLP_USERDATA));
    if (message == WM_NCCREATE) {
        context = static_cast<ChoiceContext*>(reinterpret_cast<CREATESTRUCTW*>(lparam)->lpCreateParams);
        SetWindowLongPtrW(hwnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(context));
        return TRUE;
    }
    if (message == WM_CREATE && context) {
        CreateWindowW(L"STATIC", context->label.c_str(), WS_CHILD | WS_VISIBLE, 20, 16, 360, 34, hwnd, nullptr, nullptr, nullptr);
        context->list = CreateWindowExW(WS_EX_CLIENTEDGE, L"LISTBOX", L"", WS_CHILD | WS_VISIBLE | WS_TABSTOP | WS_VSCROLL | LBS_NOTIFY,
            20, 52, 360, 148, hwnd, reinterpret_cast<HMENU>(1), nullptr, nullptr);
        g_choiceListOriginalProc = reinterpret_cast<WNDPROC>(SetWindowLongPtrW(context->list, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(ChoiceListProc)));
        for (const auto& choice : context->choices) SendMessageW(context->list, LB_ADDSTRING, 0, reinterpret_cast<LPARAM>(choice.c_str()));
        SendMessageW(context->list, LB_SETCURSEL, 0, 0);
        CreateWindowW(L"BUTTON", Tr(L"确定", L"OK"), WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_DEFPUSHBUTTON, 194, 214, 76, 28, hwnd, reinterpret_cast<HMENU>(IDOK), nullptr, nullptr);
        CreateWindowW(L"BUTTON", Tr(L"取消", L"Cancel"), WS_CHILD | WS_VISIBLE | WS_TABSTOP, 284, 214, 76, 28, hwnd, reinterpret_cast<HMENU>(IDCANCEL), nullptr, nullptr);
        SetFocus(context->list);
        return 0;
    }
    if (message == WM_COMMAND && context) {
        const auto id = LOWORD(wparam);
        if (id == IDOK || (id == 1 && HIWORD(wparam) == LBN_DBLCLK)) {
            const auto selected = static_cast<int>(SendMessageW(context->list, LB_GETCURSEL, 0, 0));
            if (selected != LB_ERR) context->selected = selected;
            DestroyWindow(hwnd);
            return 0;
        }
        if (id == IDCANCEL) { DestroyWindow(hwnd); return 0; }
    }
    if (message == WM_CLOSE) { DestroyWindow(hwnd); return 0; }
    return DefWindowProcW(hwnd, message, wparam, lparam);
}

std::wstring PromptText(HWND owner, const wchar_t* title, const wchar_t* label, bool multiline = false, bool password = false) {
    static bool registered = false;
    if (!registered) { WNDCLASSW wc{}; wc.hInstance = GetModuleHandleW(nullptr); wc.lpszClassName = L"WindowsExtendQuickSetting.Native.Prompt"; wc.lpfnWndProc = PromptProc; wc.hCursor = LoadCursorW(nullptr, IDC_ARROW); wc.hbrBackground = reinterpret_cast<HBRUSH>(COLOR_WINDOW + 1); RegisterClassW(&wc); registered = true; }
    PromptContext context{ label };
    context.multiline = multiline;
    context.password = password;
    const HWND dialog = CreateWindowExW(WS_EX_DLGMODALFRAME, L"WindowsExtendQuickSetting.Native.Prompt", title, WS_CAPTION | WS_SYSMENU,
        CW_USEDEFAULT, CW_USEDEFAULT, 400, multiline ? 260 : 170, owner, nullptr, GetModuleHandleW(nullptr), &context);
    EnableWindow(owner, FALSE); ShowWindow(dialog, SW_SHOW); UpdateWindow(dialog);
    MSG message{};
    while (IsWindow(dialog) && GetMessageW(&message, nullptr, 0, 0) > 0) { TranslateMessage(&message); DispatchMessageW(&message); }
    EnableWindow(owner, TRUE); SetForegroundWindow(owner);
    return context.accepted ? context.value : L"";
}

int ChooseListItem(HWND owner, const wchar_t* title, const wchar_t* label, const std::vector<std::wstring>& choices) {
    if (choices.empty()) return -1;
    static bool registered = false;
    if (!registered) { WNDCLASSW wc{}; wc.hInstance = GetModuleHandleW(nullptr); wc.lpszClassName = L"WindowsExtendQuickSetting.Native.Choice"; wc.lpfnWndProc = ChoiceProc; wc.hCursor = LoadCursorW(nullptr, IDC_ARROW); wc.hbrBackground = reinterpret_cast<HBRUSH>(COLOR_WINDOW + 1); RegisterClassW(&wc); registered = true; }
    ChoiceContext context{ label, choices };
    const HWND dialog = CreateWindowExW(WS_EX_DLGMODALFRAME, L"WindowsExtendQuickSetting.Native.Choice", title, WS_CAPTION | WS_SYSMENU,
        CW_USEDEFAULT, CW_USEDEFAULT, 400, 290, owner, nullptr, GetModuleHandleW(nullptr), &context);
    EnableWindow(owner, FALSE); ShowWindow(dialog, SW_SHOW); UpdateWindow(dialog);
    MSG message{};
    while (IsWindow(dialog) && GetMessageW(&message, nullptr, 0, 0) > 0) { TranslateMessage(&message); DispatchMessageW(&message); }
    EnableWindow(owner, TRUE); SetForegroundWindow(owner);
    return context.selected;
}

std::wstring Trim(std::wstring value) {
    const auto first = value.find_first_not_of(L" \t\r\n");
    if (first == std::wstring::npos) return L"";
    const auto last = value.find_last_not_of(L" \t\r\n");
    return value.substr(first, last - first + 1);
}

std::wstring Lower(std::wstring value) {
    std::transform(value.begin(), value.end(), value.begin(), towlower);
    return value;
}

std::vector<size_t> FilteredDohIndices(const std::wstring& query = g_dohSearch) {
    std::vector<size_t> result;
    const auto needle = Lower(Trim(query));
    for (size_t index = 0; index < g_dohServers.size(); ++index) {
        if (needle.empty() || Lower(g_dohServers[index].address + L" " + g_dohServers[index].templateUrl).find(needle) != std::wstring::npos)
            result.push_back(index);
    }
    return result;
}

std::wstring ExtractDohArgument(const std::wstring& command, const std::wstring& name) {
    const auto lower = Lower(command);
    const auto position = lower.find(Lower(name) + L"=");
    if (position == std::wstring::npos) return L"";
    size_t start = position + name.size() + 1;
    if (start >= command.size()) return L"";
    const bool quoted = command[start] == L'"';
    if (quoted) ++start;
    const auto end = quoted ? command.find(L'"', start) : command.find_first_of(L" \t\r\n;", start);
    return command.substr(start, end == std::wstring::npos ? std::wstring::npos : end - start);
}

bool IsValidDohEntry(const std::wstring& address, const std::wstring& templateUrl) {
    IN_ADDR ipv4{};
    IN6_ADDR ipv6{};
    const bool validAddress = InetPtonW(AF_INET, address.c_str(), &ipv4) == 1 ||
        InetPtonW(AF_INET6, address.c_str(), &ipv6) == 1;
    const auto lowerTemplate = Lower(templateUrl);
    const bool validTemplate = lowerTemplate.rfind(L"https://", 0) == 0 && lowerTemplate.size() > 8 &&
        templateUrl.find(L'/', 8) != std::wstring::npos &&
        templateUrl.find_first_of(L" \t\r\n\"") == std::wstring::npos;
    return validAddress && validTemplate;
}

bool ImportDohServers(HWND owner) {
    auto text = PromptText(owner, Tr(L"快速导入 DoH", L"Quick import DoH"), Tr(L"粘贴 netsh dns add encryption 命令（每行一条）", L"Paste one netsh dns add encryption command per line"), true);
    if (text.empty()) return false;
    std::replace(text.begin(), text.end(), L';', L'\n');
    size_t imported = 0, rejected = 0;
    size_t start = 0;
    while (start <= text.size()) {
        const auto end = text.find(L'\n', start);
        const auto line = Trim(text.substr(start, end == std::wstring::npos ? std::wstring::npos : end - start));
        if (!line.empty()) {
            const auto address = ExtractDohArgument(line, L"server");
            const auto templateUrl = ExtractDohArgument(line, L"dohtemplate");
            const bool duplicate = std::any_of(g_dohServers.begin(), g_dohServers.end(), [&](const auto& server) {
                return _wcsicmp(server.address.c_str(), address.c_str()) == 0;
            });
            if (IsValidDohEntry(address, templateUrl) && !duplicate) {
                if (RunHidden(L"netsh dns add encryption server=" + address + L" dohtemplate=\"" + templateUrl + L"\" autoupgrade=yes udpfallback=no")) ++imported;
            } else ++rejected;
        }
        if (end == std::wstring::npos) break;
        start = end + 1;
    }
    RefreshDohServers();
    const auto message = imported ? std::wstring(Tr(L"已成功导入 ", L"Imported ")) + std::to_wstring(imported) + Tr(L" 个 DoH 服务器。", L" DoH server(s).") +
        (rejected ? std::wstring(Tr(L" 已跳过无效或重复条目：", L" Skipped invalid or duplicate entries: ")) + std::to_wstring(rejected) : L"")
        : Tr(L"未识别到有效且未重复的 DoH 命令。", L"No valid, non-duplicate DoH commands found.");
    MessageBoxW(owner, message.c_str(), kTitle, MB_OK | (imported ? MB_ICONINFORMATION : MB_ICONWARNING));
    g_status = imported ? Tr(L"DoH · 快速导入成功", L"DoH · Import succeeded") : Tr(L"DoH · 快速导入失败", L"DoH · Import failed");
    if (imported) ShowToast(std::wstring(Tr(L"已导入 DoH 服务器：", L"DoH servers imported: ")) + std::to_wstring(imported));
    return imported > 0;
}

bool AddDohServer(HWND owner) {
    const auto address = PromptText(owner, Tr(L"添加 DoH 服务器", L"Add DoH server"), Tr(L"服务器 IP 地址", L"Server IP address"));
    if (address.empty()) return false;
    const auto templateUrl = PromptText(owner, Tr(L"添加 DoH 服务器", L"Add DoH server"), Tr(L"DoH 模板（https://…）", L"DoH template (https://…)"));
    if (!IsValidDohEntry(address, templateUrl)) { MessageBoxW(owner, Tr(L"请输入有效 IP 与不含空格的 https:// DoH 模板。", L"Enter a valid IP and an https:// DoH template without spaces."), kTitle, MB_OK | MB_ICONWARNING); return false; }
    if (std::any_of(g_dohServers.begin(), g_dohServers.end(), [&](const auto& server) { return _wcsicmp(server.address.c_str(), address.c_str()) == 0; })) {
        MessageBoxW(owner, Tr(L"该 DoH 服务器已存在。", L"This DoH server already exists."), kTitle, MB_OK | MB_ICONWARNING);
        return false;
    }
    const auto success = RunHidden(L"netsh dns add encryption server=" + address + L" dohtemplate=\"" + templateUrl + L"\" autoupgrade=yes udpfallback=no");
    if (success) {
        RefreshDohServers();
        ShowToast(std::wstring(Tr(L"已添加 DoH 服务器：", L"DoH server added: ")) + address);
    }
    return success;
}

bool DeleteDohServer(HWND owner, const DohServer& server) {
    const auto question = std::wstring(Tr(L"确定删除 DoH 服务器“", L"Delete DoH server \"")) + server.address + Tr(L"”吗？", L"\"?");
    if (MessageBoxW(owner, question.c_str(), kTitle, MB_OKCANCEL | MB_ICONWARNING) != IDOK) return false;
    const auto success = RunHidden(L"netsh dns delete encryption server=" + server.address);
    if (success) {
        RefreshDohServers();
        ShowToast(std::wstring(Tr(L"已删除 DoH 服务器：", L"DoH server deleted: ")) + server.address);
    }
    return success;
}

void RefreshStartWithWindows() {
    HKEY key{};
    wchar_t value[MAX_PATH * 2]{};
    DWORD type{}, size = sizeof(value);
    g_startWithWindows = RegOpenKeyExW(HKEY_CURRENT_USER, L"Software\\Microsoft\\Windows\\CurrentVersion\\Run", 0, KEY_READ, &key) == ERROR_SUCCESS &&
        RegQueryValueExW(key, L"WindowsExtendQuickSetting.Native", nullptr, &type, reinterpret_cast<BYTE*>(value), &size) == ERROR_SUCCESS;
    if (key) RegCloseKey(key);
}

bool SetStartWithWindows(bool enabled) {
    HKEY key{};
    if (RegCreateKeyExW(HKEY_CURRENT_USER, L"Software\\Microsoft\\Windows\\CurrentVersion\\Run", 0, nullptr, 0, KEY_SET_VALUE, nullptr, &key, nullptr) != ERROR_SUCCESS) return false;
    bool result = true;
    if (enabled) {
        wchar_t path[MAX_PATH]{};
        GetModuleFileNameW(nullptr, path, ARRAYSIZE(path));
        const std::wstring value = L"\"" + std::wstring(path) + L"\"";
        result = RegSetValueExW(key, L"WindowsExtendQuickSetting.Native", 0, REG_SZ, reinterpret_cast<const BYTE*>(value.c_str()),
            static_cast<DWORD>((value.size() + 1) * sizeof(wchar_t))) == ERROR_SUCCESS;
    } else result = RegDeleteValueW(key, L"WindowsExtendQuickSetting.Native") == ERROR_SUCCESS;
    RegCloseKey(key);
    if (result) g_startWithWindows = enabled;
    return result;
}

void RefreshLanguage() {
    HKEY key{};
    wchar_t value[16]{};
    DWORD type{}, size = sizeof(value);
    if (RegOpenKeyExW(HKEY_CURRENT_USER, L"Software\\WindowsExtendQuickSetting.Native", 0, KEY_READ, &key) == ERROR_SUCCESS) {
        if (RegQueryValueExW(key, L"Language", nullptr, &type, reinterpret_cast<BYTE*>(value), &size) == ERROR_SUCCESS && type == REG_SZ)
            g_isZh = _wcsicmp(value, L"en-US") != 0;
        RegCloseKey(key);
    }
}

bool SetLanguage(bool isZh) {
    HKEY key{};
    if (RegCreateKeyExW(HKEY_CURRENT_USER, L"Software\\WindowsExtendQuickSetting.Native", 0, nullptr, 0,
        KEY_SET_VALUE, nullptr, &key, nullptr) != ERROR_SUCCESS) return false;
    const std::wstring value = isZh ? L"zh-CN" : L"en-US";
    const bool success = RegSetValueExW(key, L"Language", 0, REG_SZ, reinterpret_cast<const BYTE*>(value.c_str()),
        static_cast<DWORD>((value.size() + 1) * sizeof(wchar_t))) == ERROR_SUCCESS;
    RegCloseKey(key);
    if (success) {
        g_isZh = isZh;
        ShowToast(isZh ? L"已切换为简体中文" : L"Switched to English");
    }
    return success;
}

bool ApplyWindowChrome() {
    if (!g_window) return false;
    const int roundedCorners = 2; // DWMWCP_ROUND
    const int noBorder = static_cast<int>(0xFFFFFFFE); // DWMWA_COLOR_NONE
    DwmSetWindowAttribute(g_window, 33, &roundedCorners, sizeof(roundedCorners)); // DWMWA_WINDOW_CORNER_PREFERENCE
    DwmSetWindowAttribute(g_window, 34, &noBorder, sizeof(noBorder)); // DWMWA_BORDER_COLOR
    return true;
}

bool LooksLikeUsbTethering(const IP_ADAPTER_ADDRESSES* address) {
    std::wstring value = std::wstring(address->FriendlyName ? address->FriendlyName : L"") + L" " +
        std::wstring(address->Description ? address->Description : L"");
    std::transform(value.begin(), value.end(), value.begin(), towlower);
    static const wchar_t* keywords[] = {
        L"rndis", L"remote ndis", L"mobile", L"android", L"iphone", L"apple", L"tether",
        L"usb ethernet", L"usb network", L"cdc ncm", L"cdc-ecm", L"mbim", L"wwan"
    };
    return std::any_of(std::begin(keywords), std::end(keywords), [&](const wchar_t* keyword) {
        return value.find(keyword) != std::wstring::npos;
    });
}

bool IsVirtualNetworkAdapter(const IP_ADAPTER_ADDRESSES* address) {
    std::wstring value = std::wstring(address->FriendlyName ? address->FriendlyName : L"") + L" " +
        std::wstring(address->Description ? address->Description : L"");
    std::transform(value.begin(), value.end(), value.begin(), towlower);
    static const wchar_t* blocked[] = {
        L"virtual", L"vethernet", L"hyper-v", L"loopback", L"bluetooth", L"tap", L"tun", L"vmware",
        L"virtualbox", L"docker", L"ndis", L"wfp", L"windows filter", L"microsoft wi-fi direct",
        L"localhost", L"isatap", L"teredo", L"6to4", L"pseudo", L"qos", L"npcap", L"packet driver",
        L"miniport", L"kernel debug", L"network monitor"
    };
    return std::any_of(std::begin(blocked), std::end(blocked), [&](const wchar_t* keyword) {
        return value.find(keyword) != std::wstring::npos;
    });
}


std::vector<GUID> GetWlanInterfaceGuids() {
    std::vector<GUID> result;
    HANDLE client{};
    DWORD negotiated{};
    if (WlanOpenHandle(2, nullptr, &negotiated, &client) != ERROR_SUCCESS) return result;
    PWLAN_INTERFACE_INFO_LIST interfaces{};
    if (WlanEnumInterfaces(client, nullptr, &interfaces) == ERROR_SUCCESS && interfaces) {
        for (DWORD index = 0; index < interfaces->dwNumberOfItems; ++index)
            result.push_back(interfaces->InterfaceInfo[index].InterfaceGuid);
        WlanFreeMemory(interfaces);
    }
    WlanCloseHandle(client, nullptr);
    return result;
}

bool IsKnownWlanInterface(const NET_LUID& luid, const std::vector<GUID>& wlanGuids) {
    GUID guid{};
    if (ConvertInterfaceLuidToGuid(&luid, &guid) != NO_ERROR) return false;
    return std::any_of(wlanGuids.begin(), wlanGuids.end(), [&](const GUID& candidate) {
        return InlineIsEqualGUID(guid, candidate);
    });
}


void RefreshNetworkAdapters() {
    g_networkAdapters.clear();
    g_ethernetOn = false;
    g_currentNetworkInfo = Tr(L"未连接网络", L"No network connection");
    g_currentIpInfo = L"IP: —";
    g_currentDnsInfo = L"DNS: —";
    PIP_ADAPTER_ADDRESSES currentNetwork{};
    const auto wlanInterfaceGuids = GetWlanInterfaceGuids();

    ULONG size = 16 * 1024;
    std::vector<BYTE> buffer(size);
    auto addresses = reinterpret_cast<PIP_ADAPTER_ADDRESSES>(buffer.data());
    auto result = GetAdaptersAddresses(AF_UNSPEC, GAA_FLAG_INCLUDE_PREFIX, nullptr, addresses, &size);
    if (result == ERROR_BUFFER_OVERFLOW) { buffer.resize(size); addresses = reinterpret_cast<PIP_ADAPTER_ADDRESSES>(buffer.data()); result = GetAdaptersAddresses(AF_UNSPEC, GAA_FLAG_INCLUDE_PREFIX, nullptr, addresses, &size); }
    if (result != NO_ERROR) return;
    for (auto* address = addresses; address; address = address->Next) {
        if (address->IfType != IF_TYPE_ETHERNET_CSMACD && address->IfType != IF_TYPE_IEEE80211) continue;
        if (IsVirtualNetworkAdapter(address) && !LooksLikeUsbTethering(address)) continue;
        if (address->IfType == IF_TYPE_IEEE80211 && !wlanInterfaceGuids.empty() && !IsKnownWlanInterface(address->Luid, wlanInterfaceGuids)) continue;

        MIB_IPINTERFACE_ROW row{};
        InitializeIpInterfaceEntry(&row);
        row.Family = AF_INET;
        row.InterfaceLuid = address->Luid;
        const auto metricOk = GetIpInterfaceEntry(&row) == NO_ERROR;
        MIB_IF_ROW2 interfaceRow{};
        interfaceRow.InterfaceLuid = address->Luid;
        const bool adapterEnabled = GetIfEntry2(&interfaceRow) == NO_ERROR &&
            interfaceRow.AdminStatus == NET_IF_ADMIN_STATUS_UP;
        g_networkAdapters.push_back({ address->FriendlyName ? address->FriendlyName : Tr(L"网络适配器", L"Network adapter"), address->Luid,
            metricOk ? row.Metric : 0, adapterEnabled });
        if (address->IfType == IF_TYPE_ETHERNET_CSMACD && adapterEnabled) g_ethernetOn = true;
        // Match WinUI3: a usable default gateway wins over arbitrary adapter enumeration order.
        if (address->OperStatus == IfOperStatusUp && (!currentNetwork || (address->FirstGatewayAddress && !currentNetwork->FirstGatewayAddress))) currentNetwork = address;
    }
    if (currentNetwork) {
        wchar_t ip[INET6_ADDRSTRLEN]{};
        for (auto* unicast = currentNetwork->FirstUnicastAddress; unicast; unicast = unicast->Next) {
            if (unicast->Address.lpSockaddr && unicast->Address.lpSockaddr->sa_family == AF_INET) {
                InetNtopW(AF_INET, &reinterpret_cast<sockaddr_in*>(unicast->Address.lpSockaddr)->sin_addr, ip, ARRAYSIZE(ip));
                break;
            }
        }
        const auto interfaceName = std::wstring(currentNetwork->FriendlyName ? currentNetwork->FriendlyName : Tr(L"网络", L"Network"));
        g_currentNetworkInfo = std::wstring(currentNetwork->IfType == IF_TYPE_IEEE80211 ? L"Wi-Fi · " : Tr(L"以太网 · ", L"Ethernet · ")) + interfaceName;
        g_currentIpInfo = L"IP: " + (ip[0] ? std::wstring(ip) : std::wstring(L"—"));
        std::wstring dns;
        for (auto* server = currentNetwork->FirstDnsServerAddress; server && dns.size() < 90; server = server->Next) {
            wchar_t value[INET6_ADDRSTRLEN]{};
            const auto family = server->Address.lpSockaddr ? server->Address.lpSockaddr->sa_family : AF_UNSPEC;
            if (family == AF_INET) InetNtopW(AF_INET, &reinterpret_cast<sockaddr_in*>(server->Address.lpSockaddr)->sin_addr, value, ARRAYSIZE(value));
            else if (family == AF_INET6) InetNtopW(AF_INET6, &reinterpret_cast<sockaddr_in6*>(server->Address.lpSockaddr)->sin6_addr, value, ARRAYSIZE(value));
            if (value[0]) { if (!dns.empty()) dns += L", "; dns += value; }
        }
        g_currentDnsInfo = L"DNS: " + (dns.empty() ? std::wstring(L"—") : dns);
    }
    if (g_dohServers.empty()) { g_dohPrimary = g_dohBackup = 0; }
    else {
        g_dohPrimary = std::min(g_dohPrimary, g_dohServers.size() - 1);
        g_dohBackup = std::min(g_dohBackup, g_dohServers.size() - 1);
    }
}

bool SetAdapterMetric(size_t index, ULONG metric) {
    if (index >= g_networkAdapters.size() || metric < 1 || metric > 9999) return false;
    MIB_IPINTERFACE_ROW row{};
    InitializeIpInterfaceEntry(&row);
    row.Family = AF_INET;
    row.InterfaceLuid = g_networkAdapters[index].luid;
    if (GetIpInterfaceEntry(&row) != NO_ERROR) return false;
    row.UseAutomaticMetric = FALSE;
    row.Metric = metric;
    const auto changed = SetIpInterfaceEntry(&row) == NO_ERROR;
    if (changed) RefreshNetworkAdapters();
    return changed;
}

void RefreshUsbTethering() {
    g_usbInterface.clear();
    g_usbOn = false;
    ULONG size = 16 * 1024;
    std::vector<BYTE> buffer(size);
    auto addresses = reinterpret_cast<PIP_ADAPTER_ADDRESSES>(buffer.data());
    auto result = GetAdaptersAddresses(AF_UNSPEC, 0, nullptr, addresses, &size);
    if (result == ERROR_BUFFER_OVERFLOW) { buffer.resize(size); addresses = reinterpret_cast<PIP_ADAPTER_ADDRESSES>(buffer.data()); result = GetAdaptersAddresses(AF_UNSPEC, 0, nullptr, addresses, &size); }
    if (result != NO_ERROR) return;
    for (auto* address = addresses; address; address = address->Next) {
        const std::wstring name = address->FriendlyName ? address->FriendlyName : L"";
        const std::wstring description = address->Description ? address->Description : L"";
        std::wstring lower = name;
        std::transform(lower.begin(), lower.end(), lower.begin(), towlower);
        std::wstring descriptionLower = description;
        std::transform(descriptionLower.begin(), descriptionLower.end(), descriptionLower.begin(), towlower);
        if (descriptionLower.find(L"rndis") != std::wstring::npos || descriptionLower.find(L"android") != std::wstring::npos ||
            descriptionLower.find(L"iphone") != std::wstring::npos || lower.find(L"tether") != std::wstring::npos || lower.find(L"mobile") != std::wstring::npos) {
            g_usbInterface = name;
            g_usbOn = address->OperStatus == IfOperStatusUp;
            return;
        }
    }
}

bool SetUsbTethering(bool enabled) {
    RefreshUsbTethering();
    if (!IsSafeNetshInterfaceName(g_usbInterface)) return false;
    const auto result = RunHidden(L"netsh interface set interface name=\"" + g_usbInterface + L"\" admin=" + (enabled ? L"enable" : L"disable"));
    RefreshUsbTethering();
    return result;
}

std::wstring WifiSsid(const DOT11_SSID& ssid) {
    if (ssid.uSSIDLength == 0) return Tr(L"已启用，未连接", L"Enabled, disconnected");
    const auto length = MultiByteToWideChar(CP_UTF8, 0, reinterpret_cast<const char*>(ssid.ucSSID),
        static_cast<int>(ssid.uSSIDLength), nullptr, 0);
    if (length <= 0) return Tr(L"已连接", L"Connected");
    std::wstring result(length, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, reinterpret_cast<const char*>(ssid.ucSSID),
        static_cast<int>(ssid.uSSIDLength), result.data(), length);
    return result;
}

void RefreshWifiStatus() {
    HANDLE client{};
    DWORD negotiated{};
    if (WlanOpenHandle(2, nullptr, &negotiated, &client) != ERROR_SUCCESS) {
        g_wifiOn = false;
        g_status = Tr(L"Wi-Fi · 系统 WLAN 服务不可用", L"Wi-Fi · WLAN service unavailable");
        return;
    }
    PWLAN_INTERFACE_INFO_LIST interfaces{};
    if (WlanEnumInterfaces(client, nullptr, &interfaces) != ERROR_SUCCESS || !interfaces || interfaces->dwNumberOfItems == 0) {
        g_wifiOn = false;
        g_status = Tr(L"Wi-Fi · 未检测到无线适配器", L"Wi-Fi · No wireless adapter");
    } else {
        g_wifiOn = false;
        bool connected = false;
        g_wifiAdapters.clear();
        for (DWORD index = 0; index < interfaces->dwNumberOfItems; ++index) {
            const auto& adapter = interfaces->InterfaceInfo[index];
            WifiAdapter wifiAdapter{ adapter.strInterfaceDescription, adapter.InterfaceGuid };
            g_wifiOn = g_wifiOn || adapter.isState != wlan_interface_state_not_ready;
            DWORD size{};
            WLAN_OPCODE_VALUE_TYPE valueType{};
            PWLAN_CONNECTION_ATTRIBUTES connection{};
            const auto query = WlanQueryInterface(client, &adapter.InterfaceGuid,
                wlan_intf_opcode_current_connection, nullptr, &size,
                reinterpret_cast<PVOID*>(&connection), &valueType);
            if (query == ERROR_SUCCESS && connection) {
                const auto ssid = WifiSsid(connection->wlanAssociationAttributes.dot11Ssid);
                WlanFreeMemory(connection);
                wifiAdapter.ssid = ssid;
                wifiAdapter.connected = ssid != Tr(L"已启用，未连接", L"Enabled, disconnected");
                if (ssid != Tr(L"已启用，未连接", L"Enabled, disconnected")) {
                    g_wifiOn = true;
                    if (!connected) { g_status = L"Wi-Fi · " + ssid; connected = true; }
                }
            }
            g_wifiAdapters.push_back(std::move(wifiAdapter));
        }
        if (!g_wifiAdapters.empty()) {
            g_wifiAdapterIndex = std::min(g_wifiAdapterIndex, g_wifiAdapters.size() - 1);
            if (g_wifiAdapterSelectedByUser && g_wifiAdapterIdSet) {
                const auto previous = std::find_if(g_wifiAdapters.begin(), g_wifiAdapters.end(), [](const auto& item) {
                    return IsEqualGUID(item.interfaceId, g_wifiAdapterId);
                });
                if (previous != g_wifiAdapters.end()) {
                    g_wifiAdapterIndex = static_cast<size_t>(previous - g_wifiAdapters.begin());
                } else {
                    // A USB wireless adapter may disappear between refreshes. Do not
                    // silently redirect its old index to an unrelated interface.
                    g_wifiAdapterSelectedByUser = false;
                    g_wifiAdapterIdSet = false;
                }
            }
            if (!g_wifiAdapterSelectedByUser) {
                const auto active = std::find_if(g_wifiAdapters.begin(), g_wifiAdapters.end(), [](const auto& item) { return item.connected; });
                if (active != g_wifiAdapters.end()) g_wifiAdapterIndex = static_cast<size_t>(active - g_wifiAdapters.begin());
            }
            g_wifiAdapterId = g_wifiAdapters[g_wifiAdapterIndex].interfaceId;
            g_wifiAdapterIdSet = true;
        }
        if (!connected)
            g_status = g_wifiOn ? Tr(L"Wi-Fi · 已启用，未连接", L"Wi-Fi · Enabled, disconnected") : Tr(L"Wi-Fi · 已关闭", L"Wi-Fi · Off");
    }
    if (interfaces) WlanFreeMemory(interfaces);
    WlanCloseHandle(client, nullptr);
}

bool DisconnectWifi() {
    HANDLE client{};
    DWORD negotiated{};
    if (WlanOpenHandle(2, nullptr, &negotiated, &client) != ERROR_SUCCESS) return false;
    PWLAN_INTERFACE_INFO_LIST interfaces{};
    bool disconnected = false;
    if (WlanEnumInterfaces(client, nullptr, &interfaces) == ERROR_SUCCESS && interfaces) {
        if (g_wifiAdapterIndex < g_wifiAdapters.size())
            disconnected = WlanDisconnect(client, &g_wifiAdapters[g_wifiAdapterIndex].interfaceId, nullptr) == ERROR_SUCCESS;
        WlanFreeMemory(interfaces);
    }
    WlanCloseHandle(client, nullptr);
    return disconnected;
}

void RefreshWifiNetworks();
void RefreshSavedWifi();

bool ChooseWifiAdapter(HWND owner) {
    RefreshWifiStatus();
    if (g_wifiAdapters.empty()) {
        g_status = Tr(L"Wi-Fi · 未检测到无线网卡", L"Wi-Fi · No wireless adapter detected");
        return false;
    }
    if (g_wifiAdapters.size() == 1) return true;
    std::vector<std::wstring> choices;
    choices.reserve(g_wifiAdapters.size());
    for (size_t index = 0; index < g_wifiAdapters.size(); ++index) {
        const auto& adapter = g_wifiAdapters[index];
        auto choice = adapter.name;
        if (adapter.connected) choice += std::wstring(Tr(L"  · 已连接：", L"  · Connected: ")) + adapter.ssid;
        else choice += Tr(L"  · 未连接", L"  · Disconnected");
        if (index == g_wifiAdapterIndex) choice += Tr(L"  · 当前", L"  · Current");
        choices.push_back(std::move(choice));
    }
    const auto choice = ChooseListItem(owner, Tr(L"选择 Wi-Fi 网卡", L"Choose Wi-Fi adapter"),
        Tr(L"选择用于查看和连接 Wi-Fi 的无线网卡：", L"Choose the wireless adapter used to view and connect Wi-Fi:"), choices);
    if (choice < 0) return false;
    g_wifiAdapterIndex = static_cast<size_t>(choice);
    g_wifiAdapterSelectedByUser = true;
    g_wifiAdapterId = g_wifiAdapters[g_wifiAdapterIndex].interfaceId;
    g_wifiAdapterIdSet = true;
    g_wifiAvailableOffset = g_wifiSavedOffset = 0;
    RefreshWifiNetworks();
    RefreshSavedWifi();
    ShowToast(std::wstring(Tr(L"Wi-Fi 网卡：", L"Wi-Fi adapter: ")) + g_wifiAdapters[g_wifiAdapterIndex].name);
    return true;
}

void RefreshWifiNetworks() {
    g_wifiNetworks.clear();
    HANDLE client{};
    DWORD negotiated{};
    if (WlanOpenHandle(2, nullptr, &negotiated, &client) != ERROR_SUCCESS) return;
    PWLAN_INTERFACE_INFO_LIST interfaces{};
    if (WlanEnumInterfaces(client, nullptr, &interfaces) == ERROR_SUCCESS && interfaces) {
        for (DWORD index = 0; index < interfaces->dwNumberOfItems; ++index) {
            const auto& adapter = interfaces->InterfaceInfo[index];
            if (!g_wifiAdapters.empty() && (g_wifiAdapterIndex >= g_wifiAdapters.size() || !IsEqualGUID(adapter.InterfaceGuid, g_wifiAdapters[g_wifiAdapterIndex].interfaceId))) continue;
            WlanScan(client, &adapter.InterfaceGuid, nullptr, nullptr, nullptr);
            PWLAN_AVAILABLE_NETWORK_LIST networks{};
            if (WlanGetAvailableNetworkList(client, &adapter.InterfaceGuid, 0, nullptr, &networks) == ERROR_SUCCESS && networks) {
                for (DWORD item = 0; item < networks->dwNumberOfItems; ++item) {
                    const auto& network = networks->Network[item];
                    const auto name = WifiSsid(network.dot11Ssid);
                    if (name.empty() || std::any_of(g_wifiNetworks.begin(), g_wifiNetworks.end(), [&](const auto& known) { return known.name == name; })) continue;
                    g_wifiNetworks.push_back({ name, network.wlanSignalQuality, network.bSecurityEnabled != FALSE,
                        (network.dwFlags & WLAN_AVAILABLE_NETWORK_HAS_PROFILE) != 0, adapter.InterfaceGuid,
                        network.dot11DefaultAuthAlgorithm, network.dot11DefaultCipherAlgorithm });
                }
                WlanFreeMemory(networks);
            }
        }
        WlanFreeMemory(interfaces);
    }
    WlanCloseHandle(client, nullptr);
    std::sort(g_wifiNetworks.begin(), g_wifiNetworks.end(), [](const auto& left, const auto& right) { return left.signal > right.signal; });
}

void RefreshSavedWifi() {
    g_savedWifi.clear();
    HANDLE client{};
    DWORD negotiated{};
    if (WlanOpenHandle(2, nullptr, &negotiated, &client) != ERROR_SUCCESS) return;
    PWLAN_INTERFACE_INFO_LIST interfaces{};
    if (WlanEnumInterfaces(client, nullptr, &interfaces) == ERROR_SUCCESS && interfaces) {
        for (DWORD index = 0; index < interfaces->dwNumberOfItems; ++index) {
            PWLAN_PROFILE_INFO_LIST profiles{};
            const auto id = interfaces->InterfaceInfo[index].InterfaceGuid;
            if (!g_wifiAdapters.empty() && (g_wifiAdapterIndex >= g_wifiAdapters.size() || !IsEqualGUID(id, g_wifiAdapters[g_wifiAdapterIndex].interfaceId))) continue;
            if (WlanGetProfileList(client, &id, nullptr, &profiles) == ERROR_SUCCESS && profiles) {
                for (DWORD item = 0; item < profiles->dwNumberOfItems; ++item) {
                    const std::wstring name(profiles->ProfileInfo[item].strProfileName);
                    auto lower = [](std::wstring value) { std::transform(value.begin(), value.end(), value.begin(), towlower); return value; };
                    if (!name.empty() && (g_wifiSearch.empty() || lower(name).find(lower(g_wifiSearch)) != std::wstring::npos) && std::none_of(g_savedWifi.begin(), g_savedWifi.end(), [&](const auto& saved) { return saved.name == name; }))
                        g_savedWifi.push_back({ name, id });
                }
                WlanFreeMemory(profiles);
            }
        }
        WlanFreeMemory(interfaces);
    }
    WlanCloseHandle(client, nullptr);
    std::sort(g_savedWifi.begin(), g_savedWifi.end(), [](const auto& left, const auto& right) { return left.name < right.name; });
}

bool ForgetSavedWifi(const SavedWifi& profile) {
    const auto forgetQuestion = std::wstring(Tr(L"确定忘记已保存的 Wi-Fi“", L"Forget saved Wi-Fi \"")) + profile.name + Tr(L"”吗？", L"\"?");
    if (MessageBoxW(g_window, forgetQuestion.c_str(), kTitle,
        MB_OKCANCEL | MB_ICONWARNING) != IDOK) return false;
    HANDLE client{};
    DWORD negotiated{};
    if (WlanOpenHandle(2, nullptr, &negotiated, &client) != ERROR_SUCCESS) return false;
    PWSTR xml{}; DWORD flags = 0, access = 0;
    WlanGetProfile(client, &profile.interfaceId, profile.name.c_str(), nullptr, &xml, &flags, &access);
    const auto result = WlanDeleteProfile(client, &profile.interfaceId, profile.name.c_str(), nullptr) == ERROR_SUCCESS;
    if (result && xml) { g_lastForgottenName = profile.name; g_lastForgottenXml = xml; g_lastForgottenInterface = profile.interfaceId; }
    if (xml) WlanFreeMemory(xml);
    WlanCloseHandle(client, nullptr);
    if (result) {
        g_status = std::wstring(Tr(L"已忘记 Wi-Fi：", L"Forgot Wi-Fi: ")) + profile.name;
        RefreshSavedWifi();
        ShowToast(std::wstring(Tr(L"已忘记 ", L"Forgot ")) + profile.name, Tr(L"撤销", L"Undo"));
    }
    return result;
}

bool UndoForgetWifi() {
    if (g_lastForgottenName.empty() || g_lastForgottenXml.empty()) return false;
    HANDLE client{}; DWORD negotiated{};
    if (WlanOpenHandle(2, nullptr, &negotiated, &client) != ERROR_SUCCESS) return false;
    DWORD reason = 0;
    const auto ok = WlanSetProfile(client, &g_lastForgottenInterface, 0, g_lastForgottenXml.c_str(), nullptr, TRUE, nullptr, &reason) == ERROR_SUCCESS;
    WlanCloseHandle(client, nullptr);
    if (ok) {
        g_status = std::wstring(Tr(L"已撤销忘记 Wi-Fi：", L"Restored Wi-Fi: ")) + g_lastForgottenName;
        RefreshSavedWifi();
        ShowToast(std::wstring(Tr(L"已恢复 ", L"Restored ")) + g_lastForgottenName);
        g_lastForgottenName.clear();
        g_lastForgottenXml.clear();
    }
    return ok;
}

bool ApplySelectedDoh() {
    if (g_dohServers.empty() || g_networkAdapters.empty()) return false;
    const auto active = std::find_if(g_networkAdapters.begin(), g_networkAdapters.end(), [](const NetworkAdapter& adapter) { return adapter.up; });
    if (active == g_networkAdapters.end()) return false;
    const auto& iface = active->name;
    if (!IsSafeNetshInterfaceName(iface)) return false;
    const auto& primary = g_dohServers[std::min(g_dohPrimary, g_dohServers.size() - 1)].address;
    std::wstring cmd = L"netsh interface ip set dns name=\"" + iface + L"\" static " + primary + L" primary";
    if (!RunHidden(cmd)) return false;
    if (g_dohServers.size() > 1) {
        const auto& backup = g_dohServers[std::min(g_dohBackup, g_dohServers.size() - 1)].address;
        // A duplicate secondary resolver adds no value; a distinct one must succeed
        // before the UI is allowed to report the selection as applied.
        if (_wcsicmp(primary.c_str(), backup.c_str()) != 0 &&
            !RunHidden(L"netsh interface ip add dns name=\"" + iface + L"\" " + backup + L" index=2")) return false;
    }
    g_dohApplied = true;
    return true;
}

bool ConfirmApplySelectedDoh(HWND owner) {
    if (g_dohServers.empty()) return false;
    const auto active = std::find_if(g_networkAdapters.begin(), g_networkAdapters.end(), [](const NetworkAdapter& adapter) { return adapter.up; });
    if (active == g_networkAdapters.end()) return false;
    const auto& primary = g_dohServers[std::min(g_dohPrimary, g_dohServers.size() - 1)].address;
    const auto& backup = g_dohServers.size() > 1 ? g_dohServers[std::min(g_dohBackup, g_dohServers.size() - 1)].address : std::wstring();
    const auto message = g_isZh
        ? L"将把网络接口“" + active->name + L"”的 DNS 设为：\n首选 " + primary +
            (backup.empty() ? L"" : L"，备用 " + backup) + L"\nWindows 将通过对应的 DoH 模板进行加密解析（会覆盖当前 DNS 设置）。"
        : L"Set DNS on interface '" + active->name + L"' to:\nprimary " + primary +
            (backup.empty() ? L"" : L", backup " + backup) + L"\nWindows will resolve via the matching DoH templates (overwrites current DNS settings).";
    return MessageBoxW(owner, message.c_str(), Tr(L"应用首选 / 备用 DoH", L"Apply primary / backup DoH"),
        MB_OKCANCEL | MB_ICONWARNING | MB_DEFBUTTON2) == IDOK;
}

std::wstring ReadSavedWifiPassword(const SavedWifi& profile) {
    HANDLE client{};
    DWORD negotiated{};
    PWSTR xml{};
    DWORD flags = WLAN_PROFILE_GET_PLAINTEXT_KEY;
    DWORD access{};
    if (WlanOpenHandle(2, nullptr, &negotiated, &client) != ERROR_SUCCESS) return {};
    const auto result = WlanGetProfile(client, &profile.interfaceId, profile.name.c_str(), nullptr, &xml, &flags, &access);
    std::wstring content = result == ERROR_SUCCESS && xml ? xml : L"";
    if (xml) WlanFreeMemory(xml);
    WlanCloseHandle(client, nullptr);
    const auto begin = content.find(L"<keyMaterial>");
    const auto end = content.find(L"</keyMaterial>");
    if (begin == std::wstring::npos || end == std::wstring::npos || end <= begin + 13) return {};
    return content.substr(begin + 13, end - begin - 13);
}

bool ConnectSavedWifi(const WifiNetwork& network) {
    if (!network.saved) return false;
    HANDLE client{};
    DWORD negotiated{};
    if (WlanOpenHandle(2, nullptr, &negotiated, &client) != ERROR_SUCCESS) return false;
    WLAN_CONNECTION_PARAMETERS parameters{};
    parameters.wlanConnectionMode = wlan_connection_mode_profile;
    parameters.strProfile = network.name.c_str();
    parameters.dot11BssType = dot11_BSS_type_any;
    const auto result = WlanConnect(client, &network.interfaceId, &parameters, nullptr) == ERROR_SUCCESS;
    WlanCloseHandle(client, nullptr);
    if (result) g_status = std::wstring(Tr(L"Wi-Fi · 正在连接 ", L"Wi-Fi · Connecting ")) + network.name;
    return result;
}

std::wstring XmlEscape(std::wstring value) {
    const std::pair<const wchar_t*, const wchar_t*> replacements[] = {
        { L"&", L"&amp;" }, { L"<", L"&lt;" }, { L">", L"&gt;" }, { L"\"", L"&quot;" }, { L"'", L"&apos;" }
    };
    for (const auto& replacement : replacements) {
        size_t position = 0;
        while ((position = value.find(replacement.first, position)) != std::wstring::npos) {
            value.replace(position, wcslen(replacement.first), replacement.second);
            position += wcslen(replacement.second);
        }
    }
    return value;
}

bool ConnectNewWifi(HWND owner, const WifiNetwork& network) {
    if (network.saved) return ConnectSavedWifi(network);
    HANDLE client{};
    DWORD negotiated{};
    if (WlanOpenHandle(2, nullptr, &negotiated, &client) != ERROR_SUCCESS) return false;

    if (!network.secured) {
        DOT11_SSID ssid{};
        const int bytes = WideCharToMultiByte(CP_UTF8, 0, network.name.c_str(), -1, reinterpret_cast<char*>(ssid.ucSSID), DOT11_SSID_MAX_LENGTH, nullptr, nullptr);
        ssid.uSSIDLength = bytes > 0 ? static_cast<ULONG>(bytes - 1) : 0;
        WLAN_CONNECTION_PARAMETERS parameters{};
        parameters.wlanConnectionMode = wlan_connection_mode_discovery_unsecure;
        parameters.pDot11Ssid = &ssid;
        parameters.dot11BssType = dot11_BSS_type_any;
        const bool success = WlanConnect(client, &network.interfaceId, &parameters, nullptr) == ERROR_SUCCESS;
        WlanCloseHandle(client, nullptr);
        if (success) g_status = std::wstring(Tr(L"Wi-Fi · 正在连接 ", L"Wi-Fi · Connecting ")) + network.name;
        return success;
    }

    const auto passwordLabel = std::wstring(Tr(L"输入“", L"Enter password for \"")) + network.name + Tr(L"”的密码（至少 8 位）", L"\" (at least 8 characters)");
    const auto password = PromptText(owner, Tr(L"连接 Wi-Fi", L"Connect Wi-Fi"), passwordLabel.c_str(), false, true);
    if (password.size() < 8 || password.size() > 63) {
        WlanCloseHandle(client, nullptr);
        if (!password.empty()) MessageBoxW(owner, Tr(L"Wi-Fi 密码必须为 8 到 63 个字符。", L"The Wi-Fi password must contain 8 to 63 characters."), kTitle, MB_OK | MB_ICONWARNING);
        return false;
    }
    const wchar_t* authentication = network.auth == DOT11_AUTH_ALGO_WPA_PSK ? L"WPAPSK" :
        network.auth == DOT11_AUTH_ALGO_WPA3_SAE ? L"WPA3SAE" : L"WPA2PSK";
    const wchar_t* encryption = network.cipher == DOT11_CIPHER_ALGO_TKIP ? L"TKIP" : L"AES";
    const auto name = XmlEscape(network.name);
    const auto key = XmlEscape(password);
    const auto profile = L"<?xml version=\"1.0\"?><WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\"><name>" + name +
        L"</name><SSIDConfig><SSID><name>" + name + L"</name></SSID></SSIDConfig><connectionType>ESS</connectionType><connectionMode>auto</connectionMode>"
        L"<MSM><security><authEncryption><authentication>" + authentication + L"</authentication><encryption>" + encryption +
        L"</encryption><useOneX>false</useOneX></authEncryption><sharedKey><keyType>passPhrase</keyType><protected>false</protected><keyMaterial>" +
        key + L"</keyMaterial></sharedKey></security></MSM></WLANProfile>";
    DWORD reason{};
    const auto setResult = WlanSetProfile(client, &network.interfaceId, 0, profile.c_str(), nullptr, TRUE, nullptr, &reason);
    WlanCloseHandle(client, nullptr);
    if (setResult != ERROR_SUCCESS) {
        MessageBoxW(owner, Tr(L"无法创建 Wi-Fi 配置。该网络可能使用企业证书或当前不支持的认证方式。", L"Unable to create the Wi-Fi profile. The network may use enterprise certificates or an unsupported authentication method."), kTitle, MB_OK | MB_ICONWARNING);
        return false;
    }
    WifiNetwork saved = network;
    saved.saved = true;
    const bool success = ConnectSavedWifi(saved);
    RefreshSavedWifi();
    return success;
}

void RefreshBluetoothAdapters() {
    const bool wasEnabled = g_bluetoothOn;
    g_bluetoothAdapters.clear();
    const HDEVINFO devices = SetupDiGetClassDevsW(&GUID_DEVCLASS_BLUETOOTH, nullptr, nullptr, DIGCF_PRESENT);
    if (devices == INVALID_HANDLE_VALUE) { g_bluetoothOn = false; return; }
    for (DWORD index = 0;; ++index) {
        SP_DEVINFO_DATA device{ sizeof(device) };
        if (!SetupDiEnumDeviceInfo(devices, index, &device)) break;
        wchar_t name[512]{};
        DWORD type{};
        if (!SetupDiGetDeviceRegistryPropertyW(devices, &device, SPDRP_FRIENDLYNAME, &type,
            reinterpret_cast<PBYTE>(name), sizeof(name), nullptr)) {
            SetupDiGetDeviceRegistryPropertyW(devices, &device, SPDRP_DEVICEDESC, &type,
                reinterpret_cast<PBYTE>(name), sizeof(name), nullptr);
        }
        wchar_t service[256]{};
        wchar_t hardwareIds[1024]{};
        SetupDiGetDeviceRegistryPropertyW(devices, &device, SPDRP_SERVICE, &type,
            reinterpret_cast<PBYTE>(service), sizeof(service), nullptr);
        SetupDiGetDeviceRegistryPropertyW(devices, &device, SPDRP_HARDWAREID, &type,
            reinterpret_cast<PBYTE>(hardwareIds), sizeof(hardwareIds), nullptr);
        const auto serviceLower = Lower(service);
        const auto hardwareLower = Lower(hardwareIds);
        const bool physicalAdapter = serviceLower.find(L"bthusb") != std::wstring::npos ||
            hardwareLower.rfind(L"usb\\vid_", 0) == 0 || hardwareLower.rfind(L"pci\\ven_", 0) == 0;
        if (name[0] != L'\0' && physicalAdapter) {
            wchar_t instanceId[MAX_DEVICE_ID_LEN]{};
            CM_Get_Device_IDW(device.DevInst, instanceId, ARRAYSIZE(instanceId), 0);
            ULONG status{}, problem{};
            const auto enabled = CM_Get_DevNode_Status(&status, &problem, device.DevInst, 0) == CR_SUCCESS &&
                (status & DN_HAS_PROBLEM) == 0;
            g_bluetoothAdapters.push_back({ name, device.DevInst, instanceId, enabled });
        }
    }
    SetupDiDestroyDeviceInfoList(devices);
    g_bluetoothOn = std::any_of(g_bluetoothAdapters.begin(), g_bluetoothAdapters.end(), [](const auto& adapter) { return adapter.enabled; });
    // Do not interrupt the user with a modal dialog from the periodic refresh.  A
    // visible, actionable prompt is shown on the next Bluetooth-card click instead.
    if (wasEnabled && !g_bluetoothOn && !g_bluetoothAdapters.empty()) {
        g_bluetoothRecoveryPending = true;
        ShowToast(Tr(L"蓝牙适配器已断开，点击蓝牙卡选择要重新启用的适配器", L"Bluetooth adapter disconnected. Click the Bluetooth card to choose an adapter."));
    }
    if (g_bluetoothOn) g_bluetoothRecoveryPending = false;
}

void RefreshBluetoothDevices() {
    g_bluetoothDevices.clear();
    BLUETOOTH_DEVICE_SEARCH_PARAMS search{};
    search.dwSize = sizeof(search);
    search.fReturnAuthenticated = TRUE;
    search.fReturnRemembered = TRUE;
    search.fReturnConnected = TRUE;
    search.fReturnUnknown = TRUE;
    search.fIssueInquiry = TRUE;
    search.cTimeoutMultiplier = 2;
    BLUETOOTH_DEVICE_INFO device{};
    device.dwSize = sizeof(device);
    auto handle = BluetoothFindFirstDevice(&search, &device);
    if (!handle) return;
    do {
        if (device.szName[0]) g_bluetoothDevices.push_back({ device.szName, device.Address, device.fAuthenticated != FALSE, device.fConnected != FALSE });
        device = {};
        device.dwSize = sizeof(device);
    } while (BluetoothFindNextDevice(handle, &device));
    BluetoothFindDeviceClose(handle);
    const auto maximum = g_bluetoothDevices.size() > 4 ? g_bluetoothDevices.size() - 4 : 0;
    g_bluetoothDeviceOffset = std::min(g_bluetoothDeviceOffset, maximum);
}

bool ToggleBluetoothDevice(HWND owner, const BluetoothDevice& target) {
    if (target.paired) {
        const auto removeQuestion = std::wstring(Tr(L"确定移除蓝牙设备“", L"Remove Bluetooth device \"")) + target.name + Tr(L"”吗？", L"\"?");
        if (MessageBoxW(owner, removeQuestion.c_str(), kTitle, MB_OKCANCEL | MB_ICONWARNING) != IDOK) return false;
        if (BluetoothRemoveDevice(&target.address) != ERROR_SUCCESS) return false;
    } else {
        BLUETOOTH_DEVICE_INFO device{};
        device.dwSize = sizeof(device);
        device.Address = target.address;
        if (BluetoothGetDeviceInfo(nullptr, &device) != ERROR_SUCCESS ||
            BluetoothAuthenticateDeviceEx(nullptr, nullptr, &device, nullptr, MITMProtectionNotRequired) != ERROR_SUCCESS) return false;
    }
    RefreshBluetoothDevices();
    return true;
}

bool SetBluetoothAdapterEnabled(size_t selected, bool enabled) {
    if (selected >= g_bluetoothAdapters.size()) return false;
    std::wstring command;
    if (enabled) {
        for (size_t index = 0; index < g_bluetoothAdapters.size(); ++index) {
            if (index != selected && g_bluetoothAdapters[index].enabled) {
                // A failed disable must stop the switch; otherwise two adapters can remain enabled.
                if (!command.empty()) command += L" && ";
                command += L"pnputil /disable-device \"" + g_bluetoothAdapters[index].instanceId + L"\"";
            }
        }
        if (!command.empty()) command += L" && ";
        command += L"pnputil /enable-device \"" + g_bluetoothAdapters[selected].instanceId + L"\"";
    } else {
        command = L"pnputil /disable-device \"" + g_bluetoothAdapters[selected].instanceId + L"\"";
    }
    if (!RunHidden(command)) return false;
    RefreshBluetoothAdapters();
    return true;
}

bool ChooseBluetoothAdapter(HWND owner, bool recovery) {
    RefreshBluetoothAdapters();
    if (g_bluetoothAdapters.empty()) {
        g_status = Tr(L"蓝牙 · 未检测到物理蓝牙适配器", L"Bluetooth · No physical adapter detected");
        return false;
    }
    if (g_bluetoothAdapters.size() == 1) {
        const bool target = recovery ? true : !g_bluetoothAdapters.front().enabled;
        const bool success = SetBluetoothAdapterEnabled(0, target);
        g_status = success ? (target ? Tr(L"蓝牙 · 已启用", L"Bluetooth · Enabled") : Tr(L"蓝牙 · 已禁用", L"Bluetooth · Disabled"))
                           : Tr(L"蓝牙 · 适配器操作失败", L"Bluetooth · Adapter operation failed");
        return success;
    }

    std::vector<std::wstring> choices;
    choices.reserve(g_bluetoothAdapters.size());
    for (size_t index = 0; index < g_bluetoothAdapters.size(); ++index) {
        choices.push_back(g_bluetoothAdapters[index].name +
            (g_bluetoothAdapters[index].enabled ? Tr(L"  · 当前使用", L"  · Current") : Tr(L"  · 未使用", L"  · Inactive")));
    }
    const auto choice = ChooseListItem(owner, Tr(L"选择蓝牙适配器", L"Choose Bluetooth adapter"), recovery
        ? Tr(L"当前适配器已断开；请选择要重新启用的蓝牙适配器：", L"The current adapter is unavailable. Choose an adapter to re-enable:")
        : Tr(L"Windows 同一时间只能启用一个蓝牙适配器：", L"Windows can enable only one Bluetooth adapter at a time:"), choices);
    if (choice < 0) return false;
    const auto selected = static_cast<size_t>(choice);
    if (g_bluetoothAdapters[selected].enabled) {
        g_status = std::wstring(Tr(L"蓝牙 · 正在使用 ", L"Bluetooth · Using ")) + g_bluetoothAdapters[selected].name;
        return true;
    }
    const auto question = std::wstring(Tr(L"切换到“", L"Switch to \"")) + g_bluetoothAdapters[selected].name +
        Tr(L"”吗？当前启用的蓝牙适配器将被禁用。", L"\"? The currently enabled Bluetooth adapter will be disabled.");
    if (MessageBoxW(owner, question.c_str(), Tr(L"切换蓝牙适配器", L"Switch Bluetooth adapter"), MB_OKCANCEL | MB_ICONWARNING) != IDOK) return false;
    const bool success = SetBluetoothAdapterEnabled(selected, true);
    g_status = success ? std::wstring(Tr(L"蓝牙 · 已切换到 ", L"Bluetooth · Switched to ")) + g_bluetoothAdapters[selected].name
                       : Tr(L"蓝牙 · 切换失败，当前适配器保持不变", L"Bluetooth · Switch failed; current adapter was left unchanged");
    return success;
}

void DrawText(HDC dc, const std::wstring& text, int x, int y, int width, int height, COLORREF color, int size, bool bold = false) {
    HFONT font = CreateFontW(-size, 0, 0, 0, bold ? FW_SEMIBOLD : FW_NORMAL, FALSE, FALSE, FALSE,
        DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY,
        DEFAULT_PITCH | FF_DONTCARE, L"Microsoft YaHei UI");
    auto old = SelectObject(dc, font);
    SetBkMode(dc, TRANSPARENT);
    SetTextColor(dc, color);
    RECT rect{x, y, x + width, y + height};
    DrawTextW(dc, text.c_str(), -1, &rect, DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
    SelectObject(dc, old);
    DeleteObject(font);
}

void DrawGlyph(HDC dc, const std::wstring& glyph, int x, int y, int width, int height, COLORREF color, int size) {
    HFONT font = CreateFontW(-size, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE,
        DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY,
        DEFAULT_PITCH | FF_DONTCARE, L"Segoe Fluent Icons");
    const auto old = SelectObject(dc, font);
    SetBkMode(dc, TRANSPARENT);
    SetTextColor(dc, color);
    RECT rect{ x, y, x + width, y + height };
    DrawTextW(dc, glyph.c_str(), -1, &rect, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
    SelectObject(dc, old);
    DeleteObject(font);
}

void DrawCenteredText(HDC dc, const std::wstring& text, int x, int y, int width, int height, COLORREF color, int size) {
    HFONT font = CreateFontW(-size, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE,
        DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY,
        DEFAULT_PITCH | FF_DONTCARE, L"Microsoft YaHei UI");
    const auto old = SelectObject(dc, font);
    SetBkMode(dc, TRANSPARENT);
    SetTextColor(dc, color);
    RECT rect{ x, y, x + width, y + height };
    DrawTextW(dc, text.c_str(), -1, &rect, DT_CENTER | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
    SelectObject(dc, old);
    DeleteObject(font);
}

void FillRoundRect(HDC dc, const RECT& rect, COLORREF color, int radius = 10) {
    HBRUSH brush = CreateSolidBrush(color);
    HPEN pen = CreatePen(PS_SOLID, 1, color);
    auto oldBrush = SelectObject(dc, brush);
    auto oldPen = SelectObject(dc, pen);
    RoundRect(dc, rect.left, rect.top, rect.right, rect.bottom, radius, radius);
    SelectObject(dc, oldPen);
    SelectObject(dc, oldBrush);
    DeleteObject(pen);
    DeleteObject(brush);
}

void DrawActionButton(HDC dc, const RECT& rect, const std::wstring& label, bool enabled = true, bool primary = false) {
    const auto fill = !enabled ? RGB(242, 242, 242) : primary ? RGB(0, 103, 192) : RGB(235, 243, 251);
    const auto border = !enabled ? RGB(220, 220, 220) : primary ? RGB(0, 103, 192) : RGB(185, 211, 235);
    HBRUSH brush = CreateSolidBrush(fill);
    HPEN pen = CreatePen(PS_SOLID, 1, border);
    const auto oldBrush = SelectObject(dc, brush);
    const auto oldPen = SelectObject(dc, pen);
    RoundRect(dc, rect.left, rect.top, rect.right, rect.bottom, 8, 8);
    SelectObject(dc, oldPen);
    SelectObject(dc, oldBrush);
    DeleteObject(pen);
    DeleteObject(brush);
    DrawCenteredText(dc, label, rect.left + 4, rect.top, rect.right - rect.left - 8, rect.bottom - rect.top,
        !enabled ? RGB(145, 145, 145) : primary ? RGB(255, 255, 255) : RGB(0, 82, 155), 10);
}

void DrawWhiteFade(HDC dc, int left, int top, int right, int bottom, bool strongestAtTop) {
    if (right <= left || bottom <= top) return;
    HDC source = CreateCompatibleDC(dc);
    HBITMAP pixel = CreateCompatibleBitmap(dc, 1, 1);
    const auto oldBitmap = SelectObject(source, pixel);
    SetPixel(source, 0, 0, RGB(255, 255, 255));
    const int height = bottom - top;
    for (int row = 0; row < height; ++row) {
        const int strength = strongestAtTop ? height - row : row + 1;
        const BYTE alpha = static_cast<BYTE>(35 + (185 * strength) / height);
        BLENDFUNCTION blend{ AC_SRC_OVER, 0, alpha, 0 };
        AlphaBlend(dc, left, top + row, right - left, 1, source, 0, 0, 1, 1, blend);
    }
    SelectObject(source, oldBitmap);
    DeleteObject(pixel);
    DeleteDC(source);
}

void DrawTile(HDC dc, const RECT& rect, const wchar_t* icon, const wchar_t* label, bool enabled,
              bool hoverMain = false, bool hoverExpand = false, bool expanded = false) {
    const auto base = enabled ? RGB(0, 103, 192) : RGB(245, 245, 245);
    const auto hover = RGB(0, 62, 120);
    FillRoundRect(dc, rect, base, 9);
    const int middle = (rect.left + rect.right) / 2;
    // WinUI3 renders the icon and Chevron as two buttons. Clip the hovered
    // half to the rounded outer tile so inactive cards never lose their corner.
    const auto drawHoverHalf = [&](const RECT& half) {
        HBRUSH brush = CreateSolidBrush(hover);
        HRGN rounded = CreateRoundRectRgn(rect.left, rect.top, rect.right + 1, rect.bottom + 1, 18, 18);
        HRGN halfRegion = CreateRectRgn(half.left, half.top, half.right, half.bottom);
        CombineRgn(rounded, rounded, halfRegion, RGN_AND);
        const int savedDc = SaveDC(dc);
        SelectClipRgn(dc, rounded);
        FillRect(dc, &rect, brush);
        RestoreDC(dc, savedDc);
        DeleteObject(halfRegion);
        DeleteObject(rounded);
        DeleteObject(brush);
    };
    if (hoverMain) drawHoverHalf(RECT{rect.left, rect.top, middle, rect.bottom});
    if (hoverExpand) {
        drawHoverHalf(RECT{middle, rect.top, rect.right, rect.bottom});
    }
    HPEN divider = CreatePen(PS_SOLID, 1, enabled ? RGB(150, 200, 238) : RGB(218, 218, 218));
    const auto oldPen = SelectObject(dc, divider);
    MoveToEx(dc, middle, rect.top + 1, nullptr);
    LineTo(dc, middle, rect.bottom - 1);
    SelectObject(dc, oldPen);
    DeleteObject(divider);
    const auto mainColor = (enabled || hoverMain) ? RGB(255, 255, 255) : RGB(38, 38, 38);
    const auto expandColor = (enabled || hoverExpand) ? RGB(255, 255, 255) : RGB(38, 38, 38);
    DrawGlyph(dc, icon, rect.left + 8, rect.top + 7, middle - rect.left - 16, 34, mainColor, 17);
    DrawGlyph(dc, expanded ? L"\xE70D" : L"\xE76C", middle + 6, rect.top + 8,
        rect.right - middle - 12, 32, expandColor, 13);
    DrawCenteredText(dc, label, rect.left - 4, rect.bottom + 5, rect.right - rect.left + 8, 20, RGB(45, 45, 45), 11);
}

void PaintWindow(HWND hwnd) {
    PAINTSTRUCT ps{};
    auto dc = BeginPaint(hwnd, &ps);
    RECT client{};
    GetClientRect(hwnd, &client);
    auto background = CreateSolidBrush(RGB(233, 239, 245));
    FillRect(dc, &client, background);
    DeleteObject(background);

    if (g_page == Page::Settings) {
        DrawText(dc, Tr(L"设置", L"Settings"), 24, 18, 250, 30, RGB(25, 25, 25), 20, true);

        const auto drawSettingsCard = [&](int top, const wchar_t* glyph, const wchar_t* title,
                                          const wchar_t* description, const std::wstring& value) {
            const RECT card{24, top, 344, top + 72};
            FillRoundRect(dc, card, RGB(255, 255, 255), 8);
            DrawGlyph(dc, glyph, 38, top + 20, 30, 30, RGB(55, 55, 55), 17);
            DrawText(dc, title, 78, top + 12, 150, 25, RGB(30, 30, 30), 13, true);
            DrawText(dc, description, 78, top + 36, 170, 22, RGB(105, 105, 105), 11);
            if (!value.empty()) DrawText(dc, value, 246, top + 22, 84, 28, RGB(0, 103, 192), 11, true);
        };

        drawSettingsCard(62, L"\xE7E8", Tr(L"开机自启动", L"Start with Windows"),
            Tr(L"登录 Windows 后自动运行", L"Run automatically after sign-in"),
            g_startWithWindows ? Tr(L"已开启", L"On") : Tr(L"已关闭", L"Off"));
        drawSettingsCard(146, L"\xE8C1", Tr(L"语言", L"Language"),
            Tr(L"界面显示语言", L"Display language"), g_isZh ? L"中文  \xE70D" : L"English  \xE70D");
        drawSettingsCard(230, L"\xE839", Tr(L"上网方式优先级", L"Connection priority"),
            Tr(L"调整以太网、Wi-Fi、USB 共享的优先级", L"Set Ethernet, Wi-Fi and USB tether priority"),
            Tr(L"管理", L"Manage"));
        drawSettingsCard(314, L"\xE946", Tr(L"关于", L"About"),
            (std::wstring(L"WindowsExtendQuickSetting Native v") + GetNativeVersion()).c_str(), L"");
        DrawText(dc, Tr(L"网络与蓝牙快速控制", L"Quick network and Bluetooth controls"),
            78, 363, 240, 22, RGB(105, 105, 105), 11);
        // WinUI3 keeps navigation in the footer; use the same stable location
        // for returning instead of a separate top-left back affordance.
        DrawGlyph(dc, L"\xE72B", 308, 608, 32, 28, RGB(50, 50, 50), 17);
        EndPaint(hwnd, &ps);
        return;
    }
    if (g_page == Page::Doh) {
        DrawGlyph(dc, L"\xE72B", 22, 18, 28, 28, RGB(35, 35, 35), 16);
        DrawText(dc, Tr(L"DoH 设置", L"DoH settings"), 58, 18, 250, 30, RGB(25, 25, 25), 20, true);
        const auto primary = g_dohServers.empty() ? Tr(L"未选择", L"None") : g_dohServers[std::min(g_dohPrimary, g_dohServers.size() - 1)].address.c_str();
        const auto backup = g_dohServers.size() < 2 ? Tr(L"未选择", L"None") : g_dohServers[std::min(g_dohBackup, g_dohServers.size() - 1)].address.c_str();
        FillRoundRect(dc, RECT{24, 62, 344, 166}, RGB(255, 255, 255), 10);
        DrawGlyph(dc, L"\xE774", 38, 78, 30, 30, RGB(55, 55, 55), 17);
        DrawText(dc, Tr(L"全局 DoH", L"Global DoH"), 78, 72, 160, 25, RGB(30, 30, 30), 13, true);
        DrawText(dc, g_dohEnabled ? Tr(L"已启用，点击切换", L"Enabled, click to toggle") : Tr(L"已关闭，点击切换", L"Off, click to toggle"), 78, 96, 180, 22, RGB(105, 105, 105), 11);
        FillRoundRect(dc, RECT{282, 81, 326, 103}, g_dohEnabled ? RGB(0, 120, 212) : RGB(130, 130, 130), 14);
        FillRoundRect(dc, RECT{g_dohEnabled ? 304 : 284, 83, g_dohEnabled ? 324 : 304, 101}, RGB(255, 255, 255), 12);
        DrawActionButton(dc, RECT{40, 120, 180, 151}, std::wstring(Tr(L"首选：", L"Primary: ")) + primary);
        DrawActionButton(dc, RECT{188, 120, 328, 151}, std::wstring(Tr(L"备用：", L"Backup: ")) + backup);

        FillRoundRect(dc, RECT{24, 178, 344, 612}, RGB(255, 255, 255), 10);
        DrawText(dc, Tr(L"DoH 服务器（DNS over HTTPS）", L"DoH servers (DNS over HTTPS)"), 40, 192, 250, 24, RGB(30, 30, 30), 13, true);
        DrawActionButton(dc, RECT{40, 218, 112, 248}, Tr(L"快速导入", L"Import"));
        DrawActionButton(dc, RECT{118, 218, 194, 248}, Tr(L"手动添加", L"Add"));
        DrawActionButton(dc, RECT{200, 218, 244, 248}, Tr(L"搜索", L"Find"));
        DrawActionButton(dc, RECT{250, 218, 288, 248}, Tr(L"应用", L"Apply"), !g_dohServers.empty(), true);
        if (!g_dohSearch.empty()) DrawActionButton(dc, RECT{294, 218, 332, 248}, Tr(L"清除", L"Clear"));

        const auto filteredDoh = FilteredDohIndices();
        constexpr size_t visibleDohRows = 12;
        const auto maximumDohOffset = filteredDoh.size() > visibleDohRows ? filteredDoh.size() - visibleDohRows : 0;
        g_dohOffset = std::min(g_dohOffset, maximumDohOffset);
        if (filteredDoh.empty()) DrawText(dc, g_dohServers.empty() ? Tr(L"未发现系统 DoH 服务器", L"No system DoH servers") : Tr(L"未找到匹配的 DoH 服务器", L"No matching DoH servers"), 40, 258, 270, 22, RGB(100, 100, 100), 11);
        const auto dohEnd = std::min(filteredDoh.size(), g_dohOffset + visibleDohRows);
        for (size_t position = g_dohOffset; position < dohEnd; ++position) {
            const auto index = filteredDoh[position];
            const int top = 254 + static_cast<int>(position - g_dohOffset) * 29;
            DrawText(dc, g_dohServers[index].address, 40, top, 92, 18, RGB(55, 55, 55), 11, true);
            DrawText(dc, g_dohServers[index].templateUrl, 132, top, 164, 18, RGB(100, 100, 100), 10);
            DrawActionButton(dc, RECT{300, top - 3, 330, top + 23}, L"×");
        }
        if (g_dohOffset > 0) DrawGlyph(dc, L"\xE70E", 330, 254, 12, 18, RGB(110, 110, 110), 9);
        if (dohEnd < filteredDoh.size()) DrawGlyph(dc, L"\xE70D", 330, 584, 12, 18, RGB(110, 110, 110), 9);
        if (g_dohPicker != 0) {
            FillRoundRect(dc, RECT{24, 154, 344, 452}, RGB(255, 255, 255), 10);
            DrawText(dc, g_dohPicker == 1 ? Tr(L"选择首选 DoH", L"Select primary DoH") : Tr(L"选择备用 DoH", L"Select backup DoH"),
                40, 168, 220, 24, RGB(30, 30, 30), 13, true);
            DrawText(dc, L"×", 306, 166, 22, 24, RGB(90, 90, 90), 16, true);
            FillRoundRect(dc, RECT{40, 198, 328, 230}, RGB(244, 247, 249), 10);
            DrawGlyph(dc, L"\xE721", 50, 204, 18, 18, RGB(100, 100, 100), 12);
            DrawText(dc, g_dohPickerSearch.empty() ? Tr(L"输入以筛选 IP 或模板", L"Type to filter IP or template") : g_dohPickerSearch,
                74, 202, 236, 24, g_dohPickerSearch.empty() ? RGB(115, 115, 115) : RGB(45, 45, 45), 11);
            const auto pickerMatches = FilteredDohIndices(g_dohPickerSearch);
            if (pickerMatches.empty()) DrawText(dc, Tr(L"未找到匹配的 DoH 服务器", L"No matching DoH server"), 40, 246, 270, 24, RGB(110, 110, 110), 11);
            for (size_t row = 0; row < std::min<size_t>(pickerMatches.size(), 6); ++row) {
                const auto index = pickerMatches[row];
                const int top = 242 + static_cast<int>(row) * 32;
                FillRoundRect(dc, RECT{40, top, 328, top + 28}, RGB(248, 250, 252), 8);
                DrawText(dc, g_dohServers[index].address, 52, top + 4, 92, 19, RGB(45, 45, 45), 11, true);
                DrawText(dc, g_dohServers[index].templateUrl, 146, top + 4, 154, 19, RGB(90, 90, 90), 10);
            }
        }
        EndPaint(hwnd, &ps);
        return;
    }

    const auto ethernetCaption = std::wstring(Tr(L"以太网 · ", L"Ethernet · ")) + (g_ethernetOn ? Tr(L"已连接", L"Connected") : Tr(L"未连接", L"Disconnected"));
    const auto wifiCaption = g_status.rfind(L"Wi-Fi · ", 0) == 0 ? g_status : std::wstring(L"Wi-Fi");
    std::wstring bluetoothCaption = Tr(L"已开启", L"On");
    if (!g_bluetoothOn) bluetoothCaption = Tr(L"已关闭", L"Off");
    const auto usbCaption = std::wstring(Tr(L"USB 共享 · ", L"USB tether · ")) + (g_usbOn ? Tr(L"已连接", L"Connected") : Tr(L"未连接", L"Disconnected"));
    const auto displayCaption = std::wstring(Tr(L"屏幕 · ", L"Display · ")) +
        (g_activeDisplayCount > 1 ? (g_displayExtended ? Tr(L"扩展", L"Extend") : Tr(L"复制", L"Duplicate")) : Tr(L"单屏", L"Single"));
    DrawTile(dc, RECT{24, 24, 123, 71}, L"\xE839", ethernetCaption.c_str(), g_ethernetOn, g_hoverTile == 1 && !g_hoverTileExpand, g_hoverTile == 1 && g_hoverTileExpand,
        g_detailsVisible && g_detailsKind == 3);
    DrawTile(dc, RECT{135, 24, 234, 71}, L"\xE701", wifiCaption.c_str(), g_wifiOn, g_hoverTile == 2 && !g_hoverTileExpand, g_hoverTile == 2 && g_hoverTileExpand,
        g_detailsVisible && g_detailsKind == 1);
    DrawTile(dc, RECT{245, 24, 344, 71}, L"\xE702", bluetoothCaption.c_str(), g_bluetoothOn, g_hoverTile == 3 && !g_hoverTileExpand, g_hoverTile == 3 && g_hoverTileExpand,
        g_detailsVisible && g_detailsKind == 2);
    DrawTile(dc, RECT{24, 114, 123, 161}, L"\xE88E", usbCaption.c_str(), g_usbOn, g_hoverTile == 4 && !g_hoverTileExpand, g_hoverTile == 4 && g_hoverTileExpand,
        g_detailsVisible && g_detailsKind == 4);
    DrawTile(dc, RECT{135, 114, 234, 161}, L"\xE7F4", displayCaption.c_str(), g_activeDisplayCount > 0, g_hoverTile == 5 && !g_hoverTileExpand, g_hoverTile == 5 && g_hoverTileExpand,
        g_detailsVisible && g_detailsKind == 5);

    const int networkHeight = g_currentNetworkExpanded ? 132 : 76;
    const int networkTop = CurrentNetworkTop();
    // Current network and the quick-settings detail surface share one content
    // column, so their left/right edges always align.
    RECT network{kContentLeft, networkTop, kContentRight, networkTop + networkHeight};
    FillRoundRect(dc, network, RGB(255, 255, 255), 10);
    DrawText(dc, Tr(L"当前网络", L"Current network"), 40, networkTop + 16, 220, 22, RGB(30, 30, 30), 14, true);
    const RECT currentNetworkChevron{294, networkTop + 11, 326, networkTop + 39};
    FillRoundRect(dc, currentNetworkChevron,
        g_hoverCurrentNetworkChevron ? RGB(230, 238, 248) : RGB(247, 249, 251), 14);
    DrawGlyph(dc, g_currentNetworkExpanded ? L"\xE70D" : L"\xE70E",
        300, networkTop + 15, 20, 20, RGB(55, 55, 55), 11);
    if (g_currentNetworkExpanded) {
        DrawText(dc, g_currentNetworkInfo, 40, networkTop + 46, 270, 20, RGB(55, 55, 55), 11);
        const auto dohSummary = g_dohEnabled
            ? (g_dohServers.empty() ? std::wstring(Tr(L"DoH：已启用", L"DoH: enabled")) : std::wstring(Tr(L"DoH：已启用 · ", L"DoH: enabled · ")) + g_dohServers[std::min(g_dohPrimary, g_dohServers.size() - 1)].address)
            : std::wstring(Tr(L"DoH：已关闭", L"DoH: disabled"));
        DrawText(dc, g_currentIpInfo, 40, networkTop + 66, 270, 18, RGB(100, 100, 100), 10);
        DrawText(dc, g_currentDnsInfo, 40, networkTop + 84, 270, 18, RGB(100, 100, 100), 10);
                DrawText(dc, dohSummary, 40, networkTop + 102, 250, 18,
                    g_dohEnabled ? RGB(0, 103, 192) : RGB(100, 100, 100), 11);
    } else {
        const auto compactInfo = g_currentNetworkInfo + (g_dohEnabled
            ? (g_dohServers.empty() ? std::wstring(Tr(L" · DoH 已启用", L" · DoH enabled")) : std::wstring(g_dohApplied ? Tr(L" · DoH 已应用 ", L" · DoH applied ") : L" · DoH ") + g_dohServers[std::min(g_dohPrimary, g_dohServers.size() - 1)].address)
            : L"");
        DrawText(dc, compactInfo, 40, networkTop + 48, 285, 20, RGB(75, 75, 75), 11);
    }
    if (g_detailsVisible) FillRoundRect(dc, RECT{kContentLeft, 184, kContentRight, networkTop - 14}, RGB(255, 255, 255), 10);
    if (g_detailsVisible && g_detailsKind == 1) {
        DrawText(dc, Tr(L"可用网络", L"Available networks"), 40, 200, 108, 20, RGB(70, 70, 70), 11, true);
        DrawGlyph(dc, g_wifiAvailableExpanded ? L"\xE70D" : L"\xE70E", 306, 201, 20, 18, RGB(75, 75, 75), 11);
        if (g_wifiAdapterIndex < g_wifiAdapters.size()) {
            DrawActionButton(dc, RECT{148, 196, 258, 224}, std::wstring(Tr(L"切换网卡", L"Adapter")) + L"  \xE70D");
        }
        const bool connectedWifi = g_status.rfind(L"Wi-Fi · ", 0) == 0 &&
            g_status.find(Tr(L"未连接", L"disconnected")) == std::wstring::npos &&
            g_status.find(Tr(L"已关闭", L"Off")) == std::wstring::npos;
        if (connectedWifi) DrawActionButton(dc, RECT{266, 196, 332, 224}, Tr(L"断开", L"Disconnect"));
        if (g_wifiAvailableExpanded) {
            if (g_wifiNetworks.empty()) DrawText(dc, Tr(L"未发现可用网络", L"No available networks"), 40, 228, 260, 20, RGB(100, 100, 100), 11);
            const int availableRows = WifiAvailableVisibleRows();
            const auto availableEnd = std::min(g_wifiNetworks.size(), g_wifiAvailableOffset + static_cast<size_t>(availableRows));
            for (size_t index = g_wifiAvailableOffset; index < availableEnd; ++index) {
                const auto& wifi = g_wifiNetworks[index];
                const int top = 228 + static_cast<int>(index - g_wifiAvailableOffset) * 24;
                const auto state = std::wstring(wifi.secured ? Tr(L"受保护", L"Secured") : Tr(L"开放", L"Open")) + L" " + std::to_wstring(wifi.signal) + L"%";
                DrawText(dc, wifi.name, 40, top, 146, 20, RGB(70, 70, 70), 11);
                DrawText(dc, state, 188, top, 72, 20, RGB(110, 110, 110), 10);
                DrawActionButton(dc, RECT{272, top - 2, 330, top + 22}, Tr(L"连接", L"Connect"), true, true);
            }
            const int availableBottom = 228 + availableRows * 24;
            if (g_wifiAvailableOffset > 0) DrawWhiteFade(dc, 40, 228, 316, 237, true);
            if (availableEnd < g_wifiNetworks.size()) DrawWhiteFade(dc, 40, availableBottom - 9, 316, availableBottom, false);
            if (g_wifiAvailableOffset > 0) DrawText(dc, L"⌃", 318, 228, 16, 18, RGB(145, 145, 145), 10, true);
            if (availableEnd < g_wifiNetworks.size()) DrawText(dc, L"⌄", 318, availableBottom - 20, 16, 18, RGB(145, 145, 145), 10, true);
        }
        const int savedHeader = WifiSavedHeaderTop();
        DrawText(dc, Tr(L"已保存 Wi-Fi", L"Saved Wi-Fi"), 40, savedHeader, 150, 20, RGB(70, 70, 70), 11, true);
        DrawGlyph(dc, g_wifiSavedExpanded ? L"\xE70D" : L"\xE70E", 306, savedHeader + 1, 20, 18, RGB(75, 75, 75), 11);
        if (g_wifiSavedExpanded) {
            FillRoundRect(dc, RECT{200, savedHeader - 4, 298, savedHeader + 22}, g_wifiSavedSearchActive ? RGB(235, 244, 253) : RGB(246, 248, 250), 8);
            DrawGlyph(dc, L"\xE721", 208, savedHeader + 2, 16, 16, RGB(105, 105, 105), 11);
            DrawText(dc, g_wifiSearch.empty() ? Tr(L"搜索", L"Search") : g_wifiSearch, 228, savedHeader - 2, 62, 22,
                g_wifiSearch.empty() ? RGB(115, 115, 115) : RGB(45, 45, 45), 10);
            const int savedTop = WifiSavedListTop();
            if (g_savedWifi.empty()) DrawText(dc, Tr(L"没有已保存的 Wi-Fi", L"No saved Wi-Fi"), 40, savedTop, 260, 20, RGB(100, 100, 100), 11);
            const auto savedEnd = std::min(g_savedWifi.size(), g_wifiSavedOffset + static_cast<size_t>(WifiSavedVisibleRows()));
            for (size_t index = g_wifiSavedOffset; index < savedEnd; ++index)
                DrawText(dc, g_savedWifi[index].name + Tr(L"     密码    忘记", L"     Password    Forget"), 40, savedTop + static_cast<int>(index - g_wifiSavedOffset) * 24, 280, 20, RGB(70, 70, 70), 11);
            if (g_wifiSavedOffset > 0) DrawWhiteFade(dc, 40, savedTop, 316, savedTop + 9, true);
            if (savedEnd < g_savedWifi.size()) DrawWhiteFade(dc, 40, CurrentNetworkTop() - 34, 316, CurrentNetworkTop() - 25, false);
            if (g_wifiSavedOffset > 0) DrawText(dc, L"⌃", 318, savedTop, 16, 18, RGB(145, 145, 145), 10, true);
            if (savedEnd < g_savedWifi.size()) DrawText(dc, L"⌄", 318, CurrentNetworkTop() - 45, 16, 18, RGB(145, 145, 145), 10, true);
        }
    } else if (g_detailsVisible && g_detailsKind == 2) {
        DrawText(dc, Tr(L"蓝牙适配器", L"Bluetooth adapters"), 40, 200, 164, 20, RGB(70, 70, 70), 11, true);
        DrawActionButton(dc, RECT{208, 196, 330, 224}, Tr(L"切换适配器", L"Switch adapter"), !g_bluetoothAdapters.empty());
        if (g_bluetoothAdapters.empty()) DrawText(dc, Tr(L"未检测到蓝牙适配器", L"No Bluetooth adapter"), 40, 228, 260, 20, RGB(100, 100, 100), 11);
        for (size_t index = 0; index < std::min<size_t>(g_bluetoothAdapters.size(), 4); ++index) {
            const int top = 228 + static_cast<int>(index) * 24;
            DrawText(dc, g_bluetoothAdapters[index].name, 40, top, 190, 20, RGB(70, 70, 70), 11);
            DrawActionButton(dc, RECT{270, top - 2, 330, top + 22},
                g_bluetoothAdapters[index].enabled ? Tr(L"禁用", L"Disable") : Tr(L"启用", L"Enable"));
        }
        DrawText(dc, Tr(L"蓝牙设备", L"Bluetooth devices"), 40, 306, 180, 20, RGB(70, 70, 70), 11, true);
        DrawActionButton(dc, RECT{270, 302, 332, 328},
            g_bluetoothScanning ? Tr(L"扫描中…", L"Scanning…") : Tr(L"扫描", L"Scan"), !g_bluetoothScanning);
        if (g_bluetoothDevices.empty()) DrawText(dc, Tr(L"未发现蓝牙设备", L"No Bluetooth devices"), 40, 330, 260, 20, RGB(100, 100, 100), 11);
        const auto deviceEnd = std::min(g_bluetoothDevices.size(), g_bluetoothDeviceOffset + 4);
        for (size_t index = g_bluetoothDeviceOffset; index < deviceEnd; ++index) {
            const auto& device = g_bluetoothDevices[index];
            DrawText(dc, device.name + (device.connected ? Tr(L"  已连接  移除", L"  Connected  Remove") : device.paired ? Tr(L"  已配对  移除", L"  Paired  Remove") : Tr(L"  点击配对", L"  Click to pair")),
                40, 330 + static_cast<int>(index - g_bluetoothDeviceOffset) * 24, 280, 20, RGB(70, 70, 70), 11);
        }
        if (g_bluetoothDeviceOffset > 0) DrawWhiteFade(dc, 40, 330, 316, 339, true);
        if (deviceEnd < g_bluetoothDevices.size()) DrawWhiteFade(dc, 40, 414, 316, 423, false);
        if (g_bluetoothDeviceOffset > 0) DrawText(dc, L"⌃", 318, 330, 16, 18, RGB(145, 145, 145), 10, true);
        if (deviceEnd < g_bluetoothDevices.size()) DrawText(dc, L"⌄", 318, 402, 16, 18, RGB(145, 145, 145), 10, true);
        DrawActionButton(dc, RECT{40, 436, 156, 468}, Tr(L"发送文件", L"Send files"), true, true);
        DrawActionButton(dc, RECT{170, 436, 286, 468}, Tr(L"接收文件", L"Receive files"));
    } else if (g_detailsVisible && g_detailsKind == 3) {
        DrawText(dc, Tr(L"上网方式优先级（metric 越小越优先）", L"Connection priority (lower metric wins)"), 40, 200, 280, 20, RGB(70, 70, 70), 11, true);
        if (g_networkAdapters.empty()) DrawText(dc, Tr(L"未检测到物理网络适配器", L"No physical network adapters"), 40, 228, 280, 20, RGB(100, 100, 100), 11);
        for (size_t index = 0; index < std::min<size_t>(g_networkAdapters.size(), 10); ++index) {
            const auto& adapter = g_networkAdapters[index];
            const int top = 228 + static_cast<int>(index) * 24;
            DrawText(dc, adapter.name, 40, top, 120, 20, RGB(70, 70, 70), 11);
            DrawText(dc, L"metric " + std::to_wstring(adapter.metric) + (adapter.up ? Tr(L" · 已连接", L" · Connected") : Tr(L" · 未连接", L" · Disconnected")),
                160, top, 108, 20, RGB(100, 100, 100), 10);
            DrawActionButton(dc, RECT{274, top - 2, 330, top + 22}, Tr(L"编辑", L"Edit"));
        }
    } else if (g_detailsVisible && g_detailsKind == 4) {
        DrawText(dc, Tr(L"USB/RNDIS 网络共享", L"USB/RNDIS tethering"), 40, 200, 260, 20, RGB(70, 70, 70), 11, true);
        if (g_usbInterface.empty()) {
            DrawText(dc, Tr(L"未检测到 USB/RNDIS 网卡。请连接手机并在手机端开启 USB 网络共享。", L"No USB/RNDIS adapter. Connect the phone and enable USB tethering on the phone."),
                40, 228, 280, 44, RGB(100, 100, 100), 11);
        } else {
            DrawText(dc, g_usbInterface, 40, 228, 210, 20, RGB(70, 70, 70), 11);
            DrawActionButton(dc, RECT{270, 226, 330, 250}, g_usbOn ? Tr(L"断开", L"Disconnect") : Tr(L"启用", L"Enable"));
            DrawText(dc, g_usbOn ? Tr(L"USB 网络共享已连接", L"USB tethering connected") : Tr(L"USB 网卡已检测到，等待手机端共享", L"USB adapter detected; waiting for phone tethering"),
                40, 258, 280, 20, RGB(100, 100, 100), 11);
        }
    } else if (g_detailsVisible && g_detailsKind == 5) {
        DrawText(dc, Tr(L"屏幕管理", L"Display management"), 40, 200, 260, 20, RGB(70, 70, 70), 11, true);
        DrawActionButton(dc, RECT{40, 228, 164, 260}, Tr(L"复制屏幕", L"Duplicate displays"), true, !g_displayExtended);
        DrawActionButton(dc, RECT{178, 228, 302, 260}, Tr(L"扩展屏幕", L"Extend displays"), true, g_displayExtended);
        DrawActionButton(dc, RECT{40, 270, 302, 302}, Tr(L"打开屏幕布局设置", L"Open display layout settings"));
        DrawActionButton(dc, RECT{40, 312, 302, 344}, g_hdrEnabled ? Tr(L"HDR · 已启用（点击关闭）", L"HDR · On (click to turn off)") : Tr(L"HDR · 已关闭（点击启用）", L"HDR · Off (click to turn on)"), g_hdrSupported);
        DrawActionButton(dc, RECT{40, 354, 302, 386}, g_activeDisplayCount == 0 ? Tr(L"安装/启用虚拟屏驱动", L"Install/enable virtual display driver") : Tr(L"虚拟屏 · 仅在无物理屏幕时可用", L"Virtual display · Available only without a physical display"), g_activeDisplayCount == 0);
        DrawActionButton(dc, RECT{40, 392, 302, 424}, g_vddAuto
            ? Tr(L"无显示器时自动启用 · 已开启", L"Auto-enable without a display · On")
            : Tr(L"无显示器时自动启用 · 已关闭", L"Auto-enable without a display · Off"), true);
        DrawText(dc, Tr(L"将已签名的 IddCx 驱动 INF 放入程序目录 drivers 文件夹", L"Put a signed IddCx driver INF into the \"drivers\" folder next to the app"), 40, 428, 270, 24, RGB(110, 110, 110), 10);
    } else if (g_detailsVisible) {
        DrawText(dc, Tr(L"请选择上方快捷卡片查看详情。", L"Select a quick card above to view details."), 40, 200, 280, 20, RGB(100, 100, 100), 11);
    }

    if (g_detailsVisible) {
        // Soft lower fade keeps long detail content visually inside the card boundary.
        for (int i = 0; i < 5; ++i) {
            const int y = networkTop - 10 - i * 4;
            HBRUSH fade = CreateSolidBrush(RGB(255, 255, 255));
            RECT strip{kContentLeft + 1, y, kContentRight - 1, y + 4};
            FillRect(dc, &strip, fade);
            DeleteObject(fade);
        }
    }

    const int footerY = g_detailsVisible ? 610 : 528;
    DrawGlyph(dc, L"\xE713", 308, footerY - 2, 32, 28, RGB(50, 50, 50), 17);
    if (!g_toastText.empty()) {
        const int toastTop = g_detailsVisible ? 442 : 458;
        RECT toast{ 42, toastTop, 326, toastTop + 38 };
        FillRoundRect(dc, toast, RGB(45, 45, 45), 9);
        SetBkMode(dc, TRANSPARENT);
        SetTextColor(dc, RGB(255, 255, 255));
        if (!g_toastAction.empty()) {
            SIZE messageSize{}, actionSize{};
            GetTextExtentPoint32W(dc, g_toastText.c_str(), static_cast<int>(g_toastText.size()), &messageSize);
            GetTextExtentPoint32W(dc, g_toastAction.c_str(), static_cast<int>(g_toastAction.size()), &actionSize);
            const int groupWidth = messageSize.cx + 14 + actionSize.cx;
            const int groupLeft = std::max(54, 184 - groupWidth / 2);
            RECT messageRect{ groupLeft, toastTop, groupLeft + messageSize.cx + 4, toastTop + 38 };
            DrawTextW(dc, g_toastText.c_str(), -1, &messageRect, DT_CENTER | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
            g_toastActionLeft = messageRect.right + 10;
            g_toastActionRight = std::min(314, g_toastActionLeft + static_cast<int>(actionSize.cx) + 12);
            RECT actionRect{ g_toastActionLeft, toastTop, g_toastActionRight, toastTop + 38 };
            SetTextColor(dc, RGB(120, 190, 255));
            DrawTextW(dc, g_toastAction.c_str(), -1, &actionRect, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
        } else {
            g_toastActionLeft = g_toastActionRight = 0;
            RECT messageRect{ 54, toastTop, 314, toastTop + 38 };
            DrawTextW(dc, g_toastText.c_str(), -1, &messageRect, DT_CENTER | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
        }
    }
    EndPaint(hwnd, &ps);
}

void ToggleWindow() {
    // NOTIFYICON_VERSION_4 delivers both WM_LBUTTONUP and NIN_SELECT for one
    // click; without this guard the second call re-hides the just-shown popup.
    static DWORD lastToggleTick = 0;
    const DWORD now = GetTickCount();
    if (lastToggleTick != 0 && now - lastToggleTick < 400) return;
    lastToggleTick = now;
    // Pure toggle: a visible popup minimizes, a hidden/minimized one returns.
    // Focus-based checks misfire because the tray click moves focus first.
    if (IsWindowVisible(g_window)) {
        if (!g_fadeHiding) {
            g_fadeHiding = true;
            KillTimer(g_window, kResizeTimer);
            SetTimer(g_window, kFadeTimer, 15, nullptr);
        }
    } else {
        RefreshWifiStatus();
        const auto height = DesiredWindowHeight();
        const auto bounds = PopupBoundsNearTaskbar(height, true);
        SetWindowPos(g_window, HWND_TOPMOST, bounds.left, bounds.top,
            kWindowWidth, height, SWP_SHOWWINDOW);
        g_windowAlpha = 0;
        g_fadeHiding = false;
        SetLayeredWindowAttributes(g_window, 0, g_windowAlpha, LWA_ALPHA);
        SetTimer(g_window, kFadeTimer, 15, nullptr);
        SetForegroundWindow(g_window);
        SetTimer(g_window, kRefreshTimer, 3000, nullptr);
    }
}

void ResizeNearTray() {
    if (!IsWindowVisible(g_window)) return;
    g_resizeTargetHeight = DesiredWindowHeight();
    SetWindowLongPtrW(g_window, GWL_EXSTYLE, GetWindowLongPtrW(g_window, GWL_EXSTYLE) | WS_EX_LAYERED);
    g_windowAlpha = 220;
    g_fadeHiding = false;
    SetLayeredWindowAttributes(g_window, 0, g_windowAlpha, LWA_ALPHA);
    SetTimer(g_window, kFadeTimer, 15, nullptr);
    SetTimer(g_window, kResizeTimer, 15, nullptr);
}

void ShowTrayMenu(HWND hwnd) {
    HMENU menu = CreatePopupMenu();
    AppendMenuW(menu, MF_STRING, kTrayOpen, Tr(L"打开快速设置", L"Open quick settings"));
    AppendMenuW(menu, MF_STRING, kTraySettings, Tr(L"设置", L"Settings"));
    AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
    AppendMenuW(menu, MF_STRING, kTrayExit, Tr(L"退出", L"Exit"));
    POINT point{};
    GetCursorPos(&point);
    SetForegroundWindow(hwnd);
    TrackPopupMenu(menu, TPM_RIGHTBUTTON | TPM_BOTTOMALIGN | TPM_LEFTALIGN, point.x, point.y, 0, hwnd, nullptr);
    DestroyMenu(menu);
}

LRESULT CALLBACK WindowProc(HWND hwnd, UINT message, WPARAM wparam, LPARAM lparam) {
    switch (message) {
    case WM_PAINT:
        PaintWindow(hwnd);
        return 0;
    case WM_MOUSEWHEEL:
        if (g_page == Page::Doh && g_dohPicker == 0) {
            POINT point{ GET_X_LPARAM(lparam), GET_Y_LPARAM(lparam) };
            ScreenToClient(hwnd, &point);
            if (point.y >= 250 && point.y < 612) {
                const auto filtered = FilteredDohIndices();
                constexpr size_t visibleRows = 12;
                const auto maximum = filtered.size() > visibleRows ? filtered.size() - visibleRows : 0;
                const int direction = GET_WHEEL_DELTA_WPARAM(wparam) < 0 ? 1 : -1;
                if (direction > 0 && g_dohOffset < maximum) ++g_dohOffset;
                if (direction < 0 && g_dohOffset > 0) --g_dohOffset;
                InvalidateRect(hwnd, nullptr, FALSE);
                return 0;
            }
        }

        if (g_page == Page::Home && g_detailsVisible && g_detailsKind == 1) {
            POINT point{ GET_X_LPARAM(lparam), GET_Y_LPARAM(lparam) };
            ScreenToClient(hwnd, &point);
            const int direction = GET_WHEEL_DELTA_WPARAM(wparam) < 0 ? 1 : -1;
            if (g_wifiAvailableExpanded && point.y >= 228 && point.y < 228 + WifiAvailableVisibleRows() * 24) {
                const auto rows = static_cast<size_t>(WifiAvailableVisibleRows());
                const auto maximum = g_wifiNetworks.size() > rows ? g_wifiNetworks.size() - rows : 0;
                if (direction > 0 && g_wifiAvailableOffset < maximum) ++g_wifiAvailableOffset;
                if (direction < 0 && g_wifiAvailableOffset > 0) --g_wifiAvailableOffset;
            } else if (g_wifiSavedExpanded && point.y >= WifiSavedListTop() && point.y < CurrentNetworkTop() - 18) {
                const auto rows = static_cast<size_t>(WifiSavedVisibleRows());
                const auto maximum = g_savedWifi.size() > rows ? g_savedWifi.size() - rows : 0;
                if (direction > 0 && g_wifiSavedOffset < maximum) ++g_wifiSavedOffset;
                if (direction < 0 && g_wifiSavedOffset > 0) --g_wifiSavedOffset;
            }
            InvalidateRect(hwnd, nullptr, FALSE);
            return 0;
        }
        if (g_page == Page::Home && g_detailsVisible && g_detailsKind == 2) {
            POINT point{ GET_X_LPARAM(lparam), GET_Y_LPARAM(lparam) };
            ScreenToClient(hwnd, &point);
            if (point.y >= 330 && point.y < 426) {
                const int direction = GET_WHEEL_DELTA_WPARAM(wparam) < 0 ? 1 : -1;
                const auto maximum = g_bluetoothDevices.size() > 4 ? g_bluetoothDevices.size() - 4 : 0;
                if (direction > 0 && g_bluetoothDeviceOffset < maximum) ++g_bluetoothDeviceOffset;
                if (direction < 0 && g_bluetoothDeviceOffset > 0) --g_bluetoothDeviceOffset;
                InvalidateRect(hwnd, nullptr, FALSE);
                return 0;
            }
        }
        break;
    case kVddWorkMessage:
        g_vddWorkInProgress = false;
        RefreshDisplayState();
        InvalidateRect(hwnd, nullptr, FALSE);
        switch (wparam) {
        case 1: ShowToast(Tr(L"虚拟屏已启用", L"Virtual display enabled")); break;
        case 2: ShowToast(Tr(L"虚拟屏驱动已就绪", L"The virtual display driver is already ready")); break;
        case 3: ShowToast(Tr(L"未找到虚拟屏驱动，请将已签名 INF 放入 drivers 文件夹", L"No virtual display driver found; put a signed INF into the drivers folder")); break;
        default: ShowToast(Tr(L"启用失败，请以管理员身份运行后重试", L"Enable failed; run as administrator and retry")); break;
        }
        return 0;
    case WM_TIMER:
        if (wparam == kRefreshTimer) {
            RefreshWifiStatus();
            RefreshBluetoothAdapters();
            RefreshUsbTethering();
            RefreshDohEnabled();
            RefreshNetworkAdapters();
            RefreshDisplayState();
            AutoVirtualDisplayCheck();
            InvalidateRect(hwnd, nullptr, FALSE);
        } else if (wparam == kFadeTimer) {
            if (g_fadeHiding) {
                if (g_windowAlpha <= 32) {
                    g_windowAlpha = 0;
                    SetLayeredWindowAttributes(hwnd, 0, g_windowAlpha, LWA_ALPHA);
                    KillTimer(hwnd, kFadeTimer);
                    g_fadeHiding = false;
                    ShowWindow(hwnd, SW_HIDE);
                } else {
                    g_windowAlpha = static_cast<BYTE>(g_windowAlpha - 32);
                    SetLayeredWindowAttributes(hwnd, 0, g_windowAlpha, LWA_ALPHA);
                }
            } else {
                g_windowAlpha = static_cast<BYTE>(std::min(255, static_cast<int>(g_windowAlpha) + 32));
                SetLayeredWindowAttributes(hwnd, 0, g_windowAlpha, LWA_ALPHA);
                if (g_windowAlpha == 255) {
                    KillTimer(hwnd, kFadeTimer);
                }
            }
        } else if (wparam == kResizeTimer) {
            RECT current{};
            GetWindowRect(hwnd, &current);
            const int currentHeight = current.bottom - current.top;
            const int difference = g_resizeTargetHeight - currentHeight;
            if (difference == 0) {
                KillTimer(hwnd, kResizeTimer);
            } else {
                const int step = difference > 0 ? std::max(1, difference / 3) : std::min(-1, difference / 3);
                const int nextHeight = abs(difference) <= 2 ? g_resizeTargetHeight : currentHeight + step;
                const auto bounds = PopupBoundsNearTaskbar(nextHeight, false);
                SetWindowPos(hwnd, HWND_TOPMOST, bounds.left, bounds.top,
                    kWindowWidth, nextHeight, SWP_NOACTIVATE);
            }
        } else if (wparam == kToastTimer) {
            KillTimer(hwnd, kToastTimer);
            g_toastText.clear();
            g_toastAction.clear();
            InvalidateRect(hwnd, nullptr, FALSE);
        } else if (wparam == kActivateTimer && g_activateEvent && WaitForSingleObject(g_activateEvent, 0) == WAIT_OBJECT_0) {
            ToggleWindow();
        }
        return 0;
    case WM_MOUSEMOVE: {
        const int x = GET_X_LPARAM(lparam), y = GET_Y_LPARAM(lparam);
        const int hover = y >= 24 && y <= 71 ? (x <= 123 ? 1 : x <= 234 ? 2 : x <= 344 ? 3 : 0) :
            (x >= 24 && x <= 123 && y >= 114 && y <= 161 ? 4 : x >= 135 && x <= 234 && y >= 114 && y <= 161 ? 5 : 0);
        const bool hoverExpand = hover == 1 ? x >= 74 : hover == 2 ? x >= 184 : hover == 3 ? x >= 294 : hover == 4 ? x >= 74 : hover == 5 ? x >= 184 : false;
        const int networkTop = CurrentNetworkTop();
        const bool hoverChevron = x >= 294 && x <= 326 && y >= networkTop + 11 && y <= networkTop + 39;
        if (hover != g_hoverTile || hoverExpand != g_hoverTileExpand || hoverChevron != g_hoverCurrentNetworkChevron) {
            g_hoverTile = hover;
            g_hoverTileExpand = hoverExpand;
            g_hoverCurrentNetworkChevron = hoverChevron;
            InvalidateRect(hwnd, nullptr, FALSE);
        }
        TRACKMOUSEEVENT track{ sizeof(track), TME_LEAVE, hwnd, 0 };
        TrackMouseEvent(&track);
        return 0;
    }
    case WM_MOUSELEAVE:
        if (g_hoverTile != 0 || g_hoverTileExpand || g_hoverCurrentNetworkChevron) {
            g_hoverTile = 0;
            g_hoverTileExpand = false;
            g_hoverCurrentNetworkChevron = false;
            InvalidateRect(hwnd, nullptr, FALSE);
        }
        return 0;
    case WM_LBUTTONUP: {
        const int x = GET_X_LPARAM(lparam);
        const int y = GET_Y_LPARAM(lparam);
        const int toastTop = g_detailsVisible ? 442 : 458;
        if (!g_toastAction.empty() && x >= g_toastActionLeft && x <= g_toastActionRight && y >= toastTop && y <= toastTop + 38) {
            UndoForgetWifi();
            InvalidateRect(hwnd, nullptr, FALSE);
            return 0;
        }
        if (g_page == Page::Settings) {
            if (x >= 292 && y >= 594) g_page = Page::Home;
            else if (y >= 62 && y <= 134) SetStartWithWindows(!g_startWithWindows);
            else if (y >= 146 && y <= 218) SetLanguage(!g_isZh);
            else if (y >= 230 && y <= 302) {
                g_page = Page::Home;
                g_detailsVisible = true;
                g_detailsKind = 3;
                RefreshNetworkAdapters();
            }
            ResizeNearTray();
            InvalidateRect(hwnd, nullptr, TRUE);
            return 0;
        }
        if (g_page == Page::Doh) {
            if (y < 55) g_page = Page::Home;
            else if (g_dohPicker != 0) {
                if (x >= 300 && y >= 160 && y <= 196) { g_dohPicker = 0; g_dohPickerSearch.clear(); }
                else if (x >= 40 && x <= 328 && y >= 242 && y < 434) {
                    const auto matches = FilteredDohIndices(g_dohPickerSearch);
                    const auto row = static_cast<size_t>((y - 242) / 32);
                    if (row < matches.size()) {
                        if (g_dohPicker == 1) g_dohPrimary = matches[row]; else g_dohBackup = matches[row];
                        g_status = g_dohPicker == 1 ? Tr(L"已选择首选 DoH", L"Primary DoH selected") : Tr(L"已选择备用 DoH", L"Backup DoH selected");
                        g_dohPicker = 0;
                        g_dohPickerSearch.clear();
                    }
                }
            }
            else if (y >= 62 && y < 116) {
                const bool target = !g_dohEnabled;
                if (!SetDohEnabled(target)) {
                    g_status = Tr(L"DoH · 开关操作失败", L"DoH · Toggle failed");
                    ShowToast(Tr(L"DoH 设置失败，请检查管理员权限", L"DoH update failed; check administrator permission"));
                } else {
                    g_status = target ? Tr(L"DoH · 已启用", L"DoH · Enabled") : Tr(L"DoH · 已关闭", L"DoH · Disabled");
                    ShowToast(target ? Tr(L"DoH 已启用", L"DoH enabled") : Tr(L"DoH 已关闭", L"DoH disabled"));
                }
            }
            else if (y >= 116 && y <= 152 && x < 185 && !g_dohServers.empty()) { g_dohPicker = 1; g_dohPickerSearch.clear(); }
            else if (y >= 116 && y <= 152 && x >= 185 && !g_dohServers.empty()) { g_dohPicker = 2; g_dohPickerSearch.clear(); }
            else if (y >= 218 && y <= 248 && x >= 294 && x <= 332 && !g_dohSearch.empty()) {
                g_dohSearch.clear();
                g_dohOffset = 0;
            }
            else if (y >= 218 && y <= 248 && x >= 250 && x <= 288 && !g_dohServers.empty()) {
                if (ConfirmApplySelectedDoh(hwnd))
                    g_status = ApplySelectedDoh() ? Tr(L"DoH · 已应用到当前网卡", L"DoH · Applied to current adapter") : Tr(L"DoH · 应用失败", L"DoH · Apply failed");
            }
            else if (y >= 218 && y <= 248 && x >= 200 && x <= 244) {
                g_dohSearch = PromptText(hwnd, Tr(L"搜索系统 DoH", L"Search system DoH"), Tr(L"输入 IP 或模板关键字", L"Enter an IP or template keyword"));
                g_dohOffset = 0;
            }
            else if (y >= 218 && y <= 248 && x >= 118 && x <= 194) { AddDohServer(hwnd); g_dohOffset = 0; }
            else if (y >= 218 && y <= 248 && x >= 40 && x <= 112) { ImportDohServers(hwnd); g_dohOffset = 0; }
            else if (y >= 254 && y < 602 && x >= 296) {
                const auto row = static_cast<size_t>((y - 254) / 29);
                const auto filtered = FilteredDohIndices();
                const auto position = g_dohOffset + row;
                if (position < filtered.size()) DeleteDohServer(hwnd, g_dohServers[filtered[position]]);
            }
            InvalidateRect(hwnd, nullptr, TRUE);
            return 0;
        }
        if (y >= 24 && y <= 71) {
            if (x >= 24 && x <= 123) {
                if (x < 74) { const bool target = !g_ethernetOn; if (!SetEthernetEnabled(target)) g_status = Tr(L"以太网 · 操作失败", L"Ethernet · Operation failed"); else g_status = target ? Tr(L"以太网 · 正在启用", L"Ethernet · Enabling") : Tr(L"以太网 · 正在禁用", L"Ethernet · Disabling"); RefreshNetworkAdapters(); }
                else { g_detailsVisible = !g_detailsVisible || g_detailsKind != 3; g_detailsKind = 3; RefreshNetworkAdapters(); }
            } else if (x >= 135 && x <= 234) {
                if (x < 184) {
                    if (!SetWifiEnabled(!g_wifiOn)) g_status = Tr(L"Wi-Fi · 操作失败", L"Wi-Fi · Operation failed");
                    RefreshWifiStatus();
                } else {
                    if (g_detailsVisible && g_detailsKind == 1) {
                        g_detailsVisible = false;
                        g_detailsKind = 0;
                    } else {
                        g_detailsVisible = true;
                        g_detailsKind = 1;
                        RefreshWifiNetworks();
                        RefreshSavedWifi();
                    }
                }
            } else if (x >= 245 && x <= 344) {
                if (x >= 294) {
                    g_detailsVisible = true;
                    g_detailsKind = 2;
                    RefreshBluetoothAdapters();
                    RefreshBluetoothDevices();
                } else {
                    RefreshBluetoothAdapters();
                    ChooseBluetoothAdapter(hwnd, g_bluetoothRecoveryPending);
                }
            }
        } else if (y >= 114 && y <= 161 && x >= 24 && x <= 123) {
            if (x < 74) {
                if (!SetUsbTethering(!g_usbOn)) g_status = Tr(L"USB 共享 · 未检测到可控制的移动网络适配器", L"USB tether · No controllable mobile adapter");
            } else {
                g_detailsVisible = true;
                g_detailsKind = 4;
                RefreshUsbTethering();
            }
        } else if (y >= 114 && y <= 161 && x >= 135 && x <= 234) {
            if (x < 184) {
                if (!SwitchDisplayMode(!g_displayExtended)) g_status = Tr(L"屏幕模式切换失败", L"Display mode switch failed");
                else ShowToast(g_displayExtended ? Tr(L"已切换为扩展屏幕", L"Switched to extended displays") : Tr(L"已切换为复制屏幕", L"Switched to duplicate displays"));
            } else {
                g_detailsVisible = !g_detailsVisible || g_detailsKind != 5;
                g_detailsKind = 5;
                RefreshDisplayState();
            }
        } else if (g_detailsVisible && g_detailsKind == 1 && x >= 296 && y >= 196 && y <= 224) {
            g_wifiAvailableExpanded = !g_wifiAvailableExpanded;
        } else if (g_detailsVisible && g_detailsKind == 1 && x >= 148 && x <= 258 && y >= 196 && y <= 224 && !g_wifiAdapters.empty()) {
            ChooseWifiAdapter(hwnd);
        } else if (g_detailsVisible && g_detailsKind == 2 && x >= 208 && x <= 330 && y >= 196 && y <= 224 && !g_bluetoothAdapters.empty()) {
            ChooseBluetoothAdapter(hwnd, g_bluetoothRecoveryPending);
        } else if (g_detailsVisible && g_detailsKind == 1 && x >= 296 && y >= WifiSavedHeaderTop() - 4 && y <= WifiSavedHeaderTop() + 24) {
            g_wifiSavedExpanded = !g_wifiSavedExpanded;
        } else if (g_detailsVisible && g_detailsKind == 1 && g_wifiSavedExpanded && y >= WifiSavedListTop() && y < CurrentNetworkTop() - 20) {
            const auto index = g_wifiSavedOffset + static_cast<size_t>((y - WifiSavedListTop()) / 24);
            if (index < g_savedWifi.size()) {
                if (x < 220) {
                    const auto password = ReadSavedWifiPassword(g_savedWifi[index]);
                    MessageBoxW(hwnd, password.empty() ? Tr(L"无法读取密码：该网络可能不使用密码，或 Windows 未授予明文访问权限。", L"Unable to read the password. This network may not use one, or Windows denied plaintext access.") : password.c_str(),
                        Tr(L"已保存 Wi-Fi 密码", L"Saved Wi-Fi password"), MB_OK | MB_ICONINFORMATION);
                } else {
                    ForgetSavedWifi(g_savedWifi[index]);
                }
            }
        } else if (g_detailsVisible && g_detailsKind == 1 && g_wifiSavedExpanded &&
                   y >= WifiSavedHeaderTop() - 4 && y <= WifiSavedHeaderTop() + 24 && x >= 196 && x < 296) {
            g_wifiSavedSearchActive = true;
        } else if (g_detailsVisible && g_detailsKind == 1 && y >= 196 && y <= 224 && x >= 270) {
            if (!DisconnectWifi()) g_status = Tr(L"Wi-Fi · 断开失败", L"Wi-Fi · Disconnect failed");
            else { RefreshWifiStatus(); RefreshWifiNetworks(); }
        } else if (g_detailsVisible && g_detailsKind == 1 && g_wifiAvailableExpanded &&
                   y >= 228 && y < 228 + WifiAvailableVisibleRows() * 24) {
            const auto index = g_wifiAvailableOffset + static_cast<size_t>((y - 228) / 24);
            if (index < g_wifiNetworks.size()) {
                if (!ConnectNewWifi(hwnd, g_wifiNetworks[index]))
                    g_status = Tr(L"Wi-Fi · 连接失败或已取消", L"Wi-Fi · Connection failed or cancelled");
            }
        } else if (g_detailsVisible && g_detailsKind == 2 && y >= 436 && y <= 472) {
            ShellExecuteW(hwnd, L"open", L"fsquirt.exe", x < 160 ? L"-send" : L"-receive", nullptr, SW_SHOWNORMAL);
        } else if (g_detailsVisible && g_detailsKind == 2 && y >= 300 && y <= 328 && x >= 270) {
            if (g_bluetoothScanning) return 0;
            g_bluetoothScanning = true;
            InvalidateRect(hwnd, nullptr, FALSE);
            UpdateWindow(hwnd);
            RefreshBluetoothDevices();
            g_bluetoothScanning = false;
            g_status = Tr(L"蓝牙 · 扫描完成", L"Bluetooth · Scan complete");
        } else if (g_detailsVisible && g_detailsKind == 2 && y >= 330 && y < 426) {
            const auto index = g_bluetoothDeviceOffset + static_cast<size_t>((y - 330) / 24);
            if (index < g_bluetoothDevices.size() && !ToggleBluetoothDevice(hwnd, g_bluetoothDevices[index])) g_status = Tr(L"蓝牙设备操作失败", L"Bluetooth device operation failed");
        } else if (g_detailsVisible && g_detailsKind == 2 && y >= 228 && y < 300) {
            const auto index = static_cast<size_t>((y - 228) / 24);
            if (index < g_bluetoothAdapters.size()) {
                if (g_bluetoothAdapters[index].enabled) {
                    if (!SetBluetoothAdapterEnabled(index, false)) g_status = Tr(L"蓝牙 · 禁用失败", L"Bluetooth · Disable failed");
                    else g_status = std::wstring(Tr(L"蓝牙 · 已禁用 ", L"Bluetooth · Disabled ")) + g_bluetoothAdapters[index].name;
                } else {
                    ChooseBluetoothAdapter(hwnd, g_bluetoothRecoveryPending);
                }
            }
        } else if (g_detailsVisible && g_detailsKind == 4 && y >= 220 && y < 276 && !g_usbInterface.empty()) {
            if (!SetUsbTethering(!g_usbOn)) g_status = Tr(L"USB 共享 · 操作失败", L"USB tether · Operation failed");
        } else if (g_detailsVisible && g_detailsKind == 5 && y >= 224 && y < 266) {
            const bool extend = x >= 160;
            if (!SwitchDisplayMode(extend)) ShowToast(Tr(L"屏幕模式切换失败", L"Display mode switch failed"));
            else ShowToast(extend ? Tr(L"已切换为扩展屏幕", L"Switched to extended displays") : Tr(L"已切换为复制屏幕", L"Switched to duplicate displays"));
        } else if (g_detailsVisible && g_detailsKind == 5 && y >= 266 && y < 308) {
            ShellExecuteW(hwnd, L"open", L"ms-settings:display", nullptr, nullptr, SW_SHOWNORMAL);
        } else if (g_detailsVisible && g_detailsKind == 5 && y >= 308 && y < 350 && g_hdrSupported) {
            ShowToast(SetHdrEnabled(!g_hdrEnabled) ? (g_hdrEnabled ? Tr(L"HDR 已启用", L"HDR enabled") : Tr(L"HDR 已关闭", L"HDR disabled")) : Tr(L"HDR 操作失败", L"HDR operation failed"));
    } else if (g_detailsVisible && g_detailsKind == 5 && y >= 350 && y < 388 && g_activeDisplayCount == 0) {
        // pnputil can block for minutes behind a UAC prompt, so install/enable
        // runs on a worker thread; kVddWorkMessage reports the outcome.
        const int state = DetectVirtualDisplayState();
        std::wstring infPath;
        if (state == 0) {
            ShowToast(Tr(L"虚拟屏驱动已就绪", L"The virtual display driver is already ready"));
        } else if (g_vddWorkInProgress) {
            // Already running; its completion toast reports the outcome.
        } else if (state == 2 && !FindVirtualDriverInf(infPath)) {
            ShowToast(Tr(L"未找到虚拟屏驱动，请将已签名 INF 放入 drivers 文件夹", L"No virtual display driver found; put a signed INF into the drivers folder"));
        } else {
            g_vddWorkInProgress = true;
            ShowToast(state == 2
                ? Tr(L"正在安装虚拟屏驱动…", L"Installing the virtual display driver…")
                : Tr(L"正在启用虚拟屏…", L"Enabling the virtual display…"));
            auto* work = new VddWork{ hwnd, state == 2 };
            if (CreateThread(nullptr, 0, VddWorkThread, work, 0, nullptr) == nullptr) {
                g_vddWorkInProgress = false;
                delete work;
                ShowToast(Tr(L"启用失败，请以管理员身份运行后重试", L"Enable failed; run as administrator and retry"));
            }
        }
    } else if (g_detailsVisible && g_detailsKind == 5 && y >= 392 && y < 424) {
            g_vddAuto = !g_vddAuto;
            SaveVddAuto();
            ShowToast(g_vddAuto
                ? Tr(L"未检测到实体屏幕时将自动启用虚拟屏", L"The virtual display will auto-enable without a display")
                : Tr(L"已关闭虚拟屏自动启用", L"Virtual display auto-enable turned off"));
        } else if (g_detailsVisible && g_detailsKind == 3 && y >= 228 && y < 468) {
            const auto index = static_cast<size_t>((y - 228) / 24);
            if (index < g_networkAdapters.size()) {
                const auto& adapter = g_networkAdapters[index];
                const auto value = Trim(PromptText(hwnd, adapter.name.c_str(),
                    Tr(L"接口跃点数（1–9999，数值越小优先级越高）", L"Interface metric (1–9999; lower values have higher priority)")));
                if (!value.empty()) {
                    wchar_t* end{};
                    const auto metric = wcstoul(value.c_str(), &end, 10);
                    if (end == value.c_str() || *end != L'\0' || metric < 1 || metric > 9999) {
                        MessageBoxW(hwnd, Tr(L"请输入 1 到 9999 之间的整数。", L"Enter an integer between 1 and 9999."), kTitle, MB_OK | MB_ICONWARNING);
                    } else if (!SetAdapterMetric(index, static_cast<ULONG>(metric))) {
                        g_status = Tr(L"网卡优先级调整失败", L"Network priority update failed");
                    } else {
                        g_status = std::wstring(Tr(L"已设置网卡优先级：", L"Adapter priority updated: ")) + std::to_wstring(metric);
                    }
                }
            }
        } else {
            const int networkTop = CurrentNetworkTop();
            if (g_currentNetworkExpanded && y >= networkTop + 96 && y <= networkTop + 122 && x >= 24) {
                // The DoH summary line itself opens the DoH settings page.
                g_page = Page::Doh;
                RefreshDohServers();
            } else if (x >= 294 && x <= 326 && y >= networkTop + 11 && y <= networkTop + 39) {
                g_currentNetworkExpanded = !g_currentNetworkExpanded;
            } else if (y >= DesiredWindowHeight() - 60) {
            g_page = Page::Settings;
            RefreshStartWithWindows();
            }
        }
        ResizeNearTray();
        InvalidateRect(hwnd, nullptr, TRUE);
        return 0;
    }
    case kTrayMessage:
    {
        // NOTIFYICON_VERSION_4 packs the event into LOWORD(lParam) and the
        // cursor Y coordinate into HIWORD(lParam). Comparing the whole LPARAM
        // silently loses both left-click toggling and the context menu.
        const UINT trayEvent = LOWORD(static_cast<DWORD_PTR>(lparam));
        if (trayEvent == WM_LBUTTONUP || trayEvent == WM_LBUTTONDBLCLK || trayEvent == NIN_SELECT || trayEvent == NIN_KEYSELECT) ToggleWindow();
        else if (trayEvent == WM_RBUTTONUP || trayEvent == WM_CONTEXTMENU) ShowTrayMenu(hwnd);
        return 0;
    }
    case WM_COMMAND:
        switch (LOWORD(wparam)) {
        case kTrayOpen: g_page = Page::Home; ToggleWindow(); break;
        case kTraySettings:
            g_page = Page::Settings;
            RefreshStartWithWindows();
            if (!IsWindowVisible(hwnd)) ToggleWindow(); else InvalidateRect(hwnd, nullptr, TRUE);
            break;
        case kTrayExit: DestroyWindow(hwnd); break;
        }
        return 0;
    case WM_KEYDOWN:
        if (g_wifiSavedSearchActive && wparam == VK_ESCAPE) {
            g_wifiSavedSearchActive = false;
            InvalidateRect(hwnd, nullptr, FALSE);
            return 0;
        }
        if (g_dohPicker != 0 && wparam == VK_ESCAPE) {
            g_dohPicker = 0;
            g_dohPickerSearch.clear();
            InvalidateRect(hwnd, nullptr, FALSE);
            return 0;
        }
        if (wparam == VK_ESCAPE) {
            ShowWindow(hwnd, SW_HIDE);
            return 0;
        }
        break;
    case WM_CHAR:
        if (g_wifiSavedSearchActive) {
            const auto character = static_cast<wchar_t>(wparam);
            if (character == L'\b') { if (!g_wifiSearch.empty()) g_wifiSearch.pop_back(); }
            else if (character >= L' ') g_wifiSearch.push_back(character);
            g_wifiSavedOffset = 0;
            RefreshSavedWifi();
            InvalidateRect(hwnd, nullptr, FALSE);
            return 0;
        }
        if (g_dohPicker != 0) {
            const auto character = static_cast<wchar_t>(wparam);
            if (character == L'\b') { if (!g_dohPickerSearch.empty()) g_dohPickerSearch.pop_back(); }
            else if (character >= L' ') g_dohPickerSearch.push_back(character);
            InvalidateRect(hwnd, nullptr, FALSE);
            return 0;
        }
        break;
    case WM_DESTROY:
        KillTimer(hwnd, kRefreshTimer);
        KillTimer(hwnd, kFadeTimer);
        KillTimer(hwnd, kActivateTimer);
        Shell_NotifyIconW(NIM_DELETE, &g_tray);
        if (g_trayIcon) DestroyIcon(g_trayIcon);
        if (g_activateEvent) CloseHandle(g_activateEvent);
        if (g_mutex) { ReleaseMutex(g_mutex); CloseHandle(g_mutex); }
        PostQuitMessage(0);
        return 0;
    }
    return DefWindowProcW(hwnd, message, wparam, lparam);
}
}

int APIENTRY wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int) {
    g_mutex = CreateMutexW(nullptr, TRUE, L"Local\\WindowsExtendQuickSetting.SingleInstance");
    if (GetLastError() == ERROR_ALREADY_EXISTS) {
        HANDLE activate = OpenEventW(EVENT_MODIFY_STATE, FALSE, L"Local\\WindowsExtendQuickSetting.Activate");
        if (activate) { SetEvent(activate); CloseHandle(activate); }
        CloseHandle(g_mutex);
        return 0;
    }
    g_activateEvent = CreateEventW(nullptr, FALSE, FALSE, L"Local\\WindowsExtendQuickSetting.Activate");

    WNDCLASSW wc{};
    wc.hInstance = instance;
    wc.lpszClassName = kClassName;
    wc.lpfnWndProc = WindowProc;
    wc.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    wc.hIcon = LoadIconW(instance, MAKEINTRESOURCEW(1));
    RegisterClassW(&wc);

    g_window = CreateWindowExW(WS_EX_TOOLWINDOW | WS_EX_LAYERED, kClassName, kTitle, WS_POPUP,
        0, 0, kWindowWidth, kPageHeight, nullptr, nullptr, instance, nullptr);
    ApplyWindowChrome();

    g_tray.cbSize = sizeof(g_tray);
    g_tray.hWnd = g_window;
    g_tray.uID = kTrayId;
    g_tray.uFlags = NIF_ICON | NIF_MESSAGE | NIF_TIP | NIF_GUID;
    g_tray.uCallbackMessage = kTrayMessage;
    g_trayIcon = LoadTrayIcon();
    g_tray.hIcon = g_trayIcon ? g_trayIcon : LoadIconW(instance, MAKEINTRESOURCEW(1));
    lstrcpynW(g_tray.szTip, kTitle, ARRAYSIZE(g_tray.szTip));
    g_tray.guidItem = kTrayGuid;
    Shell_NotifyIconW(NIM_ADD, &g_tray);
    g_tray.uVersion = NOTIFYICON_VERSION_4;
    Shell_NotifyIconW(NIM_SETVERSION, &g_tray);
    RefreshLanguage();
    RefreshVddAuto();
    RefreshWifiStatus();
    RefreshBluetoothAdapters();
    RefreshBluetoothDevices();
    RefreshDohServers();
    RefreshDohEnabled();
    RefreshStartWithWindows();
    RefreshUsbTethering();
    RefreshNetworkAdapters();
    RefreshDisplayState();
    ApplyWindowChrome();
    SetTimer(g_window, kActivateTimer, 120, nullptr);
    ToggleWindow();

    MSG message{};
    while (GetMessageW(&message, nullptr, 0, 0)) {
        TranslateMessage(&message);
        DispatchMessageW(&message);
    }
    if (g_mutex) { ReleaseMutex(g_mutex); CloseHandle(g_mutex); }
    return 0;
}
