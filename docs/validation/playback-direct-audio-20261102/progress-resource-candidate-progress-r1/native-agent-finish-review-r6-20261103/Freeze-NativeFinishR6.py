"""Freeze and verify this agent's native r6 / read-only bridge evidence.

The default mode writes a fresh local package. --finalize hashes the package;
--verify reads only the archived package and checks the final hashes.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import difflib
import hashlib
import json
from pathlib import Path
import shutil
import sys

BASE = Path(__file__).resolve().parent
U = BASE.parent
SOURCE = U / "source"
HELPER_SHA = "ef24415492cd31333a692ef689f2dac5c34c7ca7a58a6b771316ceb0dc5d353c"
OLD_RUN = "native-agent-retry-short-r6"
RESP_RUN = "native-agent-responsibility-short-r6"
NEW_RUN = "native-agent-responsibility-host-r6-fixture-r2-resume-20261103"
BUILD = "native-agent-build-r6"
FIX_BUILD = "native-agent-build-r6-fixture-r2"
HELPER_NAMES = {
    "PlaybackLogicalHandoff.h", "PlaybackInitialization.h", "PlaybackSourceContext.cpp",
    "PlaybackSourceContext.h", "PlaybackPlayerWatch.inc", "PlaybackProgressWatch.inc",
    "PlaybackWriterCall.asm",
}
RUNTIME = [
    "src/RizToolbox.Runtime/Hooks/NativePlaybackStockSourceHub.cs",
    "src/RizToolbox.Runtime/Hooks/NativePlaybackStockSourceHub.ProgressResource.cs",
    "src/RizToolbox.Runtime/Hooks/NativePlaybackStockSourceBridge.cs",
    "src/RizToolbox.Runtime/Hooks/NativePlaybackStockSourceTypes.cs",
]
def fact(path: Path) -> dict:
    raw = path.read_bytes()
    return {"path": str(path), "sha256": hashlib.sha256(raw).hexdigest(), "bytes": len(raw)}
def read(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8-sig"))
def save(name: str, data) -> None:
    (BASE / name).write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
def require(condition, message):
    if not condition:
        raise RuntimeError(message)
def validate_fact(row: dict):
    actual = fact(Path(row["path"]))
    require(actual["sha256"] == row["sha256"], "Hash changed: " + row["path"])
    require(actual["bytes"] == row["bytes"], "Size changed: " + row["path"])
def copy(path: Path):
    relative = path.relative_to(U)
    target = BASE / "artifacts" / relative
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(path, target)
    require(fact(path)["sha256"] == fact(target)["sha256"], "Copy changed: " + str(path))
    return target.relative_to(BASE).as_posix()
def build_audit(name: str):
    directory = U / name
    data = read(directory / "native-build-result.json")
    require(data["passed"] is True and data["actualExitCode"] == 0, "Build did not pass: " + name)
    require((directory / "build.exit.txt").read_text().strip() == "0", "Build actual exit absent: " + name)
    require(data["sourceVersion"] == 4 and data["frameBytes"] == 168 and data["resourceBytes"] == 112, "Wire differs")
    for row in data["sources"] + data["files"]:
        validate_fact(row)
        copy(Path(row["path"]))
    copy(directory / "native-build-result.json")
    return data
def case_audit(name: str):
    directory = U / name
    data = read(directory / "native-retry-result.json")
    for key in ["helper", "fixture", "fixtureSource", "runnerSource"]:
        validate_fact(data[key])
    require(data["helper"]["sha256"] == HELPER_SHA, "Unexpected tested helper")
    require(all(data[k] for k in ["helperUnchanged", "fixtureUnchanged", "sourceInputsUnchanged"]), "Run change guard failed")
    require(all(data[k] is False for k in ["gameExecuted", "installed", "productionAdmission", "singleReturnReceiptEpochAcceptance"]), "Scope differs")
    runner = Path(data["runnerSource"]["path"])
    for row in data["cases"]:
        for key in ["stdout", "stderr"]:
            validate_fact(row[key])
        actual_exit = int((directory / (row["mode"] + ".exit.txt")).read_text())
        require(actual_exit == row["actualExitCode"], "Actual exit mismatch")
        lines = Path(row["stdout"]["path"]).read_text(encoding="utf-8").splitlines()
        actual_summary = next(json.loads(line) for line in reversed(lines) if line.startswith("{"))
        require(actual_summary == row["summary"], "Stored summary differs from stdout")
        require(row["summary"]["sourceWireBytes"] == 168 and row["summary"]["resourceWireBytes"] == 112, "Run wire differs")
        basic_pass = actual_exit == 0 and actual_summary["passed"] is True and actual_summary["failures"] == 0
        require(bool(row["passed"]) == basic_pass, "Run pass facts differ")
    for path in sorted(directory.iterdir()):
        if path.is_file():
            copy(path)
    return data
def finalize():
    rows = []
    for path in sorted(BASE.rglob("*")):
        if path.is_file() and path != BASE / "hash-manifest.json":
            row = fact(path)
            row["path"] = path.relative_to(BASE).as_posix()
            rows.append(row)
    save("hash-manifest.json", {"kind": "native-r6-finish-package-sha256", "files": rows})
    print(json.dumps({"finalized": True, "files": len(rows), "manifest": fact(BASE / "hash-manifest.json")}))
def verify():
    data = read(BASE / "hash-manifest.json")
    issues = []
    expected = {row["path"] for row in data["files"]}
    actual = {p.relative_to(BASE).as_posix() for p in BASE.rglob("*") if p.is_file() and p != BASE / "hash-manifest.json"}
    issues += sorted(expected ^ actual)
    for row in data["files"]:
        path = BASE / row["path"]
        if not path.is_file():
            continue
        observed = fact(path)
        if observed["sha256"] != row["sha256"] or observed["bytes"] != row["bytes"]:
            issues.append(row["path"])
    print(json.dumps({"passed": not issues, "verifiedFiles": len(data["files"]), "issues": issues}))
    return 0 if not issues else 1
def freeze():
    require(not (BASE / "artifacts").exists(), "Use a new package for a new freeze")
    original = build_audit(BUILD)
    revised = build_audit(FIX_BUILD)
    old = case_audit(OLD_RUN)
    responsibility = case_audit(RESP_RUN)
    host = case_audit(NEW_RUN)
    require(len(old["cases"]) == 46 and old["passed"] is True, "Original 46 not complete")
    require(sum(r["summary"]["checks"] for r in old["cases"]) == 7969, "Original check count differs")
    require(len(responsibility["cases"]) == 10 and sum(r["passed"] for r in responsibility["cases"]) == 9, "Original marker scope differs")
    failed = [r for r in responsibility["cases"] if not r["passed"]]
    require(len(failed) == 1 and failed[0]["mode"] == "host-responsibility-closed", "Unexpected original failure")
    require(failed[0]["actualExitCode"] == 1 and failed[0]["summary"]["failures"] == 2, "Original failure not preserved")
    require(host["passed"] is True and len(host["cases"]) == 1 and host["cases"][0]["mode"] == failed[0]["mode"], "Host replacement scope differs")
    require(host["cases"][0]["summary"]["checks"] == 217, "Host checks differ")
    old_sources = {Path(r["path"]).name: r for r in original["sources"]}
    new_sources = {Path(r["path"]).name: r for r in revised["sources"]}
    source_rows = []
    for name in sorted(HELPER_NAMES):
        old_path = Path(old_sources[name]["path"])
        relative = old_path.relative_to(U / BUILD / "frozen-source")
        live = SOURCE / relative
        row = {
            "relativePath": relative.as_posix(), "live": fact(live),
            "r6Frozen": old_sources[name], "fixtureR2Frozen": new_sources[name],
            "matchesBothBuilds": fact(live)["sha256"] == old_sources[name]["sha256"] == new_sources[name]["sha256"],
        }
        require(row["matchesBothBuilds"], "Helper input changed: " + name)
        functional = U / "functional-matrix-r1/frozen-source" / relative
        row["functionalFrozen"] = fact(functional) if functional.is_file() else None
        row["matchesFunctional"] = row["functionalFrozen"] is None or row["functionalFrozen"]["sha256"] == row["live"]["sha256"]
        require(row["matchesFunctional"], "Functional frozen input changed: " + name)
        if functional.is_file():
            copy(functional)
        copy(live)
        source_rows.append(row)
    fixture_rel = "native/RizToolbox.PlaybackSourcePrototype/ProgressResourceNativeFixture.cpp"
    live_fixture = fact(SOURCE / fixture_rel)
    require(live_fixture["sha256"] == new_sources["ProgressResourceNativeFixture.cpp"]["sha256"], "Live fixture not the completed r2 fixture")
    copy(SOURCE / fixture_rel)
    copy(SOURCE / "native/RizToolbox.PlaybackSourcePrototype/Test-ProgressResourceNativeRetry.py")
    before = Path(old_sources["ProgressResourceNativeFixture.cpp"]["path"]).read_text()
    after = Path(new_sources["ProgressResourceNativeFixture.cpp"]["path"]).read_text()
    expected = before.replace(
        "        CHECK(frameCount == 4 && responsibilityBefore == 0); CheckNoJournalFrames();",
        "        // A host watch has no player/audio roots before the actual Start;\n"
        "        // those two originals execute transparently with no source frame.\n"
        "        const unsigned ordinaryFrames = hostReceipt ? 2u : 4u;\n"
        "        CHECK(pitches == 1 && setClips == 1);\n"
        "        CHECK(frameCount == ordinaryFrames && responsibilityBefore == 0); CheckNoJournalFrames();",
    ).replace(
        "        CHECK(responsibilityReceipts == 3 && responsibilityNoJournal == 5);",
        "        CHECK(responsibilityReceipts == 3 && responsibilityNoJournal == ordinaryFrames + 1);",
    )
    require(after == expected, "Fixture diff exceeds the allowed counting correction")
    (BASE / "fixture-only-correction.patch").write_text("".join(difflib.unified_diff(
        before.splitlines(True), after.splitlines(True), fromfile="r6/ProgressResourceNativeFixture.cpp",
        tofile="fixture-r2/ProgressResourceNativeFixture.cpp")), encoding="utf-8")
    runner_before = Path(responsibility["runnerSource"]["path"]).read_text()
    runner_after = (SOURCE / "native/RizToolbox.PlaybackSourcePrototype/Test-ProgressResourceNativeRetry.py").read_text()
    require(runner_after == runner_before.replace(
        '"closed": (3, 3, 5, 0, 0),',
        '"closed": (3, 3, 3 if mode.startswith("host-") else 5, 0, 0),'), "Runner diff exceeds counting correction")
    (BASE / "runner-only-correction.patch").write_text("".join(difflib.unified_diff(
        runner_before.splitlines(True), runner_after.splitlines(True), fromfile="r6/Test-ProgressResourceNativeRetry.py",
        tofile="fixture-r2/Test-ProgressResourceNativeRetry.py")), encoding="utf-8")
    runtime_rows = []
    for rel in RUNTIME:
        original_path = SOURCE / rel
        archive = copy(original_path)
        runtime_rows.append({"relativePath": rel, "live": fact(original_path), "snapshot": archive, "readOnlyReview": True})
    refs = ["IMPLEMENTATION-CONTRACT.md", "retry-review-r1/DORMANT-QUALIFICATION-FINDING.md",
            "retry-review-r1/RESOURCE-RETRY-REVIEW.md",
            "native-agent-final-review-r1/2026-11-03_native-progress-resource-r5-report.md",
            "native-agent-final-review-r1/hash-manifest.json"]
    for rel in refs:
        copy(U / rel)
    selected = []
    for row in responsibility["cases"]:
        chosen = host["cases"][0] if row["mode"] == "host-responsibility-closed" else row
        selected.append({
            "mode": chosen["mode"], "selectedRun": NEW_RUN if row["mode"] == "host-responsibility-closed" else RESP_RUN,
            "passed": chosen["passed"], "actualExitCode": chosen["actualExitCode"], "checks": chosen["summary"]["checks"],
            "summary": chosen["summary"],
            "fixtureSha256": host["fixture"]["sha256"] if row["mode"] == "host-responsibility-closed" else responsibility["fixture"]["sha256"],
            "helperSha256": HELPER_SHA,
        })
    require(all(r["passed"] for r in selected), "Selected marker coverage is incomplete")
    audit = {
        "kind": "native-r6-resume-and-bridge-scope-audit", "passed": True,
        "observedUtc": datetime.now(timezone.utc).isoformat(), "helper": fact(U / BUILD / "RizToolbox.PlaybackSourcePrototype.dll"),
        "old46": {"run": OLD_RUN, "cases": 46, "checks": 7969, "passed": True, "repeated": False},
        "originalMarkerRun": {"run": RESP_RUN, "passed": False, "cases": 10, "passedCases": 9, "failuresRetained": 2},
        "hostResume": {"run": NEW_RUN, "passed": True, "cases": 1, "checks": 217, "actualExitCode": 0},
        "selectedMarkerCoverage": {"cases": 10, "checks": sum(r["checks"] for r in selected), "passed": True, "allTenRerunOnFixtureR2": False, "casesDetail": selected},
        "combinedSelectedCoverage": {"cases": 56, "checks": 7969 + sum(r["checks"] for r in selected), "failures": 0},
        "r6BuildCompleteBeforeShutdown": True, "fixtureR2BuildCompleteBeforeShutdown": True,
        "fixtureR2BuildActualExitCode": revised["actualExitCode"], "newBuildPerformedThisTurn": False,
        "productSourcesModifiedThisTurn": [], "testSourcesModifiedThisTurn": [], "helperInputs": source_rows,
        "fixtureLive": live_fixture, "runtimeReadOnlySnapshots": runtime_rows,
        "scope": {"nativeOwnedFixture": True, "bridgeStaticReview": True, "managedClockAcceptance": False,
                  "singleReturnOpaqueReceiptEpochAcceptance": False, "productionAdmission": False,
                  "gameExecuted": False, "gameUiAccountAccessed": False, "existingProcessesKilled": False,
                  "rootRuntimeModified": False},
        "shutdownContext": {
            "source": "Parent task instruction",
            "reportedShutdownLocal": "2026-11-03 00:24 Asia/Shanghai",
            "reportedRestartLocal": "2026-11-03 07:33 Asia/Shanghai",
            "nativeEvidenceInterpretation": "Power loss interrupted the wider work handoff/aggregation; persisted fixture-r2 build exit and manifest were already complete. No native build/test is relabelled interrupted.",
        },
    }
    save("source-audit.json", audit)
    print(json.dumps({"passed": True, "old46": audit["old46"], "selectedMarkerCoverage": {"cases": 10, "checks": audit["selectedMarkerCoverage"]["checks"]},
                      "combinedSelectedCoverage": audit["combinedSelectedCoverage"], "snapshotCount": len(runtime_rows), "sourceModified": False}))
def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--finalize", action="store_true")
    parser.add_argument("--verify", action="store_true")
    args = parser.parse_args()
    if args.verify:
        return verify()
    if args.finalize:
        finalize()
    else:
        freeze()
    return 0
if __name__ == "__main__":
    sys.exit(main())
