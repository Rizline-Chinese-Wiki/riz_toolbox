"""Offline proof checks for the fixed System32 WinHTTP forwarding fixture.

Uses only the standard library. Never loads WinHTTP, starts a child or sends a
request. The parent owns execution, immutable input handles and raw provenance.
"""
import argparse
import ast
import ctypes
import hashlib
import json
import os
import struct
import sys
import types
from pathlib import Path


HERE = Path(__file__).resolve().parent
PE_READER_PATH = HERE.parent / "foundation-early-fixed-20261025/verify-early-fixed.py"
PE_READER_SHA256 = "056b494994c86558444d873da2a841c99fd31cd82297884fa41b288f20730d64"
API_NAMES = ["WinHttpOpen", "WinHttpGetProxyForUrl", "WinHttpGetIEProxyConfigForCurrentUser", "WinHttpCloseHandle"]
PRODUCTION_EXPORTS = sorted(API_NAMES + ["RizWinHttpForward_"+name for name in ("Initialize","GetInfo","GetVersion")])
TEST_EXPORT = "RizWinHttpForward_SetFault"


def require(value,message):
    if not value:
        raise ValueError(message)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def unique_object(pairs):
    result = {}
    for key,value in pairs:
        require(key not in result,"Duplicate JSON object key")
        result[key] = value
    return result


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig"),object_pairs_hook=unique_object)


def pinned_pe_reader():
    data = PE_READER_PATH.read_bytes()
    require(sha(data) == PE_READER_SHA256,"Unknown frozen PE reader input")
    source = ast.parse(data.decode("utf-8-sig"),filename=str(PE_READER_PATH))
    selected = [node for node in source.body if isinstance(node,(ast.FunctionDef,ast.ClassDef))
                and node.name in {"EvidenceError","require","sha","inspect_pe"}]
    require(len(selected) == 4 and {node.name for node in selected} == {"EvidenceError","require","sha","inspect_pe"},
            "Frozen PE reader definitions changed")
    module = types.ModuleType("pinned_winhttp_disk_pe_reader")
    module.__dict__.update(hashlib=hashlib,struct=struct,Path=Path)
    tree = ast.Module(body=selected,type_ignores=[])
    exec(compile(tree,str(PE_READER_PATH),"exec"),module.__dict__)
    return module.inspect_pe


def windows_api():
    require(os.name == "nt","System32 file identity requires Windows")
    # This is the already resident OS API, never the target WinHTTP module.
    return ctypes.WinDLL("kernel32",use_last_error=True)


def system_winhttp_path():
    api = windows_api().GetSystemDirectoryW
    api.argtypes = [ctypes.c_wchar_p,ctypes.c_uint32]
    api.restype = ctypes.c_uint32
    buffer = ctypes.create_unicode_buffer(32768)
    count = api(buffer,len(buffer))
    require(0 < count < len(buffer),"GetSystemDirectoryW failed or truncated")
    path = Path(buffer.value)/"winhttp.dll"
    require(path.is_absolute(),"System directory is not absolute")
    return path


def disk_identity(image):
    import msvcrt
    class FileTime(ctypes.Structure):
        _fields_ = [("low",ctypes.c_uint32),("high",ctypes.c_uint32)]
    class Information(ctypes.Structure):
        _fields_ = [("attributes",ctypes.c_uint32),("creation",FileTime),("access",FileTime),
                    ("write",FileTime),("volume",ctypes.c_uint32),("sizeHigh",ctypes.c_uint32),
                    ("sizeLow",ctypes.c_uint32),("links",ctypes.c_uint32),
                    ("indexHigh",ctypes.c_uint32),("indexLow",ctypes.c_uint32)]
    api = windows_api().GetFileInformationByHandle
    api.argtypes = [ctypes.c_void_p,ctypes.POINTER(Information)]
    api.restype = ctypes.c_int
    def get(handle):
        info = Information()
        require(api(handle,ctypes.byref(info)) != 0,"Cannot identify parent held file")
        return (info.volume,(info.indexHigh<<32)|info.indexLow,(info.sizeHigh<<32)|info.sizeLow)
    with Path(image["path"]).open("rb") as stream:
        handle = ctypes.c_void_p(msvcrt.get_osfhandle(stream.fileno()))
        before = get(handle)
        data = stream.read()
        after = get(handle)
    require(before == after and before[2] == len(data) == image["bytes"] and sha(data) == image["sha256"],
            "Parent file identity and PE byte hash disagree")
    return dict(method="GetFileInformationByHandle",volumeSerial=before[0],fileIndex=before[1],
                bytes=before[2],imageSha256=image["sha256"])


def rva_bytes(image,rva,length):
    section = next((row for row in image["sections"] if row["rva"] <= rva
                    and rva+length <= row["rva"]+row["rawSize"]),None)
    require(section is not None,"RVA bytes are not fully backed by a held disk section")
    data = Path(image["path"]).read_bytes()
    require(sha(data) == image["sha256"],"PE bytes changed after inspection")
    offset = section["rawOffset"]+rva-section["rva"]
    return data[offset:offset+length]


