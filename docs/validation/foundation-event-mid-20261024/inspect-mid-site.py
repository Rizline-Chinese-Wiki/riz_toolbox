"""Read-only, hash-pinned evidence for exactly one GameAssembly function.

Only a binary search of the PE exception directory is used; only the selected
RUNTIME_FUNCTION and its one UNWIND_INFO are decoded. No script.json read, game
execution, process memory access or patch is performed.
"""
from pathlib import Path
import hashlib
import json
import struct
import capstone
import pefile

ROOT = Path(__file__).resolve().parent
IMAGE = Path(r"D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll")
EXPECTED_SHA = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79"
PRIOR = ROOT.parent / "foundation-native-void-20261024/terminal-evidence.json"
START, CANDIDATE, SITE, RESUME = 0x15642E0, 0x1564417, 0x156441B, 0x1564420

def sha_file(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()

digest = sha_file(IMAGE)
assert digest == EXPECTED_SHA, "UnsupportedBuild: GameAssembly hash changed"
pe = pefile.PE(str(IMAGE), fast_load=True)
assert pe.FILE_HEADER.Machine == 0x8664 and pe.OPTIONAL_HEADER.Magic == 0x20B
directory = pe.OPTIONAL_HEADER.DATA_DIRECTORY[3]
assert directory.Size % 12 == 0
lo, hi = 0, directory.Size // 12
search = []
while lo < hi:
    index = (lo + hi) // 2
    table_rva = directory.VirtualAddress + index * 12
    record = pe.get_data(table_rva, 12)
    begin, end, unwind_rva = struct.unpack("<III", record)
    search.append({"index": index, "tableRva": hex(table_rva), "begin": hex(begin), "end": hex(end)})
    if SITE < begin:
        hi = index
    elif SITE >= end:
        lo = index + 1
    else:
        break
else:
    raise AssertionError("No RUNTIME_FUNCTION owns the pinned mid site")
assert begin == START and end == 0x156454E

header = pe.get_data(unwind_rva, 4)
version, flags = header[0] & 7, header[0] >> 3
prolog, count, frame_reg, frame_offset = header[1], header[2], header[3] & 15, header[3] >> 4
assert (version, flags, prolog, count, frame_reg, frame_offset) == (1, 0, 0x61, 10, 0, 0)
unwind_raw = pe.get_data(unwind_rva, 4 + ((count + 1) & ~1) * 2)
codes = []
cursor = 0
registers = ("rax", "rcx", "rdx", "rbx", "rsp", "rbp", "rsi", "rdi", "r8", "r9", "r10", "r11", "r12", "r13", "r14", "r15")
while cursor < count:
    offset, value = struct.unpack_from("BB", unwind_raw, 4 + cursor * 2)
    operation, information = value & 15, value >> 4
    item = {"codeOffset": hex(offset), "operation": operation, "information": information}
    if operation == 4:
        scaled = struct.unpack_from("<H", unwind_raw, 6 + cursor * 2)[0]
        item.update(name="UWOP_SAVE_NONVOL", register=registers[information], stackOffset=hex(scaled * 8))
        cursor += 2
    elif operation == 2:
        item.update(name="UWOP_ALLOC_SMALL", allocation=hex(information * 8 + 8))
        cursor += 1
    elif operation == 0:
        item.update(name="UWOP_PUSH_NONVOL", register=registers[information])
        cursor += 1
    else:
        raise AssertionError(f"Unexpected unwind op {operation}")
    codes.append(item)
assert cursor == count

md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
md.detail = True
function_bytes = pe.get_data(begin, end - begin)
instructions = list(md.disasm(function_bytes, begin))
assert instructions and instructions[-1].address + instructions[-1].size == end
assert sum(i.size for i in instructions) == end - begin
rows = [{"rva": hex(i.address), "bytes": i.bytes.hex(), "mnemonic": i.mnemonic, "operands": i.op_str} for i in instructions]
branches = []
indirect_branches = []
for i in instructions:
    if i.group(capstone.CS_GRP_JUMP) or i.group(capstone.CS_GRP_CALL):
        if i.operands[0].type == capstone.x86_const.X86_OP_IMM:
            branches.append({"source": hex(i.address), "mnemonic": i.mnemonic, "target": hex(i.operands[0].imm)})
        else:
            indirect_branches.append({"source": hex(i.address), "mnemonic": i.mnemonic, "operands": i.op_str})
interior = [edge for edge in branches if CANDIDATE < int(edge["target"], 16) < RESUME]
assert not interior
assert pe.get_data(CANDIDATE, 9).hex() == "33db33c00f1f440000"
assert pe.get_data(SITE, 5).hex() == "0f1f440000"
prior = json.loads(PRIOR.read_text(encoding="utf-8-sig"))
assert prior["inputs"]["gameAssemblySha256"] == digest
prior_range = next(r for r in prior["ranges"] if r["name"] == "UnityEvent.Invoke" and int(r["start"], 16) == START)
prior_bytes = pe.get_data(START, int(prior_range["end"], 16) - START)
assert hashlib.sha256(prior_bytes).hexdigest() == prior_range["sha256"]
for row in prior_range["instructions"]:
    pc = int(row["rva"], 16)
    expected = bytes.fromhex(row["bytes"])
    assert pe.get_data(pc, len(expected)) == expected

result = {
    "schemaVersion": 1, "kind": "bounded-unityevent-mid-site-static-evidence",
    "gameRuntimeVerified": False, "gamePatchPublished": False,
    "inputs": {"image": str(IMAGE), "sha256": digest, "priorEvidence": str(PRIOR), "priorEvidenceSha256": sha_file(PRIOR)},
    "pe": {"machine": hex(pe.FILE_HEADER.Machine), "imageBasePreferredOnly": hex(pe.OPTIONAL_HEADER.ImageBase),
           "exceptionDirectoryRva": hex(directory.VirtualAddress), "exceptionDirectorySize": hex(directory.Size),
           "lookup": "binary search only; no other UNWIND_INFO decoded", "lookupSteps": search},
    "runtimeFunction": {"index": index, "tableRva": hex(table_rva), "bytes": record.hex(), "begin": hex(begin), "endExclusive": hex(end),
                        "unwindRva": hex(unwind_rva), "functionBytesSha256": hashlib.sha256(function_bytes).hexdigest(), "instructions": len(rows)},
    "unwind": {"bytes": unwind_raw.hex(), "version": version, "flags": flags, "prologSize": prolog, "countSlots": count,
               "frameRegister": frame_reg, "frameOffset16": frame_offset, "operations": codes, "stackDeltaAfterProlog": "-0x28",
               "midRspModulo16": 0, "handlerOrChainedEntry": None},
    "site": {"candidate9Start": hex(CANDIDATE), "candidate9Bytes": pe.get_data(CANDIDATE, 9).hex(), "selected5Start": hex(SITE),
             "selected5Bytes": pe.get_data(SITE, 5).hex(), "resumePc": hex(RESUME), "callEncoding": "e8 + little-endian signed int32(relayVA - (loadedImageBase + 0x1564420))",
             "relayRequirement": "abs(relayVA - resumeVA) within signed int32; relay tail JMPs without register/flags/stack changes",
             "selected5Instruction": "NOP dword ptr [RAX + RAX]; performs no memory access",
             "xorBeforeCall": ["0x1564417 XOR EBX,EBX zeros full RBX", "0x1564419 XOR EAX,EAX zeros full RAX"],
             "flagsAfterXor": {"CF": 0, "OF": 0, "SF": 0, "ZF": 1, "PF": 1, "AF": "undefined by XOR; preserve live image, do not synthesize"},
             "replayNeededAtSelectedSite": False, "nineByteCallReturnWithoutAdjustment": "0x156441c",
             "allDirectEdgesInsideSelectedFunction": branches, "directTargetsInsideCandidate9Exclusive": interior,
             "indirectBranchesInsideSelectedFunction": indirect_branches,
             "scopeLimit": "No whole-image cross-reference scan. No claim that arbitrary external/indirect branches cannot enter these bytes."},
    "bridgePlan": {"frameBytes": "0x1b0", "configBytes": "0x28", "statsBytes": "0x28", "slots": 32,
                   "entryI": "source mid RSP - 8; [I] is actual CALL return PC", "pushOrder": ["push r13", "pushfq"],
                   "allocationN": "0x1e8", "frameBaseB": "I - 0x1f8", "BModulo16": 0,
                   "framePointer": "R13=B+0x80; .setframe r13,0x80", "savedFlags": "B+0x1e8", "savedOriginalR13": "B+0x1f0",
                   "actualReturnSlot": "B+0x1f8", "sourceMidRsp": "B+0x200", "callbackShadow": "B+0x00..0x1f",
                   "restoreFlagsBody": "push qword ptr [rsp+0x1e8]; popfq", "legalEpilog": ["lea rsp,[r13+0x170]", "pop r13", "ret"],
                   "preservedScope": "all 16 GPR values (source RSP included), live user RFLAGS, XMM0..15 low128, MXCSR, x87 control word; not an AVX upper-lane/x87-data snapshot",
                   "drainLimit": "C++ dispatch counters end before ASM restore/RET; enclosing physical native void0 parent must protect the full call window"},
    "publication": {"productionPublicationVerified": False, "fiveByteAtomicityProven": False,
                    "aligned8Start": "0x1564418", "aligned8OriginalBytes": pe.get_data(0x1564418, 8).hex(),
                    "aligned8ReplacementShape": "db33c0e8 + rel32", "instructionPublicationFromAtomicDataWriteProven": False,
                    "pinnedDobbyEvidence": "../judge-context-publication-20261020/dobby-static.json",
                    "pinnedDobbySha256": "8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded",
                    "decision": "Do not publish into a live game without a proven native execution safepoint and instruction-cache publication protocol."},
    "instructions": rows,
    "checks": {"pinnedImageHash": True, "priorOneRangeBytes": True, "oneRuntimeFunction": True,
               "complete148InstructionDecode": len(rows) == 148, "exactNineAndFiveBytes": True, "noDirectInteriorTargetInSelectedFunction": True},
    "sources": ["https://learn.microsoft.com/en-us/cpp/build/prolog-and-epilog?view=msvc-170",
                "https://learn.microsoft.com/en-us/cpp/build/exception-handling-x64?view=msvc-170"]
}
assert all(result["checks"].values())
(ROOT / "mid-site-evidence.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"passed": True, "imageSha256": digest, "runtimeFunction": [hex(begin), hex(end)], "instructions": len(rows),
                  "exceptionLookupSteps": len(search), "site": hex(SITE), "bytes": pe.get_data(SITE, 5).hex(), "noLocalDirectInteriorTargets": True}))
