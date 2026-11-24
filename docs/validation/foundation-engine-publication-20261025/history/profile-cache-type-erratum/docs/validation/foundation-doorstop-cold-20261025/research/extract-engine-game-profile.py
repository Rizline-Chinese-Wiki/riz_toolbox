"""Extract fixed game facts only; never map/execute an image or grant Ready.

Writes this new research JSON/MD and EngineGameProfile.h. Exact four original
hashes and the prior read-only evidence are mandatory, not caller parameters.
"""
from pathlib import Path
import bisect
import hashlib
import json
import re
import struct
import sys

import capstone
import pefile


HERE = Path(__file__).resolve().parent
REPO = HERE.parents[3]
GAME = Path("D:/SteamLibrary/steamapps/common/Rizline")
HEADER = REPO/"native/RizToolbox.EngineBootstrap/EngineGameProfile.h"
PRIOR = REPO/"docs/validation/foundation-engine-binding-20261025/research/engine-installation-evidence.json"
PRIOR_SHA = "c44de32c5abdc88b5f75d34c056bf4972f0bda7429505676bd6c2a5c08e2441b"
STEAM_MANIFEST = GAME.parents[1]/"appmanifest_2272590.acf"
EXPECTED = {
    "Rizline.exe":"f9c37f1078619d02c62a7a5815ca48179666efb2dfe7e6e698bfff7858c26e5e",
    "GameAssembly.dll":"853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79",
    "UnityPlayer.dll":"43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1",
    "Rizline_Data/il2cpp_data/Metadata/global-metadata.dat":"03ea18f6a92ae8bea200978e87184718bfed9c1b4606ec070ba00ecfb96c6073",
}
DECLARATIONS = [
    ("Start",0x792240,15,"uint8(owner, enumerator, nameArgument, nodeOutputSlot)"),
    ("Execute",0x78f840,18,"uint32(node, outputByteSlot)"),
    ("Release",0x78f680,16,"uint8(node)"),
    ("Detach",0x793840,15,"void(node)"),
]
SOURCE_PATHS = {
    "managedTypes":"src/RizToolbox.Runtime/Hooks/NativeEngineNodeContextBridge.Types.cs",
    "nativeTransport":"native/RizToolbox.JudgeContext/EngineNodeContext.h",
    "nativeSnapshot":"native/RizToolbox.JudgeContext/EngineNodeContext.cpp",
    "fixedPrefixes":"native/RizToolbox.FixedPublisher/FixedPublisher.cpp",
    "fixedOriginals":"native/RizToolbox.FixedPublisher/FixedOriginals.asm",
    "fixedConfiguration":"native/RizToolbox.FixedPublisher/FixedPublisher.h",
    "fixedModuleProfile":"native/RizToolbox.FixedPublisher/FixedPublisherProfile.h",
    "adapterProfile":"src/RizToolbox.Adapter/profiles/steam-25977441.json",
    "rootedOwner":"src/RizToolbox.Runtime/NativeRuntime.UnityCoroutineOwner.cs",
}


def require(value,message):
    if not value:
        raise ValueError(message)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def steam_identity():
    # Only these public installation identifiers are retained. Do not copy the
    # manifest's account, usage or other unrelated fields into the evidence.
    text = STEAM_MANIFEST.read_text(encoding="utf-8-sig")
    result = {}
    for key in ("appid", "name", "installdir", "buildid"):
        matches = re.findall(r'"'+key+r'"\s*"([^"\r\n]+)"',text)
        require(len(matches) == 1,"Steam installation identity is not unique: "+key)
        result[key] = matches[0]
    require(result == dict(appid="2272590",name="Rizline",installdir="Rizline",buildid="25977441"),
            "Fixed Steam game installation identity differs")
    return dict(path=str(STEAM_MANIFEST),**result)


