"""Offline PE/PDB/C13 verification of the independent source prototype."""
import argparse
import hashlib
import json
import struct
import sys
from pathlib import Path

import pefile


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def pdb_identity(path):
    data = path.read_bytes()
    assert data[:32] == b"Microsoft C/C++ MSF 7.00\r\n\x1aDS\0\0\0"
    block_size, _, _, directory_size, _, block_map = struct.unpack_from("<6I", data, 32)
    count = (directory_size + block_size - 1) // block_size
    blocks = struct.unpack_from(f"<{count}I", data, block_map * block_size)
    directory = b"".join(data[x * block_size:(x + 1) * block_size] for x in blocks)[:directory_size]
    streams = struct.unpack_from("<I", directory)[0]
    sizes = struct.unpack_from(f"<{streams}I", directory, 4)
    cursor = 4 + 4 * streams
    for number, size in enumerate(sizes):
        count = 0 if size == 0xffffffff else (size + block_size - 1) // block_size
        blocks = struct.unpack_from(f"<{count}I", directory, cursor)
        cursor += count * 4
        if number == 1:
            stream = b"".join(data[x * block_size:(x + 1) * block_size] for x in blocks)[:size]
            return stream[12:28], struct.unpack_from("<I", stream, 8)[0]
    raise AssertionError("No native PDB identity")


def image_identity(image, pdb):
    pe = pefile.PE(str(image))
    assert pe.FILE_HEADER.Machine == 0x8664 and pe.OPTIONAL_HEADER.Magic == 0x20b
    result = []
    for row in pe.DIRECTORY_ENTRY_DEBUG:
        if row.struct.Type != 2:
            continue
        data = pe.get_data(row.struct.AddressOfRawData, row.struct.SizeOfData)
        guid, age = pdb_identity(pdb)
        assert data[:4] == b"RSDS" and data[4:20] == guid and struct.unpack_from("<I", data, 20)[0] == age
        result.append({"guidBytes": guid.hex(), "age": age, "pdbPathInPe": data[24:].rstrip(b"\0").decode("utf-8")})
    assert len(result) == 1
    return {"image": str(image), "imageSha256": sha(image), "pdb": str(pdb), "pdbSha256": sha(pdb),
            "matchesNativePdbIdentity": True, **result[0]}


def image_survey(path):
    pe = pefile.PE(str(path))
    imports = [{"module": row.dll.decode("ascii"), "symbols": [
        symbol.name.decode("ascii") if symbol.name else f"ordinal:{symbol.ordinal}" for symbol in row.imports]}
        for row in getattr(pe, "DIRECTORY_ENTRY_IMPORT", [])]
    exports = [{"name": row.name.decode("ascii") if row.name else None, "ordinal": row.ordinal,
                "rva": hex(row.address), "forwarder": row.forwarder.decode("ascii") if row.forwarder else None}
               for row in getattr(getattr(pe, "DIRECTORY_ENTRY_EXPORT", None), "symbols", [])]
    return {"image": str(path), "sha256": sha(path), "machine": hex(pe.FILE_HEADER.Machine),
            "optionalHeaderMagic": hex(pe.OPTIONAL_HEADER.Magic), "entryPointRva": hex(pe.OPTIONAL_HEADER.AddressOfEntryPoint),
            "dllCharacteristics": hex(pe.OPTIONAL_HEADER.DllCharacteristics), "imports": imports, "exports": exports}


