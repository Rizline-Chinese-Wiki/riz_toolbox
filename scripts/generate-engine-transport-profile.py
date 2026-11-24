"""Generate V5 native compile facts from four actual PE files; never load them.

Bootstrap's own bytes cannot contain their final whole-file hash. Its generated
identity therefore observes the held self file; the independent CLR profile must
pin the final five whole-file hashes after linking. All other identities and all
non-self ILT/IAT/EAT RVAs are exact disk facts, never an admission credential.
"""
from __future__ import annotations

import argparse
import bisect
import hashlib
import json
import re
import struct
from pathlib import Path

import pefile

REPO = Path(__file__).resolve().parents[1]
FACTS = REPO / "native/RizToolbox.EngineBootstrap/EngineTransportGameProfile.h"
CONTRACT = REPO / "native/RizToolbox.EngineBootstrap/EngineTransportCompileContract.h"
ADMISSION_FACTS = REPO / "native/RizToolbox.EngineBootstrap/EngineTransportAdmissionFacts.h"
BOOTSTRAP = "RizToolbox.EngineBootstrap.dll"
KERNEL = "RizToolbox.EngineNodeKernel.dll"
MARKER_NAME = "RizEngineTransportOwnedMarker"
MARKER = b"RIZTRNSPOWNEDV1\0"
TARGET_SHA = "43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1"
EXECUTABLE_SHA = "f9c37f1078619d02c62a7a5815ca48179666efb2dfe7e6e698bfff7858c26e5e"
WINHTTP = ("WinHttpOpen", "WinHttpGetProxyForUrl", "WinHttpGetIEProxyConfigForCurrentUser", "WinHttpCloseHandle")
BOOTSTRAP_NAMES = ("RizEngineTransport_GetVersion", "RizEngineTransport_GetInfoV5", "RizEngineTransport_VerifyV5", "RizEngineTransport_AbortAfterPublication")
KERNEL_NAMES = (
    "RizEngineNodeContext_GetAbiVersion", "RizEngineNodeContext_GetLimit", "RizEngineNodeContext_GetFrameSize", "RizEngineNodeContext_GetCompletionSize",
    "RizEngineNodeContext_GetEntry", "RizEngineNodeContext_Configure", "RizEngineNodeContext_Attach", "RizEngineNodeContext_GetInfo",
    "RizEngineNodeContext_GetInlineAbiVersion", "RizEngineNodeContext_GetInlineLimit", "RizEngineNodeContext_GetInlineFrameSize", "RizEngineNodeContext_GetInlineEntry",
    "RizEngineNodeContext_ConfigureInline", "RizEngineNodeContext_AttachInline", "RizEngineNodeContext_GetInlineInfo",
    "RizEngineSchedulerContext_GetAbiVersion", "RizEngineSchedulerContext_GetLimit", "RizEngineSchedulerContext_GetFrameSize", "RizEngineSchedulerContext_GetCompletionSize",
    "RizEngineSchedulerContext_GetRowFrameSize", "RizEngineSchedulerContext_GetEntry", "RizEngineSchedulerContext_GetDrainRowEntry", "RizEngineSchedulerContext_Configure",
    "RizEngineSchedulerContext_Attach", "RizEngineSchedulerContext_GetInfo", "RizEngineWaitContext_GetAbiVersion", "RizEngineWaitContext_GetLimit",
    "RizEngineWaitContext_GetFrameSize", "RizEngineWaitContext_GetCompletionSize", "RizEngineWaitContext_GetPhaseFrameSize", "RizEngineWaitContext_GetEntry",
    "RizEngineWaitContext_GetPhaseEntry", "RizEngineWaitContext_Configure", "RizEngineWaitContext_Attach", "RizEngineWaitContext_GetInfo",
)
SYMBOLS = ("UnityMain2",) + WINHTTP + BOOTSTRAP_NAMES + KERNEL_NAMES
FIXED_ENTRY = 0x17E7C08
OWNED_UNITY_MAIN = 0x17E7C20
OWNED_TLS_CALLBACKS = (0x17E7708, 0x17E7780)


def require(condition: bool, reason: str) -> None:
    if not condition:
        raise ValueError(reason)


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def absolute_file(value: Path) -> Path:
    require(value.is_absolute(), f"Absolute input path required: {value}")
    require(value.is_file() and not value.is_symlink(), f"Ordinary input file required: {value}")
    path = value.resolve(strict=True)
    require(bool(re.fullmatch(r"[A-Za-z0-9_. -]+", path.name)), f"ASCII module basename required: {path.name}")
    return path


def absolute_output(value: Path) -> Path:
    require(value.is_absolute(), f"Absolute output path required: {value}")
    path = value.resolve()
    require(not path.exists(), f"Preserve previous output: {path}")
    game = Path("D:/SteamLibrary/steamapps/common/Rizline").resolve()
    require(path != game and game not in path.parents, "Generated files must be outside the game installation.")
    return path


