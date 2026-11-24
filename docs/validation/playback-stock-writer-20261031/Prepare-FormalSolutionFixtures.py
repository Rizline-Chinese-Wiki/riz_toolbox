"""Recreate the fixture outputs produced by the already executed Hosting suite.

This preserves the normal full-matrix build environment after a source-only
continuation join. It does not claim that a clean solution build works.
"""
import hashlib
import json
import shutil
from pathlib import Path

CACHE = Path('D:/riz-build-cache-next')
PREVIOUS = CACHE / 'foundation-stock-formal-full-r2-20261101'
JOINED = CACHE / 'foundation-stock-formal-full-joined-r1-20261101'
OUT = CACHE / 'playback-stock-formal-main-integration-r1-20261101/solution-fixture-preparation-r1'


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def fact(path, expected=None):
    path = Path(path).resolve()
    body = path.read_bytes()
    digest = hashlib.sha256(body).hexdigest()
    assert expected is None or digest == expected, str(path)
    return dict(path=str(path), sha256=digest, bytes=len(body))


def run():
    assert not OUT.exists()
    previous = read(PREVIOUS / 'progress.json')
    joined = read(JOINED / 'test-result.json')
    host = next(row for row in previous['projects'] if row['name'] == 'Hosting')
    assert host['passed'] and host['buildExitCode'] == host['testExitCode'] == 0 and host['checks'] == 20
    assert joined['passed'] and joined['sourceSetsByteIdentical']
    for row in previous['compiledSources'] + joined['compiledSources']:
        fact(row['path'], row['sha256'])
    base = Path('source/tests/RizToolbox.Hosting.Tests/Fixtures')
    rows = []
    for name in ('PluginV1', 'PrivateV1', 'PluginV2', 'PrivateV2', 'DefaultOnly'):
        old = PREVIOUS / base / name / 'bin/Release/net6.0'
        new = JOINED / base / name / 'bin/Release/net6.0'
        assert old.is_dir(), str(old)
        for source in sorted(old.iterdir()):
            if not source.is_file():
                continue
            before = fact(source)
            destination = new / source.name
            new.mkdir(parents=True, exist_ok=True)
            if destination.exists():
                fact(destination, before['sha256'])
            else:
                shutil.copyfile(source, destination)
            rows.append(dict(source=before, destination=fact(destination, before['sha256'])))
    assert rows
    OUT.mkdir()
    result = dict(kind='formal-foundation-canonical-hosting-fixture-environment', passed=True,
                  originalHostingActualBuildExitCode=0, originalHostingActualTestExitCode=0,
                  hostingChecks=20, copiedFiles=len(rows), files=rows,
                  originalProgress=fact(PREVIOUS / 'progress.json'),
                  originalBuildLog=fact(PREVIOUS / 'Hosting-build.log'),
                  originalTestLog=fact(PREVIOUS / 'Hosting-tests.log'),
                  foundation=fact(JOINED / 'test-result.json'), runner=fact(__file__),
                  scope='Recreate fixture outputs of the successful full-matrix Hosting step after a source-only join.',
                  cleanSolutionBuildProven=False, productSourceModified=False,
                  gameExecuted=False, installed=False)
    (OUT / 'test-result.json').write_text(json.dumps(result, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    print(json.dumps(dict(passed=True, files=len(rows), result=fact(OUT / 'test-result.json'))))


if __name__ == '__main__':
    run()
