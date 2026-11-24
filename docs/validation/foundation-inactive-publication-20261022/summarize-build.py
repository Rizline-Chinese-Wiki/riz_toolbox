"""Record this completed staged build; optionally check it and the separate installed 1021 batch.

No installation, process stop, game launch or UI action. The installed source is verified
from its frozen archive, because the working tree intentionally contains newer changes.
"""
import argparse
import hashlib
import json
import re
import shutil
import zipfile
from datetime import datetime, timezone
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--staged", action="store_true")
parser.add_argument("--native-output", type=Path, default=Path(r"D:\RizToolbox-build-cache\inactive-publication-20261022"))
args = parser.parse_args()
root = Path(__file__).resolve().parent
repo = root.parents[2]
installed_batch = root.parent / "foundation-local-protection-host-20261022"
game = Path(r"D:\SteamLibrary\steamapps\common\Rizline")


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def write(name, data):
    (root / name).write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if not args.staged:
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
        nativeAcceptance="Packaged helper and Dobby fixtures only; this inactive publication change is staged, not installed or tested in the game.",
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
    staged = read(root / "package-manifest.json")
    assert len(staged["files"]) == 18
    checks = [dict(path=f["path"], expected=f["sha256"], actual=sha(repo / "artifacts/package" / f["path"])) for f in staged["files"]]
    assert all(f["expected"] == f["actual"] for f in checks)
    sources = read(root / "source-manifest.json")["files"]
    assert all(sha(repo / f["path"]) == f["sha256"] for f in sources)
    write("staging-check.json", dict(checkedAtUtc=datetime.now(timezone.utc).isoformat(), files=checks, allMatched=True, installed=False))
    write("source-integrity-check.json", dict(checked=len(sources), changed=[]))

    previous = read(installed_batch / "package-manifest.json")
    assert len(previous["files"]) == 18
    installed = [dict(path=f["path"], expected=f["sha256"], actual=sha(game / f["path"])) for f in previous["files"]]
    assert all(f["expected"] == f["actual"] for f in installed)
    archive_proof = read(installed_batch / "source-archive-check.json")
    assert sha(installed_batch / "source-inputs.zip") == archive_proof["archiveSha256"]
    assert sha(installed_batch / "source-manifest.json") == archive_proof["manifestSha256"]
    old_sources = read(installed_batch / "source-manifest.json")["files"]
    with zipfile.ZipFile(installed_batch / "source-inputs.zip") as archive:
        assert archive.testzip() is None and set(archive.namelist()) == {f["path"] for f in old_sources}
        assert all(hashlib.sha256(archive.read(f["path"])).hexdigest() == f["sha256"] for f in old_sources)
    write("previous-installation-check.json", dict(checkedAtUtc=datetime.now(timezone.utc).isoformat(),
        batch=installed_batch.name, files=installed, allMatched=True, archivedSourceInputsChecked=len(old_sources),
        scope="Installed 1021 batch and its archived inputs only; working-tree source is the newer staged build."))
    originals = read(installed_batch / "original-game-before.json")["files"]
    assert len(originals) == 3
    original_checks = [dict(path=f["path"], expected=f["sha256"], actual=sha(game / f["path"])) for f in originals]
    assert all(f["expected"] == f["actual"] for f in original_checks)
    write("original-game-check.json", dict(checkedAtUtc=datetime.now(timezone.utc).isoformat(), files=original_checks, allUnchanged=True))
    print(json.dumps(dict(stagedMatched=18, currentSourceInputs=len(sources), installedPreviousBatchMatched=18,
        archivedPreviousInputs=len(old_sources), originalGameFilesUnchanged=3, installedNewBatch=False)))
