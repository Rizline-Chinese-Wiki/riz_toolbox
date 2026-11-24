"""Register this batch's native implementation and tests, once; no build or install."""
from pathlib import Path

repo = Path(__file__).resolve().parents[3]
native = repo / "scripts/Build-JudgeContext.ps1"
body = native.read_text(encoding="utf-8-sig")
if "OperationContext.cpp" not in body:
    lines = body.splitlines()
    out = []
    for line in lines:
        out.append(line)
        if '"cl /nologo /c ' in line and "CoroutineContext.cpp" in line:
            out.append(line.replace("CoroutineContext", "OperationContext"))
    body = "\n".join(out) + "\n"
    marker = '$nativeOutput\\CoroutineContext.obj`"'
    # Only the link line contains this marker and not the compile /Fo argument.
    lines = body.splitlines()
    count = 0
    for i, line in enumerate(lines):
        if '"cl /nologo /LD ' in line and "JudgeContext.cpp" in line:
            assert marker in line
            lines[i] = line.replace(marker, marker + ' `"$nativeOutput\\OperationContext.obj`"')
            count += 1
    assert count == 1
    body = "\n".join(lines) + "\n"
    body = body.replace("$expectedExports = @(\n", "$expectedExports = @(\n"
        "    'RizOperationContext_Configure', 'RizOperationContext_GetEntry', 'RizOperationContext_GetFrameSize',\n"
        "    'RizOperationContext_GetLimit', 'RizOperationContext_GetStats',\n", 1)
    native.write_text(body, encoding="utf-8", newline="\n")

audio = repo / "tests/RizToolbox.AudioContent.Tests/Program.cs"
body = audio.read_text(encoding="utf-8-sig")
new_tests = ["NativeUnityEventDispatchAdoptionTests", "NativeCoroutineCancellationIdentityTests", "NativeUnityEventMutationTests"]
for cls in new_tests:
    if f"tests=tests.Concat({cls}.Cases)" not in body:
        marker = "tests=tests.Concat(NativeUnityEventMidIntegrationTests.Cases).ToArray();"
        assert marker in body
        body = body.replace(marker, marker + f"\ntests=tests.Concat({cls}.Cases).ToArray();", 1)
    if f".Concat({cls}.Cases).Select" not in body and body.count(f".Concat({cls}.Cases)") == 1:
        marker = ".Concat(NativeUnityEventMidIntegrationTests.Cases)"
        assert body.count(marker) == 2
        at = body.rfind(marker)
        body = body[:at] + body[at:].replace(marker, marker + f"\n        .Concat({cls}.Cases)", 1)
audio.write_text(body, encoding="utf-8", newline="\n")

hooks = repo / "tests/RizToolbox.Hooks.Tests/Program.cs"
body = hooks.read_text(encoding="utf-8-sig")
if ".Concat(NativeOperationObservationTests.Cases)" not in body:
    marker = ".Concat(NativeListenerContextBridgeTests.Cases).ToArray();"
    assert marker in body
    body = body.replace(marker, ".Concat(NativeListenerContextBridgeTests.Cases)\n .Concat(NativeOperationObservationTests.Cases).ToArray();", 1)
hooks.write_text(body, encoding="utf-8", newline="\n")
print("Registered native OperationContext and three Audio/one Hooks regression classes; not built.")
