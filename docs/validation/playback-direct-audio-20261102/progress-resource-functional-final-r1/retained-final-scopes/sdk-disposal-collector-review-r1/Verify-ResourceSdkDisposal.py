"""Independent read-only proof verifier. Never runs a build, test, or collector.

--preflight records currently available integrity and exact invocation evidence.
The default final mode only closes coverage after the original observer has
collected the real successful SDK result. All writes stay in this review folder.
"""
from __future__ import annotations
import argparse
import ast
from datetime import datetime, timezone
import difflib
import hashlib
import json
import os
from pathlib import Path
import subprocess

REVIEW = Path(__file__).resolve().parent
U = REVIEW.parent
OLD = U/'functional-matrix-r2'
NEW = U/'sdk-current-dispose-r1'
R7 = U/'foundation-r7'
OBS = U/'sdk-disposal-observer-r1'
COMBINED = U/'functional-combined-after-disposal-r1'
REL = 'tests/RizToolbox.PlaybackStartInitialization.Tests/ProgressClipResourceLifecycleChecks.cs'
SCRIPT_NAMES = ['Run-ResourceSdkDisposeR1.py','Collect-ResourceSdkDisposalR1.py','Observe-ResourceSdkDisposalR1.py']
INPUTS = [
    ('RizToolbox.PlaybackStockFixture.dll','4a76ea8f8a4bda5f8fde7b411ddb602085a2b99f95fd1883d0cce30febb32d1d'),
    ('RizToolbox.SingleReturn.Prototype.dll','7ff2b0546f61081d9c4e77e63072e69b073285e0ccb0bbb7156148f40a17fa50'),
    ('RizToolbox.PlaybackSourcePrototype.dll','ef24415492cd31333a692ef689f2dac5c34c7ca7a58a6b771316ceb0dc5d353c'),
    ('dobby.dll','8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded'),
]
OLD_CHECKS = {'resource-first-late-retry':28,'resource-handoff-late-retry':31,'resource-current':17,
 'resource-capture':25,'resource-yield-retry':29,'resource-withdraw-retire':12,'resource-foreign':22,
 'resource-worker':22,'resource-seh':20,'resource-dormant-setclip':15,'resource-dormant-progressclip':16}
MODES = set(OLD_CHECKS) | {'resource-sdk-current'}
DELTA3 = {'tests/RizToolbox.GameCamera.Tests/NativeCameraChecks.cs','tests/RizToolbox.InitializationRootFree.Tests/Program.cs','tests/RizToolbox.InitializationRootFree.Tests/RizToolbox.InitializationRootFree.Tests.csproj'}
FACTS = {}
FIXED = {}
ISSUES = []

def now():
    return datetime.now(timezone.utc).isoformat()

def fact(path, fixed=False):
    path = Path(path)
    raw = path.read_bytes()
    result = dict(path=str(path),sha256=hashlib.sha256(raw).hexdigest(),bytes=len(raw))
    FACTS[str(path)] = result
    if fixed:
        FIXED[str(path)] = result
    return result

def read(path, fixed=False):
    path = Path(path)
    fact(path,fixed)
    return json.loads(path.read_text(encoding='utf-8-sig'))

def check(value, code, detail):
    if not value:
        ISSUES.append(dict(code=code,detail=detail))
    return bool(value)

def verify(row, fixed=False):
    actual = fact(row['path'],fixed)
    return check(actual == row,'recorded-file-fact',dict(recorded=row,actual=actual))

def file_inventory(root):
    return {p.relative_to(root).as_posix() for p in root.rglob('*') if p.is_file()
      and not set(p.relative_to(root).parts) & {'bin','obj','.git','__pycache__'}}

def source_manifest(folder, result_ref=None):
    path = folder/'source-manifest.json'
    if result_ref:
        check(Path(result_ref['path']) == path,'source-manifest-reference-path',str(folder))
        verify(result_ref,True)
    data = read(path,True)
    root = folder/'source'
    check(Path(data['source']) == root,'frozen-source-root-binding',str(folder))
    rows = data['files']
    index = {r['path']:r for r in rows}
    check(len(index) == len(rows) == 913,'unique-source-count',str(folder))
    check(file_inventory(root) == set(index),'source-inventory',str(folder))
    for rel,row in index.items():
        candidate = (root/rel).resolve()
        check(candidate.is_relative_to(root.resolve()),'source-within-root',rel)
        actual = fact(candidate)
        check(actual['sha256'] == row['sha256'] and actual['bytes'] == row['bytes'],'frozen-source-fact',str(candidate))
    return data,index

