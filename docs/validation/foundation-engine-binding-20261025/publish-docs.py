"""Publish the completed current batch only after terminal and exact-input proofs."""
import hashlib, json, re
from pathlib import Path
b = Path(__file__).resolve().parent
r = b.parents[2]
def read(name): return json.loads((b/name).read_text(encoding='utf-8-sig'))
s = read('build-summary.json'); run = read('build-run.json'); wrapper = read('build.log.run.json')
assert isinstance(run['sessionId'], int) and run['sessionId'] != 48324 and run['exitCode'] == run['terminalExitCodeObserved'] == wrapper['exitCode'] == 0
session = run['sessionId']
assert s['projects'] == 25 and s['passed'] == s['total'] == 1617
assert s['logSha256'] == run['logSha256'] == wrapper['logSha256'] == hashlib.sha256((b/'build.log').read_bytes()).hexdigest()
assert read('source-archive-check.json')['entries'] == 565 and read('package-archive-check.json')['entries'] == 22
assert read('build-input-integrity-check.json')['currentChanged'] == []
assert read('previous-installation-check.json')['allMatched'] and read('original-game-check.json')['allUnchanged']
d = len(s['compilerDiagnosticLines']); native = s['engineNodeNative']; joint = s['engineNodeManaged']
assert native['checks'] == 3402 and native['transportAbiVersion'] == 2 and native['rawExecuteEaxVerified']
assert joint['checks'] == 889 and joint['actualIdentityLedgerCaptureVerified']

def latest(prefix):
    return f'''<!-- ENGINE-BINDING-LATEST:BEGIN -->
- **最新：返回 ABI 与协程身份关联已验证，25 套 1617/1617 离线通过。** [本批说明]({prefix}docs/validation/{b.name}/README.md)。修正 Execute 保留完整 uint32 EAX，返回 ABI2 拒绝采用旧 helper；Start/Release 仍保 AL。现有三类 tracker 的 receiver/slot/generation/Enumerator/延后 Coroutine handle 与 native owner、node/engine generation 分别关联，foreign 隔离/地址复用/有界溢出保留责任；Runtime177、Audio640、Hooks189，565 源/22 包冻结未安装。
- **实证范围**：自有原生3402检查、136原调用/124观察配对；真实 Dobby→CLR→实际 identity/node账本五进程889检查、44原调用/28context，scalar provider/pairs受控。13组身份回归中3组走三类实际tracker实现及受控backend，与Runtime正式字段投影7组synthetic metadata回归分开证明。full{session} exit0；{d}条去重原始编译诊断，未声称零warning或干净换机复现。交叉复核发现的foreign遮蔽下owned未知node漏记已修正，修正前48324仅保历史原件。
- **仍缺**：UnityPlayer四入口的生产安全首次发布/独立V4凭据和coordinator接线；调度器消费/销毁、async与Start内联清理、实际对象/GC、全部writer/delegate/future消费者、完整资源退休及Retry/Return/Loader。新研究实际找到独立责任，不能把pre-il2cpp_init或Release0当完成证明。EngineCancelled=false、Pending保留、RequestPlay关闭；完整B01–B19仍active，仅B03整项/B18a子项勾。游戏实测暂缓，旧1021安装18项/其326源码ZIP与原游戏3hash匹配。
<!-- ENGINE-BINDING-LATEST:END -->'''

