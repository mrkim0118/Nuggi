from __future__ import annotations

import os
import threading
from dataclasses import dataclass
from pathlib import Path

from PIL import Image, ImageOps


class BackgroundRemovalError(Exception):
    pass


@dataclass(frozen=True, slots=True)
class MaskQualityMetrics:
    coverage_ratio: float
    edge_ratio: float
    bbox_fill_ratio: float
    touches_all_edges: bool
    penalty: float
    should_retry: bool


def trim_transparent_padding(image: Image.Image) -> Image.Image:
    if image.mode != "RGBA":
        image = image.convert("RGBA")

    alpha = image.getchannel("A")
    bbox = alpha.getbbox()
    if not bbox:
        return image

    cropped = image.crop(bbox)
    padded = Image.new("RGBA", (cropped.width + 2, cropped.height + 2), (0, 0, 0, 0))
    padded.paste(cropped, (1, 1))
    return padded


def model_home() -> Path:
    # Same lookup rembg uses for its model files.
    return Path(
        os.path.expanduser(os.getenv("U2NET_HOME", os.path.join(os.getenv("XDG_DATA_HOME", "~"), ".u2net")))
    )


def is_model_downloaded(model_name: str) -> bool:
    return (model_home() / f"{model_name}.onnx").is_file()


# Segment Anything (click/box -> one object's mask). Quantized vit_b: ~113 MB, CPU friendly.
SAM_OPTIONS = {"sam_model": "sam_vit_b_01ec64", "sam_quant": True}

# Mask R-CNN from the ONNX Model Zoo (MIT): finds and names COCO objects.
DETECTION_MODEL_FILE = "MaskRCNN-12.onnx"
DETECTION_MODEL_URL = (
    "https://github.com/onnx/models/raw/main/validated/vision/"
    "object_detection_segmentation/mask-rcnn/model/MaskRCNN-12.onnx"
)
COCO_CLASSES = [
    "background", "person", "bicycle", "car", "motorcycle", "airplane", "bus", "train", "truck", "boat",
    "traffic light", "fire hydrant", "stop sign", "parking meter", "bench", "bird", "cat", "dog", "horse",
    "sheep", "cow", "elephant", "bear", "zebra", "giraffe", "backpack", "umbrella", "handbag", "tie",
    "suitcase", "frisbee", "skis", "snowboard", "sports ball", "kite", "baseball bat", "baseball glove",
    "skateboard", "surfboard", "tennis racket", "bottle", "wine glass", "cup", "fork", "knife", "spoon",
    "bowl", "banana", "apple", "sandwich", "orange", "broccoli", "carrot", "hot dog", "pizza", "donut",
    "cake", "chair", "couch", "potted plant", "bed", "dining table", "toilet", "tv", "laptop", "mouse",
    "remote", "keyboard", "cell phone", "microwave", "oven", "toaster", "sink", "refrigerator", "book",
    "clock", "vase", "scissors", "teddy bear", "hair drier", "toothbrush",
]
# Large surfaces that are background, not a subject to cut out.
IGNORED_DETECTIONS = {"dining table", "bed", "couch"}


@dataclass(frozen=True, slots=True)
class DetectedObject:
    label: str
    score: float
    box: tuple[float, float, float, float]  # left, top, right, bottom in image pixels


def split_mask_components(mask: Image.Image, min_area_ratio: float = 0.005) -> list[Image.Image]:
    """One mask per separate (non-touching) region, largest first; specks are dropped."""
    import cv2
    import numpy as np

    values = np.array(mask.convert("L"))
    count, labels, stats, _ = cv2.connectedComponentsWithStats((values >= 128).astype(np.uint8), connectivity=8)
    min_area = max(1, int(values.size * min_area_ratio))
    grow = np.ones((5, 5), np.uint8)  # keep the soft anti-aliased edge just outside the solid region

    parts = []
    for index in sorted(range(1, count), key=lambda i: -stats[i, cv2.CC_STAT_AREA]):
        if stats[index, cv2.CC_STAT_AREA] < min_area:
            continue
        region = cv2.dilate((labels == index).astype(np.uint8), grow)
        parts.append(Image.fromarray(np.where(region > 0, values, 0).astype(np.uint8), "L"))
    return parts


