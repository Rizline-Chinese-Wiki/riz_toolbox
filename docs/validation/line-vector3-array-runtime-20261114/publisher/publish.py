"""Freeze and independently recheck the scoped 901-input Runtime array batch."""
import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import uuid

ROOT = Path('C:/Users/71957/Desktop/riz_toolbox')
WORK = ROOT / 'work/line-vector3-array-integration-20261113'
RUN = Path('D:/riz-build-cache-next/line-vector3-array-integration-20261113/main-r1')
BASIS = ROOT / 'docs/validation/line-dynamic-consumer-20261113'
ARCHIVE = ROOT / 'docs/validation/line-vector3-array-runtime-20261114'
SUPPLEMENT = RUN.parent / 'default-runtime-r1'
BASE_SHA = '4e56d42e620345ca89b0fad3bcf8df4eca99fd3854388ff352b2f8a8853fe367'
BASE_RESULT_SHA = '2546860ab24b607e24eaf9e3fc7d40ad89007d6d26291f12db08462aa9a9d494'
PAYLOAD_SHA = '21aff2c3fb487632dfabc3329ad83ee5b98b8b3b008f62682295db6e3d90a666'


def require(ok, why):
    if not ok:
        raise RuntimeError(why)


def sha(path):
    p = Path(path)
    require(p.is_file() and not p.is_symlink(), 'Missing or linked evidence: ' + str(p))
    return hashlib.sha256(p.read_bytes()).hexdigest()


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def encoded(value):
    return (json.dumps(value, ensure_ascii=False, indent=2) + '\n').encode('utf-8')


def key(path):
    # Archive lookups normalize recorded names without resolving original
    # filesystem paths; an archived recheck never probes the old C:/D: files.
    return os.path.normcase(os.path.normpath(str(path)))


class View:
    def __init__(self, archive=None):
        self.archive = archive
        self.index = read(archive / 'original-path-index.json') if archive else None

    def at(self, original):
        if self.archive is None:
            return Path(original)
        name = self.index[key(original)]
        require(not Path(name).is_absolute() and all(p not in ('..', '') for p in name.split('/')), 'Unsafe archived path')
        return self.archive / name

    def read(self, path):
        return read(self.at(path))

    def sha(self, path):
        return sha(self.at(path))


def names(path):
    text = Path(path).read_text(encoding='utf-8-sig')
    block = text.split('Cases =', 1)[1].split('};', 1)[0]
    found = re.findall(r'\("([^"\r\n]+)"', block)
    require(found and len(found) == len(set(found)), 'Case names absent or duplicate')
    return found