def facts() -> dict:
    raw = FACTS.read_bytes()
    text = raw.decode("utf-8-sig")
    arrays = {
        name: bytes(int(value, 16) for value in re.findall(r"0x([0-9a-fA-F]{2})(?![0-9a-fA-F])", body))
        for name, body in re.findall(r"inline constexpr unsigned char (\w+)\[\]\s*\{(.*?)\};", text, re.S)
    }
    function_block = re.search(r"inline constexpr Function Functions\[\]\s*\{(.*?)\};", text, re.S)
    require(function_block is not None, "Fixed thirteen-function source table is missing.")
    functions = []
    row_pattern = r'\{"(\w+)",\s*((?:0x[0-9a-fA-F]+,\s*){7})(\w+),\s*(\w+),\s*"([0-9a-f]{64})",\s*(\w+)\}'
    for match in re.finditer(row_pattern, function_block.group(1)):
        name, numbers, prefix_name, body_name, body_sha, rf_name = match.groups()
        index, begin, end, prefix_bytes, body_bytes, continuation, rf_count = [int(value, 16) for value in re.findall(r"0x[0-9a-fA-F]+", numbers)]
        body, prefix = arrays[body_name], arrays[prefix_name]
        require(index == len(functions) and end - begin == body_bytes == len(body), "Fixed function table shape drifted.")
        require(continuation == begin + prefix_bytes and len(prefix) == prefix_bytes and body.startswith(prefix) and sha(body) == body_sha,
                f"Fixed function digest/prefix differs: {name}")
        rf_match = re.search(r"inline constexpr RuntimeFunction " + re.escape(rf_name) + r"\[\]\s*\{(.*?)\};", text, re.S)
        require(rf_match is not None, f"Missing source runtime functions: {name}")
        rows = [tuple(int(value, 16) for value in group) for group in re.findall(r"\{(0x[0-9a-fA-F]+),\s*(0x[0-9a-fA-F]+),\s*(0x[0-9a-fA-F]+)\}", rf_match.group(1))]
        require(len(rows) == rf_count, f"Source runtime function count differs: {name}")
        functions.append(dict(name=name, index=index, beginRva=begin, endRva=end, prefixBytes=prefix_bytes,
                              bodyBytes=body_bytes, bodySha256=body_sha, body=body, runtimeFunctions=rows))
    require(len(functions) == 13 and sum(len(row["runtimeFunctions"]) for row in functions) == 27, "Fixed 13/27 source shape is incomplete.")
    unwind = {int(name[len("Unwind_"):], 16): data for name, data in arrays.items() if name.startswith("Unwind_")}
    require(unwind and all(row[2] in unwind for function in functions for row in function["runtimeFunctions"]), "Source unwind facts are incomplete.")
    digest_match = re.search(r'inline constexpr char FactsSha256\[\]\s*=\s*"([0-9a-f]{64})";', text)
    require(digest_match is not None, "Fixed contract SHA is missing.")
    contract_raw = CONTRACT.read_bytes()
    contract_text = contract_raw.decode("utf-8-sig")
    symbol_match = re.search(r"Symbols\[EdgeCount\]\s*\{(.*?)\};", contract_text, re.S)
    require(symbol_match is not None and tuple(re.findall(r'"(\w+)"', symbol_match.group(1))) == SYMBOLS,
            "The generator and native 44-symbol contract disagree.")
    return dict(functions=functions, unwind=unwind, fixedContractSha256=digest_match.group(1),
                sourceBytes=raw, contractBytes=contract_raw)


