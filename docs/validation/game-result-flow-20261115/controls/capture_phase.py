"""Capture one read-only phase of the exact installed candidate's new game session."""
import argparse
import ctypes
from ctypes import wintypes
import datetime as dt
import hashlib
import json
from pathlib import Path
import re


GAME = Path('D:/SteamLibrary/steamapps/common/Rizline')
CACHE = Path('D:/riz-build-cache-next/runtime-tick-admission-20261115')
OUT = CACHE / 'runtime-r3'
PID = 25008
SESSION = '20261115T051638374Z-892aaffc8645488f9462136b3308845c'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
read = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))


def require(value, reason):
    if not value:
        raise RuntimeError(reason)


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('phase')
parser.add_argument('--ui-observation', required=True)
args = parser.parse_args()
require(re.fullmatch('[a-z][a-z0-9-]{0,60}', args.phase), 'Invalid phase name')
phase = OUT / 'phases' / args.phase
require(not phase.exists(), 'Preserve previous phase observations')
kernel = ctypes.WinDLL('kernel32', use_last_error=True)
kernel.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
kernel.OpenProcess.restype = wintypes.HANDLE
kernel.GetExitCodeProcess.argtypes = [wintypes.HANDLE, ctypes.POINTER(wintypes.DWORD)]
kernel.QueryFullProcessImageNameW.argtypes = [wintypes.HANDLE, wintypes.DWORD, wintypes.LPWSTR, ctypes.POINTER(wintypes.DWORD)]
kernel.CloseHandle.argtypes = [wintypes.HANDLE]
handle = kernel.OpenProcess(0x1000, False, PID)
require(handle, 'Expected current test process is absent')
try:
    code = wintypes.DWORD()
    require(kernel.GetExitCodeProcess(handle, ctypes.byref(code)) and code.value == 259, 'Current test process is no longer running')
    name = ctypes.create_unicode_buffer(32768)
    count = wintypes.DWORD(len(name))
    require(kernel.QueryFullProcessImageNameW(handle, 0, name, ctypes.byref(count)), 'Cannot identify current process')
    require(Path(name.value).resolve() == (GAME / 'Rizline.exe').resolve(), 'Test PID belongs to another image')
finally:
    kernel.CloseHandle(handle)

install_path = CACHE / 'install-r3/result.json'
install = read(install_path)
require(install['passed'] and install['naturalExit'] and install['actualInstallerExitCode'] == 0, 'Exact installation is not accepted')
require(all(sha(GAME / row['path']) == row['sha256'] for row in install['packageFiles']), 'Actual installed candidate changed')
require(sha(GAME / 'GameAssembly.dll') == install['gameAssemblySha256'], 'Original game identity changed')
status_bytes = (GAME / 'BepInEx/RizToolbox/status.json').read_bytes()
status = json.loads(status_bytes.decode('utf-8-sig'))
require(status['diagnostics']['SessionId'] == SESSION, 'Current diagnostic session differs')
combined = ''
for name in ('toolbox.log.3', 'toolbox.log.2', 'toolbox.log.1', 'toolbox.log'):
    path = GAME / 'BepInEx/RizToolbox' / name
    if path.is_file():
        combined += path.read_text(encoding='utf-8-sig')
lines = combined.splitlines()
starts = [i for i, line in enumerate(lines) if 'RizToolbox 0.3 starting;' in line and 'diagnostic session ' + SESSION + '.' in line]
require(len(starts) == 1, 'Exact current loader session is not retained')
lines = lines[starts[0]:]
lifecycle = [line for line in lines if 'Observed lifecycle ' in line]
native = [line for line in (GAME / 'BepInEx/RizToolbox/native-preinit.log').read_text(encoding='utf-8-sig').splitlines()
          if line.startswith('native-preinit-v1 pid=' + str(PID) + ' ')]
require(any('stage=ready stage_code=49 result=1' in line for line in native), 'Current native producer has no Ready proof')
modules = {row['Manifest']['Id']: row for row in status['adapterModules']}
if args.phase == 'settings-page':
    require(modules['rizline.settings.read']['State'] == 1 and modules['rizline.settings.override']['State'] == 1,
            'Settings services are not active on the actual settings page')
phase.mkdir(parents=True)
(phase / 'status.json').write_bytes(status_bytes)
(phase / 'toolbox-current-session.log').write_text('\n'.join(lines) + '\n', encoding='utf-8')
(phase / 'lifecycle.log').write_text('\n'.join(lifecycle) + '\n', encoding='utf-8')
(phase / 'native-current-process.log').write_text('\n'.join(native) + '\n', encoding='utf-8')
record = dict(schema='current-game-result-flow-phase/v1', observedUtc=dt.datetime.now(dt.timezone.utc).isoformat(),
              phase=args.phase, gameProcessId=PID, diagnosticSessionId=SESSION,
              actualProcessLiveAndImageMatched=True, actualInstalledFilesMatched=len(install['packageFiles']),
              actualInstallResultSha256=sha(install_path), gameAssemblyUnchanged=True,
              uiObservation=args.ui_observation, uiObservationSource='Root inspected actual sky.get_window_state output in the active turn',
              sdkVersion=status['sdkVersion'], statusRevision=status['revision'],
              moduleStates={key: value['State'] for key, value in modules.items()},
              lifecycleEvents={name: sum('Observed lifecycle ' + name + ':' in line for line in lifecycle)
                               for name in ('RunStarted', 'Paused', 'Resumed', 'RunEnded', 'SceneChanged', 'ObservationChanged')},
              currentSessionErrorLines=[line for line in lines if '[Error]' in line],
              files={p.name: sha(p) for p in phase.iterdir() if p.is_file()},
              gameSettingsModifiedByThisCapture=False, fullGameAcceptance=False, sdkGoalComplete=False)
(phase / 'observation.json').write_text(json.dumps(record, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print(json.dumps(dict(captured=args.phase, observationSha256=sha(phase / 'observation.json'),
                      lifecycle=record['lifecycleEvents'], errors=len(record['currentSessionErrorLines']),
                      fullGameAcceptance=False, sdkGoalComplete=False)))
