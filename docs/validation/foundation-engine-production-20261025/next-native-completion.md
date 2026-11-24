# 下一批：从 Stop 请求关联走到真实协程完成

2026-10-26，普通 PE 逆向，`flavor = null`。本次先确认真实原生路径，再实现有限的 Stop 物理调用与实际 Detach 节点代次关联。关联仅产生取消候选，未新增 scheduler 或 async hook，未发取消完成凭据，也未解除 Pending。后续 K 批已完成统一构建和 698/698 受控回归（取消关联 13 组）；详见 [K 批记录](../foundation-engine-retirement-20261026/README.md)。本文件的原生研究仍仅为磁盘事实，未变成游戏动态证据。

范围沿用 [scope.md](scope.md)：真实游戏目录只读，不启动或停止游戏。研究使用 Python 3.14、pefile 2024.8.26、Capstone 5.0.6；IDA 工具索引不可用，未使用 GUI、LoadLibrary 或目标函数调用。完整 B01–B19 验收范围不变。

## 已核实的输入与证据

| Evidence | 原件及 SHA256 | 有限观察 |
|---|---|---|
| E1 | `D:/SteamLibrary/steamapps/common/Rizline/UnityPlayer.dll`；`43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1` | 31,006,120 字节，AMD64，ImageBase `180000000`，ImageSize `1E65000`；19 组普通导入及 2 个导出；仅磁盘读取 |
| E2 | [cancellation-engine-evidence.json](../foundation-operation-preinit-20261024/research/cancellation-engine-evidence.json)；`169d7e54d9b19db0fc40afc4bf93271c730547675eac52b454fec93f25cf4a4b` | 16 个完整函数及 CHAININFO 合并范围、机器码与反汇编，含四节点入口及 Start 内联分支 |
| E3 | [inspect-cancellation-engine.py](../foundation-operation-preinit-20261024/research/inspect-cancellation-engine.py)；`fc64b6124f8b2bf2ce7efe6011fe2106a87bd3fda7855abde0f742bb5411dde3` | E2 的既有复核脚本；运行会重写 E2，本次未运行或改写历史原件 |
| E4 | [cancellation-research.md](../foundation-operation-preinit-20261024/cancellation-research.md)；`2c7cb6daaaee7370c14e1260c82e6734b5cff1523d19037d11ba91c568a1d7a4e` | 既有 icall/节点/调度静态事实和未完成边界 |
| E5 | 本次工具输出中的有界磁盘反汇编；未另存历史 JSON | 补读 `78FC50`、`44D630`、`447C30`、`447D60`；下表 body SHA 可按固定磁盘复核，无动态证明 |

E1 导入分类：KERNEL32 224、USER32 113、VERSION 3、ole32 11、SHLWAPI 2、SETUPAPI 5、ADVAPI32 19、GDI32 4、SHELL32 5、OPENGL32 6、WINMM 23、OLEAUT32 5、IMM32 8、WINHTTP 4、bcrypt 1、HID 14、CRYPT32 7、WS2_32 46、dwmapi 2。它包括系统、图形、文件/设备、网络和加密能力；导入存在不说明本次调用了这些能力。

| 函数范围 RVA，左闭右开 | 完整 body SHA256 | 说明 |
|---|---|---|
| `792240..7929BF` | `6a24dc40b10797a33529428b7d82821f1cb646a72d9108cccb884bfe2f8d7721` | Start，E2 |
| `78FC50..79014B` | `938b7a48664df2e0b0195fa2075f130f285a9498e0ce87693269dc2c6db3696f` | yield 类型分流及独立 wait 发布，E5 |
| `44D630..44D756` | `ce57348a38b6eb9041cece746e038dddd5c676721f39c4c2f857669c03b74f20` | scheduler 登记，E5 |
| `447C30..447D60` | `400ac94b4e996135f4a5602be15a0bcebf3f123e8f650b4dbb003544b6225abb` | 无 cleanup 的调度记录消费，E5 |
| `447D60..447FAF` | `cafab343388f190607027203284728c3ce672cfe89115a50257e0faae66e3d7c` | scheduler pump，E5 |