def compiled_sources(path):
    data = path.read_bytes()
    count = struct.unpack_from("<H", data, 2)[0]
    optional = struct.unpack_from("<H", data, 16)[0]
    sources = []
    for number in range(count):
        section = struct.unpack_from("<8sIIIIIIHHI", data, 20 + optional + number * 40)
        if section[0] != b".debug$S":
            continue
        debug = data[section[4]:section[4] + section[3]]
        assert struct.unpack_from("<I", debug)[0] == 4
        cursor, subsections = 4, {}
        while cursor + 8 <= len(debug):
            kind, length = struct.unpack_from("<II", debug, cursor)
            subsections.setdefault(kind, []).append(debug[cursor + 8:cursor + 8 + length])
            cursor = (cursor + 8 + length + 3) & ~3
        if 0xf4 not in subsections:
            continue
        assert len(subsections[0xf3]) == 1
        strings = subsections[0xf3][0]
        for checksums in subsections[0xf4]:
            cursor = 0
            while cursor + 6 <= len(checksums):
                name_at, size, kind = struct.unpack_from("<IBB", checksums, cursor)
                source = Path(strings[name_at:strings.index(b"\0", name_at)].decode("utf-8"))
                expected = checksums[cursor + 6:cursor + 6 + size]
                algorithm = {1: "md5", 2: "sha1", 3: "sha256"}[kind]
                assert hashlib.new(algorithm, source.read_bytes()).digest() == expected, source
                sources.append({"path": str(source), "algorithm": algorithm, "compiledChecksum": expected.hex(),
                                "sha256": sha(source), "matchesActualCompiledObject": True})
                cursor = (cursor + 6 + size + 3) & ~3
    assert sources, f"No C13 source records: {path}"
    return {"object": str(path), "sha256": sha(path), "sources": sources}


