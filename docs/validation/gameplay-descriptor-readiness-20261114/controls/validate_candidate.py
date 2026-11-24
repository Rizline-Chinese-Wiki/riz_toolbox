"""Fresh complete1171 build and affected Hold plus27 ordinary managed suites."""
import datetime as dt
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).resolve().parent
BASE = ROOT / 'docs/validation/complete-solution-hold-20261114'
OUT = Path('D:/riz-build-cache-next/gameplay-descriptor-readiness-20261114/run-r1')
DOTNET = 'C:/Program Files/dotnet/dotnet.exe'
SUITES = ['Core', 'Adapter', 'Hosting', 'Game', 'Runtime', 'Sdk', 'Scope', 'Diagnostics', 'PackageLifecycle',
    'Module', 'Mutation', 'Data', 'Storage', 'Services', 'GameResources', 'GameAppearance', 'GameCamera',
    'Hooks', 'Gameplay', 'GameplaySourceGeneration', 'Settings', 'Visual', 'Performance', 'AudioContent', 'UiInput', 'RunModification', 'SingleEvent']
sha = lambda path: hashlib.sha256(Path(path).read_bytes()).hexdigest()
read = lambda path: json.loads(Path(path).read_text(encoding='utf-8-sig'))


def require(ok, reason):
    if not ok: raise RuntimeError(reason)


def now(): return dt.datetime.now(dt.timezone.utc).isoformat()


