using System.Runtime.InteropServices;
using System.Text;
using Windows.Storage.Pickers;

namespace TubaWinUi3.Services;

/// <summary>Win32 传统文件对话框（GetOpenFileName / GetSaveFileName），无需 WinRT 权限。</summary>
public static class Win32Dialogs
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OPENFILENAME
    {
        public int lStructSize; public IntPtr hwndOwner; public IntPtr hInstance;
        public string lpstrFilter; public string lpstrCustomFilter; public int nMaxCustFilter;
        public int nFilterIndex; public string lpstrFile; public int nMaxFile;
        public string lpstrFileTitle; public int nMaxFileTitle; public string lpstrInitialDir;
        public string lpstrTitle; public int Flags; public short nFileOffset;
        public short nFileExtension; public string lpstrDefExt; public IntPtr lCustData;
        public IntPtr lpfnHook; public string lpTemplateName; public IntPtr pvReserved;
        public int dwReserved; public int FlagsEx;
    }

    [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetOpenFileName(ref OPENFILENAME ofn);

    [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetSaveFileName(ref OPENFILENAME ofn);

    private const int OFN_FILEMUSTEXIST = 0x1000;
    private const int OFN_NOCHANGEDIR = 8;
    private const int OFN_OVERWRITEPROMPT = 2;
    private const int OFN_ALLOWMULTISELECT = 0x200;
    private const int OFN_EXPLORER = 0x80000;

    /// <summary>
    /// nFilterIndex 的合法取值从 1 开始（第 1 组过滤器）。传 0 时对话框的「文件类型」
    /// 下拉没有选中项，文件列表会是**空的**——表现就是「一个文件都选不了」。
    /// </summary>
    private const int FirstFilterIndex = 1;

    private static IntPtr Hwnd() => WinRT.Interop.WindowNative.GetWindowHandle(TubaWinUi3.App.MainWindow!);

    /// <summary>打开文件选择（filter 格式: "名称\0*.ext;*.ext2\0所有文件\0*.*\0\0"）。</summary>
    public static string? PickOpen(string filter, string title)
    {
        var ofn = new OPENFILENAME
        {
            lStructSize = Marshal.SizeOf<OPENFILENAME>(),
            hwndOwner = Hwnd(),
            lpstrFilter = filter,
            // nFilterIndex 必须 >= 1：传 0 时「文件类型」下拉没有选中项，文件列表会是空的
            // （什么都点不了，看起来像「无法选择文件」）。
            nFilterIndex = FirstFilterIndex,
            lpstrFile = new string(new char[1024]),
            nMaxFile = 1024,
            lpstrTitle = title,
            Flags = OFN_FILEMUSTEXIST | OFN_NOCHANGEDIR
        };
        return GetOpenFileName(ref ofn) ? ofn.lpstrFile.TrimEnd('\0') : null;
    }

    /// <summary>多选文件：返回完整路径列表（未选择/取消时为空列表）。</summary>
    public static IReadOnlyList<string> PickOpenMultiple(string filter, string title)
    {
        var ofn = new OPENFILENAME
        {
            lStructSize = Marshal.SizeOf<OPENFILENAME>(),
            hwndOwner = Hwnd(),
            lpstrFilter = filter,
            nFilterIndex = FirstFilterIndex,
            lpstrFile = new string(new char[32768]),
            nMaxFile = 32768,
            lpstrTitle = title,
            Flags = OFN_FILEMUSTEXIST | OFN_NOCHANGEDIR | OFN_ALLOWMULTISELECT | OFN_EXPLORER
        };
        if (!GetOpenFileName(ref ofn)) return [];
        var parts = ofn.lpstrFile.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return [];
        if (parts.Length == 1) return [parts[0]];
        // 多选时第一个字段是目录，其余是文件名
        var dir = parts[0];
        return parts.Skip(1).Select(f => Path.Combine(dir, f)).ToList();
    }

    /// <summary>另存为对话框，返回完整路径。</summary>
    public static string? PickSave(string filter, string defExt, string? initialName = null)
    {
        var ofn = new OPENFILENAME
        {
            lStructSize = Marshal.SizeOf<OPENFILENAME>(),
            hwndOwner = Hwnd(),
            lpstrFilter = filter,
            nFilterIndex = FirstFilterIndex,
            lpstrFile = (initialName ?? "").PadRight(1024, '\0'),
            nMaxFile = 1024,
            lpstrTitle = "选择输出位置",
            lpstrDefExt = defExt,
            Flags = OFN_OVERWRITEPROMPT | OFN_NOCHANGEDIR
        };
        return GetSaveFileName(ref ofn) ? ofn.lpstrFile.TrimEnd('\0') : null;
    }

    /// <summary>选择文件夹：优先 WinRT 原生选择器（InitializeWithWindow），失败时回退 Win32 浏览对话框。
    /// 必须异步等待——同步阻塞 UI 线程会让消息泵停转，选择器永远不会返回。</summary>
    public static async Task<string?> PickFolderAsync(string? title = null)
    {
        try
        {
            var picker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.ComputerFolder
            };
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, Hwnd());
            var folder = await picker.PickSingleFolderAsync();
            return folder?.Path;
        }
        catch
        {
            return BrowseForFolder(title);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BROWSEINFO
    {
        public IntPtr hwndOwner;
        public IntPtr pidlRoot;
        public string pszDisplayName;
        public string lpszTitle;
        public int ulFlags;
        public IntPtr lpfn;
        public IntPtr lParam;
        public int iImage;
    }

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SHBrowseForFolder(ref BROWSEINFO lpbi);

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SHGetPathFromIDList(IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszPath);

    private const int BIF_RETURNONLYFSDIRS = 0x0001;
    private const int BIF_NEWDIALOGSTYLE = 0x0040;

    private static string? BrowseForFolder(string? title = null)
    {
        var bi = new BROWSEINFO
        {
            hwndOwner = Hwnd(),
            lpszTitle = string.IsNullOrWhiteSpace(title) ? "选择目标文件夹" : title,
            ulFlags = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE,
            pszDisplayName = new string(new char[260])
        };
        var pidl = SHBrowseForFolder(ref bi);
        if (pidl == IntPtr.Zero) return null;
        var path = new StringBuilder(260);
        SHGetPathFromIDList(pidl, path);
        return path.Length > 0 ? path.ToString() : null;
    }
}