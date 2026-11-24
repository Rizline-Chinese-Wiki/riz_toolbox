"""Freeze the original reader/owner batch after actual root-observed completion."""
import argparse
import datetime as dt
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import sys

HERE = Path(__file__).resolve().parent
ROOT = Path('C:/Users/71957/Desktop/riz_toolbox')
RUN = Path('D:/riz-build-cache-next/line-setpositions-20261114/main-r1')
SHADOW = Path('D:/riz-build-cache-next/line-setpositions-profile-20261114/root-r3')
ARCHIVE = ROOT / 'docs/validation/line-setpositions-runtime-20261114'
MAIN_SHA = 'f1efcca1703aad1491635ca373d07fa571aa222a9d3d23d253aa6f96570746f4'  # Filled only from the actual completed root run before publication.
ROOT_EXIT_SHA = '2fdc2e235e32c88ed982cc8ce2be768399c329e5a1058953a6460a3569d2587b'
SHADOW_SHA = '330f58feb3c890764219f7c053a9da21eb7f5de54a20a1dbe567260fcd0cb9a0'
BASE_SHA = '8df53d0819339010c4d49b2dacb54b38a386ad1a5f4ed86e35e2599be45eb469'
CORE_SHA = 'a76f022d12ddb2d21bd7e36a441653df726672f71545788e2bf6067c4603072d'
PAYLOAD_SHA = '8ceb7283aef196c82760b846b09db4e4f079bb24f214dc04fa2d604a53f25e82'


def load_core():
    import hashlib
    path = HERE / 'publication_core.py'
    info = path.stat(follow_symlinks=False)
    assert not path.is_symlink() and not (getattr(info, 'st_file_attributes', 0) & 0x400)
    assert hashlib.sha256(path.read_bytes()).hexdigest() == CORE_SHA
    spec = importlib.util.spec_from_file_location('reader_publication_helpers', path)
    module = importlib.util.module_from_spec(spec)
    previous = sys.dont_write_bytecode
    sys.dont_write_bytecode = True
    try:
        spec.loader.exec_module(module)
    finally:
        sys.dont_write_bytecode = previous
    return module


core = load_core()
sha, read, save, require = core.sha, core.read, core.save, core.require


def expected_steps():
    return [('six-release', 0), ('solution', 0), ('setpositions9', 9), ('default375', 375), ('reader7', 7)]


def case_names(path, registry, count):
    block = Path(path).read_text(encoding='utf-8-sig').split(registry + ' =', 1)[1].split('};', 1)[0]
    names = re.findall(r'\("([^"\r\n]+)"', block)
    require(len(names) == count and len(set(names)) == count, 'Actual source registry differs')
    return names


