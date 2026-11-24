"""Prepare a hash-pinned merge and rollback package without applying any source."""
from pathlib import Path
import datetime
import difflib
import hashlib
import json
import zipfile

R = Path(__file__).resolve().parent
MAIN = Path('C:/Users/71957/Desktop/riz_toolbox')
SOURCE = R / 'source'
OUT = R / 'integration-preparation-r4'
SKIP = {'bin', 'obj', '.git', '__pycache__'}
README = 'cfbac4691d36edc5daba513aa62d5856ae61e29bf553d0dc2979832750847364'


def sha(data):
    return hashlib.sha256(data).hexdigest()


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def save(path, value):
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False), encoding='utf-8')


def fact(path):
    data = path.read_bytes()
    return dict(path=str(path), sha256=sha(data), bytes=len(data))


def archive(path, rows, root):
    with zipfile.ZipFile(path, 'x', compression=zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for row in rows:
            data = (root / row['path']).read_bytes()
            assert sha(data) == row['sha256'], 'Source changed: ' + row['path']
            z.writestr(row['path'], data)
    with zipfile.ZipFile(path) as z:
        assert z.testzip() is None and len(z.namelist()) == len(rows)
        assert all(sha(z.read(row['path'])) == row['sha256'] for row in rows)
    return fact(path)


assert not OUT.exists(), 'Keep preparation packages immutable; choose a new scope.'
baseline_path = R / 'baseline-manifest.json'
functional_path = R / 'functional-matrix-r2/source-manifest.json'
foundation_path = R / 'foundation-r2/source-manifest.json'
baseline = read(baseline_path)['files']
functional = {row['path']: row for row in read(functional_path)['files']}
foundation = {row['path']: row for row in read(foundation_path)['files']}
base = {row['path']: row for row in baseline}
assert len(base) == 894
assert sha((MAIN / 'README.md').read_bytes()) == README
assert all(sha((MAIN / row['path']).read_bytes()) == row['sha256'] for row in baseline)
current = []
for path in sorted(SOURCE.rglob('*')):
    if not path.is_file() or set(path.relative_to(SOURCE).parts) & SKIP:
        continue
    data = path.read_bytes()
    current.append(dict(path=path.relative_to(SOURCE).as_posix(), sha256=sha(data), bytes=len(data)))
candidate = {row['path']: row for row in current}
assert len(candidate) == 913 and candidate.keys() == functional.keys() == foundation.keys()
functional_delta = [p for p in candidate if candidate[p]['sha256'] != functional[p]['sha256']]
foundation_delta = [p for p in candidate if candidate[p]['sha256'] != foundation[p]['sha256']]
assert functional_delta == [
    'tests/RizToolbox.GameCamera.Tests/NativeCameraChecks.cs',
    'tests/RizToolbox.InitializationRootFree.Tests/Program.cs',
    'tests/RizToolbox.InitializationRootFree.Tests/RizToolbox.InitializationRootFree.Tests.csproj',
]
assert foundation_delta == functional_delta[1:]
managed = [row for row in current if row['path'].startswith('src/')]
native = [row for row in current if row['path'].startswith('native/')]
assert len(managed) == 484 and len(native) == 48
assert all(row['sha256'] == functional[row['path']]['sha256'] for row in managed + native)
changes = []
patches = []
for path in sorted(base.keys() | candidate.keys()):
    before = base.get(path)
    after = candidate.get(path)
    if before is not None and after is not None and before['sha256'] == after['sha256']:
        continue
    assert after is not None, 'This preparation does not authorize deletions.'
    destination = MAIN / path
    if before is None:
        assert not destination.exists(), 'An added destination already exists: ' + path
    old = destination.read_bytes() if before is not None else b''
    new = (SOURCE / path).read_bytes()
    category = 'managed' if path.startswith('src/') else 'native' if path.startswith('native/') else 'test'
    changes.append(dict(path=path, kind='added' if before is None else 'modified', category=category,
        beforeSha256=before['sha256'] if before is not None else None, afterSha256=after['sha256'],
        beforeBytes=len(old), afterBytes=len(new)))
    patches.extend(difflib.unified_diff(old.decode('utf-8-sig').splitlines(keepends=True),
        new.decode('utf-8-sig').splitlines(keepends=True),
        fromfile='a/' + path if before is not None else '/dev/null', tofile='b/' + path))
assert len(changes) == 53
OUT.mkdir()
save(OUT / 'candidate-source-manifest.json', dict(origin=str(SOURCE), files=current,
    capturedAtUtc=datetime.datetime.now(datetime.timezone.utc).isoformat()))
save(OUT / 'baseline-source-manifest.json', read(baseline_path))
(OUT / 'candidate-review.patch').write_text(''.join(patches), encoding='utf-8')
before_archive = archive(OUT / 'main-before-894.zip', baseline, MAIN)
after_archive = archive(OUT / 'candidate-913.zip', current, SOURCE)
assert all(sha((MAIN / row['path']).read_bytes()) == row['sha256'] for row in baseline)
assert all(sha((SOURCE / row['path']).read_bytes()) == row['sha256'] for row in current)
assert sha((MAIN / 'README.md').read_bytes()) == README
report = dict(kind='progress-resource-integration-preparation', passed=True, integrityOnly=True,
    functionalValidationComplete=False, formalSdkComplete=False, acceptanceComplete=False,
    mainApplicationReady=False, mainApplied=False, fullBCompleted=False, productionAdmission=False,
    gameExecuted=False, dllMainExecuted=False, installed=False,
    observedAtUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
    baselineInputs=894, candidateInputs=913, managedProductInputs=484, nativeInputs=48,
    changes=changes, changedFiles=len(changes), addedFiles=sum(row['kind'] == 'added' for row in changes),
    modifiedFiles=sum(row['kind'] == 'modified' for row in changes),
    changedManagedFiles=sum(row['category'] == 'managed' for row in changes),
    changedNativeFiles=sum(row['category'] == 'native' for row in changes),
    changedTestFiles=sum(row['category'] == 'test' for row in changes),
    readOnlyContextReview=fact(R / 'independent-root-free-contract-review-r1/test-result.json'),
    readOnlyPrefixReview=fact(R / 'independent-root-free-prefix-review-r2/test-result.json'),
    preparationRunner=fact(Path(__file__).resolve()),
    runnerDerivation=fact(R / 'integration-preparation-r4-runner-derivation.json'),
    functionalR2Manifest=fact(functional_path), foundationR2Manifest=fact(foundation_path),
    functionalR2TestOnlyDeltas=functional_delta, foundationR2TestOnlyDeltas=foundation_delta,
    managedAndNativeInputsUnchangedSinceFunctionalR2=True, mainBaselineUnchanged=True,
    readmeSha256=README, archives=[before_archive, after_archive],
    reviewPatch=fact(OUT / 'candidate-review.patch'),
    requiredAcceptanceScopes=[
        dict(scope='T two-run SDK closure', directory='D:/riz-build-cache-next/playback-stock-stop-occurrence-r1-20261102',
            requires='Old 6 and new 12 actual exit 0, same frozen T inputs, completed closure collector.'),
        dict(scope='U functional-matrix-r2', directory=str(R / 'functional-matrix-r2'),
            requires='All 12 current-product modes actual exit 0 with independently parsed passed results.'),
        dict(scope='U mapped-r3', directory=str(R / 'mapped-r3'),
            requires='Fresh build, metadata and all four mapped joint modes actual exit 0; no game method or DllMain.'),
        dict(scope='U managed foundation', directory=str(R / 'foundation-r6'),
            requires='Exact test-only delta proof, original four actual RootFree modes from r5 plus one fresh resource mode from r6, fresh solution, SDK14 and six Release; link prior full28/2599 without rewriting its later solution failure.'),
    ],
    applyPolicy='After every gate has actually closed: reverify every before hash and added destination, copy only the 53 pinned paths, reverify all 913 files, then fresh-main validation and case review. This script never applies files.')
save(OUT / 'preparation-result.json', report)
artifacts = [fact(path) for path in sorted(OUT.iterdir()) if path.is_file()]
save(OUT / 'hash-manifest.json', dict(kind='immutable-integration-preparation-integrity', files=artifacts))
assert all(sha(Path(row['path']).read_bytes()) == row['sha256'] for row in artifacts)
print(json.dumps(dict(passed=True, integrityOnly=True, mainApplied=False, changes=len(changes),
    added=report['addedFiles'], modified=report['modifiedFiles'], managed=report['changedManagedFiles'],
    native=report['changedNativeFiles'], tests=report['changedTestFiles'], output=str(OUT))))
