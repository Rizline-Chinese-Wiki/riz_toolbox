"""Bind a cold caller to exact internal MAP symbols without new DLL exports."""
from pathlib import Path
import argparse
import hashlib
import importlib.util
import json
import re
import struct

HERE=Path(__file__).resolve().parent
FORWARD_READER=HERE.parent/'foundation-winhttp-forward-20261025/verify-winhttp-forward.py'
FORWARD_READER_SHA='eab34bfffe42056a53712d4accef1ce94576e8781a298be01710be47acce3dd2'
API_NAMES=['WinHttpOpen','WinHttpGetProxyForUrl','WinHttpGetIEProxyConfigForCurrentUser','WinHttpCloseHandle']
def require(value,message):
    if not value:raise ValueError(message)
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def reader():
    require(sha(FORWARD_READER)==FORWARD_READER_SHA,'Historical forwarding verifier changed')
    spec=importlib.util.spec_from_file_location('frozen_forward_reader_for_cold',FORWARD_READER)
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    return module
def map_symbol(text,name,image):
    # MSVC MAP public lines contain section:offset, symbol, RVA+preferred-base.
    matches=[]
    for line in text.splitlines():
        match=re.match(r'^\s+[0-9a-fA-F]{4}:[0-9a-fA-F]{8,16}\s+'+re.escape(name)+r'\s+([0-9a-fA-F]{16})\b',line)
        if match:matches.append(int(match[1],16)-image['imageBase'])
    require(len(matches)==1 and 0<matches[0]<image['imageSize'],'Missing/ambiguous internal MAP symbol: '+name)
    return matches[0]
def prepare(proxy,map_path,baseline_path,header):
    lib=reader();image=lib.pinned_pe_reader()(proxy);baseline=lib.pinned_pe_reader()(baseline_path)
    actual={(row['name'],row['ordinal']) for row in image['exports']}
    original={(row['name'],row['ordinal']) for row in baseline['exports']}
    require(len(actual)==len(image['exports'])==len(original)==82 and actual==original,'Production 82 name/ordinal contract differs from the held installed baseline')
    require(image['entryRva']!=0,'Production proxy must have its actual DllEntry')
    require(not any(row['name'].startswith('Riz') for row in image['exports']),'Internal diagnostic symbols leaked as production exports')
    text=map_path.read_text(encoding='utf-8-sig',errors='strict')
    info_rva=map_symbol(text,'RizWinHttpForward_GetInfo',image)
    record_rva=map_symbol(text,'RizWinHttpDoorstopRecord',image)
    attach_rva=map_symbol(text,'RizWinHttpDoorstopAttach',image)
    expected_protections={info_rva:0x60000000,record_rva:0xc0000000,attach_rva:0x60000000}
    for rva,required in expected_protections.items():
        section=next((s for s in image['sections'] if s['rva']<=rva<s['rva']+max(s['virtualSize'],s['rawSize'])),None)
        require(section is not None and section['characteristics']&required==required,'Internal MAP symbol has wrong image section permissions')
    slots=[];active=[]
    for kind,name in enumerate(API_NAMES):
        row=next(row for row in image['exports'] if row['name']==name)
        require(not row['forwarder'],'Required WinHTTP name became a PE forwarder')
        code=lib.rva_bytes(image,row['rva'],11)
        require(code[:3]==b'\x4c\x8b\x1d','Required WinHTTP export is not the canonical RIP-relative table load')
        table_slot=row['rva']+7+struct.unpack_from('<i',code,3)[0]
        tail=b'\x41\xff\x23' if kind==0 else bytes([0x41,0xff,0x63,kind*8])
        require(code[7:7+len(tail)]==tail,'Required WinHTTP typed stub has unexpected tail instruction')
        active.append(table_slot);slots.append(row['rva'])
    require(len(set(active))==1,'Four production exports do not share one atomic active-table pointer')
    require(header is None or not header.exists(),'Preserve an existing generated caller contract')
    digest=bytes.fromhex(image['sha256'])
    if header is not None:
        content='#pragma once\n#include <cstdint>\nnamespace RizColdSymbols {\n'
        for name,value in [('GetInfoRva',info_rva),('DoorstopRecordRva',record_rva),('AttachRva',attach_rva),('ImageSize',image['imageSize']),('TimeDateStamp',image['timeDateStamp'])]:
            content+=f'inline constexpr std::uint32_t {name}=0x{value:x}u;\n'
        content+='inline constexpr std::uint32_t ExportRvas[4]={'+','.join(f'0x{x:x}u' for x in slots)+'};\n'
        content+='inline constexpr unsigned char FileSha256[32]={'+','.join(f'0x{x:02x}' for x in digest)+'};\n}\n'
        header.parent.mkdir(parents=True,exist_ok=True);header.write_text(content,encoding='utf-8',newline='\n')
    return dict(kind='production-doorstop-cold-disk-contract',passed=True,proxy=image,baseline=baseline,
        mapPath=str(map_path.resolve()),mapSha256=sha(map_path),getInfoRva=info_rva,doorstopRecordRva=record_rva,attachRva=attach_rva,
        exportRvas=slots,activePointerRva=active[0],generatedHeaderSha256=sha(header) if header else None,
        sourceReaderSha256=FORWARD_READER_SHA,historicalPeReaderSha256=lib.PE_READER_SHA256,
        targetImagesLoaded=False,childrenExecuted=False,gameExecuted=False)
def main():
    parser=argparse.ArgumentParser();parser.add_argument('--proxy',type=Path,required=True);parser.add_argument('--map',type=Path,required=True)
    parser.add_argument('--baseline',type=Path,required=True);parser.add_argument('--header',type=Path);parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args();require(not args.output.exists(),'Preserve an existing disk contract')
    result=prepare(args.proxy.resolve(),args.map.resolve(),args.baseline.resolve(),args.header.resolve() if args.header else None)
    args.output.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')
    print(json.dumps(dict(passed=True,exports=82,getInfoRva=result['getInfoRva'],recordRva=result['doorstopRecordRva'])))
if __name__=='__main__':main()
