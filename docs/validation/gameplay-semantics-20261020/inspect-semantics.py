"""Pinned-build judgement/time semantics. Static evidence, no runtime or source mutations."""
from datetime import datetime, timezone
from pathlib import Path
import hashlib
import json
import re
import struct
import capstone
import pefile

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
BINARY = Path(r"D:\SteamLibrary\steamapps\common\Rizline\GameAssembly.dll")
DUMP = Path(r"C:\Users\71957\Desktop\绿豆诡计v2\output\il2cpp_output\dump.cs")
SHA = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79"
assert hashlib.sha256(BINARY.read_bytes()).hexdigest() == SHA
pe = pefile.PE(str(BINARY), fast_load=True)
pe.parse_data_directories(directories=[pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_IMPORT"], pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_EXPORT"]])
decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
decoder.detail = True
dump = DUMP.read_text(encoding="utf-8")
bounds = {
    "NormalMode.GetJudge": (0x3C0310, 0x3C0B38),
    "InLevelController.forwardJudge": (0x371D60, 0x371D80),
    "GetJudgeControl.GetJudge": (0x3E8030, 0x3E80E0),
    "NoteJudgeControl.Update": (0x3FD9E0, 0x3FFAC0),
    "NoteJudgeControl.autoPredicate": (0x3FD410, 0x3FD9E0),
    "CheckHold.MoveNext": (0x402670, 0x4033A0),
    "NoteInformation.SendRendererEvent": (0x3FCF70, 0x3FD010),
    "ProgressControl.Update": (0x400FE0, 0x4016A0),
    "ProgressControl.RealTimeToChartTime": (0x400C80, 0x400EF0),
    "ProgressControl.ChartTimeToRealTime": (0x400400, 0x400670),
    "MUGMusicPlayer.GetTime": (0x3FBB10, 0x3FBD90),
    "LevelControl.Start": (0x3EA580, 0x3ECF20),
    "Chart.get_ChartDelay": (0x3E5670, 0x3E5690),
}
functions = {name: list(decoder.disasm(pe.get_data(start, end - start), start)) for name, (start, end) in bounds.items()}


def expect(name, address, mnemonic, operands):
    i = next(i for i in functions[name] if i.address == address)
    assert (i.mnemonic, i.op_str) == (mnemonic, operands), (name, hex(address), i.mnemonic, i.op_str)


def constant(name, address, size=4):
    i = next(i for i in functions[name] if i.address == address)
    operand = next(o for o in i.operands if o.type == capstone.x86.X86_OP_MEM and o.mem.base == capstone.x86.X86_REG_RIP)
    rva = i.address + i.size + operand.mem.disp
    return {"instructionRva": hex(address), "dataRva": hex(rva), "value": struct.unpack("<d" if size == 8 else "<f", pe.get_data(rva, size))[0]}


