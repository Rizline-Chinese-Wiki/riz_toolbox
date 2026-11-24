"""Bounded, read-only PE/dump cross-check. No process launch, patch or network."""
import argparse, bisect, hashlib, json, platform, re
from pathlib import Path
from collections import defaultdict
import capstone
from capstone.x86 import X86_OP_IMM, X86_OP_MEM, X86_REG_RIP
import pefile

EXPECTED = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79"
SCOPES = {
    "NormalMode.LoadWorkshopLevel": 0x3C10D0,
    "LevelOver.LoadWorkshopLevel": 0x3CF8A0,
    "LevelOver.OnDestroy": 0x3CFA50,
    "WorkshopLevel.Initialize": 0x3E0C50,
    "WorkshopLevel.Initialize.MoveNext": 0x3D3040,
    "WorkshopLevel.ctor": 0x3E12A0,
    "WorkshopLevel.Validate": 0x3E0FD0,
    "WorkshopLevel.LoadChart": 0x3E0D00,
    "WorkshopLevel.LoadMusic": 0x3E0EA0,
    "WorkshopLevel.LoadMusic.MoveNext": 0x3D59B0,
    "WorkshopLevel.LoadOGG": 0x3E0F30,
    "WorkshopLevel.LoadOGG.MoveNext": 0x3D5CC0,
    "WorkshopLevel.LoadOGG.Dispose": 0x3D60E0,
    "WorkshopLevel.LoadOGG.Finally1": 0x3D6230,
    "WorkshopLevel.LoadOGG.Finally2": 0x3D6280,
    "WorkshopLevel.LoadMP3": 0x3E0E10,
    "WorkshopLevel.LoadMP3.MoveNext": 0x3D5140,
    "WorkshopLevel.LoadMP3.Dispose": 0x3D57C0,
    "WorkshopLevel.LoadMP3.Finally1": 0x3D5910,
    "WorkshopLevel.LoadMP3.Finally2": 0x3D5960,
    "WorkshopCard.LoadCover.MoveNext": 0x3D39F0,
    "WorkshopCard.StartPlayLevel.MoveNext": 0x3D7630,
}
parser = argparse.ArgumentParser()
parser.add_argument("--game", type=Path, required=True)
parser.add_argument("--dump", type=Path, required=True)
parser.add_argument("--symbols", type=Path, required=True)
parser.add_argument("--out", type=Path, required=True)
args = parser.parse_args()
data = args.game.read_bytes()
sha = hashlib.sha256(data).hexdigest()
if sha != EXPECTED:
    raise SystemExit("Unsupported image; no static target assumptions applied: " + sha)
pe = pefile.PE(data=data, fast_load=True)
pe.parse_data_directories(directories=[pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_IMPORT"],
                                     pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_EXPORT"]])
methods = json.loads(args.symbols.read_text(encoding="utf-8"))["ScriptMethod"]
by_address = defaultdict(list)
for method in methods:
    by_address[method["Address"]].append(method)
addresses = sorted(by_address)
args.out.mkdir(parents=True, exist_ok=True)
imports = {entry.dll.decode(errors="replace"): [
    item.name.decode(errors="replace") if item.name else f"ordinal:{item.ordinal}" for item in entry.imports
] for entry in getattr(pe, "DIRECTORY_ENTRY_IMPORT", [])}
exports = [{"name": item.name.decode(errors="replace") if item.name else f"ordinal:{item.ordinal}",
            "rva": hex(item.address)}
           for item in getattr(getattr(pe, "DIRECTORY_ENTRY_EXPORT", None), "symbols", [])]
triage = {
    "quality": "readable-native-PE-imports-and-exports",
    "sha256": sha, "machine": hex(pe.FILE_HEADER.Machine), "imageBase": hex(pe.OPTIONAL_HEADER.ImageBase),
    "clrDirectorySize": pe.OPTIONAL_HEADER.DATA_DIRECTORY[14].Size,
    "imports": imports, "exports": exports,
    "sections": [{"name": s.Name.rstrip(b"\0").decode(), "entropy": round(s.get_entropy(), 3),
                  "virtualAddress": hex(s.VirtualAddress), "rawSize": s.SizeOfRawData} for s in pe.sections],
    "limitations": ["Readable static anchors do not exclude dynamic API loading.",
                    "No binary execution, network contact, runtime validation or hook installation."],
    "tools": {"python": platform.python_version(), "capstone": capstone.__version__, "pefile": pefile.__version__}
}
(args.out / "triage.json").write_text(json.dumps(triage, indent=2), encoding="utf-8")
md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
md.detail = True
result, text = [], []
for name, rva in SCOPES.items():
    end = addresses[bisect.bisect_right(addresses, rva)]
    if end - rva > 0x1800:
        raise SystemExit(f"Scope too large without a deliberate review: {name} {end-rva:x}")
    instructions, calls = [], []
    text.append(f"\n=== {name} [{rva:08x}, {end:08x}) ===")
    for method in by_address[rva]:
        text.append(method["Name"] + " :: " + method["Signature"])
    for insn in md.disasm(pe.get_data(rva, end-rva), rva):
        line = {"rva": hex(insn.address), "mnemonic": insn.mnemonic, "operands": insn.op_str, "bytes": insn.bytes.hex()}
        annotation = ""
        if insn.mnemonic in ("call", "jmp") and insn.operands and insn.operands[0].type == X86_OP_IMM:
            destination = insn.operands[0].imm
            names = [item["Name"] for item in by_address.get(destination, [])]
            line["destinationNames"] = names
            if insn.mnemonic == "call":
                calls.append({"at": hex(insn.address), "to": hex(destination), "names": names})
            if names:
                annotation = " ; " + " | ".join(names[:5]) + (" ..." if len(names) > 5 else "")
        instructions.append(line)
        text.append(f"{insn.address:08x} {insn.mnemonic:9} {insn.op_str}{annotation}")
    result.append({"name": name, "rva": hex(rva), "end": hex(end), "symbols": by_address[rva],
                   "calls": calls, "instructions": instructions})
(args.out / "disassembly.txt").write_text("\n".join(text), encoding="utf-8")
(args.out / "boundaries.json").write_text(json.dumps({
    "schemaVersion": 1, "evidenceKind": "static-disassembly", "gameAssemblySha256": sha,
    "dumpSha256": hashlib.sha256(args.dump.read_bytes()).hexdigest(),
    "scriptJsonSha256": hashlib.sha256(args.symbols.read_bytes()).hexdigest(),
    "runtimeAcceptance": False, "scopes": result
}, indent=2), encoding="utf-8")
dump = args.dump.read_text(encoding="utf-8").splitlines()
selected = []
for index, line in enumerate(dump):
    if re.match(r"public (?:class|struct) (?:WorkshopLevel(?:s|Metadata)?|InsideLevelParams|ResourceReference`1|<LoadMusic>d__22|<LoadOGG>d__23|<LoadMP3>d__24|<Initialize>d__19|<StartPlayLevel>d__41|<LoadCover>d__36)(?:\s|$)", line):
        end = index + 1
        while end < len(dump) and dump[end] != "}":
            end += 1
        selected.extend(f"{number+1}: {dump[number]}" for number in range(index, min(end+1, len(dump))))
        selected.append("")
(args.out / "metadata-excerpts.txt").write_text("\n".join(selected), encoding="utf-8")
print(f"Verified {sha}; PE {pe.FILE_HEADER.Machine:x}; {len(imports)} import DLLs, {len(exports)} exports; {len(result)} bounded scopes.")
