"""Stage the two descriptor-readiness changes against the complete1171 snapshot."""
import hashlib
import json
from pathlib import Path
import shutil

ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).resolve().parent
BASE = ROOT / 'docs/validation/complete-solution-hold-20261114'
sha = lambda path: hashlib.sha256(Path(path).read_bytes()).hexdigest()
if sha(BASE / 'validation-summary.json') != '94b40bdc6e452b24b8fdaeeb9de10fe8917c1fb9ab1736a1d35b356708c26218': raise RuntimeError('Complete base differs')
state = json.loads((BASE / 'solution/result.json').read_text(encoding='utf-8'))
files = {**state['frozenInputs'], **state['additionalSolutionInputs']}
if not state['passed'] or len(files) != 1171: raise RuntimeError('Complete candidate build not available')
names = ('src/RizToolbox.Adapter/Gameplay/GameplayNativeBackend.cs', 'tests/RizToolbox.GameplaySourceGeneration.Tests/Program.cs')
if any((WORK / name).exists() for name in names): raise RuntimeError('Preserve staged sources')
for name in names:
    source = BASE / 'source' / name
    if sha(source) != files[name]: raise RuntimeError('Frozen base source changed')
    target = WORK / name; target.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(source, target)
print('Staged two isolated sources; main/source archives unchanged')
