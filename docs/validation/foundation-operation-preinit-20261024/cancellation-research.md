# 引擎取消完成：固定 UnityPlayer 的实际下一入口

2026-10-24。**Stop 确实进入 UnityPlayer；现在已找到实际 native coroutine 节点、owner 链、调度登记和执行引用的静态闭合路径。下一步应观察 `UnityPlayer+793840` 的节点撤销、`+78F680` 的最后引用清理和 `+78F840` 的执行窗口，并先用真实 Start 建立节点身份。** 只挂 Stop 返回或节点摘链仍不够：正在执行的协程可先被摘链，直到执行引用退出才进入最终清理。本稿不签发 EngineCancelled，不解除现有 Pending，也未执行游戏代码。

## 证据与复核方式

仅磁盘 PE/机器码读取，无 LoadLibrary、游戏/UI、native 调用、构建或安装。采用 ida-reverse 技能；当前工具表没有可调用 IDA API，使用已有 Python 3.14、pefile 2024.8.26、Capstone 5.0.6 静态读取。固定输入：

| 输入 | SHA256 | 大小 / 映像 |
| --- | --- | --- |
| `D:/SteamLibrary/steamapps/common/Rizline/UnityPlayer.dll` | `43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1` | 31,006,120 bytes；AMD64；ImageBase `180000000`，ImageSize `1E65000` |
| 同目录 `GameAssembly.dll` | `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79` | 35,146,752 bytes；此前六 Stop wrapper 的固定输入 |

实际输出 [cancellation-engine-evidence.json](research/cancellation-engine-evidence.json)，SHA256 `169d7e54d9b19db0fc40afc4bf93271c730547675eac52b454fec93f25cf4a4b`，保存 16 个完整函数范围、机器码、body hash、每条指令和 `.pdata`/unwind 链。部分函数被拆成多个 RUNTIME_FUNCTION，脚本沿 CHAININFO 合并同一 primary 后完整解码，不在第一个 RF 或第一个 ret 截断。可从仓库根只读复核：

```powershell
python docs/validation/foundation-operation-preinit-20261024/research/inspect-cancellation-engine.py
```

脚本只读取两个固定 DLL，重写同批 research JSON；不映射或调用它们。GameAssembly 侧的 wrapper、icall 字符串/缓存及声明依据沿用[六入口研究](../foundation-cancellation-request-20261024/research.md)和[原始 Stop ABI](../foundation-cancellation-request-20261024/stop-abi.json)。生成的 dump/script 只作命名辅助，没有当作活体 metadata。

## E1 → F1：icall 的目标确实在引擎

UnityPlayer 的实际注册函数 `[FD7BC0,FD7C6D)` 同时读取函数表 `18D4AE0` 和名称表 `18DB7A0`。`FD7BF0` 是 `4B8BB426E04A8D01`，`FD7C01` 是 `4B8BAC26A0B78D01`；循环将同一 index 的 name/function 交给注册回调，并在 `FD7C45` 比较 `0xD97`（3479）。这不是只凭字符串猜测函数地址。

| name table index | 实际名称 | UnityPlayer target RVA |
| --- | --- | --- |
| 2282 | `UnityEngine.MonoBehaviour::StopCoroutine` | `100770`，string 形式 |
| 2283 | `UnityEngine.MonoBehaviour::StopAllCoroutines` | `100D20` |
| 2292 | `UnityEngine.MonoBehaviour::StartCoroutineManaged` | `102520`，string Start |
| 2293 | `UnityEngine.MonoBehaviour::StartCoroutineManaged2` | `1029F0`，IEnumerator Start |
| 2294 | `UnityEngine.MonoBehaviour::StopCoroutineManaged` | `102C60`，Coroutine handle |
| 2295 | `UnityEngine.MonoBehaviour::StopCoroutineFromEnumeratorManaged` | `102E70` |
| 2237 | `UnityEngine.Coroutine::ReleaseCoroutine` | `F9AE0`，首条 E9 到 `78F770` |

