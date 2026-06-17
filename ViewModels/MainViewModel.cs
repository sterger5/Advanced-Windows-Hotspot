using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AdvancedWindowsHotspot.Models;
using AdvancedWindowsHotspot.Services;

namespace AdvancedWindowsHotspot.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly HotspotService _hotspotService;
        private readonly DispatcherTimer _saveSettingsTimer;

        #region 属性

        private string _ssid = string.Empty;
        public string Ssid
        {
            get => _ssid;
            set
            {
                if (SetProperty(ref _ssid, value))
                {
                    DeferSaveSettings();
                    RaiseCanExecuteChanged();
                }
            }
        }

        private string _password = string.Empty;
        public string Password
        {
            get => _password;
            set
            {
                if (SetProperty(ref _password, value))
                {
                    DeferSaveSettings();
                    RaiseCanExecuteChanged();
                }
            }
        }

        private bool _isPasswordVisible;
        public bool IsPasswordVisible
        {
            get => _isPasswordVisible;
            set
            {
                if (SetProperty(ref _isPasswordVisible, value))
                {
                    DeferSaveSettings();
                }
            }
        }

        private WiFiBand _selectedBand = WiFiBand.Auto;
        public WiFiBand SelectedBand
        {
            get => _selectedBand;
            set
            {
                if (SetProperty(ref _selectedBand, value))
                {
                    // 频段选择2.4G或5G时，强制使用系统热点模式（WiFiDirect不支持频段选择）
                    if (value != WiFiBand.Auto && !UseSystemHotspot)
                    {
                        UseSystemHotspot = true;
                    }
                    OnPropertyChanged(nameof(IsBandForcingSystemHotspot));
                    OnPropertyChanged(nameof(ShowBandWarning));
                    DeferSaveSettings();
                }
            }
        }

        /// <summary>
        /// 当前频段选择是否强制要求系统热点模式
        /// </summary>
        public bool IsBandForcingSystemHotspot => _selectedBand != WiFiBand.Auto;

        private bool _allowInternet = true;
        public bool AllowInternet
        {
            get => _allowInternet;
            set
            {
                if (SetProperty(ref _allowInternet, value))
                {
                    DeferSaveSettings();
                    OnPropertyChanged(nameof(ShowEnableSharingButton));
                    OnPropertyChanged(nameof(ShowSharingSection));
                    OnAllowInternetChanged();
                }
            }
        }

        private bool _useSystemHotspot;
        public bool UseSystemHotspot
        {
            get => _useSystemHotspot;
            set
            {
                if (SetProperty(ref _useSystemHotspot, value))
                {
                    DeferSaveSettings();
                    OnPropertyChanged(nameof(ShowEnableSharingButton));
                }
            }
        }

        private HotspotStatus _status = HotspotStatus.Idle;
        public HotspotStatus Status
        {
            get => _status;
            set
            {
                if (SetProperty(ref _status, value))
                {
                    OnPropertyChanged(nameof(IsRunning));
                    OnPropertyChanged(nameof(IsIdle));
                    OnPropertyChanged(nameof(CanStart));
                    OnPropertyChanged(nameof(CanStop));
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(ActionButtonText));
                    OnPropertyChanged(nameof(ShowEnableSharingButton));
                    OnPropertyChanged(nameof(ShowSharingSection));
                    RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsRunning => _status == HotspotStatus.Running;
        public bool IsIdle => _status == HotspotStatus.Idle;

        public bool CanStart => _status == HotspotStatus.Idle || _status == HotspotStatus.Error;
        public bool CanStop => _status == HotspotStatus.Running;

        public string StatusText => _status switch
        {
            HotspotStatus.Idle => "就绪",
            HotspotStatus.Starting => "正在启动...",
            HotspotStatus.Running => "热点运行中",
            HotspotStatus.Stopping => "正在停止...",
            HotspotStatus.Error => "发生错误",
            _ => "未知"
        };

        public string ActionButtonText => IsRunning ? "停止热点" : "启动热点";

        private string _errorMessage = string.Empty;
        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        private bool _is5GHzSupported = true;
        public bool Is5GHzSupported
        {
            get => _is5GHzSupported;
            set
            {
                if (SetProperty(ref _is5GHzSupported, value))
                {
                    OnPropertyChanged(nameof(ShowBandWarning));
                }
            }
        }

        /// <summary>
        /// 是否显示5GHz不支持警告（选择了5GHz但网卡不支持）
        /// </summary>
        public bool ShowBandWarning => SelectedBand == WiFiBand.FiveGHz && !Is5GHzSupported;

        private bool _isInitialized;
        public bool IsInitialized
        {
            get => _isInitialized;
            set => SetProperty(ref _isInitialized, value);
        }

        private string _initError = string.Empty;
        public string InitError
        {
            get => _initError;
            set => SetProperty(ref _initError, value);
        }

        private bool _isConfiguringIcs;
        public bool IsConfiguringIcs
        {
            get => _isConfiguringIcs;
            set => SetProperty(ref _isConfiguringIcs, value);
        }

        private string _icsResultMessage = string.Empty;
        public string IcsResultMessage
        {
            get => _icsResultMessage;
            set
            {
                if (SetProperty(ref _icsResultMessage, value))
                {
                    OnPropertyChanged(nameof(HasIcsResult));
                }
            }
        }

        private bool _icsResultSuccess;
        public bool IcsResultSuccess
        {
            get => _icsResultSuccess;
            set => SetProperty(ref _icsResultSuccess, value);
        }

        public bool HasIcsResult => !string.IsNullOrEmpty(IcsResultMessage);

        /// <summary>
        /// 是否显示手动启用共享按钮（WiFiDirect模式 + 热点运行中 + 允许联网）
        /// </summary>
        public bool ShowEnableSharingButton => IsRunning && !UseSystemHotspot && AllowInternet;

        /// <summary>
        /// 是否显示共享适配器选择区域（允许联网 + 热点运行中）
        /// </summary>
        public bool ShowSharingSection => AllowInternet && IsRunning;

        /// <summary>
        /// 可用的互联网适配器列表
        /// </summary>
        public ObservableCollection<NetworkAdapterItem> InternetAdapters { get; } = new();

        private NetworkAdapterItem? _selectedInternetAdapter;
        /// <summary>
        /// 用户选中的互联网适配器
        /// </summary>
        public NetworkAdapterItem? SelectedInternetAdapter
        {
            get => _selectedInternetAdapter;
            set
            {
                if (SetProperty(ref _selectedInternetAdapter, value))
                {
                    DeferSaveSettings();
                }
            }
        }

        #endregion

        #region 命令

        public ICommand ToggleHotspotCommand { get; }
        public ICommand TogglePasswordVisibilityCommand { get; }
        public ICommand EnableSharingCommand { get; }

        #endregion

        public MainViewModel()
        {
            _hotspotService = new HotspotService();

            _saveSettingsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _saveSettingsTimer.Tick += (s, e) =>
            {
                _saveSettingsTimer.Stop();
                SaveSettings();
            };

            ToggleHotspotCommand = new RelayCommand(OnToggleHotspot, CanToggleHotspot);
            TogglePasswordVisibilityCommand = new RelayCommand(OnTogglePasswordVisibility);
            EnableSharingCommand = new RelayCommand(OnEnableSharing, CanEnableSharing);

            LoadSettings();
            InitializeAsync();
        }

        private async void InitializeAsync()
        {
            try
            {
                Logger.Info("正在初始化应用...");
                await _hotspotService.InitializeAsync();

                Is5GHzSupported = _hotspotService.Is5GHzSupported;
                IsInitialized = true;
                Logger.Info("应用初始化完成");
            }
            catch (Exception ex)
            {
                Logger.Error("应用初始化失败", ex);
                InitError = $"初始化失败: {ex.Message}";
                IsInitialized = true; // 仍然显示界面，但显示错误
            }
        }

        private async void OnToggleHotspot()
        {
            if (IsRunning)
            {
                await StopHotspotAsync();
            }
            else
            {
                await StartHotspotAsync();
            }
        }

        private bool CanToggleHotspot()
        {
            if (IsRunning)
            {
                return CanStop;
            }
            return CanStart && !string.IsNullOrWhiteSpace(Ssid) && !string.IsNullOrWhiteSpace(Password);
        }

        private async Task StartHotspotAsync()
        {
            try
            {
                Status = HotspotStatus.Starting;
                ErrorMessage = string.Empty;
                IcsResultMessage = string.Empty;
                Logger.Info($"用户请求启动热点: SSID={Ssid}");

                var settings = new HotspotSettings
                {
                    Ssid = Ssid,
                    Password = Password,
                    Band = SelectedBand,
                    AllowInternet = AllowInternet,
                    UseSystemHotspot = UseSystemHotspot
                };

                await _hotspotService.StartAsync(settings);
                Status = HotspotStatus.Running;

                // 热点启动后刷新互联网适配器列表
                RefreshInternetAdapters();

                Logger.Info("热点启动成功");
            }
            catch (NotSupportedException ex)
            {
                Status = HotspotStatus.Error;
                ErrorMessage = ex.Message;
                Logger.Error("热点启动失败（不支持）", ex);
                MessageBox.Show(ex.Message, "无法启动热点", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (InvalidOperationException ex)
            {
                Status = HotspotStatus.Error;
                ErrorMessage = ex.Message;
                Logger.Error("热点启动失败（操作无效）", ex);
                MessageBox.Show(ex.Message, "无法启动热点", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                Status = HotspotStatus.Error;
                ErrorMessage = $"启动失败: {ex.Message}";
                Logger.Error("热点启动失败", ex);
                MessageBox.Show($"启动热点失败:\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task StopHotspotAsync()
        {
            try
            {
                Status = HotspotStatus.Stopping;
                IcsResultMessage = string.Empty;
                Logger.Info("用户请求停止热点");

                await _hotspotService.StopAsync();
                Status = HotspotStatus.Idle;
                Logger.Info("热点已停止");
            }
            catch (Exception ex)
            {
                Status = HotspotStatus.Error;
                ErrorMessage = $"停止失败: {ex.Message}";
                Logger.Error("停止热点失败", ex);
            }
        }

        private void OnTogglePasswordVisibility()
        {
            IsPasswordVisible = !IsPasswordVisible;
        }

        private bool CanEnableSharing()
        {
            return ShowEnableSharingButton && !IsConfiguringIcs;
        }

        private async void OnEnableSharing()
        {
            if (IsConfiguringIcs) return;

            IsConfiguringIcs = true;
            IcsResultMessage = string.Empty;
            IcsResultMessage = "正在配置网络共享...";

            try
            {
                var selectedAdapter = SelectedInternetAdapter?.Name;
                var result = await Task.Run(() => _hotspotService.ManualEnableInternetSharing(selectedAdapter));
                IcsResultSuccess = result.success;
                IcsResultMessage = result.message;
            }
            catch (Exception ex)
            {
                IcsResultSuccess = false;
                IcsResultMessage = $"配置失败: {ex.Message}";
            }
            finally
            {
                IsConfiguringIcs = false;
            }
        }

        private void RaiseCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>
        /// 刷新可用的互联网适配器列表
        /// </summary>
        private void RefreshInternetAdapters()
        {
            InternetAdapters.Clear();
            SelectedInternetAdapter = null;

            try
            {
                var adapters = _hotspotService.GetAvailableInternetAdapters();
                foreach (var (name, description) in adapters)
                {
                    InternetAdapters.Add(new NetworkAdapterItem(name, description));
                }

                // 自动选择第一个适配器
                if (InternetAdapters.Count > 0)
                {
                    SelectedInternetAdapter = InternetAdapters[0];
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"刷新互联网适配器列表失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 运行中切换"允许联网"开关时，动态启用/禁用ICS
        /// </summary>
        private async void OnAllowInternetChanged()
        {
            if (!IsRunning) return;

            try
            {
                if (!AllowInternet)
                {
                    // 关闭联网 → 禁用ICS
                    IcsResultMessage = string.Empty;
                    await Task.Run(() => _hotspotService.DisableInternetSharing());
                    Logger.Info("已禁用网络共享（用户关闭允许联网开关）");
                }
                else
                {
                    // 开启联网 → 刷新适配器列表
                    RefreshInternetAdapters();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"切换允许联网状态失败: {ex.Message}");
            }
        }

        #region 设置持久化

        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AdvancedWindowsHotspot", "settings.json");

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    // 默认值
                    Ssid = Environment.MachineName;
                    var random = new Random().Next(1000, 9999);
                    Password = $"Hotspot{random}";
                    return;
                }

                var json = File.ReadAllText(SettingsPath);
                var settings = System.Text.Json.JsonSerializer.Deserialize<SettingsData>(json);
                if (settings != null)
                {
                    Ssid = settings.Ssid ?? Environment.MachineName;
                    Password = settings.Password ?? $"Hotspot{new Random().Next(1000, 9999)}";
                    IsPasswordVisible = settings.IsPasswordVisible;
                    SelectedBand = settings.SelectedBand;
                    AllowInternet = settings.AllowInternet;
                    UseSystemHotspot = settings.UseSystemHotspot;
                }

                Logger.Info("已加载保存的设置");
            }
            catch (Exception ex)
            {
                Logger.Error("加载设置失败", ex);
                Ssid = Environment.MachineName;
                var random = new Random().Next(1000, 9999);
                Password = $"Hotspot{random}";
            }
        }

        /// <summary>
        /// 延迟保存设置（防抖：500ms 内多次变更只触发一次写入）
        /// </summary>
        private void DeferSaveSettings()
        {
            _saveSettingsTimer.Stop();
            _saveSettingsTimer.Start();
        }

        private void SaveSettings()
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath);
                if (dir != null && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var settings = new SettingsData
                {
                    Ssid = Ssid,
                    Password = Password,
                    IsPasswordVisible = IsPasswordVisible,
                    SelectedBand = SelectedBand,
                    AllowInternet = AllowInternet,
                    UseSystemHotspot = UseSystemHotspot
                };

                var json = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                Logger.Error("保存设置失败", ex);
            }
        }

        private class SettingsData
        {
            public string? Ssid { get; set; }
            public string? Password { get; set; }
            public bool IsPasswordVisible { get; set; }
            public WiFiBand SelectedBand { get; set; } = WiFiBand.Auto;
            public bool AllowInternet { get; set; } = true;
            public bool UseSystemHotspot { get; set; }
        }

        #endregion
    }

    /// <summary>
    /// 网络适配器选项（用于下拉选择）
    /// </summary>
    public class NetworkAdapterItem
    {
        public string Name { get; }
        public string Description { get; }

        public NetworkAdapterItem(string name, string description)
        {
            Name = name;
            Description = description;
        }

        /// <summary>
        /// 显示文本：适配器名称 (描述)
        /// </summary>
        public string DisplayName => Description == Name ? Name : $"{Name} ({Description})";

        public override string ToString() => DisplayName;
    }
}