for name, prefix in [('README.md',''),('.docs/CURRENT-CHECKPOINT.md','../'),('docs/IMPLEMENTATION-STATUS.md','../'),('.docs/foundation-expansion/CHECKLIST.md','../../')]:
    path = r/name; body = path.read_text(encoding='utf-8-sig')
    body = re.sub(r'<!-- ENGINE-BINDING-IN-PROGRESS:BEGIN -->.*?<!-- ENGINE-BINDING-IN-PROGRESS:END -->','',body,flags=re.S)
    old = re.search(r'<!-- (?:ENGINE-NODE-LATEST|ENGINE-BINDING-LATEST):BEGIN -->.*?<!-- (?:ENGINE-NODE-LATEST|ENGINE-BINDING-LATEST):END -->', body,re.S)
    assert old, name
    history = ''
    if 'ENGINE-BINDING-LATEST' not in old.group() and name != 'README.md':
        history = '\n\n### 历史1597（节点观察与标量账本）\n\n' + re.sub(r'<!-- ENGINE-NODE-LATEST:(?:BEGIN|END) -->','',old.group()).replace('**最新：','**历史：')
    body = body[:old.start()] + latest(prefix) + history + body[old.end():]
    body = body.replace('当前离线Runtime170；','当前离线Runtime177；')
    body = body.replace('当前离线AudioContent627，','当前离线AudioContent640，返回ABI2/实际tracker身份关联已接，')
    body = body.replace('| B15 音频/本地内容 | 当前离线AudioContent627；','| B15 音频/本地内容 | 当前离线AudioContent640；返回ABI2和三类tracker独立身份关联13组及实际新桥889/5受控provider联合已验证；')
    if name.endswith('CHECKLIST.md'):
        body = re.sub(r'^- \[ \] B19 分发：当前.*$',f'- [ ] B19 分发：当前1617/1617、25套，565源/22包冻结未安装；返回ABI2、owner投影和tracker/engine独立代次账本已接，自有native3402及真实CLR/账本889/5通过。{d}条去重原始编译诊断，旧1021安装未变；UnityPlayer安全首次发布/V4、实际coordinator/调度清理、游戏冷启/GC、故障矩阵、干净环境/升级及完整B19仍缺。见[证据](../../docs/validation/{b.name}/README.md)。',body,flags=re.M)
    if name == 'docs/IMPLEMENTATION-STATUS.md':
        body = re.sub(r'^\| B19 分发维护 \|.*$',f'| B19 分发维护 | 当前1617/1617、25套，565源/22包冻结未安装；返回ABI2、owner投影和身份账本已接，native3402与真实CLR/账本889/5；{d}条原始诊断。旧安装和原游戏hash匹配。[证据](validation/{b.name}/README.md) | 引擎安全首次发布/V4、实际生产接线、游戏冷启/GC、故障和干净环境/升级完整矩阵仍缺 |',body,flags=re.M)
    path.write_text(body,encoding='utf-8',newline='\n')

