"""Verify this batch's published local destinations, excluding anchors and URLs."""
import json
import re
from pathlib import Path
from urllib.parse import unquote

batch = Path(__file__).resolve().parent
repo = batch.parents[2]
files = [repo / name for name in ["README.md", "docs/IMPLEMENTATION-STATUS.md", ".docs/CURRENT-CHECKPOINT.md",
    ".docs/foundation-expansion/CHECKLIST.md", "docs/2026-10-25_底层-引擎协程节点观察-report.md"]] + sorted(batch.glob("*.md"))
missing, checked = [], 0
for path in files:
    for match in re.finditer(r"\[[^\]\n]*\]\(([^)\n]+)\)", path.read_text(encoding="utf-8-sig")):
        target = match.group(1).strip().strip("<>")
        if target.startswith("#") or "://" in target or target.startswith("mailto:"): continue
        target = unquote(target.split("#", 1)[0])
        if not target: continue
        checked += 1
        if not (path.parent / target).resolve().exists(): missing.append(dict(source=path.relative_to(repo).as_posix(), target=target))
proof = dict(files=[p.relative_to(repo).as_posix() for p in files], checkedLocalLinks=checked, missing=missing,
    scope="Published local Markdown destinations in this batch and updated entry docs; excludes anchors/external URLs and unrelated history.")
(batch / "documentation-check.json").write_text(json.dumps(proof, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps(dict(files=len(files), checkedLocalLinks=checked, missing=missing), ensure_ascii=False))
assert not missing
