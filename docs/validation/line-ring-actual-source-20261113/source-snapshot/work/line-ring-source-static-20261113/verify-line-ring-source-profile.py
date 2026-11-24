"""Verify reused line/ring pins using existing workspace text only.

Run from the repository root with:
    python work/line-ring-source-static-20261113/verify-line-ring-source-profile.py

This script never opens the image/dump/metadata paths mentioned inside old JSON,
loads a native module, executes C# code, compiles, or runs the backend tests.
"""
from __future__ import annotations

import hashlib
import json
import re
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
CASE = Path(__file__).resolve().parent
PROFILE = "src/RizToolbox.Adapter/Gameplay/NativeSeekLineRingSourceProfile.cs"
GEOMETRY = "work/seek-hold-geometry-native-20261110/geometry-static-evidence.json"
PRIOR_CHECK = "work/seek-hold-geometry-native-20261110/geometry-profile-verification.json"
HOLD_PROFILE = "src/RizToolbox.Adapter/Gameplay/NativeSeekHoldGeometryProfile.cs"
RENDERER_PROFILE = "src/RizToolbox.Adapter/Gameplay/NativeSeekRendererProfile.cs"
CANVAS_PROFILE = "src/RizToolbox.Adapter/Gameplay/NativeSeekCanvasNativeProfile.cs"
POSITION_PROFILE = "src/RizToolbox.Adapter/Gameplay/NativeSeekCanvasPositionNativeProfile.cs"
CLOCK_PROFILE = "src/RizToolbox.Adapter/Gameplay/GameplayClockNativeProfile.cs"
HANDOFF = "work/line-ring-basis-handoff-20261112/implementation-contract.md"
CONTRACT = "work/line-ring-source-static-20261113/implementation-contract.md"
SCOPE = "work/line-ring-source-static-20261113/scope.md"
read_set: dict[str, str] = {}


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def read(relative: str) -> str:
    path = (ROOT / relative).resolve()
    require(path.is_relative_to(ROOT), "Input escaped the workspace: " + relative)
    require(path.suffix in {".cs", ".json", ".txt", ".md", ".py"}, "Only text inputs are permitted")
    data = path.read_bytes()
    read_set[relative] = digest(data)
    return data.decode("utf-8-sig")


