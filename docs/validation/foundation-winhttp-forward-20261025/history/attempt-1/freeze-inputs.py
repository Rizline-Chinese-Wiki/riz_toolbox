"""Freeze the new forwarding component while preserving the prior 604 inputs."""
from pathlib import Path
from datetime import datetime, timezone
import hashlib
import json
import zipfile

batch=Path(__file__).resolve().parent
repo=batch.parents[2]
previous=batch.parent/'foundation-early-kernel-20261025'
base=json.loads((previous/'source-final-manifest.json').read_text())['files']
new=[
    'native/RizToolbox.Doorstop/riz_winhttp.h',
    'native/RizToolbox.Doorstop/riz_winhttp.c',
    'native/RizToolbox.Doorstop/riz_winhttp_entry.asm',
    'native/RizToolbox.WinHttpFixture/WinHttpForwardFixture.cpp',
    'native/RizToolbox.WinHttpFixture/Importer.cpp',
    'native/RizToolbox.WinHttpFixture/WinHttpFixtureApi.h',
    'scripts/Build-WinHttpForwardFixture.ps1',
    'scripts/Test-WinHttpForwardFixture.ps1',
    'docs/validation/foundation-winhttp-forward-20261025/verify-winhttp-forward.py',
]
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
assert len(base)==604
assert not any(sha(repo/row['path'])!=row['sha256'] for row in base), 'Previous sources changed; investigate without overwriting them.'
assert not set(new).intersection(row['path'] for row in base)
paths=sorted([row['path'] for row in base]+new)
rows=[dict(path=path,sha256=sha(repo/path)) for path in paths]
assert len(rows)==613 and len(set(paths))==613
destination=batch/'inputs-before.json'
assert not destination.exists(), 'Use a separately named revision rather than erase a prior freeze.'
destination.write_text(json.dumps(dict(frozenAtUtc=datetime.now(timezone.utc).isoformat(),files=rows,
    previousSources=604,newComponentInputs=new,previousSourcesChanged=[],
    scope='Fixed system WinHTTP forwarding integration only; no full managed build, game installation, or production loader adoption.'),indent=2)+'\n',encoding='utf-8')
with zipfile.ZipFile(batch/'source-inputs.zip','x',compression=zipfile.ZIP_DEFLATED) as archive:
    for row in rows:
        data=(repo/row['path']).read_bytes();assert hashlib.sha256(data).hexdigest()==row['sha256']
        archive.writestr(row['path'],data)
game_root=Path('D:/SteamLibrary/steamapps/common/Rizline')
prior=json.loads((previous/'real-game-preservation-check.json').read_text())
preservation={}
for name in ('installedFiles','originalFiles'):
    preservation[name]=[]
    for row in prior[name]:
        actual=sha(game_root/row['path']);assert actual==row['expected']==row['actual']
        preservation[name].append(dict(path=row['path'],expected=actual))
system=Path('C:/Windows/System32/winhttp.dll')
(batch/'environment-before.json').write_text(json.dumps(dict(systemWinHttp=dict(path=str(system),sha256=sha(system),bytes=system.stat().st_size),
    realGame=preservation,gameExecuted=False,gameModified=False,sourceArchiveSha256=sha(batch/'source-inputs.zip')),indent=2)+'\n',encoding='utf-8')
print(json.dumps(dict(declaredSources=len(rows),previousSourcesChanged=0,newInputs=len(new),archiveSha256=sha(batch/'source-inputs.zip'))))
