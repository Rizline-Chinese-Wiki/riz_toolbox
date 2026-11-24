"""Freeze the completed build, source inputs and actual installed file hashes.
Run from the repository after Build.ps1, and again with --installed after Install.ps1.
This script never launches or controls the game.
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
args = parser.parse_args()
root = Path(__file__).resolve().parent
repo = root.parents[2]
game = Path(r"D:\SteamLibrary\steamapps\common\Rizline")
native = Path(r"D:\RizToolbox-build-cache\batch-gamepad-20261021")
def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def write(name, data): (root/name).write_text(json.dumps(data, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
if not args.installed:
    log = root/"build.log"
    body = log.read_text(encoding="utf-8-sig")
    suites = []
    for line in body.splitlines():
        match = re.fullmatch(r".*?(\d+)/(\d+).*passed\.", line.strip())
        if match: suites.append(dict(summary=line.strip(), passed=int(match[1]), total=int(match[2])))
    assert len(suites) == 25 and all(s["passed"] == s["total"] for s in suites)
    assert sum(s["total"] for s in suites) == 744
    assert "Package: " in body and "FAIL " not in body and not re.search(r"error (?:CS|MSB|NU)\d+", body)
    fixtures = [json.loads(line) for line in body.splitlines() if line.startswith('{"schemaVersion":1,"kind":"real-windows-x64-native-fixture"')]
    assert len(fixtures) == 2 and all(f["passed"] and f["inFlight"] == 0 for f in fixtures)
    assert fixtures[1]["realDobbyPreparedCommit"]
    write("build-summary.json", dict(completedAtUtc=datetime.now(timezone.utc).isoformat(),
        projects=len(suites), passed=744, total=744, suites=suites, nativeFixtures=fixtures,
        logSha256=sha(log), nativeAcceptance="Packaged native helper and real Dobby fixture passed; game acceptance requires the separate runtime evidence.",
        compilerDiagnosticLines=sorted(set(line.strip() for line in body.splitlines() if re.search(r"\b(?:warning|error) (?:CS|MSB|NU)\d+",line,re.I)))))
    files = []
    for directory in ["src", "tests", "examples", "scripts", "native"]:
        for path in (repo/directory).rglob("*"):
            if path.is_file() and path.suffix in {".cs",".csproj",".props",".targets",".ps1",".cpp",".h",".asm",".json"} and not {"bin","obj","out"}.intersection(path.relative_to(repo).parts):
                files.append(dict(path=path.relative_to(repo).as_posix(),sha256=sha(path)))
    for name in ["Directory.Build.props","Directory.Build.targets","RizToolbox.sln","dependencies.lock.json"]:
        if (repo/name).is_file(): files.append(dict(path=name,sha256=sha(repo/name)))
    write("source-manifest.json",dict(files=sorted(files,key=lambda x:x["path"])))
    shutil.copy2(repo/"artifacts/package-manifest.json",root/"package-manifest.json")
    (root/"native-build").mkdir(exist_ok=True)
    for name in ["native-manifest.json","build.log","exports.txt","unwindinfo.txt","fixture-result.json","publication-fixture-result.json"]:
        if (native/name).is_file(): shutil.copy2(native/name,root/"native-build"/name)
    print(json.dumps(dict(projects=25,passed=744,total=744,sourceFiles=len(files),nativeFixtureChecks=[f["checks"] for f in fixtures])))
else:
    package = json.loads((root/"package-manifest.json").read_text(encoding="utf-8-sig"))
    assert len(package["files"]) == 18
    checks = [dict(path=f["path"],expected=f["sha256"],actual=sha(game/f["path"])) for f in package["files"]]
    assert all(x["expected"] == x["actual"] for x in checks)
    source = json.loads((root/"source-manifest.json").read_text(encoding="utf-8"))
    changed = [x["path"] for x in source["files"] if sha(repo/x["path"]) != x["sha256"]]
    assert not changed, changed
    write("installation-check.json",dict(checkedAtUtc=datetime.now(timezone.utc).isoformat(),game=str(game),files=checks,allMatched=True))
    write("source-integrity-check.json",dict(checked=len(source["files"]),changed=changed))
    print(json.dumps(dict(installedMatched=len(checks),sourceFilesChecked=len(source["files"]),changed=changed)))

