"""Audit and atomically archive the completed 885-input scoped validation.

Publication requires the original controller's completed Windows-handle exit
record and its independently supplied SHA-256. This script does no build/test.
The --check-only path reads the archive alone, including its own source copy.
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
INTEGRATION = ROOT / "work/line-ring-target-provider-integration-20261113"
RUN = Path("D:/riz-build-cache-next/line-ring-target-provider-integration-20261113/main-r1")
ARCHIVE = ROOT / "docs/validation/line-ring-target-provider-20261113"
PRIOR = ROOT / "docs/validation/line-ring-endpoint-local-20261113"
RECEIPT = INTEGRATION / "controller-handle-exit.json"
HELPER = ROOT / "work/line-ring-source-validation-20261113/run_validation.py"
OFFLINE = ROOT / "work/ring-x-numeric-20261113/NuGet.Config"
FIXTURE_RECORD = ROOT / "docs/validation/canvas-basis-shared-source-20261112/build-test-result.json"
DOTNET = Path("C:/Program Files/dotnet/dotnet.exe")
TEST = "RizToolbox.Judgements.Tests"
SIX = ["RizToolbox." + x for x in ("Sdk", "Core", "Runtime", "Adapter", "Hosting", "Loader")]
SHA_RE = re.compile(r"[0-9a-f]{64}\Z")
MAX_FILES = 1500
MAX_BYTES = 256 * 1024 * 1024
MAX_FILE_BYTES = 64 * 1024 * 1024
CONTROLLER_PID = 40232
CONTROLLER_SCRIPT_SHA = "b397c10117d124db254b4c33a68d99577c31323fc4385093f7ef2a58ee99a9d8"
OBSERVER_SHA = "c8c196c4e244f454cb74ba1fdad6e361db39427bf452f72efb75a2353e6ab663"
HELPER_SHA = "61c2983ae117322b25ff96ad75b659b870fdadf9aada9274727248cd5ca98fd4"
OFFLINE_SHA = "964302159308872b9729e1828a1ba897f30634f7bd751cde11ea8e6fed8cfb97"
BUILD_SHA = "7086702d90ffcc936231f8090049513aa2ce43254888d13cbe43155d5b521db1"
PAYLOAD_MANIFEST_SHA = "6b680faffe7ec28e3d67d8b0e42f14300cde09fdc30f663077e1f85e8b3d3392"
PAYLOAD_PATCH_SHA = "2d6b0783914a1c2f2b6a51b2b286a0dfbd9bfc0b44a9813ccaa42f5d2e1a82b6"
REVIEW_JSON_SHA = "f6a46ea4fe91d097477b7bb5dac159edc45c5f100ad6cde0a1a681528093f463"
REVIEW_MD_SHA = "95fd2b90697aa34c8de0da302ef9137a0a799da55958935a3f13ef60e0c345b4"
PRIOR_SUMMARY_SHA = "004d421af4985d78ef9c13aa709b7d7470ced8ed27e902826969c786a0166957"
PRIOR_MANIFEST_SHA = "1f3946fa03f4fac67f10a88b567ccf183f0a3deae830ae9d118f9c3c0777585c"
GOLDEN_SHA = "1bf6d52a1e1aacc01f2c0e92c951ed860f0137164860476888e473c7cea13a48"
FIXTURE_RECORD_SHA = "79776e6285697456b92d34689f802cfca9d59294cb871fb765eac59a1eac76d2"
APPLICATION_SHA = "8de9e82d6006b1382eaaf906d02d0af64b22c2b96313d8b66d6421a0e983b8d8"
CONTROL_NAMES = {
    str(INTEGRATION / "validate.py"): ("controls/validate.py", CONTROLLER_SCRIPT_SHA),
    str(ROOT / "scripts/Build.ps1"): ("controls/Build.ps1", BUILD_SHA),
    str(ROOT / "work/line-chart-occurrence-integration-20261113/payload-manifest.json"):
        ("controls/payload-manifest.json", PAYLOAD_MANIFEST_SHA),
    str(ROOT / "work/line-chart-occurrence-integration-20261113/payload.patch"):
        ("controls/payload.patch", PAYLOAD_PATCH_SHA),
}
FIRST12 = ["six-release", "solution", "legacy-listing", "target-provider", "chart-occurrence",
           "endpoint-local", "numeric", "ring-x", "invalid-chart-name", "invalid-chart-arity",
           "invalid-provider-name", "invalid-provider-arity"]
SOURCE_NAMES = [f"source-{i:02}" for i in range(1, 20)]
BASIS_NAMES = [f"basis-{i:02}" for i in range(1, 17)]
ALL_STEPS = FIRST12 + SOURCE_NAMES + BASIS_NAMES
CASE_FILES = {
    "target-provider": ("tests/RizToolbox.Judgements.Tests/LineRingSource/NativeSeekLineRingSourceChecks.TargetProvider.cs", "[] TargetProviderCases =", 6),
    "chart-occurrence": ("tests/RizToolbox.Judgements.Tests/LineRingSource/NativeSeekLineChartOccurrenceChecks.cs", "[] Cases =", 5),
    "endpoint-local": ("tests/RizToolbox.Judgements.Tests/LineRingNumeric/NativeSeekLineEndpointLocalChecks.cs", "[] Cases =", 6),
    "numeric": ("tests/RizToolbox.Judgements.Tests/LineRingNumeric/NativeSeekLineRingNumericChecks.cs", "[] Cases =", 13),
    "ring-x": ("tests/RizToolbox.Judgements.Tests/LineRingNumeric/NativeSeekLineRingXNumericChecks.cs", "[] Cases =", 7),
    "source": ("tests/RizToolbox.Judgements.Tests/LineRingSource/NativeSeekLineRingSourceChecks.cs", "[] Cases =", 19),
    "basis": ("tests/RizToolbox.Judgements.Tests/CanvasBasis/NativeSeekCanvasBasisChecks.cs", "[] Cases =", 16),
}


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


def no_duplicate_keys(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, "Duplicate JSON key: " + key)
        result[key] = value
    return result


def parse_json(text: str):
    return json.loads(text, object_pairs_hook=no_duplicate_keys)


def read(path: Path):
    return parse_json(safe_file(path).read_text(encoding="utf-8-sig"))


def encoded(value) -> bytes:
    return (json.dumps(value, ensure_ascii=False, indent=2) + "\n").encode("utf-8")


def stamp(value: str) -> dt.datetime:
    parsed = dt.datetime.fromisoformat(value)
    require(parsed.tzinfo is not None, "UTC observation lacks timezone")
    return parsed.astimezone(dt.timezone.utc)


def expected_controls() -> dict[str, str]:
    return {name: digest for name, (_, digest) in CONTROL_NAMES.items()}


def check_prior(prior_summary: Path, prior_manifest: Path, prior_names: Path) -> list[dict]:
    require(sha(prior_summary) == PRIOR_SUMMARY_SHA and sha(prior_manifest) == PRIOR_MANIFEST_SHA,
            "Accepted 881 archive pin differs")
    prior = read(prior_summary)
    require(prior["acceptedThisBatch"] is True and prior["compilerInputs"] == 881
            and prior["naturalSteps"] == 11 and prior["focusedPassedCases"] == 29
            and prior["old300ExecutedThisBatch"] is False and prior["sdkGoalComplete"] is False
            and prior["completeSeekPermitted"] is False, "Prior 881 scope differs")
    manifest = read(prior_manifest)
    require(isinstance(manifest, dict) and manifest.get("run/legacy-case-list.json") == sha(prior_names),
            "Prior name inventory is not the pinned archived file")
    names = read(prior_names)
    require(isinstance(names, list) and len(names) == 300
            and [x["index"] for x in names] == list(range(1, 301))
            and len({x["name"] for x in names}) == 300, "Prior 300 names invalid")
    return names


def check_receipt(receipt_path: Path, receipt_digest: str, result_path: Path, result: dict) -> dict:
    require(SHA_RE.fullmatch(receipt_digest) is not None and sha(receipt_path) == receipt_digest,
            "Original handle exit receipt SHA differs")
    receipt = read(receipt_path)
    require(set(receipt) == {"schema", "controllerPid", "createdUtc", "observedAliveUtc",
            "controllerScriptSha256", "observerSha256", "handleKeptUntilExit", "sdkGoalComplete",
            "completeSeekPermitted", "actualExitCode", "finishedUtc", "resultSha256AtExit",
            "controllerScriptUnchanged"}, "Original handle exit receipt schema differs")
    require(receipt["schema"] == "original-controller-handle-exit/v1"
            and receipt["controllerPid"] == CONTROLLER_PID
            and receipt["actualExitCode"] == 0
            and receipt["controllerScriptSha256"] == CONTROLLER_SCRIPT_SHA
            and receipt["observerSha256"] == OBSERVER_SHA
            and receipt["handleKeptUntilExit"] is True
            and receipt["controllerScriptUnchanged"] is True
            and receipt["sdkGoalComplete"] is False
            and receipt["completeSeekPermitted"] is False
            and receipt["resultSha256AtExit"] == sha(result_path), "Original handle exit is not pinned successful process")
    created = stamp(receipt["createdUtc"]); alive = stamp(receipt["observedAliveUtc"])
    finished = stamp(receipt["finishedUtc"]); result_finished = stamp(result["finishedUtc"])
    require(created <= stamp(result["startedUtc"]) <= alive <= finished
            and result_finished <= finished and (stamp(result["startedUtc"]) - created).total_seconds() < 20,
            "Original handle observation chronology differs")
    return receipt


def load_input_helper():
    require(sha(HELPER) == HELPER_SHA, "Compiler input scanner changed")
    spec = importlib.util.spec_from_file_location("provider_evidence_input_scanner", HELPER)
    require(spec is not None and spec.loader is not None, "Compiler input scanner unavailable")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def case_names(source: Path, marker: str, count: int) -> list[str]:
    body = safe_file(source).read_text(encoding="utf-8")
    require(body.count(marker) == 1, "Ambiguous registry case array: " + str(source))
    block = body.split(marker, 1)[1].split("};", 1)[0]
    names = re.findall(r'(?m)^\s*\("([^"\r\n]+)",', block)
    require(len(names) == count and len(set(names)) == count, "Registry case names differ: " + str(source))
    return names


def registry_names(input_file) -> dict[str, list[str]]:
    return {kind: case_names(input_file(name), marker, count)
            for kind, (name, marker, count) in CASE_FILES.items()}


def build_argv(name: str) -> list[str]:
    project = ROOT / ("src/RizToolbox.Loader/RizToolbox.Loader.csproj" if name == "six-release" else "RizToolbox.sln")
    return [str(DOTNET), "build", str(project), "-c", "Release", "--artifacts-path", str(RUN / "artifacts"),
            "--verbosity", "minimal", "--disable-build-servers", "-m:1", "-p:UseSharedCompilation=false",
            "-p:NuGetAudit=false", "-p:RestoreConfigFile=" + str(OFFLINE)]


def step_spec(name: str):
    test = RUN / "artifacts/bin" / TEST / "release" / (TEST + ".dll")
    prefix = [str(DOTNET), str(test)]
    if name in ("six-release", "solution"):
        return build_argv(name), 0, None, None
    if name == "legacy-listing":
        return prefix + ["--list-legacy-cases"], 0, None, None
    grouped = {
        "target-provider": ("--line-ring-target-provider-checks", 6, "Line ring source target provider "),
        "chart-occurrence": ("--line-chart-occurrence-checks", 5, "Line chart occurrence "),
        "endpoint-local": ("--line-ring-endpoint-local-checks", 6, "Line ring numeric endpoint "),
        "numeric": ("--line-ring-numeric-checks", 13, "Line ring numeric "),
        "ring-x": ("--line-ring-ring-x-numeric-checks", 7, "Line ring numeric ring "),
    }
    if name in grouped:
        selector, count, label = grouped[name]
        return prefix + [selector], 0, count, label
    invalid = {
        "invalid-chart-name": ["--line-chart-occurrence-check"],
        "invalid-chart-arity": ["--line-chart-occurrence-checks", "extra"],
        "invalid-provider-name": ["--line-ring-target-provider-check"],
        "invalid-provider-arity": ["--line-ring-target-provider-checks", "extra"],
    }
    if name in invalid:
        return prefix + invalid[name], 2, None, None
    if name.startswith("source-") and name in SOURCE_NAMES:
        return prefix + ["--line-ring-source-case", str(int(name[7:]))], 0, 1, "Line ring source "
    if name.startswith("basis-") and name in BASIS_NAMES:
        return prefix + ["--canvas-basis-case", str(int(name[6:]))], 0, 1, "Canvas basis "
    raise RuntimeError("Unexpected step: " + name)


def check_result(result: dict, result_path: Path, inputs_before: Path, inputs_after: Path,
                 controls_file, input_file, log_file, listing_file: Path, prior_names: Path,
                 binary_file, native_file, golden_file: Path, fixture_record: Path) -> dict:
    require(result.get("passed") is True and not result.get("failure") and result.get("finishedUtc")
            and result["controllerPid"] == CONTROLLER_PID and result["compilerInputs"] == 885
            and result["scope"] == "Retained real backend reader target provider and exact no-Note chart occurrence mapping; offline fixtures only"
            and result["defaultExpectedCases"] == 372 and result["activeChildren"] == []
            and result["old300ExecutedThisBatch"] is False
            and result["default372SingleProcessExecuted"] is False
            and result["sdkGoalComplete"] is False and result["completeSeekPermitted"] is False
            and result["gameStarted"] is False and result["installed"] is False,
            "Controller terminal scope or identity differs")
    require(all(result[x] is True for x in ("inputsUnchanged", "controlsUnchanged",
            "nativeFixturesUnchanged", "compiledBytesUnchanged")), "Controller terminal gate is false")
    require(result["controlsBefore"] == expected_controls(), "Controller control map differs")
    for original, (archived, digest) in CONTROL_NAMES.items():
        require(sha(controls_file(archived)) == digest, "Controller control byte differs: " + original)
    before = read(inputs_before); after = read(inputs_after)
    require(before == after and len(before) == 885 and all(SHA_RE.fullmatch(x) for x in before.values()),
            "885 compiler input snapshots differ")
    for name, digest in before.items():
        require(safe_name(name) == name and sha(input_file(name)) == digest,
                "Compiler input byte differs: " + name)
    require(sha(input_file("src/RizToolbox.Adapter/Gameplay/NativeSeekLineChartOccurrenceMap.cs"))
            == "5b3c94fceffab3f873e93df6f3c4963bee94b9801b01745c85347519126ffef4",
            "Reviewed occurrence source differs")
    prior = check_prior(*prior_names)
    listing = read(listing_file)
    require(listing == prior and len(listing) == 300 and all(not x["name"].startswith(
            ("Line chart occurrence ", "Line ring source ", "Line ring numeric ", "Canvas basis ")) for x in listing),
            "Legacy name inventory changed")
    program = safe_file(input_file("tests/RizToolbox.Judgements.Tests/Program.cs")).read_text(encoding="utf-8")
    usage = re.findall(r'Console\.Error\.WriteLine\("(Usage: [^"\r\n]+)"\);', program)
    require(len(usage) == 1 and "--line-ring-target-provider-checks" in usage[0]
            and "--line-chart-occurrence-checks" in usage[0], "Program selector usage differs")
    names = registry_names(input_file)
    steps = result["steps"]
    require(isinstance(steps, list) and len(steps) == 47
            and [row["name"] for row in steps[:12]] == FIRST12
            and {row["name"] for row in steps[12:31]} == set(SOURCE_NAMES)
            and {row["name"] for row in steps[31:]} == set(BASIS_NAMES)
            and len({row["name"] for row in steps}) == 47, "Exact 47-step groups differ")
    for row in steps:
        name = row["name"]
        argv, expected_exit, count, prefix = step_spec(name)
        require(row["arguments"] == argv and row["exitCode"] == expected_exit
                and row["naturalExit"] is True and row["matchesExpected"] is True,
                "Step argv or natural expected exit differs: " + name)
        stdout_path = log_file(name, "stdout"); stderr_path = log_file(name, "stderr")
        require(sha(stdout_path) == row["stdoutSha256"] and sha(stderr_path) == row["stderrSha256"],
                "Step log hash differs: " + name)
        body = stdout_path.read_text(encoding="utf-8-sig"); errors = stderr_path.read_text(encoding="utf-8-sig")
        runs = re.findall(r"(?m)^RUN (.+)$", body); passes = re.findall(r"(?m)^PASS (.+)$", body)
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
                        "Six Release build has warnings")
        elif name == "legacy-listing":
            require(not runs and not passes and not totals and parse_json(body) == listing,
                    "Legacy listing stdout differs")
        elif name.startswith("invalid-"):
            require(not body and errors == usage[0] + "\n" and not runs and not passes and not totals,
                    "Invalid selector output differs: " + name)
        else:
            require(totals == [(str(count), str(count))] and runs == passes
                    and len(passes) == count and len(set(passes)) == count
                    and all(x.startswith(prefix) for x in passes), "Selector RUN/PASS/count differs: " + name)
            if name in names:
                require(passes == names[name], "Grouped registry names differ: " + name)
            elif name.startswith("source-"):
                require(passes == [names["source"][int(name[7:]) - 1]], "Source case name differs: " + name)
            else:
                require(passes == [names["basis"][int(name[6:]) - 1]], "Basis case name differs: " + name)
    products = result["productsBeforeTests"]
    require(result["productsAfterSix"] == products == result["productsAfterTests"]
            and set(products) == set(SIX) and all(set(products[x]) == {".dll", ".pdb"} for x in SIX),
            "Six production DLL/PDB pairs changed")
    for component in SIX:
        for ext in (".dll", ".pdb"):
            require(sha(binary_file("production", component + ext)) == products[component][ext],
                    "Production byte differs: " + component + ext)
    pair = result["testPair"]
    require(set(pair) == {".dll", ".pdb"}, "Test pair inventory differs")
    for ext in pair:
        require(sha(binary_file("test", TEST + ext)) == pair[ext], "Test byte differs: " + ext)
    dependencies = result["dependencyCopies"]
    expected_dependencies = {str(RUN / "artifacts/bin" / TEST / "release" / (component + ext)): products[component][ext]
                             for component in SIX[:4] for ext in (".dll", ".pdb")}
    require(dependencies == expected_dependencies and len(dependencies) == 8,
            "Test dependency graph differs")
    for original, digest in dependencies.items():
        require(sha(binary_file("dependency", Path(original).name)) == digest,
                "Test dependency byte differs: " + original)
    fixtures = result["nativeFixtures"]
    require(set(fixtures) == {"RIZ_INITIALIZATION_ABI_FIXTURE", "RIZ_INITIALIZATION_ABI_DOBBY",
            "RIZ_INITIALIZATION_CONTEXT_HELPER"}, "Native fixture set differs")
    require(read(fixture_record)["nativeFixtures"] == fixtures, "Native fixture source record differs")
    for key, item in fixtures.items():
        require(SHA_RE.fullmatch(item["sha256"]) and sha(native_file(key, Path(item["path"]).name)) == item["sha256"],
                "Native fixture byte differs: " + key)
    require(sha(golden_file) == GOLDEN_SHA
            and before["tests/RizToolbox.Judgements.Tests/LineRingNumeric/single-rounding-goldens.json"] == GOLDEN_SHA,
            "Full Single golden fixture differs")
    return {"resultSha256": sha(result_path), "sourceGraphSha256": sha(inputs_after),
            "positiveCases": 72, "naturalSteps": 47, "legacyCasesListed": 300}


class Plan:
    def __init__(self):
        self.files: dict[str, tuple[Path, str, int]] = {}
        self.generated: dict[str, bytes] = {}
        self.total = 0

    def add(self, path: Path, name: str, expected: str | None = None):
        name = safe_name(name)
        require(name not in self.files and name not in self.generated and name != "artifact-manifest.json",
                "Archive path collision: " + name)
        source = safe_file(path); digest = sha(source); size = source.stat().st_size
        require(expected is None or digest == expected, "Source SHA differs: " + name)
        self.files[name] = source, digest, size
        self.total += size
        self.bound()

    def generate(self, name: str, body: bytes):
        name = safe_name(name)
        require(name not in self.files and name not in self.generated and name != "artifact-manifest.json",
                "Archive path collision: " + name)
        self.generated[name] = body
        self.total += len(body)
        self.bound()

    def bound(self):
        manifest_reserve = (len(self.files) + len(self.generated) + 1) * 160
        require(len(self.files) + len(self.generated) + 1 <= MAX_FILES
                and self.total + manifest_reserve <= MAX_BYTES,
                "Archive file/byte budget exceeded")

    def recheck_sources(self):
        for name, (source, digest, size) in self.files.items():
            require(source.stat().st_size == size and sha(source) == digest,
                    "Source changed while archiving: " + name)


def audit_current(receipt_sha: str) -> tuple[dict, dict[str, str], dict]:
    require(HERE == ROOT / "work/line-ring-target-provider-evidence-20261113"
            and ROOT == Path("C:/Users/71957/Desktop/riz_toolbox").resolve(), "Publisher location differs")
    require(sha(INTEGRATION / "validate.py") == CONTROLLER_SCRIPT_SHA
            and sha(INTEGRATION / "observe.py") == OBSERVER_SHA
            and sha(HELPER) == HELPER_SHA and sha(OFFLINE) == OFFLINE_SHA, "Control source changed")
    result_path = RUN / "result.json"; result = read(result_path)
    receipt = check_receipt(RECEIPT, receipt_sha, result_path, result)
    helper = load_input_helper()
    before = read(RUN / "inputs-before.json")
    after = read(RUN / "inputs-after.json")
    require(helper.inputs() == before == after, "Current 885 source graph changed")
    prior_inputs = read(PRIOR / "run/inputs-after.json")
    require(len(prior_inputs) == 881 and set(prior_inputs) <= set(before)
            and set(before) - set(prior_inputs) == {
                "src/RizToolbox.Adapter/Gameplay/NativeSeekLineChartOccurrenceMap.cs",
                "src/RizToolbox.Adapter/Gameplay/NativeSeekLineRingSourceCapture.Targets.cs",
                "tests/RizToolbox.Judgements.Tests/LineRingSource/NativeSeekLineChartOccurrenceChecks.cs",
                "tests/RizToolbox.Judgements.Tests/LineRingSource/NativeSeekLineRingSourceChecks.TargetProvider.cs"}
            and {x for x in prior_inputs if before[x] != prior_inputs[x]} == {
                "src/RizToolbox.Adapter/Gameplay/NativeSeekLineRingSourceCapture.Lifecycle.cs",
                "tests/RizToolbox.Judgements.Tests/Program.cs"}, "885/881 source delta differs")
    receipt_result = check_result(result, result_path, RUN / "inputs-before.json", RUN / "inputs-after.json",
        lambda name: Path({v[0]: k for k, v in CONTROL_NAMES.items()}[name]),
        lambda name: ROOT / name,
        lambda name, stream: RUN / (name + "." + stream + ".log"),
        RUN / "legacy-case-list.json",
        (PRIOR / "validation-summary.json", PRIOR / "artifact-manifest.json", PRIOR / "run/legacy-case-list.json"),
        lambda kind, name: (RUN / "artifacts/bin" / (name.removesuffix(".dll").removesuffix(".pdb") if kind == "production" else TEST)
                            / "release" / name) if kind != "dependency" else RUN / "artifacts/bin" / TEST / "release" / name,
        lambda key, filename: Path(result["nativeFixtures"][key]["path"]),
        RUN / "artifacts/bin" / TEST / "release/line-ring-numeric-goldens.json", FIXTURE_RECORD)
    require(receipt_result["resultSha256"] == receipt["resultSha256AtExit"], "Result changed after handle exit")
    review = read(INTEGRATION / "independent-review.json")
    require(sha(INTEGRATION / "independent-review.json") == REVIEW_JSON_SHA
            and sha(INTEGRATION / "independent-review.md") == REVIEW_MD_SHA
            and review["blockingFindings"] == [] and review["sdkGoalComplete"] is False,
            "Independent static review changed")
    application = read(INTEGRATION / "main-application-record.json")
    require(sha(INTEGRATION / "main-application-record.json") == APPLICATION_SHA
            and sha(FIXTURE_RECORD) == FIXTURE_RECORD_SHA
            and application["appliedToMain"] is True and application["priorInputs"] == 881
            and application["newInputs"] == 885 and application["newValidationPassed"] is False
            and application["sdkGoalComplete"] is False, "Historical application record overclaims validation")
    return result, before, receipt


def make_plan(result: dict, inputs: dict[str, str], receipt_sha: str) -> Plan:
    plan = Plan()
    for name, digest in inputs.items():
        plan.add(ROOT / name, "compiler-inputs/" + name, digest)
    for original, (name, digest) in CONTROL_NAMES.items():
        plan.add(Path(original), name, digest)
    plan.add(INTEGRATION / "observe.py", "controls/observe.py", OBSERVER_SHA)
    plan.add(HELPER, "controls/input-scanner.py", HELPER_SHA)
    plan.add(OFFLINE, "controls/NuGet.Config", OFFLINE_SHA)
    plan.add(INTEGRATION / "main-application-record.json", "controls/main-application-record.json", APPLICATION_SHA)
    plan.add(INTEGRATION / "independent-review.json", "reviews/independent-review.json", REVIEW_JSON_SHA)
    plan.add(INTEGRATION / "independent-review.md", "reviews/independent-review.md", REVIEW_MD_SHA)
    plan.add(RECEIPT, "run/controller-handle-exit.json", receipt_sha)
    for name in ("result.json", "inputs-before.json", "inputs-after.json", "legacy-case-list.json"):
        plan.add(RUN / name, "run/" + name)
    for name in ALL_STEPS:
        for stream in ("stdout", "stderr"):
            plan.add(RUN / (name + "." + stream + ".log"), "run/logs/" + name + "." + stream + ".log")
    for name in ("validation-summary.json", "artifact-manifest.json"):
        plan.add(PRIOR / name, "prior-881/" + name)
    plan.add(PRIOR / "run/legacy-case-list.json", "prior-881/legacy-case-list.json")
    binary = RUN / "artifacts/bin"
    for component in SIX:
        for ext in (".dll", ".pdb"):
            plan.add(binary / component / "release" / (component + ext),
                     "binaries/production/" + component + ext, result["productsBeforeTests"][component][ext])
    for ext in (".dll", ".pdb"):
        plan.add(binary / TEST / "release" / (TEST + ext), "binaries/test/" + TEST + ext, result["testPair"][ext])
    for path, digest in result["dependencyCopies"].items():
        plan.add(Path(path), "binaries/test/dependencies/" + Path(path).name, digest)
    plan.add(binary / TEST / "release/line-ring-numeric-goldens.json",
             "fixtures/line-ring-numeric-goldens.json", GOLDEN_SHA)
    plan.add(FIXTURE_RECORD, "fixtures/native-source-record.json", FIXTURE_RECORD_SHA)
    for key, row in result["nativeFixtures"].items():
        plan.add(Path(row["path"]), "fixtures/native/" + key + "/" + Path(row["path"]).name, row["sha256"])
    plan.add(Path(__file__), "publisher/publish.py")
    summary = {
        "schema": "line-ring-target-provider-main-evidence/v1",
        "acceptedAtUtc": dt.datetime.now(dt.timezone.utc).isoformat(),
        "acceptedThisBatch": True, "compilerInputs": 885, "defaultExpectedCases": 372,
        "naturalSteps": 47, "focusedPassedCases": 72, "invalidSelectorChecks": 4,
        "sixReleaseWarnings": 0, "buildErrors": 0,
        "providerCases": 6, "chartOccurrenceCases": 5, "endpointCases": 6,
        "numericCases": 13, "ringXCases": 7, "sourceCases": 19, "basisCases": 16,
        "productionPairs": 6, "testPairs": 1, "dependencyFiles": 8, "nativeFixtures": 3,
        "legacyCasesListed": 300, "old300ExecutedThisBatch": False,
        "default372SingleProcessExecuted": False, "sdkGoalComplete": False,
        "completeSeekPermitted": False, "fullSeekPermitted": False,
        "gameStarted": False, "installed": False,
        "resultSha256": sha(RUN / "result.json"), "exitReceiptSha256": receipt_sha,
        "sourceGraphSha256": sha(RUN / "inputs-after.json"),
        "prior881SummarySha256": PRIOR_SUMMARY_SHA,
        "scope": "Offline retained source local target provider and chart occurrence incremental matrix; preceding 881 preview is separate evidence",
    }
    plan.generate("validation-summary.json", encoded(summary))
    plan.generate("README.md", (
        "# Retained line/ring target provider incremental evidence\n\n"
        "The frozen 885-input graph passed 47 scoped natural steps and 72 focused cases. "
        "The prior 881-input delivery is separate. The old 300 names were listed and compared, "
        "not executed here; default 372 was not run in one process. "
        "The original controller handle exit, 94 original logs, compiler inputs, DLL/PDB pairs, "
        "native fixtures and complete Single golden file are retained. "
        "This historical local projection does not enable complete Speed/Seek or the SDK goal.\n\n"
        "Recheck only archived bytes: python -B publisher/publish.py --check-only --archive <archive-dir>.\n"
    ).encode("utf-8"))
    return plan


def check_archive(path: Path) -> dict:
    raw_path = Path(path)
    require(raw_path.is_dir() and not raw_path.is_symlink(), "Missing or linked archive")
    path = raw_path.resolve()
    found = set()
    for item in path.rglob("*"):
        require(not item.is_symlink(), "Archive contains linked path")
        if item.is_file():
            found.add(safe_name(item.relative_to(path).as_posix()))
    manifest = read(path / "artifact-manifest.json")
    require(isinstance(manifest, dict) and len(found) <= MAX_FILES
            and found == set(manifest) | {"artifact-manifest.json"}, "Archive inventory differs")
    total = 0
    for name, digest in manifest.items():
        require(safe_name(name) == name and SHA_RE.fullmatch(digest)
                and sha(path / name) == digest, "Archived byte differs: " + name)
        total += (path / name).stat().st_size
    total += (path / "artifact-manifest.json").stat().st_size
    require(total <= MAX_BYTES, "Archive byte bound exceeded")
    summary = read(path / "validation-summary.json")
    require(summary["schema"] == "line-ring-target-provider-main-evidence/v1"
            and summary["acceptedThisBatch"] is True and summary["compilerInputs"] == 885
            and summary["naturalSteps"] == 47 and summary["focusedPassedCases"] == 72
            and summary["sixReleaseWarnings"] == 0 and summary["buildErrors"] == 0
            and summary["invalidSelectorChecks"] == 4 and summary["legacyCasesListed"] == 300
            and summary["productionPairs"] == 6 and summary["testPairs"] == 1
            and summary["dependencyFiles"] == 8 and summary["nativeFixtures"] == 3
            and summary["old300ExecutedThisBatch"] is False
            and summary["default372SingleProcessExecuted"] is False
            and summary["sdkGoalComplete"] is False and summary["completeSeekPermitted"] is False
            and summary["fullSeekPermitted"] is False and summary["gameStarted"] is False
            and summary["installed"] is False,
            "Archive summary scope differs")
    result_path = path / "run/result.json"; result = read(result_path)
    require(sha(result_path) == summary["resultSha256"]
            and sha(path / "run/controller-handle-exit.json") == summary["exitReceiptSha256"]
            and sha(path / "run/inputs-after.json") == summary["sourceGraphSha256"],
            "Archive summary hashes differ")
    check_receipt(path / "run/controller-handle-exit.json", summary["exitReceiptSha256"], result_path, result)
    require(sha(path / "controls/observe.py") == OBSERVER_SHA
            and sha(path / "controls/input-scanner.py") == HELPER_SHA
            and sha(path / "controls/NuGet.Config") == OFFLINE_SHA
            and sha(path / "controls/main-application-record.json") == APPLICATION_SHA
            and sha(path / "fixtures/native-source-record.json") == FIXTURE_RECORD_SHA
            and sha(path / "reviews/independent-review.json") == REVIEW_JSON_SHA
            and sha(path / "reviews/independent-review.md") == REVIEW_MD_SHA,
            "Archived reviewed controls differ")
    require(read(path / "reviews/independent-review.json")["blockingFindings"] == [],
            "Archived review has a blocker")
    application = read(path / "controls/main-application-record.json")
    require(application["appliedToMain"] is True and application["priorInputs"] == 881
            and application["newInputs"] == 885 and application["newValidationPassed"] is False
            and application["sdkGoalComplete"] is False, "Archived application record differs")
    evidence = check_result(result, result_path, path / "run/inputs-before.json", path / "run/inputs-after.json",
        lambda name: path / name,
        lambda name: path / "compiler-inputs" / safe_name(name),
        lambda name, stream: path / "run/logs" / (name + "." + stream + ".log"),
        path / "run/legacy-case-list.json",
        (path / "prior-881/validation-summary.json", path / "prior-881/artifact-manifest.json",
         path / "prior-881/legacy-case-list.json"),
        lambda kind, name: path / ("binaries/production/" if kind == "production" else
                                   "binaries/test/dependencies/" if kind == "dependency" else "binaries/test/") / name,
        lambda key, filename: path / "fixtures/native" / key / filename,
        path / "fixtures/line-ring-numeric-goldens.json", path / "fixtures/native-source-record.json")
    require(evidence["resultSha256"] == summary["resultSha256"]
            and evidence["sourceGraphSha256"] == summary["sourceGraphSha256"],
            "Archive independent evidence differs")
    required = {"compiler-inputs/" + name for name in read(path / "run/inputs-after.json")}
    required |= {"run/logs/" + name + "." + stream + ".log" for name in ALL_STEPS
                 for stream in ("stdout", "stderr")}
    required |= {"binaries/production/" + component + ext for component in SIX for ext in (".dll", ".pdb")}
    required |= {"binaries/test/" + TEST + ext for ext in (".dll", ".pdb")}
    required |= {"binaries/test/dependencies/" + Path(x).name for x in result["dependencyCopies"]}
    require(len(required) == 885 + 94 + 12 + 2 + 8,
            "Required compiler/log/binary inventory cardinality differs")
    required |= {"validation-summary.json", "README.md", "publisher/publish.py",
                 "run/result.json", "run/inputs-before.json", "run/inputs-after.json",
                 "run/legacy-case-list.json", "run/controller-handle-exit.json",
                 "prior-881/validation-summary.json", "prior-881/artifact-manifest.json",
                 "prior-881/legacy-case-list.json", "fixtures/line-ring-numeric-goldens.json",
                 "fixtures/native-source-record.json", "reviews/independent-review.json",
                 "reviews/independent-review.md", "controls/observe.py", "controls/input-scanner.py",
                 "controls/NuGet.Config", "controls/main-application-record.json"}
    required |= {name for name, _ in CONTROL_NAMES.values()}
    required |= {"fixtures/native/" + key + "/" + Path(row["path"]).name
                 for key, row in result["nativeFixtures"].items()}
    require(required <= found,
            "Archive required exact inventory is incomplete")
    return {"acceptedThisBatch": True, "archiveFiles": len(found), "archiveBytes": total,
            "summarySha256": sha(path / "validation-summary.json"), "sdkGoalComplete": False,
            "completeSeekPermitted": False}


def publish(receipt_sha: str) -> dict:
    require(not ARCHIVE.exists(), "Preserve existing accepted archive")
    result, inputs, _ = audit_current(receipt_sha)
    plan = make_plan(result, inputs, receipt_sha)
    staging = ARCHIVE.parent / (".line-ring-target-provider-staging-" + uuid.uuid4().hex)
    staging.mkdir()
    # A failed stage remains for diagnosis. Never merge into an accepted archive.
    for name, (source, digest, size) in plan.files.items():
        target = staging / name
        target.parent.mkdir(parents=True, exist_ok=True)
        with source.open("rb") as reader, target.open("xb") as writer:
            while block := reader.read(1024 * 1024):
                writer.write(block)
        require(target.stat().st_size == size and sha(target) == digest, "Staged byte differs: " + name)
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
    require(not ARCHIVE.exists(), "Concurrent archive appeared; staged evidence preserved")
    staging.rename(ARCHIVE)
    return check_archive(ARCHIVE)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check-only", action="store_true")
    parser.add_argument("--archive", type=Path, default=ARCHIVE)
    parser.add_argument("--exit-receipt-sha", type=str)
    args = parser.parse_args()
    if args.check_only:
        require(args.exit_receipt_sha is None, "Archive check is independent of external receipt")
        result = check_archive(args.archive)
    else:
        require(args.archive == ARCHIVE and args.exit_receipt_sha is not None
                and SHA_RE.fullmatch(args.exit_receipt_sha.lower()),
                "Publication requires fixed new path and independently pinned completed handle-exit SHA")
        result = publish(args.exit_receipt_sha.lower())
    print(json.dumps(result), flush=True)


if __name__ == "__main__":
    main()
