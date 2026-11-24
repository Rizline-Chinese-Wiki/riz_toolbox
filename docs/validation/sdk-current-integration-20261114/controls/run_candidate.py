"""Offline SDK reference package, two independent plugins and formal host on1171."""
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
BASE = Path('D:/riz-build-cache-next/foundation-current-integration-20261114/run-r2')
MANIFEST = ROOT / 'work/foundation-current-integration-next-20261114/candidate-manifest.json'
OUT = Path('D:/riz-build-cache-next/sdk-current-integration-20261114/run-r1')
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
    require(OUT.parent == Path('D:/riz-build-cache-next/sdk-current-integration-20261114').resolve(), 'Use a fresh scoped run directory')
    require(not OUT.exists(), 'Preserve prior SDK runs')
    require(sha(MANIFEST) == '7b8cea0b1a3a5d7cf06c5821374a50e2433402d9e80c2f6383c1a48bc6dc18d4', 'Current source manifest differs')
    candidate = read(BASE / 'result.json'); declared = read(MANIFEST)
    require(candidate['controllerPid'] == 28536 and candidate['fullFrozenInputs'] == declared['expectedInputs']
        and len(candidate['fullFrozenInputs']) == 1171, 'Current source snapshot differs')
    require(DOTNET.is_file() and PWSH.is_file(), 'Known CLI tools are unavailable')
    expected = candidate['fullFrozenInputs']; extras = dict(candidate['auxiliaryInputs'])
    for name in ('scripts/Pack-Sdk.ps1', 'scripts/Test-SdkDistribution.ps1', 'packaging/sdk/README.md'):
        extras[name] = sha(ROOT / name)
    for path in (ROOT / 'templates/riz-plugin').rglob('*'):
        if path.is_file() and not any(part in ('bin', 'obj', '.git') for part in path.parts):
            extras[path.relative_to(ROOT).as_posix()] = sha(path)
    # Distribution.Tests is deliberately outside the foundation managed
    # matrix scanner. Freeze its two actual sources as additional inputs.
    for path in (ROOT / 'tests/RizToolbox.Distribution.Tests').rglob('*'):
        if path.is_file() and path.suffix in ('.cs', '.csproj', '.props', '.targets', '.json') and not any(part in ('bin', 'obj', '.git') for part in path.parts):
            extras[path.relative_to(ROOT).as_posix()] = sha(path)
    OUT.mkdir(parents=True); source = OUT / 'source'; controls = OUT / 'controls'; controls.mkdir()
    for name, digest in expected.items():
        target = source / name; target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(BASE / 'source' / name, target); require(sha(target) == digest, 'Frozen compiler source differs')
    for name, digest in extras.items():
        target = source / name; target.parent.mkdir(parents=True, exist_ok=True)
        if target.exists(): require(sha(target) == digest, 'Additional source conflicts with1171 snapshot')
        else: shutil.copyfile(BASE / 'source' / name if name in candidate['auxiliaryInputs'] else ROOT / name, target)
        require(sha(target) == digest, 'Additional distribution source differs')
    dependencies = {}; feed = OUT / 'dependency-feed'; feed.mkdir()
    for name in ('microsoft.netcore.app.ref', 'microsoft.aspnetcore.app.ref', 'microsoft.windowsdesktop.app.ref', 'microsoft.netcore.app.host.win-x64'):
        package = ROOT.drive + '/Users/71957/.nuget/packages/' + name + '/6.0.36/' + name + '.6.0.36.nupkg'
        path = Path(package); require(path.is_file(), 'Required cached framework package is unavailable: ' + name)
        target = feed / path.name; shutil.copyfile(path, target); require(sha(path) == sha(target), 'Cached dependency changed')
        dependencies[path.name] = dict(original=str(path), sha256=sha(target))
    config = source / 'NuGet.Config'; require(not config.exists(), 'Preserve an existing snapshot config')
    config.write_text('<configuration><packageSources><clear/><add key="FrameworkCache" value="' + escape(str(feed)) + '"/></packageSources></configuration>', encoding='utf-8')
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
    shutil.copyfile(WORK / 'source-review.md', controls / 'source-review.md')
    env = os.environ.copy()
    env.update(DOTNET_GENERATE_ASPNET_CERTIFICATE='false', DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false',
        DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1', DOTNET_NOLOGO='1')
    state = dict(schema='sdk-candidate-distribution-checks/v1', controllerPid=os.getpid(), startedUtc=now(), passed=False,
        candidateOnly=True, mainApplied=False, compilerInputs=1171, frozenInputs=expected, additionalInputs=extras,
        sourceManifestSha256=sha(MANIFEST), baseCurrentControllerPid=28536, baseCurrentValidationAccepted=False, dependencies=dependencies, offlineRunnerSha256=sha(offline),
        originalRunnerSha256=extras['scripts/Test-SdkDistribution.ps1'], steps=[], sdkGoalComplete=False,
        additionalCompilerInputs=2, fullManagedMatrix=False, runtimeGameVerification=False,
        templateEngineIsolated=True, installed=False, gameStarted=False)
    save(OUT / 'result.json', state); print('controllerPid=' + str(os.getpid()), flush=True)

    def stable():
        return all(sha(source / name) == digest for name, digest in expected.items()) and all(sha(source / name) == digest for name, digest in extras.items())

    def run(name, argv, expected_passes=None):
        require(stable(), 'Frozen source changed before ' + name)
        row = dict(id=name, argv=list(map(str, argv)), startedUtc=now(), naturalExit=False)
        state['steps'].append(row); save(OUT / 'result.json', state); log = OUT / (name + '.log')
        with log.open('xb') as stream:
            child = subprocess.Popen(row['argv'], cwd=source, env=env, stdin=subprocess.DEVNULL, stdout=stream, stderr=subprocess.STDOUT)
            row['pid'] = child.pid; save(OUT / 'result.json', state); row['exitCode'] = child.wait(); row['naturalExit'] = True
        body = log.read_text(encoding='utf-8-sig', errors='replace')
        row.update(finishedUtc=now(), logSha256=sha(log), passNames=re.findall(r'^PASS (.+)$', body, re.M), failNames=re.findall(r'^FAIL (.+)$', body, re.M))
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
