"""Executable regression checks for the fail-closed visual evidence gate."""
from __future__ import annotations

import importlib.util
from pathlib import Path
import random
import struct
import tempfile
import unittest
import zlib

MODULE = Path(__file__).resolve().parents[1] / "build/quality/verify_visual_artifacts.py"
spec = importlib.util.spec_from_file_location("visual_artifacts", MODULE)
guard = importlib.util.module_from_spec(spec)
spec.loader.exec_module(guard)


def chunk(kind: bytes, content: bytes) -> bytes:
    return (struct.pack(">I", len(content)) + kind + content
            + struct.pack(">I", zlib.crc32(kind + content) & 0xffffffff))


def image_bytes(seed: int) -> bytes:
    randomizer = random.Random(seed)
    width, height = 120, 80
    pixels = b"".join(
        b"\x00" + randomizer.randbytes(width * 4) for _ in range(height))
    header = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    return (guard.PNG_SIGNATURE + chunk(b"IHDR", header)
            + chunk(b"IDAT", zlib.compress(pixels))
            + chunk(b"IEND", b""))


class VisualGuardTests(unittest.TestCase):
    def fixture(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        root = Path(tmp.name)
        shots = root / "screenshots"
        shots.mkdir()
        manifest = root / "expected.txt"
        manifest.write_text("a.png\nb.png\n", encoding="utf-8")
        (shots / "a.png").write_bytes(image_bytes(1))
        (shots / "b.png").write_bytes(image_bytes(2))
        return root, shots, manifest

    def test_distinct_valid_pngs_pass(self):
        _, shots, manifest = self.fixture()
        self.assertEqual(len(guard.verify(shots, manifest)), 2)

    def test_duplicate_capture_is_rejected(self):
        _, shots, manifest = self.fixture()
        (shots / "b.png").write_bytes((shots / "a.png").read_bytes())
        with self.assertRaisesRegex(ValueError, "Identical captures"):
            guard.verify(shots, manifest)

    def test_missing_and_extra_captures_fail(self):
        _, shots, manifest = self.fixture()
        (shots / "b.png").unlink()
        with self.assertRaisesRegex(ValueError, "inventory mismatch"):
            guard.verify(shots, manifest)
        (shots / "b.png").write_bytes(image_bytes(2))
        (shots / "unreviewed.png").write_bytes(image_bytes(3))
        with self.assertRaisesRegex(ValueError, "inventory mismatch"):
            guard.verify(shots, manifest)

    def test_pixel_corruption_fails(self):
        _, shots, manifest = self.fixture()
        original = bytearray((shots / "a.png").read_bytes())
        original[90] ^= 0xff
        (shots / "a.png").write_bytes(original)
        with self.assertRaisesRegex(ValueError, "corrupted PNG"):
            guard.verify(shots, manifest)

    def test_approved_baseline_cannot_drift(self):
        root, shots, manifest = self.fixture()
        baselines = root / "approved"
        baselines.mkdir()
        (baselines / "a.png").write_bytes((shots / "a.png").read_bytes())
        self.assertEqual(len(guard.verify(shots, manifest, baselines)), 2)
        (shots / "a.png").write_bytes(image_bytes(3))
        with self.assertRaisesRegex(ValueError, "Protected visual baseline changed"):
            guard.verify(shots, manifest, baselines)

    def test_full_project_manifest_includes_every_produced_state(self):
        actual_manifest = MODULE.parents[2] / "build/quality/expected_screenshots.txt"
        names = guard.manifest_names(actual_manifest)
        self.assertEqual(len(names), 22)
        self.assertIn("UCR-two-button-full-circles-preview.png", names)
        self.assertIn("UCR-output-picker-single-axis-preview.png", names)


if __name__ == "__main__":
    unittest.main()
