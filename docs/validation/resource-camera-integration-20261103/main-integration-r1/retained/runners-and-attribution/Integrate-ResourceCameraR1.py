"""Verify completed evidence, apply exact source deltas, then validate fresh main snapshots."""
from pathlib import Path
import argparse
import ctypes
from ctypes import wintypes
import datetime
import hashlib
import json
import os
import re
import shutil
import subprocess
import traceback

P = Path(__file__).resolve().parent
MAIN = Path('C:/Users/71957/Desktop/riz_toolbox')
U = Path('D:/riz-build-cache-next/playback-progress-resource-continuity-r1-20261102')
V = Path('D:/riz-build-cache-next/game-camera-scale-x-r1-20261103')
T = Path('D:/riz-build-cache-next/playback-stock-stop-occurrence-r1-20261102')
DOTNET = Path('C:/Program Files/dotnet/dotnet.exe')
FEED = U / 'foundation-r7/environment/local-feed'
LIFE = 'tests/RizToolbox.PlaybackStartInitialization.Tests/ProgressClipResourceLifecycleChecks.cs'
README = 'cfbac4691d36edc5daba513aa62d5856ae61e29bf553d0dc2979832750847364'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
read = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))
fact = lambda p: dict(path=str(p), sha256=sha(p), bytes=p.stat().st_size)
now = lambda: datetime.datetime.now(datetime.timezone.utc).isoformat()


class PendingGate(Exception):
    pass


def save(p, value):
    p.parent.mkdir(parents=True, exist_ok=True)
    temporary = p.with_suffix(p.suffix + '.writing')
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    temporary.replace(p)


def source(root, rows):
    for row in rows:
        path = (root / row['path']).resolve()
        assert path.is_relative_to(root.resolve()) and path.is_file()
        assert sha(path) == row['sha256'] and path.stat().st_size == row['bytes'], str(path)


def file_facts(value):
    if isinstance(value, dict):
        if isinstance(value.get('path'), str) and Path(value['path']).is_absolute() and isinstance(value.get('sha256'), str):
            yield value
        for child in value.values():
            yield from file_facts(child)
    elif isinstance(value, list):
        for child in value:
            yield from file_facts(child)


