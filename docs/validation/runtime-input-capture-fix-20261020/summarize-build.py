"""Summarize this frozen full build; run from the repository with Python 3.
Does not build, install, control the game or infer native acceptance from test totals.
"""
import hashlib
import json
import re
from datetime import datetime, timezone
from pathlib import Path

root = Path(__file__).resolve().parent
repo = root.parents[2]
log = root / "build.log"
body = log.read_text(encoding="utf-8-sig")
suites = []
for line in body.splitlines():
    match = re.fullmatch(r".*?(\d+)/(\d+).*passed\.", line.strip())
    if match:
        suites.append({"summary": line.strip(), "passed": int(match[1]), "total": int(match[2])})
assert len(suites) == 25, f"Missing or duplicate check suite: {len(suites)}"
assert all(suite["passed"] == suite["total"] for suite in suites)
assert sum(suite["total"] for suite in suites) == 644
assert "Package: " in body and "FAIL " not in body and not re.search(r"error (?:CS|MSB|NU)\d+", body)
summary = {
    "completedAtUtc": datetime.now(timezone.utc).isoformat(),
    "projects": len(suites), "passed": sum(s["passed"] for s in suites),
    "total": sum(s["total"] for s in suites), "suites": suites,
    "logSha256": hashlib.sha256(log.read_bytes()).hexdigest(),
    "nativeAcceptance": "Not installed or game-tested at build-summary time. Native helper is not part of the package.",
    "compilerDiagnosticLines": sorted(set(line.strip() for line in body.splitlines() if re.search(r"\b(?:warning|error) (?:CS|MSB|NU)\d+", line, re.I))),
    "precedingBuild": "../runtime-input-retirement-20261020/build.log belongs to the earlier 644 package with the stripped version-method failure"
}
(root / "build-summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
extensions = {".cs", ".csproj", ".props", ".targets", ".ps1", ".cpp", ".h", ".asm"}
files = []
for directory in ["src", "tests", "examples", "scripts", "native"]:
    for path in (repo / directory).rglob("*"):
        if path.is_file() and path.suffix in extensions and not {"bin", "obj", "out"}.intersection(path.relative_to(repo).parts):
            files.append({"path": path.relative_to(repo).as_posix(), "sha256": hashlib.sha256(path.read_bytes()).hexdigest()})
for name in ["Directory.Build.props", "Directory.Build.targets", "RizToolbox.sln"]:
    path = repo / name
    if path.is_file(): files.append({"path": name, "sha256": hashlib.sha256(path.read_bytes()).hexdigest()})
(root / "source-manifest.json").write_text(json.dumps({"files": sorted(files, key=lambda x: x["path"])}, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"projects": len(suites), "passed": summary["passed"], "total": summary["total"], "sourceFiles": len(files)}, ensure_ascii=False))
