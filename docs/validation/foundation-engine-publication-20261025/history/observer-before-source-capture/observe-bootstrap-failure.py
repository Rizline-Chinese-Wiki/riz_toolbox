"""Observe one fixed owned startup process from creation, without target calls.

Usage (root alone runs this after building the exact selected fault profile)::

  C:/Python314/python.exe observe-bootstrap-failure.py \
    --host C:/absolute/case/EnginePublication.Host.exe \
    --bootstrap C:/absolute/case/RizToolbox.EngineBootstrap.dll \
    --map C:/absolute/case/EngineBootstrap.map \
    --output C:/absolute/new-evidence-directory --timeout 30

The CLI accepts no arbitrary child command, function, RVA, target PID, or attach
operation. The actual child receives only --output <directory>/child-case.json.
Observer exit 0 means a real fail-fast debug event, full raw V4 capture and actual
child exit were observed. It does not verify the selected fault's publication
invariants: the independent parent verifier must inspect those recorded facts.
"""
from __future__ import annotations

import argparse
import ctypes as C
import hashlib
import json
import os
from pathlib import Path
import re
import struct
import subprocess
import sys
import time


U16, U32, U64, PTR = C.c_uint16, C.c_uint32, C.c_uint64, C.c_void_p
BOOL = C.c_int32
FAILFAST_CODES = {0xC0000409, 0xC0000602}
DBG_CONTINUE, DBG_EXCEPTION_NOT_HANDLED = 0x00010002, 0x80010001
DEBUG_PROCESS, DEBUG_ONLY_THIS_PROCESS, CREATE_NO_WINDOW = 1, 2, 0x08000000


def require(value, message):
    if not value:
        raise ValueError(message)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def save(path, value):
    with path.open("x", encoding="utf-8", newline="\n") as output:
        json.dump(value, output, ensure_ascii=False, indent=2)
        output.write("\n")


def normalized(path):
    value = str(path)
    if value.startswith("\\\\?\\"):
        value = value[4:]
    return str(Path(value).resolve()).replace("\\", "/").casefold()


def decode_v4(data):
    require(len(data) == 4096, "V4 capture must contain exactly 4096 bytes")
    value = dict(zip(("version", "byteSize", "state", "failure"), struct.unpack_from("<4I", data)))
    value["epoch"], = struct.unpack_from("<Q", data, 16)
    value.update(zip(("ownerThread", "processId", "attachReason", "startupStatic", "claimed", "profile", "configuredMask",
                      "boundMask", "committedMask", "sourceWriteAttempts", "sourceWriteCompletions", "protectionOutstanding",
                      "eventCount", "overflow"), struct.unpack_from("<14I", data, 24)))
    value.update(zip(("reservedPointer", "bootstrapModule", "kernelModule", "targetModule", "executableModule"), struct.unpack_from("<5Q", data, 80)))
    value["admissionFlags"], value["verifyFailures"] = struct.unpack_from("<2I", data, 120)
    modules = []
    for index in range(4):
        offset = 128 + index * 96
        item = dict(zip(("kind", "machine", "imageSize", "timeDateStamp"), struct.unpack_from("<4I", data, offset)))
        item.update(zip(("module", "fileBytes", "fileIndex"), struct.unpack_from("<3Q", data, offset + 16)))
        item["volumeSerial"], item["flags"] = struct.unpack_from("<2I", data, offset + 40)
        item["sha256"] = data[offset + 48:offset + 80].hex()
        item["heldFile"], = struct.unpack_from("<Q", data, offset + 80)
        item["entryRva"], item["exceptionRva"] = struct.unpack_from("<2I", data, offset + 88)
        modules.append(item)
    rows = []
    for index in range(4):
        offset = 512 + index * 320
        item = dict(zip(("version", "byteSize", "kind", "prefixBytes"), struct.unpack_from("<4I", data, offset)))
        item.update(zip(("module", "target", "original", "continuation", "entry", "epoch"), struct.unpack_from("<6Q", data, offset + 16)))
        item.update(zip(("rva", "originalProtection", "observedProtection", "flags", "protectAttempts", "protectSucceeded",
                         "sourceWriteAttempts", "sourceBytesWritten", "readbackAttempts", "readbackVerified", "restoreAttempts",
                         "restoreSucceeded", "flushAttempts", "flushSucceeded", "protectionOutstanding", "lastWin32Error"),
                        struct.unpack_from("<16I", data, offset + 64)))
        item["helperEpoch"], = struct.unpack_from("<Q", data, offset + 128)
        item["helperSlot"], item["reserved"] = struct.unpack_from("<2I", data, offset + 136)
        for name, at in (("originalBytes", 144), ("patchBytes", 176), ("observedBytes", 208), ("moduleSha256", 240), ("bodySha256", 272)):
            item[name] = data[offset + at:offset + at + 32].hex()
        item["runtimeFunctionCount"], = struct.unpack_from("<I", data, offset + 304)
        item["reservedTail"] = list(struct.unpack_from("<3I", data, offset + 308))
        rows.append(item)
    value["modules"], value["rows"] = modules, rows
    value["events"] = [dict(zip(("sequence", "stage", "kind", "state", "failure", "win32Error", "reserved"),
                               struct.unpack_from("<Q6I", data, 1792 + index * 32))) for index in range(64)]
    value["reserved"] = data[3840:].hex()
    return value


