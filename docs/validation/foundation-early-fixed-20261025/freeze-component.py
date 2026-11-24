"""Freeze the exact early fixed integration inputs before the root-owned build."""
from pathlib import Path
from datetime import datetime, timezone
import hashlib
import json

batch = Path(__file__).resolve().parent
repo = batch.parents[2]
paths = [repo / 'native/RizToolbox.EarlyFixedFixture' / name for name in (
    'EarlyFixedShared.h', 'Recorder.cpp', 'Proxy.cpp', 'ImporterApi.h',
    'Importer.cpp', 'Host.cpp', 'EarlyFixedDebugger.cpp')]
paths.extend(repo / name for name in (
    'native/RizToolbox.FixedPublisher/FixedTargets.asm',
    'native/RizToolbox.FixedPublisher/FixedPublisher.h',
    'native/RizToolbox.JudgeContext/EngineNodeContext.h',
    'scripts/Build-EarlyFixedFixture.ps1', 'scripts/Test-EarlyFixedFixture.ps1',
    'docs/validation/foundation-early-fixed-20261025/verify-early-fixed.py',
    'docs/validation/foundation-fixed-publisher-20261025/verify-fixed-publisher.py'))
assert len(paths) == len(set(paths)) == 14
manifest = dict(
    frozenAtUtc=datetime.now(timezone.utc).isoformat(),
    scope='Early fixed owned-loader integration only; not a rerun of the 25 managed suites or a game install.',
    files=sorted([dict(path=p.relative_to(repo).as_posix(),
                       sha256=hashlib.sha256(p.read_bytes()).hexdigest()) for p in paths],
                 key=lambda row: row['path']))
destination = batch / 'component-inputs-before.json'
assert not destination.exists(), 'Preserve the preceding freeze before creating another one.'
destination.write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
print(json.dumps(dict(componentSources=len(paths))))
