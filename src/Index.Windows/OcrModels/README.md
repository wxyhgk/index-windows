# Bundled OCR model

Index uses the lightweight PP-OCRv4 mobile Chinese detector and recognizer locally through
ONNX Runtime. The complete model set is approximately 16.2 MB. Model initialization remains
lazy so a missing or incompatible runtime cannot prevent the application from starting.

- `v4/ch_PP-OCRv4_det_mobile.onnx`
  - source: RapidAI/RapidOCR model manifest, ModelScope release `v3.9.2`
  - SHA-256: `d2a7720d45a54257208b1e13e36a8479894cb74155a5efe29462512d42f49da9`
- `v4/ch_ppocr_mobile_v2.0_cls_mobile.onnx`
  - source: RapidAI/RapidOCR model manifest, ModelScope release `v3.9.2`
  - SHA-256: `e47acedf663230f8863ff1ab0e64dd2d82b838fceb5957146dab185a89d6215c`
- `v4/ch_PP-OCRv4_rec_mobile.onnx`
  - source: RapidAI/RapidOCR model manifest, ModelScope release `v3.9.2`
  - SHA-256: `48fc40f24f6d2a207a2b1091d3437eb3cc3eb6b676dc3ef9c37384005483683b`
- `v4/ppocr_keys_v1.txt`
  - source: RapidAI/RapidOCR model manifest, ModelScope release `v3.9.2`
  - SHA-256: `28b2362ad4ab2dc38769aa72feb535e3a9ddb3fd2a7585a05920e6393b1dc7f7`

The PaddleOCR models, RapidOCR, and RapidOcrNet are Apache-2.0 licensed. See the
repository root `THIRD_PARTY_NOTICES.md` for upstream links.
