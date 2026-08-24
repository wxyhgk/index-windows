# Ketcher WinUI 3 原生移植开发说明

## 1. 项目目标

本目录用于开发一个面向 Windows 的原生小分子结构编辑器。项目参考 Ketcher
3.17.2 的功能、交互和数据模型，但运行时不得依赖 WebView2、React、HTML、CSS、
JavaScript 或 Node.js。

首要使用场景是接收 Index/MolGrapher 输出的带二维预测坐标的 Mol/SDF，在不重新
生成坐标的前提下显示结构，并允许用户修正识别错误后重新导出 Mol/SDF。

本项目不是完整复刻 Ketcher 的所有能力。第一阶段只覆盖普通小分子编辑；反应式、
聚合物、宏分子、R-group、查询结构和协同编辑不属于 MVP。

## 2. “原生”的验收定义

- UI 使用 C#、WinUI 3 和 XAML。
- 分子画布使用 Win2D 或 SkiaSharp，不嵌入浏览器窗口。
- 鼠标、触控笔、键盘、焦点、缩放和无障碍由 Windows 输入系统处理。
- 核心文档模型、命令栈和 MOL/SDF 解析不得依赖 UI，也不得引用 WinUI 类型。
- 发布目录不包含 Ketcher Web bundle，不要求安装 WebView2 Runtime。
- 离线运行，不访问远程 Ketcher/Indigo 服务。

## 3. 技术基线

- Windows 11 x64
- .NET 9
- Windows App SDK 1.7 / WinUI 3
- C# nullable reference types
- 测试框架沿用仓库现有 `Index.Tests`
- 渲染优先评估 Win2D；若文字测量、离屏渲染或测试可重复性不满足要求，再选择
  SkiaSharp。MVP 不要同时维护两套渲染器。

不要把 `ketcher-core` 的 JavaScript 作为长期运行的“无界面后端”。那仍然需要 JS
运行时，无法达到纯原生目标。可以阅读和移植其算法，但进入本项目的生产代码应为 C#。

## 4. 推荐目录结构

```text
ketcher_winui3/
  README.md
  src/
    Ketcher.WinUI3.Core/          # 纯 C# 文档模型、几何、命令、格式读写
      Chemistry/
      Geometry/
      Commands/
      Formats/
    Ketcher.WinUI3.Rendering/     # Win2D 或 SkiaSharp 渲染与命中测试
    Ketcher.WinUI3.Controls/      # 可嵌入 Index 的 WinUI 3 控件
    Ketcher.WinUI3.Demo/          # 独立测试应用，不承载业务逻辑
  tests/
    Ketcher.WinUI3.Core.Tests/
    Ketcher.WinUI3.Rendering.Tests/
    Ketcher.WinUI3.Integration.Tests/
  test-data/
    mol/
    sdf/
    expected-images/
  THIRD_PARTY_NOTICES.md
```

项目成熟前不要直接把实验代码写进 `src/Index/`。先让原生控件在 Demo 中独立工作，
再通过项目引用接入 Index。

## 5. 架构边界

```text
Ketcher.WinUI3.Controls
          ↓
Ketcher.WinUI3.Rendering
          ↓
Ketcher.WinUI3.Core
```

依赖只能向下：

- `Core` 不引用 WinUI、Win2D、SkiaSharp、文件选择器或剪贴板。
- `Rendering` 读取只读文档快照，不直接修改分子模型。
- `Controls` 把指针/键盘操作转换成 Core 命令，并负责 Windows 平台副作用。
- Index 只依赖公开控件和数据接口，不直接操作内部原子、键集合。

建议的最小公开接口：

```csharp
public interface IMoleculeEditor
{
    MoleculeDocument Document { get; }
    bool CanUndo { get; }
    bool CanRedo { get; }

    void LoadMolfile(string molfile);
    string SaveMolfile(MolfileVersion version = MolfileVersion.V2000);
    void NewDocument();
    void Undo();
    void Redo();

    event EventHandler? DocumentChanged;
}
```

SDF 第一阶段按“一个 mol block 加 property records”处理。编辑结构时必须保留未知的
SDF property；不能因为保存而静默丢失 MolGrapher 或其他程序附加的元数据。

## 6. 核心数据模型

模型至少包含：