def audit(v, require_root=True):
    result = v.read(RUN / 'result.json')
    require(result['schema'] == 'line-vector3-array-main-validation/v1'
            and result['passed'] and result['controllerPid'] == 31208
            and result['compilerInputs'] == 901 and result['activeChildren'] == []
            and result['expectedFocusedPassedCases'] == 67 and result['expectedNaturalSteps'] == 12
            and all(result[k] is True for k in ('inputsUnchanged', 'controlsUnchanged', 'nativeFixturesUnchanged', 'compiledBytesUnchanged'))
            and all(result[k] is False for k in ('fullSource30ExecutedThisBatch', 'fullLegacy300ExecutedThisBatch',
                    'fullRuntimeDefaultExecutedThisBatch', 'sdkGoalComplete', 'completeSeekPermitted', 'gameStarted', 'installed')),
            'Current main result failed, changed scope or is not terminal')
    require(v.sha(BASIS / 'validation-summary.json') == BASE_SHA
            and v.sha(BASIS / 'run/result.json') == BASE_RESULT_SHA
            and v.sha(WORK / 'payload-manifest.json') == PAYLOAD_SHA, 'Immutable ancestor or exact payload differs')
    summary = v.read(BASIS / 'validation-summary.json')
    prior = v.read(BASIS / 'run/result.json')
    before = v.read(BASIS / 'run/inputs-after.json')
    inputs = v.read(RUN / 'inputs-after.json')
    manifest = v.read(WORK / 'payload-manifest.json')
    application = v.read(WORK / 'application-record.json')
    require(summary['acceptedThisBatch'] and summary['compilerInputs'] == len(before) == 898
            and summary['inputGraphSha256'] == v.sha(BASIS / 'run/inputs-after.json')
            and manifest['inputsBefore'] == before and manifest['inputsAfter'] == inputs
            and application['mainApplied'] and not application['freshMainPassed']
            and application['payloadManifestSha256'] == PAYLOAD_SHA,
            'Accepted 898 base or immutable application receipt differs')
    expected = dict(before)
    for row in manifest['files']:
        require(row['baseSha256'] == before.get(row['path'])
                and row['payloadSha256'] == v.sha(WORK / 'files' / row['path'])
                and row['payloadSha256'] == v.sha(row['sourcePath']), 'Staged payload or actual tested source differs')
        if row['baseSha256'] is not None:
            require(v.sha(WORK / 'before' / row['path']) == row['baseSha256'], 'Saved original differs')
        expected[row['path']] = row['payloadSha256']
    require(len(manifest['files']) == 4 and expected == inputs == v.read(RUN / 'inputs-before.json')
            and len(inputs) == 901 and len(set(inputs) - set(before)) == 3
            and all(v.sha(ROOT / p) == digest for p, digest in inputs.items()), 'Exact 901 compiler input graph differs')
    require(all(v.sha(p) == digest for p, digest in result['controlsBefore'].items()), 'Frozen control differs')
    handle = v.read(RUN / 'controller-handle-exit.json')
    require(handle['schema'] == 'original-controller-handle-exit/v1'
            and handle['controllerPid'] == 31208 and handle['actualExitCode'] == 0
            and handle['handleKeptUntilExit'] and handle['controllerScriptUnchanged']
            and handle['resultSha256AtExit'] == v.sha(RUN / 'result.json')
            and handle['controllerScriptSha256'] == v.sha(WORK / 'validate_main.py')
            and handle['observerSha256'] == v.sha(WORK / 'observe.py'), 'Original controller exit is not certified')
    rows = result['steps']
    sequence = ['six-release', 'solution', 'legacy-listing', 'vector', 'single', 'renderer-abi'] + [f'source-{i:02}' for i in range(25, 31)]
    old_steps = {row['name']: row for row in prior['steps']}
    expected_names = {'vector': names(v.at(ROOT / 'tests/RizToolbox.Runtime.Tests/Vector3ArrayPublicationRegression.cs')),
                      'single': names(v.at(ROOT / 'tests/RizToolbox.Runtime.Tests/SingleArrayPublicationRegression.cs')),
                      'renderer-abi': old_steps['renderer-abi']['passNames']}
    expected_names.update({f'source-{i:02}': old_steps[f'source-{i:02}']['passNames'] for i in range(25, 31)})
    require([row['name'] for row in rows] == sequence and len(rows) == 12
            and len(expected_names['vector']) == 7 and len(expected_names['single']) == 3
            and len(expected_names['renderer-abi']) == 51, 'Actual step or registry list differs')
    total = 0
    for row in rows:
        name = row['name']
        require(row['naturalExit'] and row['exitCode'] == 0 and row['matchesExpected'], 'Step did not naturally pass: ' + name)
        dotnet = str(Path('C:/Program Files/dotnet/dotnet.exe'))
        runtime = str(RUN / 'artifacts/bin/RizToolbox.Runtime.Tests/release/RizToolbox.Runtime.Tests.dll')
        judgement = str(RUN / 'artifacts/bin/RizToolbox.Judgements.Tests/release/RizToolbox.Judgements.Tests.dll')
        if name in ('six-release', 'solution'):
            project = ROOT / ('src/RizToolbox.Loader/RizToolbox.Loader.csproj' if name == 'six-release' else 'RizToolbox.sln')
            argv = [dotnet, 'build', str(project), '-c', 'Release', '--artifacts-path', str(RUN / 'artifacts'),
                    '--verbosity', 'minimal', '--disable-build-servers', '-m:1', '-p:UseSharedCompilation=false',
                    '-p:NuGetAudit=false', '-p:RestoreConfigFile=' + str(ROOT / 'work/ring-x-numeric-20261113/NuGet.Config')]
        elif name == 'legacy-listing':
            argv = [dotnet, judgement, '--list-legacy-cases']
        elif name.startswith('source-'):
            argv = [dotnet, judgement, '--line-ring-source-case', str(int(name[-2:]))]
        else:
            argv = [dotnet, runtime, {'vector': '--vector3-array-checks', 'single': '--single-array-checks', 'renderer-abi': '--renderer-abi-checks'}[name]]
        require(row['arguments'] == argv, 'Actual child command differs: ' + name)
        out, err = [v.at(RUN / (name + '.' + stream + '.log')) for stream in ('stdout', 'stderr')]
        require(sha(out) == row['stdoutSha256'] and sha(err) == row['stderrSha256'] and not err.read_bytes(), 'Original log differs: ' + name)
        body = out.read_text(encoding='utf-8-sig')
        passes = re.findall(r'^PASS (.+)$', body, re.M)
        runs = re.findall(r'^RUN (.+)$', body, re.M)
        if name in ('six-release', 'solution'):
            require(('已成功生成。' in body or 'Build succeeded.' in body)
                    and re.findall(r'^\s*(\d+) (?:个错误|Error\(s\))\s*$', body, re.M) == ['0']
                    and not passes and not runs, 'Build output failed')
            if name == 'six-release':
                require(re.findall(r'^\s*(\d+) (?:个警告|Warning\(s\))\s*$', body, re.M) == ['0'], 'Production warnings')
        elif name == 'legacy-listing':
            require(not passes and not runs and json.loads(body) == v.read(BASIS / 'run/legacy-case-list.json')
                    == v.read(RUN / 'legacy-case-list.json'), 'Legacy listing differs')
        else:
            wanted = expected_names[name]
            require(passes == row['passNames'] == wanted and row['runNames'] == runs
                    and (not runs if name == 'renderer-abi' else runs == passes)
                    and re.findall(r'(\d+)/(\d+) passed\.', body) == [(str(len(wanted)), str(len(wanted)))], 'Actual case names or totals differ')
            total += len(passes)
    require(total == 67, 'Scoped positive total differs')
    products = result['productsBeforeTests']
    require(result['productsAfterSix'] == products == result['productsAfterTests'] and len(products) == 6, 'Production bytes changed')
    for component, values in products.items():
        require(set(values) == {'.dll', '.pdb'}, 'Incomplete production pair')
        for ext, digest in values.items():
            require(v.sha(RUN / 'artifacts/bin' / component / 'release' / (component + ext)) == digest, 'Production binary differs')
    require(len(result['testPairs']) == 4 and len(result['dependencies']) == 12
            and all(v.sha(p) == digest for group in ('testPairs', 'dependencies') for p, digest in result[group].items()), 'Test pair or dependency differs')
    require(result['nativeFixtures'] == prior['nativeFixtures'] and len(result['nativeFixtures']) == 3
            and all(v.sha(row['path']) == row['sha256'] for row in result['nativeFixtures'].values()), 'Native fixture differs')
    golden = RUN / 'artifacts/bin/RizToolbox.Judgements.Tests/release/line-ring-numeric-goldens.json'
    require(v.sha(golden) == result['goldenSha256'] == inputs['tests/RizToolbox.Judgements.Tests/LineRingNumeric/single-rounding-goldens.json'], 'Executed golden differs')
    if require_root:
        root_exit = v.read(WORK / 'root-exit.json')
        require(root_exit['observedBy'] == 'root' and root_exit['observedRootExec']['actualExitCode'] == 0
                and root_exit['resultSha256'] == v.sha(RUN / 'result.json')
                and root_exit['handleExitSha256'] == v.sha(RUN / 'controller-handle-exit.json'), 'Independent root exit differs')
    return result, inputs, manifest


