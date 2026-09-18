#!/usr/bin/env python3
"""Fail closed unless the release PDB uses a checkout-independent Source Link map."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import re


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("source_link", type=Path)
    parser.add_argument("revision")
    args = parser.parse_args()

    if re.fullmatch(r"[0-9a-f]{40}", args.revision) is None:
        raise ValueError("release revision must be one full lowercase commit hash")
    if not args.source_link.is_file():
        raise FileNotFoundError(f"Source Link output missing: {args.source_link}")

    actual = json.loads(args.source_link.read_text(encoding="utf-8"))
    expected = {
        "documents": {
            "/_/*": (
                "https://raw.githubusercontent.com/JStack424/Stackmaster/"
                f"{args.revision}/*"
            )
        }
    }
    if actual != expected:
        raise ValueError(
            "Source Link is not checkout-independent or does not match the pinned release revision: "
            + json.dumps(actual, sort_keys=True)
        )

    print("verified checkout-independent Source Link map")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