class Image:
    def __init__(self,name):
        self.path = GAME/name
        self.data = self.path.read_bytes()
        require(sha(self.data) == EXPECTED[name],"Fixed original hash differs: "+name)
        self.pe = pefile.PE(data=self.data,fast_load=True)
        self.pe.parse_data_directories(directories=[0,1,9,13])
        require(self.pe.FILE_HEADER.Machine == 0x8664 and self.pe.OPTIONAL_HEADER.Magic == 0x20b,
                "Pinned image is not AMD64 PE32+: "+name)
        self.size = self.pe.OPTIONAL_HEADER.SizeOfImage
        self.base = self.pe.OPTIONAL_HEADER.ImageBase
        directory = self.pe.OPTIONAL_HEADER.DATA_DIRECTORY[3]
        require(directory.Size%12 == 0,"Runtime function table is truncated")
        pdata = self.read(directory.VirtualAddress,directory.Size)
        self.functions = [struct.unpack_from("<III",pdata,index) for index in range(0,len(pdata),12)]
        require(all(0 < begin < end <= self.size and 0 < unwind < self.size for begin,end,unwind in self.functions),
                "Invalid bounded runtime function")
        require(self.functions == sorted(self.functions) and len({row[0] for row in self.functions}) == len(self.functions),
                "Runtime functions are not sorted unique ranges")
        self.function_set = set(self.functions)
        self.starts = [row[0] for row in self.functions]
        self.unwinds = {}
        self.roots = {}
        self.md = capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_64)

    def read(self,rva,size):
        require(rva > 0 and size > 0 and rva+size <= self.size,"PE read leaves image")
        offset = self.pe.get_offset_from_rva(rva)
        section = self.pe.get_section_by_rva(rva)
        require(section is not None and rva-section.VirtualAddress+size <= section.SizeOfRawData,
                "PE read is not fully backed by one disk section")
        value = self.data[offset:offset+size]
        require(len(value) == size,"Truncated disk bytes")
        return value

    def unwind(self,rva):
        if rva in self.unwinds:
            return self.unwinds[rva]
        header = self.read(rva,4)
        version,flags = header[0]&7,header[0]>>3
        require(version == 1 and flags in (0,1,2,3,4),"Unsupported actual UNWIND_INFO header")
        count,frame = header[2],header[3]
        slots = self.read(rva+4,((count+1)&~1)*2) if count else b""
        codes = []
        index = 0
        widths = {0:1,2:1,3:1,4:2,5:3,8:2,9:3,10:1}
        while index < count:
            offset,encoded = slots[2*index:2*index+2]
            operation,info = encoded&15,encoded>>4
            if operation == 1:
                require(info in (0,1),"Invalid UWOP_ALLOC_LARGE")
                width = 2 if info == 0 else 3
            else:
                require(operation in widths,"Unknown actual unwind operation")
                width = widths[operation]
            require(index+width <= count,"Unwind operation overruns its slots")
            raw = slots[2*index:2*(index+width)]
            codes.append(dict(slot=index,codeOffset=offset,operation=operation,operationInfo=info,slots=width,rawHex=raw.hex()))
            index += width
        tail = rva+4+len(slots)
        chain = None
        handler = None
        extra = b""
        if flags&4:
            extra = self.read(tail,12)
            chain = list(struct.unpack("<III",extra))
            require(tuple(chain) in self.function_set,"CHAININFO does not identify an actual pdata row")
        elif flags&3:
            # A handler's language-specific data is not length-framed here.
            # Keep exact fixed header/codes/handler pointer but never call it a
            # fully extracted language-specific handler record.
            extra = self.read(tail,4)
            handler, = struct.unpack("<I",extra)
        raw = header+slots+extra
        row = dict(rva=rva,version=version,flags=flags,prologSize=header[1],codeSlotCount=count,
                   frameRegister=frame&15,frameOffset=frame>>4,codes=codes,chain=chain,handlerRva=handler,
                   languageSpecificDataComplete=handler is None,rawHex=raw.hex(),bytes=len(raw),sha256=sha(raw))
        self.unwinds[rva] = row
        return row

    def primary(self,row):
        if row in self.roots:
            return self.roots[row]
        original = row
        visited = set()
        while True:
            require(row not in visited and len(visited) < 16,"Cyclic or excessive unwind chain")
            visited.add(row)
            # Resolve only CHAININFO ownership while finding a group. Decoding
            # unrelated handler opcodes would broaden this bounded extraction
            # to every function in the image. Selected group records are still
            # fully decoded and checked by function() below.
            header = self.read(row[2],4)
            version,flags,count = header[0]&7,header[0]>>3,header[2]
            if not flags&4:
                self.roots[original] = row
                return row
            require(version == 1 and flags == 4,"Unsupported CHAININFO ownership header")
            row = struct.unpack("<III",self.read(row[2]+4+((count+1)&~1)*2,12))
            require(row in self.function_set,"CHAININFO does not identify an actual pdata row")

    def function(self,rva,label):
        index = bisect.bisect_right(self.starts,rva)-1
        require(index >= 0 and self.functions[index][0] <= rva < self.functions[index][1],"Requested RVA has no real RF")
        root = self.primary(self.functions[index])
        group = [row for row in self.functions if self.primary(row) == root]
        begin,end = min(row[0] for row in group),max(row[1] for row in group)
        require(begin == rva and end-begin <= 0x20000,"Function root/bound differs from requested entry")
        require(all(left[1] == right[0] for left,right in zip(group,group[1:])),"Grouped RF body has a gap")
        data = self.read(begin,end-begin)
        instructions = list(self.md.disasm(data,begin))
        require(sum(item.size for item in instructions) == len(data),"Complete function did not linearly decode")
        ranges = [dict(beginRva=a,endRva=b,unwindRva=c,unwind=self.unwind(c)) for a,b,c in group]
        return dict(label=label,beginRva=begin,endRva=end,bodyBytes=data.hex(),bodySize=len(data),bodySha256=sha(data),
                    instructions=[dict(rva=item.address,bytes=item.bytes.hex(),mnemonic=item.mnemonic,operands=item.op_str)
                                  for item in instructions],runtimeFunctions=ranges,completeLinearDecode=True)

    def metadata(self):
        pe = self.pe
        exports = [dict(name=symbol.name.decode("ascii") if symbol.name else None,rva=symbol.address,
                        ordinal=symbol.ordinal,forwarder=symbol.forwarder.decode("ascii") if symbol.forwarder else None)
                   for symbol in getattr(getattr(pe,"DIRECTORY_ENTRY_EXPORT",None),"symbols",[])]
        imports = []
        for delay,descriptors in ((False,getattr(pe,"DIRECTORY_ENTRY_IMPORT",[])),(True,getattr(pe,"DIRECTORY_ENTRY_DELAY_IMPORT",[]))):
            for descriptor in descriptors:
                imports.append(dict(module=descriptor.dll.decode("ascii"),delay=delay,
                    symbols=[dict(name=s.name.decode("ascii") if s.name else None,ordinal=s.ordinal,iatRva=s.address-self.base)
                             for s in descriptor.imports]))
        callbacks = []
        tls = getattr(pe,"DIRECTORY_ENTRY_TLS",None)
        if tls and tls.struct.AddressOfCallBacks:
            at = tls.struct.AddressOfCallBacks-self.base
            for index in range(32):
                value, = struct.unpack("<Q",self.read(at+8*index,8))
                if value == 0:
                    break
                require(self.base < value < self.base+self.size,"TLS callback leaves actual image")
                callbacks.append(value-self.base)
            else:
                raise ValueError("TLS list exceeds bound")
        return dict(name=self.path.name,path=str(self.path),sha256=sha(self.data),fileBytes=len(self.data),
                    machine=pe.FILE_HEADER.Machine,imageSize=self.size,preferredImageBase=self.base,
                    timeDateStamp=pe.FILE_HEADER.TimeDateStamp,entryPointRva=pe.OPTIONAL_HEADER.AddressOfEntryPoint,
                    exceptionDirectory=dict(rva=pe.OPTIONAL_HEADER.DATA_DIRECTORY[3].VirtualAddress,
                                            bytes=pe.OPTIONAL_HEADER.DATA_DIRECTORY[3].Size),
                    tlsDirectoryRva=pe.OPTIONAL_HEADER.DATA_DIRECTORY[9].VirtualAddress,tlsCallbacks=callbacks,
                    imports=imports,exports=exports,peWarnings=pe.get_warnings())


