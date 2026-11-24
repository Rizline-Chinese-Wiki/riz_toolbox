"""Reuse the pinned local-launch extractor with only result ownership scopes.

Read-only PE parsing. Does not open a process, alter game files, or invoke code.
"""
import ast
import hashlib
import json
import re
import sys
from pathlib import Path

here = Path(__file__).resolve().parent
base = here.parent / 'local-launch-20261022' / 'inspect-launch.py'
symbols = Path(r'C:\Users\71957\Desktop\绿豆诡计v2\output\il2cpp_output\script.json')
entries = json.loads(symbols.read_text(encoding='utf8'))['ScriptMethod']
targets = {
    'NormalMode.GetJudge': 'NormalModeInLevelController$$GetJudge',
    'NormalMode.GetInsideLevelParams': 'NormalModeInLevelController$$GetInsideLevelParams',
    'NormalMode.LevelOver': 'NormalModeInLevelController$$LevelOver',
    'NormalMode.LevelOverCoroutine': 'NormalModeInLevelController$$LevelOverCoroutine',
    'NormalMode.LevelOverCoroutine.MoveNext': '<LevelOverCoroutine>d__109$$MoveNext',
    'NormalMode.Skip': 'NormalModeInLevelController$$Skip',
    'NormalMode.Update': 'NormalModeInLevelController$$Update',
    'NormalMode.LevelOverDelayAction': '<>c$$<LevelOver>b__108_0',
    'PauseUI.Back': 'PauseUIControl$$Back',
    'PauseUI.Update': 'PauseUIControl$$Update',
    'Progress.Update': 'InLevel.ProgressControl$$Update',
    'Progress.Play': 'InLevel.ProgressControl$$Play',
    'Unity.SceneManager.Internal_SceneLoaded': 'UnityEngine.SceneManagement.SceneManager$$Internal_SceneLoaded',
    'LevelOver.Start': 'LevelOverControl$$Start',
    'SteamTimeline.SetTimelineGameMode': 'Steamworks.SteamTimeline$$SetTimelineGameMode',
    'SteamTimeline.AddInstantaneousTimelineEvent': 'Steamworks.SteamTimeline$$AddInstantaneousTimelineEvent',
    'SteamTimeline.EndRangeTimelineEvent': 'Steamworks.SteamTimeline$$EndRangeTimelineEvent',
}
scopes = {}
for name, target in targets.items():
    found = [x['Address'] for x in entries if x['Name'] == target]
    if len(set(found)) != 1:
        raise SystemExit('Missing or ambiguous bounded scope: ' + target)
    scopes[name] = found[0]
source = base.read_text(encoding='utf8')
source, count = re.subn(r'^SCOPES = .*$', 'SCOPES = ' + repr(scopes), source, count=1, flags=re.M)
if count != 1:
    raise SystemExit('Base extractor shape changed')
classes = {'InLevelControl', 'InLevelController', 'InsideLevelResult',
           'NormalModeInLevelController', 'LevelOverControl', 'LevelInformation',
           'ProgressControl', 'PauseUIControl', 'SceneManager', '<LevelOverCoroutine>d__109',
           '<StartNextSceneEnumerator>d__26', 'SteamTimeline', 'TimelineEventHandle'}
source, count = re.subn(r'classes = \{.*?\}\nexcerpt', 'classes = ' + repr(classes) + '\nexcerpt', source, count=1, flags=re.S)
if count != 1:
    raise SystemExit('Base extractor class-selection shape changed')
sys.argv = [str(__file__), '--game', r'D:\SteamLibrary\steamapps\common\Rizline\GameAssembly.dll',
            '--dump', str(symbols.with_name('dump.cs')), '--symbols', str(symbols), '--out', str(here)]
(here / 'extractor-provenance.json').write_text(json.dumps({
    'baseExtractor': str(base), 'baseExtractorSha256': hashlib.sha256(base.read_bytes()).hexdigest(),
    'wrapperSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
    'scopeSelection': scopes, 'mode': 'bounded-static-read-only', 'runtimeAcceptance': False
}, indent=2), encoding='utf8')
exec(compile(source, str(base), 'exec'))
