"""Recheck prior frozen milestones and the untouched local game installation."""
from pathlib import Path
from datetime import datetime, timezone
import hashlib
import json
import zipfile

batch = Path(__file__).resolve().parent
repo = batch.parents[2]
game = Path('D:/SteamLibrary/steamapps/common/Rizline')

def sha(data):
    return hashlib.sha256(data).hexdigest()

def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

def save(name, value):
    (batch/name).write_text(json.dumps(value, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')

milestones = []
entry_paths = {'README.md','docs/IMPLEMENTATION-STATUS.md','.docs/CURRENT-CHECKPOINT.md','.docs/foundation-expansion/CHECKLIST.md'}
for name, count in (('foundation-early-loader-20261025',576), ('foundation-fixed-publisher-20261025',12)):
    previous = batch.parent/name
    rows = read(previous/'source-manifest.json')['files']
    assert len(rows) == count and not entry_paths.intersection(row['path'] for row in rows)
    changed = [row['path'] for row in rows if sha((repo/row['path']).read_bytes()) != row['sha256']]
    assert not changed, changed
    proof = read(previous/'source-archive-check.json')
    archive_path = previous/proof['archive']
    assert sha(archive_path.read_bytes()) == proof['archiveSha256']
    with zipfile.ZipFile(archive_path) as archive:
        assert archive.testzip() is None and len(archive.namelist()) == count
        assert all(sha(archive.read(row['path'])) == row['sha256'] for row in rows)
    milestones.append(dict(batch=name, currentSourcesChecked=count, sourceArchiveSha256=proof['archiveSha256'], changed=[]))
old = batch.parent/'foundation-local-protection-host-20261022'
old_rows = read(old/'source-manifest.json')['files']
assert len(old_rows) == 326
old_proof = read(old/'source-archive-check.json')
with zipfile.ZipFile(old/'source-inputs.zip') as archive:
    assert archive.testzip() is None and len(archive.namelist()) == 326
    assert all(sha(archive.read(row['path'])) == row['sha256'] for row in old_rows)
assert sha((old/'source-inputs.zip').read_bytes()) == old_proof['archiveSha256']
installed = read(batch.parent/'foundation-early-loader-20261025/previous-installation-check.json')['files']
assert len(installed) == 18
for row in installed:
    row['actual'] = sha((game/row['path']).read_bytes())
    assert row['actual'] == row['expected']
originals = read(batch.parent/'foundation-early-loader-20261025/original-game-check.json')['files']
originals.append(dict(path='UnityPlayer.dll',expected='43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1'))
for row in originals:
    row['actual'] = sha((game/row['path']).read_bytes())
    assert row['actual'] == row['expected']
save('preservation-check.json', dict(checkedAtUtc=datetime.now(timezone.utc).isoformat(), priorFrozenMilestones=milestones,
    installedBatch='foundation-local-protection-host-20261022', installedFiles=installed, installedSourceArchiveEntries=326,
    installedSourceArchiveSha256=old_proof['archiveSha256'], originalGameFiles=originals,
    allMatched=True, gameExecuted=False, installed=False))
print(json.dumps(dict(currentPreviousSources=588, oldInstalledFiles=18, oldInstalledArchiveEntries=326, originalGameFiles=4, allMatched=True)))