def app(folder):
    return folder/'source/tests/RizToolbox.PlaybackStartInitialization.Tests/bin/Release/net8.0/RizToolbox.Gameplay.Tests.dll'

def expected_command(folder,mode):
    return ['C:/Program Files/dotnet/dotnet.exe',str(app(folder)),mode,*[str(folder/'inputs'/name) for name,_ in INPUTS]]

def native_inputs(folder,result):
    rows = result['nativeInputs']
    expected_names = [name for name,_ in INPUTS]
    check(len(rows)==4 and [Path(r['path']).name for r in rows]==expected_names,'ordered-native-input-count',str(folder))
    for row,(name,expected_sha) in zip(rows,INPUTS):
        check(Path(row['path']) == folder/'inputs'/name and row['sha256']==expected_sha,'native-input-scope-and-identity',row['path'])
        verify(row,True)

def case_row(folder,row,expected_checks,expected_exit=0,pending_allowed=False):
    mode = row['mode']
    check(row['command']==expected_command(folder,mode),'exact-case-command-source-app-mode-inputs',dict(folder=str(folder),mode=mode,command=row['command']))
    launch = read(folder/(mode+'.launch.json'),True)
    check(set(launch)=={'mode','command','startedAtUtc','actualExitCode','pid'},'launch-schema',mode)
    check(launch['mode']==mode and launch['command']==row['command'] and launch['pid']==row['pid']
      and launch['startedAtUtc']==row['startedAtUtc'] and launch['actualExitCode'] is None,'launch-row-linkage',mode)
    if row['actualExitCode'] is None:
        check(pending_allowed,'unexpected-pending-row',mode)
        check(not (folder/(mode+'.exit.txt')).exists() and not (folder/(mode+'.result.json')).exists(),'pending-row-no-exit-or-result',mode)
        return False
    check(row['actualExitCode']==expected_exit,'actual-case-exit',mode)
    check(read(folder/(mode+'.result.json'),True)==row,'sidecar-aggregate-row',mode)
    check((folder/(mode+'.exit.txt')).read_text().strip()==str(expected_exit),'exit-file-value',mode)
    fact(folder/(mode+'.exit.txt'),True)
    for key in ['stdout','stderr']:
        check(Path(row[key]['path'])==folder/(mode+'.'+key+'.log'),'log-scope',mode+'/'+key)
        verify(row[key],True)
    actual = json.loads(Path(row['stdout']['path']).read_text(encoding='utf-8-sig').strip().splitlines()[-1])
    check(actual==row['actual'] and actual['mode']==mode and type(actual['checks']) is int and actual['checks']>0
      and (expected_checks is None or actual['checks']==expected_checks),'actual-summary-exact-schema-and-count',mode)
    if expected_exit==0:
        check(actual['gameExecuted'] is False and actual['productionAdmission'] is False,'actual-offline-scope',mode)
        check(row['passed'] is True and actual['passed'] is True,'actual-row-pass',mode)
    else:
        check(row['passed'] is False and actual['passed'] is False and 'Another prepared playback clock change owns this baseline.' in actual['failure'],'retained-sdk-failure',mode)
    if folder==NEW and expected_exit==0:
        expected = dict(actualPrepare=20,actualCommit=20,sdkExecuted=True,preparedCurrentTransactionPreserved=True,
          knownWithdrawalRetired=False,retainedRoots=0,fullGenerationCompleted=False)
        check(all(actual.get(k)==v for k,v in expected.items()),'new-sdk-success-guards',actual)
    return True