class Image:
    def __init__(self, path: Path):
        self.path = absolute_file(path)
        self.data = self.path.read_bytes()
        self.pe = pefile.PE(data=self.data, fast_load=True)
        pe = self.pe
        require(pe.FILE_HEADER.Machine == 0x8664 and pe.OPTIONAL_HEADER.Magic == 0x20B
                and pe.FILE_HEADER.SizeOfOptionalHeader == 240 and pe.OPTIONAL_HEADER.NumberOfRvaAndSizes == 16,
                f"AMD64 PE32+ image with a complete header required: {path}")
        require(0x1000 <= pe.OPTIONAL_HEADER.SizeOfImage <= 0x20000000, f"Image size outside native bounds: {path}")
        pe.parse_data_directories(directories=[0, 1, 3, 5, 9, 13])
        self.record = dict(path=str(self.path), module=self.path.name, sha256=sha(self.data), fileBytes=len(self.data),
                           preferredImageBase=pe.OPTIONAL_HEADER.ImageBase, machine=pe.FILE_HEADER.Machine,
                           imageSize=pe.OPTIONAL_HEADER.SizeOfImage, timeDateStamp=pe.FILE_HEADER.TimeDateStamp,
                           entryRva=pe.OPTIONAL_HEADER.AddressOfEntryPoint,
                           exceptionRva=pe.OPTIONAL_HEADER.DATA_DIRECTORY[3].VirtualAddress,
                           exceptionBytes=pe.OPTIONAL_HEADER.DATA_DIRECTORY[3].Size,
                           tlsRva=pe.OPTIONAL_HEADER.DATA_DIRECTORY[9].VirtualAddress,
                           tlsBytes=pe.OPTIONAL_HEADER.DATA_DIRECTORY[9].Size,
                           delayImportRva=pe.OPTIONAL_HEADER.DATA_DIRECTORY[13].VirtualAddress,
                           delayImportBytes=pe.OPTIONAL_HEADER.DATA_DIRECTORY[13].Size,
                           hashPolicy=2, reserved=0)

    def read(self, rva: int, count: int) -> bytes:
        require(rva >= 0 and count >= 0 and rva + count <= self.pe.OPTIONAL_HEADER.SizeOfImage, "RVA range outside image.")
        headers = self.pe.OPTIONAL_HEADER.SizeOfHeaders
        if rva < headers:
            require(rva + count <= min(headers, len(self.data)), "Header RVA range is not backed by disk bytes.")
            return self.data[rva:rva + count]
        sections = [section for section in self.pe.sections if section.VirtualAddress <= rva
                    and rva + count <= section.VirtualAddress + section.Misc_VirtualSize]
        require(len(sections) == 1, f"RVA range must belong to one section: {self.path.name} {rva:x}+{count:x}")
        section = sections[0]
        offset = rva - section.VirtualAddress
        require(offset + count <= section.SizeOfRawData and section.PointerToRawData + offset + count <= len(self.data),
                "RVA range is not wholly backed by raw file data.")
        start = section.PointerToRawData + offset
        return self.data[start:start + count]

    def protection(self, rva: int, count: int, executable: bool) -> None:
        self.read(rva, count)
        sections = [section for section in self.pe.sections if section.VirtualAddress <= rva
                    and rva + count <= section.VirtualAddress + section.Misc_VirtualSize]
        require(len(sections) == 1 and sections[0].Characteristics & 0xE0000000 == (0x60000000 if executable else 0x40000000),
                f"Expected {'RX' if executable else 'read-only'} section: {self.path.name} {rva:x}")

    def exports(self) -> list:
        require(hasattr(self.pe, "DIRECTORY_ENTRY_EXPORT"), f"Export directory missing: {self.path.name}")
        return list(self.pe.DIRECTORY_ENTRY_EXPORT.symbols)

    def exported(self, symbol: str, executable: bool = True) -> int:
        matches = [entry for entry in self.exports() if entry.name == symbol.encode("ascii")]
        require(len(matches) == 1 and not matches[0].forwarder and matches[0].address, f"Unique direct EAT symbol required: {symbol}")
        rva = int(matches[0].address)
        directory = self.pe.OPTIONAL_HEADER.DATA_DIRECTORY[0]
        require(not directory.VirtualAddress <= rva < directory.VirtualAddress + directory.Size, "Forwarded EAT address is forbidden.")
        self.protection(rva, 1, executable)
        return rva

    def imported(self, module: str, symbols: tuple[str, ...]) -> dict[str, tuple[int, int]]:
        descriptors = [descriptor for descriptor in getattr(self.pe, "DIRECTORY_ENTRY_IMPORT", [])
                       if descriptor.dll.decode("ascii").casefold() == module.casefold()]
        require(len(descriptors) == 1, f"Unique ordinary descriptor required: {self.path.name} -> {module}")
        descriptor = descriptors[0]
        require(descriptor.struct.OriginalFirstThunk and descriptor.struct.FirstThunk, "Both ordinary ILT and IAT are required.")
        names = [entry.name.decode("ascii") if entry.name else None for entry in descriptor.imports]
        require(len(names) == len(symbols) and len(set(names)) == len(names) and set(names) == set(symbols),
                f"Exact named ordinary import set differs: {self.path.name} -> {module}")
        result = {}
        for index, entry in enumerate(descriptor.imports):
            lookup = descriptor.struct.OriginalFirstThunk + index * 8
            iat = descriptor.struct.FirstThunk + index * 8
            require(entry.address - self.pe.OPTIONAL_HEADER.ImageBase == iat, "IAT slot index differs from ordinary ILT.")
            thunk = struct.unpack("<Q", self.read(lookup, 8))[0]
            require(0 < thunk <= 0xFFFFFFFD and not thunk & (1 << 63), "Ordinal/bound import cannot replace ILT name.")
            require(self.read(thunk + 2, len(entry.name) + 1) == entry.name + b"\0", "Actual ILT name bytes differ.")
            self.read(iat, 8)
            result[entry.name.decode("ascii")] = (lookup, iat)
        require(self.read(descriptor.struct.OriginalFirstThunk + len(names) * 8, 8) == bytes(8)
                and self.read(descriptor.struct.FirstThunk + len(names) * 8, 8) == bytes(8), "ILT/IAT terminator is missing.")
        return result

    def unchanged(self) -> None:
        require(self.path.read_bytes() == self.data, f"Input changed while generating profile: {self.path}")


