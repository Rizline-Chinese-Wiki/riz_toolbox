"""Read three MonoBehaviours in the fixed 73 KiB Level scene and their scripts.

Only freezes the base MonoBehaviour headers, raw object bytes, first PPtrs,
script identities, and one GameObject. Stripped type trees do not describe the
custom fields: first-field labels are a documented metadata-order inference,
not a generated full type-tree decode or runtime address observation.
"""
import hashlib
import importlib.metadata
import json
import struct
from pathlib import Path

import UnityPy

here = Path(__file__).resolve().parent
root = Path(r"D:\SteamLibrary\steamapps\common\Rizline\Rizline_Data")
expected = {
    "level6": "e95ea6ba4b09ade889b087fc91addc7cfa89274b367477927550759d4dd188bc",
    "globalgamemanagers.assets": "0b40ece56a0a01b0aa91d3ccfca44144177bc1507db23b0d6a9e75483112149f",
}
sources = {}
for name, digest in expected.items():
    data = (root / name).read_bytes()
    assert hashlib.sha256(data).hexdigest() == digest, name
    sources[name] = {"size": len(data), "sha256": digest}
scene = next(iter(UnityPy.load(str(root / "level6")).files.values()))
scripts = next(iter(UnityPy.load(str(root / "globalgamemanagers.assets")).files.values()))
rows = []
for path_id, script_id, class_name, labels in [
    (188, 1326, "LevelControl", ["levelInformation"]),
    (189, 1397, "ProgressControl", ["levelInformation", "audioSource"]),
    (219, 1018, "LevelInformation", ["levelControl", "progressControl"]),
]:
    obj = scene.objects[path_id]
    raw = obj.get_raw_data()
    header = obj.read_typetree(check_read=False)
    assert header["m_GameObject"] == {"m_FileID": 0, "m_PathID": 58}
    assert header["m_Enabled"] == 1
    assert header["m_Name"] == ""
    assert header["m_Script"] == {"m_FileID": 1, "m_PathID": script_id}
    assert scene.externals[0].path == "globalgamemanagers.assets"
    script = scripts.objects[script_id].read()
    assert (script.m_ClassName, script.m_Namespace, script.m_AssemblyName) == (class_name, "InLevel", "Assembly-CSharp")
    pointers = []
    for n, label in enumerate(labels):
        offset = 32 + n * 12
        file_id, target_id = struct.unpack_from("<iq", raw, offset)
        pointers.append({"metadataOrderField": label, "serializedByteOffset": offset,
                         "fileId": file_id, "pathId": target_id,
                         "rawHex": raw[offset:offset + 12].hex()})
    rows.append({"pathId": path_id, "scriptPathId": script_id, "class": "InLevel." + class_name,
                 "baseHeader": header, "objectSize": len(raw), "rawHex": raw.hex(),
                 "rawSha256": hashlib.sha256(raw).hexdigest(), "firstSerializedPointers": pointers})
assert rows[0]["firstSerializedPointers"][0]["pathId"] == 219
assert rows[1]["firstSerializedPointers"][0]["pathId"] == 219
assert rows[2]["firstSerializedPointers"][0]["pathId"] == 188
assert rows[2]["firstSerializedPointers"][1]["pathId"] == 189
game_object = scene.objects[58].read_typetree()
assert game_object["m_Name"] == "LevelInformation" and game_object["m_IsActive"] is True
components = {x["component"]["m_PathID"] for x in game_object["m_Component"] if x["component"]["m_FileID"] == 0}
assert {188, 189, 219}.issubset(components)
result = {"schemaVersion": 1, "runtimeAcceptance": False, "mode": "bounded-offline-serialized-reference-read",
          "sources": sources, "unityPyVersion": importlib.metadata.version("UnityPy"),
          "objects": rows, "gameObject58": game_object,
          "limitations": ["Custom MonoBehaviour type trees are stripped; labels infer first field order from pinned dump.cs.",
                          "Serialized PPtrs identify scene objects, never runtime addresses or callback scheduling.",
                          "No claim that Progress.Update runs after LevelControl.Start, getter, or coroutine resume."]}
(here / "scene-references.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf8")
print(json.dumps({"objects": len(rows), "sceneBytes": sources["level6"]["size"], "runtimeAcceptance": False}))
