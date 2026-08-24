"""MolGrapher 本地推理服务。

接收分子结构图片，返回 SMILES 字符串。
首次推理自动从 HuggingFace 下载模型（~1GB）。
"""

from __future__ import annotations

import asyncio
import hashlib
import json
import logging
import os
import sys
import tempfile
import time
from pathlib import Path
from typing import Optional

# MolGrapher and its ML dependencies write progress with plain ``print`` calls.
# A service started from a short-lived Windows terminal can retain invalid
# standard handles, making the first print fail with ``OSError: [Errno 22]``.
# Keep process output attached to a real file for the entire service lifetime.
_service_dir = Path(__file__).resolve().parent
_log_dir = _service_dir / "logs"
_log_dir.mkdir(exist_ok=True)
_output_stream = open(
    _log_dir / "molgrapher.log",
    "a",
    encoding="utf-8",
    buffering=1,
)
sys.stdout = _output_stream
sys.stderr = _output_stream

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
_inference_lock = asyncio.Lock()
_inference_backend = {"backend": "pytorch", "requested_device": "CPU"}
_caption_removal_enabled = os.environ.get("MOLGRAPHER_CAPTION_REMOVAL", "0") == "1"
_abbreviation_filter_enabled = os.environ.get("MOLGRAPHER_ABBREVIATION_FILTER", "1") != "0"
_cache_profile = (
    f"recognition-v2-openvino-fp32-aligned-"
    f"caption-{int(_caption_removal_enabled)}-"
    f"abbreviation-filter-{int(_abbreviation_filter_enabled)}"
)
_recognition_cache_dir = _service_dir / "cache" / _cache_profile
_recognition_cache_limit = 512


class RecognizeResponse(BaseModel):
    smi: Optional[str] = None
    confidence: float = 0.0
    abbreviations: list = []
    sdf: Optional[str] = None
    processing_time_ms: int = 0
    error: Optional[str] = None
    cached: bool = False


def _cache_path(image_bytes: bytes) -> Path:
    digest = hashlib.sha256(image_bytes).hexdigest()
    return _recognition_cache_dir / f"{digest}.json"


def _load_cached_result(cache_path: Path) -> Optional[RecognizeResponse]:
    try:
        payload = json.loads(cache_path.read_text(encoding="utf-8"))
        payload["processing_time_ms"] = 0
        payload["cached"] = True
        return RecognizeResponse(**payload)
    except (OSError, ValueError, TypeError):
        return None


def _save_cached_result(cache_path: Path, result: RecognizeResponse) -> None:
    if result.error is not None or not result.smi or not result.sdf:
        return

    _recognition_cache_dir.mkdir(parents=True, exist_ok=True)
    payload = {
        "smi": result.smi,
        "confidence": result.confidence,
        "abbreviations": result.abbreviations,
        "sdf": result.sdf,
        "processing_time_ms": result.processing_time_ms,
        "error": None,
        "cached": False,
    }
    temporary_path = cache_path.with_suffix(".tmp")
    temporary_path.write_text(
        json.dumps(payload, ensure_ascii=False, separators=(",", ":")),
        encoding="utf-8",
    )
    os.replace(temporary_path, cache_path)

    cache_files = sorted(
        _recognition_cache_dir.glob("*.json"),
        key=lambda path: path.stat().st_mtime_ns,
        reverse=True,
    )
    for expired_path in cache_files[_recognition_cache_limit:]:
        try:
            expired_path.unlink()
        except OSError:
            logger.warning("Failed to remove old recognition cache: %s", expired_path)


