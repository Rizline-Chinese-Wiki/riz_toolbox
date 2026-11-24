"""Preserve the completed but failing r3 initializer-stage test."""
from pathlib import Path
import hashlib
import json
import shutil
import zipfile

batch=Path(__file__).resolve().parent
source=Path('D:/RizToolbox-build-cache/winhttp-forward-r3-20261025')
target=batch/'history/attempt-r3'
target.mkdir(exist_ok=False)
for path in (batch/'attempts/r3').iterdir():
    assert path.is_file()
    shutil.copyfile(path,target/path.name)
rows=[]
with zipfile.ZipFile(target/'build-cache.zip','x',compression=zipfile.ZIP_DEFLATED) as archive:
    for path in sorted(source.rglob('*')):
        if not path.is_file():continue
        assert not path.is_symlink()
        data=path.read_bytes();name=path.relative_to(source).as_posix()
        archive.writestr(name,data)
        rows.append(dict(path=name,sha256=hashlib.sha256(data).hexdigest()))
with zipfile.ZipFile(target/'build-cache.zip') as archive:
    assert len(archive.namelist())==len(rows)
    for row in rows:assert hashlib.sha256(archive.read(row['path'])).hexdigest()==row['sha256']
host=json.loads((source/'cases/production-normal/case.json').read_text())
process=json.loads((source/'cases/production-normal/process.json').read_text())
assert process['pid']==59236 and process['exitCode']==1 and not process['timedOut']
assert host['finished'] and not host['passed'] and host['sehCode']==0
(target/'terminal.json').write_text(json.dumps(dict(sessionId=49482,exitCode=1,
    actualTerminalObserved=True,buildCompleted=True,failedChild=process,
    hostChecks=host['checks'],failures=host['failures'],cacheFiles=len(rows),
    failure='TLS no longer crashes; compiler discarded unreferenced internal const C/C++ initializer pointers.'),indent=2)+'\n')
(target/'cache-manifest.json').write_text(json.dumps(dict(files=rows),indent=2)+'\n')
print(json.dumps(dict(preservedFailedRun='r3',hostChecks=host['checks'],failures=host['failures'])))
