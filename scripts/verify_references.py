#!/usr/bin/env python3
from __future__ import annotations
import hashlib
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
FINGERPRINTS = ROOT / "scripts" / "reference-fingerprints.json"
REQUIRED = ROOT / "scripts" / "required-references.txt"


def main() -> int:
    if len(sys.argv) != 2:
        print("usage: verify_references.py DIRECTORY", file=sys.stderr)
        return 2
    directory = Path(sys.argv[1]).expanduser().resolve()
    required = [line.strip() for line in REQUIRED.read_text().splitlines() if line.strip()]
    data = json.loads(FINGERPRINTS.read_text())
    expected = data["files"]
    if sorted(required) != sorted(expected):
        raise RuntimeError("required-references.txt and reference-fingerprints.json disagree")
    for name in required:
        path = directory / name
        if not path.is_file():
            raise FileNotFoundError(f"missing private reference: {path}")
        digest = hashlib.sha256(path.read_bytes()).hexdigest()
        if digest != expected[name]:
            raise RuntimeError(f"private reference hash mismatch: {name}")
    print(f"verified {len(required)} private references: {data['runtime_label']}", file=sys.stderr)
    print(directory)
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(f"reference validation failed: {exc}", file=sys.stderr)
        raise SystemExit(1)
