"""Check finite, inherited LOCAL launch/result anchors; never invokes native code.

Run --capture-broker once to record the exact source baseline discussed by README.
Default verification reads that snapshot; --check-live-broker additionally detects
whether later implementation work has changed the inspected source baseline.
"""
import argparse
import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("--capture-broker", action="store_true")
parser.add_argument("--check-live-broker", action="store_true")
args = parser.parse_args()
root = Path(__file__).resolve().parent
repo = root.parents[2]
EXPECTED = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79"
launch = json.loads((root / "boundaries.json").read_text(encoding="utf8"))
metadata_path = root.parent / "local-metadata-bridge-20261022" / "boundaries.json"
metadata = json.loads(metadata_path.read_text(encoding="utf8"))
provider_path = root.parent / "local-provider-catalog-20261021" / "boundaries.json"
provider = json.loads(provider_path.read_text(encoding="utf8"))
thunk = json.loads((root / "instantiation-thunk.json").read_text(encoding="utf8"))
for document in (launch, metadata, provider, thunk):
    assert document["gameAssemblySha256"] == EXPECTED
    assert document["scriptJsonSha256"] == launch["scriptJsonSha256"]
    if "runtimeAcceptance" in document:
        assert document["runtimeAcceptance"] is False
scopes = {"launch": {s["name"]: s for s in launch["scopes"]},
          "metadata": {s["name"]: s for s in metadata["scopes"]},
          "provider": {s["name"]: s for s in provider["scopes"]},
          "thunk": {"clone": thunk}}
checks = []

def expect(document, scope, rva, mnemonic, operands):
    instruction = next(i for i in scopes[document][scope]["instructions"] if i["rva"] == hex(rva))
    assert (instruction["mnemonic"], instruction["operands"]) == (mnemonic, operands), (scope, instruction)
    checks.append({"evidence": document, "scope": scope, "rva": hex(rva),
                   "mnemonic": mnemonic, "operands": operands})

# Parameter assignment is separate from Awake and is a shared-code address.
expect("launch", "InLevelController.SetInsideLevelParams", 0x36B0E0, "mov", "qword ptr [rcx + 0x20], rdx")
expect("launch", "InLevelController.SetInsideLevelParams", 0x36B0E8, "jmp", "0x263f60")
assert len(scopes["launch"]["InLevelController.SetInsideLevelParams"]["symbols"]) > 10
# Awake publishes six delegates and controllerSet to the persistent manager.
expect("launch", "InLevelController.Awake", 0x371907, "call", "0x4019d0")
for rva, field, register in ((0x371A0D,0x28,"rbx"),(0x371AB5,0x30,"rdi"),
                             (0x371B5F,0x38,"rdi"),(0x371C0A,0x40,"rdi"),
                             (0x371CB1,0x48,"rdi"),(0x371D04,0x50,"rdi")):
    expect("launch", "InLevelController.Awake", rva, "mov", f"qword ptr [rbp + {hex(field)}], {register}" if field != 0x50 else "qword ptr [rbx + 0x50], rdi")
expect("launch", "InLevelController.Awake", 0x371D43, "mov", "byte ptr [rax + 0x20], 1")
# Both result retry and pause retry clone before calling the virtual parameter setter.
expect("launch", "LevelOver.StartPlayLevel.MoveNext", 0x3D84CE, "call", "0x5a91b0")
expect("launch", "LevelOver.StartPlayLevel.MoveNext", 0x3D84FA, "call", "qword ptr [r9 + 0x1c8]")
expect("launch", "NormalMode.RetryCoroutine.MoveNext", 0x3C6D99, "call", "0x5a91b0")
expect("launch", "NormalMode.RetryCoroutine.MoveNext", 0x3C6DBD, "call", "qword ptr [r9 + 0x1c8]")
expect("thunk", "clone", 0x5A920F, "call", "0x153e310")
expect("thunk", "clone", 0x5A9219, "call", "0x1540240")
expect("thunk", "clone", 0x5A924B, "call", "0x263f80")
# A real controller result contains the existing metadata, cover and audio refs.
expect("launch", "NormalMode.ControllerOver", 0x3BEAA5, "call", "0x153d490")
expect("launch", "NormalMode.ControllerOver", 0x3BECAA, "call", "0x3755a0")
# Start invokes the manager ControllerOver delegate before inlined persistence.
expect("metadata", "LevelOverControl.Start", 0x3D0E27, "call", "qword ptr [rax + 0x18]")
expect("metadata", "LevelOverControl.Start", 0x3D0E2D, "mov", "qword ptr [rbx + 0x168], rax")
for rva, destination in ((0x3D0ECE,0x375380),(0x3D0EE9,0x36D170),(0x3D0FAB,0x3899C0),
                         (0x3D0FBC,0x3CE440),(0x3D0FC6,0x3CF1B0)):
    expect("metadata", "LevelOverControl.Start", rva, "call", hex(destination))
