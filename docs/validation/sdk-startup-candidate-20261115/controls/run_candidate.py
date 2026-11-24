"""Offline SDK reference package, two independent plugins and formal host on1172."""
import datetime as dt
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).resolve().parent
BASE = Path('D:/riz-build-cache-next/runtime-tick-admission-20261115/run-r2')
MANIFEST = WORK / 'source-manifest.json'
OUT = Path('D:/riz-build-cache-next/sdk-startup-candidate-20261115/run-r1')
DOTNET = Path('C:/Program Files/dotnet/dotnet.exe')
PWSH = Path('C:/Program Files/WindowsApps/OpenAI.CodexPrimaryRuntime.v26-1112-737-0_26.1112.737.0_x64__3k8sg7r9htsxt/dependencies/native/powershell/pwsh.exe')
sha = lambda path: hashlib.sha256(Path(path).read_bytes()).hexdigest()
read = lambda path: json.loads(Path(path).read_text(encoding='utf-8-sig'))


def require(ok, reason):
    if not ok: raise RuntimeError(reason)


def save(path, data):
    pending = path.with_suffix(path.suffix + '.pending')
    pending.write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf-8'); os.replace(pending, path)


def now(): return dt.datetime.now(dt.timezone.utc).isoformat()


def main():
    global OUT
    parser = argparse.ArgumentParser(); parser.add_argument('--out', type=Path, required=True); args = parser.parse_args()
    OUT = args.out.resolve()
    require(OUT.parent == Path('D:/riz-build-cache-next/sdk-startup-candidate-20261115').resolve(), 'Use a fresh scoped run directory')
    require(not OUT.exists(), 'Preserve prior SDK runs')
    require(sha(MANIFEST) == 'fd6d920e6be3a0fd9148939510b1421435ffa5a0403e8477e868e01d318e3909', 'Prepared manifest differs')
    declared = read(MANIFEST); candidate = read(BASE / 'result.json')
    waited = read(BASE.parent / 'run-r2-supervision/controller-exit.json')
    require(sha(BASE / 'result.json') == declared['baseResultSha256']
        and sha(BASE.parent / 'run-r2-supervision/controller-exit.json') == declared['baseSupervisionSha256']
        and waited['controllerExited'] and waited['actualControllerExitCode'] == 0
        and waited['controllerPid'] == waited['resultControllerPid'] == candidate['controllerPid'] == 43772
        and waited['supervisorPid'] == 34372
        and waited['exitObservedBy'] == 'original subprocess.Popen.wait process handle'
        and candidate['passed'] and candidate['fullFrozenInputsUnchanged']
        and candidate['fullFrozenInputs'] == declared['expectedInputs'] and len(declared['expectedInputs']) == 1172,
        'Complete1172 base source/original wait differs')
    require(DOTNET.is_file() and PWSH.is_file(), 'Known CLI tools are unavailable')
    expected = declared['expectedInputs']; extras = declared['additionalInputs']
    OUT.mkdir(parents=True); source = OUT / 'source'; controls = OUT / 'controls'; controls.mkdir()
    for name, digest in expected.items():
        target = source / name; target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(BASE / 'source' / name, target); require(sha(target) == digest, 'Frozen compiler source differs')
    for name, digest in extras.items():
        target = source / name; target.parent.mkdir(parents=True, exist_ok=True)
        if target.exists(): require(sha(target) == digest, 'Additional source conflicts with1172 snapshot')
        else: shutil.copyfile(BASE / 'source' / name if name in candidate['auxiliaryInputs'] else WORK / 'frozen-current-sources' / name, target)
        require(sha(target) == digest, 'Additional distribution source differs')
    dependencies = {}; feed = OUT / 'dependency-feed'; feed.mkdir()
    for name in ('microsoft.netcore.app.ref', 'microsoft.aspnetcore.app.ref', 'microsoft.windowsdesktop.app.ref', 'microsoft.netcore.app.host.win-x64'):
        package = ROOT.drive + '/Users/71957/.nuget/packages/' + name + '/6.0.36/' + name + '.6.0.36.nupkg'
        path = Path(package); require(path.is_file() and sha(path) == declared['dependencyPins'][path.name]['sha256'], 'Pinned original framework package differs: ' + name)
        target = feed / path.name; shutil.copyfile(path, target); require(sha(path) == sha(target), 'Cached dependency changed')
        dependencies[path.name] = dict(original=str(path), sha256=sha(target))
    config = source / 'NuGet.Config'; require(not config.exists(), 'Preserve an existing snapshot config')
    config.write_text('<configuration><packageSources><clear/><add key="FrameworkCache" value="' + escape(str(feed)) + '"/></packageSources><config><add key="globalPackagesFolder" value="' + escape(str(OUT / 'formal-host-nuget-cache')) + '"/></config></configuration>', encoding='utf-8')
    script = source / 'scripts/Test-SdkDistribution.ps1'; body = script.read_text(encoding='utf-8-sig')
    shutil.copyfile(script, controls / 'Test-SdkDistribution.original.ps1')
    # Only dependency transport, fresh output naming and build orchestration
    # change. The original two plugin probes and all14 formal-host checks remain.
    require(body.count('https://api.nuget.org/v3/index.json') == 1, 'Distribution feed anchor differs')
    body = body.replace('https://api.nuget.org/v3/index.json', escape(str(feed)))
    anchor = "$proofRoot = Join-Path $repoRoot ('artifacts/sdk-distribution-' + [Guid]::NewGuid().ToString('N'))"
    require(body.count(anchor) == 1, 'Proof directory anchor differs')
    body = body.replace(anchor, "$proofRoot = Join-Path $repoRoot 'artifacts/sdk-distribution-proof'")
    anchor = '--use-lock-file --verbosity minimal'; require(body.count(anchor) == 1, 'Restore anchor differs')
    body = body.replace(anchor, anchor + ' -p:NuGetAudit=false')
    anchor = 'dotnet build $project -c Release --no-restore --verbosity minimal'; require(body.count(anchor) == 1, 'Plugin build anchor differs')
    body = body.replace(anchor, anchor + ' --disable-build-servers -m:1 -p:UseSharedCompilation=false')
    anchor = "dotnet run --project (Join-Path $repoRoot 'tests/RizToolbox.Distribution.Tests/RizToolbox.Distribution.Tests.csproj') -c Release --"
    require(body.count(anchor) == 1, 'Formal host anchor differs')
    body = body.replace(anchor, anchor.replace('-c Release --', '-c Release --no-build --no-restore --'))
    offline = source / 'scripts/Test-SdkDistribution.Offline.ps1'; offline.write_text(body, encoding='utf-8', newline='\n')
    shutil.copyfile(offline, controls / offline.name); shutil.copyfile(__file__, controls / Path(__file__).name)
    shutil.copyfile(MANIFEST, controls / 'source-manifest.json')
    for name in ('base-startup-result.json', 'base-startup-controller-exit.json', 'source-diff.json', 'control-adaptation.diff', 'prepare_controls.py', 'supervise.py'):
        shutil.copyfile(WORK / name, controls / name)
    shutil.copyfile(WORK / 'source-review.md', controls / 'source-review.md')
    env = os.environ.copy()
    env.update(DOTNET_GENERATE_ASPNET_CERTIFICATE='false', DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false',
        DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1', DOTNET_NOLOGO='1')
    state = dict(schema='sdk-candidate-distribution-checks/v1', controllerPid=os.getpid(), startedUtc=now(), passed=False,
        candidateOnly=True, mainApplied=False, compilerInputs=1172, frozenInputs=expected, additionalInputs=extras,
        sourceManifestSha256=sha(MANIFEST), baseCurrentControllerPid=43772, baseStartupValidationPassed=True, baseResultSha256=declared['baseResultSha256'], baseSupervisionSha256=declared['baseSupervisionSha256'], dependencies=dependencies, offlineRunnerSha256=sha(offline),
        originalRunnerSha256=extras['scripts/Test-SdkDistribution.ps1'], steps=[], sdkGoalComplete=False,
        additionalCompilerInputs=0, distributionCompilerInputsOverlap=2, distinctCompilerInputs=1172, fullManagedMatrix=False, runtimeGameVerification=False,
        templateEngineIsolated=True, formalHostCacheIsolated=True, environmentOverrides={key:env[key] for key in ('DOTNET_GENERATE_ASPNET_CERTIFICATE', 'DOTNET_ADD_GLOBAL_TOOLS_TO_PATH', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE', 'DOTNET_NOLOGO')}, installed=False, gameStarted=False)
    save(OUT / 'result.json', state); print('controllerPid=' + str(os.getpid()), flush=True)

    def stable():
        return all(sha(source / name) == digest for name, digest in expected.items()) and all(sha(source / name) == digest for name, digest in extras.items())

    def run(name, argv, expected_passes=None):
        require(stable(), 'Frozen source changed before ' + name)
        row = dict(id=name, argv=list(map(str, argv)), startedUtc=now(), naturalExit=False)
        state['steps'].append(row); save(OUT / 'result.json', state); log = OUT / (name + '.log')
        with log.open('xb') as stream:
            child = subprocess.Popen(row['argv'], cwd=source, env=env, stdin=subprocess.DEVNULL, stdout=stream, stderr=subprocess.STDOUT, creationflags=subprocess.CREATE_NO_WINDOW)
            row['pid'] = child.pid; save(OUT / 'result.json', state); row['exitCode'] = child.wait(); row['naturalExit'] = True
        body = log.read_text(encoding='utf-8-sig', errors='replace')
        row.update(finishedUtc=now(), creationFlags=subprocess.CREATE_NO_WINDOW, hidden=True, logSha256=sha(log), passNames=re.findall(r'^PASS (.+)$', body, re.M), failNames=re.findall(r'^FAIL (.+)$', body, re.M))
        save(OUT / 'result.json', state)
        require(row['exitCode'] == 0 and not row['failNames'] and stable(), 'SDK child failed: ' + name)
        if expected_passes is not None: require(len(row['passNames']) == len(set(row['passNames'])) == expected_passes, 'SDK check count differs')
        print(name + ': exit0 checks=' + str(len(row['passNames'])), flush=True)

    try:
        run('formal-host-build', [DOTNET, 'build', source / 'tests/RizToolbox.Distribution.Tests/RizToolbox.Distribution.Tests.csproj',
            '-c', 'Release', '--verbosity', 'minimal', '--disable-build-servers', '-m:1', '-p:UseSharedCompilation=false',
            '-p:NuGetAudit=false', '--configfile', config])
        run('sdk-distribution', [PWSH, '-NoProfile', '-File', offline], 14)
        proof = source / 'artifacts/sdk-distribution-proof/result.json'; result = read(proof)
        require(result['checksPassed'] == 14 and not result['runtimeGameVerification'], 'Actual distribution proof differs')
        state['distributionProof'] = dict(path=str(proof), sha256=sha(proof)); state['checksPassed'] = 14
        state['package'] = dict(path=str(source / 'artifacts/sdk/RizToolbox.Sdk.1.2.0.nupkg'), sha256=result['packageSha256'])
        require(sha(state['package']['path']) == state['package']['sha256'], 'SDK package differs')
        state['frozenInputsUnchanged'] = stable(); require(state['frozenInputsUnchanged'], 'Frozen input changed')
        state['passed'] = True
    except Exception as error:
        state['failure'] = str(error); raise
    finally:
        state['finishedUtc'] = now(); save(OUT / 'result.json', state)
        print(json.dumps({key:state.get(key) for key in ('passed', 'checksPassed', 'failure')}), flush=True)


if __name__ == '__main__': main()
