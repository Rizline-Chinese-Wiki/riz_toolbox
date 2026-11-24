"""Archive the root-observed full build and exact declared source/package sets."""
from pathlib import Path
from datetime import datetime, timezone
import hashlib
import json
import shutil
import zipfile

batch=Path(__file__).resolve().parent
repo=batch.parents[2]
def read(path): return json.loads(path.read_text(encoding='utf-8-sig'))
def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def write(name,value): (batch/name).write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
run=read(batch/'build-run.json'); wrapper=read(batch/'build.log.run.json')
assert run['sessionId']==23528 and run['exitCode']==run['terminalExitCodeObserved']==wrapper['exitCode']==0
assert run['logSha256']==wrapper['logSha256']==sha(batch/'build.log')
summary=read(batch/'build-summary.json')
assert summary['projects']==25 and summary['passed']==summary['total']==1617
inputs=read(batch/'build-inputs-before.json')['files']; rows=read(batch/'source-manifest.json')['files']
assert inputs==rows and len(rows)==599 and all(sha(repo/row['path'])==row['sha256'] for row in rows)
package=read(batch/'package-manifest.json')['files']
assert len(package)==23 and len({row['path'] for row in package})==23
package_root=repo/'artifacts/package'
assert all(sha(package_root/row['path'])==row['sha256'] for row in package)
for archive_name,manifest_name,proof_name,source in (
    ('source-inputs.zip','source-manifest.json','source-archive-check.json',repo),
    ('staged-package.zip','package-manifest.json','package-archive-check.json',package_root)):
    manifest=batch/manifest_name; entries=read(manifest)['files']; destination=batch/archive_name
    if not destination.exists():
        with zipfile.ZipFile(destination,'w',compression=zipfile.ZIP_DEFLATED) as archive:
            for row in entries: archive.write(source/row['path'],row['path'])
    with zipfile.ZipFile(destination) as archive:
        assert archive.testzip() is None and set(archive.namelist())=={row['path'] for row in entries}
        assert all(hashlib.sha256(archive.read(row['path'])).hexdigest()==row['sha256']==sha(source/row['path']) for row in entries)
    write(proof_name,dict(verifiedAtUtc=datetime.now(timezone.utc).isoformat(),entries=len(entries),
        archive=archive_name,archiveSha256=sha(destination),manifestSha256=sha(manifest),allEntryHashesMatched=True,
        installed=False,scope='Exact declared files only; toolchains, SDKs and system runtime remain external prerequisites.'))

kernel=read(batch/'engine-kernel-evidence.json'); assert kernel['passed'] and kernel['actualOriginalCalls']==180
supplied=batch/'native-build/engine-node-kernel-supplied-files'; supplied.mkdir(exist_ok=True)
supplied_manifest=[]
for row in kernel['files']:
    if row['role'] in ('supplied-managed-output-file','native-regression-fixture','native-managed-regression-fixture','fixture-only-dobby'):
        original=Path(row['path']); assert row['beforeSha256']==row['afterSha256']
        during_run=original
        if sha(original)!=row['afterSha256']:
            assert original.name=='RizToolbox.JudgeContext.dll'
            assert row['role']=='supplied-managed-output-file'
            during_run=batch.parent/'foundation-early-fixed-20261025/raw/build/RizToolbox.JudgeContext.dll'
            assert sha(during_run)==row['afterSha256']
        destination=supplied/original.name
        if destination.exists(): assert sha(destination)==row['afterSha256']
        else: shutil.copy2(during_run,destination)
        supplied_manifest.append(dict(originalPath=row['path'],path=destination.relative_to(batch).as_posix(),sha256=sha(destination),role=row['role'],recoveredExactSource=str(during_run)))
write('kernel-supplied-files.json',dict(files=supplied_manifest,scope='Supplied test binaries/managed tree and fixture-only Dobby; Windows/.NET installation not archived.'))

# Preserve the real preliminary terminals and source snapshots, not just green
# final records. The old-header-name sample remains an accurate historical file.
history=batch/'history'
for name,source in (
    ('kernel-only-first',Path('D:/RizToolbox-build-cache/engine-kernel-compile-first-20261025')),
    ('entry-first',Path('D:/RizToolbox-build-cache/engine-kernel-entry-first-20261025')),
    ('runner-first-build',Path('D:/RizToolbox-build-cache/engine-kernel-runner-first-20261025')),
    ('runner-first-cases',Path('D:/RizToolbox-build-cache/engine-kernel-cases-first-20261025'))):
    shutil.copytree(source,history/name,dirs_exist_ok=True)
write('history/observed-runs.json',dict(runs=[
    dict(sessionId=11782,exitCode=0,scope='Kernel-only compilation; no entry fixture or calls.'),
    dict(sessionId=73213,exitCode=0,scope='Kernel plus initial descriptor fixture compilation.'),
    dict(exitCode=0,scope='Initial descriptor child returned0/1606; no yielded session handle. Historical preferredBase key actually described mapped header.'),
    dict(sessionId=96386,exitCode=0,scope='Final descriptor schema plus native136 and five managed modes, seven owned processes.'),
    dict(sessionId=23528,exitCode=0,scope='Final full build,25 managed suites1617 plus old native paths and seven new Kernel processes.')],
    scope='Actual terminals observed by root; no invented failed feature test or game execution.'))

checks=dict(sessionId=23528,projects=25,passed=1617,total=1617,declaredSourceInputs=599,packageFiles=23,
    kernelBinaryBytes=(batch/'native-build/engine-node-kernel/RizToolbox.EngineNodeKernel.dll').stat().st_size,
    kernelIndependentProcesses=7,kernelEntryChecks=1606,kernelEntryParentAssertions=1486,
    kernelNativeChecks=3402,kernelManagedChecks=889,kernelActualOriginalCalls=180,
    kernelObserverPairs=kernel['nativeRegression']['entered']+kernel['managedRegression']['physicalCaptures'],
    kernelEntryTables=4,kernelEntryPointers=128,kernelEntryRejections=256,
    legacyEarlyLoaderProcesses=39,legacyEarlyLoaderAssertions=7316,
    compilerDiagnosticLines=len(summary['compilerDiagnosticLines']),
    productionKernelAdopted=False,kernelEarlyLoaderChainVerified=False,productionV4Issued=False,
    engineCancellationCompleted=False,installed=False,gameExecuted=False)
write('milestone-summary.json',checks)
print(json.dumps(checks))