def save(name: str, value: object) -> str:
    data = (json.dumps(value, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    (CASE / name).write_bytes(data)
    return digest(data)


def main() -> None:
    profile = read(PROFILE)
    evidence = json.loads(read(GEOMETRY))
    prior = json.loads(read(PRIOR_CHECK))
    hold_source = read(HOLD_PROFILE)
    renderer_source = read(RENDERER_PROFILE)
    canvas_source = read(CANVAS_PROFILE)
    read(POSITION_PROFILE)
    clock_source = read(CLOCK_PROFILE)
    read(HANDOFF)
    read(CONTRACT)
    read(SCOPE)
    read(str(Path(__file__).relative_to(ROOT)).replace("\\", "/"))

    require(read_set[GEOMETRY] == "1e1a0a2af774014f81c87bd3a4467fb7dca6338819ae82767c9440853603116b",
            "The frozen source evidence changed")
    require(read_set[PRIOR_CHECK] == "8ef2e4567fa01e1724c35ddfd3c39d5658a404e2f4c31075888385980764c4e3",
            "The original geometry verification record changed")
    require(prior["passed"] and read_set[HOLD_PROFILE] == prior["profileSha256"],
            "The frozen Hold profile does not match its original verification record")
    require(read_set[RENDERER_PROFILE] == prior["inputSha256"]["renderer"],
            "The frozen renderer wrapper source changed")
    fingerprint = re.search(r'Fingerprint\s*=\s*"([a-f0-9]{64})"', clock_source).group(1)
    require(fingerprint == prior["inputSha256"]["binary"] == evidence["inputFiles"]["binary"]["sha256"],
            "The already-recorded image identity differs")
    require("Fingerprint = GameplayClockNativeProfile.Fingerprint" in profile,
            "The new profile is not bound to the existing clock image identity")

    class_map = {
        "InformationClass": "InLevel.LevelInformation",
        "LineControlClass": "InLevel.LineControl",
        "LineClass": "InLevel.LineInformation",
        "LinePointClass": "InLevel.LinePoint",
        "GroupClass": "InLevel.LinesGroupInformation",
        "TransformClass": "UnityEngine.Transform",
        "component": "UnityEngine.Component",
    }
    fields = [dict(declaringClass=class_map[owner], name=name, type=type_name,
                   offset=hex(int(offset, 16)), requiredLiveStatic=False)
              for owner, name, type_name, offset in re.findall(
                  r'Field\((\w+), "([^"]+)", "([^"]+)", (0x[0-9a-f]+)\);', profile)]
    require(len(fields) == 25 and len({(f["declaringClass"], f["name"]) for f in fields}) == 25,
            "Exactly 25 distinct source fields are required")
    expected_field_counts = {
        "InLevel.LevelInformation": 1, "InLevel.LineControl": 8, "InLevel.LineInformation": 7,
        "InLevel.LinePoint": 5, "InLevel.LinesGroupInformation": 4,
    }
    for declaring, expected in expected_field_counts.items():
        require(sum(f["declaringClass"] == declaring for f in fields) == expected,
                "The complete required field subset differs: " + declaring)
    for field in fields:
        require(field in evidence["exactFields"], "Field is outside frozen exactFields: " + str(field))

    methods = [dict(declaringClass=class_map[owner], name=name, returns=returns,
                    parameters=[], requiredEntryRva=hex(int(rva, 16)), requiredLiveStatic=False)
               for owner, name, returns, rva in re.findall(
                   r'Bind\((\w+), "([^"]+)", "([^"]+)", (0x[0-9a-f]+)\);', profile)]
    require(len(methods) == 10, "Four producer identity pins and six getter metadata pins are required")
    for method in methods:
        require(any(all(original.get(key) == value for key, value in method.items())
                    for original in evidence["exactMethodEntryRequirements"]),
                "Method entry is outside the frozen metadata contract: " + str(method))

    source_bodies = [dict(name=name, rva=hex(int(rva, 16)), length=int(length), sha256=sha)
                     for name, rva, length, sha in re.findall(
                         r'new\("([^"]+)", (0x[0-9a-f]+), (\d+), "([a-f0-9]{64})"\)', profile)]
    require(len(source_bodies) == 10 and len({b["name"] for b in source_bodies}) == 10,
            "The profile must use exactly ten distinct complete body spans")
    bodies: list[dict[str, object]] = []
    total_instructions = 0
    frozen_by_name = {body["name"]: body for body in evidence["geometryBodies"]}
    selected_entries = {int(body["rva"], 16) for body in source_bodies}
    for pin in source_bodies:
        original = frozen_by_name[pin["name"]]
        require(all(original[key] == value for key, value in pin.items()),
                "Complete body pin differs from frozen JSON: " + pin["name"])
        require(original["boundKind"] == "complete-next-metadata-span",
                "A selected body has another recorded bound")
        cursor = int(pin["rva"], 16)
        chunks: list[bytes] = []
        tuples: list[tuple[int, str, str]] = []
        for instruction in original["instructions"]:
            require(int(instruction["rva"], 16) == cursor, "Frozen instruction gap/overlap in " + pin["name"])
            chunk = bytes.fromhex(instruction["bytes"])
            require(bool(chunk), "A frozen instruction has no bytes")
            chunks.append(chunk)
            tuples.append((cursor, instruction["mnemonic"], instruction["operands"].strip()))
            cursor += len(chunk)
        raw = b"".join(chunks)
        require(len(raw) == pin["length"] and cursor == int(original["completeSpanEnd"], 16),
                "Frozen instructions do not cover the complete selected span")
        require(int(pin["rva"], 16) < int(original["runtimeFunctionEnd"], 16) <= cursor,
                "Frozen runtime-function bound exceeds its selected complete span")
        require(digest(raw) == pin["sha256"], "Frozen instruction bytes differ from body hash: " + pin["name"])
        asm_relative = "work/seek-hold-geometry-native-20261110/" + pin["name"] + ".asm.txt"
        asm = read(asm_relative)
        # Frozen asm adds metadata-name comments after a semicolon. Preserve
        # exact address/mnemonic/operand text while removing only that annotation.
        asm_tuples = [(int(rva, 16), mnemonic, operands.split(";", 1)[0].strip())
                      for rva, mnemonic, operands in re.findall(r'^(0x[0-9a-f]+)[ \t]+(\w+)[ \t]*(.*)$', asm, re.M)]
        require(asm_tuples == tuples, "The frozen asm text differs from its JSON instructions: " + pin["name"])
        origin_source = hold_source if pin["name"].startswith("LineControl.") or pin["name"] == "Transform.get_position" else renderer_source
        literal = f'new("{pin["name"]}", {pin["rva"]}, {pin["length"]}, "{pin["sha256"]}")'
        require(literal in origin_source, "The original producer/getter source pin differs")
        external_edges = []
        for edge in original["externalBodyEdges"]:
            copied = dict(edge)
            copied["targetPinnedInFrozenGeometry"] = copied.pop("targetPinnedBody", False)
            copied["targetPinnedInThisProfile"] = ("target" in edge and int(edge["target"], 16) in selected_entries)
            external_edges.append(copied)
        total_instructions += len(tuples)
        bodies.append(dict(pin, completeSpanEnd=original["completeSpanEnd"],
                           runtimeFunctionEnd=original["runtimeFunctionEnd"], boundKind=original["boundKind"],
                           originalSource=original["source"], instructionCount=len(tuples),
                           asm=asm_relative, externalBodyEdges=external_edges,
                           externalClosureKnown=False))

    require({int(m["requiredEntryRva"], 16) for m in methods} == selected_entries,
            "A pinned entry is missing its complete body")
    api_calls = set(re.findall(r'_api\.([A-Za-z0-9_]+)\s*\(', profile))
    expected_calls = {"Class", "InspectMetadataClass", "InspectLineRingSourceField",
                      "InspectLineRingSourceGetter", "InspectDeclaredMethod"}
    require(api_calls == expected_calls, "Profile contains another Runtime operation: " + str(api_calls))
    require(not re.search(r'\b(?:new\s+NativeSeekHold\w*|ISeekHold\w*)\b', profile),
            "A Hold profile/instance dependency was introduced")
    require("ISeekCanvasNativeProfile canvasProfile" in profile,
            "The original Canvas source profile is not the constructor dependency")
    require("internal interface ISeekLineRingSourceProfile : ISeekChartNativeProfile" in profile,
            "The independent interface inheritance changed")
    for member in ("InformationClass", "LineControlClass", "LineClass", "LinePointClass", "GroupClass", "TransformClass",
                   "GetTransformMethod", "GetParentMethod", "GetPositionMethod", "GetLocalPositionMethod",
                   "GetLocalRotationMethod", "GetLocalScaleMethod"):
        require(f"IntPtr {member} {{ get; }}" in profile, "Required profile member is absent: " + member)
    for flag in ("RuntimeLineRingSourceVerified", "TargetEnvironmentRebuiltKnown", "ResolvedUnityInternalCallsKnown",
                 "EngineConsumerClosureKnown", "ProductionAdmissionKnown", "CompleteSeekPermitted"):
        require(f"const bool {flag} = false;" in profile, "An unverified boundary was promoted: " + flag)
    for token in ("public void VerifyCurrent(Action guard)", "InspectLineRingSourceGetter(cls, name, _module, guard)",
                  "InspectLineRingSourceField(expected.DeclaringClass, expected.Name, expected.TypeName",
                  "expected.Offset, Guard", "current.ImplementationFlags != expected.ImplementationFlags",
                  "current.ParameterTypes.SequenceEqual(expected.ParameterTypes)", "SHA256.HashData(bytes)",
                  "_canvas.ChartClass != ChartClass", "_canvas.ProgressClass != ProgressClass",
                  "_canvas.InformationClass != InformationClass", "_canvas.TransformClass != TransformClass",
                  "if (!guardFailed) Interlocked.Exchange(ref _faulted, 1)"):
        require(token in profile, "A required current/guard/identity check is absent: " + token)
    require("ReadStatic" not in canvas_source and "InvokePublished" not in canvas_source,
            "The composed Canvas source profile gained a value publication")

    static = {
        "kind": "file-only-line-ring-source-profile-reuse",
        "evidenceDate": "2026-11-13",
        "sourceProfile": PROFILE,
        "sourceProfileSha256": read_set[PROFILE],
        "recordedImageSha256": fingerprint,
        "readSet": read_set,
        "exactFields": fields,
        "exactMethodEntryRequirements": methods,
        "completeBodies": bodies,
        "zeroWriterRuntimeApiAllowlist": sorted(api_calls),
        "callerGuard": {
            "nativeOverride": "VerifyCurrent(Action guard)",
            "freshGetterHelper": "InspectLineRingSourceGetter(cls, name, module, guard)",
            "freshFieldHelper": "InspectLineRingSourceField(cls, name, type, offset, guard)",
            "legacyInspectorBoundary": "Before/after each public Runtime inspector and composed Canvas.VerifyCurrent",
            "legacyInspectorInternalsIndividuallyGuarded": False,
            "runtimeHelperExecutionVerified": False,
        },
        "actualSourceGraphObserved": False,
        "actualBaselineObserved": False,
        "runtimeAbiVerified": False,
        "resolvedUnityInternalCallsObserved": False,
        "producerSemanticsClosed": False,
        "completeSeekPermitted": False,
        "gameBinaryOpened": False,
        "dllMapped": False,
        "gameCodeExecuted": False,
        "built": False,
        "backendTestsExecuted": False,
    }
    static_sha = save("line-ring-source-static-evidence.json", static)
    report = {
        "passed": True,
        "kind": "file-only-frozen-text-reuse-verification",
        "profileSha256": read_set[PROFILE],
        "verifierSha256": read_set[str(Path(__file__).relative_to(ROOT)).replace("\\", "/")],
        "staticEvidenceSha256": static_sha,
        "exactFields": len(fields),
        "exactMethodEntryRequirements": len(methods),
        "completeBodies": len(bodies),
        "completeBodyBytes": sum(body["length"] for body in bodies),
        "frozenInstructionTextEntries": total_instructions,
        "recordedImageSha256": fingerprint,
        "readSet": read_set,
        "runtimeHelperSourceAuditedByThisVerifier": False,
        "currentSourceBuilt": False,
        "backendTestsExecuted": False,
        "runtimeAbiVerified": False,
        "actualSourceCaptureVerified": False,
        "gameBinaryOpened": False,
        "dllMapped": False,
        "gameCodeExecuted": False,
        "completeSeekPermitted": False,
    }
    save("line-ring-source-profile-verification.json", report)
    print(json.dumps({key: report[key] for key in ("passed", "kind", "profileSha256", "exactFields",
                                                  "exactMethodEntryRequirements", "completeBodies", "completeBodyBytes",
                                                  "frozenInstructionTextEntries", "gameBinaryOpened", "currentSourceBuilt")},
                     ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
