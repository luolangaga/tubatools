using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Services;
using Windows.System;
using Windows.UI;

namespace TubaWinUi3.Controls;

/// <summary>
/// 可嵌入的键盘按键检测组件：按键后键位高亮，已按过的键留有浅色标记。
/// 挂载时安装全局低级键盘钩子，卸载时移除，便于直接嵌入任意页面。
/// </summary>
public sealed class KeyboardTestControl : UserControl
{
    // 按键高亮/已按标记/统计色取自 ThemeColors（跟随系统强调色与明暗主题），不再本地硬编码品牌色
    private static Color KeyPressed => ThemeColors.AccentBlue;
    private static Color KeyVisited => Color.FromArgb(60, KeyPressed.R, KeyPressed.G, KeyPressed.B);
    private static Color AccentGreen => ThemeColors.AccentGreen;

    private static readonly VirtualKey NumpadEnter = (VirtualKey)0xE01C;
    private static readonly VirtualKey CopilotKey = (VirtualKey)0xE07E;

    private readonly Dictionary<VirtualKey, Border> _keyMap = [];
    private readonly HashSet<VirtualKey> _visitedKeys = [];
    private readonly HashSet<VirtualKey> _heldKeys = [];
    private Grid? _keyGrid;
    private int _totalPressed;
    private bool _compactMode;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc? _hookProc;
    private Microsoft.UI.Dispatching.DispatcherQueue? _dispatcher;
    private TextBlock? _countText;
    private TextBlock? _lastKeyText;

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYUP = 0x0105;

