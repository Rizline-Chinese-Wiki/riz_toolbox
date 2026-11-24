"""Strict, hash-pinned profile adapter for the existing EarlyFixed checks.

This program never loads a DLL, starts a child, or changes either historical
verifier. Only enumerated AST identity/version literals are adapted. All fault,
workload, exception, physical-pairing and actual-memory assertions are reused.
"""
import argparse
import ast
import hashlib
import json
import os
import struct
import sys
import types
from pathlib import Path


HERE = Path(__file__).resolve().parent
EARLY_PATH = HERE.parent / "foundation-early-fixed-20261025/verify-early-fixed.py"
FIXED_PATH = HERE.parent / "foundation-fixed-publisher-20261025/verify-fixed-publisher.py"
PINNED_EARLY_SHA256 = "056b494994c86558444d873da2a841c99fd31cd82297884fa41b288f20730d64"
PINNED_FIXED_SHA256 = "2144013f0dfbd114dec164b43c2fac28a083f2ca3bd11e120a5deab2775b27d7"
PROFILE = "engine-kernel"
WIRE_VERSION = 3
PUBLISHER_VERSION = 2
MAGIC = 0x4b465245
HOST = "RizKernelOwnedHost.exe"
KERNEL = "RizToolbox.EngineNodeKernel.dll"
PUBLISHER = "RizKernelFixedPublisher.dll"
PROXY = "RizKernelOwnedProxy.dll"
RECORDER = "RizKernelOwnedRecorder.dll"
EXPORTS = sorted("RizEngineNodeContext_"+name for name in (
    "GetAbiVersion", "GetLimit", "GetFrameSize", "GetCompletionSize", "GetEntry", "Configure", "Attach", "GetInfo"))


def require(value, message):
    if not value:
        raise ValueError(message)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def expression(source):
    return ast.parse(source, mode="eval").body


STRING_PROFILE = {
    "RizFixedPublisher.dll": PUBLISHER,
    "rizfixedpublisher.dll": PUBLISHER.lower(),
    "RizToolbox.JudgeContext.dll": KERNEL,
    "riztoolbox.judgecontext.dll": KERNEL.lower(),
    "RizOwnedProxy.dll": PROXY,
    "rizownedproxy.dll": PROXY.lower(),
    "RizOwnedRecorder.dll": RECORDER,
    "rizownedrecorder.dll": RECORDER.lower(),
    "RizOwnedHost.exe": HOST,
    "owned-fixed-publisher-pe-proof": "owned-kernel-fixed-publisher-pe-proof",
    "owned-early-fixed-pe-import-check": "owned-early-kernel-pe-import-check",
    "owned-early-fixed-parent-initialization": "owned-early-kernel-parent-initialization",
    "owned-early-fixed-shared-log-verification": "owned-early-kernel-shared-log-verification",
    "owned-early-fixed-verifier-failure": "owned-early-kernel-verifier-failure",
}


class ExactProfile(ast.NodeTransformer):
    """Adapt only fixed identities and exact named wire-version expressions."""
    def __init__(self, family):
        self.family = family
        self.function = None
        self.changes = []
        if family == "fixed":
            pairs = [
                ("decode_receipt", 'info["version"] == 1', 'info["version"] == 2'),
                ("decode_receipt", 'row["version"] == 1', 'row["version"] == 2'),
            ]
        else:
            pairs = [
                ("initialize", 'struct.pack_into("<4I", data, 0, MAGIC, 2, MAP_BYTES, EVENT_CAPACITY)',
                 'struct.pack_into("<4I", data, 0, MAGIC, 3, MAP_BYTES, EVENT_CAPACITY)'),
                ("initialize", 'struct.pack_into("<8I", data, 152, 2, 176, mode, fault_stage, fault_index, 0, EVENT_CAPACITY, 2)',
                 'struct.pack_into("<8I", data, 152, 3, 176, mode, fault_stage, fault_index, 0, EVENT_CAPACITY, 2)'),
                ("decode_mapping", '(MAGIC, 2, MAP_BYTES, EVENT_CAPACITY)', '(MAGIC, 3, MAP_BYTES, EVENT_CAPACITY)'),
                ("verify_mapping", 'header["config"]["version"] == 2', 'header["config"]["version"] == 3'),
                ("verify_mapping", 'meta["version"] == 2', 'meta["version"] == 3'),
            ]
        self.rules = {(function, ast.dump(expression(before), include_attributes=False)):
                      (before, after) for function,before,after in pairs}
        self.hits = {key:0 for key in self.rules}

    def visit_FunctionDef(self, node):
        previous = self.function
        self.function = node.name
        result = self.generic_visit(node)
        self.function = previous
        return result

    def visit_Constant(self, node):
        if isinstance(node.value, str) and node.value in STRING_PROFILE:
            updated = STRING_PROFILE[node.value]
            self.changes.append(dict(type="exact-string",function=self.function,line=node.lineno,before=node.value,after=updated))
            return ast.copy_location(ast.Constant(value=updated),node)
        if self.family == "early" and self.function is None and type(node.value) is int and node.value == 0x58465245:
            self.changes.append(dict(type="shared-magic",function=None,line=node.lineno,before=node.value,after=MAGIC))
            return ast.copy_location(ast.Constant(value=MAGIC),node)
        return node

    def generic_visit(self, node):
        node = super().generic_visit(node)
        if isinstance(node, ast.expr):
            key = (self.function,ast.dump(node,include_attributes=False))
            if key in self.rules:
                before, after = self.rules[key]
                self.hits[key] += 1
                self.changes.append(dict(type="exact-version-expression",function=self.function,line=node.lineno,before=before,after=after))
                return ast.copy_location(expression(after),node)
        return node

    def complete(self):
        require(all(count == 1 for count in self.hits.values()), "Pinned profile expression was missing or matched more than once")
        if self.family == "early":
            require(sum(row["type"] == "shared-magic" for row in self.changes) == 1, "Exactly one shared magic must be adapted")


