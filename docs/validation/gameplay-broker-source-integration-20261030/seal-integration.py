"""Verify the actual manifests and current inputs before writing this batch's index.

The plan names independent processes. Counts from native fixtures, managed suites,
and SDK plugins remain separate because their coverage is different.
"""
import argparse
import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path


def read(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def digest(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def require(condition, message):
    if not condition:
        raise ValueError(message)


def run(plan_path):
    plan = read(plan_path)
    root = Path(__file__).resolve().parent
    checked = {}

    def check(row):
        path = Path(row.get("path", row.get("manifestPath", "")))
        require(path.is_absolute() and path.is_file(), f"Missing absolute evidence: {path}")
        actual = digest(path)
        require(actual == row["sha256"].lower(), f"Changed evidence: {path}")
        checked[str(path)] = {"path": str(path), "sha256": actual, "bytes": path.stat().st_size}

    manifests = {name: read(path) for name, path in plan.items()}
    for name in ("core", "full", "release", "solution", "sdk", "mapped"):
        manifest = manifests[name]
        require(manifest["passed"] is True, f"{name} did not pass")
        require(manifest["gameExecuted"] is False and manifest["installed"] is False,
                f"{name} has the wrong execution scope")
    core, full, release, solution, sdk, mapped = (manifests[name] for name in
                                                ("core", "full", "release", "solution", "sdk", "mapped"))
    require(all(row["sha256"] == digest(plan["core"]) for row in
                (full["sourceProof"], release["sourceSnapshot"], mapped["sourceProof"]))
            and all(row["sha256"] == digest(plan["full"]) for row in (solution["foundationProof"], sdk["foundationProof"])),
            "The current source proofs belong to different batches")
    require(core["runnerExitCode"] == 0 and core["snapshotCaptured"] is True and core["child"]["passed"] is True,
            "The current source snapshot did not complete")
    require(full["fullManagedMatrix"] is True and len(full["projects"]) == 27,
            "The full 27-suite matrix is required")
    require(all(row["passed"] is True and row["buildExitCode"] == row["testExitCode"] == 0
                and row["checks"] > 0 for row in full["projects"]), "A managed project has no actual passing exit")
    require(release["buildExitCode"] == 0 and release["sourceHashesUnchanged"] is True
            and len(release["files"]) == 6, "The six current Release assemblies did not build")
    require(solution["buildExitCode"] == solution["errors"] == 0 and solution["sourceHashesUnchanged"] is True,
            "The current solution did not build")
    require(sdk["runnerExitCode"] == 0 and sdk["checks"] == 14, "The independent SDK plugins did not pass")
    require(mapped["buildExitCode"] == 0 and len(mapped["modes"]) == 6
            and {row["mode"] for row in mapped["modes"]} == {"unhooked", "normal", "head", "tail", "consumer", "original"}
            and all(row["exitCode"] == 0 and row["checks"] > 0 for row in mapped["modes"])
            and mapped["gameDllMainExecuted"] is False, "The actual mapped clock modes did not pass")

    for group in (core["inputs"], core["snapshotFiles"], core["child"]["inputs"], core["child"]["artifacts"],
                  full["inputs"], full["compiledSources"], release["sources"], release["files"],
                  sdk["additionalSources"], mapped["sources"], mapped["inputs"], mapped["artifacts"]):
        for row in group:
            check(row)
    for row in (core["sourceArchive"], full["sourceProof"], release["sourceSnapshot"], release["nativeBuild"],
                solution["foundationProof"], solution["solution"], solution["script"], solution["profile"], solution["log"],
                sdk["foundationProof"], sdk["result"], mapped["sourceProof"], mapped["script"]):
        check(row)

    broker = manifests["broker"]
    require(broker["allPassed"] is True and broker["productionAdmission"] is False
            and broker["gameCodeExecuted"] is False, "Broker fixture has the wrong result or scope")
    for row in broker["rows"]:
        require(row["passed"] is True, "A broker child failed")
        exit_path = Path(plan["broker"]).parent / (row["mode"] + ".exit.txt")
        require(exit_path.read_text(encoding="utf-8-sig").strip() == "0", "A broker child has no actual exit zero")
        for path in (exit_path, exit_path.with_name(row["mode"] + ".log")):
            check({"path": str(path), "sha256": digest(path)})
    require(len(broker["rows"]) == broker["processes"], "Broker process count differs")
    freeze = manifests["brokerFreeze"]
    require(freeze["passed"] is True and freeze["processes"] == broker["processes"]
            and freeze["checks"] == broker["checks"] and freeze["productionAdmission"] is False,
            "The broker symbol/source freeze does not match the actual child matrix")
    for row in freeze["entries"]:
        check({"path": str(Path(plan["brokerFreeze"]).parent / row["path"]), "sha256": row["sha256"]})
        if row["kind"] == "actual-managed-pdb-sha256-source":
            check({"path": row["source"], "sha256": row["sha256"]})

    play = manifests["play"]
    require(play["passed"] is True and play["test"]["contractPassed"] is True
            and all(play[name]["actualExit"] == 0 for name in ("nativeBuild", "managedBuild", "test")),
            "Current-source Play duplicate execution did not pass")
    require(play["sourceProof"]["sha256"] == digest(plan["core"])
            and all(play[name] is True for name in ("sourceUnchanged", "sourceProofUnchanged", "sourceProofSnapshotUnchanged",
                                                    "sourceProofLiveInputsUnchanged", "nativeCurrentInputsUnchanged", "overlayInputsUnchanged")),
            "Play was not compiled from this current core proof")
    play_result = play["test"]["result"]
    require(play_result["actualOuterUpdateExecute"] is True and play_result["actualNativeAddAppends"] == 2
            and play_result["actualNonemptySourceRecords"] == 20 and play_result["actualNativeReferenceTlsReads"] == 83
            and play_result["waitedForFirstDuplicateExit"] is True and play_result["completedSecondDuplicateExit"] is True
            and play_result["gameExecuted"] is False and play_result["productionReadyIssued"] is False,
            "The actual nonempty Play contract or its scope differs")
    occurrence = manifests["occurrence"]
    require(occurrence["passed"] is True and occurrence["checks"] == {"passed": 175, "total": 175}
            and all(row["exitCode"] == 0 for row in occurrence["stages"])
            and occurrence["productionAdmission"] is False and occurrence["realStockHookCoverage"] is False,
            "The controlled occurrence provider result has the wrong scope")
    repository_path = Path(__file__).resolve().parents[3]
    for row in read(Path(plan["occurrence"]).parent / "live-owned-before.json"):
        check({"path": str(repository_path / row["path"]), "sha256": row["sha256"]})
    for name in ("play", "occurrence"):
        directory = Path(plan[name]).parent
        for row in read(directory / "source-before.json"):
            check({"path": str(directory / "source-snapshot" / row["path"]), "sha256": row["sha256"]})
        check({"path": str(directory / "source-snapshot.zip"), "sha256": manifests[name]["sourceArchiveSha256"]})
    check({"path": str(Path(plan["occurrence"]).parent / "native/RizToolbox.PlaybackOwnedFixture.dll"),
           "sha256": occurrence["nativeDllSha256"]})
    check({"path": str(Path(__file__).resolve()), "sha256": digest(__file__)})

    artifacts = []
    evidence_names = {"brokerFreeze": "FREEZE", "occurrence": "OWNED"}
    for name, path in plan.items():
        row = {"path": str(Path(path).resolve()), "sha256": digest(path)}
        check(row)
        artifacts.append({"id": name, "evidence_id": "E-" + evidence_names.get(name, name.upper()),
                          "source_type": "file", "source_ref": row["path"],
                          "observed_at": datetime.now(timezone.utc).isoformat(),
                          "linked_workitem": "WI-N2-BROKER-SOURCE-INTEGRATION", **checked[row["path"]]})
    repository = str(Path(__file__).resolve().parents[3]).lower()
    summary = {
        "kind": "gameplay-broker-source-integration", "passed": True, "flavor": None,
        "observedAtUtc": datetime.now(timezone.utc).isoformat(),
        "managedSuites": len(full["projects"]), "managedChecks": sum(row["checks"] for row in full["projects"]),
        "projects": [{key: row[key] for key in ("name", "checks", "buildExitCode", "testExitCode")} for row in full["projects"]],
        "coreChecks": {key: core["child"][key] for key in ("audioChecks", "coordinatorChecks", "receiverChecks")},
        "sdkChecks": sdk["checks"], "solutionErrors": solution["errors"], "solutionWarnings": solution["warnings"],
        "releaseAssemblies": len(release["files"]), "mappedProcesses": len(mapped["modes"]), "mappedChecks": mapped["checks"],
        "brokerProcesses": broker["processes"], "brokerChecks": int(broker["checks"]),
        "playDistinctModes": 1, "playAddAppends": 2, "playSourceRows": 20, "playSourceTransactions": 10,
        "playReferenceDeliveries": 83, "ownedProviderChecks": occurrence["checks"]["passed"],
        "revalidatedUniqueFiles": len(checked),
        "currentWorktreeInputs": sum(str(path).lower().startswith(repository) for path in checked),
        "gameExecuted": False, "gameDllMainExecuted": False, "installed": False,
        "productionAdmissionVerified": False, "realSpeedSeekEnabled": False, "fullB01B19Completed": False,
    }
    index = {"kind": "gameplay-broker-source-integration-evidence", "flavor": None, "scope": "scope.md",
             "artifacts": artifacts, "verifiedFiles": list(checked.values()),
             "countsHaveDistinctScopes": True, "summary": summary}
    for name, value in (("test-result.json", summary), ("evidence-index.json", index)):
        (root / name).write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(summary, ensure_ascii=False))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--plan", required=True)
    run(parser.parse_args().plan)
