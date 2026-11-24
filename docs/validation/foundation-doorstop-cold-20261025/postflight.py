"""Check completed source/archive/document identities without running targets."""
from pathlib import Path
from urllib.parse import unquote
import hashlib
import json
import re
import zipfile

batch=Path(__file__).resolve().parent
repo=batch.parents[2]
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def read(path):return json.loads(path.read_text(encoding='utf-8-sig'))
manifest=read(batch/'source-final-manifest.json')
for row in manifest['files']:assert sha(repo/row['path'])==row['sha256'],row['path']
for name,rows in [('source-final-inputs.zip',manifest['files']),('binaries.zip',read(batch/'binary-manifest.json')['files'])]:
    with zipfile.ZipFile(batch/name) as archive:
        assert len(archive.namelist())==len(rows)
        for row in rows:assert hashlib.sha256(archive.read(row['path'])).hexdigest()==row['sha256']
documents=[repo/'README.md',repo/'.docs/CURRENT-CHECKPOINT.md',repo/'.docs/PROGRESS.md',
    repo/'docs/IMPLEMENTATION-STATUS.md',repo/'.docs/foundation-expansion/CHECKLIST.md',
    repo/'docs/2026-10-25_底层-Doorstop冷启动接线-report.md',
    batch/'README.md',batch/'scope.md',batch/'production-next.md',batch/'fixture-cross-review.md',
    batch/'research/engine-game-profile.md']
links=0
for path in documents:
    for target in re.findall(r'(?<!!)\[[^\]]+\]\(([^)]+)\)',path.read_text(encoding='utf-8-sig')):
        if target.startswith(('http:','https:','codex:','app:','mailto:','#')):continue
        target=unquote(target.split('#',1)[0].split('?',1)[0].strip('<>'))
        if not target:continue
        assert (path.parent/target).exists(),(path,target)
        links+=1
summary=read(batch/'summary.json')
assert summary['cold']['processes']==5 and summary['standalone']['processes']==21
result=dict(sourceInputsUnchanged=len(manifest['files']),sourceZipEntries=626,binaryZipEntries=8,
    documents=len(documents),localLinks=links,missingLinks=0,gameExecuted=False,installed=False)
output=batch/'postflight.json';assert not output.exists()
output.write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8',newline='\n')
print(json.dumps(result))
