"""Bind an observed original root terminal to its exact isolated result."""
import argparse
import hashlib
import json
from pathlib import Path

WORK = Path(__file__).resolve().parent
parser = argparse.ArgumentParser(); parser.add_argument('--run', required=True); parser.add_argument('--session', type=int, required=True)
parser.add_argument('--chunk', required=True); parser.add_argument('--exit', type=int, choices=(0, 1), required=True); args = parser.parse_args()
if args.run not in ('focused-r3', 'focused-r4', 'focused-r5', 'run-r3'): raise RuntimeError('Unexpected run receipt')
run = Path('D:/riz-build-cache-next/gameplay-descriptor-readiness-20261114') / args.run
target = WORK / (args.run + '-root-exit.json')
if target.exists(): raise RuntimeError('Preserve root receipts')
state = json.loads((run / 'result.json').read_text(encoding='utf-8'))
if not state['finishedUtc'] or state['passed'] != (args.exit == 0): raise RuntimeError('Actual result/exit differs')
if args.exit == 0 and not (state['fullFrozenInputsUnchanged'] and all(row['naturalExit'] and row['exitCode'] == 0 for row in state['steps'])):
    raise RuntimeError('Successful scope incomplete')
receipt = dict(schema='descriptor-readiness-candidate-root-exit/v1', controllerPid=state['controllerPid'],
    actualToolSession=args.session, actualToolChunk=args.chunk, actualToolExitCode=args.exit,
    resultSha256=hashlib.sha256((run / 'result.json').read_bytes()).hexdigest(),
    sourceGenerationOnly=state['sourceGenerationOnly'], functionalPass=state['passed'], mainApplied=False, sdkGoalComplete=False)
target.write_text(json.dumps(receipt, indent=2) + '\n', encoding='utf-8')
print(json.dumps(dict(recorded=True, actualToolExitCode=args.exit, receiptSha256=hashlib.sha256(target.read_bytes()).hexdigest())))