def pe_contract(data):
    require(len(data) >= 0x100 and data[:2] == b"MZ", "Selected bootstrap is not a disk PE")
    pe, = struct.unpack_from("<I", data, 0x3c)
    require(0 < pe <= len(data) - 264 and data[pe:pe + 4] == b"PE\0\0", "Selected bootstrap PE header is truncated")
    machine, count, timestamp, _, _, opt_size, characteristics = struct.unpack_from("<HHIIIHH", data, pe + 4)
    opt = pe + 24
    require(machine == 0x8664 and struct.unpack_from("<H", data, opt)[0] == 0x20b
            and characteristics & 0x2000 and 0 < count <= 96 and opt_size >= 240, "Selected bootstrap must be an AMD64 PE32+ DLL")
    preferred, = struct.unpack_from("<Q", data, opt + 24)
    image_size, = struct.unpack_from("<I", data, opt + 56)
    sections = []
    for index in range(count):
        at = opt + opt_size + index * 40
        require(at + 40 <= len(data), "Selected bootstrap section table is truncated")
        virtual_size, rva, raw_size, raw_offset = struct.unpack_from("<4I", data, at + 8)
        flags, = struct.unpack_from("<I", data, at + 36)
        require(raw_offset + raw_size <= len(data), "Selected bootstrap section leaves disk bytes")
        sections.append(dict(rva=rva, virtualSize=virtual_size, rawSize=raw_size, rawOffset=raw_offset, characteristics=flags))
    return dict(preferredImageBase=preferred, imageSize=image_size, timeDateStamp=timestamp, sections=sections,
                fileBytes=len(data), sha256=sha(data))


def record_rva(map_data, image):
    text = map_data.decode("utf-8-sig", errors="strict")
    values = []
    for line in text.splitlines():
        match = re.match(r"^\s+[0-9A-Fa-f]{4}:[0-9A-Fa-f]{8,16}\s+RizEnginePublicationRecord\s+([0-9A-Fa-f]{16})\b", line)
        if match:
            values.append(int(match[1], 16) - image["preferredImageBase"])
    require(len(values) == 1 and 0 < values[0] <= image["imageSize"] - 4096, "MAP must contain exactly one full-range RizEnginePublicationRecord")
    at = values[0]
    section = next((s for s in image["sections"] if s["rva"] <= at and at + 4096 <= s["rva"] + max(s["rawSize"], s["virtualSize"])), None)
    require(section and section["characteristics"] & 0xc0000000 == 0xc0000000
            and not section["characteristics"] & 0x20000000, "Named V4 record must lie wholly inside writable, readable, non-executable image data")
    return at


class EXCEPTION_RECORD(C.Structure):
    _fields_ = [("code", U32), ("flags", U32), ("record", PTR), ("address", PTR), ("count", U32), ("padding", U32), ("information", U64 * 15)]


class EXCEPTION_INFO(C.Structure):
    _fields_ = [("record", EXCEPTION_RECORD), ("firstChance", U32)]


class CREATE_PROCESS_INFO(C.Structure):
    _fields_ = [("file", PTR), ("process", PTR), ("thread", PTR), ("base", PTR), ("debugOffset", U32), ("debugSize", U32),
                ("tlsBase", PTR), ("start", PTR), ("imageName", PTR), ("unicode", U16)]


class CREATE_THREAD_INFO(C.Structure):
    _fields_ = [("thread", PTR), ("tlsBase", PTR), ("start", PTR)]


