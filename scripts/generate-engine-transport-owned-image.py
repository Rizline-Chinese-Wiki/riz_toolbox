#!/usr/bin/env python3
"""Build a dedicated, non-game, owned PE for the V5 startup fixture.

This is a file transformation only. It never loads a target, creates a process,
executes machine code, patches the game installation, or grants game admission.
Only the selected thirteen bodies, their twenty-seven unwind records, the
bounded original .pdata table and structural PE headers survive the input.
All loader-invoked/exported code is newly emitted leaf fixture code.
"""
from __future__ import annotations

import argparse
import contextlib
import hashlib
import json
import os
from pathlib import Path
import re
import struct
import sys
from dataclasses import dataclass
from typing import BinaryIO, Iterator


PINNED_SHA = "43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1"
PINNED_BYTES = 31_006_120
FACTS_SHA = "b85a2e3e5b7a1db5c807ddabfa7409f0f9bd8f549e7603a06dd70c980b6c4bdb"
HEADER_SHA = "de2c9593aaf0d4ef11765ebf0521b2ba4a3a855af3f4420365cb497f534c213c"
IMAGE_SIZE, PREFERRED, TIMESTAMP = 0x1E65000, 0x180000000, 0x68D678AF
EXCEPTION_RVA, EXCEPTION_BYTES = 0x1D21000, 0x112B00
METADATA_RVA, METADATA_CAPACITY = 0x1E34000, 0xB80
ENTRY_RVA, UNITY_MAIN_RVA = 0x17E7C08, 0x17E7C20
TLS_CALLBACKS = (0x17E7708, 0x17E7780)
MARKER = b"RIZTRNSPOWNEDV1\0"
IMPORTS = ("WinHttpOpen", "WinHttpGetProxyForUrl", "WinHttpGetIEProxyConfigForCurrentUser", "WinHttpCloseHandle")
STUBS = (("owned-entry", ENTRY_RVA, bytes.fromhex("b801000000c3")),
         ("owned-tls-0", TLS_CALLBACKS[0], b"\xc3"),
         ("owned-tls-1", TLS_CALLBACKS[1], b"\xc3"),
         ("owned-UnityMain2", UNITY_MAIN_RVA, bytes.fromhex("31c0c3")))
# Fixed independently inspected bounds, shortest whole-instruction prefix, RF
# count and whole-body digest. Input facts cannot choose unrelated copy ranges.
FUNCTIONS = (
    ("NodeStart", 0x792240, 0x7929BF, 15, 1, "6a24dc40b10797a33529428b7d82821f1cb646a72d9108cccb884bfe2f8d7721"),
    ("NodeExecute", 0x78F840, 0x78FA2E, 18, 1, "1ffd5505fa6c15a9315524ad28f9846b8ba4ef04cbf9cf5c0a807fbd21a997f6"),
    ("NodeRelease", 0x78F680, 0x78F768, 16, 3, "771128974dd1a81b49b7f6a9743a492a752cf30aa42bd95073df111cf159d1a3"),
    ("NodeDetach", 0x793840, 0x793A5B, 15, 1, "942344fdd9c188c5e9b8371d6e03ce8fb8f944bbfcd520cadd3ef5715dc5edf7"),
    ("MRegister", 0x44D630, 0x44D756, 15, 1, "ce57348a38b6eb9041cece746e038dddd5c676721f39c4c2f857669c03b74f20"),
    ("MRemove", 0x447AD0, 0x447C26, 14, 1, "3586c1418372fd581f21df83addb7ec59256794fc52374d0fc55593a732758ff"),
    ("MConsume", 0x447C30, 0x447D60, 14, 1, "400ac94b4e996135f4a5602be15a0bcebf3f123e8f650b4dbb003544b6225abb"),
    ("MPump", 0x447D60, 0x447FAF, 15, 3, "cafab343388f190607027203284728c3ce672cfe89115a50257e0faae66e3d7c"),
    ("MDrain", 0x447750, 0x4478AC, 14, 3, "6cdea79f34ee8ddcec81015abe94c299df9732c54327e10cc0ba94040b9f435d"),
    ("MCallback", 0x78F5E0, 0x78F673, 17, 1, "0cb5566bcbbe2510032fe522b586bd1940f621ba1e41ba2c93dd2a67098beafc"),
    ("NYield", 0x78FC50, 0x79014B, 15, 3, "938b7a48664df2e0b0195fa2075f130f285a9498e0ce87693269dc2c6db3696f"),
    ("NDispatch", 0x5A5260, 0x5A5343, 14, 1, "7143eab8a6af6b584a341b1b41857b7ce9f74a4108987508ca803bdab1d11ccf"),
    ("NBatch", 0x5DA510, 0x5DA6DB, 14, 7, "01f8cbd43685808116a8c8e34fe4cf7053de54595988ae8ade76676b351d6657"),
)


