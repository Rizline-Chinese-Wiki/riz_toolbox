"""Fresh six-component/solution builds and Runtime tests of the applied main graph."""
import datetime as dt
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).resolve().parent
OUT = Path('D:/riz-build-cache-next/line-vector3-buffer-reader-20261114/main-r1')
DOTNET = Path('C:/Program Files/dotnet/dotnet.exe')
ACCEPTED = ROOT / 'docs/validation/line-vector3-array-runtime-20261114'
MANIFEST_SHA = '3cb6ddc58aa0988f4c49c35fc7ef54db6f0677f8ed62fdb40a8a8acd028fc5b7'
SIX = ['RizToolbox.' + name for name in ('Sdk', 'Core', 'Runtime', 'Adapter', 'Hosting', 'Loader')]


def sha(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def save(path, value):
    staged = Path(str(path) + '.pending')
    staged.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    os.replace(staged, path)


def require(ok, message):
    if not ok:
        raise RuntimeError(message)


def scanner():
    path = ROOT / 'work/line-ring-source-validation-20261113/run_validation.py'
    require(sha(path) == '61c2983ae117322b25ff96ad75b659b870fdadf9aada9274727248cd5ca98fd4', 'Scanner pin differs')
    spec = importlib.util.spec_from_file_location('buffer_main_scanner', path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def main():
    require(not OUT.exists(), 'Preserve previous main runs')
    require(sha(WORK / 'payload-manifest.json') == MANIFEST_SHA, 'Payload manifest differs')
    manifest = read(WORK / 'payload-manifest.json')
    expected = dict(manifest['expectedInputs'])
    for row in manifest['files']:
        if row.get('compilerInput', True):
            expected[row['target']] = row['sha256']
    scan = scanner()
    require(len(expected) == 909 and scan.inputs() == expected, 'Applied main graph differs')
    application = read(WORK / 'application-record.json')
    require(application['freshMainPassed'] is False and application['compilerInputsAfter'] == 909
            and application['payloadManifestSha256'] == MANIFEST_SHA, 'Application history differs')
    controls = {str(path): sha(path) for path in (Path(__file__), WORK / 'prepare.py', WORK / 'root-review-r2.md', WORK / 'shadow-root-exit.json', ROOT / 'scripts/Build.ps1',
                 WORK / 'apply.py', WORK / 'application-record.json', WORK / 'payload-manifest.json')}
    OUT.mkdir(parents=True)
    artifacts = OUT / 'artifacts'
    config = OUT / 'NuGet.Config'
    config.write_text('<?xml version="1.0"?><configuration><packageSources><clear /></packageSources></configuration>', encoding='utf-8')
    env = os.environ.copy()
    env.update(DOTNET_GENERATE_ASPNET_CERTIFICATE='false', DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false',
               DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1', DOTNET_NOLOGO='1')
    fixtures = {}
    for key, row in read(ACCEPTED / 'run/result.json')['nativeFixtures'].items():
        path = ACCEPTED / 'fixtures' / key / Path(row['path']).name
        require(sha(path) == row['sha256'], 'Archived fixture differs')
        env[key] = str(path)
        fixtures[str(path)] = row['sha256']
    state = dict(schema='line-buffer-reader-main-validation/v1', controllerPid=os.getpid(),
                 startedUtc=dt.datetime.now(dt.timezone.utc).isoformat(), passed=False,
                 compilerInputs=909, inputsBefore=expected, controlsBefore=controls, nativeFixtures=fixtures,
                 payloadManifestSha256=MANIFEST_SHA, steps=[], installed=False, gameStarted=False,
                 sdkGoalComplete=False, completeSeekPermitted=False,
                 focusedReaderCases=7, adjacentWriterCases=36, defaultRuntimeCases=366, caseCountsOverlap=False)
    save(OUT / 'result.json', state)
    print('controllerPid=' + str(os.getpid()), flush=True)

    def stable():
        return scan.inputs() == expected and all(sha(path) == digest for path, digest in controls.items())

    def run(name, argv, count=None):
        require(stable(), 'Source/control changed before ' + name)
        row = dict(id=name, argv=[str(value) for value in argv], naturalExit=False,
                   startedUtc=dt.datetime.now(dt.timezone.utc).isoformat())
        state['steps'].append(row)
        save(OUT / 'result.json', state)
        log = OUT / (name + '.log')
        with log.open('wb') as stream:
            process = subprocess.Popen(row['argv'], cwd=ROOT, env=env, stdout=stream, stderr=subprocess.STDOUT)
            row['pid'] = process.pid
            save(OUT / 'result.json', state)
            row['exitCode'] = process.wait(timeout=1800)
            row['naturalExit'] = True
        body = log.read_text(encoding='utf-8-sig', errors='replace')
        row.update(logSha256=sha(log), passNames=re.findall(r'^PASS (.+)$', body, re.M),
                   failNames=re.findall(r'^FAIL (.+)$', body, re.M),
                   finishedUtc=dt.datetime.now(dt.timezone.utc).isoformat())
        if count is None:
            totals = re.findall(r'\b(\d+) Warning\(s\)\s+(\d+) Error\(s\)', body)
            if not totals:
                totals = re.findall(r'(\d+)\s+个警告\s+(\d+)\s+个错误', body)
            require(len(totals) == 1 and totals[0][1] == '0', 'Build error summary differs')
            row['warnings'], row['errors'] = map(int, totals[0])
            if name == 'six-release':
                require(row['warnings'] == 0, 'Six Release build has warnings')
        else:
            require(len(row['passNames']) == count and len(set(row['passNames'])) == count,
                    'Actual case coverage differs: ' + name)
        require(row['exitCode'] == 0 and not row['failNames'] and stable(), 'Child/source failed: ' + name)
        save(OUT / 'result.json', state)
        print(name + ': exit0, ' + str(len(row['passNames'])) + ' checks', flush=True)

    def products():
        return {name: {ext: sha(artifacts / 'bin' / name / 'release' / (name + ext))
                       for ext in ('.dll', '.pdb')} for name in SIX}

    try:
        for name, project in (('six-release', ROOT / 'src/RizToolbox.Loader/RizToolbox.Loader.csproj'),
                              ('solution', ROOT / 'RizToolbox.sln')):
            run(name, [DOTNET, 'build', project, '-c', 'Release', '--artifacts-path', artifacts,
                       '--verbosity', 'minimal', '--disable-build-servers', '-m:1',
                       '-p:UseSharedCompilation=false', '-p:NuGetAudit=false',
                       '-p:RestoreConfigFile=' + str(config)])
            if name == 'six-release':
                state['productsAfterSix'] = products()
        state['productsBeforeTests'] = products()
        require(state['productsAfterSix'] == state['productsBeforeTests'], 'Solution changed production bytes')
        assembly = artifacts / 'bin/RizToolbox.Runtime.Tests/release/RizToolbox.Runtime.Tests.dll'
        state['runtimeTestFiles'] = {str(path): sha(path) for path in sorted(assembly.parent.iterdir()) if path.is_file()}
        judgement = artifacts / 'bin/RizToolbox.Judgements.Tests/release/RizToolbox.Judgements.Tests.dll'
        state['judgementTestFiles'] = {str(path): sha(path) for path in sorted(judgement.parent.iterdir()) if path.is_file()}
        run('reader7', [DOTNET, judgement, '--line-ring-buffer-checks'], 7)
        for case in range(1, 37):
            run('writer' + str(case).zfill(2), [DOTNET, judgement, '--line-ring-endpoint-writer-case', case], 1)
        selected_source = (1, 2, 4, 8, 9, 12, 13, 14, 15, 16, 20, 23, 24, 25, 26, 27, 28, 29, 30)
        state['sourceCasesThisBatch'] = list(selected_source)
        state['fullSource30ExecutedThisBatch'] = False
        for case in selected_source:
            run('source' + str(case).zfill(2), [DOTNET, judgement, '--line-ring-source-case', case], 1)
        run('default366', [DOTNET, assembly], 366)
        state['productsAfterTests'] = products()
        require(state['productsAfterTests'] == state['productsBeforeTests']
                and all(sha(path) == digest for path, digest in {**state['runtimeTestFiles'], **state['judgementTestFiles']}.items()), 'Test bytes changed')
        state['passed'] = stable()
    except Exception as error:
        state['failure'] = repr(error)
    state['inputsAfter'] = scan.inputs()
    state['inputsUnchanged'] = state['inputsAfter'] == expected
    state['passed'] = state['passed'] and state['inputsUnchanged']
    state['finishedUtc'] = dt.datetime.now(dt.timezone.utc).isoformat()
    save(OUT / 'result.json', state)
    print(json.dumps(dict(passed=state['passed'], resultSha256=sha(OUT / 'result.json'), failure=state.get('failure'))), flush=True)
    return 0 if state['passed'] else 1


if __name__ == '__main__':
    sys.exit(main())