def cpp_bytes(name,data):
    lines = ["inline constexpr unsigned char "+name+"[]{"]
    for at in range(0,len(data),16):
        lines.append("    "+", ".join("0x%02x"%byte for byte in data[at:at+16])+",")
    lines.append("};")
    return "\n".join(lines)


def make_header(facts):
    out = ["#pragma once","#include <cstddef>","#include <cstdint>",
        "// Generated from fixed disk images only. No Configure, patch or Ready authority.",
        "// Preserve the separate RIZFIXEDOWNEDV1 fixture profile; these are game facts.",
        "namespace RizEngineGameProfile {", "inline constexpr std::uint32_t Version = 1;",
        "inline constexpr bool FactsOnly = true, ProductionFirstInstallAuthorized = false;",
        "inline constexpr bool CompleteStartupAdmissionKnown = false, AllIndirectCallersClosed = false;",
        "inline constexpr bool SchedulerCancellationClosureKnown = false;",
        "inline constexpr char FactsSha256[] = \""+facts["factsSha256"]+"\";",
        "inline constexpr char BuildId[] = \""+facts["buildId"]+"\";",
        "inline constexpr std::uint32_t KernelTransportAbi = 2, KernelScalarRecord = 1, KernelSlot = 28;",
        "inline constexpr std::uint32_t KernelFrameBytes = 112, KernelCompletionBytes = 32, KernelInfoBytes = 112;",
        "inline constexpr wchar_t KernelModule[] = L\"RizToolbox.EngineNodeKernel.dll\";",
        "struct ImageIdentity { const wchar_t* module; std::uint64_t fileBytes, preferredImageBase;",
        "    std::uint32_t machine, imageSize, timeDateStamp, entryRva, exceptionRva, exceptionBytes; const unsigned char* sha256; };",
        "struct RuntimeFunction { std::uint32_t beginRva, endRva, unwindRva; };",
        "struct UnwindIdentity { std::uint32_t rva, bytes, flags, prologBytes, codeSlots;",
        "    RuntimeFunction chain; const unsigned char* raw; const unsigned char* sha256; };",
        "struct FunctionIdentity { std::uint32_t kind, rva, endRva, prefixBytes, bodyBytes, runtimeFunctionCount;",
        "    std::uint32_t argumentCount, returnBytes;",
        "    const unsigned char* prefix; const unsigned char* body; const unsigned char* bodySha256; const RuntimeFunction* runtimeFunctions; };",
        "// Match NativeEngineNodeProfile's real native x64 Cdecl declarations.",
        "// All parameters retain pointer width; no hidden IL2CPP MethodInfo.",
        "using StartFunction = std::uint8_t(__cdecl*)(void*, void*, void*, void*);",
        "using ExecuteFunction = std::uint32_t(__cdecl*)(void*, void*);",
        "using ReleaseFunction = std::uint8_t(__cdecl*)(void*);",
        "using DetachFunction = void(__cdecl*)(void*);",
    ]
    for name,image in facts["images"].items():
        out += [cpp_bytes(name+"Sha256",bytes.fromhex(image["sha256"])),
                'inline constexpr wchar_t '+name+'Module[] = L"'+image["name"]+'";',
                "inline constexpr ImageIdentity "+name+"{"+name+"Module, "+str(image["fileBytes"])+"ull, 0x%xull, 0x%x, 0x%x, 0x%x, 0x%x, 0x%x, 0x%x, %sSha256};"%
                    (image["preferredImageBase"],image["machine"],image["imageSize"],image["timeDateStamp"],image["entryPointRva"],
                     image["exceptionDirectory"]["rva"],image["exceptionDirectory"]["bytes"],name)]
    metadata = facts["metadata"]
    out += [cpp_bytes("GlobalMetadataSha256",bytes.fromhex(metadata["sha256"])),
            "inline constexpr std::uint64_t GlobalMetadataFileBytes = %dull;"%metadata["fileBytes"],
            cpp_bytes("GlobalMetadataFirst32Bytes",bytes.fromhex(metadata["first32BytesHex"])),
            "// The actual disk header has no standard IL2CPP magic. No format/version inferred.",
            "inline constexpr bool GlobalMetadataStandardHeaderRecognized = false;",
            "inline constexpr bool GlobalMetadataVersionKnown = false;"]
    for function in facts["functions"]:
        name = function["label"]
        out += [cpp_bytes(name+"Prefix",bytes.fromhex(function["prefixHex"])),
                cpp_bytes(name+"Body",bytes.fromhex(function["bodyBytes"])),
                cpp_bytes(name+"BodySha256",bytes.fromhex(function["bodySha256"])),
                "inline constexpr RuntimeFunction "+name+"RuntimeFunctions[]{"]
        out += ["    {0x%x, 0x%x, 0x%x},"%(row["beginRva"],row["endRva"],row["unwindRva"]) for row in function["runtimeFunctions"]]
        out.append("};")
    unwinds = {row["unwindRva"]:row["unwind"] for function in facts["functions"] for row in function["runtimeFunctions"]}
    for rva,row in sorted(unwinds.items()):
        name = "Unwind%x"%rva
        out += [cpp_bytes(name,bytes.fromhex(row["rawHex"])),cpp_bytes(name+"Sha256",bytes.fromhex(row["sha256"]))]
    out.append("inline constexpr UnwindIdentity Unwinds[]{")
    for rva,row in sorted(unwinds.items()):
        chain = row["chain"] or [0,0,0]
        out.append("    {0x%x, %d, %d, %d, %d, {0x%x, 0x%x, 0x%x}, Unwind%x, Unwind%xSha256},"%
            (rva,row["bytes"],row["flags"],row["prologSize"],row["codeSlotCount"],*chain,rva,rva))
    out.append("};\ninline constexpr FunctionIdentity Functions[]{")
    for kind,row in enumerate(facts["functions"]):
        name=row["label"]
        out.append("    {%d, 0x%x, 0x%x, %d, %d, %d, %d, %d, %sPrefix, %sBody, %sBodySha256, %sRuntimeFunctions},"%
            (kind,row["beginRva"],row["endRva"],row["prefixBytes"],row["bodySize"],len(row["runtimeFunctions"]),row["argumentCount"],row["returnBytes"],name,name,name,name))
    out += ["};","inline constexpr std::uint32_t UnityMainRva = 0x%x, UnityMain2Rva = 0x%x;"%
            (facts["unityMain"]["UnityMain"],facts["unityMain"]["UnityMain2"]),
            "inline constexpr std::uint32_t UnityTlsCallbackRvas[]{"+", ".join("0x%x"%value for value in facts["images"]["UnityPlayer"]["tlsCallbacks"])+"};",
            "struct OrdinaryImport { const char* name; std::uint32_t iatRva; };",
            "inline constexpr OrdinaryImport ExecutableUnityImports[]{"]
    out += ['    {"'+row["name"]+'", 0x%x},'%row["iatRva"] for row in facts["ordinaryImportEdges"]["executableToUnity"]["symbols"]]
    out += ["};", "inline constexpr OrdinaryImport UnityWinHttpImports[]{"]
    out += ['    {"'+row["name"]+'", 0x%x},'%row["iatRva"] for row in facts["ordinaryImportEdges"]["unityToWinHttp"]["symbols"]]
    out += ["};",
            "// Proven scalar-copy implementation offsets; not object liveness/GC ownership.",
            "inline constexpr std::uint32_t NodeOwnerOffset = 0x58, NodeEnumeratorHandleOffset = 0x10;",
            "inline constexpr std::uint32_t NodeEnumeratorCacheOffset = 0x18, NodeEnumeratorTypeOffset = 0x20;",
            "inline constexpr std::uint32_t NodeReferenceCountOffset = 0x60, ManagedCachedPtrOffset = 0x10;",
            "inline constexpr bool NodeAfterReadAuthorized = false, HandleIsEnumeratorPointer = false;",
            "inline constexpr bool ReceiverRootStillRequired = true, NativeOwnerDereferenceAuthorized = false;",
            "static_assert(sizeof(void*) == 8);","static_assert(sizeof(Functions)/sizeof(Functions[0]) == 4);",
            "static_assert(sizeof(StartPrefix) == 15 && sizeof(ExecutePrefix) == 18);",
            "static_assert(sizeof(ReleasePrefix) == 16 && sizeof(DetachPrefix) == 15);",
            "static_assert(sizeof(ReleaseRuntimeFunctions)/sizeof(ReleaseRuntimeFunctions[0]) == 3);",
            "} // namespace RizEngineGameProfile",""]
    return "\n".join(out)


