"""MolGrapher 本地推理服务。

接收分子结构图片，返回 SMILES 字符串。
首次推理自动从 HuggingFace 下载模型（~1GB）。
"""

from __future__ import annotations

import asyncio
import logging
import os
import tempfile
import time
from typing import Optional

# PaddlePaddle Windows 上 OneDNN fused_conv2d 有 bug。
# 必须在 import paddle 之前设置。
os.environ["FLAGS_use_mkldnn"] = "0"
os.environ["FLAGS_use_onednn"] = "0"
os.environ["FLAGS_enable_pir_api"] = "0"
os.environ["FLAGS_enable_pir_in_executor"] = "0"

from fastapi import FastAPI, File, HTTPException, UploadFile
from pydantic import BaseModel

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(name)s] %(levelname)s: %(message)s",
)
logger = logging.getLogger("molgrapher-service")

app = FastAPI(title="MolGrapher Service", version="1.0.0")

_model = None
_load_lock = asyncio.Lock()


class RecognizeResponse(BaseModel):
    smi: Optional[str] = None
    confidence: float = 0.0
    abbreviations: list = []
    sdf: Optional[str] = None
    processing_time_ms: int = 0
    error: Optional[str] = None


def _load_model():
    """同步加载模型（在线程池中调用）。"""
    global _model
    if _model is not None:
        return _model

    logger.info("Loading MolGrapher model (first run downloads ~1GB from HuggingFace)...")
    t0 = time.time()

    import sys
    import types

    # CairoSVG 需要 cairo 原生库（Windows 无），但推理流程不用可视化。
    if "cairosvg" not in sys.modules:
        try:
            import cairosvg  # noqa: F401
        except OSError:
            mock = types.ModuleType("cairosvg")
            mock.svg2png = lambda *a, **kw: None
            mock.svg2svg = lambda *a, **kw: None
            sys.modules["cairosvg"] = mock

    # monkey-patch PaddleOCR.__init__ 强制 enable_mkldnn=False。
    from paddleocr import PaddleOCR as _PaddleOCR
    _orig_ocr_init = _PaddleOCR.__init__

    def _patched_ocr_init(self, *a, **kw):
        kw["enable_mkldnn"] = False
        _orig_ocr_init(self, *a, **kw)

    _PaddleOCR.__init__ = _patched_ocr_init

    # CaptionRemover 在 DataLoader worker 中 self.ocr 可能为 None。
    # patch __call__：OCR 不可用时直接返回原图（跳过 caption removal）。
    from molgrapher.utils.utils_dataset import CaptionRemover as _CR
    _orig_cr_call = _CR.__call__

    def _patched_cr_call(self, pil_image):
        if self.ocr is None:
            import numpy as _np
            image = _np.array(pil_image, dtype=_np.uint8)
            if image.ndim == 2:
                image = _np.stack((image,) * 3, axis=-1)
            return image
        return _orig_cr_call(self, pil_image)

    _CR.__call__ = _patched_cr_call

    # PyTorch 2.1.2 在 num_workers=0 时不允许 prefetch_factor / persistent_workers。
    # 直接 patch DataModule.get_dataloader，在 num_workers=0 时去掉这两个参数。
    from molgrapher.data_modules.data_module import DataModule as _DM
    _orig_get_dl = _DM.get_dataloader

    def _patched_get_dl(self, dataset):
        if self.config.get("nb_workers", 0) == 0:
            from torch.utils.data import DataLoader
            from torch_geometric.loader import DataLoader as PyGDataLoader
            if hasattr(dataset, "collate_fn") and (dataset.collate_fn is not None):
                return DataLoader(
                    dataset,
                    batch_size=self.config["batch_size"],
                    num_workers=0,
                    shuffle=False,
                    pin_memory=True,
                    drop_last=False,
                    collate_fn=dataset.collate_fn,
                )
            else:
                return PyGDataLoader(
                    dataset,
                    batch_size=self.config["batch_size"],
                    num_workers=0,
                    shuffle=False,
                    pin_memory=True,
                    drop_last=False,
                )
        return _orig_get_dl(self, dataset)

    _DM.get_dataloader = _patched_get_dl

    from molgrapher.models.molgrapher_model import MolgrapherModel

    _model = MolgrapherModel(
        args={
            "visualize": False,
            "clean": False,
            "force_cpu": True,
            "preprocess": False,  # 跳过 caption removal（DataLoader worker 中 PaddleOCR 对象丢失）
            "force_no_multiprocessing": True,  # 避免多进程传递 PaddleOCR 对象
        }
    )

    elapsed = time.time() - t0
    logger.info("MolGrapher model loaded in %.1fs", elapsed)
    return _model


async def get_model():
    """异步获取模型（懒加载，线程安全）。"""
    async with _load_lock:
        if _model is None:
            await asyncio.to_thread(_load_model)
    return _model


def _smiles_to_sdf(smi: str) -> Optional[str]:
    """用 RDKit 将 SMILES 转为 SDF（含 2D 坐标）。"""
    try:
        from rdkit import Chem
        from rdkit.Chem import AllChem
        from io import StringIO
        mol = Chem.MolFromSmiles(smi)
        if mol is None:
            return None
        AllChem.Compute2DCoords(mol)
        out = StringIO()
        writer = Chem.SDWriter(out)
        writer.write(mol)
        writer.close()
        return out.getvalue()
    except Exception:
        logger.exception("SDF generation failed for SMILES: %s", smi)
        return None


def _run_inference(image_path: str) -> RecognizeResponse:
    """同步推理（在线程池中调用）。"""
    model = _model
    start = time.time()
    try:
        annotations = model.predict_batch([image_path])
        elapsed_ms = int((time.time() - start) * 1000)

        if annotations and len(annotations) > 0:
            a = annotations[0]
            smi = a.get("smi")
            sdf = _smiles_to_sdf(smi) if smi else None
            return RecognizeResponse(
                smi=smi,
                confidence=float(a.get("conf", 0.0)),
                abbreviations=a.get("abbreviations", []),
                sdf=sdf,
                processing_time_ms=elapsed_ms,
            )
        return RecognizeResponse(
            processing_time_ms=elapsed_ms,
            error="No molecule detected in image",
        )
    except Exception as e:
        elapsed_ms = int((time.time() - start) * 1000)
        logger.exception("Recognition failed")
        return RecognizeResponse(
            processing_time_ms=elapsed_ms,
            error=str(e),
        )


@app.get("/health")
def health():
    return {
        "status": "ok",
        "model_loaded": _model is not None,
    }


@app.post("/recognize", response_model=RecognizeResponse)
async def recognize(file: UploadFile = File(...)):
    """接收分子结构图片，返回 SMILES。"""
    if not file.filename or not file.filename.lower().endswith((".png", ".jpg", ".jpeg")):
        raise HTTPException(400, "Please upload a PNG or JPEG image")

    image_bytes = await file.read()
    if len(image_bytes) > 10 * 1024 * 1024:
        raise HTTPException(413, "Image too large (max 10MB)")

    tmp_path = None
    try:
        with tempfile.NamedTemporaryFile(suffix=".png", delete=False) as tmp:
            tmp.write(image_bytes)
            tmp_path = tmp.name

        await get_model()
        result = await asyncio.to_thread(_run_inference, tmp_path)
        return result
    finally:
        if tmp_path and os.path.exists(tmp_path):
            os.unlink(tmp_path)


if __name__ == "__main__":
    import uvicorn

    uvicorn.run(app, host="127.0.0.1", port=8100)
