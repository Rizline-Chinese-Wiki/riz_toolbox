"""Archive only the exact successfully rebased graph and its source lineage."""
import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import shutil

ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).resolve().parent
RUN = Path('D:/riz-build-cache-next/foundation-current-integration-20261114/run-r2')
PARENT = Path('D:/riz-build-cache-next/line-producer-control-20261114/main-r4')
BASE = ROOT / 'docs/validation/gameplay-descriptor-readiness-20261114'
META = ROOT / 'work/producer-metadata-history-next-20261114'
DEST = ROOT / 'docs/validation/foundation-current-integration-20261114'


def sha(path):
    with Path(path).open('rb') as stream: return hashlib.file_digest(stream, 'sha256').hexdigest()


def read(path): return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def require(value, reason):
    if not value: raise RuntimeError(reason)


def regular(path):
    require(path.is_file() and not path.is_symlink()
        and not (getattr(path.lstat(), 'st_file_attributes', 0) & 0x400), 'Nonregular/reparse evidence')


def local(base, name):
    raw = base / name
    require(raw.resolve().is_relative_to(base) and raw.resolve() != base, 'Archive path escaped')
    for part in (raw, *raw.parents):
        if part == base: break
        require(not part.is_symlink() and not (getattr(part.lstat(), 'st_file_attributes', 0) & 0x400), 'Reparse archive path')
    regular(raw); return raw