class BackgroundRemover:
    _session_cache: dict[str, object] = {}
    _session_lock = threading.Lock()

    def __init__(self, model_name: str = "u2net") -> None:
        self.model_name = model_name

    def select_object(self, image: Image.Image, point: tuple[int, int]) -> Image.Image:
        """Mask of the single object under a clicked point (Segment Anything)."""
        prompt = [{"type": "point", "label": 1, "data": [int(point[0]), int(point[1])]}]
        return self.segment_prompts(image, [prompt])[0]

    def segment_prompts(self, image: Image.Image, prompts: list[list[dict]]) -> list[Image.Image]:
        """One Segment Anything mask per prompt. The costly image encoder runs once for all prompts."""
        import cv2
        import numpy as np

        try:
            # rembg's SamSession.predict re-encodes the image on every call (~2 s each). These are the same
            # steps split in two so several objects share one encoding. Keep in sync with rembg/sessions/sam.py.
            from rembg.sessions.sam import apply_coords, get_input_points, transform_masks

            session = self._get_session("sam")
            input_size, target_size = (684, 1024), 1024
            pixels = np.array(image.convert("RGB"))
            original_size = pixels.shape[:2]
            scale = min(input_size[1] / pixels.shape[1], input_size[0] / pixels.shape[0])
            transform_matrix = np.array([[scale, 0, 0], [0, scale, 0], [0, 0, 1]])
            warped = cv2.warpAffine(pixels, transform_matrix[:2], (input_size[1], input_size[0]), flags=cv2.INTER_LINEAR)
            embedding = session.encoder.run(None, {session.encoder.get_inputs()[0].name: warped.astype(np.float32)})[0]

            results = []
            for prompt in prompts:
                points, labels = get_input_points(prompt)
                coords = np.concatenate([points, np.array([[0.0, 0.0]])], axis=0)[None, :, :]
                point_labels = np.concatenate([labels, np.array([-1])], axis=0)[None, :].astype(np.float32)
                coords = apply_coords(coords, input_size, target_size).astype(np.float32)
                coords = np.concatenate([coords, np.ones((1, coords.shape[1], 1), dtype=np.float32)], axis=2)
                coords = np.matmul(coords, transform_matrix.T)[:, :, :2].astype(np.float32)

                masks, _, _ = session.decoder.run(None, {
                    "image_embeddings": embedding,
                    "point_coords": coords,
                    "point_labels": point_labels,
                    "mask_input": np.zeros((1, 1, 256, 256), dtype=np.float32),
                    "has_mask_input": np.zeros(1, dtype=np.float32),
                    "orig_im_size": np.array(input_size, dtype=np.float32),
                })
                masks = transform_masks(masks, original_size, np.linalg.inv(transform_matrix))
                combined = np.zeros(masks.shape[2:], dtype=np.uint8)
                for single in masks[0]:
                    combined[single > 0.0] = 255
                results.append(Image.fromarray(combined, "L").resize(image.size))
            return results
        except BackgroundRemovalError:
            raise
        except Exception as exc:
            raise BackgroundRemovalError(f"Object selection failed: {exc}") from exc

    def cut_detected_objects(self, image: Image.Image) -> list[tuple[str, Image.Image]]:
        """Each detected object as (label, mask): Mask R-CNN finds and names it, Segment Anything traces its edge."""
        objects = self.detect_objects(image)
        prompts = [[{"type": "rectangle", "label": 1, "data": [int(v) for v in item.box]}] for item in objects]
        masks = self.segment_prompts(image, prompts) if prompts else []
        return [(item.label, mask) for item, mask in zip(objects, masks) if mask.getbbox()]

    def detect_objects(self, image: Image.Image, min_score: float = 0.6) -> list[DetectedObject]:
        """Find and name objects (COCO classes) with Mask R-CNN, highest score first."""
        import numpy as np

        session = self._get_detection_session()
        rgb = image.convert("RGB")
        ratio = 800.0 / min(rgb.size)
        resized = rgb.resize((max(1, round(rgb.width * ratio)), max(1, round(rgb.height * ratio))), Image.Resampling.BILINEAR)

        # Model expects BGR, CHW, mean-subtracted, padded to a multiple of 32.
        pixels = np.array(resized)[:, :, ::-1].astype(np.float32).transpose(2, 0, 1)
        pixels -= np.array([102.9801, 115.9465, 122.7717], dtype=np.float32)[:, None, None]
        padded = np.zeros((3, -(-pixels.shape[1] // 32) * 32, -(-pixels.shape[2] // 32) * 32), dtype=np.float32)
        padded[:, : pixels.shape[1], : pixels.shape[2]] = pixels

        boxes, labels, scores, _ = session.run(None, {session.get_inputs()[0].name: padded})

        found = []
        for box, label, score in zip(boxes, labels, scores):
            name = COCO_CLASSES[int(label)] if 0 < int(label) < len(COCO_CLASSES) else "object"
            if score < min_score or name in IGNORED_DETECTIONS:
                continue
            left, top, right, bottom = (float(v) / ratio for v in box)
            found.append(DetectedObject(
                label=name,
                score=float(score),
                box=(max(0.0, left), max(0.0, top), min(float(image.width), right), min(float(image.height), bottom)),
            ))
        return sorted(found, key=lambda item: -item.score)

    def _get_detection_session(self):
        with self._session_lock:
            cached_session = self._session_cache.get(DETECTION_MODEL_FILE)
            if cached_session is not None:
                return cached_session

            import onnxruntime as ort
            import pooch

            model_path = pooch.retrieve(DETECTION_MODEL_URL, None, fname=DETECTION_MODEL_FILE, path=model_home())
            session = ort.InferenceSession(model_path, providers=["CPUExecutionProvider"])
            self._session_cache[DETECTION_MODEL_FILE] = session
            return session

    def _get_session(self, model_name: str | None = None):
        effective_model_name = model_name or self.model_name
        with self._session_lock:
            cached_session = self._session_cache.get(effective_model_name)
            if cached_session is not None:
                return cached_session

            try:
                from rembg import new_session
            except ImportError as exc:  # pragma: no cover - dependency guard
                raise BackgroundRemovalError(
                    "rembg is not installed. Install requirements.txt first."
                ) from exc

            options = SAM_OPTIONS if effective_model_name == "sam" else {}
            session = new_session(effective_model_name, **options)
            self._session_cache[effective_model_name] = session
            return session

    @staticmethod
    def load_image(input_path: Path) -> Image.Image:
        with Image.open(input_path) as source_image:
            return ImageOps.exif_transpose(source_image).convert("RGBA")

    def generate_mask(self, image: Image.Image) -> Image.Image:
        try:
            from rembg import remove
        except ImportError as exc:  # pragma: no cover - dependency guard
            raise BackgroundRemovalError(
                "rembg is not installed. Install requirements.txt first."
            ) from exc

        normalized_image = image.convert("RGBA")
        primary_candidate = self._generate_mask_candidate(remove, normalized_image, self.model_name)
        if not primary_candidate[2].should_retry:
            return primary_candidate[1]

        best_candidate = primary_candidate
        for fallback_model_name in self._iter_fallback_models():
            try:
                fallback_candidate = self._generate_mask_candidate(remove, normalized_image, fallback_model_name)
            except BackgroundRemovalError:
                continue

            if fallback_candidate[2].penalty < best_candidate[2].penalty:
                best_candidate = fallback_candidate
                if not fallback_candidate[2].should_retry:
                    break

        return best_candidate[1]

    @staticmethod
    def apply_mask(image: Image.Image, mask: Image.Image) -> Image.Image:
        normalized_image = image.convert("RGBA")
        normalized_mask = mask.convert("L").resize(normalized_image.size, Image.Resampling.LANCZOS)

        result = normalized_image.copy()
        result.putalpha(normalized_mask)
        return result

    def _generate_mask_candidate(self, remove_func, image: Image.Image, model_name: str) -> tuple[str, Image.Image, MaskQualityMetrics]:
        try:
            mask_image = remove_func(
                image,
                session=self._get_session(model_name),
                only_mask=True,
                post_process_mask=True,
            )
        except Exception as exc:
            raise BackgroundRemovalError(f"Failed to generate mask with model '{model_name}': {exc}") from exc

        if not isinstance(mask_image, Image.Image):
            raise BackgroundRemovalError("Mask generation did not return an image result.")

        normalized_mask = mask_image.convert("L")
        return model_name, normalized_mask, self._evaluate_mask_quality(normalized_mask)

    def _iter_fallback_models(self) -> list[str]:
        candidates = ["u2net", "isnet-general-use", "birefnet-general-lite", "bria-rmbg", "u2netp"]
        # Only retry with models already on disk: a retry must never start a multi-hundred-MB download mid-batch.
        return [name for name in candidates if name != self.model_name and is_model_downloaded(name)]

    @staticmethod
    def _evaluate_mask_quality(mask_image: Image.Image) -> MaskQualityMetrics:
        normalized_mask = mask_image.convert("L")
        width, height = normalized_mask.size
        total_pixels = max(1, width * height)

        histogram = normalized_mask.histogram()
        coverage_ratio = sum(histogram[128:]) / total_pixels

        bbox = normalized_mask.point(lambda value: 255 if value >= 128 else 0, mode="L").getbbox()
        if bbox is None:
            bbox_fill_ratio = 0.0
            touches_all_edges = False
        else:
            bbox_width = max(1, bbox[2] - bbox[0])
            bbox_height = max(1, bbox[3] - bbox[1])
            bbox_fill_ratio = (bbox_width * bbox_height) / total_pixels
            touches_all_edges = bbox[0] <= 1 and bbox[1] <= 1 and bbox[2] >= width - 1 and bbox[3] >= height - 1

        edge_total = max(1, (width * 2) + (height * 2) - 4)
        edge_foreground = 0
        pixels = normalized_mask.load()
        for x in range(width):
            edge_foreground += 1 if pixels[x, 0] >= 128 else 0
            if height > 1:
                edge_foreground += 1 if pixels[x, height - 1] >= 128 else 0
        for y in range(1, max(1, height - 1)):
            edge_foreground += 1 if pixels[0, y] >= 128 else 0
            if width > 1:
                edge_foreground += 1 if pixels[width - 1, y] >= 128 else 0
        edge_ratio = edge_foreground / edge_total

        penalty = abs(coverage_ratio - 0.28) * 24.0
        if coverage_ratio < 0.01:
            penalty += 140.0 + ((0.01 - coverage_ratio) * 2200.0)
        if coverage_ratio > 0.86:
            penalty += 140.0 + ((coverage_ratio - 0.86) * 400.0)
        if edge_ratio > 0.45:
            penalty += 110.0 + ((edge_ratio - 0.45) * 260.0)
        if bbox_fill_ratio > 0.94:
            penalty += 70.0 + ((bbox_fill_ratio - 0.94) * 220.0)
        if touches_all_edges:
            penalty += 60.0

        should_retry = (
            coverage_ratio < 0.01
            or coverage_ratio > 0.86
            or edge_ratio > 0.45
            or (bbox_fill_ratio > 0.94 and coverage_ratio > 0.45)
            or touches_all_edges
        )
        return MaskQualityMetrics(
            coverage_ratio=coverage_ratio,
            edge_ratio=edge_ratio,
            bbox_fill_ratio=bbox_fill_ratio,
            touches_all_edges=touches_all_edges,
            penalty=penalty,
            should_retry=should_retry,
        )
