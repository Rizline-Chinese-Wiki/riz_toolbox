"""Verify transport V5 / operation V7 candidate disk images. Never execute a PE."""
from __future__ import annotations
import argparse
import base64
import hashlib
import json
import re
import stat
import struct
import zipfile
from pathlib import Path
import pefile

BOOT = "RizToolbox.EngineBootstrap.dll"
KERNEL = "RizToolbox.EngineNodeKernel.dll"
KERNEL_SHA = "1541feaa8fe7b37d0d3a544fbb315387ea93e3f6b96a4dc955dc76dc9c30e38a"
GAME = {"UnityPlayer.dll": "43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1",
        "Rizline.exe": "f9c37f1078619d02c62a7a5815ca48179666efb2dfe7e6e698bfff7858c26e5e"}
API = tuple("RizEngineTransport_" + name for name in ("GetVersion", "GetInfoV5", "VerifyV5", "AbortAfterPublication"))
WINHTTP = ("WinHttpOpen", "WinHttpGetProxyForUrl", "WinHttpGetIEProxyConfigForCurrentUser", "WinHttpCloseHandle")
KN = tuple("RizEngineNodeContext_" + n for n in (
    "GetAbiVersion", "GetLimit", "GetFrameSize", "GetCompletionSize", "GetEntry", "Configure", "Attach", "GetInfo",
    "GetInlineAbiVersion", "GetInlineLimit", "GetInlineFrameSize", "GetInlineEntry", "ConfigureInline", "AttachInline", "GetInlineInfo"))
KN += tuple("RizEngineSchedulerContext_" + n for n in (
    "GetAbiVersion", "GetLimit", "GetFrameSize", "GetCompletionSize", "GetRowFrameSize", "GetEntry", "GetDrainRowEntry", "Configure", "Attach", "GetInfo"))
KN += tuple("RizEngineWaitContext_" + n for n in (
    "GetAbiVersion", "GetLimit", "GetFrameSize", "GetCompletionSize", "GetPhaseFrameSize", "GetEntry", "GetPhaseEntry", "Configure", "Attach", "GetInfo"))
RVAS = (0x792240, 0x78F840, 0x78F680, 0x793840, 0x44D630, 0x447AD0, 0x447C30,
        0x447D60, 0x447750, 0x78F5E0, 0x78FC50, 0x5A5260, 0x5DA510)
MANAGED_NAMES = tuple("RizToolbox." + name + ".dll" for name in ("Sdk", "Core", "Runtime", "Adapter", "Hosting", "Loader"))
PAYLOAD_PATHS = {"BepInEx/plugins/RizToolbox/" + name for name in MANAGED_NAMES}
PLAYBACK_PATHS = ("BepInEx/plugins/RizToolbox/profiles/steam-25977441.json",
                  "BepInEx/plugins/RizToolbox/RizToolbox.SingleReturn.Prototype.dll",
                  "BepInEx/plugins/RizToolbox/RizToolbox.PlaybackSourcePrototype.dll")
MOD_ROOT = "BepInEx/RizToolbox/mods/"
MOD_PATHS = {f"{MOD_ROOT}{plugin}/{name}"
             for plugin in ("example.panel", "example.events", "example.color-low", "example.color-high", "example.scores")
             for name in ("plugin.json", "RizToolbox.Examples.dll")}
PAYLOAD_PATHS.update(PLAYBACK_PATHS)
PAYLOAD_PATHS.update(("BepInEx/plugins/RizToolbox/RizToolbox.JudgeContext.dll",
                      BOOT, KERNEL, "winhttp.dll", ".doorstop_version",
                      "BepInEx/plugins/RizToolbox/licenses/doorstop-corresponding-source.zip", "BepInEx/plugins/RizToolbox/licenses/UnityDoorstop.LGPL-2.1.txt"))
EVIDENCE_PATHS = set((
    "production-native-build.json", "native-profile.json", "final-native-validation.json",
    "NativeEngineTransportProfile.Generated.cs", "EngineTransportProfile.Generated.h", "production-native-sources.zip",
    "bootstrap.map", "build-production-transport.cmd", "transport-doorstop-build.json", "base-package-manifest.json",
    "transport-managed-build.json", "managed-source-snapshot.json", "managed-source-snapshot.zip",
    "operation-native-build.json", "operation-native-sources.zip", "operation-export-validation.json",
    "operation-profile-verification.json", "operation-native-proof.json", "operation-native-result.json", "operation-managed-proof.json",
    "tween-admission-native-proof.json", "tween-admission-native-result.json", "tween-admission-native-sources.zip",
    "tween-source-native-proof.json", "tween-source-native-result.json", "tween-source-native-sources.zip",
    "tween-retirement-native-proof.json", "tween-retirement-native-sources.zip",
    "tween-references-native-proof.json", "tween-references-native-result.json", "tween-references-native-sources.zip"))
SOURCE_CORE = {"native/RizToolbox.JudgeContext/" + name for name in (
    "TweenSourceContext.cpp", "TweenSourceContext.h", "TweenSourceProfile.h", "TweenSourceEntry.asm", "MidContext.h", "TweenAdmission.h",
    "TweenSourceContext.References.h", "TweenSourceReferences.h", "TweenSourceReferences.cpp",
    "TweenRetirementContext.h", "TweenRetirementContext.cpp")}
SOURCE_INPUTS = SOURCE_CORE | {"native/RizToolbox.JudgeContext/fixture/TweenSourceFixture.cpp",
                              "native/RizToolbox.JudgeContext/fixture/TweenSourceFixtureCaller.asm", "scripts/Test-TweenSourcesNative.ps1"}
SINGLE_SPAN_RVAS = (0xBC1087, 0xBC1107, 0xBC110D, 0xBC115D, 0xBC1162, 0xBC121B, 0xBC1221)
SINGLE_SPAN_WIDTHS = (9, 6, 9, 5, 9, 6, 9)
SINGLE_EXPORTS = {"RizTweenSingleEventContext_" + name for name in
                  ("GetMidEntry", "GetPreEntry", "GetPostEntry", "Attach", "GetInfo", "ReadCurrent")}
SINGLE_SOURCE_INPUTS = {"native/RizToolbox.JudgeContext/" + name for name in
                       ("TweenSingleEventContext.cpp", "TweenSingleEventContext.h", "TweenSingleEventEntry.asm",
                        "TweenSingleEventPublication.inl")}
PLAYBACK_ATTESTATION_EXPORTS = {
    "RizFirstInstall_GetPlaybackAttestationV1", "RizFirstInstall_ReadPlaybackAttestedRangeV1",
    "RizFirstInstall_ReadPlaybackAttestedSnapshotV1"}
PLAYBACK_ATTESTATION_SOURCE_INPUTS = {"native/RizToolbox.JudgeContext/" + name for name in
                                      ("FirstInstallPlaybackAttestation.h", "FirstInstallPlaybackAttestation.inl")}
SINGLE_BLOCK_TAMPER_OFFSETS = (
    0, 4, 5, 7, 9, 14, 15, 20, 21, 63, 64, 67, 87, 88, 99, 100,
    255, 256, 260, 261, 263, 265, 270, 271, 276, 277, 319, 320, 323, 343, 344, 355, 356,
    511, 512, 516, 517, 519, 521, 526, 527, 532, 533, 575, 576, 579, 599, 600, 611, 612,
    767, 768, 895, 896, 907, 908, 919, 920, 931, 932, 1279, 1280, 1285, 1286, 1293, 1294,
    1311, 1312, 1317, 1318, 1325, 1326, 1343, 1344, 1349, 1350, 1357, 1358, 1536, 4095)
SINGLE_SPAN_TAMPER_MODES = {
    f"tamper-single-span-{span}-byte-{offset}": (span, offset)
    for span, width in enumerate(SINGLE_SPAN_WIDTHS) for offset in range(width)}
SINGLE_BLOCK_TAMPER_MODES = {f"tamper-single-block-byte-{offset}": offset for offset in SINGLE_BLOCK_TAMPER_OFFSETS}
SINGLE_PROTECTION_MODES = {f"tamper-single-source-protection-{span}": span for span in range(7)}
SINGLE_PROTECTION_MODES["tamper-single-block-protection"] = 7
SINGLE_TAMPER_MODES = set(SINGLE_SPAN_TAMPER_MODES) | set(SINGLE_BLOCK_TAMPER_MODES) | set(SINGLE_PROTECTION_MODES)
SINGLE_PARTIAL_OBJECTS = {name + ".obj" for name in (
    "JudgeContext", "JudgeEntry", "MidEntry", "ListenerEntry", "ListenerContext", "InitializationContext", "CoroutineContext", "OperationContext",
    "TweenAdmission", "TweenRetirementContext", "TweenSourceContext", "TweenSourceReferences", "TweenSourcePublication", "TweenSourceEntry",
    "TweenSingleEventContext", "TweenSingleEventEntry", "EngineNodeContext", "VoidContext", "MidContext", "BootstrapVoidContext", "RuntimeInvokeContext")}

def selected_mod_paths(base):
    if "packageKind" not in base and "basicPlugins" not in base:
        return MOD_PATHS
    ids = base.get("basicPlugins")
    need(base.get("schemaVersion") == 3 and base.get("packageKind") in ("Runtime", "Author", "Validation")
         and base.get("developmentAssetsInstalled") is False and base.get("proofEnabledByPackage") is False
         and isinstance(ids, list), "Invalid distribution plugin selection")
    need(all(isinstance(i, str) and i in ("basic.song-scores", "basic.theme", "basic.quick-panel") for i in ids)
         and len(ids) == len(set(ids)), "Unknown or duplicate ordinary plugin")
    expected = {f"{MOD_ROOT}{i}/{name}" for i in ids for name in ("plugin.json", "RizToolbox.BasicPlugins.dll")}
    declared = [item["path"] for item in base["files"] if item["path"].lower().startswith(MOD_ROOT.lower())]
    need(len(declared) == len(expected) and set(declared) == expected, "Runtime plugin files differ from the selection")
    return expected


def need(value, reason):
    if not value:
        raise ValueError(reason)

def digest(data):
    return hashlib.sha256(data).hexdigest()

def is_linked(path):
    info = path.lstat()
    return stat.S_ISLNK(info.st_mode) or bool(getattr(info, "st_file_attributes", 0) & stat.FILE_ATTRIBUTE_REPARSE_POINT)


def ordinary(path):
    path = Path(path)
    need(path.is_absolute() and path.is_file(), f"Absolute regular file required: {path}")
    for part in (path, *path.parents):
        need(not is_linked(part), f"Linked input refused: {part}")
    return path.resolve()

def row(path):
    path = ordinary(path)
    data = path.read_bytes()
    return dict(path=str(path), sha256=digest(data), bytes=len(data))

def read_json(path):
    data = json.loads(ordinary(path).read_text(encoding="utf-8-sig"))
    need(isinstance(data, dict), "JSON object required")
    return data

def false_fields(data, names):
    for name in names:
        need(name in data and data[name] is False, f"{name} must remain literal false")


def archived_sources(path, rows, expected=None):
    """Read exact frozen source bytes; producer paths are informational only."""
    declared = {}
    for item in rows:
        name = item.get("relativePath")
        need(isinstance(name, str) and re.fullmatch(r"[A-Za-z0-9_.\-/]+", name)
             and not Path(name).is_absolute() and "\\" not in name and ":" not in name
             and all(part not in ("", ".", "..") for part in name.split("/")) and name not in declared,
             "Unsafe or duplicate archived source name")
        need(re.fullmatch(r"[0-9a-f]{64}", item.get("sha256", ""))
             and type(item.get("bytes")) is int and 0 < item["bytes"] <= 16777216, "Invalid archived source identity")
        declared[name] = item
    need(declared and (expected is None or set(declared) == set(expected)), "Exact declared source input set differs")
    with zipfile.ZipFile(ordinary(path)) as archive:
        entries = archive.infolist()
        need(len(entries) == len(declared) and {entry.filename for entry in entries} == set(declared),
             "The source ZIP must contain the exact declared compilation inputs")
        result = {}
        for entry in entries:
            name = entry.filename
            need(not entry.is_dir() and (entry.external_attr >> 16) & 0xF000 != 0xA000
                 and name not in result and entry.file_size == declared[name]["bytes"], "Linked, duplicate or resized source ZIP entry")
            data = archive.read(entry)
            need(len(data) == declared[name]["bytes"] and digest(data) == declared[name]["sha256"], "Archived compilation input bytes differ")
            result[name] = data
    return result

