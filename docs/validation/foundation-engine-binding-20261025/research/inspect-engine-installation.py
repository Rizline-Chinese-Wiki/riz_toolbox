"""Read-only fixed PE evidence for native engine installation timing.

No image mapping/loading, process creation, native execution or game writes.
Only this script's evidence JSON is written, beside the script.
"""
from pathlib import Path
import bisect
import hashlib
import json
import struct
import sys
import zipfile

import capstone
import pefile

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[3]
GAME = Path("D:/SteamLibrary/steamapps/common/Rizline")
KINDS = {0x792240: "Start", 0x78F840: "Execute", 0x78F680: "Release", 0x793840: "Detach"}


def sha(data):
    return hashlib.sha256(data).hexdigest()


class Image:
    def __init__(self, name):
        self.path = GAME / name
        self.data = self.path.read_bytes()
        self.pe = pefile.PE(data=self.data, fast_load=True)
        self.pe.parse_data_directories(directories=[0, 1, 9])
        self.base = self.pe.OPTIONAL_HEADER.ImageBase
        directory = self.pe.OPTIONAL_HEADER.DATA_DIRECTORY[3]
        pdata = self.pe.get_data(directory.VirtualAddress, directory.Size)
        self.functions = [struct.unpack_from("<III", pdata, i) for i in range(0, len(pdata) - 11, 12)]
        self.starts = [row[0] for row in self.functions]
        self.roots = {}
        self.groups = {}
        for row in self.functions:
            root = self.primary(row)
            self.groups.setdefault(root, []).append(row)
        self.md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
        self.md.detail = True
        self.decoded = {}
        self.imports = {}
        for descriptor in getattr(self.pe, "DIRECTORY_ENTRY_IMPORT", []):
            for symbol in descriptor.imports:
                self.imports[symbol.address - self.base] = dict(
                    dll=descriptor.dll.decode(), name=symbol.name.decode() if symbol.name else None,
                    ordinal=symbol.ordinal, iatRva=hex(symbol.address - self.base))

    def primary(self, row):
        if row in self.roots:
            return self.roots[row]
        initial = row
        for _ in range(16):
            header = self.pe.get_data(row[2], 4)
            if not (header[0] >> 3) & 4:
                self.roots[initial] = row
                return row
            row = struct.unpack("<III", self.pe.get_data(row[2] + 4 + ((header[2] + 1) & ~1) * 2, 12))
        raise ValueError("Unwind chain exceeds analysis bound")

    def root_at(self, rva):
        index = bisect.bisect_right(self.starts, rva) - 1
        if index < 0:
            return None
        row = self.functions[index]
        return self.primary(row) if row[0] <= rva < row[1] else None

    def region(self, rva, label=None):
        root = self.root_at(rva)
        if root is None:
            body = self.pe.get_data(rva, 32)
            instructions = []
            for instruction in self.md.disasm(body, rva):
                instructions.append(dict(rva=hex(instruction.address), bytes=instruction.bytes.hex(),
                    op=instruction.mnemonic, args=instruction.op_str))
                if instruction.mnemonic in ("ret", "jmp", "int3"):
                    break
            return dict(label=label, requestedRva=hex(rva), noRuntimeFunction=True,
                        bytes32=body.hex(), boundedEntryInstructions=instructions,
                        scope="32-byte entry window only, not an inferred complete function or unwind proof")
        if root not in self.decoded:
            group = self.groups[root]
            lo, hi = min(row[0] for row in group), max(row[1] for row in group)
            body = self.pe.get_data(lo, hi - lo)
            if len(body) > 0x20000:
                raise ValueError(f"Region exceeds explicit static bound: {lo:x}-{hi:x}")
            decoded = list(self.md.disasm(body, lo))
            instructions = []
            for instruction in decoded:
                item = dict(rva=hex(instruction.address), bytes=instruction.bytes.hex(),
                            op=instruction.mnemonic, args=instruction.op_str)
                if instruction.mnemonic in ("call", "jmp"):
                    if instruction.operands[0].type == capstone.x86.X86_OP_IMM:
                        item["directTargetRva"] = hex(instruction.operands[0].imm)
                    elif instruction.operands[0].type == capstone.x86.X86_OP_MEM:
                        memory = instruction.operands[0].mem
                        if memory.base == capstone.x86.X86_REG_RIP:
                            item["pointerSlotRva"] = hex(instruction.address + instruction.size + memory.disp)
                            imp = self.imports.get(instruction.address + instruction.size + memory.disp)
                            if imp:
                                item["import"] = imp
                instructions.append(item)
            self.decoded[root] = dict(beginRva=hex(lo), endRva=hex(hi), bodyBytes=body.hex(),
                bodySha256=sha(body), fullyLinearDecoded=sum(i.size for i in decoded) == len(body),
                runtimeFunctions=[dict(beginRva=hex(a), endRva=hex(b), unwindRva=hex(c),
                    unwindHeader=self.pe.get_data(c, 4).hex()) for a, b, c in group], instructions=instructions)
        return dict(label=label, **self.decoded[root])

    def metadata(self):
        tls = []
        if hasattr(self.pe, "DIRECTORY_ENTRY_TLS"):
            callbacks = self.pe.DIRECTORY_ENTRY_TLS.struct.AddressOfCallBacks - self.base
            if callbacks >= 0:
                for index in range(32):
                    callback = struct.unpack("<Q", self.pe.get_data(callbacks + index * 8, 8))[0]
                    if not callback:
                        break
                    tls.append(hex(callback - self.base))
                else:
                    raise ValueError("TLS callback count exceeds bound")
        return dict(path=str(self.path), sha256=sha(self.data), bytes=len(self.data),
            imageBase=hex(self.base), imageSize=hex(self.pe.OPTIONAL_HEADER.SizeOfImage),
            entryPointRva=hex(self.pe.OPTIONAL_HEADER.AddressOfEntryPoint), tlsCallbacks=tls,
            exports=[dict(name=s.name.decode() if s.name else None, rva=hex(s.address), ordinal=s.ordinal)
                for s in getattr(getattr(self.pe, "DIRECTORY_ENTRY_EXPORT", None), "symbols", [])],
            imports=list(self.imports.values()),
            sections=[dict(name=s.Name.decode().rstrip("\0"), rva=hex(s.VirtualAddress),
                rawBytes=s.SizeOfRawData, flags=hex(s.Characteristics)) for s in self.pe.sections])

    def direct_references(self, targets):
        # Candidate byte search is never counted as an instruction xref until
        # its whole containing primary RF region confirms the boundary.
        references = []
        for section in self.pe.sections:
            if not section.Characteristics & 0x20000000:
                continue
            data = section.get_data()
            for opcode in (0xE8, 0xE9):
                at = data.find(bytes([opcode]))
                while at >= 0 and at + 5 <= len(data):
                    source = section.VirtualAddress + at
                    destination = source + 5 + struct.unpack_from("<i", data, at + 1)[0]
                    if destination in targets:
                        region = self.region(source, "direct-reference parent")
                        exact = next((i for i in region.get("instructions", []) if i["rva"] == hex(source)), None)
                        references.append(dict(sourceRva=hex(source), targetRva=hex(destination),
                            opcode=hex(opcode), verifiedInstruction=bool(exact and exact.get("directTargetRva") == hex(destination)),
                            parentBeginRva=region.get("beginRva"), parentEndRva=region.get("endRva"),
                            parentBodySha256=region.get("bodySha256")))
                    at = data.find(bytes([opcode]), at + 1)
        return references

    def string_rvas(self, text):
        needle = text.encode("ascii") + b"\0"
        result = []
        at = self.data.find(needle)
        while at >= 0:
            result.append(self.pe.get_rva_from_offset(at))
            at = self.data.find(needle, at + 1)
        return result

    def rip_references(self, targets, writes_only=()):
        # Deliberately limited to ordinary REX.W LEA/MOV and FF /2,/4 RIP
        # instruction forms. This is not a whole-image indirect-call closure.
        references = []
        prefixes = [bytes((rex, opcode, modrm)) for rex in (0x48, 0x4C)
                    for opcode in (0x8D, 0x8B, 0x89) for modrm in range(5, 0x40, 8)]
        prefixes += [b"\xFF\x15", b"\xFF\x25"]
        for section in self.pe.sections:
            if not section.Characteristics & 0x20000000:
                continue
            data = section.get_data()
            for prefix in prefixes:
                at = data.find(prefix)
                width = len(prefix) + 4
                while at >= 0 and at + width <= len(data):
                    source = section.VirtualAddress + at
                    destination = source + width + struct.unpack_from("<i", data, at + len(prefix))[0]
                    if destination in targets and (destination not in writes_only or len(prefix) == 3 and prefix[1] == 0x89):
                        region = self.region(source, "RIP-reference parent")
                        exact = next((i for i in region.get("instructions", []) if i["rva"] == hex(source)), None)
                        if exact:
                            references.append(dict(sourceRva=hex(source), targetRva=hex(destination),
                                instruction=exact, parentBeginRva=region["beginRva"],
                                parentEndRva=region["endRva"], parentBodySha256=region["bodySha256"]))
                    at = data.find(prefix, at + 1)
        return sorted(references, key=lambda row: int(row["sourceRva"], 16))


