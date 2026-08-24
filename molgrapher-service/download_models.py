"""下载 MolGrapher 模型到 MolGrapher/data/models/ 目录。

MolGrapher 内部用 wget 下载模型，Windows 上不可用。
此脚本用 huggingface_hub 替代，确保模型在 load_from_checkpoint 期望的路径。

路径逻辑（graph_recognizer.py）：
    os.path.dirname(__file__) + "/../../data/models/keypoint_detector/kd_model.ckpt"
    = <MolGrapher 包根>/data/models/keypoint_detector/kd_model.ckpt

所以 pip install -e ./MolGrapher 后，模型放在 ./MolGrapher/data/models/ 下。

代理：自动读取 HTTP_PROXY / HTTPS_PROXY 环境变量（start.bat 已设置）。
"""

import os
import sys

from huggingface_hub import hf_hub_download

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
LOCAL_DIR = os.path.join(BASE_DIR, "MolGrapher", "data")
REPO_ID = "ds4sd/MolGrapher"

# 默认只下载 gc_no_stereo_model（MolgrapherModel 默认变体）
# 如需立体化学或 GCN 变体，取消注释
FILES = [
    "models/keypoint_detector/kd_model.ckpt",
    "models/graph_classifier/gc_no_stereo_model.ckpt",
    # "models/graph_classifier/gc_stereo_model.ckpt",
    # "models/graph_classifier/gc_gcn_model.ckpt",
]


def main():
    proxy = os.environ.get("HTTPS_PROXY") or os.environ.get("https_proxy") or ""
    if proxy:
        print(f"  Proxy: {proxy}")

    print("MolGrapher model download")
    print(f"  Target: {LOCAL_DIR}")
    print(f"  Source: {REPO_ID}")
    print()

    all_ok = True
    for filename in FILES:
        local_path = os.path.join(LOCAL_DIR, filename)
        if os.path.exists(local_path):
            size_mb = os.path.getsize(local_path) / (1024 * 1024)
            print(f"  [OK] {filename} ({size_mb:.0f} MB)")
            continue

        os.makedirs(os.path.dirname(local_path), exist_ok=True)
        print(f"  [DL] {filename} ...")
        try:
            hf_hub_download(repo_id=REPO_ID, filename=filename, local_dir=LOCAL_DIR)
            size_mb = os.path.getsize(local_path) / (1024 * 1024)
            print(f"  [OK] {filename} ({size_mb:.0f} MB)")
        except Exception as e:
            print(f"  [FAIL] {filename}: {e}", file=sys.stderr)
            all_ok = False

    if all_ok:
        print("\nAll model files ready.")
    else:
        print("\nSome downloads failed. Check network or proxy settings.", file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()
