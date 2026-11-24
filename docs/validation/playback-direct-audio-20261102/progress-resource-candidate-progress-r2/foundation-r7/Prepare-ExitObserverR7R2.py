from pathlib import Path
import ast

O = Path(__file__).resolve().parent
t = (O / 'Collect-FoundationContinuationExitR7.py').read_text()
old = " and '--final-freeze' in launch['actualCommandLine']"
assert t.count(old) == 1
t = t.replace(old, '').replace('continuation-exit-collector', 'continuation-exit-collector-r2')
t = t.replace('continuation-os-exit-observation', 'continuation-os-exit-observation-r2')
t = t.replace('foundation-continuation-audit.', 'foundation-continuation-audit-r2.')
ast.parse(t)
(O / 'Collect-FoundationContinuationExitR7R2.py').write_text(t, encoding='utf-8')
print('Continuation exit observer prepared without an inapplicable freeze CLI flag.')