静态能确认这一固定注册映射。实际运行时 `il2cpp_resolve_icall` 得到的地址、缓存值和模块归属仍须后续核验；本稿没有调用 resolver。

## E2 → F2：可以关联的实际节点身份

IEnumerator Start binding `1029F0` 在 `102B12` 调用引擎 `[792240,7929BF)`：RCX 为 native MonoBehaviour，RDX 为实际 managed IEnumerator，R8 为零，R9 为 native node 输出槽。引擎分配 `0x88` bytes 节点，保存 enum 引用、owner、方法信息并插入 owner 的双链；**在 Start 返回前即于 `7924BA` 调用 `78F840` 执行第一次协程窗口**。

binding 返回后，`102B53` 为 managed Coroutine wrapper 建立节点侧引用，`102B5B` 的 `48897B10` 把 native node 写到 managed Coroutine `m_Ptr(+10)`。因此最有用的身份关系为：实际 tracker 的 receiver/Enumerator/Coroutine + owner/slot/generation ↔ native MonoBehaviour ↔ 本次分配的 native node。节点地址本身会被复用，必须再带本地观察代次，不能用裸地址当永久身份。

| 字段 | 已核实的机器码事实与边界 |
| --- | --- |
| native node `+0/+8` | owner intrusive list 的相邻指针；owner 的 sentinel 位于 `+70`，`+78` 为一端节点 |
| node `+10/+18/+20` | script reference 的 handle/type/cache。构造 helper `775AD0` 先置 type=2，再取得 handle；type=2 时 `+20` 保存 actual managed reference。**`+10` 不是直接 IEnumerator 地址** |
| node `+28` | 关联 managed Coroutine wrapper 的 script reference；释放 wrapper 不等于停止调度 |
| node `+58` | native MonoBehaviour owner；Start 在 `79245C` 保存，取消节点入口在 `793883` 清零 |
| node `+60` | 引用计数；执行入口、调度登记、嵌套关系会持有它，不能直接称作“正在执行数” |
| node `+64` | 最后引用清理分支写 1；不能只读该 byte 就发完成凭据 |
| node `+68/+70` | native nested coroutine 关系，最终清理会拆开并递归释放 |
| node `+78` | 额外 continuation/wait 记录；清理中注销其字段和相关引用，不能忽略 |

实施时应在 native 侧捕获上述实际输入/输出与节点发布，保留原实际参数并仅把已复制的标量交到 CLR。Start 内同步首 MoveNext 早于 wrapper 返回，映射必须支持 pending native node，不能等到托管 CompleteStart 才假定所有早期执行已观测。

## E3 → F3：摘链与最后清理之间确有执行延迟

三个实际 Stop 路径汇合到节点入口 `[793840,793A5B)`：

- StopAll binding 在 `100E10` 取 `RCX=[nativeOwner+78]`，`100E14` 调用 `793840`，循环到 owner sentinel 空。
- handle binding 在 `102E19` 令 RCX=实际 `Coroutine.m_Ptr`，`102E1C` 调用 `793840`；之前还清除关联调度树项。
- enum binding `102F50` 调用 `[793A60,793C6D)`，按 owner ID 及实际 enum（包括 nested 关系）匹配调度项，再扫描 owner list，`793C56` 把匹配 node 传给 `793840`。

`793840` 在 `793857..79386C` 互写 prev/next 并将 node `+0/+8` 清零；`793883` 清 owner `+58`。它还匹配主或 nested continuation 的 scheduler 记录，调用 `[447AD0,447C26)` 删除。该函数在 `447B33/447BF5` 真正调用树 erase，再在 `447C16..447C19` 执行记录的 cleanup callback（RCX=记录 payload）。

