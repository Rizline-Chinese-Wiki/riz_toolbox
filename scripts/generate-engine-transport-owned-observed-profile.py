"""Validate the explicit observed-owned variant and emit separate compile facts.

The frozen ordinary-owned validator continues to reject this target. This
reader verifies the actual disk code/layout and never loads a PE or grants
production admission. A layout-only pass supplies the independent cold Host.
"""
from __future__ import annotations

import argparse
import bisect
import hashlib
import importlib.util
import json
from pathlib import Path
import struct
import sys

REPO = Path(__file__).resolve().parents[1]
PROFILE = REPO / "scripts/generate-engine-transport-profile.py"
OBSERVED = REPO / "scripts/generate-engine-transport-owned-observed-image.py"
OBSERVED_SHA = "8d2554d5d71ce00adc8f36f52ca31facac629ccf2710228e14eff1c71fa6b25b"


def require(value: bool, reason: str) -> None:
    if not value:
        raise ValueError(reason)


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def load(path: Path, name: str):
    spec = importlib.util.spec_from_file_location(name, path)
    require(spec is not None and spec.loader is not None, "Independent file reader is unavailable.")
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


def layout_header(target, observed: dict, source: dict) -> bytes:
    data = observed["observation"]
    wrappers = data["wrappers"]
    lines = ["#pragma once", "#include <cstdint>", "namespace RizEngineTransportOwnedObservation {",
             f'inline constexpr wchar_t ModuleName[]=L"{target.path.name}";',
             f'inline constexpr std::uint32_t Version=1, StorageRva=0x{data["storageRva"]:x}, StorageBytes={data["storageBytes"]};',
             "inline constexpr std::uint32_t HeaderBytes=64, Capacity=8, RecordBytes=1216, WindowCount=36, PayloadOffset=64, SlotBytes=32;",
             "inline constexpr unsigned char Magic[16]{" + ",".join("0x" + data["magicHex"][index:index+2] for index in range(0, 32, 2)) + "};",
             "struct Window { std::uint32_t sourceRva, byteCount; };", "inline constexpr Window Windows[36]{"]
    for row in data["physicalWindows"]:
        lines.append(f'    {{0x{row["sourceRva"]:x},{row["byteCount"]}}},')
    lines += ["};", "struct CodeRange { std::uint32_t beginRva,endRva,unwindRva,ordinal; };", "inline constexpr CodeRange Code[5]{"]
    rows = list(struct.iter_unpack("<III", target.read(target.record["exceptionRva"], target.record["exceptionBytes"])))
    for row in wrappers:
        owner = rows[row["staticRfOrdinal"]]
        lines.append("    {" + ",".join(f"0x{value:x}" for value in (*owner,row["staticRfOrdinal"])) + "},")
    helper = data["sampler"]["runtimeFunction"]
    lines += ["    {" + ",".join(f"0x{value:x}" for value in (*helper,data["sampler"]["staticRfOrdinal"])) + "},", "};",
              f'inline constexpr std::uint32_t SamplerRva=0x{data["sampler"]["rva"]:x}, SamplerBytes={data["sampler"]["byteCount"]};',
              f'inline constexpr char TargetSha256[]="{target.record["sha256"]}";',
              f'inline constexpr char FixedContractSha256[]="{source["fixedContractSha256"]}";',
              "inline constexpr unsigned char SamplerCode[]{" + ",".join(f"0x{value:02x}" for value in bytes.fromhex(data["sampler"]["machineHex"])) + "};",
              "inline constexpr unsigned char Wrappers[4][11]{"]
    for row in wrappers:
        lines.append("    {"+",".join(f"0x{value:02x}" for value in bytes.fromhex(row["machineHex"]))+"},")
    lines += ["};", "inline constexpr std::uint32_t CallbackRvas[4]{0x17e7708,0x17e7780,0x17e7c08,0x17e7c20};", "}", ""]
    return "\n".join(lines).encode("utf-8")


