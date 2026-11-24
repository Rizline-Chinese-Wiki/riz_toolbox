"""Exercise the exact signature/invoke seam on a synthetic image, not the game."""
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

WORK = Path(__file__).resolve().parent
ROOT = WORK.parents[1]
OUT = Path('D:/riz-build-cache-next/line-setpositions-profile-20261114/root-r2')


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def save(path, value):
    path.write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')


def main():
    assert not OUT.exists()
    manifest = json.loads((WORK / 'candidate-root-v3.json').read_text())
    assert all(sha(WORK / name) == digest for name, digest in manifest['files'].items())
    OUT.mkdir(parents=True)
    source = OUT / 'source'
    shutil.copytree(ROOT / 'src', source / 'src', ignore=shutil.ignore_patterns('bin', 'obj'))
    shutil.copytree(ROOT / 'tests/RizToolbox.Runtime.Tests', source / 'tests/RizToolbox.Runtime.Tests', ignore=shutil.ignore_patterns('bin', 'obj'))
    for name in ('Directory.Build.props', 'global.json'):
        shutil.copy2(ROOT / name, source / name)
    for name, digest in manifest['files'].items():
        target = source / name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(WORK / name, target)
        assert sha(target) == digest
    inputs = {p.relative_to(source).as_posix(): sha(p) for p in source.rglob('*') if p.is_file()}
    save(OUT / 'inputs-before.json', inputs)
    config = OUT / 'NuGet.Config'
    config.write_text('<?xml version="1.0"?><configuration><packageSources><clear /></packageSources></configuration>', encoding='utf-8')
    env = os.environ.copy()
    env.update(DOTNET_GENERATE_ASPNET_CERTIFICATE='false', DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false',
               DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1', DOTNET_NOLOGO='1')
    artifacts = OUT / 'artifacts'
    dotnet = 'C:/Program Files/dotnet/dotnet.exe'
    assembly = artifacts / 'bin/RizToolbox.Runtime.Tests/release/RizToolbox.Runtime.Tests.dll'
    commands = [('build', [dotnet, 'build', str(source / 'tests/RizToolbox.Runtime.Tests/RizToolbox.Runtime.Tests.csproj'),
                           '-c', 'Release', '--artifacts-path', str(artifacts), '-m:1', '-warnaserror',
                           '--disable-build-servers', '-p:UseSharedCompilation=false', '-p:NuGetAudit=false',
                           '-p:RestoreConfigFile=' + str(config)]),
                ('setpositions8', [dotnet, str(assembly), '--line-setpositions-checks'])]
    state = dict(controllerPid=os.getpid(), candidateSha256=sha(WORK / 'candidate-root-v3.json'),
                 startedUtc=dt.datetime.now(dt.timezone.utc).isoformat(), passed=False,
                 mainApplied=False, gameStarted=False, realProfileAdmissionVerified=False,
                 completeSeekPermitted=False, steps=[])
    save(OUT / 'result.json', state)
    try:
        for name, argv in commands:
            row = dict(name=name, argv=argv, naturalExit=False)
            state['steps'].append(row)
            with (OUT / (name + '.log')).open('wb') as stream:
                process = subprocess.Popen(argv, cwd=source, env=env, stdout=stream, stderr=subprocess.STDOUT)
                row['pid'] = process.pid
                save(OUT / 'result.json', state)
                row['exitCode'] = process.wait(timeout=900)
                row['naturalExit'] = True
            row['logSha256'] = sha(OUT / (name + '.log'))
            save(OUT / 'result.json', state)
            print(name + ': exit=' + str(row['exitCode']), flush=True)
            assert row['exitCode'] == 0, name
        body = (OUT / 'setpositions8.log').read_text(encoding='utf-8-sig')
        assert body.count('PASS SetPositions ') == 8 and 'SetPositions checks: 8/8 passed.' in body
        assert all(sha(source / name) == digest for name, digest in inputs.items())
        state['binaries'] = {p.relative_to(OUT).as_posix(): sha(p) for p in artifacts.rglob('*') if p.is_file() and p.suffix in ('.dll', '.pdb')}
        state['passed'] = True
    except Exception as error:
        state['failure'] = repr(error)
    state['finishedUtc'] = dt.datetime.now(dt.timezone.utc).isoformat()
    save(OUT / 'result.json', state)
    print(json.dumps(dict(passed=state['passed'], failure=state.get('failure'), resultSha256=sha(OUT / 'result.json'))), flush=True)
    return 0 if state['passed'] else 1


if __name__ == '__main__':
    sys.exit(main())
