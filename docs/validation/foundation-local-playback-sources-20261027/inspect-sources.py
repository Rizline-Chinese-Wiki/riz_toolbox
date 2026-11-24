"""Read-only fixed-build disassembly for the local-playback source binding.

This records the shipped constructor/selection logic; it does not execute the
game, establish production startup admission, or treat dump labels as code.
"""
import hashlib
import json
import sys
from pathlib import Path

import capstone
import pefile

image_path, names_path, output_path = map(Path, sys.argv[1:4])
image = image_path.read_bytes()
digest = hashlib.sha256(image).hexdigest()
if digest != "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79":
    raise SystemExit("Unsupported GameAssembly image")
if output_path.exists():
    raise SystemExit("Use a fresh output path; preserve previous evidence")
names_bytes = names_path.read_bytes()
names = json.loads(names_bytes)["ScriptMethod"]
by_address = {}
for method in names:
    by_address.setdefault(method["Address"], []).append(method["Name"])
addresses = sorted(by_address)
pe = pefile.PE(data=image, fast_load=True)
decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
decoder.detail = True
scopes = []
for start in (0x375300, 0x3BEDF0, 0x3C10D0, 0x371E20):
    end = next(value for value in addresses if value > start)
    if not 0 < end - start <= 16384:
        raise SystemExit("Unexpected bounded method extent")
    decoded = []
    for instruction in decoder.disasm(pe.get_data(start, end - start), start):
        record = {"rva": instruction.address, "mnemonic": instruction.mnemonic, "operands": instruction.op_str}
        if instruction.mnemonic == "call" and instruction.operands[0].type == capstone.x86.X86_OP_IMM:
            record["calleeNames"] = by_address.get(instruction.operands[0].imm, [])
        decoded.append(record)
    scopes.append({"rva": start, "endRvaFromNextDumpLabel": end, "names": by_address.get(start, []), "instructions": decoded})
output_path.parent.mkdir(parents=True, exist_ok=True)
output_path.write_text(json.dumps({
    "schemaVersion": 1, "kind": "local-playback-source-static-inspection", "gameAssemblySha256": digest,
    "dumpSymbolsSha256": hashlib.sha256(names_bytes).hexdigest(), "capstoneVersion": capstone.__version__,
    "pefileVersion": pefile.__version__, "scopes": scopes, "gameExecuted": False,
    "productionAdmissionVerified": False,
    "limitations": ["Next dump label bounds the disassembly; labels alone do not prove native type identity or control-flow completeness.",
                    "Static call sites are not executed-session evidence or permission to activate gameplay."],
}, ensure_ascii=False, indent=2), encoding="utf-8")
for scope in scopes:
    calls = [i for i in scope["instructions"] if "calleeNames" in i]
    print(hex(scope["rva"]), scope["names"], "instructions", len(scope["instructions"]))
    print(json.dumps([{**{key: value for key, value in call.items() if key != "calleeNames"},
                       "calleeNames": call["calleeNames"][:4], "calleeAliasCount": len(call["calleeNames"])}
                      for call in calls], ensure_ascii=False))
