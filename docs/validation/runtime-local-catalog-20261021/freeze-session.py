from pathlib import Path
from datetime import datetime, timezone
import json, hashlib, sys

root = Path(__file__).resolve().parent
game = Path(r'D:\SteamLibrary\steamapps\common\Rizline')
data = game / 'BepInEx/RizToolbox'
session, label = sys.argv[1:3]
assert '/' not in session and '\\' not in session and label.replace('-', '').isalnum()
source = data / 'diagnostics' / session
out = root / (label + '-' + datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ'))
out.mkdir()
files, events = [], []
for path in sorted(source.glob('*')) + [data/'status.json', data/'installation.json']:
    if not path.is_file() or path.name == 'session.lock': continue
    body = path.read_bytes(); (out/path.name).write_bytes(body)
    files.append(dict(name=path.name, source=str(path), bytes=len(body), sha256=hashlib.sha256(body).hexdigest()))
    if path.suffix == '.jsonl': events.extend(json.loads(line) for line in body.decode('utf-8-sig').splitlines())
seqs = [x['sequence'] for x in events]
assert events and all(x['sessionId'] == session for x in events)
assert seqs == list(range(seqs[0],seqs[-1]+1))
interesting = [x for x in events if any(s in x.get('message','') for s in ('TmpFieldProbe ', 'LocalCatalogProbe ', 'LocalCatalogFixture ', 'Visual cleanup quarantined'))]
result = dict(session=session, records=len(events), firstSequence=seqs[0], lastSequence=seqs[-1], contiguous=True,
              errors=[x for x in events if x['level'] in ('Error','Fatal')], observations=interesting, files=files)
(out/'summary.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps(dict(path=str(out), records=len(events), errors=len(result['errors']), observations=len(interesting))))
