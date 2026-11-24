"""Independent offline PE/shared-recorder checks for the fixed early host.

Uses only Python's standard library. Never loads a DLL or starts a child. The
PowerShell parent owns execution and supplies the preserved raw mapping.
"""
import argparse
import hashlib
import importlib.util
import json
import struct
import sys
from pathlib import Path

FIXED_VERIFIER_PATH = Path(__file__).resolve().parent.parent / "foundation-fixed-publisher-20261025/verify-fixed-publisher.py"
_fixed_spec = importlib.util.spec_from_file_location("frozen_fixed_publisher_checks", FIXED_VERIFIER_PATH)
fixed = importlib.util.module_from_spec(_fixed_spec)
_fixed_spec.loader.exec_module(fixed)


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


def inspect_images(directory, host_name):
    names = [host_name, "RizFixedOwnedTargets.dll", "RizOwnedProxy.dll", "RizOwnedRecorder.dll",
             "RizToolbox.JudgeContext.dll", "RizFixedPublisher.dll"]
    images = {name: inspect_pe(directory / name) for name in names}
    def ordinary(source, target):
        row = images[source]
        found = [entry for entry in row["imports"] if entry["module"].lower() == target.lower()]
        require(len(found) == 1 and found[0]["symbols"], f"Missing actual ordinary import {source} -> {target}")
        require(not any(entry["module"].lower() == target.lower() for entry in row["delayImports"]), f"Unexpected delay import {source} -> {target}")
        return dict(source=source, target=target, symbols=found[0]["symbols"])
    edges = [ordinary(host_name, "RizFixedOwnedTargets.dll"), ordinary("RizFixedOwnedTargets.dll", "RizOwnedProxy.dll"),
             ordinary("RizOwnedProxy.dll", "RizOwnedRecorder.dll"), ordinary("RizOwnedProxy.dll", "RizToolbox.JudgeContext.dll"),
             ordinary("RizOwnedProxy.dll", "RizFixedPublisher.dll"),ordinary("RizOwnedRecorder.dll", "RizFixedPublisher.dll")]
    for image in images.values():
        require(not any("dobby" in entry["module"].lower() for entry in image["imports"]+image["delayImports"]), "EarlyFixed ordinary/delay graph must not import Dobby")
    for name in ("RizOwnedProxy.dll","RizOwnedRecorder.dll"):
        image = images[name]
        require(not image["delayImports"], f"{name}: loader fixture has no delay import")
        allowed = {"kernel32.dll","ntdll.dll","rizownedrecorder.dll","rizfixedpublisher.dll","riztoolbox.judgecontext.dll"}
        require(all(row["module"].lower() in allowed for row in image["imports"]), f"{name}: no undeclared CRT/allocator/loader dependency")
        for imported in image["imports"]:
            if imported["module"].lower() == "ntdll.dll":
                require(imported["symbols"] == [{"name":"__C_specific_handler"}], f"{name}: native SEH handler is its only NTDLL import")
    require(images["RizFixedOwnedTargets.dll"]["tls"] and images["RizFixedOwnedTargets.dll"]["tls"]["callbacks"], "Importer must retain real TLS callbacks")
    for name in ("RizFixedOwnedStart", "RizFixedOwnedExecute", "RizFixedOwnedRelease", "RizFixedOwnedDetach"):
        found = [row for row in images["RizFixedOwnedTargets.dll"]["exports"] if row["name"] == name]
        require(len(found) == 1 and found[0]["rva"] and not found[0]["forwarder"], f"Importer actual nonforwarded target {name} is missing")
    # The required importer cannot be an initialization dependency of its proxy
    # or recorder. Other imports are reported, not called safe by inference.
    for source in names[2:]:
        require(not any(entry["module"].lower() in {host_name.lower(), "rizfixedownedtargets.dll"}
                        for entry in images[source]["imports"] + images[source]["delayImports"]), f"Reverse importer dependency in {source}")
    component = fixed.inspect_fixed(directory/"RizFixedPublisher.dll",directory/"RizFixedOwnedTargets.dll",directory/"RizToolbox.JudgeContext.dll")
    require(images["RizFixedPublisher.dll"]["entryRva"] == 0 and not images["RizFixedPublisher.dll"]["tls"], "Fixed publisher has no entry or TLS initialization to claim returned")
    return dict(kind="owned-early-fixed-pe-import-check", passed=True, images=list(images.values()), edges=edges, fixedComponent=component,
                ordinaryStaticImportsVerified=True, allTransitiveLoaderDependenciesSafeVerified=False,
                realSystemWinHttpVerified=False, gameFirstInstallVerified=False, engineCancellationCompleted=False)


MAGIC, MAP_BYTES, EVENT_CAPACITY = 0x58465245, 449120, 4096
MODE_NAMES = ["normal", "prior-call", "late", "worker", "reentry", "duplicate", "wrong-module", "wrong-hash",
              "wrong-architecture", "wrong-prefix", "wrong-abi", "missing-dependency", "incomplete-log", "dependency-not-ready", "baseline-only", "capture-busy-probe"]
