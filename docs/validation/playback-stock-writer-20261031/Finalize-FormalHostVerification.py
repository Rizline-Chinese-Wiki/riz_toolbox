"""Seal already executed internal host-regression evidence; run no target code."""
import hashlib
import json
import zipfile
from datetime import datetime, timezone
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
ROOT = Path('D:/riz-build-cache-next/playback-stock-formal-host-review-r1-20261031')
SNAPSHOT = Path('D:/riz-build-cache-next/playback-stock-formal-handoff-stage-r1-20261031/snapshot-r2')
OUT = ROOT / 'root-completion-r1'
files = {}


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def fact(path, expected=None):
    path = Path(path).resolve()
    body = path.read_bytes()
    row = dict(path=str(path), sha256=hashlib.sha256(body).hexdigest(), bytes=len(body))
    assert expected is None or row['sha256'] == expected, str(path)
    old = files.setdefault(str(path).casefold(), row)
    assert old == row, f'Input changed during collection: {path}'
    return row


def collect(value):
    if isinstance(value, list):
        for child in value:
            collect(child)
    elif isinstance(value, dict):
        if isinstance(value.get('path'), str) and isinstance(value.get('sha256'), str):
            fact(value['path'], value['sha256'])
        for child in value.values():
            collect(child)


def write(path, value):
    Path(path).write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def execution(relative, processes, checks):
    path = ROOT / relative / 'test-result.json'
    proof = read(path)
    assert proof['passed'] and proof['sourceAndInputsUnchanged']
    assert proof['processes'] == processes and proof['checks'] == checks
    assert not proof['counterexamples']
    assert not proof['gameExecuted'] and not proof['installed'] and not proof['productionAdmission']
    assert proof['sourceManifest']['sha256'] == fact(ROOT / 'source-r2-freeze.json')['sha256']
    assert len(proof['rows']) == processes
    for row in proof['rows']:
        assert row['actualExitCode'] == 0 and not row['timedOut'] and row['result']['passed']
        assert (path.parent / (row['mode'] + '.exit.txt')).read_text().strip() == '0'
        for suffix in ('exit.txt', 'stdout.log', 'stderr.log'):
            fact(path.parent / (row['mode'] + '.' + suffix))
    collect(proof)
    fact(path)
    return proof