def audit_default(v):
    require(v.sha(SUPPLEMENT / 'result.json') == 'ca8f325a30c369fa6344e9d2cd79b284dd9e0702cb921bb7ad455cdaeae7f6e0'
            and v.sha(WORK / 'root-default-exit.json') == '38b7f987415700a03af4e1a022ec5239e8008389e3a43a32c4304814a22a3427', 'Supplemental Runtime evidence changed')
    result = v.read(SUPPLEMENT / 'result.json')
    observed = v.read(WORK / 'root-default-exit.json')
    row = result['step']
    body = v.at(SUPPLEMENT / 'default-runtime.stdout.log').read_text(encoding='utf-8-sig')
    passes = re.findall(r'^PASS (.+)$', body, re.M)
    require(result['passed'] and result['compilerInputs'] == 901 and result['testedBytesUnchanged']
            and row['arguments'] == [str(Path('C:/Program Files/dotnet/dotnet.exe')),
                str(RUN / 'artifacts/bin/RizToolbox.Runtime.Tests/release/RizToolbox.Runtime.Tests.dll')]
            and result['mainResultSha256'] == v.sha(RUN / 'result.json')
            and result['totals'] == [['356', '356']]
            and row['naturalExit'] and row['exitCode'] == 0 and row['matchesExpected']
            and passes == row['passNames'] and len(passes) == len(set(passes)) == 356
            and re.findall(r'Native runtime checks: (\d+)/(\d+) passed\.', body) == [('356', '356')]
            and row['stdoutSha256'] == v.sha(SUPPLEMENT / 'default-runtime.stdout.log')
            and row['stderrSha256'] == v.sha(SUPPLEMENT / 'default-runtime.stderr.log')
            and not v.at(SUPPLEMENT / 'default-runtime.stderr.log').read_bytes()
            and all(v.sha(p) == digest for p, digest in result['testAndDependencyBytes'].items())
            and observed['observedBy'] == 'root' and observed['actualRootToolExitCode'] == 0
            and observed['actualChildExitCode'] == 0 and observed['childNaturalExit']
            and observed['supplementResultSha256'] == v.sha(SUPPLEMENT / 'result.json')
            and observed['childPid'] == row['pid'], 'Default Runtime suite did not naturally pass on the same bytes')
    return result


