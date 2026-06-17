<div align="center">

# Advanced Windows Hotspot

**一个基于现代 UI（Material 3）的 Windows 高级热点管理工具**

**An advanced Windows hotspot management tool with modern Material 3 UI**

![License: GPL-3.0](https://img.shields.io/badge/License-GPLv3-blue.svg)
![.NET Version](https://img.shields.io/badge/.NET-8.0%20WPF-purple)
![Platform](https://img.shields.io/badge/Platform-Windows-lightgrey)

</div>

---

## 📖 简介 | Introduction

**English:**  
Advanced Windows Hotspot is a WPF application that provides powerful Wi-Fi hotspot capabilities on Windows. It supports two hotspot modes — **WiFi Direct** and **System Tethering** — and features automatic Internet Connection Sharing (ICS) configuration, 5 GHz band detection, and a modern Google Material 3 user interface.

**简体中文：**  
Advanced Windows Hotspot 是一款 WPF 桌面应用，为 Windows 系统提供强大的 Wi-Fi 热点功能。它支持两种热点模式——**WiFi Direct** 和**系统热点**——具备自动 Internet 连接共享（ICS）配置、5 GHz 频段检测和现代化的 Google Material 3 用户界面。

---

## ✨ 功能特点 | Features

### 🚀 双热点模式 | Dual Hotspot Modes

| 模式 | 说明 | 特点 |
|------|------|------|
| **WiFi Direct 模式** | 通过 WiFi Direct API 创建热点 | 无需网络连接、独立于系统移动热点、不支持频段选择 |
| **系统热点模式** | 通过 `NetworkOperatorTetheringManager` API 创建 | 需要网络连接、支持 2.4G/5G 频段选择、原生网络共享 |

### 🔌 Internet 连接共享 | Internet Connection Sharing

- **网络适配器下拉选择** — 用户可手动选择用于 Internet 连接共享的上行网卡（Wi-Fi / 以太网）
- 自动检测上行网卡，通过 **COM INetSharingManager** API 自动配置 ICS
- 等效于在网卡属性 → 共享中勾选「允许其他网络用户通过此计算机的 Internet 连接来连接」
- 支持手动启用/禁用网络共享，运行中切换"允许通过本机联网"立即生效
- **WiFi Direct 模式下，设备连接后自动触发 ICS 配置**
- **以太网（有线网络）上行网卡完整支持**

### 📡 频段检测 | Band Detection

- 三种并行方式检测 5 GHz 支持：`netsh wlan show wirelesscapabilities`、`netsh wlan show driver`、TetheringManager API
- 智能策略：选择 2.4G/5G 频段时自动切换至系统热点模式（WiFi Direct 不支持频段选择）
- 频段不兼容时给出明确错误提示

### 🎨 Material 3 UI

- Google Material 3 设计语言（紫调配色）
- 无边框窗口设计，自带圆角阴影效果
- 密码可见性切换
- 窗口位置/大小自动记忆

### ⚙️ 其他功能 | Other Features

- 设置自动持久化（SSID、密码、频段、开关选项）
- 文件日志系统（自动滚动、大小限制、最多保留 5 个日志文件）
- 支持 Windows 7+（管理员权限运行）

---

## 🖼️ 界面预览 | UI Preview

```
┌─────────────────────────────────────────┐
│  🚀 Advanced Windows Hotspot  ─ □ ✕    │
├─────────────────────────────────────────┤
│  ● 就绪                                 │
│  ┌─────────────────────────────────┐    │
│  │ 网络名称                         │    │
│  │ 输入热点名称                     │    │
│  ├─────────────────────────────────┤    │
│  │ 密码                             │    │
│  │ 输入密码 (8-63位)           👁️   │    │
│  ├─────────────────────────────────┤    │
│  │ 密码长度需为8-63个字符           │    │
│  ├─────────────────────────────────┤    │
│  │ 频段                    [自动 ▼]│    │
│  ├─────────────────────────────────┤    │
│  │ [☑] 允许通过本机联网              │    │
│  ├─────────────────────────────────┤    │
│  │ [☐] 使用系统热点                  │    │
│  ├─────────────────────────────────┤    │
│  │      [    启动热点    ]           │    │
│  ├─────────────────────────────────┤    │
│  │ 日志目录: ...  │ 2.4GHz / 5GHz │    │
│  └─────────────────────────────────┘    │
└─────────────────────────────────────────┘
```

---

## 📋 系统要求 | Requirements

| 要求 | 说明 |
|------|------|
| **操作系统** | Windows 10 1809+ / Windows 11 |
| **运行时** | 无需额外安装（自包含发布） |
| **权限** | **管理员权限**（必需，用于 ICS 配置和热点管理） |
| **网卡** | 支持 WiFi Direct 或移动热点的无线网卡 |

---

## 🚀 快速开始 | Quick Start

### 下载安装 | Download & Install

1. 前往 [Releases](https://github.com/sterger5/Advanced-Windows-Hotspot/releases) 页面下载最新版安装程序
2. 以**管理员身份**运行安装程序
3. 安装完成后，从桌面或开始菜单启动应用

> **注意：** 安装包为自包含发布，无需额外安装 .NET 运行时。

### 从源码构建 | Build from Source

```bash
# 克隆仓库
git clone https://github.com/sterger5/Advanced-Windows-Hotspot.git
cd Advanced-Windows-Hotspot

# 构建
dotnet build AdvancedWindowsHotspot.csproj

# 以管理员身份运行（必须）
# Run as administrator (required)
.\bin\Debug\net8.0-windows10.0.26100.0\AdvancedWindowsHotspot.exe
```

> **注意：** 请务必以**管理员身份**运行此应用，否则热点和 ICS 功能将无法正常工作。

> **Note:** This application **MUST** be run as **Administrator**, otherwise hotspot and ICS features will not work.

---

## 🎮 使用指南 | Usage Guide

### 基本操作 | Basic Operation

1. **输入热点名称** — 设置 Wi-Fi 网络的 SSID（最多 32 个字符）
2. **输入密码** — 设置 8-63 位的网络密码
3. **选择频段** — 可选自动、2.4 GHz、5 GHz
4. **允许联网** — 开启后连接热点的设备可通过本机上网（运行中切换立即生效）
5. **选择共享来源** — 在下拉框中选择用于 Internet 连接共享的上行网卡
6. **使用系统热点** — 切换至 Windows 原生移动热点模式（支持频段选择）
7. **点击启动/停止** — 开始或停止热点

### 提示 | Tips

- **WiFi Direct 模式**（默认）：无需网络连接即可创建热点，适合无网络环境使用。设备连接后自动配置 ICS 上网。如自动配置失败，可在网络共享区域选择正确的上行网卡后点击「启用网络共享」手动配置。
- **系统热点模式**：通过 Windows 移动热点创建，支持频段选择，需要网络连接。支持以太网（有线）和 Wi-Fi 上行网卡。
- 日志文件位于 `%APPDATA%\AdvancedWindowsHotspot\logs\`，方便排查问题。

---

## 🏗️ 项目架构 | Project Architecture

```
AdvancedWindowsHotspot/
├── App.xaml                 # 应用入口 + Material 3 样式资源
├── App.xaml.cs              # 异常处理和日志初始化
├── MainWindow.xaml          # Material 3 主界面
├── MainWindow.xaml.cs       # 窗口行为（拖拽、记忆位置）
├── app.manifest             # 管理员权限声明
├── nuget.config             # NuGet 源配置
├── Converters/
│   └── Converters.cs        # 值转换器
├── Models/
│   └── HotspotSettings.cs   # 设置模型 + 枚举定义
├── Services/
│   ├── HotspotService.cs    # 核心服务（热点管理 + ICS 配置）
│   └── Logger.cs            # 文件日志（自动滚动）
└── ViewModels/
    ├── MainViewModel.cs     # 主视图模型
    ├── RelayCommand.cs      # ICommand 实现
    └── ViewModelBase.cs     # MVVM 基类
```

### 核心技术栈 | Tech Stack

| 技术 | 用途 |
|------|------|
| **WPF / .NET 8.0** | 桌面应用框架 |
| **WiFiDirectAdvertisementPublisher** | WiFi Direct 热点创建 |
| **NetworkOperatorTetheringManager** | 系统热点管理 |
| **HNetCfg.HNetShare COM** | Internet 连接共享 (ICS) |
| **MVVM 模式** | UI 与业务逻辑分离 |
| **Material 3** | 用户界面设计 |

---

## 🔧 技术细节 | Technical Details

### WiFi Direct 模式工作原理

1. 创建 `WiFiDirectAdvertisementPublisher`，设置 SSID 和密码
2. 设置 `IsAutonomousGroupOwnerEnabled = true`（设备作为接入点）
3. 启用 `LegacySettings`，使普通 Wi-Fi 设备也能连接
4. 启动连接监听器 `WiFiDirectConnectionListener`
5. 设备连接后，使用 COM INetSharingManager 配置 ICS

### ICS 配置流程

1. 启动 `SharedAccess` 服务
2. 查找 WiFi Direct 虚拟适配器（最多重试 5 次，每次间隔 3 秒）
3. 通过 `NetworkInformation.GetInternetConnectionProfile()` 获取当前联网适配器
4. 使用 PowerShell COM 脚本调用 `HNetCfg.HNetShare`：
   - 枚举所有网络连接
   - 禁用旧的共享配置
   - 对上行网卡调用 `EnableSharing(0)`（公共连接）
   - 对热点适配器调用 `EnableSharing(1)`（私有连接）
   - 验证共享状态

### 频段检测

三种检测方式并行执行（15 秒超时）：
- `netsh wlan show wirelesscapabilities`
- `netsh wlan show driver`
- `NetworkOperatorTetheringManager.IsBandSupported()`

---

## 📄 许可证 | License

本项目基于 **GNU General Public License v3.0** 开源许可证发布。

This project is licensed under the **GNU General Public License v3.0**.

```
Copyright (C) 2024 sterger5

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
```

---

## 🙏 致谢 | Acknowledgements

- [NoInternetHotspot](https://github.com/sterger5/NoInternetHotspot) — 项目参考灵感（WiFi Direct 实现思路）
- Google Material Design 3 — UI 设计参考

---

<div align="center">

**Made with ❤️ by sterger5**

</div>