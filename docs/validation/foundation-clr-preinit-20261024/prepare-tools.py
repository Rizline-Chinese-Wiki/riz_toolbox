"""Prepare evidence tools; do not regenerate the already archived 1515 batch."""
from pathlib import Path
import shutil
root = Path(__file__).resolve().parent
previous = root.parent / 'foundation-preinit-producer-20261024'
for name in ['summarize-build.py','freeze-source.py','freeze-package.py','compare-sources.py','verify-terminal-evidence.py','check-docs.py']:
    body = (previous / name).read_text(encoding='utf-8-sig')
    body = body.replace('preinit-producer-final-20261024', 'clr-preinit-final-20261024')
    if name == 'compare-sources.py':
        body = body.replace('foundation-first-install-20261024','foundation-preinit-producer-20261024').replace('changes-from-1511','changes-from-1515')
        body = body.replace('1511/', '1515/').replace('preinit-producer/', 'clr-preinit/')
    if name == 'check-docs.py':
        body = body.replace('底层-原生启动安装-report.md','底层-CLR启动采用-report.md')
    (root / name).write_text(body, encoding='utf-8', newline='\n')
for name in ['direct-call-candidates.json','mid-site-evidence.json','terminal-disassembly.txt','terminal-evidence.json','terminal-metadata.txt','static-evidence-origin.json']:
    shutil.copy2(previous / name, root / name)
print('Prepared tools and inherited, separately revalidated static evidence.')
