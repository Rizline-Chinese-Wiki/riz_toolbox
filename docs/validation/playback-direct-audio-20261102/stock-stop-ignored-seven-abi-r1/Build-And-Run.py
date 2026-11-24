from pathlib import Path
import datetime, hashlib, json, os, shutil, struct, subprocess, sys

R = Path(__file__).resolve().parent
T = Path('D:/riz-build-cache-next/playback-stock-stop-occurrence-r1-20261102')
N = T/'native-r3'
EXPECTED = '75a41e3e715e7025386a75a993e369e8e22aef3e66eb5de9becd5899a0424490'
def now(): return datetime.datetime.now(datetime.timezone.utc).isoformat()
def fact(p):
    p=Path(p)
    return dict(path=str(p),sha256=hashlib.sha256(p.read_bytes()).hexdigest(),bytes=p.stat().st_size)
def save(p,v): Path(p).write_text(json.dumps(v,indent=2),encoding='utf-8')
def run(command,out,stem,timeout=60):
    row=dict(command=[str(x) for x in command],startedAtUtc=now())
    with (out/(stem+'.stdout.log')).open('xb') as so,(out/(stem+'.stderr.log')).open('xb') as se:
        p=subprocess.Popen(row['command'],stdout=so,stderr=se,creationflags=subprocess.CREATE_NO_WINDOW)
        row['pid']=p.pid
        save(out/(stem+'.launch.json'),row)
        row['actualExitCode']=p.wait(timeout=timeout)
    row['finishedAtUtc']=now()
    (out/(stem+'.exit.txt')).write_text(str(row['actualExitCode']),encoding='utf-8')
    row['stdout']=fact(out/(stem+'.stdout.log')); row['stderr']=fact(out/(stem+'.stderr.log'))
    row['actuals']=[json.loads(x) for x in (out/(stem+'.stdout.log')).read_text(encoding='utf-8-sig',errors='replace').splitlines() if x.startswith('{')]
    save(out/(stem+'.result.json'),row)
    return row

