from pathlib import Path
from datetime import datetime, timezone
import hashlib, json, sys

root=Path(__file__).resolve().parent
marker=Path(r'D:\SteamLibrary\steamapps\common\Rizline\BepInEx\RizToolbox\probes\local-resource-reference.enabled')
record=root/'probe-file.json'; body=b'opt-in existing-owner resource metadata validation\n'
sha=lambda data:hashlib.sha256(data).hexdigest()
if sys.argv[1:]==['stage']:
    assert not record.exists(), 'Preserve the existing restoration record.'
    original=root/'original-marker.bin'
    if marker.exists(): original.write_bytes(marker.read_bytes())
    record.write_text(json.dumps(dict(createdUtc=datetime.now(timezone.utc).isoformat(),path=str(marker),existed=marker.exists(),backup=str(original),writtenSha256=sha(body)),indent=2)+'\n',encoding='utf-8')
    marker.parent.mkdir(parents=True,exist_ok=True); marker.write_bytes(body)
    print('Staged only the opt-in metadata marker.')
elif sys.argv[1:]==['restore']:
    saved=json.loads(record.read_text(encoding='utf-8')); actual=marker.read_bytes()
    assert sha(actual)==saved['writtenSha256'], 'Marker changed; preserve it for review.'
    if saved['existed']: marker.write_bytes(Path(saved['backup']).read_bytes())
    else: marker.unlink()
    (root/'probe-file-restoration.json').write_text(json.dumps(dict(restoredUtc=datetime.now(timezone.utc).isoformat(),path=str(marker),restored='Original' if saved['existed'] else 'Absent'),indent=2)+'\n',encoding='utf-8')
    print('Restored the exact metadata marker.')
else: raise SystemExit('Use stage or restore after confirming Rizline is stopped.')
