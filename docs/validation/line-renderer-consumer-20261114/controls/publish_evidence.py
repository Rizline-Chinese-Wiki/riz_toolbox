"""Publish a self-contained evidence archive, or verify it without original paths."""
import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import shutil
import stat

ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).resolve().parent
CACHE = Path('D:/riz-build-cache-next/line-renderer-consumer-20261114')
FINAL = ROOT / 'docs/validation/line-renderer-consumer-20261114'
SIX = ['RizToolbox.' + n for n in ('Sdk', 'Core', 'Runtime', 'Adapter', 'Hosting', 'Loader')]
SOURCE_CASES = [13, 14, 25, 26, 27, 28, 29, 30]


def sha(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def require(ok, reason):
    if not ok:
        raise RuntimeError(reason)


def inventory(folder):
    result = {}
    for path in folder.rglob('*'):
        attrs = path.lstat()
        require(not path.is_symlink() and not (getattr(attrs, 'st_file_attributes', 0) & 0x400), 'Reparse/symlink archive entry')
        if stat.S_ISDIR(attrs.st_mode):
            continue
        require(stat.S_ISREG(attrs.st_mode), 'Non-regular archive entry')
        relative = path.relative_to(folder).as_posix()
        result[relative] = dict(sha256=sha(path), bytes=attrs.st_size)
    return result


def check(folder):
    # This branch never reads ROOT, CACHE, WORK, original logs, fixtures, or the
    # provenance paths stored as strings in archived controller records.
    folder = folder.resolve()
    summary = read(folder / 'validation-summary.json')
    require(summary['schema'] == 'renderer-consumer-evidence/v1', 'Archive schema differs')
    actual = inventory(folder)
    require(set(actual) == set(summary['files']) | {'validation-summary.json'}, 'Archive entries differ')
    for name, row in summary['files'].items():
        require(actual[name] == row, 'Archived file changed: ' + name)
    require(summary['fileCountIncludingSummary'] == len(actual), 'File count differs')
    state = read(folder / 'run/result.json')
    require(state['passed'] and state['inputsUnchanged'] and state['compilerInputs'] == 922
            and state['inputsBefore'] == state['inputsAfter'], 'Main graph was not accepted')
    require(state['selectedSourceCases'] == SOURCE_CASES and state['rendererCases'] == 12
            and state['readerCases'] == 7 and state['endpointWriterCases'] == 36
            and state['defaultRuntimeCases'] == 375
            and not state['fullSdkMatrixRerun'] and not state['fullSourceSuiteRerun'], 'Main validation scope differs')
    expected_steps = [('six-release', None), ('solution', None)]
    expected_steps += [('renderer-' + str(i), 1) for i in range(1, 13)]
    expected_steps += [('reader-seven', 7)]
    expected_steps += [('endpoint-' + str(i), 1) for i in range(1, 37)]
    expected_steps += [('source-' + str(i), 1) for i in SOURCE_CASES]
    expected_steps += [('runtime-default', 375)]
    require(len(state['steps']) == len(expected_steps), 'Missing main steps')
    for step, (name, count) in zip(state['steps'], expected_steps):
        require(step['id'] == name and step['naturalExit'] and step['exitCode'] == 0 and not step['failNames'], 'Main step failed: ' + name)
        require(sha(folder / 'run' / (name + '.log')) == step['logSha256'], 'Main log differs')
        if count is not None:
            require(len(step['passNames']) == count and len(set(step['passNames'])) == count, 'Main case count differs')
    require(state['steps'][0]['warnings'] == 0 and state['steps'][0]['errors'] == 0
            and state['steps'][1]['errors'] == 0, 'Main build scope differs')
    root_exit = read(folder / 'controls/main-root-exit.json')
    require(root_exit['controllerPid'] == state['controllerPid'] and root_exit['actualToolExitCode'] == 0
            and root_exit['resultSha256'] == sha(folder / 'run/result.json'), 'Main root exit is unconfirmed')
    manifest = read(folder / 'controls/payload-manifest.json')
    require(sha(folder / 'controls/payload-manifest.json') == state['payloadManifestSha256']
            and manifest['expectedInputs'] == state['inputsBefore'], 'Payload differs from validated main')
    application = read(folder / 'controls/application-record.json')
    require(application['applied'] and application['freshMainPassed'] is False
            and application['compilerInputsAfter'] == 922, 'Application history was rewritten')
    for name, digest in state['inputsBefore'].items():
        require(sha(folder / 'source' / name) == digest, 'Archived source differs: ' + name)
    for name, digest in state['controlsBefore'].items():
        require(sha(folder / 'controls' / Path(name).name) == digest, 'Archived control differs: ' + name)
    for name in SIX:
        for extension, digest in state['productionAfterSix'][name].items():
            require(digest == state['productionAfterTests'][name][extension]
                    == sha(folder / 'products' / name / (name + extension)), 'Production pair differs')
    for relative, digest in state['testOutputs'].items():
        require(sha(folder / 'test-outputs' / relative) == digest, 'Direct test output differs')
    for key, row in state['nativeFixtures'].items():
        require(sha(folder / 'fixtures' / key / Path(row['path']).name) == row['sha256'], 'Native fixture differs')
    candidate = read(folder / 'candidate/result.json')
    require(candidate['passed'] and candidate['frozenInputsUnchanged'] and candidate['candidateOverlayUnchanged']
            and candidate['frozenInputs'] == state['inputsBefore']
            and len(candidate['steps']) == 4 and all(s['naturalExit'] and s['exitCode'] == 0 for s in candidate['steps']), 'Candidate evidence differs')
    require(candidate['plannedCases'] == [11, 12] and not candidate['fullRendererSuiteRerun'], 'Candidate focused scope differs')
    for step in candidate['steps']:
        require(sha(folder / 'candidate' / (step['id'] + '.log')) == step['logSha256'], 'Candidate log differs')
    candidate_exit = read(folder / 'controls/shadow-root-exit.json')
    require(candidate_exit['actualToolExitCode'] == 0 and candidate_exit['controllerPid'] == candidate['controllerPid']
            and candidate_exit['resultSha256'] == sha(folder / 'candidate/result.json'), 'Candidate root exit differs')
    for attempt in ('compile-r1', 'tests-r1', 'tests-r2', 'tests-r3', 'tests-r4', 'tests-r5'):
        history_dir = folder / 'history' / attempt
        previous = read(history_dir / 'result.json')
        require(previous['originalInputs'] == candidate['originalInputs'], 'Predecessor base graph differs')
        for relative, digest in previous['frozenInputs'].items():
            if relative in previous['candidateOverlay']:
                artifact = history_dir / 'overlay' / relative
            else:
                saved = folder / 'base-backup' / relative
                artifact = saved if saved.exists() else folder / 'source' / relative
            require(sha(artifact) == digest, 'Predecessor source differs: ' + attempt + '/' + relative)
        for step in previous['steps']:
            if 'logSha256' in step:
                require(sha(history_dir / (step['id'] + '.log')) == step['logSha256'], 'Predecessor completed log differs')
        for original, digest in previous['controls'].items():
            name = Path(original).name
            choices = [history_dir / 'controls' / name, folder / 'controls' / name]
            if name == 'validate_candidate.py':
                choices.insert(0, history_dir / 'validate_candidate.original.py')
            require(any(path.exists() and sha(path) == digest for path in choices), 'Predecessor control differs')
    cancellation = read(folder / 'history/tests-r4/root-cancellation.json')
    require(cancellation['accepted'] is False and cancellation['interruptedChild']['naturalExit'] is False,
            'Interrupted predecessor was promoted to a natural pass')
    for flag in ('installed', 'gameStarted', 'completeSeekPermitted', 'sdkGoalComplete', 'realGameBodyAdmissionTested'):
        require(state[flag] is False, 'Scope flag was promoted: ' + flag)
    print(json.dumps(dict(archiveOnly=True, passed=True, files=len(actual), bytes=sum(r['bytes'] for r in actual.values()),
                          summarySha256=sha(folder / 'validation-summary.json')), ensure_ascii=False))


def publish():
    archive_parent = (ROOT / 'docs/validation').resolve()
    require(FINAL.resolve().parent == archive_parent, 'Archive destination escaped its workspace parent')
    require(not FINAL.exists(), 'Preserve existing published archives')
    stage = FINAL.with_name(FINAL.name + '.staging')
    require(stage.resolve().parent == archive_parent and stage != FINAL, 'Archive staging escaped its workspace parent')
    require(not stage.exists(), 'Preserve existing archive staging')
    main = CACHE / 'main-r1'
    state = read(main / 'result.json')
    require(state['passed'] and state['inputsUnchanged'], 'Main validation is incomplete')
    root_exit = read(WORK / 'main-root-exit.json')
    require(root_exit['actualToolExitCode'] == 0 and root_exit['resultSha256'] == sha(main / 'result.json'), 'Main root exit is unconfirmed')
    candidate = CACHE / 'tests-r6'
    require(read(candidate / 'result.json')['passed'], 'Candidate validation is incomplete')
    stage.mkdir(parents=True)

    def copy(source, relative, digest=None):
        source = Path(source)
        if digest is not None:
            require(sha(source) == digest, 'Original artifact changed: ' + str(source))
        destination = stage / relative
        require(not destination.exists(), 'Archive destination collision: ' + str(relative))
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, destination)
        require(sha(source) == sha(destination), 'Artifact changed during copy')

    for name, digest in state['inputsBefore'].items():
        copy(ROOT / name, Path('source') / name, digest)
    for path in main.iterdir():
        if path.is_file():
            copy(path, Path('run') / path.name)
    for original, digest in state['controlsBefore'].items():
        copy(main / 'controls' / Path(original).name, Path('controls') / Path(original).name, digest)
    for name in ('main-root-exit.json', 'publish_evidence.py', 'candidate-files-current.json',
                 'candidate-diff-current.patch', 'predecessor-root-observations.json'):
        copy(WORK / name, Path('controls') / name)
    for name in SIX:
        for extension, digest in state['productionAfterTests'][name].items():
            copy(main / 'artifacts/bin' / name / 'release' / (name + extension), Path('products') / name / (name + extension), digest)
    for relative, digest in state['testOutputs'].items():
        copy(main / relative, Path('test-outputs') / relative, digest)
    for key, row in state['nativeFixtures'].items():
        copy(row['path'], Path('fixtures') / key / Path(row['path']).name, row['sha256'])
    for path in candidate.iterdir():
        if path.is_file():
            copy(path, Path('candidate') / path.name)
    for path in (candidate / 'controls').iterdir():
        if path.is_file():
            copy(path, Path('candidate/controls') / path.name)
    for name in SIX:
        for extension in ('.dll', '.pdb'):
            source = candidate / 'artifacts/bin' / name / 'release' / (name + extension)
            copy(source, Path('candidate-products') / name / source.name)
    for path in (WORK / 'main-backup').rglob('*'):
        if path.is_file():
            copy(path, Path('base-backup') / path.relative_to(WORK / 'main-backup'))
    for attempt in ('compile-r1', 'tests-r1', 'tests-r2', 'tests-r3', 'tests-r4', 'tests-r5'):
        old = CACHE / attempt
        history = read(old / 'result.json')
        for path in old.iterdir():
            if path.is_file():
                copy(path, Path('history') / attempt / path.name)
        if (old / 'controls').exists():
            for path in (old / 'controls').iterdir():
                if path.is_file():
                    copy(path, Path('history') / attempt / 'controls' / path.name)
        for relative, digest in history['candidateOverlay'].items():
            copy(old / 'source' / relative, Path('history') / attempt / 'overlay' / relative, digest)
    meta = ROOT / 'work/line-renderer-next-20261114'
    require(sha(meta / 'observed-metadata.json') == 'cb34cfb3d5523a8b32773d878cd18241ea6aa872e83397b2ac95735be1c593fd', 'Fixed metadata changed')
    observed = read(meta / 'observed-metadata.json')
    copy(meta / 'observed-metadata.json', 'evidence/observed-metadata.json')
    for name, digest in observed['snippets'].items():
        copy(meta / name, Path('evidence') / name, digest)
    for row, key in ((observed['setPositions'], 'frozenAsm'), (observed['dynamicProducer'], 'text')):
        source = ROOT / row[key]
        copy(source, Path('evidence') / source.name, row.get('asmSha256', row['sha256']))
    for name in ('EasyPool.dump.txt', 'EasyPoolChild.dump.txt'):
        copy(WORK / name, Path('evidence') / name)
    files = inventory(stage)
    summary = dict(schema='renderer-consumer-evidence/v1', createdUtc=dt.datetime.now(dt.timezone.utc).isoformat(),
                   fileCountIncludingSummary=len(files) + 1, bytesExcludingSummary=sum(r['bytes'] for r in files.values()),
                   mainInputs=922, rendererCases=12, originalReaderCases=7, endpointWriterCases=36,
                   selectedSourceCases=SOURCE_CASES, defaultRuntimeCases=375,
                   actualGameBodyAdmissionTested=False, completeSeekPermitted=False, sdkGoalComplete=False,
                   files=files)
    (stage / 'validation-summary.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    check(stage)
    require(stage.resolve().parent == archive_parent and FINAL.resolve().parent == archive_parent,
            'Resolved archive rename paths changed')
    os.replace(stage, FINAL)
    check(FINAL)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--check-only', type=Path)
    options = parser.parse_args()
    if options.check_only is not None:
        check(options.check_only)
    else:
        publish()
