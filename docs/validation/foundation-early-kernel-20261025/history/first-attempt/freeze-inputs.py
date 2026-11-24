"""Freeze this native integration batch against the last composite source set."""
from pathlib import Path
from datetime import datetime, timezone
import difflib
import hashlib
import json
import zipfile

batch = Path(__file__).resolve().parent
repo = batch.parents[2]
previous = batch.parent / 'foundation-kernel-install-20261025'
def sha(data): return hashlib.sha256(data).hexdigest()
base = json.loads((previous/'source-final-manifest.json').read_text(encoding='utf-8-sig'))['files']
paths = {row['path'] for row in base}
paths.update([
    'native/RizToolbox.EarlyFixedFixture/EarlyFixedProfile.h',
    'native/RizToolbox.FixedPublisher/FixedPublisherProfile.h',
    'docs/validation/foundation-early-fixed-20261025/verify-early-fixed.py',
    'docs/validation/foundation-fixed-publisher-20261025/verify-fixed-publisher.py',
    'docs/validation/foundation-early-kernel-20261025/verify-early-kernel.py',
])
expected_changes = {
    'native/RizToolbox.EarlyFixedFixture/'+name for name in
    ('EarlyFixedDebugger.cpp','EarlyFixedShared.h','Host.cpp','Importer.cpp','ImporterApi.h','Proxy.cpp','Recorder.cpp')
} | {'native/RizToolbox.FixedPublisher/FixedPublisher.cpp','native/RizToolbox.FixedPublisher/FixedPublisher.h',
     'scripts/Build-EarlyFixedFixture.ps1','scripts/Build-FixedPublisherFixture.ps1','scripts/Test-EarlyFixedFixture.ps1'}
changes = []
with zipfile.ZipFile(previous/'final-source-inputs.zip') as archive:
    for row in base:
        data = (repo/row['path']).read_bytes()
        if sha(data) != row['sha256']:
            before = archive.read(row['path'])
            assert sha(before) == row['sha256']
            changes.append(dict(path=row['path'],beforeSha256=row['sha256'],sha256=sha(data)))
            history = batch/'history'; history.mkdir(exist_ok=True)
            name = row['path'].replace('/','__')
            (history/(name+'.before')).write_bytes(before)
            difference = ''.join(difflib.unified_diff(before.decode('utf-8-sig').splitlines(keepends=True),
                data.decode('utf-8-sig').splitlines(keepends=True),fromfile=row['path']+' (composite599)',tofile=row['path']+' (new)'))
            (history/(name+'.diff')).write_text(difference,encoding='utf-8',newline='\n')
assert {row['path'] for row in changes} == expected_changes, changes
rows = [dict(path=path,sha256=sha((repo/path).read_bytes())) for path in sorted(paths)]
assert len(rows) == 604
destination = batch/'inputs-before.json'
assert not destination.exists(), 'Never overwrite an earlier freeze.'
destination.write_text(json.dumps(dict(frozenAtUtc=datetime.now(timezone.utc).isoformat(),files=rows,
    scope='Composite599 plus two profile headers and three invoked verifiers; native integration only, not another full managed build.'),indent=2)+'\n',encoding='utf-8')
(batch/'intentional-source-changes.json').write_text(json.dumps(dict(previousSources=599,changed=changes,
    addedDeclarations=sorted(paths-{row['path'] for row in base}),historicalArchiveUnmodified=True),indent=2)+'\n',encoding='utf-8')
with zipfile.ZipFile(batch/'source-inputs.zip','x',compression=zipfile.ZIP_DEFLATED) as archive:
    for row in rows:
        data = (repo/row['path']).read_bytes(); assert sha(data) == row['sha256']
        archive.writestr(row['path'],data)
print(json.dumps(dict(declaredSources=len(rows),changed=len(changes),archiveSha256=sha((batch/'source-inputs.zip').read_bytes()))))
