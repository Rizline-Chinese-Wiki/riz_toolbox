"""Independently decode the actual static Doorstop cold-caller evidence.

This verifier reads disk images and raw records only. It never loads a target
DLL, starts a child, patches an image, installs a package, or sends a request.
"""
import argparse
import hashlib
import importlib.util
import json
import re
import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
FORWARD = HERE.parent / "foundation-winhttp-forward-20261025/verify-winhttp-forward.py"
FORWARD_SHA = "eab34bfffe42056a53712d4accef1ce94576e8781a298be01710be47acce3dd2"
API_NAMES = ["WinHttpOpen", "WinHttpGetProxyForUrl", "WinHttpGetIEProxyConfigForCurrentUser", "WinHttpCloseHandle"]
PHASES = [4, 5, 6, 7, 11]


def require(value, message):
    if not value:
        raise ValueError(message)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def forward_reader():
    require(sha(FORWARD.read_bytes()) == FORWARD_SHA, "Historical forward verifier bytes changed")
    spec = importlib.util.spec_from_file_location("frozen_forward_for_cold_proof", FORWARD)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def directory(image, index):
    data = Path(image["path"]).read_bytes()
    require(sha(data) == image["sha256"], "Held PE image bytes changed")
    opt = struct.unpack_from("<I", data, 0x3c)[0] + 24
    require(struct.unpack_from("<I", data, opt + 108)[0] > index, "Required PE directory missing")
    return struct.unpack_from("<II", data, opt + 112 + index * 8)


def section_at(image, rva, size):
    return next((s for s in image["sections"] if s["rva"] <= rva
                 and rva + size <= s["rva"] + max(s["rawSize"], s["virtualSize"])), None)


def cfg_contract(lib, image):
    rva, size = directory(image, 10)
    require(rva and size >= 148, "Owned caller must retain actual CFG load configuration")
    data = lib.rva_bytes(image, rva, 148)
    declared = struct.unpack_from("<I", data)[0]
    check, dispatch, table, count = struct.unpack_from("<4Q", data, 112)
    flags = struct.unpack_from("<I", data, 144)[0]
    require(declared >= 148 and check and dispatch and table and count and flags & 0x100 and flags & 0x400,
            "Owned caller lost CFG instrumentation or its actual guarded function table")
    require(count <= 65536 and image["imageBase"] <= table < image["imageBase"] + image["imageSize"],
            "CFG function table is not bounded image data")
    stride = 4 + ((flags >> 28) & 15)
    rows = lib.rva_bytes(image, table - image["imageBase"], count * stride)
    targets = [struct.unpack_from("<I", rows, i * stride)[0] for i in range(count)]
    require(targets == sorted(set(targets)) and all(section_at(image, x, 1)
            and section_at(image, x, 1)["characteristics"] & 0x20000000 for x in targets),
            "Actual CFG table contains duplicate, unsorted or non-executable targets")
    return dict(directoryRva=rva, flags=flags, checkPointerVa=check, dispatchPointerVa=dispatch,
                functionTableVa=table, functionCount=count, functionRvas=targets)


def ordinary_edge(image, target, expected):
    edges = [x for x in image["imports"] if x["module"].casefold() == target.casefold()]
    require(len(edges) == 1 and edges[0]["symbols"]
            and sorted(x.get("name", "") for x in edges[0]["symbols"]) == sorted(expected)
            and all("ordinal" not in x for x in edges[0]["symbols"])
            and not any(x["module"].casefold() == target.casefold() for x in image["delayImports"]),
            "Missing or changed exact ordinary named dependency: " + image["name"] + " -> " + target)
    return dict(source=image["name"], target=target, symbols=edges[0]["symbols"])


def map_rva(text, name, image):
    values = [int(m[1], 16) - image["imageBase"] for line in text.splitlines()
              if (m := re.match(r"^\s+[0-9A-Fa-f]{4}:[0-9A-Fa-f]{8,16}\s+" + re.escape(name)
                                + r"\s+([0-9A-Fa-f]{16})\b", line))]
    require(len(values) == 1 and 0 < values[0] < image["imageSize"], "Missing/ambiguous actual MAP symbol: " + name)
    return values[0]


