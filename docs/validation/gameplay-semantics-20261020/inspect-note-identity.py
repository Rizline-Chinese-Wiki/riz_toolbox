"""Pinned-build Note identity and all direct GetJudgeControl callsites; static only."""
from pathlib import Path
from datetime import datetime, timezone
import hashlib
import json
import re
import struct
import pefile
import capstone

ROOT = Path(__file__).resolve().parent
BINARY = Path(r"D:\SteamLibrary\steamapps\common\Rizline\GameAssembly.dll")
DUMP = Path(r"C:\Users\71957\Desktop\绿豆诡计v2\output\il2cpp_output\dump.cs")
METADATA = DUMP.with_name("script.json")
SHA = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79"
assert hashlib.sha256(BINARY.read_bytes()).hexdigest() == SHA
pe = pefile.PE(str(BINARY), fast_load=True)
md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
md.detail = True
bounds = {
    "GetJudgeControl.GetJudge": (0x3E8030, 0x3E80E0),
    "LevelControl.Start": (0x3EA580, 0x3ECF20),
    "NoteJudgeControl.CheckHold": (0x3FD010, 0x3FD090),
    "NoteJudgeControl.autoPredicate": (0x3FD410, 0x3FD9E0),
    "NoteJudgeControl.Update": (0x3FD9E0, 0x3FFAC0),
    "CheckHold.MoveNext": (0x402670, 0x4033A0),
    "List.referenceGetItem.nativeSemantics": (0x9A40B0, 0x9A4100),
    "List.referenceAdd.nativeSemantics": (0x33B0, 0x3420),
    "List.referenceRemoveAll.nativeSemantics": (0x9DCC00, 0x9DCD40),
}
functions = {n: list(md.disasm(pe.get_data(a, b-a), a)) for n, (a, b) in bounds.items()}


def expect(name, address, mnemonic, operands):
    i = next(i for i in functions[name] if i.address == address)
    assert (i.mnemonic, i.op_str) == (mnemonic, operands), (name, hex(address), i.mnemonic, i.op_str)


def float_constant(name, address):
    i = next(i for i in functions[name] if i.address == address)
    o = next(o for o in i.operands if o.type == capstone.x86.X86_OP_MEM and o.mem.base == capstone.x86.X86_REG_RIP)
    data = i.address + i.size + o.mem.disp
    return {"instructionRva": hex(address), "dataRva": hex(data), "value": struct.unpack("<f", pe.get_data(data, 4))[0]}


# Chart.lines[outer] is retained at stack+20; Line.notes[inner] becomes R15,
# and the very same managed Note reference is written into the created NoteInformation.
for a, m, o in [
    (0x3EB020, "mov", "rax, qword ptr [rax + 0x40]"),
    (0x3EB077, "mov", "rcx, qword ptr [rcx + 0x40]"),
    (0x3EB08D, "call", "0x9a40b0"),
    (0x3EB092, "mov", "qword ptr [rsp + 0x20], rax"),
    (0x3EBF93, "mov", "rsi, qword ptr [rsp + 0x20]"),
    (0x3EBFA0, "mov", "rax, qword ptr [rsi + 0x18]"),
    (0x3EBFC2, "call", "0x9a40b0"),
    (0x3EBFC7, "mov", "r15, rax"),
    (0x3EC188, "mov", "rdx, rdi"),
    (0x3EC18B, "call", "0x33b0"),
    (0x3EC190, "mov", "qword ptr [rdi + 0x20], r13"),
    (0x3EC1A0, "mov", "qword ptr [rdi + 0x28], r15"),
]:
    expect("LevelControl.Start", a, m, o)

