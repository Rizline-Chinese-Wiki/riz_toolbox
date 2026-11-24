"""Freeze declared full-build inputs before the sole root builder runs."""
from pathlib import Path
from datetime import datetime, timezone
import difflib
import hashlib
import json
import zipfile

batch=Path(__file__).resolve().parent
repo=batch.parents[2]
def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
suffixes={'.cs','.csproj','.props','.targets','.ps1','.cpp','.c','.h','.asm','.json'}
paths=set()
for directory in ('src','tests','examples','scripts','native','templates','tools','packaging'):
    for path in (repo/directory).rglob('*'):
        if path.is_file() and path.suffix in suffixes and not {'bin','obj','out'}.intersection(path.relative_to(repo).parts):
            paths.add(path)
for name in ('native/RizToolbox.Doorstop/RIZLINE-MODIFICATIONS.md',
             'docs/validation/foundation-early-loader-20261025/verify-early-loader.py',
             'docs/validation/foundation-engine-kernel-20261025/verify-engine-kernel.py',
             'Directory.Build.props','Directory.Build.targets','RizToolbox.sln','dependencies.lock.json','global.json'):
    path=repo/name
    if path.is_file(): paths.add(path)
rows=sorted([dict(path=p.relative_to(repo).as_posix(),sha256=sha(p)) for p in paths],key=lambda row:row['path'])
assert len(rows)>576 and len({row['path'] for row in rows})==len(rows)
previous=batch.parent/'foundation-early-loader-20261025'
old=json.loads((previous/'source-manifest.json').read_text(encoding='utf-8-sig'))['files']
changes=[dict(path=row['path'],beforeSha256=row['sha256'],afterSha256=sha(repo/row['path']))
         for row in old if sha(repo/row['path'])!=row['sha256']]
assert {row['path'] for row in changes}=={
    'native/RizToolbox.JudgeContext/EngineNodeContext.cpp','scripts/Build.ps1',
    'scripts/Test-EngineNodeNative.ps1','scripts/Test-EngineNodeManaged.ps1'},changes
destination=batch/'build-inputs-before.json'
assert not destination.exists(),'Preserve the preceding freeze rather than overwrite it.'
destination.write_text(json.dumps(dict(frozenAtUtc=datetime.now(timezone.utc).isoformat(),files=rows,
    scope='Declared full source set plus two actually invoked offline verifiers; external toolchain/SDK/cache identities are separate.'),indent=2)+'\n',encoding='utf-8')
history=batch/'history';history.mkdir(exist_ok=True)
with zipfile.ZipFile(previous/'source-inputs.zip') as archive:
    for row in changes:
        body=archive.read(row['path'])
        assert hashlib.sha256(body).hexdigest()==row['beforeSha256']
        label=row['path'].replace('/','__')
        (history/(label+'.before')).write_bytes(body)
        difference=''.join(difflib.unified_diff(body.decode('utf-8-sig').splitlines(keepends=True),
            (repo/row['path']).read_text(encoding='utf-8-sig').splitlines(keepends=True),
            fromfile=row['path']+' (previous archived)',tofile=row['path']+' (new frozen)'))
        (history/(label+'.diff')).write_text(difference,encoding='utf-8',newline='\n')
(batch/'intentional-source-changes.json').write_text(json.dumps(dict(previousBatch=previous.name,previousDeclaredSources=576,
    changed=changes,oldArchiveUnmodified=True),indent=2)+'\n',encoding='utf-8')
print(json.dumps(dict(declaredSourceInputs=len(rows),intentionalPreviousChanges=len(changes))))
