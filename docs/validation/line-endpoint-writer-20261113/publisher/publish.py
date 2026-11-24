"""Audit and atomically archive the completed 891-input endpoint-writer scope.

Publication requires an independently supplied SHA-256 of the original
controller handle-exit receipt. This script does no build or test execution.
--check-only reads the archive independently of current workspace files.
"""
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import uuid


HERE = Path(__file__).resolve().parent
ROOT = Path("C:/Users/71957/Desktop/riz_toolbox").resolve()
INTEGRATION = ROOT / "work/line-endpoint-writer-integration-20261113"
REVIEW = ROOT / "work/line-endpoint-writer-review-20261113"
RUN = Path("D:/riz-build-cache-next/line-endpoint-writer-integration-20261113/main-r1")
CANDIDATE_RESULT = Path("D:/riz-build-cache-next/line-endpoint-writer-candidate-20261113/root-r4/result.json")
ARCHIVE = ROOT / "docs/validation/line-endpoint-writer-20261113"
PRIOR = ROOT / "docs/validation/line-ring-target-provider-20261113"
RECEIPT = INTEGRATION / "controller-handle-exit.json"
OBSERVER = INTEGRATION / "observe.py"
HELPER = ROOT / "work/line-ring-source-validation-20261113/run_validation.py"
OFFLINE = ROOT / "work/ring-x-numeric-20261113/NuGet.Config"
FIXTURE_RECORD = ROOT / "docs/validation/canvas-basis-shared-source-20261112/build-test-result.json"
DOTNET = Path("C:/Program Files/dotnet/dotnet.exe")
JUDGEMENTS = "RizToolbox.Judgements.Tests"
RUNTIME_TEST = "RizToolbox.Runtime.Tests"
SIX = ["RizToolbox." + x for x in ("Sdk", "Core", "Runtime", "Adapter", "Hosting", "Loader")]
SHA_RE = re.compile(r"[0-9a-f]{64}\Z")
MAX_FILES = 1500
MAX_BYTES = 256 * 1024 * 1024
MAX_FILE_BYTES = 64 * 1024 * 1024

CONTROLLER_PID = 29728
CONTROLLER_SHA = "c3bb85ee455bdf2b0eb5cfae1dbef6314e78596a0b4e29df18c0e3680a222984"
OBSERVER_SHA = "fb53892f1250f77ba267da0437ff2fe0d21f1b4087f3fde6c4aafe7f94000fee"
HELPER_SHA = "61c2983ae117322b25ff96ad75b659b870fdadf9aada9274727248cd5ca98fd4"
OFFLINE_SHA = "964302159308872b9729e1828a1ba897f30634f7bd751cde11ea8e6fed8cfb97"
BUILD_SHA = "19624697b8976c2d55c7be8971dbaa97a3dd2c67e2693164fba943f6d2b1e62b"
APPLY_SHA = "cd59e5d29f2a41a40332e40e4f2ea41d30cf69fe716d3831d38f888316648afb"
PAYLOAD_MANIFEST_SHA = "bf10a76f416e1fcf5d0437a8b6ea8e27036d77f5ae2d9951667ddfca5b23f5a7"
PAYLOAD_PATCH_SHA = "405103c5cb2b94d311c91cc642813dbf568d0a8f6713fda8987d2925b0c1ff65"
APPLICATION_SHA = "d1467efefa07c4c9d1dd91cf8334907c92848e582011ab5b3880efea8cc6fac7"
REVIEW_JSON_SHA = "ded244da1ed056fff50c08dfaacf1bad757204f9465db4c09b432db07426ed92"
REVIEW_MD_SHA = "eba82cccd9a40830294462d0d4231ef44775bf8a024e4b04486653796bf824dc"
CANDIDATE_RESULT_SHA = "e43ceeea7874dd67f20373e8c5bf8da981176a5b872a4842dacb1be28293dcca"
PRIOR_SUMMARY_SHA = "2ae690323504cb557142aef81618b1f9a1d10a3de9e15e8e9be40053cee22663"
PRIOR_MANIFEST_SHA = "163424a26507ac0cbd2b9c82b169cbc7c27dd2a74de34be3b7098b421b031b52"
FIXTURE_RECORD_SHA = "79776e6285697456b92d34689f802cfca9d59294cb871fb765eac59a1eac76d2"
GOLDEN_SHA = "1bf6d52a1e1aacc01f2c0e92c951ed860f0137164860476888e473c7cea13a48"

CONTROLS = {
    str(INTEGRATION / "validate_main.py"): ("controls/validate_main.py", CONTROLLER_SHA),
    str(INTEGRATION / "apply.py"): ("controls/apply.py", APPLY_SHA),
    str(INTEGRATION / "payload-manifest.json"): ("controls/payload-manifest.json", PAYLOAD_MANIFEST_SHA),
    str(INTEGRATION / "payload.patch"): ("controls/payload.patch", PAYLOAD_PATCH_SHA),
    str(INTEGRATION / "application-record.json"): ("controls/application-record.json", APPLICATION_SHA),
    str(ROOT / "scripts/Build.ps1"): ("controls/Build.ps1", BUILD_SHA),
}
STEPS = ["six-release", "solution", "legacy-listing", "endpoint-writer", "target-provider",
         "chart-occurrence", "endpoint-local", "numeric", "ring-x", "invalid-writer-name",
         "invalid-writer-arity", "source-13", "source-14", "source-17", "source-18",
         "source-19", "basis-01", "renderer-abi"]
