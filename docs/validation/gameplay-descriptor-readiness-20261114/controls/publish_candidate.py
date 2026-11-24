"""Publish the exact complete candidate; archive checking opens no original paths."""
import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import shutil

ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).resolve().parent
CACHE = Path('D:/riz-build-cache-next/gameplay-descriptor-readiness-20261114')
RUN = CACHE / 'run-r3'
DEST = ROOT / 'docs/validation/gameplay-descriptor-readiness-20261114'
HISTORY = ('run-r1', 'run-r2', 'focused-r3', 'focused-r4', 'focused-r5')


def sha(path):
    with Path(path).open('rb') as stream: return hashlib.file_digest(stream, 'sha256').hexdigest()


def read(path): return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def require(value, reason):
    if not value: raise RuntimeError(reason)


def regular(path):
    attributes = path.lstat()
    require(path.is_file() and not path.is_symlink()
        and not (getattr(attributes, 'st_file_attributes', 0) & 0x400), 'Nonregular/reparse artifact')


def local(base, name):
    raw = base / name
    require(raw.resolve().is_relative_to(base) and raw.resolve() != base, 'Archive path escaped')
    for part in (raw, *raw.parents):
        if part == base: break
        require(not part.is_symlink() and not (getattr(part.lstat(), 'st_file_attributes', 0) & 0x400), 'Reparse archive path')
    regular(raw)
    return raw


def check(archive):
    archive = archive.resolve(); summary = read(local(archive, 'validation-summary.json'))
    require(summary['schema'] == 'gameplay-descriptor-readiness-evidence/v1' and summary['passed'], 'Wrong candidate archive')
    for name, item in summary['artifacts'].items():
        path = local(archive, name)
        require(path.stat().st_size == item['bytes'] and sha(path) == item['sha256'], 'Artifact differs: ' + name)
    actual = {path.relative_to(archive).as_posix() for path in archive.rglob('*') if path.is_file()}
    require(actual == set(summary['artifacts']) | {'validation-summary.json'}, 'Inventory differs')
    state = read(local(archive, 'run/result.json')); witness = read(local(archive, 'controls/run-r3-root-exit.json'))
    require(state['controllerPid'] == witness['controllerPid'] == 39064
        and witness['actualToolExitCode'] == 0 and witness['actualToolSession'] == 78272
        and witness['functionalPass'] and witness['resultSha256'] == sha(local(archive, 'run/result.json')),
        'Original terminal receipt differs')
    require(state['passed'] and state['finishedUtc'] and state['fullFrozenInputsUnchanged']
        and len(state['fullFrozenInputs']) == state['fullSolutionInputCount'] == 1171
        and state['solutionProjects'] == 45 and state['scopedInputCount'] == 939
        and not state['sourceGenerationOnly'], 'Complete candidate scope differs')
    require([row['id'] for row in state['steps']] == state['expectedSteps'] and len(state['steps']) == 90
        and state['suiteCount'] == len(state['plannedSuites']) == 27 and state['selectedHoldChecks'] == 62,
        'Natural matrix identities differ')
    for row in state['steps']:
        require(row['naturalExit'] and row['exitCode'] == 0 and not row['failNames']
            and row['logSha256'] == sha(local(archive, 'run/' + row['id'] + '.log')), 'Direct step failed or changed')
        if row['id'] != 'solution':
            require(row['passCount'] == len(row['passNames']) > 0, 'Missing actual direct assertions')
        if row['id'] == 'suite-GameplaySourceGeneration': require(row['passCount'] == 14, 'Source generation coverage differs')
        if row['id'] == 'suite-Gameplay': require(row['passCount'] == 175, 'Actual native backend coverage differs')
    require(sum(row['passCount'] for row in state['steps'] if row['id'].startswith('suite-')) == state['suitePassCount'], 'Suite total differs')
    require(sum(row['passCount'] for row in state['steps'] if row['id'] != 'solution' and not row['id'].startswith('suite-')) == 62, 'Hold total differs')
    for name, digest in state['fullFrozenInputs'].items():
        require(sha(local(archive, 'source/' + name)) == digest, 'Frozen candidate source differs')
    for name, digest in state['products'].items():
        require(sha(local(archive, 'run/' + name)) == digest, 'Direct product/dependency differs')
    for key, row in state['nativeFixtures'].items():
        require(sha(local(archive, 'fixtures/' + key + '/' + Path(row['path']).name)) == row['sha256'], 'Owned native fixture differs')
    for label in HISTORY:
        old = read(local(archive, 'history/' + label + '/result.json'))
        exit_record = read(local(archive, 'controls/' + label + '-root-exit.json'))
        require(exit_record['controllerPid'] == old['controllerPid']
            and exit_record['resultSha256'] == sha(local(archive, 'history/' + label + '/result.json'))
            and old['passed'] == (exit_record['actualToolExitCode'] == 0)
            and old['passed'] == (label == 'focused-r5'), 'Historical result overclaimed')
        changed = summary['historySourceDeltas'][label]
        require(set(changed) <= set(state['overlay']), 'Historical source escaped candidate overlay')
        for name, digest in old['fullFrozenInputs'].items():
            source = 'history/' + label + '/source/' + name if name in changed else 'source/' + name
            require(sha(local(archive, source)) == digest, 'Historical source closure differs')
        for row in old['steps']:
            require(sha(local(archive, 'history/' + label + '/' + row['id'] + '.log')) == row['logSha256'], 'Historical log differs')
    for key in ('fullManagedMatrix', 'fullJudgementsRerun', 'parentRendererSuiteRerun', 'mainApplied', 'sdkGoalComplete', 'installed', 'gameStarted'):
        require(state[key] is False and summary[key] is False, 'Scope overclaimed: ' + key)
    print(json.dumps(dict(passed=True, archiveOnly=True, files=len(actual),
        bytes=sum(row['bytes'] for row in summary['artifacts'].values()) + (archive / 'validation-summary.json').stat().st_size,
        ordinarySuites=27, suiteCases=state['suitePassCount'], selectedHoldCases=62, summarySha256=sha(archive / 'validation-summary.json'))))


