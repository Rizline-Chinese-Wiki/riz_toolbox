"""Verify the exact S4 + managed-native SEH fix, without accepting later work."""
import hashlib
import json
import zipfile
from datetime import datetime, timezone
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
CACHE = Path('D:/riz-build-cache-next')
ROOT = CACHE / 'playback-clock-managed-seh-fix-r1-20261031'
OUT = ROOT / 'validation-r1'
S4 = CACHE / 'playback-stock-clock-bootstrap-r1-20261031'
BRIDGE = 'src/RizToolbox.Runtime/Hooks/NativePlaybackSingleReturnBridge.cs'
FIX_HASH = '79e4947c066ad91ae56cba29db50cf61f8d091777d4456569a2f2afad3450f07'
files = {}


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def check(path, expected=None):
    path = Path(path).resolve()
    key = str(path).casefold()
    if key not in files:
        data = path.read_bytes()
        files[key] = dict(path=str(path), sha256=hashlib.sha256(data).hexdigest(), bytes=len(data))
    row = files[key]
    assert expected is None or row['sha256'] == expected, str(path)
    return row


def collect(value):
    if isinstance(value, list):
        for row in value:
            collect(row)
    elif isinstance(value, dict):
        for path_key, hash_key in (('path', 'sha256'), ('manifestPath', 'sha256'),
                                   ('image', 'imageSha256'), ('pdb', 'pdbSha256')):
            if isinstance(value.get(path_key), str) and isinstance(value.get(hash_key), str):
                check(value[path_key], value[hash_key])
        for row in value.values():
            collect(row)


def write(path, value):
    Path(path).write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def terminal(stem):
    assert (ROOT / (stem + '.driver.exit.txt')).read_text().strip() == '0', stem
    for suffix in ('exit.txt', 'stdout.log', 'stderr.log'):
        check(ROOT / (stem + '.driver.' + suffix))