SOURCE_INDEXES = (13, 14, 17, 18, 19)
CASES = {
    "endpoint-writer": ("tests/RizToolbox.Judgements.Tests/LineRingSource/NativeSeekLineRingSourceChecks.EndpointWriter.cs", "[] EndpointWriterCases =", 17),
    "target-provider": ("tests/RizToolbox.Judgements.Tests/LineRingSource/NativeSeekLineRingSourceChecks.TargetProvider.cs", "[] TargetProviderCases =", 6),
    "chart-occurrence": ("tests/RizToolbox.Judgements.Tests/LineRingSource/NativeSeekLineChartOccurrenceChecks.cs", "[] Cases =", 5),
    "endpoint-local": ("tests/RizToolbox.Judgements.Tests/LineRingNumeric/NativeSeekLineEndpointLocalChecks.cs", "[] Cases =", 6),
    "numeric": ("tests/RizToolbox.Judgements.Tests/LineRingNumeric/NativeSeekLineRingNumericChecks.cs", "[] Cases =", 13),
    "ring-x": ("tests/RizToolbox.Judgements.Tests/LineRingNumeric/NativeSeekLineRingXNumericChecks.cs", "[] Cases =", 7),
    "source": ("tests/RizToolbox.Judgements.Tests/LineRingSource/NativeSeekLineRingSourceChecks.cs", "[] Cases =", 19),
    "basis": ("tests/RizToolbox.Judgements.Tests/CanvasBasis/NativeSeekCanvasBasisChecks.cs", "[] Cases =", 16),
}
RUNTIME_CASES = (
    ("VoidPublicationRegression", 11), ("ExactFieldWriteRegression", 20),
    ("GuardedFieldPublicationRegression", 5), ("RendererQuaternionRegression", 8),
    ("TweenGraphReadRegression", 7),
)


def require(ok: bool, why: str) -> None:
    if not ok:
        raise RuntimeError(why)


def safe_name(name: str) -> str:
    require(isinstance(name, str) and name and "\\" not in name and not name.startswith("/"), "Unsafe archive name")
    require(all(part not in ("", ".", "..") and ":" not in part for part in name.split("/")),
            "Unsafe archive name: " + name)
    return name


def safe_file(path: Path) -> Path:
    path = Path(path)
    require(path.is_file() and not path.is_symlink(), "Missing or linked evidence file: " + str(path))
    require(path.stat().st_size <= MAX_FILE_BYTES, "Oversized evidence file: " + str(path))
    return path


