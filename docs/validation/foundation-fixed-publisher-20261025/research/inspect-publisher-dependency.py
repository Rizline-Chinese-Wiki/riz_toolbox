"""Disk-only local-call/import evidence for the self-owned fixed publisher.

Does not load a DLL, execute native code, build, start a process or use UI.
Local CFG is bounded to actual RF components/explicit leaf entry windows.
OS/helper implementations and arbitrary indirect closure are not inferred.
"""
from pathlib import Path
import argparse
import bisect
import hashlib
import json
import struct

import capstone
import pefile

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[3]
parser = argparse.ArgumentParser()
parser.add_argument('--publisher', type=Path, default=Path('D:/RizToolbox-build-cache/fixed-publisher-sixth-20261025/RizFixedPublisher.dll'))
args = parser.parse_args()
data = args.publisher.read_bytes()
sha = lambda b: hashlib.sha256(b).hexdigest()
pe = pefile.PE(data=data)
base = pe.OPTIONAL_HEADER.ImageBase
md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
md.detail = True
directory = pe.OPTIONAL_HEADER.DATA_DIRECTORY[3]
pdata = pe.get_data(directory.VirtualAddress, directory.Size)
rows = [struct.unpack_from('<III', pdata, i) for i in range(0, len(pdata)-11, 12)]
starts = [r[0] for r in rows]
assert starts == sorted(starts)
imports = {s.address-base:dict(dll=d.dll.decode(),name=s.name.decode() if s.name else None,
    ordinal=s.ordinal,iatRva=hex(s.address-base)) for d in pe.DIRECTORY_ENTRY_IMPORT for s in d.imports}
exports = {s.name.decode():s.address for s in pe.DIRECTORY_ENTRY_EXPORT.symbols if s.name}

def primary(row):
    seen=set()
    while row not in seen and len(seen)<16:
        seen.add(row);header=pe.get_data(row[2],4)
        if not(header[0]>>3)&4:return row
        row=struct.unpack('<III',pe.get_data(row[2]+4+((header[2]+1)&~1)*2,12))
    raise ValueError('cyclic/overbound CHAININFO')

groups={}
for row in rows:groups.setdefault(primary(row),[]).append(row)

def root_at(rva):
    i=bisect.bisect_right(starts,rva)-1
    return primary(rows[i]) if i>=0 and rows[i][0]<=rva<rows[i][1] else None

def executable(rva):
    return any(s.VirtualAddress<=rva<s.VirtualAddress+max(s.Misc_VirtualSize,s.SizeOfRawData)
        and s.Characteristics&0x20000000 for s in pe.sections)

def classify(ins):
    item=dict(rva=hex(ins.address),bytes=ins.bytes.hex(),op=ins.mnemonic,args=ins.op_str)
    if ins.mnemonic=='call' or ins.mnemonic.startswith('j'):
        operand=ins.operands[0]
        if operand.type==capstone.x86.X86_OP_IMM:item['directTargetRva']=hex(operand.imm)
        elif operand.type==capstone.x86.X86_OP_MEM and operand.mem.base==capstone.x86.X86_REG_RIP:
            slot=ins.address+ins.size+operand.mem.disp;item['pointerSlotRva']=hex(slot)
            if slot in imports:item['import']=imports[slot]
        else:item['unresolvedIndirect']=True
    for operand in ins.operands:
        if operand.type==capstone.x86.X86_OP_MEM and operand.mem.base==capstone.x86.X86_REG_RIP:
            item.setdefault('ripDataRvas',[]).append(hex(ins.address+ins.size+operand.mem.disp))
    return item

cache={}
def region(request):
    root=root_at(request);key=root[0] if root else request
    if key in cache:return cache[key]
    spans=sorted(groups[root]) if root else [(request,request+64,0)]
    result=dict(keyRva=hex(key),requestedRva=hex(request),noRuntimeFunction=root is None,
        runtimeFunctions=[],spans=[],instructions=[],cfgSeedRvas=[])
    limits=[]
    for a,b,u in spans:
        assert 0<b-a<=0x10000 and executable(a)
        raw=pe.get_data(a,b-a)
        if not root:
            leaf=[]
            for ins in md.disasm(raw,a):
                leaf.append(ins)
                if ins.mnemonic in ('ret','jmp','int3'):break
            raw=raw[:sum(i.size for i in leaf)];b=a+len(raw)
        else:
            header=pe.get_data(u,4);x=dict(beginRva=hex(a),endRva=hex(b),unwindRva=hex(u),unwindHeader=header.hex())
            if(header[0]>>3)&3:x['languageHandlerRva']=hex(struct.unpack('<I',pe.get_data(u+4+((header[2]+1)&~1)*2,4))[0])
            result['runtimeFunctions'].append(x)
        result['spans'].append(dict(beginRva=hex(a),endRva=hex(b),bytes=raw.hex(),sha256=sha(raw)))
        result['cfgSeedRvas'].append(hex(a));limits.append((a,b))
    def remaining(at):return next((b-at for a,b in limits if a<=at<b),0)
    pending=[a for a,b in limits];visited={};bad=[]
    while pending:
        at=pending.pop()
        while at not in visited and remaining(at):
            decoded=list(md.disasm(pe.get_data(at,min(15,remaining(at))),at,count=1))
            if not decoded:bad.append(hex(at));break
            ins=decoded[0];item=classify(ins);visited[at]=item
            target=int(item['directTargetRva'],16) if 'directTargetRva'in item else None
            if target is not None and remaining(target):pending.append(target)
            if ins.mnemonic in ('ret','jmp','int3','ud2','hlt'):break
            at+=ins.size
    result['instructions']=[visited[k] for k in sorted(visited)]
    result['cfgUndecodableInstructionRvas']=bad
    cache[key]=result;return result