def executable_dir64(pe, exports, object_path):
    anchor = exports["StockExecutableDir64Anchor"]
    target = exports["StockSetPitch"]
    section = pe.get_section_by_rva(anchor)
    assert section.Name.rstrip(b"\0") == b".text" and section.Characteristics & 0x20000000
    assert anchor + 8 <= section.VirtualAddress + section.Misc_VirtualSize
    expected = pe.OPTIONAL_HEADER.ImageBase + target
    stored = struct.unpack("<Q", pe.get_data(anchor, 8))[0]
    assert stored == expected
    relocations = [entry for block in pe.DIRECTORY_ENTRY_BASERELOC for entry in block.entries
                   if entry.rva == anchor]
    assert len(relocations) == 1 and relocations[0].type == 10
    assert not any(row.struct.BeginAddress <= anchor < row.struct.EndAddress
                   for row in pe.DIRECTORY_ENTRY_EXCEPTION)

    data = object_path.read_bytes()
    section_count, symbol_at, symbol_count, optional_size = (
        struct.unpack_from("<H", data, 2)[0], struct.unpack_from("<I", data, 8)[0],
        struct.unpack_from("<I", data, 12)[0], struct.unpack_from("<H", data, 16)[0])
    strings_at = symbol_at + symbol_count * 18
    symbols = {}
    index = 0
    while index < symbol_count:
        at = symbol_at + index * 18
        name = data[at:at + 8]
        if name[:4] == b"\0" * 4:
            string_at = strings_at + struct.unpack_from("<I", name, 4)[0]
            name = data[string_at:data.index(b"\0", string_at)]
        else:
            name = name.rstrip(b"\0")
        value, number, kind, storage, auxiliary = struct.unpack_from("<IhHBB", data, at + 8)
        symbols[name.decode("utf-8")] = (index, value, number, kind, storage)
        index += 1 + auxiliary
    target_symbol = symbols["StockSetPitch"]
    anchor_symbol = symbols["StockExecutableDir64Anchor"]
    assert 0 < anchor_symbol[2] <= section_count and anchor_symbol[2] == target_symbol[2]
    coff_section = struct.unpack_from("<8sIIIIIIHHI", data, 20 + optional_size + (anchor_symbol[2] - 1) * 40)
    coff_section_name = coff_section[0].rstrip(b"\0").decode("ascii")
    assert coff_section_name == ".text$mn" and coff_section[9] & 0x20000000
    coff_rows = [struct.unpack_from("<IIH", data, coff_section[5] + row * 10)
                 for row in range(coff_section[7])]
    matching = [row for row in coff_rows if row[0] == anchor_symbol[1]]
    assert matching == [(anchor_symbol[1], target_symbol[0], 1)]
    assert struct.unpack_from("<Q", data, coff_section[4] + anchor_symbol[1])[0] == 0
    return {"export": "StockExecutableDir64Anchor", "anchorRva": hex(anchor), "section": ".text",
            "executable": True, "baseRelocationType": "IMAGE_REL_BASED_DIR64", "baseRelocationTypeValue": 10,
            "preferredImageBase": hex(pe.OPTIONAL_HEADER.ImageBase), "storedPreferredAddress": hex(stored),
            "targetExport": "StockSetPitch", "targetRva": hex(target), "outsideRuntimeFunctionRanges": True,
            "coffObject": str(object_path), "coffObjectSha256": sha(object_path),
            "coffSection": coff_section_name,
            "coffAnchorOffset": hex(anchor_symbol[1]), "coffTargetSymbolIndex": target_symbol[0],
            "coffRelocationType": "IMAGE_REL_AMD64_ADDR64", "coffRelocationTypeValue": 1,
            "actualMappingExecutedByThisVerifier": False}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--build", type=Path, required=True)
    parser.add_argument("--dobby", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    assert not args.output.exists(), "Preserve every earlier proof"
    root = args.build
    manifest = json.loads((root / "build-result.json").read_text(encoding="utf-8-sig"))
    assert manifest["passed"] and manifest["actualExitCode"] == 0
    for row in manifest["sources"] + manifest["frozenSources"]:
        assert sha(Path(row["path"])) == row["sha256"], row["path"]
    for row in manifest["files"]:
        assert sha(Path(row["path"])) == row["sha256"], row["path"]
    pairs = [("RizToolbox.PlaybackSourcePrototype.dll", "RizToolbox.PlaybackSourcePrototype.pdb"),
             ("RizToolbox.PlaybackStockFixture.dll", "RizToolbox.PlaybackStockFixture.pdb"),
             ("PlaybackSourceFixture.exe", "PlaybackSourceFixture.pdb")]
    images = [image_identity(root / image, root / pdb) for image, pdb in pairs]
    objects = [compiled_sources(Path(row["object"])) for row in manifest["translationUnits"]]
    for unit, obj in zip(manifest["translationUnits"], objects):
        assert any(Path(row["path"]).resolve() == Path(unit["source"]).resolve() for row in obj["sources"])
    pe = pefile.PE(str(root / "RizToolbox.PlaybackStockFixture.dll"))
    exports = {e.name.decode(): e.address for e in pe.DIRECTORY_ENTRY_EXPORT.symbols if e.name}
    dir64 = executable_dir64(pe, exports, root / "PlaybackStockFixture.asm.obj")
    targets = []
    definitions = [
        ("StockGetTime", 640, "48895c2408574883ec50", ["010a04000a340c000a920670"]),
        ("StockSetPitch", 320, "40534883ec30", ["0106020006520230"]),
        ("StockPause", 160, "40534883ec20", ["0106020006320230"]),
        ("StockReplay", 560, "40534883ec40", ["011504001568030006720230", "2105020005740a00", "21000000"]),
        ("StockResume", 720, "40574883ec50", ["0172060072340c001568040006920270", "2105020005780300", "21000000"]),
        ("StockPlayScheduled", 96, "40534883ec30", ["0106020006520230", "2105020005680200", "21000000"]),
        ("StockSetClip", 80, "48895c2408574883ec20", ["010a04000a3406000a320670"]),
        ("StockProgressPlay", 272, "4883ec28385142", ["0104010004420000", "2105020005340400", "21000000"]),
        ("StockPauseLeaf", 64, "40534883ec20", ["0106020006320230"]),
    ]
    all_functions = [row.struct for row in pe.DIRECTORY_ENTRY_EXCEPTION]
    assert all(a.BeginAddress < b.BeginAddress and a.EndAddress <= b.BeginAddress
               for a, b in zip(all_functions, all_functions[1:])), "OS runtime-function array must be sorted and disjoint"
    for name, length, prefix, expected_unwinds in definitions:
        rva = exports[name]
        body = pe.get_data(rva, length)
        assert body.startswith(bytes.fromhex(prefix))
        assert exports[name + "End"] == rva + length
        functions = [row for row in all_functions if rva <= row.BeginAddress < rva + length]
        assert len(functions) == len(expected_unwinds) and functions[0].BeginAddress == rva
        records = []
        for function, expected in zip(functions, expected_unwinds):
            assert function.EndAddress <= rva + length
            prefix_bytes = bytes.fromhex(expected)
            assert pe.get_data(function.UnwindData, len(prefix_bytes)) == prefix_bytes
            flags = prefix_bytes[0] >> 3
            length_unwind = 4 + ((prefix_bytes[2] + 1) & ~1) * 2
            chain = None
            if flags & 4:
                assert struct.unpack("<III", pe.get_data(function.UnwindData + length_unwind, 12)) == (
                    functions[0].BeginAddress, functions[0].EndAddress, functions[0].UnwindData)
                length_unwind += 12
                chain = {"begin": hex(functions[0].BeginAddress), "end": hex(functions[0].EndAddress),
                         "unwind": hex(functions[0].UnwindData)}
            records.append({"beginOffset": function.BeginAddress - rva, "endOffset": function.EndAddress - rva,
                            "unwindRva": hex(function.UnwindData), "unwindHex": pe.get_data(function.UnwindData, length_unwind).hex(),
                            "flags": flags, "chain": chain})
        if name == "StockProgressPlay":
            assert body[7] == 0x74 and body[31:36] == bytes.fromhex("48895c2420")
        if name == "StockReplay":
            assert body[6:8] == bytes.fromhex("803d") and body[16:21] == bytes.fromhex("0f29742430")
            assert body[152:157] == bytes.fromhex("48897c2450")
        if name == "StockResume":
            assert body[6:8] == bytes.fromhex("803d") and body[16:21] == bytes.fromhex("0f29742440")
            assert body[109:114] == bytes.fromhex("48895c2460") and body[245:250] == bytes.fromhex("0f297c2430")
        targets.append({"name": name, "rva": hex(rva), "length": length, "bodySha256": hashlib.sha256(body).hexdigest(),
                        "prefix": prefix, "runtimeFunctionCount": len(records), "runtimeFunctions": records,
                        "scope": "Controlled native body with exact stock entry prefix; not a complete game implementation"})
    for export in ["StockMethodGetTime", "StockMethodSetPitch", "StockMethodSetPitchAlias", "StockMethodPause", "StockMethodReplay",
                   "StockMethodResume", "StockMethodProgressPlay", "StockMethodPauseLeaf", "StockMethodPlayScheduled", "StockMethodSetClip",
                   "StockFixtureCreateProgress", "StockFixtureGetBodyLength"]:
        assert export in exports
    expected_dobby = "8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded"
    assert sha(args.dobby) == expected_dobby
    result = {"kind": "actual-native-source-prototype-pe-pdb-coff-source", "passed": True,
              "abiVersion": 1, "frameSize": 160, "infoSize": 136, "images": images, "compiledObjects": objects,
              "stockTargets": targets, "executableDir64": dir64,
              "imageSurveys": [image_survey(root / image) for image, _ in pairs] + [image_survey(args.dobby)],
              "verificationInputs": [{"path": str(path), "sha256": sha(path)} for path in
                  [Path(__file__).resolve(), root / "build-result.json", Path(sys.executable), Path(pefile.__file__)]],
              "pythonVersion": sys.version, "pefileVersion": pefile.__version__,
              "dobby": {"path": str(args.dobby), "sha256": expected_dobby, "noPdbClaim": True},
              "gameCodeExecuted": False, "productionAdmission": False}
    args.output.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps({"passed": True, "nativePePdbPairs": len(images), "actualCompiledObjects": len(objects), "stockTargets": len(targets)}))


if __name__ == "__main__":
    main()
