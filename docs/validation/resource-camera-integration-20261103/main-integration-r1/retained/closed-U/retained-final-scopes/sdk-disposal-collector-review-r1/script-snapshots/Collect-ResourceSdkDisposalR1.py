"""Combine eleven old successes and the actual corrected SDK success, preserving the failure."""
from pathlib import Path
import argparse
import datetime
import hashlib
import json

U = Path(__file__).resolve().parent
OUT = U / 'functional-combined-after-disposal-r1'
read = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
fact = lambda p: dict(path=str(p), sha256=sha(p), bytes=p.stat().st_size)


def verify(row):
    path = Path(row['path'])
    assert path.is_file() and sha(path) == row['sha256'], 'Changed artifact: ' + str(path)
    if 'bytes' in row:
        assert path.stat().st_size == row['bytes']


def check_actual(row, folder):
    mode = row['mode']
    assert row['passed'] is True and row['actualExitCode'] == 0 and row['actual']['passed'] is True
    assert read(folder / (mode + '.result.json')) == row
    assert int((folder / (mode + '.exit.txt')).read_text()) == 0
    verify(row['stdout']); verify(row['stderr'])
    actual = json.loads(Path(row['stdout']['path']).read_text(encoding='utf-8-sig').strip().splitlines()[-1])
    assert actual == row['actual'] and actual['mode'] == mode and actual['checks'] > 0
    assert actual['gameExecuted'] is False and actual['productionAdmission'] is False
    launch = read(folder / (mode + '.launch.json'))
    assert launch['pid'] == row['pid'] and launch['command'] == row['command']
    return dict(**row, sourceRun=str(folder), sourceResult=fact(folder / (mode + '.result.json')))


