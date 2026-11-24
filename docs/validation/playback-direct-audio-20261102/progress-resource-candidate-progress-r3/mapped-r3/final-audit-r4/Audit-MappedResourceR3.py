"""Read-only evidence audit. Writes only to this audit directory.

Does not build, execute tests, load game images, terminate, or restart jobs.
The process snapshot is collected separately through Win32_Process.
"""
from __future__ import annotations

from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import subprocess

A = Path(__file__).resolve().parent
M = A.parent
U = M.parent
N = U / 'native-agent-finish-review-r6-20261103'
HELPER = 'ef24415492cd31333a692ef689f2dac5c34c7ca7a58a6b771316ceb0dc5d353c'
EXPECTED = {
    'source-manifest.json': '9f0f3e47b57350248cfaf00251b0c07e4bd96fbb6f608b270c59c9285c82fe82',
    'scope.json': '4c98191ba3c6a1a95c9e8c3fe259c9ddd1fc0c6a2ebef2405f4f234056bc6db6',
    'runner': '36f033796ab1fce6031ab5e59315e817b2a2c66c7ad89a209044921011451da0',
    'native-manifest': '160fa37fc4ea783ef153ac51ec35430ab621b934ee0f20f6d3d692d72cdf6f5d',
}
ISSUES: list[dict] = []
WARNINGS: list[dict] = []
FACTS: dict[str,dict] = {}

def now():
    return datetime.now(timezone.utc).isoformat()

def load(path):
    path = Path(path)
    raw = path.read_bytes()
    FACTS[str(path)] = dict(path=str(path), sha256=hashlib.sha256(raw).hexdigest(), bytes=len(raw))
    return json.loads(raw.decode('utf-8-sig'))

def fact(path):
    path = Path(path)
    raw = path.read_bytes()
    result = dict(path=str(path), sha256=hashlib.sha256(raw).hexdigest(), bytes=len(raw))
    FACTS[str(path)] = result
    return result

def check(ok, code, detail):
    if not ok:
        ISSUES.append(dict(code=code, detail=detail))
    return bool(ok)

def inside(root, rel):
    result = (root / rel).resolve()
    if not result.is_relative_to(root.resolve()):
        raise ValueError('Outside audited root: ' + str(result))
    return result

def verify(row, path=None):
    path = Path(path or row['path'])
    if not path.is_file():
        return check(False, 'missing-file', str(path))
    actual = fact(path)
    return check(actual['sha256'] == row['sha256'] and actual['bytes'] == row['bytes'], 'file-fact-mismatch', str(path))

def archived(row):
    rel = Path(row['path']).relative_to(U)
    return inside(N / 'artifacts', rel)

def source_inventory(root):
    return {p.relative_to(root).as_posix() for p in root.rglob('*')
            if p.is_file() and not set(p.relative_to(root).parts) & {'bin','obj','.git','__pycache__'}}

