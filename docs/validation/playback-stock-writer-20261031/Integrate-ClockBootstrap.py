"""Apply the exact verified S4 increment; retain before/after ownership facts."""
import hashlib
import json
import zipfile
from pathlib import Path

REPOSITORY=Path(__file__).resolve().parents[3]
CASE=Path('D:/riz-build-cache-next/playback-stock-clock-bootstrap-r1-20261031')
OUT=Path('D:/riz-build-cache-next/playback-clock-main-integration-r1-20261031')

def read(path):return json.loads(Path(path).read_text(encoding='utf-8-sig'))
def fact(path):
    path=Path(path).resolve()
    return dict(path=str(path),sha256=hashlib.sha256(path.read_bytes()).hexdigest(),bytes=path.stat().st_size)
def write(path,value):Path(path).write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')

def run():
    assert not OUT.exists()
    proof=read(CASE/'completion-r1.json')
    assert proof['passed'] and proof['managedChecks']==179 and proof['nativeChecks']==388
    assert fact(proof['sourceArchive']['path'])['sha256']==proof['sourceArchive']['sha256']
    assert fact(proof['beforeArchive']['path'])['sha256']==proof['beforeArchive']['sha256']
    changes=read(CASE/'handoff-r1/changed-files.json');assert len(changes)==19
    with zipfile.ZipFile(proof['beforeArchive']['path']) as archive:
        before={entry.filename:archive.read(entry) for entry in archive.infolist() if not entry.is_dir()}
    with zipfile.ZipFile(proof['sourceArchive']['path']) as archive:
        after={entry.filename:archive.read(entry) for entry in archive.infolist() if not entry.is_dir()}
    assert len(before)==745 and len(after)==756
    for name,data in before.items():assert (REPOSITORY/name).read_bytes()==data,name
    for row in changes:
        path=(REPOSITORY/row['relativePath']).resolve();assert path.is_relative_to(REPOSITORY)
        if row['before'] is None:assert not path.exists(),str(path)
        else:assert fact(path)['sha256']==row['before']['sha256']
        assert hashlib.sha256(after[row['relativePath']]).hexdigest()==row['after']['sha256']
    OUT.mkdir()
    write(OUT/'before.json',dict(kind='pre-clock-bootstrap-current-inputs',passed=True,sourceArchive=proof['beforeArchive'],
        currentFiles=[fact(REPOSITORY/name) for name in sorted(before)],scope='Before the 19 S4 implementation writes.'))
    actual=[]
    for row in changes:
        path=REPOSITORY/row['relativePath'];path.parent.mkdir(parents=True,exist_ok=True)
        old=None if row['before'] is None else fact(path)
        path.write_bytes(after[row['relativePath']]);actual.append(dict(relativePath=row['relativePath'],before=old,after=fact(path)))
    for name,data in after.items():assert (REPOSITORY/name).read_bytes()==data,name
    result=dict(kind='actual-clock-bootstrap-current-worktree-integration',passed=True,implementationMerged=True,
        currentFullMatrixVerified=False,sourceFilesCompared=756,changedFiles=actual,stageProof=fact(CASE/'completion-r1.json'),
        before=fact(OUT/'before.json'),sourceArchive=proof['sourceArchive'],gameExecuted=False,installed=False,productionAdmission=False)
    write(OUT/'test-result.json',result)
    print(json.dumps(dict(passed=True,implementationMerged=True,files=756,changedFiles=19,currentFullMatrixStillRequired=True)))

if __name__=='__main__':run()
