"""Independent, offline PE/shared-recorder checks for owned early-loader tests.

Uses only Python's standard library. Never loads a DLL or starts a child. The
PowerShell parent owns execution and supplies the preserved raw mapping.
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


def inspect_images(directory, host_name):
    names = [host_name, "RizOwnedImporter.dll", "RizOwnedProxy.dll", "RizOwnedRecorder.dll",
             "RizToolbox.JudgeContext.dll", "dobby.dll"]
    images = {name: inspect_pe(directory / name) for name in names}
    def ordinary(source, target):
        row = images[source]
        found = [entry for entry in row["imports"] if entry["module"].lower() == target.lower()]
        require(len(found) == 1 and found[0]["symbols"], f"Missing actual ordinary import {source} -> {target}")
        require(not any(entry["module"].lower() == target.lower() for entry in row["delayImports"]), f"Unexpected delay import {source} -> {target}")
        return dict(source=source, target=target, symbols=found[0]["symbols"])
    edges = [ordinary(host_name, "RizOwnedImporter.dll"), ordinary("RizOwnedImporter.dll", "RizOwnedProxy.dll"),
             ordinary("RizOwnedProxy.dll", "RizOwnedRecorder.dll"), ordinary("RizOwnedProxy.dll", "RizToolbox.JudgeContext.dll"),
             ordinary("RizOwnedProxy.dll", "dobby.dll")]
    require(images["RizOwnedImporter.dll"]["tls"] and images["RizOwnedImporter.dll"]["tls"]["callbacks"], "Importer must retain real TLS callbacks")
    for name in ("OwnedEarlyStart", "OwnedEarlyExecute", "OwnedEarlyRelease", "OwnedEarlyDetach"):
        found = [row for row in images["RizOwnedImporter.dll"]["exports"] if row["name"] == name]
        require(len(found) == 1 and found[0]["rva"] and not found[0]["forwarder"], f"Importer actual nonforwarded target {name} is missing")
    # The required importer cannot be an initialization dependency of its proxy
    # or recorder. Other imports are reported, not called safe by inference.
    for source in names[2:]:
        require(not any(entry["module"].lower() in {host_name.lower(), "rizownedimporter.dll"}
                        for entry in images[source]["imports"] + images[source]["delayImports"]), f"Reverse importer dependency in {source}")
    return dict(kind="owned-early-loader-pe-import-check", passed=True, images=list(images.values()), edges=edges,
                ordinaryStaticImportsVerified=True, allTransitiveLoaderDependenciesSafeVerified=False,
                realSystemWinHttpVerified=False, gameFirstInstallVerified=False, engineCancellationCompleted=False)


MAGIC, MAP_BYTES, EVENT_CAPACITY = 0x594c5245, 427208, 4096
MODE_NAMES = ["normal", "prior-call", "late", "worker", "reentry", "duplicate", "wrong-module", "wrong-hash",
              "wrong-architecture", "wrong-prefix", "wrong-abi", "missing-dependency", "incomplete-log", "dependency-not-ready"]
FAULT_NAMES = ["none", "before-prepare", "after-prepare", "before-configure", "after-configure", "before-commit", "after-commit"]
EVENT_NAMES = {1:"RecorderAttach", 2:"ProxyAttachBegin", 3:"DependencyVerified", 4:"TargetValidated", 5:"PrepareBegin", 6:"PrepareEnd",
               7:"ConfigureBegin", 8:"ConfigureEnd", 9:"CommitBegin", 10:"CommitEnd", 11:"Ready", 12:"Reject", 13:"Terminating",
               14:"InstallDeferred", 15:"ProxyAnchor", 16:"DependencyAttach", 17:"OverflowProbe", 20:"TargetEnter", 21:"TargetExit", 22:"PhaseBegin",
               23:"PhaseEnd", 24:"InitializerEnter", 25:"InitializerExit", 26:"OriginalException", 27:"OriginalCaught",
               28:"TargetArguments", 30:"WorkerRequested", 31:"WorkerStarted", 32:"WorkerFinished", 40:"ObserverEnter",
               41:"ObserverExit", 42:"ObserverAttached", 43:"ObserverArguments", 44:"ObserverSnapshot", 45:"ObserverCompletion",
               46:"ObserverIdentity",
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
    struct.pack_into("<4I", data, 0, MAGIC, 1, MAP_BYTES, EVENT_CAPACITY)
    struct.pack_into("<8I", data, 152, 1, 176, mode, fault_stage, fault_index, 0, EVENT_CAPACITY, 2)
    images = {row["name"].lower(): row for row in proof["images"]}
    for i, name in enumerate(["RizOwnedImporter.dll", "RizToolbox.JudgeContext.dll", "dobby.dll"]):
        image = images[name.lower()]
        struct.pack_into("<QII32s", data, 184+48*i, image["bytes"], image["timeDateStamp"], 0, bytes.fromhex(image["sha256"]))
    output.write_bytes(data)
    return dict(kind="owned-early-loader-parent-initialization", passed=True, mode=mode, modeName=MODE_NAMES[mode],
                faultStage=fault_stage, faultIndex=fault_index, bytes=MAP_BYTES, sha256=sha(data))


def decode_mapping(data):
    require(len(data) == MAP_BYTES, "Shared mapping length differs from protocol")
    magic, version, size, capacity = struct.unpack_from("<4I", data)
    require((magic, version, size, capacity) == (MAGIC, 1, MAP_BYTES, EVENT_CAPACITY), "Invalid shared header transport shape")
    next_sequence, = struct.unpack_from("<q", data, 16)
    names = ["eventCount", "overflow", "state", "failure", "writeAttempts", "writeCompletions", "childPid", "producerClaim",
             "importerInitializers", "producerThread"]
    header = dict(zip(names, struct.unpack_from("<10i", data, 24)))
    header.update(nextSequence=next_sequence, targetCalls=list(struct.unpack_from("<4i", data, 64)))
    header.update(zip(["recorderModule", "proxyModule", "importerModule", "helperModule", "dobbyModule"], struct.unpack_from("<5Q", data, 80)))
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
    data, initial = args.mapping.read_bytes(), args.initial.read_bytes()
    header, tuples, events = decode_mapping(data)
    original_header, _, original_events = decode_mapping(initial)
    checks = []
    def check(condition, name):
        require(condition, name)
        checks.append(name)
    config = original_header["config"]
    mode, stage, index = config["mode"], config["faultStage"], config["faultIndex"]
    check(not original_events and original_header["nextSequence"] == 0 and original_header["state"] == 0, "Parent mapping was initially empty")
    check(header["childPid"] == args.child_pid and args.child_pid > 0, "Events belong to the exact owned child PID")
    check(data[152:328] == initial[152:328], "Child left parent image identity/config unchanged")
    check(all(event["pid"] == args.child_pid and event["tid"] != 0 for event in events), "Every row has real owned PID and nonzero OS TID")
    check(events and all(event["sequence"] == i+1 for i, event in enumerate(events))
          and sorted(event["physicalIndex"] for event in events) == list(range(len(events))), "Published rows form one exact ordered sequence independent of reserved slot order")
    check(len(events) <= header["eventCount"] <= max(header["nextSequence"], EVENT_CAPACITY), "Recorded event count is bounded")
    if mode != 12:
        check(header["overflow"] == 0 and len(events) == header["eventCount"] == header["nextSequence"], "No missing, partial or overflowing recorder rows")
    else:
        check(header["overflow"] != 0 and header["state"] == 5, "Intentional incomplete-log case retains Unknown")
    by_type = {}
    for event in events:
        by_type.setdefault(event["type"], []).append(event)
    def rows(kind):
        return by_type.get(kind, [])
    def first(kind):
        require(rows(kind), f"Missing event {EVENT_NAMES[kind]}")
        return rows(kind)[0]["sequence"]
    check(len(rows(1)) == 1 and header["recorderAttachSequence"] == first(1), "Actual recorder attach recorded once")
    check(header["proxyEnterSequence"] == first(2) and first(1) < first(2), "Recorder attach precedes proxy install")
    success = mode in (0, 3) and stage == 0
    late = mode in (1, 2)
    duplicate = mode == 5
    if success:
        check(args.child_exit == 0 and header["state"] == 6 and header["failure"] == 0, "Owned normal child completed normally")
        check(header["writeAttempts"] == header["writeCompletions"] == 4, "Four actual source write attempts and completions")
        check(len(rows(11)) == 1 and header["readySequence"] == first(11), "One actual fixture Ready point")
        check(header["importerInitializers"] == 4, "Four actual importer initialization stages")
        check(rows(24) and first(11) < min(event["sequence"] for event in rows(24)+rows(20)), "Ready precedes every importer initializer and target invocation")
        check(len(rows(50)) == 1 and first(50) > max(event["sequence"] for event in rows(21)+rows(41)), "Host completed after actual bodies and observers")
    else:
        check(args.child_exit & 0xffffffff == 0xe04c0000 | header["failure"], "Exact owned rejection exit matches recorded failure")
        check(header["failure"] != 0 and header["state"] in (4, 5), "Failure is retained as NoWrite or Unknown")
        if mode != 12:
            check(rows(12) and rows(13) and header["terminationSequence"] == first(13), "Rejection and terminal event retained")
        if not duplicate:
            check(not rows(11) and header["readySequence"] == 0, "Rejected admission did not produce Ready")
        else:
            check(len(rows(11)) == 1 and header["writeAttempts"] == header["writeCompletions"] == 4 and header["state"] == 5,
                  "Duplicate after real publication preserves historical Ready and sticky Unknown")
        if late:
            check(len(rows(14)) == 1 and header["importerInitializers"] == 4 and rows(20), "Late/prior modes contain real deferred importer calls")
            check(header["failure"] == 5 and header["writeAttempts"] == header["writeCompletions"] == 0 and header["state"] == 4,
                  "Actual prior history is rejected without any source write")
            check(first(14) < first(24) < first(12), "Deferred admission precedes actual initializer then rejection")
        else:
            check(header["importerInitializers"] == 0 and not rows(24) and not rows(20), "Rejected proxy did not let importer initialize or execute")
        if mode != 12:
            check(header["state"] == (5 if header["writeAttempts"] else 4), "Every write attempt makes failure permanently Unknown")
        if not stage:
            expected_failures = {1:5, 2:5, 4:4, 5:4, 6:13, 7:9, 8:10, 9:11, 10:7, 11:6, 12:3, 13:6}
            check(header["failure"] == expected_failures[mode], "The selected admission rejection, rather than an unrelated failure, was observed")
    expected_prepared, expected_configured, expected_committed = 0, 0, 0
    if success or duplicate:
        expected_prepared = expected_configured = expected_committed = 4
    elif stage:
        expected_prepared = index + (stage == 2) if stage <= 2 else 4
        expected_configured = index + (stage == 4) if stage in (3, 4) else (4 if stage >= 5 else 0)
        expected_committed = index + (stage == 6) if stage >= 5 else 0
        check(header["failure"] == 18, "The precise selected fault injection was observed")
        check(header["writeAttempts"] == header["writeCompletions"] == expected_committed, "Fault preserves exact earlier real commits")
    check(len(rows(6)) == expected_prepared and len(rows(8)) == expected_configured and len(rows(10)) == expected_committed,
          "Prepare/Configure/Commit successful rows match selected failure boundary")
    if rows(7):
        check(len(rows(6)) == 4 and max(event["sequence"] for event in rows(6)) < first(7), "All targets prepared before any configure")
    if rows(9):
        check(len(rows(8)) == 4 and max(event["sequence"] for event in rows(8)) < first(9), "All configurations immutable before first source write")
    proof = read_json(args.pe_proof)
    images = {row["name"].lower(): row for row in proof["images"]}
    importer = images["rizownedimporter.dll"]
    importer_bytes = Path(importer["path"]).read_bytes()
    def prefix(rva):
        for section in importer["sections"]:
            delta = rva-section["rva"]
            if 0 <= delta and delta+32 <= section["rawSize"]:
                return importer_bytes[section["rawOffset"]+delta:section["rawOffset"]+delta+32].hex()
        raise EvidenceError("Tuple prefix is not backed by importer file bytes")
    prepared = [row for row in tuples if row["prepared"]]
    check(len(prepared) == expected_prepared and sum(row["configured"] for row in tuples) == expected_configured
          and sum(row["committed"] for row in tuples) == expected_committed, "Immutable tuple statuses match actual successful calls")
    for kind, row in enumerate(tuples):
        if not row["version"]:
            check(not row["prepared"] and not row["configured"] and not row["committed"], f"Uninitialized kind {kind} cannot hold publication claims")
            continue
        check(row["version"] == 1 and row["byteSize"] == 216 and row["kind"] == kind and row["slot"] == 29, f"Kind {kind} tuple shape")
        validated = [event for event in rows(4) if event["kind"] == kind]
        if not validated:
            check(not row["prepared"] and not row["configured"] and not row["committed"], f"Unvalidated kind {kind} cannot acquire publication authority")
            continue
        check(len(validated) == 1, f"Kind {kind} validated once")
        check(row["module"] == header["importerModule"] and row["module"] != 0 and row["target"] == row["module"]+row["rva"]
              and row["moduleSha256"] == importer["sha256"], f"Kind {kind} actual loaded module/hash/RVA tuple")
        check(row["originalBytes"] == prefix(row["rva"]), f"Kind {kind} original source bytes match held disk image")
        if row["prepared"]:
            check(row["original"] and row["entry"] and row["original"] not in (row["target"], row["entry"])
                  and row["prepareSequence"] in [event["sequence"] for event in rows(5)], f"Kind {kind} real prepared original/entry")
        if row["configured"]:
            check(row["epoch"] and row["configureSequence"] in [event["sequence"] for event in rows(7)] and row["prepareSequence"] < row["configureSequence"], f"Kind {kind} configuration follows actual prepare")
        if row["committed"]:
            check(row["writeAttemptSequence"] in [event["sequence"] for event in rows(9)] and row["writeCompleteSequence"] in [event["sequence"] for event in rows(10)]
                  and row["configureSequence"] < row["writeAttemptSequence"] < row["writeCompleteSequence"], f"Kind {kind} exact source write events")
            check(row["publishedBytes"] != row["originalBytes"] and row["publishedProtect"] == row["originalProtect"], f"Kind {kind} source changed and protection restored")
    workload = verify_workload(events, header, tuples, importer, success, late, check)
    debug_proof = verify_debugger(args.debug_events, events, images, header, check) if args.debug_events else None
    if not args.debug_events:
        check(not any(event["type"] >= 1000 for event in events), "Primary run contains no debugger instrumentation")
    return dict(kind="owned-early-loader-shared-log-verification", passed=True, checks=len(checks), assertions=checks,
                mode=mode, modeName=MODE_NAMES[mode], faultStage=stage, faultName=FAULT_NAMES[stage], faultIndex=index,
                ownedChildPid=args.child_pid, actualExitCode=args.child_exit, mappingSha256=sha(data), initialMappingSha256=sha(initial),
                header=header, tuples=tuples, events=events, workload=workload, debuggerProof=debug_proof, normalFixtureReadyObserved=success,
                realSystemWinHttpVerified=False, gameFirstInstallVerified=False, productionV4Issued=False,
                allTransitiveLoaderDependenciesSafeVerified=False, engineCancellationCompleted=False, installed=False, gameCodeExecuted=False)


def verify_workload(events, header, tuples, importer, success, late, check):
    def of(event_type):
        return [event for event in events if event["type"] == event_type]
    enters = of(20)
    by_call = {}
    for event in events:
        if event["type"] in (20,21,26,27,28,40,41,43,44,45,46):
            by_call.setdefault(event["epoch"], {}).setdefault(event["type"], []).append(event)
    counts = [sum(event["kind"] == kind for event in enters) for kind in range(4)]
    check(header["targetCalls"] == counts, "Actual original-body event counts equal atomic counters")
    bodies = {}
    target_names = ["OwnedEarlyStart", "OwnedEarlyExecute", "OwnedEarlyRelease", "OwnedEarlyDetach"]
    target_rvas = {kind:next(row["rva"] for row in importer["exports"] if row["name"] == name) for kind, name in enumerate(target_names)}
    actual_module = header["importerModule"] or (enters[0]["module"] if enters else 0)
    for event in enters:
        call_id = event["epoch"]
        group = by_call[call_id]
        kind = event["kind"]
        check(call_id > 0 and len(group[20]) == 1 and kind in range(4) and event["index"] in (0,1), f"Call {call_id} has one exact body identity")
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
    if success or late:
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
    if success:
        phase_counts = {phase:sum(event["phase"] == phase for event in phases.values()) for phase in range(1,10)}
        check(phase_counts == {1:1,2:1,3:1,4:1,5:1,6:(2 if header['config']['mode']==3 else 1),7:4,8:4,9:1}, "Normal workload includes initializer, post-loader, worker and eight exception phases")
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
                check(snapshot["flags"] == 3 and snapshot["result"] == 3 and snapshot["value0"] == owner
                      and snapshot["value1"] == (0xf123456700000000 | (call_id//8)), f"Call {call_id} exact native scalar snapshot owner/enumhandle/refcount")
                check(exit_["value1"] == 0, f"Call {call_id} non-Start completion has no output node")
    return dict(actualOriginalCalls=len(enters), originalCallsByKind=counts, phases=len(phases), actualImporterBaseObserved=actual_module,
                actualNormalReturns=len(of(21)), originalExceptionCalls=len(of(26)), originalCaughtCalls=len(of(27)),
                actualObserverPairs=len(of(40)), workersStarted=len(of(31)), workersFinished=len(of(32)),
                executeFullWidthVerified=bool([row for row in of(21) if row["kind"] == 1 and row["result"] == 0xa5c3fe81]))


def verify_debugger(path, events, images, header, check):
    rows = [json.loads(line) for line in path.read_text(encoding="utf-8-sig").splitlines() if line]
    debug = [event for event in events if event["type"] >= 1000]
    check(len(rows) == len(debug) > 0, "Debugger JSONL and shared observations have exact row counts")
    for row, event in zip(rows, debug):
        check(row["sequence"] == event["sequence"] and row["type"] == event["type"] and row["pid"] == event["pid"]
              and row["tid"] == event["tid"] and row["moduleId"] == event["kind"] and row["base"] == event["module"]
              and row["target"] == event["target"] and row["returnAddress"] == event["original"] and row["returnRax"] == event["value1"], "Debugger raw JSONL matches actual shared event")
        check(row["processAttach"] and row["debugInstrumented"] and event["flags"] == 0x80000000, "Debugger observations remain explicitly instrumented")
    modules = {1:"dobby.dll", 2:"riztoolbox.judgecontext.dll", 3:"rizownedrecorder.dll", 4:"rizownedproxy.dll", 5:"rizownedimporter.dll"}
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
    return dict(instrumented=True, observedRows=len(debug), rawJsonlSha256=sha(path.read_bytes()),
                actualDependencyInitializationReturnBeforeProxyVerified=True, realSystemWinHttpVerified=False,
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
        result = dict(kind="owned-early-loader-verifier-failure", passed=False, error=f"{type(error).__name__}: {error}")
        args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        print(json.dumps(result, ensure_ascii=False), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
