"""Retain the reviewed final test freeze and actual completed scopes; never apply products."""
from pathlib import Path
import datetime
import hashlib
import json

R = Path(__file__).resolve().parent
T = Path('D:/riz-build-cache-next/playback-stock-stop-occurrence-r1-20261102')
MAIN = Path('C:/Users/71957/Desktop/riz_toolbox')
OUT = MAIN / 'docs/validation/playback-direct-audio-20261102/progress-resource-candidate-progress-r2'
EXCLUDE = {'source', 'solution-source', 'sdk-source', 'foundation-environment', 'release-managed', 'bin', 'obj', 'packages', 'local-reference-feed'}
sha = lambda data: hashlib.sha256(data).hexdigest()
read = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))
artifacts = []


def retain(path, prefix=''):
    path = path.resolve()
    root = T if prefix == 'T' else R
    relative = Path(prefix) / path.relative_to(root)
    destination = OUT / relative
    data = path.read_bytes()
    if destination.exists():
        assert destination.read_bytes() == data
        return
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_bytes(data)
    assert destination.read_bytes() == data
    artifacts.append(dict(original=str(path), retained=relative.as_posix(), sha256=sha(data), bytes=len(data)))


def retain_evidence_tree(folder, prefix=''):
    for path in sorted(folder.rglob('*')):
        if not path.is_file() or set(path.relative_to(folder).parts) & EXCLUDE:
            continue
        if path.suffix.lower() in {'.json', '.log', '.txt', '.md', '.patch', '.py', '.ps1', '.cs', '.csproj', '.after', '.before'}:
            retain(path, prefix)


foundation_path = R / 'foundation-r7/foundation-final-summary.json'
assert foundation_path.is_file(), 'Foundation r5 has not reached its real terminal result.'
foundation = read(foundation_path)
assert foundation['passed'] is True and foundation['complete'] is True and foundation['evidenceIntegrityPassed'] is True
assert foundation['remainingOnlyActualOsExitCode'] == 0 and not foundation['notCompletedSteps']
root_free = read(R / 'foundation-r7/root-free-test-result.json')
assert root_free['passed'] is True and root_free['actualCaseCount'] == 5 and root_free['freshProcesses'] == 1 and root_free['inheritedProcesses'] == 4
assert all(read(Path(row['process']['path']))['actualExitCode'] == 0 and row['actual']['passed'] is True for row in root_free['rows'])
baseline = read(R / 'baseline-manifest.json')
assert all(sha((MAIN / row['path']).read_bytes()) == row['sha256'] for row in baseline['files'])
assert sha((MAIN / 'README.md').read_bytes()) == 'cfbac4691d36edc5daba513aa62d5856ae61e29bf553d0dc2979832750847364'
preparation = read(R / 'integration-preparation-r4/preparation-result.json')
candidate = read(R / 'integration-preparation-r4/candidate-source-manifest.json')['files']
freeze = read(R / 'foundation-r7/source-manifest.json')['files']
assert {row['path']: row['sha256'] for row in candidate} == {row['path']: row['sha256'] for row in freeze}
assert len(candidate) == 913 and preparation['changedFiles'] == 53
assert all(sha((R / 'source' / row['path']).read_bytes()) == row['sha256'] for row in candidate)
review = read(R / 'independent-root-free-contract-review-r1/test-result.json')
assert review['passed'] is True
assert not OUT.exists(), 'Preserve the old scope and use a new retention directory.'
OUT.mkdir(parents=True)
for name in ['baseline-manifest.json', 'IMPLEMENTATION-CONTRACT.md', '2026-11-03_progress-resource-implementation.md',
             'functional-inputs-r2.json', 'functional-modes-r2.json', 'shutdown-audit-r2.json',
             'Prepare-ResourceIntegrationR4.py', 'integration-preparation-r4-runner-derivation.json',
             'integration-preparation-r4-runner.patch', 'Retain-ResourceCandidateProgressR4.py']:
    retain(R / name)
for path in sorted((R / 'integration-preparation-r4').iterdir()):
    if path.is_file(): retain(path)
for folder in ['foundation-r7', 'foundation-r5', 'independent-root-free-contract-review-r1', 'independent-root-free-prefix-review-r2', 'foundation-r3/root-free-diagnostics-r3']:
    retain_evidence_tree(R / folder)
for folder in ['core-r2', 'full-r2', 'solution-r2']:
    retain_evidence_tree(R / 'foundation-r2' / folder)
for folder in ['foundation-r2', 'foundation-r3', 'foundation-r4', 'foundation-r6']:
    for path in sorted((R / folder).glob('*')):
        if path.is_file() and path.suffix in {'.json', '.txt', '.log'}: retain(path)
release = read(R / 'foundation-r7/release-r5/managed-build-manifest.json')
assert release['passed'] is True and release['buildExitCode'] == 0 and len(release['files']) == 6
for row in release['files']:
    path = Path(row['path']); assert sha(path.read_bytes()) == row['sha256']; retain(path)