def require(condition: bool, reason: str) -> None:
    if not condition:
        raise ValueError(reason)


def sha(data: bytes | bytearray) -> str:
    return hashlib.sha256(data).hexdigest()


def align(value: int, unit: int) -> int:
    return (value + unit - 1) & -unit


def absolute(value: str) -> Path:
    path = Path(value)
    require(path.is_absolute(), f"An absolute path is required: {value}")
    return path.resolve()


@contextlib.contextmanager
def locked_read(path: Path) -> Iterator[BinaryIO]:
    """Deny input write/delete for the complete transform on Windows."""
    if os.name != "nt":
        with path.open("rb") as stream:
            yield stream
        return
    import ctypes
    from ctypes import wintypes
    import msvcrt
    kernel = ctypes.WinDLL("kernel32.dll", use_last_error=True)
    kernel.CloseHandle.argtypes = (wintypes.HANDLE,)
    kernel.CloseHandle.restype = wintypes.BOOL
    create = kernel.CreateFileW
    create.argtypes = (wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, ctypes.c_void_p,
                       wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE)
    create.restype = wintypes.HANDLE
    handle = create(str(path), 0x80000000, 1, None, 3, 0x80, None)
    require(handle != ctypes.c_void_p(-1).value, f"Cannot acquire read-only input lock: {path}")
    try:
        descriptor = msvcrt.open_osfhandle(handle, os.O_RDONLY | os.O_BINARY)
    except BaseException:
        kernel.CloseHandle(handle)
        raise
    with os.fdopen(descriptor, "rb") as stream:
        yield stream


@dataclass(frozen=True)
class Section:
    name: str
    virtual_size: int
    rva: int
    raw_size: int
    raw: int
    characteristics: int


class Pe:
    def __init__(self, data: bytes | bytearray):
        self.data = data
        require(len(data) == PINNED_BYTES and data[:2] == b"MZ", "Unexpected fixed PE byte size or DOS signature")
        self.nt = self.u32(0x3C)
        require(self.nt >= 64 and self.nt + 264 <= len(data) and data[self.nt:self.nt + 4] == b"PE\0\0", "Invalid fixed NT headers")
        self.optional = self.nt + 24
        require(self.u16(self.nt + 4) == 0x8664 and self.u16(self.optional) == 0x20B
                and self.u16(self.nt + 20) == 240 and self.u16(self.nt + 6) == 8, "The fixed PE must be AMD64 with eight exact sections")
        self.headers = self.u32(self.optional + 60)
        require(self.u32(self.optional + 56) == IMAGE_SIZE and self.u32(self.nt + 8) == TIMESTAMP
                and self.u64(self.optional + 24) == PREFERRED and self.u32(self.optional + 16) == ENTRY_RVA
                and self.u32(self.optional + 108) == 16, "Fixed PE header facts differ")
        section_at = self.optional + 240
        self.sections: list[Section] = []
        for index in range(8):
            values = struct.unpack_from("<8sIIIIIIHHI", data, section_at + index * 40)
            section = Section(values[0].rstrip(b"\0").decode("ascii"), values[1], values[2], values[3], values[4], values[9])
            require(section.virtual_size > 0 and section.rva + section.virtual_size <= IMAGE_SIZE
                    and section.raw + section.raw_size <= len(data), "A fixed section is out of bounds")
            self.sections.append(section)

    def u16(self, at: int) -> int:
        return struct.unpack_from("<H", self.data, at)[0]

    def u32(self, at: int) -> int:
        return struct.unpack_from("<I", self.data, at)[0]

    def u64(self, at: int) -> int:
        return struct.unpack_from("<Q", self.data, at)[0]

    def offset(self, rva: int, count: int) -> int:
        require(count > 0 and rva >= 0 and rva + count <= IMAGE_SIZE, "Invalid bounded RVA")
        if rva + count <= self.headers:
            return rva
        matches = [s for s in self.sections if rva >= s.rva and rva - s.rva + count <= s.raw_size
                   and rva - s.rva + count <= s.virtual_size]
        require(len(matches) == 1, f"RVA {rva:x}+{count:x} is not uniquely backed by one fixed section")
        return matches[0].raw + rva - matches[0].rva

    def read(self, rva: int, count: int) -> bytes:
        start = self.offset(rva, count)
        return bytes(self.data[start:start + count])

    def directory(self, index: int) -> tuple[int, int]:
        return struct.unpack_from("<II", self.data, self.optional + 112 + index * 8)


