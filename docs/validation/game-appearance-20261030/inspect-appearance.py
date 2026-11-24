"""Read-only, fixed-build survey and narrow appearance disassembly. Never loads the DLL."""
import hashlib
import json
import sys
from pathlib import Path

import capstone
import pefile

binary, symbols, output = map(Path, sys.argv[1:4])
raw = binary.read_bytes()
fingerprint = hashlib.sha256(raw).hexdigest()
if fingerprint != "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79":
    raise SystemExit("Unsupported GameAssembly build")
pe = pefile.PE(data=raw, fast_load=True)
pe.parse_data_directories(directories=[
    pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_IMPORT"],
    pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_EXPORT"],
])
imports = [{"dll": entry.dll.decode(), "symbols": [i.name.decode() if i.name else f"ordinal:{i.ordinal}" for i in entry.imports]}
           for entry in getattr(pe, "DIRECTORY_ENTRY_IMPORT", [])]
exports = [{"name": i.name.decode() if i.name else None, "rva": hex(i.address)}
           for i in getattr(getattr(pe, "DIRECTORY_ENTRY_EXPORT", None), "symbols", [])]
# imports/exports are captured before choosing or decoding the target methods.
index = json.loads(symbols.read_text(encoding="utf-8"))["ScriptMethod"]
names = {}
for method in index:
    names.setdefault(method["Address"], []).append(method["Name"])
starts = sorted(names)
decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
decoder.detail = True
scopes = [
    ("InLevel.LineInformation.set_startColor", 0x3FB510),
    ("InLevel.LineInformation.set_endColor", 0x3FB460),
    ("InLevel.LineInformation.Appear", 0x3FAFF0),
    ("InLevel.LineControl.SetLineColor", 0x3ED530),
    ("InLevel.LineControl.SetDynamicLines", 0x3ECF40),
    ("InLevel.LinesGroupInformation.set_color", 0x3FB990),
    ("InLevel.NoteInformation.Appear", 0x3FCDC0),
    ("InLevel.NoteControl.SetNotes", 0x3FC620),
    ("InLevel.EasyPoolChild.Reset", 0x3F7C00),
]
disassembly = []
for name, rva in scopes:
    end = next(address for address in starts if address > rva)
    if end-rva > 16384:
        raise SystemExit(f"Unexpected method bound: {name}")
    instructions = []
    for instruction in decoder.disasm(pe.get_data(rva, end-rva), rva):
        item = {"rva": hex(instruction.address), "bytes": instruction.bytes.hex(),
                "mnemonic": instruction.mnemonic, "operands": instruction.op_str}
        if instruction.mnemonic in ("call", "jmp") and instruction.operands and instruction.operands[0].type == capstone.x86.X86_OP_IMM:
            item["targetNames"] = names.get(instruction.operands[0].imm, [])
        instructions.append(item)
    disassembly.append({"name": name, "rva": hex(rva), "size": end-rva, "instructions": instructions})
result = {"schemaVersion": 1, "evidenceKind": "static-disassembly", "gameAssemblySha256": fingerprint,
          "binaryBytes": len(raw), "machine": hex(pe.FILE_HEADER.Machine),
          "sections": [{"name": s.Name.rstrip(b"\0").decode(), "rva": hex(s.VirtualAddress), "size": s.Misc_VirtualSize} for s in pe.sections],
          "imports": imports, "exports": exports, "scopes": disassembly,
          "tools": {"capstone": capstone.__version__, "pefile": pefile.__version__},
          "runtimeAcceptance": False,
          "limitations": ["Read-only static evidence; no game/GUI/load/injection was run.",
                          "IDA headless helper reported Missing idalib-mcp; this explicit Capstone/PE fallback does not claim an IDA survey."]}
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(result, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
for scope in disassembly:
    print(scope["name"], scope["rva"], scope["size"])
    for item in scope["instructions"]:
        if item.get("targetNames") or (scope["size"] <= 256):
            print(item["rva"], item["mnemonic"], item["operands"], ",".join(item.get("targetNames", [])))
print("Survey imports:", len(imports), "DLLs; exports:", len(exports), "; runtimeAcceptance: false")
