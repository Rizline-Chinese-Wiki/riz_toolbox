"""Close this T increment from six original successes plus 12 continuation exits.

This is a new two-run scope. It never changes the interrupted formal-r2 result,
the old observer, historical partial indexes, or any main-worktree file.
"""
import argparse
import json
import shutil
import sys
import traceback
from continuation_common import *

COMBINED = R / 'formal-sdk-combined-after-shutdown-r1'
CLOSURE = R / 'integration-closure-after-shutdown-r1'


def readiness():
    result = {'ready': False, 'expectedOriginalCompleted': 6,
              'expectedContinuationProcesses': 12, 'expectedCombinedProcesses': 18,
              'actualContinuationCompleted': 0, 'actualContinuationFailed': [],
              'livePids': [], 'pending': REMAINING[:],
              'oldAggregateIsInterrupted': True, 'oldAggregateFalseIsNotAllRowsFailed': True}
    try:
        old, complete, interrupted = verify_original()
        result['originalCompletedChecks'] = sum(row['actual']['checks'] for row in complete)
        if not (O / 'test-result.json').is_file():
            result['reason'] = 'continuation-not-launched'
            return result
        current = read(O / 'test-result.json')
        result.update(continuationDriverPid=current['runnerPid'],
                      continuationState=current.get('state'), livePids=current.get('livePids'),
                      pending=current.get('pending'))
        rows = current['rows']
        names = [row['mode'] for row in rows]
        assert len(names) == len(set(names)) and set(names) <= set(REMAINING)
        completed = [row for row in rows if row.get('actualExitCode') is not None]
        result['actualContinuationCompleted'] = len(completed)
        result['actualContinuationFailed'] = [row['mode'] for row in completed if row.get('passed') is not True]
        if not current.get('finishedAtUtc'):
            result['reason'] = 'continuation-terminal-error' if current.get('exception') else 'continuation-running'
            if current.get('exception'):
                result['exception'] = current['exception']
            return result
        assert current['kind'] == 'same-frozen-T-formal-sdk-after-shutdown-continuation'
        assert current['passed'] is True and current['inputsUnchanged'] is True
        assert current['originalEvidenceUnchanged'] is True and current['inJob'] is False
        assert Path(current['pythonExecutable']) == Path('C:/Python314/pythonw.exe')
        assert current['parallelLimit'] == 3 and current['processesTerminated'] == 0
        assert current['originalCompletedModesNotRerun'] == list(BASE_CHECKS)
        assert current['remainingModes'] == REMAINING
        assert current['originalDriverPid'] == 4104
        assert current['originalRun'] == fact(OLD / 'test-result.json')
        assert current['sourceManifest']['sha256'] == EXPECTED_MANIFEST
        assert current['products'] == old['products'] == read(B / 'build-products.json')
        assert current['nativeInputs'] == old['nativeInputs']
        assert names == REMAINING and current['processes'] == 12
        assert not current['pending'] and not current['livePids']
        assert current['checks'] == sum(row['actual']['checks'] for row in rows)
        assert all(current[k] is False for k in ('gameExecuted', 'installed', 'productionAdmission', 'fullBCompleted'))
        launch = read(O / 'driver.launch.json')
        assert launch['returnedValue'] == 0 and launch['pid'] == current['runnerPid']
        assert launch['mechanism'] == 'Win32_Process.Create' and launch['hiddenExecutable'] == 'pythonw.exe'
        assert launch['originalDriverRestarted'] is False
        for row in rows:
            verify_row(row, O)
            assert row['inJob'] is False
        verify_record(current['script'])
        verify_record(current['commonScript'])
        verify_preserved()
        result.update(ready=True, reason='all-18-real-terminal-successes-from-two-runs',
                      continuationChecks=current['checks'], combinedChecks=339 + current['checks'])
    except BaseException:
        result.update(reason='verification-rejected', exception=traceback.format_exc())
    return result


