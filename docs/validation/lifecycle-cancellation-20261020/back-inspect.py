"""Back request/terminal native proof. Static only; writes this evidence folder."""
from pathlib import Path
from datetime import datetime, timezone
import hashlib
import json
import re
import capstone
import pefile

root = Path(__file__).resolve().parent
repo = root.parents[2]
binary = Path(r"D:\SteamLibrary\steamapps\common\Rizline\GameAssembly.dll")
expected = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79"
assert hashlib.sha256(binary.read_bytes()).hexdigest() == expected
dump_path = Path(r"C:\Users\71957\Desktop\绿豆诡计v2\output\il2cpp_output\dump.cs")
dump_text = dump_path.read_text(encoding="utf-8")
fields = {}
for type_name, expected_fields in {
    "NormalModeInLevelController": {"_crashed": "50", "_skip": "51"},
    "LevelInformation": {"levelOver": "C0", "crashed": "C1"},
    "PauseUIControl": {"_pausing": "B4", "_over": "C0", "_terminated": "C1"},
}.items():
    match = re.search(r"^public class " + type_name + r"(?:\s*:[^\r\n]+)?\s*\{.*?^\}", dump_text, re.M | re.S)
    assert match is not None
    for name, offset in expected_fields.items():
        assert re.search(r"System.Boolean " + re.escape(name) + r"; // 0x" + offset + r"\b", match.group(0))
    fields[type_name] = expected_fields
pe = pefile.PE(str(binary), fast_load=True)
decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
functions = {
    "Back": list(decoder.disasm(pe.get_data(0x3C3A50, 0x50), 0x3C3A50)),
    "Update": list(decoder.disasm(pe.get_data(0x3C3E20, 0xB90), 0x3C3E20)),
}


def check(name, address, operation, operands):
    insn = next(i for i in functions[name] if i.address == address)
    assert (insn.mnemonic, insn.op_str) == (operation, operands)


check("Back", 0x3C3A54, "cmp", "byte ptr [rcx + 0xb4], 0")
check("Back", 0x3C3A5B, "je", "0x3c3a87")
check("Back", 0x3C3A5D, "cmp", "byte ptr [rcx + 0xc0], 0")
check("Back", 0x3C3A64, "jne", "0x3c3a87")
check("Back", 0x3C3A66, "mov", "byte ptr [rcx + 0xc0], 1")
check("Update", 0x3C3EDA, "cmp", "byte ptr [rdi + 0xc1], 0")
check("Update", 0x3C3EE1, "jne", "0x3c4529")
check("Update", 0x3C4460, "cmp", "byte ptr [rdi + 0xc0], 0")
check("Update", 0x3C4467, "je", "0x3c4533")
check("Update", 0x3C447C, "mov", "dword ptr [rax + 0xa0], ebx")
check("Update", 0x3C4492, "mov", "byte ptr [rcx + 0x51], 1")
check("Update", 0x3C449D, "call", "qword ptr [rax + 0x188]")
check("Update", 0x3C44B5, "call", "0x36aea0")
check("Update", 0x3C44BA, "mov", "byte ptr [rdi + 0xc1], 1")

sources = {}
for relative in ["src/RizToolbox.Adapter/Modules/ExactBackCancellationModule.cs",
                 "src/RizToolbox.Adapter/BackCancellationTracker.cs", "tests/RizToolbox.Game.Tests/Program.cs"]:
    path = repo / relative
    sources[relative] = hashlib.sha256(path.read_bytes()).hexdigest()
proof = {"kind": "static-only", "runtimeBackCancellationVerified": False, "sourceBuildVerified": False,
         "observedAtUtc": datetime.now(timezone.utc).isoformat(), "binary": str(binary), "sha256": expected,
         "dumpSha256": hashlib.sha256(dump_path.read_bytes()).hexdigest(), "verifiedBooleanFieldOffsets": fields,
         "evidenceBaseline": "cancellation-static-proof.json",
         "sourceSha256": sources,
         "controlFlow": {"Back": "_pausing && !_over -> _over=true (request only)",
                         "Update": "pending same-run request + _terminated and controller._skip false->true after original return (terminal evidence)",
                         "beforeRefusal": "native information.levelOver/crashed or controller._crashed/_skip refuses capture even when sampled state still reports an open run"},
         "resultAssociation": "not established; neither a mandatory result nor its mandatory absence is claimed",
         "functions": {name: [{"rva": hex(i.address), "mnemonic": i.mnemonic, "operands": i.op_str} for i in instructions]
                       for name, instructions in functions.items()}}
output = root / "back-static-proof.json"
output.write_text(json.dumps(proof, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"passed": True, "runtimeBackCancellationVerified": False, "sourceBuildVerified": False,
                  "proofSha256": hashlib.sha256(output.read_bytes()).hexdigest()}))