可从项目根只读复核一个补读函数的范围字节；此命令不输出文件、不执行目标代码：

```powershell
@'
from pathlib import Path
import hashlib, pefile
p = Path('D:/SteamLibrary/steamapps/common/Rizline/UnityPlayer.dll')
data = p.read_bytes()
assert hashlib.sha256(data).hexdigest() == '43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1'
pe = pefile.PE(data=data, fast_load=True)
for begin, end in [(0x78fc50,0x79014b),(0x44d630,0x44d756),(0x447c30,0x447d60),(0x447d60,0x447faf)]:
    print(hex(begin), hex(end), hashlib.sha256(pe.get_data(begin,end-begin)).hexdigest())
'@ | C:/Python314/python.exe -
```

## 已核事实与对应实施路径

### F1：四入口不能证明 Start 自身的内联清理

`evidence_ids = [E2, E4]`；静态结论 validated，生产动态未验证。`7924CD` 直接减少自身 node 引用并执行内联清理，这条路径不调用自身 `78F680`。`79256D` 可能 free `0x88` 字节 node，随后 `792576` 清输出槽，`79257D` 跳公共 epilogue。另一条路径 `792582..79258E` 释放并清 wrapper script reference，`792596` 清输出槽，`79259D` 跳 epilogue。Start null 输出或 AL 返回不能证明实际选择了这些路径。

P1，callflow：实际 Start 序号及 owner/Enumerator → `7924CD` 前复制实际 RBX node 身份和减引用前计数 → 实际 `79257D` 或 `79259D` 终点回执 → 同一 Start 正常 Complete/Dispose 及 Execute/调用排空 → 该代节点内联清理候选。每步依赖 E2 的固定代码身份，仍需新的动态记录。

最少新增 `InlineBegin`、两种 `InlineEnd` 的原生旁路记录或等价强度控制流回执；携带线程/物理 Start 序号、node 代次、native owner 和 enum handle/type/cache，终点不再解引用 node。减引用前计数应为 1，异常/非法计数不能因到达分支即接受。这些 RVA 是指令位置，不能伪装普通函数签名交给现有 Dobby 四函数入口。保寄存器/flags、跳板及异常展开合同须独立验证；现有 112/32 字节 ABI 不应偷用 reserved 字段加入事实。

### F2：scheduler 正常消费与取消删除是不同责任

`evidence_ids = [E2, E5]`；静态结论 validated，生产动态未验证。

| 新入口或位置 | ABI/事实 | 必要作用 |
|---|---|---|
| `44D630` | RCX tree、RDX outIteratorSlot、R8 descriptor；正常返回原始 RAX 指针，非 bool | 登记实际 record 及代次，正常返回读取实际输出槽 |
| `447AD0` | RCX scheduler、RDX descriptor、R8 treeEntry；删除后可能调 cleanup | 取消删除与 cleanup 责任 |
| `447C30` | RCX scheduler、R8 treeEntry；入参 RDX 不使用；仅摘树不调 cleanup | 正常消费；漏它就无法闭合记录寿命 |
| `78F5E0` | `uint32(owner*,node*)`；匹配 owner 后尾跳 Execute，否则 EAX=1 | 实际 callback 窗口，包括不进入 Execute 的分支 |
| `447D60` 内 `447EEA/447F51` | 实际间接 callback；此前 `447EDF/447F46` 已消费记录 | 记录已摘除但回调仍在途的窗口 |

`44D630` 输入 descriptor `+18=node payload`、`+20=callback`、`+28=cleanup`、`+30=owner ID`，本协程链 callback=`78F5E0`、cleanup=`78F680`；输出 record 的字段偏移相对多 `20`。记录身份必须为实际 treeEntry+本地代次+node 代次，并保留 mask/登记 epoch。取消删除、正常消费和 callback 返回分开配对；禁止成员归零立即授完成。`447EF2` 比较完整 `EAX==1` 后才调 cleanup，Execute/回调 ABI 不能按 AL 或 bool 归一化。

