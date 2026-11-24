"""Read-only host/input-source evidence. Writes only inside this validation folder.
Run from the repository: C:/Python314/python.exe docs/validation/input-action-source-20261020/inspect-host-source.py
"""
from pathlib import Path
import datetime
import hashlib
import json
import re
import pefile

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
GAME = Path(r"D:\SteamLibrary\steamapps\common\Rizline")
DUMP = Path(r"C:\Users\71957\Desktop\绿豆诡计v2\output\il2cpp_output\dump.cs")
EXPECTED = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79"

def sha(data):
    return hashlib.sha256(data).hexdigest()

def write(name, value):
    (HERE / name).write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

binary = (GAME / "GameAssembly.dll").read_bytes()
assert sha(binary) == EXPECTED, "Unexpected build; do not transfer addresses."
pe = pefile.PE(data=binary, fast_load=False)
imports = [
    {"module": entry.dll.decode(errors="replace"),
     "names": [imp.name.decode(errors="replace") if imp.name else "ordinal:" + str(imp.ordinal) for imp in entry.imports]}
    for entry in getattr(pe, "DIRECTORY_ENTRY_IMPORT", [])
]
write("binary-identity.json", {
    "observedAtUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
    "source": str(GAME / "GameAssembly.dll"), "sha256": sha(binary),
    "machine": pe.FILE_HEADER.Machine,
    "imports": imports,
    "exports": [x.name.decode(errors="replace") for x in getattr(getattr(pe, "DIRECTORY_ENTRY_EXPORT", None), "symbols", []) if x.name],
    "importClassification": {
        "network": [x["module"] for x in imports if any(s in x["module"].lower() for s in ("ws2", "winhttp", "wininet"))],
        "crypto": [x["module"] for x in imports if any(s in x["module"].lower() for s in ("bcrypt", "crypt", "ssl"))],
        "windowsRuntimeFilesRegistryProcess": [x["module"] for x in imports if any(s in x["module"].lower() for s in ("kernel", "advapi", "ucrt", "vcruntime"))],
        "scope": "Identity/survey only. Presence is not proof of a particular input path. No injection or import patch performed."
    }
})

selection = {
    "src/RizToolbox.Runtime/RuntimeBridge.cs": ["var result = original", "_api!.BindCurrentThread", "_tick(_api!)", "_tickMethod"],
    "src/RizToolbox.Loader/Bootstrap.cs": ["profile.TickMethod", "_host?.Tick()"],
    "src/RizToolbox.Loader/ToolboxHost.cs": ["_adapter.FlushBoundaryObservations", "_main.Pump(32)", "_events.Publish(new FrameUpdate"],
    "src/RizToolbox.Adapter/profiles/steam-25977441.json": ["tickType", "tickMethod", "gameManager"],
    "src/RizToolbox.Adapter/UiInput/UiInputModule.cs": ["_events.Subscribe<FrameUpdate>", "_service?.Tick()", "_service.Tick()"],
    "src/RizToolbox.Adapter/UiInput/UiInputService.cs": ["_backend.Capture", "_input.Dispatch", "window.Tick", "_input.GetBindings"],
    "src/RizToolbox.Adapter/UiInput/NativeUiInputBackend.cs": ["TryCaptureDevice", "get_wasPressedThisFrame", "get_current", "focused == false", "textEditing =", "DeviceStatus"],
    "src/RizToolbox.Core/Input/InputActionBroker.cs": ["externalTextEditing", "_focus.Any", "InContext", "action.Callback", "pressed.Contains"],
    "examples/RizToolbox.Examples/ModuleRuntimeProbe.cs": ["proof-f8", "INPUT REGISTRATION", "inputRegistered = true"]
}
sources = []
for relative, needles in selection.items():
    data = (ROOT / relative).read_bytes()
    text = data.decode("utf-8-sig")
    matches = [{"line": index, "text": line.strip()} for index, line in enumerate(text.splitlines(), 1) if any(needle in line for needle in needles)]
    sources.append({"path": relative, "sha256": sha(data), "matches": matches})
write("host-source-evidence.json", sources)

dump_data = DUMP.read_bytes()
lines = dump_data.decode("utf-8-sig").splitlines()
targets = {
    ("", "GameManager"), ("UnityEngine.InputSystem", "InputSystem"),
    ("UnityEngine.InputSystem", "InputManager"), ("UnityEngine.InputSystem", "InputSettings"),
    ("UnityEngine.InputSystem", "InputDevice"), ("UnityEngine.InputSystem.Controls", "ButtonControl"),
    ("UnityEngine.InputSystem.LowLevel", "InputState"), ("UnityEngine.InputSystem.LowLevel", "InputUpdate"),
    ("UnityEngine.InputSystem.UI", "VirtualMouseInput"), ("UnityEngine.InputSystem.UI", "InputSystemUIInputModule")
}
metadata = []
namespace = ""
for index, line in enumerate(lines):
    if line.startswith("// Namespace:"):
        namespace = line.partition(":")[2].strip()
    match = re.match(r"public (?:class|struct|enum) ([^ :]+)", line)
    if not match or (namespace, match.group(1)) not in targets:
        continue
    end = next(n for n in range(index + 1, len(lines)) if lines[n] == "}")
    metadata.append({"namespace": namespace, "type": match.group(1), "line": index + 1,
                     "declaration": "\n".join(lines[index:end + 1])})
write("input-metadata.json", {"path": str(DUMP), "sha256": sha(dump_data), "types": metadata,
    "limitations": ["dump enum field offsets do not preserve enum numeric constants.", "RVA metadata alone does not prove runtime execution order or input events."]})

# Preserve one coherent read, instead of treating a moving toolbox.log as an immutable artifact.
frozen = HERE / "runtime-log-frozen.log"
if not frozen.exists():
    frozen.write_bytes((GAME / "BepInEx" / "RizToolbox" / "toolbox.log").read_bytes())
log_data = frozen.read_bytes()
selected = []
comparisons = []
for index, line in enumerate(log_data.decode("utf-8-sig").splitlines(), 1):
    if any(label in line for label in ("Input source comparison:", "ModuleProbe INPUT", "Input System sample:", "ModuleProbe TEXT CHANGED", "ModuleProbe BUTTON")):
        selected.append({"line": index, "text": line})
    marker = "Input source comparison: "
    if marker in line:
        value = line.split(marker, 1)[1].strip()
        comparisons.append({"line": index, "at": line.split(" ", 1)[0], "values": json.loads(value)})
write("runtime-input-evidence.json", {"source": str(frozen), "sha256": sha(log_data), "selected": selected,
    "comparisons": comparisons, "inputF8LogCount": sum("ModuleProbe INPUT F8 " in x["text"] for x in selected),
    "limitations": ["Only framework logs are captured here; no OS/CUA input-delivery proof is inferred.",
                    "Bounded idle sampling is not continuous observation of every key press.",
                    "A ready device is not proof that its F8 key received an event."]})
print(json.dumps({"status": "recorded", "output": str(HERE), "sourceFiles": len(sources),
    "metadataTypes": len(metadata), "inputComparisons": len(comparisons), "gameAssemblySha256": EXPECTED}, ensure_ascii=False))