- `Atom`：稳定 ID、元素、二维坐标、电荷、同位素、自由基、显式氢数、映射号。
- `Bond`：稳定 ID、起止原子、键级、立体方向、芳香标记。
- `MoleculeDocument`：原子、键、选择集、SDF properties、文档版本号。
- `DocumentSnapshot`：供渲染线程读取的不可变快照。
- `IEditorCommand`：`Execute`/`Undo`，用于所有可编辑操作。

不要使用数组下标作为长期原子身份。删除原子或重新排序 MOL block 后，下标会变化；
选择集、命令栈和键连接必须依赖稳定 ID。

坐标约定必须尽早固定：Core 使用与设备无关的文档坐标；屏幕缩放、DPI 和 viewport
变换只存在于 Rendering/Controls。加载 MolGrapher SDF 时保留原始二维坐标，不自动
调用布局算法。

## 7. MVP 功能范围

### 必须完成

- 读取和写出 Mol V2000。
- 读取单记录 SDF，并原样保留 property records。
- 正确显示原子标签、隐式碳、单/双/三键、芳香键、楔形键和虚线楔形键。
- 平移、滚轮缩放、缩放到适合窗口。
- 单击选择、框选、多选、拖动原子或选择集。
- 添加、修改、删除原子和键。
- 常用元素快捷选择：C、N、O、S、P、F、Cl、Br、I、B、Si。
- 单键、双键、三键、芳香键和立体键工具。
- 苯环及 3—8 元碳环模板。
- 撤销/重做；每个用户动作只能产生一个命令记录。
- 新建、打开、保存、复制 Mol/SDF 文本。
- 可作为 `UserControl` 嵌入 Index，不创建自己的主窗口。

### MVP 暂不实现

- RXN 和多步反应编辑。
- S-group、聚合物和宏分子编辑。
- R-group、查询原子/键和 SMARTS 全功能。
- 自动结构布局、名称转结构、结构搜索和云端服务。
- 完整复刻 Ketcher 的菜单、图标位置和视觉样式。

## 8. 渲染与交互要求

- 文档到屏幕的变换统一为一个 `ViewportTransform`，绘制和命中测试必须共用。
- 命中半径按屏幕像素定义，再反算到文档坐标，避免缩放后难以选择。
- 原子标签需要遮盖穿过文字的键线；双键偏移方向必须稳定。
- 所有绘制路径应可在无窗口测试中生成，避免只能靠人工截图判断。
- 画布重绘不能修改文档；输入事件不能直接调用绘图 API 修改缓存。
- 拖动期间允许预览，但松开指针后才提交一个可撤销命令。
- 目标是普通结构在 60 FPS 下拖动；含 500 个原子时交互不应明显阻塞 UI 线程。

建议为渲染建立金图测试，同时允许小范围像素容差，以免字体栅格化差异造成脆弱测试。

## 9. 格式兼容与测试样本

格式解析是高风险区域，不要用正则表达式拼凑 MOL parser。建立逐行 tokenizer，并在错误
中报告记录号和字段位置。

测试数据至少覆盖：

- 空分子、单原子、断开的多个组分。
- 普通环、稠环、芳香环和交叉键。
- 正负电荷、同位素、显式氢、Cl/Br/Si 等双字符元素。
- 双键几何、上/下楔形键。
- MolGrapher 实际输出的 SDF，包括预测坐标和属性字段。
- CRLF、LF、尾部空行、超长属性以及非 ASCII 属性文本。
- 读取后写出再读取的结构等价性。

MVP 完成前至少保留一组与当前 Web Ketcher 的对照样本。对照的是结构和坐标语义，
不是逐字节相同；MOL block 中原子顺序可以不同，但连接关系、属性和立体信息不能变化。

## 10. 与 Index 的接入契约

当前 Index 从 `MolGrapherClient` 获得：

```text
Smiles
Sdf
Confidence
ProcessingTimeMs
```

接入原生编辑器时：

1. 优先调用 `LoadMolfile(Sdf)`，保持模型预测坐标。
2. SDF 缺失或损坏时才允许使用 SMILES 回退。
3. SMILES 回退需要明确的布局实现；不能悄悄依赖 RDKit 二次生成并冒充预测坐标。
4. 编辑器输出以 Mol/SDF 为主，SMILES 只是派生格式。
5. 解析失败必须显示可诊断错误，不得以空白画布吞掉错误。

