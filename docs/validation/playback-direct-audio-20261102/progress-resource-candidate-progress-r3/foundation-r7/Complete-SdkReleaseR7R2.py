"""Only SDK distribution, the six-assembly release build, and its symbol proof remain."""
from pathlib import Path
import argparse
import ast
import builtins
import ctypes
from ctypes import wintypes
import datetime
import difflib
import hashlib
import json
import os
import shutil
import subprocess
import sys
import traceback

O = Path(__file__).resolve().parent
R = O.parent
S = O / 'source'
B = O / 'solution-source'
SDK = O / 'sdk-source-r2'
DOTNET = Path('C:/Program Files/dotnet/dotnet.exe')
PWSH = Path('C:/Users/71957/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe')
PROFILE = Path('C:/Users/71957/riz-build-cache/engine-production-r5-20261026/NativeEngineProductionProfile.Generated.cs')
NATIVE = Path('D:/riz-build-cache-next/engine-transport-production-native-r2-20261027')
TRANSPORT = NATIVE / 'NativeEngineTransportProfile.Generated.cs'
SYMBOL_VERIFIER = O / 'Verify-ManagedSymbolsR7Continuation.ps1'
now = lambda: datetime.datetime.now(datetime.timezone.utc).isoformat()
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
fact = lambda p: dict(path=str(p), sha256=sha(p), bytes=p.stat().st_size)
read = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))
K = ctypes.WinDLL('kernel32', use_last_error=True)
K.GetCurrentProcess.restype = wintypes.HANDLE
K.GetProcessTimes.argtypes = [wintypes.HANDLE, ctypes.POINTER(wintypes.FILETIME), ctypes.POINTER(wintypes.FILETIME), ctypes.POINTER(wintypes.FILETIME), ctypes.POINTER(wintypes.FILETIME)]
K.IsProcessInJob.argtypes = [wintypes.HANDLE, wintypes.HANDLE, ctypes.POINTER(wintypes.BOOL)]


