"""Publish only the finished 881-input endpoint-local incremental validation.

This script performs no build or test. It independently audits the terminal
controller result, its exact logs and bytes, then stages a bounded archive in
docs/validation and renames it into one new directory. Do not run it until the
root agent has reviewed the original exec completion and exit receipt.
"""
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import shutil
import sys
import uuid

HERE = Path(__file__).resolve().parent
EXPECTED_ROOT = Path("C:/Users/71957/Desktop/riz_toolbox").resolve()
ROOT = EXPECTED_ROOT
WORK = ROOT / "work/line-ring-endpoint-integration-20261113"
RUN = Path("D:/riz-build-cache-next/line-ring-endpoint-integration-20261113/main-r1")
ARCHIVE = ROOT / "docs/validation/line-ring-endpoint-local-20261113"
PRIOR = ROOT / "docs/validation/line-ring-target-primitives-20261113"
FIXTURE_RECORD = ROOT / "docs/validation/canvas-basis-shared-source-20261112/build-test-result.json"
HELPER = ROOT / "work/line-ring-source-validation-20261113/run_validation.py"
CAPTURE_TARGET = Path("D:/riz-build-cache-next/delivery-20261113-r1/Capture-FinalSixInputs.targets")
CSC_ROOT = RUN / "csc-inputs"
ARTIFACTS = RUN / "artifacts"
TEST = "RizToolbox.Judgements.Tests"
DOTNET = Path("C:/Program Files/dotnet/dotnet.exe")
OFFLINE = ROOT / "work/ring-x-numeric-20261113/NuGet.Config"
SIX = ["RizToolbox." + name for name in ("Sdk", "Core", "Runtime", "Adapter", "Hosting", "Loader")]
EXTENSIONS = (".dll", ".pdb")
SHA_RE = re.compile(r"[0-9a-f]{64}\Z")
MAX_FILES = 1200
MAX_BYTES = 512 * 1024 * 1024
MAX_FILE_BYTES = 64 * 1024 * 1024
RESULT_SHA = "3a45cd9bded930640375cc3a756d2c634c2c240962b60a0fa734940e87fbec02"
RECEIPT_SHA = "59d2687b3482ab943014b1d2f5b96bd16c2fa5ed2d6d5bf531dcdc60f4aedc9b"
VALIDATOR_SHA = "67c064736a289e6635ccf59128682741238632908050059297b7e1ff1d0e99ca"
HELPER_SHA = "61c2983ae117322b25ff96ad75b659b870fdadf9aada9274727248cd5ca98fd4"
CAPTURE_SHA = "9e18340a8e12a7aa9e991459b9e5175dfbb40b4e2966af6f2bf284ed19f3b7b4"
PRIOR_SUMMARY_SHA = "417532c108acfb463a109ae887389f50f8c4efb8e905e07e4d702ded99541eda"
PRIOR_MANIFEST_SHA = "235f24dd3a63825fe688aa8b6e07787e8748f5f382ba5f1e53fa97fd6534f5d1"
GOLDEN_SHA = "1bf6d52a1e1aacc01f2c0e92c951ed860f0137164860476888e473c7cea13a48"

TARGETS = {
    "src/RizToolbox.Adapter/Gameplay/NativeSeekLineEndpointLocalTarget.cs":
        "980264063b778394518869c94ec804e07f7b2f81d703d452d3287c6cf3a3a74b",
    "tests/RizToolbox.Judgements.Tests/LineRingNumeric/NativeSeekLineEndpointLocalChecks.cs":
        "cabf698b7ff6567d173cdc4906dde013d667a5d90c7b331c7440822767b1791e",
    "tests/RizToolbox.Judgements.Tests/Program.cs":
        "879d1802d27b02dda601430443c70ee0ff8e2c7f3997f5cf68ba0def11414409",
    "scripts/Build.ps1": "e9e08174d5a26cf8ab011bee95b7b514a64fd82247fb1bd52999b4842038ade9",
}
FROZEN = {
    "merge-manifest.json": "964d7f23b2161e888b26e883185cea717ff72f14b656e061937e6fc978fa4246",
    "payload.patch": "0b2da8f0b0849caac2c2c0b51fbefe9324fe03c4507ff8622a8e3d9950f4e606",
    "handoff.md": "406581f7069a04441c77642f095808c58fde1d0efd0dc1a4c9cbe55f82563092",
    "application-record.json": "df4e065445a891f5edf611686a771b9cf247be7eff3930f5d40547a0deae8347",
    "inputs-after.json": "3ba7e5b3a05af9c7f6d705bd370f777986563192934dc0a0be51bedd077542e8",
    "integrate_validate.py": VALIDATOR_SHA,
    "controller-exit-receipt.json": RECEIPT_SHA,
}
ENDPOINT_NAMES = [
    "Line ring numeric endpoint local slots and Single goldens",
    "Line ring numeric endpoint same-canvas binding remains unproven",
    "Line ring numeric endpoint baseline and canvas association",
    "Line ring numeric endpoint physical Transform aliases",
    "Line ring numeric endpoint occurrence and duplicate index guards",
    "Line ring numeric endpoint guard reentry and recovery",
]
STEP_NAMES = ["six-release", "solution", "legacy-listing", "endpoint-local", "numeric", "ring-x",
              "canvas-owned-source-17", "canvas-owned-source-18", "canvas-owned-source-19",
              "invalid-endpoint-name", "invalid-endpoint-arity"]


