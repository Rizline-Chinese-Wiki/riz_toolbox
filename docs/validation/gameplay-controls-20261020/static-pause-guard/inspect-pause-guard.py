"""Read-only, pinned-build PauseUI/control-flow evidence. Never a runtime test."""
import hashlib
import json
import re
import struct
from datetime import datetime, timezone
from pathlib import Path

import capstone
import pefile

BINARY = Path(r"D:\SteamLibrary\steamapps\common\Rizline\GameAssembly.dll")
DUMP = Path(r"C:\Users\71957\Desktop\绿豆诡计v2\output\il2cpp_output\dump.cs")
EXPECTED = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79"
ROOT = Path(__file__).resolve().parent
assert hashlib.sha256(BINARY.read_bytes()).hexdigest() == EXPECTED, "Unsupported game build"
pe = pefile.PE(str(BINARY), fast_load=True)
pe.parse_data_directories(directories=[
    pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_IMPORT"],
    pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_EXPORT"],
])
assert pe.FILE_HEADER.Machine == 0x8664
decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
decoder.detail = True
dump = DUMP.read_text(encoding="utf-8")


def class_text(name):
    # The relevant classes have no nested bodies in Il2CppDumper's flat listing.
    match = re.search(r"^public class " + re.escape(name) + r"(?:\s*:[^\r\n]+)?\s*\{.*?^\}", dump, re.M | re.S)
    assert match is not None, name
    return match.group(0)


pause_metadata = class_text("PauseUIControl")
controller_metadata = class_text("NormalModeInLevelController")
progress_metadata = class_text("ProgressControl")
for field, offset in (("_pausing", "B4"), ("_over", "C0"), ("_terminated", "C1"), ("_pauseTime", "BC")):
    assert re.search(r"\b" + field + r"; // 0x" + offset + r"\b", pause_metadata)
assert "System.Boolean _skip; // 0x51" in controller_metadata
assert "System.Boolean prepareToPlay; // 0x40" in progress_metadata
assert "System.Boolean isPlaying; // 0x42" in progress_metadata

# Bounds are exact method starts and the next start from dump.cs; trailing INT3
# padding is kept visible. Each decode starts at a verified method boundary.
bounds = {
    "PauseUIControl.CanDoOperation": (0x3C49D0, 0x3C49E7),
    "PauseUIControl.ctor": (0x3C49B0, 0x3C49D0),
    "PauseUIControl.Back": (0x3C3A50, 0x3C3AA0),
    "PauseUIControl.Pause": (0x3C3AA0, 0x3C3C70),
    "PauseUIControl.Resume": (0x3C3C70, 0x3C3D40),
    "PauseUIControl.Update": (0x3C3E20, 0x3C49B0),
    "PauseUIControl.Pause.delayed_callback": (0x3B1B60, 0x3B1B69),
    "NormalModeInLevelController.Pause": (0x3A2410, 0x3A2440),
    "NormalModeInLevelController.Resume": (0x3A2440, 0x3A2470),
    "NormalModeInLevelController.Retry": (0x3C1310, 0x3C1540),
    "NormalModeInLevelController.Skip": (0x3C17D0, 0x3C17F0),
    "NormalModeInLevelController.LevelOver": (0x3C0C00, 0x3C1040),
    "NormalModeInLevelController.ControllerOver": (0x3BEA60, 0x3BED10),
    "NormalModeInLevelController.Update": (0x3C19E0, 0x3C2480),
    "InLevel.ProgressControl.Play": (0x400700, 0x400810),
    "TouchBlocker.get_Blocked": (0x3830A0, 0x383110),
    "WindowControl.get_HasActiveWindow": (0x3869B0, 0x386A20),
    "UnityEngine.Time.get_timeSinceLevelLoad": (0x155F090, 0x155F0C0),
}
decoded = {name: list(decoder.disasm(pe.get_data(start, end - start), start))
           for name, (start, end) in bounds.items()}


