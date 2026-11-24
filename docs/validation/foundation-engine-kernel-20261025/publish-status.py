"""Publish this actual full milestone without changing B-task acceptance."""
from pathlib import Path
import os

batch=Path(__file__).resolve().parent;repo=batch.parents[2]
for name in ('README.md','docs/IMPLEMENTATION-STATUS.md','.docs/CURRENT-CHECKPOINT.md','.docs/foundation-expansion/CHECKLIST.md'):
    path=repo/name;text=path.read_text(encoding='utf-8')
    assert '<!-- ENGINE-KERNEL-LATEST:BEGIN -->' not in text
    target=os.path.relpath(batch/'README.md',path.parent).replace('\\','/')
    block=f'''<!-- ENGINE-KERNEL-LATEST:BEGIN -->
- **最新完整构建：独立协程运行核已实现并验证。** [本批证据]({target})。session23528实际exit0，25套1617/1617，599项声明源/23项包冻结。新Kernel仅39424字节、8个ABI2导出和2项系统导入，无入口/TLS/CRT/Dobby直接依赖；canonical入口表改为typed constexpr，旧helper继续独立回归。
- **实际范围**：新核7进程：128入口与256拒绝、1606宿主检查/1486父断言；原生3402+五托管889检查，180原调用/152配对。旧39早期宿主/7316另计。31项输入在新核运行期间持只读句柄且hash不变；后续另一测试正常复制旧helper的一项变化及准确旧文件恢复已单列。94条去重编译诊断保留，未声称零warning。
- **接下来**：新Kernel尚未由生产或固定早期链采用；下一批独立绑定它，再实现真实WinHTTP/首次发布与V4、coordinator/对象GC、scheduler/async/Start内联清理和全消费者退休。新包未安装，游戏实测稍后。完整B01–B19仍active，仅B03整项/B18a子项验收；EngineCancelled=false、Pending保留、RequestPlay关闭。
<!-- ENGINE-KERNEL-LATEST:END -->

### 上一批固定发布早期链（历史）

'''
    marker='<!-- EARLY-FIXED-LATEST:BEGIN -->';assert text.count(marker)==1
    start=text.index(marker);end=text.index('<!-- EARLY-FIXED-LATEST:END -->',start)
    old=text[start:end].replace('**最新：无 Dobby','**历史：无 Dobby',1)
    text=text[:start]+block+old+text[end:]
    text=text.replace('### 上一个完整构建里程碑','### 历史完整构建：旧早期宿主')
    path.write_text(text,encoding='utf-8',newline='\n')
    print(name)
