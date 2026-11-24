"""Freeze this native subpackage's existing evidence without changing D: originals."""
import hashlib
import json
import shutil
from pathlib import Path

CASE = Path(__file__).resolve().parent
REPO = CASE.parents[2]
CACHE = Path("D:/riz-build-cache-next")
BUILD = CACHE / "playback-stock-native-build-r6-20261031"
CHECKS = CACHE / "playback-stock-native-checks-r6-20261031"
OWNED = [
    "native/RizToolbox.PlaybackSourcePrototype/PlaybackSourceContext.h",
    "native/RizToolbox.PlaybackSourcePrototype/PlaybackSourceContext.cpp",
    "native/RizToolbox.PlaybackSourcePrototype/PlaybackSourceFixture.cpp",
    "native/RizToolbox.PlaybackStockFixture/PlaybackStockFixture.h",
    "native/RizToolbox.PlaybackStockFixture/PlaybackStockFixture.cpp",
    "native/RizToolbox.PlaybackStockFixture/PlaybackStockFixture.asm",
    "native/RizToolbox.PlaybackStockFixture/PlaybackSourceCaller.asm",
    "scripts/Build-PlaybackSourcePrototype.ps1",
    "scripts/Test-PlaybackSourcePrototype.ps1",
    "scripts/Verify-PlaybackSourcePrototype.py",
]


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")


def row(path, role):
    return {"path": str(path.resolve()), "bytes": path.stat().st_size, "sha256": sha(path), "role": role}


def copy(source, destination):
    destination.parent.mkdir(parents=True, exist_ok=True)
    assert not destination.exists(), destination
    shutil.copyfile(source, destination)
    assert sha(source) == sha(destination)


def main():
    assert not (CASE / "sources-r6").exists()
    assert not (CASE / "evidence").exists()
    for name in ["README.md", "scope.md", "timeline.md", "2026-10-31_Playback-stock-native-report.md"]:
        copy(CASE / name, CASE / "history/r4-reported" / name)
    owned = []
    for name in OWNED:
        source, frozen = REPO / name, CASE / "sources-r6" / name
        copy(source, frozen)
        owned.append({"path": str(source), "snapshot": str(frozen.relative_to(CASE)), "sha256": sha(source)})
    build = json.loads((BUILD / "build-result.json").read_text(encoding="utf-8-sig"))
    checks = json.loads((CHECKS / "test-result.json").read_text(encoding="utf-8-sig"))
    proof = json.loads((BUILD / "native-symbol-source-proof.json").read_text(encoding="utf-8"))
    assert build["passed"] and build["actualExitCode"] == 0
    assert checks["passed"] and checks["processes"] == 13 and checks["checks"] == 27037
    for directory, destination in [(BUILD, CASE / "evidence/r6/build"), (CHECKS, CASE / "evidence/r6/checks"),
            (CACHE / "playback-stock-native-build-r5-20261031", CASE / "evidence/r5-failed/build"),
            (CACHE / "playback-stock-native-checks-r5-20261031", CASE / "evidence/r5-failed/checks"),
            (CACHE / "playback-stock-native-build-r4-20261031", CASE / "evidence/r4-history/build"),
            (CACHE / "playback-stock-native-checks-r3-20261031", CASE / "evidence/r4-history/checks")]:
        for source in sorted(directory.iterdir()):
            if source.is_file() and source.suffix in {".json", ".txt", ".log", ".cmd"}:
                copy(source, destination / source.name)
    write(CASE / "native-owned-inputs.json", {"kind": "native-source-task-owned-inputs", "revision": "r6",
        "files": owned, "build": str(BUILD / "build-result.json"), "checks": str(CHECKS / "test-result.json"),
        "symbols": str(BUILD / "native-symbol-source-proof.json"), "actualProcesses": 13, "checksCount": 27037,
        "gameCodeExecuted": False, "productionAdmission": False, "formalCurrentProvider": False})
    paths = {}
    directories = []

    def add(path, role):
        path = path.resolve()
        paths.setdefault(str(path).casefold(), row(path, role))

    for pattern in ["playback-stock-native-build-r*-20261031", "playback-stock-native-checks-r*-20261031"]:
        for directory in sorted(CACHE.glob(pattern)):
            files = sorted(path for path in directory.rglob("*") if path.is_file())
            directories.append({"path": str(directory), "files": [str(path.resolve()) for path in files]})
            for path in files:
                add(path, "retained-native-input-or-output")
    for item in build["sources"] + checks["inputs"] + proof["verificationInputs"]:
        add(Path(item["path"]), "current-external-input")
    for obj in proof["compiledObjects"]:
        for source in obj["sources"]:
            add(Path(source["path"]), "actual-C13-source-or-header")
    for name in OWNED:
        add(REPO / name, "current-owned-source")
    for root in [CASE / "sources-r6", CASE / "evidence", CASE / "history"]:
        for path in sorted(root.rglob("*")):
            if path.is_file():
                add(path, "report-local-snapshot")
    for name in ["native-owned-inputs.json", "Prepare-Evidence.py", "Verify-Report.py"]:
        add(CASE / name, "report-input-or-tool")
    write(CASE / "artifact-index.json", {"kind": "complete-native-artifact-fixity", "revision": "r6",
        "selfHashExcluded": True, "nativeDirectories": directories, "files": sorted(paths.values(), key=lambda x: x["path"]),
        "exclusions": ["artifact-index.json (this index)", "later report/index/integrity files (sealed separately)"]})
    print(json.dumps({"passed": True, "ownedSources": len(owned), "nativeDirectories": len(directories),
                      "hashedArtifacts": len(paths), "DOriginalsModified": False}))


if __name__ == "__main__":
    main()