def read_facts(raw: bytes, suffix: str, source: Pe) -> dict:
    if suffix.lower() == ".json":
        document = json.loads(raw.decode("utf-8-sig"))
        require(document.get("kind") == "fixed-engine-production-transport-contract" and document.get("schemaVersion") == 1
                and document.get("passed") is True and document.get("factsOnly") is True, "A successful fixed transport facts receipt is required")
        facts = document["facts"]
        canonical = json.dumps(facts, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")
        require(sha(canonical) == FACTS_SHA and document.get("factsPayloadSha256") == FACTS_SHA, "The independently pinned complete facts payload changed")
        require(facts["image"]["sha256"] == PINNED_SHA and facts["image"]["fileBytes"] == PINNED_BYTES, "Facts identify a different input image")
        for actual, expected in zip(facts["functions"], FUNCTIONS, strict=True):
            require((actual["name"], actual["beginRva"], actual["endRva"], actual["prefixBytes"],
                     len(actual["runtimeFunctions"]), actual["bodySha256"]) == expected,
                    "The thirteen fixed function metadata rows changed")
        return {"type": "fixed-json", "factsPayloadSha256": FACTS_SHA}
    text = raw.decode("utf-8-sig")
    require(sha(raw) == HEADER_SHA, "Only the exact independently generated fixed facts header is accepted")
    require(re.search(r'FactsSha256\[\]\s*=\s*"' + FACTS_SHA + '"', text) is not None
            and 'ProductionFirstInstallAuthorized = false' in text, "The fixed header has a different facts or admission identity")
    # Disk identity is independently pinned and all selected bytes are checked
    # again below; the header is not parsed as executable code or instructions.
    require(source.directory(3) == (EXCEPTION_RVA, EXCEPTION_BYTES), "The pinned source exception directory changed")
    return {"type": "fixed-header", "factsPayloadSha256": FACTS_SHA}


def runtime_facts(source: Pe) -> tuple[list[tuple[int, int, int]], list[dict], list[dict]]:
    require(source.directory(3) == (EXCEPTION_RVA, EXCEPTION_BYTES), "Unexpected original exception directory")
    pdata = source.read(EXCEPTION_RVA, EXCEPTION_BYTES)
    rows = [struct.unpack_from("<III", pdata, at) for at in range(0, len(pdata), 12)]
    require(len(rows) == 93_760, "The full fixed .pdata row count changed")
    for index, row in enumerate(rows):
        require(0 < row[0] < row[1] <= IMAGE_SIZE and 0 < row[2] < IMAGE_SIZE
                and (index == 0 or rows[index - 1][0] < row[0]), "The full fixed runtime-function table is invalid or unsorted")
    copied: list[dict] = []
    selected: list[dict] = []
    selected_tuples: set[tuple[int, int, int]] = set()
    for name, begin, end, prefix, count, digest in FUNCTIONS:
        body = source.read(begin, end - begin)
        require(sha(body) == digest, f"The pinned complete body changed: {name}")
        functions = [(index, row) for index, row in enumerate(rows) if begin <= row[0] and row[1] <= end]
        require(len(functions) == count and functions[0][1][0] == begin and functions[-1][1][1] == end
                and all(functions[i - 1][1][1] == functions[i][1][0] for i in range(1, count)), f"Incomplete selected RF family: {name}")
        copied.append({"kind": "selected-body", "name": name, "rva": begin, "bytes": end - begin,
                       "sha256": digest, "prefixBytes": prefix})
        for ordinal, row in functions:
            selected_tuples.add(row)
            selected.append({"function": name, "ordinal": ordinal, "beginRva": row[0], "endRva": row[1], "unwindRva": row[2]})
    unwind_by_rva: dict[int, dict] = {}
    for row in selected:
        rva = row["unwindRva"]
        head = source.read(rva, 4)
        version, flags = head[0] & 7, head[0] >> 3
        require(version == 1 and flags in (0, 4) and head[3] == 0, "The selected unwind needs an unsupported handler or frame contract")
        size = align(4 + head[2] * 2, 4) + (12 if flags == 4 else 0)
        raw = source.read(rva, size)
        if flags == 4:
            require(struct.unpack_from("<III", raw, size - 12) in selected_tuples, "A selected CHAININFO parent leaves the selected fixed functions")
        unwind_by_rva[rva] = {"kind": "selected-unwind", "rva": rva, "bytes": size, "sha256": sha(raw), "raw": raw.hex()}
    require(len(selected) == 27 and len(unwind_by_rva) == 27, "The selected source RF/unwind count changed")
    copied += sorted(unwind_by_rva.values(), key=lambda row: row["rva"])
    copied.append({"kind": "complete-source-pdata", "rva": EXCEPTION_RVA, "bytes": EXCEPTION_BYTES,
                   "rows": len(rows), "sha256": sha(pdata)})
    return rows, selected, copied


class Arena:
    def __init__(self):
        self.data = bytearray(METADATA_CAPACITY)
        self.used = 0
        self.ranges: list[dict] = []

    def reserve(self, name: str, count: int, alignment: int = 1) -> int:
        at = align(self.used, alignment)
        require(count > 0 and at + count <= len(self.data), "Owned metadata exceeds the preserved .rodata section")
        self.used = at + count
        self.ranges.append({"kind": name, "rva": METADATA_RVA + at, "bytes": count})
        return METADATA_RVA + at

    def put(self, rva: int, data: bytes) -> None:
        at = rva - METADATA_RVA
        require(0 <= at <= self.used and len(data) <= self.used - at, "Owned metadata write escapes its allocation")
        self.data[at:at + len(data)] = data

    def blob(self, name: str, data: bytes, alignment: int = 1) -> int:
        rva = self.reserve(name, len(data), alignment)
        self.put(rva, data)
        return rva


def owned_metadata(module_name: str, data_rva: int) -> tuple[Arena, dict, list[int]]:
    arena = Arena()
    export_rva = arena.reserve("owned-export-directory", 40, 4)
    eat = arena.reserve("owned-export-addresses", 8, 4)
    names = arena.reserve("owned-export-names", 8, 4)
    ordinals = arena.reserve("owned-export-ordinals", 4, 2)
    dll_name = arena.blob("owned-export-module-name", module_name.encode("ascii") + b"\0")
    export_symbols = ("RizEngineTransportOwnedMarker", "UnityMain2")
    export_names = [arena.blob("owned-export-name-" + name, name.encode("ascii") + b"\0") for name in export_symbols]
    export_bytes = arena.used
    marker_rva = arena.blob("owned-marker", MARKER, 16)
    arena.put(eat, struct.pack("<II", marker_rva, UNITY_MAIN_RVA))
    arena.put(names, struct.pack("<II", *export_names))
    arena.put(ordinals, struct.pack("<HH", 0, 1))
    arena.put(export_rva, struct.pack("<IIHHIIIIIII", 0, 0, 0, 0, dll_name, 1, 2, 2, eat, names, ordinals))
    imports_rva = arena.reserve("owned-import-descriptors", 40, 8)
    import_dll = arena.blob("owned-import-module-name", b"winhttp.dll\0")
    hints = [arena.blob("owned-import-name-" + name, b"\0\0" + name.encode("ascii") + b"\0", 2) for name in IMPORTS]
    ilt = arena.blob("owned-import-lookup-table", struct.pack("<QQQQQ", *hints, 0), 8)
    iat = arena.blob("owned-import-address-table", struct.pack("<QQQQQ", *hints, 0), 8)
    arena.put(imports_rva, struct.pack("<IIIII", ilt, 0, 0, import_dll, iat) + bytes(20))
    tls_rva = arena.reserve("owned-TLS-directory", 40, 8)
    callbacks_rva = arena.blob("owned-TLS-callback-array", struct.pack("<QQQ", *(PREFERRED + rva for rva in TLS_CALLBACKS), 0), 8)
    tls_index = data_rva
    arena.put(tls_rva, struct.pack("<QQQQII", 0, 0, PREFERRED + tls_index, PREFERRED + callbacks_rva, 0, 0))
    leaf_rva = arena.blob("owned-leaf-unwind", b"\x01\0\0\0", 4)
    relocation_slots = [tls_rva + 16, tls_rva + 24, callbacks_rva, callbacks_rva + 8]
    metadata = {"export": {"rva": export_rva, "bytes": export_bytes, "moduleName": module_name,
                           "symbols": [{"name": export_symbols[0], "rva": marker_rva, "data": True},
                                       {"name": export_symbols[1], "rva": UNITY_MAIN_RVA, "data": False}]},
                "imports": {"rva": imports_rva, "bytes": 40, "module": "winhttp.dll", "lookupRva": ilt, "iatRva": iat,
                            "names": [{"name": name, "lookupRva": ilt + index * 8, "iatRva": iat + index * 8,
                                       "hintNameRva": hints[index]} for index, name in enumerate(IMPORTS)]},
                "tls": {"rva": tls_rva, "bytes": 40, "callbackArrayRva": callbacks_rva, "callbacks": list(TLS_CALLBACKS),
                        "indexRva": tls_index, "rawStartRva": 0, "rawEndRva": 0, "zeroFillBytes": 0},
                "markerRva": marker_rva, "markerHex": MARKER.hex(), "leafUnwindRva": leaf_rva, "usedBytes": arena.used}
    return arena, metadata, relocation_slots


def relocation_bytes(slots: list[int]) -> bytes:
    pages: dict[int, list[int]] = {}
    for rva in sorted(set(slots)):
        pages.setdefault(rva & ~0xFFF, []).append(0xA000 | (rva & 0xFFF))
    result = bytearray()
    for page, entries in sorted(pages.items()):
        if len(entries) & 1:
            entries.append(0)  # IMAGE_REL_BASED_ABSOLUTE padding, not another VA.
        result += struct.pack("<II", page, 8 + len(entries) * 2) + struct.pack("<" + "H" * len(entries), *entries)
    return bytes(result)


def transform(original: bytes, module_name: str, facts_info: dict) -> tuple[bytes, dict]:
    source = Pe(original)
    rows, selected, copied = runtime_facts(source)
    output = bytearray(len(original))
    output[:source.headers] = original[:source.headers]
    for section in source.sections:
        if section.characteristics & 0x20000000:
            output[section.raw:section.raw + section.raw_size] = b"\xCC" * section.raw_size
    for row in copied:
        start = source.offset(row["rva"], row["bytes"])
        output[start:start + row["bytes"]] = original[start:start + row["bytes"]]
    data_sections = [s for s in source.sections if s.name == ".data" and s.characteristics & 0x80000000]
    require(len(data_sections) == 1 and data_sections[0].raw_size >= 16, "Owned TLS requires the exact writable .data section")
    arena, metadata, relocation_slots = owned_metadata(module_name, data_sections[0].rva)
    meta_at = source.offset(METADATA_RVA, METADATA_CAPACITY)
    output[meta_at:meta_at + METADATA_CAPACITY] = arena.data
    new_ranges = list(arena.ranges)
    selected_ordinals = {row["ordinal"] for row in selected}
    leaf_edits: list[dict] = []
    for label, rva, code in STUBS:
        require(not any(begin < rva + len(code) and rva < end for _, begin, end, *_ in FUNCTIONS), "An owned stub overlaps a fixed body")
        start = source.offset(rva, len(code)); output[start:start + len(code)] = code
        new_ranges.append({"kind": label, "rva": rva, "bytes": len(code), "hex": code.hex()})
        matches = [(ordinal, row) for ordinal, row in enumerate(rows) if row[0] <= rva and rva + len(code) <= row[1]]
        require(len(matches) == 1 and matches[0][0] not in selected_ordinals, "An owned stub lacks a unique nonselected static RF")
        ordinal, before = matches[0]
        after = (before[0], before[1], metadata["leafUnwindRva"])
        table_at = source.offset(EXCEPTION_RVA + ordinal * 12, 12)
        output[table_at:table_at + 12] = struct.pack("<III", *after)
        leaf_edits.append({"kind": label, "ordinal": ordinal, "tableRva": EXCEPTION_RVA + ordinal * 12,
                           "original": list(before), "owned": list(after), "ownedUnwindHex": "01000000"})
    relocations = relocation_bytes(relocation_slots)
    reloc_section = [s for s in source.sections if s.name == ".reloc"]
    require(len(reloc_section) == 1 and len(relocations) <= reloc_section[0].virtual_size, "Owned relocations exceed the fixed section geometry")
    reloc_rva = reloc_section[0].rva; reloc_at = source.offset(reloc_rva, len(relocations))
    output[reloc_at:reloc_at + len(relocations)] = relocations
    new_ranges += [{"kind": "owned-TLS-index-and-raw-data", "rva": data_sections[0].rva, "bytes": 16},
                   {"kind": "owned-DIR64-relocations", "rva": reloc_rva, "bytes": len(relocations), "slots": relocation_slots}]
    # The new DLL retains fixed geometry, never original loader callbacks or
    # dependency/resource/security/load-config directories.
    struct.pack_into("<II", output, source.nt + 12, 0, 0)
    characteristics = source.u16(source.nt + 22)
    struct.pack_into("<H", output, source.nt + 22, (characteristics | 0x2002) & ~1)
    struct.pack_into("<I", output, source.optional + 64, 0)  # optional CheckSum
    struct.pack_into("<H", output, source.optional + 70, (source.u16(source.optional + 70) | 0x160) & ~0x4000)
    output[source.optional + 112:source.optional + 240] = bytes(128)
    directories = {0: (metadata["export"]["rva"], metadata["export"]["bytes"]),
                   1: (metadata["imports"]["rva"], metadata["imports"]["bytes"]),
                   3: (EXCEPTION_RVA, EXCEPTION_BYTES), 5: (reloc_rva, len(relocations)),
                   9: (metadata["tls"]["rva"], metadata["tls"]["bytes"]), 12: (metadata["imports"]["iatRva"], 40)}
    for index, pair in directories.items():
        struct.pack_into("<II", output, source.optional + 112 + index * 8, *pair)
    result = Pe(output)
    require(result.sections == source.sections, "The generated DLL changed fixed raw/virtual section geometry")
    require(all(result.read(row["rva"], row["bytes"]) == source.read(row["rva"], row["bytes"])
                for row in copied if row["kind"] != "complete-source-pdata"), "A selected original body or unwind changed")
    for row in selected:
        require(result.read(EXCEPTION_RVA + row["ordinal"] * 12, 12)
                == source.read(EXCEPTION_RVA + row["ordinal"] * 12, 12), "A selected source RF changed")
    # Independently rebuild every expected raw byte and verify no unlisted game
    # content survived. This also checks gaps, padding, overlay and certificate.
    allowed = bytearray(PINNED_BYTES)
    allowed[:source.headers] = b"\1" * source.headers
    for row in copied:
        start = source.offset(row["rva"], row["bytes"]); allowed[start:start + row["bytes"]] = b"\1" * row["bytes"]
    for row in new_ranges:
        start = source.offset(row["rva"], row["bytes"]); allowed[start:start + row["bytes"]] = b"\1" * row["bytes"]
    for section in source.sections:
        expected_fill = 0xCC if section.characteristics & 0x20000000 else 0
        for at in range(section.raw, section.raw + section.raw_size):
            require(allowed[at] != 0 or output[at] == expected_fill, "Unlisted game content survived in a section")
    raw_end = max(s.raw + s.raw_size for s in source.sections)
    require(not any(output[raw_end:]), "An original overlay/certificate survived")
    manifest = {"schemaVersion": 1, "kind": "engine-transport-owned-target-image", "passed": True,
                "facts": facts_info, "moduleName": module_name, "fileBytes": len(output), "imageSize": IMAGE_SIZE,
                "machine": 0x8664, "timeDateStamp": TIMESTAMP, "preferredImageBase": PREFERRED, "entryRva": ENTRY_RVA,
                "headersBytes": source.headers, "sections": [vars(s) for s in result.sections],
                "copiedRanges": copied, "selectedRuntimeFunctions": selected,
                "ownedRanges": new_ranges, "ownedStubRuntimeFunctionEdits": leaf_edits,
                "metadata": metadata, "directories": [{"index": i, "rva": pair[0], "bytes": pair[1]} for i, pair in directories.items()],
                "relocationSlots": relocation_slots, "pdataRows": len(rows), "selectedFunctions": 13, "selectedRuntimeFunctionCount": 27,
                "sourcePdataSha256": sha(source.read(EXCEPTION_RVA, EXCEPTION_BYTES)),
                "outputPdataSha256": sha(result.read(EXCEPTION_RVA, EXCEPTION_BYTES)),
                "pdataPostCopyEditsRestrictedToOwnedStubUnwindPointers": True,
                "originalCodeOutsideSelectedBodiesRemoved": True, "unlistedRawBytesVerified": True,
                "loaderEntrypointsOwned": True, "targetLoaded": False, "targetExecuted": False,
                "gameExecuted": False, "gameDiskPatched": False, "installerExecuted": False,
                "productionAdmissionVerified": False, "fullFamilyAdoptionVerified": False}
    return bytes(output), manifest


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", required=True, help="Absolute pinned original UnityPlayer DLL, read only")
    facts = parser.add_mutually_exclusive_group(required=True)
    facts.add_argument("--facts", help="Absolute successful fixed production-transport-facts.json")
    facts.add_argument("--facts-header", help="Absolute exact generated EngineTransportGameProfile.h")
    parser.add_argument("--output-directory", required=True, help="Fresh absolute output directory outside the game installation")
    parser.add_argument("--module-name", default="RizToolbox.TransportOwnedTarget.dll")
    args = parser.parse_args()
    source_path = absolute(args.image); facts_path = absolute(args.facts or args.facts_header); output = absolute(args.output_directory)
    require(not output.exists(), "Use a fresh nonexistent output directory; prior evidence is never overwritten")
    require(output != source_path.parent and source_path.parent not in output.parents, "Owned output must be separate from the game installation")
    require(re.fullmatch(r"[A-Za-z0-9_.-]+\.dll", args.module_name) is not None
            and args.module_name.lower() not in ("unityplayer.dll", "gameassembly.dll", "winhttp.dll", "riztoolbox.enginebootstrap.dll", "riztoolbox.enginenodekernel.dll"),
            "An independent owned target DLL basename is required")
    with locked_read(source_path) as source_file, locked_read(facts_path) as facts_file:
        original = source_file.read(); facts_raw = facts_file.read()
        require(len(original) == PINNED_BYTES and sha(original) == PINNED_SHA, "The original disk image is not the exact pinned fixed game input")
        source = Pe(original); facts_info = read_facts(facts_raw, facts_path.suffix, source)
        generated, manifest = transform(original, args.module_name, facts_info)
        source_file.seek(0); facts_file.seek(0)
        require(sha(source_file.read()) == PINNED_SHA and sha(facts_file.read()) == sha(facts_raw), "Read-locked generator inputs changed")
        output.mkdir(parents=True, exist_ok=False)
        target = output / args.module_name; manifest_path = output / "owned-target-image.json"
        with target.open("xb") as file:
            require(file.write(generated) == len(generated), "Owned target persistence was incomplete")
            file.flush(); os.fsync(file.fileno())
        require(sha(target.read_bytes()) == sha(generated), "Persisted owned target differs from generated bytes")
        manifest.update({"input": {"path": str(source_path), "bytes": len(original), "sha256": PINNED_SHA, "readOnly": True},
                         "factsInput": {"path": str(facts_path), "bytes": len(facts_raw), "sha256": sha(facts_raw)},
                         "generator": {"path": str(Path(__file__).resolve()), "sha256": sha(Path(__file__).read_bytes())},
                         "output": {"path": str(target), "bytes": len(generated), "sha256": sha(generated)},
                         "inputHashesUnchanged": True})
        with manifest_path.open("x", encoding="utf-8", newline="\n") as file:
            json.dump(manifest, file, ensure_ascii=False, indent=2); file.write("\n")
        print(json.dumps({"passed": True, "target": str(target), "sha256": sha(generated), "manifest": str(manifest_path),
                          "ownedMarkerRva": manifest["metadata"]["markerRva"], "targetLoaded": False, "gameExecuted": False}, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError, KeyError, struct.error, json.JSONDecodeError) as error:
        print(f"Owned target generation failed: {error}", file=sys.stderr)
        raise SystemExit(1)