class Image:
    def __init__(self, path):
        self.path = ordinary(path)
        self.data = self.path.read_bytes()
        self.pe = pefile.PE(data=self.data, fast_load=True)
        p = self.pe
        need(p.FILE_HEADER.Machine == 0x8664 and p.OPTIONAL_HEADER.Magic == 0x20B
             and p.FILE_HEADER.SizeOfOptionalHeader == 240 and p.OPTIONAL_HEADER.NumberOfRvaAndSizes == 16,
             f"Complete x64 PE32+ required: {path}")
        p.parse_data_directories(directories=[0, 1, 3, 9, 13, 14])
        need(not p.OPTIONAL_HEADER.DATA_DIRECTORY[13].VirtualAddress and not p.OPTIONAL_HEADER.DATA_DIRECTORY[13].Size,
             f"Delay imports forbidden: {path}")
        need(not p.OPTIONAL_HEADER.DATA_DIRECTORY[14].VirtualAddress, f"Native image required: {path}")
    def bytes(self, rva, size):
        need(rva > 0 and size > 0 and rva + size <= self.pe.OPTIONAL_HEADER.SizeOfImage, "Invalid PE range")
        sections = [s for s in self.pe.sections if s.VirtualAddress <= rva
                    and rva + size <= s.VirtualAddress + min(s.Misc_VirtualSize, s.SizeOfRawData)]
        need(len(sections) == 1, "Range must have one complete raw-backed section")
        off = sections[0].PointerToRawData + rva - sections[0].VirtualAddress
        result = self.data[off:off + size]
        need(len(result) == size, "Truncated PE range")
        return result
    def executable(self, rva):
        self.bytes(rva, 1)
        need(any(s.VirtualAddress <= rva < s.VirtualAddress + s.Misc_VirtualSize
                 and s.Characteristics & 0xE0000000 == 0x60000000 for s in self.pe.sections), "RX function required")
    def exports(self, names=None, count=None):
        need(hasattr(self.pe, "DIRECTORY_ENTRY_EXPORT"), f"Missing EAT: {self.path}")
        entries = list(self.pe.DIRECTORY_ENTRY_EXPORT.symbols)
        actual = [e.name.decode("ascii") if e.name else None for e in entries]
        need(len(set(actual)) == len(actual) and all(actual), "Unique named exports required")
        if names is not None:
            need(set(actual) == set(names) and len(actual) == len(names), "Exact final EAT contract differs")
        if count is not None:
            need(len(entries) == count and self.pe.DIRECTORY_ENTRY_EXPORT.struct.NumberOfFunctions == count,
                 "Final EAT count differs")
        result = {}
        for e in entries:
            need(not e.forwarder, "Forwarded exports forbidden")
            self.executable(e.address)
            result[e.name.decode("ascii")] = int(e.address)
        return result
    def imports(self, module, names):
        ds = [d for d in getattr(self.pe, "DIRECTORY_ENTRY_IMPORT", []) if d.dll.decode("ascii").lower() == module.lower()]
        need(len(ds) == 1, f"Unique ordinary import descriptor required: {module}")
        d = ds[0]
        need(d.struct.OriginalFirstThunk and d.struct.FirstThunk, "Independent ordinary ILT and IAT required")
        actual = [e.name.decode("ascii") if e.name else None for e in d.imports]
        need(len(set(actual)) == len(actual) and set(actual) == set(names) and len(actual) == len(names), "Exact ordinary named imports differ")
        result = {}
        for i, entry in enumerate(d.imports):
            ilt, iat = d.struct.OriginalFirstThunk + i * 8, d.struct.FirstThunk + i * 8
            need(entry.address - self.pe.OPTIONAL_HEADER.ImageBase == iat, "IAT/ILT ordinal differs")
            thunk = struct.unpack("<Q", self.bytes(ilt, 8))[0]
            need(0 < thunk < 0x80000000 and self.bytes(thunk + 2, len(entry.name) + 1) == entry.name + b"\0", "Bound/ordinal imports refused")
            need(self.bytes(iat, 8) == self.bytes(ilt, 8), "Final disk IAT must remain unbound ordinary ILT")
            result[entry.name.decode("ascii")] = (ilt, iat)
        need(self.bytes(d.struct.OriginalFirstThunk + len(actual) * 8, 8) == bytes(8)
             and self.bytes(d.struct.FirstThunk + len(actual) * 8, 8) == bytes(8), "Import terminator differs")
        return result

def validate(directory, game_directory=None, relocated_inputs=()):
    root = directory.resolve(strict=True)
    if game_directory is not None:
        need(game_directory.is_absolute(), "An absolute offline game directory is required")
    relocated = dict(relocated_inputs)
    need(len(relocated) == len(relocated_inputs), "Duplicate relocated native build input")

    def build_input(value):
        path = Path(value)
        if game_directory is not None and path.name in GAME:
            path = game_directory / path.name
        elif path in relocated:
            path = relocated[path]
        return ordinary(path)

    manifest_path = root / "production-native-build.json"
    build = read_json(manifest_path)
    need(build.get("schemaVersion") == 1 and build.get("kind") == "engine-transport-production-native-candidate-build"
         and build.get("passed") is True and build.get("buildExitCode") == 0
         and build.get("sourceHashesUnchanged") is True and build.get("gameFilesUnchanged") is True, "Successful actual production native build required")
    false_fields(build, ("fixtureExecuted", "gameExecuted", "installed", "packaged"))
    declared = {Path(item["path"]) for item in build["inputs"]}
    need(all(source in declared and source.name not in GAME for source in relocated), "Unknown relocated native build input")
    admitted = bool(build.get("productionAdmissionGranted", False))
    need(build.get("productionAdmissionVerified") is admitted, "Build productionAdmissionVerified must match productionAdmissionGranted")
    if admitted:
        need(build.get("interiorEntryClosureVerified") is True and build.get("productionColdTraceVerified") is True,
             "Admitted build must record Stage A interior-entry closure and cold-trace verification")
    outputs = {}
    for item in build["files"]:
        path = ordinary(item["path"])
        need(path.parent == root and path.name not in outputs and row(path)["sha256"] == item["sha256"]
             and path.stat().st_size == item["bytes"], "A final build artifact changed or escaped its build root")
        outputs[path.name] = item
    need(set(outputs) == {p.name for p in root.iterdir() if p.is_file() and p.name != manifest_path.name}, "Undeclared final build artifact")
    inputs = {str(build_input(x["path"])): x for x in build["inputs"]}
    need(len(inputs) == len(build["inputs"]), "Duplicate build input")
    # All frozen input bytes remain checked; they are evidence, not executable authority.
    for path, item in inputs.items():
        actual = row(path)
        need(actual["sha256"] == item["sha256"] and actual["bytes"] == item["bytes"],
             f"Frozen build input changed: {path}")
    profile = read_json(root / "native-profile.json")
    need(profile.get("kind") == "engine-transport-native-compile-facts" and profile.get("nativeProfile") == 2
         and profile.get("profile") == "production" and profile.get("sourceHashesUnchanged") is True, "Owned or legacy native profile refused")
    false_fields(profile, ("nativePublicationReadyProved", "kernelConfigured", "sourcePatched", "targetImagesLoaded", "gameExecuted", "installed"))
    need(profile.get("productionAdmissionGranted") is admitted and profile.get("executionWindowProved") is admitted
         and profile.get("interiorEntryClosureVerified") is admitted and profile.get("productionColdTraceVerified") is admitted,
         "Compile profile admission/Stage A state differs from build manifest")
    need(len(profile["images"]) == 5 and len(profile["edges"]) == 44, "Complete V5 five-image/44-edge compile contract required")
    images = [Image(root / BOOT), Image(root / KERNEL), Image(build_input(profile["images"][2]["path"])),
              Image(build_input(profile["images"][3]["path"])), Image(root / "winhttp.dll")]
    need(images[2].path.name == "UnityPlayer.dll" and images[3].path.name == "Rizline.exe", "Owned image cannot be repackaged as a production target")
    for i in (2, 3):
        need(digest(images[i].data) == GAME[images[i].path.name], "Pinned game input differs")
    need(digest(images[1].data) == KERNEL_SHA, "Exact verified 35-export Kernel required")
    for i in range(1, 5):
        need(profile["images"][i]["sha256"] == digest(images[i].data) and profile["images"][i]["module"] == images[i].path.name,
             "Generated native image pin differs from final actual PE")
    need(profile["images"][0]["hashPolicy"] == 1 and profile["images"][0]["sha256"] == "0" * 64, "Bootstrap self-hash cycle policy differs")
    exps = [images[0].exports(API, 4), images[1].exports(KN, 35), images[2].exports(), None, images[4].exports(count=82)]
    need(images[0].bytes(exps[0][API[0]], 6) == bytes.fromhex("b805000000c3"), "Actual GetVersion body does not return ABI5")
    for i in (0, 1):
        need(not images[i].pe.OPTIONAL_HEADER.DATA_DIRECTORY[9].VirtualAddress, "Bootstrap/Kernel TLS forbidden")
    need(images[1].pe.OPTIONAL_HEADER.AddressOfEntryPoint == 0, "Kernel entry forbidden")
    modules = {d.dll.decode("ascii").lower() for d in images[0].pe.DIRECTORY_ENTRY_IMPORT}
    need(modules == {KERNEL.lower(), "kernel32.dll", "ntdll.dll"}, "Bootstrap contains unexpected loader/CRT dependencies")
    edges = []
    for src, dst, names in ((3, 2, ("UnityMain2",)), (2, 4, WINHTTP), (4, 0, API), (0, 1, KN)):
        imported = images[src].imports(images[dst].path.name, names)
        for name in names:
            lookup, iat = imported[name]
            edges.append(dict(source=src, destination=dst, lookupRva=lookup, iatRva=iat, exportRva=exps[dst][name], symbol=name))
    need(len(edges) == 44, "Final ordinary graph is incomplete")
    payload = b"".join(struct.pack("<5I", x["source"], x["destination"], x["lookupRva"], x["iatRva"], x["exportRva"])
                       + x["symbol"].encode("ascii") + b"\0" for x in edges)
    for actual, prior in zip(edges, profile["edges"], strict=True):
        need(all(prior[k] == actual[k] for k in ("source", "destination", "symbol")), "Compile/final edge order differs")
        for key in ("lookupRva", "iatRva", "exportRva"):
            need(prior[key] in (0, actual[key]), "Known compile edge changed after final link")
    generated_path = root / "NativeEngineTransportProfile.Generated.cs"
    generated = generated_path.read_text(encoding="utf-8-sig")
    hashes = [digest(x.data) for x in images]
    found_hashes = re.findall(r'"([0-9a-f]{64})"', generated)
    if admitted:
        need(found_hashes == hashes + [profile["fixedContractSha256"], profile["interiorClosureSha256"], profile["coldTraceSha256"]]
             and ", true, " in generated, "Final independent admitted managed five-image/Stage A SHA pins differ")
    else:
        need(found_hashes == hashes, "Final independent managed five-image SHA pins differ")
    need([int(x, 16) for x in re.findall(r"0x[0-9a-fA-F]+", generated)] == list(RVAS)
         and "NativeEngineTransportProfileData" in generated and "NativeEngineProductionProfile" not in generated,
         "The managed profile is legacy, owned, or lacks exact thirteen targets")
    need(build["managedProfile"]["sha256"] == row(generated_path)["sha256"] and build["managedProfile"]["finalImageHashes"] == hashes,
         "Managed generated source provenance differs")
    archive_path = root / "production-native-sources.zip"
    need(row(archive_path)["sha256"] == build["sourceArchive"]["sha256"], "Frozen source ZIP differs")
    with zipfile.ZipFile(archive_path) as archive:
        names = archive.namelist()
        need(len(names) == len(set(names)) and all(not n.startswith("/") and ".." not in Path(n).parts for n in names), "Unsafe source ZIP")
        for name in names:
            matches = [x for x in inputs.values() if Path(x["path"]).as_posix().endswith("/" + name)]
            need(len(matches) == 1 and digest(archive.read(name)) == matches[0]["sha256"], "Archived compiled source is not independently frozen")
        gate = archive.read("native/RizToolbox.EngineBootstrap/EngineTransportProfile.h").decode("utf-8-sig")
        need("Generated::ProductionFirstInstallAuthorized" in gate
             and "Generated::InteriorEntryClosureVerified" in gate
             and "Generated::ProductionColdTraceVerified" in gate
             and "static_assert(!Owned||!ProductionFirstInstallAuthorized);" in gate,
             "The actual compiled production gate source lacks Stage A verification bindings")
    gen_hdr = (root / "EngineTransportProfile.Generated.h").read_text(encoding="utf-8-sig")
    if admitted:
        need("inline constexpr bool InteriorEntryClosureVerified=true;" in gen_hdr
             and "inline constexpr bool ProductionColdTraceVerified=true;" in gen_hdr
             and "inline constexpr bool ProductionAdmissionVerified=true;" in gen_hdr
             and "inline constexpr bool ProductionFirstInstallAuthorized=true;" in gen_hdr,
             "Admitted generated header lacks Stage A verification flags")
    else:
        need("inline constexpr bool ProductionFirstInstallAuthorized=false;" in gen_hdr,
             "Unadmitted generated header must remain closed")
    pe_dump_path = root / "bootstrap.pe.txt"
    pe_dump_text = pe_dump_path.read_text(encoding="utf-8-sig", errors="replace")
    required_imports = ("VirtualAlloc", "VirtualProtect", "FlushInstructionCache", "RtlAddFunctionTable")
    imports_verified = all(sym in pe_dump_text for sym in required_imports)
    if admitted:
        need(imports_verified, "Admitted Bootstrap PE dump lacks required memory/protection/unwind imports")
    command = (root / "build-production-transport.cmd").read_text(encoding="utf-8-sig")
    need(command.count("/DRIZ_ENGINE_TRANSPORT_PRODUCTION=1") == 4 and "RIZ_ENGINE_TRANSPORT_OWNED" not in command
         and "/ENTRY:RizEngineTransportBootstrapEntry" in command, "Actual compile command profile/entry differs")
    mapping = (root / "bootstrap.map").read_text(encoding="utf-8-sig")
    entry = re.findall(r"^\s*\w+:\w+\s+RizEngineTransportBootstrapEntry\s+([0-9a-fA-F]+)\s", mapping, re.M)
    need(len(entry) == 1 and int(entry[0], 16) - images[0].pe.OPTIONAL_HEADER.ImageBase == images[0].pe.OPTIONAL_HEADER.AddressOfEntryPoint,
         "Final actual entry does not match the frozen producer symbol")
    license_path = root / "UnityDoorstop.LGPL-2.1.txt"
    corresponding = root / "doorstop-corresponding-source.zip"
    with zipfile.ZipFile(corresponding) as archive:
        names = archive.namelist()
        need(len(names) == len(set(names)) and all(not n.startswith("/") and ".." not in Path(n).parts for n in names), "Unsafe corresponding source ZIP")
        need(archive.read("LICENSE") == license_path.read_bytes(), "Doorstop license/source mismatch")
        for name in ("riz_engine_transport.cpp", "riz_engine_transport.h"):
            value = archive.read("src/windows/" + name)
            matches = [x for p, x in inputs.items() if p.endswith("transport-doorstop-build.json")]
            need(len(matches) == 1, "Unique actual production Doorstop build required")
            doorstop = read_json(matches[0]["path"])
            need(doorstop.get("kind") == "engine-transport-doorstop-build" and doorstop.get("passed") is True
                 and doorstop.get("owned") is False and doorstop.get("sourceHashesUnchanged") is True, "Owned/failed Doorstop refused")
            false_fields(doorstop, ("installed", "gameExecuted"))
            need(doorstop.get("productionAdmissionVerified") is admitted, "Doorstop admission flag differs from build")
            source_rows = [x for x in doorstop["inputs"] if Path(x["path"]).name == name]
            need(len(source_rows) == 1 and digest(value) == source_rows[0]["sha256"], "Doorstop compiled corresponding source differs")
    return dict(schemaVersion=1, kind="engine-transport-production-final-native-validation", passed=True,
                publicationVersion=5, publicationBytes=91376, nativeProfile=2, finalGraphComplete=True,
                bootstrapRequiredImportsVerified=imports_verified,
                images=[dict(index=i, module=x.path.name, **row(x.path)) for i, x in enumerate(images)],
                edges=edges, dependencyGraphSha256=digest(payload), functionRvas=list(RVAS),
                generatedSource=row(generated_path), buildManifest=row(manifest_path), nativeCompileFacts=row(root / "native-profile.json"),
                sourceArchive=row(archive_path), correspondingSource=row(corresponding), license=row(license_path),
                sourceGateClosed=(not admitted), targetImagesLoaded=False, nativePublicationReadyProved=False,
                productionAdmissionGranted=admitted, productionAdmissionVerified=admitted, gameExecuted=False, installed=False)

