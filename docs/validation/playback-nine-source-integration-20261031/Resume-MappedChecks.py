"""Resume only the three uncompleted core-r4 mapped checks in a fresh directory."""
import concurrent.futures
import hashlib
import json
import os
import shutil
import subprocess
from datetime import datetime, timezone
from pathlib import Path

BASE = Path('D:/riz-build-cache-next')
OLD = BASE / 'playback-stock-nine-joint-r2-20261031'
OUT = BASE / 'playback-stock-nine-joint-resume-r1-20261031'
CORE = BASE / 'foundation-nine-source-core-r4-20261031/test-result.json'
NATIVE = BASE / 'playback-stock-native-build-r14-20261031/build-result.json'
FOUNDATION = BASE / 'foundation-private-native-r3-20261031/native-manifest.json'
REPO = Path(__file__).resolve().parents[3]
DOTNET = 'C:/Program Files/dotnet/dotnet.exe'
PWSH = 'C:/Users/71957/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe'


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def sha(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def fact(path):
    path = Path(path).resolve()
    return dict(path=str(path), sha256=sha(path), bytes=path.stat().st_size)


def write(path, value):
    Path(path).write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')


def check(row):
    assert sha(row['path']) == row['sha256'], row['path']


def actual(name, arguments):
    with (OUT / (name + '.stdout.log')).open('wb') as stdout, (OUT / (name + '.stderr.log')).open('wb') as stderr:
        process = subprocess.run(arguments, stdout=stdout, stderr=stderr, check=False)
    (OUT / (name + '.exit.txt')).write_text(str(process.returncode), encoding='utf-8')
    print(json.dumps(dict(phase=name, actualExitCode=process.returncode)), flush=True)
    return process.returncode


def last_json(path):
    return json.loads([line for line in Path(path).read_text(encoding='utf-8-sig').splitlines() if line.strip()][-1])


def verify_mode(mode, value, count):
    assert value['passed'] is True and value['mode'] == mode and value['checks'] == count
    assert value['kind'] == 'mapped-playback-stock-joint-profile'
    assert value['actualDobbyPrepareCount'] == value['actualDobbyCommitCount'] == value['physicalMemberCount'] == 9
    assert (value['initialRevision'], value['commitRevision'], value['restoredRevision']) == (1, 2, 3)
    for flag in ('nativeAuthorityActiveAfterTermination', 'groupActiveAfterTermination', 'getTimeActiveAfterTermination',
                 'pitchActiveAfterTermination', 'gameCodeExecuted', 'dllMainExecuted', 'gameLaunched', 'productionAdmission'):
        assert value[flag] is False, (mode, flag)
    for field in ('getTimeOriginalCalls', 'sourceEntered', 'sourceExited', 'sourceBefore', 'sourceAfter'):
        assert value[field] == 0, (mode, field)
    assert value['metadataSynthetic'] is True and value['syntheticGraph'] is True
    if mode.startswith('foundation-'):
        assert (value['foundationEpoch'], value['foundationOriginalCount'], value['foundationWriteCount']) == (1, 28, 64)


def main():
    assert not OUT.exists(), 'Preserve interrupted checks; use a fresh output directory.'
    assert not (OLD / 'test-result.json').exists(), 'This continuation only handles the interrupted batch.'
    core = read(CORE)
    assert core['passed'] is True and core['runnerExitCode'] == 0
    assert sha(CORE) == 'd96e595c2b8244d176b3b748f72d265212489f2a845791f4ba9f6b2a647707d0'
    old_sources, old_native = read(OLD / 'source-before.json'), read(OLD / 'native-inputs.json')
    old_symbols = read(OLD / 'managed-symbol-source-proof.json')
    for row in old_sources + old_native + core['snapshotFiles']:
        check(row)
    OUT.mkdir(parents=True)
    result = dict(kind='mapped-playback-stock-joint-profile', passed=False, failure=None, buildExitCode=None,
                  checks=0, modes=[], sources=[], nativeInputs=[], images=[], artifacts=[], managedSymbols=[],
                  sourceProof=fact(CORE), nativeProof=fact(NATIVE), foundationProof=fact(FOUNDATION), script=fact(__file__),
                  metadataSynthetic=True, graphSynthetic=True, mappedPinnedImage=True, gameExecuted=False,
                  gameDllMainExecuted=False, installed=False, productionAdmissionVerified=False, original44Repeated=False,
                  resumedAfterMissingHandle=True, originalSession=83712,
                  originalInterruptedDriver=fact(BASE / 'playback-source-nine-final-run-r2-20261031/run-result.json'),
                  interruptedFoundationNormalHasNoExit=(not (OLD / 'foundation-normal.exit.txt').exists()),
                  priorSuccessfulModesReused=[], remainingModesActuallyRun=[])
    src = OUT / 'source'
    try:
        for row in old_sources:
            destination = src / row['relativePath']
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(row['path'], destination)
            result['sources'].append(dict(row, path=str(destination)))
            check(result['sources'][-1])
        for row in old_native:
            destination = OUT / Path(row['path']).relative_to(OLD)
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(row['path'], destination)
            result['nativeInputs'].append(dict(row, path=str(destination)))
            check(result['nativeInputs'][-1])
        write(OUT / 'source-before.json', result['sources'])
        write(OUT / 'native-inputs.json', result['nativeInputs'])
        config = OUT / 'NuGet.Config'
        config.write_text('<configuration><packageSources><clear/></packageSources></configuration>', encoding='utf-8')
        project = src / 'tests/RizToolbox.Gameplay.Tests/RizToolbox.Gameplay.Tests.csproj'
        result['buildExitCode'] = actual('build', [DOTNET, 'build', str(project), '-c', 'Release', '-m:1',
                                                 '--configfile', str(config), '-p:UseSharedCompilation=false', '--nologo', '-v:minimal'])
        assert result['buildExitCode'] == 0
        binary = project.parent / 'bin/Release/net8.0'
        helper = OUT / 'native-inputs/foundation-native/RizToolbox.JudgeContext.dll'
        shutil.copy2(helper, binary / helper.name)
        for item in old_symbols:
            old_image = Path(item['assembly']['path'])
            new_image = binary / old_image.name
            assert sha(new_image) == item['assembly']['sha256'], 'Reused checks require byte-identical managed binaries.'
        result['reusedManagedImagesByteIdentical'] = True
        symbols_script = OUT / 'Verify-Symbols.ps1'
        symbols_script.write_text('''$ErrorActionPreference='Stop'
$definition='C:/Users/71957/Desktop/riz_toolbox/scripts/Build-PlaybackSourceGroupSnapshot.ps1'
$tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile($definition,[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'Invalid symbol verifier'}
foreach($name in @('Hash','ContainedRelative','ManagedSymbolProof')){
 $nodes=@($ast.FindAll({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq $name},$false))
 if($nodes.Count -ne 1){throw 'Missing unique symbol function'}
 . ([scriptblock]::Create($nodes[0].Extent.Text))
}
$src=Join-Path $PSScriptRoot 'source';$hashes=@{}
foreach($row in Get-Content -LiteralPath (Join-Path $PSScriptRoot 'source-before.json') -Raw|ConvertFrom-Json){$hashes[[IO.Path]::GetFullPath($row.path)]=$row.sha256}
$binary=Join-Path $src 'tests/RizToolbox.Gameplay.Tests/bin/Release/net8.0'
$images=@('Sdk','Core','Runtime','Adapter','Gameplay.Tests'|ForEach-Object{ManagedSymbolProof (Join-Path $binary ('RizToolbox.'+$_.ToString()+'.dll')) $src $hashes})
if($images.Count -ne 5){throw 'Five actual PE/PDB pairs required'}
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'managed-symbol-source-proof.json'),($images|ConvertTo-Json -Depth 24),[Text.UTF8Encoding]::new($false))
''', encoding='utf-8')
        assert actual('symbols', [PWSH, '-NoProfile', '-File', str(symbols_script)]) == 0
        result['managedSymbols'] = read(OUT / 'managed-symbol-source-proof.json')
        expected = {'normal': 28, 'pitch-tail': 32, 'body': 32, 'consumer': 32,
                    'foundation-normal': 32, 'foundation-outside': 35, 'foundation-unwind': 35}
        for mode in list(expected)[:4]:
            log, exit_path = OLD / (mode + '.log'), OLD / (mode + '.exit.txt')
            assert exit_path.read_text().strip() == '0'
            value = last_json(log)
            verify_mode(mode, value, expected[mode])
            result['modes'].append(dict(mode=mode, actualExitCode=0, checks=expected[mode], log=fact(log),
                                        exit=fact(exit_path), actual=value, reusedFromByteIdenticalBuild=True))
            result['priorSuccessfulModesReused'].append(mode)
            result['images'].append(dict(mode=mode, **fact(OLD / 'images' / mode / 'GameAssembly.dll')))
        def remaining(mode):
            image = OUT / 'images' / mode / 'GameAssembly.dll'
            image.parent.mkdir(parents=True)
            shutil.copy2(OLD / 'images/normal/GameAssembly.dll', image)
            assert sha(image) == '853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79'
            arguments = [DOTNET, str(binary / 'RizToolbox.Gameplay.Tests.dll'), '--mapped-playback-stock-group-checks',
                         str(image), str(OUT / 'native-inputs/dobby.dll'),
                         str(OUT / 'native-inputs/gettime-r4/RizToolbox.SingleReturn.Prototype.dll'),
                         str(OUT / 'native-inputs/source-native/RizToolbox.PlaybackSourcePrototype.dll'), mode]
            write(OUT / (mode + '.command.json'), dict(arguments=arguments, startedAt=datetime.now(timezone.utc).isoformat()))
            code = actual(mode, arguments)
            assert code == 0, mode
            log = OUT / (mode + '.stdout.log')
            value = last_json(log)
            verify_mode(mode, value, expected[mode])
            return dict(mode=mode, actualExitCode=code, checks=expected[mode], log=fact(log),
                        stderr=fact(OUT / (mode + '.stderr.log')), actual=value), dict(mode=mode, **fact(image))
        with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
            jobs = {pool.submit(remaining, mode): mode for mode in list(expected)[4:]}
            for future in concurrent.futures.as_completed(jobs):
                row, image = future.result()
                result['modes'].append(row)
                result['images'].append(image)
                result['remainingModesActuallyRun'].append(row['mode'])
                write(OUT / 'progress.json', result)
        result['modes'].sort(key=lambda row: list(expected).index(row['mode']))
        result['checks'] = sum(row['checks'] for row in result['modes'])
        assert len(result['modes']) == 7 and result['checks'] == 226
        for row in result['sources'] + result['nativeInputs'] + result['images']:
            check(row)
        result['frozenInputsUnchanged'] = result['passed'] = True
    except Exception as error:
        result['failure'] = str(error)
        raise
    finally:
        result['artifacts'] = [fact(path) for path in OUT.iterdir() if path.is_file() and path.name != 'test-result.json']
        write(OUT / 'test-result.json', result)


if __name__ == '__main__':
    main()
