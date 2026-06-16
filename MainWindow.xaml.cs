using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AdvancedWindowsHotspot.Models;
using AdvancedWindowsHotspot.ViewModels;

namespace AdvancedWindowsHotspot
{
    public partial class MainWindow : Window
    {
        private MainViewModel ViewModel => (MainViewModel)DataContext;

        private static readonly string WindowStatePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AdvancedWindowsHotspot", "window.json");

        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();

            // 密码框同步
            PasswordBox.PasswordChanged += OnPasswordChanged;
            PasswordTextBox.TextChanged += OnPasswordTextChanged;

            // 频段选择
            BandComboBox.SelectionChanged += OnBandSelectionChanged;

            // 拖动窗口
            MouseLeftButtonDown += OnMouseLeftButtonDown;

            // 窗口关闭时保存状态
            Closed += OnWindowClosed;

            // 加载窗口状态
            LoadWindowState();

            // 根据ViewModel中加载的设置同步ComboBox选中项
            Loaded += OnWindowLoaded;
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            // 同频频段选择
            BandComboBox.SelectedIndex = ViewModel.SelectedBand switch
            {
                WiFiBand.TwoPointFourGHz => 1,
                WiFiBand.FiveGHz => 2,
                _ => 0
            };

            // 同步密码框
            if (!ViewModel.IsPasswordVisible)
            {
                PasswordBox.Password = ViewModel.Password;
            }
        }

        private void OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm && !vm.IsPasswordVisible)
            {
                vm.Password = PasswordBox.Password;
            }
        }

        private void OnPasswordTextChanged(object sender, TextChangedEventArgs e)
        {
            // 仅在可见模式下同步，由绑定自动处理
        }

        private void OnBandSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is MainViewModel vm && BandComboBox.SelectedItem is ComboBoxItem item)
            {
                var tag = item.Tag?.ToString();
                vm.SelectedBand = tag switch
                {
                    "FiveGHz" => WiFiBand.FiveGHz,
                    "TwoPointFourGHz" => WiFiBand.TwoPointFourGHz,
                    _ => WiFiBand.Auto
                };
            }
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            SaveWindowState();
            Application.Current.Shutdown();
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            SaveWindowState();
        }

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            // 允许窗口通过 Aero Snap 调整大小
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            var hwndSource = System.Windows.Interop.HwndSource.FromHwnd(handle);
            hwndSource?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // 处理 WM_NCHITTEST 以支持窗口边缘调整大小
            const int WM_NCHITTEST = 0x0084;
            const int HTCLIENT = 1;

            if (msg == WM_NCHITTEST)
            {
                // 让默认处理先执行
                handled = false;
                var result = DefWindowProc(hwnd, msg, wParam, lParam);
                if (result.ToInt32() == HTCLIENT)
                {
                    // 在客户区内，检查是否在边缘
                    var x = lParam.ToInt32() & 0xFFFF;
                    var y = lParam.ToInt32() >> 16;
                    var pos = PointFromScreen(new Point(x, y));

                    double resizeMargin = 6;
                    bool onLeft = pos.X <= resizeMargin;
                    bool onRight = pos.X >= ActualWidth - resizeMargin;
                    bool onTop = pos.Y <= resizeMargin;
                    bool onBottom = pos.Y >= ActualHeight - resizeMargin;

                    if (onTop && onLeft) { handled = true; return (IntPtr)13; }     // HTTOPLEFT
                    if (onTop && onRight) { handled = true; return (IntPtr)14; }    // HTTOPRIGHT
                    if (onBottom && onLeft) { handled = true; return (IntPtr)16; }  // HTBOTTOMLEFT
                    if (onBottom && onRight) { handled = true; return (IntPtr)17; } // HTBOTTOMRIGHT
                    if (onLeft) { handled = true; return (IntPtr)10; }              // HTLEFT
                    if (onRight) { handled = true; return (IntPtr)11; }             // HTRIGHT
                    if (onTop) { handled = true; return (IntPtr)12; }               // HTTOP
                    if (onBottom) { handled = true; return (IntPtr)15; }            // HTBOTTOM
                }
                return result;
            }
            return IntPtr.Zero;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        #region 窗口状态持久化

        private void LoadWindowState()
        {
            try
            {
                if (!File.Exists(WindowStatePath))
                {
                    return;
                }

                var json = File.ReadAllText(WindowStatePath);
                var state = JsonSerializer.Deserialize<WindowStateData>(json);
                if (state != null)
                {
                    Width = state.Width > 0 ? state.Width : Width;
                    Height = state.Height > 0 ? state.Height : Height;

                    if (state.Left >= 0 && state.Top >= 0)
                    {
                        Left = state.Left;
                        Top = state.Top;
                        WindowStartupLocation = WindowStartupLocation.Manual;
                    }

                    if (state.IsMaximized)
                    {
                        WindowState = WindowState.Maximized;
                    }
                }
            }
            catch (Exception)
            {
                // 忽略加载失败
            }
        }

        private void SaveWindowState()
        {
            try
            {
                var dir = Path.GetDirectoryName(WindowStatePath);
                if (dir != null && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var state = new WindowStateData
                {
                    Left = WindowState == WindowState.Normal ? Left : RestoreBounds.Left,
                    Top = WindowState == WindowState.Normal ? Top : RestoreBounds.Top,
                    Width = WindowState == WindowState.Normal ? Width : RestoreBounds.Width,
                    Height = WindowState == WindowState.Normal ? Height : RestoreBounds.Height,
                    IsMaximized = WindowState == WindowState.Maximized
                };

                var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(WindowStatePath, json);
            }
            catch (Exception)
            {
                // 忽略保存失败
            }
        }

        private class WindowStateData
        {
            public double Left { get; set; }
            public double Top { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public bool IsMaximized { get; set; }
        }

        #endregion
    }
}
