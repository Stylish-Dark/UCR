#!/usr/bin/env python3
"""Fail-closed checks on the *actual* WPF screenshot archive.

These checks do not certify aesthetics. They prevent missing, fake/empty, misnamed,
duplicated, or unexpectedly altered owner-approved visual evidence from being
treated as complete. Independent rendered-image review remains mandatory.
"""
from __future__ import annotations

import argparse
import hashlib
from pathlib import Path
import struct
import sys
import zlib

PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"
TINY_CAPTURE = "UCR-two-button-full-circles-preview.png"


def manifest_names(manifest: Path) -> list[str]:
    names = [line.strip() for line in manifest.read_text(encoding="utf-8-sig").splitlines()
             if line.strip() and not line.lstrip().startswith("#")]
    if not names or len(names) != len(set(names)):
        raise ValueError("Screenshot manifest missing or contains duplicate names")
    if any(Path(name).name != name or not name.endswith(".png") for name in names):
        raise ValueError("Manifest contains invalid/unsafe screenshot name")
    return names


def image_evidence(path: Path) -> tuple[int, int, str]:
    data = path.read_bytes()
    minimum = 500 if path.name == TINY_CAPTURE else 5000
    if len(data) < minimum or data[:8] != PNG_SIGNATURE:
        raise ValueError(f"{path.name}: missing, empty, or invalid PNG")
    offset = 8
    width = height = None
    payload = []
    completed = False
    while offset + 12 <= len(data):
        length = struct.unpack(">I", data[offset:offset + 4])[0]
        kind = data[offset + 4:offset + 8]
        end = offset + 12 + length
        if end > len(data):
            break
        body = data[offset + 8:offset + 8 + length]
        expected_crc = struct.unpack(">I", data[end - 4:end])[0]
        if zlib.crc32(kind + body) & 0xffffffff != expected_crc:
            raise ValueError(f"{path.name}: corrupted PNG chunk")
        if kind == b"IHDR":
            if width is not None or len(body) != 13:
                raise ValueError(f"{path.name}: malformed IHDR")
            width, height, depth, colour, compression, filtering, interlace = struct.unpack(">IIBBBBB", body)
            if (depth, colour, compression, filtering, interlace) != (8, 6, 0, 0, 0):
                raise ValueError(f"{path.name}: expected real 8-bit RGBA WPF render")
        elif kind == b"IDAT":
            payload.append(body)
        elif kind == b"IEND":
            completed = True
            offset = end
            break
        offset = end
    if not completed or offset != len(data) or not payload or width is None or height is None:
        raise ValueError(f"{path.name}: incomplete PNG")
    if width < 100 or height < 60 or width > 10000 or height > 10000:
        raise ValueError(f"{path.name}: implausible capture dimensions {width}x{height}")
    try:
        raw_pixels = zlib.decompress(b"".join(payload))
    except zlib.error as exc:
        raise ValueError(f"{path.name}: invalid pixel data") from exc
    if len(raw_pixels) != height * (1 + 4 * width):
        raise ValueError(f"{path.name}: pixel buffer has invalid dimensions")
    return width, height, hashlib.sha256(data).hexdigest()

def verify(screenshots: Path, manifest: Path, baselines: Path | None = None) -> list[str]:
    expected = manifest_names(manifest)
    actual = {path.name for path in screenshots.glob("*.png") if path.is_file()}
    missing = sorted(set(expected) - actual)
    extra = sorted(actual - set(expected))
    if missing or extra:
        raise ValueError(f"Screenshot inventory mismatch: missing={missing}; unexpected={extra}")
    if len(actual) != len(expected):
        raise ValueError("Screenshot inventory is not one-to-one")

    by_hash: dict[str, str] = {}
    checked: list[str] = []
    for name in expected:
        path = screenshots / name
        width, height, digest = image_evidence(path)
        if digest in by_hash:
            raise ValueError(
                f"Identical captures masquerading as different states: {by_hash[digest]} and {name}")
        by_hash[digest] = name
        if baselines is not None and (baselines / name).is_file():
            baseline = baselines / name
            baseline_dimensions = image_evidence(baseline)
            if baseline_dimensions != (width, height, digest):
                raise ValueError(
                    f"Protected visual baseline changed: {name}. "
                    "Do not regenerate the owner-approved baseline to silence CI.")
        checked.append(f"{name}: {width}x{height}, sha256={digest[:12]}")
    if baselines is not None and baselines.is_dir():
        unknown = sorted(p.name for p in baselines.glob("*.png")
                         if p.name not in set(expected))
        if unknown:
            raise ValueError(f"Orphaned approved visual baselines: {unknown}")
    return checked


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--screenshots", type=Path, required=True)
    parser.add_argument("--expected", type=Path, required=True)
    parser.add_argument("--baselines", type=Path, default=Path("quality/approved_visual_baselines"))
    args = parser.parse_args()
    try:
        result = verify(args.screenshots, args.expected, args.baselines)
    except (ValueError, OSError) as exc:
        print(f"VISUAL EVIDENCE REJECTED: {exc}", file=sys.stderr)
        return 1
    for line in result:
        print(line)
    print(f"Visual evidence complete: {len(result)} distinct real WPF PNG captures. "
          "This is not a visual-design approval.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