def build(folder,result,ref=None):
    row = result['rows'][0]
    check(row['mode']=='build' and row['actualExitCode']==0 and row['passed'] is True,'fresh-build-actual-exit',str(folder))
    check(read(folder/'build.result.json',True)==row,'build-result-row',str(folder))
    launch = read(folder/'build.launch.json',True)
    check(all(launch.get(k)==row.get(k) for k in ['mode','pid','command','startedAtUtc']) and launch['actualExitCode'] is None,'build-launch-row',str(folder))
    check((folder/'build.exit.txt').read_text().strip()=='0','build-exit-file',str(folder));fact(folder/'build.exit.txt',True)
    for key in ['stdout','stderr']:
        check(Path(row[key]['path'])==folder/('build.'+key+'.log'),'build-log-scope',str(folder));verify(row[key],True)
    output=Path(row['stdout']['path']).read_text(encoding='utf-8-sig')
    check('0 Warning(s)' in output and '0 Error(s)' in output,'clean-build-summary',str(folder))
    project=folder/'source/tests/RizToolbox.PlaybackStartInitialization.Tests/RizToolbox.PlaybackStartInitialization.Tests.csproj'
    expected=['C:/Program Files/dotnet/dotnet.exe','build',str(project),'-c','Release','-m:1','--configfile',str(folder/'NuGet.Config'),'-p:UseSharedCompilation=false','--nologo','-v:minimal']
    check(row['command']==expected,'exact-fresh-build-project-command',str(folder))
    path=folder/'build-products.json'
    if ref:
        check(Path(ref['path'])==path,'build-products-manifest-reference',str(folder));verify(ref,True)
    products=read(path,True)
    inventory={p for p in app(folder).parent.iterdir() if p.is_file()}
    check({Path(r['path']) for r in products}==inventory and len(products)==len(inventory),'full-build-products-inventory',str(folder))
    check(app(folder) in inventory,'case-app-in-fresh-products',str(folder))
    for product in products:
        verify(product,True)
    return dict(actualExitCode=0,products=len(products),app=fact(app(folder),True))