functional = read(R / 'functional-matrix-r2/test-result.json')
functional_rows = [row for row in functional['rows'] if row.get('mode') != 'build' and row.get('actualExitCode') == 0]
assert len(functional_rows) >= 11 and all(row['actual']['passed'] is True for row in functional_rows)
retain(R / 'functional-matrix-r2/test-result.json')
for suffix in ['.result.json', '.stdout.log', '.stderr.log', '.exit.txt', '.launch.json']:
    retain(R / 'functional-matrix-r2' / ('resource-sdk-current' + suffix))
for name in ['build.result.json', 'basis/test-only-delta.json', 'basis/prepared-disposal-only.patch', 'resource-sdk-current.launch.json']:
    retain(R / 'sdk-current-dispose-r1' / name)
retain(R / 'sdk-current-dispose-r1/test-result.json')
retain(R / 'sdk-disposal-observer-r1/observer-result.json')
for row in functional_rows:
    mode = row['mode']
    for suffix in ['.result.json', '.stdout.log', '.stderr.log', '.exit.txt', '.launch.json']:
        path = R / 'functional-matrix-r2' / (mode + suffix)
        if path.exists(): retain(path)
mapped = read(R / 'mapped-r3/test-result.json')
retain(R / 'mapped-r3/test-result.json')
for row in mapped['rows']:
    if row.get('actualExitCode') != 0: continue
    for suffix in ['.result.json', '.stdout.log', '.stderr.log', '.exit.txt', '.launch.json']:
        path = R / 'mapped-r3' / (row['name'] + suffix)
        if path.exists(): retain(path)
for name in ['scope.json', 'source-manifest.json', 'build-products.json']:
    path = R / 'mapped-r3' / name
    if path.exists(): retain(path)
for folder in ['final-audit-r4']:
    retain_evidence_tree(R / 'mapped-r3' / folder)
retain_evidence_tree(T / 'closure-continuation-audit-r4-20261103', 'T')
continuation = read(T / 'formal-continuation-r3/test-result.json')
retain(T / 'formal-continuation-r3/test-result.json', 'T')
retain(T / 'closure-after-shutdown-observer-r1/observer-result.json', 'T')
old_pack = MAIN / 'docs/validation/playback-direct-audio-20261102/progress-resource-candidate-progress-r1/scope-index.json'
old_pack_data = old_pack.read_bytes()
functional_complete = functional.get('passed') is True and len(functional_rows) == 12
mapped_complete = mapped.get('passed') is True and len(mapped['rows']) == 6 and all(row['actualExitCode'] == 0 for row in mapped['rows'])
index = dict(kind='reviewed-resource-candidate-current-progress-retention', passed=True, integrityOnly=True,
    mainApplied=False, mainApplicationReady=False, incrementAcceptanceComplete=False,
    formalSdkComplete=False, fullBCompleted=False, productionAdmission=False, installed=False,
    gameExecuted=False, gameUiAccessed=False, accountAccessed=False,
    observedAtUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(), candidateInputs=913,
    managedProductInputs=484, nativeInputs=48, changedPaths=53, mainProductBaselineUnchanged=True,
    foundationR7Complete=True, inheritedActualFullSuites=28, inheritedActualFullChecks=2599,
    fullMatrixRerunInR5=False, rootFreeActualCases=5, freshRootFreeModes=1, inheritedRootFreeModes=4,
    freshRootFreeChecks=root_free['freshChecks'], inheritedRootFreeChecks=80, totalRootFreeChecks=root_free['checks'],
    functionalComplete=functional_complete, originalSdkCaseActualFailed=True, correctedSdkCaseAccepted=False,
    functionalCompletedModes=len(functional_rows),
    functionalCompletedChecks=sum(row['actual']['checks'] for row in functional_rows),
    mappedComplete=mapped_complete, mappedActualCompletedModes=[row['name'] for row in mapped['rows'] if row.get('actualExitCode') == 0],
    TContinuationCompletedModes=sum(row.get('actualExitCode') == 0 for row in continuation['rows']),
    TContinuationComplete=continuation.get('passed') is True,
    previousProgressScope=dict(path=str(old_pack), sha256=sha(old_pack_data), bytes=len(old_pack_data)),
    unknownResourceCleanupCompleted=False, unknownResourceHostRootsRetained=4, artifacts=artifacts)
for row in artifacts:
    assert sha((OUT / row['retained']).read_bytes()) == row['sha256']
assert all(sha((MAIN / row['path']).read_bytes()) == row['sha256'] for row in baseline['files'])
(OUT / 'scope-index.json').write_text(json.dumps(index, indent=2), encoding='utf-8')
print(json.dumps(dict(passed=True, integrityOnly=True, retainedFiles=len(artifacts), freshRootFreeChecks=root_free['freshChecks'], totalRootFreeChecks=root_free['checks'],
    functionalComplete=functional_complete, mappedComplete=mapped_complete, mainApplied=False, output=str(OUT))))
