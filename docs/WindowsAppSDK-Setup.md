# Windows App SDK 安装指南 / Windows App SDK Installation Guide

## 🚀 快速安装 / Quick Installation

### 方法1: Microsoft Store 安装 / Method 1: Microsoft Store Installation

1. 打开 Microsoft Store / Open Microsoft Store
2. 搜索 "Windows App Runtime" / Search for "Windows App Runtime"
3. 安装最新版本 (1.6+) / Install latest version (1.6+)

### 方法2: 命令行安装 / Method 2: Command Line Installation

```powershell
# 使用 winget 安装 / Install using winget
winget install Microsoft.WindowsAppRuntime.1.6

# 或者下载并安装 MSI 包 / Or download and install MSI package
# 从以下链接下载: / Download from:
# https://aka.ms/windowsappsdk/1.6/latest/windowsappruntimeinstall-x64.exe
```

### 方法3: 手动下载安装 / Method 3: Manual Download Installation

1. 访问 / Visit: https://github.com/microsoft/WindowsAppSDK/releases
2. 下载最新的 WindowsAppRuntimeInstall-x64.exe / Download latest WindowsAppRuntimeInstall-x64.exe
3. 以管理员身份运行安装程序 / Run installer as administrator

## ✅ 验证安装 / Verify Installation

```powershell
# 检查已安装的 Windows App Runtime 版本 / Check installed Windows App Runtime version
Get-AppxPackage -Name "*WindowsAppRuntime*"

# 或者检查注册表 / Or check registry
Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\WindowsAppRuntime" -ErrorAction SilentlyContinue
```

## 🔧 故障排除 / Troubleshooting

### 常见错误 / Common Errors

1. **COM Exception (0x80040154)**
   ```
   原因: Windows App Runtime 未正确安装或注册
   解决方案:
   1. 重新安装 Windows App Runtime
   2. 重启计算机
   3. 以管理员身份运行应用程序
   ```

2. **找不到启动配置文件 / Launch Profile Not Found**
   ```
   原因: MAUI 项目配置问题
   解决方案:
   1. 确保 launchSettings.json 配置正确
   2. 使用 dotnet run 直接运行
   3. 检查目标框架是否正确
   ```

## 🏃‍♂️ 运行 AgenticAI.Desktop / Running AgenticAI.Desktop

```bash
# 进入项目目录 / Navigate to project directory
cd "c:\Users\phoen\Documents\wind\AgenticAI.Desktop\src\1.Presentation\AgenticAI.Desktop.MAUI"

# 运行应用程序 / Run application
dotnet run --framework net9.0-windows10.0.19041.0

# 或者发布后运行 / Or run after publishing
dotnet publish --framework net9.0-windows10.0.19041.0 --configuration Release --output ./publish
./publish/AgenticAI.Desktop.MAUI.exe
```

## 📋 系统要求检查清单 / System Requirements Checklist

- [ ] Windows 10 版本 1903 或更高 / Windows 10 version 1903 or higher
- [ ] .NET 9.0 SDK 已安装 / .NET 9.0 SDK installed
- [ ] Windows App Runtime 1.6+ 已安装 / Windows App Runtime 1.6+ installed
- [ ] WebView2 Runtime 已安装 / WebView2 Runtime installed
- [ ] Visual C++ Redistributable 2015-2022 已安装 / Visual C++ Redistributable 2015-2022 installed

## 🆘 获取帮助 / Getting Help

如果仍然遇到问题，请：
1. 检查 Windows 更新 / Check Windows Updates
2. 重启计算机 / Restart computer
3. 以管理员身份运行 / Run as administrator
4. 查看应用程序事件日志 / Check Application Event Logs

**If you still encounter issues, please:**
1. **Check Windows Updates**
2. **Restart computer**
3. **Run as administrator**
4. **Check Application Event Logs**
