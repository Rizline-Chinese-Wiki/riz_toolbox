"""Audit actual terminals, immutable source joins and PE/PDB/product receipts without executing tests."""
from pathlib import Path
import datetime
import hashlib
import json
import traceback

O = Path(__file__).resolve().parent
R = O.parent
S = O / 'source'
B = O / 'solution-source'
PROGRAM = 'tests/RizToolbox.InitializationRootFree.Tests/Program.cs'
CS = 'tests/RizToolbox.InitializationRootFree.Tests/RizToolbox.InitializationRootFree.Tests.csproj'
CAMERA = 'tests/RizToolbox.GameCamera.Tests/NativeCameraChecks.cs'
read = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
fact = lambda p: dict(path=str(p), sha256=sha(p), bytes=p.stat().st_size)
path_identity = lambda p: str(Path(p).resolve()).casefold()


def verify(row):
    p = Path(row['path'])
    assert p.is_file() and sha(p) == row['sha256'], 'Recorded bytes changed: ' + str(p)
    if 'bytes' in row:
        assert p.stat().st_size == row['bytes'], 'Recorded length changed: ' + str(p)


def reference(row, expected):
    verify(row)
    assert path_identity(row['path']) == path_identity(expected), 'Wrong evidence reference: ' + str(expected)


def process(path):
    row = read(path)
    assert isinstance(row['actualExitCode'], int) and row['pid'] > 0 and row['creationTimeUtc']
    for key in ('stdout', 'stderr'):
        verify(row[key])
    assert int((path.parent / 'exit.txt').read_text().strip()) == row['actualExitCode']
    launch = read(path.parent / 'launch.json')
    assert launch['command'] == row['command'] and launch['pid'] == row['pid'] and launch['creationTimeUtc'] == row['creationTimeUtc']
    return row


def passed(path, kind=None):
    value = read(path)
    assert value['passed'] is True, 'Evidence is not passing: ' + str(path)
    if kind:
        assert value['kind'] == kind
    return value


def symbols(path, expected_pairs, frozen):
    proof = passed(path, 'foundation-r7-actual-managed-pe-pdb-source-proof')
    assert proof['pePdbPairs'] == expected_pairs and proof['actualBuildExitCode'] == 0
    assert proof['sourceInputCount'] == 913 and proof['capturedCSharpInputCount'] == 765
    reference(proof['sourceManifest'], O / 'source-manifest.json')
    verify(proof['retainedBuildResult'])
    verify(proof['retainedBuildProducts'])
    verify(proof['verifier'])
    assert proof['actualCodeViewMatchesPortablePdb'] is True and proof['documentSha256MatchesSnapshot'] is True
    assert len(proof['images']) == expected_pairs and sum(image['documentCount'] for image in proof['images']) == proof['documentCount']
    for image in proof['images']:
        verify(image['assembly'])
        verify(image['pdb'])
        assert image['actualCodeViewMatchesPortablePdb'] is True and image['documentSha256MatchesSnapshot'] is True
        assert len(image['documents']) == image['documentCount']
        for document in image['documents']:
            if document['frozenInput']:
                assert document['relativePath'] in frozen and document['sha256'] == frozen[document['relativePath']]['sha256']
                assert sha(S / document['relativePath']) == document['sha256']
            else:
                assert document['externalProfile'] or document['generatedUnderSnapshotObj']
                verify(document['retainedSource'])
                assert document['retainedSource']['sha256'] == document['sha256']
    return proof


result = dict(kind='progress-resource-foundation-r7-final-terminal-evidence-audit', passed=False,
    evidenceIntegrityPassed=False, complete=False, auditedAtUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
    verifier=fact(Path(__file__).resolve()), testsExecutedByAudit=False, gameExecuted=False,
    installed=False, downloaded=False, productionAdmission=False, cleanupCompleted=False)
