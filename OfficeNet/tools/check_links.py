# OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.
"""Checks that every relative link and image in the Markdown resolves.

A broken link in documentation is invisible until a reader hits it, and the bilingual mirror
doubles the number of relative paths that can go wrong — `docs/id/WordNet.md` reaches the
screenshots through `../screenshots/`, one level deeper than its English counterpart.

    python tools/check_links.py

Exits non-zero and lists every broken target, so CI can fail on it.
"""

import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# Directories with nothing to check, and enough files to make the walk slow.
SKIP = {"bin", "obj", ".git", "node_modules", "BenchmarkDotNet.Artifacts", ".venv", "__pycache__"}

LINK = re.compile(r"!?\[[^\]]*\]\(([^)\s]+)(?:\s+\"[^\"]*\")?\)")


def markdown_files():
    for base, dirs, files in os.walk(ROOT):
        dirs[:] = [d for d in dirs if d not in SKIP]

        for name in files:
            if name.endswith(".md"):
                yield os.path.join(base, name)


def main():
    broken = []
    checked = 0

    for path in markdown_files():
        with open(path, encoding="utf-8") as handle:
            text = handle.read()

        for match in LINK.finditer(text):
            target = match.group(1)

            # External links and same-page anchors are out of scope: resolving them needs network
            # access, which would make this check flaky for no gain.
            if target.startswith(("http://", "https://", "mailto:", "#")):
                continue

            # Strip an anchor; the file has to exist, the heading is not verified.
            file_part = target.split("#")[0]

            if not file_part:
                continue

            resolved = os.path.normpath(os.path.join(os.path.dirname(path), file_part))
            checked += 1

            if not os.path.exists(resolved):
                relative = os.path.relpath(path, ROOT).replace(os.sep, "/")
                broken.append(f"{relative}: {target}")

    if broken:
        print(f"{len(broken)} broken link(s):\n")

        for item in broken:
            print(f"  {item}")

        return 1

    print(f"All {checked} relative links resolve.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
