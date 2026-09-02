# Index Windows Agent Guide

本文件适用于整个仓库。Index 当前工作目标是 Windows 11 x64 上的原生截图、图库与分子识别应用。
修改前先阅读根目录 `README.md`、`ARCHITECTURE.md` 和相关模块的 `README.md`；真实实现、当前测试和
Windows 用户需求优先于历史 macOS 文档中的旧方案。

## 1. 平台与技术基线

- 只实现和验证 Windows，不新增 macOS/Linux 条件分支。
- 主应用使用 .NET 9、C#、WinUI 3 和 Windows App SDK。
- `src/Index.Core/` 是不依赖平台 UI 的领域与应用边界。
- `src/Index.Windows/` 放置不依赖 WinUI 的 Windows 基础设施适配器。
- `src/Index/` 是 WinUI 可执行项目，只承载应用装配、Windows UI 和必须依赖 Windows App SDK 的副作用。
- MolGrapher 是本地 Python 服务；OpenVINO 只属于 Python 推理实现，不得泄漏到 C# UI 或领域模型。

## 2. 强制依赖方向

依赖方向必须保持：

```text
Index (WinUI executable)
  -> Index.Windows
  -> Index.Core

Index.Windows
  -> Index.Core

Index.Core
  -> no UI/platform project
```

硬性规则：

- `Index.Core` 不得引用 WinUI、Windows Runtime UI、P/Invoke、`System.Drawing`、WebView2 或具体文件选择器。
- `Index.Windows` 不得引用 WinUI；纯 HTTP、Win32、System.Drawing 和可测试的 Windows adapter 优先放这里。
- UI 不得反向进入 Core；跨边界通过接口、不可变值对象和显式结果类型通信。
- 新功能不得因为“方便”直接从 View 访问数据库连接、磁盘目录布局或 Python 模型内部。
- 修改项目引用后必须运行现有边界测试。

## 3. 单窗口与导航规范

Index 的图库、图片预览和分子工作区采用单主窗口设计。

- 双击图片不得创建新的顶层 Window。
- 分子识别结果不得创建新的顶层 Window。
- 主窗口必须使用显式导航状态，例如 `Library`、`PreviewWorkspace`、`Settings`，不得依赖
  `_content.Children[index]`、隐式 ZIndex 或控件插入顺序表达页面状态。
- 页面进入、退出、刷新和快捷键导航必须走同一个 host/controller，不得各自修改视觉树。
- `MainWindow` 只负责 Shell、顶层导航和 composition；页面查询、识别流程和业务状态应下沉到 controller/viewmodel。
- 新工作区优先实现为可嵌入 `UserControl`；只有钉图、截图覆盖层等产品明确要求独立 HWND 的功能才能创建 Window。

## 4. 预览会话与异步生命周期

图片加载、识别和 Ketcher 更新必须属于一个显式 `PreviewSession` 或等价会话对象。

- 会话至少包含稳定 session ID、当前 shot ID、generation 和 `CancellationTokenSource`。
- 切图、返回图库、刷新图库和应用退出时必须取消旧会话。
- 迟到结果必须同时核对 session ID 与 shot ID，不能只比较当前控件字段。
- 取消后不得继续更新按钮、metadata、画布或 Ketcher。
- 不允许只“忽略结果”却让旧 MolGrapher 请求继续占用串行推理队列。
- 禁止无保护的 fire-and-forget；必须显式观察异常，或使用统一安全任务启动器。
- `DispatcherQueue.TryEnqueue` 返回 `false` 时必须结束对应 Task，不能留下永不完成的 TCS。
- `async void` 只允许用于 UI 事件入口，并必须在入口捕获和呈现异常。

## 5. 图片资产边界

UI 不得自行拼接 `originals/`、`thumbnails/` 路径或重复实现回退规则。

- 通过 `IShotAssetReader` 或等价应用接口读取图片。
- 读取结果必须区分 `Original`、`ThumbnailFallback`、`Missing` 和 `Corrupt`。
- 原图缺失但缩略图存在时允许预览，并明确提示降级状态。
- 识别优先使用原图；使用缩略图回退时必须产生 warning，不能静默冒充原图识别。
- 复制、导出、打开原图、预览和识别必须共享同一资产解析策略。
- 文件保存与数据库记录必须维持一致性；新增写流程应有失败清理和孤儿数据测试。
- 不得因单条损坏记录让图库整体加载失败。