def target_facts(target: Image, source: dict, owned: bool) -> dict:
    record = target.record
    require(record["imageSize"] == 0x1E65000 and record["preferredImageBase"] == 0x180000000
            and record["timeDateStamp"] == 0x68D678AF and record["entryRva"] == FIXED_ENTRY
            and record["exceptionRva"] == 0x1D21000 and record["exceptionBytes"] == 0x112B00,
            "Fixed source PE identity/layout differs.")
    rows = list(struct.iter_unpack("<III", target.read(record["exceptionRva"], record["exceptionBytes"])))
    require(len(rows) == 93760 and all(begin < end <= record["imageSize"] and unwind < record["imageSize"] and unwind
            for begin, end, unwind in rows), "Full source runtime-function table has invalid bounds.")
    begins = [row[0] for row in rows]
    require(all(begins[index] < begins[index + 1] for index in range(len(begins) - 1)), "Source runtime functions must be strictly sorted.")
    verified = []
    for function in source["functions"]:
        actual = target.read(function["beginRva"], function["bodyBytes"])
        target.protection(function["beginRva"], function["bodyBytes"], True)
        require(actual == function["body"] and sha(actual) == function["bodySha256"], f"Full source body differs: {function['name']}")
        overlap = [row for row in rows if row[0] < function["endRva"] and row[1] > function["beginRva"]]
        require(overlap == function["runtimeFunctions"], f"Complete source RF group differs: {function['name']}")
        for row in overlap:
            require(rows[bisect.bisect_left(begins, row[0])] == row, "Source RF lookup differs.")
        verified.append({key: value for key, value in function.items() if key != "body"})
    for rva, expected in source["unwind"].items():
        target.protection(rva, len(expected), False)
        require(target.read(rva, len(expected)) == expected, f"Source unwind bytes differ at {rva:x}.")
    result = dict(functionCount=13, sourceRuntimeFunctionCount=27, fullRuntimeFunctionTableCount=len(rows), functions=verified,
                  unwindCount=len(source["unwind"]), fixedContractSha256=source["fixedContractSha256"])
    if not owned:
        require(record["module"] == "UnityPlayer.dll" and record["sha256"] == TARGET_SHA, "Production target is not the fixed game file.")
        return result
    require(record["module"].casefold() != "unityplayer.dll" and record["sha256"] != TARGET_SHA,
            "Owned compile facts cannot masquerade as the original game module.")
    require(target.read(FIXED_ENTRY, 6) == bytes.fromhex("b801000000c3"), "Owned entry must be the exact private TRUE-return stub.")
    target.protection(FIXED_ENTRY, 6, True)
    unity_main = target.exported("UnityMain2")
    require(unity_main == OWNED_UNITY_MAIN and target.read(unity_main, 3) == bytes.fromhex("31c0c3"), "Owned UnityMain2 stub differs.")
    marker = target.exported(MARKER_NAME, executable=False)
    target.protection(marker, len(MARKER), False)
    require(target.read(marker, len(MARKER)) == MARKER, "Immutable owned marker bytes differ.")
    export_names = [entry.name.decode("ascii") if entry.name else None for entry in target.exports()]
    require(set(export_names) == {"UnityMain2", MARKER_NAME} and len(export_names) == 2
            and target.pe.DIRECTORY_ENTRY_EXPORT.struct.NumberOfFunctions == 2
            and target.pe.DIRECTORY_ENTRY_EXPORT.struct.NumberOfNames == 2, "Owned target export surface differs.")
    require(hasattr(target.pe, "DIRECTORY_ENTRY_TLS") and record["tlsRva"] and record["tlsBytes"] == 40, "Owned target must declare its own TLS directory.")
    tls = target.pe.DIRECTORY_ENTRY_TLS.struct
    require(tls.StartAddressOfRawData == tls.EndAddressOfRawData == tls.SizeOfZeroFill == tls.Characteristics == 0,
            "Owned TLS must not borrow a game template or unproved zero-fill policy.")
    index_va = tls.AddressOfIndex
    require(index_va >= record["preferredImageBase"], "Owned TLS index VA is invalid.")
    index_rva = index_va - record["preferredImageBase"]
    target.read(index_rva, 4)
    index_sections = [section for section in target.pe.sections if section.VirtualAddress <= index_rva
                      and index_rva + 4 <= section.VirtualAddress + section.Misc_VirtualSize]
    require(len(index_sections) == 1 and index_sections[0].Characteristics & 0xE0000000 == 0xC0000000,
            "Owned loader TLS index must belong to its own non-executable writable section.")
    callbacks_va = tls.AddressOfCallBacks
    require(callbacks_va >= record["preferredImageBase"], "Owned TLS callback array VA is invalid.")
    callback_array = callbacks_va - record["preferredImageBase"]
    actual_callbacks = struct.unpack("<QQQ", target.read(callback_array, 24))
    require(actual_callbacks == tuple(record["preferredImageBase"] + rva for rva in OWNED_TLS_CALLBACKS) + (0,),
            "Owned target must have exactly its two fixed private TLS callbacks.")
    for rva in OWNED_TLS_CALLBACKS:
        target.protection(rva, 1, True)
        require(target.read(rva, 1) == b"\xc3", "Owned TLS callback is not the exact private RET stub.")
    require(target.pe.OPTIONAL_HEADER.DATA_DIRECTORY[5].VirtualAddress and target.pe.OPTIONAL_HEADER.DATA_DIRECTORY[5].Size,
            "Owned TLS addresses require an actual relocation directory.")
    relocations = {(entry.rva, entry.type) for block in getattr(target.pe, "DIRECTORY_ENTRY_BASERELOC", []) for entry in block.entries}
    require((callback_array, 10) in relocations and (callback_array + 8, 10) in relocations
            and (record["tlsRva"] + 16, 10) in relocations and (record["tlsRva"] + 24, 10) in relocations,
            "Owned TLS VA pointers must have DIR64 relocation entries.")
    stub_functions = []
    for rva in (*OWNED_TLS_CALLBACKS, FIXED_ENTRY, unity_main):
        index = bisect.bisect_right(begins, rva) - 1
        require(index >= 0 and rows[index][0] <= rva < rows[index][1], "Owned stub must have an explicit runtime-function owner.")
        owner = rows[index]
        require(target.read(owner[2], 4) == bytes.fromhex("01000000"), "Owned stub runtime function requires its own leaf unwind.")
        stub_functions.append(dict(pc=rva, tableIndex=index, beginRva=owner[0], endRva=owner[1], unwindRva=owner[2]))
    require(all(not (function["beginRva"] <= rva < function["endRva"]) for function in source["functions"]
                for rva in (*OWNED_TLS_CALLBACKS, FIXED_ENTRY, unity_main, marker)), "Owned marker/stubs overlap fixed source bodies.")
    result.update(ownedMarkerRva=marker, ownedMarkerHex=MARKER.hex(), ownedEntryRva=FIXED_ENTRY, ownedUnityMainRva=unity_main,
                  ownedTlsCallbacks=list(OWNED_TLS_CALLBACKS), ownedStubRuntimeFunctions=stub_functions,
                  ownedStubsExecuted=False, sourceImageLoaded=False)
    return result


