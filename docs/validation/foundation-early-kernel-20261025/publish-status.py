"""Publish concise current status only after the observed final matrix."""
from pathlib import Path
import json

batch = Path(__file__).resolve().parent
repo = batch.parents[2]
summary = json.loads((batch/'final-summary.json').read_text())
assert summary['passed'] and summary['sessionId'] == 17459
assert summary['kernel']['cases'] == summary['legacyEarly']['cases'] == 73
assert summary['legacyPublisher']['cases'] == 57 and not summary['gameExecuted']
marker = '<!-- EARLY-KERNEL-LATEST:BEGIN -->'
for name,link in [
    ('README.md','docs/validation/foundation-early-kernel-20261025/README.md'),
    ('.docs/CURRENT-CHECKPOINT.md','../docs/validation/foundation-early-kernel-20261025/README.md'),
    ('docs/IMPLEMENTATION-STATUS.md','validation/foundation-early-kernel-20261025/README.md'),
    ('.docs/foundation-expansion/CHECKLIST.md','../../docs/validation/foundation-early-kernel-20261025/README.md'),
]:
    path = repo/name
    text = path.read_text(encoding='utf-8-sig')
    assert marker not in text and '<!-- ENGINE-KERNEL-LATEST:BEGIN -->' in text
    block = f'''{marker}
- **最新原生集成：新 Kernel 自有启动链已接通。** [本批原件]({link})。root 实际观察 session17459 终态0；新链73/13248断言、旧发布器57、旧链73/13031断言均通过，共203个自有宿主；604项声明输入和18条二进制按7+4+7隔离归档。本批没有重跑上一批25套1617，23项包字节保持。
- **实际修复与证据**：独立 publisher 记录2、wire3与helper ABI2/scalar1分层；复用同一核心代码，固定模块/入口/epoch不混用。调试观察改为有界映射身份与线程硬件返回断点；3个错栈经过保留责任、后续真实RSP返回清槽。正式debug为5Load/0Unload，另3次强化预飞实际各6Load/1Unload；1007未命中，不计覆盖。首批31803与失败诊断原件保留。
- **直接下一步**：实现固定系统WinHTTP转发及生产启动凭据，再接新核的CLR只读采用、coordinator/真实对象GC、scheduler/async/Start内联清理和全部消费者资源退休；补完整Retry/Return/Loader。新链仅用于自有宿主，真实游戏仍是旧安装，18安装项/4原始文件hash一致；未安装或运行游戏。B01–B19仍active，仅B03整项/B18a子项验收，EngineCancelled=false、Pending保留、RequestPlay关闭。
<!-- EARLY-KERNEL-LATEST:END -->

下方为上一批独立核完整构建的历史时点；其“下一步”已由上方接续，不代表当前固定链仍未实现。

'''
    text = text.replace('<!-- ENGINE-KERNEL-LATEST:BEGIN -->',block+'<!-- ENGINE-KERNEL-LATEST:BEGIN -->',1)
    if name.endswith('CHECKLIST.md'):
        old = '- [ ] B19 分发：当前25套1617/1617，另39自有早期加载进程/7316父断言；576源/22包冻结未安装。生产安全首次安装/V4、真实对象接线、调度/退休、游戏流程、干净环境与升级矩阵仍缺。见[证据](../../docs/validation/foundation-early-loader-20261025/README.md)。'
        new = '- [ ] B19 分发：上一完整构建25套1617/1617、599源/23包；随后安装安全16/恢复13修复，现组合源再增至本批604并通过新核73/旧发布器57/旧链73。23包仍原字节，新链未安装。生产启动/V4、真实对象接线、调度/退休、游戏流程、干净环境与升级矩阵仍缺。见[当前证据](../../docs/validation/foundation-early-kernel-20261025/README.md)。'
        assert old in text
        text = text.replace(old,new)
    path.write_text(text,encoding='utf-8',newline='\n')
print('Updated four current status documents; task checkboxes unchanged.')