def inspect_images(binary):
    lib = forward_reader()
    root = binary.resolve() if binary.is_dir() else binary.resolve().parent
    require(root.is_absolute() and root.is_dir(), "--binary must identify an absolute built fixture directory")
    contract_path, manifest_path = root / "cold-contract.json", root / "cold-build-manifest.json"
    header_path = root / "ColdProxySymbols.h"
    contract, manifest = lib.read_json(contract_path), lib.read_json(manifest_path)
    require(contract["kind"] == "production-doorstop-cold-disk-contract" and contract["passed"] is True
            and contract["sourceReaderSha256"] == FORWARD_SHA and contract["historicalPeReaderSha256"] == lib.PE_READER_SHA256,
            "Disk contract does not bind the pinned independent PE reader")
    require(manifest["kind"] == "production-doorstop-cold-build" and manifest["schemaVersion"] == 1
            and manifest["publicExports"] == 82 and manifest["fixedSystemWinHttp"] is True and manifest["dllEntryProfile"] is True
            and all(manifest[k] is False for k in ("fixtureExecuted", "gameExecuted", "installed")),
            "Builder manifest is not the explicit unexecuted candidate")
    parse = lib.pinned_pe_reader()
    paths = dict(proxy=root / "winhttp.dll", importer=root / "WinHttpCold.Importer.dll", host=root / "WinHttpCold.Host.exe",
                 baseline=Path(contract["baseline"]["path"]), system=lib.system_winhttp_path())
    require(all(p.is_absolute() and p.is_file() for p in paths.values()), "Every PE proof input must be an exact existing absolute file")
    images = {name: parse(path) for name, path in paths.items()}
    for name in ("proxy", "baseline"):
        require(images[name] == contract[name], "Current exact PE bytes differ from the generated cold contract")
    proxy = images["proxy"]
    actual = lib.export_shape(proxy, [x["name"] for x in images["baseline"]["exports"]])
    require(len(actual) == 82 and {(x["name"], x["ordinal"]) for x in proxy["exports"]}
            == {(x["name"], x["ordinal"]) for x in images["baseline"]["exports"]},
            "All 82 real baseline names and ordinals must remain exact")
    require(proxy["entryRva"] > 0 and proxy["characteristics"] & 0x2000 and proxy["tls"] is None
            and not any(x.startswith("Riz") for x in actual), "Actual Doorstop entry or private-export boundary differs")
    stubs = lib.inspect_tail_stubs(proxy, actual)
    require([s["exportRva"] for s in stubs] == contract["exportRvas"]
            and stubs[0]["activePointerRva"] == contract["activePointerRva"], "Four real tail stubs differ from their generated header contract")
    map_path = Path(contract["mapPath"])
    require(map_path.resolve() == (root / "doorstop/doorstop.map").resolve()
            and sha(map_path.read_bytes()) == contract["mapSha256"], "Exact candidate MAP changed")
    map_text = map_path.read_text(encoding="utf-8-sig")
    internal = dict(getInfoRva=map_rva(map_text, "RizWinHttpForward_GetInfo", proxy),
                    doorstopRecordRva=map_rva(map_text, "RizWinHttpDoorstopRecord", proxy),
                    attachRva=map_rva(map_text, "RizWinHttpDoorstopAttach", proxy))
    require(all(contract[k] == v for k, v in internal.items()), "Generated MAP RVAs differ from the actual held map")
    for key, size, required, forbidden in (("getInfoRva", 1, 0x60000000, 0x80000000),
            ("attachRva", 1, 0x60000000, 0x80000000), ("doorstopRecordRva", 1304, 0xc0000000, 0x20000000)):
        section = section_at(proxy, internal[key], size)
        require(section and section["characteristics"] & required == required and not section["characteristics"] & forbidden,
                "Internal MAP function/record has incorrect full-range section permissions")
    header = '#pragma once\n#include <cstdint>\nnamespace RizColdSymbols {\n'
    for name, value in (("GetInfoRva", internal["getInfoRva"]), ("DoorstopRecordRva", internal["doorstopRecordRva"]),
            ("AttachRva", internal["attachRva"]), ("ImageSize", proxy["imageSize"]), ("TimeDateStamp", proxy["timeDateStamp"])):
        header += f"inline constexpr std::uint32_t {name}=0x{value:x}u;\n"
    header += 'inline constexpr std::uint32_t ExportRvas[4]={' + ','.join(f'0x{x:x}u' for x in contract["exportRvas"]) + '};\n'
    header += 'inline constexpr unsigned char FileSha256[32]={' + ','.join(f'0x{x:02x}' for x in bytes.fromhex(proxy["sha256"])) + '};\n}\n'
    require(header_path.read_bytes() == header.encode() and sha(header_path.read_bytes()) == contract["generatedHeaderSha256"],
            "Generated exact-image header no longer matches all MAP RVAs and candidate SHA256")
    edges = [ordinary_edge(images["host"], images["importer"]["name"], ["RizColdImporter_GetSnapshot", "RizColdImporter_RunOutsideLoader"]),
             ordinary_edge(images["importer"], "winhttp.dll", API_NAMES)]
    require(images["host"]["entryRva"] and images["importer"]["entryRva"] and images["importer"]["tls"]
            and images["importer"]["tls"]["callbacks"], "Actual static importer must retain real entry and TLS initializer callbacks")
    for name in ("host", "importer"):
        image = images[name]
        require(not image["delayImports"], "Static caller must not substitute delay-loaded dependencies")
        symbols = [s.get("name", "") for d in image["imports"] for s in d["symbols"]]
        require("RizWinHttpForward_Initialize" not in symbols and "RizWinHttpDoorstopAttach" not in symbols,
                "Host/importer may observe but cannot initialize the production resolver")
        if name == "host":
            require(not any(d["module"].casefold() == "winhttp.dll" for d in image["imports"])
                    and not any(s.startswith("WinHttp") for s in symbols), "Host cannot import WinHTTP directly before its static importer")
    require(not any(d["module"].casefold() in (images["host"]["name"].casefold(), images["importer"]["name"].casefold(), "winhttp.dll")
                    for d in proxy["imports"] + proxy["delayImports"]), "Proxy acquired a circular importer/WinHTTP ordinary dependency")
    cfg = {name: cfg_contract(lib, images[name]) for name in ("host", "importer")}
    system_exports = {x["name"]: x for x in images["system"]["exports"]}
    require(all(n in system_exports and system_exports[n]["rva"] and not system_exports[n]["forwarder"] for n in API_NAMES),
            "Actual System32 DLL must expose all four direct named EAT targets")
    provenance = []
    for row in manifest["files"] + manifest["sources"]:
        path = Path(row["path"])
        require(path.is_absolute() and path.is_file() and sha(path.read_bytes()) == row["sha256"], "Builder input/output hash changed: " + str(path))
        provenance.append(dict(path=str(path.resolve()), sha256=row["sha256"]))
    for path in (contract_path, manifest_path, header_path, map_path, FORWARD, Path(lib.PE_READER_PATH)):
        provenance.append(dict(path=str(path.resolve()), sha256=sha(path.read_bytes())))
    for image in images.values():
        image["fileIdentity"] = lib.disk_identity(image)
    return dict(kind="production-doorstop-cold-pe-proof", schemaVersion=1, passed=True, images=images,
                contract=contract, contractPath=str(contract_path), contractSha256=sha(contract_path.read_bytes()),
                tailStubs=stubs, internalSymbols=internal, staticEdges=edges, cfg=cfg,
                provenance=provenance, systemApiExports=[dict(kind=k, **system_exports[n]) for k, n in enumerate(API_NAMES)],
                forwardReaderSha256=FORWARD_SHA, historicalPeReaderSha256=lib.PE_READER_SHA256,
                targetDllLoadedByVerifier=False, childrenStartedByVerifier=0, gameExecuted=False, installed=False)


