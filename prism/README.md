# Desktop Widgets

桌面小工具集合，单 EXE（Prism.exe）运行，7 个面板覆盖时钟/主题/系统监控/磁盘/网络/WiFi/音频/回收站。

## 架构

```
Common/
├── W.cs                   # 公共工具集 (主题/圆角/拖拽/进度条/互斥)
└── Settings.cs            # INI 配置解析
Components/
├── TopBarWidget.cs        # 时钟 + 日夜切换
├── SystemWidget.cs        # CPU/RAM/GPU/SWAP/NPU
├── DiskWidget.cs          # C:/D: 磁盘用量
├── NetworkWidget.cs       # WiFi/公网IP/上下行速率
├── WiFiWidget.cs          # 附近WiFi扫描 + 连接
└── RecycleBinWidget.cs    # 回收站 (拖放/清空/桌面图标)
Prism.cs                   # 入口 Program.Main()
```

## 编译

```powershell
csc /target:winexe /out:Prism.exe \
    /reference:System.Windows.Forms.dll \
    /reference:System.Drawing.dll \
    /reference:Microsoft.CSharp.dll \
    /reference:System.Management.dll \
    /reference:native\OpenHardwareMonitorLib.dll \
    Common\*.cs Components\*.cs Prism.cs
```

## 核心设计

### 1. 主题事件传播

TopBar 切换 Dark/Light → `Program.NotifyTheme(light)` → 所有 Widget 的 `ThemeChanged` 事件处理器统一刷新颜色。

避免了多进程间的文件/注册表轮询延迟。

### 2. 公共工具 `W` 类

| 方法 | 用途 |
|------|------|
| `W.Round(f)` | DWM 圆角 (Win11) |
| `W.Lock(name)` | 进程互斥锁 |
| `W.Lbl(...)` | 快速创建 Label |
| `W.Bar(fg,w,h,x,y)` | 自定义进度条 (嵌套 Panel, 无动画闪烁) |
| `W.BarSet(bar,pct)` | 更新进度条宽度 |
| `W.MakeDraggable(f, extras)` | 窗口拖拽 |
| `W.InitTheme()` / `W.ThemeForm()` | 主题初始化/应用 |

### 3. 字体驱动的布局

使用 `TextRenderer.MeasureText("X", font)` 计算文字实际渲染高度，行间距 = `font.Size * 0.25`，确保任何字号变化都能自适应。

### 4. WiFi 面板

使用 `Show()`/`Hide()` 模式（不销毁重建），通过 `FormClosing` 事件拦截关闭动作改为隐藏。
扫描直接调用 `netsh wlan show networks mode=bssid`（`cmd /c chcp 65001` 强制 UTF-8 解决中文"信号"标签问题），输出到 `C:\temp\_wifi_scan.txt` 后 C# 直接解析。
当前 SSID 通过 `netsh wlan show interfaces` 直接获取。**零 PowerShell 依赖。**
内置密码输入面板和 WLAN 配置文件生成。

### 5. 自定义进度条

放弃 WinForms ProgressBar（有自带闪光动画），改用两个嵌套 Panel：
- 外层 Panel (背景色 = 进度条底色)
- 内层 Panel (背景色 = 金色/蓝色, Width 按百分比缩放)

## 已知优化项

### 低优先级
- **字体资源释放**: `new Font(...)` 创建的字体未调用 `Dispose()`。由于字体在整个进程生命周期中使用，影响极小。
- **PerformanceCounter 生命周期**: 部分 Counter 在异常时泄漏句柄。
- **RecycleBin `dynamic`**: COM 对象使用 `dynamic` 而非类型化接口，有 0.1ms 级性能折损。

### 中优先级
- **路径硬编码**: 壁纸路径写死为相对路径。如果移动项目文件夹需手动修改。
- **分离线(1px Panel)未存储引用**: 主题切换时这些线不会更新颜色（影响较小，线色对比度始终足够）。

### 高优先级
- **TopBar 主题代码重复**: TopBar 有自己的 `SetTheme()` 和颜色更新逻辑，与 `W.ThemeForm()` 不共享。如果后续增加新主题色方案需要改两处。
- **WiFi 面板不响应主题切换**: `WiFiWidget` 未订阅 `Program.ThemeChanged`。
- **位置硬编码**: X=1426 等值写死为 1646px 屏幕宽度。如果更换显示器需要改动。
- **`Screen.PrimaryScreen.WorkingArea.Width` 在部分 Widget 的 Form 初始化中返回 0**：WinForms 的 `Screen` 类在 `Application.Run` 之前可能未就绪。

## 文件结构

```
desktop-widgets/
├── Prism.cs            # 入口
├── README.md           # 本文档
├── settings.ini        # 配置文件
├── Common/
│   ├── Theme.cs        # 主题系统
│   ├── W.cs            # 公共工具
│   └── Settings.cs     # INI 配置
├── Components/
│   ├── TopBarWidget.cs
│   ├── SystemWidget.cs
│   ├── DiskWidget.cs
│   ├── NetworkWidget.cs
│   ├── WiFiWidget.cs
│   ├── BatteryWidget.cs
│   ├── RecycleBinWidget.cs
│   └── AudioWidget.cs
├── test/
│   ├── pipeline.ps1    # 一键：编译 + 全量静态分析
│   ├── test.ps1        # 冒烟测试（编译+启动+窗口检测）
│   ├── perf.ps1        # UI 卡顿检测（Thread.Sleep 等）
│   ├── security.ps1    # 安全合规（明文密码/HTTP）
│   ├── resources.ps1   # 资源泄漏（Font GDI 句柄）
│   ├── artifacts.ps1   # 调试残留（DumpGlassDebug 等）
│   └── codecheck.ps1   # 代码质量（过时 API/硬编码）
├── native/
│   └── OpenHardwareMonitorLib.dll
└── assets/
    ├── wallpaper-day.png
    └── wallpaper-night.png
```

## 测试

```powershell
.\test\pipeline.ps1     # 一键：编译 + 冒烟 + 全量静态分析
.\test\test.ps1         # 仅冒烟（编译→启动→窗口检测→稳定性）
.\test\perf.ps1         # 性能：Thread.Sleep / UI 卡顿
.\test\security.ps1     # 安全：明文密码 / HTTP 明文
.\test\resources.ps1    # 资源：Font GDI 泄漏
.\test\artifacts.ps1    # 代码清理：调试残留
.\test\codecheck.ps1    # 质量：过时 API / 硬编码
```

每个脚本独立可运行，pipeline 聚合全部结果。Exit 0 = 通过，非 0 = 发现问题。

## 自启动

Prism 首次运行时自动注册到 `HKLM\...\Run`（系统级，早于 HKCU）。

```
HKLM\Software\Microsoft\Windows\CurrentVersion\Run
    Prism = d:\01-Personal\Projects\desktop-widgets\Prism.exe
```