def verify_target(helper, obs, target, manifest: dict, source: dict) -> dict:
    record = target.record
    require(manifest["schemaVersion"] == 1 and manifest["kind"] == "engine-transport-owned-observed-target-image"
            and manifest["passed"] is True and manifest["inputHashesUnchanged"] is True
            and manifest["output"]["sha256"] == record["sha256"] and manifest["output"]["bytes"] == len(target.data),
            "Actual target differs from its independent observed generation manifest.")
    require(manifest["generator"]["sha256"] == OBSERVED_SHA
            and sha(OBSERVED.read_bytes()) == OBSERVED_SHA, "Observed generator identity differs.")
    require(record["imageSize"] == 0x1E65000 and record["fileBytes"] == 31006120
            and record["preferredImageBase"] == 0x180000000 and record["timeDateStamp"] == 0x68D678AF
            and record["entryRva"] == 0x17E7C08 and record["exceptionRva"] == 0x1D21000 and record["exceptionBytes"] == 0x112B00,
            "Observed-owned target changed fixed PE identity/geometry.")
    require(target.path.name.casefold() != "unityplayer.dll" and record["sha256"] != helper.TARGET_SHA,
            "An observed-owned target cannot be the original game module.")
    rows = list(struct.iter_unpack("<III", target.read(record["exceptionRva"], record["exceptionBytes"])))
    require(len(rows) == 93760 and all(0 < unwind < record["imageSize"] and begin < end <= record["imageSize"] for begin,end,unwind in rows)
            and all(rows[index][0] < rows[index+1][0] and rows[index][1] <= rows[index+1][0] for index in range(len(rows)-1)),
            "Full static RF table must remain strictly ordered and nonoverlapping.")
    verified = []
    for function in source["functions"]:
        require(target.read(function["beginRva"], function["bodyBytes"]) == function["body"], "An observed variant changed one of thirteen complete source bodies.")
        actual = [row for row in rows if row[0] < function["endRva"] and row[1] > function["beginRva"]]
        require(actual == function["runtimeFunctions"], "Selected source RF groups differ.")
        target.protection(function["beginRva"], function["bodyBytes"], True)
        verified.append({key:value for key,value in function.items() if key != "body"})
    for rva, expected in source["unwind"].items():
        require(target.read(rva,len(expected)) == expected, "One of twenty-seven complete source unwind records changed.")
        target.protection(rva,len(expected),False)
    data = manifest["observation"]
    require(data["version"] == 1 and data["headerBytes"] == 64 and data["capacity"] == 8 and data["recordBytes"] == 1216
            and data["slotBytes"] == 32 and data["storageBytes"] == 9792
            and data["readinessRecorded"] is False and data["publicationQueried"] is False,
            "Observed target does not implement the explicit bounded machine sampling contract.")
    fixed = obs.physical_windows(helper.FACTS.read_bytes(), obs.import_base().Pe(manifest_original_bytes(manifest, obs)))
    require(data["physicalWindows"] == fixed, "Observation source-window plan differs from independent fixed physical facts.")
    storage = data["storageRva"]
    sections = [section for section in target.pe.sections if section.VirtualAddress <= storage
                and storage + data["storageBytes"] <= section.VirtualAddress + min(section.Misc_VirtualSize, section.SizeOfRawData)]
    require(len(sections)==1 and sections[0].Name.rstrip(b"\0") == b".data"
            and sections[0].Characteristics & 0xE0000000 == 0xC0000000
            and storage == sections[0].VirtualAddress + 0x1000,
            "Actual observation data must be wholly backed by its own fixed non-executable RW section.")
    initial = obs.observation_header() + bytes(data["storageBytes"] - 64)
    require(target.read(storage,len(initial)) == initial, "Observation data is not its exact fresh header/zero record capacity.")
    machine, references = obs.sampler(data["sampler"]["rva"],storage,fixed)
    sampler = data["sampler"]
    require(sampler["byteCount"] == len(machine) and sampler["sha256"] == sha(machine)
            and bytes.fromhex(sampler["machineHex"]) == machine and sampler["references"] == references
            and target.read(sampler["rva"],len(machine)) == machine,
            "Actual machine sampler does not equal its independently rebuilt bounded instruction sequence.")
    target.protection(sampler["rva"],len(machine),True)
    require(tuple(sampler["runtimeFunction"]) == rows[sampler["staticRfOrdinal"]]
            and rows[sampler["staticRfOrdinal"]][:2] == (sampler["rva"],sampler["rva"]+len(machine))
            and target.read(rows[sampler["staticRfOrdinal"]][2],4) == bytes.fromhex("01000000"),
            "Sampler requires its exact complete static leaf RF/unwind ownership.")
    require(len(data["wrappers"])==4, "Exactly four owned observation wrappers required.")
    for manifest_row, (name,rva,callback) in zip(data["wrappers"],obs.WRAPPERS):
        expected = bytes.fromhex("41ba")+struct.pack("<I",callback)+b"\xe9"+struct.pack("<i",sampler["rva"]-rva-11)
        require(manifest_row["name"]==name and manifest_row["callbackId"]==callback and manifest_row["rva"]==rva
                and target.read(rva,11)==expected and manifest_row["machineHex"]==expected.hex(), "Observation wrapper bytes/ID/direct target differ.")
        owner=rows[manifest_row["staticRfOrdinal"]]
        require(owner[0] <= rva and rva+11 <= owner[1] and target.read(owner[2],4)==bytes.fromhex("01000000"), "Each observation wrapper requires its actual static leaf owner.")
        target.protection(rva,11,True)
    require(not(target.pe.OPTIONAL_HEADER.DllCharacteristics & 0x4000)
            and target.pe.OPTIONAL_HEADER.DATA_DIRECTORY[10].VirtualAddress == target.pe.OPTIONAL_HEADER.DATA_DIRECTORY[10].Size == 0,
            "This explicitly owned variant has no CFG load-config metadata or hidden guard dispatch.")
    marker=target.exported(helper.MARKER_NAME,False)
    require(marker==manifest["metadata"]["markerRva"] and target.read(marker,16)==helper.MARKER
            and target.exported("UnityMain2")==helper.OWNED_UNITY_MAIN
            and len(target.exports())==target.pe.DIRECTORY_ENTRY_EXPORT.struct.NumberOfFunctions==target.pe.DIRECTORY_ENTRY_EXPORT.struct.NumberOfNames==2,
            "Observed target's exact readonly marker/two-export surface changed.")
    tls=target.pe.DIRECTORY_ENTRY_TLS.struct
    require(tls.StartAddressOfRawData==tls.EndAddressOfRawData==tls.SizeOfZeroFill==tls.Characteristics==0
            and record["tlsBytes"]==40, "Observed target cannot borrow a game TLS template.")
    callbacks=tls.AddressOfCallBacks-record["preferredImageBase"]
    require(struct.unpack("<QQQ",target.read(callbacks,24))==tuple(record["preferredImageBase"]+rva for rva in helper.OWNED_TLS_CALLBACKS)+(0,), "Observed TLS callback array differs.")
    index=tls.AddressOfIndex-record["preferredImageBase"]
    require(index==next(section.VirtualAddress for section in target.pe.sections if section.Name.rstrip(b"\0")==b".data"), "Observed TLS index must remain its own first data DWORD.")
    relocations={(entry.rva,entry.type) for block in target.pe.DIRECTORY_ENTRY_BASERELOC for entry in block.entries}
    require(all((rva,10) in relocations for rva in (callbacks,callbacks+8,record["tlsRva"]+16,record["tlsRva"]+24)), "Observed TLS pointers lack their actual DIR64 relocations.")
    require({item.dll.decode("ascii").casefold() for item in target.pe.DIRECTORY_ENTRY_IMPORT}=={"winhttp.dll"}, "Observed target imports may contain only its four ordinary WinHTTP links.")
    target.imported("winhttp.dll",helper.WINHTTP)
    return dict(functionCount=13,sourceRuntimeFunctionCount=27,fullRuntimeFunctionTableCount=93760,
                functions=verified,unwindCount=len(source["unwind"]),fixedContractSha256=source["fixedContractSha256"],
                ownedMarkerRva=marker,ownedMarkerHex=helper.MARKER.hex(),ownedObservedVariant=1,
                observedSamplerSha256=sampler["sha256"],observationStorageRva=storage,sourceImageLoaded=False,ownedStubsExecuted=False)


