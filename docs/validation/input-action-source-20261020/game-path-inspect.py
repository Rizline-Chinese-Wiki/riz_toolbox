"""Pinned read-only native input investigation. No process access or game changes.

Run with C:/Python314/python.exe. All addresses are RVAs in one SHA-locked PE.
Selected unwind-bounded code regions and validated direct xrefs are retained as JSON;
neither execution order of independent Unity callbacks nor runtime device state
is established by this script.
"""
from bisect import bisect_right
from collections import defaultdict
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import struct

import capstone
import pefile

ROOT = Path(__file__).resolve().parent
BINARY = Path(r"D:\SteamLibrary\steamapps\common\Rizline\GameAssembly.dll")
DUMP = Path(r"C:\Users\71957\Desktop\绿豆诡计v2\output\il2cpp_output\dump.cs")
EXPECTED_SHA = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79"
raw = BINARY.read_bytes()
assert hashlib.sha256(raw).hexdigest() == EXPECTED_SHA
pe = pefile.PE(data=raw, fast_load=False)
decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
decoder.detail = True
methods = []
classes = []
current = None
namespace = ""
for line_no, line in enumerate(DUMP.read_text(encoding="utf-8-sig").splitlines(), 1):
    if line.startswith("// Namespace: "):
        namespace = line[len("// Namespace: "):]
    found = re.match(r"public (?:class|struct|enum|interface) ([^ :]+)", line)
    if found:
        current = {"namespace": namespace, "type": found[1], "line": line_no, "declaration": line, "members": []}
        classes.append(current)
    elif current is not None and line.startswith("\t"):
        current["members"].append({"line": line_no, "text": line.strip()})
        found = re.search(r"(.+?)\s+([^\s(]+)\((.*?)\); // RVA: (0x[0-9a-fA-F]+)", line.strip())
        if found:
            methods.append({"namespace": namespace, "type": current["type"], "method": found[2],
                            "signature": line.strip(), "line": line_no, "rva": int(found[4], 16)})

aliases = defaultdict(list)
for item in methods:
    aliases[item["rva"]].append(item)
method_starts = sorted(rva for rva in aliases if rva)
bounds = sorted((entry.struct.BeginAddress, entry.struct.EndAddress)
                for entry in pe.DIRECTORY_ENTRY_EXCEPTION)
bound_starts = [start for start, _ in bounds]


def short_alias(rva):
    names = [".".join(filter(None, [m["namespace"], m["type"], m["method"]])) for m in aliases.get(rva, [])]
    return {"rva": hex(rva), "aliases": names[:12], "aliasCount": len(names)}


def function_bound(rva):
    idx = bisect_right(bound_starts, rva) - 1
    if idx >= 0 and bounds[idx][0] <= rva < bounds[idx][1]:
        return bounds[idx][0], bounds[idx][1], ".pdata"
    # Leaf functions can omit unwind information. Cap the read at next known
    # metadata start and mark it as a bounded prefix, rather than claiming a CFG.
    idx = bisect_right(method_starts, rva)
    stop = min(method_starts[idx] if idx < len(method_starts) else rva + 128, rva + 128)
    return rva, stop, "metadata-next-start-bounded-prefix"


decoded = {}


def function(rva):
    if rva in decoded:
        return decoded[rva]
    start, end, source = function_bound(rva)
    items = list(decoder.disasm(pe.get_data(start, end - start), start))
    result = {"requestedRva": hex(rva), "start": hex(start), "endExclusive": hex(end), "boundsSource": source,
              "methodAliases": short_alias(rva), "instructions": [], "directCalls": []}
    for insn in items:
        record = {"rva": hex(insn.address), "bytes": insn.bytes.hex(), "mnemonic": insn.mnemonic, "operands": insn.op_str}
        if insn.mnemonic in ("call", "jmp") and insn.operands and insn.operands[0].type == capstone.CS_OP_IMM:
            dest = insn.operands[0].imm
            record["target"] = short_alias(dest)
            result["directCalls"].append(record)
        result["instructions"].append(record)
    decoded[rva] = result
    return result


