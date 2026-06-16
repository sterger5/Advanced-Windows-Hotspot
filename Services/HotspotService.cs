using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.WiFiDirect;
using Windows.Networking.Connectivity;
using Windows.Networking.NetworkOperators;
using Windows.Security.Credentials;
using AdvancedWindowsHotspot.Models;

namespace AdvancedWindowsHotspot.Services
{
    public class HotspotService
    {
        #region WiFiDirect 组件

        private WiFiDirectAdvertisementPublisher? _publisher;
        private WiFiDirectConnectionListener? _connectionListener;
        private readonly Dictionary<string, WiFiDirectDevice> _connectedDevices = new();
        private TaskCompletionSource<bool>? _wfdStartTcs;

        #endregion

        #region TetheringManager 组件

        private NetworkOperatorTetheringManager? _tetheringManager;

        #endregion

        #region 状态

        private bool _is5GHzSupported;
        private bool _is2_4GHzSupported = true;
        private bool _isRunningViaWiFiDirect;
        private bool _isRunningViaTethering;
        private bool _allowInternet;
        private bool _icsConfiguredForWiFiDirect;

        public bool Is5GHzSupported => _is5GHzSupported;
        public bool Is2_4GHzSupported => _is2_4GHzSupported;
        public bool IsRunning => _isRunningViaWiFiDirect || _isRunningViaTethering;

        public event EventHandler<string>? StatusChanged;
        public event EventHandler? HotspotStarted;
        public event EventHandler? HotspotStopped;

        #endregion

        public async Task InitializeAsync()
        {
            try
            {
                Logger.Info("正在初始化热点服务...");

                // 直接通过无线网卡检测5GHz支持（不依赖网络连接）
                await DetectBandSupportAsync();

                // 尝试初始化TetheringManager（用于系统热点模式）
                try
                {
                    var connectionProfile = NetworkInformation.GetInternetConnectionProfile();
                    if (connectionProfile != null)
                    {
                        _tetheringManager = NetworkOperatorTetheringManager.CreateFromConnectionProfile(connectionProfile);
                        Logger.Info("TetheringManager初始化成功");
                    }
                    else
                    {
                        Logger.Info("无活动网络连接，TetheringManager不可用");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"TetheringManager初始化失败: {ex.Message}");
                }

                Logger.Info($"热点服务初始化完成 - 5GHz支持: {_is5GHzSupported}");
            }
            catch (Exception ex)
            {
                Logger.Error("初始化热点服务失败", ex);
                throw;
            }
        }

        /// <summary>
        /// 通过多种方式并行查询无线网卡是否支持5GHz频段
        /// </summary>
        private async Task DetectBandSupportAsync()
        {
            try
            {
                // 并行启动所有检测方式，任一成功即返回
                var t1 = Detect5GHzViaNetshCapabilitiesAsync();
                var t2 = Detect5GHzViaNetshDriverAsync();
                var t3 = Detect5GHzViaTetheringManagerAsync();

                var tasks = new[] { t1, t2, t3 };
                var all = Task.WhenAll(tasks);

                // 等待所有任务完成（最多15秒）
                var completed = await Task.WhenAny(all, Task.Delay(15000));

                // 检查结果：任一方法检测到5GHz支持即认为支持
                if (t1.IsCompletedSuccessfully && t1.Result)
                {
                    _is5GHzSupported = true;
                    Logger.Info("通过netsh wirelesscapabilities检测到5GHz支持");
                    return;
                }

                if (t2.IsCompletedSuccessfully && t2.Result)
                {
                    _is5GHzSupported = true;
                    Logger.Info("通过netsh wlan show driver检测到5GHz支持");
                    return;
                }

                if (t3.IsCompletedSuccessfully && t3.Result)
                {
                    _is5GHzSupported = true;
                    Logger.Info("通过TetheringManager检测到5GHz支持");
                    return;
                }

                // 无法确定，默认支持（让用户尝试）
                _is5GHzSupported = true;
                Logger.Info("无法确定5GHz支持，默认设为支持");
            }
            catch (Exception ex)
            {
                Logger.Warning($"5GHz频段检测失败: {ex.Message}，默认设为支持");
                _is5GHzSupported = true;
            }
        }

        private async Task<bool> Detect5GHzViaNetshCapabilitiesAsync()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = "wlan show wirelesscapabilities",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8
                };

                using var process = Process.Start(psi);
                if (process == null) return false;

                var output = await process.StandardOutput.ReadToEndAsync();
                process.WaitForExit(5000);