def decode_info(lib, data):
    require(len(data) == 1248, "Info raw length differs")
    if not any(data):
        value = lib.decode_info(struct.pack("<2I", 1, 1248) + data[8:])
        value["version"] = value["byteSize"] = 0
        return value
    return lib.decode_info(data)


def decode_entry(lib, data):
    require(len(data) == 1304, "Actual DllEntry record must be exactly 1304 bytes")
    entry = dict(zip(("version", "byteSize", "stage", "attempts", "threadId", "initializeResult", "infoResult",
                      "entryLastError", "initializeLastError", "exitLastError", "readyChecksPassed", "failure"), struct.unpack_from("<12I", data)))
    entry["selfModule"], = struct.unpack_from("<Q", data, 48)
    entry["info"] = decode_info(lib, data[56:])
    return entry


def decode_phase(lib, data):
    require(len(data) == 2592, "Actual phase proof must be exactly 2592 bytes")
    value = dict(zip(("phase", "threadId", "infoResult", "lastErrorBefore", "lastErrorAfter", "reserved"), struct.unpack_from("<6I", data)))
    value["proxyBase"], value["getInfoAddress"] = struct.unpack_from("<2Q", data, 24)
    value["info"] = decode_info(lib, data[40:1288])
    value["entryRecord"] = decode_entry(lib, data[1288:])
    return value