def expect(name, address, mnemonic, operands):
    insn = next(i for i in decoded[name] if i.address == address)
    assert (insn.mnemonic, insn.op_str) == (mnemonic, operands), (name, hex(address), insn.mnemonic, insn.op_str)


expect("PauseUIControl.CanDoOperation", 0x3C49D0, "cmp", "byte ptr [rcx + 0xb4], 0")
expect("PauseUIControl.CanDoOperation", 0x3C49D7, "jne", "0x3c49dc")
expect("PauseUIControl.CanDoOperation", 0x3C49D9, "xor", "al, al")
expect("PauseUIControl.CanDoOperation", 0x3C49DC, "cmp", "byte ptr [rcx + 0xc0], 0")
expect("PauseUIControl.CanDoOperation", 0x3C49E3, "sete", "al")
expect("PauseUIControl.Pause", 0x3C3AF9, "mov", "word ptr [rbx + 0xb4], 0x101")
assert not any(i.mnemonic in ("call", "jmp") and i.op_str == "0x400700" for i in decoded["PauseUIControl.Pause"])
expect("PauseUIControl.Update", 0x3C42FD, "call", "0x400700")
expect("PauseUIControl.Update", 0x3C4307, "call", "0x3c3aa0")
expect("PauseUIControl.Update", 0x3C46CA, "call", "0x400700")
expect("PauseUIControl.Update", 0x3C46D4, "call", "0x3c3aa0")
expect("NormalModeInLevelController.Pause", 0x3A2429, "xor", "edx, edx")
expect("NormalModeInLevelController.Pause", 0x3A242F, "jmp", "0x400700")
expect("PauseUIControl.Resume", 0x3C3CB8, "mov", "dword ptr [rbx + 0xa0], 0xffffffff")
expect("PauseUIControl.Resume", 0x3C3CC2, "mov", "dword ptr [rbx + 0xbc], 0x40400000")
expect("PauseUIControl.Resume", 0x3C3CE6, "mov", "dl, 1")
expect("PauseUIControl.Resume", 0x3C3CE8, "call", "0x400700")
expect("InLevel.ProgressControl.Play", 0x400704, "cmp", "byte ptr [rcx + 0x42], dl")
expect("InLevel.ProgressControl.Play", 0x40070D, "cmp", "byte ptr [rcx + 0x40], 0")
expect("InLevel.ProgressControl.Play", 0x4007A4, "mov", "word ptr [rcx + 0x40], 1")
expect("NormalModeInLevelController.Retry", 0x3C1373, "cmp", "byte ptr [rax + 0xb4], 0")
expect("NormalModeInLevelController.Retry", 0x3C1380, "cmp", "byte ptr [rax + 0xc0], 0")
expect("NormalModeInLevelController.Skip", 0x3C17D3, "mov", "byte ptr [rcx + 0x51], 1")
expect("NormalModeInLevelController.Skip", 0x3C17DE, "jmp", "qword ptr [rax + 0x188]")
expect("NormalModeInLevelController.LevelOver", 0x3C0DFA, "mov", "dword ptr [rdi + 0xa0], 0")


def float_operand(name, address):
    i = next(i for i in decoded[name] if i.address == address)
    memory = next(o.mem for o in i.operands
                  if o.type == capstone.x86.X86_OP_MEM and o.mem.base == capstone.x86.X86_REG_RIP)
    rva = i.address + i.size + memory.disp
    return {"instructionRva": hex(address), "constantRva": hex(rva), "value": struct.unpack("<f", pe.get_data(rva, 4))[0]}