继续登记函数 `[78FA30,78FC47)` 在 `78FB92` 增加 node 引用；`78FBE0` 把 node 放入 payload，`78FBEA` 取 callback `78F5E0`，`78FBF6` 取 cleanup `78F680`，`78FC23` 调用登记函数 `44D630`。回调 `78F5E0` 先核 native owner 与 node `+58` 相同才尾跳执行函数；owner 已清时不会继续其正常执行分支。

执行函数 `[78F840,78FA2E)` 在 `78F852`（`FF4160`）先加 node 引用，再调用实际脚本执行路径。结束时 `78F988`（以及 nested 收尾 `78F9F7`）调用 `78F680` 释放执行引用。`78F680` 的行为是关键：

1. `78F686`（`FF4960`）减引用；`78F690` 只有 `<=0` 才进最后清理；仍大于零就直接返回 `EAX=1`。所以 scheduler 项已删除不表示正执行的 MoveNext 已退出。
2. 最后分支 `78F69D` 以后写 `+64=1`、拆 nested、再次确保 owner list 摘除、清 continuation 记录。
3. `78F715..78F727` 释放 enum script handle并清 `+10/+18`。若无 managed Coroutine wrapper 引用，`78F739` 释放 `0x88` bytes native node；否则清 wrapper handle并留待 `ReleaseCoroutine` 清理。
4. 正常最后分支返回 `EAX=0`。original 可能已经释放 node，**After 不能再次解引用同一裸 node**。原始异常、负计数/失配或观察故障必须保 Unknown，不伪造 AL=0。

`ReleaseCoroutine` 的真实 `[78F770,78F832)` 另证：引用计数非零时只清 wrapper 关联，不撤销 scheduler/执行登记；计数为零才可释放节点。因此它适合诊断寿命，不能替取消完成事件。Start 首次执行后的 `[7924CD,79259A)` 还存在内联最终清理，故只挂 `78F680` 不是全生命周期闭包；这一路需随 Start 登记一并处理。

## 可以直接开始的实现顺序

1. **先建实际 native node registry。** 在 native Start `[792240,7929BF)` 建 pending 代次，关联 actual native owner、actual IEnumerator 和输出 node，完成后核 managed Coroutine `m_Ptr`。同时观察 `78F840` 的实际执行窗口，解决同步第一次 Move、native nested 和地址复用；所有 scalar record 先登记再读对象，Unknown 不授完成。先做自有 shape fixture验证 `4ptr→u8` 原参数/返回、输出槽和原异常，再申请固定引擎入口的安全发布合同。
2. **选择 `793840` + `78F680` 为下一对取消入口。** 前者 native ABI 是 `void(node*)`，只标记 exact request 对应 node 的取消/摘链原因；后者是 `u8(node*)`，保留原始返回字节并区分 pending release 与最后清理。`78F840` 也是返回字节且带第二指针的调用，不能拿现有 void0 operation 错配，也不追加 IL2CPP hidden MethodInfo。release 的 Before 应保留合法正引用状态；After 只用 native 保存的分支/结果，绝不回读可能已 free 的 node。
3. **只对 exact 代次形成局部完成候选。** 必须已观察 Stop 原因和节点关联，确认 scheduler 清除/owner detach、实际执行窗口全 drain、最后引用清理正常完成，且无 missing/foreign/overflow/异常责任、未发生新登记或代次替换。先以 native receipt保存这些事实，再设计 EngineCancelled 的逐记录转换；没有上述动态证据时仍 false。即使单节点完成，也不能一口气解除整个 launch 的 Pending、future producer 或全 run 资源退休。

本轮只给出有固定机器码依据的实施入口，没有发布这些 hook。尚未枚举所有 scheduler registry、所有 yield/continuation 实现、native node 的全部写者或所有销毁路径；未验证真实 icall 缓存/调用、IL2CPP GC、所有合法 PC 异常展开、并发首发和引擎停止后的 future Start 封口。它比“再观察一次 Stop 返回”推进到了实际调度/执行责任，但仍是静态前提，不是取消完成验收。
