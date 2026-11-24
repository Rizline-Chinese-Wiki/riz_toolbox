from pathlib import Path
import datetime,hashlib,json,subprocess,shutil,os
R=Path(__file__).resolve().parent
O=R/'run'
O.mkdir(exist_ok=False)
exe=R/'build/StockStopNativeFixture.exe'
source=Path(r'D:/riz-build-cache-next/playback-stock-stop-occurrence-r1-20261102/native-r2/RizToolbox.PlaybackSourcePrototype.dll')
helper=O/source.name
shutil.copyfile(source,helper)
def now(): return datetime.datetime.now(datetime.timezone.utc).isoformat()
def fact(p): return dict(path=str(p),sha256=hashlib.sha256(p.read_bytes()).hexdigest(),bytes=p.stat().st_size)
def save(p,v): p.write_text(json.dumps(v,indent=2),encoding='utf-8')
inputs=[fact(exe),fact(helper),fact(source),fact(R/'build/StockStopNativeFixture.pdb'),fact(Path(__file__))]
result=dict(kind='independent-stock-stop-foreign-player-shared-source-actual-native-counterexample',runnerPid=os.getpid(),startedAtUtc=now(),inputs=inputs,rows=[],gameExecuted=False,productionAdmission=False)
try:
 for mode in ['replay','foreign-player-shared']:
  command=[str(exe),mode,str(helper)]
  row=dict(mode=mode,command=command,startedAtUtc=now())
  with (O/(mode+'.stdout.log')).open('xb') as so,(O/(mode+'.stderr.log')).open('xb') as se:
   p=subprocess.Popen(command,stdout=so,stderr=se,creationflags=subprocess.CREATE_NO_WINDOW)
   row['pid']=p.pid
   save(O/(mode+'.launch.json'),row)
   row['actualExitCode']=p.wait(timeout=30)
  row['finishedAtUtc']=now()
  row['stdout']=fact(O/(mode+'.stdout.log'))
  row['stderr']=fact(O/(mode+'.stderr.log'))
  row['actuals']=[json.loads(x) for x in (O/(mode+'.stdout.log')).read_text(encoding='utf-8-sig').splitlines() if x.startswith('{')]
  (O/(mode+'.exit.txt')).write_text(str(p.returncode),encoding='utf-8')
  result['rows'].append(row)
  save(O/(mode+'.result.json'),row)
  print(json.dumps(dict(mode=mode,pid=p.pid,actualExitCode=p.returncode,actuals=row['actuals'])),flush=True)
 control,bad=result['rows']
 result['controlPassed']=control['actualExitCode']==0 and control['actuals'][-1]['passed'] is True
 diag=bad['actuals'][0]
 result['counterexampleConfirmed']=bad['actualExitCode']==1 and diag['actualStockStop'] is True and diag['expectedStockStop'] is False and diag['stopFlags']==129 and diag['outerPlayer']!=diag['foreignPlayer'] and diag['stopCaller']==diag['fixedStopCaller'] and bad['actuals'][-1]['failures']==2
 result['inputsUnchanged']=all(fact(Path(x['path']))['sha256']==x['sha256'] for x in inputs)
 prep=json.loads((R/'preparation.json').read_text())
 result['upstreamSourceUnchanged']=all(fact(Path(x['original']))['sha256']==x['originalSha256'] for x in prep['sources'])
 result['evidencePassed']=result['controlPassed'] and result['counterexampleConfirmed'] and result['inputsUnchanged'] and result['upstreamSourceUnchanged']
finally:
 result['finishedAtUtc']=now()
 save(O/'test-result.json',result)
raise SystemExit(0 if result.get('evidencePassed') else 1)
