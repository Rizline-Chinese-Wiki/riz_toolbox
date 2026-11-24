"""Validate final document links and source fixity without executing images."""
from pathlib import Path
from urllib.parse import unquote
import hashlib
import json
import re

batch=Path(__file__).resolve().parent
repo=batch.parents[2]
root_written=[repo/'README.md',repo/'.docs/CURRENT-CHECKPOINT.md',repo/'docs/IMPLEMENTATION-STATUS.md',
    repo/'.docs/foundation-expansion/CHECKLIST.md',repo/'.docs/PROGRESS.md',
    repo/'docs/2026-10-25_底层-系统WinHTTP转发-report.md',batch/'README.md',batch/'production-next.md']
# Path.write_text's platform default translated LF into CRLF; retain Git's
# existing LF document format without changing any frozen implementation input.
for path in root_written:
    data=path.read_bytes()
    if b'\r\n' in data:path.write_bytes(data.replace(b'\r\n',b'\n'))
manifest=json.loads((batch/'source-final-manifest.json').read_text())
for row in manifest['files']:
    assert hashlib.sha256((repo/row['path']).read_bytes()).hexdigest()==row['sha256'],row['path']
documents=root_written+[batch/'scope.md',batch/'native-notes.md',batch/'fixture-notes.md',
    batch/'verifier-notes.md',batch/'scripts-review.md',batch/'importer-initializer-review.md',batch/'review-final.md']
links=0
for path in documents:
    for target in re.findall(r'(?<!!)\[[^\]]+\]\(([^)]+)\)',path.read_text(encoding='utf-8-sig')):
        if target.startswith(('http:','https:','app:','codex:','#','mailto:')):continue
        target=unquote(target.split('#',1)[0].split('?',1)[0].strip('<>'))
        if not target:continue
        linked=path.parent/target
        assert linked.exists(),(path,target)
        links+=1
result=dict(sourceInputsUnchanged=len(manifest['files']),documents=len(documents),
    localLinks=links,missingLocalLinks=0,gameExecuted=False,installed=False)
destination=batch/'postflight.json'
assert not destination.exists()
destination.write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8',newline='\n')
print(json.dumps(result))
