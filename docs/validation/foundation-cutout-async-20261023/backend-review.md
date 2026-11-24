# Cutout 异步来源后端的有限核对

2026-10-23，`flavor=null`、`gameRuntimeVerified=false`。范围为新增 `NativeLocalCutoutCoroutineBackend` 的实际源码、其 tracker/source 关系，以及两个 throwing Reset 的固定 PE 有限范围。授权与整体边界见 [scope](scope.md)。本文没有修改 src/tests/native、运行构建、安装或操作游戏；父代理执行的构建及定向检查应以其独立日志为准，本文不补造运行结果。

结论：生产后端已经核验 exact IEnumerator、actual source row、reset Action 的声明与 Target，持有独立 enum/Coroutine 根，并通过已有 typed Start/Move 观察入口接线。两个 Reset 的确只进入 NotSupportedException 路径，不会复位 state。**这些事实只支持两类已观察协程的局部自然结束；动态 provider、UnityEvent listener 及其外置工作仍未封口，Source.Pending 和整局消费者结束保持独立。**

## Evidence

| E-id | source_ref / artifact | content_hash / 核对内容 | repro_command |
|---|---|---|---|
| E-001 | `src/RizToolbox.Adapter/AudioContent/NativeLocalCutoutCoroutineBackend.cs` | `de21c9d6e2be1d983249b129dee5ed56b69e1e0df4ad1c2771fbb7809234b908` | `Get-FileHash src/RizToolbox.Adapter/AudioContent/NativeLocalCutoutCoroutineBackend.cs -Algorithm SHA256` |
| E-002 | 固定 `GameAssembly.dll` 的 ResetDelay `[3AF250,3AF290)` | 64 字节，SHA `5fedf6fbaf76fa2fda445b22667bd7e53381344359dda551f968ba31cd6ecc7b` | 下方有限复核代码 |
| E-003 | 固定 PE 的 Event Reset `[39B5A0,39B5E0)` | 64 字节，SHA `9464093dd95239ed94645e8c6890529d87a3759f879cc9ba49a76bf5bab03926` | 下方有限复核代码 |
| E-004 | `EventTrigger.<StartEvents>b__14_1 [38A700,38A710)` | 16 字节，SHA `b904053c62926849e6cffc4eb7246680105082ac087f21564c410c77257f2ca0`；有效体 `c6413000 c3` | 本批 [既有四函数调查](next-async-evidence.md) |
| E-005 | 实际 tracker/source/backend 代码关系 | source 持有真实 controller/EventTrigger/GO/Transform/list/每 Trigger/events 的独立根；后端从其不可变快照核对身份 | `Get-Content src/RizToolbox.Adapter/AudioContent/NativeLocalCutoutCoroutineTracker.cs` 及对应 Source/SourceBackend |

source_type 均为 file/command；observed_at 为 2026-10-23；linked_workitem 为 Cutout 异步来源的局部观察与完成记录退休，supersedes=none。既有导入/导出分诊沿用 [固定样本 triage](../local-launch-20261022/triage.json)。本文不是新的全库调查，也没有扩大 [EventTrigger 5 函数/475 指令调查](event-trigger-research.md)。

