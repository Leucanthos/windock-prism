# WinDock

macOS 风格桌面 Dock，玻璃质感图标 + 动态窗口同步。

## 项目结构

```
dock/
├── App.cs              # 入口：DPI 感知、互斥锁、自动杀旧实例
├── TestIcon.cs         # 单图标测试（独立入口，可单独编译运行）
├── Windock.ico         # 托盘图标
├── Common/
│   ├── Theme.cs        # 主题：颜色、字体、磨砂玻璃纹理渲染（Hash 噪声算法）
│   └── W.cs            # 工具：DWM 圆角、Label 工厂、窗口拖动、Mutex
├── Core/
│   ├── DockBar.cs      # 基础层：窗口枚举、可见性判断、焦点/关闭、标题获取
│   └── DockLine.cs     # 引擎：动态图标管理、布局、任务栏切换、固定应用同步
├── UI/
│   ├── DockIcon.cs     # 单图标组件：放大动画、徽章、Tooltip、右键回调
│   └── GlassMenu.cs    # 右键菜单弹出层（占位，待重新设计）
└── README.md
```

## 核心实现

### DPI 感知
- `SetProcessDPIAware()` 在 `Main()` 最开头调用
- `GetDeviceCaps(dc, LOGPIXELSX)` 获取物理 DPI
- 所有尺寸按 `logicalSize * DpiX / 96f` 缩放
- 修复 WinForms DPI 自动缩放：`AutoScaleMode.None` + `SetWindowPos` 在 `HandleCreated` 强制设尺寸

### 动态图标窗口
- 每个图标是独立的无边框 Form（玻璃瓦片），内含 PictureBox
- DWM 圆角（`DwmSetWindowAttribute` + `DWMWA_WINDOW_CORNER_PREFERENCE`）
- 放大动画：16ms Timer，`curScale` 向 `targetScale` 做 0.2 lerp
- `ApplyScale()` 通过 `SetWindowPos` 更新位置/尺寸，基准坐标 `BaseX` 不变

### 窗口枚举
- `GetTopWindow` + `GetWindow(GW_HWNDNEXT)` 遍历所有顶层窗口
- 过滤规则：
  - `WS_EX_TOOLWINDOW` → 跳过
  - 空标题 → 跳过
  - Widget 窗口（TopBar, System, Disk, Network, Battery, Recycle Bin, WiFi Panel, Audio）→ 跳过
  - 系统进程（explorer, searchapp, textinputhost, shellexperiencehost, clicktodo）→ 跳过

### 固定应用同步
- 每 2.5s 扫描 `%APPDATA%\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar\*.lnk`
- COM `WScript.Shell.CreateShortcut()` 解析目标路径
- 三级匹配合并（防止重复图标）：
  1. 精确文件路径匹配
  2. 精确进程名匹配
  3. 进程名 StartsWith 匹配（解决 Steam → steamwebhelper）
- 取消固定时回收旧图标：`FindOrCreateRunning` 检查 `Pinned=true` 但 `PinPath` 已不在 `pinnedPaths` 中的图标

### 任务栏切换
- `FindWindow("Shell_TrayWnd")` + `ShowWindow(SW_HIDE/SW_SHOW)`
- 点击 V 形最小化图标 → 隐藏 Dock / 显示任务栏 / 系统托盘显示图标
- 双击托盘图标 → 隐藏任务栏 / 显示 Dock
- `FormClosed` 中无条件恢复任务栏（防止崩溃后任务栏永久消失）

### 主题
- 2s 定时器轮询注册表 `SystemUsesLightTheme`（替代不可靠的 `SystemEvents.UserPreferenceChanged`）
- 玻璃纹理：`RenderGlass()` 用 Hash 噪声 + 边缘光泽/阴影 + 高光生成 800×100 位图
- 暗色模式：深海军蓝底 + 蓝色发光连接线
- 亮色模式：暖米色底 + 金色暖光连接线

### 徽章
- GDI+ 直接画在 PictureBox 的 Paint 事件上
- 圆形 + 数字，随 `curScale` 缩放

## 已知问题
- 右键菜单（GlassMenu）视觉效果不理想，需要专门设计（参考 widgets 项目风格）
- 托盘图标在 Windows 隐藏区域（^ 溢出区）
- 未实现：单像素级透明度控制、独立图标透明度

## 编译

需要 .NET Framework 4.x（C# 编译器）：

```powershell
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" `
  -target:winexe -out:WinDock.exe `
  -reference:System.Windows.Forms.dll `
  -reference:System.Drawing.dll `
  -win32icon:Windock.ico `
  App.cs Core\DockLine.cs Core\DockBar.cs UI\DockIcon.cs UI\GlassMenu.cs Common\Theme.cs Common\W.cs
```
