"""Freeze the production cold-start candidate against the previous 613 inputs."""
from pathlib import Path
from datetime import datetime,timezone
import hashlib
import json
import sys
import zipfile

batch=Path(__file__).resolve().parent
repo=batch.parents[2]
prior=batch.parent/'foundation-winhttp-forward-20261025'
revision=sys.argv[1] if len(sys.argv)==2 else 'r3'
assert revision.isalnum() and len(sys.argv)<=2
run=batch/'attempts'/revision
run.mkdir(parents=True,exist_ok=False)
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
old=json.loads((prior/'source-final-manifest.json').read_text())['files']
assert len(old)==613
changed=[dict(path=row['path'],previousSha256=row['sha256'],sha256=sha(repo/row['path'])) for row in old if sha(repo/row['path'])!=row['sha256']]
expected={'scripts/Build-Doorstop.ps1','native/RizToolbox.Doorstop/riz_winhttp.h',
    'native/RizToolbox.Doorstop/riz_winhttp.c','native/RizToolbox.Doorstop/RIZLINE-MODIFICATIONS.md'}
assert {row['path'] for row in changed}==expected,changed
added=[
    'native/RizToolbox.Doorstop/riz_winhttp_doorstop.h','native/RizToolbox.Doorstop/riz_winhttp_doorstop.c',
    'native/RizToolbox.WinHttpColdFixture/ColdApi.h','native/RizToolbox.WinHttpColdFixture/Importer.cpp','native/RizToolbox.WinHttpColdFixture/Host.cpp',
    'native/RizToolbox.EngineBootstrap/EngineGameProfile.h',
    'scripts/Build-WinHttpColdFixture.ps1','scripts/Test-WinHttpColdFixture.ps1',
    'docs/validation/foundation-doorstop-cold-20261025/prepare-cold-contract.py',
    'docs/validation/foundation-doorstop-cold-20261025/verify-cold.py',
    'docs/validation/foundation-doorstop-cold-20261025/research/extract-engine-game-profile.py',
    'docs/validation/foundation-doorstop-cold-20261025/research/engine-game-profile.json',
    'docs/validation/foundation-doorstop-cold-20261025/research/engine-game-profile.md',
]
assert not set(added)&{row['path'] for row in old}
paths=sorted([row['path'] for row in old]+added)
assert len(paths)==len(set(paths))==626
files=[dict(path=name,sha256=sha(repo/name)) for name in paths]
with zipfile.ZipFile(run/'source-inputs.zip','x',compression=zipfile.ZIP_DEFLATED) as archive:
    for row in files:
        data=(repo/row['path']).read_bytes();assert hashlib.sha256(data).hexdigest()==row['sha256'];archive.writestr(row['path'],data)
manifest=dict(frozenAtUtc=datetime.now(timezone.utc).isoformat(),previousSources=613,sourceInputs=626,
    previousSourcesChanged=changed,newInputs=added,files=files,sourceArchiveSha256=sha(run/'source-inputs.zip'))
(run/'inputs-before.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8',newline='\n')
previous=json.loads((prior/'real-game-preservation-check.json').read_text())
game=Path('D:/SteamLibrary/steamapps/common/Rizline')
preservation={}
for key in ('installedFiles','originalFiles'):
    preservation[key]=[]
    for row in previous[key]:
        digest=sha(game/row['path']);assert digest==row['sha256']
        preservation[key].append(dict(path=row['path'],sha256=digest))
environment=dict(realGame=preservation,systemWinHttp=dict(path='C:/Windows/System32/winhttp.dll',sha256=sha(Path('C:/Windows/System32/winhttp.dll'))),
    originalPackage=json.loads((batch.parent/'foundation-engine-kernel-20261025/package-manifest.json').read_text())['files'],
    gameExecuted=False,installed=False)
for row in environment['originalPackage']:assert sha(repo/'artifacts/package'/row['path'])==row['sha256']
(run/'environment-before.json').write_text(json.dumps(environment,indent=2)+'\n',encoding='utf-8',newline='\n')
print(json.dumps(dict(revision=revision,sourceInputs=626,changedPreviousSources=4,newInputs=13,archiveSha256=manifest['sourceArchiveSha256'])))