def manifest_original_bytes(manifest: dict, obs) -> bytes:
    path=Path(manifest["input"]["path"])
    with obs.import_base().locked_read(path) as stream:
        data=stream.read()
        require(sha(data)==manifest["input"]["sha256"]==obs.import_base().PINNED_SHA,"Independent original disk facts changed.")
        return data


def main() -> None:
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--target",type=Path,required=True)
    parser.add_argument("--target-manifest",type=Path,required=True)
    parser.add_argument("--output-observation-header",type=Path)
    for name in ("executable","kernel","doorstop","output-header","output-json"):
        parser.add_argument("--"+name,type=Path)
    args=parser.parse_args()
    helper=load(PROFILE,"riz_observed_profile_base")
    obs=load(OBSERVED,"riz_observed_image_reader")
    manifest_path=helper.absolute_file(args.target_manifest)
    manifest_bytes=manifest_path.read_bytes();manifest=json.loads(manifest_bytes)
    target=helper.Image(args.target);source=helper.facts()
    target_facts=verify_target(helper,obs,target,manifest,source)
    outputs=[]
    if args.output_observation_header:
        outputs.append((helper.absolute_output(args.output_observation_header),layout_header(target,manifest,source)))
    complete=[args.executable,args.kernel,args.doorstop,args.output_header,args.output_json]
    require(all(complete) or not any(complete),"Supply all five full compile-profile arguments, or only the target/layout pass.")
    if all(complete):
        executable,kernel,doorstop=(helper.Image(path) for path in (args.executable,args.kernel,args.doorstop))
        require(executable.path.name.casefold()!="rizline.exe" and executable.record["sha256"]!=helper.EXECUTABLE_SHA
                and kernel.path.name==helper.KERNEL and doorstop.path.name=="winhttp.dll", "Observed compile profile requires only distinct owned module roles.")
        require(not(executable.pe.FILE_HEADER.Characteristics&0x2000) and all(image.pe.FILE_HEADER.Characteristics&0x2000 for image in (target,kernel,doorstop)),"Actual EXE/DLL roles differ.")
        require(kernel.record["entryRva"]==kernel.record["tlsRva"]==kernel.record["tlsBytes"]==0
                and len(kernel.exports())==35 and {row.name.decode("ascii") if row.name else None for row in kernel.exports()}==set(helper.KERNEL_NAMES)
                and len(doorstop.exports())==82,"Actual Kernel/Doorstop complete export contracts differ.")
        images=[target,executable,doorstop,kernel]
        require(all(image.record["delayImportRva"]==image.record["delayImportBytes"]==0 for image in images),"Delay imports cannot supplement the ordinary graph.")
        edges=[]
        def group(src,dst,symbols,src_image,dst_image,dst_name):
            imports=src_image.imported(dst_name,symbols) if src_image else {}
            for symbol in symbols:
                lookup,iat=imports[symbol] if src_image else (0,0)
                edges.append(dict(index=len(edges),source=src,destination=dst,symbol=symbol,lookupRva=lookup,iatRva=iat,exportRva=dst_image.exported(symbol) if dst_image else 0))
        group(3,2,("UnityMain2",),executable,target,target.path.name)
        group(2,4,helper.WINHTTP,target,doorstop,doorstop.path.name)
        group(4,0,helper.BOOTSTRAP_NAMES,doorstop,None,helper.BOOTSTRAP)
        group(0,1,helper.KERNEL_NAMES,None,kernel,helper.KERNEL)
        require(len(edges)==44 and tuple(row["symbol"] for row in edges)==helper.SYMBOLS,"Observed ordinary graph membership/order differs.")
        zero=dict(module=helper.BOOTSTRAP,fileBytes=0,imageSize=0,timeDateStamp=0,entryRva=0,exceptionRva=0,sha256="00"*32,hashPolicy=1,reserved=0)
        records=[zero,kernel.record,target.record,executable.record,doorstop.record]
        native_header=helper.generated_header(records,edges,1,target_facts["ownedMarkerRva"]).encode("utf-8")
        out_header=helper.absolute_output(args.output_header);out_json=helper.absolute_output(args.output_json)
        require(out_header.name=="EngineTransportProfile.Generated.h","Use the exact separate native profile include basename.")
        result=dict(schemaVersion=1,kind="engine-transport-observed-owned-compile-facts",profile="owned-observed",nativeProfile=1,
                    ownedObservationVersion=1,images=records,edges=edges,targetFacts=target_facts,
                    targetManifest=dict(path=str(manifest_path),sha256=sha(manifest_bytes)),
                    generatedHeader=dict(path=str(out_header),sha256=sha(native_header)),sourceHashesUnchanged=True,
                    generator=dict(path=str(Path(__file__).resolve()),sha256=sha(Path(__file__).read_bytes())),
                    productionAdmissionGranted=False,executionWindowProved=False,nativePublicationReadyProved=False,gameExecuted=False,installed=False)
        outputs += [(out_header,native_header),(out_json,(json.dumps(result,indent=2,ensure_ascii=False)+"\n").encode("utf-8"))]
        for image in images:image.unchanged()
    require(outputs and len({str(path).casefold() for path,_ in outputs})==len(outputs),"Distinct requested new outputs required.")
    target.unchanged();require(manifest_path.read_bytes()==manifest_bytes,"Observed target manifest changed during validation.")
    for path,data in outputs:helper.write_new(path,data)
    print(json.dumps(dict(passed=True,observedOwnedVariant=1,fullCompileProfile=all(complete),targetSha256=target.record["sha256"],
                          outputs=[dict(path=str(path),sha256=sha(data)) for path,data in outputs],productionAdmissionGranted=False)))


if __name__=="__main__":main()