def admission():
    paths = [T / 'formal-sdk-combined-after-shutdown-r1/test-result.json',
             T / 'integration-closure-after-shutdown-r1/test-result.json',
             U / 'functional-combined-after-disposal-r1/test-result.json',
             U / 'sdk-disposal-collector-review-r1/final-closure-review.json',
             T / 'closure-after-shutdown-observer-r1/observer-result.json']
    missing = [str(p) for p in paths if not p.is_file()]
    if missing:
        raise PendingGate('Final evidence absent: ' + ', '.join(missing))
    sdk, closed, functional, review, observer = map(read, paths)
    if observer.get('state') != 'collected':
        raise PendingGate('The original T collector has not returned successfully.')
    assert observer['collectorExecuted'] and observer['collectorActualExitCode'] == 0
    assert sdk['passed'] and sdk['processes'] == 18 and sdk['sourceFiles'] == 894 and sdk['productInputs'] == 482
    assert sdk['formalSdkMatrixComplete'] and not sdk['pending'] and not sdk['livePids']
    assert len(sdk['rows']) == 18 and len({row['mode'] for row in sdk['rows']}) == 18
    assert all(row['actualExitCode'] == 0 and row['passed'] and row['actual']['passed'] for row in sdk['rows'])
    assert sdk['checks'] == sum(row['actual']['checks'] for row in sdk['rows'])
    assert closed['passed'] and closed['sdkProcesses'] == 18 and len(closed['completedScopes']) == 22
    assert closed['formalSdkMatrixComplete'] and closed['currentIncrementValidationComplete'] and closed['completedArtifactIntegrityPassed']
    assert closed['exactCurrentMainSourceMatch'] and closed['mainReadmeUnchanged']
    assert functional['passed'] and functional['processes'] == 12 and functional['checks'] == 275
    assert all(row['actualExitCode'] == 0 and row['passed'] and row['actual']['passed'] for row in functional['rows'])
    assert review['passed'] and review['autoCoverageClosed'] and not review['issues']
    seal = read(U / 'final-sdk-disposal-integration-r1/test-result.json')
    assert seal['passed'] and seal['canonicalTestPatchApplied'] and seal['canonicalMatchesSuccessfulSdkSnapshot']
    next_rows = read(P / 'resource-after-sdk-manifest.json')['files']
    source(U / 'source', next_rows)
    redirect = read(U / 'final-sdk-disposal-integration-r1/historical-canonical-reference-redirect.json')
    assert redirect['allowedRedirectCount'] == 1
    old_path = Path(redirect['oldCanonicalReference']['path']).resolve()
    assert old_path == (U / 'source' / LIFE).resolve()
    old_hash = redirect['oldCanonicalReference']['sha256']
    old_copy = Path(redirect['historicalBytesRetained']['path'])
    assert sha(old_copy) == old_hash == 'f8fea816545050b81a328fdae0b3ed7dd4e1788a5e12465fd8dd69b038c78348'
    facts = {}
    roots = list(paths) + [U / 'final-sdk-disposal-integration-r1/test-result.json',
            U / 'sdk-disposal-collector-review-r1/review-hash-manifest.json',
            Path(closed['verifiedArtifacts']['path'])]
    for root in roots:
        for row in file_facts(read(root)):
            facts[(row['path'], row['sha256'])] = row
    redirected = 0
    for row in facts.values():
        original = Path(row['path'])
        target = old_copy if original.resolve() == old_path and row['sha256'] == old_hash else original
        if target == old_copy:
            redirected += 1
        assert sha(target) == row['sha256'] and ('bytes' not in row or target.stat().st_size == row['bytes']), str(original)
    assert redirected == 1
    assert sha(MAIN / 'README.md') == README
    return dict(kind='closed-T-and-U-evidence-before-resource-main-application', passed=True, verifiedAtUtc=now(),
                sdkProcesses=18, sdkChecks=sdk['checks'], tCompletedScopes=22, uModes=12, uChecks=275,
                verifiedOriginalFacts=len(facts), exactHistoricalCanonicalRedirects=redirected,
                gates=[fact(path) for path in paths], canonicalTransition=fact(U / 'final-sdk-disposal-integration-r1/test-result.json'),
                originalArtifactIntegrityVerified=True, runner=fact(Path(__file__).resolve()), mainApplied=False, fullBCompleted=False, gameExecuted=False)


def apply_delta(name, before, after, delta, origin):
    output = P / name
    assert not output.exists(), 'Preserve prior applications and inspect their journal before any retry.'
    source(MAIN, before)
    source(origin, after)
    for row in delta:
        target = MAIN / row['path']
        assert target.resolve().is_relative_to(MAIN.resolve())
        if row['before'] is None:
            assert not target.exists() and not target.is_symlink(), str(target)
        else:
            assert sha(target) == row['before']['sha256']
    output.mkdir()
    journal = dict(kind=name, startedAtUtc=now(), sourceApplicationComplete=False, mainApplied=False, paths=[])
    save(output / 'application-journal.json', journal)
    for row in delta:
        target = MAIN / row['path']
        target.parent.mkdir(parents=True, exist_ok=True)
        temporary = target.with_name(target.name + '.riz-staged-copy')
        assert not temporary.exists()
        if row['before'] is not None:
            assert sha(target) == row['before']['sha256']
        else:
            assert not target.exists()
        shutil.copy2(origin / row['path'], temporary)
        assert sha(temporary) == row['after']['sha256']
        temporary.replace(target)
        journal['paths'].append(dict(path=row['path'], before=row['before'], after=fact(target)))
        journal['mainApplied'] = True
        save(output / 'application-journal.json', journal)
    source(MAIN, after)
    assert sha(MAIN / 'README.md') == README
    journal.update(sourceApplicationComplete=True, finishedAtUtc=now(), sourceFiles=len(after), changedPaths=len(delta))
    save(output / 'application-journal.json', journal)
    return output


