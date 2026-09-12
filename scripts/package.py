#!/usr/bin/env python3
"""Build and verify the deterministic Thunderstore Stackmaster release ZIP.

The script has no upload capability. It packages only an explicit allowlist from
a clean committed checkout and the already-built Release plugin DLL.
"""

from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
from pathlib import Path
import re
import struct
import subprocess
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[1]
PACKAGE = ROOT / "packages" / "Stackmaster"
DLL = ROOT / "src" / "Stackmaster" / "bin" / "Release" / "Stackmaster.dll"
VERSION = "0.2.1"
DEPENDENCY = "denikson-BepInExPack_Valheim-5.4.2350"
EXPECTED = (
    "manifest.json",
    "icon.png",
    "README.md",
    "CHANGELOG.md",
    "plugins/Stackmaster/Stackmaster.dll",
)
SOURCE = {
    "manifest.json": PACKAGE / "manifest.json",
    "icon.png": PACKAGE / "icon.png",
    "README.md": PACKAGE / "README.md",
    "CHANGELOG.md": PACKAGE / "CHANGELOG.md",
    "plugins/Stackmaster/Stackmaster.dll": DLL,
}


def run_git(*args: str) -> str:
    return subprocess.check_output(["git", *args], cwd=ROOT, text=True).strip()


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def validate_png(data: bytes) -> None:
    if len(data) < 33 or data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError("icon.png is not a valid PNG")
    length = struct.unpack(">I", data[8:12])[0]
    if data[12:16] != b"IHDR" or length != 13:
        raise ValueError("icon.png does not start with a valid IHDR")
    width, height, bit_depth, color_type = struct.unpack(">IIBB", data[16:26])
    if (width, height) != (256, 256):
        raise ValueError(f"icon.png must be 256x256, got {width}x{height}")
    if bit_depth != 8 or color_type != 6:
        raise ValueError("icon.png must be 8-bit RGBA")


def validate_manifest(data: bytes) -> None:
    manifest = json.loads(data.decode("utf-8"))
    if manifest != {
        "name": "Stackmaster",
        "version_number": VERSION,
        "website_url": "https://github.com/JStack424/Stackmaster",
        "description": "Turn a messy Viking inventory into a tidy, adventure-ready loadout.",
        "dependencies": [DEPENDENCY],
    }:
        raise ValueError("manifest.json does not match the approved release metadata")
    if not re.fullmatch(r"[A-Za-z0-9_]+-[A-Za-z0-9_]+-\d+\.\d+\.\d+", DEPENDENCY):
        raise ValueError("dependency string is not a valid Thunderstore dependency")


def validate_dll(data: bytes) -> None:
    if len(data) < 0x40 or data[:2] != b"MZ" or b"PE\x00\x00" not in data[:1024]:
        raise ValueError("Stackmaster.dll is not a Windows PE assembly")
    for marker in (b"Stackmaster", b"com.jstack424.stackmaster", VERSION.encode("ascii")):
        if marker not in data and marker.decode().encode("utf-16le") not in data:
            raise ValueError(f"Stackmaster.dll is missing identity marker {marker!r}")
    for leaked in (b"/home/hatch", b"C:\\Users\\", b"StackmasterReferences"):
        if leaked.lower() in data.lower():
            raise ValueError(f"Stackmaster.dll contains a private path marker: {leaked!r}")


def validate_files(files: dict[str, bytes]) -> None:
    if tuple(files) != EXPECTED:
        raise ValueError(f"unexpected ZIP allowlist/order: {tuple(files)!r}")
    for name, data in files.items():
        if not data:
            raise ValueError(f"{name} is empty")
    validate_manifest(files["manifest.json"])
    validate_png(files["icon.png"])
    validate_dll(files["plugins/Stackmaster/Stackmaster.dll"])
    forbidden_suffixes = (".pdb", ".cs", ".csproj", ".sln", ".json.lock")
    for name in files:
        lower = name.lower()
        if lower.endswith(forbidden_suffixes):
            raise ValueError(f"development file is forbidden: {name}")
        if any(token in lower for token in ("bepinex.dll", "harmony", "unity", "assembly_valheim", "stackmaster.core.dll", "test")):
            raise ValueError(f"private/runtime/test artifact is forbidden: {name}")


def load_sources() -> dict[str, bytes]:
    missing = [str(path) for path in SOURCE.values() if not path.is_file()]
    if missing:
        raise FileNotFoundError("missing package input(s): " + ", ".join(missing))
    return {name: SOURCE[name].read_bytes() for name in EXPECTED}


def verify_zip(path: Path) -> dict[str, bytes]:
    with zipfile.ZipFile(path) as archive:
        infos = archive.infolist()
        names = tuple(info.filename for info in infos)
        if names != EXPECTED:
            raise ValueError(f"ZIP paths do not match allowlist/order: {names!r}")
        if any(info.is_dir() for info in infos):
            raise ValueError("ZIP must not contain directory entries")
        files = {info.filename: archive.read(info) for info in infos}
        if archive.testzip() is not None:
            raise ValueError("ZIP CRC validation failed")
    validate_files(files)
    return files


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts" / f"JStack424-Stackmaster-{VERSION}.zip")
    parser.add_argument("--verify-only", type=Path)
    args = parser.parse_args()

    if args.verify_only:
        files = verify_zip(args.verify_only)
        print(f"verified {args.verify_only}")
        for name, data in files.items():
            print(f"{sha256(data)}  {name}")
        print(f"{sha256(args.verify_only.read_bytes())}  {args.verify_only.name}")
        return 0

    if run_git("status", "--porcelain", "--untracked-files=normal"):
        raise RuntimeError("refusing to package a dirty or uncommitted working tree")
    head = run_git("rev-parse", "HEAD")
    commit_time = int(run_git("show", "-s", "--format=%ct", "HEAD"))
    timestamp = dt.datetime.fromtimestamp(commit_time, tz=dt.timezone.utc)
    if timestamp.year < 1980:
        timestamp = timestamp.replace(year=1980)
    date_time = (timestamp.year, timestamp.month, timestamp.day, timestamp.hour, timestamp.minute, timestamp.second // 2 * 2)

    files = load_sources()
    validate_files(files)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.unlink(missing_ok=True)
    with zipfile.ZipFile(args.output, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name, data in files.items():
            info = zipfile.ZipInfo(name, date_time=date_time)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.create_system = 3
            info.external_attr = 0o100644 << 16
            archive.writestr(info, data, compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)

    verified = verify_zip(args.output)
    print(f"source commit: {head}")
    print(f"created and verified: {args.output}")
    for name, data in verified.items():
        print(f"{sha256(data)}  {name}")
    print(f"{sha256(args.output.read_bytes())}  {args.output.name}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(f"package failed: {exc}", file=sys.stderr)
        raise SystemExit(1)