def traverse(start):
    entry=int(region(start)['keyRva'],16);pending=[entry];seen={entry};edges=[];sites=[];unknown=[];handlers=[]
    while pending:
        at=pending.pop(0);r=region(at)
        assert len(seen)<128
        transfers=[]
        for rf in r['runtimeFunctions']:
            if 'languageHandlerRva'in rf:
                handler=int(rf['languageHandlerRva'],16);handlers.append(dict(functionRva=hex(at),handlerRva=hex(handler)))
                transfers.append((handler,dict(rva=rf['unwindRva'],op='unwind-language-handler')))
        for item in r['instructions']:
            if 'import'in item:sites.append(dict(functionRva=hex(at),**item))
            elif 'unresolvedIndirect'in item or 'pointerSlotRva'in item:unknown.append(dict(functionRva=hex(at),**item))
            if 'directTargetRva'in item:transfers.append((int(item['directTargetRva'],16),item))
        for target,ins in transfers:
            if not executable(target):
                unknown.append(dict(functionRva=hex(at),reason='direct target outside executable image',**ins));continue
            child=int(region(target)['keyRva'],16)
            if child!=at:
                edges.append(dict(sourceFunctionRva=hex(at),siteRva=ins['rva'],targetRva=hex(target),targetFunctionRva=hex(child),op=ins['op']))
                if child not in seen:seen.add(child);pending.append(child)
    return dict(entryRva=hex(start),functionCount=len(seen),functions=[hex(x)for x in sorted(seen)],
        directEdges=edges,importSites=sites,unresolvedIndirectSites=unknown,languageHandlers=handlers)

api_names=['Prepare','Bind','Commit','GetInfo','GetInfoSize','GetVersion']
graphs={name:traverse(exports['RizFixedPublisher_'+name])for name in api_names}
originals=[]
for name in ['Start','Execute','Release','Detach']:
    begin=exports['RizFixedOriginal'+name];prefix=exports['RizFixedOriginal'+name+'PrefixEnd'];end=exports['RizFixedOriginal'+name+'End']
    r=region(begin)
    originals.append(dict(kind=name,beginRva=hex(begin),prefixEndRva=hex(prefix),endRva=hex(end),
        prefixBytes=pe.get_data(begin,prefix-begin).hex(),tailBytes=pe.get_data(prefix,end-prefix).hex(),
        runtimeFunctions=r['runtimeFunctions'],instructions=r['instructions']))

strings=[]
for text in ['RizEngineNodeContext_GetAbiVersion','RizEngineNodeContext_GetEntry','RizEngineNodeContext_GetInfo',
             'RizFixedOwnedMarker','RizFixedOwnedStart','RizFixedOwnedExecute','RizFixedOwnedRelease','RizFixedOwnedDetach']:
    pattern=text.encode()+b'\0';at=data.find(pattern);locations=[]
    while at>=0:locations.append(pe.get_rva_from_offset(at));at=data.find(pattern,at+1)
    strings.append(dict(text=text,stringRvas=[hex(v)for v in locations],references=[dict(functionRva=r['keyRva'],**i)
        for r in cache.values()for i in r['instructions']if any(hex(v)in i.get('ripDataRvas',[])for v in locations)]))

source_names=['FixedPublisher.h','FixedPublisher.cpp','FixedOriginals.asm']
out=dict(schemaVersion=1,kind='owned-fixed-publisher-dependency-review',
    image=dict(path=str(args.publisher),sha256=sha(data),bytes=len(data),imageBase=hex(base),imageSize=hex(pe.OPTIONAL_HEADER.SizeOfImage),
        entryPointRva=hex(pe.OPTIONAL_HEADER.AddressOfEntryPoint),tlsRva=hex(pe.OPTIONAL_HEADER.DATA_DIRECTORY[9].VirtualAddress),
        delayImportRva=hex(pe.OPTIONAL_HEADER.DATA_DIRECTORY[13].VirtualAddress),loadConfigRva=hex(pe.OPTIONAL_HEADER.DATA_DIRECTORY[10].VirtualAddress),
        machine=hex(pe.FILE_HEADER.Machine),imports=list(imports.values()),exports={k:hex(v)for k,v in exports.items()},
        runtimeFunctionRows=len(rows)),graphs=graphs,originalStubs=originals,fixedGetProcAddressNames=strings,
    regions=[cache[k]for k in sorted(cache)],
    sources=[dict(path='native/RizToolbox.FixedPublisher/'+n,sha256=sha((REPO/'native/RizToolbox.FixedPublisher'/n).read_bytes()))for n in source_names],
    tools=dict(python=__import__('sys').version,pefile=pefile.__version__,capstone=capstone.__version__),
    boundaries=dict(diskOnly=True,nativeExecutedByThisReview=False,localExactCfgAndImportSitesReviewed=True,
        fullIndirectCallClosureProved=False,osImplementationAudited=False,helperImplementationAudited=False,
        allTransitiveDependenciesLoaderSafeProved=False,earlyLoaderAdmissionVerified=False,
        arbitraryGprOrFlagsTransparencyProved=False,productionV4Issued=False,gameReady=False,engineCancellationCompleted=False))
destination=HERE/'publisher-dependency-evidence.json';destination.write_text(json.dumps(out,indent=2)+'\n',encoding='utf-8')
print(json.dumps(dict(path=str(destination),sha256=sha(destination.read_bytes()),regions=len(cache),
    graphCounts={k:dict(functions=v['functionCount'],indirect=len(v['unresolvedIndirectSites']))for k,v in graphs.items()})))
