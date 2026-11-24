"""Prepare the exact Canvas basis + Ring X numeric main payload; write only here."""
from __future__ import annotations

import difflib
import hashlib
import importlib.util
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).resolve().parent
CANVAS = ROOT / "work/canvas-numeric-basis-integration-20261113"
RING = ROOT / "work/ring-x-numeric-20261113"
AUTHORIZED_ROOT = Path(r"C:\Users\71957\Desktop\riz_toolbox").resolve()
CANVAS_MANIFEST_SHA = "c1664a09d7b89b567e3a67d4878cad6e015fe0e4e788e088ce95c89ee66f51ab"
CANVAS_PATCH_SHA = "db96b4e50d3f3febe0b30d41ebb86b6d35cf047d5f06d08c9ee8442d57831693"
CANVAS_ROOT_REVIEW_SHA = "92c6a95cef4400c605b32b5ec36220456d261f0b1108e5f7c3e94a2e4154a61d"
CANVAS_APPLY_SHA = "af68e07a82a21064167755b3aa762adfae002abf3c0191f6d8b26bfdeef1828d"
RING_FROZEN_SHA = "45e7f93cb03da70eea2b903a7ddfdd90ca81c78236377c030482b15b91a147d5"
RING_PATCH_SHA = "68bbc832ca47987e2df0a86f19403bad86837e848465e8b97389460313396764"
RING_REVIEW_SHA = "b21099cb2b908272fe0674c20eaa6554beda9d61c736693c7f0bc36ae021ceb1"
RING_SOURCE_SHA = "311a4c1af3dd2567e96b8f2e095f13c6fb68334680fc619603264a36c94a5ec5"
RING_TEST_SHA = "55b8a0030e658e2ece1cee357cb231bdea461b9bc0e432fb00e4a415343271d4"
BUILD = "scripts/Build.ps1"
PROGRAM = "tests/RizToolbox.Judgements.Tests/Program.cs"
RING_SOURCE = "src/RizToolbox.Adapter/Gameplay/NativeSeekLineRingXNumericMath.cs"
RING_TEST = "tests/RizToolbox.Judgements.Tests/LineRingNumeric/NativeSeekLineRingXNumericChecks.cs"
CANVAS_INPUTS = [
    "src/RizToolbox.Adapter/Gameplay/NativeSeekCanvasTargetCapture.Numeric.cs",
    "src/RizToolbox.Adapter/Gameplay/NativeSeekLineRingSourceCapture.cs",
    "src/RizToolbox.Adapter/Gameplay/NativeSeekLineRingSourceCapture.Lifecycle.cs",
    "src/RizToolbox.Adapter/Gameplay/SeekCanvasNumericBasis.cs",
    "tests/RizToolbox.Judgements.Tests/LineRingSource/NativeSeekLineRingSourceChecks.cs",
    "tests/RizToolbox.Judgements.Tests/LineRingSource/NativeSeekLineRingSourceChecks.NumericBasis.cs",
]
TARGETS = CANVAS_INPUTS + [RING_SOURCE, RING_TEST, PROGRAM, BUILD]


def require(ok: bool, why: str) -> None:
    if not ok:
        raise RuntimeError(why)


