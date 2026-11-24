"""Derive a complete mapped proof only after the resumed actual processes finish."""
import hashlib
import json
from pathlib import Path

BASE = Path('D:/riz-build-cache-next')
RAW = BASE / 'playback-stock-nine-joint-resume-r1-20261031/test-result.json'
OUT = BASE / 'playback-stock-nine-joint-continuation-proof-r1-20261031'


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def fact(path):
    path = Path(path)
    with path.open('rb') as stream:
        digest = hashlib.file_digest(stream, 'sha256').hexdigest()
    return dict(path=str(path), sha256=digest, bytes=path.stat().st_size)


def main():
    assert RAW.is_file(), 'The actual resumed process set has not finished.'
    result = read(RAW)
    assert result['passed'] is True and result['failure'] is None and result['reusedManagedImagesByteIdentical'] is True
    assert len(result['modes']) == 7 and result['checks'] == 226
    assert result['priorSuccessfulModesReused'] == ['normal', 'pitch-tail', 'body', 'consumer']
    assert set(result['remainingModesActuallyRun']) == {'foundation-normal', 'foundation-outside', 'foundation-unwind'}
    assert not OUT.exists(), 'Preserve the derived evidence; choose a fresh output.'
    for row in result['sources'] + result['nativeInputs'] + result['images']:
        assert fact(row['path'])['sha256'] == row['sha256'], row['path']
    for row in result['modes']:
        log = Path(row['log']['path'])
        exit_path = Path(row['exit']['path']) if 'exit' in row else RAW.parent / (row['mode'] + '.exit.txt')
        assert row['actualExitCode'] == 0 and exit_path.read_text().strip() == '0'
        assert fact(log)['sha256'] == row['log']['sha256']
        actual = json.loads([line for line in log.read_text(encoding='utf-8-sig').splitlines() if line.strip()][-1])
        assert actual == row['actual'] and actual['passed'] is True
        row['exit'] = fact(exit_path)
    core = read(result['sourceProof']['path'])
    result.update(kind='mapped-playback-stock-joint-profile', sourceDirectory=str(RAW.parent / 'source'),
                  sourceArchive=core['sourceArchive'], buildLog=fact(RAW.parent / 'build.stdout.log'),
                  buildExit=fact(RAW.parent / 'build.exit.txt'), resumedExecutionProof=fact(RAW),
                  continuationCollector=fact(__file__),
                  continuationScope='Four actual zero-exit modes from the exact same five managed DLLs; only the three previously uncompleted modes were resumed. Original interrupted runner has no fabricated exit.')
    OUT.mkdir()
    (OUT / 'test-result.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(dict(passed=True, processes=7, checks=226, proof=fact(OUT / 'test-result.json'))))


if __name__ == '__main__':
    main()
