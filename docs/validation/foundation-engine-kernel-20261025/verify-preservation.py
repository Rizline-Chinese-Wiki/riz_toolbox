"""Check prior frozen artifacts without pretending intentional source edits did not happen."""
from pathlib import Path
from datetime import datetime, timezone
import hashlib
import json
import zipfile

batch=Path(__file__).resolve().parent; repo=batch.parents[2]
def read(path): return json.loads(path.read_text(encoding='utf-8-sig'))
def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
checked=[]
intent=read(batch/'intentional-source-changes.json')['changed']
allowed={row['path']:row for row in intent}
for name,count in (('foundation-early-loader-20261025',576),('foundation-fixed-publisher-20261025',12),('foundation-early-fixed-20261025',14)):
    previous=batch.parent/name;rows=read(previous/'source-manifest.json')['files']
    proof=read(previous/'source-archive-check.json'); archive_path=previous/'source-inputs.zip'
    assert len(rows)==count and sha(archive_path)==proof['archiveSha256']
    assert sha(previous/'source-manifest.json')==proof['manifestSha256']
    with zipfile.ZipFile(archive_path) as archive:
        assert archive.testzip() is None and set(archive.namelist())=={row['path'] for row in rows}
        assert all(hashlib.sha256(archive.read(row['path'])).hexdigest()==row['sha256'] for row in rows)
    changed=[]
    for row in rows:
        current=sha(repo/row['path'])
        if current!=row['sha256']:
            assert name=='foundation-early-loader-20261025' and row['path'] in allowed
            assert allowed[row['path']]['beforeSha256']==row['sha256'] and allowed[row['path']]['afterSha256']==current
            changed.append(row['path'])
    assert len(changed)==(4 if count==576 else 0)
    checked.append(dict(batch=name,archivedSources=count,archiveSha256=proof['archiveSha256'],archiveUnchanged=True,intentionalCurrentChanges=changed))
unity=Path('D:/SteamLibrary/steamapps/common/Rizline/UnityPlayer.dll')
unity_hash=sha(unity);assert unity_hash=='43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1'
value=dict(checkedAtUtc=datetime.now(timezone.utc).isoformat(),priorMilestones=checked,
    originalUnityPlayer=dict(path=str(unity),sha256=unity_hash,matched=True),
    installedCheck='previous-installation-check.json',otherOriginalFilesCheck='original-game-check.json',
    installedFiles=18,installedArchivedSources=326,gameExecuted=False,installed=False)
(batch/'preservation-check.json').write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps(dict(archivedSources=602,intentionalCurrentChanges=4,allArchivedHashesMatched=True,unityPlayerUnchanged=True)))
