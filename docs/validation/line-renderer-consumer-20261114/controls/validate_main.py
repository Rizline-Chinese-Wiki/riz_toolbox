"""Fresh builds and affected regression scopes of the exact applied main graph."""
import argparse
import datetime as dt
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import time

ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).resolve().parent
OUT = Path('D:/riz-build-cache-next/line-renderer-consumer-20261114/main-r1')
DOTNET = Path('C:/Program Files/dotnet/dotnet.exe')
SIX = ['RizToolbox.' + name for name in ('Sdk', 'Core', 'Runtime', 'Adapter', 'Hosting', 'Loader')]
SOURCE_CASES = [13, 14, 25, 26, 27, 28, 29, 30]


def sha(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def save(path, value):
    temporary = Path(str(path) + '.pending')
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    os.replace(temporary, path)


def require(ok, reason):
    if not ok:
        raise RuntimeError(reason)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--payload-sha256', required=True)
    args = parser.parse_args()
    require(not OUT.exists(), 'Preserve previous main validation')
    manifest_path = WORK / 'payload-manifest.json'
    require(sha(manifest_path) == args.payload_sha256, 'Payload pin differs')
    manifest = read(manifest_path)
    expected = manifest['expectedInputs']
    require(len(expected) == 922, 'Unexpected main scope')
    application = read(WORK / 'application-record.json')
    require(application['applied'] and application['freshMainPassed'] is False
            and application['compilerInputsAfter'] == 922
            and application['payloadManifestSha256'] == args.payload_sha256, 'Application history differs')
    scanner_path = ROOT / 'work/line-ring-source-validation-20261113/run_validation.py'
    require(sha(scanner_path) == '61c2983ae117322b25ff96ad75b659b870fdadf9aada9274727248cd5ca98fd4', 'Scanner pin differs')
    spec = importlib.util.spec_from_file_location('renderer_main_scanner', scanner_path)
    scanner = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(scanner)
    require(scanner.inputs() == expected, 'Applied main graph differs')
    controls = {str(path): sha(path) for path in (Path(__file__), scanner_path, WORK / 'prepare_payload.py',
        WORK / 'apply_payload.py', WORK / 'application-record.json', manifest_path,
        WORK / 'shadow-root-exit.json', WORK / 'source-review.md', ROOT / 'scripts/Build.ps1')}
    for row in manifest['files']:
        require(sha(ROOT / row['target']) == row['sha256'], 'Applied payload differs')
    require(sha(ROOT / 'README.md') == manifest['readmeSha256'], 'Preserve user README')
    OUT.mkdir(parents=True)
    control_dir = OUT / 'controls'
    control_dir.mkdir()
    for path in controls:
        source = Path(path)
        destination = control_dir / source.name
        require(not destination.exists(), 'Control filename collision')
        shutil.copyfile(source, destination)
    config = OUT / 'NuGet.Config'
    config.write_text('<?xml version="1.0"?><configuration><packageSources><clear /></packageSources></configuration>', encoding='utf-8')
    artifacts = OUT / 'artifacts'
    env = os.environ.copy()
    env.update(DOTNET_GENERATE_ASPNET_CERTIFICATE='false', DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false',
               DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1', DOTNET_NOLOGO='1')
    archive = ROOT / 'docs/validation/line-vector3-array-runtime-20261114'
    fixtures = {}
    for key, row in read(archive / 'run/result.json')['nativeFixtures'].items():
        path = archive / 'fixtures' / key / Path(row['path']).name
        require(sha(path) == row['sha256'], 'Native fixture changed')
        env[key] = str(path)
        fixtures[key] = dict(path=str(path), sha256=row['sha256'])
    state = dict(schema='renderer-consumer-main-validation/v1', controllerPid=os.getpid(),
        startedUtc=dt.datetime.now(dt.timezone.utc).isoformat(), passed=False,
        compilerInputs=922, inputsBefore=expected, controlsBefore=controls, nativeFixtures=fixtures,
        payloadManifestSha256=args.payload_sha256, steps=[], rendererCases=12, readerCases=7,
        endpointWriterCases=36, selectedSourceCases=SOURCE_CASES, defaultRuntimeCases=375,
        rendererProcessConcurrency=2,
        fullSourceSuiteRerun=False, fullSdkMatrixRerun=False, installed=False, gameStarted=False,
        realGameBodyAdmissionTested=False, completeSeekPermitted=False, sdkGoalComplete=False)
    state['availableCommitBeforeBuild'] = scanner.available_commit()
    state['rendererProcessConcurrency'] = 2 if state['availableCommitBeforeBuild'] >= 4 * 1024 ** 3 else 1
    save(OUT / 'result.json', state)
    print('controllerPid=' + str(os.getpid()), flush=True)

    def stable():
        return scanner.inputs() == expected and all(sha(path) == digest for path, digest in controls.items())

    def products():
        return {name: {ext: sha(artifacts / 'bin' / name / 'release' / (name + ext))
                       for ext in ('.dll', '.pdb')} for name in SIX}

    def run(name, argv, count=None):
        require(stable(), 'Main input/control changed before ' + name)
        row = dict(id=name, argv=[str(value) for value in argv], naturalExit=False,
                   startedUtc=dt.datetime.now(dt.timezone.utc).isoformat())
        state['steps'].append(row)
        save(OUT / 'result.json', state)
        log = OUT / (name + '.log')
        with log.open('wb') as stream:
            child = subprocess.Popen(row['argv'], cwd=ROOT, env=env, stdout=stream, stderr=subprocess.STDOUT)
            row['pid'] = child.pid
            save(OUT / 'result.json', state)
            row['exitCode'] = child.wait()
            row['naturalExit'] = True
        body = log.read_text(encoding='utf-8-sig', errors='replace')
        row.update(logSha256=sha(log), passNames=re.findall(r'^PASS (.+)$', body, re.M),
                   failNames=re.findall(r'^FAIL (.+)$', body, re.M), finishedUtc=dt.datetime.now(dt.timezone.utc).isoformat())
        save(OUT / 'result.json', state)
        require(row['exitCode'] == 0 and not row['failNames'] and stable(), 'Main child failed: ' + name)
        if count is None:
            totals = re.findall(r'\b(\d+) Warning\(s\)\s+(\d+) Error\(s\)', body)
            if not totals:
                totals = re.findall(r'(\d+)\s+个警告\s+(\d+)\s+个错误', body)
            require(len(totals) == 1 and totals[0][1] == '0', 'Build summary differs')
            row['warnings'], row['errors'] = map(int, totals[0])
            if name == 'six-release':
                require(row['warnings'] == 0, 'Six Release build has warnings')
        else:
            require(len(row['passNames']) == count and len(set(row['passNames'])) == count, 'Actual case coverage differs: ' + name)
        save(OUT / 'result.json', state)
        print(name + ': exit0, checks=' + str(len(row['passNames'])), flush=True)

    def renderer_checks(assembly):
        # These fixtures have independent process-local native state and unique
        # synthetic image files. Keep two children active, with fixed journal
        # order and a single writer of the controller/result records.
        rows = []
        for case in range(1, 13):
            row = dict(id='renderer-' + str(case),
                argv=[str(DOTNET), str(assembly), '--line-renderer-consumer-case', str(case)],
                naturalExit=False, status='queued')
            rows.append(row)
        state['steps'].extend(rows)
        save(OUT / 'result.json', state)
        active = {}; cursor = 0; failure = None
        while cursor < len(rows) or active:
            while failure is None and cursor < len(rows) and len(active) < state['rendererProcessConcurrency']:
                row = rows[cursor]; cursor += 1
                try:
                    require(stable(), 'Main input/control changed before ' + row['id'])
                    log = OUT / (row['id'] + '.log')
                    stream = log.open('wb')
                    try:
                        child = subprocess.Popen(row['argv'], cwd=ROOT, env=env, stdout=stream, stderr=subprocess.STDOUT)
                    except Exception:
                        stream.close()
                        raise
                    row.update(pid=child.pid, status='running', startedUtc=dt.datetime.now(dt.timezone.utc).isoformat())
                    active[child.pid] = (child, stream, row, log)
                    save(OUT / 'result.json', state)
                except Exception as error:
                    failure = error
            for pid, (child, stream, row, log) in list(active.items()):
                code = child.poll()
                if code is None:
                    continue
                stream.close()
                row.update(exitCode=code, naturalExit=True, status='finished',
                           finishedUtc=dt.datetime.now(dt.timezone.utc).isoformat())
                body = log.read_text(encoding='utf-8-sig', errors='replace')
                row.update(logSha256=sha(log), passNames=re.findall(r'^PASS (.+)$', body, re.M),
                           failNames=re.findall(r'^FAIL (.+)$', body, re.M))
                del active[pid]
                save(OUT / 'result.json', state)
                try:
                    require(code == 0 and not row['failNames'] and len(row['passNames']) == 1
                            and stable(), 'Main renderer child failed: ' + row['id'])
                    print(row['id'] + ': exit0, checks=1', flush=True)
                except Exception as error:
                    failure = failure or error
            if failure is not None and not active:
                raise failure
            if active:
                time.sleep(.25)

    try:
        for name, project in (('six-release', 'src/RizToolbox.Loader/RizToolbox.Loader.csproj'), ('solution', 'RizToolbox.sln')):
            run(name, [DOTNET, 'build', ROOT / project, '-c', 'Release', '--artifacts-path', artifacts,
                '--verbosity', 'minimal', '--disable-build-servers', '-m:1', '-p:UseSharedCompilation=false',
                '-p:NuGetAudit=false', '--configfile', config])
            if name == 'six-release':
                state['productionAfterSix'] = products()
        require(products() == state['productionAfterSix'], 'Solution changed the production pairs')
        judgements = artifacts / 'bin/RizToolbox.Judgements.Tests/release/RizToolbox.Judgements.Tests.dll'
        runtime = artifacts / 'bin/RizToolbox.Runtime.Tests/release/RizToolbox.Runtime.Tests.dll'
        renderer_checks(judgements)
        run('reader-seven', [DOTNET, judgements, '--line-ring-buffer-checks'], 7)
        for case in range(1, 37):
            run('endpoint-' + str(case), [DOTNET, judgements, '--line-ring-endpoint-writer-case', str(case)], 1)
        for case in SOURCE_CASES:
            run('source-' + str(case), [DOTNET, judgements, '--line-ring-source-case', str(case)], 1)
        run('runtime-default', [DOTNET, runtime], 375)
        state['productionAfterTests'] = products()
        require(state['productionAfterTests'] == state['productionAfterSix'], 'Tests changed production pairs')
        state['testOutputs'] = {}
        for assembly in (judgements, runtime):
            for path in assembly.parent.iterdir():
                if path.is_file():
                    state['testOutputs'][path.relative_to(OUT).as_posix()] = sha(path)
        state['inputsAfter'] = scanner.inputs()
        state['inputsUnchanged'] = state['inputsAfter'] == expected
        require(state['inputsUnchanged'] and stable(), 'Main changed during validation')
        require(sha(ROOT / 'README.md') == manifest['readmeSha256'], 'User README changed')
        state['passed'] = True
    except Exception as error:
        state['failure'] = str(error)
        raise
    finally:
        state['finishedUtc'] = dt.datetime.now(dt.timezone.utc).isoformat()
        save(OUT / 'result.json', state)
        print(json.dumps({key: state.get(key) for key in ('passed', 'inputsUnchanged', 'failure')}), flush=True)


if __name__ == '__main__':
    main()