expect("NormalMode.GetJudge", 0x3C0321, "mov", "esi, edx")
expect("NormalMode.GetJudge", 0x3C032B, "mov", "rdi, rcx")
expect("NormalMode.GetJudge", 0x3C032E, "movaps", "xmm6, xmm2")
expect("NormalMode.GetJudge", 0x3C0508, "mov", "eax, dword ptr [rbx + rcx*4 + 0x3c0b38]")
expect("GetJudgeControl.GetJudge", 0x3E806C, "cmp", "byte ptr [rax + 0xc0], 0")
expect("GetJudgeControl.GetJudge", 0x3E80B9, "movaps", "xmm2, xmm6")
expect("NoteJudgeControl.Update", 0x3FDE9B, "mov", "eax, dword ptr [rax + 0xb8]")
expect("NoteJudgeControl.Update", 0x3FDEA4, "mov", "dword ptr [rbx + 0x64], eax")
expect("NoteJudgeControl.Update", 0x3FF295, "movss", "xmm2, dword ptr [rbx + 0x64]")
expect("NoteJudgeControl.Update", 0x3FF29A, "subss", "xmm2, dword ptr [r14 + 0x34]")
expect("NoteJudgeControl.Update", 0x3FF2A3, "call", "0x3e8030")
expect("CheckHold.MoveNext", 0x403388, "call", "0x3e8030")
expect("NoteInformation.SendRendererEvent", 0x3FCF7D, "mov", "dword ptr [rcx + 0x68], 0")
expect("NoteInformation.SendRendererEvent", 0x3FCFD8, "mov", "ecx, dword ptr [rbx + 0x68]")
expect("NoteInformation.SendRendererEvent", 0x3FCFDB, "inc", "ecx")
expect("NoteInformation.SendRendererEvent", 0x3FCFDD, "mov", "dword ptr [rbx + 0x68], ecx")
expect("ProgressControl.Update", 0x401218, "call", "0x3fbb10")
expect("ProgressControl.Update", 0x401224, "subsd", "xmm1, qword ptr [rbx + 0x48]")
expect("ProgressControl.Update", 0x401257, "movsd", "qword ptr [rbx + 0x50], xmm1")
expect("ProgressControl.Update", 0x401272, "movss", "dword ptr [rax + 0xb4], xmm0")
expect("ProgressControl.Update", 0x401296, "movss", "dword ptr [rax + 0xb8], xmm0")
expect("ProgressControl.Update", 0x4012A7, "call", "0x400c80")
expect("ProgressControl.Update", 0x4012B5, "movss", "dword ptr [rdi + 0xb0], xmm0")
expect("LevelControl.Start", 0x3ECC45, "call", "0x400400")
expect("LevelControl.Start", 0x3ECC53, "movss", "dword ptr [rdi + 0x34], xmm0")
expect("LevelControl.Start", 0x3ECD93, "call", "0x400400")
expect("LevelControl.Start", 0x3ECDA1, "movss", "dword ptr [rbx + 0x38], xmm0")

constants = {
    "holdCompletionSentinel": constant("CheckHold.MoveNext", 0x40337D),
    "missSentinel": constant("CheckHold.MoveNext", 0x402CCC),
    "missUpdateSentinel": constant("NoteJudgeControl.Update", 0x3FE03C),
    "normalJudgeSentinel": constant("NormalMode.GetJudge", 0x3C04E6),
    "deltaDisplayMultiplier": constant("NormalMode.GetJudge", 0x3C094A),
    "emptyBpmMapSecondsDivisor": constant("ProgressControl.RealTimeToChartTime", 0x400ED5, 8),
    "bpmMapSecondsDivisor": constant("ProgressControl.RealTimeToChartTime", 0x400E90, 8),
    "emptyInverseMapMultiplier": constant("ProgressControl.ChartTimeToRealTime", 0x40064E, 8),
    "inverseMapMultiplier": constant("ProgressControl.ChartTimeToRealTime", 0x40060D, 8),
    "chartDelayMsDivisor": constant("Chart.get_ChartDelay", 0x3E5678),
}
assert constants["holdCompletionSentinel"]["value"] == -2.0
assert constants["normalJudgeSentinel"]["value"] == -2.0
assert constants["missSentinel"]["value"] == constants["missUpdateSentinel"]["value"] == 1.0
assert constants["deltaDisplayMultiplier"]["value"] == constants["chartDelayMsDivisor"]["value"] == 1000.0
for key in ("emptyBpmMapSecondsDivisor", "bpmMapSecondsDivisor", "emptyInverseMapMultiplier", "inverseMapMultiplier"):
    assert constants[key]["value"] == 60.0