selected = {
    "GameManager.Update": 0x36F030,
    "LowLevelTouchControl.OnEnable": 0x3A7630,
    "LowLevelTouchControl.OnDisable": 0x3A75D0,
    "LowLevelTouchControl.UpdateTouch": 0x3A7690,
    "InputActionControl.Awake": 0x3726D0,
    "InputActionControl.GetAction": 0x3728B0,
    "InputActionControl.IsPressed": 0x372C00,
    "InputActionControl.OnDisable": 0x372D50,
    "InputActionControl.OnEnable": 0x372DF0,
    "InputActionControl.WasPressedThisFrame": 0x372F80,
    "InputActions.ctor": 0x373680,
    "InputDeviceDetector.ctor": 0x374950,
    "InputDeviceDetector.OnActionPerformed": 0x3744C0,
    "InputDeviceDetector.OnMouseEvent": 0x374500,
    "InputDeviceDetector.ApplyDevice": 0x374120,
    "InLevel.InputControl.Update": 0x3E9F80,
    "InLevel.InputControl.UpdateKeyboardInputs": 0x3E97F0,
    "MainMenuControl.Update": 0x3F1760,
    "PauseUIControl.Update": 0x3C3E20,
    "InputSystem.Update(type)": 0x129C8F0,
    "InputSystem.Update()": 0x129CA60,
    "InputSystem.get_version": 0x129DA80,
    "InputSystem.get_settings": 0x129DA10,
    "InputManager.Update(type)": 0x1306D00,
    "InputManager.Update()": 0x1306DC0,
    "InputManager.get_defaultUpdateType": 0x1307740,
    "InputManager.ApplySettings": 0x12FAE40,
    "InputManager.OnBeforeUpdate": 0x12FF800,
    "InputManager.OnUpdate": 0x1300220,
    "InputManager.InvokeAfterUpdateCallback": 0x12FED70,
    "InputUpdate.OnBeforeUpdate": 0x136C630,
    "InputUpdate.OnUpdate": 0x136C6A0,
    "InputUpdate.IsPlayerUpdate": 0x136C620,
    "InputUpdate.GetUpdateTypeForPlayer": 0x136C5F0,
    "InputState.get_currentUpdateType": 0x136C450,
    "InputState.get_updateCount": 0x136C490,
    "ButtonControl.get_wasPressedThisFrame": 0x126B310,
    "ButtonControl.get_wasReleasedThisFrame": 0x126B410,
    "ButtonControl.BeginTestingForFramePresses": 0x126AFC0,
    "ButtonControl.UpdateWasPressed": 0x126B0F0,
    "InputAction.WasPressedThisFrame": 0x124B8D0,
    "Keyboard.get_current": 0x12BC720,
    "Keyboard.get_Item": 0x12BC590,
    "Keyboard.get_f8Key": 0x12BC9E0,
    "Keyboard.get_spaceKey": 0x12BCDF0,
    "Mouse.get_current": 0x12BD4E0,
    "InputControlExtensions.ReadValueAsObject": 0x12908D0,
    "InputSystemUIInputModule.Process": 0x1339060,
    "InputSystemUIInputModule.OnPointCallback": 0x1335E80,
    "InputSystemUIInputModule.OnLeftClickCallback": 0x13358F0,
    "InputSystemUIInputModule.ProcessPointerButton": 0x1337730,
    "StandaloneInputModule.Process": 0x1798580,
    "LegacyInput.GetKeyDown": 0x157DF40,
    "LegacyInput.CheckDisabled": 0x157DEE0,
}
selected.update({"EventSystem." + m["method"]: m["rva"] for m in methods
                 if m["namespace"] == "UnityEngine.EventSystems" and m["type"] == "EventSystem"
                 and m["method"] in ("Update", "TickModules", "get_currentInputModule")})

targets = {rva for name, rva in selected.items() if name in (
    "InputActionControl.WasPressedThisFrame", "InputActionControl.IsPressed", "Keyboard.get_current", "Mouse.get_current",
    "ButtonControl.get_wasPressedThisFrame", "InputAction.WasPressedThisFrame", "InputSystem.Update(type)",
    "InputSystem.Update()", "InputUpdate.OnUpdate", "InputManager.InvokeAfterUpdateCallback", "LegacyInput.GetKeyDown")}
targets.update(m["rva"] for m in methods if m["namespace"] == "UnityEngine" and m["type"] == "Input"
               and m["method"] in ("GetMouseButton", "GetMouseButtonDown", "get_mousePosition"))
direct_xrefs = {hex(target): [] for target in sorted(targets)}
unvalidated = []
for section in pe.sections:
    if not section.Characteristics & 0x20000000:
        continue
    data = section.get_data()
    base = section.VirtualAddress
    for opcode in (b"\xe8", b"\xe9"):
        offset = -1
        while True:
            offset = data.find(opcode, offset + 1)
            if offset < 0 or offset + 5 > len(data):
                break
            at = base + offset
            target = at + 5 + struct.unpack_from("<i", data, offset + 1)[0]
            if target not in targets:
                continue
            start, end, source = function_bound(at)
            # A raw E8/E9 byte sequence in data or an immediate is not an xref.
            # Accept only instruction boundaries reached from unwind function
            # start. Prefix-only leaf candidates are recorded separately.
            if source != ".pdata":
                unvalidated.append({"rva": hex(at), "target": hex(target), "reason": "no unwind function bound"})
                continue
            caller = function(start)
            item = next((i for i in caller["directCalls"] if int(i["rva"], 16) == at
                         and i["target"]["rva"] == hex(target)), None)
            if item is not None:
                direct_xrefs[hex(target)].append({"rva": hex(at), "mnemonic": item["mnemonic"],
                                                   "caller": short_alias(start), "callerEndExclusive": hex(end)})

