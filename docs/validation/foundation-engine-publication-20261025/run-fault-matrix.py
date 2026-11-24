"""Run the 26 compiled owned Bootstrap faults, retaining actual parent evidence.

Root-owned execution only, after a complete native build::

  C:/Python314/python.exe run-fault-matrix.py \
    --build C:/absolute/build/publication-build-manifest.json \
    --output C:/absolute/new-fault-matrix-directory

This runner matches process behavior and preserves native facts. It is not the
independent publication verifier and never labels its result as test passed.
Safe faults execute the fixed native host directly. Fatal faults execute the
fixed debug observer, which creates its own fixed native host from the start.
"""
from __future__ import annotations

import argparse
import ctypes as C
import hashlib
import json
import os
from pathlib import Path
import struct
import subprocess
import sys
import time


HERE = Path(__file__).resolve().parent
OBSERVER = HERE / "observe-bootstrap-failure.py"
FAILFAST_CODES = {0xC0000409, 0xC0000602}
SAFE = {(1, 0), (8, 0), (2, 0), (2, 1), (2, 2), (2, 3), (3, 0), (4, 0)}
SELECTORS = [(1, 0), (8, 0)] + [(stage, index) for stage in range(2, 8) for index in range(4)]
SHARED = ("EnginePublication.Host.exe", "RizEngineOwnedTargets.dll", "RizToolbox.EngineNodeKernel.dll")


def require(value, message):
    if not value:
        raise ValueError(message)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def save(path, value):
    with path.open("x", encoding="utf-8", newline="\n") as stream:
        json.dump(value, stream, ensure_ascii=False, indent=2)
        stream.write("\n")


def norm(path):
    return str(Path(path).resolve()).replace("\\", "/").casefold()


def file_meta(path):
    data = path.read_bytes()
    return dict(path=str(path.resolve()), sha256=sha(data), byteSize=len(data))


class FILE_INFO(C.Structure):
    _fields_ = [("attributes", C.c_uint32), ("creation", C.c_uint32 * 2), ("access", C.c_uint32 * 2),
                ("write", C.c_uint32 * 2), ("volume", C.c_uint32), ("sizeHigh", C.c_uint32),
                ("sizeLow", C.c_uint32), ("links", C.c_uint32), ("indexHigh", C.c_uint32), ("indexLow", C.c_uint32)]


