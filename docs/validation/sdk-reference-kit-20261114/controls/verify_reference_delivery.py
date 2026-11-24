"""Unpack the delivered bytes and execute the documented isolated template flow."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).resolve().parent
OUT = Path('D:/riz-build-cache-next/sdk-current-hold-20261114/kit-smoke-r1')
DOTNET = 'C:/Program Files/dotnet/dotnet.exe'
receipt = json.loads((WORK / 'delivery-receipt.json').read_text(encoding='utf-8'))
package = Path(receipt['archive'])
sha = lambda path: hashlib.sha256(Path(path).read_bytes()).hexdigest()
if OUT.exists(): raise RuntimeError('Preserve prior smoke results')
if sha(package) != receipt['sha256'] or not receipt['referenceKitOnly']: raise RuntimeError('Delivered ZIP differs')
OUT.mkdir(parents=True)
with zipfile.ZipFile(package) as archive:
    for entry in archive.infolist():
        target = OUT / entry.filename
        if not target.resolve().is_relative_to(OUT.resolve()): raise RuntimeError('Archive path escaped output')
        target.parent.mkdir(parents=True, exist_ok=True); target.write_bytes(archive.read(entry))
kit = OUT / package.stem
manifest = json.loads((kit / 'manifest.json').read_text(encoding='utf-8'))
if not all(sha(kit / name) == row['sha256'] for name, row in manifest['files'].items()): raise RuntimeError('Extracted files differ')
env = os.environ.copy()
env.update(DOTNET_GENERATE_ASPNET_CERTIFICATE='false', DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false',
    DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1', DOTNET_NOLOGO='1',
    NUGET_PACKAGES=str(OUT / 'nuget-cache'))
state = dict(schema='sdk-reference-delivery-smoke/v1', controllerPid=os.getpid(), passed=False,
    deliverySha256=sha(package), steps=[], gameStarted=False, installed=False, sdkGoalComplete=False)


def save(): (OUT / 'result.json').write_text(json.dumps(state, indent=2) + '\n', encoding='utf-8')


plan = [
    ('isolated-template', [DOTNET, 'new', 'install', kit / 'template/riz-plugin', '--debug:custom-hive', kit / '.template-hive']),
    ('generate-project', [DOTNET, 'new', 'riz-plugin', '--name', 'MyMod', '--PluginId', 'author.mymod', '--output', kit / 'MyMod', '--debug:custom-hive', kit / '.template-hive']),
    ('offline-restore', [DOTNET, 'restore', kit / 'MyMod/MyMod.csproj', '--configfile', kit / 'NuGet.Config', '-p:NuGetAudit=false']),
    ('release-build', [DOTNET, 'build', kit / 'MyMod/MyMod.csproj', '-c', 'Release', '--no-restore', '--disable-build-servers', '-m:1', '-p:UseSharedCompilation=false'])]
save(); print('controllerPid=' + str(os.getpid()), flush=True)
try:
    for name, argv in plan:
        row = dict(id=name, argv=list(map(str, argv)), naturalExit=False); state['steps'].append(row); save()
        log = OUT / (name + '.log')
        with log.open('xb') as stream:
            child = subprocess.Popen(row['argv'], cwd=kit, env=env, stdin=subprocess.DEVNULL, stdout=stream, stderr=subprocess.STDOUT)
            row['pid'] = child.pid; save(); row['exitCode'] = child.wait(); row['naturalExit'] = True
        row['logSha256'] = sha(log); save()
        if row['exitCode'] != 0: raise RuntimeError('Delivered flow failed: ' + name)
        print(name + ': exit0', flush=True)
    binary = kit / 'MyMod/bin/Release/net6.0/MyMod.dll'
    if not binary.is_file() or list(binary.parent.glob('RizToolbox.*.dll')): raise RuntimeError('Compiled template missing or copied host runtime')
    restored = OUT / 'nuget-cache/riztoolbox.sdk/1.2.0/riztoolbox.sdk.1.2.0.nupkg'
    if sha(restored) != manifest['packageSha256']: raise RuntimeError('The delivered package was not the actual restored SDK')
    state.update(passed=True, pluginSha256=sha(binary), manifestInputsUnchanged=all(sha(kit / name) == row['sha256'] for name, row in manifest['files'].items()))
    if not state['manifestInputsUnchanged']: raise RuntimeError('Delivered source changed')
except Exception as error:
    state.update(passed=False, failure=str(error)); raise
finally:
    save(); print(json.dumps({name:state.get(name) for name in ('passed', 'failure')}), flush=True)
