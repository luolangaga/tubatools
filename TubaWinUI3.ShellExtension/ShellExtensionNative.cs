using System.Runtime.InteropServices;

namespace TubaWinUi3.ShellExtension;

/// <summary>Shell 扩展用到的 Win32 P/Invoke 与结构体（仅 kernel32，无 WinRT/WinUI 依赖）。</summary>
internal static unsafe class ShellExtensionNative
{
    internal const uint GetModuleHandleExFromAddress = 0x00000004;
    internal const uint GetModuleHandleExUnchangedRefCount = 0x00000002;

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "GetModuleHandleExW", CharSet = CharSet.Unicode)]
    internal static extern bool GetModuleHandleEx(uint flags, nint lpModuleName, out nint phModule);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "GetModuleFileNameW", CharSet = CharSet.Unicode)]
    internal static extern uint GetModuleFileName(nint hModule, char* lpFilename, uint nSize);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "GetCurrentPackageFamilyName")]
    internal static extern int GetCurrentPackageFamilyName(ref int packageFamilyNameLength, char* packageFamilyName);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode)]
    internal static extern bool CreateProcess(
        string? lpApplicationName,
        char* lpCommandLine,
        nint lpProcessAttributes,
        nint lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        nint lpEnvironment,
        string? lpCurrentDirectory,
        ref StartupInfo lpStartupInfo,
        out ProcessInformation lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(nint hObject);

    /// <summary>
    /// STARTUPINFO 的最小占位：只用到 cb，LPWSTR 字段用 nint（恒为 0）表示 ——
    /// 全程无托管类型，NativeAOT 下 sizeof/指针传递安全，不需要运行时封送。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct StartupInfo
    {
        public int cb;
        public nint lpReserved;
        public nint lpDesktop;
        public nint lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public nint lpReserved2;
        public nint hStdInput;
        public nint hStdOutput;
        public nint hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessInformation
    {
        public nint hProcess;
        public nint hThread;
        public int dwProcessId;
        public int dwThreadId;
    }
}
