# Index

Windows 原生截图、标注与本地图片资料库。

**冻结屏幕 → 框选 → 标注 → 复制 / 保存 / 钉图 / 上传**

## 功能

- **冻结截图**：多显示器画面一次冻结，框选与标注始终对应同一帧
- **标注编辑器**：统一 Tool Palette（画笔、矩形、椭圆、箭头、文字、尺寸标注等），原图不可变，修订可回溯
- **内容库**：截图、录屏、剪切板统一入口，支持 OCR、标签、收藏与搜索
- **钉图**：截图钉在桌面，支持缩放、拖拽、置顶
- **虚拟 4K 窗口**：创建虚拟高分辨率窗口用于截图
- **分子识别**：识别截图中的化学分子结构（接口已就位，后续接入 API）
- **结构化剪贴板**：复制颜色、URL、文本、图片，粘贴时自动识别类型

## 构建

要求 Windows 11（build 26100+）与 .NET 9 SDK。

```powershell
dotnet restore src/Index.sln
dotnet build src/Index.sln --configuration Release
dotnet test src/Index.Tests/Index.Tests.csproj --no-restore
```

运行：

```powershell
dotnet run --project src/Index/Index.csproj
```

发布安装程序：

```powershell
dotnet publish src/Index/Index.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained false `
  --output artifacts/windows-x64 `
  -p:WindowsAppSDKSelfContained=true `
  -p:PublishSingleFile=false

& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" `
  "/DAppVersion=0.0.15" `
  "/DSourceDir=$PWD\artifacts\windows-x64" `
  "/DOutputDir=$PWD\installer\output" `
  installer\Index.iss
```

## 架构

```
Index (WinUI 可执行)
  → Index.Windows (Windows 基础设施适配器)
    → Index.Core (领域与应用边界，无平台 UI 依赖)
```

- **Index.Core**：领域模型、OCR/识别插件接口、渲染、存储、导航状态
- **Index.Windows**：Win32 适配器、SQLite、HTTP client、系统 OCR
- **Index**：WinUI 装配、截图覆盖层、编辑器工作区、Pin 窗口、Ketcher 分子画布

## 技术栈

- .NET 9、C#、WinUI 3、Windows App SDK
- SkiaSharp（分子渲染）
- SQLite（本地存储）
- WebView2（Ketcher 分子编辑器，过渡方案）
- Inno Setup（安装程序）

## 文档

[架构](ARCHITECTURE.md) · [隐私](PRIVACY.md) · [安全](SECURITY.md) · [第三方声明](THIRD_PARTY_NOTICES.md)

## 许可

[GNU General Public License v3.0 or later](LICENSE)（`GPL-3.0-or-later`）