def created(handle):
    a, b, c, d = (wintypes.FILETIME() for _ in range(4))
    assert K.GetProcessTimes(handle, ctypes.byref(a), ctypes.byref(b), ctypes.byref(c), ctypes.byref(d))
    ticks = (a.dwHighDateTime << 32) + a.dwLowDateTime
    return (datetime.datetime(1601, 1, 1, tzinfo=datetime.timezone.utc) + datetime.timedelta(microseconds=ticks // 10)).isoformat()


def save(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + '.writing')
    temporary.write_text(json.dumps(value, indent=2), encoding='utf-8')
    temporary.replace(path)


def verify(row):
    p = Path(row['path'])
    assert p.is_file() and sha(p) == row['sha256'], 'Recorded bytes changed: ' + str(p)
    if 'bytes' in row:
        assert p.stat().st_size == row['bytes']


def check_source(root, rows):
    return all((root / row['path']).is_file() and sha(root / row['path']) == row['sha256'] for row in rows)


def preflight():
    """Read-only: validate every external path and historical prerequisite before launching work."""
    for path in (DOTNET, PWSH, PROFILE, TRANSPORT, SYMBOL_VERIFIER, O / 'NuGet.Config'):
        assert path.is_file(), 'Missing fixed input: ' + str(path)
    assert sha(PROFILE) == 'bf5d5cf89f20e068b9a7a7cc8f5f055cd5c19cad3aa1db0f053f6649e78aa63b'
    assert sha(TRANSPORT) == '60a05f096646a31c99b00b1f1d96e961dd6761e2ed42019bc3c39838e40cd06f'
    local_feed = O / 'environment/local-feed'
    config = (O / 'NuGet.Config').read_text(encoding='utf-8-sig')
    assert str(local_feed) in config and 'http' not in config.lower()
    assert len(list(local_feed.glob('*.nupkg'))) == 3
    seed = read(O / 'reference-package-seed-r7.json')
    assert seed['passed'] is True and seed['packageCount'] == 3
    for row in seed['files'] + seed['localFeed']:
        verify(row)
    manifest = read(O / 'source-manifest.json')
    rows = manifest['files']
    assert len(rows) == 913 and sum(row['path'].startswith('src/') for row in rows) == 484
    assert sum(row['path'].startswith('native/') for row in rows) == 48
    assert check_source(S, rows) and check_source(B, rows) and check_source(R / 'source', rows)
    solution = read(O / 'solution-r7/test-result.json')
    assert solution['passed'] is True and solution['actualExitCode'] == 0 and solution['errors'] == 0
    solution_process = read(O / 'solution-r7/process-result.json')
    assert solution_process['actualExitCode'] == 0 and solution_process['passed'] is True
    verify(solution_process['stdout']); verify(solution_process['stderr'])
    affected = read(O / 'root-free-test-result.json')
    assert affected['passed'] is True and affected['actualCaseCount'] == 5 and affected['checks'] == 113
    assert affected['freshChecks'] == 33 and affected['inheritedChecks'] == 80
    root_symbols_path = O / 'symbols-rootfree-r6/test-result.json'
    root_symbols = read(root_symbols_path)
    assert root_symbols['passed'] is True and root_symbols['pePdbPairs'] == 5 and root_symbols['documentCount'] == 497
    for image in root_symbols['images']:
        verify(image['assembly']); verify(image['pdb'])
    native_path = NATIVE / 'production-native-build.json'
    native = read(native_path)
    assert native['passed'] is True and native['productionAdmissionGranted'] is False
    for row in native['files']:
        verify(row)
    assert not SDK.exists(), 'Preserve the failed earlier SDK source copy and use the r2 copy.'
    for name in ('sdk-r7', 'release-r7', 'release-symbols-r7', 'release-managed', 'symbols-release-r7'):
        assert not (O / name).exists(), 'Preserve an earlier output instead of rerunning it: ' + name
    verifier_text = SYMBOL_VERIFIER.read_text(encoding='utf-8-sig')
    assert "'release-r7/process-result.json'" in verifier_text and "+'-r7')" in verifier_text
    assert "'release-managed'" in verifier_text and "'solution-source'" in verifier_text
    proof = dict(kind='foundation-r7-sdk-release-r2-read-only-preflight', passed=True,
        checkedAtUtc=now(), runner=fact(Path(__file__).resolve()), sourceManifest=fact(O / 'source-manifest.json'),
        sourceCount=913, managedProductCount=484, nativeSourceCount=48, sourceUnchanged=True,
        fixedInputs=[fact(p) for p in (DOTNET,PWSH,PROFILE,TRANSPORT,SYMBOL_VERIFIER,O / 'NuGet.Config')],
        localFeed=[fact(p) for p in sorted(local_feed.glob('*.nupkg'))],
        alreadyCompletedSolution=fact(O / 'solution-r7/test-result.json'),
        alreadyCompletedAffectedCases=fact(O / 'root-free-test-result.json'), alreadyCompletedRootFreeSymbols=fact(root_symbols_path),
        previousInitialFailure=fact(O / 'foundation-driver.result.json'), previousContinuationFailure=fact(O / 'foundation-continuation.result.json'),
        freshSdkSource=str(SDK), outputsConfirmedAbsent=True, testsOrBuildsExecutedByPreflight=False)
    return rows, config, local_feed, native_path, proof


def run(name, command, env, result, cwd):
    folder = O / name
    folder.mkdir(exist_ok=False)
    row = dict(name=name, command=[str(p) for p in command], cwd=str(cwd), startedAtUtc=now(), actualExitCode=None, passed=False)
    result['rows'].append(row)
    with (folder / 'stdout.log').open('xb') as stdout, (folder / 'stderr.log').open('xb') as stderr:
        process = subprocess.Popen(row['command'], cwd=cwd, env=env, stdout=stdout, stderr=stderr, creationflags=subprocess.CREATE_NO_WINDOW)
        row.update(pid=process.pid, creationTimeUtc=created(wintypes.HANDLE(int(process._handle))))
        save(folder / 'launch.json', row)
        save(O / 'continuation-r2.progress.json', result)
        row['actualExitCode'] = process.wait()
    (folder / 'exit.txt').write_text(str(row['actualExitCode']), encoding='utf-8')
    row.update(finishedAtUtc=now(), stdout=fact(folder / 'stdout.log'), stderr=fact(folder / 'stderr.log'))
    row['passed'] = row['actualExitCode'] == 0
    save(folder / 'process-result.json', row)
    save(O / 'continuation-r2.progress.json', result)
    assert row['passed'], 'Actual remaining step failed: ' + name
    return row


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--preflight-only', action='store_true')
    arguments = parser.parse_args()
    rows, config, local_feed, native_path, preflight_proof = preflight()
    if arguments.preflight_only:
        save(O / 'continuation-r2.preflight.json', preflight_proof)
        print(json.dumps(preflight_proof, indent=2))
        return 0
    assert not (O / 'continuation-r2.launch.json').exists(), 'Never rerun a retained continuation.'
    inside = wintypes.BOOL()
    assert K.IsProcessInJob(K.GetCurrentProcess(), None, ctypes.byref(inside))
    result = dict(kind='foundation-r7-sdk-release-remaining-continuation-r2', passed=False,
        runnerPid=os.getpid(), parentPid=os.getppid(), command=[sys.executable,*sys.argv], creationTimeUtc=created(K.GetCurrentProcess()),
        inJob=bool(inside.value), startedAtUtc=now(), rows=[], runner=fact(Path(__file__).resolve()),
        sourceManifest=fact(O / 'source-manifest.json'), preflight=preflight_proof,
        gameExecuted=False, installed=False, downloaded=False, productionAdmission=False,
        rootFreeReexecuted=False, solutionReexecuted=False, managed28Reexecuted=False)
    save(O / 'continuation-r2.launch.json', result)
    try:
        assert not inside.value
        environment = O / 'environment'
        env = os.environ.copy()
        env.update(PATH='C:/Program Files/dotnet;' + env.get('PATH',''), DOTNET_CLI_HOME=str(environment / 'dotnet-home'),
            NUGET_PACKAGES=str(environment / 'packages'), NUGET_HTTP_CACHE_PATH=str(environment / 'http-cache'),
            TEMP=str(environment / 'temp'), TMP=str(environment / 'temp'), DOTNET_CLI_TELEMETRY_OPTOUT='1',
            DOTNET_NOLOGO='1', DOTNET_CLI_UI_LANGUAGE='en', MSBUILDDISABLENODEREUSE='1', NuGetAudit='false')
        SDK.mkdir(exist_ok=False)
        for row in rows:
            original, destination = S / row['path'], SDK / row['path']
            assert sha(original) == row['sha256']
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(original, destination)
            assert sha(destination) == row['sha256']
        (SDK / 'NuGet.Config').write_text(config, encoding='utf-8')
        sdk_script = SDK / 'scripts/Test-SdkDistribution.ps1'
        original_text = sdk_script.read_text(encoding='utf-8-sig')
        remote = 'https://api.nuget.org/v3/index.json'
        assert original_text.count(remote) == 1
        offline_text = original_text.replace(remote, str(local_feed))
        sdk_script.write_text(offline_text, encoding='utf-8')
        patch_path = O / 'basis/sdk-offline-feed-only-r2.patch'
        patch_path.write_text(''.join(difflib.unified_diff(original_text.splitlines(True), offline_text.splitlines(True),
            'frozen/scripts/Test-SdkDistribution.ps1', 'sdk-source-r2/scripts/Test-SdkDistribution.ps1')), encoding='utf-8')
        transform_path = O / 'sdk-offline-runner-transformation-r2.json'
        save(transform_path, dict(kind='offline-sdk-distribution-runner-only-feed-transformation', passed=True,
            original=fact(S / 'scripts/Test-SdkDistribution.ps1'), runner=fact(sdk_script), patch=fact(patch_path),
            onlyPackageSourceChanged=True, allAssertionsUnmodified=True, downloaded=False, softwareInstalled=False))
        run('sdk-r7', [PWSH,'-NoProfile','-File',sdk_script], env, result, SDK)
        actual_paths = list((SDK / 'artifacts').glob('sdk-distribution-*/result.json'))
        assert len(actual_paths) == 1
        actual_sdk = read(actual_paths[0])
        assert actual_sdk['checksPassed'] == 14 and actual_sdk['runtimeGameVerification'] is False
        for name in ('AuthorOne','AuthorTwo'):
            assert (Path(actual_sdk['generatedProjects']) / name / ('bin/Release/net6.0/' + name + '.dll')).is_file()
        for row in rows:
            if row['path'] != 'scripts/Test-SdkDistribution.ps1':
                assert sha(SDK / row['path']) == row['sha256']
        save(O / 'sdk-r7/test-result.json', dict(kind='foundation-r7-actual-offline-sdk-distribution', passed=True,
            actualExitCode=0, checks=14, generatedIndependentPlugins=2, process=fact(O / 'sdk-r7/process-result.json'),
            result=fact(actual_paths[0]), sourceManifest=fact(O / 'source-manifest.json'), runnerTransformation=fact(transform_path),
            sdkSourceRoot=str(SDK), products=[fact(p) for p in sorted((SDK / 'artifacts').rglob('*')) if p.is_file() and p.suffix.lower() in {'.dll','.pdb','.nupkg'}],
            runtimeGameVerification=False, softwareInstalled=False, downloaded=False))
        run('release-r7', [DOTNET,'build',B / 'src/RizToolbox.Loader/RizToolbox.Loader.csproj','-c','Release','-m:1',
            '--configfile',O / 'NuGet.Config','-p:UseSharedCompilation=false','-p:NuGetAudit=false',
            '-p:RizEngineProductionProfileSource=' + str(PROFILE),'-p:RizEngineTransportProfileSource=' + str(TRANSPORT),
            '--nologo','-v:minimal','-o',O / 'release-managed'], env, result, B)
        files = [fact(O / 'release-managed' / ('RizToolbox.' + name + '.dll')) for name in ('Sdk','Core','Runtime','Adapter','Hosting','Loader')]
        assert len(files) == 6 and check_source(B, rows)
        save(O / 'release-r7/managed-build-manifest.json', dict(kind='engine-transport-production-managed-candidate-build', passed=True,
            buildExitCode=0, sourceHashesUnchanged=True, sourceManifest=fact(O / 'source-manifest.json'),
            sources=[fact(B / row['path']) for row in rows if row['path'].startswith('src/')],
            nativeBuild=fact(native_path), generatedSource=fact(TRANSPORT), legacyProfile=fact(PROFILE),
            process=fact(O / 'release-r7/process-result.json'), files=files,
            productionAdmissionGranted=False, executionWindowProved=False, gameExecuted=False, installed=False))
        run('release-symbols-r7', [PWSH,'-NoProfile','-File',SYMBOL_VERIFIER,'-Stage','Release'], env, result, O)
        release_symbol_path = O / 'symbols-release-r7/test-result.json'
        release_symbols = read(release_symbol_path)
        assert release_symbols['passed'] is True and release_symbols['pePdbPairs'] == 6 and release_symbols['stage'] == 'Release'
        result.update(passed=True, sdk=fact(O / 'sdk-r7/test-result.json'), release=fact(O / 'release-r7/managed-build-manifest.json'),
            releaseSymbolProof=fact(release_symbol_path), sdkChecks=14, releaseAssemblyCount=6)
    except BaseException:
        result['failure'] = traceback.format_exc()
        result['passed'] = False
    finally:
        result['frozenSourcesUnchanged'] = check_source(S, rows)
        result['originSourcesUnchanged'] = check_source(R / 'source', rows)
        result['passed'] = result['passed'] and result['frozenSourcesUnchanged'] and result['originSourcesUnchanged']
        result['finishedAtUtc'] = now()
        (O / 'continuation-r2.exit.txt').write_text('0' if result['passed'] else '1', encoding='utf-8')
        save(O / 'continuation-r2.result.json', result)
    return 0 if result['passed'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
