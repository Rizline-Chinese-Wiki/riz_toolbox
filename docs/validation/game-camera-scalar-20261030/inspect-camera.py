"""Read-only fixed-build, full-runtime-function camera survey. Never loads the PE."""
import hashlib
import json
import re
import sys
from pathlib import Path
import capstone
import pefile

binary, dump, symbols, destination = map(Path, sys.argv[1:5])
raw = binary.read_bytes()
binary_hash = hashlib.sha256(raw).hexdigest()
dump_hash = hashlib.sha256(dump.read_bytes()).hexdigest()
if binary_hash != "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79":
    raise SystemExit("Unsupported PE")
if dump_hash != "092cdb96914ccb7af3f31d9d2e703e00dd6ddff63fd559f11ac6b42e6635c1cb":
    raise SystemExit("Unsupported dump")
dump_text = dump.read_text(encoding="utf-8")
def in_level_class(name):
    match = re.search(r"// Namespace: InLevel\npublic class " + re.escape(name) + r" : MonoBehaviour\n\{(.*?)\n\}", dump_text, re.S)
    if match is None:
        raise SystemExit("Missing exact dump class " + name)
    return match.group(1)
judge_block = in_level_class("NoteJudgeControl")
input_block = in_level_class("InputControl")
if "UnityEngine.Camera _camera; // 0x70" not in judge_block or "System.Void Start(); // RVA: 0x3FD3E0" not in judge_block or "_camera;" in input_block:
    raise SystemExit("Camera owner correction disagrees with the fixed dump")
pe = pefile.PE(data=raw, fast_load=True)
pe.parse_data_directories(directories=[pefile.DIRECTORY_ENTRY[name] for name in (
    "IMAGE_DIRECTORY_ENTRY_IMPORT", "IMAGE_DIRECTORY_ENTRY_EXPORT", "IMAGE_DIRECTORY_ENTRY_EXCEPTION")])
imports = [{"dll": row.dll.decode(), "symbols": [item.name.decode() if item.name else "ordinal:" + str(item.ordinal) for item in row.imports]}
           for row in getattr(pe, "DIRECTORY_ENTRY_IMPORT", [])]
exports = [{"name": row.name.decode() if row.name else None, "rva": hex(row.address)}
           for row in getattr(getattr(pe, "DIRECTORY_ENTRY_EXPORT", None), "symbols", [])]
names = {}
for row in json.loads(symbols.read_text(encoding="utf-8"))["ScriptMethod"]:
    names.setdefault(row["Address"], []).append(row["Name"])
decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
decoder.detail = True
scopes = [
    ("InLevel.CameraControl.SetScale", 0x3E2E00),
    ("InLevel.CameraControl.SetXPosition", 0x3E3060),
    ("InLevel.CameraControl.Update", 0x3E32A0),
    ("InLevel.NoteJudgeControl.Start", 0x3FD3E0),
    ("UnityEngine.Component.get_transform", 0x1536440),
    ("UnityEngine.Transform.get_localPosition", 0x1561640),
    ("UnityEngine.Transform.set_localPosition", 0x1561CD0),
    ("UnityEngine.Transform.IsChildOf", 0x15605F0),
]
rows = []
for label, address in scopes:
    matches = [row.struct for row in pe.DIRECTORY_ENTRY_EXCEPTION if row.struct.BeginAddress <= address < row.struct.EndAddress]
    if len(matches) != 1:
        raise SystemExit("Expected one full runtime-function entry for " + label)
    rf = matches[0]
    next_symbol = min(value for value in names if value > address)
    fragments = sorted([row.struct for row in pe.DIRECTORY_ENTRY_EXCEPTION if address <= row.struct.BeginAddress < next_symbol], key=lambda row: row.BeginAddress)
    if rf.BeginAddress != address or not fragments or any(row.EndAddress > next_symbol for row in fragments):
        raise SystemExit("Runtime-function boundaries disagree with exact method span: " + label)
    function_end = max(row.EndAddress for row in fragments)
    size = function_end - address
    if size > 16384:
        raise SystemExit("Unexpected camera RF size")
    body = pe.get_data(address, size)
    instructions = []
    for instruction in decoder.disasm(body, address):
        item = {"rva": hex(instruction.address), "bytes": instruction.bytes.hex(),
                "mnemonic": instruction.mnemonic, "operands": instruction.op_str}
        if instruction.mnemonic in ("call", "jmp") and instruction.operands and instruction.operands[0].type == capstone.x86.X86_OP_IMM:
            item["targetNames"] = names.get(instruction.operands[0].imm, [])
        instructions.append(item)
    if sum(len(bytes.fromhex(item["bytes"])) for item in instructions) != len(body):
        raise SystemExit("Incomplete camera method decode: " + label)
    row = {"name": label, "requestedRva": hex(address), "rfBegin": hex(address), "rfEnd": hex(function_end),
           "nextScriptMethod": hex(next_symbol), "runtimeFunctions": [
               {"begin": hex(fragment.BeginAddress), "end": hex(fragment.EndAddress), "bytes": fragment.EndAddress-fragment.BeginAddress,
                "sha256": hashlib.sha256(pe.get_data(fragment.BeginAddress, fragment.EndAddress-fragment.BeginAddress)).hexdigest()} for fragment in fragments],
           "bytes": size, "sha256": hashlib.sha256(body).hexdigest(), "instructions": instructions}
    rows.append(row)
    print(label, row["rfBegin"], row["rfEnd"], size, row["sha256"])
    for item in instructions:
        print(item["rva"], item["mnemonic"], item["operands"], ",".join(item.get("targetNames", [])))
result = {"schemaVersion": 1, "evidenceKind": "full-runtime-function-static-disassembly", "gameAssemblySha256": binary_hash,
          "dumpSha256": dump_hash, "machine": hex(pe.FILE_HEADER.Machine), "imports": imports, "exports": exports,
          "sourceFacts": {"actualCameraOwner": "InLevel.NoteJudgeControl", "cameraField": "_camera", "cameraFieldOffset": "0x70",
                          "informationOwnerField": "noteJudgeControl", "informationOwnerFieldOffset": "0x58", "originStartRva": "0x3fd3e0",
                          "inputControlHasCameraField": False, "rigOwner": "InLevel.CameraControl", "rigInformationOffset": "0x20",
                          "rigScalar": "Transform.localPosition.x", "scalarUnit": "unity-local-unit", "numericMinimum": None, "numericMaximum": None,
                          "runtimeCameraRigCorrelationRequired": "Cached Camera transform is the rig transform or IsChildOf(rig)",
                          "callbackOrder": "CameraControl.Update overwrites this property each frame; host/camera/render order and sustained visibility remain unverified.",
                          "gameplayEffect": "Camera position can affect input projection/gameplay; no harmlessness claim."},
          "scopes": rows, "runtimeAcceptance": False, "tools": {"pefile": pefile.__version__, "capstone": capstone.__version__}}
destination.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
