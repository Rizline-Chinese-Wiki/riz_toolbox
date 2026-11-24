# Cutout 的 EventTrigger 直接分派路径

2026-10-23，有限静态调查，`flavor=null`、`gameRuntimeVerified=false`。本次只读取固定 PE、dump.cs 与 Python 解析的 script.json，未修改构建输入、运行构建、调用游戏或操作 UI。授权来源是父任务要求追踪 Cutout 的直接事件路径；调查范围严格限定以下 5 个函数、475 条指令，不扫描全库或拓展 listener 的动态调用图。既有导入/导出分诊见 [triage](../local-launch-20261022/triage.json)。

**结论：Cutout 的 MoveNext 返回 false，不能证明其启动的事件消费者已经结束。** 它会调用另一个 MonoBehaviour 组件 EventTrigger；EventTrigger 可能同步调用 UnityEvent，或者在自身启动延时协程。对 controller 调用 StopAllCoroutines 不能独立覆盖 EventTrigger 的协程；即使 EventTrigger 随 clone 销毁，已经调用的 listener 仍可能把工作交给其它对象。

## Evidence

固定样本 `D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll` SHA-256 为 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`。以 Python pefile 映射 RVA、capstone x64 解码，再由 script.json 的下一 ScriptMethod 地址界定范围，实际读取如下：

三份输入分别实读核对：GameAssembly 为上述 `853d…b79`；`dump.cs` SHA-256 为 `092cdb96914ccb7af3f31d9d2e703e00dd6ddff63fd559f11ac6b42e6635c1cb`；`script.json` SHA-256 为 `a51cc6ee5881110fbeb884679b13d20bce4ac4df9055bf535f3cdc476bf35d10`。script.json 只通过 Python JSON 解析，没有文本搜索或完整打印。

| E-id | 函数及完整有限范围 | 指令数 | 范围 SHA-256 |
|---|---|---:|---|
| E-001 | `Cutout.MoveNext [3C5900,3C5A00)` | 67 | `312cc89bebd9387088be77cdfbb7211c0e27448cb7d162ca76102076a0e8b29a` |
| E-002 | `EventTrigger.StartEvents [38A460,38A6E0)` | 154 | `4ade395caa0b2f98cc975a2a0ab505d1ffc2bfc843b54577f9680ab170470992` |
| E-003 | `StartEventsEnumerator.MoveNext [39B4D0,39B5A0)` | 61 | `9912c95fe348279dc7e73f3a4ecb081c902fb452704bf43bbc6a33ba04a4a006` |
| E-004 | `SimpleCoroutine.SimpleDelay [3AE340,3AE3E0)` | 43 | `2028073281255117de961b3cf62352098696c272e0533091c1c8907df338ee13` |
| E-005 | `UnityEvent.Invoke [15642E0,1564550)` | 150 | `625f2c70ab6f2aee5aef7c8aca789fc925873511c71bb14b683ec9e6e2b4e43c` |

source_type 为磁盘 file/command；observed_at 为 2026-10-23；linked_workitem 为活动 LOCAL 退休的事件消费者补证，supersedes=none。本表范围哈希可复现为 `hashlib.sha256(pefile.PE(path).get_data(start, end-start)).hexdigest()`；这只是磁盘证据，不证明实际运行到这些分支。

元数据来源为 `C:/Users/71957/Desktop/绿豆诡计v2/output/il2cpp_output/dump.cs`：93244–93300 的 Trigger/EventTrigger 及其协程、114054–114225 的 UnityEvent/InvokableCall 类型；controller 的 `cutoutMaskEventTrigger` 字段为 `+130`。生成的符号/字段仍须在游戏接线时由实际 NativeRuntime metadata 检查，不授权裸偏移调用。

## Findings

### F-001：Cutout 向另一 MonoBehaviour 交出事件工作

`severity=n/a_re`、`category=design`、`status=validated-static`、`confidence=high`，证据 E-001/E-002。

- `3C5934 mov rcx,[enumerator+20]` 取实际 controller，`3C5951 mov rcx,[controller+130]` 取其 `cutoutMaskEventTrigger`，`3C5963 call 38A460`。
- EventTrigger 的实际 GO/Transform/场景/与 clone 的父子关系是运行时对象事实。本函数体没有证明它一定是 clone 子对象，也没有证明它属于旧 Level 场景。
- Cutout 的后续阶段 `3C59E3 call Object.Destroy@153E680` 只请求销毁 controller GO；这个调用和 MoveNext 结束都不是其它事件宿主/监听者结束证据。

### F-002：StartEvents 自身有两类延时来源，也会同步分派

`severity=n/a_re`、`category=reverse_algo`、`status=validated-static`、`confidence=high`，证据 E-002/E-003/E-004。

EventTrigger 元数据字段为 `refuseInvokeBeforeEventsFinished+23`、`refuseInvokeWhileTouchBlocked+24`、`triggers+28`、`_refuseInvoke+30`；Trigger 持有 `UnityEvent events+10`、`float offset+18`。

1. `_refuseInvoke` 或 touch 条件可以直接拒绝本次调用，不能把“StartEvents 返回”统一理解为已分派或已完成。
2. 当 `+23` 为 true：`38A50C` 设置 `_refuseInvoke=true`，计算 triggers offset 的 Max 后，`38A5ED mov rdx,rdi` 将 **EventTrigger 本身**作为新 Action 的 Target；`38A604 mov rcx,rdi`、`38A607 call SimpleDelay@3AE340` 也把它作为协程 host。该 Action 的实际方法指针来自 metadata slot，本轮未进一步解释其函数体；不能以名称推断它已执行。
3. `38A662` 读每个 Trigger.offset，`38A66A` 与双精度阈值比较。阈值在 RVA `17C4BC0` 的实际字节为 `8d ed b5 a0 f7 c6 b0 3e`，值 `1e-6`。小于等于阈值的正常值在 `38A683` 取 Trigger.events，并于 `38A68E call UnityEvent.Invoke@15642E0` **同步**分派。NaN 的比较行为须保留实际指令语义，不能当作合法 offset。
4. 大于阈值的分支 `38A69F call StartEventsEnumerator@38A3F0`，随后 `38A6AA mov rcx,rdi`、`38A6AD call MonoBehaviour.StartCoroutine@153D380`。这也是 **EventTrigger 自身**的协程，而不是 controller 的协程。
5. 该 enumerator 在 `39B521` 读其 Trigger.offset，`39B537` 创建 WaitForSeconds；恢复后 `39B57A` 取 Trigger.events，`39B585 call UnityEvent.Invoke@15642E0`，然后返回 false。
6. SimpleDelay 把收到的 Action 保存到它新建的 enumerator `+28`（`3AE3A5`），并以原 host 为 RCX 尾调 `StartCoroutine@153D380`（`3AE3D0`）。因此 StartEvents 的正常返回不会等待这些异步工作。

这里的“另一宿主”指另一个 MonoBehaviour 组件；它可能与 controller 位于相同或不同 GO，必须实际核验。仅 controller.StopAllCoroutines 的正常返回不能证明这些协程已停止。

### F-003：真实 UnityEvent listener 是静态边界之外的动态来源

`severity=n/a_re`、`category=design`、`status=validated-static`、`confidence=high`，证据 E-005。

- `156435A` 调用 `PersistentCallGroup.Initialize@1550EE0`；随后合并/遍历 InvokableCallList 的持久、运行时和执行列表。
- `1564470/15644C2` 调用 `InvokableCall.Invoke@154E5B0`；另一分支 `156451F call [class+178]` 通过实例虚表分派，MethodInfo 来自 `class+180`。
- dump 显示 `UnityEventBase.m_Calls+10/m_PersistentCalls+18`；`InvokableCallList.m_PersistentCalls+10/m_RuntimeCalls+18/m_ExecutingCalls+20`；`PersistentCall.m_Target+10/m_MethodName+20/m_Arguments+30`；`InvokableCall.Delegate+10`。这些容器/委托都能保留 listener 对象；具体 listener 是否指向旧 controller、其它持久对象或普通 closure，只能从实际实例及其方法/Target 确认。
- 本轮没有读取 prefab/scene 序列化 listener 实例，没有分析 AddListener 的实际调用历史，也没有继续反汇编 listener。**因此不能证明其只播放动画、不会启动新的 Delay/协程，或不会把旧 controller 捕获到其它容器。**

## Path：退休中要补的实际来源

P-001，`path_type=callflow`：controller → `cutoutMaskEventTrigger` → `StartEvents` → 同步 UnityEvent 或 EventTrigger 自有延时协程 → 动态 listener。

1. 在首次 Cutout 分派前捕获并强持 exact EventTrigger、其 GO/transform/scene 和 triggers 的实际列表；完整验证类型、数量、版本、每个 Trigger/events/offset。迟到于同步分派的捕获不能追溯证明此前无外置工作。（F-001/F-002）
2. 记录 StartEvents 的真实入/出、每个延时操作的实际 host/enumerator/Coroutine 输出与终结；同步 Invoke 也必须有在途配对。controller StopAll 不代替 EventTrigger 操作排空。（F-002）
3. 对事件的 persistent/runtime/executing listeners 逐项建立实际 Target/Method 和调用来源。只接受已审核且有结束观测的行为；动态目标未知、列表在读取时变化或扫描超限即保持未证实。不要通过清空共享 UnityEvent 或停止全局 manager 的全部协程制造成功。（F-003）
4. 若 EventTrigger 真为自有 clone 子树，可以定向取消其自有操作并在 clone 销毁后验证其组件/GO 确实 dead；若是独立/持久对象，须单独排空此 run 生成的操作。两种分支都不能忽略已经转交给 listener 的工作。
5. 只有这条事件支路、controller 自有协程、场景转场、菜单锁、结果 UI/队列和原生 hooks 各自有有效完成/排空证据后，才能推进完整消费者结束。根数归零、UI Close、scene 完成、Cutout MoveNext false 均不单独签发该证明。

2026-10-23 时间线：先核 Cutout 的确切 callsite，再读 StartEvents，随后只沿其直接 Enumerator/Delay/UnityEvent 三个目的地检查；累计 5 函数/475 指令即停止。后续需要实际对象/listener 与原生操作 ledger，未在本调查中执行。

## 有限复核脚本

在仓库外或根目录均可用 `C:/Python314/python.exe` 执行以下代码；依赖本机已有 pefile/capstone，不安装依赖，不加载 DLL，不写文件。代码固定仅解码同样五个范围，打印输入哈希、各范围哈希/指令数及本文列出的关键指令，不扩展被调用函数。

```python
from pathlib import Path
import bisect
import hashlib
import json
import capstone
import pefile