def ready_info(info, proxy_base, system_base, proof, check):
    lib = forward_reader()
    image, system = proof["images"]["proxy"], proof["images"]["system"]
    addresses = [system_base + x["rva"] for x in proof["systemApiExports"]]
    check(info["version"] == 1 and info["byteSize"] == 1248 and info["state"] == 2 and info["phase"] == 7
          and info["flags"] == 63 and info["initializeAttempts"] == info["publishCount"] == 1 and info["resolvedCount"] == 4
          and info["failure"] == info["unknown"] == info["lastWin32Error"] == info["faultStage"] == info["faultIndex"] == 0,
          "Actual canonical resolver is complete Ready after exactly one successful attempt/publication")
    check(info["selfModule"] == proxy_base and info["systemModule"] == system_base and system_base != proxy_base
          and info["heldSystemFile"] > 0 and info["addresses"] == addresses and info["protections"] == [0x20] * 4,
          "Live Info binds all four exact System32 named EAT targets and their executable protections")
    check(len({info[k] for k in ("initialTable", "poisonTable", "resolvedTable")}) == 3
          and info["activeTable"] == info["resolvedTable"] and all(info[k] % 8 == 0 for k in ("initialTable", "poisonTable", "resolvedTable")),
          "All three retained tables remain distinct aligned identities and only complete resolved slots are active")
    initial, poison = (lib.table_from_disk(image, proxy_base, info[k]) for k in ("initialTable", "poisonTable"))
    check(initial == poison, "Distinct read-only initial/poison tables bind the same four real typed refusal functions")
    pointer, = struct.unpack("<Q", lib.rva_bytes(image, proof["tailStubs"][0]["activePointerRva"], 8))
    check(pointer - image["imageBase"] == info["initialTable"] - proxy_base, "Actual initial table matches the held tail stubs' disk pointer initializer")
    resolved_section = section_at(image, info["resolvedTable"] - proxy_base, 32)
    check(resolved_section and resolved_section["characteristics"] & 0xc0000000 == 0xc0000000
          and not resolved_section["characteristics"] & 0x20000000, "Actual resolved four-slot range is writable non-executable proxy image data")
    identity = system["fileIdentity"]
    check(info["fileIndex"] == identity["fileIndex"] and info["fileVolumeSerial"] == identity["volumeSerial"]
          and info["fileBytes"] == identity["bytes"], "Native retained System32 file identity equals the independently held disk image")
    check(lib.normalized_path(info["systemPath"]) == lib.normalized_path(system["path"])
          and lib.normalized_path(info["loadedPath"]) == lib.normalized_path(system["path"])
          and info["loadFlags"] == 0x800, "Resolver loaded the exact fixed System32 path with the fixed system-search flag")
    export_rva, export_bytes = directory(system, 0)
    check((info["peMachine"], info["peImageSize"], info["peTimeDateStamp"], info["peExportRva"], info["peExportBytes"])
          == (system["machine"], system["imageSize"], system["timeDateStamp"], export_rva, export_bytes),
          "Loaded System32 PE header and export directory agree with independently parsed disk metadata")
    return initial, poison


def normal_phase(value, phase, main_thread, proxy_base, system_base, proof, check):
    check(value["phase"] == phase and value["threadId"] == main_thread and value["reserved"] == 0
          and value["infoResult"] == 0 and value["lastErrorBefore"] == value["lastErrorAfter"]
          and value["proxyBase"] == proxy_base and value["getInfoAddress"] == proxy_base + proof["internalSymbols"]["getInfoRva"],
          "Actual initializer/host observation uses the main thread and exact hash-bound private MAP function")
    ready_info(value["info"], proxy_base, system_base, proof, check)
    entry = value["entryRecord"]
    check((entry["version"], entry["byteSize"], entry["stage"], entry["attempts"]) == (1, 1304, 4, 1)
          and entry["threadId"] == main_thread and entry["selfModule"] == proxy_base
          and entry["initializeResult"] == entry["infoResult"] == entry["failure"] == 0 and entry["readyChecksPassed"] == 1
          and entry["entryLastError"] == entry["initializeLastError"] == entry["exitLastError"],
          "Actual PROCESS_ATTACH wrapper completed Ready before this initializer and preserved incoming LastError")
    check(entry["info"] == value["info"], "Completed DllEntry snapshot and later live Info remain exactly identical")


