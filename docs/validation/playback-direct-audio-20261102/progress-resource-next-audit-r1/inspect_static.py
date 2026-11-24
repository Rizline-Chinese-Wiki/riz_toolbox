"""Read-only fixed-image and source inspection. Writes only beside this script.
No DLL loading, mapping, entry-point invocation, game process, or long test.
"""
from pathlib import Path
import bisect, hashlib, json, re, struct, sys
import capstone, pefile

OUT = Path(__file__).resolve().parent
REPO = Path(r"C:\Users\71957\Desktop\riz_toolbox")
IMAGE = Path(r"D:\riz-build-cache-next\playback-direct-audio-managed-r1-20261102\mapped-r4\image\GameAssembly.dll")
DUMP = Path(r"C:\Users\71957\Desktop\绿豆诡计v2\output\il2cpp_output\dump.cs")
SCRIPT = DUMP.with_name("script.json")
EXPECTED_IMAGE = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79"
EXPECTED_DUMP = "092cdb96914ccb7af3f31d9d2e703e00dd6ddff63fd559f11ac6b42e6635c1cb"

def sha(b): return hashlib.sha256(b).hexdigest()
def save(name, obj): (OUT / name).write_text(json.dumps(obj, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
def hx(n): return hex(n)
def reference(p):
    b = p.read_bytes()
    return {"path": str(p), "sha256": sha(b), "length": len(b)}

image_bytes, dump_bytes = IMAGE.read_bytes(), DUMP.read_bytes()
assert sha(image_bytes) == EXPECTED_IMAGE
assert sha(dump_bytes) == EXPECTED_DUMP
pe = pefile.PE(data=image_bytes, fast_load=True)
pe.parse_data_directories(directories=[0, 1])
base = pe.OPTIONAL_HEADER.ImageBase
md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
md.detail = True
rf_directory = pe.OPTIONAL_HEADER.DATA_DIRECTORY[3]
rf = list(struct.iter_unpack("<III", pe.get_data(rf_directory.VirtualAddress, rf_directory.Size)))
rf_starts = [x[0] for x in rf]
lines = dump_bytes.decode("utf-8-sig").splitlines()
ns, owner, declarations = "", "", []
for num, line in enumerate(lines, 1):
    if line.startswith("// Namespace: "): ns = line[len("// Namespace: "):]
    if re.match(r"(?:public|private|internal|protected).*\b(?:class|struct|interface)\b", line): owner = line
    match = re.search(r"// RVA: 0x([0-9A-Fa-f]+)", line)
    if match: declarations.append({"line": num, "namespace": ns, "owner": owner,
                                  "rva": int(match[1], 16), "declaration": line.strip()})

imports = [{"dll": x.dll.decode(), "symbols": [{"name": s.name.decode() if s.name else None, "ordinal": s.ordinal,
             "iatRva": hx(s.address-base)} for s in x.imports]} for x in pe.DIRECTORY_ENTRY_IMPORT]
exports = [{"name": x.name.decode() if x.name else None, "ordinal": x.ordinal, "rva": hx(x.address)}
           for x in pe.DIRECTORY_ENTRY_EXPORT.symbols]
save("pe-survey.json", {"image": reference(IMAGE), "machine": hx(pe.FILE_HEADER.Machine), "imageBase": hx(base),
    "entryPointRva": hx(pe.OPTIONAL_HEADER.AddressOfEntryPoint), "sizeOfImage": pe.OPTIONAL_HEADER.SizeOfImage,
    "sectionTable": [{"name": x.Name.rstrip(b"\x00").decode(), "rva": hx(x.VirtualAddress),
        "virtualSize": x.Misc_VirtualSize, "rawSize": x.SizeOfRawData, "flags": hx(x.Characteristics)} for x in pe.sections],
    "runtimeFunctionCount": len(rf), "importCount": sum(len(x["symbols"]) for x in imports),
    "exportCount": len(exports), "imports": imports, "exports": exports,
    "tools": {"python": sys.version, "pefile": pefile.__version__, "capstone": capstone.__version__},
    "execution": {"dllLoad": False, "dllMain": False, "gameMethods": False, "gameProcess": False}})

def containing(addr):
    idx = bisect.bisect_right(rf_starts, addr) - 1
    return rf[idx] if idx >= 0 and rf[idx][0] <= addr < rf[idx][1] else None

def insns(rva, length):
    return [{"rva": hx(i.address-base), "bytes": bytes(i.bytes).hex(), "mnemonic": i.mnemonic, "operands": i.op_str}
            for i in md.disasm(pe.get_data(rva, length), base+rva)]

def function(name, rva, leaf_len=None):
    row = containing(rva)
    length = leaf_len if leaf_len is not None else row[1]-rva
    body = pe.get_data(rva, length)
    return {"name": name, "rva": hx(rva), "length": length, "sha256": sha(body), "bytes": body.hex(),
        "runtimeFunction": {"begin": hx(row[0]), "end": hx(row[1]), "unwind": hx(row[2])} if row else None,
        "rfOverlap": [{"begin": hx(b), "end": hx(e), "unwind": hx(u)} for b,e,u in rf if b < rva+length and e > rva],
        "methodDeclarations": [x for x in declarations if x["rva"] == rva], "instructions": insns(rva, length)}

functions = [function("ProgressControl.set_Clip shared leaf", 0x4016f0, 13),
    function("ProgressControl.get_Clip shared leaf", 0x4016d0, 5),
    function("ProgressControl.set_MusicPlayer shared leaf", 0x401700, 13),
    function("ProgressControl.<Start>d__25.MoveNext", 0x404990),
    function("ProgressControl.<Start>b__25_0", 0x400f60),
    function("ProgressControl.OnDestroy", 0x4006d0),
    function("ProgressControl.Start", 0x400ef0)]
tail = 0x4016f0+13+struct.unpack("<i", pe.get_data(0x4016f0+9, 4))[0]
functions.append(function("set_Clip barrier first tail", tail, 5))
functions.append(function("barrier second tail", 0x2f5750, 5))
functions.append(function("export il2cpp_gc_wbarrier_set_field", 0x284100, 11))
functions.append(function("exported barrier store and continuation", 0x2f4910, 8))
functions.append(function("barrier terminal leaf through RET", 0x320b20, 0x320b6c-0x320b20))
functions.append(function("generic interface invocation helper", 0x2ef0))
save("fixed-bodies.json", {"image": reference(IMAGE), "dump": reference(DUMP), "tailRva": hx(tail),
    "matchingExports": [x for x in exports if int(x["rva"],16) == tail or "wbarrier" in (x["name"] or "")], "functions": functions})
(OUT / "fixed-bodies.asm.txt").write_text("\n\n".join(
    f"{f['name']} @{f['rva']} length={f['length']} sha256={f['sha256']} RF={f['runtimeFunction']}\n" +
    "\n".join(f"{x['rva']:>10}  {x['bytes']:<32} {x['mnemonic']} {x['operands']}" for x in f["instructions"])
    for f in functions)+"\n", encoding="utf-8")

targets = {0x4016f0, 0x4016d0, 0x404990, 0x400f60}
candidates = []
for sec in pe.sections:
    if not sec.Characteristics & 0x20000000: continue
    data = sec.get_data()
    for m in re.finditer(b"[\xe8\xe9]", data):
        at = m.start()
        if at+5 > len(data): continue
        rva = sec.VirtualAddress+at
        target = rva+5+struct.unpack_from("<i", data, at+1)[0]
        if target not in targets: continue
        row = containing(rva)
        aligned = False
        if row:
            aligned = any(i.address-base == rva and i.size == 5 and i.mnemonic in ("call","jmp")
                          for i in md.disasm(pe.get_data(row[0], row[1]-row[0]),base+row[0]))
        candidates.append({"atRva": hx(rva), "targetRva": hx(target), "kind": "call" if data[at] == 0xe8 else "jmp",
            "instructionAlignedInRf": aligned, "rf": [hx(v) for v in row] if row else None,
            "callerDeclarations": [x for x in declarations if row and x["rva"] == row[0]]})
save("direct-reference-scan.json", {"method": "E8/E9 byte candidates checked against whole containing RF instruction boundaries; unaligned remain untrusted", "targets": [hx(x) for x in sorted(targets)], "references": candidates})
excerpts = []
for start,end,label in [(91685,91728,"resource declarations"),(98128,98175,"Progress declarations")]:
    excerpts.append({"label":label, "firstLine":start, "lastLine":end, "lines":[{"line":i,"text":lines[i-1]} for i in range(start,end+1)]})
save("metadata-excerpts.json", {"dump": reference(DUMP), "excerpts": excerpts,
    "sharedSetterDeclarationCount": sum(x["rva"] == 0x4016f0 for x in declarations),
    "sharedSetterDeclarations": [x for x in declarations if x["rva"] == 0x4016f0]})
script_methods = json.loads(SCRIPT.read_text(encoding="utf-8-sig"))["ScriptMethod"]
save("script-method-excerpts.json", {"source": reference(SCRIPT), "warning": "Existing dump signatures do not consistently display IL2CPP hidden self; infer native registers from exact machine code instead.",
    "methods": [x for x in script_methods if x["Address"] in {0x4016f0,0x4016d0,0x263f60,0x2f5750,0x2ef0,0x1540a40}]})
print(json.dumps({"imageSha256":sha(image_bytes), "dumpSha256":sha(dump_bytes), "tailRva":hx(tail),
    "tailExports":[x for x in exports if int(x["rva"],16)==tail], "sharedSetterDeclarations":sum(x["rva"]==0x4016f0 for x in declarations),
    "directReferences":candidates},ensure_ascii=False,indent=2))
