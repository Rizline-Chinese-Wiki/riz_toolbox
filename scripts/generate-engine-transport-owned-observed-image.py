#!/usr/bin/env python3
"""Generate an explicitly observed-owned V5 target without executing any PE.

The four private loader/export stubs tail-jump to newly emitted AMD64 leaf code.
That code uses only volatile registers and direct RIP-relative memory accesses:
no stack frame, CALL, import, CRT, CLR, loader lookup or Ready claim is involved.
It records actual window bytes when the owned TLS/entry/export code is reached.
The frozen old owned generator and production/profile validators are unchanged.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import struct
import sys

REPO = Path(__file__).resolve().parents[1]
BASE_PATH = REPO / "scripts/generate-engine-transport-owned-image.py"
FACTS_HEADER = REPO / "native/RizToolbox.EngineBootstrap/EngineTransportGameProfile.h"
BASE_SHA256 = "6ec099d70cb5d65cdb7f730193e51c3a112caf3349ae9e3a660eb503f3e3ed9f"
OBSERVATION_VERSION = 1
HEADER_BYTES, CAPACITY, RECORD_BYTES, WINDOW_SLOT_BYTES = 64, 8, 1216, 32
OBSERVATION_OFFSET = 0x1000
MAGIC = b"RIZOBSERVEDV1".ljust(16, b"\0")
WRAPPERS = (("tls-0", 0x17E7708, 0), ("tls-1", 0x17E7780, 1),
            ("dll-entry", 0x17E7C08, 2), ("UnityMain2", 0x17E7C20, 3))


def require(value: bool, reason: str) -> None:
    if not value:
        raise ValueError(reason)


def sha(data: bytes | bytearray) -> str:
    return hashlib.sha256(data).hexdigest()


def import_base():
    require(sha(BASE_PATH.read_bytes()) == BASE_SHA256,
            "Frozen base owned generator differs; audit a new observation variant explicitly.")
    spec = importlib.util.spec_from_file_location("riz_observed_owned_base", BASE_PATH)
    require(spec is not None and spec.loader is not None, "Frozen owned image generator is unavailable.")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module  # dataclass type resolution, no main() call.
    spec.loader.exec_module(module)
    return module


def physical_windows(header: bytes, source) -> list[dict]:
    text = header.decode("utf-8-sig")
    block = re.search(r"inline constexpr PhysicalWindow PhysicalWindows\[\]\s*\{(.*?)\};", text, re.S)
    require(block is not None, "Independent fixed physical-window table is missing.")
    pattern = r'\{((?:0x[0-9a-fA-F]+,\s*){8})\{((?:0x[0-9a-fA-F]+,\s*){2}0x[0-9a-fA-F]+)\},\s*(\w+),\s*"([0-9a-f]{64})"\}'
    result = []
    for match in re.finditer(pattern, block.group(1)):
        values = [int(value, 16) for value in re.findall(r"0x[0-9a-fA-F]+", match.group(1))]
        index, role, role_index, observation, begin, end, count, function = values
        require(index == len(result) and 0 < count <= 32 and end - begin == count,
                "Fixed source-window order or full byte range differs.")
        original = source.read(begin, count)
        require(sha(original) == match.group(4), "A fixed full source window differs from the held original image.")
        result.append(dict(index=index, role=role, roleIndex=role_index, observationRva=observation,
                           sourceRva=begin, byteCount=count, functionIndex=function, originalSha256=sha(original),
                           payloadOffset=64 + index * WINDOW_SLOT_BYTES, slotBytes=WINDOW_SLOT_BYTES))
    require(len(result) == 36 and [row["role"] for row in result] == [0] * 13 + [1] * 23,
            "The complete exact 13 typed plus 23 side source windows are required.")
    return result


class Code:
    """Small auditable AMD64 emitter; each relocation remains image-relative."""
    def __init__(self, rva: int):
        self.rva = rva
        self.data = bytearray()
        self.labels: dict[str, int] = {}
        self.fixups: list[tuple[int, str]] = []
        self.references: list[dict] = []

    def emit(self, data: bytes) -> None:
        self.data += data

    def label(self, name: str) -> None:
        require(name not in self.labels, "Duplicate machine-code label.")
        self.labels[name] = len(self.data)

    def branch(self, opcode: bytes, label: str) -> None:
        self.emit(opcode)
        self.fixups.append((len(self.data), label))
        self.emit(bytes(4))

    def rip(self, opcode: bytes, target: int, role: str, count: int) -> None:
        self.emit(opcode)
        at = len(self.data)
        delta = target - (self.rva + at + 4)
        require(-(1 << 31) <= delta < (1 << 31), "Machine RIP-relative address exceeds signed32 range.")
        self.emit(struct.pack("<i", delta))
        self.references.append(dict(instructionRva=self.rva + at - len(opcode), displacementRva=self.rva + at,
                                    nextRva=self.rva + at + 4, targetRva=target, bytes=count, role=role))

    def finish(self) -> bytes:
        for at, name in self.fixups:
            require(name in self.labels, "Unresolved machine-code branch.")
            struct.pack_into("<i", self.data, at, self.labels[name] - (at + 4))
        return bytes(self.data)


def sampler(rva: int, storage: int, windows: list[dict]) -> tuple[bytes, list[dict]]:
    code = Code(rva)
    code.emit(bytes.fromhex("4183fa03"))                 # cmp r10d,3 (export)
    code.branch(b"\x0f\x84", "export-reason")
    code.emit(bytes.fromhex("83fa01"))                   # cmp edx,DLL_PROCESS_ATTACH
    code.branch(b"\x0f\x85", "return")
    code.emit(bytes.fromhex("4189d3"))                   # mov r11d,edx (real reason)
    code.branch(b"\xe9", "capture")
    code.label("export-reason")
    code.emit(bytes.fromhex("41bbffffffff"))             # explicit export reason sentinel
    code.label("capture")
    code.emit(bytes.fromhex("b801000000"))               # mov eax,1
    code.rip(bytes.fromhex("f0480fc105"), storage + 40, "claim-sequence-atomic-xadd", 8)
    code.emit(bytes.fromhex("48ffc0"))                   # inc rax: sequence starts at one
    code.emit(bytes.fromhex("4883f808"))                 # cmp rax,8
    code.branch(b"\x0f\x87", "overflow")
    code.emit(bytes.fromhex("4c69c0") + struct.pack("<I", RECORD_BYTES))  # imul r8,rax,1216
    code.rip(bytes.fromhex("4c8d0d"), storage + HEADER_BYTES - RECORD_BYTES, "record-address-bias", 0)
    code.emit(bytes.fromhex("4d01c8"))                   # add r8,r9
    code.emit(bytes.fromhex("498900"))                   # [record+0]=sequence
    code.emit(bytes.fromhex("45895010"))                 # [record+16]=callback ID
    code.emit(bytes.fromhex("658b0c2548000000"))         # ecx=GS:[0x48], actual TEB ThreadId
    code.emit(bytes.fromhex("41894814"))                 # [record+20]=ThreadId
    code.emit(bytes.fromhex("45895818"))                 # [record+24]=actual reason
    code.emit(bytes.fromhex("41c7401c24000000"))         # [record+28]=36 windows
    code.rip(bytes.fromhex("488d0d"), 0, "actual-loaded-image-base", 0)
    code.emit(bytes.fromhex("49894820"))                 # [record+32]=image base
    code.emit(bytes.fromhex("488b0c24"))                 # rcx=[rsp], actual incoming return PC
    code.emit(bytes.fromhex("49894828"))                 # [record+40]=return PC
    code.emit(bytes.fromhex("41c74030") + struct.pack("<I", sum(row["byteCount"] for row in windows)))
    # Copy only each declared full source range, never a synthesized patch or
    # bytes from the publication. Padding remains file-initialized zero.
    loads = {8: bytes.fromhex("488b0d"), 4: bytes.fromhex("8b0d"), 2: bytes.fromhex("668b0d"), 1: bytes.fromhex("8a0d")}
    stores = {8: bytes.fromhex("498988"), 4: bytes.fromhex("418988"), 2: bytes.fromhex("66418988"), 1: bytes.fromhex("418888")}
    for row in windows:
        used = 0
        for width in (8, 4, 2, 1):
            while row["byteCount"] - used >= width:
                code.rip(loads[width], row["sourceRva"] + used, "actual-source-window-" + str(row["index"]), width)
                code.emit(stores[width] + struct.pack("<I", row["payloadOffset"] + used))
                used += width
        require(used == row["byteCount"], "Machine sampler omitted a source byte.")
    code.emit(bytes.fromhex("49894008"))                 # last write: record commitSequence
    code.rip(bytes.fromhex("f048ff05"), storage + 56, "complete-record-atomic-inc", 8)
    code.branch(b"\xe9", "return")
    code.label("overflow")
    code.rip(bytes.fromhex("f048ff05"), storage + 48, "overflow-atomic-inc", 8)
    code.label("return")
    code.emit(bytes.fromhex("31c04183fa020f94c0c3"))     # Entry TRUE; TLS/export return zero
    return code.finish(), code.references


def observation_header() -> bytes:
    return MAGIC + struct.pack("<IIIIIIQQQ", OBSERVATION_VERSION, HEADER_BYTES,
        HEADER_BYTES + CAPACITY * RECORD_BYTES, CAPACITY, RECORD_BYTES, 36, 0, 0, 0)


def transform(base, original: bytes, module_name: str, facts_info: dict, header: bytes) -> tuple[bytes, dict]:
    frozen, manifest = base.transform(original, module_name, facts_info)
    source = base.Pe(original)
    image = base.Pe(frozen)
    windows = physical_windows(header, source)
    data_section = next(section for section in image.sections if section.name == ".data")
    require(data_section.characteristics & 0xE0000000 == 0xC0000000, "Observation storage requires the original non-executable RW .data section.")
    storage_rva = data_section.rva + OBSERVATION_OFFSET
    storage_bytes = HEADER_BYTES + CAPACITY * RECORD_BYTES
    require(OBSERVATION_OFFSET >= 16 and OBSERVATION_OFFSET + storage_bytes <= min(data_section.virtual_size, data_section.raw_size),
            "Observation storage overlaps TLS index or escapes backed RW storage.")
    rows, selected, _ = base.runtime_facts(source)
    selected_ordinals = {record["ordinal"] for record in selected}
    provisional, _ = sampler(0x1000, storage_rva, windows)
    text = next(section for section in image.sections if section.name == ".text")
    choices = [(index, row) for index, row in enumerate(rows) if index not in selected_ordinals
               and row[0] >= text.rva and row[1] <= text.rva + min(text.virtual_size, text.raw_size)
               and row[1] - row[0] >= len(provisional)
               and not any(begin < row[1] and row[0] < end for _, begin, end, *_ in base.FUNCTIONS)
               and not any(row[0] <= address < row[1] for _, address, _ in WRAPPERS)]
    require(bool(choices), "No unselected whole static RF can contain the leaf sampler.")
    helper_ordinal, before = choices[0]
    helper_rva = before[0]
    machine, references = sampler(helper_rva, storage_rva, windows)
    require(len(machine) == len(provisional), "Machine layout changed with final image-relative placement.")
    after = (helper_rva, helper_rva + len(machine), manifest["metadata"]["leafUnwindRva"])
    require((helper_ordinal == 0 or rows[helper_ordinal - 1][0] < after[0])
            and (helper_ordinal + 1 == len(rows) or after[1] <= rows[helper_ordinal + 1][0]),
            "Sampler RF would disturb sorted nonoverlapping .pdata ownership.")
    output = bytearray(frozen)
    storage_offset = image.offset(storage_rva, storage_bytes)
    require(not any(output[storage_offset:storage_offset + storage_bytes]), "Observation RW region is not wholly fresh zero-initialized owned data.")
    output[storage_offset:storage_offset + HEADER_BYTES] = observation_header()
    helper_offset = image.offset(helper_rva, len(machine))
    require(output[helper_offset:helper_offset + len(machine)] == b"\xcc" * len(machine), "Observation code would overwrite a selected or existing owned code range.")
    output[helper_offset:helper_offset + len(machine)] = machine
    table_rva = base.EXCEPTION_RVA + helper_ordinal * 12
    struct.pack_into("<III", output, image.offset(table_rva, 12), *after)
    wrappers = []
    for name, address, callback_id in WRAPPERS:
        delta = helper_rva - (address + 11)
        require(-(1 << 31) <= delta < (1 << 31), "Owned entry-to-sampler displacement exceeds rel32.")
        code = bytes.fromhex("41ba") + struct.pack("<I", callback_id) + b"\xe9" + struct.pack("<i", delta)
        require(len(code) == 11 and not any(begin < address + len(code) and address < end for _, begin, end, *_ in base.FUNCTIONS),
                "Observation entry overlaps a fixed body.")
        owner_rows = [(index, row) for index, row in enumerate(rows) if row[0] <= address and address + len(code) <= row[1]]
        require(len(owner_rows) == 1 and owner_rows[0][0] not in selected_ordinals, "Observation entry lacks its own unselected leaf RF owner.")
        offset = image.offset(address, len(code))
        output[offset:offset + len(code)] = code
        wrappers.append(dict(name=name, callbackId=callback_id, rva=address, byteCount=len(code), machineHex=code.hex(),
                             targetRva=helper_rva, staticRfOrdinal=owner_rows[0][0],
                             captureReason="DLL_PROCESS_ATTACH only" if callback_id < 3 else "explicit UnityMain2 export call"))
    final = base.Pe(output)
    require(final.sections == source.sections and len(output) == base.PINNED_BYTES, "Observed variant changed fixed PE geometry.")
    for record in manifest["copiedRanges"]:
        if record["kind"] != "complete-source-pdata":
            require(final.read(record["rva"], record["bytes"]) == source.read(record["rva"], record["bytes"]),
                    "Observed variant changed a selected complete body or source unwind.")
    for record in selected:
        rva = base.EXCEPTION_RVA + record["ordinal"] * 12
        require(final.read(rva, 12) == source.read(rva, 12), "Observed variant changed a selected static RF.")
    require(final.read(manifest["metadata"]["markerRva"], 16) == base.MARKER, "Read-only owned marker changed.")
    allowed_pdata_edits = {row["ordinal"] for row in manifest["ownedStubRuntimeFunctionEdits"]} | {helper_ordinal}
    for ordinal, old in enumerate(rows):
        now = struct.unpack("<III", final.read(base.EXCEPTION_RVA + ordinal * 12, 12))
        require(now == old or ordinal in allowed_pdata_edits, "An unrelated static RF changed in the observed variant.")
    # Rebuild the complete final file from the audited frozen base and the exact
    # four wrapper/code/data/.pdata changes, including no unlisted raw content.
    expected = bytearray(frozen)
    expected[storage_offset:storage_offset + HEADER_BYTES] = observation_header()
    expected[helper_offset:helper_offset + len(machine)] = machine
    struct.pack_into("<III", expected, image.offset(table_rva, 12), *after)
    for row in wrappers:
        offset = image.offset(row["rva"], row["byteCount"])
        expected[offset:offset + row["byteCount"]] = bytes.fromhex(row["machineHex"])
    require(output == expected, "Observed output includes an unlisted byte change.")
    manifest.update(dict(kind="engine-transport-owned-observed-target-image", ownedObservationVersion=1,
                         loaderEntrypointsObserved=True, pdataPostCopyEditsRestrictedToOwnedStubUnwindPointers=False,
                         pdataPostCopyEditsRestrictedToListedOwnedStubAndSamplerRecords=True,
                         outputPdataSha256=sha(final.read(base.EXCEPTION_RVA, base.EXCEPTION_BYTES))))
    wrapper_kinds = {"tls-0": "owned-tls-0", "tls-1": "owned-tls-1", "dll-entry": "owned-entry", "UnityMain2": "owned-UnityMain2"}
    for wrapper in wrappers:
        matches = [row for row in manifest["ownedRanges"] if row["kind"] == wrapper_kinds[wrapper["name"]]]
        require(len(matches) == 1, "Frozen base manifest lacks one exact owned entry range.")
        matches[0].update(bytes=wrapper["byteCount"], hex=wrapper["machineHex"], observationCallbackId=wrapper["callbackId"])
    manifest["ownedRanges"] += [dict(kind="observed-owned-leaf-sampler", rva=helper_rva, bytes=len(machine), sha256=sha(machine)),
                                 dict(kind="observed-owned-RW-storage", rva=storage_rva, bytes=storage_bytes)]
    manifest["ownedStubRuntimeFunctionEdits"].append(dict(kind="observed-owned-leaf-sampler", ordinal=helper_ordinal,
        tableRva=table_rva, original=list(before), owned=list(after), ownedUnwindHex="01000000"))
    manifest["observation"] = dict(version=1, storageRva=storage_rva, storageBytes=storage_bytes, magicHex=MAGIC.hex(),
        headerBytes=HEADER_BYTES, capacity=CAPACITY, recordBytes=RECORD_BYTES, slotBytes=WINDOW_SLOT_BYTES,
        headerOffsets=dict(version=16, headerBytes=20, totalBytes=24, capacity=28, recordBytes=32, windowCount=36,
                           claimedSequence=40, overflow=48, committedRecords=56),
        recordOffsets=dict(sequence=0, commitSequence=8, callbackId=16, threadId=20, reason=24, windowCount=28,
                           actualImageBase=32, incomingReturnPc=40, payloadBytes=48, reserved=52, sourcePayload=64),
        callbackIds=dict(tls0=0, tls1=1, dllEntry=2, unityMain2=3), wrappers=wrappers, physicalWindows=windows,
        sampler=dict(rva=helper_rva, byteCount=len(machine), sha256=sha(machine), machineHex=machine.hex(),
                     staticRfOrdinal=helper_ordinal, runtimeFunction=list(after), references=references,
                     stackBytes=0, calls=0, nonvolatileRegistersTouched=[], unwindHex="01000000"),
        cfg=dict(guardCfEnabled=False, loadConfigRva=0, guardFunctionTable=False,
                 policy="Owned variant has no CFG metadata; all sampler transfers are direct rel32 JMP and memory reads are not calls."),
        readinessRecorded=False, publicationQueried=False, threadIdSource="GS:[0x48] TEB.ClientId.UniqueThread low DWORD",
        proofScope="Actual source bytes first sampled inside each owned TLS/process-attach/entry/export wrapper, not production startup admission.")
    return bytes(output), manifest


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", required=True)
    facts = parser.add_mutually_exclusive_group(required=True)
    facts.add_argument("--facts")
    facts.add_argument("--facts-header")
    parser.add_argument("--output-directory", required=True)
    parser.add_argument("--module-name", default="RizToolbox.TransportOwnedTarget.dll")
    args = parser.parse_args()
    base = import_base()
    source_path = base.absolute(args.image)
    facts_path = base.absolute(args.facts or args.facts_header)
    output = base.absolute(args.output_directory)
    require(not output.exists() and output != source_path.parent and source_path.parent not in output.parents,
            "Preserve evidence and the game installation; use a new independent absolute output directory.")
    require(re.fullmatch(r"[A-Za-z0-9_.-]+\.dll", args.module_name) is not None
            and args.module_name.casefold() not in ("unityplayer.dll", "gameassembly.dll", "winhttp.dll",
                "riztoolbox.enginebootstrap.dll", "riztoolbox.enginenodekernel.dll"), "Independent owned target basename required.")
    own_path = Path(__file__).resolve()
    with base.locked_read(source_path) as source_file, base.locked_read(facts_path) as facts_file, \
            base.locked_read(FACTS_HEADER) as header_file, base.locked_read(BASE_PATH) as generator_file:
        original, facts_raw, header = source_file.read(), facts_file.read(), header_file.read()
        require(len(original) == base.PINNED_BYTES and sha(original) == base.PINNED_SHA, "Pinned input image identity differs.")
        require(sha(header) == base.HEADER_SHA and sha(generator_file.read()) == BASE_SHA256, "Frozen independent source contract changed.")
        facts_info = base.read_facts(facts_raw, facts_path.suffix, base.Pe(original))
        generated, manifest = transform(base, original, args.module_name, facts_info, header)
        for stream, expected in ((source_file, base.PINNED_SHA), (facts_file, sha(facts_raw)),
                                 (header_file, base.HEADER_SHA), (generator_file, BASE_SHA256)):
            stream.seek(0)
            require(sha(stream.read()) == expected, "A held generator input changed.")
        output.mkdir(parents=True, exist_ok=False)
        target = output / args.module_name
        with target.open("xb") as stream:
            require(stream.write(generated) == len(generated), "Observed target persistence was incomplete.")
            stream.flush()
            os.fsync(stream.fileno())
        require(sha(target.read_bytes()) == sha(generated), "Persisted observed target differs from its exact generated bytes.")
        manifest.update(dict(input=dict(path=str(source_path), bytes=len(original), sha256=base.PINNED_SHA, readOnly=True),
            factsInput=dict(path=str(facts_path), bytes=len(facts_raw), sha256=sha(facts_raw)),
            observationFactsHeader=dict(path=str(FACTS_HEADER), sha256=base.HEADER_SHA),
            generator=dict(path=str(own_path), sha256=sha(own_path.read_bytes())),
            baseGenerator=dict(path=str(BASE_PATH), sha256=BASE_SHA256),
            output=dict(path=str(target), bytes=len(generated), sha256=sha(generated)), inputHashesUnchanged=True))
        manifest_path = output / "owned-observed-target-image.json"
        with manifest_path.open("x", encoding="utf-8", newline="\n") as stream:
            json.dump(manifest, stream, ensure_ascii=False, indent=2)
            stream.write("\n")
        print(json.dumps(dict(passed=True, target=str(target), sha256=sha(generated), manifest=str(manifest_path),
                              observationStorageRva=manifest["observation"]["storageRva"], windows=36,
                              targetLoaded=False, gameExecuted=False, productionAdmissionVerified=False)))


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, KeyError, struct.error, json.JSONDecodeError) as error:
        print(f"Observed owned target generation failed: {error}", file=sys.stderr)
        raise SystemExit(1)