for rva in selected.values():
    function(rva)
# Some managed methods have several unwind entries. The first entry for this
# getter ends at a fall-through, so preserve its remaining metadata-bounded span
# explicitly. This is a code span, not an independently recovered function CFG.
extra_spans = {
    "ButtonControl.wasPressedThisFrame.metadataSpan": [
        {"rva": hex(i.address), "bytes": i.bytes.hex(), "mnemonic": i.mnemonic, "operands": i.op_str}
        for i in decoder.disasm(pe.get_data(0x126B310, 0x100), 0x126B310)]}


def expect(rva, at, mnemonic, operands):
    found = next(i for i in function(rva)["instructions"] if int(i["rva"], 16) == at)
    assert (found["mnemonic"], found["operands"]) == (mnemonic, operands), found


expect(0x3A7630, 0x3A7674, "mov", "edx, 3")
expect(0x3A7630, 0x3A7680, "jmp", "0x1311590")
expect(0x3A75D0, 0x3A7614, "mov", "edx, 1")
expect(0x3A75D0, 0x3A7620, "jmp", "0x1311590")
expect(0x3A7690, 0x3A7764, "call", "0x129ca60")
expect(0x1300220, 0x1300421, "call", "0x136c6a0")
expect(0x1300220, 0x13010AC, "call", "0x12fed70")
expect(0x1300220, 0x13010FA, "call", "0x12fed70")
expect(0x12FED70, 0x12FEDA7, "cmp", "ebx, 8")
expect(0x12FED70, 0x12FEDAA, "je", "0x12fedf5")
expect(0x126B310, 0x126B36A, "cmp", "dword ptr [rcx], eax")
imports = [{"dll": entry.dll.decode(errors="replace"),
            "symbols": [i.name.decode(errors="replace") if i.name else "ordinal:" + str(i.ordinal) for i in entry.imports]}
           for entry in pe.DIRECTORY_ENTRY_IMPORT]
exports = [{"name": item.name.decode(errors="replace") if item.name else None, "rva": hex(item.address)}
           for item in pe.DIRECTORY_ENTRY_EXPORT.symbols]
class_names = {"GameManager", "InputActionControl", "InputActions", "InputDeviceDetector", "InputControl", "LowLevelTouchControl",
               "InputUpdate", "ButtonControl", "InputSettings", "InputUpdateType"}
report = {
    "kind": "pinned-static-evidence", "runtimeInputVerified": False,
    "observedAtUtc": datetime.now(timezone.utc).isoformat(), "binary": str(BINARY), "sha256": EXPECTED_SHA,
    "dump": str(DUMP), "dumpSha256": hashlib.sha256(DUMP.read_bytes()).hexdigest(),
    "tools": {"capstone": capstone.__version__, "pefile": pefile.__version__,
              "ida": "not installed in current tool index; existing parent task uses local metadata + capstone fallback"},
    "E-imports": imports, "E-exports": exports,
    "PE": {"machine": hex(pe.FILE_HEADER.Machine), "imageBase": hex(pe.OPTIONAL_HEADER.ImageBase),
           "sections": [{"name": s.Name.rstrip(b"\x00").decode(errors="replace"), "rva": hex(s.VirtualAddress),
                         "size": s.Misc_VirtualSize} for s in pe.sections]},
    "selectedTargets": {name: hex(rva) for name, rva in selected.items()},
    "metadataClasses": [c for c in classes if c["type"] in class_names],
    "functions": {hex(rva): body for rva, body in sorted(decoded.items())},
    "extraCodeSpans": extra_spans,
    "validatedDirectXrefs": direct_xrefs,
    "rawCandidatesWithoutUnwindBounds": unvalidated,
    "limitations": ["Direct E8/E9 transfers only; virtual, indirect, generic-shared and inlined callers are not an exhaustive call graph.",
                    "Static callback code cannot prove current updateMode, active EventSystem module, source device identity or Host sampling phase.",
                    "Dump enum field comments are storage offsets, not numeric enum values.",
                    "Methods with shared RVAs retain multiple metadata aliases; a shared body alone cannot choose its receiver type.",
                    "A managed method may span several unwind entries; an unwind-bounded region alone is not necessarily its whole method body."]}
output = ROOT / "game-path-static.json"
output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"passed": True, "runtimeInputVerified": False, "output": str(output),
                  "selectedFunctions": len(selected), "xrefTargets": len(targets),
                  "validatedXrefs": sum(map(len, direct_xrefs.values())),
                  "sha256": hashlib.sha256(output.read_bytes()).hexdigest()}))
