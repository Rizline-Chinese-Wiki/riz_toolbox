"""Adapt the original14 independent SDK checks to the current1171 frozen graph."""
import ast
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).resolve().parent
OLD = ROOT / 'work/sdk-current-hold-snapshot-20261114'
targets = [WORK / 'run_candidate.py', WORK / 'supervise.py']
if any(path.exists() for path in targets): raise RuntimeError('Preserve current SDK controls')


def replace(text, old, new, count=1):
    if text.count(old) != count: raise RuntimeError('SDK control anchor differs: ' + old)
    return text.replace(old, new)


body = (OLD / 'run_candidate.py').read_text(encoding='utf-8')
body = body.replace('two independent plugins and formal host on939', 'two independent plugins and formal host on1171')
body = replace(body, "BASE = ROOT / 'work/hold-discovery-guard-next-20261114/candidate-evidence-r2'",
    "BASE = Path('D:/riz-build-cache-next/foundation-current-integration-20261114/run-r2')\nMANIFEST = ROOT / 'work/foundation-current-integration-next-20261114/candidate-manifest.json'")
body = body.replace('D:/riz-build-cache-next/sdk-current-hold-20261114', 'D:/riz-build-cache-next/sdk-current-integration-20261114')
start = body.index("    require(sha(BASE / 'summary.json')")
end = body.index("    for name in ('scripts/Pack-Sdk.ps1'", start)
body = body[:start] + '''    require(sha(MANIFEST) == '7b8cea0b1a3a5d7cf06c5821374a50e2433402d9e80c2f6383c1a48bc6dc18d4', 'Current source manifest differs')
    candidate = read(BASE / 'result.json'); declared = read(MANIFEST)
    require(candidate['controllerPid'] == 28536 and candidate['fullFrozenInputs'] == declared['expectedInputs']
        and len(candidate['fullFrozenInputs']) == 1171, 'Current source snapshot differs')
    require(DOTNET.is_file() and PWSH.is_file(), 'Known CLI tools are unavailable')
    expected = candidate['fullFrozenInputs']; extras = dict(candidate['auxiliaryInputs'])
''' + body[end:]
body = replace(body, '        else: shutil.copyfile(ROOT / name, target)',
    "        else: shutil.copyfile(BASE / 'source' / name if name in candidate['auxiliaryInputs'] else ROOT / name, target)")
body = body.replace('with939 snapshot', 'with1171 snapshot')
body = replace(body, 'compilerInputs=939, frozenInputs=expected, additionalInputs=extras,', 'compilerInputs=1171, frozenInputs=expected, additionalInputs=extras,')
body = replace(body, "candidateSummarySha256=sha(BASE / 'summary.json'),", "sourceManifestSha256=sha(MANIFEST), baseCurrentControllerPid=28536, baseCurrentValidationAccepted=False,")
body = replace(body, "    shutil.copyfile(offline, controls / offline.name); shutil.copyfile(__file__, controls / Path(__file__).name)",
    "    shutil.copyfile(offline, controls / offline.name); shutil.copyfile(__file__, controls / Path(__file__).name)\n    shutil.copyfile(MANIFEST, controls / 'source-manifest.json')\n    shutil.copyfile(WORK / 'source-review.md', controls / 'source-review.md')")
ast.parse(body)
supervisor = (ROOT / 'work/foundation-current-integration-next-20261114/supervise_candidate_r2.py').read_text(encoding='utf-8')
supervisor = replace(supervisor, "SUP = Path('D:/riz-build-cache-next/foundation-current-integration-20261114/run-r2-supervision')",
    "SUP = Path('D:/riz-build-cache-next/sdk-current-integration-20261114/run-r1-supervision')")
supervisor = supervisor.replace("RUN = SUP.parent / 'run-r2'", "RUN = SUP.parent / 'run-r1'")
supervisor = supervisor.replace('validate_candidate_r2.py', 'run_candidate.py')
supervisor = supervisor.replace("resultInputsUnchanged=result.get('fullFrozenInputsUnchanged')", "resultInputsUnchanged=result.get('frozenInputsUnchanged')")
supervisor = supervisor.replace('current-foundation-supervised-root/v1', 'current-sdk-supervised-root/v1').replace('current-foundation-supervisor-launch/v1', 'current-sdk-supervisor-launch/v1')
ast.parse(supervisor)
targets[0].write_text(body, encoding='utf-8', newline='\n'); targets[1].write_text(supervisor, encoding='utf-8', newline='\n')
print(json.dumps(dict(prepared=True, executed=False, controls={path.name:hashlib.sha256(path.read_bytes()).hexdigest() for path in targets})))
