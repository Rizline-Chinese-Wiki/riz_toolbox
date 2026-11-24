"""Archive only the completed, fresh 879-input line/ring primitives matrix.

No build, test, game, network, environment, or prior run is changed. Publication
is a bounded copy into a new sibling directory followed by one directory rename.
"""
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import importlib.util
import json
import re
import shutil
import sys
import uuid
from pathlib import Path


HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
EXPECTED_ROOT = Path(r"C:\Users\71957\Desktop\riz_toolbox").resolve()
WORK = ROOT / "work/line-ring-target-primitives-integration-20261113"
RUN = Path(r"D:\riz-build-cache-next\line-ring-target-primitives-integration-20261113\main-r1")
OLD_RUN = Path(r"D:\riz-build-cache-next\line-ring-numeric-integration-20261113\recovery-r1")
OLD_ORIGINAL = OLD_RUN.parent / "main-r1"
ARCHIVE = ROOT / "docs/validation/line-ring-target-primitives-20261113"
DOTNET = Path(r"C:\Program Files\dotnet\dotnet.exe")
OFFLINE = ROOT / "work/ring-x-numeric-20261113/NuGet.Config"
SIX = ["RizToolbox." + x for x in ("Sdk", "Core", "Runtime", "Adapter", "Hosting", "Loader")]
TESTS = ["RizToolbox." + x + ".Tests" for x in ("Judgements", "Runtime", "AudioContent")]
SHA_RE = re.compile(r"[0-9a-f]{64}\Z")
MAX_FILES, MAX_BYTES, MAX_FILE_BYTES = 1500, 512 * 1024 * 1024, 64 * 1024 * 1024

FROZEN = {
    "prepare.py": "367c77cb0243f80cf922ef8ae9b67a6a7d6c61c051320d2f97fb2dcc6b7873e6",
    "apply.py": "abb7c652b87757aac1f50a938dba5371a30b639709fe5beb7db878aa44c80658",
    "validate_main.py": "fd9d4dc720395efe435760c214983afd2b0067395d1463e94039cf0c74783b73",
    "patch-manifest.json": "cd750fa21b36989cda8d6fdd1cfd56d8627d7325ea6f3c17021f2c21f529abac",
    "integration.patch": "ede89745777ca3a455296cb8ddaec5b9acea6efe3c2f0afbe9cdc13d1383cadc",
    "before-compiler-inputs.json": "2d3c53b6fe5a1f27f75bc95db4a27c343973c31f67ccf8a9056f92fb8584b703",
    "expected-after-compiler-inputs.json": "d56802e34a2f1bfd427ce4056e09c93b3c552cbcf4f45332231c84b8f85cbf63",
    "main-inputs-after.json": "d56802e34a2f1bfd427ce4056e09c93b3c552cbcf4f45332231c84b8f85cbf63",
    "superseded-874-stop.json": "15fc95f56699649ddb2bc96f8b6aa5e1ad92b14c53058bd68d68be002d5a0e51",
    "independent-controls-review.json": "c38006cf72ec5825de0bcd127146352e622d70fecb18d7415035caebb7379111",
    "root-review-scope-clarification.json": "560ca8e9ea570c73c5600e888171be1e9f708bb22d7f11e953691b1f8325fce3",
}
CANVAS_REVIEW_SHA = "a48a2427764b40402ab9a127a7f020d3d3dc491384048a45c24baf0df4c0fb3a"
CANVAS_ROOT_REVIEW_SHA = "92c6a95cef4400c605b32b5ec36220456d261f0b1108e5f7c3e94a2e4154a61d"
RING_REVIEW_SHA = "b21099cb2b908272fe0674c20eaa6554beda9d61c736693c7f0bc36ae021ceb1"
BUILD_SHA = "f9e008d3c1d1f4f9a42bc00e0c5770104823ce41101b5bca07cb47d00c0b9011"
GOLDEN_SHA = "1bf6d52a1e1aacc01f2c0e92c951ed860f0137164860476888e473c7cea13a48"
RING_GOLDEN_SHA = "3ce67eb819676dac687488c8ac81b5efdc5c451c52673d7b82b314fad44fe5bb"
RING_ORACLE_SHA = "29c015661f0bf0817c67b06221d4ac4438ddb8a77c3fcd1a04606a613e6bc86e"
BASELINE_SHA = "1ee2fc9a5b905a607e2d6969df11dfc943ac01e89a3f6b8cd4c059333d95a0aa"
HELPER_SHA = "61c2983ae117322b25ff96ad75b659b870fdadf9aada9274727248cd5ca98fd4"
APPLICATION_SHA = "15163d8db55dce4ac3edcc305aee4a41d178c997fbbc763437249636f402218a"
OLD_RESULT_SHA = "ffe9eab0a9e4f1cda0c22bcb65a64e26a8b730fb2eb3c382c1cb4be3dd3cb670"
OBSERVER_SHA = "22d1e23a0c50dc301aa98dea4ea158576fae6cd5c81fad42d244ca8d466382e0"
LAUNCH_SHA = "33c7728e3a8a59aa1e10f81c63c5842e4b6344a04a558ccef54efdbee8ce3c7c"

CONTROL_ROOT = [
    "work/line-ring-target-primitives-integration-20261113/" + x for x in
    ("patch-manifest.json", "integration.patch", "prepare.py", "apply.py",
     "expected-after-compiler-inputs.json", "application-record.json", "main-inputs-after.json",
     "stop_superseded_874.ps1", "superseded-874-stop.json",
     "superseded-874-result-before-stop.json", "superseded-874-result-after-stop.json")
] + [
    "work/line-ring-source-validation-20261113/run_validation.py",
    "docs/validation/canvas-basis-shared-source-20261112/build-test-result.json",
    "docs/validation/line-ring-actual-source-20261113/legacy-case-list.json",
    "work/canvas-numeric-basis-integration-20261113/patch-manifest.json",
    "work/canvas-numeric-basis-integration-20261113/integration.patch",
    "work/canvas-numeric-basis-integration-20261113/root-integration-review.json",
    "work/ring-x-numeric-20261113/FROZEN-MANIFEST.md",
    "work/ring-x-numeric-20261113/integration.patch",
    "work/ring-x-numeric-20261113/independent-review.json",
    "work/ring-x-numeric-20261113/candidate/src/RizToolbox.Adapter/Gameplay/NativeSeekLineRingXNumericMath.cs",
    "work/ring-x-numeric-20261113/candidate/tests/NativeSeekLineRingXNumericChecks.cs",
    "work/ring-x-numeric-20261113/NuGet.Config",
    "work/ring-x-numeric-20261113/single-bit-goldens.json",
    "work/ring-x-numeric-20261113/oracle_fraction.py",
]
CONTROL_RUN = ["patch-manifest.json", "validation-control.py"]


def require(ok: bool, message: str) -> None:
    if not ok:
        raise RuntimeError(message)


