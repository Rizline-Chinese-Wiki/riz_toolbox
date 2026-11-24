"""Read the pinned image only; do not load or invoke game code."""
import hashlib
import json
import sys
from datetime import datetime, timezone
from pathlib import Path

import capstone
import pefile

IMAGE = Path('D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll')
METADATA = Path('D:/riz-build-cache-next/gameplay-native-values-static-r1-20261030/evidence-r3/metadata.json')
OUT = Path('D:/riz-build-cache-next/playback-musicplayer-writer-profile-r1-20261101')
PIN = '853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79'


def fact(path):
    path = Path(path).resolve()
    body = path.read_bytes()
    return dict(path=str(path), sha256=hashlib.sha256(body).hexdigest(), bytes=len(body))


def write(path, value):
    Path(path).write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def run():
    assert not OUT.exists(), 'Preserve earlier profile evidence.'
    image, metadata = fact(IMAGE), fact(METADATA)
    assert image['sha256'] == PIN
    declarations = json.loads(METADATA.read_text(encoding='utf-8-sig'))
    assert 'set_MusicPlayer(Pigeon.MUGToolkit.Core.MUGMusicPlayer value); // RVA: 0x401700' in declarations['InLevel.ProgressControl']
    assert 'set_Clip(UnityEngine.AudioClip value); // RVA: 0x3FC370' in declarations['Pigeon.MUGToolkit.Core.MUGMusicPlayer']
    pe = pefile.PE(str(IMAGE))
    decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
    base = pe.OPTIONAL_HEADER.ImageBase
    definitions = [('Progress.set_MusicPlayer', 0x401700, 13), ('MUG.set_Clip', 0x3fc370, 31)]
    methods = []
    OUT.mkdir()
    for name, rva, length in definitions:
        code = pe.get_data(rva, length)
        instructions = [dict(rva=hex(i.address - base), bytes=i.bytes.hex(), mnemonic=i.mnemonic,
                             operands=i.op_str) for i in decoder.disasm(code, base + rva)]
        functions = [row.struct for row in pe.DIRECTORY_ENTRY_EXCEPTION
                     if row.struct.BeginAddress <= rva < row.struct.EndAddress]
        asm = '\n'.join(f"{i['rva']}  {i['bytes']}  {i['mnemonic']} {i['operands']}" for i in instructions) + '\n'
        path = OUT / (name + '.asm')
        path.write_text(asm, encoding='utf-8')
        methods.append(dict(name=name, rva=hex(rva), length=length, bytes=code.hex(),
                            instructions=instructions, assembly=fact(path),
                            runtimeFunctions=[dict(begin=hex(f.BeginAddress), end=hex(f.EndAddress),
                                                   unwind=hex(f.UnwindData)) for f in functions]))
    assert methods[0]['bytes'] == '488951384883c138e95328e6ff'
    assert not methods[0]['runtimeFunctions']
    assert methods[0]['instructions'][-1]['mnemonic'] == 'jmp'
    assert methods[1]['runtimeFunctions'] == [dict(begin='0x3fc370', end='0x3fc38f', unwind='0x1ce4d30')]
    imports = [dict(module=entry.dll.decode(), symbols=[item.name.decode() if item.name else '#'+str(item.ordinal)
                for item in entry.imports]) for entry in pe.DIRECTORY_ENTRY_IMPORT]
    exports = pe.DIRECTORY_ENTRY_EXPORT.symbols
    assert fact(IMAGE) == image and fact(METADATA) == metadata
    result = dict(kind='pinned-music-player-reference-writer-static-profile', passed=True,
                  observedAtUtc=datetime.now(timezone.utc).isoformat(), image=image, metadata=metadata,
                  methods=methods, imports=imports, exportCount=len(exports),
                  progressSetterDirectStoreOffset='0x38', progressSetterTailTargetRva='0x263f60',
                  tailTargetSemanticNameProven=False,
                  progressSetterLeafWithoutRuntimeFunction=True,
                  mugSetClipTailTargetRva='0x1511a20',
                  knownSourceWriterNotCurrentlyObserved=True,
                  runtimeDetourRelocationProven=False, nativeOriginalUnwindProven=False,
                  continuousSourceGenerationProven=False, completeLowerWriterCoverage=False,
                  tools=dict(python=sys.version, capstone=capstone.__version__, pefile=pefile.__version__),
                  runner=fact(__file__), gameImageLoaded=False, gameCodeExecuted=False,
                  productionAdmission=False, installed=False)
    write(OUT / 'test-result.json', result)
    print(json.dumps(dict(passed=True, methods=2, progressSetterLeaf=True, result=fact(OUT / 'test-result.json'))))


if __name__ == '__main__':
    run()
