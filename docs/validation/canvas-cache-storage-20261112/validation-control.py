"""Build and exercise this source graph without loading or starting the game."""
import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
DOTNET = Path("C:/Program Files/dotnet/dotnet.exe")
PREVIOUS = ROOT / "docs/validation/canvas-position-writes-20261112"
SIX = ["RizToolbox." + n for n in ("Sdk", "Core", "Runtime", "Adapter", "Hosting", "Loader")]
TESTS = ["RizToolbox." + n + ".Tests" for n in ("Judgements", "Runtime", "AudioContent")]


def sha(path):
    return hashlib.file_digest(Path(path).open("rb"), "sha256").hexdigest()


def write(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def totals_for(name, content):
    pattern = r"(\d+)/(\d+) audio/content checks passed\." if name == "7-audio-content" else r"(\d+)/(\d+) passed"
    return re.findall(pattern, content)


def inputs():
    # Retain every previously accepted input and add all current source files,
    # including ignored new implementations. Generated bin/obj are excluded.
    paths = set(json.loads((PREVIOUS / "inputs-before.json").read_text(encoding="utf-8")))
    for base, names in (("src", SIX), ("tests", TESTS)):
        for name in names:
            for folder, dirs, files in os.walk(ROOT / base / name):
                dirs[:] = [d for d in dirs if d not in ("bin", "obj", "artifacts", ".git")]
                for file in files:
                    p = Path(folder) / file
                    if p.suffix in (".cs", ".csproj", ".props", ".targets", ".json"):
                        paths.add(p.relative_to(ROOT).as_posix())
    return {p: sha(ROOT / p) for p in sorted(paths)}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", required=True)
    parser.add_argument("--cache-only", dest="canvas_only", action="store_true")
    parser.add_argument("--cache-tail-only", action="store_true")
    parser.add_argument("--complete-parser-rejected-run", type=Path)
    opt = parser.parse_args()
    out = Path(opt.output).resolve()
    out.mkdir(parents=True, exist_ok=False)  # Never overwrite failed/old evidence.
    if opt.canvas_only and opt.complete_parser_rejected_run:
        raise RuntimeError("A parser recovery must complete the full source graph.")
    previous_run = opt.complete_parser_rejected_run.resolve() if opt.complete_parser_rejected_run else None
    artifacts = (previous_run or out) / "artifacts"
    started = dt.datetime.now(dt.timezone.utc).isoformat()
    report = {"scope": "Actual Canvas cache storage: six Release components; current Judgements 300; Runtime ABI 51 and exact static read 6; complete default AudioContent 1001",
              "startedUtc": started, "steps": [], "passed": False,
              "gameStarted": False, "installed": False, "oldZipChanged": False,
              "sourceReviewComplete": False}
    report["artifactsDirectory"] = str(artifacts)
    report["validationControlSha256"] = sha(__file__)
    report["childTimeoutSeconds"] = 2700
    shutil.copyfile(__file__, out / "validation-control.py")
    if opt.canvas_only:
        report["scope"] = "Canvas cache iteration only: current Judgements build and explicit 16-group selector; not the full acceptance matrix"
    if opt.cache_tail_only:
        report["scope"] = "Canvas cache tail iteration only: current Judgements build and eight fault/lifecycle groups; not the full acceptance matrix"
    report["controllerPid"] = os.getpid()
    before = inputs()
    write(out / "inputs-before.json", before)
    env = os.environ.copy()
    env["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
    env["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1"
    fixtures = json.loads((PREVIOUS / "build-test-result.json").read_text(encoding="utf-8"))["nativeFixtures"]
    for key, item in fixtures.items():
        actual = sha(item["path"])
        if actual != item["sha256"]:
            raise RuntimeError("Own native fixture changed: " + key)
        env[key] = item["path"]  # Child processes only; never permanent environment.
    report["nativeFixtures"] = fixtures

    def step(name, args, expected=None):
        print("START " + name, flush=True)
        log = out / (name + ".log")
        began = dt.datetime.now(dt.timezone.utc).isoformat()
        with log.open("wb") as stream:
            try:
                child = subprocess.Popen([str(x) for x in args], cwd=ROOT, env=env,
                                       stdin=subprocess.DEVNULL, stdout=stream,
                                       stderr=subprocess.STDOUT)
                report["activeChild"] = {"name": name, "pid": child.pid, "startedUtc": began, "log": str(log)}
                write(out / "result.json", report)
                code = child.wait(timeout=2700)
                timeout = False
            except subprocess.TimeoutExpired:
                child.kill()
                child.wait()
                code = None
                timeout = True
        content = log.read_text(encoding="utf-8", errors="replace")
        totals = totals_for(name, content)
        good = code == 0 and not timeout and (expected is None or totals == [(str(expected), str(expected))])
        item = {"name": name, "arguments": [str(x) for x in args], "startedUtc": began,
                "finishedUtc": dt.datetime.now(dt.timezone.utc).isoformat(),
                "exitCode": code, "timedOut": timeout, "totals": totals,
                "matchesExpected": good, "log": str(log), "logSha256": sha(log)}
        if name == "5-judgements":
            cases = re.findall(r"^PASS (Canvas caches .+)$", content, re.MULTILINE)
            item["canvasGroups"] = cases
            count = 8 if opt.cache_tail_only else 16
            item["canvasGroupsUnique"] = len(cases) == count and len(set(cases)) == count
            good = good and item["canvasGroupsUnique"]
            item["matchesExpected"] = good
        report["steps"].append(item)
        report.pop("activeChild", None)
        write(out / "result.json", report)
        print("END " + name + " exit=" + str(code) + " expected=" + str(good), flush=True)
        if not good:
            raise RuntimeError("Validation failed; original log preserved: " + str(log))

    def build(name, project):
        step(name, [DOTNET, "build", project, "-c", "Release", "--artifacts-path", artifacts,
                    "--verbosity", "minimal", "--disable-build-servers", "-m:1",
                    "-p:UseSharedCompilation=false", "-p:NuGetAudit=false"])

    def exercise():
        if opt.canvas_only or opt.cache_tail_only:
            build("2-build-RizToolbox.Judgements.Tests", ROOT / "tests" / TESTS[0] / (TESTS[0] + ".csproj"))
            selector = "--canvas-cache-tail-checks" if opt.cache_tail_only else "--canvas-cache-checks"
            step("5-judgements", [DOTNET, artifacts / "bin" / TESTS[0] / "release" / (TESTS[0] + ".dll"), selector], 8 if opt.cache_tail_only else 16)
            return
        def dll(test):
            return artifacts / "bin" / test / "release" / (test + ".dll")
        if previous_run:
            # Preserve the original controller failure. These seven children
            # already exited naturally with 0 against precisely this source.
            old = json.loads((previous_run / "result.json").read_text(encoding="utf-8"))
            old_before = json.loads((previous_run / "inputs-before.json").read_text(encoding="utf-8"))
            old_after = json.loads((previous_run / "inputs-after.json").read_text(encoding="utf-8"))
            if old_before != before or old_after != before or not old.get("inputsUnchanged"):
                raise RuntimeError("Cannot recover a changed source graph.")
            if len(old["steps"]) != 7 or old["steps"][-1]["name"] != "7-audio-content":
                raise RuntimeError("Recovery is restricted to the seven-step AudioContent parser rejection.")
            expected_names = ["1-six"] + [str(n) + "-build-" + test for n, test in enumerate(TESTS, 2)] + ["5-judgements", "6-renderer-abi", "7-audio-content"]
            if [x["name"] for x in old["steps"]] != expected_names:
                raise RuntimeError("The original child sequence differs from the expected graph.")
            for previous in old["steps"]:
                name = previous["name"]
                log = Path(previous["log"])
                if previous["exitCode"] != 0 or previous.get("timedOut") or sha(log) != previous["logSha256"]:
                    raise RuntimeError("Original child exit/log is not accepted: " + name)
                content = log.read_text(encoding="utf-8", errors="replace")
                totals = totals_for(name, content)
                expected = {"5-judgements": 300, "6-renderer-abi": 51, "7-audio-content": 1001}.get(name)
                if expected is not None and totals != [(str(expected), str(expected))]:
                    raise RuntimeError("Original actual test total differs: " + name)
                item = dict(previous)
                item["originalMatchesExpected"] = previous["matchesExpected"]
                item["originalTotals"] = previous["totals"]
                item["matchesExpected"] = True
                item["totals"] = totals
                if name == "5-judgements" and not item.get("canvasGroupsUnique"):
                    raise RuntimeError("The original 16 Canvas groups were not independently identified.")
                report["steps"].append(item)
                print("RETAIN actual child " + name + " exit=0", flush=True)
            report["parserRecovery"] = {"originalRun": str(previous_run), "originalResultSha256": sha(previous_run / "result.json"),
                                       "reason": "AudioContent reports N/N audio/content checks passed.; the first controller pattern accepted only N/N passed.",
                                       "originalReportUnmodified": True, "passedChildrenRepeated": False,
                                       "originalSourcesAndCurrentSourcesIdentical": True}
        else:
            build("1-six", ROOT / "src/RizToolbox.Loader/RizToolbox.Loader.csproj")
            for n, test in enumerate(TESTS, 2):
                build(str(n) + "-build-" + test, ROOT / "tests" / test / (test + ".csproj"))
            step("5-judgements", [DOTNET, dll(TESTS[0])], 300)
            step("6-renderer-abi", [DOTNET, dll(TESTS[1]), "--renderer-abi-checks"], 51)
            step("7-audio-content", [DOTNET, dll(TESTS[2])], 1001)
        step("8-exact-static-read", [DOTNET, dll(TESTS[1]), "--exact-static-field-read-checks"], 6)
        products = {}
        for component in SIX:
            products[component] = {ext: sha(artifacts / "bin" / component / "release" / (component + ext))
                                   for ext in (".dll", ".pdb")}
        report["products"] = products
        copies = []
        for test in TESTS:
            for component in SIX[:4]:
                for ext in (".dll", ".pdb"):
                    actual = sha(artifacts / "bin" / test / "release" / (component + ext))
                    copies.append({"test": test, "component": component, "extension": ext,
                                   "sha256": actual, "matches": actual == products[component][ext]})
        report["dependencyCopies"] = copies
        report["dependenciesMatch"] = len(copies) == 24 and all(x["matches"] for x in copies)
        if not report["dependenciesMatch"]:
            raise RuntimeError("Current test dependencies differ from current six Release products.")
    try:
        exercise()
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
        print(json.dumps({k: report.get(k) for k in ("passed", "inputCount", "inputsUnchanged", "failure")}), flush=True)
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    sys.exit(main())
