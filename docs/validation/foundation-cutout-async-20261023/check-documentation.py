"""Check this batch's linked files and reported totals, without rebuilding."""
import json
import re
from pathlib import Path
from urllib.parse import unquote

root = Path(__file__).resolve().parent
repo = root.parents[2]
documents = [repo / name for name in [
    "README.md", ".docs/CURRENT-CHECKPOINT.md", ".docs/foundation-expansion/CHECKLIST.md",
    "docs/IMPLEMENTATION-STATUS.md", "docs/2026-10-22_架构-InFalsusTools-reference-report.md",
    "docs/2026-10-23_底层-Cutout直接异步协程-report.md",
]] + [root / name for name in ["README.md", "scope.md", "review.md", "next-integration.md"]]
missing, checked = [], 0
for document in documents:
    body = document.read_text(encoding="utf-8-sig")
    body = re.sub(r"```.*?```", "", body, flags=re.S)
    for target in re.findall(r"\]\(([^)]+)\)", body):
        target = target.strip().strip("<>")
        if re.match(r"(?:[a-zA-Z][\w+.-]*:|#)", target):
            continue
        target = unquote(target.split("#", 1)[0])
        if not target:
            continue
        checked += 1
        if not (document.parent / target).resolve().exists():
            missing.append(dict(document=document.relative_to(repo).as_posix(), target=target))
summary = json.loads((root / "build-summary.json").read_text(encoding="utf-8-sig"))
assert summary["passed"] == summary["total"] == 1412 and summary["projects"] == 25
checklist = (repo / ".docs/foundation-expansion/CHECKLIST.md").read_text(encoding="utf-8-sig")
marked = re.findall(r"^- \[[xX]\] (B\d+[a-z]?)\b", checklist, flags=re.M)
assert marked == ["B03", "B18a"]
report = dict(documents=len(documents), localLinksChecked=checked, missing=missing,
    fullBuildPassed=summary["passed"], suites=summary["projects"], markedItems=marked)
(root / "documentation-check.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
assert not missing, missing
print(json.dumps(report, ensure_ascii=False))
