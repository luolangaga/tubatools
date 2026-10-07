using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using TubaWinUi3.ShellIntegration;

namespace TubaWinUi3.ShellExtension;

/// <summary>IExplorerCommand（框架接口声明；只声明，不调用——本地实现由本 DLL 提供）。</summary>
[GeneratedComInterface]
[Guid("A08CE4D0-FA25-44AB-B57C-C7B1C323E0B9")]
internal partial interface IExplorerCommand
{
    [PreserveSig]
    int GetTitle(nint psiItemArray, out nint ppszName);

    [PreserveSig]
    int GetIcon(nint psiItemArray, out nint ppszIcon);

    [PreserveSig]
    int GetToolTip(nint psiItemArray, out nint ppszInfotip);

    [PreserveSig]
    int GetCanonicalName(out Guid pguidCommandName);

    [PreserveSig]
    int GetState(nint psiItemArray, int fOkToBeSlow, out int pCmdState);

    [PreserveSig]
    int Invoke(nint psiItemArray, nint pbc);

    [PreserveSig]
    int GetFlags(out int pFlags);

    [PreserveSig]
    int EnumSubCommands(out nint ppEnum);
}

/// <summary>
/// Win11 新版右键菜单命令（「用图吧工具箱检测文件占用」）。
/// 由 dllhost（包内 COM 代理）加载，Explorer 构建菜单时调用 GetTitle/GetIcon/GetState，
/// 点击时调用 Invoke。菜单构建路径上的方法必须快（只读一个几 KB 的标记文件），耗时操作只在 Invoke。
/// 开关状态 = 标记文件是否存在（主程序在工具页开关；不存在 → ECS_HIDDEN，条目不出现在菜单里）。
/// </summary>
[GeneratedComClass]
internal sealed partial class FileLockExplorerCommand : IExplorerCommand
{
    private const int SOk = 0;
    private const int ENotImpl = unchecked((int)0x80004001);

    private const int EcsEnabled = 0;
    private const int EcsHidden = 2;

    public int GetTitle(nint psiItemArray, out nint ppszName)
    {
        ppszName = AllocateString(
            FileLockShellMenuContract.TryReadMarker(ShellExtensionPaths.DataRoots(), out var title, out _)
                ? title
                : FileLockShellMenuContract.DefaultTitle);
        return SOk;
    }

    public int GetIcon(nint psiItemArray, out nint ppszIcon)
    {
        var icon = ShellExtensionPaths.TryGetIconReference();
        if (icon is not null)
        {
            ppszIcon = AllocateString(icon);
            return SOk;
        }

        ppszIcon = 0;
        return ENotImpl;
    }

    public int GetToolTip(nint psiItemArray, out nint ppszInfotip)
    {
        if (FileLockShellMenuContract.TryReadMarker(ShellExtensionPaths.DataRoots(), out _, out var tooltip))
        {
            ppszInfotip = AllocateString(tooltip);
            return SOk;
        }

        ppszInfotip = 0;
        return ENotImpl;
    }

    public int GetCanonicalName(out Guid pguidCommandName)
    {
        // 官方示例约定：无规范名时返回 GUID_NULL
        pguidCommandName = Guid.Empty;
        return SOk;
    }

    public int GetState(nint psiItemArray, int fOkToBeSlow, out int pCmdState)
    {
        pCmdState = FileLockShellMenuContract.MarkerExists(ShellExtensionPaths.DataRoots())
            ? EcsEnabled
            : EcsHidden;
        return SOk;
    }

    public int Invoke(nint psiItemArray, nint pbc)
    {
        try
        {
            var path = ShellItemSelection.TryGetFirstFilesystemPath(psiItemArray);
            if (string.IsNullOrEmpty(path))
            {
                ShellExtensionPaths.Log("Invoke：未能从选中项取到文件系统路径，已忽略");
                return SOk;
            }

            var launched = ShellExtensionPaths.TryLaunchFileLockCheck(path, out var detail);
            ShellExtensionPaths.Log(launched
                ? $"Invoke：已启动文件占用查看（{path}）"
                : $"Invoke：启动失败（{detail}）目标（{path}）");
        }
        catch (Exception ex)
        {
            // COM 回调异常绝不能外泄（会穿透 ABI 变成崩溃）：记录后按成功返回
            ShellExtensionPaths.Log("Invoke 异常：" + ex.Message);
        }

        return SOk;
    }

    public int GetFlags(out int pFlags)
    {
        pFlags = 0; // ECF_DEFAULT
        return SOk;
    }

    public int EnumSubCommands(out nint ppEnum)
    {
        ppEnum = 0;
        return ENotImpl; // 单命令，不级联子菜单
    }

    /// <summary>返回 CoTaskMem 分配的 UTF-16 字符串（Shell 用 CoTaskMemFree 释放）。</summary>
    private static nint AllocateString(string value)
    {
        var bytes = System.Text.Encoding.Unicode.GetBytes(value + "\0");
        var pointer = Marshal.AllocCoTaskMem(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return pointer;
    }
}

/// <summary>
/// 读取选中项路径。这里手工走 IShellItemArray / IShellItem 的 vtable（不经 ComWrappers）：
/// 处理程序运行在 NativeAOT 下，只取「第一项文件系统路径」一个需求，手工调用最直接、依赖最少。
/// </summary>
internal static unsafe class ShellItemSelection
{
    private static readonly Guid ShellItemIid = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");
    private const uint SigdnFilesysPath = 0x80058000;

    internal static string? TryGetFirstFilesystemPath(nint shellItemArray)
    {
        if (shellItemArray == 0) return null;

        // vtable（IUnknown 3 槽之后）：
        //   3 BindToHandler / 4 GetPropertyStore / 5 GetPropertyDescriptionList /
        //   6 GetAttributes / 7 GetCount / 8 GetItemAt / 9 EnumItems
        var arrayVtable = *(nint**)shellItemArray;
        var getCount = (delegate* unmanaged[Stdcall]<nint, uint*, int>)arrayVtable[7];
        var getItemAt = (delegate* unmanaged[Stdcall]<nint, uint, Guid*, nint*, int>)arrayVtable[8];

        uint count = 0;
        if (getCount(shellItemArray, &count) < 0 || count == 0) return null;

        nint shellItem = 0;
        var iid = ShellItemIid;
        if (getItemAt(shellItemArray, 0, &iid, &shellItem) < 0 || shellItem == 0) return null;

        try
        {
            // IShellItem vtable：3 BindToHandler / 4 GetParent / 5 GetDisplayName / 6 GetAttributes / 7 Compare
            var itemVtable = *(nint**)shellItem;
            var getDisplayName = (delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)itemVtable[5];

            nint name = 0;
            if (getDisplayName(shellItem, SigdnFilesysPath, &name) < 0 || name == 0) return null;
            try
            {
                return Marshal.PtrToStringUni(name);
            }
            finally
            {
                Marshal.FreeCoTaskMem(name);
            }
        }
        finally
        {
            var release = (delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)shellItem)[2];
            release(shellItem);
        }
    }
}