def plan_live():
    result, inputs, payload = audit(View())
    audit_default(View())
    files = {}
    index = {}

    def add(original, relative):
        original = Path(original)
        require(original.is_absolute(), 'Original evidence path must be absolute')
        require(relative not in files and not Path(relative).is_absolute() and '..' not in Path(relative).parts, 'Unsafe or duplicate archive target')
        files[relative] = (original, sha(original), original.stat().st_size)
        index.setdefault(key(original), relative)

    for p in inputs:
        add(ROOT / p, 'compiler-inputs/' + p)
    for p in result['controlsBefore']:
        if key(p) not in index:
            add(p, 'controls/' + str(len(files)).zfill(4) + '-' + Path(p).name)
    for name in ('result.json', 'inputs-before.json', 'inputs-after.json', 'controller-handle-exit.json', 'legacy-case-list.json'):
        add(RUN / name, 'run/' + name)
    add(WORK / 'root-exit.json', 'run/root-exit.json')
    add(WORK / 'root-default-exit.json', 'supplement/root-default-exit.json')
    add(WORK / 'check_default_runtime.py', 'supplement/check_default_runtime.py')
    for name in ('result.json', 'default-runtime.stdout.log', 'default-runtime.stderr.log'):
        add(SUPPLEMENT / name, 'supplement/' + name)
    for row in result['steps']:
        for stream in ('stdout', 'stderr'):
            add(RUN / (row['name'] + '.' + stream + '.log'), 'run/logs/' + row['name'] + '.' + stream + '.log')
    for component, values in result['productsBeforeTests'].items():
        for ext in values:
            add(RUN / 'artifacts/bin' / component / 'release' / (component + ext), 'binaries/production/' + component + ext)
    for group in ('testPairs', 'dependencies'):
        for p in result[group]:
            add(p, 'binaries/' + group + '/' + Path(p).relative_to(RUN / 'artifacts/bin').as_posix())
    for component in ('RizToolbox.Runtime.Tests', 'RizToolbox.Judgements.Tests'):
        for ext in ('.deps.json', '.runtimeconfig.json'):
            add(RUN / 'artifacts/bin' / component / 'release' / (component + ext), 'binaries/replay/' + component + ext)
    for name, row in result['nativeFixtures'].items():
        add(row['path'], 'fixtures/' + name + '/' + Path(row['path']).name)
    add(RUN / 'artifacts/bin/RizToolbox.Judgements.Tests/release/line-ring-numeric-goldens.json', 'fixtures/line-ring-numeric-goldens.json')
    for p in (BASIS / 'validation-summary.json', BASIS / 'run/result.json', BASIS / 'run/inputs-after.json', BASIS / 'run/legacy-case-list.json', WORK / 'payload-manifest.json', WORK / 'application-record.json'):
        if key(p) not in index:
            add(p, 'ancestry/' + str(len(files)).zfill(4) + '-' + p.name)
    add(Path(__file__), 'publisher/publish.py')
    require(len(files) < 1500 and sum(v[2] for v in files.values()) < 320 * 1024 * 1024, 'Archive bound exceeded')
    return result, files, index


