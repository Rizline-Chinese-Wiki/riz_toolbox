"""Freeze exact compiled owned images as a compile-time Bootstrap profile."""
from pathlib import Path
import argparse
import hashlib
import json
import struct
import pefile

NAMES=['RizFixedOwnedStart','RizFixedOwnedExecute','RizFixedOwnedRelease','RizFixedOwnedDetach']
PREFIXES=[bytes.fromhex(value) for value in (
    '48895c2408488974241848897c2420','405553574156488d6c24884881ec78010000',
    '40534883ec20ff4960488bd983796000','48896c2420574883ec20488bf933ed')]
def require(value,message):
    if not value:raise ValueError(message)
def sha(data):return hashlib.sha256(data).hexdigest()
def image(path):
    data=path.read_bytes();pe=pefile.PE(data=data)
    require(pe.FILE_HEADER.Machine==0x8664 and pe.OPTIONAL_HEADER.Magic==0x20b,'Owned profile requires AMD64 PE32+')
    return dict(path=str(path.resolve()),module=path.name,sha256=sha(data),fileBytes=len(data),
        preferredImageBase=pe.OPTIONAL_HEADER.ImageBase,machine=pe.FILE_HEADER.Machine,imageSize=pe.OPTIONAL_HEADER.SizeOfImage,
        timeDateStamp=pe.FILE_HEADER.TimeDateStamp,entryRva=pe.OPTIONAL_HEADER.AddressOfEntryPoint,
        exceptionRva=pe.OPTIONAL_HEADER.DATA_DIRECTORY[3].VirtualAddress,exceptionBytes=pe.OPTIONAL_HEADER.DATA_DIRECTORY[3].Size),pe
def raw(pe,rva,size):
    section=pe.get_section_by_rva(rva)
    require(section is not None and size>0 and rva-section.VirtualAddress+size<=section.SizeOfRawData,'Owned profile read leaves disk section')
    data=pe.get_data(rva,size);require(len(data)==size,'Truncated owned image bytes');return data
def extract(target):
    meta,pe=target
    exports={row.name.decode('ascii'):row.address for row in pe.DIRECTORY_ENTRY_EXPORT.symbols if row.name}
    require(all(name in exports for name in NAMES+['RizFixedOwnedMarker']),'Owned target lacks mandatory exact exports')
    marker=exports['RizFixedOwnedMarker']
    require(raw(pe,marker,16)==b'RIZFIXEDOWNEDV1\x00','Owned marker differs')
    section=pe.get_section_by_rva(marker);require(section.Characteristics&0xe0000000==0x40000000,'Owned marker must be read-only data')
    pdata=raw(pe,meta['exceptionRva'],meta['exceptionBytes'])
    require(len(pdata)%12==0,'Owned pdata truncated')
    functions=[struct.unpack_from('<III',pdata,index) for index in range(0,len(pdata),12)]
    require(len(set(row[0] for row in functions))==len(functions),'Owned pdata has duplicate function starts')
    def unwind(rva):
        header=raw(pe,rva,4);version,flags,count=header[0]&7,header[0]>>3,header[2]
        require(version==1 and flags in (0,4),'Selected owned unwind has unsupported handler data')
        size=4+((count+1)&~1)*2+(12 if flags&4 else 0)
        data=raw(pe,rva,size)
        chain=struct.unpack_from('<III',data,size-12) if flags&4 else (0,0,0)
        return dict(rva=rva,bytes=size,flags=flags,prologBytes=header[1],codeSlots=count,chain=chain,rawHex=data.hex(),sha256=sha(data))
    def root(row):
        seen=set()
        while True:
            require(row not in seen and len(seen)<16,'Cyclic owned unwind chain');seen.add(row)
            header=raw(pe,row[2],4)
            if not header[0]>>3&4:return row
            record=unwind(row[2]);row=tuple(record['chain']);require(row in functions,'Owned chained RF absent from actual pdata')
    output=[];unwinds={}
    for kind,name in enumerate(NAMES):
        rva=exports[name];primary=next((row for row in functions if row[0]==rva),None)
        require(primary is not None,'Owned target lacks exact entry RF: '+name)
        group=[row for row in functions if root(row)==primary]
        require(group and all(a[1]==b[0] for a,b in zip(group,group[1:])),'Owned RF group is not contiguous')
        end=max(row[1] for row in group);body=raw(pe,rva,end-rva)
        require(body.startswith(PREFIXES[kind]) and len(body)>=32,'Owned actual prefix differs: '+name)
        for entry in getattr(pe,'DIRECTORY_ENTRY_BASERELOC',[]):
            require(not any(row.type and rva<=row.rva<end for row in entry.entries),'Owned function body contains loader relocations')
        for row in group:unwinds[row[2]]=unwind(row[2])
        output.append(dict(kind=kind,name=name,rva=rva,endRva=end,prefixHex=PREFIXES[kind].hex(),prefixBytes=len(PREFIXES[kind]),
            bodyHex=body.hex(),bodySha256=sha(body),runtimeFunctions=group,argumentCount=[4,2,1,1][kind],returnBytes=[1,4,1,0][kind]))
    return marker,output,[unwinds[key] for key in sorted(unwinds)]