P2，callflow：实际登记输出 record → 取消删除或正常消费 → callback 与 cleanup 各自物理完成 → 记录及在途责任归零。E2/E5 仅建立路径，尚未证明所有 registry、间接调用或重复登记都覆盖。

### F3：独立 wait 发布不经过 scheduler 登记

`evidence_ids = [E2, E5]`；静态结论 validated，实际 wait 分发路线仍 candidate。

`78FC50` 可识别 RCX=node、RDX=实际 yield/wait managed reference；整体按 void 使用，不依赖未定义 RAX。`78FDAC`、`790129` 注册 scheduler；另一路 `79006C/790077/790082` 将 callback=`78F5E0`、cleanup=`78F680`、node 写入 wait 的 `+10/+18/+20`，owner ID 写入 `+28`。`790098` 保存 node `+78`，`79009C` 增 wait 引用。普通 Release 的 `78F6F5..78F711` 与 Start 内联 `792527..792543` 对应清除这些字段及释放 wait 引用。

P3，callflow：wait 发布代次 → callback/cleanup 可能执行 → 实际撤销 → 已捕获窗口排空。发布/撤销需独立记录，不能由 scheduler 数量代替。**实际 wait 分发函数 RVA、本路径全部写者和并发语义本次未闭合**；仅在 `78FC50` 前后观察不足以证明分支和撤销。下一次研究应优先沿 wait `+10/+18/+20` 的实际间接读取定位消费者，避免重新泛搜全部协程函数。

## 本批已写代码与未授出的权限

[NativeCoroutineCancellationLedger](../../../src/RizToolbox.Adapter/AudioContent/NativeCoroutineCancellationLedger.cs) 新增实际 engine Detach scope 关联候选。输入必须是 IdentityLedger 当前主线程栈顶、Relevant、未 Complete/Dispose 的真实 Call，并由对应 NodeLedger 独立确认实际物理记录。它复制 request/parent/root ID、Stop 和 Detach 序号、StartSequence、tracker owner/receiver/slot/generation、native owner、node/代次和 enum handle。与请求 Before/Matched 的不可变身份、当前 provider 和再次读取的 Call tuple 逐项匹配，期间改变或重入保留 Unknown。

foreign 主线程 Stop 只有在 owned 祖先存在时建立物理 mask；mask 不读 provider、不增加 owned 请求数，但严格配对 Complete/Dispose。mask 内的 owned Detach 不能借外层 Stop。Observer 的 foreign worker Stop 直接略过，owned worker 先保留 Pending 再标 Unknown，不读取 provider。Named Stop 只记录实际观察到的子集，仍 `MatchUnproven=true`，不能授全效果集合。

新 [NativeCoroutineCancellationEngineAssociationTests](../../../tests/RizToolbox.AudioContent.Tests/NativeCoroutineCancellationEngineAssociationTests.cs) 有 13 组受控检查，使用实际 request/identity/node 账本及受控 frame/provider，已在 K 批 r3 完整 698 项回归中通过。追加的同帧 Start→Stop 测试要求关联时通过原有单调升级规则确认 handle；不把尚未刷新这一正常时序当身份漂移。它们不能证明真实 UnityPlayer hook、IL2CPP GC、原生取消完成或真实 scheduler/async 路线。

下一批顺序为：真实 Start 内联回执 → Stop scope 到 exact node 代次接线 → scheduler 登记/消费/删除与回调账本 → wait 发布/撤销和消费者 → 独立逐记录取消完成判定。只有请求匹配、普通或内联清理、所有执行/调用排空、scheduler 与 wait 来源封口、future producer 禁止再发布后，才可转换逐记录完成。writer/delegate/listener/媒体与根仍由各自 owner 独立退休。`Release AL=0` 仅是节点清理事实，Pending 的保守保留不是完整任务完成。

