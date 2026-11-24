"""Read four PE files from disk; never load a module, execute targets or patch.

Only the requested JSON evidence is written. pefile parses DOS/NT headers,
ordinary/delay imports, exports/forwarder strings, TLS and version resources.
This is not IDA analysis, runtime loader observation, signature recovery or a
proof of every GetProcAddress/dynamic consumer. Python must be 64 bit so the
literal System32 path is not a WOW64-redirected SysWOW64 view.
"""
import argparse
import hashlib
import json
import struct
import sys
from datetime import datetime, timezone
from pathlib import Path

import pefile


def require(value, message):
    if not value:
        raise ValueError(message)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def text(value):
    return value.decode("ascii", "strict") if isinstance(value, bytes) else value


def inspect(path, key):
    data = path.read_bytes()
    pe = pefile.PE(data=data, fast_load=True)
    pe.parse_data_directories(directories=[0, 1, 2, 9, 13])
    base = pe.OPTIONAL_HEADER.ImageBase
    image_size = pe.OPTIONAL_HEADER.SizeOfImage
    sections = [dict(name=text(s.Name.split(b"\0", 1)[0]), rva=s.VirtualAddress,
                     virtualSize=s.Misc_VirtualSize, rawOffset=s.PointerToRawData,
                     rawSize=s.SizeOfRawData, characteristics=s.Characteristics)
                for s in pe.sections]

    def region(rva, size=16):
        section = next((s for s in sections if s["rva"] <= rva < s["rva"] + max(s["virtualSize"], s["rawSize"])), None)
        require(section is not None, f"{key}: RVA {rva:x} has no image section")
        delta = rva - section["rva"]
        file_backed = delta + size <= section["rawSize"]
        return dict(rva=rva, section=section["name"], sectionCharacteristics=section["characteristics"],
                    fileOffset=section["rawOffset"] + delta if file_backed else None,
                    bytes=pe.get_data(rva, size).hex() if file_backed else None,
                    byteCount=size if file_backed else 0)

    imports = {}
    for table, attribute in [("ordinary", "DIRECTORY_ENTRY_IMPORT"), ("delay", "DIRECTORY_ENTRY_DELAY_IMPORT")]:
        rows = []
        for module in getattr(pe, attribute, []):
            symbols = []
            for symbol in module.imports:
                symbols.append(dict(kind="name" if symbol.name is not None else "ordinal",
                                    name=text(symbol.name), ordinal=symbol.ordinal,
                                    hint=symbol.hint if symbol.name is not None else None,
                                    iatRva=symbol.address - base))
            rows.append(dict(module=text(module.dll), descriptorRva=pe.get_rva_from_offset(module.struct.get_file_offset()),
                             descriptorFileOffset=module.struct.get_file_offset(),
                             firstThunkRva=getattr(module.struct, "FirstThunk", None),
                             originalFirstThunkRva=getattr(module.struct, "OriginalFirstThunk", None),
                             delayAttributes=getattr(module.struct, "grAttrs", None), symbols=symbols))
        imports[table] = rows

    exports = []
    table = getattr(pe, "DIRECTORY_ENTRY_EXPORT", None)
    export_meta = None
    if table is not None:
        directory = pe.OPTIONAL_HEADER.DATA_DIRECTORY[0]
        export_meta = dict(moduleName=text(pe.get_string_at_rva(table.struct.Name)),
                           directoryRva=directory.VirtualAddress, directorySize=directory.Size,
                           ordinalBase=table.struct.Base, addressTableSlots=table.struct.NumberOfFunctions,
                           nameTableEntries=table.struct.NumberOfNames)
        for symbol in table.symbols:
            row = dict(name=text(symbol.name), ordinal=symbol.ordinal, rva=symbol.address,
                       forwarder=text(symbol.forwarder))
            if symbol.address and symbol.forwarder is None:
                row["entryBytes"] = region(symbol.address)
                code = pe.get_data(symbol.address, 8)
                # Actual proxy code can be an indirect tail thunk even though
                # its PE export is NOT an EAT forwarder. Read the file slot only;
                # its eventual runtime value is deliberately not guessed.
                rex = 1 if code[0] in range(0x40, 0x50) and code[1:3] == b"\xff\x25" else 0
                if code[rex:rex+2] == b"\xff\x25":
                    displacement = struct.unpack_from("<i", code, rex+2)[0]
                    instruction_size = rex + 6
                    slot_rva = symbol.address + instruction_size + displacement
                    row["ripIndirectJump"] = dict(instructionBytes=code[:instruction_size].hex(), slotRva=slot_rva,
                                                  slot=region(slot_rva, 8),
                                                  onDiskSlotValue=pe.get_qword_at_rva(slot_rva),
                                                  runtimeResolved=False)
            exports.append(row)

    tls = None
    tls_table = getattr(pe, "DIRECTORY_ENTRY_TLS", None)
    if tls_table is not None:
        value = tls_table.struct
        callbacks = []
        if value.AddressOfCallBacks:
            require(base <= value.AddressOfCallBacks < base + image_size, f"{key}: TLS table outside image")
            for i in range(256):
                callback = pe.get_qword_at_rva(value.AddressOfCallBacks - base + 8*i)
                require(callback is not None, f"{key}: truncated TLS callback table")
                if callback == 0:
                    break
                require(base <= callback < base + image_size, f"{key}: TLS callback outside image")
                callbacks.append(dict(va=callback, **region(callback - base)))
            else:
                raise ValueError(f"{key}: unterminated TLS callback table")
        tls = dict(directoryRva=pe.OPTIONAL_HEADER.DATA_DIRECTORY[9].VirtualAddress,
                   directorySize=pe.OPTIONAL_HEADER.DATA_DIRECTORY[9].Size,
                   startRawDataVa=value.StartAddressOfRawData, endRawDataVa=value.EndAddressOfRawData,
                   indexVa=value.AddressOfIndex, callbackArrayVa=value.AddressOfCallBacks,
                   zeroFill=value.SizeOfZeroFill, characteristics=value.Characteristics,
                   callbacks=callbacks)
    file_version = None
    if getattr(pe, "VS_FIXEDFILEINFO", []):
        version = pe.VS_FIXEDFILEINFO[0]
        file_version = ".".join(str(x) for x in [version.FileVersionMS >> 16,
                              version.FileVersionMS & 0xffff, version.FileVersionLS >> 16,
                              version.FileVersionLS & 0xffff])
    result = dict(key=key, path=str(path.resolve()), bytes=len(data), sha256=sha(data),
                  machine=pe.FILE_HEADER.Machine, machineHex=f"0x{pe.FILE_HEADER.Machine:04x}",
                  optionalMagic=pe.OPTIONAL_HEADER.Magic, imageBase=base, imageSize=image_size,
                  timeDateStamp=pe.FILE_HEADER.TimeDateStamp, fileVersion=file_version,
                  dllCharacteristics=pe.OPTIONAL_HEADER.DllCharacteristics,
                  isDll=bool(pe.FILE_HEADER.Characteristics & 0x2000),
                  entryRva=pe.OPTIONAL_HEADER.AddressOfEntryPoint,
                  entry=region(pe.OPTIONAL_HEADER.AddressOfEntryPoint) if pe.OPTIONAL_HEADER.AddressOfEntryPoint else None,
                  tls=tls, imports=imports, exportDirectory=export_meta,
                  exports=exports, exportCount=len(exports),
                  forwarderCount=sum(x["forwarder"] is not None for x in exports),
                  unnamedExportCount=sum(x["name"] is None for x in exports), sections=sections,
                  warningCount=len(pe.get_warnings()), parseWarnings=pe.get_warnings())
    pe.close()
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-directory", type=Path, default=Path("D:/SteamLibrary/steamapps/common/Rizline"))
    parser.add_argument("--system-winhttp", type=Path, default=Path("C:/Windows/System32/winhttp.dll"))
    parser.add_argument("--output", type=Path, default=Path(__file__).with_name("game-winhttp-contract.json"))
    args = parser.parse_args()
    require(struct.calcsize("P") == 8, "Use 64-bit Python; do not silently parse a WOW64 redirected System32")
    paths = dict(gameExe=args.game_directory / "Rizline.exe", unityPlayer=args.game_directory / "UnityPlayer.dll",
                 installedProxy=args.game_directory / "winhttp.dll", systemWinhttp=args.system_winhttp)
    images = {key: inspect(path, key) for key, path in paths.items()}
    require(all(image["machine"] == 0x8664 and image["optionalMagic"] == 0x20b for image in images.values()),
            "The current contract is fixed AMD64 PE32+ only")
    required = []
    for table in ("ordinary", "delay"):
        for module in images["unityPlayer"]["imports"][table]:
            if module["module"].lower() == "winhttp.dll":
                for symbol in module["symbols"]:
                    required.append(dict(importKind=table, module=module["module"], **symbol))
    require(required, "No actual UnityPlayer WinHTTP import found")
    by_name = {key: {e["name"]: e for e in images[key]["exports"] if e["name"] is not None}
               for key in ("installedProxy", "systemWinhttp")}
    forwarding = []
    for symbol in required:
        if symbol["kind"] == "name":
            proxy = by_name["installedProxy"].get(symbol["name"])
            system = by_name["systemWinhttp"].get(symbol["name"])
        else:
            proxy = next((e for e in images["installedProxy"]["exports"] if e["ordinal"] == symbol["ordinal"]), None)
            system = next((e for e in images["systemWinhttp"]["exports"] if e["ordinal"] == symbol["ordinal"]), None)
        forwarding.append(dict(importContract=symbol, installedProxyExport=proxy, systemExport=system,
                               installedProxySatisfiesStaticSymbol=proxy is not None,
                               systemSatisfiesStaticSymbol=system is not None,
                               resolutionKey=symbol["name"] if symbol["kind"] == "name" else f"#{symbol['ordinal']}",
                               ordinalTranslationAuthorized=False, runtimeForwardingExecuted=False))
    proxy_names, system_names = [set(by_name[k]) for k in ("installedProxy", "systemWinhttp")]
    proxy_ordinals = {e["ordinal"]: e for e in images["installedProxy"]["exports"]}
    system_ordinals = {e["ordinal"]: e for e in images["systemWinhttp"]["exports"]}
    ordinal_mismatches = [dict(ordinal=n, installedProxyName=proxy_ordinals[n]["name"], systemName=system_ordinals[n]["name"])
                          for n in sorted(proxy_ordinals.keys() & system_ordinals.keys())
                          if proxy_ordinals[n]["name"] != system_ordinals[n]["name"]]
    changes = [key for key, path in paths.items() if sha(path.read_bytes()) != images[key]["sha256"]]
    require(not changes, f"An input changed during extraction: {changes}")
    evidence = dict(schemaVersion=1, kind="disk-only-game-winhttp-contract",
                    observedAtUtc=datetime.now(timezone.utc).isoformat(),
                    method=dict(parser="pefile", pefileVersion=pefile.__version__, pythonVersion=sys.version,
                                pythonExecutable=sys.executable, pointerBytes=struct.calcsize("P"),
                                idaBackendUsed=False, targetModulesLoaded=False, targetCodeExecuted=False,
                                sourceScriptSha256=sha(Path(__file__).read_bytes())), images=images,
                    requiredForwarding=forwarding,
                    comparison=dict(sharedNames=sorted(proxy_names & system_names),
                                    systemNamesAbsentFromProxy=sorted(system_names - proxy_names),
                                    proxyNamesAbsentFromSystem=sorted(proxy_names - system_names),
                                    sharedNameDifferentOrdinal=[dict(name=n, installedProxyOrdinal=by_name["installedProxy"][n]["ordinal"],
                                                                    systemOrdinal=by_name["systemWinhttp"][n]["ordinal"])
                                                               for n in sorted(proxy_names & system_names)
                                                               if by_name["installedProxy"][n]["ordinal"] != by_name["systemWinhttp"][n]["ordinal"]],
                                    sameOrdinalDifferentName=ordinal_mismatches),
                    inputHashesUnchanged=True,
                    staticImportContractCompleteForTheseFourDiskImages=True,
                    dynamicLookupConsumersEnumerated=False, runtimeForwardingVerified=False,
                    loaderSearchResolutionVerified=False, systemDependencyClosureVerified=False,
                    gameFirstCallTimingVerified=False, productionFirstInstallVerified=False,
                    gameExecuted=False, installed=False)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(evidence, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(dict(output=str(args.output), requiredNames=[r["resolutionKey"] for r in forwarding],
                          exportCounts={k:i["exportCount"] for k,i in images.items()},
                          forwarderCounts={k:i["forwarderCount"] for k,i in images.items()},
                          sharedNames=len(proxy_names & system_names), systemOnly=len(system_names - proxy_names),
                          proxyOnly=len(proxy_names - system_names), inputHashesUnchanged=True), ensure_ascii=False))


if __name__ == "__main__":
    main()
