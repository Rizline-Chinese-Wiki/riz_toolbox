"""Bind two owned native callback positives to the current product source."""
import hashlib
import json
import zipfile
from datetime import datetime, timezone
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
CACHE = Path('D:/riz-build-cache-next')
ZIP = CACHE / 'playback-stock-formal-handoff-stage-r1-20261031/snapshot-r2/implemented-source.zip'
OUT = CACHE / 'play-callbacks20-21-main-evidence-r1-20261101'
files = {}


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def fact(path, expected=None):
    path = Path(path).resolve()
    body = path.read_bytes()
    row = dict(path=str(path), sha256=hashlib.sha256(body).hexdigest(), bytes=len(body))
    assert expected is None or row['sha256'] == expected, str(path)
    old = files.setdefault(str(path).casefold(), row)
    assert old == row, f'Input changed while collecting: {path}'
    return row


def collect(value):
    if isinstance(value, list):
        for row in value:
            collect(row)
    elif isinstance(value, dict):
        if isinstance(value.get('path'), str) and isinstance(value.get('sha256'), str):
            fact(value['path'], value['sha256'])
        for row in value.values():
            collect(row)


def write(path, value):
    Path(path).write_text(json.dumps(value, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')


def run():
    assert not OUT.exists()
    fact(ZIP, '299d9cb8286b5f651dc4a79e536473a3b101a16b0c20d8bda2c84bf0f2183c3e')
    with zipfile.ZipFile(ZIP) as archive:
        baseline = {row.filename: hashlib.sha256(archive.read(row)).hexdigest()
                    for row in archive.infolist() if not row.is_dir()}
    assert len(baseline) == 785
    product = {name: digest for name, digest in baseline.items() if name.startswith('src/')}
    assert len(product) == 471
    for name, digest in product.items():
        fact(REPO / name, digest)
    scopes = []
    for site, root_name, result_name, before_name, after_name, index_name, pin in (
        (20, 'play-site20-stage-r1-20261101', None, 'source-before-r2.json',
         'source-after-finish-r1.json', 'evidence-index-r1.json',
         '6ab8c324a827144ec53feb66453076f2b71bcce28b28b61b31d42f63ff20c237'),
        (21, 'execute-site21-stage-r1-20261101', 'test-result-r1.json', 'source-before-r1.json',
         'source-after-r1.json', 'index-r1.json',
         'b332e461b4b6a74fa95fbd6641dfbd75eac27c52e154bff5a02c91984898d573'),
    ):
        root = CACHE / root_name
        completion_path = root / 'completion-r1.json'
        completion = read(completion_path)
        fact(completion_path, pin)
        assert completion['passed'] and not completion['productionAdmission']
        assert not completion['gameExecuted'] and not completion['installed']
        # The site20 finisher names its immutable final source-after receipt.
        if site == 20:
            before_path = Path(completion['sourceBefore']['path'])
            after_path = Path(completion['sourceAfter']['path'])
            actual = completion['actual']
            execution_path = Path(completion['execution']['path'])
            symbols_path = Path(completion['managedSymbols']['path'])
        else:
            before_path, after_path = root / before_name, root / after_name
            result = read(root / result_name)
            assert result['passed'] and result['sourceUnchanged']
            collect(result)
            fact(root / result_name)
            actual = result['actual']
            execution_path = Path(result['execution']['path'])
            symbols_path = Path(result['symbols']['path'])
        before, after = read(before_path), read(after_path)
        assert before == after and len(before) == 910
        rows = {row['relativePath']: row for row in before}
        assert set(baseline) <= set(rows) and len(set(rows) - set(baseline)) == 125
        changed = [name for name, digest in baseline.items() if rows[name]['sha256'] != digest]
        assert len(changed) == 4 and all(name.startswith('tests/') for name in changed)
        assert all(rows[name]['sha256'] == digest for name, digest in product.items())
        collect(before)
        fact(before_path)
        fact(after_path)
        assert actual['passed'] and actual['actualCallVerified']
        assert actual['actualMidCallbacks'] == actual['actualNonemptyRows'] == actual['actualListenerCalls'] == 1
        assert actual['rootFrees'] == 0 and not actual['retirementIssued']
        assert actual['listenerEffects']['UnaccountedInvocations'] == 1
        assert not actual['listenerEffects']['AllInvokedEffectsAccounted']
        assert all(not row['EffectsEnded'] for row in actual['children'])
        assert actual['source']['Unknown'] == actual['source']['Faults'] == 0
        if site == 20:
            assert actual['actualPlaySourceSites'] == [20]
        else:
            assert actual['newPositiveSite'] == 21 and actual['actualExecuteCallVerified']
            # Execute returns through the existing Remove cleanup source (6).
            assert actual['actualSourceSites'] == [6, 14, 21]
            assert [(row['Site'], row['Stage']) for row in actual['rawSources']] == [
                (14, 0), (21, 0), (21, 1), (14, 1), (6, 0), (6, 1)]
            assert actual['source']['Events'] == 6 and actual['source']['CompletedTransactions'] == 3
        execution, symbols = read(execution_path), read(symbols_path)
        assert execution['passed'] and symbols['passed']
        assert len(execution['rows']) == 1 and execution['rows'][0]['actualExitCode'] == 0
        assert len(symbols['images']) == 5
        collect(execution)
        collect(symbols)
        collect(completion)
        index_path = root / index_name
        collect(read(index_path))
        fact(index_path)
        if site == 21:
            collect(read(root / 'seal-protected-inputs-r1.json'))
            fact(root / 'seal-protected-inputs-r1.json')
        scopes.append(dict(site=site, completion=fact(completion_path), evidenceIndex=fact(index_path),
                           execution=fact(execution_path), symbols=fact(symbols_path),
                           sourceFiles=910, standardBaselineFiles=785, identicalBaselineFiles=781,
                           modifiedTestFiles=changed, additionalFixtureFiles=125,
                           currentProductSourceFiles=471, productEdits=0,
                           positiveProcesses=1, managedPePdbPairs=5, portablePdbDocuments=565,
                           nativePePdbPairs=1, nativeCoffObjects=14,
                           actualMidCallbacks=1, actualNonemptyRows=1, actualListenerCalls=1,
                           unaccountedListenerEffects=1, effectsEnded=False, rootFrees=0))
    failure_root = CACHE / 'play-callbacks20-21-main-collector-failure-r1-20261101'
    fact(failure_root / 'collector-before-fix.py')
    fact(failure_root / 'observed-failure.json')
    fact(__file__)
    OUT.mkdir()
    result = dict(kind='current-product-owned-play-callback20-21-evidence', passed=True,
                  observedAtUtc=datetime.now(timezone.utc).isoformat(), sourceArchive=fact(ZIP),
                  currentProductSourceFiles=471, productSourceEdits=0,
                  newPositiveSites=[20, 21], positiveProcesses=2, scopes=scopes,
                  historicalSite19Repeated=False, combinedSingleProcessCoverageClaimed=False,
                  full785SourceByteIdentityClaimed=False, fixtureInputFilesPerScope=910,
                  verifiedUniqueFiles=len(files), gameCodeExecuted=False, installed=False,
                  nonemptyEffectsEnded=False, retirementIssued=False,
                  completeSourceGeneration=False, productionAdmission=False, fullB01B19Completed=False)
    write(OUT / 'test-result.json', result)
    write(OUT / 'evidence-index.json', dict(kind='owned-play-callback20-21-evidence-index', passed=True,
          selfHashExcluded=True, result=fact(OUT / 'test-result.json'),
          files=sorted(files.values(), key=lambda row: row['path'].casefold())))
    docs = REPO / 'docs/validation/play-callbacks20-21-20261101'
    docs.mkdir(exist_ok=True)
    write(docs / 'evidence-index.json', dict(result=fact(OUT / 'test-result.json'),
                                            evidenceIndex=fact(OUT / 'evidence-index.json')))
    print(json.dumps(dict(passed=True, sites=[20, 21], products=471, processes=2,
                          result=fact(OUT / 'test-result.json'), index=fact(OUT / 'evidence-index.json'))))


if __name__ == '__main__':
    run()
