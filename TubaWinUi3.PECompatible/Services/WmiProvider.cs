using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace TubaWinUi3.Compatible.Services
{
    /// <summary>
    /// PE 兼容版（NativeAOT）的 WMI 查询实现。
    ///
    /// System.Management 的 WbemContext / WbemObjectTextSrc / WbemStatusCodeText 依赖托管 COM
    /// interop，ILC 在 full AOT 下会直接判定它们 "COM interop is not supported with full ahead
    /// of time compilation"（IL3052），运行时必然抛异常。这里改成直接调用 WMI 的 COM vtable：
    /// IWbemLocator -> IWbemServices -> IEnumWbemClassObject -> IWbemClassObject，
    /// 用 unmanaged 函数指针完成所有调用，不经过任何托管 COM 封装或动态代码生成。
    ///
    /// 线程模型：固定一个 MTA 工作线程串行处理查询，IWbemServices 按命名空间缓存复用。
    /// （COM 的模拟级别通过 CoSetProxyBlanket 逐个代理设置——不能只依赖进程级的
    /// CoInitializeSecurity，因为应用里别的组件可能已经抢先设成了 Identify 级别。）
    ///
    /// 与 TubaWinUi3.Compatible/Services/WmiProvider.cs 保持同名同签名。
    /// </summary>
    public static unsafe class WmiProvider
    {
        public const string DefaultNamespace = @"root\cimv2";

        private const int S_OK = 0;

        private const uint CLSCTX_INPROC_SERVER = 1;
        private const uint COINIT_MULTITHREADED = 0;

        private const int WBEM_FLAG_RETURN_IMMEDIATELY = 0x10;
        private const int WBEM_FLAG_FORWARD_ONLY = 0x20;

        // COM 代理安全级别（CoSetProxyBlanket）。
        private const uint RPC_C_AUTHN_WINNT = 10;
        private const uint RPC_C_AUTHZ_NONE = 0;
        private const uint RPC_C_AUTHN_LEVEL_CALL = 3;
        private const uint RPC_C_IMP_LEVEL_IMPERSONATE = 3;
        private const uint EOAC_NONE = 0;

        /// <summary>单次 IEnumWbemClassObject::Next 的等待上限（毫秒）。</summary>
        private const uint NextTimeoutMs = 15000;

        /// <summary>调用方等待整次查询的上限（毫秒）；超时按「无数据」处理，不阻塞界面。</summary>
        private const int QueryTimeoutMs = 20000;

        // 各 COM 接口的 vtable 槽位（IUnknown 占 0..2），顺序以 Windows SDK 的 WbemCli.h 为准。
        // IWbemClassObject: GetQualifierSet(3) Get(4) Put(5) Delete(6) GetNames(7)
        //                   BeginEnumeration(8) Next(9) EndEnumeration(10) ...
        private const int WbemClassObjectBeginEnumerationIndex = 8;
        private const int WbemClassObjectNextIndex = 9;
        private const int WbemClassObjectEndEnumerationIndex = 10;
        // IWbemServices: OpenNamespace(3) CancelAsyncCall(4) QueryObjectSink(5) GetObject(6) GetObjectAsync(7)
        // PutClass(8) PutClassAsync(9) DeleteClass(10) DeleteClassAsync(11) CreateClassEnum(12) CreateClassEnumAsync(13)
        // PutInstance(14) PutInstanceAsync(15) DeleteInstance(16) DeleteInstanceAsync(17) CreateInstanceEnum(18)
        // CreateInstanceEnumAsync(19) ExecQuery(20) ...
        private const int WbemServicesExecQueryIndex = 20;
        private const int WbemLocatorConnectServerIndex = 3;
        // IEnumWbemClassObject: Reset(3) Next(4) NextAsync(5) Clone(6) Skip(7)
        private const int EnumWbemClassObjectNextIndex = 4;
        private const int IUnknownReleaseIndex = 2;

        private static readonly Guid CLSID_WbemLocator = new Guid("4590F811-1D3A-11D0-891F-00AA004B2E24");
        private static readonly Guid IID_IWbemLocator = new Guid("DC12A687-737F-11CF-884D-00AA004B2E24");

        private const ushort VT_EMPTY = 0;
        private const ushort VT_NULL = 1;
        private const ushort VT_I2 = 2;
        private const ushort VT_I4 = 3;
        private const ushort VT_R4 = 4;
        private const ushort VT_R8 = 5;
        private const ushort VT_BSTR = 8;
        private const ushort VT_BOOL = 11;
        private const ushort VT_I1 = 16;
        private const ushort VT_UI1 = 17;
        private const ushort VT_UI2 = 18;
        private const ushort VT_UI4 = 19;
        private const ushort VT_I8 = 20;
        private const ushort VT_UI8 = 21;
        private const ushort VT_INT = 22;
        private const ushort VT_UINT = 23;
        private const ushort VT_ARRAY = 0x2000;

        /// <summary>x64 下 VARIANT 固定 24 字节：vt 在偏移 0，数据联合体在偏移 8。</summary>
        [StructLayout(LayoutKind.Explicit, Size = 24)]
        private struct VARIANT
        {
            [FieldOffset(0)] public ushort vt;
            [FieldOffset(8)] public IntPtr bstrVal;
            [FieldOffset(8)] public IntPtr parray;
            [FieldOffset(8)] public IntPtr punkVal;
            [FieldOffset(8)] public byte bVal;
            [FieldOffset(8)] public short iVal;
            [FieldOffset(8)] public int lVal;
            [FieldOffset(8)] public uint ulVal;
            [FieldOffset(8)] public long llVal;
            [FieldOffset(8)] public ulong ullVal;
            [FieldOffset(8)] public float fltVal;
            [FieldOffset(8)] public double dblVal;
        }

        private sealed class QueryRequest
        {
            public string Namespace;
            public string Wql;
            public IReadOnlyList<WmiRow> Result;
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
        }

        private static readonly BlockingCollection<QueryRequest> Requests =
            new BlockingCollection<QueryRequest>();

        private static readonly Dictionary<string, IntPtr> ServiceCache =
            new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);

        private static readonly object WorkerGate = new object();
        private static Thread _worker;

        [DllImport("ole32.dll", ExactSpelling = true)]
        private static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid riid, out IntPtr ppv);

        [DllImport("ole32.dll", ExactSpelling = true)]
        private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

        /// <summary>进程级 COM 安全级别（尽力而为；真正起作用的是 CoSetProxyBlanket）。</summary>
        [DllImport("ole32.dll", ExactSpelling = true)]
        private static extern int CoInitializeSecurity(
            IntPtr pSecDesc,
            int cAuthSvc,
            IntPtr asAuthSvc,
            IntPtr pReserved1,
            uint dwAuthnLevel,
            uint dwImpLevel,
            IntPtr pAuthList,
            uint dwCapabilities,
            IntPtr pReserved3);

        /// <summary>为单个代理设置认证/模拟级别；这是 WMI 客户端不依赖进程级设置的标准做法。</summary>
        [DllImport("ole32.dll", ExactSpelling = true)]
        private static extern int CoSetProxyBlanket(
            IntPtr pProxy,
            uint dwAuthnSvc,
            uint dwAuthzSvc,
            IntPtr pServerPrincName,
            uint dwAuthnLevel,
            uint dwImpLevel,
            IntPtr pAuthInfo,
            uint dwCapabilities);

        [DllImport("ole32.dll", ExactSpelling = true)]
        private static extern void CoUninitialize();

        [DllImport("oleaut32.dll", ExactSpelling = true)]
        private static extern int VariantClear(IntPtr pvarg);

        [DllImport("oleaut32.dll", ExactSpelling = true)]
        private static extern int SafeArrayGetLBound(IntPtr psa, uint nDim, out int plLbound);

        [DllImport("oleaut32.dll", ExactSpelling = true)]
        private static extern int SafeArrayGetUBound(IntPtr psa, uint nDim, out int plUbound);

        [DllImport("oleaut32.dll", ExactSpelling = true)]
        private static extern int SafeArrayAccessData(IntPtr psa, out IntPtr ppvData);

        [DllImport("oleaut32.dll", ExactSpelling = true)]
        private static extern int SafeArrayUnaccessData(IntPtr psa);

        public static IReadOnlyList<WmiRow> Query(string wql)
        {
            return Query(DefaultNamespace, wql);
        }

        public static IReadOnlyList<WmiRow> Query(string ns, string wql)
        {
            if (string.IsNullOrWhiteSpace(wql))
                return Array.Empty<WmiRow>();

            if (!EnsureWorker())
                return Array.Empty<WmiRow>();

            var request = new QueryRequest
            {
                Namespace = string.IsNullOrWhiteSpace(ns) ? DefaultNamespace : ns,
                Wql = wql
            };

            try
            {
                Requests.Add(request);
            }
            catch (InvalidOperationException)
            {
                return Array.Empty<WmiRow>();
            }

            if (!request.Done.Wait(QueryTimeoutMs))
                return Array.Empty<WmiRow>();

            return request.Result ?? Array.Empty<WmiRow>();
        }

        private static bool EnsureWorker()
        {
            lock (WorkerGate)
            {
                if (_worker != null)
                    return true;

                try
                {
                    var worker = new Thread(WorkerLoop)
                    {
                        IsBackground = true,
                        Name = "TubaWmiWorker"
                    };

                    try { worker.SetApartmentState(ApartmentState.MTA); }
                    catch { }

                    worker.Start();
                    _worker = worker;
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        private static void WorkerLoop()
        {
            CoInitializeEx(IntPtr.Zero, COINIT_MULTITHREADED);

            // 进程级设置可能已被其它组件占用（会返回 RPC_E_TOO_LATE），失败不致命。
            CoInitializeSecurity(IntPtr.Zero, -1, IntPtr.Zero, IntPtr.Zero, 0, 3, IntPtr.Zero, 0, IntPtr.Zero);

            foreach (var request in Requests.GetConsumingEnumerable())
            {
                try
                {
                    request.Result = ExecuteQuery(request.Namespace, request.Wql);
                }
                catch
                {
                    request.Result = Array.Empty<WmiRow>();
                }
                finally
                {
                    request.Done.Set();
                }
            }
        }

        private static IReadOnlyList<WmiRow> ExecuteQuery(string ns, string wql)
        {
            var rows = new List<WmiRow>();

            IntPtr services = GetServices(ns);
            if (services == IntPtr.Zero)
                return rows;

            IntPtr enumerator = IntPtr.Zero;
            try
            {
                if (!TryExecQuery(services, wql, out enumerator))
                {
                    // 缓存的连接可能已失效，下次重新连接。
                    InvalidateServices(ns);
                    return rows;
                }

                var next = (delegate* unmanaged<IntPtr, uint, uint, IntPtr*, uint*, int>)
                    VtableSlot(enumerator, EnumWbemClassObjectNextIndex);

                while (true)
                {
                    IntPtr instance = IntPtr.Zero;
                    uint returned = 0;
                    int hr = next(enumerator, NextTimeoutMs, 1, &instance, &returned);
                    if (hr != S_OK || returned == 0 || instance == IntPtr.Zero)
                    {
                        if (instance != IntPtr.Zero) Release(instance);
                        break;
                    }

                    var row = ReadRow(instance);
                    Release(instance);
                    if (row != null) rows.Add(row);
                }
            }
            catch
            {
            }
            finally
            {
                if (enumerator != IntPtr.Zero) Release(enumerator);
            }

            return rows;
        }

        private static IntPtr GetServices(string ns)
        {
            IntPtr services;
            if (ServiceCache.TryGetValue(ns, out services) && services != IntPtr.Zero)
                return services;

            services = Connect(ns);
            if (services != IntPtr.Zero)
                ServiceCache[ns] = services;

            return services;
        }

        private static void InvalidateServices(string ns)
        {
            IntPtr services;
            if (ServiceCache.TryGetValue(ns, out services))
            {
                ServiceCache.Remove(ns);
                if (services != IntPtr.Zero) Release(services);
            }
        }

        private static IntPtr Connect(string ns)
        {
            IntPtr locator = IntPtr.Zero;
            try
            {
                var clsid = CLSID_WbemLocator;
                var iid = IID_IWbemLocator;
                if (CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_INPROC_SERVER, ref iid, out locator) != S_OK ||
                    locator == IntPtr.Zero)
                    return IntPtr.Zero;

                var connectServer = (delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, IntPtr, IntPtr, IntPtr*, int>)
                    VtableSlot(locator, WbemLocatorConnectServerIndex);

                IntPtr namespaceBstr = Marshal.StringToBSTR(ns);
                IntPtr services = IntPtr.Zero;
                int hr;
                try
                {
                    hr = connectServer(locator, namespaceBstr, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero, &services);
                }
                finally
                {
                    Marshal.FreeBSTR(namespaceBstr);
                }

                if (hr != S_OK || services == IntPtr.Zero)
                    return IntPtr.Zero;

                ApplyProxyBlanket(services);
                return services;
            }
            finally
            {
                if (locator != IntPtr.Zero) Release(locator);
            }
        }

        private static void ApplyProxyBlanket(IntPtr proxy)
        {
            if (proxy == IntPtr.Zero)
                return;

            CoSetProxyBlanket(
                proxy,
                RPC_C_AUTHN_WINNT,
                RPC_C_AUTHZ_NONE,
                IntPtr.Zero,
                RPC_C_AUTHN_LEVEL_CALL,
                RPC_C_IMP_LEVEL_IMPERSONATE,
                IntPtr.Zero,
                EOAC_NONE);
        }

        private static bool TryExecQuery(IntPtr services, string wql, out IntPtr enumerator)
        {
            enumerator = IntPtr.Zero;

            var execQuery = (delegate* unmanaged<IntPtr, IntPtr, IntPtr, int, IntPtr, IntPtr*, int>)
                VtableSlot(services, WbemServicesExecQueryIndex);

            IntPtr language = Marshal.StringToBSTR("WQL");
            IntPtr query = Marshal.StringToBSTR(wql);
            IntPtr result = IntPtr.Zero;
            int hr;
            try
            {
                hr = execQuery(services, language, query,
                    WBEM_FLAG_RETURN_IMMEDIATELY | WBEM_FLAG_FORWARD_ONLY, IntPtr.Zero, &result);
            }
            finally
            {
                Marshal.FreeBSTR(language);
                Marshal.FreeBSTR(query);
            }

            if (hr != S_OK || result == IntPtr.Zero)
                return false;

            ApplyProxyBlanket(result);
            enumerator = result;
            return true;
        }

        private static WmiRow ReadRow(IntPtr instance)
        {
            var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            var begin = (delegate* unmanaged<IntPtr, int, int>)
                VtableSlot(instance, WbemClassObjectBeginEnumerationIndex);
            var next = (delegate* unmanaged<IntPtr, int, IntPtr*, VARIANT*, int*, int*, int>)
                VtableSlot(instance, WbemClassObjectNextIndex);
            var end = (delegate* unmanaged<IntPtr, int>)
                VtableSlot(instance, WbemClassObjectEndEnumerationIndex);

            if (begin(instance, 0) != S_OK)
                return new WmiRow(values);

            try
            {
                while (true)
                {
                    IntPtr nameBstr = IntPtr.Zero;
                    var variant = default(VARIANT);
                    int cimType = 0;
                    int flavor = 0;
                    int hr = next(instance, 0, &nameBstr, &variant, &cimType, &flavor);
                    if (hr != S_OK)
                        break;

                    try
                    {
                        var name = nameBstr == IntPtr.Zero ? null : Marshal.PtrToStringBSTR(nameBstr);
                        if (!string.IsNullOrEmpty(name))
                        {
                            var value = ToManaged(ref variant);
                            if (value != null)
                                values[name] = value;
                        }
                    }
                    finally
                    {
                        if (nameBstr != IntPtr.Zero) Marshal.FreeBSTR(nameBstr);
                        VariantClear((IntPtr)(&variant));
                    }
                }
            }
            finally
            {
                end(instance);
            }

            return new WmiRow(values);
        }

        private static object ToManaged(ref VARIANT value)
        {
            switch (value.vt)
            {
                case VT_EMPTY:
                case VT_NULL:
                    return null;
                case VT_I2:
                    return (int)value.iVal;
                case VT_I4:
                case VT_INT:
                    return value.lVal;
                case VT_R4:
                    return value.fltVal;
                case VT_R8:
                    return value.dblVal;
                case VT_BSTR:
                    return value.bstrVal == IntPtr.Zero ? null : Marshal.PtrToStringBSTR(value.bstrVal);
                case VT_BOOL:
                    return value.iVal != 0;
                case VT_I1:
                    return (int)(sbyte)value.bVal;
                case VT_UI1:
                    return (int)value.bVal;
                case VT_UI2:
                    return (int)(ushort)value.iVal;
                case VT_UI4:
                case VT_UINT:
                    return (long)value.ulVal;
                case VT_I8:
                    return value.llVal;
                case VT_UI8:
                    return value.ullVal;
                case VT_ARRAY | VT_UI2:
                    return ReadUInt16Array(value.parray);
                // WMI 把 CIM 的 uint16[]（如 WmiMonitorID.ManufacturerName）包成 SAFEARRAY(LONG)，
                // 每个元素仍是 0..0xFFFF 的字符码。
                case VT_ARRAY | VT_I4:
                    return ReadUInt16FromInt32Array(value.parray);
                case VT_ARRAY | VT_BSTR:
                    return ReadBstrArray(value.parray);
                default:
                    return null;
            }
        }

        private static ushort[] ReadUInt16Array(IntPtr safeArray)
        {
            if (safeArray == IntPtr.Zero)
                return null;

            int count = GetSafeArrayLength(safeArray);
            if (count <= 0)
                return null;

            if (SafeArrayAccessData(safeArray, out IntPtr data) != S_OK || data == IntPtr.Zero)
                return null;

            try
            {
                var result = new ushort[count];
                fixed (ushort* destination = result)
                {
                    Buffer.MemoryCopy((void*)data, destination, count * sizeof(ushort), count * sizeof(ushort));
                }
                return result;
            }
            finally
            {
                SafeArrayUnaccessData(safeArray);
            }
        }

        private static ushort[] ReadUInt16FromInt32Array(IntPtr safeArray)
        {
            if (safeArray == IntPtr.Zero)
                return null;

            int count = GetSafeArrayLength(safeArray);
            if (count <= 0)
                return null;

            if (SafeArrayAccessData(safeArray, out IntPtr data) != S_OK || data == IntPtr.Zero)
                return null;

            try
            {
                var result = new ushort[count];
                for (int i = 0; i < count; i++)
                    result[i] = (ushort)Marshal.ReadInt32(data, i * sizeof(int));
                return result;
            }
            finally
            {
                SafeArrayUnaccessData(safeArray);
            }
        }

        private static string[] ReadBstrArray(IntPtr safeArray)
        {
            if (safeArray == IntPtr.Zero)
                return null;

            int count = GetSafeArrayLength(safeArray);
            if (count <= 0)
                return null;

            if (SafeArrayAccessData(safeArray, out IntPtr data) != S_OK || data == IntPtr.Zero)
                return null;

            try
            {
                var result = new string[count];
                for (int i = 0; i < count; i++)
                {
                    IntPtr bstr = Marshal.ReadIntPtr(data, i * IntPtr.Size);
                    result[i] = bstr == IntPtr.Zero ? null : Marshal.PtrToStringBSTR(bstr);
                }
                return result;
            }
            finally
            {
                SafeArrayUnaccessData(safeArray);
            }
        }

        private static int GetSafeArrayLength(IntPtr safeArray)
        {
            if (SafeArrayGetLBound(safeArray, 1, out int lower) != S_OK)
                return 0;
            if (SafeArrayGetUBound(safeArray, 1, out int upper) != S_OK)
                return 0;

            return upper - lower + 1;
        }

        private static IntPtr VtableSlot(IntPtr instance, int index)
        {
            IntPtr vtable = Marshal.ReadIntPtr(instance);
            return Marshal.ReadIntPtr(vtable, index * IntPtr.Size);
        }

        private static int Release(IntPtr instance)
        {
            if (instance == IntPtr.Zero)
                return 0;

            var release = (delegate* unmanaged<IntPtr, int>)VtableSlot(instance, IUnknownReleaseIndex);
            return release(instance);
        }
    }
}
