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


# Segment Anything (click -> one object's mask). Quantized vit_b: ~113 MB, CPU friendly.
SAM_OPTIONS = {"sam_model": "sam_vit_b_01ec64", "sam_quant": True}


class BackgroundRemover:
    _session_cache: dict[str, object] = {}
    _session_lock = threading.Lock()

    def __init__(self, model_name: str = "u2net") -> None:
        self.model_name = model_name

    def select_object(self, image: Image.Image, point: tuple[int, int]) -> Image.Image:
        """Mask of the single object under a clicked point (Segment Anything)."""
        prompt = [{"type": "point", "label": 1, "data": [int(point[0]), int(point[1])]}]
        try:
            masks = self._get_session("sam").predict(image.convert("RGB"), sam_prompt=prompt)
        except BackgroundRemovalError:
            raise
        except Exception as exc:
            raise BackgroundRemovalError(f"Object selection failed: {exc}") from exc
        return masks[0].convert("L").resize(image.size)

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
