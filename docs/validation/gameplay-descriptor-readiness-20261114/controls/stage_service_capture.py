"""Stage service-side lease expiry separately from optional clock source lifetime."""
from pathlib import Path
import hashlib
import json
import shutil

ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).resolve().parent
BASE = ROOT / 'docs/validation/complete-solution-hold-20261114'
name = 'src/RizToolbox.Adapter/Gameplay/GameplayService.cs'
source = BASE / 'source' / name; target = WORK / name
if target.exists(): raise RuntimeError('Preserve staged service')
state = json.loads((BASE / 'solution/result.json').read_text(encoding='utf-8'))
if hashlib.sha256(source.read_bytes()).hexdigest() != state['frozenInputs'][name]: raise RuntimeError('Base service differs')
target.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(source, target)
print('Staged exact service capture source; main unchanged')
