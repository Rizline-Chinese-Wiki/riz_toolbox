"""Reproduce selected consumption/continuation groups, with no native execution."""
from pathlib import Path
import hashlib
import json
import runpy
import struct

HERE = Path(__file__).resolve().parent
context = runpy.run_path(str(HERE/"scheduler-inspect.py"))
selected = [
    (0x447750, "Scheduler manager drain/destroy: direct erase and unconditional cleanup"),
    (0x447C30, "Scheduler erase without cleanup"),
    (0x447D60, "Scheduler pump consuming/requeuing before callback"),
    (0x78FC50, "Yield/continuation variants"),
    (0x342400, "Async continuation shared-reference release"),
    (0x695340, "Scheduler node freelist allocation"),
    (0x3F01B0, "Scheduler tree erase"),
    (0xF7750, "AsyncOperation InternalDestroy icall"),
    (0xF7790, "AsyncOperation isDone icall and native vtable+8"),
    (0x5DA510, "Observed matching async callback/cleanup record shape in shutdown drain; concrete yielded-object provenance not proven"),
]
functions = [context["region"](entry, label) for entry, label in selected]
pe, base = context["pe"], context["base"]
icalls = []
for index, expected_name, expected_target in [
    (2218, "UnityEngine.AsyncOperation::InternalDestroy", 0xF7750),
    (2219, "UnityEngine.AsyncOperation::get_isDone", 0xF7790),
]:
    name = struct.unpack("<Q", pe.get_data(0x18DB7A0+index*8, 8))[0]-base
    target = struct.unpack("<Q", pe.get_data(0x18D4AE0+index*8, 8))[0]-base
    actual_name = pe.get_data(name, 150).split(b"\0")[0].decode("ascii")
    assert actual_name == expected_name and target == expected_target
    icalls.append(dict(index=index, name=actual_name, targetRva=hex(target), nameRva=hex(name),
                       namePointerRva=hex(0x18DB7A0+index*8), targetPointerRva=hex(0x18D4AE0+index*8)))
result = dict(schemaVersion=1, kind="fixed-unityplayer-scheduler-consumption-research", inputs=context["inputs"],
              functions=functions, asyncIcallBindings=icalls,
              scope="Bounded selected full RF/CHAININFO static disk inspection only",
              gameCodeExecuted=False)
output = HERE/"scheduler-consumption.json"
output.write_text(json.dumps(result, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
lines = []
for function in functions:
    lines.append(f"\n## {function['label']} {function['beginRva']}..{function['endRva']}")
    lines.extend(f"{row['rva']:>10} {row['bytes']:<28} {row['op']:<9} {row['args']}" for row in function["instructions"])
assembly = HERE/"scheduler-consumption-disassembly.txt"
assembly.write_text("\n".join(lines)+"\n", encoding="utf-8")
print(json.dumps(dict(output=str(output), sha256=hashlib.sha256(output.read_bytes()).hexdigest(),
                      functions=len(functions), instructions=sum(len(x["instructions"]) for x in functions),
                      assemblySha256=hashlib.sha256(assembly.read_bytes()).hexdigest(), gameCodeExecuted=False)))