def generated_header(images: list[dict], edges: list[dict], profile: int, marker: int,
                     admission: dict | None = None) -> str:
    admitted = admission is not None and admission.get("authorized", False) and profile == 2
    fixed_sha = admission["fixedContractSha256"] if admission else ("00" * 32)
    closure_sha = admission["interiorClosureSha256"] if admitted else ("00" * 32)
    trace_sha = admission["coldTraceSha256"] if admitted else ("00" * 32)
    note = ("// Stage A verified interior-entry closure and write-free cold-start admission enabled."
            if admitted else "// Actual disk facts only. Fixed-game first-write admission remains unavailable.")
    lines = ["#pragma once", '#include "EngineTransportCompileContract.h"', note,
             "namespace RizEngineTransportGeneratedProfile {", "using namespace RizEngineTransportCompileContract;",
             f"inline constexpr std::uint32_t Version=1,Profile={profile};",
             f"inline constexpr bool InteriorEntryClosureVerified={'true' if admitted else 'false'};",
             f"inline constexpr bool ProductionColdTraceVerified={'true' if admitted else 'false'};",
             f"inline constexpr bool ProductionAdmissionVerified={'true' if admitted else 'false'};",
             f"inline constexpr bool ProductionFirstInstallAuthorized={'true' if admitted else 'false'};",
             f'inline constexpr char FixedContractSha256[]="{fixed_sha}";',
             f'inline constexpr char InteriorClosureSha256[]="{closure_sha}";',
             f'inline constexpr char ColdTraceSha256[]="{trace_sha}";',
             "inline constexpr ImageIdentity Images[5]{"]
    for row in images:
        module = json.dumps(row["module"], ensure_ascii=True)
        values = ",".join(f"0x{value:02x}" for value in bytes.fromhex(row["sha256"]))
        lines.append(f'    {{L{module},{row["fileBytes"]}ull,0x{row["imageSize"]:x},0x{row["timeDateStamp"]:x},'
                     f'0x{row["entryRva"]:x},0x{row["exceptionRva"]:x},{{{values}}},{row["hashPolicy"]},0}},')
    lines.extend(["};", "inline constexpr ImportEdge Edges[44]{"])
    for edge in edges:
        lines.append('    {%d,%d,"%s",0x%x,0x%x,0x%x},' % tuple(edge[key] for key in ("source", "destination", "symbol", "lookupRva", "iatRva", "exportRva")))
    lines.extend(["};", f"inline constexpr std::uint32_t OwnedMarkerRva=0x{marker:x};",
                  "inline constexpr unsigned char OwnedMarker[16]{" + ",".join(f"0x{value:02x}" for value in (MARKER if profile == 1 else bytes(16))) + "};", "}", ""])
    return "\n".join(lines)


