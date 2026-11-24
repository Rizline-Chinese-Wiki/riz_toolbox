"""Offline verifier for exact owned fixed-publisher PE/receipt evidence.
Never builds, loads a DLL or executes a fixture. Formats follow the actual
FixedPublisher.h; host context evidence is separate from the native ABI.
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
    runtime_functions = []
    pdata_rva, pdata_size = directory(3)
    if pdata_rva:
        require(pdata_size % 12 == 0, f"{path.name}: invalid runtime function directory size")
        for i in range(pdata_size//12):
            begin, end, unwind = unpack("<III", offset(pdata_rva+12*i, 12))
            require(0 < begin < end <= image_size and 0 < unwind < image_size, f"{path.name}: invalid runtime function row")
            require(not runtime_functions or begin >= runtime_functions[-1]["endRva"], f"{path.name}: unsorted/overlapping runtime functions")
            runtime_functions.append(dict(beginRva=begin, endRva=end, unwindInfoRva=unwind))
    return dict(path=str(path.resolve()), name=path.name, sha256=sha(data), bytes=len(data), machine=machine,
                imageBase=base, imageSize=image_size, timeDateStamp=timestamp, entryRva=entry, characteristics=characteristics,
                sections=section_rows, imports=imports, delayImports=delays, tls=tls, exports=exports, runtimeFunctions=runtime_functions)


PREFIXES = [bytes.fromhex(value) for value in [
    "48895c2408488974241848897c2420",
    "405553574156488d6c24884881ec78010000",
    "40534883ec20ff4960488bd983796000",
    "48896c2420574883ec20488bf933ed"]]
BOUNDARIES = [[0,5,10,15],[0,2,3,4,6,11,18],[0,2,6,9,12,16],[0,5,6,10,13,15]]
KIND_NAMES = ["Start", "Execute", "Release", "Detach"]
STATE_NAMES = ["Fresh", "Preparing", "Prepared", "Bound", "Publishing", "FixtureReady", "DisabledNoSourceWrite", "Unknown"]
EVENT_NAMES = {1:"PrepareBegin",2:"PrepareValidated",3:"ContinuationBound",4:"PrepareEnd",5:"BindBegin",6:"EntryBound",7:"BindEnd",
               10:"CommitBegin",11:"ProtectBegin",12:"ProtectEnd",13:"SourceWriteBegin",14:"SourceWriteEnd",15:"ReadbackBegin",
               16:"ReadbackEnd",17:"RestoreBegin",18:"RestoreEnd",19:"FlushBegin",20:"FlushEnd",21:"CommitEnd",
               22:"FailureObserved",23:"FinalReadback",24:"Ready"}
FAULT_NAMES = ["none","before-protect","after-protect","before-write","after-write","before-readback","after-readback",
               "before-restore","after-restore","before-flush","after-flush","partial-write","protect-api-injection",
               "restore-api-injection","flush-api-injection"]


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def rva_bytes(image, rva, size):
    data = Path(image["path"]).read_bytes()
    require(sha(data) == image["sha256"], f"Input PE changed: {image['name']}")
    for section in image["sections"]:
        delta = rva-section["rva"]
        if 0 <= delta and delta+size <= section["rawSize"]:
            at = section["rawOffset"]+delta
            return data[at:at+size]
    raise EvidenceError(f"{image['name']}: requested bytes outside file-backed sections")


def exported(image, name):
    found = [row for row in image["exports"] if row["name"] == name]
    require(len(found) == 1 and found[0]["rva"] and not found[0]["forwarder"], f"Missing exact nonforwarded export {image['name']}!{name}")
    return found[0]["rva"]


def unwind_info(image, rva, depth=0):
    require(depth < 8, "Excessive/cyclic chained unwind records")
    header = rva_bytes(image, rva, 4)
    version, flags = header[0]&7, header[0]>>3
    prolog, count, frame = header[1:]
    require(version == 1 and flags & ~7 == 0, "Fixed fixture requires understood unwind version1")
    raw = rva_bytes(image, rva, 4+2*((count+1)&~1))
    operations, i = [], 0
    widths = {0:1,2:1,3:1,4:2,5:3,8:2,9:3,10:1}
    while i < count:
        at = 4+2*i
        code_offset, packed = raw[at:at+2]
        op, info = packed&15, packed>>4
        width = (2 if info == 0 else 3 if info == 1 else 0) if op == 1 else widths.get(op,0)
        require(width and i+width <= count and code_offset <= prolog, "Invalid/unsupported fixed unwind operation")
        require(not operations or code_offset <= operations[-1]["codeOffset"], "Unwind codes are not descending")
        slots = list(struct.unpack_from("<"+"H"*(width-1), raw, at+2)) if width > 1 else []
        value = None
        if op == 1:
            value = slots[0]*8 if info == 0 else slots[0] | slots[1]<<16
        elif op == 2:
            value = info*8+8
        elif op in (4,8):
            value = slots[0]*(16 if op == 8 else 8)
        elif op in (5,9):
            value = slots[0] | slots[1]<<16
        operations.append(dict(codeOffset=code_offset, op=op, info=info, slots=slots, value=value))
        i += width
    result = dict(rva=rva, version=version, flags=flags, prologSize=prolog, codeSlots=count,
                  frameRegister=frame&15, frameOffset=frame>>4, operations=operations, bytes=raw.hex())
    if flags&4:
        require(not flags&3, "Chained unwind cannot also declare a handler")
        tail = rva+len(raw)
        begin, end, target = struct.unpack("<III", rva_bytes(image, tail, 12))
        result["chainedFunction"] = dict(beginRva=begin,endRva=end,unwindInfoRva=target)
        result["chain"] = unwind_info(image,target,depth+1)
    elif flags&3:
        result["handlerRva"], = struct.unpack("<I", rva_bytes(image,rva+len(raw),4))
    return result


def function_for(image, rva):
    rows = [row for row in image["runtimeFunctions"] if row["beginRva"] <= rva < row["endRva"]]
    require(len(rows) == 1, f"Missing exact static RUNTIME_FUNCTION for {image['name']}+{rva:x}")
    result = dict(rows[0])
    result["unwind"] = unwind_info(image,result["unwindInfoRva"])
    return result


def inspect_fixed(publisher_path, targets_path, helper_path):
    publisher, targets, helper = [inspect_pe(path.resolve()) for path in (publisher_path, targets_path, helper_path)]
    require(publisher["name"].lower() == "rizfixedpublisher.dll" and targets["name"].lower() == "rizfixedownedtargets.dll"
            and helper["name"].lower() == "riztoolbox.judgecontext.dll", "Only fixed self-owned component names are supported")
    require(not publisher["tls"] and not publisher["delayImports"], "Publisher must have no TLS or delay imports")
    allowed = {"VirtualQuery","GetCurrentThreadId","GetModuleHandleW","GetProcAddress","GetSystemInfo","GetModuleFileNameW",
               "CreateFileW","GetFileSizeEx","ReadFile","CloseHandle","VirtualProtect","GetLastError","SetLastError",
               "FlushInstructionCache","GetCurrentProcess"}
    actual = []
    for dependency in publisher["imports"]:
        module_name = dependency["module"].lower()
        require(module_name in ("kernel32.dll","ntdll.dll"), "Fixed publisher has a CRT/Dobby/other dependency")
        for symbol in dependency["symbols"]:
            require(symbol.get("name") in (allowed if module_name == "kernel32.dll" else {"__C_specific_handler"}), f"Unexpected publisher imported API {symbol}")
            actual.append(symbol["name"])
    require(actual and len(actual) == len(set(actual)), "Publisher import list missing or duplicated")
    for name in ("Prepare","Bind","Commit","GetInfo","GetVersion","GetInfoSize"):
        exported(publisher,"RizFixedPublisher_"+name)
    marker_rva = exported(targets,"RizFixedOwnedMarker")
    require(rva_bytes(targets,marker_rva,16) == b"RIZFIXEDOWNEDV1\0", "Fixed owned marker mismatch")
    marker_section = next(row for row in targets["sections"] if row["rva"] <= marker_rva < row["rva"]+max(row["virtualSize"],row["rawSize"]))
    require(not marker_section["characteristics"]&0xa0000000, "Owned marker must not be writable or executable")
    target_rows = []
    for kind, name in enumerate(KIND_NAMES):
        target_rva = exported(targets,"RizFixedOwned"+name)
        prefix_end = exported(targets,"RizFixedOwned"+name+"PrefixEnd")
        require(prefix_end-target_rva == len(PREFIXES[kind]) and rva_bytes(targets,target_rva,len(PREFIXES[kind])) == PREFIXES[kind], f"Kind {kind} actual non-NOP prefix differs from fixed contract")
        function = function_for(targets,target_rva)
        require(function["beginRva"] == target_rva and function["unwind"]["frameRegister"] == 0, f"Kind {kind} unexpected target RF/frame register")
        verify_fixed_unwind_codes(kind,function["unwind"],False)
        target_rows.append(dict(kind=kind,name=name,targetRva=target_rva,prefixEndRva=prefix_end,prefix=PREFIXES[kind].hex(),
                                boundaries=BOUNDARIES[kind],function=function,firstContinuationBytes=rva_bytes(targets,prefix_end,12).hex()))
    require(rva_bytes(targets,target_rows[2]["prefixEndRva"],4) == b"\x41\x0f\x94\xc0", "Release continuation must first consume prefix CMP flags with SETZ R8B")
    return dict(kind="owned-fixed-publisher-pe-proof",passed=True,publisher=publisher,targets=targets,helper=helper,
                importedPublisherApis=sorted(actual),targetRows=target_rows,noCrtDobbyHeapOrLateLoadImportsVerified=True,
                completeStaticOrIndirectCallClosureProved=False,earlyLoaderAdmissionVerified=False,
                productionV4Issued=False,gameReady=False,engineCancellationCompleted=False)


def verify_fixed_unwind_codes(kind, unwind, original):
    # Save offsets are deliberately different in Start's source frame and its
    # no-allocation replay stub. Decode the real .xdata, not an ASM annotation.
    expected = [
        [(15,4,7,0x20),(10,4,6,0x18),(5,4,3,8)] if original else
            [(19,4,7,0x48),(19,4,6,0x40),(19,4,3,0x30),(19,2,4,0x28)],
        [(18,1,0,0x178),(6,0,14,None),(4,0,7,None),(3,0,3,None),(2,0,5,None)],
        [(6,2,3,0x20),(2,0,3,None)],
        [(10,4,5,0x48),(10,2,3,0x20),(6,0,7,None)],
    ][kind]
    actual = [(row["codeOffset"],row["op"],row["info"],row["value"]) for row in unwind["operations"]]
    require(sorted(actual,key=repr) == sorted(expected,key=repr), f"Kind {kind} actual unwind operations differ from independently decoded fixed prefix effects")
    require(unwind["version"] == 1 and unwind["flags"] == 0 and unwind["frameRegister"] == unwind["frameOffset"] == 0,
            f"Kind {kind} unexpected handler/chain/frame register on fixed ASM")
    require(unwind["prologSize"] == (len(PREFIXES[kind])+10 if original else [19,18,16,15][kind]), "Fixed prolog extent differs from actual replay representation")


def decode_receipt(data):
    require(len(data) == 21640, "Fixed GetInfo size must equal actual ABI21640")
    info = dict(zip(["version","byteSize","state","failure"],struct.unpack_from("<4I",data)))
    info["epoch"], = struct.unpack_from("<Q",data,16)
    info.update(zip(["ownerThread","claimed","preparedCount","boundCount","nextCommit","sourceWriteAttempts","sourceWriteCompletions",
                     "protectionOutstanding","eventCount","overflow","reserved0","reserved1"],struct.unpack_from("<12I",data,24)))
    require(info["version"] == 1 and info["byteSize"] == len(data) and info["state"] < len(STATE_NAMES)
            and info["eventCount"] <= 256 and not info["reserved0"] and not info["reserved1"], "Invalid fixed info transport header")
    info["stateName"] = STATE_NAMES[info["state"]]
    receipts = []
    for kind in range(4):
        at = 72+kind*272
        row = dict(zip(["version","byteSize","kind","prefixBytes"],struct.unpack_from("<4I",data,at)))
        row.update(zip(["module","target","original","continuation","entry","epoch"],struct.unpack_from("<6Q",data,at+16)))
        row.update(zip(["rva","originalProtection","observedProtection","flags","protectAttempts","protectSucceeded","sourceWriteAttempts",
                        "sourceBytesWritten","readbackAttempts","readbackVerified","restoreAttempts","restoreSucceeded","flushAttempts",
                        "flushSucceeded","protectionOutstanding","lastWin32Error"],struct.unpack_from("<16I",data,at+64)))
        row["helperEpoch"],row["helperSlot"],row["reserved"] = struct.unpack_from("<QII",data,at+128)
        row.update(originalBytes=data[at+144:at+176].hex(),patchBytes=data[at+176:at+208].hex(),
                   observedBytes=data[at+208:at+240].hex(),moduleSha256=data[at+240:at+272].hex())
        require(row["version"] == 1 and row["byteSize"] == 272 and row["kind"] == kind
                and row["prefixBytes"] == len(PREFIXES[kind]) and not row["reserved"] and not row["flags"]&~7, "Invalid per-kind fixed receipt shape")
        receipts.append(row)
    events = []
    for index in range(256):
        at = 1160+index*80
        if index >= info["eventCount"]:
            require(not any(data[at:at+80]), "Unexpected unpublished fixed event tail")
            continue
        row = dict(sequence=struct.unpack_from("<Q",data,at)[0])
        row.update(zip(["stage","kind","state","failure","result","win32Error","flags","sourceBytesWritten"],struct.unpack_from("<8I",data,at+8)))
        row.update(zip(["target","original","entry","epoch"],struct.unpack_from("<4Q",data,at+40)))
        row["originalProtection"],row["observedProtection"] = struct.unpack_from("<II",data,at+72)
        require(row["sequence"] == index+1 and row["stage"] in EVENT_NAMES and row["kind"] <= 4
                and row["state"] < len(STATE_NAMES) and not row["flags"]&~31, "Unknown/nonsequential fixed event")
        row["stageName"] = EVENT_NAMES[row["stage"]]
        events.append(row)
    return info, receipts, events


def verify_receipt(path, pe_proof, summary_path=None, memory_path=None):
    proof = read_json(pe_proof)
    require(proof["passed"] and proof["kind"] == "owned-fixed-publisher-pe-proof", "Fixed PE proof is absent or failed")
    publisher, targets = proof["publisher"],proof["targets"]
    data = path.read_bytes()
    info, receipts, events = decode_receipt(data)
    checks = []
    def check(value, message):
        require(value,message)
        checks.append(message)
    def of(stage,kind=None):
        return [row for row in events if row["stage"] == stage and (kind is None or row["kind"] == kind)]
    check(info["claimed"] in (0,1) and info["preparedCount"] <= 4 and info["boundCount"] <= 4 and info["nextCommit"] <= 4,
          "All exact four-entry counters are bounded")
    check(info["preparedCount"] == sum(bool(row["flags"]&1) for row in receipts)
          and info["boundCount"] == sum(bool(row["flags"]&2) for row in receipts)
          and info["nextCommit"] == sum(bool(row["flags"]&4) for row in receipts), "Four receipt authority bits match global counters")
    check(info["sourceWriteAttempts"] == sum(row["sourceWriteAttempts"] for row in receipts)
          and info["protectionOutstanding"] == sum(row["protectionOutstanding"] for row in receipts), "Global source/protection obligations equal actual per-entry facts")
    check(info["sourceWriteCompletions"] == sum(row["sourceBytesWritten"] == row["prefixBytes"] for row in receipts),
          "Completed full source writes differ explicitly from partial-byte attempts")
    check(not info["overflow"] or info["state"] == 7, "Overflow cannot leave a normal/Disabled receipt")
    if info["failure"]:
        check(info["state"] in (6,7), "Failure has a terminal retained state")
        if info["sourceWriteAttempts"] or info["protectionOutstanding"]:
            check(info["state"] == 7, "Any attempted source write or remaining protection responsibility keeps Unknown")
        if info["state"] == 6:
            check(info["sourceWriteAttempts"] == info["protectionOutstanding"] == 0, "DisabledNoSourceWrite requires both zero source attempts and restored protection")
    if info["claimed"]:
        check(info["ownerThread"] != 0 and len(of(1)) == 1, "Permanent claim has one real owner and Prepare scope")
    if of(3):
        check(len(of(2)) == 4 and max(row["sequence"] for row in of(2)) < min(row["sequence"] for row in of(3)), "All four target validations precede publishing any original continuation")
    if of(5):
        check(info["preparedCount"] == 4 and len(of(4)) == 1 and of(4)[0]["sequence"] < of(5)[0]["sequence"], "Bind follows actual four-entry Prepare completion")
    if of(10):
        check(info["preparedCount"] == info["boundCount"] == 4 and len(of(6)) == 4 and len(of(7)) == 1
              and max(row["sequence"] for row in of(6)) < of(7)[0]["sequence"] < of(10)[0]["sequence"], "Four immutable Bind rows precede any actual commit/protection call")
    unknown_seen = False
    for event in events:
        check(not unknown_seen or event["state"] == 7, "Unknown is monotonic across all later published events")
        unknown_seen |= event["state"] == 7
        if event["flags"]&1:
            check(not event["flags"]&16, "Injected API-result row does not pretend an OS API was called")
        if event["kind"] < 4:
            row = receipts[event["kind"]]
            check(event["target"] in (0,row["target"]) and event["original"] in (0,row["original"])
                  and event["entry"] in (0,row["entry"]), "Every event remains on its immutable exact target/original/entry")
    if unknown_seen:
        check(info["state"] == 7, "Final snapshot cannot clear a published Unknown event")
    # An Unknown created only by a transient protection window must not have
    # been published; successful cleanup can then yield DisabledNoSourceWrite.
    original_bases = set()
    replay_rows = []
    for kind,row in enumerate(receipts):
        check(row["protectSucceeded"] <= row["protectAttempts"] and row["restoreSucceeded"] <= row["restoreAttempts"]
              and row["flushSucceeded"] <= row["flushAttempts"] and row["readbackVerified"] <= row["readbackAttempts"],
              f"Kind {kind} API successes cannot exceed actual calls")
        check(row["sourceWriteAttempts"] in (0,1) and row["sourceBytesWritten"] <= row["prefixBytes"]
              and bool(row["sourceBytesWritten"]) <= bool(row["sourceWriteAttempts"]), f"Kind {kind} no retry or inflated source byte count")
        for begin,end,attempt_field,success_field in [(11,12,"protectAttempts","protectSucceeded"),
                (17,18,"restoreAttempts","restoreSucceeded"),(19,20,"flushAttempts","flushSucceeded")]:
            actual = [event for event in of(end,kind) if event["flags"]&16]
            succeeded = [event for event in actual if event["flags"]&4]
            check(row[attempt_field] == len(actual) and row[success_field] == len(succeeded), f"Kind {kind} {attempt_field} counts only true OS API rows")
            check(len(of(begin,kind)) == len(of(end,kind)), f"Kind {kind} every started OS stage has a captured completion")
        check(row["sourceWriteAttempts"] == len(of(13,kind)) and len(of(14,kind)) == row["sourceWriteAttempts"],
              f"Kind {kind} actual volatile-byte write has one begin/end and no repeat")
        if of(14,kind):
            check(of(14,kind)[0]["sourceBytesWritten"] == row["sourceBytesWritten"] and of(13,kind)[0]["sequence"] < of(14,kind)[0]["sequence"], f"Kind {kind} partial/full bytes derive from actual write completion row")
        if not row["target"]:
            check(not row["flags"] and not row["sourceWriteAttempts"] and not row["protectionOutstanding"], f"Kind {kind} unvalidated row has no authority or write responsibility")
            continue
        check(row["rva"] == proof["targetRows"][kind]["targetRva"] and row["target"] == row["module"]+row["rva"]
              and row["continuation"] == row["target"]+len(PREFIXES[kind]) and row["moduleSha256"] == targets["sha256"], f"Kind {kind} actual fixed module/hash/RVA/continuation")
        check(row["originalProtection"] == 0x20 and row["originalBytes"] == rva_bytes(targets,row["rva"],32).hex(), f"Kind {kind} source exact32 before bytes match held disk PE")
        # Original addresses resolve from actual PE export/RF metadata, not from
        # a second caller-provided original descriptor.
        original_rva = exported(publisher,"RizFixedOriginal"+KIND_NAMES[kind])
        base = row["original"]-original_rva
        original_bases.add(base)
        check(base > 0 and row["original"] != row["target"], f"Kind {kind} static original belongs to a separate publisher image")
        prefix = rva_bytes(publisher,original_rva,len(PREFIXES[kind]))
        check(prefix == PREFIXES[kind], f"Kind {kind} static original replays the full actual non-NOP prefix")
        tail = rva_bytes(publisher,original_rva+len(prefix),10)
        check(tail[:3] == b"\x4c\x8b\x1d" and tail[7:] == b"\x41\xff\xe3",
              f"Kind {kind} original continuation uses exact flags-neutral MOV volatile R11/JMP R11")
        displacement, = struct.unpack_from("<i",tail,3)
        slot_rva = original_rva+len(prefix)+7+displacement
        slot = next((section for section in publisher["sections"] if section["rva"] <= slot_rva
                     and slot_rva+8 <= section["rva"]+max(section["virtualSize"],section["rawSize"])),None)
        check(slot and slot["characteristics"]&0x80000000 and not slot["characteristics"]&0x20000000, f"Kind {kind} RIP-relative MOV reads separate writable nonexecutable continuation storage")
        function = function_for(publisher,original_rva)
        verify_fixed_unwind_codes(kind,function["unwind"],True)
        check(function["beginRva"] == original_rva and function["endRva"] == original_rva+len(prefix)+10
              and function["unwind"]["prologSize"] == len(prefix)+10 and function["unwind"]["frameRegister"] == 0,
              f"Kind {kind} exact static stub RF includes both final MOV/JMP instructions")
        replay_rows.append(dict(kind=kind,originalRva=original_rva,continuationSlotRva=slot_rva,function=function,
                                boundaries=BOUNDARIES[kind]+[len(prefix)+7],tailBytes=tail.hex(),volatileRegisterClobber="R11"))
        if row["flags"]&2:
            check(row["helperSlot"] == 28 and row["helperEpoch"] and row["entry"] and row["epoch"] == info["epoch"] == 1, f"Kind {kind} keeps helper epoch separate from publisher epoch")
            patch = bytearray.fromhex(row["originalBytes"])
            patch[:14] = b"\xff\x25\0\0\0\0"+struct.pack("<Q",row["entry"])
            patch[14:row["prefixBytes"]] = b"\x90"*(row["prefixBytes"]-14)
            check(row["patchBytes"] == patch.hex(), f"Kind {kind} exact 14-byte absolute source jump and instruction-complete padding")
        if row["readbackVerified"]:
            check(row["observedBytes"] == row["patchBytes"] and len(of(16,kind)) == row["readbackAttempts"], f"Kind {kind} actual observed source bytes match planned patch")
        if row["flags"]&4:
            check(row["sourceBytesWritten"] == row["prefixBytes"] and row["readbackVerified"] and row["restoreSucceeded"]
                  and not row["protectionOutstanding"] and row["flushSucceeded"] and row["observedProtection"] == row["originalProtection"], f"Kind {kind} commit requires full actual write/readback/restore/flush")
            check(len(of(21,kind)) == 1, f"Kind {kind} one actual completed commit")
    check(len(original_bases) <= 1, "All static originals refer to the same exact publisher image")
    if replay_rows:
        slots = [row["continuationSlotRva"] for row in replay_rows]
        check(all(slot == slots[0]+8*row["kind"] for slot,row in zip(slots,replay_rows)), "Four immutable continuation slots are one exact fixed array")
    if info["state"] == 5:
        check(not info["failure"] and not info["overflow"] and info["nextCommit"] == 4
              and info["sourceWriteAttempts"] == info["sourceWriteCompletions"] == 4 and not info["protectionOutstanding"], "FixtureReady requires four complete writes without unresolved responsibility")
        check(len(of(24)) == len(of(23)) == 1 and max(row["sequence"] for row in of(21)) < of(23)[0]["sequence"] < of(24)[0]["sequence"], "Final readback and Ready occur after every actual commit")
    elif of(24):
        check(len(of(24)) == 1 and info["state"] == 7 and info["failure"] and info["nextCommit"] == 4,
              "A later post-publication failure retains historical Ready while becoming permanently Unknown")
    case = None
    if summary_path:
        lines = [line for line in summary_path.read_text(encoding="utf-8-sig").splitlines() if line.startswith("{")]
        check(len(lines) == 1,"Receipt has exactly one original process summary")
        summary = json.loads(lines[0])
        check(summary["kind"] == "fixed-publisher-fixture" and summary["passed"] and summary["failures"] == 0,
              "Actual owned host process reported no failed checks")
        stage,kind = summary["faultStage"],summary["faultKind"]
        check(0 <= stage < len(FAULT_NAMES) and 0 <= kind < 4,"Actual process selected a fixed known internal fault")
        if stage == 0:
            check(info["state"] == 5 and info["failure"] == 0,"Normal actual process reached four-source FixtureReady")
        else:
            check(not of(24) and info["nextCommit"] == kind and info["preparedCount"] == info["boundCount"] == 4,
                  "Selected fault retained four permanent bindings and only its successful prior commit prefix")
            row = receipts[kind]
            write = stage in (4,5,6,7,8,9,10,11,13,14)
            full = write and stage != 11
            protected = stage != 1 and stage != 12
            readback = stage in (6,7,8,9,10,13,14)
            flushed = stage == 10
            restored = protected and stage != 13
            expected_failure = 14 if stage==11 else 13 if stage==12 else 16 if stage==13 else 17 if stage==14 else 12
            check(info["failure"] == expected_failure,"Actual failure class matches the precise selected API/byte boundary")
            check(info["sourceWriteAttempts"] == kind+int(write) and info["sourceWriteCompletions"] == kind+int(full),
                  "Actual global source attempts/full-completions match selected partial/full-write boundary")
            check(info["state"] == (7 if kind or write or stage==13 else 6),"Selected failure has exact NoSourceWrite versus Unknown terminal state")
            expected = dict(protectAttempts=int(protected),protectSucceeded=int(protected),sourceWriteAttempts=int(write),
                            sourceBytesWritten=3 if stage==11 else row["prefixBytes"] if full else 0,
                            readbackAttempts=int(readback),readbackVerified=int(readback),restoreAttempts=int(restored),
                            restoreSucceeded=int(restored),flushAttempts=int(flushed),flushSucceeded=int(flushed),
                            protectionOutstanding=int(stage==13))
            check(all(row[field] == value for field,value in expected.items()),"Selected receipt distinguishes every real API/byte/readback/restore/flush fact")
            check(info["protectionOutstanding"] == int(stage==13),"Failed protection restore is retained independently from source bytes")
            check(row["flags"] == 3 and all(receipts[i]["flags"] == (7 if i<kind else 3) for i in range(4)),
                  "Failed/current and untouched later entries retain prepared/bound authority without false committed flag")
            check(summary["actualOriginalCalls"] == summary["observerEnters"] == summary["observerExits"] == summary["originalExceptionsCaught"] == 0,
                  "Fault child never executes a partially published target")
        check(summary["controlledUnwindCases"] == 50 and not summary["actualExceptionsAtEveryPc"]
              and not summary["gameExecuted"] and not summary["firstInstallVerified"],"Fault/normal summary retains exact controlled-only scope")
        case = dict(faultStage=stage,faultName=FAULT_NAMES[stage],faultKind=kind,summarySha256=sha(summary_path.read_bytes()))
    memory = None
    if memory_path:
        raw_memory = memory_path.read_bytes()
        check(len(raw_memory) == 240,"Actual source-memory snapshot has exact240-byte transport")
        version,size,page_size,reserved = struct.unpack_from("<4I",raw_memory)
        check(version == 1 and size == len(raw_memory) and reserved == 0 and page_size >= 4096
              and page_size <= 65536 and page_size&(page_size-1) == 0,"Actual GetSystemInfo page size and memory header are valid")
        outstanding_pages = set()
        for row in receipts:
            if row["protectionOutstanding"]:
                check(row["target"]//page_size == (row["target"]+row["prefixBytes"]-1)//page_size,
                      "Outstanding source protection range is contained in one actual system page")
                outstanding_pages.add(row["target"]//page_size)
        snapshots = []
        for kind,row in enumerate(receipts):
            at = 16+56*kind
            raw_kind,protection,target,allocation = struct.unpack_from("<IIQQ",raw_memory,at)
            actual = raw_memory[at+24:at+56]
            check(raw_kind == kind and target == row["target"] and allocation == row["module"] != 0,
                  f"Kind {kind} actual VirtualQuery allocation and memory address match exact owned receipt")
            written = row["sourceBytesWritten"]
            original = bytes.fromhex(row["originalBytes"])
            patch = bytes.fromhex(row["patchBytes"])
            expected = patch[:written]+original[written:]
            check(actual == expected,f"Kind {kind} actual32 source bytes equal precise written prefix and untouched original suffix")
            touched_pages = {target//page_size,(target+31)//page_size}
            expected_protection = 0x40 if touched_pages & outstanding_pages else row["originalProtection"]
            check(protection == expected_protection,f"Kind {kind} actual page protection reflects every shared-page outstanding obligation")
            snapshots.append(dict(kind=kind,target=target,allocationBase=allocation,protection=protection,bytes=actual.hex(),
                                  sharesOutstandingPage=bool(touched_pages & outstanding_pages)))
        memory = dict(rawMemorySha256=sha(raw_memory),systemPageSize=page_size,snapshots=snapshots,
                      actualSourceBytesVerified=True,actualSharedPageProtectionVerified=True)
    return dict(kind="owned-fixed-publisher-receipt-proof",passed=True,checks=len(checks),assertions=checks,
                rawReceiptSha256=sha(data),info=info,receipts=receipts,events=events,staticOriginals=replay_rows,case=case,
                memory=memory,
                exactFixedBytesVerified=True,controlContextUnwindExecuted=False,earlyLoaderAdmissionVerified=False,
                completeStaticOrIndirectCallClosureProved=False,productionV4Issued=False,gameReady=False,engineCancellationCompleted=False)


SENTINELS = [0x1122334455667788,0x2233445566778899,0x33445566778899aa,0x445566778899aabb,
             0x5566778899aabbcc,0x66778899aabbccdd,0x778899aabbccddee,0x8899aabbccddeeff]
NONVOL_OFFSETS = [144,160,168,176,216,224,232,240]


def context_fields(raw):
    require(len(raw) == 1232, "Expected native AMD64 CONTEXT size1232")
    return dict(flags=struct.unpack_from("<I",raw,48)[0],eflags=struct.unpack_from("<I",raw,68)[0],
                rip=struct.unpack_from("<Q",raw,248)[0],rsp=struct.unpack_from("<Q",raw,152)[0],
                rcx=struct.unpack_from("<Q",raw,128)[0],r11=struct.unpack_from("<Q",raw,208)[0],
                nonvol=[struct.unpack_from("<Q",raw,offset)[0] for offset in NONVOL_OFFSETS])


def verify_unwind(path,pe_proof,receipt_path):
    proof = read_json(pe_proof)
    info,receipts,_ = decode_receipt(receipt_path.read_bytes())
    require(info["state"] == 2 and info["preparedCount"] == 4 and info["sourceWriteAttempts"] == 0, "Unwind evidence must precede all source publication on prepared originals")
    rows = [json.loads(line) for line in path.read_text(encoding="utf-8-sig").splitlines() if line]
    expected_keys = {(kind,original,offset) for kind in range(4) for original in (False,True)
                     for offset in BOUNDARIES[kind]+([len(PREFIXES[kind])+7] if original else [])}
    actual_keys = [(row["kind"],row["originalStub"],row["offset"]) for row in rows]
    require(len(actual_keys) == len(expected_keys) == 50 and set(actual_keys) == expected_keys, "All and only50 exact source/replay instruction boundaries are required, including tail MOV/JMP and source continuation PC")
    checks = []
    def check(value,message):
        require(value,message)
        checks.append(message)
    for row in rows:
        kind,original,offset = row["kind"],row["originalStub"],row["offset"]
        label = f"kind{kind}/{'original' if original else 'target'}+{offset}"
        image = proof["publisher"] if original else proof["targets"]
        name = "RizFixedOriginal" if original else "RizFixedOwned"
        start_rva = exported(image,name+KIND_NAMES[kind])
        function = function_for(image,start_rva+offset)
        check(row["controlledContext"] is True and row["actualExceptionAtEachPc"] is False, label+" stays a constructed-context observation")
        check(row["controlPc"] == row["imageBase"]+start_rva+offset and row["controlPc"] == receipts[kind]["original" if original else "target"]+offset,
              label+" actual runtime PC agrees with exact exported PE lane")
        check((row["functionBegin"],row["functionEnd"],row["unwindRva"],row["prologSize"],row["codeCount"]) ==
              (function["beginRva"],function["endRva"],function["unwindInfoRva"],function["unwind"]["prologSize"],function["unwind"]["codeSlots"]),
              label+" actual RtlLookupFunctionEntry/header equals disk RUNTIME_FUNCTION and xdata")
        verify_fixed_unwind_codes(kind,function["unwind"],original)
        before,after = [context_fields(bytes.fromhex(row[name])) for name in ("contextBeforeHex","contextAfterHex")]
        stack = bytes.fromhex(row["stackHex"])
        check(len(stack) == 2048 and row["stackBase"]%16 == 0, label+" uses a real bounded aligned controlled stack")
        entry_rsp = row["stackBase"]+129*8
        caller_rip, = struct.unpack_from("<Q",stack,129*8)
        check(caller_rip != 0 and row["expectedRip"] == caller_rip and row["expectedRsp"] == entry_rsp+8,
              label+" expected caller derives from preserved stack return slot")
        # Independently interpret just the fixed prefix operations. Source and
        # replay share bytes until N; the stub then MOVs its slot into volatile
        # R11 and JMPs R11. These instructions do not change the nonvolatile set.
        expected_stack = bytearray(2048)
        struct.pack_into("<Q",expected_stack,129*8,caller_rip)
        rsp = entry_rsp
        registers = SENTINELS.copy()
        def write(address,value):
            require(row["stackBase"] <= address <= row["stackBase"]+len(expected_stack)-8,"Controlled save outside evidence stack")
            struct.pack_into("<Q",expected_stack,address-row["stackBase"],value)
        def push(value):
            nonlocal rsp
            rsp -= 8
            write(rsp,value)
        if kind == 0:
            for boundary,slot,index in [(5,8,0),(10,0x18,2),(15,0x20,3)]:
                if offset >= boundary:
                    write(entry_rsp+slot,registers[index])
        elif kind == 1:
            for boundary,index in [(2,1),(3,0),(4,3),(6,6)]:
                if offset >= boundary:
                    push(registers[index])
            if offset >= 11:
                registers[1] = rsp-0x78
            if offset >= 18:
                rsp -= 0x178
        elif kind == 2:
            if offset >= 2:
                push(registers[0])
            if offset >= 6:
                rsp -= 0x20
            if offset >= 12:
                registers[0] = before["rcx"]
        else:
            if offset >= 5:
                write(entry_rsp+0x20,registers[1])
            if offset >= 6:
                push(registers[3])
            if offset >= 10:
                rsp -= 0x20
            if offset >= 13:
                registers[3] = before["rcx"]
            if offset >= 15:
                registers[1] = 0
        check(stack == expected_stack, label+" raw saved stack bytes match independently replayed actual prefix semantics")
        check(before["rip"] == row["controlPc"] and before["rsp"] == row["beforeRsp"] == rsp
              and before["nonvol"] == row["beforeNonvol"] == registers,
              label+" raw input CONTEXT agrees with instruction progress and MOV source registers")
        if original and offset == len(PREFIXES[kind])+7:
            check(before["r11"] == receipts[kind]["continuation"],label+" new tail JMP uses the actual continuation loaded by the preceding MOV")
        check(after["rip"] == row["afterRip"] == caller_rip and after["rsp"] == row["afterRsp"] == entry_rsp+8
              and after["nonvol"] == row["afterNonvol"] == SENTINELS and row["expectedNonvol"] == SENTINELS,
              label+" actual RtlVirtualUnwind independently restores all eight caller nonvolatiles and return stack")
        check(row["passed"] is True,label+" host result is consistent with independent bytes/context checks")
    return dict(kind="owned-fixed-publisher-unwind-proof",passed=True,checks=len(checks),assertions=checks,
                rawUnwindSha256=sha(path.read_bytes()),controlledContexts=len(rows),independentlyRecomputed=True,
                actualExceptionAtEachPc=False,arbitraryPcExceptionTransparencyVerified=False,earlyLoaderAdmissionVerified=False,
                productionV4Issued=False,gameReady=False,engineCancellationCompleted=False)


def verify_calls(path,summary_path):
    lines = [line for line in summary_path.read_text(encoding="utf-8-sig").splitlines() if line.startswith("{")]
    require(len(lines) == 1,"One actual host summary is required")
    summary = json.loads(lines[0])
    require(summary["kind"] == "fixed-publisher-fixture" and summary["passed"] and summary["failures"] == 0
            and summary["faultStage"] == 0,"Actual-call evidence belongs to successful normal host")
    rows = [json.loads(line) for line in path.read_text(encoding="utf-8-sig").splitlines() if line]
    require(len(rows) == 28,"Expected8 actual pre-publication exception baselines plus20 hooked normal/worker/exception outer calls")
    checks = []
    def check(value,message):
        require(value,message)
        checks.append(message)
    main_tid = rows[0]["threadId"]
    worker_tid = rows[16]["threadId"]
    check(main_tid and worker_tid and main_tid != worker_tid,"Actual worker runs on a distinct OS thread")
    total_originals = total_enters = total_exits = caught = 0
    baseline_exceptions = {}
    for index,row in enumerate(rows):
        kind = index%4
        baseline = index < 8
        phase_index = index-8
        mode = (1 if index<4 else 2) if baseline else (0 if phase_index<12 else 1 if phase_index<16 else 2)
        references = 1 if not baseline and 4<=phase_index<8 else 3
        label = f"actual outer call{index}/kind{kind}/fault{mode}"
        check((row["kind"],row["faultMode"],row["references"],row["baseline"]) == (kind,mode,references,baseline),label+" belongs to exact declared pre/post-publication phase")
        check(row["threadId"] == (worker_tid if not baseline and 8<=phase_index<12 else main_tid),label+" actual caller thread is exact")
        expected_calls = [int(i==kind)+int(not mode and kind==0 and i in (1,2)) for i in range(4)]
        check(row["calls"] == expected_calls and row["diagnosticsFailed"] == 0,label+" calls original once and only Start's two synchronous children")
        total_originals += sum(row["calls"])
        observer_count = 0 if baseline or phase_index<4 else sum(expected_calls)
        check(row["observerEnters"] == row["observerExits"] == observer_count,label+" actual helper callback pair count matches pre/postAttach boundary")
        total_enters += row["observerEnters"]
        total_exits += row["observerExits"]
        expected_ref = references-int(kind==2 or (not mode and kind==0))
        check(row["finalNodeRef"] == expected_ref,label+" Release decrements exactly once before original body")
        if mode:
            caught += 1
            check(row["returned"] == 0 and row["rawReturn"] == 0,label+" exception never manufactures a normal scalar return")
            check(row["exceptionPayload"] == 0xfedcba9876543210 and row["exceptionKind"] == kind,label+" original typed exception payload and kind remain intact")
            if mode == 1:
                check(row["exceptionCode"] == 0xe0465853 and row["exceptionParameters"] == 2,
                      label+" raw SEH code/flags/parameters reached the original filter")
            else:
                check(row["exceptionCode"] == row["exceptionFlags"] == row["exceptionParameters"] == 0,
                      label+" typed C++ catch is not misreported as a captured raw SEH record")
            exception_tuple = tuple(row[field] for field in ("exceptionCode","exceptionFlags","exceptionParameters","exceptionPayload","exceptionKind"))
            if baseline:
                baseline_exceptions[(kind,mode)] = exception_tuple
            else:
                check(exception_tuple == baseline_exceptions[(kind,mode)],label+" raw exception flags/code/params/payload exactly match same-kind real pre-publication baseline without masking")
        else:
            check(row["returned"] == 1 and row["rawReturn"] == [0x81,0xa5c3fe81,0xe7,0][kind],label+" preserves complete raw scalar including Execute EAX")
            check(row["exceptionCode"] == row["exceptionFlags"] == row["exceptionParameters"] == row["exceptionPayload"] == row["exceptionKind"] == 0,
                  label+" normal call has no invented exception observation")
            if kind in (0,2):
                check(row["releaseCount"] == expected_ref and row["releaseZeroFlag"] == int(expected_ref==0),
                      label+" continuation consumes CMP ZF and exact post-DEC count")
            check(row["outputByte"] == (0x9d if kind in (0,1) else 0) and row["outputNodeMatches"] == (kind==0),
                  label+" actual typed Start/Execute output is correct")
        check(row["detachZero"] == 0,label+" fixed Detach does not leak uncleared EBP diagnostic")
    check(len(baseline_exceptions) == 8,"All four SEH and four C++ actual exception baselines remain distinct")
    check((total_originals,total_enters,total_exits,caught) == (34,20,20,16),"All actual raw-call totals independently recompute with baselines separately included")
    check((summary["actualOriginalCalls"],summary["observerEnters"],summary["observerExits"],summary["originalExceptionsCaught"]) ==
          (total_originals,total_enters,total_exits,caught),"Host summary equals independently checked actual-call rows")
    check(summary["controlledUnwindCases"] == 50 and not summary["actualExceptionsAtEveryPc"]
          and not summary["gameExecuted"] and not summary["firstInstallVerified"],"Actual calls do not expand controlled unwind/game authority")
    return dict(kind="owned-fixed-publisher-actual-call-proof",passed=True,checks=len(checks),assertions=checks,
                rawCallsSha256=sha(path.read_bytes()),rawSummarySha256=sha(summary_path.read_bytes()),outerCalls=len(rows),baselineExceptionCalls=8,
                actualOriginalCalls=total_originals,observerPairs=total_enters,originalExceptionsCaught=caught,
                exactReleaseCountAndCmpZfVerified=True,executeUint32Verified=True,exactExceptionBaselineFlagsVerified=True,arbitraryFlagsPreservationVerified=False,
                actualExceptionAtEachPc=False,earlyLoaderAdmissionVerified=False,productionV4Issued=False,gameReady=False,engineCancellationCompleted=False)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--publisher",type=Path)
    parser.add_argument("--targets",type=Path)
    parser.add_argument("--helper",type=Path)
    parser.add_argument("--receipt",type=Path)
    parser.add_argument("--unwind",type=Path)
    parser.add_argument("--calls",type=Path)
    parser.add_argument("--summary",type=Path)
    parser.add_argument("--memory",type=Path)
    parser.add_argument("--pe-proof",type=Path)
    parser.add_argument("--output",type=Path,required=True)
    args = parser.parse_args()
    try:
        if args.calls:
            require(args.summary,"Actual-call verification requires original host stdout summary")
            value = verify_calls(args.calls,args.summary)
        elif args.unwind:
            require(args.pe_proof and args.receipt,"Unwind verification requires exact PE proof and prepared receipt")
            value = verify_unwind(args.unwind,args.pe_proof,args.receipt)
        elif args.receipt:
            require(args.pe_proof,"Missing fixed PE proof")
            value = verify_receipt(args.receipt,args.pe_proof,args.summary,args.memory)
        else:
            require(args.publisher and args.targets and args.helper,"Provide exact three PE inputs")
            value = inspect_fixed(args.publisher,args.targets,args.helper)
        args.output.write_text(json.dumps(value,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
        print(json.dumps(dict(passed=True,kind=value["kind"],checks=value.get("checks",0),output=str(args.output)),ensure_ascii=False))
        return 0
    except Exception as error:
        value = dict(kind="owned-fixed-publisher-independent-verifier-failure",passed=False,error=f"{type(error).__name__}: {error}")
        args.output.write_text(json.dumps(value,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
        print(json.dumps(value,ensure_ascii=False),file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())


