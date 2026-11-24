"""Check this batch's published local Markdown links, not the whole historical repo."""
import json
import re
from pathlib import Path
from urllib.parse import unquote

batch = Path(__file__).resolve().parent
repo = batch.parents[2]
files = [repo / name for name in [
    "README.md", "docs/IMPLEMENTATION-STATUS.md", ".docs/CURRENT-CHECKPOINT.md",
    ".docs/foundation-expansion/CHECKLIST.md",
    "docs/2026-10-22_架构-InFalsusTools-reference-report.md",
    "docs/2026-10-24_底层-监听调用原生层-report.md",
]] + sorted(batch.glob("*.md"))
missing = []
checked = 0
for path in files:
    body = path.read_text(encoding="utf-8-sig")
    for match in re.finditer(r"\[[^\]\n]*\]\(([^)\n]+)\)", body):
        target = match.group(1).strip().strip("<>")
        if target.startswith("#") or "://" in target or target.startswith("mailto:"):
            continue
        target = unquote(target.split("#", 1)[0])
        if not target:
            continue
        checked += 1
        destination = (path.parent / target).resolve()
        if destination != (batch / "documentation-check.json").resolve() and not destination.exists():
            missing.append(dict(source=path.relative_to(repo).as_posix(), target=target))
proof = dict(files=[path.relative_to(repo).as_posix() for path in files],
             checkedLocalLinks=checked, missing=missing,
             scope="Published local Markdown destinations in this batch and its updated entry documents; does not validate anchors or external URLs.")
(batch / "documentation-check.json").write_text(json.dumps(proof, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps(dict(files=len(files), checkedLocalLinks=checked, missing=missing), ensure_ascii=False))
assert not missing