## 6. 识别插件边界

C# 侧通过稳定、强类型的插件接口使用识别能力：

```csharp
public interface IRecognitionPlugin<TOutput> : IRecognitionPlugin
{
    Task<TOutput> RecognizeAsync(
        RecognitionInput input,
        CancellationToken cancellationToken = default);
}
```

- 每个插件必须声明稳定 ID、显示名、能力 ID、插件版本、输出类型和优先级。
- 能力 ID 使用可扩展字符串，例如 `molecule.structure`、`chemical.formula`；不得用封闭枚举阻止第三方能力扩展。
- 插件输出保持领域强类型；不得为了“通用”让 UI 解析 `Dictionary<string, object>` 或传输 JSON。
- View/UserControl 不得直接 `new MolGrapherClient()` 或其他识别器；由 composition root 注册到 `IRecognitionPluginRegistry`。
- 同一能力允许多个实现；默认选择必须确定性地按优先级和插件 ID 排序，也应支持用户指定插件 ID。
- 具体 HTTP、Python、OpenVINO、OCR SDK 与模型路径属于插件 adapter，不得泄漏进 Core 或 UI。
- 识别 workflow 负责 pending/success/empty/error/cancelled 状态，View 只绑定和发命令。
- 识别结果优先使用带预测二维坐标的 Mol/SDF；仅在 SDF 缺失或损坏时回退 SMILES。
- 不得用 RDKit 从 SMILES 二次布局后冒充 MolGrapher 预测坐标。
- 传输 DTO 与领域结果分离；HTTP 字段变化不得直接传播到 UI。
- 协议应版本化，并包含 engine/model/schema version、coordinate source、cache hit、warning 和结构化错误。

## 7. MolGrapher 服务规范

`molgrapher-service/app.py` 只应作为 composition/API 入口，逐步拆分为：

```text
api/routes + schemas
RecognitionService
RecognitionEngine protocol
MolGrapherEngine
RecognitionCache
Settings
```

- 上游 MolGrapher monkey-patch 必须全部收口在 `MolGrapherEngine` adapter，不得散落到路由和缓存代码。
- OpenVINO 加速属于 engine 内部 strategy；C# 客户端不得感知 CNN 层、设备名或 IR 路径。
- OpenVINO 安装前应校验模型版本、输入 shape 和目标层；失败必须给出结构化降级原因。
- 服务端缓存 key 必须包含：图片哈希、engine ID/version、模型内容哈希、预处理 profile、后处理版本、
  输出 schema 和坐标策略。
- 更换 checkpoint、后端、预处理或坐标策略后，旧识别缓存必须自动失效。
- 不得把“缓存命中 0ms”当成真实推理耗时；区分请求耗时、排队耗时和推理耗时。
- 启动脚本不得修改用户全局 Git 配置。

## 8. MolGrapher 一体化生命周期

目标是由 Index 管理本地识别服务，而不是要求用户手动维护终端窗口。

- 使用 `LocalRecognitionHost` 或等价组件管理子进程、端口、日志、ready 和退出。
- Host 与 HTTP client 分离：Host 管进程，client 只管协议传输。
- 启动后必须执行 health + protocol/model version 握手。
- 端口冲突、依赖缺失、模型首次准备和服务崩溃必须给用户可诊断状态。
- 应用退出时只结束由本应用启动的服务，不得杀死无法确认归属的 Python 进程。
- 不要把首次模型转换的长超时固定在通用 HTTP client；连接、准备、排队和推理分别设置 timeout。

## 9. Ketcher 生命周期

当前 Web Ketcher 是过渡方案；长期原生移植说明位于 `ketcher_winui3/README.md`。

- WebView2/Ketcher 应由 MainWindow 级 `MoleculeWorkspaceHost` 或其他长寿命 host 持有。
- 返回图库或收起分子面板只能 detach/hide，不得关闭 WebView2。
- 应用真正退出时才 Dispose Ketcher。
- Ketcher 初始化必须有单一 in-flight Task、取消机制和明确状态；禁止并发创建多个 WebView。
- Dispose 与初始化轮询必须安全互斥，Dispose 后不得再次解引用 WebView。
- 删除旧实现前保留一个可运行回退，但同一功能不得长期保留两套活跃生命周期模型。
- `MoleculePreviewWindow` 当前属于遗留独立窗口方案；不得新增调用，完成 host 迁移后应删除。
- 原生 `ketcher_winui3` 通过验收前，不得删除当前 Web 资产和 WebView2 回退。

