"""Freeze the three installer changes on top of the verified core23528 source set."""
from pathlib import Path
from datetime import datetime,timezone
import hashlib,json,re,shutil,zipfile

batch=Path(__file__).resolve().parent;repo=batch.parents[2]
core=batch.parent/'foundation-engine-kernel-20261025'
def read(path):return json.loads(path.read_text(encoding='utf-8-sig'))
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def save(name,value):(batch/name).write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
inputs=read(batch/'inputs-before.json')['files']
assert len(inputs)==7 and all(sha(repo/row['path'])==row['sha256'] for row in inputs)
runs=read(batch/'terminal-observations.json')
assert runs['safetySessionId']==66373 and runs['recoverySessionId']==67106
assert runs['safetyExitCode']==runs['recoveryExitCode']==read(batch/'safety.run.json')['exitCode']==read(batch/'recovery.run.json')['exitCode']==0
red=read(batch/'before-fix-run.json');assert red['exitCode']==1
assert 'Invalid package' in (batch/'before-fix-safety.log').read_text(encoding='utf-8-sig')
safety_log=(batch/'safety.log').read_text(encoding='utf-8-sig')
match=re.search(r'Results:\s*(.+?results\.json)',safety_log)
assert match
safety_path=Path(match.group(1).strip())
safety=read(safety_path);assert safety['count']==len(safety['passed'])==16 and not safety['gameWasLaunched']
shutil.copy2(safety_path,batch/'safety-results.json')
recovery=read(batch/'recovery/results.json')
assert recovery['count']==recovery['passed'] and recovery['count']>0 and all(row['passed'] for row in recovery['results'])
original=read(core/'source-manifest.json')['files'];assert len(original)==599
allowed={'scripts/Install.ps1','scripts/Test-InstallSafety.ps1','scripts/Test-InstallRecovery.ps1'}
overlay=[];final=[]
for row in original:
    current=sha(repo/row['path']);final.append(dict(path=row['path'],sha256=current))
    if current!=row['sha256']:
        assert row['path'] in allowed
        overlay.append(dict(path=row['path'],beforeSha256=row['sha256'],sha256=current))
assert {row['path'] for row in overlay}==allowed
save('source-overlay-manifest.json',dict(files=overlay,baseBatch=core.name,baseSessionId=23528,
    scope='Exactly three installer/test script changes after core full build; all other596 source inputs identical.'))
save('source-final-manifest.json',dict(files=final,coreFullSessionId=23528,installerSafetySessionId=66373,installerRecoverySessionId=67106,
    scope='Composite current599 inputs: core code/full1617 proof plus separately executed installer fixes. Not a second full build.'))
archive_path=batch/'final-source-inputs.zip'
if not archive_path.exists():
    with zipfile.ZipFile(archive_path,'w',compression=zipfile.ZIP_DEFLATED) as archive:
        for row in final:archive.write(repo/row['path'],row['path'])
with zipfile.ZipFile(archive_path) as archive:
    assert archive.testzip() is None and set(archive.namelist())=={row['path'] for row in final}
    assert all(hashlib.sha256(archive.read(row['path'])).hexdigest()==row['sha256']==sha(repo/row['path']) for row in final)
save('source-archive-check.json',dict(entries=599,archive='final-source-inputs.zip',archiveSha256=sha(archive_path),
    manifestSha256=sha(batch/'source-final-manifest.json'),allEntryHashesMatched=True,compositeValidation=True))
package=read(core/'package-manifest.json')['files'];assert len(package)==23
assert all(sha(repo/'artifacts/package'/row['path'])==row['sha256'] for row in package)
save('package-check.json',dict(files=package,count=23,unchangedSinceCore23528=True,sourcePackageManifestSha256=sha(core/'package-manifest.json')))
# Retain the actual synthetic repositories: none of these paths is the game.
raw=batch/'raw';raw.mkdir(exist_ok=True)
for name,path in (('safety-fixture',Path(safety['testRoot'])),('recovery-fixtures',Path(recovery['testRoot']))):
    assert path.resolve().is_relative_to((repo/'artifacts').resolve()) and 'SteamLibrary' not in str(path)
    shutil.copytree(path,raw/name,dirs_exist_ok=True)
save('input-integrity-check.json',dict(checked=7,beforeMatchesAfter=True,changed=[],coreSourcesUnchanged=596,installerChanges=3))
game=Path('D:/SteamLibrary/steamapps/common/Rizline')
installed=read(core/'previous-installation-check.json')['files']
originals=read(core/'original-game-check.json')['files']
originals.append(dict(path='UnityPlayer.dll',expected='43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1'))
for row in installed+originals:
    row['actual']=sha(game/row['path']);assert row['actual']==row['expected']
save('real-game-preservation-check.json',dict(installedFiles=installed,originalFiles=originals,allMatched=True,realGameExecuted=False,realGameModified=False))
save('summary.json',dict(verifiedAtUtc=datetime.now(timezone.utc).isoformat(),coreFullSessionId=23528,coreSuites=25,corePassed=1617,
    safetySessionId=66373,safetyChecks=safety['count'],recoverySessionId=67106,recoveryChecks=recovery['count'],
    beforeFixActualExitCode=1,beforeFixFailure='Invalid package manifest',declaredInputFiles=7,changedScripts=3,
    finalCompositeSources=599,packageFiles=23,compositeSourceArchiveSha256=sha(archive_path),
    realGameModified=False,realGameExecuted=False,secondFullBuildClaim=False))
print(json.dumps(read(batch/'summary.json')))
