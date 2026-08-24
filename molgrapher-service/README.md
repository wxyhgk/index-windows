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

服务启动后监听 `http://127.0.0.1:8765`。

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
curl -X POST http://127.0.0.1:8765/recognize -F "file=@test_molecule.png"
```
