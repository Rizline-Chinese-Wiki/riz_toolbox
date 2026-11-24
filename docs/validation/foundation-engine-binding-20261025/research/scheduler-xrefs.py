"""Finite direct-transfer/RIP-load cross references in executable PE sections."""
from pathlib import Path
import bisect
import hashlib
import json
import runpy
import struct

HERE = Path(__file__).resolve().parent
context = runpy.run_path(str(HERE/"scheduler-inspect.py"))
pe, functions, starts = context["pe"], context["functions"], context["starts"]
roots, region = context["roots"], context["region"]
targets = {0x44D630, 0x447750, 0x447AD0, 0x447C30, 0x447D60, 0x78F5E0, 0x78FA30, 0x78FC50,
           0x792240, 0x793840, 0x78F680, 0x78F840}
globals_ = {0x1CB3C00}
cache = {}


def decoded_at(address):
    index = bisect.bisect_right(starts, address)-1
    if index < 0 or not functions[index][0] <= address < functions[index][1]:
        return None, None
    primary = roots[index]
    if primary not in cache:
        function = region(address, f"xref group containing {hex(address)}")
        cache[primary] = (function, {int(x["rva"], 16): x for x in function["instructions"]})
    function, instructions = cache[primary]
    return function, instructions.get(address)


direct, rip = [], []
for section in pe.sections:
    if not section.Characteristics & 0x20000000:
        continue
    blob = section.get_data()[:min(section.SizeOfRawData, section.Misc_VirtualSize)]
    for opcode in (0xE8, 0xE9):
        at = 0
        while (at := blob.find(bytes([opcode]), at)) >= 0:
            start = at
            at += 1
            if start+5 > len(blob):
                continue
            address = section.VirtualAddress+start
            target = address+5+struct.unpack_from("<i", blob, start+1)[0]
            if target not in targets:
                continue
            function, instruction = decoded_at(address)
            if not instruction or instruction.get("directTarget") != hex(target):
                continue
            direct.append(dict(target=hex(target), source=hex(address), instruction=instruction,
                               callerBegin=function["beginRva"], callerEnd=function["endRva"]))
    # x64 RIP-relative load/LEA forms with a REX prefix. Every candidate must
    # also be a decoded instruction boundary in its full unwind-chain group.
    for opcode in (0x8B, 0x8D):
        at = 0
        while (at := blob.find(bytes([opcode]), at)) >= 0:
            middle = at
            at += 1
            if middle < 1 or middle+6 > len(blob) or blob[middle-1] & 0xF0 != 0x40 or blob[middle+1] & 0xC7 != 5:
                continue
            address = section.VirtualAddress+middle-1
            target = address+7+struct.unpack_from("<i", blob, middle+2)[0]
            if target not in globals_ | targets:
                continue
            function, instruction = decoded_at(address)
            if instruction:
                rip.append(dict(target=hex(target), source=hex(address), instruction=instruction,
                                callerBegin=function["beginRva"], callerEnd=function["endRva"]))
assert len(cache) <= 100, "Unexpectedly broad xref closure: inspect before expanding"
data_pointers = []
for section in pe.sections:
    if section.Characteristics & 0x20000000:
        continue
    raw = section.get_data()
    for target in sorted(targets):
        pattern = struct.pack("<Q", context["base"]+target)
        offset = 0
        while (offset := raw.find(pattern, offset)) >= 0:
            data_pointers.append(dict(source=hex(section.VirtualAddress+offset), target=hex(target),
                                      section=section.Name.decode().rstrip("\0")))
            offset += 1
result = dict(schemaVersion=1, kind="fixed-unityplayer-scheduler-direct-xrefs",
              scope="Direct E8/E9 and selected REX RIP load/LEA only, decoded instruction boundaries confirmed against complete selected RF/CHAININFO. Not all indirect calls or data references.",
              inputs=context["inputs"], directTargets=[hex(x) for x in sorted(targets)], globals=[hex(x) for x in sorted(globals_)],
              directTransfers=sorted(direct, key=lambda x:int(x["source"],16)),
              ripReferences=sorted(rip, key=lambda x:int(x["source"],16)),
              absoluteDataPointers=data_pointers,
              functions=[value[0] for _, value in sorted(cache.items())],
              gameCodeExecuted=False, allIndirectCallersEnumerated=False)
out = HERE/"scheduler-xrefs.json"
out.write_text(json.dumps(result, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
text = []
for function in result["functions"]:
    text.append(f"\n## {function['beginRva']}..{function['endRva']}")
    text.extend(f"{row['rva']:>10} {row['bytes']:<28} {row['op']:<9} {row['args']}" for row in function["instructions"])
assembly = HERE/"scheduler-xref-disassembly.txt"
assembly.write_text("\n".join(text)+"\n", encoding="utf-8")
print(json.dumps(dict(output=str(out), sha256=hashlib.sha256(out.read_bytes()).hexdigest(), functions=len(cache),
                      directTransfers=len(direct), ripReferences=len(rip),
                      assemblySha256=hashlib.sha256(assembly.read_bytes()).hexdigest(), gameCodeExecuted=False)))