def pe_rsds(p):
    data=Path(p).read_bytes()
    pe=struct.unpack_from('<I',data,0x3c)[0]
    if data[pe:pe+4]!=b'PE\0\0': raise ValueError('not a PE')
    machine,sections=struct.unpack_from('<HH',data,pe+4)
    optlen=struct.unpack_from('<H',data,pe+20)[0]
    opt=pe+24
    magic=struct.unpack_from('<H',data,opt)[0]
    d=opt+(112 if magic==0x20b else 96)+6*8
    debugrva,debugsize=struct.unpack_from('<II',data,d)
    sectionbase=opt+optlen
    def off(rva):
        for i in range(sections):
            s=sectionbase+i*40
            vsize,va,rawsize,ptr=struct.unpack_from('<IIII',data,s+8)
            if va<=rva<va+max(vsize,rawsize): return ptr+rva-va
        raise ValueError('unmapped rva')
    records=[]
    for i in range(debugsize//28):
        at=off(debugrva)+i*28
        typ,size,_,ptr=struct.unpack_from('<IIII',data,at+12)
        if typ==2 and data[ptr:ptr+4]==b'RSDS':
            raw=data[ptr+4:ptr+20]
            age=struct.unpack_from('<I',data,ptr+20)[0]
            pdb=data[ptr+24:ptr+size].split(b'\0',1)[0].decode('utf-8')
            records.append(dict(guidBytes=raw.hex(),age=age,pdbPath=pdb))
    return dict(machine=hex(machine),optionalMagic=hex(magic),rsds=records)

def pdb_rsds(p):
    data=Path(p).read_bytes()
    if not data.startswith(b'Microsoft C/C++ MSF 7.00'): raise ValueError('not an MSF7 PDB')
    block,_,_,dirs,_,mapblock=struct.unpack_from('<IIIIII',data,32)
    blocks=(dirs+block-1)//block
    nums=struct.unpack_from('<'+'I'*blocks,data,mapblock*block)
    directory=b''.join(data[n*block:(n+1)*block] for n in nums)[:dirs]
    count=struct.unpack_from('<I',directory)[0]
    sizes=struct.unpack_from('<'+'I'*count,directory,4)
    pos=4+4*count; streams=[]
    for size in sizes:
        n=0 if size==0xffffffff else (size+block-1)//block
        ns=struct.unpack_from('<'+'I'*n,directory,pos) if n else []
        pos+=n*4
        streams.append(None if size==0xffffffff else b''.join(data[k*block:(k+1)*block] for k in ns)[:size])
    info=streams[1]
    version,signature,age=struct.unpack_from('<III',info)
    return dict(version=version,signature=signature,age=age,guidBytes=info[12:28].hex())

if len(sys.argv)!=2 or sys.argv[1] not in ('build','run'):
    raise SystemExit('Use build or run')
if sys.argv[1]=='build':
    O=R/'build-r1'; O.mkdir(exist_ok=False)
    F=O/'frozen-source'; F.mkdir()
    sources=[]
    for p in sorted((R/'source').iterdir()):
        q=F/p.name; shutil.copyfile(p,q)
        sources.append(dict(original=fact(p),frozen=fact(q)))
    inputs=[fact(N/'RizToolbox.PlaybackSourcePrototype.dll'),fact(N/'PlaybackSourcePrototype.pdb')]
    assert inputs[0]['sha256']==EXPECTED
    flags='/nologo /c /std:c++17 /O2 /W4 /WX /EHa /fp:strict /MD /Z7 /guard:cf'
    commands=['@echo off','call "C:/Program Files (x86)/Microsoft Visual Studio/2019/BuildTools/VC/Auxiliary/Build/vcvars64.bat" >nul','if errorlevel 1 exit /b %errorlevel%',
      f'cl {flags} /Fo"{O}/IgnoredSevenAbiFixture.obj" "{F}/IgnoredSevenAbiFixture.cpp"','if errorlevel 1 exit /b %errorlevel%',
      f'ml64 /nologo /c /Zi /Fo"{O}/IgnoredSevenAbiCaller.obj" "{F}/IgnoredSevenAbiCaller.asm"','if errorlevel 1 exit /b %errorlevel%',
      f'link /nologo /DEBUG /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF "{O}/IgnoredSevenAbiFixture.obj" "{O}/IgnoredSevenAbiCaller.obj" /OUT:"{O}/IgnoredSevenAbiFixture.exe" /PDB:"{O}/IgnoredSevenAbiFixture.pdb"','if errorlevel 1 exit /b %errorlevel%',
      f'dumpbin /headers "{O}/IgnoredSevenAbiFixture.exe" > "{O}/fixture.headers.txt"','if errorlevel 1 exit /b %errorlevel%',
      f'dumpbin /pdbpath:verbose "{O}/IgnoredSevenAbiFixture.exe" > "{O}/fixture.pdbpath.txt"','if errorlevel 1 exit /b %errorlevel%',
      f'dumpbin /unwindinfo "{O}/IgnoredSevenAbiFixture.exe" > "{O}/fixture.unwindinfo.txt"','if errorlevel 1 exit /b %errorlevel%']
    recipe=O/'build.cmd'; recipe.write_text('\n'.join(commands)+'\n',encoding='utf-8')
    result=dict(kind='independent-ignored-seven-abi-fixture-build',runnerPid=os.getpid(),sources=sources,helperInputs=inputs,compilerFlags=flags,gameExecuted=False)
    result['build']=run([os.environ['COMSPEC'],'/d','/c',recipe],O,'build')
    result['passed']=result['build']['actualExitCode']==0
    result['recipe']=fact(recipe); result['runner']=fact(__file__)
    if result['passed']:
        exe=O/'IgnoredSevenAbiFixture.exe'; pdb=O/'IgnoredSevenAbiFixture.pdb'
        pe=pe_rsds(exe); db=pdb_rsds(pdb)
        result['pePdb']=dict(pe=pe,pdb=db,matched=any(x['guidBytes']==db['guidBytes'] and x['age']==db['age'] for x in pe['rsds']))
        result['outputs']=[fact(exe),fact(pdb)]
        result['passed'] &= result['pePdb']['matched']
    save(O/'build-result.json',result)
    print(json.dumps(result),flush=True)
    raise SystemExit(0 if result['passed'] else 1)

O=R/'run-n3-r1'; O.mkdir(exist_ok=False)
exe=R/'build-r1/IgnoredSevenAbiFixture.exe'; helper=O/'RizToolbox.PlaybackSourcePrototype.dll'
shutil.copyfile(N/helper.name,helper)
assert fact(helper)['sha256']==EXPECTED
inputs=[fact(exe),fact(R/'build-r1/IgnoredSevenAbiFixture.pdb'),fact(helper),fact(N/helper.name),fact(N/'PlaybackSourcePrototype.pdb'),fact(__file__)]
frozen=[fact(p) for p in sorted((R/'build-r1/frozen-source').iterdir())]
values={
 1:[0],
 2:[0,0x80000000,0x3f9e0419,0x7fc12345,0x7f812345,0xff800000,1,0xffffffff],
 3:[0,0x8000000000000000,0x3ff3c083126e978d,0x7ff8123456789abc,0x7ff0000000000001,0xfff0000000000000,1,0xffffffffffffffff],
 4:[0,1,0x1122334455667788,0xfedcba9876543210],
 5:[0,1,0x80,0xff],
 6:[0,1,0x80,0xff],
 7:[0,1,0x1122334455667788,0xfedcba9876543210,0xffffffffffffffff]}
result=dict(kind='independent-actual-native-ignored-seven-abi-tls-normal-seh-thread',runnerPid=os.getpid(),startedAtUtc=now(),inputs=inputs,frozenSources=frozen,rows=[],gameExecuted=False,productionAdmission=False)
pe=pe_rsds(helper); db=pdb_rsds(N/'PlaybackSourcePrototype.pdb')
result['helperPePdb']=dict(pe=pe,pdb=db,matched=any(x['guidBytes']==db['guidBytes'] and x['age']==db['age'] for x in pe['rsds']))
cases=[]
for outer in ('pause','replay'):
    for kind,vs in values.items():
        for value in vs:
            for mode in ('normal','seh'):
                cases.append((mode,outer,kind,value))
for kind,vs in values.items():
    value=vs[-1]
    for mode in ('worker-ignore','worker-ignore-seh','worker-nested','worker-nested-seh'):
        cases.append((mode,'pause',kind,value))
try:
    for i,(mode,outer,kind,value) in enumerate(cases):
        stem=f'{i+1:03d}-{mode}-{outer}-k{kind}-{value:016x}'
        row=run([exe,mode,outer,str(kind),f'{value:x}',helper],O,stem,30)
        result['rows'].append(row)
        actual=row['actuals'][-1] if row['actuals'] else {}
        row['passed']=row['actualExitCode']==0 and actual.get('passed') is True
        if not row['passed'] or (i+1)%28==0:
            print(json.dumps(dict(case=i+1,total=len(cases),pid=row['pid'],actualExitCode=row['actualExitCode'],passed=row['passed'],actuals=row['actuals'])),flush=True)
        save(O/'test-result.json',result)
    result['inputUnchanged']=all(fact(x['path'])['sha256']==x['sha256'] for x in inputs+frozen)
    result['counts']=dict(cases=len(cases),passed=sum(x['passed'] for x in result['rows']),checks=sum(x['actuals'][-1].get('checks',0) for x in result['rows'] if x['actuals']),failures=sum(x['actuals'][-1].get('failures',1) for x in result['rows'] if x['actuals']))
    result['passed']=all(x['passed'] for x in result['rows']) and result['inputUnchanged'] and result['helperPePdb']['matched']
finally:
    result['finishedAtUtc']=now(); save(O/'test-result.json',result)
print(json.dumps(dict(counts=result.get('counts'),passed=result.get('passed'),helperPePdb=result['helperPePdb'],inputUnchanged=result.get('inputUnchanged'))),flush=True)
raise SystemExit(0 if result.get('passed') else 1)
