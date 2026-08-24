# Windows Toolbar

截图、钉图和后续编辑器共用的工具栏契约。逻辑沿用 macOS 端已经验证的设计，
但不引用 AppKit 或 WinUI 类型。

依赖方向：

```text
Overlay / Pin host
    -> UI/Toolbar/ToolbarView      WinUI 渲染
    -> ToolbarLayout               纯几何
    -> ToolbarRegistry             控件聚合
    -> IToolbarControl             控件契约
    -> AnnotationState             标注状态
```

规则：

- 控件按 `Tools / Style / History / Actions` 分组并在注册表排序。
- `Style` 独占样式行，其余控件进入主行；没有样式控件时退化为单行。
- 主行始终最靠近选区，空间不足时整块翻到选区上方，不折行。
- `ToolbarLayout` 同时产出视觉 frame 与扩大的 `HitFrame`，不允许宿主另算命中区。
- 宿主只注入 `ToolbarContext` 并处理命令，不直接构造具体按钮。
- 新控件通过 `ToolbarRegistry.Register` 接入；不修改 Overlay 的布局代码。
- Live Text、AI 选区等临时画布模式通过 `ToolbarHostCapabilities` 注入并互斥。

完成按钮只结束截图并确认图库入库，不额外导出桌面文件；显式导出由图库动作负责。
当前骨架只注册已经接通的完成和取消。下一阶段接入标注画布后，再注册工具、
样式轴、撤销/重做与更多动作。
