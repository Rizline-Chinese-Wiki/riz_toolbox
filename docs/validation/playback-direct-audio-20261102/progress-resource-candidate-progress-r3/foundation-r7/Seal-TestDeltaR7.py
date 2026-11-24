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
program_relative = names[0][0]
prefix_before = B / 'Program.before-prefix.cs'
prefix_after = S / program_relative
old_literal = '"The resource root release previously failed and remains unresolved: Synthetic unknown GC-root free outcome."'
new_literal = '"UnsupportedSignature: The resource root release previously failed and remains unresolved: Synthetic unknown GC-root free outcome."'
before_bytes = prefix_before.read_bytes()
after_bytes = prefix_after.read_bytes()
assert before_bytes.count(old_literal.encode()) == 1
assert after_bytes == before_bytes.replace(old_literal.encode(), new_literal.encode())
native_abi = B / 'NativeAbi.exception-prefix.cs'
assert 'base("UnsupportedSignature: " + message)' in native_abi.read_text(encoding='utf-8-sig')
assert sha(native_abi) == sha(S / 'src/RizToolbox.Runtime/NativeAbi.cs')
r5_manifest = read(R / 'foundation-r5/source-manifest.json')
r5_files = {row['path']: row for row in r5_manifest['files']}
assert sha(prefix_before) == r5_files[program_relative]['sha256']
r5_four = read(R / 'foundation-r5/root-free-test-result.json')
assert len(r5_four['rows']) == 4 and sum(row['actual']['checks'] for row in r5_four['rows']) == 80
assert all(row['actual']['passed'] is True and row['actual']['mode'] != 'tracker-resource-free-fault' for row in r5_four['rows'])
prefix_patch = B / 'resource-unresolved-exact-exception-prefix.patch'
prefix_patch.write_text(''.join(difflib.unified_diff(prefix_before.read_text(encoding='utf-8-sig').splitlines(True),
    prefix_after.read_text(encoding='utf-8-sig').splitlines(True), 'foundation-r5/' + program_relative,
    'foundation-r7/' + program_relative)), encoding='utf-8')
prefix_delta = dict(kind='foundation-r7-exact-resource-only-exception-message-expectation-delta', passed=True,
    priorSourceManifest=fact(R / 'foundation-r5/source-manifest.json'), before=fact(prefix_before), after=fact(prefix_after),
    patch=fact(prefix_patch), exceptionConstructor=fact(native_abi),
    onlyChangeIsExactFixedExceptionPrefix=True, changedHelperOnlyCalledByResourceFaultMode=True,
    inheritedFourCases=r5_four['rows'], inheritedFourCaseCount=4, inheritedFourChecks=80, fourCasesReexecuted=False,
    preservedPriorDriverFailure=fact(R / 'foundation-r5/foundation-driver.result.json'),
    preservedPriorResourceFailure=fact(R / 'foundation-r5/tracker-resource-free-fault/process-result.json'),
    preservedPriorResourceStdout=fact(R / 'foundation-r5/tracker-resource-free-fault/stdout.log'),
    independentIncrementalReview=fact(R / 'independent-root-free-prefix-review-r2/test-result.json'),
    independentIncrementalReviewManifest=fact(R / 'independent-root-free-prefix-review-r2/manifest.json'),
    allProductHashesUnchanged=True, assertionsWeakened=False)
(O / 'r5-prefix-expectation-delta.json').write_text(json.dumps(prefix_delta, indent=2), encoding='utf-8')
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
result = dict(kind='foundation-r7-root-free-current-resource-test-only-delta', passed=True,
    sealedAtUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
    priorSourceManifest=fact(R / 'foundation-r3/source-manifest.json'), changes=changes, patch=fact(patch),
    r5PrefixExpectationDelta=fact(O / 'r5-prefix-expectation-delta.json'),
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
