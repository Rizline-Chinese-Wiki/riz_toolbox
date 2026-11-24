"""Run owned native retry cases in a fresh evidence directory.

The callback is an owned Source gate, not the GetTime receipt issuer. These
checks claim no managed Clock publication, production roots or game execution.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys


LEGACY_MODES = (
    "revision", "host-revision", "foreign", "host-foreign", "host-start",
    "host-start-interrupt", "host-start-late", "worker", "seh", "class",
)
RETRY_MODES = (
    "host-phase6-retry", "phase6-retry", "host-phase6-handoff", "phase6-handoff",
    "host-phase6-same", "host-phase6-aba", "host-phase6-pending-retry",
    "phase6-pending-retry", "host-phase6-pending-handoff", "phase6-pending-handoff",
    "host-phase6-pending-same", "host-phase6-pending-aba",
)
UNKNOWN_MODES = tuple(
    prefix + "pending-unknown-" + fault
    for prefix in ("host-", "")
    for fault in ("worker", "seh", "class")
)
WITHDRAW_MODES = tuple(
    prefix + "withdraw-" + phase
    for prefix in ("host-", "")
    for phase in ("future", "yield", "candidate", "phase6", "consumed", "inflight")
)
WITHDRAW_UNKNOWN_MODES = tuple(
    prefix + "withdraw-pending-unknown-" + fault
    for prefix in ("host-", "")
    for fault in ("worker", "seh", "class")
)
RESPONSIBILITY_MODES = tuple(
    prefix + "responsibility-" + kind
    for prefix in ("host-", "")
    for kind in ("closed", "worker", "seh", "class", "saturation")
)


def fact(path: Path) -> dict:
    data = path.read_bytes()
    return {"path": str(path), "sha256": hashlib.sha256(data).hexdigest(), "bytes": len(data)}


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--build", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--helper", type=Path)
    parser.add_argument("--modes", nargs="+", choices=LEGACY_MODES + RETRY_MODES + UNKNOWN_MODES + WITHDRAW_MODES + WITHDRAW_UNKNOWN_MODES + RESPONSIBILITY_MODES)
    args = parser.parse_args()
    source_root = Path(__file__).resolve().parents[2]
    task_root = source_root.parent
    build = args.build.resolve()
    output = args.output.resolve()
    helper = (args.helper or build / "RizToolbox.PlaybackSourcePrototype.dll").resolve()
    if output.exists() or not output.is_relative_to(task_root):
        raise RuntimeError("Use a fresh evidence directory inside the isolated task root.")
    output.mkdir(parents=True)
    shutil.copy2(__file__, output / Path(__file__).name)
    baseline = task_root / "native-agent-build-r3"
    baseline_manifest = json.loads((baseline / "native-build-result.json").read_text(encoding="utf-8-sig"))
    provenance = []
    helper_input_names = {
        "PlaybackLogicalHandoff.h", "PlaybackInitialization.h", "PlaybackSourceContext.cpp",
        "PlaybackSourceContext.h", "PlaybackPlayerWatch.inc", "PlaybackProgressWatch.inc", "PlaybackWriterCall.asm",
    }
    for item in baseline_manifest["sources"]:
        frozen = Path(item["path"])
        if frozen.name not in helper_input_names:
            continue
        relative = frozen.relative_to(baseline / "frozen-source")
        live = fact(source_root / relative)
        current_frozen = fact(build / "frozen-source" / relative)
        baseline_frozen = fact(frozen)
        provenance.append({
            "relativePath": str(relative), "live": live, "buildFrozen": current_frozen,
            "r3Frozen": baseline_frozen,
            "matchesR3": live["sha256"] == current_frozen["sha256"] == baseline_frozen["sha256"] == item["sha256"],
            "matchesBuild": live["sha256"] == current_frozen["sha256"],
        })
    if len(provenance) != len(helper_input_names) or not all(row["matchesBuild"] for row in provenance):
        raise RuntimeError("Current/frozen native helper inputs differ.")
    tested_manifest = json.loads((helper.parent / "native-build-result.json").read_text(encoding="utf-8-sig"))
    tested_helper_record = next(row for row in tested_manifest["files"] if Path(row["path"]).name == helper.name)
    if fact(helper)["sha256"] != tested_helper_record["sha256"]:
        raise RuntimeError("Tested native helper differs from its build manifest.")
    tested_sources = [fact(Path(row["path"])) for row in tested_manifest["sources"] if Path(row["path"]).name in helper_input_names]
    if any(actual["sha256"] != expected["sha256"] for actual, expected in zip(
        tested_sources, (row for row in tested_manifest["sources"] if Path(row["path"]).name in helper_input_names), strict=True
    )):
        raise RuntimeError("Tested native helper frozen sources differ from their build manifest.")
    fixture_source = fact(build / "frozen-source/native/RizToolbox.PlaybackSourcePrototype/ProgressResourceNativeFixture.cpp")
    live_fixture = fact(source_root / "native/RizToolbox.PlaybackSourcePrototype/ProgressResourceNativeFixture.cpp")
    if fixture_source["sha256"] != live_fixture["sha256"]:
        raise RuntimeError("Current/frozen native fixture source differs.")
    result = {
        "kind": "owned-source-native-resource-retry-matrix", "passed": False,
        "startedUtc": datetime.now(timezone.utc).isoformat(),
        "helperSourceEquivalentToR3": all(row["matchesR3"] for row in provenance),
        "helperInputChangesFromR3": [row["relativePath"] for row in provenance if not row["matchesR3"]],
        "helper": fact(helper), "fixture": fact(build / "ProgressResourceNativeFixture.exe"),
        "fixtureSource": fixture_source, "runnerSource": fact(output / Path(__file__).name),
        "helperInputs": provenance, "testedHelperFrozenSources": tested_sources,
        "gameExecuted": False, "installed": False, "productionAdmission": False,
        "singleReturnReceiptEpochAcceptance": False, "cases": [],
    }
    try:
        for mode in args.modes or LEGACY_MODES + RETRY_MODES + UNKNOWN_MODES + WITHDRAW_MODES + WITHDRAW_UNKNOWN_MODES:
            stdout_path = output / (mode + ".stdout.log")
            stderr_path = output / (mode + ".stderr.log")
            with stdout_path.open("wb") as stdout, stderr_path.open("wb") as stderr:
                process = subprocess.run(
                    [str(build / "ProgressResourceNativeFixture.exe"), str(helper), mode],
                    stdout=stdout, stderr=stderr, check=False,
                )
            (output / (mode + ".exit.txt")).write_text(str(process.returncode), encoding="utf-8")
            lines = stdout_path.read_text(encoding="utf-8", errors="replace").splitlines()
            summary = next((json.loads(line) for line in reversed(lines) if line.startswith("{")), None)
            passed = process.returncode == 0 and summary is not None and summary.get("passed") is True
            if mode in RETRY_MODES and summary is not None:
                passed &= (
                    summary["retryAttempts"] == 2 and summary["retryAccepted"] == 1
                    and summary["staleRejections"] >= 16 and summary["inFlightRetryRejections"] == 2
                    and summary["priorOrdinal"] > 0 and summary["freshOrdinal"] > summary["priorOrdinal"]
                    and summary["actualSetClip"] == 2
                    and summary["pendingReplacements"] == (1 if "pending" in mode else 0)
                )
            if mode in UNKNOWN_MODES + WITHDRAW_UNKNOWN_MODES and summary is not None:
                passed &= summary["unknownRootRefusals"] == 6 and summary["retryAccepted"] == 0
            if mode in WITHDRAW_MODES and summary is not None:
                passed &= summary["withdrawRetired"] == 2 and summary["withdrawInFlightRejections"] == (1 if "inflight" in mode else 0)
            if mode in RESPONSIBILITY_MODES and summary is not None:
                expected = {
                    "closed": (3, 3, 5, 0, 0),
                    "worker": (0, 0, 0, 1, 0),
                    "seh": (1, 1, 0, 0, 0),
                    "class": (0, 0, 0, 0, 0),
                    "saturation": (256, 256, 1, 0, 1),
                }[mode.rsplit("-", 1)[1]]
                actual = tuple(summary[name] for name in (
                    "responsibilityBefore", "responsibilityReceipts", "responsibilityNoJournal",
                    "responsibilityWorkerReceipts", "responsibilityAllocationFailures",
                ))
                passed &= actual == expected and summary["sourceWireBytes"] == 168
            row = {"mode": mode, "passed": bool(passed), "actualExitCode": process.returncode,
                   "stdout": fact(stdout_path), "stderr": fact(stderr_path), "summary": summary}
            result["cases"].append(row)
            print(json.dumps({"mode": mode, "actualExitCode": process.returncode, "passed": bool(passed)}), flush=True)
        result["passed"] = all(row["passed"] for row in result["cases"])
        result["helperUnchanged"] = fact(helper) == result["helper"]
        result["fixtureUnchanged"] = fact(build / "ProgressResourceNativeFixture.exe") == result["fixture"]
        result["sourceInputsUnchanged"] = all(fact(Path(row["live"]["path"])) == row["live"] for row in provenance)
        result["sourceInputsUnchanged"] &= fact(Path(live_fixture["path"])) == live_fixture
        result["passed"] &= result["helperUnchanged"] and result["fixtureUnchanged"] and result["sourceInputsUnchanged"]
    finally:
        result["finishedUtc"] = datetime.now(timezone.utc).isoformat()
        (output / "native-retry-result.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    sys.exit(main())
