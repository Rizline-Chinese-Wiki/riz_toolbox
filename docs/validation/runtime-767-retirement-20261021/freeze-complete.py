from pathlib import Path
from datetime import datetime, timezone
import hashlib, json

root = Path(__file__).resolve().parent
game = Path(r'D:\SteamLibrary\steamapps\common\Rizline')
data = game / 'BepInEx/RizToolbox'
session = '20261021T050719415Z-bea8877e6d904db19169658e8e93ac51'
source = data / 'diagnostics' / session
out = root / ('complete-' + datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ'))
out.mkdir()
manifest, records = [], []
for path in sorted(source.glob('*')) + sorted(data.glob('toolbox.log*')):
    if not path.is_file() or path.name == 'session.lock': continue
    body = path.read_bytes()
    (out / path.name).write_bytes(body)
    manifest.append(dict(source=str(path), name=path.name, bytes=len(body), sha256=hashlib.sha256(body).hexdigest()))
    if path.suffix == '.jsonl':
        records.extend(json.loads(line) for line in body.decode('utf-8-sig').splitlines())
assert records and all(x['sessionId'] == session for x in records)
seqs = [x['sequence'] for x in records]
assert seqs == list(range(seqs[0], seqs[-1] + 1))
receipts = [x for x in records if '2b5b36d61b844818bb57bdd06f55960f' in json.dumps(x)]
assert any('Visual cleanup completed' in json.dumps(x) and 'attempts=2' in json.dumps(x) for x in receipts)
result = dict(session=session, events=len(records), firstSequence=seqs[0], lastSequence=seqs[-1],
              contiguous=True, materialReceipts=receipts, files=manifest,
              capturedAtUtc=datetime.now(timezone.utc).isoformat())
(out/'summary.json').write_text(json.dumps(result, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
print(json.dumps(dict(path=str(out), events=len(records), firstSequence=seqs[0], lastSequence=seqs[-1], materialReceipts=len(receipts))))
