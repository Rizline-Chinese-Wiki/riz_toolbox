"""Pinned local native cancellation evidence; no process access or production edits."""
from datetime import datetime, timezone
from pathlib import Path
import hashlib
import json
import capstone
import pefile

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
BINARY = Path(r"D:\SteamLibrary\steamapps\common\Rizline\GameAssembly.dll")
DUMP = Path(r"C:\Users\71957\Desktop\绿豆诡计v2\output\il2cpp_output\dump.cs")
SHA = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79"
assert hashlib.sha256(BINARY.read_bytes()).hexdigest() == SHA
baseline_path = ROOT.parent / "gameplay-controls-20261020/static-pause-guard/pause-guard-static-proof.json"
baseline = json.loads(baseline_path.read_text(encoding="utf-8"))
assert baseline["sha256"] == SHA and baseline["E-imports"] and baseline["E-exports"]
pe = pefile.PE(str(BINARY), fast_load=True)
decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
bounds = {
    "NormalModeInLevelController.Retry": (0x3C1310, 0x3C1540),
    "PauseUIControl.Back": (0x3C3A50, 0x3C3AA0),
    "PauseUIControl.Update": (0x3C3E20, 0x3C49B0),
    "NormalModeInLevelController.LevelOver": (0x3C0C00, 0x3C1040),
    "NormalModeInLevelController.RetryCoroutine.MoveNext": (0x3C68B0, 0x3C6E10),
    "NormalModeInLevelController.LevelOverCoroutine.MoveNext": (0x3C5C80, 0x3C5F20),
    "InterfaceFuncInvoker0.string.Invoke": (0x2EF0, 0x2F80),
}
decoded = {name: list(decoder.disasm(pe.get_data(start, end - start), start))
           for name, (start, end) in bounds.items()}


def expect(name, address, mnemonic, operands):
    i = next(i for i in decoded[name] if i.address == address)
    assert (i.mnemonic, i.op_str) == (mnemonic, operands), (name, hex(address), i.mnemonic, i.op_str)


retry = "NormalModeInLevelController.Retry"
expect(retry, 0x3C1359, "mov", "rax, qword ptr [rdi + 0xb0]")
expect(retry, 0x3C1373, "cmp", "byte ptr [rax + 0xb4], 0")
expect(retry, 0x3C137A, "je", "0x3c151b")
expect(retry, 0x3C1380, "cmp", "byte ptr [rax + 0xc0], 0")
expect(retry, 0x3C1387, "jne", "0x3c151b")
expect(retry, 0x3C13A4, "call", "0x1559790")  # GetActiveScene
expect(retry, 0x3C13B4, "call", "0x1559610")  # CreateScene
expect(retry, 0x3C13BD, "call", "0x155a1d0")  # SetActiveScene
expect(retry, 0x3C13E9, "call", "0x153e680")  # Destroy(controller.gameObject)
expect(retry, 0x3C1446, "mov", "rcx, qword ptr [rdi + 0x28]")
expect(retry, 0x3C1455, "call", "0x1536400")  # info.get_gameObject
expect(retry, 0x3C146B, "xor", "edx, edx")
expect(retry, 0x3C146D, "mov", "rcx, rax")
expect(retry, 0x3C1470, "call", "0x1538ab0")  # info.gameObject.SetActive(false)
expect(retry, 0x3C1479, "call", "0x155a2f0")  # UnloadSceneAsync
expect(retry, 0x3C1516, "call", "0x153d380")  # StartCoroutine
expect("PauseUIControl.Back", 0x3C3A54, "cmp", "byte ptr [rcx + 0xb4], 0")
expect("PauseUIControl.Back", 0x3C3A5D, "cmp", "byte ptr [rcx + 0xc0], 0")
expect("PauseUIControl.Back", 0x3C3A66, "mov", "byte ptr [rcx + 0xc0], 1")
expect("PauseUIControl.Update", 0x3C447C, "mov", "dword ptr [rax + 0xa0], ebx")
expect("PauseUIControl.Update", 0x3C4492, "mov", "byte ptr [rcx + 0x51], 1")
expect("PauseUIControl.Update", 0x3C449D, "call", "qword ptr [rax + 0x188]")
expect("PauseUIControl.Update", 0x3C44BA, "mov", "byte ptr [rdi + 0xc1], 1")
# The skipped path selects metadata's interface slot 0 rather than the normal
# fixed next-scene branch. This is not proof that any result page must appear.
expect("NormalModeInLevelController.LevelOver", 0x3C0F6E, "cmp", "byte ptr [rdi + 0x51], 0")
expect("NormalModeInLevelController.LevelOver", 0x3C0FB1, "xor", "ecx, ecx")
expect("NormalModeInLevelController.LevelOver", 0x3C0FB3, "call", "0x2ef0")

