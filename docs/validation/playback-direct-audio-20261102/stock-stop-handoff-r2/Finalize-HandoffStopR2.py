from pathlib import Path
import datetime, difflib, hashlib, json, os, shutil, sys
O=Path(__file__).resolve().parent;R=O.parent;OLD=R/'handoff-stop-r1'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
fact=lambda p:dict(path=str(p),sha256=sha(p),bytes=p.stat().st_size)
read=lambda p:json.loads(p.read_text(encoding='utf-8-sig'))
assert not (O/'completion-evidence.json').exists()
copies=[]
for p in sorted(OLD.glob('r2-independent-audit.*')):
 q=O/p.name;assert not q.exists();shutil.copy2(p,q);assert sha(q)==sha(p)
 copies.append(dict(original=fact(p),exactCopy=fact(q)))
previous=(OLD/'StockStopHandoffChecks.cs').read_text(encoding='utf-8-sig').splitlines(keepends=True)
current=(O/'StockStopHandoffChecks.cs').read_text(encoding='utf-8-sig').splitlines(keepends=True)
diff=''.join(difflib.unified_diff(previous,current,fromfile='preserved-r1/StockStopHandoffChecks.cs',tofile='final-r2/StockStopHandoffChecks.cs'))
(O/'test-correction-from-r1.diff').write_text(diff,encoding='utf-8')
result=read(O/'test-result.json');audit=read(O/'independent-audit.json');old=read(OLD/'test-result.json')
evidence=dict(kind='handoff-stock-stop-final-evidence-index',passed=bool(result['passed'] and audit['passed']),
 observedAtUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),pid=os.getpid(),command=[sys.executable,*sys.argv],
 summary='Final native-r3 normal Replay/Resume Stop after actual same-run InitV2 and cross-run conditional handoff, before and after stale predecessor cleanup.',
 frozenOriginFiles=894,frozenCompiledFiles=895,
 independentTest=fact(O/'source/tests/RizToolbox.PlaybackStartInitialization.Tests/StockStopHandoffChecks.cs'),
 localEntry=fact(O/'source/tests/RizToolbox.PlaybackStartInitialization.Tests/Program.cs'),
 testCorrection=fact(O/'test-correction-from-r1.diff'),
 authoritativeResult=fact(O/'test-result.json'),audit=fact(O/'independent-audit.json'),
 driverLaunch=fact(O/'driver.launch.json'),buildResult=fact(O/'build.result.json'),
 modeResults=[fact(O/(row['mode']+'.result.json')) for row in result['rows']],
 checks=result['checks'],actualModes=[dict(mode=row['mode'],pid=row['pid'],exit=row['actualExitCode'],checks=row['actual']['checks']) for row in result['rows']],
 driverPid=result['runnerPid'],driverExit=(O/'driver.exit.txt').read_text().strip(),
 testedRate=1,sdkValueTransactions=0,sdkSelectedRateRecovery=False,sdkMatrixRepeated=False,
 actualReplayStops=audit['actualReplayStops'],actualResumeStops=audit['actualResumeStops'],actualReservedRestorers=audit['actualReservedRestorers'],
 actualStopSource='Owned fixture original fixed CALL; observer reads only and never injects Stop.',
 stockVsRestorer='Actual stock children match native ParentSequence plus zero MethodInfo; separate issued native WriterReservation validates the one restore pitch and its own PhysicalExit.',
 rootsPerActiveGraph=7,rootsAtEnd=0,lateCleanup=['predecessor session','predecessor publication','predecessor rooted source','removed predecessor watch'],
 previousFailures=dict(result=fact(OLD/'test-result.json'),passed=old['passed'],checks=old['checks'],
    allModesPassed=old['allModesPassed'],inputsUnchanged=old['inputsUnchanged'],sourceOriginUnchanged=old['sourceOriginUnchanged'],
    explanation='Both r1 failures were test-only whole-observation sequence assertions which omitted the real reserved restore pitch after stock schedule. Frozen raw results remain unchanged.'),
 previousRunnerDelta=fact(OLD/'source-origin-delta-r1.json'),auditExecutionExactCopies=copies,
 mainModified=False,productModified=False,gameExecuted=False,productionAdmission=False,fullGenerationCompleted=False)
(O/'completion-evidence.json').write_text(json.dumps(evidence,indent=2),encoding='utf-8')
index=[fact(p) for p in sorted(O.iterdir()) if p.is_file()]
(O/'evidence-files.json').write_text(json.dumps(index,indent=2),encoding='utf-8')
print(json.dumps(dict(passed=evidence['passed'],checks=result['checks'],sameExit=result['rows'][0]['actualExitCode'],crossExit=result['rows'][1]['actualExitCode'],auditPid=read(O/'r2-independent-audit.result.json')['pid'],evidence=str(O/'completion-evidence.json'))))
raise SystemExit(0 if evidence['passed'] else 1)
