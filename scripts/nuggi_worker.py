from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from PIL import Image

PROJECT_ROOT = Path(__file__).resolve().parent.parent
if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))

from nuggi.background_remover import (
    BackgroundRemovalError,
    BackgroundRemover,
    split_mask_components,
    trim_transparent_padding,
)

# Batch save modes (the "mode" field of a job).
MODE_SINGLE = "single"      # one PNG with everything the mask keeps
MODE_SPLIT = "split"        # one PNG per separate region of the mask
MODE_OBJECTS = "objects"    # one PNG per detected object, named by its kind


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Nuggi worker")
    subparsers = parser.add_subparsers(dest="command", required=True)

    mask_parser = subparsers.add_parser("mask", help="Generate a foreground mask")
    mask_parser.add_argument("--input", required=True)
    mask_parser.add_argument("--output", required=True)
    mask_parser.add_argument("--model", default="u2net")

    select_parser = subparsers.add_parser("select", help="Mask of the object under a point")
    select_parser.add_argument("--input", required=True)
    select_parser.add_argument("--output", required=True)
    select_parser.add_argument("--x", type=int, required=True)
    select_parser.add_argument("--y", type=int, required=True)

    convert_parser = subparsers.add_parser("convert", help="Save an image as PNG (for formats the UI cannot open)")
    convert_parser.add_argument("--input", required=True)
    convert_parser.add_argument("--output", required=True)

    batch_parser = subparsers.add_parser("batch", help="Remove backgrounds for a JSON list of jobs in one process")
    batch_parser.add_argument("--jobs", required=True)
    batch_parser.add_argument("--model", default="u2net")

    return parser


def save_mask(input_path: Path, output_path: Path, model_name: str) -> None:
    remover = BackgroundRemover(model_name=model_name)
    output_path.parent.mkdir(parents=True, exist_ok=True)

    normalized = remover.load_image(input_path)
    mask_image = remover.generate_mask(normalized)
    mask_image.save(output_path, format="PNG")


def save_selection(input_path: Path, output_path: Path, x: int, y: int) -> None:
    remover = BackgroundRemover()
    output_path.parent.mkdir(parents=True, exist_ok=True)
    remover.select_object(remover.load_image(input_path), (x, y)).save(output_path, format="PNG")


def save_converted(input_path: Path, output_path: Path) -> None:
    output_path.parent.mkdir(parents=True, exist_ok=True)
    BackgroundRemover.load_image(input_path).save(output_path, format="PNG")


def unique_path(candidate: Path) -> Path:
    path, counter = candidate, 1
    while path.exists():
        path = candidate.with_name(f"{candidate.stem}-{counter}{candidate.suffix}")
        counter += 1
    return path


def load_or_create_mask(remover: BackgroundRemover, image: Image.Image, mask_path: Path) -> Image.Image:
    if mask_path.is_file():
        with Image.open(mask_path) as source_mask:
            return source_mask.convert("L")

    mask_image = remover.generate_mask(image)
    mask_path.parent.mkdir(parents=True, exist_ok=True)
    mask_image.save(mask_path, format="PNG")
    return mask_image


def run_job(remover: BackgroundRemover, job: dict) -> list[Path]:
    """Saves the job's cutout(s) and returns the written files."""
    output_path = Path(job["output"])
    mode = job.get("mode", MODE_SINGLE)
    image = remover.load_image(Path(job["input"]))

    if mode == MODE_OBJECTS:
        # Detection decides the regions, so the saved/edited mask is not used here.
        named_masks = remover.cut_detected_objects(image)
        if not named_masks:
            raise BackgroundRemovalError("No known objects were found in this image.")
        pieces = [
            (output_path.with_name(f"{output_path.stem}_{number}_{label.replace(' ', '-')}.png"), mask)
            for number, (label, mask) in enumerate(named_masks, start=1)
        ]
    else:
        mask_image = load_or_create_mask(remover, image, Path(job["mask"]))
        if mode == MODE_SPLIT:
            parts = split_mask_components(mask_image)
            if not parts:
                raise BackgroundRemovalError("The mask is empty, so there is nothing to split.")
            pieces = [
                (output_path.with_name(f"{output_path.stem}_{number}.png"), part)
                for number, part in enumerate(parts, start=1)
            ]
        else:
            pieces = [(output_path, mask_image)]

    output_path.parent.mkdir(parents=True, exist_ok=True)
    written = []
    for path, mask in pieces:
        result_image = remover.apply_mask(image, mask)
        if job.get("trim"):
            result_image = trim_transparent_padding(result_image)
        # The app picks a free name for single saves; split/object names are made here, so never overwrite.
        path = path if mode == MODE_SINGLE else unique_path(path)
        result_image.save(path, format="PNG")
        written.append(path)
    return written


def run_batch(jobs_path: Path, model_name: str) -> None:
    jobs = json.loads(jobs_path.read_text(encoding="utf-8-sig"))  # accepts files with or without a BOM
    # One remover for the whole batch, so each model is loaded once instead of once per image.
    remover = BackgroundRemover(model_name=model_name)

    for index, job in enumerate(jobs):
        try:
            written = run_job(remover, job)
            print(f"DONE\t{index}\t{len(written)}", flush=True)
        except Exception as exc:
            message = str(exc).replace("\n", " ").replace("\t", " ") or type(exc).__name__
            print(f"FAIL\t{index}\t{message}", flush=True)


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()

    try:
        if args.command == "mask":
            save_mask(Path(args.input).resolve(), Path(args.output).resolve(), args.model)
        elif args.command == "select":
            save_selection(Path(args.input).resolve(), Path(args.output).resolve(), args.x, args.y)
        elif args.command == "convert":
            save_converted(Path(args.input).resolve(), Path(args.output).resolve())
        elif args.command == "batch":
            run_batch(Path(args.jobs).resolve(), args.model)
    except Exception as exc:  # pragma: no cover - process surface
        print(str(exc), file=sys.stderr)
        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())
