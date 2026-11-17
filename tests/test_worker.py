import contextlib
import io
import json
import sys
import tempfile
import unittest
from pathlib import Path

from PIL import Image, ImageDraw

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "scripts"))

from nuggi.background_remover import BackgroundRemover  # noqa: E402
import nuggi_worker  # noqa: E402


def make_mask(fill: int, circle: bool = False) -> Image.Image:
    mask = Image.new("L", (100, 100), fill)
    if circle:
        ImageDraw.Draw(mask).ellipse((25, 25, 75, 75), fill=255)
    return mask


class MaskQualityTest(unittest.TestCase):
    def test_full_mask_retries(self):
        self.assertTrue(BackgroundRemover._evaluate_mask_quality(make_mask(255)).should_retry)

    def test_empty_mask_retries(self):
        self.assertTrue(BackgroundRemover._evaluate_mask_quality(make_mask(0)).should_retry)

    def test_centered_subject_is_accepted(self):
        metrics = BackgroundRemover._evaluate_mask_quality(make_mask(0, circle=True))
        self.assertFalse(metrics.should_retry)
        self.assertLess(metrics.penalty, BackgroundRemover._evaluate_mask_quality(make_mask(255)).penalty)


class BatchTest(unittest.TestCase):
    def test_bad_file_does_not_stop_the_batch(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            good = root / "good.png"
            Image.new("RGB", (100, 100), (200, 50, 50)).save(good)
            mask = root / "good_mask.png"
            make_mask(0, circle=True).save(mask)
            broken = root / "broken.png"
            broken.write_bytes(b"not an image")

            jobs = [
                {"input": str(broken), "output": str(root / "out" / "a.png"), "mask": str(root / "m1.png"), "trim": True},
                {"input": str(good), "output": str(root / "out" / "b.png"), "mask": str(mask), "trim": True},
            ]
            jobs_path = root / "jobs.json"
            jobs_path.write_text(json.dumps(jobs), encoding="utf-8")

            stdout = io.StringIO()
            with contextlib.redirect_stdout(stdout):
                nuggi_worker.run_batch(jobs_path, "u2net")

            lines = stdout.getvalue().splitlines()
            self.assertTrue(lines[0].startswith("FAIL\t0\t"))
            self.assertEqual(lines[1], "DONE\t1")
            with Image.open(root / "out" / "b.png") as result:
                self.assertEqual(result.mode, "RGBA")
                self.assertEqual(result.size, (53, 53))  # trimmed circle + 1px padding


if __name__ == "__main__":
    unittest.main()