def _load_model():
    """同步加载模型（在线程池中调用）。"""
    global _model, _inference_backend
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

    if not _caption_removal_enabled:
        from molgrapher.datasets.dataset_image import ImageDataset as _ImageDataset

        def _fast_image_dataset_init(
            self,
            dataset,
            config,
            preprocessed=False,
            evaluate=False,
            force_cpu=False,
            *args,
            **kwargs,
        ):
            self.dataset = dataset
            self.config = config
            self.border_size = 30
            self.evaluate = evaluate
            self.caption_remover = _CR(force_cpu=force_cpu, remove_captions=False)
            # CaptionRemover.ocr is a class attribute and may later be populated
            # by the abbreviation detector. Shadow it on this dataset instance.
            self.caption_remover.ocr = None
            self.preprocessed = preprocessed

        _ImageDataset.__init__ = _fast_image_dataset_init

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
            "remove_captions": _caption_removal_enabled,
            "force_no_multiprocessing": True,  # 避免多进程传递 PaddleOCR 对象
            "align_rdkit_output": True,
        }
    )

    if _abbreviation_filter_enabled:
        original_mp_run = _model.abbreviation_detector.mp_run

        def _filtered_mp_run(images_filenames, graphs, bonds_sizes, filter=False):
            return original_mp_run(
                images_filenames,
                graphs,
                bonds_sizes,
                filter=True,
            )

        _model.abbreviation_detector.mp_run = _filtered_mp_run

    if os.environ.get("MOLGRAPHER_OPENVINO", "1") != "0":
        try:
            from openvino_backend import install_openvino_acceleration

            _inference_backend = install_openvino_acceleration(
                _model,
                _service_dir / "models" / "openvino",
            )
        except Exception:
            _inference_backend = {
                "backend": "pytorch",
                "requested_device": "CPU",
                "error": "OpenVINO initialization failed; using PyTorch",
            }
            logger.exception("OpenVINO initialization failed; using PyTorch")

    elapsed = time.time() - t0
    logger.info("MolGrapher model loaded in %.1fs", elapsed)
    return _model


async def get_model():
    """异步获取模型（懒加载，线程安全）。"""
    async with _load_lock:
        if _model is None:
            await asyncio.to_thread(_load_model)
    return _model


def _run_inference(image_path: str) -> RecognizeResponse:
    """同步推理（在线程池中调用）。"""
    model = _model
    start = time.time()
    try:
        # MolGrapher can preserve the detector's atom positions in its MOL
        # output. Use a per-request output directory and a POSIX-style input
        # path because upstream extracts the filename by splitting on '/'.
        with tempfile.TemporaryDirectory(prefix="molgrapher-output-") as output_dir:
            previous_output_dir = model.args["save_mol_folder"]
            model.args["save_mol_folder"] = Path(output_dir).as_posix() + "/"
            normalized_image_path = Path(image_path).as_posix()
            try:
                annotations = model.predict_batch([normalized_image_path])
                mol_path = Path(output_dir) / f"{Path(image_path).stem}.mol"
                mol_block = mol_path.read_text(encoding="utf-8") if mol_path.exists() else None
            finally:
                model.args["save_mol_folder"] = previous_output_dir
        elapsed_ms = int((time.time() - start) * 1000)

        if annotations and len(annotations) > 0:
            a = annotations[0]
            smi = a.get("smi")
            sdf = f"{mol_block.rstrip()}\n$$$$\n" if mol_block else None
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
        "inference_backend": {
            key: value
            for key, value in _inference_backend.items()
            if key != "runtime"
        },
        "optimizations": {
            "caption_removal": _caption_removal_enabled,
            "abbreviation_filter": _abbreviation_filter_enabled,
            "recognition_cache": True,
        },
    }


@app.post("/recognize", response_model=RecognizeResponse)
async def recognize(file: UploadFile = File(...)):
    """接收分子结构图片，返回 SMILES。"""
    if not file.filename or not file.filename.lower().endswith((".png", ".jpg", ".jpeg")):
        raise HTTPException(400, "Please upload a PNG or JPEG image")

    image_bytes = await file.read()
    if len(image_bytes) > 10 * 1024 * 1024:
        raise HTTPException(413, "Image too large (max 10MB)")

    cache_path = _cache_path(image_bytes)
    cached_result = await asyncio.to_thread(_load_cached_result, cache_path)
    if cached_result is not None:
        return cached_result

    tmp_path = None
    try:
        with tempfile.NamedTemporaryFile(suffix=".png", delete=False) as tmp:
            tmp.write(image_bytes)
            tmp_path = tmp.name

        await get_model()
        async with _inference_lock:
            # Another request for the same image may have filled the cache while
            # this request waited for model loading or the inference lock.
            cached_result = await asyncio.to_thread(_load_cached_result, cache_path)
            if cached_result is not None:
                return cached_result
            result = await asyncio.to_thread(_run_inference, tmp_path)
            await asyncio.to_thread(_save_cached_result, cache_path, result)
        return result
    finally:
        if tmp_path and os.path.exists(tmp_path):
            os.unlink(tmp_path)


if __name__ == "__main__":
    import uvicorn

    uvicorn.run(app, host="127.0.0.1", port=8100)