def check(archive):
    archive = archive.resolve(); summary = read(local(archive, 'validation-summary.json'))
    require(summary['schema'] == 'current-foundation-rebased-evidence/v2' and summary['passed'], 'Wrong current candidate archive')
    for name, row in summary['artifacts'].items():
        path = local(archive, name)
        require(path.stat().st_size == row['bytes'] and sha(path) == row['sha256'], 'Artifact differs: ' + name)
    actual = {path.relative_to(archive).as_posix() for path in archive.rglob('*') if path.is_file()}
    require(actual == set(summary['artifacts']) | {'validation-summary.json'}, 'Archive inventory differs')
    state = read(local(archive, 'run/result.json')); witness = read(local(archive, 'controls/run-r2-root-exit.json'))
    manifest = read(local(archive, 'controls/candidate-manifest.json'))
    require(state['passed'] and state['fullFrozenInputsUnchanged'] and state['mainSourceUnchanged']
        and state['controllerPid'] == witness['controllerPid'] == 28536
        and witness['actualControllerExitCode'] == 0 and witness['supervisorPid'] == 34024
        and witness['functionalPass'] and witness['resultSha256'] == sha(local(archive, 'run/result.json')),
        'Original current candidate exit differs')
    require(state['manifestSha256'] == sha(local(archive, 'controls/candidate-manifest.json'))
        and len(state['fullFrozenInputs']) == state['completeSolutionInputs'] == manifest['completeSolutionInputs'] == 1171
        and state['fullFrozenInputs'] == state['fullFrozenInputsAfter'] == manifest['expectedInputs']
        and state['scopedInputs'] == 939 and state['additionalSolutionInputs'] == 232 and state['solutionProjects'] == 45,
        'Complete current solution graph differs')
    require(len(state['steps']) == 95 and [row['id'] for row in state['steps']] == state['expectedSteps']
        and state['suiteCount'] == len(state['plannedSuites']) == 27 and state['selectedHoldChecks'] == 62
        and state['selectedProducerCases'] == [11, 12, 8], 'Actual current matrix differs')
    for row in state['steps']:
        require(row['naturalExit'] and row['exitCode'] == 0 and not row['failNames']
            and row['logSha256'] == sha(local(archive, 'run/' + row['id'] + '.log')), 'Direct step differs')
        if row['id'] not in ('six-release', 'solution'): require(row['passCount'] == len(row['passNames']) > 0, 'Direct assertion count missing')
        if row['id'] == 'suite-GameplaySourceGeneration': require(row['passCount'] == 14, 'Source generation coverage differs')
        if row['id'] == 'suite-Gameplay': require(row['passCount'] == 175, 'Gameplay capture cleanup coverage differs')
    require(state['steps'][0]['warnings'] == state['steps'][0]['errors'] == state['steps'][1]['errors'] == 0, 'Current production/solution build differs')
    require(sum(row['passCount'] for row in state['steps'] if row['id'].startswith('suite-')) == state['suitePassCount'], 'Current suite total differs')
    for name, digest in {**state['fullFrozenInputs'], **state['auxiliaryInputs']}.items():
        require(sha(local(archive, 'source/' + name)) == digest, 'Current frozen source differs')
    for name, digest in state['controls'].items():
        require(sha(local(archive, 'controls/' + name)) == digest, 'Pinned current control differs')
    for name, digest in state['products'].items():
        require(sha(local(archive, 'run/' + name)) == digest, 'Actual test output/dependency differs')
    require(state['productionAfterSix'] == state['productionAfterTests'], 'Six production pairs changed')
    for name, pair in state['productionAfterSix'].items():
        for ext, digest in pair.items():
            require(sha(local(archive, 'run/artifacts/bin/' + name + '/release/' + name + ext)) == digest, 'Six current production pair differs')
    for key, row in state['nativeFixtures'].items():
        require(sha(local(archive, 'fixtures/' + key + '/' + Path(row['path']).name)) == row['sha256'], 'Owned fixture differs')
    waited = read(local(archive, 'supervision/controller-exit.json'))
    require(waited['controllerExited'] and waited['actualControllerExitCode'] == 0
        and waited['controllerPid'] == waited['resultControllerPid'] == state['controllerPid'] == 28536
        and waited['supervisorPid'] == 34024 and waited['resultPassed'] and waited['resultInputsUnchanged']
        and waited['resultMainSourceUnchanged'] and waited['exitObservedBy'] == 'original subprocess.Popen.wait process handle'
        and waited['resultSha256'] == witness['resultSha256'] == sha(local(archive, 'run/result.json'))
        and waited['controllerControlSha256'] == sha(local(archive, 'controls/validate_candidate_r2.py'))
        and waited['supervisorControlSha256'] == sha(local(archive, 'controls/supervise_candidate_r2.py'))
        and witness['supervisionSha256'] == sha(local(archive, 'supervision/controller-exit.json')),
        'Original owning supervisor terminal differs')
    old = read(local(archive, 'history/run-r1/result.json'))
    lost = read(local(archive, 'controls/run-r1-interruption-receipt.json'))
    require(not old['passed'] and 'finishedUtc' not in old and not lost['functionalPass']
        and lost['originalToolExitCode'] is None and lost['actualControllerExitCode'] is None
        and not lost['controllerExists'] and not lost['unfinishedChildrenExist']
        and old['fullFrozenInputs'] == state['fullFrozenInputs'] and old['auxiliaryInputs'] == state['auxiliaryInputs']
        and lost['resultSha256'] == sha(local(archive, 'history/run-r1/result.json'))
        and lost['incompleteLogSha256'] == sha(local(archive, 'history/run-r1/suite-Runtime.log')),
        'Interrupted/unknown exit history overclaimed')
    for row in old['steps']:
        if row.get('logSha256'):
            require(sha(local(archive, 'history/run-r1/' + row['id'] + '.log')) == row['logSha256'], 'Interrupted direct log differs')
    # This is source lineage only, not revalidation of earlier test matrices.
    prior = read(local(archive, 'lineage/descriptor-result.json'))
    require(sha(local(archive, 'lineage/descriptor-summary.json')) == manifest['descriptorSummarySha256']
        and sha(local(archive, 'lineage/descriptor-result.json')) == manifest['descriptorResultSha256'], 'Descriptor source basis differs')
    fixture = manifest['rebasePreservedTestFixture']
    require({name for name, digest in prior['fullFrozenInputs'].items() if digest != state['fullFrozenInputs'][name]} == {fixture}, 'Rebase source difference escaped the test fixture')
    require(sha(local(archive, 'lineage/descriptor-source/' + fixture)) == prior['fullFrozenInputs'][fixture]
        and state['fullFrozenInputs'][fixture] == manifest['parentFullInputs'][fixture], 'Rebase restored an older history fixture')
    for name, digest in manifest['parentFullInputs'].items():
        old = 'source/' + name if state['fullFrozenInputs'].get(name) == digest else 'lineage/parent-source/' + name
        require(sha(local(archive, old)) == digest, 'Parent source graph cannot be reconstructed')
    require(sha(local(archive, 'lineage/parent-source/scripts/Build.ps1')) == manifest['scriptBaseSha256'], 'Parent build matrix differs')
    for key in ('fullManagedMatrix', 'fullJudgementsRerun', 'parentRendererSuiteRerun', 'mainApplied', 'sdkGoalComplete', 'completeSeekPermitted', 'installed', 'gameStarted'):
        require(state[key] is False and summary[key] is False, 'Scope overclaimed: ' + key)
    print(json.dumps(dict(passed=True, archiveOnly=True, files=len(actual),
        bytes=sum(row['bytes'] for row in summary['artifacts'].values()) + (archive / 'validation-summary.json').stat().st_size,
        suiteCases=state['suitePassCount'], holdCases=62, producerCases=3, summarySha256=sha(archive / 'validation-summary.json'))))


