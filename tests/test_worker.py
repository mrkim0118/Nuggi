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

from nuggi.background_remover import BackgroundRemover, split_mask_components  # noqa: E402
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
            # utf-8-sig adds a BOM, like .NET Encoding.UTF8 does; the worker must still read it.
            jobs_path.write_text(json.dumps(jobs, ensure_ascii=False), encoding="utf-8-sig")

            stdout = io.StringIO()
            with contextlib.redirect_stdout(stdout):
                nuggi_worker.run_batch(jobs_path, "u2net")

            lines = stdout.getvalue().splitlines()
            self.assertTrue(lines[0].startswith("FAIL\t0\t"))
            self.assertEqual(lines[1], "DONE\t1\t1")
            with Image.open(root / "out" / "b.png") as result:
                self.assertEqual(result.mode, "RGBA")
                self.assertEqual(result.size, (53, 53))  # trimmed circle + 1px padding

    def test_split_mode_saves_each_region_without_overwriting(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            source = root / "two.png"
            Image.new("RGB", (200, 100), (10, 120, 200)).save(source)
            mask = root / "mask.png"
            two_blobs().save(mask)
            out_dir = root / "out"
            out_dir.mkdir()
            (out_dir / "two_nuggi_1.png").write_bytes(b"existing")  # a file from an earlier run

            job = {"input": str(source), "output": str(out_dir / "two_nuggi.png"), "mask": str(mask), "trim": True, "mode": "split"}
            written = nuggi_worker.run_job(BackgroundRemover(), job)

            self.assertEqual([path.name for path in written], ["two_nuggi_1-1.png", "two_nuggi_2.png"])
            self.assertEqual((out_dir / "two_nuggi_1.png").read_bytes(), b"existing")
            with Image.open(written[0]) as larger:
                self.assertGreater(larger.width, 55)  # the bigger blob comes first


def two_blobs() -> Image.Image:
    mask = Image.new("L", (200, 100), 0)
    draw = ImageDraw.Draw(mask)
    draw.ellipse((10, 10, 80, 80), fill=255)     # big region
    draw.ellipse((130, 30, 170, 70), fill=255)   # smaller region
    draw.point((195, 95), fill=255)              # speck, must be ignored
    return mask


class SplitMaskTest(unittest.TestCase):
    def test_separate_regions_become_separate_masks(self):
        parts = split_mask_components(two_blobs())
        self.assertEqual(len(parts), 2)
        self.assertEqual(parts[0].getbbox(), (10, 10, 81, 81))  # exactly the big blob
        self.assertIsNone(parts[0].crop((120, 0, 200, 100)).getbbox())  # the other blob is not in it

    def test_empty_mask_has_no_parts(self):
        self.assertEqual(split_mask_components(Image.new("L", (50, 50), 0)), [])


if __name__ == "__main__":
    unittest.main()