def validate_tween_admission(evidence, build, helper_sha, compiled_sources):
    """Check executed gate behavior against archived inputs, without producer paths."""
    manifest = read_json(evidence / "tween-admission-native-proof.json")
    result = read_json(evidence / "tween-admission-native-result.json")
    need(manifest.get("kind") == "typed-tween-admission-native-fixture-build"
         and manifest.get("passed") is True and manifest.get("compileExitCode") == 0
         and manifest.get("testExitCode") == 0 and manifest.get("inputHashesUnchanged") is True
         and manifest.get("sameProductionGateAndDispatchSources") is True,
         "Successful execution of the actual native gate and dispatch sources is required")
    false_fields(manifest, ("productionHelperExecuted", "gameExecuted"))
    build_sha = row(evidence / "operation-native-build.json")["sha256"]
    need(manifest["helper"]["sha256"] == helper_sha
         and manifest["nativeBuildManifest"]["sha256"] == build_sha
         and manifest["result"]["sha256"] == row(evidence / "tween-admission-native-result.json")["sha256"],
         "The executed Tween gate fixture used a different helper/source build")
    source_zip = evidence / "tween-admission-native-sources.zip"
    archive_row = row(source_zip)
    need(manifest["sourceArchive"]["sha256"] == archive_row["sha256"]
         and manifest["sourceArchive"]["bytes"] == archive_row["bytes"], "Tween fixture source archive differs")
    expected = {"native/RizToolbox.JudgeContext/" + name for name in (
        "TweenRetirementContext.h", "TweenRetirementContext.cpp",
        "TweenAdmission.h", "TweenAdmission.cpp", "OperationContext.h", "OperationContext.cpp",
        "FirstInstall.h", "fixture/TweenAdmissionFixture.cpp")}
    expected.add("scripts/Test-TweenAdmissionNative.ps1")
    sources = archived_sources(source_zip, manifest["sourceFiles"], expected)
    for name, data in sources.items():
        if name.startswith("native/"):
            need(compiled_sources.get(name) == data, "The fixture used different archived production gate/dispatch bytes")
    need(result.get("kind") == "typed-tween-admission-native-fixture" and result.get("passed") is True
         and result.get("failures") == 0 and result.get("independentProcesses") == 4
         and result.get("nativeBuildManifestSha256") == build_sha and result.get("helperSha256") == helper_sha,
         "Four isolated actual Tween gate behavior processes are required")
    modes = result.get("modes", [])
    need(len(modes) == 4 and {item.get("mode") for item in modes} == {"normal", "unknown-read", "abnormal", "capacity"},
         "The Tween gate execution proof omits required behaviors")
    for value in [result, *modes]:
        need(value.get("passed") is True and value.get("failures") == 0 and value.get("checks", 0) > 0
             and value.get("actualNativeGateExecuted") is True and value.get("actualOperationDispatchExecuted") is True
             and value.get("controlledReadyAndMetadata") is True and value.get("specificIssuanceOnly") is True,
             "Tween gate behavior evidence is incomplete")
        false_fields(value, ("productionMetadataVerified", "productionHelperExecuted", "gameExecuted"))
    need(result["checks"] == sum(item["checks"] for item in modes), "Tween gate check total differs")
    normal = next(item for item in modes if item["mode"] == "normal")
    need(normal.get("skippedCalls") == 5 and normal.get("beforeCalls", 0) > 0
         and normal["beforeCalls"] == normal.get("afterCalls")
         and all(normal.get(name) is True for name in (
             "workerClosingWithoutEarlySeal", "allFivePhysicalLanesBlockUnregister", "deadUnownedSkipped",
             "aliveUnownedForwarded", "nullArgumentOriginalException", "skippedFlagsWithoutReturned",
             "exactOriginalCounters", "retiredSlotNewEpoch")), "Actual Tween gate concurrency/dispatch facts are missing")
    return dict(nativeProcesses=4, checks=result["checks"], actualGateAndDispatchExecuted=True,
                controlledMetadata=True, productionMetadataVerified=False, sourceArchiveVerified=True)