# Stock retry has already performed destructive scene work before loading assets.
expect("launch", "NormalMode.Retry", 0x3C13B4, "call", "0x1559610")
expect("launch", "NormalMode.Retry", 0x3C13BD, "call", "0x155a1d0")
expect("launch", "NormalMode.Retry", 0x3C13E9, "call", "0x153e680")
expect("launch", "NormalMode.Retry", 0x3C1479, "call", "0x155a2f0")
expect("metadata", "NormalModeInLevelController.LoadLevel.MoveNext", 0x3C618F, "call", "0x1544b90")
expect("metadata", "LevelOverControl.LoadLevel.MoveNext", 0x3D3D7F, "call", "0x1544b90")
expect("launch", "NormalMode.RetryCoroutine.MoveNext", 0x3C6BB6, "call", "0x3e0d00")
expect("launch", "NormalMode.RetryCoroutine.MoveNext", 0x3C6DFC, "call", "0x1513e80")
expect("launch", "LevelOver.StartPlayLevel.MoveNext", 0x3D8344, "call", "0x3e0d00")
expect("launch", "LevelOver.StartPlayLevel.MoveNext", 0x3D870E, "call", "0x1513e80")
# Back computes its route from result.metadata's interface getter, then transitions.
expect("launch", "LevelOver.Back", 0x3CE219, "call", "0x2ef0")
expect("launch", "LevelOver.Back", 0x3CE23C, "call", "0x402170")
expect("launch", "LevelOver.Retry", 0x3D0027, "mov", "rsi, qword ptr [rax + 0x10]")
expect("launch", "LevelOver.Retry", 0x3D0089, "call", "0x153d380")
expect("launch", "SceneManager.StartNextScene", 0x4021E5, "call", "0x153d490")
# Resource releases are tied to result receiver fields, not a generic session owner.
expect("provider", "LevelOver.OnDestroy", 0x3CFA99, "call", "0x3000")
expect("provider", "LevelOver.OnDestroy", 0x3CFAB6, "call", "0x3000")

broker_path = root / "hook-broker-source-snapshot.json"
method_specs = {
    "src/RizToolbox.Runtime/Hooks/ExactHookBroker.cs": {
        "InvokeVoid": "original(instance, method)",
        "InvokeEnum32": "original(instance, value, method)",
        "InvokeJudge": "original(instance, judge, delta, method)",
        "InvokeReference": "var result=original(instance,method)",
    },
    "src/RizToolbox.Runtime/Hooks/ExactHookBroker.Boolean.cs": {
        "InvokeBoolean": "var result = original(instance, method)",
    },
}
if args.capture_broker:
    if broker_path.exists():
        raise SystemExit("Refusing to overwrite the recorded broker baseline.")
    snapshots = []
    for relative, methods in method_specs.items():
        path = repo / relative
        raw = path.read_bytes()
        source = raw.decode("utf8")
        rows = []
        for name in methods:
            start = source.index("    private static ", source.index(" " + name + "(" ) - 80)
            assert name + "(" in source[start:source.index("{", start)]
            open_at = source.index("{", start)
            depth, end = 1, open_at + 1
            while depth:
                depth += (source[end] == "{") - (source[end] == "}")
                end += 1
            rows.append({"name": name, "startLine": source.count("\n", 0, start) + 1,
                         "endLine": source.count("\n", 0, end) + 1, "source": source[start:end]})
        snapshots.append({"path": relative, "sha256": hashlib.sha256(raw).hexdigest(), "methods": rows})
    broker_path.write_text(json.dumps({"observedAtUtc": datetime.now(timezone.utc).isoformat(),
        "evidenceKind": "read-only-source-baseline", "runtimeAcceptance": False,
        "files": snapshots}, indent=2), encoding="utf8")
broker = json.loads(broker_path.read_text(encoding="utf8"))
assert broker["runtimeAcceptance"] is False
for source in broker["files"]:
    for method in source["methods"]:
        assert method_specs[source["path"]][method["name"]] in method["source"]
    if args.check_live_broker:
        assert hashlib.sha256((repo / source["path"]).read_bytes()).hexdigest() == source["sha256"], source["path"]

artifact_paths = [root / name for name in ("boundaries.json", "triage.json", "metadata-excerpts.txt", "disassembly.txt",
    "inspect-launch.py", "instantiation-thunk.json", "instantiation-thunk.txt", "inspect-instantiation-thunk.py",
    "hook-broker-source-snapshot.json")]
artifact_paths += [metadata_path, provider_path]
result = {"schemaVersion": 1, "kind": "bounded-static-anchor-verification", "passed": True,
    "runtimeAcceptance": False, "gameAssemblySha256": EXPECTED, "checks": checks,
    "brokerOriginalCallBodiesChecked": sum(len(f["methods"]) for f in broker["files"]),
    "liveBrokerHashChecked": args.check_live_broker,
    "artifacts": [{"path": str(p.relative_to(repo)).replace("\\", "/"),
                   "sha256": hashlib.sha256(p.read_bytes()).hexdigest()} for p in artifact_paths],
    "limitations": ["Instruction anchors establish only the stated finite static relationships.",
                    "No launch, result, retry, save isolation, cleanup or UI behavior is runtime-accepted.",
                    "The broker observation is tied to the saved source baseline; future changes require fresh review."]}
(root / "static-claims.json").write_text(json.dumps(result, indent=2), encoding="utf8")
print(f"Verified {len(checks)} finite instruction anchors and 5 original-call broker bodies; runtimeAcceptance=False.")
