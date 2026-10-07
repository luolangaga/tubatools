using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using TubaWinUi3.ShellIntegration;

namespace TubaWinUi3.ShellExtension;

/// <summary>IClassFactory（标准 COM 类工厂，用于 DllGetClassObject 交出命令实例）。</summary>
[GeneratedComInterface]
[Guid("00000001-0000-0000-C000-000000000046")]
internal partial interface IClassFactory
{
    [PreserveSig]
    int CreateInstance(nint pUnkOuter, ref Guid riid, out nint ppvObject);

    [PreserveSig]
    int LockServer(int fLock);
}

/// <summary>
/// COM 服务器入口：导出 DllGetClassObject / DllCanUnloadNow（dllhost 以 LoadLibrary 方式加载）。
/// NativeAOT 把 [UnmanagedCallersOnly(EntryPoint=…)] 的方法直接写进 DLL 导出表；
/// 对象经 StrategyBasedComWrappers 包装成 COM 可见的指针（源生成互操作，AOT 无反射）。
/// </summary>
internal static unsafe class ExplorerCommandServer
{
    private const int SOk = 0;
    private const int ENoInterface = unchecked((int)0x80004002);
    private const int EFail = unchecked((int)0x80004005);
    private const int EPointer = unchecked((int)0x80004003);
    private const int ClassENotAvailable = unchecked((int)0x80040111);

    private static readonly StrategyBasedComWrappers Wrappers = new();

    internal static nint GetComInterfaceForObject(object value) =>
        Wrappers.GetOrCreateComInterfaceForObject(value, CreateComInterfaceFlags.None);

    [UnmanagedCallersOnly(EntryPoint = "DllGetClassObject", CallConvs = [typeof(CallConvStdcall)])]
    public static int DllGetClassObject(Guid* rclsid, Guid* riid, void** ppv)
    {
        if (ppv is null) return EPointer;
        *ppv = null;
        if (rclsid is null || riid is null) return EPointer;

        if (*rclsid != FileLockShellMenuContract.CommandClsidGuid)
        {
            ShellExtensionPaths.Log($"DllGetClassObject：未知 CLSID {*rclsid:B}");
            return ClassENotAvailable;
        }

        nint factoryUnknown = 0;
        try
        {
            factoryUnknown = GetComInterfaceForObject(new CommandClassFactory());
            var iid = *riid;
            var hr = Marshal.QueryInterface(factoryUnknown, in iid, out var result);
            if (hr < 0) return hr;
            *ppv = (void*)result;
            return SOk;
        }
        catch (Exception ex)
        {
            ShellExtensionPaths.Log("DllGetClassObject 异常：" + ex.Message);
            return EFail;
        }
        finally
        {
            if (factoryUnknown != 0) Marshal.Release(factoryUnknown);
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "DllCanUnloadNow", CallConvs = [typeof(CallConvStdcall)])]
    public static int DllCanUnloadNow()
    {
        // S_FALSE：保持加载。命令对象生命周期很短，卸载 DLL 没有收益，反而引入时序风险。
        return 1;
    }
}

[GeneratedComClass]
internal sealed partial class CommandClassFactory : IClassFactory
{
    private const int SOk = 0;
    private const int EFail = unchecked((int)0x80004005);
    private const int ClassENoAggregation = unchecked((int)0x80040110);

    public int CreateInstance(nint pUnkOuter, ref Guid riid, out nint ppvObject)
    {
        ppvObject = 0;
        if (pUnkOuter != 0) return ClassENoAggregation; // 不支持聚合

        nint unknown = 0;
        try
        {
            unknown = ExplorerCommandServer.GetComInterfaceForObject(new FileLockExplorerCommand());
            var hr = Marshal.QueryInterface(unknown, in riid, out ppvObject);
            return hr < 0 ? hr : SOk;
        }
        catch (Exception ex)
        {
            ShellExtensionPaths.Log("CreateInstance 异常：" + ex.Message);
            return EFail;
        }
        finally
        {
            if (unknown != 0) Marshal.Release(unknown);
        }
    }

    public int LockServer(int fLock) => SOk;
}