def validate_tween_sources(evidence, compiled_sources):
    """Owned model execution proves source behavior, never production Ready/GC."""
    manifest = read_json(evidence / "tween-source-native-proof.json")
    result = read_json(evidence / "tween-source-native-result.json")
    need(manifest.get("schemaVersion") == 1 and manifest.get("kind") == "owned-native-tween-source-fixture-build"
         and manifest.get("passed") is True and manifest.get("compileExitCode") == 0
         and manifest.get("sourceHashesUnchanged") is True and manifest.get("failure") is None,
         "Successful actual owned Tween source compilation/execution evidence required")
    false_fields(manifest, ("productionHelperExecuted", "gameExecuted", "officialMetadataVerified"))
    need(manifest["result"]["sha256"] == row(evidence / "tween-source-native-result.json")["sha256"],
         "Owned Tween source result differs from its actual runner proof")
    source_zip = evidence / "tween-source-native-sources.zip"
    archive_row = row(source_zip)
    need(all(manifest["sourceArchive"][key] == result["sourceArchive"][key] == archive_row[key] for key in ("sha256", "bytes")),
         "Owned Tween source archive/result binding differs")
    sources = archived_sources(source_zip, manifest["sourceFiles"], SOURCE_INPUTS)
    runner = sources["scripts/Test-TweenSourcesNative.ps1"].decode("utf-8-sig")
    relative_block = re.search(r"\$relative\s*=\s*@\((.*?)\)\s*\r?\n\$locks", runner, re.S)
    need(relative_block is not None, "The archived source runner has no exact compilation input inventory")
    runner_inputs = re.findall(r"'([^']+)'", relative_block.group(1))
    need(len(runner_inputs) == len(SOURCE_INPUTS) == 14 and set(runner_inputs) == SOURCE_INPUTS and len(SOURCE_CORE) == 11,
         "The actual owned source runner's fourteen compilation inputs and eleven production core files differ")
    fixture_source = sources["native/RizToolbox.JudgeContext/fixture/TweenSourceFixture.cpp"].decode("utf-8-sig")
    need("/DRIZ_TWEEN_SOURCE_FIXTURE" in runner and "RizTweenSourceFixture_Metadata" in fixture_source,
         "The owned source proof must retain its explicit controlled metadata build seam")
    for name in SOURCE_CORE:
        need(compiled_sources.get(name) == sources[name], "Owned Tween source execution used different actual compiled production core bytes")
    need(result.get("schemaVersion") == 1 and result.get("kind") == "owned-native-tween-source-fixture-summary"
         and result.get("passed") is True and result.get("sourceHashesUnchanged") is True
         and result.get("actualPreservingEntryExecuted") is True and result.get("independentProcesses") == 13
         and result.get("failure") is None, "Thirteen independent actual preserving-entry source processes required")
    false_fields(result, ("productionHelperExecuted", "gameExecuted", "officialMetadataVerified"))
    modes = result.get("modes", [])
    reference_names = {"references-grow", "references-fault", "references-abnormal",
                       "references-before-changed", "references-after-changed"}
    names = {"happy", "capacity", "broken-pair", "wrong-parent", "foreign-tail", "abnormal-exit", "unbound", "unfinished-window"} | reference_names
    need(len(modes) == 13 and {item.get("mode") for item in modes} == names and manifest.get("modes") == modes,
         "The owned source runner/result omit or change a required process")
    mode_block = re.search(r"foreach\(\$mode\s+in\s+@\((.*?)\)\)", runner, re.S)
    runner_modes = re.findall(r"'([^']+)'", mode_block.group(1)) if mode_block is not None else []
    need(len(runner_modes) == 13 and set(runner_modes) == names,
         "The archived owned runner does not execute the declared thirteen modes")
    for item in modes:
        need(item.get("passed") is True and item.get("failures") == 0
             and type(item.get("checks")) is int and item["checks"] > 0
             and type(item.get("machineCalls")) is int and item["machineCalls"] > 0,
             "Actual owned source process has no successful preserving-entry execution checks")
        false_fields(item, ("gameCodeExecuted", "gameRuntimeVerified", "officialMetadataVerified"))
        if item["mode"] in reference_names:
            # These joint executions publish references while source frames are
            # live. Their own JSON scope has no old ABI/journal coverage fields;
            # do not fabricate those fields or reinterpret model roots as GC.
            need(item.get("kind") == "owned-joint-tween-source-references-fixture",
                 "Joint reference/source mode has a different actual fixture contract")
            false_fields(item, ("actualPublisherReadyVerified",))
            reference_fields = ("referenceCallbacks", "referencesRooted", "metadataReads", "growthReadRestored",
                                "nestedOperations", "maximumCallbackDepth", "beforeNotices", "afterNotices", "abnormalNotices",
                                "retainedReferenceScopes", "retainedSourceTransactions", "unknown", "modeledEffects")
            need(all(type(item.get(key)) is int and item[key] >= 0 for key in reference_fields)
                 and item["referenceCallbacks"] > 0 and item["referencesRooted"] > 0 and item["metadataReads"] > 0
                 and item["maximumCallbackDepth"] >= 1 and item["beforeNotices"] >= 1 and item["modeledEffects"] > 0,
                 "Joint reference/source mode omits actual root-first callbacks, metadata or original effects")
            if item["mode"] == "references-grow":
                need(item["unknown"] == 0 and item["growthReadRestored"] == 1 and item["nestedOperations"] == 1
                     and item["maximumCallbackDepth"] >= 2 and item["afterNotices"] >= 1 and item["abnormalNotices"] == 0
                     and item["retainedReferenceScopes"] == item["retainedSourceTransactions"] == 0,
                     "Joint growth must restore the protected backing read, preserve nested callbacks and complete both source/reference scopes")
            elif item["mode"] == "references-fault":
                need(item["unknown"] == 1 and item["growthReadRestored"] == 1 and item["afterNotices"] >= 1
                     and item["abnormalNotices"] == 0 and item["retainedReferenceScopes"] == item["retainedSourceTransactions"] == 1,
                     "Joint observer fault must still deliver After and retain both exact reference/source responsibilities")
            elif item["mode"] == "references-abnormal":
                need(item["unknown"] == 1 and item["abnormalNotices"] == 1 and item["afterNotices"] == 0
                     and item["retainedReferenceScopes"] == item["retainedSourceTransactions"] == 1,
                     "Joint abnormal original must retain its abnormal notice and both scopes without inventing After")
            elif item["mode"] == "references-before-changed":
                need(item["beforeCommittedMutationChecked"] is True and item["unknown"] == 1
                     and item["afterNotices"] >= 1 and item["abnormalNotices"] == 1 and item["modeledEffects"] == 1
                     and item["retainedReferenceScopes"] == item["retainedSourceTransactions"] == 1,
                     "Joint reentrant Before mutation must retain the original source body and unfinished responsibility")
            else:
                need(item["afterCommittedMutationChecked"] is True and item["originalPopTokenPreserved"] is True
                     and item["originalRaxPreserved"] is True and item["redirects"] == 0 and item["unknown"] == 1
                     and item["afterNotices"] == 1 and item["abnormalNotices"] == 1 and item["modeledEffects"] == 1
                     and item["retainedReferenceScopes"] == item["retainedSourceTransactions"] == 1,
                     "Joint reentrant After mutation must preserve the original pop token/result and never redirect")
            continue
        need(item.get("kind") == "owned-native-tween-source-fixture"
             and (item.get("siteCount"), item.get("bindingCount"), item.get("recordBytes"), item.get("infoBytes")) == (22, 43, 192, 96)
             and 0 <= item.get("coveredBindings", -1) <= 43,
             "Actual owned source process has an incomplete ABI/execution contract")
        false_fields(item, ("fullStackAliasProof",))
        need(item.get("retainedRecords", -1) >= 0 and item["retainedRecords"] <= 8192
             and item["retainedRecords"] <= item.get("events", -1), "Owned source retained journal counts differ")
        if item["mode"] == "happy":
            need(item.get("unknown") == 0 and item.get("faults") == 0, "Normal owned source unexpectedly became Unknown")
        else:
            need(item.get("unknown") == 1 and item.get("faults", 0) > 0, "An abnormal owned source process did not retain Unknown")
    need(result.get("checks") == sum(item["checks"] for item in modes), "Owned source check total differs")
    happy = next(item for item in modes if item["mode"] == "happy")
    need(happy["coveredBindings"] == 43 and happy["events"] > 8192 and happy["retainedRecords"] == 8192
         and happy.get("pendingTransactions") == 0 and happy.get("pairedOperations", 0) > 4096
         and happy.get("modeledEffects", 0) > 4096 and happy.get("modeledGcBarrierCalls", 0) > 0,
         "Actual owned source happy mode omits bindings, uninterrupted modeled barriers or ring rollover")
    unfinished = next(item for item in modes if item["mode"] == "unfinished-window")
    need(unfinished.get("pendingTransactions", 0) > 0 and unfinished["events"] == unfinished["retainedRecords"] == 8192,
         "The owned source unfinished window did not retain its pending records")
    unbound = next(item for item in modes if item["mode"] == "unbound")
    need(unbound["events"] == unbound["retainedRecords"] == unbound.get("pairedOperations") == 0,
         "The unbound source fixture claimed admitted metadata or a transaction")
    return dict(nativeProcesses=13, jointReferenceProcesses=5, checks=result["checks"], sourceCount=22, bindings=43,
                recordBytes=192, infoBytes=96, actualPreservingEntryExecuted=True,
                exactArchivedInputs=14, productionCoreInputsMatched=11, controlledMetadataAndAdmission=True,
                productionHelperExecuted=False, productionReadyProved=False, officialMetadataVerified=False,
                actualGcVerified=False, gameRuntimeVerified=False, sourceArchiveVerified=True)


def validate_tween_retirement(evidence, compiled_sources):
    """Bind actual owned rundown executions to this helper's compiled bytes.

    The aggregate uses controlled source counters. Source preserving-entry and
    reference callback executions have separate proofs; none execute the game.
    """
    manifest = read_json(evidence / "tween-retirement-native-proof.json")
    need(manifest.get("schemaVersion") == 1 and manifest.get("kind") == "owned-tween-retirement-native-build"
         and manifest.get("passed") is True and manifest.get("sourceHashesUnchanged") is True
         and manifest.get("compileExitCode") == 0 and manifest.get("failure") is None,
         "Actual successful native retirement compilation/execution required")
    false_fields(manifest, ("gameExecuted", "productionHelperExecuted"))
    archive = evidence / "tween-retirement-native-sources.zip"
    need(manifest["sourceArchive"]["sha256"] == row(archive)["sha256"], "Retirement source archive changed")
    core = {"native/RizToolbox.JudgeContext/" + name for name in (
        "TweenRetirementContext.h", "TweenRetirementContext.cpp", "TweenAdmission.h", "TweenAdmission.cpp",
        "OperationContext.h", "OperationContext.cpp", "FirstInstall.h", "TweenSourceContext.h",
        "TweenSourceReferences.h", "TweenSourceReferences.cpp", "TweenSingleEventContext.h", "MidContext.h")}
    inputs = core | {"native/RizToolbox.JudgeContext/fixture/TweenRetirementFixture.cpp",
                     "scripts/Test-TweenRetirementNative.ps1"}
    sources = archived_sources(archive, manifest["sources"], inputs)
    for name in core:
        need(compiled_sources.get(name) == sources[name], "Retirement execution used different actual compiled production core bytes")
    runner = sources["scripts/Test-TweenRetirementNative.ps1"].decode("utf-8-sig")
    need("/DRIZ_TWEEN_RETIREMENT_FIXTURE" in runner and "/W4 /WX" in runner,
         "The retirement proof omitted its controlled strict native build seam")
    modes = manifest.get("modes", [])
    names = {"happy", "observer-fault", "reference-fault", "admission-fault", "reference-read-fault", "unconfigured"}
    names |= {f"observer-{phase}-{lane}" for phase in ("enter", "exit") for lane in range(6)}
    need(len(modes) == 18 and {item.get("mode") for item in modes} == names,
         "The retirement proof omits a required native race or fault process")
    for item in modes:
        need(item.get("kind") == "owned-tween-retirement-native-fixture" and item.get("passed") is True
             and item.get("failures") == 0 and type(item.get("checks")) is int and item["checks"] > 0
             and item.get("actualEntryReservations") is True and item.get("sourceCountersControlled") is True
             and item.get("actualAdmissionBatch") is (item["mode"] != "unconfigured"),
             "Native retirement results lost their actual reservation/admission or controlled-source scope")
        false_fields(item, ("gameExecuted",))
    happy = next(item for item in modes if item["mode"] == "happy")
    need(all(happy.get(name) is True for name in ("actualReferenceEnd", "allSixBeforeSnapshotRaces",
                                                "foreignOriginalInFlightDuringCommit", "singleInFlightBlocksAcquire", "singleCountersControlled")),
         "Native retirement omitted reference End, six pre-snapshot races, overlapping foreign original or controlled Single in-flight guards")
    return dict(nativeProcesses=18, checks=sum(item["checks"] for item in modes), sourceArchiveVerified=True,
                productionCoreInputsMatched=len(core), sourceCountersControlled=True, gameExecuted=False,
                actualEntryReservations=True, foreignOriginalInFlightDuringCommit=True)


def validate_tween_references(evidence, compiled_sources):
    manifest = read_json(evidence / "tween-references-native-proof.json")
    result = read_json(evidence / "tween-references-native-result.json")
    need(manifest.get("schemaVersion") == 1 and manifest.get("kind") == "owned-tween-source-references-native-build"
         and manifest.get("passed") is True and manifest.get("compileExitCode") == 0
         and manifest.get("sourceHashesUnchanged") is True and manifest.get("failure") is None,
         "Actual standalone reference compilation/execution required")
    false_fields(manifest, ("gameExecuted", "productionHelperExecuted", "officialMetadataVerified", "actualPublisherReadyVerified"))
    need(manifest["result"]["sha256"] == row(evidence / "tween-references-native-result.json")["sha256"],
         "Actual standalone reference result changed")
    archive = evidence / "tween-references-native-sources.zip"
    actual_archive = row(archive)
    need(all(manifest["sourceArchive"][name] == result["sourceArchive"][name] == actual_archive[name]
             for name in ("sha256", "bytes")), "Reference source/result archive relation changed")
    core = {"native/RizToolbox.JudgeContext/" + name for name in (
        "TweenRetirementContext.h", "TweenRetirementContext.cpp", "TweenSourceReferences.h", "TweenSourceReferences.cpp", "TweenSourceContext.h")}
    inputs = core | {"native/RizToolbox.JudgeContext/fixture/TweenSourceReferencesFixture.cpp", "scripts/Test-TweenSourceReferencesNative.ps1"}
    sources = archived_sources(archive, manifest["sourceFiles"], inputs)
    for name in core:
        need(compiled_sources.get(name) == sources[name], "Reference execution used different actual compiled production core bytes")
    need(result.get("kind") == "owned-tween-source-references-native-summary" and result.get("passed") is True
         and result.get("independentProcesses") == 9 and result.get("sourceHashesUnchanged") is True
         and result.get("failure") is None, "Nine actual independent reference processes required")
    false_fields(result, ("gameExecuted", "productionHelperExecuted", "officialMetadataVerified", "actualPublisherReadyVerified"))
    modes = result.get("modes", [])
    names = {"happy", "cpp", "seh", "nonzero", "original-cpp", "original-seh", "capacity", "unpaired", "empty"}
    need(len(modes) == 9 and {item.get("mode") for item in modes} == names, "The reference proof omitted a required execution")
    for item in modes:
        need(item.get("kind") == "owned-native-tween-source-references-fixture" and item.get("passed") is True
             and item.get("failures") == 0 and type(item.get("checks")) is int and item["checks"] > 0
             and item.get("controlledOwnedRoots") is True, "Actual reference callback/root process failed or changed scope")
        false_fields(item, ("gameCodeExecuted", "officialMetadataVerified", "actualPublisherReadyVerified", "hardwareCetVerified"))
    return dict(nativeProcesses=9, checks=result["checks"], sourceArchiveVerified=True,
                productionCoreInputsMatched=len(core), controlledOwnedRoots=True, gameExecuted=False)