class LOAD_DLL_INFO(C.Structure):
    _fields_ = [("file", PTR), ("base", PTR), ("debugOffset", U32), ("debugSize", U32), ("imageName", PTR), ("unicode", U16)]


class DEBUG_STRING_INFO(C.Structure):
    _fields_ = [("data", PTR), ("unicode", U16), ("length", U16)]


class EVENT_UNION(C.Union):
    _fields_ = [("exception", EXCEPTION_INFO), ("createProcess", CREATE_PROCESS_INFO), ("createThread", CREATE_THREAD_INFO),
                ("loadDll", LOAD_DLL_INFO), ("exitCode", U32), ("unloadBase", PTR), ("debugString", DEBUG_STRING_INFO), ("raw", C.c_ubyte * 160)]


class DEBUG_EVENT(C.Structure):
    _fields_ = [("code", U32), ("pid", U32), ("tid", U32), ("data", EVENT_UNION)]


class STARTUPINFO(C.Structure):
    _fields_ = [("cb", U32), ("reserved", C.c_wchar_p), ("desktop", C.c_wchar_p), ("title", C.c_wchar_p),
                ("x", U32), ("y", U32), ("width", U32), ("height", U32), ("xChars", U32), ("yChars", U32),
                ("fill", U32), ("flags", U32), ("show", U16), ("reservedBytes", U16), ("reservedData", PTR),
                ("input", PTR), ("output", PTR), ("error", PTR)]


class PROCESS_INFORMATION(C.Structure):
    _fields_ = [("process", PTR), ("thread", PTR), ("pid", U32), ("tid", U32)]


class FILE_INFO(C.Structure):
    _fields_ = [("attributes", U32), ("creation", U32 * 2), ("access", U32 * 2), ("write", U32 * 2),
                ("volume", U32), ("sizeHigh", U32), ("sizeLow", U32), ("links", U32), ("indexHigh", U32), ("indexLow", U32)]


class MEMORY_INFO(C.Structure):
    _fields_ = [("base", PTR), ("allocationBase", PTR), ("allocationProtect", U32), ("partition", U16),
                ("regionSize", C.c_size_t), ("state", U32), ("protect", U32), ("kind", U32)]


class Windows:
    def __init__(self):
        require(os.name == "nt" and C.sizeof(PTR) == 8, "Observer requires 64-bit Windows Python")
        require(C.sizeof(DEBUG_EVENT) == 176 and C.sizeof(EXCEPTION_RECORD) == 152 and C.sizeof(STARTUPINFO) == 104
                and C.sizeof(MEMORY_INFO) == 48, "Actual Windows ctypes structure layout differs")
        self.dll = C.WinDLL("kernel32", use_last_error=True)
        self.bind("CreateFileW", PTR, C.c_wchar_p, U32, U32, PTR, U32, U32, PTR)
        self.bind("ReadFile", BOOL, PTR, PTR, U32, C.POINTER(U32), PTR)
        self.bind("SetFilePointerEx", BOOL, PTR, C.c_int64, C.POINTER(C.c_int64), U32)
        self.bind("GetFileInformationByHandle", BOOL, PTR, C.POINTER(FILE_INFO))
        self.bind("GetFinalPathNameByHandleW", U32, PTR, C.c_wchar_p, U32, U32)
        self.bind("CloseHandle", BOOL, PTR)
        self.bind("CreateProcessW", BOOL, C.c_wchar_p, C.c_wchar_p, PTR, PTR, BOOL, U32, PTR, C.c_wchar_p,
                  C.POINTER(STARTUPINFO), C.POINTER(PROCESS_INFORMATION))
        self.bind("WaitForDebugEvent", BOOL, C.POINTER(DEBUG_EVENT), U32)
        self.bind("ContinueDebugEvent", BOOL, U32, U32, U32)
        self.bind("ReadProcessMemory", BOOL, PTR, PTR, PTR, C.c_size_t, C.POINTER(C.c_size_t))
        self.bind("VirtualQueryEx", C.c_size_t, PTR, PTR, C.POINTER(MEMORY_INFO), C.c_size_t)
        self.bind("TerminateProcess", BOOL, PTR, U32)
        self.bind("WaitForSingleObject", U32, PTR, U32)
        self.bind("GetExitCodeProcess", BOOL, PTR, C.POINTER(U32))
        self.bind("DebugSetProcessKillOnExit", BOOL, BOOL)

    def bind(self, name, result, *arguments):
        function = getattr(self.dll, name)
        function.restype, function.argtypes = result, arguments
        setattr(self, name, function)

    @staticmethod
    def check(value, message):
        if not value:
            raise OSError(C.get_last_error(), message)

    def identity(self, handle):
        info = FILE_INFO()
        self.check(self.GetFileInformationByHandle(handle, C.byref(info)), "Cannot read actual module file identity")
        return dict(volumeSerial=info.volume, fileIndex=(info.indexHigh << 32) | info.indexLow, bytes=(info.sizeHigh << 32) | info.sizeLow)

    def path(self, handle):
        text = C.create_unicode_buffer(32768)
        count = self.GetFinalPathNameByHandleW(handle, text, len(text), 0)
        self.check(0 < count < len(text), "Cannot read actual debug-event file path")
        return text.value

    def read_file(self, handle):
        self.check(self.SetFilePointerEx(handle, 0, None, 0), "Cannot seek held input")
        buffer = C.create_string_buffer(65536)
        pieces, total = [], 0
        while True:
            count = U32()
            self.check(self.ReadFile(handle, buffer, len(buffer), C.byref(count), None), "Cannot read held input")
            if not count.value:
                break
            total += count.value
            require(total <= 64 * 1024 * 1024, "Held input exceeds fixed 64 MiB bound")
            pieces.append(buffer.raw[:count.value])
        self.check(self.SetFilePointerEx(handle, 0, None, 0), "Cannot rewind held input")
        return b"".join(pieces)

    def memory(self, process, address, count):
        value = C.create_string_buffer(count)
        read = C.c_size_t()
        self.check(self.ReadProcessMemory(process, PTR(address), value, count, C.byref(read)) and read.value == count,
                   "Cannot read complete actual remote observation")
        return value.raw


