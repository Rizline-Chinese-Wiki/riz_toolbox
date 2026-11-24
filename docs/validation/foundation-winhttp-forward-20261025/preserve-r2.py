"""Preserve the failed owned-child run and its separate diagnostic observer."""
from pathlib import Path
import hashlib
import json
import shutil
import zipfile

batch = Path(__file__).resolve().parent
history = batch / 'history'
history.mkdir(exist_ok=True)

def archive_tree(source, target):
    assert source.is_dir() and not target.exists()
    rows = []
    with zipfile.ZipFile(target, 'x', compression=zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(source.rglob('*')):
            if not path.is_file():
                continue
            assert not path.is_symlink()
            data = path.read_bytes()
            name = path.relative_to(source).as_posix()
            archive.writestr(name, data)
            rows.append(dict(path=name, sha256=hashlib.sha256(data).hexdigest()))
    with zipfile.ZipFile(target) as archive:
        assert len(archive.namelist()) == len(rows)
        for row in rows:
            assert hashlib.sha256(archive.read(row['path'])).hexdigest() == row['sha256']
    return rows

r2 = history / 'attempt-r2'
r2.mkdir(exist_ok=False)
for path in (batch / 'attempts/r2').iterdir():
    assert path.is_file()
    shutil.copyfile(path, r2 / path.name)
rows = archive_tree(Path('D:/RizToolbox-build-cache/winhttp-forward-r2-20261025'), r2 / 'build-cache.zip')
(r2 / 'terminal.json').write_text(json.dumps(dict(sessionId=17519, exitCode=1,
    actualTerminalObserved=True, buildCompleted=True, timedOut=False,
    failedChildPid=49576, failedChildExitCode=-1073740791, failedChildExitHex='0xc0000409',
    case='production-normal', verifiedCases=0, cacheFiles=len(rows),
    failure='Importer TLS read global Api before its CRT dynamic initializer; CFG terminated the owned child.'), indent=2)+'\n')
(r2 / 'cache-manifest.json').write_text(json.dumps(dict(files=rows), indent=2)+'\n')

observer = history / 'observer-r2'
observer.mkdir(exist_ok=False)
shutil.copyfile(batch / 'OwnedExceptionObserver.cpp', observer / 'OwnedExceptionObserver.cpp')
rows = archive_tree(Path('D:/RizToolbox-build-cache/winhttp-observer-20261025'), observer / 'diagnostic-cache.zip')
(observer / 'terminal.json').write_text(json.dumps(dict(sessionId=64495,
    actualTerminalObserved=True, driverExitCode=0, ownedTargetExitHex='0xc0000409',
    successfulFixtureCase=False, gameExecuted=False, attachedToExistingProcess=False,
    cacheFiles=len(rows)), indent=2)+'\n')
(observer / 'cache-manifest.json').write_text(json.dumps(dict(files=rows), indent=2)+'\n')
print(json.dumps(dict(preservedFailedRun='r2', preservedOwnedObserver=True)))