def validate_single_profile(compiled_sources, profile_proof, game_pe):
    """Independently bind seven source spans to the compiled producer and pinned image."""
    publication = compiled_sources["native/RizToolbox.JudgeContext/TweenSingleEventPublication.inl"].decode("utf-8-sig")

    def array(name, count):
        match = re.search(r"constexpr\s+std::uint32_t\s+" + name + r"\[" + str(count) + r"\]\s*\{([^}]+)\}", publication)
        need(match is not None, "The frozen Single producer omits its actual span identity array")
        return tuple(int(value.strip(), 0) for value in match.group(1).split(",") if value.strip())

    need(array("singleSpanRva", 7) == SINGLE_SPAN_RVAS and array("singleSpanWidth", 7) == SINGLE_SPAN_WIDTHS
         and array("singleReturnRva", 3) == (0xBC110D, 0xBC1162, 0xBC1221)
         and re.search(r"singleAllocationSize\s*=\s*4096\s*;", publication),
         "The archived Single publisher differs from the fixed seven-span/4096-byte contract")
    expected = []
    previous_end = 0
    for index, (rva, width) in enumerate(zip(SINGLE_SPAN_RVAS, SINGLE_SPAN_WIDTHS)):
        data = game_pe.get_data(rva, width)
        need(0xBC106A <= rva < rva + width <= 0xBC1255 and rva >= previous_end and len(data) == width,
             "A Single source span is incomplete, overlapping or outside its verified continuation")
        expected.append(dict(index=index, rva=rva, width=width, bytesSha256=digest(data)))
        previous_end = rva + width
    need(profile_proof.get("singleEventPublisher") == dict(fileName="TweenSingleEventPublication.inl",
                                                         sha256=digest(compiled_sources["native/RizToolbox.JudgeContext/TweenSingleEventPublication.inl"]))
         and profile_proof.get("singleEventSpans") == expected,
         "The independent profile proof omits or changes an actual Single source span")
    return expected


def validate_single_publication(item, managed=False):
    """Compare native/CLR observed values, never derive actual writes from expectations."""
    value = item.get("singleEventPublication")
    need(isinstance(value, dict), "The actual V7 proof omits the Single publication section")
    fixed = dict(capabilityVersion=1, byteSize=416, operationId=25, kind=3, slot=31, expectedWrites=7,
                 configuredMask=127, publishedMask=127, attemptedWrites=7, completedWrites=7,
                 registeredFunctionCount=3, lookupChecks=15, boundaryChecks=15)
    need(all(type(value.get(name)) is int and value[name] == expected for name, expected in fixed.items()),
         "The actual Single section has incomplete shape, masks, native write counts or unwind checks")
    need(type(value.get("epoch")) is int and value["epoch"] > 0
         and type(value.get("parentEpoch")) is int and value["parentEpoch"] > 0
         and type(value.get("parentRowEpoch")) is int and value["parentEpoch"] == value["parentRowEpoch"],
         "The Single lane has no actual epoch or matching operation-row-25 parent epoch")
    for name, expected in (("spanRvas", SINGLE_SPAN_RVAS), ("spanWidths", SINGLE_SPAN_WIDTHS),
                           ("stubSizes", (21, 21, 21)), ("unwindSizes", (36, 36, 36))):
        observed = value.get(name)
        need(isinstance(observed, list) and all(type(part) is int for part in observed) and tuple(observed) == expected,
             "An observed Single source span, post stub or complete xdata extent differs")
    if managed:
        need(all(type(value.get(name)) is int for name in ("phase", "unknown", "blockBytesVerified", "sourceBytesVerified",
                                                         "nativeLaneConfigured", "nativeObserverAttached", "nativeParentsEntered", "nativeChildrenEntered"))
             and (value.get("phase"), value.get("unknown"), value.get("blockBytesVerified"), value.get("sourceBytesVerified"),
              value.get("nativeLaneConfigured"), value.get("nativeObserverAttached"), value.get("nativeParentsEntered"),
              value.get("nativeChildrenEntered")) == (3, 0, 4096, sum(SINGLE_SPAN_WIDTHS), 1, 0, 0, 0)
             and value.get("observationPhase") == "before-managed-observer-adoption"
             and value.get("actualGameCallbacksVerified") is False,
             "The actual CLR Single adoption omitted full memory verification or changed its readonly scope")
    else:
        need(all(type(value.get(name)) is int for name in ("allocationSize", "phase", "unknown", "reserved"))
             and value.get("parentEpochMatchesOperation25") is True and value.get("allocationSize") == 4096
             and value.get("phase") == 3 and value.get("unknown") == 0 and value.get("reserved") == 0
             and value.get("spanProtections") == [0x20] * 7,
             "The native Single section lost its Ready phase, parent identity or original source protections")
        need(item.get("singleSourceBytesVerified") is True and type(item.get("singleBlockBytesVerified")) is int
             and item["singleBlockBytesVerified"] == 4096 and item.get("singleUnwindVerified") is True
             and item.get("singleCountersZero") is True and item.get("singleSyntheticBoundaryChecks") == 15,
             "Actual mapped Single patch, complete-block seal, OS unwind or no-callback evidence is missing")
    return value


def validate_single_tamper_mode(item):
    mode = item["mode"]
    span, offset = SINGLE_SPAN_TAMPER_MODES.get(mode, (-1, -1))
    block = SINGLE_BLOCK_TAMPER_MODES.get(mode, -1)
    protection = SINGLE_PROTECTION_MODES.get(mode, -1)
    need((item.get("tamperedSingleSpan"), item.get("tamperedSingleOffset"), item.get("tamperedSingleBlockOffset"),
          item.get("tamperedSingleProtection")) == (span, offset, block, protection),
         "A mapped Single corruption does not identify its exact source byte, sampled block offset or protection")
    single = mode in SINGLE_TAMPER_MODES
    need(item.get("fixtureProtectionChanges") == (1 if protection >= 0 else 0)
         and item.get("fixtureTamperWrites") == (1 if mode != "normal" and protection < 0 else 0)
         and item.get("fixtureRestorations") == (1 if single else 0)
         and item.get("singlePermanentUnknownAfterRestore") is single,
         "A Single corruption omitted actual restoration/permanent Unknown or confused byte and protection mutations")


def validate_single_partial(native_result, build, helper_sha, compiled_sources):
    """Keep actual linked-object fault evidence distinct from production DLL execution."""
    proof = native_result.get("singlePartialPublication")
    need(isinstance(proof, dict) and proof.get("schemaVersion") == 1
         and proof.get("kind") == "single-partial-publication-linked-object-evidence"
         and proof.get("actualHelperObjectsLinked") is True and proof.get("productionDLLInvoked") is False
         and proof.get("boundedCompileTimeSeam") == "RIZ_FIRST_INSTALL_UNKNOWN_FIXTURE",
         "The V7 package omits bounded actual linked-object Single partial-publication evidence")

    def decode(name):
        encoded = proof.get(name + "RawBase64")
        need(isinstance(encoded, str) and len(encoded) <= 8 * 1024 * 1024, "Embedded partial-publication original is missing or unbounded")
        raw = base64.b64decode(encoded, validate=True)
        need(digest(raw) == proof.get(name + "Sha256"), "Embedded original partial-publication evidence bytes changed")
        value = json.loads(raw.decode("utf-8-sig"))
        need(isinstance(value, dict), "Embedded original partial-publication evidence is not a JSON object")
        return value

    manifest, result = decode("manifest"), decode("result")
    need(manifest.get("kind") == result.get("kind") == "first-install-atomic-unknown-snapshot-race"
         and manifest.get("fixtureCompileTimeMacro") == "RIZ_FIRST_INSTALL_UNKNOWN_FIXTURE"
         and manifest.get("productionHelperHasFixtureExport") is False
         and manifest.get("actualProducerSourceCompiledIntoIsolatedExe") is True and manifest.get("realHelperTransports") is True
         and manifest.get("sourceImagesUnchanged") is True and manifest.get("mappedImageOnly") is True
         and result.get("passed") is True and result.get("failures") == 0 and result.get("checks", 0) > 0
         and result.get("productionFirstInstallSourceExecuted") is True and result.get("fixtureCompileTimeSeam") is True
         and result.get("actualHelperObjectsLinked") is True and result.get("productionDLLInvoked") is False
         and result.get("mappedImageOnly") is True and result.get("writesStarted") == 64
         and result.get("v7UnknownVerified") is True and result.get("noAdditionalProducerWrite") is True,
         "Actual partial-publication producer compilation or complete Unknown observation is missing")
    false_fields(manifest, ("gameCodeExecuted", "originalInitInvoked", "firstInstallSafetyVerified", "gameRuntimeVerified"))
    false_fields(result, ("gameCodeExecuted", "gameInitExecuted", "firstInstallSafetyVerified", "gameRuntimeVerified"))

    expected_sources = {name for name in compiled_sources if name.startswith("native/RizToolbox.JudgeContext/")}
    observed_sources = proof.get("sourceFiles", [])
    need(len(observed_sources) == len(expected_sources) and {row.get("relativePath") for row in observed_sources} == expected_sources,
         "The embedded partial producer must freeze the complete same-helper native input set")
    for source in observed_sources:
        data = compiled_sources[source["relativePath"]]
        need(source.get("sha256") == digest(data) and source.get("bytes") == len(data),
             "Partial-publication compilation differs from the helper's independently archived native bytes")
    published_files = {}
    for row in build["files"]:
        name = Path(row["path"]).name
        need(name not in published_files, "Duplicate actual helper build artifact role")
        published_files[name] = row
    observed = manifest.get("files", [])
    for name in SINGLE_PARTIAL_OBJECTS:
        rows = [row for row in observed if Path(row["path"]).name == name]
        need(len(rows) == 1 and name in published_files and rows[0]["sha256"] == published_files[name]["sha256"],
             "A partial-publication EXE linked an object from a different actual helper build")
    helper_rows = [row for row in observed if Path(row["path"]).name == "RizToolbox.JudgeContext.dll"]
    need(helper_rows and all(row["sha256"] == helper_sha for row in helper_rows), "Partial-publication dependency copies differ from this helper")
    for name, expected in (("GameAssembly.dll", "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79"),
                           ("dobby.dll", "8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded"),
                           ("FirstInstall.cpp", digest(compiled_sources["native/RizToolbox.JudgeContext/FirstInstall.cpp"])),
                           ("FirstInstallUnknownFixture.cpp", digest(compiled_sources["native/RizToolbox.JudgeContext/fixture/FirstInstallUnknownFixture.cpp"]))):
        rows = [row for row in observed if Path(row["path"]).name == name]
        need(len(rows) == 1 and rows[0]["sha256"] == expected, "A partial-publication source/game/Dobby role differs from the frozen helper inputs")

    modes = result.get("singlePartialWriteModes", [])
    need(len(modes) == 7 and {row.get("mode") for row in modes} == {f"partial-single-{index}" for index in range(7)},
         "All seven actual Single partial-publication stop positions are required")
    for row in modes:
        index = row.get("stoppedAfterSpan")
        need(type(index) is int and 0 <= index < 7 and row.get("mode") == f"partial-single-{index}"
             and row.get("kind") == "first-install-single-partial-publication" and row.get("passed") is True
             and row.get("failures") == 0 and row.get("checks", 0) > 0
             and (row.get("phase"), row.get("unknown"), row.get("writesStarted"), row.get("singleAttemptedWrites"),
                  row.get("singleCompletedWrites"), row.get("singleConfiguredMask"), row.get("singlePublishedMask"))
             == (4, 1, 36 + index, 1 + index, 1 + index, 127, (1 << (index + 1)) - 1)
             and type(row.get("singleEpoch")) is int and row["singleEpoch"] > 0
             and type(row.get("parentEpoch")) is int and row["parentEpoch"] > 0 and row["parentEpoch"] == row.get("parentRowEpoch")
             and all(row.get(name) is True for name in ("resourcesRetained", "reentryDidNotWrite", "actualHelperObjectsLinked", "mappedImageOnly"))
             and row.get("gameCodeExecuted") is False and row.get("transportInvocations") == 0,
             "An actual Single partial publication lost its observed masks, completed writes, parent or retained responsibility")
    return dict(processes=7, linkedProductionObjects=len(SINGLE_PARTIAL_OBJECTS), compiledSourceInputsMatched=len(expected_sources),
                sourceBytesVerified=True, originalManifestSha256=proof["manifestSha256"], originalResultSha256=proof["resultSha256"],
                actualHelperObjectsLinked=True, productionDLLInvoked=False, boundedCompileTimeSeam=proof["boundedCompileTimeSeam"], gameCodeExecuted=False)