def export_shape(image,names):
    exports = image["exports"]
    require(sorted(row["name"] for row in exports) == sorted(names)
            and all(row["rva"] and not row["forwarder"] for row in exports),
            "Forwarder has unexpected, forwarded or missing named exports")
    data = Path(image["path"]).read_bytes()
    nt = struct.unpack_from("<I",data,0x3c)[0]
    opt = nt+24
    export_rva,export_size = struct.unpack_from("<II",data,opt+112)
    raw = rva_bytes(image,export_rva,40)
    function_count,name_count = struct.unpack_from("<II",raw,20)
    require(function_count == name_count == len(names) and len({row["ordinal"] for row in exports}) == len(names),
            "Forwarder hides extra ordinal-only exports or export aliases")
    return {row["name"]:row for row in exports}


def no_initializer(image):
    require(image["machine"] == 0x8664 and image["characteristics"]&0x2000 and image["entryRva"] == 0,
            "Forwarder must be an AMD64 DLL without an entry point")
    data = Path(image["path"]).read_bytes()
    nt = struct.unpack_from("<I",data,0x3c)[0]
    opt = nt+24
    count = struct.unpack_from("<I",data,opt+108)[0]
    require(15 <= count <= 16 and struct.unpack_from("<H",data,nt+20)[0] >= 112+8*count,
            "Forwarder directory array is truncated")
    require(all(struct.unpack_from("<II",data,opt+112+8*i) == (0,0) for i in (9,10,13,14))
            and image["tls"] is None and not image["delayImports"],
            "Forwarder has unexpected TLS/load-config/delay/CLR initialization")


def inspect_tail_stubs(image,exports):
    rows = []
    pointer_rvas = set()
    for kind,name in enumerate(API_NAMES):
        rva = exports[name]["rva"]
        prefix = rva_bytes(image,rva,11)
        require(prefix[:3] == bytes.fromhex("4c8b1d"),"Typed forwarder must first load its active table into volatile R11")
        pointer_rva = rva+7+struct.unpack_from("<i",prefix,3)[0]
        suffix = bytes.fromhex("41ff23") if kind == 0 else bytes.fromhex("41ff63")+bytes([kind*8])
        require(prefix[7:7+len(suffix)] == suffix,"Typed forwarder must tail-jump through its exact fixed slot")
        section = next((row for row in image["sections"] if row["rva"] <= rva
                        and rva+7+len(suffix) <= row["rva"]+row["rawSize"]),None)
        require(section and section["characteristics"]&0x20000000 and not section["characteristics"]&0x80000000,
                "Typed stub must reside in a nonwritable executable disk section")
        pointer_section = next((row for row in image["sections"] if row["rva"] <= pointer_rva
                                and pointer_rva+8 <= row["rva"]+row["rawSize"]),None)
        require(pointer_rva%8 == 0 and pointer_section and pointer_section["characteristics"]&0x80000000
                and not pointer_section["characteristics"]&0x20000000,"Active table pointer must be aligned writable nonexecutable data")
        pointer_rvas.add(pointer_rva)
        rows.append(dict(kind=kind,name=name,exportRva=rva,activePointerRva=pointer_rva,
                         exactBytes=prefix[:7+len(suffix)].hex()))
    require(len(pointer_rvas) == 1,"Four exported typed stubs must use one atomic active-table pointer")
    return rows


def inspect_forward_image(image,fixture):
    no_initializer(image)
    exports = export_shape(image,PRODUCTION_EXPORTS+([TEST_EXPORT] if fixture else []))
    # Canonical resolver uses only these OS operations. No generic allocator,
    # CRT, networking import, arbitrary delay loader or Dobby dependency enters.
    allowed = {"kernel32.dll": {"CloseHandle","CreateFileW","GetFileInformationByHandle","GetLastError","SetLastError",
        "GetCurrentThreadId","GetModuleFileNameW","GetModuleHandleW","GetProcAddress","GetSystemDirectoryW",
        "LoadLibraryExW","VirtualQuery","CompareStringOrdinal","RaiseFailFastException"},
        "ntdll.dll":{"__C_specific_handler"}}
    actual = []
    for dependency in image["imports"]:
        module = dependency["module"].lower()
        require(module in allowed,"Forwarder imports an undeclared CRT/heap/Dobby/runtime library")
        for symbol in dependency["symbols"]:
            require(symbol.get("name") in allowed[module] and "ordinal" not in symbol,
                    "Forwarder imports an undeclared OS function")
            actual.append((module,symbol["name"]))
    return dict(fixtureProfile=fixture,exactExports=sorted(exports),directImports=sorted(actual),
                noAutomaticInitialization=True,tailStubs=inspect_tail_stubs(image,exports))