实际输入 hash：GameAssembly 为 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`；dump.cs 为 `092cdb96914ccb7af3f31d9d2e703e00dd6ddff63fd559f11ac6b42e6635c1cb`；script.json 为 `a51cc6ee5881110fbeb884679b13d20bce4ac4df9055bf535f3cdc476bf35d10`。script.json 只用 Python JSON 解析，不以全文搜索或输出作为依据。

## Findings

### F-001：两个 Reset 不提供正常复位路径

`severity=n/a_re`、`category=reverse_algo`、`status=validated-static`、`confidence=high`，证据 E-002/E-003。

- Delay Reset：`3AF256` 覆盖 RCX this，未保留 enumerator receiver；`3AF272 call F1B1C0` 调用 `NotSupportedException..ctor`；`3AF289 call 264FB0`，其后仅两个 `int3`。
- Event Reset：`39B5A6` 覆盖 RCX this；`39B5C2 call F1B1C0`；`39B5D9 call 264FB0`，其后仅两个 `int3`。
- 两个完整 64 字节范围均没有对 iterator state `+10`、current `+18`、Trigger/seconds 等实例字段的写入。`264FB0` 是前批已核的公共抛异常路径，最终进入与 `il2cpp_raise_exception` 相同的 raise helper。
- 后端先以 exact nested declaring class、方法名 `System.Collections.IEnumerator.Reset`、零入参、InstanceVoid 及对应 RVA 验元数据，再核代码范围 SHA。它没有调用 Reset，也没有安装 Reset hook；局部退休的依据仍是已观察的自然 MoveNext false 与实际 drain，不能因 Reset 会抛就释放还活的 iterator。

### F-002：reset Action 使用 exact MethodInfo，而非仅地址或 usage slot

`severity=n/a_re`、`category=design`、`status=validated-source`、`confidence=high`，证据 E-001/E-004。

`38A700` 与其它声明存在共享代码别名。后端从实际 `EventTrigger` 声明解析 `<StartEvents>b__14_1`，要求 exact 零参 InstanceVoid/RVA，并检查 MethodInfo 的 VirtualMethodPointer 等于该入口、Flags 无 virtual 位 `0x40`，再核 16 字节 SHA。没有把磁盘 usage slot `1E55790` 解释为 runtime MethodInfo。

每次读 reset enum 时都检查 todo 是 exact `System.Action`，`m_target` 与 `method_code` 为 source 的 actual EventTrigger；`method` 等于上述 exact MethodInfo，`method_ptr` 和 `invoke_impl` 等于其地址，`delegates=null`，`method_is_virtual=false`。因此同 RVA 的别名方法、另一 Target、多播、虚方法或错误 MethodInfo 都不能满足本回执。此结论是源码 gate 的行为，尚未在真实游戏 Action 实例中执行验证。

### F-003：元数据及实际对象核对清单

`severity=n/a_re`、`category=design`、`status=validated-source`、`confidence=high`，证据 E-001/E-005。

| 对象 | 核验内容 |
|---|---|
| 平台和镜像 | Windows x64、Unity 2022.3.62f2、profile SHA 与实际磁盘 GameAssembly SHA 均匹配 |
| 两类 enum | exact nested `SimpleCoroutine.<Delay>d__4` 与 `EventTrigger.<StartEventsEnumerator>d__16`；state Int32 `+10`、current Object `+18`；每次读取核独立根的身份、角色及 exact class |
| MoveNext | Delay `3AF0A0`、Event `39B4D0`，零参 bool-return-0；与已有共享物理 patch 增加逻辑 observer |
| typed Start | 已有 `StartCoroutine(IEnumerator)` 与 `StartCoroutine_Auto` 的 exact metadata/alias；真实 host、caller `38A60C/38A6B2`、物理 StartEvents dispatch generation 由 tracker/source admission 核验 |
| Delay 字段 | seconds Single `+20`、todo Action `+28`、provider Func&lt;Single&gt; `+30`；seconds 必须 finite，实际 provider 可为 0 或 exact closed field class |
| Event 字段 | enum.trigger 为 exact Trigger `+20`；Trigger.events 为 exact UnityEvent `+10`、offset Single `+18`；身份/事件/offset bits 必须匹配 preactivation source 的真实 row |
| Delegate 官方字段 | `method_ptr IntPtr +10`、`invoke_impl IntPtr +18`、`m_target Object +20`、`method IntPtr +28`、`method_code IntPtr +40`、`method_is_virtual Boolean +70`；MulticastDelegate.delegates `Delegate[] +78` |
| Coroutine 输出 | 只保留 Start 实际返回的 exact Coroutine class；同步自然结束输出为零由 tracker 正确处理，后端不制造替代 handle |

Source 自己已独立 root controller、EventTrigger、其 GO/Transform、triggers list 与各 Trigger/UnityEvent；tracker 和后端持有 Source。因此 backend 中的 Trigger/events/host 数字只用于比较已有实际根闭包，不能供无根调用方取得原生操作权限。实际 enum 又独立 root，持有它自己的 Action/provider 引用。源码共 64 个 enum 槽和 64 个 Coroutine 槽，没有把数值 identity 当成资源 owner。

后端没有产生 owned `Arg.Value` 或调用带 owned 参数的 native Invoke；它使用真实字段读与独立 Root。SourceBackend 的 get_Item owned index 参数继续由其 `NativePublicationArgumentRecords` 在 runtime adoption 前记录；这里没有另加未经记录的数值参数。

### F-004：provider 是明确保留的动态消费者边界

`severity=n/a_re`、`category=design`、`status=validated-source`、`confidence=high`，证据 E-001/E-005。

**provider 非零时只验证它的 exact `System.Func<System.Single>` class，没有验证 provider MethodInfo、Target、完整 invocation list 或方法体。** 它仍能运行动态代码，并可能把工作交给其它对象。状态机允许 Delay 的 state=1 provider 路径递减 seconds，也允许 state=2 WaitForSeconds 路径；不能错误要求 seconds 全生命周期不变。但“provider 已不再被此 enum 使用”不等于 provider 曾产生的外置工作都结束。同样，UnityEvent.Invoke 的同步返回及 DelayedEvent false 不封闭动态 listener 的外置工作。后端没有清 Source.Pending 或签发 whole-run 结束 token。

### F-005：局部 free 故障和观察/authority 故障分开处理

`severity=n/a_re`、`category=design`、`status=validated-source`、`confidence=high`，证据 E-001/E-005。

释放先要求 tracker 正在退休同 slot/enum/generation，且纯身份已经撤回、已做二次 drain。依次释放 Coroutine 根和 enum 根；每根在 free 前重新读 Source idle 与全部三 binding 的实际活动，再复核 `owner.ObservationAuthorityUnknown` 和 Source.Unknown。worker、reentry、native 计数未知、其它同 launch observer 的 fault 都会阻止后续 free。

某 root 的 Dispose 抛错时保留其 root/一次尝试记录，设置 backend.CleanupUnknown，但如果观察/authority 没有故障且后续活动仍 known0，另一独立根仍得到唯一一次释放尝试。因此用于这段清理的 `ReadActivityCore(false)` 只忽略本地清理错误；它绝不忽略 `ObservationAuthorityUnknown`。任何失败最终 aggregate throw，slot 不被伪装成可复用，也不重试 unknown free。

## Path 与验证限界

P-001，`path_type=callflow`：preactivation Source 根与不可变闭包 → backend exact metadata/code gate → typed Start before 采用实际 enum → 同步/后续 MoveNext 配对 → 自然 false/Start 输出确认 → 撤纯身份/二次 drain → 各根一次 free。证据 E-001–E-005，发现 F-001–F-005。

本批受控 tracker 回归能覆盖状态、同步首 Move、worker、重复来源、容量与退休算法；**没有直接 production backend 元数据故障、错误原生 Action 或 root-free 故障的实机 fixture**。现有 helper/Dobby 测试也不能替代真实游戏 enum/delegate/class 的实际读取。本文没有新跑独立 native ABI runner；父代理完整 build 当时仍在运行，结果另以最终摘要与日志交付，不在此提前宣称成功。

## 有限 Reset 复核

以下代码只读取磁盘并解码同样两个 64 字节 Reset，不加载 DLL、不写文件、不扩展被调函数；需要本机已有 pefile/capstone。

```python
from pathlib import Path
import hashlib
import pefile
import capstone

raw = Path(r"D:\SteamLibrary\steamapps\common\Rizline\GameAssembly.dll").read_bytes()
assert hashlib.sha256(raw).hexdigest() == "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79"
pe = pefile.PE(data=raw, fast_load=True)
decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
for start, expected in [
    (0x3AF250, "5fedf6fbaf76fa2fda445b22667bd7e53381344359dda551f968ba31cd6ecc7b"),
    (0x39B5A0, "9464093dd95239ed94645e8c6890529d87a3759f879cc9ba49a76bf5bab03926"),
]:
    data = pe.get_data(start, 0x40)
    assert hashlib.sha256(data).hexdigest() == expected
    print(hex(start), expected)
    for item in decoder.disasm(data, start):
        print(hex(item.address), item.bytes.hex(), item.mnemonic, item.op_str)
```

Timeline：2026-10-23 实读固定样本两个 Reset 并核对 generated dump；按实际新 tracker 接口新增后端，有限交叉审查补 ObservationAuthorityUnknown；父代理开始统一构建后仅新增本记录。没有扩大到 provider/listener 的动态执行证明。