def validate_operation_package(package, evidence, managed, game_directory):
    """Bind the exclusive V7 helper, compiled sources and actual V7 adoption."""
    build = read_json(evidence / "operation-native-build.json")
    need(build.get("kind") == "judge-context-operation-native-build" and build.get("passed") is True
         and build.get("buildExitCode") == 0 and build.get("inputHashesUnchanged") is True
         and (build.get("publicationVersion"), build.get("operationCount"), build.get("expectedWrites"), build.get("tweenSourceCount")) == (7, 26, 64, 22),
         "A successful current operation helper build is required")
    false_fields(build, ("gameExecuted", "installed"))
    need((build.get("playbackAttestationVersion"), build.get("playbackAttestationHeaderBytes"),
          build.get("playbackAttestationRangeBytes"), build.get("playbackAttestationOriginalCount")) == (1, 120, 184, 28),
         "The current helper must declare the exact private playback attestation ABI")
    helper = Image(package / "BepInEx/plugins/RizToolbox/RizToolbox.JudgeContext.dll")
    helper_sha = digest(helper.data)
    helpers = [item for item in build["files"] if Path(item["path"]).name == helper.path.name]
    need(len(helpers) == 1 and helpers[0]["sha256"] == helper_sha, "Operation payload differs from its actual native build")
    sources_zip = evidence / "operation-native-sources.zip"
    need(row(sources_zip)["sha256"] == build["sourceArchive"]["sha256"], "Operation source archive differs")
    compiled_sources = archived_sources(sources_zip, build["sources"])
    build_script = compiled_sources["scripts/Build-JudgeContext.ps1"].decode("utf-8-sig")
    export_block = re.search(r"\$expectedExports\s*=\s*@\((.*?)\n\)", build_script, re.S)
    need(export_block is not None, "The actual compiled helper build has no complete export declaration")
    expected_exports = re.findall(r"'([A-Za-z0-9_]+)'", export_block.group(1))
    need(expected_exports == build["expectedExports"] and len(expected_exports) == len(set(expected_exports)) == 88,
         "Native V7 build must freeze all 88 helper exports in its actual build script")
    actual_exports = helper.exports(expected_exports, 88)
    need(PLAYBACK_ATTESTATION_EXPORTS.issubset(actual_exports)
         and PLAYBACK_ATTESTATION_SOURCE_INPUTS.issubset(compiled_sources),
         "The helper or its actual source archive omits the private playback attestation producer")
    need({"RizTweenRetirement_" + name for name in ("Acquire", "Verify", "VerifyReferenceRoots", "UnregisterBatch", "Release", "GetInfo")}
         <= set(actual_exports), "The current helper omits a required native retirement export")
    need({name for name in actual_exports if name.startswith("RizFirstInstall_GetInfo")} == {"RizFirstInstall_GetInfoV7"},
         "Only the current V7 helper publication reader may be shipped")
    need({name for name in actual_exports if name.startswith("RizTweenSingleEvent")} == SINGLE_EXPORTS,
         "The Single producer requires its six exact exports and no configuration/parent bypass")
    need(SINGLE_SOURCE_INPUTS.issubset(compiled_sources), "The actual helper archive omits compiled Single producer inputs")
    need(all(name in build_script for name in ("TweenSingleEventContext.cpp", "TweenSingleEventContext.obj",
                                              "TweenSingleEventEntry.asm", "TweenSingleEventEntry.obj"))
         and "-g '*.inl'" in build_script,
         "The actual build must compile/link Single C++ and MASM and freeze its included publication input")
    source_exports = {
        "RizTweenSourceContext_GetEntry", "RizTweenSourceContext_GetInfo", "RizTweenSourceContext_ReadRecord",
        "RizTweenSourceReferences_GetInfo", "RizTweenSourceReferences_ReadCurrentNotice",
    }
    source_publication_exports = {
        "RizFirstInstall_GetTweenSourceInfoV1", "RizFirstInstall_BindTweenSourceMetadata",
        "RizFirstInstall_AttachTweenSourceReferencesV1",
    }
    need({name for name in actual_exports if name.startswith("RizTweenSource")} == source_exports
         and source_publication_exports.issubset(actual_exports),
         "The five source exports or three FirstInstall source gates are incomplete or expose a configuration bypass")
    profile = compiled_sources["native/RizToolbox.JudgeContext/FirstInstallProfile.h"]
    tween_profile = compiled_sources["native/RizToolbox.JudgeContext/FirstInstallTweenProfile.h"]
    need('#include "FirstInstallTweenProfile.h"' in profile.decode("utf-8-sig"),
         "The actual compilation profile does not include its Tween bodies")
    producer = compiled_sources["native/RizToolbox.JudgeContext/FirstInstall.cpp"].decode("utf-8-sig")
    header = compiled_sources["native/RizToolbox.JudgeContext/FirstInstall.h"].decode("utf-8-sig")
    need("sizeof(RizFirstInstallInfoV7) == 2528" in header
         and "sizeof(RizTweenSingleEventPublicationV1) == 416" in header
         and "offsetof(RizFirstInstallInfoV7, operations) == 448" in header
         and "offsetof(RizFirstInstallInfoV7, singleEvent) == 2112" in header
         and "operationCapabilityVersion = 4" in producer
         and not re.search(r"RIZFI_EXPORT[^\n]*RizFirstInstall_GetInfo(?:V2|V3|V4|V5|V6)?\(", producer)
         and '#include "TweenSingleEventPublication.inl"' in producer
         and all(name in producer for name in ("PrepareSingleEvent", "CommitSingleEvent", "SinglePublishedIdentity"))
         and all(name in producer for name in ("RizTweenSourcePreparePublication", "RizTweenSourceCommitPublication",
                                               "RizTweenSourceVerifyPublication", "RizTweenSourceGetPublishedPatch")),
         "The archived operation producer is not the exclusive V7 implementation with Single/source publication")
    export_proof = read_json(evidence / "operation-export-validation.json")
    need(row(evidence / "operation-export-validation.json")["sha256"] == build["actualExportValidation"]["sha256"]
         and export_proof.get("kind") == "judge-context-actual-export-validation" and export_proof.get("passed") is True
         and export_proof["helper"]["sha256"] == helper_sha
         and export_proof["exports"] == sorted(actual_exports)
         and (export_proof.get("publicationVersion"), export_proof.get("operationCount"), export_proof.get("expectedWrites"),
              export_proof.get("tweenSourceCount")) == (7, 26, 64, 22), "Actual helper PE export evidence differs")
    false_fields(export_proof, ("gameExecuted", "installed"))
    profile_proof = read_json(evidence / "operation-profile-verification.json")
    need(profile_proof.get("kind") == "latest-first-install-operation-profile-verification" and profile_proof.get("passed") is True
         and (profile_proof.get("operationCount"), profile_proof.get("expectedWrites")) == (26, 64)
         and profile_proof["profile"]["sha256"] == digest(profile)
         and profile_proof.get("additionalProfiles") == [dict(fileName="FirstInstallTweenProfile.h", sha256=digest(tween_profile))],
         "Independent twenty-six-operation source profile proof differs")
    false_fields(profile_proof, ("gameExecuted", "installed", "productionAdmissionVerified"))
    game = ordinary(game_directory / "GameAssembly.dll").read_bytes()
    need(digest(game) == profile_proof["gameAssembly"]["sha256"] == "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79",
         "The operation profile requires the actual pinned game image")
    game_pe = pefile.PE(data=game, fast_load=True)
    arrays = {name: bytes(int(value.strip(), 0) for value in values.split(",") if value.strip())
              for name, values in re.findall(r"unsigned char\s+(Operation\d+(?:Continuation\d+)?(?:Bytes|Unwind))\[\]\s*\{([^}]*)\}",
                                             profile.decode("utf-8-sig") + "\n" + tween_profile.decode("utf-8-sig"))}
    exception = game_pe.OPTIONAL_HEADER.DATA_DIRECTORY[3]
    pdata = game_pe.get_data(exception.VirtualAddress, exception.Size)
    need(exception.Size % 12 == 0 and len(pdata) == exception.Size, "The pinned game static RF table is incomplete")
    runtime_functions = {}
    for offset in range(0, len(pdata), 12):
        function = struct.unpack_from("<III", pdata, offset)
        runtime_functions.setdefault(function[0], []).append(function)
    need(len(profile_proof["operations"]) == 26, "The actual profile proof omits operations")
    for index, operation in enumerate(profile_proof["operations"]):
        body = arrays[f"Operation{index}Bytes"]
        unwind = arrays[f"Operation{index}Unwind"]
        need(operation["id"] == index and operation["bodyBytes"] == operation["endRva"] - operation["rva"]
             and len(body) == max(32, operation["bodyBytes"])
             and runtime_functions.get(operation["rva"]) == [(operation["rva"], operation["endRva"], operation["unwindRva"])]
             and game_pe.get_data(operation["rva"], len(body)) == body
             and digest(body[:operation["bodyBytes"]]) == operation["bodySha256"]
             and game_pe.get_data(operation["unwindRva"], len(unwind)) == unwind
             and digest(unwind) == operation["unwindSha256"], "Actual game body/unwind does not match the archived helper profile")
    continuation_pattern = r"\{\s*(\d+),\s*(\d+),\s*(0x[0-9a-fA-F]+),\s*(0x[0-9a-fA-F]+),\s*(0x[0-9a-fA-F]+),\s*Operation25Continuation(\d+)Bytes,\s*sizeof\(Operation25Continuation\6Bytes\),\s*Operation25Continuation\6Unwind,\s*sizeof\(Operation25Continuation\6Unwind\)\s*\}"
    continuation_rows = [tuple(int(value, 0) for value in match) for match in re.findall(continuation_pattern, profile.decode("utf-8-sig"))]
    need(continuation_rows == [(25, 0, 0xBC106A, 0xBC1255, 0x1D4E3B4, 0),
                               (25, 1, 0xBC1255, 0xBC1271, 0x1D4E3D8, 1),
                               (25, 2, 0xBC1271, 0xBC1277, 0x1D4E3FC, 2)],
         "The compiled Single event profile omits its three exact continuation RFs")
    continuation_facts = []
    previous_end = 0xBC106A
    parent = (0xBC0FF0, 0xBC106A, 0x1D4E3A0)
    for operation, index, begin, end, unwind_rva, array_id in continuation_rows:
        body = arrays[f"Operation25Continuation{array_id}Bytes"]
        unwind = arrays[f"Operation25Continuation{array_id}Unwind"]
        need(len(unwind) >= 16, "A compiled continuation omits its complete CHAININFO")
        chain_offset = (4 + unwind[2] * 2 + 3) & ~3
        need(previous_end == begin and end - begin == len(body) and game_pe.get_data(begin, len(body)) == body
             and runtime_functions.get(begin) == [(begin, end, unwind_rva)]
             and game_pe.get_data(unwind_rva, len(unwind)) == unwind and unwind[0] == 0x21
             and len(unwind) == chain_offset + 12 and struct.unpack_from("<III", unwind, chain_offset) == parent,
             "An actual Single continuation body, static RF or complete CHAININFO parent differs")
        continuation_facts.append(dict(operationId=operation, index=index, rva=begin, endRva=end,
                                       unwindRva=unwind_rva, bodyBytes=len(body), bodySha256=digest(body),
                                       unwindBytes=len(unwind), unwindSha256=digest(unwind),
                                       parentRva=parent[0], parentEndRva=parent[1], parentUnwindRva=parent[2]))
        previous_end = end
    need(profile_proof.get("operationContinuations") == continuation_facts
         and profile_proof.get("singleEventFunctionBytes") == previous_end - parent[0] == 647,
         "The independent profile proof omits actual compiled Single continuation identities")
    single_spans = validate_single_profile(compiled_sources, profile_proof, game_pe)
    source_profile = compiled_sources["native/RizToolbox.JudgeContext/TweenSourceProfile.h"].decode("utf-8-sig")
    source_arrays = {int(index): bytes(int(value.strip(), 0) for value in values.split(",") if value.strip())
                     for index, values in re.findall(r"unsigned char\s+Site(\d+)Bytes\[\]\s*\{([^}]*)\}", source_profile)}
    source_rows = re.findall(r"\{\s*(\d+),\s*(0x[0-9a-f]+),\s*(0x[0-9a-f]+),\s*(0x[0-9a-f]+),\s*Site(\d+)Bytes,\s*sizeof\(Site(\d+)Bytes\)\s*\}", source_profile)
    need(len(source_rows) == 22 and set(source_arrays) == set(range(22)), "The compiled source profile omits exact replay spans")
    source_spans = []
    for index, values in enumerate(source_rows):
        identity, begin, end, dead, array_id, size_id = (int(value, 0) for value in values)
        data = source_arrays[index]
        need(identity == array_id == size_id == index and end - begin == len(data) and len(data) >= 5
             and game_pe.get_data(begin, len(data)) == data, "A fixed game source replay span differs from the compiled profile")
        parent = profile_proof["operations"][22 if index == 21 else 20 if index >= 19 else 23]
        need(parent["rva"] <= begin < end <= parent["endRva"] and (dead == 0 or parent["rva"] <= dead < parent["endRva"]),
             "A source span or dead continuation leaves its actual primary function")
        source_spans.append((begin, end))
    ordered_sources = sorted(source_spans)
    need(all(left[1] <= right[0] for left, right in zip(ordered_sources, ordered_sources[1:])), "Compiled source replay spans overlap")
    native_manifest = read_json(evidence / "operation-native-proof.json")
    native_result = read_json(evidence / "operation-native-result.json")
    native_helpers = [item for item in native_manifest["files"] if Path(item["path"]).name == helper.path.name]
    need(native_manifest.get("kind") == "fixed-mapped-image-first-install-publication"
         and native_manifest.get("sourceImagesUnchanged") is True and len(native_helpers) == 1
         and native_helpers[0]["sha256"] == helper_sha and native_result.get("passed") is True
         and native_result.get("failures") == 0 and native_result.get("independentProcesses") == 182,
         "All 182 actual current-helper mapped publication processes are required")
    false_fields(native_manifest, ("gameCodeExecuted", "originalInitInvoked", "firstInstallSafetyVerified", "gameRuntimeVerified"))
    need(native_manifest.get("mappedImageOnly") is True
         and native_manifest.get("mappingMode") == "LoadLibraryExW DONT_RESOLVE_DLL_REFERENCES",
         "Mapped publication proof must retain the non-executing image mapping contract")
    modes = [native_result] + native_result["tamperModes"]
    tween_modes = {"tamper-tween-play": (20, 3575), "tamper-tween-add": (21, 267),
                   "tamper-tween-execute": (22, 5907), "tamper-tween-update": (23, 5171), "tamper-tween-awake": (24, 321)}
    source_modes = {f"tamper-tween-source-{index}": index for index in range(22)}
    source_block_modes = {"tamper-tween-source-relay", "tamper-tween-source-unwind", "tamper-tween-source-table"}
    single_metadata_modes = {"tamper-single-event-unwind": 1, "tamper-single-event-chain": 2, "tamper-single-event-pdata": 3}
    expected_modes = {
         "normal", "tamper-source", "tamper-post", "tamper-relay", "tamper-chain", "tamper-operation", "tamper-delegate-remove",
         "tamper-single-event", *single_metadata_modes, *tween_modes, *source_modes, *source_block_modes} | SINGLE_TAMPER_MODES
    need(len(modes) == len(expected_modes) == 182 and {item["mode"] for item in modes} == expected_modes,
         "The native proof must include every Single source byte, exact sampled block boundary, protection and prior corruption")
    need(native_result.get("singleBlockTamperOffsets") == list(SINGLE_BLOCK_TAMPER_OFFSETS)
         and native_result.get("singleSourceByteTamperCount") == sum(SINGLE_SPAN_WIDTHS)
         and native_result.get("singleBlockSealBytes") == 4096 and native_result.get("singleBlockTamperIsExhaustive") is False,
         "The Single proof must preserve its exact sampled offsets and distinguish full seal verification from exhaustive fault injection")
    need(native_result.get("allProcessChecks") == sum(item.get("checks", 0) for item in modes), "Mapped source-publication check total differs")
    for item in modes:
        validate_single_publication(item)
        validate_single_tamper_mode(item)
        expected_single_continuation = 2 if item["mode"] == "tamper-single-event" else 1 if item["mode"] in single_metadata_modes else -1
        need(item.get("singleEventContinuationsVerified") == 3
             and item.get("tamperedSingleContinuation") == expected_single_continuation
             and item.get("tamperedSingleMetadata") == single_metadata_modes.get(item["mode"], 0),
             "The actual Single continuation body/unwind/parent/pdata mutation identity differs")
        need(item.get("passed") is True and item.get("failures") == 0 and item.get("checks", 0) > 0
             and (item.get("operationReceiptVersion"), item.get("operationRowsVerified"), item.get("writesStarted")) == (7, 26, 64)
             and item.get("invalidV7HeaderCases") == 17 and item.get("v7UnknownVerified") is True
             and (item.get("tweenSourceRowsVerified"), item.get("tweenSourceBindingsVerified"), item.get("tweenSourceFunctionsVerified")) == (22, 43, 56)
             and item.get("tweenSourceCallbacksExecuted") == item.get("operationCallbacksExecuted") == 0
             and item.get("gameCodeExecuted") is False and item.get("gameRuntimeVerified") is False
             and item.get("mappedImageOnly") is True, "An actual V7 native source-publication mode is missing or inconsistent")
        need(item.get("tamperedTweenSourceSite") == source_modes.get(item["mode"], -1), "Source tamper proof does not identify the exact source site")
        need(item.get("phase") == 3 and item.get("unknown") == 0
             and item.get("currentPhase") == 4 and item.get("currentUnknown") == 1 and item.get("currentWritesStarted") == 64
             and item.get("noAdditionalProducerWrite") is True and item.get("v2ReadyVerified") is True and item.get("v2UnknownVerified") is True
             and item.get("tamperDetected") is (item["mode"] != "normal"), "Mapped mode did not retain exact Ready/Unknown or tamper provenance")
        expected_tween = tween_modes.get(item["mode"])
        observed_tween = (item.get("tamperedOperationId"), item.get("tamperedOperationBodyOffset"), item.get("tweenFullBodyTailTamperVerified"))
        if item["mode"] == "tamper-single-event":
            need(observed_tween == (25, 646, False)
                 and item.get("singleEventFullBodyTailTamperVerified") is True,
                 "The Single-event full-body tamper proof does not identify its exact changed byte")
        else:
            need(item.get("singleEventFullBodyTailTamperVerified") in (False, None)
                 and observed_tween == ((*expected_tween, True) if expected_tween else (-1, 0, False)),
                 "A Tween full-body tamper proof does not identify its exact actual changed byte")
    partial_publication = validate_single_partial(native_result, build, helper_sha, compiled_sources)
    adoption = read_json(evidence / "operation-managed-proof.json")
    need(adoption.get("kind") == "mapped-image-managed-preinit-adoption-summary" and adoption.get("passed") is True
         and adoption.get("failures") == 0 and adoption.get("independentProcesses") == 2
         and (adoption.get("publicationVersion"), adoption.get("operationCount"), adoption.get("expectedWrites")) == (7, 26, 64)
         and adoption["helperSha256"] == helper_sha and adoption["sourceSnapshot"]["sha256"] == managed["sourceSnapshot"]["sha256"]
         == row(evidence / "managed-source-snapshot.json")["sha256"], "Actual CLR adoption must match the payload helper and compiled managed source snapshot")
    false_fields(adoption, ("gameRuntimeVerified", "firstInstallSafetyVerified", "installed"))
    need(len(adoption["modes"]) == 2 and {item["mode"] for item in adoption["modes"]} == {"happy", "reentry"}, "Both actual CLR adoption modes are required")
    for item in adoption["modes"]:
        observed_single = validate_single_publication(item, managed=True)
        operation = item["operationAdoption"]
        need(item["passed"] is True and item["failures"] == 0 and item["writesStarted"] == 64
             and item["listenerPublicationVersion"] == 7 and operation["ProductionV7Verified"] is True
             and item.get("productionListenerReceiptV7Verified") is True and item.get("metadataIdentityControlled") is True
             and all(operation.get(name) is True for name in ("SingleEventPublicationVerified", "SingleEventMemoryVerified", "SingleEventReadonlyAdoption"))
             and operation["DeclaredChannels"] == operation["ReadOnlyChannels"] == 26
             and operation["AttachedChannels"] == (26 if item["mode"] == "happy" else 0)
             and operation["UnchangedInstructions"] is True and operation["ZeroEngineInstallCalls"] is True,
             "The actual managed V7 operation and Single readonly adoption is inconsistent")
        false_fields(item, ("gameRuntimeVerified", "firstInstallSafetyVerified", "listenerObserverAttached", "listenerCallbacksVerified", "nativeToClrCallbackVerified"))
        false_fields(operation, ("ActualGameCallbacksVerified",))
        bridge = item.get("singleEventBridgeAdoption")
        if item["mode"] == "happy":
            need(isinstance(bridge, dict)
                 and all(type(bridge.get(name)) is int for name in ("parentCapacity", "epoch", "parentEpoch", "attached", "inFlight", "nativeParentsEntered", "nativeChildrenEntered"))
                 and (bridge.get("parentCapacity"), bridge.get("epoch"), bridge.get("parentEpoch"), bridge.get("attached"),
                      bridge.get("inFlight"), bridge.get("nativeParentsEntered"), bridge.get("nativeChildrenEntered"))
                 == (128, observed_single["epoch"], observed_single["parentEpoch"], 1, 0, 0, 0)
                 and all(bridge.get(name) is True for name in ("sharedOwner", "activityKnown", "survivedClrCollection"))
                 and bridge.get("actualGameCallbacksVerified") is False,
                 "Actual happy CLR Single bridge adoption must retain one shared idle native observer across collection")
        else:
            need("singleEventBridgeAdoption" in item and bridge is None,
                 "The reentry refusal cannot claim a new Single managed observer adoption")
    need(adoption.get("checks") == sum(item.get("checks", 0) for item in adoption["modes"]), "Actual managed adoption check total differs")
    snapshot = read_json(evidence / "managed-source-snapshot.json")
    need(snapshot.get("passed") is True and snapshot.get("runnerExitCode") == 0
         and snapshot["sourceArchive"]["sha256"] == row(evidence / "managed-source-snapshot.zip")["sha256"],
         "The compiled managed snapshot source archive differs")
    child = snapshot.get("child", {})
    actual_tests = [item for item in child.get("artifacts", [])
                    if item.get("path", "").replace("\\", "/").split("/")[-4:]
                    == ["checks", "Runtime", "bin", "RizToolbox.Runtime.Tests.dll"]]
    need(child.get("passed") is True and child.get("sourceHashesUnchanged") is True
         and len(actual_tests) == 1 and actual_tests[0].get("bytes", 0) > 0
         and adoption.get("managedAssemblySha256") == actual_tests[0]["sha256"],
         "The adopted actual Runtime test assembly differs from the compiled source snapshot")
    with zipfile.ZipFile(evidence / "managed-source-snapshot.zip") as archive:
        declared = {}
        for item in snapshot["snapshotFiles"]:
            path = Path(item["path"])
            parts = path.parts
            positions = [index for index, part in enumerate(parts) if part == "source-snapshot"]
            need(len(positions) == 1, "A compiled source has no unambiguous snapshot root")
            name = "/".join(parts[positions[0] + 1:])
            need(name not in declared, "A compiled source is repeated")
            declared[name] = item
        need(len(archive.namelist()) == len(declared) and set(archive.namelist()) == set(declared), "The compiled managed source archive omits, duplicates or adds inputs")
        for name, item in declared.items():
            data = archive.read(name)
            need(digest(data) == item["sha256"] and len(data) == item["bytes"], "Compiled managed source byte differs")
        publication = archive.read("src/RizToolbox.Runtime/Hooks/NativeOperationPublication.cs").decode("utf-8-sig")
        need("Count = 26" in publication and "NativeFirstInstallInformationV7" in publication
             and "OperationCapabilityVersion != 4" in publication and "ExpectedWrites != 64" in publication,
             "The packaged current managed code does not use the twenty-six-operation V7 contract")
        owner = archive.read("src/RizToolbox.Runtime/Hooks/NativeFirstInstall.cs").decode("utf-8-sig")
        single_shape = archive.read("src/RizToolbox.Runtime/Hooks/NativeSingleEventPublication.cs").decode("utf-8-sig")
        single_memory = archive.read("src/RizToolbox.Runtime/Hooks/NativeFirstInstall.SingleEvent.cs").decode("utf-8-sig")
        single_adoption = archive.read("tests/RizToolbox.Runtime.Tests/NativeFirstInstallMappedRegression.SingleEvent.cs").decode("utf-8-sig")
        need('"RizFirstInstall_GetInfoV7"' in owner and '"RizFirstInstall_GetInfoV6"' not in owner
             and "Marshal.SizeOf<NativeFirstInstallInformationV7>() != 2528" in owner
             and owner.count("VerifySingleEventMemory(") >= 2
             and "value.ByteSize != 416" in single_shape and "value.ParentEpoch != parent.Epoch" in single_shape
             and "value.AttemptedWrites != 7" in single_shape and "value.CompletedWrites != 7" in single_shape
             and "Enumerable.Repeat((byte)0xcc, 4096)" in single_memory
             and "actual.SequenceEqual(expected)" in single_memory and "RtlLookupFunctionEntry" in single_memory
             and "producer.VerifyReady()" in single_adoption and "native.ParentsEntered == 0" in single_adoption
             and "native.ChildrenEntered == 0" in single_adoption,
             "The compiled CLR snapshot omits actual V7 owner, full Single reconstruction or readonly adoption")
    admission = validate_tween_admission(evidence, build, helper_sha, compiled_sources)
    sources = validate_tween_sources(evidence, compiled_sources)
    retirement = validate_tween_retirement(evidence, compiled_sources)
    references = validate_tween_references(evidence, compiled_sources)
    return dict(helperSha256=helper_sha, publicationVersion=7, publicationBytes=2528, operationCapabilityVersion=4,
                operationCount=26, expectedWrites=64, tweenSourceCount=22, helperExportCount=88,
                playbackAttestation=dict(version=1, headerBytes=120, rangeBytes=184, originals=28,
                                         actualWrites=64, copiedRowsConferAuthority=False, productionAdmission=False),
                singleEventProfileSpans=single_spans,
                tweenAdmission=admission, tweenSources=sources,
                tweenRetirement=retirement, tweenReferences=references,
                singleEvent=dict(capabilityVersion=1, sectionBytes=416, spanCount=7, sourceBytes=sum(SINGLE_SPAN_WIDTHS),
                                 blockBytes=4096, nativeMappedProcesses=182, sourceByteTamperProcesses=len(SINGLE_SPAN_TAMPER_MODES),
                                 sampledBlockOffsets=list(SINGLE_BLOCK_TAMPER_OFFSETS), blockTamperIsExhaustive=False,
                                 protectionTamperProcesses=len(SINGLE_PROTECTION_MODES), clrReadonlyAdoptionProcesses=2,
                                 clrBridgeAdoptionProcesses=1, partialPublication=partial_publication, actualGameCallbacksVerified=False),
                mappedNativeProcesses=182, mappedManagedProcesses=2, sourceArchivesVerified=True)


