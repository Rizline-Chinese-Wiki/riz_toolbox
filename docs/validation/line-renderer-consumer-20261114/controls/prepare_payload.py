"""Prepare an exact main payload only after the current frozen candidate passes."""
import hashlib
import importlib.util
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).resolve().parent
SHADOW = Path('D:/riz-build-cache-next/line-renderer-consumer-20261114/tests-r6')
ACCEPTED = ROOT / 'docs/validation/line-setpositions-runtime-20261114'


def sha(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def require(ok, reason):
    if not ok:
        raise RuntimeError(reason)


def main():
    target = WORK / 'payload-manifest.json'
    require(not target.exists(), 'Preserve prepared payloads')
    require(sha(ACCEPTED / 'validation-summary.json') == '41cf05aa4db2cf4f6040570a383a82efdf82de6c0a498793e84ec6b246a9f20f', 'Accepted base changed')
    state = read(SHADOW / 'result.json')
    require(state['passed'] and state['frozenInputsUnchanged'] and state['candidateOverlayUnchanged'], 'Current frozen candidate has not passed')
    require(state['plannedCases'] == [11, 12] and not state['fullRendererSuiteRerun'], 'Focused scope differs')
    require(len(state['steps']) == 4 and all(row['naturalExit'] and row['exitCode'] == 0 for row in state['steps']), 'Candidate lacks natural successful focused steps')
    predecessor = read(SHADOW.parent / 'tests-r5/result.json')
    require({k: v for k, v in predecessor['frozenInputs'].items() if k.startswith('src/')}
            == {k: v for k, v in state['frozenInputs'].items() if k.startswith('src/')}, 'Test-only repair changed production inputs')
    require(state['steps'][0]['warnings'] == 0 and state['steps'][0]['errors'] == 0, 'Six Release build differs')
    witness = read(WORK / 'shadow-root-exit.json')
    require(witness['controllerPid'] == state['controllerPid'] and witness['actualToolExitCode'] == 0
            and witness['resultSha256'] == sha(SHADOW / 'result.json'), 'Exact candidate root exit is unconfirmed')
    original = state['originalInputs']
    scanner_path = ROOT / 'work/line-ring-source-validation-20261113/run_validation.py'
    require(sha(scanner_path) == '61c2983ae117322b25ff96ad75b659b870fdadf9aada9274727248cd5ca98fd4', 'Scanner changed')
    spec = importlib.util.spec_from_file_location('renderer_payload_scanner', scanner_path)
    scanner = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(scanner)
    require(len(original) == 913 and scanner.inputs() == original, 'Accepted main changed during candidate work')
    for name, digest in state['frozenInputs'].items():
        require(sha(SHADOW / 'source' / name) == digest, 'Frozen input changed: ' + name)
    expected = dict(original)
    files = []
    for name, digest in state['candidateOverlay'].items():
        require(sha(WORK / name) == digest == state['frozenInputs'][name], 'Current candidate differs: ' + name)
        expected[name] = digest
        if original.get(name) == digest:
            continue
        files.append(dict(target=name, candidate=(WORK / name).relative_to(ROOT).as_posix(),
                          baseSha256=original.get(name), sha256=digest, compilerInput=True))
    require(len(expected) == 922, 'Unexpected integrated input count')
    script = 'scripts/Build.ps1'
    body = (ROOT / script).read_text(encoding='utf-8-sig')
    anchor = "                for ($caseIndex = 1; $caseIndex -le 300; $caseIndex++) {\n                    & dotnet $judgementsAssembly '--legacy-case' $caseIndex"
    require(body.count(anchor) == 1, 'Managed matrix anchor changed')
    insert = """                # Original retained renderer consumer; each native fixture runs in a fresh process.
                # Ordered storage recovery does not close Unity effects or admit full Seek.
                for ($caseIndex = 1; $caseIndex -le 12; $caseIndex++) {
                    & dotnet $judgementsAssembly '--line-renderer-consumer-case' $caseIndex
                    if ($LASTEXITCODE -ne 0) { throw \"Judgements renderer consumer case $caseIndex failed with exit code $LASTEXITCODE.\" }
                }
"""
    candidate_script = WORK / script
    require(not candidate_script.exists(), 'Preserve candidate matrix controls')
    candidate_script.parent.mkdir(parents=True, exist_ok=True)
    candidate_script.write_text(body.replace(anchor, insert + anchor), encoding='utf-8', newline='\n')
    files.append(dict(target=script, candidate=candidate_script.relative_to(ROOT).as_posix(),
                      baseSha256=sha(ROOT / script), sha256=sha(candidate_script), compilerInput=False))
    value = dict(schema='renderer-consumer-main-payload/v1', baseInputs=original, expectedInputs=expected,
                 files=files, candidateResultSha256=sha(SHADOW / 'result.json'),
                 rootExitSha256=sha(WORK / 'shadow-root-exit.json'),
                 baseSummarySha256=sha(ACCEPTED / 'validation-summary.json'),
                 readmeSha256=sha(ROOT / 'README.md'), mainApplied=False, freshMainPassed=False,
                 candidateFocusedCases=[11, 12], candidateFullSuiteRerun=False,
                 rendererCases=12, freshMainFullRendererRequired=True, completeSeekPermitted=False, sdkGoalComplete=False)
    require(value['readmeSha256'] == 'cfbac4691d36edc5daba513aa62d5856ae61e29bf553d0dc2979832750847364', 'Preserve user README bytes')
    target.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(dict(payloadSha256=sha(target), files=len(files), expectedInputs=len(expected))))


if __name__ == '__main__':
    main()