constants = {
    "pauseUi.minimumSceneSeconds": float_operand("PauseUIControl.Update", 0x3C3EC9),
    "pauseUi.exitProgressThreshold": float_operand("PauseUIControl.Update", 0x3C4453),
    "pauseUi.touchDragThreshold": float_operand("PauseUIControl.Update", 0x3C467B),
    "pauseUi.blockReleaseDelaySeconds": float_operand("PauseUIControl.Pause", 0x3C3C3A),
    "focusLoss.minimumSceneSeconds": float_operand("NormalModeInLevelController.Update", 0x3C23F5),
}
imports = [{"dll": item.dll.decode("ascii", "replace"), "symbols": [
    entry.name.decode("ascii", "replace") if entry.name else "ordinal:" + str(entry.ordinal)
    for entry in item.imports]} for item in getattr(pe, "DIRECTORY_ENTRY_IMPORT", [])]
exports = [{"name": item.name.decode("ascii", "replace") if item.name else None,
            "ordinal": item.ordinal, "rva": hex(item.address)}
           for item in getattr(getattr(pe, "DIRECTORY_ENTRY_EXPORT", None), "symbols", [])]

evidence = {
    "kind": "static-only", "runtimeControlVerified": False,
    "observedAtUtc": datetime.now(timezone.utc).isoformat(),
    "binary": str(BINARY), "sha256": EXPECTED, "architecture": "Windows x64",
    "dump": str(DUMP), "dumpSha256": hashlib.sha256(DUMP.read_bytes()).hexdigest(),
    "tools": {"pefile": pefile.__version__, "capstone": capstone.__version__},
    "E-imports": imports, "E-exports": exports,
    "metadata": {"PauseUIControl": pause_metadata, "NormalModeInLevelController": controller_metadata,
                 "ProgressControl": progress_metadata},
    "constants": constants,
    "exactPauseGuardMethods": [
        {"assembly": "Assembly-CSharp.dll", "namespace": "", "class": "TouchBlocker",
         "method": "get_Blocked", "static": True, "parameters": [], "return": "System.Boolean", "rva": "0x3830a0"},
        {"assembly": "Assembly-CSharp.dll", "namespace": "", "class": "WindowControl",
         "method": "get_HasActiveWindow", "static": True, "parameters": [], "return": "System.Boolean", "rva": "0x3869b0"},
        {"assembly": "UnityEngine.CoreModule.dll", "namespace": "UnityEngine", "class": "Time",
         "method": "get_timeSinceLevelLoad", "static": True, "parameters": [], "return": "System.Single", "rva": "0x155f090"},
    ],
    "functions": {name: {"startRva": hex(bounds[name][0]), "endExclusiveRva": hex(bounds[name][1]),
        "instructions": [{"rva": hex(i.address), "mnemonic": i.mnemonic, "operands": i.op_str} for i in insns]}
        for name, insns in decoded.items()},
    "findings": [
        {"id": "F-CanDo", "status": "static-observed", "formula": "_pausing && !_over"},
        {"id": "F-Pause", "status": "static-observed", "callflow": "ProgressControl.Play(false) -> PauseUIControl.Pause()"},
        {"id": "F-Resume", "status": "static-observed", "callflow": "PauseUIControl.Resume() -> ProgressControl.Play(true); prepareToPlay starts before actual isPlaying"},
        {"id": "F-Skip", "status": "static-observed", "callflow": "_skip=true -> virtual LevelOver; LevelOver's skip branch clears _levelOverTime"},
    ],
}
(ROOT / "pause-guard-static-proof.json").write_text(json.dumps(evidence, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
listing = []
for name, insns in decoded.items():
    listing.append(f"\n{name} {hex(bounds[name][0])}..{hex(bounds[name][1])}")
    listing.extend(f"{i.address:08x} {i.mnemonic:9} {i.op_str}" for i in insns)
(ROOT / "disassembly.txt").write_text("\n".join(listing) + "\n", encoding="utf-8")
print(json.dumps({"passed": True, "runtimeControlVerified": False, "functions": len(decoded),
                  "importDlls": len(imports), "exports": len(exports), "constants": constants,
                  "proofSha256": hashlib.sha256((ROOT / "pause-guard-static-proof.json").read_bytes()).hexdigest()}))
