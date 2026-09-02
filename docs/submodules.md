# Git Submodule 管理规范

本规范适用于 Index 仓库中的所有 Git submodule。目标是让第三方源码的来源、版本、修改、
许可证和发布边界都可复现、可审计，同时避免 submodule 污染日常 .NET 构建与用户安装。

## 1. 何时允许使用 submodule

只有同时满足以下条件时才引入 submodule：

- 依赖是需要保留上游提交历史或源码布局的大型第三方项目；
- NuGet、PyPI、npm 等现有包管理器不能合理表达该依赖；
- Index 需要持续跟踪上游，而不是只借用少量代码；
- 已确认许可证允许当前用途，并在 `THIRD_PARTY_NOTICES.md` 中登记。

少量、稳定且需要深度修改的代码优先以明确许可证的 vendored source 管理；生成文件、模型、
构建产物和仅供下载的二进制不得为了方便而做成 submodule。

## 2. 路径与命名

- 新增通用第三方源码统一放在 `third_party/<project-name>`。
- `.gitmodules` 中的 section 名必须与路径一致，URL 使用公开的 HTTPS canonical URL。
- `molgrapher-service/MolGrapher` 是历史路径例外，不作为新 submodule 的目录范例。
- submodule 内部的嵌套 submodule 必须保留上游布局，不将其复制到 Index 的其他目录。

## 3. 版本固定与更新

- superproject 必须固定到完整、已审核的 commit，不跟随 `main`、`master`、tag 的可变解析结果
  或 `latest` 下载地址。
- 嵌套 submodule 同样必须固定；检查与初始化一律使用递归模式。
- 更新必须作为独立、可审查的改动，记录旧 commit、新 commit、上游变更摘要、许可证变化和
  实际验证结果。
- 未经明确任务授权，不顺手更新 submodule，也不运行会把所有依赖推进到远端最新提交的命令。

推荐的首次检出命令：

```powershell
git clone --recurse-submodules <index-repository-url>
```

已有工作区初始化命令：

```powershell
git submodule sync --recursive
git submodule update --init --recursive
```

更新单个依赖时，先在其目录中检出经过审核的 commit，再从 superproject 审查 gitlink 变化：

```powershell
git -C third_party/<project-name> fetch origin
git -C third_party/<project-name> checkout <full-commit>
git diff --submodule=log -- third_party/<project-name>
```

不得使用 `git submodule update --remote` 作为日常更新方式。

## 4. 本地修改与 fork

- submodule 在 Index 的正常工作树中必须保持 clean，不允许依赖未提交的嵌套修改才能构建。
- 需要长期维护上游修改时，使用 Index 管理的 fork，在 fork 内提交修改，并更新 `.gitmodules`
  的 URL 与固定 commit。
- 小型、临时且预计会上游合入的修改可以放在
  `third_party/patches/<project-name>/`，同时提供固定顺序的应用脚本和说明；补丁不得只存在于
  开发者本地的 submodule 工作树。
- 不得在 Index 的普通提交中混入第三方仓库内未解释的 merge commit、生成文件或格式化噪声。

## 5. 许可证与来源记录

每个 submodule 都必须在 `THIRD_PARTY_NOTICES.md` 中记录：

- 用途与是否为可选组件；
- 上游项目 URL；
- 当前固定的完整 commit；
- 许可证与本地许可证文件路径；
- Index 是否包含本地补丁或 fork；
- 是否会被构建、安装或随发布包分发；
- 若包含嵌套依赖，构建或分发时需要保留的声明。

更新 commit 时必须同时复核上游许可证、NOTICE、传递依赖和二进制再分发条款。

## 6. 构建边界

- 常规 `dotnet build`、`dotnet test` 和应用启动不得隐式构建第三方驱动或要求安装额外 SDK。
- 每个需要特殊工具链的 submodule 使用独立脚本，例如
  `scripts/build-virtual-display-driver.ps1`；脚本应检查前置条件，并将输出写到 `artifacts/`
  下的新目录。
- submodule 缺失时，常规 Index 功能必须仍可构建和运行；只有显式请求该可选组件的脚本可以
  给出可诊断的初始化提示。
- 不将 submodule 的中间产物、SDK 缓存或签名密钥提交到 superproject。

## 7. Windows 驱动附加规则

虚拟显示器等 Windows 驱动必须遵守以下额外边界：

- 上游已签名二进制与 Index 本地源码构建产物严格区分，不能混用版本或来源说明。
- 修改 INF、CAT、SYS、设备 ID 或驱动源码会使上游签名不再适用；本地修改版本必须使用 Index
  自己的合规签名与发布流程。
- test-signed、禁用签名强制或测试证书方案只允许隔离开发环境使用，不得进入正式发布包。
- 驱动安装必须是可选操作，明确展示来源、版本、管理员权限、重启需求和卸载/恢复方式；不得
  在应用启动或更新时静默安装。
- Index 必须在未安装驱动、驱动不可用或虚拟屏被禁用时继续提供普通截图能力。
- 安装、更新和卸载前必须保存并核对显示器配置；不得误操作其他厂商的虚拟显示设备。

## 8. 提交前检查

涉及 submodule 的改动至少执行：

```powershell
git submodule status --recursive
git submodule foreach --recursive git status --short
git diff --submodule=log
git diff --check
```

验收标准：

- `git submodule status --recursive` 没有意外的 `-`（未初始化）、`+`（与固定 commit 不一致）
  或 `U`（冲突）；已登记的历史迁移例外必须在交付说明中单独列出。
- 每个 submodule 的 `git status --short` 为空。
- `.gitmodules`、gitlink、`THIRD_PARTY_NOTICES.md` 和相关构建脚本描述的是同一版本与来源。
- 只提交 gitlink，不把 submodule 的整个文件树作为普通文件提交到 superproject。

如果改动还影响 Index 源码，继续执行仓库常规验证：

```powershell
dotnet build src/Index/Index.csproj --no-restore
dotnet test src/Index.Tests/Index.Tests.csproj --no-restore
```

## 9. 当前仓库状态

- `molgrapher-service/MolGrapher` 固定于
  `3c9e54b7368d123669d20cbdc70e6caa38af7a79`，属于历史路径例外；后续应在不改变该 commit
  的前提下单独完成标准化，不能与其他功能修改混在一起。
- `third_party/virtual-display-driver` 固定于
  `d7244969b2aa8bb38e76d79505eda217996cefea`。
- Virtual Display Driver 的嵌套 Windows Driver Frameworks 固定于
  `3b9780e847cf68d6199dafe0f87650cf1f9c227f`。
- Virtual Display Driver 当前仅作为可选源码依赖；Index 尚未构建、安装或分发它。