def run():
    assert not OUT.exists(), 'Preserve previous verification outputs.'
    source = read(ROOT / 'source-r2-freeze.json')
    assert source['passed'] and source['files'] == len(source['rows']) == 785
    archive = SNAPSHOT / 'implemented-source.zip'
    fact(archive, '299d9cb8286b5f651dc4a79e536473a3b101a16b0c20d8bda2c84bf0f2183c3e')
    with zipfile.ZipFile(archive) as frozen:
        entries = {entry.filename: entry for entry in frozen.infolist() if not entry.is_dir()}
        assert len(entries) == 785
        for row in source['rows']:
            expected = hashlib.sha256(frozen.read(entries[row['relativePath']])).hexdigest()
            assert row['sha256'] == expected
            fact(Path(source['independentRoot']) / row['relativePath'], expected)
            fact(REPO / row['relativePath'], expected)
    fixed = execution('root-fixed-execution-r1', 2, 46)
    withdrawn = execution('root-withdrawn-execution-r1', 1, 34)
    direct = next(row['result']['facts'] for row in fixed['rows']
                  if row['mode'] == 'clone-session-native-handoff')
    assert direct['copiedSessionAccepted'] is False
    assert direct['authorityAfter'] and direct['legitimateHostSupportedAfter']
    assert direct['nativeBefore'] == direct['nativeAfter'] == 4
    positive = withdrawn['rows'][0]['result']['facts']
    assert positive['withdrawnPredecessorTransfer'] and positive['sameModuleServiceAndSdkView']
    assert positive['oldProviderCleanupPreservedSuccessor'] and positive['nativeSuccessor'] == 3.75
    symbols = []
    for relative, proof_path in (
        ('root-fixed-symbols-r2', ROOT / 'root-fixed-execution-r1/test-result.json'),
        ('root-withdrawn-symbols-r1', ROOT / 'root-withdrawn-execution-r1/test-result.json'),
    ):
        path = ROOT / relative / 'test-result.json'
        proof = read(path)
        assert proof['passed'] and proof['pePdbPairs'] == 5 and proof['documents'] == 479
        assert proof['execution']['sha256'] == fact(proof_path)['sha256']
        assert all(image['codeViewMatches'] and image['sourceChecksumsMatch'] for image in proof['images'])
        collect(proof)
        symbols.append(fact(path))
    # Preserve the observed failure as historical evidence. Its old mutable
    # probe source is not used as proof for this fixed-source acceptance.
    historical_path = ROOT / 'execution-r3/test-result.json'
    historical = read(historical_path)
    assert not historical['passed'] and historical['sourceAndInputsUnchanged']
    assert historical['counterexamples'] == ['clone-session-native-handoff']
    failure = next(row for row in historical['rows'] if row['mode'] == 'clone-session-native-handoff')
    assert failure['actualExitCode'] == 1 and failure['result']['facts']['copiedSessionAccepted']
    fact(historical_path, '66e1c4250764c31e66edeb44420876dadbf9e40906fda17968489c3f08081325')
    for row in historical['rows']:
        for suffix in ('exit.txt', 'stdout.log', 'stderr.log'):
            fact(historical_path.parent / (row['mode'] + '.' + suffix))
    for prefix in ('root-fixed-execution-r1.driver', 'root-withdrawn-execution-r1.driver',
                   'root-withdrawn-build-r1', 'root-fixed-symbols-r2', 'root-withdrawn-symbols-r1'):
        assert (ROOT / (prefix + '.exit.txt')).read_text().strip() == '0'
        for suffix in ('exit.txt', 'stdout.log', 'stderr.log'):
            fact(ROOT / (prefix + '.' + suffix))
    # The first symbol verifier missed named net6 generated documents. Keep
    # that harness failure, while acceptance uses corrected actual proofs.
    assert (ROOT / 'root-fixed-symbols-r1.exit.txt').read_text().strip() == '1'
    for name in ('root-fixed-symbols-r1.exit.txt', 'root-fixed-symbols-r1.stderr.log',
                 'Verify-FormalHostReviewSymbols.failed-r1.ps1', 'FINDING-r1.md',
                 'run_probes_r3.py', 'root-run-withdrawn-r1.py'):
        fact(ROOT / name)
    fact(__file__)
    result = dict(kind='formal-host-current-source-independent-verification-seal', passed=True,
                  observedAtUtc=datetime.now(timezone.utc).isoformat(), flavor=None,
                  sourceFilesCompared=785, sourceArchive=fact(archive),
                  sourceManifest=fact(ROOT / 'source-r2-freeze.json'),
                  actualProcesses=3, actualChecks=80, managedPePdbPairs=10,
                  portablePdbDocuments=958,
                  fixedExecution=fact(ROOT / 'root-fixed-execution-r1/test-result.json'),
                  exactWithdrawnPositive=fact(ROOT / 'root-withdrawn-execution-r1/test-result.json'),
                  symbolProofs=symbols, historicalCounterexample=fact(historical_path),
                  copiedInternalSessionRejected=True, exactWithdrawnPredecessorPreserved=True,
                  sameModuleServiceSdkViewPreserved=True, externalSdkVulnerabilityClaim=False,
                  broadCaseReviewPass=False, verifiedUniqueFiles=len(files),
                  completeSourceGeneration=False, completeLowerWriterCoverage=False,
                  productionAdmission=False, arbitrarySeek=False, fullB01B19Completed=False,
                  gameExecuted=False, installed=False)
    for row in list(files.values()):
        fact(row['path'], row['sha256'])
    OUT.mkdir()
    write(OUT / 'test-result.json', result)
    index = dict(kind='formal-host-independent-verification-evidence-index', passed=True,
                 selfHashExcluded=True, result=fact(OUT / 'test-result.json'),
                 historicalCounterexampleDependencies='Only original result and raw process logs; old mutable probe sources excluded.',
                 files=sorted(files.values(), key=lambda row: row['path'].casefold()))
    write(OUT / 'evidence-index.json', index)
    print(json.dumps(dict(passed=True, processes=3, checks=80, files=len(files),
                          result=fact(OUT / 'test-result.json'), index=fact(OUT / 'evidence-index.json'))))


if __name__ == '__main__':
    run()
