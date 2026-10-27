using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TubaWinUi3.Services.SpaceCleanup;

public static class SpaceFileOperations
{
    public static string ValidateTarget(string scanRoot, string target)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(scanRoot));
        string path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
        string prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, Path.GetPathRoot(path), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("不能删除扫描根目录、磁盘根目录或扫描范围以外的项目。");
        // Recheck ancestors immediately before executing, not just at scan time.
        // This also prevents traversal through a directory swapped for a junction after scanning.
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("该路径包含链接或挂载点，请在资源管理器中检查实际目标。");
            if (string.Equals(Path.TrimEndingDirectorySeparator(current), root, StringComparison.OrdinalIgnoreCase)) break;
        }
        return path;
    }

    public static Task DeleteAsync(string scanRoot, string target, bool recycle, IntPtr owner)
        => RunSta(() =>
        {
            string path = ValidateTarget(scanRoot, target);
            object? operationObject = null; IShellItem? item = null;
            try
            {
                operationObject = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("3ad05575-8857-4850-9277-11b85bdb8e09"), true)!);
                var operation = (IFileOperation)operationObject!;
                Marshal.ThrowExceptionForHR(operation.SetOwnerWindow(owner));
                // Keep native error/"too large for recycle bin" confirmations visible. Never silently fall back to permanent deletion.
                uint flags = recycle ? 0x00080000u | 0x20000000u | 0x00004000u : 0x00000010u;
                Marshal.ThrowExceptionForHR(operation.SetOperationFlags(flags));
                var iid = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");
                Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out item));
                Marshal.ThrowExceptionForHR(operation.DeleteItem(item, IntPtr.Zero));
                Marshal.ThrowExceptionForHR(operation.PerformOperations());
                Marshal.ThrowExceptionForHR(operation.GetAnyOperationsAborted(out bool aborted));
                if (aborted || File.Exists(path) || Directory.Exists(path)) throw new IOException("操作已取消或部分项目未能删除，请刷新后检查。");
            }
            finally
            {
                if (item is not null && Marshal.IsComObject(item)) Marshal.FinalReleaseComObject(item);
                if (operationObject is not null && Marshal.IsComObject(operationObject)) Marshal.FinalReleaseComObject(operationObject);
            }
        });

    public static void Reveal(string path)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = false };
        // Explorer's /select syntax needs the comma and quoted path in the same command line.
        start.Arguments = $"/select,\"{Path.GetFullPath(path).Replace("\"", "")}\"";
        Process.Start(start);
    }

    internal static Task RunSta(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { action(); completion.TrySetResult(); } catch (Exception ex) { completion.TrySetException(ex); } })
        { IsBackground = true, Name = "SpaceCleanup.Shell" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bind, ref Guid iid, out IShellItem item);

    // A typed IShellItem is required: marshaling an object as Interface passes IUnknown,
    // which is not the vtable DeleteItem expects and can crash inside the shell.
    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        [PreserveSig] int BindToHandler(IntPtr context, ref Guid handler, ref Guid iid, out IntPtr result);
        [PreserveSig] int GetParent(out IShellItem parent);
        [PreserveSig] int GetDisplayName(uint kind, out IntPtr name);
        [PreserveSig] int GetAttributes(uint mask, out uint attributes);
        [PreserveSig] int Compare(IShellItem other, uint hint, out int order);
    }

    // Full vtable in SDK order; unused slots must not be removed.
    [ComImport, Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        [PreserveSig] int Advise(IntPtr sink, out uint cookie);
        [PreserveSig] int Unadvise(uint cookie);
        [PreserveSig] int SetOperationFlags(uint flags);
        [PreserveSig] int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        [PreserveSig] int SetProgressDialog(IntPtr dialog);
        [PreserveSig] int SetProperties(IntPtr properties);
        [PreserveSig] int SetOwnerWindow(IntPtr owner);
        [PreserveSig] int ApplyPropertiesToItem(IntPtr item);
        [PreserveSig] int ApplyPropertiesToItems(IntPtr items);
        [PreserveSig] int RenameItem(IntPtr item, IntPtr name, IntPtr sink);
        [PreserveSig] int RenameItems(IntPtr items, IntPtr name);
        [PreserveSig] int MoveItem(IntPtr item, IntPtr destination, IntPtr name, IntPtr sink);
        [PreserveSig] int MoveItems(IntPtr items, IntPtr destination);
        [PreserveSig] int CopyItem(IntPtr item, IntPtr destination, IntPtr name, IntPtr sink);
        [PreserveSig] int CopyItems(IntPtr items, IntPtr destination);
        [PreserveSig] int DeleteItem(IShellItem item, IntPtr sink);
        [PreserveSig] int DeleteItems(IntPtr items);
        [PreserveSig] int NewItem(IntPtr destination, uint attributes, IntPtr name, IntPtr template, IntPtr sink);
        [PreserveSig] int PerformOperations();
        [PreserveSig] int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }
}
