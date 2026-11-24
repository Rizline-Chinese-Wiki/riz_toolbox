"""Retain the read-only review and point-in-time selected source originals."""
from pathlib import Path
import datetime
import hashlib
import json
import shutil

R = Path(__file__).resolve().parent
MAIN = Path('C:/Users/71957/Desktop/riz_toolbox')
A = R / 'next-gap-audit-r1'
O = R / 'next-gap-audit-retention-r1'
DOC = MAIN / 'docs/validation/playback-direct-audio-20261102/progress-resource-next-audit-r1'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
fact = lambda p: dict(path=str(p), sha256=sha(p), bytes=p.stat().st_size)
read = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))
save = lambda p, v: p.write_text(json.dumps(v, indent=2), encoding='utf-8')

assert not O.exists() and not DOC.exists(), 'Preserve earlier retention attempts.'
assert sha(A / 'artifact-index.json') == '891f5f4b3357645ba2f88c0da91ad9a8d8d8fb56da90af584322b18a60c743ae'
assert sha(A / '2026-11-02_progress-clip-next-gap-review.md') == 'ed911420b4106bbecf100574636825a4d7045afe15e36be5fb704c406b614b9e'
assert sha(A / 'next-function-contract.json') == '46e60dd9963da101929d9df5c435ad0896c22636f10cd27fcc12cedf754e4ecd'
index = read(A / 'artifact-index.json')
assert len(index['files']) == 14
for row in index['files']:
    p = Path(row['path'])
    assert p.resolve().is_relative_to(A.resolve())
    assert sha(p) == row['sha256'] and p.stat().st_size == row['length']
check = read(A / 'static-verification-result.json')
assert check['passed'] and check['checkCount'] == 18
assert not check['functionalValidationComplete'] and not check['productionAdmission']
assert check['newLongTests'] == check['dllMainCalls'] == check['gameMethodCalls'] == check['gameProcessesStarted'] == 0
assert len(read(A / 'next-function-contract.json')['acceptance']) == 7
source = read(A / 'source-manifest.json')['rows']
for row in source:
    p = Path(row['path'])
    assert p.resolve().is_relative_to(MAIN.resolve())
    assert p.relative_to(MAIN).as_posix() == row['relativePath']
    assert sha(p) == row['sha256'] and p.stat().st_size == row['length']
candidate = read(R / 'candidate-review-r1/manifest.json')
assert len(candidate['sourceFiles']) == 894
assert all(sha(MAIN / row['path']) == row['sha256'] for row in candidate['sourceFiles'])
assert sha(MAIN / 'README.md') == candidate['mainReadmeBefore']['sha256']
O.mkdir()
DOC.mkdir()
retained = []
for p in sorted(A.iterdir()):
    assert p.is_file()
    target = DOC / p.name
    shutil.copy2(p, target)
    assert sha(p) == sha(target)
    retained.append(dict(original=fact(p), retained=fact(target)))
snapshots = []
for row in source:
    p = Path(row['path'])
    target = DOC / 'selected-source-originals' / row['relativePath']
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(p, target)
    assert sha(target) == row['sha256']
    snapshots.append(dict(originalPath=row['path'], originalSha256=row['sha256'], retained=fact(target)))
result = dict(
    kind='read-only-progress-resource-next-contract-retention', passed=True,
    observedAtUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
    auditArtifactCount=len(retained), selectedSourceOriginals=len(snapshots),
    retainedArtifacts=retained, selectedSourceSnapshots=snapshots,
    currentMainFrozenSourceFiles=894, currentMainProductInputs=482,
    currentMainSourceUnchanged=True, mainReadmeUnchanged=True,
    staticFileAssertions=18, behavioralChecksExecuted=0,
    productImplementationStarted=False, functionalValidationComplete=False,
    gameExecuted=False, productionAdmission=False, fullBCompleted=False,
    script=fact(Path(__file__)),
)
save(O / 'test-result.json', result)
save(DOC / 'retention-result.json', result)
shutil.copy2(Path(__file__), DOC / Path(__file__).name)
print(json.dumps({key: result[key] for key in ['passed','auditArtifactCount','selectedSourceOriginals','staticFileAssertions','currentMainSourceUnchanged']}))