reverse = Path(r"C:\Users\71957\Desktop\绿豆诡计v2\output\il2cpp_output")
inputs = {
    "game": (Path(r"D:\SteamLibrary\steamapps\common\Rizline\GameAssembly.dll"),
             "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79"),
    "dump": (reverse / "dump.cs", "092cdb96914ccb7af3f31d9d2e703e00dd6ddff63fd559f11ac6b42e6635c1cb"),
    "script": (reverse / "script.json", "a51cc6ee5881110fbeb884679b13d20bce4ac4df9055bf535f3cdc476bf35d10"),
}
bodies = {}
for name, (path, expected) in inputs.items():
    bodies[name] = path.read_bytes()
    observed = hashlib.sha256(bodies[name]).hexdigest()
    assert observed == expected, (name, observed)
    print(name, observed)
symbols = {}
for item in json.loads(bodies["script"])["ScriptMethod"]:
    symbols.setdefault(item["Address"], []).append(item["Name"])
starts = sorted(symbols)
pe = pefile.PE(data=bodies["game"], fast_load=True)
decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
scopes = [
    (0x3C5900, 0x3C5A00, 67, "312cc89bebd9387088be77cdfbb7211c0e27448cb7d162ca76102076a0e8b29a"),
    (0x38A460, 0x38A6E0, 154, "4ade395caa0b2f98cc975a2a0ab505d1ffc2bfc843b54577f9680ab170470992"),
    (0x39B4D0, 0x39B5A0, 61, "9912c95fe348279dc7e73f3a4ecb081c902fb452704bf43bbc6a33ba04a4a006"),
    (0x3AE340, 0x3AE3E0, 43, "2028073281255117de961b3cf62352098696c272e0533091c1c8907df338ee13"),
    (0x15642E0, 0x1564550, 150, "625f2c70ab6f2aee5aef7c8aca789fc925873511c71bb14b683ec9e6e2b4e43c"),
]
anchors = {0x3C5951, 0x3C5963, 0x3C59E3, 0x38A50C, 0x38A5ED,
           0x38A604, 0x38A607, 0x38A662, 0x38A66A, 0x38A68E,
           0x38A69F, 0x38A6AA, 0x38A6AD, 0x39B521, 0x39B537,
           0x39B57A, 0x39B585, 0x3AE3A5, 0x3AE3D0, 0x156435A,
           0x1564470, 0x15644C2, 0x156451F}
seen = set()
count = 0
for start, end, expected_count, expected_hash in scopes:
    assert starts[bisect.bisect_right(starts, start)] == end
    data = pe.get_data(start, end - start)
    digest = hashlib.sha256(data).hexdigest()
    assert digest == expected_hash
    instructions = list(decoder.disasm(data, start))
    assert len(instructions) == expected_count
    count += len(instructions)
    assert count <= 2000
    print(hex(start), hex(end), digest, len(instructions), symbols[start])
    for instruction in instructions:
        if instruction.address in anchors:
            seen.add(instruction.address)
            print(hex(instruction.address), instruction.bytes.hex(),
                  instruction.mnemonic, instruction.op_str)
assert len(scopes) == 5 and count == 475 and seen == anchors
assert pe.get_data(0x17C4BC0, 8).hex() == "8dedb5a0f7c6b03e"
print("5 bounded functions / 475 instructions; static only")
```