def require(ok: bool, reason: str) -> None:
    if not ok:
        raise RuntimeError(reason)


def safe_name(name: str) -> str:
    require(isinstance(name, str) and name and "\\" not in name and not name.startswith("/"), "Unsafe archive name")
    require(all(part not in ("", ".", "..") and ":" not in part for part in name.split("/")),
            "Unsafe archive name: " + name)
    return name


def safe_file(path: Path) -> Path:
    path = Path(path)
    require(path.is_file(), "Missing evidence file: " + str(path))
    require(not any(part.is_symlink() for part in (path, *path.parents)), "Symlink in evidence path: " + str(path))
    require(path.stat().st_size <= MAX_FILE_BYTES, "Oversized evidence file: " + str(path))
    return path


def sha(path: Path) -> str:
    with safe_file(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def read(path: Path):
    return json.loads(safe_file(path).read_text(encoding="utf-8-sig"))


def encoded(value) -> bytes:
    return (json.dumps(value, ensure_ascii=False, indent=2) + "\n").encode("utf-8")


def root_control_name(path: Path) -> str:
    path = Path(path)
    if path.is_relative_to(ROOT):
        return "controls/root/" + safe_name(path.relative_to(ROOT).as_posix())
    require(path == CAPTURE_TARGET, "Unexpected external control: " + str(path))
    return "controls/external/Capture-FinalSixInputs.targets"


def expected_controls() -> dict[str, str]:
    return {**{str(WORK / name): digest for name, digest in FROZEN.items() if name != "controller-exit-receipt.json"},
            str(ROOT / "scripts/Build.ps1"): TARGETS["scripts/Build.ps1"],
            str(CAPTURE_TARGET): CAPTURE_SHA}


def check_prior_archive() -> dict:
    require(sha(PRIOR / "validation-summary.json") == PRIOR_SUMMARY_SHA
            and sha(PRIOR / "artifact-manifest.json") == PRIOR_MANIFEST_SHA,
            "Accepted 879 archive controls changed")
    summary = read(PRIOR / "validation-summary.json")
    require(summary["acceptedThisBatch"] is True and summary["compilerInputs"] == 879
            and summary["naturalSteps"] == 362 and summary["legacyCases"] == 300
            and summary["sdkGoalComplete"] is False and summary["completeSeekPermitted"] is False,
            "Prior 879 archive is not the accepted matrix")
    manifest = read(PRIOR / "artifact-manifest.json")
    require(isinstance(manifest, dict) and len(manifest) <= 1500, "Prior archive manifest invalid")
    files = {path.relative_to(PRIOR).as_posix() for path in PRIOR.rglob("*") if path.is_file()}
    require(files == set(manifest) | {"artifact-manifest.json"}, "Prior archive inventory changed")
    for name, digest in manifest.items():
        require(SHA_RE.fullmatch(digest) and sha(PRIOR / safe_name(name)) == digest,
                "Prior archive byte changed: " + name)
    require(summary["resultSha256"] == manifest["run/result.json"], "Prior result mismatch")
    return summary


def check_receipt(result: dict) -> None:
    path = WORK / "controller-exit-receipt.json"
    require(sha(path) == RECEIPT_SHA, "Root exit receipt bytes changed")
    receipt = read(path)
    require(receipt == {
        "schema": "root-exec-observed-controller-exit/v1",
        "observationKind": "unified exec tool completion, original session retained",
        "sessionId": 13012, "toolChunkId": "da38d7", "controllerPid": 11764,
        "actualExitCode": 0, "observedUtc": "2026-11-13T06:46:24.031304+00:00",
        "resultSha256": RESULT_SHA, "controllerScriptSha256": VALIDATOR_SHA,
        "kernelHandleWitness": False, "sdkGoalComplete": False, "completeSeekPermitted": False,
    }, "Root exit receipt differs from original exec completion")
    require(dt.datetime.fromisoformat(receipt["observedUtc"]) >= dt.datetime.fromisoformat(result["finishedUtc"]),
            "Exit receipt precedes terminal result")


def load_input_helper():
    require(sha(HELPER) == HELPER_SHA, "Input scanner changed")
    spec = importlib.util.spec_from_file_location("endpoint_evidence_input_helper", HELPER)
    require(spec is not None and spec.loader is not None, "Input scanner unavailable")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def check_current_inputs(result: dict) -> dict[str, str]:
    before = read(RUN / "inputs-before.json")
    after = read(RUN / "inputs-after.json")
    require(before == after == read(WORK / "inputs-after.json") and len(before) == 881,
            "Original 881 compiler input snapshots differ")
    require(load_input_helper().inputs() == before, "Current 881 compiler inputs changed")
    for name, digest in before.items():
        require(safe_name(name) == name and sha(ROOT / name) == digest,
                "Current compiler input changed: " + name)
    for name, digest in TARGETS.items():
        require(sha(ROOT / name) == digest, "Applied endpoint target or Build control changed: " + name)
        if name != "scripts/Build.ps1":
            require(before[name] == digest, "Endpoint target absent from 881 graph: " + name)
    manifest = read(WORK / "merge-manifest.json")
    require(len(manifest["files"]) == 4 and {item["target"]: item["payloadSha256"] for item in manifest["files"]} == TARGETS,
            "Frozen four-target manifest differs")
    application = read(WORK / "application-record.json")
    require(application["appliedToMain"] is True and application["newInputCount"] == 881
            and application["newFullMatrixExecuted"] is False
            and application["newIncrementalValidationPassed"] is False,
            "Historical application record changed or overclaims validation")
    require(result["compilerInputs"] == len(before), "Result compiler count differs")
    return before


def build_arguments(name: str) -> list[str]:
    project = ROOT / ("src/RizToolbox.Loader/RizToolbox.Loader.csproj" if name == "six-release" else "RizToolbox.sln")
    args = [str(DOTNET), "build", str(project), "-c", "Release", "--artifacts-path", str(ARTIFACTS),
            "--verbosity", "minimal", "--disable-build-servers", "-m:1",
            "-p:UseSharedCompilation=false", "-p:NuGetAudit=false", "-p:RestoreConfigFile=" + str(OFFLINE)]
    if name == "six-release":
        args += ["--no-incremental", "-p:ProvideCommandLineArgs=true", "-p:SkipCompilerExecution=false",
                 "-p:CustomAfterMicrosoftCommonTargets=" + str(CAPTURE_TARGET),
                 "-p:BuildProjectReferences=true", "-p:DebugType=portable", "-p:ChecksumAlgorithm=SHA256",
                 "-p:RizCompilationStage=graph", "-p:RizCaptureRoot=" + str(CSC_ROOT),
                 "-p:RizEngineProductionProfileSource=", "-p:RizEngineTransportProfileSource="]
    return args


def step_specs() -> dict[str, tuple[list[str], int, int | None, str | None]]:
    assembly = ARTIFACTS / "bin" / TEST / "release" / (TEST + ".dll")
    prefix = [str(DOTNET), str(assembly)]
    specs = {
        "six-release": (build_arguments("six-release"), 0, None, None),
        "solution": (build_arguments("solution"), 0, None, None),
        "legacy-listing": (prefix + ["--list-legacy-cases"], 0, None, None),
        "endpoint-local": (prefix + ["--line-ring-endpoint-local-checks"], 0, 6, "Line ring numeric endpoint "),
        "numeric": (prefix + ["--line-ring-numeric-checks"], 0, 13, "Line ring numeric "),
        "ring-x": (prefix + ["--line-ring-ring-x-numeric-checks"], 0, 7, "Line ring numeric ring "),
        **{f"canvas-owned-source-{i}": (prefix + ["--line-ring-source-case", str(i)], 0, 1,
                                             "Line ring source ") for i in (17, 18, 19)},
        "invalid-endpoint-name": (prefix + ["--line-ring-endpoint-local-check"], 2, None, None),
        "invalid-endpoint-arity": (prefix + ["--line-ring-endpoint-local-checks", "extra"], 2, None, None),
    }
    require(list(specs) == STEP_NAMES, "Publisher step specification differs")
    return specs


def check_build_log(name: str, body: str) -> None:
    warnings = 0 if name == "six-release" else 56
    require(len(re.findall(r"(?m)^已成功生成。$", body)) == 1
            and re.findall(r"(?m)^\s*(\d+) 个警告\s*$", body) == [str(warnings)]
            and re.findall(r"(?m)^\s*(\d+) 个错误\s*$", body) == ["0"],
            "Build success/count summary differs: " + name)
    if name == "six-release":
        names = re.findall(r"(?m)^  (RizToolbox\.[A-Za-z]+) -> .+\\(RizToolbox\.[A-Za-z]+)\.dll$", body)
        require(len(names) == 6 and {left for left, right in names} == set(SIX)
                and all(left == right for left, right in names), "Six production outputs missing from build log")


def check_steps(result: dict, log_file, listing_file, program_file,
                prior_listing_file, prior_log_file) -> list[dict]:
    specs = step_specs()
    steps = result["steps"]
    require(len(steps) == 11 and [row["name"] for row in steps] == STEP_NAMES,
            "Exact 11-step order or count differs")
    usage = re.findall(r'Console\.Error\.WriteLine\("(Usage: [^"\r\n]+)"\);',
                       safe_file(program_file).read_text(encoding="utf-8"))
    require(len(usage) == 1 and "--line-ring-endpoint-local-checks" in usage[0], "Program usage differs")
    listing = read(listing_file)
    require(isinstance(listing, list) and len(listing) == 300
            and [row["index"] for row in listing] == list(range(1, 301))
            and len({row["name"] for row in listing}) == 300
            and all(not row["name"].startswith("Line ring numeric endpoint ") for row in listing)
            and listing == read(prior_listing_file),
            "Legacy 300 listing changed; prior archive is separate evidence")
    for row in steps:
        name = row["name"]
        argv, expected_exit, total, prefix = specs[name]
        require(row["arguments"] == argv and row["exitCode"] == expected_exit
                and row["naturalExit"] is True and row["matchesExpected"] is True,
                "Step argv or natural expected exit differs: " + name)
        log = log_file(name)
        require(sha(log) == row["logSha256"], "Step log hash differs: " + name)
        body = log.read_text(encoding="utf-8-sig")
        runs = re.findall(r"(?m)^RUN (.+)$", body)
        passes = re.findall(r"(?m)^PASS (.+)$", body)
        totals = re.findall(r"(\d+)/(\d+) passed", body)
        require(not re.search(r"(?m)^FAIL\b", body) and row["passNames"] == passes,
                "Recorded PASS lines or failure differ: " + name)
        if name in ("six-release", "solution"):
            require(not runs and not passes and not totals, "Build log contains test output")
            check_build_log(name, body)
        elif name == "legacy-listing":
            require(not runs and not passes and not totals and json.loads(body) == listing,
                    "Legacy listing log differs")
        elif name.startswith("invalid-"):
            require(not runs and not passes and not totals and body == usage[0] + "\n",
                    "Invalid endpoint selector does not print exact usage")
        else:
            require(totals == [(str(total), str(total))] and len(passes) == total
                    and runs == passes and len(set(passes)) == total
                    and all(value.startswith(prefix) for value in passes),
                    "Selector RUN/PASS/totals differ: " + name)
            if name == "endpoint-local":
                require(passes == ENDPOINT_NAMES, "Six endpoint-local registry cases differ")
            elif name == "numeric":
                require(passes == re.findall(r"(?m)^PASS (.+)$",
                    prior_log_file("numeric-13").read_text(encoding="utf-8-sig")),
                    "Numeric 13 names differ from accepted source")
            elif name == "ring-x":
                require(passes == re.findall(r"(?m)^PASS (.+)$",
                    prior_log_file("ring-x-7").read_text(encoding="utf-8-sig")),
                    "Ring X 7 names differ from accepted source")
            else:
                case = name.rsplit("-", 1)[1]
                require(passes == re.findall(r"(?m)^PASS (.+)$",
                    prior_log_file("source-" + case).read_text(encoding="utf-8-sig")),
                    "Source case name differs from accepted source: " + name)
    return listing


def check_result_meta(result: dict) -> None:
    require(result.get("finishedUtc") and result.get("passed") is True
            and result.get("failure") in (None, "") and result.get("activeChild") is None
            and result["controllerPid"] == 11764 and result["compilerInputs"] == 881
            and result["defaultExpectedCases"] == 361
            and result["scope"] == "Incremental endpoint-local class and test registration; prior 879 full matrix is separate evidence",
            "Controller has no exact successful terminal result")
    for key in ("old300ExecutedThisBatch", "sdkGoalComplete", "completeSeekPermitted"):
        require(result[key] is False, "Controller scope overclaims " + key)
    for key in ("compiledBytesUnchanged", "actualCscCaptureUnchanged", "inputsUnchanged", "controlsUnchanged"):
        require(result[key] is True, "Controller terminal invariant false: " + key)
    require(result["controlsBefore"] == expected_controls(), "Controller frozen control map differs")
    require(len(result["steps"]) == 11 and [row["name"] for row in result["steps"]] == STEP_NAMES,
            "Controller step inventory differs")


def check_controls(result: dict, control_file) -> None:
    require(result["controlsBefore"] == expected_controls(), "Recorded controls differ")
    for path, digest in result["controlsBefore"].items():
        require(sha(control_file(Path(path))) == digest, "Control byte differs: " + path)


def check_products(result: dict, production_file, test_file, dependency_file,
                   golden_file, fixture_file) -> None:
    products = result["productsBefore"]
    require(products == result["productsAfterSix"] == result["productsAfter"]
            and set(products) == set(SIX)
            and all(set(values) == set(EXTENSIONS) for values in products.values()),
            "Twelve production hashes or before/after equality differ")
    for component, values in products.items():
        for ext, digest in values.items():
            require(SHA_RE.fullmatch(digest) and sha(production_file(component, ext)) == digest,
                    "Production DLL/PDB changed: " + component + ext)
    pair = result["testAssemblyBefore"]
    require(set(pair) == set(EXTENSIONS), "Test DLL/PDB pair differs")
    for ext, digest in pair.items():
        require(sha(test_file(ext)) == digest, "Judgements test DLL/PDB changed")
    dependencies = result["dependencyCopies"]
    expected = {component + ext: products[component][ext] for component in SIX[:4] for ext in EXTENSIONS}
    require(len(dependencies) == 8 and {Path(path).name: digest for path, digest in dependencies.items()} == expected,
            "Eight test dependency identities differ")
    for name, digest in expected.items():
        require(sha(dependency_file(name)) == digest, "Test dependency changed: " + name)
    require(sha(golden_file) == GOLDEN_SHA, "Numeric golden copy changed")
    fixtures = read(FIXTURE_RECORD)["nativeFixtures"]
    require(result["nativeFixtures"] == fixtures and len(fixtures) == 3,
            "Three pinned native fixture identities differ")
    for key, row in fixtures.items():
        require(sha(fixture_file(key, Path(row["path"]).name)) == row["sha256"],
                "Native fixture changed: " + key)


CSC_NAMES = {"additional.sha256", "analyzers.sha256", "compile.sha256", "csc-command-line.txt",
             "editorconfigs.sha256", "imports.sha256", "project-references.txt", "properties.txt",
             "reference-configuration.txt", "references.sha256", "resolved-references.sha256"}


def check_capture(result: dict, capture_file, actual_inventory: set[str]) -> None:
    recorded = result["actualCscCaptureFiles"]
    require(len(recorded) == 66, "Six real Csc capture file groups differ")
    expected: dict[str, str] = {}
    for path, digest in recorded.items():
        source = Path(path)
        require(source.is_relative_to(CSC_ROOT) and SHA_RE.fullmatch(digest),
                "Unexpected Csc capture path or digest")
        name = safe_name(source.relative_to(CSC_ROOT).as_posix())
        expected[name] = digest
    require(len(expected) == 66 and set(expected) == {component + "/" + name
            for component in SIX for name in CSC_NAMES}
            and actual_inventory == set(expected), "Csc capture inventory differs")
    for name, digest in expected.items():
        require(sha(capture_file(name)) == digest, "Actual Csc capture bytes changed: " + name)
    adapter_compile = capture_file("RizToolbox.Adapter/compile.sha256").read_text(encoding="utf-8-sig")
    target = "NativeSeekLineEndpointLocalTarget.cs|" + TARGETS[
        "src/RizToolbox.Adapter/Gameplay/NativeSeekLineEndpointLocalTarget.cs"].upper()
    require(sum(line.endswith(target) for line in adapter_compile.splitlines()) == 1,
            "Actual Adapter compiler input omitted exact endpoint source")


def check_endpoint_source(source_file) -> None:
    body = safe_file(source_file).read_text(encoding="utf-8")
    require(len(re.findall(r"(?m)^\s*(?:Check|Reject<[^>]+>)\(", body)) == 19
            and all(body.count('("' + name + '"') == 1 for name in ENDPOINT_NAMES),
            "Six endpoint cases or 19 model assertion calls differ")


class Plan:
    def __init__(self):
        self.files: dict[str, tuple[Path, str, int]] = {}
        self.generated: dict[str, bytes] = {}
        self.total = 0

    def add(self, path: Path, name: str, digest: str | None = None) -> None:
        name = safe_name(name)
        require(name not in self.files and name not in self.generated and name != "artifact-manifest.json",
                "Evidence path collision: " + name)
        source = safe_file(path)
        actual = sha(source)
        require(digest is None or actual == digest, "Source SHA differs: " + name)
        size = source.stat().st_size
        self.total += size
        require(len(self.files) + len(self.generated) + 2 <= MAX_FILES and self.total <= MAX_BYTES,
                "Evidence file or byte bound exceeded")
        self.files[name] = (source, actual, size)

    def generate(self, name: str, body: bytes) -> None:
        name = safe_name(name)
        require(name not in self.files and name not in self.generated and name != "artifact-manifest.json",
                "Evidence path collision: " + name)
        self.total += len(body)
        require(len(self.files) + len(self.generated) + 2 <= MAX_FILES and self.total <= MAX_BYTES,
                "Evidence byte bound exceeded")
        self.generated[name] = body

    def verify_sources(self) -> None:
        for name, (path, digest, size) in self.files.items():
            require(path.stat().st_size == size and sha(path) == digest,
                    "Evidence source changed during staging: " + name)


def current_capture_inventory() -> set[str]:
    require(CSC_ROOT.is_dir() and not CSC_ROOT.is_symlink(), "Current Csc capture root absent")
    files = set()
    for path in CSC_ROOT.rglob("*"):
        require(not path.is_symlink(), "Csc capture contains a symlink")
        if path.is_file():
            files.add(safe_name(path.relative_to(CSC_ROOT).as_posix()))
    return files


def audit_current() -> tuple[dict, dict[str, str]]:
    require(ROOT.resolve() == EXPECTED_ROOT
            and HERE == ROOT / "work/line-ring-endpoint-evidence-20261113",
            "Publisher is outside the authorized workspace")
    require(sha(RUN / "result.json") == RESULT_SHA, "Terminal result hash differs")
    result = read(RUN / "result.json")
    check_result_meta(result)
    check_receipt(result)
    check_prior_archive()
    for name, digest in FROZEN.items():
        require(sha(WORK / name) == digest, "Frozen integration control changed: " + name)
    check_controls(result, lambda path: path)
    inputs = check_current_inputs(result)
    check_endpoint_source(ROOT / "tests/RizToolbox.Judgements.Tests/LineRingNumeric/NativeSeekLineEndpointLocalChecks.cs")
    check_steps(result, lambda name: RUN / (name + ".log"), RUN / "legacy-case-list.json",
                ROOT / "tests/RizToolbox.Judgements.Tests/Program.cs",
                PRIOR / "run/legacy-case-list.json", lambda name: PRIOR / "run/logs" / (name + ".log"))
    binary = ARTIFACTS / "bin"
    check_products(result,
        lambda component, ext: binary / component / "release" / (component + ext),
        lambda ext: binary / TEST / "release" / (TEST + ext),
        lambda name: binary / TEST / "release" / name,
        binary / TEST / "release/line-ring-numeric-goldens.json",
        lambda key, filename: Path(result["nativeFixtures"][key]["path"]))
    check_capture(result, lambda name: CSC_ROOT / name, current_capture_inventory())
    require(sha(CAPTURE_TARGET) == CAPTURE_SHA, "Actual Csc capture target changed")
    return result, inputs


def make_plan(result: dict, inputs: dict[str, str]) -> Plan:
    plan = Plan()
    for name, digest in inputs.items():
        plan.add(ROOT / name, "compiler-inputs/" + name, digest)
    for path, digest in result["controlsBefore"].items():
        plan.add(Path(path), root_control_name(Path(path)), digest)
    plan.add(WORK / "controller-exit-receipt.json", "run/controller-exit-receipt.json", RECEIPT_SHA)
    for name in ("result.json", "inputs-before.json", "inputs-after.json", "legacy-case-list.json"):
        plan.add(RUN / name, "run/" + name)
    for name in STEP_NAMES:
        plan.add(RUN / (name + ".log"), "run/logs/" + name + ".log")
    for name in ("validation-summary.json", "artifact-manifest.json"):
        plan.add(PRIOR / name, "prior-879/" + name)
    plan.add(PRIOR / "run/legacy-case-list.json", "prior-879/legacy-case-list.json")
    for name in ("numeric-13", "ring-x-7", "source-17", "source-18", "source-19"):
        plan.add(PRIOR / "run/logs" / (name + ".log"), "prior-879/logs/" + name + ".log")
    for component in SIX:
        for ext in EXTENSIONS:
            plan.add(ARTIFACTS / "bin" / component / "release" / (component + ext),
                     "binaries/production/" + component + ext, result["productsBefore"][component][ext])
    for ext in EXTENSIONS:
        plan.add(ARTIFACTS / "bin" / TEST / "release" / (TEST + ext),
                 "binaries/test/" + TEST + ext, result["testAssemblyBefore"][ext])
    for path, digest in result["dependencyCopies"].items():
        plan.add(Path(path), "binaries/test/dependencies/" + Path(path).name, digest)
    plan.add(ARTIFACTS / "bin" / TEST / "release/line-ring-numeric-goldens.json",
             "fixtures/line-ring-numeric-goldens.json", GOLDEN_SHA)
    plan.add(FIXTURE_RECORD, "fixtures/source-record.json")
    for key, row in result["nativeFixtures"].items():
        plan.add(Path(row["path"]), "fixtures/native/" + key + "/" + Path(row["path"]).name, row["sha256"])
    for path, digest in result["actualCscCaptureFiles"].items():
        plan.add(Path(path), "csc-inputs/" + Path(path).relative_to(CSC_ROOT).as_posix(), digest)
    for name in ("root-integration-review.json",):
        plan.add(WORK / name, "reviews/" + name)
    plan.add(ROOT / "work/line-ring-endpoint-binding-20261113/independent-root-review.json",
             "reviews/original-core-review.json")
    plan.add(Path(__file__), "publisher/publish.py")
    summary = {
        "schema": "line-ring-endpoint-local-main-evidence/v1",
        "acceptedAtUtc": dt.datetime.now(dt.timezone.utc).isoformat(),
        "acceptedThisBatch": True, "compilerInputs": 881, "defaultExpectedCases": 361,
        "naturalSteps": 11, "endpointCases": 6, "endpointModelAssertions": 19,
        "numericCases": 13, "ringXCases": 7, "canvasOwnedSourceCases": 3,
        "focusedPassedCases": 29, "invalidSelectorChecks": 2,
        "sixReleaseWarnings": 0, "solutionWarnings": 56, "solutionErrors": 0,
        "productionPairs": 6, "testPairs": 1, "dependencyFiles": 8, "nativeFixtures": 3,
        "actualCscCaptureFiles": 66,
        "resultSha256": RESULT_SHA, "rootExitReceiptSha256": RECEIPT_SHA,
        "sourceGraphSha256": sha(RUN / "inputs-after.json"),
        "prior879SummarySha256": PRIOR_SUMMARY_SHA,
        "old300ExecutedThisBatch": False, "default361SingleProcessExecuted": False,
        "scope": "Incremental endpoint local calculation and adjacent checks on exact final 881 graph; preceding full 879 matrix remains separate evidence",
        "sdkGoalComplete": False, "completeSeekPermitted": False, "fullSeekPermitted": False,
        "gameStarted": False, "installed": False,
    }
    plan.generate("validation-summary.json", encoded(summary))
    plan.generate("README.md", (
        "# Endpoint local incremental validation\n\n"
        "Accepted only for the frozen 881-input source graph and copied production bytes. "
        "Six Release components and the solution built successfully; 29 focused cases and two invalid-selector checks passed. "
        "The exact original exec completion, immutable controller receipt, eleven logs, input snapshots, "
        "12 production DLL/PDB files, test DLL/PDB pair, eight dependency copies and 66 real Csc capture files are retained.\n\n"
        "The preceding 879-input full matrix is separate evidence. The original 300 cases were listed, "
        "not executed in this incremental batch; default 361 single-process execution is not claimed. "
        "The endpoint model has no existing native caller and does not enable full Speed/Seek or production admission. "
        "No game was installed or started.\n\n"
        "Recheck archive hashes: python -B publisher/publish.py --check-only --archive <this-directory>. "
        "Reproduction of the original compiler commands also requires the pinned local SDK and referenced files, "
        "whose original paths are preserved in Csc capture; the archive does not claim to bundle the toolchain.\n"
    ).encode("utf-8"))
    return plan


def check_archive(path: Path) -> dict:
    path = path.resolve()
    require(path.is_dir() and not path.is_symlink(), "Missing or linked archive")
    manifest = read(path / "artifact-manifest.json")
    require(isinstance(manifest, dict) and len(manifest) + 1 <= MAX_FILES, "Archive manifest bound exceeded")
    files = {p.relative_to(path).as_posix() for p in path.rglob("*") if p.is_file()}
    require(files == set(manifest) | {"artifact-manifest.json"}, "Archive inventory differs")
    total = 0
    for name, digest in manifest.items():
        require(SHA_RE.fullmatch(digest) and sha(path / safe_name(name)) == digest,
                "Archived bytes changed: " + name)
        total += (path / name).stat().st_size
    require(total <= MAX_BYTES, "Archive size bound exceeded")
    summary = read(path / "validation-summary.json")
    require(summary["acceptedThisBatch"] is True and summary["compilerInputs"] == 881
            and summary["naturalSteps"] == 11 and summary["focusedPassedCases"] == 29
            and summary["old300ExecutedThisBatch"] is False and summary["sdkGoalComplete"] is False
            and summary["completeSeekPermitted"] is False, "Archive scope differs")
    require(sha(path / "run/result.json") == RESULT_SHA
            and sha(path / "run/controller-exit-receipt.json") == RECEIPT_SHA,
            "Archived controller or exit receipt differs")
    result = read(path / "run/result.json")
    check_result_meta(result)
    require(read(path / "run/inputs-before.json") == read(path / "run/inputs-after.json"),
            "Archived input snapshots differ")
    inputs = read(path / "run/inputs-after.json")
    require(len(inputs) == 881, "Archived input count differs")
    for name, digest in inputs.items():
        require(sha(path / "compiler-inputs" / safe_name(name)) == digest, "Archived compiler input changed")
    check_controls(result, lambda source: path / root_control_name(source))
    check_steps(result, lambda name: path / "run/logs" / (name + ".log"),
                path / "run/legacy-case-list.json", path / "compiler-inputs/tests/RizToolbox.Judgements.Tests/Program.cs",
                path / "prior-879/legacy-case-list.json", lambda name: path / "prior-879/logs" / (name + ".log"))
    check_capture(result, lambda name: path / "csc-inputs" / name,
                  {p.relative_to(path / "csc-inputs").as_posix() for p in (path / "csc-inputs").rglob("*") if p.is_file()})
    for component, values in result["productsBefore"].items():
        for ext, digest in values.items():
            require(sha(path / "binaries/production" / (component + ext)) == digest, "Archived production byte differs")
    for ext, digest in result["testAssemblyBefore"].items():
        require(sha(path / "binaries/test" / (TEST + ext)) == digest, "Archived test byte differs")
    for source, digest in result["dependencyCopies"].items():
        require(sha(path / "binaries/test/dependencies" / Path(source).name) == digest, "Archived dependency differs")
    return {"acceptedThisBatch": True, "archiveFiles": len(files), "bytes": total,
            "summarySha256": sha(path / "validation-summary.json"), "sdkGoalComplete": False,
            "completeSeekPermitted": False}


def publish() -> dict:
    require(not ARCHIVE.exists(), "Preserve existing accepted archive")
    result, inputs = audit_current()
    plan = make_plan(result, inputs)
    staging = ARCHIVE.parent / (".endpoint-local-staging-" + uuid.uuid4().hex)
    staging.mkdir()
    # Failed staging is preserved for diagnosis, never merged into an accepted directory.
    for name, (source, digest, size) in plan.files.items():
        target = staging / name
        target.parent.mkdir(parents=True, exist_ok=True)
        with target.open("xb") as stream:
            stream.write(source.read_bytes())
        require(target.stat().st_size == size and sha(target) == digest, "Staged byte copy differs")
    for name, body in plan.generated.items():
        target = staging / name
        target.parent.mkdir(parents=True, exist_ok=True)
        with target.open("xb") as stream:
            stream.write(body)
    manifest = {name: digest for name, (_, digest, _) in plan.files.items()}
    manifest.update({name: hashlib.sha256(body).hexdigest() for name, body in plan.generated.items()})
    (staging / "artifact-manifest.json").write_bytes(encoded(manifest))
    plan.verify_sources()
    check_archive(staging)
    require(not ARCHIVE.exists(), "Concurrent archive appeared; staged evidence preserved")
    staging.rename(ARCHIVE)
    return check_archive(ARCHIVE)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check-only", action="store_true")
    parser.add_argument("--check-current", action="store_true")
    parser.add_argument("--archive", type=Path, default=ARCHIVE)
    args = parser.parse_args()
    if args.check_only:
        result = check_archive(args.archive)
        if args.check_current:
            audit_current()
    else:
        require(not args.check_current and args.archive == ARCHIVE, "Publication uses exact fixed new path")
        result = publish()
    print(json.dumps(result), flush=True)


if __name__ == "__main__":
    main()
