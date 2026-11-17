from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from PIL import Image

PROJECT_ROOT = Path(__file__).resolve().parent.parent
if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))

from nuggi.background_remover import BackgroundRemover, trim_transparent_padding


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Nuggi worker")
    subparsers = parser.add_subparsers(dest="command", required=True)

    mask_parser = subparsers.add_parser("mask", help="Generate a foreground mask")
    mask_parser.add_argument("--input", required=True)
    mask_parser.add_argument("--output", required=True)
    mask_parser.add_argument("--model", default="u2net")

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


def save_converted(input_path: Path, output_path: Path) -> None:
    output_path.parent.mkdir(parents=True, exist_ok=True)
    BackgroundRemover.load_image(input_path).save(output_path, format="PNG")


def run_job(remover: BackgroundRemover, job: dict) -> None:
    input_path = Path(job["input"])
    output_path = Path(job["output"])
    mask_path = Path(job["mask"])

    normalized = remover.load_image(input_path)

    if mask_path.is_file():
        with Image.open(mask_path) as source_mask:
            mask_image = source_mask.convert("L")
    else:
        mask_image = remover.generate_mask(normalized)
        mask_path.parent.mkdir(parents=True, exist_ok=True)
        mask_image.save(mask_path, format="PNG")

    result_image = remover.apply_mask(normalized, mask_image)
    if job.get("trim"):
        result_image = trim_transparent_padding(result_image)

    output_path.parent.mkdir(parents=True, exist_ok=True)
    result_image.save(output_path, format="PNG")


def run_batch(jobs_path: Path, model_name: str) -> None:
    jobs = json.loads(jobs_path.read_text(encoding="utf-8-sig"))  # accepts files with or without a BOM
    # One remover for the whole batch, so each model is loaded once instead of once per image.
    remover = BackgroundRemover(model_name=model_name)

    for index, job in enumerate(jobs):
        try:
            run_job(remover, job)
            print(f"DONE\t{index}", flush=True)
        except Exception as exc:
            message = str(exc).replace("\n", " ").replace("\t", " ") or type(exc).__name__
            print(f"FAIL\t{index}\t{message}", flush=True)


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()

    try:
        if args.command == "mask":
            save_mask(Path(args.input).resolve(), Path(args.output).resolve(), args.model)
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