def audit_native():
    before = len(ISSUES)
    manifest = load(N / 'hash-manifest.json')
    check(FACTS[str(N / 'hash-manifest.json')]['sha256'] == EXPECTED['native-manifest'], 'native-manifest-identity', 'Expected final manifest including timezone correction history')
    rows = manifest['files']
    paths = [r['path'] for r in rows]
    check(len(rows) == len(set(paths)) == 275, 'native-manifest-count', len(rows))
    inventory = {p.relative_to(N).as_posix() for p in N.rglob('*') if p.is_file() and p != N / 'hash-manifest.json'}
    check(inventory == set(paths), 'native-manifest-inventory', sorted(inventory ^ set(paths)))
    for row in rows:
        verify(row, inside(N, row['path']))

    builds = []
    for name in ['native-agent-build-r6','native-agent-build-r6-fixture-r2']:
        root = N / 'artifacts' / name
        data = load(root / 'native-build-result.json')
        check(data['passed'] is True and data['actualExitCode'] == 0 and (root / 'build.exit.txt').read_text().strip() == '0', 'native-build-exit', name)
        check((data['sourceVersion'],data['frameBytes'],data['resourceBytes']) == (4,168,112), 'native-build-wire', name)
        check(len(data['sources']) == 10 and len(data['files']) == 20, 'native-build-file-count', name)
        for row in data['sources'] + data['files']:
            verify(row, archived(row))
        built_helper = fact(root / 'RizToolbox.PlaybackSourcePrototype.dll')['sha256']
        if name == 'native-agent-build-r6':
            check(built_helper == HELPER, 'native-build-helper', name)
        builds.append(dict(name=name,actualExitCode=data['actualExitCode'],sources=len(data['sources']),files=len(data['files']),builtHelperSha256=built_helper,selectedAsTestHelper=name=='native-agent-build-r6'))

    run_names = ['native-agent-retry-short-r6','native-agent-responsibility-short-r6','native-agent-responsibility-host-r6-fixture-r2-resume-20261103']
    runs = {}
    for name in run_names:
        root = N / 'artifacts' / name
        data = load(root / 'native-retry-result.json')
        check(data['helper']['sha256'] == HELPER, 'native-run-helper', name)
        check(all(data[k] is True for k in ['helperUnchanged','fixtureUnchanged','sourceInputsUnchanged']), 'native-run-input-guard', name)
        check(all(data[k] is False for k in ['gameExecuted','installed','productionAdmission','singleReturnReceiptEpochAcceptance']), 'native-run-scope', name)
        for key in ['helper','fixture','fixtureSource','runnerSource']:
            verify(data[key], archived(data[key]))
        for row in data['cases']:
            for key in ['stdout','stderr']:
                verify(row[key], archived(row[key]))
            text = archived(row['stdout']).read_text(encoding='utf-8-sig')
            summary = next(json.loads(line) for line in reversed(text.splitlines()) if line.startswith('{'))
            exit_code = int((root / (row['mode'] + '.exit.txt')).read_text())
            check(summary == row['summary'] and exit_code == row['actualExitCode'], 'native-actual-summary-exit', name + '/' + row['mode'])
            check(summary['sourceWireBytes'] == 168 and summary['resourceWireBytes'] == 112 and summary['gameExecuted'] is False, 'native-summary-scope', name + '/' + row['mode'])
            check(row['passed'] == (exit_code == 0 and summary['passed'] is True and summary['failures'] == 0), 'native-derived-pass', name + '/' + row['mode'])
        runs[name] = data
    old, marker, host = [runs[n] for n in run_names]
    failures = [r for r in marker['cases'] if not r['passed']]
    check(len(old['cases']) == 46 and old['passed'] is True and all(r['passed'] for r in old['cases']) and sum(r['summary']['checks'] for r in old['cases']) == 7969, 'native-old46', len(old['cases']))
    check(len(marker['cases']) == 10 and marker['passed'] is False and len(failures) == 1 and failures[0]['mode'] == 'host-responsibility-closed' and failures[0]['actualExitCode'] == 1 and failures[0]['summary']['checks'] == 216 and failures[0]['summary']['failures'] == 2, 'native-retained-failure', failures)
    check(len(host['cases']) == 1 and host['passed'] is True and host['cases'][0]['mode'] == 'host-responsibility-closed' and host['cases'][0]['passed'] is True and host['cases'][0]['summary']['checks'] == 217, 'native-host-resume', len(host['cases']))
    selected_marker = [host['cases'][0] if r['mode'] == 'host-responsibility-closed' else r for r in marker['cases']]
    selected = old['cases'] + selected_marker
    total_checks = sum(r['summary']['checks'] for r in selected)
    check(len(selected) == 56 and total_checks == 24806 and all(r['passed'] and r['actualExitCode'] == 0 and r['summary']['failures'] == 0 for r in selected), 'native-selected-total', dict(modes=len(selected),checks=total_checks))
    source_audit = load(N / 'source-audit.json')
    check(len(source_audit['helperInputs']) == 7,'native-helper-input-count',len(source_audit['helperInputs']))
    check(source_audit['selectedMarkerCoverage']['allTenRerunOnFixtureR2'] is False and source_audit['old46']['repeated'] is False, 'native-selection-not-rerun', 'Preserved historical successful runs plus one host replacement')
    for row in source_audit['selectedMarkerCoverage']['casesDetail']:
        data = runs[row['selectedRun']]
        chosen = next(r for r in data['cases'] if r['mode'] == row['mode'])
        check(row['summary'] == chosen['summary'] and row['actualExitCode'] == chosen['actualExitCode'] == 0 and row['checks'] == chosen['summary']['checks'] and row['helperSha256'] == HELPER and row['fixtureSha256'] == data['fixture']['sha256'], 'native-selected-source', row['mode'])
    for row in source_audit['helperInputs']:
        helper_hashes = {row[k]['sha256'] for k in ['live','r6Frozen','fixtureR2Frozen','functionalFrozen'] if row.get(k)}
        check(len(helper_hashes) == 1,'native-helper-source-equivalence',row['relativePath'])
        for key in ['live','r6Frozen','fixtureR2Frozen','functionalFrozen']:
            if row.get(key):
                verify(row[key])
                verify(row[key],archived(row[key]))
    fixture_suffix = 'frozen-source/native/RizToolbox.PlaybackSourcePrototype/ProgressResourceNativeFixture.cpp'
    fixture_before = (N/'artifacts/native-agent-build-r6'/fixture_suffix).read_text(encoding='utf-8-sig')
    fixture_after = (N/'artifacts/native-agent-build-r6-fixture-r2'/fixture_suffix).read_text(encoding='utf-8-sig')
    fixture_expected = fixture_before.replace(
        '        CHECK(frameCount == 4 && responsibilityBefore == 0); CheckNoJournalFrames();',
        '        // A host watch has no player/audio roots before the actual Start;\n'
        '        // those two originals execute transparently with no source frame.\n'
        '        const unsigned ordinaryFrames = hostReceipt ? 2u : 4u;\n'
        '        CHECK(pitches == 1 && setClips == 1);\n'
        '        CHECK(frameCount == ordinaryFrames && responsibilityBefore == 0); CheckNoJournalFrames();',
    ).replace(
        '        CHECK(responsibilityReceipts == 3 && responsibilityNoJournal == 5);',
        '        CHECK(responsibilityReceipts == 3 && responsibilityNoJournal == ordinaryFrames + 1);',
    )
    check(fixture_before != fixture_after and fixture_after == fixture_expected,'native-fixture-correction-allowlist','Only the host/nonhost ordinary-frame and no-journal expected counts plus originals assertion may change.')
    runner_before = (N/'artifacts/native-agent-responsibility-short-r6/Test-ProgressResourceNativeRetry.py').read_text(encoding='utf-8-sig')
    runner_after = (N/'artifacts/source/native/RizToolbox.PlaybackSourcePrototype/Test-ProgressResourceNativeRetry.py').read_text(encoding='utf-8-sig')
    check(runner_before != runner_after and runner_after == runner_before.replace('"closed": (3, 3, 5, 0, 0),','"closed": (3, 3, 3 if mode.startswith("host-") else 5, 0, 0),'),'native-runner-correction-allowlist','Only the host/nonhost no-journal expected count may change.')
    final_source_check = load(N / 'review-source-final-check.json')
    for row in final_source_check['rows']:
        actual = fact(row['path'])
        check(actual['sha256'] == row['expectedSha256'] == row['actualSha256'], 'native-current-source-identity', row['path'])

    evidence = load(N / 'evidence.json')
    findings = load(N / 'findings.json')
    paths_data = load(N / 'paths.json')
    eids, fids, pids = [{r['id'] for r in data} for data in [evidence,findings,paths_data]]
    check((len(evidence),len(findings),len(paths_data)) == (9,4,2) and (len(eids),len(fids),len(pids)) == (9,4,2), 'native-graph-counts', [len(evidence),len(findings),len(paths_data)])
    for e in evidence:
        check(all(e.get(k) for k in ['id','title','source_type','source_ref','artifact_path','content_hash','repro_command','observed_at']), 'native-evidence-fields', e['id'])
        check(fact(inside(N,e['artifact_path']))['sha256'] == e['content_hash'], 'native-evidence-hash', e['id'])
    for f in findings:
        check(bool(f['evidence_ids']) and set(f['evidence_ids']) <= eids and (f['status'] != 'validated' or f['confidence'] == 'high'), 'native-finding-reference', f['id'])
    for p in paths_data:
        check(p['path_type'] in ['solve','callflow','attack'] and bool(p['steps']), 'native-path-kind', p['id'])
        for step in p['steps']:
            check(step['evidence'] in eids and step['finding'] in fids, 'native-path-reference', p['id'])
            linked_finding = next((f for f in findings if f['id'] == step['finding']),None)
            check(linked_finding and step['evidence'] in linked_finding['evidence_ids'],'native-path-finding-evidence-alignment',p['id'])
    report = (N / '2026-11-03_native-resource-r6-and-bit256-report.md').read_text(encoding='utf-8-sig')
    report_eids = {eid for eid in eids if eid in report}
    check(eids <= report_eids, 'native-report-evidence-coverage', sorted(eids - report_eids))
    check('07:41:31' in (N / 'timeline.md').read_text(encoding='utf-8-sig'), 'native-timezone-correction', 'Host time must be 07:41:31 Asia/Shanghai')
    return dict(passed=len(ISSUES)==before,manifest=FACTS[str(N/'hash-manifest.json')],verifiedFiles=len(rows),builds=builds,selectedModes=len(selected),selectedChecks=total_checks,selectedFailures=sum(r['summary']['failures'] for r in selected),retainedHistoricalFailedModes=len(failures),retainedHistoricalFailures=sum(r['summary']['failures'] for r in failures),fullRerun=False,helperSourceInputsEquivalent=7,fixtureAndRunnerCorrectionAllowlistChecked=True,evidenceRecords=len(evidence),findings=len(findings),paths=len(paths_data),staticSemanticsReperformed=False,genericCaseReview='Not PASS: Markdown-only schema is incompatible with this existing JSON-layout package; actual JSON references and artifact hashes independently checked.')