def main():
    global OUT
    parser = argparse.ArgumentParser(); parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--source-generation-only', action='store_true'); args = parser.parse_args()
    OUT = args.out.resolve()
    require(OUT.parent == Path('D:/riz-build-cache-next/gameplay-descriptor-readiness-20261114').resolve(), 'Use a fresh scoped output')
    require(not OUT.exists(), 'Preserve prior runs')
    require(sha(BASE / 'validation-summary.json') == '94b40bdc6e452b24b8fdaeeb9de10fe8917c1fb9ab1736a1d35b356708c26218', 'Complete base differs')
    base = read(BASE / 'solution/result.json'); original = {**base['frozenInputs'], **base['additionalSolutionInputs']}
    require(base['passed'] and len(original) == 1171 and base['solutionProjects'] == 45, 'Complete base scope differs')
    names = ('src/RizToolbox.Adapter/Gameplay/GameplayNativeBackend.cs', 'src/RizToolbox.Adapter/Gameplay/GameplayService.cs', 'tests/RizToolbox.GameplaySourceGeneration.Tests/Program.cs')
    overlay = {name:sha(WORK / name) for name in names}; expected = {**original, **overlay}
    require(all(original[name] != digest for name, digest in overlay.items()), 'Expected three source changes')
    OUT.mkdir(parents=True); source = OUT / 'source'; controls = OUT / 'controls'; controls.mkdir()
    for name, digest in expected.items():
        origin = WORK / name if name in overlay else BASE / 'source' / name
        target = source / name; target.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(origin, target)
        require(sha(origin) == sha(target) == digest, 'Frozen source differs')
    for name in ('validate_candidate.py', 'stage_candidate.py'): shutil.copyfile(WORK / name, controls / name)
    config = OUT / 'NuGet.Config'; config.write_text('<configuration><packageSources><clear/></packageSources></configuration>', encoding='utf-8')
    env = os.environ.copy(); env.update(DOTNET_GENERATE_ASPNET_CERTIFICATE='false', DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false',
        DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1', DOTNET_NOLOGO='1')
    fixtures = read(ROOT / 'work/hold-discovery-guard-next-20261114/candidate-evidence-r2/run/result.json')['nativeFixtures']
    fixtures['RIZ_PLAYBACK_OWNED_FIXTURE'] = dict(path='D:/riz-build-cache-next/gameplay-occurrence-provider-r4-20261031/native/RizToolbox.PlaybackOwnedFixture.dll',
        sha256='88bacc51b2c759c3c650a87a8a10f73f2ae38f37e553a18203037aed2737b7a8')
    for key, row in fixtures.items():
        require(sha(row['path']) == row['sha256'], 'Owned native fixture differs'); env[key] = row['path']
    held = [('discovery', '--hold-discovery-guard-case', 14), ('alive', '--hold-alive-guard-case', 6),
        ('guard', '--hold-source-guard-case', 10), ('owner', '--hold-native-owner-case', 15), ('existing-hold', '--hold-observation-case', 17)]
    suites = ['GameplaySourceGeneration'] if args.source_generation_only else SUITES
    if args.source_generation_only: held = []
    build_name = 'source-generation-build' if args.source_generation_only else 'solution'
    planned = [build_name] + [kind + '-' + str(case) for kind, _, count in held for case in range(1, count+1)] + ['suite-' + name for name in suites]
    state = dict(schema='descriptor-readiness-complete-candidate/v1', controllerPid=os.getpid(), startedUtc=now(), passed=False,
        baseSummarySha256=sha(BASE / 'validation-summary.json'), baseInputs=original, fullFrozenInputs=expected, overlay=overlay,
        fullSolutionInputCount=1171, scopedInputCount=939, solutionProjects=45, nativeFixtures=fixtures,
        expectedSteps=planned, steps=[], plannedSuites=suites, selectedHoldChecks=0 if args.source_generation_only else 62,
        sourceGenerationOnly=args.source_generation_only,
        fullManagedMatrix=False, fullJudgementsRerun=False, parentRendererSuiteRerun=False,
        mainApplied=False, sdkGoalComplete=False, installed=False, gameStarted=False)

    def save():
        pending = OUT / 'result.json.pending'; pending.write_text(json.dumps(state, indent=2) + '\n', encoding='utf-8'); os.replace(pending, OUT / 'result.json')

    def stable(): return all(sha(source / key) == digest for key, digest in expected.items()) and all(sha(WORK / key) == digest for key, digest in overlay.items())

    def run(name, argv, count=None):
        require(stable(), 'Frozen input changed before ' + name)
        row = dict(id=name, argv=list(map(str, argv)), startedUtc=now(), naturalExit=False)
        state['steps'].append(row); save(); log = OUT / (name + '.log')
        with log.open('xb') as stream:
            child = subprocess.Popen(row['argv'], cwd=source, env=env, stdin=subprocess.DEVNULL, stdout=stream, stderr=subprocess.STDOUT)
            row['pid'] = child.pid; save(); row['exitCode'] = child.wait(); row['naturalExit'] = True
        body = log.read_text(encoding='utf-8-sig', errors='replace')
        row.update(finishedUtc=now(), logSha256=sha(log), passNames=re.findall(r'^PASS (.+)$', body, re.M), failNames=re.findall(r'^FAIL (.+)$', body, re.M))
        save(); require(row['exitCode'] == 0 and not row['failNames'] and stable(), 'Candidate step failed: ' + name)
        if name in ('solution', 'source-generation-build'):
            totals = re.findall(r'\b(\d+) Warning\(s\)\s+(\d+) Error\(s\)', body) or re.findall(r'(\d+)\s+个警告\s+(\d+)\s+个错误', body)
            require(len(totals) == 1 and int(totals[0][1]) == 0, 'Solution build totals differ')
            row.update(warnings=int(totals[0][0]), errors=0)
        else:
            require(len(row['passNames']) > 0 and (count is None or len(row['passNames']) == count), 'Direct test coverage differs: ' + name)
        row['passCount'] = len(row['passNames']); save(); print(name + ': exit0 checks=' + str(row['passCount']), flush=True)

    artifacts = OUT / 'artifacts'
    save(); print('controllerPid=' + str(os.getpid()), flush=True)
    try:
        project = 'tests/RizToolbox.GameplaySourceGeneration.Tests/RizToolbox.GameplaySourceGeneration.Tests.csproj' if args.source_generation_only else 'RizToolbox.sln'
        run(build_name, [DOTNET, 'build', source / project, '-c', 'Release', '--artifacts-path', artifacts,
            '--verbosity', 'minimal', '--disable-build-servers', '-m:1', '-p:UseSharedCompilation=false', '-p:NuGetAudit=false', '--configfile', config])
        judgements = artifacts / 'bin/RizToolbox.Judgements.Tests/release/RizToolbox.Judgements.Tests.dll'
        for kind, selector, count in held:
            for case in range(1, count+1): run(kind + '-' + str(case), [DOTNET, judgements, selector, case], 1)
        for suite in suites:
            project_name = 'RizToolbox.' + suite + '.Tests'
            project = source / 'tests' / project_name / (project_name + '.csproj')
            assembly_name = next((node.text for node in ET.fromstring(project.read_bytes()).iter('AssemblyName') if node.text), project_name)
            assembly = artifacts / 'bin' / project_name / 'release' / (assembly_name + '.dll')
            run('suite-' + suite, [DOTNET, assembly], 14 if suite == 'GameplaySourceGeneration' else None)
        require([row['id'] for row in state['steps']] == planned and len(planned) == (2 if args.source_generation_only else 90), 'Planned scope differs')
        state['fullFrozenInputsUnchanged'] = stable(); require(state['fullFrozenInputsUnchanged'], 'Final input check differs')
        state['products'] = {path.relative_to(OUT).as_posix(): sha(path) for path in (artifacts / 'bin').rglob('*') if path.is_file()}
        state['suiteCount'] = len(suites); state['suitePassCount'] = sum(row['passCount'] for row in state['steps'] if row['id'].startswith('suite-'))
        state['passed'] = True
    except Exception as error:
        state['failure'] = str(error); raise
    finally:
        state['finishedUtc'] = now(); save(); print(json.dumps({name:state.get(name) for name in ('passed', 'suiteCount', 'suitePassCount', 'failure')}), flush=True)


if __name__ == '__main__': main()