def main():
    require(sha(PRIOR.read_bytes()) == PRIOR_SHA,"Prior installation evidence bytes changed")
    prior = json.loads(PRIOR.read_text(encoding="utf-8-sig"))
    source_rows = {key:dict(path=path,sha256=sha((REPO/path).read_bytes())) for key,path in SOURCE_PATHS.items()}
    texts = {key:(REPO/path).read_text(encoding="utf-8-sig") for key,path in SOURCE_PATHS.items()}
    steam = steam_identity()
    adapter = json.loads(texts["adapterProfile"])
    require(adapter["id"] == "steam-"+steam["buildid"] and adapter["gameAssemblySha256"] == EXPECTED["GameAssembly.dll"],
            "Fixed adapter build identity differs from the actual Steam installation")
    managed_declarations = (
        ("byte","Start","IntPtr owner, IntPtr enumerator, IntPtr nameArgument, IntPtr outputNode"),
        ("uint","Execute","IntPtr node, IntPtr outputByte"),
        ("byte","Release","IntPtr node"),
        ("void","Detach","IntPtr node"),
    )
    for result,name,arguments in managed_declarations:
        require(re.search(r"\[UnmanagedFunctionPointer\(CallingConvention\.Cdecl\)\]\s*internal delegate "+
                          re.escape(result+" "+name+"("+arguments+");"),texts["managedTypes"]),
                "Typed managed native ABI source differs: "+name)
    for text in ("RizFixedHelperSlot = 28;",):
        require(text in texts["fixedConfiguration"],"Fixed helper slot source differs")
    require('L"RizToolbox.EngineNodeKernel.dll"' in texts["fixedModuleProfile"]
            and "GetAbiVersion() { return 2; }" in texts["nativeSnapshot"],"Kernel module/transport ABI source differs")
    for name,size in (("Frame",112),("Completion",32),("Info",112)):
        require("static_assert(sizeof(RizEngineNode"+name+"V1) == "+str(size)+");" in texts["nativeTransport"],
                "Fixed scalar record size source differs: "+name)
    images = {key:Image(name) for key,name in (("Executable","Rizline.exe"),("GameAssembly","GameAssembly.dll"),("UnityPlayer","UnityPlayer.dll"))}
    metadata_path = GAME/"Rizline_Data/il2cpp_data/Metadata/global-metadata.dat"
    metadata_bytes = metadata_path.read_bytes()
    require(sha(metadata_bytes) == EXPECTED["Rizline_Data/il2cpp_data/Metadata/global-metadata.dat"],"Fixed metadata hash differs")
    # This fixed file is opaque on disk; its hash is known, its format/version
    # are not. Do not interpret arbitrary first DWORDs as a standard header.
    require(len(metadata_bytes) >= 32 and metadata_bytes[:4] != bytes.fromhex("af1bb1fa"),
            "Fixed opaque metadata header no longer matches the observed format boundary")
    unity = images["UnityPlayer"]
    functions = []
    prefixes = re.search(r"prefixes\[4\]\[18\]\{(.*?)\n\};",texts["fixedPrefixes"],re.S)
    require(prefixes is not None,"Canonical fixed prefix table changed")
    prefix_rows = re.findall(r"\{([^{}]+)\}",prefixes.group(1))
    require(len(prefix_rows) == 4,"Canonical prefix row count differs")
    for kind,(name,rva,length,abi) in enumerate(DECLARATIONS):
        match = re.search(r"NativeEngineNodeKind\."+name+r"\s*=>\s*(0x[0-9A-Fa-f]+)",texts["managedTypes"])
        require(match and int(match.group(1),16) == rva,"Managed actual kind RVA differs")
        function = unity.function(rva,name)
        old = next(item for item in prior["unityFunctions"] if item.get("label") == name)
        require(function["beginRva"] == int(old["beginRva"],16) and function["endRva"] == int(old["endRva"],16)
                and function["bodyBytes"] == old["bodyBytes"] and function["bodySha256"] == old["bodySha256"],
                "Actual full function differs from prior evidence: "+name)
        require([(item["beginRva"],item["endRva"],item["unwindRva"]) for item in function["runtimeFunctions"]] ==
                [(int(item["beginRva"],16),int(item["endRva"],16),int(item["unwindRva"],16)) for item in old["runtimeFunctions"]],
                "Actual complete pdata group differs from prior evidence: "+name)
        require(all(item["unwind"]["rawHex"][:8] == old_item["unwindHeader"]
                    for item,old_item in zip(function["runtimeFunctions"],old["runtimeFunctions"])),
                "Actual unwind headers differ from prior evidence: "+name)
        prefix = unity.read(rva,length)
        require(prefix == bytes(int(value,16) for value in re.findall(r"0x([0-9a-fA-F]+)",prefix_rows[kind])),
                "Actual game prefix differs from frozen static original: "+name)
        require(sum(item.size for item in unity.md.disasm(prefix,rva)) == length,"Prefix cuts a real instruction")
        require(all(item["unwind"]["languageSpecificDataComplete"] for item in function["runtimeFunctions"]),
                "Engine function has unbounded language-specific unwind data")
        function.update(kind=kind,abi=abi,argumentCount=[4,2,1,1][kind],returnBytes=[1,4,1,0][kind],
                        callingConvention="Windows x64 Cdecl",hasIl2CppMethodInfo=False,
                        prefixBytes=length,prefixHex=prefix.hex(),continuationRva=rva+length)
        functions.append(function)
    require(len(functions[2]["runtimeFunctions"]) == 3
            and [row["unwind"]["flags"] for row in functions[2]["runtimeFunctions"]] == [0,4,4],
            "Release must retain the actual primary plus two chained runtime functions")
    for snippet in ("address + 0x58","address + 0x10","address + 0x18","address + 0x20","address + 0x60"):
        require(snippet in texts["nativeSnapshot"],"Actual scalar snapshot source no longer has the documented offset")
    require("uint32(node" in texts["nativeTransport"] and "0x10" in texts["rootedOwner"]
            and '"m_CachedPtr"' in texts["rootedOwner"],"ABI or rooted owner metadata source differs")
    image_rows = {key:image.metadata() for key,image in images.items()}
    for row in image_rows.values():
        old = next(item for item in prior["images"] if Path(item["path"]).name == row["name"])
        require(row["sha256"] == old["sha256"] and row["fileBytes"] == old["bytes"]
                and row["imageSize"] == int(old["imageSize"],16),"Fixed image facts differ from prior research")
    main_exports = {row["name"]:row["rva"] for row in image_rows["UnityPlayer"]["exports"] if row["name"] in ("UnityMain","UnityMain2")}
    require(main_exports == {"UnityMain":0x808690,"UnityMain2":0x808690},"UnityMain export identity changed")
    require(image_rows["UnityPlayer"]["tlsCallbacks"] == [0x17e7708,0x17e7780],"Actual two TLS callback identities changed")
    edge_exe = [row for row in image_rows["Executable"]["imports"] if row["module"].lower() == "unityplayer.dll"]
    edge_winhttp = [row for row in image_rows["UnityPlayer"]["imports"] if row["module"].lower() == "winhttp.dll"]
    require(len(edge_exe) == 1 and not edge_exe[0]["delay"]
            and any(row["name"] == "UnityMain2" and row["iatRva"] == 0xe240 for row in edge_exe[0]["symbols"]),"Actual EXE ordinary UnityMain2 edge differs")
    require(len(edge_winhttp) == 1 and not edge_winhttp[0]["delay"]
            and {row["name"] for row in edge_winhttp[0]["symbols"]} == {"WinHttpOpen","WinHttpGetProxyForUrl","WinHttpGetIEProxyConfigForCurrentUser","WinHttpCloseHandle"},
            "Actual UnityPlayer ordinary four-name WinHTTP edge differs")
    facts = dict(profileVersion=1,buildId=adapter["id"],steamInstallation=steam,images=image_rows,
        metadata=dict(path=str(metadata_path),sha256=sha(metadata_bytes),fileBytes=len(metadata_bytes),
            first32BytesHex=metadata_bytes[:32].hex(),standardIl2CppMagicPresent=False,version=None,
            format="unknown opaque disk bytes; no decoder or format claim"),
        functions=functions,unityMain=main_exports,
        unityMainFunction=unity.function(0x808690,"UnityMain/UnityMain2 shared entry"),
        tlsFunctions=[unity.function(rva,"Unity TLS callback") for rva in image_rows["UnityPlayer"]["tlsCallbacks"]],
        ordinaryImportEdges=dict(executableToUnity=edge_exe[0],unityToWinHttp=edge_winhttp[0]),
        scalarOffsets=dict(owner=0x58,enumeratorHandle=0x10,enumeratorCache=0x18,enumeratorType=0x20,referenceCount=0x60,managedCachedPtr=0x10),
        fieldEvidenceSources=source_rows,
        capabilities=dict(productionFirstInstallAuthorized=False,completeStartupAdmissionKnown=False,allIndirectCallersClosed=False,
            unknownFieldsDoNotGrantCapabilities=True,nodeAfterReadAuthorized=False,handleIsEnumeratorPointer=False,
            nativeOwnerDereferenceAuthorized=False,schedulerCancellationClosureKnown=False,managedReceiverRootRequired=True))
    facts["factsSha256"] = sha(json.dumps(facts,sort_keys=True,separators=(",",":")).encode())
    header = make_header(facts)
    # Re-read every original and evidence source before emitting any artifact.
    require(all(sha((GAME/name).read_bytes()) == digest for name,digest in EXPECTED.items()),"Original input changed during extraction")
    require(sha(PRIOR.read_bytes()) == PRIOR_SHA and all(sha((REPO/row["path"]).read_bytes()) == row["sha256"] for row in source_rows.values()),
            "Source/evidence input changed during extraction")
    require(steam_identity() == steam,"Steam installation identifiers changed during extraction")
    HEADER.parent.mkdir(parents=True,exist_ok=True)
    HEADER.write_text(header,encoding="utf-8")
    result = dict(schemaVersion=1,kind="fixed-engine-game-profile-disk-facts",passed=True,
        tools=dict(python=sys.version,pefile=pefile.__version__,capstone=capstone.__version__,idaBackendUsed=False),
        scriptSha256=sha(Path(__file__).read_bytes()),priorEvidence=dict(path=str(PRIOR.relative_to(REPO)),sha256=PRIOR_SHA),
        originalHashesMatched=4,inputHashesUnchanged=True,sourceInputsUnchanged=True,facts=facts,
        generatedHeader=dict(path=str(HEADER.relative_to(REPO)),sha256=sha(HEADER.read_bytes())),
        codeExecuted=False,imagesLoaded=False,childStarted=False,gameModified=False,readyGranted=False,V4Issued=False)
    target = HERE/"engine-game-profile.json"
    target.write_text(json.dumps(result,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    lines = ["# 固定引擎游戏profile：磁盘事实", "", "2026-10-25。仅静态提取，profileVersion=1；不授首次写入、Configure、Ready或V4。已按ida-reverse路由核工具，当前无可调用IDA后端；实际使用Python/pefile/Capstone离线读取，没有加载DLL、构建、child、游戏/UI或安装。", "",
        "## Evidence → Finding → Path", "", "| Evidence | 实际发现 | Path与限制 |", "|---|---|---|",
        "| 当前四原始文件hash、[完整JSON](engine-game-profile.json) | EXE/GameAssembly/UnityPlayer/global-metadata固定SHA全部匹配，结束前再次核对未变 | 只限定此固定版本，Windows/游戏更新后重新提取 |",
        "| [旧installation证据](../../foundation-engine-binding-20261025/research/engine-installation-evidence.json) | 四完整函数字节/hash、全部pdata行及三PE大小/hash逐项相等 | 本批补齐精确UNWIND_INFO及可引用常量，不改写旧原件 |",
        "| [生成头文件](../../../../native/RizToolbox.EngineBootstrap/EngineGameProfile.h) | 定长constexpr身份、四prefix/完整body、六个unwind记录及全部RF链 | 不移除RIZFIXEDOWNEDV1旧fixture marker，也不调用publisher |", "",
        "| kind | RVA–完整body末端 | 前缀长度 | 实际RF数 | ABI（无隐藏MethodInfo） |", "|---|---|---:|---:|---|"]
    for row in functions:
        lines.append("| %s | 0x%x–0x%x | %d | %d | %s |"%(row["label"],row["beginRva"],row["endRva"],row["prefixBytes"],len(row["runtimeFunctions"]),row["abi"]))
    lines += ["", "Release三个RF必须保留：primary 0x78f680–0x78f69d/unwind1ac7b88；第二段0x78f69d–0x78f74b/unwind1b27a7c和第三段0x78f74b–0x78f768/unwind1b27a90均CHAININFO回同primary。不能以一个虚构的连续RF替代。JSON含所有原始unwind字节、slot解码及chain三元组；本批四函数均无未定长语言特定handler数据。", "",
        "UnityMain与UnityMain2同RVA808690；Unity两个TLS实际RVA17e7708/17e7780，完整有界函数/真实RF也保留在JSON。EXE普通导入UnityMain2的IAT为e240；UnityPlayer普通按名导入四个WinHTTP名称。没有因为有这条静态边就推导实际首次调用/系统初始化顺序。", "",
        "固定 global-metadata.dat 的磁盘首DWORD不是标准IL2CPP magic（首32字节00c000c020f887004de059b56363dc57301b33844f63cc403cd9e4ca176766a2）。保留完整文件SHA/长度/原始前32字节；format未知、version=null，不能把不透明字节解释成metadata版本。本批未解码或加载该文件。", "",
        "node owner58、enumHandle10/cache18/type20、refCount60来自现有native复制实现；managed m_CachedPtr10来自现有receiver root+metadata重验实现，并与旧固定icall机器码研究对应。enumHandle不是IEnumerator指针，refCount不是执行深度，不读original返回后的node；receiver root、GC/销毁竞争和实际owner活性仍须外部合同。这些字段仅声明已知标量来源，没有未知字段被填造。", "",
        "当前缺口仍是完整启动准入/既有执行者历史、生产固定publisher和V4封存、真实GC/coordinator/scheduler/取消退休闭包。header明确FactsOnly=true、ProductionFirstInstallAuthorized=false；profileVersion1不是Kernel ABI2/scalar1，也不是fixture publisher2或early wire3。完整B01–B19未因此完成。", "", "## 固定性及复核", "",
        "从仓库根执行：", "", "```powershell", "C:/Python314/python.exe docs/validation/foundation-doorstop-cold-20261025/research/extract-engine-game-profile.py", "```", "",
        "该命令仅重读固定输入并重建本批三个输出；任何hash/字节/RF/chain/ABI来源断言失败即停止，不能忽略失败继续签事实。", "",
        "- script SHA256：`"+result["scriptSha256"]+"`", "- JSON SHA256：`"+sha(target.read_bytes())+"`", "- header SHA256：`"+result["generatedHeader"]["sha256"]+"`", "- canonical facts SHA256：`"+facts["factsSha256"]+"`", ""]
    (HERE/"engine-game-profile.md").write_text("\n".join(lines),encoding="utf-8")
    print(json.dumps(dict(passed=True,originalHashesMatched=4,functions=4,runtimeFunctions=sum(len(row["runtimeFunctions"]) for row in functions),
        engineUnwindRecords=len({row["unwindRva"] for function in functions for row in function["runtimeFunctions"]}),
        headerSha256=result["generatedHeader"]["sha256"],factsSha256=facts["factsSha256"],V4Issued=False)))


if __name__ == "__main__":
    main()
