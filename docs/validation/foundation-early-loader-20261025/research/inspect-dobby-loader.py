"""Bounded disk-only audit of the pinned Dobby Prepare/Commit dependencies.

No DLL mapping, native execution, build, game, process or UI operation. The
direct graph is conservative static reachability, not feasible path proof.
RF CHAININFO is folded while each exact disjoint range is decoded separately.
"""
from pathlib import Path
import bisect
import hashlib
import json
import struct

import capstone
import pefile

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[3]
PATH = REPO / ".tools/bepinex-789/BepInEx/core/dobby.dll"
PIN = "8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded"
sha = lambda data: hashlib.sha256(data).hexdigest()
data = PATH.read_bytes()
assert sha(data) == PIN
pe = pefile.PE(data=data)
base = pe.OPTIONAL_HEADER.ImageBase
md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
md.detail = True
directory = pe.OPTIONAL_HEADER.DATA_DIRECTORY[3]
pdata = pe.get_data(directory.VirtualAddress, directory.Size)
rows = [struct.unpack_from("<III", pdata, i) for i in range(0, len(pdata) - 11, 12)]
starts = [r[0] for r in rows]
assert starts == sorted(starts)

def primary(row):
    seen = set()
    while row not in seen and len(seen) < 16:
        seen.add(row)
        header = pe.get_data(row[2], 4)
        if not (header[0] >> 3) & 4:
            return row
        row = struct.unpack("<III", pe.get_data(row[2] + 4 + ((header[2] + 1) & ~1) * 2, 12))
    raise ValueError("bad/cyclic CHAININFO")

groups = {}
for row in rows:
    groups.setdefault(primary(row), []).append(row)

def root_at(rva):
    i = bisect.bisect_right(starts, rva) - 1
    return primary(rows[i]) if i >= 0 and rows[i][0] <= rva < rows[i][1] else None

def executable(rva):
    return any(s.VirtualAddress <= rva < s.VirtualAddress + max(s.Misc_VirtualSize, s.SizeOfRawData)
               and s.Characteristics & 0x20000000 for s in pe.sections)

imports = {}
for descriptor in getattr(pe, "DIRECTORY_ENTRY_IMPORT", []):
    for symbol in descriptor.imports:
        imports[symbol.address - base] = dict(dll=descriptor.dll.decode(),
            name=symbol.name.decode() if symbol.name else None, ordinal=symbol.ordinal,
            iatRva=hex(symbol.address-base))

def classify(ins):
    rec = dict(rva=hex(ins.address), bytes=ins.bytes.hex(), op=ins.mnemonic, args=ins.op_str)
    if ins.mnemonic == "call" or ins.mnemonic.startswith("j"):
        operand = ins.operands[0]
        if operand.type == capstone.x86.X86_OP_IMM:
            rec["directTargetRva"] = hex(operand.imm)
        elif operand.type == capstone.x86.X86_OP_MEM:
            mem = operand.mem
            if mem.base == capstone.x86.X86_REG_RIP:
                slot = ins.address+ins.size+mem.disp
                rec["pointerSlotRva"] = hex(slot)
                if slot in imports:
                    rec["import"] = imports[slot]
            else:
                rec["unresolvedIndirect"] = True
        else:
            rec["unresolvedIndirect"] = True
    for operand in ins.operands:
        if operand.type == capstone.x86.X86_OP_MEM and operand.mem.base == capstone.x86.X86_REG_RIP:
            rec.setdefault("ripDataRvas", []).append(hex(ins.address+ins.size+operand.mem.disp))
    return rec

