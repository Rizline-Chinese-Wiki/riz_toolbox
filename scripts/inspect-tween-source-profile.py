"""Read-only exact Tween Update source spans; never load or execute game code."""
import argparse, hashlib, json, struct
from pathlib import Path
import capstone
import pefile
GAME_SHA = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79"
# Whole instruction spans. A store and its original write-barrier call form one
# indivisible replay unit, including computations of the barrier slot address.
SITES = (
 (0,"ReadyPopFront",0x398F18,0x398F1D,0x398F00),
 (1,"ReadyPopAppend",0x3996B8,0x3996BD,0x3996A0),
 (2,"PlayingInsertFront",0x3995BB,0x3995D0,0),
 (3,"PlayingInsertAppend",0x399D97,0x399DAC,0),
 (4,"PlayingMoveWrite",0x3997E1,0x3997F6,0),
 (5,"PlayingMoveClear",0x399815,0x399829,0),
 (6,"PlayingTailClear",0x39966A,0x39967F,0),
 (7,"ScheduledPushFront",0x399E97,0x399E9C,0),
 (8,"ScheduledPushTail",0x399F7D,0x399F82,0),
 (9,"ScheduledSetItem",0x399FD8,0x399FDD,0),
 (10,"ScheduledRemoveAdvance",0x399F98,0x399F9D,0),
 (11,"ScheduledRemoveTail",0x399FFA,0x399FFF,0),
 (12,"ScheduledRemoveCurrent",0x39A02C,0x39A031,0),
 (13,"TailDeadDecision",0x399634,0x399641,0x39964E),
 (14,"PlayingExecuteFront",0x3995DA,0x3995DF,0),
 (15,"PlayingExecuteExisting",0x399785,0x39978A,0),
 (16,"PlayingExecuteAppend",0x399DB6,0x399DBB,0),
 (17,"UpdateOnStartFront",0x399565,0x39956A,0),
 (18,"UpdateOnStartAppend",0x399D41,0x399D46,0),
 (19,"PlayOnStart",0x3957E8,0x3957ED,0),
 (20,"PlayOnComplete",0x395D50,0x395D55,0),
 (21,"ExecuteOnComplete",0x3984C2,0x3984C7,0),
)
def sha(b): return hashlib.sha256(b).hexdigest()
def inspect(path):
 raw=path.read_bytes()
 if sha(raw)!=GAME_SHA: raise ValueError("Unsupported exact GameAssembly")
 pe=pefile.PE(data=raw,fast_load=True);pe.parse_data_directories(directories=[3])
 rf=[r.struct for r in pe.DIRECTORY_ENTRY_EXCEPTION if r.struct.BeginAddress==0x398D60]
 if len(rf)!=1 or rf[0].EndAddress!=0x39A194 or rf[0].UnwindData!=0x1CE66EC: raise ValueError("Actual Update RF identity differs")
 decoder=capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_64);decoder.detail=True
 body=pe.get_data(0x398D60,0x39A194-0x398D60)
 linear=list(decoder.disasm(body,0x398D60))
 parents=[(0x398D60,0x39A194,0x1CE66EC),(0x3956F0,0x3964E8,0x1CE5F64),(0x396E40,0x398554,0x1CE5FE0)]
 code_ends={0x398D60:0x39A114,0x3956F0:0x396468,0x396E40:0x398514}
 decoded={a:list(decoder.disasm(pe.get_data(a,code_ends[a]-a),a)) for a,_,_ in parents}
 boundaries={i.address for rows in decoded.values() for i in rows}|{i.address+i.size for rows in decoded.values() for i in rows}
 branches=[]
 for i in [instruction for rows in decoded.values() for instruction in rows]:
  if (i.mnemonic.startswith("j") or i.mnemonic=="call") and i.operands and i.operands[0].type==capstone.x86.X86_OP_IMM:
   branches.append(dict(sourceRva=i.address,targetRva=i.operands[0].imm,mnemonic=i.mnemonic))
 switch_tables=[]
 for parent_begin,table_rvas in [(0x398D60,(0x39A114,0x39A154)),(0x3956F0,(0x396468,0x3964A8)),(0x396E40,(0x398514,))]:
  parent_end=next(b for a,b,_ in parents if a==parent_begin)
  for table in table_rvas:
   data=pe.get_data(table,64);targets=struct.unpack("<16I",data)
   if any(not parent_begin<=target<code_ends[parent_begin] for target in targets):raise ValueError("An inline switch leaves its exact parent code")
   switch_tables.append(dict(parentBeginRva=parent_begin,tableRva=table,bytesHex=data.hex(),sha256=sha(data),targets=targets))
   branches.extend(dict(sourceRva=table+index*4,targetRva=target,mnemonic="pinned-inline-switch-entry") for index,target in enumerate(targets))
 shadow_cells=[]
 for a,b,_ in parents:
  accesses=[]
  for instruction in decoded[a]:
   for operand in instruction.operands:
    if operand.type==capstone.x86.X86_OP_MEM and operand.mem.base==capstone.x86.X86_REG_RSP and operand.mem.index==0 and 0<=operand.mem.disp<8:
     accesses.append(dict(rva=instruction.address,mnemonic=instruction.mnemonic,operands=instruction.op_str))
  shadow_cells.append(dict(parentBeginRva=a,outgoingHomeCellOffset=0,bytes=8,explicitRspCellAccesses=accesses,
   inspectedCodeEndRva=code_ends[a],aliasAndCalleeWritesProved=False))
 facts=[]
 for identity,name,begin,end,dead in SITES:
  b=pe.get_data(begin,end-begin);ins=list(decoder.disasm(b,begin))
  if begin not in boundaries or end not in boundaries or sum(i.size for i in ins)!=len(b):raise ValueError("Source span splits or omits actual instructions: "+name)
  interior=[r for r in branches if not begin<=r['sourceRva']<end and begin<r['targetRva']<end]
  # Do not suppress conflicting edges. The publisher must split that span or
  # preserve every target in a proved relay before any publication is allowed.
  parent=next((a,b,u) for a,b,u in parents if a<=begin<end<=b)
  actual=[r.struct for r in pe.DIRECTORY_ENTRY_EXCEPTION if r.struct.BeginAddress==parent[0]]
  if len(actual)!=1 or (actual[0].EndAddress,actual[0].UnwindData)!=(parent[1],parent[2]):raise ValueError("Source parent actual RF differs")
  header=pe.get_data(parent[2],4);unwind_size=(4+header[2]*2+3)&~3
  if header[0]>>3!=0:raise ValueError("The selected source parents require a new unwind interpretation")
  source_unwind=pe.get_data(parent[2],unwind_size)
  facts.append(dict(id=identity,name=name,parentBeginRva=parent[0],parentEndRva=parent[1],parentUnwindRva=parent[2],parentUnwindHex=source_unwind.hex(),parentUnwindSha256=sha(source_unwind),beginRva=begin,endRva=end,width=len(b),bytesHex=b.hex(),sha256=sha(b),
   deadContinuationRva=dead,instructions=[dict(rva=i.address,size=i.size,bytes=i.bytes.hex(),mnemonic=i.mnemonic,operands=i.op_str) for i in ins],
   incomingBranches=[r for r in branches if begin<=r['targetRva']<end],interiorIncomingBranches=interior,
   outgoingBranches=[r for r in branches if begin<=r['sourceRva']<end and not begin<=r['targetRva']<end],
   replayRequiresPcRelativeRelocation=any(i.mnemonic.startswith('j') or i.mnemonic=='call' or 'rip' in i.op_str for i in ins),
   rawStoreAndBarrierOneReplay=identity in (2,3,4,5,6)))
 h=pe.get_data(0x1CE66EC,4);n=(4+h[2]*2+3)&~3;u=pe.get_data(0x1CE66EC,n)
 return dict(schemaVersion=1,kind="fixed-tween-source-site-input-facts",passed=True,gameAssembly=dict(path=str(path.resolve()),sha256=sha(raw)),
  parent=dict(beginRva=0x398D60,endRva=0x39A194,bodyBytes=len(body),bodySha256=sha(body),unwindRva=0x1CE66EC,unwindPrefixHex=u.hex(),unwindPrefixSha256=sha(u),
   leadingLinearBytes=sum(i.size for i in linear),unwindVersion=h[0]&7,unwindFlags=h[0]>>3),sites=facts,
  switchTables=switch_tables,outgoingHomeCellObservations=shadow_cells,anyInteriorIncomingBranches=any(r['interiorIncomingBranches'] for r in facts),sourceCodeExecuted=False,gameExecuted=False,
  limitations=["Direct immediate edges and all five fixed 16-entry inline switch tables are included; out-of-function predecessors and arbitrary indirect calls need independent review.",
   "Input evidence only; relays, registered unwind, all-register preservation, roots, gate/journal dispatch and publication remain unproved."])
if __name__=="__main__":
 p=argparse.ArgumentParser();p.add_argument('--game-assembly',type=Path,required=True);p.add_argument('--output-json',type=Path,required=True);a=p.parse_args()
 if a.output_json.exists():raise SystemExit('Preserve prior source evidence; fresh output required')
 result=inspect(a.game_assembly);a.output_json.parent.mkdir(parents=True,exist_ok=True);a.output_json.write_text(json.dumps(result,indent=2),encoding='utf-8')
 print('Read-only source instruction spans verified; no native or game code executed.')
