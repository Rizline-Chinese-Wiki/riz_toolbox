"""Validate the integrated source against fresh core/full/release/SDK evidence.

The slow core-r4 mapped batch is deliberately absent: it proves an older tree.
This evidence closes this integration batch, not N2 or the B01-B19 objective.
"""
import hashlib
import json
import zipfile
from datetime import datetime, timezone
from pathlib import Path

REPOSITORY = Path(__file__).resolve().parents[3]
CACHE = Path('D:/riz-build-cache-next')
ROOT = CACHE / 'playback-stock-main-integration-r1-20261031'
OUT = ROOT / 'validation-r1'


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


verified = {}


def check(path, expected=None):
    path = Path(path).resolve()
    key = str(path).casefold()
    if key not in verified:
        verified[key] = {'path': str(path), 'sha256': hashlib.sha256(path.read_bytes()).hexdigest(), 'bytes': path.stat().st_size}
    fact = verified[key]
    assert expected is None or fact['sha256'] == expected.lower(), str(path)
    return fact


def collect(value):
    if isinstance(value, list):
        for item in value:
            collect(item)
    elif isinstance(value, dict):
        for path_key, hash_key in (('path', 'sha256'), ('manifestPath', 'sha256'), ('file', 'sha256')):
            if isinstance(value.get(path_key), str) and isinstance(value.get(hash_key), str):
                check(value[path_key], value[hash_key])
        for item in value.values():
            collect(item)


