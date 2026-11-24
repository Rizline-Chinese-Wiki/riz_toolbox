"""Use the newly compiled combined managed snapshot with the n1 helper only."""
from pathlib import Path
import datetime,hashlib,json,os,subprocess,time
R=Path(__file__).resolve().parent;B=R/'positive-r3';S=B/'source';O=R/'formal-r2';O.mkdir(exist_ok=False)
import sys
sys.stdout=(R/'formal-r2.driver.stdout.log').open('x',encoding='utf-8',buffering=1)
sys.stderr=(R/'formal-r2.driver.stderr.log').open('x',encoding='utf-8',buffering=1)
def now():return datetime.datetime.now(datetime.timezone.utc).isoformat()
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def fact(p):return dict(path=str(p),sha256=sha(p),bytes=p.stat().st_size)
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def save(p,j):
 q=p.with_suffix(p.suffix+'.writing');q.write_text(json.dumps(j,indent=2),encoding='utf-8');q.replace(p)
assert read(B/'build.result.json')['actualExitCode']==0
manifest=read(B/'source-manifest.json');assert all(sha(S/row['path'])==row['sha256'] for row in manifest['files'])
products=read(B/'build-products.json');assert all(sha(Path(row['path']))==row['sha256'] for row in products)
inputs=[B/'inputs'/name for name in ['RizToolbox.PlaybackStockFixture.dll','RizToolbox.SingleReturn.Prototype.dll','RizToolbox.PlaybackSourcePrototype.dll','dobby.dll']]
assert sha(inputs[1])==__import__('hashlib').sha256((R/'native-r3/RizToolbox.SingleReturn.Prototype.dll').read_bytes()).hexdigest()
assert sha(inputs[2])==__import__('hashlib').sha256((R/'native-r3/RizToolbox.PlaybackSourcePrototype.dll').read_bytes()).hexdigest()
app=S/'tests/RizToolbox.PlaybackStartInitialization.Tests/bin/Release/net8.0/RizToolbox.Gameplay.Tests.dll'
pending=['player-sdk-normal','direct-source-first','direct-source-same','direct-source-cross','direct-sdk-foreign','direct-sdk-worker']
pending += ['direct-sdk-'+kind+'-'+str(index) for kind in ['native','runtime'] for index in range(6)]
jobs=[]
result=dict(kind='current-stock-stop-ignored-original-tls-isolation-formal-sdk-matrix',passed=False,runnerPid=os.getpid(),
 startedAtUtc=now(),sourceManifest=fact(B/'source-manifest.json'),products=products,nativeInputs=[fact(p) for p in inputs],
 rows=[],pending=pending[:],parallelLimit=3,gameExecuted=False,installed=False,productionAdmission=False,fullBCompleted=False)
env={**os.environ,'RIZ_INITIALIZER_TRACE':'1','DOTNET_NOLOGO':'1','DOTNET_CLI_TELEMETRY_OPTOUT':'1','DOTNET_CLI_UI_LANGUAGE':'en'}
save(O/'test-result.json',result)
while pending or jobs:
 while pending and len(jobs)<3:
  mode=pending.pop(0);command=list(map(str,['C:/Program Files/dotnet/dotnet.exe',app,mode,*inputs]))
  so=(O/(mode+'.stdout.log')).open('wb');se=(O/(mode+'.stderr.log')).open('wb')
  p=subprocess.Popen(command,cwd=S,env=env,stdout=so,stderr=se,creationflags=subprocess.CREATE_NO_WINDOW)
  row=dict(mode=mode,command=command,pid=p.pid,startedAtUtc=now(),actualExitCode=None,passed=False)
  result['rows'].append(row);jobs.append((p,so,se,row));save(O/(mode+'.launch.json'),row);print(json.dumps(dict(launched=mode,pid=p.pid)),flush=True)
 for job in jobs[:]:
  p,so,se,row=job;code=p.poll()
  if code is None:continue
  so.close();se.close();mode=row['mode'];row.update(actualExitCode=code,finishedAtUtc=now(),stdout=fact(O/(mode+'.stdout.log')),stderr=fact(O/(mode+'.stderr.log')))
  (O/(mode+'.exit.txt')).write_text(str(code))
  try:row['actual']=json.loads((O/(mode+'.stdout.log')).read_text(encoding='utf-8-sig').strip().splitlines()[-1])
  except (ValueError,IndexError):pass
  row['passed']=code==0 and row.get('actual',{}).get('passed') is True
  save(O/(mode+'.result.json'),row);jobs.remove(job);print(json.dumps(dict(exited=mode,actualExitCode=code,passed=row['passed'])),flush=True)
 result.update(pending=pending[:],livePids=[job[0].pid for job in jobs],lastObservedAliveUtc=now());save(O/'test-result.json',result)
 if jobs:time.sleep(3)
result['inputsUnchanged']=all(sha(Path(row['path']))==row['sha256'] for row in products+result['nativeInputs']) and all(sha(S/row['path'])==row['sha256'] for row in manifest['files'])
result.update(passed=result['inputsUnchanged'] and all(row['passed'] for row in result['rows']),processes=len(result['rows']),checks=sum(row.get('actual',{}).get('checks',0) for row in result['rows']),finishedAtUtc=now())
save(O/'test-result.json',result);raise SystemExit(0 if result['passed'] else 1)
