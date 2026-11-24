"""Fresh r6 continuation. Retain r2 actual regression evidence; run affected tests and remaining builds."""
from pathlib import Path
import argparse
import ctypes
from ctypes import wintypes
import datetime
import difflib
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import traceback

O = Path(__file__).resolve().parent
R = O.parent
S = O / 'source'
B = O / 'solution-source'
LIVE = R / 'source'
OLD = R / 'foundation-r2'
DOTNET = 'C:/Program Files/dotnet/dotnet.exe'
PWSH = 'C:/Users/71957/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe'
PROFILE = Path('C:/Users/71957/riz-build-cache/engine-production-r5-20261026/NativeEngineProductionProfile.Generated.cs')
NATIVE = Path('D:/riz-build-cache-next/engine-transport-production-native-r2-20261027')
CS = 'tests/RizToolbox.InitializationRootFree.Tests/RizToolbox.InitializationRootFree.Tests.csproj'
PROGRAM = 'tests/RizToolbox.InitializationRootFree.Tests/Program.cs'
CAMERA = 'tests/RizToolbox.GameCamera.Tests/NativeCameraChecks.cs'
SKIP = {'bin', 'obj', '.git', '__pycache__'}
MODES = ('tracker-resource-free-fault',)
K = ctypes.WinDLL('kernel32', use_last_error=True)
K.GetCurrentProcess.restype = wintypes.HANDLE
K.GetProcessTimes.argtypes = [wintypes.HANDLE, ctypes.POINTER(wintypes.FILETIME), ctypes.POINTER(wintypes.FILETIME), ctypes.POINTER(wintypes.FILETIME), ctypes.POINTER(wintypes.FILETIME)]
K.IsProcessInJob.argtypes = [wintypes.HANDLE, wintypes.HANDLE, ctypes.POINTER(wintypes.BOOL)]
now = lambda: datetime.datetime.now(datetime.timezone.utc).isoformat()
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
fact = lambda p: dict(path=str(p), sha256=sha(p), bytes=p.stat().st_size)
read = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))


