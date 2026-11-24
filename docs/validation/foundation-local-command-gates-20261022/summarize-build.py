"""Freeze this batch's completed build and verify its actual installed package.

No game process launch, UI action, or installation is performed here.
"""
import argparse
import hashlib
import json
import re
import shutil
from datetime import datetime, timezone
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("--installed", action="store_true")
parser.add_argument("--staged", action="store_true", help="Verify only the new staged artifact and source set, preserving the installed 999 package.")
parser.add_argument("--native-output", type=Path, default=Path(r"D:\RizToolbox-build-cache\local-command-gates-20261022"))
args = parser.parse_args()
assert not (args.installed and args.staged), "Choose installed or staged verification, not both."
root = Path(__file__).resolve().parent
repo = root.parents[2]
game = Path(r"D:\SteamLibrary\steamapps\common\Rizline")


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write(name, data):
    (root / name).write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if not (args.installed or args.staged):
    log = root / "build.log"
    body = log.read_text(encoding="utf-8-sig")
    suites = []
    for line in body.splitlines():
        match = re.fullmatch(r".*?(\d+)/(\d+).*passed\.", line.strip())
        if match:
            suites.append(dict(summary=line.strip(), passed=int(match[1]), total=int(match[2])))
    assert len(suites) == 25 and all(s["passed"] == s["total"] for s in suites)
    assert "Package: " in body and "FAIL " not in body and not re.search(r"error (?:CS|MSB|NU)\d+", body)
    fixtures = [json.loads(line) for line in body.splitlines() if line.startswith('{"schemaVersion":1,"kind":"real-windows-x64-native-fixture"')]
    assert len(fixtures) == 2 and all(f["passed"] and f["inFlight"] == 0 for f in fixtures)
    assert fixtures[1]["realDobbyPreparedCommit"]
    total = sum(s["total"] for s in suites)
    write("build-summary.json", dict(completedAtUtc=datetime.now(timezone.utc).isoformat(), projects=len(suites),
        passed=total, total=total, suites=suites, nativeFixtures=fixtures, logSha256=sha(log),
        nativeAcceptance="Packaged helper and Dobby fixtures only; LOCAL command boundary game acceptance remains separate; no playable LOCAL run is claimed.",
        compilerDiagnosticLines=sorted(set(line.strip() for line in body.splitlines() if re.search(r"\b(?:warning|error) (?:CS|MSB|NU)\d+", line, re.I)))))
    files = []
    for directory in ["src", "tests", "examples", "scripts", "native", "templates", "tools", "packaging"]:
        for path in (repo / directory).rglob("*"):
            if path.is_file() and path.suffix in {".cs", ".csproj", ".props", ".targets", ".ps1", ".cpp", ".h", ".asm", ".json"} and not {"bin", "obj", "out"}.intersection(path.relative_to(repo).parts):
                files.append(dict(path=path.relative_to(repo).as_posix(), sha256=sha(path)))
    for name in ["Directory.Build.props", "Directory.Build.targets", "RizToolbox.sln", "dependencies.lock.json", "global.json"]:
        if (repo / name).is_file():
            files.append(dict(path=name, sha256=sha(repo / name)))
    write("source-manifest.json", dict(files=sorted(files, key=lambda f: f["path"])))
    shutil.copy2(repo / "artifacts/package-manifest.json", root / "package-manifest.json")
    (root / "native-build").mkdir(exist_ok=True)
    for name in ["native-manifest.json", "build.log", "exports.txt", "unwindinfo.txt", "fixture-result.json", "publication-fixture-result.json"]:
        source = args.native_output / name
        assert source.is_file(), source
        shutil.copy2(source, root / "native-build" / name)
    print(json.dumps(dict(projects=len(suites), passed=total, total=total, sourceFiles=len(files), nativeFixtureChecks=[f["checks"] for f in fixtures])))
else:
    package = json.loads((root / "package-manifest.json").read_text(encoding="utf-8-sig"))
    assert len(package["files"]) == 18
    package_location = repo / "artifacts/package" if args.staged else game
    checks = [dict(path=f["path"], expected=f["sha256"], actual=sha(package_location / f["path"])) for f in package["files"]]
    assert all(f["expected"] == f["actual"] for f in checks)
    source = json.loads((root / "source-manifest.json").read_text(encoding="utf-8"))
    changed = [f["path"] for f in source["files"] if sha(repo / f["path"]) != f["sha256"]]
    assert not changed, changed
    write("staged-package-check.json" if args.staged else "installation-check.json",
          dict(checkedAtUtc=datetime.now(timezone.utc).isoformat(), location=str(package_location), files=checks, allMatched=True, stagedOnly=args.staged))
    write("source-integrity-check.json", dict(checked=len(source["files"]), changed=changed))
    before = json.loads((root / "original-game-before.json").read_text(encoding="utf-8-sig"))
    assert len(before["files"]) == 3
    originals = [dict(path=f["path"], expected=f["sha256"], actual=sha(game / f["path"])) for f in before["files"]]
    assert all(f["expected"] == f["actual"] for f in originals)
    write("original-game-after.json", dict(checkedAtUtc=datetime.now(timezone.utc).isoformat(), files=originals, allUnchanged=True))
    if args.staged:
        installed = json.loads((root / "installed-999-package-manifest.json").read_text(encoding="utf-8-sig"))
        installed_checks = [dict(path=f["path"], expected=f["sha256"], actual=sha(game / f["path"])) for f in installed["files"]]
        assert len(installed_checks) == 18 and all(f["expected"] == f["actual"] for f in installed_checks)
        write("installed-999-check.json", dict(checkedAtUtc=datetime.now(timezone.utc).isoformat(), installedPackageChecks=999,
              files=installed_checks, allMatched=True, newPackageInstalled=False))
    print(json.dumps(dict(stagedMatched=len(checks) if args.staged else None,
                         installedMatched=18 if args.staged else len(checks),
                         sourceFilesChecked=len(source["files"]), changed=changed, originalGameFilesUnchanged=3,
                         newPackageInstalled=not args.staged)))



