"""Check an already extracted fixed-PE constructor contract; no native code is executed."""
from pathlib import Path
from datetime import datetime, timezone
import argparse
import hashlib
import json
import pefile

parser = argparse.ArgumentParser()
parser.add_argument('--output', required=True)
args = parser.parse_args()
root = Path(__file__).resolve().parents[3]
output = Path(args.output).resolve()
if output.exists():
    raise SystemExit('Use a fresh evidence directory.')
output.mkdir(parents=True)
digest = lambda raw: hashlib.sha256(raw).hexdigest()
previous = root / 'docs/validation/foundation-initialization-coroutines-20261023'
proof = json.loads((previous / 'coroutine-evidence.json').read_text(encoding='utf-8'))
game = Path('D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll')
dump = Path('C:/Users/71957/Desktop/绿豆诡计v2/output/il2cpp_output/dump.cs')
raw, dump_raw = game.read_bytes(), dump.read_bytes()
assert digest(raw) == proof['inputs']['gameAssemblySha256']
assert digest(dump_raw) == proof['inputs']['dumpSha256']
scope = next(row for row in proof['ranges'] if int(row['start'], 16) == 0x377890)
pe = pefile.PE(data=raw, fast_load=True)
body = pe.get_data(0x377890, 0x90)
assert digest(body) == scope['sha256'] == 'b098d64e184cd11738043d57d7602fe598b8bc6ca06364e88d58171f2c368bb9'
for instruction in scope['instructions']:
    data = bytes.fromhex(instruction['bytes'])
    assert pe.get_data(int(instruction['rva'], 16), len(data)) == data
lines = dump_raw.decode('utf-8').splitlines()
index = lines.index('public class UnityAction : MulticastDelegate')
assert '.ctor(System.Object object, System.IntPtr method); // RVA: 0x377890' in lines[index + 2]
selected = [row for row in scope['instructions'] if int(row['rva'], 16) in
            [0x377899, 0x3778a0, 0x3778a7, 0x3778ae, 0x3778dd, 0x3778e0, 0x3778e2, 0x3778e6, 0x3778ea, 0x3778ee]]
assert len(selected) == 10
consumers = []
for file, start, end in [
    ('NativeLocalInitializationCoroutineBackend.cs', '    private void RequireDelegate(', '    public void RetainCoroutine('),
    ('NativeLocalCutoutCoroutineBackend.cs', '    private void RequireResetAction(', '    public void RetainCoroutine('),
]:
    path = root / 'src/RizToolbox.Adapter/AudioContent' / file
    text = path.read_text(encoding='utf-8')
    excerpt = text[text.index(start):text.index(end, text.index(start))]
    assert all('"' + field + '"' in excerpt for field in ['method_ptr', 'invoke_impl', 'method_code', 'm_target', 'method', 'delegates', 'method_is_virtual'])
    consumers.append({'path': path.relative_to(root).as_posix(), 'sourceSha256': digest(path.read_bytes()), 'excerpt': excerpt})
result = {'observedAtUtc': datetime.now(timezone.utc).isoformat(), 'passed': True,
          'kind': 'fixed-PE-existing-closed-UnityAction-constructor-contract',
          'gameExecuted': False, 'runtimeDelegateObjectsVerified': False,
          'game': str(game), 'gameSha256': digest(raw), 'dump': str(dump), 'dumpSha256': digest(dump_raw),
          'previousEvidence': str(previous / 'coroutine-evidence.json'),
          'previousEvidenceSha256': digest((previous / 'coroutine-evidence.json').read_bytes()),
          'constructorRva': '0x377890', 'constructorBytes': len(body), 'constructorSha256': digest(body),
          'instructions': selected, 'unityActionDumpLine': index + 1, 'unityActionDeclaration': lines[index:index + 5],
          'existingProductionConsumers': consumers,
          'supportedSubset': 'Closed nonstatic direct singlecast: method_ptr=invoke_impl=exact entry, method_code=m_target=exact receiver. Other shapes stay Pending.'}
(output / 'delegate-contract.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print(json.dumps({'passed': True, 'constructorSha256': result['constructorSha256'], 'existingConsumers': len(consumers)}))
