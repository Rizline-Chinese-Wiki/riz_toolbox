"""Preserve the new initial-pending test failure, before adding diagnostic detail."""
import hashlib
import json
from pathlib import Path

WORK = Path(__file__).resolve().parent
RUN = Path('D:/riz-build-cache-next/gameplay-descriptor-readiness-20261114/run-r2')
target = WORK / 'run-r2-root-exit.json'
if target.exists(): raise RuntimeError('Preserve receipts')
state = json.loads((RUN / 'result.json').read_text(encoding='utf-8'))
if state['controllerPid'] != 42980 or state['passed'] or state['failure'] != 'Candidate step failed: suite-GameplaySourceGeneration': raise RuntimeError('Unexpected second fix result')
receipt = dict(schema='descriptor-readiness-candidate-root-exit/v1', controllerPid=42980, actualToolSession=90134,
    actualToolChunk='178d35', actualToolExitCode=1, resultSha256=hashlib.sha256((RUN / 'result.json').read_bytes()).hexdigest(),
    functionalPass=False, reason='Existing descriptor-readiness case advanced past the previous failure; a new initially-absent-source assertion failed and needs actual error/counter diagnosis. No final pass claimed.')
target.write_text(json.dumps(receipt, indent=2) + '\n', encoding='utf-8')
print('Recorded initial-pending case failure; no final PASS')
