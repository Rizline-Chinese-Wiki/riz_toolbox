"""Read the pinned local Dobby image and its PE unwind-bounded hook implementation.
No patch, process access, network or library execution. Run with the existing Python tools.
"""
import hashlib
import json
import struct
from pathlib import Path
import capstone
import pefile

ROOT = Path(__file__).resolve().parent
PATH = Path(r"D:\SteamLibrary\steamapps\common\Rizline\BepInEx\core\dobby.dll")
data = PATH.read_bytes()
digest = hashlib.sha256(data).hexdigest()
assert digest == "8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded"
pe = pefile.PE(data=data)
assert pe.FILE_HEADER.Machine == 0x8664
exports = {s.name.decode(): s.address for s in pe.DIRECTORY_ENTRY_EXPORT.symbols if s.name}
bounds = {e.struct.BeginAddress: e.struct.EndAddress for e in pe.DIRECTORY_ENTRY_EXCEPTION}
md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64); md.detail = True
def function(rva):
    # MSVC splits DobbyHook/Prepare into several chained unwind regions. A single
    # RUNTIME_FUNCTION is not a whole function. Walk instructions and branch edges
    # from the exported entry; never infer a function end from the first region.
    pending, found, edges = [rva], {}, {}
    while pending:
        pc = pending.pop()
        if pc in found: continue
        assert abs(pc-rva) < 16384 and len(found) < 4096
        instruction = next(md.disasm(pe.get_data(pc, 15), pc, count=1))
        found[pc] = instruction
        targets = []
        if instruction.mnemonic not in ("ret", "int3", "ud2"):
            branch = instruction.group(capstone.CS_GRP_JUMP)
            if branch and instruction.operands[0].type == capstone.x86_const.X86_OP_IMM:
                targets.append(instruction.operands[0].imm)
            if instruction.mnemonic != "jmp": targets.append(pc+instruction.size)
        edges[pc] = targets
        pending.extend(targets)
    instructions = [found[x] for x in sorted(found)]
    return {"entryRva": hex(rva), "unwindRegions": [{"start":hex(a), "end":hex(b)} for a,b in bounds.items() if any(a <= pc < b for pc in found)],
            "instructions": [{"rva": hex(i.address), "bytes": i.bytes.hex(), "mnemonic": i.mnemonic, "operands": i.op_str} for i in instructions],
            "edges": {hex(a): [hex(b) for b in bs] for a,bs in edges.items()},
            "directCalls": [i.operands[0].imm for i in instructions if i.mnemonic == "call" and i.operands[0].type == capstone.x86_const.X86_OP_IMM]}
hook = function(exports["DobbyHook"])
prepare = function(exports["DobbyPrepare"])
commit = function(exports["DobbyCommit"])
route = struct.unpack("<4Q", pe.get_data(0x20590, 32))
assert [x-pe.OPTIONAL_HEADER.ImageBase for x in route] == [0x2c90,0x29a0,0x2810,0x2c70]
assert 0x2880 not in prepare["directCalls"] and 0x3aa0 not in prepare["directCalls"]
assert next(i for i in prepare["instructions"] if i["rva"]=="0x3242")["operands"] == "qword ptr [r14], rax"
assert next(i for i in commit["instructions"] if i["rva"]=="0x2de3")["operands"] == "0x2880"
helpers = {hex(x):function(x) for x in [0x2880,0x2810,0x29a0,0x2c70,0x2c90,0x2890,0x2940,0x2cc0,0x3c40,0x3aa0]}
result = {"imagePath": str(PATH), "sha256": digest, "exports": {n: hex(v) for n, v in exports.items()},
          "hook": hook, "prepare": prepare, "commit": commit, "helpers": helpers,
          "routingVtableRva":"0x20590", "routingVtableTargets":[hex(x-pe.OPTIONAL_HEADER.ImageBase) for x in route],
          "scope": "Pinned-image static evidence: Prepare stores the original at 0x3242 without activation; Commit calls activation 0x2880 at 0x2de3. Dynamic fixture separately verifies target code, trampoline and actual entry. No atomic concurrent machine-code patch claim."}
(ROOT / "dobby-static.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"sha256": digest, "hookRva": hex(exports["DobbyHook"]), "prepareRva":hex(exports["DobbyPrepare"]), "commitRva":hex(exports["DobbyCommit"]), "functions":len(helpers)+3, "prepareInstructions":len(prepare["instructions"]), "passed":True}))