    public KeyboardTestControl()
    {
        Content = BuildUi();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _dispatcher = DispatcherQueue;
        InstallHook();
        _keyGrid?.Focus(FocusState.Programmatic);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => Cleanup();

    /// <summary>移除键盘钩子。控件卸载时自动调用，也可由宿主显式调用（可重复调用）。</summary>
    public void Cleanup()
    {
        RemoveHook();
        _dispatcher = null;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _dispatcher != null)
        {
            int lParamValue = Marshal.ReadInt32(lParam);
            int vkCode = lParamValue & 0xFF;
            int scanCode = (lParamValue >> 16) & 0xFF;
            bool extended = ((lParamValue >> 24) & 1) != 0;
            var key = ResolveKey((VirtualKey)vkCode, scanCode, extended);
            bool isDown = wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN;
            bool isUp = wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP;

            if (isDown || isUp)
            {
                key = ResolveComboKey(key, isDown);
            }

            if (isDown)
            {
                _dispatcher.TryEnqueue(() =>
                {
                    HandleKeyDown(key);
                    _keyGrid?.Focus(FocusState.Programmatic);
                });
            }
            else if (isUp)
            {
                _dispatcher.TryEnqueue(() =>
                {
                    HandleKeyUp(key);
                });
            }

            if (ShouldBlockKey(key))
            {
                return (IntPtr)1;
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static VirtualKey ResolveKey(VirtualKey key, int scanCode, bool extended)
    {
        if (extended && scanCode == 0x7E) return CopilotKey;
        if (key == VirtualKey.Shift) return scanCode == 0x36 ? VirtualKey.RightShift : VirtualKey.LeftShift;
        if (key == VirtualKey.Control) return extended ? VirtualKey.RightControl : VirtualKey.LeftControl;
        if (key == VirtualKey.Menu) return extended ? VirtualKey.RightMenu : VirtualKey.LeftMenu;
        if (key == VirtualKey.Enter) return extended ? NumpadEnter : VirtualKey.Enter;
        return key;
    }

    private static bool IsWinKey(VirtualKey key) => key is VirtualKey.LeftWindows or VirtualKey.RightWindows;

    private static bool IsShiftKey(VirtualKey key) => key is VirtualKey.LeftShift or VirtualKey.RightShift;

    private VirtualKey ResolveComboKey(VirtualKey key, bool isDown)
    {
        if (IsWinKey(key) || IsShiftKey(key))
        {
            bool partnerHeld = IsWinKey(key)
                ? _heldKeys.Any(IsShiftKey)
                : _heldKeys.Any(IsWinKey);
            if (isDown)
            {
                _heldKeys.Add(key);
                if (partnerHeld)
                {
                    var partner = IsWinKey(key) ? _heldKeys.First(IsShiftKey) : _heldKeys.First(IsWinKey);
                    UndoKey(partner);
                    return CopilotKey;
                }
            }
            else
            {
                _heldKeys.Remove(key);
                if (partnerHeld) return CopilotKey;
            }
        }
        return key;
    }

    private void UndoKey(VirtualKey key)
    {
        if (_visitedKeys.Remove(key))
        {
            _totalPressed--;
            if (_countText is not null) _countText.Text = _totalPressed.ToString();
        }
        if (_keyMap.TryGetValue(key, out var border))
        {
            border.Background = new SolidColorBrush(ThemeColors.KeyDefault);
            var tb = FindTextBlock(border);
            if (tb is not null) tb.Foreground = new SolidColorBrush(ThemeColors.KeyText);
        }
    }

    private static bool ShouldBlockKey(VirtualKey key)
    {
        return key is VirtualKey.LeftWindows or VirtualKey.RightWindows
            or VirtualKey.LeftMenu or VirtualKey.RightMenu or VirtualKey.Menu
            or (VirtualKey)0xE07E
            or VirtualKey.F4 or VirtualKey.F6 or VirtualKey.F10
            or VirtualKey.Tab or VirtualKey.Escape;
    }

    private void InstallHook()
    {
        if (_hookId != IntPtr.Zero) return;
        _hookProc = HookCallback;
        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(curModule.ModuleName), 0);
    }

    private void RemoveHook()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
        _hookProc = null;
    }

    private void HandleKeyDown(VirtualKey key)
    {
        if (_keyMap.TryGetValue(key, out var keyBorder))
        {
            keyBorder.Background = new SolidColorBrush(KeyPressed);
            var tb = FindTextBlock(keyBorder);
            if (tb is not null) tb.Foreground = new SolidColorBrush(ThemeColors.PrimaryText);
        }
        if (_visitedKeys.Add(key))
        {
            _totalPressed++;
        }
        if (_countText is not null) _countText.Text = _totalPressed.ToString();
        if (_lastKeyText is not null) _lastKeyText.Text = KeyDisplayName(key);
    }

    private void HandleKeyUp(VirtualKey key)
    {
        if (_keyMap.TryGetValue(key, out var keyBorder))
        {
            if (_visitedKeys.Contains(key))
            {
                keyBorder.Background = new SolidColorBrush(KeyVisited);
                var tb = FindTextBlock(keyBorder);
                if (tb is not null) tb.Foreground = new SolidColorBrush(ThemeColors.KeyText);
            }
            else
            {
                keyBorder.Background = new SolidColorBrush(ThemeColors.KeyDefault);
            }
        }
    }

    private UIElement BuildUi()
    {
        var countText = new TextBlock
        {
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(AccentGreen),
            Text = "0"
        };
        _countText = countText;
        var lastKeyText = new TextBlock
        {
            FontSize = 14,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        };
        _lastKeyText = lastKeyText;

        var statsBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20 };
        statsBar.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                new FontIcon { Glyph = "\uE92E", FontSize = 14, Foreground = new SolidColorBrush(AccentGreen) },
                new TextBlock { Text = "已检测按键:", FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(ThemeColors.DimText) },
                countText
            }
        });
        statsBar.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = "最后按键:", FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(ThemeColors.DimText) },
                lastKeyText
            }
        });

        var keyGrid = new Grid
        {
            Background = new SolidColorBrush(ThemeColors.KeyboardBg),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            IsTabStop = true
        };
        _keyGrid = keyGrid;

        BuildFullKeyboardLayout(keyGrid);

        keyGrid.KeyDown += (s, e) =>
        {
            var key = ResolveComboKey(ResolveKey(e.Key, (int)e.KeyStatus.ScanCode, e.KeyStatus.IsExtendedKey), true);
            HandleKeyDown(key);
            e.Handled = true;
        };

        keyGrid.KeyUp += (s, e) =>
        {
            var key = ResolveComboKey(ResolveKey(e.Key, (int)e.KeyStatus.ScanCode, e.KeyStatus.IsExtendedKey), false);
            HandleKeyUp(key);
            e.Handled = true;
        };

        keyGrid.PointerPressed += (s, e) =>
        {
            keyGrid.Focus(FocusState.Programmatic);
            e.Handled = true;
        };

        var resetBtn = new Button
        {
            IsTabStop = false,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE72C", FontSize = 12 },
                    new TextBlock { Text = "重置" }
                }
            }
        };
        resetBtn.Click += (_, _) =>
        {
            ResetState();
            keyGrid.Focus(FocusState.Programmatic);
        };

        var modeSwitch = new ToggleSwitch
        {
            Header = "布局",
            OffContent = "大键盘",
            OnContent = "小键盘",
            IsTabStop = false,
            MinWidth = 140
        };
        modeSwitch.Toggled += (_, _) =>
        {
            _compactMode = modeSwitch.IsOn;
            RebuildLayout();
        };

        var actionBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        actionBar.Children.Add(statsBar);
        actionBar.Children.Add(resetBtn);
        actionBar.Children.Add(modeSwitch);

        var keyScroller = new ScrollViewer
        {
            Content = keyGrid,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Disabled,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var root = new StackPanel { Spacing = 14, HorizontalAlignment = HorizontalAlignment.Center };
        root.Children.Add(actionBar);
        root.Children.Add(keyScroller);

        return root;
    }

    private void ResetState()
    {
        _totalPressed = 0;
        _visitedKeys.Clear();
        if (_countText is not null) _countText.Text = "0";
        if (_lastKeyText is not null) _lastKeyText.Text = "";
        foreach (var border in _keyMap.Values)
        {
            border.Background = new SolidColorBrush(ThemeColors.KeyDefault);
            var tb = FindTextBlock(border);
            if (tb is not null) tb.Foreground = new SolidColorBrush(ThemeColors.KeyText);
        }
    }

    private void RebuildLayout()
    {
        if (_keyGrid is null) return;
        ResetState();
        _keyGrid.Children.Clear();
        _keyGrid.RowDefinitions.Clear();
        _keyGrid.ColumnDefinitions.Clear();
        if (_compactMode)
        {
            BuildTklLayout(_keyGrid);
        }
        else
        {
            BuildFullKeyboardLayout(_keyGrid);
        }
        _keyGrid.Focus(FocusState.Programmatic);
    }

    private void BuildFullKeyboardLayout(Grid rootGrid)
    {
        _keyMap.Clear();
        var main = BuildMainRows();

        var numpad = BuildNumpadGrid(44, 42, 4);
        numpad.Margin = new Thickness(0, 46, 0, 4);

        var host = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            VerticalAlignment = VerticalAlignment.Top
        };
        host.Children.Add(main);
        host.Children.Add(numpad);
        rootGrid.Children.Add(host);
    }

    private void BuildTklLayout(Grid rootGrid)
    {
        _keyMap.Clear();
        var main = BuildMainRows();
        main.HorizontalAlignment = HorizontalAlignment.Center;
        rootGrid.Children.Add(main);
    }

    private StackPanel BuildMainRows()
    {
        var main = new StackPanel { Spacing = 4 };
        foreach (var row in GetKeyboardRows())
        {
            var rowPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            foreach (var def in row)
            {
                if (def.Key is null)
                {
                    rowPanel.Children.Add(new Border { Width = def.Width, Height = 42 });
                    continue;
                }
                var keyBorder = MakeKeyBorder(def.Label, def.Width, 42);
                _keyMap[def.Key.Value] = keyBorder;
                rowPanel.Children.Add(keyBorder);
            }
            main.Children.Add(rowPanel);
        }
        return main;
    }

    private Grid BuildNumpadGrid(double keyWidth, double keyHeight, double gap)
    {
        var grid = new Grid
        {
            ColumnSpacing = gap,
            RowSpacing = gap
        };
        for (int i = 0; i < 4; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(keyWidth) });
        }
        for (int i = 0; i < 5; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(keyHeight) });
        }

        void Add(VirtualKey key, string label, int row, int col, int rowSpan = 1, int colSpan = 1)
        {
            var border = MakeKeyBorder(
                label,
                keyWidth * colSpan + gap * (colSpan - 1),
                keyHeight * rowSpan + gap * (rowSpan - 1));
            Grid.SetRow(border, row);
            Grid.SetColumn(border, col);
            Grid.SetRowSpan(border, rowSpan);
            Grid.SetColumnSpan(border, colSpan);
            grid.Children.Add(border);
            _keyMap[key] = border;
        }

        Add(VirtualKey.NumberKeyLock, "NumLk", 0, 0);
        Add(VirtualKey.Divide, "/", 0, 1);
        Add(VirtualKey.Multiply, "*", 0, 2);
        Add(VirtualKey.Subtract, "-", 0, 3);
        Add(VirtualKey.NumberPad7, "7", 1, 0);
        Add(VirtualKey.NumberPad8, "8", 1, 1);
        Add(VirtualKey.NumberPad9, "9", 1, 2);
        Add(VirtualKey.Add, "+", 1, 3, rowSpan: 2);
        Add(VirtualKey.NumberPad4, "4", 2, 0);
        Add(VirtualKey.NumberPad5, "5", 2, 1);
        Add(VirtualKey.NumberPad6, "6", 2, 2);
        Add(VirtualKey.NumberPad1, "1", 3, 0);
        Add(VirtualKey.NumberPad2, "2", 3, 1);
        Add(VirtualKey.NumberPad3, "3", 3, 2);
        Add(NumpadEnter, "Enter", 3, 3, rowSpan: 2);
        Add(VirtualKey.NumberPad0, "0", 4, 0, colSpan: 2);
        Add(VirtualKey.Decimal, ".", 4, 2);

        return grid;
    }

    private Border MakeKeyBorder(string label, double width, double height)
    {
        // 键帽字号只允许 12 / 14：长标签（F1、Backspace 等）用 12，短标签高键帽用 14
        double fontSize = label.Length > 3 ? 12 : (height > 44 ? 14 : 12);
        var text = new TextBlock
        {
            Text = label,
            FontSize = fontSize,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.KeyText),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };

        return new Border
        {
            Width = width,
            Height = height,
            Background = new SolidColorBrush(ThemeColors.KeyDefault),
            BorderBrush = new SolidColorBrush(ThemeColors.KeyBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = text
        };
    }

    private static TextBlock? FindTextBlock(Border border)
    {
        return border.Child as TextBlock;
    }

    private static string KeyDisplayName(VirtualKey key)
    {
        return key switch
        {
            VirtualKey.Space => "Space",
            VirtualKey.Enter => "Enter",
            (VirtualKey)0xE01C => "NumEnter",
            (VirtualKey)0xE07E => "Copilot",
            VirtualKey.Back => "Backspace",
            VirtualKey.Tab => "Tab",
            VirtualKey.CapitalLock => "CapsLock",
            VirtualKey.Shift => "Shift",
            VirtualKey.LeftShift => "LShift",
            VirtualKey.RightShift => "RShift",
            VirtualKey.Control => "Ctrl",
            VirtualKey.LeftControl => "LCtrl",
            VirtualKey.RightControl => "RCtrl",
            VirtualKey.Menu => "Alt",
            VirtualKey.LeftMenu => "LAlt",
            VirtualKey.RightMenu => "RAlt",
            VirtualKey.LeftWindows => "Win",
            VirtualKey.RightWindows => "Win",
            VirtualKey.Escape => "Esc",
            VirtualKey.Delete => "Delete",
            VirtualKey.Insert => "Insert",
            VirtualKey.Home => "Home",
            VirtualKey.End => "End",
            VirtualKey.PageUp => "PgUp",
            VirtualKey.PageDown => "PgDn",
            VirtualKey.Left => "←",
            VirtualKey.Right => "→",
            VirtualKey.Up => "↑",
            VirtualKey.Down => "↓",
            VirtualKey.Number0 => "0",
            VirtualKey.Number1 => "1",
            VirtualKey.Number2 => "2",
            VirtualKey.Number3 => "3",
            VirtualKey.Number4 => "4",
            VirtualKey.Number5 => "5",
            VirtualKey.Number6 => "6",
            VirtualKey.Number7 => "7",
            VirtualKey.Number8 => "8",
            VirtualKey.Number9 => "9",
            VirtualKey.NumberPad0 => "Num0",
            VirtualKey.NumberPad1 => "Num1",
            VirtualKey.NumberPad2 => "Num2",
            VirtualKey.NumberPad3 => "Num3",
            VirtualKey.NumberPad4 => "Num4",
            VirtualKey.NumberPad5 => "Num5",
            VirtualKey.NumberPad6 => "Num6",
            VirtualKey.NumberPad7 => "Num7",
            VirtualKey.NumberPad8 => "Num8",
            VirtualKey.NumberPad9 => "Num9",
            VirtualKey.Snapshot => "PrtSc",
            VirtualKey.Scroll => "ScrLk",
            VirtualKey.Pause => "Pause",
            VirtualKey.F1 => "F1",
            VirtualKey.F2 => "F2",
            VirtualKey.F3 => "F3",
            VirtualKey.F4 => "F4",
            VirtualKey.F5 => "F5",
            VirtualKey.F6 => "F6",
            VirtualKey.F7 => "F7",
            VirtualKey.F8 => "F8",
            VirtualKey.F9 => "F9",
            VirtualKey.F10 => "F10",
            VirtualKey.F11 => "F11",
            VirtualKey.F12 => "F12",
            VirtualKey.Multiply => "Num*",
            VirtualKey.Add => "Num+",
            VirtualKey.Subtract => "Num-",
            VirtualKey.Divide => "Num/",
            VirtualKey.Decimal => "Num.",
            VirtualKey.NumberKeyLock => "NumLk",
            VirtualKey.Application => "Menu",
            (VirtualKey)0xBA => ";",
            (VirtualKey)0xBB => "=",
            (VirtualKey)0xBC => ",",
            (VirtualKey)0xBD => "-",
            (VirtualKey)0xBE => ".",
            (VirtualKey)0xBF => "/",
            (VirtualKey)0xC0 => "`",
            (VirtualKey)0xDB => "[",
            (VirtualKey)0xDC => "\\",
            (VirtualKey)0xDD => "]",
            (VirtualKey)0xDE => "'",
            _ => key.ToString()
        };
    }

    private sealed record KeyDef(VirtualKey? Key, string Label, double Width);

    private static List<List<KeyDef>> GetKeyboardRows()
    {
        var rows = new List<List<KeyDef>>();

        rows.Add([
            new KeyDef(VirtualKey.Escape, "Esc", 40),
            new KeyDef(null, "", 16),
            new KeyDef(VirtualKey.F1, "F1", 40), new KeyDef(VirtualKey.F2, "F2", 40),
            new KeyDef(VirtualKey.F3, "F3", 40), new KeyDef(VirtualKey.F4, "F4", 40),
            new KeyDef(VirtualKey.F5, "F5", 40), new KeyDef(VirtualKey.F6, "F6", 40),
            new KeyDef(VirtualKey.F7, "F7", 40), new KeyDef(VirtualKey.F8, "F8", 40),
            new KeyDef(VirtualKey.F9, "F9", 40), new KeyDef(VirtualKey.F10, "F10", 40),
            new KeyDef(VirtualKey.F11, "F11", 40), new KeyDef(VirtualKey.F12, "F12", 40),
            new KeyDef(null, "", 16),
            new KeyDef(VirtualKey.Snapshot, "PrtSc", 40), new KeyDef(VirtualKey.Scroll, "ScrLk", 40),
            new KeyDef(VirtualKey.Pause, "Pause", 40),
        ]);

        rows.Add([
            new KeyDef((VirtualKey)0xC0, "`", 40),
            new KeyDef(VirtualKey.Number1, "1", 40), new KeyDef(VirtualKey.Number2, "2", 40),
            new KeyDef(VirtualKey.Number3, "3", 40), new KeyDef(VirtualKey.Number4, "4", 40),
            new KeyDef(VirtualKey.Number5, "5", 40), new KeyDef(VirtualKey.Number6, "6", 40),
            new KeyDef(VirtualKey.Number7, "7", 40), new KeyDef(VirtualKey.Number8, "8", 40),
            new KeyDef(VirtualKey.Number9, "9", 40), new KeyDef(VirtualKey.Number0, "0", 40),
            new KeyDef((VirtualKey)0xBD, "-", 40), new KeyDef((VirtualKey)0xBB, "=", 40),
            new KeyDef(VirtualKey.Back, "⌫", 80),
            new KeyDef(VirtualKey.Insert, "Ins", 40), new KeyDef(VirtualKey.Home, "Home", 40),
            new KeyDef(VirtualKey.PageUp, "PgUp", 40),
        ]);

        rows.Add([
            new KeyDef(VirtualKey.Tab, "Tab", 60),
            new KeyDef(VirtualKey.Q, "Q", 40), new KeyDef(VirtualKey.W, "W", 40),
            new KeyDef(VirtualKey.E, "E", 40), new KeyDef(VirtualKey.R, "R", 40),
            new KeyDef(VirtualKey.T, "T", 40), new KeyDef(VirtualKey.Y, "Y", 40),
            new KeyDef(VirtualKey.U, "U", 40), new KeyDef(VirtualKey.I, "I", 40),
            new KeyDef(VirtualKey.O, "O", 40), new KeyDef(VirtualKey.P, "P", 40),
            new KeyDef((VirtualKey)0xDB, "[", 40), new KeyDef((VirtualKey)0xDD, "]", 40),
            new KeyDef((VirtualKey)0xDC, "\\", 60),
            new KeyDef(VirtualKey.Delete, "Del", 40), new KeyDef(VirtualKey.End, "End", 40),
            new KeyDef(VirtualKey.PageDown, "PgDn", 40),
        ]);

        rows.Add([
            new KeyDef(VirtualKey.CapitalLock, "Caps", 70),
            new KeyDef(VirtualKey.A, "A", 40), new KeyDef(VirtualKey.S, "S", 40),
            new KeyDef(VirtualKey.D, "D", 40), new KeyDef(VirtualKey.F, "F", 40),
            new KeyDef(VirtualKey.G, "G", 40), new KeyDef(VirtualKey.H, "H", 40),
            new KeyDef(VirtualKey.J, "J", 40), new KeyDef(VirtualKey.K, "K", 40),
            new KeyDef(VirtualKey.L, "L", 40),
            new KeyDef((VirtualKey)0xBA, ";", 40), new KeyDef((VirtualKey)0xDE, "'", 40),
            new KeyDef(VirtualKey.Enter, "Enter", 90),
        ]);

        rows.Add([
            new KeyDef(VirtualKey.LeftShift, "Shift", 90),
            new KeyDef(VirtualKey.Z, "Z", 40), new KeyDef(VirtualKey.X, "X", 40),
            new KeyDef(VirtualKey.C, "C", 40), new KeyDef(VirtualKey.V, "V", 40),
            new KeyDef(VirtualKey.B, "B", 40), new KeyDef(VirtualKey.N, "N", 40),
            new KeyDef(VirtualKey.M, "M", 40),
            new KeyDef((VirtualKey)0xBC, ",", 40), new KeyDef((VirtualKey)0xBE, ".", 40),
            new KeyDef((VirtualKey)0xBF, "/", 40),
            new KeyDef(VirtualKey.RightShift, "Shift", 110),
            new KeyDef(VirtualKey.Up, "↑", 40),
        ]);

        rows.Add([
            new KeyDef(VirtualKey.LeftControl, "Ctrl", 60),
            new KeyDef(VirtualKey.LeftWindows, "Win", 50),
            new KeyDef(VirtualKey.LeftMenu, "Alt", 50),
            new KeyDef(VirtualKey.Space, "Space", 250),
            new KeyDef(VirtualKey.RightMenu, "Alt", 50),
            new KeyDef(CopilotKey, "Copilot", 50),
            new KeyDef(VirtualKey.RightControl, "Ctrl", 60),
            new KeyDef(VirtualKey.Left, "←", 40), new KeyDef(VirtualKey.Down, "↓", 40),
            new KeyDef(VirtualKey.Right, "→", 40),
        ]);

        return rows;
    }
}
