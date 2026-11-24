"""Guarded ten-target application. --check-only is read-only; --apply writes main."""
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import tempfile

PREPARE_PATH = Path(__file__).resolve().parent / "prepare.py"
PREPARE_SHA = "367c77cb0243f80cf922ef8ae9b67a6a7d6c61c051320d2f97fb2dcc6b7873e6"
if hashlib.sha256(PREPARE_PATH.read_bytes()).hexdigest() != PREPARE_SHA:
    raise RuntimeError("Combined preparation control changed before import")
import prepare


ROOT, WORK, BUILD = prepare.ROOT, prepare.WORK, prepare.BUILD
MANIFEST_SHA = "cd750fa21b36989cda8d6fdd1cfd56d8627d7325ea6f3c17021f2c21f529abac"
PATCH_SHA = "ede89745777ca3a455296cb8ddaec5b9acea6efe3c2f0afbe9cdc13d1383cadc"
BEFORE_GRAPH_SHA = "2d3c53b6fe5a1f27f75bc95db4a27c343973c31f67ccf8a9056f92fb8584b703"
AFTER_GRAPH_SHA = "d56802e34a2f1bfd427ce4056e09c93b3c552cbcf4f45332231c84b8f85cbf63"
BUILD_BEFORE_SHA = "a4b9fb431f20e119341b911f070dbc79d7a355584e16557f127f871544e496c3"
BUILD_AFTER_SHA = "f9e008d3c1d1f4f9a42bc00e0c5770104823ce41101b5bca07cb47d00c0b9011"
STOP_SCRIPT_SHA = "acc06ca7ad6db8663937c684f1658d86ba11faa25dfa4158cbe8ebfc649f0453"
STOP_RECORD_SHA = "15fc95f56699649ddb2bc96f8b6aa5e1ad92b14c53058bd68d68be002d5a0e51"


def now() -> str:
    return dt.datetime.now(dt.timezone.utc).isoformat()


def state(path: Path) -> str | None:
    return prepare.sha(path) if path.exists() else None