FAULT_NAMES = fixed.FAULT_NAMES
EVENT_NAMES = {1:"RecorderAttach", 2:"ProxyAttachBegin", 3:"DependencyVerified", 4:"TargetValidated", 5:"PrepareBegin", 6:"PrepareEnd",
               7:"ConfigureBegin", 8:"ConfigureEnd", 9:"CommitBegin", 10:"CommitEnd", 11:"Ready", 12:"Reject", 13:"Terminating",
               14:"InstallDeferred", 15:"ProxyAnchor", 16:"DependencyAttach", 17:"OverflowProbe", 20:"TargetEnter", 21:"TargetExit", 22:"PhaseBegin",
               23:"PhaseEnd", 24:"InitializerEnter", 25:"InitializerExit", 26:"OriginalException", 27:"OriginalCaught",
               28:"TargetArguments",29:"OriginalExceptionDetails",30:"WorkerRequested",31:"WorkerStarted",32:"WorkerFinished",40:"ObserverEnter",
               41:"ObserverExit", 42:"ObserverAttached", 43:"ObserverArguments", 44:"ObserverSnapshot", 45:"ObserverCompletion",
               46:"ObserverIdentity",47:"TargetDiagnostics",48:"OriginalTypedCaught",
               60:"PrepareGroupBegin",61:"PrepareGroupEnd",62:"BindGroupBegin",63:"BindGroupEnd",64:"SnapshotCaptured",65:"CaptureBusyObserved",
               50:"HostCompleted", 1001:"DebugDllLoad", 1002:"DebugAttachEnter", 1003:"DebugAttachReturn",
               1004:"DebugTlsEnter", 1005:"DebugTlsReturn"}


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def initialize(proof_path, mode, fault_stage, fault_index, output):
    proof = read_json(proof_path)
    require(proof["passed"] and proof["ordinaryStaticImportsVerified"], "PE proof has not passed")
    require(0 <= mode < len(MODE_NAMES) and 0 <= fault_stage < len(FAULT_NAMES) and 0 <= fault_index < 4, "Invalid parent test selector")
    require(not fault_stage or mode == 0, "Fault injection uses normal mode only")
    data = bytearray(MAP_BYTES)
    struct.pack_into("<4I", data, 0, MAGIC, 2, MAP_BYTES, EVENT_CAPACITY)
    struct.pack_into("<8I", data, 152, 2, 176, mode, fault_stage, fault_index, 0, EVENT_CAPACITY, 2)
    images = {row["name"].lower(): row for row in proof["images"]}
    for i, name in enumerate(["RizFixedOwnedTargets.dll", "RizToolbox.JudgeContext.dll", "RizFixedPublisher.dll"]):
        image = images[name.lower()]
        struct.pack_into("<QII32s", data, 184+48*i, image["bytes"], image["timeDateStamp"], 0, bytes.fromhex(image["sha256"]))
    output.write_bytes(data)
    return dict(kind="owned-early-fixed-parent-initialization", passed=True, mode=mode, modeName=MODE_NAMES[mode],
                faultStage=fault_stage, faultIndex=fault_index, bytes=MAP_BYTES, sha256=sha(data))


def decode_mapping(data):
    require(len(data) == MAP_BYTES, "Shared mapping length differs from protocol")
    magic, version, size, capacity = struct.unpack_from("<4I", data)
    require((magic, version, size, capacity) == (MAGIC, 2, MAP_BYTES, EVENT_CAPACITY), "Invalid shared header transport shape")
    next_sequence, = struct.unpack_from("<q", data, 16)
    names = ["eventCount", "overflow", "state", "failure", "writeAttempts", "writeCompletions", "childPid", "producerClaim",
             "importerInitializers", "producerThread"]
    header = dict(zip(names, struct.unpack_from("<10i", data, 24)))
    header.update(nextSequence=next_sequence, targetCalls=list(struct.unpack_from("<4i", data, 64)))
    header.update(zip(["recorderModule", "proxyModule", "importerModule", "helperModule", "publisherModule"], struct.unpack_from("<5Q", data, 80)))
    header.update(zip(["recorderAttachSequence", "proxyEnterSequence", "readySequence", "terminationSequence"], struct.unpack_from("<4q", data, 120)))
    header["config"] = dict(zip(["version", "byteSize", "mode", "faultStage", "faultIndex", "flags", "eventLimit", "expectedHelperAbi"],
                                struct.unpack_from("<8I", data, 152)))
    tuples = []
    for i in range(4):
        at = 360 + 216*i
        row = dict(zip(["version", "byteSize", "kind", "slot"], struct.unpack_from("<4I", data, at)))
        row.update(zip(["module", "target", "original", "entry", "epoch"], struct.unpack_from("<5Q", data, at+16)))
        row.update(zip(["rva", "originalProtect", "publishedProtect", "reserved"], struct.unpack_from("<4I", data, at+56)))
        row.update(zip(["prepareSequence", "configureSequence", "writeAttemptSequence", "writeCompleteSequence"], struct.unpack_from("<4Q", data, at+72)))
        row.update(originalBytes=data[at+104:at+136].hex(), publishedBytes=data[at+136:at+168].hex(), moduleSha256=data[at+168:at+200].hex())
        row.update(zip(["prepared", "configured", "committed", "failure"], struct.unpack_from("<4i", data, at+200)))
        tuples.append(row)
    events = []
    for i in range(EVENT_CAPACITY):
        at = 1224 + i*104
        sequence, = struct.unpack_from("<q", data, at)
        if sequence == 0:
            require(not any(data[at:at+104]), "Unpublished partial event row")
            continue
        row = dict(sequence=sequence, physicalIndex=i)
        row.update(zip(["pid", "tid", "type", "phase", "kind", "index", "result", "flags"], struct.unpack_from("<8I", data, at+8)))
        row.update(zip(["module", "target", "original", "entry", "epoch", "value0", "value1", "caller"], struct.unpack_from("<8Q", data, at+40)))
        require(row["type"] in EVENT_NAMES, f"Unknown event id {row['type']}")
        row["name"] = EVENT_NAMES[row["type"]]
        events.append(row)
    events.sort(key=lambda row: row["sequence"])
    return header, tuples, events


