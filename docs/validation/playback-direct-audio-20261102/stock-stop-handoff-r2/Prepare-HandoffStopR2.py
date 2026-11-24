from pathlib import Path
import datetime, hashlib, json, shutil
O=Path(__file__).resolve().parent;R=O.parent;LIVE=R/'source';S=O/'source'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
fact=lambda p:dict(path=str(p),sha256=sha(p),bytes=p.stat().st_size)
save=lambda p,v:p.write_text(json.dumps(v,indent=2),encoding='utf-8')
assert not S.exists();S.mkdir();rows=[]
for p in sorted(LIVE.rglob('*')):
 relative=p.relative_to(LIVE)
 if not p.is_file() or set(relative.parts)&{'bin','obj','.git','__pycache__'}:continue
 q=S/relative;q.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(p,q);assert sha(q)==sha(p)
 rows.append(dict(path=relative.as_posix(),sha256=sha(p),bytes=p.stat().st_size))
assert len(rows)==894,len(rows)
save(O/'origin-source-manifest.json',dict(origin=str(LIVE),files=rows))
tests=S/'tests/RizToolbox.PlaybackStartInitialization.Tests';shutil.copy2(O/'StockStopHandoffChecks.cs',tests/'StockStopHandoffChecks.cs')
p=tests/'Program.cs';before=fact(p);text=p.read_text(encoding='utf-8-sig')
anchor='  if(mode is "stock-stop-replay" or "stock-stop-resume")return RunStockStop(a,mode);';assert text.count(anchor)==1
text=text.replace(anchor,'  if(mode is "stock-stop-handoff-same" or "stock-stop-handoff-cross")return RunStockStopHandoff(a,mode);\n'+anchor);p.write_text(text,encoding='utf-8')
inputs=O/'inputs';inputs.mkdir();Q=R.parent/'playback-progress-control-isolation-r1-20261102';native=[]
for p in [R/'fixture-r1/RizToolbox.PlaybackStockFixture.dll',R/'native-r3/RizToolbox.SingleReturn.Prototype.dll',R/'native-r3/RizToolbox.PlaybackSourcePrototype.dll',Q/'managed-checks-r5/inputs/dobby.dll']:
 q=inputs/p.name;shutil.copy2(p,q);assert sha(q)==sha(p);native.append(dict(origin=fact(p),frozen=fact(q)))
files=[dict(path=p.relative_to(S).as_posix(),sha256=sha(p),bytes=p.stat().st_size) for p in sorted(S.rglob('*')) if p.is_file()]
save(O/'source-manifest.json',dict(source=str(S),origin=str(LIVE),files=files))
save(O/'preparation-result.json',dict(passed=True,observedAtUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
 originSourceCount=len(rows),frozenSourceCount=len(files),sourceManifest=fact(O/'source-manifest.json'),
 nativeInputs=native,nativeBuildReceipt=fact(R/'native-r3/native-build-result.json'),fixtureProfile=fact(R/'profile-r1/profile.json'),
 previousAssertionFailuresPreserved=fact(R/'handoff-stop-r1/test-result.json'),
 mainModified=False,productModified=False,sourceOriginModified=False,sdkValueTransactions=0,sdkSelectedRateRecovery=False,gameExecuted=False,productionAdmission=False))
print(json.dumps(dict(passed=True,source=str(S),originSourceCount=len(rows),frozenSourceCount=len(files))))
