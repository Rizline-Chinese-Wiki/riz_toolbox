import hashlib,json,sys
from pathlib import Path
import capstone,pefile
p=Path(sys.argv[1]);b=p.read_bytes();sha=hashlib.sha256(b).hexdigest()
if sha!='853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79':raise SystemExit('Unsupported GameAssembly build')
pe=pefile.PE(data=b,fast_load=True);md=capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_64);md.detail=True
scopes=[('shared-setter-rejected',0x36B0E0,13),('WorkshopLevel.LoadChart',0x3E0D00,0x1a0),('NormalMode.LoadWorkshopLevel',0x3C10D0,0x1b0)]
proof=[]
for name,rva,size in scopes:
 instructions=list(md.disasm(pe.get_data(rva,size),rva));proof.append({'name':name,'rva':hex(rva),'instructions':[{'rva':hex(i.address),'mnemonic':i.mnemonic,'operands':i.op_str}for i in instructions]})
caller=proof[2]['instructions'];call=next((i for i in caller if i['rva']=='0x3c1138'),None)
if call is None or call['mnemonic']!='call' or call['operands']!='0x3e0d00':raise SystemExit('Expected direct parser call missing')
ctor=next((i for i in caller if i['rva']=='0x3c1251'),None)
if ctor is None or ctor['mnemonic']!='call':raise SystemExit('Expected InsideLevelParams construction call missing')
result={'schemaVersion':1,'evidenceKind':'static-disassembly','gameAssemblySha256':sha,'capstoneVersion':capstone.__version__,'pefileVersion':pefile.__version__,'directParserCall':call,'followingConstructorCall':ctor,'scopes':proof,'runtimeAcceptance':False,'limitations':['Static direct call proves a compiled call path, not an executed session or successful native patch.','The shared three-instruction setter is rejected as a hook boundary.']}
Path(sys.argv[2]).write_text(json.dumps(result,indent=2,ensure_ascii=False),encoding='utf-8')
print('Verified direct parser call',call,'; constructor call',ctor)
