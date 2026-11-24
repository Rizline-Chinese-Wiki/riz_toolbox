"""Freeze one candidate input graph and compile/test it in an isolated tree."""
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

ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).resolve().parent
DOTNET = Path('C:/Program Files/dotnet/dotnet.exe')


def sha(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def save(path, value):
    staged = Path(str(path) + '.pending')
    staged.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    os.replace(staged, path)


def require(ok, reason):
    if not ok:
        raise RuntimeError(reason)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--test-cases', type=int, default=0)
    args = parser.parse_args()
    out = args.out.resolve()
    allowed = Path('D:/riz-build-cache-next/line-renderer-consumer-20261114').resolve()
    require(out.parent == allowed and not out.exists(), 'Use a fresh direct child of the renderer build cache')
    scanner_path = ROOT / 'work/line-ring-source-validation-20261113/run_validation.py'
    require(sha(scanner_path) == '61c2983ae117322b25ff96ad75b659b870fdadf9aada9274727248cd5ca98fd4', 'Scanner changed')
    spec = importlib.util.spec_from_file_location('renderer_scanner', scanner_path)
    scanner = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(scanner)
    original = scanner.inputs()
    require(len(original) == 913, 'Expected accepted 913 input graph')
    overlay = {}
    for base in ('src', 'tests'):
        for path in (WORK / base).rglob('*'):
            if path.is_file() and path.suffix in ('.cs', '.csproj', '.props', '.targets', '.json'):
                overlay[path.relative_to(WORK).as_posix()] = sha(path)
    expected = dict(original)
    expected.update(overlay)
    controls = {str(Path(__file__)): sha(__file__), str(scanner_path): sha(scanner_path)}
    out.mkdir(parents=True)
    control_dir = out / 'controls'
    control_dir.mkdir()
    shutil.copyfile(__file__, control_dir / 'validate_candidate.py')
    shutil.copyfile(scanner_path, control_dir / 'input_scanner.py')
    shadow = out / 'source'
    for name, digest in expected.items():
        source = WORK / name if name in overlay else ROOT / name
        require(sha(source) == digest, 'Input changed while copying: ' + name)
        target = shadow / name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, target)
    config = out / 'NuGet.Config'
    config.write_text('<?xml version="1.0"?><configuration><packageSources><clear /></packageSources></configuration>', encoding='utf-8')
    env = os.environ.copy()
    env.update(DOTNET_GENERATE_ASPNET_CERTIFICATE='false', DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false',
               DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1', DOTNET_NOLOGO='1')
    fixtures = {}
    archive = ROOT / 'docs/validation/line-vector3-array-runtime-20261114'
    fixture_rows = json.loads((archive / 'run/result.json').read_text(encoding='utf-8-sig'))['nativeFixtures']
    for key, row in fixture_rows.items():
        path = archive / 'fixtures' / key / Path(row['path']).name
        require(sha(path) == row['sha256'], 'Native fixture changed')
        env[key] = str(path)
        fixtures[key] = dict(path=str(path), sha256=row['sha256'])
    state = dict(schema='renderer-consumer-candidate/v1', controllerPid=os.getpid(),
                 startedUtc=dt.datetime.now(dt.timezone.utc).isoformat(), passed=False,
                 originalInputs=original, candidateOverlay=overlay, frozenInputs=expected,
                 controls=controls, nativeFixtures=fixtures, steps=[], installed=False, gameStarted=False,
                 completeSeekPermitted=False, realGameBodyAdmissionTested=False)
    state['plannedCases'] = [10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 11, 12] if args.test_cases == 12 else list(range(1, args.test_cases + 1))
    save(out / 'result.json', state)
    print('controllerPid=' + str(os.getpid()), flush=True)

    def stable():
        return all(sha(shadow / name) == digest for name, digest in expected.items())

    def run(name, argv, count=None):
        require(stable(), 'Frozen source changed before ' + name)
        row = dict(id=name, argv=[str(value) for value in argv], naturalExit=False,
                   startedUtc=dt.datetime.now(dt.timezone.utc).isoformat())
        state['steps'].append(row)
        save(out / 'result.json', state)
        log = out / (name + '.log')
        with log.open('wb') as stream:
            child = subprocess.Popen(row['argv'], cwd=shadow, env=env, stdout=stream, stderr=subprocess.STDOUT)
            row['pid'] = child.pid
            save(out / 'result.json', state)
            row['exitCode'] = child.wait()
            row['naturalExit'] = True
        body = log.read_text(encoding='utf-8-sig', errors='replace')
        row.update(logSha256=sha(log), passNames=re.findall(r'^PASS (.+)$', body, re.M),
                   failNames=re.findall(r'^FAIL (.+)$', body, re.M), finishedUtc=dt.datetime.now(dt.timezone.utc).isoformat())
        save(out / 'result.json', state)
        require(row['exitCode'] == 0 and not row['failNames'] and stable(), 'Child failed: ' + name)
        if count is None:
            totals = re.findall(r'\b(\d+) Warning\(s\)\s+(\d+) Error\(s\)', body)
            if not totals:
                totals = re.findall(r'(\d+)\s+个警告\s+(\d+)\s+个错误', body)
            require(len(totals) == 1 and totals[0][1] == '0', 'Build error summary differs')
            row['warnings'], row['errors'] = map(int, totals[0])
            if name == 'six-release':
                require(row['warnings'] == 0, 'Six-component build has warnings')
        else:
            require(len(row['passNames']) == count and len(set(row['passNames'])) == count, 'Case coverage differs')
        save(out / 'result.json', state)
        print(name + ': exit0, passes=' + str(len(row['passNames'])), flush=True)

    try:
        artifacts = out / 'artifacts'
        def build(name, project):
            run(name, [DOTNET, 'build', shadow / project, '-c', 'Release', '--artifacts-path', artifacts,
                       '--verbosity', 'minimal', '--disable-build-servers', '-m:1',
                       '-p:UseSharedCompilation=false', '-p:NuGetAudit=false', '--configfile', config])
        build('six-release', 'src/RizToolbox.Loader/RizToolbox.Loader.csproj')
        if args.test_cases:
            build('judgements-build', 'tests/RizToolbox.Judgements.Tests/RizToolbox.Judgements.Tests.csproj')
            assembly = artifacts / 'bin/RizToolbox.Judgements.Tests/release/RizToolbox.Judgements.Tests.dll'
            for case in state['plannedCases']:
                run('renderer-case-' + str(case), [DOTNET, assembly, '--line-renderer-consumer-case', str(case)], 1)
        state['frozenInputsUnchanged'] = stable()
        state['passed'] = state['frozenInputsUnchanged']
    except Exception as error:
        state['failure'] = str(error)
        raise
    finally:
        state['finishedUtc'] = dt.datetime.now(dt.timezone.utc).isoformat()
        state['candidateOverlayAtFinish'] = {name: sha(WORK / name) for name in overlay}
        state['candidateOverlayUnchanged'] = state['candidateOverlayAtFinish'] == overlay
        save(out / 'result.json', state)
        print(json.dumps({key: state.get(key) for key in ('passed', 'frozenInputsUnchanged', 'candidateOverlayUnchanged', 'failure')}), flush=True)


if __name__ == '__main__':
    main()
