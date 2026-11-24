"""Three fixed PE function ranges; script.json is parsed as JSON, never searched as text."""
import hashlib
import json
from pathlib import Path
import capstone
import pefile

ROOT = Path(__file__).resolve().parent
IMAGE = Path(r"D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll")
SCRIPT = Path(r"C:/Users/71957/Desktop/绿豆诡计v2/output/il2cpp_output/script.json")
TARGETS = (0x15642E0, 0x1563A60, 0x154E1D0)
expected = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79"
raw = IMAGE.read_bytes()
assert hashlib.sha256(raw).hexdigest() == expected
methods = json.loads(SCRIPT.read_text(encoding="utf-8-sig"))["ScriptMethod"]
addresses = sorted({row["Address"] for row in methods})
names = {}
for row in methods:
    names.setdefault(row["Address"], []).append(row["Name"])
pe = pefile.PE(data=raw, fast_load=True)
decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
decoder.detail = True
manifest = {"imageSha256": expected, "scriptSha256": hashlib.sha256(SCRIPT.read_bytes()).hexdigest(),
            "gameRuntimeVerified": False, "functions": []}
output = []
total = 0
for start in TARGETS:
    end = addresses[addresses.index(start) + 1]
    data = pe.get_data(start, end - start)
    insns = list(decoder.disasm(data, start))
    total += len(insns)
    assert total <= 1500
    calls = []
    output.append(f"=== {','.join(names[start])} [{start:08X},{end:08X}) ===")
    for insn in insns:
        note = ""
        if insn.mnemonic in ("call", "jmp") and insn.operands and insn.operands[0].type == capstone.x86.X86_OP_IMM:
            destination = insn.operands[0].imm
            note = " | ".join(names.get(destination, []))
            if insn.mnemonic == "call":
                calls.append({"site": hex(insn.address), "returnCaller": hex(insn.address + insn.size),
                              "destination": hex(destination), "names": names.get(destination, [])})
        output.append(f"{insn.address:08X} {insn.bytes.hex():24} {insn.mnemonic:8} {insn.op_str} ; {note}")
    manifest["functions"].append({"start": hex(start), "end": hex(end), "names": names[start],
        "instructions": len(insns), "sha256": hashlib.sha256(data).hexdigest(), "directCalls": calls})
manifest["totalInstructions"] = total
(ROOT / "listener-prepare-disassembly.txt").write_text("\n".join(output) + "\n", encoding="utf-8")
(ROOT / "listener-prepare-evidence.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
print(json.dumps(manifest, indent=2))