def save(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    q = path.with_suffix(path.suffix + '.writing')
    q.write_text(json.dumps(value, indent=2), encoding='utf-8')
    q.replace(path)


def created(handle):
    a, b, c, d = (wintypes.FILETIME() for _ in range(4))
    assert K.GetProcessTimes(handle, ctypes.byref(a), ctypes.byref(b), ctypes.byref(c), ctypes.byref(d))
    ticks = (a.dwHighDateTime << 32) + a.dwLowDateTime
    return (datetime.datetime(1601, 1, 1, tzinfo=datetime.timezone.utc) + datetime.timedelta(microseconds=ticks // 10)).isoformat()


def verify(row):
    p = Path(row['path'])
    assert p.is_file() and sha(p) == row['sha256'], 'Input changed: ' + str(p)
    if 'bytes' in row:
        assert p.stat().st_size == row['bytes'], 'Input length changed: ' + str(p)


def inventory(root):
    return sorted(p for p in root.rglob('*') if p.is_file() and not set(p.relative_to(root).parts) & SKIP)


def copy_source(root, rows):
    assert not root.exists(), 'Preserve previous source copies: ' + str(root)
    root.mkdir()
    for row in rows:
        original = S / row['path']
        target = root / row['path']
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(original, target)
        assert sha(target) == row['sha256']


def check_source(root, rows):
    return all((root / row['path']).is_file() and sha(root / row['path']) == row['sha256'] for row in rows)


def run(name, command, env, result, cwd=B):
    folder = O / name
    folder.mkdir(exist_ok=False)
    row = dict(name=name, command=[str(p) for p in command], cwd=str(cwd), startedAtUtc=now(), actualExitCode=None, passed=False)
    result['rows'].append(row)
    with (folder / 'stdout.log').open('xb') as stdout, (folder / 'stderr.log').open('xb') as stderr:
        process = subprocess.Popen(row['command'], cwd=cwd, env=env, stdout=stdout, stderr=stderr, creationflags=subprocess.CREATE_NO_WINDOW)
        row.update(pid=process.pid, creationTimeUtc=created(wintypes.HANDLE(int(process._handle))))
        save(folder / 'launch.json', row)
        save(O / 'foundation-continuation.progress.json', result)
        row['actualExitCode'] = process.wait()
    (folder / 'exit.txt').write_text(str(row['actualExitCode']), encoding='utf-8')
    row.update(finishedAtUtc=now(), stdout=fact(folder / 'stdout.log'), stderr=fact(folder / 'stderr.log'))
    row['passed'] = row['actualExitCode'] == 0
    save(folder / 'process-result.json', row)
    save(O / 'foundation-continuation.progress.json', result)
    assert row['passed'], 'Actual step failed: ' + name
    return row


def products(directory, output):
    rows = [fact(p) for p in sorted(directory.rglob('*')) if p.is_file() and p.suffix.lower() in {'.dll', '.pdb', '.json'}]
    save(output, rows)
    return rows


def retain_stage_products(directory, destination, output):
    destination.mkdir(exist_ok=False)
    rows = []
    for p in sorted(directory.rglob('*')):
        if not p.is_file() or p.suffix.lower() not in {'.dll', '.pdb', '.json'}:
            continue
        original = fact(p)
        target = destination / p.relative_to(directory)
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(p, target)
        assert sha(target) == original['sha256']
        rows.append(dict(original=original, retained=fact(target)))
    save(output, rows)
    return rows


def main():
    assert not (O / 'foundation-continuation.result.json').exists(), 'Preserve earlier continuation terminals.'
    inside = wintypes.BOOL()
    assert K.IsProcessInJob(K.GetCurrentProcess(), None, ctypes.byref(inside))
    result = dict(kind='foundation-r7-remaining-build-continuation', passed=False,
        runnerPid=os.getpid(), parentPid=os.getppid(), command=[sys.executable, *sys.argv],
        creationTimeUtc=created(K.GetCurrentProcess()), inJob=bool(inside.value), startedAtUtc=now(), rows=[],
        driver=fact(Path(__file__).resolve()), gameExecuted=False, installed=False, downloaded=False,
        productionAdmission=False, currentMainModified=False, rootFreeCasesReexecuted=False,
        inheritedManagedMatrixReexecuted=False)
    save(O / 'foundation-continuation.launch.json', result)
    rows = []
    try:
        assert not inside.value
        initial_path = O / 'foundation-driver.result.json'
        initial = read(initial_path)
        assert initial['passed'] is False and len(initial['rows']) == 3
        assert [row['name'] for row in initial['rows']] == ['root-free-build-r7', 'tracker-resource-free-fault', 'root-free-symbols-r7']
        assert all(row['actualExitCode'] == 0 and row['passed'] is True for row in initial['rows'])
        assert 'FileNotFoundError' in initial['failure'] and 'symbols-rootfree-r7' in initial['failure']
        verify(initial['driver'])
        manifest = read(O / 'source-manifest.json')
        rows = manifest['files']
        assert len(rows) == 913 and check_source(S, rows) and check_source(B, rows) and check_source(LIVE, rows)
        assert PROFILE.is_file() and sha(PROFILE) == 'bf5d5cf89f20e068b9a7a7cc8f5f055cd5c19cad3aa1db0f053f6649e78aa63b'
        result.update(initialDriver=fact(initial_path), sourceManifest=fact(O / 'source-manifest.json'), legacyProfile=fact(PROFILE))
        tests = read(O / 'root-free-test-result.json')
        assert tests['passed'] is True and tests['actualCaseCount'] == 5 and tests['freshProcesses'] == 1 and tests['inheritedProcesses'] == 4
        assert tests['freshChecks'] == 33 and tests['inheritedChecks'] == 80 and tests['checks'] == 113
        result['completedAffectedCases'] = fact(O / 'root-free-test-result.json')
        symbol_path = O / 'symbols-rootfree-r6/test-result.json'
        symbol_proof = read(symbol_path)
        assert symbol_proof['passed'] is True and symbol_proof['stage'] == 'RootFree' and symbol_proof['pePdbPairs'] == 5 and symbol_proof['documentCount'] == 497
        verify(symbol_proof['verifier'])
        verify(symbol_proof['sourceManifest'])
        for image in symbol_proof['images']:
            verify(image['assembly']); verify(image['pdb'])
        result['completedRootFreeSymbolProof'] = fact(symbol_path)
        basis = O / 'basis'
        environment = O / 'environment'
        local_feed = environment / 'local-feed'
        seed = read(O / 'reference-package-seed-r7.json')
        assert seed['passed'] is True and seed['packageCount'] == 3
        for row in seed['files'] + seed['localFeed']:
            verify(row)
        env = os.environ.copy()
        env.update(PATH='C:/Program Files/dotnet;' + env.get('PATH', ''), DOTNET_CLI_HOME=str(environment / 'dotnet-home'),
            NUGET_PACKAGES=str(environment / 'packages'), NUGET_HTTP_CACHE_PATH=str(environment / 'http-cache'), TEMP=str(environment / 'temp'), TMP=str(environment / 'temp'),
            DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_NOLOGO='1', DOTNET_CLI_UI_LANGUAGE='en', MSBUILDDISABLENODEREUSE='1',
            DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1', NuGetAudit='false')
        solution = run('solution-r7', [DOTNET, 'build', B / 'RizToolbox.sln', '-c', 'Release', '-m:1', '--configfile', O / 'NuGet.Config',
            '-p:UseSharedCompilation=false', '-p:NuGetAudit=false', '-p:RizEngineProductionProfileSource=' + str(PROFILE), '--nologo', '-v:minimal'], env, result)
        body = (O / 'solution-r7/stdout.log').read_text(encoding='utf-8-sig') + (O / 'solution-r7/stderr.log').read_text(encoding='utf-8-sig')
        warnings = re.findall(r'(?m)^\s*(\d+)\s+Warning\(s\)\s*$', body)
        errors = re.findall(r'(?m)^\s*(\d+)\s+Error\(s\)\s*$', body)
        assert warnings and errors and int(errors[-1]) == 0 and check_source(B, rows)
        solution_products = retain_stage_products(B / 'src', O / 'solution-managed-products', O / 'solution-r7/product-files.json')
        save(O / 'solution-r7/test-result.json', dict(kind='foundation-r7-fresh-solution-build', passed=True,
            actualExitCode=0, warnings=int(warnings[-1]), errors=0, process=fact(O / 'solution-r7/process-result.json'),
            sourceManifest=fact(O / 'source-manifest.json'), compiledCSharpInputCount=765,
            sourceHashesUnchanged=True, productFiles=fact(O / 'solution-r7/product-files.json'),
            inherited28SuiteEvidence=fact(O / 'retained-r2-regression-evidence.json'), gameExecuted=False, installed=False))
        sdk_source = O / 'sdk-source'
        copy_source(sdk_source, rows)
        (sdk_source / 'NuGet.Config').write_text(config, encoding='utf-8')
        sdk_script = sdk_source / 'scripts/Test-SdkDistribution.ps1'
        original_sdk = sdk_script.read_text(encoding='utf-8-sig')
        assert original_sdk.count('https://api.nuget.org/v3/index.json') == 1
        offline_sdk = original_sdk.replace('https://api.nuget.org/v3/index.json', str(local_feed))
        sdk_script.write_text(offline_sdk, encoding='utf-8')
        patch = ''.join(difflib.unified_diff(original_sdk.splitlines(True), offline_sdk.splitlines(True), 'a/scripts/Test-SdkDistribution.ps1', 'offline-runner/scripts/Test-SdkDistribution.ps1'))
        (basis / 'sdk-offline-feed-only-runner.patch').write_text(patch, encoding='utf-8')
        save(O / 'sdk-offline-runner-transformation.json', dict(kind='sdk-runner-only-offline-package-source-transformation', passed=True,
            original=fact(S / 'scripts/Test-SdkDistribution.ps1'), runner=fact(sdk_script), patch=fact(basis / 'sdk-offline-feed-only-runner.patch'),
            onlyNugetSourceChanged=True, allTestAssertionsUnmodified=True, softwareInstalled=False))
        sdk = run('sdk-r7', [PWSH, '-NoProfile', '-File', sdk_script], env, result, cwd=sdk_source)
        sdk_results = list((sdk_source / 'artifacts').glob('sdk-distribution-*/result.json'))
        assert len(sdk_results) == 1
        sdk_actual = read(sdk_results[0])
        assert sdk_actual['checksPassed'] == 14 and sdk_actual['runtimeGameVerification'] is False
        for name in ('AuthorOne', 'AuthorTwo'):
            assert (Path(sdk_actual['generatedProjects']) / name / ('bin/Release/net6.0/' + name + '.dll')).is_file()
        save(O / 'sdk-r7/test-result.json', dict(kind='foundation-r7-actual-offline-sdk-distribution', passed=True,
            process=fact(O / 'sdk-r7/process-result.json'), actualExitCode=0, checks=14, generatedIndependentPlugins=2,
            result=fact(sdk_results[0]), sourceManifest=fact(O / 'source-manifest.json'),
            runnerTransformation=fact(O / 'sdk-offline-runner-transformation.json'),
            products=[fact(p) for p in sorted((sdk_source / 'artifacts').rglob('*')) if p.is_file() and p.suffix in {'.dll', '.pdb', '.nupkg'}],
            runtimeGameVerification=False, softwareInstalled=False, downloaded=False))
        native_path = NATIVE / 'production-native-build.json'
        native = read(native_path)
        assert native['kind'] == 'engine-transport-production-native-candidate-build' and native['passed'] is True
        assert native['productionAdmissionGranted'] is False
        for row in native['files']:
            verify(row)
        transport_profile = NATIVE / 'NativeEngineTransportProfile.Generated.cs'
        release = run('release-r7', [DOTNET, 'build', B / 'src/RizToolbox.Loader/RizToolbox.Loader.csproj', '-c', 'Release', '-m:1',
            '--configfile', O / 'NuGet.Config', '-p:UseSharedCompilation=false', '-p:NuGetAudit=false',
            '-p:RizEngineProductionProfileSource=' + str(PROFILE), '-p:RizEngineTransportProfileSource=' + str(transport_profile),
            '--nologo', '-v:minimal', '-o', O / 'release-managed'], env, result)
        assemblies = [fact(O / 'release-managed' / ('RizToolbox.' + name + '.dll')) for name in ('Sdk', 'Core', 'Runtime', 'Adapter', 'Hosting', 'Loader')]
        assert len(assemblies) == 6 and check_source(B, rows)
        save(O / 'release-r7/managed-build-manifest.json', dict(kind='engine-transport-production-managed-candidate-build', passed=True,
            buildExitCode=0, sourceHashesUnchanged=True, sourceManifest=fact(O / 'source-manifest.json'),
            sources=[fact(B / row['path']) for row in rows if row['path'].startswith('src/')],
            nativeBuild=fact(native_path), generatedSource=fact(transport_profile), legacyProfile=fact(PROFILE),
            process=fact(O / 'release-r7/process-result.json'), files=assemblies,
            productionAdmissionGranted=False, executionWindowProved=False, gameExecuted=False, installed=False))
        symbols = run('release-symbols-r7', [PWSH, '-NoProfile', '-File', O / 'Verify-ManagedSymbolsR7Continuation.ps1', '-Stage', 'Release'], env, result)
        result['releaseManagedSymbolProof'] = fact(O / 'symbols-release-r7/test-result.json')
        result.update(passed=True, solution=fact(O / 'solution-r7/test-result.json'), sdk=fact(O / 'sdk-r7/test-result.json'),
            release=fact(O / 'release-r7/managed-build-manifest.json'))
    except BaseException:
        result['failure'] = traceback.format_exc()
        result['passed'] = False
    finally:
        result['frozenSourcesUnchanged'] = bool(rows) and check_source(S, rows)
        result['originSourcesUnchanged'] = bool(rows) and check_source(LIVE, rows)
        result['passed'] = result['passed'] and result['frozenSourcesUnchanged'] and result['originSourcesUnchanged']
        result['finishedAtUtc'] = now()
        (O / 'foundation-continuation.exit.txt').write_text('0' if result['passed'] else '1', encoding='utf-8')
        save(O / 'foundation-continuation.result.json', result)
    return 0 if result['passed'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