def sha_bytes(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def sha(path: Path) -> str:
    return sha_bytes(path.read_bytes())


def read(path: Path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def json_bytes(value) -> bytes:
    return (json.dumps(value, ensure_ascii=False, indent=2) + "\n").encode("utf-8")


def once(value: bytes, before: bytes, after: bytes, label: str) -> bytes:
    require(value.count(before) == 1 and after not in value, "Expected one frozen anchor: " + label)
    return value.replace(before, after, 1)


def safe_main(name: str) -> Path:
    require(name in TARGETS, "Target outside ten-file whitelist: " + name)
    path = ROOT
    for component in name.split("/"):
        path = path / component
        require(not path.is_symlink(), "Symlink in target path: " + name)
    require(path.resolve().is_relative_to(ROOT), "Target escapes main root: " + name)
    return path


def canvas_check():
    require(sha(CANVAS / "apply.py") == CANVAS_APPLY_SHA, "Reviewed Canvas applier changed")
    spec = importlib.util.spec_from_file_location("frozen_canvas_apply", CANVAS / "apply.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module.check(), module


def ring_program(before: bytes) -> bytes:
    value = once(before,
        b"tests = tests.Concat(RizToolbox.Judgements.Tests.LineRingNumeric.NativeSeekLineRingNumericChecks.Cases).ToArray();\n",
        b"tests = tests.Concat(RizToolbox.Judgements.Tests.LineRingNumeric.NativeSeekLineRingNumericChecks.Cases).ToArray();\n"
        b"tests = tests.Concat(RizToolbox.Judgements.Tests.LineRingNumeric.NativeSeekLineRingXNumericChecks.Cases).ToArray();\n",
        "Ring X case registration")
    value = once(value,
        b'or "--line-ring-numeric-checks" or "--line-ring-source-checks"',
        b'or "--line-ring-numeric-checks" or "--line-ring-ring-x-numeric-checks" or "--line-ring-source-checks"',
        "Ring X selector admission")
    value = once(value,
        b"|--line-ring-numeric-checks|--line-ring-source-checks",
        b"|--line-ring-numeric-checks|--line-ring-ring-x-numeric-checks|--line-ring-source-checks",
        "Ring X usage")
    value = once(value,
        b'        "--line-ring-numeric-checks" => RizToolbox.Judgements.Tests.LineRingNumeric.NativeSeekLineRingNumericChecks.Cases,\n',
        b'        "--line-ring-numeric-checks" => RizToolbox.Judgements.Tests.LineRingNumeric.NativeSeekLineRingNumericChecks.Cases,\n'
        b'        "--line-ring-ring-x-numeric-checks" => RizToolbox.Judgements.Tests.LineRingNumeric.NativeSeekLineRingXNumericChecks.Cases,\n',
        "Ring X selector mapping")
    return value


def ring_build(canvas_build: bytes) -> bytes:
    before = (
        b"                & dotnet $judgementsAssembly '--line-ring-numeric-checks'\r\n"
        b'                if ($LASTEXITCODE -ne 0) { throw "Judgements numeric checks failed with exit code $LASTEXITCODE." }\r\n'
    )
    after = before + (
        b"                # The 7 Ring X numeric checks run together in one separate process.\r\n"
        b"                & dotnet $judgementsAssembly '--line-ring-ring-x-numeric-checks'\r\n"
        b'                if ($LASTEXITCODE -ne 0) { throw "Judgements Ring X numeric checks failed with exit code $LASTEXITCODE." }\r\n'
    )
    return once(canvas_build, before, after, "Build Ring X numeric suite")


def patch_lines(before: bytes, after: bytes, name: str) -> str:
    old = before.decode("utf-8-sig").replace("\r\n", "\n").splitlines(keepends=True) if before else []
    new = after.decode("utf-8-sig").replace("\r\n", "\n").splitlines(keepends=True)
    header = f"diff --git a/{name} b/{name}\n"
    if not before:
        header += "new file mode 100644\n"
    return header + "".join(difflib.unified_diff(
        old, new, fromfile="a/" + name if before else "/dev/null", tofile="b/" + name, n=3))


def ring_patch(source: bytes, test: bytes, before_program: bytes, after_program: bytes) -> bytes:
    # Reproduce the frozen candidate's maker exactly, including its LF patch format.
    return (patch_lines(b"", source, RING_SOURCE)
            + patch_lines(b"", test, RING_TEST)
            + patch_lines(before_program, after_program, PROGRAM)).encode("utf-8")


def calculate():
    require(ROOT == AUTHORIZED_ROOT and WORK == ROOT / "work/line-ring-target-primitives-integration-20261113",
            "Preparation is outside its authorized workspace")
    require(sha(CANVAS / "patch-manifest.json") == CANVAS_MANIFEST_SHA
            and sha(CANVAS / "integration.patch") == CANVAS_PATCH_SHA
            and sha(CANVAS / "root-integration-review.json") == CANVAS_ROOT_REVIEW_SHA,
            "Reviewed Canvas package changed")
    review = read(CANVAS / "root-integration-review.json")
    require(review["manifestSha256"] == CANVAS_MANIFEST_SHA
            and review["patchSha256"] == CANVAS_PATCH_SHA and review["blockers"] == [],
            "Canvas root review does not accept this exact package")
    (canvas_manifest, before_graph, canvas_after_graph), canvas_module = canvas_check()
    require([row["targetPath"] for row in canvas_manifest["changes"]] == CANVAS_INPUTS + [BUILD],
            "Canvas package has another target list")
    require(sha(RING / "FROZEN-MANIFEST.md") == RING_FROZEN_SHA
            and sha(RING / "integration.patch") == RING_PATCH_SHA
            and sha(RING / "independent-review.json") == RING_REVIEW_SHA,
            "Frozen Ring X handoff changed")
    ring_review = read(RING / "independent-review.json")
    require(ring_review["scopeComplete"] and ring_review["blockers"] == []
            and ring_review["decision"] == "accept_isolated_ring_x_numeric_candidate"
            and ring_review["reviewedSha256"]["frozenManifest"]["sha256"] == RING_FROZEN_SHA
            and any(row["sha256"] == RING_PATCH_SHA and row["path"].endswith("/integration.patch")
                    for row in ring_review["reviewedSha256"]["frozenDeliverables"]),
            "Ring X independent review does not accept the frozen candidate")
    ring_source_file = RING / "candidate" / RING_SOURCE
    ring_test_file = RING / "candidate/tests/NativeSeekLineRingXNumericChecks.cs"
    require(sha(ring_source_file) == RING_SOURCE_SHA and sha(ring_test_file) == RING_TEST_SHA,
            "Frozen Ring X class bytes changed")
    require(safe_main(RING_SOURCE).exists() is False and safe_main(RING_TEST).exists() is False,
            "Ring X add target already exists")
    before_program = safe_main(PROGRAM).read_bytes()
    require(sha_bytes(before_program) == "7ac5eab1a05369b9099eda1e6fd3ee4f20c1943dae4681574682406692d5d5e2",
            "Program differs from the frozen numeric main base")
    after_program = ring_program(before_program)
    ring_source, ring_test = ring_source_file.read_bytes(), ring_test_file.read_bytes()
    require(ring_patch(ring_source, ring_test, before_program, after_program)
            == (RING / "integration.patch").read_bytes(),
            "Ring X classes or Program transformation differ from its frozen patch")
    canvas_build = (CANVAS / "files" / BUILD).read_bytes()
    final_build = ring_build(canvas_build)

    contents: dict[str, tuple[bytes, bytes]] = {}
    records = []
    for row in canvas_manifest["changes"][:-1]:
        name = row["targetPath"]
        before = (CANVAS / row["beforePath"]).read_bytes() if row["beforePath"] else b""
        after = (CANVAS / row["payloadPath"]).read_bytes()
        contents[name] = (before, after)
        records.append({"targetPath": name, "operation": row["operation"],
                        "mainBaseSha256": row["mainBaseSha256"], "newTargetSha256": row["newTargetSha256"],
                        "payloadPath": "files/" + name,
                        "beforePath": "before/" + name if before else None})
    for name, after in ((RING_SOURCE, ring_source), (RING_TEST, ring_test), (PROGRAM, after_program)):
        before = before_program if name == PROGRAM else b""
        contents[name] = (before, after)
        records.append({"targetPath": name, "operation": "modify" if before else "add",
                        "mainBaseSha256": sha_bytes(before) if before else None,
                        "newTargetSha256": sha_bytes(after), "payloadPath": "files/" + name,
                        "beforePath": "before/" + name if before else None})
    before_build = (CANVAS / "before" / BUILD).read_bytes()
    contents[BUILD] = (before_build, final_build)
    records.append({"targetPath": BUILD, "operation": "modify", "mainBaseSha256": sha_bytes(before_build),
                    "newTargetSha256": sha_bytes(final_build), "payloadPath": "files/" + BUILD,
                    "beforePath": "before/" + BUILD})
    require(len(records) == 10 and len(set(TARGETS)) == 10, "Combined payload must have ten targets")
    after_graph = dict(canvas_after_graph)
    for name in (RING_SOURCE, RING_TEST, PROGRAM):
        after_graph[name] = sha_bytes(contents[name][1])
    after_graph = dict(sorted(after_graph.items()))
    require(len(before_graph) == 874 and len(canvas_after_graph) == 877 and len(after_graph) == 879,
            "Compiler graph counts differ from 874 to 877 to 879")
    require(canvas_module.current_inputs(before_graph) == before_graph,
            "Main graph changed while computing the combined payload")
    require(all((safe_main(name).read_bytes() == before if before else not safe_main(name).exists())
                for name, (before, _) in contents.items()), "Main target changed while preparing payload")
    patch = "".join(patch_lines(*contents[name], name) for name in TARGETS).encode("utf-8")
    manifest = {
        "schema": "line-ring-target-primitives-combined-main/v1",
        "status": "prepared; independently reviewed candidates; not applied, built or tested in main",
        "authorizedRoot": str(AUTHORIZED_ROOT),
        "baseCompilerInputCount": 874, "expectedCompilerInputCountAfter": 879,
        "baseCompilerInputsSha256": sha_bytes(json_bytes(before_graph)),
        "expectedCompilerInputsAfterSha256": sha_bytes(json_bytes(after_graph)),
        "canvasPackageManifestSha256": CANVAS_MANIFEST_SHA,
        "canvasPackagePatchSha256": CANVAS_PATCH_SHA,
        "canvasRootReviewSha256": CANVAS_ROOT_REVIEW_SHA,
        "ringFrozenManifestSha256": RING_FROZEN_SHA,
        "ringFrozenPatchSha256": RING_PATCH_SHA,
        "ringIndependentReviewSha256": RING_REVIEW_SHA,
        "ringSourceSha256": RING_SOURCE_SHA, "ringTestSha256": RING_TEST_SHA,
        "ringIndependentReview": "isolated candidate accepted; main graph execution untested",
        "runnerCaseBounds": {"lineRingSource": 19, "canvasBasis": 16, "lineRingNumeric": 13,
                             "ringXNumeric": 7, "legacy": 300, "defaultExpected": 355},
        "nonCompilerControlsAfter": {BUILD: sha_bytes(final_build)},
        "changes": records, "patchSha256": sha_bytes(patch),
        "completeSeekPermitted": False, "gameStarted": False, "installed": False,
        "sdkGoalComplete": False, "newMainFullMatrixPassed": False,
    }
    return contents, before_graph, after_graph, patch, manifest


def main() -> None:
    contents, before_graph, after_graph, patch, manifest = calculate()
    require(not (WORK / "patch-manifest.json").exists() and not (WORK / "files").exists()
            and not (WORK / "before").exists(), "Combined payload is already prepared")
    for name in TARGETS:
        before, after = contents[name]
        target = WORK / "files" / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(after)
        if before:
            old = WORK / "before" / name
            old.parent.mkdir(parents=True, exist_ok=True)
            old.write_bytes(before)
    (WORK / "before-compiler-inputs.json").write_bytes(json_bytes(before_graph))
    (WORK / "expected-after-compiler-inputs.json").write_bytes(json_bytes(after_graph))
    (WORK / "integration.patch").write_bytes(patch)
    (WORK / "patch-manifest.json").write_bytes(json_bytes(manifest))
    print(json.dumps({"prepared": True, "targets": 10, "compilerInputsBefore": 874,
                      "expectedCompilerInputsAfter": 879,
                      "manifestSha256": sha(WORK / "patch-manifest.json"),
                      "patchSha256": sha(WORK / "integration.patch"),
                      "buildSha256": manifest["nonCompilerControlsAfter"][BUILD]}))


if __name__ == "__main__":
    main()