def inputs(base: dict[str, str]) -> dict[str, str]:
    prepare.require(prepare.sha(prepare.CANVAS / "apply.py") == prepare.CANVAS_APPLY_SHA,
                    "Reviewed Canvas applier changed before compiler enumeration")
    spec = importlib.util.spec_from_file_location("frozen_canvas_apply", prepare.CANVAS / "apply.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module.current_inputs(base)


def supersession_digest() -> str:
    script = WORK / "stop_superseded_874.ps1"
    stop_file = WORK / "superseded-874-stop.json"
    before = WORK / "superseded-874-result-before-stop.json"
    after = WORK / "superseded-874-result-after-stop.json"
    prior_result = Path(r"D:\riz-build-cache-next\line-ring-numeric-integration-20261113\recovery-r1\result.json")
    prepare.require(prepare.sha(script) == STOP_SCRIPT_SHA, "Reviewed 874 supersession control changed")
    prepare.require(prepare.sha(stop_file) == STOP_RECORD_SHA, "Actual 874 supersession record changed")
    stop = prepare.read(stop_file)
    prepare.require(Path(stop["priorRun"]).resolve() == prior_result.parent.resolve()
                    and stop["priorControllerPid"] == 14388
                    and stop["controllerActualExitCode"] == -1
                    and stop["controllerNaturalExit"] is False
                    and stop["remainingOwnedChildren"] == 0
                    and all(stop[key] is False for key in ("acceptedThisBatch", "sdkGoalComplete",
                                                               "completeSeekPermitted", "originalUnfinishedResultRewritten"))
                    and prepare.sha(before) == stop["resultBeforeStopSnapshotSha256"]
                    and prepare.sha(after) == stop["resultAfterStopSnapshotSha256"]
                    and prepare.sha(prior_result) == stop["priorResultSha256"] == prepare.sha(after),
                    "874 supersession record or preserved result snapshots differ")
    prior = prepare.read(after)
    prepare.require(prior["controllerPid"] == 14388 and prior["passed"] is False
                    and not prior.get("finishedUtc") and prior["acceptedThisBatch"] is False,
                    "Preserved intermediate report claims a terminal or acceptance")
    stopped = stop["stoppedChildren"]
    prepare.require(len(stopped) == 1 and stopped[0]["pid"] == 43572
                    and stopped[0]["actualExitCode"] == -1 and stopped[0]["naturalExit"] is False
                    and set(prior["activeChildren"]) == {"basis-16"}
                    and prior["activeChildren"]["basis-16"]["pid"] == 43572,
                    "Actual terminated child and original active boundary differ")
    prior_work = ROOT / "work/line-ring-numeric-integration-20261113/recovery-r1"
    for field, path in (
        ("controllerScriptSha256", prior_work / "resume.py"),
        ("controllerLaunchRecordSha256", prior_work / "root-launch.json"),
        ("controllerStdoutSha256", prior_work / "controller.stdout.log"),
        ("controllerStderrSha256", prior_work / "controller.stderr.log"),
    ):
        prepare.require(prepare.sha(path) == stop[field], "Preserved intermediate control/log changed: " + field)
    logs = {path.name: path for path in prior_result.parent.glob("*.log")}
    inventory = stop["preservedLogs"]
    prepare.require(len(inventory) == len(logs) and {row["name"] for row in inventory} == set(logs),
                    "Preserved intermediate log inventory changed")
    for row in inventory:
        path = logs[row["name"]]
        prepare.require(path.stat().st_size == row["length"] and prepare.sha(path) == row["sha256"],
                        "Preserved intermediate log bytes changed: " + row["name"])
    return prepare.sha(stop_file)


def check():
    prepare.require(ROOT == prepare.AUTHORIZED_ROOT
                    and WORK == ROOT / "work/line-ring-target-primitives-integration-20261113",
                    "Applier is outside its authorized workspace")
    prepare.require(not (WORK / "application-record.json").exists(), "Combined package already applied")
    contents, before, after, patch, expected_manifest = prepare.calculate()
    prepare.require(prepare.sha(WORK / "patch-manifest.json") == MANIFEST_SHA
                    and prepare.sha(WORK / "integration.patch") == PATCH_SHA
                    and prepare.sha(WORK / "before-compiler-inputs.json") == BEFORE_GRAPH_SHA
                    and prepare.sha(WORK / "expected-after-compiler-inputs.json") == AFTER_GRAPH_SHA,
                    "Combined manifest, patch, or graph snapshot changed")
    manifest = prepare.read(WORK / "patch-manifest.json")
    prepare.require(manifest == expected_manifest and patch == (WORK / "integration.patch").read_bytes(),
                    "Combined manifest or patch differs from the frozen two candidates")
    prepare.require(before == prepare.read(WORK / "before-compiler-inputs.json")
                    and after == prepare.read(WORK / "expected-after-compiler-inputs.json"),
                    "Combined graph snapshots differ from the current exact payload")
    prepare.require(len(manifest["changes"]) == 10
                    and [row["targetPath"] for row in manifest["changes"]] == prepare.TARGETS
                    and manifest["runnerCaseBounds"] == {"lineRingSource": 19, "canvasBasis": 16,
                        "lineRingNumeric": 13, "ringXNumeric": 7, "legacy": 300, "defaultExpected": 355}
                    and manifest["nonCompilerControlsAfter"] == {BUILD: BUILD_AFTER_SHA}
                    and not manifest["completeSeekPermitted"] and not manifest["sdkGoalComplete"]
                    and not manifest["newMainFullMatrixPassed"],
                    "Combined manifest has another scope or case bound")
    payload_inventory = {p.relative_to(WORK / "files").as_posix()
                         for p in (WORK / "files").rglob("*") if p.is_file()}
    before_inventory = {p.relative_to(WORK / "before").as_posix()
                        for p in (WORK / "before").rglob("*") if p.is_file()}
    prepare.require(payload_inventory == set(prepare.TARGETS)
                    and before_inventory == {name for name, (old, _) in contents.items() if old},
                    "Combined payload or before-copy inventory differs from ten-target whitelist")
    for row in manifest["changes"]:
        name = row["targetPath"]
        old, new = contents[name]
        payload = WORK / row["payloadPath"]
        prepare.require(payload.is_file() and not payload.is_symlink() and payload.read_bytes() == new
                        and prepare.sha_bytes(new) == row["newTargetSha256"],
                        "Combined payload changed: " + name)
        target = prepare.safe_main(name)
        prior_sha = prepare.sha_bytes(old) if old else None
        prepare.require(row["mainBaseSha256"] == prior_sha and state(target) == prior_sha,
                        "Main target changed: " + name)
        if old:
            saved = WORK / row["beforePath"]
            prepare.require(saved.is_file() and not saved.is_symlink()
                            and saved.read_bytes() == old and target.read_bytes() == old,
                            "Before copy differs from main: " + name)
    prepare.require(inputs(before) == before and state(prepare.safe_main(BUILD)) == BUILD_BEFORE_SHA,
                    "Main compiler graph or Build control changed before application")
    return manifest, before, after


def apply() -> None:
    prepare.require(ROOT == prepare.AUTHORIZED_ROOT, "Applier is outside authorized workspace")
    lock = WORK / "application.lock"
    fd = os.open(lock, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    try:
        with os.fdopen(fd, "wb") as stream:
            stream.write((now() + " pid=" + str(os.getpid()) + "\n").encode("ascii"))
    except Exception:
        lock.unlink(missing_ok=True)
        raise
    changed: list[tuple[str, Path, str | None, str]] = []
    record = {"startedUtc": now(), "appliedToMain": False, "manifestSha256": MANIFEST_SHA,
              "baseCompilerInputCount": 874, "expectedCompilerInputCountAfter": 879,
              "gameStarted": False, "installed": False, "sdkGoalComplete": False,
              "completeSeekPermitted": False, "fullSeekPermitted": False,
              "newMainFullMatrixPassed": False, "prior874UnfinishedPreserved": False}
    try:
        manifest, before, after = check()
        stop_sha = supersession_digest()
        record["prior874UnfinishedPreserved"] = True
        record["superseded874StopRecordSha256"] = stop_sha
        expected = dict(before)
        for row in manifest["changes"]:
            name = row["targetPath"]
            target = prepare.safe_main(name)
            prepare.require(inputs(before) == dict(sorted(expected.items())),
                            "Concurrent compiler graph edit before write: " + name)
            expected_build = BUILD_AFTER_SHA if any(item[0] == BUILD for item in changed) else BUILD_BEFORE_SHA
            prepare.require(state(prepare.safe_main(BUILD)) == expected_build,
                            "Concurrent Build control edit before write: " + name)
            prepare.require(prepare.sha(WORK / "superseded-874-stop.json") == stop_sha,
                            "874 supersession evidence changed before write: " + name)
            prepare.require(state(target) == row["mainBaseSha256"],
                            "Concurrent target edit before write: " + name)
            payload = (WORK / row["payloadPath"]).read_bytes()
            prepare.require(prepare.sha_bytes(payload) == row["newTargetSha256"],
                            "Combined payload changed before write: " + name)
            if row["operation"] == "add":
                out_fd = os.open(target, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o666)
                changed.append((name, target, None, row["newTargetSha256"]))
                with os.fdopen(out_fd, "wb") as stream:
                    stream.write(payload)
                    stream.flush()
                    os.fsync(stream.fileno())
            else:
                with tempfile.NamedTemporaryFile(dir=WORK, prefix=".combined-apply-", delete=False) as stream:
                    staged = Path(stream.name)
                    stream.write(payload)
                    stream.flush()
                    os.fsync(stream.fileno())
                try:
                    prepare.require(state(target) == row["mainBaseSha256"],
                                    "Concurrent target edit during staging: " + name)
                    os.replace(staged, target)
                    changed.append((name, target, row["mainBaseSha256"], row["newTargetSha256"]))
                finally:
                    staged.unlink(missing_ok=True)
            prepare.require(state(target) == row["newTargetSha256"],
                            "Written target bytes differ: " + name)
            if name != BUILD:
                expected[name] = row["newTargetSha256"]
        prepare.require(inputs(before) == after and state(prepare.safe_main(BUILD)) == BUILD_AFTER_SHA,
                        "Post-application compiler graph or Build control differs")
        prepare.require(prepare.sha(WORK / "superseded-874-stop.json") == stop_sha,
                        "874 supersession evidence changed during application")
        prepare.require(all(state(prepare.safe_main(row["targetPath"])) == row["newTargetSha256"]
                            for row in manifest["changes"]), "A target changed after combined application")
        (WORK / "main-inputs-after.json").write_bytes(prepare.json_bytes(after))
        record.update({"appliedToMain": True, "compilerInputCountAfter": 879,
                       "mainInputsAfterSha256": prepare.sha(WORK / "main-inputs-after.json"),
                       "nonCompilerControlsAfter": {BUILD: BUILD_AFTER_SHA},
                       "targetRecords": manifest["changes"]})
        record["finishedUtc"] = now()
        (WORK / "application-record.json").write_bytes(prepare.json_bytes(record))
    except Exception as error:
        record["failure"] = str(error)
        record["rollback"] = []
        for name, target, prior_sha, payload_sha in reversed(changed):
            try:
                prepare.require(prepare.safe_main(name) == target, "Rollback target path changed")
            except Exception:
                record["rollback"].append({"targetPath": name, "restored": False,
                                           "reason": "Target path changed or left authorized root; left untouched"})
                continue
            if target.is_symlink() or state(target) != payload_sha:
                record["rollback"].append({"targetPath": name, "restored": False,
                                           "reason": "Concurrent edit or partial write; left untouched"})
                continue
            if prior_sha is None:
                try:
                    prepare.require(prepare.safe_main(name) == target, "Rollback target path changed")
                except Exception:
                    record["rollback"].append({"targetPath": name, "restored": False,
                                               "reason": "Target path changed before unlink; left untouched"})
                    continue
                target.unlink()
            else:
                saved = WORK / "before" / name
                if saved.is_symlink() or state(saved) != prior_sha:
                    record["rollback"].append({"targetPath": name, "restored": False,
                                               "reason": "Before copy changed; left payload untouched"})
                    continue
                old = saved.read_bytes()
                with tempfile.NamedTemporaryFile(dir=WORK, prefix=".combined-rollback-", delete=False) as stream:
                    staged = Path(stream.name)
                    stream.write(old)
                    stream.flush()
                    os.fsync(stream.fileno())
                try:
                    if target.is_symlink() or state(target) != payload_sha or prepare.sha_bytes(old) != prior_sha:
                        record["rollback"].append({"targetPath": name, "restored": False,
                                                   "reason": "Target or before copy changed during rollback; left untouched"})
                        continue
                    try:
                        prepare.require(prepare.safe_main(name) == target, "Rollback target path changed")
                    except Exception:
                        record["rollback"].append({"targetPath": name, "restored": False,
                                                   "reason": "Target path changed before replace; left untouched"})
                        continue
                    os.replace(staged, target)
                finally:
                    staged.unlink(missing_ok=True)
            record["rollback"].append({"targetPath": name, "restored": state(target) == prior_sha})
        record["finishedUtc"] = now()
        (WORK / "application-attempt.json").write_bytes(prepare.json_bytes(record))
        raise
    finally:
        lock.unlink(missing_ok=True)
    print(json.dumps({"appliedToMain": True, "compilerInputs": 879,
                      "nonCompilerControls": 1, "newMainFullMatrixPassed": False}))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--check-only", action="store_true")
    group.add_argument("--apply", action="store_true")
    options = parser.parse_args()
    if options.check_only:
        check()
        print(json.dumps({"bytesAdmissible": True, "targets": 10, "compilerInputsBefore": 874,
                          "expectedCompilerInputsAfter": 879, "defaultCasesAfter": 355,
                          "prior874MatrixClosureCertifiedHere": False,
                          "main879MatrixExecuted": False}))
    else:
        apply()
