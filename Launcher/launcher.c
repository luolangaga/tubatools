#ifndef UNICODE
#define UNICODE
#endif

#include <windows.h>
#include <shlwapi.h>

#pragma comment(lib, "shlwapi.lib")

int WINAPI wWinMain(HINSTANCE hInstance, HINSTANCE hPrevInstance, PWSTR lpCmdLine, int nCmdShow)
{
    WCHAR exePath[MAX_PATH];
    GetModuleFileNameW(NULL, exePath, MAX_PATH);

    WCHAR dir[MAX_PATH];
    wcscpy_s(dir, _countof(dir), exePath);
    PathRemoveFileSpecW(dir);

    WCHAR target[MAX_PATH];
    PathCombineW(target, dir, L"src\\TubaWinUi3.exe");

    if (GetFileAttributesW(target) == INVALID_FILE_ATTRIBUTES)
    {
        WCHAR msg[MAX_PATH + 64];
        wsprintfW(msg, L"找不到程序文件：\n%s", target);
        MessageBoxW(NULL, msg, L"图吧工具箱WinUI3", MB_OK | MB_ICONERROR);
        return 1;
    }

    SHELLEXECUTEINFOW sei = { sizeof(sei) };
    sei.fMask = SEE_MASK_NOASYNC;
    sei.lpVerb = L"open";
    sei.lpFile = target;
    sei.lpDirectory = dir;
    sei.nShow = SW_SHOWNORMAL;

    if (!ShellExecuteExW(&sei))
    {
        WCHAR msg[128];
        wsprintfW(msg, L"启动失败，错误代码：%lu", GetLastError());
        MessageBoxW(NULL, msg, L"图吧工具箱WinUI3", MB_OK | MB_ICONERROR);
        return 1;
    }

    return 0;
}