def validate_package(directory, game_directory):
    """Relocated package check: no access to the producer's original directories."""
    root = directory.resolve(strict=True)
    package = root / "package"
    evidence = root / "evidence"
    manifest = read_json(root / "transport-candidate-package-manifest.json")
    need(manifest.get("kind") == "engine-transport-production-candidate-package" and manifest.get("schemaVersion") == 1
         and manifest.get("payloadScope") == "full" and manifest.get("publicationVersion") == 5, "A complete V5 transport package is required")
    false_fields(manifest, ("gameExecuted", "installed"))
    admitted = bool(manifest.get("productionAdmissionGranted", False))
    need(manifest.get("executionWindowProved") is admitted, "Relocated package executionWindowProved must match productionAdmissionGranted")
    need((manifest.get("operationPublicationVersion"), manifest.get("operationPublicationBytes"), manifest.get("operationCount"),
          manifest.get("operationExpectedWrites"), manifest.get("tweenSourceCount"), manifest.get("helperExportCount")) == (7, 2528, 26, 64, 22, 88)
         and (manifest.get("playbackAttestationVersion"), manifest.get("playbackAttestationHeaderBytes"),
              manifest.get("playbackAttestationRangeBytes"), manifest.get("playbackAttestationOriginalCount")) == (1, 120, 184, 28)
         and (manifest.get("singleEventCapabilityVersion"), manifest.get("singleEventSectionBytes"),
              manifest.get("singleEventSpanCount"), manifest.get("singleEventExpectedWrites")) == (1, 416, 7, 7),
         "The current package requires the exclusive V7 helper and its complete Single publication")
    base_manifest = read_json(evidence / "base-package-manifest.json")
    mods = selected_mod_paths(base_manifest)
    expected_payload = PAYLOAD_PATHS | mods
    need(len(manifest["files"]) == len(expected_payload) and {x["path"] for x in manifest["files"]} == expected_payload,
         "The relocated V5 runtime and selected plugin payload must match exactly")
    need(len(manifest["evidence"]) == len(EVIDENCE_PATHS) == 31 and {item["path"] for item in manifest["evidence"]} == EVIDENCE_PATHS,
         "The relocated package requires the exact thirty-one evidence files")
    for collection, base in ((manifest["files"], package), (manifest["evidence"], evidence)):
        seen = set()
        for item in collection:
            relative = item["path"]
            need(isinstance(relative, str) and re.fullmatch(r"[A-Za-z0-9_.\-/]+", relative)
                 and "\\" not in relative and not Path(relative).is_absolute() and all(p not in ("", ".", "..") for p in relative.split("/"))
                 and relative not in seen, "Unsafe or duplicate package path")
            seen.add(relative)
            actual = row(base / relative)
            need(actual["sha256"] == item["sha256"] and actual["bytes"] == item["bytes"], "Relocated package hash/length differs")
    base = base_manifest
    base_version = base.get("schemaVersion")
    need(base_version in (2, 3), "Unknown base package manifest schema")
    base_rows = {}
    for item in base["files"] + (base.get("resources", []) if base_version == 3 else []):
        path = item["path"]
        need(path not in base_rows, "Duplicate base package path")
        base_rows[path] = item["sha256"]
    payload_rows = {item["path"]: item["sha256"] for item in manifest["files"]}
    for target in sorted(set(PLAYBACK_PATHS) | mods):
        sources = [target]
        if base_version == 3 and "packageKind" not in base and target in MOD_PATHS:
            suffix = target.removeprefix(MOD_ROOT)
            sources += [f"author/examples/packages/{suffix}", f"validation/proof/packages/{suffix}"]
        source = next((path for path in sources if path in base_rows), None)
        need(source is not None and base_rows[source] == payload_rows[target],
             f"Inherited base package hash differs: {target}")
    expected_files = {"transport-candidate-package-manifest.json"} | {"package/" + name for name in expected_payload} | {"evidence/" + name for name in EVIDENCE_PATHS}
    actual_files = set()
    for path in root.rglob("*"):
        need(not is_linked(path), "Linked relocated package entry refused")
        if path.is_file():
            actual_files.add(path.relative_to(root).as_posix())
    need(actual_files == expected_files, "Relocated package omits or adds declared payload/evidence bytes")
    final = read_json(evidence / "final-native-validation.json")
    operations = validate_operation_package(package, evidence, read_json(evidence / "transport-managed-build.json"), game_directory)
    need(final.get("passed") is True and final.get("finalGraphComplete") is True and final.get("nativeProfile") == 2
         and final.get("sourceGateClosed") is (not admitted), "Successful final production PE evidence required")
    false_fields(final, ("gameExecuted", "installed", "targetImagesLoaded", "nativePublicationReadyProved"))
    need(final.get("productionAdmissionGranted") is admitted and final.get("productionAdmissionVerified") is admitted,
         "Final validation admission flag differs from package")
    if admitted:
        need(final.get("bootstrapRequiredImportsVerified") is True, "Admitted package final validation requires bootstrap PE import verification")
    images = [Image(package / BOOT), Image(package / KERNEL), Image(game_directory / "UnityPlayer.dll"),
              Image(game_directory / "Rizline.exe"), Image(package / "winhttp.dll")]
    for i, image in enumerate(images):
        need(digest(image.data) == final["images"][i]["sha256"] and final["images"][i]["index"] == i
             and final["images"][i]["module"] == image.path.name, "Payload/game PE does not match its exact final independently validated role")
    need(digest(images[1].data) == KERNEL_SHA and all(digest(images[i].data) == GAME[images[i].path.name] for i in (2, 3)),
         "Pinned fixed-game/Kernel identity differs")
    exps = [images[0].exports(API, 4), images[1].exports(KN, 35), images[2].exports(), None, images[4].exports(count=82)]
    need(images[0].bytes(exps[0][API[0]], 6) == bytes.fromhex("b805000000c3"), "Payload GetVersion is not ABI5")
    edges = []
    for src, dst, names in ((3, 2, ("UnityMain2",)), (2, 4, WINHTTP), (4, 0, API), (0, 1, KN)):
        actual = images[src].imports(images[dst].path.name, names)
        for name in names:
            ilt, iat = actual[name]
            edges.append(dict(source=src, destination=dst, lookupRva=ilt, iatRva=iat, exportRva=exps[dst][name], symbol=name))
    payload = b"".join(struct.pack("<5I", x["source"], x["destination"], x["lookupRva"], x["iatRva"], x["exportRva"])
                       + x["symbol"].encode("ascii") + b"\0" for x in edges)
    need(edges == final["edges"] and digest(payload) == final["dependencyGraphSha256"], "Relocated actual final 44-edge graph differs")
    generated = (evidence / "NativeEngineTransportProfile.Generated.cs").read_text(encoding="utf-8-sig")
    found_hashes = re.findall(r'"([0-9a-f]{64})"', generated)
    need(found_hashes[:5] == [digest(x.data) for x in images]
         and [int(x, 16) for x in re.findall(r"0x[0-9a-fA-F]+", generated)] == list(RVAS), "Relocated managed profile five pins/13 RVAs differ")
    if admitted:
        need(len(found_hashes) == 8 and ", true, " in generated, "Relocated admitted managed profile lacks Stage A hashes")
    with zipfile.ZipFile(evidence / "production-native-sources.zip") as archive:
        gate = archive.read("native/RizToolbox.EngineBootstrap/EngineTransportProfile.h").decode("utf-8-sig")
        need("Generated::ProductionFirstInstallAuthorized" in gate
             and "Generated::InteriorEntryClosureVerified" in gate
             and "Generated::ProductionColdTraceVerified" in gate
             and "static_assert(!Owned||!ProductionFirstInstallAuthorized);" in gate,
             "The archived actual compiled production gate lacks Stage A verification bindings")
    return dict(schemaVersion=1, kind="engine-transport-relocated-package-validation", passed=True,
                operationPublication=operations,
                finalGraphComplete=True, dependencyGraphSha256=digest(payload), publicationVersion=5, nativeProfile=2,
                productionAdmissionGranted=admitted, targetImagesLoaded=False, gameExecuted=False, installed=False)

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    scope = parser.add_mutually_exclusive_group(required=True)
    scope.add_argument("--build-directory", type=Path)
    scope.add_argument("--package-directory", type=Path)
    parser.add_argument("--game-directory", type=Path)
    parser.add_argument("--relocated-input", action="append", nargs=2, type=Path, default=[])
    parser.add_argument("--output-json", type=Path)
    parser.add_argument("--print-json", action="store_true")
    args = parser.parse_args()
    directory = args.build_directory or args.package_directory
    need(directory.is_absolute(), "Absolute input directory required")
    if args.package_directory:
        need(not args.relocated_input, "Relocated build inputs apply only to the build directory")
        need(args.game_directory is not None and args.game_directory.is_absolute(), "Actual absolute game directory required for relocated 44-edge validation")
        result = validate_package(directory, args.game_directory)
        game = args.game_directory.resolve()
    else:
        result = validate(directory, args.game_directory, args.relocated_input)
        game = Path(result["images"][2]["path"]).parent
    need(args.output_json is not None or args.print_json, "A fresh evidence output or JSON stdout is required")
    if args.output_json:
        need(args.output_json.is_absolute() and not args.output_json.exists(), "Fresh absolute output required")
        output = args.output_json.resolve()
        need(directory.resolve() not in output.parents and game not in output.parents,
             "Validation evidence must stay outside the immutable candidate and game installation")
        args.output_json.parent.mkdir(parents=True, exist_ok=True)
        with args.output_json.open("x", encoding="utf-8", newline="\n") as stream:
            json.dump(result, stream, indent=2)
            stream.write("\n")
    if args.print_json:
        print(json.dumps(result))
    else:
        print(f"Final production V5 disk contract verified: 5 images, 44 resolved edges; "
              f"productionAdmissionGranted={result['productionAdmissionGranted']}.")

if __name__ == "__main__":
    main()