def run():
    assert not OUT.exists(), 'Preserve previous results.'
    paths = {name: CACHE / ('foundation-stock-clock-seh-' + name + '-r1-20261031') /
             ('managed-build-manifest.json' if name == 'release' else 'test-result.json')
             for name in ('core', 'full', 'release', 'solution', 'sdk')}
    paths['full'] = CACHE / 'foundation-stock-clock-seh-full-r2-20261031/test-result.json'
    proofs = {name: read(path) for name, path in paths.items()}
    for name, proof in proofs.items():
        assert proof['passed'] and not proof.get('failure'), name
        assert not proof.get('gameExecuted', False) and not proof.get('installed', False), name
        collect(proof)
        check(paths[name])
        terminal(name + ('-r2' if name == 'full' else '-r1'))
    core, full, release, solution, sdk = (proofs[n] for n in ('core', 'full', 'release', 'solution', 'sdk'))
    assert core['runnerExitCode'] == 0 and len(core['inputs']) == 744
    assert full['sourceProof']['sha256'] == check(paths['core'])['sha256']
    assert full['fullManagedMatrix'] and len(full['projects']) == 27
    assert sum(row['checks'] for row in full['projects']) == 2465
    assert all(row['passed'] and row['buildExitCode'] == row['testExitCode'] == 0 for row in full['projects'])
    assert release['buildExitCode'] == 0 and release['sourceHashesUnchanged']
    assert release['sourceSnapshot']['sha256'] == check(paths['core'])['sha256']
    assert len([row for row in release['files'] if row['path'].lower().endswith('.dll')]) == 6
    assert solution['buildExitCode'] == solution['errors'] == solution['warnings'] == 0
    assert sdk['runnerExitCode'] == 0 and sdk['checks'] == 14
    distribution = read(sdk['result']['path'])
    assert distribution['checksPassed'] == 14 and distribution['runtimeGameVerification'] is False
    plugins = list(Path(distribution['generatedProjects']).rglob('*.csproj'))
    assert len(plugins) == 2
    for plugin in plugins:
        check(plugin)

    # The pre-fix S4 managed results remain historical. Reuse only unchanged
    # native evidence and compare every archived source against this declared fix.
    stage = read(S4 / 'completion-r1.json')
    check(S4 / 'completion-r1.json')
    collect(stage['sourceArchive'])
    changed = []
    with zipfile.ZipFile(stage['sourceArchive']['path']) as archive:
        entries = [row for row in archive.infolist() if not row.is_dir()]
        assert len(entries) == 756
        for row in entries:
            old_hash = hashlib.sha256(archive.read(row)).hexdigest()
            current = check(REPO / row.filename)
            if current['sha256'] != old_hash:
                changed.append(row.filename)
            assert current['sha256'] == (FIX_HASH if row.filename == BRIDGE else old_hash), row.filename
    assert changed == [BRIDGE], changed
    native_path = stage['nativeProof']['path']
    collect(stage['nativeProof'])
    native = read(native_path)
    assert native['passed']
    collect(native)

    clock_path = ROOT / 'clock-checks-r1/test-result.json'
    clock = read(clock_path)
    assert clock['passed'] and clock['processes'] == 10 and clock['checks'] == 179
    assert all(row['actualExitCode'] == 0 for row in clock['rows'])
    collect(clock)
    check(clock_path)
    terminal('clock-checks-r1')
    collect(read(ROOT / 'clock-source-proof.json'))
    check(ROOT / 'clock-source-proof.json')
    symbols_path = ROOT / 'clock-symbols-r1/test-result.json'
    symbols = read(symbols_path)
    assert symbols['passed'] and symbols['pePdbPairs'] == 5 and symbols['documents'] == 469
    collect(symbols)
    check(symbols_path)
    terminal('clock-symbols-r1')

    review_path = CACHE / 'playback-clock-review-seh-r2-20261031/review-result.json'
    review = read(review_path)
    assert review['passed'] and review['fixConfirmed'] and review['sourceInputCount'] == 756
    assert review['fix']['after']['sha256'] == FIX_HASH and len(review['rows']) == 5
    for row in review['rows']:
        assert row['actualExitCode'] == 0
        result = row['result']
        assert result['actual'] == 3 and result['OriginalCalls'] == 1 and result['InFlight'] == 0
        logical = row['mode'] == 'logical-disposed'
        assert result['Known'] is logical and result['notified'] is not logical
    collect(review)
    check(review_path)
    check(ROOT / 'full-r1-interruption-observation.json')
    check(__file__)
    for row in files.values():
        assert hashlib.sha256(Path(row['path']).read_bytes()).hexdigest() == row['sha256'], row['path']
    result = dict(kind='stock-clock-managed-native-seh-current-source-validation', passed=True,
                  observedAtUtc=datetime.now(timezone.utc).isoformat(),
                  sourceScope='S4 plus the exact managed-native SEH fix, before S5/S6 integration',
                  currentSourceAccepted=True, sourceFilesCompared=756, bridgeSha256=FIX_HASH,
                  changedSourcePaths=changed, coreInputs=744, managedSuites=27, managedChecks=2465,
                  releaseAssemblies=6, solutionErrors=0, solutionWarnings=0,
                  sdkIndependentPlugins=2, sdkChecks=14, clockProcesses=10, clockChecks=179,
                  clockPePdbPairs=5, clockSourceDocuments=469, realNativeExceptionProcesses=5,
                  priorNativeEvidenceScope='Unchanged independent S4 native prototype only; old managed results are not current-source acceptance.',
                  proofs={name: check(path) for name, path in paths.items()},
                  clock=check(clock_path), symbols=check(symbols_path), native=check(native_path),
                  nativeSehReview=check(review_path), verifiedUniqueFiles=len(files),
                  completeStockProviderImplemented=False, crossRunHandoffImplemented=False,
                  sourceGenerationAdmissionProven=False, completeLowerWriterCoverage=False,
                  arbitrarySeekImplemented=False, fullB01B19Completed=False,
                  productionAdmission=False, gameExecuted=False, installed=False)
    OUT.mkdir()
    write(OUT / 'test-result.json', result)
    index = dict(kind='stock-clock-managed-native-seh-evidence-index', passed=True, flavor=None,
                 selfHashExcluded=True, result=check(OUT / 'test-result.json'),
                 files=sorted(files.values(), key=lambda row: row['path'].casefold()))
    write(OUT / 'evidence-index.json', index)
    write(REPO / 'docs/validation/playback-stock-writer-20261031/stock-clock-seh-validation-index.json',
          dict(result=check(OUT / 'test-result.json'), evidenceIndex=check(OUT / 'evidence-index.json')))
    print(json.dumps({key: result[key] for key in ('passed', 'managedSuites', 'managedChecks',
                                                  'clockChecks', 'realNativeExceptionProcesses', 'verifiedUniqueFiles')}))


if __name__ == '__main__':
    run()