def check_archive(path):
    manifest = read(path / 'artifact-manifest.json')
    found = {p.relative_to(path).as_posix() for p in path.rglob('*') if p.is_file()}
    require(found == set(manifest) | {'artifact-manifest.json'} and len(found) < 1500
            and all(sha(path / p) == digest for p, digest in manifest.items()), 'Archive inventory or bytes differ')
    result, _, _ = audit(View(path))
    audit_default(View(path))
    summary = read(path / 'validation-summary.json')
    require(summary['acceptedThisBatch'] and summary['compilerInputs'] == 901
            and summary['naturalSteps'] == 12 and summary['focusedPassedCases'] == 67
            and summary['defaultRuntimeSupplementCases'] == 356 and summary['defaultRuntimeSupplementPassed']
            and summary['resultSha256'] == sha(View(path).at(RUN / 'result.json'))
            and summary['publisherSha256'] == sha(path / 'publisher/publish.py')
            and summary['sdkGoalComplete'] is False and summary['completeSeekPermitted'] is False, 'Summary scope differs')
    return {'acceptedThisBatch': True, 'compilerInputs': 901, 'naturalSteps': 12, 'focusedPassedCases': 67,
            'archiveFiles': len(found), 'archiveBytes': sum(p.stat().st_size for p in path.rglob('*') if p.is_file()),
            'summarySha256': sha(path / 'validation-summary.json'), 'sdkGoalComplete': False, 'completeSeekPermitted': False}


def main():
    parser = argparse.ArgumentParser()
    action = parser.add_mutually_exclusive_group(required=True)
    for name in ('record-exit', 'preflight', 'publish', 'check-only'):
        action.add_argument('--' + name, action='store_true')
    parser.add_argument('--archive', type=Path, default=ARCHIVE)
    args = parser.parse_args()
    if args.check_only:
        print(json.dumps(check_archive(args.archive.resolve())))
        return
    if args.record_exit:
        result, _, _ = audit(View(), require_root=False)
        record = {'observedBy': 'root', 'observedUtc': dt.datetime.now(dt.timezone.utc).isoformat(),
                  'controllerPid': 31208, 'resultSha256': sha(RUN / 'result.json'),
                  'handleExitSha256': sha(RUN / 'controller-handle-exit.json'),
                  'publisherSha256': sha(Path(__file__)),
                  'observedRootExec': {'actualExitCode': 0, 'observationSource': 'Original Windows process handle receipt read and checked by root'},
                  'sdkGoalComplete': False, 'completeSeekPermitted': False}
        with (WORK / 'root-exit.json').open('xb') as stream:
            stream.write(encoded(record))
        print(json.dumps(record))
        return
    result, files, index = plan_live()
    if args.preflight:
        print(json.dumps({'preflightPassed': True, 'plannedFiles': len(files), 'resultSha256': sha(RUN / 'result.json')}))
        return
    require(not ARCHIVE.exists(), 'Preserve previous archive')
    staging = ARCHIVE.parent / ('.' + ARCHIVE.name + '-' + uuid.uuid4().hex)
    require(staging.resolve().is_relative_to((ROOT / 'docs/validation').resolve()), 'Staging escaped evidence directory')
    staging.mkdir()
    for relative, (original, digest, size) in files.items():
        require(sha(original) == digest and original.stat().st_size == size, 'Source changed during publication')
        target = staging / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(original, target)
        require(sha(target) == digest, 'Copied evidence differs')
    (staging / 'original-path-index.json').write_bytes(encoded(index))
    summary = {'schema': 'line-vector3-array-main-evidence/v1', 'acceptedThisBatch': True,
               'acceptedAtUtc': dt.datetime.now(dt.timezone.utc).isoformat(), 'compilerInputs': 901,
               'naturalSteps': 12, 'focusedPassedCases': 67, 'vector3Cases': 7, 'singleCases': 3,
               'runtimeAbiCases': 51, 'dynamicConsumerCases': 6, 'legacyCasesListedOnly': 300,
               'sixReleaseWarnings': 0, 'buildErrors': 0, 'primaryFullRuntimeDefaultExecutedThisBatch': False,
               'defaultRuntimeSupplementCases': 356, 'defaultRuntimeSupplementPassed': True,
               'fullSource30ExecutedThisBatch': False, 'resultSha256': sha(RUN / 'result.json'),
               'handleExitSha256': sha(RUN / 'controller-handle-exit.json'), 'rootExitSha256': sha(WORK / 'root-exit.json'),
               'publisherSha256': sha(Path(__file__)), 'prior898SummarySha256': BASE_SHA,
               'sdkGoalComplete': False, 'completeSeekPermitted': False, 'gameStarted': False, 'installed': False}
    (staging / 'validation-summary.json').write_bytes(encoded(summary))
    manifest = {p.relative_to(staging).as_posix(): sha(p) for p in staging.rglob('*') if p.is_file()}
    (staging / 'artifact-manifest.json').write_bytes(encoded(manifest))
    check_archive(staging)
    require(not ARCHIVE.exists(), 'Archive appeared during staging')
    staging.rename(ARCHIVE)
    print(json.dumps(check_archive(ARCHIVE)))


if __name__ == '__main__':
    main()