替换点位于：

- `src/Index/UI/Molecule/MoleculePreviewWindow.cs`
- `src/Index/UI/Molecule/MoleculeSketcherView.cs`
- `src/Index/UI/Gallery/ShotPreviewWindow.cs`

原生控件通过验收前保留现有 WebView2 Ketcher 作为可运行回退；不要在开发初期删除
`scripts/ketcher/` 或 `src/Index/Assets/Ketcher/`。

## 11. 开发阶段

### 阶段 0：技术验证

- 建立解决方案、Core/Rendering/Controls/Demo/Test 项目。
- 选定 Win2D 或 SkiaSharp，并记录选择依据。
- 加载一个 MolGrapher SDF，正确绘制原子和键。
- 验证 100%、150%、200% DPI。

### 阶段 1：只读查看器

- 完成 V2000/SDF parser 和 writer。
- 完成 viewport、基础渲染、平移缩放和适配窗口。
- 建立结构等价测试与渲染金图。

### 阶段 2：基础编辑器

- 选择、移动、增删原子和键。
- 命令栈、撤销重做、快捷键。
- 保存后重新加载验证。

### 阶段 3：实用化

- 环模板、键类型、常用元素和立体键。
- 原生 WinUI 工具栏、菜单、状态栏和文件对话框。
- 键盘、触控笔、DPI、深浅主题及性能验证。

### 阶段 4：接入 Index

- 用原生控件替换 `MoleculeSketcherView` 的 WebView2 实现。
- 对 MolGrapher 实际识别结果做端到端验证。
- 在确认功能覆盖和回退策略后再移除 Web 资产与 WebView2 依赖。

## 12. 每阶段验收门槛

- `dotnet build` 零错误、零警告。
- Core 单元测试全部通过。
- `git diff --check` 通过。
- 解析器对损坏输入不崩溃、不无限循环、不分配不受限内存。
- 保存前后结构等价，SDF properties 不丢失。
- MolGrapher SDF 的二维坐标未经布局器改写。
- Demo 可在未安装 Node.js、未联网的 Windows 机器运行。
- 报告中区分自动测试、Demo 实际启动和人工编辑验证。

## 13. 开源许可与来源记录

当前 Web 集成使用 Ketcher 3.17.2；其已安装包元数据声明许可证为 Apache-2.0，来源为
`https://github.com/epam/ketcher`。移植代码时必须：

- 保留被移植文件或算法对应的原版权声明。
- 在本目录的 `THIRD_PARTY_NOTICES.md` 记录上游仓库、commit/tag、原文件路径和本地文件。
- 附带 Apache-2.0 许可证文本，并标明本地修改。
- 不要只写“参考 Ketcher”；逐段移植或翻译代码必须可以追溯来源。
- 新增第三方化学库前先核对许可证、Windows x64 发布方式和离线可用性。

在正式分发前应再次由维护者核对上游具体版本的 LICENSE/NOTICE；包元数据不能替代最终
发行审计。

## 14. 开发纪律

- 仅支持 Windows，不加入 macOS/Linux 条件分支。
- 保留工作区已有修改，不执行 reset 或覆盖无关文件。
- 不在 UI 事件中实现化学规则或 MOL 解析。
- 不为追求像 Ketcher 而复制其 React 组件结构；按原生 Windows 交互重新设计 UI。
- 不允许解析失败后从 SMILES 静默重建坐标。
- 未经确认，不删除当前 Ketcher WebView2 回退实现。
- 未经用户明确要求，不提交、推送、打 tag 或提升版本号。

## 15. 首个可交付任务

第一位开发者应只完成以下纵向切片：

1. 创建建议的项目结构。
2. 实现 Mol V2000 的原子/键/二维坐标读取。
3. 使用选定渲染器显示一份 MolGrapher SDF。
4. 实现平移、滚轮缩放和适配窗口。
5. 为 parser 和坐标变换编写测试。
6. 在 Demo 中展示，不修改 Index 现有运行路径。

这个切片通过后再开始编辑功能。不要一开始同时实现格式、完整工具栏、全部化学规则和
Index 接入。