def array(name,data):return 'inline constexpr unsigned char '+name+'[]={'+','.join('0x%02x'%value for value in data)+'};\n'
def main():
    parser=argparse.ArgumentParser();parser.add_argument('--target',type=Path,required=True);parser.add_argument('--executable',type=Path,required=True)
    parser.add_argument('--kernel',type=Path,required=True);parser.add_argument('--header',type=Path,required=True);parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args();require(not args.header.exists() and not args.output.exists(),'Preserve earlier generated owned profile')
    values={name:image(path.resolve()) for name,path in [('Target',args.target),('Executable',args.executable),('Kernel',args.kernel)]}
    require(values['Target'][0]['module']=='RizEngineOwnedTargets.dll' and values['Kernel'][0]['module']=='RizToolbox.EngineNodeKernel.dll','Owned module names differ')
    require(values['Executable'][1].OPTIONAL_HEADER.DATA_DIRECTORY[9].Size==0,'Owned startup EXE must not have TLS')
    marker,functions,unwinds=extract(values['Target'])
    text='#pragma once\n#include "EngineGameProfile.h"\nnamespace RizEngineBootstrapOwnedProfile {\n'
    text+='using RizEngineGameProfile::ImageIdentity;\nusing RizEngineGameProfile::RuntimeFunction;\nusing RizEngineGameProfile::UnwindIdentity;\nusing RizEngineGameProfile::FunctionIdentity;\n'
    for name,(row,pe) in values.items():
        text+=array(name+'Sha256',bytes.fromhex(row['sha256']))
        text+='inline constexpr wchar_t '+name+'Module[]=L"'+row['module']+'";\n'
        text+=('inline constexpr ImageIdentity %s{%sModule,%dull,0x%xull,0x%x,0x%x,0x%x,0x%x,0x%x,0x%x,%sSha256};\n'%
            (name,name,row['fileBytes'],row['preferredImageBase'],row['machine'],row['imageSize'],row['timeDateStamp'],row['entryRva'],row['exceptionRva'],row['exceptionBytes'],name))
    text+='inline constexpr std::uint32_t MarkerRva=0x%xu;\n'%marker
    for row in functions:
        key='Function'+str(row['kind']);text+=array(key+'Prefix',bytes.fromhex(row['prefixHex']))+array(key+'Body',bytes.fromhex(row['bodyHex']))+array(key+'BodySha256',bytes.fromhex(row['bodySha256']))
        text+='inline constexpr RuntimeFunction '+key+'RuntimeFunctions[]={'+','.join('{0x%x,0x%x,0x%x}'%tuple(r) for r in row['runtimeFunctions'])+'};\n'
    for row in unwinds:
        text+=array('Unwind%x'%row['rva'],bytes.fromhex(row['rawHex']))+array('Unwind%xSha256'%row['rva'],bytes.fromhex(row['sha256']))
    text+='inline constexpr FunctionIdentity Functions[]={\n'
    for row in functions:
        key='Function'+str(row['kind']);text+=('    {%d,0x%x,0x%x,%d,%d,%d,%d,%d,%sPrefix,%sBody,%sBodySha256,%sRuntimeFunctions},\n'%
            (row['kind'],row['rva'],row['endRva'],row['prefixBytes'],len(bytes.fromhex(row['bodyHex'])),len(row['runtimeFunctions']),row['argumentCount'],row['returnBytes'],key,key,key,key))
    text+='};\ninline constexpr UnwindIdentity Unwinds[]={\n'
    for row in unwinds:text+='    {0x%x,%d,%d,%d,%d,{0x%x,0x%x,0x%x},Unwind%x,Unwind%xSha256},\n'%(
        row['rva'],row['bytes'],row['flags'],row['prologBytes'],row['codeSlots'],*row['chain'],row['rva'],row['rva'])
    text+='};\n}\n'
    args.header.parent.mkdir(parents=True,exist_ok=True);args.header.write_text(text,encoding='utf-8',newline='\n')
    result=dict(kind='engine-bootstrap-owned-build-profile',images={key:row for key,(row,_) in values.items()},markerRva=marker,functions=functions,unwinds=unwinds,
        generatedHeaderSha256=sha(args.header.read_bytes()),ownedOnly=True,productionAdmissionGranted=False,targetImagesLoaded=False)
    args.output.write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8',newline='\n')
    print(json.dumps(dict(functions=4,unwinds=len(unwinds),headerSha256=result['generatedHeaderSha256'])))
if __name__=='__main__':main()
