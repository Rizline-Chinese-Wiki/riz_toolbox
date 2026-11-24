"""Read-only B13 evidence. Writes selected metadata/assembly facts only under --output.
No game process is started, attached, or modified. Requires existing UnityPy, pefile, capstone.
"""
from __future__ import annotations
import argparse
import bisect
import hashlib
import importlib.metadata
import json
from pathlib import Path
from datetime import datetime, timezone
import UnityPy
import pefile
import capstone


def sha(path: Path) -> str:
    h = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument('--data', type=Path, default=Path(r'D:\SteamLibrary\steamapps\common\Rizline\Rizline_Data'))
    parser.add_argument('--assembly', type=Path, default=Path(r'D:\SteamLibrary\steamapps\common\Rizline\GameAssembly.dll'))
    parser.add_argument('--dump', type=Path, default=Path(r'C:\Users\71957\Desktop\绿豆诡计v2\output\il2cpp_output\dump.cs'))
    parser.add_argument('--script', type=Path, default=Path(r'C:\Users\71957\Desktop\绿豆诡计v2\output\il2cpp_output\script.json'))
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    evidence = {
        'schemaVersion': 1, 'capturedAtUtc': datetime.now(timezone.utc).isoformat(),
        'scope': 'Static read-only metadata and selected disassembly. No runtime validation, game launch, attach, or modification.',
        'tools': {x: importlib.metadata.version(x) for x in ('UnityPy', 'pefile', 'capstone')},
        'inputs': [], 'dumpApiEvidence': {}, 'resourceRoutes': [], 'shaders': [], 'materials': [],
        'sceneBindings': [], 'renderers': [], 'cameras': [], 'nativeMethods': [],
    }
    asset_names = ['globalgamemanagers', 'globalgamemanagers.assets', 'resources.assets', 'sharedassets0.assets',
                   'sharedassets2.assets', 'sharedassets6.assets', 'level1', 'level2', 'level6', 'Resources/unity_builtin_extra']
    for path in [args.assembly, args.dump, args.script] + [args.data / x for x in asset_names]:
        evidence['inputs'].append({'path': str(path.resolve()), 'bytes': path.stat().st_size, 'sha256': sha(path)})
    lines = args.dump.read_text(encoding='utf-8').splitlines()
    class_blocks = {}
    namespace = ''
    for index, line in enumerate(lines):
        if line.startswith('// Namespace: '): namespace = line.split(': ', 1)[1]
        if line.startswith('public class '):
            name = line.split('public class ', 1)[1].split(' : ', 1)[0]
            end = next((i for i in range(index + 1, len(lines)) if lines[i] == '}'), index)
            class_blocks[namespace + '.' + name] = (index, end)
    method_queries = {
        'UnityEngine.Material': ['HasFloat(', 'HasInteger(', 'HasVector(', 'HasColor(', 'HasProperty(', 'GetFloat(', 'SetFloat(', 'GetColor(', 'SetColor(', 'get_shader(', 'ComputeCRC('],
        'UnityEngine.Shader': ['GetPropertyCount(', 'GetPropertyType(', 'FindPropertyIndex(', 'Find(', 'PropertyToID('],
        'UnityEngine.Resources': ['Load(', 'GetBuiltinResource('],
        'UnityEngine.SpriteRenderer': ['get_sprite(', 'set_sprite(', 'get_color(', 'set_color('],
        'UnityEngine.LineRenderer': ['get_startColor(', 'get_endColor(', 'set_startColor(', 'set_endColor(', 'get_widthMultiplier(', 'set_widthMultiplier('],
        'UnityEngine.Camera': ['get_main(', 'get_backgroundColor(', 'set_backgroundColor(', 'get_orthographicSize(', 'set_orthographicSize('],
        'LeTai.Asset.TranslucentImage.TranslucentImageSource': ['get_BlurConfig(', 'set_BlurConfig(', 'get_Downsample(', 'set_Downsample(', 'OnRenderImage('],
    }
    for cls, queries in method_queries.items():
        begin, end = class_blocks[cls]
        evidence['dumpApiEvidence'][cls] = {q: [{'line': i + 1, 'signature': lines[i].strip()} for i in range(begin, end + 1) if q in lines[i]] for q in queries}
    selected_shader_ids = {'resources.assets': {19, 20}, 'sharedassets0.assets': {11}, 'sharedassets2.assets': {95}, 'globalgamemanagers.assets': {5}, 'Resources/unity_builtin_extra': {10753, 10757, 10770}}
    selected_material_ids = {'resources.assets': {1, 2}, 'sharedassets2.assets': {4, 5}, 'sharedassets6.assets': {6, 7, 8}}
    selected_props = {'_FaceColor', '_OutlineColor', '_OutlineWidth', '_FaceDilate', '_Vibrancy', '_Brightness', '_Flatten', '_Color', '_BaseFill', '_Cutoff'}
    resource_keys = {'translucentimage', 'fonts & materials/liberationsans sdf - outline', 'shaders/shadowpostprocess'}
    for name in asset_names:
        env = UnityPy.load(str(args.data / name))
        for obj in env.objects:
            if obj.type.name == 'BuildSettings':
                tree = obj.read_typetree()
                evidence['buildScenes'] = tree['scenes']  # Deliberately exclude unrelated build fields.
                evidence['unityVersion'] = tree['m_Version']
            elif obj.type.name == 'ResourceManager':
                tree = obj.read_typetree()
                for key, pointer in tree['m_Container']:
                    if key in resource_keys:
                        external = obj.assets_file.externals[pointer['m_FileID'] - 1].path if pointer['m_FileID'] else name
                        evidence['resourceRoutes'].append({'key': key, 'ownerFile': name, 'ownerPathId': obj.path_id, 'pointer': pointer, 'resolvedFile': external})
            elif obj.type.name == 'Shader' and obj.path_id in selected_shader_ids.get(name, set()):
                tree = obj.read_typetree(); form = tree['m_ParsedForm']
                props = []
                for prop in form['m_PropInfo']['m_Props']:
                    if prop['m_Name'] not in selected_props: continue
                    props.append({'name': prop['m_Name'], 'serializedType': prop['m_Type'], 'flags': prop['m_Flags'],
                                  'defaultVector': [prop[f'm_DefValue[{i}]'] for i in range(4)], 'attributes': prop['m_Attributes']})
                evidence['shaders'].append({'file': name, 'pathId': obj.path_id, 'nameForDisplayOnly': form['m_Name'],
                    'serializedObjectSha256': hashlib.sha256(obj.get_raw_data()).hexdigest(), 'selectedProperties': props})
            elif obj.type.name == 'Material' and obj.path_id in selected_material_ids.get(name, set()):
                tree = obj.read_typetree(); data = obj.read(); shader = data.m_Shader.deref(); st = shader.read_typetree()
                evidence['materials'].append({'file': name, 'pathId': obj.path_id, 'nameForDisplayOnly': tree['m_Name'],
                    'serializedObjectSha256': hashlib.sha256(obj.get_raw_data()).hexdigest(), 'shaderPointer': tree['m_Shader'],
                    'resolvedShaderFile': shader.assets_file.name, 'resolvedShaderPathId': shader.path_id,
                    'resolvedShaderNameForDisplayOnly': st['m_ParsedForm']['m_Name'],
                    'selectedSavedFloats': [(k, v) for k, v in tree['m_SavedProperties']['m_Floats'] if k in selected_props],
                    'selectedSavedColors': [(k, v) for k, v in tree['m_SavedProperties']['m_Colors'] if k in selected_props]})
            elif obj.type.name == 'MonoBehaviour' and name in {'level1', 'level2', 'level6', 'sharedassets6.assets'}:
                # Type trees for custom script bodies were stripped. Base header only; do not decode guessed script offsets.
                data = obj.read(check_read=False)
                try: script = data.m_Script.deref_parse_as_object()
                except Exception: continue
                if script.m_Namespace == 'InLevel' or script.m_ClassName in {'TranslucentImageSource'}:
                    game_object = data.m_GameObject.deref_parse_as_object() if data.m_GameObject.path_id else None
                    evidence['sceneBindings'].append({'file': name, 'pathId': obj.path_id, 'scriptNamespace': script.m_Namespace,
                        'scriptClass': script.m_ClassName, 'scriptAssembly': script.m_AssemblyName,
                        'gameObjectPathId': data.m_GameObject.path_id, 'gameObjectNameForDisplayOnly': getattr(game_object, 'm_Name', None),
                        'customSerializedFieldsDecoded': False})
            elif obj.type.name in {'SpriteRenderer', 'LineRenderer'} and name == 'sharedassets6.assets':
                tree = obj.read_typetree()
                evidence['renderers'].append({'file': name, 'pathId': obj.path_id, 'type': obj.type.name,
                    'gameObject': tree['m_GameObject'], 'materials': tree['m_Materials'],
                    'sprite': tree.get('m_Sprite'), 'color': tree.get('m_Color')})
            elif obj.type.name == 'Camera' and name == 'level6':
                tree = obj.read_typetree()
                evidence['cameras'].append({'file': name, 'pathId': obj.path_id, 'gameObject': tree['m_GameObject'],
                    'orthographic': tree['orthographic'], 'orthographicSize': tree['orthographic size'], 'backgroundColor': tree['m_BackGroundColor']})
    methods = json.loads(args.script.read_text(encoding='utf-8'))['ScriptMethod']
    by_name = {m['Name']: m for m in methods}; labels = {}
    for item in methods: labels.setdefault(item['Address'], []).append(item['Name'])
    starts = sorted(labels)
    pe = pefile.PE(str(args.assembly), fast_load=True)
    pe.parse_data_directories(directories=[pefile.DIRECTORY_ENTRY['IMAGE_DIRECTORY_ENTRY_EXCEPTION']])
    unwind = {x.struct.BeginAddress: x.struct.EndAddress for x in pe.DIRECTORY_ENTRY_EXCEPTION}
    md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64); md.detail = True
    chosen = ['InLevel.CameraControl$$SetScale', 'InLevel.CameraControl$$SetXPosition', 'InLevel.CameraControl$$Update',
              'InLevel.LevelControl$$Start', 'InLevel.LineInformation$$set_startColor', 'InLevel.LineInformation$$set_endColor',
              'InLevel.LinesGroupInformation$$set_color', 'InLevel.EasyPoolChild$$Reset', 'InLevel.NoteInformation$$Appear',
              'LeTai.Asset.TranslucentImage.TranslucentImage$$SyncMaterialProperty', 'LeTai.Asset.TranslucentImage.TranslucentImage$$Update',
              'LeTai.Asset.TranslucentImage.TranslucentImageSource$$OnRenderImage']
    for name in chosen:
        if name not in by_name: continue
        rva = by_name[name]['Address']; next_index = bisect.bisect_right(starts, rva)
        # PE unwind records describe code fragments, not necessarily complete IL2CPP methods.
        # CameraControl.SetScale/SetXPosition each span more than one unwind fragment.
        end = starts[next_index] if next_index < len(starts) else rva + 8192
        size = min(end - rva, 32768); instructions = list(md.disasm(pe.get_data(rva, size), pe.OPTIONAL_HEADER.ImageBase + rva))
        calls = []; snippets = []
        for index, ins in enumerate(instructions):
            if ins.mnemonic not in {'call', 'jmp'}: continue
            operand = ins.operands[0]
            called_rva = operand.imm - pe.OPTIONAL_HEADER.ImageBase if operand.type == capstone.x86.X86_OP_IMM else None
            called_names = labels.get(called_rva, [])
            if ins.mnemonic == 'jmp' and not called_names: continue
            calls.append({'rva': hex(ins.address-pe.OPTIONAL_HEADER.ImageBase), 'instruction': ins.mnemonic, 'targetRva': hex(called_rva) if called_rva is not None else None, 'targetNames': called_names[:8], 'indirectOperand': ins.op_str if called_rva is None else None})
            if any(n.startswith(('UnityEngine.SpriteRenderer$$', 'UnityEngine.LineRenderer$$', 'UnityEngine.Transform$$', 'UnityEngine.Material$$')) for n in called_names):
                snippets.append([{'rva': hex(x.address-pe.OPTIONAL_HEADER.ImageBase), 'mnemonic': x.mnemonic, 'operands': x.op_str} for x in instructions[max(0,index-5):index+2]])
        evidence['nativeMethods'].append({'name': name, 'rva': hex(rva), 'endRva': hex(end),
            'boundarySource': 'next distinct ScriptMethod RVA, capped at 32768 bytes; PE unwind fragments listed separately',
            'peUnwindFragments': [{'beginRva': hex(a), 'endRva': hex(b)} for a, b in unwind.items() if rva <= a < end],
            'bytesInspected': size, 'calls': calls, 'selectedCallsiteSnippets': snippets})
    destination = args.output / 'static-profile-evidence.json'
    destination.write_text(json.dumps(evidence, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({'output': str(destination), 'sha256': sha(destination), 'shaderProfiles': len(evidence['shaders']),
                      'resourceRoutes': len(evidence['resourceRoutes']), 'sceneBindings': len(evidence['sceneBindings']),
                      'nativeMethods': len(evidence['nativeMethods'])}, ensure_ascii=False))


if __name__ == '__main__': main()