sources = {}
for relative in (
    "src/RizToolbox.Adapter/GameEventState.cs",
    "src/RizToolbox.Adapter/GameEventObserver.cs",
    "src/RizToolbox.Adapter/Modules/ExactGameplayHookModule.cs",
    "src/RizToolbox.Runtime/Hooks/ExactHookBroker.cs",
    "src/RizToolbox.Core/PluginScope.cs",
    "src/RizToolbox.Sdk/LifecycleContracts.cs",
):
    p = REPO / relative
    text = p.read_text(encoding="utf-8")
    sources[relative] = {"sha256": hashlib.sha256(p.read_bytes()).hexdigest(),
                         "lines": [{"line": index, "text": line} for index, line in enumerate(text.splitlines(), 1)
                                   if any(marker in line for marker in ("AbortFromVerifiedCallback", "bool associated", "RunEnded",
                                       "ControllerUnavailable", "SubscribeBoundary", "Bind(", "completed", "Finish(entry"))]}

proof = {
    "kind": "static-research-only", "runtimeCancellationVerified": False,
    "observedAtUtc": datetime.now(timezone.utc).isoformat(), "binary": str(BINARY), "sha256": SHA,
    "dumpSha256": hashlib.sha256(DUMP.read_bytes()).hexdigest(),
    "E-imports-and-exports": {"source": str(baseline_path), "sourceSha256": hashlib.sha256(baseline_path.read_bytes()).hexdigest(),
                              "importDlls": len(baseline["E-imports"]), "exports": len(baseline["E-exports"])},
    "exactTargets": [
        {"assembly": "Assembly-CSharp.dll", "namespace": "", "class": "NormalModeInLevelController",
         "method": "Retry", "parameters": [], "static": False, "return": "System.Void", "rva": "0x3c1310", "abi": "InstanceVoid"},
        {"assembly": "Assembly-CSharp.dll", "namespace": "", "class": "PauseUIControl",
         "method": "Back", "parameters": [], "static": False, "return": "System.Void", "rva": "0x3c3a50", "abi": "InstanceVoid"},
        {"assembly": "Assembly-CSharp.dll", "namespace": "", "class": "PauseUIControl",
         "method": "Update", "parameters": [], "static": False, "return": "System.Void", "rva": "0x3c3e20", "abi": "InstanceVoid"},
    ],
    "retryAcceptance": {
        "before": "same observed open run + receiver/info/pause identity + _pausing=true && _over=false + rooted infoGameObject.activeSelf=true",
        "after": "original returned normally + same token still current + same rooted infoGameObject.activeSelf=false",
        "notAcceptanceSignals": ["pause._over transition (Retry does not write it)", "pause._terminated transition (Retry does not write it)",
                                 "Destroy liveness immediately false (destruction is deferred)", "retained coroutine handle (Retry returns void and drops it)"]},
    "backAcceptance": {"request": "same token + _over false->true after successful Back return",
                       "terminalCandidate": "pending accepted request + same rooted PauseUI/controller/info/run + Update original returned normally + _terminated false->true + controller._skip=true",
                       "resultPageBehavior": "unverified; neither mandatory result nor mandatory absence is claimed"},
    "functions": {name: [{"rva": hex(i.address), "mnemonic": i.mnemonic, "operands": i.op_str} for i in insns]
                  for name, insns in decoded.items()},
    "sourceAudit": sources,
}
output = ROOT / "cancellation-static-proof.json"
output.write_text(json.dumps(proof, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
listing = []
for name, insns in decoded.items():
    listing.append("\n" + name)
    listing.extend(f"{i.address:08x} {i.mnemonic:9} {i.op_str}" for i in insns)
(ROOT / "disassembly.txt").write_text("\n".join(listing) + "\n", encoding="utf-8")
print(json.dumps({"passed": True, "runtimeCancellationVerified": False, "functions": len(decoded),
                  "proofSha256": hashlib.sha256(output.read_bytes()).hexdigest()}))
