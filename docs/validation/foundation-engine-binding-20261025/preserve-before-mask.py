"""Preserve the genuinely completed first build without treating it as the final candidate."""
import hashlib, json, re, shutil, zipfile
from datetime import datetime, timezone
from pathlib import Path
b = Path(__file__).resolve().parent; repo = b.parents[2]; out = b/'before-mask-fix'
assert not out.exists()
wrapper = json.loads((b/'build.log.run.json').read_text(encoding='utf-8-sig'))
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
assert wrapper['exitCode'] == 0 and wrapper['logSha256'] == sha(b/'build.log')
body = (b/'build.log').read_text(encoding='utf-8-sig')
suites = [tuple(map(int, m.groups())) for line in body.splitlines() if (m:=re.fullmatch(r'.*?(\d+)/(\d+).*passed\.',line.strip()))]
assert len(suites)==25 and sum(total for passed,total in suites)==1617 and all(a==c for a,c in suites)
inputs = json.loads((b/'build-inputs-before.json').read_text(encoding='utf-8-sig'))['files']
package = json.loads((repo/'artifacts/package-manifest.json').read_text(encoding='utf-8-sig'))
assert len(inputs)==565 and len(package['files'])==22
assert all(sha(repo/row['path']) == row['sha256'] for row in inputs)
assert all(sha(repo/'artifacts/package'/row['path']) == row['sha256'] for row in package['files'])
out.mkdir()
for name in ['build.log','build.log.run.json','build-inputs-before.json']:
    shutil.copy2(b/name,out/name)
(out/'package-manifest.json').write_text(json.dumps(package,indent=2)+'\n',encoding='utf-8')
for name,root,rows in [('source-inputs.zip',repo,inputs),('staged-package.zip',repo/'artifacts/package',package['files'])]:
    with zipfile.ZipFile(out/name,'w',compression=zipfile.ZIP_DEFLATED) as z:
        for row in rows: z.write(root/row['path'],row['path'])
    with zipfile.ZipFile(out/name) as z:
        assert z.testzip() is None and set(z.namelist())=={row['path'] for row in rows}
        assert all(hashlib.sha256(z.read(row['path'])).hexdigest()==row['sha256'] for row in rows)
record=dict(recordedAtUtc=datetime.now(timezone.utc).isoformat(),sessionId=48324,terminalExitCodeObserved=0,
    exitCode=0,projects=25,passed=1617,total=1617,sourceInputs=565,packageFiles=22,logSha256=sha(out/'build.log'),
    sourceZipSha256=sha(out/'source-inputs.zip'),packageZipSha256=sha(out/'staged-package.zip'),installed=False,gameExecuted=False,
    finalCandidate=False,reason='Cross-review found an owned unknown node hidden under a foreign Start was filtered rather than retained as Unknown responsibility. Existing Foreign test asserted that weaker behavior. Preserve this successful build as before-fix evidence only; fix and rebuild the final candidate separately.')
(out/'build-run.json').write_text(json.dumps(record,indent=2)+'\n',encoding='utf-8')
print(json.dumps(record))