def inspect_images(production,fixture,host,importer):
    parse = pinned_pe_reader()
    paths = dict(production=production,fixture=fixture,host=host,importer=importer,system=system_winhttp_path())
    require(all(path.is_absolute() and path.is_file() for path in paths.values()),"All held image paths must be absolute existing files")
    images = {name:parse(path) for name,path in paths.items()}
    for image in images.values():
        image["fileIdentity"] = disk_identity(image)
    require(production.name == fixture.name and production.resolve() != fixture.resolve(),
            "Production/test profiles have the same fixed DLL basename and separate absolute files")
    contracts = dict(production=inspect_forward_image(images["production"],False),fixture=inspect_forward_image(images["fixture"],True))
    ordinary = [row for row in images["importer"]["imports"] if row["module"].lower() == production.name.lower()]
    require(len(ordinary) == 1 and sorted(symbol.get("name") for symbol in ordinary[0]["symbols"]) == sorted(API_NAMES)
            and all("ordinal" not in symbol for symbol in ordinary[0]["symbols"])
            and not any(row["module"].lower() == production.name.lower() for row in images["importer"]["delayImports"]),
            "Owned importer must have the exact four ordinary named imports from this fixed forwarder")
    require(images["importer"]["entryRva"] and images["importer"]["tls"] and images["importer"]["tls"]["callbacks"],
            "Owned importer must retain actual DLL initialization and TLS callbacks")
    system_exports = {row["name"]:row for row in images["system"]["exports"]}
    require(all(name in system_exports and system_exports[name]["rva"] and not system_exports[name]["forwarder"] for name in API_NAMES),
            "This actual System32 WinHTTP must export all four names as direct image addresses")
    return dict(kind="owned-winhttp-forward-pe-proof",schemaVersion=1,passed=True,images=images,contracts=contracts,
                systemApiExports=[dict(kind=kind,**system_exports[name]) for kind,name in enumerate(API_NAMES)],
                transitiveVerifier=dict(path=str(PE_READER_PATH),sha256=PE_READER_SHA256,
                    selectedDefinitions=["EvidenceError","require","sha","inspect_pe"],historicalFileModified=False),
                targetWinHttpLoadedByVerifier=False,childrenStartedByVerifier=0,networkRequestsByVerifier=0)


def decode_info(data):
    require(len(data) == 1248,"Native GetInfo raw record must contain exactly 1248 bytes")
    info = dict(zip(("version","byteSize","state","failure","phase","flags","initializeAttempts","publishCount"),
                    struct.unpack_from("<8I",data)))
    require(info["version"] == 1 and info["byteSize"] == 1248,"Native Info transport header differs from the canonical record")
    info.update(zip(("systemModule","heldSystemFile","selfModule","activeTable","initialTable","poisonTable","resolvedTable",
                     "fileIndex","fileBytes"),struct.unpack_from("<9Q",data,32)))
    info.update(zip(("fileVolumeSerial","systemPathChars","loadedPathChars","peMachine","peImageSize","peTimeDateStamp",
                     "peExportRva","peExportBytes","loadFlags","resolvedCount","lastWin32Error","unknown"),
                    struct.unpack_from("<12I",data,104)))
    info["addresses"] = list(struct.unpack_from("<4Q",data,152))
    info["protections"] = list(struct.unpack_from("<4I",data,184))
    info["faultStage"],info["faultIndex"] = struct.unpack_from("<2I",data,200)
    for name,offset in (("systemPath",208),("loadedPath",728)):
        units = struct.unpack_from("<260H",data,offset)
        require(0 in units,"Native fixed path buffer is not terminated")
        length = units.index(0)
        require(not any(units[length:]),"Native path buffer has nonzero bytes after its terminator")
        value = data[offset:offset+length*2].decode("utf-16-le",errors="strict")
        require(info[name+"Chars"] == length,"Native path length disagrees with actual raw UTF-16 record")
        info[name] = value
    require(info["state"] in range(5) and info["failure"] in range(16) and info["phase"] in range(8)
            and info["flags"] < 128 and info["unknown"] in (0,1) and info["resolvedCount"] <= 4
            and info["faultStage"] <= 10 and info["faultIndex"] < 4,"Native Info has an unknown enum or impossible bounded count")
    return info


def decode_call_row(data):
    require(len(data) == 320,"Typed WinHTTP call row must be exactly 320 bytes")
    row = dict(zip(("version","byteSize","phase","kind","threadId","seed"),struct.unpack_from("<6I",data)))
    require(row["version"] == 1 and row["byteSize"] == 320 and 0 <= row["kind"] < 4
            and 1 <= row["phase"] <= 11 and row["threadId"] != 0,"Typed call row has invalid protocol identity")
    row["sequence"],row["apiAddress"] = struct.unpack_from("<2Q",data,24)
    row["args"] = list(struct.unpack_from("<5Q",data,40))
    row["rawReturn"], = struct.unpack_from("<Q",data,80)
    row.update(zip(("lastErrorBefore","lastErrorAfter","openAccessType","openFlags","autoFlags","autoDetectFlags",
                    "autoReserved","autoLogon"),struct.unpack_from("<8I",data,88)))
    row["autoConfigUrl"],row["autoReservedPointer"] = struct.unpack_from("<2Q",data,120)
    row.update(zip(("outputAccessType","outputAutoDetect","freesAttempted","freesFailed"),struct.unpack_from("<4I",data,136)))
    strings = []
    for index in range(3):
        offset = 152+56*index
        pointer,present,length,complete,reserved = struct.unpack_from("<Q4I",data,offset)
        used = row["kind"] == 2 or (row["kind"] == 1 and index < 2)
        require((not used and not any(data[offset:offset+56])) or
                (used and present in (0,1) and complete == 1 and reserved == 0 and length < 32768
                 and bool(pointer) == bool(present)),"Captured private output string is incomplete or malformed")
        digest = data[offset+24:offset+56].hex()
        require(present or (length == 0 and int(digest,16) == 0),"Absent private string has a nonzero digest or length")
        strings.append(dict(pointer=pointer,present=present,length=length,complete=complete,saltedSha256=digest))
    row["strings"] = strings
    return row