unity = Image("UnityPlayer.dll")
exe = Image("Rizline.exe")
assembly = Image("GameAssembly.dll")
assert sha(unity.data) == "43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1"
assert sha(exe.data) == "f9c37f1078619d02c62a7a5815ca48179666efb2dfe7e6e698bfff7858c26e5e"
assert sha(assembly.data) == "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79"

references = unity.direct_references(set(KINDS))
requested = set(KINDS) | {0x1029F0, 0x102520, unity.pe.OPTIONAL_HEADER.AddressOfEntryPoint}
requested |= {int(r["parentBeginRva"], 16) for r in references if r["verifiedInstruction"]}
unity_mains = {s.name.decode(): s.address for s in unity.pe.DIRECTORY_ENTRY_EXPORT.symbols
               if s.name in (b"UnityMain", b"UnityMain2")}
requested |= set(unity_mains.values()) | {0x806670, 0x17E7AD4, 0x17E85D8,
    0x6B60C0, 0x6B68F0, 0x33E500, 0x17E78E8, 0x17E7938, 0x17E7A50,
    0x447EF2, 0x1838FBC, 0x1839000}
for callback in unity.metadata()["tlsCallbacks"]:
    requested.add(int(callback, 16))
strings = {name: unity.string_rvas(name) for name in (
    "il2cpp_init", "il2cpp_gc_wbarrier_set_field", "GameAssembly.dll", "UnityMain")}
