"""Publish and independently check this candidate's settings/failed-result observations."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil

WORK = Path(__file__).resolve().parent
ROOT = WORK.parents[1]
CACHE = Path('D:/riz-build-cache-next/runtime-tick-admission-20261115')
DEST = ROOT / 'docs/validation/game-result-flow-20261115'
SESSION = '20261115T051638374Z-892aaffc8645488f9462136b3308845c'
RUN_ID = '9674fa3a160c412b969673391aa14dcb'
sha = lambda path: hashlib.sha256(Path(path).read_bytes()).hexdigest()
read = lambda path: json.loads(Path(path).read_text(encoding='utf-8-sig'))


def require(value, reason):
    if not value:
        raise RuntimeError(reason)


def local(base, name):
    parts = str(name).split('/')
    require(all(part and part not in ('.', '..') and ':' not in part and '\\' not in part for part in parts), 'Unsafe archive path')
    path = base.joinpath(*parts)
    require(path.resolve().is_relative_to(base.resolve()) and path.resolve() != base.resolve(), 'Archive path escaped')
    for parent in (path, *path.parents):
        require(not parent.is_symlink() and not getattr(parent.lstat(), 'st_file_attributes', 0) & 0x400, 'Reparse archive path')
        if parent == base:
            break
    require(path.is_file(), 'Missing archived artifact')
    return path


def check(base):
    base = Path(base).resolve()
    at = lambda name: local(base, name)
    obj = lambda name: read(at(name))
    summary = obj('validation-summary.json')
    require(summary['schema'] == 'current-game-settings-failed-result-evidence/v1', 'Wrong evidence scope')
    require({p.relative_to(base).as_posix() for p in base.rglob('*') if p.is_file()}
            == set(summary['artifacts']) | {'validation-summary.json'}, 'Artifact inventory differs')
    for name, row in summary['artifacts'].items():
        path = at(name)
        require(sha(path) == row['sha256'] and path.stat().st_size == row['bytes'], 'Artifact changed: ' + name)
    settings = obj('phases/settings-page/observation.json')
    result = obj('phases/result-page/observation.json')
    for phase in (settings, result):
        prefix = 'phases/' + phase['phase'] + '/'
        require(phase['gameProcessId'] == 25008 and phase['diagnosticSessionId'] == SESSION
                and phase['actualProcessLiveAndImageMatched'] and phase['actualInstalledFilesMatched'] == 23
                and phase['gameAssemblyUnchanged'] and not phase['currentSessionErrorLines']
                and not phase['gameSettingsModifiedByThisCapture'], 'Phase identity or scope differs')
        for name, digest in phase['files'].items():
            require(sha(at(prefix + name)) == digest, 'Frozen phase changed')
        status = obj(prefix + 'status.json')
        require(status['diagnostics']['SessionId'] == SESSION and status['sdkVersion'] == '1.2.0'
                and len(status['adapterModules']) == 30, 'Current SDK status differs')
    require(settings['moduleStates']['rizline.settings.read'] == settings['moduleStates']['rizline.settings.override'] == 1,
            'Actual settings page did not activate the settings modules')
    text = at('phases/result-page/toolbox-current-session.log').read_text(encoding='utf-8-sig')
    prefix = '[example.events] Observed game.run.finished: '
    finished = [json.loads(line.split(prefix, 1)[1].strip()) for line in text.splitlines() if prefix in line]
    require(len(finished) == 1, 'Expected one actual SDK result')
    finished = finished[0]
    require(finished['RunId'] == RUN_ID and finished['ChartId'] == 'chart.GreatnessEarth.Axya.0.EZ'
            and finished['AssociatedWithObservedStart'] and finished['SessionId'] == '8b008e4fc53c42c6aefe7e37f2f5a589',
            'The actual result is not associated with this observed start')
    require(all(finished[name] == number for name, number in dict(Score=0, RawCompletion=0, Perfect=0, MaxPerfect=0, Bad=0, Miss=479).items())
            and finished['Crashed'] is True and finished['Skipped'] is False and finished['EndReason'] == 2,
            'Actual native failed result fields differ')
    require(result['lifecycleEvents']['RunStarted'] == result['lifecycleEvents']['RunEnded'] == 1
            and 'Observed lifecycle RunStarted:' in text and 'Observed lifecycle RunEnded:' in text
            and 'phase=Finished; run=' + RUN_ID in text
            and 'Result-origin return: accepted=True; run=' + RUN_ID in text
            and 'Actual InsideLevelResult matched its rooted ControllerOver entry origin' in text
            and 'AssociatedWithObservedStart = True, EndReason = Failed' in text,
            'Actual start/original-return/end provenance is incomplete')
    require(re.search(r'Observed lifecycle RunStarted:.*run=' + RUN_ID + ';', text)
            and re.search(r'Observed lifecycle RunEnded:.*run=' + RUN_ID + ';', text), 'Start/end RunId differs')
    install = obj('lineage/install-result.json')
    candidate = obj('lineage/candidate-result.json')
    require(settings['actualInstallResultSha256'] == result['actualInstallResultSha256'] == sha(at('lineage/install-result.json'))
            and install['passed'] and install['installed'] and install['actualInstallerExitCode'] == 0
            and install['validationResultSha256'] == sha(at('lineage/candidate-result.json'))
            and candidate['passed'] and candidate['fullFrozenInputsUnchanged'] and len(candidate['fullFrozenInputs']) == 1172,
            'The observations do not belong to the exact current candidate')
    for name, digest in summary['sourceInputs'].items():
        require(candidate['fullFrozenInputs'][name] == sha(at('source/' + name)) == digest, 'Referenced current SDK source differs')
    preview = obj('lineage/preview-receipt.json')
    require(preview['sha256'] == '76a3ee353a73c0436547b6fbc13e90a4e27ad3170abc1cd7802ad5842fb8801e', 'Preview identity differs')
    acceptance = obj('limited-acceptance.json')
    require(acceptance['settingsPageModulesActive'] and acceptance['failedResultAssociatedWithObservedStart']
            and acceptance['uiCountsMatched'] and acceptance['nativeResult'] == finished
            and acceptance['resultObservationSha256'] == sha(at('phases/result-page/observation.json')),
            'Limited result acceptance differs')
    for name in ('normalClearVerified', 'fullB01Matrix', 'exactBackCancellationVerified', 'settingsWriteAndRollbackVerified',
                 'allJudgementSourcesVerified', 'gameExitVerified', 'fullGameAcceptance', 'sdkGoalComplete'):
        require(acceptance[name] is False, 'Unobserved scope was promoted: ' + name)
    output = dict(passed=True, archiveOnly=True, files=len(summary['artifacts']), failedResultAssociatedWithObservedStart=True,
                  normalClearVerified=False, sdkGoalComplete=False, summarySha256=sha(at('validation-summary.json')))
    print(json.dumps(output))
    return output


def publish():
    staging = DEST.with_name(DEST.name + '.staging')
    require(not DEST.exists() and not staging.exists(), 'Preserve previous evidence')
    staging.mkdir(parents=True)

    def copy(origin, relative):
        target = staging / relative
        require(target.resolve().is_relative_to(staging.resolve()) and not target.exists(), 'Duplicate or escaped role')
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(origin, target)
        require(sha(origin) == sha(target), 'Evidence changed during copy')

    for name in ('settings-page', 'result-page'):
        for path in (CACHE / 'runtime-r3/phases' / name).iterdir():
            require(path.is_file(), 'Unexpected nested phase evidence')
            copy(path, 'phases/' + name + '/' + path.name)
    copy(CACHE / 'install-r3/result.json', 'lineage/install-result.json')
    copy(CACHE / 'run-r2/result.json', 'lineage/candidate-result.json')
    copy(ROOT / 'artifacts/delivery-20261115/startup-preview-receipt.json', 'lineage/preview-receipt.json')
    source_names = ['src/RizToolbox.Adapter/' + name for name in
                    ('GameEventState.cs', 'GameEventObserver.cs', 'Modules/ExactResultOriginModule.cs', 'Modules/ExactBackCancellationModule.cs')]
    source_names += ['src/RizToolbox.Sdk/LifecycleContracts.cs']
    source_inputs = {}
    for name in source_names:
        copy(CACHE / 'run-r2/source' / name, 'source/' + name)
        source_inputs[name] = sha(CACHE / 'run-r2/source' / name)
    copy(WORK / 'source-review.md', 'source-review.md')
    copy(WORK / 'capture_phase.py', 'controls/capture_phase.py')
    copy(__file__, 'controls/publish_evidence.py')
    text = (staging / 'phases/result-page/toolbox-current-session.log').read_text(encoding='utf-8-sig')
    prefix = '[example.events] Observed game.run.finished: '
    finished = [json.loads(line.split(prefix, 1)[1].strip()) for line in text.splitlines() if prefix in line]
    require(len(finished) == 1, 'Actual single-result observation is unavailable')
    acceptance = dict(schema='current-game-settings-failed-result-limited-acceptance/v1',
                      settingsPageModulesActive=True, failedResultAssociatedWithObservedStart=True, uiCountsMatched=True,
                      nativeResult=finished[0], resultObservationSha256=sha(staging / 'phases/result-page/observation.json'),
                      uiObservationSource='Root actual sky window-state inspection after the user reported 跑完了; values are preserved in the phase receipt.',
                      normalClearVerified=False, fullB01Matrix=False, exactBackCancellationVerified=False,
                      settingsWriteAndRollbackVerified=False, allJudgementSourcesVerified=False, gameExitVerified=False,
                      fullGameAcceptance=False, sdkGoalComplete=False)
    (staging / 'limited-acceptance.json').write_text(json.dumps(acceptance, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    inventory = {p.relative_to(staging).as_posix(): dict(sha256=sha(p), bytes=p.stat().st_size) for p in sorted(staging.rglob('*')) if p.is_file()}
    summary = dict(schema='current-game-settings-failed-result-evidence/v1', artifacts=inventory, sourceInputs=source_inputs,
                   copiedFullCompilerTree=False, gameBinariesIncluded=False, uiScreenshotBytesIncluded=False, sdkGoalComplete=False)
    (staging / 'validation-summary.json').write_text(json.dumps(summary, indent=2) + '\n', encoding='utf-8')
    check(staging)
    os.replace(staging, DEST)
    print(json.dumps(dict(published=str(DEST), files=len(inventory), summarySha256=sha(DEST / 'validation-summary.json'))))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check-only', type=Path)
    args = parser.parse_args()
    check(args.check_only) if args.check_only else publish()
