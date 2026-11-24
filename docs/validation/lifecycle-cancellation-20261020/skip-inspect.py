"""Read-only Skip -> NormalMode LevelOver postcondition proof. Never a runtime pass."""
from datetime import datetime, timezone
from pathlib import Path
import hashlib
import json
import re
import capstone
import pefile

root = Path(__file__).resolve().parent
repo = root.parents[2]
binary = Path(r"D:\SteamLibrary\steamapps\common\Rizline\GameAssembly.dll")
dump_path = Path(r"C:\Users\71957\Desktop\绿豆诡计v2\output\il2cpp_output\dump.cs")
sha = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79"
assert hashlib.sha256(binary.read_bytes()).hexdigest() == sha
dump = dump_path.read_text(encoding="utf-8")
assert "System.Boolean get_enabled(); // RVA: 0x1534F90" in dump
assert "System.Void set_enabled(System.Boolean value); // RVA: 0x1535010" in dump
pe = pefile.PE(str(binary), fast_load=True)
decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
functions = {
    "Skip": list(decoder.disasm(pe.get_data(0x3C17D0, 0x15), 0x3C17D0)),
    "LevelOver": list(decoder.disasm(pe.get_data(0x3C0C00, 0x440), 0x3C0C00)),
    "Behaviour.get_enabled": list(decoder.disasm(pe.get_data(0x1534F90, 0x30), 0x1534F90)),
}


def check(name, address, operation, operands):
    instruction = next(i for i in functions[name] if i.address == address)
    assert (instruction.mnemonic, instruction.op_str) == (operation, operands)


check("Skip", 0x3C17D3, "mov", "byte ptr [rcx + 0x51], 1")
check("Skip", 0x3C17D7, "mov", "rdx, qword ptr [rax + 0x190]")
check("Skip", 0x3C17DE, "jmp", "qword ptr [rax + 0x188]")
check("LevelOver", 0x3C0C79, "mov", "rcx, qword ptr [rdi + 0xb0]")
check("LevelOver", 0x3C0C93, "xor", "r8d, r8d")
check("LevelOver", 0x3C0C96, "xor", "edx, edx")
check("LevelOver", 0x3C0C98, "call", "0x1535010")
check("LevelOver", 0x3C0C9D, "cmp", "byte ptr [rdi + 0x51], 0")
check("LevelOver", 0x3C0CA1, "jne", "0x3c0dfa")
check("LevelOver", 0x3C0DFA, "mov", "dword ptr [rdi + 0xa0], 0")
check("LevelOver", 0x3C0F6E, "cmp", "byte ptr [rdi + 0x51], 0")
check("LevelOver", 0x3C0FB3, "call", "0x2ef0")
check("LevelOver", 0x3C102F, "jmp", "0x153d380")

source_hashes = {}
for relative in ["src/RizToolbox.Adapter/Modules/ExactSkipTerminationModule.cs", "tests/RizToolbox.Game.Tests/Program.cs"]:
    source_hashes[relative] = hashlib.sha256((repo / relative).read_bytes()).hexdigest()
proof = {
    "kind": "static-only", "runtimeSkipTerminationVerified": False, "sourceBuildVerified": False,
    "observedAtUtc": datetime.now(timezone.utc).isoformat(), "binary": str(binary), "sha256": sha,
    "dumpSha256": hashlib.sha256(dump_path.read_bytes()).hexdigest(),
    "importsAndExportsEvidence": "cancellation-static-proof.json (same pinned binary)",
    "target": {"assembly": "Assembly-CSharp.dll", "namespace": "", "class": "NormalModeInLevelController",
               "method": "Skip", "parameters": [], "return": "System.Void", "abi": "InstanceVoid", "rva": "0x3c17d0"},
    "postconditionSource": {"method": "NormalModeInLevelController.LevelOver", "rva": "0x3c0c00",
                            "firstSideEffect": "controller.pauseUIControl.enabled=false via Behaviour.set_enabled(false)",
                            "getter": "UnityEngine.CoreModule.dll / UnityEngine.Behaviour.get_enabled() -> System.Boolean @0x1534f90"},
    "acceptance": "same observed open run + active controller/info/owned enabled pause + native non-ended/non-crashed/non-skipped before + original returned normally + _skip false->true + same rooted pause.enabled true->false",
    "classification": "Skipped; never Aborted, never fabricated RunFinished",
    "resultPageBehavior": "not established; no mandatory result or absence is claimed",
    "normalModeSubclassesInDump": re.findall(r"^public class ([^\r\n]+?) : NormalModeInLevelController\s*$", dump, re.M),
    "sourceSha256": source_hashes,
    "functions": {name: [{"rva": hex(i.address), "mnemonic": i.mnemonic, "operands": i.op_str} for i in instructions]
                  for name, instructions in functions.items()},
}
output = root / "skip-static-proof.json"
output.write_text(json.dumps(proof, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"passed": True, "runtimeSkipTerminationVerified": False, "sourceBuildVerified": False,
                  "proofSha256": hashlib.sha256(output.read_bytes()).hexdigest()}))
