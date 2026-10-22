from __future__ import annotations

import argparse
import sys
from pathlib import Path

from PIL import Image

PROJECT_ROOT = Path(__file__).resolve().parent.parent
if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))

from nuggi.background_remover import BackgroundRemovalError, BackgroundRemover, trim_transparent_padding


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Nuggi worker")
    subparsers = parser.add_subparsers(dest="command", required=True)

    preview_parser = subparsers.add_parser("preview", help="Generate a cutout preview")
    preview_parser.add_argument("--input", required=True)
    preview_parser.add_argument("--output", required=True)
    preview_parser.add_argument("--model", default="u2net")

    mask_parser = subparsers.add_parser("mask", help="Generate a foreground mask")
    mask_parser.add_argument("--input", required=True)
    mask_parser.add_argument("--output", required=True)
    mask_parser.add_argument("--model", default="u2net")

    remove_parser = subparsers.add_parser("remove", help="Remove background and save PNG")
    remove_parser.add_argument("--input", required=True)
    remove_parser.add_argument("--output", required=True)
    remove_parser.add_argument("--model", default="u2net")
    remove_parser.add_argument("--mask")
    remove_parser.add_argument("--trim", action="store_true")

    return parser


def save_preview(input_path: Path, output_path: Path, model_name: str) -> None:
    remover = BackgroundRemover(model_name=model_name)
    _, preview = remover.build_cutout_preview(input_path)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    preview.save(output_path, format="PNG")


def save_mask(input_path: Path, output_path: Path, model_name: str) -> None:
    remover = BackgroundRemover(model_name=model_name)
    output_path.parent.mkdir(parents=True, exist_ok=True)

    normalized = remover.load_image(input_path)
    mask_image = remover.generate_mask(normalized)
    mask_image.save(output_path, format="PNG")


def save_removed_image(
    input_path: Path,
    output_path: Path,
    model_name: str,
    trim: bool,
    mask_path: Path | None,
) -> None:
    remover = BackgroundRemover(model_name=model_name)
    output_path.parent.mkdir(parents=True, exist_ok=True)

    normalized = remover.load_image(input_path)

    if mask_path is not None:
        with Image.open(mask_path) as source_mask:
            mask_image = source_mask.convert("L")

        result_image = remover.apply_mask(normalized, mask_image)
    else:
        result_image = remover.remove_image(normalized)

    if trim:
        result_image = trim_transparent_padding(result_image)

    result_image.save(output_path, format="PNG")


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()

    try:
        input_path = Path(args.input).expanduser().resolve()
        output_path = Path(args.output).expanduser().resolve()

        if args.command == "preview":
            save_preview(input_path, output_path, args.model)
        elif args.command == "mask":
            save_mask(input_path, output_path, args.model)
        elif args.command == "remove":
            mask_path = Path(args.mask).expanduser().resolve() if args.mask else None
            save_removed_image(input_path, output_path, args.model, args.trim, mask_path)
        else:  # pragma: no cover - defensive fallback
            parser.error("Unknown command")
            return 1
    except BackgroundRemovalError as exc:
        print(str(exc), file=sys.stderr)
        return 1
    except Exception as exc:  # pragma: no cover - process surface
        print(str(exc), file=sys.stderr)
        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())