class HeldInputs:
    def __init__(self):
        require(os.name == "nt" and C.sizeof(C.c_void_p) == 8, "Fault matrix requires Windows x64 Python")
        self.kernel = C.WinDLL("kernel32", use_last_error=True)
        signatures = {
            "CreateFileW": (C.c_void_p, [C.c_wchar_p, C.c_uint32, C.c_uint32, C.c_void_p, C.c_uint32, C.c_uint32, C.c_void_p]),
            "ReadFile": (C.c_int32, [C.c_void_p, C.c_void_p, C.c_uint32, C.POINTER(C.c_uint32), C.c_void_p]),
            "SetFilePointerEx": (C.c_int32, [C.c_void_p, C.c_int64, C.c_void_p, C.c_uint32]),
            "GetFileInformationByHandle": (C.c_int32, [C.c_void_p, C.POINTER(FILE_INFO)]),
            "CloseHandle": (C.c_int32, [C.c_void_p]),
        }
        for name, (restype, argtypes) in signatures.items():
            function = getattr(self.kernel, name)
            function.restype, function.argtypes = restype, argtypes
        self.rows, self.lookup = [], {}

    @staticmethod
    def check(value, message):
        if not value:
            raise OSError(C.get_last_error(), message)

    def identity(self, handle):
        value = FILE_INFO()
        self.check(self.kernel.GetFileInformationByHandle(handle, C.byref(value)), "Cannot read actual held input identity")
        return dict(fileIndex=(value.indexHigh << 32) | value.indexLow, volumeSerial=value.volume,
                    bytes=(value.sizeHigh << 32) | value.sizeLow)

    def read(self, handle):
        self.check(self.kernel.SetFilePointerEx(handle, 0, None, 0), "Cannot rewind held input")
        buffer, parts, total = C.create_string_buffer(65536), [], 0
        while True:
            count = C.c_uint32()
            self.check(self.kernel.ReadFile(handle, buffer, len(buffer), C.byref(count), None), "Cannot read actual held input")
            if count.value == 0:
                break
            total += count.value
            require(total <= 128 * 1024 * 1024, "Fault matrix input exceeds fixed 128 MiB bound")
            parts.append(buffer.raw[:count.value])
        self.check(self.kernel.SetFilePointerEx(handle, 0, None, 0), "Cannot rewind held input")
        return b"".join(parts)

    def hold(self, path, expected=None, category="input"):
        path = Path(path).resolve()
        require(path.is_absolute() and path.is_file(), "Required exact fault input is missing: " + str(path))
        key = norm(path)
        if key in self.lookup:
            row = self.lookup[key]
            require(expected is None or row["sha256"] == expected, "Conflicting selected input hash")
            return row
        handle = self.kernel.CreateFileW(str(path), 0x80000000, 1, None, 3, 0x80, None)
        require(handle not in (None, C.c_void_p(-1).value), "Cannot hold read-only selected input: " + str(path))
        try:
            data = self.read(handle)
            row = dict(path=str(path), category=category, sha256=sha(data), byteSize=len(data), identity=self.identity(handle),
                       writeAndDeleteDenied=True, handle=handle, data=data)
            require(expected is None or row["sha256"] == expected, "Selected build input SHA256 changed: " + str(path))
            self.rows.append(row)
            self.lookup[key] = row
            return row
        except Exception:
            self.kernel.CloseHandle(handle)
            raise

    def public(self):
        return [{key: value for key, value in row.items() if key not in ("handle", "data")} for row in self.rows]

    def finish(self):
        for row in self.rows:
            try:
                row["afterSha256"] = sha(self.read(row["handle"]))
                row["afterIdentity"] = self.identity(row["handle"])
            except Exception as error:
                row["afterReadError"] = f"{type(error).__name__}: {error}"
            self.kernel.CloseHandle(row["handle"])
        return all(row.get("afterSha256") == row["sha256"] and row.get("afterIdentity") == row["identity"] for row in self.rows)


def artifact(directory, metadata, expected_size):
    name = metadata["path"]
    require(isinstance(name, str) and name and Path(name).name == name and not Path(name).is_absolute()
            and not any(character in name for character in (":", "/", "\\")), "Evidence raw reference leaves its exact case directory")
    path = directory / name
    data = path.read_bytes()
    require(len(data) == expected_size and sha(data) == metadata["sha256"], "Actual raw evidence hash or EOF differs")
    return data


