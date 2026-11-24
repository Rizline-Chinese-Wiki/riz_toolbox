"""Check local documentation targets and the unchanged599 frozen inputs."""
from pathlib import Path
from datetime import datetime,timezone
from urllib.parse import unquote
import hashlib,json,re

batch=Path(__file__).resolve().parent;repo=batch.parents[2]
paths=[repo/name for name in ('README.md','docs/IMPLEMENTATION-STATUS.md','.docs/CURRENT-CHECKPOINT.md',
    '.docs/foundation-expansion/CHECKLIST.md','docs/2026-10-25_底层-独立协程运行核验证-report.md')]
paths.extend(batch/name for name in ('README.md','scope.md','next-integration.md',
    'review-entry.md','review-runtime.md','kernel-dependency-review.md'))
supplement=batch.parent/'foundation-kernel-install-20261025'
paths.extend(supplement/name for name in ('README.md','review.md'))
missing=[];checked=0
for path in paths:
    for raw in re.findall(r'\]\(([^)]+)\)',path.read_text(encoding='utf-8-sig')):
        target=raw.strip().split(' "',1)[0].strip('<>')
        if not target or target.startswith(('#','http://','https://','mailto:','codex:','app:')):continue
        target=unquote(target.split('#',1)[0]);checked+=1
        if not (path.parent/target).resolve().exists():missing.append(dict(file=path.relative_to(repo).as_posix(),target=target))
rows=json.loads((batch/'build-inputs-before.json').read_text(encoding='utf-8-sig'))['files']
final=json.loads((supplement/'source-final-manifest.json').read_text(encoding='utf-8-sig'))['files']
overlay=json.loads((supplement/'source-overlay-manifest.json').read_text(encoding='utf-8-sig'))['files']
allowed={row['path']:row for row in overlay}
assert set(allowed)=={'scripts/Install.ps1','scripts/Test-InstallSafety.ps1','scripts/Test-InstallRecovery.ps1'}
assert {row['path'] for row in final}=={row['path'] for row in rows}
assert all(row['sha256']==next(item['sha256'] for item in rows if item['path']==row['path'])
           for row in final if row['path'] not in allowed)
assert all(row['sha256']==allowed[row['path']]['sha256'] for row in final if row['path'] in allowed)
changed=[row['path'] for row in final if hashlib.sha256((repo/row['path']).read_bytes()).hexdigest()!=row['sha256']]
value=dict(checkedAtUtc=datetime.now(timezone.utc).isoformat(),files=[p.relative_to(repo).as_posix() for p in paths],
    checkedLocalLinks=checked,missing=missing,frozenSourcesChecked=len(rows),changedFrozenSources=changed,
    independentlyVerifiedInstallerOverlayFiles=len(overlay),unchangedCoreSourceFiles=596,
    scope='Local file targets and frozen input bytes; heading anchors and remote HTTP not checked.')
(batch/'documentation-check.json').write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
assert not missing and not changed,value
print(json.dumps(dict(files=len(paths),localLinks=checked,missing=0,frozenSources=len(rows),changed=0)))
