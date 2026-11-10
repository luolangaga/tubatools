using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ComputerUse.Windows.Runtime;

internal sealed class WindowLifecycleTracker : IDisposable
{
    private const uint EventObjectCreate = 0x8000;
    private const uint EventObjectDestroy = 0x8001;
    private const int ObjectIdWindow = 0;
    private const int ChildIdSelf = 0;
    private const uint WinEventOutOfContext = 0;
    private const uint WmQuit = 0x0012;

    private readonly object _gate = new();
    private readonly Dictionary<nint, long> _generations = new();
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly WinEventDelegate _callback;
    private readonly Thread _thread;
    private Exception? _startupFailure;
    private nint _hook;
    private uint _threadId;
    private bool _disposed;

    internal WindowLifecycleTracker()
    {
        _callback = OnWinEvent;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "computer-use-window-lifecycle",
        };
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(5)))
        {
            throw new InvalidOperationException(
                "Timed out while starting the native window lifecycle tracker.");
        }
        if (_startupFailure is not null)
        {
            throw new InvalidOperationException(
                "Failed to start the native window lifecycle tracker.",
                _startupFailure);
        }
    }

    internal long Generation(nint handle)
    {
        lock (_gate)
        {
            return _generations.TryGetValue(handle, out var generation)
                ? generation
                : 0;
        }
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        _hook = SetWinEventHook(
            EventObjectCreate,
            EventObjectDestroy,
            0,
            _callback,
            0,
            0,
            WinEventOutOfContext);
        if (_hook == 0)
        {
            _startupFailure = new Win32Exception(Marshal.GetLastWin32Error());
            _ready.Set();
            return;
        }
        _ready.Set();
        while (GetMessage(out _, 0, 0, 0) > 0)
        {
        }
        _ = UnhookWinEvent(_hook);
        _hook = 0;
    }

    private void OnWinEvent(
        nint hook,
        uint eventType,
        nint handle,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        _ = hook;
        _ = eventType;
        _ = eventThread;
        _ = eventTime;
        if (handle == 0 ||
            objectId != ObjectIdWindow ||
            childId != ChildIdSelf)
        {
            return;
        }
        lock (_gate)
        {
            _generations.TryGetValue(handle, out var generation);
            _generations[handle] = checked(generation + 1);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        if (_threadId != 0 && _thread.IsAlive)
        {
            _ = PostThreadMessage(_threadId, WmQuit, 0, 0);
            _ = _thread.Join(TimeSpan.FromSeconds(2));
        }
        _ready.Dispose();
    }

    private delegate void WinEventDelegate(
        nint hook,
        uint eventType,
        nint handle,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        internal nint Handle;
        internal uint Value;
        internal nuint WParam;
        internal nint LParam;
        internal uint Time;
        internal int PointX;
        internal int PointY;
        internal uint Private;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint module,
        WinEventDelegate callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(nint hook);

    [DllImport("user32.dll")]
    private static extern int GetMessage(
        out Message message,
        nint window,
        uint minimum,
        uint maximum);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(
        uint threadId,
        uint message,
        nuint wParam,
        nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