def check(folder):
    require(len(MAIN_SHA) == len(ROOT_EXIT_SHA) == 64, 'Actual root completion pins must be frozen first')
    folder = Path(folder)
    inventory = core.plain_inventory(folder)
    pins = read(folder / 'artifact-manifest.json')
    require(inventory == set(pins) and all(sha(folder / n) == v for n, v in pins.items()), 'Archive inventory/bytes differ')
    state = read(folder / 'run/result.json')
    summary = read(folder / 'validation-summary.json')
    require(sha(folder / 'run/result.json') == MAIN_SHA == summary['resultSha256'], 'Fixed main result differs')
    core.coverage(state, expected_steps())
    require(state['compilerInputs'] == 913 and state['inputsBefore'] == state['inputsAfter']
            and len(state['inputsAfter']) == 913 and state['inputsUnchanged'], 'Frozen main graph differs')
    require(all(sha(folder / 'source' / n) == v for n, v in state['inputsAfter'].items()), 'Actual main source differs')
    require(sha(folder / 'source/README.md') == core.README_SHA, 'User README differs')
    for path, digest in state['controlsBefore'].items():
        require(sha(folder / 'control' / core.basename(path)) == digest, 'Fixed main control differs')
    require(sha(folder / 'control/payload-manifest-v4.json') == PAYLOAD_SHA, 'Payload manifest pin differs')
    for key, target in (('runtimeTestFiles', 'runtime-tests'), ('judgementTestFiles', 'judgement-tests')):
        for path, digest in state[key].items():
            require(sha(folder / 'binaries' / target / core.basename(path)) == digest, 'Fixed test/dependency bytes differ')
        for component in ('RizToolbox.Sdk', 'RizToolbox.Core', 'RizToolbox.Runtime', 'RizToolbox.Adapter'):
            for ext, digest in state['productsBeforeTests'][component].items():
                require(sha(folder / 'binaries' / target / (component + ext)) == digest, 'Test dependency is not the fresh production pair')
    for path, digest in state['nativeFixtures'].items():
        parts = path.replace('\\', '/').split('/')
        require(sha(folder / 'fixtures' / parts[-2] / parts[-1]) == digest, 'Native fixture differs')
    require(state['productsAfterSix'] == state['productsBeforeTests'] == state['productsAfterTests'], 'Production pairs changed')
    for component, pair in state['productsBeforeTests'].items():
        for ext, digest in pair.items():
            require(sha(folder / 'binaries/production' / (component + ext)) == digest, 'Production archive differs')
    require(state['steps'][0]['warnings'] == state['steps'][0]['errors'] == state['steps'][1]['errors'] == 0, 'Build gates differ')
    for row in state['steps']:
        log = folder / 'run' / (row['id'] + '.log')
        require(sha(log) == row['logSha256'] and re.findall(r'^PASS (.+)$', log.read_text(encoding='utf-8-sig', errors='replace'), re.M)
                == row['passNames'], 'Actual log case names differ')
    runtime = folder / 'source/tests/RizToolbox.Runtime.Tests'
    reader = folder / 'source/tests/RizToolbox.Judgements.Tests/LineRingSource'
    selected = case_names(runtime / 'LineSetPositionsRegression.cs', 'LineSetPositionsCases', 9)
    readers = case_names(reader / 'NativeSeekLineRingSourceChecks.LineBuffer.cs', 'LineBufferCases', 7)
    require(state['steps'][2]['passNames'] == selected and state['steps'][4]['passNames'] == readers
            and set(selected).issubset(state['steps'][3]['passNames']), 'Actual new case registries differ')
    require(sha(folder / 'run/root-exit.json') == ROOT_EXIT_SHA == summary['rootExitSha256'], 'Root completion digest differs')
    witness = read(folder / 'run/root-exit.json')
    require(witness['actualRootSession'] == 45588 and witness['actualRootExitCode'] == 0
            and witness['resultSha256'] == MAIN_SHA and witness['kernelHandleWitnessClaimed'] is False, 'Actual root terminal differs')
    require(sha(folder / 'control/publish.py') == summary['publisherSha256']
            and sha(folder / 'control/publication_core.py') == CORE_SHA, 'Publisher linkage differs')
    upstream = read(folder / 'shadow/result.json')
    require(sha(folder / 'shadow/result.json') == SHADOW_SHA and upstream['passed']
            and upstream['steps'][0]['naturalExit'] and upstream['steps'][1]['naturalExit']
            and upstream['steps'][0]['exitCode'] == upstream['steps'][1]['exitCode'] == 0, 'Original v4 shadow differs')
    for row in upstream['steps']:
        require(sha(folder / 'shadow' / (row['name'] + '.log')) == row['logSha256'], 'Shadow log differs')
    candidate_path = folder / 'shadow/candidate-root-v4.json'
    require(sha(candidate_path) == '55e3a377bd4c92c8fa26baa432e3ae07c0abaccac4a12f74bc5252ce794873fb'
            == upstream['candidateSha256'], 'Actual shadow candidate differs')
    shadow_inputs = read(folder / 'shadow/inputs-before.json')
    for name, digest in read(candidate_path)['files'].items():
        require(shadow_inputs[name] == digest == sha(folder / 'shadow/payload' / name)
                == sha(folder / 'source' / name), 'Shadow/actual main payload differs')
    require(sha(folder / 'ancestry/main909-summary.json') == BASE_SHA, 'Accepted ancestry differs')
    require(sha(folder / 'static/observed-metadata.json') == 'cb34cfb3d5523a8b32773d878cd18241ea6aa872e83397b2ac95735be1c593fd', 'Frozen static metadata differs')
    metadata = read(folder / 'static/observed-metadata.json')
    require(metadata['fields']['InLevel.LineControl.linePositions'] == {'offset': '0x78', 'type': 'UnityEngine.Vector3[]'}
            and all(sha(folder / 'static' / n) == v for n, v in metadata['snippets'].items()), 'Exact scratch metadata/snippets differ')
    require(metadata['setPositions']['rva'] == '0x3fb160' and metadata['setPositions']['length'] == 288
            and metadata['setPositions']['sha256'] == 'e948e88fdbba2a25281ae2fb8ced224a3b9197915714dfef210fdc19e86fb8e6'
            and sha(folder / 'static/SetPositions-buffer.asm.txt') == metadata['setPositions']['asmSha256'],
            'Exact two-argument wrapper static evidence differs')
    require(summary['acceptedThisBatch'] and summary['compilerInputs'] == 913
            and summary['setPositionsCases'] == 9 and summary['nativeSeamCases'] == 8
            and summary['managedProfileGateCases'] == 1 and summary['defaultRuntimeCases'] == 375
            and summary['originalReaderCases'] == 7 and summary['caseCountsOverlap'] is True
            and summary['sdkGoalComplete'] is False and summary['completeSeekPermitted'] is False
            and summary['gameStarted'] is False and summary['realProfileBodyAdmissionVerified'] is False,
            'Acceptance scope differs')
    return dict(passed=True, files=len(pins), bytes=sum((folder / n).stat().st_size for n in pins),
                summarySha256=sha(folder / 'validation-summary.json'))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--preflight', action='store_true')
    parser.add_argument('--check-only', type=Path)
    opt = parser.parse_args()
    if opt.check_only is not None:
        print(json.dumps(check(opt.check_only)))
        return
    require(len(MAIN_SHA) == len(ROOT_EXIT_SHA) == 64 and sha(RUN / 'result.json') == MAIN_SHA, 'Run not frozen at actual completion')
    work = ROOT / 'work/line-setpositions-integration-20261114'
    state = read(RUN / 'result.json')
    core.coverage(state, expected_steps())
    require(sha(work / 'root-exit.json') == ROOT_EXIT_SHA and sha(SHADOW / 'result.json') == SHADOW_SHA, 'Root/upstream pin differs')
    require(all(sha(ROOT / n) == v for n, v in state['inputsAfter'].items()), 'Current source changed')
    for key in ('controlsBefore', 'runtimeTestFiles', 'judgementTestFiles', 'nativeFixtures'):
        require(all(sha(p) == v for p, v in state[key].items()), 'Frozen control/test bytes changed')
    if opt.preflight:
        print(json.dumps(dict(passed=True, compilerInputs=913, naturalSteps=5)))
        return
    staged = ARCHIVE.with_name(ARCHIVE.name + '.pending')
    require(not staged.exists() and not ARCHIVE.exists(), 'Never overwrite old evidence')
    staged.mkdir(parents=True)

    def copy(path, name, expected=None):
        target = staged / name
        target.parent.mkdir(parents=True, exist_ok=True)
        require(not target.exists(), 'Duplicate archive destination: ' + name)
        before = sha(path)
        require(expected is None or before == expected, 'Fixed copy source differs')
        shutil.copy2(path, target)
        require(sha(target) == before == sha(path), 'Copy changed')

    for name, digest in state['inputsAfter'].items():
        copy(ROOT / name, 'source/' + name, digest)
    copy(ROOT / 'README.md', 'source/README.md', core.README_SHA)
    copy(RUN / 'result.json', 'run/result.json', MAIN_SHA)
    copy(work / 'root-exit.json', 'run/root-exit.json', ROOT_EXIT_SHA)
    for row in state['steps']:
        copy(RUN / (row['id'] + '.log'), 'run/' + row['id'] + '.log', row['logSha256'])
    for component, pair in state['productsBeforeTests'].items():
        for ext, digest in pair.items():
            copy(RUN / 'artifacts/bin' / component / 'release' / (component + ext), 'binaries/production/' + component + ext, digest)
    for key, target in (('runtimeTestFiles', 'runtime-tests'), ('judgementTestFiles', 'judgement-tests')):
        for path, digest in state[key].items():
            copy(path, 'binaries/' + target + '/' + core.basename(path), digest)
    for path, digest in state['nativeFixtures'].items():
        p = Path(path)
        copy(p, 'fixtures/' + p.parent.name + '/' + p.name, digest)
    for path, digest in state['controlsBefore'].items():
        copy(path, 'control/' + core.basename(path), digest)
    for name in ('publish.py', 'publication_core.py'):
        copy(work / name, 'control/' + name)
    for path in (work / 'control-iterations').rglob('*'):
        if path.is_file():
            copy(path, 'control/iterations/' + path.relative_to(work / 'control-iterations').as_posix())
    for name in ('result.json', 'inputs-before.json', 'build.log', 'setpositions9.log'):
        copy(SHADOW / name, 'shadow/' + name)
    candidate = ROOT / 'work/line-setpositions-profile-next-20261114'
    copy(candidate / 'candidate-root-v4.json', 'shadow/candidate-root-v4.json',
         '55e3a377bd4c92c8fa26baa432e3ae07c0abaccac4a12f74bc5252ce794873fb')
    copy(candidate / 'root-shadow-exit-v4.json', 'shadow/root-shadow-exit-v4.json')
    for name, digest in read(candidate / 'candidate-root-v4.json')['files'].items():
        copy(candidate / name, 'shadow/payload/' + name, digest)
    old = candidate / 'iteration/root-v3-tested'
    for path in old.rglob('*'):
        if path.is_file():
            copy(path, 'upstream-v3/' + path.relative_to(old).as_posix())
    copy(ROOT / 'docs/validation/line-vector3-buffer-reader-20261114/validation-summary.json', 'ancestry/main909-summary.json', BASE_SHA)
    static = ROOT / 'work/line-renderer-next-20261114'
    copy(static / 'observed-metadata.json', 'static/observed-metadata.json',
         'cb34cfb3d5523a8b32773d878cd18241ea6aa872e83397b2ac95735be1c593fd')
    for name, digest in read(static / 'observed-metadata.json')['snippets'].items():
        copy(static / name, 'static/' + name, digest)
    method = read(static / 'observed-metadata.json')['setPositions']
    copy(ROOT / method['frozenAsm'], 'static/SetPositions-buffer.asm.txt', method['asmSha256'])
    summary = dict(schema='line-setpositions-main-evidence/v1', acceptedThisBatch=True,
                   acceptedAtUtc=dt.datetime.now(dt.timezone.utc).isoformat(), compilerInputs=913,
                   naturalSteps=5, setPositionsCases=9, nativeSeamCases=8, managedProfileGateCases=1,
                   defaultRuntimeCases=375, originalReaderCases=7, caseCountsOverlap=True,
                   fullSource30ExecutedThisBatch=False, fullSdkMatrixExecutedThisBatch=False,
                   sixReleaseWarnings=0, solutionWarnings=state['steps'][1]['warnings'], buildErrors=0,
                   resultSha256=MAIN_SHA, rootExitSha256=ROOT_EXIT_SHA, publisherSha256=sha(__file__),
                   setPositionsEntryIntegrated=True, rendererWriterIntegrated=False,
                   realProfileBodyAdmissionVerified=False, sdkGoalComplete=False,
                   completeSeekPermitted=False, installed=False, gameStarted=False)
    save(staged / 'validation-summary.json', summary)
    save(staged / 'artifact-manifest.json', {p.relative_to(staged).as_posix(): sha(p) for p in staged.rglob('*') if p.is_file()})
    result = check(staged)
    os.replace(staged, ARCHIVE)
    print(json.dumps(result))


if __name__ == '__main__':
    main()