# Exact incoming Windows x64 register assignments. script.json omits the implicit
# instance argument, so it alone must never be interpreted as a complete C ABI.
expect("GetJudgeControl.GetJudge", 0x3E8041, "mov", "edi, edx")
expect("GetJudgeControl.GetJudge", 0x3E8048, "mov", "rbx, rcx")
expect("GetJudgeControl.GetJudge", 0x3E804B, "movaps", "xmm6, xmm2")
expect("NoteJudgeControl.autoPredicate", 0x3FD42E, "mov", "rbx, rdx")
expect("NoteJudgeControl.autoPredicate", 0x3FD431, "mov", "rdi, rcx")
expect("NoteJudgeControl.CheckHold", 0x3FD020, "movzx", "ebp, r8b")
expect("NoteJudgeControl.CheckHold", 0x3FD024, "mov", "rdi, rdx")
expect("NoteJudgeControl.CheckHold", 0x3FD027, "mov", "rsi, rcx")
expect("NoteJudgeControl.CheckHold", 0x3FD066, "mov", "qword ptr [rbx + 0x20], rsi")
expect("NoteJudgeControl.CheckHold", 0x3FD073, "mov", "qword ptr [rbx + 0x28], rdi")
expect("NoteJudgeControl.CheckHold", 0x3FD082, "mov", "byte ptr [rbx + 0x30], bpl")
expect("CheckHold.MoveNext", 0x40268C, "mov", "rsi, rcx")
expect("CheckHold.MoveNext", 0x402726, "mov", "rdi, qword ptr [rsi + 0x20]")
expect("NoteJudgeControl.Update", 0x3FE990, "mov", "r14, rax")
expect("NoteJudgeControl.Update", 0x3FE454, "cmp", "byte ptr [rbx + 0x79], r12b")
expect("NoteJudgeControl.Update", 0x3FE458, "jne", "0x3ff2e2")
expect("NoteJudgeControl.Update", 0x3FF320, "call", "0x9dcc00")
expect("List.referenceGetItem.nativeSemantics", 0x9A40DA, "mov", "rax, qword ptr [rcx + rbx*8 + 0x20]")
expect("List.referenceAdd.nativeSemantics", 0x33E6, "mov", "dword ptr [rcx + 0x18], eax")
expect("List.referenceAdd.nativeSemantics", 0x33EF, "mov", "qword ptr [r10 + r9*8 + 0x20], rdx")
expect("List.referenceRemoveAll.nativeSemantics", 0x9DCC5A, "mov", "rdx, qword ptr [rax + rdx*8 + 0x20]")
expect("List.referenceRemoveAll.nativeSemantics", 0x9DCC5F, "call", "qword ptr [rsi + 0x18]")
expect("List.referenceRemoveAll.nativeSemantics", 0x9DCC62, "test", "al, al")
expect("List.referenceRemoveAll.nativeSemantics", 0x9DCD2C, "mov", "dword ptr [rdi + 0x18], ebp")

constants = {
    "bad": float_constant("NoteJudgeControl.Update", 0x3FE490),
    "dragInit": float_constant("NoteJudgeControl.Update", 0x3FF327),
    "dragTouchReset": float_constant("NoteJudgeControl.Update", 0x3FF4F4),
    "dragLoopReset": float_constant("NoteJudgeControl.Update", 0x3FF9FD),
    "missTap": float_constant("NoteJudgeControl.Update", 0x3FE03C),
    "missDrag": float_constant("NoteJudgeControl.Update", 0x3FE2D8),
    "holdMiss": float_constant("CheckHold.MoveNext", 0x402CCC),
    "holdCompletion": float_constant("CheckHold.MoveNext", 0x40337D),
}
assert constants["bad"]["value"] == -1.0
assert all(constants[k]["value"] == -2.0 for k in ("dragInit", "dragTouchReset", "dragLoopReset", "holdCompletion", "missDrag"))
assert all(constants[k]["value"] == 1.0 for k in ("missTap", "holdMiss"))

# All calls in these *decoded* methods; byte scan of executable sections is only
# an additional discovery check, not evidence that an arbitrary E8 is code.
actual = {i.address for seq in functions.values() for i in seq if i.mnemonic == "call" and i.op_str == "0x3e8030"}
rows = [
    (0x3FD72B, "auto", "RBX", "RDI", "TimingDifference", None, "raw1"),
    (0x3FD75F, "auto", "RBX", "RDI", "TimingDifference", None, "raw4"),
    (0x3FE186, "tap/hold timeout", "[RBX+0x40].Item[EDI]", "RBX", "Sentinel", 1.0, "raw0-or-5"),
    (0x3FE265, "tap/hold timeout", "[RBX+0x40].Item[EDI]", "RBX", "Sentinel", 1.0, "raw0-or-5"),
    (0x3FE373, "drag timeout", "[RBX+0x48].Item[EDI]", "RBX", "Sentinel", -2.0, "raw0-or-5"),
    (0x3FEA08, "tap/hold head max", "R14", "RBX", "TimingDifference", None, "raw4"),
    (0x3FEF8A, "bad visual/request", "R14", "RBX", "Sentinel", -1.0, "raw-minus1"),
    (0x3FF2A3, "tap/hold head", "R14", "RBX", "TimingDifference", None, "raw1-or-2-or-3"),
    (0x3FF569, "drag success", "[RBX+0x48].Item[EDI]", "RBX", "Sentinel", -2.0, "raw4"),
    (0x3FF73C, "drag success", "[RBX+0x48].Item[EDI]", "RBX", "Sentinel", -2.0, "raw1"),
    (0x402CD7, "hold continuation miss", "[RSI+0x28]", "RDI == [RSI+0x20]", "Sentinel", 1.0, "raw0-or-5"),
    (0x403388, "hold completion", "[RSI+0x28]", "RDI == [RSI+0x20]", "Sentinel", -2.0, "raw1-or-2"),
]
assert actual == {r[0] for r in rows}
discovery = []
for section in pe.sections:
    if not section.Characteristics & 0x20000000:
        continue
    data = section.get_data()
    for offset in range(len(data)-4):
        if data[offset] == 0xE8:
            rva = section.VirtualAddress + offset
            if rva+5+struct.unpack_from("<i", data, offset+1)[0] == 0x3E8030:
                discovery.append(rva)
