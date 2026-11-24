"""Offline checks of the exact EngineNodeKernel PE and actual entry-fixture rows.

Never loads a PE or starts a child. Static table evidence is independently read
from disk; actual GetEntry addresses are supplied by the preserved fixture.
"""
import argparse
import hashlib
import json
import struct
import sys
from pathlib import Path


class EvidenceError(ValueError):
    pass


def require(condition, message):
    if not condition:
        raise EvidenceError(message)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def inspect_pe(path):
    data = path.read_bytes()
    def unpack(fmt, offset):
        require(0 <= offset and offset + struct.calcsize(fmt) <= len(data), f"Out-of-bounds PE field in {path.name}")
        return struct.unpack_from(fmt, data, offset)
    require(data[:2] == b"MZ", f"{path.name}: missing DOS signature")
    nt, = unpack("<I", 0x3c)
    require(data[nt:nt+4] == b"PE\0\0", f"{path.name}: missing PE signature")
    machine, sections, timestamp, _, _, optional_size, characteristics = unpack("<HHIIIHH", nt+4)
    opt = nt + 24
    magic, = unpack("<H", opt)
    require(machine == 0x8664 and magic == 0x20b and sections > 0, f"{path.name}: expected AMD64 PE32+")
    entry, = unpack("<I", opt+16)
    base, = unpack("<Q", opt+24)
    image_size, headers_size = unpack("<II", opt+56)
    directories, = unpack("<I", opt+108)
    require(directories >= 14 and optional_size >= 112+8*14, f"{path.name}: truncated data directories")
    table = opt + optional_size
    section_rows = []
    for i in range(sections):
        at = table + 40*i
        name = data[at:at+8].split(b"\0", 1)[0].decode("ascii", "strict")
        virtual_size, rva, raw_size, raw_offset = unpack("<IIII", at+8)
        flags, = unpack("<I", at+36)
        require(raw_offset + raw_size <= len(data), f"{path.name}: truncated section {name}")
        section_rows.append(dict(name=name, rva=rva, virtualSize=virtual_size, rawSize=raw_size, rawOffset=raw_offset, characteristics=flags))
    def offset(rva, length=1):
        if rva < headers_size:
            require(rva + length <= min(headers_size, len(data)), f"{path.name}: invalid header RVA")
            return rva
        for row in section_rows:
            delta = rva - row["rva"]
            if 0 <= delta < max(row["virtualSize"], row["rawSize"]):
                require(delta + length <= row["rawSize"], f"{path.name}: non-file-backed RVA {rva:x}")
                return row["rawOffset"] + delta
        raise EvidenceError(f"{path.name}: unmapped RVA {rva:x}")
    def cstr(rva):
        at = offset(rva)
        end = data.find(b"\0", at, at+4096)
        require(end >= at, f"{path.name}: unterminated import string")
        return data[at:end].decode("ascii", "strict")
    def directory(index):
        return unpack("<II", opt+112+8*index)
    exports = []
    export_rva, export_size = directory(0)
    if export_rva:
        require(export_size >= 40, f"{path.name}: invalid export directory")
        _, _, _, _, _, ordinal_base, function_count, name_count, functions_rva, names_rva, ordinals_rva = unpack("<IIHHIIIIIII", offset(export_rva, 40))
        require(name_count <= function_count <= 65536, f"{path.name}: unreasonable export count")
        for i in range(name_count):
            name_rva, = unpack("<I", offset(names_rva+4*i, 4))
            ordinal, = unpack("<H", offset(ordinals_rva+2*i, 2))
            require(ordinal < function_count, f"{path.name}: invalid exported ordinal")
            function_rva, = unpack("<I", offset(functions_rva+4*ordinal, 4))
            exports.append(dict(name=cstr(name_rva), rva=function_rva, ordinal=ordinal_base+ordinal,
                                forwarder=cstr(function_rva) if export_rva <= function_rva < export_rva+export_size else None))
    def thunks(rva):
        values = []
        for i in range(4096):
            value, = unpack("<Q", offset(rva+8*i, 8))
            if not value:
                return values
            values.append(dict(ordinal=value & 0xffff) if value & (1 << 63) else dict(name=cstr(value+2)))
        raise EvidenceError(f"{path.name}: unbounded thunk table")
    imports = []
    import_rva, import_size = directory(1)
    if import_rva:
        require(import_size >= 20, f"{path.name}: invalid ordinary import size")
        for i in range(min(import_size//20+1, 4096)):
            row = unpack("<IIIII", offset(import_rva+20*i, 20))
            if not any(row):
                break
            original, _, _, name, first = row
            require(name and first, f"{path.name}: incomplete ordinary import")
            imports.append(dict(module=cstr(name), symbols=thunks(original or first)))
        else:
            raise EvidenceError(f"{path.name}: unterminated ordinary import table")
    delays = []
    delay_rva, delay_size = directory(13)
    if delay_rva:
        require(delay_size >= 32, f"{path.name}: invalid delay import size")
        for i in range(min(delay_size//32+1, 4096)):
            row = unpack("<IIIIIIII", offset(delay_rva+32*i, 32))
            if not any(row):
                break
            attributes, name, _, _, names, _, _, _ = row
            require(attributes & 1 and name and names, f"{path.name}: unsupported legacy delay import")
            delays.append(dict(module=cstr(name), symbols=thunks(names)))
        else:
            raise EvidenceError(f"{path.name}: unterminated delay import table")
    tls_rva, tls_size = directory(9)
    tls = None
    if tls_rva:
        require(tls_size >= 40, f"{path.name}: invalid TLS directory")
        start, end, index, callbacks, zero, alignment = unpack("<QQQQII", offset(tls_rva, 40))
        callback_rvas = []
        if callbacks:
            for i in range(256):
                value, = unpack("<Q", offset(callbacks-base+8*i, 8))
                if not value:
                    break
                require(base <= value < base+image_size, f"{path.name}: non-image TLS callback")
                callback_rvas.append(value-base)
            else:
                raise EvidenceError(f"{path.name}: unbounded TLS callback table")
        tls = dict(directoryRva=tls_rva, callbacks=callback_rvas, startVa=start, endVa=end, indexVa=index,
                   zeroFill=zero, characteristics=alignment)
    return dict(path=str(path.resolve()), name=path.name, sha256=sha(data), bytes=len(data), machine=machine,
                imageBase=base, imageSize=image_size, timeDateStamp=timestamp, entryRva=entry, characteristics=characteristics,
                sections=section_rows, imports=imports, delayImports=delays, tls=tls, exports=exports)


EXPORT_NAMES = sorted("RizEngineNodeContext_"+name for name in (
    "GetAbiVersion", "GetLimit", "GetFrameSize", "GetCompletionSize", "GetEntry", "Configure", "Attach", "GetInfo"))


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def section_at(image, rva, size=1):
    rows = [row for row in image["sections"] if row["rva"] <= rva
            and rva+size <= row["rva"]+max(row["virtualSize"], row["rawSize"])]
    require(len(rows) == 1, f"RVA {rva:x} does not belong to exactly one section")
    return rows[0]


def file_bytes(image, data, rva, size):
    section = section_at(image, rva, size)
    delta = rva-section["rva"]
    require(delta+size <= section["rawSize"], "Requested evidence is not file-backed")
    return data[section["rawOffset"]+delta:section["rawOffset"]+delta+size]


def inspect_kernel(path, manifest_path):
    require(path.is_absolute() and path.name == "RizToolbox.EngineNodeKernel.dll", "Use the absolute real Kernel filename, without a renamed legacy helper")
    data = path.read_bytes()
    image = inspect_pe(path)
    checks = []
    def check(value, message):
        require(value, message)
        checks.append(message)
    manifest = read_json(manifest_path)
    candidates = [row for row in manifest["files"] if row.get("role") == "kernel"]
    check(len(candidates) == 1 and candidates[0]["name"] == path.name
          and Path(candidates[0]["path"]).resolve() == path.resolve() and candidates[0]["sha256"] == image["sha256"],
          "Kernel absolute path/name/hash agrees with the completed build manifest")
    nt = struct.unpack_from("<I", data, 0x3c)[0]
    opt = nt+24
    directory_count = struct.unpack_from("<I", data, opt+108)[0]
    check(15 <= directory_count <= 16 and struct.unpack_from("<H",data,nt+20)[0] >= 112+8*directory_count
          and image["characteristics"]&0x2000 and image["machine"] == 0x8664,
          "Exact input is an AMD64 PE32+ DLL")
    directories = [struct.unpack_from("<II", data, opt+112+8*index) for index in range(directory_count)]
    check(image["entryRva"] == 0, "Kernel has no PE entry or automatic CRT entry wrapper")
    for index, label in ((9,"TLS"),(10,"load-config"),(13,"delay-import"),(14,"CLR")):
        check(directories[index] == (0,0), f"Kernel has no {label} directory or initialization mechanism")
    check(not image["tls"] and not image["delayImports"], "Parsed kernel has no TLS callbacks or delay imports")
    imports = sorted((row["module"].lower(), symbol.get("name"), symbol.get("ordinal"))
                     for row in image["imports"] for symbol in row["symbols"])
    check(imports == [("kernel32.dll","RaiseFailFastException",None),("ntdll.dll","__C_specific_handler",None)],
          "Only exact RaiseFailFastException and native SEH imports remain; no CRT/Dobby/heap/late loader direct import")
    exports = image["exports"]
    export_rva, export_size = directories[0]
    export_header = struct.unpack("<IIHHIIIIIII", file_bytes(image, data, export_rva, 40))
    check(export_header[6] == export_header[7] == 8 and len(exports) == 8 and sorted(row["name"] for row in exports) == EXPORT_NAMES,
          "All eight named exports are exact, without unnamed/extra legacy exports")
    check(len({row["ordinal"] for row in exports}) == 8 and all(row["rva"] and not row["forwarder"] for row in exports),
          "Eight exports have distinct ordinal identities and real nonforwarded RVAs")
    for row in exports:
        section = section_at(image, row["rva"])
        check(section["characteristics"]&0x20000000 and not section["characteristics"]&0x80000000,
              f"Export {row['name']} belongs to executable nonwritable image code")
    relocation_rva, relocation_size = directories[5]
    check(relocation_rva != 0 and relocation_size >= 8, "Kernel has real base relocations for literal typed entries")
    relocations = []
    at = 0
    while at < relocation_size:
        page, size = struct.unpack("<II", file_bytes(image, data, relocation_rva+at, 8))
        check(size >= 8 and size%2 == 0 and at+size <= relocation_size, "Base relocation block has bounded exact transport")
        for raw, in struct.iter_unpack("<H", file_bytes(image, data, relocation_rva+at+8, size-8)):
            kind, delta = raw>>12, raw&0xfff
            if kind == 0:
                continue
            check(kind == 10, "Every actual AMD64 relocation is DIR64, without an unknown relocation type")
            target = page+delta
            section = section_at(image, target, 8)
            relocations.append(dict(rva=target, type=kind, section=section["name"]))
        at += size
    check(at == relocation_size and len({row["rva"] for row in relocations}) == len(relocations),
          "Base relocation directory is fully parsed without duplicate writes")
    image["directories"] = {str(index):dict(rva=row[0],size=row[1]) for index,row in enumerate(directories)}
    image["dir64Relocations"] = relocations
    return dict(kind="engine-node-kernel-pe-verification",passed=True,checks=len(checks),assertions=checks,image=image,
                buildManifestPath=str(manifest_path.resolve()),buildManifestSha256=sha(manifest_path.read_bytes()),
                exactExportNames=EXPORT_NAMES,allowedDirectImports=imports,staticEntryTablesVerified=False,
                noAutomaticInitializationVerified=True,allTransitiveOsDependenciesSafeVerified=False,
                firstInstallSafetyVerified=False,productionV4Issued=False,gameCodeExecuted=False,installed=False)


def verify_entries(proof, path):
    raw = read_json(path)
    image = proof["image"]
    disk = Path(image["path"]).read_bytes()
    require(sha(disk) == image["sha256"], "Held Kernel changed between PE and entry-table parsing")
    checks = proof["assertions"]
    def check(value, message):
        require(value, message)
        checks.append(message)
    check(raw["kind"] == "engine-node-kernel-entry-fixture" and raw["schemaVersion"] == 1 and raw["passed"] is True
          and raw["finished"] is True and raw["checks"] > 0 and not raw["failures"] and raw["error"] == "" and raw["sehCode"] == 0,
          "Actual descriptor fixture completed rather than reporting compilation/loader/API failure as success")
    module = raw["module"]
    base = module["base"]
    check(Path(raw["arguments"]["kernelPath"]).resolve() == Path(image["path"])
          and Path(raw["arguments"]["outputPath"]).resolve() == path.resolve()
          and Path(module["actualPath"]).resolve() == Path(image["path"]), "Actual requested/output/loaded paths preserve exact Kernel file identity")
    # Windows can rewrite the mapped OptionalHeader to the actual load base.
    # It never supplies the disk preferred base used for literal-pointer proof.
    check(base > 0 and module["mappedHeaderImageBase"] in (base,image["imageBase"]) and module["imageSize"] == image["imageSize"]
          and module["machine"] == 0x8664 and module["entryPointRva"] == 0 and module["sameFile"] is True,
          "Actual mapped image reports the same AMD64 PE identity and no entry")
    requested, loaded = module["requestedIdentity"], module["loadedIdentity"]
    check(requested["available"] and loaded["available"] and requested["error"] == loaded["error"] == 0
          and all(requested[key] == loaded[key] for key in ("volumeSerial","indexHigh","indexLow")),
          "Native requested/loaded file handle identities agree")
    check(raw["abi"] == dict(version=2,limit=32,frameSize=112,completionSize=32), "Actual descriptor APIs return ABI2/32 slots/112-frame/32-completion")
    check(raw["witnessCalls"] == 0 and raw["typedEntriesInvoked"] is False and raw["hooksPublished"] is False,
          "Entry-table fixture did not invoke a typed entry, original or observer or publish hooks")

    def memory(row, address, length, protection):
        check(row["queried"] and row["error"] == 0 and row["exactModule"] and row["allocationBase"] == base
              and row["base"] <= address and address+length <= row["base"]+row["regionSize"]
              and row["state"] == 0x1000 and row["type"] == 0x1000000 and row["protect"] == protection
              and row["rx"] == (protection == 0x20) and row["readOnly"] == (protection == 2),
              "Actual VirtualQuery describes the complete range in the exact image with immutable expected protection")

    check(len(raw["exports"]) == 8 and sorted(row["name"] for row in raw["exports"]) == EXPORT_NAMES,
          "Actual GetProcAddress observes exactly the eight expected exported APIs")
    for row in raw["exports"]:
        disk_row = next(value for value in image["exports"] if value["name"] == row["name"])
        check(row["rva"] == disk_row["rva"] and row["address"] == base+disk_row["rva"],
              "Actual API export address minus moduleBase equals its held disk export RVA")
        memory(row["memory"],row["address"],1,0x20)

    entries = raw["entries"]
    coordinates = {(kind,slot) for kind in range(4) for slot in range(32)}
    check(len(entries) == 128 and {(row["kind"],row["slot"]) for row in entries} == coordinates
          and len({row["entry"] for row in entries}) == 128, "Actual GetEntry enumerates all128 distinct kind/slot addresses exactly once")
    index = {(row["kind"],row["slot"]):row for row in entries}

    def fresh(info, kind, slot, entry):
        expected = dict(status=2,abiVersion=1,byteSize=112,kind=kind,slot=slot,entry=entry)
        zero_fields = ("attached","unknown","target","original","epoch","entered","exited","inFlight","enterFaults","exitFaults","snapshotFaults","outputFaults")
        check(all(info[key] == value for key,value in expected.items()) and all(info[key] == 0 for key in zero_fields),
              "Actual GetInfo supplies exact Fresh kind/slot/entry with no claimed descriptor or activity")

    for row in entries:
        rva = row["entry"]-base
        section = section_at(image,rva)
        check(0 < rva < image["imageSize"] and row["entryRva"] == rva
              and section["characteristics"]&0x20000000 and not section["characteristics"]&0x80000000,
              "Each actual typed entry independently resolves to executable nonwritable held disk code")
        memory(row["memory"],row["entry"],1,0x20)
        fresh(row["initialInfo"],row["kind"],row["slot"],row["entry"])
        fresh(row["afterInfo"],row["kind"],row["slot"],row["entry"])

    tables = raw["tables"]
    check(len(tables) == 4 and {row["kind"] for row in tables} == set(range(4)), "Actual mapping contains four separate typed table observations")
    relocations = {row["rva"] for row in image["dir64Relocations"]}
    table_slots = set()
    proofs = []
    for kind in range(4):
        table = next(row for row in tables if row["kind"] == kind)
        expected_rvas = [index[kind,slot]["entry"]-base for slot in range(32)]
        pattern = struct.pack("<32Q",*(image["imageBase"]+rva for rva in expected_rvas))
        candidates = []
        for section in image["sections"]:
            flags = section["characteristics"]
            if not flags&0x40000000 or flags&(0x80000000|0x20000000):
                continue
            part = disk[section["rawOffset"]:section["rawOffset"]+section["rawSize"]]
            for offset in range((-section["rva"])%8,max(0,len(part)-256+1),8):
                if part[offset:offset+256] == pattern:
                    candidates.append((section["rva"]+offset,section))
        check(len(candidates) == 1, "Independent held-disk scan finds one aligned read-only literal32-entry sequence for this actual GetEntry vector")
        table_rva, section = candidates[0]
        check(table["candidateCount"] == 1 and table["candidateRvas"] == [table_rva]
              and table["rva"] == table_rva and table["address"] == base+table_rva
              and table["section"] == section["name"] and table["sectionCharacteristics"] == section["characteristics"],
              "Native table observation agrees with independently found disk RVA and real section properties")
        memory(table["memory"],table["address"],256,2)
        slots = table["slots"]
        check(len(slots) == 32 and {row["slot"] for row in slots} == set(range(32)), "Mapped table reports all32 pointer slots exactly once")
        for slot in range(32):
            row = next(value for value in slots if value["slot"] == slot)
            slot_rva = table_rva+slot*8
            disk_pointer, = struct.unpack("<Q",file_bytes(image,disk,slot_rva,8))
            check(slot_rva not in table_slots and slot_rva in relocations, "Every one of128 literal pointer slots has its own real DIR64 relocation")
            table_slots.add(slot_rva)
            check(row["address"] == base+slot_rva and row["pointer"] == index[kind,slot]["entry"]
                  and row["pointerRva"] == expected_rvas[slot] and disk_pointer == image["imageBase"]+expected_rvas[slot],
                  "Actual mapped slot pointer and held disk preferredBase pointer resolve to the same exact GetEntry RVA")
        proofs.append(dict(kind=kind,tableRva=table_rva,section=section["name"],entryRvas=expected_rvas,dir64SlotRvas=[table_rva+8*slot for slot in range(32)]))
    check(len(table_slots) == 128, "Four immutable tables have exactly128 distinct independently checked DIR64 pointer slots")

    rejections = raw["rejections"]
    expected_rejections = {(kind,slot,field) for kind,slot in coordinates for field in ("target","original")}
    check(len(rejections) == 256 and {(row["candidateKind"],row["candidateSlot"],row["field"]) for row in rejections} == expected_rejections,
          "Actual Configure rejects each of128 candidate typed entries in both target and original positions")
    entry_addresses = {row["entry"] for row in entries}
    for row in rejections:
        kind, slot = row["candidateKind"],row["candidateSlot"]
        destination = (kind+1)%4
        other = "original" if row["field"] == "target" else "target"
        check(row["targetKind"] == destination and row["targetSlot"] == slot and row["configureResult"] == 1
              and row[row["field"]] == index[kind,slot]["entry"] and row[other] > 0 and row[other] not in entry_addresses
              and row["target"] != row["original"], "Cross-kind Configure rejects the actual source entry before any destination claim")
        fresh(row["infoAfter"],destination,slot,index[destination,slot]["entry"])
    invalid = raw["invalidRequests"]
    expected_invalid = {(kind,slot) for kind in (4,0xffffffff) for slot in (0,31)} | {(kind,slot) for kind in range(4) for slot in (32,0xffffffff)}
    check(len(invalid) == 12 and {(row["kind"],row["slot"]) for row in invalid} == expected_invalid,
          "Actual descriptor fixture covers every specified out-of-range kind/slot request")
    for row in invalid:
        check(row["entry"] == 0 and row["info"]["status"] == row["configureResult"] == row["attachResult"] == 1,
              "Invalid coordinates return no entry and reject GetInfo/Configure/Attach without execution")
    proof.update(checks=len(checks),staticEntryTablesVerified=True,actualEntryFixturePath=str(path.resolve()),
                 actualEntryFixtureSha256=sha(path.read_bytes()),actualEntryFixtureChecks=raw["checks"],actualModuleBase=base,
                 actualAbi=raw["abi"],mappedHeaderImageBaseObserved=module["mappedHeaderImageBase"],diskPreferredImageBase=image["imageBase"],
                 actualRelocationDelta=base-image["imageBase"],tableProofs=proofs,tableDir64Relocations=128,
                 extraDir64Relocations=[row for row in image["dir64Relocations"] if row["rva"] not in table_slots],
                 entryPointerRejections=256,invalidCoordinateRequests=12,typedEntriesInvokedByEntryFixture=False)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--kernel", type=Path, required=True)
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--entries", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        result = inspect_kernel(args.kernel, args.manifest)
        if args.entries:
            verify_entries(result, args.entries)
        args.output.write_text(json.dumps(result,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
        print(json.dumps(dict(passed=True,checks=result["checks"],staticEntryTablesVerified=result["staticEntryTablesVerified"],output=str(args.output)),ensure_ascii=False))
        return 0
    except Exception as error:
        value = dict(kind="engine-node-kernel-verifier-failure",passed=False,error=f"{type(error).__name__}: {error}")
        args.output.write_text(json.dumps(value,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
        print(json.dumps(value,ensure_ascii=False),file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
