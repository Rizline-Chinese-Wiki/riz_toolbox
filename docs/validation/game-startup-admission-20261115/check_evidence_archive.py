"""Read-only verification of the portable combined-startup evidence ZIP.

This never extracts files, starts processes, builds, installs, accesses a game,
resolves historical absolute paths, or executes archived controls or binaries.
"""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import sys
import zipfile


def require(value, message):
    if not value:
        raise ValueError(message)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def verify(path, expected_sha256=None):
    archive_sha256 = sha(path.read_bytes())
    if expected_sha256:
        require(archive_sha256 == expected_sha256.lower(), "Archive SHA-256 differs")
    with zipfile.ZipFile(path) as archive:
        names = archive.namelist()
        require(len(names) == len(set(names)), "Duplicate ZIP members")
        for name in names:
            require(name and "\\" not in name and ":" not in name
                    and not name.startswith("/") and ".." not in PurePosixPath(name).parts
                    and not name.endswith("/"), "Unsafe ZIP member: " + name)
        read = archive.read
        load = lambda name: json.loads(read(name).decode("utf-8-sig"))
        index = load("hash-index.json")
        require(index["schema"] == "portable-startup-evidence-index/v1", "Unexpected index schema")
        rows = index["files"]
        require(len(rows) == len({row["path"] for row in rows}), "Duplicate index rows")
        require(set(names) == {row["path"] for row in rows} | {"hash-index.json"}, "Index is not exhaustive")
        for row in rows:
            data = read(row["path"])
            require(len(data) == row["bytes"] and sha(data) == row["sha256"], "Member differs: " + row["path"])

        summary = load("summary.json")
        require(summary["schema"] == "combined-startup-evidence-summary/v1", "Unexpected summary schema")
        require(summary["sdkGoalComplete"] is False and summary["completeSeekPermitted"] is False
                and summary["fullManagedMatrix"] is False and summary["fullJudgementsRerun"] is False
                and summary["mainApplied"] is False and summary["gameSdkStartupVerified"] is False,
                "Acceptance scope was broadened")
        runtime = load("controls/runtime/overlay-manifest.json")
        normal = load("controls/runtime/normal-interval-controls.json")
        accepted = load("basis/accepted-result.json")
        require(sha(read("basis/accepted-summary.json")) == runtime["basisSummarySha256"], "Accepted summary differs")
        require(sha(read("basis/accepted-result.json")) == runtime["basisResultSha256"], "Accepted result differs")
        require(accepted["passed"] and accepted["fullFrozenInputsUnchanged"]
                and len(accepted["fullFrozenInputs"]) == 1171, "Accepted basis scope differs")
        r1 = load("runs/r1/result.json")
        r2 = load("runs/r2/result.json")
        source_names = {name.removeprefix("source/") for name in names if name.startswith("source/")}
        require(source_names == set(r2["fullFrozenInputs"]) | set(r2["auxiliaryInputs"]), "Source closure is not exact")
        require(len(r2["fullFrozenInputs"]) == 1172 and len(r2["auxiliaryInputs"]) == 1
                and "scripts/Build.ps1" in r2["auxiliaryInputs"], "Current source count differs")
        for name, digest in {**r2["fullFrozenInputs"], **r2["auxiliaryInputs"]}.items():
            require(sha(read("source/" + name)) == digest, "Current frozen input differs: " + name)
        delta = {name for name in r1["fullFrozenInputs"]
                 if r1["fullFrozenInputs"][name] != r2["fullFrozenInputs"].get(name)}
        require(delta == set(summary["r1DeltaInputs"]) and len(delta) == 2, "Historical source delta differs")
        require({name.removeprefix("r1-source-delta/") for name in names if name.startswith("r1-source-delta/")}
                == delta, "Historical source delta is not exact")
        for name, digest in {**r1["fullFrozenInputs"], **r1["auxiliaryInputs"]}.items():
            member = ("r1-source-delta/" if name in delta else "source/") + name
            require(sha(read(member)) == digest, "Historical reconstructed input differs: " + name)

        expected_counts = {
            "r1": {"six-release": 0, "solution": 0, "tick-admission": 8, "suite-Runtime": 383,
                   "suite-Hosting": 20, "suite-Adapter": 10, "suite-Hooks": 210,
                   "suite-Gameplay": 175, "suite-Module": 31, "suite-GameCamera": 55},
            "r2": {"six-release": 0, "solution": 0, "tick-admission": 8,
                   "suite-Runtime": 383, "suite-Module": 31, "suite-GameCamera": 55},
        }
        for run_name, result in [("r1", r1), ("r2", r2)]:
            prefix = "runs/" + run_name + "/"
            module = load("controls/module-" + ("v1" if run_name == "r1" else "v2") + "/overlay-manifest.json")
            expected = dict(accepted["fullFrozenInputs"])
            for manifest in (runtime, module):
                require(manifest["ready"] and manifest["frozen"], "Overlay not frozen")
                for row in manifest["files"]:
                    name = row["targetPath"]
                    require(expected.get(name) == row["baseSha256"], "Overlay basis differs: " + name)
                    expected[name] = row["sha256"]
                    member = (("controls/runtime/overlay/" if manifest is runtime else
                               "controls/module-" + ("v1" if run_name == "r1" else "v2") + "/files/") + name)
                    require(sha(read(member)) == row["sha256"], "Overlay byte differs: " + name)
            require(expected == result["fullFrozenInputs"], "Overlay source graph differs")
            require(result["auxiliaryInputs"] == accepted["auxiliaryInputs"], "Auxiliary basis differs")
            require(result["passed"] and result["fullFrozenInputsUnchanged"] and result["parentManagedInputsUnchanged"]
                    and result["completeSolutionInputs"] == 1172 and result["changedExistingInputs"] == 6
                    and result["addedCompilerInputs"] == 1 and len(result["parentManagedInputs"]) == 1160,
                    "Validation result is not accepted within its stated scope")
            for flag in ("fullManagedMatrix", "fullJudgementsRerun", "mainApplied", "installed", "gameStarted",
                         "sdkGoalComplete", "completeSeekPermitted"):
                require(result[flag] is False, "Historical validation flag changed: " + flag)
            require([step["id"] for step in result["steps"]] == list(expected_counts[run_name]), "Run step scope differs")
            for step in result["steps"]:
                log = read(prefix + step["id"] + ".log")
                text = log.decode("utf-8-sig", errors="replace").replace("\r\n", "\n")
                passes = re.findall(r"^PASS (.+)$", text, re.M)
                failures = re.findall(r"^FAIL (.+)$", text, re.M)
                require(step["exitCode"] == 0 and step["naturalExit"] is True and step["pid"] > 0
                        and sha(log) == step["logSha256"] and passes == step["passNames"]
                        and failures == step["failNames"] == []
                        and len(passes) == expected_counts[run_name][step["id"]], "Step receipt differs: " + run_name + "/" + step["id"])
                if step["id"] in ("six-release", "solution"):
                    require(re.search(r"\b0 (?:个错误|Error\(s\))", text), "Build error summary missing")
                if step["id"] == "six-release":
                    require(re.search(r"\b0 (?:个警告|Warning\(s\))", text), "Six build warning summary differs")
            for name, digest in result["controls"].items():
                require(sha(read(prefix + "controls/" + name)) == digest, "Frozen run control differs")
            controller_name = "validate_candidate.py" if run_name == "r1" else "validate_candidate_r2.py"
            supervisor_name = "supervise_candidate.py" if run_name == "r1" else "supervise_candidate_r2.py"
            receipt = load(prefix + "supervision/controller-exit.json")
            require(read(prefix + "supervision/controller-exit.json") == read(prefix + "supervision/supervision.json"), "Original wait copies differ")
            require(receipt["controllerExited"] is True and receipt["actualControllerExitCode"] == 0
                    and receipt["resultPassed"] is True
                    and receipt["controllerPid"] == receipt["resultControllerPid"] == result["controllerPid"]
                    and receipt["resultSha256"] == sha(read(prefix + "result.json"))
                    and receipt["exitObservedBy"] == "original subprocess.Popen.wait process handle"
                    and receipt["controllerControlSha256"] == sha(read("controls/runtime/" + controller_name))
                    and receipt["supervisorControlSha256"] == sha(read("controls/runtime/" + supervisor_name)), "Original wait identity differs")
            launch = load(prefix + "supervision/launch.json")
            require(launch["launchExitIsNotControllerExit"] is True and launch["supervisorPid"] == receipt["supervisorPid"], "Launch identity differs")
            require(sha(read(prefix + "controls/overlay-manifest.json")) == result["overlayManifestSha256"]
                    and sha(read(prefix + "controls/module-overlay-manifest.json")) == result["moduleOverlayManifestSha256"], "Frozen overlay receipt differs")

        for row in runtime["files"]:
            patch_name = Path(row["targetPath"]).name + ".patch"
            require(sha(read("controls/runtime/" + patch_name)) == row["patchSha256"], "Runtime patch differs")
        for name, digest in normal["files"].items():
            require(sha(read("controls/runtime/" + name)) == digest, "Normal interval control differs")
        for name, pairs in r2["productionAfterSix"].items():
            for extension, digest in pairs.items():
                require(sha(read("outputs/r2/" + name + extension)) == digest, "Six output differs")
        for extension in (".dll", ".pdb", ".deps.json"):
            product = "artifacts/bin/RizToolbox.Examples/release/RizToolbox.Examples" + extension
            require(sha(read("outputs/r2/RizToolbox.Examples" + extension)) == r2["products"][product], "Examples output differs")
        for key, row in r2["nativeFixtures"].items():
            filename = row["path"].replace("\\", "/").split("/")[-1]
            require(sha(read("fixtures/" + key + "/" + filename)) == row["sha256"], "Owned native fixture differs")

        negative = read("negative-control/baseline-module-tests.log").decode("utf-8-sig")
        require(len(re.findall(r"^PASS ", negative, re.M)) == 29
                and len(re.findall(r"^FAIL ", negative, re.M)) == 2
                and "Built-in 'rizline.game-camera' failed production registration." in negative
                and "Module manifest or interval is invalid." in negative, "Zero interval negative control differs")
        require("0x0010000000000000" in read("controls/module-v2/files/tests/RizToolbox.Module.Tests/BuiltinManifestContractChecks.cs").decode(), "Normal Double assertion missing")

        install = load("install/result.json")
        manifest = load("install/package-manifest.json")
        require(install["passed"] and install["installed"] and install["actualInstallerExitCode"] == 0
                and install["naturalExit"] and install["cleanCommit"] and install["userConfigurationPreserved"]
                and install["verifiedPackageFiles"] == 23 and install["allManagedProductsFromCurrentCombinedBuild"], "Completed install receipt differs")
        require(install["validationResultSha256"] == sha(read("runs/r2/result.json"))
                and install["originalValidationExitSha256"] == sha(read("runs/r2/supervision/controller-exit.json"))
                and install["packageManifestSha256"] == sha(read("install/package-manifest.json"))
                and install["nativeBasisInstallResultSha256"] == sha(read("basis/native-install-result.json")), "Install provenance differs")
        require(manifest["files"] == install["packageFiles"] and len(manifest["files"]) == 23, "Package scope differs")
        package_names = {name.removeprefix("install/package/") for name in names if name.startswith("install/package/")}
        require(package_names == {row["path"] for row in manifest["files"]}, "Package member closure differs")
        for row in manifest["files"]:
            require(sha(read("install/package/" + row["path"])) == row["sha256"], "Owned package byte differs")
        for name, pair in r2["productionAfterSix"].items():
            require(sha(read("install/package/BepInEx/plugins/RizToolbox/" + name + ".dll")) == pair[".dll"], "Install did not use current six")
        examples_hash = sha(read("outputs/r2/RizToolbox.Examples.dll"))
        require(sum(row["path"].endswith("/RizToolbox.Examples.dll") and row["sha256"] == examples_hash
                    for row in install["packageFiles"]) == 5, "Install did not use current Examples")

        observation = load("runtime-r1/observation.json")
        require(observation["installResultSha256"] == sha(read("install/result.json"))
                and observation["exact23InstalledFilesVerified"] is True
                and observation["gameSdkStartupVerified"] is False
                and observation["loaderStarted"] is False and observation["unityTickBound"] is False
                and observation["gameRoundPlayed"] is False and observation["exitKind"] == "normal", "Limited startup observation differs")
        for name, digest in observation["files"].items():
            require(sha(read("runtime-r1/" + name)) == digest, "Runtime observation log differs")
        current_log = read("runtime-r1/native-preinit-current.log").decode()
        require("pid=15924" in current_log and "stage=operation.prove.block-present stage_code=145 result=0 operation_row=14 dobby_result=0" in current_log
                and "event=il2cpp stage=original-return stage_code=0 result=1" in current_log,
                "Current native startup blocker differs")
        require(not read("runtime-r1/toolbox-current-session.log"), "Unexpected current-session Loader output")
    return {"passed": True, "archiveSha256": archive_sha256, "members": len(names),
            "sourceInputs": 1172, "auxiliaryInputs": 1, "r1NaturalSteps": 10, "r2NaturalSteps": 6,
            "r2FocusedCases": 8, "r2OrdinaryCases": 469, "ownedPackageFiles": 23,
            "gameSdkStartupVerified": False, "sdkGoalComplete": False,
            "verificationScope": "ZIP fixity, original wait/log/control/source/product identity and recorded scope only"}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive", type=Path)
    parser.add_argument("--expected-sha256")
    arguments = parser.parse_args()
    try:
        print(json.dumps(verify(arguments.archive, arguments.expected_sha256), indent=2))
    except (ValueError, KeyError, OSError, zipfile.BadZipFile) as error:
        print(json.dumps({"passed": False, "failure": str(error)}), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
