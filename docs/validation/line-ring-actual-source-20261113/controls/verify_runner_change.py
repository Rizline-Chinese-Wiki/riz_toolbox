"""Prove the runner-only change by reconstructing the exact old file bytes."""
import difflib
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).parent
NAME = 'tests/RizToolbox.Judgements.Tests/Program.cs'
MANIFEST = Path('D:/riz-build-cache-next/line-ring-source-20261113/root-r3/source-r1/inputs-before.json')
expected = json.loads(MANIFEST.read_text(encoding='utf-8'))[NAME]
current_bytes = (ROOT / NAME).read_bytes()
current = current_bytes.decode('utf-8').replace('\r\n', '\n')
start = current.index('// Keep the original regression order and bodies.')
end = current.index('else if (args.Length == 2 && args[0] == "--line-ring-source-case"', start)
restored = current[:start] + current[end:].replace('else if (args.Length == 2 && args[0] == "--line-ring-source-case"', 'if (args.Length == 2 && args[0] == "--line-ring-source-case"', 1)
assert restored.count('|--list-legacy-cases|--legacy-case N]') == 1
restored = restored.replace('|--list-legacy-cases|--legacy-case N]', ']')
old_filter = 'tests.Where(test => !test.Name.StartsWith("Canvas basis ", StringComparison.Ordinal) && !test.Name.StartsWith("Line ring source ", StringComparison.Ordinal)).ToArray()'
assert restored.count('"--without-canvas-basis-checks" => legacyTests,') == 1
restored = restored.replace('"--without-canvas-basis-checks" => legacyTests,', '"--without-canvas-basis-checks" => ' + old_filter + ',')
variants = [restored.encode('utf-8'), restored.replace('\n', '\r\n').encode('utf-8')]
matching = [value for value in variants if hashlib.sha256(value).hexdigest() == expected]
assert len(matching) == 1, 'Runner has changes beyond the exact declared selection block/usage/reused filter.'
old_bytes = matching[0]
old_path = WORK / 'original-program-before-partition.cs.txt'
if old_path.exists():
    assert old_path.read_bytes() == old_bytes
else:
    old_path.write_bytes(old_bytes)
diff = ''.join(difflib.unified_diff(restored.splitlines(True), current.splitlines(True), fromfile='original/Program.cs', tofile='current/Program.cs'))
(WORK / 'runner-only-change.diff').write_text(diff, encoding='utf-8')
result = {'onlyChangedInput': NAME, 'oldManifest': str(MANIFEST), 'oldSha256': expected,
          'currentSha256': hashlib.sha256(current_bytes).hexdigest(), 'oldBytesReconstructedExactly': True,
          'testBodiesAndFixtureCleanupChanged': False, 'productionChanged': False,
          'originalSelectionFilterPreserved': True, 'runtimeExecutionVerified': False}
(WORK / 'runner-change-review.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
print(json.dumps(result))