cache = {}
def region(request):
    root = root_at(request)
    key = root[0] if root else request
    if key in cache:
        return cache[key]
    spans = sorted(groups[root]) if root else [(request, request+64, 0)]
    item = dict(keyRva=hex(key), requestedRva=hex(request), noRuntimeFunction=root is None,
                runtimeFunctions=[], spans=[], instructions=[])
    for start, end, unwind in spans:
        assert 0 < end-start <= 0x20000 and executable(start)
        raw = pe.get_data(start, end-start)
        decoded = list(md.disasm(raw, start))
        if root is None:
            bounded = []
            for ins in decoded:
                bounded.append(ins)
                if ins.mnemonic in ("ret", "jmp", "int3"):
                    break
            decoded = bounded
            raw = raw[:sum(i.size for i in decoded)]
            end = start + len(raw)
        else:
            item["runtimeFunctions"].append(dict(beginRva=hex(start), endRva=hex(end), unwindRva=hex(unwind),
                unwindHeader=pe.get_data(unwind,4).hex(), primaryBeginRva=hex(root[0])))
        item["spans"].append(dict(beginRva=hex(start), endRva=hex(end), bytes=raw.hex(), sha256=sha(raw),
            fullyDecoded=sum(i.size for i in decoded)==len(raw)))
    # RF ranges may contain switch tables. Do not manufacture calls by a
    # linear sweep through their bytes. Seed exact RF component entries,
    # follow ordinary fallthrough/direct internal branches, and stop at any
    # unresolved indirect transfer. Funclet/seeding is conservative, not an
    # assertion that each component is feasible from the public entry.
    limits = [(int(s['beginRva'],16),int(s['endRva'],16)) for s in item['spans']]
    todo = [a for a,b in limits]
    visited = {}
    gaps = []
    def remaining(pc):
        return next((b-pc for a,b in limits if a<=pc<b),0)
    while todo:
        pc=todo.pop()
        while pc not in visited and remaining(pc):
            instructions=list(md.disasm(pe.get_data(pc,min(15,remaining(pc))),pc,count=1))
            if not instructions:
                gaps.append(hex(pc));break
            ins=instructions[0];rec=classify(ins);visited[pc]=rec
            target=int(rec['directTargetRva'],16) if 'directTargetRva' in rec else None
            if target is not None and remaining(target) and (ins.mnemonic=='call' or ins.mnemonic.startswith('j')):
                todo.append(target)
            if ins.mnemonic in ('ret','retf','jmp','int3','ud2','hlt'):
                break
            pc += ins.size
    item['instructions']=[visited[k] for k in sorted(visited)]
    item['cfgUndecodableInstructionRvas']=gaps
    item['cfgSeedRvas']=[hex(a) for a,b in limits]
    item['cfgScope']='Exact component-entry/direct-branch CFG only. Raw RF spans include inline data; unresolved jump-table destinations and EH transfers are not silently recovered.'
    cache[key] = item
    return item

# Exact fixed constructor/vtable evidence, supplemental to the direct graph.
# These are candidates for the fresh record path, not general indirect closure.
vtable = [struct.unpack("<Q", pe.get_data(0x20590+i*8,8))[0]-base for i in range(4)]
assert vtable == [0x2c90,0x29a0,0x2810,0x2c70]
virtual = {0x3214:0x29a0,0x321d:0x2c90,0x2883:0x2810,0x2d1f:0x2c70}

def traverse(start, supplement):
    first = int(region(start)["keyRva"],16)
    pending = [first]
    parents = {first:None}
    edges, api, unresolved = [], [], []
    while pending:
        at = pending.pop(0)
        body = region(at)
        assert len(parents) <= 768, "explicit graph bound exceeded"
        for ins in body["instructions"]:
            site = int(ins["rva"],16)
            if "import" in ins:
                api.append(dict(functionRva=hex(at), **ins))
            if "directTargetRva" in ins:
                target = int(ins["directTargetRva"],16)
                if not executable(target):
                    unresolved.append(dict(reason="direct target outside executable image",functionRva=hex(at),**ins))
                    continue
                child = int(region(target)["keyRva"],16)
                if child != at:
                    edge = dict(sourceFunctionRva=hex(at), siteRva=ins["rva"], targetRva=hex(target),
                        targetFunctionRva=hex(child), op=ins["op"], supplementedVtable=False)
                    edges.append(edge)
                    if child not in parents:
                        parents[child]=edge;pending.append(child)
            if "unresolvedIndirect" in ins or ("pointerSlotRva" in ins and "import" not in ins):
                unresolved.append(dict(functionRva=hex(at),**ins))
                if supplement and site in virtual:
                    child = int(region(virtual[site])["keyRva"],16)
                    edge = dict(sourceFunctionRva=hex(at),siteRva=ins["rva"],targetRva=hex(virtual[site]),
                        targetFunctionRva=hex(child),op=ins["op"],supplementedVtable=True,vtableRva="0x20590")
                    edges.append(edge)
                    if child not in parents:
                        parents[child]=edge;pending.append(child)
    def path(at):
        result = []
        while parents[at] is not None:
            edge=parents[at];result.append(edge);at=int(edge["sourceFunctionRva"],16)
        return list(reversed(result))
    for row in api:
        row["staticPath"] = path(int(row["functionRva"],16))
    return dict(entryRva=hex(start),supplementedFixedFreshVtable=supplement,functionCount=len(parents),
        functions=[hex(v) for v in sorted(parents)],edges=edges,importSites=api,unresolvedIndirectSites=unresolved,
        scope="Conservative exact-RF direct branch/call graph. No branch feasibility, general register/vtable resolution, OS API internals, EH callback or thread/loader-lock proof.")

graphs = {name:traverse(start, supplement) for name,start,supplement in [
    ("prepareDirect",0x3060,False),("commitDirect",0x2d30,False),
    ("prepareFreshVtableSupplement",0x3060,True),("commitFreshVtableSupplement",0x2d30,True),
    ("crtEntryDirect",0x554c,False)]}
