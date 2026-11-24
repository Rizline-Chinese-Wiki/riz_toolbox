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
        save(O / 'foundation-driver.progress.json', result)
        row['actualExitCode'] = process.wait()
    (folder / 'exit.txt').write_text(str(row['actualExitCode']), encoding='utf-8')
    row.update(finishedAtUtc=now(), stdout=fact(folder / 'stdout.log'), stderr=fact(folder / 'stderr.log'))
    row['passed'] = row['actualExitCode'] == 0
    save(folder / 'process-result.json', row)
    save(O / 'foundation-driver.progress.json', result)
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
    parser = argparse.ArgumentParser()
    parser.add_argument('--final-freeze', action='store_true', required=True)
    parser.parse_args()
    assert not S.exists() and not (O / 'foundation-driver.result.json').exists(), 'Never rerun a retained r6 driver.'
    inside = wintypes.BOOL()
    assert K.IsProcessInJob(K.GetCurrentProcess(), None, ctypes.byref(inside))
    result = dict(kind='progress-resource-foundation-r7-fresh-continuation-driver', passed=False,
        runnerPid=os.getpid(), parentPid=os.getppid(), command=[sys.executable, *sys.argv], creationTimeUtc=created(K.GetCurrentProcess()),
        inJob=bool(inside.value), startedAtUtc=now(), rows=[], finalFreezeExplicit=True,
        driver=fact(Path(__file__).resolve()), inheritedChecksReexecuted=False,
        gameExecuted=False, installed=False, downloaded=False, productionAdmission=False, currentMainModified=False)
    rows = []
    try:
        assert not inside.value, 'WMI driver must remain independent of the exec job.'
        assert PROFILE.is_file() and sha(PROFILE) == 'bf5d5cf89f20e068b9a7a7cc8f5f055cd5c19cad3aa1db0f053f6649e78aa63b'
        result['fixedLegacyProfile'] = fact(PROFILE)
        result['preservedPreviousR6RunnerFailure'] = fact(R / 'foundation-r6/foundation-driver.result.json')
        save(O / 'foundation-driver.launch.json', result)
        before = [dict(path=p.relative_to(LIVE).as_posix(), sha256=sha(p), bytes=p.stat().st_size) for p in inventory(LIVE)]
        assert len(before) == 913
        S.mkdir()
        for row in before:
            original, target = LIVE / row['path'], S / row['path']
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(original, target)
            assert sha(target) == sha(original) == row['sha256']
            rows.append(row)
        assert [p.relative_to(LIVE).as_posix() for p in inventory(LIVE)] == [r['path'] for r in rows]
        assert check_source(LIVE, rows)
        prior_r6 = read(R / 'foundation-r6/source-manifest.json')
        assert {row['path']: row['sha256'] for row in rows} == {row['path']: row['sha256'] for row in prior_r6['files']}
        save(O / 'source-manifest.json', dict(kind='progress-resource-foundation-r7-final-freeze', source=str(S), origin=str(LIVE), files=rows,
            capturedAtUtc=now(), finalFreezeExplicit=True, sourceUnchangedDuringCapture=True))
        result['sourceManifest'] = fact(O / 'source-manifest.json')
        current = {row['path']: row for row in rows}
        old_manifest = read(OLD / 'source-manifest.json')
        functional_manifest = read(R / 'functional-matrix-r2/source-manifest.json')
        old = {row['path']: row for row in old_manifest['files']}
        functional = {row['path']: row for row in functional_manifest['files']}
        assert current.keys() == old.keys() == functional.keys()
        changes_old = [p for p in current if current[p]['sha256'] != old[p]['sha256']]
        changes_functional = [p for p in current if current[p]['sha256'] != functional[p]['sha256']]
        assert sorted(changes_old) == sorted([CS, PROGRAM]) and sorted(changes_functional) == sorted([CS, PROGRAM, CAMERA])
        assert sum(p.startswith('src/') for p in current) == 484 and sum(p.startswith('native/') for p in current) == 48
        assert sum(p.endswith('.cs') for p in current) == 765
        before_text = (OLD / 'source' / CS).read_text(encoding='utf-8-sig').replace('\r\n', '\n')
        after_text = (S / CS).read_text(encoding='utf-8-sig').replace('\r\n', '\n')
        seam = '    <Compile Include="../RizToolbox.PlaybackStartInitialization.Tests/FakeNative.SharedConstructor.cs" Link="NativeFixture/FakeNative.SharedConstructor.cs" />\n'
        addition = ('    <Compile Include="../RizToolbox.PlaybackStartInitialization.Tests/FakeNative.DirectAudio.cs" Link="NativeFixture/FakeNative.DirectAudio.cs" />\n'
            + '    <Compile Include="../RizToolbox.PlaybackStartInitialization.Tests/FakeNative.ProgressResource.cs" Link="NativeFixture/FakeNative.ProgressResource.cs" />\n')
        assert before_text.count(seam) == 1 and after_text == before_text.replace(seam, seam + addition)
        basis = O / 'basis'
        basis.mkdir(exist_ok=True)
        delta = read(O / 'test-delta.json')
        assert delta['passed'] is True and delta['onlyChangedCSharpInput'] == PROGRAM
        for changed in delta['changes']:
            verify(changed['before'])
            verify(changed['after'])
            verify(changed['candidate'])
            assert current[changed['path']]['sha256'] == changed['after']['sha256']
        shutil.copy2(OLD / 'source' / CS, basis / 'InitializationRootFree.foundation-r2.csproj')
        patch = ''.join(difflib.unified_diff(before_text.splitlines(True), after_text.splitlines(True), 'a/' + CS, 'b/' + CS))
        (basis / 'initialization-root-free-links-from-r2.patch').write_text(patch, encoding='utf-8')
        comparison = dict(kind='progress-resource-foundation-r7-source-inheritance-proof', passed=True,
            sourceManifest=fact(O / 'source-manifest.json'), foundationR2Manifest=fact(OLD / 'source-manifest.json'),
            functionalR2Manifest=fact(R / 'functional-matrix-r2/source-manifest.json'), totalFrozenInputCount=913,
            managedProductInputCount=484, nativeSourceInputCount=48, csharpInputCount=765,
            allManagedProductHashesMatchFoundationR2AndFunctionalR2=True, allNativeHashesMatchFoundationR2AndFunctionalR2=True,
            allCSharpHashesMatchFoundationR2=False, matchingFoundationR2CSharpInputCount=764,
            onlyChangedCSharpInput=PROGRAM, foundationR2Deltas=changes_old, functionalR2Deltas=changes_functional,
            testDelta=fact(O / 'test-delta.json'), patch=fact(basis / 'initialization-root-free-links-from-r2.patch'),
            changes=[dict(path=p, before=old[p]['sha256'], after=current[p]['sha256']) for p in changes_old],
            cameraTestDeltaRetained=fact(OLD / 'basis/game-camera-current-metadata-negative.patch'))
        save(O / 'source-inheritance-proof.json', comparison)
        # Validate earlier actual results independently. Their terminal failures stay terminal failures.
        core_path, full_path = OLD / 'core-r2/test-result.json', OLD / 'full-r2/test-result.json'
        core, full = read(core_path), read(full_path)
        assert core['passed'] is True and core['runnerExitCode'] == 0 and core['child']['passed'] is True
        core_count = sum(core['child'][key] for key in ('coordinatorChecks', 'receiverChecks', 'audioChecks'))
        assert core_count == 1039
        for row in core['inputs'] + core['snapshotFiles'] + core['child']['inputs'] + core['child']['artifacts']:
            verify(row)
        assert full['passed'] is True and full['fullManagedMatrix'] is True and len(full['projects']) == 28
        assert all(p['passed'] is True and p['buildExitCode'] == p['testExitCode'] == 0 and p['checks'] > 0 for p in full['projects'])
        assert sum(p['checks'] for p in full['projects']) == 2599
        assert 'InitializationRootFree' not in full['requestedSuites']
        for row in full['inputs'] + full['compiledSources']:
            verify(row)
        compiled_cs = {Path(row['path']).relative_to(OLD / 'full-r2/source').as_posix(): row['sha256'] for row in full['compiledSources'] if Path(row['path']).suffix == '.cs'}
        assert len(compiled_cs) == 765
        assert [p for p in compiled_cs if compiled_cs[p] != current[p]['sha256']] == [PROGRAM]
        assert compiled_cs[PROGRAM] == old[PROGRAM]['sha256']
        for name in full['requestedSuites']:
            p = 'tests/RizToolbox.' + name + '.Tests/RizToolbox.' + name + '.Tests.csproj'
            assert current[p]['sha256'] == old[p]['sha256']
            # The only C# delta is outside all 28 default project directories,
            # and no actual matrix project/ProjectReference/Compile link names it.
            assert not PROGRAM.startswith(str(Path(p).parent).replace('\\', '/') + '/')
        for p in (S / 'src').rglob('*.csproj'):
            assert 'RizToolbox.InitializationRootFree.Tests' not in p.read_text(encoding='utf-8-sig')
        for p in (S / 'tests').rglob('*.csproj'):
            if p != S / CS:
                assert 'RizToolbox.InitializationRootFree.Tests' not in p.read_text(encoding='utf-8-sig')
        old_driver = read(OLD / 'foundation-driver.result.json')
        old_solution = read(OLD / 'solution-r2/test-result.json')
        assert old_driver['passed'] is False and old_driver['actualExitCode'] == 1
        assert old_solution['passed'] is False and old_solution['buildExitCode'] == 1 and old_solution['errors'] == 1
        assert not (OLD / 'sdk-r2.exit.txt').exists() and not (OLD / 'release-r2.exit.txt').exists()
        inherited = dict(kind='progress-resource-foundation-r7-retained-actual-regression-evidence', passed=True,
            core=fact(core_path), full=fact(full_path), coreChecks=1039, managedSuiteCount=28, managedChecks=2599,
            testsReexecuted=False, matchingCapturedCSharpInputs=764, onlyChangedCSharpInput=PROGRAM,
            changedProgramOutsideAll28ExecutedCompileClosures=True, all28ExecutedProjectFilesExactlyMatch=True,
            deltaProjectWasOutside28SuiteMatrix=True, sourceInheritance=fact(O / 'source-inheritance-proof.json'),
            fullProjects=full['projects'], preservedPriorDriverFailure=fact(OLD / 'foundation-driver.result.json'),
            preservedPriorSolutionFailure=fact(OLD / 'solution-r2/test-result.json'),
            priorSolutionActualExitCode=1, priorSdkState='not-started', priorReleaseState='not-started',
            preservedFoundationR3Failure=fact(R / 'foundation-r3/foundation-driver.result.json'),
            gameExecuted=False, productionAdmission=False)
        save(O / 'retained-r2-regression-evidence.json', inherited)
        result['inheritedRegressionEvidence'] = fact(O / 'retained-r2-regression-evidence.json')
        copy_source(B, rows)
        environment = O / 'environment'
        packages = environment / 'packages'
        local_feed = environment / 'local-feed'
        local_feed.mkdir(parents=True)
        seed = read(OLD / 'reference-package-seed-r2.json')
        assert seed['passed'] is True and seed['packageCount'] == 3
        package_rows = []
        for row in seed['files']:
            verify(row)
            p = Path(row['path'])
            target = packages / p.relative_to(OLD / 'foundation-environment/packages')
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(p, target)
            assert sha(target) == row['sha256']
            package_rows.append(fact(target))
            if p.suffix == '.nupkg':
                shutil.copy2(p, local_feed / p.name)
        assert len(list(local_feed.glob('*.nupkg'))) == 3
        save(O / 'reference-package-seed-r7.json', dict(kind='existing-local-reference-package-copies', passed=True,
            source=fact(OLD / 'reference-package-seed-r2.json'), packageCount=3, files=package_rows,
            localFeed=[fact(p) for p in sorted(local_feed.glob('*.nupkg'))], downloaded=False, installed=False))
        config = '<configuration><packageSources><clear/><add key="OfflineReferencePackages" value="' + str(local_feed) + '"/></packageSources></configuration>'
        (O / 'NuGet.Config').write_text(config, encoding='utf-8')
        (B / 'NuGet.Config').write_text(config, encoding='utf-8')
        env = os.environ.copy()
        env.update(PATH='C:/Program Files/dotnet;' + env.get('PATH', ''), DOTNET_CLI_HOME=str(environment / 'dotnet-home'),
            NUGET_PACKAGES=str(packages), NUGET_HTTP_CACHE_PATH=str(environment / 'http-cache'), TEMP=str(environment / 'temp'), TMP=str(environment / 'temp'),
            DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_NOLOGO='1', DOTNET_CLI_UI_LANGUAGE='en', MSBUILDDISABLENODEREUSE='1',
            DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1', NuGetAudit='false')
        for key in ('DOTNET_CLI_HOME', 'NUGET_HTTP_CACHE_PATH', 'TEMP'):
            Path(env[key]).mkdir(parents=True, exist_ok=True)
        result['offlineConfig'] = fact(O / 'NuGet.Config')
        inputs = O / 'root-free-inputs'
        inputs.mkdir()
        ctor = R / 'fixture-agent-build-v1'
        ctor_proof = read(ctor / 'build-result.json')
        assert ctor_proof['passed'] is True
        original_fixture = ctor / 'RizToolbox.PlaybackStockFixture.dll'
        assert ctor_proof['sourceRoles'] == 19 and ctor_proof['physicalEntries'] == 20
        assert sha(original_fixture) == '4a76ea8f8a4bda5f8fde7b411ddb602085a2b99f95fd1883d0cce30febb32d1d'
        source_inputs = [original_fixture] + [R / 'functional-matrix-r2/inputs' / name for name in ('RizToolbox.SingleReturn.Prototype.dll', 'RizToolbox.PlaybackSourcePrototype.dll', 'dobby.dll')]
        copied = []
        for original in source_inputs:
            target = inputs / original.name
            shutil.copy2(original, target)
            assert sha(target) == sha(original)
            copied.append(dict(original=fact(original), isolated=fact(target)))
        assert sha(source_inputs[2]) == 'ef24415492cd31333a692ef689f2dac5c34c7ca7a58a6b771316ceb0dc5d353c'
        save(O / 'root-free-inputs.json', dict(kind='root-free-current-r6-helper-exact-resource-fixture-inputs', passed=True,
            files=copied, resourceFixtureBuildProof=fact(ctor / 'build-result.json'),
            fixtureSha256CheckUnmodified=True, fixtureFamily='current-progress-resource-19-source-plus-GetTime',
            resourceFixtureUsed=True, currentR6Helper=True, expectedPhysicalMembers=20, fullResourceFunctionalVerificationClaimed=False))
        project = B / CS
        build = run('root-free-build-r7', [DOTNET, 'build', project, '-c', 'Release', '-m:1', '--configfile', O / 'NuGet.Config',
            '-p:UseSharedCompilation=false', '-p:NuGetAudit=false', '-p:RizEngineProductionProfileSource=' + str(PROFILE), '--nologo', '-v:minimal'], env, result)
        bin_dir = project.parent / 'bin/Release/net8.0'
        retained_products = O / 'root-free-managed-products'
        retained_products.mkdir()
        for p in bin_dir.iterdir():
            if p.is_file():
                shutil.copy2(p, retained_products / p.name)
        build_products = products(retained_products, O / 'root-free-build-products.json')
        tests = dict(kind='foundation-r7-affected-initialization-root-free-checks', passed=False, build=fact(O / 'root-free-build-r7/process-result.json'),
            sourceManifest=fact(O / 'source-manifest.json'), inputs=fact(O / 'root-free-inputs.json'), buildProducts=fact(O / 'root-free-build-products.json'), rows=[],
            gameExecuted=False, productionAdmission=False)
        prefix_delta_path = O / 'r5-prefix-expectation-delta.json'
        prefix_delta = read(prefix_delta_path)
        assert prefix_delta['passed'] is True and prefix_delta['onlyChangeIsExactFixedExceptionPrefix'] is True
        verify(prefix_delta['before'])
        verify(prefix_delta['after'])
        verify(prefix_delta['exceptionConstructor'])
        verify(prefix_delta['independentIncrementalReview'])
        verify(prefix_delta['independentIncrementalReviewManifest'])
        assert prefix_delta['changedHelperOnlyCalledByResourceFaultMode'] is True
        for inherited_case in prefix_delta['inheritedFourCases']:
            verify(inherited_case['process'])
            prior_process = read(Path(inherited_case['process']['path']))
            assert prior_process['actualExitCode'] == 0 and prior_process['passed'] is True
            verify(prior_process['stdout'])
            verify(prior_process['stderr'])
            prior_actual = json.loads(Path(prior_process['stdout']['path']).read_text(encoding='utf-8-sig').strip().splitlines()[-1])
            assert prior_actual == inherited_case['actual'] and prior_actual['passed'] is True
            assert inherited_case['mode'] in ('host-unclaimed-free-fault', 'host-router-free-fault', 'tracker-progress-free-fault', 'tracker-enumerator-free-fault')
            tests['rows'].append(dict(**inherited_case, inherited=True, actualExecutionScope='foundation-r5', reexecuted=False))
        assert len(tests['rows']) == 4 and sum(row['actual']['checks'] for row in tests['rows']) == 80
        tests['inheritedFourCaseProof'] = fact(prefix_delta_path)
        save(O / 'root-free-test-result.json', tests)
        for mode in MODES:
            row = run(mode, [DOTNET, retained_products / 'RizToolbox.Gameplay.Tests.dll', mode, *[inputs / p.name for p in source_inputs]], env, result)
            actual = json.loads((O / mode / 'stdout.log').read_text(encoding='utf-8-sig').strip().splitlines()[-1])
            assert actual['passed'] is True and actual['mode'] == mode and actual['checks'] > 0
            assert actual['gameExecuted'] is False and actual['productionAdmission'] is False
            assert actual['cleanupCompleted'] is False and actual['retainedUnknown'] is True
            assert actual['completeUnknownContextRetained'] is True and actual['backingBuffersDisposed'] is False
            if mode == 'tracker-resource-free-fault':
                assert actual['resourceUnknownRetained'] is True and actual['hostRootsRetained'] is True and actual['ActiveRoots'] == 4
            elif mode.startswith('tracker-'):
                assert actual['resourceUnknownRetained'] is False and actual['hostRootsRetained'] is False and actual['ActiveRoots'] == 0
            tests['rows'].append(dict(mode=mode, process=fact(O / mode / 'process-result.json'), actual=actual,
                inherited=False, actualExecutionScope='foundation-r7', reexecuted=True))
            save(O / 'root-free-test-result.json', tests)
        for row in build_products:
            verify(row)
        tests.update(passed=True, actualCaseCount=5, freshProcesses=1, inheritedProcesses=4,
            freshChecks=sum(row['actual']['checks'] for row in tests['rows'] if row['inherited'] is False),
            inheritedChecks=80, checks=sum(row['actual']['checks'] for row in tests['rows']),
            cleanupCompleted=False, resourceUnknownRetainsHostRoots=True, resourceUnknownRetainedHostRoots=4,
            noNativeFreeReplay=True, productionAdmission=False)
        save(O / 'root-free-test-result.json', tests)
        result['affectedTestProof'] = fact(O / 'root-free-test-result.json')
        result.update(freshAffectedCaseCount=1, inheritedAffectedCaseCount=4, inheritedAffectedChecks=80,
            freshAffectedChecks=tests['freshChecks'])
        symbols = run('root-free-symbols-r7', [PWSH, '-NoProfile', '-File', O / 'Verify-ManagedSymbolsR7.ps1', '-Stage', 'RootFree'], env, result)
        result['affectedManagedSymbolProof'] = fact(O / 'symbols-rootfree-r7/test-result.json')
        assert check_source(B, rows)
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
        symbols = run('release-symbols-r7', [PWSH, '-NoProfile', '-File', O / 'Verify-ManagedSymbolsR7.ps1', '-Stage', 'Release'], env, result)
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
        (O / 'foundation-driver.exit.txt').write_text('0' if result['passed'] else '1', encoding='utf-8')
        save(O / 'foundation-driver.result.json', result)
    return 0 if result['passed'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