def review_prerequisites(verified):
    def verify(path, digest, length, origin):
        p = Path(path)
        assert p.is_absolute(), (origin, 'Expected an absolute path', path)
        key = (str(p), digest)
        if key in verified:
            return
        record = {'path': str(p), 'sha256': digest}
        if length is not None:
            record['bytes'] = length
        verify_record(record)
        verified[key] = {'origin': origin, **fact(p)}

    def visit(value, origin):
        if isinstance(value, dict):
            if isinstance(value.get('path'), str) and isinstance(value.get('sha256'), str):
                if Path(value['path']).is_absolute():
                    verify(value['path'], value['sha256'], value.get('bytes'), origin)
            for key, digest_key in [('image', 'imageSha256'), ('pdb', 'pdbSha256'), ('object', 'sha256')]:
                if isinstance(value.get(key), str) and isinstance(value.get(digest_key), str):
                    verify(value[key], value[digest_key], None, origin)
            for child in value.values():
                visit(child, origin)
        elif isinstance(value, list):
            for child in value:
                visit(child, origin)

    frozen = verify_frozen()
    visit(frozen, 'current-main-and-identical-T-SDK-frozen-package')
    preflight = verify_preserved()
    visit(preflight, 'preserved-shutdown-and-original-run-evidence')
    progress_path = R / 'integration-progress-r1/test-result.json'
    progress = read(progress_path)
    assert progress['passed'] is True and len(progress['completedScopes']) == 21
    scopes = []
    for scope in progress['completedScopes']:
        original = scope['original']
        verify(original['path'], original['sha256'], original['bytes'], 'existing-completed-scope')
        verify(scope['retained']['path'], scope['retained']['sha256'], scope['retained']['bytes'], 'existing-retained-scope')
        path = Path(original['path'])
        data = read(path)
        assert data['passed'] is True, str(path)
        visit(data, str(path))
        scopes.append(path)
    return frozen, scopes, visit


def audit_prerequisites():
    """Read-only audit while the real matrix is pending; never claims closure."""
    verified = {}
    try:
        frozen, scopes, _ = review_prerequisites(verified)
        output = {'prerequisitesVerified': True, 'existingCompletedScopes': len(scopes),
                  'verifiedArtifacts': len(verified), 'sourceFiles': frozen['sourceFiles'],
                  'productInputs': frozen['productInputs'], 'closureCollected': False,
                  'formalSdkMatrixComplete': False, 'currentReadiness': readiness()}
        code = 0
    except BaseException:
        output = {'prerequisitesVerified': False, 'closureCollected': False,
                  'formalSdkMatrixComplete': False, 'exception': traceback.format_exc()}
        code = 1
    print(json.dumps(output))
    return code


