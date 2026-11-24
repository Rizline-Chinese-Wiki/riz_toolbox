"""Archive a complete solution build and two real fixture-loading output modes."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil

ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).resolve().parent
SOLUTION = Path('D:/riz-build-cache-next/hold-discovery-guard-20261114/solution-r4')
MODES = Path('D:/riz-build-cache-next/hosting-fixture-output-20261114')
DEST = ROOT / 'docs/validation/complete-solution-hold-20261114'
sha = lambda path: hashlib.sha256(Path(path).read_bytes()).hexdigest()
read = lambda path: json.loads(Path(path).read_text(encoding='utf-8-sig'))


def require(ok, reason):
    if not ok: raise RuntimeError(reason)


def local(base, name):
    raw = base / name
    for part in (raw, *raw.parents):
        if part == base: break
        require(not part.is_symlink() and not getattr(part.lstat(), 'st_file_attributes', 0) & 0x400, 'Reparse evidence')
    path = raw.resolve(); require(path.is_relative_to(base) and path.is_file(), 'Artifact escaped archive'); return path


def check(base):
    base = base.resolve(); summary = read(local(base, 'validation-summary.json'))
    require(summary['schema'] == 'complete-solution-hold-evidence/v1' and summary['passed']
        and not summary['fullManagedMatrix'] and not summary['mainApplied'] and not summary['sdkGoalComplete'], 'Scope differs')
    for name, row in summary['artifacts'].items():
        path = local(base, name); require(sha(path) == row['sha256'] and path.stat().st_size == row['bytes'], 'Artifact differs: ' + name)
    require({path.relative_to(base).as_posix() for path in base.rglob('*') if path.is_file()} == set(summary['artifacts']) | {'validation-summary.json'}, 'Inventory differs')
    state = read(local(base, 'solution/result.json')); witness = read(local(base, 'controls/root-exits.json'))
    require(state['passed'] and state['naturalExit'] and state['exitCode'] == 0 and state['errors'] == 0
        and state['solutionProjects'] == 45 and state['completeSolutionInputCount'] == 1171
        and state['frozenInputsUnchanged'] and state['additionalSolutionInputsUnchanged'], 'Complete solution differs')
    require(witness['solution']['actualToolExitCode'] == 0 and witness['solution']['controllerPid'] == state['controllerPid']
        and witness['solution']['resultSha256'] == sha(local(base, 'solution/result.json')), 'Actual solution root differs')
    complete = {**state['frozenInputs'], **state['additionalSolutionInputs']}
    require(len(complete) == 1171 and len(state['frozenInputs']) == 939 and len(state['additionalSolutionInputs']) == 232, 'Input closure differs')
    for name, digest in complete.items(): require(sha(local(base, 'source/' + name)) == digest, 'Complete input differs')
    require(sha(local(base, 'solution/solution.log')) == state['logSha256'], 'Solution log differs')
    for mode in ('artifacts', 'default'):
        run = read(local(base, mode + '/result.json'))
        require(run['passed'] and run['fullFrozenInputsUnchanged'] and run['fullFrozenInputs'] == complete
            and witness[mode]['actualToolExitCode'] == 0 and witness[mode]['controllerPid'] == run['controllerPid']
            and witness[mode]['resultSha256'] == sha(local(base, mode + '/result.json')), 'Actual Hosting mode differs')
        for row in run['steps']:
            require(row['naturalExit'] and row['exitCode'] == 0 and not row['failNames']
                and sha(local(base, mode + '/' + row['id'] + '.log')) == row['logSha256'], 'Hosting natural step differs')
        require(len(run['steps'][-1]['passNames']) == len(set(run['steps'][-1]['passNames'])) == 20, 'Hosting checks differ')
        for name, digest in run['fixtureOutputs'].items(): require(sha(local(base, mode + '/outputs/' + name)) == digest, 'Actual fixture output differs')
    for label in ('solution-r1', 'solution-r2', 'solution-r3'):
        old = read(local(base, 'history/' + label + '/result.json'))
        require(not old['passed'], 'Failed history became a pass')
        for name, digest in {**old['frozenInputs'], **old.get('additionalSolutionInputs', {})}.items():
            target = 'source/' + name if complete.get(name) == digest else 'history/' + label + '/source-delta/' + name
            require(sha(local(base, target)) == digest, 'Historical source provenance differs')
    print(json.dumps(dict(passed=True, archiveOnly=True, fullSolutionInputs=1171, hostingModes=2, hostingChecksPerMode=20,
        files=len(summary['artifacts'])+1, summarySha256=sha(base / 'validation-summary.json'))))


def publish():
    require(not DEST.exists(), 'Preserve accepted evidence')
    state = read(SOLUTION / 'result.json'); require(state['passed'] and state['controllerPid'] == 2236, 'Current solution not complete')
    modes = {mode:read(MODES / (mode + '-r1/result.json')) for mode in ('artifacts', 'default')}
    require(all(row['passed'] for row in modes.values()), 'Both actual Hosting modes required')
    staging = DEST.with_name(DEST.name + '.staging')
    require(not staging.exists() and all(path.resolve().is_relative_to(ROOT.resolve()) for path in (DEST, staging)), 'Preserve checked staging')
    staging.mkdir()

    def copy(source, name, digest=None):
        source = Path(source); target = staging / name
        require(source.is_file() and not source.is_symlink() and not target.exists() and target.resolve().is_relative_to(staging.resolve()), 'Invalid/duplicate artifact')
        digest = digest or sha(source); target.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(source, target)
        require(sha(source) == sha(target) == digest, 'Artifact changed')

    complete = {**state['frozenInputs'], **state['additionalSolutionInputs']}
    for name, digest in complete.items(): copy(SOLUTION / 'source' / name, 'source/' + name, digest)
    copy(SOLUTION / 'result.json', 'solution/result.json'); copy(SOLUTION / 'solution.log', 'solution/solution.log', state['logSha256'])
    for name, digest in state['products'].items(): copy(SOLUTION / name, 'solution/' + name, digest)
    for path in (SOLUTION / 'controls').iterdir():
        if path.is_file(): copy(path, 'controls/' + path.name)
    witness = dict(solution=dict(controllerPid=2236, actualToolSession=27377, actualToolChunk='c69cb0', actualToolExitCode=0,
        resultSha256=sha(SOLUTION / 'result.json')),
        artifacts=dict(controllerPid=38744, actualToolSession=None, actualToolChunk='fbc8fe', actualToolExitCode=0,
            resultSha256=sha(MODES / 'artifacts-r1/result.json')),
        default=dict(controllerPid=43308, actualToolSession=81006, actualToolChunk='3771c3', actualToolExitCode=0,
            resultSha256=sha(MODES / 'default-r1/result.json')))
    for mode, run in modes.items():
        out = MODES / (mode + '-r1'); copy(out / 'result.json', mode + '/result.json')
        for row in run['steps']: copy(out / (row['id'] + '.log'), mode + '/' + row['id'] + '.log', row['logSha256'])
        assembly = SOLUTION / 'artifacts/bin/RizToolbox.Hosting.Tests/release/RizToolbox.Hosting.Tests.dll' if mode == 'artifacts' else SOLUTION / 'source/tests/RizToolbox.Hosting.Tests/bin/Release/net8.0/RizToolbox.Hosting.Tests.dll'
        for path in assembly.parent.rglob('*'):
            if path.is_file(): copy(path, mode + '/outputs/' + path.relative_to(assembly.parent).as_posix())
    hold_work = ROOT / 'work/hold-discovery-guard-next-20261114'
    for label in ('solution-r1', 'solution-r2', 'solution-r3'):
        prior = SOLUTION.parent / label; old = read(prior / 'result.json')
        copy(prior / 'result.json', 'history/' + label + '/result.json'); copy(prior / 'solution.log', 'history/' + label + '/solution.log', old['logSha256'])
        copy(hold_work / (label + '-root-exit.json'), 'history/' + label + '/root-exit.json')
        for path in (prior / 'controls').iterdir():
            if path.is_file(): copy(path, 'history/' + label + '/controls/' + path.name)
        for name, digest in {**old['frozenInputs'], **old.get('additionalSolutionInputs', {})}.items():
            if complete.get(name) != digest: copy(prior / 'source' / name, 'history/' + label + '/source-delta/' + name, digest)
    for name in ('publish_evidence.py', 'validate_hosting.py', 'source-review.md'): copy(WORK / name, 'controls/' + name)
    (staging / 'controls/root-exits.json').write_text(json.dumps(witness, indent=2) + '\n', encoding='utf-8')
    summary = dict(schema='complete-solution-hold-evidence/v1', passed=True, compilerInputs=939, fullSolutionInputs=1171,
        additionalSolutionInputs=232, solutionProjects=45, hostingChecksPerMode=20, hostingModes=2,
        fullManagedMatrix=False, mainApplied=False, sdkGoalComplete=False, installed=False, gameStarted=False,
        artifacts={path.relative_to(staging).as_posix(): dict(bytes=path.stat().st_size, sha256=sha(path)) for path in sorted(staging.rglob('*')) if path.is_file()})
    (staging / 'validation-summary.json').write_text(json.dumps(summary, indent=2) + '\n', encoding='utf-8')
    check(staging); os.rename(staging, DEST); print('Published ' + str(DEST))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(); parser.add_argument('--check-only', type=Path); args = parser.parse_args()
    check(args.check_only) if args.check_only else publish()
