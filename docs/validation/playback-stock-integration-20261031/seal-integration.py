"""Seal actual current-source S1/S2 results; keep each execution scope separate."""
import argparse
import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path


def read(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def sha(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def require(value, message):
    if not value:
        raise ValueError(message)


def write(path, value):
    Path(path).write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def run(plan_path, output):
    output = Path(output).resolve()
    require(not output.exists(), "Preserve earlier seal results; use a new directory")
    output.mkdir(parents=True)
    plan = read(plan_path)
    names = {"core", "full", "release", "solution", "sdk", "bridge", "bridgeSymbols",
             "group", "groupFocused", "mapped", "nativeBuild", "nativeChecks", "nativeSymbols"}
    require(set(plan) == names, "The declared actual proof set differs from this batch")
    manifests = {name: read(path) for name, path in plan.items()}
    checked = {}

    def check(path, expected):
        path = Path(path).resolve()
        require(path.is_file(), f"Missing evidence: {path}")
        digest = sha(path)
        require(digest == expected.lower(), f"Changed evidence: {path}")
        key = str(path).casefold()
        if key in checked:
            require(checked[key]["sha256"] == digest, f"Conflicting evidence: {path}")
        checked[key] = {"path": str(path), "sha256": digest, "bytes": path.stat().st_size}

    def collect(value, base):
        if isinstance(value, list):
            for item in value:
                collect(item, base)
        elif isinstance(value, dict):
            for path_key, hash_key in (("path", "sha256"), ("manifestPath", "sha256"),
                                       ("object", "sha256"), ("image", "imageSha256"),
                                       ("pdb", "pdbSha256")):
                path, digest = value.get(path_key), value.get(hash_key)
                if isinstance(path, str) and isinstance(digest, str):
                    resolved = Path(path) if Path(path).is_absolute() else base / path
                    check(resolved, digest)
            for item in value.values():
                collect(item, base)

    for name, value in manifests.items():
        require(value.get("passed") is True, f"{name} has no complete actual passing result")
        for flag in ("gameExecuted", "gameCodeExecuted", "gameDllMainExecuted", "installed",
                     "productionAdmission", "productionAdmissionVerified", "productionAdmissionGranted"):
            require(value.get(flag, False) is False, f"{name} has the wrong {flag} scope")
        if name in ("group", "groupFocused"):
            # A copy-stage record identifies the immutable core input. The
            # sole explicitly declared test overlay has a different actual
            # compiled checksum, backed by its own source and PDB documents.
            core_inputs = {str(Path(row["path"]).resolve()).casefold(): row["sha256"]
                           for row in manifests["core"]["snapshotFiles"]}
            overlay = value["overlay"]
            before, after = overlay["coreSource"], overlay["compiledSource"]
            changed = [row for row in value["compiledSources"] if row.get("testOnlyOverlay")]
            require(len(changed) == 1 and Path(after["path"]).parts[-2:] ==
                    ("RizToolbox.PlaybackSourceGroup.Tests", "Program.cs") and
                    changed[0]["path"] == after["path"] and changed[0]["sha256"] == after["sha256"] and
                    changed[0]["coreSha256"] == before["sha256"], f"{name} has an undeclared build overlay")
            require(len(value["sources"]) == len(core_inputs), f"{name} did not copy the complete current core")
            overlay_rows = 0
            for row in value["sources"]:
                require(core_inputs.get(str(Path(row["source"]).resolve()).casefold()) == row["sha256"],
                        f"{name} copy record has no exact core input")
                check(row["source"], row["sha256"])
                if Path(row["path"]).resolve() == Path(after["path"]).resolve():
                    require(row["sha256"] == before["sha256"], f"{name} overlay changed its original source identity")
                    check(before["path"], before["sha256"])
                    check(after["path"], after["sha256"])
                    overlay_rows += 1
                else:
                    check(row["path"], row["sha256"])
            require(overlay_rows == 1, f"{name} has no unique explicitly backed test overlay")
            collect({key: item for key, item in value.items() if key != "sources"}, Path(plan[name]).parent)
        else:
            collect(value, Path(plan[name]).parent)
        check(plan[name], sha(plan[name]))

    core, full, release, solution, sdk = (manifests[name] for name in ("core", "full", "release", "solution", "sdk"))
    bridge, group, focused, mapped = (manifests[name] for name in ("bridge", "group", "groupFocused", "mapped"))
    core_hash, full_hash, native_hash = sha(plan["core"]), sha(plan["full"]), sha(plan["nativeBuild"])
    for name in ("full", "bridge", "group", "groupFocused", "mapped"):
        require(manifests[name]["sourceProof"]["sha256"] == core_hash, f"{name} compiled a different source batch")
    require(release["sourceSnapshot"]["sha256"] == core_hash, "Release compiled a different source batch")
    require(solution["foundationProof"]["sha256"] == sdk["foundationProof"]["sha256"] == full_hash,
            "Solution or SDK uses a different full source tree")
    for name in ("bridge", "group", "groupFocused", "mapped"):
        require(manifests[name]["nativeProof"]["sha256"] == native_hash, f"{name} uses a different source helper")
    require(core["snapshotCaptured"] and core["runnerExitCode"] == 0 and core["child"]["passed"], "Core did not finish")
    require(full["fullManagedMatrix"] and len(full["projects"]) == 27 and
            all(row["passed"] and row["buildExitCode"] == row["testExitCode"] == 0 and row["checks"] > 0
                for row in full["projects"]), "The current 27-suite matrix did not pass")
    require(release["buildExitCode"] == 0 and release["sourceHashesUnchanged"] and len(release["files"]) == 6,
            "The six current Release assemblies did not build")
    require(solution["buildExitCode"] == solution["errors"] == 0 and solution["sourceHashesUnchanged"], "Solution did not pass")
    require(sdk["runnerExitCode"] == 0 and sdk["checks"] == 14, "Independent SDK plugins did not pass")

    def actual_json(path):
        lines = [line for line in Path(path).read_text(encoding="utf-8-sig").splitlines() if line.strip()]
        return json.loads(lines[-1])

    require(bridge["buildExitCode"] == 0 and len(bridge["modes"]) == 4 and
            {row["mode"] for row in bridge["modes"]} == {"normal", "callback-fault", "deferred-release", "withdraw-inflight"},
            "The source bridge mode set differs")
    for row in bridge["modes"]:
        actual = actual_json(row["log"]["path"])
        require(row["actualExitCode"] == 0 and actual["passed"] and actual["checks"] == row["checks"] and
                actual["actualDobbyPrepares"] == actual["actualDobbyCommits"] == 8,
                "A source bridge child has no exact actual execution proof")
    bridge_symbols = manifests["bridgeSymbols"]
    require(bridge_symbols["pePdbPairs"] == len(bridge_symbols["images"]) == 5 and
            bridge_symbols["sourceDocuments"] == sum(row["documentCount"] for row in bridge_symbols["images"]),
            "The actual bridge PE/PDB/document proof is incomplete")

    for name, wrapper, count in (("group", group, 19), ("groupFocused", focused, 2)):
        require(wrapper["buildExitCode"] == wrapper["runnerExitCode"] == 0 and wrapper["frozenInputsUnchanged"],
                f"{name} has no actual immutable build/execution result")
        child = read(wrapper["childProof"]["path"])
        require(child["allPassed"] and len(child["rows"]) == count and wrapper["processes"] == count,
                f"{name} has an incomplete process set")
        expected_calls = 38 if name == "group" else 6
        require(child["actualDobbyPrepareCalls"] == child["actualDobbyCommitCalls"] == expected_calls,
                f"{name} did not actually commit both physical targets")
        base = Path(wrapper["childProof"]["path"]).parent
        for row in child["rows"]:
            exit_path = base / (row["mode"] + ".exit.txt")
            require(exit_path.read_text(encoding="utf-8-sig").strip() == "0", f"{name} child has no actual zero exit")
            check(exit_path, sha(exit_path))
            require(row["passed"] and row["checks"] > 0, f"{name} child did not pass")
        require(wrapper["checks"] == sum(row["checks"] for row in child["rows"]), f"{name} count differs")

    require(mapped["buildExitCode"] == 0 and len(mapped["modes"]) == 4 and
            {row["mode"] for row in mapped["modes"]} == {"normal", "pitch-tail", "body", "consumer"} and
            mapped["gameDllMainExecuted"] is False and mapped["original44Repeated"] is False,
            "The mapped stock joint proof has the wrong scope")
    for row in mapped["modes"]:
        actual = actual_json(row["log"]["path"])
        require(row["actualExitCode"] == 0 and actual["passed"] and actual["actualDobbyCommitCount"] == 2 and
                actual["getTimeOriginalCalls"] == actual["sourceEntered"] == actual["sourceBefore"] == actual["sourceAfter"] == 0 and
                not any(actual[key] for key in ("nativeAuthorityActiveAfterTermination", "groupActiveAfterTermination",
                                                "getTimeActiveAfterTermination", "pitchActiveAfterTermination")),
                "A mapped child failed the native authority or zero-game-execution gate")

    native_build, native_checks, native_symbols = (manifests[name] for name in ("nativeBuild", "nativeChecks", "nativeSymbols"))
    require(native_build["actualExitCode"] == 0 and native_build["inputsUnchanged"] and native_build["frozenSourcesUnchanged"],
            "The exact native build is incomplete")
    require(len(native_checks["rows"]) == 13 and all(row["actualExitCode"] == 0 and row["passed"] and
            row["result"]["actualDobbyPrepared"] == row["result"]["actualDobbyCommitted"] == 8
            for row in native_checks["rows"]), "The actual 13-process native matrix did not pass")
    require(native_symbols["abiVersion"] == 1 and native_symbols["frameSize"] == 160 and native_symbols["infoSize"] == 136,
            "The native source ABI proof differs")
    require(len(native_symbols["images"]) == 3 and len(native_symbols["compiledObjects"]) == 5,
            "The actual native PE/PDB/COFF proof is incomplete")

    check(plan_path, sha(plan_path))
    check(__file__, sha(__file__))
    repository = str(Path(__file__).resolve().parents[3]).casefold()
    summary = {
        "kind": "current-stock-source-integration", "passed": True, "flavor": None,
        "observedAtUtc": datetime.now(timezone.utc).isoformat(), "sourceProofSha256": core_hash,
        "managedSuites": 27, "managedChecks": sum(row["checks"] for row in full["projects"]),
        "releaseAssemblies": 6, "solutionErrors": solution["errors"], "solutionWarnings": solution["warnings"],
        "sdkChecks": 14, "sourceBridgeProcesses": 4, "sourceBridgeChecks": bridge["checks"],
        "sourceBridgePePdbPairs": bridge_symbols["pePdbPairs"], "sourceBridgeDocuments": bridge_symbols["sourceDocuments"],
        "groupProcesses": group["processes"], "groupChecks": group["checks"],
        "groupFocusedProcesses": focused["processes"], "groupFocusedChecks": focused["checks"],
        "mappedProcesses": 4, "mappedChecks": mapped["checks"],
        "nativeProcesses": 13, "nativeChecks": sum(row["result"]["checks"] for row in native_checks["rows"]),
        "verifiedUniqueFiles": len(checked),
        "currentWorktreeInputs": sum(row["path"].casefold().startswith(repository) for row in checked.values()),
        "testOnlyGroupOverlays": True, "foundationCompositionAvailable": False,
        "completeStockProviderImplemented": False, "crossRunHandoffImplemented": False,
        "gameExecuted": False, "gameDllMainExecuted": False, "installed": False,
        "productionAdmissionVerified": False, "realSpeedSeekEnabled": False, "fullB01B19Completed": False,
    }
    artifacts = [{"id": "E-" + name.upper(), "path": str(Path(path).resolve()), "sha256": sha(path)}
                 for name, path in plan.items()]
    write(output / "evidence-index.json", {"kind": "stock-source-integration-evidence", "flavor": None,
                                           "artifacts": artifacts, "verifiedFiles": list(checked.values()),
                                           "countsHaveDistinctScopes": True, "summary": summary})
    write(output / "test-result.json", summary)
    print(json.dumps(summary, ensure_ascii=False))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--plan", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    run(args.plan, args.output)
