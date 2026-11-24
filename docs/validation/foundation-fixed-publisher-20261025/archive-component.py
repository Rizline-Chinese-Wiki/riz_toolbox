"""Verify and archive the observed standalone component, never a game install."""
from pathlib import Path
from datetime import datetime, timezone
import hashlib, json, shutil, zipfile
batch=Path(__file__).resolve().parent
repo=batch.parents[2]
build=Path('D:/RizToolbox-build-cache/fixed-publisher-final-20261025')
cases=Path('D:/RizToolbox-build-cache/fixed-publisher-cases-final-20261025')
def read(path):return json.loads(path.read_text(encoding='utf-8-sig'))
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def write(name,value):(batch/name).write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
run=read(batch/'component-run.json');wrapper=read(batch/'component.log.run.json')
assert run['sessionId']==5523 and run['exitCode']==run['terminalExitCodeObserved']==wrapper['exitCode']==0
assert run['buildLogSha256']==wrapper['buildLogSha256']==sha(batch/'build.log')
assert run['testLogSha256']==wrapper['testLogSha256']==sha(batch/'test.log')
inputs=read(batch/'component-inputs-before.json')['files'];assert len(inputs)==12
assert all(sha(repo/row['path'])==row['sha256'] for row in inputs)
compiled=read(build/'fixture-build-manifest.json')
assert all(sha(Path(row['path']))==row['sha256'] for row in compiled['sources'])
evidence=read(cases/'fixture-result.json')
assert evidence['passed'] and evidence['verifiedCases']==evidence['expectedCases']==57 and evidence['allInputHashesUnchanged']
assert all(row['beforeSha256']==row['afterSha256']==sha(Path(row['path'])) for row in evidence['files'])
assert not any(evidence[key] for key in ['gameExecuted','productionFirstInstallVerified','loaderStageVerified','engineCancellationCompleted','installed'])
assert all(row['process']['exitCode']==0 and not row['process']['timedOut'] and row['host']['passed'] for row in evidence['caseResults'])
assert len(compiled['files'])==4
raw=batch/'raw';raw.mkdir(exist_ok=True)
shutil.copytree(build,raw/'build',dirs_exist_ok=True)
shutil.copytree(cases,raw/'cases',dirs_exist_ok=True)
write('source-manifest.json',dict(files=inputs,scope='Twelve declared standalone component inputs only; external toolchain/SDK not archived.'))
binaries=[dict(path=row['name'],sha256=row['sha256']) for row in compiled['files']]
write('binary-manifest.json',dict(files=binaries,scope='Owned fixture, target, publisher and reused exact helper DLL only; not a game package.'))
for manifest_name,archive_name,proof_name,base in [('source-manifest.json','source-inputs.zip','source-archive-check.json',repo),
        ('binary-manifest.json','fixture-binaries.zip','binary-archive-check.json',raw/'build')]:
    manifest=batch/manifest_name;rows=read(manifest)['files'];destination=batch/archive_name
    if not destination.exists():
        with zipfile.ZipFile(destination,'w',compression=zipfile.ZIP_DEFLATED) as archive:
            for row in rows:archive.write(base/row['path'],row['path'])
    with zipfile.ZipFile(destination) as archive:
        assert archive.testzip() is None and set(archive.namelist())=={row['path'] for row in rows}
        assert all(hashlib.sha256(archive.read(row['path'])).hexdigest()==row['sha256']==sha(base/row['path']) for row in rows)
    write(proof_name,dict(entries=len(rows),archive=archive_name,archiveSha256=sha(destination),manifestSha256=sha(manifest),allEntryHashesMatched=True))
write('component-input-integrity-check.json',dict(checked=12,preBuildMatchesArchived=True,currentChanged=[]))
normal=evidence['caseResults'][0]['host']
assert normal['actualOriginalCalls']==34 and normal['observerEnters']==normal['observerExits']==20 and normal['originalExceptionsCaught']==16
call_proof=read(cases/'normal/calls-proof.json')
summary=dict(sessionId=5523,componentSources=12,binaryFiles=4,independentProcesses=57,verifiedCases=57,
    nativeHostChecks=sum(row['host']['checks'] for row in evidence['caseResults']),
    independentReceiptAssertions=sum(row['receipt']['checks'] for row in evidence['caseResults']),
    controlledUnwindCases=sum(row['host']['controlledUnwindCases'] for row in evidence['caseResults']),
    independentUnwindAssertions=sum(row['unwind']['checks'] for row in evidence['caseResults']),
    independentCallAssertions=call_proof['checks'],actualOriginalCalls=34,observerPairs=20,originalExceptionsCaught=16,
    physicalMemorySnapshots=57,previousFullMilestone='foundation-early-loader-20261025/session99557/25 suites1617',
    fullManagedSuitesRerun=False,loaderStageVerified=False,productionFirstInstallVerified=False,gameExecuted=False,installed=False)
write('build-summary.json',summary)
write('fixed-publisher-evidence.json',evidence)
print(json.dumps(summary))