def load_profile(path, expected_sha, family, fixed=None):
    data = path.read_bytes()
    require(sha(data) == expected_sha, f"Unknown transitive verifier bytes: {path.name}")
    tree = ast.parse(data.decode("utf-8-sig"),filename=str(path))
    protected = {}
    if family == "early":
        protected = {node.name:ast.dump(node,include_attributes=False) for node in tree.body
                     if isinstance(node,ast.FunctionDef) and node.name in ("verify_workload","exception_records")}
        # Substitute this already hash-verified fixed module for the historical
        # four-statement import bootstrap. No assertion/function is removed.
        kept,removed = [],[]
        for node in tree.body:
            assignment = isinstance(node,ast.Assign) and len(node.targets) == 1 and isinstance(node.targets[0],ast.Name)
            if assignment and node.targets[0].id in ("FIXED_VERIFIER_PATH","_fixed_spec","fixed"):
                removed.append(node)
            elif isinstance(node,ast.Expr) and isinstance(node.value,ast.Call) and ast.unparse(node.value) == "_fixed_spec.loader.exec_module(fixed)":
                removed.append(node)
            else:
                kept.append(node)
        require(len(removed) == 4, "Unexpected historical fixed-module import bootstrap")
        tree.body = kept
    adapter = ExactProfile(family)
    tree = adapter.visit(tree)
    adapter.complete()
    if family == "early":
        after = {node.name:ast.dump(node,include_attributes=False) for node in tree.body
                 if isinstance(node,ast.FunctionDef) and node.name in protected}
        require(protected == after and len(after) == 2, "Fault/workload/exception implementation must remain structurally identical")
    ast.fix_missing_locations(tree)
    module = types.ModuleType("hash_pinned_"+family+"_kernel_profile")
    module.__file__ = str(path)
    if fixed is not None:
        module.fixed = fixed
    exec(compile(tree,str(path),"exec"),module.__dict__)
    return module,dict(sourcePath=str(path),sourceSha256=expected_sha,family=family,
                       changes=adapter.changes,adaptedAstSha256=sha(ast.dump(tree,include_attributes=False).encode()),
                       historicalFileModified=False,protectedWorkloadAndExceptionsUnchanged=family == "early")


def load_all():
    fixed, fixed_adapter = load_profile(FIXED_PATH,PINNED_FIXED_SHA256,"fixed")
    early, early_adapter = load_profile(EARLY_PATH,PINNED_EARLY_SHA256,"early",fixed)
    return early,fixed,[fixed_adapter,early_adapter]


