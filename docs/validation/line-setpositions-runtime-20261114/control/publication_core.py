"""Freeze this accepted offline slice and verify it without original C:/D: lookups."""
import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import shutil

HERE = Path(__file__).resolve().parent
ROOT = Path('C:/Users/71957/Desktop/riz_toolbox')
RUN = Path('D:/riz-build-cache-next/line-vector3-buffer-20261114/main-r1')
SHADOW = RUN.parent / 'shadow-r3'
ARCHIVE = ROOT / 'docs/validation/line-vector3-buffer-runtime-20261114'
MAIN_SHA = 'bd9867853e1228e020e4c1d60f1f0b11e2e8edbc3e2d31f05d1a09560ff06f99'
SHADOW_SHA = 'a5b53640be0dd3cdff565fe8caf75528203a65c18c9d9013ac330efca59b1124'
ROOT_EXIT_SHA = 'ac66a6d27cd6d431e1f24aa4de491b5ae4c1ecd7d2c746b402800ec9a9af9048'
README_SHA = 'cfbac4691d36edc5daba513aa62d5856ae61e29bf553d0dc2979832750847364'


def sha(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def save(path, value):
    Path(path).write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def require(ok, reason):
    if not ok:
        raise RuntimeError(reason)


def coverage(state, expected):
    require(state['passed'] and len(state['steps']) == len(expected), 'Successful natural run required')
    for row, (name, count) in zip(state['steps'], expected):
        require(row['id'] == name and row['naturalExit'] and row['exitCode'] == 0
                and not row['failNames'] and len(row['passNames']) == count
                and len(set(row['passNames'])) == count, 'Natural coverage differs: ' + name)


def basename(value):
    # String-only: archive checks never resolve original recorded C:/D: paths.
    return str(value).replace('\\', '/').rsplit('/', 1)[-1]


def plain_inventory(folder):
    # Check the root and every directory entry before descending. A reparse
    # point must never redirect archive verification to an outside file/tree.
    def reject_link(path):
        info = path.stat(follow_symlinks=False)
        require(not path.is_symlink() and not (getattr(info, 'st_file_attributes', 0) & 0x400),
                'Archive contains a symbolic link or reparse point: ' + str(path))
    reject_link(folder)
    pending, result = [folder], set()
    while pending:
        directory = pending.pop()
        with os.scandir(directory) as entries:
            for entry in entries:
                path = Path(entry.path)
                reject_link(path)
                if entry.is_dir(follow_symlinks=False):
                    pending.append(path)
                else:
                    require(entry.is_file(follow_symlinks=False), 'Archive contains a nonregular file')
                    if path != folder / 'artifact-manifest.json':
                        result.add(path.relative_to(folder).as_posix())
    return result


def check(folder):
    folder = Path(folder)
    actual = plain_inventory(folder)
    pins = read(folder / 'artifact-manifest.json')
    require(actual == set(pins), 'Archive inventory differs')
    require(all(sha(folder / name) == digest for name, digest in pins.items()), 'Archived bytes differ')
    summary = read(folder / 'validation-summary.json')
    state = read(folder / 'run/result.json')
    require(sha(folder / 'run/result.json') == MAIN_SHA == summary['resultSha256'], 'Main run pin differs')
    coverage(state, [('six-release', 0), ('solution', 0), ('buffer10', 10), ('default366', 366)])
    require(state['compilerInputs'] == 904 and state['inputsBefore'] == state['inputsAfter']
            and len(state['inputsAfter']) == 904 and state['inputsUnchanged'], 'Source graph differs')
    require(all(sha(folder / 'source' / name) == digest for name, digest in state['inputsAfter'].items()), 'Source snapshot differs')
    require(sha(folder / 'source/README.md') == README_SHA, 'User README snapshot differs')
    require(all(sha(folder / 'control' / basename(path)) == digest
                for path, digest in state['controlsBefore'].items()), 'Fixed controller bytes differ')
    require(all(sha(folder / 'binaries/runtime-tests' / basename(path)) == digest
                for path, digest in state['runtimeTestFiles'].items()), 'Fixed Runtime test files differ')
    for path, digest in state['nativeFixtures'].items():
        parts = path.replace('\\', '/').split('/')
        require(sha(folder / 'fixtures' / parts[-2] / parts[-1]) == digest, 'Fixed native fixture differs')
    require(state['productsAfterSix'] == state['productsBeforeTests'] == state['productsAfterTests'], 'Production pairs changed')
    require(state['steps'][0]['warnings'] == 0 and state['steps'][0]['errors'] == 0
            and state['steps'][1]['errors'] == 0, 'Fresh build gate differs')
    for row in state['steps']:
        log = folder / 'run' / (row['id'] + '.log')
        require(sha(log) == row['logSha256'], 'Main log differs')
        require(re.findall(r'^PASS (.+)$', log.read_text(encoding='utf-8-sig', errors='replace'), re.M)
                == row['passNames'], 'Actual main log case names differ')
    registry = (folder / 'source/tests/RizToolbox.Runtime.Tests/Vector3BufferRegression.cs').read_text(encoding='utf-8-sig')
    names = re.findall(r'\("([^"\r\n]+)"', registry.split('Cases =', 1)[1].split('};', 1)[0])
    require(len(names) == 10 and state['steps'][2]['passNames'] == names
            and set(names).issubset(state['steps'][3]['passNames']), 'New actual case registry differs')
    for name, pair in state['productsBeforeTests'].items():
        require(all(sha(folder / 'binaries/production' / (name + ext)) == digest for ext, digest in pair.items()), 'Production archive differs')
    for component in ('RizToolbox.Runtime', 'RizToolbox.Sdk', 'RizToolbox.Core', 'RizToolbox.Adapter'):
        require(all(sha(folder / 'binaries/runtime-tests' / (component + ext)) == digest
                    for ext, digest in state['productsBeforeTests'][component].items()), 'Test dependency is not the fresh production pair')
    witness = read(folder / 'run/root-exit.json')
    require(sha(folder / 'run/root-exit.json') == ROOT_EXIT_SHA == summary['rootExitSha256'], 'Root exit digest differs')
    require(sha(folder / 'control/publish.py') == summary['publisherSha256']
            and summary['shadowResultSha256'] == SHADOW_SHA, 'Publication/ancestry linkage differs')
    require(witness['actualRootSession'] == 35568 and witness['actualRootExitCode'] == 0
            and witness['toolChunk'] == 'b942f4' and witness['resultSha256'] == MAIN_SHA
            and witness['kernelHandleWitnessClaimed'] is False, 'Root actual-exit evidence differs')
    shadow = read(folder / 'shadow/result.json')
    require(sha(folder / 'shadow/result.json') == SHADOW_SHA, 'Shadow run differs')
    coverage(shadow, [('runtime-build', 0), ('buffer10', 10), ('vector7', 7), ('single3', 3), ('abi51', 51), ('default366', 366)])
    for row in shadow['steps']:
        log = folder / 'shadow' / (row['id'] + '.log')
        require(sha(log) == row['logSha256'] and re.findall(r'^PASS (.+)$',
                log.read_text(encoding='utf-8-sig', errors='replace'), re.M) == row['passNames'], 'Fixed shadow log differs')
    require(summary['acceptedThisBatch'] is True and summary['compilerInputs'] == 904
            and summary['focusedBufferCases'] == 10 and summary['defaultRuntimeCases'] == 366
            and summary['caseCountsOverlap'] and summary['sdkGoalComplete'] is False
            and summary['completeSeekPermitted'] is False and summary['gameStarted'] is False,
            'Batch boundaries differ')
    return dict(passed=True, files=len(pins), bytes=sum((folder / name).stat().st_size for name in pins),
                summarySha256=sha(folder / 'validation-summary.json'))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--preflight', action='store_true')
    parser.add_argument('--check-only', type=Path)
    opt = parser.parse_args()
    if opt.check_only is not None:
        print(json.dumps(check(opt.check_only)))
        return
    work = ROOT / 'work/line-vector3-buffer-root-20261114'
    require(sha(RUN / 'result.json') == MAIN_SHA and sha(SHADOW / 'result.json') == SHADOW_SHA, 'Fixed result changed')
    state, shadow = read(RUN / 'result.json'), read(SHADOW / 'result.json')
    coverage(state, [('six-release', 0), ('solution', 0), ('buffer10', 10), ('default366', 366)])
    coverage(shadow, [('runtime-build', 0), ('buffer10', 10), ('vector7', 7), ('single3', 3), ('abi51', 51), ('default366', 366)])
    witness = read(work / 'root-exit.json')
    require(sha(work / 'root-exit.json') == ROOT_EXIT_SHA and witness['actualRootExitCode'] == 0
            and witness['resultSha256'] == MAIN_SHA, 'Actual root completion required')
    sources = {str(ROOT / name): digest for name, digest in state['inputsAfter'].items()}
    require(all(sha(path) == digest for path, digest in {**sources, **state['controlsBefore'], **state['nativeFixtures'], **state['runtimeTestFiles']}.items()), 'Live frozen byte changed')
    require(all(sha(SHADOW / path) == digest for path, digest in shadow['binaries'].items()), 'Shadow binaries differ')
    if opt.preflight:
        print(json.dumps(dict(passed=True, mainInputs=904, focusedBufferCases=10, defaultRuntimeCases=366, caseCountsOverlap=True)))
        return
    staged = ARCHIVE.with_name(ARCHIVE.name + '.pending')
    require(not ARCHIVE.exists() and not staged.exists(), 'Never overwrite an earlier archive')
    staged.mkdir(parents=True)

    def copy(source, name, digest=None):
        target = staged / name
        target.parent.mkdir(parents=True, exist_ok=True)
        require(not target.exists(), 'Duplicate archive destination: ' + name)
        before = sha(source)
        require(digest is None or before == digest, 'Source pin differs: ' + name)
        shutil.copy2(source, target)
        require(sha(target) == before == sha(source), 'Copy bytes changed: ' + name)

    for name, digest in state['inputsAfter'].items():
        copy(ROOT / name, 'source/' + name, digest)
    copy(ROOT / 'README.md', 'source/README.md', README_SHA)
    copy(RUN / 'result.json', 'run/result.json', MAIN_SHA)
    copy(work / 'root-exit.json', 'run/root-exit.json')
    for row in state['steps']:
        copy(RUN / (row['id'] + '.log'), 'run/' + row['id'] + '.log', row['logSha256'])
    for name, pair in state['productsBeforeTests'].items():
        for ext, digest in pair.items():
            copy(RUN / 'artifacts/bin' / name / 'release' / (name + ext), 'binaries/production/' + name + ext, digest)
    for path, digest in state['runtimeTestFiles'].items():
        copy(path, 'binaries/runtime-tests/' + Path(path).name, digest)
    for path, digest in state['nativeFixtures'].items():
        source = Path(path)
        copy(source, 'fixtures/' + source.parent.name + '/' + source.name, digest)
    for name in ('validate_main.py', 'validate_shadow.py', 'apply_main.py', 'payload-manifest.json', 'application-record.json', 'publish.py'):
        copy(work / name, 'control/' + name)
    for source in (work / 'control-iterations').rglob('*'):
        if source.is_file():
            copy(source, 'control/control-iterations/' + source.relative_to(work / 'control-iterations').as_posix())
    review = ROOT / 'work/line-vector3-buffer-review-20261114'
    for source in review.iterdir():
        if source.is_file():
            copy(source, 'review/' + source.name)
    copy(SHADOW / 'result.json', 'shadow/result.json', SHADOW_SHA)
    for row in shadow['steps']:
        copy(SHADOW / (row['id'] + '.log'), 'shadow/' + row['id'] + '.log', row['logSha256'])
    copy(ROOT / 'docs/validation/line-vector3-array-runtime-20261114/validation-summary.json', 'ancestry/vector3-array-summary.json',
         '3fe60eef9ecabc489c2ec4e575c56212f59f9ab397e9b7c47e73c1d1cd188cc9')
    summary = dict(schema='line-vector3-buffer-main-evidence/v1', acceptedThisBatch=True,
                   acceptedAtUtc=dt.datetime.now(dt.timezone.utc).isoformat(), compilerInputs=904,
                   focusedBufferCases=10, defaultRuntimeCases=366, caseCountsOverlap=True,
                   naturalSteps=4, sixReleaseWarnings=0, solutionWarnings=state['steps'][1]['warnings'], buildErrors=0,
                   resultSha256=MAIN_SHA, rootExitSha256=sha(work / 'root-exit.json'), shadowResultSha256=SHADOW_SHA,
                   publisherSha256=sha(__file__), fullSdkMatrixExecutedThisBatch=False,
                   rendererSetPositionsImplemented=False, originalReaderIntegratedThisBatch=False,
                   sdkGoalComplete=False, completeSeekPermitted=False, installed=False, gameStarted=False)
    save(staged / 'validation-summary.json', summary)
    pins = {p.relative_to(staged).as_posix(): sha(p) for p in sorted(staged.rglob('*')) if p.is_file()}
    save(staged / 'artifact-manifest.json', pins)
    result = check(staged)
    os.replace(staged, ARCHIVE)
    print(json.dumps(result))


if __name__ == '__main__':
    main()
