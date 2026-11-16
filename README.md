# 轻盒鼠标操作录制器

轻盒工具箱的 Windows 桌面连点器，可录制鼠标点击位置及操作间隔，并按顺序重复执行。它是免安装的单文件 Windows 程序，源码与当前下载版本一并提供。

## 下载使用

在仓库的 **Releases** 下载最新 Windows 版本；当前版本也可从仓库文件列表下载 `LightboxClicker-20261104-r5.exe`。下载后双击运行。程序未经代码签名，Windows 可能显示未知发布者提示；运行前请先检查源码并确认文件来源。

## 功能

- 录制左键、右键和中键点击，自动编号并在桌面显示位置标记。
- 保存、载入 `.lbx` 操作流程；默认目录为 `%APPDATA%\LightboxToolbox\Recordings`。
- 按记录的每步间隔或固定间隔重放，可设置重复轮数。
- 全局快捷键控制重放、紧急停止、添加点位、录制和显示/隐藏准星。
- 自定义快捷键、准星颜色与尺寸、编号颜色及默认操作参数。
- 支持按比例调整主窗口大小。

## 默认快捷键

| 快捷键 | 操作 |
| --- | --- |
| F6 | 开始/停止重放 |
| F7 | 紧急停止 |
| F8 | 手动添加当前位置左键点位 |
| F9 | 开始/停止录制 |
| F10 | 显示/隐藏桌面准星 |

快捷键可以在“自定义”设置中更改。

## 从源码编译

要求 Windows 和 .NET Framework 4.x 的 C# 编译器。PowerShell 在仓库目录执行：

```powershell
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /optimize+ /out:LightboxClicker.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /resource:main-mascot.png,LightboxDesktopClicker.main-mascot.png /resource:settings-mascot.png,LightboxDesktopClicker.settings-mascot.png LightboxClicker.cs
```

## 安全卸载

程序不会安装系统服务或卸载器。先停止录制/重放并关闭程序，然后删除下载目录中的 EXE 即可。设置和录制文件默认保留在 `%APPDATA%\LightboxToolbox`。如需一并清除，请先备份 `Recordings` 中要保留的 `.lbx` 文件，再在资源管理器地址栏输入 `%APPDATA%` 并删除 `LightboxToolbox` 文件夹。

## 使用提醒

- 点击操作会发送到录制的位置。请先以少量轮数、较长间隔试运行，避免在支付、删除、发送等重要操作界面使用。
- 程序不会提权，也不绕过反作弊或安全软件。某些独占全屏游戏或受保护窗口可能不接受模拟输入；请遵守目标应用规则。
- 项目使用 Windows WinForms 和 Win32 输入接口。源码可供检查；发布的 EXE 未进行代码签名。