def publish():
    state = read(RUN / 'result.json'); witness = read(WORK / 'run-r2-root-exit.json')
    require(state['passed'] and state['finishedUtc'] and state['fullFrozenInputsUnchanged'] and state['mainSourceUnchanged']
        and witness['actualControllerExitCode'] == 0 and witness['resultSha256'] == sha(RUN / 'result.json'), 'Current candidate has not passed naturally')
    manifest = read(WORK / 'candidate-manifest.json')
    require(all(sha(ROOT / name) == digest for name,digest in manifest['parentFullInputs'].items())
        and sha(ROOT / 'scripts/Build.ps1') == manifest['scriptBaseSha256'], 'Main source changed before current publication')
    staging = DEST.with_name(DEST.name + '.staging')
    require(not DEST.exists() and not staging.exists(), 'Preserve previous current archives'); staging.mkdir()

    def copy(origin, name, digest=None):
        origin = Path(origin); regular(origin); digest = digest or sha(origin); target = staging / name
        require(target.resolve().is_relative_to(staging.resolve()) and not target.exists(), 'Duplicate/escaped archive artifact')
        target.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(origin, target)
        require(sha(origin) == sha(target) == digest, 'Current artifact changed during copy')

    copy(RUN / 'result.json', 'run/result.json')
    for name, digest in {**state['fullFrozenInputs'], **state['auxiliaryInputs']}.items(): copy(RUN / 'source' / name, 'source/' + name, digest)
    for name, digest in state['products'].items(): copy(RUN / name, 'run/' + name, digest)
    for row in state['steps']: copy(RUN / (row['id'] + '.log'), 'run/' + row['id'] + '.log', row['logSha256'])
    for name, digest in state['controls'].items(): copy(RUN / 'controls' / name, 'controls/' + name, digest)
    for name in ('record_r2_exit.py', 'run-r2-root-exit.json', 'publish_evidence_r2.py', 'supervise_candidate_r2.py', 'prepare_r2_acceptance.py', 'prepare_supervised_r2.py'): copy(WORK / name, 'controls/' + name)
    for key, row in state['nativeFixtures'].items(): copy(row['path'], 'fixtures/' + key + '/' + Path(row['path']).name, row['sha256'])
    copy(BASE / 'validation-summary.json', 'lineage/descriptor-summary.json', manifest['descriptorSummarySha256'])
    copy(BASE / 'run/result.json', 'lineage/descriptor-result.json', manifest['descriptorResultSha256'])
    copy(BASE / 'controls/run-r3-root-exit.json', 'lineage/descriptor-root-exit.json')
    fixture = manifest['rebasePreservedTestFixture']
    copy(BASE / 'source' / fixture, 'lineage/descriptor-source/' + fixture)
    for name, digest in manifest['parentFullInputs'].items():
        if state['fullFrozenInputs'].get(name) != digest: copy(PARENT / 'source' / name, 'lineage/parent-source/' + name, digest)
    copy(ROOT / 'scripts/Build.ps1', 'lineage/parent-source/scripts/Build.ps1', manifest['scriptBaseSha256'])
    copy(META / 'payload-manifest.json', 'lineage/metadata-repair-payload.json', manifest['parentMetadataPayloadSha256'])
    copy(META / 'application-record.json', 'lineage/metadata-repair-application.json')
    for name in ('launch.json', 'supervision.json', 'controller-exit.json', 'controller.log', 'supervisor.log'):
        copy(RUN.parent / 'run-r2-supervision' / name, 'supervision/' + name)
    interrupted = RUN.parent / 'run-r1'; old = read(interrupted / 'result.json')
    require(old['fullFrozenInputs'] == state['fullFrozenInputs'] and old['auxiliaryInputs'] == state['auxiliaryInputs'],
        'Interrupted source differs from the new complete graph')
    copy(interrupted / 'result.json', 'history/run-r1/result.json')
    for path in (interrupted / 'controls').iterdir():
        if path.is_file(): copy(path, 'history/run-r1/controls/' + path.name)
    for row in old['steps']:
        log = interrupted / (row['id'] + '.log')
        if log.exists(): copy(log, 'history/run-r1/' + row['id'] + '.log', row.get('logSha256'))
    inventory = {path.relative_to(staging).as_posix():dict(bytes=path.stat().st_size, sha256=sha(path))
        for path in sorted(staging.rglob('*')) if path.is_file()}
    summary = dict(schema='current-foundation-rebased-evidence/v2', createdUtc=dt.datetime.now(dt.timezone.utc).isoformat(), passed=True,
        completeSolutionInputs=1171, solutionProjects=45, ordinarySuites=27, suiteCases=state['suitePassCount'], holdCases=62, producerCases=3,
        fullManagedMatrix=False, fullJudgementsRerun=False, parentRendererSuiteRerun=False, mainApplied=False,
        sdkGoalComplete=False, completeSeekPermitted=False, installed=False, gameStarted=False, sourceLineageOnly=True, artifacts=inventory)
    (staging / 'validation-summary.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    check(staging); os.replace(staging, DEST); print('Published ' + str(DEST))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(); parser.add_argument('--check-only', type=Path); args = parser.parse_args()
    if args.check_only: check(args.check_only)
    else: publish()
