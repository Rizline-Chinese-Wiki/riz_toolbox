"""Check local link targets and unchanged frozen integration inputs."""
from pathlib import Path
from datetime import datetime, timezone
from urllib.parse import unquote
import hashlib
import json
import re

batch = Path(__file__).resolve().parent
repo = batch.parents[2]
paths = [repo/name for name in ('README.md','docs/IMPLEMENTATION-STATUS.md',
    '.docs/CURRENT-CHECKPOINT.md','.docs/foundation-expansion/CHECKLIST.md',
    'docs/2026-10-25_底层-固定发布早期链验证-report.md')]
paths.extend(batch/name for name in ('README.md','scope.md','review.md','native-dependency-review.md','next-production-contract.md'))
missing = []
checked = 0
for path in paths:
    for target in re.findall(r'\]\(([^)]+)\)', path.read_text(encoding='utf-8-sig')):
        target = target.strip().split(' "',1)[0].strip('<>')
        if not target or target.startswith(('#','https://','http://','mailto:','codex:','app:')):
            continue
        target = unquote(target.split('#',1)[0])
        checked += 1
        if not (path.parent/target).resolve().exists():
            missing.append(dict(file=path.relative_to(repo).as_posix(), target=target))
inputs = json.loads((batch/'component-inputs-before.json').read_text(encoding='utf-8-sig'))['files']
changed = [row['path'] for row in inputs if hashlib.sha256((repo/row['path']).read_bytes()).hexdigest() != row['sha256']]
value = dict(checkedAtUtc=datetime.now(timezone.utc).isoformat(), files=[p.relative_to(repo).as_posix() for p in paths],
             checkedLocalLinks=checked, missing=missing, frozenSourcesChecked=len(inputs), changedFrozenSources=changed,
             scope='Local file targets and frozen input bytes only; does not check HTTP or heading anchors.')
(batch/'documentation-check.json').write_text(json.dumps(value, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
assert not missing and not changed, value
print(json.dumps(dict(files=len(paths),checkedLocalLinks=checked,missing=len(missing),changedFrozenSources=len(changed))))