def collect():
    assert not OUT.exists(), 'Preserve earlier collections.'
    old_dir = U / 'functional-matrix-r2'
    new_dir = U / 'sdk-current-dispose-r1'
    original_path = old_dir / 'test-result.json'
    retry_path = new_dir / 'test-result.json'
    original = read(original_path); retry = read(retry_path)
    assert original['passed'] is False and original['finishedAtUtc']
    assert retry['passed'] is True and retry['finishedAtUtc'] and retry['frozenSourcesUnchanged'] is True
    assert retry['candidateSourcesUnchanged'] is True and retry['inJob'] is False
    assert len(retry['rows']) == 2 and retry['rows'][0]['mode'] == 'build' and retry['rows'][0]['actualExitCode'] == 0
    assert retry['rows'][1]['mode'] == 'resource-sdk-current'
    original_failed = [row for row in original['rows'] if row['mode'] == 'resource-sdk-current']
    assert len(original_failed) == 1 and original_failed[0]['actualExitCode'] == 1 and original_failed[0]['actual']['checks'] == 34
    assert 'Another prepared playback clock change owns this baseline.' in original_failed[0]['actual']['failure']
    verify(original_failed[0]['stdout']); verify(original_failed[0]['stderr'])
    rows = [check_actual(row, old_dir) for row in original['rows'] if row['mode'] != 'build' and row['actualExitCode'] == 0]
    assert len(rows) == 11 and sum(row['actual']['checks'] for row in rows) == 237
    rows.append(check_actual(retry['rows'][1], new_dir))
    expected = set(read(U / 'functional-modes-r2.json')['modes'])
    assert len(rows) == 12 and len({row['mode'] for row in rows}) == 12 and {row['mode'] for row in rows} == expected
    for result in [original, retry]:
        verify(result['sourceManifest'])
        manifest = read(Path(result['sourceManifest']['path']))
        assert len(manifest['files']) == 913
        source = Path(manifest['source'])
        assert all(sha(source / row['path']) == row['sha256'] for row in manifest['files'])
        for row in result['nativeInputs']: verify(row)
    old_manifest = read(Path(original['sourceManifest']['path']))
    new_manifest = read(Path(retry['sourceManifest']['path']))
    old_rows = {row['path']: row for row in old_manifest['files']}
    new_rows = {row['path']: row for row in new_manifest['files']}
    assert old_rows.keys() == new_rows.keys()
    delta = sorted(path for path in old_rows if old_rows[path]['sha256'] != new_rows[path]['sha256'])
    lifecycle = 'tests/RizToolbox.PlaybackStartInitialization.Tests/ProgressClipResourceLifecycleChecks.cs'
    assert delta == sorted(['tests/RizToolbox.GameCamera.Tests/NativeCameraChecks.cs',
        'tests/RizToolbox.InitializationRootFree.Tests/Program.cs',
        'tests/RizToolbox.InitializationRootFree.Tests/RizToolbox.InitializationRootFree.Tests.csproj', lifecycle])
    managed = [path for path in old_rows if path.startswith('src/')]
    native = [path for path in old_rows if path.startswith('native/')]
    assert len(managed) == 484 and len(native) == 48
    assert all(old_rows[path]['sha256'] == new_rows[path]['sha256'] for path in managed + native)
    assert {Path(row['path']).name: row['sha256'] for row in original['nativeInputs']} == {Path(row['path']).name: row['sha256'] for row in retry['nativeInputs']}
    original_source = (Path(old_manifest['source']) / lifecycle).read_bytes()
    corrected_source = (Path(new_manifest['source']) / lifecycle).read_bytes()
    needle = b'                    sdk.Speed(1.25);'
    newline = b'\r\n' if b'\r\n' in original_source else b'\n'
    insertion = newline.join([
        b'                    // Confirmation retains rollback ownership until this prepared transaction is disposed.',
        b'                    // Release that reservation before starting the next independent SDK transaction.',
        b'                    prepared.Dispose();', needle])
    assert original_source.count(needle) == 1 and corrected_source == original_source.replace(needle, insertion)
    delta_proof = read(new_dir / 'basis/test-only-delta.json')
    assert delta_proof['passed'] is True and delta_proof['onlyExplicitDisposalAdded'] is True and delta_proof['existingAssertionsUnmodified'] is True
    products = read(new_dir / 'build-products.json')
    for row in products: verify(row)
    OUT.mkdir()
    retained = []
    originals = [original_path, retry_path, old_dir / 'resource-sdk-current.result.json',
        old_dir / 'resource-sdk-current.stdout.log', old_dir / 'resource-sdk-current.stderr.log',
        new_dir / 'basis/test-only-delta.json', new_dir / 'basis/prepared-disposal-only.patch']
    for row in rows:
        originals.extend([Path(row['sourceResult']['path']), Path(row['stdout']['path']), Path(row['stderr']['path'])])
    for path in dict.fromkeys(originals):
        target = OUT / 'retained' / path.relative_to(U)
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil_data = path.read_bytes(); target.write_bytes(shutil_data)
        assert sha(target) == sha(path)
        retained.append(dict(original=fact(path), retained=fact(target)))
    result = dict(kind='explicit-eleven-original-successes-and-one-corrected-sdk-success', passed=True,
        collectedAtUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(), processes=12,
        checks=sum(row['actual']['checks'] for row in rows), rows=rows,
        sourceRuns=[dict(role='eleven-original-actual-successes', aggregate=fact(original_path), aggregatePassed=False,
            aggregateTerminal=True, successfulCases=11, checks=237, failedSdkCaseRetained=True),
            dict(role='one-test-disposal-corrected-actual-success', aggregate=fact(retry_path), aggregatePassed=True,
            aggregateTerminal=True, successfulCases=1, checks=retry['checks'])],
        inputSourceFiles=913, managedProductInputs=484, nativeInputs=48, unchangedAllManagedAndNativeInputs=True,
        testOnlyDeltaPaths=delta, testDelta=fact(new_dir / 'basis/test-only-delta.json'),
        originalFailedSdkCase=fact(old_dir / 'resource-sdk-current.result.json'),
        originalFailedAggregateRetainedUnchanged=True, functionalSubscopeComplete=True,
        fullIncrementAcceptanceComplete=False, mainApplied=False, productionAdmission=False,
        gameExecuted=False, installed=False, fullBCompleted=False, retained=retained)
    (OUT / 'test-result.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
    manifest = [fact(path) for path in OUT.rglob('*') if path.is_file()]
    (OUT / 'hash-manifest.json').write_text(json.dumps(dict(files=manifest), indent=2), encoding='utf-8')
    print(json.dumps(dict(passed=True, processes=12, checks=result['checks'], output=str(OUT))))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--check-ready', action='store_true')
    args = parser.parse_args()
    path = U / 'sdk-current-dispose-r1/test-result.json'
    state = read(path) if path.exists() else {}
    ready = bool(state.get('passed') is True and state.get('finishedAtUtc'))
    if args.check_ready:
        print(json.dumps(dict(ready=ready, terminal=bool(state.get('finishedAtUtc')), passed=state.get('passed'))))
    else:
        assert ready, 'The fresh corrected SDK case has not actually succeeded.'
        collect()
