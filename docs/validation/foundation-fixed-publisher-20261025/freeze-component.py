"""Freeze the exact standalone component inputs; previous full suite is separate."""
from pathlib import Path
from datetime import datetime, timezone
import hashlib, json
batch=Path(__file__).resolve().parent
repo=batch.parents[2]
paths=sorted((repo/'native/RizToolbox.FixedPublisher').glob('*'))
paths=[p for p in paths if p.suffix in {'.cpp','.h','.asm'}]
paths.extend(repo/p for p in ['scripts/Build-FixedPublisherFixture.ps1','scripts/Test-FixedPublisherFixture.ps1',
    'docs/validation/foundation-fixed-publisher-20261025/verify-fixed-publisher.py','native/RizToolbox.JudgeContext/EngineNodeContext.h'])
assert len(paths)==12 and len(set(paths))==12
manifest=dict(frozenAtUtc=datetime.now(timezone.utc).isoformat(),scope='Standalone fixed publisher component only; does not claim rerunning the 25 full managed suites.',
    files=sorted([dict(path=p.relative_to(repo).as_posix(),sha256=hashlib.sha256(p.read_bytes()).hexdigest()) for p in paths],key=lambda x:x['path']))
(batch/'component-inputs-before.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
print(json.dumps(dict(componentSources=len(paths))))