def safe_name(name: str) -> str:
    require(isinstance(name, str) and name and "\\" not in name and not name.startswith("/"), "Unsafe archive name")
    require(all(x not in ("", ".", "..") and ":" not in x for x in name.split("/")), "Unsafe archive name: " + name)
    return name


def safe_file(path: Path) -> Path:
    path = Path(path)
    require(path.is_file(), "Missing file: " + str(path))
    for part in (path, *path.parents):
        require(not part.is_symlink(), "Symlink in evidence path: " + str(path))
    require(path.stat().st_size <= MAX_FILE_BYTES, "Evidence file exceeds bound: " + str(path))
    return path


def sha(path: Path) -> str:
    with safe_file(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def read(path: Path):
    return json.loads(safe_file(path).read_text(encoding="utf-8-sig"))


def encoded(value) -> bytes:
    return (json.dumps(value, ensure_ascii=False, indent=2) + "\n").encode("utf-8")


def control_archive_name(path: Path) -> str:
    path = Path(path)
    if path.is_relative_to(ROOT):
        return "controls/root/" + safe_name(path.relative_to(ROOT).as_posix())
    require(path.is_relative_to(RUN), "Unexpected control path: " + str(path))
    return "controls/run/" + safe_name(path.relative_to(RUN).as_posix())


def expected_controls() -> set[str]:
    return {str(ROOT / x) for x in CONTROL_ROOT} | {str(RUN / x) for x in CONTROL_RUN}


def specs(listing: list[dict], artifacts: Path) -> dict[str, tuple[list[str], int | None, str | None, str | None, int]]:
    judgements = artifacts / "bin" / TESTS[0] / "release" / (TESTS[0] + ".dll")
    runtime = artifacts / "bin" / TESTS[1] / "release" / (TESTS[1] + ".dll")
    audio = artifacts / "bin" / TESTS[2] / "release" / (TESTS[2] + ".dll")
    result = {}
    for component in SIX:
        project = ROOT / "src" / component / (component + ".csproj")
        result["build-" + component] = ([str(DOTNET), "build", str(project), "-c", "Release",
            "--artifacts-path", str(artifacts), "--verbosity", "minimal", "--disable-build-servers",
            "-m:1", "-p:UseSharedCompilation=false", "-p:NuGetAudit=false",
            "-p:RestoreConfigFile=" + str(OFFLINE)], None, None, None, 0)
    for test in TESTS:
        project = ROOT / "tests" / test / (test + ".csproj")
        result["build-" + test] = ([str(DOTNET), "build", str(project), "-c", "Release",
            "--artifacts-path", str(artifacts), "--verbosity", "minimal", "--disable-build-servers",
            "-m:1", "-p:UseSharedCompilation=false", "-p:NuGetAudit=false",
            "-p:RestoreConfigFile=" + str(OFFLINE)], None, None, None, 0)
    result["build-solution"] = ([str(DOTNET), "build", str(ROOT / "RizToolbox.sln"), "-c", "Release",
        "--artifacts-path", str(artifacts), "--verbosity", "minimal", "--disable-build-servers",
        "-m:1", "-p:UseSharedCompilation=false", "-p:NuGetAudit=false",
        "-p:RestoreConfigFile=" + str(OFFLINE)], None, None, None, 0)
    result["list-legacy-cases"] = ([str(DOTNET), str(judgements), "--list-legacy-cases"], None, None, None, 0)
    result["numeric-13"] = ([str(DOTNET), str(judgements), "--line-ring-numeric-checks"], 13, "Line ring numeric ", None, 0)
    result["ring-x-7"] = ([str(DOTNET), str(judgements), "--line-ring-ring-x-numeric-checks"], 7, "Line ring numeric ring ", None, 0)
    for i in range(1, 20):
        result[f"source-{i:02d}"] = ([str(DOTNET), str(judgements), "--line-ring-source-case", str(i)], 1, "Line ring source ", None, 0)
    for i in range(1, 17):
        result[f"basis-{i:02d}"] = ([str(DOTNET), str(judgements), "--canvas-basis-case", str(i)], 1, "Canvas basis ", None, 0)
    for row in listing:
        i = row["index"]
        result[f"legacy-{i:03d}"] = ([str(DOTNET), str(judgements), "--legacy-case", str(i)], 1, None, row["name"], 0)
    for selector, values in (("legacy-case", ("0", "301", "not-an-int")),
                             ("line-ring-source-case", ("0", "20", "not-an-int")),
                             ("canvas-basis-case", ("0", "17", "not-an-int"))):
        for value in values:
            result[f"invalid-{selector}-{value}"] = ([str(DOTNET), str(judgements), "--" + selector, value], None, None, None, 2)
    result["invalid-ring-selector"] = ([str(DOTNET), str(judgements), "--line-ring-ring-x-numeric-check"], None, None, None, 2)
    result["invalid-ring-arity"] = ([str(DOTNET), str(judgements), "--line-ring-ring-x-numeric-checks", "extra"], None, None, None, 2)
    result["renderer-abi"] = ([str(DOTNET), str(runtime), "--renderer-abi-checks"], 51, None, None, 0)
    result["audio-content"] = ([str(DOTNET), str(audio)], 1001, None, None, 0)
    result["exact-static-read"] = ([str(DOTNET), str(runtime), "--exact-static-field-read-checks"], 6, None, None, 0)
    require(len(result) == 362, "Step specification count differs")
    return result


def audit_steps(result: dict, listing: list[dict], usage: str, log_file) -> None:
    expected = specs(listing, RUN / "artifacts")
    steps = result["steps"]
    require(len(steps) == 362 and {x["name"] for x in steps} == set(expected), "Exact 362-step inventory differs")
    require(len({x["name"] for x in steps}) == 362, "Duplicate step")
    names_by_prefix = {x: [] for x in ("source-", "basis-", "legacy-")}
    numeric_names = ring_names = None
    for row in steps:
        name = row["name"]
        argv, total, prefix, exact, exit_code = expected[name]
        require(row["arguments"] == argv and row["expectedExitCode"] == exit_code,
                "Exact argv or expected exit differs: " + name)
        require(row["exitCode"] == exit_code and row["naturalExit"] is True
                and row["matchesExpected"] is True and row["timedOut"] is False
                and row["resourceStop"] is None and row.get("startedUtc") and row.get("finishedUtc"),
                "Step did not finish naturally: " + name)
        expected_log = RUN / (name + ".log")
        require(row["log"] == str(expected_log), "Step log path differs: " + name)
        log = safe_file(log_file(name, False))
        require(sha(log) == row["logSha256"], "Step log SHA differs: " + name)
        content = log.read_text(encoding="utf-8-sig", errors="replace")
        totals = re.findall(r"(\d+)/(\d+) audio/content checks passed\." if name == "audio-content" else r"(\d+)/(\d+) passed", content)
        passes = re.findall(r"^PASS (.+)$", content, re.MULTILINE)
        runs = re.findall(r"^RUN (.+)$", content, re.MULTILINE)
        require(row["totals"] == [list(x) for x in totals] and row["passNames"] == passes
                and row["runNames"] == runs and not re.search(r"^FAIL\b", content, re.MULTILINE),
                "Recorded RUN/PASS/totals differ from log: " + name)
        require(totals == ([] if total is None else [(str(total), str(total))]), "Exact total differs: " + name)
        if exit_code == 2:
            expected_stderr = RUN / (name + ".stderr.log")
            require(row["stderrLog"] == str(expected_stderr) and content == "" and not runs and not passes,
                    "Invalid-selector stdout or stderr path differs: " + name)
            stderr = safe_file(log_file(name, True))
            require(sha(stderr) == row["stderrLogSha256"]
                    and stderr.read_text(encoding="utf-8-sig", errors="replace") == usage + "\n",
                    "Invalid-selector exact usage differs: " + name)
        else:
            require(row["stderrLog"] is None and row["stderrLogSha256"] is None,
                    "Unexpected separate stderr: " + name)
        if exact is not None:
            require(passes == [exact] and runs == [exact], "Legacy case/listing mismatch: " + name)
        elif prefix is not None:
            require(len(passes) == total and runs == passes and len(set(passes)) == total
                    and all(x.startswith(prefix) for x in passes), "Case names differ: " + name)
        elif name in ("renderer-abi", "audio-content", "exact-static-read"):
            require(len(passes) == total and len(set(passes)) == total and not runs,
                    "Tail PASS names differ: " + name)
        else:
            require(not passes and not runs, "Unexpected case lines: " + name)
        for group in names_by_prefix:
            if name.startswith(group):
                names_by_prefix[group].extend(passes)
        if name == "numeric-13":
            numeric_names = passes
        if name == "ring-x-7":
            ring_names = passes
        if name == "list-legacy-cases":
            require(json.loads(content) == listing, "Current listing stdout differs")
    for prefix, count in (("source-", 19), ("basis-", 16), ("legacy-", 300)):
        require(len(names_by_prefix[prefix]) == count and len(set(names_by_prefix[prefix])) == count,
                "Case names are not unique: " + prefix)
    require(numeric_names is not None and ring_names is not None
            and len(set(numeric_names + ring_names)) == 20
            and not any(x.startswith("Line ring numeric ring ") for x in numeric_names),
            "Numeric and Ring X selectors overlap")


def audit_report(result: dict, run_file, source_file, compiled_file, control_file) -> tuple[dict, dict]:
    require(result.get("finishedUtc") and result["passed"] is True and result.get("failure") in (None, "")
            and result["activeChildren"] == {}, "Controller has no successful terminal")
    require(result["controllerPid"] == 41520 and result["artifactsDirectory"] == str(RUN / "artifacts")
            and result["controlSha256"] == FROZEN["validate_main.py"], "Wrong controller or artifact root")
    require(result["acceptedThisBatch"] is False and result["prior874UnfinishedPreserved"] is True
            and result["oldResultRetainedAsCurrentPass"] is False
            and result["default355SingleProcessExecuted"] is False
            and result["defaultExpectedCases"] == 355 and result["inputCount"] == 879
            and result["maximumConcurrentTestChildren"] == 2
            and result["childTimeoutSeconds"] == 2700
            and result["minimumAvailableCommitBytes"] == 512 * 1024 * 1024,
            "Controller scope or legacy disposition differs")
    for flag in ("sdkGoalComplete", "completeSeekPermitted", "fullSeekPermitted", "gameStarted", "installed"):
        require(result[flag] is False, "Unexpected goal or game claim: " + flag)
    for flag in ("earlyFeatureGatesPassed", "compiledBytesUnchangedDuringTests",
                 "dependencyCopiesUnchangedDuringTests", "nativeFixturesUnchangedDuringTests",
                 "numericGoldenFixtureUnchangedDuringTests", "ringNonCompilerFixturesUnchanged",
                 "controlFilesUnchangedDuringTests", "stepLogsUnchanged", "superseded874EvidenceUnchanged",
                 "inputsUnchanged", "nonCompilerControlsUnchanged"):
        require(result[flag] is True, "Final invariant false: " + flag)
    require(result["superseded874StopRecordSha256"] == FROZEN["superseded-874-stop.json"]
            and result["applicationRecordSha256"] == APPLICATION_SHA
            and result["nonCompilerControlsBefore"] == {"scripts/Build.ps1": BUILD_SHA},
            "Applied Build or supersession identity differs")
    before, after = read(run_file("inputs-before.json")), read(run_file("inputs-after.json"))
    require(before == after and len(before) == 879
            and sha(run_file("inputs-before.json")) == sha(run_file("inputs-after.json"))
            and before == read(control_file(WORK / "expected-after-compiler-inputs.json")),
            "Fresh 879-input graph changed")
    require(all(safe_name(name) == name and sha(source_file(name)) == digest for name, digest in before.items()),
            "Compiler input bytes differ")
    require(sha(source_file("scripts/Build.ps1")) == BUILD_SHA, "Build control bytes differ")
    program = source_file("tests/RizToolbox.Judgements.Tests/Program.cs").read_text(encoding="utf-8")
    usage_lines = re.findall(r'Console\.Error\.WriteLine\("(Usage: RizToolbox\.Judgements\.Tests \[[^"\r\n]+\])"\);', program)
    require(len(usage_lines) == 1 and hashlib.sha256(usage_lines[0].encode("utf-8")).hexdigest()
            == result["invalidUsageSha256"], "Invalid-selector usage source differs")
    listing = read(run_file("legacy-case-list.json"))
    baseline = read(control_file(ROOT / "docs/validation/line-ring-actual-source-20261113/legacy-case-list.json"))
    require(sha(control_file(ROOT / "docs/validation/line-ring-actual-source-20261113/legacy-case-list.json")) == BASELINE_SHA
            and listing == baseline and len(listing) == 300
            and [x["index"] for x in listing] == list(range(1, 301))
            and len({x["name"] for x in listing}) == 300, "Legacy listing differs")
    audit_steps(result, listing, usage_lines[0], lambda name, err: run_file("logs/" + name + (".stderr.log" if err else ".log")))
    controls = result["controlHashesBefore"]
    require(set(controls) == expected_controls(), "Frozen control inventory differs")
    require(all(SHA_RE.fullmatch(digest) and sha(control_file(Path(path))) == digest
                for path, digest in controls.items()), "Frozen control file SHA differs")
    require(sha(control_file(RUN / "validation-control.py")) == FROZEN["validate_main.py"]
            and sha(control_file(RUN / "patch-manifest.json")) == FROZEN["patch-manifest.json"]
            and sha(control_file(ROOT / "work/line-ring-source-validation-20261113/run_validation.py")) == HELPER_SHA
            and sha(control_file(ROOT / "work/line-ring-target-primitives-integration-20261113/application-record.json")) == APPLICATION_SHA,
            "Current exact run controls differ")
    manifest = read(control_file(WORK / "patch-manifest.json"))
    require(manifest["runnerCaseBounds"] == {"lineRingSource": 19, "canvasBasis": 16,
            "lineRingNumeric": 13, "ringXNumeric": 7, "legacy": 300, "defaultExpected": 355}
            and len(manifest["changes"]) == 10 and manifest["completeSeekPermitted"] is False
            and manifest["sdkGoalComplete"] is False, "Combined manifest scope differs")
    require(sha(control_file(WORK / "patch-manifest.json")) == FROZEN["patch-manifest.json"]
            and sha(control_file(WORK / "integration.patch")) == FROZEN["integration.patch"]
            and sha(control_file(WORK / "prepare.py")) == FROZEN["prepare.py"]
            and sha(control_file(WORK / "apply.py")) == FROZEN["apply.py"]
            and sha(control_file(WORK / "expected-after-compiler-inputs.json")) == FROZEN["expected-after-compiler-inputs.json"]
            and sha(control_file(WORK / "main-inputs-after.json")) == FROZEN["main-inputs-after.json"],
            "Frozen combined package differs")
    applied = read(control_file(WORK / "application-record.json"))
    require(applied["appliedToMain"] is True and applied["targetRecords"] == manifest["changes"]
            and applied["compilerInputCountAfter"] == 879
            and applied["superseded874StopRecordSha256"] == FROZEN["superseded-874-stop.json"]
            and applied["newMainFullMatrixPassed"] is False and applied["sdkGoalComplete"] is False
            and applied["completeSeekPermitted"] is False,
            "Application record differs from frozen package")
    for row in manifest["changes"]:
        require(sha(source_file(row["targetPath"])) == row["newTargetSha256"],
                "Applied target differs: " + row["targetPath"])
    products = result["productsBeforeTests"]
    require(products == result["productsAfterTests"] and set(products) == set(SIX)
            and all(set(x) == {".dll", ".pdb"} for x in products.values()), "Six production DLL/PDB pairs differ")
    tests = result["testAssemblies"]
    require(set(tests) == set(TESTS) and all(set(x) == {".dll", ".pdb"} for x in tests.values()),
            "Three test DLL/PDB pairs differ")
    for component, values in products.items():
        for ext, digest in values.items():
            require(sha(compiled_file("production/" + component + ext)) == digest,
                    "Production artifact differs: " + component + ext)
    for test, values in tests.items():
        for ext, digest in values.items():
            require(sha(compiled_file("test-assemblies/" + test + ext)) == digest,
                    "Test artifact differs: " + test + ext)
    deps = result["dependencyCopies"]
    expected_deps = {(test, component, ext) for test in TESTS for component in SIX[:4] for ext in (".dll", ".pdb")}
    require(len(deps) == 24 and {(x["test"], x["component"], x["extension"]) for x in deps} == expected_deps,
            "24 dependency copy inventory differs")
    for row in deps:
        name = row["test"] + "/" + row["component"] + row["extension"]
        require(row["matches"] is True and row["sha256"] == products[row["component"]][row["extension"]]
                and sha(compiled_file("dependency-copies/" + name)) == row["sha256"],
                "Current dependency copy differs: " + name)
    golden = result["numericGoldenFixture"]
    require(golden == {"sourceSha256": GOLDEN_SHA, "outputSha256": GOLDEN_SHA,
            "outputPath": str(RUN / "artifacts/bin" / TESTS[0] / "release/line-ring-numeric-goldens.json")}
            and sha(source_file("tests/RizToolbox.Judgements.Tests/LineRingNumeric/single-rounding-goldens.json")) == GOLDEN_SHA
            and sha(compiled_file("line-ring-numeric-goldens.json")) == GOLDEN_SHA,
            "Current numeric golden copy differs")
    ring_paths = {str(ROOT / "work/ring-x-numeric-20261113/single-bit-goldens.json"): RING_GOLDEN_SHA,
                  str(ROOT / "work/ring-x-numeric-20261113/oracle_fraction.py"): RING_ORACLE_SHA}
    require(result["ringNonCompilerFixturesBefore"] == ring_paths
            and result["ringNonCompilerFixturesAfter"] == ring_paths
            and all(sha(control_file(Path(path))) == digest for path, digest in ring_paths.items()),
            "Non-compiler Ring golden/oracle differs")
    fixtures = result["nativeFixtures"]
    require(set(fixtures) == {"RIZ_INITIALIZATION_ABI_FIXTURE", "RIZ_INITIALIZATION_CONTEXT_HELPER",
                              "RIZ_INITIALIZATION_ABI_DOBBY"}, "Three native fixture identities differ")
    declaration = read(control_file(ROOT / "docs/validation/canvas-basis-shared-source-20261112/build-test-result.json"))
    require(declaration["nativeFixtures"] == fixtures, "Native fixture declaration differs")
    for key, row in fixtures.items():
        require(SHA_RE.fullmatch(row["sha256"]) and sha(compiled_file("native-fixtures/" + key + "/" + Path(row["path"]).name)) == row["sha256"],
                "Native fixture bytes differ: " + key)
    return before, manifest


class Plan:
    def __init__(self):
        self.files: dict[str, tuple[Path, str, int]] = {}
        self.generated: dict[str, bytes] = {}
        self.total = 0

    def add(self, path: Path, name: str, digest: str | None = None):
        name = safe_name(name)
        require(name not in self.files and name not in self.generated and name != "artifact-manifest.json",
                "Archive path collision: " + name)
        path = safe_file(path)
        actual = sha(path)
        require(digest is None or actual == digest, "Source SHA differs: " + name)
        size = path.stat().st_size
        self.total += size
        require(len(self.files) + len(self.generated) + 2 <= MAX_FILES and self.total <= MAX_BYTES,
                "Evidence copy bound exceeded")
        self.files[name] = (path, actual, size)

    def generate(self, name: str, body: bytes):
        name = safe_name(name)
        require(name not in self.files and name not in self.generated and name != "artifact-manifest.json",
                "Archive path collision: " + name)
        self.total += len(body)
        require(len(self.files) + len(self.generated) + 2 <= MAX_FILES and self.total <= MAX_BYTES,
                "Evidence copy bound exceeded")
        self.generated[name] = body

    def verify_sources(self):
        for name, (path, digest, size) in self.files.items():
            require(path.stat().st_size == size and sha(path) == digest, "Source changed during staging: " + name)


def check_old(path) -> dict:
    stop = read(path("superseded-874-stop.json"))
    require(sha(path("superseded-874-stop.json")) == FROZEN["superseded-874-stop.json"]
            and stop["priorResultSha256"] == OLD_RESULT_SHA
            and stop["controllerActualExitCode"] == -1 and stop["controllerNaturalExit"] is False
            and stop["remainingOwnedChildren"] == 0
            and all(stop[x] is False for x in ("acceptedThisBatch", "sdkGoalComplete",
                                                "completeSeekPermitted", "originalUnfinishedResultRewritten")),
            "874 supersession boundary differs")
    require(sha(path("result-before-stop.json")) == stop["resultBeforeStopSnapshotSha256"]
            and sha(path("result-after-stop.json")) == stop["resultAfterStopSnapshotSha256"]
            and sha(path("prior-result.json")) == stop["priorResultSha256"],
            "874 snapshots or original unfinished report differ")
    prior = read(path("result-after-stop.json"))
    require(prior["passed"] is False and prior.get("finishedUtc") in (None, "")
            and prior["acceptedThisBatch"] is False and prior["controllerPid"] == 14388
            and set(prior["activeChildren"]) == {"basis-16"}
            and len(stop["stoppedChildren"]) == 1
            and stop["stoppedChildren"][0]["pid"] == prior["activeChildren"]["basis-16"]["pid"]
            and stop["stoppedChildren"][0]["naturalExit"] is False,
            "874 report was incorrectly closed")
    logs = stop["preservedLogs"]
    require(len(logs) == 11 and {x["name"] for x in logs} == {f"basis-{i:02d}.log" for i in range(6, 17)},
            "874 preserved log inventory differs")
    for row in logs:
        item = path("logs/" + row["name"])
        require(item.stat().st_size == row["length"] and sha(item) == row["sha256"],
                "874 preserved log differs: " + row["name"])
    original = read(path("original-main-result.json"))
    require(original["passed"] is False and original.get("finishedUtc") in (None, "")
            and original["controllerPid"] == 43292, "Older original report claims completion")
    for key, name in (("controllerScriptSha256", "old-controller.py"),
                      ("controllerLaunchRecordSha256", "old-root-launch.json"),
                      ("controllerStdoutSha256", "old-controller.stdout.log"),
                      ("controllerStderrSha256", "old-controller.stderr.log")):
        require(sha(path(name)) == stop[key], "874 supporting control/log differs: " + name)
    return stop


def audit_witness(result: dict, path, result_path: Path) -> None:
    launch = read(path("root-launch.json"))
    observer_launch = read(path("observer-launch.json"))
    observation = read(path("controller-exit-observation.json"))
    require(sha(path("root-launch.json")) == LAUNCH_SHA
            and sha(path("observe_controller.py")) == OBSERVER_SHA,
            "Controller or observer launch control changed")
    require(launch["pid"] == result["controllerPid"] == 41520
            and launch["scriptSha256"] == FROZEN["validate_main.py"]
            and Path(launch["output"]).resolve() == RUN.resolve(),
            "Original controller launch differs")
    require(observer_launch["pid"] == observation["observerPid"] == 14072
            and observer_launch["observedControllerPid"] == 41520
            and observer_launch["scriptSha256"] == OBSERVER_SHA,
            "Observer launch differs")
    require(observation["schema"] == "original-controller-handle-exit/v1"
            and observation["observedPid"] == 41520
            and observation["observedCreatedUtc"] == "2026-11-13T05:26:05.624595+00:00"
            and observation["observedLaunchRecordSha256"] == LAUNCH_SHA
            and observation["observedValidatorSha256"] == FROZEN["validate_main.py"]
            and observation["observerSha256"] == OBSERVER_SHA
            and observation["handleKeptUntilExit"] is True
            and observation["actualExitCode"] == 0
            and observation["resultSha256AtExit"] == sha(result_path)
            and observation["validatorUnchangedAtExit"] is True
            and observation["launchRecordUnchangedAtExit"] is True
            and observation["acceptedThisBatch"] is False
            and observation["sdkGoalComplete"] is False
            and observation["completeSeekPermitted"] is False,
            "Original controller exit was not witnessed with this exact result")
    alive = dt.datetime.fromisoformat(observation["observedAliveAtUtc"])
    report_finished = dt.datetime.fromisoformat(result["finishedUtc"])
    witnessed_finished = dt.datetime.fromisoformat(observation["finishedUtc"])
    require(alive < report_finished <= witnessed_finished, "Controller observation time order differs")
    for name in ("controller.stdout.log", "controller.stderr.log",
                 "observer.stdout.log", "observer.stderr.log"):
        safe_file(path(name))


def reviews(review_file, candidate_file, source_file):
    for name, digest in (("canvas-independent.json", CANVAS_REVIEW_SHA),
                         ("canvas-root-integration.json", CANVAS_ROOT_REVIEW_SHA),
                         ("ring-independent.json", RING_REVIEW_SHA),
                         ("combined-independent-controls.json", FROZEN["independent-controls-review.json"]),
                         ("root-scope-clarification.json", FROZEN["root-review-scope-clarification.json"])):
        require(sha(review_file(name)) == digest, "Review changed: " + name)
    canvas = read(review_file("canvas-independent.json"))
    root_canvas = read(review_file("canvas-root-integration.json"))
    ring = read(review_file("ring-independent.json"))
    combined = read(review_file("combined-independent-controls.json"))
    clarity = read(review_file("root-scope-clarification.json"))
    require(canvas["scopeComplete"] is True and canvas["blockers"] == []
            and root_canvas["blockers"] == [] and root_canvas["candidateReviewSha256"] == CANVAS_REVIEW_SHA
            and ring["scopeComplete"] is True and ring["blockers"] == []
            and combined["blockers"] == []
            and clarity["reviewedReportSha256"] == FROZEN["independent-controls-review.json"]
            and clarity["originalReviewPreserved"] is True
            and clarity["canvasFilesIdenticalToReviewedIntegrationPayload"] == 6
            and clarity["canvasFilesIdenticalToInitialFrozenCandidate"] == 5
            and clarity["ringFilesIdenticalToInitialFrozenCandidate"] == 2
            and clarity["controlsReviewDecisionAffected"] is False
            and clarity["executionAcceptance"] is False
            and clarity["sdkGoalComplete"] is False and clarity["completeSeekPermitted"] is False
            and clarity["sixthCanvasFile"]["reviewedIntegrationAndCurrentMainSha256"]
                == sha(source_file(clarity["sixthCanvasFile"]["path"])),
            "Scoped review or 5+1 Canvas clarification differs")
    canvas_rows = canvas["candidate"]["files"]
    require(len(canvas_rows) == 6 and canvas["candidate"]["manifest"]["sha256"]
            == sha(candidate_file("canvas/manifest.json"))
            and canvas["candidate"]["patch"]["sha256"] == sha(candidate_file("canvas/candidate.patch")),
            "Canvas candidate package differs")
    for row in canvas_rows:
        require(sha(candidate_file("canvas/" + row["path"])) == row["sha256"],
                "Canvas frozen candidate file differs: " + row["path"])
        if row["path"] != clarity["sixthCanvasFile"]["path"]:
            require(sha(source_file(row["path"])) == row["sha256"],
                    "One of five unchanged Canvas files differs")
    sixth = clarity["sixthCanvasFile"]
    require(sha(candidate_file("canvas/" + sixth["path"])) == sixth["initialFrozenCandidateSha256"],
            "Original sixth Canvas candidate differs")
    require(sha(candidate_file("ring/FROZEN-MANIFEST.md")) == ring["reviewedSha256"]["frozenManifest"]["sha256"]
            and sha(candidate_file("ring/integration.patch")) == "68bbc832ca47987e2df0a86f19403bad86837e848465e8b97389460313396764",
            "Ring frozen candidate package differs")
    for row in ring["reviewedSha256"]["frozenDeliverables"]:
        if row["path"].endswith("candidate/src/RizToolbox.Adapter/Gameplay/NativeSeekLineRingXNumericMath.cs"):
            candidate = "ring/src/RizToolbox.Adapter/Gameplay/NativeSeekLineRingXNumericMath.cs"
            target = "src/RizToolbox.Adapter/Gameplay/NativeSeekLineRingXNumericMath.cs"
        elif row["path"].endswith("candidate/tests/NativeSeekLineRingXNumericChecks.cs"):
            candidate = "ring/tests/NativeSeekLineRingXNumericChecks.cs"
            target = "tests/RizToolbox.Judgements.Tests/LineRingNumeric/NativeSeekLineRingXNumericChecks.cs"
        else:
            continue
        require(sha(candidate_file(candidate)) == row["sha256"] == sha(source_file(target)),
                "Ring candidate and current source differ")


def check_archive(folder: Path, check_current: bool = False) -> dict:
    folder = Path(folder)
    require(folder.is_dir() and not folder.is_symlink(), "Archive missing or symlinked")
    manifest = read(folder / "artifact-manifest.json")
    require(isinstance(manifest, dict) and len(manifest) <= MAX_FILES - 1, "Archive manifest invalid")
    inventory = set()
    for item in folder.rglob("*"):
        require(not item.is_symlink(), "Archive contains symlink")
        if item.is_file():
            inventory.add(item.relative_to(folder).as_posix())
    require(inventory == set(manifest) | {"artifact-manifest.json"}, "Archive inventory differs")
    require(sum((folder / safe_name(name)).stat().st_size for name in manifest) <= MAX_BYTES,
            "Archive exceeds byte bound")
    for name, digest in manifest.items():
        require(SHA_RE.fullmatch(digest) and sha(folder / safe_name(name)) == digest,
                "Archive SHA differs: " + name)
    run_file = lambda name: folder / "run" / safe_name(name)
    source_file = lambda name: folder / "source-snapshot" / safe_name(name)
    compiled_file = lambda name: folder / "compiled" / safe_name(name)
    control_file = lambda path: folder / control_archive_name(path)
    prior_file = lambda name: folder / "prior874" / safe_name(name)
    review_file = lambda name: folder / "review" / safe_name(name)
    candidate_file = lambda name: folder / "candidate" / safe_name(name)
    result = read(run_file("result.json"))
    inputs, _ = audit_report(result, run_file, source_file, compiled_file, control_file)
    audit_witness(result, lambda name: folder / "witness" / safe_name(name), run_file("result.json"))
    stop = check_old(prior_file)
    reviews(review_file, candidate_file, source_file)
    expected_names = {"source-snapshot/" + x for x in inputs} | {"source-snapshot/scripts/Build.ps1"}
    expected_names |= {"run/" + x for x in ("result.json", "inputs-before.json", "inputs-after.json", "legacy-case-list.json")}
    expected_names |= {"run/logs/" + row["name"] + ".log" for row in result["steps"]}
    expected_names |= {"run/logs/" + row["name"] + ".stderr.log" for row in result["steps"] if row["stderrLog"] is not None}
    expected_names |= {control_archive_name(Path(x)) for x in result["controlHashesBefore"]}
    expected_names |= {"compiled/production/" + component + ext for component in SIX for ext in (".dll", ".pdb")}
    expected_names |= {"compiled/test-assemblies/" + test + ext for test in TESTS for ext in (".dll", ".pdb")}
    expected_names |= {"compiled/dependency-copies/" + row["test"] + "/" + row["component"] + row["extension"]
                       for row in result["dependencyCopies"]}
    expected_names |= {"compiled/line-ring-numeric-goldens.json"}
    expected_names |= {"compiled/native-fixtures/" + key + "/" + Path(row["path"]).name
                       for key, row in result["nativeFixtures"].items()}
    expected_names |= {"prior874/" + x for x in ("superseded-874-stop.json", "result-before-stop.json",
        "result-after-stop.json", "prior-result.json", "original-main-result.json", "old-controller.py",
        "old-root-launch.json", "old-controller.stdout.log", "old-controller.stderr.log")}
    expected_names |= {"prior874/logs/" + row["name"] for row in stop["preservedLogs"]}
    expected_names |= {"review/" + x for x in ("canvas-independent.json", "canvas-root-integration.json",
        "ring-independent.json", "combined-independent-controls.json", "root-scope-clarification.json")}
    canvas = read(review_file("canvas-independent.json"))
    expected_names |= {"candidate/canvas/" + x for x in ("manifest.json", "candidate.patch")}
    expected_names |= {"candidate/canvas/" + row["path"] for row in canvas["candidate"]["files"]}
    expected_names |= {"candidate/ring/" + x for x in ("FROZEN-MANIFEST.md", "integration.patch",
        "src/RizToolbox.Adapter/Gameplay/NativeSeekLineRingXNumericMath.cs",
        "tests/NativeSeekLineRingXNumericChecks.cs")}
    expected_names |= {"review/support/" + x for x in ("validate_main.py", "root-application-review.json",
        "independent-controls-review.md", "root-review-scope-clarification.json", "before-compiler-inputs.json")}
    expected_names |= {"witness/" + x for x in ("root-launch.json", "observe_controller.py",
        "observer-launch.json", "controller-exit-observation.json", "controller.stdout.log",
        "controller.stderr.log", "observer.stdout.log", "observer.stderr.log")}
    expected_names |= {"controls/publish.py", "validation-summary.json", "README.md"}
    require(set(manifest) == expected_names, "Archive semantic file inventory differs")
    summary = read(folder / "validation-summary.json")
    require(summary["schema"] == "line-ring-target-primitives-main-evidence/v1"
            and summary["acceptedThisBatch"] is True
            and summary["compilerInputs"] == 879 and summary["defaultExpectedCases"] == 355
            and summary["naturalSteps"] == 362
            and summary["productionPairs"] == 6 and summary["productionFiles"] == 12
            and summary["testPairs"] == 3 and summary["testFiles"] == 6
            and summary["dependencyFiles"] == 24 and summary["nativeFixtures"] == 3
            and summary["resultSha256"] == manifest["run/result.json"]
            and summary["sourceGraphSha256"] == manifest["run/inputs-before.json"]
            and all(summary[x] is False for x in ("sdkGoalComplete", "completeSeekPermitted",
                                                    "fullSeekPermitted", "gameStarted", "installed",
                                                    "default355SingleProcessExecuted", "prior874Accepted")),
            "Acceptance summary differs from archived fresh result")
    require(len(inputs) == 879, "Archive source graph count differs")
    if check_current:
        check_current_graph(inputs)
    return summary


def check_current_graph(expected: dict) -> None:
    require(ROOT.resolve() == EXPECTED_ROOT and HERE == ROOT / "work/line-ring-target-primitives-evidence-20261113",
            "Publisher is outside authorized workspace")
    for name in ("prepare.py", "apply.py"):
        require(sha(WORK / name) == FROZEN[name], "Imported application control changed: " + name)
    sys.path.insert(0, str(WORK))
    try:
        spec = importlib.util.spec_from_file_location("frozen_combined_apply_for_publication", WORK / "apply.py")
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        current = module.inputs(read(WORK / "before-compiler-inputs.json"))
        require(module.supersession_digest() == FROZEN["superseded-874-stop.json"],
                "Original 874 supersession evidence changed")
    finally:
        sys.path.pop(0)
    require(current == expected and len(current) == 879 and sha(ROOT / "scripts/Build.ps1") == BUILD_SHA,
            "Current compiler graph or Build changed")


def make_plan() -> Plan:
    require(ROOT.resolve() == EXPECTED_ROOT and HERE == ROOT / "work/line-ring-target-primitives-evidence-20261113",
            "Publisher is outside authorized workspace")
    require(not ARCHIVE.exists(), "Evidence archive already exists; use --check-only")
    for name, digest in FROZEN.items():
        require(sha(WORK / name) == digest, "Frozen package/review changed: " + name)
    require(sha(RUN / "validation-control.py") == FROZEN["validate_main.py"]
            and sha(RUN / "patch-manifest.json") == FROZEN["patch-manifest.json"],
            "Run does not use frozen validator/package")
    result = read(RUN / "result.json")
    require(result["controlSha256"] == FROZEN["validate_main.py"], "Result uses other validator")
    audit_witness(result, lambda name: WORK / safe_name(name), RUN / "result.json")
    # The original controller writes these paths. No acceptance is inferred from
    # the superseded 874 report or from the old compiled outputs.
    current_log = lambda name, err: RUN / (name + (".stderr.log" if err else ".log"))
    def run_file(name):
        name = safe_name(name)
        return RUN / name.removeprefix("logs/")
    source_file = lambda name: ROOT / safe_name(name)
    artifact = RUN / "artifacts/bin"
    def compiled_file(name):
        parts = safe_name(name).split("/")
        if parts[0] == "production":
            component, ext = parts[1].rsplit(".", 1)
            return artifact / component / "release" / parts[1]
        if parts[0] == "test-assemblies":
            component, ext = parts[1].rsplit(".", 1)
            return artifact / component / "release" / parts[1]
        if parts[0] == "dependency-copies":
            return artifact / parts[1] / "release" / parts[2]
        if parts[0] == "line-ring-numeric-goldens.json":
            return artifact / TESTS[0] / "release/line-ring-numeric-goldens.json"
        require(parts[0] == "native-fixtures", "Unexpected compiled path")
        return Path(result["nativeFixtures"][parts[1]]["path"])
    inputs, manifest = audit_report(result, run_file, source_file, compiled_file, lambda p: p)
    check_current_graph(inputs)
    old_files = {
        "superseded-874-stop.json": WORK / "superseded-874-stop.json",
        "result-before-stop.json": WORK / "superseded-874-result-before-stop.json",
        "result-after-stop.json": WORK / "superseded-874-result-after-stop.json",
        "prior-result.json": OLD_RUN / "result.json",
        "original-main-result.json": OLD_ORIGINAL / "result.json",
        "old-controller.py": ROOT / "work/line-ring-numeric-integration-20261113/recovery-r1/resume.py",
        "old-root-launch.json": ROOT / "work/line-ring-numeric-integration-20261113/recovery-r1/root-launch.json",
        "old-controller.stdout.log": ROOT / "work/line-ring-numeric-integration-20261113/recovery-r1/controller.stdout.log",
        "old-controller.stderr.log": ROOT / "work/line-ring-numeric-integration-20261113/recovery-r1/controller.stderr.log",
    }
    old_stop = read(old_files["superseded-874-stop.json"])
    for row in old_stop["preservedLogs"]:
        old_files["logs/" + row["name"]] = OLD_RUN / row["name"]
    check_old(lambda name: old_files[name])
    review_files = {
        "canvas-independent.json": ROOT / "work/canvas-numeric-basis-20261113/independent-review.json",
        "canvas-root-integration.json": ROOT / "work/canvas-numeric-basis-integration-20261113/root-integration-review.json",
        "ring-independent.json": ROOT / "work/ring-x-numeric-20261113/independent-review.json",
        "combined-independent-controls.json": WORK / "independent-controls-review.json",
        "root-scope-clarification.json": WORK / "root-review-scope-clarification.json",
    }
    candidate_files = {
        "canvas/manifest.json": ROOT / "work/canvas-numeric-basis-20261113/manifest.json",
        "canvas/candidate.patch": ROOT / "work/canvas-numeric-basis-20261113/candidate.patch",
        "ring/FROZEN-MANIFEST.md": ROOT / "work/ring-x-numeric-20261113/FROZEN-MANIFEST.md",
        "ring/integration.patch": ROOT / "work/ring-x-numeric-20261113/integration.patch",
        "ring/src/RizToolbox.Adapter/Gameplay/NativeSeekLineRingXNumericMath.cs": ROOT / "work/ring-x-numeric-20261113/candidate/src/RizToolbox.Adapter/Gameplay/NativeSeekLineRingXNumericMath.cs",
        "ring/tests/NativeSeekLineRingXNumericChecks.cs": ROOT / "work/ring-x-numeric-20261113/candidate/tests/NativeSeekLineRingXNumericChecks.cs",
    }
    canvas = read(review_files["canvas-independent.json"])
    for row in canvas["candidate"]["files"]:
        candidate_files["canvas/" + row["path"]] = ROOT / "work/canvas-numeric-basis-20261113/candidate" / row["path"]
    reviews(lambda name: review_files[name], lambda name: candidate_files[name], source_file)
    plan = Plan()
    for name, digest in inputs.items():
        plan.add(ROOT / name, "source-snapshot/" + name, digest)
    plan.add(ROOT / "scripts/Build.ps1", "source-snapshot/scripts/Build.ps1", BUILD_SHA)
    for name in ("result.json", "inputs-before.json", "inputs-after.json", "legacy-case-list.json"):
        plan.add(RUN / name, "run/" + name)
    for row in result["steps"]:
        name = row["name"]
        plan.add(current_log(name, False), "run/logs/" + name + ".log", row["logSha256"])
        if row["stderrLog"] is not None:
            plan.add(current_log(name, True), "run/logs/" + name + ".stderr.log", row["stderrLogSha256"])
    for path, digest in result["controlHashesBefore"].items():
        plan.add(Path(path), control_archive_name(Path(path)), digest)
    for component, values in result["productsBeforeTests"].items():
        for ext, digest in values.items():
            filename = component + ext
            plan.add(compiled_file("production/" + filename), "compiled/production/" + filename, digest)
    for test, values in result["testAssemblies"].items():
        for ext, digest in values.items():
            filename = test + ext
            plan.add(compiled_file("test-assemblies/" + filename), "compiled/test-assemblies/" + filename, digest)
    for row in result["dependencyCopies"]:
        name = row["test"] + "/" + row["component"] + row["extension"]
        plan.add(compiled_file("dependency-copies/" + name), "compiled/dependency-copies/" + name, row["sha256"])
    plan.add(compiled_file("line-ring-numeric-goldens.json"), "compiled/line-ring-numeric-goldens.json", GOLDEN_SHA)
    for key, row in result["nativeFixtures"].items():
        name = "native-fixtures/" + key + "/" + Path(row["path"]).name
        plan.add(Path(row["path"]), "compiled/" + name, row["sha256"])
    for name, path in old_files.items():
        plan.add(path, "prior874/" + name)
    for name, path in review_files.items():
        plan.add(path, "review/" + name)
    for name, path in candidate_files.items():
        plan.add(path, "candidate/" + name)
    # Other supporting records are deliberately small and do not form an old
    # matrix acceptance: they identify the launch, application, and clarification.
    for name in ("validate_main.py", "root-application-review.json", "independent-controls-review.md",
                 "root-review-scope-clarification.json", "before-compiler-inputs.json"):
        plan.add(WORK / name, "review/support/" + name)
    for name in ("root-launch.json", "observe_controller.py", "observer-launch.json",
                 "controller-exit-observation.json", "controller.stdout.log", "controller.stderr.log",
                 "observer.stdout.log", "observer.stderr.log"):
        plan.add(WORK / name, "witness/" + name)
    plan.add(HERE / "publish.py", "controls/publish.py")
    stamp = dt.datetime.now(dt.timezone.utc).isoformat()
    summary = {"schema": "line-ring-target-primitives-main-evidence/v1", "acceptedAtUtc": stamp,
        "acceptedThisBatch": True, "compilerInputs": 879, "defaultExpectedCases": 355,
        "naturalSteps": 362, "productionPairs": 6, "productionFiles": 12,
        "testPairs": 3, "testFiles": 6, "dependencyFiles": 24, "nativeFixtures": 3,
        "numericCases": 13, "ringXCases": 7, "sourceCases": 19, "canvasBasisCases": 16,
        "legacyCases": 300, "rendererAbiChecks": 51, "audioContentChecks": 1001,
        "exactStaticReadChecks": 6, "invalidSelectorChecks": 11,
        "resultSha256": sha(RUN / "result.json"), "sourceGraphSha256": sha(RUN / "inputs-before.json"),
        "prior874Accepted": False, "default355SingleProcessExecuted": False,
        "sdkGoalComplete": False, "completeSeekPermitted": False, "fullSeekPermitted": False,
        "gameStarted": False, "installed": False}
    plan.generate("validation-summary.json", encoded(summary))
    readme = f"""# Fresh line/ring target primitives matrix

Published {stamp} from the exact 879-input main graph and reviewed Build.ps1. The fresh controller naturally completed 362 steps: ten builds (six Release components, three test projects, one solution), one legacy listing, numeric 13, Ring X 7, source 19 single-case, Canvas basis 16 single-case, legacy 300 single-case, eleven invalid-selector checks, and renderer ABI 51, audio/content 1001 and exact static read 6 checks. Default Judgements expects 355 cases; no default 355-case single process is claimed.

The archive holds six production DLL/PDB pairs (12 files), three test DLL/PDB pairs (6 files), 24 current dependency DLL/PDB copies, the copied numeric golden, Ring X golden and independent oracle, three native fixture files, exact source and Build bytes, all current logs and controls. Every step has its own recorded argv, actual exit, log SHA, RUN/PASS and totals. Invalid selectors have empty stdout and the exact Program usage on stderr. `witness/` binds the original controller's actual exit code 0 to the exact terminal result SHA and preserves the launch, observer, and console logs.

Five Canvas files match the initial frozen candidate byte for byte. The sixth, SourceChecks.cs, has only three new case-label prefixes; the earlier root integration review covers that rebase. The six files match the reviewed Canvas integration payload. The original independent controls review is preserved with the root scope clarification. Ring X's two files match its independently reviewed candidate.

`prior874/` preserves the separate unfinished 874 recovery result, before/after snapshots, actual stop record, eleven preserved logs, old controller controls and logs, and the earlier unfinished original report. That run did not close its matrix and certifies no 879 bytes. `artifact-manifest.json` covers every archived file. `--check-only` checks the archive without consulting current main; `--check-only --check-current` also compares today's compiler graph and Build bytes.

This accepts only the fresh offline primitives matrix. SDK completion, full target reconstruction, writer, and complete Seek remain open. No game was installed or started.
"""
    plan.generate("README.md", readme.encode("utf-8"))
    return plan


def publish() -> dict:
    plan = make_plan()
    parent = ARCHIVE.parent
    require(parent.is_dir() and parent.resolve().is_relative_to(ROOT.resolve())
            and not parent.is_symlink() and not ARCHIVE.exists(), "Unsafe or occupied archive destination")
    temp = parent / (ARCHIVE.name + ".preparing-" + uuid.uuid4().hex)
    require(temp.resolve().parent == parent.resolve(), "Staging path escaped validation directory")
    temp.mkdir(exist_ok=False)
    for name, (source, digest, _) in plan.files.items():
        target = temp / name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, target)
        require(sha(target) == digest, "Staged copy changed: " + name)
    for name, body in plan.generated.items():
        target = temp / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(body)
    manifest = {name: sha(temp / name) for name in sorted(set(plan.files) | set(plan.generated))}
    (temp / "artifact-manifest.json").write_bytes(encoded(manifest))
    staged = check_archive(temp, check_current=True)
    plan.verify_sources()
    require(sha(RUN / "result.json") == staged["resultSha256"] and not ARCHIVE.exists(),
            "Run changed or destination appeared before final rename")
    temp.rename(ARCHIVE)
    return check_archive(ARCHIVE, check_current=True)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check-only", action="store_true", help="verify existing archive")
    parser.add_argument("--check-current", action="store_true", help="also compare current main graph; requires --check-only")
    option = parser.parse_args()
    require(not option.check_current or option.check_only, "--check-current requires --check-only")
    summary = check_archive(ARCHIVE, option.check_current) if option.check_only else publish()
    print(json.dumps({"archiveVerified": True, "acceptedThisBatch": True, "naturalSteps": 362,
                      "sdkGoalComplete": False, "completeSeekPermitted": False,
                      "archive": str(ARCHIVE), "resultSha256": summary["resultSha256"]}))
    return 0


if __name__ == "__main__":
    sys.exit(main())
