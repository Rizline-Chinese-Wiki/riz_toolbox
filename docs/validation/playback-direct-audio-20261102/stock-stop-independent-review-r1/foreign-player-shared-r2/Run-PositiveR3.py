from pathlib import Path
import datetime,hashlib,json,subprocess,shutil,os
R=Path(__file__).resolve().parent;O=R/'positive-r3';O.mkdir(exist_ok=False)
source=Path(r'D:/riz-build-cache-next/playback-stock-stop-occurrence-r1-20261102/native-r3/RizToolbox.PlaybackSourcePrototype.dll')
exe=R/'build/StockStopNativeFixture.exe';helper=O/source.name;shutil.copyfile(source,helper)
def now(): return datetime.datetime.now(datetime.timezone.utc).isoformat()
def fact(p): return dict(path=str(p),sha256=hashlib.sha256(p.read_bytes()).hexdigest(),bytes=p.stat().st_size)
def save(p,v): p.write_text(json.dumps(v,indent=2),encoding='utf-8')
inputs=[fact(exe),fact(helper),fact(source),fact(R/'build/StockStopNativeFixture.pdb'),fact(Path(__file__))]
result=dict(kind='independent-stock-stop-fixed-contract-and-ignored-original-finally-raw-al-regression',passed=False,runnerPid=os.getpid(),startedAtUtc=now(),inputs=inputs,rows=[],gameExecuted=False,productionAdmission=False)
try:
 for mode in ['replay','resume','foreign-player-shared','foreign-player-seh-restore','foreign-start-al-restore','foreign-start-seh-restore']:
  row=dict(mode=mode,command=[str(exe),mode,str(helper)],startedAtUtc=now())
  with (O/(mode+'.stdout.log')).open('xb') as so,(O/(mode+'.stderr.log')).open('xb') as se:
   p=subprocess.Popen(row['command'],stdout=so,stderr=se,creationflags=subprocess.CREATE_NO_WINDOW)
   row['pid']=p.pid;save(O/(mode+'.launch.json'),row);row['actualExitCode']=p.wait(timeout=30)
  row['finishedAtUtc']=now();row['stdout']=fact(O/(mode+'.stdout.log'));row['stderr']=fact(O/(mode+'.stderr.log'))
  row['actuals']=[json.loads(x) for x in (O/(mode+'.stdout.log')).read_text(encoding='utf-8-sig').splitlines() if x.startswith('{')]
  (O/(mode+'.exit.txt')).write_text(str(p.returncode),encoding='utf-8')
  row['passed']=p.returncode==0 and row['actuals'][-1]['passed'] is True and row['actuals'][-1]['failures']==0
  result['rows'].append(row);save(O/(mode+'.result.json'),row)
  print(json.dumps(dict(mode=mode,pid=p.pid,actualExitCode=p.returncode,actuals=row['actuals'])),flush=True)
 result['inputsUnchanged']=all(fact(Path(x['path']))['sha256']==x['sha256'] for x in inputs)
 result['processes']=len(result['rows']);result['checks']=sum(x['actuals'][-1]['checks'] for x in result['rows'])
 result['passed']=result['inputsUnchanged'] and len(result['rows'])==6 and all(x['passed'] for x in result['rows'])
finally:
 result['finishedAtUtc']=now();save(O/'test-result.json',result)
raise SystemExit(0 if result['passed'] else 1)
