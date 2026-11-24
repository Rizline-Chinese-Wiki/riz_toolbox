"""Extract the new kit and execute its four documented commands with a new local cache."""
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).resolve().parent
OUT = Path('D:/riz-build-cache-next/sdk-startup-kit-20261115/smoke-r1')
DOTNET = 'C:/Program Files/dotnet/dotnet.exe'
sha = lambda p: hashlib.sha256(Path(p).read_bytes()).hexdigest()
read = lambda p: json.loads(Path(p).read_text(encoding='utf-8-sig'))
now = lambda: dt.datetime.now(dt.timezone.utc).isoformat()


def require(value, reason):
    if not value:
        raise RuntimeError(reason)


receipt = read(WORK / 'delivery-receipt.json')
package = Path(receipt['zip'])
require(not OUT.exists() and sha(package) == receipt['sha256'], 'Preserve prior smoke or delivered ZIP changed')
OUT.mkdir(parents=True)
kit = OUT / 'kit'
kit.mkdir()
with zipfile.ZipFile(package) as archive:
    require(archive.testzip() is None and len(archive.namelist()) == len(set(archive.namelist())), 'Invalid ZIP')
    for name in archive.namelist():
        target = kit / name
        require(target.resolve().is_relative_to(kit.resolve()) and not target.exists()
                and not name.startswith('/') and '..' not in Path(name).parts and '\\' not in name, 'ZIP path escaped')
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(archive.read(name))
manifest = read(kit / 'manifest.json')
require(set(p.relative_to(kit).as_posix() for p in kit.rglob('*') if p.is_file()) == set(manifest['files']) | {'manifest.json'}
        and all(sha(kit / name) == row['sha256'] for name, row in manifest['files'].items()), 'Extracted kit differs')
env = os.environ.copy()
env.update(DOTNET_GENERATE_ASPNET_CERTIFICATE='false', DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false',
           DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1', DOTNET_NOLOGO='1')
state = dict(schema='current-startup-sdk-reference-kit-smoke/v1', controllerPid=os.getpid(), startedUtc=now(), passed=False,
             deliverySha256=receipt['sha256'], manifestSha256=sha(kit / 'manifest.json'), steps=[],
             childEnvironmentOverrides=sorted(key for key in env if env.get(key) != os.environ.get(key)),
             gameStarted=False, installedIntoGame=False, globalTemplateHiveModified=False, sdkGoalComplete=False)


def save():
    temporary = OUT / 'result.pending.json'
    temporary.write_text(json.dumps(state, indent=2) + '\n', encoding='utf-8')
    os.replace(temporary, OUT / 'result.json')


plan = [
    ('isolated-template', [DOTNET, 'new', 'install', kit / 'template/riz-plugin', '--debug:custom-hive', kit / '.template-hive']),
    ('generate-project', [DOTNET, 'new', 'riz-plugin', '--name', 'MyMod', '--PluginId', 'author.mymod', '--output', kit / 'MyMod', '--debug:custom-hive', kit / '.template-hive']),
    ('offline-restore', [DOTNET, 'restore', kit / 'MyMod/MyMod.csproj', '--configfile', kit / 'NuGet.Config', '--packages', kit / '.nuget-cache', '-p:NuGetAudit=false']),
    ('release-build', [DOTNET, 'build', kit / 'MyMod/MyMod.csproj', '-c', 'Release', '--no-restore', '--disable-build-servers', '-m:1', '-p:UseSharedCompilation=false']),
]
save()
print('controllerPid=' + str(os.getpid()), flush=True)
try:
    for name, argv in plan:
        row = dict(id=name, argv=list(map(str, argv)), startedUtc=now(), naturalExit=False)
        state['steps'].append(row)
        save()
        startup = subprocess.STARTUPINFO()
        startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
        startup.wShowWindow = 0
        with (OUT / (name + '.log')).open('xb') as stream:
            child = subprocess.Popen(row['argv'], cwd=kit, env=env, stdin=subprocess.DEVNULL,
                                     stdout=stream, stderr=subprocess.STDOUT,
                                     startupinfo=startup, creationflags=subprocess.CREATE_NO_WINDOW)
            row['pid'] = child.pid
            save()
            row['exitCode'] = child.wait()
            row['naturalExit'] = True
        row.update(finishedUtc=now(), logSha256=sha(OUT / (name + '.log')))
        save()
        require(row['exitCode'] == 0, 'Delivered command failed: ' + name)
        print(name + ': actual0', flush=True)
    binary = kit / 'MyMod/bin/Release/net6.0/MyMod.dll'
    require(binary.is_file() and not list(binary.parent.glob('RizToolbox.*.dll')), 'Plugin absent or host DLL copied')
    require(sha(kit / '.nuget-cache/riztoolbox.sdk/1.2.0/riztoolbox.sdk.1.2.0.nupkg') == manifest['sdkPackageSha256'],
            'The exact delivered SDK was not used by restore')
    require(all(sha(kit / name) == row['sha256'] for name, row in manifest['files'].items()), 'Delivered inputs changed')
    plugin = read(kit / 'MyMod/plugin.json')
    require(plugin['id'] == 'author.mymod' and plugin['entryType'] == 'MyMod.Plugin'
            and plugin['entryAssembly'] == 'MyMod.dll', 'Template identities differ')
    state.update(passed=True, pluginSha256=sha(binary), pluginManifestSha256=sha(kit / 'MyMod/plugin.json'),
                 restoredSdkSha256=manifest['sdkPackageSha256'], allDeliveredInputsUnchanged=True)
except Exception as error:
    state.update(passed=False, failure=str(error))
    raise
finally:
    state['finishedUtc'] = now()
    save()
    print(json.dumps({key: state.get(key) for key in ('passed', 'failure', 'pluginSha256')}), flush=True)