def verify_mapping(args):
    data,initial = args.mapping.read_bytes(),args.initial.read_bytes()
    header,tuples,events = decode_mapping(data)
    initial_header,_,initial_events = decode_mapping(initial)
    checks = []
    def check(value,message):
        require(value,message)
        checks.append(message)
    mode,stage,index = [header["config"][name] for name in ("mode","faultStage","faultIndex")]
    success = mode in (0,3) and stage == 0
    baseline = mode == 14
    late = mode in (1,2)
    check(0 <= mode < 16 and 0 <= stage < 15 and 0 <= index < 4 and (not stage or mode == 0),"Selectors belong to exact V2 mode/internal fault contract")
    check(not initial_events and initial_header["state"] == initial_header["nextSequence"] == 0,"Parent starts with a fresh empty mapping")
    check(data[152:328] == initial[152:328] and header["config"]["version"] == 2,"Parent V2 image/config identity stayed immutable")
    check(header["childPid"] == args.child_pid > 0 and all(row["pid"] == args.child_pid and row["tid"] for row in events),"Every event belongs to exact owned child and a real OS thread")
    check(events and all(row["sequence"] == i+1 for i,row in enumerate(events))
          and sorted(row["physicalIndex"] for row in events) == list(range(len(events))),"Published events have unique complete global sequence independent of reservation order")
    if mode == 12:
        check(header["overflow"] and header["state"] == 5 and header["failure"] == 3,"Actual log exhaustion keeps Unknown and cannot authorize admission")
    else:
        check(header["overflow"] == 0 and len(events) == header["eventCount"] == header["nextSequence"],"No missing/partial/overflowing normal event rows")
    by_type = {}
    for row in events:
        by_type.setdefault(row["type"],[]).append(row)
    def of(kind):
        return by_type.get(kind,[])
    def first(kind):
        require(of(kind),f"Missing actual {EVENT_NAMES[kind]} event")
        return of(kind)[0]["sequence"]
    check(len(of(1)) == 1 and header["recorderAttachSequence"] == first(1),"One real Recorder attach initializes the mapped transport")
    if baseline or mode == 15:
        check(not of(2) and header["proxyEnterSequence"] == 0 and header["producerClaim"] == 0,"Unpublished baseline/Recorder Busy rejection never enters or claims proxy producer")
    else:
        check(len(of(2)) == 1 and header["proxyEnterSequence"] == first(2) and first(1) < first(2),"Producer begins only after actual Recorder attach")
    check(not of(5) and not of(6),"Fixed integration does not manufacture four Dobby Prepare operations")
    if success or baseline:
        check(args.child_exit == 0 and header["state"] == 6 and header["failure"] == 0 and len(of(50)) == 1,"Actual successful/baseline child completes without failures")
        check(header["importerInitializers"] == 4,"Actual TLS/C/C++/DllMain all execute")
    else:
        check((args.child_exit&0xffffffff) == (0xe04c0000|header["failure"]) and header["failure"] != 0,"Exact failed child exit agrees with recorded rejection")
        if mode != 12:
            check(len(of(12)) == len(of(13)) == 1 and header["terminationSequence"] == first(13),"Actual rejection and terminal event are retained")
        if late:
            check(header["importerInitializers"] == 4 and len(of(14)) == 1 and first(14) < first(24) < first(2) < first(12),"Deferred producer rejects real prior initializer/target history")
            check(header["failure"] == 5,"Prior-call rejection is the actual expected failure")
        else:
            check(header["importerInitializers"] == 0 and not of(24) and not of(20),"Failed early producer never lets importer initialize or execute")
    if baseline or late:
        check(len(of(14)) == 1 and not of(60) and not of(61) and not of(7) and not of(8) and not of(62) and not of(63) and not of(9),"Deferred/baseline has zero Prepare/Configure/Bind/Commit operations")
    if baseline or (not success and mode != 5):
        check(not of(11) and header["readySequence"] == 0,"No failed/unpublished baseline produces Ready")
    if success or mode == 5:
        check(len(of(11)) == 1 and header["readySequence"] == first(11),"Historical actual Ready is preserved exactly once")
        check(header["writeAttempts"] == header["writeCompletions"] == 4,"Four actual source publications completed")
        if success:
            check(first(11) < min(row["sequence"] for row in of(24)+of(20)),"Actual Ready precedes every importer initialization and original call")
        else:
            check(header["state"] == 5 and header["failure"] == 4,"Duplicate after publication retains historical Ready and sticky Unknown")
    if of(7):
        check(len(of(60)) == len(of(61)) == 1 and of(61)[0]["result"] == 0 and first(60) < first(61) < first(7),"One actual four-entry PrepareGroup succeeds before helper Configure")
    if of(62):
        check(len(of(8)) == 4 and all(row["result"] == 0 for row in of(8)) and max(row["sequence"] for row in of(8)) < first(62),"All four real helper Configure calls precede BindGroup")
    if of(9):
        check(len(of(63)) == 1 and of(63)[0]["result"] == 0 and first(62) < first(63) < first(9),"Actual BindGroup completes before any source Commit")
    for begin,end in [(60,61),(7,8),(62,63),(9,10)]:
        check(len(of(begin)) == len(of(end)),f"Actual group/API {EVENT_NAMES[begin]} has matching captured outcome")
    proof = read_json(args.pe_proof)
    check(proof["kind"] == "owned-early-fixed-pe-import-check" and proof["passed"],"Actual fixed/no-Dobby PE proof is present")
    info,receipts,fixed_events = fixed.decode_receipt(data[427208:448848])
    meta = dict(zip(["version","byteSize","captureCalls","infoResult","memoryResult","publisherState","flags"],struct.unpack_from("<IIQIIII",data,449088)))
    check(meta["version"] == 2 and meta["byteSize"] == 32 and meta["captureCalls"] > 0 and meta["infoResult"] == meta["memoryResult"] == 0
          and meta["flags"] == (7 if mode==15 else 3) and meta["publisherState"] == info["state"],"Actual appended GetInfo/page snapshot retain exact success and permanent Busy admission facts")
    if mode == 15:
        check(len(of(65)) == 1 and first(1) < first(65) < first(12) and of(65)[0]["kind"] == 4
              and of(65)[0]["result"] == of(65)[0]["value1"] == 1 and of(65)[0]["value0"] == 2
              and of(65)[0]["flags"] == 7 and of(65)[0]["epoch"] == 3 and meta["captureCalls"] == 4,
              "Real recursive Capture loses the acquired gate (2); outer/later/final Capture preserve permanent Busy and cannot return success")
        check(header["failure"] == 22 and header["state"] == 5 and header["writeAttempts"] == header["writeCompletions"] == 0
              and not header["proxyModule"] and not header["producerThread"] and not of(60) and not of(7) and not of(62) and not of(9) and not of(14),
              "Native Busy fault itself closes admission before Proxy/Importer/Prepare or any source/protection write")
    else:
        check(not of(65),"Normal mode does not fabricate a controlled Capture conflict")
    check(header["writeAttempts"] == info["sourceWriteAttempts"] and header["writeCompletions"] == info["sourceWriteCompletions"],"Outer recorder source counts copy actual component facts rather than Commit intents")
    # Derive exact component proof with the previous frozen verifier. Derived
    # files contain only slices of the captured mapping, never synthesized rows.
    receipt_path = args.mapping.with_suffix(".publisher-info.bin")
    receipt_path.write_bytes(data[427208:448848])
    fixed_pe_path = args.mapping.with_suffix(".fixed-pe.json")
    fixed_pe_path.write_text(json.dumps(proof["fixedComponent"],ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    component_proof = fixed.verify_receipt(receipt_path,fixed_pe_path)
    if baseline or late or mode in (4,8,9,10,11,12,13,15):
        check(info["state"] == 0 and info["claimed"] == 0 and not fixed_events,"Pre-Prepare rejection or unpublished baseline leaves actual publisher Fresh")
    if mode in (6,7):
        check(info["claimed"] == 1 and info["failure"] == (4 if mode==6 else 6) and info["state"] == 6,"Wrong module/hash is rejected by actual publisher Prepare")
    if success or mode == 5:
        check(info["state"] == 5 and info["nextCommit"] == 4 and info["failure"] == 0,"Actual fixed publisher reached four-entry FixtureReady")
    if stage:
        write = stage in (4,5,6,7,8,9,10,11,13,14)
        full = write and stage != 11
        check(info["preparedCount"] == info["boundCount"] == 4 and info["nextCommit"] == index,"Internal fault retains exact permanent bindings and successful earlier prefix")
        check(info["sourceWriteAttempts"] == index+int(write) and info["sourceWriteCompletions"] == index+int(full),"Actual component internal fault has exact attempted and fully-written counts")
        check(info["failure"] == (14 if stage==11 else 13 if stage==12 else 16 if stage==13 else 17 if stage==14 else 12),"Actual component failed at the exact selected internal boundary")
        check(receipts[index]["sourceBytesWritten"] == (1 if stage==11 else receipts[index]["prefixBytes"] if full else 0),"Internal partial-write selector actually wrote one byte, distinct from standalone3byte test")
        protected = stage not in (1,12)
        readback = stage in (6,7,8,9,10,13,14)
        restored,flushed = protected and stage != 13,stage == 10
        expected = dict(protectAttempts=int(protected),protectSucceeded=int(protected),sourceWriteAttempts=int(write),
                        sourceBytesWritten=1 if stage==11 else receipts[index]["prefixBytes"] if full else 0,
                        readbackAttempts=int(readback),readbackVerified=int(readback),restoreAttempts=int(restored),
                        restoreSucceeded=int(restored),flushAttempts=int(flushed),flushSucceeded=int(flushed),
                        protectionOutstanding=int(stage==13))
        check(all(receipts[index][field] == value for field,value in expected.items()),"Selected internal fault keeps precise real API/byte/readback/restore/flush counters")
        check(all(row["flags"] == (7 if kind<index else 3) for kind,row in enumerate(receipts)),"Only real earlier successful commits carry committed authority")
        check(info["protectionOutstanding"] == int(stage==13) and info["state"] == (7 if index or write or stage==13 else 6),"Internal protection failure and source responsibility remain precise")
        check(header["failure"] == 16,"Outer producer reports actual failed Commit, not a fabricated successful publication")
    if not success and not baseline:
        unknown = bool(header["writeAttempts"] or info["protectionOutstanding"] or info["state"] == 7 or mode in (12,15))
        check(header["state"] == (5 if unknown else 4),"Outer failure preserves all actual source/protection/log Unknown obligations")
    images = {row["name"].lower():row for row in proof["images"]}
    target_image = images["rizfixedownedtargets.dll"]
    actual_memory = data[448848:449088]
    mem_version,mem_size,page_size,reserved = struct.unpack_from("<4I",actual_memory)
    check((mem_version,mem_size,reserved) == (1,240,0) and 4096 <= page_size <= 65536 and page_size&(page_size-1)==0,"Actual GetSystemInfo page size/memory transport are exact")
    outstanding = {row["target"]//page_size for row in receipts if row["protectionOutstanding"]}
    memory_rows = []
    for kind in range(4):
        at = 16+56*kind
        raw_kind,protection,target,allocation = struct.unpack_from("<IIQQ",actual_memory,at)
        raw = actual_memory[at+24:at+56]
        rva = proof["fixedComponent"]["targetRows"][kind]["targetRva"]
        original = fixed.rva_bytes(proof["fixedComponent"]["targets"],rva,32)
        row = receipts[kind]
        written = row["sourceBytesWritten"]
        patch = bytes.fromhex(row["patchBytes"])
        expected = patch[:written]+original[written:]
        check(raw_kind == kind and target == allocation+rva and allocation != 0,"Actual source snapshot belongs to exact held target PE export")
        check(raw == expected,"Every actual32-byte source snapshot has precise written patch prefix and untouched disk suffix")
        check(protection == (0x40 if {target//page_size,(target+31)//page_size}&outstanding else 0x20),"Every actual page protection includes all overlapping outstanding-source ranges")
        if row["target"]:
            check(row["target"] == target and row["module"] == allocation,"Publisher receipt and independent actual-memory addresses agree")
        memory_rows.append(dict(kind=kind,target=target,allocationBase=allocation,protection=protection,bytes=raw.hex()))
        tuple_ = tuples[kind]
        if tuple_["prepared"]:
            check(tuple_["target"] == row["target"] and tuple_["original"] == row["original"] and tuple_["slot"] == 28
                  and tuple_["originalBytes"] == row["originalBytes"] and tuple_["moduleSha256"] == target_image["sha256"],"Outer tuple agrees with real fixed prepared identity and source bytes")
        if tuple_["configured"]:
            check(tuple_["entry"] == row["entry"] and tuple_["epoch"] == row["helperEpoch"],"Outer tuple keeps actual helper epoch distinct from fixed publisher epoch")
    check(len({row["allocationBase"] for row in memory_rows}) == 1 and (not header["importerModule"] or header["importerModule"] == memory_rows[0]["allocationBase"]),
          "All four actual source pages belong to the same held Targets module and recorded image identity")
    workload = verify_workload(events,header,tuples,target_image,success,late,check,baseline)
    exceptions = exception_records(events,check)
    comparison = None
    if success:
        check(args.baseline is not None,"Successful hooked runs require the earlier actual unpublished baseline child")
        base = read_json(args.baseline)
        check(base["passed"] and base["mode"] == 14 and base["unpublishedExceptionBaseline"] and base["ownedChildPid"] != args.child_pid
              and base["targetImageSha256"] == target_image["sha256"],"Baseline is a separately completed unpublished same-binary child")
        check(len(exceptions) == len(base["exceptionRecords"]) == 8,"Baseline and hooked run each contain four SEH and four C++ samples")
        for record in exceptions:
            other = next(row for row in base["exceptionRecords"] if (row["kind"],row["faultMode"]) == (record["kind"],record["faultMode"]))
            check(record["stableTuple"] == other["stableTuple"],"Raw exception code/flags/count/payload/kind match actual cross-child same-binary baseline without masking")
        comparison = dict(baselineProofSha256=sha(args.baseline.read_bytes()),baselineChildPid=base["ownedChildPid"],
                          exactRawFieldsMatched=True,sameProcessOrObjectClaim=False)
    debug_proof = verify_debugger(args.debug_events,events,images,header,check) if args.debug_events else None
    if not args.debug_events:
        check(not any(row["type"]>=1000 for row in events),"Primary run has no debugger instrumentation")
    return dict(kind="owned-early-fixed-shared-log-verification",passed=True,checks=len(checks),assertions=checks,
                mode=mode,modeName=MODE_NAMES[mode],faultStage=stage,faultName=FAULT_NAMES[stage],faultIndex=index,
                ownedChildPid=args.child_pid,actualExitCode=args.child_exit,mappingSha256=sha(data),initialMappingSha256=sha(initial),
                header=header,tuples=tuples,events=events,publisherInfo=info,publisherReceipts=receipts,publisherEvents=fixed_events,
                publisherComponentProof=component_proof,publisherSnapshot=meta,actualMemoryRows=memory_rows,workload=workload,
                exceptionRecords=exceptions,unpublishedExceptionBaseline=baseline,exceptionBaselineComparison=comparison,
                targetImageSha256=target_image["sha256"],debuggerProof=debug_proof,normalFixtureReadyObserved=success,
                realSystemWinHttpVerified=False,gameFirstInstallVerified=False,productionV4Issued=False,
                allTransitiveLoaderDependenciesSafeVerified=False,engineCancellationCompleted=False,installed=False,gameCodeExecuted=False)


def verify_workload(events, header, tuples, importer, success, late, check, baseline):
    def of(event_type):
        return [event for event in events if event["type"] == event_type]
    enters = of(20)
    by_call = {}
    for event in events:
        if event["type"] in (20,21,26,27,28,29,40,41,43,44,45,46,47,48):
            by_call.setdefault(event["epoch"], {}).setdefault(event["type"], []).append(event)
    counts = [sum(event["kind"] == kind for event in enters) for kind in range(4)]
    check(header["targetCalls"] == counts, "Actual original-body event counts equal atomic counters")
    bodies = {}
    target_names = ["RizFixedOwnedStart", "RizFixedOwnedExecute", "RizFixedOwnedRelease", "RizFixedOwnedDetach"]
    target_rvas = {kind:next(row["rva"] for row in importer["exports"] if row["name"] == name) for kind, name in enumerate(target_names)}
    actual_module = header["importerModule"] or (enters[0]["module"] if enters else 0)
    for event in enters:
        call_id = event["epoch"]
        group = by_call[call_id]
        kind = event["kind"]
        check(call_id > 0 and len(group[20]) == 1 and kind in range(4) and event["index"] in (0,1), f"Call {call_id} has one exact body identity")
        check(all(row["kind"] == kind and row["phase"] == event["phase"] and row["index"] == event["index"] and row["tid"] == event["tid"]
                  for rows in group.values() for row in rows), f"Call {call_id} every raw scalar/exception/observer record stays on its actual physical invocation")
        check(event["caller"] != 0 and event["module"] == actual_module != 0
              and event["target"] == actual_module+target_rvas[kind]
              and (not tuples[kind]["committed"] or event["target"] == tuples[kind]["target"]), f"Call {call_id} executes the actual importer export identified in the held PE")
        check(len(group.get(28, [])) == 1, f"Call {call_id} records all four actual arguments")
        args = group[28][0]
        check(event["sequence"] < args["sequence"] and args["kind"] == kind and args["tid"] == event["tid"], f"Call {call_id} exact argument record follows body entry")
        actual_args = [event["value0"], event["value1"], args["value0"], args["value1"]]
        check(actual_args[0] != 0 and (kind == 0 or actual_args[2:] == [0,0])
              and (kind < 2 or actual_args[1] == 0), f"Call {call_id} actual typed parameter shape has no hidden fifth argument")
        fault = event["flags"]
        check(fault in (0,1,2), f"Call {call_id} fault mode is explicit")
        phase_begin = next((row for row in of(22) if row["epoch"] == call_id//8), None)
        check(phase_begin is not None, f"Call {call_id} has an actual phase generation")
        initial_references = 2 if event["phase"] == 9 else 3
        nested_exists = not (phase_begin["flags"] and phase_begin["index"] == 0)
        before_references = (initial_references if kind == 0 or event["index"] else
                             initial_references-int(nested_exists)-(1 if kind == 3 else 0))
        if kind in (2,3):
            check(len(group.get(47, [])) == 1, f"Call {call_id} has one actual prefix side-effect diagnostic")
            diagnostic = group[47][0]
            check(args["sequence"] < diagnostic["sequence"] and diagnostic["tid"] == event["tid"]
                  and diagnostic["value0"] == before_references, f"Call {call_id} prefix diagnostic follows real body entry with expected evolving reference count")
            if kind == 2:
                check(diagnostic["result"] == diagnostic["value1"] == before_references-1
                      and diagnostic["flags"] == int(before_references == 1), f"Call {call_id} real DEC executes once and preserves its exact CMP zero flag")
            else:
                check(diagnostic["result"] == diagnostic["flags"] == 0 and diagnostic["value1"] == before_references,
                      f"Call {call_id} real Detach prefix clears EBP without changing node reference count")
        else:
            check(not group.get(47), f"Call {call_id} has no unrelated Release/Detach side-effect diagnostic")
        if fault:
            check(not group.get(21) and len(group.get(26, [])) == len(group.get(27, [])) == 1, f"Call {call_id} original exception has no false normal return")
            thrown, caught = group[26][0], group[27][0]
            code = 0xe0454c53 if fault == 1 else 0xe06d7363
            check(args["sequence"] < thrown["sequence"] < caught["sequence"] and thrown["result"] == caught["result"] == code
                  and thrown["value0"] == 0xfedcba9876543210 and thrown["value1"] == call_id, f"Call {call_id} preserves actual SEH/C++ type and exception identity")
        else:
            check(not group.get(26) and not group.get(27) and len(group.get(21, [])) == 1, f"Call {call_id} returned normally exactly once")
            end = group[21][0]
            expected = [0x81,0xa5c3fe81,0xe7,0][kind]
            check(end["sequence"] > args["sequence"] and end["result"] == end["value0"] == expected, f"Call {call_id} keeps exact raw return including Execute EAX high bits")
            check((kind != 0 or end["value1"] != 0) and (kind != 1 or end["value1"] == 0x9d)
                  and (kind < 2 or end["value1"] == 0), f"Call {call_id} records exact typed output")
        bodies[call_id] = dict(enter=event, args=actual_args, group=group)
    check(len(bodies) == len(by_call), "Every observer/exception row belongs to one actual original invocation")
    stacks = {}
    for event in events:
        if event["type"] == 20:
            stack = stacks.setdefault(event["tid"], [])
            if event["index"]:
                check(stack and bodies[stack[-1]]["enter"]["kind"] == 0 and stack[-1]//8 == event["epoch"]//8
                      and event["kind"] in (1,2), f"Nested call {event['epoch']} belongs to its physical Start")
            else:
                check(not stack, f"Direct call {event['epoch']} cannot borrow an outer original scope")
            stack.append(event["epoch"])
        elif event["type"] in (21,26):
            stack = stacks.setdefault(event["tid"], [])
            check(stack and stack.pop() == event["epoch"], f"Original call {event['epoch']} exits in exact physical LIFO order")
    check(all(not stack for stack in stacks.values()), "All original-body thread stacks drained")
    phases = {}
    for event in of(22):
        check(event["epoch"] not in phases, "Phase/work generation is never reused")
        ends = [row for row in of(23) if row["epoch"] == event["epoch"]]
        check(len(ends) == 1 and ends[0]["sequence"] > event["sequence"] and ends[0]["tid"] == event["tid"]
              and ends[0]["value1"] == ends[0]["result"] == 0 and ends[0]["value0"] > 0, f"Phase {event['epoch']} exact normal physical completion")
        body_rows = [row for row in enters if row["epoch"]//8 == event["epoch"]]
        actual = [sum(row["kind"] == kind for row in body_rows) for kind in range(4)]
        nested = 0 if event["flags"] and event["index"] == 0 else 1
        check(actual == [1,1+nested,1+nested,1], f"Phase {event['epoch']} has all four direct calls and exact synchronous children")
        check(all(event["sequence"] < row["sequence"] < ends[0]["sequence"] and row["tid"] == event["tid"] for row in body_rows), f"Phase {event['epoch']} owns its actual thread and call interval")
        phases[event["epoch"]] = event
    check(len(of(23)) == len(phases), "No phase completion exists without its original scope")
    if success or late or baseline:
        init_enters, init_exits = of(24), of(25)
        check(len(init_enters) == len(init_exits) == 4 and [row["phase"] for row in init_enters] == [1,2,3,4], "Actual TLS/C/C++/DllMain initialization order is retained")
        for start in init_enters:
            end = [row for row in init_exits if row["phase"] == start["phase"]]
            work = [row for row in phases.values() if row["phase"] == start["phase"]]
            check(len(end) == len(work) == 1 and start["sequence"] < work[0]["sequence"] < end[0]["sequence"], f"Initializer {start['phase']} encloses its actual four-target work")
    if late:
        admission = next(row["sequence"] for row in of(2))
        phase = 5 if header["config"]["mode"] == 1 else 6
        historical = [row for row in phases.values() if row["phase"] == phase]
        check(len(historical) == 1 and historical[0]["sequence"] < admission, "Late rejection has actual additional original-call history before install attempt")
        if phase == 6:
            check(len(of(31)) == 1 and not of(32) and of(31)[0]["tid"] != header["producerThread"], "Late admission retains a distinct live worker instead of claiming it drained")
    if baseline:
        phase_counts = {phase:sum(event["phase"] == phase for event in phases.values()) for phase in range(1,12)}
        check(phase_counts == {1:1,2:1,3:1,4:1,5:0,6:0,7:0,8:0,9:0,10:4,11:4}
              and counts == [12,22,22,12], "Unpublished baseline executes the same real four exports from all initializers and eight exception phases")
        check(not any(of(kind) for kind in (31,32,40,41,42,43,44,45,46)), "Unpublished baseline has no worker or helper observer attachment/capture")
    if success:
        phase_counts = {phase:sum(event["phase"] == phase for event in phases.values()) for phase in range(1,10)}
        check(phase_counts == {1:1,2:1,3:1,4:1,5:1,6:(2 if header['config']['mode']==3 else 1),7:4,8:4,9:1}, "Normal workload includes initializer, post-loader, worker and eight exception phases")
        check(counts == ([16,30,30,16] if header['config']['mode']==3 else [15,28,28,15]), "Actual body counts include every synchronous child and all real exception invocations")
        worker_start, worker_end = of(31), of(32)
        check(len(worker_start) == len(worker_end) == phase_counts[6]
              and all(row["tid"] != header["producerThread"] for row in worker_start), "Actual workers execute outside the loader producer thread")
        check(of(42) and len(of(42)) == 4 and {row["kind"] for row in of(42)} == set(range(4)), "All four helper channels attached only after loader initialization")
        attach_sequence = max(row["sequence"] for row in of(42))
        check(attach_sequence > max(row["sequence"] for row in of(25)), "Actual observer attachment is outside all importer initializers")
        observer_stacks = {}
        for event in events:
            if event["type"] == 40:
                stack = observer_stacks.setdefault(event["tid"], [])
                stack.append(event["epoch"])
            elif event["type"] == 41:
                stack = observer_stacks.setdefault(event["tid"], [])
                check(stack and stack.pop() == event["epoch"], f"Observer {event['epoch']} cleans up exact same-thread LIFO scope")
        check(all(not stack for stack in observer_stacks.values()), "All observer thread stacks drained")
        for call_id, body in bodies.items():
            event, group = body["enter"], body["group"]
            observed = event["sequence"] > attach_sequence
            check(bool(group.get(40)) == observed, f"Call {call_id} has correct pre/post-Attach observation boundary")
            if not observed:
                continue
            check(all(len(group.get(key, [])) == 1 for key in (40,41,43,44,45,46)), f"Call {call_id} one complete observer transport record")
            enter, exit_, extra, snapshot, completion, identity = (group[key][0] for key in (40,41,43,44,45,46))
            check(all(enter["sequence"] < row["sequence"] < event["sequence"] for row in (extra,snapshot,identity))
                  and exit_["sequence"] < completion["sequence"], f"Call {call_id} observer records retain actual before/after order")
            check(all(row["target"] == tuples[event["kind"]]["target"] and row["original"] == tuples[event["kind"]]["original"]
                      and row["entry"] == tuples[event["kind"]]["entry"] for row in (enter,exit_,extra,snapshot,completion,identity)),
                  f"Call {call_id} observer uses the exact configured target/original/entry tuple")
            check(enter["sequence"] < event["sequence"] and exit_["sequence"] > (group[26][0] if event["flags"] else group[21][0])["sequence"]
                  and enter["tid"] == exit_["tid"] == event["tid"], f"Call {call_id} native observer encloses the actual original")
            check([enter["value0"], enter["value1"], extra["value0"], extra["value1"]] == body["args"], f"Call {call_id} transports all four original arguments exactly")
            expected_flags = 2 if event["flags"] else (1 if event["kind"] == 3 else 9)
            check(exit_["result"] == expected_flags and exit_["value0"] == (0 if event["flags"] else [0x81,0xa5c3fe81,0xe7,0][event["kind"]]), f"Call {call_id} keeps exact completion flags/raw scalar without fabricated exception return")
            check(completion["flags"] == (3 if event["kind"] == 0 and not event["flags"] else 0), f"Call {call_id} reads only actual normal Start output slot")
            check(completion["value0"] == identity["value0"] != 0
                  and completion["value1"] == identity["value1"] == (call_id ^ 0x9123456789abcdef), f"Call {call_id} completes exact captured frame sequence and token")
            if event["kind"] == 0:
                check(snapshot["flags"] == snapshot["result"] == snapshot["value0"] == snapshot["value1"] == 0,
                      f"Call {call_id} Start carries no invented node snapshot")
                check(exit_["value1"] == (0 if event["flags"] else group[21][0]["value1"]), f"Call {call_id} completion copies only its actual Start output")
            else:
                owner = next(body_["args"][0] for body_ in bodies.values() if body_["enter"]["epoch"]//8 == call_id//8 and body_["enter"]["kind"] == 0)
                phase_begin = phases[call_id//8]
                initial_references = 2 if event["phase"] == 9 else 3
                nested_exists = not (phase_begin["flags"] and phase_begin["index"] == 0)
                expected_references = initial_references if event["index"] else initial_references-int(nested_exists)-(1 if event["kind"] == 3 else 0)
                check(snapshot["flags"] == 3 and snapshot["result"] == expected_references and snapshot["value0"] == owner
                      and snapshot["value1"] == (0xf123456700000000 | (call_id//8)), f"Call {call_id} exact native scalar snapshot owner/enumhandle/refcount")
                check(exit_["value1"] == 0, f"Call {call_id} non-Start completion has no output node")
        check(len(of(40)) == len(of(41)) == 56 and [sum(row["kind"]==kind for row in of(40)) for kind in range(4)] == [10,18,18,10], "Actual helper observers contain all56 exact enter/exit pairs")
        check(all(len({row["value0"] for row in of(46) if row["kind"]==kind}) == sum(row["kind"]==kind for row in of(46)) for kind in range(4)),
              "Every observed helper frame has a distinct sequence within its exact kind/slot channel")
    elif not baseline:
        check(not any(of(kind) for kind in (40,41,42,43,44,45,46)), "Rejected producer never attaches or invents helper observations")
    return dict(actualOriginalCalls=len(enters), originalCallsByKind=counts, phases=len(phases), actualImporterBaseObserved=actual_module,
                actualNormalReturns=len(of(21)), originalExceptionCalls=len(of(26)), originalCaughtCalls=len(of(27)),
                actualObserverPairs=len(of(40)), workersStarted=len(of(31)), workersFinished=len(of(32)),
                executeFullWidthVerified=bool([row for row in of(21) if row["kind"] == 1 and row["result"] == 0xa5c3fe81]))


def exception_records(events, check):
    records = []
    for thrown in [row for row in events if row["type"] == 26]:
        call_id, kind, fault = thrown["epoch"], thrown["kind"], thrown["flags"]
        group = [row for row in events if row["epoch"] == call_id]
        caught = [row for row in group if row["type"] == 27]
        details = [row for row in group if row["type"] == 29]
        typed = [row for row in group if row["type"] == 48]
        check(len(caught) == len(details) == 1 and fault in (1,2), f"Exception {call_id} retains one actual native record and completed catch")
        caught, details = caught[0], details[0]
        code = 0xe0454c53 if fault == 1 else 0xe06d7363
        check(thrown["sequence"] < caught["sequence"] < details["sequence"] and thrown["result"] == caught["result"] == code
              and details["caller"] != 0 and details["tid"] == caught["tid"] == thrown["tid"], f"Exception {call_id} preserves exact code/address/thread/order")
        check(thrown["value0"] == 0xfedcba9876543210 and thrown["value1"] == call_id, f"Exception {call_id} originates with the actual typed payload and local call generation")
        if fault == 1:
            check(not typed and details["result"] == 3 and caught["value0"] == thrown["value0"] and caught["value1"] == call_id
                  and details["value0"] == kind and details["value1"] == 0, f"SEH {call_id} preserves all three exact exception parameters")
        else:
            check(len(typed) == 1 and thrown["sequence"] < typed[0]["sequence"] < caught["sequence"]
                  and typed[0]["result"] == kind and typed[0]["value0"] == thrown["value0"] and typed[0]["value1"] == call_id,
                  f"C++ {call_id} reaches the actual typed catch with unchanged payload/kind/local generation")
            check(details["result"] == 4 and caught["value0"] == 0x19930520 and caught["value1"] != 0
                  and details["value0"] != 0 and details["value1"] != 0, f"C++ {call_id} retains actual MSVC magic/object/type/module information")
        records.append(dict(kind=kind,faultMode=fault,callId=call_id,rawFlags=details["flags"],code=code,
                            parameterCount=details["result"],exceptionAddress=details["caller"],
                            information=[caught["value0"],caught["value1"],details["value0"],details["value1"]],
                            stableTuple=[kind,fault,code,details["flags"],details["result"],thrown["value0"],kind,
                                         caught["value0"] if fault==2 else 0]))
    check(len({(row["kind"],row["faultMode"]) for row in records}) == len(records), "Each actual exception kind has one independent same-binary comparison sample")
    return records


def verify_debugger(path, events, images, header, check):
    rows = [json.loads(line) for line in path.read_text(encoding="utf-8-sig").splitlines() if line]
    debug = [event for event in events if event["type"] >= 1000]
    check(len(rows) == len(debug) > 0, "Debugger JSONL and shared observations have exact row counts")
    for row, event in zip(rows, debug):
        check(row["sequence"] == event["sequence"] and row["type"] == event["type"] and row["pid"] == event["pid"]
              and row["tid"] == event["tid"] and row["moduleId"] == event["kind"] and row["base"] == event["module"]
              and row["target"] == event["target"] and row["returnAddress"] == event["original"] and row["returnRax"] == event["value1"], "Debugger raw JSONL matches actual shared event")
        check(row["processAttach"] and row["debugInstrumented"] and event["flags"] == 0x80000000, "Debugger observations remain explicitly instrumented")
    modules = {1:"rizfixedpublisher.dll", 2:"riztoolbox.judgecontext.dll", 3:"rizownedrecorder.dll", 4:"rizownedproxy.dll", 5:"rizfixedownedtargets.dll"}
    for module_id, name in modules.items():
        group = [event for event in debug if event["kind"] == module_id]
        loads = [event for event in group if event["type"] == 1001]
        check(len(loads) == 1, f"Actual debug DLL load for {name}")
        image = images[name]
        for entry_type, return_type, rvas in [(1002,1003,[image["entryRva"]] if image["entryRva"] else []),
                                               (1004,1005,image["tls"]["callbacks"] if image["tls"] else [])]:
            starts = [event for event in group if event["type"] == entry_type]
            ends = [event for event in group if event["type"] == return_type]
            check(len(starts) == len(ends) == len(rvas), f"Actual PE entry/TLS callback count for {name}")
            for start in starts:
                candidates = [end for end in ends if end["target"] == start["target"] and end["tid"] == start["tid"] and end["original"] == start["original"]]
                check(start["target"]-start["module"] in rvas and len(candidates) == 1 and loads[0]["sequence"] < start["sequence"] < candidates[0]["sequence"], f"Actual initialization return matched its entry/stack PC for {name}")
                if module_id in (1,2):
                    check(candidates[0]["sequence"] < header["proxyEnterSequence"], f"Dependency {name} actual initialization returned before proxy source writes")
        if module_id == 1:
            check(image["entryRva"] == 0 and not image["tls"] and len(group) == 1, "No-entry/no-TLS publisher has only an actual DLL load; no fabricated initialization callback or return")
    return dict(instrumented=True, observedRows=len(debug), rawJsonlSha256=sha(path.read_bytes()),
                actualHelperInitializationReturnBeforeProxyVerified=True, publisherHasNoInitializationEntryVerified=True, realSystemWinHttpVerified=False,
                gameFirstInstallVerified=False, allTransitiveLoaderDependenciesSafeVerified=False)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inspect-images", type=Path)
    parser.add_argument("--host-name", default="RizOwnedHost.exe")
    parser.add_argument("--initialize", action="store_true")
    parser.add_argument("--mode", type=int, default=0)
    parser.add_argument("--fault-stage", type=int, default=0)
    parser.add_argument("--fault-index", type=int, default=0)
    parser.add_argument("--pe-proof", type=Path)
    parser.add_argument("--mapping", type=Path)
    parser.add_argument("--initial", type=Path)
    parser.add_argument("--child-pid", type=int)
    parser.add_argument("--child-exit", type=int)
    parser.add_argument("--debug-events", type=Path)
    parser.add_argument("--baseline", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        if args.inspect_images:
            result = inspect_images(args.inspect_images.resolve(), args.host_name)
        elif args.initialize:
            result = initialize(args.pe_proof, args.mode, args.fault_stage, args.fault_index, args.output)
            print(json.dumps(result, ensure_ascii=False))
            return 0
        else:
            require(args.mapping and args.initial and args.pe_proof and args.child_pid is not None and args.child_exit is not None, "Missing exact mapping verification parameters")
            result = verify_mapping(args)
        args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        print(json.dumps(dict(passed=True, output=str(args.output), images=len(result.get("images", [])), checks=result.get("checks", 0)), ensure_ascii=False))
        return 0
    except Exception as error:
        result = dict(kind="owned-early-fixed-verifier-failure", passed=False, error=f"{type(error).__name__}: {error}")
        args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        print(json.dumps(result, ensure_ascii=False), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
