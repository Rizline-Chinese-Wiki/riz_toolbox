"""Strict evidence checks shared by the after-shutdown continuation and collector."""
from pathlib import Path
import ctypes
import datetime
import hashlib
import json

O = Path(__file__).resolve().parent
R = O.parent
E = R / 'after-shutdown-evidence-r1'
MAIN = Path('C:/Users/71957/Desktop/riz_toolbox')
B = R / 'positive-r3'
S = B / 'source'
OLD = R / 'formal-r2'
APP = S / 'tests/RizToolbox.PlaybackStartInitialization.Tests/bin/Release/net8.0/RizToolbox.Gameplay.Tests.dll'
DOTNET = Path('C:/Program Files/dotnet/dotnet.exe')
EXPECTED_MANIFEST = '88fe4b79d5f2c320429a50d754c839706cd914615cab19842ccbe89b34b40333'
EXPECTED_README = 'cfbac4691d36edc5daba513aa62d5856ae61e29bf553d0dc2979832750847364'
INPUT_DIGESTS = {
    'RizToolbox.PlaybackStockFixture.dll': '604ebbddb5c7fb19974b1b329a1a5bc6f43d10e6fe6ebfd0e77b6e9e59e186a3',
    'RizToolbox.SingleReturn.Prototype.dll': '21d888694b361ef38a99558d0590511385c04b77bc490aa17f4163274e318a52',
    'RizToolbox.PlaybackSourcePrototype.dll': '75a41e3e715e7025386a75a993e369e8e22aef3e66eb5de9becd5899a0424490',
    'dobby.dll': '8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded',
}
INPUTS = [B / 'inputs' / name for name in INPUT_DIGESTS]
BASE_CHECKS = {
    'player-sdk-normal': 28, 'direct-source-first': 102,
    'direct-source-same': 54, 'direct-source-cross': 57,
    'direct-sdk-foreign': 61, 'direct-sdk-worker': 37,
}
REMAINING = ['direct-sdk-' + kind + '-' + str(i)
             for kind in ('native', 'runtime') for i in range(6)]
ALL_MODES = list(BASE_CHECKS) + REMAINING
SUFFIXES = ('.stdout.log', '.stderr.log', '.launch.json', '.result.json', '.exit.txt')


def now():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def fact(path):
    p = Path(path)
    return {'path': str(p), 'sha256': sha(p), 'bytes': p.stat().st_size}


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def save(path, value):
    p = Path(path)
    q = p.with_suffix(p.suffix + '.writing')
    q.write_text(json.dumps(value, indent=2), encoding='utf-8')
    q.replace(p)


def verify_record(row):
    p = Path(row['path'])
    assert p.is_absolute() and p.is_file(), str(p)
    assert sha(p) == row['sha256'], ('sha256', str(p))
    if 'bytes' in row:
        assert p.stat().st_size == row['bytes'], ('bytes', str(p))


def in_job(handle=None):
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    kernel.GetCurrentProcess.restype = ctypes.c_void_p
    kernel.IsProcessInJob.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.POINTER(ctypes.c_int)]
    value = ctypes.c_int()
    h = kernel.GetCurrentProcess() if handle is None else int(handle)
    assert kernel.IsProcessInJob(h, None, ctypes.byref(value)), ctypes.get_last_error()
    return bool(value.value)


def power_status():
    class Power(ctypes.Structure):
        _fields_ = [('ACLineStatus', ctypes.c_ubyte), ('BatteryFlag', ctypes.c_ubyte),
                    ('BatteryLifePercent', ctypes.c_ubyte), ('SystemStatusFlag', ctypes.c_ubyte),
                    ('BatteryLifeTime', ctypes.c_uint32), ('BatteryFullLifeTime', ctypes.c_uint32)]
    value = Power()
    assert ctypes.windll.kernel32.GetSystemPowerStatus(ctypes.byref(value))
    return {name: getattr(value, name) for name, _ in value._fields_}


def command_for(mode):
    return ['C:/Program Files/dotnet/dotnet.exe', str(APP), mode, *(str(p) for p in INPUTS)]


def verify_row(row, directory, terminal=True):
    mode = row['mode']
    assert mode in ALL_MODES
    assert row['command'] == command_for(mode), (mode, 'command')
    launch = read(directory / (mode + '.launch.json'))
    assert all(row[k] == launch[k] for k in ('mode', 'command', 'pid', 'startedAtUtc')), (mode, 'launch provenance')
    assert launch['actualExitCode'] is None and launch['passed'] is False
    if 'inJob' in launch:
        assert launch['inJob'] == row['inJob'] is False
    if not terminal:
        return
    assert row['actualExitCode'] == 0 and row['passed'] is True
    assert row.get('finishedAtUtc')
    assert row['actual']['passed'] is True and row['actual']['mode'] == mode
    assert row['actual']['checks'] > 0
    assert read(directory / (mode + '.result.json')) == row
    assert (directory / (mode + '.exit.txt')).read_text().strip() == '0'
    for kind in ('stdout', 'stderr'):
        assert Path(row[kind]['path']) == directory / (mode + '.' + kind + '.log')
        verify_record(row[kind])
    actual = json.loads(Path(row['stdout']['path']).read_text(encoding='utf-8-sig').strip().splitlines()[-1])
    assert actual == row['actual'], (mode, 'stdout actual')


