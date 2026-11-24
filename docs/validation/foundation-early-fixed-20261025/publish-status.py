"""Publish the observed integration milestone without changing acceptance ticks."""
from pathlib import Path
import os

batch = Path(__file__).resolve().parent
repo = batch.parents[2]
for name in ('README.md','docs/IMPLEMENTATION-STATUS.md','.docs/CURRENT-CHECKPOINT.md','.docs/foundation-expansion/CHECKLIST.md'):
    path = repo/name
    text = path.read_text(encoding='utf-8')
    assert '<!-- EARLY-FIXED-LATEST:BEGIN -->' not in text
    target = os.path.relpath(batch/'README.md', path.parent).replace('\\','/')
    block = f'''<!-- EARLY-FIXED-LATEST:BEGIN -->
- **最新：无 Dobby 的固定发布早期链已接通，73 个独立进程通过。** [本批证据]({target})。root 观察 session34351 实际 exit0，14 项输入/7 个二进制已冻结；全 Prepare→4 Configure→Bind→4 Commit/Ready 早于真实 TLS/C/C++/DllMain 首调用。正常86/worker92次原调用，各56观察配对，包含真实前缀副作用和原异常。
- **本轮修复与验证**：Capture Busy/错误现永久关闭原生准入，真实受控递归及后续捕获均不能误放行；独立未发布进程建立同 binary/RVA 的 SEH/C++ raw flags 基线。73 个进程含56内部故障、实际1字节部分写和17条调试补充观测；API结果注入、跨 child 基线和仪器化边界均单列。上次完整25套1617/1617仍属于99557，本批未重跑托管套件。
- **接下来**：实现实际游戏的系统 WinHTTP 转发与可信首次发布/独立 V4，再接 coordinator/对象GC、scheduler/async/Start内联清理和全消费者退休，补完整 Retry/Return/Loader。游戏实测稍后，本批未安装；B01–B19 继续，仅 B03 整项/B18a 子项验收，EngineCancelled=false、Pending保留、RequestPlay关闭。旧576/12声明源、已装18文件/326源ZIP和4项原游戏hash已重新核对。
<!-- EARLY-FIXED-LATEST:END -->

### 上一批独立固定组件（历史）

'''
    marker = '<!-- FIXED-PUBLISHER-LATEST:BEGIN -->'
    assert text.count(marker) == 1
    start = text.index(marker)
    end = text.index('<!-- FIXED-PUBLISHER-LATEST:END -->', start)
    old = text[start:end].replace('**最新：固定四入口', '**历史：固定四入口', 1)
    text = text[:start] + block + old + text[end:]
    path.write_text(text, encoding='utf-8', newline='\n')
    print(name)