def run(output, name, command, env, cwd, result):
    folder = output / name
    folder.mkdir()
    row = dict(name=name, command=[str(x) for x in command], cwd=str(cwd), startedAtUtc=now(), actualExitCode=None, passed=False)
    result['rows'].append(row)
    with (folder / 'stdout.log').open('xb') as stdout, (folder / 'stderr.log').open('xb') as stderr:
        process = subprocess.Popen(row['command'], cwd=cwd, env=env, stdout=stdout, stderr=stderr, creationflags=subprocess.CREATE_NO_WINDOW)
        row['pid'] = process.pid
        save(folder / 'launch.json', row)
        save(output / 'test-result.json', result)
        row['actualExitCode'] = process.wait()
    (folder / 'exit.txt').write_text(str(row['actualExitCode']), encoding='utf-8')
    row.update(finishedAtUtc=now(), passed=row['actualExitCode'] == 0, stdout=fact(folder / 'stdout.log'), stderr=fact(folder / 'stderr.log'))
    save(folder / 'process-result.json', row)
    save(output / 'test-result.json', result)
    assert row['passed'], name + ' actual exit ' + str(row['actualExitCode'])
    return (folder / 'stdout.log').read_text(encoding='utf-8-sig')


def resource():
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    kernel.GetCurrentProcess.restype = wintypes.HANDLE
    kernel.IsProcessInJob.argtypes = [wintypes.HANDLE, wintypes.HANDLE, ctypes.POINTER(wintypes.BOOL)]
    in_job = wintypes.BOOL()
    assert kernel.IsProcessInJob(kernel.GetCurrentProcess(), None, ctypes.byref(in_job)) and not in_job.value
    receipt = admission()
    before = read(P / 'main-before-manifest.json')['files']
    after = read(P / 'resource-after-sdk-manifest.json')['files']
    delta = read(P / 'staged-deltas.json')['resource']
    assert len(before) == 894 and len(after) == 913 and len(delta) == 53
    output = apply_delta('main-resource-application-r1', before, after, delta, U / 'source')
    save(output / 'admission-before-application.json', receipt)
    result = dict(kind='fresh-resource-main-focused-integration', passed=False, mainApplied=True,
                  startedAtUtc=now(), sourceFiles=913, rows=[], gameExecuted=False, installed=False,
                  fullIncrementAcceptanceComplete=False, fullBCompleted=False)
    try:
        snapshot = output / 'source'
        for row in after:
            target = snapshot / row['path']
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(MAIN / row['path'], target)
        source(snapshot, after)
        save(output / 'source-manifest.json', dict(source=str(MAIN), frozen=str(snapshot), files=after))
        environment = output / 'environment'
        for name in ['dotnet-home','packages','temp','http-cache']:
            (environment / name).mkdir(parents=True)
        config = output / 'NuGet.Config'
        config.write_text('<configuration><packageSources><clear/><add key="OfflineReferencePackages" value="' + str(FEED) + '"/></packageSources></configuration>', encoding='utf-8')
        env = os.environ.copy()
        env.update(DOTNET_CLI_HOME=str(environment / 'dotnet-home'), NUGET_PACKAGES=str(environment / 'packages'),
                   NUGET_HTTP_CACHE_PATH=str(environment / 'http-cache'), TEMP=str(environment / 'temp'), TMP=str(environment / 'temp'),
                   DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_NOLOGO='1', DOTNET_CLI_UI_LANGUAGE='en', MSBUILDDISABLENODEREUSE='1', NuGetAudit='false')
        project = snapshot / 'tests/RizToolbox.PlaybackStartInitialization.Tests/RizToolbox.PlaybackStartInitialization.Tests.csproj'
        run(output, 'resource-build', [DOTNET,'build',project,'-c','Release','-m:1','--configfile',config,'-p:UseSharedCompilation=false','--nologo','-v:minimal'], env, snapshot, result)
        app = project.parent / 'bin/Release/net8.0/RizToolbox.Gameplay.Tests.dll'
        equal_products = []
        for old in read(U / 'sdk-current-dispose-r1/build-products.json'):
            actual = fact(app.parent / Path(old['path']).name)
            assert actual['sha256'] == old['sha256'] and actual['bytes'] == old['bytes']
            equal_products.append(dict(validatedSdk=old, freshMain=actual))
        save(output / 'fresh-main-build-products.json', dict(passed=True, byteIdenticalToSuccessfulSdk=True, files=equal_products))
        inputs = []
        for row in read(U / 'sdk-current-dispose-r1/test-result.json')['nativeInputs']:
            # The successful SDK scope pins four flat physical-input facts, in invocation order.
            selected = row
            original = Path(selected['path'])
            assert sha(original) == selected['sha256']
            target = output / 'inputs' / original.name
            target.parent.mkdir(exist_ok=True)
            shutil.copy2(original, target)
            assert sha(target) == selected['sha256']
            inputs.append(target)
        assert [p.name for p in inputs] == ['RizToolbox.PlaybackStockFixture.dll','RizToolbox.SingleReturn.Prototype.dll','RizToolbox.PlaybackSourcePrototype.dll','dobby.dll']
        checks = 0
        for mode, expected in [('resource-current',17),('resource-capture',25)]:
            text = run(output, mode, [DOTNET,app,mode,*inputs], env, snapshot, result)
            actual = json.loads([line for line in text.splitlines() if line][-1])
            assert actual['passed'] and actual['mode'] == mode and actual['checks'] == expected
            checks += expected
        camera = snapshot / 'tests/RizToolbox.GameCamera.Tests/RizToolbox.GameCamera.Tests.csproj'
        run(output,'camera-build',[DOTNET,'build',camera,'-c','Release','-m:1','--configfile',config,'-p:UseSharedCompilation=false','--nologo','-v:minimal'],env,snapshot,result)
        text = run(output,'camera',[DOTNET,camera.parent / 'bin/Release/net8.0/RizToolbox.GameCamera.Tests.dll'],env,snapshot,result)
        assert 'Game camera checks: 34/34 passed.' in text
        result.update(passed=True, focusedChecks=checks+34, resourceChecks=checks, cameraChecks=34,
                      freshBuildProductCount=len(equal_products), oldSdkBinaryIdentityVerified=True,
                      products=fact(output / 'fresh-main-build-products.json'))
    except BaseException:
        result['failure'] = traceback.format_exc()
    finally:
        source(MAIN, after)
        result.update(main913Unchanged=True, finishedAtUtc=now())
        save(output / 'test-result.json', result)
    return 0 if result['passed'] else 1


