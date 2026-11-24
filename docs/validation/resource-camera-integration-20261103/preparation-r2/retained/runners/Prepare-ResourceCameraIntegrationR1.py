"""Stage the exact U-then-V composition without changing either candidate or main."""
from pathlib import Path
import argparse
import datetime
import difflib
import hashlib
import json
import shutil
import zipfile

P = Path('D:/riz-build-cache-next/resource-camera-integration-r1-20261103')
U = Path('D:/riz-build-cache-next/playback-progress-resource-continuity-r1-20261102')
V = Path('D:/riz-build-cache-next/game-camera-scale-x-r1-20261103')
T = Path('D:/riz-build-cache-next/playback-stock-stop-occurrence-r1-20261102')
MAIN = Path('C:/Users/71957/Desktop/riz_toolbox')
LIFECYCLE = 'tests/RizToolbox.PlaybackStartInitialization.Tests/ProgressClipResourceLifecycleChecks.cs'
README_HASH = 'cfbac4691d36edc5daba513aa62d5856ae61e29bf553d0dc2979832750847364'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
fact = lambda p: dict(path=str(p), sha256=sha(p), bytes=p.stat().st_size)
read = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))
now = lambda: datetime.datetime.now(datetime.timezone.utc).isoformat()


def save(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def index(path, count):
    rows = read(path)['files']
    result = {row['path']: row for row in rows}
    assert len(rows) == len(result) == count
    for rel in result:
        assert not Path(rel).is_absolute() and '..' not in Path(rel).parts
    return result


def verify_source(root, rows):
    for rel, row in rows.items():
        path = (root / rel).resolve()
        assert path.is_relative_to(root.resolve()) and path.is_file()
        assert sha(path) == row['sha256'] and path.stat().st_size == row['bytes'], str(path)


def verify_fact(row):
    path = Path(row['path'])
    assert sha(path) == row['sha256']
    assert 'bytes' not in row or path.stat().st_size == row['bytes']


def changes(before, after):
    assert before.keys() <= after.keys(), 'Source deletion is not part of this increment.'
    return [dict(path=rel, kind='added' if rel not in before else 'modified', before=before.get(rel), after=after[rel])
            for rel in sorted(after) if rel not in before or before[rel]['sha256'] != after[rel]['sha256']]


def archive(path, rows, origins):
    with zipfile.ZipFile(path, 'x', compression=zipfile.ZIP_DEFLATED) as target:
        for rel, row in sorted(rows.items()):
            data = (origins[rel] / rel).read_bytes()
            assert hashlib.sha256(data).hexdigest() == row['sha256']
            target.writestr(rel, data)
    with zipfile.ZipFile(path) as target:
        assert len(target.namelist()) == len(rows)
        assert all(hashlib.sha256(target.read(rel)).hexdigest() == row['sha256'] for rel, row in rows.items())
    return fact(path)


def patch(path, delta, before_origins, after_origins):
    text = []
    for row in delta:
        rel = row['path']
        old = (before_origins[rel] / rel).read_text(encoding='utf-8-sig').splitlines(True) if row['before'] else []
        new = (after_origins[rel] / rel).read_text(encoding='utf-8-sig').splitlines(True)
        text.extend(difflib.unified_diff(old, new, 'before/' + rel if row['before'] else '/dev/null', 'after/' + rel))
    path.write_text(''.join(text), encoding='utf-8')


def observe_gates():
    paths = {
        'T combined eighteen SDK': T / 'formal-sdk-combined-after-shutdown-r1/test-result.json',
        'T complete integration closure': T / 'integration-closure-after-shutdown-r1/test-result.json',
        'U twelve functional modes': U / 'functional-combined-after-disposal-r1/test-result.json',
        'U independent exact SDK coverage': U / 'sdk-disposal-collector-review-r1/final-closure-review.json',
    }
    rows = []
    for label, path in paths.items():
        row = dict(gate=label, path=str(path), present=path.is_file(), ready=False)
        if path.is_file():
            data = read(path)
            row.update(result=fact(path), passed=data.get('passed') is True)
            if label == 'T combined eighteen SDK':
                row['ready'] = data.get('passed') is True and data.get('processes') == 18 and data.get('formalSdkMatrixComplete') is True and not data.get('pending') and not data.get('livePids')
            elif label == 'T complete integration closure':
                row['ready'] = data.get('passed') is True and data.get('formalSdkMatrixComplete') is True and data.get('currentIncrementValidationComplete') is True and data.get('sdkProcesses') == 18 and len(data.get('completedScopes', [])) == 22
            elif label == 'U independent exact SDK coverage':
                row['ready'] = data.get('passed') is True and data.get('autoCoverageClosed') is True
            else:
                row['ready'] = data.get('passed') is True and len(data.get('rows', [])) == 12
        rows.append(row)
    # This reports scheduling readiness only. Admission must still verify all
    # referenced bytes, actual rows, final collectors and before hashes.
    return dict(kind='resource-camera-current-gate-observation', observedAtUtc=now(),
                allFourReportedGatesReady=all(row['ready'] for row in rows), rows=rows,
                integrationAdmissionVerified=False, mainApplicationReady=False, mainApplied=False)


def prepare():
    assert not P.exists(), 'Preserve earlier staged sources and use a fresh scope after changes.'
    base = index(U / 'baseline-manifest.json', 894)
    current_u = index(V / 'baseline-manifest.json', 913)
    next_u = index(U / 'sdk-current-dispose-r1/source-manifest.json', 913)
    current_v = index(V / 'functional-r1/source-manifest.json', 915)
    verify_source(MAIN, base)
    verify_source(U / 'source', current_u)
    verify_source(U / 'sdk-current-dispose-r1/source', next_u)
    verify_source(V / 'source', current_v)
    assert sha(MAIN / 'README.md') == README_HASH
    test_delta = changes(current_u, next_u)
    assert len(test_delta) == 1 and test_delta[0]['path'] == LIFECYCLE
    assert test_delta[0]['before']['sha256'] == 'f8fea816545050b81a328fdae0b3ed7dd4e1788a5e12465fd8dd69b038c78348'
    assert test_delta[0]['after']['sha256'] == '0abcb623cebb5f6bef5e03e69efb37ccd214b3f70a877e6bdf3ed37ec71d8904'
    old = (U / 'source' / LIFECYCLE).read_text(encoding='utf-8-sig')
    new = (U / 'sdk-current-dispose-r1/source' / LIFECYCLE).read_text(encoding='utf-8-sig')
    marker = '                    sdk.Speed(1.25);\n'
    addition = ('                    // Confirmation retains rollback ownership until this prepared transaction is disposed.\n'
                '                    // Release that reservation before starting the next independent SDK transaction.\n'
                '                    prepared.Dispose();\n')
    assert old.count(marker) == 1 and old.replace(marker, addition + marker) == new
    for rel in current_u:
        if rel.startswith(('src/', 'native/')):
            assert current_u[rel] == next_u[rel]
    v_delta = read(V / 'functional-r1/delta.json')['paths']
    assert len(v_delta) == 9
    final = dict(next_u)
    origins = {rel: U / 'sdk-current-dispose-r1/source' for rel in next_u}
    for row in v_delta:
        rel = row['path']
        assert row['before'] == current_u.get(rel)
        assert row['after'] == current_v[rel]
        final[rel] = row['after']
        origins[rel] = V / 'source'
    assert len(final) == 915 and final[LIFECYCLE] == next_u[LIFECYCLE]
    assert [rel for rel in final if rel in current_v and final[rel] != current_v[rel]] == [LIFECYCLE]
    assert sum(rel.startswith('src/') for rel in final) == 485
    assert sum(rel.startswith('native/') for rel in final) == 48
    u_delta = changes(base, next_u)
    actual_v_delta = changes(next_u, final)
    combined_delta = changes(base, final)
    assert len(u_delta) == 53 and len(actual_v_delta) == 9 and len(combined_delta) == 61
    assert sum(row['kind'] == 'added' for row in combined_delta) == 21
    assert LIFECYCLE not in {row['path'] for row in actual_v_delta}
    required = [
        (U / 'foundation-r7/foundation-final-summary.json', lambda j: j.get('passed') is True and j.get('evidenceIntegrityPassed') is True),
        (U / 'mapped-r3/final-audit-r4/CLOSE-EVIDENCE.json', lambda j: j.get('passed') is True),
        (V / 'functional-r1/test-result.json', lambda j: j.get('passed') is True and j.get('checks') == 55),
        (V / 'foundation-r1/test-result.json', lambda j: j.get('passed') is True and j.get('managedChecks') == 2620 and j.get('sdkChecks') == 14 and j.get('releaseAssemblyCount') == 6),
        (V / 'symbols-r1/test-result.json', lambda j: j.get('passed') is True and j.get('pePdbPairs') == 11 and j.get('documentCount') == 990),
    ]
    for path, predicate in required:
        assert predicate(read(path)), str(path)
    P.mkdir()
    staged = P / 'candidate-915'
    for rel, row in sorted(final.items()):
        target = staged / rel
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(origins[rel] / rel, target)
        assert sha(target) == row['sha256']
    verify_source(staged, final)
    save(P / 'main-before-manifest.json', dict(source=str(MAIN), files=list(base.values())))
    save(P / 'resource-after-sdk-manifest.json', dict(source=str(U / 'sdk-current-dispose-r1/source'), files=list(next_u.values())))
    save(P / 'joint-candidate-manifest.json', dict(source=str(staged), files=list(final.values())))
    save(P / 'staged-deltas.json', dict(resource=u_delta, camera=actual_v_delta, combined=combined_delta,
                                      exactLifecycleDisposeDelta=test_delta, vBaselineDifferenceOnly=[LIFECYCLE]))
    base_origins = {rel: MAIN for rel in base}
    u_origins = {rel: U / 'sdk-current-dispose-r1/source' for rel in next_u}
    patch(P / '01-resource-53.patch', u_delta, base_origins, u_origins)
    patch(P / '02-camera-9.patch', actual_v_delta, u_origins, origins)
    patch(P / 'combined-61.patch', combined_delta, base_origins, origins)
    archives = [archive(P / 'main-before-894.zip', base, base_origins),
                archive(P / 'resource-with-sdk-disposal-913.zip', next_u, u_origins),
                archive(P / 'joint-with-sdk-disposal-915.zip', final, origins)]
    gates = observe_gates()
    save(P / 'gates-at-preparation.json', gates)
    verify_source(MAIN, base)
    verify_source(U / 'source', current_u)
    verify_source(U / 'sdk-current-dispose-r1/source', next_u)
    verify_source(V / 'source', current_v)
    assert sha(MAIN / 'README.md') == README_HASH
    result = dict(kind='staged-precise-resource-then-camera-composition', passed=True, integrityOnly=True,
                  createdAtUtc=now(), mainInputs=894, resourceInputs=913, jointInputs=915,
                  managedProductInputs=485, nativeInputs=48, resourceChanges=53, cameraChanges=9,
                  combinedChanges=61, addedFiles=21, modifiedFiles=40, lifecycleDisposePreserved=True,
                  productsIdenticalToValidatedV=True, nativeIdenticalToValidatedU=True,
                  futureSdkSourceSnapshot=fact(U / 'sdk-current-dispose-r1/source-manifest.json'),
                  originalCameraDelta=fact(V / 'functional-r1/delta.json'), completedSubscopes=[fact(p) for p, _ in required],
                  baselineManifest=fact(P / 'main-before-manifest.json'), resourceManifest=fact(P / 'resource-after-sdk-manifest.json'),
                  jointManifest=fact(P / 'joint-candidate-manifest.json'), changes=fact(P / 'staged-deltas.json'),
                  archives=archives, gatesAtPreparation=fact(P / 'gates-at-preparation.json'),
                  runner=fact(Path(__file__).resolve()), stageOnly=True, mainApplied=False,
                  sourceCandidatesModified=False, jointCandidateBuilt=False, jointCandidateValidated=False,
                  allFunctionalScopesComplete=False, integrationAdmissionVerified=False, mainApplicationReady=False,
                  gameExecuted=False, installed=False, productionAdmission=False, fullBCompleted=False,
                  applyPolicy='After four gates and full original-artifact verification: apply only resource53 with before hashes; fresh-U validation; then camera9 with its own before hashes; fresh joint validation. Never copy the older V ZIP wholesale. No deletion or README change.')
    save(P / 'preparation-result.json', result)
    retained = [fact(p) for p in sorted(P.iterdir()) if p.is_file()]
    save(P / 'hash-manifest.json', dict(passed=True, files=retained))
    print(json.dumps(dict(passed=True, stageOnly=True, resourceChanges=53, cameraChanges=9, combinedChanges=61,
                         mainApplied=False, mainApplicationReady=False, output=str(P))))


def audit():
    result = read(P / 'preparation-result.json')
    assert result['passed'] and result['stageOnly'] and result['mainApplied'] is False
    for row in read(P / 'hash-manifest.json')['files']:
        verify_fact(row)
    verify_source(P / 'candidate-915', index(P / 'joint-candidate-manifest.json', 915))
    verify_source(MAIN, index(P / 'main-before-manifest.json', 894))
    assert sha(MAIN / 'README.md') == README_HASH
    observation = observe_gates()
    observation.update(stagedCandidateUnchanged=True, mainBeforeUnchanged=True,
                       preparation=fact(P / 'preparation-result.json'))
    stamp = datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%S%fZ')
    save(P / ('readiness-' + stamp + '.json'), observation)
    print(json.dumps(observation, ensure_ascii=False))
    return 0 if observation['allFourReportedGatesReady'] else 3


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument('--prepare', action='store_true')
    group.add_argument('--audit', action='store_true')
    args = parser.parse_args()
    if args.prepare:
        prepare()
    else:
        raise SystemExit(audit())
