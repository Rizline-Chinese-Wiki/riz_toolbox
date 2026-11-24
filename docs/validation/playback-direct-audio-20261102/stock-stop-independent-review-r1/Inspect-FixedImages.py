from pathlib import Path
import hashlib,json,pefile,capstone
R=Path(__file__).resolve().parent
O=R/'static-r1';O.mkdir(exist_ok=False)
GAME=Path(r'D:/riz-build-cache-next/playback-direct-audio-managed-r1-20261102/mapped-r4/image/GameAssembly.dll')
HELPER=Path(r'D:/riz-build-cache-next/playback-stock-stop-occurrence-r1-20261102/native-r3/RizToolbox.PlaybackSourcePrototype.dll')
def inspect(p,ranges):
 b=p.read_bytes();pe=pefile.PE(data=b,fast_load=True)
 pe.parse_data_directories(directories=[pefile.DIRECTORY_ENTRY['IMAGE_DIRECTORY_ENTRY_IMPORT'],pefile.DIRECTORY_ENTRY['IMAGE_DIRECTORY_ENTRY_EXPORT']])
 md=capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_64)
 return dict(path=str(p),sha256=hashlib.sha256(b).hexdigest(),bytes=len(b),machine=hex(pe.FILE_HEADER.Machine),imageBase=hex(pe.OPTIONAL_HEADER.ImageBase),imports=[dict(library=x.dll.decode('ascii','replace'),count=len(x.imports)) for x in getattr(pe,'DIRECTORY_ENTRY_IMPORT',[])],exports=len(getattr(getattr(pe,'DIRECTORY_ENTRY_EXPORT',None),'symbols',[])),disassembly=[dict(name=name,rva=hex(rva),length=size,insns=[dict(rva=hex(i.address),bytes=i.bytes.hex(),mnemonic=i.mnemonic,operands=i.op_str) for i in md.disasm(pe.get_data(rva,size),rva)]) for name,rva,size in ranges])
result=dict(kind='read-only-fixed-image-and-new-native-static-evidence',game=inspect(GAME,[('AudioStop',0x1511810,64),('AudioStopBoolean',0x1511850,80),('ReplayBeforeStop',0x3fbec0,0xa0),('ResumeBeforeStop',0x3fc100,0xb0)]),helper=inspect(HELPER,[]),targetLoadedOrExecuted=False)
(O/'pe-static-evidence.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
print(json.dumps(dict(path=str(O/'pe-static-evidence.json'),sha256=hashlib.sha256((O/'pe-static-evidence.json').read_bytes()).hexdigest(),gameSha256=result['game']['sha256'],targetLoadedOrExecuted=False)))