def referenced_bytes(case_path,row,expected_bytes=None):
    name = row["path"]
    require(isinstance(name,str) and name and Path(name).name == name and not Path(name).is_absolute()
            and ":" not in name and "/" not in name and "\\" not in name,"Raw artifact reference must be a same-directory basename")
    data = (case_path.parent/name).read_bytes()
    require(sha(data) == row["sha256"] and (expected_bytes is None or len(data) == expected_bytes),
            "Raw artifact hash or exact length differs from its actual file")
    return data


def normalized_path(value):
    require(isinstance(value,str),"Module path is not a string")
    if value.startswith("\\\\?\\"):
        value = value[4:]
    require(Path(value).is_absolute() and ".." not in Path(value).parts,"Module path must be absolute without parent traversal")
    return str(Path(value)).replace("\\","/").casefold()


def verify_module(actual,image,check):
    file = image["fileIdentity"]
    check(type(actual["base"]) is int and actual["base"] > 0
          and normalized_path(actual["path"]) == normalized_path(image["path"])
          and actual["fileVolumeSerial"] == file["volumeSerial"] and actual["fileIndex"] == file["fileIndex"],
          "Actual loaded module path/base/file identity matches the parent-held exact image")


def table_from_disk(image,module_base,address):
    require(address%8 == 0 and module_base <= address and address+32 <= module_base+image["imageSize"],
            "Static table address is not an aligned four-pointer range inside the held forwarder")
    data = rva_bytes(image,address-module_base,32)
    values = struct.unpack("<4Q",data)
    table_section = next(row for row in image["sections"] if row["rva"] <= address-module_base
                         and address-module_base+32 <= row["rva"]+row["rawSize"])
    require(table_section["characteristics"]&0x40000000 and not table_section["characteristics"]&0xa0000000,
            "Static refusal table must be read-only nonexecutable disk data")
    require(all(image["imageBase"] <= value < image["imageBase"]+image["imageSize"] for value in values),
            "Static refusal table contains a non-image pointer")
    require(all(any(section["rva"] <= value-image["imageBase"] < section["rva"]+section["rawSize"]
                    and section["characteristics"]&0x20000000 and not section["characteristics"]&0x80000000
                    for section in image["sections"]) for value in values),
            "Every static typed refusal target must belong to actual nonwritable executable code")
    return [module_base+value-image["imageBase"] for value in values]


def validate_call(row,forward_addresses,system_addresses,main_thread,check):
    kind,phase,route = row["kind"],row["phase"],row["route"]
    check(row["version"] == 1 and row["byteSize"] == 320 and kind in range(4) and phase in range(1,12)
          and row["threadId"] > 0 and row["sequence"] > 0 and row["lastErrorBefore"] == row["seed"],
          "Every actual typed call retains exact row shape, real thread, sequence and pre-call LastError seed")
    check(route in ("direct","forward","importer") and row["apiAddress"] ==
          (system_addresses[kind] if route == "direct" else forward_addresses[kind]),
          "Call address is the exact held forwarder stub or actual resolved System32 export")
    check((route == "direct" and phase in (2,8)) or (route == "forward" and phase in (1,3,9,10))
          or (route == "importer" and phase in (4,5,6,7,11)),"Call route belongs to its actual loader/outside/worker phase")
    check((phase in (8,9)) == (row["worker"] in (1,2,3)) and
          (row["threadId"] != main_thread if phase in (8,9) else row["threadId"] == main_thread),
          "Worker calls have distinct owned worker identity and initializer/host calls use the real main thread")
    args = row["args"]
    check(len(args) == 5 and all(type(value) is int and 0 <= value < 1<<64 for value in args),
          "All five typed argument scalars are retained without truncation")
    if kind == 0:
        check(args[0] != 0 and args[1] == row["openAccessType"] == 1 and args[2] == args[3] == 0
              and args[4] == row["openFlags"] and row["openFlags"] in (0,0x40000000),
              "Open uses NO_PROXY and retains the full nonzero fifth stack argument when selected")
    else:
        check(row["rawReturn"] < 1<<32 and not row["openAccessType"] and not row["openFlags"],
              "BOOL APIs retain all 32 return bits rather than only AL")
        check(not any(args[4:] if kind == 1 else args[1:]),"Unused typed argument slots are exactly zero")
        if kind == 1:
            check(args[1] and args[2] and args[3] and row["autoFlags"] == row["autoDetectFlags"] == row["autoReserved"]
                  == row["autoLogon"] == row["autoConfigUrl"] == row["autoReservedPointer"] == 0,
                  "Proxy test supplies local invalid input and no autodetection/PAC/reserved/credential options")
        elif kind == 2:
            check(args[0] != 0,"IE config API receives an actual output structure")
    check(row["freesFailed"] == 0 and row["freesAttempted"] == sum(text["present"] for text in row["strings"]),
          "Every returned allocated string is actually freed exactly once without failure")
    if phase in (1,10):
        check(row["rawReturn"] == 0 and row["lastErrorAfter"] == 1114 and not row["pairId"],
              "Unready/poisoned typed calls return NULL/FALSE and ERROR_DLL_INIT_FAILED")
    if phase in (4,5,6,7):
        check(kind in (1,3) and args[0] == 0 and row["rawReturn"] == 0 and not row["pairId"],
              "Actual loader initializers only run the fixed null-handle local refusal workload")