def verify_stage_a_admission(interior_path: Path, cold_path: Path, source: dict) -> dict:
    admission_raw = ADMISSION_FACTS.read_bytes()
    admission_text = admission_raw.decode("utf-8-sig")
    def read_str(name: str) -> str:
        m = re.search(rf'inline constexpr char {name}\[\]\s*=\s*"([0-9a-f]{{64}})";', admission_text)
        require(m is not None, f"Missing {name} in EngineTransportAdmissionFacts.h")
        return m.group(1)
    expected_facts = read_str("ExpectedFactsSha256")
    expected_closure = read_str("InteriorClosureSha256")
    expected_trace = read_str("ColdTraceSha256")
    require(expected_facts == source["fixedContractSha256"], "Admission facts ExpectedFactsSha256 differs from FixedContractSha256.")
    require("InteriorEntryClosureVerified = true;" in admission_text
            and "ProductionColdTraceVerified = true;" in admission_text
            and "ProductionAdmissionVerified = true;" in admission_text,
            "EngineTransportAdmissionFacts.h has unverified Stage A admission flags.")

    ic_path = absolute_file(interior_path)
    ct_path = absolute_file(cold_path)
    ic_bytes = ic_path.read_bytes()
    ct_bytes = ct_path.read_bytes()
    require(sha(ic_bytes) == expected_closure, f"interior-closure.json SHA256 mismatch: {sha(ic_bytes)} != {expected_closure}")
    require(sha(ct_bytes) == expected_trace, f"cold-trace.json SHA256 mismatch: {sha(ct_bytes)} != {expected_trace}")

    ic = json.loads(ic_bytes)
    require(ic.get("kind") == "stage-a-interior-entry-closure"
            and ic.get("interiorEntryClosureVerified") is True
            and ic.get("unitySha256") == TARGET_SHA
            and ic.get("factsSha256") == source["fixedContractSha256"]
            and ic.get("pdataRuntimeFunctionCount") == 93760
            and ic.get("relocDir64Count") == 59494
            and all(not ic.get(k) for k in (
                "externalInteriorBranches", "rawInteriorHits", "ripRelToInterior",
                "relocToInterior", "pdataInteriorHandlers", "pdataScopeTargets",
                "eatInteriorHits", "crtInteriorHits")),
            "Stage A interior-entry closure evidence failed semantic verification.")

    ct = json.loads(ct_bytes)
    tid0 = ct.get("tid0")
    bp = ct.get("bootstrapPub") or {}
    dr = ct.get("doorstopRec") or {}
    require(ct.get("kind") == "stage-a-write-free-cold-trace"
            and ct.get("edgesVerified") is True and ct.get("edgesVerifiedCount") == 44
            and ct.get("unityMain2Hit") is True
            and ct.get("preUnityMain2PageFaultCount") == 0 and ct.get("pageFaults") == []
            and isinstance(tid0, int) and tid0 > 0
            and ct.get("bootstrapPubCaptured") is True and ct.get("doorstopRecCaptured") is True
            and bp.get("startupStatic") == 1 and bp.get("attachReason") == 1
            and bp.get("ownerThread") == tid0 and bp.get("producerThread") == tid0
            and dr.get("threadId") == tid0,
            "Stage A write-free cold-start trace failed semantic verification.")

    events = ct.get("events") or []
    seq_by_kind = {}
    for ev in events:
        k = ev.get("kind")
        if k and k not in seq_by_kind:
            seq_by_kind[k] = (ev.get("seq", -1), ev.get("tid", -1))
    order = ("bootstrap_entry", "bootstrap_return", "doorstop_entry", "doorstop_return",
             "unity_tls0", "unity_tls1", "unity_entry", "rizline_entry", "unity_main2")
    require(all(name in seq_by_kind for name in order), "Stage A cold trace missing mandatory lifecycle ordering events.")
    for idx in range(len(order) - 1):
        require(seq_by_kind[order[idx]][0] < seq_by_kind[order[idx + 1]][0],
                f"Stage A cold-trace sequence violated between {order[idx]} and {order[idx + 1]}.")
    require(all(seq_by_kind[name][1] == tid0 for name in order), "Stage A cold-trace lifecycle events ran on mismatched threads.")
    return {
        "authorized": True,
        "fixedContractSha256": source["fixedContractSha256"],
        "interiorClosureSha256": expected_closure,
        "coldTraceSha256": expected_trace,
        "admissionFactsPath": str(ADMISSION_FACTS),
        "admissionFactsBytes": admission_raw,
        "interiorClosurePath": str(ic_path),
        "interiorClosureBytes": ic_bytes,
        "coldTracePath": str(ct_path),
        "coldTraceBytes": ct_bytes,
    }


