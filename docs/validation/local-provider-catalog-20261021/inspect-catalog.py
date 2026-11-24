"""Bounded static catalog/ownership evidence. Never executes or edits the sample."""
import argparse, bisect, hashlib, json, platform
from collections import defaultdict
from pathlib import Path
import capstone, pefile
from capstone.x86 import X86_OP_IMM, X86_OP_MEM, X86_REG_RIP

EXPECTED = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79"
SCOPES = {
    "WorkshopLevels.LoadLevels.MoveNext": (0x3D4060, None),
    "WorkshopLevels.LoadedCallback": (0x3D9720, None),
    "WorkshopLevels.IdSelector": (0x3D9700, None),
    "WorkshopLevelListControl.Start": (0x3DE090, None),
    "WorkshopLevelListControl.ReloadAll": (0x3DD7C0, None),
    "WorkshopLevelListControl.ChangeDisc": (0x3DD3F0, None),
    "WorkshopLevelLoopList.ChangeDisc": (0x3DF190, None),
    "WorkshopLevelLoopList.FavoriteResolver": (0x3D92B0, None),
    "WorkshopLevelLoopList.ChangeLevel": (0x3DF880, None),
    "WorkshopLevelLoopList.ChangeLevelCoroutine.MoveNext": (0x3D29C0, None),
    "WorkshopLevelLoopList.UpdateSorting": (0x3E05A0, None),
    "WorkshopLevelLoopList.UpdatePosition": (0x3E02D0, None),
    "WorkshopLevelLoopList.UpdateLevelStatus": (0x3E01C0, None),
    "WorkshopLevelListItem.OnClick": (0x3DE650, None),
    "WorkshopLevelCardControl.SetInfo": (0x3DC080, None),
    "WorkshopLevelCardControl.PlayLevel": (0x3DBC40, None),
    "WorkshopPreviewControl.TakeAudioClip": (0x3E23F0, None),
    "WorkshopPreviewImageCache.TakeOwnership": (0x3E2840, None),
    "WorkshopPreviewImageCache.TakeOwnership.MoveNext": (0x3D8E60, None),
    "WorkshopCard.StartPlayLevel.MoveNext": (0x3D7630, None),
    "LevelOver.OnDestroy": (0x3CFA50, None),
    "NormalMode.LoadWorkshopLevel": (0x3C10D0, None),
    "ResourceReference.shared.ctor.callsite-inferred": (0xAEF2C0, 0xAEF308),
    "ResourceReference.shared.Release.neighbor-inferred": (0xAEF1F0, 0xAEF2BE),
    "IResourceReference.interface-dispatch.slot1": (0x3000, 0x3080),
}

ap=argparse.ArgumentParser()
ap.add_argument('--game',type=Path,required=True)
ap.add_argument('--dump',type=Path,required=True)
ap.add_argument('--symbols',type=Path,required=True)
ap.add_argument('--out',type=Path,required=True)
a=ap.parse_args()
raw=a.game.read_bytes()
sha=hashlib.sha256(raw).hexdigest()
if sha!=EXPECTED: raise SystemExit('Unsupported sample '+sha)
p=pefile.PE(data=raw,fast_load=True)
by=defaultdict(list)
for item in json.loads(a.symbols.read_text(encoding='utf8'))['ScriptMethod']:
    by[item['Address']].append(item)
starts=sorted(by)
md=capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_64); md.detail=True
a.out.mkdir(parents=True,exist_ok=True)
lines=[]; scopes=[]
for name,(start,end) in SCOPES.items():
    if end is None: end=starts[bisect.bisect_right(starts,start)]
    if end-start>0x1800:raise SystemExit('Oversized scope '+name)
    lines.append(f'\n=== {name} [{start:08x}, {end:08x}) ===')
    for sym in by[start]:lines.append(sym['Name']+' :: '+sym['Signature'])
    ins=[]; calls=[]
    for i in md.disasm(p.get_data(start,end-start),start):
        item={'rva':hex(i.address),'mnemonic':i.mnemonic,'operands':i.op_str,'bytes':i.bytes.hex()}
        note=''
        if i.mnemonic in ('call','jmp') and i.operands and i.operands[0].type==X86_OP_IMM:
            to=i.operands[0].imm; names=[x['Name'] for x in by.get(to,[])]
            item['destinationNames']=names
            if i.mnemonic=='call':calls.append({'at':hex(i.address),'to':hex(to),'names':names})
            if names:note=' ; '+' | '.join(names[:5])
        for op in i.operands:
            if op.type==X86_OP_MEM and op.mem.base==X86_REG_RIP:
                item['ripRelativeRva']=hex(i.address+i.size+op.mem.disp)
        ins.append(item); lines.append(f'{i.address:08x} {i.mnemonic:9} {i.op_str}{note}')
    scopes.append({'name':name,'start':hex(start),'end':hex(end),'symbols':by[start],'calls':calls,'instructions':ins})
(a.out/'disassembly.txt').write_text('\n'.join(lines),encoding='utf8')
(a.out/'boundaries.json').write_text(json.dumps({'schemaVersion':1,'evidenceKind':'static-disassembly','runtimeAcceptance':False,'gameAssemblySha256':sha,'dumpSha256':hashlib.sha256(a.dump.read_bytes()).hexdigest(),'scriptJsonSha256':hashlib.sha256(a.symbols.read_bytes()).hexdigest(),'triageRef':'../local-content-resolution-20261021/triage.json','tools':{'python':platform.python_version(),'capstone':capstone.__version__,'pefile':pefile.__version__},'scopes':scopes},indent=2),encoding='utf8')
dump=a.dump.read_text(encoding='utf8').splitlines()
ranges=[(91668,91728),(95917,96025),(96027,96079),(96132,96300),(96465,96582),(96707,96735)]
excerpt=[]
for lo,hi in ranges:
    excerpt.extend(f'{i+1}: {dump[i]}' for i in range(lo-1,hi))
    excerpt.append('')
(a.out/'metadata-excerpts.txt').write_text('\n'.join(excerpt),encoding='utf8')
for scope in scopes:
    print(scope['name'],scope['start'],scope['end'])
    for c in scope['calls']:
        if c['names']: print(' ',c['at'],c['to'],' | '.join(c['names'][:3]))
