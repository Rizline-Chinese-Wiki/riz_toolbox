"""Stage or restore only this opt-in validation's exact files; never controls the game."""
from pathlib import Path
from datetime import datetime, timezone
import hashlib, json, sys

root = Path(__file__).resolve().parent
data = Path(r'D:\SteamLibrary\steamapps\common\Rizline\BepInEx\RizToolbox')
record = root / 'probe-files.json'
def sha(body): return hashlib.sha256(body).hexdigest()
def write(path, value): path.write_text(json.dumps(value, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
def now(): return datetime.now(timezone.utc).isoformat()
if sys.argv[1:] == ['stage']:
    assert not record.exists(), 'Do not overwrite an earlier restoration record.'
    rows = []
    paths = [(data/'config/plugin-example.panel/config-local-catalog-proof.json', b'{"Enabled":true,"PublishValidationFixture":true}\n'),
             (data/'probes/local-resource-reference.enabled', b'opt-in read-only metadata validation\n')]
    paths += [(data/('plugin-storage/storage/plugin-example.panel/'+name), None) for name in
              ('configuration/local-catalog-selection.json', 'data/catalog-validation/tone.wav',
               'data/catalog-validation/cover.png', 'data/catalog-validation/chart.json')]
    for index, (path, body) in enumerate(paths):
        backup = root/('original-'+str(index)+'-'+path.name)
        if path.exists(): backup.write_bytes(path.read_bytes())
        rows.append(dict(path=str(path), existed=path.exists(), backup=str(backup),
                         writtenSha256=None if body is None else sha(body)))
    write(record, dict(createdUtc=now(), files=rows))
    for path, body in paths:
        if body is not None:
            path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(body)
    print('Staged two opt-in files; four storage paths recorded before launch.')
elif sys.argv[1:] == ['restore']:
    saved = json.loads(record.read_text(encoding='utf-8')); result = []
    for row in saved['files']:
        path=Path(row['path']); actual=path.read_bytes() if path.exists() else None
        if row['writtenSha256'] is not None:
            assert actual is not None and sha(actual)==row['writtenSha256'], 'Opt-in file changed: '+str(path)
        if actual is not None: (root/('observed-'+path.name)).write_bytes(actual)
        if row['existed']: path.write_bytes(Path(row['backup']).read_bytes())
        elif actual is not None: path.unlink()
        result.append(dict(path=str(path), observedSha256=None if actual is None else sha(actual), restored='Original' if row['existed'] else 'Absent'))
    write(root/'probe-files-restoration.json', dict(restoredUtc=now(), files=result))
    print('Restored '+str(len(result))+' exact files.')
else: raise SystemExit('Use stage or restore after verifying Rizline is stopped.')