def collect():
    ready = readiness()
    if not ready['ready']:
        print(json.dumps(ready))
        return 3
    assert not CLOSURE.exists() and not COMBINED.exists(), 'Preserve all previous collection attempts.'
    CLOSURE.mkdir()
    verified = {}
    result = {'kind': 'same-T-stock-stop-integration-closure-after-confirmed-shutdown', 'passed': False,
              'observedAtUtc': now(), 'sourceFiles': 894, 'productInputs': 482,
              'mainApplied': True, 'completedScopes': [], 'sourceRuns': [],
              'formalSdkMatrixComplete': False, 'currentIncrementValidationComplete': False,
              'mainDocumentsModifiedByThisCollector': False, 'oldResultsModified': False,
              'supersededHistoricalMatricesRestarted': False,
              'gameExecuted': False, 'installed': False, 'productionAdmission': False, 'fullBCompleted': False}
    try:
        frozen, scopes, visit = review_prerequisites(verified)
        old, base, interrupted = verify_original()
        continuation = read(O / 'test-result.json')
        assert readiness()['ready'] is True
        visit(continuation, 'actual-12-process-after-shutdown-continuation')
        visit(read(OLD / 'test-result.json'), 'unaltered-interrupted-original-sdk-run')
        source_runs = [
            {'role': 'six-terminal-successes-before-shutdown', 'run': fact(OLD / 'test-result.json'),
             'driverPid': 4104, 'startedAtUtc': old['startedAtUtc'],
             'aggregateTerminal': False, 'aggregatePassed': old['passed'],
             'aggregateStatus': 'interrupted-by-confirmed-system-power-off',
             'completedModesUsed': list(BASE_CHECKS), 'completedProcessesUsed': 6, 'checksUsed': 339,
             'interruptedModesNotUsed': [row['mode'] for row in interrupted],
             'pendingModesNotUsed': old['pending']},
            {'role': 'twelve-terminal-successes-after-reboot', 'run': fact(O / 'test-result.json'),
             'driverPid': continuation['runnerPid'], 'startedAtUtc': continuation['startedAtUtc'],
             'finishedAtUtc': continuation['finishedAtUtc'], 'aggregateTerminal': True,
             'aggregatePassed': True, 'completedModesUsed': REMAINING,
             'completedProcessesUsed': 12, 'checksUsed': continuation['checks']},
        ]
        rows = []
        for directory, source_rows in [(OLD, base), (O, continuation['rows'])]:
            for row in source_rows:
                verify_row(row, directory)
                rows.append({'sourceRun': str(directory / 'test-result.json'),
                             'sourceResult': fact(directory / (row['mode'] + '.result.json')),
                             **row})
        assert len(rows) == 18 and {row['mode'] for row in rows} == set(ALL_MODES)
        combined = {'kind': 'formal-sdk-complete-from-explicit-original-six-and-continuation-twelve',
                    'passed': True, 'observedAtUtc': now(), 'finishedAtUtc': now(),
                    'sourceRuns': source_runs, 'rows': rows, 'processes': 18,
                    'checks': sum(row['actual']['checks'] for row in rows),
                    'pending': [], 'livePids': [], 'inputsUnchanged': True,
                    'originalAggregateRetainedUnchanged': True,
                    'shutdownEvidence': fact(E / 'system-events.raw.json'),
                    'rebootCimEvidence': fact(E / 'cim-after-reboot.raw.json'),
                    'preflight': fact(E / 'preflight-result-r3.json'),
                    'sourceManifest': frozen['sourceManifest'], 'products': frozen['products'],
                    'nativeInputs': frozen['nativeInputs'], 'sourceFiles': 894, 'productInputs': 482,
                    'formalSdkMatrixComplete': True, 'gameExecuted': False,
                    'installed': False, 'productionAdmission': False, 'fullBCompleted': False}
        assert combined['checks'] == 339 + continuation['checks']
        COMBINED.mkdir()
        save(COMBINED / 'test-result.json', combined)
        visit(combined, 'explicit-two-run-combined-SDK-scope')
        scopes.append(COMBINED / 'test-result.json')
        retained = CLOSURE / 'retained'
        retained.mkdir()
        for i, original in enumerate(scopes):
            assert read(original)['passed'] is True
            target = retained / (f'{i:02d}__' + original.parent.name + '__' + original.name)
            shutil.copy2(original, target)
            assert sha(original) == sha(target)
            result['completedScopes'].append({'original': fact(original), 'retained': fact(target), 'retainedFile': target.name})
        for directory, source_rows in [(OLD, base), (O, continuation['rows'])]:
            target_dir = retained / 'formal-sdk-originals' / directory.name
            target_dir.mkdir(parents=True)
            for row in source_rows:
                for suffix in SUFFIXES:
                    original = directory / (row['mode'] + suffix)
                    target = target_dir / original.name
                    shutil.copy2(original, target)
                    assert sha(original) == sha(target)
        shutdown_target = retained / 'after-shutdown-evidence-r1'
        shutil.copytree(E, shutdown_target)
        for original in E.rglob('*'):
            if original.is_file():
                assert sha(original) == sha(shutdown_target / original.relative_to(E))
        for original in [O / 'test-result.json', O / 'driver.launch.json',
                         O / 'cim-launch-audit.raw.json', O / 'frozen-before.json', O / 'frozen-after.json',
                         O / 'Run-FormalContinuationAfterShutdownR1.py', O / 'continuation_common.py', Path(__file__)]:
            target = retained / original.name
            shutil.copy2(original, target)
            assert sha(original) == sha(target)
        assert len(result['completedScopes']) == 22
        verify_frozen()
        verify_preserved()
        result.update(passed=True, sourceRuns=source_runs, sdkProcesses=18,
                      sdkChecks=combined['checks'], formalSdkMatrixComplete=True,
                      currentIncrementValidationComplete=True, completedArtifactIntegrityPassed=True,
                      exactCurrentMainSourceMatch=True, mainReadmeUnchanged=True,
                      sourceManifest=frozen['sourceManifest'], candidateManifest=frozen['candidateManifest'],
                      mainReadme=frozen['mainReadme'], previousProgressIndex=fact(R / 'integration-progress-r1/test-result.json'),
                      combinedSdkScope=fact(COMBINED / 'test-result.json'),
                      originalInterruptedSdkScope=fact(OLD / 'test-result.json'),
                      continuationSdkScope=fact(O / 'test-result.json'), finishedAtUtc=now())
    except BaseException:
        result['exception'] = traceback.format_exc()
    finally:
        save(CLOSURE / 'verified-artifacts.json', list(verified.values()))
        result['verifiedArtifactCount'] = len(verified)
        result['verifiedArtifacts'] = fact(CLOSURE / 'verified-artifacts.json')
        result['script'] = fact(Path(__file__))
        save(CLOSURE / 'test-result.json', result)
    print(json.dumps({'passed': result['passed'], 'scopes': len(result['completedScopes']),
                      'artifacts': len(verified), 'sdkChecks': result.get('sdkChecks'),
                      'exception': result.get('exception')}))
    return 0 if result['passed'] else 1


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument('--check-ready', action='store_true')
    group.add_argument('--audit-prerequisites', action='store_true')
    group.add_argument('--collect', action='store_true')
    args = parser.parse_args(argv)
    if args.check_ready:
        ready = readiness()
        print(json.dumps(ready))
        return 0 if ready['ready'] else 3
    if args.audit_prerequisites:
        return audit_prerequisites()
    return collect()


if __name__ == '__main__':
    raise SystemExit(main())
