using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MediaBrushes = System.Windows.Media.Brushes;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace ScreenshotQ
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static readonly (string Key, string Label, string DefaultGesture)[] ShortcutDefaults =
        {
            ("takeScreenshot", "Take Screenshot (Global)", "Alt+Shift+A"),
            ("undo", "Undo", "Ctrl+Z"),
            ("save", "Save", "Ctrl+S"),
            ("copy", "Copy", "Ctrl+C"),
            ("delete", "Delete", "Delete"),
            ("cancel", "Cancel", "Escape"),
            ("toolRect", "Tool Rectangle", "R"),
            ("toolEllipse", "Tool Ellipse", "E"),
            ("toolArrow", "Tool Arrow", "A"),
            ("toolText", "Tool Text", "T"),
            ("toolNumber", "Tool Number", "N"),
        };

        private const int HotkeyId = 9001;

        [Flags]
        private enum HotKeyModifiers : uint
        {
            None = 0x0000,
            Alt = 0x0001,
            Control = 0x0002,
            Shift = 0x0004,
            Win = 0x0008
        }

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private readonly string _outputFolder;
        private readonly Dictionary<string, (Key Key, ModifierKeys Modifiers)> _shortcuts = new();
        private readonly Forms.NotifyIcon _trayIcon;
        private bool _isExplicitExit;
        private bool _closeHintShown;

        public MainWindow()
        {
            InitializeComponent();

            _outputFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "ScreenshotQ");

            Directory.CreateDirectory(_outputFolder);
            InitializeDefaultShortcuts();
            _trayIcon = CreateTrayIcon();

            SourceInitialized += (_, _) =>
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
                RegisterOrUpdateScreenshotHotkey();
            };

            Closing += MainWindow_Closing;

            Closed += (_, _) =>
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                UnregisterHotKey(hwnd, HotkeyId);
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            };
        }

        private Forms.NotifyIcon CreateTrayIcon()
        {
            Forms.ContextMenuStrip menu = new();
            menu.Items.Add("Open", null, (_, _) => ShowMainWindowFromTray());
            menu.Items.Add("Take Screenshot", null, (_, _) => _ = Dispatcher.InvokeAsync(() => ScreenshotButton_Click(this, new RoutedEventArgs())));
            menu.Items.Add("Exit", null, (_, _) => ExitFromTray());

            System.Drawing.Icon icon = CreateTrayIconFromAsset() ?? System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? string.Empty) ?? SystemIcons.Application;

            Forms.NotifyIcon notifyIcon = new()
            {
                Icon = icon,
                Text = "ScreenshotQ",
                Visible = true,
                ContextMenuStrip = menu
            };

            notifyIcon.DoubleClick += (_, _) => ShowMainWindowFromTray();
            return notifyIcon;
        }

        private static System.Drawing.Icon? CreateTrayIconFromAsset()
        {
            string assetPath = Path.Combine(AppContext.BaseDirectory, "Assets", "5172910.png");
            if (!File.Exists(assetPath))
            {
                return null;
            }

            using Bitmap bitmap = new(assetPath);
            IntPtr hIcon = bitmap.GetHicon();
            try
            {
                using System.Drawing.Icon temp = System.Drawing.Icon.FromHandle(hIcon);
                return (System.Drawing.Icon)temp.Clone();
            }
            finally
            {
                _ = DestroyIcon(hIcon);
            }
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isExplicitExit)
            {
                return;
            }

            e.Cancel = true;
            HideToTray();
        }

        private void HideToTray()
        {
            ShowInTaskbar = false;
            Hide();

            if (_closeHintShown)
            {
                return;
            }

            _trayIcon.ShowBalloonTip(
                2000,
                "ScreenshotQ is running",
                "The app is still active in system tray. Right-click tray icon to exit.",
                Forms.ToolTipIcon.Info);
            _closeHintShown = true;
        }

        private void ShowMainWindowFromTray()
        {
            if (!Dispatcher.CheckAccess())
            {
                _ = Dispatcher.InvokeAsync(ShowMainWindowFromTray);
                return;
            }

            ShowInTaskbar = true;
            if (!IsVisible)
            {
                Show();
            }

            WindowState = WindowState.Normal;
            Activate();
        }

        private void ExitFromTray()
        {
            if (!Dispatcher.CheckAccess())
            {
                _ = Dispatcher.InvokeAsync(ExitFromTray);
                return;
            }

            _isExplicitExit = true;
            Close();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId && ScreenshotButton.IsEnabled)
            {
                ScreenshotButton_Click(this, new RoutedEventArgs());
                handled = true;
            }
            return IntPtr.Zero;
        }

        private bool RegisterOrUpdateScreenshotHotkey()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            UnregisterHotKey(hwnd, HotkeyId);

            if (!_shortcuts.TryGetValue("takeScreenshot", out (Key Key, ModifierKeys Modifiers) shotKey))
            {
                return false;
            }

            int vk = KeyInterop.VirtualKeyFromKey(shotKey.Key);
            if (vk == 0)
            {
                return false;
            }

            HotKeyModifiers mods = HotKeyModifiers.None;
            if ((shotKey.Modifiers & ModifierKeys.Alt) != 0) mods |= HotKeyModifiers.Alt;
            if ((shotKey.Modifiers & ModifierKeys.Control) != 0) mods |= HotKeyModifiers.Control;
            if ((shotKey.Modifiers & ModifierKeys.Shift) != 0) mods |= HotKeyModifiers.Shift;
            if ((shotKey.Modifiers & ModifierKeys.Windows) != 0) mods |= HotKeyModifiers.Win;

            return RegisterHotKey(hwnd, HotkeyId, (uint)mods, (uint)vk);
        }

        private void InitializeDefaultShortcuts()
        {
            _shortcuts.Clear();
            foreach ((string key, _, string defaultGesture) in ShortcutDefaults)
            {
                if (TryParseKeyBinding(defaultGesture, out Key k, out ModifierKeys mods))
                    _shortcuts[key] = (k, mods);
            }
        }

        private void ShortcutsButton_Click(object sender, RoutedEventArgs e)
        {
            Window dialog = new()
            {
                Title = "Shortcut Settings",
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Width = 420,
                Height = 500,
                Background = MediaBrushes.White,
            };

            Grid layout = new();
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            ScrollViewer scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(12, 12, 12, 8) };
            Grid form = new();
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });

            Dictionary<string, TextBox> editors = new();

            for (int i = 0; i < ShortcutDefaults.Length; i++)
            {
                (string key, string label, string _) = ShortcutDefaults[i];
                form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                TextBlock labelBlock = new()
                {
                    Text = label,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 8)
                };

                TextBox editor = new()
                {
                    Margin = new Thickness(0, 0, 0, 8),
                    Text = _shortcuts.TryGetValue(key, out (Key Key, ModifierKeys Modifiers) existing)
                        ? FormatKeyBinding(existing.Key, existing.Modifiers)
                        : string.Empty,
                    ToolTip = "Press key combination here. Backspace/Delete to clear."
                };

                editor.PreviewKeyDown += (s, ev) =>
                {
                    Key pressed = ev.Key == Key.System ? ev.SystemKey : ev.Key;
                    if (pressed is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift)
                    {
                        ev.Handled = true;
                        return;
                    }
                    if (pressed is Key.Back or Key.Delete)
                    {
                        editor.Text = string.Empty;
                        ev.Handled = true;
                        return;
                    }
                    editor.Text = FormatKeyBinding(pressed, Keyboard.Modifiers);
                    ev.Handled = true;
                };

                Grid.SetRow(labelBlock, i);
                Grid.SetColumn(labelBlock, 0);
                Grid.SetRow(editor, i);
                Grid.SetColumn(editor, 1);
                form.Children.Add(labelBlock);
                form.Children.Add(editor);
                editors[key] = editor;
            }

            scroll.Content = form;
            Grid.SetRow(scroll, 0);
            layout.Children.Add(scroll);

            StackPanel footer = new()
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(12, 0, 12, 12)
            };

            Button cancel = new() { Content = "Cancel", Width = 88, Margin = new Thickness(0, 0, 8, 0) };
            cancel.Click += (_, _) => dialog.Close();

            Button save = new() { Content = "Save", Width = 88, IsDefault = true };
            save.Click += (_, _) =>
            {
                Dictionary<string, (Key, ModifierKeys)> parsed = new();
                HashSet<string> unique = new(StringComparer.OrdinalIgnoreCase);

                foreach ((string key, string label, string _) in ShortcutDefaults)
                {
                    string raw = editors[key].Text.Trim();
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        MessageBox.Show(dialog, $"Shortcut for '{label}' cannot be empty.", "Invalid Shortcut", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    if (!TryParseKeyBinding(raw, out Key gKey, out ModifierKeys gMods))
                    {
                        MessageBox.Show(dialog, $"Shortcut for '{label}' is invalid: {raw}", "Invalid Shortcut", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    string normalized = $"{gMods}-{gKey}";
                    if (!unique.Add(normalized))
                    {
                        MessageBox.Show(dialog, $"Duplicate shortcut detected: {raw}", "Duplicate Shortcut", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    parsed[key] = (gKey, gMods);
                }

                _shortcuts.Clear();
                foreach ((string key, _, _) in ShortcutDefaults)
                    _shortcuts[key] = parsed[key];

                if (!RegisterOrUpdateScreenshotHotkey())
                {
                    MessageBox.Show(
                        dialog,
                        "Could not register screenshot hotkey. It may be used by another app.",
                        "Hotkey Registration Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }

                StatusText.Text = "Shortcuts saved.";
                dialog.Close();
            };

            footer.Children.Add(cancel);
            footer.Children.Add(save);
            Grid.SetRow(footer, 1);
            layout.Children.Add(footer);

            dialog.Content = layout;
            dialog.ShowDialog();
        }

        private static bool TryParseKeyBinding(string text, out Key key, out ModifierKeys modifiers)
        {
            key = Key.None;
            modifiers = ModifierKeys.None;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            try
            {
                KeyGestureConverter converter = new();
                object? result = converter.ConvertFromString(text);
                if (result is KeyGesture parsed && parsed.Key != Key.None)
                {
                    key = parsed.Key;
                    modifiers = parsed.Modifiers;
                    return true;
                }
            }
            catch { }

            string[] parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            ModifierKeys mods = ModifierKeys.None;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                mods |= parts[i].ToUpperInvariant() switch
                {
                    "CTRL" or "CONTROL" => ModifierKeys.Control,
                    "ALT" => ModifierKeys.Alt,
                    "SHIFT" => ModifierKeys.Shift,
                    "WIN" or "WINDOWS" => ModifierKeys.Windows,
                    _ => ModifierKeys.None
                };
            }
            if (Enum.TryParse<Key>(parts[^1], ignoreCase: true, out Key singleKey) && singleKey != Key.None)
            {
                key = singleKey;
                modifiers = mods;
                return true;
            }
            return false;
        }

        private static string FormatKeyBinding(Key key, ModifierKeys modifiers)
        {
            if (modifiers == ModifierKeys.None)
                return key.ToString();

            var parts = new List<string>();
            if ((modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
            if ((modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
            if ((modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
            if ((modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");
            parts.Add(key.ToString());
            return string.Join("+", parts);
        }

        private async void ScreenshotButton_Click(object sender, RoutedEventArgs e)
        {
            bool restoreMainWindow = IsVisible;

            try
            {
                ScreenshotButton.IsEnabled = false;
                StatusText.Text = "Capturing screen...";

                WindowState = WindowState.Minimized;
                await Task.Delay(220);

                (BitmapSource frozenImage, Rect virtualBounds) = CaptureVirtualScreenImage();
                var editor = new SnipEditorWindow(frozenImage, _outputFolder, virtualBounds, _shortcuts);
                bool? result = editor.ShowDialog();

                if (restoreMainWindow)
                {
                    WindowState = WindowState.Normal;
                    Activate();
                }

                if (result == true && !string.IsNullOrWhiteSpace(editor.SavedFilePath))
                {
                    StatusText.Text = "Saved: " + editor.SavedFilePath;
                }
                else
                {
                    StatusText.Text = "Capture canceled.";
                }
            }
            catch (Exception ex)
            {
                if (restoreMainWindow)
                {
                    WindowState = WindowState.Normal;
                    Activate();
                }
                StatusText.Text = "Error: " + ex.Message;
            }
            finally
            {
                ScreenshotButton.IsEnabled = true;
            }
        }

        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _outputFolder,
                UseShellExecute = true
            });

            StatusText.Text = "Opened folder: " + _outputFolder;
        }

        private (BitmapSource Image, Rect Bounds) CaptureVirtualScreenImage()
        {
            int left = (int)SystemParameters.VirtualScreenLeft;
            int top = (int)SystemParameters.VirtualScreenTop;
            int width = (int)SystemParameters.VirtualScreenWidth;
            int height = (int)SystemParameters.VirtualScreenHeight;

            using var bitmap = new Bitmap(width, height);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(left, top, 0, 0, new System.Drawing.Size(width, height));
            }

            IntPtr hBitmap = bitmap.GetHbitmap();
            try
            {
                BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

                source.Freeze();
                return (source, new Rect(left, top, width, height));
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}