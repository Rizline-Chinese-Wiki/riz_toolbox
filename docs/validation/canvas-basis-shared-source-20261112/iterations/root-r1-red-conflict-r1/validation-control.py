"""Fresh, bounded offline validation of the one-source Canvas basis graph."""
import argparse
import ctypes
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[2]
DOTNET = Path("C:/Program Files/dotnet/dotnet.exe")
PREVIOUS = ROOT / "docs/validation/canvas-cache-storage-20261112"
SIX = ["RizToolbox." + name for name in ("Sdk", "Core", "Runtime", "Adapter", "Hosting", "Loader")]
TESTS = ["RizToolbox." + name + ".Tests" for name in ("Judgements", "Runtime", "AudioContent")]


def sha(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def write(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def inputs():
    paths = set(json.loads((PREVIOUS / "inputs-before.json").read_text(encoding="utf-8")))
    paths.update(("RizToolbox.sln", "Directory.Build.props", "global.json"))
    for base, names in (("src", SIX), ("tests", TESTS)):
        for name in names:
            for folder, dirs, files in os.walk(ROOT / base / name):
                dirs[:] = [d for d in dirs if d not in ("bin", "obj", "artifacts", ".git")]
                for file in files:
                    path = Path(folder) / file
                    if path.suffix in (".cs", ".csproj", ".props", ".targets", ".json"):
                        paths.add(path.relative_to(ROOT).as_posix())
    return {name: sha(ROOT / name) for name in sorted(paths)}


class MemoryStatus(ctypes.Structure):
    _fields_ = [("length", ctypes.c_ulong), ("load", ctypes.c_ulong),
                ("totalPhysical", ctypes.c_ulonglong), ("availablePhysical", ctypes.c_ulonglong),
                ("totalPageFile", ctypes.c_ulonglong), ("availablePageFile", ctypes.c_ulonglong),
                ("totalVirtual", ctypes.c_ulonglong), ("availableVirtual", ctypes.c_ulonglong),
                ("availableExtendedVirtual", ctypes.c_ulonglong)]


def available_commit():
    status = MemoryStatus()
    status.length = ctypes.sizeof(status)
    if not ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(status)):
        raise ctypes.WinError()
    return status.availablePageFile


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--basis-only", action="store_true")
    parser.add_argument("--basis-conflict-only", action="store_true")
    opt = parser.parse_args()
    out = opt.output.resolve()
    out.mkdir(parents=True, exist_ok=False)
    artifacts = out / "artifacts"
    report = {"scope": "Same-source Canvas cache/position basis; fresh current graph, no game execution",
              "fullMatrixRequested": not (opt.basis_only or opt.basis_conflict_only), "controllerPid": os.getpid(),
              "startedUtc": dt.datetime.now(dt.timezone.utc).isoformat(), "steps": [], "passed": False,
              "sourceReviewComplete": False, "gameStarted": False, "installed": False,
              "completeSeekPermitted": False, "oldZipChanged": False,
              "artifactsDirectory": str(artifacts), "childTimeoutSeconds": 2700,
              "minimumAvailableCommitBytes": 512 * 1024 * 1024,
              "judgementsExecutionPartition": [16, 300], "default316SingleProcessExecuted": False,
              "validationControlSha256": sha(__file__)}
    shutil.copyfile(__file__, out / "validation-control.py")
    before = inputs()
    write(out / "inputs-before.json", before)
    env = os.environ.copy()
    env["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
    env["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1"
    fixtures = json.loads((PREVIOUS / "build-test-result.json").read_text(encoding="utf-8"))["nativeFixtures"]
    for key, item in fixtures.items():
        if sha(item["path"]) != item["sha256"]:
            raise RuntimeError("Own native fixture changed: " + key)
        env[key] = item["path"]
    report["nativeFixtures"] = fixtures

    def step(name, arguments, expected=None):
        print("START " + name, flush=True)
        log = out / (name + ".log")
        began = dt.datetime.now(dt.timezone.utc).isoformat()
        timeout = False
        resource_stop = None
        with log.open("wb") as stream:
            child = subprocess.Popen([str(arg) for arg in arguments], cwd=ROOT, env=env,
                                     stdin=subprocess.DEVNULL, stdout=stream, stderr=subprocess.STDOUT)
            report["activeChild"] = {"name": name, "pid": child.pid, "startedUtc": began, "log": str(log)}
            write(out / "result.json", report)
            deadline = time.monotonic() + report["childTimeoutSeconds"]
            while child.poll() is None:
                try:
                    child.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    remaining = available_commit()
                    if remaining < report["minimumAvailableCommitBytes"]:
                        resource_stop = {"reason": "Less than 512 MiB of system commit remains.",
                                         "availableCommitBytes": remaining, "pid": child.pid,
                                         "arguments": [str(arg) for arg in arguments]}
                        child.kill()
                        child.wait()
                        break
                    if time.monotonic() >= deadline:
                        timeout = True
                        child.kill()
                        child.wait()
                        break
            code = child.returncode
        content = log.read_text(encoding="utf-8", errors="replace")
        pattern = r"(\d+)/(\d+) audio/content checks passed\." if name == "9-audio-content" else r"(\d+)/(\d+) passed"
        totals = re.findall(pattern, content)
        good = code == 0 and not timeout and resource_stop is None and (
            expected is None or totals == [(str(expected), str(expected))])
        item = {"name": name, "arguments": [str(arg) for arg in arguments], "startedUtc": began,
                "finishedUtc": dt.datetime.now(dt.timezone.utc).isoformat(), "exitCode": code,
                "timedOut": timeout, "resourceStop": resource_stop, "totals": totals,
                "log": str(log), "logSha256": sha(log)}
        if name == "6-canvas-basis":
            groups = re.findall(r"^PASS (Canvas basis .+)$", content, re.MULTILINE)
            item["basisGroups"] = groups
            group_count = 1 if opt.basis_conflict_only else 16
            item["basisGroupsUnique"] = len(groups) == group_count and len(set(groups)) == group_count
            good = good and item["basisGroupsUnique"]
        if name == "7-judgements-without-basis":
            item["containsBasisGroups"] = bool(re.search(r"^PASS Canvas basis ", content, re.MULTILINE))
            good = good and not item["containsBasisGroups"]
        item["matchesExpected"] = good
        report["steps"].append(item)
        report.pop("activeChild", None)
        write(out / "result.json", report)
        print("END " + name + " exit=" + str(code) + " expected=" + str(good), flush=True)
        if not good:
            raise RuntimeError("Validation failed; original child/log preserved: " + str(log))

    def build(name, project):
        step(name, [DOTNET, "build", project, "-c", "Release", "--artifacts-path", artifacts,
                    "--verbosity", "minimal", "--disable-build-servers", "-m:1",
                    "-p:UseSharedCompilation=false", "-p:NuGetAudit=false"])

    def dll(test):
        return artifacts / "bin" / test / "release" / (test + ".dll")

    try:
        if opt.basis_only or opt.basis_conflict_only:
            build("2-build-Judgements", ROOT / "tests" / TESTS[0] / (TESTS[0] + ".csproj"))
            selector = "--canvas-basis-conflict-checks" if opt.basis_conflict_only else "--canvas-basis-checks"
            step("6-canvas-basis", [DOTNET, dll(TESTS[0]), selector], 1 if opt.basis_conflict_only else 16)
        else:
            build("1-six", ROOT / "src/RizToolbox.Loader/RizToolbox.Loader.csproj")
            for index, test in enumerate(TESTS, 2):
                build(str(index) + "-build-" + test.removeprefix("RizToolbox."), ROOT / "tests" / test / (test + ".csproj"))
            build("5-solution", ROOT / "RizToolbox.sln")
            step("6-canvas-basis", [DOTNET, dll(TESTS[0]), "--canvas-basis-checks"], 16)
            step("7-judgements-without-basis", [DOTNET, dll(TESTS[0]), "--without-canvas-basis-checks"], 300)
            step("8-renderer-abi", [DOTNET, dll(TESTS[1]), "--renderer-abi-checks"], 51)
            step("9-audio-content", [DOTNET, dll(TESTS[2])], 1001)
            step("10-exact-static-read", [DOTNET, dll(TESTS[1]), "--exact-static-field-read-checks"], 6)
            report["products"] = {
                component: {ext: sha(artifacts / "bin" / component / "release" / (component + ext))
                            for ext in (".dll", ".pdb")} for component in SIX}
            report["dependencyCopies"] = [
                {"test": test, "component": component, "extension": ext,
                 "sha256": sha(artifacts / "bin" / test / "release" / (component + ext)),
                 "matches": sha(artifacts / "bin" / test / "release" / (component + ext)) == report["products"][component][ext]}
                for test in TESTS for component in SIX[:4] for ext in (".dll", ".pdb")]
            report["dependenciesMatch"] = len(report["dependencyCopies"]) == 24 and all(
                item["matches"] for item in report["dependencyCopies"])
            if not report["dependenciesMatch"]:
                raise RuntimeError("The current test dependencies do not match the current six Release products.")
        report["passed"] = True
    except Exception as error:
        report["failure"] = str(error)
    finally:
        after = inputs()
        write(out / "inputs-after.json", after)
        report["inputCount"] = len(before)
        report["inputsUnchanged"] = before == after
        report["passed"] = report["passed"] and report["inputsUnchanged"]
        report["finishedUtc"] = dt.datetime.now(dt.timezone.utc).isoformat()
        write(out / "result.json", report)
        print(json.dumps({key: report.get(key) for key in ("passed", "inputCount", "inputsUnchanged", "failure")}), flush=True)
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    sys.exit(main())
