"""Join 22 recorded successful children with five fresh actual suite runs."""
import hashlib
import json
import re
import shutil
from datetime import datetime, timezone
from pathlib import Path

CACHE = Path('D:/riz-build-cache-next')
MAIN = CACHE / 'playback-stock-formal-main-integration-r1-20261101'
OLD = CACHE / 'foundation-stock-formal-full-r2-20261101'
NEW = CACHE / 'foundation-stock-formal-full-continuation-r4-20261101'
CORE = CACHE / 'foundation-stock-formal-core-r1-20261101/test-result.json'
OUT = CACHE / 'foundation-stock-formal-full-joined-r1-20261101'
SUITES = ['Core', 'Adapter', 'Hosting', 'Game', 'Runtime', 'Sdk', 'Scope',
          'Diagnostics', 'PackageLifecycle', 'Module', 'Mutation', 'Data',
          'Storage', 'Services', 'GameResources', 'GameAppearance', 'GameCamera',
          'Hooks', 'Gameplay', 'Settings', 'Visual', 'Performance', 'AudioContent',
          'UiInput', 'RunModification', 'Judgements', 'SingleEvent']


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def fact(path, expected=None):
    path = Path(path).resolve()
    body = path.read_bytes()
    digest = hashlib.sha256(body).hexdigest()
    assert expected is None or digest == expected, f'Changed input: {path}'
    return dict(path=str(path), sha256=digest, bytes=len(body))


def write(path, value):
    Path(path).write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def inventory(proof, root):
    rows = {}
    for row in proof['compiledSources']:
        fact(row['path'], row['sha256'])
        relative = Path(row['path']).relative_to(root / 'source').as_posix()
        assert relative not in rows
        rows[relative] = row
    return rows


def run():
    assert not OUT.exists(), 'Preserve previous execution evidence.'
    old_path, new_path = OLD / 'progress.json', NEW / 'test-result.json'
    old, new, core = read(old_path), read(new_path), read(CORE)
    assert core['passed'] and core['runnerExitCode'] == 0 and len(core['inputs']) == 760
    assert old['kind'] == new['kind'] == 'foundation-managed-snapshot-checks'
    assert old['passed'] is False and old['failure'] is None and old['fullManagedMatrix']
    assert not (OLD / 'test-result.json').exists()
    assert not (MAIN / 'full-r2.driver.exit.txt').exists()
    assert [row['name'] for row in old['projects']] == SUITES[:22]
    assert new['passed'] and new['failure'] is None and not new['fullManagedMatrix']
    assert new['requestedSuites'] == SUITES[22:]
    assert [row['name'] for row in new['projects']] == SUITES[22:]
    assert old['sourceProof'] == new['sourceProof']
    assert new['sourceProof']['sha256'] == fact(CORE)['sha256']
    assert (MAIN / 'full-continuation-r4.driver.exit.txt').read_text().strip() == '0'
    previous, continuation = inventory(old, OLD), inventory(new, NEW)
    assert set(previous) == set(continuation)
    assert all(row['sha256'] == continuation[name]['sha256'] for name, row in previous.items())
    inputs = {}
    for proof in (old, new):
        assert not proof['gameExecuted'] and not proof['installed']
        for row in proof['inputs']:
            current = fact(row['path'], row['sha256'])
            key = str(Path(row['path']).resolve()).casefold()
            assert key not in inputs or inputs[key] == current
            inputs[key] = current
    projects, logs = [], []
    for proof, root, proof_path, reused in ((old, OLD, old_path, True), (new, NEW, new_path, False)):
        for row in proof['projects']:
            assert row['passed'] and row['buildExitCode'] == row['testExitCode'] == 0
            name = row['name']
            build_log, test_log = root / (name + '-build.log'), root / (name + '-tests.log')
            text = test_log.read_text(encoding='utf-8-sig')
            match = re.search(r'(?m)(\d+)/(\d+)[^\r\n]*passed\.?\s*$', text)
            assert match and int(match[1]) == int(match[2]) == row['checks'] > 0
            assert row['summary'] in text
            assert re.search(r'(?m)^\s*0\s+(?:个错误|Error\(s\))\s*$', build_log.read_text(encoding='utf-8-sig'))
            projects.append(dict(row, actualExecutionManifest=fact(proof_path),
                                 actualBuildLog=fact(build_log), actualTestLog=fact(test_log),
                                 actualExecutedSourceRoot=str(root / 'source'),
                                 completedChildRowReusedFromInterruptedParent=reused))
            logs.extend((fact(build_log), fact(test_log)))
    assert len(projects) == 27 and sum(row['checks'] for row in projects) == 2465
    OUT.mkdir()
    compiled = []
    for relative, row in continuation.items():
        destination = OUT / 'source' / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(row['path'], destination)
        compiled.append(fact(destination, row['sha256']))
    write(OUT / 'source-before.json', compiled)
    manifest = dict(schemaVersion=1, kind='foundation-managed-snapshot-checks', passed=True,
                    observedAtUtc=datetime.now(timezone.utc).isoformat(), sourceProof=new['sourceProof'],
                    inputs=list(inputs.values()), compiledSources=compiled, projects=projects, failure=None,
                    requestedSuites=SUITES, fullManagedMatrix=True, distributionSuiteRun=False,
                    continuation=True, originalParentTerminalSuccessClaimed=False,
                    originalParentHandle='58199 missing; no matching live process at recovery',
                    reusedCompletedSuites=22, newlyExecutedSuites=5,
                    originalProgress=fact(old_path), continuationExecution=fact(new_path),
                    sourceSetsByteIdentical=True, collector=fact(__file__),
                    explanation='22 actual zero-exit completed child rows plus five fresh actual zero-exit suites; interrupted parent has no terminal success.',
                    gameExecuted=False, installed=False, productionAdmissionVerified=False)
    write(OUT / 'test-result.json', manifest)
    extras = [fact(MAIN / 'Run-RemainingFoundation-r4.ps1')]
    for suffix in ('exit.txt', 'stdout.log', 'stderr.log'):
        extras.append(fact(MAIN / ('full-continuation-r4.driver.' + suffix)))
    for suffix in ('stdout.log', 'stderr.log'):
        extras.append(fact(MAIN / ('full-r2.driver.' + suffix)))
    write(OUT / 'continuation-proof.json', dict(kind='formal-foundation-interrupted-run-continuation',
          passed=True, sourceSetsByteIdentical=True, originalParentTerminalSuccessClaimed=False,
          actualCompletedChildRows=22, actualFreshSuites=5, actualPassingChecks=2465,
          result=fact(OUT / 'test-result.json'), logs=logs, extras=extras))
    print(json.dumps(dict(passed=True, reusedSuites=22, freshSuites=5, checks=2465,
                          sourceFiles=len(compiled), result=fact(OUT / 'test-result.json'))))


if __name__ == '__main__':
    run()
