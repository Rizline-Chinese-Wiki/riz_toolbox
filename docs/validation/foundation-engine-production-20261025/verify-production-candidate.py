"""Audit a completed fixed production candidate using disk bytes only.

No image is loaded and no build, game, child process or native export is run.
ILT/IAT fields below are original file fields, never a runtime IAT observation.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import struct
import sys
from pathlib import Path

import pefile


GAME_HASHES = {
    "Rizline.exe": "f9c37f1078619d02c62a7a5815ca48179666efb2dfe7e6e698bfff7858c26e5e",
    "UnityPlayer.dll": "43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1",
    "GameAssembly.dll": "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79",
}
KERNEL_HASH = "923486d2901ab6f66ea6d90d99cc65b1afafbc3275c338f0fd71ea5d22bc5662"
MODULES = ("RizToolbox.EngineBootstrap.dll", "RizToolbox.EngineNodeKernel.dll",
           "UnityPlayer.dll", "Rizline.exe", "winhttp.dll")
BOOTSTRAP_NAMES = (
    "RizEngineFirstInstall_GetVersion", "RizEngineFirstInstall_GetInfoV4",
    "RizEngineFirstInstall_VerifyV4", "RizEngineFirstInstall_AbortAfterPublication",
)
KERNEL_NAMES = (
    "RizEngineNodeContext_GetAbiVersion", "RizEngineNodeContext_GetLimit",
    "RizEngineNodeContext_GetFrameSize", "RizEngineNodeContext_GetCompletionSize",
    "RizEngineNodeContext_GetEntry", "RizEngineNodeContext_Configure",
    "RizEngineNodeContext_Attach", "RizEngineNodeContext_GetInfo",
)
WINHTTP_NAMES = ("WinHttpOpen", "WinHttpGetProxyForUrl",
                "WinHttpGetIEProxyConfigForCurrentUser", "WinHttpCloseHandle")
CANONICAL = ((3, 2, "UnityMain2"),
             *((2, 4, name) for name in WINHTTP_NAMES),
             *((4, 0, name) for name in BOOTSTRAP_NAMES),
             *((0, 1, name) for name in KERNEL_NAMES))
IDENTITY_FIELDS = ("module", "sha256", "fileBytes", "preferredImageBase", "machine", "imageSize",
                   "timeDateStamp", "entryRva", "exceptionRva", "exceptionBytes", "tlsRva", "tlsBytes",
                   "delayImportRva", "delayImportBytes", "ordinaryImports")
DOORSTOP_PRIVATE = ("DllEntry", "RizEngineDoorstopRecord", "RizEngineDoorstopBeforeAttach",
                    "RizEngineDoorstopBeforeIat", "RizEngineDoorstopAfterAttach",
                    "RizEngineDoorstopOnFailure", "RizEngineDoorstopOnDetach",
                    "RizWinHttpDoorstopRecord", "RizWinHttpDoorstopAttach", "RizWinHttpForward_GetInfo")
BOOTSTRAP_PRIVATE = ("RizEngineBootstrapEntry", "RizEnginePublicationRecord", "RizEngineContinuation")


def sha(data):
    return hashlib.sha256(data).hexdigest()


class AuditError(ValueError):
    pass


class Audit:
    def __init__(self):
        self.checks = 0
        self.files = {}
        self.result = dict(kind="fixed-production-candidate-disk-audit", schemaVersion=1, passed=False,
                           runtimeIatObserved=False, targetImagesLoaded=False, targetProcessesStarted=0,
                           executionWindowProved=False, productionAdmissionGranted=False,
                           productionAdmissionProved=False,
                           limitations=[
                               "This audit reads existing files; it does not independently rebuild or execute the candidate.",
                               "Disk ILT/IAT/EAT and section flags do not prove mapped addresses, page protections, loader callback order or first-execution history.",
                               "All declared source hashes are checked; this is not an inferred complete transitive build-input inventory.",
                               "Input bytes are cached and rehashed before success; the several files are not an atomic filesystem snapshot.",
                               "Static identities and dependency edges never authorize the game's first source write.",
                           ])

    def check(self, value, message):
        self.checks += 1
        if not value:
            raise AuditError(message)

    def read(self, path, role):
        path = Path(path)
        self.check(path.is_absolute() and path.is_file() and not path.is_symlink(),
                   "Absolute ordinary input file required: " + str(path))
        path = path.resolve()
        key = str(path).casefold()
        if key not in self.files:
            data = path.read_bytes()
            self.check(len(data) <= 256 * 1024 * 1024, "Input size is out of bounds: " + str(path))
            self.files[key] = dict(path=str(path), sha256=sha(data), fileBytes=len(data), roles=[], data=data)
        record = self.files[key]
        if role not in record["roles"]:
            record["roles"].append(role)
        return record["data"]

    def json(self, path, role):
        return json.loads(self.read(path, role).decode("utf-8-sig"))

    def declared(self, rows, role):
        self.check(isinstance(rows, list) and 0 < len(rows) <= 8192, "Invalid declared input list: " + role)
        seen = set()
        for row in rows:
            self.check(isinstance(row, dict) and isinstance(row.get("path"), str), "Invalid declared path: " + role)
            key = str(Path(row["path"]).resolve()).casefold()
            self.check(key not in seen, "Duplicate declared input: " + row["path"])
            seen.add(key)
            data = self.read(row["path"], role)
            self.check(re.fullmatch(r"[0-9a-f]{64}", row.get("sha256", "")) is not None
                       and sha(data) == row["sha256"], "Declared SHA256 differs: " + row["path"])
        return seen

    def linked(self, reference, expected_path, role):
        self.check(Path(reference["path"]).is_absolute()
                   and Path(reference["path"]).resolve() == expected_path.resolve(), "Profile link path differs: " + role)
        self.check(sha(self.read(expected_path, role)) == reference["sha256"], "Profile link SHA256 differs: " + role)

    def recheck(self):
        for record in self.files.values():
            data = Path(record["path"]).read_bytes()
            self.check(len(data) == record["fileBytes"] and sha(data) == record["sha256"],
                       "An input changed during disk audit: " + record["path"])

    def finish(self):
        self.result["checks"] = self.checks
        self.result["inputs"] = [{key: value for key, value in row.items() if key != "data"}
                                 for row in self.files.values()]
        return self.result


class Image:
    def __init__(self, audit, path, expected_hash=None):
        self.audit, self.path = audit, Path(path).resolve()
        self.data = audit.read(path, "PE image")
        if expected_hash is not None:
            audit.check(sha(self.data) == expected_hash, "Fixed image fingerprint differs: " + str(path))
        self.pe = pefile.PE(data=self.data, fast_load=True)
        self.pe.parse_data_directories(directories=[0, 1, 9, 13])
        pe = self.pe
        audit.check(pe.FILE_HEADER.Machine == 0x8664 and pe.OPTIONAL_HEADER.Magic == 0x20b
                    and pe.FILE_HEADER.SizeOfOptionalHeader == 240
                    and pe.OPTIONAL_HEADER.NumberOfRvaAndSizes == 16, "AMD64 PE32+ header differs: " + str(path))
        audit.check(0 < pe.OPTIONAL_HEADER.SizeOfImage <= 0x20000000, "PE image size is out of bounds")
        imports = getattr(pe, "DIRECTORY_ENTRY_IMPORT", [])
        audit.check(len(imports) <= 256 and all(len(d.imports) <= 8192 for d in imports), "Import inventory bound exceeded")
        audit.check(len({d.dll.decode("ascii").casefold() for d in imports}) == len(imports), "Duplicate ordinary import module")
        self.facts = dict(module=self.path.name, sha256=sha(self.data), fileBytes=len(self.data),
                          preferredImageBase=pe.OPTIONAL_HEADER.ImageBase, machine=pe.FILE_HEADER.Machine,
                          imageSize=pe.OPTIONAL_HEADER.SizeOfImage, timeDateStamp=pe.FILE_HEADER.TimeDateStamp,
                          entryRva=pe.OPTIONAL_HEADER.AddressOfEntryPoint,
                          exceptionRva=pe.OPTIONAL_HEADER.DATA_DIRECTORY[3].VirtualAddress,
                          exceptionBytes=pe.OPTIONAL_HEADER.DATA_DIRECTORY[3].Size,
                          tlsRva=pe.OPTIONAL_HEADER.DATA_DIRECTORY[9].VirtualAddress,
                          tlsBytes=pe.OPTIONAL_HEADER.DATA_DIRECTORY[9].Size,
                          delayImportRva=pe.OPTIONAL_HEADER.DATA_DIRECTORY[13].VirtualAddress,
                          delayImportBytes=pe.OPTIONAL_HEADER.DATA_DIRECTORY[13].Size,
                          ordinaryImports=[dict(module=d.dll.decode("ascii"), symbols=[
                              value.name.decode("ascii") if value.name else "#" + str(value.ordinal)
                              for value in d.imports]) for d in imports])

    def bytes_at(self, rva, size):
        self.audit.check(isinstance(rva, int) and isinstance(size, int) and 0 < rva < self.facts["imageSize"]
                         and 0 < size <= self.facts["imageSize"] - rva, "RVA span is outside image: " + str(self.path))
        offset = self.pe.get_offset_from_rva(rva)
        self.audit.check(0 <= offset < len(self.data) and size <= len(self.data) - offset,
                         "RVA span has no original file bytes")
        data = self.pe.get_data(rva, size)
        self.audit.check(len(data) == size and data == self.data[offset:offset + size], "PE raw span differs")
        return data

    def code(self, rva, size=1):
        section = self.pe.get_section_by_rva(rva)
        self.audit.check(section is not None and section.Characteristics & 0xe0000000 == 0x60000000
                         and size <= max(section.Misc_VirtualSize, section.SizeOfRawData) - (rva - section.VirtualAddress),
                         "Expected executable/read-only file section")
        self.bytes_at(rva, size)

    def match(self, expected):
        self.audit.check(Path(expected["path"]).is_absolute() and Path(expected["path"]).resolve() == self.path,
                         "Profile image path differs")
        for name in IDENTITY_FIELDS:
            self.audit.check(expected.get(name) == self.facts[name], "Profile image field differs: " + self.path.name + "." + name)

    def exports(self):
        entries = getattr(getattr(self.pe, "DIRECTORY_ENTRY_EXPORT", None), "symbols", [])
        self.audit.check(0 < len(entries) <= 8192, "Export inventory missing or too large")
        self.audit.check(all(entry.name for entry in entries), "Unnamed export is outside this fixed ABI")
        pairs = [(entry.name.decode("ascii"), entry.ordinal) for entry in entries]
        self.audit.check(len(set(name for name, _ in pairs)) == len(pairs)
                         and len(set(ordinal for _, ordinal in pairs)) == len(pairs), "Duplicate public export name/ordinal")
        return sorted(pairs)

    def export(self, symbol):
        entries = getattr(getattr(self.pe, "DIRECTORY_ENTRY_EXPORT", None), "symbols", [])
        matches = [entry for entry in entries if entry.name == symbol.encode("ascii")]
        self.audit.check(len(matches) == 1 and not matches[0].forwarder and matches[0].address,
                         "Unique non-forwarded EAT symbol required: " + symbol)
        rva = matches[0].address
        directory = self.pe.OPTIONAL_HEADER.DATA_DIRECTORY[0]
        self.audit.check(not directory.VirtualAddress <= rva < directory.VirtualAddress + directory.Size,
                         "EAT target points into forwarding data")
        self.code(rva)
        return rva

    def imported(self, module, symbol):
        matches = [d for d in getattr(self.pe, "DIRECTORY_ENTRY_IMPORT", [])
                   if d.dll.decode("ascii").casefold() == module.casefold()]
        self.audit.check(len(matches) == 1 and matches[0].struct.OriginalFirstThunk and matches[0].struct.FirstThunk,
                         "Unique ordinary ILT/IAT descriptor required: " + module)
        descriptor = matches[0]
        selected = [(index, value) for index, value in enumerate(descriptor.imports) if value.name == symbol.encode("ascii")]
        self.audit.check(len(selected) == 1, "Unique ordinary named import required: " + symbol)
        index, value = selected[0]
        lookup = descriptor.struct.OriginalFirstThunk + index * 8
        iat = descriptor.struct.FirstThunk + index * 8
        self.audit.check(value.address - self.facts["preferredImageBase"] == iat, "Disk ILT/IAT slot association differs")
        lookup_value, = struct.unpack("<Q", self.bytes_at(lookup, 8))
        iat_value, = struct.unpack("<Q", self.bytes_at(iat, 8))
        self.audit.check(0 < lookup_value <= 0xfffffffd and not lookup_value & (1 << 63), "ILT entry is not a named import RVA")
        actual_name = self.bytes_at(lookup_value + 2, len(symbol) + 1)
        self.audit.check(actual_name == symbol.encode("ascii") + b"\0", "Raw ILT import name differs")
        self.audit.check(iat_value == lookup_value, "Expected original unbound disk IAT entry")
        section = self.pe.get_section_by_rva(iat)
        self.audit.check(section is not None and bool(section.Characteristics & 0x40000000)
                         and not section.Characteristics & 0x20000000, "Disk IAT slot section must be readable and non-executable")
        return dict(lookupRva=lookup, iatRva=iat, diskLookupValue=lookup_value, diskIatValue=iat_value,
                    iatSection=section.Name.rstrip(b"\0").decode("ascii"), iatSectionCharacteristics=section.Characteristics)

    def tls(self):
        rva, size = self.facts["tlsRva"], self.facts["tlsBytes"]
        self.audit.check(bool(rva) == bool(size), "Incomplete TLS directory pair")
        if not rva:
            return dict(directoryRva=0, directoryBytes=0, originalDirectoryHex="", callbacks=[])
        self.audit.check(size == 40 and hasattr(self.pe, "DIRECTORY_ENTRY_TLS"), "Expected bounded PE32+ TLS directory")
        raw = self.bytes_at(rva, size)
        start, end, index, callbacks, zero_fill, characteristics = struct.unpack("<QQQQII", raw)
        self.audit.check(end >= start and callbacks >= self.facts["preferredImageBase"], "TLS original preferred VA fields differ")
        callback_rva = callbacks - self.facts["preferredImageBase"]
        values, table = [], bytearray()
        for slot in range(65):
            value_raw = self.bytes_at(callback_rva + slot * 8, 8)
            table += value_raw
            value, = struct.unpack("<Q", value_raw)
            if value == 0:
                break
            self.audit.check(slot < 64 and value >= self.facts["preferredImageBase"], "TLS callback array bound/VA differs")
            target_rva = value - self.facts["preferredImageBase"]
            self.code(target_rva)
            values.append(dict(originalPreferredVa=value, rva=target_rva))
        else:
            raise AuditError("TLS callback array lacks bounded terminator")
        return dict(directoryRva=rva, directoryBytes=size, originalDirectoryHex=raw.hex(), directorySha256=sha(raw),
                    startRawDataPreferredVa=start, endRawDataPreferredVa=end, addressOfIndexPreferredVa=index,
                    addressOfCallbacksPreferredVa=callbacks, callbackArrayRva=callback_rva,
                    originalCallbackArrayHex=table.hex(), callbackArraySha256=sha(table), callbacks=values,
                    sizeOfZeroFill=zero_fill, characteristics=characteristics)


def number(token):
    return int(re.sub(r"(?i)(ull|ul|u|ll|l)$", "", token.strip()), 0)


def check_header(audit, text, images, edges):
    audit.check(text.count("namespace RizEngineBootstrapProductionProfile") == 1, "Generated native namespace differs")
    audit.check("ProductionFirstInstallAuthorized" not in text, "A generated profile cannot define execution-window permission")
    for name, image in (("Kernel", images[1]), ("Doorstop", images[4])):
        arrays = re.findall(r"inline\s+constexpr\s+unsigned\s+char\s+" + name + r"Sha256\[\]\s*=\s*\{([^}]+)\};", text)
        audit.check(len(arrays) == 1, "Generated native hash array differs: " + name)
        raw = bytes(number(token) for token in arrays[0].split(",") if token.strip())
        audit.check(len(raw) == 32 and raw.hex() == image.facts["sha256"], "Generated native SHA differs: " + name)
        names = re.findall(r"inline\s+constexpr\s+wchar_t\s+" + name + r'Module\[\]\s*=\s*L"([^"]+)";', text)
        audit.check(names == [image.facts["module"]], "Generated native module name differs: " + name)
        records = re.findall(r"inline\s+constexpr\s+ImageIdentity\s+" + name + r"\{([^}]+)\};", text)
        audit.check(len(records) == 1, "Generated native image constant differs: " + name)
        fields = [value.strip() for value in records[0].split(",")]
        audit.check(len(fields) == 10 and fields[0] == name + "Module" and fields[-1] == name + "Sha256", "Generated image constant shape differs")
        expected = [image.facts[field] for field in ("fileBytes", "preferredImageBase", "machine", "imageSize", "timeDateStamp", "entryRva", "exceptionRva", "exceptionBytes")]
        audit.check([number(value) for value in fields[1:-1]] == expected, "Generated image numeric facts differ")
    tables = re.findall(r"inline\s+constexpr\s+ImportEdge\s+Edges\[17\]\s*=\s*\{(.*?)\};", text, re.S)
    audit.check(len(tables) == 1, "Generated native edge table differs")
    rows = re.findall(r'\{\s*(\d+)\s*,\s*(\d+)\s*,\s*"([A-Za-z_0-9]+)"\s*,\s*(0x[0-9a-fA-F]+|\d+)\s*,\s*(0x[0-9a-fA-F]+|\d+)\s*,\s*(0x[0-9a-fA-F]+|\d+)\s*\}', tables[0])
    parsed = [dict(source=int(row[0]), destination=int(row[1]), symbol=row[2],
                   lookupRva=number(row[3]), iatRva=number(row[4]), exportRva=number(row[5])) for row in rows]
    audit.check(len(parsed) == 17 and parsed == edges, "Generated native edge facts differ from JSON")


def check_map(audit, path, image, symbols, record_sizes, entry_symbol):
    text = audit.read(path, "private linker MAP").decode("utf-8-sig", errors="strict")
    public = {name for name, _ in image.exports()}
    output = {}
    for symbol in symbols:
        matches = re.findall(r"(?m)^\s*[0-9a-fA-F]+:[0-9a-fA-F]+\s+" + re.escape(symbol) + r"\s+([0-9a-fA-F]{16})\b", text)
        audit.check(len(matches) == 1 and symbol not in public, "Private MAP symbol missing, ambiguous or exported: " + symbol)
        rva = int(matches[0], 16) - image.facts["preferredImageBase"]
        size = record_sizes.get(symbol, 1)
        section = image.pe.get_section_by_rva(rva)
        audit.check(0 < rva < image.facts["imageSize"] and section is not None
                    and size <= max(section.Misc_VirtualSize, section.SizeOfRawData) - (rva - section.VirtualAddress), "MAP symbol exceeds its image section")
        if symbol in record_sizes:
            audit.check(section.Characteristics & 0xc0000000 == 0xc0000000, "Private record must be in writable/readable image data")
        else:
            image.code(rva)
        output[symbol] = dict(rva=rva, bytes=size, exported=False)
    audit.check(output[entry_symbol]["rva"] == image.facts["entryRva"], "Actual PE entry differs from the private MAP function")
    return output


def verify(audit, build, baseline):
    manifest_path = build / "production-build-manifest.json"
    manifest = audit.json(manifest_path, "completed production build manifest")
    audit.check(manifest.get("kind") == "fixed-game-engine-production-candidate-build"
                and manifest.get("schemaVersion") == 1, "Completed production build manifest is required")
    for field in ("productionAdmissionProved", "gameExecuted", "installed"):
        audit.check(manifest.get(field) is False, "Build manifest must retain an unadmitted/unexecuted candidate: " + field)
    audit.check(manifest.get("testProcessesStarted") == 0 and isinstance(manifest.get("managedBuilt"), bool), "Build scope differs")
    declared_files = audit.declared(manifest["files"], "declared production output")
    native_sources = audit.declared(manifest["sources"], "declared native build source")
    before = audit.json(build / "source-before-native-build.json", "native source freeze before build")
    audit.check(before == manifest["sources"], "Native before/after source declarations differ")
    required = ("winhttp.dll", MODULES[0], MODULES[1], "doorstop.map", "bootstrap.map",
                "production-native-profile.json", "production-managed-profile.json")
    audit.check({str((build / name).resolve()).casefold() for name in required} <= declared_files, "Required final candidate output is undeclared")
    audit.check(manifest.get("nativeProfile") == "production-native-profile.json"
                and manifest.get("managedProfile") == "production-managed-profile.json", "Fixed profile filenames differ")
    native_path, managed_path = build / manifest["nativeProfile"], build / manifest["managedProfile"]
    native = audit.json(native_path, "native compile profile")
    managed = audit.json(managed_path, "managed compile profile")
    audit.check(native.get("kind") == "fixed-game-engine-production-compile-facts" and native.get("schemaVersion") == 1, "Native fact schema differs")
    audit.check(managed.get("kind") == "fixed-game-engine-managed-compile-facts" and managed.get("schemaVersion") == 1, "Managed fact schema differs")
    for profile in (native, managed):
        for field in ("productionAdmissionGranted", "executionWindowProved", "targetImagesLoaded", "installed"):
            audit.check(profile.get(field) is False, "Compile-time facts cannot grant or claim runtime authority: " + field)
    audit.linked(native["generatedHeader"], build / "EngineBootstrapProductionProfile.h", "generated native header")
    audit.linked(managed["nativeProfile"], native_path, "managed-to-native profile link")
    audit.linked(managed["generatedSource"], build / "NativeEngineProductionProfile.Generated.cs", "generated C# source")
    expected_keys = set(GAME_HASHES) | {"Doorstop", "Kernel"}
    audit.check(set(native["images"]) == expected_keys, "Native fixed image inventory differs")
    game = {name: Image(audit, native["images"][name]["path"], expected) for name, expected in GAME_HASHES.items()}
    images = {0: Image(audit, build / MODULES[0]), 1: Image(audit, build / MODULES[1], KERNEL_HASH),
              2: game["UnityPlayer.dll"], 3: game["Rizline.exe"], 4: Image(audit, build / MODULES[4])}
    for name, image in game.items():
        image.match(native["images"][name])
    images[1].match(native["images"]["Kernel"])
    images[4].match(native["images"]["Doorstop"])
    images[0].match(managed["bootstrap"])
    audit.check(all(image.facts["module"] == MODULES[index] for index, image in images.items()), "Five fixed chain image names differ")
    audit.check(len({str(image.path).casefold() for image in images.values()}) == 5, "Five fixed chain identities must be distinct files")
    for index, image in images.items():
        audit.check(bool(image.pe.FILE_HEADER.Characteristics & 0x2000) == (index != 3), "DLL/EXE PE characteristic differs")
        audit.check(image.facts["delayImportRva"] == image.facts["delayImportBytes"] == 0, "Fixed chain cannot contain delay imports")
        if index != 2:
            audit.check(image.facts["tlsRva"] == image.facts["tlsBytes"] == 0, "Fixed EXE/Bootstrap/Kernel/Doorstop must have no TLS directory")
    audit.check(images[1].facts["entryRva"] == 0, "Fixed Kernel must have no PE entry")
    for index, names in ((0, BOOTSTRAP_NAMES), (1, KERNEL_NAMES)):
        audit.check({name for name, _ in images[index].exports()} == set(names), "Fixed public native ABI differs")
        for name in names:
            images[index].export(name)
    bootstrap_dependencies = {row["module"].casefold() for row in images[0].facts["ordinaryImports"]}
    audit.check(bootstrap_dependencies <= {"kernel32.dll", "ntdll.dll", MODULES[1].casefold()}
                and MODULES[1].casefold() in bootstrap_dependencies, "Bootstrap ordinary dependency allowlist differs")
    edges = native.get("edges")
    audit.check(isinstance(edges, list) and len(edges) == 17, "Exactly seventeen canonical source/destination edges are required")
    completed = []
    for position, (source, destination, symbol) in enumerate(CANONICAL):
        declared = edges[position]
        audit.check((declared.get("source"), declared.get("destination"), declared.get("symbol")) == (source, destination, symbol), "Canonical edge order/name differs")
        disk = images[source].imported(MODULES[destination], symbol)
        export_rva = images[destination].export(symbol)
        for field in ("lookupRva", "iatRva"):
            audit.check(declared.get(field) == (0 if source == 0 else disk[field]), "Compile-time source self-reference rule differs: " + symbol + "." + field)
        audit.check(declared.get("exportRva") == (0 if destination == 0 else export_rva), "Compile-time EAT self-reference rule differs: " + symbol)
        completed.append(dict(index=position, source=source, sourceModule=MODULES[source], destination=destination,
                              destinationModule=MODULES[destination], symbol=symbol, declared=declared,
                              finalDisk={**disk, "exportRva": export_rva}, runtimeIatObserved=False))
    for source, destination, names in ((3, 2, ("UnityMain2",)), (2, 4, WINHTTP_NAMES),
                                        (4, 0, BOOTSTRAP_NAMES), (0, 1, KERNEL_NAMES)):
        groups = [row for row in images[source].facts["ordinaryImports"] if row["module"].casefold() == MODULES[destination].casefold()]
        audit.check(len(groups) == 1 and len(groups[0]["symbols"]) == len(names)
                    and set(groups[0]["symbols"]) == set(names), "Selected ordinary descriptor must contain exactly its fixed named edges")
    header = audit.read(build / "EngineBootstrapProductionProfile.h", "generated native header fields").decode("utf-8-sig")
    check_header(audit, header, images, edges)
    generated = audit.read(build / "NativeEngineProductionProfile.Generated.cs", "generated C# fields").decode("utf-8-sig")
    audit.check("internal static partial class NativeEngineProductionProfile" in generated
                and "static partial void Populate(ref NativeEngineProductionProfileData? value)" in generated,
                "Generated C# profile interface differs")
    expected_hashes = [images[index].facts["sha256"] for index in range(5)]
    audit.check(re.findall(r'"([0-9a-f]{64})"', generated) == expected_hashes, "Generated C# five image hashes/order differ")
    arrays = re.findall(r"new\s+uint\[\]\s*\{([^}]+)\}", generated)
    audit.check(len(arrays) == 1 and [number(value) for value in arrays[0].split(",") if value.strip()]
                == [0x792240, 0x78f840, 0x78f680, 0x793840], "Generated C# fixed function RVAs differ")
    profile_sources = [Path(row["path"]) for row in manifest["sources"] if Path(row["path"]).name == "EngineBootstrapProfile.h"]
    audit.check(len(profile_sources) == 1, "Native permission source must be declared exactly once")
    profile_text = audit.read(profile_sources[0], "native hard permission gate").decode("utf-8-sig")
    audit.check(re.search(r"inline\s+constexpr\s+bool\s+ProductionFirstInstallAuthorized\s*=\s*false\s*;", profile_text) is not None
                and "static_assert(!ProductionFirstInstallAuthorized);" in profile_text, "Source execution-window permission must remain literal false with static assertion")
    command = audit.read(build / "build-bootstrap.cmd", "recorded native build command").decode("utf-8-sig")
    audit.check("/DRIZ_ENGINE_BOOTSTRAP_PRODUCTION=1" in command and "/NODEFAULTLIB" in command
                and "/ENTRY:RizEngineBootstrapEntry" in command and "/W4 /WX" in command, "Recorded production compile/link contract differs")
    audit.check("/DRIZ_ENGINE_BOOTSTRAP_OWNED" not in command, "Production candidate cannot select owned profile")
    baseline_image = Image(audit, baseline)
    baseline_exports, proxy_exports = baseline_image.exports(), images[4].exports()
    audit.check(len(baseline_exports) == len(proxy_exports) == 82 and baseline_exports == proxy_exports,
                "Doorstop must preserve all 82 baseline public names and ordinals")
    audit.check(not any(name.startswith(("RizEngine", "RizWinHttp")) for name, _ in proxy_exports), "A private proxy symbol became a public DLL export")
    private = dict(doorstop=check_map(audit, build / "doorstop.map", images[4], DOORSTOP_PRIVATE,
                                    {"RizEngineDoorstopRecord": 12448, "RizWinHttpDoorstopRecord": 1304}, "DllEntry"),
                   bootstrap=check_map(audit, build / "bootstrap.map", images[0], BOOTSTRAP_PRIVATE,
                                       {"RizEnginePublicationRecord": 4096, "RizEngineContinuation": 32}, "RizEngineBootstrapEntry"))
    audit.check(len(images[3].facts["ordinaryImports"]) == 2, "Fixed EXE must list its complete two ordinary dependencies")
    audit.check(len(images[2].facts["ordinaryImports"]) == 19, "Fixed UnityPlayer must list its complete nineteen ordinary dependencies")
    tls = {MODULES[index]: images[index].tls() for index in range(5)}
    audit.check([row["rva"] for row in tls["UnityPlayer.dll"]["callbacks"]] == [0x17e7708, 0x17e7780], "Fixed UnityPlayer original two TLS callback RVAs differ")
    managed_summary = dict(built=manifest["managedBuilt"], verifiedSources=0, verifiedFiles=0)
    if manifest["managedBuilt"]:
        managed_manifest = audit.json(build / "managed-build-manifest.json", "completed managed build manifest")
        audit.check(managed_manifest.get("kind") == "fixed-game-engine-managed-candidate-build"
                    and managed_manifest.get("schemaVersion") == 1, "Completed managed manifest differs")
        for field in ("productionAdmissionProved", "gameExecuted", "installed"):
            audit.check(managed_manifest.get(field) is False, "Managed build cannot claim runtime authority")
        audit.declared(managed_manifest["sources"], "declared managed build source")
        managed_files = audit.declared(managed_manifest["files"], "declared managed output")
        audit.check(audit.json(build / "source-before-managed-build.json", "managed source freeze before build")
                    == managed_manifest["sources"], "Managed before/after source declarations differ")
        audit.check({str((build / "managed" / ("RizToolbox." + name + ".dll")).resolve()).casefold()
                     for name in ("Sdk", "Core", "Runtime", "Adapter", "Hosting", "Loader")} <= managed_files,
                    "Required managed candidate output is undeclared")
        audit.linked(managed_manifest["generatedSource"], build / "NativeEngineProductionProfile.Generated.cs", "managed build generated source")
        managed_summary.update(verifiedSources=len(managed_manifest["sources"]), verifiedFiles=len(managed_manifest["files"]))
    audit.result.update(buildDirectory=str(build), baselineProxy=str(baseline.resolve()),
                        declaredNativeSourceCount=len(native_sources), declaredProductionFileCount=len(declared_files),
                        images=[dict(index=index, path=str(images[index].path), **images[index].facts) for index in range(5)],
                        additionalDiskGameFacts=dict(path=str(game["GameAssembly.dll"].path), **game["GameAssembly.dll"].facts),
                        edges=completed, completeExeOrdinaryImports=images[3].facts["ordinaryImports"],
                        completeUnityOrdinaryImports=images[2].facts["ordinaryImports"], originalTls=tls,
                        proxyExports=[dict(name=name, ordinal=ordinal) for name, ordinal in proxy_exports],
                        privateMapSymbols=private, managedBuild=managed_summary)
    audit.recheck()
    audit.result["passed"] = True


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--build", required=True, type=Path)
    parser.add_argument("--baseline-proxy", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    if not args.build.is_absolute() or not args.baseline_proxy.is_absolute() or not args.output.is_absolute():
        parser.error("All three paths must be absolute.")
    if not args.build.is_dir() or args.build.is_symlink():
        parser.error("--build must be an existing ordinary build directory.")
    if args.output.exists() or not args.output.parent.is_dir():
        parser.error("--output must be a fresh JSON file in an existing directory.")
    audit = Audit()
    try:
        audit.read(Path(__file__).resolve(), "verifier source")
        verify(audit, args.build.resolve(), args.baseline_proxy)
    except Exception as error:
        audit.result["failure"] = dict(type=type(error).__name__, message=str(error))
    result = audit.finish()
    with args.output.open("x", encoding="utf-8", newline="\n") as stream:
        json.dump(result, stream, indent=2, ensure_ascii=False)
        stream.write("\n")
    print(json.dumps(dict(passed=result["passed"], checks=result["checks"], inputs=len(result["inputs"]),
                          output=str(args.output), runtimeIatObserved=False, executionWindowProved=False,
                          productionAdmissionProved=False)))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    sys.exit(main())