selected = [0x3060,0x2d30,0x4700,0x46b0,0x48a0,0x3d10,0x3d50,0x4d38,0xe4e0,0xedbc,
    0x20590,0x2880,0x2810,0x29a0,0x2c90,0x2c70,0x4ad0,0x4d74,0x554c,0x15e00,0x15e1c]
for at in selected:
    if executable(at):region(at)

def raw_data(at, count, label):
    section=next(s for s in pe.sections if s.VirtualAddress<=at<s.VirtualAddress+s.Misc_VirtualSize)
    offset=at-section.VirtualAddress
    raw=pe.get_data(at,count)
    return dict(label=label,rva=hex(at),bytesRequested=count,diskBytes=raw.hex(),
        section=section.Name.decode().rstrip('\0'),sectionOffset=hex(offset),
        entirelyLoaderZeroFilled=offset>=section.SizeOfRawData,
        scope='Disk section storage only; zero-filled image storage is not an observed runtime value.')

globals_data=[raw_data(a,n,label) for a,n,label in [
    (0x30b08,4,'logging threshold initialized to 1'),(0x31a08,8,'first lazy list singleton'),
    (0x31a10,8,'second lazy list singleton'),(0x31a20,4,'logger console configuration'),
    (0x31a24,4,'logger file configuration'),(0x31a28,8,'logger filename'),
    (0x31a30,8,'logger file stream'),(0x32bc8,8,'CRT heap handle'),
    (0x21bf0,40,'initializer-pair table includes heap init and uninit at 21c00/21c08'),
    (0x1f2d8,24,'C++ initializer range'),(0x1f2f8,40,'C initializer range')]]
api_paths={}
for name,g in graphs.items():
    api_paths[name]={}
    for api_name in ['LoadLibraryExW','FreeLibrary','GetProcAddress','HeapAlloc','EnterCriticalSection',
                     'WriteFile','GetStringTypeW','VirtualAlloc','VirtualProtect','VirtualProtectEx','OpenProcess']:
        candidates=[s for s in g['importSites'] if s['import']['name']==api_name]
        api_paths[name][api_name]=min(candidates,key=lambda s:len(s['staticPath'])) if candidates else None
out = dict(schemaVersion=1,kind="pinned-dobby-loader-static-audit",source=dict(path=str(PATH),sha256=PIN,
    bytes=len(data),imageBase=hex(base),imageSize=hex(pe.OPTIONAL_HEADER.SizeOfImage),
    timeDateStamp=hex(pe.FILE_HEADER.TimeDateStamp),entryPointRva=hex(pe.OPTIONAL_HEADER.AddressOfEntryPoint),
    tlsDirectoryRva=hex(pe.OPTIONAL_HEADER.DATA_DIRECTORY[9].VirtualAddress),
    delayImportDirectoryRva=hex(pe.OPTIONAL_HEADER.DATA_DIRECTORY[13].VirtualAddress),
    exports=[dict(name=s.name.decode() if s.name else None,rva=hex(s.address)) for s in pe.DIRECTORY_ENTRY_EXPORT.symbols],
    imports=list(imports.values())),tools=dict(python=__import__('sys').version,pefile=pefile.__version__,capstone=capstone.__version__),
    exactFixedVtable=dict(rva="0x20590",bytes=pe.get_data(0x20590,32).hex(),entries=[hex(v) for v in vtable],
        constructorSites=["0x31fc","0x3203"],candidateDispatches={hex(k):hex(v) for k,v in virtual.items()},
        scope="Explicit fresh routing record constructor stores this fixed table. General object provenance and every downstream virtual call remain unproved."),
    graphs=graphs,selectedImportPaths=api_paths,selectedStaticData=globals_data,regions=[cache[k] for k in sorted(cache)],
    boundaries=dict(diskOnly=True,nativeExecuted=False,loaderLockSafeProved=False,gameReady=False,
        fullIndirectClosureProved=False,allFailurePathsSafeProved=False,osImportImplementationAudited=False,
        ordinaryImportPresenceIsActualCallProof=False,debugNormalRunClosesUntakenBranches=False,
        linearRFDecodeIsInstructionReachability=False))
destination=HERE/'dobby-loader-evidence.json'
destination.write_text(json.dumps(out,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps(dict(path=str(destination),sha256=sha(destination.read_bytes()),regions=len(cache),
    graphs={k:dict(functions=v['functionCount'],imports=sorted({s['import']['name'] for s in v['importSites']}),
        indirect=len(v['unresolvedIndirectSites'])) for k,v in graphs.items()}),ensure_ascii=False))
