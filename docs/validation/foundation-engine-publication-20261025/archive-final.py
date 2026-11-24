"""Archive verified owned Engine publication evidence without target execution.

Run only after the root has finished its report and all selected evidence::

  C:/Python314/python.exe archive-final.py \
    --cache C:/Users/71957/riz-build-cache/engine-publication-r4-20261025 \
    --output C:/Users/71957/Desktop/riz_toolbox/docs/validation/foundation-engine-publication-20261025

This file hashes and archives existing files only. It does not build, load a
target DLL, invoke a verifier, start a child, install, or change source files.
All new output names use exclusive creation; previous archives are preserved.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import struct
import sys
import zipfile


BATCH = Path(__file__).resolve().parent
REPO = BATCH.parents[2]
BASE = REPO / "docs/validation/foundation-doorstop-cold-20261025"
PREVIOUS_MANIFEST = BASE / "source-final-manifest.json"
REVIEW_SHA = "7d7651a4c472a7e0891025a929f8ee8e0667768fd58fedbd21331cd7c2451a13"
DEFAULT_CACHE = Path("C:/Users/71957/riz-build-cache/engine-publication-r4-20261025")
SELECTORS = {(1, 0), (8, 0)} | {(stage, index) for stage in range(2, 8) for index in range(4)}
SAFE_SELECTORS = {(1, 0), (8, 0)} | {(2, index) for index in range(4)} | {(3, 0), (4, 0)}
CHANGED = {
    "docs/validation/foundation-doorstop-cold-20261025/research/engine-game-profile.json": "Correct fixed-game profile facts; historical inputs retained",
    "docs/validation/foundation-doorstop-cold-20261025/research/engine-game-profile.md": "Document fixed-game profile erratum and unchanged admission boundary",
    "docs/validation/foundation-doorstop-cold-20261025/research/extract-engine-game-profile.py": "Correct fixed-game profile extraction",
    "native/RizToolbox.EngineBootstrap/EngineGameProfile.h": "Correct generated fixed-game facts; no production admission",
    "src/RizToolbox.Runtime/Hooks/NativeEngineNodeContextBridge.cs": "Read-only adoption of the existing native V4 publication",
    "src/RizToolbox.Runtime/Hooks/NativeEngineNodeObserverDispatcher.cs": "Adopt already configured native entries without republishing targets",
    "tests/RizToolbox.Runtime.Tests/Program.cs": "Expose owned same-process V4 and adoption fixture coverage",
}
NEW = (
    "native/RizToolbox.EngineBootstrap/EnginePublication.h",
    "native/RizToolbox.EngineBootstrap/EngineBootstrapProfile.h",
    "native/RizToolbox.EngineBootstrap/EngineFirstInstall.cpp",
    "native/RizToolbox.EngineBootstrap/EngineOriginals.asm",
    "native/RizToolbox.EngineBootstrapFixture/FixtureApi.h",
    "native/RizToolbox.EngineBootstrapFixture/Targets.cpp",
    "native/RizToolbox.EngineBootstrapFixture/Targets.asm",
    "native/RizToolbox.EngineBootstrapFixture/Importer.cpp",
    "native/RizToolbox.EngineBootstrapFixture/Host.cpp",
    "native/RizToolbox.EngineBootstrapFixture/ManagedHost.cpp",
    "src/RizToolbox.Runtime/Hooks/NativeEngineFirstInstall.cs",
    "src/RizToolbox.Runtime/Hooks/NativeEnginePublication.cs",
    "tests/RizToolbox.Runtime.Tests/NativeEnginePublicationManagedEntry.cs",
    "tests/RizToolbox.Runtime.Tests/NativeEnginePublicationTests.cs",
    "scripts/Build-EnginePublicationFixture.ps1",
    "docs/validation/foundation-engine-publication-20261025/preserve-profile-erratum.py",
    "docs/validation/foundation-engine-publication-20261025/generate-owned-profile.py",
    "docs/validation/foundation-engine-publication-20261025/observe-bootstrap-failure.py",
    "docs/validation/foundation-engine-publication-20261025/run-owned-preflight.py",
    "docs/validation/foundation-engine-publication-20261025/run-fault-matrix.py",
    "docs/validation/foundation-engine-publication-20261025/verify-fault-matrix.py",
    "docs/validation/foundation-engine-publication-20261025/check-dynamic-refusal.py",
    "docs/validation/foundation-engine-publication-20261025/archive-final.py",
)
PREFLIGHT_NAMES = (
    "preflight-native", "preflight-managed-normal-r6", "preflight-detach-r6",
    "preflight-original-seh-r6", "preflight-original-cpp-r6", "preflight-exceptions-r6",
)
OUTPUT_NAMES = (
    "source-final-inputs.zip", "source-final-manifest.json", "raw-inputs.zip", "raw-manifest.json",
    "binaries.zip", "binary-manifest.json", "history-raw-inputs.zip", "history-raw-manifest.json",
    "evidence-validation.json", "document-manifest.json", "environment-final-check.json", "summary.json",
)


def require(value, message):
    if not value:
        raise ValueError(message)


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def sha(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def norm(path):
    return str(Path(path).resolve()).replace("\\", "/").casefold()


def json_write(path, value):
    with path.open("x", encoding="utf-8", newline="\n") as stream:
        json.dump(value, stream, ensure_ascii=False, indent=2)
        stream.write("\n")


def file_row(path, name=None):
    path = Path(path).resolve()
    require(path.is_file() and not path.is_symlink(), "Missing or linked archive input: " + str(path))
    return dict(path=name if name is not None else str(path), sha256=sha(path), bytes=path.stat().st_size)


def hash_check(row, label="Evidence"):
    path = Path(row["path"])
    require(path.is_absolute() and path.is_file() and not path.is_symlink(), label + " file is missing or linked: " + str(path))
    require(sha(path) == row["sha256"], label + " current hash differs: " + str(path))
    if "bytes" in row:
        require(path.stat().st_size == row["bytes"], label + " exact EOF differs: " + str(path))
    return path


def raw_reference(directory, meta, expected_size):
    name = meta["path"]
    require(isinstance(name, str) and name and Path(name).name == name and not any(c in name for c in ("/", "\\", ":")),
            "Raw reference leaves its exact evidence directory")
    path = directory / name
    require(path.is_file() and path.stat().st_size == expected_size and sha(path) == meta["sha256"], "Raw artifact hash or EOF differs: " + str(path))
    return path


def held_check(rows, label):
    for row in rows:
        expected = row.get("sha256", row.get("beforeSha256"))
        require(expected and row.get("afterSha256", expected) == expected, label + " changed during its actual run")
        hash_check(dict(path=row["path"], sha256=expected), label)
        if "afterIdentity" in row:
            require(row["afterIdentity"] == row["identity"], label + " held file identity changed during its actual run")


def source_set():
    previous = read(PREVIOUS_MANIFEST)
    rows = previous["files"]
    require(len(rows) == 626 and len({item["path"] for item in rows}) == 626, "G source identity set differs")
    previous_zip = BASE / "source-final-inputs.zip"
    require(sha(previous_zip) == previous["sourceArchiveSha256"], "Historical G source ZIP changed")
    with zipfile.ZipFile(previous_zip) as archive:
        require(set(archive.namelist()) == {row["path"] for row in rows} and len(archive.infolist()) == 626, "Historical G ZIP member set differs")
        for row in rows:
            require(hashlib.sha256(archive.read(row["path"])).hexdigest() == row["sha256"], "Historical G source bytes changed")
    current, changed = [], []
    for row in rows:
        name = row["path"]
        path = REPO / name
        observed = file_row(path, name)
        current.append(observed)
        if observed["sha256"] != row["sha256"]:
            require(name in CHANGED, "Undeclared prior source change: " + name)
            changed.append(dict(path=name, beforeSha256=row["sha256"], afterSha256=observed["sha256"], reason=CHANGED[name]))
    require({row["path"] for row in changed} == set(CHANGED), "The seven declared changes differ from actual current source bytes")
    require(len(NEW) == len(set(NEW)) == 23 and not set(NEW) & {row["path"] for row in rows}, "New source declaration duplicates or overlaps historical inputs")
    new = [file_row(REPO / name, name) for name in NEW]
    current.extend(new)
    require(len(current) == 649 and len({row["path"] for row in current}) == 649, "Actual declared source set differs from 626 retained + 23 new")
    return dict(files=sorted(current, key=lambda row: row["path"]), previousSourcesChanged=changed, newInputs=new,
                previousManifestPath=str(PREVIOUS_MANIFEST), previousManifestSha256=sha(PREVIOUS_MANIFEST),
                previousSourceArchiveSha256=sha(previous_zip))


def validate(cache):
    evidence = dict(kind="owned-engine-publication-archive-input-validation", schemaVersion=1, validatedAtUtc=datetime.now(timezone.utc).isoformat(),
                    buildsOrTestsStartedByArchiver=0, targetImagesLoadedByArchiver=False, primaryProcesses=[], supportingEvidence=[],
                    previousDiagnosticProcessesCountedAsPrimary=False)
    build_path = cache / "publication-build-manifest.json"
    build = read(build_path)
    require(build["kind"] == "engine-publication-build" and build["schemaVersion"] == 1
            and build["fixtureExecuted"] is False and build["gameExecuted"] is False and build["installed"] is False
            and build["productionAdmissionProved"] is False, "Wrong completed selected native build manifest")
    require(len(build["sources"]) == 13 and len(build["files"]) == 31, "Selected native build source/output counts differ")
    for row in build["sources"] + build["files"] + [build["generatedProfile"], build["generatedHeader"]]:
        hash_check(row, "Native build")
    evidence["nativeBuild"] = dict(path=str(build_path), sha256=sha(build_path), sources=13, binaries=31)
    review_path = BATCH / "preflight-reviewed-facts.json"
    require(sha(review_path) == REVIEW_SHA, "The selected six-workload offline review changed")
    review = read(review_path)
    require(review["passed"] and review["schemaVersion"] == 1 and len(review["cases"]) == 6
            and review["childStartedByReviewer"] is False and review["sourceOrBinaryModified"] is False,
            "Six actual workload review is incomplete or has a different evidence boundary")
    require({row["name"] for row in review["cases"]} == set(PREFLIGHT_NAMES), "Wrong six primary owned workload selectors")
    for row in review["inputs"]:
        hash_check(row, "Reviewed workload input")
    managed_inputs = {}
    for reviewed in review["cases"]:
        directory = cache / reviewed["name"]
        report_path = directory / "preflight-result.json"
        process = read(report_path)
        host = read(directory / "case.json")
        require(process["passed"] and process["allInputsUnchanged"] and process["actualExitCode"] == 0
                and process["actualProcessId"] == reviewed["actualProcessId"] == host["actualProcessId"]
                and process["buildManifestSha256"] == sha(build_path) and host["passed"] and host["finished"]
                and host["sehCode"] == 0 and not host["error"] and not host["failures"], "Actual selected owned workload did not complete successfully")
        held_check(process["inputs"], "Primary owned workload")
        for row in process["inputs"]:
            path = Path(row["path"])
            if path.is_relative_to(REPO / "tests/RizToolbox.Runtime.Tests/bin/Release/net8.0"):
                if str(path) in managed_inputs:
                    require(managed_inputs[str(path)] == row["sha256"], "Different managed binaries were used in selected primary workloads")
                managed_inputs[str(path)] = row["sha256"]
        raw_reference(directory, host["initialSnapshot"], 2162752)
        raw_reference(directory, host["finalSnapshot"], 2162752)
        raw_reference(directory, host["rowsSnapshot"], host["rowsSnapshot"]["rowCount"] * 8448)
        if process.get("managedPassed") is not None:
            managed = read(directory / "managed-result.json")
            require(process["managedPassed"] and process["managedProcessId"] == process["actualProcessId"]
                    and host["managedRequested"] and host["managedResult"] == 0 and managed["passed"], "Same-process managed workload failed")
        evidence["primaryProcesses"].append(dict(category="owned-native-or-same-process-managed", selector=reviewed["name"],
                                                 actualProcessId=process["actualProcessId"], actualExitCode=0, processReportSha256=sha(report_path)))
    evidence["reviewedWorkloads"] = dict(path=str(review_path), sha256=sha(review_path), assertions=review["reviewAssertions"], cases=6,
                                         existingRawInputs=len(review["inputs"]), managedRunInputs=[dict(path=k, sha256=v) for k, v in managed_inputs.items()])
    matrix_path = cache / "fault-matrix-r1/fault-matrix-result.json"
    matrix = read(matrix_path)
    require(matrix["kind"] == "engine-publication-fault-matrix-process-observation" and matrix["expectedProcessBehaviorMatched"]
            and matrix["allHeldInputsUnchanged"] and matrix["completedCaseCount"] == 26 and len(matrix["cases"]) == 26,
            "Actual fault parent did not complete all selected expected process behaviors")
    require({(row["stage"], row["index"]) for row in matrix["cases"]} == SELECTORS, "Actual fault selectors differ")
    held_check(matrix["inputFiles"], "Fault matrix held input")
    verifier_path = BATCH / "fault-matrix-verified-r2.json"
    verified = read(verifier_path)
    require(verified["kind"] == "offline-owned-startup-fault-verification" and verified["passed"] and not verified["failures"]
            and len(verified["cases"]) == 26 and all(row["passed"] for row in verified["cases"])
            and verified["checks"] == 2035 and verified["matrixSha256"] == sha(matrix_path)
            and verified["verifierSha256"] == sha(BATCH / "verify-fault-matrix.py")
            and verified["decoderSha256"] == sha(BATCH / "observe-bootstrap-failure.py"), "Independent 26-fault verification differs from selected current evidence")
    independent = {(row["stage"], row["index"]): row for row in verified["cases"]}
    safe = fatal = 0
    for case in matrix["cases"]:
        selector = case["stage"], case["index"]
        directory = Path(case["directory"])
        require(directory.resolve() == (matrix_path.parent / case["name"]).resolve() and case["expectedProcessBehaviorMatched"]
                and not case["timedOut"], "Fault case was incomplete or left its selected directory")
        disk_case = read(directory / "case-result.json")
        require(disk_case == case, "Aggregate fault result differs from its exact case file")
        for row in case["artifactFiles"]:
            hash_check(row, "Fault case artifact")
        checked = independent[selector]
        if selector in SAFE_SELECTORS:
            safe += 1
            host_path = directory / "case.json"
            host = read(host_path)
            require(not case["fatalExpected"] and case["actualExitCode"] == 1 and host["sehCode"] == 0
                    and checked["hostResultSha256"] == sha(host_path) and checked["actualProcessId"] == host["actualProcessId"]
                    and checked["independentSourceReadback"] is False, "Expected no-write fault has a different process/result boundary")
            pid, exit_code = host["actualProcessId"], 1
        else:
            fatal += 1
            result_path = directory / "observation/observer-result.json"
            result = read(result_path)
            require(case["fatalExpected"] and case["actualExitCode"] == 0 and result["passed"] and result["exitObserved"]
                    and result["fatalEventObserved"] and result["lastChanceObserved"] and not result["timedOut"]
                    and result["allHeldInputsUnchanged"] and result["actualExitCode"] in (0xc0000409, 0xc0000602)
                    and result["actualExitCode"] == result["processHandleExitCode"]
                    and checked["observerResultSha256"] == sha(result_path) and checked["independentSourceReadback"] is True,
                    "Actual fatal fault lacks the selected complete debug observation")
            held_check(result["inputFiles"], "Fatal observer held input")
            terminal = [row for row in result["snapshots"] if row["reason"] == "last-chance-exception"]
            require(len(terminal) == 1 and terminal[0]["actualSourceBodies"]["complete"]
                    and len(terminal[0]["actualSourceBodies"]["functions"]) == 4, "Fatal selected source capture is incomplete")
            raw_reference(directory / "observation", terminal[0], 4096)
            for body in terminal[0]["actualSourceBodies"]["functions"]:
                for name in ("disk", "memory"):
                    raw_reference(directory / "observation", body[name], body["byteSize"])
            pid, exit_code = result["actualChildPid"], result["actualExitCode"]
        require(pid == checked["actualProcessId"] and exit_code == checked["actualExitCode"], "Independent fault verifier selected a different actual child")
        evidence["primaryProcesses"].append(dict(category="compiled-startup-fault", selector=case["name"],
                                                 actualProcessId=pid, actualExitCode=exit_code, independentSourceReadback=selector not in SAFE_SELECTORS))
    require((safe, fatal) == (8, 18), "Safe/fatal actual evidence count differs")
    evidence["faultMatrix"] = dict(path=str(matrix_path), sha256=sha(matrix_path), verifierPath=str(verifier_path),
                                   verifierSha256=sha(verifier_path), independentChecks=verified["checks"], cases=26,
                                   safeNoWriteProcesses=safe, fatalProcesses=fatal, fatalActualSourceCaptures=18,
                                   auxiliaryDebuggerParentProcesses=18)
    dynamic_path = cache / "dynamic-refusal-r1/dynamic-refusal-result.json"
    dynamic = read(dynamic_path)
    require(dynamic["kind"] == "engine-bootstrap-dynamic-refusal-parent" and dynamic["passed"] and len(dynamic["cases"]) == 2
            and {row["profile"] for row in dynamic["cases"]} == {"owned", "production-candidate"}
            and dynamic["buildManifestSha256"] == sha(build_path), "Two dynamic no-publication refusal cases are incomplete")
    held_check(dynamic["heldInputs"], "Dynamic-refusal parent")
    for case in dynamic["cases"]:
        directory = dynamic_path.parent / case["profile"]
        child_path = directory / "child-result.json"
        child = read(child_path)
        require(case["passed"] and case["actualExitCode"] == 0 and not case["timedOut"] and child["passed"]
                and child["actualProcessId"] == case["actualChildPid"] and case["childReportSha256"] == sha(child_path)
                and child["targetApisCalled"] is False and child["configureCalled"] is False,
                "Dynamic refusal did not preserve its actual selected no-configuration result")
        held_check(child["heldInputs"], "Dynamic-refusal child")
        for name in case["snapshots"]:
            require(Path(name).name == name and (directory / name).is_file() and (directory / name).stat().st_size == 4096,
                    "Dynamic refusal V4 byte capture missing")
        evidence["primaryProcesses"].append(dict(category="dynamic-load-refusal", selector=case["profile"],
                                                 actualProcessId=case["actualChildPid"], actualExitCode=0, childReportSha256=sha(child_path)))
    evidence["dynamicRefusal"] = dict(path=str(dynamic_path), sha256=sha(dynamic_path), cases=2)
    environment_path = BATCH / "environment-current.json"
    environment = read(environment_path)
    require(environment["allMatched"] and len(environment["files"]) == 45
            and (environment["installedFiles"], environment["originalGameFiles"], environment["packageFiles"]) == (18, 4, 23)
            and environment["gameExecuted"] is False and environment["installed"] is False, "Existing game/package preservation set differs")
    for row in environment["files"]:
        require(row["sha256"] == row["expected"], "Previously recorded preservation mismatch")
        hash_check(row, "Current real-game/package preservation")
    evidence["environment"] = dict(path=str(environment_path), sha256=sha(environment_path), files=45,
                                    installedFiles=18, originalGameFiles=4, existingPackageFiles=23, allMatched=True)
    require(len(evidence["primaryProcesses"]) == 34, "Actual primary process count differs from six owned workloads + 26 faults + two dynamic refusals")
    evidence["primaryOwnedTargetProcesses"] = 34
    return evidence, build, managed_inputs, environment


def archive(output, name, inputs):
    require(len({entry[0] for entry in inputs}) == len(inputs), "Archive member names overlap: " + name)
    path = output / name
    rows = []
    with zipfile.ZipFile(path, "x", compression=zipfile.ZIP_DEFLATED, compresslevel=6, allowZip64=True) as zipped:
        for member, source in inputs:
            require(not member.startswith("/") and ".." not in Path(member).parts and "\\" not in member,
                    "Archive member escapes its namespace")
            row = file_row(source, member)
            row["originalPath"] = str(source.resolve())
            zipped.write(source, member)
            rows.append(row)
    with zipfile.ZipFile(path) as zipped:
        require(len(zipped.infolist()) == len(rows) and set(zipped.namelist()) == {row["path"] for row in rows}, "Final ZIP has missing/duplicate members")
        for row in rows:
            data = zipped.read(row["path"])
            require(len(data) == row["bytes"] and hashlib.sha256(data).hexdigest() == row["sha256"], "Final ZIP member hash/EOF mismatch")
            require(sha(Path(row["originalPath"])) == row["sha256"], "Archive input changed during ZIP creation")
    return dict(path=path.name, sha256=sha(path), bytes=path.stat().st_size, files=rows, membersVerifiedAfterClose=True)


def directory_inputs(root, prefix):
    require(root.is_dir() and not root.is_symlink(), "Raw evidence directory is missing or linked: " + str(root))
    return [(prefix + "/" + path.relative_to(root).as_posix(), path) for path in sorted(root.rglob("*")) if path.is_file()]


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--cache", type=Path, default=DEFAULT_CACHE)
    parser.add_argument("--output", type=Path, default=BATCH)
    args = parser.parse_args()
    try:
        require(args.cache.is_absolute() and args.output.is_absolute(), "Cache/output paths must be absolute")
        cache, output = args.cache.resolve(), args.output.resolve()
        require(cache == DEFAULT_CACHE.resolve(), "Only the selected actual r4 cache can be finalized by this batch")
        require(output.is_dir() and output.resolve() == BATCH.resolve(), "Final artifacts belong in this exact H validation directory")
        require(all(not (output / name).exists() for name in OUTPUT_NAMES), "Preserve existing H finalization output; do not overwrite")
        sources = source_set()
        evidence, build, managed_run_inputs, environment = validate(cache)
        source_archive = archive(output, "source-final-inputs.zip", [(row["path"], REPO / row["path"]) for row in sources["files"]])
        require(all(sha(REPO / row["path"]) == row["sha256"] for row in sources["files"]), "Declared source set changed while archiving")
        sources.update(sourceArchiveSha256=source_archive["sha256"], sourceArchiveBytes=source_archive["bytes"],
                       sourceFiles=649, previousChangedFiles=7, newDeclaredFiles=23, archiveMembersVerified=True)
        json_write(output / "source-final-manifest.json", sources)
        binary_inputs = [("native/" + Path(row["path"]).resolve().relative_to(cache).as_posix(), Path(row["path"]).resolve()) for row in build["files"]]
        managed_directory = REPO / "tests/RizToolbox.Runtime.Tests/bin/Release/net8.0"
        managed_paths = sorted(path for path in managed_directory.iterdir() if path.is_file() and
                               (path.suffix in (".dll", ".pdb") or path.name.endswith((".deps.json", ".runtimeconfig.json"))))
        require(managed_paths and all(Path(name) in managed_paths for name in managed_run_inputs), "A runtime-held managed input is missing from the final binary archive")
        binary_inputs.extend(("managed-tests/" + path.name, path) for path in managed_paths)
        binary_archive = archive(output, "binaries.zip", binary_inputs)
        json_write(output / "binary-manifest.json", dict(**binary_archive, nativeFiles=31, managedFiles=len(managed_paths),
                                                        actualManagedRunInputs=[dict(path=key, sha256=value) for key, value in managed_run_inputs.items()]))
        raw_archive = archive(output, "raw-inputs.zip", directory_inputs(cache, "r4-cache"))
        json_write(output / "raw-manifest.json", dict(**raw_archive, omittedFiles=[], completeSelectedCache=True,
                                                     includesAllPrimaryAndEarlierR4Preflights=True))
        history_inputs = []
        history_roots = ("engine-publication-preflight-20261025", "engine-publication-r2-20261025", "engine-publication-r3-20261025")
        for name in history_roots:
            history_inputs.extend(directory_inputs(cache.parent / name, name))
        history_inputs.extend(directory_inputs(BATCH / "history", "h-batch-history"))
        history_archive = archive(output, "history-raw-inputs.zip", history_inputs)
        json_write(output / "history-raw-manifest.json", dict(**history_archive, originalDirectoriesRetained=True,
                                                             previousDiagnosticProcessesCountedAsPrimary=False))
        documents = set(BATCH.rglob("*.md"))
        documents.update(path for path in (REPO / "README.md", REPO / ".docs/PROGRESS.md",
                                           REPO / ".docs/foundation-expansion/SPEC.md",
                                           REPO / ".docs/foundation-expansion/CHECKLIST.md") if path.is_file())
        documents.update(path for path in (REPO / "docs").glob("2026-10-25*report.md") if path.is_file())
        document_rows = [file_row(path, path.relative_to(REPO).as_posix()) for path in sorted(documents)]
        json_write(output / "document-manifest.json", dict(kind="current-H-document-fixity", files=document_rows,
                                                          includedInSourceArchive=False, capturedAtUtc=datetime.now(timezone.utc).isoformat()))
        # Check the current environment again after all ZIP writing. Existing
        # installed files and the old package remain outside the new outputs.
        for row in environment["files"]:
            hash_check(row, "Post-archive environment preservation")
        json_write(output / "environment-final-check.json", dict(**environment, currentHashesRecheckedAfterArchive=True))
        for row in sources["files"]:
            require(sha(REPO / row["path"]) == row["sha256"], "Final declared source changed during raw/history archiving")
        json_write(output / "evidence-validation.json", evidence)
        summary = dict(kind="owned-engine-first-publication-final-archive", schemaVersion=1, evidenceValidated=True,
                       sourceInputs=649, changedPreviousSources=7, newInputs=23, nativeBinaryFiles=31,
                       managedBinaryFiles=len(managed_paths), primaryOwnedTargetProcesses=34,
                       ownedNativeAndSameProcessManagedCases=6, compiledFaultCases=26, safeNoWriteFaults=8,
                       fatalFaultsWithActualSourceReadback=18, dynamicNoPublicationRefusals=2,
                       auxiliaryCurrentDebuggerParents=18, historicalDiagnosticProcessesIncludedInPrimary=False,
                       preflightReviewAssertions=evidence["reviewedWorkloads"]["assertions"], independentFaultChecks=evidence["faultMatrix"]["independentChecks"],
                       rawFiles=len(raw_archive["files"]), historicalRawFiles=len(history_archive["files"]), documentFiles=len(document_rows),
                       archives={"source": {key: value for key, value in source_archive.items() if key != "files"},
                                 "binary": {key: value for key, value in binary_archive.items() if key != "files"},
                                 "raw": {key: value for key, value in raw_archive.items() if key != "files"},
                                 "history": {key: value for key, value in history_archive.items() if key != "files"}},
                       allArchiveMembersReReadAndHashVerified=True, installedFilesUnchanged=18, originalGameFilesUnchanged=4,
                       existingPackageFilesUnchanged=23, archiverStartedBuildsOrTests=0, sourceFilesModifiedByArchiver=0,
                       ownedV4PublicationVerified=True, sameProcessManagedReadOnlyAdoptionVerified=True,
                       gameExecuted=False, installed=False, productionAdmissionProved=False, wholeLoaderSafetyVerified=False,
                       fullB01ThroughB19AcceptanceCompleted=False, realUnityGcAndObjectLifetimeVerified=False,
                       fullSchedulerCancellationClosureVerified=False, fullManagedRegressionRerun=False)
        json_write(output / "summary.json", summary)
        print(json.dumps(summary))
        return 0
    except Exception as error:
        print(json.dumps(dict(evidenceValidated=False, error=f"{type(error).__name__}: {error}")), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
