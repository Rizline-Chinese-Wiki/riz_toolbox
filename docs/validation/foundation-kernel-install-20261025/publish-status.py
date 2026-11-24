"""Append verified installer coverage to the full-build milestone."""
from pathlib import Path
import json,os

batch=Path(__file__).resolve().parent;repo=batch.parents[2]
summary=json.loads((batch/'summary.json').read_text(encoding='utf-8-sig'))
assert summary['safetyChecks']==16 and summary['recoveryChecks']==13
for name in ('README.md','docs/IMPLEMENTATION-STATUS.md','.docs/CURRENT-CHECKPOINT.md','.docs/foundation-expansion/CHECKLIST.md'):
    path=repo/name;text=path.read_text(encoding='utf-8')
    assert '<!-- KERNEL-INSTALL-SUPPLEMENT -->' not in text
    target=os.path.relpath(batch/'README.md',path.parent).replace('\\','/')
    addition=f'''- **安装补充已修复并实测**：[独立证据]({target})。新23包在旧22项白名单下的真实拒绝已复现；精确加入Kernel后，自有临时目录16项安全检查和13项恢复检查通过。三脚本覆盖修复与原核心构建分列，现行599源码另有组合归档，其余596项不变；没有再次宣称重跑1617，也未操作真实游戏目录。<!-- KERNEL-INSTALL-SUPPLEMENT -->
'''
    marker='<!-- ENGINE-KERNEL-LATEST:END -->';assert text.count(marker)==1
    text=text.replace(marker,addition+marker,1)
    path.write_text(text,encoding='utf-8',newline='\n')
core_readme=repo/'docs/validation/foundation-engine-kernel-20261025/README.md'
text=core_readme.read_text(encoding='utf-8')
marker='## 安装补充已另批完成'
assert marker not in text
text+='''
## 安装补充已另批完成

核心23528之后发现安装白名单仍是22项，真实临时安装会拒绝新23包。该问题已在[独立补充批](../foundation-kernel-install-20261025/README.md)修复并通过16项安全/13项恢复检查。后续只改3个安装/测试脚本，核心599归档保留构建时身份；当前源码以补充批的[组合599清单](../foundation-kernel-install-20261025/source-final-manifest.json)和[组合ZIP证明](../foundation-kernel-install-20261025/source-archive-check.json)为准，其余596项相同。不能把核心ZIP误称为包含后来的安装修复，也不需要将不受影响的1617项再算一次。
'''
core_readme.write_text(text,encoding='utf-8',newline='\n')
print('Published verified installer supplement.')