def write(path, value):
    Path(path).write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def run():
    assert not OUT.exists(), 'Preserve prior validation evidence.'
    paths = {name: CACHE / ('foundation-stock-run-integrated-' + name + '-r1-20261031') /
             ('managed-build-manifest.json' if name == 'release' else 'test-result.json')
             for name in ('core', 'full', 'release', 'solution', 'sdk')}
    paths['integration'] = ROOT / 'test-result.json'
    paths['hostRun'] = CACHE / 'playback-stock-run-authority-r1-20261031/completion-r1.json'
    proofs = {name: read(path) for name, path in paths.items()}
    for name, proof in proofs.items():
        assert proof['passed'] is True and not proof.get('failure'), name
        assert not proof.get('gameExecuted', False) and not proof.get('installed', False), name
        if name == 'integration':
            # Migration's before facts refer to overwritten original paths. Bind
            # them to the exact pre-write archive; after facts still check live.
            collect(proof['historicalWorktreeArchive']); collect(proof['frozenDeliveries'])
            old = read(proof['historicalWorktreeArchive']['path'])
            archived = {str(Path(row['originalPath']).resolve()).casefold(): row['archived'] for row in old['files']}
            for change in proof['changedFiles']:
                collect(change['after'])
                if change['before'] is not None:
                    original = str(Path(change['before']['path']).resolve()).casefold()
                    backup = archived[original]
                    assert backup['sha256'] == change['before']['sha256']
                    check(backup['path'], change['before']['sha256'])
        else:
            collect(proof)
        check(paths[name])
    core, full, release, solution, sdk = (proofs[name] for name in ('core', 'full', 'release', 'solution', 'sdk'))
    assert core['runnerExitCode'] == 0 and len(core['inputs']) == 738
    assert full['sourceProof']['sha256'] == check(paths['core'])['sha256'] and full['fullManagedMatrix'] is True
    assert len(full['projects']) == 27 and sum(row['checks'] for row in full['projects']) == 2465
    assert all(row['passed'] and row['buildExitCode'] == row['testExitCode'] == 0 for row in full['projects'])
    assert release['buildExitCode'] == 0 and release['sourceHashesUnchanged'] is True
    assert release['sourceSnapshot']['sha256'] == check(paths['core'])['sha256']
    assert len([row for row in release['files'] if row['path'].lower().endswith('.dll')]) == 6
    assert solution['buildExitCode'] == solution['errors'] == solution['warnings'] == 0
    assert sdk['runnerExitCode'] == 0 and sdk['checks'] == 14
    sdk_child = read(sdk['result']['path'])
    assert sdk_child['checksPassed'] == 14 and sdk_child['runtimeGameVerification'] is False
    generated_projects = list(Path(sdk_child['generatedProjects']).rglob('*.csproj'))
    assert len(generated_projects) == 2
    for path in generated_projects:
        check(path)
    collect(sdk_child)
    for driver in ('core-r2', 'full-r1', 'release-r1', 'solution-r1', 'sdk-r1'):
        path = ROOT / (driver + '.driver.exit.txt')
        assert path.read_text(encoding='utf-8-sig').strip() == '0', driver
        for suffix in ('.driver.exit.txt', '.driver.stdout.log', '.driver.stderr.log'):
            check(ROOT / (driver + suffix))
    host = proofs['hostRun']
    assert host['processes'] == 15 and host['checks'] == 326
    for key in ('execution', 'managedSymbols'):
        child = read(host[key]['path'])
        assert child['passed'] is True
        collect(child)
    assert read(host['managedSymbols']['path'])['documents'] == 465
    # Check the final integrated files, including the unchanged native source.
    with zipfile.ZipFile(host['sourceArchive']['path']) as archive:
        members = [item for item in archive.infolist() if not item.is_dir()]
        assert len(members) == 745
        for item in members:
            path = REPOSITORY / item.filename
            digest = hashlib.sha256(archive.read(item)).hexdigest()
            if item.filename == 'scripts/Test-EngineWaitManagedChecks.ps1':
                assert digest == 'edb66f48a59dfb67cb700c7ab99a6d6b416f6c5890d294a44f33c891bf6bc903'
                digest = '5c1d582c0a98db366a5bf5f2ea2fa0255e7064ea8a86f10e02f40ad56983feda'
            check(path, digest)
    assert (ROOT / 'old-proof-current-source-rejection.exit.txt').read_text().strip() == '1'
    assert 'changed evidence' in (ROOT / 'old-proof-current-source-rejection.stderr.log').read_text(encoding='utf-8-sig')
    check(ROOT / 'old-proof-current-source-rejection.exit.txt')
    check(ROOT / 'old-proof-current-source-rejection.stderr.log')
    check(__file__)
    result = {'kind': 'integrated-stock-source-current-validation', 'passed': True,
              'observedAtUtc': datetime.now(timezone.utc).isoformat(), 'currentSourceAccepted': True,
              'sourceFilesCompared': 745, 'coreInputs': 738, 'mergedFiles': 25,
              'managedSuites': 27, 'managedChecks': 2465, 'releaseAssemblies': 6,
              'solutionErrors': 0, 'solutionWarnings': 0, 'sdkIndependentPlugins': 2, 'sdkChecks': 14,
              'hostRunProcesses': 15, 'hostRunChecks': 326, 'hostRunPePdbPairs': 5, 'hostRunSourceDocuments': 465,
              'historicalCoreR4AcceptedForCurrentSource': False, 'proofs': {name: check(path) for name, path in paths.items()},
              'completeNativeSourceCoverage': False, 'finalClockJointImplemented': False,
              'completeStockProviderImplemented': False, 'crossRunHandoffImplemented': False,
              'arbitrarySeekImplemented': False, 'b01b19Complete': False,
              'gameExecuted': False, 'installed': False, 'productionAdmission': False,
              'verifiedUniqueFiles': len(verified)}
    OUT.mkdir()
    write(OUT / 'test-result.json', result)
    index = {'kind': 'integrated-stock-source-evidence-index', 'passed': True, 'flavor': None,
             'scope': 'This integrated offline batch only; N2 and B01-B19 remain incomplete.',
             'selfHashExcluded': True, 'result': check(OUT / 'test-result.json'),
             'files': sorted(verified.values(), key=lambda row: row['path'].casefold())}
    write(OUT / 'evidence-index.json', index)
    write(REPOSITORY / 'docs/validation/playback-stock-writer-20261031/stock-main-validation-index.json', {
        'result': check(OUT / 'test-result.json'), 'evidenceIndex': check(OUT / 'evidence-index.json')})
    print(json.dumps({key: result[key] for key in ('passed', 'managedSuites', 'managedChecks', 'hostRunChecks', 'sdkChecks', 'verifiedUniqueFiles')}))


if __name__ == '__main__':
    run()