def write_new(path: Path, data: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("xb") as output:
        output.write(data)


def main() -> None:
    generator_path = Path(__file__).resolve()
    generator_bytes = generator_path.read_bytes()
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("target", "executable", "doorstop", "kernel"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--profile", choices=("owned", "production"), required=True)
    parser.add_argument("--output-header", type=Path, required=True)
    parser.add_argument("--output-json", type=Path, required=True)
    parser.add_argument("--interior-closure", type=Path, default=None)
    parser.add_argument("--cold-trace", type=Path, default=None)
    args = parser.parse_args()
    header_path, json_path = absolute_output(args.output_header), absolute_output(args.output_json)
    require(header_path != json_path and header_path.name == "EngineTransportProfile.Generated.h", "Use the exact native include basename and a distinct JSON path.")
    source = facts()
    owned = args.profile == "owned"
    admission = {"authorized": False, "fixedContractSha256": source["fixedContractSha256"],
                 "interiorClosureSha256": "00" * 32, "coldTraceSha256": "00" * 32}
    if (args.interior_closure is not None) != (args.cold_trace is not None):
        raise ValueError("Provide both --interior-closure and --cold-trace, or neither.")
    if args.interior_closure is not None:
        require(not owned, "Owned profile can never authorize production admission.")
        admission = verify_stage_a_admission(args.interior_closure, args.cold_trace, source)
    target, executable, doorstop, kernel = (Image(getattr(args, name)) for name in ("target", "executable", "doorstop", "kernel"))
    images = [target, executable, doorstop, kernel]
    require(len({str(image.path).casefold() for image in images}) == 4
            and len({image.path.name.casefold() for image in images}) == 4, "Four distinct module paths and basenames are required.")
    require(doorstop.path.name == "winhttp.dll" and kernel.path.name == KERNEL, "Kernel/Doorstop basenames differ from native contract.")
    require(bool(executable.pe.FILE_HEADER.Characteristics & 0x2000) is False
            and all(image.pe.FILE_HEADER.Characteristics & 0x2000 for image in (target, doorstop, kernel)), "EXE/DLL file roles differ.")
    require(all(image.record["delayImportRva"] == image.record["delayImportBytes"] == 0 for image in images), "Delay imports cannot supplement the ordinary graph.")
    require(kernel.record["entryRva"] == kernel.record["tlsRva"] == kernel.record["tlsBytes"] == 0, "Kernel must have no entry point or TLS.")
    kernel_exports = kernel.exports()
    require(len(kernel_exports) == 35 and {entry.name.decode("ascii") if entry.name else None for entry in kernel_exports} == set(KERNEL_NAMES)
            and kernel.pe.DIRECTORY_ENTRY_EXPORT.struct.NumberOfNames == kernel.pe.DIRECTORY_ENTRY_EXPORT.struct.NumberOfFunctions == 35,
            "Exact thirty-five-entry Kernel EAT is required.")
    require(len(doorstop.exports()) == 82 and doorstop.pe.DIRECTORY_ENTRY_EXPORT.struct.NumberOfNames == 82
            and doorstop.pe.DIRECTORY_ENTRY_EXPORT.struct.NumberOfFunctions == 82, "Doorstop must preserve the 82 public WinHTTP exports.")
    for symbol in KERNEL_NAMES:
        kernel.exported(symbol)
    target_result = target_facts(target, source, owned)
    if owned:
        require(executable.path.name.casefold() != "rizline.exe" and executable.record["sha256"] != EXECUTABLE_SHA,
                "Owned executable cannot masquerade as the fixed game.")
        require({descriptor.dll.decode("ascii").casefold() for descriptor in getattr(target.pe, "DIRECTORY_ENTRY_IMPORT", [])} == {"winhttp.dll"},
                "Owned target can import only its private WinHTTP chain.")
    else:
        require(executable.path.name == "Rizline.exe" and executable.record["sha256"] == EXECUTABLE_SHA,
                "Production executable differs from the pinned fixed game.")
    edges = []

    def add_group(source_index: int, destination_index: int, symbols: tuple[str, ...], source_image: Image | None, destination_image: Image | None, destination_name: str) -> None:
        imports = source_image.imported(destination_name, symbols) if source_image else {}
        for symbol in symbols:
            lookup, iat = imports[symbol] if source_image else (0, 0)
            export_rva = destination_image.exported(symbol) if destination_image else 0
            edges.append(dict(index=len(edges), source=source_index, destination=destination_index, symbol=symbol,
                              lookupRva=lookup, iatRva=iat, exportRva=export_rva))

    add_group(3, 2, ("UnityMain2",), executable, target, target.path.name)
    add_group(2, 4, WINHTTP, target, doorstop, doorstop.path.name)
    add_group(4, 0, BOOTSTRAP_NAMES, doorstop, None, BOOTSTRAP)
    add_group(0, 1, KERNEL_NAMES, None, kernel, KERNEL)
    require(len(edges) == 44 and tuple(edge["symbol"] for edge in edges) == SYMBOLS, "Exact forty-four-edge ordering differs.")
    payload = b"".join(struct.pack("<IIIII", *(edge[key] for key in ("source", "destination", "lookupRva", "iatRva", "exportRva")))
                       + edge["symbol"].encode("ascii") + b"\0" for edge in edges)
    zero_self = dict(module=BOOTSTRAP, fileBytes=0, imageSize=0, timeDateStamp=0, entryRva=0, exceptionRva=0,
                     sha256="00" * 32, hashPolicy=1, reserved=0)
    records = [zero_self, kernel.record, target.record, executable.record, doorstop.record]
    header = generated_header(records, edges, 1 if owned else 2, target_result.get("ownedMarkerRva", 0), admission).encode("utf-8")
    for image in images:
        image.unchanged()
    require(FACTS.read_bytes() == source["sourceBytes"] and CONTRACT.read_bytes() == source["contractBytes"], "Native source facts changed during generation.")
    require(generator_path.read_bytes() == generator_bytes, "The generator changed during generation.")
    provenance_items = [(generator_path, generator_bytes), (FACTS, source["sourceBytes"]), (CONTRACT, source["contractBytes"])]
    if admission["authorized"]:
        provenance_items.extend([
            (Path(admission["admissionFactsPath"]), admission["admissionFactsBytes"]),
            (Path(admission["interiorClosurePath"]), admission["interiorClosureBytes"]),
            (Path(admission["coldTracePath"]), admission["coldTraceBytes"]),
        ])
    provenance = [dict(path=str(path), sha256=sha(data), bytes=len(data)) for path, data in provenance_items]
    admitted = bool(admission["authorized"])
    result = dict(schemaVersion=1, kind="engine-transport-native-compile-facts", profile=args.profile, nativeProfile=1 if owned else 2,
                  sourceHashesUnchanged=True, images=records, edges=edges, targetFacts=target_result, sources=provenance,
                  generatedHeader=dict(path=str(header_path), sha256=sha(header), bytes=len(header)),
                  dependencyGraphDigest=dict(sha256=sha(payload), payloadBytes=len(payload),
                    encoding="44 rows: source,destination,lookupRva,iatRva,exportRva as five little-endian uint32, followed by ASCII symbol including NUL",
                    scope="compile-facts-with-self-RVAs-zero", runtimeGraphDigestComplete=False,
                    runtimeUnknownEdges=list(range(5, 44)),
                    runtimeRule="Replace Bootstrap-owned unknown ILT/IAT/EAT RVAs with unique actual ordinary mappings before computing native publication digest."),
                  interiorEntryClosureVerified=admitted,
                  productionColdTraceVerified=admitted,
                  fixedContractSha256=admission["fixedContractSha256"],
                  interiorClosureSha256=admission["interiorClosureSha256"],
                  coldTraceSha256=admission["coldTraceSha256"],
                  productionAdmissionGranted=admitted, executionWindowProved=admitted, nativePublicationReadyProved=False,
                  kernelConfigured=False, sourcePatched=False, targetImagesLoaded=False, gameExecuted=False, installed=False)
    # Check both output paths and all inputs before creating either artifact.
    require(not header_path.exists() and not json_path.exists(), "Output appeared while inputs were being checked.")
    write_new(header_path, header)
    write_new(json_path, (json.dumps(result, indent=2, ensure_ascii=False) + "\n").encode("utf-8"))
    print(json.dumps(dict(kind=result["kind"], profile=args.profile, images=5, edges=44, functions=13, sourceRuntimeFunctions=27,
                          generatedHeaderSha256=sha(header), runtimeGraphDigestComplete=False,
                          interiorEntryClosureVerified=admitted, productionColdTraceVerified=admitted,
                          productionAdmissionGranted=admitted)))


if __name__ == "__main__":
    main()