def kernel_pe(image):
    data = Path(image["path"]).read_bytes()
    require(sha(data) == image["sha256"] and image["name"] == KERNEL, "Kernel disk identity changed or wrong filename")
    nt = struct.unpack_from("<I",data,0x3c)[0]
    opt = nt+24
    count = struct.unpack_from("<I",data,opt+108)[0]
    require(15 <= count <= 16 and struct.unpack_from("<H",data,nt+20)[0] >= 112+8*count, "Kernel directories are truncated")
    directories = [struct.unpack_from("<II",data,opt+112+8*index) for index in range(count)]
    require(image["machine"] == 0x8664 and image["characteristics"]&0x2000 and image["entryRva"] == 0,
            "Kernel must be an AMD64 DLL with no automatic entry")
    require(all(directories[index] == (0,0) for index in (9,10,13,14)) and not image["tls"] and not image["delayImports"],
            "Kernel requires absent TLS/load-config/delay/CLR initialization directories")
    actual = sorted((dependency["module"].lower(),symbol.get("name"),symbol.get("ordinal"))
                    for dependency in image["imports"] for symbol in dependency["symbols"])
    require(actual == [("kernel32.dll","RaiseFailFastException",None),("ntdll.dll","__C_specific_handler",None)],
            "Kernel has an unexpected CRT/Dobby/heap/loader or other direct import")
    exports = image["exports"]
    require(len(exports) == 8 and sorted(row["name"] for row in exports) == EXPORTS
            and all(row["rva"] and not row["forwarder"] for row in exports), "Kernel exports must be the exact eight nonforwarded APIs")
    export_rva,_ = directories[0]
    section = next(row for row in image["sections"] if row["rva"] <= export_rva
                   and export_rva+40 <= row["rva"]+row["rawSize"])
    offset = section["rawOffset"]+export_rva-section["rva"]
    function_count,name_count = struct.unpack_from("<II",data,offset+20)
    require(function_count == name_count == 8 and len({row["ordinal"] for row in exports}) == 8,
            "Kernel must not hide extra ordinal-only exports")
    return dict(kind="early-kernel-exact-pe-contract",passed=True,imageSha256=image["sha256"],exports=EXPORTS,
                directImports=actual,noInitializationRequired=True,noEntryTlsDelayLoadConfigOrClr=True,
                allTransitiveOsDependenciesSafeVerified=False)


def profile_metadata(adapters):
    return dict(profile=PROFILE,wireVersion=WIRE_VERSION,publisherVersion=PUBLISHER_VERSION,
                scalarRecordVersion=1,transportAbiVersion=2,
                transitiveVerifiers=adapters,historicalVerifiersModified=False)


def require_profile(proof):
    metadata = proof.get("profileMetadata",{})
    require(metadata.get("profile") == PROFILE and metadata.get("wireVersion") == 3 and metadata.get("publisherVersion") == 2,
            "Proof belongs to a different helper/publisher wire profile")
    require([row.get("sourceSha256") for row in metadata.get("transitiveVerifiers",[])] == [PINNED_FIXED_SHA256,PINNED_EARLY_SHA256],
            "Proof was not produced with the exact pinned transitive verifiers")


def disk_file_identity(image):
    """Identify the very handle used to re-read the parent-held PE bytes."""
    require(os.name == "nt", "Windows handle file identities require Windows")
    import ctypes
    import msvcrt
    class FileTime(ctypes.Structure):
        _fields_ = [("low",ctypes.c_uint32),("high",ctypes.c_uint32)]
    class FileInformation(ctypes.Structure):
        _fields_ = [("attributes",ctypes.c_uint32),("creation",FileTime),
                    ("access",FileTime),("write",FileTime),("volume",ctypes.c_uint32),
                    ("sizeHigh",ctypes.c_uint32),("sizeLow",ctypes.c_uint32),
                    ("links",ctypes.c_uint32),("indexHigh",ctypes.c_uint32),("indexLow",ctypes.c_uint32)]
    get_information = ctypes.WinDLL("kernel32",use_last_error=True).GetFileInformationByHandle
    get_information.argtypes = [ctypes.c_void_p,ctypes.POINTER(FileInformation)]
    get_information.restype = ctypes.c_int
    def identify(handle):
        info = FileInformation()
        require(get_information(handle,ctypes.byref(info)) != 0, "Parent cannot identify its held image handle")
        return (info.volume,(info.indexHigh<<32)|info.indexLow,(info.sizeHigh<<32)|info.sizeLow)
    with Path(image["path"]).open("rb") as stream:
        handle = ctypes.c_void_p(msvcrt.get_osfhandle(stream.fileno()))
        before = identify(handle)
        data = stream.read()
        after = identify(handle)
    require(before == after and before[2] == image["bytes"] == len(data) and sha(data) == image["sha256"],
            "Parent image handle identity or bytes changed during PE identity read")
    return dict(method="GetFileInformationByHandle",volumeSerial=before[0],fileIndex=before[1],
                bytes=before[2],imageSha256=image["sha256"])


