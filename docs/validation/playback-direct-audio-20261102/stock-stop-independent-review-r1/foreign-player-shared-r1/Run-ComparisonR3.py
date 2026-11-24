from pathlib import Path
import datetime,hashlib,json,subprocess,shutil,os
R=Path(__file__).resolve().parent
O=R/'run-n3'
O.mkdir(exist_ok=False)
exe=R/'build/StockStopNativeFixture.exe'
source=Path(r'D:/riz-build-cache-next/playback-stock-stop-occurrence-r1-20261102/native-r3/RizToolbox.PlaybackSourcePrototype.dll')
helper=O/source.name
shutil.copyfile(source,helper)
def now(): return datetime.datetime.now(datetime.timezone.utc).isoformat()
def fact(p): return dict(path=str(p),sha256=hashlib.sha256(p.read_bytes()).hexdigest(),bytes=p.stat().st_size)
def save(p,v): p.write_text(json.dumps(v,indent=2),encoding='utf-8')
inputs=[fact(exe),fact(helper),fact(source),fact(R/'build/StockStopNativeFixture.pdb'),fact(Path(__file__))]
result=dict(kind='independent-stock-stop-foreign-player-shared-source-actual-native-same-exe-r3-comparison',runnerPid=os.getpid(),startedAtUtc=now(),inputs=inputs,rows=[],gameExecuted=False,productionAdmission=False)
try:
 for mode in ['replay','resume','foreign-player-shared']:
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
 control,resume,bad=result['rows']
 result['controlPassed']=all(x['actualExitCode']==0 and x['actuals'][-1]['passed'] is True for x in [control,resume])
 diag=bad['actuals'][0]
 result['counterexampleBoundaryFixed']=bad['actualExitCode']==1 and diag['actualStockStop'] is False and diag['expectedStockStop'] is False and diag['stopFlags']==1 and diag['stopParentSequence']==0 and diag['outerPlayer']!=diag['foreignPlayer'] and diag['stopCaller']==diag['fixedStopCaller'] and bad['actuals'][-1]['failures']==1 and 'line 163:' in (O/'foreign-player-shared.stderr.log').read_text()
 result['inputsUnchanged']=all(fact(Path(x['path']))['sha256']==x['sha256'] for x in inputs)
 prep=json.loads((R/'preparation.json').read_text())
 result['upstreamSourceUnchanged']=all(fact(Path(x['original']))['sha256']==x['originalSha256'] for x in prep['sources'])
 result['evidencePassed']=result['controlPassed'] and result['counterexampleBoundaryFixed'] and result['inputsUnchanged'] and result['upstreamSourceUnchanged']
finally:
 result['finishedAtUtc']=now()
 save(O/'test-result.json',result)
raise SystemExit(0 if result.get('evidencePassed') else 1)
