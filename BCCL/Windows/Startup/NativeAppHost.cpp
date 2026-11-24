#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <objbase.h>
#include <shellapi.h>

#include <cstdint>
#include <filesystem>
#include <hostfxr.h>
#include <nethost.h>

namespace
{
    constexpr int missingDotnetHost = static_cast<int>(0x80008083);
    constexpr int missingFramework = static_cast<int>(0x80008096);
    constexpr int hostStartupFailed = static_cast<int>(0x80008084);
    constexpr wchar_t desktopRuntimeDownload[] = L"https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe";
    constexpr DWORD pathCapacity = 32768;

    // CreateAppHost writes the managed assembly name into this UTF-8 buffer.
    char assemblyName[1025] =
        "c3ab8ff13720e8ad9047dd39466b3c8974e592c2fa383d4a3960714caef0c4f2";

    // The .NET bundler writes a little-endian header offset immediately before its signature.
    struct BundleMarker
    {
        int64_t offset;
        uint8_t signature[32];
    };

    volatile BundleMarker bundle = { 0, {
        0x8b, 0x12, 0x02, 0xb9, 0x6a, 0x61, 0x20, 0x38,
        0x72, 0x7b, 0x93, 0x02, 0x14, 0xd7, 0xa0, 0x32,
        0x13, 0xf5, 0xb9, 0xe6, 0xef, 0xae, 0x33, 0x18,
        0xee, 0x3b, 0x2d, 0xce, 0x24, 0xb3, 0x6a, 0xae
    } };

    int RunLauncher(int argc, const wchar_t** argv)
    {
        wchar_t executable[pathCapacity];
        const DWORD length = GetModuleFileNameW(nullptr, executable, pathCapacity);
        wchar_t managedName[sizeof(assemblyName)];
        if (length == 0 || length == pathCapacity || bundle.offset == 0 ||
            !MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, assemblyName, -1, managedName, _countof(managedName)))
        {
            return hostStartupFailed;
        }

        const auto assemblyPath = std::filesystem::path(executable).parent_path() / managedName;
        wchar_t hostfxrPath[pathCapacity];
        size_t hostfxrPathLength = _countof(hostfxrPath);
        const int status = get_hostfxr_path(hostfxrPath, &hostfxrPathLength, nullptr);
        if (status != 0)
            return status;

        // nethost resolves the runtime's host/fxr/<version>/hostfxr.dll.
        const auto dotnetRoot = std::filesystem::path(hostfxrPath)
            .parent_path().parent_path().parent_path().parent_path();
        const HMODULE host = LoadLibraryExW(hostfxrPath, nullptr,
            LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
        if (!host)
            return hostStartupFailed;

        const auto run = reinterpret_cast<hostfxr_main_bundle_startupinfo_fn>(
            GetProcAddress(host, "hostfxr_main_bundle_startupinfo"));
        if (!run)
            return missingFramework;

        return run(argc, argv, executable, dotnetRoot.c_str(), assemblyPath.c_str(), bundle.offset);
    }
}

int wmain(int argc, const wchar_t** argv)
{
    const int status = RunLauncher(argc, argv);
    if (status == missingDotnetHost || status == missingFramework)
    {
        // Retain the .NET apphost's opt-out for noninteractive launches.
        wchar_t disableGui[2];
        if (GetEnvironmentVariableW(L"DOTNET_DISABLE_GUI_ERRORS", disableGui, _countof(disableGui)) != 1 ||
            disableGui[0] != L'1')
        {
            const HRESULT com = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED | COINIT_DISABLE_OLE1DDE);
            SHELLEXECUTEINFOW download = {};
            download.cbSize = sizeof(download);
            download.fMask = SEE_MASK_NOASYNC | SEE_MASK_FLAG_NO_UI;
            download.lpVerb = L"open";
            download.lpFile = desktopRuntimeDownload;
            download.nShow = SW_SHOWNORMAL;
            const BOOL opened = ShellExecuteExW(&download);
            if (SUCCEEDED(com))
                CoUninitialize();
            if (opened)
                MessageBoxW(nullptr,
                    L"BCCL 需要 .NET 8 桌面运行时（x64）。\n\n"
                    L"已在默认浏览器中打开微软的下载链接。安装完成后，请重新打开 BCCL。",
                    L"BCCL 需要安装运行时", MB_OK | MB_ICONINFORMATION | MB_SETFOREGROUND);
            else
                MessageBoxW(nullptr, desktopRuntimeDownload, L".NET 8 Desktop Runtime (x64)", MB_OK | MB_ICONERROR);
        }
    }
    return status;
}