interesting = {rva for values in strings.values() for rva in values} | {0x1D1CF28, 0x1D1CC68}
rip_references = unity.rip_references(interesting, writes_only={0x1D1CF28})
requested |= {int(r["parentBeginRva"], 16) for r in rip_references}
exe_references = exe.rip_references({0xE240})
exe_requested = {exe.pe.OPTIONAL_HEADER.AddressOfEntryPoint, 0x10F0, 0x1000}
exe_requested |= {int(r["parentBeginRva"], 16) for r in exe_references}

source_archive = REPO / "artifacts/package/BepInEx/plugins/RizToolbox/licenses/doorstop-corresponding-source.zip"
with zipfile.ZipFile(source_archive) as archive:
    sources = []
    for name in ("src/windows/entrypoint.c", "src/bootstrap.c", "src/windows/riz_preinit.c", "src/windows/riz_init_call.c"):
        data = archive.read(name)
        sources.append(dict(path=name, sha256=sha(data), text=data.decode("utf-8-sig")))

result = dict(schemaVersion=1, kind="fixed-engine-installation-static-research",
    scope="Read-only bounded PE and corresponding-source reading. No LoadLibrary, image mapping, native execution, game/UI, installation or source patch.",
    tools=dict(python=sys.version, pefile=pefile.__version__, capstone=capstone.__version__, idaCallableAvailable=False),
    images=[unity.metadata(), exe.metadata(), assembly.metadata()],
    unityMainRvas={name: hex(rva) for name, rva in unity_mains.items()}, directNodeReferences=references,
    interestingRipReferences=rip_references, exeUnityMainImportReferences=exe_references,
    unityFunctions=[unity.region(rva, KINDS.get(rva)) for rva in sorted(requested)],
    exeFunctions=[exe.region(rva, "EXE startup / imported UnityMain2 caller") for rva in sorted(exe_requested)],
    crtInitializerTables=[dict(label=label, beginRva=hex(lo), endRva=hex(hi),
        entries=[dict(slotRva=hex(slot), targetRva=hex(value - unity.base) if value else None)
            for slot in range(lo, hi, 8)
            for value in [struct.unpack("<Q", unity.pe.get_data(slot, 8))[0]]])
        for label, lo, hi in [("CRT C initializer range", 0x186AE80, 0x186AEC8),
                             ("CRT C++ initializer range", 0x1865070, 0x186AE68)]],
    relevantStrings={name: [hex(rva) for rva in values] for name, values in strings.items()},
    doorstopSourceArchive=dict(path=str(source_archive), sha256=sha(source_archive.read_bytes()), sources=sources),
    boundaries=dict(currentPreInitEarlyEnoughVerified=False, unityMainAdmissionVerified=False,
        allIndirectCallersClosed=False, originalGameCodeExecuted=False, firstSourceWriteSafetyVerified=False,
        engineCancellationVerified=False))
out = HERE / "engine-installation-evidence.json"
out.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps(dict(output=str(out), sha256=sha(out.read_bytes()), unityMainRvas=result["unityMainRvas"],
    functions=len(result["unityFunctions"]), references=len(references),
    verifiedReferences=sum(r["verifiedInstruction"] for r in references), gameCodeExecuted=False)))
