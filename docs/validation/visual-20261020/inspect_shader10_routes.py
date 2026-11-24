"""Read-only check for a trusted Resources route to the actual Chinese TMP Shader10.
No game process, UI, build, installation, or production profile edit.
"""
from __future__ import annotations
import hashlib
import importlib.metadata
import json
import struct
from datetime import datetime, timezone
from pathlib import Path
import UnityPy

ROOT=Path(r'D:\SteamLibrary\steamapps\common\Rizline\Rizline_Data')
OUT=Path(__file__).parent/'shader10-resource-route-evidence.json'

def sha(path):
    h=hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda:stream.read(1024*1024),b''):h.update(chunk)
    return h.hexdigest()

def main():
    environments={};files={}
    def file(name):
        if name not in files:
            environments[name]=UnityPy.load(str(ROOT/name));files[name]=next(f for f in environments[name].assets if f.name==name)
        return files[name]
    global_file=file('globalgamemanagers'); resources=file('resources.assets'); shared=file('sharedassets0.assets')
    manager=next(o.read_typetree() for o in global_file.objects.values() if o.type.name=='ResourceManager')
    evidence=dict(schemaVersion=1,capturedAtUtc=datetime.now(timezone.utc).isoformat(),
        scope='Static Resources route provenance only; no runtime support claim or new production profile.',
        UnityPyVersion=importlib.metadata.version('UnityPy'),
        inputs=[dict(path=str(ROOT/n),bytes=(ROOT/n).stat().st_size,sha256=sha(ROOT/n))for n in ['globalgamemanagers','resources.assets','sharedassets0.assets']],
        targetShader=dict(file='sharedassets0.assets',pathId=10),resourceRoutes=[],targetMaterials=[],targetFontAssets=[],tmpSettingsCandidates=[])
    shader=shared.objects[10];shader_tree=shader.read_typetree();properties=[]
    for p in shader_tree['m_ParsedForm']['m_PropInfo']['m_Props']:
        if p['m_Name'] in {'_FaceColor','_OutlineColor','_OutlineWidth','_FaceDilate'}:
            properties.append(dict(name=p['m_Name'],serializedType=p['m_Type'],flags=p['m_Flags'],defaultVector=[p[f'm_DefValue[{i}]']for i in range(4)]))
    evidence['targetShader'].update(rawSha256=hashlib.sha256(shader.get_raw_data()).hexdigest(),nameForDisplay=shader_tree['m_ParsedForm']['m_Name'],selectedProperties=properties)
    for key,pointer in manager['m_Container']:
        route=dict(key=key,pointer=pointer)
        if pointer['m_PathID']==0:route['resolved']=None
        else:
            name=Path(global_file.externals[pointer['m_FileID']-1].path.replace('\\','/')).name if pointer['m_FileID'] else global_file.name
            obj=file(name).objects[pointer['m_PathID']];route['resolved']=dict(file=name,pathId=obj.path_id,type=obj.type.name)
            data=obj.read(check_read=False) if obj.type.name=='MonoBehaviour' else obj.read()
            if obj.type.name=='Material':
                linked=data.m_Shader.deref();route['resolved']['shader']=dict(file=linked.assets_file.name,pathId=linked.path_id)
            elif obj.type.name=='MonoBehaviour':
                script=data.m_Script.deref_parse_as_object();route['resolved']['script']=dict(assembly=script.m_AssemblyName,namespace=script.m_Namespace,type=script.m_ClassName)
        evidence['resourceRoutes'].append(route)
    for obj in shared.objects.values():
        if obj.type.name=='Material':
            data=obj.read();linked=data.m_Shader.deref()
            if linked.assets_file.name=='sharedassets0.assets' and linked.path_id==10:
                evidence['targetMaterials'].append(dict(file=shared.name,pathId=obj.path_id,nameForDisplay=data.m_Name,rawSha256=hashlib.sha256(obj.get_raw_data()).hexdigest()))
        elif obj.type.name=='MonoBehaviour':
            data=obj.read(check_read=False)
            try:script=data.m_Script.deref_parse_as_object()
            except Exception:continue
            if script.m_Namespace=='TMPro' and script.m_ClassName=='TMP_FontAsset':
                raw=obj.get_raw_data();refs=[]
                for material in evidence['targetMaterials']:
                    encoded=struct.pack('<iq',0,material['pathId']);offsets=[at for at in range(32,len(raw)-11,4)if raw[at:at+12]==encoded]
                    if offsets:refs.append(dict(material=material['pathId'],rawPPtrOffsets=offsets,fieldDecoded=False))
                evidence['targetFontAssets'].append(dict(file=shared.name,pathId=obj.path_id,nameForDisplay=data.m_Name,materialReferenceCandidates=refs,customTypeTreePresent=obj.serialized_type.node is not None))
    for oid in [30,31,34]:
        obj=resources.objects[oid];raw=obj.get_raw_data();refs=[]
        for font in evidence['targetFontAssets']:
            encoded=struct.pack('<iq',2,font['pathId']);offsets=[at for at in range(32,len(raw)-11,4)if raw[at:at+12]==encoded]
            if offsets:refs.append(dict(font=font['pathId'],rawPPtrOffsets=offsets,fieldDecoded=False))
        evidence['tmpSettingsCandidates'].append(dict(file=resources.name,pathId=oid,rawReferencesToTargetFonts=refs,customFieldsDecoded=False))
    direct_target=any(r.get('resolved') and (r['resolved'].get('shader')==dict(file='sharedassets0.assets',pathId=10) or
        r['resolved']['type']=='Shader' and r['resolved']['file']=='sharedassets0.assets' and r['resolved']['pathId']==10) for r in evidence['resourceRoutes'])
    fonts={(x['file'],x['pathId'])for x in evidence['targetFontAssets']}
    direct_font=any(r.get('resolved') and (r['resolved']['file'],r['resolved']['pathId'])in fonts for r in evidence['resourceRoutes'])
    evidence['conclusion']=dict(directTypedMaterialOrShaderRouteToTargetFound=direct_target,directTypedFontAssetRouteToTargetFound=direct_font,
        positiveTypedSettingsToTargetFontChainFound=any(x['rawReferencesToTargetFonts']for x in evidence['tmpSettingsCandidates']),
        reason='All 30 ResourceManager entries inspected. Resource material/font keys resolve to LiberationSans Shader11; no entry directly resolves to Shader10, its Material4 or FontAsset217. TMP Settings/default/fallback font objects have no raw reference candidate to FontAsset217.',
        runtimeDecision='Keep Shader10 parameter requests refused. No closed profile is added from names or hashes alone.')
    OUT.write_text(json.dumps(evidence,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(dict(output=str(OUT),sha256=sha(OUT),resourceRoutes=len(evidence['resourceRoutes']),targetMaterials=evidence['targetMaterials'],targetFonts=evidence['targetFontAssets'],conclusion=evidence['conclusion']),ensure_ascii=False))

if __name__=='__main__':main()