def camera():
    prior = P / 'main-resource-application-r1/test-result.json'
    result = read(prior)
    assert result['passed'] and result['focusedChecks'] == 76 and result['main913Unchanged']
    receipt = read(P / 'main-resource-application-r1/admission-before-application.json')
    assert receipt['passed'] and receipt['sdkProcesses'] == 18 and receipt['uChecks'] == 275
    for row in receipt['gates']:
        assert sha(Path(row['path'])) == row['sha256']
    before = read(P / 'resource-after-sdk-manifest.json')['files']
    after = read(P / 'joint-candidate-manifest.json')['files']
    delta = read(P / 'staged-deltas.json')['camera']
    assert len(before) == 913 and len(after) == 915 and len(delta) == 9
    output = apply_delta('main-camera-application-r1',before,after,delta,P / 'candidate-915')
    save(output / 'test-result.json', dict(kind='precise-camera-source-main-application', passed=True,
         mainApplied=True, sourceApplicationComplete=True, changedPaths=9, mainSourceFiles=915,
         sourceManifest=fact(P / 'joint-candidate-manifest.json'), resourceValidation=fact(prior),
         journal=fact(output / 'application-journal.json'), finishedAtUtc=now(),
         freshJointValidationComplete=False, fullIncrementAcceptanceComplete=False, gameExecuted=False, installed=False, fullBCompleted=False))
    return 0


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument('--check', action='store_true')
    mode.add_argument('--resource', action='store_true')
    mode.add_argument('--camera', action='store_true')
    args = parser.parse_args()
    try:
        if args.check:
            print(json.dumps(admission()))
        else:
            raise SystemExit(resource() if args.resource else camera())
    except PendingGate as error:
        print(json.dumps(dict(ready=False, pending=str(error), mainApplied=False)))
        raise SystemExit(3)