expected_switch = {-1: 0x3C0514, 0: 0x3C062E, 1: 0x3C0526, 2: 0x3C05DD, 3: 0x3C060E, 4: 0x3C0626, 5: 0x3C062E}
actual_switch = {value: struct.unpack("<I", pe.get_data(0x3C0B38 + (value + 1) * 4, 4))[0] for value in expected_switch}
assert actual_switch == expected_switch
enum_mapping = [
    {"raw": -1, "nativeCounterMeaning": "Bad", "switchTarget": "0x3c0514", "counter": "_bad++"},
    {"raw": 0, "nativeCounterMeaning": "Miss", "switchTarget": "0x3c062e", "counter": "_miss++"},
    {"raw": 1, "nativeCounterMeaning": "Perfect", "switchTarget": "0x3c0526", "counter": "_perfect++"},
    {"raw": 2, "nativeCounterMeaning": "RizTimeEarlyPerfect", "switchTarget": "0x3c05dd", "counter": "_perfect++, _early++, _earlyIngame++"},
    {"raw": 3, "nativeCounterMeaning": "RizTimeLatePerfect", "switchTarget": "0x3c060e", "counter": "_perfect++, _late++, _lateIngame++"},
    {"raw": 4, "nativeCounterMeaning": "RizTimeMaxPerfect", "switchTarget": "0x3c0626", "counter": "_maxPerfect++"},
    {"raw": 5, "nativeCounterMeaning": "RizTimeMiss", "switchTarget": "0x3c062e", "counter": "_miss++, _challengeTimeMiss++"},
]

source_hashes = {}
for relative in ["src/RizToolbox.Sdk/HookContracts.cs", "src/RizToolbox.Sdk/GameplayContracts.cs",
                 "src/RizToolbox.Adapter/Modules/ExactGameplayHookModule.cs", "src/RizToolbox.Adapter/Gameplay/GameplayService.cs",
                 "src/RizToolbox.Adapter/Gameplay/GameplayNativeBackend.cs"]:
    source_hashes[relative] = hashlib.sha256((REPO / relative).read_bytes()).hexdigest()

proof = {
    "kind": "static-only", "runtimeUnitsVerified": False, "noteIdentityAtJudgeVerified": False,
    "observedAtUtc": datetime.now(timezone.utc).isoformat(), "binary": str(BINARY), "sha256": SHA,
    "dumpSha256": hashlib.sha256(DUMP.read_bytes()).hexdigest(),
    "tools": {"pefile": pefile.__version__, "capstone": capstone.__version__},
    "imports": [{"dll": i.dll.decode(), "count": len(i.imports)} for i in pe.DIRECTORY_ENTRY_IMPORT],
    "exportsCount": len(pe.DIRECTORY_ENTRY_EXPORT.symbols), "constants": constants,
    "judgeAbi": "void(receiver RCX, enum32 EDX, float32 XMM2, MethodInfo* R9)",
    "enumMapping": {"source": "native switch table + counter field uses; dump enum literal values are missing", "cases": enum_mapping},
    "units": {"delta": "seconds only for verified time-difference call paths; -2 sentinel and miss sentinel are not measured offsets",
              "nowRealTimeAndNowSyncedTime": "same float(_nowTime double) in Progress.Update",
              "nowTimeEmptyMap": "realSeconds * chart.bPM / 60",
              "nowTimeShiftMap": "key.time + (realSeconds-key.floorPosition) * key.value * chart.bPM / 60",
              "inverseEmptyMap": "chartCoordinate * 60 / chart.bPM",
              "inverseShiftMap": "key.floorPosition + (chartCoordinate-key.time) * 60 / key.value / chart.bPM"},
    "noteIndex": "NoteInformation._index at 0x68 is a renderer loop cursor, not a note identity",
    "judgeCallsites": [{"function": name, "rva": hex(i.address)} for name, insns in functions.items()
                       for i in insns if i.mnemonic == "call" and i.op_str == "0x3e8030"],
    "sourceSha256": source_hashes,
    "functions": {name: [{"rva": hex(i.address), "mnemonic": i.mnemonic, "operands": i.op_str} for i in insns]
                  for name, insns in functions.items()},
}
(ROOT / "static-proof.json").write_text(json.dumps(proof, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"passed": True, "functions": len(functions), "judgeCallsites": len(proof["judgeCallsites"]),
                  "runtimeUnitsVerified": False, "noteIdentityAtJudgeVerified": False,
                  "proofSha256": hashlib.sha256((ROOT / "static-proof.json").read_bytes()).hexdigest()}))
