"""Generate fixed candidate compile-time identities without loading target images.

The generated facts never grant the game's first-write execution-window permit.
Native Bootstrap references itself by its bounded actual EAT/ILT, avoiding a
binary hash cycle. Managed compilation pins the final five image hashes.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

import pefile

GAME_HASHES = {
    "Rizline.exe": "f9c37f1078619d02c62a7a5815ca48179666efb2dfe7e6e698bfff7858c26e5e",
    "UnityPlayer.dll": "43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1",
    "GameAssembly.dll": "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79",
}
KERNEL_HASH = "923486d2901ab6f66ea6d90d99cc65b1afafbc3275c338f0fd71ea5d22bc5662"
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
WINHTTP_NAMES = ("WinHttpOpen", "WinHttpGetProxyForUrl", "WinHttpGetIEProxyConfigForCurrentUser", "WinHttpCloseHandle")
REPO = Path(__file__).resolve().parents[3]


def require(value, message):
    if not value:
        raise ValueError(message)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def load(path, expected=None):
    require(path.is_absolute() and path.is_file() and not path.is_symlink(), "An absolute ordinary image file is required: " + str(path))
    data = path.read_bytes()
    observed = digest(data)
    require(expected is None or observed == expected, "Fixed image hash differs: " + str(path))
    pe = pefile.PE(data=data, fast_load=True)
    pe.parse_data_directories(directories=[0, 1, 3, 9, 13])
    require(pe.FILE_HEADER.Machine == 0x8664 and pe.OPTIONAL_HEADER.Magic == 0x20b, "AMD64 PE32+ image required")
    result = dict(path=str(path), module=path.name, sha256=observed, fileBytes=len(data),
                  preferredImageBase=pe.OPTIONAL_HEADER.ImageBase, machine=pe.FILE_HEADER.Machine,
                  imageSize=pe.OPTIONAL_HEADER.SizeOfImage, timeDateStamp=pe.FILE_HEADER.TimeDateStamp,
                  entryRva=pe.OPTIONAL_HEADER.AddressOfEntryPoint,
                  exceptionRva=pe.OPTIONAL_HEADER.DATA_DIRECTORY[3].VirtualAddress,
                  exceptionBytes=pe.OPTIONAL_HEADER.DATA_DIRECTORY[3].Size,
                  tlsRva=pe.OPTIONAL_HEADER.DATA_DIRECTORY[9].VirtualAddress,
                  tlsBytes=pe.OPTIONAL_HEADER.DATA_DIRECTORY[9].Size,
                  delayImportRva=pe.OPTIONAL_HEADER.DATA_DIRECTORY[13].VirtualAddress,
                  delayImportBytes=pe.OPTIONAL_HEADER.DATA_DIRECTORY[13].Size,
                  ordinaryImports=[dict(module=d.dll.decode("ascii"), symbols=[i.name.decode("ascii") if i.name else "#" + str(i.ordinal) for i in d.imports])
                                   for d in getattr(pe, "DIRECTORY_ENTRY_IMPORT", [])])
    return result, pe


def export(pe, symbol):
    matches = [e for e in pe.DIRECTORY_ENTRY_EXPORT.symbols if e.name == symbol.encode("ascii")]
    require(len(matches) == 1 and not matches[0].forwarder and matches[0].address, "Exact non-forwarded EAT symbol required: " + symbol)
    section = pe.get_section_by_rva(matches[0].address)
    require(section is not None and section.Characteristics & 0xe0000000 == 0x60000000, "EAT symbol must belong to executable/read-only image code")
    return matches[0].address


def imported(pe, module, symbol):
    descriptors = [d for d in getattr(pe, "DIRECTORY_ENTRY_IMPORT", []) if d.dll.decode("ascii").casefold() == module.casefold()]
    require(len(descriptors) == 1 and descriptors[0].struct.OriginalFirstThunk and descriptors[0].struct.FirstThunk, "Exact ordinary ILT/IAT descriptor required: " + module)
    d = descriptors[0]
    matches = [(index, value) for index, value in enumerate(d.imports) if value.name == symbol.encode("ascii")]
    require(len(matches) == 1, "Unique ordinary import required: " + symbol)
    index, value = matches[0]
    lookup = d.struct.OriginalFirstThunk + index * 8
    iat = value.address - pe.OPTIONAL_HEADER.ImageBase
    require(iat == d.struct.FirstThunk + index * 8, "ILT/IAT slot index differs")
    return lookup, iat


def byte_array(name, value):
    return "inline constexpr unsigned char " + name + "[]={" + ",".join("0x%02x" % b for b in value) + "};\n"


def image_constant(name, value):
    return (byte_array(name + "Sha256", bytes.fromhex(value["sha256"]))
            + "inline constexpr wchar_t " + name + 'Module[]=L"' + value["module"] + '";\n'
            + ("inline constexpr ImageIdentity %s{%sModule,%dull,0x%xull,0x%x,0x%x,0x%x,0x%x,0x%x,0x%x,%sSha256};\n" %
               (name, name, value["fileBytes"], value["preferredImageBase"], value["machine"], value["imageSize"],
                value["timeDateStamp"], value["entryRva"], value["exceptionRva"], value["exceptionBytes"], name)))


def write_new(path, data):
    require(path.is_absolute() and not path.exists(), "Preserve earlier generated profile: " + str(path))
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("x", encoding="utf-8", newline="\n") as stream:
        stream.write(data)


def native(args):
    game = args.game.resolve()
    values = {name: load(game / name, expected) for name, expected in GAME_HASHES.items()}
    doorstop = load(args.doorstop.resolve())
    kernel = load(args.kernel.resolve(), KERNEL_HASH)
    require(doorstop[0]["module"] == "winhttp.dll" and kernel[0]["module"] == "RizToolbox.EngineNodeKernel.dll", "Candidate module names differ")
    require(kernel[0]["entryRva"] == kernel[0]["tlsBytes"] == kernel[0]["delayImportBytes"] == 0, "Fixed Kernel must have no entry/TLS/delay imports")
    require(not doorstop[0]["delayImportBytes"], "Candidate Doorstop cannot delay-load Bootstrap")
    require(len(doorstop[1].DIRECTORY_ENTRY_EXPORT.symbols) == 82, "Candidate Doorstop must preserve its 82 public exports")
    require({e.name.decode("ascii") for e in kernel[1].DIRECTORY_ENTRY_EXPORT.symbols} == set(KERNEL_NAMES), "Fixed Kernel export table differs")
    require(set(next(d["symbols"] for d in doorstop[0]["ordinaryImports"] if d["module"].casefold() == "riztoolbox.enginebootstrap.dll")) == set(BOOTSTRAP_NAMES), "Doorstop ordinary Bootstrap dependency must contain exactly four ABI names")
    edges = []

    def add(source, destination, symbol, source_pe=None, destination_pe=None, destination_name=None):
        lookup, iat = imported(source_pe, destination_name, symbol) if source_pe is not None else (0, 0)
        target = export(destination_pe, symbol) if destination_pe is not None else 0
        edges.append(dict(source=source, destination=destination, symbol=symbol, lookupRva=lookup, iatRva=iat, exportRva=target))

    add(3, 2, "UnityMain2", values["Rizline.exe"][1], values["UnityPlayer.dll"][1], "UnityPlayer.dll")
    for name in WINHTTP_NAMES:
        add(2, 4, name, values["UnityPlayer.dll"][1], doorstop[1], "WINHTTP.dll")
    for name in BOOTSTRAP_NAMES:
        add(4, 0, name, doorstop[1], None, "RizToolbox.EngineBootstrap.dll")
    for name in KERNEL_NAMES:
        add(0, 1, name, None, kernel[1])
    require(len(edges) == 17 and len({(e["source"], e["symbol"]) for e in edges}) == 17, "Fixed edge contract differs")
    header = ('#pragma once\n#include "EngineGameProfile.h"\n#include "EngineBootstrapProductionContract.h"\n'
              'namespace RizEngineBootstrapProductionProfile {\nusing RizEngineGameProfile::ImageIdentity;\n'
              'using RizEngineBootstrapProductionContract::ImportEdge;\n')
    header += image_constant("Kernel", kernel[0]) + image_constant("Doorstop", doorstop[0])
    header += "inline constexpr ImportEdge Edges[17]={\n"
    for edge in edges:
        header += ' {%d,%d,"%s",0x%x,0x%x,0x%x},\n' % tuple(edge[k] for k in ("source", "destination", "symbol", "lookupRva", "iatRva", "exportRva"))
    header += "};\n}\n"
    write_new(args.header, header)
    profile = dict(kind="fixed-game-engine-production-compile-facts", schemaVersion=1,
                   images={**{name: row for name, (row, _) in values.items()}, "Doorstop": doorstop[0], "Kernel": kernel[0]},
                   edges=edges, generatedHeader=dict(path=str(args.header), sha256=digest(args.header.read_bytes())),
                   productionAdmissionGranted=False, executionWindowProved=False, targetImagesLoaded=False, installed=False)
    write_new(args.output, json.dumps(profile, indent=2) + "\n")
    print(json.dumps(dict(phase="native", edges=17, generatedHeaderSha256=profile["generatedHeader"]["sha256"], productionAdmissionGranted=False)))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="phase", required=True)
    first = sub.add_parser("native")
    for name in ("game", "doorstop", "kernel", "header", "output"):
        first.add_argument("--" + name, type=Path, required=True)
    second = sub.add_parser("managed")
    for name in ("profile", "bootstrap", "source", "output"):
        second.add_argument("--" + name, type=Path, required=True)
    args = parser.parse_args()
    if args.phase == "native":
        native(args)
    else:
        managed(args)


def managed(args):
    facts = json.loads(args.profile.read_text(encoding="utf-8-sig"))
    require(facts["kind"] == "fixed-game-engine-production-compile-facts" and facts["productionAdmissionGranted"] is False,
            "An unadmitted native candidate fact set is required")
    for row in facts["images"].values():
        require(digest(Path(row["path"]).read_bytes()) == row["sha256"], "Native compile input changed")
    bootstrap, pe = load(args.bootstrap.resolve())
    require(bootstrap["module"] == "RizToolbox.EngineBootstrap.dll" and bootstrap["tlsBytes"] == bootstrap["delayImportBytes"] == 0,
            "Fixed Bootstrap layout differs")
    require({e.name.decode("ascii") for e in pe.DIRECTORY_ENTRY_EXPORT.symbols} == set(BOOTSTRAP_NAMES), "Bootstrap public ABI differs")
    for name in BOOTSTRAP_NAMES:
        export(pe, name)
    require(set(next(row["symbols"] for row in bootstrap["ordinaryImports"] if row["module"].casefold() == "riztoolbox.enginenodekernel.dll")) == set(KERNEL_NAMES),
            "Bootstrap must have all eight ordinary Kernel imports")
    for name in KERNEL_NAMES:
        imported(pe, "RizToolbox.EngineNodeKernel.dll", name)
    images = facts["images"]
    hashes = [bootstrap["sha256"], images["Kernel"]["sha256"], images["UnityPlayer.dll"]["sha256"],
              images["Rizline.exe"]["sha256"], images["Doorstop"]["sha256"]]
    source = ('// Generated fixed candidate identities; no startup authority.\n#nullable enable\nnamespace RizToolbox.Runtime.Hooks;\n'
              'internal static partial class NativeEngineProductionProfile\n{\n'
              '    static partial void Populate(ref NativeEngineProductionProfileData? value)\n'
              '        => value = new NativeEngineProductionProfileData(\n'
              + ',\n'.join('            "' + value + '"' for value in hashes)
              + ',\n            new uint[] { 0x792240, 0x78f840, 0x78f680, 0x793840 });\n}\n')
    write_new(args.source, source)
    result = dict(kind="fixed-game-engine-managed-compile-facts", schemaVersion=1,
                  nativeProfile=dict(path=str(args.profile), sha256=digest(args.profile.read_bytes())), bootstrap=bootstrap,
                  generatedSource=dict(path=str(args.source), sha256=digest(args.source.read_bytes())),
                  productionAdmissionGranted=False, executionWindowProved=False, targetImagesLoaded=False, installed=False)
    write_new(args.output, json.dumps(result, indent=2) + "\n")
    print(json.dumps(dict(phase="managed", generatedSourceSha256=result["generatedSource"]["sha256"], productionAdmissionGranted=False)))


if __name__ == "__main__":
    main()