summary = f'''# 返回 ABI 修正与协程身份关联

2026-10-25。**本批完整构建 25 套、1617/1617 通过，565 项声明源码和 22 项暂存文件已归档并逐项核对；新包未安装。** 核心成果是修正调度器依赖的 Execute 完整 EAX 返回，并把实际节点观察与既有三类 tracker 的身份连接起来。完整 B01–B19 仍在继续，仅 B03 整项/B18a 子项维持原有验收。见[范围](scope.md)、[正式报告](../../2026-10-25_底层-返回ABI与协程身份-report.md)。

## 为什么需要修正返回 ABI

固定 UnityPlayer 的 scheduler pump 比较完整 `EAX==1`，callback 又可尾跳 Execute。此前通道仅保证 AL；本批将 Execute 的 original、entry、dispatch 和 CLR delegate 全部改为 uint32，完整保留诸如 `0xABCDEF01` 与 `0xA5C3FE81` 的高位。Start/Release 继续保 AL，Detach 保 void，不把值归一化为 bool。新增 `GetAbiVersion()=2`，新桥拒绝旧 helper；Frame112/Completion32/Info112 的标量记录仍为版本1，helper总59 exports。没有宣称保留完整64位RAX。[实际消费路径](scheduler-cleanup-research.md)与[安装/receiver研究](engine-installation-research.md)提供机器码依据；旧1597归档仍只证明其当时AL范围，没有改写旧结果。

## 身份与责任

`NativeRuntime.ReadUnityCoroutineOwner` 借用已有 receiver root，经正式元数据核验 Object 声明的 `m_CachedPtr` 类型/offset/实例字段，确认 MonoBehaviour 继承，前后重验对象与两次字段值；null/变化/错线程拒绝，不读取 native owner/node。该方法目前用真实runtime API加synthetic metadata验证，尚无当前游戏活体对象证明。

实际 `NativeEngineCoroutineIdentityLedger` 独占包装一个节点账本：Start进入时先记录责任，按已验证managed receiver/native owner配对和唯一当前Enumerator连接tracker。同步第一次Execute在Start返回前关联；managed Coroutine handle稍后只允许同owner/slot/generation/Enumerator升级。引擎节点代次与tracker槽代次独立，历史完全释放的node地址不继续污染foreign调用；最近foreign Start遮蔽外层，2048层上限溢出永久保留未跟踪责任。provider只读托管标量，未授取消完成、Ready或根释放。

## 实际验证与局限

| 证据 | 结果 | 能证明的范围 |
| --- | --- | --- |
| [完整构建](build-run.json)、[原始日志](build.log)、[汇总](build-summary.json) | session{session} exit0；25/1617；Runtime177、Audio640、Hooks189 | 当前声明输入的本机完整构建，保留{d}条去重原始编译诊断 |
| [原生真实Dobby](engine-node-native-evidence.json) | 3402检查；136原调用、124观察配对、12无观察；8高位Execute样本/10完整completion | ABI2、caller、异常、worker、detach、node释放与原函数转发，自有目标 |
| [真实native→CLR联合](engine-node-managed-evidence.json) | 5进程889检查，44原调用、28context配对；7条Start身份链 | 实际新桥/dispatcher/identity与node账本；provider/pair及返回handle仍受控，未执行实际tracker backend或IL2CPP metadata |
| [托管预检](managed-preflight.log) | Runtime177/177、Audio640/640；新增13组identity和7组owner投影 | 三类实际tracker采用受控backend，与native联合受控provider是分开的证据 |
| [旧实现反例重放](mask-before-replay/20261025T034151Z-6b979b54/result.json) | redObserved=true，隔离进程正常退出且输入DLL未改变 | 归档旧账本无法通过加强后的foreign遮蔽断言；不计入1617 |
| [输入前后核对](build-input-integrity-check.json)、[源ZIP](source-archive-check.json)、[包ZIP](package-archive-check.json) | 565源/22包逐项hash匹配 | 当前归档；不包含完整工具链、依赖和干净换机复现 |
| [旧安装](previous-installation-check.json)、[原游戏](original-game-check.json) | 18已装文件/326旧源ZIP/3原文件匹配 | 当前游戏仍旧1021安装，本包没有实测 |

原生预检session44490与托管预检session52562均由root观察terminal0。[修正前全量48324](before-mask-fix/build-run.json)虽然1617通过，交叉复核仍发现其Foreign测试允许漏记已验证owner的未知node；该结果及565源/22包保留为历史，未作为最终candidate。修正后先登记scalar scope再标记Unknown，既不借外层身份也不读取provider/node，原有案例新增两个反例并通过[专项回归](mask-preflight.log)，随后另cache完整重建为当前最终结果。全量cache为 `D:/RizToolbox-build-cache/engine-binding-mask-final-20261025`，root唯一builder。三个代理分别处理native ABI/安装研究、身份实现/owner测试/报告、scheduler与内联路径研究；有限交叉复核不称外部独立审计。

## 下一步仍须完成

UnityPlayer 已早于现有 GameAssembly pre-init 存在，现有时机不能证明四入口尚未运行，不能直接普通首patch。下一段按[自有早期加载宿主合同](early-loader-design.md)验证依赖proxy、importer TLS/CRT、worker首次调用及逐写故障，不能从静态依赖图推断已安全。scheduler消费前erase、重复登记、manager销毁直接cleanup、async等待旁路和Start内联清理各有独立责任；四个节点入口并不足以闭合全部未来执行和消费者。继续完成可证明的生产首次发布/独立凭据、coordinator与实际对象/GC接线、全部调度清理及整局资源退休，然后以完整Retry/Return/Loader验证。`EngineCancelled=false`、Pending保留、`RequestPlay`关闭。用户“先继续代码，实测稍后”保持。

复核命令（仓库根、Python3.14）：先 `summarize-build.py`、`freeze-source.py`、`freeze-package.py`，再 `summarize-build.py --staged`、`verify-frozen-inputs.py` 和 `compare-sources.py`（均在本目录）。它们只核对/归档，不安装游戏。归档中的声明输入与当前工作树开始下一批后会分开，不能把历史成功外推到后续源码。
'''
(b/'README.md').write_text(summary,encoding='utf-8',newline='\n')
print('Published current batch and four entry documents from completed proof; acceptance boxes unchanged.')
