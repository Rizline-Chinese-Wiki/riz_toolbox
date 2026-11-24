"""Exact byte readers borrowed from the accepted full518-document independent review."""
from pathlib import Path
import struct,uuid

def check(value,msg):
    if not value: raise ValueError(msg)

def u16(b,p): return struct.unpack_from('<H',b,p)[0]

def u32(b,p): return struct.unpack_from('<I',b,p)[0]

def compressed(b,p):
    n=b[p]
    if n<128:return n,p+1
    if n<192:return ((n&63)<<8)|b[p+1],p+2
    check(n<224,'Invalid blob compressed integer')
    return ((n&31)<<24)|(b[p+1]<<16)|(b[p+2]<<8)|b[p+3],p+4

def portable(p):
    b=Path(p).read_bytes(); check(b[:4]==b'BSJB','Portable PDB metadata signature')
    pos=(16+u32(b,12)+3)&~3; streams=u16(b,pos+2);pos+=4; heaps={}
    for _ in range(streams):
        offset,n=struct.unpack_from('<II',b,pos);pos+=8
        end=b.index(0,pos);name=b[pos:end].decode();pos=(end+4)&~3
        check(offset+n<=len(b),'Portable PDB stream bounds')
        heaps[name]=b[offset:offset+n]
    for name in ('#Pdb','#~','#Blob','#GUID'):check(name in heaps,'Missing portable PDB stream '+name)
    t=heaps['#~'];h=t[6];valid=struct.unpack_from('<Q',t,8)[0]
    check(valid & ((1<<48)-1)==0,'Unexpected type-system tables in standalone portable PDB')
    pos=24;counts={}
    for i in range(64):
        if valid>>i&1:counts[i]=u32(t,pos);pos+=4
    count=counts.get(48,0);check(count>0,'No portable PDB Document table')
    bi=4 if h&4 else 2;gi=4 if h&2 else 2
    def idx(pos,n):return (u32(t,pos) if n==4 else u16(t,pos)),pos+n
    def blob(n):
        length,start=compressed(heaps['#Blob'],n)
        check(start+length<=len(heaps['#Blob']),'Blob bounds')
        return heaps['#Blob'][start:start+length]
    def guid(n):return str(uuid.UUID(bytes_le=heaps['#GUID'][(n-1)*16:n*16]))
    rows=[]
    for _ in range(count):
        name,pos=idx(pos,bi);alg,pos=idx(pos,gi);digest,pos=idx(pos,bi);language,pos=idx(pos,gi)
        parts=blob(name);sep=chr(parts[0]);at=1;segments=[]
        while at<len(parts):
            n,at=compressed(parts,at);segments.append(blob(n).decode('utf-8'))
        rows.append({'document':sep.join(segments),'hashAlgorithm':guid(alg),'sha256':blob(digest).hex(),'language':guid(language)})
    return {'id':heaps['#Pdb'][:20].hex(),'documents':rows}

def codeview(p):
    b=Path(p).read_bytes();check(b[:2]==b'MZ','PE DOS signature')
    start=u32(b,60);check(b[start:start+4]==b'PE\0\0','PE signature')
    ns=u16(b,start+6);optsize=u16(b,start+20);opt=start+24;magic=u16(b,opt)
    check(magic in (0x10b,0x20b),'PE optional header magic')
    dd=opt+(112 if magic==0x20b else 96);rva,n=struct.unpack_from('<II',b,dd+6*8)
    sections=[]
    for i in range(ns):
        at=opt+optsize+i*40;vs,va,rs,rp=struct.unpack_from('<IIII',b,at+8);sections.append((va,max(vs,rs),rp))
    def rvaoff(x):
        for va,n,rp in sections:
            if va<=x<va+n:return rp+x-va
        raise ValueError('Unmapped debug RVA')
    at=rvaoff(rva);check(n%28==0,'Debug directory length');rows=[]
    for i in range(n//28):
        entry=at+i*28;stamp=u32(b,entry+4);typ=u32(b,entry+12);length=u32(b,entry+16);ptr=u32(b,entry+24)
        if typ==2:
            data=b[ptr:ptr+length];check(data[:4]==b'RSDS','CodeView signature')
            rows.append({'guid':str(uuid.UUID(bytes_le=data[4:20])),'guidBytes':data[4:20].hex(),'stamp':stamp,'age':u32(data,20),'path':data[24:].split(b'\0',1)[0].decode('utf-8')})
    check(len(rows)==1,'Exact single CodeView entry');return rows[0]