def _unique_json_object(pairs):
    result = {}
    for key,value in pairs:
        require(key not in result, "Duplicate debugger JSON object key")
        result[key] = value
    return result


def verify_kernel_debugger(path,events,images,header,check):
    """Check every new-profile observation without projecting away raw rows."""
    rows = [json.loads(line,object_pairs_hook=_unique_json_object)
            for line in path.read_text(encoding="utf-8-sig").splitlines() if line.strip()]
    debug = [event for event in events if event["type"] >= 1000]
    check(len(rows) == len(debug) > 0, "Every debugger JSON row has exactly one shared observation")
    check(all(row["sequence"] < other["sequence"] for row,other in zip(rows,rows[1:])),
          "Debugger JSON rows preserve strict global publication order")
    names = {1:PUBLISHER.lower(),2:KERNEL.lower(),3:RECORDER.lower(),4:PROXY.lower(),5:"rizfixedownedtargets.dll"}
    selected_bases = {1:header["publisherModule"],2:header["helperModule"],3:header["recorderModule"],
                      4:header["proxyModule"],5:header["importerModule"]}
    observer,rf,tf = 0x80000000,1<<16,1<<8
    instances,pending,generations = {},{},dict.fromkeys(names,0)
    pairs,mismatches,retired = [],[],[]
    starts = []
    def number(value,bits=64):
        return type(value) is int and 0 <= value < 1<<bits
    def exact(event,**expected):
        check(all(event[key] == value for key,value in expected.items()),
              "Type-specific debugger payload agrees exactly with shared scalar fields")
    def context(value):
        check(set(value) == {"rip","rsp","eflags","dr","dr6","dr7"}
              and all(number(value[key]) for key in ("rip","rsp","dr6","dr7"))
              and number(value["eflags"],32) and isinstance(value["dr"],list)
              and len(value["dr"]) == 4 and all(number(item) for item in value["dr"]),
              "Actual before/readback hardware contexts have the complete fixed scalar shape")
    def owned_context(value,tid):
        addresses = [0]*4
        for (thread,slot),site in pending.items():
            if thread == tid:
                addresses[slot] = site["returnPc"]
        control = sum(1<<(slot*2) for slot,address in enumerate(addresses) if address)
        check(value["dr"] == addresses and value["dr7"]&~0x400 == control and not value["eflags"]&tf,
              "All four actual hardware slots and local execute controls belong to exact pending returns")
    def reset_status(value):
        # SetThreadContext/GetThreadContext can normalize reserved/reset bits.
        # Only the owned B0..3 and the BD/BS/BT cause bits are asserted here.
        check(value["dr6"]&0xe00f == 0,
              "Actual readback clears owned B0..3 and BD/BS/BT debug causes")
    def hit_status(value,tid,slot,pc):
        owned_hits = sum(1<<candidate for (thread,candidate),site in pending.items()
                         if thread == tid and site["returnPc"] == pc)
        check(value["dr6"]&(1<<slot) != 0 and value["dr6"]&0xf&~owned_hits == 0
              and value["dr6"]&0xe000 == 0,
              "Actual hardware hit has its owned slot and no unrelated debug cause")
    def identity(row,event):
        role = row["moduleId"]
        image = images[names[role]]
        file = image["fileIdentity"]
        check(file["method"] == "GetFileInformationByHandle" and file["imageSha256"] == image["sha256"]
              and file["bytes"] == image["bytes"] and number(file["volumeSerial"],32) and number(file["fileIndex"]),
              "Parent PE proof contains independently measured disk-handle identity")
        check(row["fileIdentityMatched"] is True and row["fileVolumeSerial"] == row["expectedFileVolumeSerial"] == file["volumeSerial"]
              and row["fileIndex"] == row["expectedFileIndex"] == file["fileIndex"]
              and row["moduleBytes"] == image["imageSize"] and row["returnAddress"] == row["returnRax"] == 0,
              "Observed actual and debugger-expected file identities both equal parent-held disk identity")
        check(all(number(row[key]) for key in ("instanceId","loadSequence","fileIndex","moduleBytes"))
              and number(row["fileVolumeSerial"],32) and number(row["generation"],32),
              "Mapping identity fields are bounded unsigned scalar values")
        exact(event,index=row["generation"],entry=row["fileIndex"],epoch=row["instanceId"],
              value0=row["fileVolumeSerial"],value1=row["loadSequence"])
        return (role,row["base"],row["moduleBytes"],row["generation"],row["loadSequence"],
                row["fileVolumeSerial"],row["fileIndex"])
    for row,event in zip(rows,debug):
        event_type,role = row["type"],row["moduleId"]
        check(number(event_type,32) and event_type in range(1001,1009) and number(role,32) and role in names
              and all(number(row[key]) for key in ("sequence","base","target","returnAddress","returnRax"))
              and number(row["pid"],32) and number(row["tid"],32) and row["tid"] > 0,
              "Debugger row uses a known new-profile event and bounded child scalars")
        check(row["sequence"] == event["sequence"] and event_type == event["type"] and row["pid"] == event["pid"] == header["childPid"]
              and row["tid"] == event["tid"] and role == event["kind"] and row["base"] == event["module"] != 0
              and row["module"] == names[role], "Every raw debugger row matches its exact shared child/thread/role/base/sequence")
        check(row["debugInstrumented"] is True and row["noInitializationRequired"] is (role in (1,2))
              and row["processAttach"] is (event_type in (1002,1003,1004,1005)),
              "Actual initialization, loader observations and diagnostics retain distinct explicit meanings")
        if event_type in (1001,1006,1007):
            key = identity(row,event)
            instance_id = row["instanceId"]
            if event_type == 1001:
                generations[role] += 1
                check(len(instances) < 32 and instance_id == len(instances)+1 and instance_id not in instances
                      and row["generation"] == generations[role] <= 8 and row["loadSequence"] == row["sequence"]
                      and row["live"] is True and row["target"] == row["base"],
                      "Actual load creates one bounded globally unique instance and sequential role generation")
                check(not any(item["live"] and item["base"] == row["base"] for item in instances.values())
                      and (role in (1,2) or generations[role] == 1),
                      "A live base is never duplicated and initialized roles have one observed lifetime")
                exact(event,phase=0,result=1,flags=observer,target=row["base"],original=row["moduleBytes"],caller=0)
                instances[instance_id] = dict(key=key,role=role,base=row["base"],instanceId=instance_id,
                    generation=row["generation"],loadSequence=row["sequence"],unloadSequence=None,live=True,
                    fileVolumeSerial=row["fileVolumeSerial"],fileIndex=row["fileIndex"])
            else:
                check(instance_id in instances and instances[instance_id]["key"] == key and role in (1,2)
                      and row["live"] is False and row["target"] == row["base"],
                      "Unload/retired notification refers to the precise observed no-initializer mapping instance")
                item = instances[instance_id]
                if event_type == 1006:
                    check(item["live"] and item["loadSequence"] < row["sequence"] < header["proxyEnterSequence"],
                          "Nonselected no-initializer mapping actually unloads before producer entry")
                    exact(event,phase=0,result=0,flags=observer,target=row["base"],original=row["moduleBytes"],caller=0)
                    item["live"],item["unloadSequence"] = False,row["sequence"]
                else:
                    check(not item["live"] and item["unloadSequence"] < row["sequence"]
                          and not any(other["live"] and other["base"] == row["base"] for other in instances.values())
                          and sum(other["base"] == row["base"] for other in instances.values()) == 1
                          and row["retiredUnloadNotification"] is True,
                          "Repeated unload notification has one already-closed unambiguous instance and cannot close another lifetime")
                    query = row["query"]
                    check(set(query) == {"base","allocationBase","regionSize","state","type","protect"}
                          and all(number(query[name]) for name in ("base","allocationBase","regionSize"))
                          and all(number(query[name],32) for name in ("state","type","protect"))
                          and query["regionSize"] > 0 and query["base"] <= row["base"] < query["base"]+query["regionSize"] <= 1<<64
                          and query["state"] in (0x1000,0x2000,0x10000) and query["protect"] < observer
                          and not (query["state"] == 0x1000 and query["type"] == 0x1000000 and query["allocationBase"] == row["base"]),
                          "Actual retired-base VM query covers that address and no longer describes its original committed image")
                    exact(event,phase=query["type"],result=query["state"],flags=observer|query["protect"],
                          target=query["base"],original=query["regionSize"],caller=query["allocationBase"])
                    retired.append(dict(sequence=row["sequence"],instanceId=instance_id,loadSequence=item["loadSequence"],query=query))
            continue
        check(role in (3,4,5) and number(row["instanceId"]) and row["instanceId"] in instances,
              "Only real initialized fixture roles own entry/return/hardware diagnostic records")
        item = instances[row["instanceId"]]
        check(item["live"] and item["role"] == role and item["base"] == row["base"]
              and item["loadSequence"] < row["sequence"], "Actual initialization/diagnostic belongs to the same live observed image instance")
        before,after = row["hardwareContextBefore"],row["hardwareContextAfter"]
        context(before); context(after)
        slot,tid = row["debugSlot"],row["tid"]
        check(number(slot,32) and slot < 4 and number(row["expectedRsp"]) and number(row["actualRsp"])
              and number(row["dr6"]) and number(row["dr7"])
              and row["actualRsp"] == before["rsp"] and row["dr6"] == before["dr6"] and row["dr7"] == before["dr7"],
              "Top-level hardware diagnostics exactly mirror actual pre-change context")
        owned_context(before,tid)
        pending_key = (tid,slot)
        exact(event,flags=observer,phase=0,index=slot)
        if event_type in (1002,1004):
            free = next((candidate for candidate in range(4) if (tid,candidate) not in pending),None)
            check(len(pending) < 64 and pending_key not in pending and slot == free
                  and row["returnAddress"] > 0 and row["returnRax"] == 0 and before["rip"] == row["target"]+1
                  and row["expectedRsp"] == before["rsp"]+8 < 1<<64 and after["rip"] == row["target"]
                  and after["rsp"] == before["rsp"] and row["resumeFlagRequested"] is False,
                  "Actual entry arms the first free owned slot for the stack return at entry RSP plus eight")
            exact(event,result=1,target=row["target"],original=row["returnAddress"],entry=row["instanceId"],
                  epoch=row["expectedRsp"],value0=row["actualRsp"],value1=0,caller=row["returnAddress"])
            site = dict(instanceId=row["instanceId"],role=role,tid=tid,slot=slot,entryType=event_type,
                        entryPc=row["target"],returnPc=row["returnAddress"],expectedRsp=row["expectedRsp"],
                        entrySequence=row["sequence"],mismatchSequences=[])
            pending[pending_key] = site
            starts.append(site)
        else:
            check(pending_key in pending, "Return/stack diagnostic has an exact pending thread and hardware slot")
            site = pending[pending_key]
            check(site["instanceId"] == row["instanceId"] and site["expectedRsp"] == row["expectedRsp"]
                  and before["rip"] == site["returnPc"] and after["rip"] == before["rip"] and after["rsp"] == before["rsp"],
                  "Hardware observation keeps exact pending image/return-PC/stack identity and actual readback PC/RSP")
            hit_status(before,tid,slot,site["returnPc"])
            check(row["resumeFlagRequested"] is True and after["eflags"]&rf != 0,
                  "Real hardware hit resumes with RF observed in SetThreadContext readback")
            if event_type == 1008:
                check(row["returnStackMismatch"] is True and row["resumeFlagSet"] is True
                      and row["target"] == row["returnAddress"] == site["returnPc"]
                      and row["actualRsp"] != site["expectedRsp"]
                      and not any(thread == tid and candidate["returnPc"] == site["returnPc"]
                                  and candidate["expectedRsp"] == row["actualRsp"] for (thread,_),candidate in pending.items()),
                      "Wrong-RSP pass is a real different stack at the pending PC and cannot be interpreted as any exact return")
                exact(event,result=0,target=row["target"],original=row["expectedRsp"],entry=row["actualRsp"],
                      epoch=row["instanceId"],value0=row["dr6"],value1=row["dr7"],caller=row["target"])
                site["mismatchSequences"].append(row["sequence"])
                mismatches.append(dict(sequence=row["sequence"],entrySequence=site["entrySequence"],tid=tid,slot=slot,
                                       instanceId=row["instanceId"],returnPc=site["returnPc"],
                                       expectedRsp=site["expectedRsp"],actualRsp=row["actualRsp"]))
            else:
                check(event_type == (1003 if site["entryType"] == 1002 else 1005)
                      and row["target"] == site["entryPc"] and row["returnAddress"] == site["returnPc"]
                      and row["actualRsp"] == site["expectedRsp"]
                      and (event_type == 1005 or row["returnRax"]&0xffffffff == 1),
                      "Actual initialization return matches entry type/PC/thread/exact RSP and preserves DLL success RAX")
                exact(event,result=1,target=row["target"],original=row["returnAddress"],entry=row["instanceId"],
                      epoch=row["expectedRsp"],value0=row["actualRsp"],value1=row["returnRax"],caller=row["returnAddress"])
                del pending[pending_key]
                pairs.append(dict(site,returnSequence=row["sequence"],actualReturnRsp=row["actualRsp"],
                                  returnRax=row["returnRax"],slotCleared=True))
        owned_context(after,tid); reset_status(after)
        check((after["eflags"] & ~(rf|2)) == (before["eflags"] & ~(rf|2))
              and (event_type not in (1002,1004) or bool(after["eflags"]&rf) == bool(before["eflags"]&rf)),
              "Actual readback preserves EFLAGS except requested RF and the OS-normalized fixed bit")
        armed = after["dr"][slot] == row["returnAddress"] and after["dr7"]&(3<<(slot*2)) == 1<<(slot*2) \
                and (after["dr7"]>>(16+slot*4))&0xf == 0
        cleared = after["dr"][slot] == 0 and after["dr7"]&(3<<(slot*2)) == 0
        check(row["slotArmed"] is armed and row["slotCleared"] is cleared
              and row["resumeFlagObserved"] is bool(after["eflags"]&rf)
              and (cleared if event_type in (1003,1005) else armed),
              "Actual readback independently proves the retained or cleared slot and exact RF metadata")
    check(not pending, "Every owned hardware return is eventually completed at exact RSP with its slot cleared")
    selected = []
    for role,name in names.items():
        group = [item for item in instances.values() if item["role"] == role]
        live = [item for item in group if item["live"]]
        check(len(live) == 1 and live[0]["base"] == selected_bases[role] != 0,
              "Final single live mapping for each role equals actual header-selected module")
        current = live[0]
        selected.append(dict(role=role,module=name,instanceId=current["instanceId"],base=current["base"],
                             loadSequence=current["loadSequence"],fileVolumeSerial=current["fileVolumeSerial"],fileIndex=current["fileIndex"]))
        image = images[name]
        if role in (1,2):
            check(image["entryRva"] == 0 and image["tls"] is None and current["loadSequence"] < header["proxyEnterSequence"]
                  and all(item["live"] or item["unloadSequence"] < header["proxyEnterSequence"] for item in group),
                  "No-entry/TLS dependency is truly mapped before producer and every nonselected mapping is already actually retired")
        for entry_type,rvas in ((1002,[image["entryRva"]] if image["entryRva"] else []),
                                (1004,image["tls"]["callbacks"] if image["tls"] else [])):
            group_starts = [site for site in starts if site["role"] == role and site["entryType"] == entry_type]
            check(sorted(site["entryPc"]-current["base"] for site in group_starts) == sorted(rvas),
                  "Actual PE entry/TLS RVA multiset has exactly one completed observation per real callback")
    recorder = [pair for pair in pairs if pair["role"] == 3]
    proxy = [pair for pair in pairs if pair["role"] == 4 and pair["entryType"] == 1002]
    target_tls = [pair for pair in pairs if pair["role"] == 5 and pair["entryType"] == 1004]
    check(recorder and max(pair["returnSequence"] for pair in recorder) < header["proxyEnterSequence"]
          and len(proxy) == 1 and target_tls and proxy[0]["entrySequence"] < header["proxyEnterSequence"]
          < header["readySequence"] < proxy[0]["returnSequence"] < min(pair["entrySequence"] for pair in target_tls),
          "Real Recorder returns before producer; Proxy publishes Ready and actually returns before Targets TLS entry")
    check(all(next(pair for pair in pairs if pair["entrySequence"] == mismatch["entrySequence"])["returnSequence"] > mismatch["sequence"]
              for mismatch in mismatches), "Every wrong-RSP diagnostic retains responsibility until a later real exact-stack return")
    return dict(instrumented=True,observedRows=len(debug),rawJsonlSha256=sha(path.read_bytes()),allDebugRowsMatchedShared=True,
                actualLoadCount=len(instances),actualUnloadCount=sum(not item["live"] for item in instances.values()),
                observedModuleInstances=len(instances),observedModuleLifetimes=[{key:value for key,value in item.items() if key!="key"} for item in instances.values()],
                selectedMappings=selected,finalSelectedMappingsUniqueVerified=True,allNonselectedMappingsRetiredBeforeProxyVerified=True,
                parentDiskFileIdentityVerified=True,entryReturnPairs=len(pairs),initializationPairs=pairs,hardwareReturnStackAndSlotCleanupVerified=True,
                returnStackMismatches=len(mismatches),returnStackMismatchObservations=mismatches,
                retiredUnloadNotifications=len(retired),retiredUnloadNotificationsObserved=bool(retired),retiredUnloadObservations=retired,
                noInitializationRequired=True,actualKernelLoadBeforeProxyVerified=True,actualPublisherLoadBeforeProxyVerified=True,
                zeroSyntheticKernelInitializationRowsVerified=True,publisherHasNoInitializationEntryVerified=True,
                kernelLoadSequence=next(item["loadSequence"] for item in selected if item["role"]==2),
                publisherLoadSequence=next(item["loadSequence"] for item in selected if item["role"]==1),
                completeOsLoaderLifetimeClosureVerified=False,realSystemWinHttpVerified=False,gameFirstInstallVerified=False,
                allTransitiveLoaderDependenciesSafeVerified=False)