def review(preflight):
    result=dict(kind='independent-read-only-sdk-disposal-collector-review-r1',startedAtUtc=now(),phase='preflight' if preflight else 'final',
      autoCoverageClosed=False,newSdkCaseAccepted=False,testsStarted=0,buildsStarted=0,collectorsStarted=0,
      sourceWrites=False,protectedPackagesWritten=False,processesKilled=False,processesRestarted=False,
      mainApplied=False,fullIncrementAcceptanceComplete=False,productionAdmission=False)
    for name in SCRIPT_NAMES:
        raw=(U/name).read_bytes();ast.parse(raw,filename=name);compile(raw,name,'exec')
        script_fact=fact(U/name,True)
        copy=REVIEW/'script-snapshots'/name
        if not copy.exists():
            copy.parent.mkdir(exist_ok=True);copy.write_bytes(raw)
        check(fact(copy)['sha256']==script_fact['sha256'],'script-remains-pinned-to-review-copy',name)
    original=read(OLD/'test-result.json',True)
    retry=read(NEW/'test-result.json',False)
    observer=read(OBS/'observer-result.json',False)
    check(original['passed'] is False and bool(original['finishedAtUtc']),'original-failed-aggregate-terminal',None)
    check(retry['runnerPid']==8304 and retry['parentPid']==10632 and retry['inJob'] is False and len(retry['rows'])==2,'fresh-driver-row-shape',None)
    check(observer['runnerPid']==40604 and observer['parentPid']==10632 and observer['inJob'] is False,'original-observer-identity-record',None)
    verify(observer['collector'],True)
    check(observer['collector']==fact(U/'Collect-ResourceSdkDisposalR1.py',True),'collector-observer-pin',None)
    native_manifest=U/'functional-inputs-r2.json'
    check(Path(retry['nativeInputManifest']['path'])==native_manifest,'native-input-origin-manifest-reference',None)
    verify(retry['nativeInputManifest'],True)
    input_origins=read(native_manifest,True)['files']
    check(len(input_origins)==4 and [Path(r['path']).name for r in input_origins]==[n for n,_ in INPUTS],'ordered-original-input-manifest',None)
    for row,(_,expected_hash) in zip(input_origins,INPUTS):
        check(row['sha256']==expected_hash,'fixed-native-helper-hash',row['path']);verify(row,True)
    native_inputs(OLD,original);native_inputs(NEW,retry)
    modes_manifest=read(U/'functional-modes-r2.json',True)
    check(len(modes_manifest['modes'])==12 and set(modes_manifest['modes'])==MODES,'exact-twelve-mode-manifest',modes_manifest['modes'])
    old_manifest,old_index=source_manifest(OLD,original['sourceManifest'])
    new_manifest,new_index=source_manifest(NEW,retry['sourceManifest'])
    r7_manifest,r7_index=source_manifest(R7)
    check(set(old_index)==set(new_index)==set(r7_index),'all-three-source-paths',None)
    delta4=sorted(p for p in old_index if old_index[p]['sha256']!=new_index[p]['sha256'])
    delta1=sorted(p for p in r7_index if r7_index[p]['sha256']!=new_index[p]['sha256'])
    check(set(delta4)==DELTA3|{REL} and delta1==[REL],'exact-test-deltas',dict(functionalR2=delta4,foundationR7=delta1))
    managed=[p for p in old_index if p.startswith('src/')];native=[p for p in old_index if p.startswith('native/')]
    check(len(managed)==484 and len(native)==48,'product-native-input-counts',dict(managed=len(managed),native=len(native)))
    check(all(old_index[p]['sha256']==new_index[p]['sha256']==r7_index[p]['sha256'] for p in managed+native),'all-products-native-source-equivalent',None)
    live=U/'source'
    check(file_inventory(live)==set(r7_index),'candidate-source-inventory',None)
    for rel,row in r7_index.items():
        actual=fact(live/rel)
        check(actual['sha256']==row['sha256'] and actual['bytes']==row['bytes'],'candidate-source-unchanged-from-r7',rel)
    proof_path=NEW/'basis/test-only-delta.json'
    check(Path(retry['testOnlyDelta']['path'])==proof_path,'test-delta-manifest-reference',None);verify(retry['testOnlyDelta'],True)
    proof=read(proof_path,True)
    for key in ['origin','functionalR2','originalFailedCase','patch']:
        verify(proof[key],True)
    check(Path(proof['origin']['path'])==R7/'source-manifest.json' and Path(proof['functionalR2']['path'])==OLD/'source-manifest.json'
      and Path(proof['originalFailedCase']['path'])==OLD/'resource-sdk-current.result.json' and Path(proof['patch']['path'])==NEW/'basis/prepared-disposal-only.patch','proof-path-bindings',None)
    check(proof['originDeltaPaths']==[REL] and proof['functionalR2DeltaPaths']==delta4
      and all(proof[k] is True for k in ['passed','allManaged484AndNative48InputsUnchanged','candidateSourceUnmodified','onlyExplicitDisposalAdded','existingAssertionsUnmodified']),'test-proof-exact-fields',None)
    before=(OLD/'source'/REL).read_bytes();after=(NEW/'source'/REL).read_bytes()
    check(before==(R7/'source'/REL).read_bytes()==(live/REL).read_bytes(),'candidate-r7-original-lifecycle-unchanged',None)
    needle=b'                    sdk.Speed(1.25);';newline=b'\r\n' if b'\r\n' in before else b'\n'
    insertion=newline.join([b'                    // Confirmation retains rollback ownership until this prepared transaction is disposed.',
      b'                    // Release that reservation before starting the next independent SDK transaction.',b'                    prepared.Dispose();',needle])
    check(before.count(needle)==1 and after==before.replace(needle,insertion),'byte-exact-dispose-and-two-comments-only',None)
    check((NEW/'basis/Lifecycle.before.cs').read_bytes()==before and (NEW/'basis/Lifecycle.after.cs').read_bytes()==after,'basis-before-after-source-binding',None)
    fact(NEW/'basis/Lifecycle.before.cs',True);fact(NEW/'basis/Lifecycle.after.cs',True)
    patch=''.join(difflib.unified_diff(before.decode('utf-8-sig').splitlines(True),after.decode('utf-8-sig').splitlines(True),'r2/'+REL,'sdk-disposal/'+REL))
    check((NEW/'basis/prepared-disposal-only.patch').read_bytes()==patch.replace('\n',os.linesep).encode('utf-8'),'patch-exact-source-diff',None)
    old_build=build(OLD,original)
    new_build=build(NEW,retry,retry['buildProducts'])
    selected=[];failed=[]
    for row in original['rows'][1:]:
        if row['mode']=='resource-sdk-current':
            case_row(OLD,row,34,1);failed.append(row)
        else:
            check(row['mode'] in OLD_CHECKS,'known-original-success-mode',row['mode'])
            case_row(OLD,row,OLD_CHECKS[row['mode']]);selected.append(row)
    check(len(selected)==11 and len(failed)==1 and sum(r['actual']['checks'] for r in selected)==237,'eleven-original-successes-and-retained-failure',None)
    sdk=retry['rows'][1]
    check(sdk['mode']=='resource-sdk-current','new-sdk-only-mode',None)
    sdk_terminal=case_row(NEW,sdk,None,0,pending_allowed=preflight)
    sdk_actual_checks=sdk.get('actual',{}).get('checks')
    if not sdk_terminal:
        check(retry['passed'] is False and not retry.get('finishedAtUtc'),'pending-not-prematurely-accepted',None)
        check(observer['state']=='waiting' and observer['passed'] is False and observer['collectorExecuted'] is False,'original-observer-pending-not-collected',None)
    snapshots=read(REVIEW/'process-snapshot.json')
    processes={p['pid']:p for p in snapshots['processes']}
    identities=[]
    for pid,parent,name,created,command in [
      (8304,10632,'pythonw.exe','2026-11-03T00:50:14.660054+00:00','"C:\\Python314\\pythonw.exe" "'+str(U/SCRIPT_NAMES[0])+'"'),
      (10880,8304,'dotnet.exe','2026-11-03T00:50:44.166951+00:00',subprocess.list2cmdline(expected_command(NEW,'resource-sdk-current'))),
      (40604,10632,'pythonw.exe','2026-11-03T00:59:32.335920+00:00','"C:\\Python314\\pythonw.exe" "'+str(U/SCRIPT_NAMES[2])+'"')]:
        p=processes.get(pid)
        ok=bool(p and p['parentPid']==parent and p['name']==name and datetime.fromisoformat(p['createdUtc'])==datetime.fromisoformat(created) and p['command']==command)
        check(ok,'recorded-exact-live-process-identity',pid)
        identities.append(dict(pid=pid,identityVerified=ok,snapshotUtc=snapshots['observedUtc'],process=p))
    result.update(exactInvocationEvidence=identities,oldBuild=old_build,newBuild=new_build,oldSuccessfulCases=11,oldSuccessfulChecks=237,
      oldFailedSdkExitCode=1,oldFailedSdkChecks=34,newSdkExpectedChecksFromSource=38,newSdkExpectedChecksBasis='Source-derived expectation only: previous check33, then successful Need+two SDK Speed assertions+continuation+root-release assertions. Actual remains pending until parsed from its real log.',newSdkActualPending=not sdk_terminal,newSdkActualExitCode=sdk['actualExitCode'],newSdkActualChecks=sdk_actual_checks,
      newSdkTerminal=sdk_terminal,observerState=observer['state'],sourceFiles=913,managedProductInputs=484,nativeSourceInputs=48,orderedPhysicalInputs=4,
      sourceDeltaFromOld=delta4,sourceDeltaFromR7=delta1,lifecycleOriginal=fact(live/REL),lifecycleIndependentCorrection=fact(NEW/'source'/REL),
      scripts=[fact(U/n) for n in SCRIPT_NAMES],currentProofManifests=[fact(p) for p in [U/'functional-modes-r2.json',native_manifest,R7/'source-manifest.json',OLD/'source-manifest.json',NEW/'source-manifest.json',proof_path,NEW/'build-products.json',OLD/'build-products.json',NEW/'basis/prepared-disposal-only.patch']])
    baseline_path=REVIEW/'review-baseline.json'
    if preflight and not baseline_path.exists():
        baseline_path.write_text(json.dumps(dict(createdAtUtc=now(),fixedFacts=list(FIXED.values())),indent=2)+'\n',encoding='utf-8')
    elif baseline_path.exists():
        baseline=read(baseline_path)
        for row in baseline['fixedFacts']:
            verify(row)
    else:
        check(False,'preflight-baseline-required-before-final',None)
    if not preflight:
        check(observer['state']=='collected' and observer['passed'] is True and observer['collectorExecuted'] is True and observer['collectorActualExitCode']==0 and bool(observer['finishedAtUtc']),'original-observer-real-collected-terminal',observer['state'])
        verify(observer['sdkResult']);check(Path(observer['sdkResult']['path'])==NEW/'test-result.json','observer-sdk-result-binding',None)
        for key in ['collectorStdout','collectorStderr','closure']:
            verify(observer[key])
        check(Path(observer['closure']['path'])==COMBINED/'test-result.json','observer-combined-closure-binding',None)
        check(retry['passed'] is True and retry['finishedAtUtc'] and retry['checks']==sdk_actual_checks and retry['actualCases']==1
          and retry['frozenSourcesUnchanged'] is True and retry['candidateSourcesUnchanged'] is True,'new-sdk-driver-final-guards',None)
        check((NEW/'driver.exit.txt').read_text().strip()=='0','new-sdk-driver-recorded-exit',None);fact(NEW/'driver.exit.txt')
        combined=read(COMBINED/'test-result.json')
        actual_combined_checks=237+sdk_actual_checks
        check(combined['passed'] is True and combined['processes']==12 and combined['checks']==actual_combined_checks and combined['functionalSubscopeComplete'] is True,'combined-exact-functional-totals',None)
        for key in ['fullIncrementAcceptanceComplete','mainApplied','productionAdmission','gameExecuted','installed','fullBCompleted']:
            check(combined[key] is False,'combined-non-admission-scope',key)
        sources=combined['sourceRuns']
        check(len(sources)==2,'combined-two-source-runs',len(sources))
        expected_sources=[dict(role='eleven-original-actual-successes',aggregate=fact(OLD/'test-result.json'),aggregatePassed=False,aggregateTerminal=True,successfulCases=11,checks=237,failedSdkCaseRetained=True),
          dict(role='one-test-disposal-corrected-actual-success',aggregate=fact(NEW/'test-result.json'),aggregatePassed=True,aggregateTerminal=True,successfulCases=1,checks=sdk_actual_checks)]
        check(sources==expected_sources,'combined-exact-two-source-attribution',sources)
        check(len(combined['rows'])==12 and {r['mode'] for r in combined['rows']}==MODES,'combined-unique-twelve-modes',None)
        expected_rows={r['mode']:(OLD,r) for r in selected};expected_rows['resource-sdk-current']=(NEW,sdk)
        for row in combined['rows']:
            folder,source_row=expected_rows[row['mode']]
            expected_row=dict(**source_row,sourceRun=str(folder),sourceResult=fact(folder/(row['mode']+'.result.json')))
            check(row==expected_row,'combined-row-exact-original-source-result',row['mode'])
        check(combined['testOnlyDeltaPaths']==delta4 and combined['testDelta']==fact(proof_path)
          and combined['originalFailedSdkCase']==fact(OLD/'resource-sdk-current.result.json')
          and combined['originalFailedAggregateRetainedUnchanged'] is True,'combined-failure-and-test-delta-linkage',None)
        check(combined['inputSourceFiles']==913 and combined['managedProductInputs']==484 and combined['nativeInputs']==48
          and combined['unchangedAllManagedAndNativeInputs'] is True,'combined-input-counts-and-equivalence',None)
        collector_stdout=json.loads(Path(observer['collectorStdout']['path']).read_text(encoding='utf-8-sig').strip().splitlines()[-1])
        check(collector_stdout==dict(passed=True,processes=12,checks=actual_combined_checks,output=str(COMBINED)),'collector-actual-stdout-totals-and-path',collector_stdout)
        for retained in combined['retained']:
            verify(retained['original']);verify(retained['retained'])
            check(retained['original']['sha256']==retained['retained']['sha256'],'retained-copy-fixity',retained['original']['path'])
            check(Path(retained['retained']['path'])==COMBINED/'retained'/Path(retained['original']['path']).relative_to(U),'retained-copy-scope',retained['retained']['path'])
        manifest=read(COMBINED/'hash-manifest.json')
        inventory={p for p in COMBINED.rglob('*') if p.is_file() and p!=COMBINED/'hash-manifest.json'}
        check({Path(r['path']) for r in manifest['files']}==inventory and len(manifest['files'])==len(inventory),'combined-hash-manifest-full-inventory',None)
        for row in manifest['files']:
            verify(row)
        result.update(newSdkCaseAccepted=not ISSUES,autoCoverageClosed=not ISSUES,combined=fact(COMBINED/'test-result.json'),combinedManifest=fact(COMBINED/'hash-manifest.json'),combinedChecks=actual_combined_checks)
    result.update(passed=not ISSUES,readOnlyIntegrityPassed=not ISSUES,issues=ISSUES,finishedAtUtc=now(),auditedFacts=sorted(FACTS.values(),key=lambda r:r['path']))
    return result

parser=argparse.ArgumentParser()
parser.add_argument('--preflight',action='store_true')
args=parser.parse_args()
try:
    outcome=review(args.preflight)
except Exception as exc:
    outcome=dict(kind='independent-read-only-sdk-disposal-collector-review-r1',phase='preflight' if args.preflight else 'final',
      passed=False,readOnlyIntegrityPassed=False,autoCoverageClosed=False,newSdkCaseAccepted=False,issues=ISSUES+[dict(code='review-exception',detail=repr(exc))],finishedAtUtc=now(),auditedFacts=list(FACTS.values()))
target=REVIEW/('preflight-review.json' if args.preflight else 'final-closure-review.json')
target.write_text(json.dumps(outcome,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps({k:v for k,v in outcome.items() if k not in ['auditedFacts']},ensure_ascii=False,indent=2))
raise SystemExit(0 if outcome['readOnlyIntegrityPassed'] else 1)
