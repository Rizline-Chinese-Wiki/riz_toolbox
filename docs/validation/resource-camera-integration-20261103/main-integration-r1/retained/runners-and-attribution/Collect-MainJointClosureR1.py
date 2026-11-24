"""Close this exact main integration, preserving failed aggregates and historical input epochs."""
from pathlib import Path
import datetime
import hashlib
import json
import zipfile

P = Path(__file__).resolve().parent
MAIN = Path('C:/Users/71957/Desktop/riz_toolbox')
U = Path('D:/riz-build-cache-next/playback-progress-resource-continuity-r1-20261102')
T = Path('D:/riz-build-cache-next/playback-stock-stop-occurrence-r1-20261102')
O = P / 'main-integration-closure-r1'
read = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
fact = lambda p: dict(path=str(p), sha256=sha(p), bytes=p.stat().st_size)
now = lambda: datetime.datetime.now(datetime.timezone.utc).isoformat()


def save(p, value):
    p.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def facts(value):
    if isinstance(value, dict):
        if isinstance(value.get('path'), str) and Path(value['path']).is_absolute() and isinstance(value.get('sha256'), str):
            yield value
        for child in value.values():
            yield from facts(child)
    elif isinstance(value, list):
        for child in value:
            yield from facts(child)


def main():
    assert not O.exists(), 'Keep closure evidence immutable.'
    before = {r['path']: r for r in read(P / 'main-before-manifest.json')['files']}
    current = {r['path']: r for r in read(P / 'joint-candidate-manifest.json')['files']}
    assert len(before) == 894 and len(current) == 915
    for rel, row in current.items():
        assert sha(MAIN / rel) == row['sha256']
    actual_delta = {rel for rel, row in current.items() if rel not in before or row['sha256'] != before[rel]['sha256']}
    expected_delta = {row['path'] for row in read(P / 'staged-deltas.json')['combined']}
    assert actual_delta == expected_delta and len(actual_delta) == 61
    assert sha(MAIN / 'README.md') == 'cfbac4691d36edc5daba513aa62d5856ae61e29bf553d0dc2979832750847364'
    resource = read(P / 'main-resource-application-r1/test-result.json')
    camera = read(P / 'main-camera-application-r1/test-result.json')
    assert resource['passed'] and resource['focusedChecks'] == 76 and resource['mainApplied']
    assert camera['passed'] and camera['mainSourceFiles'] == 915 and camera['mainApplied']
    original = read(P / 'main-joint-validation-r1/test-result.json')
    continuation = read(P / 'main-joint-validation-continuation-r1/test-result.json')
    chain = read(P / 'main-joint-build-chain-r1.json')
    symbols = read(P / 'symbols-main-joint-r2/test-result.json')
    symbol_process = read(P / 'main-joint-symbol-execution-r2/process-result.json')
    assert original['passed'] is False and 'FileNotFoundError' in original['failure']
    assert len(original['rows']) == 29 and all(row['passed'] and row['actualExitCode'] == 0 for row in original['rows'])
    assert continuation['passed'] and len(continuation['rows']) == 2 and all(row['actualExitCode'] == 0 for row in continuation['rows'])
    assert chain['passed'] and chain['managedChecks'] == 2620 and chain['sdkChecks'] == 14 and chain['releaseAssemblyCount'] == 6
    assert symbols['passed'] and symbols['pePdbPairs'] == 11 and symbols['documentCount'] == 991
    assert symbol_process['actualExitCode'] == 0
    assert original['sourceUnchanged'] and original['baselinesUnchanged'] and continuation['main915Unchanged'] and continuation['frozen915Unchanged']
    sdk = read(T / 'formal-sdk-combined-after-shutdown-r1/test-result.json')
    t_closed = read(T / 'integration-closure-after-shutdown-r1/test-result.json')
    u_closed = read(U / 'functional-combined-after-disposal-r1/test-result.json')
    assert sdk['passed'] and sdk['checks'] == 669 and sdk['processes'] == 18
    assert t_closed['passed'] and len(t_closed['completedScopes']) == 22
    assert u_closed['passed'] and u_closed['checks'] == 275 and u_closed['processes'] == 12
    collected = {}
    roots = [P / 'main-resource-application-r1/test-result.json', P / 'main-camera-application-r1/test-result.json',
             P / 'main-resource-application-r1/fresh-main-build-products.json',
             P / 'main-joint-validation-r1/test-result.json', P / 'main-joint-validation-r1/solution-products.json',
             P / 'main-joint-validation-continuation-r1/test-result.json', P / 'main-joint-validation-continuation-r1/release-products.json',
             P / 'symbols-main-joint-r2/test-result.json',
             T / 'formal-sdk-combined-after-shutdown-r1/test-result.json', T / 'integration-closure-after-shutdown-r1/test-result.json',
             Path(t_closed['verifiedArtifacts']['path']), U / 'functional-combined-after-disposal-r1/test-result.json',
             U / 'sdk-disposal-collector-review-r1/final-closure-review.json', U / 'sdk-disposal-collector-review-r1/review-hash-manifest.json',
             U / 'final-sdk-disposal-integration-r1/test-result.json']
    for root in roots:
        for row in facts(read(root)):
            collected[(row['path'], row['sha256'])] = row
    redirects = []
    old_u = U / 'source/tests/RizToolbox.PlaybackStartInitialization.Tests/ProgressClipResourceLifecycleChecks.cs'
    u_transition = read(U / 'final-sdk-disposal-integration-r1/historical-canonical-reference-redirect.json')
    old_u_copy = Path(u_transition['historicalBytesRetained']['path'])
    assert sha(old_u_copy) == 'f8fea816545050b81a328fdae0b3ed7dd4e1788a5e12465fd8dd69b038c78348'
    before_zip = P / 'main-before-894.zip'
    archive_fact = next(row for row in read(P / 'preparation-result.json')['archives'] if Path(row['path']) == before_zip)
    assert sha(before_zip) == archive_fact['sha256']
    with zipfile.ZipFile(before_zip) as backup:
        for row in collected.values():
            path = Path(row['path']).resolve()
            if path.is_file() and sha(path) == row['sha256'] and ('bytes' not in row or path.stat().st_size == row['bytes']):
                continue
            if path == old_u.resolve() and row['sha256'] == u_transition['oldCanonicalReference']['sha256']:
                assert sha(old_u_copy) == row['sha256']
                redirects.append(dict(reason='one-tested-U-canonical-disposal-transition', historical=row, retained=fact(old_u_copy), current=fact(path)))
                continue
            if path.is_relative_to(MAIN.resolve()):
                rel = path.relative_to(MAIN.resolve()).as_posix()
                assert rel in actual_delta and rel in before and row['sha256'] == before[rel]['sha256']
                data = backup.read(rel)
                assert hashlib.sha256(data).hexdigest() == row['sha256'] and ('bytes' not in row or len(data) == row['bytes'])
                assert sha(MAIN / rel) == current[rel]['sha256']
                redirects.append(dict(reason='precisely-journaled-main-894-to-915-transition', historical=row, archive=archive_fact, entry=rel, current=fact(MAIN / rel)))
                continue
            raise AssertionError('Unexpected changed evidence: ' + str(path))
    O.mkdir()
    save(O / 'verified-original-facts.json',dict(passed=True,count=len(collected),files=list(collected.values())))
    save(O / 'historical-input-transitions.json',dict(passed=True, count=len(redirects), redirects=redirects,
         exactMainDelta=sorted(actual_delta), mainBeforeArchive=archive_fact, beforeInputs=894,afterInputs=915,oldUCanonicalAllowedRedirects=1))
    zip_path = O / 'current-main-source-915.zip'
    with zipfile.ZipFile(zip_path,'x',compression=zipfile.ZIP_DEFLATED) as archive:
        for rel in sorted(current):
            archive.write(MAIN / rel,rel)
    with zipfile.ZipFile(zip_path) as archive:
        assert len(archive.namelist()) == 915
        for rel,row in current.items():
            assert hashlib.sha256(archive.read(rel)).hexdigest() == row['sha256']
    source_rows=[dict(path=rel,sha256=sha(MAIN / rel),bytes=(MAIN / rel).stat().st_size) for rel in sorted(current)]
    save(O / 'current-main-source-manifest.json',dict(source=str(MAIN),files=source_rows))
    result=dict(kind='exact-resource-camera-main-integration-offline-closure-r1',passed=True,finishedAtUtc=now(),
         mainApplied=True, mainSourceFiles=915,managedProductInputs=485,nativeInputs=48,
         resourceChanges=53,cameraChanges=9,combinedChanges=61,
         tSdkProcesses=18,tSdkChecks=669,tScopes=22,uFunctionalModes=12,uFunctionalChecks=275,
         freshResourceIntegrationChecks=76,freshManagedSuites=28,freshManagedChecks=2620,freshSdkChecks=14,
         freshSolutionErrors=0,freshSolutionWarnings=chain['solutionWarnings'],freshReleaseAssemblies=6,
         freshPePdbPairs=11,freshSourceDocuments=991,verifiedOriginalFacts=len(collected),historicalInputTransitions=len(redirects),
         lifecycleDisposePreserved=True,originalFailedAggregatePreserved=True,originalFailedAggregatePassed=False,
         originalSuccessfulChildren=29,continuationSuccessfulChildren=2,allSuccessfulChildActualExitCodesZero=True,
         currentIncrementOfflineValidationComplete=True,artifactIntegrityPassed=True,
         sourceManifest=fact(O / 'current-main-source-manifest.json'),sourceArchive=fact(zip_path),
         appliedResource=fact(P / 'main-resource-application-r1/test-result.json'),appliedCamera=fact(P / 'main-camera-application-r1/test-result.json'),
         explicitBuildAndTestChain=fact(P / 'main-joint-build-chain-r1.json'),symbols=fact(P / 'symbols-main-joint-r2/test-result.json'),
         historicalTransitionProof=fact(O / 'historical-input-transitions.json'),
         verifiedFacts=fact(O / 'verified-original-facts.json'),
         sourceRuns=[dict(role='original-fresh-main-solution-and-28-successes-before-path-error',result=fact(P / 'main-joint-validation-r1/test-result.json'),aggregatePassed=False),
                     dict(role='actual-remaining-sdk14-and-six-release',result=fact(P / 'main-joint-validation-continuation-r1/test-result.json'),aggregatePassed=True)],
         gameExecuted=False,installed=False,productionAdmission=False,nativeSpeedSeekEnabled=False,
         fullGenerationCompleted=False,unknownLowerCompleted=False,fullBCompleted=False)
    save(O / 'test-result.json',result)
    save(O / 'hash-manifest.json',dict(passed=True,files=[fact(p) for p in sorted(O.iterdir()) if p.is_file()]))
    print(json.dumps(dict(passed=True,mainSourceFiles=915,verifiedOriginalFacts=len(collected),transitions=len(redirects),mainApplied=True,fullBCompleted=False)))


if __name__ == '__main__':
    main()