def verify_case(args):
    lib = forward_reader()
    raw, proof = lib.read_json(args.case), lib.read_json(args.pe_proof)
    assertions = []
    def check(value, message):
        require(value, message)
        assertions.append(message)
    check(proof["kind"] == "production-doorstop-cold-pe-proof" and proof["schemaVersion"] == 1 and proof["passed"] is True
          and proof["forwardReaderSha256"] == FORWARD_SHA and proof["historicalPeReaderSha256"] == lib.PE_READER_SHA256,
          "Case binds the independently generated exact cold PE proof and unchanged historical reader")
    for row in proof["provenance"]:
        check(sha(Path(row["path"]).read_bytes()) == row["sha256"], "Current exact build input/output remains equal to the parent proof")
    for image in proof["images"].values():
        check(lib.disk_identity(image) == image["fileIdentity"], "Actual current PE disk bytes and file identity still match the held parent proof")
    check(raw["schemaVersion"] == 1 and raw["kind"] == "winhttp-cold-fixture" and raw["actualProcessId"] == args.child_pid > 0
          and raw["mainThreadId"] > 0 and raw["sehCode"] == 0 and raw["checks"] > 0,
          "Case is the independently owned actual child with a real main thread and no native exception")
    check(lib.normalized_path(raw["arguments"]["output"]) == lib.normalized_path(str(args.case.resolve())), "Actual child writes the exact parent-selected case artifact")
    check(all(raw["scope"][k] is False for k in ("httpRequestsIssued", "autoDetectOrPacEnabled", "settingsModified", "privateConfigStringsLogged",
          "hostInitializedResolver", "importerInitializedResolver", "gameExecuted", "installed")) and raw["scope"]["trueStaticDependencyChain"] is True,
          "Fixture declares its actual static dependency chain and limited local observation workload")
    controlled = raw["controlledInputs"]
    check(controlled == dict(agent="RizToolbox.OwnedWinHttpFixture", invalidUrl="::riz-owned-invalid-url::", openAccessType=1, normalOpenFlags=0,
          fifthArgumentProbeFlags=0x40000000, autoProxyFlags=0, autoDetectFlags=0, autoConfigUrl=0, reservedPointer=0, reserved=0, autoLogon=0),
          "Only the fixed no-proxy/local-invalid-input workload is selected")
    importer_meta = raw["importerSnapshot"]
    data = lib.referenced_bytes(args.case, importer_meta, 16832)
    version, size, mask, count, checks, failures = struct.unpack_from("<4I2Q", data)
    check((version, size, mask) == (1, 16832, 0x8f0) and checks > 0
          and all(importer_meta[k] == v for k, v in zip(("version", "byteSize", "phaseMask", "rowCount", "checks", "failures"),
                                                       (version, size, mask, count, checks, failures))),
          "Importer JSON transport header equals its complete actual 16832-byte snapshot")
    phases = [decode_phase(lib, data[32 + i * 2592:32 + (i + 1) * 2592]) for i in range(5)]
    check(importer_meta["phases"] == phases, "All five phase JSON objects match every independently decoded raw native field")
    check([v["phase"] for v in phases] == PHASES and all(v["threadId"] == raw["mainThreadId"] for v in phases),
          "Actual TLS, C, C++, DllMain and outside-loader observations are all recorded on the main thread")
    calls = raw["calls"]
    check(raw["callsSnapshot"]["rowByteSize"] == 320 and raw["callsSnapshot"]["rowCount"] == len(calls), "Canonical call transport count/size match actual JSON rows")
    call_data = lib.referenced_bytes(args.case, raw["callsSnapshot"], len(calls) * 320)
    if args.expect == "identity-refusal":
        check(raw["passed"] is False and raw["finished"] is False and raw["error"] == "Cold importer rejected selected production proxy contract"
              and raw["failures"] and raw["outsideResult"] == 1 and raw["initialImporter"]["phaseMask"] == 0xf0
              and raw["initialImporter"]["rowCount"] == count == 0 and raw["initialImporter"]["failures"] > 0 and failures > 0,
              "Wrong/missing proxy identity is deliberately refused in all real phases before any hidden API call")
        check(not calls and not raw["pairs"] and not raw["infoSnapshots"] and not call_data and not any(data[12992:]),
              "Identity refusal contains zero WinHTTP target calls, comparison pairs or fabricated native call rows")
        check(len({v["proxyBase"] for v in phases}) == 1 and phases[0]["proxyBase"] > 0
              and all(v["infoResult"] == 0xffffffff and v["getInfoAddress"] == v["lastErrorBefore"] == v["lastErrorAfter"] == v["reserved"] == 0
                      and not any(data[32 + i * 2592 + 40:32 + (i + 1) * 2592]) for i, v in enumerate(phases)),
              "Rejected identity never dereferences the private MAP RVA; all live Info and DllEntry record bytes remain zero")
        check(all(raw[k] == dict(base=0, path="", fileVolumeSerial=0, fileIndex=0) for k in ("forwardModule", "systemModule", "importerModule"))
              and raw["additionalReference"] == 0 and len(raw["slots"]) == 4
              and all(s["kind"] == i and all(v == 0 for k, v in s.items() if k != "kind") for i, s in enumerate(raw["slots"])),
              "Host exits from explicit importer rejection before recording any accepted proxy/system identity or extra reference")
        return dict(kind="production-doorstop-cold-case-proof", schemaVersion=1, passed=True, expect=args.expect, actualChildPid=args.child_pid,
                    expectedChildExitCode=1, checks=len(assertions), assertions=assertions, hostRawSha256=sha(args.case.read_bytes()),
                    peProofSha256=sha(args.pe_proof.read_bytes()), apiCalls=0, directForwardPairs=0, importerPhases=5,
                    rejectedProxyBase=phases[0]["proxyBase"], hiddenMapAddressInvoked=False, childrenStartedByVerifier=0,
                    productionV4Issued=False, wholeSystemLoaderSafetyVerified=False, gameExecuted=False, installed=False)
    check(raw["passed"] is True and raw["finished"] is True and not raw["error"] and not raw["failures"] and raw["outsideResult"] == 0
          and raw["initialImporter"] == dict(phaseMask=0xf0, rowCount=8, failures=0) and count == 12 and failures == 0,
          "Normal actual child completes all true initializers and the outside phase without assertions or exceptions")
    images = proof["images"]
    for key, name in (("forwardModule", "proxy"), ("systemModule", "system"), ("importerModule", "importer")):
        lib.verify_module(raw[key], images[name], check)
    proxy_base, system_base = raw["forwardModule"]["base"], raw["systemModule"]["base"]
    for value, phase in zip(phases, PHASES):
        normal_phase(value, phase, raw["mainThreadId"], proxy_base, system_base, proof, check)
        check(value["lastErrorBefore"] == 0x6c610000 + phase, "Importer live snapshot preserves its exact phase-specific LastError sentinel")
    ready, entry = phases[0]["info"], phases[0]["entryRecord"]
    check(all(v["info"] == ready and v["entryRecord"] == entry for v in phases), "All true initializers observe the same completed one-time DllEntry result")
    expected_labels = ["after-main", "after-workers", "after-additional-reference"]
    check([v["label"] for v in raw["infoSnapshots"]] == expected_labels and raw["additionalReference"] == proxy_base,
          "Actual host captures all required boundaries and its additional absolute reference resolves the same proxy")
    for index, snapshot in enumerate(raw["infoSnapshots"]):
        value = decode_phase(lib, lib.referenced_bytes(args.case, snapshot, 2592))
        check(value == snapshot["proof"], "Host phase JSON matches all 2592 independently decoded native bytes")
        normal_phase(value, 11, raw["mainThreadId"], proxy_base, system_base, proof, check)
        check(value["lastErrorBefore"] == 0x6c620000 + index and value["info"] == ready and value["entryRecord"] == entry,
              "Worker activity and an extra LoadLibrary reference cannot repeat initialization or alter the completed record")
        initial, poison = ready_info(value["info"], proxy_base, system_base, proof, check)
        check(snapshot["initialSlots"] == initial and snapshot["poisonSlots"] == poison
              and snapshot["resolvedSlots"] == snapshot["activeSlots"] == ready["addresses"],
              "Actual host memory slots agree with the independently bound read-only tables and all four resolved targets")
    check(len(raw["slots"]) == 4, "All four actual resolved memory-page observations are present")
    for k, slot in enumerate(raw["slots"]):
        check(slot["kind"] == k and slot["address"] == ready["addresses"][k] and slot["allocationBase"] == system_base
              and slot["state"] == 0x1000 and slot["type"] == 0x1000000 and slot["protect"] == ready["protections"][k] == 0x20
              and slot["error"] == 0 and slot["base"] <= slot["address"] < slot["base"] + slot["regionSize"],
              "Actual named system export belongs to the held System32 executable MEM_IMAGE page")
    check(len(calls) in (144, 146) and len(raw["pairs"]) == (len(calls) - 12) // 2,
          "Normal workload retains exactly all importer rows and 66/67 complete direct-forward pairs")
    forward_addresses = [proxy_base + x["exportRva"] for x in proof["tailStubs"]]
    for index, row in enumerate(calls):
        decoded = lib.decode_call_row(call_data[index * 320:(index + 1) * 320])
        check(all(row[k] == v for k, v in decoded.items()), "Every call JSON scalar, argument and private-output field equals its actual 320 native bytes")
        lib.validate_call(row, forward_addresses, ready["addresses"], raw["mainThreadId"], check)
    check(sorted(v["sequence"] for v in calls) == list(range(1, len(calls) + 1)), "Global importer/host/worker sequences contain no duplicates, missing rows or truncation")
    imported = [v for v in calls if v["route"] == "importer"]
    check(len(imported) == 12 and [v["sequence"] for v in imported] == list(range(1, 13)), "Importer owns precisely the first 12 real call sequence numbers")
    for i, row in enumerate(imported):
        decoded = lib.decode_call_row(data[12992 + 320 * i:12992 + 320 * (i + 1)])
        check(all(row[k] == v for k, v in decoded.items()) and row["pairId"] == row["worker"] == 0,
              "Importer rows independently match both snapshot transports and have no fabricated comparison/worker identity")
    for phase in PHASES[:4]:
        group = [v for v in imported if v["phase"] == phase]
        check([v["kind"] for v in group] == [3, 1] and [v["seed"] for v in group] == [0x6c710000 + phase, 0x6c710001 + phase],
              "Each actual loader initializer executes exactly Close(NULL) then local-invalid Proxy(NULL)")
    outside = [v for v in imported if v["phase"] == 11]
    check([v["kind"] for v in outside] == [0, 2, 1, 3] and [v["seed"] for v in outside] == list(range(0x6c71000b, 0x6c71000f))
          and outside[0]["rawReturn"] > 0 and outside[2]["rawReturn"] == 0 and outside[3]["rawReturn"] > 0
          and outside[2]["args"][0] == outside[3]["args"][0] == outside[0]["rawReturn"],
          "Importer positive four-API workload occurs outside loader and closes its actual own successful session")
    host = [v for v in calls if v["route"] != "importer"]
    check(sorted(v["sequence"] for v in host) == list(range(13, len(calls) + 1)) and all(v["pairId"] > 0 for v in host),
          "All host/worker calls start at sequence 13 and belong to real comparison pairs")
    pair_ids = set()
    for pair in raw["pairs"]:
        group = [v for v in host if v["pairId"] == pair["id"]]
        check(pair["id"] not in pair_ids and len(group) == 2 and {v["route"] for v in group} == {"direct", "forward"},
              "Each comparison has exactly one actual direct and one actual forwarded call")
        direct, forwarded = (next(v for v in group if v["route"] == route) for route in ("direct", "forward"))
        check(pair["equal"] is True and pair["kind"] == direct["kind"] == forwarded["kind"]
              and pair["directSequence"] == direct["sequence"] and pair["forwardSequence"] == forwarded["sequence"]
              and direct["worker"] == forwarded["worker"] and direct["threadId"] == forwarded["threadId"]
              and lib.equivalent_calls(direct, forwarded), "Independent direct-forward comparison preserves full BOOL/Open return semantics, LastError and freed private outputs")
        pair_ids.add(pair["id"])
    check(pair_ids == set(range(1, len(raw["pairs"]) + 1)) and {v["pairId"] for v in host} == pair_ids,
          "Every expected direct-forward pair has one contiguous identity with no omitted or extra call")
    workers = [v for v in host if v["worker"]]
    check({v["worker"] for v in workers} == {1, 2, 3} and len({v["threadId"] for v in workers}) == 3
          and all(len([v for v in workers if v["worker"] == w]) == 40
                  and len({v["threadId"] for v in workers if v["worker"] == w}) == 1 for w in (1, 2, 3)),
          "Three distinct actual worker threads each complete all four five-pair typed rounds")
    for worker in (0, 1, 2, 3):
        for route in ("direct", "forward"):
            group = [v for v in host if v["worker"] == worker and v["route"] == route]
            seeds = [0x6b400000] if worker == 0 else [0x7b000000 + worker * 0x10000 + i * 0x100 for i in range(4)]
            for seed in seeds:
                rows = sorted((v for v in group if seed <= v["seed"] < seed + 5), key=lambda v: v["sequence"])
                check([v["kind"] for v in rows] == [0, 2, 1, 3, 3] and [v["seed"] for v in rows] == list(range(seed, seed + 5))
                      and rows[0]["rawReturn"] > 0 and rows[2]["rawReturn"] == 0 and rows[3]["rawReturn"] > 0 and rows[4]["rawReturn"] == 0
                      and rows[2]["args"][0] == rows[3]["args"][0] == rows[0]["rawReturn"] and rows[4]["args"][0] == 0,
                      "Every real direct/forward round retains Open/IE/Proxy/associated Close/Close(NULL) order and its own session")
    probes = [v for v in host if v["kind"] == 0 and v["openFlags"] == 0x40000000]
    check(len(probes) == 2 and {v["route"] for v in probes} == {"direct", "forward"}
          and all(v["worker"] == 0 and v["seed"] == 0x6b410000 for v in probes), "Exactly one full five-argument Open probe pair preserves the nonzero stack argument")
    cleanup = [v for v in host if v["label"] == "cleanup-if-platform-accepts-nonzero-flags"]
    check(len(cleanup) == (2 if any(v["rawReturn"] for v in probes) else 0) and len(calls) == 144 + len(cleanup),
          "Fifth-argument cleanup occurs exactly when actual platform return values require it")
    for opened in probes:
        if opened["rawReturn"]:
            closed = [v for v in cleanup if v["route"] == opened["route"]]
            check(len(closed) == 1 and closed[0]["kind"] == 3 and closed[0]["args"][0] == opened["rawReturn"]
                  and closed[0]["rawReturn"] > 0 and closed[0]["sequence"] > opened["sequence"], "Any actually accepted fifth-argument session is closed through its own exact route")
    return dict(kind="production-doorstop-cold-case-proof", schemaVersion=1, passed=True, expect=args.expect, actualChildPid=args.child_pid,
                expectedChildExitCode=0, checks=len(assertions), assertions=assertions, hostRawSha256=sha(args.case.read_bytes()),
                peProofSha256=sha(args.pe_proof.read_bytes()), apiCalls=len(calls), directForwardPairs=len(pair_ids), importerPhases=5,
                actualDllEntryBeforeImporterVerified=True, additionalReferenceDoesNotReinitialize=True,
                systemImageSha256=images["system"]["sha256"], proxyImageSha256=images["proxy"]["sha256"],
                childrenStartedByVerifier=0, productionV4Issued=False, wholeSystemLoaderSafetyVerified=False, gameExecuted=False, installed=False)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inspect", action="store_true")
    parser.add_argument("--binary", type=Path)
    parser.add_argument("--case", type=Path)
    parser.add_argument("--pe-proof", type=Path)
    parser.add_argument("--child-pid", type=int)
    parser.add_argument("--expect", choices=("normal", "identity-refusal"))
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        require(not args.output.exists(), "Preserve an existing verifier output")
        if args.inspect:
            require(args.binary and args.binary.is_absolute() and not args.case, "Inspect requires an absolute exact built fixture --binary path")
            result = inspect_images(args.binary)
        else:
            require(args.case and args.case.is_absolute() and args.pe_proof and args.pe_proof.is_absolute()
                    and args.child_pid and args.expect, "Case verification requires absolute case/proof plus independent child PID and expected behavior")
            result = verify_case(args)
        args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        print(json.dumps(dict(passed=True, kind=result["kind"], checks=result.get("checks", 0), output=str(args.output))))
        return 0
    except Exception as error:
        result = dict(kind="production-doorstop-cold-verifier-failure", passed=False, error=f"{type(error).__name__}: {error}")
        if not args.output.exists():
            args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        print(json.dumps(result), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