                return output.Contains("5 GHz") || output.Contains("5GHz") ||
                       output.Contains("802.11a") || output.Contains("802.11ac") || output.Contains("802.11ax");
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> Detect5GHzViaNetshDriverAsync()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = "-Command \"Get-NetAdapter | Where-Object {$_.MediaType -eq '802.11' -or $_.InterfaceDescription -like '*Wi-Fi*' -or $_.InterfaceDescription -like '*Wireless*' -or $_.InterfaceDescription -like '*802.11*'} | Select-Object -First 1 | ForEach-Object { netsh wlan show driver }\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8
                };

                using var process = Process.Start(psi);
                if (process == null) return false;

                var output = await process.StandardOutput.ReadToEndAsync();
                process.WaitForExit(10000);

                return output.Contains("802.11a") || output.Contains("802.11ac") ||
                       output.Contains("802.11ax") || output.Contains("5 GHz");
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> Detect5GHzViaTetheringManagerAsync()
        {
            try
            {
                if (_tetheringManager != null)
                {
                    var config = _tetheringManager.GetCurrentAccessPointConfiguration();
                    return config.IsBandSupported(TetheringWiFiBand.FiveGigahertz);
                }

                var connectionProfile = NetworkInformation.GetInternetConnectionProfile();
                if (connectionProfile != null)
                {
                    var tm = NetworkOperatorTetheringManager.CreateFromConnectionProfile(connectionProfile);
                    var config = tm.GetCurrentAccessPointConfiguration();
                    var supported = config.IsBandSupported(TetheringWiFiBand.FiveGigahertz);
                    Logger.Info($"通过TetheringManager检测到5GHz支持: {supported}");
                    return supported;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public async Task StartAsync(HotspotSettings settings)
        {
            if (IsRunning)
            {
                throw new InvalidOperationException("热点已在运行中");
            }

            try
            {
                Logger.Info($"正在启动热点: SSID={settings.Ssid}, 频段={settings.Band}, 允许联网={settings.AllowInternet}, 系统热点模式={settings.UseSystemHotspot}");

                // 检查5GHz支持
                if (settings.Band == WiFiBand.FiveGHz && !_is5GHzSupported)
                {
                    throw new NotSupportedException("当前无线网卡不支持5GHz频段热点，请切换至2.4GHz或自动频段后重试");
                }

                // 策略：
                // 用户选择了"使用系统热点" → TetheringManager
                // 频段选择了2.4G或5G → 强制TetheringManager（WiFiDirect不支持频段选择）
                // 用户未选择"使用系统热点"且频段为自动 → WiFiDirect（独立于系统热点）
                //   - 需要联网时WiFiDirect + ICS
                //   - 不需要联网时纯WiFiDirect

                if (settings.UseSystemHotspot || settings.Band != WiFiBand.Auto)
                {
                    // 系统热点模式：必须使用TetheringManager
                    // 频段为2.4G或5G时也强制使用TetheringManager，因为WiFiDirect不支持频段选择
                    if (settings.Band != WiFiBand.Auto && !settings.UseSystemHotspot)
                    {
                        Logger.Info($"频段为{settings.Band}，强制使用系统热点模式（WiFiDirect不支持频段选择）");
                    }

                    if (_tetheringManager == null)
                    {
                        if (settings.Band != WiFiBand.Auto)
                        {
                            throw new InvalidOperationException("当前无网络连接，无法使用指定频段。WiFiDirect模式不支持频段选择，请连接网络后重试，或切换频段为「自动」。");
                        }
                        throw new InvalidOperationException("当前无网络连接，无法使用系统热点模式。请连接网络或取消「使用系统热点」选项。");
                    }
                    await StartWithTetheringManager(settings);
                }
                else
                {
                    // WiFiDirect模式（独立于系统热点）
                    await StartWithWiFiDirect(settings);
                }
            }
            catch (NotSupportedException)
            {
                throw;
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Error("启动热点时发生异常", ex);
                throw;
            }
        }

        #region WiFiDirect 模式

        private async Task StartWithWiFiDirect(HotspotSettings settings)
        {
            Logger.Info($"使用WiFiDirect模式启动热点: SSID={settings.Ssid}");

            _wfdStartTcs = new TaskCompletionSource<bool>();

            try
            {
                _publisher = new WiFiDirectAdvertisementPublisher();
                _publisher.StatusChanged += OnPublisherStatusChanged;

                var advertisement = _publisher.Advertisement;
                advertisement.IsAutonomousGroupOwnerEnabled = true;

                var legacySettings = advertisement.LegacySettings;
                legacySettings.IsEnabled = true;
                legacySettings.Ssid = settings.Ssid;

                var credential = new PasswordCredential();
                credential.Password = settings.Password;
                legacySettings.Passphrase = credential;

                _publisher.Start();

                Logger.Info("WiFiDirect广告发布已启动，等待状态确认...");

                // 等待启动结果（超时10秒）
                var completed = await Task.WhenAny(_wfdStartTcs.Task, Task.Delay(10000));

                if (completed != _wfdStartTcs.Task)
                {
                    CleanupWiFiDirect();
                    throw new InvalidOperationException("WiFiDirect热点启动超时");
                }

                var success = await _wfdStartTcs.Task;

                if (success)
                {
                    _isRunningViaWiFiDirect = true;
                    _allowInternet = settings.AllowInternet;
                    _icsConfiguredForWiFiDirect = false;

                    // 启动连接监听器
                    StartConnectionListener();

                    // ICS配置延迟到设备连接时执行（此时虚拟适配器才真正就绪）

                    HotspotStarted?.Invoke(this, EventArgs.Empty);
                    Logger.Info("WiFiDirect热点启动成功");
                }
                else
                {
                    CleanupWiFiDirect();
                    throw new InvalidOperationException("WiFiDirect热点启动失败");
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                CleanupWiFiDirect();
                Logger.Error("WiFiDirect启动异常", ex);
                throw new InvalidOperationException($"WiFiDirect启动失败: {ex.Message}", ex);
            }
        }

        private void OnPublisherStatusChanged(WiFiDirectAdvertisementPublisher sender,
            WiFiDirectAdvertisementPublisherStatusChangedEventArgs args)
        {
            switch (args.Status)
            {
                case WiFiDirectAdvertisementPublisherStatus.Started:
                    Logger.Info("WiFiDirect广告已开始发布");
                    _wfdStartTcs?.TrySetResult(true);
                    break;

                case WiFiDirectAdvertisementPublisherStatus.Aborted:
                    var errorMsg = args.Error switch
                    {
                        WiFiDirectError.RadioNotAvailable =>
                            "Wi-Fi已关闭，请开启Wi-Fi后重试",
                        WiFiDirectError.ResourceInUse =>
                            "WiFiDirect资源被占用，请关闭其他使用WiFiDirect的应用后重试",
                        _ => $"WiFiDirect启动失败 (错误代码: {(int)args.Error})"
                    };
                    Logger.Error($"WiFiDirect广告中止: {errorMsg}");
                    _wfdStartTcs?.TrySetResult(false);

                    if (_isRunningViaWiFiDirect)
                    {
                        _isRunningViaWiFiDirect = false;
                        StatusChanged?.Invoke(this, errorMsg);
                        HotspotStopped?.Invoke(this, EventArgs.Empty);
                    }
                    break;

                case WiFiDirectAdvertisementPublisherStatus.Stopped:
                    Logger.Info("WiFiDirect广告已停止");
                    if (_isRunningViaWiFiDirect)
                    {
                        _isRunningViaWiFiDirect = false;
                        HotspotStopped?.Invoke(this, EventArgs.Empty);
                    }
                    break;
            }
        }

        private void StartConnectionListener()
        {
            try
            {
                _connectionListener = new WiFiDirectConnectionListener();
                _connectionListener.ConnectionRequested += OnConnectionRequested;
                Logger.Info("WiFiDirect连接监听器已启动");
            }
            catch (Exception ex)
            {
                Logger.Warning($"启动连接监听器失败: {ex.Message}，设备可能无法连接");
            }
        }

        private async void OnConnectionRequested(WiFiDirectConnectionListener sender,
            WiFiDirectConnectionRequestedEventArgs args)
        {
            try
            {
                var request = args.GetConnectionRequest();
                var deviceId = request.DeviceInformation.Id;
                var deviceName = request.DeviceInformation.Name;

                Logger.Info($"设备请求连接: {deviceName} ({deviceId})");

                // 接受连接
                var wfdDevice = await WiFiDirectDevice.FromIdAsync(deviceId);

                // 获取端点对（包含IP地址）
                var endpointPairs = wfdDevice.GetConnectionEndpointPairs();
                if (endpointPairs.Count > 0)
                {
                    var remoteHost = endpointPairs[0].RemoteHostName?.DisplayName ?? "未知";
                    Logger.Info($"设备已连接: {deviceName}, IP: {remoteHost}");
                    StatusChanged?.Invoke(this, $"设备已连接: {deviceName}");
                }

                // 监听设备断开连接
                wfdDevice.ConnectionStatusChanged += OnDeviceConnectionStatusChanged;

                // 保存设备引用
                _connectedDevices[deviceId] = wfdDevice;

                // 设备连接后配置ICS（此时虚拟适配器才真正就绪）
                if (_allowInternet && !_icsConfiguredForWiFiDirect)
                {
                    _icsConfiguredForWiFiDirect = true;
                    Logger.Info("设备已连接，开始配置ICS...");
                    await Task.Run(() => EnableInternetSharingForWiFiDirect());
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"处理连接请求失败: {ex.Message}");
            }
        }

        private void OnDeviceConnectionStatusChanged(WiFiDirectDevice sender, object args)
        {
            try
            {
                if (sender.ConnectionStatus == WiFiDirectConnectionStatus.Disconnected)
                {
                    var deviceId = sender.DeviceId;
                    Logger.Info($"设备已断开: {deviceId}");

                    sender.ConnectionStatusChanged -= OnDeviceConnectionStatusChanged;
                    _connectedDevices.Remove(deviceId);

                    StatusChanged?.Invoke(this, "设备已断开连接");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"处理设备断开事件失败: {ex.Message}");
            }
        }

        private void CleanupWiFiDirect()
        {
            if (_publisher != null)
            {
                _publisher.StatusChanged -= OnPublisherStatusChanged;
                try
                {
                    _publisher.Stop();
                }
                catch
                {
                    // 忽略停止时的异常
                }
                _publisher = null;
            }

            if (_connectionListener != null)
            {
                _connectionListener.ConnectionRequested -= OnConnectionRequested;
                _connectionListener = null;
            }

            foreach (var device in _connectedDevices.Values)
            {
                try
                {
                    device.ConnectionStatusChanged -= OnDeviceConnectionStatusChanged;
                }
                catch
                {
                    // 忽略
                }
            }
            _connectedDevices.Clear();

            // 禁用ICS
            try
            {
                DisableInternetSharing();
            }
            catch
            {
                // 忽略
            }

            _icsConfiguredForWiFiDirect = false;
            _allowInternet = false;
            _isRunningViaWiFiDirect = false;
        }

        #endregion

        #region TetheringManager 模式

        private async Task StartWithTetheringManager(HotspotSettings settings)
        {
            if (_tetheringManager == null)
            {
                throw new InvalidOperationException("TetheringManager未初始化，无法使用指定频段");
            }

            Logger.Info($"使用TetheringManager模式启动热点: SSID={settings.Ssid}, 频段={settings.Band}");

            var band = settings.Band switch
            {
                WiFiBand.FiveGHz => TetheringWiFiBand.FiveGigahertz,
                WiFiBand.TwoPointFourGHz => TetheringWiFiBand.TwoPointFourGigahertz,
                _ => TetheringWiFiBand.Auto
            };

            NetworkOperatorTetheringOperationResult result;

            try
            {
                var sessionConfig = new NetworkOperatorTetheringSessionAccessPointConfiguration();
                sessionConfig.Ssid = settings.Ssid;
                sessionConfig.Passphrase = settings.Password;

                try
                {
                    sessionConfig.Band = band;
                    Logger.Info($"已设置会话频段: {band}");
                }
                catch (Exception ex)
                {
                    Logger.Warning($"设置会话频段失败: {ex.Message}，将使用默认频段");
                }

                Logger.Info("尝试使用会话配置启动热点...");
                result = await _tetheringManager.StartTetheringAsync(sessionConfig);
            }
            catch (Exception ex) when (
                ex.HResult == unchecked((int)0x80040154) ||
                ex.Message.Contains("classfactory") ||
                ex.Message.Contains("请求的类"))
            {
                Logger.Warning($"会话配置方式不可用: {ex.Message}，回退到持久化配置方式");

                var config = _tetheringManager.GetCurrentAccessPointConfiguration();
                config.Ssid = settings.Ssid;
                config.Passphrase = settings.Password;

                try
                {
                    config.Band = band;
                    Logger.Info($"已设置持久化频段: {band}");
                }
                catch (Exception ex2)
                {
                    Logger.Warning($"设置频段失败: {ex2.Message}，将使用默认频段");
                }

                await _tetheringManager.ConfigureAccessPointAsync(config);
                Logger.Info("持久化配置已更新，正在启动热点...");
                result = await _tetheringManager.StartTetheringAsync();
            }

            if (result.Status == TetheringOperationStatus.Success)
            {
                Logger.Info("TetheringManager热点启动成功");
                _isRunningViaTethering = true;

                if (!settings.AllowInternet)
                {
                    DisableInternetSharing();
                }

                HotspotStarted?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                var errorMsg = result.AdditionalErrorMessage ?? result.Status.ToString();
                Logger.Error($"TetheringManager热点启动失败: Status={result.Status}, Message={errorMsg}");

                if (result.Status == TetheringOperationStatus.WiFiDeviceOff)
                {
                    if (band == TetheringWiFiBand.FiveGigahertz)
                    {
                        throw new NotSupportedException("当前无线网卡不支持5GHz频段热点，请切换至2.4GHz或自动频段后重试");
                    }
                    throw new InvalidOperationException("无线网卡不可用，请确保Wi-Fi已开启");
                }

                throw new InvalidOperationException($"热点启动失败: {errorMsg}");
            }
        }

        #endregion

        public async Task StopAsync()
        {
            try
            {
                Logger.Info("正在停止热点...");

                if (_isRunningViaWiFiDirect)
                {
                    CleanupWiFiDirect();
                    Logger.Info("WiFiDirect热点已停止");
                    HotspotStopped?.Invoke(this, EventArgs.Empty);
                }
                else if (_isRunningViaTethering && _tetheringManager != null)
                {
                    var result = await _tetheringManager.StopTetheringAsync();
                    _isRunningViaTethering = false;
                    Logger.Info($"TetheringManager热点已停止: {result.Status}");
                    HotspotStopped?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("停止热点时发生异常", ex);
                throw;
            }
        }

        public TetheringOperationalState GetOperationalState()
        {
            if (_isRunningViaWiFiDirect)
            {
                return TetheringOperationalState.On;
            }

            if (_tetheringManager == null)
            {
                return TetheringOperationalState.Off;
            }

            try
            {
                return _tetheringManager.TetheringOperationalState;
            }
            catch (Exception ex)
            {
                Logger.Error("获取热点状态失败", ex);
                return TetheringOperationalState.Off;
            }
        }

        public TetheringCapability GetTetheringCapability()
        {
            try
            {
                var connectionProfile = NetworkInformation.GetInternetConnectionProfile();
                if (connectionProfile == null)
                {
                    return TetheringCapability.DisabledDueToUnknownCause;
                }

                return NetworkOperatorTetheringManager.GetTetheringCapabilityFromConnectionProfile(connectionProfile);
            }
            catch (Exception ex)
            {
                Logger.Error("获取热点能力失败", ex);
                return TetheringCapability.DisabledDueToUnknownCause;
            }
        }

        #region Internet连接共享 (ICS)

        /// <summary>
        /// 手动启用Internet连接共享（供UI按钮调用）
        /// 返回 (是否成功, 结果消息)
        /// </summary>
        public (bool success, string message) ManualEnableInternetSharing()
        {
            try
            {
                var (internetAdapterName, hotspotAdapterName) = FindIcsAdapters(retry: false);
                if (hotspotAdapterName == null)
                {
                    return (false, "未找到热点虚拟适配器（Wi-Fi Direct Virtual Adapter），请确保热点已启动且有设备连接");
                }
                if (internetAdapterName == null)
                {
                    return (false, "未找到互联网适配器，请确保本机已连接互联网");
                }

                Logger.Info($"手动启用ICS: 公共={internetAdapterName}, 私有={hotspotAdapterName}");

                bool success = ConfigureIcs(internetAdapterName, hotspotAdapterName);
                if (success)
                {
                    _icsConfiguredForWiFiDirect = true;
                    return (true, $"已启用共享: {internetAdapterName} → {hotspotAdapterName}");
                }
                else
                {
                    return (false, "ICS配置失败，请尝试手动在网卡属性中勾选共享");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"手动启用ICS失败: {ex.Message}");
                return (false, $"配置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 自动检测上行网卡（Wi-Fi/以太网）并启用"允许其他网络用户通过此计算机的Internet连接来连接"
        /// </summary>
        private void EnableInternetSharingForWiFiDirect()
        {
            try
            {
                Logger.Info("正在为WiFiDirect配置Internet连接共享...");

                var (internetAdapterName, hotspotAdapterName) = FindIcsAdapters(retry: true);

                if (internetAdapterName == null)
                {
                    Logger.Error("未找到互联网适配器，无法启用ICS");
                    return;
                }

                if (hotspotAdapterName == null)
                {
                    Logger.Error("未找到热点适配器，无法启用ICS");
                    return;
                }

                bool success = ConfigureIcs(internetAdapterName, hotspotAdapterName);

                if (success)
                {
                    Logger.Info("WiFiDirect Internet连接共享配置成功");
                }
                else
                {
                    Logger.Error("所有ICS配置方式均失败，设备连接后可能无法上网");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("配置WiFiDirect Internet连接共享失败", ex);
            }
        }

        /// <summary>
        /// 查找ICS所需的互联网适配器和热点适配器
        /// </summary>
        /// <param name="retry">是否带重试等待热点适配器就绪（自动模式）</param>
        private (string? internetAdapter, string? hotspotAdapter) FindIcsAdapters(bool retry)
        {
            EnsureIcsServiceRunning();

            string? hotspotAdapterName;
            if (retry)
            {
                hotspotAdapterName = null;
                for (int i = 0; i < 5; i++)
                {
                    hotspotAdapterName = FindWiFiDirectVirtualAdapter();
                    if (hotspotAdapterName != null)
                    {
                        break;
                    }
                    Logger.Info($"等待WiFiDirect虚拟适配器就绪... ({i + 1}/5)");
                    System.Threading.Thread.Sleep(3000);
                }

                if (hotspotAdapterName != null)
                {
                    Logger.Info($"找到WiFiDirect虚拟适配器: {hotspotAdapterName}");
                }
                else
                {
                    Logger.Warning("未找到WiFiDirect虚拟适配器，ICS可能无法正确配置");
                }
            }
            else
            {
                hotspotAdapterName = FindWiFiDirectVirtualAdapter();
            }

            var internetAdapterName = GetInternetAdapterName();
            Logger.Info($"互联网适配器: {internetAdapterName ?? "未找到"}");

            return (internetAdapterName, hotspotAdapterName);
        }

        /// <summary>
        /// 配置ICS：先尝试PowerShell COM，失败则回退到C# COM
        /// </summary>
        private bool ConfigureIcs(string internetAdapterName, string hotspotAdapterName)
        {
            bool success = ConfigureIcsViaPowerShell(internetAdapterName, hotspotAdapterName);

            if (!success)
            {
                Logger.Warning("PowerShell COM方式失败，尝试C# COM方式");
                success = ConfigureIcsViaCom(internetAdapterName, hotspotAdapterName);
            }

            if (success)
            {
                _icsConfiguredForWiFiDirect = true;
            }

            return success;
        }

        /// <summary>
        /// 确保ICS（SharedAccess）服务正在运行
        /// </summary>
        private void EnsureIcsServiceRunning()
        {
            try
            {
                var output = RunProcessWithOutput("powershell",
                    "-Command \"Start-Service -Name 'SharedAccess' -ErrorAction SilentlyContinue; Set-Service -Name 'SharedAccess' -StartupType Manual -ErrorAction SilentlyContinue; (Get-Service -Name 'SharedAccess').Status\"");
                Logger.Info($"ICS服务状态: {output?.Trim() ?? "未知"}");
            }
            catch (Exception ex)
            {
                Logger.Warning($"启动ICS服务失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 查找WiFiDirect虚拟适配器名称（更可靠的识别方式）
        /// </summary>
        private string? FindWiFiDirectVirtualAdapter()
        {
            try
            {
                // 方法1：查找描述包含"Wi-Fi Direct"的适配器
                var output = RunProcessWithOutput("powershell",
                    "-Command \"(Get-NetAdapter | Where-Object {$_.InterfaceDescription -like '*Wi-Fi Direct*' -or $_.InterfaceDescription -like '*Microsoft Wi-Fi Direct*' -or $_.InterfaceDescription -like '*Microsoft Wi-Fi Direct Virtual*'} | Select-Object -First 1).Name\"");
                if (!string.IsNullOrWhiteSpace(output?.Trim()))
                {
                    return output.Trim();
                }

                // 方法2：查找状态为Up的"Local Area Connection*"适配器
                output = RunProcessWithOutput("powershell",
                    "-Command \"(Get-NetAdapter | Where-Object {$_.Name -like 'Local Area Connection*' -and $_.Status -eq 'Up'} | Select-Object -First 1).Name\"");
                if (!string.IsNullOrWhiteSpace(output?.Trim()))
                {
                    return output.Trim();
                }

                // 方法3：查找所有虚拟适配器中最新启用的
                output = RunProcessWithOutput("powershell",
                    "-Command \"(Get-NetAdapter | Where-Object {($_.InterfaceDescription -like '*Virtual*' -or $_.InterfaceDescription -like '*Hosted*') -and $_.Status -eq 'Up'} | Sort-Object -Property MediaConnectionState -Descending | Select-Object -First 1).Name\"");
                if (!string.IsNullOrWhiteSpace(output?.Trim()))
                {
                    return output.Trim();
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"查找WiFiDirect虚拟适配器失败: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// 通过PowerShell脚本使用COM INetSharingManager配置ICS
        /// 使用()方法调用和get_显式方法名，等同于在适配器属性→共享里勾选
        /// </summary>
        private bool ConfigureIcsViaPowerShell(string internetAdapterName, string hotspotAdapterName)
        {
            try
            {
                Logger.Info($"通过PowerShell COM配置ICS: 公共={internetAdapterName}, 私有={hotspotAdapterName}");

                // 将脚本写入临时文件执行，避免C#字符串中PowerShell语法的转义问题
                var scriptPath = Path.Combine(Path.GetTempPath(), "configure_ics.ps1");
                File.WriteAllText(scriptPath, GetIcsScript(internetAdapterName, hotspotAdapterName));

                try
                {
                    var output = RunProcessWithOutput("powershell",
                        $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                        60000);

                    Logger.Info($"PowerShell ICS输出:\n{output}");

                    if (output != null && output.Contains("SUCCESS"))
                    {
                        return true;
                    }

                    Logger.Warning($"PowerShell ICS配置可能失败，输出: {output}");
                    return false;
                }
                finally
                {
                    try { File.Delete(scriptPath); } catch { }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"PowerShell COM ICS配置异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 生成ICS配置的PowerShell脚本内容
        /// 使用COM INetSharingManager，按连接名把上行共享到下行（等同勾选ICS）
        /// </summary>
        /// <summary>
        /// 对字符串进行 PowerShell 单引号转义（单引号内出现单引号需替换为两个单引号）
        /// </summary>
        private static string PsEscape(string value)
        {
            return value.Replace("'", "''");
        }

        private static string GetIcsScript(string publicName, string privateName)
        {
            var safePublic = PsEscape(publicName);
            var safePrivate = PsEscape(privateName);

            var sb = new StringBuilder();
            sb.AppendLine("$ErrorActionPreference = 'Stop'");
            sb.AppendLine();
            sb.AppendLine("try");
            sb.AppendLine("{");
            sb.AppendLine("    $sMan = New-Object -ComObject HNetCfg.HNetShare");
            sb.AppendLine("    if ($null -eq $sMan) {");
            sb.AppendLine("        Write-Output 'ERROR: 无法创建HNetCfg.HNetShare COM对象'");
            sb.AppendLine("        exit 1");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    # 枚举所有连接，建立 名称->INetConnection 映射");
            sb.AppendLine("    $cons = $sMan.EnumEveryConnection()");
            sb.AppendLine("    $map = @{}");
            sb.AppendLine("    foreach ($c in $cons) {");
            sb.AppendLine("        try {");
            sb.AppendLine("            $cfg = $sMan.NetConnectionProps($c)");
            sb.AppendLine("            if ($cfg -and $cfg.Name) {");
            sb.AppendLine("                $map[$cfg.Name] = $c");
            sb.AppendLine("                Write-Output \"INFO: 发现连接: $($cfg.Name)\"");
            sb.AppendLine("            }");
            sb.AppendLine("        } catch { }");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine($"    if (-not $map.ContainsKey('{safePublic}')) {{");
            sb.AppendLine($"        Write-Output 'ERROR: 找不到上行连接（互联网适配器）: {safePublic}'");
            sb.AppendLine("        Write-Output 'INFO: 可用连接列表:'");
            sb.AppendLine("        foreach ($key in $map.Keys) {");
            sb.AppendLine("            Write-Output \"  - $key\"");
            sb.AppendLine("        }");
            sb.AppendLine("        exit 1");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine($"    if (-not $map.ContainsKey('{safePrivate}')) {{");
            sb.AppendLine($"        Write-Output 'ERROR: 找不到下行连接（热点适配器）: {safePrivate}'");
            sb.AppendLine("        Write-Output 'INFO: 可用连接列表:'");
            sb.AppendLine("        foreach ($key in $map.Keys) {");
            sb.AppendLine("            Write-Output \"  - $key\"");
            sb.AppendLine("        }");
            sb.AppendLine("        exit 1");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine($"    $pubConn = $map['{safePublic}']");
            sb.AppendLine($"    $priConn = $map['{safePrivate}']");
            sb.AppendLine();
            sb.AppendLine($"    Write-Output 'INFO: 上行连接: {safePublic}'");
            sb.AppendLine($"    Write-Output 'INFO: 下行连接: {safePrivate}'");
            sb.AppendLine();
            sb.AppendLine("    # 取共享配置（使用get_显式方法名）");
            sb.AppendLine("    $pubCfg = $sMan.get_NetSharingConfigurationForINetConnection($pubConn)");
            sb.AppendLine("    $priCfg = $sMan.get_NetSharingConfigurationForINetConnection($priConn)");
            sb.AppendLine();
            sb.AppendLine("    # 检查是否已经正确配置");
            sb.AppendLine("    if ($pubCfg.SharingEnabled -and $pubCfg.SharingConnectionType -eq 0 -and");
            sb.AppendLine("        $priCfg.SharingEnabled -and $priCfg.SharingConnectionType -eq 1) {");
            sb.AppendLine("        Write-Output 'INFO: ICS已正确配置，无需修改'");
            sb.AppendLine("        Write-Output 'SUCCESS: ICS配置完成'");
            sb.AppendLine("        exit 0");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    # 先关旧共享，防止\"已共享给别的接口\"冲突");
            sb.AppendLine("    try { $pubCfg.DisableSharing() } catch { }");
            sb.AppendLine("    Start-Sleep -Milliseconds 800");
            sb.AppendLine();
            sb.AppendLine("    # 开启：上行=Internet(public)，下行=private");
            sb.AppendLine("    # EnableSharing 参数: 0 = PUBLIC (共享来源), 1 = PRIVATE (家庭网络连接)");
            sb.AppendLine("    $pubCfg = $sMan.get_NetSharingConfigurationForINetConnection($pubConn)");
            sb.AppendLine("    $pubCfg.EnableSharing(0)");
            sb.AppendLine("    Write-Output 'INFO: 已启用公共连接共享（上行）'");
            sb.AppendLine();
            sb.AppendLine("    $priCfg = $sMan.get_NetSharingConfigurationForINetConnection($priConn)");
            sb.AppendLine("    $priCfg.EnableSharing(1)");
            sb.AppendLine("    Write-Output 'INFO: 已启用私有连接共享（下行）'");
            sb.AppendLine();
            sb.AppendLine("    # 确保ICS服务运行");
            sb.AppendLine("    Start-Service -Name SharedAccess -ErrorAction SilentlyContinue");
            sb.AppendLine();
            sb.AppendLine("    # 验证");
            sb.AppendLine("    Start-Sleep -Seconds 2");
            sb.AppendLine("    $pubCheck = $sMan.get_NetSharingConfigurationForINetConnection($pubConn)");
            sb.AppendLine("    $priCheck = $sMan.get_NetSharingConfigurationForINetConnection($priConn)");
            sb.AppendLine("    Write-Output \"INFO: 公共连接共享状态: Enabled=$($pubCheck.SharingEnabled), Type=$($pubCheck.SharingConnectionType)\"");
            sb.AppendLine("    Write-Output \"INFO: 私有连接共享状态: Enabled=$($priCheck.SharingEnabled), Type=$($priCheck.SharingConnectionType)\"");
            sb.AppendLine();
            sb.AppendLine("    if ($pubCheck.SharingEnabled -and $priCheck.SharingEnabled) {");
            sb.AppendLine("        Write-Output 'SUCCESS: ICS配置完成'");
            sb.AppendLine("        exit 0");
            sb.AppendLine("    } else {");
            sb.AppendLine("        Write-Output 'ERROR: ICS配置验证失败'");
            sb.AppendLine("        exit 1");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine("catch");
            sb.AppendLine("{");
            sb.AppendLine("    Write-Output \"ERROR: $($_.Exception.Message)\"");
            sb.AppendLine("    exit 1");
            sb.AppendLine("}");
            return sb.ToString();
        }

        /// <summary>
        /// 通过C# COM对象配置ICS（作为PowerShell方式的备选）
        /// 顺序：先禁用公共连接旧共享 → 启用公共(0) → 启用私有(1)
        /// </summary>
        private bool ConfigureIcsViaCom(string? internetAdapterName, string? hotspotAdapterName)
        {
            try
            {
                var type = Type.GetTypeFromProgID("HNetCfg.HNetShare");
                if (type == null)
                {
                    Logger.Warning("无法获取HNetCfg.HNetShare COM对象");
                    return false;
                }

                dynamic? manager = Activator.CreateInstance(type);
                if (manager == null)
                {
                    Logger.Warning("无法创建HNetCfg.HNetShare实例");
                    return false;
                }

                try
                {
                    // 枚举所有连接，建立名称映射
                    var connections = manager.EnumEveryConnection;
                    var nameMap = new Dictionary<string, object>();

                    foreach (var conn in connections)
                    {
                        try
                        {
                            dynamic props = manager.NetConnectionProps[conn];
                            string connName = props.Name;
                            nameMap[connName] = conn;
                            Logger.Info($"发现连接: {connName}");
                        }
                        catch (Exception ex)
                        {
                            Logger.Debug($"处理连接时出错: {ex.Message}");
                        }
                    }

                    if (!nameMap.TryGetValue(internetAdapterName!, out var publicConnection))
                    {
                        Logger.Error($"C# COM: 找不到上行连接: {internetAdapterName}");
                        return false;
                    }

                    if (!nameMap.TryGetValue(hotspotAdapterName!, out var privateConnection))
                    {
                        Logger.Error($"C# COM: 找不到下行连接: {hotspotAdapterName}");
                        return false;
                    }

                    Logger.Info($"上行连接: {internetAdapterName}");
                    Logger.Info($"下行连接: {hotspotAdapterName}");

                    // 取共享配置
                    dynamic pubConfig = manager.INetSharingConfigurationForINetConnection[publicConnection];
                    dynamic privConfig = manager.INetSharingConfigurationForINetConnection[privateConnection];

                    // 检查是否已经正确配置
                    if (pubConfig.SharingEnabled && pubConfig.SharingConnectionType == 0 &&
                        privConfig.SharingEnabled && privConfig.SharingConnectionType == 1)
                    {
                        Logger.Info("ICS已正确配置，无需修改");
                        return true;
                    }

                    // 先关旧共享，防止"已共享给别的接口"冲突
                    try { pubConfig.DisableSharing(); } catch { }
                    System.Threading.Thread.Sleep(800);

                    // 重新获取配置（禁用后可能需要重新获取）
                    pubConfig = manager.INetSharingConfigurationForINetConnection[publicConnection];
                    privConfig = manager.INetSharingConfigurationForINetConnection[privateConnection];

                    // 启用公共连接共享（0 = PUBLIC，共享来源）
                    pubConfig.EnableSharing(0);
                    Logger.Info("已启用公共连接共享（上行）");

                    // 启用私有连接共享（1 = PRIVATE，家庭网络连接）
                    privConfig.EnableSharing(1);
                    Logger.Info("已启用私有连接共享（下行）");

                    return true;
                }
                finally
                {
                    Marshal.ReleaseComObject(manager);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"C# COM ICS配置失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取互联网适配器名称
        /// </summary>
        private string? GetInternetAdapterInfo()
        {
            try
            {
                var profile = NetworkInformation.GetInternetConnectionProfile();
                if (profile != null)
                {
                    var adapterId = profile.NetworkAdapter?.NetworkAdapterId.ToString();
                    if (adapterId != null)
                    {
                        var output = RunProcessWithOutput("powershell",
                            $"-Command \"(Get-NetAdapter | Where-Object {{$_.InterfaceGuid -eq '{adapterId}'}}).Name\"");
                        if (!string.IsNullOrEmpty(output))
                        {
                            return output.Trim();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"获取互联网适配器信息失败: {ex.Message}");
            }
            return null;
        }

        private string? RunProcessWithOutput(string fileName, string arguments, int timeoutMs = 10000)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(timeoutMs);
                    return output;
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"执行命令失败: {fileName} {arguments}, 错误: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// 执行PowerShell脚本（使用Base64编码方式，避免引号转义问题）
        /// </summary>
        private string? RunPowerShellScript(string script, int timeoutMs = 30000)
        {
            try
            {
                // 将脚本编码为Base64，使用-EncodedCommand参数传递
                // 这样可以避免引号转义、特殊字符等问题
                var bytes = System.Text.Encoding.Unicode.GetBytes(script);
                var encodedCommand = Convert.ToBase64String(bytes);

                return RunProcessWithOutput("powershell",
                    $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}",
                    timeoutMs);
            }
            catch (Exception ex)
            {
                Logger.Error($"执行PowerShell脚本失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 获取当前有互联网连接的适配器名称
        /// </summary>
        private string? GetInternetAdapterName()
        {
            try
            {
                // 方法1：通过NetworkInformation获取互联网连接配置文件
                var profile = NetworkInformation.GetInternetConnectionProfile();
                if (profile != null)
                {
                    var adapterId = profile.NetworkAdapter?.NetworkAdapterId.ToString();
                    if (adapterId != null)
                    {
                        var output = RunProcessWithOutput("powershell",
                            $"-Command \"(Get-NetAdapter | Where-Object {{$_.InterfaceGuid -eq '{adapterId}'}}).Name\"");
                        if (!string.IsNullOrWhiteSpace(output?.Trim()))
                        {
                            var name = output.Trim();
                            // 排除Wi-Fi Direct虚拟适配器（不能把热点适配器当成上行）
                            if (!name.Contains("Wi-Fi Direct", StringComparison.OrdinalIgnoreCase) &&
                                !name.Contains("Local Area Connection", StringComparison.OrdinalIgnoreCase))
                            {
                                Logger.Info($"通过InternetConnectionProfile找到互联网适配器: {name}");
                                return name;
                            }
                            Logger.Info($"跳过Wi-Fi Direct虚拟适配器: {name}");
                        }
                    }
                }

                // 方法2：通过PowerShell直接查找有默认网关且状态为Up的适配器
                Logger.Info("方法1失败，尝试通过默认网关查找互联网适配器");
                var gwOutput = RunProcessWithOutput("powershell",
                    "-Command \"(Get-NetRoute -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue | Sort-Object RouteMetric | Select-Object -First 1 | Get-NetAdapter).Name\"");
                if (!string.IsNullOrWhiteSpace(gwOutput?.Trim()))
                {
                    var name = gwOutput.Trim();
                    if (!name.Contains("Wi-Fi Direct", StringComparison.OrdinalIgnoreCase) &&
                        !name.Contains("Local Area Connection", StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.Info($"通过默认网关找到互联网适配器: {name}");
                        return name;
                    }
                }

                // 方法3：查找状态为Up的非虚拟适配器
                Logger.Info("方法2失败，尝试查找状态为Up的非虚拟适配器");
                var upOutput = RunProcessWithOutput("powershell",
                    "-Command \"(Get-NetAdapter | Where-Object {$_.Status -eq 'Up' -and $_.InterfaceDescription -notlike '*Wi-Fi Direct*' -and $_.InterfaceDescription -notlike '*Virtual*' -and $_.Name -notlike 'Local Area Connection*'} | Select-Object -First 1).Name\"");
                if (!string.IsNullOrWhiteSpace(upOutput?.Trim()))
                {
                    Logger.Info($"通过状态查找找到互联网适配器: {upOutput.Trim()}");
                    return upOutput.Trim();
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"获取互联网适配器名称失败: {ex.Message}");
            }

            return null;
        }

        private void DisableInternetSharing()
        {
            try
            {
                Logger.Info("正在禁用Internet连接共享...");

                var type = Type.GetTypeFromProgID("HNetCfg.HNetShare");
                if (type != null)
                {
                    dynamic? manager = Activator.CreateInstance(type);
                    if (manager != null)
                    {
                        try
                        {
                            var connections = manager.EnumEveryConnection;
                            foreach (var conn in connections)
                            {
                                try
                                {
                                    dynamic sharingConfig = manager.INetSharingConfigurationForINetConnection[conn];
                                    if (sharingConfig.SharingEnabled)
                                    {
                                        sharingConfig.DisableSharing();
                                        Logger.Info("已禁用Internet连接共享");
                                    }
                                }
                                catch
                                {
                                    // 忽略
                                }
                            }
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(manager);
                        }
                    }
                }

                // 不停止SharedAccess服务，避免影响系统原生移动热点等其他共享功能

                Logger.Info("Internet连接共享已禁用");
            }
            catch (Exception ex)
            {
                Logger.Error("禁用Internet连接共享失败", ex);
            }
        }

        #endregion
    }
}