assert set(discovery) == actual

dump = DUMP.read_text(encoding="utf-8")


def class_body(name):
    matches = re.findall(r"^public class " + re.escape(name) + r"(?: : [^\n]+)?\n\{\n(.*?)^\}", dump, re.M | re.S)
    assert len(matches) == 1, (name, len(matches))
    return matches[0]


classes = {name: class_body(name) for name in ("GetJudgeControl", "NoteJudgeControl", "<CheckHold>d__32", "Note", "NoteInformation", "Chart", "Line")}
assert "InLevel.LevelInformation levelInformation; // 0x20" in classes["GetJudgeControl"]
assert len([x for x in classes["GetJudgeControl"].splitlines() if " // 0x" in x]) == 1
assert "InLevel.Note noteData; // 0x28" in classes["NoteInformation"]
assert "InLevel.NoteInformation holdNoteInformation; // 0x28" in classes["<CheckHold>d__32"]
metadata = json.loads(METADATA.read_text(encoding="utf-8"))["ScriptMethod"]
methods = [m for m in metadata if m["Address"] in (0x3E8030, 0x3FD410, 0x3FD010, 0x402670)]
assert len(methods) == 4
proof = {
    "kind": "static-only", "runtimeIdentityVerified": False,
    "observedAtUtc": datetime.now(timezone.utc).isoformat(), "binary": str(BINARY), "sha256": SHA,
    "dumpSha256": hashlib.sha256(DUMP.read_bytes()).hexdigest(),
    "metadataWarning": "script.json signatures omit implicit instance receiver; use dump instance ownership plus native register evidence",
    "metadata": methods, "classes": classes,
    "exactAbi": {
        "GetJudgeControl.GetJudge": "void(RCX this, EDX int32, XMM2 float32, R9 MethodInfo*)",
        "NoteJudgeControl.autoPredicate": "bool8 AL(RCX this, RDX NoteInformation*, R8 MethodInfo*)",
        "NoteJudgeControl.CheckHold": "reference RAX(RCX this, RDX NoteInformation*, R8B bool8, R9 MethodInfo*)",
        "CheckHold.MoveNext": "bool8 AL(RCX this, RDX MethodInfo*)",
    },
    "chartIdentity": {
        "intrinsicNoteIdPresent": False,
        "referencePath": "LevelInformation.chart.lines[lineOrdinal].notes[noteOrdinal] -> NoteInformation.noteData (same reference)",
        "sourceNote": "0x3ebfc2 List<Note>.get_Item -> R15", "assignment": "0x3ec1a0 [RDI+0x28] = R15",
        "warning": "LineInformation is a rendered segment; one chart Line may have multiple segments. No arithmetic segment index -> chart Line identity is asserted.",
    },
    "constants": constants,
    "callsites": [{"callRva": hex(a), "returnRva": hex(a+5), "source": source,
                   "callerNoteSource": note, "callerNoteJudgeOwner": owner,
                   "deltaSemantics": semantics, "sentinel": sentinel, "rawKind": raw}
                  for a, source, note, owner, semantics, sentinel, raw in rows],
    "discoveryE8Matches": [hex(a) for a in discovery],
    "coverage": {"totalDirectCallsites": 12, "typedUpstreamContextCallsites": 4,
                 "otherUpdateLocalCallsites": 8, "timingDifferenceCallsites": 4,
                 "sentinelCallsites": 8, "currentManagedBrokerCanCaptureCallerRegisters": False},
    "functions": {name: [{"rva": hex(i.address), "mnemonic": i.mnemonic, "operands": i.op_str} for i in insns]
                  for name, insns in functions.items()},
}
path = ROOT / "note-identity-proof.json"
path.write_text(json.dumps(proof, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"passed": True, "methods": len(methods), "decodedCallsites": len(actual),
                  "timingDifferenceCallsites": 4, "typedUpstreamContextCallsites": 4,
                  "runtimeIdentityVerified": False, "proofSha256": hashlib.sha256(path.read_bytes()).hexdigest()}))