def verify_original():
    old = read(OLD / 'test-result.json')
    assert old['runnerPid'] == 4104
    assert old['startedAtUtc'] == '2026-11-02T13:44:07.145028+00:00'
    assert old['lastObservedAliveUtc'] == '2026-11-02T16:23:51.611281+00:00'
    assert not old.get('finishedAtUtc')
    assert len(old['rows']) == 9
    completed = [row for row in old['rows'] if row.get('actualExitCode') is not None]
    assert {row['mode'] for row in completed} == set(BASE_CHECKS)
    for row in completed:
        verify_row(row, OLD)
        assert row['actual']['checks'] == BASE_CHECKS[row['mode']]
    interrupted = [row for row in old['rows'] if row.get('actualExitCode') is None]
    assert [row['mode'] for row in interrupted] == REMAINING[:3]
    for row in interrupted:
        verify_row(row, OLD, terminal=False)
        assert not (OLD / (row['mode'] + '.exit.txt')).exists()
        assert not (OLD / (row['mode'] + '.result.json')).exists()
    assert old['pending'] == REMAINING[3:]
    assert old['sourceManifest']['sha256'] == EXPECTED_MANIFEST
    assert old['parallelLimit'] == 3
    assert all(old[k] is False for k in ('gameExecuted', 'installed', 'productionAdmission', 'fullBCompleted'))
    return old, completed, interrupted


def verify_frozen():
    old, completed, interrupted = verify_original()
    manifest_path = B / 'source-manifest.json'
    assert sha(manifest_path) == EXPECTED_MANIFEST
    manifest = read(manifest_path)
    candidate = read(R / 'candidate-review-r1/manifest.json')
    assert candidate['passed'] is True
    assert len(manifest['files']) == len(candidate['sourceFiles']) == 894
    identity = lambda rows: {r['path']: (r['sha256'], r['bytes']) for r in rows}
    assert identity(manifest['files']) == identity(candidate['sourceFiles'])
    source_checks = []
    for row in manifest['files']:
        for label, root in [('SDK-frozen-source', S), ('current-main', MAIN)]:
            current = {'path': str(root / row['path']), 'sha256': row['sha256'], 'bytes': row['bytes']}
            verify_record(current)
            source_checks.append({'origin': label, **current})
    assert sha(MAIN / 'README.md') == candidate['mainReadmeBefore']['sha256'] == EXPECTED_README
    assert candidate['productInputs'] == 482 and candidate['productCSharp'] == 475
    assert read(B / 'build.result.json')['actualExitCode'] == 0
    products = read(B / 'build-products.json')
    assert products == old['products'] and len(products) == 14
    for row in products:
        verify_record(row)
        assert Path(row['path']).parent == APP.parent
    native = [fact(p) for p in INPUTS]
    assert {Path(r['path']).name: r['sha256'] for r in native} == INPUT_DIGESTS
    assert native == old['nativeInputs']
    for p in INPUTS[1:3]:
        assert sha(R / 'native-r3' / p.name) == sha(p)
    assert APP.is_file() and DOTNET.is_file()
    return {'passed': True, 'observedAtUtc': now(), 'sourceFiles': 894,
            'productInputs': 482, 'productCSharp': 475,
            'sourceManifest': fact(manifest_path),
            'buildProductsManifest': fact(B / 'build-products.json'),
            'candidateManifest': fact(R / 'candidate-review-r1/manifest.json'),
            'mainReadme': fact(MAIN / 'README.md'),
            'products': products, 'nativeInputs': native,
            'sourceVerificationRows': source_checks,
            'originalCompletedModes': list(BASE_CHECKS), 'originalCompletedChecks': 339,
            'interruptedModes': [row['mode'] for row in interrupted],
            'originalPendingModes': old['pending'], 'remainingModes': REMAINING,
            'gameExecuted': False, 'installed': False, 'productionAdmission': False, 'fullBCompleted': False}


def verify_preserved():
    preflight = read(E / 'preflight-result-r3.json')
    assert preflight['passed'] is True
    for row in preflight['preservedOriginalFiles']:
        verify_record(row['original'])
        verify_record(row['retained'])
        assert row['original']['sha256'] == row['retained']['sha256']
    for row in preflight['rawEvidence']:
        verify_record(row)
    return preflight
