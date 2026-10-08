# 第三方软件与模型声明

本文件记录 Index 当前直接使用或按需加载的第三方组件。Index 自身的源代码采用
[GNU GPL v3.0 or later](LICENSE)；各第三方项目和模型仍分别适用下列上游条款，
不会因 Index 使用 GPL 而被重新授权。

## GRDB.swift

- 用途：SQLite 数据访问与观察。
- 当前锁定版本：7.8.0（见 `Package.resolved`）。
- 许可证：MIT。
- Copyright © 2015–2025 Gwendal Roué。
- 上游：[groue/GRDB.swift](https://github.com/groue/GRDB.swift)；
  [LICENSE](https://github.com/groue/GRDB.swift/blob/master/LICENSE)。

## 3Dmol.js

- 用途：交互式 XYZ 分子显示与定格。
- 当前加载版本：2.5.5，通过 jsDelivr 按需加载，不存入仓库或 App bundle。
- 许可证：BSD-3-Clause；上游许可证同时列出其包含的 GLmol、Three.js 和 jQuery 声明。
- Copyright © 2014 University of Pittsburgh and contributors。
- 上游：[3dmol/3Dmol.js](https://github.com/3dmol/3Dmol.js)；
  [LICENSE](https://github.com/3dmol/3Dmol.js/blob/master/LICENSE)。
- 论文：Rego N, Koes D. 3Dmol.js: molecular visualization with WebGL. Bioinformatics. 2015.

## MobileCLIP Core ML 模型

- 用途：可选的本地语义图片搜索。
- 分发方式：模型不进入仓库或 App bundle；用户在设置中主动从
  [`apple/coreml-mobileclip`](https://huggingface.co/apple/coreml-mobileclip) 下载。
- 许可证：模型页面当前标记为 Apple Sample Code License（`apple-ascl`）；下载和使用模型时，
  用户应查阅并遵守模型页面当时提供的完整条款。
- 项目与论文：[apple/ml-mobileclip](https://github.com/apple/ml-mobileclip)。

## CLIP-Finder2 与 OpenCLIP tokenizer

- 用途：`CLIPTokenizer.swift` 由 CLIP-Finder2 的 Swift tokenizer 移植，并遵循 OpenCLIP 的
  tokenizer 结构；BPE 词表由用户按需下载。
- CLIP-Finder2：MIT，Copyright © 2024 Fabio Guzman；
  [项目](https://github.com/fguzman82/CLIP-Finder2)，
  [LICENSE](https://github.com/fguzman82/CLIP-Finder2/blob/main/LICENSE)。
- OpenCLIP：MIT；[mlfoundations/open_clip](https://github.com/mlfoundations/open_clip)，
  [LICENSE](https://github.com/mlfoundations/open_clip/blob/main/LICENSE)。

## Microsoft Win2D

- 用途：Windows.Graphics.Capture 合成表面的无重采样 PNG 编码。
- 当前版本：1.2.0（见 `src/Index/Index.csproj`）。
- 许可证：MIT。
- 上游：[microsoft/Win2D](https://github.com/microsoft/Win2D)，
  [LICENSE](https://github.com/microsoft/Win2D/blob/main/LICENSE.txt)。

## Virtual Display Driver

- 用途：Windows 高分辨率截图所需的可选虚拟显示器驱动源码；当前仅作为固定源码依赖，
  尚未由 Index 构建、安装或随发布包分发。
- 当前锁定提交：`d7244969b2aa8bb38e76d79505eda217996cefea`
  （见 `third_party/virtual-display-driver` submodule）。
- 许可证：MIT，Copyright © 2024 Virtual Display。
- 上游：[VirtualDrivers/Virtual-Display-Driver](https://github.com/VirtualDrivers/Virtual-Display-Driver)；
  [本地 LICENSE](third_party/virtual-display-driver/LICENSE)。
- 其嵌套的 Microsoft Windows Driver Frameworks 源码锁定于
  `3b9780e847cf68d6199dafe0f87650cf1f9c227f`；构建或分发驱动前须继续保留该依赖自身的
  版权与许可证声明。

## Ketcher

- 用途：Windows 端 WebView2 内的离线二维分子结构绘图与编辑。
- 当前锁定版本：`ketcher-react`、`ketcher-core`、`ketcher-standalone` 3.17.2
  （见 `scripts/ketcher/package-lock.json`）。
- 许可证：Apache License 2.0。
- Copyright © EPAM Systems, Inc. 及贡献者。
- 上游：[epam/ketcher](https://github.com/epam/ketcher)，
  [LICENSE](https://github.com/epam/ketcher/blob/master/LICENSE)。
- 编辑器 bundle 同时包含 React 18.3.1（MIT）及 Ketcher 的 npm 传递依赖；
  完整版本与许可证元数据记录在 `scripts/ketcher/package-lock.json`。

## OpenVINO

- 用途：在 Windows 上加速 MolGrapher 的关键点检测器和图分类器视觉骨干网络。
- 当前锁定版本：2025.3.0（见 `molgrapher-service/requirements.txt`）。
- 许可证：Apache License 2.0。
- Copyright © Intel Corporation 及贡献者。
- 上游：[openvinotoolkit/openvino](https://github.com/openvinotoolkit/openvino)，
  [LICENSE](https://github.com/openvinotoolkit/openvino/blob/master/LICENSE)。

## FLIPPED

- 用途：Windows 截图窗口候选过滤与 DWM 可见边界处理的实现参考。
- 许可证：MIT。
- 版权所有：Copyright (c) 2021-2024 Zhang Wengeng (XMuli)。
- 上游：[SunnyCapturer/FLIPPED](https://github.com/SunnyCapturer/FLIPPED)，
  [LICENSE](https://github.com/SunnyCapturer/FLIPPED/blob/master/LICENSE)。

## RapidOcrNet、RapidOCR 与 PaddleOCR 模型

- 用途：Windows x64 上的离线截图文字检测、识别与逐词坐标输出；系统 OCR 不可用时仍可工作。
- 当前版本：RapidOcrNet 4.1.0（见 `src/Index.Windows/Index.Windows.csproj`）。
- 当前随附模型：PP-OCRv4 mobile 简体中文轻量检测与识别模型（约 16.2 MB）。
- 许可证：RapidOcrNet、RapidOCR 及 PaddleOCR 模型均为 Apache License 2.0；RapidOcrNet 的
  NOTICE 还列明其包含的 PdfPig 与 PContour 派生实现。
- 上游：[BobLd/RapidOcrNet](https://github.com/BobLd/RapidOcrNet)、
  [RapidAI/RapidOCR](https://github.com/RapidAI/RapidOCR)、
  [PaddlePaddle/PaddleOCR](https://github.com/PaddlePaddle/PaddleOCR)。

## Apple 系统框架

AppKit、ScreenCaptureKit、Vision、Core ML、WebKit 等由 macOS 提供，不作为第三方源码随本仓库
分发，其使用受 Apple 平台和 SDK 条款约束。