## 10. 存储与数据库

- `ShotStore`、图库组织和附件操作不得各自私有化同一个 SQLite 文件的事务协调。
- 跨 shot、标签、收藏、集合和附件的用例应通过共享 database factory/write coordinator 或 Unit of Work。
- View 不得持有数据库连接或执行 SQL。
- repository 返回领域值，不返回 WinUI 控件、文件对话框或 Shell 对象。
- 删除文件前必须先确认引用计数；重复内容共享 SHA 时不得误删其他记录资产。
- 新增迁移必须有升级测试和已有数据回读测试。

## 11. 文件规模与职责

文件长度不是唯一标准，但以下情况必须先拆职责再继续堆功能：

- View 同时承担 I/O、网络、状态机和渲染。
- Window 同时承担路由、查询、页面装配和业务 workflow。
- Python 路由文件同时承担模型 patch、缓存和推理实现。
- 一个类需要三个以上 generation/boolean 字段才能维持异步正确性。

推荐目标：

- `MainWindow` 仅作为 shell。
- `ShotWorkspaceView` 只构造和绑定 UI。
- `ShotWorkspaceViewModel/Controller` 管理预览会话。
- `IShotAssetReader` 管理图片来源与回退。
- `IRecognitionPluginRegistry` 管理识别能力发现与插件选择。
- `MoleculeWorkspaceHost` 管理 Ketcher 生命周期。

纯布局数学、状态转换和命令规则必须放进可单元测试的非 WinUI 类型。

## 12. 构建与运行

常规验证：

```powershell
dotnet build src/Index/Index.csproj --no-restore
dotnet test src/Index.Tests/Index.Tests.csproj --no-restore
git diff --check
```

Release 发布：

```powershell
dotnet publish src/Index/Index.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  --output artifacts/<descriptive-name> `
  -p:WindowsAppSDKSelfContained=true `
  -p:PublishSingleFile=false
```

- 不要发布到正在运行的 `Index.exe` 所在目录。
- 先发布到新的旁路目录，确认成功后再退出旧进程并启动新版本。
- 停止进程前必须核对 PID 和可执行文件绝对路径，只处理本仓库明确启动的实例。
- MolGrapher 服务和 Index 主程序分别核对 PID；重启 UI 不应无故终止推理服务。
- 真实截图、录屏、分子识别和桌面自动化具有副作用，执行前告知用户。

## 13. 验证要求

常规修改至少运行 build、全部测试和 `git diff --check`。

涉及预览/识别/Ketcher 时额外验证：

- 双击图片不会产生额外顶层窗口。
- 原图、缩略图回退和文件完全缺失三种状态。
- 快速切图不会显示上一张结果。
- 识别中切图、返回图库和退出应用能正确取消。
- Ketcher 初始化中退出不会崩溃。
- 返回图库再打开时 Ketcher 不重新加载。
- 图库刷新后预览导航使用当前集合，不引用旧 View。
- SDF 坐标保持为模型预测坐标。
- 缓存命中与非命中结果结构一致。

报告必须区分：单元测试通过、Release 已启动、界面已查看、真实识别已执行。未验证的内容不得声称已通过。

## 14. 工作区与交付

- 工作区可能已有用户修改；保留无关变更，不要 reset、checkout 或覆盖他人的工作。
- 修改前检查 `git status --short`，只编辑任务范围内文件。
- 不使用 `git reset --hard`、`git checkout --` 或其他破坏性回滚。
- 未经用户明确要求，不提交、推送、打 tag 或提升版本号。
- 不删除当前 Ketcher、模型、缓存或用户图库数据，除非用户明确授权并已核对目标。
- 修改后只汇报本次实际验证过的结果。

## 15. Git Submodule

- 所有 submodule 的引入、更新、补丁、许可证和构建发布必须遵守
  [`docs/submodules.md`](docs/submodules.md)。
- 新增通用第三方源码统一放在 `third_party/<project-name>`，固定到审核过的完整 commit，并递归
  固定其嵌套 submodule。
- submodule 工作树必须保持 clean；长期修改使用受控 fork，不能依赖开发者本地未提交内容。
- 驱动源码不得进入常规 .NET 构建；驱动安装必须可选、显式申请管理员权限并提供恢复路径。