def audit_mapped():
    before = len(ISSUES)
    data = load(M / 'test-result.json')
    manifest = load(M / 'source-manifest.json')
    scope = load(M / 'scope.json')
    for key in ['source-manifest.json','scope.json']:
        check(FACTS[str(M/key)]['sha256'] == EXPECTED[key], 'mapped-identity', key)
    check(fact(U/'Run-MappedResourceR3.py')['sha256'] == fact(M/'Run-MappedResourceR3.py')['sha256'] == data['runner']['sha256'] == EXPECTED['runner'], 'mapped-runner-identity', 'Original launched runner and archived copy')
    rows = manifest['files']
    paths = {r['path'] for r in rows}
    check(len(rows) == len(paths) == 913, 'mapped-frozen-source-count', len(rows))
    source_issue_start = len(ISSUES)
    for row in rows:
        verify(row,inside(M/'source',row['path']))
    check(source_inventory(M/'source') == paths, 'mapped-frozen-inventory', sorted(source_inventory(M/'source') ^ paths))
    frozen_sources_unchanged = len(ISSUES) == source_issue_start
    old = load(U/'functional-matrix-r2/source-manifest.json')
    old_files = {r['path']:(r['sha256'],r['bytes']) for r in old['files']}
    new_files = {r['path']:(r['sha256'],r['bytes']) for r in rows}
    check(old_files == new_files, 'mapped-functional-source-equivalence', 'Compare all frozen paths, hashes, and sizes')
    live_changed = []
    for row in rows:
        path = inside(U/'source',row['path'])
        if not path.is_file() or fact(path)['sha256'] != row['sha256']:
            live_changed.append(row['path'])
    live_extra = sorted(source_inventory(U/'source') ^ paths)
    authorized_test_delta_paths = {'tests/RizToolbox.GameCamera.Tests/NativeCameraChecks.cs','tests/RizToolbox.InitializationRootFree.Tests/RizToolbox.InitializationRootFree.Tests.csproj'}
    non_test_differences = sorted(p for p in set(live_changed) | set(live_extra) if not p.startswith('tests/'))
    check(not non_test_differences, 'mapped-current-U-non-test-source-difference', non_test_differences)
    if live_changed or live_extra:
        WARNINGS.append(dict(code='later-current-U-test-deltas',paths=live_changed,pathDifferences=live_extra,knownRootAuthorizedExactDeltaPaths=sorted(set(live_changed)&authorized_test_delta_paths),additionalObservedTestDeltaPaths=sorted(set(live_changed)-authorized_test_delta_paths),source='Root task message confirms Camera assertions and RootFree Compile-link corrections. All later test deltas are recorded separately; this audit accepts only frozen mapped-r3 source/build, and does not accept current versions of the changed tests.',productSourceDifferences=[p for p in live_changed if p.startswith(('src/','native/'))]))
    products = load(M/'build-products.json')
    for row in products:
        verify(row)
    inputs = load(M/'native-inputs.json')
    for row in inputs:
        verify(row['origin'])
        verify(row['copy'])
        check(row['origin']['sha256'] == row['copy']['sha256'] == row['expectedSha256'], 'mapped-input-identity', row['copy']['path'])
    check(inputs[-1]['expectedSha256'] == HELPER, 'mapped-helper-version', inputs[-1]['expectedSha256'])
    for entry in scope['mappingEntryPoints']:
        verify(entry)
        check('LoadLibraryEx(path, IntPtr.Zero, 1)' in Path(entry['path']).read_text(encoding='utf-8-sig'), 'mapped-DONT_RESOLVE-entry', entry['path'])
    check(scope['imageMapFlag'] == 1 and scope['maxIndependentTestProcesses'] == 3 and scope['killTimeout'] is None and all(scope[k] is False for k in ['invokeGameMethods','executeGameMethods','dllMainExecuted','gameUiAccessed','accountAccessed','currentMainModified','productSourcesModified']), 'mapped-scope-boundary', 'Offline DONT_RESOLVE only')
    snapshot = load(A/'process-snapshot.json')
    processes = {p['processId']:p for p in snapshot['processes']}
    pending, completed, not_launched, process_checks, completed_evidence = [], [], [], [], []
    initial_times = {15824:'2026-11-03T07:53:33.210550+08:00',3900:'2026-11-03T07:54:03.827725+08:00',11292:'2026-11-03T07:54:03.833380+08:00',38568:'2026-11-03T07:54:13.230465+08:00'}
    prior_joint_identities = []
    prior_audits = sorted((A/'history').glob('*pending-audit.json')) + [A/'initial-audit-with-reviewer-assumption-errors.json',A/'pending-audit.json']
    for prior_path in prior_audits:
        if not prior_path.is_file():
            continue
        prior = json.loads(prior_path.read_text(encoding='utf-8-sig')).get('mappedR3',{})
        for prior_row in prior.get('liveProcessIdentityChecks',[]):
            if prior_row.get('identityVerified') is True:
                prior_joint_identities.append(dict(**prior_row,evidenceAudit=str(prior_path),processSnapshotUtc=prior.get('processSnapshotUtc')))
    verified_joint_identities = []
    for row in data['rows']:
        name = row['name']
        if row['actualExitCode'] is None:
            pending.append(name)
            launch = load(M/(name+'.launch.json'))
            check(launch == row, 'mapped-launch-row-match', name)
            p = processes.get(row['pid'])
            matched = bool(p and p['parentProcessId'] == data['runnerPid'] and p['name'] == 'dotnet.exe' and p['commandLine'] == subprocess.list2cmdline(row['command']))
            if p and row['pid'] in initial_times:
                matched &= datetime.fromisoformat(p['creationDate']) == datetime.fromisoformat(initial_times[row['pid']])
            elif p:
                matched &= abs((datetime.fromisoformat(p['creationDate'])-datetime.fromisoformat(row['startedAtUtc'])).total_seconds()) < 10
            check(matched,'mapped-pending-process-identity',dict(name=name,pid=row['pid']))
            process_checks.append(dict(name=name,pid=row['pid'],identityVerified=matched,creationDate=p['creationDate'] if p else None,commandLine=p['commandLine'] if p else None))
            check(not (M/(name+'.exit.txt')).exists() and not (M/(name+'.result.json')).exists(),'mapped-pending-exit-absence',name)
            continue
        completed.append(name)
        check(row['actualExitCode'] == 0 and row['passed'] is True and (M/(name+'.exit.txt')).read_text().strip() == '0','mapped-completed-actual-exit',name)
        check(load(M/(name+'.result.json')) == row,'mapped-completed-result-match',name)
        for key in ['stdout','stderr']:
            verify(row[key])
        text = Path(row['stdout']['path']).read_text(encoding='utf-8-sig').splitlines()
        checks = None
        if name == 'build':
            check('    0 Warning(s)' in text and '    0 Error(s)' in text,'mapped-fresh-build-clean',name)
        elif name == 'metadata':
            check(sum(line.startswith('PASS ') for line in text) == 149 and text[-1] == '149/149 stock metadata/code checks passed. Game code calls=0; DllMain calls=0; native object allocations=0.','mapped-metadata-149',name)
            checks = 149
        else:
            actual = next(json.loads(line) for line in reversed(text) if line.startswith('{'))
            check(actual == row['actual'],'mapped-joint-stdout-summary',name)
            expected = dict(kind='mapped-playback-stock-joint-profile',mode=name.removeprefix('joint-'),checks=41 if name=='joint-normal' else 45,passed=True,gameCodeExecuted=False,dllMainExecuted=False,gameLaunched=False,productionAdmission=False,actualDobbyPrepareCount=20,actualDobbyCommitCount=20,physicalMemberCount=20,sourceMemberCount=19,resourceRelocatedBytes=8,getTimeOriginalCalls=0,sourceBefore=0,sourceAfter=0,sourceEntered=0,sourceExited=0,metadataSynthetic=True,syntheticGraph=True,nativeAuthorityActiveAfterTermination=False,groupActiveAfterTermination=False,getTimeActiveAfterTermination=False,pitchActiveAfterTermination=False,sourceHealthSampleBeforeTermination=True,foundationEpoch=0,foundationOriginalCount=0,foundationWriteCount=0,initialRevision=1,commitRevision=2,restoredRevision=3)
            check(all(actual.get(k) == v for k,v in expected.items()),'mapped-joint-acceptance-fields',name)
            checks = actual.get('checks')
            matches = [p for p in prior_joint_identities if p['name']==name and p['pid']==row['pid'] and p['commandLine']==subprocess.list2cmdline(row['command'])]
            check(bool(matches),'mapped-completed-prior-live-identity',dict(name=name,pid=row['pid']))
            if matches:
                verified_joint_identities.append(matches[0])
                fact(matches[0]['evidenceAudit'])
        completed_evidence.append(dict(name=name,pid=row['pid'],actualExitCode=row['actualExitCode'],checks=checks,passed=row['passed'],finishedAtUtc=row['finishedAtUtc'],stdout=row['stdout'],stderr=row['stderr']))
    expected_names = ['build','metadata','joint-normal','joint-pitch-tail','joint-body','joint-consumer']
    not_launched = [n for n in expected_names if n not in {r['name'] for r in data['rows']}]
    driver_live = processes.get(data['runnerPid'])
    if pending or not_launched:
        driver_ok = bool(driver_live and driver_live['name'] == 'pythonw.exe' and driver_live['parentProcessId'] == 10632 and datetime.fromisoformat(driver_live['creationDate']) == datetime.fromisoformat(initial_times[15824]) and driver_live['commandLine'] == '"C:\\Python314\\pythonw.exe" "'+str(U/'Run-MappedResourceR3.py')+'"')
        check(driver_ok,'mapped-durable-driver-identity',data['runnerPid'])
        check(data.get('finishedAtUtc') is None and not (M/'driver.exit.txt').exists(),'mapped-no-premature-driver-close','Driver must remain incomplete while joint work is pending')
    live_tests = [p for p in processes.values() if p['name'] == 'dotnet.exe' and p['parentProcessId'] == data['runnerPid'] and str(M) in (p['commandLine'] or '')]
    check(len(live_tests) <= 3,'mapped-concurrency',len(live_tests))
    verified_joint_identities.extend(process_checks)
    terminal = not pending and not not_launched and bool(data.get('finishedAtUtc'))
    if terminal:
        check(data['passed'] is True and data['driverExitCode'] == 0 and (M/'driver.exit.txt').read_text().strip() == '0' and all(data[k] is True for k in ['frozenSourcesUnchanged','nativeInputsUnchanged','buildProductsUnchanged']),'mapped-driver-terminal-acceptance','All six actual results and final hash guards required')
    accepted = terminal and len(ISSUES)==before
    closure_reason = 'All actual exits, summaries and final hash guards validated' if accepted else 'Terminal evidence needs resolution; see issues.' if terminal else 'Joint results remain pending; live jobs left intact. No acceptance inferred from elapsed time or previous metadata-r2 runs.'
    return dict(integrityPassed=len(ISSUES)==before,completed=completed,completedEvidence=completed_evidence,completedChecks=sum(r['checks'] or 0 for r in completed_evidence),pending=pending,notLaunched=not_launched,terminal=terminal,accepted=accepted,acceptanceScope='Exact mapped-r3 frozen source and fresh Gameplay build only; excludes all later changes to tests outside the frozen Gameplay build.',previousMetadataR2UsedForAcceptance=False,mappedSourceScopeReview=dict(entry='NativePlaybackStockGroupProfileChecks.cs:30',modeList=['normal','pitch-tail','body','consumer'],foundationBranchEntered=False,terminalGuards='NativePlaybackStockGroupProfileChecks.cs:194-200 requires originalCalls/sourceEntered/sourceExited/forbiddenCallbacks/sourceBefore/sourceAfter/fixture invocations/object allocations all zero.',gameImageEntryMethodsInvoked=False,reviewMethod='Read frozen source; no dynamic invocation by auditor.'),frozenSourceFiles=len(rows),buildProductFiles=len(products),nativeInputFiles=len(inputs),sourceManifest=FACTS[str(M/'source-manifest.json')],scope=FACTS[str(M/'scope.json')],frozenSourcesUnchanged=frozen_sources_unchanged,currentUSourceEquivalent=not live_changed and not live_extra,currentUSourceChangedPaths=live_changed,currentUSourcePathDifferences=live_extra,currentProductSourceChangedPaths=[p for p in live_changed if p.startswith(('src/','native/'))],liveIndependentTestProcesses=len(live_tests),processSnapshotUtc=snapshot['observedAtUtc'],liveProcessIdentityChecks=process_checks,jointProcessIdentityEvidence=verified_joint_identities,driverPid=data['runnerPid'],resultSnapshot=FACTS[str(M/'test-result.json')],closureReason=closure_reason)

result = dict(kind='mapped-r3-and-native-r6-read-only-evidence-audit-r4',observedAtUtc=now(),gameCodeExecuted=False,gameDllMainExecuted=False,gameLaunched=False,gameUiAccessed=False,accountAccessed=False,testsStarted=0,buildsStarted=0,killsPerformed=False,restartsPerformed=False,sourceWritesPerformed=False,nativePackageWritesPerformed=False)
try:
    result['nativeR6'] = audit_native()
    result['mappedR3'] = audit_mapped()
except Exception as exc:
    ISSUES.append(dict(code='audit-exception',detail=repr(exc)))
result['issues'] = ISSUES
result['warnings'] = WARNINGS
result['evidenceIntegrityPassed'] = not ISSUES
result['mappedAcceptancePassed'] = bool(result.get('mappedR3',{}).get('accepted'))
result['finishedAtUtc'] = now()
result['auditedFacts'] = sorted(FACTS.values(),key=lambda r:r['path'])
target = A / ('completed-audit.json' if result['mappedAcceptancePassed'] else 'pending-audit.json')
target.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps({k:v for k,v in result.items() if k not in ['auditedFacts']},ensure_ascii=False,indent=2))
raise SystemExit(0 if result['evidenceIntegrityPassed'] else 1)