def publish():
    state = read(RUN / 'result.json'); witness = read(WORK / 'run-r3-root-exit.json')
    require(state['passed'] and state['finishedUtc'] and state['fullFrozenInputsUnchanged']
        and witness['actualToolExitCode'] == 0 and witness['resultSha256'] == sha(RUN / 'result.json'), 'Exact candidate has not passed')
    require(all(sha(RUN / 'source' / name) == digest for name, digest in state['fullFrozenInputs'].items())
        and all(sha(WORK / name) == digest for name, digest in state['overlay'].items()), 'Final candidate bytes changed')
    staging = DEST.with_name(DEST.name + '.staging')
    require(not DEST.exists() and not staging.exists(), 'Preserve prior candidate archive')
    staging.mkdir()

    def copy(origin, name, digest=None):
        origin = Path(origin); regular(origin); digest = digest or sha(origin); target = staging / name
        require(target.resolve().is_relative_to(staging.resolve()) and not target.exists(), 'Duplicate/escaped artifact')
        target.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(origin, target)
        require(sha(origin) == sha(target) == digest, 'Artifact changed during copy')

    copy(RUN / 'result.json', 'run/result.json')
    for name, digest in state['fullFrozenInputs'].items(): copy(RUN / 'source' / name, 'source/' + name, digest)
    for name, digest in state['products'].items(): copy(RUN / name, 'run/' + name, digest)
    for row in state['steps']: copy(RUN / (row['id'] + '.log'), 'run/' + row['id'] + '.log', row['logSha256'])
    for path in (RUN / 'controls').iterdir():
        if path.is_file(): copy(path, 'controls/' + path.name)
    for name in ('record_terminal.py', 'record_r1_failure.py', 'record_r2_failure.py', 'stage_service_capture.py', 'source-review.md', 'publish_candidate.py', 'run-r3-root-exit.json'):
        copy(WORK / name, 'controls/' + name)
    for key, row in state['nativeFixtures'].items(): copy(row['path'], 'fixtures/' + key + '/' + Path(row['path']).name, row['sha256'])
    deltas = {}
    for label in HISTORY:
        history = CACHE / label; old = read(history / 'result.json')
        copy(history / 'result.json', 'history/' + label + '/result.json')
        copy(WORK / (label + '-root-exit.json'), 'controls/' + label + '-root-exit.json')
        require(set(old['fullFrozenInputs']) == set(state['fullFrozenInputs']), 'Historical source closure differs')
        changed = {name:digest for name, digest in old['fullFrozenInputs'].items() if digest != state['fullFrozenInputs'][name]}
        deltas[label] = changed
        for name, digest in changed.items(): copy(history / 'source' / name, 'history/' + label + '/source/' + name, digest)
        for path in (history / 'controls').iterdir():
            if path.is_file(): copy(path, 'history/' + label + '/controls/' + path.name)
        for row in old['steps']: copy(history / (row['id'] + '.log'), 'history/' + label + '/' + row['id'] + '.log', row['logSha256'])
    inventory = {path.relative_to(staging).as_posix():dict(bytes=path.stat().st_size, sha256=sha(path))
        for path in sorted(staging.rglob('*')) if path.is_file()}
    summary = dict(schema='gameplay-descriptor-readiness-evidence/v1', createdUtc=dt.datetime.now(dt.timezone.utc).isoformat(),
        passed=True, completeSolutionInputs=1171, solutionProjects=45, ordinaryManagedSuites=27, suiteCases=state['suitePassCount'], selectedHoldCases=62,
        fullManagedMatrix=False, fullJudgementsRerun=False, parentRendererSuiteRerun=False, mainApplied=False,
        sdkGoalComplete=False, installed=False, gameStarted=False, historySourceDeltas=deltas, artifacts=inventory)
    (staging / 'validation-summary.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    check(staging); os.replace(staging, DEST); print('Published ' + str(DEST))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(); parser.add_argument('--check-only', type=Path); args = parser.parse_args()
    if args.check_only: check(args.check_only)
    else: publish()
