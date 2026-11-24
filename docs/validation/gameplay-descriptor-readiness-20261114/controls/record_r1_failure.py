"""Preserve the first incomplete fix and its actual old-lease regression."""
import hashlib
import json
from pathlib import Path

WORK = Path(__file__).resolve().parent
RUN = Path('D:/riz-build-cache-next/gameplay-descriptor-readiness-20261114/run-r1')
target = WORK / 'run-r1-root-exit.json'
if target.exists(): raise RuntimeError('Preserve root receipts')
state = json.loads((RUN / 'result.json').read_text(encoding='utf-8'))
if state['controllerPid'] != 28676 or state['passed'] or state['failure'] != 'Candidate step failed: suite-GameplaySourceGeneration': raise RuntimeError('Unexpected first fix result')
receipt = dict(schema='descriptor-readiness-candidate-root-exit/v1', controllerPid=28676, actualToolSession=73775,
    actualToolChunk='24076c', actualToolExitCode=1, resultSha256=hashlib.sha256((RUN / 'result.json').read_bytes()).hexdigest(),
    functionalPass=False, reason='Changing fresh descriptor-less preparation to NotReady also exposed that service Capture preserves a control lease across descriptor availability loss; old authoritative leases must separately expire without changing independent clock generation.')
target.write_text(json.dumps(receipt, indent=2) + '\n', encoding='utf-8')
print('Recorded incomplete first fix; no final candidate PASS')