def debugger_adapter(early):
    early.EVENT_NAMES.update({1006:"DebugDllUnload",1007:"DebugRetiredUnloadNotification",1008:"DebugReturnStackMismatch"})
    early.verify_debugger = verify_kernel_debugger


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inspect-images",type=Path)
    parser.add_argument("--host-name",default=HOST)
    parser.add_argument("--initialize",action="store_true")
    parser.add_argument("--mode",type=int,default=0)
    parser.add_argument("--fault-stage",type=int,default=0)
    parser.add_argument("--fault-index",type=int,default=0)
    parser.add_argument("--pe-proof",type=Path)
    parser.add_argument("--mapping",type=Path)
    parser.add_argument("--initial",type=Path)
    parser.add_argument("--child-pid",type=int)
    parser.add_argument("--child-exit",type=int)
    parser.add_argument("--debug-events",type=Path)
    parser.add_argument("--baseline",type=Path)
    parser.add_argument("--output",type=Path,required=True)
    args = parser.parse_args()
    try:
        early,fixed,adapters = load_all()
        debugger_adapter(early)
        if args.inspect_images:
            require(args.host_name == HOST, "Kernel profile requires the exact owned Kernel host identity")
            result = early.inspect_images(args.inspect_images.resolve(),args.host_name)
            for image in result["images"]:
                image["fileIdentity"] = disk_file_identity(image)
            image = next(row for row in result["images"] if row["name"] == KERNEL)
            result["kernelPeContract"] = kernel_pe(image)
        elif args.initialize:
            proof = read_json(args.pe_proof)
            require_profile(proof)
            require(proof["kind"] == "owned-early-kernel-pe-import-check" and proof["kernelPeContract"]["passed"], "Missing strict Kernel PE proof")
            result = early.initialize(args.pe_proof,args.mode,args.fault_stage,args.fault_index,args.output)
            result["profileMetadata"] = profile_metadata(adapters)
            print(json.dumps(result,ensure_ascii=False))
            return 0
        else:
            require(args.mapping and args.initial and args.pe_proof and args.child_pid is not None and args.child_exit is not None,
                    "Missing exact shared mapping verification parameters")
            proof = read_json(args.pe_proof)
            require_profile(proof)
            require(proof["kind"] == "owned-early-kernel-pe-import-check" and proof["kernelPeContract"]["passed"], "Missing strict Kernel profile proof")
            if args.baseline:
                require_profile(read_json(args.baseline))
            # This call retains the entire pinned fault/exception/workload/
            # actual-memory verifier. Adapted decoder rejects wire2/receipt1.
            result = early.verify_mapping(args)
            for kind,row in enumerate(result["tuples"]):
                if any(value for value in row.values() if not isinstance(value,str)) or any(int(row[name],16) for name in ("originalBytes","publishedBytes","moduleSha256")):
                    require(row["version"] == 3 and row["byteSize"] == 216 and row["kind"] == kind and row["slot"] == 28,
                            "Initialized tuple belongs to a different wire/profile or kind/slot")
            result["kernelPeContract"] = proof["kernelPeContract"]
        result["profileMetadata"] = profile_metadata(adapters)
        args.output.write_text(json.dumps(result,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
        print(json.dumps(dict(passed=True,profile=PROFILE,output=str(args.output),checks=result.get("checks",0)),ensure_ascii=False))
        return 0
    except Exception as error:
        value = dict(kind="owned-early-kernel-verifier-failure",passed=False,profile=PROFILE,error=f"{type(error).__name__}: {error}")
        args.output.write_text(json.dumps(value,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
        print(json.dumps(value,ensure_ascii=False),file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
