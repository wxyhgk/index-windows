"""OpenVINO acceleration for MolGrapher's two image CNN passes."""

from __future__ import annotations

import json
import logging
import os
import gc
from pathlib import Path
from types import MethodType
from typing import Any

import numpy as np
import torch
from torch import nn

logger = logging.getLogger("molgrapher-service.openvino")


class _KeypointExport(nn.Module):
    def __init__(self, detector: nn.Module):
        super().__init__()
        # Do not register the LightningModule itself: TorchScript inspects its
        # ``trainer`` property, which raises when the module is detached. Keep
        # only the layers that participate in inference.
        self.feature_extractor = detector.feature_extractor
        self.conv1 = detector.conv1
        self.bn1 = detector.bn1
        self.relu = detector.relu
        self.conv2 = detector.conv2
        self.bn2 = detector.bn2

    def forward(self, images: torch.Tensor) -> torch.Tensor:
        features = self.feature_extractor(images)["layer4"]
        features = self.relu(self.bn1(self.conv1(features)))
        return self.bn2(self.conv2(features))


class _BackboneExport(nn.Module):
    def __init__(self, backbone: nn.Module):
        super().__init__()
        self.backbone = backbone

    def forward(self, images: torch.Tensor) -> torch.Tensor:
        return self.backbone(images)["layer4"]


def _source_signature(model: Any) -> dict[str, Any]:
    """Return a cheap cache signature for the installed MolGrapher checkpoints."""
    import molgrapher

    data_dir = Path(molgrapher.__file__).resolve().parent.parent / "data" / "models"
    checkpoint_paths = [
        data_dir / "keypoint_detector" / "kd_model.ckpt",
        data_dir / "graph_classifier" / "gc_no_stereo_model.ckpt",
    ]
    return {
        str(path): {"size": path.stat().st_size, "mtime_ns": path.stat().st_mtime_ns}
        for path in checkpoint_paths
    }


def _ensure_ir(
    openvino: Any,
    export_model: nn.Module,
    xml_path: Path,
    signature: dict[str, Any],
) -> None:
    manifest_path = xml_path.with_suffix(".json")
    bin_path = xml_path.with_suffix(".bin")
    expected_manifest = {
        "openvino": openvino.__version__,
        "sources": signature,
        "precision": "FP32",
    }

    if xml_path.exists() and bin_path.exists() and manifest_path.exists():
        try:
            if json.loads(manifest_path.read_text(encoding="utf-8")) == expected_manifest:
                return
        except (OSError, json.JSONDecodeError):
            pass

    logger.info("Converting %s to OpenVINO IR (first run only)", xml_path.stem)
    export_model.eval()
    example = torch.zeros((1, 3, 1024, 1024), dtype=torch.float32)
    with torch.inference_mode():
        converted = openvino.convert_model(export_model, example_input=example)
    openvino.save_model(converted, xml_path, compress_to_fp16=False)
    manifest_path.write_text(
        json.dumps(expected_manifest, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )


def _infer(compiled_model: Any, images: torch.Tensor) -> torch.Tensor:
    input_array = images.detach().cpu().contiguous().numpy()
    output = compiled_model([input_array])[compiled_model.output(0)]
    # OpenVINO owns the returned buffer; copy it before the next inference.
    return torch.from_numpy(np.array(output, copy=True))


def _parameter_bytes(module: nn.Module) -> int:
    return sum(parameter.numel() * parameter.element_size() for parameter in module.parameters())


class _OpenVINOBackbone(nn.Module):
    """Small adapter that does not retain the original PyTorch CNN weights."""

    def __init__(self, compiled_model: Any):
        super().__init__()
        self._compiled_model = compiled_model

    def forward(self, images: torch.Tensor) -> dict[str, torch.Tensor]:
        return {"layer4": _infer(self._compiled_model, images)}


def install_openvino_acceleration(
    molgrapher_model: Any,
    cache_dir: Path,
) -> dict[str, Any]:
    """Replace only the two CNN forward calls and leave graph logic in PyTorch."""
    import openvino as ov

    cache_dir.mkdir(parents=True, exist_ok=True)
    recognizer = molgrapher_model.model
    keypoint_detector = recognizer.keypoint_detector
    graph_classifier = recognizer.graph_classifier
    graph_backbone = graph_classifier.backbone
    signature = _source_signature(molgrapher_model)

    keypoint_xml = cache_dir / "keypoint_detector_fp32.xml"
    backbone_xml = cache_dir / "graph_classifier_backbone_fp32.xml"
    _ensure_ir(ov, _KeypointExport(keypoint_detector), keypoint_xml, signature)
    _ensure_ir(ov, _BackboneExport(graph_backbone), backbone_xml, signature)

    core = ov.Core()
    # Explicit CPU is faster and substantially more stable than AUTO/GPU on
    # the tested Intel UHD 770 system. The environment variable remains useful
    # for diagnostics on other Windows hardware.
    requested_device = os.environ.get("MOLGRAPHER_OPENVINO_DEVICE", "CPU")
    properties = {"PERFORMANCE_HINT": "LATENCY"}
    keypoint_compiled = core.compile_model(keypoint_xml, requested_device, properties)
    backbone_compiled = core.compile_model(backbone_xml, requested_device, properties)

    def keypoint_forward(_self: nn.Module, images: torch.Tensor) -> torch.Tensor:
        return _infer(keypoint_compiled, images)

    keypoint_detector.forward = MethodType(keypoint_forward, keypoint_detector)

    # The OpenVINO IR now owns both CNN inference paths. Retaining the original
    # PyTorch modules keeps roughly 140 MB of weights resident for no benefit.
    # Keep KeypointDetector.predict and the graph classifier/GNN heads, while
    # replacing only modules whose forward calls are handled by OpenVINO.
    released_parameter_bytes = _parameter_bytes(keypoint_detector) + _parameter_bytes(
        graph_backbone
    )
    keypoint_detector.feature_extractor = nn.Identity()
    keypoint_detector.conv1 = nn.Identity()
    keypoint_detector.bn1 = nn.Identity()
    keypoint_detector.relu = nn.Identity()
    keypoint_detector.conv2 = nn.Identity()
    keypoint_detector.bn2 = nn.Identity()
    graph_classifier.backbone = _OpenVINOBackbone(backbone_compiled)
    del graph_backbone
    gc.collect()

    execution_devices = {
        "keypoint": keypoint_compiled.get_property("EXECUTION_DEVICES"),
        "graph_backbone": backbone_compiled.get_property("EXECUTION_DEVICES"),
    }
    logger.info(
        "OpenVINO enabled (requested=%s, execution=%s)",
        requested_device,
        execution_devices,
    )
    return {
        "backend": "openvino",
        "requested_device": requested_device,
        "execution_devices": execution_devices,
        "released_pytorch_parameter_bytes": released_parameter_bytes,
        # Keep compiled models and Core alive for the patched methods.
        "runtime": (core, keypoint_compiled, backbone_compiled),
    }
