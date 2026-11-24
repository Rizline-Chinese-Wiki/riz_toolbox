"""Seal only the authorized RootFree test adaptation and source-equivalence facts."""
from pathlib import Path
import datetime
import difflib
import hashlib
import json

O = Path(__file__).resolve().parent
R = O.parent
B = O / 'basis'
S = R / 'source'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
fact = lambda p: dict(path=str(p), sha256=sha(p), bytes=p.stat().st_size)
read = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))
assert not (O / 'test-delta.json').exists(), 'Never replace a sealed test delta.'
names = [
    ('tests/RizToolbox.InitializationRootFree.Tests/Program.cs', 'Program.before.cs'),
    ('tests/RizToolbox.InitializationRootFree.Tests/RizToolbox.InitializationRootFree.Tests.csproj', 'InitializationRootFree.before.csproj')]
changes = []
patches = []
old = read(R / 'foundation-r3/source-manifest.json')
expected = {row['path']: row for row in old['files']}
rows = []
for row in old['files']:
    p = S / row['path']
    assert p.is_file()
    rows.append(dict(path=row['path'], sha256=sha(p), bytes=p.stat().st_size))
actual = {row['path']: row for row in rows}
assert sorted(p for p in actual if actual[p]['sha256'] != expected[p]['sha256']) == sorted(p for p, _ in names)
assert sum(p.startswith('src/') for p in actual) == 484
assert sum(p.startswith('native/') for p in actual) == 48
for relative, before in names:
    before_path, after_path = B / before, S / relative
    assert sha(before_path) == expected[relative]['sha256']
    after_copy = B / (Path(relative).name + '.after')
    after_copy.write_bytes(after_path.read_bytes())
    changes.append(dict(path=relative, before=fact(before_path), after=fact(after_copy), candidate=fact(after_path)))
    before_text = before_path.read_text(encoding='utf-8-sig')
    after_text = after_path.read_text(encoding='utf-8-sig')
    patches.extend(difflib.unified_diff(before_text.splitlines(True), after_text.splitlines(True), 'a/' + relative, 'b/' + relative))
patch = B / 'initialization-root-free-current-resource-contract.patch'
patch.write_text(''.join(patches), encoding='utf-8')
result = dict(kind='foundation-r5-root-free-current-resource-test-only-delta', passed=True,
    sealedAtUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
    priorSourceManifest=fact(R / 'foundation-r3/source-manifest.json'), changes=changes, patch=fact(patch),
    totalFrozenInputCount=913, managedProductInputCount=484, nativeSourceInputCount=48,
    all484ManagedProductHashesUnchanged=True, all48NativeSourceHashesUnchanged=True,
    onlyChangedCSharpInput=names[0][0], affectedTestProject='RizToolbox.InitializationRootFree.Tests',
    unaffected28ManagedSuitesReexecuted=False,
    priorFailurePreserved=fact(R / 'foundation-r3/foundation-driver.result.json'),
    priorOriginalFourthModePreserved=fact(R / 'foundation-r3/root-free-diagnostics-r3/original-tracker-enumerator-free-fault/process-result.json'),
    priorDiagnosticPreserved=fact(R / 'foundation-r3/root-free-diagnostics-r3/test-result.json'),
    currentResourceFixtureRequired=True, currentR6HelperRequired=True, oldProfileCompatibilityReintroduced=False,
    allFiveCasesAreUnknownCleanupResponsibilityChecks=True,
    resourceUnknownCase=dict(cleanupCompleted=False, expectedHostRootsRetained=4, expectedRouterRootsReleased=False,
        expectedRefusalType='UnsupportedSignatureException', expectedNoNativeFreeReplay=True),
    gameExecuted=False, installed=False, productionAdmission=False)
(O / 'test-delta.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
print(json.dumps({k: v for k, v in result.items() if k != 'changes'}, indent=2))
