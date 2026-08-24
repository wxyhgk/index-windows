# MolGrapher 本地推理服务

基于 [MolGrapher](https://github.com/DS4SD/MolGrapher)（ICCV 2023）的本地 Python 微服务，
将分子结构图片识别为 SMILES 字符串。

## 快速开始

```bat
start.bat
```

首次运行自动：
1. 创建 Python 虚拟环境
2. 安装依赖（约 5 分钟）
3. 首次推理时下载模型（~1GB，从 HuggingFace）

服务启动后监听 `http://127.0.0.1:8100`。

首次识别会生成并缓存两个 OpenVINO IR 模型，可能需要数分钟；后续启动会直接复用
`models/openvino/` 中的缓存。默认使用 OpenVINO CPU 后端。在其他 Windows 设备上
可设置 `MOLGRAPHER_OPENVINO_DEVICE=GPU` 或 `AUTO:GPU,CPU` 进行对比测试；设置
`MOLGRAPHER_OPENVINO=0` 可回退到原始 PyTorch 前向。

服务默认启用低延迟识别路径：跳过输入阶段重复的 caption OCR、仅在预测图包含缩写
占位节点时运行缩写 OCR，并按图片 SHA-256 缓存最近 512 个成功结果。设置
`MOLGRAPHER_CAPTION_REMOVAL=1` 可恢复文档标题清理；设置
`MOLGRAPHER_ABBREVIATION_FILTER=0` 可强制对每张图运行缩写 OCR。不同配置使用独立
缓存命名空间，缓存目录为 `cache/`。

## API

### GET /health

```json
{ "status": "ok", "model_loaded": false }
```

### POST /recognize

`multipart/form-data`，字段 `file` 为 PNG/JPEG 图片（≤10MB）。

```json
{
  "smi": "O=C(O)C1=CC=C(C=C1)C(=O)O",
  "confidence": 0.991,
  "abbreviations": [],
  "processing_time_ms": 3200
}
```

识别失败时 `smi` 为 `null`，`error` 包含原因。

## 模型

- 来源：[docling-project/MolGrapher](https://huggingface.co/docling-project/MolGrapher)
- 架构：Keypoint Detector (151MB) + Graph Classifier (304MB)
- 推理：PyTorch CPU，单张图约 2-5 秒（1024×1024 输入）
- 许可证：MIT

## 测试

```bash
curl -X POST http://127.0.0.1:8100/recognize -F "file=@test_molecule.png"
```