def run(args):
    win = Windows()
    host, bootstrap, map_path, output = args.host.resolve(), args.bootstrap.resolve(), args.map.resolve(), args.output.resolve()
    require(args.host.is_absolute() and args.bootstrap.is_absolute() and args.map.is_absolute() and args.output.is_absolute(), "Every observer path must be absolute")
    require(host.name == "EnginePublication.Host.exe" and bootstrap.name == "RizToolbox.EngineBootstrap.dll"
            and host.parent == bootstrap.parent, "Only the fixed same-directory owned host/bootstrap are accepted")
    inputs = [host, bootstrap, map_path, host.parent / "RizEngineOwnedTargets.dll", host.parent / "RizToolbox.EngineNodeKernel.dll", Path(__file__).resolve()]
    require(all(path.is_file() for path in inputs), "Fixed owned process dependency or MAP is missing")
    require(not output.exists(), "Preserve earlier observer evidence; use a new output directory")
    output.mkdir(parents=True)
    result = dict(kind="engine-bootstrap-failure-observation", schemaVersion=1, passed=False, observerPid=os.getpid(),
                  started=False, actualChildPid=None, actualMainThreadId=None, timedOut=False, exitObserved=False, actualExitCode=None,
                  bootstrapLoadVerified=False, fatalEventObserved=False, lastChanceObserved=False, snapshotCaptured=False,
                  wholeLoaderSafetyVerified=False, gameExecuted=False, installed=False, targetApisCalledByObserver=False,
                  inputFiles=[], moduleEvents=[], snapshots=[], headerObservations=[], error=None)
    held, debug_handles, live = [], set(), {}
    held_bytes = {}
    process = PROCESS_INFORMATION()
    image = None
    record = None
    sequence = 0
    active_base = None
    selected_generation = 0
    initial_breakpoint = False
    pending = None
    error = None
    events_path = output / "debug-events.jsonl"
    events = events_path.open("x", encoding="utf-8", newline="\n")

    def event_log(value):
        events.write(json.dumps(value, ensure_ascii=False) + "\n")
        events.flush()

    def close(handle):
        if handle:
            win.CloseHandle(handle)

    def observe_headers(event, module_path, identity, base):
        # Addresses come solely from actual OS debug events for one of the
        # four exact held fixture images, never from a command-line RVA.
        # The input list also contains MAP between bootstrap and targets. Use
        # explicit fixed path membership rather than relying on list order.
        fixed_paths = {normalized(host), normalized(bootstrap), normalized(host.parent / "RizEngineOwnedTargets.dll"),
                       normalized(host.parent / "RizToolbox.EngineNodeKernel.dll")}
        selected = next((item for item in result["inputFiles"]
                         if module_path and normalized(item["path"]) in fixed_paths
                         and normalized(item["path"]) == normalized(module_path)), None)
        if selected is None:
            return None
        item = dict(eventSequence=sequence, eventCode=event.code, modulePath=module_path, moduleBase=base,
                    threadId=event.tid, expectedPath=selected["path"], actualFileIdentity=identity,
                    heldFileIdentity=selected["identity"], identityMatched=identity == selected["identity"],
                    complete=False, error=None)
        result["headerObservations"].append(item)

        def artifact(name, data):
            path = output / f"headers-event-{sequence:05d}.{name}.bin"
            with path.open("xb") as stream:
                stream.write(data)
            return dict(path=path.name, sha256=sha(data), byteSize=len(data))

        def differences(disk, memory, disk_at, memory_at):
            return [dict(relativeOffset=index, diskFileOffset=disk_at + index,
                         memoryRva=memory_at + index, memoryAddress=base + memory_at + index,
                         diskByte=left, memoryByte=right)
                    for index, (left, right) in enumerate(zip(disk, memory)) if left != right]

        try:
            require(identity == selected["identity"], "Actual selected image header observation has a different file identity")
            disk = held_bytes[selected["path"]]
            require(len(disk) >= 64 and disk[:2] == b"MZ", "Held image lacks its complete DOS header")
            disk_dos = disk[:64]
            memory_dos = win.memory(process.process, base, 64)
            item["diskDos"] = artifact("dos-disk", disk_dos)
            item["memoryDos"] = artifact("dos-memory", memory_dos)
            item["dosDifferences"] = differences(disk_dos, memory_dos, 0, 0)
            disk_nt, = struct.unpack_from("<I", disk_dos, 0x3c)
            memory_nt, = struct.unpack_from("<I", memory_dos, 0x3c)
            item.update(diskNtRva=disk_nt, memoryNtRva=memory_nt)
            require(memory_dos[:2] == b"MZ" and 0 < disk_nt <= len(disk) - 264
                    and disk[disk_nt:disk_nt + 4] == b"PE\0\0", "DOS header does not identify a bounded actual PE header")
            disk_header = disk[disk_nt:disk_nt + 264]
            image_size, = struct.unpack_from("<I", disk_header, 80)
            require(0 < memory_nt <= 0x100000 and memory_nt + 264 <= image_size,
                    "Actual memory e_lfanew leaves its exact selected image")
            memory_header = win.memory(process.process, base + memory_nt, 264)
            item["diskNt"] = artifact("nt-disk", disk_header)
            item["memoryNt"] = artifact("nt-memory", memory_header)
            item["ntDifferences"] = differences(disk_header, memory_header, disk_nt, memory_nt)
            item.update(complete=True, diskImageSize=image_size,
                        headersExactlyEqual=disk_dos == memory_dos and disk_nt == memory_nt and disk_header == memory_header,
                        differenceCount=len(item["dosDifferences"]) + len(item["ntDifferences"]))
        except Exception as caught:
            item["error"] = f"{type(caught).__name__}: {caught}"
        save(output / f"headers-event-{sequence:05d}.json", item)
        return f"headers-event-{sequence:05d}.json"

    def capture(event, reason):
        if active_base is None:
            return None
        address = active_base + record
        name = f"publication-event-{sequence:05d}"
        preinit = reason == "bootstrap-load-before-DllEntry"
        meta = dict(eventSequence=sequence, reason=reason, threadId=event.tid, moduleBase=active_base,
                    moduleGeneration=selected_generation, recordRva=record, address=address,
                    path=None, sha256=None, byteSize=0, publication=None, preInitializationObservation=preinit,
                    permissionsStrictlyVerified=False, virtualMemorySegments=[], error=None)
        try:
            cursor = address
            while cursor < address + 4096:
                memory = MEMORY_INFO()
                queried = win.VirtualQueryEx(process.process, PTR(cursor), C.byref(memory), C.sizeof(memory))
                item = dict(queriedAddress=cursor, queryResult=queried, queryError=C.get_last_error() if queried != C.sizeof(memory) else 0,
                            base=memory.base, allocationBase=memory.allocationBase, regionSize=memory.regionSize,
                            state=memory.state, type=memory.kind, protect=memory.protect)
                meta["virtualMemorySegments"].append(item)
                require(queried == C.sizeof(memory), "Cannot query full actual V4 memory range")
                require(memory.base and memory.regionSize and memory.base <= cursor < memory.base + memory.regionSize,
                        "Actual V4 memory query does not cover its requested byte")
                item["coveredBytes"] = min(address + 4096, memory.base + memory.regionSize) - cursor
                require(memory.allocationBase == active_base and memory.state == 0x1000 and memory.kind == 0x1000000,
                        "Actual V4 record leaves its committed owned image")
                require(not memory.protect & 0x100 and memory.protect & 0xff != 1,
                        "Actual V4 record contains guard or no-access memory")
                item["writableNonExecutable"] = memory.protect & 0xff in (4, 8)
                # READWRITE and WRITECOPY are both writable image data. At the
                # actual load notification the loader may not have finalized
                # all protections; retain that raw observation with its exact
                # page facts, without treating it as terminal permission proof.
                if not preinit:
                    require(item["writableNonExecutable"], "Terminal V4 record contains executable or nonwritable image memory")
                cursor += item["coveredBytes"]
            meta["permissionsStrictlyVerified"] = all(item["writableNonExecutable"] for item in meta["virtualMemorySegments"])
            data = win.memory(process.process, address, 4096)
            raw = output / (name + ".bin")
            with raw.open("xb") as file:
                file.write(data)
            meta.update(path=raw.name, sha256=sha(data), byteSize=4096, publication=decode_v4(data))
            result["snapshotCaptured"] = True
        except Exception as caught:
            meta["error"] = f"{type(caught).__name__}: {caught}"
            if not preinit:
                raise
        finally:
            save(output / (name + ".json"), meta)
            result["snapshots"].append(meta)
        return meta["path"]

    try:
        for path in inputs:
            handle = win.CreateFileW(str(path), 0x80000000, 1, None, 3, 0x80, None)
            require(handle not in (None, C.c_void_p(-1).value), "Cannot hold exact read-only input: " + str(path))
            held.append((path, handle))
            data = win.read_file(handle)
            row = dict(path=str(path), sha256=sha(data), identity=win.identity(handle), writeAndDeleteDenied=True)
            result["inputFiles"].append(row)
            held_bytes[str(path)] = data
            if path == bootstrap:
                image = pe_contract(data)
            elif path == map_path:
                map_data = data
        record = record_rva(map_data, image)
        result["bootstrapDisk"] = dict(path=str(bootstrap), **image)
        result["mapSha256"] = next(row["sha256"] for row in result["inputFiles"] if row["path"] == str(map_path))
        result["recordSymbol"] = "RizEnginePublicationRecord"
        result["recordRva"] = record
        startup = STARTUPINFO()
        startup.cb = C.sizeof(startup)
        child_output = output / "child-case.json"
        command = C.create_unicode_buffer(subprocess.list2cmdline([str(host), "--output", str(child_output)]))
        win.check(win.CreateProcessW(str(host), command, None, None, False, DEBUG_PROCESS | DEBUG_ONLY_THIS_PROCESS | CREATE_NO_WINDOW,
                                    None, str(host.parent), C.byref(startup), C.byref(process)), "Cannot create exact owned process under debugger")
        result.update(started=True, actualChildPid=process.pid, actualMainThreadId=process.tid,
                      executable=str(host), workingDirectory=str(host.parent), childOutput=str(child_output),
                      creationFlags=DEBUG_PROCESS | DEBUG_ONLY_THIS_PROCESS | CREATE_NO_WINDOW)
        win.check(win.DebugSetProcessKillOnExit(True), "Cannot preserve debugger-owned child cleanup policy")
        deadline = time.monotonic() + args.timeout
        while not result["exitObserved"]:
            if time.monotonic() >= deadline:
                result["timedOut"] = True
                raise TimeoutError("Exact owned bootstrap process exceeded finite debug deadline")
            event = DEBUG_EVENT()
            if not win.WaitForDebugEvent(C.byref(event), 100):
                code = C.get_last_error()
                if code in (0, 121, 258):
                    continue
                raise OSError(code, "WaitForDebugEvent failed")
            pending = event
            sequence += 1
            require(event.pid == process.pid, "Debugger received an unexpected process identity")
            row = dict(sequence=sequence, eventCode=event.code, actualChildPid=event.pid, threadId=event.tid, continuation=DBG_CONTINUE)
            if event.code == 3:
                value = event.data.createProcess
                if value.process and value.process != process.process:
                    debug_handles.add(value.process)
                if value.thread and value.thread != process.thread:
                    debug_handles.add(value.thread)
                try:
                    path = win.path(value.file) if value.file else None
                    identity = win.identity(value.file) if value.file else None
                    row.update(imageBase=value.base, imagePath=path, fileIdentity=identity,
                               headerEvidence=observe_headers(event, path, identity, value.base))
                finally:
                    close(value.file)
            elif event.code == 2:
                value = event.data.createThread
                if value.thread and value.thread != process.thread:
                    debug_handles.add(value.thread)
                row.update(startAddress=value.start, tlsBase=value.tlsBase)
            elif event.code == 6:
                value = event.data.loadDll
                try:
                    path = win.path(value.file) if value.file else None
                    identity = win.identity(value.file) if value.file else None
                    row.update(moduleBase=value.base, modulePath=path, fileIdentity=identity,
                               headerEvidence=observe_headers(event, path, identity, value.base))
                    disk = None
                    if path and Path(path.replace("\\\\?\\", "")).name.casefold() == "ntdll.dll":
                        disk = pe_contract_non_dll_name(win.read_file(value.file))
                    live[value.base] = dict(path=path, imageSize=disk["imageSize"] if disk else None)
                    if path and normalized(path) == normalized(bootstrap):
                        expected = next(item for item in result["inputFiles"] if item["path"] == str(bootstrap))
                        require(identity == expected["identity"] and sha(win.read_file(value.file)) == expected["sha256"], "Actual loaded bootstrap differs from selected held DLL")
                        require(active_base is None, "A second selected bootstrap mapping appeared while one remained live")
                        active_base = value.base
                        selected_generation += 1
                        result["bootstrapLoadVerified"] = True
                        row.update(selectedBootstrap=True, moduleGeneration=selected_generation)
                        # This is the loader's actual pre-DllEntry load event;
                        # the initial record is retained even if failure comes
                        # before the first application breakpoint is reached.
                        row["snapshot"] = capture(event, "bootstrap-load-before-DllEntry")
                    elif path and Path(path.replace("\\\\?\\", "")).name.casefold() == bootstrap.name.casefold():
                        raise ValueError("The fixed bootstrap basename was loaded from a different file path")
                finally:
                    close(value.file)
                result["moduleEvents"].append(row.copy())
            elif event.code == 7:
                base = event.data.unloadBase
                row.update(moduleBase=base, previous=live.pop(base, None))
                if base == active_base:
                    row["selectedBootstrapUnloaded"] = True
                    active_base = None
                result["moduleEvents"].append(row.copy())
            elif event.code == 1:
                value = event.data.exception
                exception = value.record
                count = min(exception.count, 15)
                row.update(exceptionCode=exception.code, exceptionFlags=exception.flags, exceptionAddress=exception.address,
                           firstChance=value.firstChance, parameterCount=exception.count, information=list(exception.information[:count]),
                           continuation=DBG_EXCEPTION_NOT_HANDLED)
                is_loader_breakpoint = not initial_breakpoint and exception.code == 0x80000003 and value.firstChance == 1 and any(
                    item["path"] and Path(item["path"].replace("\\\\?\\", "")).name.casefold() == "ntdll.dll" and item["imageSize"]
                    and base <= exception.address < base + item["imageSize"] for base, item in live.items())
                if is_loader_breakpoint:
                    initial_breakpoint = True
                    row.update(initialLoaderBreakpointHandled=True, continuation=DBG_CONTINUE)
                else:
                    if not value.firstChance:
                        result["lastChanceObserved"] = True
                    if exception.code in FAILFAST_CODES:
                        result["fatalEventObserved"] = True
                    row["snapshot"] = capture(event, "last-chance-exception" if not value.firstChance else "first-chance-exception")
            elif event.code == 5:
                result.update(exitObserved=True, actualExitCode=event.data.exitCode)
                row["exitCode"] = event.data.exitCode
            elif event.code == 8:
                # Do not copy arbitrary application debug text into evidence.
                row.update(debugStringAddress=event.data.debugString.data, debugStringUnicode=event.data.debugString.unicode,
                           debugStringLength=event.data.debugString.length, debugStringTextCaptured=False)
            elif event.code == 4:
                row["threadExitCode"] = event.data.exitCode
            event_log(row)
            win.check(win.ContinueDebugEvent(event.pid, event.tid, row["continuation"]), "ContinueDebugEvent failed")
            pending = None
        exit_code = U32()
        win.check(win.GetExitCodeProcess(process.process, C.byref(exit_code)), "Cannot confirm actual child process exit")
        require(exit_code.value == result["actualExitCode"], "Debug exit and owned process handle exit disagree")
        result["processHandleExitCode"] = exit_code.value
        terminal = [item for item in result["snapshots"] if item["reason"] in ("first-chance-exception", "last-chance-exception")]
        result["terminalSnapshot"] = terminal[-1]["path"] if terminal else None
        require(result["fatalEventObserved"] and terminal and result["actualExitCode"] in FAILFAST_CODES
                and result["bootstrapLoadVerified"] and not result["timedOut"], "Fail-fast observation requires the real fatal event, actual exit, selected module and terminal V4 bytes")
        last = terminal[-1]["publication"]
        require(last["version"] == 4 and last["byteSize"] == 4096 and last["processId"] == process.pid
                and last["bootstrapModule"] == terminal[-1]["moduleBase"], "Actual terminal record is not this exact owned V4 process/bootstrap")
    except Exception as caught:
        error = f"{type(caught).__name__}: {caught}"
        result["error"] = error
    finally:
        # Only this CreateProcess handle is terminated. Never attach/kill by a
        # recycled PID, never enumerate or stop an existing application.
        if process.process and not result["exitObserved"]:
            result["cleanupTerminationRequested"] = bool(win.TerminateProcess(process.process, 0xe045424f))
            if pending is not None:
                win.ContinueDebugEvent(pending.pid, pending.tid, DBG_EXCEPTION_NOT_HANDLED if pending.code == 1 else DBG_CONTINUE)
                pending = None
            end = time.monotonic() + 5
            while time.monotonic() < end:
                event = DEBUG_EVENT()
                if win.WaitForDebugEvent(C.byref(event), 100):
                    sequence += 1
                    event_log(dict(sequence=sequence, eventCode=event.code, actualChildPid=event.pid, threadId=event.tid,
                                   cleanup=True, exitCode=event.data.exitCode if event.code == 5 else None))
                    if event.code == 6:
                        close(event.data.loadDll.file)
                    elif event.code == 3:
                        close(event.data.createProcess.file)
                    elif event.code == 2:
                        close(event.data.createThread.thread)
                    win.ContinueDebugEvent(event.pid, event.tid, DBG_EXCEPTION_NOT_HANDLED if event.code == 1 else DBG_CONTINUE)
                    if event.pid == process.pid and event.code == 5:
                        result["cleanupExitCode"] = event.data.exitCode
                        break
            result["cleanupWaitResult"] = win.WaitForSingleObject(process.process, 1000)
        for handle in debug_handles:
            close(handle)
        close(process.thread)
        close(process.process)
        for row, (path, handle) in zip(result["inputFiles"], held):
            try:
                row["afterSha256"] = sha(win.read_file(handle))
                row["afterIdentity"] = win.identity(handle)
            except Exception as caught:
                row["afterReadError"] = str(caught)
            close(handle)
        result["allHeldInputsUnchanged"] = all(row.get("afterSha256") == row["sha256"] and row.get("afterIdentity") == row["identity"] for row in result["inputFiles"])
        result["eventCount"] = sequence
        events.close()
        result["eventsSha256"] = sha(events_path.read_bytes())
        result["passed"] = error is None and result["allHeldInputsUnchanged"] and result["exitObserved"] and result["fatalEventObserved"]
        save(output / "observer-result.json", result)
    return result


def pe_contract_non_dll_name(data):
    # The same bounded parser is used for the actual ntdll load-file handle.
    return pe_contract(data)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--host", type=Path, required=True)
    parser.add_argument("--bootstrap", type=Path, required=True)
    parser.add_argument("--map", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--timeout", type=int, default=30)
    args = parser.parse_args()
    try:
        require(5 <= args.timeout <= 60, "Owned process timeout must be between 5 and 60 seconds")
        result = run(args)
        print(json.dumps(dict(passed=result["passed"], actualChildPid=result["actualChildPid"], actualExitCode=result["actualExitCode"],
                              snapshotCount=len(result["snapshots"]), error=result["error"])))
        return 0 if result["passed"] else 1
    except Exception as error:
        print(json.dumps(dict(passed=False, error=f"{type(error).__name__}: {error}")), file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
