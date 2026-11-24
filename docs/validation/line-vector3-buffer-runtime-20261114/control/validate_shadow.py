"""Build and exercise one frozen offline source graph. Never installs or starts the game."""
import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
DOTNET = Path('C:/Program Files/dotnet/dotnet.exe')
ACCEPTED = ROOT / 'docs/validation/line-vector3-array-runtime-20261114'
BASE_SHA = '3fe60eef9ecabc489c2ec4e575c56212f59f9ab397e9b7c47e73c1d1cd188cc9'
ENV = dict(DOTNET_GENERATE_ASPNET_CERTIFICATE='false',
           DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false', DOTNET_CLI_TELEMETRY_OPTOUT='1',
           DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1', DOTNET_NOLOGO='1')


def sha(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def write(path, value):
    path = Path(path)
    staged = path.with_name(path.name + '.pending')
    staged.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    os.replace(staged, path)


def require(ok, message):
    if not ok:
        raise RuntimeError(message)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--manifest', required=True, type=Path)
    parser.add_argument('--manifest-sha256', required=True)
    parser.add_argument('--output', required=True, type=Path)
    opt = parser.parse_args()
    require(re.fullmatch('[0-9a-f]{64}', opt.manifest_sha256)
            and sha(opt.manifest) == opt.manifest_sha256, 'Payload pin differs')
    manifest = read(opt.manifest)
    base = read(ACCEPTED / 'run/inputs-after.json')
    require(sha(ACCEPTED / 'validation-summary.json') == BASE_SHA
            and len(base) == 901 and manifest['baseInputs'] == base,
            'Accepted source ancestry differs')
    require(all(sha(ROOT / name) == digest for name, digest in base.items()),
            'Main differs from accepted source graph')
    payload = manifest['files']
    require(len({row['target'] for row in payload}) == len(payload), 'Duplicate payload')
    expected = dict(base)
    for row in payload:
        name = row['target']
        require(not Path(name).is_absolute() and '..' not in Path(name).parts
                and row['baseSha256'] == base.get(name)
                and sha(ROOT / row['candidate']) == row['sha256'], 'Payload or base differs: ' + name)
        expected[name] = row['sha256']
    out = opt.output.resolve()
    require(out.is_relative_to(Path('D:/riz-build-cache-next/line-vector3-buffer-20261114').resolve())
            and not out.exists(), 'Use a new bounded output; preserve earlier attempts')
    out.mkdir(parents=True)
    source, artifacts = out / 'source', out / 'artifacts'
    for name in base:
        target = source / name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(ROOT / name, target)
    for row in payload:
        target = source / row['target']
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(ROOT / row['candidate'], target)
    require(all(sha(source / name) == digest for name, digest in expected.items()),
            'Staged source differs')
    (out / 'NuGet.Config').write_text(
        '<?xml version="1.0"?><configuration><packageSources><clear /></packageSources></configuration>',
        encoding='utf-8')
    env = os.environ.copy()
    env.update(ENV)
    fixtures = {}
    for key, row in read(ACCEPTED / 'run/result.json')['nativeFixtures'].items():
        path = ACCEPTED / 'fixtures' / key / Path(row['path']).name
        require(sha(path) == row['sha256'], 'Native fixture changed: ' + key)
        env[key] = str(path)
        fixtures[str(path)] = row['sha256']
    state = dict(schema='line-vector3-buffer-shadow/v1', controllerPid=os.getpid(),
                 startedUtc=dt.datetime.now(dt.timezone.utc).isoformat(), passed=False,
                 payloadManifestSha256=opt.manifest_sha256, controllerSha256=sha(__file__),
                 baseSummarySha256=BASE_SHA, inputs=expected, nativeFixtures=fixtures,
                 installed=False, gameStarted=False, completeSeekPermitted=False,
                 sdkGoalComplete=False, steps=[])
    write(out / 'result.json', state)
    assembly = artifacts / 'bin/RizToolbox.Runtime.Tests/release/RizToolbox.Runtime.Tests.dll'
    commands = [('runtime-build', [DOTNET, 'build', source / 'tests/RizToolbox.Runtime.Tests/RizToolbox.Runtime.Tests.csproj',
                                  '-c', 'Release', '--artifacts-path', artifacts,
                                  '--configfile', out / 'NuGet.Config', '-m:1', '-nr:false',
                                  '-p:UseSharedCompilation=false', '--nologo'])]
    for suite in manifest['suites']:
        commands.append((suite['id'], [DOTNET, assembly, *suite['args']]))
    try:
        for name, raw_argv in commands:
            argv = [str(value) for value in raw_argv]
            log = out / (name + '.log')
            row = dict(id=name, argv=argv, startedUtc=dt.datetime.now(dt.timezone.utc).isoformat(),
                       naturalExit=False)
            state['steps'].append(row)
            write(out / 'result.json', state)
            with log.open('wb') as stream:
                process = subprocess.Popen(argv, cwd=source, env=env, stdout=stream, stderr=subprocess.STDOUT)
                row['pid'] = process.pid
                write(out / 'result.json', state)
                try:
                    row['exitCode'] = process.wait(timeout=1800)
                    row['naturalExit'] = True
                except subprocess.TimeoutExpired:
                    row['timedOut'] = True
                    # Preserve an active child as active; never fabricate its exit.
                    raise RuntimeError('Child still active after timeout: ' + name)
            body = log.read_text(encoding='utf-8-sig', errors='replace')
            row.update(logSha256=sha(log), finishedUtc=dt.datetime.now(dt.timezone.utc).isoformat(),
                       passNames=re.findall(r'^PASS (.+)$', body, re.M),
                       failNames=re.findall(r'^FAIL (.+)$', body, re.M))
            require(row['exitCode'] == 0 and not row['failNames'], 'Child failed: ' + name)
            if name == 'runtime-build':
                counts = re.findall(r'\b(\d+) Warning\(s\)\s+([0-9]+) Error\(s\)', body)
                if not counts:
                    counts = re.findall(r'(\d+)\s+个警告\s+(\d+)\s+个错误', body)
                require(counts == [('0', '0')], 'Strict warning/error build gate failed')
            else:
                suite = next(item for item in manifest['suites'] if item['id'] == name)
                require(len(row['passNames']) == suite['expectedCount']
                        and len(set(row['passNames'])) == suite['expectedCount'], 'Unexpected natural case coverage: ' + name)
            write(out / 'result.json', state)
            print(name + ': exit0, ' + str(len(row['passNames'])) + ' checks', flush=True)
        state['sourceUnchanged'] = all(sha(source / name) == digest for name, digest in expected.items())
        state['mainUnchanged'] = all(sha(ROOT / name) == digest for name, digest in base.items())
        state['payloadUnchanged'] = all(sha(ROOT / row['candidate']) == row['sha256'] for row in payload)
        state['binaries'] = {str(path.relative_to(out)): sha(path) for path in sorted(artifacts.rglob('*'))
                             if path.is_file() and path.suffix in ('.dll', '.pdb')}
        state['passed'] = state['sourceUnchanged'] and state['mainUnchanged'] and state['payloadUnchanged']
    except Exception as error:
        state['failure'] = repr(error)
    state['finishedUtc'] = dt.datetime.now(dt.timezone.utc).isoformat()
    write(out / 'result.json', state)
    print(json.dumps(dict(passed=state['passed'], resultSha256=sha(out / 'result.json'),
                          failure=state.get('failure')), ensure_ascii=False), flush=True)
    return 0 if state['passed'] else 1


if __name__ == '__main__':
    sys.exit(main())