def equivalent_calls(direct,forward):
    if direct["kind"] != forward["kind"] or direct["lastErrorBefore"] != forward["lastErrorBefore"] \
            or direct["lastErrorAfter"] != forward["lastErrorAfter"]:
        return False
    if (bool(direct["rawReturn"]) != bool(forward["rawReturn"])) if direct["kind"] == 0 else (direct["rawReturn"] != forward["rawReturn"]):
        return False
    for name in ("outputAccessType","outputAutoDetect","freesAttempted","freesFailed","openFlags","openAccessType"):
        if direct[name] != forward[name]:
            return False
    return all(all(left[name] == right[name] for name in ("present","length","complete","saltedSha256"))
               for left,right in zip(direct["strings"],forward["strings"]))


def verify_case(args):
    raw = read_json(args.case)
    proof = read_json(args.pe_proof)
    checks = []
    def check(value,message):
        require(value,message)
        checks.append(message)
    check(proof["kind"] == "owned-winhttp-forward-pe-proof" and proof["schemaVersion"] == 1 and proof["passed"] is True
          and proof["transitiveVerifier"]["sha256"] == PE_READER_SHA256,
          "Case uses the exact parent PE proof and pinned offline parser")
    check(raw["kind"] == "winhttp-forward-fixture" and raw["schemaVersion"] == 1
          and raw["actualProcessId"] == args.child_pid > 0 and raw["mainThreadId"] > 0,
          "Actual host PID matches the independent parent-owned child")
    check(raw["passed"] is True and raw["finished"] is True and not raw["error"] and raw["sehCode"] == 0
          and not raw["failures"] and raw["checks"] > 0,"Actual host completed with no assertion or exception failures")
    arguments = raw["arguments"]
    check(arguments["fixtureProfile"] is (args.profile == "fixture") and arguments["faultStage"] == args.fault_stage
          and arguments["faultIndex"] == args.fault_index and (args.profile == "fixture" or args.fault_stage == args.fault_index == 0),
          "Independent parent profile/fault selectors bind this exact case rather than host self-selection")
    check(0 <= args.fault_stage <= 10 and 0 <= args.fault_index < 4
          and (args.fault_stage in (5,6,10) or args.fault_index == 0),"Fault stage/index belongs to the fixed canonical selector set")
    images = proof["images"]
    forward_image,system_image = images[args.profile],images["system"]
    check(normalized_path(arguments["forward"]) == normalized_path(forward_image["path"])
          and normalized_path(arguments["output"]) == normalized_path(str(args.case.resolve())),
          "Actual command forward image and output are the parent-specified absolute artifacts")
    check(normalized_path(system_image["path"]) == normalized_path(str(system_winhttp_path())),
          "System proof belongs to the actual GetSystemDirectoryW winhttp.dll")
    # The runner holds all images for the complete child lifetime. These reads
    # additionally reject stale or mismatched disk identity when proving a case.
    for image in images.values():
        check(disk_identity(image) == image["fileIdentity"],"Current parent disk bytes and file identity match the held PE proof")
    verify_module(raw["forwardModule"],forward_image,check)
    forward_base = raw["forwardModule"]["base"]
    stub_rows = proof["contracts"][args.profile]["tailStubs"]
    forward_addresses = [forward_base+row["exportRva"] for row in stub_rows]
    snapshots = raw["infoSnapshots"]
    labels = ["before","initialized"] if args.fault_stage else ["before","initialized","after-workers","repeated"]
    check([row["label"] for row in snapshots] == labels,"Actual native snapshots occur at all required before/init/workers/repeat boundaries")
    infos = []
    for snapshot in snapshots:
        data = referenced_bytes(args.case,snapshot,1248)
        info = decode_info(data)
        check(snapshot["result"] == 0 and snapshot["lastErrorBefore"] == snapshot["lastErrorAfter"]
              and snapshot["info"] == info,"Native 1248-byte GetInfo output matches JSON and preserves exact caller LastError")
        check(info["selfModule"] == forward_base and len({info[name] for name in ("initialTable","poisonTable","resolvedTable")}) == 3
              and all(info[name]%8 == 0 for name in ("activeTable","initialTable","poisonTable","resolvedTable")),
              "Info retains three distinct aligned static tables in the exact forward image")
        initial = table_from_disk(forward_image,forward_base,info["initialTable"])
        poison = table_from_disk(forward_image,forward_base,info["poisonTable"])
        initial_pointer = struct.unpack("<Q",rva_bytes(forward_image,stub_rows[0]["activePointerRva"],8))[0]
        check(initial_pointer-forward_image["imageBase"] == info["initialTable"]-forward_base,
              "Actual initial table is independently tied to all four stubs' single disk active-pointer initializer")
        check(initial == poison and snapshot["initialSlots"] == initial and snapshot["poisonSlots"] == poison,
              "Actual distinct refusal tables independently match held disk four typed function pointers")
        check(snapshot["resolvedSlots"] == info["addresses"] and snapshot["activeSlots"] ==
              (info["addresses"] if info["activeTable"] == info["resolvedTable"] else poison if info["activeTable"] == info["poisonTable"] else initial)
              and info["activeTable"] in (info["initialTable"],info["poisonTable"],info["resolvedTable"]),
              "Actual four-slot active snapshot selects only one complete known table")
        check(info["faultStage"] == args.fault_stage and info["faultIndex"] == args.fault_index,
              "Actual native Info preserves the parent-selected fault identity")
        infos.append(info)
    before,initialized = infos[:2]
    check(before["state"] == before["failure"] == before["phase"] == before["flags"] == before["initializeAttempts"]
          == before["publishCount"] == before["resolvedCount"] == before["unknown"] == 0
          and before["activeTable"] == before["initialTable"] and not any(before["addresses"])
          and not before["systemModule"] and not before["heldSystemFile"],"Fresh native resolver is unattempted, unpublished and uses its initial refusal table")
    check(all(all(info[name] == before[name] for name in ("selfModule","initialTable","poisonTable","resolvedTable"))
              for info in infos),"Every snapshot retains the same actual static table identities")
    init = raw["initialize"]
    if args.profile == "fixture":
        check(init["faultSelection"] == 0 and init["faultRepeatSelection"] == 2,
              "Fixed test selector is selected once and a second setter cannot reset it")
    else:
        check(init["faultSelection"] == init["faultRepeatSelection"] == 0,
              "Production profile has no test selector path")
    system_base = initialized["systemModule"]
    system_addresses = [system_base+row["rva"] if system_base else 0 for row in proof["systemApiExports"]]
    expected_fields = {
        1:(1,0,0,13,0),2:(2,0,0,13,0),3:(3,1,0,13,0),4:(3,3,0,13,0),
        5:(5,15,args.fault_index,11,0),6:(5,15,args.fault_index,12,0),7:(6,31,4,13,0),
        8:(6,95,4,2,1),9:(4,3,0,9,0),10:(5,15,args.fault_index,13,0)}
    if args.fault_stage:
        phase,flags,count,failure,unknown = expected_fields[args.fault_stage]
        check(initialized["phase"] == phase and initialized["flags"] == flags and initialized["resolvedCount"] == count
              and initialized["failure"] == failure and initialized["unknown"] == unknown
              and initialized["state"] == (4 if unknown else 3) and initialized["publishCount"] == 0
              and initialized["initializeAttempts"] == (2 if args.fault_stage == 8 else 1)
              and initialized["activeTable"] == initialized["poisonTable" if unknown else "initialTable"]
              and init["result"] == 1 and init["lastErrorAfter"] == initialized["lastWin32Error"] != 0,
              "Actual injected native phase/count/failure/retained responsibility agrees with its exact selector")
        check(initialized["addresses"] == system_addresses[:count]+[0]*(4-count)
              and all(value != 0 for value in initialized["protections"][:count]) and not any(initialized["protections"][count:]),
              "Partial resolver keeps only the exact by-name prefix of real system slots and never publishes it")
        check(not raw["pairs"] and not raw["importerSnapshot"]["path"] and not raw["importerModule"]["base"],
              "Resolver refusal never enters importer, worker or direct-vs-forward comparison workloads")
    else:
        check(init["result"] == 0 and init["lastErrorAfter"] == init["lastErrorBefore"]
              and initialized["state"] == 2 and initialized["phase"] == 7 and initialized["flags"] == 63
              and initialized["failure"] == initialized["unknown"] == initialized["lastWin32Error"] == 0
              and initialized["initializeAttempts"] == initialized["publishCount"] == 1
              and initialized["resolvedCount"] == 4 and initialized["activeTable"] == initialized["resolvedTable"]
              and initialized["addresses"] == system_addresses,"Successful resolver publishes all four real named system slots once and preserves caller LastError")
        check(infos[2] == initialized,"Worker workload cannot alter resolver state, counts or retained tables")
        repeated = infos[3]
        check(init["repeatResult"] == 1 and repeated["state"] == 4 and repeated["unknown"] == 1 and repeated["failure"] == 2
              and repeated["flags"] == 127 and repeated["publishCount"] == 1 and repeated["initializeAttempts"] == 2
              and repeated["activeTable"] == repeated["poisonTable"] and repeated["lastWin32Error"] == 1114,
              "Actual repeated Initialize permanently poisons future calls rather than republishing")
        changing = {"state","unknown","failure","flags","initializeAttempts","activeTable","lastWin32Error"}
        check(all(value == repeated[name] for name,value in initialized.items() if name not in changing),
              "Repeated initialization retains system module/file and all resolved addresses as permanent responsibility")
        verify_module(raw["systemModule"],system_image,check)
        verify_module(raw["importerModule"],images["importer"],check)
        check(normalized_path(arguments["importer"]) == normalized_path(images["importer"]["path"]),
              "Actual importer uses the held exact ordinary-import image")
        for kind,slot in enumerate(raw["slots"]):
            check(slot["kind"] == kind and slot["address"] == system_addresses[kind]
                  and slot["allocationBase"] == system_base and slot["error"] == 0 and slot["state"] == 0x1000
                  and slot["type"] == 0x1000000 and slot["protect"] == initialized["protections"][kind]
                  and slot["protect"]&0xff == 0x20 and not slot["protect"]&0x100
                  and slot["base"] <= slot["address"] < slot["base"]+slot["regionSize"],
                  "Actual resolved slot VirtualQuery belongs to exact System32 MEM_IMAGE executable page")
    if initialized["heldSystemFile"]:
        fid = system_image["fileIdentity"]
        check(initialized["fileIndex"] == fid["fileIndex"] and initialized["fileVolumeSerial"] == fid["volumeSerial"]
              and initialized["fileBytes"] == system_image["bytes"],"Native retained file identity equals parent-held System32 bytes")
    if initialized["systemPath"]:
        check(normalized_path(initialized["systemPath"]) == normalized_path(system_image["path"]),
              "Native resolver uses only the exact fixed System32 WinHTTP path")
    if initialized["loadedPath"]:
        check(normalized_path(initialized["loadedPath"]) == normalized_path(system_image["path"]),
              "Actually loaded module path agrees with fixed system file rather than search-order alias")
    if initialized["peMachine"]:
        data = Path(system_image["path"]).read_bytes();opt=struct.unpack_from("<I",data,0x3c)[0]+24
        export_rva,export_bytes = struct.unpack_from("<II",data,opt+112)
        check(initialized["peMachine"] == system_image["machine"] and initialized["peImageSize"] == system_image["imageSize"]
              and initialized["peTimeDateStamp"] == system_image["timeDateStamp"]
              and (initialized["peExportRva"],initialized["peExportBytes"]) == (export_rva,export_bytes),
              "Actual loaded System32 PE metadata matches independently parsed held disk header")
    check(initialized["loadFlags"] == (0x800 if initialized["systemModule"] else 0),
          "Resolver uses fixed LOAD_LIBRARY_SEARCH_SYSTEM32 when it actually loads the module")
    calls = raw["calls"]
    calls_meta = raw["callsSnapshot"]
    check(calls_meta["rowByteSize"] == 320 and calls_meta["rowCount"] == len(calls),"Raw call snapshot has exact canonical row count/size")
    calls_data = referenced_bytes(args.case,calls_meta,len(calls)*320)
    for index,row in enumerate(calls):
        decoded = decode_call_row(calls_data[index*320:(index+1)*320])
        check(all(row[name] == value for name,value in decoded.items()),
              "Every typed API JSON field equals its exact independently decoded native call row")
        validate_call(row,forward_addresses,system_addresses,raw["mainThreadId"],check)
    host_calls = [row for row in calls if row["route"] != "importer"]
    check(sorted(row["sequence"] for row in host_calls) == list(range(1,len(host_calls)+1)),
          "Actual host/worker call sequence has no duplicates, missing rows or truncation")
    refusal_groups = [[row for row in calls if row["phase"] == phase] for phase in (1,10)]
    check(all(sorted(row["kind"] for row in group) == [0,1,2,3] for group in refusal_groups),
          "All four actual typed APIs are refused both before initialization and after failure/repeat")
    pair_rows = {}
    for pair in raw["pairs"]:
        group = [row for row in calls if row["pairId"] == pair["id"]]
        check(pair["id"] not in pair_rows and len(group) == 2 and {row["route"] for row in group} == {"direct","forward"},
              "Each comparison pair has one exact direct and one exact forwarded actual call")
        direct = next(row for row in group if row["route"] == "direct")
        forwarded = next(row for row in group if row["route"] == "forward")
        check(pair["kind"] == direct["kind"] == forwarded["kind"] and pair["directSequence"] == direct["sequence"]
              and pair["forwardSequence"] == forwarded["sequence"] and pair["equal"] is True
              and direct["worker"] == forwarded["worker"] and equivalent_calls(direct,forwarded),
              "Independent pair comparison preserves actual full return, LastError and private output semantics")
        pair_rows[pair["id"]] = group
    check(sorted(pair_rows) == list(range(1,len(pair_rows)+1))
          and all(row["pairId"] in pair_rows for row in calls if row["route"] == "direct" or row["phase"] in (3,9)),
          "Every successful-path direct/forward call belongs to exactly one complete comparison pair")
    for group in pair_rows.values():
        for opened in group:
            if opened["kind"] != 0 or opened["rawReturn"] == 0:
                continue
            closed = [row for row in calls if row["kind"] == 3 and row["route"] == opened["route"]
                      and row["worker"] == opened["worker"] and row["threadId"] == opened["threadId"]
                      and row["args"][0] == opened["rawReturn"] and row["sequence"] > opened["sequence"]]
            # Handles can be reused in a later round; the first matching close
            # in the same thread/route must close this particular successful Open.
            check(closed and min(closed,key=lambda row:row["sequence"])["rawReturn"] != 0,
                  "Every successful direct/forward session Open has an actual associated successful Close")
    if args.fault_stage:
        check(len(calls) == 8 and not pair_rows,"Injected failure contains exactly eight actual initial/final refusal calls")
    else:
        check(len(raw["slots"]) == 4,"Normal proof preserves all four actual module/page slot observations")
        importer_meta = raw["importerSnapshot"]
        data = referenced_bytes(args.case,importer_meta,10272)
        version,size,mask,count,host_checks,failures = struct.unpack_from("<4I2Q",data)
        check((version,size,mask,count) == (1,10272,0x8f0,12) and failures == 0 and host_checks > 0
              and all(importer_meta[name] == value for name,value in zip(("version","byteSize","phaseMask","rowCount","checks","failures"),
                  (version,size,mask,count,host_checks,failures))),"Actual importer binary retains all four real initializer phases and outside-loader phase")
        importer_calls = [row for row in calls if row["route"] == "importer"]
        decoded = [decode_call_row(data[32+index*320:32+(index+1)*320]) for index in range(count)]
        check(len(importer_calls) == count and not any(data[32+count*320:])
              and all(all(row[name] == value for name,value in actual.items()) for row,actual in zip(importer_calls,decoded))
              and [row["sequence"] for row in importer_calls] == list(range(1,13)),
              "All importer JSON calls exactly match bounded independently decoded snapshot rows")
        for phase in (4,5,6,7):
            check([row["kind"] for row in importer_calls if row["phase"] == phase] == [3,1],
                  "Each real loader initializer executes only Close(NULL) then local Proxy(NULL) refusal")
        outside = [row for row in importer_calls if row["phase"] == 11]
        check([row["kind"] for row in outside] == [0,2,1,3] and outside[0]["rawReturn"] != 0
              and outside[2]["rawReturn"] == 0 and outside[3]["rawReturn"] != 0
              and outside[2]["args"][0] == outside[3]["args"][0] == outside[0]["rawReturn"],
              "Importer positive four-API workload executes outside loader and closes its exact own session")
        workers = [row for row in calls if row["phase"] in (8,9)]
        check(len({row["threadId"] for row in workers}) == 3
              and {row["worker"] for row in workers} == {1,2,3}
              and all(len([row for row in workers if row["worker"] == worker]) == 40 for worker in (1,2,3)),
              "Three actual worker threads each complete four five-pair typed forwarding rounds")
        check(any(row["kind"] == 0 and row["openFlags"] == 0x40000000 for row in calls if row["pairId"]),
              "Actual comparison includes the nonzero fifth Open stack argument")
    scope = raw["scope"]
    check(all(scope[name] is False for name in ("httpRequestsIssued","autoDetectOrPacEnabled","settingsModified",
              "privateConfigStringsLogged","loaderLockResolverSafetyVerified","gameExecuted","installed"))
          and scope["systemModuleLoadedByResolverOnly"] is True,"Host scope records only local safe calls and no game/install/loader-safety upgrade")
    return dict(kind="owned-winhttp-forward-parent-case-proof",schemaVersion=1,passed=True,checks=len(checks),assertions=checks,
                actualChildPid=args.child_pid,profile=args.profile,faultStage=args.fault_stage,faultIndex=args.fault_index,
                hostRawSha256=sha(args.case.read_bytes()),peProofSha256=sha(args.pe_proof.read_bytes()),
                apiCalls=len(calls),directForwardPairs=len(pair_rows),nativeInfos=infos,
                systemImageSha256=system_image["sha256"],forwardImageSha256=forward_image["sha256"],
                faultMechanism=("actual-recursive-initialize" if args.fault_stage == 8 else
                    "fixed-wrong-export-name" if args.fault_stage == 5 else "fixed-self-address-substitution" if args.fault_stage == 6 else
                    "fixed-file-identity-comparison" if args.fault_stage == 9 else "forced-fixed-stage-result" if args.fault_stage else "none"),
                naturalOsFailureCoverageClaim=False,wholeSystemLoaderSafetyVerified=False,productionV4Issued=False,
                gameExecuted=False,installed=False,engineCancellationCompleted=False,childrenStartedByVerifier=0)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inspect",action="store_true")
    parser.add_argument("--production-forward","--forward",dest="production_forward",type=Path)
    parser.add_argument("--fixture-forward",type=Path)
    parser.add_argument("--host",type=Path)
    parser.add_argument("--importer",type=Path)
    parser.add_argument("--case",type=Path)
    parser.add_argument("--pe-proof",type=Path)
    parser.add_argument("--child-pid",type=int)
    parser.add_argument("--profile",choices=("production","fixture"))
    parser.add_argument("--fault-stage",type=int)
    parser.add_argument("--fault-index",type=int)
    parser.add_argument("--output",type=Path,required=True)
    args = parser.parse_args()
    try:
        if args.inspect:
            require(not args.case and all((args.production_forward,args.fixture_forward,args.host,args.importer)),
                    "Inspect requires exact production/test/host/importer absolute image paths")
            result = inspect_images(args.production_forward,args.fixture_forward,args.host,args.importer)
        else:
            require(args.case and args.pe_proof and args.child_pid is not None and args.profile is not None
                    and args.fault_stage is not None and args.fault_index is not None,
                    "Case verification requires independent parent child PID/profile/fault stage/index")
            result = verify_case(args)
        args.output.write_text(json.dumps(result,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
        print(json.dumps(dict(passed=True,kind=result["kind"],checks=result.get("checks",0),output=str(args.output)),ensure_ascii=False))
        return 0
    except Exception as error:
        result = dict(kind="owned-winhttp-forward-verifier-failure",passed=False,error=f"{type(error).__name__}: {error}")
        args.output.write_text(json.dumps(result,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
        print(json.dumps(result),file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