try:
    driver_path = O / 'foundation-driver.result.json'
    driver = read(driver_path)
    result['initialDriverActualPassed'] = driver['passed']
    result['driver'] = fact(driver_path)
    verify(driver['driver'])
    assert driver['inJob'] is False and driver['finalFreezeExplicit'] is True
    assert driver['gameExecuted'] is False and driver['installed'] is False and driver['productionAdmission'] is False
    os_exit = read(O / 'driver-os-exit-observation.json')
    assert os_exit['collectionCompleted'] is True and os_exit['inJob'] is False
    assert os_exit['driverPid'] == driver['runnerPid'] and os_exit['driverCreationTimeUtc'] == driver['creationTimeUtc']
    result['initialDriverActualOsExitCode'] = os_exit['actualExitCode']
    result['driverOsExitObservation'] = fact(O / 'driver-os-exit-observation.json')
    launch = read(O / 'wmi-driver.launch.json')
    assert launch['pid'] == driver['runnerPid'] and launch['actualParentPid'] == driver['parentPid']
    assert launch['returnedValue'] == 0 and launch['mechanism'] == 'Win32_Process.Create'
    assert launch['actualCommandLine'] == os_exit['driverCommand']
    assert datetime.datetime.fromisoformat(launch['creationDate']) == datetime.datetime.fromisoformat(driver['creationTimeUtc'])
    result['wmiLaunch'] = fact(O / 'wmi-driver.launch.json')
    assert int((O / 'foundation-driver.exit.txt').read_text().strip()) == os_exit['actualExitCode']
    assert driver['passed'] is False and os_exit['actualExitCode'] == 1
    assert len(driver['rows']) == 3 and all(row['actualExitCode'] == 0 for row in driver['rows'])
    assert 'FileNotFoundError' in driver['failure'] and 'symbols-rootfree-r7' in driver['failure']
    continuation1_path = O / 'foundation-continuation.result.json'
    continuation1 = read(continuation1_path)
    continuation1_os_path = O / 'continuation-os-exit-observation-r2.json'
    continuation1_os = read(continuation1_os_path)
    assert continuation1['passed'] is False and 'NameError' in continuation1['failure'] and "'config'" in continuation1['failure']
    assert continuation1_os['collectionCompleted'] is True and continuation1_os['actualExitCode'] == 1
    assert continuation1_os['driverPid'] == continuation1['runnerPid']
    assert continuation1_os['driverCreationTimeUtc'] == continuation1['creationTimeUtc']
    assert len(continuation1['rows']) == 1 and continuation1['rows'][0]['name'] == 'solution-r7' and continuation1['rows'][0]['actualExitCode'] == 0
    verify(continuation1['driver'])
    continuation2_path = O / 'continuation-r2.result.json'
    continuation2 = read(continuation2_path)
    continuation2_os_path = O / 'continuation-r2-os-exit-observation.json'
    continuation2_os = read(continuation2_os_path)
    assert continuation2_os['collectionCompleted'] is True and continuation2_os['actualExitCode'] == 0 and continuation2_os['inJob'] is False
    assert continuation2_os['driverPid'] == continuation2['runnerPid']
    assert continuation2_os['driverCreationTimeUtc'] == continuation2['creationTimeUtc']
    assert continuation2_os['driverParentPid'] == continuation2['parentPid']
    assert len(continuation2_os['command']) == len(continuation2['command']) == 2
    for command in (continuation2_os['command'], continuation2['command']):
        assert path_identity(command[0]) == path_identity('C:/Python314/pythonw.exe')
        assert path_identity(command[1]) == path_identity(O / 'Complete-SdkReleaseR7R2.py')
    assert continuation2['passed'] is True and continuation2['inJob'] is False
    assert continuation2['frozenSourcesUnchanged'] is True and continuation2['originSourcesUnchanged'] is True
    assert int((O / 'continuation-r2.exit.txt').read_text()) == 0
    verify(continuation2['runner']); verify(continuation2_os['supervisor']); verify(continuation2_os['driver'])
    verify(continuation2_os['stdout']); verify(continuation2_os['stderr'])
    combined_rows = driver['rows'] + continuation1['rows'] + continuation2['rows']
    result.update(initialDriverFailurePreserved=fact(driver_path),
        firstContinuationFailurePreserved=fact(continuation1_path), firstContinuationActualOsExitCode=1,
        firstContinuationOsExitObservation=fact(continuation1_os_path),
        remainingOnlyContinuation=fact(continuation2_path), remainingOnlyActualOsExitCode=0,
        remainingOnlyOsExitObservation=fact(continuation2_os_path),
        combinedSuccessfulStepCount=len(combined_rows),
        actualSteps=[dict(name=row['name'], actualExitCode=row['actualExitCode'], pid=row['pid'], creationTimeUtc=row['creationTimeUtc'],
            sourceRun=('initial-r7' if row in driver['rows'] else 'first-continuation-r7' if row in continuation1['rows'] else 'remaining-sdk-release-r7-r2')) for row in combined_rows])
    required = ['root-free-build-r7', 'tracker-resource-free-fault', 'root-free-symbols-r7', 'solution-r7', 'sdk-r7', 'release-r7', 'release-symbols-r7']
    result['notCompletedSteps'] = [name for name in required if not any(row['name'] == name and row['actualExitCode'] == 0 for row in combined_rows)]
    for row in combined_rows:
        actual = process(O / row['name'] / 'process-result.json')
        assert actual == row
    assert not result['notCompletedSteps'] and all(row['actualExitCode'] == 0 for row in combined_rows), 'Actual foundation child commands remain incomplete or failed.'
    assert [row['name'] for row in combined_rows] == required
    assert driver['frozenSourcesUnchanged'] is True and driver['originSourcesUnchanged'] is True
    reference(driver['sourceManifest'], O / 'source-manifest.json')
    manifest = read(O / 'source-manifest.json')
    frozen = {row['path']: row for row in manifest['files']}
    assert len(frozen) == len(manifest['files']) == 913
    assert sum(p.startswith('src/') for p in frozen) == 484 and sum(p.startswith('native/') for p in frozen) == 48
    assert sum(p.endswith('.cs') for p in frozen) == 765
    for row in manifest['files']:
        for root in (S, B, R / 'source'):
            assert sha(root / row['path']) == row['sha256']
    previous = {row['path']: row for row in read(R / 'foundation-r6/source-manifest.json')['files']}
    assert previous == frozen
    r2 = {row['path']: row for row in read(R / 'foundation-r2/source-manifest.json')['files']}
    functional = {row['path']: row for row in read(R / 'functional-matrix-r2/source-manifest.json')['files']}
    assert frozen.keys() == r2.keys() == functional.keys()
    assert sorted(p for p in frozen if frozen[p]['sha256'] != r2[p]['sha256']) == sorted([PROGRAM, CS])
    assert sorted(p for p in frozen if frozen[p]['sha256'] != functional[p]['sha256']) == sorted([PROGRAM, CS, CAMERA])
    assert all(frozen[p]['sha256'] == r2[p]['sha256'] == functional[p]['sha256'] for p in frozen if p.startswith(('src/', 'native/')))
    delta = passed(O / 'r5-prefix-expectation-delta.json')
    for key in ('before', 'after', 'exceptionConstructor', 'patch', 'independentIncrementalReview', 'independentIncrementalReviewManifest'):
        verify(delta[key])
    old_literal = b'"The resource root release previously failed and remains unresolved: Synthetic unknown GC-root free outcome."'
    new_literal = b'"UnsupportedSignature: The resource root release previously failed and remains unresolved: Synthetic unknown GC-root free outcome."'
    assert Path(delta['after']['path']).read_bytes() == Path(delta['before']['path']).read_bytes().replace(old_literal, new_literal)
    assert delta['changedHelperOnlyCalledByResourceFaultMode'] is True and delta['fourCasesReexecuted'] is False
    independent = read(Path(delta['independentIncrementalReview']['path']))
    assert independent['passed'] is True
    core_path, full_path = R / 'foundation-r2/core-r2/test-result.json', R / 'foundation-r2/full-r2/test-result.json'
    core, full = passed(core_path), passed(full_path)
    assert core['runnerExitCode'] == 0 and core['child']['passed'] is True
    assert sum(core['child'][k] for k in ('coordinatorChecks', 'receiverChecks', 'audioChecks')) == 1039
    assert full['fullManagedMatrix'] is True and len(full['projects']) == 28 and sum(p['checks'] for p in full['projects']) == 2599
    assert 'InitializationRootFree' not in full['requestedSuites']
    for p in full['projects']:
        assert p['passed'] is True and p['buildExitCode'] == p['testExitCode'] == 0
    for row in core['inputs'] + core['snapshotFiles'] + core['child']['inputs'] + core['child']['artifacts'] + full['inputs'] + full['compiledSources']:
        verify(row)
    for name in full['requestedSuites']:
        p = 'tests/RizToolbox.' + name + '.Tests/RizToolbox.' + name + '.Tests.csproj'
        assert frozen[p]['sha256'] == r2[p]['sha256']
        assert not PROGRAM.startswith(str(Path(p).parent).replace('\\', '/') + '/')
    for p in [*(S / 'src').rglob('*.csproj'), *(S / 'tests').rglob('*.csproj')]:
        if p != S / CS:
            assert 'RizToolbox.InitializationRootFree.Tests' not in p.read_text(encoding='utf-8-sig')
    affected = passed(O / 'root-free-test-result.json', 'foundation-r7-affected-initialization-root-free-checks')
    assert affected['actualCaseCount'] == len(affected['rows']) == 5 and affected['freshProcesses'] == 1 and affected['inheritedProcesses'] == 4
    assert affected['inheritedChecks'] == 80 and affected['checks'] == sum(row['actual']['checks'] for row in affected['rows'])
    fresh_cases = []
    inherited_cases = []
    for case in affected['rows']:
        verify(case['process'])
        p = process(Path(case['process']['path']))
        assert p['actualExitCode'] == 0
        actual = json.loads(Path(p['stdout']['path']).read_text(encoding='utf-8-sig').strip().splitlines()[-1])
        assert actual == case['actual'] and actual['passed'] is True
        assert actual['cleanupCompleted'] is False and actual['completeUnknownContextRetained'] is True and actual['backingBuffersDisposed'] is False
        assert actual['nativeFreeReplayed'] is False and actual['retainedUnknown'] is True
        if case['inherited']:
            assert case['actualExecutionScope'] == 'foundation-r5' and case['reexecuted'] is False
            inherited_cases.append(case)
        else:
            assert case['actualExecutionScope'] == 'foundation-r7' and case['mode'] == 'tracker-resource-free-fault'
            assert actual['resourceUnknownRetained'] is True and actual['hostRootsRetained'] is True and actual['ActiveRoots'] == 4
            fresh_cases.append(case)
    assert len(fresh_cases) == 1 and len(inherited_cases) == 4
    assert sum(case['actual']['checks'] for case in inherited_cases) == 80
    for row in read(O / 'root-free-build-products.json'):
        verify(row)
    for row in read(O / 'root-free-inputs.json')['files']:
        verify(row['original']); verify(row['isolated'])
    root_symbols = symbols(O / 'symbols-rootfree-r6/test-result.json', 5, frozen)
    solution = passed(O / 'solution-r7/test-result.json', 'foundation-r7-fresh-solution-build')
    assert solution['actualExitCode'] == 0 and solution['errors'] == 0 and solution['sourceHashesUnchanged'] is True
    for row in read(O / 'solution-r7/product-files.json'):
        verify(row['retained'])
        assert row['original']['sha256'] == row['retained']['sha256'] and row['original']['bytes'] == row['retained']['bytes']
    sdk = passed(O / 'sdk-r7/test-result.json', 'foundation-r7-actual-offline-sdk-distribution')
    assert sdk['actualExitCode'] == 0 and sdk['checks'] == 14 and sdk['generatedIndependentPlugins'] == 2
    verify(sdk['result'])
    sdk_actual = read(Path(sdk['result']['path']))
    assert sdk_actual['checksPassed'] == 14 and sdk_actual['runtimeGameVerification'] is False
    sdk_source = O / 'sdk-source-r2'
    assert path_identity(sdk['sdkSourceRoot']) == path_identity(sdk_source)
    package = sdk_source / 'artifacts/sdk/RizToolbox.Sdk.1.2.0.nupkg'
    assert sha(package) == sdk_actual['packageSha256']
    for row in sdk['products']:
        verify(row)
    original_sdk = (S / 'scripts/Test-SdkDistribution.ps1').read_text(encoding='utf-8-sig')
    actual_sdk = (sdk_source / 'scripts/Test-SdkDistribution.ps1').read_text(encoding='utf-8-sig')
    assert actual_sdk == original_sdk.replace('https://api.nuget.org/v3/index.json', str(O / 'environment/local-feed'))
    for row in manifest['files']:
        if row['path'] != 'scripts/Test-SdkDistribution.ps1':
            assert sha(sdk_source / row['path']) == row['sha256']
    release = passed(O / 'release-r7/managed-build-manifest.json', 'engine-transport-production-managed-candidate-build')
    assert release['buildExitCode'] == 0 and release['sourceHashesUnchanged'] is True and release['productionAdmissionGranted'] is False
    assert len(release['sources']) == 484 and len(release['files']) == 6
    assert {Path(row['path']).name for row in release['files']} == {'RizToolbox.' + name + '.dll' for name in ('Sdk','Core','Runtime','Adapter','Hosting','Loader')}
    for row in release['sources'] + release['files']:
        verify(row)
    for key in ('nativeBuild', 'generatedSource', 'legacyProfile', 'sourceManifest', 'process'):
        verify(release[key])
    native = passed(Path(release['nativeBuild']['path']))
    assert native['productionAdmissionGranted'] is False
    for row in native['files']:
        verify(row)
    release_symbols = symbols(O / 'symbols-release-r7/test-result.json', 6, frozen)
    prior_failures = []
    for scope in ('foundation-r1', 'foundation-r2', 'foundation-r3', 'foundation-r5', 'foundation-r6'):
        p = R / scope / 'foundation-driver.result.json'
        assert read(p)['passed'] is False
        prior_failures.append(fact(p))
    prior_camera = R / 'foundation-r1/full-r1/GameCamera-tests.log'
    assert prior_camera.read_text(encoding='utf-8-sig').strip().endswith('Game camera checks: 32/33 passed.')
    assert not (R / 'foundation-r4/wmi-driver.launch.json').exists()
    result.update(passed=True, evidenceIntegrityPassed=True, complete=True, frozenSourceCount=913,
        managedProductInputCount=484, nativeSourceInputCount=48, capturedCSharpInputCount=765,
        allProductInputsMatchFunctionalR2=True, functionalR2TestDeltas=[PROGRAM, CS, CAMERA],
        sourceManifest=fact(O / 'source-manifest.json'), inheritedCoreProof=fact(core_path), inheritedCoreChecks=1039,
        inheritedManagedMatrixProof=fact(full_path), inheritedManagedSuiteCount=28, inheritedManagedChecks=2599,
        inheritedCasesReexecuted=False, affectedCases=fact(O / 'root-free-test-result.json'),
        affectedActualCaseCount=5, freshAffectedCaseCount=1, freshAffectedChecks=affected['freshChecks'],
        inheritedAffectedCaseCount=4, inheritedAffectedChecks=80, affectedCombinedChecks=affected['checks'],
        resourceUnknownRetainsHostRoots=True, retainedHostRootCountInResourceUnknownCase=4,
        unknownNativeFreeReplayed=False, unknownBackingBuffersDisposed=False,
        solution=fact(O / 'solution-r7/test-result.json'), solutionWarnings=solution['warnings'], solutionErrors=0,
        sdk=fact(O / 'sdk-r7/test-result.json'), sdkChecks=14, independentlyGeneratedSdkPlugins=2,
        release=fact(O / 'release-r7/managed-build-manifest.json'), releaseAssemblyCount=6, releaseAssemblies=release['files'],
        rootFreeManagedSymbolProof=fact(O / 'symbols-rootfree-r6/test-result.json'), rootFreePePdbPairs=5, rootFreePdbDocuments=root_symbols['documentCount'],
        releaseManagedSymbolProof=fact(O / 'symbols-release-r7/test-result.json'), releasePePdbPairs=6, releasePdbDocuments=release_symbols['documentCount'],
        totalPePdbPairs=11, totalPdbDocuments=root_symbols['documentCount'] + release_symbols['documentCount'],
        preservedPriorActualFailures=prior_failures, priorCamera32of33FailurePreserved=fact(prior_camera),
        unlaunchedR4NotClaimedExecuted=True)
except BaseException:
    result['failure'] = traceback.format_exc()
(O / 'foundation-final-summary.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
print(json.dumps({k: v for k, v in result.items() if k not in {'releaseAssemblies'}}, indent=2))
raise SystemExit(0 if result['passed'] else 1)