def safe_observation(case, stage, index, process):
    require(process["actualExitCode"] == 1 and not process["timedOut"], "Safe fault did not return its expected actual exit 1")
    path = case / "case.json"
    host = read_json(path)
    require(host["kind"] == "engine-bootstrap-owned-host" and host["schemaVersion"] == 1
            and host["actualProcessId"] == process["actualPid"] and host["mainThreadId"] > 0 and host["sehCode"] == 0,
            "Safe fault does not identify the actual parent-started exception-free host")
    require(host["passed"] is False and host["finished"] is False and host["error"] == "Actual static first-install initialization failed"
            and host["failures"] and host["managedRequested"] is False,
            "Safe fault must use the intentional static initialization refusal path")
    snapshot = artifact(case, host["initialSnapshot"], 2162752)
    version, size, mask, count, checks, failures = struct.unpack_from("<4I2Q", snapshot)
    require((version, size, mask, count) == (1, 2162752, 0xf0, 16) and checks > 0 and failures > 0,
            "Safe fault lacks all sixteen actual early-phase observations")
    expected_failure = {1: 3, 8: 2, 2: 9, 3: 10, 4: 10}[stage]
    configured = (1 << index) - 1 if stage == 2 else 15 if stage in (3, 4) else 0
    bound = 15 if stage in (3, 4) else 0
    summaries = []
    for number in range(16):
        offset = 64 + number * 8448
        row = snapshot[offset:offset + 8448]
        head = struct.unpack_from("<16I", row)
        sequence, = struct.unpack_from("<Q", row, 64)
        require(head[:6] == (1, 8448, 4 + number // 4, number % 4, 0, host["mainThreadId"])
                and sequence == number + 1 and head[6] == 1 and head[8] == 0 and head[9] == 0,
                "Safe fault early call row is incomplete or contains an unexpected exception")
        for label, record_offset in (("before", 256), ("after", 4352)):
            data = row[record_offset:record_offset + 4096]
            header = struct.unpack_from("<4I", data)
            pid, = struct.unpack_from("<I", data, 28)
            configured_mask, bound_mask, committed_mask, writes, completed, outstanding = struct.unpack_from("<6I", data, 48)
            require(header == (4, 4096, 6, expected_failure) and pid == process["actualPid"]
                    and (configured_mask, bound_mask, committed_mask) == (configured, bound, 0)
                    and writes == completed == outstanding == 0,
                    "Safe fault actual V4 first-failure/configuration/no-write/protection facts differ")
            for kind in range(4):
                target = 512 + kind * 320
                source_attempts, source_bytes = struct.unpack_from("<2I", data, target + 88)
                protection_outstanding, = struct.unpack_from("<I", data, target + 120)
                require(source_attempts == source_bytes == protection_outstanding == 0, "Safe fault row reports a source write or unreturned protection")
            summaries.append(dict(sequence=sequence, capture=label, sha256=sha(data), state=header[2], failure=header[3],
                                  configuredMask=configured_mask, boundMask=bound_mask, sourceWriteAttempts=writes,
                                  sourceWriteCompletions=completed, protectionOutstanding=outstanding))
    require(not host["rows"] and host["rowsSnapshot"]["rowCount"] == 0, "Safe fault unexpectedly reached outside workload")
    return dict(kind="safe-no-source-write-refusal", expectedProcessBehaviorMatched=True, actualChildPid=process["actualPid"],
                initialSnapshot=file_meta(case / host["initialSnapshot"]["path"]), hostJson=file_meta(path),
                expectedFirstFailure=expected_failure, expectedConfiguredMask=configured, expectedBoundMask=bound, publicationObservations=summaries,
                independentPublicationVerificationPerformed=False)


def fatal_observation(case, process):
    require(process["actualExitCode"] == 0 and not process["timedOut"], "Fatal observer did not complete its actual capture contract")
    directory = case / "observation"
    result_path = directory / "observer-result.json"
    observed = read_json(result_path)
    require(observed["kind"] == "engine-bootstrap-failure-observation" and observed["observerPid"] == process["actualPid"]
            and observed["passed"] is True and observed["started"] is True and observed["actualChildPid"] > 0
            and observed["exitObserved"] is True and observed["fatalEventObserved"] is True
            and observed["actualExitCode"] in FAILFAST_CODES and observed["processHandleExitCode"] == observed["actualExitCode"]
            and observed["bootstrapLoadVerified"] is True and observed["snapshotCaptured"] is True
            and observed["allHeldInputsUnchanged"] is True and observed["timedOut"] is False,
            "Fatal behavior requires actual debug event, process handle exit, selected module and immutable held inputs")
    snapshots = [item for item in observed["snapshots"] if item.get("path") == observed["terminalSnapshot"]]
    require(len(snapshots) == 1, "Fatal observer omitted or duplicated its actual terminal snapshot")
    terminal = snapshots[0]
    artifact(directory, terminal, 4096)
    bodies = terminal.get("actualSourceBodies")
    require(bodies and bodies["complete"] is True and not bodies["error"] and len(bodies["functions"]) == 4,
            "Fatal event lacks the complete independently selected actual source-body captures")
    for kind, body in enumerate(bodies["functions"]):
        require(body["kind"] == kind and body["byteSize"] >= 32 and body["endRva"] - body["rva"] == body["byteSize"]
                and body["virtualMemorySegments"], "Fatal source-body layout/actual protection facts are incomplete")
        artifact(directory, body["disk"], body["byteSize"])
        artifact(directory, body["memory"], body["byteSize"])
    event_bytes = (directory / "debug-events.jsonl").read_bytes()
    require(sha(event_bytes) == observed["eventsSha256"], "Actual fatal debug event stream hash changed")
    events = [json.loads(line) for line in event_bytes.decode("utf-8").splitlines() if line]
    require(any(item.get("exceptionCode") in FAILFAST_CODES and item.get("actualChildPid") == observed["actualChildPid"] for item in events),
            "Fatal observer result has no corresponding actual fail-fast exception event")
    return dict(kind="fatal-publication-responsibility", expectedProcessBehaviorMatched=True, actualChildPid=observed["actualChildPid"],
                actualChildExitCode=observed["actualExitCode"], observerJson=file_meta(result_path), terminalSnapshot=terminal,
                independentPublicationVerificationPerformed=False)


def execute(command, directory, case):
    result = dict(command=command, workingDirectory=str(directory), started=False, actualPid=None, actualExitCode=None,
                  timedOut=False, timeoutSeconds=40, killOnlyOwnedSubprocess=True)
    start = time.monotonic()
    with (case / "process.stdout.log").open("xb") as stdout, (case / "process.stderr.log").open("xb") as stderr:
        try:
            process = subprocess.Popen(command, cwd=directory, stdout=stdout, stderr=stderr, shell=False,
                                       creationflags=subprocess.CREATE_NO_WINDOW)
        except Exception as error:
            result["error"] = f"{type(error).__name__}: {error}"
            result["elapsedSeconds"] = round(time.monotonic() - start, 6)
            return result
        result.update(started=True, actualPid=process.pid)
        try:
            result["actualExitCode"] = process.wait(timeout=40)
        except subprocess.TimeoutExpired:
            result["timedOut"] = True
            process.kill()
            try:
                result["actualExitCode"] = process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                result["cleanupWaitTimedOut"] = True
        result["elapsedSeconds"] = round(time.monotonic() - start, 6)
    return result


def run(args):
    require(args.build.is_absolute() and args.output.is_absolute(), "Build manifest and new output paths must be absolute")
    build, output = args.build.resolve(), args.output.resolve()
    require(build.name == "publication-build-manifest.json" and build.is_file(), "Select the exact completed publication build manifest")
    require(not output.exists(), "Preserve prior fault matrix evidence; choose a new output directory")
    manifest_data = build.read_bytes()
    manifest = json.loads(manifest_data.decode("utf-8-sig"))
    require(manifest["kind"] == "engine-publication-build" and manifest["schemaVersion"] == 1
            and manifest["fixtureExecuted"] is False and manifest["gameExecuted"] is False and manifest["installed"] is False,
            "Wrong completed unexecuted build manifest")
    fault_rows = [item for item in manifest["files"] if item["profile"].startswith("fault-")]
    require(len(fault_rows) == 26 and {(item["stage"], item["index"]) for item in fault_rows} == set(SELECTORS),
            "Completed build does not contain exactly the 26 fixed compiled fault selectors")
    profiles = {(item["stage"], item["index"]): item for item in fault_rows}
    shared = {Path(item["path"]).name: item for item in manifest["files"] if item["profile"] == "shared"}
    require(set(shared) == set(SHARED), "Completed build shared image set differs from the exact owned fixture")
    output.mkdir(parents=True)
    held = HeldInputs()
    results = []
    parent = dict(kind="engine-publication-fault-matrix-process-observation", schemaVersion=1, observerPid=os.getpid(), expectedCases=26,
                  safeCases=8, fatalCases=18, buildManifest=str(build), cases=results,
                  independentPublicationVerificationPerformed=False, expectedProcessBehaviorMatched=False,
                  gameExecuted=False, installed=False, productionAdmissionGranted=False, error=None)
    try:
        held.hold(build, sha(manifest_data), category="build-manifest")
        held.hold(Path(__file__).resolve(), category="parent-runner")
        observer = held.hold(OBSERVER, category="fixed-debug-observer")
        held.hold(Path(sys.executable).resolve(), category="python-runtime")
        for row in manifest.get("sources", []):
            held.hold(Path(row["path"]), row["sha256"], category="build-source")
        for field in ("generatedProfile", "generatedHeader"):
            row = manifest[field]
            held.hold(Path(row["path"]), row["sha256"], category=field)
        for row in list(shared.values()) + fault_rows:
            path = Path(row["path"]).resolve()
            require(path.is_relative_to(build.parent), "Selected compiled fault image leaves its exact build directory")
            if row["profile"].startswith("fault-"):
                require(path.name == "RizToolbox.EngineBootstrap.dll" and path.parent.name == row["profile"]
                        and row["profile"] == f"fault-{row['stage']}-{row['index']}", "Selected fault profile path/name/selector disagree")
                held.hold(path.parent / "bootstrap.map", category="selected-bootstrap-map")
            held.hold(path, row["sha256"], category="selected-native-image")
        parent["selectedObserverSha256"] = observer["sha256"]
        save(output / "inputs-before.json", dict(files=held.public()))
        for stage, index in SELECTORS:
            name = f"fault-{stage}-{index}"
            case = output / name
            case.mkdir()
            deployment = case / "deployment"
            deployment.mkdir()
            plan = dict(name=name, stage=stage, index=index, directory=str(case), deploymentDirectory=str(deployment),
                        fatalExpected=(stage, index) not in SAFE, actualExitCode=None, timedOut=False,
                        expectedBehavior="safe-exit-1" if (stage, index) in SAFE else "fatal-debug-observation",
                        expectedProcessBehaviorMatched=False, process=None, deployment=[], evidence=None, error=None)
            results.append(plan)
            try:
                selected = profiles[(stage, index)]
                rows = [shared[name] for name in SHARED] + [selected]
                paths = [Path(row["path"]).resolve() for row in rows]
                paths.append(Path(selected["path"]).resolve().parent / "bootstrap.map")
                for path in paths:
                    source = held.lookup[norm(path)]
                    target = deployment / path.name
                    with target.open("xb") as stream:
                        stream.write(source["data"])
                    copy = held.hold(target, source["sha256"], category="case-deployment")
                    plan["deployment"].append({key: value for key, value in copy.items() if key not in ("data", "handle")})
                save(case / "case-plan.json", {key: value for key, value in plan.items() if key not in ("process", "evidence", "error", "expectedProcessBehaviorMatched")})
                if (stage, index) in SAFE:
                    command = [str(deployment / "EnginePublication.Host.exe"), "--output", str(case / "case.json")]
                else:
                    command = [sys.executable, str(OBSERVER), "--host", str(deployment / "EnginePublication.Host.exe"),
                               "--bootstrap", str(deployment / "RizToolbox.EngineBootstrap.dll"), "--map", str(deployment / "bootstrap.map"),
                               "--output", str(case / "observation"), "--timeout", "30"]
                process = execute(command, deployment, case)
                plan["process"] = process
                plan["actualExitCode"] = process["actualExitCode"]
                plan["timedOut"] = process["timedOut"]
                save(case / "process.json", process)
                plan["evidence"] = safe_observation(case, stage, index, process) if (stage, index) in SAFE else fatal_observation(case, process)
                plan["expectedProcessBehaviorMatched"] = True
            except Exception as error:
                plan["error"] = f"{type(error).__name__}: {error}"
            finally:
                plan["artifactFiles"] = [file_meta(path) for path in sorted(case.rglob("*")) if path.is_file()]
                save(case / "case-result.json", plan)
            print(json.dumps(dict(case=name, expectedProcessBehaviorMatched=plan["expectedProcessBehaviorMatched"], error=plan["error"])), flush=True)
    except Exception as error:
        parent["error"] = f"{type(error).__name__}: {error}"
    finally:
        parent["allHeldInputsUnchanged"] = held.finish()
        parent["inputFiles"] = held.public()
        parent["completedCaseCount"] = len(results)
        parent["expectedProcessBehaviorMatched"] = not parent["error"] and len(results) == 26 and all(item["expectedProcessBehaviorMatched"] for item in results) and parent["allHeldInputsUnchanged"]
        save(output / "fault-matrix-result.json", parent)
    return parent


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--build", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        result = run(args)
        print(json.dumps(dict(expectedProcessBehaviorMatched=result["expectedProcessBehaviorMatched"], cases=result["completedCaseCount"], error=result["error"])))
        return 0 if result["expectedProcessBehaviorMatched"] else 1
    except Exception as error:
        print(json.dumps(dict(expectedProcessBehaviorMatched=False, error=f"{type(error).__name__}: {error}")), file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
