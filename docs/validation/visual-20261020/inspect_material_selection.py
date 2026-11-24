"""Read-only material-target reachability research. No game launch, attach, or dotnet.

Custom MonoBehaviour type trees are stripped. We record exact base-script/component
identity and GameObject hierarchy; raw serialized PPtr matches remain candidates,
never mislabeled as a decoded m_Material/fontSharedMaterial field.
"""
from __future__ import annotations
import argparse
import collections
import hashlib
import importlib.metadata
import json
import re
import struct
from datetime import datetime, timezone
from pathlib import Path
import UnityPy
import capstone
import pefile


def sha(path):
    h = hashlib.sha256()
    with Path(path).open('rb') as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b''): h.update(chunk)
    return h.hexdigest()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--data', type=Path, default=Path(r'D:\SteamLibrary\steamapps\common\Rizline\Rizline_Data'))
    ap.add_argument('--dump', type=Path, default=Path(r'C:\Users\71957\Desktop\绿豆诡计v2\output\il2cpp_output\dump.cs'))
    ap.add_argument('--output', type=Path, default=Path(__file__).parent / 'material-live-selection-evidence.json')
    a = ap.parse_args()
    assembly = a.data.parent / 'GameAssembly.dll'
    installed = [a.data / x for x in ['globalgamemanagers', 'globalgamemanagers.assets', 'resources.assets']]
    installed += sorted(a.data.glob('sharedassets*.assets')) + sorted(a.data.glob('level[0-9]'))
    ev = dict(schemaVersion=1, capturedAtUtc=datetime.now(timezone.utc).isoformat(),
              scope='Static installed-file/base-script/hierarchy/disassembly review only. No native execution or UI.',
              tools={k: importlib.metadata.version(k) for k in ('UnityPy', 'pefile', 'capstone')},
              inputs=[dict(path=str(p), bytes=p.stat().st_size, sha256=sha(p)) for p in [assembly, a.dump] + installed],
              limitations=['Custom MonoBehaviour type trees are stripped. Raw PPtr matches below do not prove the serialized field name.',
                           'Static active hierarchy is not the current runtime active state. Runtime order and target-cap retention are unobserved.',
                           'No shader-name or hierarchy-name match grants permission to write a material.'],
              sceneSummary=[], components=[], profileMaterials=[], nativeMethods=[], dumpInheritance=[])
    all_materials=[]
    classes = {}; methods = {}; namespace = ''; current = None
    for line_no, line in enumerate(a.dump.read_text(encoding='utf-8').splitlines(), 1):
        if line.startswith('// Namespace: '): namespace = line[len('// Namespace: '):]
        m = re.match(r'public class ([^ :]+)(?: : ([^\r\n]+))?', line)
        if m:
            current = (namespace + '.' if namespace else '') + m[1]
            classes[current] = dict(parent=m[2], line=line_no)
            methods[current] = []
        if current and '// RVA:' in line: methods[current].append(dict(line=line_no, signature=line.strip()))

    def derived(name, base):
        seen = set()
        while name and name not in seen:
            if name == base: return True
            seen.add(name); item = classes.get(name)
            if not item: return False
            parent = item['parent']
            if parent in classes: name = parent
            else:
                ns = name.rpartition('.')[0]
                guess = ns + '.' + parent if ns and parent else parent
                if guess in classes: name = guess
                else:
                    candidates = [k for k in classes if k.rsplit('.', 1)[-1] == parent]
                    if len(candidates) != 1: return None
                    name = candidates[0]
        return False

    for cl in ['TMPro.TextMeshProUGUI', 'TMPro.TMP_Text', 'UnityEngine.UI.MaskableGraphic',
               'UnityEngine.UI.Graphic', 'UnityEngine.UI.Image', 'LeTai.Asset.TranslucentImage.TranslucentImage']:
        ev['dumpInheritance'].append(dict(type=cl, **classes[cl], derivesGraphic=derived(cl, 'UnityEngine.UI.Graphic'),
             materialMethods=[m for m in methods[cl] if any(s in m['signature'] for s in ['get_material(', 'set_material(', 'get_fontSharedMaterial(', 'set_fontSharedMaterial(', 'get_materialForRendering('])]))
    envs = {}; files = {}
    def asset(path):
        name = Path(path).name
        if name not in files:
            envs[name] = UnityPy.load(str(path))
            files[name] = next(f for f in envs[name].assets if f.name.lower() == name.lower())
        return files[name]

    ggm = asset(a.data / 'globalgamemanagers')
    build = next(o.read_typetree() for o in ggm.objects.values() if o.type.name == 'BuildSettings')
    # Do not export unrelated fields such as BuildSettings.m_AuthToken.
    scenes = build['scenes']; ev['buildScenes'] = scenes; ev['unityVersion'] = build['m_Version']
    for path in installed:
        f = asset(path)
        for o in f.objects.values():
            if o.type.name != 'Material': continue
            data = o.read()
            try: shader = data.m_Shader.deref()
            except Exception: continue
            shader_key = (shader.assets_file.name.lower(), shader.path_id)
            profile = {('sharedassets0.assets', 11): 'rizline.pc.tmp-mobile-outline.v1',
                       ('resources.assets', 19): 'rizline.pc.translucent-ui-float.v1'}.get(shader_key)
            material=dict(file=f.name, pathId=o.path_id, nameForDisplay=data.m_Name,
                    rawSha256=hashlib.sha256(o.get_raw_data()).hexdigest(), profile=profile,
                    shaderFile=shader.assets_file.name, shaderPathId=shader.path_id)
            all_materials.append(material)
            if profile: ev['profileMaterials'].append(material)

    def hierarchy(go):
        path = []; active = True; seen = set(); sibling_path = []
        while go is not None:
            key = (go.object_reader.assets_file.name, go.object_reader.path_id)
            if key in seen: raise ValueError('Hierarchy loop')
            seen.add(key); path.append(go.m_Name); active &= bool(go.m_IsActive)
            transforms = [p.component.deref_parse_as_object() for p in go.m_Component
                          if p.component.deref().type.name in ('Transform', 'RectTransform')]
            if not transforms: break
            parent_pointer = transforms[0].m_Father
            if not parent_pointer.path_id: break
            parent = parent_pointer.deref_parse_as_object()
            child_id = transforms[0].object_reader.path_id
            sibling_path.append(next((i for i, p in enumerate(parent.m_Children) if p.path_id == child_id), None))
            go = parent.m_GameObject.deref_parse_as_object()
        return '/'.join(reversed(path)), active, list(reversed(sibling_path))

    for scene_index, scene_path in enumerate(scenes):
        f = asset(a.data / f'level{scene_index}'); graphic_count = collections.Counter(); selected = []
        for o in f.objects.values():
            if o.type.name != 'MonoBehaviour': continue
            data = o.read(check_read=False)
            try:
                script = data.m_Script.deref_parse_as_object()
                cl = (script.m_Namespace + '.' if script.m_Namespace else '') + script.m_ClassName
                if derived(cl, 'UnityEngine.UI.Graphic'): graphic_count[cl] += 1
                if cl not in ('TMPro.TextMeshProUGUI', 'LeTai.Asset.TranslucentImage.TranslucentImage'): continue
                go = data.m_GameObject.deref_parse_as_object(); fullpath, active, siblings = hierarchy(go)
            except Exception as error:
                ev.setdefault('baseReadFailures', []).append(dict(file=f.name, pathId=o.path_id, message=str(error)[:200])); continue
            refs = []; raw = o.get_raw_data()
            for material in all_materials:
                if material['file'].lower() == f.name.lower(): file_ids = [0]
                else: file_ids = [i + 1 for i, ext in enumerate(f.externals) if Path(ext.path.replace('\\', '/')).name.lower() == material['file'].lower()]
                for file_id in file_ids:
                    encoded = struct.pack('<iq', file_id, material['pathId'])
                    offsets = [n for n in range(32, len(raw) - 11, 4) if raw[n:n+12] == encoded]
                    if offsets: refs.append(dict(profile=material['profile'], targetFile=material['file'], targetPathId=material['pathId'],
                        shaderFile=material['shaderFile'],shaderPathId=material['shaderPathId'],materialNameForDisplay=material['nameForDisplay'],
                        rawPPtrOffsets=offsets, fieldDecoded=False))
            row = dict(file=f.name, pathId=o.path_id, scene=Path(scene_path).stem, componentType=cl,
                componentEnabled=bool(data.m_Enabled),componentAssembly=script.m_AssemblyName, gameObjectPathId=go.object_reader.path_id,
                hierarchy=fullpath, staticActiveInHierarchy=active, siblingIndexPath=siblings,
                typeTreePresent=o.serialized_type.node is not None, rawSerializedMaterialReferenceCandidates=refs)
            ev['components'].append(row); selected.append(row)
        ev['sceneSummary'].append(dict(file=f.name, scene=Path(scene_path).stem, serializedGraphicCountLowerBound=sum(graphic_count.values()),
            countLimitation='Unqualified ambiguous Image parent is not auto-resolved; direct TranslucentImage components are listed separately.',
            graphicComponentCounts=dict(graphic_count), selectedComponents=len(selected),
            tmpCount=sum(x['componentType']=='TMPro.TextMeshProUGUI' for x in selected),translucentCount=sum(x['componentType']=='LeTai.Asset.TranslucentImage.TranslucentImage' for x in selected),
            selectedStaticActiveCount=sum(x['staticActiveInHierarchy'] and x['componentEnabled'] for x in selected)))

    pe = pefile.PE(str(assembly), fast_load=True); base = pe.OPTIONAL_HEADER.ImageBase
    md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
    for name, rva, size in [('Graphic.get_material', 0x15E86A0, 144), ('Image.get_material', 0x15EF950, 224),
                            ('TMP_Text.get_fontSharedMaterial', 0x4AA4B0, 16), ('TMP_Text.set_fontSharedMaterial', 0x14E4CD0, 160),
                            ('TextMeshProUGUI.UpdateMaterial', 0x149A520, 320), ('TextMeshProUGUI.SetSharedMaterial', 0x1499460, 128)]:
        ev['nativeMethods'].append(dict(name=name, rva=hex(rva), bytesInspected=size,
            instructions=[dict(rva=hex(i.address-base), instruction=i.mnemonic, operands=i.op_str) for i in md.disasm(pe.get_data(rva, size), base+rva)]))
    a.output.write_text(json.dumps(ev, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(dict(output=str(a.output), sha256=sha(a.output), scenes=len(ev['sceneSummary']),
        selectedComponents=len(ev['components']), profileMaterials=len(ev['profileMaterials']), baseReadFailures=len(ev.get('baseReadFailures', [])),
        sceneSummary=ev['sceneSummary']), ensure_ascii=False))


if __name__ == '__main__': main()