def sha(path: Path) -> str:
    with safe_file(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def unique_json_keys(pairs):
    output = {}
    for key, value in pairs:
        require(key not in output, "Duplicate JSON key: " + key)
        output[key] = value
    return output


def parse_json(text: str):
    return json.loads(text, object_pairs_hook=unique_json_keys)


def read(path: Path):
    return parse_json(safe_file(path).read_text(encoding="utf-8-sig"))


def encoded(value) -> bytes:
    return (json.dumps(value, ensure_ascii=False, indent=2) + "\n").encode("utf-8")


def stamp(value: str) -> dt.datetime:
    result = dt.datetime.fromisoformat(value)
    require(result.tzinfo is not None, "Unzoned timestamp")
    return result.astimezone(dt.timezone.utc)


def check_receipt(path: Path, digest: str, result_path: Path, result: dict) -> None:
    require(SHA_RE.fullmatch(digest) is not None and sha(path) == digest, "Original handle receipt SHA differs")
    receipt = read(path)
    require(set(receipt) == {"schema", "controllerPid", "createdUtc", "observedAliveUtc",
            "controllerScriptSha256", "observerSha256", "handleKeptUntilExit", "sdkGoalComplete",
            "completeSeekPermitted", "actualExitCode", "finishedUtc", "resultSha256AtExit",
            "controllerScriptUnchanged"}, "Original handle receipt schema differs")
    require(receipt["schema"] == "original-controller-handle-exit/v1"
            and receipt["controllerPid"] == CONTROLLER_PID
            and receipt["controllerScriptSha256"] == CONTROLLER_SHA
            and receipt["observerSha256"] == OBSERVER_SHA
            and receipt["handleKeptUntilExit"] is True
            and receipt["controllerScriptUnchanged"] is True
            and receipt["actualExitCode"] == 0
            and receipt["sdkGoalComplete"] is False
            and receipt["completeSeekPermitted"] is False
            and receipt["resultSha256AtExit"] == sha(result_path),
            "Original controller actual exit/result does not certify this run")
    created, alive, finished = map(stamp, (receipt["createdUtc"], receipt["observedAliveUtc"], receipt["finishedUtc"]))
    started, result_finished = map(stamp, (result["startedUtc"], result["finishedUtc"]))
    require(created <= started <= alive <= finished and result_finished <= finished
            and (started - created).total_seconds() < 20, "Original handle chronology differs")


def case_names(path: Path, marker: str, count: int) -> list[str]:
    body = safe_file(path).read_text(encoding="utf-8")
    require(body.count(marker) == 1, "Ambiguous registry array: " + str(path))
    block = body.split(marker, 1)[1].split("};", 1)[0]
    names = re.findall(r'(?m)^\s*\("([^"\r\n]+)",', block)
    require(len(names) == count and len(set(names)) == count, "Registry case count/name differs: " + str(path))
    return names


def build_argv(name: str) -> list[str]:
    project = ROOT / ("src/RizToolbox.Loader/RizToolbox.Loader.csproj" if name == "six-release" else "RizToolbox.sln")
    return [str(DOTNET), "build", str(project), "-c", "Release", "--artifacts-path", str(RUN / "artifacts"),
            "--verbosity", "minimal", "--disable-build-servers", "-m:1", "-p:UseSharedCompilation=false",
            "-p:NuGetAudit=false", "-p:RestoreConfigFile=" + str(OFFLINE)]


def step_spec(name: str) -> tuple[list[str], int, int | None, str | None, bool]:
    test = RUN / "artifacts/bin" / JUDGEMENTS / "release" / (JUDGEMENTS + ".dll")
    prefix = [str(DOTNET), str(test)]
    if name in ("six-release", "solution"):
        return build_argv(name), 0, None, None, False
    if name == "legacy-listing":
        return prefix + ["--list-legacy-cases"], 0, None, None, False
    grouped = {
        "endpoint-writer": ("--line-ring-endpoint-writer-checks", 17, "Line ring source endpoint writer "),
        "target-provider": ("--line-ring-target-provider-checks", 6, "Line ring source target provider "),
        "chart-occurrence": ("--line-chart-occurrence-checks", 5, "Line chart occurrence "),
        "endpoint-local": ("--line-ring-endpoint-local-checks", 6, "Line ring numeric endpoint "),
        "numeric": ("--line-ring-numeric-checks", 13, "Line ring numeric "),
        "ring-x": ("--line-ring-ring-x-numeric-checks", 7, "Line ring numeric ring "),
    }
    if name in grouped:
        selector, count, label = grouped[name]
        return prefix + [selector], 0, count, label, True
    invalid = {"invalid-writer-name": ["--line-ring-endpoint-writer-check"],
               "invalid-writer-arity": ["--line-ring-endpoint-writer-checks", "extra"]}
    if name in invalid:
        return prefix + invalid[name], 2, None, None, False
    if name.startswith("source-") and int(name[7:]) in SOURCE_INDEXES:
        return prefix + ["--line-ring-source-case", str(int(name[7:]))], 0, 1, "Line ring source ", True
    if name == "basis-01":
        return prefix + ["--canvas-basis-case", "1"], 0, 1, "Canvas basis ", True
    if name == "renderer-abi":
        runtime_test = RUN / "artifacts/bin" / RUNTIME_TEST / "release" / (RUNTIME_TEST + ".dll")
        return [str(DOTNET), str(runtime_test), "--renderer-abi-checks"], 0, 51, None, False
    raise RuntimeError("Unexpected step: " + name)


class View:
    """Resolve either original run files or self-contained archived copies."""
    def __init__(self, base: Path, archived: bool):
        self.base = base
        self.archived = archived

    def input(self, name: str) -> Path:
        return self.base / "compiler-inputs" / safe_name(name) if self.archived else ROOT / safe_name(name)

    def control(self, original: str) -> Path:
        return self.base / CONTROLS[original][0] if self.archived else Path(original)

    def extra(self, name: str, original: Path) -> Path:
        return self.base / safe_name(name) if self.archived else original

    def run(self, name: str) -> Path:
        return self.base / "run" / safe_name(name) if self.archived else self.base / safe_name(name)

    def log(self, name: str, stream: str) -> Path:
        filename = name + "." + stream + ".log"
        return self.base / "run/logs" / filename if self.archived else self.base / filename

    def prior(self, name: str) -> Path:
        return self.base / "prior-885" / safe_name(name) if self.archived else PRIOR / safe_name(name)

    def production(self, component: str, ext: str) -> Path:
        return (self.base / "binaries/production" / (component + ext) if self.archived
                else self.base / "artifacts/bin" / component / "release" / (component + ext))

    def test(self, component: str, ext: str) -> Path:
        return (self.base / "binaries/test" / component / (component + ext) if self.archived
                else self.base / "artifacts/bin" / component / "release" / (component + ext))

    def dependency(self, test_component: str, component: str, ext: str) -> Path:
        return (self.base / "binaries/dependencies" / test_component / (component + ext) if self.archived
                else self.base / "artifacts/bin" / test_component / "release" / (component + ext))

    def fixture(self, key: str, original: str) -> Path:
        return (self.base / "fixtures/native" / key / Path(original).name if self.archived
                else Path(original))

    def golden(self) -> Path:
        return (self.base / "fixtures/line-ring-numeric-goldens.json" if self.archived
                else self.base / "artifacts/bin" / JUDGEMENTS / "release/line-ring-numeric-goldens.json")


def load_scanner():
    require(sha(HELPER) == HELPER_SHA, "Compiler input scanner changed")
    spec = importlib.util.spec_from_file_location("writer_evidence_input_scanner", HELPER)
    require(spec is not None and spec.loader is not None, "Compiler input scanner unavailable")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def check_prior(view: View) -> tuple[dict[str, str], list[dict]]:
    summary_path = view.prior("validation-summary.json")
    manifest_path = view.prior("artifact-manifest.json")
    names_path = view.prior("legacy-case-list.json") if view.archived else view.prior("run/legacy-case-list.json")
    inputs_path = view.prior("inputs-after.json") if view.archived else view.prior("run/inputs-after.json")
    require(sha(summary_path) == PRIOR_SUMMARY_SHA and sha(manifest_path) == PRIOR_MANIFEST_SHA,
            "Accepted 885 archive pin differs")
    summary, manifest, names, inputs = map(read, (summary_path, manifest_path, names_path, inputs_path))
    require(summary["acceptedThisBatch"] is True and summary["compilerInputs"] == 885
            and summary["naturalSteps"] == 47 and summary["focusedPassedCases"] == 72
            and summary["old300ExecutedThisBatch"] is False and summary["sdkGoalComplete"] is False
            and summary["completeSeekPermitted"] is False, "Prior 885 scope differs")
    require(manifest.get("run/legacy-case-list.json") == sha(names_path)
            and manifest.get("run/inputs-after.json") == sha(inputs_path),
            "Prior archived names/input inventory differs")
    require(len(names) == 300 and [x["index"] for x in names] == list(range(1, 301))
            and len({x["name"] for x in names}) == 300 and len(inputs) == 885,
            "Prior 300 names or 885 inputs invalid")
    return inputs, names


def check_payload_and_review(view: View, inputs: dict[str, str], prior_inputs: dict[str, str]) -> None:
    manifest = read(view.control(str(INTEGRATION / "payload-manifest.json")))
    require(manifest["schema"] == "line-endpoint-writer-main-integration-payload/v1"
            and manifest["acceptedBase"]["validationSummarySha256"] == PRIOR_SUMMARY_SHA
            and manifest["expectedAfterApplying"]["compilerInputs"] == 891
            and manifest["expectedAfterApplying"]["defaultCases"] == 389
            and manifest["expectedAfterApplying"]["newWriterCases"] == 17
            and manifest["expectedAfterApplying"]["legacyCasesListed"] == 300
            and manifest["patchSha256"] == PAYLOAD_PATCH_SHA
            and manifest["freshMainBuildOrTestsRunByPayloadAuthor"] is False,
            "Payload manifest overclaims or differs")
    records = manifest["files"]
    require(isinstance(records, list) and len(records) == 9 and len({x["path"] for x in records}) == 9,
            "Nine-file payload inventory differs")
    for row in records:
        name = safe_name(row["path"])
        actual = sha(view.control(str(ROOT / "scripts/Build.ps1"))) if name == "scripts/Build.ps1" else sha(view.input(name))
        require(row["payloadSha256"] == actual and row["kind"] in ("new", "replacement"),
                "Main payload byte differs: " + name)
        if row["kind"] == "new":
            require(name not in prior_inputs and row["baseSha256"] is None, "New payload base differs: " + name)
        else:
            old = (view.prior("controls/Build.ps1") if name == "scripts/Build.ps1" else
                   view.prior("compiler-inputs/" + name))
            require(row["baseSha256"] == sha(old) and row["acceptedArchiveSha256"] == sha(old),
                    "Replacement payload base differs: " + name)
    application = read(view.control(str(INTEGRATION / "application-record.json")))
    require(application["appliedToMain"] is True and application["priorInputs"] == 885
            and application["newInputs"] == 891 and application["newValidationPassed"] is False
            and application["candidateObservedRootExec"] == {"sessionId": 23504,
                "completionChunkId": "92fa40", "actualExitCode": 0}
            and application["candidateResultSha256"] == CANDIDATE_RESULT_SHA
            and application["manifestSha256"] == PAYLOAD_MANIFEST_SHA
            and application["sdkGoalComplete"] is False
            and application["completeSeekPermitted"] is False
            and application["atomicConditionalWrites"] is False,
            "Main application record differs")
    candidate = read(view.extra("candidate/root-r4-result.json", CANDIDATE_RESULT))
    require(candidate["passed"] is True and candidate["mainApplied"] is False
            and len(candidate["passNames"]) == 17 and len(candidate["steps"]) == 2
            and candidate["compiledUnchanged"] is True and candidate["inputsUnchanged"] is True
            and candidate["sdkGoalComplete"] is False and candidate["completeSeekPermitted"] is False,
            "Prior isolated candidate record differs")
    review_path = view.extra("reviews/review-root-r2.json", REVIEW / "review-root-r2.json")
    review = read(review_path)
    require(sha(review_path) == REVIEW_JSON_SHA
            and sha(view.extra("reviews/review-root-r2.md", REVIEW / "review-root-r2.md")) == REVIEW_MD_SHA
            and review["blockingFindingsForRestrictedCommandPrimitive"] == []
            and len(review["openNativeAtomicityLimit"]) == 1
            and review["completeSeekPermitted"] is False
            and review["sdkGoalComplete"] is False,
            "Independent review or open native limit differs")
    commands = safe_file(view.input("src/RizToolbox.Adapter/Gameplay/NativeSeekLineEndpointCommands.cs")).read_text(encoding="utf-8")
    require("internal bool BeforeIsAtomicPreState => false;" in commands
            and "internal bool AtomicConditionalWrites => false;" in commands
            and "internal bool CompleteSeekPermitted => false;" in commands,
            "Endpoint command atomicity/Seek guard differs")


def check_evidence(view: View) -> dict:
    result_path = view.run("result.json")
    result = read(result_path)
    require(result.get("passed") is True and not result.get("failure")
            and result["finishedUtc"] and result["controllerPid"] == CONTROLLER_PID
            and result["compilerInputs"] == 891 and result["defaultExpectedCases"] == 389
            and result["scope"] == "Parent-equal observational endpoint commands and selected source/lifetime regressions; not atomic Unity writes"
            and result["activeChild"] is None
            and result["old300ExecutedThisBatch"] is False
            and result["default389SingleProcessExecuted"] is False
            and result["fullSource19ExecutedThisBatch"] is False
            and result["fullBasis16ExecutedThisBatch"] is False
            and result["sdkGoalComplete"] is False
            and result["completeSeekPermitted"] is False
            and result["atomicConditionalWrites"] is False
            and result["gameStarted"] is False and result["installed"] is False,
            "Controller terminal identity or scope differs")
    require(all(result[x] is True for x in ("inputsUnchanged", "controlsUnchanged",
            "nativeFixturesUnchanged", "compiledBytesUnchanged")), "Controller terminal gate is false")
    require(result["controlsBefore"] == {name: digest for name, (_, digest) in CONTROLS.items()},
            "Controller controls map differs")
    for original, (_, digest) in CONTROLS.items():
        require(sha(view.control(original)) == digest, "Control byte differs: " + original)
    require(sha(view.extra("controls/observe.py", OBSERVER)) == OBSERVER_SHA
            and sha(view.extra("controls/input-scanner.py", HELPER)) == HELPER_SHA
            and sha(view.extra("controls/NuGet.Config", OFFLINE)) == OFFLINE_SHA
            and sha(view.extra("fixtures/native-source-record.json", FIXTURE_RECORD)) == FIXTURE_RECORD_SHA
            and sha(view.extra("candidate/root-r4-result.json", CANDIDATE_RESULT)) == CANDIDATE_RESULT_SHA,
            "Auxiliary frozen control byte differs")
    before, after = read(view.run("inputs-before.json")), read(view.run("inputs-after.json"))
    require(before == after and len(before) == 891
            and all(SHA_RE.fullmatch(x) for x in before.values()), "891 compiler input snapshots differ")
    for name, digest in before.items():
        require(safe_name(name) == name and sha(view.input(name)) == digest,
                "Compiler input byte differs: " + name)
    prior_inputs, prior_names = check_prior(view)
    new_names = {x["path"] for x in read(view.control(str(INTEGRATION / "payload-manifest.json")))["files"]
                 if x["kind"] == "new"}
    require(set(prior_inputs) <= set(before) and set(before) - set(prior_inputs) == new_names
            and len(new_names) == 6
            and {x for x in prior_inputs if before[x] != prior_inputs[x]} == {
                "src/RizToolbox.Adapter/Gameplay/NativeSeekLineRingSourceCapture.Lifecycle.cs",
                "tests/RizToolbox.Judgements.Tests/Program.cs"},
            "891/885 compiler graph delta differs")
    check_payload_and_review(view, before, prior_inputs)
    listing = read(view.run("legacy-case-list.json"))
    require(listing == prior_names and len(listing) == 300
            and all(not x["name"].startswith(("Line ring source ", "Line chart occurrence ",
                "Line ring numeric ", "Canvas basis ")) for x in listing),
            "Legacy 300 names or exclusion differ")
    program = safe_file(view.input("tests/RizToolbox.Judgements.Tests/Program.cs")).read_text(encoding="utf-8")
    usage = re.findall(r'Console\.Error\.WriteLine\("(Usage: [^"\r\n]+)"\);', program)
    require(len(usage) == 1 and "--line-ring-endpoint-writer-checks" in usage[0]
            and "NativeSeekLineRingSourceChecks.EndpointWriterCases" in program,
            "Program selector registry or usage differs")
    names = {key: case_names(view.input(file), marker, count)
             for key, (file, marker, count) in CASES.items()}
    runtime_names = [case for file, count in RUNTIME_CASES
                     for case in case_names(view.input("tests/RizToolbox.Runtime.Tests/" + file + ".cs"),
                                            "[] Cases =", count)]
    require(len(runtime_names) == 51 and len(set(runtime_names)) == 51,
            "Runtime ABI case inventory differs")
    steps = result["steps"]
    require(isinstance(steps, list) and [row["name"] for row in steps] == STEPS
            and len(set(STEPS)) == 18, "Exact 18-step order differs")
    for row in steps:
        name = row["name"]
        argv, expected_exit, count, prefix, runs_expected = step_spec(name)
        require(row["arguments"] == argv and row["exitCode"] == expected_exit
                and row["naturalExit"] is True and row["matchesExpected"] is True,
                "Step argv or natural expected exit differs: " + name)
        stdout, stderr = view.log(name, "stdout"), view.log(name, "stderr")
        require(sha(stdout) == row["stdoutSha256"] and sha(stderr) == row["stderrSha256"],
                "Step log byte differs: " + name)
        body = stdout.read_text(encoding="utf-8-sig")
        errors = stderr.read_text(encoding="utf-8-sig")
        runs = re.findall(r"(?m)^RUN (.+)$", body)
        passes = re.findall(r"(?m)^PASS (.+)$", body)
        totals = re.findall(r"(\d+)/(\d+) passed", body)
        require(not re.search(r"(?m)^FAIL\b", body + errors) and row["passNames"] == passes,
                "Step recorded PASS/failure differs: " + name)
        if expected_exit == 0:
            require(not errors, "Successful step has stderr: " + name)
        if name in ("six-release", "solution"):
            require(not runs and not passes and not totals
                    and ("已成功生成。" in body or "Build succeeded." in body)
                    and re.findall(r"(?m)^\s*(\d+) (?:个错误|Error\(s\))\s*$", body) == ["0"],
                    "Build success or zero errors differs: " + name)
            if name == "six-release":
                require(re.findall(r"(?m)^\s*(\d+) (?:个警告|Warning\(s\))\s*$", body) == ["0"],
                        "Six Release build warnings differ")
        elif name == "legacy-listing":
            require(not runs and not passes and not totals and parse_json(body) == listing,
                    "Legacy listing stdout differs")
        elif name.startswith("invalid-"):
            require(not body and errors == usage[0] + "\n" and not runs and not passes and not totals,
                    "Invalid selector output differs: " + name)
        elif name == "renderer-abi":
            require(not runs and passes == runtime_names and totals == [("51", "51")],
                    "Runtime Renderer ABI 51 PASS-only names/count differ")
        else:
            require(runs_expected and runs == passes and len(passes) == count
                    and len(set(passes)) == count and totals == [(str(count), str(count))]
                    and all(x.startswith(prefix) for x in passes),
                    "Judgements selector RUN/PASS/count differs: " + name)
            if name in names:
                expected_names = names[name]
            elif name.startswith("source-"):
                expected_names = [names["source"][int(name[7:]) - 1]]
            else:
                expected_names = [names["basis"][0]]
            require(passes == expected_names, "Selector registry names/order differ: " + name)
    require(sum(len(row["passNames"]) for row in steps) == 111,
            "Scoped positive case count differs")
    products = result["productsBeforeTests"]
    require(result["productsAfterSix"] == products == result["productsAfterTests"]
            and set(products) == set(SIX)
            and all(set(products[component]) == {".dll", ".pdb"} for component in SIX),
            "Six production DLL/PDB pairs differ before/after tests")
    for component in SIX:
        for ext in (".dll", ".pdb"):
            require(sha(view.production(component, ext)) == products[component][ext],
                    "Production binary byte differs: " + component + ext)
    test_paths = {str(RUN / "artifacts/bin" / component / "release" / (component + ext))
                  for component in (JUDGEMENTS, RUNTIME_TEST) for ext in (".dll", ".pdb")}
    test_pairs = result["testPairs"]
    require(set(test_pairs) == test_paths and len(test_pairs) == 4,
            "Two test DLL/PDB pair inventory differs")
    for component in (JUDGEMENTS, RUNTIME_TEST):
        for ext in (".dll", ".pdb"):
            original = str(RUN / "artifacts/bin" / component / "release" / (component + ext))
            require(sha(view.test(component, ext)) == test_pairs[original],
                    "Test binary byte differs: " + component + ext)
    dependencies = result["dependencies"]
    expected_dependencies = {
        str(RUN / "artifacts/bin" / component / "release" / (product + ext)): products[product][ext]
        for component, chosen in ((JUDGEMENTS, SIX[:4]),
                                  (RUNTIME_TEST, ("RizToolbox.Runtime", "RizToolbox.Sdk")))
        for product in chosen for ext in (".dll", ".pdb")
    }
    require(dependencies == expected_dependencies and len(dependencies) == 12,
            "Twelve test dependency copies differ")
    for component, chosen in ((JUDGEMENTS, SIX[:4]),
                              (RUNTIME_TEST, ("RizToolbox.Runtime", "RizToolbox.Sdk"))):
        for product in chosen:
            for ext in (".dll", ".pdb"):
                require(sha(view.dependency(component, product, ext)) == products[product][ext],
                        "Test dependency byte differs: " + component + "/" + product + ext)
    fixtures = result["nativeFixtures"]
    require(set(fixtures) == {"RIZ_INITIALIZATION_ABI_FIXTURE", "RIZ_INITIALIZATION_ABI_DOBBY",
            "RIZ_INITIALIZATION_CONTEXT_HELPER"}
            and read(view.extra("fixtures/native-source-record.json", FIXTURE_RECORD))["nativeFixtures"] == fixtures,
            "Native fixture source set differs")
    for key, row in fixtures.items():
        require(SHA_RE.fullmatch(row["sha256"]) is not None
                and sha(view.fixture(key, row["path"])) == row["sha256"],
                "Native fixture byte differs: " + key)
    require(sha(view.golden()) == GOLDEN_SHA
            and before["tests/RizToolbox.Judgements.Tests/LineRingNumeric/single-rounding-goldens.json"] == GOLDEN_SHA,
            "Full Single golden fixture differs")
    return {"resultSha256": sha(result_path), "sourceGraphSha256": sha(view.run("inputs-after.json")),
            "naturalSteps": 18, "focusedPassedCases": 111, "judgementsPassedCases": 60,
            "runtimeAbiPassedCases": 51, "legacyCasesListed": 300}


class Plan:
    def __init__(self):
        self.files: dict[str, tuple[Path, str, int]] = {}
        self.generated: dict[str, bytes] = {}
        self.total = 0

    def bound(self) -> None:
        reserve = (len(self.files) + len(self.generated) + 1) * 160
        require(len(self.files) + len(self.generated) + 1 <= MAX_FILES
                and self.total + reserve <= MAX_BYTES, "Archive file/byte bound exceeded")

    def add(self, path: Path, name: str, expected: str | None = None) -> None:
        name = safe_name(name)
        require(name not in self.files and name not in self.generated and name != "artifact-manifest.json",
                "Archive path collision: " + name)
        source = safe_file(path)
        digest = sha(source)
        require(expected is None or digest == expected, "Source SHA differs: " + name)
        size = source.stat().st_size
        self.files[name] = (source, digest, size)
        self.total += size
        self.bound()

    def generate(self, name: str, body: bytes) -> None:
        name = safe_name(name)
        require(name not in self.files and name not in self.generated and name != "artifact-manifest.json",
                "Archive path collision: " + name)
        self.generated[name] = body
        self.total += len(body)
        self.bound()

    def recheck_sources(self) -> None:
        for name, (source, digest, size) in self.files.items():
            require(source.stat().st_size == size and sha(source) == digest,
                    "Source changed during archive staging: " + name)


def audit_current(receipt_sha: str) -> tuple[dict, dict[str, str], dict]:
    require(HERE == ROOT / "work/line-endpoint-writer-evidence-20261113"
            and ROOT == Path("C:/Users/71957/Desktop/riz_toolbox").resolve(),
            "Publisher location differs")
    require(not ARCHIVE.exists(), "Preserve existing endpoint writer archive")
    require(sha(INTEGRATION / "validate_main.py") == CONTROLLER_SHA
            and sha(OBSERVER) == OBSERVER_SHA and sha(HELPER) == HELPER_SHA
            and sha(OFFLINE) == OFFLINE_SHA, "Controller/observer/scanner/offline bytes changed")
    view = View(RUN, False)
    result_path = view.run("result.json")
    result = read(result_path)
    check_receipt(RECEIPT, receipt_sha, result_path, result)
    before, after = read(view.run("inputs-before.json")), read(view.run("inputs-after.json"))
    require(load_scanner().inputs() == before == after,
            "Current 891 source graph changed after completed run")
    evidence = check_evidence(view)
    require(evidence["resultSha256"] == read(RECEIPT)["resultSha256AtExit"],
            "Result changed after original handle exit")
    return result, before, evidence


def make_plan(result: dict, inputs: dict[str, str], receipt_sha: str) -> Plan:
    plan = Plan()
    for name, digest in inputs.items():
        plan.add(ROOT / name, "compiler-inputs/" + name, digest)
    for original, (name, digest) in CONTROLS.items():
        plan.add(Path(original), name, digest)
    for source, name, digest in ((OBSERVER, "controls/observe.py", OBSERVER_SHA),
            (HELPER, "controls/input-scanner.py", HELPER_SHA),
            (OFFLINE, "controls/NuGet.Config", OFFLINE_SHA),
            (FIXTURE_RECORD, "fixtures/native-source-record.json", FIXTURE_RECORD_SHA),
            (CANDIDATE_RESULT, "candidate/root-r4-result.json", CANDIDATE_RESULT_SHA),
            (REVIEW / "review-root-r2.json", "reviews/review-root-r2.json", REVIEW_JSON_SHA),
            (REVIEW / "review-root-r2.md", "reviews/review-root-r2.md", REVIEW_MD_SHA),
            (RECEIPT, "run/controller-handle-exit.json", receipt_sha)):
        plan.add(source, name, digest)
    for name in ("result.json", "inputs-before.json", "inputs-after.json", "legacy-case-list.json"):
        plan.add(RUN / name, "run/" + name)
    for name in STEPS:
        for stream in ("stdout", "stderr"):
            filename = name + "." + stream + ".log"
            plan.add(RUN / filename, "run/logs/" + filename)
    for name, source in (("validation-summary.json", PRIOR / "validation-summary.json"),
                         ("artifact-manifest.json", PRIOR / "artifact-manifest.json"),
                         ("legacy-case-list.json", PRIOR / "run/legacy-case-list.json"),
                         ("inputs-after.json", PRIOR / "run/inputs-after.json"),
                         ("controls/Build.ps1", PRIOR / "controls/Build.ps1"),
                         ("compiler-inputs/src/RizToolbox.Adapter/Gameplay/NativeSeekLineRingSourceCapture.Lifecycle.cs",
                          PRIOR / "compiler-inputs/src/RizToolbox.Adapter/Gameplay/NativeSeekLineRingSourceCapture.Lifecycle.cs"),
                         ("compiler-inputs/tests/RizToolbox.Judgements.Tests/Program.cs",
                          PRIOR / "compiler-inputs/tests/RizToolbox.Judgements.Tests/Program.cs")):
        plan.add(source, "prior-885/" + name)
    binary = RUN / "artifacts/bin"
    for component in SIX:
        for ext in (".dll", ".pdb"):
            plan.add(binary / component / "release" / (component + ext),
                     "binaries/production/" + component + ext,
                     result["productsBeforeTests"][component][ext])
    for component in (JUDGEMENTS, RUNTIME_TEST):
        for ext in (".dll", ".pdb"):
            source = binary / component / "release" / (component + ext)
            plan.add(source, "binaries/test/" + component + "/" + component + ext,
                     result["testPairs"][str(source)])
    for original, digest in result["dependencies"].items():
        source = Path(original)
        component = source.parent.parent.name
        require(component in (JUDGEMENTS, RUNTIME_TEST), "Unexpected dependency owner")
        plan.add(source, "binaries/dependencies/" + component + "/" + source.name, digest)
    plan.add(binary / JUDGEMENTS / "release/line-ring-numeric-goldens.json",
             "fixtures/line-ring-numeric-goldens.json", GOLDEN_SHA)
    for key, row in result["nativeFixtures"].items():
        plan.add(Path(row["path"]), "fixtures/native/" + key + "/" + Path(row["path"]).name,
                 row["sha256"])
    plan.add(Path(__file__), "publisher/publish.py")
    summary = {
        "schema": "line-endpoint-writer-main-evidence/v1",
        "acceptedAtUtc": dt.datetime.now(dt.timezone.utc).isoformat(),
        "acceptedThisBatch": True, "compilerInputs": 891, "defaultExpectedCases": 389,
        "naturalSteps": 18, "focusedPassedCases": 111, "judgementsPassedCases": 60,
        "runtimeAbiPassedCases": 51, "invalidSelectorChecks": 2,
        "writerCases": 17, "providerCases": 6, "chartOccurrenceCases": 5,
        "endpointCases": 6, "numericCases": 13, "ringXCases": 7,
        "selectedSourceCases": 5, "selectedBasisCases": 1,
        "sixReleaseWarnings": 0, "buildErrors": 0,
        "productionPairs": 6, "testPairs": 2, "dependencyFiles": 12,
        "nativeFixtures": 3, "legacyCasesListed": 300,
        "old300ExecutedThisBatch": False, "default389SingleProcessExecuted": False,
        "fullSource19ExecutedThisBatch": False, "fullBasis16ExecutedThisBatch": False,
        "sdkGoalComplete": False, "completeSeekPermitted": False,
        "fullSeekPermitted": False, "atomicConditionalWrites": False,
        "beforeIsAtomicPreState": False, "externalEffectsUnclosed": True,
        "sideEffectsUnknownAfterNativeEntry": True, "gameStarted": False, "installed": False,
        "resultSha256": sha(RUN / "result.json"),
        "exitReceiptSha256": receipt_sha,
        "sourceGraphSha256": sha(RUN / "inputs-after.json"),
        "prior885SummarySha256": PRIOR_SUMMARY_SHA,
        "candidateRoot4ResultSha256": CANDIDATE_RESULT_SHA,
        "payloadManifestSha256": PAYLOAD_MANIFEST_SHA,
        "independentReviewSha256": REVIEW_JSON_SHA,
        "scope": "Offline parent-equal observational endpoint local-position command and selected source/lifetime checks; native atomic gate remains open",
    }
    plan.generate("validation-summary.json", encoded(summary))
    plan.generate("README.md", (
        "# Endpoint writer incremental evidence\n\n"
        "The 891-input main graph completed 18 scoped natural steps with 111 focused PASS results "
        "(60 Judgements and 51 Runtime Renderer ABI). The previous accepted 885-input scope is separate. "
        "The 300 legacy names were listed and compared; the old 300, full 19 source cases, full 16 basis cases, "
        "and default 389 in one process were not executed in this batch. "
        "The 17 new cases cover a parent-equal local-position command with observed prestate. "
        "Native engine atomicity and external effects remain open; this does not admit complete Seek or the SDK goal.\n\n"
        "Recheck archived bytes only: python -B publisher/publish.py --check-only --archive <archive-dir>.\n"
    ).encode("utf-8"))
    return plan


def check_archive(path: Path) -> dict:
    raw = Path(path)
    require(raw.is_dir() and not raw.is_symlink(), "Missing or linked archive")
    path = raw.resolve()
    found = set()
    for item in path.rglob("*"):
        require(not item.is_symlink(), "Archive contains linked path")
        if item.is_file():
            found.add(safe_name(item.relative_to(path).as_posix()))
    manifest = read(path / "artifact-manifest.json")
    require(isinstance(manifest, dict) and len(found) <= MAX_FILES
            and found == set(manifest) | {"artifact-manifest.json"},
            "Archive manifest inventory differs")
    total = (path / "artifact-manifest.json").stat().st_size
    for name, digest in manifest.items():
        require(safe_name(name) == name and SHA_RE.fullmatch(digest) is not None
                and sha(path / name) == digest, "Archived byte differs: " + name)
        total += (path / name).stat().st_size
    require(total <= MAX_BYTES, "Archive byte bound exceeded")
    summary = read(path / "validation-summary.json")
    require(summary["schema"] == "line-endpoint-writer-main-evidence/v1"
            and summary["acceptedThisBatch"] is True
            and summary["compilerInputs"] == 891
            and summary["defaultExpectedCases"] == 389
            and summary["naturalSteps"] == 18
            and summary["focusedPassedCases"] == 111
            and summary["judgementsPassedCases"] == 60
            and summary["runtimeAbiPassedCases"] == 51
            and summary["invalidSelectorChecks"] == 2
            and summary["writerCases"] == 17
            and summary["selectedSourceCases"] == 5
            and summary["selectedBasisCases"] == 1
            and summary["productionPairs"] == 6
            and summary["testPairs"] == 2
            and summary["dependencyFiles"] == 12
            and summary["nativeFixtures"] == 3
            and summary["sixReleaseWarnings"] == 0
            and summary["buildErrors"] == 0
            and summary["legacyCasesListed"] == 300
            and summary["old300ExecutedThisBatch"] is False
            and summary["default389SingleProcessExecuted"] is False
            and summary["fullSource19ExecutedThisBatch"] is False
            and summary["fullBasis16ExecutedThisBatch"] is False
            and summary["sdkGoalComplete"] is False
            and summary["completeSeekPermitted"] is False
            and summary["fullSeekPermitted"] is False
            and summary["atomicConditionalWrites"] is False
            and summary["beforeIsAtomicPreState"] is False
            and summary["externalEffectsUnclosed"] is True
            and summary["sideEffectsUnknownAfterNativeEntry"] is True
            and summary["gameStarted"] is False and summary["installed"] is False
            and summary["prior885SummarySha256"] == PRIOR_SUMMARY_SHA
            and summary["candidateRoot4ResultSha256"] == CANDIDATE_RESULT_SHA
            and summary["payloadManifestSha256"] == PAYLOAD_MANIFEST_SHA
            and summary["independentReviewSha256"] == REVIEW_JSON_SHA,
            "Archive summary scope differs")
    view = View(path, True)
    result_path = view.run("result.json")
    result = read(result_path)
    require(sha(result_path) == summary["resultSha256"]
            and sha(view.run("controller-handle-exit.json")) == summary["exitReceiptSha256"]
            and sha(view.run("inputs-after.json")) == summary["sourceGraphSha256"],
            "Archive summary result/receipt/graph hashes differ")
    check_receipt(view.run("controller-handle-exit.json"), summary["exitReceiptSha256"], result_path, result)
    evidence = check_evidence(view)
    require(evidence["resultSha256"] == summary["resultSha256"]
            and evidence["sourceGraphSha256"] == summary["sourceGraphSha256"],
            "Independent archived result audit differs")
    inputs = read(view.run("inputs-after.json"))
    required = {"compiler-inputs/" + name for name in inputs}
    required |= {"run/logs/" + name + "." + stream + ".log"
                 for name in STEPS for stream in ("stdout", "stderr")}
    required |= {"binaries/production/" + component + ext
                 for component in SIX for ext in (".dll", ".pdb")}
    required |= {"binaries/test/" + component + "/" + component + ext
                 for component in (JUDGEMENTS, RUNTIME_TEST) for ext in (".dll", ".pdb")}
    required |= {"binaries/dependencies/" + Path(name).parent.parent.name + "/" + Path(name).name
                 for name in result["dependencies"]}
    require(len(required) == 891 + 36 + 12 + 4 + 12,
            "Required compiler/log/binary cardinality differs")
    required |= {"validation-summary.json", "README.md", "publisher/publish.py",
                 "run/result.json", "run/inputs-before.json", "run/inputs-after.json",
                 "run/legacy-case-list.json", "run/controller-handle-exit.json",
                 "prior-885/validation-summary.json", "prior-885/artifact-manifest.json",
                 "prior-885/legacy-case-list.json", "prior-885/inputs-after.json",
                 "prior-885/controls/Build.ps1",
                 "prior-885/compiler-inputs/src/RizToolbox.Adapter/Gameplay/NativeSeekLineRingSourceCapture.Lifecycle.cs",
                 "prior-885/compiler-inputs/tests/RizToolbox.Judgements.Tests/Program.cs",
                 "controls/observe.py", "controls/input-scanner.py", "controls/NuGet.Config",
                 "candidate/root-r4-result.json", "reviews/review-root-r2.json",
                 "reviews/review-root-r2.md", "fixtures/line-ring-numeric-goldens.json",
                 "fixtures/native-source-record.json"}
    required |= {name for name, _ in CONTROLS.values()}
    required |= {"fixtures/native/" + key + "/" + Path(row["path"]).name
                 for key, row in result["nativeFixtures"].items()}
    require(required <= found, "Archive required exact inventory is incomplete")
    return {"acceptedThisBatch": True, "archiveFiles": len(found), "archiveBytes": total,
            "summarySha256": sha(path / "validation-summary.json"),
            "sdkGoalComplete": False, "completeSeekPermitted": False,
            "atomicConditionalWrites": False}


def publish(receipt_sha: str) -> dict:
    require(not ARCHIVE.exists(), "Preserve existing endpoint writer archive")
    result, inputs, _ = audit_current(receipt_sha)
    plan = make_plan(result, inputs, receipt_sha)
    staging = ARCHIVE.parent / (".line-endpoint-writer-staging-" + uuid.uuid4().hex)
    staging.mkdir()
    # A failed stage is retained for diagnosis; never merge partial evidence.
    for name, (source, digest, size) in plan.files.items():
        target = staging / name
        target.parent.mkdir(parents=True, exist_ok=True)
        with source.open("rb") as reader, target.open("xb") as writer:
            while block := reader.read(1024 * 1024):
                writer.write(block)
        require(target.stat().st_size == size and sha(target) == digest,
                "Staged byte differs: " + name)
    for name, body in plan.generated.items():
        target = staging / name
        target.parent.mkdir(parents=True, exist_ok=True)
        with target.open("xb") as writer:
            writer.write(body)
    manifest = {name: digest for name, (_, digest, _) in plan.files.items()}
    manifest.update({name: hashlib.sha256(body).hexdigest() for name, body in plan.generated.items()})
    (staging / "artifact-manifest.json").write_bytes(encoded(manifest))
    plan.recheck_sources()
    checked = check_archive(staging)
    require(checked["acceptedThisBatch"] is True and not ARCHIVE.exists(),
            "Staged archive audit failed or concurrent archive appeared")
    staging.rename(ARCHIVE)
    return check_archive(ARCHIVE)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check-only", action="store_true")
    parser.add_argument("--archive", type=Path, default=ARCHIVE)
    parser.add_argument("--exit-receipt-sha", type=str)
    args = parser.parse_args()
    if args.check_only:
        require(args.exit_receipt_sha is None, "Archived check is independent of external receipt")
        result = check_archive(args.archive)
    else:
        require(args.archive == ARCHIVE and args.exit_receipt_sha is not None
                and SHA_RE.fullmatch(args.exit_receipt_sha.lower()) is not None,
                "Publication requires the fixed path and independently pinned original handle-exit receipt SHA")
        result = publish(args.exit_receipt_sha.lower())
    print(json.dumps(result), flush=True)


if __name__ == "__main__":
    main()
