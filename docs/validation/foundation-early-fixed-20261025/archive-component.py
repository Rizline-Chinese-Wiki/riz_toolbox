"""Archive one root-observed completed early-fixed integration run."""
from pathlib import Path
from datetime import datetime, timezone
import hashlib
import json
import shutil
import zipfile

batch = Path(__file__).resolve().parent
repo = batch.parents[2]

def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def write(name, value):
    (batch/name).write_text(json.dumps(value, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')

run = read(batch/'component-run.json')
wrapper = read(batch/'component.log.run.json')
assert run['sessionId'] > 0 and run['exitCode'] == run['terminalExitCodeObserved'] == wrapper['exitCode'] == 0
assert run['rootSoleBuilder'] and run['finalCandidate']
assert run['buildLogSha256'] == wrapper['buildLogSha256'] == sha(batch/'build.log')
assert run['testLogSha256'] == wrapper['testLogSha256'] == sha(batch/'test.log')
build, cases = Path(wrapper['buildDirectory']), Path(wrapper['casesDirectory'])
inputs = read(batch/'component-inputs-before.json')['files']
assert len(inputs) == 14 and len({row['path'] for row in inputs}) == 14
assert all(sha(repo/row['path']) == row['sha256'] for row in inputs)
compiled = read(build/'fixture-build-manifest.json')
assert len(compiled['sources']) == 11
assert all(sha(Path(row['path'])) == row['sha256'] for row in compiled['sources'])
assert all(any((repo/item['path']).resolve() == Path(row['path']).resolve() and item['sha256'] == row['sha256'] for item in inputs) for row in compiled['sources'])
evidence = read(cases/'fixture-result.json')
assert evidence['passed'] and evidence['verifiedCases'] == evidence['expectedCases'] == evidence['independentProcesses'] == 73
assert evidence['allInputHashesUnchanged'] and evidence['baselineFirst'] and evidence['debuggerSupplementIncluded']
assert all(row['beforeSha256'] == row['afterSha256'] == sha(Path(row['path'])) for row in evidence['files'])
assert not any(evidence[key] for key in ('realSystemWinHttpVerified','gameFirstInstallVerified','productionV4Issued',
    'allTransitiveLoaderDependenciesSafeVerified','schedulerClosureVerified','engineCancellationCompleted','installed','gameCodeExecuted'))
expected_names = {'mode-'+str(i) for i in range(16)} | {'fault-'+str(s)+'-'+str(k) for s in range(1,15) for k in range(4)} | {'normal-debugger'}
assert {row['name'] for row in evidence['processes']} == expected_names
assert all(row['started'] and not row['timedOut'] and row['exactOwnedProcess'] for row in evidence['processes'])
assert all(row['passed'] and row['checks'] > 0 for row in evidence['caseResults'])
assert len(compiled['files']) == len(evidence['files']) == 7
raw = batch/'raw'
raw.mkdir(exist_ok=True)
shutil.copytree(build, raw/'build', dirs_exist_ok=True)
shutil.copytree(cases, raw/'cases', dirs_exist_ok=True)
for record in evidence['processes']:
    name = record['name']
    stored = read(raw/'cases'/name/'process.json')
    assert stored == record
    assert (raw/'cases'/name/'mapping-initial.bin').stat().st_size == (raw/'cases'/name/'mapping-final.bin').stat().st_size == 449120
    assert read(raw/'cases'/name/'result.json')['passed']
write('source-manifest.json', dict(files=inputs, scope='Fourteen declared integration inputs including the frozen transitive fixed verifier; toolchain/SDK not archived.'))
binaries = [dict(path=row['name'], sha256=row['sha256']) for row in compiled['files']]
write('binary-manifest.json', dict(files=binaries, scope='Seven owned fixture binaries including two exact reused DLLs; not a game package.'))
for manifest_name, archive_name, proof_name, base in (
    ('source-manifest.json','source-inputs.zip','source-archive-check.json',repo),
    ('binary-manifest.json','fixture-binaries.zip','binary-archive-check.json',raw/'build')):
    manifest = batch/manifest_name
    rows = read(manifest)['files']
    destination = batch/archive_name
    if not destination.exists():
        with zipfile.ZipFile(destination, 'w', compression=zipfile.ZIP_DEFLATED) as archive:
            for row in rows:
                archive.write(base/row['path'], row['path'])
    with zipfile.ZipFile(destination) as archive:
        assert archive.testzip() is None and set(archive.namelist()) == {row['path'] for row in rows}
        assert all(hashlib.sha256(archive.read(row['path'])).hexdigest() == row['sha256'] == sha(base/row['path']) for row in rows)
    write(proof_name, dict(entries=len(rows), archive=archive_name, archiveSha256=sha(destination), manifestSha256=sha(manifest), allEntryHashesMatched=True))
write('component-input-integrity-check.json', dict(checked=14, preBuildMatchesArchived=True, currentChanged=[]))
write('early-fixed-evidence.json', evidence)
summary = dict(completedAtUtc=datetime.now(timezone.utc).isoformat(), sessionId=run['sessionId'], componentSources=14,
    binaryFiles=7, independentProcesses=73, verifiedCases=73, independentAssertions=evidence['checks'],
    modes=16, internalFaultCases=56, debuggerCases=1, baselineFirst=True,
    physicalMemorySnapshots=73, previousFullMilestone='foundation-early-loader-20261025/session99557/25 suites1617',
    reusedPublisherSha256=compiled['reusedPublisherSha256'], reusedHelperSha256=compiled['reusedHelperSha256'],
    dobbyImported=False, fullManagedSuitesRerun=False, realSystemWinHttpVerified=False,
    gameFirstInstallVerified=False, productionV4Issued=False, engineCancellationCompleted=False, gameExecuted=False, installed=False)
write('build-summary.json', summary)
print(json.dumps(summary))
